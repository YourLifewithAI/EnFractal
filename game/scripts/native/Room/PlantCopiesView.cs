using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EnFractal.Native.Look;

namespace EnFractal.Native.Room;

/// <summary>
/// The room's plant copies in the scene (the 10x world, packet S6): per set and 8 m tile, one MultiMesh per distance band (the
/// set's meshes by detail, DrawDistances), and the copies' collision built from their rows (PlantCollision) as static physics
/// bodies on the server, one per tile and layer, with no node per copy. Tiles are built nearest the camera first, a few each
/// frame within DrawDistances.BuildBudgetMs, so a 350,000-copy island arrives without a hitch; the collision within
/// DrawDistances.NearCollisionM of the spawns is built when the view enters the scene, before the bodies are placed.
///
/// Nothing here is authoritative: the copies are the manifest's, and a copy is the entity plant:&lt;set&gt;-&lt;row&gt;, never a
/// node. A hit on a copy's collision names its copy through EntityAt (the holder node the body reports and the shape's index).
/// Layers: a tree's trunk and a solid stone are on the world layer (they block sight, reach and the camera, and navigation
/// avoids them); a tree's pole and caps are on the hidden layer, which bodies climb and stand on and nothing else sees.
/// </summary>
public partial class PlantCopiesView : Node3D
{
    public const string ViewName = "Copies";
    /// <summary>Meta on a collision holder: the set's name, and per shape index the row it belongs to.</summary>
    public const string PlantSetMeta = "plant_set", PlantRowsMeta = "plant_rows";

    private RoomData _room = null!;
    private SetView[] _sets = Array.Empty<SetView>();
    private readonly List<Work> _pending = new();
    private readonly List<Rid> _bodies = new();
    private readonly List<Rid> _shapes = new();
    private readonly Dictionary<(int Set, int Tile), Node3D> _tiles = new();
    private Vector3 _sortedAround = new(float.NaN, 0, float.NaN);
    private int _framesSinceSort;

    /// <summary>Tiles drawn and tiles whose collision is built so far, of VisualTiles and CollisionTiles in all.</summary>
    public int VisualTilesBuilt { get; private set; }
    public int CollisionTilesBuilt { get; private set; }
    public int VisualTiles { get; private set; }
    public int CollisionTiles { get; private set; }
    /// <summary>Tiles whose collision was built on entering the scene, near the spawns.</summary>
    public int NearCollisionTiles { get; private set; }
    public bool Complete => IsInsideTree() && _pending.Count == 0;
    /// <summary>Frames that built something, the main thread's time they spent, the longest, and the most tiles one frame built.</summary>
    public int BuildFrames { get; private set; }
    public double BuildMs { get; private set; }
    public double LongestFrameMs { get; private set; }
    public int MostTilesInAFrame { get; private set; }
    public double EnterMs { get; private set; }
    /// <summary>The slowest single tile and what it was (set, tile, drawn or collided), for measurements.</summary>
    public double LongestTileMs { get; private set; }
    public string LongestTile { get; private set; } = "";

    private sealed class SetView
    {
        public required PlantSet Set;
        public required Node3D Node;
        public required Mesh[] Meshes;
        public required CopyBand[] Bands;
    }

    private readonly record struct Work(int Set, int Tile, bool Collision, Vector2 Centre);

    /// <summary>The view of a room's copies, its meshes loaded and painted; it builds once it is in the scene.</summary>
    public static PlantCopiesView Create(RoomData room)
    {
        var copies = room.Copies ?? throw new ArgumentException("the room has no plant copies", nameof(room));
        var view = new PlantCopiesView { Name = ViewName, _room = room };
        view.SetMeta("plant_copies", copies.TotalCount);
        view._sets = copies.Sets.Select(set =>
        {
            var node = new Node3D { Name = set.Name };
            node.SetMeta(PlantSetMeta, set.Name);
            node.SetMeta("species", set.Species);
            node.SetMeta("plant_kind", set.Kind.ToString().ToLowerInvariant());
            view.AddChild(node);
            var meshes = set.MeshPaths.Select(path => CopyMesh(room, path, set.Name)).ToArray();
            return new SetView { Set = set, Node = node, Meshes = meshes, Bands = DrawDistances.CopyBands(set.Kind, TypicalHeightM(set), meshes.Length) };
        }).ToArray();
        for (var s = 0; s < view._sets.Length; s++)
        {
            var set = view._sets[s].Set;
            for (var t = 0; t < set.TileCount; t++)
            {
                var (tx, tz, _, _) = set.Tile(t);
                var centre = new Vector2((tx + 0.5f) * PlantCopies.TileM, (tz + 0.5f) * PlantCopies.TileM);
                view._pending.Add(new Work(s, t, false, centre));
                view.VisualTiles++;
                if (set.Collides == "none") continue;
                view._pending.Add(new Work(s, t, true, centre));
                view.CollisionTiles++;
            }
        }
        return view;
    }

