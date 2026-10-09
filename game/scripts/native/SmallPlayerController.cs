using Godot;
using Godot.Collections;
using EnFractal.Native.Room;

namespace EnFractal.Native;

/// <summary>
/// The player's 10 cm body. The root is at the feet; the room stays in real metres. Body properties
/// (size, speeds, step, jump) live here; gravity and wind are world properties from
/// scripts/world_physics_profile.gd. Heavier gravity than the room's default keeps the same jump, only quicker; lighter
/// gravity lets the body leap higher, as on the moon (see JumpSpeedMps).
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
    /// <summary>Jump apex above the take-off floor in the room's default gravity and heavier. Clears the 4 cm book with room to spare.</summary>
    [Export] public float JumpApexM { get; set; } = 0.065f;
    /// <summary>
    /// The highest leap, in the lightest gravity: three body heights (founder, 8 October: "being able to leap up a hillside
    /// would be very satisfying"). Without it floaty gravity's leap would be 38 cm and last 2.3 s.
    /// </summary>
    [Export] public float MaxJumpApexM { get; set; } = 0.30f;
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
    /// <summary>
    /// The largest gap under the capsule that the visual body closes. Jolt rests a CharacterBody3D's capsule 0 to
    /// 1.5 mm above its support (measured: 0.50 to 1.44 mm on the floor, the rug and the book), because its motion cast finds
    /// contact only to a fraction of the motion; on a 10 cm figure that reads as hovering.
    /// </summary>
    public const float MaxSeatGapM = 0.0025f;
    /// <summary>The gap under the capsule that the visual body closed on the last physics tick (0 in the air).</summary>
    public float SeatGapM { get; private set; }

    public float GravityMps2 { get; private set; } = 3.5f;
    /// <summary>The world's fall limit (thick floaty air falls slower); the body's own TerminalFallMps still caps it.</summary>
    public float WorldTerminalFallMps { get; private set; } = 6.0f;
    /// <summary>The world's multiplier on the body's air acceleration.</summary>
    public float AirControl { get; private set; } = 1.0f;
    public float EffectiveTerminalFallMps => Mathf.Min(TerminalFallMps, WorldTerminalFallMps);
    public Vector2 WindMps { get; private set; }
    public string WorldPhysicsId { get; private set; } = "room_tuned";
    public int WorldPhysicsRevision { get; private set; } = -1;
    /// <summary>The default preset's gravity (world_physics_profile.gd DEFAULT), where the jump's take-off speed is set.</summary>
    public float ReferenceGravityMps2 { get; private set; } = 3.5f;
    /// <summary>
    /// Take-off speed. In the default gravity and heavier it is solved so the apex, integrated at the physics tick, is
    /// JumpApexM (the discrete apex is v^2/2g + v*dt/2): real gravity keeps the 6.5 cm jump, only quicker. In lighter
    /// gravity the legs push off as hard as in the default, so the leap rises higher as gravity falls, as on the moon
    /// (half the gravity, about twice the height), up to MaxJumpApexM.
    /// </summary>
    public float JumpSpeedMps => GravityMps2 >= ReferenceGravityMps2
        ? SpeedForApex(GravityMps2, JumpApexM)
        : Mathf.Min(SpeedForApex(ReferenceGravityMps2, JumpApexM), SpeedForApex(GravityMps2, Mathf.Max(JumpApexM, MaxJumpApexM)));

    /// <summary>The apex the jump reaches under the current gravity, integrated at the physics tick.</summary>
    public float JumpApexNowM
    {
        get
        {
            var speed = JumpSpeedMps;
            return speed * speed / (2.0f * GravityMps2) + speed * 0.5f / Engine.PhysicsTicksPerSecond;
        }
    }

    /// <summary>How long the jump lasts on flat ground: up at the take-off speed, down under the fall limit.</summary>
    public float JumpAirtimeS
    {
        get
        {
            var apex = JumpApexNowM;
            var limit = EffectiveTerminalFallMps;
            var free = Mathf.Sqrt(2.0f * apex / GravityMps2);
            var down = free * GravityMps2 <= limit ? free : limit / GravityMps2 + (apex - limit * limit / (2.0f * GravityMps2)) / limit;
            return JumpSpeedMps / GravityMps2 + down;
        }
    }

    private static float SpeedForApex(float gravity, float apex)
    {
        var half = gravity * 0.5f / Engine.PhysicsTicksPerSecond;
        return -half + Mathf.Sqrt(half * half + 2.0f * gravity * apex);
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
    /// <summary>
    /// The room's playable volume (the manifest's bounds: "avatars outside it are recovered"), set by the command host. The
    /// body never walks, slides or is pushed out of it sideways: it stops at the bound as at a wall, with the whole capsule
    /// inside. A generated landscape's ground runs a few centimetres past its bounds and then stops, with nothing beyond.
    /// Null (the body suites' fixtures) keeps no bound. It is enforced in the body, not by colliders, so nothing else meets
    /// it: rays to the sun, sight, the navigation mesh and sandbox placement see the room exactly as before.
    /// </summary>
    public Aabb? PlayableBounds { get; private set; }
    /// <summary>A body this far below the playable bounds is falling out of the room and is recovered (without bounds: 12 m below its spawn).</summary>
    public const float RecoverBelowBoundsM = 1.0f;
    /// <summary>Physics ticks on which the playable bounds held the body back.</summary>
    public int BoundsStops { get; private set; }
    public Vector3 CreationVelocity => _creationVelocity;
    public bool HasCreationGuard { get; private set; }
    /// <summary>Ticks on which a creation guard took back the creation's share of a move.</summary>
    public int CreationGuardStops { get; private set; }

    // ---- Climbing and swimming (founder's playtest, 8 October: "climbing cliffs and climbing trees", "it should feel
    // like you're swimming when you're in deep water"). The numbers are starting points for the founder to tune by feel.

    /// <summary>Any colliding face too steep to stand on can be climbed by pushing into it. Off for the companion, which floats instead (part 2).</summary>
    [Export] public bool CanClimb { get; set; } = true;
    /// <summary>Water (Room/RoomWater) slows wading and floats the body in water over its head. Off for the companion.</summary>
    [Export] public bool CanSwim { get; set; } = true;
    /// <summary>Slow and steady, against a walk of 0.32 m/s.</summary>
    [Export] public float ClimbSpeedMps { get; set; } = 0.12f;
    /// <summary>On the ground, pushing into a face this long grabs it; brushing past or bumping it never does. In the air a push grabs at once.</summary>
    [Export] public float ClimbGrabDelayS { get; set; } = 0.2f;
    /// <summary>The pull over the top: up beside the face, then over onto the ledge.</summary>
    [Export] public float PullOverSpeedMps { get; set; } = 0.25f;
    /// <summary>Swimming against the walk; Shift swims at the walk itself.</summary>
    [Export] public float SwimSpeedFactor { get; set; } = 0.6f;
    /// <summary>The wading speed just before the water floats the body (it slows gradually from the walk as the water deepens).</summary>
    [Export] public float WadeSlowestFactor { get; set; } = 0.6f;
    [Export] public float SwimAccelerationMps2 { get; set; } = 1.6f;
    /// <summary>The push must point within 45 degrees of straight into the face to grab it.</summary>
    public const float ClimbGrabCos = 0.7f;
    /// <summary>Held back by what it pushes into, a body grabs with its push within 60 degrees of straight in (it is not brushing past).</summary>
    public const float ClimbHeldGrabCos = 0.5f;
    /// <summary>Held back on a walkable slope steeper than this, pushing up it, the body scrambles: it climbs the slope.</summary>
    public const float ScrambleSlopeDeg = 35.0f;
    /// <summary>A face leaning out over the climber more than 20 degrees past vertical is a roof: it cannot be grabbed or climbed onto.</summary>
    public const float ClimbOverhangNormalY = -0.34f;
    /// <summary>How far the hands reach for the face each tick, and the gentle press that keeps them on it.</summary>
    public const float ClimbProbeM = 0.015f;
    public const float ClimbStickMps = 0.06f;
    /// <summary>Jump lets go: a small kick away from the face and up.</summary>
    public const float ClimbKickAwayMps = 0.25f;
    public const float ClimbKickUpMps = 0.2f;
    /// <summary>After letting go, the same push does not grab again at once.</summary>
    public const float RegrabDelayS = 0.35f;
    public const float PullOverMaxS = 1.0f;
    public const float SwimBobM = 0.003f;
    public const float SwimBobPeriodS = 2.4f;
    /// <summary>The visible body tips forward this far to lie along the water (the capsule and the eye stay upright).</summary>
    public const float SwimTiltDeg = 75.0f;
    /// <summary>Water's drag on the body's vertical speed: quadratic (a fall is broken within centimetres) and linear (it settles).</summary>
    public const float WaterDragQuadraticPerM = 40.0f;
    public const float WaterDragLinearPerS = 4.0f;
    public const float SwimSpringPerS = 6.0f;
    public const float SwimSpringMaxMps = 0.25f;
    /// <summary>The water catches a falling or plunging body with its eye at least this far above the surface (no diving this round).</summary>
    public const float EyeClearanceM = 0.005f;
    /// <summary>A creation force pulling a climber off its face faster than this lets go, and the body carries that momentum.</summary>
    public const float ClimbCreationLetGoMps = 0.1f;
    /// <summary>The ledge a climber pulls over onto may be up to about the chest above the feet.</summary>
    public float PullOverReachM => BodyHeightM * 0.55f;
    /// <summary>Water this deep (surface to bed) floats the body: the water would reach the eyes.</summary>
    public float SwimDepthM => BodyHeightM * 0.8f;
    /// <summary>Shallower than this, a swimmer finds its feet and wades (below SwimDepthM so the change never flickers).</summary>
    public float SwimExitDepthM => BodyHeightM * 0.7f;
    /// <summary>A swimmer's feet ride this far under the surface: the eye (8.7 cm up) stays 2.7 cm above the water.</summary>
    public float SwimFloatDepthM => BodyHeightM * 0.6f;

    // ---- Floating (the Gubble: "a ghost bubble, set apart from the world"; the founder, 8 October). It never climbs, swims
    // or walks on the ground: it hovers a little above whatever is under it, the ground or the water, with a gentle bob.

    /// <summary>The body hovers instead of walking, climbing or swimming (the Gubble). Set before the body enters the tree.</summary>
    [Export] public bool Floats { get; set; }
    /// <summary>How far the feet hover over the ground or the water: a fifth of the 10 cm body, plainly off the ground at that scale.</summary>
    [Export] public float HoverHeightM { get; set; } = 0.02f;
    /// <summary>The bob: up and down this far either side of the hover, once per HoverBobPeriodS.</summary>
    public const float HoverBobM = 0.004f;
    public const float HoverBobPeriodS = 2.6f;
    /// <summary>How fast a floating body rises (up a cliff, over a wall) and sinks (off a ledge), and how firmly it settles to its height.</summary>
    [Export] public float FloatRiseMps { get; set; } = 0.30f;
    [Export] public float FloatSinkMps { get; set; } = 0.25f;
    public const float HoverSpringPerS = 8.0f;
    /// <summary>How far under a floating body its ground or water is looked for; with none that near it holds its height, never lost.</summary>
    public const float HoverSupportSearchM = 3.0f;
    /// <summary>
    /// Set each tick by whoever steers a floating body. RiseOverObstacles: something in its way that it cannot duck under is
    /// risen over (up the face, never through it). FloatAltitudeFloorY: it floats no lower than this (up to a player on a cliff
    /// top or in a tree); null for none.
    /// </summary>
    public bool RiseOverObstacles { get; set; }
    public float? FloatAltitudeFloorY { get; set; }
    /// <summary>The top of what the floating body hovers over on the last tick (ground or water), or null over nothing within reach.</summary>
    public float? HoverSupportY { get; private set; }
    /// <summary>Whether the floating body hovered over water on the last tick.</summary>
    public bool HoversOverWater { get; private set; }
    /// <summary>True on a tick the floating body rose over something in its way.</summary>
    public bool RisingOver { get; private set; }
    /// <summary>Ticks a floating body spent rising over something in its way, and ducking under something low.</summary>
    public int RiseTicks { get; private set; }
    public int DuckTicks { get; private set; }

    // ---- The open sea and the way home (the founder, 9 October: "the kids want to be able to swim forever"). The sea has no
    // edge: no current, no wash ashore, no wall a child meets. Past the island the water answers from the room's sea where
    // its meshes stop (RoomSea.OpenWater). Home is one key away from anywhere: B fades to the jetty (the founder: every room
    // has one where its door was, and it may become a portal to other islands), else the nearest beach, else the spawn.

    /// <summary>The room's sea (Room/RoomSea), set by the command host; null for a room without one (the bounds are its edge).</summary>
    public RoomSea? Sea { get; private set; }
    /// <summary>
    /// The last safety net, this far (planar) from the island's middle: a swimmer who gets there is taken home as by B. Float
    /// precision decides it (SmallAvatarPhysicsTest.MeasureFarOut). The body's own motion stays even out to 10 km (the steps
    /// round the same way every tick, though at 10 km the 1 mm float spacing slows the fast swim by 8 %). What shakes is what
    /// the eye sees: positions near the camera move by whole float steps, 0.06 mm at 1 km (about a pixel on the Gubble 16 cm
    /// from the eye), 0.12 mm at 2 km, 1 mm at 10 km. At 1 km nothing shakes by more than a pixel, and it is 52 minutes of
    /// fast swimming (0.32 m/s) without a break, an hour and a half at the plain swim (0.19 m/s).
    /// </summary>
    public const float DefaultFarNetM = 1000.0f;
    public float FarNetM { get; set; } = DefaultFarNetM;
    /// <summary>The fade out on the way home, then the fade back in.</summary>
    public const float HomeFadeS = 0.6f;
    /// <summary>Trips home so far (B, the far net).</summary>
    public int HomeTrips { get; private set; }
    /// <summary>Where the body last went home to: "jetty", a beach's id, or "spawn" ("" before any).</summary>
    public string LastHome { get; private set; } = "";
    /// <summary>True from B until the fade back in ends: the body takes no input meanwhile.</summary>
    public bool GoingHome => _homeTime >= 0;
    /// <summary>How dark the fade home is now: 0 to 1 and back (the HUD draws it).</summary>
    public float HomeFade => _homeTime < 0 ? 0 : _homeTime < HomeFadeS ? _homeTime / HomeFadeS : Mathf.Clamp(2 - _homeTime / HomeFadeS, 0, 1);
    /// <summary>Raised when the body has been put down at home (the Gubble comes too).</summary>
    public event System.Action<SmallPlayerController>? WentHome;
    /// <summary>Where B goes, for the HUD: the jetty, else the beach, else the start.</summary>
    public string HomeName => Sea?.JettyData != null ? "the jetty" : Sea != null && Sea.Beaches.Count > 0 ? "the beach" : "the start";
    // ---- Diving (the founder's son, 9 October: "dive into the water and swim down under the surface"). Hold Ctrl to dive and
    // Space to rise; under water W swims the way you look (F1 and F2). No breath limit and no harm (the island's rule: no
    // punishment). Let go and the body drifts slowly up, so nobody gets stuck on the bottom. Nothing goes through the bed,
    // the open-sea bed included, which has no collider. The founder tunes these by playing.

    /// <summary>Under water, the swim's speed is this share of the swim at the surface (the plain swim 0.19 m/s, 0.32 with Shift).</summary>
    [Export] public float DiveSpeedFactor { get; set; } = 1.0f;
    /// <summary>How fast Ctrl takes a diver down, and Space up.</summary>
    [Export] public float DiveSinkMps { get; set; } = 0.12f;
    [Export] public float DiveRiseMps { get; set; } = 0.15f;
    /// <summary>With no key held under water, the body drifts up this fast.</summary>
    [Export] public float DriftUpMps { get; set; } = 0.03f;
    /// <summary>A swimmer this far under its floating depth is diving; above it, the surface's own float takes over.</summary>
    public const float DiveStartM = 0.01f;
    /// <summary>True on a tick the body swam under the surface (Ctrl held, or still on its way back up from a dive).</summary>
    public bool IsDiving { get; private set; }
    /// <summary>Whether the eye is under the water's surface (a read for the look's view under water).</summary>
    public bool EyeUnderWater => Water.Wet && GlobalPosition.Y + EyeHeightM < Water.SurfaceY;
    /// <summary>Ticks spent diving, and dives begun.</summary>
    public int DiveTicks { get; private set; }
    public int DiveStarts { get; private set; }

    /// <summary>Dive input from a caller other than the keyboard (Ctrl and Space); retained until replaced.</summary>
    public void SetDiveInput(bool down, bool up)
    {
        _diveDown = down;
        _diveUp = up;
    }

    /// <summary>Whether the body is out past the island's reef (the open sea).</summary>
    public bool AtSea => Sea != null && Sea.PastReefM(new Vector2(GlobalPosition.X, GlobalPosition.Z)) > 0;

    public bool IsClimbing => _climbing;
    public bool IsPullingOver => _climbing && _pullPhase > 0;
    public bool IsSwimming => _swimming;
    /// <summary>The face being climbed (outward, unit); meaningful while IsClimbing.</summary>
    public Vector3 ClimbNormal => _climbNormal;
    public int Grabs { get; private set; }
    public int PullOvers { get; private set; }
    public int ClimbReleases { get; private set; }
    public int SwimStarts { get; private set; }
    /// <summary>Recoveries so far (R, a fall out of the room, or a non-finite position).</summary>
    public int Recoveries { get; private set; }
    /// <summary>The water at the body's feet on the last physics tick (RoomWater); Dry when there is none or the body cannot swim.</summary>
    public RoomWater.Column Water { get; private set; } = RoomWater.Column.Dry;
    /// <summary>How far the visible body is tipped toward lying along the water (0 upright, 1 fully).</summary>
    public float SwimTilt => _tilt;
    /// <summary>
    /// The command host's answer to "are this body's hands full?" (it is carrying something). Climbing takes both hands: a
    /// body that is carrying does not grab a face, and one that picks something up lets go.
    /// </summary>
    public System.Func<bool>? HandsFull { get; set; }

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
    private bool _climbing;
    private Vector3 _climbNormal = Vector3.Back;
    private float _grabPush;
    private float _regrabBlock;
    private int _pullPhase;
    private Vector3 _pullTarget;
    private float _pullTime;
    private int _faceLost;
    private readonly KinematicCollision3D _climbContact = new();
    private bool _swimming;
    private bool _waterLeap;
    private float _swimClock;
    private float _tilt;
    private float _visualYaw;
    private float _hoverClock;
    private float _homeTime = -1;
    private Vector3 _ownVelocity;
    private Vector3 _creationPushed;
    private float _swimSurfaceY = float.NaN;
    private bool _openSeaWater;
    private bool _diveDown;
    private bool _diveUp;
    private bool _wasDiving;
    private bool _homeArrived;

    public override void _Ready()
    {
        MotionMode = MotionModeEnum.Grounded;
        CollisionLayer = 2;
        // A companion must never become an obstacle trapping direct player control. The hidden layer holds the parts that
        // are never drawn (a tree's climbing pole and crown caps): the body climbs and stands on them (RoomBuilder.BodyMask).
        CollisionMask = RoomBuilder.BodyMask;
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
        ReferenceGravityMps2 = (float)DefaultWorldPhysics()["gravity_mps2"].AsDouble();
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
        // The pill reaches down to the feet, where the collision capsule's lowest point is. It used to start 3 %
        // of the body height up (3 mm on the 10 cm body, 7.2 mm on the old 0.24 m companion), so both avatars
        // hovered over the rug in the look captures.
        AddMesh(VisualRoot, new CapsuleMesh
        {
            Radius = BodyRadiusM * 0.82f, Height = BodyHeightM * 0.75f,
            RadialSegments = 12, Rings = 4
        }, new Vector3(0, BodyHeightM * 0.375f, 0), BodyMaterial);
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

    /// <summary>The trusted host's room bounds (see PlayableBounds). Refused unless finite and wider than the body.</summary>
    public bool SetPlayableBounds(Aabb bounds)
    {
        var radius = (float)Profile.RadiusMeters;
        if (!bounds.Position.IsFinite() || !bounds.Size.IsFinite() || bounds.Size.X <= radius * 2 || bounds.Size.Z <= radius * 2 || bounds.Size.Y <= 0) return false;
        PlayableBounds = bounds;
        return true;
    }

    /// <summary>Test seam for the body suites: no playable bounds again.</summary>
    internal void ClearPlayableBounds() => PlayableBounds = null;

    /// <summary>The trusted host's sea for this room (null for none).</summary>
    public void SetSea(RoomSea? sea) => Sea = sea;

    /// <summary>
    /// Whether the bounds hold the body at their sides. Not round an island: its land never reaches them and the sea has no
    /// edge (the far net, FarNetM, is the last safety net out there). The top and the bottom hold either way.
    /// </summary>
    protected bool BoundsHoldSides => PlayableBounds != null && Sea == null;

    /// <summary>Whether feet here keep the whole capsule inside the playable bounds horizontally (always, without bounds or round an island).</summary>
    public bool InsidePlayableBounds(Vector3 feetPosition)
    {
        if (PlayableBounds is not { } bounds || !BoundsHoldSides) return true;
        var radius = BodyRadiusM - 0.0005f;
        return feetPosition.X >= bounds.Position.X + radius && feetPosition.X <= bounds.End.X - radius &&
               feetPosition.Z >= bounds.Position.Z + radius && feetPosition.Z <= bounds.End.Z - radius;
    }

    /// <summary>The horizontal velocity cut so this tick's motion ends inside the playable bounds: at the bound, the outward part stops.</summary>
    private Vector3 WithinBounds(Vector3 horizontal, float dt)
    {
        if (PlayableBounds is not { } bounds || !BoundsHoldSides || dt <= 0) return horizontal;
        var here = GlobalPosition;
        float Axis(float velocity, float position, float low, float high)
        {
            var next = position + velocity * dt;
            if (velocity > 0 && next > high) return Mathf.Max(0, (high - position) / dt);
            if (velocity < 0 && next < low) return Mathf.Min(0, (low - position) / dt);
            return velocity;
        }
        var limited = new Vector3(
            Axis(horizontal.X, here.X, bounds.Position.X + BodyRadiusM, bounds.End.X - BodyRadiusM), horizontal.Y,
            Axis(horizontal.Z, here.Z, bounds.Position.Z + BodyRadiusM, bounds.End.Z - BodyRadiusM));
        if (limited != horizontal) BoundsStops++;
        return limited;
    }

    /// <summary>
    /// After the move: a slide, a climb or a creation push that still crossed the bound is put back on it, and its outward
    /// speed stops. The top holds the whole body under it too (a climber, a pull-over, a leap).
    /// </summary>
    private void HoldInsideBounds()
    {
        if (PlayableBounds is not { } bounds) return;
        var here = GlobalPosition;
        var top = bounds.End.Y - BodyHeightM;
        var sides = BoundsHoldSides;
        var held = new Vector3(
            sides ? Mathf.Clamp(here.X, bounds.Position.X + BodyRadiusM, bounds.End.X - BodyRadiusM) : here.X, bounds.Size.Y > BodyHeightM ? Mathf.Min(here.Y, top) : here.Y,
            sides ? Mathf.Clamp(here.Z, bounds.Position.Z + BodyRadiusM, bounds.End.Z - BodyRadiusM) : here.Z);
        if (held == here) return;
        GlobalPosition = held;
        Velocity = new Vector3(held.X != here.X ? 0 : Velocity.X, held.Y != here.Y ? Mathf.Min(0, Velocity.Y) : Velocity.Y, held.Z != here.Z ? 0 : Velocity.Z);
        _creationVelocity = new Vector3(held.X != here.X ? 0 : _creationVelocity.X, _creationVelocity.Y, held.Z != here.Z ? 0 : _creationVelocity.Z);
        BoundsStops++;
    }

    /// <summary>A floating body out past the far net is put back on it, and its outward speed stops.</summary>
    private void HoldInsideFarNet()
    {
        var flat = new Vector2(GlobalPosition.X, GlobalPosition.Z) - Sea!.MiddleM;
        var reach = flat.Length();
        if (reach <= FarNetM || reach <= 0) return;
        var outward = flat / reach;
        var held = Sea.MiddleM + outward * FarNetM;
        GlobalPosition = new Vector3(held.X, GlobalPosition.Y, held.Y);
        var along = Velocity.X * outward.X + Velocity.Z * outward.Y;
        if (along > 0) Velocity -= new Vector3(outward.X, 0, outward.Y) * along;
        BoundsStops++;
    }

    /// <summary>The trusted host's gravity and wind. Accepts only a valid profile with a newer revision.</summary>
    public bool SetWorldPhysics(Dictionary profile)
    {
        var script = GD.Load<GDScript>(WorldPhysicsScript);
        if (!script.Call("validate", profile).AsBool() || profile["revision"].AsInt32() <= WorldPhysicsRevision) return false;
        ApplyWorldPhysics(profile);
        return true;
    }

    /// <summary>
    /// Where the G key sends the next world physics preset: the command host sets this to its world.set_physics path
    /// (Kernel/CommandHost.PlayerPhysics), so the key travels the single command path as the player. Unset, G does nothing.
    /// </summary>
    public System.Func<string, bool>? WorldPhysicsRequest { get; set; }

    /// <summary>The preset after the current one in world_physics_profile.gd PRESET_IDS (tuned, real, floaty, then tuned again).</summary>
    public string NextWorldPhysicsPreset()
    {
        var ids = GD.Load<GDScript>(WorldPhysicsScript).GetScriptConstantMap()["PRESET_IDS"].AsGodotArray();
        var next = 0;
        for (var index = 0; index < ids.Count; index++)
            if (ids[index].AsString() == WorldPhysicsId) next = (index + 1) % ids.Count;
        return ids[next].AsString();
    }

    /// <summary>The G key: ask the command host for the next preset. False when no host is attached or it refused.</summary>
    public bool RequestNextWorldPhysics() => WorldPhysicsRequest?.Invoke(NextWorldPhysicsPreset()) ?? false;

    /// <summary>Test seam for the body suites, which run without a command host: apply the next preset directly.</summary>
    internal string CycleWorldPhysics()
    {
        var profile = GD.Load<GDScript>(WorldPhysicsScript).Call("preset", NextWorldPhysicsPreset(), WorldPhysicsRevision + 1).AsGodotDictionary();
        SetWorldPhysics(profile);
        PrintWorldPhysics();
        return WorldPhysicsId;
    }

    /// <summary>The playtest's console line for the active world physics.</summary>
    public void PrintWorldPhysics() =>
        GD.Print($"PHYSICS_PROFILE {WorldPhysicsId} gravity={GravityMps2:0.##} m/s2 jump={JumpApexNowM * 100:0.#} cm airtime={JumpAirtimeS:0.00} s terminal_fall={EffectiveTerminalFallMps:0.##} m/s air_control={AirControl:0.##}");

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
        if (!_ready || !feetPosition.IsFinite() || !InsidePlayableBounds(feetPosition) || !FindSupportedPosition(feetPosition, out var position)) return false;
        GlobalPosition = position;
        // With physics interpolation on, a teleport must not be drawn as a glide from the old place to the new one.
        ResetPhysicsInterpolation();
        StopClimbingAndSwimming();
        Velocity = Vector3.Zero;
        _jumpBuffer = 0;
        LastSafePosition = position;
        HasSafePosition = true;
        return true;
    }

    /// <summary>
    /// Put a floating body here, held in the air, if the whole capsule fits (the Gubble beside a player on a crowded jetty).
    /// Only for a body that floats: anything else would fall.
    /// </summary>
    protected bool TryPlaceFloating(Vector3 feetPosition)
    {
        if (!_ready || !Floats || !feetPosition.IsFinite() || !CapsuleFits(feetPosition)) return false;
        GlobalPosition = feetPosition;
        ResetPhysicsInterpolation();
        StopClimbingAndSwimming();
        Velocity = Vector3.Zero;
        return true;
    }

    /// <summary>Back to the last safe footing, else the spawn. While a creation moves the body, a checkpoint the guard refuses is skipped.</summary>
    public bool Recover()
    {
        Recoveries++;
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
            // B: home, from anywhere (the founder, 9 October): to the jetty, else the nearest beach, else the spawn.
            if (code == Key.B) RequestHome();
            // World physics is a world change: G sends world.set_physics through the command host as the player.
            if (code == Key.G) RequestNextWorldPhysics();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_ready) return;
        var dt = Mathf.Clamp((float)delta, 0, 0.05f);
        // Separate last tick's creation contribution from the body's own motion.
        Velocity -= _creationApplied;
        _creationApplied = Vector3.Zero;
        UpdateHomeTrip(dt);
        var control = InputEnabled && !GoingHome ? _control : Vector2.Zero;
        var sprint = _sprint;
        var diveDown = InputEnabled && !GoingHome && _diveDown;
        var diveUp = InputEnabled && !GoingHome && _diveUp;
        if (GoingHome) _jumpBuffer = 0;
        if (InputEnabled && ReadKeyboard && !GoingHome)
        {
            control = new Vector2(
                (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0),
                (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0)).LimitLength();
            sprint = Input.IsPhysicalKeyPressed(Key.Shift);
            diveDown = Input.IsPhysicalKeyPressed(Key.Ctrl);
            diveUp = Input.IsPhysicalKeyPressed(Key.Space);
        }
        var onFloor = IsOnFloor();
        _coyote = onFloor ? CoyoteTimeS : Mathf.Max(0, _coyote - dt);
        _regrabBlock = Mathf.Max(0, _regrabBlock - dt);
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
        var before = GlobalPosition;
        _ownVelocity = Vector3.Zero;
        _creationPushed = Vector3.Zero;
        SampleWater(dt);
        _wasDiving = IsDiving;
        IsDiving = false;
        if (_climbing) Climb(dt, control);
        else if (Floats) Hover(dt, wish, sprint);
        else if (UpdateSwimming()) Swim(dt, wish, sprint, control, diveDown, diveUp);
        else Walk(dt, wish, sprint, onFloor);
        HoldInsideBounds();
        // The far net, hours of swimming out: home as by B. A floating body (the Gubble, sent off on its own) is held at the
        // net instead, as at a wall (Codex Astra's review), and says blocked through its goal.
        if (Sea != null && !Floats && !GoingHome && new Vector2(GlobalPosition.X, GlobalPosition.Z).DistanceTo(Sea.MiddleM) > FarNetM) BeginHome();
        if (Sea != null && Floats) HoldInsideFarNet();
        // Project the creation contribution along contacts the same way MoveAndSlide projects motion.
        for (var index = 0; index < GetSlideCollisionCount(); index++)
        {
            var normal = GetSlideCollision(index).GetNormal();
            if (_creationApplied.Dot(normal) < 0) _creationApplied = _creationApplied.Slide(normal);
            if (_creationVelocity.Dot(normal) < 0) _creationVelocity = _creationVelocity.Slide(normal);
        }
        if (HasCreationGuard && _creationPushed != Vector3.Zero && !GuardAllows(GlobalPosition))
        {
            // Creation motion may not carry the body into space it is not allowed to be pushed into; the body's own walking,
            // swimming and climbing may, as they may with no creation about (Codex Sol's review of the open sea: a worn
            // glider's guard took back every whole move past the room's old bounds, a wall again). The tick is moved again
            // from where it began with the body's own motion alone, and the creation's motion stops. (Taking back only the
            // creation's share after the move missed a push that ended against a wall: the slide had already cut it away.)
            GlobalPosition = before;
            Velocity = _ownVelocity;
            if (_ownVelocity.LengthSquared() > 0) MoveAndSlide();
            Velocity = _ownVelocity;
            _creationApplied = Vector3.Zero;
            _creationVelocity = Vector3.Zero;
            CreationGuardStops++;
        }
        var actual = GlobalPosition - before;
        if (!_climbing) actual.Y = 0;
        IsBlocked = control.LengthSquared() > 0.1f && actual.LengthSquared() < 0.0000001f;
        if ((IsOnFloor() || (Floats && HoverSupportY != null)) && Mathf.Abs(Velocity.Y) < 0.1f && (!HasCreationGuard || GuardAllows(GlobalPosition)))
        {
            LastSafePosition = GlobalPosition;
            HasSafePosition = true;
            _waterLeap = false;
        }
        if (!GlobalPosition.IsFinite() || GlobalPosition.Y < _spawnPoint.Y - 12.0f ||
            (PlayableBounds is { } bounds && GlobalPosition.Y < bounds.Position.Y - RecoverBelowBoundsM)) Recover();
        UpdateVisual(dt);
        VisualRoot.Visible = !EyeCamera.Current;
    }

    /// <summary>On foot or in the air: the walk, the run, steps, jumps and creation forces, slowed by the water it wades in.</summary>
    private void Walk(float dt, Vector3 wish, bool sprint, bool onFloor)
    {
        var start = GlobalPosition;
        var desired = wish * (sprint ? RunSpeedMps : WalkSpeedMps) * WadeFactor();
        if (!onFloor && WindMps != Vector2.Zero)
            desired = (desired + new Vector3(WindMps.X, 0, WindMps.Y)).LimitLength(RunSpeedMps + WindMps.Length());
        var acceleration = onFloor ? GroundAccelerationMps2 : AirAccelerationMps2 * AirControl;
        var horizontal = WithinBounds(new Vector3(Velocity.X, 0, Velocity.Z).MoveToward(desired, acceleration * dt), dt);
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
        var total = WithCreation(new Vector3(horizontal.X, vertical, horizontal.Z), dt);
        // Falling into deep water: the water catches the body this tick, before the eye goes under.
        total.Y = CatchInWater(total.Y, dt);
        Velocity = total;
        if (onFloor && !jumped && vertical <= 0 && _creationApplied == Vector3.Zero &&
            horizontal.LengthSquared() > 0.000001f && TryStep(horizontal * dt))
        {
            // The step already includes the horizontal movement for this tick.
            Velocity = Vector3.Down * 0.05f;
            MoveAndSlide();
            Velocity = new Vector3(horizontal.X, Velocity.Y, horizontal.Z);
        }
        else MoveAndSlide();
        // A face too steep to stand on, pushed into: grab it (at once in the air, after a deliberate push on the ground).
        var grounded = IsOnFloor();
        TryGrab(dt, wish, deliberate: grounded, heldBack: !grounded || HeldBack(start, wish, WalkSpeedMps, dt));
    }

    /// <summary>
    /// Home (B): the fade out, home, the fade back in. Refused while already on the way, or while the body takes no input.
    /// Mid-climb the hands keep hold through the fade and let go at home; mid-leap the body flies on and lands at home; a
    /// carried thing stays in hand and comes home too (the command host places it with its holder every tick).
    /// </summary>
    public bool RequestHome()
    {
        if (!_ready || GoingHome || !InputEnabled) return false;
        BeginHome();
        return true;
    }

    private void BeginHome()
    {
        _homeTime = 0;
        _homeArrived = false;
        _jumpBuffer = 0;
    }

    private void UpdateHomeTrip(float dt)
    {
        if (_homeTime < 0) return;
        _homeTime += dt;
        if (!_homeArrived && _homeTime >= HomeFadeS)
        {
            _homeArrived = true;
            ArriveHome();
        }
        if (_homeTime >= 2 * HomeFadeS) _homeTime = -1;
    }

    /// <summary>
    /// Home: the jetty's landward end, standing and facing inland; a room with a sea but no jetty, the nearest beach; a room
    /// without a sea, the spawn. A spot with no room for the body tries a few round it, then the next home along; with none at
    /// all the body recovers to its last safe footing (it never falls off the world).
    /// </summary>
    private void ArriveHome()
    {
        var here = new Vector2(GlobalPosition.X, GlobalPosition.Z);
        var landed = false;
        LastHome = "";
        if (Sea?.JettyData is { } jetty)
        {
            var inland = Flat(jetty.RootM - jetty.EndM);
            if (inland == Vector3.Zero) inland = new Basis(Vector3.Up, Mathf.DegToRad(jetty.YawDeg)) * Vector3.Back;
            landed = LandAt(jetty.RootM, inland, "jetty");
        }
        if (!landed && Sea?.NearestBeach(here) is { } beach)
            landed = LandAt(beach.WashAshoreM, new Basis(Vector3.Up, Mathf.DegToRad(beach.YawDeg)) * Vector3.Forward, beach.Id);
        if (!landed && TryTeleportTo(_spawnPoint))
        {
            landed = true;
            LastHome = "spawn";
        }
        if (!landed) Recover();
        Velocity = Vector3.Zero;
        ClearCreationMotion();
        HomeTrips++;
        WentHome?.Invoke(this);
    }

    /// <summary>How far under a home's spot (the jetty's deck, a beach's sand) its landing may be found.</summary>
    public const float HomeDropM = 0.06f;

    /// <summary>
    /// Stand at a spot, or the nearest of a few beside it (inland first), facing inland. A landing must be dry ground within
    /// HomeDropM under the spot (Codex Astra's review: the seabed under a jetty's root was taken as home, under water), and
    /// not out past half the far net (Codex Sol's review: room data put there would start trip after trip).
    /// </summary>
    private bool LandAt(Vector3 spot, Vector3 inland, string name)
    {
        bool Near(Vector3 at) => Sea == null || new Vector2(at.X, at.Z).DistanceTo(Sea.MiddleM) <= FarNetM * 0.5f;
        if (!spot.IsFinite() || !Near(spot)) return false;
        var space = GetWorld3D().DirectSpaceState;
        bool Dry(Vector3 feet)
        {
            var water = RoomWater.At(space, feet, BodyHeightM, 0.005f, GetRid());
            if (!water.Wet && Sea != null) water = Sea.OpenWater(space, feet, BodyHeightM, 0.005f, GetRid());
            return !water.Wet || water.Under(feet) <= 0;
        }
        var side = inland.Cross(Vector3.Up);
        foreach (var offset in new[] { Vector3.Zero, inland * 0.06f, side * 0.06f, -side * 0.06f, inland * 0.12f, side * 0.12f, -side * 0.12f, inland * 0.2f })
            if (Near(spot + offset) && FindSupportedPosition(spot + offset, out var feet) && feet.Y >= spot.Y - HomeDropM && Dry(feet) && TryTeleportTo(feet))
            {
                Rotation = new Vector3(0, Mathf.Atan2(-inland.X, -inland.Z), 0);
                LastHome = name;
                return true;
            }
        return false;
    }

    /// <summary>
    /// Floating (Floats): the body hovers HoverHeightM over the ground or the water under it, with a gentle bob, and moves at
    /// the walk or the run. Nothing pulls it down: over a ledge or the sea it sinks gently to its height over what is below,
    /// and over nothing within HoverSupportSearchM it holds its height. Something in its way is ducked under when lowering
    /// the body to the ground clears it, and otherwise, while RiseOverObstacles is set, risen over: the body slides up the
    /// face and over the top, never through it. A ceiling holds it down.
    /// </summary>
    private void Hover(float dt, Vector3 wish, bool sprint)
    {
        _coyote = 0;
        _jumpBuffer = 0;
        _hoverClock += dt;
        if (MotionMode != MotionModeEnum.Floating) MotionMode = MotionModeEnum.Floating;
        // Floating mode stops a move that meets a face within this angle of head-on instead of sliding it; a rise pressed
        // against a cliff is nearly head-on (0.3 m/s up against a 1.2 m/s push is 14 degrees), so it must always slide.
        WallMinSlideAngle = 0;
        var here = GlobalPosition;
        var desired = wish * (sprint ? RunSpeedMps : WalkSpeedMps);
        var horizontal = WithinBounds(new Vector3(Velocity.X, 0, Velocity.Z).MoveToward(desired, GroundAccelerationMps2 * dt), dt);
        var heading = desired.LengthSquared() > 1e-6f ? desired.Normalized() : Vector3.Zero;
        var ahead = heading * (BodyRadiusM + 0.01f);
        var support = HoverSupport(here, ahead, out var overWater);
        HoverSupportY = support;
        HoversOverWater = overWater;
        var bob = HoverBobM * Mathf.Sin(_hoverClock * Mathf.Tau / HoverBobPeriodS);
        var target = support is { } top ? top + HoverHeightM + bob : here.Y;
        if (FloatAltitudeFloorY is { } floor && float.IsFinite(floor) && floor + bob > target) target = floor + bob;
        // Under a low ceiling, here or just ahead: hover lower (a passage just taller than the body).
        foreach (var column in heading == Vector3.Zero ? new[] { here } : new[] { here, here + ahead })
            if (Ceiling(column) is { } ceiling) target = Mathf.Min(target, ceiling - BodyHeightM - SafeMarginM * 2);
        if (support is { } ground) target = Mathf.Max(target, ground + SafeMarginM * 2);
        RisingOver = false;
        if (heading != Vector3.Zero && TestMove(GlobalTransform, heading * HoverProbeM, null, SafeMargin))
        {
            // In the way. Lowered to the ground, does it clear? Then duck under; else rise over, if allowed.
            var lowered = GlobalTransform;
            lowered.Origin.Y = (support ?? here.Y) + SafeMarginM * 2;
            if (lowered.Origin.Y < here.Y - 0.001f && !TestMove(GlobalTransform, lowered.Origin - here, null, SafeMargin) && !TestMove(lowered, heading * HoverProbeM, null, SafeMargin))
            {
                target = Mathf.Min(target, lowered.Origin.Y);
                DuckTicks++;
            }
            // (A roof over it stops the rise in the move itself; a test move up would report the face it is pressed against.)
            else if (RiseOverObstacles)
            {
                RisingOver = true;
                RiseTicks++;
            }
        }
        var vertical = RisingOver ? FloatRiseMps : Mathf.Clamp((target - here.Y) * HoverSpringPerS, -FloatSinkMps, FloatRiseMps);
        Velocity = WithCreation(new Vector3(horizontal.X, vertical, horizontal.Z), dt);
        MoveAndSlide();
    }

    /// <summary>How far ahead a floating body feels for something in its way.</summary>
    private float HoverProbeM => BodyRadiusM * 0.5f + 0.005f;

    /// <summary>
    /// The highest ground or water surface under the body or just ahead of it, looked for from the body's middle (so a table
    /// top overhead is a ceiling, not a floor) down HoverSupportSearchM. Null over nothing.
    /// </summary>
    private float? HoverSupport(Vector3 feet, Vector3 ahead, out bool overWater)
    {
        overWater = false;
        float? best = null;
        var space = GetWorld3D().DirectSpaceState;
        foreach (var column in ahead == Vector3.Zero ? new[] { feet } : new[] { feet, feet + ahead })
        {
            var from = column + Vector3.Up * (BodyHeightM * 0.5f);
            // Disposed at once: these run every tick, and wrappers left to the finalizer pile up (and trip Godot's exit).
            using var query = PhysicsRayQueryParameters3D.Create(from, column + Vector3.Down * HoverSupportSearchM, RoomBuilder.BodyMask);
            var exclude = new Array<Rid> { GetRid() };
            query.Exclude = exclude;
            query.HitBackFaces = false;
            using var hit = space.IntersectRay(query);
            if (hit.Count > 0 && (best == null || hit["position"].AsVector3().Y > best)) best = hit["position"].AsVector3().Y;
            // Water is a sheet on its own layer (RoomWater): the highest one under the middle, or over the feet if the body has sunk in.
            var water = RoomWater.At(space, column + Vector3.Up * (BodyHeightM * 0.5f), BodyHeightM, HoverSupportSearchM, GetRid());
            if (!water.Wet && Sea != null) water = Sea.OpenWater(space, column + Vector3.Up * (BodyHeightM * 0.5f), BodyHeightM, HoverSupportSearchM, GetRid());
            if (water.Wet && (best == null || water.SurfaceY > best))
            {
                best = water.SurfaceY;
                overWater = true;
            }
        }
        return best;
    }

    /// <summary>The underside of whatever is over the body's head within its hover and bob (null when clear).</summary>
    private float? Ceiling(Vector3 feet)
    {
        var from = feet + Vector3.Up * (BodyHeightM * 0.5f);
        using var query = PhysicsRayQueryParameters3D.Create(from, feet + Vector3.Up * (BodyHeightM + HoverHeightM + HoverBobM + 0.01f), RoomBuilder.BodyMask);
        var exclude = new Array<Rid> { GetRid() };
        query.Exclude = exclude;
        query.HitBackFaces = false;
        using var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? hit["position"].AsVector3().Y : null;
    }

    /// <summary>
    /// The creation stage every state shares: the creation velocity grows by its acceleration (capped), is added to the
    /// body's own motion and is remembered as applied, so it is taken off again next tick and never snaps back.
    /// </summary>
    private Vector3 WithCreation(Vector3 own, float dt)
    {
        _creationVelocity = (_creationVelocity + _creationAcceleration * dt).LimitLength(MaxCreationSpeedMps);
        var total = own + _creationVelocity;
        if (_creationGlideLimit > 0) total.Y = Mathf.Max(total.Y, -_creationGlideLimit);
        _creationApplied = total - own;
        _ownVelocity = own;
        _creationPushed = _creationApplied;
        return total;
    }

    // ---- water ----

    private float EyeHeightM => (float)Profile.EyeHeightMeters;

    /// <summary>The water at the feet, looking down as far as this tick's fall will carry them, so a fast fall meets the surface it crosses.</summary>
    private void SampleWater(float dt)
    {
        if (!CanSwim || !IsInsideTree())
        {
            Water = RoomWater.Column.Dry;
            return;
        }
        var space = GetWorld3D().DirectSpaceState;
        var below = Mathf.Max(0.02f, -Velocity.Y * dt + 0.02f);
        // A diver looks up as far as the surface it went down from, and under a roof (a shelf it dives beneath) the water is
        // still its water, with the bed under it (Codex's reviews: the shelf's top was taken for the bed, above the diver).
        var above = BodyHeightM * 4;
        if (_swimming && float.IsFinite(_swimSurfaceY)) above = Mathf.Max(above, _swimSurfaceY - GlobalPosition.Y + 0.05f);
        Water = RoomWater.At(space, GlobalPosition, above, below, GetRid(), throughRoof: _swimming);
        // Where the water meshes stop, out past the island, the sea itself answers.
        _openSeaWater = false;
        if (!Water.Wet && Sea != null)
        {
            Water = Sea.OpenWater(space, GlobalPosition, above, below, GetRid(), throughRoof: _swimming);
            _openSeaWater = Water.Wet;
        }
        // Where a sea mesh runs on past the generated floor (no bed within RoomWater.BedSearchM), the open sea's bed.
        else if (Water.Wet && Sea != null && Water.BedY <= Water.SurfaceY - RoomWater.BedSearchM + 1e-4f)
        {
            Water = Water with { BedY = Mathf.Max(Water.BedY, Sea.OpenSeaBedAt(new Vector2(GlobalPosition.X, GlobalPosition.Z))) };
            _openSeaWater = true;
        }
        _swimSurfaceY = Water.Wet ? Water.SurfaceY : _swimming ? _swimSurfaceY : float.NaN;
    }

    /// <summary>
    /// Deep water never lets the feet sink so far this tick that the eye goes under: a falling or plunging body is caught
    /// with its eye EyeClearanceM above the surface (and one already that deep rises no faster than the swim spring).
    /// </summary>
    private float CatchInWater(float vertical, float dt)
    {
        if (!CanSwim || !Water.Wet || Water.DepthM < SwimDepthM || dt <= 0) return vertical;
        var deepest = Water.SurfaceY - (EyeHeightM - EyeClearanceM);
        if (GlobalPosition.Y + vertical * dt >= deepest) return vertical;
        return Mathf.Max(vertical, Mathf.Min((deepest - GlobalPosition.Y) / dt, SwimSpringMaxMps));
    }

    /// <summary>Wading slows the body gradually as the water rises up it, to WadeSlowestFactor where it begins to float.</summary>
    private float WadeFactor()
    {
        if (!Water.Wet) return 1.0f;
        var under = Water.Under(GlobalPosition);
        if (under <= 0) return 1.0f;
        return Mathf.Lerp(1.0f, WadeSlowestFactor, Mathf.Clamp(under / SwimDepthM, 0, 1));
    }

    /// <summary>
    /// Water over the head floats the body: it swims while the water is deep where it is, finds its feet where the bed
    /// shelves up, and is carried clear of the water by a leap until it falls back in.
    /// </summary>
    private bool UpdateSwimming()
    {
        var under = Water.Under(GlobalPosition);
        if (!CanSwim || !Water.Wet)
        {
            if (_swimming) EndSwim();
            return false;
        }
        if (_swimming)
        {
            if (under < -0.01f || Water.DepthM < SwimExitDepthM) EndSwim();
        }
        else if (!(_waterLeap && Velocity.Y > 0) && under > 0.002f && Water.DepthM >= SwimDepthM) StartSwim();
        return _swimming;
    }

    private void StartSwim()
    {
        _swimming = true;
        _waterLeap = false;
        _coyote = 0;
        _grabPush = 0;
        MotionMode = MotionModeEnum.Floating;
        SwimStarts++;
    }

    private void EndSwim()
    {
        _swimming = false;
        MotionMode = MotionModeEnum.Grounded;
    }

    /// <summary>
    /// At the surface: calmer and slower than walking, with a gentle bob. Water breaks a fall within centimetres and the
    /// body rises back to the surface. A jump at the surface leaps out with the land jump's take-off. A steep bank pushed
    /// into is grabbed and climbed out of; a shelving shore is walked out of (UpdateSwimming).
    /// </summary>
    private void Swim(float dt, Vector3 wish, bool sprint, Vector2 control, bool diveDown, bool diveUp)
    {
        // Only a dive takes the body under: a fall or a plunge is still caught at the surface (CatchInWater).
        if (diveDown || (_wasDiving && GlobalPosition.Y < Water.SurfaceY - SwimFloatDepthM - DiveStartM))
        {
            Dive(dt, control, sprint, diveDown, diveUp);
            return;
        }
        _coyote = 0;
        _swimClock += dt;
        var feet = GlobalPosition;
        var target = Water.SurfaceY - SwimFloatDepthM + SwimBobM * Mathf.Sin(_swimClock * Mathf.Tau / SwimBobPeriodS);
        var spring = Mathf.Clamp((target - feet.Y) * SwimSpringPerS, -SwimSpringMaxMps, SwimSpringMaxMps);
        var excess = Velocity.Y - spring;
        excess = excess / (1 + WaterDragQuadraticPerM * Mathf.Abs(excess) * dt) * Mathf.Exp(-WaterDragLinearPerS * dt);
        var vertical = spring + excess;
        if (_jumpBuffer > 0 && InputEnabled && Mathf.Abs(feet.Y - target) < 0.015f)
        {
            vertical = JumpSpeedMps;
            _jumpBuffer = 0;
            _waterLeap = true;
            JumpsStarted++;
            EndSwim();
        }
        else _jumpBuffer = Mathf.Max(0, _jumpBuffer - dt);
        var desired = wish * WalkSpeedMps * (sprint ? 1.0f : SwimSpeedFactor);
        var horizontal = WithinBounds(new Vector3(Velocity.X, 0, Velocity.Z).MoveToward(desired, SwimAccelerationMps2 * dt), dt);
        var total = WithCreation(new Vector3(horizontal.X, vertical, horizontal.Z), dt);
        if (_swimming) total.Y = CatchInWater(total.Y, dt);
        Velocity = total;
        MoveAndSlide();
        if (_swimming) TryGrab(dt, wish, deliberate: true, heldBack: HeldBack(feet, wish, WalkSpeedMps * SwimSpeedFactor, dt));
    }

    /// <summary>
    /// Under water: W swims the way you look (the view's pitch, the eye's in F1 and the shoulder camera's, which follows it,
    /// in F2; in F3 and F4 across the view, level), A and D across; Ctrl down, Space up; no key, a slow drift up. The water's
    /// drag eases every change. The bed holds the body: the room's ground by collision, the open-sea bed by this clamp.
    /// </summary>
    private void Dive(float dt, Vector2 control, bool sprint, bool down, bool up)
    {
        _coyote = 0;
        _jumpBuffer = 0;
        if (!_wasDiving) DiveStarts++;
        IsDiving = true;
        DiveTicks++;
        Vector3 wish;
        if (MovementFrameYaw is { } frameYaw) wish = new Basis(Vector3.Up, frameYaw) * new Vector3(control.X, 0, -control.Y);
        else wish = ViewForward() * control.Y + GlobalBasis.X * control.X;
        var desired = wish.LimitLength() * WalkSpeedMps * (sprint ? 1.0f : SwimSpeedFactor) * DiveSpeedFactor;
        if (down) desired.Y -= DiveSinkMps;
        if (up) desired.Y += DiveRiseMps;
        if (!down && !up && control.LengthSquared() < 0.01f) desired.Y = DriftUpMps;
        var velocity = Velocity.MoveToward(desired, SwimAccelerationMps2 * dt);
        Velocity = WithCreation(velocity, dt);
        MoveAndSlide();
        // Nothing goes through the bed (the open-sea bed has no collider). Out in the open sea, the bed is the one where the
        // body is now: swimming off a deep ledge, the open-sea bed beyond stands higher (Codex Sol's review).
        var bedY = Water.BedY;
        if (_openSeaWater && Sea != null && Sea.OpenWater(GetWorld3D().DirectSpaceState, GlobalPosition, Water.SurfaceY - GlobalPosition.Y + 0.05f, 0.05f, GetRid(), throughRoof: true) is { Wet: true } there)
            bedY = there.BedY;
        var bed = bedY + SafeMarginM * 2;
        if (Water.Wet && GlobalPosition.Y < bed)
        {
            GlobalPosition = new Vector3(GlobalPosition.X, bed, GlobalPosition.Z);
            if (Velocity.Y < 0) Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
        }
    }

    /// <summary>
    /// The way W swims under water: the active view's forward when it is this body's own camera (the eye in F1, the shoulder
    /// camera in F2, which looks a little lower than the eye: Codex's review found F2's W swimming upward), else the eye's.
    /// </summary>
    private Vector3 ViewForward()
    {
        var camera = IsInsideTree() ? GetViewport().GetCamera3D() : null;
        if (camera != null && IsAncestorOf(camera)) return -camera.GlobalBasis.Z.Normalized();
        return GlobalBasis * (new Basis(Vector3.Right, EyeCamera.Rotation.X) * Vector3.Forward);
    }

    /// <summary>Where the body started (its spawn): the companion's last place to come home to.</summary>
    protected Vector3 SpawnPoint => _spawnPoint;

    // ---- climbing ----

    private bool Standable(Vector3 normal) => normal.Y >= Mathf.Cos(FloorMaxAngle);

    /// <summary>Too steep to stand on, but not a roof.</summary>
    private bool Climbable(Vector3 normal) =>
        normal.Y < Mathf.Cos(FloorMaxAngle) - 0.005f && normal.Y >= ClimbOverhangNormalY && Flat(normal).LengthSquared() > 0.01f;

    private static Vector3 Flat(Vector3 value)
    {
        var flat = new Vector3(value.X, 0, value.Z);
        return flat.LengthSquared() > 1e-8f ? flat.Normalized() : Vector3.Zero;
    }

    private bool HandsAreFull() => HandsFull?.Invoke() ?? false;

    /// <summary>Whether this tick's push made under a quarter of its way: the body is held back by what it pushes into.</summary>
    private bool HeldBack(Vector3 start, Vector3 wish, float speed, float dt)
    {
        var moved = new Vector3(GlobalPosition.X - start.X, 0, GlobalPosition.Z - start.Z);
        return moved.Dot(wish.Normalized()) < wish.Length() * speed * dt * 0.25f;
    }

    /// <summary>
    /// Grab a face the body pushes into: too steep to stand on, the push within 45 degrees of straight in, rising above a
    /// step (a step is stepped, never grabbed), and on the ground held for ClimbGrabDelayS. A body held back by what it
    /// pushes into (or in the air) also takes a face it feels a step up and just ahead, with the push within 60 degrees;
    /// and held back on a slope steeper than ScrambleSlopeDeg, where the walk cannot go on up (a slope steepening into a
    /// cliff, a crease), it scrambles: the slope itself is grabbed and climbed.
    /// </summary>
    private void TryGrab(float dt, Vector3 wish, bool deliberate, bool heldBack)
    {
        if (!CanClimb || _regrabBlock > 0 || !InputEnabled || wish.LengthSquared() < 0.25f || HandsAreFull())
        {
            _grabPush = 0;
            return;
        }
        var into = wish.Normalized();
        var cos = heldBack ? ClimbHeldGrabCos : ClimbGrabCos;
        Vector3? face = null;
        Vector3? ahead = null;
        var felt = false;
        Vector3? FeelAhead()
        {
            if (felt) return ahead;
            felt = true;
            if (TestMove(GlobalTransform, Vector3.Up * StepHeightM, null, SafeMargin)) return ahead = null;
            var raised = GlobalTransform;
            raised.Origin += Vector3.Up * StepHeightM;
            if (!TestMove(raised, into * 0.02f, _climbContact, SafeMargin)) return ahead = null;
            var normal = _climbContact.GetNormal();
            return ahead = Climbable(normal) && into.Dot(-Flat(normal)) >= cos ? normal : null;
        }
        for (var index = 0; index < GetSlideCollisionCount() && face == null; index++)
        {
            var collision = GetSlideCollision(index);
            var normal = collision.GetNormal();
            if (Climbable(normal) && into.Dot(-Flat(normal)) >= cos && (RisesAboveStep(normal, collision.GetPosition()) || (heldBack && FeelAhead() != null))) face = normal;
        }
        if (face == null && heldBack && IsOnFloor())
        {
            face = FeelAhead();
            var floor = GetFloorNormal();
            if (face == null && floor.Y < Mathf.Cos(Mathf.DegToRad(ScrambleSlopeDeg)) && into.Dot(-Flat(floor)) >= cos) face = floor;
        }
        if (face is not { } found)
        {
            _grabPush = 0;
            return;
        }
        _grabPush += dt;
        if (deliberate && _grabPush < ClimbGrabDelayS) return;
        _climbing = true;
        _climbNormal = found.Normalized();
        _grabPush = 0;
        _pullPhase = 0;
        _faceLost = 0;
        _swimming = false;
        _coyote = 0;
        _jumpBuffer = 0;
        MotionMode = MotionModeEnum.Floating;
        Velocity = Vector3.Zero;
        Grabs++;
    }

    /// <summary>
    /// A face is a climb, not a step, when the body meets it above step height (a face that starts at head height, a
    /// crown hanging over a perch, a ledge's lip just too tall to step) or it still stands in front of the body just
    /// above step height (a rising cliff, a trunk).
    /// </summary>
    private bool RisesAboveStep(Vector3 normal, Vector3 contact)
    {
        if (contact.Y > GlobalPosition.Y + StepHeightM + SafeMarginM) return true;
        var from = GlobalPosition + Vector3.Up * (StepHeightM + 0.004f);
        var query = PhysicsRayQueryParameters3D.Create(from, from - Flat(normal) * (BodyRadiusM + 0.02f), CollisionMask);
        query.Exclude = new Array<Rid> { GetRid() };
        query.HitBackFaces = false;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 && !Standable(hit["normal"].AsVector3()) && hit["normal"].AsVector3().Y >= ClimbOverhangNormalY;
    }

    /// <summary>
    /// On the face: forward climbs up and back climbs down, whatever way the body or the camera is turned; left and right
    /// move along the face as the view sees them. The body is pressed gently to the face. A ledge within reach above is
    /// pulled over onto; ground met while climbing down is stepped off onto; jump lets go.
    /// (Founder's playtest, 9 October: "I can climb around halfway up and then something happens to the controls and then I
    /// seem to be forced to climb down." Up had been the push toward the face, through the body's heading. On a round trunk
    /// any angle between the heading and the face sent the climber round it, which turned the face further from the
    /// heading, until forward pointed away from the face and climbed down. The thin climbing pole above the trunk turned it fastest.)
    /// </summary>
    private void Climb(float dt, Vector2 control)
    {
        _coyote = 0;
        if (!CanClimb || HandsAreFull())
        {
            LetGo(kick: false);
            return;
        }
        if (_jumpBuffer > 0 && InputEnabled)
        {
            _jumpBuffer = 0;
            LetGo(kick: true);
            return;
        }
        if (_pullPhase > 0)
        {
            PullOver(dt);
            return;
        }
        var normal = _climbNormal;
        var outward = Flat(normal);
        var right = (-outward).Cross(Vector3.Up);
        var up = control.Y;
        var viewRight = MovementFrameYaw is { } frameYaw ? new Basis(Vector3.Up, frameYaw) * Vector3.Right : GlobalBasis.X;
        var side = control.X * (new Vector3(viewRight.X, 0, viewRight.Z).Dot(right) >= 0 ? 1.0f : -1.0f);
        // Down a bank into deep water: the water takes the body before the eye goes under. Climbing out upward holds on.
        if (CanSwim && Water.Wet && Water.DepthM >= SwimDepthM)
        {
            var under = Water.Under(GlobalPosition);
            if ((up <= 0.3f && under >= SwimFloatDepthM) || under >= EyeHeightM - 0.01f)
            {
                StopClimbing(Vector3.Zero);
                StartSwim();
                return;
            }
        }
        // Creation forces: one pulling the climber off the face lets go, and the body carries it; the rest move it along the face.
        _creationVelocity = (_creationVelocity + _creationAcceleration * dt).LimitLength(MaxCreationSpeedMps);
        if (_creationVelocity.Dot(normal) > ClimbCreationLetGoMps)
        {
            LetGo(kick: false);
            return;
        }
        var along = _creationVelocity - normal * _creationVelocity.Dot(normal);
        // The room's bounds hold a climber inside, at the top as at the sides.
        if (PlayableBounds is { } bounds && up > 0 && GlobalPosition.Y + BodyHeightM >= bounds.End.Y) up = 0;
        if (up > 0.3f && FindLedge(outward, out var ledge))
        {
            BeginPullOver(ledge);
            PullOver(dt);
            return;
        }
        var alongUp = Vector3.Up - normal * Vector3.Up.Dot(normal);
        alongUp = alongUp.LengthSquared() > 1e-4f ? alongUp.Normalized() : Vector3.Up;
        var alongSide = right - normal * right.Dot(normal);
        alongSide = alongSide.LengthSquared() > 1e-4f ? alongSide.Normalized() : right;
        var velocity = (alongUp * up + alongSide * side).LimitLength(1) * ClimbSpeedMps - normal * ClimbStickMps + along;
        _creationApplied = along;
        _ownVelocity = velocity - along;
        _creationPushed = along;
        var horizontal = WithinBounds(new Vector3(velocity.X, 0, velocity.Z), dt);
        Velocity = new Vector3(horizontal.X, velocity.Y, horizontal.Z);
        MoveAndSlide();
        if (up < -0.1f)
            for (var index = 0; index < GetSlideCollisionCount(); index++)
                if (Standable(GetSlideCollision(index).GetNormal()))
                {
                    // Down at the foot of the face: step off into walking.
                    StopClimbing(Vector3.Zero);
                    return;
                }
        // Keep hold: find the face under the hands again, following its curve (a trunk, a bulge in the rock).
        if (TestMove(GlobalTransform, -normal * ClimbProbeM, _climbContact, SafeMargin))
        {
            _faceLost = 0;
            var found = _climbContact.GetNormal();
            if (Climbable(found)) _climbNormal = normal.Lerp(found, 0.35f).Normalized();
            // Walkable ground or a roof under the hands (a ledge's lip, a trunk's shoulder): keep the face as it was.
            return;
        }
        // The face fell away: over the top or round a corner. Pull over onto what is there; else, after a moment, let go.
        if (FindLedge(outward, out ledge)) BeginPullOver(ledge);
        else if (++_faceLost > 3) LetGo(kick: false);
    }

    /// <summary>
    /// Standable ground beyond the face, no higher than PullOverReachM above the feet and above them, where the whole body
    /// fits, with a clear way straight up beside the face and then over onto it.
    /// </summary>
    private bool FindLedge(Vector3 outward, out Vector3 ledge)
    {
        ledge = Vector3.Zero;
        var space = GetWorld3D().DirectSpaceState;
        foreach (var ahead in new[] { BodyRadiusM * 1.5f, BodyRadiusM * 2.5f })
        {
            var column = GlobalPosition - outward * ahead;
            var query = PhysicsRayQueryParameters3D.Create(column + Vector3.Up * (PullOverReachM + 0.005f), column + Vector3.Up * 0.002f, CollisionMask);
            query.Exclude = new Array<Rid> { GetRid() };
            query.HitBackFaces = false;
            var hit = space.IntersectRay(query);
            if (hit.Count == 0) continue;
            var normal = hit["normal"].AsVector3();
            if (!Standable(normal)) continue;
            var spot = hit["position"].AsVector3() + Vector3.Up * (BodyRadiusM * (1.0f / normal.Y - 1.0f) + SafeMarginM * 2 + 0.001f);
            if (!InsidePlayableBounds(spot) || !CapsuleFits(spot)) continue;
            // The whole body must stand under the bounds' top there (a pull-over never lifts the head out of the room).
            if (PlayableBounds is { } bounds && bounds.Size.Y > BodyHeightM && spot.Y + BodyHeightM > bounds.End.Y) continue;
            var rise = Mathf.Max(0, spot.Y + 0.002f - GlobalPosition.Y);
            if (rise > 0 && TestMove(GlobalTransform, Vector3.Up * rise, null, SafeMargin)) continue;
            var raised = GlobalTransform;
            raised.Origin += Vector3.Up * rise;
            if (TestMove(raised, new Vector3(spot.X - GlobalPosition.X, 0, spot.Z - GlobalPosition.Z), null, SafeMargin)) continue;
            ledge = spot;
            return true;
        }
        return false;
    }

    private void BeginPullOver(Vector3 ledge)
    {
        _pullTarget = ledge;
        _pullPhase = 1;
        _pullTime = 0;
    }

    /// <summary>The reward at the top: up beside the face, then over onto the ledge, and standing.</summary>
    private void PullOver(float dt)
    {
        _pullTime += dt;
        // The pull-over is the hands' own short move: creation forces wait (their velocity keeps building, so nothing snaps).
        _creationVelocity = (_creationVelocity + _creationAcceleration * dt).LimitLength(MaxCreationSpeedMps);
        var here = GlobalPosition;
        var velocity = Vector3.Zero;
        if (_pullPhase == 1)
        {
            var rise = _pullTarget.Y + 0.002f - here.Y;
            if (rise <= 0.0005f) _pullPhase = 2;
            else velocity = Vector3.Up * Mathf.Min(PullOverSpeedMps, rise / dt);
        }
        if (_pullPhase == 2)
        {
            var over = new Vector3(_pullTarget.X - here.X, 0, _pullTarget.Z - here.Z);
            if (over.Length() <= 0.001f)
            {
                PullOvers++;
                StopClimbing(Vector3.Zero);
                return;
            }
            velocity = over.Normalized() * Mathf.Min(PullOverSpeedMps, over.Length() / dt);
        }
        Velocity = velocity;
        MoveAndSlide();
        if (_pullTime > PullOverMaxS)
        {
            // Something moved into the way. A pull-over never ends in a fall: on support, stand there; else hold the face again.
            if (HasSupportNear(GlobalPosition, 0.01f))
            {
                PullOvers++;
                StopClimbing(Vector3.Zero);
            }
            else _pullPhase = 0;
        }
    }

    /// <summary>Jump lets go with a small kick away and up; a lost face just drops the body. Either way the same push does not grab again at once.</summary>
    private void LetGo(bool kick)
    {
        var outward = Flat(_climbNormal);
        StopClimbing(kick ? outward * ClimbKickAwayMps + Vector3.Up * ClimbKickUpMps : outward * 0.02f);
        _regrabBlock = RegrabDelayS;
        ClimbReleases++;
    }

    private void StopClimbing(Vector3 velocity)
    {
        _climbing = false;
        _pullPhase = 0;
        _faceLost = 0;
        MotionMode = MotionModeEnum.Grounded;
        Velocity = velocity;
    }

    /// <summary>A teleport or a recovery leaves any face or water behind.</summary>
    private void StopClimbingAndSwimming()
    {
        if (_climbing || _swimming) MotionMode = MotionModeEnum.Grounded;
        _climbing = false;
        _swimming = false;
        _pullPhase = 0;
        _faceLost = 0;
        _grabPush = 0;
        _waterLeap = false;
    }

    /// <summary>
    /// The visible body: seated on what it stands on (SeatVisual), turned to face the face it climbs or the way it swims,
    /// and tipped forward to lie along the water while swimming, its middle at the water line. The capsule and the eye never tilt.
    /// </summary>
    private void UpdateVisual(float dt)
    {
        SeatVisual();
        _tilt = Mathf.MoveToward(_tilt, _swimming ? 1.0f : 0.0f, dt / 0.35f);
        float? facing = null;
        if (_climbing) facing = Mathf.Atan2(Flat(_climbNormal).X, Flat(_climbNormal).Z);
        else if (_swimming)
        {
            var travel = new Vector3(Velocity.X, 0, Velocity.Z);
            facing = travel.LengthSquared() > 0.0004f ? Mathf.Atan2(-travel.X, -travel.Z) : GlobalRotation.Y + _visualYaw;
        }
        var offset = facing is { } yaw ? Mathf.AngleDifference(GlobalRotation.Y, yaw) : 0.0f;
        _visualYaw = Mathf.RotateToward(_visualYaw, offset, TurnRateRadPerS * dt);
        if (Mathf.Abs(_visualYaw) < 1e-4f && facing == null) _visualYaw = 0;
        if (_tilt <= 0 && _visualYaw == 0)
        {
            VisualRoot.Transform = new Transform3D(Basis.Identity, Vector3.Down * SeatGapM);
            return;
        }
        var basis = new Basis(Vector3.Up, _visualYaw) * new Basis(Vector3.Right, -Mathf.DegToRad(SwimTiltDeg) * _tilt);
        var pivot = Vector3.Up * (BodyHeightM * 0.5f);
        var lift = Water.Wet ? Mathf.Clamp(Water.Under(GlobalPosition) - BodyHeightM * 0.5f, -BodyHeightM * 0.5f, BodyHeightM * 0.5f) * _tilt : 0.0f;
        VisualRoot.Transform = new Transform3D(basis, pivot - basis * pivot + Vector3.Up * lift + Vector3.Down * SeatGapM);
    }

    /// <summary>
    /// Seat the visual body on what the capsule stands on (see MaxSeatGapM). The physics body is left exactly where
    /// Jolt put it, so steps, snapping and the jitter measurements are unchanged; only the visual closes the gap.
    /// </summary>
    private void SeatVisual()
    {
        var gap = 0.0f;
        var normal = IsOnFloor() ? GetFloorNormal() : Vector3.Zero;
        if (normal.Y >= Mathf.Cos(FloorMaxAngle))
        {
            // The capsule's nearest point to its support plane: the bottom sphere's centre, less a radius along the
            // floor normal. A ray from just above it along the normal measures the gap exactly on flat ground.
            var nearest = GlobalPosition + Vector3.Up * BodyRadiusM - normal * BodyRadiusM;
            var query = PhysicsRayQueryParameters3D.Create(nearest + normal * 0.001f, nearest - normal * (MaxSeatGapM + 0.0005f), CollisionMask);
            query.Exclude = new Array<Rid> { GetRid() };
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count > 0)
            {
                var along = (nearest - hit["position"].AsVector3()).Dot(normal);
                // Farther than the largest resting gap is not a resting gap (an edge, a step): leave the visual alone.
                if (along <= MaxSeatGapM) gap = Mathf.Clamp(along / normal.Y, 0.0f, MaxSeatGapM);
            }
        }
        SeatGapM = gap;
    }

    protected bool HasSupportNear(Vector3 position, float maximumDrop = -1)
    {
        if (maximumDrop < 0) maximumDrop = BodyHeightM * 0.5f;
        var query = PhysicsRayQueryParameters3D.Create(position + Vector3.Up * (BodyHeightM * 0.25f),
            position - Vector3.Up * maximumDrop, RoomBuilder.BodyMask);
        query.Exclude = new Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 && hit["normal"].AsVector3().Y >= Mathf.Cos(FloorMaxAngle);
    }

    private bool FindSupportedPosition(Vector3 requested, out Vector3 result)
    {
        result = requested;
        var ray = PhysicsRayQueryParameters3D.Create(requested + Vector3.Up * (BodyHeightM * 0.4f),
            requested - Vector3.Up * Mathf.Max(0.5f, BodyHeightM * 2), RoomBuilder.BodyMask);
        ray.Exclude = new Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (hit.Count == 0 || hit["normal"].AsVector3().Y < Mathf.Cos(FloorMaxAngle)) return false;
        // A vertical capsule touches a slope off its centre ray. Raise its lower
        // hemisphere enough to clear the support plane, then check the entire
        // body against nearby geometry (including roofs and uneven terrain).
        var supportNormal = hit["normal"].AsVector3();
        var slopeClearance = BodyRadiusM * (1.0f / supportNormal.Y - 1.0f);
        result = hit["position"].AsVector3() + Vector3.Up * (slopeClearance + SafeMarginM * 2 + 0.001f);
        return CapsuleFits(result);
    }

    /// <summary>Whether the whole body fits with its feet here (nothing on its mask inside the capsule).</summary>
    private bool CapsuleFits(Vector3 feet)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = _capsule, CollisionMask = CollisionMask,
            Transform = new Transform3D(Basis.Identity, feet + Vector3.Up * (BodyHeightM * 0.5f)),
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
