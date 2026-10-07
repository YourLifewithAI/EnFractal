using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace EnFractal.Native.Look;

/// <summary>
/// The light and grade a preset calls for at one hour on one day of the year. KeyHour is the hour on the time
/// keys' reference day that this hour maps to once the season has lengthened or shortened the day.
/// </summary>
public sealed record LookMoment(
    float Hour, int DayOfYear, string Season, Color KeyColor, float KeyEnergy, Color AmbientColor, float AmbientEnergy,
    float KeyElevationDeg, float KeyAzimuthDeg, float Daylight, float MoonWeight, float SeasonSaturation, float SeasonWarmth, Color SeasonTint,
    float KeyHour);

/// <summary>
/// Time of day and season for a preset. Pure functions, so the review harness can pin a moment and tests can
/// check the interpolation without a renderer.
///
/// Energy and colour come from the preset's time-of-day keys. Daylight is the key energy over the brightest
/// key. The key's direction follows from daylight, so light and direction can never disagree: the sun rises
/// and sets where daylight crosses x_look_sun.moon_daylight, sits at the preset's elevation and azimuth at the
/// default hour, and turns degrees_per_hour; the moon is a fixed direction; and the direction cross-fades
/// from sun to moon only while daylight is between moon_daylight and sun_daylight, when the light is dim.
///
/// The keys describe one reference day. When x_look_seasons.day_length_h is set, the season stretches or
/// shrinks that day around its noon (short winter days, long summer ones) and the night takes the rest, so the
/// real clock at 18:00 is dusk in winter and golden light in summer.
/// </summary>
public static class LookClock
{
    private const int SampleSteps = 24 * 60;
    private static readonly ConditionalWeakTable<StylePreset, Tuple<float, float>> LimitsCache = new();

    public static LookMoment At(StylePreset preset, float hour, int dayOfYear)
    {
        hour = Mathf.PosMod(hour, 24f);
        var sun = preset.Tuning.Sun;
        var keyHour = KeyHour(preset, hour, dayOfYear);
        var key = preset.TimeOfDayEnabled && preset.TimeKeys.Count > 0
            ? Sample(preset.TimeKeys, keyHour)
            : new TimeKey(hour, preset.KeyColor, preset.KeyEnergy, preset.AmbientColor, preset.AmbientEnergy);
        var maxEnergy = MaxEnergy(preset);
        var daylight = preset.TimeOfDayEnabled && maxEnergy > 0 ? Mathf.Clamp(key.KeyEnergy / maxEnergy, 0f, 1f) : 1f;
        float elevation = preset.KeyElevationDeg, azimuth = preset.KeyAzimuthDeg, moonWeight = 0f;
        if (preset.TimeOfDayEnabled && preset.KeyMode == "diorama" && preset.TimeKeys.Count > 1)
        {
            var (sunrise, sunset) = DayLimits(preset);
            var sunDirection = SunDirection(keyHour, sunrise, sunset, preset.DefaultHour, preset.KeyElevationDeg, preset.KeyAzimuthDeg, sun);
            moonWeight = 1f - Mathf.SmoothStep(sun.MoonDaylight, sun.SunDaylight, daylight);
            var moon = Direction(sun.MoonElevationDeg, preset.KeyAzimuthDeg + sun.MoonAzimuthOffsetDeg);
            (elevation, azimuth) = Angles(Slerp(Direction(sunDirection.Elevation, sunDirection.Azimuth), moon, moonWeight));
        }
        var (season, saturation, warmth, tint) = preset.SeasonsEnabled
            ? Grade(preset, dayOfYear)
            : ("none", 1f, 0f, new Color(0.5f, 0.5f, 0.5f));
        // The preset's key energy is the reference; time-of-day keys scale it relative to the brightest key.
        var keyEnergy = preset.TimeOfDayEnabled && maxEnergy > 0 ? preset.KeyEnergy * key.KeyEnergy / maxEnergy : preset.KeyEnergy;
        // The season colours the sunlight as well as the grade: paler in winter, golden in summer.
        var keyColor = preset.SeasonsEnabled ? key.KeyColor * SeasonLight(tint, preset.Tuning.Seasons.LightStrength) : key.KeyColor;
        return new LookMoment(hour, dayOfYear, season, keyColor, keyEnergy, key.AmbientColor, key.AmbientEnergy,
            elevation, azimuth, daylight, moonWeight, saturation, warmth, tint, keyHour);
    }

