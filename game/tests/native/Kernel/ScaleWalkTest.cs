using Godot;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Navigation;
using EnFractal.Native.Room;

namespace EnFractal.Tests.Kernel;

/// <summary>
/// SCALE EXPERIMENT (run2/scale-experiment; not for merging as is): boots an exported landscape headless and measures what the
/// founder's question needs: the load to ROOM_WORLD_READY, the Gubble's navigation bake, memory, what was built, and a walk on
/// foot across the island (the player steering along the Gubble's navigation mesh as a player would, no sprint), then a run back
/// (sprint). Run: godot --headless --path game --fixed-fps 60 res://tests/native_scale_walk.tscn -- --landscape=DIR
/// Prints SCALE_* lines; never fails a build (it reports).
/// </summary>
public partial class ScaleWalkTest : Node3D
{
    private RoomWorld _world = null!;

    public override async void _Ready()
    {
        var directory = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--landscape=", StringComparison.Ordinal))?["--landscape=".Length..];
        if (string.IsNullOrEmpty(directory)) { GD.PushError("pass -- --landscape=DIR"); GetTree().Quit(1); return; }
        var maxWalkS = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--max-walk-s=", StringComparison.Ordinal)) is { } m
            ? float.Parse(m["--max-walk-s=".Length..], System.Globalization.CultureInfo.InvariantCulture) : 600f;
        CommandHost.SaveRoot = "user://tests/scale_walk/saves";
        var clock = Stopwatch.StartNew();
        _world = GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
        _world.RoomDirectory = directory.Replace('\\', '/');
        AddChild(_world);
        while (!_world.WorldReady && _world.LoadError.Length == 0 && clock.Elapsed.TotalSeconds < 900) await Frames(1);
        var loadS = clock.Elapsed.TotalSeconds;
        if (!_world.WorldReady) { GD.Print($"SCALE_LOAD failed: {_world.LoadError}"); GetTree().Quit(2); return; }
        var navigation = _world.GetNode<RoomNavigation>("RoomNavigation");
        for (var i = 0; i < 120 && !navigation.IsReady; i++) await Frames(1);
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var room = _world.Room;
        var instances = room.Instanced.Sum(s => s.Count);
        Print($"SCALE_LOAD room={room.RoomId} to_world_ready_s={loadS:0.0} shell_parts={room.Shell.Count} objects={room.Objects.Count} " +
              $"instanced_plants={instances} in {room.Instanced.Count} sets bounds={room.Bounds.Size.X:0.#}x{room.Bounds.Size.Z:0.#} m");
        Print($"SCALE_NAV ready={navigation.IsReady} bake_ms={navigation.LastBakeMs:0} polygons={navigation.PolygonCount} tiles={navigation.TileCount}");
        Print($"SCALE_MEMORY working_set_mb={process.WorkingSet64 / 1048576} peak_working_set_mb={process.PeakWorkingSet64 / 1048576} " +
              $"godot_static_mb={OS.GetStaticMemoryUsage() / 1048576} nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)}");
        // Settle, then measure the headless frame cost (physics and scripts, no drawing) standing at the spawn.
        await Frames(60);
        var idle = await TickCost(120);
        Print($"SCALE_TICK idle_ms_mean={idle.Mean:0.00} idle_ms_max={idle.Max:0.00}");
        // Across the island on foot: from the spawn to the wash-ashore point farthest from it (else the farthest coast point).
        var player = _world.Player;
        player.ReadKeyboard = false;
        var start = player.GlobalPosition;
        if (OS.GetCmdlineUserArgs().Contains("--debug-nav"))
        {
            var regions = NavigationServer3D.MapGetRegions(navigation.Map);
            Print($"SCALE_NAV_DEBUG regions={regions.Count} closest={NavigationServer3D.MapGetClosestPoint(navigation.Map, start)} owner={NavigationServer3D.MapGetClosestPointOwner(navigation.Map, start)}");
            Print($"SCALE_NAV_DEBUG connections total={regions.Sum(r => NavigationServer3D.RegionGetConnectionsCount(r))} zero={regions.Count(r => NavigationServer3D.RegionGetConnectionsCount(r) == 0)}");
            if (regions.Count <= 5) foreach (var region in regions)
                Print($"SCALE_NAV_DEBUG region {region} connections={NavigationServer3D.RegionGetConnectionsCount(region)} enabled={NavigationServer3D.RegionGetEnabled(region)}");
            foreach (var d in new[] { 0.5f, 2f, 5f, 10f })
            {
                var r = navigation.FindRoute(start, navigation.ClosestPoint(start + new Vector3(d, 0, d)), 0.1f);
                Print($"SCALE_NAV_DEBUG route +{d} m: reaches={r.Reaches} points={r.Points.Length} length={r.LengthM:0.00}");
            }
        }
        // Across the island: toward coast points (a little inland) farthest from the spawn, the farthest place a route gets to.
        var far = start;
        var route = RoomNavigation.Route.None;
        if (room.Sea is { Coast.Length: > 0 } sea)
        {
            var centre = sea.Coast.Aggregate(Vector2.Zero, (a, p) => a + p) / sea.Coast.Length;
            var space = GetWorld3D().DirectSpaceState;
            var candidates = sea.Coast.Where((_, i) => i % Math.Max(1, sea.Coast.Length / 64) == 0)
                .Select(p => p + (centre - p) * 0.15f).OrderByDescending(p => (p - new Vector2(start.X, start.Z)).Length()).Take(24);
            foreach (var p in candidates)
            {
                var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(p.X, room.Bounds.End.Y + 1, p.Y), new Vector3(p.X, -5, p.Y), RoomBuilder.WorldLayer));
                if (hit.Count == 0) continue;
                var ground = navigation.ClosestPoint((Vector3)hit["position"]);
                var tried = navigation.FindRoute(start, ground, 0.1f);
                if (OS.GetCmdlineUserArgs().Contains("--debug-nav"))
                    Print($"SCALE_NAV_DEBUG candidate {p} hit={(Vector3)hit["position"]} ground={ground} points={tried.Points.Length} reaches={tried.Reaches} end={(tried.Points.Length > 0 ? tried.Points[^1] : Vector3.Zero)}");
                // As far across as a body can walk: where the route toward that coast point ends (water or cliffs may stop it short).
                if (tried.Points.Length == 0) continue;
                var end = tried.Points[^1];
                if (Planar(end - start).Length() <= Planar(far - start).Length()) continue;
                far = end;
                route = tried;
            }
        }
        Print($"SCALE_ROUTE from=({start.X:0.##},{start.Z:0.##}) to=({far.X:0.##},{far.Z:0.##}) straight_m={Planar(far - start).Length():0.0} " +
              $"route_m={route.LengthM:0.0} reaches={route.Reaches} corners={route.Points.Length}");
        await Walk("walk across", navigation, far, sprint: false, maxWalkS);
        await Walk("run back", navigation, start, sprint: true, maxWalkS);
        // A carried thing put down: one movable object moves 30 cm, and the navigation mesh is baked again (as after a carry).
        var movable = _world.Built.GetNodeOrNull<Node3D>("Objects")?.GetChildren().OfType<StaticBody3D>()
            .FirstOrDefault(b => b.HasMeta("movable") && b.GetMeta("movable").AsBool());
        if (movable != null)
        {
            movable.GlobalPosition += new Vector3(0.3f, 0, 0);
            await Frames(2);
            navigation.Bake();
            Print($"SCALE_REBAKE after moving {movable.Name}: {navigation.LastBakeMs:0} ms, {navigation.PolygonCount} polygons");
        }
        GetTree().Quit(0);
    }

    private async Task Walk(string name, RoomNavigation navigation, Vector3 goal, bool sprint, float maxSeconds)
    {
        var body = _world.Player;
        var from = body.GlobalPosition;
        var travelled = 0f;
        var last = from;
        var route = RoomNavigation.Route.None;
        var index = 1;
        var age = int.MaxValue;
        var frames = 0;
        var maxFrames = (int)(maxSeconds * Engine.PhysicsTicksPerSecond);
        var wall = Stopwatch.StartNew();
        var stuck = 0;
        var arrived = false;
        var replans = 0;
        while (frames < maxFrames)
        {
            var here = body.GlobalPosition;
            var remaining = Planar(goal - here).Length();
            if (remaining <= 0.1f) { arrived = true; break; }
            if (++age > 60 || route.IsEmpty)
            {
                route = navigation.FindRoute(here, goal, 0.1f);
                index = 1;
                age = 0;
                replans++;
            }
            while (index < route.Points.Length - 1 && Planar(route.Points[index] - here).Length() < 0.03f) index++;
            var corner = route.IsEmpty || index >= route.Points.Length ? goal : route.Points[index];
            var to = corner - here;
            if (new Vector2(to.X, to.Z).Length() > 0.001f) body.Rotation = new Vector3(0, Mathf.Atan2(-to.X, -to.Z), 0);
            body.SetControlInput(new Vector2(0, remaining > 0.08f ? 1 : 0.4f), sprint: sprint && remaining > 0.3f);
            await Frames(1);
            frames++;
            var now = body.GlobalPosition;
            var step = Planar(now - last).Length();
            travelled += step;
            stuck = step < 0.0005f ? stuck + 1 : 0;
            last = now;
            if (stuck > 600) break;
        }
        body.SetControlInput(Vector2.Zero);
        var simS = frames / (double)Engine.PhysicsTicksPerSecond;
        Print($"SCALE_WALK {name}: arrived={arrived} sim_s={simS:0.0} travelled_m={travelled:0.0} straight_m={Planar(goal - from).Length():0.0} " +
              $"left_m={Planar(goal - body.GlobalPosition).Length():0.0} mean_mps={(simS > 0 ? travelled / simS : 0):0.000} replans={replans} " +
              $"wall_s={wall.Elapsed.TotalSeconds:0.0} tick_ms_mean={(frames > 0 ? wall.Elapsed.TotalMilliseconds / frames : 0):0.00} stuck={(stuck > 600)}");
        await Frames(30);
    }

    private async Task<(double Mean, double Max)> TickCost(int frames)
    {
        var times = new double[frames];
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < frames; i++)
        {
            var before = watch.Elapsed.TotalMilliseconds;
            await Frames(1);
            times[i] = watch.Elapsed.TotalMilliseconds - before;
        }
        return (times.Average(), times.Max());
    }

    private static Vector2 Planar(Vector3 v) => new(v.X, v.Z);
    private static void Print(string line) => GD.Print(line);

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
}
