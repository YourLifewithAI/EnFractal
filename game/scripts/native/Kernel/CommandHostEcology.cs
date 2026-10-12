using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnFractal.Native.Room;
using EnFractal.Native.Look;

namespace EnFractal.Native.Kernel;

public partial class CommandHost
{
    private JsonObject _ecology = EmptyEcology();
    private static JsonObject EmptyEcology() => new() { ["parts"] = new JsonObject(), ["seeds"] = new JsonObject() };
    private bool _ecologyStep;
    private double _ecologyClock;
    private readonly Dictionary<string, MeshInstance3D[]> _ecologyMeshes = new();
    private readonly Dictionary<string, Material?> _ecologyMaterials = new();
    private readonly Dictionary<string, FireVisual> _burnVisuals = new();
    private readonly Dictionary<string, (Vector3 Position, string Principal)> _boltHeat = new();
    public int BurningPartCount => _ecology["parts"]!.AsObject().Count(p => p.Value!["burn"]!.GetValue<bool>());
    public string EcologyNotice { get; private set; } = "";
    public JsonObject EcologySnapshot() => (JsonObject)_ecology.DeepClone();
    public int SeedsOf(string species) => _ecology["seeds"]![species]?.GetValue<int>() ?? 0;

    private static JsonObject FreshPart(EcologyPart p) => new() {
        ["impact_cool"] = 0f, ["fuel"] = p.Fuel, ["heat"] = 0f, ["scorch"] = 0f, ["cool"] = 0f, ["burn"] = false,
        ["planted"] = false, ["growth"] = 1f, ["harvest"] = 0f, ["by"] = "", ["incident"] = "" };
    private static float N(JsonObject p, string key) => p[key]?.GetValue<float>() ?? 0f;
    private JsonObject PartState(JsonObject data, EcologyPart part) => data["parts"]![part.Id] is JsonObject saved ? (JsonObject)saved.DeepClone() : FreshPart(part);
    private bool Protected(EcologyPart p, JsonObject state) => state["planted"]!.GetValue<bool>() ||
        Authority.Call("is_locked", p.Target).AsBool();

    private string? EcologyProblem(JsonNode? node)
    {
        if (node == null) return null;
        try
        {
            if (node is not JsonObject e || e.Count is not (2 or 3) || e.Any(p => p.Key is not ("parts" or "seeds" or "plantings")) || e["parts"] is not JsonObject parts || e["seeds"] is not JsonObject seeds || seeds.Count > 64) return "ecology shape";
            if (PlantingsProblem(e) is { } plantingProblem) return plantingProblem;
            var defs = Room.Ecology.Concat(PlantedParts(e)).ToDictionary(p => p.Id);
            if (parts.Count > defs.Count) return "ecology part count";
            var species = Room.Ecology.Where(p => p.Plant).Select(p => p.Species).ToHashSet();
            foreach (var (id, count) in seeds)
                if (!species.Contains(id) || !Whole(count, 99)) return "seed inventory";
            foreach (var (id, value) in parts)
            {
                if (!defs.TryGetValue(id, out var def) || value is not JsonObject s || s.Count is not (10 or 11) || s.Any(p => p.Key is not ("impact_cool" or "fuel" or "heat" or "scorch" or "cool" or "burn" or "planted" or "growth" or "harvest" or "by" or "incident"))) return "ecology part";
                if (s["impact_cool"] is { } impactTime && (!Number(impactTime,1) || impactTime.GetValue<float>() < 0)) return "impact cooldown";
                foreach (var (field, max) in new[] { ("fuel", def.Fuel), ("heat", 20f), ("scorch", 1f), ("cool", 10f), ("growth", 1f), ("harvest", 60f) })
                    if (s[field] is not { } number || number.GetValueKind() != JsonValueKind.Number || !float.IsFinite(number.GetValue<float>()) || number.GetValue<float>() < 0 || number.GetValue<float>() > max) return "ecology range";
                if (!Bool(s["burn"]) || !Bool(s["planted"]) || StrOf(s["by"]) is not ("" or PlayerPrincipal or CompanionPrincipal) || StrOf(s["incident"]) is not { } incident || (incident != "" && !EffectId.IsMatch(incident))) return "ecology authority";
                if (s["burn"]!.GetValue<bool>() && (N(s,"fuel") <= 0 || N(s,"cool") > 0 || s["planted"]!.GetValue<bool>())) return "burning protected or spent part";
            }
            foreach (var p in PlantedParts(e))
                if (parts[p.Id] is not JsonObject s || s["planted"]?.GetValue<bool>() != true || StrOf(s["by"]) != StrOf(e["plantings"]![p.Group]!["by"])) return "new plant protection";
            return null;
        }
        catch { return "invalid ecology state"; }
    }

