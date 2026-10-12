using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EnFractal.Native.Room;

namespace EnFractal.Native.Kernel;

public partial class CommandHost
{
    public const int PlantingLimit = 128;
    private readonly Dictionary<string, Vector3> _plantedSourceRoots = new();
    private readonly Dictionary<string, StaticBody3D> _plantedBodies = new();
    public string SelectedSeed { get; private set; } = "";
    public string SeedSelectionLabel => SelectedSeed.Length == 0
        ? (_ecology["seeds"]!.AsObject().Any(p => p.Value!.GetValue<int>() > 0) ? "N Choose seed" : "N Choose seed (empty)")
        : $"N {SelectedSeed.Replace('_',' ')} ({SeedsOf(SelectedSeed)})";

    public void CycleSeed()
    {
        var choices = _ecology["seeds"]!.AsObject().Where(p => p.Value!.GetValue<int>() > 0).Select(p => p.Key).OrderBy(s => s,StringComparer.Ordinal).ToArray();
        SelectedSeed = choices.Length == 0 ? "" : choices[(Array.IndexOf(choices,SelectedSeed)+1)%choices.Length];
    }

    // Like placing a flame, the command resolves its site from the trusted avatar's facing.
    // The command never supplies an owner or a second route around the authority.
    public JsonObject PlayerPlantSeed(string? species = null)
    {
        if (species == null && SelectedSeed.Length == 0) CycleSeed();
        species ??= SelectedSeed;
        try
        {
            if (species.Length == 0) throw new Refusal("budget_exceeded","Collect seeds with 9 first, then choose a seed with N.");
            var site = PlantingGround(Player);
            return PlayerCommand("entity.set_part",new JsonObject { ["target"] = site.Target, ["part_id"] = "sow_"+species, ["value"] = 1 });
        }
        catch (Refusal refusal) { return Fail("entity.set_part",PlayerPrincipal,null,null,refusal); }
    }

    private (Vector3 Root,string Target) PlantingGround(SmallPlayerController? body)
    {
        if (body == null) throw new Refusal("not_ready","The planting avatar is not ready.");
        var direction = -body.GlobalBasis.Z; direction.Y = 0; direction = direction.Normalized();
        var ahead = body.GlobalPosition + direction * 0.28f;
        var hit = FireRay(ahead+Vector3.Up*0.3f,ahead+Vector3.Down*0.45f);
        if (hit.Count == 0) throw new Refusal("out_of_bounds","Face nearby ground to plant a seed.");
        var root = hit["position"].AsVector3();
        var collider = hit["collider"].AsGodotObject() as Node;
        var target = collider != null && collider.HasMeta("entity_id") ? collider.GetMeta("entity_id").AsString() : "";
        var ground = Room.Shell.FirstOrDefault(p => p.Id == target);
        if (ground == null || ground.MaterialRole is not ("grass" or "soil") || collider!.HasMeta("planted_tree") || hit["normal"].AsVector3().Y < 0.7f)
            throw new Refusal("occupied","Choose gently sloping grass or soil, away from buildings and rocks.");
        if (!Room.Bounds.HasPoint(root) || body.GlobalPosition.DistanceTo(root) > 0.7f)
            throw new Refusal("out_of_bounds","Walk closer to the planting ground.");
        if (Authority.Call("is_locked",target).AsBool()) throw new Refusal("target_protected","This ground is locked.");
        if (RoomWater.At(body.GetWorld3D().DirectSpaceState,root+Vector3.Up*0.01f).DepthM > RoomWater.RimM)
            throw new Refusal("occupied","Choose dry ground above the water.");
        var sight = FireRay(body.EyeCamera.GlobalPosition,root+Vector3.Up*0.015f);
        if (sight.Count > 0 && sight["position"].AsVector3().DistanceTo(root) > 0.035f)
            throw new Refusal("occupied","Something blocks the planting spot.");
        return (root,target);
    }

    private EcologyPart[] PlantTemplate(string species) => Room.Ecology.Where(p => p.Plant && p.Species == species && p.Role == "foliage" && p.Target.StartsWith("shell:"))
        .OrderBy(p => p.Id,StringComparer.Ordinal).Take(1).SelectMany(p => Room.Ecology.Where(s => s.Group == p.Group)).OrderBy(p => p.Id,StringComparer.Ordinal).ToArray();