    public override void _Ready()
    {
        var clock = Stopwatch.StartNew();
        // The bodies are placed at their spawns on the next physics frame (RoomWorld): what they could touch there exists first.
        var spawns = _room.Spawns.Select(s => new Vector2(s.PositionM.X, s.PositionM.Z)).ToArray();
        var near = _pending.Where(w => w.Collision && spawns.Any(s => TileDistance(w, s) <= DrawDistances.NearCollisionM)).ToHashSet();
        foreach (var work in near) Build(work);
        _pending.RemoveAll(near.Contains);
        NearCollisionTiles = near.Count;
        EnterMs = clock.Elapsed.TotalMilliseconds;
    }

    public override void _Process(double delta)
    {
        if (_pending.Count == 0) return;
        var focus = Focus();
        if (++_framesSinceSort >= 30 || !(focus.DistanceSquaredTo(_sortedAround) < 4f))
        {
            // Farthest first in the list, so the nearest is taken from its end.
            var at = new Vector2(focus.X, focus.Z);
            _pending.Sort((a, b) => TileDistance(b, at).CompareTo(TileDistance(a, at)));
            _sortedAround = focus;
            _framesSinceSort = 0;
        }
        var clock = Stopwatch.StartNew();
        var built = 0;
        while (_pending.Count > 0 && (built == 0 || clock.Elapsed.TotalMilliseconds < DrawDistances.BuildBudgetMs))
        {
            var work = _pending[^1];
            _pending.RemoveAt(_pending.Count - 1);
            Build(work);
            built++;
        }
        var spent = clock.Elapsed.TotalMilliseconds;
        BuildFrames++;
        BuildMs += spent;
        LongestFrameMs = Math.Max(LongestFrameMs, spent);
        MostTilesInAFrame = Math.Max(MostTilesInAFrame, built);
        if (_pending.Count > 0) return;
        SetProcess(false);
        GD.Print(System.FormattableString.Invariant(
            $"ROOM_COPIES {_room.Copies!.TotalCount} copies of {_sets.Length} sets: {VisualTiles} tiles drawn, {CollisionTiles} collided ({NearCollisionTiles} near the spawns on entering, {EnterMs:0} ms), over {BuildFrames} frames, {BuildMs:0} ms in all, longest frame {LongestFrameMs:0.0} ms; slowest tile: {LongestTile}"));
    }

    /// <summary>The copies' bodies leave the physics space with the view and come back with it; they and their shapes are freed with the view.</summary>
    public override void _Notification(int what)
    {
        switch ((long)what)
        {
            case NotificationEnterTree:
                foreach (var body in _bodies)
                {
                    PhysicsServer3D.BodySetState(body, PhysicsServer3D.BodyState.Transform, GlobalTransform);
                    PhysicsServer3D.BodySetSpace(body, GetWorld3D().Space);
                }
                break;
            case NotificationExitTree:
                foreach (var body in _bodies) PhysicsServer3D.BodySetSpace(body, default);
                break;
            case NotificationPredelete:
                foreach (var body in _bodies) PhysicsServer3D.FreeRid(body);
                foreach (var shape in _shapes) PhysicsServer3D.FreeRid(shape);
                _bodies.Clear();
                _shapes.Clear();
                break;
        }
    }

    /// <summary>
    /// The copy a collision hit belongs to, as its entity id (plant:&lt;set&gt;-&lt;row&gt;), or "" when the collider is not a copy's.
    /// Pass a ray or shape query's "collider" and "shape" (or a KinematicCollision3D's collider and shape index).
    /// </summary>
    public static string EntityAt(GodotObject? collider, int shape)
    {
        if (collider is not Node node || !node.HasMeta(PlantRowsMeta) || !node.HasMeta(PlantSetMeta)) return "";
        var rows = node.GetMeta(PlantRowsMeta).AsInt32Array();
        return shape >= 0 && shape < rows.Length ? PlantCopies.IdOf(node.GetMeta(PlantSetMeta).AsString(), rows[shape]) : "";
    }