    // Poses come from the authority (and the existing trusted carrying state), never mesh transforms.
    private EcologyPart[] LiveEcology()
    {
        var poses = ObjectPoses();
        var transforms = Room.Objects.ToDictionary(o => o.Id, o => {
            var pose = PoseOf(o,poses);
            return new Transform3D(new Basis(pose.Rotation),pose.Position) * new Transform3D(new Basis(o.Rotation),o.PositionM).AffineInverse();
        });
        return Room.Ecology.Select(p => transforms.TryGetValue(p.Target,out var transform)
            ? p with { Root = transform * p.Root, Bounds = transform * p.Bounds } : p).Concat(PlantedParts(_ecology)).ToArray();
    }

    public JsonObject PlayerEcology(string operation, string? id = null)
    {
        var candidates = LiveEcology().Where(p => operation == "repair" ? !p.Plant : p.Plant && p.Role == "foliage");
        var part = id == null ? candidates.Where(p => ReachEcology(p, Player)).OrderBy(p => p.Root.DistanceTo(Player!.GlobalPosition)).FirstOrDefault(p =>
            operation == "seed" || (operation == "repair" ? N(PartState(_ecology,p),"scorch") > 0 : N(PartState(_ecology,p),"fuel") <= 0)) : candidates.FirstOrDefault(p => p.Id == id);
        if (part == null) return Fail("entity.set_part", PlayerPrincipal, null, null, new Refusal("target_not_found", "No suitable plant or damaged structure within reach."));
        return PlayerCommand("entity.set_part", new JsonObject { ["target"] = part.Target, ["part_id"] = operation + "_" + part.Id, ["value"] = 1 });
    }

    private bool ReachEcology(EcologyPart p, SmallPlayerController? body)
    {
        if (body == null) return false;
        var point = p.Plant ? p.Root + Vector3.Up * 0.04f : body.EyeCamera.GlobalPosition.Clamp(p.Bounds.Position,p.Bounds.End);
        if (body.GlobalPosition.DistanceTo(point) > 0.7f) return false;
        var hit = FireRay(body.EyeCamera.GlobalPosition, point);
        return hit.Count == 0 || hit["position"].AsVector3().DistanceTo(point) < 0.06f;
    }