    private JsonObject SowSeed(JsonObject next,string species,string target,string principal)
    {
        var template = PlantTemplate(species);
        if (template.Length == 0) throw new Refusal("unsupported_capability","That plant type has no island seed template.");
        var count = next["seeds"]![species]?.GetValue<int>() ?? 0;
        if (count < 1) throw new Refusal("budget_exceeded","Collect a seed of this plant type first.");
        var site = PlantingGround(principal == PlayerPrincipal ? Player : Companion);
        if (site.Target != target) throw new Refusal("revision_conflict","The planting surface changed; try again.");
        if (next["plantings"] == null) next["plantings"] = new JsonObject();
        var plants = next["plantings"]!.AsObject();
        if (plants.Count >= PlantingLimit) throw new Refusal("budget_exceeded","This island has reached its 128 new-plant limit.");
        var tree = template.Any(p => p.Role == "bark");
        var radius = tree ? 0.16f : 0.035f;
        foreach (var other in LiveEcology().Where(p => p.Plant && p.Role == "foliage" && N(PartState(next,p),"fuel") > 0))
        {
            var otherTree = other.Species is "broadleaf" or "conifer";
            // Ground cover may grow beneath trees; trees need their own space, and new plants must not overlap.
            var separate = tree && otherTree ? 0.32f : other.Group.StartsWith("grown_") && (!tree || otherTree) ? radius+0.025f : 0f;
            if (separate > 0 && new Vector2(site.Root.X-other.Root.X,site.Root.Z-other.Root.Z).Length() < separate)
                throw new Refusal("occupied","Leave more space between these plants.");
        }
        var footprint = new Aabb(site.Root+new Vector3(-radius,0.025f,-radius),new Vector3(radius*2,tree ? 0.45f : 0.06f,radius*2));
        using (var query = new PhysicsShapeQueryParameters3D { Shape = new BoxShape3D { Size = footprint.Size }, Transform = new Transform3D(Basis.Identity,footprint.GetCenter()), CollisionMask = RoomBuilder.WorldLayer })
        {
            var hits = GetWorld3DForFire()!.DirectSpaceState.IntersectShape(query,16);
            if (hits.Any(h => h["collider"].AsGodotObject() is Node n && (n.HasMeta("planted_tree") || !n.HasMeta("entity_id") ||
                !Room.Shell.Any(s => s.Id == n.GetMeta("entity_id").AsString() && s.MaterialRole is "grass" or "soil"))))
                throw new Refusal("occupied","The plant needs a clear space away from structures and rocks.");
        }
        var id = "grown_"+Guid.NewGuid().ToString("N");
        plants[id] = new JsonObject { ["template"] = template[0].Group, ["position_m"] = KernelJson.Vector(site.Root), ["target"] = target, ["by"] = principal };
        next["seeds"]![species] = count-1;
        foreach (var part in PlantedParts(next).Where(p => p.Group == id))
        {
            var state = FreshPart(part); state["planted"] = true; state["growth"] = 0.08f; state["by"] = principal;
            next["parts"]![part.Id] = state;
        }
        return Fact("changed","Planted a "+species.Replace('_',' ')+" seed in a new location.",principal,principal,null,null,null,revision:Revision+1);
    }

    private string? PlantingsProblem(JsonObject e)
    {
        if (!e.ContainsKey("plantings")) return null; // Backward-compatible first fire saves.
        if (e["plantings"] is not JsonObject plants || plants.Count > PlantingLimit) return "planting budget";
        foreach (var (id,node) in plants)
        {
            if (!Regex.IsMatch(id,@"\Agrown_[a-f0-9]{32}\z") || node is not JsonObject p || p.Count != 4 || p.Any(v => v.Key is not ("template" or "position_m" or "target" or "by"))) return "planting shape";
            if (StrOf(p["by"]) is not (PlayerPrincipal or CompanionPrincipal)) return "planting owner";
            var template = Room.Ecology.Where(s => s.Group == StrOf(p["template"])).ToArray();
            if (template.Length == 0 || template.Any(s => !s.Plant || !s.Target.StartsWith("shell:"))) return "planting template";
            if (!Room.Shell.Any(s => s.Id == StrOf(p["target"]) && s.MaterialRole is "grass" or "soil")) return "planting ground";
            if (p["position_m"] is not JsonArray a || a.Count != 3 || a.Any(v => v == null || !Number(v,1000))) return "planting position";
            var root = ReadPlantRoot(p);
            if (!Room.Bounds.HasPoint(root)) return "planting bounds";
        }
        return null;
    }

