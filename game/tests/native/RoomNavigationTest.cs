using Godot;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Navigation;

namespace EnFractal.Tests;

/// <summary>
/// The founder's report in the real test room: "If I walk behind the big box and summon my comp, it gets stuck on
/// the other side of the box." Boots the room as the game does, puts the box between the two avatars, and sends
/// come through the command host as key 3 does. No room or save files are written.
/// </summary>
public partial class RoomNavigationTest : Node3D
{
    private int _checks;
    private int _failures;

    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
            AddChild(world);
            for (var i = 0; i < 120 && !world.WorldReady && world.LoadError.Length == 0; i++) await Frames(1);
            Check(world.WorldReady, "the room boots: " + world.LoadError);
            if (!world.WorldReady) { Finish(); return; }
            await Frames(5);
            var navigation = world.GetNodeOrNull<RoomNavigation>("RoomNavigation");
            Check(navigation != null && navigation.IsReady && world.Companion.Navigation == navigation,
                "the room bakes a navigation mesh from its static collision and gives it to the companion");
            if (navigation == null) { Finish(); return; }
            GD.Print($"ROOM_NAVIGATION_MEASURED {navigation.PolygonCount} polygons, bake {navigation.LastBakeMs:0.0} ms, agent radius {navigation.AgentRadiusM:0.000} m");

            // The big box (obj:box): 35 x 30 x 35 cm with its foot at (1.1, 0, 0.2). The player stands behind it,
            // the companion on the far side, so the straight line between them runs through the box.
            var box = world.Built.GetNode<Node3D>("Objects/box").GlobalPosition;
            var player = world.Player;
            var companion = world.Companion;
            var playerSpot = box + new Vector3(0, 0.003f, 0.43f);
            var farSide = box + new Vector3(0, 0.01f, -0.45f);
            var host = EnFractal.Native.Kernel.CommandHost.Of(world)!;
            host.PlayerGoal("stop");
            // No walkable floor inside the box (Lane P's finding, the open sea round): Recast rasterizes surfaces, not solids, so
            // the floor's top under a box taller than the agent kept a walkable polygon there, and the Gubble's place beside a
            // climber snapped inside a cliff. The nearest walkable point to the box's middle at floor height is its top or outside it.
            var inside = navigation!.ClosestPoint(box + new Vector3(0, 0.01f, 0));
            var underBox = Mathf.Abs(inside.X - box.X) < 0.17f && Mathf.Abs(inside.Z - box.Z) < 0.17f && inside.Y < box.Y + 0.25f;
            GD.Print($"NAVIGATION_MEASURED the nearest walkable point to the big box's middle at floor height: {inside - box} from the box's foot");
            Check(!underBox, $"no walkable floor inside the big box: the nearest walkable point to its middle at floor height is {inside - box} from its foot");
            Check(player.TryTeleportTo(playerSpot) && companion.TryTeleportTo(farSide), "player behind the big box, companion on the far side");
            var route = navigation.FindRoute(companion.GlobalPosition, player.GlobalPosition, 0.05f);
            Check(route.Reaches && route.LengthM > PlanarDistance(companion.GlobalPosition, player.GlobalPosition) + 0.1f,
                $"the planned route goes round the box (route {route.LengthM:0.00} m against {PlanarDistance(companion.GlobalPosition, player.GlobalPosition):0.00} m straight)");

            // Without navigation, as in the founder's playtest, it was stuck on the far side. The Gubble now floats: with no
            // walkable map it floats straight to the player, over the 30 cm box and never through it.
            companion.BindNavigation(null);
            var sent = host.PlayerGoal("come");
            Check(sent["ok"]!.GetValue<bool>(), "come is accepted from the player through the command host");
            var highestOver = 0.0f;
            var into = false;
            for (var i = 0; i < 480 && companion.CurrentIntent == "come"; i++)
            {
                await Frames(1);
                // Over the box by more than its radius (its rounded foot may round the edge), the body must be above the top.
                if (ClearanceFromSquare(companion.GlobalPosition, box, 0.175f - companion.BodyRadiusM) == 0)
                {
                    highestOver = Mathf.Max(highestOver, companion.GlobalPosition.Y - box.Y);
                    into |= companion.GlobalPosition.Y - box.Y < 0.30f - 0.002f;
                }
            }
            var gap = PlanarDistance(companion.GlobalPosition, player.GlobalPosition);
            GD.Print($"ROOM_NAVIGATION_MEASURED without navigation: intent {companion.CurrentIntent}, gap {gap:0.00} m, highest over the box {highestOver:0.000} m");
            Check(companion.CurrentIntent == "stay" && gap <= CompanionAvatar.ComeArrivalM + 0.02f && highestOver > 0.30f && !into,
                "without a walkable map the Gubble floats over the big box (never into it) and arrives, instead of staying stuck behind it");

