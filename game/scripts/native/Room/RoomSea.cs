using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EnFractal.Native.Room;

/// <summary>
/// The island's sea, for play (the founder, 9 October: "My daughter hates the invisible wall"; every room an island in an
/// endless sea). Read from the room's <c>extensions.x_landscape_sea</c> (pipeline/landscape/export/sea.py), which stays an
/// extension until the edge and the founder's playtest settle what the game needs. Room files are untrusted: every loop is
/// bounded (3 to 4,096 points), every number finite and within the room's coordinate limit, ids are tokens, and each beach
/// stands inside the room's bounds; anything else refuses the room. A room without the extension has no sea edge.
/// <para>
/// The geometry the bodies ask each tick: how far past the reef a point is, the gentle current there (toward the reef, so
/// back toward the island, growing from nothing at the reef to its strongest at the edge of the playable water), whether a
/// point is still in the playable water, and the nearest beach to wash up on. Outlines are closed [x, z] loops in metres.
/// </para>
/// </summary>
public sealed class RoomSea
{
    public const int MaxOutlinePoints = 4096;
    public const int MaxBeaches = 64;
    public const int MaxPasses = 16;
    public const string ExtensionName = "x_landscape_sea";
    private static readonly Regex Id = new(@"\A[a-z0-9_]{1,32}\z", RegexOptions.Compiled);

    public readonly record struct Beach(string Id, Vector3 WashAshoreM, float YawDeg, Vector3 WaterM);
    public readonly record struct Pass(Vector2 CentreM, float WidthM);
    public readonly record struct Jetty(Vector3 RootM, Vector3 EndM, float YawDeg, float WidthM, float DeckTopM);

    public float LevelM { get; private init; }
    public float SwimDepthM { get; private init; }
    public Vector2[] Coast { get; private init; } = Array.Empty<Vector2>();
    public Vector2[] Reef { get; private init; } = Array.Empty<Vector2>();
    public float ReefCrestYM { get; private init; }
    public float ReefBandHalfWidthM { get; private init; }
    public IReadOnlyList<Pass> Passes { get; private init; } = Array.Empty<Pass>();
    public Vector2[] PlayArea { get; private init; } = Array.Empty<Vector2>();
    public IReadOnlyList<Beach> Beaches { get; private init; } = Array.Empty<Beach>();
    public Jetty? JettyData { get; private init; }

    /// <summary>A sea built directly (the body suites' synthetic island). Loops must have at least three points.</summary>
    public RoomSea(float levelM, Vector2[] coast, Vector2[] reef, Vector2[] playArea, IReadOnlyList<Beach> beaches)
    {
        if (coast.Length < 3 || reef.Length < 3 || playArea.Length < 3) throw new ArgumentException("a sea's loops need three points or more");
        LevelM = levelM;
        SwimDepthM = 0.08f;
        Coast = coast;
        Reef = reef;
        PlayArea = playArea;
        Beaches = beaches;
    }

    private RoomSea() { }