    private static Vector3 ReadPlantRoot(JsonNode p) => new((float)p["position_m"]![0]!.GetValue<double>(),(float)p["position_m"]![1]!.GetValue<double>(),(float)p["position_m"]![2]!.GetValue<double>());
    private IEnumerable<EcologyPart> PlantedParts(JsonObject e)
    {
        if (e["plantings"] is not JsonObject plants) yield break;
        foreach (var (id,node) in plants)
        {
            var root = ReadPlantRoot(node!); var index = 0;
            foreach (var source in Room.Ecology.Where(p => p.Group == StrOf(node!["template"])).OrderBy(p => p.Id,StringComparer.Ordinal))
                yield return source with { Id = id+"_"+index++, Group = id, Target = node!["target"]!.GetValue<string>(), Root = root, Bounds = new Aabb(source.Bounds.Position+root-source.Root,source.Bounds.Size) };
        }
    }

    private void SyncPlantingVisuals(RoomWorld world)
    {
        var parts = PlantedParts(_ecology).ToArray(); var liveIds = parts.Select(p => p.Id).ToHashSet();
        foreach (var stale in _plantedSourceRoots.Keys.Where(id => !liveIds.Contains(id)).ToArray())
        {
            foreach (var mesh in _ecologyMeshes[stale]) mesh.QueueFree();
            if (_plantedBodies.Remove(stale,out var body)) body.QueueFree();
            _ecologyMeshes.Remove(stale); _ecologyMaterials.Remove(stale); _plantedSourceRoots.Remove(stale);
        }
        foreach (var p in parts.Where(p => !_plantedSourceRoots.ContainsKey(p.Id)))
        {
            var record = _ecology["plantings"]![p.Group]!;
            var sources = Room.Ecology.Where(s => s.Group == StrOf(record["template"])).OrderBy(s => s.Id,StringComparer.Ordinal).ToArray();
            var source = sources[int.Parse(p.Id[(p.Id.LastIndexOf('_')+1)..])];
            var meshes = new List<MeshInstance3D>();
            foreach (var original in _ecologyMeshes.GetValueOrDefault(source.Id,Array.Empty<MeshInstance3D>()))
            {
                var mesh = new MeshInstance3D { Name = p.Id, Mesh = original.Mesh, MaterialOverride = _ecologyMaterials.GetValueOrDefault(source.Id) };
                world.Built.AddChild(mesh); meshes.Add(mesh);
            }
            _ecologyMeshes[p.Id] = meshes.ToArray(); _ecologyMaterials[p.Id] = _ecologyMaterials.GetValueOrDefault(source.Id); _plantedSourceRoots[p.Id] = source.Root;
            if (p.Role == "bark")
            {
                var body = new StaticBody3D { Name = "Trunk_"+p.Id, CollisionLayer = RoomBuilder.WorldLayer, CollisionMask = 0 };
                body.SetMeta("entity_id",p.Target); body.SetMeta("planted_tree",true);
                body.AddChild(new CollisionShape3D { Shape = new BoxShape3D() }); world.Built.AddChild(body); _plantedBodies[p.Id] = body;
            }
        }
    }

    private void UpdatePlantingCollision(EcologyPart p,JsonObject state)
    {
        if (!_plantedBodies.TryGetValue(p.Id,out var body)) return;
        var growth = N(state,"growth"); var shape = body.GetChild<CollisionShape3D>(0);
        ((BoxShape3D)shape.Shape).Size = p.Bounds.Size*growth;
        body.GlobalPosition = p.Root+(p.Bounds.GetCenter()-p.Root)*growth;
    }
}
