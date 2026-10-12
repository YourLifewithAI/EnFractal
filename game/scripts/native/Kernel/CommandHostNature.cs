using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnFractal.Native.Room;

namespace EnFractal.Native.Kernel;

/// <summary>Host-owned Bloom, weather and bounded Fire. Every publication follows the sidecar's atomic save.</summary>
public partial class CommandHost
{
    public NatureState Nature { get; private set; } = new();
    public NatureWeather NatureWeather => Nature.Weather;
    public string NatureNotice { get; private set; } = "";
    public bool NatureConfigured { get; private set; }
    public event Action? NatureChanged;
    private INatureStore? _natureStore;
    private bool _natureUnavailable;
    private double _natureElapsed;
    private Vector3? _natureTrailAt;
    private Aabb[] _natureProtected = Array.Empty<Aabb>();
    private readonly Dictionary<string, NaturePreview> _naturePreviews = new();
    private readonly Dictionary<ulong, uint> _natureLayers = new();
    private readonly Dictionary<ulong, Material?> _natureMaterials = new();
    private readonly HashSet<string> _natureDerivedIds = new();
    private StandardMaterial3D? _natureScorchMaterial;
    private sealed record NaturePreview(string Principal, string Signature, int NatureRevision, int AuthorityRevision, DateTime Expires);
    internal Func<int, int, NatureCell?>? NatureSampleForTests { get; set; }

    public void ConfigureNature(INatureStore? store = null)
    {
        _natureStore = store ?? new LocalNatureStore(ProjectSettings.GlobalizePath(SavePath.GetBaseDir() + "/nature.json"));
        try
        {
            Nature = _natureStore.Load(Room.RoomId, Room.ManifestSha256);
            NatureConfigured = true; _natureUnavailable = false; NatureNotice = "";
            ApplyNatureObjects();
        }
        catch (NatureStateException e) { NatureConfigured = true; _natureUnavailable = true; NatureNotice = e.Message; }
    }