    /// <summary>Read and check the extension; a RoomLoadException for anything out of bounds.</summary>
    public static RoomSea Parse(JsonElement sea, Aabb bounds)
    {
        Expect(sea.ValueKind == JsonValueKind.Object, $"{ExtensionName} is not an object");
        var reef = Obj(sea, "reef");
        var beaches = new List<Beach>();
        foreach (var beach in Records(sea, "beaches", MaxBeaches))
        {
            var id = beach.TryGetProperty("id", out var raw) && raw.ValueKind == JsonValueKind.String ? raw.GetString()! : "";
            Expect(Id.IsMatch(id), $"{ExtensionName}: a beach id is not a token");
            Expect(beaches.All(b => b.Id != id), $"{ExtensionName}: beach {id} is listed twice");
            var ashore = Vec(beach, "wash_ashore_m", 3);
            var at = new Vector3(ashore[0], ashore[1], ashore[2]);
            // A beach outside the room's bounds would put a body where the bounds recover it from.
            Expect(at.X > bounds.Position.X && at.X < bounds.End.X && at.Y >= bounds.Position.Y && at.Y < bounds.End.Y && at.Z > bounds.Position.Z && at.Z < bounds.End.Z,
                $"{ExtensionName}: beach {id} is outside the room's bounds");
            var water = Vec(beach, "water_m", 3);
            beaches.Add(new Beach(id, at, Num(beach, "yaw_deg", -360, 360), new Vector3(water[0], water[1], water[2])));
        }
        var passes = Records(reef, "passes", MaxPasses).Select(p => { var c = Vec(p, "centre_m", 2); return new Pass(new Vector2(c[0], c[1]), Num(p, "width_m", 0, 10)); }).ToArray();
        Jetty? jetty = null;
        if (sea.TryGetProperty("jetty", out var j) && j.ValueKind != JsonValueKind.Null)
        {
            Expect(j.ValueKind == JsonValueKind.Object, $"{ExtensionName}: the jetty is not an object");
            var root = Vec(j, "root_m", 3);
            var end = Vec(j, "end_m", 3);
            jetty = new Jetty(new Vector3(root[0], root[1], root[2]), new Vector3(end[0], end[1], end[2]), Num(j, "yaw_deg", -360, 360), Num(j, "width_m", 0, 10), Num(j, "deck_top_m", -10, 10));
        }
        return new RoomSea
        {
            LevelM = Num(sea, "level_m", -10, 10), SwimDepthM = Num(sea, "swim_depth_m", 0, 10),
            Coast = Loop(Obj(sea, "coast"), "outline_m"), Reef = Loop(reef, "outline_m"),
            ReefCrestYM = Num(reef, "crest_y_m", -10, 10), ReefBandHalfWidthM = Num(reef, "band_half_width_m", 0, 10),
            Passes = passes, PlayArea = Loop(Obj(sea, "play_area"), "outline_m"), Beaches = beaches, JettyData = jetty,
        };
    }

    // ---- what the bodies ask ----

    /// <summary>How far past the reef a point is (0 inside the reef's loop).</summary>
    public float PastReefM(Vector2 point) => Inside(Reef, point) ? 0 : Nearest(Reef, point).Distance;

    /// <summary>Whether a point is in the playable water (inside the play area's loop, which takes in the island).</summary>
    public bool InPlayArea(Vector2 point) => Inside(PlayArea, point);

    /// <summary>
    /// The current at a point, in metres a second over the ground: none inside the reef; past it, toward the nearest point of
    /// the reef (back toward the island), growing from nothing at the reef to strongestMps at the edge of the playable water.
    /// </summary>
    public Vector2 Current(Vector2 point, float strongestMps)
    {
        if (Inside(Reef, point)) return Vector2.Zero;
        var (past, toward) = Nearest(Reef, point);
        if (past < 1e-4f) return Vector2.Zero;
        var toEdge = InPlayArea(point) ? Nearest(PlayArea, point).Distance : 0;
        var share = Mathf.Clamp(past / (past + toEdge), 0, 1);
        return (toward - point) / past * strongestMps * Mathf.SmoothStep(0, 1, share);
    }

