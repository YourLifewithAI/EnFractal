using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnFractal.Native.Look;

namespace EnFractal.Native.Kernel;

/// <summary>
/// The island's abilities (Run 2, Glow; docs/runs/RUN-2-GLOW.md section 2). The island's rules pack (IslandRules) says
/// which abilities exist and their bounds; effect.start casts one, carried out by the Gubble, through the one command
/// path, as the player (the HUD's PlayerEffect) or as the companion. v1 has one primitive, light.emit: Glow, a real light
/// that follows the Gubble (targets: its own avatar) or floats at a spot within its reach and in the team's sight.
/// - Bounds: params, radius and duration inside the ability's; omitted params take the pack's defaults.
/// - Who: cast_by names the principals that may cast it; a companion may target only its own avatar, and no one any
///   other entity.
/// - Caps: at most max_active of an ability at once, and MaxActiveEffects in the room whatever the ability.
/// - The receipt is transient, with an opaque effect: id (130 random bits); the effect ends at its duration, on
///   effect.stop (one id or all), on goal.stop of the Gubble, when the rules change and when the room unloads.
/// Effects are never saved: they live with the session, like goals.
/// </summary>
public partial class CommandHost
{
    /// <summary>The island's rules, or null when none loaded: then there are no abilities, and effect.start says why.</summary>
    public IslandRules? Rules { get; private set; }
    /// <summary>Why the island has no abilities (the pack did not load), for the player; empty when the rules loaded.</summary>
    public string RulesNotice { get; private set; } = "";
    /// <summary>The look the host lights glows with (RoomWorld's); null in fixtures, where effects run with nothing drawn.</summary>
    public LookDirector? LookHook { get; set; }
    /// <summary>The engine's cap on active effects (MaxActiveEffects). Tests lower it.</summary>
    public int EffectLimit { get; internal set; } = MaxActiveEffects;

    /// <summary>One running effect. In memory only: never in a receipt's durable store, a snapshot or a save.</summary>
    private sealed class ActiveEffect
    {
        public required string Id { get; init; }
        public required string Capability { get; init; }
        /// <summary>The principal that cast it: the player may stop any effect, a companion only its own.</summary>
        public required string Principal { get; init; }
        public bool FollowsCompanion { get; init; }
        public DateTime Expires { get; init; }
    }

    /// <summary>The running effects, oldest first.</summary>
    private readonly List<ActiveEffect> _effects = new();

    /// <summary>The ids of the running effects, oldest first (tests, the HUD).</summary>
    public IReadOnlyList<string> ActiveEffectIds
    {
        get
        {
            ExpireEffects();
            return _effects.Select(e => e.Id).ToList();
        }
    }

    /// <summary>RoomWorld's one line: load the island's rules from res://rules/&lt;id&gt;/v&lt;N&gt;.json. A pack that fails leaves no abilities, never a crash.</summary>
    public CommandHost LoadRules(string rulesId, int rulesVersion)
    {
        IslandRules? rules = null;
        var notice = "";
        try { rules = IslandRules.Load(rulesId, rulesVersion); }
        catch (Exception error)
        {
            notice = $"The island's rules ({KernelJson.DisplayText(rulesId ?? "", 64)} v{rulesVersion}) did not load, so the Gubble has no abilities: {error.Message}";
        }
        UseRules(rules, notice);
        return this;
    }

    /// <summary>Use these rules (null: none, with notice saying why). Effects started under the old rules end.</summary>
    public void UseRules(IslandRules? rules, string notice = "")
    {
        EndEffects(_effects.ToList());
        Rules = rules;
        RulesNotice = rules == null ? (notice.Length > 0 ? notice : "This island has no rules, so the Gubble has no abilities.") : "";
        if (rules == null) GD.Print("COMMAND_HOST " + RulesNotice);
    }

