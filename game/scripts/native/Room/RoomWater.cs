using Godot;
using System.Collections.Generic;

namespace EnFractal.Native.Room;

/// <summary>
/// The room's water, for play. Every water shell part (material role "water") carries a query-only collider on physics
/// layer 4, "water" (bit value 8): an Area3D with monitoring off, made of the part's own surface triangles. Nothing collides
/// with it. Bodies keep mask 1 (the companion 1 and 2), and areas are invisible to every query that does not ask for them:
/// the navigation bake parses static bodies on the world layer only, the camera arms, the sight sweep, reach and drops cast
/// against layer 1 without areas, and the look's sun rays leave areas out by default.
/// <para>
/// RoomWater answers two questions, with rays that ask for areas on that layer alone: where is the water surface above or
/// below a point, and how deep is the water there (from the surface down to the first world-layer surface, the bed). The
/// water is data the scene derives from the room; nothing here is authoritative state.
/// </para>
/// </summary>
public static class RoomWater
{
    /// <summary>Physics layer 4, "water" (game/project.godot), as a bit value.</summary>
    public const uint Layer = 8;
    public const string MaterialRole = "water";
    public const string ColliderName = "WaterSurface";
    /// <summary>How far up and down the queries look for a surface by default.</summary>
    public const float SearchM = 1.0f;
    /// <summary>How far under a surface the bed is looked for; deeper water reads as this deep.</summary>
    public const float BedSearchM = 3.0f;

    /// <summary>The water at one column: its surface, and the bed under that surface (BedY is SurfaceY - BedSearchM when no bed was found).</summary>
    public readonly record struct Column(bool Wet, float SurfaceY, float BedY)
    {
        public static readonly Column Dry = new(false, float.NaN, float.NaN);
        /// <summary>Surface to bed, in metres; 0 where there is no water.</summary>
        public float DepthM => Wet ? SurfaceY - BedY : 0;
        /// <summary>How far a point is under the surface (negative above it; 0 where there is no water).</summary>
        public float Under(Vector3 point) => Wet ? SurfaceY - point.Y : 0;
    }

    public static bool IsWater(ShellPart part) => part.MaterialRole == MaterialRole;

    /// <summary>
    /// The query-only collider for a water part: one concave shape per surface mesh, on the water layer, monitoring and
    /// monitorable off (it watches nothing and nothing watches it; rays that ask for areas still find it).
    /// </summary>
    public static Area3D CreateCollider(IEnumerable<(Shape3D Shape, Transform3D Transform)> shapes)
    {
        var area = new Area3D
        {
            Name = ColliderName, CollisionLayer = Layer, CollisionMask = 0, Monitoring = false, Monitorable = false,
            InputRayPickable = false,
        };
        foreach (var (shape, transform) in shapes)
        {
            // The surface is a sheet that must be found from above and from below (a body under water looking up).
            if (shape is ConcavePolygonShape3D sheet) sheet.BackfaceCollision = true;
            area.AddChild(new CollisionShape3D { Shape = shape, Transform = transform });
        }
        return area;
    }

    /// <summary>The first water surface straight above the point, within maxM (null when none).</summary>
    public static float? SurfaceAbove(PhysicsDirectSpaceState3D space, Vector3 point, float maxM = SearchM) =>
        Surface(space, point, point + Vector3.Up * maxM);

    /// <summary>The first water surface straight below the point, within maxM (null when none).</summary>
    public static float? SurfaceBelow(PhysicsDirectSpaceState3D space, Vector3 point, float maxM = SearchM) =>
        Surface(space, point, point + Vector3.Down * maxM);

    /// <summary>How deep the water is at this point's column: from the surface over (or just under) the point down to the bed. 0 when dry.</summary>
    public static float DepthAt(PhysicsDirectSpaceState3D space, Vector3 point) => At(space, point).DepthM;

    /// <summary>
    /// The water at a point's column: the highest surface from aboveM over the point down to belowM under it, and the bed
    /// under that surface (a ray on the world layer, never this layer). The body asks this once a tick at its feet.
    /// </summary>
    public static Column At(PhysicsDirectSpaceState3D space, Vector3 point, float aboveM = SearchM, float belowM = 0.05f, Rid exclude = default)
    {
        if (!point.IsFinite()) return Column.Dry;
        var surface = Surface(space, point + Vector3.Up * aboveM, point + Vector3.Down * belowM);
        if (surface is not { } level) return Column.Dry;
        var top = new Vector3(point.X, level, point.Z);
        var bed = PhysicsRayQueryParameters3D.Create(top + Vector3.Up * 0.001f, top + Vector3.Down * BedSearchM, RoomBuilder.WorldLayer);
        if (exclude.IsValid) bed.Exclude = new Godot.Collections.Array<Rid> { exclude };
        var hit = space.IntersectRay(bed);
        return new Column(true, level, hit.Count > 0 ? hit["position"].AsVector3().Y : level - BedSearchM);
    }

    private static float? Surface(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to)
    {
        if (!from.IsFinite() || !to.IsFinite()) return null;
        var query = PhysicsRayQueryParameters3D.Create(from, to, Layer);
        query.CollideWithAreas = true;
        query.CollideWithBodies = false;
        // The surface is a sheet: found from either side.
        query.HitBackFaces = true;
        var hit = space.IntersectRay(query);
        return hit.Count > 0 ? hit["position"].AsVector3().Y : null;
    }
}
