using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnFractal.Native.Room;

namespace EnFractal.Native.Kernel;

/// <summary>What fire does to a thing (the founder's rule, 13 October; CommandHost.FireRule).</summary>
public enum FireOutcome { Fireproof, Scorches, BurnsDown }

/// <summary>
/// Bloom, the flower trail, weather and Fire on the ground field, host side (docs/magic/README.md). Nature is room state:
/// the authority saves it as the host's nature block beside the team block (NatureBlock), checked whole when a save loads.
/// - Durable nature changes (nature.cast, nature.trail, nature.repair, and room.undo of them) are ordinary room revisions:
///   each goes through the authority with a durable receipt (compacted by room.checkpoint like any other), and room.undo
///   names the room revision to go back to, as the contract says. The latest nature changes can be taken back, by either
///   principal; a companion's undo of the player's change waits for the player.
/// - The simulation (weather, growth, spread, the trail) runs in memory from _PhysicsProcess in fixed steps and is saved
///   with every durable command and by a throttle, as the team block is: never moving the room revision.
/// - Every nature change needs a loaded save and a role that builds (RequireChange), before a preview, a hold or a commit,
///   and again when a held command commits after the player's click.
/// - The fire rule is one table, FireRule, read by every fire decision: preview targets, spread and object burn.
/// - What a companion learns (affected things, counts, scorched creations) is filtered by what the team has perceived; the
///   host keeps the complete set for protection and approval checks.
/// </summary>
public partial class CommandHost
{
    /// <summary>
    /// The founder's fire rule (13 October), the one table every fire decision reads:
    /// - locked things are always fireproof;
    /// - creations (the player's, or the Gubble's on the player's behalf) are fireproof in Creative mode, their bounds
    ///   stopping the burn as a lock's do; in Challenge and Live they survive as scorched, repairable remains (triggers off
    ///   until repaired at their original material cost), and a burn that would reach an unlocked one waits for the
    ///   player's yes;
    /// - the island's own stock and generated objects burn down completely.
    /// The next change to the rule is one edit here.
    /// </summary>
    public static readonly IReadOnlyDictionary<(string Kind, string Mode), FireOutcome> FireRule = new Dictionary<(string, string), FireOutcome>
    {
        [("locked", "creative")] = FireOutcome.Fireproof,
        [("locked", "challenge")] = FireOutcome.Fireproof,
        [("locked", "live")] = FireOutcome.Fireproof,
        [("creation", "creative")] = FireOutcome.Fireproof,
        [("creation", "challenge")] = FireOutcome.Scorches,
        [("creation", "live")] = FireOutcome.Scorches,
        [("stock", "creative")] = FireOutcome.BurnsDown,
        [("stock", "challenge")] = FireOutcome.BurnsDown,
        [("stock", "live")] = FireOutcome.BurnsDown,
    };

    /// <summary>A fire may reach at most this many things; a larger area is refused before it is previewed.</summary>
    public const int MaxFireTargets = 64;
    /// <summary>A preview lists at most this many cells (the count is always whole).</summary>
    public const int MaxPreviewCells = 200;
    /// <summary>Materials that burn, in an asset's materials or a creation's parts.</summary>
    private static readonly HashSet<string> FlammableMaterials = new(StringComparer.Ordinal) { "wood", "bark", "foliage", "fabric", "paper", "plastic", "leaf" };

    public NatureState Nature { get; private set; } = new();
    public NatureWeather NatureWeather => Nature.Weather;
    public string NatureNotice { get; private set; } = "";
    /// <summary>Whether this room runs its nature (RoomWorld turns it on); the saved block is loaded and kept either way.</summary>
    public bool NatureConfigured { get; private set; }
    /// <summary>Raised when what the ground field shows changed: a command, or a simulation step that changed anything.</summary>
    public event Action? NatureChanged;
    /// <summary>The simulation is saved at most this often while something is active (and at once when a fire ends, and on exit).</summary>
    public double NatureSaveIntervalS { get; set; } = 5.0;
    /// <summary>Whether room.undo can take back the latest nature change now (the HUD's Undo).</summary>
    public bool NatureCanUndo => Nature.UndoTarget(Revision) != null;
    /// <summary>Test seam: the ground the simulation finds at a cell, in place of the room's physics.</summary>
    internal Func<int, int, NatureCell?>? NatureSampleForTests { get; set; }

    /// <summary>A Fire preview the host made: whose, for exactly which cast, at which room revision, until when.</summary>
    private sealed record NaturePreview(string Principal, string Signature, int Revision, DateTime Expires);
    /// <summary>One thing a fire could reach (internal: the whole set, whoever asks) and what the rule does to it.</summary>
    private sealed record FireTarget(string Id, string Kind, FireOutcome Outcome, Aabb Bounds);

    private bool _natureUnavailable;
    private double _natureElapsed;
    private double _natureSaveClock;
    /// <summary>Something but the clock changed since the last save.</summary>
    private bool _natureDirty;
    /// <summary>The weather clock moved since the last save (saved on exit, never on its own by the throttle).</summary>
    private bool _natureClockMoved;
    /// <summary>Where the trail last planted: in memory with the flowers it planted, reset whenever nature is reloaded.</summary>
    private Vector3? _natureTrailAt;
    /// <summary>The bounds of fireproof things (locks, Creative's creations), refreshed while a fire burns.</summary>
    private Aabb[] _natureShelter = Array.Empty<Aabb>();
    private readonly Dictionary<string, NaturePreview> _naturePreviews = new(StringComparer.Ordinal);
    private readonly Queue<string> _natureLightQueue = new();
    /// <summary>What the scene shows of nature now (burned and scorched ids, with their nodes): applied only when it changes.</summary>
    private string _natureSceneKey = "";
    private readonly HashSet<string> _natureShown = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, uint> _natureLayers = new();
    private readonly Dictionary<ulong, Material?> _natureMaterials = new();
    /// <summary>Each tree-climbing collision shape's own faces, as the room built them, by its CollisionShape3D.</summary>
    private readonly Dictionary<ulong, Vector3[]> _climbFaces = new();
    private StandardMaterial3D? _natureScorchMaterial;

    // ---- loading and configuring ----

    /// <summary>Turns this room's nature on (RoomWorld does): the simulation runs and the nature commands are accepted.</summary>
    public void ConfigureNature()
    {
        NatureConfigured = true;
        LoadNature();
    }

    /// <summary>
    /// Nature from the authority's save (already judged by CheckNature when the save loaded): the scene follows it. A
    /// sidecar nature.json from earlier test builds is never read. Without a loaded save, nature waits with a notice.
    /// </summary>
    private void LoadNature()
    {
        _natureTrailAt = null;
        _naturePreviews.Clear();
        _natureLightQueue.Clear();
        _natureDirty = false;
        _natureClockMoved = false;
        if (Authority == null || !Authority.Call("is_ready").AsBool())
        {
            Nature = new NatureState();
            _natureUnavailable = true;
            NatureNotice = "The room's saved state is not loaded, so its nature waits for it.";
            ApplyNatureObjects();
            return;
        }
        var saved = KernelJson.ToJson(Authority.Call("nature")) as JsonObject;
        if (saved == null || saved.Count == 0) Nature = new NatureState();
        else if (NatureBlock.Problem(saved, Revision, Room) is { } problem)
        {
            Nature = new NatureState();
            _natureUnavailable = true;
            NatureNotice = "The saved nature is invalid (" + problem + "); it is kept as it was.";
            ApplyNatureObjects();
            return;
        }
        else Nature = NatureBlock.Unpack(saved);
        // A creation removed by an earlier build left its record behind: it is dropped (and saved so with the next change).
        foreach (var gone in Nature.Objects.Keys.Where(id => id.StartsWith("creation:", StringComparison.Ordinal) && EntityRevision(id) < 0).ToList())
            Nature.Objects.Remove(gone);
        _natureUnavailable = false;
        NatureNotice = "";
        ApplyNatureObjects();
        NatureChanged?.Invoke();
    }

