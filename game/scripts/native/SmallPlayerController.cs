using Godot;
using Godot.Collections;

namespace EnFractal.Native;

/// <summary>
/// The player's 10 cm body. The root is at the feet; the room stays in real metres. Body properties
/// (size, speeds, step, jump height) live here; gravity and wind are world properties from
/// scripts/world_physics_profile.gd, so a gravity change alters how long a jump lasts, not how high.
/// Creation effects (friendly wind, worn gliders) arrive through SetCreationEffects from the
/// invention runtime; this body never decides on its own that an effect is allowed.
/// The procedural body is an original blockout, not accepted character art.
/// </summary>
[GlobalClass]
public partial class SmallPlayerController : CharacterBody3D
{
    public const string WorldPhysicsScript = "res://scripts/world_physics_profile.gd";

    [Export] public bool ReadKeyboard { get; set; } = true;
    [Export] public float WalkSpeedMps { get; set; } = 0.32f;
    /// <summary>Three times the walk (founder playtest, 6 October: 0.60 m/s did not feel like a change of pace).</summary>
    [Export] public float RunSpeedMps { get; set; } = 0.96f;
    /// <summary>Walk speed in about 0.05 s, the run in 0.16 s, and a stop from a run in 0.16 s (7.7 cm).</summary>
    [Export] public float GroundAccelerationMps2 { get; set; } = 6.0f;
    /// <summary>Scaled by the world profile's air_control (more in floaty gravity).</summary>
    [Export] public float AirAccelerationMps2 { get; set; } = 2.0f;
    /// <summary>How fast the body turns to face its motion when movement follows a free camera (F3).</summary>
    [Export] public float TurnRateRadPerS { get; set; } = 10.0f;
    /// <summary>Jump apex above the take-off floor. Clears the 4 cm book with room to spare.</summary>
    [Export] public float JumpApexM { get; set; } = 0.065f;
    [Export] public float StepHeightM { get; set; } = 0.02f;
    [Export] public float FloorSnapM { get; set; } = 0.015f;
    [Export] public float SafeMarginM { get; set; } = 0.001f;
    [Export] public float TerminalFallMps { get; set; } = 6.0f;
    /// <summary>A jump pressed this long before landing still happens.</summary>
    [Export] public float JumpBufferS { get; set; } = 0.10f;
    /// <summary>A jump pressed this long after walking off an edge still happens.</summary>
    [Export] public float CoyoteTimeS { get; set; } = 0.08f;
    /// <summary>Creation-induced speed is capped relative to the body, not at human scale.</summary>
    [Export] public float MaxCreationSpeedMps { get; set; } = 1.2f;
    public const float MaxCreationAccelerationMps2 = 4.0f;
    public const float MaxGlideLimitMps = 7.0f;

    public float GravityMps2 { get; private set; } = 3.5f;
    /// <summary>The world's fall limit (thick floaty air falls slower); the body's own TerminalFallMps still caps it.</summary>
    public float WorldTerminalFallMps { get; private set; } = 6.0f;
    /// <summary>The world's multiplier on the body's air acceleration.</summary>
    public float AirControl { get; private set; } = 1.0f;
    public float EffectiveTerminalFallMps => Mathf.Min(TerminalFallMps, WorldTerminalFallMps);
    public Vector2 WindMps { get; private set; }
    public string WorldPhysicsId { get; private set; } = "room_tuned";
    public int WorldPhysicsRevision { get; private set; } = -1;
    /// <summary>
    /// Take-off speed whose apex, integrated at the physics tick, is JumpApexM: the discrete apex is
    /// v^2/2g + v*dt/2, so the jump is equally high under every gravity preset.
    /// </summary>
    public float JumpSpeedMps
    {
        get
        {
            var half = GravityMps2 * 0.5f / Engine.PhysicsTicksPerSecond;
            return -half + Mathf.Sqrt(half * half + 2.0f * GravityMps2 * JumpApexM);
        }
    }

    public bool InputEnabled { get; private set; } = true;
    /// <summary>
    /// Set while a free camera (the F3 diorama view) owns the mouse: movement input is relative to this
    /// heading instead of the body's, the body turns to face where it moves, and mouse look is off.
    /// </summary>
    public float? MovementFrameYaw { get; private set; }
    public Camera3D EyeCamera { get; private set; } = null!;
    public float BodyHeightM => (float)Profile.HeightMeters;
    public float BodyRadiusM => (float)Profile.RadiusMeters;
    public float ReachM => (float)Profile.InteractionReachMeters;
    public int StepsClimbed { get; private set; }
    public int JumpsStarted { get; private set; }
    public bool IsBlocked { get; private set; }
    public Color AppearanceColor { get; private set; } = new("d28f63");
    public Vector3 LastSafePosition { get; private set; }
    public Vector3 CreationVelocity => _creationVelocity;
    public bool HasCreationGuard { get; private set; }

