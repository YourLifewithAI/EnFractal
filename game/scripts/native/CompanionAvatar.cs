using Godot;
using EnFractal.Native.Navigation;
using EnFractal.Native.Room;

namespace EnFractal.Native;

/// <summary>
/// Deterministic, game-only companion body. Its goals arrive as enfractal.command goal.set /
/// goal.stop through Kernel/CommandHost; this is not an AI adapter or an authorization service.
/// It keeps its own body profile (WorldScaleProfile.Companion), a 10 cm body like the player's since the
/// founder's decision of 6 October, but a separate object so companion upgrades can change it later; the
/// original geometric appearance does not settle final companion art.
/// With the room's navigation bound (Navigation/RoomNavigation) follow and come plan routes round
/// furniture; local steering still handles the last stretch and anything the mesh does not hold.
/// </summary>
[GlobalClass]
public partial class CompanionAvatar : SmallPlayerController
{
    public string CompanionId { get; private set; } = "local_companion";
    /// <summary>The founder's name for the companion (8 October): the Gubble. The player may rename it in the customization panel.</summary>
    public const string DefaultName = "the Gubble";
    /// <summary>The name the companion had before it was the Gubble; a saved profile still holding it takes the new default.</summary>
    public const string FormerDefaultName = "Wisp";
    /// <summary>A saved name (a profile, a room's save): the former default becomes the Gubble; any other name is the player's choice.</summary>
    public static string SavedName(string saved) => saved == FormerDefaultName ? DefaultName : saved;

    /// <summary>
    /// Sets or clears the aura colour, carried as the node meta LookDirector.AuraColorMeta that the look reads when a glow starts
    /// (docs/look/GLOW.md): restart a glow to recolour it.
    /// </summary>
    public void SetAuraColor(Color? color)
    {
        AuraColor = color;
        if (color is { } c) SetMeta(EnFractal.Native.Look.LookDirector.AuraColorMeta, c);
        else if (HasMeta(EnFractal.Native.Look.LookDirector.AuraColorMeta)) RemoveMeta(EnFractal.Native.Look.LookDirector.AuraColorMeta);
    }
    public string CompanionName { get; private set; } = DefaultName;
    /// <summary>The name tag over the body: the name with a capital, as a name stands on its own ("The Gubble").</summary>
    public string NameTag => CompanionName.Length > 0 ? char.ToUpperInvariant(CompanionName[0]) + CompanionName[1..] : CompanionName;
    /// <summary>The Gubble's aura colour (the family's note), if it has one: its glow takes it. Null: the look's warm white-gold.</summary>
    public Color? AuraColor { get; private set; }
    public string CurrentIntent { get; private set; } = "stay";
    public bool GoalBlocked { get; private set; }
    public bool IsPointing => _pointer != null && _pointer.Visible;
    /// <summary>
    /// Counts every change of goal (follow, stay, come, stop, look, point), so the command host can tell the goal it
    /// set from a newer one and finish or cancel that goal's job honestly.
    /// </summary>
    public int IntentSerial { get; private set; }
    /// <summary>The IntentSerial of the last come that arrived beside the player (the body then stays); -1 before any.</summary>
    public int ComeArrivedSerial { get; private set; } = -1;
    /// <summary>The IntentSerial of the last go_to that arrived (the body then stays); -1 before any.</summary>
    public int GoToArrivedSerial { get; private set; } = -1;
    /// <summary>The box a go_to walks to (meaningful while CurrentIntent is "go_to").</summary>
    public Aabb GoToTarget => _goToTarget;
    /// <summary>Whether the body is turned toward a look or point target.</summary>
    public bool HasLookTarget => _hasLookTarget;
    /// <summary>The point the body looks or points at (meaningful while HasLookTarget).</summary>
    public Vector3 LookTarget => _lookTarget;
    /// <summary>How closely a look or point must face its target to count as arrived (about 3 degrees).</summary>
    public const float FacingToleranceRad = 0.05f;
    /// <summary>
    /// True while looking or pointing and turned to face the target within FacingToleranceRad, or standing so close
    /// that there is nothing to turn to: the arrival of a look_at or point_at goal.
    /// </summary>
    public bool FacesLookTarget
    {
        get
        {
            if (!_hasLookTarget) return false;
            var direction = Planar(_lookTarget - GlobalPosition);
            if (direction.LengthSquared() <= 0.001f) return true;
            return Mathf.Abs(Mathf.AngleDifference(Rotation.Y, Mathf.Atan2(-direction.X, -direction.Z))) <= FacingToleranceRad;
        }
    }
    protected override WorldScaleProfile Profile => WorldScaleProfile.Companion;

    // Loose follow (founder playtest, 6 October: "it always moves directly behind me"). Distances are planar,
    // centre to centre: the 10 cm companion (2 cm radius) keeps a comfortable band beside the 10 cm player's line
    // of travel instead of a point rigidly attached behind it, which swung behind on every turn. Every distance
    // below was scaled from the 0.24 m body (about ×0.42) when the companion shrank to the player's size.
    /// <summary>Closer than this and the companion eases out to its place (was 0.30 m for the 0.24 m body).</summary>
    public const float FollowNearM = 0.12f;
    /// <summary>Farther than this and it closes in (was 0.65 m).</summary>
    public const float FollowFarM = 0.30f;
    /// <summary>Its place: this far to the side of the player's line of travel (was 0.40 m) ...</summary>
    public const float FollowSideM = 0.16f;
    /// <summary>... and this far ahead, so it stays in view of the over-the-shoulder camera (was 0.08 m).</summary>
    public const float FollowLeadM = 0.04f;
    /// <summary>Come stops this far from the player (was 0.32 m).</summary>
    public const float ComeArrivalM = 0.14f;
    /// <summary>Outside follow and stop, the companion steps aside when the player comes this close (was 0.20 m).</summary>
    public const float YieldM = 0.08f;
    /// <summary>Speed per metre of distance to its place; the player's own velocity is added while it moves.</summary>
    public const float FollowGainPerS = 3.0f;
    /// <summary>The slowest purposeful approach, so the last centimetres never creep (was 0.15 m/s).</summary>
    public const float MinApproachMps = 0.08f;
    public const float CompanionTurnRateRadPerS = 8.0f;
    /// <summary>A route is planned again at least this often while it is in use.</summary>
    public const double RouteRefreshS = 0.1;
    /// <summary>A route corner closer than this counts as reached (was 0.05 m).</summary>
    public const float CornerReachedM = 0.025f;
    /// <summary>A planned route this much longer than the straight line is a real detour round something (was 0.05 m).</summary>
    public const float DetourM = 0.03f;
    /// <summary>Trying to move for this long without covering StuckDistanceM reports the goal blocked.</summary>
    public const double StuckAfterS = 1.0;
    /// <summary>Was 0.03 m for the 0.24 m body.</summary>
    public const float StuckDistanceM = 0.015f;
    /// <summary>How far ahead local steering probes for support and collisions (was 0.14 m).</summary>
    public const float SteeringProbeM = 0.06f;

