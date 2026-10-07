using Godot;
using EnFractal.Native.Navigation;

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
    public string CompanionName { get; private set; } = "Wisp";
    public string CurrentIntent { get; private set; } = "stay";
    public bool GoalBlocked { get; private set; }
    public bool IsPointing => _pointer != null && _pointer.Visible;
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

    private SmallPlayerController? _player;
    private Node3D _pointer = null!;
    private Label3D _label = null!;
    private Vector3 _lookTarget;
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

    public override void _Ready()
    {
        ReadKeyboard = false;
        WalkSpeedMps = CompanionWalkMps;
        RunSpeedMps = CompanionRunMps;
        GroundAccelerationMps2 = CompanionGroundAccelerationMps2;
        AirAccelerationMps2 = CompanionAirAccelerationMps2;
        SetAppearance(new Color("65b9b0"));
        base._Ready();
        CollisionLayer = 4;
        CollisionMask = 1 | 2;
        _entered = true;
        // The label floats a little above the body; the pointing cue comes from chest height. Both are sized
        // from the body, so they shrank with it (the 0.24 m body had its label at 0.33 m, 36 mm text).
        var h = BodyHeightM;
        _label = new Label3D
        {
            Name = "CompanionLabel", Text = CompanionName + " · companion",
            Position = Vector3.Up * (h * 1.35f), FontSize = 30, PixelSize = h * 0.006f,
            Modulate = new Color("f6dfab"), OutlineModulate = new Color("18332d"),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = false
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
        if (_label != null) _label.Text = CompanionName + " · companion";
    }

    public void BindPlayer(SmallPlayerController player) => _player = player;

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

    private void BeginIntent(string intent)
    {
        CurrentIntent = intent;
        GoalBlocked = false;
        _hasLookTarget = false;
        _following = false;
        _route = RoomNavigation.Route.None;
        _stuck = false;
        _progressTime = 0;
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

    public override void _PhysicsProcess(double delta)
    {
        if (!_entered) return;
        var dt = Mathf.Clamp((float)delta, 0, 0.05f);
        GoalBlocked = false;
        FollowingRoute = false;
        var desired = Vector3.Zero;
        var hasPlayer = HasPlayer();
        // Gravity is a world property: the companion lives under the same profile the player was given.
        if (hasPlayer && _player!.WorldPhysicsRevision > WorldPhysicsRevision) SetWorldPhysics(_player.WorldPhysicsProfile);
        if (InputEnabled && hasPlayer)
        {
            var playerOffset = Planar(GlobalPosition - _player!.GlobalPosition);
            if (CurrentIntent == "follow") desired = FollowVelocity(playerOffset, dt);
            // Stay may yield, but an explicit Stop cancels navigation. The player
            // can still pass because its collision mask excludes the companion.
            else if (CurrentIntent != "stop" && playerOffset.Length() < YieldM)
                desired = (playerOffset.LengthSquared() < 0.0001f ? GlobalBasis.X : playerOffset.Normalized()) * WalkSpeedMps;
            else if (CurrentIntent == "come") desired = ComeVelocity(playerOffset, dt);
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
        var playerVelocity = Planar(_player!.Velocity);
        var playerSpeed = playerVelocity.Length();
        var moving = playerSpeed > 0.05f;
        if (moving) _travel = playerVelocity / playerSpeed;
        var right = _travel.Cross(Vector3.Up);
        var lateral = playerOffset.Dot(right);
        // It changes side only when it is clearly on the other one (1.5 body radii off the line; was 6 cm).
        if (Mathf.Abs(lateral) > 0.03f) _followSide = Mathf.Sign(lateral);
        var place = FollowPlace(right);
        var toPlace = Planar(place - GlobalPosition);
        var placeDistance = toPlace.Length();
        // Something between the companion and its place (it is behind the box): that is out of the band too.
        var routed = PlanRoute(place, 0.05f, dt);
        var detour = routed && !_route.Direct && _route.LengthM > placeDistance + DetourM;
        // In a narrow gap the walkable place can be nearer than the band's edge; never chase away from it.
        var near = Mathf.Min(FollowNearM, Planar(place - _player.GlobalPosition).Length() - 0.02f);
        if (!_following)
            _following = distance > FollowFarM || distance < near || detour || (moving && placeDistance > 0.06f);
        else if (!moving && !detour && (placeDistance < 0.02f || (distance > near + 0.02f && distance < FollowFarM - 0.06f)))
            _following = false;
        if (!_following) return Vector3.Zero;
        // Honest about a place it cannot reach: it goes as near as the floor allows and says blocked.
        if (routed && !_route.Reaches) GoalBlocked = true;
        if (detour || (routed && !_route.Reaches))
            return RouteVelocity(Mathf.Clamp(_route.LengthM * FollowGainPerS + playerSpeed, MinApproachMps, RunSpeedMps));
        var desired = (moving ? playerVelocity : Vector3.Zero) + toPlace * FollowGainPerS;
        // Finish the last few centimetres briskly instead of creeping.
        if (!moving && desired.Length() < MinApproachMps && placeDistance > 0.005f) desired = desired.Normalized() * MinApproachMps;
        return desired.LimitLength(RunSpeedMps);
    }

    /// <summary>The place beside the player on the walkable side: when a wall or furniture covers that side, the other one.</summary>
    private Vector3 FollowPlace(Vector3 right)
    {
        Vector3 At(float side) => _player!.GlobalPosition + right * (side * FollowSideM) + _travel * FollowLeadM;
        var place = At(_followSide);
        if (!NavigationReady) return place;
        var snapped = Navigation!.ClosestPoint(place);
        var offMesh = Planar(snapped - place).Length();
        // Within an agent radius of the walkable mesh, the place is only nudged off a wall (was 8 cm, the old agent radius).
        if (offMesh <= Navigation.AgentRadiusM) return snapped;
        var other = At(-_followSide);
        var otherSnapped = Navigation.ClosestPoint(other);
        if (Planar(otherSnapped - other).Length() >= offMesh - 0.5f * Navigation.AgentRadiusM) return snapped;
        _followSide = -_followSide;
        return otherSnapped;
    }

    private Vector3 ComeVelocity(Vector3 playerOffset, float dt)
    {
        var distance = playerOffset.Length();
        var routed = PlanRoute(_player!.GlobalPosition, ComeArrivalM + 0.05f, dt);
        // Close in a straight line but with a wall or box between does not count as arrived, and neither does a
        // player it cannot reach at all (across a thin wall): that is blocked, honestly (Lane P review).
        var detour = routed && !_route.Direct && _route.LengthM > distance + DetourM;
        if (distance <= ComeArrivalM && !detour && (!routed || _route.Reaches))
        {
            Stay();
            return Vector3.Zero;
        }
        if (routed && !_route.Reaches) GoalBlocked = true;
        if (detour || (routed && !_route.Reaches))
            return RouteVelocity(Mathf.Clamp((_route.LengthM - ComeArrivalM + 0.02f) * FollowGainPerS, MinApproachMps, RunSpeedMps));
        var speed = Mathf.Clamp((distance - ComeArrivalM + 0.02f) * FollowGainPerS, MinApproachMps, RunSpeedMps);
        return -playerOffset / distance * speed;
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
        _stuck = Planar(GlobalPosition - _progressAnchor).Length() < StuckDistanceM;
        if (_stuck) _routeAge = RouteRefreshS;
        _progressTime = 0;
        _progressAnchor = GlobalPosition;
    }

    /// <summary>Move along a world-space velocity: the motion is exact through body-relative input while the body turns smoothly to face it.</summary>
    private void MoveWith(Vector3 desired, float dt)
    {
        var speed = desired.Length();
        var heading = ChooseClearDirection(desired / speed);
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

    private Vector3 ChooseClearDirection(Vector3 desired)
    {
        // Local steering for the last stretch, round the player and round anything the navigation mesh does
        // not hold; routes round furniture come from RoomNavigation. Blocked reports blocked, never teleports.
        foreach (var angle in new[] { 0.0f, -0.65f, 0.65f, -1.15f, 1.15f })
        {
            var candidate = desired.Rotated(Vector3.Up, angle);
            var probe = candidate * SteeringProbeM;
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
