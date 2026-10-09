using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EnFractal.Native.Look;
using EnFractal.Native.Look.Fauna;
using EnFractal.Native.Room;

namespace EnFractal.Tests.Look;

/// <summary>
/// The open sea on synthetic geometry and physics, so its protections hold whether or not a landscape has been exported (Codex's
/// second-opinion reviews of Lane L's open sea round, Sol and Astra): the view under water asks physics' own water-column questions
/// (a dry shelf, a submerged ceiling, the shoreline); the distant islands retreat from the swimmer, not the camera, and hold still
/// to the millimetre near the far net; an island split by mesh seams stays one island; geometry the look cannot split is left
/// untouched; the far sea's and the hazed islands' highlights fade with their colour; and the sea's fish keep to real water.
/// </summary>
public partial class LookPresetTest
{
    private const float SynthLevel = 0f;

    private static Vector2[] Square(float half) => new[] { new Vector2(-half, -half), new Vector2(half, -half), new Vector2(half, half), new Vector2(-half, half) };

    private static RoomSea SyntheticSea() => new(SynthLevel, Square(1f), Square(1.4f), Square(1.8f), Array.Empty<RoomSea.Beach>());

    private static StaticBody3D Block(Node parent, Vector3 centre, Vector3 size)
    {
        var body = new StaticBody3D { CollisionLayer = RoomBuilder.WorldLayer, CollisionMask = 0, Position = centre };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(body);
        return body;
    }

    /// <summary>A sea surface sheet 60 m across: drawn with the sea's water material, and the query-only collider physics finds.</summary>
    private static MeshInstance3D SyntheticSeaSurface(Node parent)
    {
        const float half = 30f;
        var mesh = new ArrayMesh();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        var corners = new[] { new Vector3(-half, SynthLevel, -half), new Vector3(half, SynthLevel, -half), new Vector3(half, SynthLevel, half), new Vector3(-half, SynthLevel, half) };
        arrays[(int)Mesh.ArrayType.Vertex] = corners;
        arrays[(int)Mesh.ArrayType.Normal] = Enumerable.Repeat(Vector3.Up, 4).ToArray();
        arrays[(int)Mesh.ArrayType.Color] = Enumerable.Repeat(new Color(0.4f, 0.6f, 0.7f), 4).ToArray();
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var surface = new MeshInstance3D { Name = "SeaSurface", Mesh = mesh };
        surface.SetSurfaceOverrideMaterial(0, MaterialLibrary.ForLandscape("still_water", new Color("619baf"), new Color("3a6f80")));
        surface.SetMeta(LookDirector.WaterMeta, true);
        surface.SetMeta(LookDirector.DressedMeta, true);
        parent.AddChild(surface);
        var sheet = new ConcavePolygonShape3D { Data = new[] { corners[0], corners[1], corners[2], corners[0], corners[2], corners[3] } };
        parent.AddChild(RoomWater.CreateCollider(new[] { ((Shape3D)sheet, Transform3D.Identity) }));
        return surface;
    }