    /// <summary>
    /// Body speeds. The run must outpace the player's 0.96 m/s run so follow can close a gap while the player runs:
    /// 1.20 m/s is 1.25× (the 0.24 m body ran at 1.65 m/s). Accelerations keep the player's 0.16 s ramp to full run.
    /// Step, floor snap and jump are the player's (the 0.24 m body stepped 4 cm and snapped 2.5 cm).
    /// </summary>
    public const float CompanionWalkMps = 0.32f;
    public const float CompanionRunMps = 1.20f;
    public const float CompanionGroundAccelerationMps2 = 7.5f;
    public const float CompanionAirAccelerationMps2 = 2.5f;

    /// <summary>Whether follow is moving the body (false while it rests inside its band).</summary>
    public bool FollowMoving => _following;
    /// <summary>+1 when the companion keeps to the right of the player's line of travel, -1 for the left.</summary>
    public float FollowSide => _followSide;
    /// <summary>The room's walkable map, or null for local steering only.</summary>
    public RoomNavigation? Navigation { get; private set; }
    /// <summary>True on a tick the companion steered along a planned route round something in its way.</summary>
    public bool FollowingRoute { get; private set; }
    /// <summary>The corners of the route last planned (empty when none).</summary>
    public Vector3[] RoutePoints => _route.Points;

    // The Gubble floats (founder, 8 October: "a ghost bubble, set apart from the world"). It walks the planned route while
    // that reaches its goal, hovering over the ground; when the walk cannot take it there (up a cliff, across water, a long way
    // round), it floats straight there instead, rising over what is in its way and never through it.
    /// <summary>A walking route counts as reaching only if it ends this level with its goal (a cliff top over its foot does not).</summary>
    public const float WalkLevelM = 0.06f;
    /// <summary>A walk this many times the straight way (and FloatDetourM more) is too long a way round: it floats instead.</summary>
    public const float FloatDetourFactor = 2.5f;
    public const float FloatDetourM = 0.5f;
    /// <summary>Within this height of the player (feet to feet), the companion is level with them: follow rests, come arrives.</summary>
    public const float LevelGapM = 0.12f;
    /// <summary>True while the companion floats straight to its goal instead of walking a route.</summary>
    public bool FloatingThere => _floating;
    /// <summary>How many times the companion has chosen to float rather than walk.</summary>
    public int FloatStarts { get; private set; }
    /// <summary>The point of the go_to box the companion makes for: the side it can reach (meaningful while CurrentIntent is "go_to").</summary>
    public Vector3 GoToAim => _goToAim;

    private SmallPlayerController? _player;
    private Node3D _pointer = null!;
    private Label3D _label = null!;
    private const float NameTagHeightFraction = 0.025f;
    /// <summary>The name tag's dark outline, in label pixels: thick enough to read over a busy landscape (the Glow playtest).</summary>
    public const int NameTagOutline = 10;
    private const float NameTagHideWithinM = 0.25f;
    private Vector3 _lookTarget;
    private Aabb _goToTarget;
    private float _goToArrivalM;
    private bool _hasLookTarget;
    private bool _entered;
    private bool _following;
    private float _followSide = 1.0f;
    private Vector3 _travel = Vector3.Forward;
    private RoomNavigation.Route _route = RoomNavigation.Route.None;
    private int _routeIndex;
    private Vector3 _routeGoal;
    private double _routeAge;
    private int _routeRevision = -1;
    private Vector3 _progressAnchor;
    private double _progressTime;
    private bool _stuck;
    private bool _floating;
    private float? _holdY;
    private Vector3 _goToAim;
    private double _goToAimAge;
    private Vector3 _playerLast;
    private Vector3 _playerMotion;
    private bool _playerSeen;
    private float _playerStillS;
    private int _goToAimRevision = -1;

    public override void _Ready()
    {
        ReadKeyboard = false;
        // The Gubble never climbs, swims or walks on the ground (founder, 8 October): it floats, over water and up cliffs.
        CanClimb = false;
        CanSwim = false;
        Floats = true;
        WalkSpeedMps = CompanionWalkMps;
        RunSpeedMps = CompanionRunMps;
        GroundAccelerationMps2 = CompanionGroundAccelerationMps2;
        AirAccelerationMps2 = CompanionAirAccelerationMps2;
        SetAppearance(new Color("65b9b0"));
        base._Ready();
        CollisionLayer = 4;
        CollisionMask = RoomBuilder.BodyMask | 2;
        _entered = true;
        // The label floats a little above the body; the pointing cue comes from chest height.
        // The name tag is drawn solid, with an alpha cut (founder playtest, 6 October): a see-through tag writes no
        // depth, so depth of field read the wall behind it and blurred it in F2, and writes no motion, so temporal
        // anti-aliasing smeared and doubled it while the camera moved in F3. Cut out, it is an object like the body.
        var h = BodyHeightM;
        _label = new Label3D
        {
            Name = "CompanionLabel", Text = NameTag,
            Position = Vector3.Up * (h * 1.35f), FontSize = 30, OutlineSize = NameTagOutline, PixelSize = 0.0003f, FixedSize = true,
            Modulate = new Color("f6dfab"), OutlineModulate = new Color("18332d"),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = false,
            AlphaCut = Label3D.AlphaCutMode.Discard, AlphaScissorThreshold = 0.5f
        };
        AddChild(_label);
        _pointer = new Node3D { Name = "PointingCue", Position = Vector3.Up * (h * 0.667f), Visible = false };
        AddChild(_pointer);
        var gold = new StandardMaterial3D { AlbedoColor = new Color("edc06e"), Roughness = 0.8f };
        var shaft = AddMesh(_pointer, new CylinderMesh { TopRadius = h * 0.021f, BottomRadius = h * 0.021f, Height = h * 0.71f, RadialSegments = 8 },
            new Vector3(0, 0, -h * 0.375f), gold);
        shaft.Rotation = new Vector3(-Mathf.Pi * 0.5f, 0, 0);
        var tip = AddMesh(_pointer, new CylinderMesh { TopRadius = 0, BottomRadius = h * 0.079f, Height = h * 0.167f, RadialSegments = 8 },
            new Vector3(0, 0, -h * 0.8125f), gold);
        tip.Rotation = new Vector3(-Mathf.Pi * 0.5f, 0, 0);
    }

