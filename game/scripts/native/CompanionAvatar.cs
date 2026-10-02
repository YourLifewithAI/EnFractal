using Godot;

namespace EnFractal.Native;

/// <summary>
/// Deterministic, game-only companion body. Commands are local trusted calls;
/// this is not an AI adapter or an authorization service. Its original geometric
/// appearance and separate 0.24 m fixture profile do not settle final companion art/size.
/// </summary>
[GlobalClass]
public partial class CompanionAvatar : SmallPlayerController
{
    public string CompanionId { get; private set; } = "local_companion";
    public string CompanionName { get; private set; } = "Wisp";
    public string CurrentIntent { get; private set; } = "stay";
    public bool GoalBlocked { get; private set; }
    public bool IsPointing => _pointer != null && _pointer.Visible;
    protected override WorldScaleProfile Profile => new(0.24, 0.055, 0.205, 0.40);

    private SmallPlayerController? _player;
    private Node3D _pointer = null!;
    private Label3D _label = null!;
    private Vector3 _lookTarget;
    private bool _hasLookTarget;
    private bool _entered;

    public override void _Ready()
    {
        ReadKeyboard = false;
        WalkSpeedMps = 0.80f;
        RunSpeedMps = 1.65f;
        StepHeightM = 0.04f;
        SetAppearance(new Color("65b9b0"));
        base._Ready();
        CollisionLayer = 4;
        CollisionMask = 1 | 2;
        _entered = true;
        _label = new Label3D
        {
            Name = "CompanionLabel", Text = CompanionName + " · companion",
            Position = Vector3.Up * 0.33f, FontSize = 30, PixelSize = 0.0012f,
            Modulate = new Color("f6dfab"), OutlineModulate = new Color("18332d"),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = false
        };
        AddChild(_label);
        _pointer = new Node3D { Name = "PointingCue", Position = Vector3.Up * 0.16f, Visible = false };
        AddChild(_pointer);
        var gold = new StandardMaterial3D { AlbedoColor = new Color("edc06e"), Roughness = 0.8f };
        var shaft = AddMesh(_pointer, new CylinderMesh { TopRadius = 0.005f, BottomRadius = 0.005f, Height = 0.17f, RadialSegments = 8 },
            new Vector3(0, 0, -0.09f), gold);
        shaft.Rotation = new Vector3(-Mathf.Pi * 0.5f, 0, 0);
        var tip = AddMesh(_pointer, new CylinderMesh { TopRadius = 0, BottomRadius = 0.019f, Height = 0.04f, RadialSegments = 8 },
            new Vector3(0, 0, -0.195f), gold);
        tip.Rotation = new Vector3(-Mathf.Pi * 0.5f, 0, 0);
    }

    protected override void BuildVisual()
    {
        base.BuildVisual();
        var trim = new StandardMaterial3D { AlbedoColor = new Color("eec471"), Roughness = 0.9f };
        AddMesh(VisualRoot, new CylinderMesh
        {
            TopRadius = 0, BottomRadius = BodyRadiusM, Height = 0.075f, RadialSegments = 12
        }, new Vector3(0, BodyHeightM - 0.0375f, 0), trim);
    }

