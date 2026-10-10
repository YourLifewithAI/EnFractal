using System;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using EnFractal.Native.Kernel;
using EnFractal.Native.Look;

namespace EnFractal.Native;

/// <summary>
/// The player's controls for the Gubble (RUN-2-GLOW.md section 4; the magic design's "The keys", step 2). Every order and cast is
/// the player's own command through the host, carried out by the Gubble, on the one command path the AI also uses:
/// - the ask button (right-click): a tap is the smart ask at the aim, the target frozen at the press; a hold is the wheel;
/// - 1 to 5 the ability slots by category (1 Glow: at the aimed spot when it is dark and within reach, else on the Gubble);
/// - Q come, then follow (the recall); X stop (goal.stop, which ends its glows too).
/// The Gubble's thought bubble answers at once with the icon, "done" only on the host's result, and a refusal with the host's
/// reason (GubbleCue). The dusk moment teaches Glow the first time it gets dark (DuskMoment); the profile keeps has_used_glow.
/// </summary>
public partial class RoomHud
{
    /// <summary>How far the aim reaches.</summary>
    public const float AimRangeM = 30f;
    /// <summary>The companion's collision layer (CompanionAvatar): the aim's ray meets the Gubble's body.</summary>
    public const uint GubbleLayer = 4;
    /// <summary>A ray passing this close to the Gubble's middle, in body radii, is aimed at the Gubble (it is small).</summary>
    public const float GubbleAimRadii = 1.6f;

    public GubbleWheel Wheel => _wheel;
    public GubbleCue Cue => _cue;
    /// <summary>The reticle's tag: what a tap of the ask button would do now, before the press.</summary>
    public Label AskTag => _askTag;
    public Label DuskHintLabel => _duskHint;
    public DuskMoment Dusk => _dusk;
    /// <summary>What a tap would ask now (the reticle shows it).</summary>
    public GubbleMagic.Ask AskPreview { get; private set; }
    /// <summary>What the ask button's press froze (its aim), while the button is held.</summary>
    public GubbleMagic.Ask? AskFrozen { get; private set; }
    /// <summary>Whether the player has ever cast Glow (the profile's has_used_glow): the dusk moment then never shows.</summary>
    public bool HasUsedGlow { get; private set; }
    /// <summary>Test seam: the aim's ray (from, to) instead of the view's.</summary>
    internal (Vector3 From, Vector3 To)? AimRayForTests { get; set; }
    /// <summary>Test seam: the light level at a spot instead of the look's LightLevelAt (null: no reading).</summary>
    internal Func<Vector3, float?>? LightForTests { get; set; }

    private GubbleWheel _wheel = null!;
    private GubbleCue _cue = null!;
    private Label _askTag = null!;
    private Label _duskHint = null!;
    private DuskMoment _dusk = new();
    private double _askAge = double.MaxValue;
    private int? _recallSerial;
    private (int Serial, Vector3 Point)? _lookAfter;
    private string? _fetchJob;
    private CommandHost? _listening;
    private bool _goalFailed;

    /// <summary>The wheel, the reticle's tag, the dusk hint and the Gubble's thought bubble.</summary>
    private void BuildGubbleControls(Theme compactTheme)
    {
        // The HUD runs while the wheel has the game paused (it closes the wheel); everything else waits (GubblePaused).
        ProcessMode = ProcessModeEnum.Always;
        _askTag = new Label { Name = "AskTag", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Theme = compactTheme };
        AddChild(_askTag);
        _duskHint = new Label { Name = "DuskHint", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Theme = compactTheme };
        _duskHint.AddThemeFontSizeOverride("font_size", 20);
        AddChild(_duskHint);
        _keyTexts.Add((words => _duskHint.Text = words, () => string.Format(GubbleMagic.DuskHint, K(Act.GubbleSlot1), K(Act.GubbleAsk))));
        _wheel = new GubbleWheel();
        AddChild(_wheel);
        _wheel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _cue = new GubbleCue { Companion = Companion };
        AddChild(_cue);
    }