    public override void _Process(double delta)
    {
        UpdateGesture(delta);
        var camera = GetViewport().GetCamera3D();
        if (camera == null) { _label.Visible = false; return; }
        var cameraTransform = camera.GetCameraTransform();
        _label.Visible = GetGlobalTransformInterpolated().Origin.DistanceTo(cameraTransform.Origin) >= NameTagHideWithinM;
        if (!_label.Visible) return;

        // FixedSize draws as though the tag were one metre away, but FOV still changes its size.
        // Project a 2.5%-high screen segment at that depth to keep F2, F3 and F4 equally readable.
        var screen = GetViewport().GetVisibleRect();
        var centre = screen.GetCenter();
        var halfHeight = Vector2.Down * (screen.Size.Y * NameTagHeightFraction * 0.5f);
        var heightM = camera.ProjectPosition(centre - halfHeight, 1).DistanceTo(camera.ProjectPosition(centre + halfHeight, 1));
        // Include the outline in the budget, including after a display-name change.
        var labelHeightM = _label.GetAabb().Size.Y + 2 * _label.OutlineSize * _label.PixelSize;
        if (labelHeightM > 0) _label.Scale = Vector3.One * (heightM / labelHeightM);
    }

    protected override void BuildVisual()
    {
        base.BuildVisual();
        // The hat: a cone as wide as the body on top of the head, a third of the body's height (0.075 m on 0.24 m).
        var trim = new StandardMaterial3D { AlbedoColor = new Color("eec471"), Roughness = 0.9f };
        var hat = BodyHeightM * 0.3125f;
        AddMesh(VisualRoot, new CylinderMesh
        {
            TopRadius = 0, BottomRadius = BodyRadiusM, Height = hat, RadialSegments = 12
        }, new Vector3(0, BodyHeightM - hat * 0.5f, 0), trim);
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
        if (_label != null) _label.Text = NameTag;
    }

    public void BindPlayer(SmallPlayerController player)
    {
        if (_player != null && GodotObject.IsInstanceValid(_player)) _player.WentHome -= PlayerWentHome;
        _player = player;
        player.WentHome += PlayerWentHome;
    }

    /// <summary>
    /// The player went home (B, or the far net): the Gubble comes too (the founder's island: it floats over the sea and is
    /// never lost). Following or coming to the player, or out past the reef itself, it appears beside them, on whichever side
    /// has room; with none, it floats there.
    /// </summary>
    private void PlayerWentHome(SmallPlayerController player)
    {
        if (!IsInsideTree() || !GodotObject.IsInstanceValid(player)) return;
        var outAtSea = Sea != null && Sea.PastReefM(new Vector2(GlobalPosition.X, GlobalPosition.Z)) > 0;
        if (CurrentIntent is not ("follow" or "come") && !outAtSea) return;
        var right = player.GlobalBasis.X;
        var forward = -player.GlobalBasis.Z;
        // Beside them on whichever side has room, then a little farther round (Codex Sol's review: with the four spots by a
        // crowded jetty taken, the Gubble was left out at sea for good); with nowhere to stand, floating over their head.
        if (!PlaceNearHome(player))
        {
            // Nowhere at all near home (Codex Sol's review: tall things all round it): it keeps trying while the player stays.
            _homePending = true;
            return;
        }
        _route = RoomNavigation.Route.None;
        _floating = false;
        // A height it held out there (resting aloft, an arrival by floating) means nothing at home.
        FloatAltitudeFloorY = _holdY = null;
        if (CurrentIntent == "follow") _following = false;
        WentHomeWithPlayer++;
    }

    /// <summary>
    /// Beside the player on whichever side has room, then farther round out to a metre; with nowhere to stand, floating over
    /// their head or over one of those spots; last, at its own spawn.
    /// </summary>
    private bool PlaceNearHome(SmallPlayerController player)
    {
        var right = player.GlobalBasis.X;
        var forward = -player.GlobalBasis.Z;
        foreach (var ring in new[] { FollowSideM, 0.25f, 0.35f, 0.5f, 0.75f, 1.0f })
            for (var k = 0; k < 8; k++)
            {
                var angle = k * Mathf.Pi / 4;
                if (TryTeleportTo(player.GlobalPosition + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * ring)) return true;
            }
        foreach (var ring in new[] { 0.0f, FollowSideM, 0.35f, 0.75f })
            for (var k = 0; k < (ring == 0 ? 1 : 8); k++)
            {
                var angle = k * Mathf.Pi / 4;
                var spot = player.GlobalPosition + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * ring;
                for (var up = 1; up <= 6; up++)
                    if (TryPlaceFloating(spot + Vector3.Up * (player.BodyHeightM * up + HoverHeightM))) return true;
            }
        return TryTeleportTo(SpawnPoint) || TryPlaceFloating(SpawnPoint + Vector3.Up * HoverHeightM);
    }

    private bool _homePending;
    private double _homePendingAge;

    /// <summary>Times the Gubble came home with the player (B, the far net).</summary>
    public int WentHomeWithPlayer { get; private set; }

    /// <summary>Give the companion the room's navigation (or none): follow and come then route round obstacles.</summary>
    public void BindNavigation(RoomNavigation? navigation)
    {
        Navigation = navigation;
        _route = RoomNavigation.Route.None;
    }
    public void Follow() => BeginIntent("follow");
    public void Stay() => BeginIntent("stay");
    public void Come() => BeginIntent("come");
    public void Stop() => BeginIntent("stop");

    /// <summary>
    /// Walk to a box (a thing's bounds, or a place as an empty box), routing round furniture as come does, and stop
    /// once the body's centre is within arrivalM of the box's footprint. The command host's go_to (and A2's fetch)
    /// watch GoToArrivedSerial; a body that cannot get there reports GoalBlocked, honestly.
    /// </summary>
    public void GoTo(Aabb target, float arrivalM)
    {
        if (!target.Position.IsFinite() || !target.Size.IsFinite()) return;
        BeginIntent("go_to");
        _goToTarget = target.Abs();
        _goToArrivalM = Mathf.Max(0.005f, arrivalM);
        _goToAimRevision = -1;
    }

    private void BeginIntent(string intent)
    {
        IntentSerial++;
        CurrentIntent = intent;
        GoalBlocked = false;
        _hasLookTarget = false;
        _following = false;
        _route = RoomNavigation.Route.None;
        _stuck = false;
        _progressTime = 0;
        _floating = false;
        _holdY = null;
        if (_pointer != null) _pointer.Visible = false;
        SetControlInput(Vector2.Zero);
        Velocity = new Vector3(0, Velocity.Y, 0);
        if (intent == "follow" && HasPlayer())
        {
            // Start from the player's facing and keep whichever side the companion is already on.
            _travel = Planar(-_player!.GlobalBasis.Z);
            _travel = _travel.LengthSquared() < 0.0001f ? Vector3.Forward : _travel.Normalized();
            var lateral = Planar(GlobalPosition - _player.GlobalPosition).Dot(_travel.Cross(Vector3.Up));
            _followSide = lateral < 0 ? -1.0f : 1.0f;
        }
    }

