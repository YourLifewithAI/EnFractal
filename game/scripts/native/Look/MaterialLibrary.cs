using Godot;
using System.Collections.Generic;

namespace EnFractal.Native.Look;

/// <summary>
/// Maps a material role and base colour to a material. This is the seam the Look track's material
/// system (L3) replaces: today it is a plain StandardMaterial3D with the preset's roughness per role.
/// </summary>
public static class MaterialLibrary
{
    private static StylePreset? _preset;
    private static readonly Dictionary<string, Material> Cache = new();

    public static void Configure(StylePreset preset)
    {
        _preset = preset;
        Cache.Clear();
    }

    public static Material For(string role, Color baseColor)
    {
        var key = role + "|" + baseColor.ToHtml(false);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var roughness = 0.92f;
        if (_preset != null) roughness = _preset.RoleRoughness.TryGetValue(role, out var r) ? r : _preset.DefaultRoughness;
        var material = new StandardMaterial3D { AlbedoColor = baseColor, Roughness = roughness, ResourceName = $"{role} {baseColor.ToHtml(false)}" };
        Cache[key] = material;
        return material;
    }
}
