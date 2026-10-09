using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using EnFractal.Native.Look.Fauna;
using EnFractal.Native.Room;

namespace EnFractal.Native.Look;

/// <summary>
/// The endless sea (Run 2, the founder's playtest: "the kids want to be able to swim forever"). The room's own meshes stop:
/// the generated sea surface reaches 60 m out and the backdrop's sea floor about 50 m. The open sea carries on from wherever the
/// camera is, at the same cost everywhere:
/// <list type="bullet">
/// <item>a surface that follows the camera, wearing the room's own sea water with its marks in world space (so they stay put
/// while the surface moves), leaving out the rectangle the room's sea mesh already paints, and turning into the sky's own
/// horizon colour toward the camera's far plane, so the sea runs to the horizon with no edge;</item>
/// <item>one sea floor: the backdrop's floor and an open-sea bed beyond it out to 1.5 km, both drawn at RoomSea.OpenSeaBedAt, the
/// bed the swimmers and divers touch (Lane P), so a diver never sees the bed in one place and touches it in another, and the water
/// past the room's meshes always has a bed to be water over (the depth the water shader reads);</item>
/// <item>the distant islands hold their place on the horizon (the founder, 9 October: they are never reached or swum through,
/// like the moon). They are lifted out of the backdrop mesh, each its own silhouette, and slide away along their own bearing
/// from the home island whenever a swimmer would come closer to one than it looks from the edge of the home waters.</item>
/// </list>
/// Visual only: it changes no room data, collision or protection. Swimming past the meshes is Lane P's (RoomSea answers there).
/// </summary>
public partial class OpenSea : Node3D
{
    /// <summary>Half the size of the follow surface and bed, metres: past every play camera's far plane (100 m).</summary>
    public const float ReachM = 160f;
    /// <summary>How far out the open-sea bed reaches from the island, metres: past Lane P's far net (1 km) and a camera's far plane.</summary>
    public const float BedReachM = 1500f;
    /// <summary>How many sides the open-sea bed has when the room has no backdrop floor to continue.</summary>
    public const int BedSides = 128;
    /// <summary>What of the backdrop is an island: every triangle reaching higher than this under the sea's level (deeper is floor).</summary>
    public const float IslandCutM = 0.3f;
    /// <summary>A backdrop mesh is one that reaches this far past the room's sea (the distant islands' ring, not the home island).</summary>
    public const float BackdropReachM = 20f;
    /// <summary>However a swimmer comes at an island, its shore stays at least this far off.</summary>
    public const float ShoreKeepM = 1.5f;
    /// <summary>Where, as fractions of the camera's far plane, the sea starts and finishes turning into the horizon.</summary>
    public const float HorizonFadeStart = 0.55f, HorizonFadeEnd = 0.97f;
    /// <summary>The open bed's colour when there is no backdrop floor to continue: a dark moss, of which deep water lets a tenth through.</summary>
    public static readonly Color BedColor = new(0.30f, 0.34f, 0.28f);

    /// <summary>One distant island: its mesh, its centre from the home island's centre, and how close its centre may come to a swimmer.</summary>
    public sealed record DistantIsland(MeshInstance3D Mesh, Vector2 HomeOffset, float KeepM, Vector3 BasePosition);

    public RoomSea Sea { get; private set; } = null!;
    /// <summary>The surface that follows the camera (null when the room's own sea mesh could not be found).</summary>
    public MeshInstance3D? Surface { get; private set; }
    /// <summary>The open-sea bed past the backdrop's floor, out to BedReachM: static, drawn at RoomSea.OpenSeaBedAt.</summary>
    public MeshInstance3D Bed { get; private set; } = null!;
    /// <summary>The backdrop meshes whose floor now lies at RoomSea.OpenSeaBedAt (their islands lifted out).</summary>
    public IReadOnlyList<MeshInstance3D> Floors => _floors;
    /// <summary>The centre of the room's sea (the generated sea's and the backdrop's centre), in XZ.</summary>
    public Vector2 Home { get; private set; }
    /// <summary>How far the home waters reach from Home: the reef's farthest point.</summary>
    public float HomeReachM { get; private set; }
    /// <summary>The rectangle (x0, z0, x1, z1) the room's own sea mesh covers, which the follow surface leaves to it.</summary>
    public Vector4 Hole { get; private set; }
    public IReadOnlyList<DistantIsland> Islands => _islands;
    /// <summary>The camera the sea follows: the one a review or preview framed, else the viewport's current camera.</summary>
    public Camera3D? Camera { get; set; }
    /// <summary>Why the open sea is missing a part (empty when it has them all).</summary>
    public string Note { get; private set; } = "";