    private bool HasPlayer() => _player != null && GodotObject.IsInstanceValid(_player) && _player.IsInsideTree() && IsInsideTree();

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

    // ---- Gestures: the Gubble's body answers (the magic design's "What the player sees"), moved here from the HUD ----

    /// <summary>The body's gestures. Each moves only the drawn body (its parts), never the avatar or its collider.</summary>
    public enum Gesture { None, Shake, Shiver, Wiggle, Shrug, Cast, CastSelf }

    public const double ShakeS = 0.6;
    public const double ShiverS = 1.2;
    public const double WiggleS = 0.45;
    public const double ShrugS = 0.6;
    /// <summary>The cast: a quick lean toward the spot (a toss) that peaks at CastPeakFraction, then settles; the look's spark leaves at its start.</summary>
    public const double CastS = 0.55;
    public const float CastPeakFraction = 0.25f;
    /// <summary>How far the body leans toward the spot it casts at, in radians.</summary>
    public const float CastLeanRad = 0.38f;
    /// <summary>A glow on itself: the body lifts this many body heights and settles.</summary>
    public const float CastLiftHeights = 0.14f;
    /// <summary>How dark the body goes when it dims (an overlay's alpha).</summary>
    public const float DimAlpha = 0.45f;

    /// <summary>The gesture playing now, and the last one played.</summary>
    public Gesture Playing { get; private set; }
    public Gesture LastGesture { get; private set; }
    /// <summary>Where the last cast gesture leaned (the spot), or null for a glow on itself.</summary>
    public Vector3? LastCastToward { get; private set; }
    public bool Dimmed { get; private set; }

    private double _gestureAge;
    private double _gestureS;
    private Vector3 _castDirection = Vector3.Forward;
    private readonly System.Collections.Generic.List<(Node3D Node, Transform3D Pose)> _rest = new();
    private readonly System.Collections.Generic.List<(GeometryInstance3D Mesh, Material? Before)> _dimmed = new();
    private static readonly StandardMaterial3D DimOverlay = new()
    {
        ResourceName = "gubble dim", AlbedoColor = new Color(0.02f, 0.03f, 0.05f, DimAlpha), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };

    /// <summary>A refusal: side to side about its upright, no.</summary>
    public void Shake() => Play(Gesture.Shake, ShakeS);
    /// <summary>The dusk moment: a fast small tremble.</summary>
    public void Shiver() => Play(Gesture.Shiver, ShiverS);
    /// <summary>Done: a happy bounce.</summary>
    public void Wiggle() => Play(Gesture.Wiggle, WiggleS);
    /// <summary>Not yet: up and down once.</summary>
    public void Shrug() => Play(Gesture.Shrug, ShrugS);

    /// <summary>
    /// The cast (the Glow playtest: "the Gubble placing the glowing effect"): a quick lean toward the spot, a toss, or a lift for
    /// a glow on itself (toward null). Call it as the cast starts: the look's spark leaves the body then (StartGlow), and the
    /// lean peaks CastPeakFraction into the gesture, so the body follows through as the spark flies.
    /// </summary>
    public void CastGesture(Vector3? toward)
    {
        LastCastToward = toward;
        var direction = toward is { } spot && spot.IsFinite() && IsInsideTree() ? ToLocal(spot) : Vector3.Zero;
        direction.Y = 0;
        _castDirection = direction.LengthSquared() > 1e-6f ? direction.Normalized() : Vector3.Forward;
        Play(toward == null ? Gesture.CastSelf : Gesture.Cast, CastS);
    }

    /// <summary>Dim or undim the body: an overlay on its drawn meshes.</summary>
    public void Dim(bool on)
    {
        if (on == Dimmed) return;
        Dimmed = on;
        if (on && VisualRoot != null)
        {
            foreach (var node in VisualRoot.FindChildren("*", "GeometryInstance3D", true, false))
                if (node is GeometryInstance3D mesh)
                {
                    _dimmed.Add((mesh, mesh.MaterialOverlay));
                    mesh.MaterialOverlay = DimOverlay;
                }
            return;
        }
        foreach (var (mesh, before) in _dimmed)
            if (GodotObject.IsInstanceValid(mesh) && mesh.MaterialOverlay == DimOverlay) mesh.MaterialOverlay = before;
        _dimmed.Clear();
    }

    private void Play(Gesture gesture, double seconds)
    {
        if (VisualRoot == null || !GodotObject.IsInstanceValid(VisualRoot)) return;
        if (Playing == Gesture.None)
        {
            _rest.Clear();
            foreach (var child in VisualRoot.GetChildren())
                if (child is Node3D part) _rest.Add((part, part.Transform));
        }
        Playing = gesture;
        LastGesture = gesture;
        _gestureAge = 0;
        _gestureS = seconds;
    }

    /// <summary>0 to 1 and back: up by CastPeakFraction of the gesture, eased down after.</summary>
    public static float CastEnvelope(float t) => t <= 0 || t >= 1 ? 0f
        : t < CastPeakFraction ? Mathf.Sin(t / CastPeakFraction * Mathf.Pi * 0.5f) : Mathf.Cos((t - CastPeakFraction) / (1f - CastPeakFraction) * Mathf.Pi * 0.5f);

    /// <summary>The pose of the gesture playing, at t (0 to 1) of it.</summary>
    private Transform3D GesturePose(float t)
    {
        var fade = 1f - t;
        var h = BodyHeightM;
        return Playing switch
        {
            Gesture.Shake => new Transform3D(new Basis(Vector3.Up, 0.45f * fade * Mathf.Sin(t * Mathf.Tau * 3f)), Vector3.Zero),
            Gesture.Shiver => new Transform3D(Basis.Identity, new Vector3(0.012f * h * Mathf.Sin(t * Mathf.Tau * 14f), 0, 0)),
            Gesture.Wiggle => new Transform3D(new Basis(Vector3.Back, 0.18f * fade * Mathf.Sin(t * Mathf.Tau * 2f)), Vector3.Up * (0.08f * h * Mathf.Sin(t * Mathf.Pi))),
            // The top tips toward the spot: a turn about up x direction. Fast in, slow out, with a little hop.
            Gesture.Cast => new Transform3D(new Basis(Vector3.Up.Cross(_castDirection).Normalized(), CastLeanRad * CastEnvelope(t)), Vector3.Up * (0.05f * h * Mathf.Sin(t * Mathf.Pi))),
            Gesture.CastSelf => new Transform3D(Basis.Identity, Vector3.Up * (CastLiftHeights * h * CastEnvelope(t))),
            _ => new Transform3D(Basis.Identity, Vector3.Up * (0.06f * h * Mathf.Sin(t * Mathf.Pi))),
        };
    }