    /// <summary>
    /// The HUD's ability keys: the player's effect.start, carried out by the Gubble, like PlayerGoal. No point is the
    /// Gubble's own glow (targets its avatar); a point is a light there. The area's radius and the duration are the
    /// pack's defaults, and so are the params (left out). Returns the result for the HUD to show.
    /// </summary>
    public JsonObject PlayerEffect(string capability, Vector3? point = null)
    {
        var ability = Rules?.Ability(capability);
        var centre = point ?? (Companion != null && IsInstanceValid(Companion) ? Companion.GlobalPosition : Vector3.Zero);
        var args = new JsonObject
        {
            // An ability the island lacks still goes through the host, which answers why (unsupported_capability).
            ["capability"] = capability, ["params"] = new JsonObject(),
            ["area"] = new JsonObject { ["center_m"] = KernelJson.Vector(centre), ["radius_m"] = ability?.AreaRadiusDefaultM ?? 0.5 },
            ["duration_s"] = ability?.DurationDefaultS ?? 1.0,
        };
        if (point == null) args["targets"] = new JsonArray(CompanionAvatarId);
        return PlayerCommand("effect.start", args);
    }

    /// <summary>An effect.start checked against the island's rules: what it would start.</summary>
    private sealed record EffectPlan(IslandAbility Ability, bool Self, Vector3 Centre, double RadiusM, double DurationS, SortedDictionary<string, double> Params);

