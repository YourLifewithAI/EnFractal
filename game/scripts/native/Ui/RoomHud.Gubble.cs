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
/// - 1 to 5 the ability slots by category (1 Glow: where the cursor points, the Gubble coming closer first when the spot is out
///   of its reach or sight; pointed at nothing, on the Gubble);
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
    /// <summary>Coming closer to cast, the Gubble stops and casts once the spot is this much inside its reach (and in sight).</summary>
    public const float ApproachMarginM = 0.15f;

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
    /// <summary>A cast waiting for the Gubble to come closer: the walk's serial, the slot, the ability and the spot.</summary>
    private (int Serial, int Slot, string Capability, Vector3 Point)? _castAfter;
    private double _castCheckAge;
    /// <summary>A put-down waiting for the Gubble to get there: the walk's serial and the spot.</summary>
    private (int Serial, Vector3 Point)? _putAfter;
    private double _putCheckAge;
    /// <summary>A put-down waiting for the Gubble to get within reach of its spot (tests).</summary>
    public bool PutWaiting => _putAfter != null;
    /// <summary>Whether the Gubble holds something now (the host's word).</summary>
    public bool GubbleHolds => Host?.HeldBy(CommandHost.CompanionAvatarId) != null;
    /// <summary>A cast waiting for the Gubble to come within reach and sight of its spot (tests).</summary>
    public bool CastWaiting => _castAfter != null;
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

    /// <summary>
    /// The help's line for the ability keys, from the island's pack: the slots it has, cast where you point, and the keys of the
    /// slots it lacks, still to come ("1 Glow, 3 Bubbles, 4 Fireworks where you point ... · 2/5 magic still to come").
    /// </summary>
    private string MagicKeysLine()
    {
        var known = Enumerable.Range(0, Act.GubbleSlots.Length).Where(i => SlotAbility(i) != null).Select(i => $"{K(Act.GubbleSlots[i])} {SlotName(i)}").ToArray();
        var later = Enumerable.Range(0, Act.GubbleSlots.Length).Where(i => SlotAbility(i) == null).Select(i => K(Act.GubbleSlots[i])).ToArray();
        var parts = new System.Collections.Generic.List<string>();
        if (known.Length > 0) parts.Add($"{string.Join(", ", known)} where you point (it comes closer first if it must; at the sky, on itself)");
        if (later.Length > 0) parts.Add($"{string.Join("/", later)} magic still to come");
        parts.Add("it floats after you, over water and up cliffs");
        return string.Join(" · ", parts);
    }

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

    /// <summary>The aim on screen: the cursor in every view (it is always free); while a drag hides it, where it was pressed.</summary>
    public Vector2 AimScreenPoint() => _drag.Dragging ? _drag.Anchor : GetViewport().GetMousePosition();

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
    public GubbleMagic.Ask DecideAsk(GubbleMagic.Aim aim) => GubbleMagic.Decide(aim, Carryable, DarkWithinReach, GubbleHolds);

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
        EndDrag();
        AskFrozen = DecideAsk(AimNow());
        _wheel.SetOwner($"{Companion.NameTag}'s magic", Companion.AuraColor ?? Companion.AppearanceColor);
        _wheel.SetWedges(slot => SlotAbility(slot) is { } ability ? KernelJson.DisplayText(ability.DisplayName, 24) : null,
            wedge => wedge.Kind switch
            {
                GubbleMagic.WedgeKind.Come => $"{wedge.Icon}\n{wedge.Name} ({K(Act.GubbleRecall)})",
                // While the Gubble holds something, Fetch is Put down (at the spot the press froze, or here).
                GubbleMagic.WedgeKind.Fetch when GubbleHolds => $"{GubbleMagic.PutDownIcon}\n{GubbleMagic.PutDownName}",
                _ => $"{wedge.Icon}\n{wedge.Name}",
            });
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
        // A release the window never sees (it lost focus) must not leave the game paused under the wheel, or a drag held.
        if (what == NotificationApplicationFocusOut && _wheel != null && _wheel.Held) CancelAsk();
        if (what == NotificationApplicationFocusOut) EndDrag();
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
            case GubbleMagic.AskRule.PutHere: PutDown(null); break;
            case GubbleMagic.AskRule.PutThere: PutDown(ask.Point); break;
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
            case GubbleMagic.WedgeKind.Fetch when GubbleHolds:
                PutDown(aim.Hit && !aim.AtGubble ? aim.Point : null);
                break;
            case GubbleMagic.WedgeKind.Fetch:
                if (aim.Entity != null) Fetch(aim.Entity);
                else _cue.Shrug(GubbleMagic.FetchIcon, GubbleMagic.NothingToFetch);
                break;
        }
    }

    /// <summary>
    /// An ability slot (0-based): the island's ability in that category, cast by the player and carried out by the Gubble, where
    /// the player points, dark or not (the Glow playtest, 9 October); pointed at nothing (the sky, past the aim's range) or at the
    /// Gubble, on the Gubble itself. A spot beyond the ability's reach or out of the team's sight sends the Gubble closer first,
    /// then it casts (UpdateChains). The host still checks reach and sight on the cast itself. A slot the island has no ability
    /// for shrugs.
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
        ClearChains();
        _cue.Think(icon);
        Vector3? point = aim.Hit && !aim.AtGubble && ability.Targets.Contains("point") ? aim.Point : null;
        if (point == null && !ability.Targets.Contains("self") && aim.Hit) point = aim.Point;
        if (point is { } spot && host.EffectSpot(ability.Capability, spot) is { } place && (!place.InReach || !place.InSight))
        {
            ComeCloserAndCast(slot, ability, spot);
            return;
        }
        Cast(slot, ability, point);
    }

    /// <summary>The cast itself, as the player's command; done, the Gubble tosses its light toward the spot (or lifts, on itself).</summary>
    private void Cast(int slot, IslandAbility ability, Vector3? point)
    {
        var host = Host;
        if (host == null) return;
        _cue.Think(GubbleMagic.SlotIcons[slot]);
        var result = host.PlayerEffect(ability.Capability, point);
        if (!Answer(result, wiggle: false)) return;
        // The look's spark leaves the Gubble now (StartGlow, inside the cast): the gesture starts with it.
        Companion.CastGesture(point, ability.Category);
        if (ability.Category == "light") UsedGlow();
    }

    /// <summary>
    /// The spot is beyond the Gubble's reach or out of sight: it goes there (a go_to, as the go-and-look does), and on the way,
    /// as soon as the spot is within its reach and in sight, it stops and casts. A walk that cannot get there says so.
    /// </summary>
    private void ComeCloserAndCast(int slot, IslandAbility ability, Vector3 spot)
    {
        var host = Host!;
        var result = host.PlayerGoal("go_to", spot);
        if (!result["ok"]!.GetValue<bool>()) { Answer(result); return; }
        _goalFailed = false;
        _castCheckAge = 0;
        _castAfter = (Companion.IntentSerial, slot, ability.Capability, spot);
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
        var result = host.PlayerGoal("fetch", target: target);
        if (!result["ok"]!.GetValue<bool>()) { Answer(result); return; }
        _fetchJob = result["job_id"]?.GetValue<string>();
        if (_fetchJob == null) _cue.Done();
    }

    /// <summary>The host's result on the bubble: done (with a wiggle, unless the caller gestures), or a head-shake with the host's reason. True when it was done.</summary>
    private bool Answer(JsonObject result, bool wiggle = true)
    {
        if (result["ok"]!.GetValue<bool>()) { _cue.Done(wiggle); return true; }
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
        _castAfter = null;
        _putAfter = null;
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
        else if ((_lookAfter != null || _castAfter != null || _putAfter != null) && state == "failed") _goalFailed = true;
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
        UpdateCastAfter();
        UpdatePutAfter();
        // The bubble says what the Gubble holds while nothing else shows.
        _cue.Holding = Host?.HeldName(CommandHost.CompanionAvatarId) ?? "";
    }

    // ---- putting down what the Gubble holds ----

    /// <summary>
    /// Put down what the Gubble holds, as the player's command (entity.release through its avatar): here, in front of it (no spot),
    /// or at a spot. A spot out of its reach sends it there first (a go_to); on the way, as soon as the host says the put-down would
    /// work (a preview), it stops and puts it down. A spot the host refuses for another reason (outside the room) is refused at once.
    /// </summary>
    public void PutDown(Vector3? spot)
    {
        var host = Host;
        if (host == null) { _noticeText = $"The command host is not attached; {Companion.CompanionName}'s keys are off."; return; }
        ClearChains();
        if (!GubbleHolds) { _cue.Shrug(GubbleMagic.PutDownIcon, GubbleMagic.NothingHeld); return; }
        _cue.Think(GubbleMagic.PutDownIcon);
        if (spot is not { } at) { Answer(host.PlayerPutDown()); return; }
        var trial = host.PlayerPutDown(at, preview: true);
        if (trial["ok"]!.GetValue<bool>()) { Answer(host.PlayerPutDown(at)); return; }
        var error = trial["error"];
        var outOfReach = error?["code"]?.GetValue<string>() == "out_of_bounds" && error?["retryable"]?.GetValue<bool>() == true;
        if (!outOfReach) { Answer(trial); return; }
        var walk = host.PlayerGoal("go_to", at);
        if (!walk["ok"]!.GetValue<bool>()) { Answer(walk); return; }
        _goalFailed = false;
        _putCheckAge = 0;
        _putAfter = (Companion.IntentSerial, at);
    }

    /// <summary>The put-down waiting for the Gubble's walk: as UpdateCastAfter, asking the host's preview on the way.</summary>
    private void UpdatePutAfter()
    {
        if (_putAfter is not { } put || Host is not { } host) return;
        var arrived = Companion.GoToArrivedSerial == put.Serial;
        if (!arrived && _goalFailed)
        {
            _putAfter = null;
            _goalFailed = false;
            _cue.Refuse(host.LastGoalError(CommandHost.CompanionAvatarId) ?? GubbleMagic.CannotGetThere);
            return;
        }
        if (!arrived && Companion.IntentSerial != put.Serial)
        {
            _putAfter = null;
            return;
        }
        if (!arrived)
        {
            _putCheckAge += GetProcessDeltaTime();
            if (_putCheckAge < FocusQueryS) return;
            _putCheckAge = 0;
            if (!host.PlayerPutDown(put.Point, preview: true)["ok"]!.GetValue<bool>()) return;
        }
        _putAfter = null;
        if (!arrived) host.PlayerGoal("stay");
        _cue.Think(GubbleMagic.PutDownIcon);
        Answer(host.PlayerPutDown(put.Point));
    }

    /// <summary>
    /// The cast waiting for the Gubble to come closer: it casts as soon as the spot is ApproachMarginM inside its reach and in sight
    /// (checked at the focus's pace), stopping where it is, or when its walk arrives; a walk that fails says so with the host's
    /// reason; a newer order drops it.
    /// </summary>
    private void UpdateCastAfter()
    {
        if (_castAfter is not { } cast || Host is not { } host) return;
        var arrived = Companion.GoToArrivedSerial == cast.Serial;
        if (!arrived && _goalFailed)
        {
            _castAfter = null;
            _goalFailed = false;
            _cue.Refuse(host.LastGoalError(CommandHost.CompanionAvatarId) ?? GubbleMagic.CannotGetThere);
            return;
        }
        if (!arrived && Companion.IntentSerial != cast.Serial)
        {
            _castAfter = null;
            return;
        }
        if (!arrived)
        {
            _castCheckAge += GetProcessDeltaTime();
            if (_castCheckAge < FocusQueryS) return;
            _castCheckAge = 0;
            if (host.EffectSpot(cast.Capability, cast.Point, ApproachMarginM) is not { InReach: true, InSight: true }) return;
        }
        _castAfter = null;
        // Near enough on the way: it stops there (it stays, as after any walk) and casts.
        if (!arrived) host.PlayerGoal("stay");
        if (SlotAbility(cast.Slot) is { } ability && ability.Capability == cast.Capability) Cast(cast.Slot, ability, cast.Point);
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
    /// The look reads the aura when a glow starts; a colour change gives every running glow the colour a restart would, in place,
    /// so a wisp keeps its spot and its effect id. The look owns its nodes (LookDirector.RecolorGlows), so the HUD only asks.
    /// </summary>
    public int RecolourGlows() => Look?.RecolorGlows() ?? 0;
}
