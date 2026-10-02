namespace EnFractal.Native;

/// <summary>
/// Engine-independent dimensions in real-world meters. Avatar size never rescales
/// geography or gravity. The controller adopts this contract in a separate step.
/// </summary>
public sealed record WorldScaleProfile(
    double HeightMeters,
    double RadiusMeters,
    double EyeHeightMeters,
    double InteractionReachMeters)
{
    public const int SchemaVersion = 1;
    public const double MetersPerWorldUnit = 1.0;
    public static WorldScaleProfile SmallPlayer { get; } = new(0.30, 0.06, 0.26, 0.45);

    public bool IsValid =>
        double.IsFinite(HeightMeters) && HeightMeters is >= 0.05 and <= 3.0 &&
        double.IsFinite(RadiusMeters) && RadiusMeters > 0.0 && RadiusMeters * 2.0 <= HeightMeters &&
        double.IsFinite(EyeHeightMeters) && EyeHeightMeters > 0.0 && EyeHeightMeters < HeightMeters &&
        double.IsFinite(InteractionReachMeters) && InteractionReachMeters >= RadiusMeters && InteractionReachMeters <= 5.0;
}