    private JsonObject SetEcology(JsonElement args, string principal, string action, Godot.Collections.Dictionary meta, bool preview)
    {
        var control = Str(args, "part_id")!;
        var step = control == "ecology_step";
        if (step && !_ecologyStep) throw new Refusal("permission_denied", "Only the trusted simulation advances landscape time.");
        if (!step && args.GetProperty("value").GetSingle() != 1) throw new Refusal("invalid_args", "Use a complete harvest, replant or repair action.");
        var next = (JsonObject)_ecology.DeepClone();
        var current = LiveEcology();
        JsonObject? fact = null;
        if (step) AdvanceEcology(next);
        else if (control.StartsWith("sow_", StringComparison.Ordinal))
            fact = SowSeed(next, control[4..], Str(args,"target")!, principal);
        else
        {
            var split = control.IndexOf('_');
            var operation = split < 0 ? "" : control[..split]; var id = split < 0 ? "" : control[(split+1)..];
            var part = current.FirstOrDefault(p => p.Id == id && p.Target == Str(args, "target"));
            if (part == null || operation is not ("seed" or "plant" or "repair")) throw new Refusal("unsupported_capability", "That landscape control is not available.");
            if (!ReachEcology(part, principal == PlayerPrincipal ? Player : Companion)) throw new Refusal("out_of_bounds", "Walk closer to the plant or structure.");
            var state = PartState(next, part);
            if (state["burn"]!.GetValue<bool>()) throw new Refusal("target_busy", "Douse the fire before tending this site.");
            if (operation == "seed")
            {
                if (!part.Plant || part.Role != "foliage" || N(state,"fuel") <= 0 || N(state,"growth") < 1 || N(state,"harvest") > 0) throw new Refusal("target_busy", "This plant has no ripe seeds yet.");
                var inventory = next["seeds"]!.AsObject(); var count = inventory[part.Species]?.GetValue<int>() ?? 0;
                if (count >= 99) throw new Refusal("budget_exceeded", "The seed pouch is full for this plant.");
                inventory[part.Species] = count + 1; state["harvest"] = 60f; next["parts"]![part.Id] = state;
            }
            else if (operation == "plant")
            {
                if (!part.Plant || N(state,"fuel") > 0) throw new Refusal("target_busy", "Plant a seed at a lost plant's site.");
                var count = next["seeds"]![part.Species]?.GetValue<int>() ?? 0;
                if (count < 1) throw new Refusal("budget_exceeded", "Collect a seed of this plant type first.");
                if (current.Where(p => p.Group == part.Group).Any(p => PartState(next,p)["burn"]!.GetValue<bool>())) throw new Refusal("target_busy", "Douse the whole plant first.");
                next["seeds"]![part.Species] = count - 1;
                foreach (var sibling in current.Where(p => p.Group == part.Group))
                { var fresh = FreshPart(sibling); fresh["planted"] = true; fresh["growth"] = 0.08f; fresh["by"] = principal; next["parts"]![sibling.Id] = fresh; }
            }
            else
            {
                if (part.Plant || N(state,"scorch") == 0) throw new Refusal("target_busy", "That part needs no structural repair.");
                if (Protected(part,state)) throw new Refusal("target_protected", "This structure is locked.");
                var members = current.Where(p => p.Group == part.Group).ToArray();
                if (members.Any(p => PartState(next,p)["burn"]!.GetValue<bool>())) throw new Refusal("target_busy","Douse the whole structure before repairing it.");
                foreach (var member in members) next["parts"]![member.Id] = FreshPart(member);
            }
            var line = operation == "seed" ? "Collected a " + part.Species.Replace('_',' ') + " seed." : operation == "plant" ? "Planted a " + part.Species.Replace('_',' ') + " seed; a protected sapling is growing." : "Repaired a fire-damaged stock structure.";
            fact = Fact(operation == "seed" ? "gathered" : "changed",line,principal,principal,null,null,null,revision:Revision+1);
            if (operation == "seed") fact["quantity"] = new JsonObject { ["count"] = 1 };
        }
        if (preview) return Previewed("entity.set_part", principal, action);
        if (JsonNode.DeepEquals(next, _ecology)) return Result("entity.set_part", principal, action, null, true);
        var history = _history.ToList(); if (fact != null) AddTo(history,fact);
        var team = ExportTeam(_notes,history); team["ecology"] = next;
        var request = new Godot.Collections.Dictionary { ["op"] = "ecology", ["action_id"] = action,
            ["target"] = Str(args,"target")!, ["step"] = step, ["team"] = KernelJson.ToVariant(team),
            ["expected_revision"] = Revision, ["expected_permission_revision"] = PermissionRevision };
        var result = Submit(request, principal, action, meta);
        _ecology = (JsonObject)next.DeepClone(); if (fact != null) _history = history; ApplyEcologyVisuals();
        return result;
    }