    /// <summary>The authority's nature_check: a save's nature block is believed only if NatureBlock.Problem finds nothing wrong.</summary>
    public Godot.Collections.Dictionary CheckNature(Godot.Collections.Dictionary nature, int revision)
    {
        var problem = NatureBlock.Problem(KernelJson.ToJson(nature), revision, Room);
        return problem == null ? new() { ["ok"] = true } : new() { ["ok"] = false, ["message"] = "The saved nature is invalid: " + problem + "; the save was not loaded and is kept as it was." };
    }

    private void RequireNature()
    {
        if (!NatureConfigured) throw new Refusal("not_ready", "This room's nature is not running.", retryable: true);
        if (_natureUnavailable) throw new Refusal("not_ready", NatureNotice, retryable: true);
    }

    // ---- what the player's controls send ----

    public JsonObject PlayerNaturePreview(string capability, Vector3 point, float? radius = null, float? duration = null) =>
        PlayerCommand("nature.cast", NatureCastArgs(capability, point, radius, duration), preview: true);

    public JsonObject PlayerNatureCast(string capability, Vector3 point, float? radius = null, float? duration = null, string? previewId = null)
    {
        var args = NatureCastArgs(capability, point, radius, duration);
        if (previewId != null) args["preview_id"] = previewId;
        return PlayerCommand("nature.cast", args);
    }

    public JsonObject PlayerNatureTrail(bool enabled) => PlayerCommand("nature.trail", new JsonObject { ["enabled"] = enabled });

    public JsonObject PlayerNatureRepair(string target) => PlayerCommand("nature.repair", new JsonObject { ["target"] = target });

    /// <summary>room.undo back to before the latest nature change (refused, as the contract says, when there is none).</summary>
    public JsonObject PlayerNatureUndo() =>
        PlayerCommand("room.undo", new JsonObject { ["to_revision"] = Nature.UndoTarget(Revision) ?? Math.Max(0, Revision - 1) }, expectedRevision: Revision);

    /// <summary>A cast with the pack's own default radius and duration where the control names none.</summary>
    private JsonObject NatureCastArgs(string capability, Vector3 point, float? radius, float? duration)
    {
        var ability = Rules?.Ability(capability);
        return new JsonObject
        {
            ["capability"] = capability, ["center_m"] = KernelJson.Vector(point),
            ["radius_m"] = radius ?? (float)(ability?.AreaRadiusDefaultM ?? 0.25), ["duration_s"] = duration ?? (float)(ability?.DurationDefaultS ?? 30),
        };
    }

    // ---- the commands ----