    protected virtual WorldScaleProfile Profile => WorldScaleProfile.SmallPlayer;
    protected Node3D VisualRoot = null!;
    protected StandardMaterial3D BodyMaterial = null!;
    protected bool HasSafePosition;

    private CapsuleShape3D _capsule = null!;
    private Vector2 _control;
    private float _jumpBuffer;
    private float _coyote;
    private bool _sprint;
    private float _pitch;
    private Vector3 _spawnPoint;
    private bool _ready;
    private readonly KinematicCollision3D _stepContact = new();
    private Vector3 _creationAcceleration;
    private Vector3 _creationVelocity;
    private Vector3 _creationApplied;
    private float _creationGlideLimit;
    private Callable _creationGuard;
    private Dictionary _worldPhysics = new();

    public override void _Ready()
    {
        MotionMode = MotionModeEnum.Grounded;
        CollisionLayer = 2;
        // A companion must never become an obstacle trapping direct player control.
        CollisionMask = 1;
        SafeMargin = SafeMarginM;
        FloorSnapLength = FloorSnapM;
        FloorMaxAngle = Mathf.DegToRad(45.0f);
        FloorStopOnSlope = true;
        FloorConstantSpeed = true;
        _capsule = new CapsuleShape3D { Height = BodyHeightM, Radius = BodyRadiusM };
        AddChild(new CollisionShape3D
        {
            Name = "SmallBodyCollision", Shape = _capsule,
            Position = Vector3.Up * (BodyHeightM * 0.5f)
        });
        // Near plane well inside the capsule radius so a wall a body-width away never clips; a room-scale
        // far plane keeps depth precision for the Compatibility renderer.
        EyeCamera = new Camera3D
        {
            Name = "EyeCamera", Position = Vector3.Up * (float)Profile.EyeHeightMeters,
            Near = Mathf.Min(0.01f, BodyRadiusM * 0.25f), Far = 100.0f, Fov = 72.0f, Current = false
        };
        AddChild(EyeCamera);
        BuildVisual();
        if (WorldPhysicsRevision < 0) ApplyWorldPhysics(DefaultWorldPhysics());
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
        var eye = BodyRadiusM * 0.117f;
        foreach (var side in new[] { -1.0f, 1.0f })
            AddMesh(VisualRoot, new SphereMesh { Radius = eye, Height = eye * 2, RadialSegments = 8, Rings = 4 },
                new Vector3(side * BodyRadiusM * 0.333f, BodyHeightM * 0.83f, -BodyRadiusM * 0.72f), dark);
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

    /// <summary>Right and forward input, clamped to the unit disc. Retained until replaced. A jump request is buffered briefly.</summary>
    public void SetControlInput(Vector2 rightForward, bool jump = false, bool sprint = false)
    {
        _control = rightForward.IsFinite() ? rightForward.LimitLength() : Vector2.Zero;
        if (InputEnabled && jump) _jumpBuffer = JumpBufferS;
        _sprint = sprint;
    }

    public void SetInputEnabled(bool enabled)
    {
        InputEnabled = enabled;
        _control = Vector2.Zero;
        _jumpBuffer = 0;
        Velocity = new Vector3(0, Velocity.Y, 0);
    }

    public void SetSpawnPoint(Vector3 feetPosition)
    {
        if (feetPosition.IsFinite()) _spawnPoint = feetPosition;
    }

    /// <summary>The trusted host's gravity and wind. Accepts only a valid profile with a newer revision.</summary>
    public bool SetWorldPhysics(Dictionary profile)
    {
        var script = GD.Load<GDScript>(WorldPhysicsScript);
        if (!script.Call("validate", profile).AsBool() || profile["revision"].AsInt32() <= WorldPhysicsRevision) return false;
        ApplyWorldPhysics(profile);
        return true;
    }

    /// <summary>Playtest seam: switch to the next gravity preset (G key). Returns the new preset id.</summary>
    public string CycleWorldPhysics()
    {
        var script = GD.Load<GDScript>(WorldPhysicsScript);
        var ids = script.GetScriptConstantMap()["PRESET_IDS"].AsGodotArray();
        var next = 0;
        for (var index = 0; index < ids.Count; index++)
            if (ids[index].AsString() == WorldPhysicsId) next = (index + 1) % ids.Count;
        var profile = script.Call("preset", ids[next], WorldPhysicsRevision + 1).AsGodotDictionary();
        SetWorldPhysics(profile);
        GD.Print($"PHYSICS_PROFILE {WorldPhysicsId} gravity={GravityMps2:0.##} m/s2 jump={JumpApexM * 100:0.#} cm airtime={2 * JumpSpeedMps / GravityMps2:0.00} s terminal_fall={EffectiveTerminalFallMps:0.##} m/s air_control={AirControl:0.##}");
        return WorldPhysicsId;
    }

    /// <summary>The active world profile, as the validated dictionary it was accepted from (a copy).</summary>
    public Dictionary WorldPhysicsProfile => _worldPhysics.Duplicate(true);

    /// <summary>
    /// Free-camera movement (F3): movement input is relative to <paramref name="yaw"/> and the body turns to face
    /// its motion. Null returns to body-relative movement and mouse look (F1, F2).
    /// </summary>
    public void SetMovementFrame(float? yaw) =>
        MovementFrameYaw = yaw is { } value && float.IsFinite(value) ? value : null;

    /// <summary>Test seam for the ×10 import-scale measurement only: it bypasses the room physics bounds.</summary>
    internal void ScaleWorldPhysicsForProbe(float scale)
    {
        GravityMps2 *= scale;
        WorldTerminalFallMps *= scale;
    }

    private static Dictionary DefaultWorldPhysics() =>
        GD.Load<GDScript>(WorldPhysicsScript).GetScriptConstantMap()["DEFAULT"].AsGodotDictionary();

    private void ApplyWorldPhysics(Dictionary profile)
    {
        _worldPhysics = profile.Duplicate(true);
        WorldPhysicsId = profile["id"].AsString();
        WorldPhysicsRevision = profile["revision"].AsInt32();
        GravityMps2 = (float)profile["gravity_mps2"].AsDouble();
        WorldTerminalFallMps = (float)profile["terminal_fall_mps"].AsDouble();
        AirControl = (float)profile["air_control"].AsDouble();
        WindMps = new Vector2((float)profile["wind_x_mps"].AsDouble(), (float)profile["wind_z_mps"].AsDouble());
    }

    /// <summary>
    /// Creation forces for the next physics ticks. Without a valid position guard, or with a non-finite
    /// acceleration, every creation contribution is removed at once (consent revoked, effect expired).
    /// </summary>
    public void SetCreationEffects(Vector3 acceleration, float glideLimit, Callable positionGuard)
    {
        if (!IsCallable(positionGuard) || !acceleration.IsFinite() || !float.IsFinite(glideLimit))
        {
            ClearCreationMotion();
            return;
        }
        if (acceleration.IsZeroApprox())
        {
            Velocity -= _creationApplied;
            _creationApplied = Vector3.Zero;
            _creationVelocity = Vector3.Zero;
        }
        _creationAcceleration = acceleration.LimitLength(MaxCreationAccelerationMps2);
        _creationGlideLimit = Mathf.Clamp(glideLimit, 0, MaxGlideLimitMps);
        _creationGuard = positionGuard;
        HasCreationGuard = true;
    }

    public void ClearCreationMotion()
    {
        Velocity -= _creationApplied;
        _creationApplied = Vector3.Zero;
        _creationVelocity = Vector3.Zero;
        _creationAcceleration = Vector3.Zero;
        _creationGlideLimit = 0;
        _creationGuard = default;
        HasCreationGuard = false;
    }

    /// <summary>Guards come from GDScript as method callables (object and method name) or from C# as delegates.</summary>
    private static bool IsCallable(Callable callable) =>
        callable.Delegate != null || (callable.Target != null && GodotObject.IsInstanceValid(callable.Target) && callable.Method != null && callable.Method.ToString().Length > 0);

    private bool GuardAllows(Vector3 position) => !HasCreationGuard || _creationGuard.Call(position).AsBool();

    /// <summary>Only teleport to a supported, unoccupied surface near the supplied foot height.</summary>
    public bool TryTeleportTo(Vector3 feetPosition)
    {
        if (!_ready || !feetPosition.IsFinite() || !FindSupportedPosition(feetPosition, out var position)) return false;
        GlobalPosition = position;
        Velocity = Vector3.Zero;
        _jumpBuffer = 0;
        LastSafePosition = position;
        HasSafePosition = true;
        return true;
    }

    /// <summary>Back to the last safe footing, else the spawn. While a creation moves the body, a checkpoint the guard refuses is skipped.</summary>
    public bool Recover()
    {
        SetControlInput(Vector2.Zero);
        Velocity = Vector3.Zero;
        var guarded = HasCreationGuard;
        var recovered = false;
        if (HasSafePosition && (!guarded || GuardAllows(LastSafePosition)) && TryTeleportTo(LastSafePosition)) recovered = true;
        else if ((!guarded || GuardAllows(_spawnPoint)) && TryTeleportTo(_spawnPoint)) recovered = true;
        else
            foreach (var offset in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back })
            {
                var candidate = _spawnPoint + offset * (BodyHeightM * 0.8f);
                if ((!guarded || GuardAllows(candidate)) && TryTeleportTo(candidate)) { recovered = true; break; }
            }
        ClearCreationMotion();
        return recovered;
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (!InputEnabled || !ReadKeyboard) return;
        if (input is InputEventMouseMotion mouse && Input.MouseMode == Input.MouseModeEnum.Captured && MovementFrameYaw == null)
        {
            RotateY(-mouse.Relative.X * 0.0025f);
            _pitch = Mathf.Clamp(_pitch - mouse.Relative.Y * 0.0025f, -1.35f, 1.35f);
            EyeCamera.Rotation = new Vector3(_pitch, 0, 0);
        }
        if (input is InputEventKey key && key.Pressed && !key.Echo)
        {
            var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
            if (code == Key.Space) _jumpBuffer = JumpBufferS;
            if (code == Key.R) Recover();
            if (code == Key.G) CycleWorldPhysics();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_ready) return;
        var dt = Mathf.Clamp((float)delta, 0, 0.05f);
        // Separate last tick's creation contribution from the body's own motion.
        Velocity -= _creationApplied;
        _creationApplied = Vector3.Zero;
        var control = InputEnabled ? _control : Vector2.Zero;
        var sprint = _sprint;
        if (InputEnabled && ReadKeyboard)
        {
            control = new Vector2(
                (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0),
                (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0)).LimitLength();
            sprint = Input.IsPhysicalKeyPressed(Key.Shift);
        }
        var onFloor = IsOnFloor();
        _coyote = onFloor ? CoyoteTimeS : Mathf.Max(0, _coyote - dt);
        var local = new Vector3(control.X, 0, -control.Y);
        Vector3 wish;
        if (MovementFrameYaw is { } frameYaw)
        {
            // Free camera: input is relative to the view, and the body turns to face where it goes.
            wish = new Basis(Vector3.Up, frameYaw) * local;
            if (wish.LengthSquared() > 0.0025f)
            {
                var facing = Mathf.Atan2(-wish.X, -wish.Z);
                Rotation = new Vector3(Rotation.X, Mathf.RotateToward(Rotation.Y, facing, TurnRateRadPerS * dt), Rotation.Z);
            }
        }
        else wish = GlobalBasis * local;
        wish.Y = 0;
        var desired = wish * (sprint ? RunSpeedMps : WalkSpeedMps);
        if (!onFloor && WindMps != Vector2.Zero)
            desired = (desired + new Vector3(WindMps.X, 0, WindMps.Y)).LimitLength(RunSpeedMps + WindMps.Length());
        var acceleration = onFloor ? GroundAccelerationMps2 : AirAccelerationMps2 * AirControl;
        var horizontal = new Vector3(Velocity.X, 0, Velocity.Z).MoveToward(desired, acceleration * dt);
        var vertical = onFloor ? Mathf.Min(0, Velocity.Y) : Mathf.Max(Velocity.Y - GravityMps2 * dt, -EffectiveTerminalFallMps);
        var jumped = false;
        if (_jumpBuffer > 0 && InputEnabled && _coyote > 0)
        {
            vertical = JumpSpeedMps;
            _jumpBuffer = 0;
            _coyote = 0;
            jumped = true;
            JumpsStarted++;
        }
        else _jumpBuffer = Mathf.Max(0, _jumpBuffer - dt);
        // Friendly lift is an explicit game capability: upward acceleration includes weight support.
        if (_creationAcceleration.Y > 0) vertical = Mathf.Max(vertical, 0);
        if (_creationGlideLimit > 0) vertical = Mathf.Max(vertical, -_creationGlideLimit);
        var own = new Vector3(horizontal.X, vertical, horizontal.Z);
        _creationVelocity = (_creationVelocity + _creationAcceleration * dt).LimitLength(MaxCreationSpeedMps);
        var total = own + _creationVelocity;
        if (_creationGlideLimit > 0) total.Y = Mathf.Max(total.Y, -_creationGlideLimit);
        _creationApplied = total - own;
        Velocity = total;
        var before = GlobalPosition;
        if (onFloor && !jumped && vertical <= 0 && _creationApplied == Vector3.Zero &&
            horizontal.LengthSquared() > 0.000001f && TryStep(horizontal * dt))
        {
            // The step already includes the horizontal movement for this tick.
            Velocity = Vector3.Down * 0.05f;
            MoveAndSlide();
            Velocity = new Vector3(horizontal.X, Velocity.Y, horizontal.Z);
        }
        else MoveAndSlide();
        // Project the creation contribution along contacts the same way MoveAndSlide projects motion.
        for (var index = 0; index < GetSlideCollisionCount(); index++)
        {
            var normal = GetSlideCollision(index).GetNormal();
            if (_creationApplied.Dot(normal) < 0) _creationApplied = _creationApplied.Slide(normal);
            if (_creationVelocity.Dot(normal) < 0) _creationVelocity = _creationVelocity.Slide(normal);
        }
        if (HasCreationGuard && !GuardAllows(GlobalPosition))
        {
            // Creation motion may not carry the body into space it is not allowed to be pushed into.
            GlobalPosition = before;
            Velocity = Vector3.Zero;
            _creationApplied = Vector3.Zero;
            _creationVelocity = Vector3.Zero;
        }
        var actual = GlobalPosition - before;
        actual.Y = 0;
        IsBlocked = control.LengthSquared() > 0.1f && actual.LengthSquared() < 0.0000001f;
        if (IsOnFloor() && Mathf.Abs(Velocity.Y) < 0.1f && (!HasCreationGuard || GuardAllows(GlobalPosition)))
        {
            LastSafePosition = GlobalPosition;
            HasSafePosition = true;
        }
        if (!GlobalPosition.IsFinite() || GlobalPosition.Y < _spawnPoint.Y - 12.0f) Recover();
        VisualRoot.Visible = !EyeCamera.Current;
    }