    private void AdvanceEcology(JsonObject next)
    {
        const float dt = 0.1f;
        var current = LiveEcology();
        var old = current.ToDictionary(p => p.Id, p => PartState(_ecology,p));
        var burning = current.Where(p => old[p.Id]["burn"]!.GetValue<bool>() && !Protected(p,old[p.Id])).ToArray();
        var count = burning.Length; var warming = old.Values.Count(s => N(s,"heat") > 0);
        foreach (var p in current.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var s = (JsonObject)old[p.Id].DeepClone();
            s["impact_cool"] = Math.Max(0,N(s,"impact_cool")-dt);
            s["harvest"] = Math.Max(0, N(s,"harvest") - dt); s["cool"] = Math.Max(0, N(s,"cool") - dt);
            if (N(s,"growth") < 1) s["growth"] = Math.Min(1, N(s,"growth") + dt / 30);
            var isProtected = Protected(p,s);
            if (!old[p.Id]["burn"]!.GetValue<bool>() && N(old[p.Id],"heat") == 0 && burning.Length == 0 && _fires.Count == 0 && _boltHeat.Count == 0)
            { if (!JsonNode.DeepEquals(s,old[p.Id])) next["parts"]![p.Id] = s; continue; }
            var activeNear = _fires.Values.Any(f => f.Ignites && f.Velocity == Vector3.Zero && p.Bounds.Grow(0.06f).HasPoint(f.Position)) || _boltHeat.Values.Any(v => p.Bounds.Grow(0.04f).HasPoint(v.Position)) || burning.Any(b => b.Bounds.Grow(0.05f).Intersects(p.Bounds));
            if (!activeNear && !s["burn"]!.GetValue<bool>() && N(s,"heat") == 0)
            { if (!JsonNode.DeepEquals(s,old[p.Id])) next["parts"]![p.Id] = s; continue; }
            var space = GetWorld3DForFire()?.DirectSpaceState;
            var water = space == null ? RoomWater.Column.Dry : RoomWater.At(space, p.Root + Vector3.Up * 0.01f);
            var wet = water.DepthM > RoomWater.RimM && water.SurfaceY > p.Root.Y + 0.01f;
            if (wet || isProtected) { s["burn"] = false; s["heat"] = 0f; if (wet) s["cool"] = 10f; }
            else if (s["burn"]!.GetValue<bool>())
            {
                s["fuel"] = Math.Max(0, N(s,"fuel") - dt); s["scorch"] = 1 - N(s,"fuel") / p.Fuel;
                if (N(s,"fuel") <= 0) s["burn"] = false;
            }
            else if (N(s,"cool") <= 0 && N(s,"fuel") > 0)
            {
                var source = _fires.Values.FirstOrDefault(f => f.Ignites && f.Velocity == Vector3.Zero && SurfaceContact(p,f.Position,0.05f));
                var impact = _boltHeat.FirstOrDefault(f => SurfaceContact(p,f.Value.Position,0.03f));
                var neighbor = burning.FirstOrDefault(b => b.Id != p.Id && b.Bounds.Grow(0.05f).Intersects(p.Bounds) && ClearEcologyPath(b,p));
                var heat = source != null ? dt : impact.Key != null && N(s,"impact_cool") <= 0 ? 1f : neighbor != null ? dt * 0.35f : -dt * 0.5f;
                // Reserve four burn slots for direct ignition and connected parts of an already burning plant.
                // Dense grass must not starve a burning tree of its canopy transition.
                var connected = burning.Any(b => b.Group == p.Group);
                if (heat > 0 && (N(old[p.Id],"heat") > 0 || warming < 64) && count < 24 && (count < 20 || connected || source != null))
                {
                    s["heat"] = Math.Min(20, N(s,"heat") + heat);
                    if (impact.Key != null && heat >= 1) s["impact_cool"] = 1f;
                    if (source != null) s["by"] = source.Principal;
                    if (N(s,"heat") >= p.Ignition)
                    {
                        var incident = source?.Id ?? (neighbor == null ? impact.Key : old[neighbor.Id]["incident"]!.GetValue<string>());
                        // A conservative shared incident budget survives repeated casts and reloads.
                        if (next["parts"]!.AsObject().Count(x => StrOf(x.Value!["incident"]) is { Length: > 0 }) < 128)
                        { s["burn"] = true; s["heat"] = 0f; s["by"] = source?.Principal ?? (neighbor == null ? impact.Value.Principal : old[neighbor.Id]["by"]!.GetValue<string>()); s["incident"] = incident; count++; }
                        else { s["heat"] = 0f; EcologyNotice = "Fire spread limit reached"; }
                    }
                    if (N(old[p.Id],"heat") == 0) warming++;
                }
                else s["heat"] = Math.Max(0, N(s,"heat") - dt * 0.5f);
            }
            if (!JsonNode.DeepEquals(s,old[p.Id])) next["parts"]![p.Id] = s;
        }
        _boltHeat.Clear();
    }