    /// <summary>The shape of the nature commands' args (the contract's): anything malformed is request_invalid, as for every op.</summary>
    private static void CheckNatureArgs(string op, JsonElement args)
    {
        var allowed = op switch
        {
            "nature.cast" => new[] { "capability", "center_m", "radius_m", "duration_s", "preview_id" },
            "nature.trail" => new[] { "enabled" },
            "nature.repair" => new[] { "target" },
            _ => Array.Empty<string>(),
        };
        foreach (var property in args.EnumerateObject())
            if (!allowed.Contains(property.Name))
                throw new Refusal("field_unknown", "This argument is not part of the operation.", "$.args." + KernelJson.DisplayText(property.Name, 64));
        var required = op switch
        {
            "nature.cast" => new[] { "capability", "center_m", "radius_m", "duration_s" },
            "nature.trail" => new[] { "enabled" },
            "nature.repair" => new[] { "target" },
            _ => Array.Empty<string>(),
        };
        foreach (var name in required)
            if (!args.TryGetProperty(name, out _)) throw new Refusal("request_invalid", $"This operation needs '{name}'.", "$.args." + name);
        if (op == "nature.cast")
        {
            if (Str(args, "capability") is not ("bloom" or "fire")) throw new Refusal("request_invalid", "capability is bloom or fire.", "$.args.capability");
            CheckVector(args.GetProperty("center_m"), "$.args.center_m");
            // The contract's outer bounds; the island's narrower ones are the rules pack's (invalid_args when exceeded).
            foreach (var (name, max) in new[] { ("radius_m", NatureBlock.MaxFireRadiusM), ("duration_s", 600f) })
            {
                var value = args.GetProperty(name);
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number) || number <= 0 || number > max)
                    throw new Refusal("request_invalid", $"{name} is a number above 0 and at most {max}.", "$.args." + name);
            }
            if (args.TryGetProperty("preview_id", out var id) && (id.ValueKind != JsonValueKind.String || !PreviewIdPattern.IsMatch(id.GetString()!)))
                throw new Refusal("request_invalid", "preview_id is the id a preview of this host returned.", "$.args.preview_id");
        }
        if (op == "nature.trail" && args.GetProperty("enabled").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new Refusal("request_invalid", "enabled is true or false.", "$.args.enabled");
        if (op == "nature.repair" && (Str(args, "target") is not { } target || !CreationId.IsMatch(target)))
            throw new Refusal("request_invalid", "Repair names one creation.", "$.args.target");
    }

    private static readonly System.Text.RegularExpressions.Regex PreviewIdPattern = new(@"\A[a-f0-9]{32}\z", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>nature.cast, nature.trail and nature.repair: each checked, previewed or held, then committed as one room revision.</summary>
    private JsonObject NatureCommand(JsonElement root, string principal, string fingerprint, string? approvedBy)
    {
        var op = Op(root);
        var action = Str(root, "action_id")!;
        var args = root.GetProperty("args");
        var preview = root.TryGetProperty("preview", out var p) && p.ValueKind == JsonValueKind.True;
        RequireNature();
        // What every change needs, before a preview, a hold or a commit, and again when the player's click commits a held one.
        RequireChange(principal);
        if (op == "nature.cast")
        {
            var capability = Str(args, "capability")!;
            var ability = Rules?.Ability(capability) ?? throw new Refusal("unsupported_capability", "This island does not grant that nature ability.", "$.args.capability");
            var cast = CheckCast(ability, args, principal);
            return capability == "fire"
                ? CastFire(root, ability, cast, principal, action, fingerprint, preview, approvedBy)
                : CastBloom(root, ability, cast, principal, action, fingerprint, preview, approvedBy);
        }
        if (op == "nature.trail") return SetTrail(root, args.GetProperty("enabled").GetBoolean(), principal, action, fingerprint, preview, approvedBy);
        return Repair(root, Str(args, "target")!, principal, action, fingerprint, preview, approvedBy);
    }

    /// <summary>A cast's place and size, checked against the island's rules for this caster: the pack's limits, never the code's.</summary>
    private readonly record struct Cast(Vector3 Center, float Radius, float Duration, float Intensity);

    private Cast CheckCast(IslandAbility ability, JsonElement args, string principal)
    {
        var center = KernelJson.ReadVector(args.GetProperty("center_m"));
        var radius = (float)args.GetProperty("radius_m").GetDouble();
        var duration = (float)args.GetProperty("duration_s").GetDouble();
        if (!ability.CastBy.Contains(principal == PlayerPrincipal ? "player" : "companion"))
            throw new Refusal("permission_denied", "This island does not allow this caster to use that ability.", "$.args.capability");
        if (radius > ability.AreaRadiusMaxM)
            throw new Refusal("invalid_args", "That radius is more than this island allows.", "$.args.radius_m", allowed: JsonValue.Create(ability.AreaRadiusMaxM));
        if (duration > ability.DurationMaxS)
            throw new Refusal("invalid_args", "That duration is more than this island allows.", "$.args.duration_s", allowed: JsonValue.Create(ability.DurationMaxS));
        if (!Room.Bounds.Grow(0.02f).HasPoint(center)) throw new Refusal("out_of_bounds", "Choose a point inside this island.", "$.args.center_m");
        if (Companion == null || Companion.GlobalPosition.DistanceTo(center) > ability.ReachM)
            throw new Refusal("out_of_bounds", "Bring the Gubble closer before casting.", "$.args.center_m", retryable: true);
        if (!TeamSeesSpot(center, principal)) throw new Refusal("target_not_found", "The team must see the place the Gubble will affect.", "$.args.center_m");
        if (Nature.Fire != null) throw new Refusal("target_busy", "Let the current fire finish or undo it before another nature cast.", retryable: true);
        var intensity = ability.Params.TryGetValue("intensity", out var setting) ? (float)setting.Default : 0.6f;
        return new Cast(center, radius, duration, intensity);
    }

    private JsonObject CastBloom(JsonElement root, IslandAbility ability, Cast cast, string principal, string action, string fingerprint, bool preview, string? approvedBy)
    {
        NatureCell? Sample(int x, int z) => SampleNatureCell(x, z, cast.Center.Y);
        var eligible = NatureState.Disk(cast.Center, cast.Radius).Select(g => Sample(g.X, g.Z)).Where(c => c is { Fertile: true }).Select(c => c!).ToList();
        if (eligible.Count == 0) throw new Refusal("invalid_args", "Bloom needs walkable soil or grass, above water.", "$.args.center_m");
        var data = CastData(cast, eligible, principal);
        var keyed = principal != PlayerPrincipal && ability.Tier == "keyed_yes";
        data["requires_approval"] = keyed;
        if (preview) return NaturePreviewed("nature.cast", principal, action, data, Array.Empty<string>());
        if (keyed && approvedBy == null) return Hold(root, principal, action, fingerprint, "nature.cast", "The Gubble asks to cast bloom in this area.", new(), bindRevision: true);
        var candidate = Nature.Copy();
        var step = candidate.BeginUndo("Bloom", Revision + 1, principal);
        var planted = candidate.Bloom(cast.Center, cast.Radius, cast.Duration, cast.Intensity, Sample, step);
        if (planted == 0) throw new Refusal("invalid_args", "Nothing can take root in that area.", "$.args.center_m");
        data.Remove("cells");
        data.Remove("cells_truncated");
        data["started_cells"] = eligible.Where(TeamSeesCellFor(principal)).Count(c => candidate.Cells.TryGetValue(c.Key, out var cell) && cell.LifeS > 0);
        return CommitNature(candidate, "nature.cast", principal, action, fingerprint, approvedBy, Array.Empty<string>(), data, undo: false);
    }

    private JsonObject CastFire(JsonElement root, IslandAbility ability, Cast cast, string principal, string action, string fingerprint, bool preview, string? approvedBy)
    {
        NatureCell? Sample(int x, int z) => SampleNatureCell(x, z, cast.Center.Y);
        var eligible = NatureState.Disk(cast.Center, cast.Radius).Select(g => Sample(g.X, g.Z))
            .Where(c => c != null && c.Fuel > 0 && c.Moisture < NatureLaws.IgnitionMaxMoisture).Select(c => c!).ToList();
        if (eligible.Count == 0) throw new Refusal("invalid_args", "There is no dry fuel at that spot.", "$.args.center_m");
        var targets = FireTargets(cast.Center, cast.Radius);
        if (targets.Count > MaxFireTargets) throw new Refusal("invalid_args", "That fire could touch too many things. Choose a smaller area.", "$.args.radius_m");
        var burnable = targets.Where(t => t.Outcome != FireOutcome.Fireproof).ToList();
        // Only these creations may be scorched, each at the revision it has now: a creation built or revised since never inherits the yes.
        var creations = burnable.Where(t => t.Outcome == FireOutcome.Scorches).ToDictionary(t => t.Id, t => EntityRevision(t.Id), StringComparer.Ordinal);
        var signature = CanonicalJson.Sha256Hex(CanonicalJson.Bytes(new JsonObject
        {
            ["capability"] = "fire", ["center_m"] = KernelJson.Vector(cast.Center), ["radius_m"] = KernelJson.Num(cast.Radius), ["duration_s"] = KernelJson.Num(cast.Duration),
            ["intensity"] = KernelJson.Num(cast.Intensity), ["mode"] = Mode,
            ["targets"] = new JsonArray(targets.Select(t => (JsonNode?)new JsonObject { ["id"] = t.Id, ["outcome"] = t.Outcome.ToString() }).ToArray()),
        }));
        var companion = principal != PlayerPrincipal;
        // The companion may not injure the player without a click: a fire whose area reaches the player waits for the player.
        var reachesPlayer = companion && Mode != "creative" && FireReachesPlayer(cast.Center, cast.Radius);
        var keyed = companion && ability.Tier == "keyed_yes";
        var knows = TeamKnowsFor(principal);
        var data = CastData(cast, eligible, principal);
        data["affected_ids"] = new JsonArray(burnable.Where(t => knows(t.Id)).Select(t => (JsonNode?)t.Id).ToArray());
        data["max_burning_cells"] = NatureState.MaxBurning;
        data["requires_approval"] = creations.Count > 0 || reachesPlayer || keyed;
        if (preview)
        {
            foreach (var stale in _naturePreviews.Where(p => p.Value.Expires <= Clock()).Select(p => p.Key).ToList()) _naturePreviews.Remove(stale);
            while (_naturePreviews.Count >= 32) _naturePreviews.Remove(_naturePreviews.Keys.First());
            // 128 bits from a cryptographic generator, as every host-made handle (contracts/README.md "Identifiers").
            var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            _naturePreviews[token] = new NaturePreview(principal, signature, Revision, Clock() + ApprovalLifetime);
            data["preview_id"] = token;
            return NaturePreviewed("nature.cast", principal, action, data, burnable.Where(t => knows(t.Id)).Select(t => t.Id));
        }
        var previewId = root.GetProperty("args").TryGetProperty("preview_id", out var given) ? given.GetString() : null;
        if (previewId == null || !_naturePreviews.TryGetValue(previewId, out var planned) || planned.Principal != principal || planned.Signature != signature ||
            planned.Expires <= Clock() || planned.Revision != Revision)
            throw new Refusal("revision_conflict", "Preview this fire again before casting: the place, what it would reach or its permission has changed.", "$.args.preview_id", retryable: true);
        if ((creations.Count > 0 || reachesPlayer || keyed) && approvedBy == null)
            return Hold(root, principal, action, fingerprint, "nature.cast", FireHoldReason(companion, creations.Count > 0, reachesPlayer), creations, bindRevision: true);
        var candidate = Nature.Copy();
        var step = candidate.BeginUndo("Fire", Revision + 1, principal);
        var lit = candidate.Ignite(cast.Center, cast.Radius, cast.Duration, cast.Intensity, Revision + 1, creations, Sample, step);
        if (lit == 0) throw new Refusal("invalid_args", "Nothing can take hold at the center of that area.", "$.args.center_m");
        _naturePreviews.Remove(previewId);
        data.Remove("cells");
        data.Remove("cells_truncated");
        data.Remove("affected_ids");
        data["started_cells"] = eligible.Where(TeamSeesCellFor(principal)).Count(c => candidate.Cells.TryGetValue(c.Key, out var cell) && cell.BurningS > 0);
        return CommitNature(candidate, "nature.cast", principal, action, fingerprint, approvedBy, burnable.Where(t => knows(t.Id)).Select(t => t.Id), data, undo: false);
    }

    /// <summary>The prompt's words for a held fire: what it would do, never which or how many things (names are untrusted, and the companion reads it too).</summary>
    private static string FireHoldReason(bool companion, bool scorches, bool reachesPlayer)
    {
        var words = new List<string> { companion ? "The Gubble asks to light a fire here." : "This fire needs your yes." };
        if (reachesPlayer) words.Add("It would reach you.");
        if (scorches) words.Add("It could scorch unlocked creations; they stay repairable.");
        words.Add("Approve this exact area or deny it.");
        return string.Join(" ", words);
    }

    /// <summary>A cast's data: its place and size, and the cells it would touch as far as the requester's team has perceived them.</summary>
    private JsonObject CastData(Cast cast, List<NatureCell> eligible, string principal)
    {
        var seen = eligible.Where(TeamSeesCellFor(principal)).ToList();
        return new JsonObject
        {
            ["center_m"] = KernelJson.Vector(cast.Center), ["radius_m"] = KernelJson.Num(cast.Radius), ["duration_s"] = KernelJson.Num(cast.Duration),
            ["cell_count"] = seen.Count,
            ["cells"] = new JsonArray(seen.Take(MaxPreviewCells).Select(c => (JsonNode?)KernelJson.Vector(c.Position)).ToArray()),
            ["cells_truncated"] = seen.Count > MaxPreviewCells,
        };
    }

    /// <summary>A preview's answer: its data read back as strict JSON, as a durable receipt's is (any reader's number type works).</summary>
    private JsonObject NaturePreviewed(string op, string principal, string action, JsonObject data, IEnumerable<string> affected)
    {
        var result = Previewed(op, principal, action, affected.ToArray());
        result["data"] = Normalized(data);
        return result;
    }

    /// <summary>
    /// The flower trail on or off. Turning it off is always allowed; turning it back on after the player turned it off is
    /// the player's choice, so the Gubble's request waits for the player's click, as a keyed_yes Bloom does.
    /// </summary>
    private JsonObject SetTrail(JsonElement root, bool enabled, string principal, string action, string fingerprint, bool preview, string? approvedBy)
    {
        var role = principal == PlayerPrincipal ? "player" : "companion";
        var ability = Rules?.Ability("bloom");
        if (enabled && ability == null) throw new Refusal("unsupported_capability", "This island does not grant Bloom.", "$.args.enabled");
        if (enabled && !ability!.CastBy.Contains(role)) throw new Refusal("permission_denied", "This island does not allow this caster to use Bloom.", "$.args.enabled");
        if (Nature.Fire != null) throw new Refusal("target_busy", "Let the current fire finish or undo it first.", retryable: true);
        var overrulesPlayer = enabled && role == "companion" && !Nature.TrailEnabled && Nature.TrailPrincipal == "player";
        var keyed = enabled && role == "companion" && ability!.Tier == "keyed_yes";
        var data = new JsonObject { ["enabled"] = enabled, ["requires_approval"] = overrulesPlayer || keyed };
        if (preview) return NaturePreviewed("nature.trail", principal, action, data, Array.Empty<string>());
        if ((overrulesPlayer || keyed) && approvedBy == null)
            return Hold(root, principal, action, fingerprint, "nature.trail",
                overrulesPlayer ? "The Gubble asks to turn the flower trail you turned off back on." : "The Gubble asks to leave a flower trail.", new(), bindRevision: true);
        var candidate = Nature.Copy();
        candidate.BeginUndo("Flower trail", Revision + 1, principal);
        candidate.TrailEnabled = enabled;
        candidate.TrailPrincipal = role;
        candidate.TrailRulesSha256 = Rules?.Sha256 ?? "";
        return CommitNature(candidate, "nature.trail", principal, action, fingerprint, approvedBy, Array.Empty<string>(), data, undo: false);
    }

    /// <summary>A scorched creation repaired from its hash-pinned design at its original material cost (free in Creative).</summary>
    private JsonObject Repair(JsonElement root, string id, string principal, string action, string fingerprint, bool preview, string? approvedBy)
    {
        var instance = Instance(id) ?? throw new Refusal("target_not_found", "That is not in this room.", "$.args.target");
        if (Authority.Call("is_locked", id).AsBool()) throw new Refusal("target_protected", "Only the player can unlock this protected creation.", "$.args.target");
        if (!Nature.Objects.TryGetValue(id, out var item) || !item.Creation || item.Burn <= 0) throw new Refusal("invalid_args", "That creation does not need repair.", "$.args.target");
        if (item.SourceHash != CanonicalJson.Sha256Hex(CanonicalJson.Bytes(KernelJson.ToJson(instance["source"]))))
            throw new Refusal("revision_conflict", "This creation's design changed; its old repair quote no longer applies.", "$.args.target");
        if (Nature.Fire != null) throw new Refusal("target_busy", "Let the fire finish or undo it before repairing.", retryable: true);
        var free = Mode == "creative";
        if (!free && item.RepairCost.Any(c => Nature.Wallet.GetValueOrDefault(c.Key) < c.Value))
            throw new Refusal("invalid_args", "The team's material supply cannot cover this repair yet.", "$.args.target");
        var data = new JsonObject { ["cost"] = free ? new JsonObject() : new JsonObject(item.RepairCost.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => KeyValuePair.Create(c.Key, (JsonNode?)c.Value))) };
        if (preview) return NaturePreviewed("nature.repair", principal, action, data, new[] { id });
        if (principal != PlayerPrincipal && instance["owner_id"].AsString() == PlayerPrincipal && approvedBy == null)
            return Hold(root, principal, action, fingerprint, "nature.repair", $"The Gubble asks to repair {id}, which you made, using the team's materials.",
                new() { [id] = EntityRevision(id) }, bindRevision: true);
        var candidate = Nature.Copy();
        var step = candidate.BeginUndo("Repair", Revision + 1, principal);
        candidate.RememberObject(step, id);
        if (!free) foreach (var (material, units) in item.RepairCost) candidate.Wallet[material] -= units;
        candidate.Objects.Remove(id);
        return CommitNature(candidate, "nature.repair", principal, action, fingerprint, approvedBy, new[] { id }, data, undo: false);
    }

    /// <summary>
    /// room.undo (contracts/README.md: it names the room revision): takes the room back to to_revision when every change
    /// after it is a nature change the host can still take back (its latest steps, up to NatureState.MaxUndoSteps). A
    /// newer change of another kind (building, moving, protecting) cannot be undone yet. The undo is itself a room
    /// revision, and never fails for a full receipt ledger. A companion's undo of anyone else's change waits for the player.
    /// </summary>
    private JsonObject NatureUndo(JsonElement root, string principal, string fingerprint, string? approvedBy)
    {
        var action = Str(root, "action_id")!;
        var preview = root.TryGetProperty("preview", out var p) && p.ValueKind == JsonValueKind.True;
        RequireNature();
        RequireChange(principal);
        var named = root.GetProperty("args").GetProperty("to_revision").GetInt64();
        if (named >= Revision) throw new Refusal("invalid_args", "Undo names an earlier room revision.", "$.args.to_revision", actual: JsonValue.Create(Revision));
        var target = (int)named;
        var steps = Nature.StepsBackTo(target, Revision);
        if (steps == null)
        {
            if (Nature.UndoTarget(Revision) is not { } reachable)
                throw new Refusal("unsupported_capability", "The latest change is not one the island can undo yet: only nature changes (Bloom, Fire, the trail, repairs) can be undone.", "$.args.to_revision");
            throw new Refusal("invalid_args", "That revision is not just before a nature change the island can still undo.", "$.args.to_revision", allowed: JsonValue.Create(reachable));
        }
        var objects = steps.SelectMany(s => s.Objects.Keys).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (objects.Any(id => Authority.Call("is_locked", id).AsBool()))
            throw new Refusal("target_protected", "Unlock the protected thing before undoing what fire did to it.", "$.args.to_revision");
        var needsApproval = principal != PlayerPrincipal && steps.Any(s => s.Principal != principal);
        var knows = TeamKnowsFor(principal);
        var data = new JsonObject
        {
            ["undone"] = string.Join(", ", steps.Select(s => s.Label)), ["undone_steps"] = steps.Count, ["requires_approval"] = needsApproval,
        };
        if (preview) return NaturePreviewed("room.undo", principal, action, data, objects.Where(knows));
        if (needsApproval && approvedBy == null)
            return Hold(root, principal, action, fingerprint, "room.undo", "The Gubble asks to undo your latest nature change.",
                objects.Where(id => EntityRevision(id) >= 0).ToDictionary(id => id, EntityRevision, StringComparer.Ordinal), bindRevision: true);
        var candidate = Nature.Copy();
        candidate.UndoSteps(candidate.StepsBackTo(target, Revision)!, Revision + 1, target);
        return CommitNature(candidate, "room.undo", principal, action, fingerprint, approvedBy, objects.Where(knows), data, undo: true);
    }

    /// <summary>
    /// One durable nature change: the candidate's block, the receipt (what it affected, its data) and the room's next
    /// revision in one authority write. Only once that is on disk does the candidate become the room's nature. The first
    /// answer is rebuilt from the durable receipt, exactly as every replay will be.
    /// </summary>
    private JsonObject CommitNature(NatureState candidate, string op, string principal, string action, string fingerprint, string? approvedBy,
        IEnumerable<string> affected, JsonObject data, bool undo)
    {
        candidate.ChangedAt = Revision + 1;
        candidate.TrimUndo();
        var meta = new Godot.Collections.Dictionary { ["fingerprint"] = fingerprint, ["op"] = op, ["at_utc"] = Now() };
        // The player's own held change (a fire near a creation) needed the player's click; it is no one else's approval.
        if (approvedBy != null && principal != PlayerPrincipal) meta["approved_by"] = approvedBy;
        var request = new Godot.Collections.Dictionary
        {
            ["op"] = "nature", ["action_id"] = action, ["expected_revision"] = Revision, ["expected_permission_revision"] = PermissionRevision,
            ["nature"] = KernelJson.ToVariant(NatureBlock.Pack(candidate)), ["affected"] = new Godot.Collections.Array(affected.Select(id => (Variant)id)),
            ["data"] = KernelJson.ToVariant(data), ["undo"] = undo,
        };
        var outcome = Authority.Call("submit", principal, request, meta).AsGodotDictionary();
        if (!outcome["ok"].AsBool()) throw Translate(outcome);
        PublishNature(candidate);
        _natureDirty = false;
        _natureClockMoved = false;
        _natureSaveClock = 0;
        return Durable(Authority.Call("receipt_for", principal, action).AsGodotDictionary(), replayed: false);
    }

    /// <summary>A saved nature becomes the room's: the scene follows, and the look hears of it.</summary>
    private void PublishNature(NatureState state)
    {
        Nature = state;
        NatureNotice = "";
        if (LookHook != null) LookHook.Wind = new Vector2(NatureWeather.WindMps.X, NatureWeather.WindMps.Z);
        ApplyNatureObjects();
        NatureChanged?.Invoke();
    }

    // ---- what a companion learns ----

    /// <summary>Whether the requester's team knows a thing: the player knows everything; a companion what either avatar sees now or the team's map holds.</summary>
    private Func<string, bool> TeamKnowsFor(string principal)
    {
        if (principal == PlayerPrincipal) return _ => true;
        var known = new HashSet<string>(Perception(principal), StringComparer.Ordinal);
        known.UnionWith(TeamMemory().Entries.Keys);
        return known.Contains;
    }

    /// <summary>
    /// Whether the requester's team has perceived a cell: for the player, every cell; for a companion, a cell in discovered
    /// space on its surface's level, or near an avatar's eyes on a surface with no level yet (within the radius the sweep
    /// discovers space in), or on a thing the team knows.
    /// </summary>
    private Func<NatureCell, bool> TeamSeesCellFor(string principal)
    {
        if (principal == PlayerPrincipal) return _ => true;
        var knows = TeamKnowsFor(principal);
        var eyes = new SmallPlayerController?[] { Player, Companion }.Where(b => b != null && IsInstanceValid(b) && b.IsInsideTree()).Select(b => b!.EyeCamera.GlobalPosition).ToList();
        return cell =>
        {
            if (!cell.Surface.StartsWith("shell:", StringComparison.Ordinal)) return knows(cell.Surface);
            if (_levels.ContainsKey(cell.Surface)) return DiscoveredAt(cell.Surface, cell.Position.X, cell.Position.Z);
            return eyes.Any(e => new Vector2(e.X - cell.Position.X, e.Z - cell.Position.Z).Length() <= DiscoverRadiusM);
        };
    }

    /// <summary>nature.describe: the island's nature as the requester's team knows it.</summary>
    private JsonObject NatureSnapshot(string principal)
    {
        var seesCell = TeamSeesCellFor(principal);
        var knows = TeamKnowsFor(principal);
        return new JsonObject
        {
            ["nature_revision"] = Nature.ChangedAt, ["trail_enabled"] = Nature.TrailEnabled,
            ["weather"] = new JsonObject { ["pattern"] = NatureWeather.Pattern, ["rain"] = NatureWeather.Rain01, ["wind_mps"] = KernelJson.Vector(NatureWeather.WindMps) },
            ["planted_cells"] = Nature.Cells.Values.Count(c => c.Growth > 0 && seesCell(c)),
            ["burning_cells"] = Nature.Cells.Values.Count(c => c.BurningS > 0 && seesCell(c)),
            ["wallet"] = new JsonObject(Nature.Wallet.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
            ["scorched"] = new JsonArray(Nature.Objects.Values.Where(o => o.Creation && o.Burn > 0 && knows(o.Id)).Select(o => o.Id).OrderBy(id => id, StringComparer.Ordinal).Select(id => (JsonNode?)id).ToArray()),
            ["can_undo"] = NatureCanUndo,
            ["notice"] = KernelJson.DisplayText(NatureNotice, 200),
        };
    }

    // ---- what fire left blocks the verbs ----

    /// <summary>Whether a thing is out of use because of fire: a stock object burned down, or a creation awaiting repair.</summary>
    public bool IsNatureUnavailable(string id) => Nature.Objects.TryGetValue(id, out var item) && (item.Creation ? item.Burn > 0 : item.BurnedDown);

    private bool IsBurnedDown(string id) => Nature.Objects.TryGetValue(id, out var item) && item.BurnedDown;

    /// <summary>
    /// What fire left blocks the verbs that change or use a thing, wherever a command names it (target, targets,
    /// placement.on): a stock object that burned down is gone, as if it were not in the room; a scorched creation can be
    /// repaired or removed, nothing else, until it is repaired. Goals and effects change no thing (a goal re-checks its
    /// target on arrival), so they are judged elsewhere.
    /// </summary>
    private void CheckNatureRemains(string op, JsonElement args)
    {
        if (op is "goal.set" or "goal.stop" or "effect.start" or "effect.stop" or "effect.adjust" or "nature.repair") return;
        foreach (var (id, path) in Named(op, args))
        {
            if (!Nature.Objects.TryGetValue(id, out var item)) continue;
            if (item.BurnedDown) throw new Refusal("target_not_found", "That is not in this room.", path);
            if (item.Creation && item.Burn > 0 && !(op == "entity.remove" && path == "$.args.target"))
                throw new Refusal("target_busy", "This creation is scorched; repair it before using, changing, protecting or building on it.", path);
        }
    }

    /// <summary>A removal's nature: the creation's scorch record goes with it, in the same save (null when it has none).</summary>
    private NatureState? NatureWithout(string id)
    {
        if (!Nature.Objects.ContainsKey(id)) return null;
        var cleared = Nature.Copy();
        cleared.Objects.Remove(id);
        return cleared;
    }

    // ---- the fire rule ----

    /// <summary>What the fire rule does to an entity summary now: locked, else a creation, else one of the island's own objects.</summary>
    private FireOutcome FireOutcomeOf(JsonObject entity)
    {
        var kind = entity["protected"]?.GetValue<bool>() == true ? "locked" : entity["kind"]!.GetValue<string>() == "creation" ? "creation" : "stock";
        return FireRule.TryGetValue((kind, Mode), out var outcome) ? outcome : FireRule[(kind, "creative")];
    }

    /// <summary>
    /// Every object and creation a fire at center could reach, whoever asks (the protection and approval checks need them
    /// all): what does not burn is left out, a held thing is in hands, and each carries the rule's outcome for it now.
    /// </summary>
    private List<FireTarget> FireTargets(Vector3 center, float radius) =>
        Entities().Where(e => e["kind"]!.GetValue<string>() is "creation" or "object" && e["held_by"] == null && BoundsOf(e).Grow(radius).HasPoint(center) &&
                Flammable(e["id"]!.GetValue<string>()))
            .Select(e => new FireTarget(e["id"]!.GetValue<string>(), e["kind"]!.GetValue<string>(), FireOutcomeOf(e), BoundsOf(e)))
            .ToList();

    private bool Flammable(string id)
    {
        if (id.StartsWith("obj:", StringComparison.Ordinal))
            return Room.Objects.FirstOrDefault(o => o.Id == id)?.Asset.Materials.Any(m => FlammableMaterials.Contains(m.Role)) == true;
        var source = Instance(id)?["source"].AsGodotDictionary();
        return source != null && source.ContainsKey("parts") && source["parts"].AsGodotArray().Any(p => FlammableMaterials.Contains(p.AsGodotDictionary()["material"].AsString()));
    }

    /// <summary>The fireproof things' bounds (the rule's Fireproof: locks always, creations in Creative): their ground does not burn.</summary>
    private void RefreshNatureShelter() =>
        _natureShelter = Entities().Where(e => e["kind"]!.GetValue<string>() is "creation" or "object" && FireOutcomeOf(e) == FireOutcome.Fireproof).Select(BoundsOf).ToArray();

    private bool NatureFireproof(NatureCell cell) => _natureShelter.Any(b => b.Grow(NatureLaws.ShelterMarginM).HasPoint(cell.Position + Vector3.Up * 0.02f));

    /// <summary>Whether a fire's area reaches the player's avatar (with the reach at which burning ground hurts).</summary>
    private bool FireReachesPlayer(Vector3 center, float radius)
    {
        if (Player == null || !IsInstanceValid(Player)) return false;
        var at = Player.GlobalPosition;
        return new Vector2(at.X - center.X, at.Z - center.Z).Length() <= radius + NatureLaws.PlayerBurnM && Math.Abs(at.Y - center.Y) <= 0.5f;
    }

    // ---- the simulation ----

    /// <summary>
    /// The simulation's tick, from _PhysicsProcess (its ray queries belong there, as the sight sweep's do): whole steps of
    /// NatureLaws.StepS with the remainder kept, at most NatureLaws.MaxCatchUpSteps a tick (time beyond them is dropped,
    /// so a stall or a reload never catches up), a bounded batch of light probes, and the throttled save: at most every
    /// NatureSaveIntervalS while something changes, at once when a fire ends. Tests call it directly.
    /// </summary>
    public void AdvanceNature(double delta)
    {
        if (!NatureConfigured || _natureUnavailable || !double.IsFinite(delta) || delta <= 0) return;
        if (Authority == null || !Authority.Call("is_ready").AsBool()) return;
        _natureElapsed += delta;
        _natureSaveClock += delta;
        var steps = 0;
        var fireEnded = false;
        while (_natureElapsed >= NatureLaws.StepS && steps < NatureLaws.MaxCatchUpSteps)
        {
            _natureElapsed -= NatureLaws.StepS;
            steps++;
            fireEnded |= StepNature();
        }
        _natureElapsed = Math.Min(_natureElapsed, NatureLaws.StepS);
        ProbeNatureLight();
        if (_natureDirty && (fireEnded || _natureSaveClock >= NatureSaveIntervalS)) SaveNature();
    }

    /// <summary>One step: weather, ground and fire, the trail, objects catching, the player near the flames. Returns whether a fire ended.</summary>
    private bool StepNature()
    {
        var hadFire = Nature.Fire != null;
        if (hadFire) RefreshNatureShelter();
        var eyes = new SmallPlayerController?[] { Player, Companion }.Where(b => b != null && IsInstanceValid(b)).Select(b => b!.GlobalPosition).ToList();
        var fire = Nature.Fire;
        bool Active(NatureCell cell) =>
            eyes.Any(e => new Vector2(e.X - cell.Position.X, e.Z - cell.Position.Z).Length() <= NatureLaws.ActiveRadiusM) ||
            (fire != null && new Vector2(fire.Center.X - cell.Position.X, fire.Center.Z - cell.Position.Z).Length() <= fire.RadiusM);
        NatureCell? Sample(int x, int z) => SampleNatureCell(x, z, Nature.Cells.TryGetValue(NatureState.Key(x, z), out var cell) ? cell.Y : fire?.Center.Y);
        var changed = Nature.Step(NatureLaws.StepS, Sample, NatureFireproof, Active);
        changed |= PlantTrail();
        changed |= BurnNatureObjects(NatureLaws.StepS);
        if (Player != null && IsInstanceValid(Player) && Nature.Cells.Values.Any(c => c.BurningS > 0 && c.Position.DistanceTo(Player.GlobalPosition) < NatureLaws.PlayerBurnM))
            ApplyPlayerDamage(NatureLaws.PlayerBurnDamage, PlayerDamageSource.Fire);
        if (LookHook != null) LookHook.Wind = new Vector2(NatureWeather.WindMps.X, NatureWeather.WindMps.Z);
        _natureClockMoved = true;
        var fireEnded = hadFire && Nature.Fire == null;
        if (changed || fireEnded)
        {
            _natureDirty = true;
            NatureChanged?.Invoke();
        }
        return fireEnded;
    }

    /// <summary>
    /// The flower trail behind the Gubble, with its caster's permission now: the island's rules (a keyed companion trail
    /// only under the pack it was approved under) and the caster's role. A companion that is no longer allowed to change
    /// the room (a visitor now) has its trail turned off, not just paused.
    /// </summary>
    private bool PlantTrail()
    {
        if (!Nature.TrailEnabled || Nature.Fire != null) return false;
        var role = Nature.TrailPrincipal;
        if (role == "companion" && !Authority.Call("may_change", CompanionPrincipal).AsBool())
        {
            Nature.TrailEnabled = false;
            NatureNotice = "The Gubble's flower trail stopped: it may not change the island now.";
            return true;
        }
        var ability = Rules?.Ability("bloom");
        if (ability == null || !ability.CastBy.Contains(role) || (role == "companion" && ability.Tier == "keyed_yes" && Nature.TrailRulesSha256 != Rules!.Sha256)) return false;
        SmallPlayerController? walker = Companion != null && IsInstanceValid(Companion) ? Companion : Player;
        if (walker == null || !IsInstanceValid(walker)) return false;
        var at = walker.GlobalPosition;
        if (_natureTrailAt is { } last && at.DistanceTo(last) < NatureLaws.TrailSpacingM) return false;
        var intensity = ability.Params.TryGetValue("intensity", out var setting) ? (float)setting.Default : 0.6f;
        var planted = Nature.Bloom(at, NatureLaws.TrailRadiusM, NatureLaws.TrailLifeS, intensity, (x, z) => SampleNatureCell(x, z, at.Y), step: null);
        // The marker moves with the flowers it marks: both are this step's state in memory, saved together.
        _natureTrailAt = at;
        return planted > 0;
    }

    /// <summary>
    /// Objects near burning ground catch, as the fire rule says: a stock object burns down (its pose at that moment kept for
    /// a later rebuild), a creation the fire was approved to scorch is scorched (its repair quote taken from its design).
    /// Each change is recorded in the fire's undo step.
    /// </summary>
    private bool BurnNatureObjects(float seconds)
    {
        var fire = Nature.Fire;
        if (fire == null) return false;
        var burning = Nature.Cells.Values.Where(c => c.BurningS > 0).ToList();
        var step = Nature.Undo.LastOrDefault(s => s.Revision == fire.Revision);
        var changed = false;
        foreach (var target in FireTargets(fire.Center, fire.RadiusM).Where(t => t.Outcome != FireOutcome.Fireproof))
        {
            if (target.Outcome == FireOutcome.Scorches && (!fire.AuthorizedCreations.TryGetValue(target.Id, out var revision) || EntityRevision(target.Id) != revision)) continue;
            var catching = target.Bounds.Grow(NatureLaws.ObjectCatchM);
            if (!burning.Any(c => catching.HasPoint(c.Position + Vector3.Up * 0.04f))) continue;
            Nature.RememberObject(step, target.Id);
            if (!Nature.Objects.TryGetValue(target.Id, out var state))
            {
                state = new NatureObject { Id = target.Id, Creation = target.Kind == "creation" };
                if (state.Creation) QuoteRepair(state);
                Nature.Objects[target.Id] = state;
            }
            state.Burn = Math.Min(1, state.Burn + seconds * NatureLaws.ObjectBurnPerIntensityS * fire.Intensity * (1 - Nature.Weather.Rain01));
            if (state.BurnedDown && state.PositionM == null && Room.Objects.FirstOrDefault(o => o.Id == target.Id) is { } item)
            {
                var (position, rotation) = PoseOf(item, ObjectPoses());
                state.PositionM = position;
                state.Rotation = rotation;
            }
            changed = true;
        }
        if (changed) ApplyNatureObjects();
        return changed;
    }

    /// <summary>A scorched creation's repair quote: one unit per authored part, from the hash-pinned design (never from a request).</summary>
    private void QuoteRepair(NatureObject state)
    {
        var source = KernelJson.ToJson(Instance(state.Id)!["source"])!;
        state.SourceHash = CanonicalJson.Sha256Hex(CanonicalJson.Bytes(source));
        foreach (var part in source["parts"]!.AsArray())
        {
            var material = part!["material"]!.GetValue<string>();
            state.RepairCost[material] = state.RepairCost.GetValueOrDefault(material) + 1;
        }
    }

    /// <summary>A bounded batch of light probes (up to four rays each) per tick, every cell in turn; without a look, the default light.</summary>
    private void ProbeNatureLight()
    {
        if (Nature.Cells.Count == 0) return;
        if (_natureLightQueue.Count == 0) foreach (var key in Nature.Cells.Keys) _natureLightQueue.Enqueue(key);
        for (var probes = 0; probes < NatureLaws.LightProbesPerTick && _natureLightQueue.TryDequeue(out var key);)
        {
            if (!Nature.Cells.TryGetValue(key, out var cell)) continue;
            cell.Light = Math.Clamp(LookHook?.LightLevelAt(cell.Position + Vector3.Up * 0.02f) ?? NatureLaws.DefaultLight, 0, 2);
            probes++;
        }
    }

    /// <summary>
    /// Saves the simulation's nature now, with the team block in the same write when it waits too: the throttle, a fire's
    /// end, and exit. It never moves the room revision. While an earlier save is offered and the player has not answered,
    /// nothing is written (it would make a save of this room and withdraw the offer), as for the team block.
    /// </summary>
    private void SaveNature()
    {
        _natureSaveClock = 0;
        if (!NatureConfigured || _natureUnavailable || Authority == null || !Authority.Call("is_ready").AsBool()) return;
        if (_migrationOffer != null && !Godot.FileAccess.FileExists(SavePath)) return;
        Nature.TrimUndo();
        var team = _teamDirty ? KernelJson.ToVariant(ExportTeam(_notes)) : default;
        var saved = Authority.Call("set_nature", KernelJson.ToVariant(NatureBlock.Pack(Nature)), team).AsGodotDictionary();
        if (!saved["ok"].AsBool()) return;
        _natureDirty = false;
        _natureClockMoved = false;
        if (_teamDirty) _teamDirty = false;
    }

    /// <summary>On exit: whatever changed since the last save, the weather clock included.</summary>
    private void SaveNatureOnExit()
    {
        if (_natureDirty || _natureClockMoved) SaveNature();
    }

    // ---- the ground under a cell ----

    /// <summary>
    /// The ground at a cell, from the room's physics: the first walkable face below the probe (started just above the
    /// intended ground, so a roof or an arbor never replaces the soil below it), its material and whether it is fertile.
    /// Water over it, a steep face or a fireproof place gives nothing.
    /// </summary>
    private NatureCell? SampleNatureCell(int x, int z, float? nearY = null)
    {
        if (NatureSampleForTests != null) return NatureSampleForTests(x, z);
        if (!IsInsideTree()) return null;
        var top = Math.Min(Room.Bounds.End.Y - 0.002f, nearY is { } y ? y + 0.1f : Room.Bounds.End.Y - 0.002f);
        var at = new Vector3((x + 0.5f) * NatureState.CellM, top, (z + 0.5f) * NatureState.CellM);
        if (at.X < Room.Bounds.Position.X || at.Z < Room.Bounds.Position.Z || at.X > Room.Bounds.End.X || at.Z > Room.Bounds.End.Z) return null;
        using var query = PhysicsRayQueryParameters3D.Create(at, at with { Y = Room.Bounds.Position.Y - 0.5f }, RoomBuilder.WorldLayer);
        query.HitBackFaces = false;
        using var hit = GetViewport().World3D.DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || hit["normal"].AsVector3().Y < 0.819f) return null;
        var point = hit["position"].AsVector3();
        var (material, surface) = NatureSurface(hit["collider"].AsGodotObject() as Node);
        if (material == "") return null;
        var fertile = surface.StartsWith("shell:", StringComparison.Ordinal) && material is "grass" or "soil";
        var fuel = material is "grass" or "foliage" ? NatureLaws.GrassFuel : material is "wood" or "bark" ? NatureLaws.WoodFuel : 0;
        if (material is "fabric" or "paper" or "plastic" or "cloth" or "rubber" or "leaf") fuel = NatureLaws.LightFuel;
        if (RoomWater.SurfaceAbove(GetViewport().World3D.DirectSpaceState, point + Vector3.Up * 0.004f, Room.Bounds.Size.Y + 1) is { } water && water > point.Y + 0.005f) return null;
        var cell = new NatureCell
        {
            X = x, Z = z, Y = point.Y, Surface = surface, Material = material, Fertile = fertile, Fuel = fuel,
            Moisture = NatureLaws.FreshMoisture + NatureWeather.Rain01 * NatureLaws.RainMoisture,
        };
        if (NatureFireproof(cell)) return null;
        if (Nature.Cells.TryGetValue(cell.Key, out var current))
        {
            cell.Moisture = current.Moisture;
            cell.Fuel = Math.Max(cell.Fuel, current.Growth * NatureLaws.FlowerFuel);
        }
        return cell;
    }

    private (string Material, string Id) NatureSurface(Node? collider)
    {
        for (var node = collider; node != null; node = node.GetParent())
        {
            if (node.HasMeta("material_role")) return (node.GetMeta("material_role").AsString(), node.HasMeta("entity_id") ? node.GetMeta("entity_id").AsString() : "");
            if (node.HasMeta("entity_id") && node.GetMeta("entity_id").AsString() is { } id && id.StartsWith("obj:", StringComparison.Ordinal))
            {
                var item = Room.Objects.FirstOrDefault(o => o.Id == id);
                var material = item?.Asset.Materials.FirstOrDefault(m => FlammableMaterials.Contains(m.Role))?.Role ?? item?.Asset.Materials.FirstOrDefault()?.Role ?? "";
                return (material, id);
            }
            if (node.GetParent() != Runtime) continue;
            foreach (var pair in Runtime.Get("assemblies").AsGodotDictionary())
            {
                if (pair.Value.AsGodotObject() != node) continue;
                var creationId = pair.Key.AsString();
                var instance = Instance(creationId);
                if (instance == null) return ("", "");
                var parts = instance["source"].AsGodotDictionary()["parts"].AsGodotArray();
                return (parts.Select(p => p.AsGodotDictionary()["material"].AsString()).FirstOrDefault(m => m is "wood" or "leaf") ?? "stone", creationId);
            }
        }
        return ("", "");
    }

    // ---- the scene, derived ----

    private Node3D? NatureNode(string id)
    {
        if (id.StartsWith("obj:", StringComparison.Ordinal)) return ObjectNode(id);
        if (Runtime == null || !IsInstanceValid(Runtime)) return null;
        return Runtime.Get("assemblies").AsGodotDictionary().TryGetValue(id, out var assembly) ? assembly.AsGodotObject() as Node3D : null;
    }

    /// <summary>
    /// The scene follows nature, and only when what it shows changes (a burn, a repair, an undo, a load, or the runtime
    /// rebuilding a scorched creation's assembly): a stock object that burned down is hidden and loses its collision and
    /// its trees' climbing collision; a scorched creation is darkened and its triggers are off. A partly burnt object's
    /// collision is never touched, and neither is a held one's: the hands own that (TakeHold sets it to layer 0).
    /// </summary>
    private void ApplyNatureObjects()
    {
        if (Runtime == null || !IsInstanceValid(Runtime)) return;
        var burned = Nature.Objects.Values.Where(o => o.BurnedDown).Select(o => o.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        var scorched = Nature.Objects.Values.Where(o => o.Creation && o.Burn > 0).Select(o => o.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        var key = string.Join(",", burned) + "|" + string.Join(",", scorched.Select(id => id + "@" + (NatureNode(id)?.GetInstanceId() ?? 0)));
        if (key == _natureSceneKey) return;
        _natureSceneKey = key;
        var disabled = new Godot.Collections.Dictionary();
        foreach (var id in scorched) disabled[id] = true;
        Runtime.Set("nature_disabled_creations", disabled);
        var held = _held.Values.Select(h => h.Target).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _natureShown.Concat(burned).Concat(scorched).Distinct(StringComparer.Ordinal).ToList())
        {
            var node = NatureNode(id);
            if (node == null || !IsInstanceValid(node) || held.Contains(id)) continue;
            if (id.StartsWith("obj:", StringComparison.Ordinal))
            {
                var hidden = burned.Contains(id);
                node.Visible = !hidden;
                if (node is CollisionObject3D body) SetNatureCollision(body, hidden);
            }
            else SetScorch(node, scorched.Contains(id));
        }
        _natureShown.Clear();
        _natureShown.UnionWith(burned.Concat(scorched));
        UpdateClimbCollision(burned);
    }

    /// <summary>A burned-down body loses its collision; restored as it was when the burn is undone.</summary>
    private void SetNatureCollision(CollisionObject3D body, bool hidden)
    {
        var key = body.GetInstanceId();
        if (hidden)
        {
            _natureLayers.TryAdd(key, body.CollisionLayer);
            body.CollisionLayer = 0;
        }
        else if (_natureLayers.Remove(key, out var layer)) body.CollisionLayer = layer;
    }

    private void SetScorch(Node node, bool scorch)
    {
        if (node is MeshInstance3D mesh)
        {
            var key = mesh.GetInstanceId();
            _natureMaterials.TryAdd(key, mesh.MaterialOverlay);
            _natureScorchMaterial ??= new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.13f, 0.09f, 0.72f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 1 };
            mesh.MaterialOverlay = scorch ? _natureScorchMaterial : _natureMaterials[key];
        }
        foreach (var child in node.GetChildren()) SetScorch(child, scorch);
    }

    /// <summary>
    /// A burnt tree's climbing collision goes with it. The landscape exporter merges every tree's climbing pole and crown
    /// caps into collision-only shell parts (shell:tree_climb_*): each of their faces inside a burned-down object's bounds is
    /// left out, and all come back when nothing there is burned.
    /// </summary>
    private void UpdateClimbCollision(List<string> burned)
    {
        if (GetParent()?.GetNodeOrNull("Room/Shell") is not { } shell) return;
        var gone = burned.Select(id => Room.Objects.FirstOrDefault(o => o.Id == id)).Where(o => o != null).Select(o =>
        {
            var record = Nature.Objects[o!.Id];
            var (position, rotation) = record.PositionM is { } at && record.Rotation is { } turn ? (at, turn) : PoseOf(o, ObjectPoses());
            return Sandbox.SandboxPhysics.Bounds(position, rotation, o.Asset.DimensionsM * o.Scale).Grow(0.02f);
        }).ToList();
        foreach (var part in shell.GetChildren().OfType<CollisionObject3D>().Where(n => n.HasMeta("entity_id") && n.GetMeta("entity_id").AsString().StartsWith("shell:tree_climb", StringComparison.Ordinal)))
        foreach (var holder in part.GetChildren().OfType<CollisionShape3D>())
        {
            if (holder.Shape is not ConcavePolygonShape3D shape) continue;
            var id = holder.GetInstanceId();
            if (!_climbFaces.TryGetValue(id, out var faces)) _climbFaces[id] = faces = shape.GetFaces();
            var kept = new List<Vector3>(faces.Length);
            for (var i = 0; i + 2 < faces.Length; i += 3)
            {
                var centroid = holder.GlobalTransform * ((faces[i] + faces[i + 1] + faces[i + 2]) / 3f);
                if (gone.Any(b => b.HasPoint(centroid))) continue;
                kept.Add(faces[i]);
                kept.Add(faces[i + 1]);
                kept.Add(faces[i + 2]);
            }
            if (kept.Count == shape.GetFaces().Length) continue;
            holder.Shape = new ConcavePolygonShape3D { Data = kept.ToArray(), BackfaceCollision = shape.BackfaceCollision };
        }
    }
}
