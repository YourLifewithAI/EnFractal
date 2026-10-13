using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace EnFractal.Tests.Look;

/// <summary>
/// The probe's "--parts" (S12): what the per-frame and periodic jobs of the HUD, the command host and the bodies cost one at a
/// time, called directly on the booted island: the bytes each call allocates, the Godot wrappers it leaves for the .NET finalizer
/// (FrameGarbage), and its longest time. Then the frame as a whole with no collection allowed (GC.TryStartNoGCRegion), which gives
/// the bytes and the finalizer's wrappers a frame exactly.
/// </summary>
public partial class IslandFrameProbe
{
    private async Task MeasureParts()
    {
        var hud = _world.GetNodeOrNull<EnFractal.Native.RoomHud>("RoomHud")!;
        var host = EnFractal.Native.Kernel.CommandHost.Of(_world)!;
        var ids = host.Room.Shell.Select(p => p.Id).Concat(host.Room.Objects.Select(o => o.Id)).ToList();
        var parts = new List<(string Name, Action Call)>
        {
            ("RoomHud._Process", () => hud._Process(1 / 60.0)),
            ("RoomHud.RefreshKeyText", Private(hud, "RefreshKeyText")),
            ("RoomHud.UpdateFocus (a query)", Private(hud, "UpdateFocus", 1.0)),
            ("RoomHud.UpdateAskTag (a query)", Private(hud, "UpdateAskTag", 1.0)),
            ("RoomHud.UpdateChains", Private(hud, "UpdateChains")),
            ("RoomHud.UpdateDusk", Private(hud, "UpdateDusk", 1 / 60.0)),
            ("RoomHud.TickMagicPanel", Private(hud, "TickMagicPanel", 1 / 60.0)),
            ("RoomHud.TickGubbleView", Private(hud, "TickGubbleView")),
            ("RoomHud.ClockText", () => hud.ClockText()),
            ("CommandHost._Process", () => host._Process(1 / 60.0)),
            ("CommandHost.Entities", () => host.Entities()),
            ("the authority's snapshot (1 call)", () => { using var snapshot = host.Authority.Call(SnapshotName, EnFractal.Native.Kernel.CommandHost.PlayerPrincipal); }),
            ("CommandHost.EntityRevision (every room entity)", () => { foreach (var id in ids) host.EntityRevision(id); }),
            ("CommandHost.PlayerFocus", () => host.PlayerFocus()),
            ("CommandHost.SightSweep (a whole sweep, in one call)", WholeSweep(host)),
            ("CommandHost.AdvanceNature (a step)", () => host.AdvanceNature(EnFractal.Native.Kernel.NatureLaws.StepS)),
            ("CommandHost.AdvanceNature (a tick)", () => host.AdvanceNature(1 / 60.0)),
            ("the pond survey (PondSurvey.Survey, once a boot)", () => EnFractal.Native.Look.Fauna.PondSurvey.Survey(_world.Built, EnFractal.Native.Look.Fauna.PondSurvey.RayProbe(_world.GetWorld3D().DirectSpaceState))),
            ("Player._PhysicsProcess", () => _world.Player._PhysicsProcess(1 / 60.0)),
            ("Player.SampleWater", Private(_world.Player, "SampleWater", 1 / 60f)),
            ("Companion._PhysicsProcess", () => _world.Companion._PhysicsProcess(1 / 60.0)),
            ("Companion._Process", () => _world.Companion._Process(1 / 60.0)),
        };
        foreach (var (name, call) in parts)
        {
            call();
            var casts = EnFractal.Native.Kernel.SightRay.Casts;
            var cost = FrameGarbage.Measure(call, 12);
            casts = EnFractal.Native.Kernel.SightRay.Casts - casts;
            if (casts > 0) GD.Print($"ISLAND_PROBE part {name}: {casts / 12.0:0} sight rays a call");
            GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"ISLAND_PROBE part {name}: {cost.Bytes / 1024.0:0.0} KB, {cost.Wrappers:0.#} Godot wrappers left for the finalizer, longest {cost.LongestMs:0.00} ms a call"));
            await Frame();
        }
        // The whole frame, process and physics, with no collection allowed: its bytes and its wrappers exactly.
        const int frames = 120;
        var window = await FrameGarbage.MeasureFrames(frames, Frame);
        GD.Print(window is { } w
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"ISLAND_PROBE a frame, all threads, no collection in {frames} frames: {w.Bytes / 1024.0 / frames:0.0} KB allocated, {w.Wrappers / (double)frames:0.0} Godot wrappers left for the finalizer")
            : "ISLAND_PROBE a frame: the no-collection window could not be held (a collection came anyway)");
        // Then without each of the world's nodes in turn: what each one's frames allocate and leave for the finalizer.
        var nodes = _world.GetChildren().Concat(_world.Look.GetChildren()).OfType<Node>().Where(n => n != _world.Built && n.Name != "Environment").ToList();
        foreach (var node in nodes)
        {
            var mode = node.ProcessMode;
            node.ProcessMode = ProcessModeEnum.Disabled;
            for (var i = 0; i < 5; i++) await Frame();
            var without = await FrameGarbage.MeasureFrames(frames / 2, Frame);
            node.ProcessMode = mode;
            for (var i = 0; i < 5; i++) await Frame();
            if (without is { } o)
                GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"ISLAND_PROBE a frame without {node.Name}: {o.Bytes / 1024.0 / (frames / 2):0.0} KB allocated, {o.Wrappers / (double)(frames / 2):0.0} Godot wrappers left"));
        }
    }

    private static readonly StringName SnapshotName = "snapshot";

    /// <summary>A sweep begun and finished at once: all its work, as the sweep did in one tick before S12 spread it.</summary>
    private static Action WholeSweep(EnFractal.Native.Kernel.CommandHost host)
    {
        var sweep = Private(host, "SightSweep", host.TeamSightIntervalS);
        return () =>
        {
            sweep();
            host.FinishSweep();
        };
    }

    private static Action Private(object target, string method, params object[] arguments)
    {
        var info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                   ?? throw new MissingMethodException(target.GetType().Name, method);
        return () => info.Invoke(target, arguments);
    }
}