    /// <summary>
    /// The hour on the keys' reference day that a real hour maps to. The reference day runs from the keys'
    /// sunrise to their sunset around its noon; the season's day length (x_look_seasons.day_length_h, blended like
    /// the season grades) keeps that noon and maps its own sunrise and sunset onto the reference ones, linearly
    /// through the day and through the night. Continuous and increasing; the identity without day lengths.
    /// </summary>
    public static float KeyHour(StylePreset preset, float hour, int dayOfYear)
    {
        hour = Mathf.PosMod(hour, 24f);
        if (!preset.TimeOfDayEnabled || !preset.SeasonsEnabled || preset.TimeKeys.Count < 2 || preset.Tuning.Seasons.DayLengthH.Count != 4) return hour;
        var (rise, set) = DayLimits(preset);
        var reference = set - rise;
        var length = Mathf.Clamp(DayLength(preset, dayOfYear), 1f, 23f);
        var realRise = (rise + set) * 0.5f - length * 0.5f;
        var sinceRise = Mathf.PosMod(hour - realRise, 24f);
        if (sinceRise <= length) return Mathf.PosMod(rise + sinceRise * reference / length, 24f);
        return Mathf.PosMod(set + (sinceRise - length) * (24f - reference) / (24f - length), 24f);
    }

    /// <summary>Hours from sunrise to sunset on a day of the year: the season day lengths blended like the season grades.</summary>
    public static float DayLength(StylePreset preset, int dayOfYear)
    {
        var seasons = preset.Tuning.Seasons;
        if (seasons.DayLengthH.Count != 4)
        {
            var (rise, set) = DayLimits(preset);
            return set - rise;
        }
        var (from, to, t) = Blend(dayOfYear, preset.Hemisphere, seasons);
        var s = Mathf.SmoothStep(seasons.Hold, 1f - seasons.Hold, t);
        return Mathf.Lerp(seasons.DayLengthH[Array.IndexOf(SeasonNames, from)], seasons.DayLengthH[Array.IndexOf(SeasonNames, to)], s);
    }

    /// <summary>
    /// A deterministic clock for reviews and playtests: --look-clock=HH:MM and --look-date=YYYY-MM-DD after "--" on
    /// the command line, or the ENFRACTAL_LOOK_CLOCK and ENFRACTAL_LOOK_DATE environment variables. Either may be
    /// given alone; the other then follows the preset (the real clock or calendar). Returns null when neither is
    /// set, and an error message for a value it cannot read (the look then warns and follows the preset).
    /// </summary>
    public static (float? Hour, int? DayOfYear, string Error) ClockOverride(IReadOnlyList<string> userArgs, Func<string, string?> environment)
    {
        string? Find(string flag, string variable)
        {
            foreach (var argument in userArgs)
                if (argument.StartsWith("--" + flag + "=", StringComparison.Ordinal)) return argument[(flag.Length + 3)..];
            var value = environment(variable);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        var clock = Find("look-clock", "ENFRACTAL_LOOK_CLOCK");
        var date = Find("look-date", "ENFRACTAL_LOOK_DATE");
        float? hour = null;
        int? day = null;
        var errors = new List<string>();
        if (clock != null)
        {
            if (TimeSpan.TryParseExact(clock, new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var time) && time.TotalHours < 24)
                hour = (float)time.TotalHours;
            else errors.Add($"look clock '{clock}' is not HH:MM");
        }
        if (date != null)
        {
            if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) day = parsed.DayOfYear;
            else errors.Add($"look date '{date}' is not YYYY-MM-DD");
        }
        return (hour, day, string.Join("; ", errors));
    }

    private static float MaxEnergy(StylePreset preset)
    {
        var max = 0f;
        foreach (var k in preset.TimeKeys) max = Mathf.Max(max, k.KeyEnergy);
        return max;
    }

