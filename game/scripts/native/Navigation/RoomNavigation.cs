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

    /// <summary>A walkable route: corner points from the start's projection onto the mesh to the end.</summary>
    public readonly record struct Route(Vector3[] Points, bool Reaches, bool Direct, float LengthM)
    {
        public static readonly Route None = new(Array.Empty<Vector3>(), false, false, 0);
        public bool IsEmpty => Points.Length == 0;
    }

    public Node3D SourceRoot { get; private set; } = null!;
    /// <summary>The baking box in the source root's space: the room bounds, a little deeper below and above.</summary>
    public Aabb BakeBounds { get; private set; }
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
    private bool _changed;
    private double _sincePoll;
    private double _sinceChange;

    /// <summary>The integrator's one-line wiring: bake the built room for the companion and give it the map.</summary>
    public static RoomNavigation Attach(RoomWorld world)
    {
        var bounds = world.Room.Bounds;
        var navigation = Create(world, world.Built, bounds, WorldScaleProfile.Companion, world.Companion.StepHeightM * 0.75f);
        world.Companion.BindNavigation(navigation);
        return navigation;
    }

    /// <param name="maxClimbM">Kept below the body's real step so a planned route never asks for a climb the body might fail.</param>
    public static RoomNavigation Create(Node parent, Node3D sourceRoot, Aabb bounds, WorldScaleProfile agent, float maxClimbM)
    {
        var navigation = new RoomNavigation
        {
            Name = "RoomNavigation", SourceRoot = sourceRoot,
            BakeBounds = new Aabb(bounds.Position - new Vector3(0, 0.1f, 0), bounds.Size + new Vector3(0, 0.2f, 0)),
            // Whole cells, as the baker rounds them anyway (and warns when it has to). For the 10 cm companion:
            // 2 + 2 cm clearance is 4 cm, the height 10 cm, and three quarters of its 2 cm step (1.5 cm) rounds
            // down to a 1 cm climb (the 0.24 m body had 8 cm, 24 cm and 3 cm).
            AgentRadiusM = Cells((float)agent.RadiusMeters + ClearanceM, CellSizeM, up: true),
            AgentHeightM = Cells((float)agent.HeightMeters, CellHeightM, up: true),
            AgentMaxClimbM = Mathf.Max(CellHeightM, Cells(maxClimbM, CellHeightM, up: false))
        };
        parent.AddChild(navigation);
        return navigation;
    }

    public override void _Ready()
    {
        Map = NavigationServer3D.MapCreate();
        NavigationServer3D.MapSetCellSize(Map, CellSizeM);
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
            CellSize = CellSizeM, CellHeight = CellHeightM,
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

    /// <summary>The walkable route from one point to another. Reaches is false when the goal is not on the start's walkable island (within the planar tolerance).</summary>
    public Route FindRoute(Vector3 from, Vector3 to, float toleranceM)
    {
        if (!IsReady || !from.IsFinite() || !to.IsFinite()) return Route.None;
        var points = NavigationServer3D.MapGetPath(Map, from, to, true);
        if (points.Length == 0) return Route.None;
        var end = points[^1];
        var reaches = Planar(end - to).Length() <= toleranceM;
        var length = Planar(points[0] - from).Length();
        for (var i = 1; i < points.Length; i++) length += Planar(points[i] - points[i - 1]).Length();
        return new Route(points, reaches, points.Length <= 2, length);
    }

    /// <summary>The nearest walkable point to a position (the position itself when the mesh is not ready).</summary>
    public Vector3 ClosestPoint(Vector3 point) => IsReady && point.IsFinite() ? NavigationServer3D.MapGetClosestPoint(Map, point) : point;

    private static Vector3 Planar(Vector3 value) => new(value.X, 0, value.Z);

    private static float Cells(float metres, float cell, bool up) =>
        (up ? Mathf.Ceil(metres / cell - 0.001f) : Mathf.Floor(metres / cell + 0.001f)) * cell;

    /// <summary>A cheap fingerprint of the static collision the mesh is baked from: which bodies and shapes, and where.</summary>
    private int Signature()
    {
        var hash = new HashCode();
        Accumulate(SourceRoot, ref hash);
        return hash.ToHashCode();
    }

    private static void Accumulate(Node node, ref HashCode hash)
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
                    hash.Add(Quantize(shape.Transform));
                }
        }
        foreach (var child in node.GetChildren()) Accumulate(child, ref hash);
    }

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