    private bool SurfaceContact(EcologyPart p, Vector3 at, float radius)
    {
        var closest = at.Clamp(p.Bounds.Position,p.Bounds.End);
        if (at.DistanceTo(closest) > radius) return false;
        var hit = FireRay(at,closest);
        if (hit.Count == 0 || at.DistanceTo(closest) < 0.015f) return true;
        // Stock collision is currently a whole-object proxy. Permit its own exposed part,
        // while another object's collider still blocks heat.
        var collider = hit["collider"].AsGodotObject() as Node;
        return collider != null && collider.HasMeta("entity_id") && collider.GetMeta("entity_id").AsString() == p.Target;
    }
    private bool ClearEcologyPath(EcologyPart a, EcologyPart b)
    {
        if (a.Group == b.Group && a.Plant && b.Plant) return true;
        var from = a.Bounds.GetCenter(); var to = b.Bounds.GetCenter();
        return FireRay(from,to).Count == 0;
    }

    private void DouseEcology(string principal)
    {
        EcologyNotice = "";
        var next = (JsonObject)_ecology.DeepClone();
        foreach (var (_, value) in next["parts"]!.AsObject())
        {
            var s = value!.AsObject();
            if (principal.Length > 0 && StrOf(s["by"]) != principal) continue;
            if (s["burn"]!.GetValue<bool>() || N(s,"heat") > 0)
            { s["burn"] = false; s["heat"] = 0f; s["cool"] = Math.Max(3,N(s,"cool")); }
        }
        if (JsonNode.DeepEquals(next,_ecology)) return;
        var team = ExportTeam(_notes); team["ecology"] = next;
        var outcome = Authority.Call("stop_ecology", KernelJson.ToVariant(team)).AsGodotDictionary();
        if (!outcome["ok"].AsBool()) throw Translate(outcome);
        _ecology = (JsonObject)next.DeepClone(); ApplyEcologyVisuals();
    }

    private void TickEcology(double delta)
    {
        if (Room.Ecology.Count == 0 || !Authority.Call("is_ready").AsBool()) return;
        _ecologyClock = Math.Min(0.2, _ecologyClock + delta);
        if (_ecologyClock < 0.1) return;
        _ecologyClock -= 0.1;
        _ecologyStep = true;
        try
        {
            var command = new JsonObject { ["schema"] = "enfractal.command", ["version"] = 1, ["room_id"] = Room.RoomId,
                ["action_id"] = $"eco-step-{_session}-{++_counter}", ["op"] = "entity.set_part",
                ["args"] = new JsonObject { ["target"] = Room.Ecology[0].Target, ["part_id"] = "ecology_step", ["value"] = 0.1 } };
            var result = HandleObject(CanonicalJson.Text(command),PlayerPrincipal);
            if (!HandleObjectOk(result)) EcologyNotice = "Landscape paused: " + result["error"]?["message"]?.GetValue<string>();
        }
        finally { _ecologyStep = false; }
    }