    private readonly List<DistantIsland> _islands = new();
    private readonly List<MeshInstance3D> _floors = new();
    private ShaderMaterial? _islandMaterial;
    /// <summary>The fish in the sea's deeper water near the swimmer (null until the look names the swimmer).</summary>
    public SeaLife? Life { get; private set; }

    /// <summary>The sea's fish follow this swimmer (the player; never the Gubble): the look hands it over once the room is dressed.</summary>
    public void SetSwimmer(Func<Node3D?> swimmer)
    {
        if (Life != null) return;
        Life = SeaLife.Create(Sea, swimmer);
        AddChild(Life);
    }
    private readonly List<ShaderMaterial> _water = new();
    private float _far = -1f;

    /// <summary>
    /// Build the open sea for a dressed room with a sea. waterMeshes are the room's water meshes as the look dressed them (the
    /// sea's surface is the one reaching past the room); the backdrop is the dressed land mesh reaching BackdropReachM past it.
    /// </summary>
    public static OpenSea Build(RoomSea sea, Aabb roomBounds, IEnumerable<MeshInstance3D> waterMeshes, IEnumerable<MeshInstance3D> landMeshes)
    {
        var open = new OpenSea { Name = "OpenSea", Sea = sea };
        var roomRect = new Rect2(roomBounds.Position.X, roomBounds.Position.Z, roomBounds.Size.X, roomBounds.Size.Z);
        // The room's sea surface: the water mesh that reaches well past the room (ponds and brooks stay inside it).
        var seaMesh = waterMeshes.Where(m => m.Mesh != null)
            .Select(m => (Mesh: m, Box: m.GlobalTransform * m.GetAabb()))
            .Where(m => m.Box.Size.X > roomRect.Size.X + 2f * BackdropReachM || m.Box.Size.Z > roomRect.Size.Y + 2f * BackdropReachM)
            .OrderByDescending(m => m.Box.Size.X * m.Box.Size.Z).FirstOrDefault();
        var centre = roomRect.GetCenter();
        if (seaMesh.Mesh != null)
        {
            var box = seaMesh.Box;
            open.Hole = new Vector4(box.Position.X, box.Position.Z, box.End.X, box.End.Z);
            centre = new Vector2(box.GetCenter().X, box.GetCenter().Z);
        }
        open.Home = centre;
        open.HomeReachM = sea.Reef.Length > 0 ? sea.Reef.Max(p => p.DistanceTo(centre)) : roomRect.Size.Length() * 0.5f;

        // The backdrop: islands lifted out, the floor left where it is; its deepest floor sets the open bed's depth.
        var backdrop = landMeshes.Where(m => m.Mesh != null && m.Mesh.GetSurfaceCount() > 0)
            .Select(m => (Mesh: m, Box: m.GlobalTransform * m.GetAabb()))
            .Where(m => m.Box.Position.X < roomRect.Position.X - BackdropReachM || m.Box.End.X > roomRect.End.X + BackdropReachM)
            .Select(m => m.Mesh).ToArray();
        if (seaMesh.Mesh?.GetSurfaceOverrideMaterial(0) is ShaderMaterial water && water.HasMeta("water"))
        {
            open._water.Add(water);
            var surface = (ShaderMaterial)water.Duplicate();
            surface.ResourceName = "open sea";
            surface.SetShaderParameter("world_pattern", true);
            surface.SetShaderParameter("use_hole", true);
            surface.SetShaderParameter("hole", open.Hole);
            open._water.Add(surface);
            open.Surface = new MeshInstance3D
            {
                Name = "OpenSeaSurface", Mesh = Quad(ReachM, OpenWaterColour(seaMesh.Mesh, roomRect)), MaterialOverride = surface,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                Position = new Vector3(centre.X, sea.LevelM, centre.Y),
            };
            open.Surface.SetMeta(LookDirector.DressedMeta, true);
            open.Surface.SetMeta(LookDirector.WaterMeta, true);
            open.AddChild(open.Surface);
        }
        else open.Note = "the room's sea surface was not found among its water, so the open sea has a bed but no surface";

        // Islands lifted out first, then the floor laid on the bed the bodies touch, then the open bed carrying on from its rim.
        Vector3[] rim = Array.Empty<Vector3>();
        Color[] rimColours = Array.Empty<Color>();
        Material? floorMaterial = null;
        // The sea's own look on its water (the lagoon light, the open sea deep, foam on the reef and the beaches).
        foreach (var material in open._water)
        {
            material.SetShaderParameter("use_sea", true);
            material.SetShaderParameter("sea_level", sea.LevelM);
        }
        foreach (var mesh in backdrop)
        {
            open.LiftIslands(mesh);
            var (meshRim, meshColours) = open.LayFloor(mesh);
            if (meshRim.Length > rim.Length) (rim, rimColours, floorMaterial) = (meshRim, meshColours, mesh.GetSurfaceOverrideMaterial(0));
        }
        open.Bed = new MeshInstance3D
        {
            Name = "OpenSeaBed", Mesh = open.BedMesh(rim, rimColours), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        };
        if (floorMaterial != null) open.Bed.SetSurfaceOverrideMaterial(0, floorMaterial);
        else open.Bed.MaterialOverride = new StandardMaterial3D { AlbedoColor = BedColor, Roughness = 1f, MetallicSpecular = 0.2f, VertexColorUseAsAlbedo = true, ResourceName = "open sea bed" };
        open.Bed.SetMeta(LookDirector.DressedMeta, true);
        open.AddChild(open.Bed);
        return open;
    }