    /// <summary>
    /// Checks an effect.start against the rules, the caster, the targets, the bounds, the place and the caps, in that
    /// order, and refuses with the contract's codes. Runs before any hold, so a held request is one that can start.
    /// </summary>
    private EffectPlan PlanEffect(JsonElement args, string principal)
    {
        ExpireEffects();
        if (Rules == null)
            throw new Refusal("unsupported_capability", "This island's rules did not load, so it grants no abilities.", "$.args.capability");
        var capability = Str(args, "capability")!;
        var ability = Rules.Ability(capability)
            ?? throw new Refusal("unsupported_capability", "This island has no ability by that name. Call capabilities.list.", "$.args.capability");
        var role = principal == PlayerPrincipal ? "player" : "companion";
        if (!ability.CastBy.Contains(role))
            throw new Refusal("permission_denied", principal == PlayerPrincipal ? "This island does not let the player cast that." : "This island does not let the companion cast that.", "$.args.capability");
        // Targets: the Gubble's own avatar (self, the light follows it) or none (point). Nothing else, from anyone.
        var targets = args.TryGetProperty("targets", out var named) ? named.EnumerateArray().Select(t => t.GetString()!).ToList() : new List<string>();
        var self = targets.Count > 0;
        if (self && (targets.Count != 1 || targets[0] != CompanionAvatarId))
            throw principal == PlayerPrincipal
                ? new Refusal("invalid_args", "This ability lights the Gubble or a spot; it cannot be cast on that.", "$.args.targets")
                : new Refusal("permission_denied", "A companion's ability may target only its own avatar.", "$.args.targets");
        if (self && !ability.Targets.Contains("self"))
            throw new Refusal("invalid_args", "This ability cannot be cast on the Gubble itself; name no targets and a spot.", "$.args.targets",
                allowed: new JsonArray(ability.Targets.OrderBy(t => t, StringComparer.Ordinal).Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()));
        if (!self && !ability.Targets.Contains("point"))
            throw new Refusal("invalid_args", "This ability lights only the Gubble itself; name its avatar in targets.", "$.args.targets",
                allowed: new JsonArray(ability.Targets.OrderBy(t => t, StringComparer.Ordinal).Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()));
        // Bounds: every param the ability's, within its range; omitted ones take the defaults.
        var parameters = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var parameter in args.GetProperty("params").EnumerateObject())
        {
            var path = "$.args.params." + parameter.Name;
            if (!ability.Params.TryGetValue(parameter.Name, out var range))
                throw new Refusal("invalid_args", "This ability takes no parameter by that name.", path,
                    allowed: new JsonArray(ability.Params.Keys.Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()));
            if (parameter.Value.ValueKind != JsonValueKind.Number) throw new Refusal("invalid_args", "This parameter is a number.", path);
            var value = CanonicalJson.ReadNumber(parameter.Value);
            if (value < range.Min || value > range.Max)
                throw new Refusal("invalid_args", "This parameter is out of the ability's range.", path, actual: JsonValue.Create(value), allowed: new JsonArray(range.Min, range.Max));
            parameters[parameter.Name] = value;
        }
        foreach (var (name, range) in ability.Params) parameters.TryAdd(name, range.Default);
        var area = args.GetProperty("area");
        var radius = CanonicalJson.ReadNumber(area.GetProperty("radius_m"));
        if (radius > ability.AreaRadiusMaxM)
            throw new Refusal("invalid_args", "The area is larger than the ability allows.", "$.args.area.radius_m", actual: JsonValue.Create(radius), allowed: JsonValue.Create(ability.AreaRadiusMaxM));
        var duration = CanonicalJson.ReadNumber(args.GetProperty("duration_s"));
        if (duration > ability.DurationMaxS)
            throw new Refusal("invalid_args", "That is longer than the ability lasts.", "$.args.duration_s", actual: JsonValue.Create(duration), allowed: JsonValue.Create(ability.DurationMaxS));
        // The Gubble carries every ability out.
        if (Companion == null || !IsInstanceValid(Companion) || !Companion.IsInsideTree())
            throw new Refusal("not_ready", "The Gubble is not in the room.", retryable: true);
        var centre = self ? Companion.GlobalPosition : KernelJson.ReadVector(area.GetProperty("center_m"));
        if (!self)
        {
            // A spot: inside the room, within the Gubble's reach, and in the team's sight now (where the wisp will float).
            if (Room.Sea == null && !Room.Bounds.Grow(0.001f).HasPoint(centre))
                throw new Refusal("out_of_bounds", "That spot is outside the room.", "$.args.area.center_m");
            var distance = Companion.GlobalPosition.DistanceTo(centre);
            if (distance > ability.ReachM)
                throw new Refusal("out_of_bounds", "That spot is beyond the Gubble's reach; it must come closer first.", "$.args.area.center_m", retryable: true,
                    actual: JsonValue.Create(Math.Round(distance, 3)), allowed: JsonValue.Create(ability.ReachM));
            if (!TeamSeesSpot(centre, principal))
                throw new Refusal("target_not_found", "Neither of you can see that spot now. Look there first.", "$.args.area.center_m");
        }
        // Caps: the ability's own, then the room's.
        var mine = _effects.Count(e => e.Capability == ability.Capability);
        if (mine >= ability.MaxActive)
            throw new Refusal("budget_exceeded", "As many of these are running as the island allows; stop one first.", "$.args.capability",
                actual: JsonValue.Create(mine), allowed: JsonValue.Create(ability.MaxActive));
        if (_effects.Count >= EffectLimit)
            throw new Refusal("budget_exceeded", "As many effects are running as the room allows; stop one first.", "$.args.capability",
                actual: JsonValue.Create(_effects.Count), allowed: JsonValue.Create(EffectLimit));
        return new EffectPlan(ability, self, centre, radius, duration, parameters);
    }

    /// <summary>
    /// Whether a point effect of this ability could start at a spot now as far as the place goes: within the ability's reach of the
    /// Gubble (by marginM to spare), and in the team's sight for the player. The HUD asks it to send the Gubble closer first; it
    /// loosens nothing, as effect.start makes every check again. Null when the island has no such ability or no Gubble is here.
    /// </summary>
    public (bool InReach, bool InSight)? EffectSpot(string capability, Vector3 spot, float marginM = 0)
    {
        if (Rules?.Ability(capability) is not { } ability || Companion == null || !IsInstanceValid(Companion) || !Companion.IsInsideTree() || !spot.IsFinite())
            return null;
        return (Companion.GlobalPosition.DistanceTo(spot) <= ability.ReachM - Math.Max(0, marginM), TeamSeesSpot(spot, PlayerPrincipal));
    }

    /// <summary>Whether the place a point effect would float is in sight now: of the Gubble's eyes, or the player's (the team's sight).</summary>
    private bool TeamSeesSpot(Vector3 centre, string principal)
    {
        var place = new Aabb(centre - new Vector3(EffectSightHalfWidthM, 0, EffectSightHalfWidthM),
            new Vector3(EffectSightHalfWidthM * 2, GlowLook.WispLiftM, EffectSightHalfWidthM * 2));
        if (Companion != null && IsInstanceValid(Companion) && Companion.IsInsideTree() && SeesBox(Companion, place)) return true;
        return (principal == PlayerPrincipal || SharedSight) && Player != null && IsInstanceValid(Player) && Player.IsInsideTree() && SeesBox(Player, place);
    }

    /// <summary>effect.start, checked by PlanEffect: preview says what would start; otherwise the look lights it (or refuses, and nothing starts).</summary>
    private JsonObject EffectStart(EffectPlan plan, string principal, string actionId, string fingerprint, bool preview, string? approvedBy)
    {
        var look = LookHook != null && IsInstanceValid(LookHook) && LookHook.IsInsideTree() ? LookHook : null;
        if (preview)
        {
            // A preview is honest about the look too: it says now what the start would meet.
            if (look != null && look.GlowCount >= GlowLook.MaxGlows)
                throw new Refusal("budget_exceeded", "The game cannot show another light right now; stop one first.", "$.args.capability", retryable: true);
            var previewed = Previewed("effect.start", principal, actionId);
            previewed["data"] = EffectData(plan, null, null);
            return previewed;
        }
        // 'effect:' and 26 lowercase base32 characters (130 random bits): opaque, like job ids, never a counter.
        var id = "effect:" + System.Security.Cryptography.RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz234567", 26);
        var intensity = plan.Params.TryGetValue("intensity", out var level) ? level : 1.0;
        // The look lights it first: if it cannot, the command is refused and nothing is left half-started.
        if (look != null && !look.StartGlow(id, plan.Self ? Companion : null, plan.Centre, (float)plan.RadiusM, (float)intensity))
        {
            look.StopGlow(id);
            throw new Refusal("budget_exceeded", "The game cannot show another light right now; stop one first.", "$.args.capability", retryable: true);
        }
        var expires = Clock() + TimeSpan.FromSeconds(plan.DurationS);
        _effects.Add(new ActiveEffect { Id = id, Capability = plan.Ability.Capability, Principal = principal, FollowsCompanion = plan.Self, Expires = expires });
        return Transient("effect.start", principal, actionId, fingerprint, new JsonArray(id), EffectData(plan, id, expires), created: new JsonArray(id), approvedBy: approvedBy);
    }

    private JsonObject EffectData(EffectPlan plan, string? id, DateTime? expires)
    {
        var data = new JsonObject();
        if (id != null) data["effect"] = id;
        data["capability"] = plan.Ability.Capability;
        data["category"] = plan.Ability.Category;
        data["target"] = plan.Self ? "self" : "point";
        data["params"] = new JsonObject(plan.Params.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)JsonValue.Create(p.Value))));
        data["area"] = new JsonObject { ["center_m"] = KernelJson.Vector(plan.Centre), ["radius_m"] = plan.RadiusM };
        data["duration_s"] = plan.DurationS;
        if (expires != null) data["expires_utc"] = KernelJson.Utc(expires.Value);
        return data;
    }

    /// <summary>Ends effects (the look's light goes too) and says which.</summary>
    private List<string> EndEffects(IEnumerable<ActiveEffect> ending)
    {
        var ended = new List<string>();
        foreach (var effect in ending.ToList())
        {
            if (!_effects.Remove(effect)) continue;
            if (LookHook != null && IsInstanceValid(LookHook)) LookHook.StopGlow(effect.Id);
            ended.Add(effect.Id);
        }
        return ended;
    }

    /// <summary>The effects a stop by principal covers: the player's covers every effect, a companion's only its own.</summary>
    private IEnumerable<ActiveEffect> Stoppable(string principal) => _effects.Where(e => principal == PlayerPrincipal || e.Principal == principal);

    /// <summary>effect.stop's glows: one id (an unknown one, or another's, stops nothing) or all that principal may stop.</summary>
    private List<string> StopEffects(string principal, string which)
    {
        ExpireEffects();
        return EndEffects(Stoppable(principal).Where(e => which == "all" || e.Id == which));
    }

    /// <summary>goal.stop's glows: the Gubble carries every effect, so stopping it (or everything) ends the ones the principal may stop.</summary>
    private List<string> StopCompanionEffects(string principal)
    {
        ExpireEffects();
        return EndEffects(Stoppable(principal));
    }

    /// <summary>An effect ends at its duration (the host's clock).</summary>
    private void ExpireEffects()
    {
        if (_effects.Count == 0) return;
        var now = Clock();
        EndEffects(_effects.Where(e => now >= e.Expires));
    }

    /// <summary>The room unloads: every glow ends, the look's included.</summary>
    private void EndAllEffects()
    {
        EndEffects(_effects.ToList());
        if (LookHook != null && IsInstanceValid(LookHook) && !LookHook.IsQueuedForDeletion()) LookHook.StopAllGlows();
    }

    /// <summary>capabilities.list from the island's rules: capability_summary items by capability, filtered by category, paged by cursor.</summary>
    private JsonObject Capabilities(JsonElement args)
    {
        IEnumerable<IslandAbility> abilities = (Rules?.Abilities ?? Array.Empty<IslandAbility>()).OrderBy(a => a.Capability, StringComparer.Ordinal);
        if (args.TryGetProperty("category", out var category)) abilities = abilities.Where(a => a.Category == category.GetString());
        var all = abilities.ToList();
        var start = 0;
        if (args.TryGetProperty("cursor", out var cursor) &&
            (!int.TryParse(cursor.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out start) || start > all.Count))
            throw new Refusal("invalid_args", "That cursor is not from this list.", "$.args.cursor");
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : CapabilitiesDefaultLimit;
        var page = all.Skip(start).Take(limit).ToList();
        var items = page.Select(a => (JsonNode?)new JsonObject
        {
            ["capability"] = a.Capability, ["category"] = a.Category,
            ["params"] = new JsonObject(a.Params.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)new JsonObject { ["min"] = p.Value.Min, ["max"] = p.Value.Max }))),
            ["area_radius_max_m"] = a.AreaRadiusMaxM, ["duration_max_s"] = a.DurationMaxS,
        }).ToArray();
        var data = new JsonObject { ["items"] = new JsonArray(items) };
        if (start + page.Count < all.Count) data["next_cursor"] = (start + page.Count).ToString(CultureInfo.InvariantCulture);
        return data;
    }

    /// <summary>effect.start's arguments as the contract types them (request_invalid otherwise); the ability's bounds come after, in PlanEffect.</summary>
    private static void CheckEffectArgs(JsonElement args)
    {
        var capability = args.GetProperty("capability");
        if (capability.ValueKind != JsonValueKind.String || !Token.IsMatch(capability.GetString()!))
            throw new Refusal("request_invalid", "capability is a lowercase token.", "$.args.capability");
        foreach (var parameter in args.GetProperty("params").EnumerateObject())
        {
            var value = parameter.Value;
            var scalar = value.ValueKind switch
            {
                JsonValueKind.Number => Math.Abs(CanonicalJson.ReadNumber(value)) <= 1000000,
                JsonValueKind.True or JsonValueKind.False => true,
                JsonValueKind.String => KernelText.CodePoints(value.GetString()!).Length <= 64,
                _ => false,
            };
            if (!scalar) throw new Refusal("request_invalid", "Effect parameters are numbers within a million, booleans or short strings.", "$.args.params");
        }
        var area = args.GetProperty("area");
        if (area.ValueKind != JsonValueKind.Object || area.EnumerateObject().Any(p => p.Name is not ("center_m" or "radius_m")) ||
            !area.TryGetProperty("center_m", out var centre) || !area.TryGetProperty("radius_m", out var radius))
            throw new Refusal("request_invalid", "area has center_m and radius_m.", "$.args.area");
        CheckVector(centre, "$.args.area.center_m");
        if (radius.ValueKind != JsonValueKind.Number || CanonicalJson.ReadNumber(radius) is <= 0 or > 10)
            throw new Refusal("request_invalid", "radius_m is more than 0 and at most 10 metres.", "$.args.area.radius_m");
        var duration = args.GetProperty("duration_s");
        if (duration.ValueKind != JsonValueKind.Number || CanonicalJson.ReadNumber(duration) is <= 0 or > 600)
            throw new Refusal("request_invalid", "duration_s is more than 0 and at most 600 seconds.", "$.args.duration_s");
    }
}