    /// <summary>The ability the island has in a slot (by its category), or null: the slot shows "?" and shrugs.</summary>
    public IslandAbility? SlotAbility(int slot) =>
        slot is < 0 or >= 5 ? null : Host?.Rules?.Abilities.Where(a => a.Category == GubbleMagic.SlotCategories[slot]).OrderBy(a => a.Capability, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>A slot's name: the island's display_name for it, else the engine's.</summary>
    private string SlotName(int slot) => SlotAbility(slot) is { } ability ? KernelJson.DisplayText(ability.DisplayName, 24) : GubbleMagic.SlotNames[slot];

    /// <summary>Whether the tree is paused by the wheel: then only the wheel's input and clock run.</summary>
    private bool GubblePaused => IsInsideTree() && GetTree().Paused;

    // ---- the aim ----

    /// <summary>The light level at a spot: the look's estimate (0 dark to 1 bright), or null without a look.</summary>
    public float? LightAt(Vector3 position) => LightForTests != null ? LightForTests(position) : Look?.LightLevelAt(position);

    /// <summary>Whether a spot is dark (below the look's DarkThreshold) and within reach of the Gubble's light ability.</summary>
    public bool DarkWithinReach(Vector3 point) =>
        SlotAbility(0) is { } light && light.Targets.Contains("point") && Companion != null && IsInstanceValid(Companion) &&
        Companion.GlobalPosition.DistanceTo(point) <= light.ReachM && LightAt(point) is { } level && level < LookDirector.DarkThreshold;

    /// <summary>The reticle on screen: the middle while the mouse is captured (F1, F2), else the pointer.</summary>
    private Vector2 AimScreenPoint()
    {
        var viewport = GetViewport();
        return Input.MouseMode == Input.MouseModeEnum.Captured ? viewport.GetVisibleRect().GetCenter() : viewport.GetMousePosition();
    }

    /// <summary>
    /// What the aim is on now: the ray from the view through the reticle meets the world, the room's things and the Gubble (the
    /// player's own body never). A thing is named by its id, never its name.
    /// </summary>
    public GubbleMagic.Aim AimNow()
    {
        Vector3 from, to;
        if (AimRayForTests is { } ray) (from, to) = ray;
        else
        {
            var camera = GetViewport().GetCamera3D();
            if (camera == null || !camera.IsInsideTree()) return default;
            var screen = AimScreenPoint();
            from = camera.ProjectRayOrigin(screen);
            to = from + camera.ProjectRayNormal(screen) * AimRangeM;
        }
        if (Player == null || !Player.IsInsideTree() || !from.IsFinite() || !to.IsFinite()) return default;
        using var query = PhysicsRayQueryParameters3D.Create(from, to, Room.RoomBuilder.WorldLayer | GubbleLayer);
        query.Exclude = new Godot.Collections.Array<Rid> { Player.GetRid() };
        using var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        var hitSomething = hit.Count > 0;
        var point = hitSomething ? hit["position"].AsVector3() : to;
        var collider = hitSomething ? hit["collider"].AsGodotObject() : null;
        var atGubble = collider != null && collider == Companion;
        // The Gubble is small: a ray that passes near its middle, before what it hit, is aimed at it.
        if (!atGubble && Companion != null && IsInstanceValid(Companion) && Companion.IsInsideTree())
        {
            var middle = Companion.GlobalPosition + Vector3.Up * (Companion.BodyHeightM * 0.5f);
            var along = (to - from).Normalized();
            var t = (middle - from).Dot(along);
            var nearest = from + along * t;
            atGubble = t > 0 && t <= from.DistanceTo(point) + 0.01f && nearest.DistanceTo(middle) <= Companion.BodyRadiusM * GubbleAimRadii;
        }
        string? entity = null;
        for (var node = collider as Node; node != null && entity == null; node = node.GetParent())
            if (node.HasMeta("entity_id") && node.GetMeta("entity_id").AsString() is { } id && id.StartsWith("obj:", StringComparison.Ordinal)) entity = id;
        return new GubbleMagic.Aim(hitSomething || atGubble, atGubble && !hitSomething ? Companion!.GlobalPosition : point, atGubble ? null : entity, atGubble);
    }

    /// <summary>Whether a room thing is one the Gubble could carry: movable, within its carry limit, held by no one (the host decides the rest).</summary>
    private bool Carryable(string id)
    {
        var host = Host;
        if (host == null || host.HeldBy(CommandHost.PlayerAvatar) == id || host.HeldBy(CommandHost.CompanionAvatarId) == id) return false;
        var item = host.Room.Objects.FirstOrDefault(o => o.Id == id);
        return item != null && item.Asset.Movable && item.Asset.MassKg <= Sandbox.SandboxRules.CarryLimitKg(CommandHost.CompanionAvatarId);
    }

    /// <summary>The smart ask for an aim (GubbleMagic.Decide).</summary>
    public GubbleMagic.Ask DecideAsk(GubbleMagic.Aim aim) => GubbleMagic.Decide(aim, Carryable, DarkWithinReach);

    /// <summary>The reticle's tag, asked at the focus's pace: what a tap would do, beside the reticle.</summary>
    private void UpdateAskTag(double delta)
    {
        _askAge += delta;
        if (Host == null || Customizing || _wheel.Held)
        {
            _askTag.Visible = false;
            return;
        }
        if (_askAge >= FocusQueryS)
        {
            _askAge = 0;
            AskPreview = DecideAsk(AimNow());
            var words = GubbleMagic.AskWords(AskPreview.Rule, Companion.CurrentIntent == "follow", SlotName(0));
            _askTag.Text = words.Length == 0 ? "" : $"{K(Act.GubbleAsk)}: {words}";
        }
        _askTag.Visible = _askTag.Text.Length > 0;
        if (!_askTag.Visible) return;
        var size = _askTag.GetCombinedMinimumSize();
        var screen = GetViewport().GetVisibleRect().Size;
        var at = AimScreenPoint() + new Vector2(16, 12);
        _askTag.Position = new Vector2(Mathf.Clamp(at.X, 0, Mathf.Max(0, screen.X - size.X)), Mathf.Clamp(at.Y, 0, Mathf.Max(0, screen.Y - size.Y)));
    }

    // ---- the ask button, the keys ----

    /// <summary>The ask button went down: the aim freezes now, and the hold (the wheel) starts.</summary>
    private void BeginAsk()
    {
        AskFrozen = DecideAsk(AimNow());
        _wheel.SetWedges(slot => SlotAbility(slot) is { } ability ? KernelJson.DisplayText(ability.DisplayName, 24) : null,
            wedge => wedge.Kind == GubbleMagic.WedgeKind.Come ? $"{wedge.Icon}\n{wedge.Name} ({K(Act.GubbleRecall)})" : $"{wedge.Icon}\n{wedge.Name}");
        _wheel.Begin(AimScreenPoint());
    }

    /// <summary>The ask button let go: a tap asks what fits the frozen aim; a wedge does what it says; the centre cancels.</summary>
    private void EndAsk()
    {
        var frozen = AskFrozen ?? default;
        AskFrozen = null;
        switch (_wheel.End(out var wedge))
        {
            case GubbleWheel.Outcome.Tap: Ask(frozen); break;
            case GubbleWheel.Outcome.Wedge: ChooseWedge(wedge, frozen.Aim); break;
        }
    }

    /// <summary>The wheel closes with nothing asked (Esc, the appearance panel, the window losing focus): the game resumes.</summary>
    private void CancelAsk()
    {
        _wheel.Close();
        AskFrozen = null;
    }

    public override void _Notification(int what)
    {
        // A release the window never sees (it lost focus) must not leave the game paused under the wheel.
        if (what == NotificationApplicationFocusOut && _wheel != null && _wheel.Held) CancelAsk();
    }

    /// <summary>The smart ask's rule, carried out.</summary>
    public void Ask(GubbleMagic.Ask ask)
    {
        switch (ask.Rule)
        {
            case GubbleMagic.AskRule.Toggle: Order(Companion.CurrentIntent == "follow" ? "stay" : "follow"); break;
            case GubbleMagic.AskRule.Fetch: Fetch(ask.Target); break;
            case GubbleMagic.AskRule.Glow: CastSlot(0, ask.Aim); break;
            case GubbleMagic.AskRule.Look: GoAndLook(ask.Point); break;
            default: _cue.Shrug(GubbleMagic.UnknownIcon, GubbleMagic.NothingThere); break;
        }
    }

    /// <summary>A wedge of the wheel, chosen with the aim frozen at the press.</summary>
    public void ChooseWedge(int index, GubbleMagic.Aim aim)
    {
        if (index < 0 || index >= GubbleMagic.Wedges.Length) return;
        var wedge = GubbleMagic.Wedges[index];
        switch (wedge.Kind)
        {
            case GubbleMagic.WedgeKind.Ability: CastSlot(wedge.Slot, aim); break;
            case GubbleMagic.WedgeKind.Come: Recall(); break;
            case GubbleMagic.WedgeKind.Stay: Order("stay"); break;
            case GubbleMagic.WedgeKind.Fetch:
                if (aim.Entity != null) Fetch(aim.Entity);
                else _cue.Shrug(GubbleMagic.FetchIcon, GubbleMagic.NothingToFetch);
                break;
        }
    }

    /// <summary>
    /// An ability slot (0-based): the island's ability in that category, cast by the player and carried out by the Gubble, at the
    /// aimed spot when it is dark and within reach, else on the Gubble itself. A slot the island has no ability for shrugs.
    /// </summary>
    public void CastSlot(int slot, GubbleMagic.Aim aim)
    {
        var host = Host;
        var icon = GubbleMagic.SlotIcons[slot];
        if (host == null) { _noticeText = $"The command host is not attached; {Companion.CompanionName}'s keys are off."; return; }
        if (SlotAbility(slot) is not { } ability)
        {
            _cue.Shrug(icon);
            return;
        }
        _cue.Think(icon);
        Vector3? point = slot == 0 ? (aim.Hit && !aim.AtGubble && DarkWithinReach(aim.Point) ? aim.Point : null)
            : aim.Hit && !aim.AtGubble && ability.Targets.Contains("point") ? aim.Point : null;
        if (point == null && !ability.Targets.Contains("self") && aim.Hit) point = aim.Point;
        var result = host.PlayerEffect(ability.Capability, point);
        if (Answer(result) && ability.Category == "light") UsedGlow();
    }

    /// <summary>A goal order (follow, stay, stop) as the player's command.</summary>
    private void Order(string goal, Vector3? point = null, bool keepChains = false)
    {
        var host = Host;
        if (host == null) { _noticeText = $"The command host is not attached; {Companion.CompanionName}'s keys are off."; return; }
        if (!keepChains) ClearChains();
        _cue.Think(goal switch { "follow" => GubbleMagic.FollowIcon, "stay" => GubbleMagic.StayIcon, "stop" => GubbleMagic.StopIcon, "come" => GubbleMagic.ComeIcon, _ => GubbleMagic.LookIcon });
        Answer(host.PlayerGoal(goal, point));
    }

    /// <summary>Q: come, then follow. The follow is sent when the come arrives beside the player; a newer order cancels it.</summary>
    public void Recall()
    {
        var host = Host;
        if (host == null) { _noticeText = $"The command host is not attached; {Companion.CompanionName}'s keys are off."; return; }
        ClearChains();
        _cue.Think(GubbleMagic.ComeIcon);
        var result = host.PlayerGoal("come");
        if (!result["ok"]!.GetValue<bool>()) { Answer(result); return; }
        _recallSerial = Companion.IntentSerial;
    }

    /// <summary>X: stop the Gubble's goal and its effects (goal.stop ends its glows).</summary>
    public void StopGubble() => Order("stop");

    /// <summary>Go to the aimed spot, then look (point) at it there: the old point order, aimed.</summary>
    private void GoAndLook(Vector3 point)
    {
        var host = Host;
        if (host == null) { _noticeText = $"The command host is not attached; {Companion.CompanionName}'s keys are off."; return; }
        ClearChains();
        _cue.Think(GubbleMagic.LookIcon);
        var result = host.PlayerGoal("go_to", point);
        if (!result["ok"]!.GetValue<bool>()) { Answer(result); return; }
        _goalFailed = false;
        _lookAfter = (Companion.IntentSerial, point);
    }

    /// <summary>Fetch a thing: goal.set fetch as the player; "done" when the host's job succeeds.</summary>
    private void Fetch(string? target)
    {
        var host = Host;
        if (host == null) { _noticeText = $"The command host is not attached; {Companion.CompanionName}'s keys are off."; return; }
        ClearChains();
        _cue.Think(GubbleMagic.FetchIcon);
        if (target == null) { _cue.Shrug(GubbleMagic.FetchIcon, GubbleMagic.NothingToFetch); return; }
        var command = new JsonObject
        {
            ["schema"] = "enfractal.command", ["version"] = 1, ["action_id"] = "hud-ask-" + Guid.NewGuid().ToString("N")[..24],
            ["room_id"] = host.Room.RoomId, ["op"] = "goal.set",
            ["args"] = new JsonObject { ["actor"] = CommandHost.CompanionAvatarId, ["goal"] = "fetch", ["target"] = target },
        };
        var result = host.HandleObject(CanonicalJson.Text(command), CommandHost.PlayerPrincipal);
        if (!result["ok"]!.GetValue<bool>()) { Answer(result); return; }
        _fetchJob = result["job_id"]?.GetValue<string>();
        if (_fetchJob == null) _cue.Done();
    }

    /// <summary>The host's result on the bubble: done, or a head-shake with the host's reason. True when it was done.</summary>
    private bool Answer(JsonObject result)
    {
        if (result["ok"]!.GetValue<bool>()) { _cue.Done(); return true; }
        var reason = result["error"]?["message"]?.GetValue<string>();
        _cue.Refuse(reason);
        if (reason != null) _noticeText = reason;
        return false;
    }

    private void ClearChains()
    {
        _recallSerial = null;
        _lookAfter = null;
        _fetchJob = null;
    }

    /// <summary>A goal's job ended (the host's event): a fetch's result, or a walk that could not arrive.</summary>
    private void OnGoalFinished(string actor, string jobId, string state)
    {
        if (actor != CommandHost.CompanionAvatarId) return;
        if (_fetchJob != null && jobId == _fetchJob)
        {
            _fetchJob = null;
            if (state == "succeeded") _cue.Done();
            else if (state == "failed")
            {
                // The host's own reason for the job, from its record.
                var status = Host?.HandleObject(CanonicalJson.Text(new JsonObject
                {
                    ["schema"] = "enfractal.query", ["version"] = 1, ["query_id"] = "hud-ask-" + Guid.NewGuid().ToString("N")[..24],
                    ["room_id"] = Host.Room.RoomId, ["op"] = "jobs.status", ["args"] = new JsonObject { ["job_id"] = jobId },
                }), CommandHost.PlayerPrincipal);
                _cue.Refuse(status?["data"]?["result"]?["error"]?["message"]?.GetValue<string>());
            }
            else _cue.Clear();
        }
        else if (_lookAfter != null && state == "failed") _goalFailed = true;
    }

    /// <summary>Each frame: the recall's follow and the look after a walk, when their walks arrive.</summary>
    private void UpdateChains()
    {
        if (_listening == null && Host is { } host)
        {
            _listening = host;
            host.GoalFinished += OnGoalFinished;
        }
        if (Companion == null || !IsInstanceValid(Companion)) return;
        if (_recallSerial is { } serial)
        {
            if (Companion.ComeArrivedSerial == serial)
            {
                _recallSerial = null;
                Order("follow", keepChains: true);
            }
            else if (Companion.IntentSerial != serial) _recallSerial = null;
        }
        if (_lookAfter is { } look)
        {
            if (Companion.GoToArrivedSerial == look.Serial)
            {
                _lookAfter = null;
                Order("point_at", look.Point, keepChains: true);
            }
            else if (_goalFailed)
            {
                _lookAfter = null;
                _goalFailed = false;
                _cue.Refuse(GubbleMagic.CannotGetThere);
            }
            else if (Companion.IntentSerial != look.Serial) _lookAfter = null;
        }
    }

    // ---- the dusk moment ----

    /// <summary>Test seam: the dusk moment as at the start of a session (the profile's has_used_glow is kept).</summary>
    internal void ResetDuskForTests()
    {
        _dusk = new DuskMoment();
        _cue.Dim(false);
        _duskHint.Visible = false;
    }

    private void UpdateDusk(double delta)
    {
        var step = _dusk.Update(delta, () => Player != null && Player.IsInsideTree() ? LightAt(Player.GlobalPosition) : null, LookDirector.DarkThreshold, HasUsedGlow);
        if (step == DuskMoment.Step.Show)
        {
            _cue.Shiver();
            _cue.Dim(true);
            _cue.Think(GubbleMagic.SlotIcons[0]);
        }
        else if (step == DuskMoment.Step.Hide)
        {
            _cue.Dim(false);
            if (_cue.State == "thinking" && _cue.Icon == GubbleMagic.SlotIcons[0]) _cue.Clear();
        }
        _duskHint.Visible = _dusk.Showing && PlaceBeside(_duskHint, Companion);
    }

    /// <summary>A label beside a body on screen; false when the body is behind the view.</summary>
    private bool PlaceBeside(Label label, Node3D body)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera == null || body == null || !IsInstanceValid(body) || !body.IsInsideTree()) return false;
        var at3 = body.GlobalPosition + Vector3.Up * 0.1f;
        if (camera.IsPositionBehind(at3)) return false;
        var size = label.GetCombinedMinimumSize();
        var screen = GetViewport().GetVisibleRect().Size;
        var at = camera.UnprojectPosition(at3) + new Vector2(24, -size.Y * 0.5f);
        label.Position = new Vector2(Mathf.Clamp(at.X, 0, Mathf.Max(0, screen.X - size.X)), Mathf.Clamp(at.Y, 0, Mathf.Max(0, screen.Y - size.Y)));
        return true;
    }

    /// <summary>Glow was cast: the profile remembers, and the dusk moment is over for good.</summary>
    private void UsedGlow()
    {
        if (HasUsedGlow) return;
        HasUsedGlow = true;
        _cue.Dim(false);
        _duskHint.Visible = false;
        SaveProfileValue("has_used_glow", true);
    }

    // ---- the aura ----

    /// <summary>The Gubble's colour is its aura (the family's note): its glow takes it. Glows already lit take the new colour at once.</summary>
    private void ApplyAura()
    {
        Companion.SetAuraColor(Palette[_companionColor]);
        RecolourGlows();
    }

    /// <summary>
    /// The look reads the aura when a glow starts; a colour change gives every running glow the colour a restart would (the light,
    /// its halo and a wisp's heart, as GlowLook.LightColor makes it), in place, so a wisp keeps its spot and its effect id.
    /// </summary>
    public int RecolourGlows()
    {
        if (Look == null || Host == null) return 0;
        var colour = GlowLook.LightColor(Companion.AuraColor);
        var count = 0;
        foreach (var id in Host.ActiveEffectIds)
        {
            if (Look.GlowLight(id) is not { } light || Look.GlowRoot(id) is not { } root) continue;
            light.LightColor = colour;
            if (root.GetNodeOrNull<MeshInstance3D>("Halo")?.MaterialOverride is StandardMaterial3D halo) halo.AlbedoColor = new Color(colour.R, colour.G, colour.B, halo.AlbedoColor.A);
            if (root.GetNodeOrNull<MeshInstance3D>("Core")?.MaterialOverride is StandardMaterial3D core) core.Emission = colour;
            count++;
        }
        return count;
    }
}
