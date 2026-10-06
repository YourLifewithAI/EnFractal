using Godot;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace EnFractal.Native.Look;

/// <summary>
/// The parts of an enfractal.style preset the runtime uses today. contracts/validate.py is the full
/// validator; this reader fails closed on the fields it needs. The Look track extends it as it
/// implements more of the preset (time of day, seasons, depth of field, post effects, charm).
/// </summary>
public sealed class StylePreset
{
    public string Path { get; private init; } = "";
    public string Sha256 { get; private init; } = "";
    public string PresetId { get; private init; } = "";
    public int PresetVersion { get; private init; }
    public string DisplayName { get; private init; } = "";
    public string Status { get; private init; } = "";
    public string RendererMethod { get; private init; } = "";
    public string TonemapMode { get; private init; } = "";
    public float Exposure { get; private init; }
    public float White { get; private init; } = 1f;
    public Color Background { get; private init; } = new("2a2f33");
    public Color KeyColor { get; private init; }
    public float KeyEnergy { get; private init; }
    public float KeyElevationDeg { get; private init; }
    public float KeyAzimuthDeg { get; private init; }
    public bool KeyCastsShadows { get; private init; }
    public Color AmbientColor { get; private init; }
    public float AmbientEnergy { get; private init; }
    public float HonorRoomLights { get; private init; }
    public float RoomLightEnergyScale { get; private init; } = 1f;
    public int MaxShadowedLights { get; private init; }
    public float DefaultRoughness { get; private init; }
    public IReadOnlyDictionary<string, float> RoleRoughness { get; private init; } = new Dictionary<string, float>();

    public const string StylesRoot = "res://styles";

    public static string PathFor(string presetId, int version) => $"{StylesRoot}/{presetId}/v{version}.json";

    /// <summary>Resolve a pinned preset: game/styles/&lt;id&gt;/v&lt;version&gt;.json, which must declare that id and version and, when a hash is pinned, match it.</summary>
    public static StylePreset Resolve(string presetId, int version, string? expectedSha256 = null)
    {
        var preset = Load(PathFor(presetId, version));
        if (preset.PresetId != presetId || preset.PresetVersion != version)
            throw new InvalidOperationException($"{preset.Path} declares {preset.PresetId} v{preset.PresetVersion}, not {presetId} v{version}");
        if (expectedSha256 != null && preset.Sha256 != expectedSha256)
            throw new InvalidOperationException($"{preset.Path} does not match its pin; a pinned preset version must never change");
        return preset;
    }

    public static StylePreset Load(string path)
    {
        if (!FileAccess.FileExists(path)) throw new InvalidOperationException($"style preset not found: {path}");
        var bytes = FileAccess.GetFileAsBytes(path);
        if (Array.IndexOf(bytes, (byte)'\r') >= 0) throw new InvalidOperationException($"{path} must use LF line endings; presets are pinned by hash");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != "enfractal.style" || root.GetProperty("version").GetInt32() != 1)
            throw new InvalidOperationException($"{path} is not an enfractal.style version 1 preset");
        var renderer = root.GetProperty("renderer");
        var tonemap = renderer.GetProperty("tonemap");
        var lighting = root.GetProperty("lighting");
        var key = lighting.GetProperty("key");
        var ambient = lighting.GetProperty("ambient");
        var materials = root.GetProperty("materials");
        var roles = new Dictionary<string, float>();
        foreach (var role in materials.GetProperty("roles").EnumerateObject())
            roles[role.Name] = role.Value.GetProperty("roughness").GetSingle();
        return new StylePreset
        {
            Path = path,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            PresetId = root.GetProperty("preset_id").GetString()!,
            PresetVersion = root.GetProperty("preset_version").GetInt32(),
            DisplayName = root.GetProperty("display_name").GetString()!,
            Status = root.GetProperty("status").GetString()!,
            RendererMethod = renderer.GetProperty("method").GetString()!,
            TonemapMode = tonemap.GetProperty("mode").GetString()!,
            Exposure = tonemap.GetProperty("exposure").GetSingle(),
            White = tonemap.TryGetProperty("white", out var white) ? white.GetSingle() : 1f,
            Background = lighting.TryGetProperty("background", out var background) ? new Color(background.GetString()!) : new Color("2a2f33"),
            KeyColor = new Color(key.GetProperty("color").GetString()!),
            KeyEnergy = key.GetProperty("energy").GetSingle(),
            KeyElevationDeg = key.GetProperty("elevation_deg").GetSingle(),
            KeyAzimuthDeg = key.GetProperty("azimuth_deg").GetSingle(),
            KeyCastsShadows = key.GetProperty("casts_shadows").GetBoolean(),
            AmbientColor = new Color(ambient.GetProperty("color").GetString()!),
            AmbientEnergy = ambient.GetProperty("energy").GetSingle(),
            HonorRoomLights = lighting.GetProperty("honor_room_lights").GetSingle(),
            RoomLightEnergyScale = lighting.TryGetProperty("room_light_energy_scale", out var scale) ? scale.GetSingle() : 1f,
            MaxShadowedLights = root.GetProperty("budgets").GetProperty("max_shadowed_lights").GetInt32(),
            DefaultRoughness = materials.GetProperty("default").GetProperty("roughness").GetSingle(),
            RoleRoughness = roles,
        };
    }
}