    /// <summary>
    /// A backdrop like the generator's: a floor ring 3 to 25 m out, 0.9 m down, and two islands rising out of the sea. Each island is
    /// built in two halves whose shared edge has its own vertex copies (a normal or UV seam), as an exporter may write them.
    /// indexed false writes the same triangles unindexed (geometry the look cannot split).
    /// </summary>
    private static MeshInstance3D SyntheticBackdrop(Node parent, bool indexed = true)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        const int sides = 24;
        // The floor ring.
        var radii = new[] { 3f, 6f, 10f, 15f, 25f };
        for (var r = 0; r < radii.Length; r++)
            for (var k = 0; k < sides; k++)
                positions.Add(new Vector3(Mathf.Cos(Mathf.Tau * k / sides) * radii[r], -0.9f, Mathf.Sin(Mathf.Tau * k / sides) * radii[r]));
        for (var r = 0; r + 1 < radii.Length; r++)
            for (var k = 0; k < sides; k++)
            {
                int a = r * sides + k, b = r * sides + (k + 1) % sides, c = a + sides, d = b + sides;
                indices.AddRange(new[] { a, d, b, a, c, d });
            }
        // Two islands: cones, each in two halves with their own copies of the shared apex and edge.
        foreach (var (centre, radius) in new[] { (new Vector2(8f, 0f), 1.6f), (new Vector2(-9f, 5f), 1.8f) })
            for (var half = 0; half < 2; half++)
            {
                var apex = positions.Count;
                positions.Add(new Vector3(centre.X, 1.0f, centre.Y));
                var ring = positions.Count;
                for (var k = 0; k <= 6; k++)
                {
                    var a = Mathf.Pi * (half + k / 6f);
                    positions.Add(new Vector3(centre.X + Mathf.Cos(a) * radius, -0.5f, centre.Y + Mathf.Sin(a) * radius));
                }
                for (var k = 0; k < 6; k++) indices.AddRange(new[] { apex, ring + k + 1, ring + k });
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        if (indexed)
        {
            arrays[(int)Mesh.ArrayType.Vertex] = positions.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        }
        else arrays[(int)Mesh.ArrayType.Vertex] = indices.Select(i => positions[i]).ToArray();
        var count = ((Vector3[])arrays[(int)Mesh.ArrayType.Vertex]).Length;
        arrays[(int)Mesh.ArrayType.Normal] = Enumerable.Repeat(Vector3.Up, count).ToArray();
        arrays[(int)Mesh.ArrayType.Color] = Enumerable.Repeat(new Color(0.8f, 0.85f, 0.7f), count).ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var backdrop = new MeshInstance3D { Name = "Backdrop", Mesh = mesh };
        backdrop.SetSurfaceOverrideMaterial(0, MaterialLibrary.ForLandscape("moss", new Color("84a06f"), new Color("5a7a4a")));
        backdrop.SetMeta(LookDirector.DressedMeta, true);
        backdrop.SetMeta(LandscapeLook.LandscapePaintedMeta, true);
        parent.AddChild(backdrop);
        return backdrop;
    }