    private void ApplyEcologyVisuals()
    {
        if (GetParent() is not RoomWorld world) return;
        if (_ecologyMeshes.Count == 0)
        {
            foreach (var instance in Room.Objects.Where(o => Room.Ecology.Any(p => p.Target == o.Id)))
            {
                var body = world.Built.GetNodeOrNull<Node3D>("Objects/" + RoomBuilder.NodeName(instance.Id));
                if (body == null) continue;
                foreach (var original in body.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray())
                {
                    if (original.Mesh == null) continue;
                    for (var surface = 0; surface < original.Mesh.GetSurfaceCount(); surface++)
                    {
                        var sourceMaterial = original.Mesh.SurfaceGetMaterial(surface);
                        var role = sourceMaterial?.ResourceName ?? "";
                        if (LandscapeLook.TryRole(sourceMaterial,out var tagged)) role = tagged;
                        var def = Room.Ecology.FirstOrDefault(p => p.Target == instance.Id && p.Role == role);
                        var single = new ArrayMesh(); single.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, original.Mesh.SurfaceGetArrays(surface));
                        single.SurfaceSetMaterial(0,original.GetActiveMaterial(surface));
                        var child = new MeshInstance3D { Name = def == null ? "Unburnable" : "eco_" + def.Id, Mesh = single, Transform = original.Transform };
                        original.GetParent().AddChild(child);
                    }
                    original.Visible = false;
                }
            }
            foreach (var mesh in world.Built.FindChildren("eco_*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            {
                var id = mesh.Name.ToString()[4..];
                _ecologyMeshes[id] = _ecologyMeshes.GetValueOrDefault(id,Array.Empty<MeshInstance3D>()).Append(mesh).ToArray();
                _ecologyMaterials[id] = mesh.MaterialOverride ?? mesh.GetActiveMaterial(0);
            }
        }
        SyncPlantingVisuals(world);
        foreach (var p in LiveEcology())
        {
            if (_ecology["parts"]![p.Id] == null) continue;
            var state = PartState(_ecology,p); var scorch = N(state,"scorch");
            foreach (var mesh in _ecologyMeshes.GetValueOrDefault(p.Id,Array.Empty<MeshInstance3D>()))
            {
                mesh.Visible = !(p.Plant && p.Role is "foliage" or "snow" or "gravel" or "cloth") || N(state,"fuel") > 0;
                mesh.MaterialOverride = _ecologyMaterials.GetValueOrDefault(p.Id);
                mesh.MaterialOverlay = scorch > 0.01f ? new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(0.035f,0.025f,0.018f,Math.Min(1,scorch*1.4f)), Roughness = 1 } : null;
                // Exported scatter meshes use world coordinates. Growth pivots at the authored planting site.
                var growth = N(state,"growth");
                if (p.Target.StartsWith("shell:")) { mesh.Scale = Vector3.One * growth; mesh.Position = p.Root - _plantedSourceRoots.GetValueOrDefault(p.Id,p.Root) * growth; }
            }
            UpdatePlantingCollision(p,state);
            if (state["burn"]!.GetValue<bool>())
            {
                if (!_burnVisuals.TryGetValue(p.Id,out var visual))
                { visual = new FireVisual { Name = "Burn_" + p.Id, Scale = Vector3.One * Math.Clamp(p.Bounds.Size.Length()*3f,0.6f,4f) }; AddChild(visual); _burnVisuals[p.Id] = visual; }
                visual.GlobalPosition = p.Bounds.GetCenter() + new Vector3(p.Bounds.Size.X*0.2f,-p.Bounds.Size.Y*0.4f,p.Bounds.Size.Z*0.52f);
            }
            else if (_burnVisuals.Remove(p.Id,out var visual)) visual.QueueFree();
        }
    }
}