    public bool ConfigureIdentity(string id)
    {
        if (_entered || string.IsNullOrWhiteSpace(id) || id.Length > 64) return false;
        foreach (var c in id)
            if (!char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-') return false;
        CompanionId = id;
        return true;
    }

    public void SetDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 40) return;
        foreach (var c in name) if (char.IsControl(c)) return;
        CompanionName = name.Trim();
        if (_label != null) _label.Text = CompanionName + " · companion";
    }

    public void BindPlayer(SmallPlayerController player) => _player = player;
    public void Follow() => BeginIntent("follow");
    public void Stay() => BeginIntent("stay");
    public void Come() => BeginIntent("come");
    public void Stop() => BeginIntent("stop");

    private void BeginIntent(string intent)
    {
        CurrentIntent = intent;
        GoalBlocked = false;
        _hasLookTarget = false;
        if (_pointer != null) _pointer.Visible = false;
        SetControlInput(Vector2.Zero);
        Velocity = new Vector3(0, Velocity.Y, 0);
    }

    public void LookAtPoint(Vector3 point)
    {
        if (!point.IsFinite()) return;
        BeginIntent("look");
        _lookTarget = point;
        _hasLookTarget = true;
    }

    public void PointAt(Vector3 point)
    {
        if (!point.IsFinite()) return;
        LookAtPoint(point);
        CurrentIntent = "point";
        if (_pointer != null) _pointer.Visible = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_entered) return;
        GoalBlocked = false;
        var heading = Vector3.Zero;
        var catchUp = false;
        if (InputEnabled && _player != null && GodotObject.IsInstanceValid(_player))
        {
            var playerOffset = GlobalPosition - _player.GlobalPosition;
            playerOffset.Y = 0;
            // Stay may yield, but an explicit Stop cancels navigation. The player
            // can still pass because its collision mask excludes the companion.
            if (CurrentIntent != "stop" && playerOffset.Length() < 0.20f)
            {
                heading = playerOffset.LengthSquared() < 0.0001f ? GlobalBasis.X : playerOffset.Normalized();
            }
            else if (CurrentIntent is "follow" or "come")
            {
                var target = _player.GlobalPosition;
                if (CurrentIntent == "follow")
                    target += _player.GlobalBasis.Z * 0.55f + _player.GlobalBasis.X * 0.22f;
                var difference = target - GlobalPosition;
                difference.Y = 0;
                var arrival = CurrentIntent == "come" ? 0.32f : 0.10f;
                if (difference.Length() > arrival)
                {
                    heading = difference.Normalized();
                    catchUp = difference.Length() > 0.65f;
                }
                else if (CurrentIntent == "come") Stay();
            }
        }
        if (heading.LengthSquared() > 0)
        {
            heading = ChooseClearDirection(heading);
            if (heading.LengthSquared() > 0)
            {
                var yaw = Mathf.Atan2(-heading.X, -heading.Z);
                Rotation = new Vector3(0, yaw, 0);
                SetControlInput(new Vector2(0, 1), sprint: catchUp);
            }
            else
            {
                GoalBlocked = true;
                SetControlInput(Vector2.Zero);
                Velocity = new Vector3(0, Velocity.Y, 0);
            }
        }
        else SetControlInput(Vector2.Zero);
        if (_hasLookTarget)
        {
            var direction = _lookTarget - GlobalPosition;
            direction.Y = 0;
            if (direction.LengthSquared() > 0.001f)
                Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(-direction.X, -direction.Z), Mathf.Min(1, (float)delta * 10)), 0);
            if (_pointer.Visible && _pointer.GlobalPosition.DistanceSquaredTo(_lookTarget) > 0.0001f)
            {
                var toTarget = (_lookTarget - _pointer.GlobalPosition).Normalized();
                if (Mathf.Abs(toTarget.Dot(Vector3.Up)) < 0.99f) _pointer.LookAt(_lookTarget, Vector3.Up);
            }
        }
        base._PhysicsProcess(delta);
    }

    private Vector3 ChooseClearDirection(Vector3 desired)
    {
        // Local steering only: blocked routes report blocked rather than teleport.
        // A future navigation system can replace this without moving inference into physics.
        foreach (var angle in new[] { 0.0f, -0.65f, 0.65f, -1.15f, 1.15f })
        {
            var candidate = desired.Rotated(Vector3.Up, angle);
            var probe = candidate * 0.14f;
            if (!HasSupportNear(GlobalPosition + probe)) continue;
            // Flat travel does not require the extra headroom used for a step.
            if (!TestMove(GlobalTransform, probe, null, SafeMargin)) return candidate;
            var raised = GlobalTransform;
            raised.Origin += Vector3.Up * StepHeightM;
            if (TestMove(raised, probe, null, SafeMargin)) continue;
            return candidate;
        }
        return Vector3.Zero;
    }
}