    /// <summary>A flat square, half-size reach, facing up, in one colour (the water shader takes the colour as its baked colour).</summary>
    private static ArrayMesh Quad(float reach, Color colour)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-reach, 0, -reach), new Vector3(reach, 0, -reach), new Vector3(reach, 0, reach), new Vector3(-reach, 0, reach) };
        arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Up, Vector3.Up, Vector3.Up, Vector3.Up };
        arrays[(int)Mesh.ArrayType.Color] = new[] { colour, colour, colour, colour };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.CustomAabb = new Aabb(new Vector3(-reach, -0.01f, -reach), new Vector3(2f * reach, 0.02f, 2f * reach));
        return mesh;
    }

    /// <summary>The baked colour of the room's open sea: the mean vertex colour of the sea surface outside the room.</summary>
    private static Color OpenWaterColour(MeshInstance3D sea, Rect2 room)
    {
        var arrays = sea.Mesh.SurfaceGetArrays(0);
        if (arrays[(int)Mesh.ArrayType.Color].VariantType != Variant.Type.PackedColorArray) return new Color(1f, 1f, 1f);
        var colours = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var outside = room.Grow(BackdropReachM);
        float r = 0, g = 0, b = 0;
        var count = 0;
        for (var i = 0; i < vertices.Length; i++)
        {
            var at = sea.GlobalTransform * vertices[i];
            if (outside.HasPoint(new Vector2(at.X, at.Z))) continue;
            r += colours[i].R; g += colours[i].G; b += colours[i].B;
            count++;
        }
        return count == 0 ? new Color(1f, 1f, 1f) : new Color(r / count, g / count, b / count);
    }

    /// <summary>
    /// Lift the distant islands out of a backdrop mesh: every triangle that reaches above IslandCutM under the sea's level, in
    /// connected pieces, each its own mesh with the backdrop's material; the backdrop keeps the rest (the floor, and the islands'
    /// roots deep enough that the water hides them).
    /// </summary>
    private void LiftIslands(MeshInstance3D backdrop)
    {
        var mesh = backdrop.Mesh;
        var arrays = mesh.SurfaceGetArrays(0);
        if (mesh.GetSurfaceCount() != 1 || arrays[(int)Mesh.ArrayType.Index].VariantType != Variant.Type.PackedInt32Array) { Note = "a backdrop mesh is not one indexed surface; its islands stay where they are"; return; }
        for (var channel = (int)Mesh.ArrayType.Custom0; channel < (int)Mesh.ArrayType.Index; channel++)
            if (arrays[channel].VariantType != Variant.Type.Nil) { Note = "a backdrop mesh carries custom or skin channels; its islands stay where they are"; return; }
        var world = backdrop.GlobalTransform;
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var cut = Sea.LevelM - IslandCutM;
        var parent = Enumerable.Range(0, vertices.Length).ToArray();
        int Find(int v) { while (parent[v] != v) v = parent[v] = parent[parent[v]]; return v; }
        var island = new bool[indices.Length / 3];
        for (var t = 0; t < island.Length; t++)
        {
            var (a, b, c) = (indices[3 * t], indices[3 * t + 1], indices[3 * t + 2]);
            if (!new[] { a, b, c }.Any(v => (world * vertices[v]).Y > cut)) continue;
            island[t] = true;
            parent[Find(b)] = Find(a);
            parent[Find(c)] = Find(a);
        }
        var pieces = Enumerable.Range(0, island.Length).Where(t => island[t]).GroupBy(t => Find(indices[3 * t])).ToArray();
        if (pieces.Length == 0) return;
        var material = backdrop.GetSurfaceOverrideMaterial(0) ?? backdrop.MaterialOverride ?? mesh.SurfaceGetMaterial(0);
        foreach (var piece in pieces)
        {
            var used = piece.SelectMany(t => new[] { indices[3 * t], indices[3 * t + 1], indices[3 * t + 2] }).Distinct().ToArray();
            var remap = new Dictionary<int, int>();
            for (var i = 0; i < used.Length; i++) remap[used[i]] = i;
            var part = new Godot.Collections.Array();
            part.Resize((int)Mesh.ArrayType.Max);
            for (var channel = 0; channel < (int)Mesh.ArrayType.Index; channel++)
                part[channel] = Subset(arrays[channel], used, (Mesh.ArrayType)channel);
            part[(int)Mesh.ArrayType.Index] = piece.SelectMany(t => new[] { remap[indices[3 * t]], remap[indices[3 * t + 1]], remap[indices[3 * t + 2]] }).ToArray();
            var pieceMesh = new ArrayMesh();
            pieceMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, part);
            // Its centre and reach above the water, where a swimmer would see its shore.
            var above = used.Select(v => world * vertices[v]).Where(p => p.Y > Sea.LevelM).Select(p => new Vector2(p.X, p.Z)).ToArray();
            if (above.Length == 0) above = used.Select(v => world * vertices[v]).Select(p => new Vector2(p.X, p.Z)).ToArray();
            var middle = above.Aggregate(Vector2.Zero, (s, p) => s + p) / above.Length;
            var shore = above.Max(p => p.DistanceTo(middle));
            var offset = middle - Home;
            // As close as it looks from the edge of the home waters, and never so close that its shore is reached.
            var keep = Mathf.Max(offset.Length() - HomeReachM, shore + ShoreKeepM);
            var instance = new MeshInstance3D
            {
                Name = $"DistantIsland{_islands.Count}", Mesh = pieceMesh, MaterialOverride = null, Transform = world,
                CastShadow = backdrop.CastShadow, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            instance.SetSurfaceOverrideMaterial(0, IslandMaterial(material));
            instance.SetMeta(LookDirector.DressedMeta, true);
            AddChild(instance);
            _islands.Add(new DistantIsland(instance, offset, keep, world.Origin));
        }
    }

    /// <summary>
    /// Lay a backdrop's floor on the bed the bodies touch: every vertex at RoomSea.OpenSeaBedAt under it, facing up, and where an
    /// island was lifted out its ground takes the deep floor's colour. Returns the floor's rim (its outermost ring of vertices, by
    /// angle round Home) and their colours, where the open bed carries on.
    /// </summary>
    private (Vector3[] Rim, Color[] Colours) LayFloor(MeshInstance3D backdrop)
    {
        var mesh = backdrop.Mesh;
        if (mesh.GetSurfaceCount() != 1) return (Array.Empty<Vector3>(), Array.Empty<Color>());
        var arrays = mesh.SurfaceGetArrays(0);
        var world = backdrop.GlobalTransform;
        var local = world.AffineInverse();
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var colours = arrays[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.PackedColorArray ? arrays[(int)Mesh.ArrayType.Color].AsColorArray() : null;
        var cut = Sea.LevelM - IslandCutM;
        var rimRadius = vertices.Select(v => world * v).Max(v => new Vector2(v.X, v.Z).DistanceTo(Home));
        var deep = colours == null ? new Color(1f, 1f, 1f) : Mean(vertices.Select((v, i) => (At: world * v, i))
            .Where(v => new Vector2(v.At.X, v.At.Z).DistanceTo(Home) > rimRadius - 1e-3f).Select(v => colours[v.i]));
        var rim = new List<(float Angle, Vector3 At, Color Colour)>();
        for (var i = 0; i < vertices.Length; i++)
        {
            var at = world * vertices[i];
            var flat = new Vector2(at.X, at.Z);
            if (colours != null && at.Y > cut) colours[i] = deep;
            at.Y = Sea.OpenSeaBedAt(flat);
            vertices[i] = local * at;
            if (flat.DistanceTo(Home) > rimRadius - 1e-3f) rim.Add((Mathf.Atan2(flat.Y - Home.Y, flat.X - Home.X), at, colours?[i] ?? deep));
        }
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = Enumerable.Repeat(local.Basis * Vector3.Up, vertices.Length).Select(n => n.Normalized()).ToArray();
        arrays[(int)Mesh.ArrayType.Tangent] = default;
        if (colours != null) arrays[(int)Mesh.ArrayType.Color] = colours;
        var floor = new ArrayMesh();
        floor.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var material = backdrop.GetSurfaceOverrideMaterial(0);
        backdrop.Mesh = floor;
        if (material != null) backdrop.SetSurfaceOverrideMaterial(0, material);
        _floors.Add(backdrop);
        var ordered = rim.OrderBy(r => r.Angle).ToArray();
        return (ordered.Select(r => r.At).ToArray(), ordered.Select(r => r.Colour).ToArray());
    }

    private static Color Mean(IEnumerable<Color> colours)
    {
        float r = 0, g = 0, b = 0;
        var count = 0;
        foreach (var c in colours) { r += c.R; g += c.G; b += c.B; count++; }
        return count == 0 ? new Color(1f, 1f, 1f) : new Color(r / count, g / count, b / count);
    }

    /// <summary>
    /// The open-sea bed: rings from the floor's rim (its very vertices, so the two meet without a seam) out to BedReachM, every vertex
    /// at RoomSea.OpenSeaBedAt, in the rim's colours. Without a backdrop floor, a disc round Home.
    /// </summary>
    private ArrayMesh BedMesh(Vector3[] rim, Color[] rimColours)
    {
        if (rim.Length < 3)
        {
            rim = Enumerable.Range(0, BedSides).Select(k => Mathf.Tau * k / BedSides).Select(a => new Vector3(Home.X + Mathf.Cos(a) * 0.01f, 0f, Home.Y + Mathf.Sin(a) * 0.01f)).ToArray();
            rimColours = Enumerable.Repeat(new Color(1f, 1f, 1f), rim.Length).ToArray();
        }
        var directions = rim.Select(r => (new Vector2(r.X, r.Z) - Home).Normalized()).ToArray();
        var radius = rim.Average(r => new Vector2(r.X, r.Z).DistanceTo(Home));
        var radii = new List<float>();
        for (var r = radius * 1.08f + 0.5f; ; r = r * 1.3f + 1f)
        {
            radii.Add(Mathf.Min(r, BedReachM));
            if (r >= BedReachM) break;
        }
        var count = rim.Length;
        var positions = new List<Vector3>(rim.Select(r => new Vector3(r.X, Sea.OpenSeaBedAt(new Vector2(r.X, r.Z)), r.Z)));
        foreach (var r in radii)
            foreach (var d in directions)
            {
                var flat = Home + d * r;
                positions.Add(new Vector3(flat.X, Sea.OpenSeaBedAt(flat), flat.Y));
            }
        var indices = new List<int>();
        for (var ring = 0; ring < radii.Count; ring++)
            for (var k = 0; k < count; k++)
            {
                int a = ring * count + k, b = ring * count + (k + 1) % count, c = a + count, d = b + count;
                indices.AddRange(new[] { a, b, d, a, d, c });
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = Enumerable.Repeat(Vector3.Up, positions.Count).ToArray();
        arrays[(int)Mesh.ArrayType.Color] = Enumerable.Range(0, positions.Count).Select(i => rimColours[i % count]).ToArray();
        // Wound clockwise seen from above (Godot's front face), whichever way the rim runs.
        var probe = (positions[1] - positions[0]).Cross(positions[count] - positions[0]);
        arrays[(int)Mesh.ArrayType.Index] = (probe.Y < 0f ? indices : FlipWinding(indices)).ToArray();
        var bed = new ArrayMesh();
        bed.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return bed;
    }

    private static List<int> FlipWinding(List<int> indices)
    {
        var flipped = new List<int>(indices.Count);
        for (var i = 0; i < indices.Count; i += 3) flipped.AddRange(new[] { indices[i], indices[i + 2], indices[i + 1] });
        return flipped;
    }

    private static Variant Subset(Variant channel, int[] used, Mesh.ArrayType type)
    {
        switch (channel.VariantType)
        {
            case Variant.Type.Nil: return channel;
            case Variant.Type.PackedVector3Array: { var a = channel.AsVector3Array(); return used.Select(i => a[i]).ToArray(); }
            case Variant.Type.PackedVector2Array: { var a = channel.AsVector2Array(); return used.Select(i => a[i]).ToArray(); }
            case Variant.Type.PackedColorArray: { var a = channel.AsColorArray(); return used.Select(i => a[i]).ToArray(); }
            case Variant.Type.PackedFloat32Array:
            {
                // Tangents: four floats a vertex.
                var a = channel.AsFloat32Array();
                var width = type == Mesh.ArrayType.Tangent ? 4 : a.Length / Math.Max(1, used.Max() + 1);
                return used.SelectMany(i => Enumerable.Range(0, width).Select(k => a[i * width + k])).ToArray();
            }
            default: throw new InvalidOperationException($"the open sea cannot split a mesh channel of type {channel.VariantType}");
        }
    }

    /// <summary>
    /// Where an island's centre stands, from the home centre, for a camera at camera (XZ, from the home centre). It only ever slides
    /// outward along its own bearing from the home island (so it never comes through it), never falls behind the swimmer along that
    /// bearing (so swimming round it and back never makes it jump), and keeps keepM off the swimmer. From the home waters it stands
    /// where the generator put it. It moves continuously, at the swimmer's speed or, as a swimmer slips past its side, a little faster.
    /// </summary>
    public static Vector2 IslandOffset(Vector2 homeOffset, float keepM, Vector2 camera)
    {
        var distance = homeOffset.Length();
        if (distance < 1e-4f) return homeOffset;
        var bearing = homeOffset / distance;
        var along = camera.Dot(bearing);
        var aside2 = Mathf.Max(camera.LengthSquared() - along * along, 0f);
        var reach = Mathf.Max(distance, along);
        if (aside2 < keepM * keepM) reach = Mathf.Max(reach, along + Mathf.Sqrt(keepM * keepM - aside2));
        return bearing * reach;
    }

    /// <summary>The distant islands' own copy of the backdrop's painterly material, with the haze (SeaLook) turned on.</summary>
    private Material? IslandMaterial(Material? material)
    {
        if (material is not ShaderMaterial land || !land.HasMeta("landscape")) return material;
        if (_islandMaterial != null) return _islandMaterial;
        _islandMaterial = (ShaderMaterial)land.Duplicate();
        _islandMaterial.ResourceName = "distant islands";
        var look = SeaLook.Default;
        _islandMaterial.SetShaderParameter("haze_start_m", look.HazeStartM);
        _islandMaterial.SetShaderParameter("haze_end_m", look.HazeEndM);
        _islandMaterial.SetShaderParameter("haze_max", look.HazeMax);
        return _islandMaterial;
    }

    /// <summary>The sky's horizon (its colour and brightness, as the sky shader draws it) for the far sea and the islands' haze to turn into.</summary>
    public void SetHorizon(Color horizon, float brightness)
    {
        if (_islandMaterial != null)
        {
            _islandMaterial.SetShaderParameter("haze_color", horizon);
            _islandMaterial.SetShaderParameter("haze_energy", brightness);
        }
        foreach (var material in _water)
        {
            material.SetShaderParameter("horizon_color", horizon);
            material.SetShaderParameter("horizon_energy", brightness);
        }
    }

    private void SetFar(float far)
    {
        if (Mathf.IsEqualApprox(far, _far)) return;
        _far = far;
        _islandMaterial?.SetShaderParameter("haze_far_m", far);
        foreach (var material in _water)
        {
            material.SetShaderParameter("horizon_fade_start_m", far * HorizonFadeStart);
            material.SetShaderParameter("horizon_fade_end_m", far * HorizonFadeEnd);
        }
    }

    /// <summary>Follow a camera now (the look calls this every frame from _Process; tests call it directly).</summary>
    public void Follow(Camera3D camera)
    {
        var at = camera.GlobalPosition;
        if (Surface != null) Surface.GlobalPosition = new Vector3(at.X, Sea.LevelM, at.Z);
        SetFar(camera.Far);
        var from = new Vector2(at.X, at.Z) - Home;
        foreach (var island in _islands)
        {
            var shift = IslandOffset(island.HomeOffset, island.KeepM, from) - island.HomeOffset;
            island.Mesh.GlobalPosition = island.BasePosition + new Vector3(shift.X, 0f, shift.Y);
        }
    }

    public override void _Process(double delta)
    {
        var camera = Camera != null && IsInstanceValid(Camera) && Camera.IsInsideTree() ? Camera : GetViewport()?.GetCamera3D();
        if (camera != null) Follow(camera);
    }
}
