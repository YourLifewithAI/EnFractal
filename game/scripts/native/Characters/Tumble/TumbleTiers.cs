using EnFractal.Native.Kernel;

namespace EnFractal.Native.Characters;

/// <summary>What a landing does to a drawn body, by what it would cost in Challenge.</summary>
public enum TumbleTier
{
    /// <summary>It would cost nothing: the contact layer's landing squash (K4).</summary>
    None = 0,
    /// <summary>1 to 24 health: the contact layer's stumble (K4); control is kept.</summary>
    Stumble = 1,
    /// <summary>25 to 99 health: a tumble of about 1.2 s; control is held, and moving starts the get-up early.</summary>
    Tumble = 2,
    /// <summary>100 health, a knockout in Challenge: a longer, dazed tumble of about 2 s; control is held throughout.</summary>
    Knockout = 3,
}

/// <summary>
/// The tumble's tiers (docs/runs/RUN-2-CHARACTERS.md, "What counts as a big fall"): the tumble asks
/// <see cref="PlayerVitals.FallDamage"/> what the landing would cost in Challenge, in every mode, Creative included, so a tumble
/// always means "this would have hurt", and it follows the world's gravity, a glide and deep water exactly as damage does. There
/// is never a second set of numbers: the two thresholds below are health, not speeds or heights, and retuning fall damage moves
/// the tumble with it.
/// </summary>
public static class TumbleTiers
{
    /// <summary>A landing that would cost at least this much health tumbles.</summary>
    public const float TumbleHealth = 25f;
    /// <summary>A landing that would cost this much (all of it: a knockout in Challenge) tumbles longer, dazed.</summary>
    public const float KnockoutHealth = PlayerVitals.MaximumHealth;

    /// <summary>A tumble's length: the flop on the ground and the get-up.</summary>
    public const float TumbleSeconds = 1.2f;
    /// <summary>A knockout tumble's length, which the host's recovery at the spawn waits for (KnockoutRecoveryDelayS).</summary>
    public const float KnockoutSeconds = 2.0f;
    /// <summary>The get-up: the bones blend from where they settled back to the standing pose.</summary>
    public const float GetUpSeconds = 0.4f;
    /// <summary>Moving starts a tumble's get-up early by at most this much (never a knockout's).</summary>
    public const float EarlyGetUpSeconds = 0.5f;

    /// <summary>What the landing would cost in Challenge (health), from the same function fall damage uses.</summary>
    public static float Cost(float landingSpeedMps, bool deepWater) => PlayerVitals.FallDamage(landingSpeedMps, deepWater);

    /// <summary>The tier of a landing at <paramref name="landingSpeedMps"/> (downward, m/s); deep water never tumbles.</summary>
    public static TumbleTier Classify(float landingSpeedMps, bool deepWater) => Classify(Cost(landingSpeedMps, deepWater));

    /// <summary>The tier of a landing that would cost <paramref name="health"/>.</summary>
    public static TumbleTier Classify(float health) =>
        !(health > 0) ? TumbleTier.None :
        health >= KnockoutHealth ? TumbleTier.Knockout :
        health >= TumbleHealth ? TumbleTier.Tumble : TumbleTier.Stumble;

    /// <summary>How long a tier tumbles (0 below the tumble tier: the contact layer's business).</summary>
    public static float Seconds(TumbleTier tier) => tier switch
    {
        TumbleTier.Knockout => KnockoutSeconds,
        TumbleTier.Tumble => TumbleSeconds,
        _ => 0,
    };
}