    private void UpdateGesture(double delta)
    {
        if (Playing == Gesture.None) return;
        _gestureAge += delta;
        var t = (float)(_gestureAge / _gestureS);
        if (t >= 1f)
        {
            RestorePose();
            return;
        }
        // Turned about the body's middle, so a shake turns it in place; a cast's lean tips it about its feet.
        var pivot = Playing == Gesture.Cast ? Vector3.Zero : Vector3.Up * (BodyHeightM * 0.5f);
        var about = new Transform3D(Basis.Identity, pivot) * GesturePose(t) * new Transform3D(Basis.Identity, -pivot);
        foreach (var (node, pose0) in _rest)
            if (GodotObject.IsInstanceValid(node)) node.Transform = about * pose0;
    }

    private void RestorePose()
    {
        foreach (var (node, pose0) in _rest)
            if (GodotObject.IsInstanceValid(node)) node.Transform = pose0;
        _rest.Clear();
        Playing = Gesture.None;
    }

    public override void _ExitTree()
    {
        RestorePose();
        Dim(false);
        base._ExitTree();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_entered) return;
        var dt = Mathf.Clamp((float)delta, 0, 0.05f);
        if (_homePending && HasPlayer() && (_homePendingAge += dt) >= 0.5)
        {
            _homePendingAge = 0;
            if (!_player!.GoingHome && PlaceNearHome(_player))
            {
                _homePending = false;
                FloatAltitudeFloorY = _holdY = null;
                _floating = false;
                WentHomeWithPlayer++;
            }
        }
        GoalBlocked = false;
        FollowingRoute = false;
        // Walking hovers over the ground and ducks under what is low; only a float rises over things (set by the goals below).
        RiseOverObstacles = false;
        FloatAltitudeFloorY = _holdY;
        var desired = Vector3.Zero;
        var hasPlayer = HasPlayer();
        // Gravity is a world property: the companion lives under the same profile the player was given.
        if (hasPlayer && _player!.WorldPhysicsRevision > WorldPhysicsRevision) SetWorldPhysics(_player.WorldPhysicsProfile);
        if (hasPlayer) TrackPlayerMotion(dt);
        if (InputEnabled && hasPlayer)
        {
            var playerOffset = Planar(GlobalPosition - _player!.GlobalPosition);
            if (CurrentIntent == "follow") desired = FollowVelocity(playerOffset, dt);
            // Stay may yield, but an explicit Stop cancels navigation. The player
            // can still pass because its collision mask excludes the companion. Floating above or below the player (over
            // the wall it rose over), it is in nobody's way.
            else if (CurrentIntent != "stop" && playerOffset.Length() < YieldM && Mathf.Abs(GlobalPosition.Y - _player.GlobalPosition.Y) <= LevelGapM)
                desired = (playerOffset.LengthSquared() < 0.0001f ? GlobalBasis.X : playerOffset.Normalized()) * WalkSpeedMps;
            else if (CurrentIntent == "come") desired = ComeVelocity(playerOffset, dt);
            else if (CurrentIntent == "go_to") desired = GoToVelocity(dt);
        }
        var trying = desired.LengthSquared() > 0.0025f;
        if (desired.LengthSquared() > 0.0004f) MoveWith(desired, dt);
        else
        {
            SetControlInput(Vector2.Zero);
            // At rest beside the player, turn to face them; the player turning on the spot never moves the companion.
            if (CurrentIntent == "follow" && hasPlayer)
            {
                var toPlayer = Planar(_player!.GlobalPosition - GlobalPosition);
                if (toPlayer.LengthSquared() > 0.0001f) TurnToward(toPlayer, 0.5f * CompanionTurnRateRadPerS * dt, 0.6f);
            }
        }
        WatchProgress(dt, trying);
        if (_stuck) GoalBlocked = true;
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