            // With the room's navigation: round the box and arrived.
            host.PlayerGoal("stop");
            Check(companion.TryTeleportTo(farSide), "companion back on the far side");
            companion.BindNavigation(navigation);
            host.PlayerGoal("come");
            var frames = 0;
            var blocked = 0;
            var entered = false;
            for (; frames < 360 && companion.CurrentIntent == "come"; frames++)
            {
                await Frames(1);
                if (companion.GoalBlocked) blocked++;
                entered |= ClearanceFromSquare(companion.GlobalPosition, box, 0.175f) < companion.BodyRadiusM - 0.005f;
            }
            gap = PlanarDistance(companion.GlobalPosition, player.GlobalPosition);
            GD.Print($"ROOM_NAVIGATION_MEASURED with navigation: arrived in {frames} frames, gap {gap:0.00} m, blocked frames {blocked}");
            Check(companion.CurrentIntent == "stay" && gap <= CompanionAvatar.ComeArrivalM + 0.02f && blocked == 0 && !entered,
                "with navigation the summoned companion walks round the big box and arrives beside the player");

            // Follow from the far side: it comes round the box to rest near the player instead of waiting behind it.
            Check(companion.TryTeleportTo(farSide), "companion on the far side again");
            host.PlayerGoal("follow");
            await Frames(300);
            gap = PlanarDistance(companion.GlobalPosition, player.GlobalPosition);
            var way = navigation.FindRoute(companion.GlobalPosition, player.GlobalPosition, 0.05f);
            GD.Print($"ROOM_NAVIGATION_MEASURED follow from the far side: gap {gap:0.00} m, walk {way.LengthM:0.00} m");
            Check(!companion.FollowMoving && gap <= CompanionAvatar.FollowFarM + 0.01f && way.LengthM <= gap + 0.05f,
                "follow from the far side of the box comes round and rests with nothing between them");
            host.PlayerGoal("stop");
            await MeasureCellSizes(world);
            await TestBeyondTheSearchCap(navigation);
            Finish();
        }
        catch (System.Exception exception)
        {
            GD.PushError("Room navigation test exception: " + exception);
            GetTree().Quit(1);
        }
    }

    /// <summary>
    /// The backlog's cell-size question: a re-bake runs in the physics frame (RoomNavigation.Bake), so its time is the stall
    /// a moved thing causes. Bake the real test room at three cell sizes, a few times each, and print the times; the frame at
    /// 60 Hz is 16.7 ms. The game's choice is RoomNavigation.CellSizeM.
    /// </summary>
    private async Task MeasureCellSizes(RoomWorld world)
    {
        foreach (var cell in new[] { 0.01f, 0.02f, 0.03f })
        {
            var probe = RoomNavigation.Create(this, world.Built, world.Room.Bounds, WorldScaleProfile.Companion, world.Companion.StepHeightM * 0.75f, cell);
            await Frames(2);
            var times = new System.Collections.Generic.List<double> { probe.LastBakeMs };
            for (var i = 0; i < 3; i++) { probe.Bake(); times.Add(probe.LastBakeMs); }
            times.Sort();
            GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"ROOM_NAVIGATION_CELL test room, cell {cell * 100:0} cm: median bake {times[times.Count / 2]:0.0} ms (min {times[0]:0.0}, max {times[^1]:0.0}), {probe.PolygonCount} polygons, agent radius {probe.AgentRadiusM:0.000} m"));
            Check(probe.IsReady && probe.PolygonCount > 0, $"the test room bakes at {cell * 100:0} cm cells");
            probe.QueueFree();
            await Frames(2);
        }
        var game = world.GetNode<RoomNavigation>("RoomNavigation");
        var rebakes = new System.Collections.Generic.List<double>();
        for (var i = 0; i < 5; i++) { game.Bake(); rebakes.Add(game.LastBakeMs); }
        rebakes.Sort();
        // Headless here; the founder's machine is faster. The bar is a re-bake well inside two frames, not a stall of many.
        Check(game.CellM == RoomNavigation.CellSizeM && rebakes[2] < 2 * 1000.0 / 60.0,
            $"the game's cell ({RoomNavigation.CellSizeM * 100:0} cm) re-bakes the test room within two 60 Hz frames (median {rebakes[2]:0.0} ms)");
    }

    /// <summary>
    /// The founder's walk on the garage island magnified ten times (13 October): the Gubble got lost trying to reach the player.
    /// Godot's path query gives up after a number of polygons (4,096 by default) and returns a path to the polygon nearest the
    /// goal found so far; that island bakes to 55,110. Here a 5 m field of 5 cm squares (9,900 polygons, made by hand: Recast
    /// takes minutes over thousands of pillar holes) on the room navigation's own map, split by a wall whose only gap is at the
    /// far end. Start and goal stand a hand apart on either side of the wall at its closed end, so the search must cover nearly
    /// the whole field. The default query stops short; the room's route reaches round the far end.
    /// </summary>
    private async Task TestBeyondTheSearchCap(RoomNavigation navigation)
    {
        const int count = 100;
        const float square = 0.05f;
        const int gap = 8;
        var origin = new Vector3(100, 0, 0);
        int At(int i, int j) => i * (count + 1) + j;
        var vertices = new Vector3[(count + 1) * (count + 1)];
        for (var i = 0; i <= count; i++)
            for (var j = 0; j <= count; j++)
                vertices[At(i, j)] = origin + new Vector3(i * square, 0, j * square);
        var mesh = new NavigationMesh { CellSize = navigation.CellM, CellHeight = RoomNavigation.CellHeightM, Vertices = vertices };
        var wall = count / 2;
        for (var i = 0; i < count; i++)
            for (var j = 0; j < count; j++)
                if (i != wall || j >= count - gap) mesh.AddPolygon(new[] { At(i, j), At(i + 1, j), At(i + 1, j + 1), At(i, j + 1) });
        var region = NavigationServer3D.RegionCreate();
        NavigationServer3D.RegionSetUseAsyncIterations(region, false);
        NavigationServer3D.RegionSetMap(region, navigation.Map);
        NavigationServer3D.RegionSetNavigationMesh(region, mesh);
        await Frames(3);
        var cap = new NavigationPathQueryParameters3D().PathSearchMaxPolygons;
        Check(mesh.GetPolygonCount() > 2 * cap, $"the field has more than twice the default search cap's polygons ({mesh.GetPolygonCount()} against {cap})");
        var from = origin + new Vector3((wall - 2.5f) * square, 0, 2.5f * square);
        var to = origin + new Vector3((wall + 3.5f) * square, 0, 2.5f * square);
        var capped = NavigationServer3D.MapGetPath(navigation.Map, from, to, true);
        var cappedEnd = capped.Length > 0 ? capped[^1] : from;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var route = navigation.FindRoute(from, to, 0.05f);
        var searchMs = watch.Elapsed.TotalMilliseconds;
        var bounded = navigation.FindRoute(from, to, 0.05f, maxLengthM: 2.0f);
        var wayRound = 2 * (count - gap) * square;
        GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"ROOM_NAVIGATION_MEASURED beyond the search cap: {mesh.GetPolygonCount()} polygons; start and goal {PlanarDistance(from, to):0.00} m apart across the wall; " +
            $"the default query ends {PlanarDistance(cappedEnd, to):0.00} m from the goal; the room's route {(route.Reaches ? "reaches" : "does not reach")} it, {route.LengthM:0.00} m long " +
            $"(the way round about {wayRound:0.0} m), in {searchMs:0.0} ms; held to a 2 m search it {(bounded.Reaches ? "reaches" : "does not reach")}"));
        Check(PlanarDistance(cappedEnd, to) > 0.1f, "the default query stops short of the goal on this field (the field is beyond its cap)");
        Check(route.Reaches && route.LengthM > wayRound - 0.5f,
            $"the room's route reaches the goal round the far end of the wall ({route.LengthM:0.00} m), beyond the default search cap");
        Check(!bounded.Reaches, "a route held to a search distance shorter than the way round does not reach");
        NavigationServer3D.FreeRid(region);
        await Frames(2);
    }

    private void Finish()
    {
        GD.Print($"NATIVE_ROOM_NAVIGATION: {_checks - _failures}/{_checks} checks passed; companion routes round the big box in the test room through the command host");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static float PlanarDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>Planar distance from a point to a square footprint (zero inside it).</summary>
    private static float ClearanceFromSquare(Vector3 point, Vector3 centre, float half) =>
        new Vector2(Mathf.Max(Mathf.Abs(point.X - centre.X) - half, 0), Mathf.Max(Mathf.Abs(point.Z - centre.Z) - half, 0)).Length();

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(bool condition, string label)
    {
        _checks++;
        if (condition) return;
        _failures++;
        GD.PushError("Room navigation: " + label);
    }
}
