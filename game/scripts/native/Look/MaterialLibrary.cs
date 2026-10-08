using Godot;
using System.Collections.Generic;
using System.Linq;

namespace EnFractal.Native.Look;

/// <summary>
/// Maps a material role and base colour to a painterly material driven by the style preset (L3). The
/// preset's materials.roles (or materials.default) sets albedo softening, stroke normal strength, edge
/// wear, roughness, metallic, tint and an optional painted texture; x_look_role_marks sets the pattern and
/// marks per role and x_look_paint the shader's shared multipliers. A role tint is a glaze: it moves the
/// base colour's hue toward the tint while keeping its lightness.
/// </summary>
public static class MaterialLibrary
{
    public const string ShaderPath = "res://shaders/painterly_material.gdshader";
    private static StylePreset? _preset;
    private static Shader? _shader;
    private static readonly Dictionary<string, Material> Cache = new();
    private static readonly HashSet<string> SetNames = new();

    /// <summary>Every shader parameter name a material from here has been given; each must be a uniform of the shader.</summary>
    public static IReadOnlyCollection<string> ParameterNames => SetNames;

    public static void Configure(StylePreset preset)
    {
        _preset = preset;
        _shader = GD.Load<Shader>(ShaderPath);
        Cache.Clear();
    }

    public static RoleLook LookFor(string role) => (_preset?.Tuning ?? LookTuning.Default).MarksFor(role);

    /// <summary>Whether a material was made here (and so takes paint seeds and box edges).</summary>
    public static bool IsPainterly(Material? material) => material != null && material.HasMeta("material_role") && material is ShaderMaterial;

    private static void Set(ShaderMaterial material, string name, Variant value)
    {
        SetNames.Add(name);
        material.SetShaderParameter(name, value);
    }

    public static Material For(string role, Color baseColor) => Make(role, baseColor, null);

    /// <summary>
    /// The painterly material for a captured mesh surface (review minor: captured assets got no painterly treatment): the
    /// role's marks and softening over the surface's own colour, with its colour texture, if it has one, softened into a
    /// block-in. Falls back to a plain material, like For, when no preset is configured.
    /// </summary>
    public static Material ForCaptured(string role, Color albedo, Texture2D? texture) => Make(role, albedo, texture);

    private static Material Make(string role, Color baseColor, Texture2D? photo)
    {
        var key = role + "|" + baseColor.ToHtml(false) + (photo != null ? "|photo" + photo.GetInstanceId() : "");
        if (Cache.TryGetValue(key, out var cached)) return cached;
        if (_preset == null || _shader == null)
        {
            // No preset yet (a builder test without a look): a plain, honest material.
            var plain = new StandardMaterial3D { AlbedoColor = baseColor, AlbedoTexture = photo, Roughness = 0.92f, ResourceName = $"{role} {baseColor.ToHtml(false)}" };
            Cache[key] = plain;
            return plain;
        }
        var treatment = _preset.TreatmentFor(role);
        var look = LookFor(role);
        var paint = _preset.Tuning.Paint;
        var albedo = Glaze(baseColor, treatment.Tint, _preset.GlazeAmount);
        var material = new ShaderMaterial { Shader = _shader, ResourceName = $"painterly {role} {baseColor.ToHtml(false)}" };
        Set(material, "base_color", albedo);
        Set(material, "accent_color", Accent(albedo, _preset.Accents, paint));
        Set(material, "pattern", (int)look.Pattern);
        Set(material, "albedo_softening", treatment.AlbedoSoftening);
        Set(material, "stroke_normal_strength", treatment.StrokeNormalStrength);
        Set(material, "edge_wear", treatment.EdgeWear);
        Set(material, "roughness_value", treatment.Roughness);
        Set(material, "metallic_value", treatment.Metallic);
        Set(material, "specular_strength", look.Specular);
        Set(material, "stroke_scale_m", look.StrokeScaleM);
        Set(material, "stroke_stretch", look.StrokeStretch);
        Set(material, "stroke_axis", look.StrokeAxis);
        Set(material, "pattern_scale_m", look.PatternScaleM);
        Set(material, "variation", look.Variation);
        Set(material, "role_calm", look.Calm);
        Set(material, "wrap_light", look.Wrap);
        Set(material, "terminator_warmth", look.TerminatorWarmth);
        Set(material, "sheen", look.Sheen);
        Set(material, "bevel_min_m", _preset.BevelM);
        Set(material, "bevel_fraction", paint.BevelFraction);
        Set(material, "bevel_max_m", paint.BevelMaxM);
        Set(material, "tone_wash", paint.ToneWash);
        Set(material, "tone_strokes", paint.ToneStrokes);
        Set(material, "tone_bristle", paint.ToneBristle);
        Set(material, "tone_gain", paint.ToneGain);
        Set(material, "softening_calm", paint.SofteningCalm);
        Set(material, "temperature_variation", paint.TemperatureVariation);
        Set(material, "pastel_mix", paint.PastelMix);
        Set(material, "pastel_chroma", paint.PastelChroma);
        Set(material, "pastel_scale", paint.PastelScale);
        Set(material, "pastel_lift", paint.PastelLift);
        Set(material, "wear_brightness", paint.WearBrightness);
        Set(material, "wear_lift", paint.WearLift);
        Set(material, "cavity_darkening", paint.CavityDarkening);
        Set(material, "stroke_normal_gain", paint.StrokeNormalGain);
        Set(material, "mark_fade_start", paint.MarkFadeStart);
        Set(material, "mark_fade_end", paint.MarkFadeEnd);
        Set(material, "photo_mix", photo != null ? 1f : 0f);
        Set(material, "photo_blur_lod", paint.PhotoBlurLod);
        if (photo != null)
        {
            Set(material, "photo_texture", photo);
            material.SetMeta("captured", true);
        }
        if (treatment.Texture is { } texturePath && ResourceLoader.Exists(texturePath))
        {
            Set(material, "paint_texture", GD.Load<Texture2D>(texturePath));
            Set(material, "texture_strength", look.TextureStrength);
            Set(material, "texture_scale_m", look.TextureScaleM);
        }
        material.SetMeta("material_role", role);
        // VoxelGI voxelizes BaseMaterial3D albedo only; the look director bakes with this colour instead.
        material.SetMeta("bake_albedo", albedo);
        Cache[key] = material;
        return material;
    }