    /// <summary>
    /// Loose follow: rest anywhere inside the band. When the player walks off, or the companion is outside the
    /// band, steer to a place beside the player's line of travel on the side it is already on, adding the
    /// player's velocity so it neither lags nor oscillates. The line of travel changes only when the player
    /// moves, so turning on the spot never re-targets the companion.
    /// </summary>
    private Vector3 FollowVelocity(Vector3 playerOffset, float dt)
    {
        var distance = playerOffset.Length();
        // How the player really moves, not the velocity it asks for: pushing into a face before a climb, the asked velocity
        // flickers between a step and nothing from one tick to the next, and follow started and stopped with it.
        var playerVelocity = Planar(_playerMotion);
        var playerSpeed = playerVelocity.Length();
        var walking = playerSpeed > 0.05f;
        if (walking) _travel = playerVelocity / playerSpeed;
        // A climber going up or down the face is on the move too, though not across the ground.
        var moving = walking || (_player!.IsClimbing && Mathf.Abs(_playerMotion.Y) > 0.05f);
        // Follow rests only once the player has stood still a moment: a player edging into a face moves every other tick.
        _playerStillS = moving ? 0 : _playerStillS + dt;
        var still = _playerStillS >= PlayerStillS;
        var right = _travel.Cross(Vector3.Up);
        var lateral = playerOffset.Dot(right);
        // It changes side only when it is clearly on the other one (1.5 body radii off the line; was 6 cm).
        if (Mathf.Abs(lateral) > 0.03f) _followSide = Mathf.Sign(lateral);
        var place = FollowPlace(right);
        var toPlace = Planar(place - GlobalPosition);
        var placeDistance = toPlace.Length();
        // Resting aloft (held at its height beside a climber, or over the drop beside a player up a tree), its place moves away
        // up and down as well as across.
        var aloft = _holdY != null;
        var placeGap = aloft ? (place - GlobalPosition).Length() : placeDistance;
        // Something between the companion and its place (it is behind the box): that is out of the band too. Floating, or
        // resting aloft, it goes straight there, so a walk's way round is no matter.
        var routed = PlanRoute(place, 0.05f, dt);
        var detour = routed && !_route.Direct && _route.LengthM > placeDistance + DetourM && !_floating && !aloft;
        // In a narrow gap the walkable place can be nearer than the band's edge; never chase away from it.
        var near = Mathf.Min(FollowNearM, Planar(place - _player!.GlobalPosition).Length() - 0.02f);
        // Far above or below the player (up a cliff, in a tree, across the water from a bank) is out of the band too.
        var apart = Mathf.Abs(GlobalPosition.Y - _player.GlobalPosition.Y) > LevelGapM;
        if (!_following)
        {
            _following = distance > FollowFarM || distance < near || detour || apart || (moving && placeGap > 0.06f);
            if (_following) FloatAltitudeFloorY = _holdY = null;
        }
        // Floating, it rests only once level with its place: resting lower, it would hold there and fall behind at once.
        else if (still && !detour && !apart && (!_floating || Mathf.Abs(GlobalPosition.Y - place.Y) <= WalkLevelM) &&
                 (placeDistance < 0.02f || (distance > near + 0.02f && distance < FollowFarM - 0.06f)))
            _following = false;
        if (!_following)
        {
            // Come to rest while floating, it holds its height there, as an arrival by floating does. It used to sink to the
            // ground below at once, which opened the gap to a climber again and floated it back up, over and over: the founder's
            // video (9 October) of the HUD flipping between "follow" and "floating there" and the Gubble shaking beside a climber.
            if (_floating) _holdY = GlobalPosition.Y;
            _floating = false;
            return Vector3.Zero;
        }
        if (ChooseFloat(place, routed))
        {
            RiseOverObstacles = true;
            FloatAltitudeFloorY = place.Y;
            var floating = (moving ? playerVelocity : Vector3.Zero) + toPlace * FollowGainPerS;
            if (!moving && floating.Length() < MinApproachMps && placeDistance > 0.005f) floating = floating.Normalized() * MinApproachMps;
            return floating.LimitLength(RunSpeedMps);
        }
        // Honest about a place it cannot reach: it goes as near as the floor allows and says blocked.
        if (routed && !_route.Reaches) GoalBlocked = true;
        if (detour || (routed && !_route.Reaches))
            return RouteVelocity(Mathf.Clamp(_route.LengthM * FollowGainPerS + playerSpeed, MinApproachMps, RunSpeedMps));
        var desired = (moving ? playerVelocity : Vector3.Zero) + toPlace * FollowGainPerS;
        // Finish the last few centimetres briskly instead of creeping.
        if (!moving && desired.Length() < MinApproachMps && placeDistance > 0.005f) desired = desired.Normalized() * MinApproachMps;
        return desired.LimitLength(RunSpeedMps);
    }

    /// <summary>
    /// The player's motion over the last tick (metres a second): where it went, not the velocity it asked for. A jump of more
    /// than PlayerJumpM in one tick is a teleport (a recovery, a wash ashore), not motion.
    /// </summary>
    private void TrackPlayerMotion(float dt)
    {
        var at = _player!.GlobalPosition;
        var step = at - _playerLast;
        _playerMotion = _playerSeen && dt > 0 && step.Length() < PlayerJumpM ? step / dt : Vector3.Zero;
        _playerLast = at;
        _playerSeen = true;
    }

    private const float PlayerJumpM = 0.25f;
    /// <summary>How long the player stands still before follow comes to rest.</summary>
    public const float PlayerStillS = 0.25f;

    /// <summary>The place beside the player on the walkable side: when a wall or furniture covers that side, the other one.</summary>
    private Vector3 FollowPlace(Vector3 right)
    {
        // A climbing player: just off the face beside them, where the companion floats while they climb.
        if (_player!.IsClimbing)
        {
            var outward = Planar(_player.ClimbNormal);
            if (outward.LengthSquared() > 0.0001f) return _player.GlobalPosition + outward.Normalized() * FollowSideM;
        }
        Vector3 At(float side) => _player!.GlobalPosition + right * (side * FollowSideM) + _travel * FollowLeadM;
        var place = At(_followSide);
        if (!NavigationReady) return place;
        // How far a place is from the walkable mesh: across, or up and down beyond the level band (the place beside a
        // player on a crown or a cliff top is over ground far below, the place beside a swimmer over the pond's bed).
        float Off(Vector3 wanted, Vector3 snapped) =>
            Mathf.Max(Planar(snapped - wanted).Length(), Mathf.Abs(snapped.Y - wanted.Y) > LevelGapM ? float.PositiveInfinity : 0);
        var snapped = Navigation!.ClosestPoint(place);
        var offMesh = Off(place, snapped);
        // Within an agent radius of the walkable mesh, the place is only nudged off a wall (was 8 cm, the old agent radius).
        if (offMesh <= Navigation.AgentRadiusM) return snapped;
        var other = At(-_followSide);
        var otherSnapped = Navigation.ClosestPoint(other);
        var otherOff = Off(other, otherSnapped);
        if (otherOff <= Navigation.AgentRadiusM || otherOff < offMesh - 0.5f * Navigation.AgentRadiusM)
        {
            _followSide = -_followSide;
            (place, snapped, offMesh) = (other, otherSnapped, otherOff);
        }
        // Far from anything walkable (over water, up a tree, out at sea): the place itself, where the companion floats.
        return float.IsPositiveInfinity(offMesh) || offMesh > 2 * Navigation.AgentRadiusM ? place : snapped;
    }

    /// <summary>
    /// Where come makes for: the player, or for a player under water (a diver), the spot over them at the surface, where a
    /// swimmer floats (Codex Sol's review: the Gubble hovered over a diver for ever, never level with them).
    /// </summary>
    private Vector3 ComeTarget()
    {
        var at = _player!.GlobalPosition;
        var water = _player.Water;
        if (water.Wet && at.Y < water.SurfaceY - _player.SwimFloatDepthM) at.Y = water.SurfaceY - _player.SwimFloatDepthM;
        return at;
    }

