using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Room;
using EnFractal.Native.Seams;

namespace EnFractal.Tests;

/// <summary>
/// The body's side of the stamina ledger (docs/runs/RUN-2-MECHANICS.md, packet M5; Seams/StaminaLedger.cs): what it reports each
/// tick, in what order, and what it does with the gate: the grip fails on a climb, deep water carries it to shore, land is slower
/// until the bar refills. The ledger is the fixture's own arithmetic (contracts/examples/ledger/stamina_ledger.json), proved against
/// the fixture's ten cases first, so the body meets exactly the contract the kernel meets.
/// </summary>
public partial class SmallAvatarPhysicsTest
{
    /// <summary>A ledger that records what the body reports and holds whatever gate the test sets.</summary>
    private class ScriptedLedger : IStaminaLedger
    {
        public StaminaGate GateValue = StaminaGate.Free;
        public BodyExertion Last;
        public int Reports;
        public int Breaches;
        public double SprintTotal, DeepTotal, DiveTotal;
        public Action<BodyExertion>? OnReport;

        public virtual void Report(BodyExertion exertion)
        {
            Last = exertion;
            Reports++;
            SprintTotal += exertion.SprintS;
            DeepTotal += exertion.DeepWaterS;
            DiveTotal += exertion.DiveS;
            // At most one of the three, none below 0, none past a tick (0.05 s is the body's longest).
            var kinds = (exertion.SprintS > 0 ? 1 : 0) + (exertion.DeepWaterS > 0 ? 1 : 0) + (exertion.DiveS > 0 ? 1 : 0);
            if (kinds > 1 || exertion.SprintS < 0 || exertion.DeepWaterS < 0 || exertion.DiveS < 0 || Mathf.Max(exertion.SprintS, Mathf.Max(exertion.DeepWaterS, exertion.DiveS)) > 0.0501f) Breaches++;
            OnReport?.Invoke(exertion);
        }

        public StaminaGate Gate => GateValue;
    }

    /// <summary>The ledger's rule as the README and the fixture state it (the kernel's side is Kernel/CommandHostModes.cs).</summary>
    private sealed class ModelLedger : ScriptedLedger
    {
        /// <summary>FirstBar: the top row (0: the whole bar, a bar with one row). NoRunFraction: of it, refilled before the run comes back.</summary>
        public double Stamina, Max, FirstBar, Running = 0.02, Refill = 0.1, NoRunFraction = 1;
        public double? DeepRate = 0.02, DiveRate = 0.04;
        public bool NoRun;
        private BodyExertion _pending;

        public override void Report(BodyExertion exertion)
        {
            base.Report(exertion);
            _pending = exertion;
            Refresh();
        }

        public void Refresh() => GateValue = new StaminaGate(Stamina <= 1e-6, NoRun);

        /// <summary>The kernel's VitalsTick: drain what was reported (never below empty), refill on standable ground for the seconds not run.</summary>
        public void Tick(float seconds, bool safelyGrounded)
        {
            var e = _pending;
            _pending = BodyExertion.None;
            double Within(float s) => Math.Min(Math.Max(0, s), seconds);
            var drain = Within(e.SprintS) * Running + Within(e.DeepWaterS) * (DeepRate ?? 0) + Within(e.DiveS) * (DiveRate ?? 0);
            if (drain > 0)
            {
                Stamina = Math.Max(0, Stamina - drain);
                if (Stamina <= 0) NoRun = true;
            }
            var rest = Math.Max(0, seconds - Within(e.SprintS));
            if (safelyGrounded && Stamina < Max) Stamina = Math.Min(Max, Stamina + Refill * rest);
            if (NoRun && Stamina >= (FirstBar > 0 ? Math.Min(Max, FirstBar) : Max) * NoRunFraction - 1e-6 && Stamina > 0) NoRun = false;
            Refresh();
        }
    }

    private async Task TestExertion()
    {
        TestLedgerModelHoldsTheFixture();
        await TestReportsAndOrder();
        await TestEmptyBarStopsTheRun();
        await TestEmptyBarDropsAClimber();
        await TestEmptyBarInDeepWater();
    }

