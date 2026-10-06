namespace EnFractal.Native;

/// <summary>
/// Engine-independent body dimensions in real-world metres. Avatar size never rescales the room or
/// gravity: one world unit is one metre, and the room stays at its captured size.
/// </summary>
public sealed record WorldScaleProfile(
    double HeightMeters,
    double RadiusMeters,
    double EyeHeightMeters,
    double InteractionReachMeters)
{
    public const int SchemaVersion = 1;
    public const double MetersPerWorldUnit = 1.0;

    /// <summary>The player: a 10 cm body in a real-size room (Run 1, P2).</summary>
    public static WorldScaleProfile SmallPlayer { get; } = new(0.10, 0.02, 0.087, 0.15);

    /// <summary>The companion keeps its own body, separate from the player's. Not final companion size.</summary>
    public static WorldScaleProfile Companion { get; } = new(0.24, 0.055, 0.205, 0.40);

    public bool IsValid =>
        double.IsFinite(HeightMeters) && HeightMeters is >= 0.05 and <= 3.0 &&
        double.IsFinite(RadiusMeters) && RadiusMeters > 0.0 && RadiusMeters * 2.0 <= HeightMeters &&
        double.IsFinite(EyeHeightMeters) && EyeHeightMeters > 0.0 && EyeHeightMeters < HeightMeters &&
        double.IsFinite(InteractionReachMeters) && InteractionReachMeters >= RadiusMeters && InteractionReachMeters <= 5.0;
}