    /// <summary>Move a colour's hue toward a tint while keeping its lightness. No tint, or amount 0, leaves it unchanged.</summary>
    public static Color Glaze(Color baseColor, Color? tint, float amount)
    {
        if (tint is not { } t || amount <= 0f) return baseColor;
        var baseLuma = ColorGrade.Luma(baseColor);
        var tintLuma = Mathf.Max(ColorGrade.Luma(t), 1e-3f);
        var matched = new Color(t.R * baseLuma / tintLuma, t.G * baseLuma / tintLuma, t.B * baseLuma / tintLuma);
        var glazed = baseColor.Lerp(matched, amount);
        return new Color(Mathf.Clamp(glazed.R, 0f, 1f), Mathf.Clamp(glazed.G, 0f, 1f), Mathf.Clamp(glazed.B, 0f, 1f));
    }

    /// <summary>A trim colour for patterns such as a rug border: the palette accent furthest in hue from the base, softened toward it.</summary>
    public static Color Accent(Color baseColor, IReadOnlyList<Color> accents, PaintTuning paint)
    {
        if (accents.Count == 0) return baseColor.Darkened(paint.AccentDarken * 2f);
        var pick = accents.OrderByDescending(a => HueDistance(a, baseColor)).First();
        return baseColor.Lerp(pick, paint.AccentMix).Darkened(paint.AccentDarken);
    }

    private static float HueDistance(Color a, Color b)
    {
        var d = Mathf.Abs(a.H - b.H);
        return Mathf.Min(d, 1f - d) * Mathf.Min(a.S + 0.2f, 1f);
    }

    /// <summary>The flat colour VoxelGI should voxelize for a material built here, or null for other materials.</summary>
    public static Color? BakeAlbedo(Material? material) =>
        material != null && material.HasMeta("bake_albedo") ? material.GetMeta("bake_albedo").AsColor() : null;

    /// <summary>
    /// How much of a brush mark the shader keeps when one pixel covers footprint metres of a mark size metres
    /// across: all of it below mark_fade_start × size, none above mark_fade_end × size (smoothstep between).
    /// Mirrors detail() in painterly_material.gdshader; with mark_fade_end at most 0.5 a mark is gone before
    /// its Nyquist limit (a pixel covering half a period), so marks appear as the camera comes close instead
    /// of aliasing.
    /// </summary>
    public static float Detail(float sizeM, float footprintM, PaintTuning paint)
    {
        var x = Mathf.Clamp((footprintM - paint.MarkFadeStart * sizeM) / ((paint.MarkFadeEnd - paint.MarkFadeStart) * sizeM), 0f, 1f);
        return 1f - x * x * (3f - 2f * x);
    }
}
