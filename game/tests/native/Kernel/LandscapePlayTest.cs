using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Navigation;
using EnFractal.Native.Room;

namespace EnFractal.Tests.Kernel;

/// <summary>
/// Play on the land (Run 2, Lane P): a generated landscape room booted as the game boots it, with the real bodies, Jolt
/// and the command host. The edge of the world holds both bodies inside the room's bounds; the companion walks to the
/// carryable thing with go_to and fetches it; the player walks to it, picks it up, carries it back to the spawn and sets
/// it down through enfractal.command; every named destination is reachable; and the slopes, steps, sticks and falls met
/// on the way are measured. The room is derived data and never committed: the runner generates and exports it and passes
/// "-- --landscape=DIR" (the exported room directory, its last component the room id). Saves go to a test folder.
/// </summary>
public partial class LandscapePlayTest : Node3D
{
    private const string TestRoot = "user://tests/landscape_play";
    private const string Player = CommandHost.PlayerPrincipal;
    private const string CompanionAvatar = CommandHost.CompanionAvatarId;
    private int _checks;
    private int _failures;
    private int _ids;
    private RoomWorld _world = null!;
    private CommandHost _host = null!;
    private RoomNavigation _navigation = null!;
    private string _roomId = "";
    private readonly List<string> _measured = new();

    public override async void _Ready()
    {
        try
        {
            var directory = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--landscape=", StringComparison.Ordinal))?["--landscape=".Length..];
            if (string.IsNullOrEmpty(directory))
            {
                GD.PushError("Landscape play: pass -- --landscape=DIR, an exported landscape room (the runner generates one)");
                GetTree().Quit(1);
                return;
            }
            CommandHost.SaveRoot = TestRoot + "/saves";
            RemoveSaves();
            _world = GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
            _world.RoomDirectory = directory.Replace('\\', '/');
            AddChild(_world);
            for (var i = 0; i < 1800 && !_world.WorldReady && _world.LoadError.Length == 0; i++) await Frames(1);
            Check(_world.WorldReady, "the landscape room boots: " + _world.LoadError);
            if (!_world.WorldReady) { Finish(); return; }
            _roomId = _world.Room.RoomId;
            // The test steers the player as a player would; the keyboard (nobody at it) must not override that.
            _world.Player.ReadKeyboard = false;
            _host = CommandHost.Of(_world)!;
            _navigation = _world.GetNode<RoomNavigation>("RoomNavigation");
            for (var i = 0; i < 30 && !_navigation.IsReady; i++) await Frames(1);
            Check(_navigation.IsReady, "the landscape bakes a navigation mesh for the companion");
            Measure($"LANDSCAPE_NAVIGATION {_navigation.PolygonCount} polygons, bake {_navigation.LastBakeMs:0} ms, climb {_navigation.AgentMaxClimbM:0.000} m, radius {_navigation.AgentRadiusM:0.000} m");
            await Frames(30);
            Check(_world.Player.IsOnFloor() && _world.Companion.IsOnFloor(), "both bodies stand on the land at their spawns");

            await TestEdgeOfTheWorld();
            await TestCompanionGoesToTheCrate();
            await TestPlayerCarriesTheCrateHome();
            await TestCompanionFetches();
            await TestDestinations();
            await TestNavigationClimb();
            Finish();
        }
        catch (Exception error)
        {
            GD.PushError("Landscape play test exception: " + error);
            GetTree().Quit(1);
        }
    }

    // ---- the edge of the world ----