    /// <summary>The tile node holding a set's tile (its bands and collision holders), once built.</summary>
    public Node3D? TileNode(string set, int tx, int tz)
    {
        var index = Array.FindIndex(_sets, s => s.Set.Name == set);
        if (index < 0) return null;
        var tile = _sets[index].Set.FindTile(tx, tz);
        return tile >= 0 && _tiles.TryGetValue((index, tile), out var node) ? node : null;
    }

    private Vector3 Focus()
    {
        if (GetViewport()?.GetCamera3D() is { } camera && camera.IsInsideTree()) return camera.GlobalPosition;
        return _room.SpawnFor("player").PositionM;
    }

    private static float TileDistance(Work work, Vector2 at) => work.Centre.DistanceTo(at);

    private void Build(Work work)
    {
        var clock = Stopwatch.StartNew();
        if (work.Collision) BuildCollision(work.Set, work.Tile);
        else BuildVisual(work.Set, work.Tile);
        var spent = clock.Elapsed.TotalMilliseconds;
        var (tx, tz, _, count) = _sets[work.Set].Set.Tile(work.Tile);
        _slowest.Add((spent, $"{_sets[work.Set].Set.Name} ({tx}, {tz}) {count} {(work.Collision ? "collided" : "drawn")} {spent:0.0} ms"));
        _slowest.Sort((a, b) => b.Ms.CompareTo(a.Ms));
        if (_slowest.Count > 6) _slowest.RemoveAt(_slowest.Count - 1);
        LongestTileMs = _slowest[0].Ms;
        LongestTile = string.Join("; ", _slowest.Select(t => t.What));
    }

    private readonly List<(double Ms, string What)> _slowest = new();

    private Node3D TileNodeFor(int set, int tile)
    {
        if (_tiles.TryGetValue((set, tile), out var node)) return node;
        var (tx, tz, _, _) = _sets[set].Set.Tile(tile);
        node = new Node3D { Name = $"Tile_{tx}_{tz}" };
        node.SetMeta("tile", new Vector2I(tx, tz));
        _sets[set].Node.AddChild(node);
        _tiles[(set, tile)] = node;
        return node;
    }