    private static string LedgerFixturePath => ProjectSettings.GlobalizePath("res://") + "../contracts/examples/ledger/stamina_ledger.json";

    /// <summary>The stand-in ledger gives the fixture's answers in all ten cases: the body then meets the contract through it.</summary>
    private void TestLedgerModelHoldsTheFixture()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(LedgerFixturePath));
        var root = fixture.RootElement;
        var stamina = root.GetProperty("stamina");
        var tolerance = root.GetProperty("tolerance_m").GetDouble();
        var cases = root.GetProperty("cases").EnumerateArray().ToList();
        var held = 0;
        foreach (var item in cases)
        {
            var omit = item.TryGetProperty("omit", out var o) ? o.EnumerateArray().Select(x => x.GetString()!).ToHashSet() : new HashSet<string>();
            double? Rate(string name) => omit.Contains(name) ? null : stamina.GetProperty(name).GetDouble();
            var model = new ModelLedger
            {
                Max = item.GetProperty("max_stamina_m").GetDouble(), FirstBar = item.GetProperty("first_bar_m").GetDouble(),
                Stamina = item.GetProperty("stamina_m").GetDouble(), NoRun = item.GetProperty("no_run").GetBoolean(),
                Running = stamina.GetProperty("running_m_per_s").GetDouble(), Refill = stamina.GetProperty("refill_m_per_s").GetDouble(),
                NoRunFraction = stamina.GetProperty("empty_no_run_until_fraction").GetDouble(),
                DeepRate = Rate("deep_water_m_per_s"), DiveRate = Rate("dive_m_per_s"),
            };
            var ok = true;
            foreach (var step in item.GetProperty("steps").EnumerateArray())
            {
                model.Report(new BodyExertion((float)step.GetProperty("sprint_s").GetDouble(), (float)step.GetProperty("deep_water_s").GetDouble(), (float)step.GetProperty("dive_s").GetDouble()));
                model.Tick((float)step.GetProperty("seconds").GetDouble(), step.GetProperty("safely_grounded").GetBoolean());
                var expect = step.GetProperty("expect");
                var gate = model.Gate;
                ok &= Math.Abs(model.Stamina - expect.GetProperty("stamina_m").GetDouble()) <= tolerance + 1e-6 && gate.Empty == expect.GetProperty("empty").GetBoolean() &&
                      gate.NoRun == expect.GetProperty("no_run").GetBoolean();
            }
            if (ok) held++;
            else Check(false, "the stand-in ledger gives the fixture's answers: " + item.GetProperty("name").GetString());
        }
        Check(held == cases.Count && cases.Count == 10, $"the stand-in ledger gives the fixture's answers in all {cases.Count} cases, so the body meets the contract through it");
    }

    private const float Tick = 1f / 60;

    /// <summary>A fixture floor with a body standing on it, far from the other suites.</summary>
    private (Node3D Fixture, SmallPlayerController Body) ExertionFixture(Vector3 origin, float length = 6, string name = "ExertionFixture")
    {
        var fixture = new Node3D { Name = name, Position = origin };
        AddChild(fixture);
        var floor = new StaticBody3D { Position = new Vector3(0, -0.05f, 2 - length * 0.5f + 1), CollisionLayer = 1, CollisionMask = 0 };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(6, 0.1f, length) } });
        fixture.AddChild(floor);
        var body = new SmallPlayerController { Name = name + "Body", ReadKeyboard = false, Position = new Vector3(0, 0.003f, 2) };
        fixture.AddChild(body);
        return (fixture, body);
    }

    /// <summary>The body reports what it did once a tick, before the tick, and only what is true.</summary>
    private async Task TestReportsAndOrder()
    {
        var (fixture, body) = ExertionFixture(new Vector3(800, 0, 800));
        var wall = new StaticBody3D { Position = new Vector3(0, 0.2f, -1.5f), CollisionLayer = 1, CollisionMask = 0 };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.0f, 0.4f, 0.1f) } });
        fixture.AddChild(wall);
        await Frames(5);

        var ledger = new ScriptedLedger();
        var log = new List<string>();
        ledger.OnReport = _ => log.Add("R");
        body.VitalsTick = (_, _) => log.Add("T");
        body.StaminaLedger = ledger;
        await Frames(30);
        var inOrder = log.Count >= 50 && log.Count % 2 == 0;
        for (var i = 0; i + 1 < log.Count; i += 2) inOrder &= log[i] == "R" && log[i + 1] == "T";
        Check(inOrder && ledger.Reports >= 25 && ledger.Reports == log.Count / 2, $"the body reports once a physics tick, before VitalsTick: {log.Count / 2} ticks, each report then each tick");
        Check(ledger.Last.SprintS == 0 && ledger.Last.DeepWaterS == 0 && ledger.Last.DiveS == 0, "standing still costs nothing beyond climbing");

        // Sprint held, no step taken: no running to pay for.
        body.SetControlInput(Vector2.Zero, sprint: true);
        await Frames(30);
        Check(ledger.SprintTotal == 0, "holding the run key while standing still reports no running");

        // Running: a second of sprint seconds, whole ticks of them.
        body.SetControlInput(new Vector2(0, 1), sprint: true);
        await Frames(10);
        ledger.SprintTotal = 0;
        var before = ledger.Reports;
        var sprintTicks = 0;
        ledger.OnReport = e => { if (e.SprintS > 0) sprintTicks++; };
        await Frames(60);
        var ticks = ledger.Reports - before;
        Report(ticks == 60 && sprintTicks >= 58 && Mathf.Abs((float)ledger.SprintTotal - sprintTicks * Tick) < 0.002f && ledger.Last.SprintS <= 0.0501f,
            $"running reports its seconds, the whole tick each ({ledger.SprintTotal:0.000} s over {ticks} ticks, {sprintTicks} of them running)");
        Check(ledger.Breaches == 0 && ledger.DeepTotal == 0 && ledger.DiveTotal == 0, "never two kinds at once, never more than the tick, never a negative");

        // Walking is free.
        body.SetControlInput(new Vector2(0, 1), sprint: false);
        await Frames(20);
        ledger.SprintTotal = 0;
        await Frames(40);
        Check(ledger.SprintTotal == 0, "walking reports no running");

        // Pushing into a wall at a run goes nowhere, so it is not running (a climbable face would be grabbed: the body cannot climb here).
        body.CanClimb = false;
        body.SetControlInput(new Vector2(0, 1), sprint: true);
        for (var i = 0; i < 400 && !body.IsBlocked; i++) await Frames(1);
        await Frames(15);
        ledger.SprintTotal = 0;
        await Frames(30);
        Check(body.IsBlocked && ledger.SprintTotal == 0, $"running into a wall reports no running ({ledger.SprintTotal:0.000} s while blocked)");
        body.CanClimb = true;

        // A tumble holds control: nothing is exerted.
        body.TryTeleportTo(fixture.GlobalPosition + new Vector3(0, 0.003f, 2));
        await Frames(3);
        body.HoldControl(0.5f);
        body.SetControlInput(new Vector2(0, 1), sprint: true);
        ledger.SprintTotal = 0;
        await Frames(20);
        Check(body.ControlHeld && ledger.SprintTotal == 0, "a body whose control is held (a tumble) exerts nothing");
        body.ReleaseControl();
        body.SetControlInput(Vector2.Zero);

        // Taking the ledger off frees the body again; the gate it held is gone with it.
        ledger.GateValue = new StaminaGate(true, true);
        await Frames(3);
        Check(body.Gate.Empty && body.Gate.NoRun, "the body holds the gate the ledger gave after its tick");
        body.StaminaLedger = null;
        Check(body.Gate == StaminaGate.Free, "with no ledger bound, nothing is gated");
        var reports = ledger.Reports;
        await Frames(5);
        Check(ledger.Reports == reports, "an unbound ledger hears nothing more");

        // A floating body (the Gubble) neither exerts nor is gated.
        var companionLedger = new ScriptedLedger { GateValue = new StaminaGate(true, true) };
        _companion.StaminaLedger = companionLedger;
        _companion.VitalsTick = (_, _) => { };
        await Frames(20);
        Check(companionLedger.Reports == 0 && _companion.Floats && _companion.WalkSpeedNowMps == _companion.WalkSpeedMps,
            "a floating body reports nothing and is never slowed");
        _companion.StaminaLedger = null;
        _companion.VitalsTick = null;

        body.VitalsTick = null;
        fixture.QueueFree();
        await Frames(2);
    }

    /// <summary>
    /// The empty bar takes the run away, never the walk, until the first bar is full again (the founder, 15 October); the ledger
    /// meets the body through its own reports.
    /// </summary>
    private async Task TestEmptyBarStopsTheRun()
    {
        var (fixture, body) = ExertionFixture(new Vector3(800, 0, 900), 90, "EmptyBarFixture");
        await Frames(5);
        // The starting bar, a full one: 0.4 m, run down at 0.02 m a second.
        var ledger = new ModelLedger { Max = 0.4, Stamina = 0.4 };
        ledger.Refresh();
        body.VitalsTick = (dt, grounded) => ledger.Tick(dt, grounded);
        body.StaminaLedger = ledger;
        await Frames(3);

        // Paces measured on the floor: the walk, and Shift held.
        async Task<float> PaceFor(bool sprint, int frames = 40)
        {
            body.TryTeleportTo(fixture.GlobalPosition + new Vector3(0, 0.003f, 2));
            await Frames(2);
            body.SetControlInput(new Vector2(0, 1), sprint: sprint);
            await Frames(frames);
            var from = body.GlobalPosition;
            await Frames(20);
            var pace = PlanarDistance(body.GlobalPosition, from) * 3;
            body.SetControlInput(Vector2.Zero);
            await Frames(10);
            return pace;
        }
        var walk = await PaceFor(false);
        Report(Mathf.Abs(walk - 0.32f) < 0.01f && Mathf.IsEqualApprox(body.WalkSpeedNowMps, 0.32f), $"a full bar walks at the base pace ({walk:0.000} m/s)");

        // A full starting bar lasts 20 s of running (the fixture's first case, here by the body's own reports).
        ledger.Stamina = 0.4;
        ledger.NoRun = false;
        ledger.Refresh();
        body.TryTeleportTo(fixture.GlobalPosition + new Vector3(0, 0.003f, 2));
        await Frames(2);
        body.SetControlInput(new Vector2(0, 1), sprint: true);
        var frames = 0;
        for (; frames < 3600 && !ledger.Gate.Empty; frames++) await Frames(1);
        var seconds = frames / 60f;
        Report(seconds > 19.5f && seconds < 21.0f && ledger.NoRun && ledger.Stamina <= 1e-6,
            $"a full starting bar lasts about 20 s of running, by what the body reports ({seconds:0.0} s, {ledger.SprintTotal:0.0} s of it running)");

        // Empty: Shift held no longer runs. The body walks at its whole pace, reports no running, and the walk refills the bar.
        await Frames(15);
        var sprintBefore = ledger.SprintTotal;
        var heldFrom = body.GlobalPosition;
        await Frames(30);
        var heldPace = PlanarDistance(body.GlobalPosition, heldFrom) * 2;
        var state = body.LastBodyState;
        Report(Mathf.Abs(heldPace - 0.32f) < 0.02f && body.Gate.NoRun && ledger.SprintTotal == sprintBefore && ledger.Stamina > 0 &&
               Mathf.IsEqualApprox(state.WalkSpeedMps, 0.32f),
            $"an empty bar takes the run away, never the walk: Shift held walks at {heldPace:0.000} m/s, reports no running and refills ({ledger.Stamina:0.000} m)");

        // The run comes back when the first bar is full again: 0.4 m at 0.10 m a second, about 4 s after the running stopped.
        var walked = 45;
        for (; walked < 600 && ledger.NoRun; walked++) await Frames(1);
        Report(walked >= 225 && walked <= 255 && !ledger.NoRun && ledger.Stamina >= 0.4 - 1e-6,
            $"the run comes back when the first bar is full again ({walked / 60f:0.00} s after the running stopped)");
        var runAgain = await PaceFor(true);
        Report(Mathf.Abs(runAgain - 0.96f) < 0.03f && !ledger.NoRun, $"then Shift runs at the whole pace again ({runAgain:0.000} m/s)");

        // The lock takes the run, never the knack: Quick feet at full rank keeps its walk and its run's pace.
        body.SetControlInput(Vector2.Zero);
        body.Knacks = new BodyKnacks { WalkRunFactor = 1.2f };
        ledger.Stamina = 0;
        ledger.NoRun = true;
        ledger.Refresh();
        await Frames(2);
        Check(Mathf.IsEqualApprox(body.WalkSpeedNowMps, 0.32f * 1.2f) && Mathf.IsEqualApprox(body.RunSpeedNowMps, 0.96f * 1.2f) && body.Gate.NoRun,
            $"Quick feet with an empty bar: walk {body.WalkSpeedNowMps:0.000}, run {body.RunSpeedNowMps:0.000} m/s, the run held back by the gate");
        body.Knacks = BodyKnacks.None;

        body.VitalsTick = null;
        body.StaminaLedger = null;
        fixture.QueueFree();
        await Frames(2);
    }

    /// <summary>On a climb the grip fails when the bar is empty: the body falls from where it is, and does not grab again until the bar is back.</summary>
    private async Task TestEmptyBarDropsAClimber()
    {
        var (fixture, body) = ExertionFixture(new Vector3(800, 0, 1000), 6, "GripFixture");
        // A vertical face 60 cm tall, its west face 0.3 m east of the start.
        var face = new StaticBody3D { Position = new Vector3(0.45f, 0.3f, 2), CollisionLayer = 1, CollisionMask = 0 };
        face.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.3f, 0.6f, 0.6f) } });
        fixture.AddChild(face);
        body.Rotation = new Vector3(0, -Mathf.Pi * 0.5f, 0);
        await Frames(5);
        var ledger = new ScriptedLedger();
        body.StaminaLedger = ledger;

        // A bar with something in it: grab, climb.
        body.SetControlInput(new Vector2(0, 1));
        for (var i = 0; i < 400 && !(body.IsClimbing && body.GlobalPosition.Y - fixture.GlobalPosition.Y > 0.1f); i++) await Frames(1);
        var held = body.GlobalPosition;
        var grabs = body.Grabs;
        Check(body.IsClimbing && body.GripFailures == 0, $"with the bar not empty the body grabs and climbs (at {held.Y - fixture.GlobalPosition.Y:0.000} m)");

        // The bar runs empty on the face: the very next tick the grip fails and the body falls from where it is.
        ledger.GateValue = new StaminaGate(true, true);
        var failures = body.GripFailures;
        await Frames(3);
        Check(!body.IsClimbing && body.GripFailures == failures + 1 && body.ClimbReleases > 0,
            $"an empty bar on a climb lets go ({body.GripFailures} grip failure, {body.ClimbReleases} releases)");
        var letGo = body.GlobalPosition;
        await Frames(10);
        var falling = body.GlobalPosition.Y < letGo.Y - 0.003f && PlanarDistance(body.GlobalPosition, held) < 0.03f;
        var landed = false;
        for (var i = 0; i < 200 && !landed; i++)
        {
            await Frames(1);
            landed = body.IsOnFloor();
        }
        Report(falling && landed && body.GlobalPosition.Y - fixture.GlobalPosition.Y < 0.006f && PlanarDistance(body.GlobalPosition, held) < 0.06f,
            $"the body falls from where it is (let go at {letGo.Y - fixture.GlobalPosition.Y:0.000} m, {PlanarDistance(body.GlobalPosition, held):0.000} m from the face, landed at {body.GlobalPosition.Y - fixture.GlobalPosition.Y:0.000} m)");

        // Still empty: pushing into the face grabs nothing.
        await Frames(30);
        grabs = body.Grabs;
        body.SetControlInput(new Vector2(0, 1));
        await Frames(120);
        Check(body.Grabs == grabs && !body.IsClimbing, "with the bar empty a push into the face does not grab it");

        // The bar is back: the same push climbs again.
        ledger.GateValue = StaminaGate.Free;
        for (var i = 0; i < 300 && !body.IsClimbing; i++) await Frames(1);
        Check(body.IsClimbing && body.Grabs == grabs + 1, "with the bar back, the same push grabs again");
        body.SetControlInput(Vector2.Zero);
        body.StaminaLedger = null;
        fixture.QueueFree();
        await Frames(2);
    }

    /// <summary>In deep water an empty bar carries the swimmer to shore, never to a bank it cannot climb; a diver is not held under.</summary>
    private async Task TestEmptyBarInDeepWater()
    {
        var b = new Vector3(800, 0, 1100);
        const float surface = -0.02f;
        const float bed = -0.2f;
        // The swimming fixture's pond (TestSwimming): a shelving shore on the west, a vertical bank on the east, 18 cm deep.
        var parts = new List<Node3D>
        {
            Solid(b + new Vector3(-1.0f, -0.125f, 0), new BoxShape3D { Size = new Vector3(2.0f, 0.25f, 3.0f) }),
            Solid(b + new Vector3(2.2f, -0.125f, 0), new BoxShape3D { Size = new Vector3(2.0f, 0.25f, 3.0f) }),
            Solid(b + new Vector3(0.6f, -0.125f, -1.0f), new BoxShape3D { Size = new Vector3(1.2f, 0.25f, 1.0f) }),
            Solid(b + new Vector3(0.6f, -0.125f, 1.0f), new BoxShape3D { Size = new Vector3(1.2f, 0.25f, 1.0f) }),
            Solid(b + new Vector3(0.6f, -0.225f, 0), new BoxShape3D { Size = new Vector3(1.2f, 0.05f, 1.0f) }),
            Solid(b, Prism(0.5f, new Vector2(0, 0), new Vector2(0, bed), new Vector2(0.55f, bed))),
        };
        var sheet = new SurfaceTool();
        sheet.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var corner in new[] { new Vector2(0, -0.5f), new Vector2(1.2f, -0.5f), new Vector2(1.2f, 0.5f), new Vector2(0, -0.5f), new Vector2(1.2f, 0.5f), new Vector2(0, 0.5f) })
            sheet.AddVertex(new Vector3(corner.X, surface, corner.Y));
        var water = RoomWater.CreateCollider(new[] { ((Shape3D)sheet.Commit().CreateTrimeshShape(), Transform3D.Identity) });
        water.Position = b;
        AddChild(water);
        parts.Add(water);
        var body = new SmallPlayerController { Name = "ShoreBody", ReadKeyboard = false, Position = b + new Vector3(-0.5f, 0.003f, 0) };
        AddChild(body);
        await Frames(5);

        var ledger = new ScriptedLedger();
        body.StaminaLedger = ledger;
        var floatY = surface - body.SwimFloatDepthM;
        void Place(Vector3 at)
        {
            body.GlobalPosition = at;
            body.Velocity = Vector3.Zero;
            body.SetControlInput(Vector2.Zero);
        }

        // Treading water costs the bar by the second, diving by its own, each as seconds of one kind.
        Place(b + new Vector3(0.9f, floatY, 0));
        await Frames(40);
        Check(body.IsSwimming && !body.IsDiving, "the swimmer floats at the surface of the deep end");
        ledger.DeepTotal = 0;
        var before = ledger.Reports;
        await Frames(60);
        Report(Mathf.Abs((float)ledger.DeepTotal - 1.0f) < 0.03f && ledger.Reports == before + 60 && ledger.SprintTotal == 0 && ledger.DiveTotal == 0 && ledger.Breaches == 0,
            $"swimming in deep water reports its seconds ({ledger.DeepTotal:0.000} s over a second, none of them running or diving)");
        body.SetDiveInput(true, false);
        for (var i = 0; i < 200 && !body.IsDiving; i++) await Frames(1);
        await Frames(20);
        ledger.DeepTotal = ledger.DiveTotal = 0;
        await Frames(30);
        Report(body.IsDiving && Mathf.Abs((float)ledger.DiveTotal - 0.5f) < 0.03f && ledger.DeepTotal == 0 && ledger.Breaches == 0,
            $"diving reports its seconds as under water, not as deep water ({ledger.DiveTotal:0.000} s over half a second)");

        // The bar runs empty under water, the dive key still held: the diver is not held under, it drifts up and then is carried to shore.
        ledger.GateValue = new StaminaGate(true, true);
        var depth = body.Water.SurfaceY - body.GlobalPosition.Y;
        var rose = false;
        var driftsFrom = body.ShoreDriftTicks;
        for (var i = 0; i < 900 && !(body.IsSwimming && !body.IsDiving && body.ShoreDriftTicks > driftsFrom + 30); i++)
        {
            await Frames(1);
            rose |= body.Water.SurfaceY - body.GlobalPosition.Y < depth - 0.02f;
        }
        Report(rose && !body.IsDiving && body.ShoreDriftTicks > driftsFrom,
            $"an empty bar under water lets the diver drift up with the dive key held ({depth:0.000} m deep to {body.Water.SurfaceY - body.GlobalPosition.Y:0.000} m) and then carries it");

        // At the surface it drifts to the shelving shore, west, and wades out; it does not swim or leap, and never climbs the steep east bank.
        Place(b + new Vector3(0.9f, floatY, 0));
        body.SetDiveInput(false, false);
        await Frames(10);
        var startX = body.GlobalPosition.X - b.X;
        var grabs = body.Grabs;
        var jumps = body.JumpsStarted;
        body.SetControlInput(new Vector2(0, 1), jump: true);
        var frame = 0;
        for (; frame < 1500 && !(!body.IsSwimming && body.IsOnFloor()); frame++) await Frames(1);
        var shore = body.GlobalPosition - b;
        body.SetControlInput(Vector2.Zero);
        Report(frame < 1500 && shore.X < 0.3f && shore.X > 0.0f && body.Grabs == grabs && body.JumpsStarted == jumps && body.ShoreHeading.X < -0.5f,
            $"an empty swimmer in deep water drifts to the shelving shore ({startX:0.00} m to {shore.X:0.00} m in {frame / 60f:0.0} s, heading {body.ShoreHeading.X:0.00}, past the input pushing forward and jumping)");

        // Nearest the steep bank, still west: a vertical bank is not shore.
        Place(b + new Vector3(1.12f, floatY, 0.1f));
        await Frames(10);
        var east = body.GlobalPosition;
        await Frames(120);
        Check(body.GlobalPosition.X < east.X - 0.05f && body.Grabs == grabs && body.ShoreHeading.X < -0.5f,
            $"beside the vertical bank the swimmer is carried away from it, not up it ({east.X - b.X:0.00} m to {body.GlobalPosition.X - b.X:0.00} m)");

        // Once ashore at the gate's pace, the bar refilling frees it: the same body swims again.
        ledger.GateValue = StaminaGate.Free;
        Place(b + new Vector3(0.9f, floatY, 0));
        await Frames(30);
        var anchored = body.GlobalPosition;
        body.SetControlInput(new Vector2(0, 1));
        await Frames(60);
        body.SetControlInput(Vector2.Zero);
        Check(PlanarDistance(body.GlobalPosition, anchored) > 0.1f || body.IsClimbing, "with the bar back the swimmer swims where it likes again");

        body.StaminaLedger = null;
        body.QueueFree();
        foreach (var part in parts) part.QueueFree();
        await Frames(2);
    }
}