    /// <summary>
    /// The exported terrain runs a few centimetres past the room's bounds and stops. Find walkable land at the bounds,
    /// run the player straight out over it, and send the companion to a place a metre outside: both stay inside.
    /// </summary>
    private async Task TestEdgeOfTheWorld()
    {
        var bounds = _world.Room.Bounds;
        var edges = WalkableEdges(bounds);
        Measure("LANDSCAPE_EDGE walkable land at the bounds: " + (edges.Count == 0 ? "none" : string.Join(", ", edges.Select(e => $"{e.Side} at {Text(e.Inside)} (slope {e.SlopeDeg:0} deg)"))));
        Check(edges.Count > 0, "the garage's land reaches its bounds somewhere a body can walk (the pass the river leaves by)");
        if (edges.Count == 0) return;
        var player = _world.Player;
        var companion = _world.Companion;
        Send(Command(NextId("stop"), "goal.set", new JsonObject { ["actor"] = CompanionAvatar, ["goal"] = "stop" }), Player);
        var edge = edges[0];
        Check(player.TryTeleportTo(edge.Inside), $"the player stands {EdgeInsetM:0.00} m inside the {edge.Side} bound");
        await Frames(10);
        var outward = edge.Outward;
        player.Rotation = new Vector3(0, Mathf.Atan2(-outward.X, -outward.Z), 0);
        var worstOut = float.NegativeInfinity;
        var lowest = player.GlobalPosition.Y;
        for (var i = 0; i < 150; i++)
        {
            player.SetControlInput(new Vector2(0, 1), sprint: true);
            await Frames(1);
            worstOut = Mathf.Max(worstOut, Outside(bounds, player.GlobalPosition, player.BodyRadiusM));
            lowest = Mathf.Min(lowest, player.GlobalPosition.Y);
        }
        player.SetControlInput(Vector2.Zero);
        await Frames(10);
        Measure($"LANDSCAPE_EDGE player ran {edge.Side} for 2.5 s: furthest {worstOut * 100:0.0} cm past the bounds (body edge), lowest y {lowest:0.000}, ends at {Text(player.GlobalPosition)} on floor {player.IsOnFloor()}");
        Check(worstOut <= 0.001f && player.IsOnFloor() && lowest > edge.Inside.Y - 0.05f,
            $"running at the edge of the world, the player stays inside the bounds and on the land ({worstOut * 100:0.0} cm past)");

        // The companion, sent to a place a metre outside along the same line: it stops at the bound and says blocked.
        var spot = edges.Count > 1 ? edges[1] : edge;
        Check(player.TryTeleportTo(spot.Inside - spot.Outward * 0.3f) || player.TryTeleportTo(_world.Room.SpawnFor("player").PositionM), "the player steps back from the edge");
        Check(companion.TryTeleportTo(spot.Inside), $"the companion stands inside the {spot.Side} bound");
        await Frames(10);
        companion.GoTo(new Aabb(spot.Inside + spot.Outward * 1.0f, Vector3.Zero), CommandHost.GoToStopM);
        worstOut = float.NegativeInfinity;
        var blocked = false;
        for (var i = 0; i < 180; i++)
        {
            await Frames(1);
            worstOut = Mathf.Max(worstOut, Outside(bounds, companion.GlobalPosition, companion.BodyRadiusM));
            blocked |= companion.GoalBlocked;
        }
        Measure($"LANDSCAPE_EDGE companion sent 1 m out past the {spot.Side} bound: furthest {worstOut * 100:0.0} cm past, blocked {blocked}, intent {companion.CurrentIntent}, on floor {companion.IsOnFloor()}");
        Check(worstOut <= 0.001f && companion.IsOnFloor(), $"the companion stays inside the bounds too ({worstOut * 100:0.0} cm past)");
        companion.Stop();
        Check(player.TryTeleportTo(_world.Room.SpawnFor("player").PositionM) && companion.TryTeleportTo(_world.Room.SpawnFor("companion").PositionM), "both bodies back at their spawns");
        player.Rotation = new Vector3(0, Mathf.DegToRad(_world.Room.SpawnFor("player").YawDeg), 0);
        await Frames(10);
    }

    private const float EdgeInsetM = 0.10f;

    private readonly record struct Edge(string Side, Vector3 Inside, Vector3 Outward, float SlopeDeg);

