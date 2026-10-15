using Godot;
using EnFractal.Native.Room;
using EnFractal.Native.Seams;

namespace EnFractal.Native;

/// <summary>
/// The body's side of the mechanics round (docs/runs/RUN-2-MECHANICS.md, packet M5): the knacks that move its own numbers, what it
/// reports to the kernel's stamina ledger each tick (running, deep water, diving) and what it obeys from the ledger's gate (an empty
/// bar: the grip fails, deep water carries it to shore, no running until the first bar refills). Everything here is the controller's and
/// none of it touches the collision capsule, the size, the eye height or the reach.
/// </summary>
public partial class SmallPlayerController
{
    // ---- Knacks (docs/game/MECHANICS.md section 7): on the controller, never in a drawn character's motion profile ----

    /// <summary>
    /// The team's knacks that move this body's numbers: set by the kernel when the pack or the team's ranks change (Kernel/CommandHost),
    /// or by a test. None, today's body, until then. The companion's stays none: it has no knacks.
    /// </summary>
    public BodyKnacks Knacks
    {
        get => _knacks;
        set => _knacks = value ?? BodyKnacks.None;
    }
    private BodyKnacks _knacks = BodyKnacks.None;

    /// <summary>
    /// The pace on land now: the base (WalkSpeedMps, RunSpeedMps) times Quick feet. A floating body has no knack. Wading and
    /// swimming have their own factors on top (Walk, Swim). An empty bar takes the run away (the gate's NoRun), never the walk.
    /// </summary>
    private float LandSpeedFactor => Floats ? 1f : _knacks.WalkRunFactor;
    public float WalkSpeedNowMps => WalkSpeedMps * LandSpeedFactor;
    public float RunSpeedNowMps => RunSpeedMps * LandSpeedFactor;

    /// <summary>The hop's apex above the take-off floor in the room's default gravity and heavier, with High hop (JumpApexM is the base).</summary>
    public float HopApexM => JumpApexM * (Floats ? 1f : _knacks.HopFactor);
    /// <summary>The highest leap in the lightest gravity, with High hop: the cap rises with the hop, so the knack is felt on the moon too.</summary>
    public float MaxHopApexM => MaxJumpApexM * (Floats ? 1f : _knacks.HopFactor);

    /// <summary>The swim's pace at the surface (the plain swim, or the walk itself with Shift) with Strong swimmer. The dive uses it too.</summary>
    private float SwimPaceMps(bool sprint) => WalkSpeedMps * (sprint ? 1.0f : SwimSpeedFactor) * (Floats ? 1f : _knacks.SwimFactor);
    /// <summary>The plain swim's speed at the surface, and the dive's under it, with Strong swimmer (m/s). Neither is slowed by an empty bar: the water has its own rule.</summary>
    public float SwimSpeedNowMps => SwimPaceMps(false);
    public float DiveSpeedNowMps => SwimPaceMps(false) * DiveSpeedFactor;

    /// <summary>
    /// What a landing costs this body in health points (the vitals' curve with the team's safe drop; decision 24: absolute, before any
    /// difficulty). The kernel hurts by it and the tumble's look reads it.
    /// </summary>
    public float LandingCost(float landingSpeedMps, bool deepWater) => _knacks.FallDamage(landingSpeedMps, deepWater);

    // ---- The stamina ledger (Seams/StaminaLedger.cs): report, then obey ----

    /// <summary>A moving body counts as running once it covers at least this share of a walk's distance in the tick (it is not pushing a wall).</summary>
    public const float RunningShareOfWalk = 0.5f;

    private StaminaGate _gate = StaminaGate.Free;

    /// <summary>The gate the ledger held after the last tick: the bar's emptiness and whether the body may run. Free when no ledger is bound.</summary>
    public StaminaGate Gate => _gate;
    /// <summary>What the body reported to the ledger on the last tick.</summary>
    public BodyExertion LastExertion { get; private set; }
    /// <summary>Ticks reported to the ledger, and times a climb's grip failed for want of stamina.</summary>
    public int ExertionReports { get; private set; }
    public int GripFailures { get; private set; }

    /// <summary>
    /// What the bar pays for this tick beyond climbing: seconds running on foot (sprint held, moving), swimming in deep water at the
    /// surface, or under it; never more than one, never more than the tick. Floating bodies and held ones (a tumble, a trip home)
    /// exert nothing.
    /// </summary>
    private BodyExertion TakeExertion(float dt, float moved, bool onFoot, bool swam, bool sprint, Vector2 control)
    {
        var exertion = BodyExertion.None;
        if (!(dt > 0) || Floats || GoingHome || _controlHoldS > 0) { LastExertion = exertion; return exertion; }
        if (swam) exertion = IsDiving ? new BodyExertion(0, 0, dt) : new BodyExertion(0, dt, 0);
        else if (onFoot && sprint && control.LengthSquared() > 0.01f && moved >= WalkSpeedNowMps * dt * RunningShareOfWalk) exertion = new BodyExertion(dt, 0, 0);
        LastExertion = exertion;
        return exertion;
    }

