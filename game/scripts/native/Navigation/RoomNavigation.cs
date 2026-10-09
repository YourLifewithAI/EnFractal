using Godot;
using System;
using System.Diagnostics;
using EnFractal.Native.Room;

namespace EnFractal.Native.Navigation;

/// <summary>
/// Where the companion can walk: a navigation mesh baked at runtime from the room's static collision
/// (StaticBody3D colliders on the world layer under <see cref="SourceRoot"/>), eroded by the companion's
/// radius plus a little clearance, on a navigation map of its own so its cell size never conflicts with
/// anything else. The mesh is derived data, rebuilt whenever that geometry is added, removed or moved;
/// nothing here is authoritative. A route that cannot reach its goal says so, so the companion can report
/// blocked honestly instead of teleporting.
/// </summary>
public partial class RoomNavigation : Node
{
    /// <summary>
    /// 2 cm cells: the 10 cm companion's 2 cm radius plus clearance is exactly two cells, so the agent radius needs no
    /// rounding, and a bake of the test room stays near 20 ms. 1 cm layers resolve the 6 mm rug and the 1 cm climb.
    /// </summary>
    public const float CellSizeM = 0.02f;
    public const float CellHeightM = 0.01f;
    /// <summary>Added to the body radius so routes keep the body a body-width (2 cm) off walls and corners.</summary>
    public const float ClearanceM = 0.02f;
    /// <summary>How often the source geometry is checked for changes, and how long it must hold still before a re-bake.</summary>
    public const double PollIntervalS = 0.25;
    public const double SettleS = 0.2;
    /// <summary>
    /// A route reaches its goal only when it ends this close to the goal's own nearest walkable point (2.5 cells): a
    /// body hugging a wall stands up to 2 cm inside the eroded margin, while across even a 1 cm wall the two points
    /// are at least 7 cm apart.
    /// </summary>
    public const float SameIslandM = 0.05f;

    /// <summary>A walkable route: corner points from the start's projection onto the mesh to the end.</summary>
    public readonly record struct Route(Vector3[] Points, bool Reaches, bool Direct, float LengthM)
    {
        public static readonly Route None = new(Array.Empty<Vector3>(), false, false, 0);
        public bool IsEmpty => Points.Length == 0;
    }

    public Node3D SourceRoot { get; private set; } = null!;
    /// <summary>The baking box in the source root's space: the room bounds, a little deeper below and above.</summary>
    public Aabb BakeBounds { get; private set; }
    /// <summary>The horizontal cell of this mesh (CellSizeM unless a measurement asks for another).</summary>
    public float CellM { get; private set; } = CellSizeM;
    public float AgentRadiusM { get; private set; }
    public float AgentHeightM { get; private set; }
    public float AgentMaxClimbM { get; private set; }
    public Rid Map { get; private set; }
    /// <summary>Completed bakes; a route planned on an older revision should be planned again. The map holds a bake from the next physics frame.</summary>
    public int Revision { get; private set; }
    public int PolygonCount { get; private set; }
    public double LastBakeMs { get; private set; }
    /// <summary>True from the first bake that produced a walkable mesh, once the map has synchronised.</summary>
    public bool IsReady => Revision > 0 && PolygonCount > 0 && Map.IsValid && NavigationServer3D.MapGetIterationId(Map) > 0;

    private Rid _region;
    private int _signature;
    /// <summary>Change counts of shapes whose data is too large to hash each poll (concave meshes, height maps), from their changed signal.</summary>
    private readonly System.Collections.Generic.Dictionary<ulong, int> _shapeVersions = new();
    private bool _changed;
    private double _sincePoll;
    private double _sinceChange;

    /// <summary>The integrator's one-line wiring: bake the built room for the companion and give it the map.</summary>
    public static RoomNavigation Attach(RoomWorld world)
    {
        // An island bakes its land only: the sea floor out to the reef doubled the bake (garage: 86-114 ms against 50-57 ms),
        // and nobody walks there (swimmers swim, the Gubble floats).
        var bounds = world.Room.Sea?.LandBounds(world.Room.Bounds) ?? world.Room.Bounds;
        var navigation = Create(world, world.Built, bounds, WorldScaleProfile.Companion, world.Companion.StepHeightM * 0.75f);
        world.Companion.BindNavigation(navigation);
        return navigation;
    }

