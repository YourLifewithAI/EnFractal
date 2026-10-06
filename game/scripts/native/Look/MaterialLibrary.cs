using Godot;
using System.Collections.Generic;
using System.Linq;

namespace EnFractal.Native.Look;

/// <summary>Surface patterns the painterly shader knows; values match the constants in painterly_material.gdshader.</summary>
public enum PaintPattern { None = 0, Wood = 1, Fabric = 2, Plaster = 3, Paper = 4, Cardboard = 5, Brushed = 6, Speckle = 7, Smooth = 8, Stone = 9 }

/// <summary>
/// How a material role is painted, beyond the preset's per-role numbers: which pattern, how big the brush
/// marks are and which way they run, and how the role takes light. Sizes are metres in the real-scale room;
/// the player is 10 cm tall, so marks are sized to read from its eye.
/// </summary>
public sealed record RoleLook(
    PaintPattern Pattern, float StrokeScaleM, float StrokeStretch, Vector3 StrokeAxis, float PatternScaleM,
    float Variation, float Wrap, float TerminatorWarmth, float Sheen, float Specular);

/// <summary>
/// Maps a material role and base colour to a painterly material driven by the style preset (L3). The
/// preset's materials.roles (or materials.default) sets albedo softening, stroke normal strength, edge
/// wear, roughness, metallic, tint and an optional painted texture; RoleLooks sets the pattern and marks.
/// A role tint is a glaze: it moves the base colour's hue toward the tint while keeping its lightness.
/// </summary>
public static class MaterialLibrary
{
    public const string ShaderPath = "res://shaders/painterly_material.gdshader";
    private static StylePreset? _preset;
    private static Shader? _shader;
    private static readonly Dictionary<string, Material> Cache = new();

    private static readonly RoleLook Plain = new(PaintPattern.None, 0.05f, 2f, Vector3.Up, 0.1f, 0.4f, 0.25f, 0.2f, 0f, 0.35f);

    public static readonly IReadOnlyDictionary<string, RoleLook> RoleLooks = new Dictionary<string, RoleLook>
    {
        ["painted_wall"] = new(PaintPattern.Plaster, 0.16f, 1.8f, Vector3.Up, 0.3f, 0.55f, 0.30f, 0.25f, 0f, 0.20f),
        ["plaster"] = new(PaintPattern.Plaster, 0.18f, 1.8f, Vector3.Right, 0.4f, 0.55f, 0.30f, 0.25f, 0f, 0.20f),
        ["wallpaper"] = new(PaintPattern.Plaster, 0.06f, 5f, Vector3.Up, 0.20f, 0.40f, 0.30f, 0.20f, 0f, 0.20f),
        ["wood"] = new(PaintPattern.Wood, 0.04f, 6f, Vector3.Right, 0.11f, 0.80f, 0.20f, 0.30f, 0f, 0.35f),
        ["wood_painted"] = new(PaintPattern.Wood, 0.05f, 5f, Vector3.Right, 0.11f, 0.40f, 0.25f, 0.25f, 0f, 0.30f),
        ["fabric"] = new(PaintPattern.Fabric, 0.05f, 2f, Vector3.Right, 0.004f, 0.60f, 0.45f, 0.30f, 0.35f, 0.05f),
        ["carpet"] = new(PaintPattern.Fabric, 0.06f, 1.5f, Vector3.Right, 0.006f, 0.55f, 0.50f, 0.30f, 0.45f, 0.03f),
        ["leather"] = new(PaintPattern.Smooth, 0.03f, 2f, Vector3.Right, 0.05f, 0.45f, 0.25f, 0.25f, 0.15f, 0.40f),
        ["metal"] = new(PaintPattern.Brushed, 0.05f, 8f, Vector3.Right, 0.03f, 0.35f, 0.10f, 0.10f, 0f, 0.90f),
        ["metal_painted"] = new(PaintPattern.Smooth, 0.05f, 3f, Vector3.Right, 0.05f, 0.40f, 0.20f, 0.20f, 0f, 0.50f),
        ["glass"] = new(PaintPattern.Smooth, 0.08f, 2f, Vector3.Up, 0.10f, 0.10f, 0.10f, 0.05f, 0f, 0.90f),
        ["mirror"] = new(PaintPattern.Smooth, 0.08f, 2f, Vector3.Up, 0.10f, 0.05f, 0.05f, 0.00f, 0f, 1.00f),
        ["ceramic"] = new(PaintPattern.Smooth, 0.04f, 2f, Vector3.Up, 0.05f, 0.30f, 0.20f, 0.20f, 0f, 0.60f),
        ["stone"] = new(PaintPattern.Stone, 0.06f, 2f, Vector3.Right, 0.02f, 0.70f, 0.20f, 0.25f, 0f, 0.20f),
        ["concrete"] = new(PaintPattern.Stone, 0.08f, 2f, Vector3.Right, 0.03f, 0.60f, 0.20f, 0.20f, 0f, 0.15f),
        ["brick"] = new(PaintPattern.Stone, 0.05f, 3f, Vector3.Right, 0.02f, 0.70f, 0.20f, 0.25f, 0f, 0.15f),
        ["paper"] = new(PaintPattern.Paper, 0.03f, 3f, Vector3.Right, 0.0012f, 0.35f, 0.35f, 0.25f, 0.05f, 0.10f),
        ["cardboard"] = new(PaintPattern.Cardboard, 0.04f, 3f, Vector3.Right, 0.004f, 0.50f, 0.30f, 0.30f, 0f, 0.10f),
        ["plastic"] = new(PaintPattern.Smooth, 0.05f, 2f, Vector3.Right, 0.05f, 0.30f, 0.25f, 0.20f, 0.05f, 0.60f),
        ["rubber"] = new(PaintPattern.Speckle, 0.03f, 2f, Vector3.Right, 0.0025f, 0.40f, 0.30f, 0.20f, 0f, 0.15f),
    };