    /// <summary>The gate after the tick's report and VitalsTick (the ledger's); free with no ledger.</summary>
    private void TakeGate() => _gate = _staminaLedger?.Gate ?? StaminaGate.Free;

    // ---- Out of stamina in deep water: carried to shore ----

    /// <summary>
    /// How fast an empty swimmer is carried to shore, m/s: slower than the plain swim (0.19), faster than the diver's drift up (0.03).
    /// Not in the design, which says only "drifts to shore": a number for the founder to tune by feel.
    /// </summary>
    [Export] public float ShoreDriftMps { get; set; } = 0.10f;
    /// <summary>
    /// How the shore is looked for: one ring of sight round the swimmer per tick, nearest first (a ring every 4 cm to 1.2 m, every
    /// 10 cm to 3 m, every 40 cm to 12.8 m), so no tick pays for more than the sixteen places on a ring. The first ring that holds a
    /// shore sets the heading, and the search starts again from the swimmer's side as it moves.
    /// </summary>
    public static float ShoreRingM(int ring) => ring < 30 ? 0.04f * (ring + 1) : ring < 48 ? 1.2f + 0.1f * (ring - 29) : 3.0f + 0.4f * (ring - 47);
    private const int ShoreRings = 72;
    private const int ShoreDirections = 16;

    /// <summary>The way to shore now (a unit vector across the ground), or zero when none is known yet.</summary>
    public Vector3 ShoreHeading { get; private set; }
    /// <summary>Ticks spent carried to shore.</summary>
    public int ShoreDriftTicks { get; private set; }
    private int _shoreRing;
    private ulong _shoreTick;

    /// <summary>
    /// The velocity of an empty swimmer at the surface: toward the nearest shore at ShoreDriftMps, across the water and nothing else.
    /// Shore is a place the swimmer can stand without climbing: water shallower than a swimmer floats in, or dry ground no higher than
    /// the water's rim (a steep bank is no shore, since the grip fails). Where none is in sight, the sea's nearest beach.
    /// </summary>
    private Vector3 ShoreDrift(Vector3 feet)
    {
        // A drift that did not go on from the last tick is a new one: look again from the start.
        if (_bodyTick > _shoreTick + 1)
        {
            ShoreHeading = Vector3.Zero;
            _shoreRing = 0;
        }
        _shoreTick = _bodyTick;
        ShoreDriftTicks++;
        LookForShore(feet);
        return ShoreHeading * ShoreDriftMps;
    }

    private void LookForShore(Vector3 feet)
    {
        var space = GetWorld3D().DirectSpaceState;
        var radius = ShoreRingM(_shoreRing);
        Vector3? best = null;
        var bestAlignment = -2f;
        for (var step = 0; step < ShoreDirections; step++)
        {
            var angle = Mathf.Tau * step / ShoreDirections;
            var heading = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            if (!StandableShore(space, feet + heading * radius)) continue;
            // Among the places of a ring, the heading it already had: a swimmer between two shores does not dither.
            var alignment = ShoreHeading == Vector3.Zero ? 0 : heading.Dot(ShoreHeading);
            if (alignment > bestAlignment) (best, bestAlignment) = (heading, alignment);
        }
        if (best is { } found)
        {
            ShoreHeading = found;
            _shoreRing = 0;
            return;
        }
        if (++_shoreRing < ShoreRings) return;
        // Nothing in sight: the sea's own beach, or the way to the island's middle; otherwise the swimmer holds its place.
        _shoreRing = 0;
        if (ShoreHeading != Vector3.Zero || Sea == null) return;
        var here = new Vector2(feet.X, feet.Z);
        var toward = Sea.NearestBeach(here) is { } beach ? new Vector2(beach.WashAshoreM.X, beach.WashAshoreM.Z) : Sea.MiddleM;
        var way = toward - here;
        if (way.LengthSquared() > 1e-6f) ShoreHeading = new Vector3(way.X, 0, way.Y).Normalized();
    }

    /// <summary>Whether a swimmer carried here would find its feet and could leave the water without climbing.</summary>
    private bool StandableShore(PhysicsDirectSpaceState3D space, Vector3 feet)
    {
        var above = Mathf.Max(BodyHeightM * 4, Water.SurfaceY - feet.Y + 0.05f);
        var column = RoomWater.At(space, feet, above, 0.05f, GetRid(), throughRoof: true);
        if (!column.Wet && Sea != null) column = Sea.OpenWater(space, feet, above, 0.05f, GetRid(), throughRoof: true);
        if (column.Wet) return column.DepthM < SwimExitDepthM;
        // Dry: ground that faces up and is no higher than the water's rim (a beach or a ramp, not a bank).
        using var query = PhysicsRayQueryParameters3D.Create(feet + Vector3.Up * (BodyHeightM * 0.5f), feet + Vector3.Down * BodyHeightM, RoomBuilder.BodyMask, ExcludeSelf);
        query.HitBackFaces = false;
        using var hit = space.IntersectRay(query);
        if (hit.Count == 0 || hit[HitNormal].AsVector3().Y < Mathf.Cos(FloorMaxAngle)) return false;
        return hit[HitPosition].AsVector3().Y <= Water.SurfaceY + RoomWater.RimM;
    }
}
