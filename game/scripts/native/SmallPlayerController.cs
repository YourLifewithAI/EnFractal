using Godot;

namespace EnFractal.Native;

/// <summary>
/// Native small-body prototype. The root is at the feet; geography stays in metres.
/// The procedural body is an original blockout, not accepted character art.
/// </summary>
[GlobalClass]
public partial class SmallPlayerController : CharacterBody3D
{
    [Export] public bool ReadKeyboard { get; set; } = true;
    [Export] public float WalkSpeedMps { get; set; } = 0.9f;
    [Export] public float RunSpeedMps { get; set; } = 1.5f;
    [Export] public float GravityMps2 { get; set; } = 9.8f;
    [Export] public float JumpSpeedMps { get; set; } = 1.55f;
    [Export] public float StepHeightM { get; set; } = 0.045f;

    public bool InputEnabled { get; private set; } = true;
    public Camera3D EyeCamera { get; private set; } = null!;
    public float BodyHeightM => (float)Profile.HeightMeters;
    public float BodyRadiusM => (float)Profile.RadiusMeters;
    public int StepsClimbed { get; private set; }
    public bool IsBlocked { get; private set; }
    public Color AppearanceColor { get; private set; } = new("d28f63");
    public Vector3 LastSafePosition { get; private set; }

    protected virtual WorldScaleProfile Profile => WorldScaleProfile.SmallPlayer;
    protected Node3D VisualRoot = null!;
    protected StandardMaterial3D BodyMaterial = null!;
    protected bool HasSafePosition;

    private CapsuleShape3D _capsule = null!;
    private Vector2 _control;
    private bool _jumpRequested;
    private bool _sprint;
    private float _pitch;
    private Vector3 _spawnPoint;
    private bool _ready;
    private readonly KinematicCollision3D _stepContact = new();

    public override void _Ready()
    {
        MotionMode = MotionModeEnum.Grounded;
        CollisionLayer = 2;
        // A companion must never become an obstacle trapping direct player control.
        CollisionMask = 1;
        SafeMargin = 0.001f;
        FloorSnapLength = 0.025f;
        FloorMaxAngle = Mathf.DegToRad(45.0f);
        FloorStopOnSlope = true;
        FloorConstantSpeed = true;
        _capsule = new CapsuleShape3D { Height = BodyHeightM, Radius = BodyRadiusM };
        AddChild(new CollisionShape3D
        {
            Name = "SmallBodyCollision", Shape = _capsule,
            Position = Vector3.Up * (BodyHeightM * 0.5f)
        });
        EyeCamera = new Camera3D
        {
            Name = "EyeCamera", Position = Vector3.Up * (float)Profile.EyeHeightMeters,
            Near = 0.01f, Far = 2500.0f, Fov = 72.0f, Current = false
        };
        AddChild(EyeCamera);
        BuildVisual();
        _spawnPoint = GlobalPosition;
        LastSafePosition = _spawnPoint;
        _ready = true;
    }

    protected virtual void BuildVisual()
    {
        VisualRoot = new Node3D { Name = "OriginalPrototypeBody" };
        AddChild(VisualRoot);
        BodyMaterial = new StandardMaterial3D { AlbedoColor = AppearanceColor, Roughness = 0.9f };
        AddMesh(VisualRoot, new CapsuleMesh
        {
            Radius = BodyRadiusM * 0.82f, Height = BodyHeightM * 0.72f,
            RadialSegments = 12, Rings = 4
        }, new Vector3(0, BodyHeightM * 0.39f, 0), BodyMaterial);
        var face = new StandardMaterial3D { AlbedoColor = new Color("eed5a3"), Roughness = 1 };
        AddMesh(VisualRoot, new SphereMesh
        {
            Radius = BodyRadiusM * 0.82f, Height = BodyRadiusM * 1.64f,
            RadialSegments = 12, Rings = 6
        }, new Vector3(0, BodyHeightM * 0.81f, 0), face);
        var dark = new StandardMaterial3D { AlbedoColor = new Color("183631"), Roughness = 1 };
        foreach (var side in new[] { -1.0f, 1.0f })
            AddMesh(VisualRoot, new SphereMesh { Radius = 0.007f, Height = 0.014f, RadialSegments = 8, Rings = 4 },
                new Vector3(side * 0.020f, BodyHeightM * 0.83f, -BodyRadiusM * 0.72f), dark);
    }

