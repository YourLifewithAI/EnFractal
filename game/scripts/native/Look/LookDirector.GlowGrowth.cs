using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EnFractal.Native.Look;

/// <summary>Draws host-verified Glow discoveries and cosmetic touches. No heat, force, injury or discovery authority lives here.</summary>
public partial class LookDirector
{
    private readonly List<(string First, string Second)> _glowConnections = new();
    private MeshInstance3D? _glowLinkDraw;
    private float _glowSoundVolume = 0.35f;
    private bool _glowSoundMuted;
    private readonly Dictionary<string, AudioStreamWav> _glowChimes = new(StringComparer.Ordinal);
    public int GlowConnectionCount => _glowConnections.Count(link => _glows.ContainsKey(link.First) && _glows.ContainsKey(link.Second));
    public string? GlowVariant(string id) => _glows.TryGetValue(id, out var glow) ? glow.Variant : null;
    public string? GlowTouch(string id) => _glows.TryGetValue(id, out var glow) ? glow.Touch : null;

    public bool StartGlowVariant(string id, Node3D? follow, Vector3 center, float radiusM, float intensity,
        string variant, string touch, string? bubbleSource, float volume, bool muted)
    {
        if (variant is not ("plain" or "lantern" or "torch" or "bioluminesce" or "skylight" or "glowing_bubbles") ||
            touch is not ("plain" or "air" or "ember" or "frost" or "chime")) return false;
        if (variant == "glowing_bubbles" && (bubbleSource == null || KindOf(bubbleSource) != EffectKind.Bubbles)) return false;
        if (!StartGlow(id, follow, center, radiusM, intensity)) return false;
        var glow = _glows[id];
        glow.Variant = variant;
        glow.Touch = touch;
        glow.BubbleSource = bubbleSource;
        glow.Anchor = center;
        SetGlowAudio(volume, muted);
        if (variant == "bioluminesce")
        {
            glow.Center = center + Vector3.Up * 0.012f;
            glow.Halo.Visible = false;
            if (glow.Core != null) glow.Core.Visible = false;
            Paint(glow, new Color("7aead7"));
        }
        if (variant == "skylight")
        {
            // A high source on avatar scale, inside its real light's bounded reach of the anchor.
            glow.Center = center + Vector3.Up * (glow.RadiusM * 0.7f);
            glow.Halo.Scale *= 0.7f;
            Paint(glow, new Color("dde9ff"));
        }
        if (variant == "glowing_bubbles")
        {
            glow.Halo.Visible = false;
            if (glow.Core != null) glow.Core.Visible = false;
        }
        if (variant is "bioluminesce" or "glowing_bubbles" || touch != "plain") AddGlowDecoration(glow);
        UpdateGlowVariant(glow, 0);
        glow.Root.Position = GlowPosition(glow, false);
        return true;
    }

