using Godot;
using System;

namespace EnFractal.Native.Look;

/// <summary>Everything the colour grade depends on at one moment.</summary>
public sealed record GradeParams(
    float Saturation, float Contrast, float Warmth, Color ShadowTint, Color HighlightTint,
    Color SeasonTint, float Night)
{
    /// <summary>A grade that changes nothing: grey tints carry no colour.</summary>
    public static GradeParams Identity { get; } = new(1f, 1f, 0f, new Color(0.5f, 0.5f, 0.5f), new Color(0.5f, 0.5f, 0.5f), new Color(0.5f, 0.5f, 0.5f), 0f);

    /// <summary>The palette grade for a preset at a moment: palette times season, plus night.</summary>
    public static GradeParams For(StylePreset preset, LookMoment moment) => new(
        preset.Saturation * moment.SeasonSaturation,
        preset.Contrast,
        Mathf.Clamp(preset.Warmth + moment.SeasonWarmth, -1f, 1f),
        preset.PaletteShadowTint,
        preset.HighlightTint,
        moment.SeasonTint,
        1f - moment.Daylight);
}

/// <summary>
/// The display-space colour grade the look applies after tone mapping, baked into a 3D lookup table for
/// Environment.adjustment_color_correction. Order: warmth, season tint, split toning (cool shadows, warm
/// highlights), saturation, night, contrast. Tints act through their colour only, so grey tints and a
/// neutral palette leave the image unchanged.
/// </summary>
public static class ColorGrade
{
    public const int LutSize = 33;
    private const float ShadowToneStrength = 0.8f;
    private const float HighlightToneStrength = 0.35f;
    private const float SeasonTintStrength = 0.24f;

    public static float Luma(Color c) => 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;

    private static Color Chroma(Color tint)
    {
        var luma = Luma(tint);
        return new Color(tint.R - luma, tint.G - luma, tint.B - luma);
    }

    public static Color Apply(GradeParams g, Color input)
    {
        var c = input;
        // White balance: warmth lifts red, trims blue.
        c = new Color(c.R * (1f + 0.08f * g.Warmth), c.G * (1f + 0.015f * g.Warmth), c.B * (1f - 0.10f * g.Warmth));
        // Season tint as a gentle colour cast, strongest in the mid-tones.
        var luma = Luma(c);
        var mid = 4f * luma * (1f - luma);
        c += Chroma(g.SeasonTint) * (SeasonTintStrength * mid);
        // Split toning: colour the shadows and the highlights.
        luma = Mathf.Clamp(Luma(c), 0f, 1f);
        var shade = (1f - luma) * (1f - luma);
        var light = luma * luma;
        c += Chroma(g.ShadowTint) * (ShadowToneStrength * shade) + Chroma(g.HighlightTint) * (HighlightToneStrength * light);
        // Saturation around luma.
        luma = Luma(c);
        c = new Color(luma, luma, luma).Lerp(c, g.Saturation);
        // Night: bluer, less saturated, deeper shadows.
        if (g.Night > 0f)
        {
            luma = Mathf.Clamp(Luma(c), 0f, 1f);
            c = new Color(luma, luma, luma).Lerp(c, 1f - 0.3f * g.Night);
            c = new Color(c.R * (1f - 0.14f * g.Night), c.G * (1f - 0.06f * g.Night), c.B * (1f + 0.10f * g.Night));
            var deepen = 1f - 0.25f * g.Night * (1f - luma);
            c = new Color(c.R * deepen, c.G * deepen, c.B * deepen);
        }
        // Contrast around mid grey.
        c = new Color(0.5f + (c.R - 0.5f) * g.Contrast, 0.5f + (c.G - 0.5f) * g.Contrast, 0.5f + (c.B - 0.5f) * g.Contrast);
        return new Color(Mathf.Clamp(c.R, 0f, 1f), Mathf.Clamp(c.G, 0f, 1f), Mathf.Clamp(c.B, 0f, 1f));
    }

    /// <summary>
    /// RGB8 LUT bytes: slice z is blue, row y is green, column x is red. Godot samples the LUT directly with
    /// the colour as texture coordinate, so texel i holds the grade of input (i + 0.5) / size, which makes
    /// linear filtering reproduce the grade between texels.
    /// </summary>
    public static byte[] LutBytes(GradeParams g, int size = LutSize)
    {
        var bytes = new byte[size * size * size * 3];
        var index = 0;
        for (var z = 0; z < size; z++)
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var graded = Apply(g, new Color((x + 0.5f) / size, (y + 0.5f) / size, (z + 0.5f) / size));
                    bytes[index++] = ToByte(graded.R);
                    bytes[index++] = ToByte(graded.G);
                    bytes[index++] = ToByte(graded.B);
                }
        return bytes;
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    public static ImageTexture3D LutTexture(GradeParams g, int size = LutSize)
    {
        var bytes = LutBytes(g, size);
        var slices = new Godot.Collections.Array<Image>();
        var sliceBytes = size * size * 3;
        for (var z = 0; z < size; z++)
            slices.Add(Image.CreateFromData(size, size, false, Image.Format.Rgb8, bytes.AsSpan(z * sliceBytes, sliceBytes).ToArray()));
        var texture = new ImageTexture3D();
        var error = texture.Create(Image.Format.Rgb8, size, size, size, false, slices);
        if (error != Error.Ok) throw new InvalidOperationException($"could not build the colour grade LUT ({error})");
        return texture;
    }
}
