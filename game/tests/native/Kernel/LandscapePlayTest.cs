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
/// it down through enfractal.command; every named destination is reachable; the slopes, steps, sticks and falls met
/// on the way are measured; the land's water is found and measured; and the player climbs its cliffs and a tree. The room
/// is derived data and never committed: the runner generates and exports it and passes
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
            // The Gubble floats: it hovers over the land (or water) at its spawn rather than standing on it.
            Check(_world.Player.IsOnFloor() && Hovering(_world.Companion), "the player stands on the land at its spawn, and the Gubble hovers over it");

            // "-- --climb-only" runs the climbing checks alone (for tuning them).
            if (!OS.GetCmdlineUserArgs().Contains("--climb-only"))
            {
                await TestEdgeOfTheWorld();
                await TestCompanionGoesToTheCrate();
                await TestPlayerCarriesTheCrateHome();
                await TestCompanionFetches();
                await TestDestinations();
                await TestNavigationClimb();
            }
            MeasureWater();
            await TestClimbACliff();
            await TestClimbATree();
            await TestClimbATreeWithKeys();
            await MeasureASwim();
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
        // An island (x_landscape_sea): no wall at the bounds any more, but a reef, a current and the beaches.
        if (_world.Room.Sea != null)
        {
            await TestSeaEdge(_world.Room.Sea);
            return;
        }
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
        Measure($"LANDSCAPE_EDGE companion sent 1 m out past the {spot.Side} bound: furthest {worstOut * 100:0.0} cm past, blocked {blocked}, intent {companion.CurrentIntent}, hovering {Hovering(companion)}");
        Check(worstOut <= 0.001f && Hovering(companion), $"the companion stays inside the bounds too, hovering over the land ({worstOut * 100:0.0} cm past)");
        companion.Stop();
        Check(player.TryTeleportTo(_world.Room.SpawnFor("player").PositionM) && companion.TryTeleportTo(_world.Room.SpawnFor("companion").PositionM), "both bodies back at their spawns");
        player.Rotation = new Vector3(0, Mathf.DegToRad(_world.Room.SpawnFor("player").YawDeg), 0);
        await Frames(10);
    }

    private const float EdgeInsetM = 0.10f;

    /// <summary>
    /// The island's edge on the generated garage (the founder, 9 October: "My daughter hates the invisible wall"). From the
    /// water off a beach, out past the reef: a swimmer who stops is carried back by the current; one who swims on washes up on
    /// the nearest beach, standing on the sand facing inland, with the bounds never holding them back; the Gubble comes too.
    /// The bounds' top stands well over the highest land, and the Gubble sent out past the bounds stays inside them, hovering.
    /// </summary>
    private async Task TestSeaEdge(RoomSea sea)
    {
        var bounds = _world.Room.Bounds;
        var player = _world.Player;
        var companion = _world.Companion;
        var space = GetWorld3D().DirectSpaceState;
        Measure($"LANDSCAPE_SEA level {sea.LevelM:0.000} m; coast {sea.Coast.Length} points, reef {sea.Reef.Length} (crest {sea.ReefCrestYM:0.000} m), playable water {sea.PlayArea.Length}; beaches " +
            string.Join(", ", sea.Beaches.Select(b => $"{b.Id} at {Text(b.WashAshoreM)} facing {b.YawDeg:0}")));
        Check(sea.Beaches.Count > 0 && player.Sea == sea && companion.Sea == sea, "the island's sea reaches both bodies, with beaches to wash up on");

        // No wall to climb: the bounds' top over the highest land, by more than a body and the floatiest leap.
        var highest = float.NegativeInfinity;
        for (var x = bounds.Position.X; x < bounds.End.X; x += 0.1f)
            for (var z = bounds.Position.Z; z < bounds.End.Z; z += 0.1f)
            {
                var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(x, bounds.End.Y + 1, z), new Vector3(x, bounds.Position.Y - 1, z), RoomBuilder.BodyMask));
                if (hit.Count > 0) highest = Mathf.Max(highest, hit["position"].AsVector3().Y);
            }
        Measure($"LANDSCAPE_SEA the highest land {highest:0.000} m; the bounds' top {bounds.End.Y:0.000} m");
        Check(highest + player.BodyHeightM + player.MaxJumpApexM < bounds.End.Y, $"the bounds' top stands over the highest land with room for a body and its floatiest leap ({bounds.End.Y - highest:0.00} m over it)");

        // Open water past the reef: off the first beach, outward until past the reef and deep enough to swim.
        var beach = sea.Beaches[0];
        var outward = -(new Basis(Vector3.Up, Mathf.DegToRad(beach.YawDeg)) * Vector3.Forward);
        Vector3? open = null;
        for (var step = 0.0f; step < 3.0f && open == null; step += 0.05f)
        {
            var at = beach.WaterM + outward * step;
            var flat = new Vector2(at.X, at.Z);
            var past = sea.PastReefM(flat);
            if (past > sea.ReefBandHalfWidthM + 0.1f && sea.InPlayArea(flat) && RoomWater.DepthAt(space, new Vector3(at.X, sea.LevelM, at.Z)) > 0.2f) open = new Vector3(at.X, sea.LevelM, at.Z);
        }
        Check(open != null, $"open water past the reef off {beach.Id}");
        if (open is not { } start) return;
        Send(Command(NextId("follow"), "goal.set", new JsonObject { ["actor"] = CompanionAvatar, ["goal"] = "follow" }), Player);
        Check(companion.TryTeleportTo(beach.WashAshoreM + Vector3.Up * 0.01f), "the Gubble waits on the beach");

        // The current: a swimmer who stops out there is carried back toward the island.
        player.SetControlInput(Vector2.Zero);
        player.GlobalPosition = start + Vector3.Down * player.SwimFloatDepthM;
        player.Velocity = Vector3.Zero;
        player.ResetPhysicsInterpolation();
        await Frames(10);
        var pastBefore = sea.PastReefM(new Vector2(player.GlobalPosition.X, player.GlobalPosition.Z));
        var washes = player.WashAshores;
        await Frames(240);
        var pastAfter = sea.PastReefM(new Vector2(player.GlobalPosition.X, player.GlobalPosition.Z));
        Measure($"LANDSCAPE_SEA a swimmer stopped {pastBefore:0.00} m past the reef is carried to {pastAfter:0.00} m in 4 s (swimming {player.IsSwimming}, current {player.SeaCurrent.Length():0.000} m/s)");
        Check(player.IsSwimming && pastAfter < pastBefore - 0.05f && player.WashAshores == washes, "past the reef the current carries a swimmer who stops back toward the island");

        // Swim on out: washed up on the nearest beach, standing, facing inland; the bounds never hold the swimmer back.
        player.Rotation = new Vector3(0, Mathf.Atan2(-outward.X, -outward.Z), 0);
        var stops = player.BoundsStops;
        player.SetControlInput(new Vector2(0, 1));
        var frames = 0;
        var left = Vector2.Zero;
        for (; frames < 3600 && (player.WashAshores == washes || player.WashingAshore); frames++)
        {
            await Frames(1);
            if (player.WashAshores == washes) left = new Vector2(player.GlobalPosition.X, player.GlobalPosition.Z);
        }
        player.SetControlInput(Vector2.Zero);
        await Frames(30);
        var nearest = sea.NearestBeach(left)!.Value;
        var landed = player.GlobalPosition - nearest.WashAshoreM;
        Measure($"LANDSCAPE_SEA swam out {frames / 60.0f:0.0} s and washed up on {player.LastBeachId} (nearest to where it left the water: {nearest.Id}), {new Vector2(landed.X, landed.Z).Length():0.00} m from its spot, facing {Mathf.RadToDeg(player.Rotation.Y):0} deg, on floor {player.IsOnFloor()}, bound stops {player.BoundsStops - stops}");
        Check(player.WashAshores == washes + 1 && player.LastBeachId == nearest.Id && player.IsOnFloor() && !player.IsSwimming && new Vector2(landed.X, landed.Z).Length() < 0.15f &&
            Mathf.Abs(Mathf.AngleDifference(player.Rotation.Y, Mathf.DegToRad(nearest.YawDeg))) < 0.01f && player.BoundsStops == stops,
            "swimming on past the current, the player washes up on the nearest beach, standing on the sand facing inland, never held by the bounds");
        var gap = PlanarDistance(companion.GlobalPosition, player.GlobalPosition);
        Check(gap < 0.4f, $"the Gubble comes too ({gap:0.00} m from the player)");

        // The Gubble sent a metre past the bounds over the open sea: it stays inside them, hovering over the water.
        var far = start + outward * 10;
        companion.GoTo(new Aabb(new Vector3(Mathf.Clamp(far.X, bounds.Position.X - 1, bounds.End.X + 1), sea.LevelM, Mathf.Clamp(far.Z, bounds.Position.Z - 1, bounds.End.Z + 1)), Vector3.Zero), CommandHost.GoToStopM);
        var worstOut = float.NegativeInfinity;
        var lowest = float.PositiveInfinity;
        for (var i = 0; i < 900; i++)
        {
            await Frames(1);
            worstOut = Mathf.Max(worstOut, Outside(bounds, companion.GlobalPosition, companion.BodyRadiusM));
            if (companion.HoversOverWater) lowest = Mathf.Min(lowest, companion.GlobalPosition.Y - sea.LevelM);
        }
        Measure($"LANDSCAPE_SEA the Gubble sent past the bounds over the sea: furthest {worstOut * 100:0.0} cm past, lowest {lowest * 100:0.0} cm over the water, intent {companion.CurrentIntent}, blocked {companion.GoalBlocked}");
        Check(worstOut <= 0.001f && lowest > 0.005f && companion.WashAshores == 0, "the Gubble floats out over the sea and is never lost: inside the bounds, over the water");
        companion.Stop();
        Check(player.TryTeleportTo(_world.Room.SpawnFor("player").PositionM) && companion.TryTeleportTo(_world.Room.SpawnFor("companion").PositionM), "both bodies back at their spawns");
        player.Rotation = new Vector3(0, Mathf.DegToRad(_world.Room.SpawnFor("player").YawDeg), 0);
        await Frames(10);
    }

    /// <summary>The Gubble hovers within its hover height (and bob, and a little settling) over the ground or water under it.</summary>
    private static bool Hovering(SmallPlayerController body) =>
        body.Floats && body.HoverSupportY is { } top && body.GlobalPosition.Y - top > 0.005f && body.GlobalPosition.Y - top < body.HoverHeightM + 0.015f;

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
        var setDown = CrateBox();
        var groundUnder = GroundBelow(setDown.GetCenter());
        Check(Ok(down) && Supported(setDown), $"the companion sets it down on the land: {Code(down)} (the crate's foot at {Text(new Vector3(setDown.GetCenter().X, setDown.Position.Y, setDown.GetCenter().Z))}, the ground under its middle at {groundUnder:0.000})");
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
        // An island's walkable map is its land (RoomNavigation.Attach): survey the same, and measure the bake with the sea too.
        var sea = _world.Room.Sea;
        var bounds = sea?.LandBounds(_world.Room.Bounds) ?? _world.Room.Bounds;
        if (sea != null)
        {
            var whole = RoomNavigation.Create(this, _world.Built, _world.Room.Bounds, WorldScaleProfile.Companion, 0.01f);
            for (var i = 0; i < 30 && !whole.IsReady; i++) await Frames(1);
            var land = new List<double>();
            var all = new List<double>();
            for (var i = 0; i < 3; i++) { _navigation.Bake(); land.Add(_navigation.LastBakeMs); whole.Bake(); all.Add(whole.LastBakeMs); }
            land.Sort();
            all.Sort();
            Measure($"LANDSCAPE_NAVIGATION island bake on 2 cm cells: the land alone {land[1]:0} ms ({_navigation.PolygonCount} polygons), the whole bounds with the sea floor {all[1]:0} ms ({whole.PolygonCount} polygons)");
            Check(land[1] < all[1], "the island's land-only bake is quicker than baking the sea floor too");
            whole.QueueFree();
        }
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

    // ---- climbing and water on the land (founder's playtest round, 8 October) ----

    private readonly record struct Cliff(Vector3 Foot, Vector3 Direction, float RiseM, float SteepestDeg);

    /// <summary>What a ray straight down first meets from the top of the room, and whether that is the terrain itself.</summary>
    private (Vector3 Point, float Slope, bool Terrain)? FirstFromAbove(float x, float z)
    {
        var bounds = _world.Room.Bounds;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(x, bounds.End.Y + 1, z), new Vector3(x, bounds.Position.Y - 1, z), RoomBuilder.WorldLayer));
        if (hit.Count == 0) return null;
        var id = (hit["collider"].AsGodotObject() as Node)?.GetMeta("entity_id", "").AsString() ?? "";
        return (hit["position"].AsVector3(), Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(hit["normal"].AsVector3().Y, -1, 1))), id.StartsWith("shell:terrain_", StringComparison.Ordinal));
    }

    /// <summary>
    /// Cliffs of the land itself: standable terrain at a foot, a terrain face steeper than 55 degrees starting within a few
    /// centimetres of it along one of eight headings, and standable terrain at its top 6 to 30 cm higher, well inside the
    /// bounds, with nothing over any of it (a tree's crown over the foot is a tree, not a cliff: the fix round found the
    /// search had been standing the body on crown caps).
    /// </summary>
    private List<Cliff> FindCliffs()
    {
        var bounds = _world.Room.Bounds;
        var found = new List<Cliff>();
        for (var x = bounds.Position.X + 0.4f; x < bounds.End.X - 0.4f; x += 0.1f)
            for (var z = bounds.Position.Z + 0.4f; z < bounds.End.Z - 0.4f; z += 0.1f)
            {
                if (FirstFromAbove(x, z) is not { Terrain: true } foot || foot.Slope > 30f) continue;
                for (var heading = 0; heading < 8; heading++)
                {
                    var direction = new Vector3(Mathf.Cos(heading * Mathf.Pi / 4), 0, Mathf.Sin(heading * Mathf.Pi / 4));
                    var steepest = 0f;
                    var faceStarted = false;
                    var flat = 0;
                    Vector3? top = null;
                    for (var r = 0.02f; r <= 0.5f; r += 0.01f)
                    {
                        var at = foot.Point + direction * r;
                        if (FirstFromAbove(at.X, at.Z) is not { Terrain: true } here) break;
                        if (!faceStarted)
                        {
                            if (here.Slope > 55f) faceStarted = true;
                            else if (r > 0.05f) break;
                            continue;
                        }
                        steepest = Mathf.Max(steepest, here.Slope);
                        if (here.Slope <= 35f) { flat++; top ??= here.Point; if (flat >= 5) break; }
                        else if (here.Slope > 45f) { flat = 0; top = null; }
                    }
                    if (top is not { } summit || flat < 5) continue;
                    var rise = summit.Y - foot.Point.Y;
                    if (rise >= 0.06f && rise <= 0.3f) found.Add(new Cliff(foot.Point, direction, rise, steepest));
                }
            }
        // Cliffs of a middling height first: tall enough to be a climb, short enough to finish quickly.
        return found.OrderBy(c => Mathf.Abs(c.RiseM - 0.12f)).ToList();
    }

    /// <summary>
    /// The player climbs the land's cliffs: walks into a foot, grabs the face, climbs and pulls over onto the top. Every
    /// attempt is reported. At least four of the first five must succeed, none may need a recovery, and no pull-over may
    /// end lower than where it finished (a pull-over never ends in a fall).
    /// </summary>
    private async Task TestClimbACliff()
    {
        var player = _world.Player;
        var cliffs = FindCliffs();
        Measure($"LANDSCAPE_CLIFFS {cliffs.Count} terrain cliff faces 6 to 30 cm high with standable terrain at the foot and the top, nothing over them (10 cm grid, eight headings)");
        Check(cliffs.Count > 0, "the land has cliffs to climb");
        var attempts = 0;
        var climbed = 0;
        var recoveries = player.Recoveries;
        var droppedAfterPull = 0;
        foreach (var cliff in cliffs)
        {
            if (attempts >= 5) break;
            if (!player.TryTeleportTo(cliff.Foot - cliff.Direction * 0.02f)) continue;
            attempts++;
            player.Rotation = new Vector3(0, Mathf.Atan2(-cliff.Direction.X, -cliff.Direction.Z), 0);
            await Frames(5);
            var start = player.GlobalPosition;
            var grabs = player.Grabs;
            var pulls = player.PullOvers;
            var frames = 0;
            var pulledAt = float.NaN;
            player.SetControlInput(new Vector2(0, 1));
            for (; frames < 900 && !(player.PullOvers > pulls && player.IsOnFloor() && !player.IsClimbing); frames++)
            {
                await Frames(1);
                if (float.IsNaN(pulledAt) && player.PullOvers > pulls) pulledAt = player.GlobalPosition.Y;
            }
            player.SetControlInput(Vector2.Zero);
            await Frames(20);
            var rose = player.GlobalPosition.Y - start.Y;
            var success = player.Grabs > grabs && player.PullOvers > pulls && player.IsOnFloor() && rose > cliff.RiseM * 0.7f;
            if (success) climbed++;
            if (!float.IsNaN(pulledAt) && player.GlobalPosition.Y < pulledAt - 0.01f) droppedAfterPull++;
            Measure($"LANDSCAPE_CLIMB a {cliff.RiseM * 100:0} cm cliff (steepest {cliff.SteepestDeg:0} deg) at {Text(cliff.Foot)}: {(success ? "climbed" : "not climbed")}; grabbed {player.Grabs - grabs}, pulled over {player.PullOvers - pulls}, rose {rose * 100:0.0} cm in {frames / 60.0:0.0} s, " +
                    $"ends at {Text(player.GlobalPosition)} on floor {player.IsOnFloor()}, climbing {player.IsClimbing}");
        }
        Check(climbed >= Mathf.Min(4, attempts) && attempts > 0, $"the player climbs the land's cliffs and pulls itself over the top ({climbed} of {attempts})");
        Check(player.Recoveries == recoveries && droppedAfterPull == 0, $"no climb needs a recovery and no pull-over ends in a fall (recoveries {player.Recoveries - recoveries}, drops after a pull-over {droppedAfterPull})");
        Check(player.TryTeleportTo(_world.Room.SpawnFor("player").PositionM), "the player back at the spawn after the cliffs");
        await Frames(10);
    }

    /// <summary>
    /// A tree of the land, climbed to its crown: the trunk, the hidden climbing pole through the leaves, up through the
    /// one-sided caps from inside, and standing on a cap (shell:tree_climb_foliage, collision only). The tree is one whose
    /// foot the player can walk to from the spawn.
    /// </summary>
    private async Task TestClimbATree()
    {
        var player = _world.Player;
        var poles = _world.Built.GetNodeOrNull<Node3D>("Shell/" + RoomBuilder.NodeName("shell:tree_climb_bark"));
        if (poles == null)
        {
            Measure("LANDSCAPE_TREE the room has no tree climbing poles (an export before brief 19)");
            return;
        }
        // The poles' axes: their collision vertices, clustered by where they stand.
        var axes = new List<(Vector2 Centre, float Bottom, float Top, int Count)>();
        foreach (var shapeNode in poles.GetChildren().OfType<CollisionShape3D>())
            if (shapeNode.Shape is ConcavePolygonShape3D shape)
                foreach (var local in shape.Data)
                {
                    var v = poles.GlobalTransform * shapeNode.Transform * local;
                    var index = axes.FindIndex(a => a.Centre.DistanceTo(new Vector2(v.X, v.Z)) < 0.08f);
                    if (index < 0) axes.Add((new Vector2(v.X, v.Z), v.Y, v.Y, 1));
                    else
                    {
                        var a = axes[index];
                        axes[index] = (a.Centre + (new Vector2(v.X, v.Z) - a.Centre) / (a.Count + 1), Mathf.Min(a.Bottom, v.Y), Mathf.Max(a.Top, v.Y), a.Count + 1);
                    }
                }
        var spawn = _world.Room.SpawnFor("player").PositionM;
        var space = GetWorld3D().DirectSpaceState;
        var tried = 0;
        var climbed = false;
        foreach (var (centre, bottom, top, _) in axes.OrderBy(a => a.Top - a.Bottom))
        {
            if (tried >= 4 || climbed) break;
            Vector3? foot = null;
            Vector3 toward = Vector3.Zero;
            for (var heading = 0; heading < 8 && foot == null; heading++)
            {
                var out2 = new Vector2(Mathf.Cos(heading * Mathf.Pi / 4), Mathf.Sin(heading * Mathf.Pi / 4));
                var at = centre + out2 * 0.12f;
                var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(at.X, bottom, at.Y), new Vector3(at.X, bottom - 1.0f, at.Y), RoomBuilder.WorldLayer));
                if (hit.Count == 0 || !((hit["collider"].AsGodotObject() as Node)?.GetMeta("entity_id", "").AsString() ?? "").StartsWith("shell:terrain_", StringComparison.Ordinal)) continue;
                var spot = hit["position"].AsVector3();
                if (hit["normal"].AsVector3().Y < 0.85f || !_navigation.FindRoute(spawn, spot, 0.06f).Reaches || !player.TryTeleportTo(spot)) continue;
                foot = player.GlobalPosition;
                toward = new Vector3(-out2.X, 0, -out2.Y);
            }
            if (foot is not { } start) continue;
            tried++;
            player.Rotation = new Vector3(0, Mathf.Atan2(-toward.X, -toward.Z), 0);
            await Frames(5);
            var grabs = player.Grabs;
            var pulls = player.PullOvers;
            var frames = 0;
            var highest = start.Y;
            player.SetControlInput(new Vector2(0, 1));
            for (; frames < 1500 && !(player.PullOvers > pulls && player.IsOnFloor() && !player.IsClimbing); frames++)
            {
                await Frames(1);
                highest = Mathf.Max(highest, player.GlobalPosition.Y);
            }
            player.SetControlInput(Vector2.Zero);
            await Frames(30);
            // What the body stands on, as the body sees it: the world and the hidden layer, where the crown caps are.
            var under = space.IntersectRay(PhysicsRayQueryParameters3D.Create(player.GlobalPosition + Vector3.Up * 0.02f, player.GlobalPosition + Vector3.Down * 0.03f, RoomBuilder.BodyMask, new Godot.Collections.Array<Rid> { player.GetRid() }));
            var standingOn = under.Count > 0 ? (under["collider"].AsGodotObject() as Node)?.GetMeta("entity_id", "").AsString() ?? "" : "";
            climbed = player.Grabs > grabs && player.PullOvers > pulls && player.IsOnFloor() && standingOn == "shell:tree_climb_foliage" && player.GlobalPosition.Y - start.Y > 0.12f;
            if (climbed) _climbedTree = (start, toward);
            Measure($"LANDSCAPE_TREE a tree at ({centre.X:0.00}, {centre.Y:0.00}), pole {bottom:0.000} to {top:0.000} m, its foot walkable from the spawn: {(climbed ? "climbed to its crown" : "not climbed")}; grabbed {player.Grabs - grabs}, pulled over {player.PullOvers - pulls}, " +
                    $"rose {(player.GlobalPosition.Y - start.Y) * 100:0.0} cm (highest {(highest - start.Y) * 100:0.0} cm) in {frames / 60.0:0.0} s, standing on {(standingOn.Length > 0 ? standingOn : "nothing")} at {Text(player.GlobalPosition)}");
        }
        Check(climbed, "the player climbs a tree of the land, up through its leaves, and stands on its crown");
        Check(player.TryTeleportTo(_world.Room.SpawnFor("player").PositionM), "the player back at the spawn after the tree");
        await Frames(10);
    }

    private Vector3? _deepestWater;

    /// <summary>
    /// A swim in the land's deepest water, when it is deep enough (Lane C's deeper garage pond merges after this round):
    /// walk in from dry ground the player can reach, swim, turn round and walk out. Measured; the checks are only that no
    /// recovery is needed and the eye never goes under.
    /// </summary>
    private async Task MeasureASwim()
    {
        if (_deepestWater is not { } deep)
        {
            Measure("LANDSCAPE_SWIM no open water deep enough to swim yet");
            return;
        }
        var player = _world.Player;
        var space = GetWorld3D().DirectSpaceState;
        var spawn = _world.Room.SpawnFor("player").PositionM;
        Vector3? start = null;
        for (var radius = 0.25f; radius <= 1.2f && start == null; radius += 0.1f)
            for (var heading = 0; heading < 16 && start == null; heading++)
            {
                var at = deep + new Vector3(Mathf.Cos(heading * Mathf.Pi / 8), 0, Mathf.Sin(heading * Mathf.Pi / 8)) * radius;
                if (FirstFromAbove(at.X, at.Z) is not { Terrain: true } ground || ground.Slope > 20f) continue;
                if (RoomWater.At(space, ground.Point + Vector3.Up * 0.001f, 0.4f, 0.02f).Wet || !_navigation.FindRoute(spawn, ground.Point, 0.06f).Reaches) continue;
                if (player.TryTeleportTo(ground.Point)) start = player.GlobalPosition;
            }
        if (start is not { } from)
        {
            Measure($"LANDSCAPE_SWIM no dry, reachable ground found near the deepest water at {Text(deep)}");
            return;
        }
        var toward = new Vector3(deep.X - from.X, 0, deep.Z - from.Z).Normalized();
        player.Rotation = new Vector3(0, Mathf.Atan2(-toward.X, -toward.Z), 0);
        await Frames(5);
        var recoveries = player.Recoveries;
        var eyeUnder = 0;
        var swamAt = -1;
        var frames = 0;
        player.SetControlInput(new Vector2(0, 1));
        for (; frames < 900 && (swamAt < 0 || frames < swamAt + 60); frames++)
        {
            await Frames(1);
            if (swamAt < 0 && player.IsSwimming) swamAt = frames;
            if (player.Water.Wet && player.GlobalPosition.Y + player.EyeCamera.Position.Y < player.Water.SurfaceY - 0.001f) eyeUnder++;
        }
        var swimmingAt = player.GlobalPosition;
        player.Rotation = new Vector3(0, player.Rotation.Y + Mathf.Pi, 0);
        var outFrames = 0;
        for (; outFrames < 900 && (player.IsSwimming || player.Water.Wet || !player.IsOnFloor()); outFrames++)
        {
            await Frames(1);
            if (player.Water.Wet && player.GlobalPosition.Y + player.EyeCamera.Position.Y < player.Water.SurfaceY - 0.001f) eyeUnder++;
        }
        player.SetControlInput(Vector2.Zero);
        Measure($"LANDSCAPE_SWIM from {Text(from)} toward the deepest water at {Text(deep)}: {(swamAt >= 0 ? $"swimming after {swamAt / 60.0:0.0} s, at {Text(swimmingAt)}" : "never swimming")}; " +
                $"{(player.IsOnFloor() && !player.IsSwimming ? $"walked out in {outFrames / 60.0:0.0} s to {Text(player.GlobalPosition)}" : "did not walk out")}; ticks with the eye under {eyeUnder}");
        Check(player.Recoveries == recoveries && eyeUnder == 0, "a swim in the land's water needs no recovery and never puts the eye under");
        Check(player.TryTeleportTo(_world.Room.SpawnFor("player").PositionM), "the player back at the spawn after the swim");
        await Frames(10);
    }

    private (Vector3 Start, Vector3 Toward)? _climbedTree;

    /// <summary>
    /// The founder's playtest, 9 October: "I can climb around halfway up and then something happens to the controls and
    /// then I seem to be forced to climb down." The same tree climbed as a player climbs it: the W key held through the
    /// keyboard path, the view (the body's heading in F2) turned 25 degrees once on the trunk, as a hand on the mouse does,
    /// the follow camera live.
    /// It must reach the crown cap without ever being carried down while W is held, and the follow camera must not be
    /// pulled in by the tree's hidden climbing parts.
    /// </summary>
    private async Task TestClimbATreeWithKeys()
    {
        if (_climbedTree is not { } tree)
        {
            Measure("LANDSCAPE_TREE_KEYS no tree was climbed to try with the keys");
            return;
        }
        var player = _world.Player;
        var hud = _world.GetNode<RoomHud>("RoomHud");
        hud.SetViewMode(1);
        Check(player.TryTeleportTo(tree.Start), "the player back at the foot of the tree, for the keys");
        player.Rotation = new Vector3(0, Mathf.Atan2(-tree.Toward.X, -tree.Toward.Z), 0);
        await Frames(10);
        var camera = player.GetNode<SpringArm3D>("FollowCameraArm").GetNode<Camera3D>("FollowCamera");
        var start = player.GlobalPosition;
        var grabs = player.Grabs;
        var pulls = player.PullOvers;
        var downTicks = 0;
        var highest = start.Y;
        var nearestCamera = float.MaxValue;
        var worstTurn = 0.0f;
        var turned = false;
        var frames = 0;
        player.ReadKeyboard = true;
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.W, Pressed = true });
        var lastY = player.GlobalPosition.Y;
        for (; frames < 1800 && !(player.PullOvers > pulls && player.IsOnFloor() && !player.IsClimbing); frames++)
        {
            await Frames(1);
            var y = player.GlobalPosition.Y;
            // Once on the trunk, the mouse turns the view 25 degrees, as a player looking round does.
            if (player.IsClimbing && !turned)
            {
                player.Rotation = new Vector3(0, player.Rotation.Y + Mathf.DegToRad(25), 0);
                turned = true;
            }
            if (player.IsClimbing && !player.IsPullingOver && y < lastY - 0.0005f) downTicks++;
            lastY = y;
            highest = Mathf.Max(highest, y);
            if (player.IsClimbing) nearestCamera = Mathf.Min(nearestCamera, camera.GlobalPosition.DistanceTo(player.GlobalPosition + Vector3.Up * player.BodyHeightM * 0.8f));
            if (player.IsClimbing)
            {
                var inward = -new Vector3(player.ClimbNormal.X, 0, player.ClimbNormal.Z).Normalized();
                var forward = -player.GlobalBasis.Z;
                worstTurn = Mathf.Max(worstTurn, Mathf.RadToDeg(forward.AngleTo(inward)));
            }
        }
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.W, Pressed = false });
        player.ReadKeyboard = false;
        await Frames(30);
        var space = GetWorld3D().DirectSpaceState;
        // What the body stands on, as the body sees it: the world and the hidden layer, where the crown caps are.
        var under = space.IntersectRay(PhysicsRayQueryParameters3D.Create(player.GlobalPosition + Vector3.Up * 0.02f, player.GlobalPosition + Vector3.Down * 0.03f, RoomBuilder.BodyMask, new Godot.Collections.Array<Rid> { player.GetRid() }));
        var standingOn = under.Count > 0 ? (under["collider"].AsGodotObject() as Node)?.GetMeta("entity_id", "").AsString() ?? "" : "";
        var reached = player.Grabs > grabs && player.PullOvers > pulls && player.IsOnFloor() && standingOn == "shell:tree_climb_foliage";
        Measure($"LANDSCAPE_TREE_KEYS W held, the view turned 25 deg once on the trunk, the follow camera live: {(reached ? "reached the crown" : "did not reach the crown")}; grabbed {player.Grabs - grabs}, pulled over {player.PullOvers - pulls}, " +
                $"rose {(player.GlobalPosition.Y - start.Y) * 100:0.0} cm (highest {(highest - start.Y) * 100:0.0} cm) in {frames / 60.0:0.0} s; climbing down while W was held {downTicks} ticks, the face up to {worstTurn:0} deg off the body's heading; " +
                $"the follow camera came within {nearestCamera * 100:0.0} cm of the head (its arm is {player.BodyHeightM * 3.2f * 100:0} cm); standing on {(standingOn.Length > 0 ? standingOn : "nothing")}");
        Check(reached && downTicks == 0, "with the keys and the follow camera, a tree is climbed to its crown and W never carries the climber down");
        Check(nearestCamera > player.BodyHeightM * 3.2f * 0.5f, "the tree's hidden climbing parts never pull the follow camera in");
        Check(player.TryTeleportTo(_world.Room.SpawnFor("player").PositionM), "the player back at the spawn after the keyed climb");
        await Frames(10);
    }

    /// <summary>The land's water, as the body sees it (RoomWater): where it is open and how deep. The garage's water is shallow until Lane C's deeper ponds.</summary>
    private void MeasureWater()
    {
        var bounds = _world.Room.Bounds;
        var space = GetWorld3D().DirectSpaceState;
        int wet = 0, swimmable = 0, bottomless = 0;
        var deepest = 0f;
        var deepestAt = Vector3.Zero;
        var bottomlessAt = Vector3.Zero;
        for (var x = bounds.Position.X + 0.05f; x < bounds.End.X; x += 0.1f)
            for (var z = bounds.Position.Z + 0.05f; z < bounds.End.Z; z += 0.1f)
            {
                if (RoomWater.SurfaceBelow(space, new Vector3(x, bounds.End.Y + 1, z), bounds.Size.Y + 2) is not { } level) continue;
                // The exported water sheet runs on under the shore: water the land covers is no water to the body.
                if (space.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(x, bounds.End.Y + 1, z), new Vector3(x, level, z), RoomBuilder.WorldLayer)).Count > 0) continue;
                var column = RoomWater.At(space, new Vector3(x, level, z), 0.01f, 0.01f);
                if (!column.Wet) continue;
                wet++;
                if (column.DepthM >= RoomWater.BedSearchM - 0.001f) { bottomless++; bottomlessAt = new Vector3(x, level, z); continue; }
                if (column.DepthM >= _world.Player.SwimDepthM) swimmable++;
                if (column.DepthM > deepest) (deepest, deepestAt) = (column.DepthM, new Vector3(x, column.SurfaceY, z));
            }
        _deepestWater = deepest >= _world.Player.SwimDepthM + 0.02f ? deepestAt : null;
        var areas = _world.Built.GetNode("Shell").GetChildren().Count(part => part.GetNodeOrNull<Area3D>(RoomWater.ColliderName) is { CollisionLayer: RoomWater.Layer });
        Measure($"LANDSCAPE_WATER {areas} water parts with query-only colliders on layer 4; {wet} points of open water on a 10 cm grid, {swimmable} deep enough to swim ({_world.Player.SwimDepthM * 100:0} cm); deepest {deepest * 100:0.0} cm at {Text(deepestAt)}; " +
                $"{bottomless} with no ground under the water within {RoomWater.BedSearchM:0} m{(bottomless > 0 ? ", for example at " + Text(bottomlessAt) : "")}");
        Check(areas > 0 && wet > 0, "the land's water parts carry water colliders the body can find");
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

    /// <summary>The first world-layer surface straight under a point (NaN when none within 2 m).</summary>
    private float GroundBelow(Vector3 point)
    {
        var query = PhysicsRayQueryParameters3D.Create(point, point + Vector3.Down * 2, RoomBuilder.WorldLayer);
        var crate = _world.Built.GetNode("Objects").GetChildren().OfType<CollisionObject3D>().FirstOrDefault(n => n.GetMeta("entity_id").AsString() == "obj:apple_crate");
        if (crate != null) query.Exclude = new Godot.Collections.Array<Rid> { crate.GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? hit["position"].AsVector3().Y : float.NaN;
    }

    /// <summary>
    /// Whether the box rests on something: under its middle or any corner (1 mm in), the world lies within 4 mm of its foot.
    /// (A ray under its middle alone missed a crate resting on the island garage's slope by its uphill edge.)
    /// </summary>
    private bool Supported(Aabb box)
    {
        var crate = _world.Built.GetNode("Objects").GetChildren().OfType<CollisionObject3D>().FirstOrDefault(n => n.GetMeta("entity_id").AsString() == "obj:apple_crate");
        var centre = box.GetCenter();
        var half = new Vector2(box.Size.X * 0.5f - 0.001f, box.Size.Z * 0.5f - 0.001f);
        foreach (var (x, z) in new[] { (0f, 0f), (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            var at = new Vector3(centre.X + x * half.X, box.Position.Y, centre.Z + z * half.Y);
            var query = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 0.002f, at + Vector3.Down * 0.004f, RoomBuilder.WorldLayer);
            if (crate != null) query.Exclude = new Godot.Collections.Array<Rid> { crate.GetRid() };
            if (GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0) return true;
        }
        return false;
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
        GD.Print($"NATIVE_KERNEL_LANDSCAPE: {_checks - _failures}/{_checks} checks passed; both bodies held inside the bounds, the companion's go_to and fetch and the player's carry on the land, every promised destination reached, cliffs and a tree climbed");
        RemoveSaves();
        CommandHost.SaveRoot = CommandHost.DefaultSaveRoot;
        // Let the wrappers this long run left to the finalizer go before the engine tears down: in the Linux container's
        // .NET, wrappers still pending at exit can abort Godot's shutdown after every check has passed.
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
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