    protected bool HasSupportNear(Vector3 position, float maximumDrop = -1)
    {
        if (maximumDrop < 0) maximumDrop = BodyHeightM * 0.5f;
        var query = PhysicsRayQueryParameters3D.Create(position + Vector3.Up * (BodyHeightM * 0.25f),
            position - Vector3.Up * maximumDrop, 1);
        query.Exclude = new Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 && hit["normal"].AsVector3().Y >= Mathf.Cos(FloorMaxAngle);
    }

    private bool FindSupportedPosition(Vector3 requested, out Vector3 result)
    {
        result = requested;
        var ray = PhysicsRayQueryParameters3D.Create(requested + Vector3.Up * (BodyHeightM * 0.4f),
            requested - Vector3.Up * Mathf.Max(0.5f, BodyHeightM * 2), 1);
        ray.Exclude = new Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (hit.Count == 0 || hit["normal"].AsVector3().Y < Mathf.Cos(FloorMaxAngle)) return false;
        // A vertical capsule touches a slope off its centre ray. Raise its lower
        // hemisphere enough to clear the support plane, then check the entire
        // body against nearby geometry (including roofs and uneven terrain).
        var supportNormal = hit["normal"].AsVector3();
        var slopeClearance = BodyRadiusM * (1.0f / supportNormal.Y - 1.0f);
        result = hit["position"].AsVector3() + Vector3.Up * (slopeClearance + SafeMarginM * 2 + 0.001f);
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = _capsule, CollisionMask = CollisionMask,
            Transform = new Transform3D(Basis.Identity, result + Vector3.Up * (BodyHeightM * 0.5f)),
            Margin = SafeMarginM * 0.5f, Exclude = new Array<Rid> { GetRid() }
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
        if (rise <= SafeMargin * 3 || rise > StepHeightM + SafeMargin) return false;
        GlobalPosition += horizontalMotion + Vector3.Up * rise;
        StepsClimbed++;
        return true;
    }
}