    private async Task CheckOpenSeaSynthetic(StylePreset preset)
    {
        MaterialLibrary.Configure(preset);
        var sea = SyntheticSea();
        var holder = new Node3D { Name = "SyntheticSeaHolder" };
        AddChild(holder);
        var water = SyntheticSeaSurface(holder);
        var backdrop = SyntheticBackdrop(holder);
        // The bed under the sea, a rock under the water, a slab under the water with water beneath it, a shoal and a sea stack.
        Block(holder, new Vector3(0f, -0.66f - 0.5f, 0f), new Vector3(80f, 1f, 80f));
        Block(holder, new Vector3(4.5f, -0.275f, 0f), new Vector3(1f, 0.45f, 1f));  // a submerged rock: from -0.5 up to -0.05
        Block(holder, new Vector3(-4.5f, -0.15f, 0f), new Vector3(1f, 0.1f, 1f));   // submerged ceiling: a slab from -0.2 to -0.1
        Block(holder, new Vector3(0f, -0.35f, 6.5f), new Vector3(3f, 0.3f, 3f));    // a shallow shoal: top 0.2 m under the surface
        Block(holder, new Vector3(0f, -0.2f, -6.5f), new Vector3(3f, 1f, 3f));      // a rock standing out of the sea
        var bounds = new Aabb(new Vector3(-2f, -1f, -2f), new Vector3(4f, 3f, 4f));
        var open = OpenSea.Build(sea, bounds, new[] { water }, new[] { backdrop });
        holder.AddChild(open);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        var space = GetWorld3D().DirectSpaceState;

        // 1. The view under water agrees with physics: the bodies' own column (RoomWater.At, then RoomSea.OpenWater), the eye
        // under its surface, and the sea's water (a pond keeps its own veil).
        bool PhysicsUnder(Vector3 eye)
        {
            var column = RoomWater.At(space, eye, RoomWater.SearchM, 0f);
            if (!column.Wet) column = sea.OpenWater(space, eye, RoomWater.SearchM, 0f);
            return column.Wet && eye.Y < column.SurfaceY && Mathf.Abs(column.SurfaceY - sea.LevelM) < 0.003f;
        }
        var eyes = new (string Name, Vector3 Eye)[]
        {
            ("the open sea", new Vector3(6f, -0.2f, -1.5f)), ("inside a submerged rock", new Vector3(4.5f, -0.3f, 0f)),
            ("under a submerged ceiling", new Vector3(-4.5f, -0.3f, 0f)), ("the shoreline, inside the simplified coast", new Vector3(0.6f, -0.05f, 0.6f)),
            ("below the bed", new Vector3(6f, -0.8f, -1.5f)), ("in the air", new Vector3(6f, 0.1f, -1.5f)),
        };
        var camera = new Camera3D { Far = 100f };
        holder.AddChild(camera);
        foreach (var (name, eye) in eyes)
        {
            camera.GlobalPosition = eye;
            open.Follow(camera);
            Check(open.Veil.Visible == PhysicsUnder(eye), $"the view under water agrees with physics {name} (veil {open.Veil.Visible}, physics {PhysicsUnder(eye)})");
        }

        // 4. An island split by mesh seams stays one island; 5. the floor lies on the bed; the islands stand where they were.
        Check(open.Islands.Count == 2 && open.Note.Length == 0, $"two islands, each built in halves with their own seam vertices, are lifted out as two islands ({open.Islands.Count}): {open.Note}");

        // 2. The islands retreat from the swimmer, not the camera: switching F1 to F4, or orbiting F3, moves none of them.
        var swimmer = new Node3D { Name = "SynthSwimmer" };
        holder.AddChild(swimmer);
        open.SetSwimmer(() => swimmer);
        var island = open.Islands.OrderBy(i => i.HomeOffset.X).Last();
        var bearing = island.HomeOffset.Normalized();
        foreach (var along in new[] { island.HomeOffset.Length() - island.KeepM - 0.02f, island.HomeOffset.Length() - island.KeepM + 0.3f, island.HomeOffset.Length() + 3f })
        {
            var at = open.Home + bearing * along;
            swimmer.GlobalPosition = new Vector3(at.X, -0.02f, at.Y);
            var placed = new List<Vector3>();
            foreach (var offset in new[] { new Vector3(0f, 0.09f, 0f), new Vector3(0.1f, 0.13f, 0.3f), new Vector3(2.2f, 1.5f, 0f), new Vector3(-1.0f, 1.0f, 0.9f), new Vector3(0.7f, 2.2f, -0.6f) })
            {
                camera.GlobalPosition = swimmer.GlobalPosition + offset;
                open.Follow(camera);
                placed.Add(island.Mesh.GlobalPosition);
                Check(open.Islands.All(i => new Vector2(camera.GlobalPosition.X, camera.GlobalPosition.Z).DistanceTo(IslandCentre(open, i)) > 0.5f * i.KeepM),
                    $"no play camera ({offset} off the swimmer) ever stands inside an island");
            }
            Check(placed.All(p => p.IsEqualApprox(placed[0])), $"with the swimmer {along:0.0} m out along an island's bearing, every view (F1, F2, F4, F3 orbiting) sees it in one place");
        }

        // 3. Near the far net an island holds still to the millimetre: the sideways distance is computed directly, in double precision.
        var worst = 0.0;
        foreach (var keep in new[] { 6.2f, 3.0f })
            for (var k = 0; k < 4000; k++)
            {
                // A swimmer 995 m out along the island's bearing, sliding sideways a millimetre a step across the island's keep.
                var homeOffset = new Vector2(10f, 0.3f);
                var b = homeOffset.Normalized();
                var camera2 = b * 995f + new Vector2(-b.Y, b.X) * (keep - 2f + k * 0.001f);
                var got = OpenSea.IslandOffset(homeOffset, keep, camera2);
                worst = Math.Max(worst, (got - ReferenceOffset(homeOffset, keep, camera2)).Length());
            }
        Check(worst < 0.002, $"near the 1 km net an island stands within 2 mm of where exact arithmetic puts it (worst {worst * 1000:0.###} mm)");

        // 5. Geometry the look cannot split is left exactly as it was: its islands are not flattened into the floor.
        var unindexed = SyntheticBackdrop(holder, indexed: false);
        var before = unindexed.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Max(v => v.Y);
        var rejected = OpenSea.Build(sea, bounds, new[] { water }, new[] { unindexed });
        holder.AddChild(rejected);
        var after = unindexed.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Max(v => v.Y);
        Check(Mathf.IsEqualApprox(before, after) && rejected.Islands.Count == 0 && rejected.Note.Length > 0,
            $"an unindexed backdrop is left untouched, islands and all (top {after:0.##} m, was {before:0.##} m), and the look says why: {rejected.Note}");

        // 7. The sea's fish keep to real water: never in a rock standing out of the sea or over a shoal shallower than they need.
        var life = SeaLife.Create(sea, () => swimmer);
        holder.AddChild(life);
        Check(life.Habitable(new Vector2(6f, -1.5f)) && !life.Habitable(new Vector2(0f, -6.5f)) && !life.Habitable(new Vector2(0f, 6.5f)),
            $"the sea's fish may live in open water ({life.Habitable(new Vector2(6f, -1.5f))}), never in a rock standing out of the sea ({life.Habitable(new Vector2(0f, -6.5f))}) or over a shoal 20 cm deep ({life.Habitable(new Vector2(0f, 6.5f))})");
        swimmer.GlobalPosition = new Vector3(0f, -0.02f, -9f);
        for (var step = 0; step < 90; step++) life.Advance(1f / 30f);
        Check(life.Fish.All(f => f.Position.Y < -500f || life.Habitable(new Vector2(f.Position.X, f.Position.Z))), "and every fish stays over real water as it swims");

        // 6. Highlights fade with the colour: the far sea's and the hazed islands' specular go into the horizon with them.
        var waterCode = Regex.Replace(GD.Load<Shader>(LandscapeLook.WaterShaderPath).Code, @"//[^\n]*", "");
        var landCode = Regex.Replace(GD.Load<Shader>(LandscapeLook.ShaderPath).Code, @"//[^\n]*", "");
        Check(Regex.IsMatch(waterCode, @"SPECULAR_LIGHT\s*\+=[^;]*\(1\.0\s*-\s*horizon_v\)") && Regex.IsMatch(landCode, @"SPECULAR_LIGHT\s*\+=[^;]*\(1\.0\s*-\s*haze_v\)"),
            "the far sea's and the hazed islands' highlights fade into the horizon with their colour");
        holder.QueueFree();
        await Frames(1);
    }

    private static Vector2 IslandCentre(OpenSea open, OpenSea.DistantIsland island) =>
        open.Home + island.HomeOffset + new Vector2(island.Mesh.GlobalPosition.X - island.BasePosition.X, island.Mesh.GlobalPosition.Z - island.BasePosition.Z);

    /// <summary>The island rule in double precision: along its bearing, never behind the swimmer, keep metres off.</summary>
    private static Vector2 ReferenceOffset(Vector2 homeOffset, float keepM, Vector2 at)
    {
        double hx = homeOffset.X, hz = homeOffset.Y, cx = at.X, cz = at.Y, keep = keepM;
        var distance = Math.Sqrt(hx * hx + hz * hz);
        double bx = hx / distance, bz = hz / distance;
        var along = cx * bx + cz * bz;
        var aside = -cx * bz + cz * bx;
        var reach = Math.Max(distance, along);
        if (Math.Abs(aside) < keep) reach = Math.Max(reach, along + Math.Sqrt(keep * keep - aside * aside));
        return new Vector2((float)(bx * reach), (float)(bz * reach));
    }
}