    protected static MeshInstance3D AddMesh(Node3D parent, Mesh mesh, Vector3 position, Material material)
    {
        var instance = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = material };
        parent.AddChild(instance);
        return instance;
    }

    public void SetAppearance(Color color)
    {
        if (!float.IsFinite(color.R) || !float.IsFinite(color.G) || !float.IsFinite(color.B)) return;
        AppearanceColor = new Color(Mathf.Clamp(color.R, 0, 1), Mathf.Clamp(color.G, 0, 1), Mathf.Clamp(color.B, 0, 1), 1);
        if (BodyMaterial != null) BodyMaterial.AlbedoColor = AppearanceColor;
    }

    /// <summary>Right and forward input, clamped to the unit disc. Retained until replaced.</summary>
    public void SetControlInput(Vector2 rightForward, bool jump = false, bool sprint = false)
    {
        _control = rightForward.IsFinite() ? rightForward.LimitLength() : Vector2.Zero;
        _jumpRequested = InputEnabled && jump;
        _sprint = sprint;
    }

    public void SetInputEnabled(bool enabled)
    {
        InputEnabled = enabled;
        _control = Vector2.Zero;
        _jumpRequested = false;
        Velocity = new Vector3(0, Velocity.Y, 0);
    }

    public void SetSpawnPoint(Vector3 feetPosition)
    {
        if (feetPosition.IsFinite()) _spawnPoint = feetPosition;
    }

    /// <summary>Only teleport to a supported, unoccupied surface near the supplied foot height.</summary>
    public bool TryTeleportTo(Vector3 feetPosition)
    {
        if (!_ready || !feetPosition.IsFinite() || !FindSupportedPosition(feetPosition, out var position)) return false;
        GlobalPosition = position;
        Velocity = Vector3.Zero;
        _jumpRequested = false;
        LastSafePosition = position;
        HasSafePosition = true;
        return true;
    }

    public bool Recover()
    {
        SetControlInput(Vector2.Zero);
        Velocity = Vector3.Zero;
        if (HasSafePosition && TryTeleportTo(LastSafePosition)) return true;
        if (TryTeleportTo(_spawnPoint)) return true;
        foreach (var offset in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back })
            if (TryTeleportTo(_spawnPoint + offset * 0.20f)) return true;
        return false;
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (!InputEnabled || !ReadKeyboard) return;
        if (input is InputEventMouseMotion mouse && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-mouse.Relative.X * 0.0025f);
            _pitch = Mathf.Clamp(_pitch - mouse.Relative.Y * 0.0025f, -1.35f, 1.35f);
            EyeCamera.Rotation = new Vector3(_pitch, 0, 0);
        }
        if (input is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.Space || key.Keycode == Key.Space) _jumpRequested = true;
            if (key.PhysicalKeycode == Key.R || key.Keycode == Key.R) Recover();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_ready) return;
        var dt = Mathf.Clamp((float)delta, 0, 0.05f);
        var control = InputEnabled ? _control : Vector2.Zero;
        var sprint = _sprint;
        if (InputEnabled && ReadKeyboard)
        {
            control = new Vector2(
                (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0),
                (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0)).LimitLength();
            sprint = Input.IsPhysicalKeyPressed(Key.Shift);
        }
        var local = new Vector3(control.X, 0, -control.Y);
        var wish = GlobalBasis * local;
        wish.Y = 0;
        var desired = wish * (sprint ? RunSpeedMps : WalkSpeedMps);
        var acceleration = IsOnFloor() ? 9.0f : 3.0f;
        var horizontal = new Vector3(Velocity.X, 0, Velocity.Z).MoveToward(desired, acceleration * dt);
        var vertical = IsOnFloor() ? Mathf.Min(0, Velocity.Y) : Mathf.Max(Velocity.Y - GravityMps2 * dt, -8.0f);
        if (_jumpRequested && InputEnabled && IsOnFloor()) vertical = JumpSpeedMps;
        _jumpRequested = false;
        Velocity = new Vector3(horizontal.X, vertical, horizontal.Z);
        var before = GlobalPosition;
        if (IsOnFloor() && vertical <= 0 && horizontal.LengthSquared() > 0.000001f && TryStep(horizontal * dt))
        {
            // The step already includes the horizontal movement for this tick.
            Velocity = Vector3.Down * 0.05f;
            MoveAndSlide();
            Velocity = new Vector3(horizontal.X, Velocity.Y, horizontal.Z);
        }
        else MoveAndSlide();
        var actual = GlobalPosition - before;
        actual.Y = 0;
        IsBlocked = control.LengthSquared() > 0.1f && actual.LengthSquared() < 0.0000001f;
        if (IsOnFloor() && Mathf.Abs(Velocity.Y) < 0.1f)
        {
            LastSafePosition = GlobalPosition;
            HasSafePosition = true;
        }
        if (!GlobalPosition.IsFinite() || GlobalPosition.Y < _spawnPoint.Y - 12.0f) Recover();
        VisualRoot.Visible = !EyeCamera.Current;
    }

    protected bool HasSupportNear(Vector3 position, float maximumDrop = 0.12f)
    {
        var query = PhysicsRayQueryParameters3D.Create(position + Vector3.Up * 0.06f,
            position - Vector3.Up * maximumDrop, 1);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 && hit["normal"].AsVector3().Y >= Mathf.Cos(FloorMaxAngle);
    }

    private bool FindSupportedPosition(Vector3 requested, out Vector3 result)
    {
        result = requested;
        var ray = PhysicsRayQueryParameters3D.Create(requested + Vector3.Up * 0.10f,
            requested - Vector3.Up * 0.50f, 1);
        ray.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (hit.Count == 0 || hit["normal"].AsVector3().Y < Mathf.Cos(FloorMaxAngle)) return false;
        // A vertical capsule touches a slope off its centre ray. Raise its lower
        // hemisphere enough to clear the support plane, then check the entire
        // body against nearby geometry (including roofs and uneven terrain).
        var supportNormal = hit["normal"].AsVector3();
        var slopeClearance = BodyRadiusM * (1.0f / supportNormal.Y - 1.0f);
        result = hit["position"].AsVector3() + Vector3.Up * (slopeClearance + 0.003f);
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = _capsule, CollisionMask = CollisionMask,
            Transform = new Transform3D(Basis.Identity, result + Vector3.Up * (BodyHeightM * 0.5f)),
            Margin = 0.0005f, Exclude = new Godot.Collections.Array<Rid> { GetRid() }
        };
        return GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0;
    }

    private bool TryStep(Vector3 horizontalMotion)
    {
        if (StepHeightM <= 0 || StepHeightM > BodyHeightM * 0.25f ||
            !TestMove(GlobalTransform, horizontalMotion, _stepContact, SafeMargin) ||
            _stepContact.GetNormal().Y >= Mathf.Cos(FloorMaxAngle)) return false;
        var lift = Vector3.Up * StepHeightM;
        if (TestMove(GlobalTransform, lift, null, SafeMargin)) return false;
        var raised = GlobalTransform;
        raised.Origin += lift;
        // A stopped capsule's next acceleration step can be only millimetres.
        // Probe onto the tread beyond the rounded toe, but never teleport that
        // look-ahead distance: actual horizontal travel stays at this tick's input.
        var treadProbe = horizontalMotion + horizontalMotion.Normalized() * BodyRadiusM;
        if (TestMove(raised, treadProbe, null, SafeMargin)) return false;
        raised.Origin += treadProbe;
        if (!TestMove(raised, Vector3.Down * (StepHeightM + FloorSnapLength), _stepContact, SafeMargin) ||
            _stepContact.GetNormal().Y < Mathf.Cos(FloorMaxAngle)) return false;
        var tread = raised.Origin + _stepContact.GetTravel();
        var rise = tread.Y - GlobalPosition.Y;
        if (rise <= 0.003f || rise > StepHeightM + SafeMargin) return false;
        GlobalPosition += horizontalMotion + Vector3.Up * rise;
        StepsClimbed++;
        return true;
    }
}