    public void SetGlowAudio(float volume, bool muted)
    {
        _glowSoundVolume = float.IsFinite(volume) ? Mathf.Clamp(volume, 0, 1) : 0;
        _glowSoundMuted = muted;
        foreach (var glow in _glowOrder)
            if (glow.Chime != null)
            {
                glow.Chime.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.0001f, _glowSoundVolume * 0.3f));
                if (muted || volume <= 0) glow.Chime.Stop();
            }
    }

    /// <summary>Only called after the host has saved and accepted the cast; failed previews/casts stay silent.</summary>
    public void PlayGlowSound(string id)
    {
        if (_glowSoundMuted || _glowSoundVolume <= 0 || !_glows.TryGetValue(id, out var glow)) return;
        var voice = glow.Touch;
        if (!_glowChimes.TryGetValue(voice, out var stream))
        {
            const int rate = 22050;
            var duration = voice == "chime" ? 0.65 : 0.25;
            var count = (int)(rate * duration);
            var data = new byte[count * 2];
            var frequency = voice switch { "air" => 523.25, "ember" => 329.63, "frost" => 783.99, "chime" => 659.25, _ => 440.0 };
            for (var index = 0; index < count; index++)
            {
                var time = (double)index / rate;
                var fade = Math.Sin(Math.PI * index / count) * Math.Exp(-time * 4);
                var sample = (short)(6500 * fade * (Math.Sin(2 * Math.PI * frequency * time) + 0.25 * Math.Sin(2 * Math.PI * frequency * 2 * time)));
                data[index * 2] = (byte)(sample & 255);
                data[index * 2 + 1] = (byte)((sample >> 8) & 255);
            }
            stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = data };
            _glowChimes[voice] = stream;
        }
        glow.Chime ??= new AudioStreamPlayer3D { Name = "GlowChime", MaxDistance = 4f };
        if (glow.Chime.GetParent() == null) glow.Root.AddChild(glow.Chime);
        glow.Chime.Stream = stream;
        glow.Chime.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.0001f, _glowSoundVolume * 0.3f));
        glow.Chime.Play();
    }

    private static void ReleaseGlowSound(Glow glow)
    {
        if (glow.Chime == null || !IsInstanceValid(glow.Chime)) return;
        // Stop also cancels a Play queued for the next physics frame. Detaching the
        // stream releases the player's playback before its parent can be freed.
        glow.Chime.Stop();
        glow.Chime.Stream = null;
    }

    private void ReleaseGlowAudio()
    {
        foreach (var glow in _glowOrder) ReleaseGlowSound(glow);
        // Voices are shared by live Glows, so only the Look owns their lifetime.
        // All players must detach before disposing these generated resources.
        foreach (var stream in _glowChimes.Values) stream.Dispose();
        _glowChimes.Clear();
    }

    public bool MoveGlow(string id, Vector3 center)
    {
        if (!center.IsFinite() || !_glows.TryGetValue(id, out var glow) || glow.Follow != null || glow.Variant is not ("plain" or "torch")) return false;
        glow.Anchor = center;
        glow.Center = center + Vector3.Up * GlowLook.WispLiftM;
        glow.Root.Position = GlowPosition(glow, false);
        if (glow.Spark != null && IsInstanceValid(glow.Spark)) { glow.Spark.Free(); glow.Spark = null; }
        glow.Bloomed = true;
        glow.Opened = 1;
        glow.Light.LightEnergy = glow.Energy;
        UpdateGlowLinks();
        return true;
    }

    public void SetGlowConnections(IReadOnlyList<(string First, string Second)> connections)
    {
        _glowConnections.Clear();
        foreach (var link in connections.Take(28))
            if (link.First != link.Second && _glows.ContainsKey(link.First) && _glows.ContainsKey(link.Second)) _glowConnections.Add(link);
        UpdateGlowLinks();
    }

    private void UpdateGlowLinks()
    {
        _glowConnections.RemoveAll(link => !_glows.ContainsKey(link.First) || !_glows.ContainsKey(link.Second));
        if (_glowConnections.Count == 0)
        {
            if (_glowLinkDraw != null && IsInstanceValid(_glowLinkDraw)) _glowLinkDraw.Visible = false;
            return;
        }
        if (_glowLinkDraw == null || !IsInstanceValid(_glowLinkDraw))
        {
            _glowLinkDraw = new MeshInstance3D
            {
                Name = "ConstellationTendrils", TopLevel = true,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(0.7f, 0.9f, 1f, 0.65f), EmissionEnabled = true,
                    Emission = new Color("bfdcff"), EmissionEnergyMultiplier = 1.8f,
                },
            };
            AddChild(_glowLinkDraw);
        }
        var mesh = _glowLinkDraw.Mesh as ImmediateMesh ?? new ImmediateMesh();
        mesh.ClearSurfaces();
        foreach (var link in _glowConnections)
        {
            var from = GlowPosition(_glows[link.First], true);
            var to = GlowPosition(_glows[link.Second], true);
            mesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
            for (var part = 0; part <= 12; part++)
            {
                var t = part / 12f;
                mesh.SurfaceAddVertex(from.Lerp(to, t) + Vector3.Up * (Mathf.Sin(t * Mathf.Pi) * 0.035f));
            }
            mesh.SurfaceEnd();
        }
        _glowLinkDraw.Mesh = mesh;
        _glowLinkDraw.Visible = true;
    }

    private void AddGlowDecoration(Glow glow)
    {
        var count = glow.Variant is "bioluminesce" or "glowing_bubbles" ? 24 : 12;
        var color = glow.Touch switch
        {
            "ember" => new Color("ffb26d"), "frost" => new Color("9deaff"),
            "air" => new Color("d8faff"), "chime" => new Color("ffe0a6"), _ => new Color("7aead7"),
        };
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, InstanceCount = count,
            Mesh = glow.Touch == "frost" && glow.Variant is not ("bioluminesce" or "glowing_bubbles")
                ? new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 4, Rings = 1 }
                : new QuadMesh { Size = Vector2.One },
        };
        glow.Decoration = new MultiMeshInstance3D
        {
            Name = "GlowTouch", Multimesh = multimesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                AlbedoTexture = glow.Touch == "frost" ? null : HeartTexture(), AlbedoColor = color, EmissionEnabled = true, Emission = color,
                EmissionEnergyMultiplier = 1.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        glow.Root.AddChild(glow.Decoration);
    }

    private void UpdateGlowVariant(Glow glow, float delta)
    {
        if (glow.Variant == "skylight")
        {
            var night = Moment == null ? 1f : Mathf.Clamp((float)Moment.MoonWeight, 0, 1);
            glow.Energy = GlowLook.Energy(glow.Intensity) * 0.2f * night;
            glow.Light.LightEnergy = glow.Energy;
        }
        if (glow.Decoration == null) return;
        var mesh = glow.Decoration.Multimesh;
        var bubbles = glow.BubbleSource == null ? Array.Empty<(Vector3 Position, float Radius, bool Popping)>() : EffectParticles(glow.BubbleSource).ToArray();
        for (var index = 0; index < mesh.InstanceCount; index++)
        {
            var angle = index * 2.399963f + glow.Phase;
            var clock = (float)_glowClock;
            var size = 0.014f;
            Vector3 offset;
            if (glow.Variant == "glowing_bubbles")
            {
                if (index >= bubbles.Length || bubbles[index].Popping)
                { mesh.SetInstanceTransform(index, new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero)); continue; }
                offset = bubbles[index].Position - GlowPosition(glow, true);
                size = bubbles[index].Radius * 1.6f;
            }
            else if (glow.Variant == "bioluminesce")
            {
                var distance = Mathf.Sqrt((index + 0.5f) / mesh.InstanceCount) * glow.RadiusM * 0.85f;
                offset = new Vector3(Mathf.Cos(angle) * distance, Mathf.Sin(clock * 1.5f + angle) * 0.003f, Mathf.Sin(angle) * distance);
                size = 0.025f + 0.012f * Mathf.Sin(clock + angle);
            }
            else
            {
                var orbit = glow.Touch == "air" ? 0.12f : 0.06f;
                angle += clock * (glow.Touch == "frost" ? 0.12f : 0.8f);
                var height = glow.Touch == "ember" ? Mathf.PosMod(clock * 0.07f + index * 0.015f, 0.18f) : Mathf.Sin(angle * 2) * 0.045f;
                offset = new Vector3(Mathf.Cos(angle) * orbit, height, Mathf.Sin(angle) * orbit);
                size = glow.Touch == "air" ? 0.022f : glow.Touch == "chime" ? 0.01f : 0.015f;
            }
            var scale = glow.Touch == "ember" ? new Vector3(size * 0.55f, size * 2f, size) :
                glow.Touch == "air" ? new Vector3(size * 2.5f, size * 0.55f, size) : Vector3.One * size;
            mesh.SetInstanceTransform(index, new Transform3D(Basis.Identity.Scaled(scale), offset));
            mesh.SetInstanceColor(index, new Color(1, 1, 1, 0.55f + Mathf.Sin(clock * 1.3f + angle) * 0.3f));
        }
    }
}