    /// <summary>Places at each side of the bounds where the land is walkable from EdgeInsetM inside to just past the bound, flattest first.</summary>
    private List<Edge> WalkableEdges(Aabb bounds)
    {
        var found = new List<Edge>();
        foreach (var (side, outward) in new[] { ("east", Vector3.Right), ("west", Vector3.Left), ("south", Vector3.Back), ("north", Vector3.Forward) })
        {
            var along = outward.Cross(Vector3.Up);
            var centre = bounds.GetCenter();
            var half = Mathf.Abs(along.X) > 0.5f ? bounds.Size.X * 0.5f : bounds.Size.Z * 0.5f;
            var reach = Mathf.Abs(outward.X) > 0.5f ? bounds.Size.X * 0.5f : bounds.Size.Z * 0.5f;
            Edge? best = null;
            for (var t = -half + 0.2f; t <= half - 0.2f; t += 0.05f)
            {
                var worst = 0f;
                Vector3? inside = null;
                var ok = true;
                var previous = float.NaN;
                foreach (var depth in new[] { -EdgeInsetM, -0.06f, -0.03f, 0f, 0.02f })
                {
                    var at = new Vector3(centre.X, 0, centre.Z) + along * t + outward * (reach + depth);
                    var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(
                        new Vector3(at.X, bounds.End.Y + 1, at.Z), new Vector3(at.X, bounds.Position.Y - 1, at.Z), RoomBuilder.WorldLayer));
                    if (hit.Count == 0) { ok = false; break; }
                    var normal = hit["normal"].AsVector3();
                    var y = hit["position"].AsVector3().Y;
                    var slope = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(normal.Y, -1, 1)));
                    if (slope > 30f || (!float.IsNaN(previous) && Mathf.Abs(y - previous) > 0.015f)) { ok = false; break; }
                    previous = y;
                    worst = Mathf.Max(worst, slope);
                    inside ??= new Vector3(at.X, y, at.Z);
                }
                if (ok && (best == null || worst < best.Value.SlopeDeg)) best = new Edge(side, inside!.Value, outward, worst);
            }
            if (best != null) found.Add(best.Value);
        }
        return found.OrderBy(e => e.SlopeDeg).ToList();
    }

    /// <summary>How far the body's side reaches past the bounds horizontally (negative inside).</summary>
    private static float Outside(Aabb bounds, Vector3 feet, float radius) =>
        new[] { bounds.Position.X - (feet.X - radius), feet.X + radius - bounds.End.X, bounds.Position.Z - (feet.Z - radius), feet.Z + radius - bounds.End.Z }.Max();

    // ---- journeys ----

    private async Task TestCompanionGoesToTheCrate()
    {
        var companion = _world.Companion;
        var go = Send(Command(NextId("goto"), "goal.set", new JsonObject { ["actor"] = CompanionAvatar, ["goal"] = "go_to", ["target"] = "obj:apple_crate" }), Player);
        Check(Ok(go), "the player sends the companion to the apple crate with go_to: " + Code(go));
        var walk = await Watch("companion go_to the crate", companion, () => _host.RunningGoal(CompanionAvatar) != null, 3600);
        var job = JobStatus(go);
        Check(job?["state"]?.GetValue<string>() == "succeeded", $"the companion arrives at the crate ({walk.Summary()}): {job?.ToJsonString()}");
    }

    private async Task TestPlayerCarriesTheCrateHome()
    {
        var player = _world.Player;
        var home = player.GlobalPosition;
        // The companion waits where it is (beside the crate) so it never stands in the player's way back.
        Send(Command(NextId("stay"), "goal.set", new JsonObject { ["actor"] = CompanionAvatar, ["goal"] = "stay" }), Player);
        var crate = CrateBox();
        var there = await PlayerWalk("player to the crate", () => Nearest(CrateBox(), player.GlobalPosition), 0.06f, 3600);
        Check(there.Arrived, $"the player walks from the spawn to the crate ({there.Summary()})");
        var grab = Send(Command(NextId("grab"), "entity.grab", new JsonObject { ["target"] = "obj:apple_crate" }), Player);
        Check(Ok(grab) && _host.HeldBy(CommandHost.PlayerAvatar) == "obj:apple_crate", "the player picks the apple crate up: " + Code(grab));
        var back = await PlayerWalk("player carries it back", () => home, 0.05f, 3600);
        Check(back.Arrived && _host.HeldBy(CommandHost.PlayerAvatar) == "obj:apple_crate", $"the player carries it back to the spawn ({back.Summary()})");
        var down = Send(Command(NextId("down"), "entity.release", new JsonObject()), Player);
        await Frames(10);
        var box = CrateBox();
        var drop = PlanarDistance(box.GetCenter(), player.GlobalPosition);
        Check(Ok(down) && _host.HeldBy(CommandHost.PlayerAvatar) == null && drop < 0.3f && Supported(box),
            $"entity.release sets it down beside the spawn, resting on the land ({drop:0.00} m away): " + Code(down));
        Measure($"LANDSCAPE_CRATE moved from {Text(crate.GetCenter())} to {Text(box.GetCenter())}");
    }

    private async Task TestCompanionFetches()
    {
        // Put the crate back at the first door (a place, not a walk), then have the companion walk home and fetch it.
        var player = _world.Player;
        var companion = _world.Companion;
        var original = _world.Room.Objects.Single(o => o.Id == "obj:apple_crate").PositionM;
        var place = Send(Command(NextId("place"), "entity.place", new JsonObject { ["target"] = "obj:apple_crate", ["placement"] = new JsonObject { ["position_m"] = new JsonArray(original.X, original.Y, original.Z) } }), Player);
        Check(Ok(place), "the crate goes back to its door: " + Code(place));
        var come = Send(Command(NextId("come"), "goal.set", new JsonObject { ["actor"] = CompanionAvatar, ["goal"] = "come" }), Player);
        var walk = await Watch("companion comes home", companion, () => companion.CurrentIntent == "come", 3600);
        Check(Ok(come) && companion.CurrentIntent == "stay", $"the companion walks back to the player at the spawn ({walk.Summary()})");
        var fetch = Send(Command(NextId("fetch"), "goal.set", new JsonObject { ["actor"] = CompanionAvatar, ["goal"] = "fetch", ["target"] = "obj:apple_crate" }), Player);
        Check(Ok(fetch), "the player asks the companion to fetch the crate: " + Code(fetch));
        var held = false;
        var trip = await Watch("companion fetches the crate", companion, () => { held |= _host.HeldBy(CompanionAvatar) == "obj:apple_crate"; return _host.RunningGoal(CompanionAvatar) != null; }, 7200);
        var job = JobStatus(fetch);
        var gap = PlanarDistance(companion.GlobalPosition, player.GlobalPosition);
        Check(held && job?["state"]?.GetValue<string>() == "succeeded" && _host.HeldBy(CompanionAvatar) == "obj:apple_crate" && gap <= EnFractal.Native.CompanionAvatar.ComeArrivalM + 0.03f,
            $"the companion fetches it across the land and stands {gap:0.00} m from the player holding it ({trip.Summary()}): {job?.ToJsonString()}");
        var down = Send(Command(NextId("down"), "entity.release", new JsonObject { ["actor"] = CompanionAvatar }), Player);
        await Frames(10);
        Check(Ok(down) && Supported(CrateBox()), "the companion sets it down on the land: " + Code(down));
    }

    /// <summary>
    /// Every named entity the exporter made: a route from the spawn and the companion's go_to. Only promised destinations
    /// must be reachable ("a ledge is a destination only if there is a way up"): things to find and carry, and dwellings.
    /// Other landmarks (fences, boulders, lanterns) are reported with how near a route or go_to gets, and never fail.
    /// </summary>
    private async Task TestDestinations()
    {
        var spawn = _world.Room.SpawnFor("player").PositionM;
        foreach (var item in _world.Room.Objects)
        {
            var box = EntityBox(item.Id);
            var promised = Promised(item);
            var (route, gap) = RouteTo(spawn, box);
            var nearestSide = _navigation.FindRoute(spawn, Nearest(box, spawn), 0.1f).Reaches;
            var go = Send(Command(NextId("goto"), "goal.set", new JsonObject { ["actor"] = CompanionAvatar, ["goal"] = "go_to", ["target"] = item.Id }), Player);
            var walk = await Watch($"companion go_to {item.DisplayName}", _world.Companion, () => _host.RunningGoal(CompanionAvatar) != null, 3600);
            var job = JobStatus(go);
            var state = job?["state"]?.GetValue<string>() ?? Code(go) ?? "none";
            var near = PlanarDistance(_world.Companion.GlobalPosition, Nearest(box, _world.Companion.GlobalPosition));
            Measure($"LANDSCAPE_DESTINATION {item.Id} ({item.DisplayName}, {item.Asset.Category}, {(promised ? "promised" : "landmark")}) at {Text(box.GetCenter())}: " +
                    $"route from the spawn {(route.Reaches ? "reaches" : "does not reach")}, {route.LengthM:0.00} m, ending {gap:0.00} m from it (to the side nearest the spawn alone: {(nearestSide ? "reaches" : "does not reach")}); go_to {state}, ending {near:0.00} m from it");
            if (promised)
                Check(route.Reaches && job?["state"]?.GetValue<string>() == "succeeded", $"{item.DisplayName}, a promised destination, is reachable from the spawn ({walk.Summary()})");
        }
    }

    /// <summary>
    /// What the land promises a visit to, by what the entity is (never by id): a thing to find and carry (movable), or a
    /// dwelling, which the exporter names cottage or tower or builds as a structure with a roof. Fences, boulders and
    /// lanterns are landmarks.
    /// </summary>
    private static bool Promised(ObjectInstance item) =>
        item.Asset.Movable || item.Asset.Category is "cottage" or "tower" ||
        (item.Asset.CategoryGroup == "structure" && item.Asset.Materials.Any(m => m.Slot == "roof" || m.Role == "roof"));

    /// <summary>
    /// The shortest route from a point to anywhere beside a box's footprint (the four sides and four corners, a body's
    /// clearance out), as go_to gets there from any side; and how far its end is from the footprint. A route counts only
    /// when it reaches its own end on the start's island and ends within arrival reach of the box. (Aiming at the single
    /// footprint point nearest the spawn, as before, called a fence unreachable when that side was a slope or a ledge
    /// but go_to arrived from its other side.)
    /// </summary>
    private (RoomNavigation.Route Route, float Gap) RouteTo(Vector3 from, Aabb box)
    {
        var clear = _navigation.AgentRadiusM + 0.01f;
        var centre = box.GetCenter();
        var half = new Vector2(box.Size.X * 0.5f + clear, box.Size.Z * 0.5f + clear);
        RoomNavigation.Route? best = null;
        var bestGap = float.PositiveInfinity;
        var fallback = _navigation.FindRoute(from, Nearest(box, from), 0.1f);
        foreach (var (sx, sz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            var aim = new Vector3(centre.X + sx * half.X, box.Position.Y, centre.Z + sz * half.Y);
            var route = _navigation.FindRoute(from, aim, 0.05f);
            if (!route.Reaches) continue;
            var end = route.Points[^1];
            var gap = PlanarDistance(end, Nearest(box, end));
            if (gap > CommandHost.ArrivalReachM || (best != null && route.LengthM >= best.Value.LengthM)) continue;
            (best, bestGap) = (route, gap);
        }
        if (best != null) return (best.Value, bestGap);
        var last = fallback.IsEmpty ? from : fallback.Points[^1];
        return (fallback with { Reaches = false }, PlanarDistance(last, Nearest(box, last)));
    }

    /// <summary>
    /// The navigation mesh's climb against the body's: sample the land on a 10 cm grid, keep the points the 10 cm body could
    /// stand on (slope at most 45 degrees), and count how many each mesh covers and how many it routes to from the spawn.
    /// </summary>
    private async Task TestNavigationClimb()
    {
        var bounds = _world.Room.Bounds;
        var samples = new List<(Vector3 Point, float Slope)>();
        for (var x = bounds.Position.X + 0.05f; x < bounds.End.X; x += 0.1f)
            for (var z = bounds.Position.Z + 0.05f; z < bounds.End.Z; z += 0.1f)
            {
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(x, bounds.End.Y + 1, z), new Vector3(x, bounds.Position.Y - 1, z), RoomBuilder.WorldLayer));
                if (hit.Count == 0) continue;
                var slope = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(hit["normal"].AsVector3().Y, -1, 1)));
                if (slope <= 45f) samples.Add((hit["position"].AsVector3(), slope));
            }
        var spawn = _world.Room.SpawnFor("player").PositionM;
        var twoCm = RoomNavigation.Create(this, _world.Built, bounds, WorldScaleProfile.Companion, 0.02f);
        var fineCells = RoomNavigation.Create(this, _world.Built, bounds, WorldScaleProfile.Companion, 0.01f, cellM: 0.01f);
        for (var i = 0; i < 30 && !(twoCm.IsReady && fineCells.IsReady); i++) await Frames(1);
        Measure($"LANDSCAPE_NAVIGATION bakes: 2 cm cells climb 0.01 m {_navigation.LastBakeMs:0} ms, climb 0.02 m {twoCm.LastBakeMs:0} ms; 1 cm cells climb 0.01 m {fineCells.LastBakeMs:0} ms");
        foreach (var (name, mesh) in new[] { ("climb 0.010 m on 2 cm cells (the game's)", _navigation), ("climb 0.020 m on 2 cm cells (the body's step)", twoCm), ("climb 0.010 m on 1 cm cells", fineCells) })
        {
            int covered = 0, reached = 0, steep = 0, steepCovered = 0;
            foreach (var (point, slope) in samples)
            {
                var on = PlanarDistance(mesh.ClosestPoint(point), point) <= mesh.AgentRadiusM + 0.01f && Mathf.Abs(mesh.ClosestPoint(point).Y - point.Y) < 0.03f;
                if (slope > 26.6f) steep++;
                if (!on) continue;
                covered++;
                if (slope > 26.6f) steepCovered++;
                if (mesh.FindRoute(spawn, point, 0.06f).Reaches) reached++;
            }
            Measure($"LANDSCAPE_NAVIGATION {name}: {mesh.PolygonCount} polygons; of {samples.Count} body-walkable points (10 cm grid, slope <= 45 deg) {covered} on the mesh, {reached} routed to from the spawn; of {steep} steeper than 26.6 deg, {steepCovered} on the mesh");
        }
        var worst = samples.Count == 0 ? 0 : samples.Max(s => s.Slope);
        Check(samples.Count > 0 && worst <= 45f, "the land has body-walkable ground to measure");
        twoCm.QueueFree();
        fineCells.QueueFree();
    }

    // ---- moving and measuring ----

    private sealed class Walk
    {
        public string Name = "";
        public bool Arrived;
        public int Ticks;
        public float DistanceM;
        public float WorstFloorDeg;
        public Vector3 WorstFloorAt;
        public float WorstFaceDeg;
        public Vector3 WorstFaceAt;
        public float WorstRiseM;
        public float WorstStepM;
        public Vector3 WorstStepAt;
        public int Steps;
        public int AirTicks;
        public float WorstDropM;
        public Vector3 WorstDropAt;
        public int StuckTicks;
        public Vector3 StuckAt;
        public float RestDriftM;

        public string Summary() => string.Create(CultureInfo.InvariantCulture,
            $"{DistanceM:0.00} m in {Ticks / 60.0:0.0} s; worst floor {WorstFloorDeg:0.0} deg at {Text(WorstFloorAt)}, worst face under the body {WorstFaceDeg:0.0} deg at {Text(WorstFaceAt)}; " +
            $"worst rise in one tick {WorstRiseM * 1000:0.0} mm, {Steps} steps (highest {WorstStepM * 1000:0.0} mm at {Text(WorstStepAt)}); airborne {AirTicks} ticks, worst drop {WorstDropM * 1000:0.0} mm at {Text(WorstDropAt)}; " +
            $"stuck {StuckTicks} ticks{(StuckTicks > 0 ? " near " + Text(StuckAt) : "")}; drift at rest {RestDriftM * 1000:0.0} mm");
    }

    /// <summary>Watch a body the host is driving, one physics tick at a time, while <paramref name="running"/> holds.</summary>
    private async Task<Walk> Watch(string name, SmallPlayerController body, Func<bool> running, int maxFrames)
    {
        var walk = new Walk { Name = name };
        var tracker = new Tracker(this, body, walk);
        for (var i = 0; i < maxFrames && running(); i++)
        {
            await Frames(1);
            tracker.Tick(body is CompanionAvatar c ? c.GoalBlocked : body.IsBlocked);
        }
        walk.Arrived = !running();
        await Rest(body, walk);
        Measure($"LANDSCAPE_WALK {name}: {walk.Summary()}");
        return walk;
    }

    /// <summary>The player walks a route on the companion's navigation mesh (both bodies are the same size), steering each tick as a player would.</summary>
    private async Task<Walk> PlayerWalk(string name, Func<Vector3> goal, float arriveM, int maxFrames)
    {
        var body = _world.Player;
        var walk = new Walk { Name = name };
        var tracker = new Tracker(this, body, walk);
        var route = RoomNavigation.Route.None;
        var index = 1;
        var age = int.MaxValue;
        for (var i = 0; i < maxFrames; i++)
        {
            var target = goal();
            var here = body.GlobalPosition;
            var remaining = PlanarDistance(target, here);
            if (remaining <= arriveM) { walk.Arrived = true; break; }
            if (++age > 30 || route.IsEmpty)
            {
                route = _navigation.FindRoute(here, target, arriveM);
                index = 1;
                age = 0;
            }
            while (index < route.Points.Length - 1 && PlanarDistance(route.Points[index], here) < 0.03f) index++;
            var corner = route.IsEmpty || index >= route.Points.Length ? target : route.Points[index];
            var to = corner - here;
            if (new Vector2(to.X, to.Z).Length() > 0.001f) body.Rotation = new Vector3(0, Mathf.Atan2(-to.X, -to.Z), 0);
            body.SetControlInput(new Vector2(0, remaining > 0.08f ? 1 : 0.4f), sprint: remaining > 0.3f);
            await Frames(1);
            tracker.Tick(body.IsBlocked);
        }
        body.SetControlInput(Vector2.Zero);
        await Rest(body, walk);
        Measure($"LANDSCAPE_WALK {name}: {walk.Summary()}");
        return walk;
    }

    /// <summary>After a walk, the body stands still for a second: does it slide on the slope it stopped on?</summary>
    private async Task Rest(SmallPlayerController body, Walk walk)
    {
        await Frames(10);
        var at = body.GlobalPosition;
        await Frames(60);
        walk.RestDriftM = at.DistanceTo(body.GlobalPosition);
    }

    private sealed class Tracker
    {
        private readonly LandscapePlayTest _test;
        private readonly SmallPlayerController _body;
        private readonly Walk _walk;
        private Vector3 _last;
        private bool _wasOnFloor;
        private float _floorY;
        private int _steps;

        public Tracker(LandscapePlayTest test, SmallPlayerController body, Walk walk)
        {
            (_test, _body, _walk) = (test, body, walk);
            _last = body.GlobalPosition;
            _wasOnFloor = body.IsOnFloor();
            _floorY = _last.Y;
            _steps = body.StepsClimbed;
        }

        public void Tick(bool stuck)
        {
            var here = _body.GlobalPosition;
            var onFloor = _body.IsOnFloor();
            _walk.Ticks++;
            _walk.DistanceM += new Vector2(here.X - _last.X, here.Z - _last.Z).Length();
            if (onFloor)
            {
                var floor = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(_body.GetFloorNormal().Y, -1, 1)));
                if (floor > _walk.WorstFloorDeg) (_walk.WorstFloorDeg, _walk.WorstFloorAt) = (floor, here);
                if (_wasOnFloor) _walk.WorstRiseM = Mathf.Max(_walk.WorstRiseM, here.Y - _last.Y);
                if (!_wasOnFloor && _floorY - here.Y > _walk.WorstDropM) (_walk.WorstDropM, _walk.WorstDropAt) = (_floorY - here.Y, here);
                _floorY = here.Y;
            }
            else _walk.AirTicks++;
            if (_body.StepsClimbed != _steps)
            {
                _walk.Steps += _body.StepsClimbed - _steps;
                _steps = _body.StepsClimbed;
                if (here.Y - _last.Y > _walk.WorstStepM) (_walk.WorstStepM, _walk.WorstStepAt) = (here.Y - _last.Y, here);
            }
            var query = PhysicsRayQueryParameters3D.Create(here + Vector3.Up * 0.05f, here + Vector3.Down * 0.05f, RoomBuilder.WorldLayer);
            var hit = _test.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count > 0)
            {
                var face = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(hit["normal"].AsVector3().Y, -1, 1)));
                if (face > _walk.WorstFaceDeg) (_walk.WorstFaceDeg, _walk.WorstFaceAt) = (face, hit["position"].AsVector3());
            }
            if (stuck)
            {
                if (_walk.StuckTicks == 0) _walk.StuckAt = here;
                _walk.StuckTicks++;
            }
            _wasOnFloor = onFloor;
            _last = here;
        }
    }

    // ---- helpers ----

    private Aabb CrateBox() => EntityBox("obj:apple_crate");

    private Aabb EntityBox(string id) =>
        EnFractal.Native.Sandbox.SandboxControls.Box(Query("entity.inspect", new JsonObject { ["target"] = id })["data"]?["entity"]?.AsObject() ?? new JsonObject());

    private static Vector3 Nearest(Aabb box, Vector3 from) =>
        new(Mathf.Clamp(from.X, box.Position.X, box.End.X), box.Position.Y, Mathf.Clamp(from.Z, box.Position.Z, box.End.Z));

    private bool Supported(Aabb box)
    {
        var centre = box.GetCenter();
        var query = PhysicsRayQueryParameters3D.Create(new Vector3(centre.X, box.Position.Y + 0.002f, centre.Z), new Vector3(centre.X, box.Position.Y - 0.004f, centre.Z), RoomBuilder.WorldLayer);
        var crate = _world.Built.GetNode("Objects").GetChildren().OfType<CollisionObject3D>().FirstOrDefault(n => n.GetMeta("entity_id").AsString() == "obj:apple_crate");
        if (crate != null) query.Exclude = new Godot.Collections.Array<Rid> { crate.GetRid() };
        return GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0;
    }

    private JsonObject? JobStatus(JsonObject goalResult)
    {
        var id = goalResult["job_id"]?.GetValue<string>();
        return id == null ? null : Query("jobs.status", new JsonObject { ["job_id"] = id })["data"]?.AsObject();
    }

    private string NextId(string prefix) => $"{prefix}-{++_ids}";

    private JsonObject Command(string actionId, string op, JsonObject args) =>
        new() { ["schema"] = "enfractal.command", ["version"] = 1, ["action_id"] = actionId, ["room_id"] = _roomId, ["op"] = op, ["args"] = args };

    private JsonObject Query(string op, JsonObject args) =>
        Send(new JsonObject { ["schema"] = "enfractal.query", ["version"] = 1, ["query_id"] = "q-" + (++_ids), ["room_id"] = _roomId, ["op"] = op, ["args"] = args }, Player);

    private JsonObject Send(JsonObject message, string principal) => JsonNode.Parse(_host.Handle(message.ToJsonString(), principal))!.AsObject();

    private static bool Ok(JsonObject result) => result["ok"]!.GetValue<bool>();
    private static string? Code(JsonObject result) => result["error"]?["code"]?.GetValue<string>();
    private static float PlanarDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
    private static string Text(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:0.00}, {v.Y:0.000}, {v.Z:0.00})");

    private void Measure(string line)
    {
        _measured.Add(line);
        GD.Print(line);
    }

    private void RemoveSaves()
    {
        var directory = ProjectSettings.GlobalizePath(TestRoot);
        if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
    }

    private void Finish()
    {
        GD.Print($"NATIVE_KERNEL_LANDSCAPE: {_checks - _failures}/{_checks} checks passed; both bodies held inside the bounds, the companion's go_to and fetch and the player's carry on the land, every promised destination reached");
        RemoveSaves();
        CommandHost.SaveRoot = CommandHost.DefaultSaveRoot;
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(bool condition, string label)
    {
        _checks++;
        if (condition) return;
        _failures++;
        GD.PushError("Landscape play: " + label);
    }
}
