using Godot;
using System;
using System.Linq;
using EnFractal.Native.Room;

namespace EnFractal.Native.Look;

/// <summary>
/// Applies a style preset to a room: environment, key light, ambient and the room's captured lights.
/// Owned by the Look track. The renderer itself is a project setting; a mismatch is reported, not fixed here.
/// </summary>
public partial class LookDirector : Node3D
{
    public StylePreset Preset { get; private set; } = null!;
    public int RoomLightCount { get; private set; }
    public string RendererNote { get; private set; } = "";

    public void Apply(StylePreset preset, RoomData room)
    {
        Preset = preset;
        MaterialLibrary.Configure(preset);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = preset.Background,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = preset.AmbientColor,
            AmbientLightEnergy = preset.AmbientEnergy,
            TonemapMode = preset.TonemapMode switch
            {
                "linear" => Godot.Environment.ToneMapper.Linear,
                "reinhard" => Godot.Environment.ToneMapper.Reinhardt,
                "aces" => Godot.Environment.ToneMapper.Aces,
                "agx" => Godot.Environment.ToneMapper.Agx,
                _ => Godot.Environment.ToneMapper.Filmic,
            },
            TonemapExposure = preset.Exposure,
            TonemapWhite = preset.White,
        };
        AddChild(new WorldEnvironment { Name = "Environment", Environment = environment });
        var diagonal = room.Bounds.Size.Length();
        AddChild(new DirectionalLight3D
        {
            Name = "Key", RotationDegrees = new Vector3(-preset.KeyElevationDeg, preset.KeyAzimuthDeg, 0),
            LightColor = preset.KeyColor, LightEnergy = preset.KeyEnergy, ShadowEnabled = preset.KeyCastsShadows,
            DirectionalShadowMaxDistance = Mathf.Max(12f, diagonal * 2f), ShadowBias = 0.02f,
        });
        var shadowBudget = Math.Max(0, preset.MaxShadowedLights - (preset.KeyCastsShadows ? 1 : 0));
        foreach (var hint in room.LightHints.Where(h => h.PositionM != null))
        {
            var energy = hint.RelativeIntensity * preset.RoomLightEnergyScale * preset.HonorRoomLights;
            if (energy <= 0) continue;
            Light3D light;
            if (hint.Kind is "ceiling_lamp" or "lamp" or "screen")
                light = new OmniLight3D { OmniRange = diagonal };
            else if (hint.Kind == "window" && hint.Direction is { } direction && direction.LengthSquared() > 1e-6f)
            {
                var spot = new SpotLight3D { SpotRange = diagonal, SpotAngle = 60f };
                spot.Position = hint.PositionM!.Value;
                var up = Mathf.Abs(direction.Normalized().Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
                spot.Basis = Basis.LookingAt(direction.Normalized(), up);
                light = spot;
            }
            else continue;
            light.Name = "RoomLight_" + hint.Id;
            if (light is OmniLight3D) light.Position = hint.PositionM!.Value;
            light.LightColor = hint.Color;
            light.LightEnergy = energy;
            light.ShadowEnabled = shadowBudget-- > 0;
            light.SetMeta("light_hint_id", hint.Id);
            AddChild(light);
            RoomLightCount++;
        }
        var current = ProjectSettings.GetSetting("rendering/renderer/rendering_method").AsString();
        if (current != preset.RendererMethod)
        {
            RendererNote = $"preset {preset.PresetId} is designed for {preset.RendererMethod}; project renders with {current}";
            GD.Print("LOOK: " + RendererNote);
        }
    }
}
