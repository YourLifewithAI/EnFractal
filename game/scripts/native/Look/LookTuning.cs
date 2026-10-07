using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace EnFractal.Native.Look;

/// <summary>Surface patterns the painterly shader knows; values match the constants in painterly_material.gdshader.</summary>
public enum PaintPattern { None = 0, Wood = 1, Fabric = 2, Plaster = 3, Paper = 4, Cardboard = 5, Brushed = 6, Speckle = 7, Smooth = 8, Stone = 9 }

/// <summary>
/// How a material role is painted beyond the preset's per-role treatment: the pattern, the size and direction
/// of brush marks, and how the role takes light. Sizes are metres in the real-scale room.
/// </summary>
public sealed record RoleLook(
    PaintPattern Pattern, float StrokeScaleM, float StrokeStretch, Vector3 StrokeAxis, float PatternScaleM,
    float Variation, float Wrap, float TerminatorWarmth, float Sheen, float Specular, float TextureStrength, float TextureScaleM);

/// <summary>Painterly shader multipliers shared by every role.</summary>
public sealed record PaintTuning(
    float ToneWash, float ToneStrokes, float ToneBristle, float ToneGain, float SofteningCalm, float TemperatureVariation,
    float PastelMix, float PastelChroma, float PastelScale, float PastelLift, float WearBrightness, float WearLift,
    float CavityDarkening, float StrokeNormalGain, float MarkFadeStart, float MarkFadeEnd, float BevelFraction, float BevelMaxM,
    float AccentMix, float AccentDarken, float ShadowFillBase, float ShadowFillPerSoftness);

public sealed record ShadowTuning(
    int KeySplits, float KeyAngularBaseDeg, float KeyAngularPerSoftnessDeg, float BlurBase, float BlurPerSoftness,
    float KeyMinDistanceM, float KeyDistancePerDiagonal, float Bias, float NormalBias, float LampSizeBaseM, float LampSizePerSoftnessM);

public sealed record SsaoTuning(float Power, float Detail, float Horizon, float Sharpness, float LightAffect, float AoChannelAffect);

public sealed record GlowTuning(float Strength, string BlendMode, float HdrThreshold, IReadOnlyList<float> Levels);

public sealed record GiTuning(int Subdiv, float MarginM, float Bias, float NormalBias, float TwoBouncesAbove);

/// <summary>Room lights from the manifest's light hints: falloff, reach, the window spot's cone, and how much brighter lamps glow at night.</summary>
public sealed record LampTuning(float Attenuation, float NightBoost, float RangePerDiagonal, float WindowSpotAngleDeg);

/// <summary>
/// The key light's path. The sun rises and sets where daylight (key energy over the brightest key) crosses
/// MoonDaylight; the key is the sun at or above SunDaylight, a fixed moon at or below MoonDaylight, and its
/// direction cross-fades only in between, while the light is dim.
/// </summary>
public sealed record SunTuning(
    float DegreesPerHour, float MaxElevationDeg, float HorizonElevationDeg, float MoonElevationDeg, float MoonAzimuthOffsetDeg,
    float SunDaylight, float MoonDaylight);

/// <summary>
/// Season centres (winter, spring, summer, autumn), how long each holds its grade, how strongly the season colours
/// the key, the day used when the preset does not follow the calendar, and each season's hours of daylight (empty:
/// every day is the time keys' own day).
/// </summary>
public sealed record SeasonTuning(IReadOnlyList<int> CentreDays, float Hold, float LightStrength, int FixedDayOfYear, IReadOnlyList<float> DayLengthH);

public sealed record GradeTuning(
    float ShadowTone, float HighlightTone, float SeasonTint, Vector3 WarmthRgb, float NightDesaturate, Vector3 NightTintRgb,
    float NightDeepen, int LutSize, float SeasonTintShadowFade, float NightFullBelow, float NightNoneAbove);

public sealed record DofTuning(
    float TiltPitchGain, float TiltBandNarrowing, float FarTransitionBaseM, float FarTransitionPerM, float FarBlurReference,
    float NearTransitionBase, float NearTransitionPerBlur, float AmountBase, float AmountPerBlur,
    float EyeFocusBodyHeights, float BodyFocusHeightFraction, float EyeCrispBodyHeights,
    float TiltTransitionShortening, float TiltAmountGain, float CompanionFollowM);