    /// <summary>The beach nearest a point (null when the sea lists none).</summary>
    public Beach? NearestBeach(Vector2 point)
    {
        Beach? best = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var beach in Beaches)
        {
            var distance = new Vector2(beach.WashAshoreM.X, beach.WashAshoreM.Z).DistanceTo(point);
            if (distance < bestDistance) (best, bestDistance) = (beach, distance);
        }
        return best;
    }

    /// <summary>
    /// The part of the room the walkable map needs: the coast's box (and a margin for the beaches' wet edge), from just under
    /// the sea's level to the room's top. The sea floor is no walk: the Gubble floats over the water and swimmers swim.
    /// </summary>
    public Aabb LandBounds(Aabb room, float marginM = 0.15f)
    {
        float minX = float.PositiveInfinity, minZ = float.PositiveInfinity, maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
        foreach (var p in Coast) { minX = Mathf.Min(minX, p.X); maxX = Mathf.Max(maxX, p.X); minZ = Mathf.Min(minZ, p.Y); maxZ = Mathf.Max(maxZ, p.Y); }
        var low = new Vector3(Mathf.Max(room.Position.X, minX - marginM), Mathf.Max(room.Position.Y, LevelM - 0.03f), Mathf.Max(room.Position.Z, minZ - marginM));
        var high = new Vector3(Mathf.Min(room.End.X, maxX + marginM), room.End.Y, Mathf.Min(room.End.Z, maxZ + marginM));
        return high.X > low.X && high.Y > low.Y && high.Z > low.Z ? new Aabb(low, high - low) : room;
    }

    /// <summary>Even-odd point in a closed loop.</summary>
    public static bool Inside(Vector2[] loop, Vector2 point)
    {
        var inside = false;
        for (int i = 0, j = loop.Length - 1; i < loop.Length; j = i++)
        {
            var (a, b) = (loop[i], loop[j]);
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    /// <summary>The nearest point of a closed loop's edges to a point, and how far it is.</summary>
    public static (float Distance, Vector2 Point) Nearest(Vector2[] loop, Vector2 point)
    {
        var best = (Distance: float.PositiveInfinity, Point: point);
        for (int i = 0, j = loop.Length - 1; i < loop.Length; j = i++)
        {
            var (a, b) = (loop[j], loop[i]);
            var ab = b - a;
            var length = ab.LengthSquared();
            var t = length > 1e-12f ? Mathf.Clamp((point - a).Dot(ab) / length, 0, 1) : 0;
            var on = a + ab * t;
            var distance = on.DistanceTo(point);
            if (distance < best.Distance) best = (distance, on);
        }
        return best;
    }

    // ---- checked reading ----

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new RoomLoadException(message);
    }

    private static JsonElement Obj(JsonElement element, string name)
    {
        Expect(element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object, $"{ExtensionName}: missing object '{name}'");
        return value;
    }

    private static IEnumerable<JsonElement> Records(JsonElement element, string name, int most)
    {
        Expect(element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= most,
            $"{ExtensionName}: '{name}' must be a list of at most {most}");
        foreach (var item in value.EnumerateArray())
        {
            Expect(item.ValueKind == JsonValueKind.Object, $"{ExtensionName}: an entry of '{name}' is not an object");
            yield return item;
        }
    }

    private static float Num(JsonElement element, string name, float low, float high)
    {
        Expect(element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number, $"{ExtensionName}: missing number '{name}'");
        var number = value.GetDouble();
        Expect(double.IsFinite(number) && number >= low && number <= high, $"{ExtensionName}: '{name}' must be a finite number from {low} to {high}");
        return (float)number;
    }

    private static float[] Vec(JsonElement element, string name, int size)
    {
        Expect(element.TryGetProperty(name, out var value), $"{ExtensionName}: missing '{name}'");
        return Point(value, size);
    }

    private static float[] Point(JsonElement value, int size)
    {
        Expect(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == size, $"{ExtensionName}: a point must have {size} numbers");
        var numbers = new float[size];
        var i = 0;
        foreach (var item in value.EnumerateArray())
        {
            Expect(item.ValueKind == JsonValueKind.Number, $"{ExtensionName}: a point holds a non-number");
            var number = item.GetDouble();
            Expect(double.IsFinite(number) && Math.Abs(number) <= RoomData.CoordinateLimitM, $"{ExtensionName}: coordinates must be finite and within ±{RoomData.CoordinateLimitM} m");
            numbers[i++] = (float)number;
        }
        return numbers;
    }

    private static Vector2[] Loop(JsonElement element, string name)
    {
        Expect(element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array, $"{ExtensionName}: missing loop '{name}'");
        var count = value.GetArrayLength();
        Expect(count >= 3 && count <= MaxOutlinePoints, $"{ExtensionName}: a loop must have 3 to {MaxOutlinePoints} points");
        return value.EnumerateArray().Select(p => { var xz = Point(p, 2); return new Vector2(xz[0], xz[1]); }).ToArray();
    }
}