    /// <summary>
    /// One tile of one set: its rows' transforms and tints in one MultiMesh buffer (12 floats of transform, basis rows with the
    /// origin, then 4 of colour), shared by one MultiMesh per band. The instance colour is the row's tint: Godot multiplies it
    /// into the mesh's vertex colour, as the exporter's merged meshes baked it.
    /// </summary>
    private void BuildVisual(int setIndex, int tile)
    {
        var view = _sets[setIndex];
        var set = view.Set;
        var (_, _, first, count) = set.Tile(tile);
        var buffer = InstanceBuffer(set, first, count);
        var node = TileNodeFor(setIndex, tile);
        for (var b = 0; b < view.Bands.Length; b++)
        {
            var band = view.Bands[b];
            var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = view.Meshes[band.Mesh] };
            multimesh.InstanceCount = count;
            multimesh.Buffer = buffer;
            node.AddChild(new MultiMeshInstance3D
            {
                Name = $"Band{b}", Multimesh = multimesh,
                VisibilityRangeBegin = band.BeginM, VisibilityRangeBeginMargin = band.BeginM > 0 ? DrawDistances.CopyMarginM : 0f,
                VisibilityRangeEnd = band.EndM, VisibilityRangeEndMargin = band.EndM > 0 ? DrawDistances.CopyMarginM : 0f,
                CastShadow = band.CastsShadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            });
        }
        VisualTilesBuilt++;
    }

    /// <summary>
    /// Rows first to first + count as a MultiMesh buffer with colours: per instance 16 floats, the transform's basis rows each
    /// followed by its origin's coordinate (Godot's layout, RenderingServer.MultimeshSetBuffer), then the tint and an alpha of 1.
    /// </summary>
    public static float[] InstanceBuffer(PlantSet set, int first, int count)
    {
        var buffer = new float[count * 16];
        var data = set.Data;
        for (var i = 0; i < count; i++)
        {
            var row = data.AsSpan(set.RowsAt + (first + i) * set.RowBytes, PlantSet.RowBytesBase);
            var (sin, cos) = MathF.SinCos(Field(row, 3) * (MathF.PI / 180f));
            float sx = Field(row, 4), sy = Field(row, 5), sz = Field(row, 6);
            // The basis is yaw about +Y times the scale: its columns sx (cos, 0, -sin), sy (0, 1, 0) and sz (sin, 0, cos).
            var o = i * 16;
            buffer[o] = sx * cos; buffer[o + 1] = 0f; buffer[o + 2] = sz * sin; buffer[o + 3] = Field(row, 0);
            buffer[o + 4] = 0f; buffer[o + 5] = sy; buffer[o + 6] = 0f; buffer[o + 7] = Field(row, 1);
            buffer[o + 8] = -sx * sin; buffer[o + 9] = 0f; buffer[o + 10] = sz * cos; buffer[o + 11] = Field(row, 2);
            buffer[o + 12] = Field(row, 7); buffer[o + 13] = Field(row, 8); buffer[o + 14] = Field(row, 9); buffer[o + 15] = 1f;
        }
        return buffer;
    }

    private static float Field(ReadOnlySpan<byte> row, int field) => System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(row[(4 * field)..]);

    /// <summary>
    /// One tile's collision: trees' trunks (world) and poles and caps (hidden), or solid stones' hulls (world). Every shape is a
    /// shared unit shape (PlantCollision), placed per row by a scaling transform; each body is the tile's, one per layer.
    /// </summary>
    private void BuildCollision(int setIndex, int tile)
    {
        var set = _sets[setIndex].Set;
        var (_, _, first, count) = set.Tile(tile);
        var node = TileNodeFor(setIndex, tile);
        if (set.Kind == PlantKind.Tree)
        {
            var trunks = new List<(Rid, Transform3D, int)>(count);
            var climb = new List<(Rid, Transform3D, int)>(count * 3);
            for (var row = first; row < first + count; row++)
            {
                var tree = set.Tree(row);
                var taper = PlantCollision.TaperSteps(tree);
                trunks.Add((Shared($"trunk {taper}", () => Convex(PlantCollision.UnitTrunk(taper))), PlantCollision.TrunkPlace(tree), row));
                climb.Add((Shared("pole", () => Convex(PlantCollision.UnitPole())), PlantCollision.PolePlace(tree), row));
                foreach (var cap in tree.Caps)
                {
                    var steps = PlantCollision.CapSteps(cap);
                    climb.Add((Shared($"cap {steps.X} {steps.Z}", () => Concave(PlantCollision.UnitCapFaces(steps))), PlantCollision.CapPlace(cap), row));
                }
            }
            Body(Holder(node, "Trunks", set.Name, "bark", true), RoomBuilder.WorldLayer, trunks);
            Body(Holder(node, "Climb", set.Name, "foliage", false), RoomBuilder.HiddenLayer, climb);
        }
        else if (set.Collides == "solid" && set.SolidPoints.Count > 0)
        {
            var hull = Shared("solid " + set.Name, () => Convex(set.SolidPoints.ToArray()));
            var solids = new List<(Rid, Transform3D, int)>(count);
            for (var row = first; row < first + count; row++) solids.Add((hull, set.Transform(row), row));
            Body(Holder(node, "Solids", set.Name, set.Kind == PlantKind.Stone ? "rock" : "foliage", true), RoomBuilder.WorldLayer, solids);
        }
        CollisionTilesBuilt++;
    }

    private readonly Dictionary<string, Rid> _shared = new(StringComparer.Ordinal);

    private Rid Shared(string key, Func<Rid> create)
    {
        if (!_shared.TryGetValue(key, out var shape)) _shared[key] = shape = create();
        return shape;
    }

    /// <summary>The node a body reports as its collider: what it is (the set, a material role for nature's surface rule, drawn or collision only) and its shapes' rows.</summary>
    private static Node3D Holder(Node3D tile, string name, string set, string materialRole, bool drawn)
    {
        var holder = new Node3D { Name = name };
        holder.SetMeta(PlantSetMeta, set);
        holder.SetMeta("material_role", materialRole);
        holder.SetMeta("drawn", drawn);
        tile.AddChild(holder);
        return holder;
    }

    private void Body(Node3D holder, uint layer, List<(Rid Shape, Transform3D Place, int Row)> shapes)
    {
        holder.SetMeta(PlantRowsMeta, shapes.Select(s => s.Row).ToArray());
        var body = PhysicsServer3D.BodyCreate();
        PhysicsServer3D.BodySetMode(body, PhysicsServer3D.BodyMode.Static);
        PhysicsServer3D.BodySetCollisionLayer(body, layer);
        PhysicsServer3D.BodySetCollisionMask(body, 0);
        foreach (var (shape, place, _) in shapes) PhysicsServer3D.BodyAddShape(body, shape, place);
        PhysicsServer3D.BodySetState(body, PhysicsServer3D.BodyState.Transform, GlobalTransform);
        PhysicsServer3D.BodyAttachObjectInstanceId(body, holder.GetInstanceId());
        PhysicsServer3D.BodySetSpace(body, GetWorld3D().Space);
        _bodies.Add(body);
    }

    private Rid Convex(Vector3[] points)
    {
        var shape = PhysicsServer3D.ConvexPolygonShapeCreate();
        PhysicsServer3D.ShapeSetData(shape, points);
        PhysicsServer3D.ShapeSetMargin(shape, PlantCollision.ConvexMarginM);
        _shapes.Add(shape);
        return shape;
    }

    private Rid Concave(Vector3[] faces)
    {
        var shape = PhysicsServer3D.ConcavePolygonShapeCreate();
        PhysicsServer3D.ShapeSetData(shape, new Godot.Collections.Dictionary { ["faces"] = faces, ["backface_collision"] = false });
        _shapes.Add(shape);
        return shape;
    }

    /// <summary>A set's typical height: its mesh's height times the mean y scale of up to 1,024 rows spread over its table.</summary>
    private static float TypicalHeightM(PlantSet set)
    {
        var step = Math.Max(1, set.Count / 1024);
        double sum = 0;
        var n = 0;
        for (var row = 0; row < set.Count; row += step, n++) sum += set.Scale(row).Y;
        return set.MeshHeightM * (float)(sum / Math.Max(1, n));
    }

    /// <summary>
    /// One of a set's meshes as a single mesh for a MultiMesh: the GLB's meshes merged (their node transforms applied), each
    /// surface keeping its material, painted as the look paints the land: a landscape material (its glTF extras name a harness
    /// role and say its colours are baked) becomes that role's painterly land material, which keeps the vertex colour the
    /// copies' tints multiply; any other becomes the painterly material of its own name's role.
    /// </summary>
    private static Mesh CopyMesh(RoomData room, string path, string set)
    {
        var scene = RoomBuilder.LoadGlb(room, path, "plant set " + set);
        var parts = new List<(MeshInstance3D Mesh, Transform3D At)>();
        Collect(scene, Transform3D.Identity, parts, root: true);
        if (parts.Count == 0) throw new RoomLoadException($"plant set {set}: {path} has no mesh");
        var merged = new ArrayMesh();
        foreach (var (instance, at) in parts)
            for (var surface = 0; surface < instance.Mesh.GetSurfaceCount(); surface++)
            {
                var tool = new SurfaceTool();
                tool.AppendFrom(instance.Mesh, surface, at);
                var material = instance.GetActiveMaterial(surface);
                var source = material as BaseMaterial3D;
                if (LandscapeLook.TryRole(material, out var role) && source != null)
                    tool.SetMaterial(MaterialLibrary.ForLandscape(role, source.AlbedoColor, LandscapeLook.BakeColor(instance.Mesh, surface, source.AlbedoColor)));
                else if (source != null)
                    tool.SetMaterial(MaterialLibrary.ForCaptured(source.ResourceName.Length > 0 ? source.ResourceName : "foliage", source.AlbedoColor, source.AlbedoTexture));
                else if (material != null) tool.SetMaterial(material);
                tool.Commit(merged);
            }
        scene.Free();
        return merged;
    }

    private static void Collect(Node node, Transform3D parent, List<(MeshInstance3D, Transform3D)> parts, bool root)
    {
        var at = node is Node3D spatial && !root ? parent * spatial.Transform : parent;
        if (node is MeshInstance3D { Mesh: not null } mesh) parts.Add((mesh, at));
        foreach (var child in node.GetChildren()) Collect(child, at, parts, root: false);
    }
}