    public static void Configure(StylePreset preset)
    {
        _preset = preset;
        _shader = GD.Load<Shader>(ShaderPath);
        Cache.Clear();
    }

    public static RoleLook LookFor(string role) => RoleLooks.TryGetValue(role, out var look) ? look : Plain;

    public static Material For(string role, Color baseColor)
    {
        var key = role + "|" + baseColor.ToHtml(false);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        if (_preset == null || _shader == null)
        {
            // No preset yet (a builder test without a look): a plain, honest material.
            var plain = new StandardMaterial3D { AlbedoColor = baseColor, Roughness = 0.92f, ResourceName = $"{role} {baseColor.ToHtml(false)}" };
            Cache[key] = plain;
            return plain;
        }
        var treatment = _preset.TreatmentFor(role);
        var look = LookFor(role);
        var albedo = Glaze(baseColor, treatment.Tint, _preset.GlazeAmount);
        var material = new ShaderMaterial { Shader = _shader, ResourceName = $"painterly {role} {baseColor.ToHtml(false)}" };
        material.SetShaderParameter("base_color", albedo);
        material.SetShaderParameter("accent_color", Accent(albedo, _preset.Accents));
        material.SetShaderParameter("pattern", (int)look.Pattern);
        material.SetShaderParameter("albedo_softening", treatment.AlbedoSoftening);
        material.SetShaderParameter("stroke_normal_strength", treatment.StrokeNormalStrength);
        material.SetShaderParameter("edge_wear", treatment.EdgeWear);
        material.SetShaderParameter("roughness_value", treatment.Roughness);
        material.SetShaderParameter("metallic_value", treatment.Metallic);
        material.SetShaderParameter("specular_strength", look.Specular);
        material.SetShaderParameter("stroke_scale_m", look.StrokeScaleM);
        material.SetShaderParameter("stroke_stretch", look.StrokeStretch);
        material.SetShaderParameter("stroke_axis", look.StrokeAxis);
        material.SetShaderParameter("pattern_scale_m", look.PatternScaleM);
        material.SetShaderParameter("variation", look.Variation);
        material.SetShaderParameter("wrap_light", look.Wrap);
        material.SetShaderParameter("terminator_warmth", look.TerminatorWarmth);
        material.SetShaderParameter("sheen", look.Sheen);
        material.SetShaderParameter("shadow_tint", _preset.ShadowTint);
        material.SetShaderParameter("shadow_fill", _preset.ShadowsEnabled ? 0.25f + 0.3f * _preset.ShadowSoftness : 0f);
        material.SetShaderParameter("bevel_min_m", _preset.BevelM);
        if (treatment.Texture is { } texturePath && ResourceLoader.Exists(texturePath))
        {
            material.SetShaderParameter("paint_texture", GD.Load<Texture2D>(texturePath));
            material.SetShaderParameter("texture_strength", 0.35f);
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
    public static Color Accent(Color baseColor, IReadOnlyList<Color> accents)
    {
        if (accents.Count == 0) return baseColor.Darkened(0.25f);
        var pick = accents.OrderByDescending(a => HueDistance(a, baseColor)).First();
        return baseColor.Lerp(pick, 0.55f).Darkened(0.12f);
    }

    private static float HueDistance(Color a, Color b)
    {
        var d = Mathf.Abs(a.H - b.H);
        return Mathf.Min(d, 1f - d) * Mathf.Min(a.S + 0.2f, 1f);
    }

    /// <summary>The flat colour VoxelGI should voxelize for a material built here, or null for other materials.</summary>
    public static Color? BakeAlbedo(Material? material) =>
        material != null && material.HasMeta("bake_albedo") ? material.GetMeta("bake_albedo").AsColor() : null;
}
