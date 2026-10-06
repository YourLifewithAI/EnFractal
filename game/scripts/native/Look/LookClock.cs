using Godot;
using System;
using System.Collections.Generic;

namespace EnFractal.Native.Look;

/// <summary>The light and grade a preset calls for at one hour on one day of the year.</summary>
public sealed record LookMoment(
    float Hour, int DayOfYear, string Season, Color KeyColor, float KeyEnergy, Color AmbientColor, float AmbientEnergy,
    float KeyElevationDeg, float KeyAzimuthDeg, float Daylight, float SeasonSaturation, float SeasonWarmth, Color SeasonTint);

/// <summary>
/// Time of day and season for a preset. Pure functions, so the review harness can pin a moment and tests
/// can check the interpolation without a renderer.
/// </summary>
public static class LookClock
{
    // Mid-season days in the northern hemisphere: 15 January, 15 April, 15 July, 15 October.
    private static readonly (string Season, int Day)[] NorthCentres = { ("winter", 15), ("spring", 105), ("summer", 196), ("autumn", 288) };

    /// <summary>Degrees the key light turns per hour when it follows the clock.</summary>
    public const float KeyDegreesPerHour = 15f;
    /// <summary>The highest the key climbs at noon (or the preset's own elevation, if higher); an overhead key flattens a diorama.</summary>
    public const float MaxSunElevationDeg = 60f;

    public static LookMoment At(StylePreset preset, float hour, int dayOfYear)
    {
        hour = Mathf.PosMod(hour, 24f);
        var key = preset.TimeOfDayEnabled && preset.TimeKeys.Count > 0
            ? Sample(preset.TimeKeys, hour)
            : new TimeKey(hour, preset.KeyColor, preset.KeyEnergy, preset.AmbientColor, preset.AmbientEnergy);
        var maxEnergy = 0f;
        foreach (var k in preset.TimeKeys) maxEnergy = Mathf.Max(maxEnergy, k.KeyEnergy);
        var daylight = preset.TimeOfDayEnabled && maxEnergy > 0 ? Mathf.Clamp(key.KeyEnergy / maxEnergy, 0f, 1f) : 1f;
        var (elevation, azimuth) = preset.TimeOfDayEnabled && preset.KeyMode == "diorama"
            ? KeyDirection(hour, preset.DefaultHour, preset.KeyElevationDeg, preset.KeyAzimuthDeg)
            : (preset.KeyElevationDeg, preset.KeyAzimuthDeg);
        var (season, saturation, warmth, tint) = preset.SeasonsEnabled
            ? Grade(preset, dayOfYear)
            : ("none", 1f, 0f, new Color(0.5f, 0.5f, 0.5f));
        // The preset's key energy is the reference; time-of-day keys scale it relative to the brightest key.
        var keyEnergy = preset.TimeOfDayEnabled && maxEnergy > 0 ? preset.KeyEnergy * key.KeyEnergy / maxEnergy : preset.KeyEnergy;
        // The season colours the sunlight as well as the grade: paler in winter, golden in summer.
        var keyColor = preset.SeasonsEnabled ? key.KeyColor * SeasonLight(tint) : key.KeyColor;
        return new LookMoment(hour, dayOfYear, season, keyColor, keyEnergy, key.AmbientColor, key.AmbientEnergy,
            elevation, azimuth, daylight, saturation, warmth, tint);
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
    /// Where the key light sits at an hour: at the preset's default hour it is exactly the preset's elevation
    /// and azimuth; it turns 15 degrees an hour and rises and sets on a day arc from 06:00 to 18:00. At night
    /// the key becomes a moon opposite the day key, at a fixed 35 degrees.
    /// </summary>
    public static (float Elevation, float Azimuth) KeyDirection(float hour, float defaultHour, float elevation, float azimuth)
    {
        var day = Mathf.Sin(Mathf.Pi * (hour - 6f) / 12f);
        var reference = Mathf.Sin(Mathf.Pi * (defaultHour - 6f) / 12f);
        if (day <= 0.05f) return (35f, WrapDegrees(azimuth + 180f + (hour - defaultHour) * KeyDegreesPerHour * 0.5f));
        var arc = reference > 0.05f ? elevation * day / reference : elevation;
        var ceiling = Mathf.Max(MaxSunElevationDeg, elevation);
        return (Mathf.Clamp(arc, 8f, ceiling), WrapDegrees(azimuth + (hour - defaultHour) * KeyDegreesPerHour));
    }

    private static float WrapDegrees(float degrees) => Mathf.PosMod(degrees + 180f, 360f) - 180f;

    /// <summary>The season whose mid-point is nearest the day.</summary>
    public static string SeasonAt(int dayOfYear, string hemisphere)
    {
        var (a, b, t) = Blend(dayOfYear, hemisphere);
        return t < 0.5f ? a : b;
    }

    /// <summary>The two seasons a day sits between and how far it is from the first towards the second.</summary>
    public static (string From, string To, float T) Blend(int dayOfYear, string hemisphere)
    {
        var day = Mathf.PosMod(dayOfYear - (hemisphere == "south" ? 182 : 0) - 1, 365) + 1;
        for (var i = 0; i < NorthCentres.Length; i++)
        {
            var (fromSeason, fromDay) = NorthCentres[i];
            var (toSeason, toDay) = NorthCentres[(i + 1) % NorthCentres.Length];
            var end = toDay > fromDay ? toDay : toDay + 365;
            var d = day >= fromDay ? day : day + 365;
            if (d >= fromDay && d < end) return (fromSeason, toSeason, (float)(d - fromDay) / (end - fromDay));
        }
        return ("winter", "winter", 0f);
    }

    private static (string Season, float Saturation, float Warmth, Color Tint) Grade(StylePreset preset, int dayOfYear)
    {
        var (from, to, t) = Blend(dayOfYear, preset.Hemisphere);
        // Hold each season's grade around its middle and cross-fade in between.
        var s = Mathf.SmoothStep(0.25f, 0.75f, t);
        var a = preset.SeasonGrades[from];
        var b = preset.SeasonGrades[to];
        return (t < 0.5f ? from : to, Mathf.Lerp(a.Saturation, b.Saturation, s), Mathf.Lerp(a.Warmth, b.Warmth, s), a.Tint.Lerp(b.Tint, s));
    }

    /// <summary>How much a season's tint colours the key light: its hue at a quarter strength, lightness kept.</summary>
    public const float SeasonLightStrength = 0.25f;

    public static Color SeasonLight(Color seasonTint)
    {
        var luma = Mathf.Max(ColorGrade.Luma(seasonTint), 1e-3f);
        var normalized = new Color(seasonTint.R / luma, seasonTint.G / luma, seasonTint.B / luma);
        return new Color(1f, 1f, 1f).Lerp(normalized, SeasonLightStrength);
    }

    public static int TodayDayOfYear() => DateTime.Now.DayOfYear;

    public static float NowHour()
    {
        var now = DateTime.Now;
        return now.Hour + now.Minute / 60f;
    }
}
