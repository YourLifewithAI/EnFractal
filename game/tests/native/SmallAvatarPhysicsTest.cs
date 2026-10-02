using Godot;
using EnFractal.Native;

namespace EnFractal.Tests;

/// <summary>Actual physics-world checks for the native small-body slice. No maps or saves are changed.</summary>
public partial class SmallAvatarPhysicsTest : Node3D
{
    private int _checks;
    private int _failures;
    private SmallPlayerController _player = null!;
    private CompanionAvatar _companion = null!;

    public override async void _Ready()
    {
        try
        {
            BuildFloorAndObstacles();
            _player = new SmallPlayerController { Name = "SmallPlayer", ReadKeyboard = false, Position = new Vector3(-2, 0.01f, 2) };
            AddChild(_player);
            _companion = new CompanionAvatar { Name = "Companion", Position = new Vector3(2, 0.01f, 3.5f) };
            Check(_companion.ConfigureIdentity("test_companion"), "identity configurable before scene entry");
            AddChild(_companion);
            _companion.BindPlayer(_player);
            await Frames(30);

            Check(Mathf.Abs(_player.BodyHeightM - 0.30f) < 0.00001f, "player collision height is exactly 0.30 m");
            Check(Mathf.Abs(_player.BodyRadiusM - 0.06f) < 0.00001f, "player collision radius is 0.06 m");
            Check(Mathf.Abs(_player.EyeCamera.Position.Y - 0.26f) < 0.00001f, "eye is 0.26 m above feet");
            Check(_player.EyeCamera.Near <= 0.015f && _player.FloorSnapLength < 0.03f, "camera and snap support small geometry");
            Check(_player.IsOnFloor() && Mathf.Abs(_player.GlobalPosition.Y) < 0.015f, "small capsule rests on actual floor");
            Check(!_companion.ConfigureIdentity("replacement"), "identity cannot reset after scene entry");
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
            _companion.SetAppearance(new Color("d087bd"));
            _companion.SetDisplayName("Juniper");
            Check(_companion.CompanionId == "test_companion" && _companion.CompanionName == "Juniper", "appearance/name do not replace companion identity");

            _player.SetControlInput(Vector2.Right);
            var start = _player.GlobalPosition;
            await Frames(60);
            _player.SetControlInput(Vector2.Zero);
            Check(_player.GlobalPosition.X - start.X > 0.65f && _player.GlobalPosition.X - start.X < 1.05f,
                "walking speed is retuned in real metres");
            await Frames(15);
            _player.SetControlInput(Vector2.Zero, true);
            var highest = _player.GlobalPosition.Y;
            for (var i = 0; i < 45; i++) { await Frames(1); highest = Mathf.Max(highest, _player.GlobalPosition.Y); }
            Check(highest > 0.07f && highest < 0.20f, "jump height fits a 0.30 m avatar");
            Check(_player.IsOnFloor(), "jump returns to stable floor");

            _player.SetInputEnabled(false);
            start = _player.GlobalPosition;
            _player.SetControlInput(Vector2.Right, true);
            await Frames(25);
            Check(_player.GlobalPosition.DistanceTo(start) < 0.005f, "disabled input blocks movement and jump immediately");
            _player.SetInputEnabled(true);
            _player.SetControlInput(new Vector2(float.NaN, 1));
            await Frames(3);
            Check(_player.GlobalPosition.IsFinite(), "nonfinite control input is rejected");

            Check(_player.TryTeleportTo(new Vector3(-1, 0.01f, 0)), "curb route has supported spawn");
            _player.Rotation = Vector3.Zero;
            _player.SetControlInput(Vector2.Right);
            highest = 0;
            for (var i = 0; i < 140; i++) { await Frames(1); highest = Mathf.Max(highest, _player.GlobalPosition.Y); }
            _player.SetControlInput(Vector2.Zero);
            Check(_player.GlobalPosition.X > 0.50f, $"crosses a 0.04 m curb without a jump (position={_player.GlobalPosition}, floor={_player.IsOnFloor()})");
            Check(highest > 0.035f && highest < 0.08f && _player.StepsClimbed > 0, $"curb traversal uses bounded step lift (height={highest}, steps={_player.StepsClimbed})");
            await Frames(15);

            Check(_player.TryTeleportTo(new Vector3(-1, 0.01f, -1.4f)), "high-curb route has supported spawn");
            _player.SetControlInput(Vector2.Right);
            await Frames(95);
            _player.SetControlInput(Vector2.Zero);
            Check(_player.GlobalPosition.X < -0.37f && _player.GlobalPosition.Y < 0.025f,
                "0.12 m barrier cannot masquerade as a small step");

            Check(_player.TryTeleportTo(new Vector3(-1, 0.01f, 1.2f)), "underpass route has supported spawn");
            _player.SetControlInput(Vector2.Right);
            await Frames(140);
            _player.SetControlInput(Vector2.Zero);
            Check(_player.GlobalPosition.X > 0.65f && _player.GlobalPosition.Y < 0.02f, "passes beneath a 0.35 m clearance deck");
            Check(_player.TryTeleportTo(new Vector3(0, 0.445f, 1.2f)), "can explicitly recover onto bridge deck");
            Check(Mathf.Abs(_player.GlobalPosition.Y - 0.433f) < 0.01f, "deck uses structural collider, not floor height");
            Check(_player.TryTeleportTo(new Vector3(0, 0.01f, 1.2f)), "can explicitly recover below the same deck");
            Check(_player.GlobalPosition.Y < 0.02f, "underpass recovery does not snap to deck overhead");
            start = _player.GlobalPosition;
            Check(!_player.TryTeleportTo(new Vector3(0, 0.01f, -2.8f)), "0.24 m head clearance rejects a 0.30 m body");
            Check(_player.GlobalPosition == start, "rejected recovery does not move the player");
            Check(_player.TryTeleportTo(new Vector3(-2, 0.01f, 3.2f)), "recovery checkpoint available");
            _player.SetSpawnPoint(_player.GlobalPosition);
            _player.GlobalPosition = new Vector3(100, -20, 100);
            Check(_player.Recover() && _player.GlobalPosition.DistanceTo(new Vector3(-2, 0.003f, 3.2f)) < 0.02f,
                "recovery restores a checked supported point");
            await Frames(10);

            Check(_player.TryTeleportTo(new Vector3(12, 0.10f, 0)), "recovery admits the full capsule on a 30 degree slope");
            await Frames(40);
            Check(_player.IsOnFloor() && Mathf.Abs(_player.GlobalPosition.X - 12) < 0.05f,
                "slope recovery settles without penetration or slide");
            var slopeCheckpoint = _player.GlobalPosition;
            _player.GlobalPosition = new Vector3(100, -20, 100);
            Check(_player.Recover() && _player.GlobalPosition.DistanceTo(slopeCheckpoint) < 0.015f,
                "recovery retains the last safe slope instead of falling back to the flat spawn");
            Check(_player.TryTeleportTo(new Vector3(-2, 0.01f, 3.2f)), "returns from slope fixture to companion route");
            await Frames(10);

            Check(_companion.TryTeleportTo(new Vector3(0.5f, 0.01f, 3.2f)), "companion gets its own clear spawn");
            _companion.Follow();
            await Frames(240);
            var followTarget = _player.GlobalPosition + _player.GlobalBasis.Z * 0.55f + _player.GlobalBasis.X * 0.22f;
            Check(PlanarDistance(_companion.GlobalPosition, followTarget) < 0.25f, "follow reaches offset without occupying the player");
            Check(PlanarDistance(_companion.GlobalPosition, _player.GlobalPosition) > 0.22f, "follow retains personal space");
            _companion.Come();
            await Frames(80);
            Check(_companion.CurrentIntent == "stay" && PlanarDistance(_companion.GlobalPosition, _player.GlobalPosition) < 0.39f,
                "come arrives near player then stays");
            _companion.Follow();
            await Frames(5);
            _companion.Stop();
            start = _companion.GlobalPosition;
            await Frames(35);
            Check(PlanarDistance(_companion.GlobalPosition, start) < 0.005f && _companion.CurrentIntent == "stop",
                "stop removes ongoing navigation immediately");
            Check(_companion.TryTeleportTo(_player.GlobalPosition + Vector3.Right * 0.15f), "stop fixture places companion within yielding distance");
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
            Check(_player.GlobalPosition.X - start.X > 0.70f, "companion cannot trap or block direct player movement");
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

            Check(_player.TryTeleportTo(new Vector3(2, 0.01f, -4.5f)), "low-passage destination admits player outside the roof");
            Check(_companion.TryTeleportTo(new Vector3(-1, 0.01f, -4.5f)), "0.26 m passage admits the 0.24 m companion body");
            await Frames(5);
            _companion.Come();
            blocked = false;
            for (var i = 0; i < 260; i++) { await Frames(1); blocked |= _companion.GoalBlocked; }
            Check(_companion.GlobalPosition.X > 1.6f && _companion.CurrentIntent == "stay" && !blocked,
                "companion crosses a flat low passage without requiring step headroom");

            Check(_player.TryTeleportTo(new Vector3(-4, 0.01f, 3.2f)) &&
                _companion.TryTeleportTo(new Vector3(-4.8f, 0.01f, 3.42f)), "moving follow fixture admits both bodies");
            _player.Rotation = new Vector3(0, -Mathf.Pi * 0.5f, 0);
            _companion.Follow();
            _player.SetControlInput(new Vector2(0, 1));
            var maximumGap = 0.0f;
            for (var i = 0; i < 500; i++)
            {
                await Frames(1);
                maximumGap = Mathf.Max(maximumGap, PlanarDistance(_player.Position, _companion.Position));
            }
            _player.SetControlInput(Vector2.Zero);
            Check(maximumGap < 1.5f && PlanarDistance(_player.Position, _companion.Position) < 1.45f,
                "companion catches up during sustained player walking instead of falling farther behind");
            GD.Print($"NATIVE_SMALL_AVATAR: {_checks - _failures}/{_checks} checks passed; procedural body/steering fixture, not final art or AI integration");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (System.Exception exception)
        {
            GD.PushError("Native small-avatar test exception: " + exception);
            GetTree().Quit(1);
        }
    }

    private async System.Threading.Tasks.Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
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
        Box(new Vector3(0, 0.02f, 0), new Vector3(0.7f, 0.04f, 0.7f));
        Box(new Vector3(0, 0.06f, -1.4f), new Vector3(0.7f, 0.12f, 0.7f));
        Box(new Vector3(0, 0.39f, 1.2f), new Vector3(0.8f, 0.08f, 0.7f));
        Box(new Vector3(0, 0.28f, -2.8f), new Vector3(0.8f, 0.08f, 0.7f));
        Box(new Vector3(12, 0, 0), new Vector3(8, 0.1f, 8), Mathf.DegToRad(30));
        Box(new Vector3(0, 0.27f, -4.5f), new Vector3(3, 0.02f, 0.7f));
    }

    private void Box(Vector3 position, Vector3 size, float slope = 0)
    {
        var body = new StaticBody3D { Position = position, Rotation = new Vector3(0, 0, slope), CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size } });
        AddChild(body);
    }
}
