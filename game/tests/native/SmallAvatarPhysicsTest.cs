using Godot;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Navigation;

namespace EnFractal.Tests;

/// <summary>
/// Actual physics-world checks for the 10 cm player and the companion body. No rooms or saves are changed.
/// Pass "-- --jitter-spike" to also measure the jitter scenarios with the whole world scaled ×10.
/// </summary>
public partial class SmallAvatarPhysicsTest : Node3D
{
    private int _checks;
    private int _failures;
    private SmallPlayerController _player = null!;
    private CompanionAvatar _companion = null!;
    private RoomNavigation _navigation = null!;
    private static readonly GDScript Physics = GD.Load<GDScript>(SmallPlayerController.WorldPhysicsScript);

    public override async void _Ready()
    {
        try
        {
            BuildFloorAndObstacles();
            _player = new SmallPlayerController { Name = "SmallPlayer", ReadKeyboard = false, Position = new Vector3(-2, 0.003f, 2) };
            AddChild(_player);
            _companion = new CompanionAvatar { Name = "Companion", Position = new Vector3(2, 0.01f, 3.5f) };
            Check(_companion.ConfigureIdentity("test_companion"), "identity configurable before scene entry");
            AddChild(_companion);
            _companion.BindPlayer(_player);
            // The companion's walkable map over the test floor, baked from its static collision as in the room.
            _navigation = RoomNavigation.Create(this, this, new Aabb(new Vector3(-5, 0, -6), new Vector3(10, 0.6f, 12)),
                WorldScaleProfile.Companion, _companion.StepHeightM * 0.75f);
            _companion.BindNavigation(_navigation);
            await Frames(30);

            Check(Mathf.Abs(_player.BodyHeightM - 0.10f) < 0.00001f, "player collision height is exactly 0.10 m");
            Check(Mathf.Abs(_player.BodyRadiusM - 0.02f) < 0.00001f, "player collision radius is 0.02 m");
            Check(Mathf.Abs(_player.EyeCamera.Position.Y - 0.087f) < 0.00001f, "eye is 0.087 m above feet");
            Check(Mathf.Abs(_player.ReachM - 0.15f) < 0.00001f, "reach is 0.15 m");
            Check(_player.EyeCamera.Near <= 0.006f && _player.FloorSnapLength <= 0.02f && _player.SafeMargin <= 0.001f,
                "camera near plane, floor snap and safe margin suit a 2 cm capsule");
            Check(_player.IsOnFloor() && Mathf.Abs(_player.GlobalPosition.Y) < 0.004f, "small capsule rests on actual floor");
            Check(_player.WorldPhysicsId == "room_tuned" && Mathf.IsEqualApprox(_player.GravityMps2, 3.5f), "body starts with the tuned room gravity profile");
            Check(!_companion.ConfigureIdentity("replacement"), "identity cannot reset after scene entry");
            // Founder decision, 6 October: the companion shrinks to match the player, but keeps its own profile object.
            var companionProfile = WorldScaleProfile.Companion;
            Check(Mathf.Abs(_companion.BodyHeightM - 0.10f) < 0.00001f && Mathf.Abs(_companion.BodyRadiusM - 0.02f) < 0.00001f &&
                Mathf.Abs(_companion.EyeCamera.Position.Y - 0.087f) < 0.00001f && Mathf.Abs(_companion.ReachM - 0.15f) < 0.00001f,
                "the companion is a 10 cm body like the player's: radius 2 cm, eye 8.7 cm, reach 15 cm");
            Check(companionProfile.IsValid && !ReferenceEquals(companionProfile, WorldScaleProfile.SmallPlayer),
                "the companion keeps a profile object of its own, separate from the player's");
            Check(Mathf.IsEqualApprox(_companion.StepHeightM, _player.StepHeightM) && Mathf.IsEqualApprox(_companion.FloorSnapM, _player.FloorSnapM) &&
                Mathf.IsEqualApprox(_companion.JumpApexM, _player.JumpApexM) && Mathf.IsEqualApprox(_companion.FloorSnapLength, _player.FloorSnapLength),
                "the companion steps, snaps and jumps like the player");
            Check(_companion.RunSpeedMps >= 1.2f * _player.RunSpeedMps, $"the companion's run outpaces the player's so follow can catch up ({_companion.RunSpeedMps:0.00} against {_player.RunSpeedMps:0.00} m/s)");
            var visualFits = true;
            foreach (var child in _companion.GetNode<Node3D>("OriginalPrototypeBody").GetChildren())
            {
                if (child is not MeshInstance3D mesh) continue;
                var bounds = mesh.Transform * mesh.Mesh.GetAabb();
                visualFits &= bounds.Position.Y >= -0.001f && bounds.End.Y <= _companion.BodyHeightM + 0.001f &&
                    Mathf.Max(Mathf.Abs(bounds.Position.X), Mathf.Abs(bounds.End.X)) <= _companion.BodyRadiusM + 0.001f &&
                    Mathf.Max(Mathf.Abs(bounds.Position.Z), Mathf.Abs(bounds.End.Z)) <= _companion.BodyRadiusM + 0.001f;
            }
            Check(visualFits, "companion body and hat fit the declared collision height and radius");
            Check(PlayerVisualFits(), "player body meshes fit the 0.10 m capsule");
            _companion.SetAppearance(new Color("d087bd"));
            _companion.SetDisplayName("Juniper");
            Check(_companion.CompanionId == "test_companion" && _companion.CompanionName == "Juniper", "appearance/name do not replace companion identity");

            await TestMovementAndJump();
            await TestObstacles();
            await TestDioramaCamera();
            await TestCompanion();
            await TestNavigation();
            var metrics = await RunJitterSuite(1.0f, new Vector3(0, 0, 30));
            CheckJitter(metrics);
            if (OS.GetCmdlineUserArgs().Contains("--jitter-spike"))
            {
                var scaled = await RunJitterSuite(10.0f, new Vector3(0, 0, 200));
                GD.Print(JitterReport(metrics, scaled));
            }
            GD.Print($"NATIVE_SMALL_AVATAR: {_checks - _failures}/{_checks} checks passed; 10 cm body, room gravity profiles, steps, jitter and companion steering; not final art, feel or AI integration");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (System.Exception exception)
        {
            GD.PushError("Native small-avatar test exception: " + exception);
            GetTree().Quit(1);
        }
    }

    private async Task TestMovementAndJump()
    {
        _player.SetControlInput(Vector2.Right);
        var start = _player.GlobalPosition;
        await Frames(60);
        _player.SetControlInput(Vector2.Zero);
        var walked = _player.GlobalPosition.X - start.X;
        Report(walked > 0.27f && walked < 0.34f, $"walking covers about 0.31 m in one second (walked={walked:0.000})");
        await Frames(15);
        start = _player.GlobalPosition;
        _player.SetControlInput(Vector2.Left, sprint: true);
        await Frames(60);
        _player.SetControlInput(Vector2.Zero);
        var ran = start.X - _player.GlobalPosition.X;
        Check(_player.RunSpeedMps >= 2.8f * _player.WalkSpeedMps, $"the run is about three times the walk ({_player.RunSpeedMps / _player.WalkSpeedMps:0.00}x)");
        Report(ran > 0.84f && ran < 0.97f, $"running covers about 0.88 m in one second from a standstill (ran={ran:0.000})");
        // Crisp: full run speed within 0.2 s, and a stop from a run within 0.2 s.
        await Frames(20);
        _player.SetControlInput(Vector2.Right, sprint: true);
        var rampFrames = 0;
        for (var i = 0; i < 60 && _player.Velocity.Length() < _player.RunSpeedMps * 0.99f; i++) { await Frames(1); rampFrames++; }
        await Frames(10);
        _player.SetControlInput(Vector2.Zero);
        var stopFrames = 0;
        for (var i = 0; i < 60 && new Vector2(_player.Velocity.X, _player.Velocity.Z).Length() > 0.001f; i++) { await Frames(1); stopFrames++; }
        Report(rampFrames >= 6 && rampFrames <= 12 && stopFrames >= 6 && stopFrames <= 12, $"the run reaches speed and stops within 0.2 s (ramp={rampFrames} frames, stop={stopFrames} frames)");
        await Frames(20);

        var (apex, airtime) = await MeasureJump();
        Check(apex > 0.058f && apex < 0.072f, $"tuned jump rises about 6.5 cm, clearing a 4 cm book (apex={apex:0.0000})");
        Report(airtime >= 19 && airtime <= 27, $"tuned jump lasts about 0.38 s (airborne frames={airtime})");
        Check(_player.IsOnFloor(), "jump returns to stable floor");

        var revision = _player.WorldPhysicsRevision;
        Check(_player.SetWorldPhysics(Preset("room_real", revision + 1)) && Mathf.IsEqualApprox(_player.GravityMps2, 9.8f), "real gravity profile accepted with a newer revision");
        (apex, airtime) = await MeasureJump();
        Check(apex > 0.058f && apex < 0.072f, $"real gravity keeps the jump height (apex={apex:0.0000})");
        Report(airtime >= 11 && airtime <= 17, $"real gravity makes the same jump last about 0.23 s (airborne frames={airtime})");
        Check(!_player.SetWorldPhysics(Preset("room_floaty", revision + 1)), "a stale physics revision is refused");
        var invalid = Preset("room_tuned", revision + 5);
        invalid["gravity_mps2"] = 0.3;
        Check(!_player.SetWorldPhysics(invalid), "gravity below the room bound is refused");
        invalid = Preset("room_tuned", revision + 5);
        invalid["gravity_mps2"] = double.NaN;
        Check(!_player.SetWorldPhysics(invalid), "non-finite gravity is refused");
        invalid = Preset("room_breeze_test", revision + 5);
        invalid["wind_x_mps"] = 3.0;
        Check(!_player.SetWorldPhysics(invalid), "unbounded wind is refused");
        invalid = Preset("room_floaty", revision + 5);
        invalid["terminal_fall_mps"] = 0.1;
        Check(!_player.SetWorldPhysics(invalid), "a fall limit below the room bound is refused");
        invalid = Preset("room_floaty", revision + 5);
        invalid["air_control"] = 9.0;
        Check(!_player.SetWorldPhysics(invalid), "unbounded air control is refused");
        Check(_player.CycleWorldPhysics() == "room_floaty", "playtest key cycles from real to floaty gravity");
        Check(Mathf.IsEqualApprox(_player.GravityMps2, 0.6f) && Mathf.IsEqualApprox(_player.EffectiveTerminalFallMps, 0.6f) && Mathf.IsEqualApprox(_player.AirControl, 2.0f),
            "floaty is 0.6 m/s2 with a 0.6 m/s fall limit and doubled air control");
        await Frames(2);
        Check(_companion.WorldPhysicsId == "room_floaty" && Mathf.IsEqualApprox(_companion.GravityMps2, 0.6f), "the companion lives under the same world gravity as the player");
        (apex, airtime) = await MeasureJump();
        Check(apex > 0.058f && apex < 0.072f, $"floaty gravity keeps the jump height (apex={apex:0.0000})");
        Report(airtime >= 50 && airtime <= 62, $"floaty gravity makes the same jump last about 0.93 s (airborne frames={airtime})");
        // A fall from 40 cm drifts down at the floaty fall limit instead of accelerating.
        _player.GlobalPosition += Vector3.Up * 0.40f;
        var fastest = 0.0f;
        var fallFrames = 0;
        await Frames(1);
        for (var i = 0; i < 240 && !_player.IsOnFloor(); i++) { await Frames(1); fallFrames++; fastest = Mathf.Max(fastest, -_player.Velocity.Y); }
        Report(fastest <= 0.601f && fastest > 0.55f && fallFrames > 60, $"a floaty fall is capped at 0.6 m/s (fastest={fastest:0.000} m/s over {fallFrames} frames)");
        await Frames(10);
        Check(_player.CycleWorldPhysics() == "room_tuned", "playtest key cycles real, floaty and tuned gravity presets");
        Check(Mathf.IsEqualApprox(_player.GravityMps2, 3.5f) && Mathf.IsEqualApprox(_player.EffectiveTerminalFallMps, 6.0f) && Mathf.IsEqualApprox(_player.AirControl, 1.0f),
            "cycling returns to the tuned gravity, fall limit and air control");

        // A jump pressed just before landing still happens; one pressed after walking off an edge too.
        _player.GlobalPosition += Vector3.Up * 0.05f;
        await Frames(4);
        var jumpsBefore = _player.JumpsStarted;
        var buffered = false;
        for (var i = 0; i < 30 && !buffered; i++)
        {
            if (!_player.IsOnFloor() && _player.GlobalPosition.Y < 0.012f) { _player.SetControlInput(Vector2.Zero, jump: true); buffered = true; }
            await Frames(1);
        }
        await Frames(8);
        Check(buffered && _player.JumpsStarted == jumpsBefore + 1, "a jump pressed just before landing is buffered, not dropped");
        await Frames(40);

        _player.SetInputEnabled(false);
        start = _player.GlobalPosition;
        _player.SetControlInput(Vector2.Right, true);
        await Frames(25);
        Check(_player.GlobalPosition.DistanceTo(start) < 0.002f, "disabled input blocks movement and jump immediately");
        _player.SetInputEnabled(true);
        _player.SetControlInput(new Vector2(float.NaN, 1));
        await Frames(3);
        Check(_player.GlobalPosition.IsFinite(), "nonfinite control input is rejected");
        _player.SetControlInput(Vector2.Zero);
    }

    private async Task<(float Apex, int Airtime)> MeasureJump()
    {
        await Frames(5);
        var baseY = _player.GlobalPosition.Y;
        _player.SetControlInput(Vector2.Zero, true);
        var highest = baseY;
        var airborne = 0;
        var left = false;
        for (var i = 0; i < 90; i++)
        {
            await Frames(1);
            highest = Mathf.Max(highest, _player.GlobalPosition.Y);
            if (!_player.IsOnFloor()) { airborne++; left = true; }
            else if (left) break;
        }
        await Frames(5);
        return (highest - baseY, airborne);
    }

    private async Task TestObstacles()
    {
        // 1.5 cm curb: a step, no jump needed.
        Check(_player.TryTeleportTo(new Vector3(-0.5f, 0.003f, 0)), "curb route has supported spawn");
        _player.Rotation = Vector3.Zero;
        var steps = _player.StepsClimbed;
        _player.SetControlInput(Vector2.Right, sprint: true);
        var highest = 0.0f;
        for (var i = 0; i < 120; i++) { await Frames(1); highest = Mathf.Max(highest, _player.GlobalPosition.Y); }
        _player.SetControlInput(Vector2.Zero);
        Check(_player.GlobalPosition.X > 0.45f, $"crosses a 1.5 cm curb without a jump (position={_player.GlobalPosition}, floor={_player.IsOnFloor()})");
        Check(highest > 0.012f && highest < 0.03f && _player.StepsClimbed > steps, $"curb traversal uses bounded step lift (height={highest}, steps={_player.StepsClimbed - steps})");
        await Frames(15);

        // 3 cm barrier: above the 2 cm step, so it blocks unless jumped.
        Check(_player.TryTeleportTo(new Vector3(-0.5f, 0.003f, -0.6f)), "barrier route has supported spawn");
        _player.SetControlInput(Vector2.Right);
        await Frames(90);
        _player.SetControlInput(Vector2.Zero);
        Check(_player.GlobalPosition.X < -0.11f && _player.GlobalPosition.Y < 0.008f, "a 3 cm barrier cannot masquerade as a step");

        // The 4 cm book: blocked when walking, climbed with a jump.
        Check(_player.TryTeleportTo(new Vector3(-0.5f, 0.003f, -1.4f)), "book route has supported spawn");
        _player.SetControlInput(Vector2.Right);
        await Frames(90);
        Check(_player.GlobalPosition.X < -0.12f && _player.GlobalPosition.Y < 0.008f, $"walking into the 4 cm book stops at its side (position={_player.GlobalPosition})");
        _player.SetControlInput(Vector2.Right, jump: true);
        await Frames(2);
        _player.SetControlInput(Vector2.Right);
        await Frames(25);
        _player.SetControlInput(Vector2.Zero);
        await Frames(20);
        Check(_player.IsOnFloor() && Mathf.Abs(_player.GlobalPosition.Y - 0.04f) < 0.006f && _player.GlobalPosition.X > -0.11f,
            $"a running jump lands on top of the 4 cm book (position={_player.GlobalPosition})");
        // Coyote time: walk off the book's far side and jump just after leaving it.
        _player.SetControlInput(Vector2.Right);
        var coyote = false;
        var jumps = _player.JumpsStarted;
        for (var i = 0; i < 120 && !coyote; i++)
        {
            await Frames(1);
            if (!_player.IsOnFloor() && _player.GlobalPosition.Y > 0.03f) { _player.SetControlInput(Vector2.Right, jump: true); coyote = true; }
        }
        await Frames(3);
        _player.SetControlInput(Vector2.Zero);
        Check(coyote && _player.JumpsStarted == jumps + 1, "a jump just after walking off an edge still happens (coyote time)");
        await Frames(40);

        // The 6 mm rug: walked onto and off without a jump or a stumble.
        Check(_player.TryTeleportTo(new Vector3(-0.9f, 0.003f, -2.2f)), "rug route has supported spawn");
        _player.SetControlInput(Vector2.Right);
        var airborne = 0;
        var onRug = false;
        for (var i = 0; i < 360; i++)
        {
            await Frames(1);
            if (!_player.IsOnFloor()) airborne++;
            onRug |= _player.GlobalPosition.Y > 0.004f && _player.IsOnFloor();
        }
        _player.SetControlInput(Vector2.Zero);
        Check(_player.GlobalPosition.X > 0.6f && onRug && airborne <= 6, $"walks over a 6 mm rug and off again (x={_player.GlobalPosition.X:0.00}, airborne frames={airborne})");

        // Clearances: a 12 cm underpass admits the body, an 8 cm one does not.
        Check(_player.TryTeleportTo(new Vector3(-1, 0.003f, 1.2f)), "underpass route has supported spawn");
        _player.SetControlInput(Vector2.Right, sprint: true);
        await Frames(220);
        _player.SetControlInput(Vector2.Zero);
        Check(_player.GlobalPosition.X > 0.65f && _player.GlobalPosition.Y < 0.008f, $"passes beneath a 12 cm clearance deck (position={_player.GlobalPosition})");
        Check(_player.TryTeleportTo(new Vector3(0, 0.165f, 1.2f)), "can explicitly recover onto bridge deck");
        Check(Mathf.Abs(_player.GlobalPosition.Y - 0.16f) < 0.006f, "deck uses structural collider, not floor height");
        Check(_player.TryTeleportTo(new Vector3(0, 0.003f, 1.2f)), "can explicitly recover below the same deck");
        Check(_player.GlobalPosition.Y < 0.008f, "underpass recovery does not snap to deck overhead");
        var start = _player.GlobalPosition;
        Check(!_player.TryTeleportTo(new Vector3(0, 0.003f, -2.8f)), "8 cm head clearance rejects a 10 cm body");
        Check(_player.GlobalPosition == start, "rejected recovery does not move the player");
        Check(_player.TryTeleportTo(new Vector3(-2, 0.003f, 3.2f)), "recovery checkpoint available");
        _player.SetSpawnPoint(_player.GlobalPosition);
        _player.GlobalPosition = new Vector3(100, -20, 100);
        Check(_player.Recover() && _player.GlobalPosition.DistanceTo(new Vector3(-2, 0.003f, 3.2f)) < 0.01f,
            "recovery restores a checked supported point");
        await Frames(10);

        Check(_player.TryTeleportTo(new Vector3(12, 0.10f, 0)), "recovery admits the full capsule on a 30 degree slope");
        await Frames(40);
        Check(_player.IsOnFloor() && Mathf.Abs(_player.GlobalPosition.X - 12) < 0.02f,
            "slope recovery settles without penetration or slide");
        var slopeCheckpoint = _player.GlobalPosition;
        _player.GlobalPosition = new Vector3(100, -20, 100);
        Check(_player.Recover() && _player.GlobalPosition.DistanceTo(slopeCheckpoint) < 0.01f,
            "recovery retains the last safe slope instead of falling back to the flat spawn");

        // A creation guard keeps creation-driven recovery out of refused space.
        Check(_player.TryTeleportTo(new Vector3(-2, 0.003f, 3.2f)), "returns from slope fixture to companion route");
        await Frames(10);
        _player.SetCreationEffects(new Vector3(0.4f, 0, 0), 0, new Callable(this, MethodName.GuardKeepsXBelowMinusOne));
        await Frames(90);
        Check(_player.GlobalPosition.X <= -1.0f && _player.GlobalPosition.X > -1.98f, $"creation push moves the body but never past its guard (x={_player.GlobalPosition.X:0.000})");
        Check(_player.CreationVelocity.Length() <= _player.MaxCreationSpeedMps + 0.001f, "creation speed stays within the body's cap");
        _player.SetCreationEffects(Vector3.Zero, 0, new Callable());
        Check(_player.CreationVelocity == Vector3.Zero && !_player.HasCreationGuard, "an invalid guard removes every creation contribution at once");
        start = _player.GlobalPosition;
        await Frames(20);
        Check(Mathf.Abs(_player.GlobalPosition.X - start.X) < 0.003f, "no creation-induced drift after the effect is removed");
        Check(_player.TryTeleportTo(new Vector3(-2, 0.003f, 3.2f)), "back to the companion route");
        await Frames(10);
    }

    public bool GuardKeepsXBelowMinusOne(Vector3 position) => position.X <= -1.0f;

    private async Task TestCompanion()
    {
        Check(_companion.TryTeleportTo(new Vector3(0.5f, 0.01f, 3.2f)), "companion gets its own clear spawn");
        _player.Rotation = Vector3.Zero;
        _companion.Follow();
        await Frames(240);
        var gap = PlanarDistance(_companion.GlobalPosition, _player.GlobalPosition);
        Report(gap >= CompanionAvatar.FollowNearM - 0.01f && gap <= CompanionAvatar.FollowFarM + 0.01f && !_companion.FollowMoving,
            $"follow comes to rest inside the comfortable band (gap={gap:0.000} m)");
        var (along, lateral) = Offset(-_player.GlobalBasis.Z);
        Check(Mathf.Abs(lateral) > Mathf.Abs(along), $"follow rests beside the player, not behind (along={along:0.000}, lateral={lateral:0.000})");

        // Founder playtest: "it always moves directly behind me". Turning on the spot must not re-target follow.
        var rest = _companion.GlobalPosition;
        for (var i = 0; i < 90; i++) { _player.Rotation = new Vector3(0, _player.Rotation.Y + Mathf.Pi / 60, 0); await Frames(1); }
        await Frames(30);
        Check(PlanarDistance(_companion.GlobalPosition, rest) < 0.01f, $"turning on the spot does not move the companion (moved={PlanarDistance(_companion.GlobalPosition, rest):0.0000} m)");

        // Walking: it keeps beside the line of travel and never trails directly behind.
        _player.Rotation = Vector3.Zero;
        var side = Mathf.Sign(Offset(Vector3.Forward).Lateral);
        var behind = 0;
        _player.SetControlInput(new Vector2(0, 1));
        for (var i = 0; i < 120; i++)
        {
            await Frames(1);
            (along, lateral) = Offset(Vector3.Forward);
            if (along < -0.08f && Mathf.Abs(lateral) < 0.06f) behind++;
        }
        (along, lateral) = Offset(Vector3.Forward);
        Report(behind == 0 && lateral * side > 0.10f && along > -0.08f, $"follow walks beside the player, not behind (behind frames={behind}, along={along:0.000}, lateral={lateral:0.000})");
        // Turning round and walking back: it keeps its side instead of swinging through behind the player.
        var crossed = 0;
        _player.Rotation = new Vector3(0, Mathf.Pi, 0);
        for (var i = 0; i < 120; i++)
        {
            await Frames(1);
            if (Offset(Vector3.Forward).Lateral * side < 0.04f) crossed++;
        }
        _player.SetControlInput(Vector2.Zero);
        (along, lateral) = Offset(Vector3.Back);
        Report(crossed == 0 && along > -0.10f, $"after the player turns back, follow stays on its side and alongside (crossing frames={crossed}, along={along:0.000})");
        // Settling: it comes to rest smoothly, without oscillating back and forth.
        var reversals = 0;
        var previous = Vector3.Zero;
        for (var i = 0; i < 180; i++)
        {
            await Frames(1);
            var velocity = new Vector3(_companion.Velocity.X, 0, _companion.Velocity.Z);
            if (velocity.Length() > 0.02f && previous.Length() > 0.02f && velocity.Dot(previous) < 0) reversals++;
            if (velocity.Length() > 0.02f) previous = velocity;
        }
        gap = PlanarDistance(_companion.GlobalPosition, _player.GlobalPosition);
        Report(reversals == 0 && !_companion.FollowMoving && new Vector2(_companion.Velocity.X, _companion.Velocity.Z).Length() < 0.01f &&
            gap >= CompanionAvatar.FollowNearM - 0.01f && gap <= CompanionAvatar.FollowFarM + 0.01f,
            $"follow settles in the band without oscillating (reversals={reversals}, gap={gap:0.000})");
        _player.Rotation = Vector3.Zero;
        _companion.Come();
        await Frames(80);
        var cameGap = PlanarDistance(_companion.GlobalPosition, _player.GlobalPosition);
        Report(_companion.CurrentIntent == "stay" && cameGap < CompanionAvatar.ComeArrivalM + 0.04f,
            $"come arrives near the player then stays (gap={cameGap:0.000} m)");
        _companion.Follow();
        await Frames(5);
        _companion.Stop();
        var start = _companion.GlobalPosition;
        await Frames(35);
        Check(PlanarDistance(_companion.GlobalPosition, start) < 0.005f && _companion.CurrentIntent == "stop",
            "stop removes ongoing navigation immediately");
        Check(_companion.TryTeleportTo(_player.GlobalPosition + Vector3.Right * (CompanionAvatar.YieldM - 0.02f)), "stop fixture places companion within yielding distance");
        _companion.Stop();
        start = _companion.GlobalPosition;
        await Frames(35);
        Check(PlanarDistance(_companion.GlobalPosition, start) < 0.005f && _companion.CurrentIntent == "stop",
            "explicit stop cancels proximity yielding beside the player");
        var point = _companion.GlobalPosition + Vector3.Right;
        _companion.PointAt(point);
        await Frames(30);
        Check(_companion.IsPointing && (-_companion.GlobalBasis.Z).Dot(Vector3.Right) > 0.9f,
            "point produces oriented visible cue");
        _companion.LookAtPoint(_companion.GlobalPosition + Vector3.Back);
        await Frames(30);
        Check(!_companion.IsPointing && (-_companion.GlobalBasis.Z).Dot(Vector3.Back) > 0.9f,
            "look turns without retaining stale point cue");
        _companion.Stop();

        Check(_companion.TryTeleportTo(_player.GlobalPosition + Vector3.Right * 0.4f), "companion can be placed ahead of player");
        _companion.Stay();
        start = _player.GlobalPosition;
        _player.SetControlInput(Vector2.Right);
        await Frames(65);
        _player.SetControlInput(Vector2.Zero);
        Check(_player.GlobalPosition.X - start.X > 0.28f, "companion cannot trap or block direct player movement");
        Check(_companion.GlobalPosition.IsFinite(), "yielding companion retains finite physical state");

        // Local steering must report an obstructed goal, never teleport through a wall.
        Box(new Vector3(2.0f, 0.20f, 4.75f), new Vector3(0.65f, 0.4f, 0.08f));
        Box(new Vector3(2.0f, 0.20f, 4.10f), new Vector3(0.65f, 0.4f, 0.08f));
        Box(new Vector3(1.675f, 0.20f, 4.425f), new Vector3(0.08f, 0.4f, 0.65f));
        Box(new Vector3(2.325f, 0.20f, 4.425f), new Vector3(0.08f, 0.4f, 0.65f));
        await Frames(2);
        Check(_companion.TryTeleportTo(new Vector3(2, 0.01f, 4.425f)), "blocked-route fixture admits companion safely");
        _companion.Follow();
        var blocked = false;
        for (var i = 0; i < 100; i++) { await Frames(1); blocked |= _companion.GoalBlocked; }
        Check(blocked && _companion.GlobalPosition.X > 1.70f, "obstructed follow reports blocked without crossing wall");
        _companion.Stop();
        Check(_companion.CompanionId == "test_companion", "all commands preserve identity");

        Check(_player.TryTeleportTo(new Vector3(2, 0.003f, -4.5f)), "low-passage destination admits player outside the roof");
        Check(_companion.TryTeleportTo(new Vector3(-1, 0.01f, -4.5f)), "an 11 cm passage admits the 10 cm companion body");
        await Frames(5);
        _companion.Come();
        blocked = false;
        for (var i = 0; i < 260; i++) { await Frames(1); blocked |= _companion.GoalBlocked; }
        Check(_companion.GlobalPosition.X > 1.6f && _companion.CurrentIntent == "stay" && !blocked,
            "companion crosses a flat low passage without requiring step headroom");

        Check(_player.TryTeleportTo(new Vector3(-4, 0.003f, 3.2f)) &&
            _companion.TryTeleportTo(new Vector3(-4.8f, 0.01f, 3.42f)), "moving follow fixture admits both bodies");
        _player.Rotation = new Vector3(0, -Mathf.Pi * 0.5f, 0);
        _companion.Follow();
        _player.SetControlInput(new Vector2(0, 1), sprint: true);
        var maximumGap = 0.0f;
        for (var i = 0; i < 380; i++)
        {
            await Frames(1);
            maximumGap = Mathf.Max(maximumGap, PlanarDistance(_player.Position, _companion.Position));
        }
        var runningGap = PlanarDistance(_player.Position, _companion.Position);
        _player.SetControlInput(Vector2.Zero);
        // It starts 0.83 m behind: the gap must never grow past that, and it ends in its band beside the player.
        Report(maximumGap < 0.9f && runningGap < CompanionAvatar.FollowFarM,
            $"companion catches up during sustained player running instead of falling farther behind (max gap={maximumGap:0.00}, final gap={runningGap:0.00})");
        _companion.Stop();
    }

    /// <summary>
    /// Founder playtest: "If I walk behind the big box and summon my comp, it gets stuck on the other side of the
    /// box. It can't work its way around." The companion now routes round furniture on a navigation mesh baked from
    /// the static collision; an unreachable goal is reported as blocked and never crossed.
    /// </summary>
    private async Task TestNavigation()
    {
        Check(_navigation.IsReady && _navigation.PolygonCount > 0,
            $"a navigation mesh is baked from the static collision ({_navigation.PolygonCount} polygons, last bake {_navigation.LastBakeMs:0} ms)");
        // The test room's big box, 35 x 30 x 35 cm, on open floor.
        var boxCentre = new Vector3(-3.5f, 0.15f, -1.5f);
        const float half = 0.175f;
        var revision = _navigation.Revision;
        Box(boxCentre, new Vector3(0.35f, 0.30f, 0.35f));
        var waited = 0;
        for (; waited < 120 && _navigation.Revision == revision; waited++) await Frames(1);
        Report(_navigation.Revision > revision, $"adding the box re-bakes the navigation mesh by itself (after {waited} frames, {_navigation.LastBakeMs:0} ms)");
        var playerSpot = new Vector3(-3.5f, 0.003f, -2.15f);
        var behind = new Vector3(-3.5f, 0.01f, -0.95f);
        var route = _navigation.FindRoute(behind, playerSpot, 0.05f);
        Report(route.Reaches && !route.Direct && route.LengthM > 1.25f, $"the route from behind the box goes round it (length={route.LengthM:0.00} m, points={route.Points.Length})");
        Check(_player.TryTeleportTo(playerSpot) && _companion.TryTeleportTo(behind), "player in front of the box, companion behind it");
        _player.Rotation = Vector3.Zero;

        // The founder's report, reproduced: local steering alone stays stuck behind the box.
        _companion.BindNavigation(null);
        _companion.Come();
        var reported = false;
        for (var i = 0; i < 240; i++) { await Frames(1); reported |= _companion.GoalBlocked; }
        var gap = PlanarDistance(_companion.GlobalPosition, _player.GlobalPosition);
        Report(_companion.CurrentIntent == "come" && gap > CompanionAvatar.ComeArrivalM + 0.2f,
            $"without navigation come stays stuck behind the box, as the founder saw (gap={gap:0.00} m, reported blocked={reported})");

        // With navigation it walks round the box and arrives, never entering the box.
        _companion.Stop();
        Check(_companion.TryTeleportTo(behind), "companion back behind the box");
        _companion.BindNavigation(_navigation);
        _companion.Come();
        var routed = false;
        var blockedFrames = 0;
        var frames = 0;
        var entered = false;
        for (; frames < 360 && _companion.CurrentIntent == "come"; frames++)
        {
            await Frames(1);
            routed |= _companion.FollowingRoute;
            if (_companion.GoalBlocked) blockedFrames++;
            entered |= ClearanceFromSquare(_companion.GlobalPosition, boxCentre, half) < _companion.BodyRadiusM - 0.005f;
        }
        gap = PlanarDistance(_companion.GlobalPosition, _player.GlobalPosition);
        Report(_companion.CurrentIntent == "stay" && gap <= CompanionAvatar.ComeArrivalM + 0.02f && routed && blockedFrames == 0 && !entered,
            $"with navigation come walks round the box and arrives (frames={frames}, gap={gap:0.00} m, blocked frames={blockedFrames})");

        // Follow from behind the box: the box in the way counts as outside the band, so it comes round.
        Check(_companion.TryTeleportTo(behind), "companion behind the box again");
        _companion.Follow();
        routed = false;
        entered = false;
        for (var i = 0; i < 300; i++)
        {
            await Frames(1);
            routed |= _companion.FollowingRoute;
            entered |= ClearanceFromSquare(_companion.GlobalPosition, boxCentre, half) < _companion.BodyRadiusM - 0.005f;
        }
        gap = PlanarDistance(_companion.GlobalPosition, _player.GlobalPosition);
        var way = _navigation.FindRoute(_companion.GlobalPosition, _player.GlobalPosition, 0.05f);
        var clear = way.Reaches && way.LengthM <= gap + 0.05f;
        Report(routed && !entered && !_companion.FollowMoving && clear &&
            gap >= CompanionAvatar.FollowNearM - 0.01f && gap <= CompanionAvatar.FollowFarM + 0.01f,
            $"follow from behind the box comes round and rests in view of the player (gap={gap:0.00} m, walk={way.LengthM:0.00} m)");

        // A goal it cannot reach (a closed pen) is reported, and the companion stays inside rather than crossing a wall.
        var pen = new Vector3(-3.5f, 0, 1.0f);
        Box(pen + new Vector3(0, 0.20f, 0.325f), new Vector3(0.65f, 0.4f, 0.08f));
        Box(pen + new Vector3(0, 0.20f, -0.325f), new Vector3(0.65f, 0.4f, 0.08f));
        Box(pen + new Vector3(-0.325f, 0.20f, 0), new Vector3(0.08f, 0.4f, 0.65f));
        Box(pen + new Vector3(0.325f, 0.20f, 0), new Vector3(0.08f, 0.4f, 0.65f));
        revision = _navigation.Revision;
        for (var i = 0; i < 120 && _navigation.Revision == revision; i++) await Frames(1);
        Check(_companion.TryTeleportTo(pen + new Vector3(0, 0.01f, 0)), "companion inside the closed pen");
        Check(!_navigation.FindRoute(_companion.GlobalPosition, _player.GlobalPosition, CompanionAvatar.ComeArrivalM + 0.05f).Reaches, "the navigation finds no way out of the closed pen");
        _companion.Come();
        reported = false;
        var escaped = false;
        for (var i = 0; i < 180; i++)
        {
            await Frames(1);
            reported |= _companion.GoalBlocked;
            escaped |= !Overlaps(_companion.GlobalPosition, pen, 0.285f);
        }
        Report(reported && !escaped && _companion.CurrentIntent == "come",
            $"an unreachable come reports blocked and never crosses the pen walls (blocked={reported}, escaped={escaped})");
        _companion.Stop();
    }

    private static bool Overlaps(Vector3 position, Vector3 centre, float half) =>
        Mathf.Abs(position.X - centre.X) < half && Mathf.Abs(position.Z - centre.Z) < half;

    /// <summary>Planar distance from a point to a square footprint (zero inside it).</summary>
    private static float ClearanceFromSquare(Vector3 point, Vector3 centre, float half) =>
        new Vector2(Mathf.Max(Mathf.Abs(point.X - centre.X) - half, 0), Mathf.Max(Mathf.Abs(point.Z - centre.Z) - half, 0)).Length();

    /// <summary>The companion's planar offset from the player, along a direction of travel and to its right.</summary>
    private (float Along, float Lateral) Offset(Vector3 travel)
    {
        var offset = _companion.GlobalPosition - _player.GlobalPosition;
        offset.Y = 0;
        return (offset.Dot(travel), offset.Dot(travel.Cross(Vector3.Up)));
    }

    /// <summary>F3: a high-angle camera that orbits and zooms around the player, stays centred and keeps out of geometry.</summary>
    private async Task TestDioramaCamera()
    {
        Check(_player.TryTeleportTo(new Vector3(-2, 0.003f, 3.2f)), "diorama fixture admits the player");
        _player.Rotation = new Vector3(0, 0.4f, 0);
        var hud = new RoomHud { Player = _player, Companion = _companion, RoomTitle = "TEST" };
        AddChild(hud);
        await Frames(2);
        hud.SetViewMode(2);
        await Frames(3);
        var camera = hud.DioramaCamera;
        Check(camera.Current && Mathf.IsEqualApprox(hud.DioramaYaw, 0.4f) && _player.MovementFrameYaw == hud.DioramaYaw,
            "F3 makes the diorama camera current, starting behind the player, and movement follows the view");
        bool Centred() => (_player.GlobalPosition + Vector3.Up * (_player.BodyHeightM * 0.5f) - camera.GlobalPosition).Normalized().Dot(-camera.GlobalBasis.Z) > 0.999f;
        float LooksDown() => Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(camera.GlobalBasis.Z.Y, -1, 1)));
        var reach = camera.GlobalPosition.DistanceTo(_player.GlobalPosition + Vector3.Up * (_player.BodyHeightM * 0.5f));
        Check(Centred() && Mathf.Abs(LooksDown() - RoomHud.DioramaDefaultPitchDeg) < 1.0f && Mathf.Abs(reach - RoomHud.DioramaDefaultDistanceM) < 0.03f,
            $"the diorama camera looks down on the player from a high angle (pitch={LooksDown():0.0} deg, distance={reach:0.000} m)");
        var before = new Vector2(camera.GlobalPosition.X - _player.GlobalPosition.X, camera.GlobalPosition.Z - _player.GlobalPosition.Z).Angle();
        hud.OrbitDiorama(new Vector2(400, 0));
        await Frames(3);
        var after = new Vector2(camera.GlobalPosition.X - _player.GlobalPosition.X, camera.GlobalPosition.Z - _player.GlobalPosition.Z).Angle();
        Check(Mathf.Abs(Mathf.AngleDifference(before, after)) > 0.9f && Centred() && Mathf.IsEqualApprox(_player.Rotation.Y, 0.4f),
            "moving the mouse sideways orbits the camera round the player without turning the body");
        hud.OrbitDiorama(new Vector2(0, 100000));
        await Frames(3);
        var steepest = LooksDown();
        hud.OrbitDiorama(new Vector2(0, -100000));
        await Frames(3);
        Check(Mathf.Abs(steepest - RoomHud.DioramaMaxPitchDeg) < 1.0f && Mathf.Abs(LooksDown() - RoomHud.DioramaMinPitchDeg) < 1.0f && Centred(),
            $"orbit pitch stays between {RoomHud.DioramaMinPitchDeg} and {RoomHud.DioramaMaxPitchDeg} degrees (got {steepest:0.0} and {LooksDown():0.0})");
        hud.ZoomDiorama(50);
        await Frames(3);
        var near = camera.GlobalPosition.DistanceTo(_player.GlobalPosition + Vector3.Up * (_player.BodyHeightM * 0.5f));
        hud.ZoomDiorama(-50);
        await Frames(3);
        var far = camera.GlobalPosition.DistanceTo(_player.GlobalPosition + Vector3.Up * (_player.BodyHeightM * 0.5f));
        Check(Mathf.Abs(near - RoomHud.DioramaMinDistanceM) < 0.02f && Mathf.Abs(far - RoomHud.DioramaMaxDistanceM) < 0.03f,
            $"the wheel zooms between {RoomHud.DioramaMinDistanceM} and {RoomHud.DioramaMaxDistanceM} m (got {near:0.000} and {far:0.000})");
        // Movement follows the view: forward walks away from the camera and the body turns to face its motion.
        hud.ZoomDiorama(Mathf.Log(RoomHud.DioramaMaxDistanceM / RoomHud.DioramaDefaultDistanceM) / Mathf.Log(1 / 0.88f));
        hud.OrbitDiorama(new Vector2(0, (RoomHud.DioramaDefaultPitchDeg - RoomHud.DioramaMinPitchDeg) / Mathf.RadToDeg(RoomHud.MouseRadiansPerPixel)));
        var viewForward = new Vector3(-camera.GlobalBasis.Z.X, 0, -camera.GlobalBasis.Z.Z).Normalized();
        var start = _player.GlobalPosition;
        _player.SetControlInput(new Vector2(0, 1));
        await Frames(45);
        _player.SetControlInput(Vector2.Zero);
        var moved = _player.GlobalPosition - start;
        moved.Y = 0;
        Check(moved.Length() > 0.15f && moved.Normalized().Dot(viewForward) > 0.98f && (-_player.GlobalBasis.Z).Dot(viewForward) > 0.97f,
            $"in F3 forward moves away from the camera and the body turns to face its motion (moved={moved.Length():0.000} m)");
        // Under the 12 cm deck the camera is held below the deck instead of passing through it.
        Check(_player.TryTeleportTo(new Vector3(0, 0.003f, 1.2f)), "the player can stand under the deck");
        await Frames(60);
        Check(camera.GlobalPosition.Y < 0.12f && Centred(), $"the camera stays under a low ceiling instead of clipping through it (camera y={camera.GlobalPosition.Y:0.000})");
        hud.SetViewMode(1);
        Check(_player.MovementFrameYaw == null && !camera.Current, "leaving F3 returns the body to its own heading and mouse look");
        hud.QueueFree();
        _player.GetNodeOrNull("FollowCameraArm")?.QueueFree();
        camera.GetParent().GetParent().QueueFree();
        _player.Rotation = Vector3.Zero;
        Check(_player.TryTeleportTo(new Vector3(-2, 0.003f, 3.2f)), "back to the companion route after the camera fixture");
        await Frames(10);
    }

    // ---- Jitter: the spike's scenarios, measured on a separate probe body ----

    /// <summary>VerticalSpread and Drift are in body-scale metres; Roughness is the largest and RMS second
    /// difference of height between ticks (body-scale metres), which is zero for smooth motion on a plane.</summary>
    private sealed record JitterMetric(string Name, float FloorRatio, int Transitions, float VerticalSpread, float Drift,
        float RoughnessMax = 0, float RoughnessRms = 0, string Note = "");

    /// <summary>Probe body with every length, speed and acceleration multiplied by Scale (the ×10 import-scale alternative).</summary>
    private partial class ScaledProbe : SmallPlayerController
    {
        public float WorldScale = 1.0f;
        protected override WorldScaleProfile Profile => new(0.10 * WorldScale, 0.02 * WorldScale, 0.087 * WorldScale, 0.15 * WorldScale);

        public override void _Ready()
        {
            ReadKeyboard = false;
            WalkSpeedMps *= WorldScale; RunSpeedMps *= WorldScale;
            GroundAccelerationMps2 *= WorldScale; AirAccelerationMps2 *= WorldScale;
            JumpApexM *= WorldScale; StepHeightM *= WorldScale; FloorSnapM *= WorldScale; SafeMarginM *= WorldScale;
            TerminalFallMps *= WorldScale; MaxCreationSpeedMps *= WorldScale;
            base._Ready();
            ScaleWorldPhysicsForProbe(WorldScale);
        }
    }

    private async Task<List<JitterMetric>> RunJitterSuite(float s, Vector3 origin)
    {
        var area = new Node3D { Name = $"JitterArea_x{s:0}", Position = origin };
        AddChild(area);
        void Solid(Vector3 position, Vector3 size, float angle = 0)
        {
            var body = new StaticBody3D { Position = position * s, Rotation = new Vector3(0, 0, angle), CollisionLayer = 1, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size * s } });
            area.AddChild(body);
        }
        Solid(new Vector3(0, -0.05f, 0), new Vector3(8, 0.1f, 8));
        Solid(new Vector3(0, 0.02f, -1.0f), new Vector3(0.22f, 0.04f, 0.15f));          // the book
        Solid(new Vector3(0, 0.003f, 1.0f), new Vector3(1.6f, 0.006f, 1.1f));           // the rug
        var angles = new[] { 10f, 20f, 30f, 40f };
        for (var i = 0; i < angles.Length; i++)
            Solid(new Vector3(2.0f, 0, -2.0f + i * 0.5f), new Vector3(1.2f, 0.05f, 0.3f), Mathf.DegToRad(angles[i]));
        var probe = new ScaledProbe { WorldScale = s, Name = "Probe", Position = new Vector3(-2, 0.003f, 2) * s };
        area.AddChild(probe);
        await Frames(10);
        var metrics = new List<JitterMetric>();
        Vector3 At(float x, float y, float z) => area.GlobalPosition + new Vector3(x, y, z) * s;

        async Task<JitterMetric> Stand(string name, Vector3 feet, int frames = 120)
        {
            if (!probe.TryTeleportTo(feet)) return new JitterMetric(name, 0, 0, 0, 0, Note: "no supported spawn");
            probe.SetControlInput(Vector2.Zero);
            await Frames(30);
            return await Sample(name, frames, null);
        }

        async Task<JitterMetric> Sample(string name, int frames, System.Action? each)
        {
            var start = probe.GlobalPosition;
            float low = float.MaxValue, high = float.MinValue;
            int floor = 0, transitions = 0;
            var was = probe.IsOnFloor();
            var heights = new List<float>();
            for (var i = 0; i < frames; i++)
            {
                each?.Invoke();
                await Frames(1);
                var on = probe.IsOnFloor();
                if (on) floor++;
                if (on != was) transitions++;
                was = on;
                low = Mathf.Min(low, probe.GlobalPosition.Y);
                high = Mathf.Max(high, probe.GlobalPosition.Y);
                heights.Add(probe.GlobalPosition.Y);
            }
            float roughMax = 0, roughSum = 0;
            for (var i = 1; i + 1 < heights.Count; i++)
            {
                var second = Mathf.Abs(heights[i + 1] - 2 * heights[i] + heights[i - 1]);
                roughMax = Mathf.Max(roughMax, second);
                roughSum += second * second;
            }
            var rms = heights.Count > 2 ? Mathf.Sqrt(roughSum / (heights.Count - 2)) : 0;
            var drift = new Vector2(probe.GlobalPosition.X - start.X, probe.GlobalPosition.Z - start.Z).Length();
            return new JitterMetric(name, floor / (float)frames, transitions, (high - low) / s, drift / s, roughMax / s, rms / s);
        }

        metrics.Add(await Stand("stand_floor", At(-1, 0.003f, 0)));
        metrics.Add(await Stand("stand_rug", At(0, 0.009f, 1.0f)));
        metrics.Add(await Stand("stand_book_top", At(0, 0.043f, -1.0f)));
        metrics.Add(await Stand("stand_book_edge_half_over", At(0.105f, 0.043f, -1.0f)));
        for (var i = 0; i < angles.Length; i++)
        {
            var surface = 0.025f / Mathf.Cos(Mathf.DegToRad(angles[i]));
            metrics.Add(await Stand($"stand_slope_{angles[i]:0}", At(2.0f, surface + 0.01f, -2.0f + i * 0.5f)));
        }
        // Walk across the rug (on and off its 6 mm edges).
        probe.TryTeleportTo(At(-1.2f, 0.003f, 1.0f));
        probe.Rotation = Vector3.Zero;
        await Frames(10);
        metrics.Add(await Sample("walk_across_rug", (int)(2.6f / probe.WalkSpeedMps * s * 60), () => probe.SetControlInput(Vector2.Right)));
        probe.SetControlInput(Vector2.Zero);
        // Push into the book's side for two seconds: horizontal vibration against a wall.
        probe.TryTeleportTo(At(-0.4f, 0.003f, -1.0f));
        await Frames(10);
        await Sample("push_into_book_settle", 60, () => probe.SetControlInput(Vector2.Right));
        var pushStart = probe.GlobalPosition;
        var push = await Sample("push_into_book_side", 120, () => probe.SetControlInput(Vector2.Right));
        metrics.Add(push with { Drift = new Vector2(probe.GlobalPosition.X - pushStart.X, probe.GlobalPosition.Z - pushStart.Z).Length() / s });
        probe.SetControlInput(Vector2.Zero);
        // Each ramp: walk from the floor onto it (crosses the crease), then up and down within the ramp.
        for (var i = 0; i < angles.Length; i++)
        {
            var z = -2.0f + i * 0.5f;
            var tan = Mathf.Tan(Mathf.DegToRad(angles[i]));
            var lift = 0.025f / Mathf.Cos(Mathf.DegToRad(angles[i]));
            probe.TryTeleportTo(At(1.45f, 0.003f, z));
            probe.Rotation = Vector3.Zero;
            await Frames(10);
            var onto = await Sample($"walk_onto_slope_{angles[i]:0}", (int)(0.55f / probe.WalkSpeedMps * s * 60), () => probe.SetControlInput(Vector2.Right));
            metrics.Add(onto with { Note = "includes the floor-to-ramp crease" });
            probe.SetControlInput(Vector2.Zero);
            // Start past the crease for every angle (the steepest ramp leaves the floor at x = 1.96).
            probe.TryTeleportTo(At(1.98f, (1.98f - 2.0f) * tan + lift + 0.005f, z));
            probe.Rotation = Vector3.Zero;
            await Frames(15);
            var up = await Sample($"walk_up_slope_{angles[i]:0}", (int)(0.35f / probe.WalkSpeedMps * s * 60), () => probe.SetControlInput(Vector2.Right));
            var climbed = (probe.GlobalPosition.Y - area.GlobalPosition.Y) / s;
            metrics.Add(up with { Note = $"top at {climbed * 100:0.0} cm" });
            probe.SetControlInput(Vector2.Zero);
            await Frames(10);
            metrics.Add(await Sample($"walk_down_slope_{angles[i]:0}", (int)(0.28f / probe.WalkSpeedMps * s * 60), () => probe.SetControlInput(Vector2.Left)));
            probe.SetControlInput(Vector2.Zero);
        }
        area.QueueFree();
        await Frames(2);
        return metrics;
    }

    private void CheckJitter(List<JitterMetric> metrics)
    {
        var byName = metrics.ToDictionary(m => m.Name);
        foreach (var name in new[] { "stand_floor", "stand_rug", "stand_book_top", "stand_slope_10", "stand_slope_20", "stand_slope_30", "stand_slope_40" })
        {
            var m = byName[name];
            Check(m.FloorRatio >= 0.999f && m.Transitions == 0 && m.VerticalSpread < 0.0002f && m.Drift < 0.0005f,
                $"no jitter standing still: {Describe(m)}");
        }
        var edge = byName["stand_book_edge_half_over"];
        Check(edge.Transitions <= 2 && (edge.Drift < 0.0005f || edge.FloorRatio > 0.8f), $"half over the book edge either holds or slides off once: {Describe(edge)}");
        var rug = byName["walk_across_rug"];
        Check(rug.FloorRatio >= 0.97f && rug.Transitions <= 6 && rug.RoughnessMax < 0.008f, $"walking over the rug edges stays grounded: {Describe(rug)}");
        Check(ProjectSettings.GetSetting("physics/3d/physics_engine").AsString() == "Jolt Physics", "the project runs Jolt Physics, the engine these thresholds were measured on");
        var push = byName["push_into_book_side"];
        Check(push.Drift < 0.0005f && push.VerticalSpread < 0.0005f, $"pushing into a wall does not vibrate: {Describe(push)}");
        foreach (var angle in new[] { "10", "20", "30", "40" })
        {
            var up = byName["walk_up_slope_" + angle];
            var down = byName["walk_down_slope_" + angle];
            // Jolt walks slopes smoothly; Godot Physics stuttered here (RMS 0.57 to 0.81 mm per tick squared on 20 and 30 degrees).
            Check(up.FloorRatio >= 0.95f && up.RoughnessRms < 0.0005f, $"walking up a {angle} degree slope stays grounded and smooth: {Describe(up)}");
            Check(down.FloorRatio >= 0.95f && down.RoughnessRms < 0.0005f, $"walking down a {angle} degree slope stays grounded and smooth: {Describe(down)}");
        }
    }

    private static string Describe(JitterMetric m) => string.Create(CultureInfo.InvariantCulture,
        $"{m.Name} floor={m.FloorRatio:0.000} transitions={m.Transitions} y_spread={m.VerticalSpread * 1000:0.000} mm drift={m.Drift * 1000:0.000} mm roughness max={m.RoughnessMax * 1000:0.000} rms={m.RoughnessRms * 1000:0.0000} mm {m.Note}");

    private static string JitterReport(List<JitterMetric> one, List<JitterMetric> ten)
    {
        var text = new StringBuilder("JITTER_SPIKE engine=" + ProjectSettings.GetSetting("physics/3d/physics_engine") +
            " ticks=" + Engine.PhysicsTicksPerSecond + " (lengths divided by the scale, in body-scale millimetres)\n");
        text.Append("scenario | x1 floor | x1 trans | x1 rough max mm | x1 rough rms mm | x1 y_spread mm | x1 drift mm | x10 floor | x10 trans | x10 rough max mm | x10 rough rms mm | x10 y_spread mm | x10 drift mm\n");
        for (var i = 0; i < one.Count; i++)
        {
            var a = one[i];
            var b = ten[i];
            text.Append(string.Create(CultureInfo.InvariantCulture,
                $"{a.Name} | {a.FloorRatio:0.000} | {a.Transitions} | {a.RoughnessMax * 1000:0.0000} | {a.RoughnessRms * 1000:0.0000} | {a.VerticalSpread * 1000:0.000} | {a.Drift * 1000:0.000} | " +
                $"{b.FloorRatio:0.000} | {b.Transitions} | {b.RoughnessMax * 1000:0.0000} | {b.RoughnessRms * 1000:0.0000} | {b.VerticalSpread * 1000:0.000} | {b.Drift * 1000:0.000} | {a.Note} / {b.Note}\n"));
        }
        return text.ToString();
    }

    // ---- helpers ----

    private static Godot.Collections.Dictionary Preset(string id, int revision) => Physics.Call("preset", id, revision).AsGodotDictionary();

    private bool PlayerVisualFits()
    {
        foreach (var child in _player.GetNode<Node3D>("OriginalPrototypeBody").GetChildren())
        {
            if (child is not MeshInstance3D mesh) continue;
            var bounds = mesh.Transform * mesh.Mesh.GetAabb();
            if (bounds.Position.Y < -0.001f || bounds.End.Y > _player.BodyHeightM + 0.001f ||
                Mathf.Max(Mathf.Abs(bounds.Position.X), Mathf.Abs(bounds.End.X)) > _player.BodyRadiusM + 0.001f ||
                Mathf.Max(Mathf.Abs(bounds.Position.Z), Mathf.Abs(bounds.End.Z)) > _player.BodyRadiusM + 0.001f) return false;
        }
        return true;
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>A check whose label carries a measured value worth keeping in the run's evidence.</summary>
    private void Report(bool condition, string label)
    {
        GD.Print("SMALL_AVATAR_MEASURED " + label);
        Check(condition, label);
    }

    private void Check(bool condition, string label)
    {
        _checks++;
        if (condition) return;
        _failures++;
        GD.PushError("Small avatar: " + label);
    }

    private static float PlanarDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    private void BuildFloorAndObstacles()
    {
        Box(new Vector3(0, -0.05f, 0), new Vector3(10, 0.1f, 12));
        Box(new Vector3(0, 0.0075f, 0), new Vector3(0.7f, 0.015f, 0.4f));            // 1.5 cm curb
        Box(new Vector3(0, 0.015f, -0.6f), new Vector3(0.22f, 0.03f, 0.3f));          // 3 cm barrier
        Box(new Vector3(0, 0.02f, -1.4f), new Vector3(0.22f, 0.04f, 0.3f));           // 4 cm book
        Box(new Vector3(0, 0.003f, -2.2f), new Vector3(1.0f, 0.006f, 0.5f));          // 6 mm rug
        Box(new Vector3(0, 0.14f, 1.2f), new Vector3(0.8f, 0.04f, 0.7f));             // deck, 12 cm clearance
        Box(new Vector3(0, 0.09f, -2.8f), new Vector3(0.8f, 0.02f, 0.5f));            // roof, 8 cm clearance
        Box(new Vector3(12, 0, 0), new Vector3(8, 0.1f, 8), Mathf.DegToRad(30));
        Box(new Vector3(0, 0.12f, -4.5f), new Vector3(3, 0.02f, 0.7f));               // companion's 11 cm passage: no room for a 2 cm step
    }

    private void Box(Vector3 position, Vector3 size, float slope = 0)
    {
        var body = new StaticBody3D { Position = position, Rotation = new Vector3(0, 0, slope), CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size } });
        AddChild(body);
    }
}