public sealed record PostTuning(float VignetteStart, float VignetteEnd, float GrainFine, float GrainSoft, float GrainSoftPx);

/// <summary>
/// Every look-defining number that is not a schema field, read from the preset's x_look_* extension blocks so a
/// pinned preset version reproduces its look (with the shader files it names unchanged). Blocks and fields are
/// optional for presets written before them, defaulting to the values the draft look was tuned with, and
/// Defaulted names every number a preset left to those defaults; the shipped preset states them all. Unknown
/// fields are refused so a typo cannot be silently ignored.
///
/// Role marks: a preset with an x_look_role_marks block paints every role it lists with those marks and every
/// other role with its "default" entry; only a preset without the block falls back to the built-in role table.
/// </summary>
public sealed record LookTuning(
    IReadOnlyDictionary<string, RoleLook> RoleMarks, RoleLook DefaultMarks, PaintTuning Paint, ShadowTuning Shadows,
    SsaoTuning Ssao, GlowTuning Glow, GiTuning Gi, LampTuning Lamps, SunTuning Sun, SeasonTuning Seasons,
    GradeTuning Grade, DofTuning Dof, PostTuning Post)
{
    /// <summary>"block.field" for every look number the preset left to the code's defaults (empty for the shipped preset).</summary>
    public IReadOnlyList<string> Defaulted { get; init; } = Array.Empty<string>();

    public RoleLook MarksFor(string role) => RoleMarks.TryGetValue(role, out var marks) ? marks : DefaultMarks;

    /// <summary>The x_look_* blocks LookTuning reads (StylePreset reads the others).</summary>
    public static readonly IReadOnlyList<string> Blocks = new[]
    {
        "x_look_role_marks", "x_look_paint", "x_look_shadows", "x_look_ssao", "x_look_glow", "x_look_gi", "x_look_lamps",
        "x_look_sun", "x_look_seasons", "x_look_grade", "x_look_dof", "x_look_post",
    };

    public static readonly RoleLook DefaultRoleMarks = new(PaintPattern.None, 0.05f, 2f, Vector3.Up, 0.1f, 0.4f, 0.25f, 0.2f, 0f, 0.35f, 0.35f, 0.6f);

    private static RoleLook M(PaintPattern p, float stroke, float stretch, Vector3 axis, float pattern, float variation, float wrap, float warmth, float sheen, float specular) =>
        new(p, stroke, stretch, axis, pattern, variation, wrap, warmth, sheen, specular, 0.35f, 0.6f);

    public static readonly IReadOnlyDictionary<string, RoleLook> DefaultRoles = new Dictionary<string, RoleLook>
    {
        ["painted_wall"] = M(PaintPattern.Plaster, 0.16f, 1.8f, Vector3.Up, 0.3f, 0.55f, 0.30f, 0.25f, 0f, 0.20f),
        ["plaster"] = M(PaintPattern.Plaster, 0.18f, 1.8f, Vector3.Right, 0.4f, 0.55f, 0.30f, 0.25f, 0f, 0.20f),
        ["wallpaper"] = M(PaintPattern.Plaster, 0.06f, 5f, Vector3.Up, 0.20f, 0.40f, 0.30f, 0.20f, 0f, 0.20f),
        ["wood"] = M(PaintPattern.Wood, 0.04f, 6f, Vector3.Right, 0.11f, 0.80f, 0.20f, 0.30f, 0f, 0.35f),
        ["wood_painted"] = M(PaintPattern.Wood, 0.05f, 5f, Vector3.Right, 0.11f, 0.40f, 0.25f, 0.25f, 0f, 0.30f),
        ["fabric"] = M(PaintPattern.Fabric, 0.05f, 2f, Vector3.Right, 0.004f, 0.60f, 0.45f, 0.30f, 0.35f, 0.05f),
        ["carpet"] = M(PaintPattern.Fabric, 0.06f, 1.5f, Vector3.Right, 0.006f, 0.55f, 0.50f, 0.30f, 0.45f, 0.03f),
        ["leather"] = M(PaintPattern.Smooth, 0.03f, 2f, Vector3.Right, 0.05f, 0.45f, 0.25f, 0.25f, 0.15f, 0.40f),
        ["metal"] = M(PaintPattern.Brushed, 0.05f, 8f, Vector3.Right, 0.03f, 0.35f, 0.10f, 0.10f, 0f, 0.90f),
        ["metal_painted"] = M(PaintPattern.Smooth, 0.05f, 3f, Vector3.Right, 0.05f, 0.40f, 0.20f, 0.20f, 0f, 0.50f),
        ["glass"] = M(PaintPattern.Smooth, 0.08f, 2f, Vector3.Up, 0.10f, 0.10f, 0.10f, 0.05f, 0f, 0.90f),
        ["mirror"] = M(PaintPattern.Smooth, 0.08f, 2f, Vector3.Up, 0.10f, 0.05f, 0.05f, 0.00f, 0f, 1.00f),
        ["ceramic"] = M(PaintPattern.Smooth, 0.04f, 2f, Vector3.Up, 0.05f, 0.30f, 0.20f, 0.20f, 0f, 0.60f),
        ["stone"] = M(PaintPattern.Stone, 0.06f, 2f, Vector3.Right, 0.02f, 0.70f, 0.20f, 0.25f, 0f, 0.20f),
        ["concrete"] = M(PaintPattern.Stone, 0.08f, 2f, Vector3.Right, 0.03f, 0.60f, 0.20f, 0.20f, 0f, 0.15f),
        ["brick"] = M(PaintPattern.Stone, 0.05f, 3f, Vector3.Right, 0.02f, 0.70f, 0.20f, 0.25f, 0f, 0.15f),
        ["paper"] = M(PaintPattern.Paper, 0.03f, 3f, Vector3.Right, 0.0012f, 0.35f, 0.35f, 0.25f, 0.05f, 0.10f),
        ["cardboard"] = M(PaintPattern.Cardboard, 0.04f, 3f, Vector3.Right, 0.004f, 0.50f, 0.30f, 0.30f, 0f, 0.10f),
        ["plastic"] = M(PaintPattern.Smooth, 0.05f, 2f, Vector3.Right, 0.05f, 0.30f, 0.25f, 0.20f, 0.05f, 0.60f),
        ["rubber"] = M(PaintPattern.Speckle, 0.03f, 2f, Vector3.Right, 0.0025f, 0.40f, 0.30f, 0.20f, 0f, 0.15f),
    };

    public static readonly LookTuning Default = new(
        DefaultRoles, DefaultRoleMarks,
        new PaintTuning(0.9f, 0.6f, 0.35f, 0.42f, 0.7f, 0.03f, 0.45f, 0.82f, 0.94f, 0.03f, 1.32f, 0.035f, 0.35f, 0.2f,
            0.15f, 0.45f, 0.08f, 0.025f, 0.55f, 0.12f, 0.25f, 0.3f),
        new ShadowTuning(2, 0.5f, 3.5f, 1f, 1f, 6f, 1.6f, 0.03f, 1.0f, 0.05f, 0.25f),
        new SsaoTuning(1.4f, 0.6f, 0.06f, 0.98f, 0.15f, 0.5f),
        new GlowTuning(1.0f, "softlight", 0.9f, new[] { 0f, 0f, 1f, 1f, 1f, 0f, 0f }),
        new GiTuning(128, 0.3f, 1.5f, 0f, 0.2f),
        new LampTuning(1.2f, 1.5f, 1f, 60f),
        new SunTuning(15f, 60f, 6f, 35f, 180f, 0.3f, 0.12f),
        new SeasonTuning(new[] { 15, 105, 196, 288 }, 0.25f, 0.25f, 196, Array.Empty<float>()),
        new GradeTuning(0.8f, 0.35f, 0.24f, new Vector3(0.08f, 0.015f, -0.10f), 0.3f, new Vector3(-0.14f, -0.06f, 0.10f), 0.25f, 33, 0f, 0f, 1f),
        new DofTuning(1.5f, 0.4f, 0.25f, 0.35f, 1.3f, 0.9f, 0.5f, 0.02f, 0.10f, 3f, 0.5f, 1.5f, 0f, 1f, 0f),
        new PostTuning(0.45f, 1.05f, 1.2f, 0.8f, 3f));

    private static readonly Dictionary<string, PaintPattern> PatternNames = Enum.GetValues<PaintPattern>().ToDictionary(p => p.ToString().ToLowerInvariant());

    public static LookTuning Parse(JsonElement extensions)
    {
        var d = Default;
        var defaulted = new List<string>();
        JsonElement Block(string name) => extensions.ValueKind == JsonValueKind.Object && extensions.TryGetProperty(name, out var b) ? b : default;

        var roles = new Dictionary<string, RoleLook>(d.RoleMarks);
        var defaultMarks = d.DefaultMarks;
        var marks = Block("x_look_role_marks");
        if (marks.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined)) throw new InvalidOperationException("x_look_role_marks must be an object");
        if (marks.ValueKind == JsonValueKind.Object)
        {
            // A preset that paints roles itself owns all of them: unlisted roles take its default, never the built-in table.
            if (!marks.TryGetProperty("default", out var dm)) throw new InvalidOperationException("x_look_role_marks needs a \"default\" entry for the roles it does not list");
            defaultMarks = Role(dm, d.DefaultMarks, "x_look_role_marks.default", defaulted);
            roles.Clear();
            foreach (var role in marks.EnumerateObject().Where(r => r.Name != "default"))
                roles[role.Name] = Role(role.Value, d.RoleMarks.TryGetValue(role.Name, out var known) ? known : defaultMarks, "x_look_role_marks." + role.Name, defaulted);
        }
        else defaulted.Add("x_look_role_marks");

        var p = new Fields(Block("x_look_paint"), "x_look_paint", defaulted);
        var paint = new PaintTuning(
            p.F("tone_wash", d.Paint.ToneWash), p.F("tone_strokes", d.Paint.ToneStrokes), p.F("tone_bristle", d.Paint.ToneBristle),
            p.F("tone_gain", d.Paint.ToneGain), p.F("softening_calm", d.Paint.SofteningCalm), p.F("temperature_variation", d.Paint.TemperatureVariation),
            p.F("pastel_mix", d.Paint.PastelMix), p.F("pastel_chroma", d.Paint.PastelChroma), p.F("pastel_scale", d.Paint.PastelScale),
            p.F("pastel_lift", d.Paint.PastelLift), p.F("wear_brightness", d.Paint.WearBrightness), p.F("wear_lift", d.Paint.WearLift),
            p.F("cavity_darkening", d.Paint.CavityDarkening), p.F("stroke_normal_gain", d.Paint.StrokeNormalGain),
            p.F("mark_fade_start", d.Paint.MarkFadeStart), p.F("mark_fade_end", d.Paint.MarkFadeEnd),
            p.F("bevel_fraction", d.Paint.BevelFraction), p.F("bevel_max_m", d.Paint.BevelMaxM),
            p.F("accent_mix", d.Paint.AccentMix), p.F("accent_darken", d.Paint.AccentDarken),
            p.F("shadow_fill_base", d.Paint.ShadowFillBase), p.F("shadow_fill_per_softness", d.Paint.ShadowFillPerSoftness));
        p.Done();
        if (!(paint.MarkFadeStart > 0f && paint.MarkFadeEnd > paint.MarkFadeStart && paint.MarkFadeEnd <= 0.5f))
            throw new InvalidOperationException("x_look_paint: mark_fade_start and mark_fade_end must satisfy 0 < start < end <= 0.5, so marks are gone before they alias");

        var s = new Fields(Block("x_look_shadows"), "x_look_shadows", defaulted);
        var shadows = new ShadowTuning(
            s.I("key_splits", d.Shadows.KeySplits), s.F("key_angular_base_deg", d.Shadows.KeyAngularBaseDeg), s.F("key_angular_per_softness_deg", d.Shadows.KeyAngularPerSoftnessDeg),
            s.F("blur_base", d.Shadows.BlurBase), s.F("blur_per_softness", d.Shadows.BlurPerSoftness), s.F("key_min_distance_m", d.Shadows.KeyMinDistanceM),
            s.F("key_distance_per_diagonal", d.Shadows.KeyDistancePerDiagonal), s.F("bias", d.Shadows.Bias), s.F("normal_bias", d.Shadows.NormalBias),
            s.F("lamp_size_base_m", d.Shadows.LampSizeBaseM), s.F("lamp_size_per_softness_m", d.Shadows.LampSizePerSoftnessM));
        s.Done();
        if (shadows.KeySplits is not (1 or 2 or 4)) throw new InvalidOperationException("x_look_shadows.key_splits must be 1, 2 or 4");

        var a = new Fields(Block("x_look_ssao"), "x_look_ssao", defaulted);
        var ssao = new SsaoTuning(a.F("power", d.Ssao.Power), a.F("detail", d.Ssao.Detail), a.F("horizon", d.Ssao.Horizon),
            a.F("sharpness", d.Ssao.Sharpness), a.F("light_affect", d.Ssao.LightAffect), a.F("ao_channel_affect", d.Ssao.AoChannelAffect));
        a.Done();

        var g = new Fields(Block("x_look_glow"), "x_look_glow", defaulted);
        var glow = new GlowTuning(g.F("strength", d.Glow.Strength), g.S("blend_mode", d.Glow.BlendMode), g.F("hdr_threshold", d.Glow.HdrThreshold),
            g.Floats("levels", d.Glow.Levels));
        g.Done();
        if (glow.Levels.Count != 7) throw new InvalidOperationException("x_look_glow.levels must list 7 glow levels");
        if (glow.BlendMode is not ("additive" or "screen" or "softlight" or "replace" or "mix"))
            throw new InvalidOperationException($"x_look_glow.blend_mode '{glow.BlendMode}' is not additive, screen, softlight, replace or mix");

        var v = new Fields(Block("x_look_gi"), "x_look_gi", defaulted);
        var gi = new GiTuning(v.I("subdiv", d.Gi.Subdiv), v.F("margin_m", d.Gi.MarginM), v.F("bias", d.Gi.Bias), v.F("normal_bias", d.Gi.NormalBias),
            v.F("two_bounces_above", d.Gi.TwoBouncesAbove));
        v.Done();
        if (gi.Subdiv is not (64 or 128 or 256 or 512)) throw new InvalidOperationException("x_look_gi.subdiv must be 64, 128, 256 or 512");

        var l = new Fields(Block("x_look_lamps"), "x_look_lamps", defaulted);
        var lamps = new LampTuning(l.F("attenuation", d.Lamps.Attenuation), l.F("night_boost", d.Lamps.NightBoost),
            l.F("range_per_diagonal", d.Lamps.RangePerDiagonal), l.F("window_spot_angle_deg", d.Lamps.WindowSpotAngleDeg));
        l.Done();
        if (!(lamps.RangePerDiagonal > 0f && lamps.WindowSpotAngleDeg is > 0f and < 90f))
            throw new InvalidOperationException("x_look_lamps: range_per_diagonal must be positive and window_spot_angle_deg between 0 and 90");

        var u = new Fields(Block("x_look_sun"), "x_look_sun", defaulted);
        var sun = new SunTuning(u.F("degrees_per_hour", d.Sun.DegreesPerHour), u.F("max_elevation_deg", d.Sun.MaxElevationDeg),
            u.F("horizon_elevation_deg", d.Sun.HorizonElevationDeg), u.F("moon_elevation_deg", d.Sun.MoonElevationDeg),
            u.F("moon_azimuth_offset_deg", d.Sun.MoonAzimuthOffsetDeg), u.F("sun_daylight", d.Sun.SunDaylight), u.F("moon_daylight", d.Sun.MoonDaylight));
        u.Done();
        if (!(sun.MoonDaylight >= 0f && sun.SunDaylight > sun.MoonDaylight && sun.SunDaylight <= 1f))
            throw new InvalidOperationException("x_look_sun: 0 <= moon_daylight < sun_daylight <= 1");

        var e = new Fields(Block("x_look_seasons"), "x_look_seasons", defaulted);
        var seasons = new SeasonTuning(e.Ints("centre_days", d.Seasons.CentreDays), e.F("hold", d.Seasons.Hold), e.F("light_strength", d.Seasons.LightStrength),
            e.I("fixed_day_of_year", d.Seasons.FixedDayOfYear), e.Floats("day_length_h", d.Seasons.DayLengthH));
        e.Done();
        if (seasons.DayLengthH.Count is not (0 or 4) || seasons.DayLengthH.Any(h => h is < 1f or > 23f))
            throw new InvalidOperationException("x_look_seasons.day_length_h must list four day lengths (winter, spring, summer, autumn) between 1 and 23 hours, or none");
        if (seasons.FixedDayOfYear is < 1 or > 366 || seasons.Hold is < 0f or >= 0.5f)
            throw new InvalidOperationException("x_look_seasons: fixed_day_of_year must be 1 to 366 and hold at least 0 and under 0.5");
        if (seasons.CentreDays.Count != 4 || seasons.CentreDays.Zip(seasons.CentreDays.Skip(1)).Any(t => t.Second <= t.First) || seasons.CentreDays[0] < 1 || seasons.CentreDays[3] > 365)
            throw new InvalidOperationException("x_look_seasons.centre_days must be four increasing days of the year (winter, spring, summer, autumn)");

        var r = new Fields(Block("x_look_grade"), "x_look_grade", defaulted);
        var grade = new GradeTuning(r.F("shadow_tone", d.Grade.ShadowTone), r.F("highlight_tone", d.Grade.HighlightTone), r.F("season_tint", d.Grade.SeasonTint),
            r.Vec("warmth_rgb", d.Grade.WarmthRgb), r.F("night_desaturate", d.Grade.NightDesaturate), r.Vec("night_tint_rgb", d.Grade.NightTintRgb),
            r.F("night_deepen", d.Grade.NightDeepen), r.I("lut_size", d.Grade.LutSize), r.F("season_tint_shadow_fade", d.Grade.SeasonTintShadowFade),
            r.F("night_full_below", d.Grade.NightFullBelow), r.F("night_none_above", d.Grade.NightNoneAbove));
        if (!(grade.NightFullBelow >= 0f && grade.NightNoneAbove > grade.NightFullBelow && grade.NightNoneAbove <= 1f))
            throw new InvalidOperationException("x_look_grade: 0 <= night_full_below < night_none_above <= 1");
        if (grade.SeasonTintShadowFade is < 0f or > 1f) throw new InvalidOperationException("x_look_grade.season_tint_shadow_fade must be between 0 and 1");
        r.Done();
        if (grade.LutSize is < 8 or > 65) throw new InvalidOperationException("x_look_grade.lut_size must be between 8 and 65");

        var f = new Fields(Block("x_look_dof"), "x_look_dof", defaulted);
        var dof = new DofTuning(f.F("tilt_pitch_gain", d.Dof.TiltPitchGain), f.F("tilt_band_narrowing", d.Dof.TiltBandNarrowing),
            f.F("far_transition_base_m", d.Dof.FarTransitionBaseM), f.F("far_transition_per_m", d.Dof.FarTransitionPerM), f.F("far_blur_reference", d.Dof.FarBlurReference),
            f.F("near_transition_base", d.Dof.NearTransitionBase), f.F("near_transition_per_blur", d.Dof.NearTransitionPerBlur),
            f.F("amount_base", d.Dof.AmountBase), f.F("amount_per_blur", d.Dof.AmountPerBlur),
            f.F("eye_focus_body_heights", d.Dof.EyeFocusBodyHeights), f.F("body_focus_height_fraction", d.Dof.BodyFocusHeightFraction),
            f.F("eye_crisp_body_heights", d.Dof.EyeCrispBodyHeights), f.F("tilt_transition_shortening", d.Dof.TiltTransitionShortening),
            f.F("tilt_amount_gain", d.Dof.TiltAmountGain), f.F("companion_follow_m", d.Dof.CompanionFollowM));
        if (dof.TiltTransitionShortening is < 0f or >= 1f || dof.TiltBandNarrowing is < 0f or >= 1f || dof.CompanionFollowM < 0f)
            throw new InvalidOperationException("x_look_dof: tilt_transition_shortening and tilt_band_narrowing must be at least 0 and under 1, companion_follow_m not negative");
        f.Done();

        var o = new Fields(Block("x_look_post"), "x_look_post", defaulted);
        var post = new PostTuning(o.F("vignette_start", d.Post.VignetteStart), o.F("vignette_end", d.Post.VignetteEnd),
            o.F("grain_fine", d.Post.GrainFine), o.F("grain_soft", d.Post.GrainSoft), o.F("grain_soft_px", d.Post.GrainSoftPx));
        o.Done();

        return new LookTuning(roles, defaultMarks, paint, shadows, ssao, glow, gi, lamps, sun, seasons, grade, dof, post) { Defaulted = defaulted };
    }

    private static RoleLook Role(JsonElement element, RoleLook fallback, string label, List<string> defaulted)
    {
        var f = new Fields(element, label, defaulted);
        var patternName = f.S("pattern", fallback.Pattern.ToString().ToLowerInvariant());
        if (!PatternNames.TryGetValue(patternName, out var pattern)) throw new InvalidOperationException($"{label}.pattern '{patternName}' is not one of {string.Join(", ", PatternNames.Keys)}");
        var axisName = f.S("stroke_axis", fallback.StrokeAxis == Vector3.Up ? "y" : fallback.StrokeAxis == Vector3.Back ? "z" : "x");
        var axis = axisName switch { "x" => Vector3.Right, "y" => Vector3.Up, "z" => Vector3.Back, _ => throw new InvalidOperationException($"{label}.stroke_axis must be x, y or z") };
        var look = new RoleLook(pattern, f.F("stroke_scale_m", fallback.StrokeScaleM), f.F("stroke_stretch", fallback.StrokeStretch), axis,
            f.F("pattern_scale_m", fallback.PatternScaleM), f.F("variation", fallback.Variation), f.F("wrap", fallback.Wrap),
            f.F("terminator_warmth", fallback.TerminatorWarmth), f.F("sheen", fallback.Sheen), f.F("specular", fallback.Specular),
            f.F("texture_strength", fallback.TextureStrength), f.F("texture_scale_m", fallback.TextureScaleM));
        f.Done();
        if (look.StrokeScaleM <= 0f || look.PatternScaleM <= 0f || look.StrokeStretch < 1f || look.TextureScaleM <= 0f)
            throw new InvalidOperationException($"{label}: scales must be positive and stroke_stretch at least 1");
        return look;
    }

    /// <summary>Reads named fields of one block with defaults, records each default it used, and refuses any field it did not read.</summary>
    private sealed class Fields
    {
        private readonly JsonElement _element;
        private readonly string _label;
        private readonly HashSet<string> _read = new();
        private readonly List<string> _defaulted;

        public Fields(JsonElement element, string label, List<string> defaulted)
        {
            if (element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
                throw new InvalidOperationException($"{label} must be an object");
            _element = element;
            _label = label;
            _defaulted = defaulted;
        }

        private bool Get(string name, out JsonElement value)
        {
            _read.Add(name);
            value = default;
            if (_element.ValueKind == JsonValueKind.Object && _element.TryGetProperty(name, out value)) return true;
            _defaulted.Add(_label + "." + name);
            return false;
        }

        public float F(string name, float fallback)
        {
            if (!Get(name, out var value)) return fallback;
            var number = value.GetSingle();
            if (!float.IsFinite(number)) throw new InvalidOperationException($"{_label}.{name} must be finite");
            return number;
        }

        public int I(string name, int fallback) => Get(name, out var value) ? value.GetInt32() : fallback;

        public string S(string name, string fallback) => Get(name, out var value) ? value.GetString()! : fallback;

        public Vector3 Vec(string name, Vector3 fallback)
        {
            if (!Get(name, out var value)) return fallback;
            if (value.GetArrayLength() != 3) throw new InvalidOperationException($"{_label}.{name} must have three numbers");
            return new Vector3(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
        }

        public IReadOnlyList<float> Floats(string name, IReadOnlyList<float> fallback) =>
            Get(name, out var value) ? value.EnumerateArray().Select(x => x.GetSingle()).ToArray() : fallback;

        public IReadOnlyList<int> Ints(string name, IReadOnlyList<int> fallback) =>
            Get(name, out var value) ? value.EnumerateArray().Select(x => x.GetInt32()).ToArray() : fallback;

        public void Done()
        {
            if (_element.ValueKind != JsonValueKind.Object) return;
            var unknown = _element.EnumerateObject().Select(p => p.Name).Where(n => !_read.Contains(n)).ToArray();
            if (unknown.Length > 0) throw new InvalidOperationException($"{_label} has unknown field(s) {string.Join(", ", unknown)}");
        }
    }
}