    /// <param name="maxClimbM">Kept below the body's real step so a planned route never asks for a climb the body might fail.</param>
    public static RoomNavigation Create(Node parent, Node3D sourceRoot, Aabb bounds, WorldScaleProfile agent, float maxClimbM, float cellM = CellSizeM)
    {
        var navigation = new RoomNavigation
        {
            Name = "RoomNavigation", SourceRoot = sourceRoot, CellM = cellM,
            BakeBounds = new Aabb(bounds.Position - new Vector3(0, 0.1f, 0), bounds.Size + new Vector3(0, 0.2f, 0)),
            // Whole cells, as the baker rounds them anyway (and warns when it has to). For the 10 cm companion:
            // 2 + 2 cm clearance is 4 cm, the height 10 cm, and three quarters of its 2 cm step (1.5 cm) rounds
            // down to a 1 cm climb (the 0.24 m body had 8 cm, 24 cm and 3 cm).
            AgentRadiusM = Cells((float)agent.RadiusMeters + ClearanceM, cellM, up: true),
            AgentHeightM = Cells((float)agent.HeightMeters, CellHeightM, up: true),
            AgentMaxClimbM = Mathf.Max(CellHeightM, Cells(maxClimbM, CellHeightM, up: false))
        };
        parent.AddChild(navigation);
        return navigation;
    }

    public override void _Ready()
    {
        Map = NavigationServer3D.MapCreate();
        NavigationServer3D.MapSetCellSize(Map, CellM);
        NavigationServer3D.MapSetCellHeight(Map, CellHeightM);
        NavigationServer3D.MapSetUp(Map, Vector3.Up);
        // Synchronous map updates at the end of each physics frame: a bake is in every route from the next frame.
        NavigationServer3D.MapSetUseAsyncIterations(Map, false);
        NavigationServer3D.MapSetActive(Map, true);
        _region = NavigationServer3D.RegionCreate();
        NavigationServer3D.RegionSetUseAsyncIterations(_region, false);
        NavigationServer3D.RegionSetMap(_region, Map);
        Bake();
    }

    public override void _ExitTree()
    {
        if (_region.IsValid) NavigationServer3D.FreeRid(_region);
        if (Map.IsValid) NavigationServer3D.FreeRid(Map);
        _region = default;
        Map = default;
    }