    public JsonObject NatureSnapshot() => new()
    {
        ["nature_revision"] = Nature.Revision, ["trail_enabled"] = Nature.TrailEnabled,
        ["weather"] = new JsonObject { ["pattern"] = NatureWeather.Pattern, ["rain"] = NatureWeather.Rain01, ["wind_mps"] = KernelJson.Vector(NatureWeather.WindMps) },
        ["planted_cells"] = Nature.Cells.Values.Count(c => c.Growth > 0), ["burning_cells"] = Nature.Cells.Values.Count(c => c.BurningS > 0),
        ["wallet"] = new JsonObject(Nature.Wallet.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)JsonValue.Create(p.Value)))),
        ["scorched"] = new JsonArray(Nature.Objects.Values.Where(o => o.Creation && o.Burn > 0).Select(o => (JsonNode?)JsonValue.Create(o.Id)).ToArray()),
        ["can_undo"] = Nature.Undo != null && Nature.Undo.AuthorityRevision == Revision,
        ["notice"] = NatureNotice,
    };

    public JsonObject PlayerNaturePreview(string capability, Vector3 point, float radius = .25f, float duration = 180) =>
        PlayerCommand("nature.cast", NatureCastArgs(capability, point, radius, duration), preview: true);

    private static JsonObject NatureCastArgs(string capability, Vector3 point, float radius, float duration)
    {
        if (capability == "fire") { radius = Math.Min(radius <= .25f ? 1.5f : radius, 1.5f); duration = Math.Min(duration, 30); }
        return new JsonObject { ["capability"] = capability, ["center_m"] = KernelJson.Vector(point), ["radius_m"] = radius, ["duration_s"] = duration };
    }
    public JsonObject PlayerNatureCast(string capability, Vector3 point, float radius = .25f, float duration = 180, string? previewId = null)
    {
        var args = NatureCastArgs(capability, point, radius, duration);
        if (previewId != null) args["preview_id"] = previewId;
        return PlayerCommand("nature.cast", args);
    }
    public JsonObject PlayerNatureTrail(bool enabled) => PlayerCommand("nature.trail", new JsonObject { ["enabled"] = enabled });
    public JsonObject PlayerNatureRepair(string target) => PlayerCommand("nature.repair", new JsonObject { ["target"] = target });
    public JsonObject PlayerNatureUndo() => PlayerCommand("room.undo", new JsonObject { ["to_revision"] = Math.Max(0, Nature.Revision - 1) });

    private static void CheckNatureArgs(string op, JsonElement args)
    {
        var allowed = op switch
        {
            "nature.cast" => new[] { "capability", "center_m", "radius_m", "duration_s", "preview_id" },
            "nature.trail" => new[] { "enabled" }, "nature.repair" => new[] { "target" }, _ => Array.Empty<string>(),
        };
        if (args.ValueKind != JsonValueKind.Object) throw new Refusal("invalid_args", "Nature arguments must be an object.", "$.args");
        foreach (var p in args.EnumerateObject()) if (!allowed.Contains(p.Name)) throw new Refusal("field_unknown", "This argument is not part of the operation.", "$.args." + p.Name);
        if (op == "nature.cast")
        {
            if (Str(args, "capability") is not ("bloom" or "fire")) throw new Refusal("unsupported_capability", "Choose Bloom or Fire.", "$.args.capability");
            if (!args.TryGetProperty("center_m", out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 3 || v.EnumerateArray().Any(a => a.ValueKind != JsonValueKind.Number || !a.TryGetDouble(out var number) || !double.IsFinite(number) || Math.Abs(number) > 1000)) throw new Refusal("invalid_args", "The center is a finite point in metres.", "$.args.center_m");
            foreach (var name in new[] { "radius_m", "duration_s" }) if (!args.TryGetProperty(name, out var n) || n.ValueKind != JsonValueKind.Number || !n.TryGetDouble(out var value) || !double.IsFinite(value) || value <= 0) throw new Refusal("invalid_args", "Radius and duration must be positive finite numbers.", "$.args." + name);
            if (args.TryGetProperty("preview_id", out var id) && (id.ValueKind != JsonValueKind.String || id.GetString()!.Length != 32)) throw new Refusal("invalid_args", "Use the preview id returned by this host.", "$.args.preview_id");
        }
        else if (op == "nature.trail" && (!args.TryGetProperty("enabled", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))) throw new Refusal("invalid_args", "Choose whether the trail is enabled.", "$.args.enabled");
        else if (op == "nature.repair" && (Str(args, "target") is not { } target || !CreationId.IsMatch(target))) throw new Refusal("invalid_args", "Repair names one creation.", "$.args.target");
    }

    private JsonObject NatureCommand(JsonElement root, string principal, string fingerprint, string? approvedBy)
    {
        var op = Op(root); var action = Str(root, "action_id")!; var args = root.GetProperty("args");
        var preview = root.TryGetProperty("preview", out var p) && p.ValueKind == JsonValueKind.True;
        if (!NatureConfigured || _natureUnavailable) throw new Refusal("not_ready", NatureNotice.Length > 0 ? NatureNotice : "Nature has not loaded for this room.");
        if (ReplayNature(op, principal, action, fingerprint) is { } prior) return prior;
        if (!preview && Nature.Receipts.Count >= NatureState.MaxReceipts) throw new Refusal("receipt_limit", "The nature receipt ledger is full. No change was made.");
        RefreshNatureProtection();
        var next = Nature.Copy();
        var data = new JsonObject();
        var affected = new List<string>();
        if (op == "nature.cast")
        {
            var capability = Str(args, "capability")!; var center = KernelJson.ReadVector(args.GetProperty("center_m"));
            var radius = (float)args.GetProperty("radius_m").GetDouble(); var duration = (float)args.GetProperty("duration_s").GetDouble();
            var ability = Rules?.Ability(capability);
            if (ability == null) throw new Refusal("unsupported_capability", "This island does not grant that nature ability.");
            if (!ability.CastBy.Contains(principal == PlayerPrincipal ? "player" : "companion")) throw new Refusal("permission_denied", "This island does not allow this caster to use that ability.");
            if (radius > Math.Min(capability == "fire" ? 1.5f : .6f, (float)ability.AreaRadiusMaxM) || duration > Math.Min(capability == "fire" ? 60 : 600, ability.DurationMaxS)) throw new Refusal("invalid_args", "That radius or duration exceeds the island's nature limits.");
            if (!Room.Bounds.Grow(.02f).HasPoint(center)) throw new Refusal("out_of_bounds", "Choose a point inside this island.");
            if (Companion == null || Companion.GlobalPosition.DistanceTo(center) > ability.ReachM) throw new Refusal("out_of_bounds", "Bring the Gubble closer before casting.");
            if (!TeamSeesSpot(center, principal)) throw new Refusal("target_not_found", "The team must see the place the Gubble will affect.");
            if (next.Fire != null) throw new Refusal("target_busy", "Let the current fire finish or undo it before another nature cast.");
            NatureCell? Sample(int x, int z) => SampleNatureCell(x, z, center.Y);
            var samples = NatureState.Disk(center, radius).Select(g => Sample(g.X, g.Z)).Where(c => c != null).Select(c => c!).ToArray();
            var eligible = samples.Where(c => capability == "bloom" ? c.Fertile : c.Fuel > 0 && c.Moisture < .8f).ToArray();
            if (eligible.Length == 0) throw new Refusal("invalid_args", capability == "bloom" ? "Bloom needs walkable soil or grass, above water." : "There is no dry fuel at that spot.");
            var targets = capability == "fire" ? NatureTargets(center, radius) : new List<JsonObject>();
            if (targets.Count > 64) throw new Refusal("invalid_args", "That fire could touch too many objects. Choose a smaller area.");
            affected.AddRange(targets.Select(t => t["id"]!.GetValue<string>()));
            var creations = targets.Where(t => t["kind"]!.GetValue<string>() == "creation").ToDictionary(t => t["id"]!.GetValue<string>(), t => EntityRevision(t["id"]!.GetValue<string>()));
            var signature = CanonicalJson.Sha256Hex(CanonicalJson.Bytes(new JsonObject { ["capability"] = capability, ["center_m"] = KernelJson.Vector(center), ["radius_m"] = radius, ["duration_s"] = duration, ["targets"] = new JsonArray(targets.Select(t => (JsonNode?)t.DeepClone()).ToArray()) }));
            data["affected_ids"] = new JsonArray(affected.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            data["cell_count"] = eligible.Length;
            data["cells"] = new JsonArray(eligible.Take(200).Select(c => (JsonNode?)KernelJson.Vector(c.Position)).ToArray());
            data["cells_truncated"] = eligible.Length > 200; data["max_burning_cells"] = 200;
            data["center_m"] = KernelJson.Vector(center); data["radius_m"] = radius;
            var tierApproval = principal != PlayerPrincipal && ability.Tier == "keyed_yes";
            data["requires_approval"] = creations.Count > 0 || tierApproval;
            if (preview)
            {
                foreach (var key in _naturePreviews.Where(p => p.Value.Expires <= Clock()).Select(p => p.Key).ToArray()) _naturePreviews.Remove(key);
                while (_naturePreviews.Count >= 32) _naturePreviews.Remove(_naturePreviews.Keys.First());
                var token = Guid.NewGuid().ToString("N");
                _naturePreviews[token] = new NaturePreview(principal, signature, Nature.Revision, Revision, Clock() + ApprovalLifetime);
                data["preview_id"] = token;
            }
            else
            {
                if (capability == "fire")
                {
                    var token = Str(args, "preview_id");
                    if (token == null || !_naturePreviews.TryGetValue(token, out var planned) || planned.Principal != principal || planned.Signature != signature || planned.Expires <= Clock() || planned.NatureRevision != Nature.Revision || planned.AuthorityRevision != Revision) throw new Refusal("revision_conflict", "Preview this fire again before casting: the place or its permission has changed.", "$.args.preview_id");
                    if (creations.Count > 0 && approvedBy == null)
                        return Hold(root, principal, action, fingerprint, op, $"Fire could scorch {creations.Count} unlocked creation(s). Their designs remain repairable. Approve this exact area or deny it.", creations);
                }
                if (tierApproval && approvedBy == null)
                    return Hold(root, principal, action, fingerprint, op, $"The Gubble asks to cast {capability} in this area.", creations);
                next.BeginUndo(capability == "fire" ? "Fire" : "Bloom", Revision, principal);
                var count = capability == "bloom" ? next.Bloom(center, radius, duration, Sample) : next.Ignite(center, radius, duration, Revision, creations, Sample);
                if (count == 0) throw new Refusal("invalid_args", "Nothing can take hold at the center of that area.");
                data["started_cells"] = count;
            }
        }
        else if (op == "nature.trail")
        {
            if (next.Fire != null) throw new Refusal("target_busy", "Let the current fire finish or undo it first.");
            var enabled = args.GetProperty("enabled").GetBoolean();
            var role = principal == PlayerPrincipal ? "player" : "companion";
            var ability = Rules?.Ability("bloom");
            if (enabled && ability == null) throw new Refusal("unsupported_capability", "This island does not grant Bloom.");
            if (enabled && !ability!.CastBy.Contains(role)) throw new Refusal("permission_denied", "This island does not allow this caster to use Bloom.");
            var requiresApproval = enabled && role == "companion" && ability!.Tier == "keyed_yes";
            data["enabled"] = enabled; data["requires_approval"] = requiresApproval;
            if (!preview && requiresApproval && approvedBy == null)
                return Hold(root, principal, action, fingerprint, op, "The Gubble asks to leave a flower trail.", new());
            if (!preview) { next.BeginUndo("Flower trail", Revision, principal); next.TrailEnabled = enabled; next.TrailPrincipal = role; next.TrailRulesSha256 = Rules?.Sha256 ?? ""; }
        }
        else if (op == "nature.repair")
        {
            var id = Str(args, "target")!;
            var instance = Instance(id) ?? throw new Refusal("target_not_found", "That creation is no longer in the room.");
            if (Authority.Call("is_locked", id).AsBool()) throw new Refusal("target_protected", "Only the player can unlock this protected creation.");
            if (!next.Objects.TryGetValue(id, out var item) || !item.Creation || item.Burn <= 0) throw new Refusal("invalid_args", "That creation does not need repair.");
            var source = KernelJson.ToJson(instance["source"])!;
            if (item.SourceHash != CanonicalJson.Sha256Hex(CanonicalJson.Bytes(source))) throw new Refusal("revision_conflict", "This creation's design changed; its old repair quote no longer applies.");
            if (next.Fire != null) throw new Refusal("target_busy", "Let the fire finish or undo it before repairing.");
            data["cost"] = Mode == "creative" ? new JsonObject() : new JsonObject(item.RepairCost.Select(c => KeyValuePair.Create(c.Key, (JsonNode?)JsonValue.Create(c.Value))));
            if (Mode != "creative" && item.RepairCost.Any(c => next.Wallet.GetValueOrDefault(c.Key) < c.Value)) throw new Refusal("invalid_args", "The team's material supply cannot cover this repair yet.");
            if (!preview && principal != PlayerPrincipal && instance["owner_id"].AsString() == PlayerPrincipal && approvedBy == null)
                return Hold(root, principal, action, fingerprint, op, $"The Gubble asks to repair {id} using the team's materials.", new() { [id] = EntityRevision(id) });
            affected.Add(id);
            if (!preview)
            {
                next.BeginUndo("Repair", Revision, principal); next.RememberObject(id);
                if (Mode != "creative") foreach (var cost in item.RepairCost) next.Wallet[cost.Key] -= cost.Value;
                next.Objects.Remove(id);
            }
        }
        else throw new Refusal("unsupported_capability", "That nature operation is not available.");
        var result = Result(op, principal, action, null, true); result["preview"] = preview; result["data"] = data;
        if (affected.Count > 0) result["affected"] = new JsonArray(affected.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        if (approvedBy != null) result["approved_by"] = approvedBy;
        if (!preview)
        {
            // The detailed geometry belongs to the preview; durable receipts keep the bounded summary.
            data.Remove("cells"); data.Remove("cells_truncated");
            CommitNature(next, result, principal, action, fingerprint);
        }
        return result;
    }

    private JsonObject NatureUndo(JsonElement root, string principal, string fingerprint, string? approvedBy)
    {
        var action = Str(root, "action_id")!;
        if (!NatureConfigured || _natureUnavailable) throw new Refusal("not_ready", "Nature is unavailable.");
        if (ReplayNature("room.undo", principal, action, fingerprint) is { } replay) return replay;
        if (Nature.Undo == null) throw new Refusal("invalid_args", "There is no nature change to undo.");
        if (Nature.Undo.AuthorityRevision != Revision) throw new Refusal("revision_conflict", "A newer room edit exists. Nature will not undo across that edit.");
        if (root.GetProperty("args").GetProperty("to_revision").GetInt32() != Nature.Revision - 1) throw new Refusal("revision_conflict", "Undo names the revision before the most recent nature change.");
        if (Nature.Undo.Objects.Keys.Any(id => Authority.Call("is_locked", id).AsBool())) throw new Refusal("target_protected", "Unlock the protected object before undoing its nature change.");
        var preview = root.TryGetProperty("preview", out var p) && p.ValueKind == JsonValueKind.True;
        var needsApproval = principal != PlayerPrincipal && Nature.Undo.Principal != principal;
        if (!preview && needsApproval && approvedBy == null)
            return Hold(root, principal, action, fingerprint, "room.undo", "The Gubble asks to undo the player's latest nature change.", Nature.Undo.Objects.Keys.ToDictionary(id => id, EntityRevision));
        var result = Result("room.undo", principal, action, null, true); result["preview"] = preview;
        result["data"] = new JsonObject { ["undone"] = Nature.Undo.Label, ["nature_revision"] = Nature.Revision + 1, ["requires_approval"] = needsApproval };
        if (approvedBy != null) result["approved_by"] = approvedBy;
        if (!preview) { var candidate = Nature.Copy(); candidate.UndoLast(); CommitNature(candidate, result, principal, action, fingerprint); }
        return result;
    }

    private JsonObject? ReplayNature(string op, string principal, string action, string fingerprint)
    {
        if (!Nature.Receipts.TryGetValue(principal + "|" + action, out var record)) return null;
        if (record["fingerprint"]!.GetValue<string>() != fingerprint) throw new Refusal("action_id_conflict", "This action id was already used for different content.", "$.action_id");
        var result = (JsonObject)record["result"]!.DeepClone(); result["replayed"] = true; return result;
    }
    private JsonObject? LookupNature(string principal, string action) => Nature.Receipts.TryGetValue(principal + "|" + action, out var record) ? new JsonObject { ["found"] = true, ["receipt"] = record["result"]!.DeepClone() } : null;

    private void CommitNature(NatureState candidate, JsonObject result, string principal, string action, string fingerprint)
    {
        if (candidate.Receipts.Count >= NatureState.MaxReceipts) throw new Refusal("receipt_limit", "The nature receipt ledger is full.");
        result["transient"] = false;
        (result["data"] as JsonObject ?? throw new InvalidOperationException())["nature_revision"] = candidate.Revision;
        candidate.Receipts[principal + "|" + action] = new JsonObject { ["fingerprint"] = fingerprint, ["result"] = result.DeepClone() };
        PublishNature(candidate);
    }
    private void PublishNature(NatureState candidate)
    {
        try { _natureStore!.Save(candidate); }
        catch (NatureStateException e) { NatureNotice = e.Message; throw new Refusal("not_ready", e.Message, retryable: true); }
        Nature = candidate; NatureNotice = "";
        if (LookHook != null) LookHook.Wind = new Vector2(NatureWeather.WindMps.X, NatureWeather.WindMps.Z);
        ApplyNatureObjects(); NatureChanged?.Invoke();
    }

    public void AdvanceNature(double delta)
    {
        if (!NatureConfigured || _natureUnavailable || !double.IsFinite(delta) || delta <= 0) return;
        _natureElapsed += Math.Min(delta, 1);
        if (_natureElapsed < .5) return;
        _natureElapsed = 0;
        var next = Nature.Copy(); RefreshNatureProtection();
        var position = Companion != null && IsInstanceValid(Companion) ? Companion.GlobalPosition : Player?.GlobalPosition;
        NatureCell? Sample(int x, int z) => SampleNatureCell(x, z, next.Cells.TryGetValue(NatureState.Key(x, z), out var cell) ? cell.Y : next.Fire?.Y ?? position?.Y);
        next.Step(.5f, Sample, NatureFireproof, at => LookHook?.LightLevelAt(at) ?? .6f);
        var trailAbility = Rules?.Ability("bloom");
        var trailPermitted = trailAbility != null && trailAbility.CastBy.Contains(next.TrailPrincipal) && (next.TrailPrincipal == "player" || trailAbility.Tier != "keyed_yes" || next.TrailRulesSha256 == Rules!.Sha256);
        if (next.TrailEnabled && trailPermitted && next.Fire == null && position is { } at && (_natureTrailAt == null || at.DistanceTo(_natureTrailAt.Value) >= .07f))
        {
            next.Bloom(at, .07f, 120, (x, z) => SampleNatureCell(x, z, at.Y), remember: false); _natureTrailAt = at;
        }
        BurnNatureObjects(next, .5f);
        try
        {
            PublishNature(next);
            if (Player != null && next.Cells.Values.Any(c => c.BurningS > 0 && c.Position.DistanceTo(Player.GlobalPosition) < .12f)) ApplyPlayerDamage(6, PlayerDamageSource.Fire);
        }
        catch (Refusal) { /* The previous saved state remains authoritative and visible; retry on the next tick. */ }
    }

    private NatureCell? SampleNatureCell(int x, int z, float? nearY = null)
    {
        if (NatureSampleForTests != null) return NatureSampleForTests(x, z);
        if (!IsInsideTree()) return null;
        // Start just above the intended ground, so a roof or arbor cannot replace the soil below it.
        var at = new Vector3((x + .5f) * NatureState.CellM, Math.Min(Room.Bounds.End.Y - .002f, nearY is { } y ? y + .1f : Room.Bounds.End.Y - .002f), (z + .5f) * NatureState.CellM);
        if (at.X < Room.Bounds.Position.X || at.Z < Room.Bounds.Position.Z || at.X > Room.Bounds.End.X || at.Z > Room.Bounds.End.Z) return null;
        using var query = PhysicsRayQueryParameters3D.Create(at, at with { Y = Room.Bounds.Position.Y - .5f }, RoomBuilder.WorldLayer);
        query.HitBackFaces = false;
        using var hit = GetViewport().World3D.DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || hit["normal"].AsVector3().Y < .819f) return null;
        var point = hit["position"].AsVector3(); var collider = hit["collider"].AsGodotObject() as Node;
        var (material, surface) = NatureSurface(collider);
        if (material == "") return null;
        var fertile = surface.StartsWith("shell:", StringComparison.Ordinal) && material is "grass" or "soil";
        var fuel = material is "grass" or "foliage" ? .7f : material is "wood" or "bark" ? 1f : 0;
        if (material is "fabric" or "paper" or "plastic" or "cloth" or "rubber" or "leaf") fuel = .8f;
        if (RoomWater.SurfaceAbove(GetViewport().World3D.DirectSpaceState, point + Vector3.Up * .004f, Room.Bounds.Size.Y + 1) is { } water && water > point.Y + .005f) return null;
        var cell = new NatureCell { X = x, Z = z, Y = point.Y, Surface = surface, Material = material, Fertile = fertile, Fuel = fuel, Moisture = .35f + NatureWeather.Rain01 * .35f };
        if (NatureFireproof(cell)) return null;
        if (Nature.Cells.TryGetValue(cell.Key, out var current)) { cell.Moisture = current.Moisture; cell.Fuel = Math.Max(cell.Fuel, current.Growth * .35f); }
        return cell;
    }

    private (string Material, string Id) NatureSurface(Node? collider)
    {
        for (var node = collider; node != null; node = node.GetParent())
        {
            if (node.HasMeta("material_role")) return (node.GetMeta("material_role").AsString(), node.HasMeta("entity_id") ? node.GetMeta("entity_id").AsString() : "");
            if (node.HasMeta("entity_id") && node.GetMeta("entity_id").AsString() is { } id && id.StartsWith("obj:"))
            {
                var item = Room.Objects.FirstOrDefault(o => o.Id == id);
                var material = item?.Asset.Materials.FirstOrDefault(m => m.Role is "wood" or "bark" or "foliage" or "fabric" or "paper" or "plastic")?.Role ?? item?.Asset.Materials.FirstOrDefault()?.Role ?? "";
                return (material, id);
            }
            if (node.GetParent() == Runtime)
            {
                foreach (var pair in Runtime.Get("assemblies").AsGodotDictionary())
                {
                    if (pair.Value.AsGodotObject() != node) continue;
                    var creationId = pair.Key.AsString(); var instance = Instance(creationId);
                    if (instance == null) return ("", "");
                    var parts = instance["source"].AsGodotDictionary()["parts"].AsGodotArray();
                    var material = parts.Select(p => p.AsGodotDictionary()["material"].AsString()).FirstOrDefault(m => m is "wood" or "leaf") ?? "stone";
                    return (material, creationId);
                }
            }
        }
        return ("", "");
    }

    private void RefreshNatureProtection() => _natureProtected = Entities().Where(e => e["protected"]?.GetValue<bool>() == true).Select(NatureBounds).ToArray();
    private bool NatureFireproof(NatureCell cell) => _natureProtected.Any(b => b.Grow(.04f).HasPoint(cell.Position + Vector3.Up * .02f));
    private static Aabb NatureBounds(JsonObject entity)
    {
        var bounds = entity["bounds_m"]!; var min = NatureVector(bounds["min_m"]!); var max = NatureVector(bounds["max_m"]!); return new Aabb(min, max - min);
    }
    private static Vector3 NatureVector(JsonNode node) => new((float)node[0]!.GetValue<double>(), (float)node[1]!.GetValue<double>(), (float)node[2]!.GetValue<double>());
    private List<JsonObject> NatureTargets(Vector3 center, float radius) => Entities().Where(e => e["kind"]!.GetValue<string>() is "creation" or "object" && e["protected"]?.GetValue<bool>() != true && e["held_by"] == null && NatureBounds(e).Grow(radius).HasPoint(center) && NatureFlammable(e["id"]!.GetValue<string>())).ToList();
    private bool NatureFlammable(string id)
    {
        if (id.StartsWith("obj:")) return Room.Objects.FirstOrDefault(o => o.Id == id)?.Asset.Materials.Any(m => m.Role is "wood" or "bark" or "foliage" or "fabric" or "paper" or "plastic") == true;
        var source = Instance(id)?["source"].AsGodotDictionary();
        return source != null && source.ContainsKey("parts") && source["parts"].AsGodotArray().Any(p => p.AsGodotDictionary()["material"].AsString() is "wood" or "leaf");
    }
    private void BurnNatureObjects(NatureState next, float dt)
    {
        if (next.Fire == null) return;
        var burning = next.Cells.Values.Where(c => c.BurningS > 0).ToArray();
        foreach (var entity in NatureTargets(next.Fire.Center, next.Fire.RadiusM))
        {
            var id = entity["id"]!.GetValue<string>(); var creation = id.StartsWith("creation:");
            if (creation && (!next.Fire.AuthorizedCreations.TryGetValue(id, out var revision) || EntityRevision(id) != revision)) continue;
            var bounds = NatureBounds(entity).Grow(.08f);
            if (!burning.Any(c => bounds.HasPoint(c.Position + Vector3.Up * .04f))) continue;
            next.RememberObject(id);
            if (!next.Objects.TryGetValue(id, out var state))
            {
                state = new NatureObject { Id = id, Creation = creation };
                if (creation)
                {
                    var source = KernelJson.ToJson(Instance(id)!["source"])!;
                    state.SourceHash = CanonicalJson.Sha256Hex(CanonicalJson.Bytes(source));
                    foreach (var part in source["parts"]!.AsArray())
                    {
                        var material = part!["material"]!.GetValue<string>();
                        // One unit per authored part: captured from the trusted original design, never supplied by a repair request.
                        state.RepairCost[material] = state.RepairCost.GetValueOrDefault(material) + 1;
                    }
                }
                next.Objects[id] = state;
            }
            state.Burn = Math.Min(1, state.Burn + dt * .22f * (1 - next.Weather.Rain01));
        }
    }

    public bool IsNatureUnavailable(string id) => Nature.Objects.TryGetValue(id, out var state) && (state.Creation ? state.Burn > 0 : state.Burn >= 1);
    private Node3D? NatureNode(string id)
    {
        if (id.StartsWith("obj:")) return ObjectNode(id);
        var assemblies = Runtime.Get("assemblies").AsGodotDictionary();
        return assemblies.TryGetValue(id, out var assembly) ? assembly.AsGodotObject() as Node3D : null;
    }
    private void ApplyNatureObjects()
    {
        if (Runtime == null || !IsInstanceValid(Runtime)) return;
        var disabled = new Godot.Collections.Dictionary();
        foreach (var item in Nature.Objects.Values.Where(o => o.Creation && o.Burn > 0)) disabled[item.Id] = true;
        Runtime.Set("nature_disabled_creations", disabled);
        foreach (var id in Nature.Objects.Keys.Concat(_natureDerivedIds).Distinct().ToArray())
        {
            var node = NatureNode(id); if (node == null || !IsInstanceValid(node)) continue;
            var damage = Nature.Objects.GetValueOrDefault(id);
            var hidden = damage is { Creation: false, Burn: >= 1 };
            node.Visible = !hidden;
            SetNatureBodyView(node, hidden, damage is { Creation: true, Burn: > 0 });
            if (damage != null) _natureDerivedIds.Add(id); else _natureDerivedIds.Remove(id);
        }
    }
    private void SetNatureBodyView(Node node, bool hidden, bool scorch)
    {
        var key = node.GetInstanceId();
        if (node is CollisionObject3D body)
        {
            _natureLayers.TryAdd(key, body.CollisionLayer);
            body.CollisionLayer = hidden ? 0 : _natureLayers[key];
        }
        if (node is MeshInstance3D mesh)
        {
            _natureMaterials.TryAdd(key, mesh.MaterialOverlay);
            mesh.MaterialOverlay = scorch ? _natureScorchMaterial ??= new StandardMaterial3D { AlbedoColor = new Color(.18f, .13f, .09f, .72f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 1 } : _natureMaterials[key];
        }
        foreach (var child in node.GetChildren()) SetNatureBodyView(child, hidden, scorch);
    }
}
