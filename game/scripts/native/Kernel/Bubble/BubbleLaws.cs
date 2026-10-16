namespace EnFractal.Native.Kernel;

/// <summary>
/// The Bubble's numbers (docs/game/MECHANICS.md section 5 names none but the prices, which the rules pack holds): one table of
/// provisional values, in the manner of NatureLaws, for the founder to tune by playing. The kernel reads them for the bubble
/// (CommandHostBubble) and the body for its ride and the deflate (SmallPlayerController.Bubble.cs). Metres, seconds, m/s.
/// </summary>
public static class BubbleLaws
{
    // ---- size: a bubble round the 10 cm body, and the boat ----

    /// <summary>A carry's and a lift's bubble when the control names no size (the 10 cm body fits with room to spare).</summary>
    public const float CarryRadiusDefaultM = 0.08f;
    /// <summary>The smallest and largest carry or lift: smaller would not hold the body, larger would not fit where the body goes.</summary>
    public const float CarryRadiusMinM = 0.07f;
    public const float CarryRadiusMaxM = 0.2f;
    /// <summary>The boat's deck when the control names no size, and its bounds (a raft for one or two small bodies).</summary>
    public const float BoatRadiusDefaultM = 0.12f;
    public const float BoatRadiusMinM = 0.08f;
    public const float BoatRadiusMaxM = 0.3f;

    // ---- durations when the control names none (the pack's duration_max_s bounds every cast: 60 s in storybook_wild v5) ----

    public const float CarryDurationDefaultS = 20f;
    public const float LiftDurationDefaultS = 30f;
    public const float BoatDurationDefaultS = 60f;

    // ---- motion ----

    /// <summary>How far a carry lifts the body above where the bubble formed, and how fast it rises there.</summary>
    public const float CarryHoverM = 0.05f;
    public const float CarryRiseMps = 0.2f;
    /// <summary>How far a lift rises above where it formed (three bands of the bar), and how fast.</summary>
    public const float LiftRiseM = 1.2f;
    public const float LiftRiseMps = 0.15f;
    /// <summary>A bubble drifts after the Gubble, which leads it like a balloon on a string, once the Gubble is farther than this (level distance).</summary>
    public const float LeashM = 0.2f;
    /// <summary>How fast a carry or a lift drifts after the Gubble, and a boat.</summary>
    public const float CarrySpeedMps = 0.35f;
    public const float BoatSpeedMps = 0.25f;
    /// <summary>The fastest the body follows the bubble that holds it (it keeps up with any drift above).</summary>
    public const float RideFollowMaxMps = 1.5f;

    // ---- the deflate: a bubble that pops in the air lets the body down slowly (decision 43) ----

    /// <summary>The deflate's descent, far under the safe landing (1.45 m/s), so a carry never ends in a fall.</summary>
    public const float DeflateDescentMps = 0.18f;
    /// <summary>How fast the player may steer their own drift down.</summary>
    public const float DeflateSteerMps = 0.08f;

    // ---- the boat ----

    /// <summary>The deck floats this far above the water's surface.</summary>
    public const float BoatDeckAboveWaterM = 0.01f;
    /// <summary>Shallow water, where a boat is cast: at least this deep and at most this deep (the body still wades there).</summary>
    public const float BoatCastMinDepthM = 0.01f;
    public const float BoatCastMaxDepthM = 0.08f;
    /// <summary>A boat moves only where the water under it is at least this deep.</summary>
    public const float BoatFloatMinDepthM = 0.01f;
    /// <summary>Feet this far under the deck (wading beside it) or above it still step aboard.</summary>
    public const float BoardBelowDeckM = 0.1f;
    public const float BoardAboveDeckM = 0.05f;

    // ---- consent (MECHANICS.md section 5's three guardrails, and the security notes' "cannot keep a player in a bubble") ----

    /// <summary>A Gubble's carry on the player commits at least this long after its ghost preview, so the player has seen it coming.</summary>
    public const double GhostLeadS = 1.0;
    /// <summary>The ghost shows this long; a cast naming an older preview is refused (preview it again).</summary>
    public const double GhostLifetimeS = 15.0;
    /// <summary>After the player pops a bubble (or leaves a boat by the dive key), the Gubble's carries on the player wait this long.</summary>
    public const double PopRespectS = 8.0;
    /// <summary>A carry on the player is centred on them: their feet within the bubble's radius and this of center_m.</summary>
    public const float EnvelopReachM = 0.05f;
    /// <summary>A bubble at a point never forms round the player: their feet at least its radius and this away.</summary>
    public const float StepInClearanceM = 0.02f;
    /// <summary>A body this far outside the bubble that holds it, for RiderLostAfterS, has left it (a teleport home, a wall): it pops.</summary>
    public const float RiderSlackM = 0.06f;
    public const double RiderLostAfterS = 0.3;

    // ---- caps (within the room's effect cap, CommandHost.MaxActiveEffects) ----

    /// <summary>At most this many bubbles at once, of every kind and caster.</summary>
    public const int MaxBubbles = 2;
    /// <summary>At most this many ghost previews wait at once; the oldest goes first.</summary>
    public const int MaxGhosts = 8;
}