/// <summary>
/// The Godot wrappers a piece of code leaves for the .NET finalizer: every StringName, NodePath, Godot collection, Variant and
/// object wrapper registers with GodotSharp's DisposablesTracker when made and unregisters when disposed; one left registered
/// waits for the finalizer thread, which then frees its native side away from the main thread.
/// </summary>
public static class FrameGarbage
{
    private static readonly Func<int> Tracked = TrackedCounter();

    private static Func<int> TrackedCounter()
    {
        var tracker = typeof(GodotObject).Assembly.GetType("Godot.DisposablesTracker") ?? throw new InvalidOperationException("no DisposablesTracker");
        var counted = new[] { "GodotObjectInstances", "OtherInstances" }
            .Select(name => tracker.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null)
                            ?? throw new InvalidOperationException("no DisposablesTracker." + name))
            .Select(dictionary => (Dictionary: dictionary, Count: dictionary.GetType().GetProperty("Count")!))
            .ToArray();
        return () => counted.Sum(c => (int)c.Count.GetValue(c.Dictionary)!);
    }

    /// <summary>Wrappers registered with the tracker now (live, or waiting for the finalizer).</summary>
    public static int TrackedNow => Tracked();

    /// <summary>Settle the finalizer: every wrapper that is garbage now is finalized and unregistered.</summary>
    public static void Settle()
    {
        for (var i = 0; i < 2; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>A call's mean bytes allocated on this thread, its mean wrappers left registered, and its longest time, over repeats.</summary>
    public static (double Bytes, double Wrappers, double LongestMs) Measure(Action call, int repeats)
    {
        Settle();
        long bytes = 0;
        var longest = 0.0;
        var before = TrackedNow;
        for (var i = 0; i < repeats; i++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();
            call();
            longest = Math.Max(longest, clock.Elapsed.TotalMilliseconds);
            bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
        }
        // Wrappers still registered after the calls that were not before: made and never disposed. (Collections in between could
        // finalize some; the settle before makes that rare, and a count that is already 0 cannot hide anything.)
        var left = TrackedNow - before;
        return (bytes / (double)repeats, left / (double)repeats, longest);
    }

    /// <summary>Frames run with no collection allowed: all threads' bytes and the wrappers left registered. Null if a collection came.</summary>
    public static async Task<(long Bytes, int Wrappers)?> MeasureFrames(int frames, Func<Task> frame)
    {
        Settle();
        // Starting the region collects first; the count and the bytes are taken after it.
        if (!GC.TryStartNoGCRegion(200L * 1024 * 1024)) return null;
        var before = TrackedNow;
        var allocated = GC.GetTotalAllocatedBytes(true);
        var collections = GC.CollectionCount(0);
        try
        {
            for (var i = 0; i < frames; i++) await frame();
        }
        finally
        {
            if (System.Runtime.GCSettings.LatencyMode == System.Runtime.GCLatencyMode.NoGCRegion) GC.EndNoGCRegion();
        }
        if (GC.CollectionCount(0) != collections) return null;
        return (GC.GetTotalAllocatedBytes(true) - allocated, TrackedNow - before);
    }
}