    /// <summary>Re-bake at the next poll even if no change was seen (for geometry changes a poll cannot detect).</summary>
    public void MarkDirty()
    {
        _changed = true;
        _sinceChange = SettleS;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsInstanceValid(SourceRoot) || !SourceRoot.IsInsideTree()) return;
        _sincePoll += delta;
        _sinceChange += delta;
        if (_sincePoll < PollIntervalS) return;
        _sincePoll = 0;
        var signature = Signature();
        if (signature != _signature)
        {
            // Still moving (a carried or dragged object): wait until it holds still.
            _signature = signature;
            _changed = true;
            _sinceChange = 0;
            return;
        }
        if (_changed && _sinceChange >= SettleS) Bake();
    }

    /// <summary>Bake now from the source root's current static collision.</summary>
    public void Bake()
    {
        if (!Map.IsValid || !IsInstanceValid(SourceRoot) || !SourceRoot.IsInsideTree()) return;
        var watch = Stopwatch.StartNew();
        var mesh = new NavigationMesh
        {
            GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
            GeometryCollisionMask = RoomBuilder.WorldLayer,
            GeometrySourceGeometryMode = NavigationMesh.SourceGeometryMode.RootNodeChildren,
            CellSize = CellM, CellHeight = CellHeightM,
            // The default 6 cells, except on finer cells, where Godot clamps the sample distance to 0.1 m (and warns).
            DetailSampleDistance = Mathf.Max(6.0f, Mathf.Ceil(0.1f / CellM) + 1),
            AgentRadius = AgentRadiusM, AgentHeight = AgentHeightM, AgentMaxClimb = AgentMaxClimbM, AgentMaxSlope = 45.0f,
            // Low ceilings and ledges are not walkable; a low lip within the climb is.
            FilterWalkableLowHeightSpans = true, FilterLedgeSpans = true, FilterLowHangingObstacles = true,
            FilterBakingAabb = BakeBounds
        };
        var source = new NavigationMeshSourceGeometryData3D();
        NavigationServer3D.ParseSourceGeometryData(mesh, source, SourceRoot);
        if (source.HasData()) NavigationServer3D.BakeFromSourceGeometryData(mesh, source);
        NavigationServer3D.RegionSetTransform(_region, SourceRoot.GlobalTransform);
        NavigationServer3D.RegionSetNavigationMesh(_region, mesh);
        PolygonCount = mesh.GetPolygonCount();
        _signature = Signature();
        _changed = false;
        Revision++;
        LastBakeMs = watch.Elapsed.TotalMilliseconds;
        GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"ROOM_NAVIGATION bake {Revision}: {PolygonCount} polygons in {LastBakeMs:0.0} ms (agent radius {AgentRadiusM:0.000} m, height {AgentHeightM:0.00} m, climb {AgentMaxClimbM:0.000} m)"));
    }

    /// <summary>
    /// The walkable route from one point to another. Reaches is false when the goal is not on the start's walkable
    /// island: the route must end at the goal's own nearest walkable point, and within the planar tolerance of the goal.
    /// (Within the tolerance alone was not enough: across a thin wall the route's end can be a few centimetres from a
    /// goal it can never reach. Lane P review.)
    /// </summary>
    public Route FindRoute(Vector3 from, Vector3 to, float toleranceM)
    {
        if (!IsReady || !from.IsFinite() || !to.IsFinite()) return Route.None;
        var points = NavigationServer3D.MapGetPath(Map, from, to, true);
        if (points.Length == 0) return Route.None;
        var end = points[^1];
        var island = NavigationServer3D.MapGetClosestPoint(Map, to);
        var reaches = Planar(end - to).Length() <= toleranceM && end.DistanceTo(island) <= SameIslandM;
        var length = Planar(points[0] - from).Length();
        for (var i = 1; i < points.Length; i++) length += Planar(points[i] - points[i - 1]).Length();
        return new Route(points, reaches, points.Length <= 2, length);
    }

    /// <summary>The nearest walkable point to a position (the position itself when the mesh is not ready).</summary>
    public Vector3 ClosestPoint(Vector3 point) => IsReady && point.IsFinite() ? NavigationServer3D.MapGetClosestPoint(Map, point) : point;

    private static Vector3 Planar(Vector3 value) => new(value.X, 0, value.Z);

    private static float Cells(float metres, float cell, bool up) =>
        (up ? Mathf.Ceil(metres / cell - 0.001f) : Mathf.Floor(metres / cell + 0.001f)) * cell;

    /// <summary>
    /// A cheap fingerprint of the static collision the mesh is baked from: which bodies and shapes, where, and what each
    /// shape is. A shape resized in place keeps its instance id, so its dimensions are hashed too (Lane P review).
    /// </summary>
    private int Signature()
    {
        var hash = new HashCode();
        Accumulate(SourceRoot, ref hash);
        return hash.ToHashCode();
    }

    private void Accumulate(Node node, ref HashCode hash)
    {
        if (node is StaticBody3D body && (body.CollisionLayer & RoomBuilder.WorldLayer) != 0)
        {
            hash.Add(body.GetInstanceId());
            hash.Add(Quantize(body.GlobalTransform));
            foreach (var child in body.GetChildren())
                if (child is CollisionShape3D shape)
                {
                    hash.Add(shape.GetInstanceId());
                    hash.Add(shape.Disabled);
                    hash.Add(shape.Shape?.GetInstanceId() ?? 0);
                    hash.Add(ShapeContent(shape.Shape));
                    hash.Add(Quantize(shape.Transform));
                }
        }
        foreach (var child in node.GetChildren()) Accumulate(child, ref hash);
    }

    /// <summary>The shape's dimensions to the millimetre; for mesh-sized data, a count of its changed signals instead.</summary>
    private int ShapeContent(Shape3D? shape)
    {
        var hash = new HashCode();
        switch (shape)
        {
            case null: return 0;
            case BoxShape3D box: hash.Add(Millimetres(box.Size)); break;
            case SphereShape3D sphere: hash.Add(Millimetres(sphere.Radius)); break;
            case CapsuleShape3D capsule: hash.Add(Millimetres(capsule.Radius)); hash.Add(Millimetres(capsule.Height)); break;
            case CylinderShape3D cylinder: hash.Add(Millimetres(cylinder.Radius)); hash.Add(Millimetres(cylinder.Height)); break;
            case ConvexPolygonShape3D convex:
                foreach (var point in convex.Points) hash.Add(Millimetres(point));
                break;
            default:
                var id = shape.GetInstanceId();
                if (!_shapeVersions.ContainsKey(id))
                {
                    _shapeVersions[id] = 0;
                    shape.Changed += () => _shapeVersions[id] = _shapeVersions.GetValueOrDefault(id) + 1;
                }
                hash.Add(_shapeVersions[id]);
                break;
        }
        return hash.ToHashCode();
    }

    private static int Millimetres(float value) => Mathf.RoundToInt(value * 1000);

    private static int Millimetres(Vector3 value) => HashCode.Combine(Millimetres(value.X), Millimetres(value.Y), Millimetres(value.Z));

    /// <summary>Millimetre and milliradian resolution, so float noise never triggers a re-bake.</summary>
    private static int Quantize(Transform3D transform)
    {
        var hash = new HashCode();
        foreach (var value in new[] { transform.Origin, transform.Basis.X, transform.Basis.Y, transform.Basis.Z })
        {
            hash.Add(Mathf.RoundToInt(value.X * 1000));
            hash.Add(Mathf.RoundToInt(value.Y * 1000));
            hash.Add(Mathf.RoundToInt(value.Z * 1000));
        }
        return hash.ToHashCode();
    }
}