    private Vector3 ComeVelocity(Vector3 playerOffset, float dt)
    {
        var distance = playerOffset.Length();
        var target = ComeTarget();
        var routed = PlanRoute(target, ComeArrivalM + 0.05f, dt);
        if (ChooseFloat(target, routed))
        {
            var level = Mathf.Abs(GlobalPosition.Y - target.Y) <= LevelGapM;
            var clear = target.Y > _player!.GlobalPosition.Y + 0.001f || ClearTo(_player.GlobalPosition + Vector3.Up * (_player.BodyHeightM * 0.5f), _player.GetRid());
            if (distance <= ComeArrivalM && level && clear)
            {
                ComeArrivedSerial = IntentSerial;
                ArriveFloating();
                return Vector3.Zero;
            }
            RiseOverObstacles = true;
            FloatAltitudeFloorY = target.Y;
            // Not yet level with the player, or something between: it keeps on toward them (up the face in its way, over the
            // wall and down beside them) until it arrives.
            if (distance < 1e-4f) return Vector3.Zero;
            var approach = Mathf.Clamp((distance - ComeArrivalM + 0.02f) * FollowGainPerS, MinApproachMps, RunSpeedMps);
            return -playerOffset / distance * approach;
        }
        // Close in a straight line but with a wall or box between does not count as arrived, and neither does a
        // player it cannot reach at all (across a thin wall): that is blocked, honestly (Lane P review).
        var detour = routed && !_route.Direct && _route.LengthM > distance + DetourM;
        if (distance <= ComeArrivalM && !detour && (!routed || _route.Reaches))
        {
            // Arrived: the command host finishes this come's job (the serial tells it which come it was).
            ComeArrivedSerial = IntentSerial;
            Stay();
            return Vector3.Zero;
        }
        if (routed && !_route.Reaches) GoalBlocked = true;
        if (detour || (routed && !_route.Reaches))
            return RouteVelocity(Mathf.Clamp((_route.LengthM - ComeArrivalM + 0.02f) * FollowGainPerS, MinApproachMps, RunSpeedMps));
        var speed = Mathf.Clamp((distance - ComeArrivalM + 0.02f) * FollowGainPerS, MinApproachMps, RunSpeedMps);
        return -playerOffset / distance * speed;
    }

    /// <summary>Toward the nearest point of the go_to box's footprint; arrived within _goToArrivalM of it, then stays.</summary>
    private Vector3 GoToVelocity(float dt)
    {
        var here = GlobalPosition;
        var low = _goToTarget.Position;
        var high = _goToTarget.End;
        var nearest = new Vector3(Mathf.Clamp(here.X, low.X, high.X), Mathf.Clamp(here.Y, low.Y, high.Y), Mathf.Clamp(here.Z, low.Z, high.Z));
        var distance = Planar(nearest - here).Length();
        // The side of the box the walk can reach (backlog: the nearest reachable side, not the nearest side across a wall).
        var aim = ReachableSide(dt);
        var routed = PlanRoute(aim, _goToArrivalM, dt);
        if (ChooseFloat(aim, routed))
        {
            // Level with the box: its bottom no higher than the level band over the feet, its top no lower than reach under them.
            var level = low.Y - here.Y <= LevelGapM && here.Y - high.Y <= ReachM;
            var clear = ClearTo(nearest + (here - nearest).Normalized() * 0.01f, default);
            if (distance <= _goToArrivalM && level && clear)
            {
                GoToArrivedSerial = IntentSerial;
                ArriveFloating();
                return Vector3.Zero;
            }
            RiseOverObstacles = true;
            FloatAltitudeFloorY = low.Y;
            var toward = Planar(nearest - here);
            if (distance < 1e-4f) return Vector3.Zero;
            var approach = Mathf.Clamp((distance - _goToArrivalM + 0.02f) * FollowGainPerS, MinApproachMps, RunSpeedMps);
            return toward / distance * approach;
        }
        var offset = Planar(aim - here);
        var aimDistance = offset.Length();
        var detour = routed && !_route.Direct && _route.LengthM > aimDistance + DetourM;
        if (distance <= _goToArrivalM && !detour)
        {
            // Arrived: the command host finishes this go_to's job (the serial tells it which one it was).
            GoToArrivedSerial = IntentSerial;
            Stay();
            return Vector3.Zero;
        }
        if (routed && !_route.Reaches) GoalBlocked = true;
        if (detour || (routed && !_route.Reaches))
            return RouteVelocity(Mathf.Clamp((_route.LengthM - _goToArrivalM + 0.02f) * FollowGainPerS, MinApproachMps, RunSpeedMps));
        var speed = Mathf.Clamp((distance - _goToArrivalM + 0.02f) * FollowGainPerS, MinApproachMps, RunSpeedMps);
        return aimDistance > 0.0001f ? offset / aimDistance * speed : Vector3.Zero;
    }

