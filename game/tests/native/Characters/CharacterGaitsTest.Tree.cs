using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Characters;
using EnFractal.Native.Characters.Gaits;
using EnFractal.Native.Room;

namespace EnFractal.Tests.Characters;

/// <summary>The budget, a real body walking a floor, and how bones compose.</summary>
public partial class CharacterGaitsTest
{
    /// <summary>The layers' CPU time for both bodies (the player and the Gubble), against the plan's 0.4 ms frame budget.</summary>
    private void TestCpu()
    {
        double Measure(IReadOnlyList<CharacterVisual> visuals, bool run)
        {
            var best = double.MaxValue;
            for (var round = 0; round < 5; round++)
            {
                var clock = Stopwatch.StartNew();
                for (var frame = 0; frame < Frames600; frame++)
                    foreach (var visual in visuals)
                    {
                        if (run) visual.Stack.Run(_script[frame], Clock(frame + round * Frames600));
                        else visual.Stack.Evaluate(_script[frame], Clock(frame + round * Frames600));
                    }
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds / Frames600);
            }
            return best;
        }
        var both = new[] { Installed("stickbear").Visual, Installed("gubble").Visual };
        Measure(both, false);
        var layers = Measure(both, false);
        var withSkeleton = Measure(both, true);
        var all = Shipped.Select(id => Installed(id).Visual).ToArray();
        Measure(all, false);
        var five = Measure(all, false);
        GD.Print(F($"GAITS_CPU: the gait and life layers for both bodies (Stickbear, the most bones, and the Gubble): {layers * 1000:0.0} µs a frame; with the pose handed to the skeletons and the springs kept tuned: {withSkeleton * 1000:0.0} µs; all five characters: {five * 1000:0.0} µs (best of five rounds of 600 frames, headless, Debug build)"));
        if (!OS.GetCmdlineUserArgs().Contains("--skip-budget"))
            Check(layers < 0.4, F($"both bodies' layers fit the 0.4 ms frame budget ({layers:0.000} ms)"));
        foreach (var visual in both.Concat(all)) visual.Body.Free();
    }

    /// <summary>
    /// The real thing: a SmallPlayerController wearing a drawn character with K3's layers, walking, running, stopping and
    /// turning on a test floor. The phase follows the distance the controller reports; nothing goes non-finite or sinks.
    /// </summary>
    private async Task TestController()
    {
        var fixture = new Node3D { Name = "ControllerFixture", Position = new Vector3(-20, 0, 20) };
        AddChild(fixture);
        var floor = new StaticBody3D { Position = new Vector3(0, -0.05f, 0), CollisionLayer = RoomBuilder.WorldLayer, CollisionMask = 0 };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(8, 0.1f, 8) } });
        fixture.AddChild(floor);
        await Frames(2);
        var x = -1f;
        foreach (var id in new[] { "stickbear", "tomato_man" })
        {
            var (visual, gait, _) = Installed(id);
            var body = new SmallPlayerController { Name = "Walker_" + id, ReadKeyboard = false, Position = new Vector3(x, 0.01f, 1.5f) };
            x += 2;
            body.SetBodyVisual(visual);
            fixture.AddChild(body);
            await Frames(30);
            var pose = visual.Body.Pose;
            var nonFinite = pose.NonFiniteWrites;
            var lowest = float.MaxValue;
            var finite = true;
            var startDistance = body.LastBodyState.DistanceM;
            var expected = 0.0;
            var lastDistance = startDistance;
            async Task Go(int frames, Vector2 input, bool sprint = false, float turn = 0)
            {
                body.SetControlInput(input, sprint: sprint);
                for (var i = 0; i < frames; i++)
                {
                    if (turn != 0) body.SetMovementFrame((body.MovementFrameYaw ?? body.GlobalRotation.Y) + turn * Dt);
                    await Frames(1);
                    var state = body.LastBodyState;
                    var moved = state.DistanceM - lastDistance;
                    lastDistance = state.DistanceM;
                    if (moved > 0 && state.Grounded) expected += moved / visual.Body.Profile.StrideAt(Mathf.Max(state.SpeedMps, 0));
                    lowest = Mathf.Min(lowest, visual.Body.FinalLowestPointY());
                    foreach (var bone in visual.Body.Bones) finite &= visual.Body.FinalBoneTransform(bone.Index).Origin.IsFinite();
                }
            }
            var cyclesBefore = gait.Cycles;
            await Go(120, new Vector2(0, 1));
            await Go(90, new Vector2(0, 1), sprint: true);
            await Go(20, Vector2.Zero);
            var stillCycles = gait.Cycles;
            var stillFootfalls = gait.Footfalls;
            await Go(120, Vector2.Zero);
            var stood = gait.Cycles == stillCycles && gait.Footfalls == stillFootfalls;
            await Go(150, new Vector2(0, 1), turn: 2.5f);
            body.SetMovementFrame(null);
            body.SetControlInput(Vector2.Zero);
            var walked = body.LastBodyState.DistanceM - startDistance;
            var cycles = gait.Cycles - cyclesBefore;
            Check(walked > 0.6f && gait.Footfalls > 4, F($"{id} on a real controller: walked, ran and turned {walked:0.00} m with {gait.Footfalls} footfalls"));
            Check(cycles > 0 && Math.Abs(cycles / expected - 1) < 0.25, F($"{id}: its phase follows the controller's distance ({cycles:0.00} cycles; the distance over the stride gives {expected:0.00})"));
            Check(stood, $"{id}: standing still on the real controller for 120 frames takes no step");
            Check(pose.NonFiniteWrites == nonFinite && finite, $"{id}: nothing goes non-finite on the real controller");
            Check(lowest >= -0.005f, F($"{id}: after the springs, its lowest point never sinks below -5 mm in its own space ({lowest * 1000:0.00} mm)"));
            body.SetBodyVisual(null);
            body.QueueFree();
            visual.Body.QueueFree();
            await Frames(2);
        }
        fixture.QueueFree();
        await Frames(1);
    }

    /// <summary>
    /// How a bone that both turns and squashes composes: the skeleton turns, then scales in the bone's own frame
    /// (Skeleton3D's rotation-then-scale); PoseBuffer.ModelTransform (K1) scales in the parent's frame. The suite's own
    /// composition must match the skeleton; the difference in PoseBuffer is reported for the integrator.
    /// </summary>
    private async Task TestComposition()
    {
        var holder = new Node3D { Name = "CompositionHolder", Position = new Vector3(30, 0, 0) };
        AddChild(holder);
        var visual = Visual("stickbear");
        var body = visual.Body;
        holder.AddChild(body);
        var pose = body.Pose;
        pose.Reset();
        pose.Begin(PoseLayer.Gait);
        pose.MultiplyScale(CharacterBody.RootBone, GaitMath.SquashScale(1.15f));
        pose.AddRotation(CharacterBody.RootBone, GaitMath.Pitch(0.2f));
        body.ApplyPose();
        await Frames(3);
        var head = body.HeadBone;
        var final = body.FinalBoneTransform(head).Origin;
        var scratch = new Transform3D[body.BoneCount];
        LowestAsSkeleton(body, pose, scratch);
        var mine = scratch[head].Origin;
        var theirs = pose.ModelTransform(head).Origin;
        Check(mine.DistanceTo(final) < 1e-5f, F($"the suite composes bones as the skeleton does ({mine.DistanceTo(final) * 1000:0.000} mm apart)"));
        GD.Print(F($"GAITS_NOTE: with the root turned 11.5° and stretched 15%, PoseBuffer.ModelTransform puts Stickbear's head {theirs.DistanceTo(final) * 1000:0.00} mm from where the skeleton draws it (scale composed in the parent's frame; the skeleton scales in the bone's own). The gaits keep the root's squash small and the suite checks both."));
        holder.QueueFree();
        await Frames(1);
    }
}