    /// <summary>Interpolate time-of-day keys, wrapping from the last key of the evening to the first of the morning.</summary>
    public static TimeKey Sample(IReadOnlyList<TimeKey> keys, float hour)
    {
        hour = Mathf.PosMod(hour, 24f);
        if (keys.Count == 1) return keys[0] with { Hour = hour };
        TimeKey a, b;
        var h = hour;
        var first = keys[0];
        var last = keys[^1];
        if (hour >= last.Hour || hour < first.Hour)
        {
            (a, b) = (last, first with { Hour = first.Hour + 24f });
            if (h < a.Hour) h += 24f;
        }
        else
        {
            var i = 0;
            while (!(hour >= keys[i].Hour && hour < keys[i + 1].Hour)) i++;
            (a, b) = (keys[i], keys[i + 1]);
        }
        var t = (h - a.Hour) / (b.Hour - a.Hour);
        t = t * t * (3f - 2f * t);
        return new TimeKey(hour, a.KeyColor.Lerp(b.KeyColor, t), Mathf.Lerp(a.KeyEnergy, b.KeyEnergy, t),
            a.AmbientColor.Lerp(b.AmbientColor, t), Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, t));
    }

    /// <summary>
    /// Sunrise and sunset: where daylight from the time keys rises through and falls back through
    /// moon_daylight, found by sampling each minute. Without a clear day (keys never cross), 06:00 to 18:00.
    /// </summary>
    public static (float Sunrise, float Sunset) DayLimits(StylePreset preset)
    {
        var limits = LimitsCache.GetValue(preset, p => { var (rise, set) = ComputeDayLimits(p); return Tuple.Create(rise, set); });
        return (limits.Item1, limits.Item2);
    }

    private static (float Sunrise, float Sunset) ComputeDayLimits(StylePreset preset)
    {
        var max = MaxEnergy(preset);
        var threshold = preset.Tuning.Sun.MoonDaylight;
        if (max <= 0f) return (6f, 18f);
        float? rise = null, set = null;
        var previous = Sample(preset.TimeKeys, 0f).KeyEnergy / max;
        for (var step = 1; step <= SampleSteps; step++)
        {
            var hour = step * 24f / SampleSteps;
            var current = Sample(preset.TimeKeys, hour).KeyEnergy / max;
            if (previous < threshold && current >= threshold && rise == null) rise = hour;
            if (previous >= threshold && current < threshold && rise != null) set = hour;
            previous = current;
        }
        return rise is { } r && set is { } s && s > r ? (r, s) : (6f, 18f);
    }

    /// <summary>
    /// The sun on its day arc: at the default hour exactly the preset's elevation and azimuth, rising from the
    /// horizon at sunrise and setting at sunset, never above max_elevation_deg (or the preset's elevation if
    /// higher), turning degrees_per_hour. Below the horizon it stays at horizon_elevation_deg.
    /// </summary>
    public static (float Elevation, float Azimuth) SunDirection(float hour, float sunrise, float sunset, float defaultHour, float elevation, float azimuth, SunTuning sun)
    {
        var length = sunset - sunrise;
        var arc = Mathf.Sin(Mathf.Pi * Mathf.Clamp((hour - sunrise) / length, 0f, 1f));
        var reference = Mathf.Sin(Mathf.Pi * Mathf.Clamp((defaultHour - sunrise) / length, 0f, 1f));
        var raised = reference > 0.05f ? elevation * arc / reference : elevation * arc;
        var ceiling = Mathf.Max(sun.MaxElevationDeg, elevation);
        var hourOffset = hour - defaultHour;
        if (hourOffset > 12f) hourOffset -= 24f;
        if (hourOffset < -12f) hourOffset += 24f;
        return (Mathf.Clamp(raised, sun.HorizonElevationDeg, ceiling), WrapDegrees(azimuth + hourOffset * sun.DegreesPerHour));
    }

    /// <summary>Unit vector the light comes from, for an elevation and azimuth in degrees (Godot: -Z forward, +Y up).</summary>
    public static Vector3 Direction(float elevationDeg, float azimuthDeg)
    {
        var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(-elevationDeg), Mathf.DegToRad(azimuthDeg), 0f));
        return basis.Z.Normalized();
    }

    private static (float Elevation, float Azimuth) Angles(Vector3 from)
    {
        var elevation = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(from.Y, -1f, 1f)));
        var azimuth = Mathf.RadToDeg(Mathf.Atan2(from.X, from.Z));
        return (elevation, azimuth);
    }

    private static Vector3 Slerp(Vector3 a, Vector3 b, float t)
    {
        if (t <= 0f) return a;
        if (t >= 1f) return b;
        var dot = Mathf.Clamp(a.Dot(b), -1f, 1f);
        if (dot > 0.9995f) return a.Lerp(b, t).Normalized();
        // Nearly opposite directions: pass over the top of the sky rather than through the floor.
        if (dot < -0.9995f) return (a + Vector3.Up * 0.01f).Normalized().Slerp(b, t);
        return a.Slerp(b, t);
    }

    private static float WrapDegrees(float degrees) => Mathf.PosMod(degrees + 180f, 360f) - 180f;

    /// <summary>The season whose mid-point is nearest the day, with the default season centres.</summary>
    public static string SeasonAt(int dayOfYear, string hemisphere) => SeasonAt(dayOfYear, hemisphere, LookTuning.Default.Seasons);

    public static string SeasonAt(int dayOfYear, string hemisphere, SeasonTuning seasons)
    {
        var (a, b, t) = Blend(dayOfYear, hemisphere, seasons);
        return t < 0.5f ? a : b;
    }

    public static (string From, string To, float T) Blend(int dayOfYear, string hemisphere) => Blend(dayOfYear, hemisphere, LookTuning.Default.Seasons);

    private static readonly string[] SeasonNames = { "winter", "spring", "summer", "autumn" };

    /// <summary>The two seasons a day sits between and how far it is from the first towards the second.</summary>
    public static (string From, string To, float T) Blend(int dayOfYear, string hemisphere, SeasonTuning seasons)
    {
        var day = Mathf.PosMod(dayOfYear - (hemisphere == "south" ? 182 : 0) - 1, 365) + 1;
        var centres = seasons.CentreDays;
        for (var i = 0; i < centres.Count; i++)
        {
            var fromDay = centres[i];
            var toDay = centres[(i + 1) % centres.Count];
            var end = toDay > fromDay ? toDay : toDay + 365;
            var d = day >= fromDay ? day : day + 365;
            if (d >= fromDay && d < end) return (SeasonNames[i], SeasonNames[(i + 1) % 4], (float)(d - fromDay) / (end - fromDay));
        }
        return ("winter", "winter", 0f);
    }

    private static (string Season, float Saturation, float Warmth, Color Tint) Grade(StylePreset preset, int dayOfYear)
    {
        var seasons = preset.Tuning.Seasons;
        var (from, to, t) = Blend(dayOfYear, preset.Hemisphere, seasons);
        // Hold each season's grade around its middle and cross-fade in between.
        var s = Mathf.SmoothStep(seasons.Hold, 1f - seasons.Hold, t);
        var a = preset.SeasonGrades[from];
        var b = preset.SeasonGrades[to];
        return (t < 0.5f ? from : to, Mathf.Lerp(a.Saturation, b.Saturation, s), Mathf.Lerp(a.Warmth, b.Warmth, s), a.Tint.Lerp(b.Tint, s));
    }

    /// <summary>How a season's tint colours the key light: its hue at the given strength, lightness kept.</summary>
    public static Color SeasonLight(Color seasonTint, float strength)
    {
        var luma = Mathf.Max(ColorGrade.Luma(seasonTint), 1e-3f);
        var normalized = new Color(seasonTint.R / luma, seasonTint.G / luma, seasonTint.B / luma);
        return new Color(1f, 1f, 1f).Lerp(normalized, strength);
    }

    public static int TodayDayOfYear() => DateTime.Now.DayOfYear;

    public static float NowHour()
    {
        var now = DateTime.Now;
        return now.Hour + now.Minute / 60f;
    }
}