    /// <summary>
    /// Where a go_to makes for: of the walkable points just outside the box's footprint (its sides and corners, and the
    /// nearest point), the one with the shortest walk that reaches it, chosen again as the map or the body moves on. Without
    /// a walkable map, or with no side the walk reaches, the nearest point of the footprint (the companion then floats there).
    /// </summary>
    private Vector3 ReachableSide(float dt)
    {
        var here = GlobalPosition;
        var low = _goToTarget.Position;
        var high = _goToTarget.End;
        var nearest = new Vector3(Mathf.Clamp(here.X, low.X, high.X), Mathf.Clamp(here.Y, low.Y, high.Y), Mathf.Clamp(here.Z, low.Z, high.Z));
        _goToAimAge += dt;
        if (!NavigationReady) return _goToAim = nearest;
        if (_goToAimRevision == Navigation!.Revision && _goToAimAge < 0.5) return _goToAim;
        _goToAimAge = 0;
        _goToAimRevision = Navigation.Revision;
        var centre = _goToTarget.GetCenter();
        var standOff = Navigation.AgentRadiusM;
        var best = nearest;
        var bestLength = float.PositiveInfinity;
        foreach (var (x, z) in new[] { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1), (2, 2) })
        {
            var outside = x == 2 ? nearest + Planar(nearest - centre).Normalized() * standOff
                : new Vector3(x < 0 ? low.X - standOff : x > 0 ? high.X + standOff : centre.X, low.Y, z < 0 ? low.Z - standOff : z > 0 ? high.Z + standOff : centre.Z);
            var candidate = Navigation.ClosestPoint(outside);
            var footprint = new Vector3(Mathf.Clamp(candidate.X, low.X, high.X), candidate.Y, Mathf.Clamp(candidate.Z, low.Z, high.Z));
            // It must stand within the arrival distance of the footprint, level with the box's bottom.
            if (Planar(candidate - footprint).Length() > _goToArrivalM || Mathf.Abs(candidate.Y - low.Y) > LevelGapM) continue;
            var route = Navigation.FindRoute(here, candidate, 0.02f);
            if (!route.Reaches || route.LengthM >= bestLength) continue;
            best = candidate;
            bestLength = route.LengthM;
        }
        return _goToAim = best;
    }

    /// <summary>
    /// Float or walk: the walk while the planned route reaches the goal, level with it, and not a long way round; otherwise
    /// float. Once floating it floats until the goal changes, it arrives, or follow comes to rest.
    /// </summary>
    private bool ChooseFloat(Vector3 goal, bool routed)
    {
        if (_floating) return true;
        // A walk starts from the ground under the body: one held aloft (beside a climber, by a player up a tree) floats.
        if (routed && _route.Reaches && Mathf.Abs(_route.Points[^1].Y - goal.Y) <= WalkLevelM && Mathf.Abs(_route.Points[0].Y - GlobalPosition.Y) <= LevelGapM &&
            _route.LengthM <= (goal - GlobalPosition).Length() * FloatDetourFactor + FloatDetourM) return false;
        _floating = true;
        FloatStarts++;
        return true;
    }

    /// <summary>Nothing solid between the body's middle and a point (the point's own body left out).</summary>
    private bool ClearTo(Vector3 point, Rid ignore)
    {
        var from = GlobalPosition + Vector3.Up * (BodyHeightM * 0.5f);
        using var query = PhysicsRayQueryParameters3D.Create(from, point, RoomBuilder.BodyMask);
        var exclude = new Godot.Collections.Array<Rid> { GetRid() };
        if (ignore.IsValid) exclude.Add(ignore);
        query.Exclude = exclude;
        using var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0;
    }

    /// <summary>Arrived by floating: it stays, holding its height (beside a player up a tree, it does not sink to the ground below).</summary>
    private void ArriveFloating()
    {
        var height = GlobalPosition.Y;
        Stay();
        _holdY = height;
    }

    private bool NavigationReady => Navigation != null && GodotObject.IsInstanceValid(Navigation) && Navigation.IsReady;

    /// <summary>Plan a route to the goal, or keep the current one while it is fresh. False when there is no navigation to plan on.</summary>
    private bool PlanRoute(Vector3 goal, float toleranceM, float dt)
    {
        if (!NavigationReady)
        {
            _route = RoomNavigation.Route.None;
            return false;
        }
        _routeAge += dt;
        if (_route.IsEmpty || _routeAge >= RouteRefreshS || _routeRevision != Navigation!.Revision || Planar(goal - _routeGoal).Length() > 0.03f)
        {
            _route = Navigation!.FindRoute(GlobalPosition, goal, toleranceM);
            _routeIndex = 1;
            _routeGoal = goal;
            _routeAge = 0;
            _routeRevision = Navigation.Revision;
        }
        while (_routeIndex < _route.Points.Length - 1 && Planar(_route.Points[_routeIndex] - GlobalPosition).Length() < CornerReachedM) _routeIndex++;
        return !_route.IsEmpty;
    }

    /// <summary>Steer for the next corner of the planned route; zero at its end.</summary>
    private Vector3 RouteVelocity(float speed)
    {
        var points = _route.Points;
        if (points.Length == 0) return Vector3.Zero;
        var last = _routeIndex >= points.Length - 1;
        var toCorner = Planar(points[Mathf.Min(_routeIndex, points.Length - 1)] - GlobalPosition);
        if (last && toCorner.Length() < CornerReachedM) return Vector3.Zero;
        FollowingRoute = true;
        return toCorner.Normalized() * (last ? Mathf.Min(speed, Mathf.Max(MinApproachMps, toCorner.Length() * FollowGainPerS)) : speed);
    }

    /// <summary>Report blocked when trying to move has covered almost nothing for a second, and plan again.</summary>
    private void WatchProgress(float dt, bool trying)
    {
        if (!trying)
        {
            _progressTime = 0;
            _progressAnchor = GlobalPosition;
            _stuck = false;
            return;
        }
        _progressTime += dt;
        if (_progressTime < StuckAfterS) return;
        // Floating up a cliff or over a wall is progress too.
        _stuck = (_floating ? GlobalPosition - _progressAnchor : Planar(GlobalPosition - _progressAnchor)).Length() < StuckDistanceM;
        if (_stuck) _routeAge = RouteRefreshS;
        _progressTime = 0;
        _progressAnchor = GlobalPosition;
    }

    /// <summary>Move along a world-space velocity: the motion is exact through body-relative input while the body turns smoothly to face it.</summary>
    private void MoveWith(Vector3 desired, float dt)
    {
        var speed = desired.Length();
        // Floating heads straight for its goal (the body rises over or ducks under what is in the way); walking steers locally.
        var heading = _floating ? desired / speed : ChooseClearDirection(desired / speed);
        if (heading.LengthSquared() == 0)
        {
            GoalBlocked = true;
            SetControlInput(Vector2.Zero);
            Velocity = new Vector3(0, Velocity.Y, 0);
            return;
        }
        if (!_hasLookTarget) TurnToward(heading, CompanionTurnRateRadPerS * dt, 0);
        var local = GlobalBasis.Inverse() * heading;
        SetControlInput(new Vector2(local.X, -local.Z) * Mathf.Min(1, speed / RunSpeedMps), sprint: true);
    }

    private void TurnToward(Vector3 direction, float maximumStep, float deadZone)
    {
        var yaw = Mathf.Atan2(-direction.X, -direction.Z);
        if (Mathf.Abs(Mathf.AngleDifference(Rotation.Y, yaw)) <= deadZone) return;
        Rotation = new Vector3(0, Mathf.RotateToward(Rotation.Y, yaw, maximumStep), 0);
    }

    private static Vector3 Planar(Vector3 value) => new(value.X, 0, value.Z);

    /// <summary>A heading without its components that would carry the body past a bound it stands within a probe of.</summary>
    private Vector3 AlongBounds(Vector3 heading)
    {
        if (PlayableBounds is not { } bounds || !BoundsHoldSides) return heading;
        var here = GlobalPosition;
        var reach = BodyRadiusM + SteeringProbeM;
        if ((heading.X > 0 && here.X + reach > bounds.End.X) || (heading.X < 0 && here.X - reach < bounds.Position.X)) heading.X = 0;
        if ((heading.Z > 0 && here.Z + reach > bounds.End.Z) || (heading.Z < 0 && here.Z - reach < bounds.Position.Z)) heading.Z = 0;
        return heading;
    }

    private Vector3 ChooseClearDirection(Vector3 desired)
    {
        // Local steering for the last stretch, round the player and round anything the navigation mesh does
        // not hold; routes round furniture come from RoomNavigation. Blocked reports blocked, never teleports.
        foreach (var angle in new[] { 0.0f, -0.65f, 0.65f, -1.15f, 1.15f })
        {
            var candidate = desired.Rotated(Vector3.Up, angle);
            // The room's bounds are a wall to the body (PlayableBounds): a heading across one keeps only its part along it.
            if (!InsidePlayableBounds(GlobalPosition + candidate * SteeringProbeM))
            {
                candidate = AlongBounds(candidate);
                if (candidate.LengthSquared() < 0.04f) continue;
                candidate = candidate.Normalized();
            }
            var probe = candidate * SteeringProbeM;
            // A floating body needs no ground ahead: off a ledge or over water it hovers over what is there.
            if (!Floats && !HasSupportNear(GlobalPosition + probe)) continue;
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
