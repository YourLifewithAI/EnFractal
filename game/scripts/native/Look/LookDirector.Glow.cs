using Godot;
using System;
using System.Collections.Generic;
using EnFractal.Native.Room;

namespace EnFractal.Native.Look;

/// <summary>
/// The numbers of the Gubble's glow and of the light-level estimate (Run 2, Glow; docs/look/GLOW.md). They are code constants for
/// this round, like OpenLandLight; a later preset version can carry them as a look block once the founder has judged the glow on the GPU.
/// </summary>
public static class GlowLook
{
    /// <summary>The most glows the look draws at once: the schema's outer limit on max_active, so the look never refuses what a pack allows.</summary>
    public const int MaxGlows = 8;
    /// <summary>The most glows that cast a shadow at once, and only while the preset's shadow budget has a slot left after the sun and the room's lights.</summary>
    public const int MaxShadowedGlows = 1;
    /// <summary>The engine's bounds on light.emit's intensity (contracts/island-rules.schema.json); the look clamps to them.</summary>
    public const float MinIntensity = 0.05f;
    public const float MaxIntensity = 2.0f;
    /// <summary>The engine's bound on a glow's radius (area_radius_max_m at most 3 m), and the smallest the look draws.</summary>
    public const float MinRadiusM = 0.05f;
    public const float MaxRadiusM = 3.0f;
    /// <summary>The light's energy per unit of intensity: at the pack's default 0.6 the glow's energy is 0.3.</summary>
    public const float EnergyPerIntensity = 0.5f;
    /// <summary>The light's falloff exponent (Godot's omni_attenuation): gentler than the lamps' 2.5, so the glow is a soft pool and not a hot spot.</summary>
    public const float Attenuation = 0.75f;
    /// <summary>How large the source is, metres: soft shadow edges (when it has one) and soft highlights.</summary>
    public const float LightSizeM = 0.04f;
    /// <summary>How much highlight the glow puts on shiny things: low, so it paints rather than glints.</summary>
    public const float Specular = 0.3f;
    /// <summary>The colour of a glow when the Gubble has no aura colour: a warm white-gold.</summary>
    public static readonly Color DefaultColor = new("ffe4a8");
    /// <summary>How far an aura colour is lifted toward white for the light, so even a deep blue aura lights the ground readably.</summary>
    public const float AuraWhiteMix = 0.25f;

    /// <summary>A halo on a body sits at this fraction of the body's height (its middle, a little up).</summary>
    public const float FollowHeightFraction = 0.55f;
    /// <summary>The halo's diameter in body heights, around an avatar; around any other node, HaloDefaultM.</summary>
    public const float HaloBodyHeights = 2.0f;
    public const float HaloDefaultM = 0.45f;
    /// <summary>A wisp floats this far above the point it was cast at (the player aims at a surface), and bobs gently.</summary>
    public const float WispLiftM = 0.06f;
    public const float WispCoreRadiusM = 0.012f;
    public const float WispHaloM = 0.18f;
    public const float WispCoreEnergy = 2.5f;
    public const float BobAmplitudeM = 0.012f;
    public const float BobPeriodS = 3.2f;
    /// <summary>The halo breathes: its size swells and settles by this fraction over the period.</summary>
    public const float BreathAmount = 0.05f;
    public const float BreathPeriodS = 4.0f;
    /// <summary>The halo's opacity at its centre: base plus per unit of intensity, at most HaloAlphaMax.</summary>
    public const float HaloAlphaBase = 0.16f;
    public const float HaloAlphaPerIntensity = 0.2f;
    public const float HaloAlphaMax = 0.6f;
    /// <summary>The halo fades out as a camera comes within these distances (the eye view beside the Gubble), and softens where it meets a surface.</summary>
    public const float HaloFadeNearM = 0.04f;
    public const float HaloFadeFullM = 0.2f;
    public const float HaloProximityFadeM = 0.05f;

    // ---------- the light-level estimate ----------

    /// <summary>The estimate's rays start this far above the point asked about, so the ground it lies on never counts as cover.</summary>
    public const float RayLiftM = 0.02f;
    public const float RayLengthM = 64f;
    /// <summary>The sun or moon lights a spot by KeyFloor plus the rest times the sine of its elevation (light on the ground and on the sides of things).</summary>
    public const float KeyFloor = 0.5f;
    /// <summary>The sky's three rays: straight up (this weight) and two tilted TiltDeg from the zenith (the rest, shared).</summary>
    public const float ZenithWeight = 0.5f;
    public const float TiltDeg = 40f;
    /// <summary>Lamps and glows count no closer than this, metres: a bulb's hot spot makes a place no more "lit".</summary>
    public const float NearClampM = 0.1f;
    /// <summary>
    /// Light bounced into the shade from the lit ground around a spot: this fraction of the hour's sun and sky (what an open spot
    /// would get) reaches every spot, so the shade under an overhang at noon is darker than open ground but lighter than night.
    /// </summary>
    public const float BounceFraction = 0.08f;
    /// <summary>Indoors (a closed room, VoxelGI's interior) the room's lamps and window fills bounce off the walls: their light counts this much more.</summary>
    public const float IndoorBounce = 0.5f;
    /// <summary>
    /// The level curve is perceptual (each doubling of light is the same step): level 0 at LevelFloorEnergy and below (darker than a
    /// moonlit night), level 1 at LevelFullEnergy and above (full sun on open ground), logarithmic between.
    /// </summary>
    public const float LevelFloorEnergy = 0.02f;
    public const float LevelFullEnergy = 2.0f;

    /// <summary>The two tilted sky rays, in fixed world directions (deterministic): opposite each other, between the axes.</summary>
    public static readonly Vector3 TiltA = Tilt(30f);
    public static readonly Vector3 TiltB = Tilt(210f);

    private static Vector3 Tilt(float azimuthDeg)
    {
        var t = Mathf.DegToRad(TiltDeg);
        var a = Mathf.DegToRad(azimuthDeg);
        return new Vector3(Mathf.Sin(t) * Mathf.Cos(a), Mathf.Cos(t), Mathf.Sin(t) * Mathf.Sin(a)).Normalized();
    }

    /// <summary>The light's colour for an aura colour: its hue at full brightness (the energy carries the brightness), lifted a little toward white.</summary>
    public static Color LightColor(Color? aura)
    {
        if (aura is not { } c) return DefaultColor;
        var max = Mathf.Max(c.R, Mathf.Max(c.G, c.B));
        if (!float.IsFinite(max) || max < 1e-3f) return DefaultColor;
        var hue = new Color(Mathf.Clamp(c.R / max, 0f, 1f), Mathf.Clamp(c.G / max, 0f, 1f), Mathf.Clamp(c.B / max, 0f, 1f));
        return hue.Lerp(new Color(1f, 1f, 1f), AuraWhiteMix);
    }

    /// <summary>The light's energy for an intensity, clamped to the engine's bounds.</summary>
    public static float Energy(float intensity) => Mathf.Clamp(intensity, MinIntensity, MaxIntensity) * EnergyPerIntensity;

    /// <summary>The halo's centre opacity for an intensity.</summary>
    public static float HaloAlpha(float intensity) => Mathf.Min(HaloAlphaMax, HaloAlphaBase + HaloAlphaPerIntensity * Mathf.Clamp(intensity, MinIntensity, MaxIntensity));

    /// <summary>
    /// What a point light gives a spot at a distance: Godot's own omni falloff (a smooth window to zero at the range, times distance
    /// to the minus decay), counted no closer than NearClampM, times the light's luma.
    /// </summary>
    public static float PointLight(float energy, Color color, float rangeM, float decay, float distanceM)
    {
        if (energy <= 0f || rangeM <= 0f || distanceM >= rangeM) return 0f;
        var nd = distanceM / rangeM;
        nd *= nd;
        nd *= nd;
        var window = 1f - nd;
        window *= window;
        return energy * ColorGrade.Luma(color) * window * Mathf.Pow(Mathf.Max(distanceM, NearClampM), -decay);
    }

    /// <summary>A light level from 0 (dark) to 1 (bright) for an estimated amount of light.</summary>
    public static float Level(float energy) =>
        energy > LevelFloorEnergy && float.IsFinite(energy) ? Mathf.Min(1f, Mathf.Log(energy / LevelFloorEnergy) / Mathf.Log(LevelFullEnergy / LevelFloorEnergy)) : 0f;

    /// <summary>The light a level stands for (the inverse of Level, for levels between 0 and 1).</summary>
    public static float EnergyForLevel(float level) => LevelFloorEnergy * Mathf.Pow(LevelFullEnergy / LevelFloorEnergy, Mathf.Clamp(level, 0f, 1f));
}

/// <summary>What LightLevelAt counted at a spot, in the look's light units: the sun or moon, the sky, the room's lamps and windows, and the glows.</summary>
public readonly record struct LightEstimate(float Key, float Sky, float Bounce, float Lamps, float Glows, float SkyVisibility, bool KeySeen)
{
    public float Energy => Key + Sky + Bounce + Lamps + Glows;
    public float Level => GlowLook.Level(Energy);
}

public partial class LookDirector
{
    /// <summary>
    /// The level below which the game counts a place as dark (the dusk moment, "aimed at a dark spot, glow there"). On the default
    /// preset at the fallback site: summer noon on open ground is about 0.88, under a big overhang at noon 0, a moonlit night outdoors
    /// about 0.2, and the default glow lifts any spot within half its radius above 0.6 (docs/look/GLOW.md has the table).
    /// </summary>
    public const float DarkThreshold = 0.5f;
    /// <summary>The meta an avatar carries its aura colour in (a Color): the glow takes it. Lane P's to set (see docs/look/GLOW.md).</summary>
    public const string AuraColorMeta = "aura_color";
    /// <summary>The meta on each glow's root node naming the effect it draws.</summary>
    public const string GlowMeta = "look_glow";
    /// <summary>What the estimate's rays hit: the world layer only, so the hidden parts on layer 5 (drawn: false), the avatars and the water never count as cover.</summary>
    public const uint LightMask = RoomBuilder.WorldLayer;

    private sealed class Glow
    {
        public string Id = "";
        public Node3D Root = null!;
        public OmniLight3D Light = null!;
        public MeshInstance3D Halo = null!;
        public MeshInstance3D? Core;
        public Node3D? Follow;
        public float Lift;
        public Vector3 Center;
        public float RadiusM;
        public float Intensity;
        public float Phase;
    }

    private readonly Dictionary<string, Glow> _glows = new();
    private readonly List<Glow> _glowOrder = new();
    private double _glowClock;
    private GradientTexture2D? _haloTexture;
    private RayCast3D? _probe;

    /// <summary>How many glows the look is drawing.</summary>
    public int GlowCount => _glows.Count;

    /// <summary>Whether a glow with this effect id is drawn.</summary>
    public bool HasGlow(string effectId) => effectId != null && _glows.ContainsKey(effectId);

    /// <summary>A glow's light, for tests and the review harness; null for an unknown id.</summary>
    public OmniLight3D? GlowLight(string effectId) => effectId != null && _glows.TryGetValue(effectId, out var glow) ? glow.Light : null;

    /// <summary>A glow's root node (its light, halo and, for a wisp, its core), for tests and the review harness; null for an unknown id.</summary>
    public Node3D? GlowRoot(string effectId) => effectId != null && _glows.TryGetValue(effectId, out var glow) ? glow.Root : null;

    /// <summary>
    /// Start the Gubble's glow, a real light: an omni light reaching radiusM, as bright as intensity (0.05 to 2.0) says. With follow,
    /// a soft halo on that body that moves with it; without, a small wisp floating just above center with a gentle bob. Its colour
    /// is the aura colour of the body (or, for a wisp, of the companion) where one is set (AuraColorMeta), else a warm white-gold.
    /// An id already in use is replaced. Returns false, and draws nothing, when the look already draws MaxGlows other glows or the
    /// call is unusable (an empty id, a number that is not finite, a follow node that is gone or not in the scene).
    /// A halo never casts a shadow (its light sits inside the body); a wisp casts one only while the shadow budget has a slot.
    /// </summary>
    public bool StartGlow(string effectId, Node3D? follow, Vector3 center, float radiusM, float intensity)
    {
        if (string.IsNullOrEmpty(effectId) || effectId.Length > 128 || !float.IsFinite(radiusM) || !float.IsFinite(intensity)) return false;
        if (follow != null && (!IsInstanceValid(follow) || follow.IsQueuedForDeletion() || !follow.IsInsideTree())) return false;
        if (follow == null && !(float.IsFinite(center.X) && float.IsFinite(center.Y) && float.IsFinite(center.Z))) return false;
        if (!_glows.ContainsKey(effectId) && _glows.Count >= GlowLook.MaxGlows) return false;
        FreeGlow(effectId);
        var glow = BuildGlow(effectId, follow, center, Mathf.Clamp(radiusM, GlowLook.MinRadiusM, GlowLook.MaxRadiusM), Mathf.Clamp(intensity, GlowLook.MinIntensity, GlowLook.MaxIntensity));
        _glows[effectId] = glow;
        _glowOrder.Add(glow);
        AssignGlowShadows();
        return true;
    }

    /// <summary>End a glow and free everything it drew. An unknown id is a no-op.</summary>
    public void StopGlow(string effectId)
    {
        if (effectId == null || !FreeGlow(effectId)) return;
        AssignGlowShadows();
    }

    /// <summary>End every glow.</summary>
    public void StopAllGlows()
    {
        for (var i = _glowOrder.Count - 1; i >= 0; i--) FreeGlow(_glowOrder[i].Id);
    }

    private bool FreeGlow(string effectId)
    {
        if (!_glows.Remove(effectId, out var glow)) return false;
        _glowOrder.Remove(glow);
        if (IsInstanceValid(glow.Root)) glow.Root.Free();
        return true;
    }

    private Glow BuildGlow(string id, Node3D? follow, Vector3 center, float radiusM, float intensity)
    {
        var body = follow as SmallPlayerController;
        var color = GlowLook.LightColor(AuraOf(follow ?? CompanionBody()));
        var glow = new Glow
        {
            Id = id, Follow = follow, RadiusM = radiusM, Intensity = intensity,
            Lift = body != null ? body.BodyHeightM * GlowLook.FollowHeightFraction : 0f,
            Center = center + Vector3.Up * GlowLook.WispLiftM,
            Phase = Seed(id) / 100f * Mathf.Tau,
        };
        // The glow's nodes stand in world space (top level), so they never inherit the look's transform and need no tree to be placed.
        glow.Root = new Node3D { Name = "Glow_" + RoomBuilder.NodeName(id), TopLevel = true };
        glow.Root.SetMeta(GlowMeta, id);
        glow.Light = new OmniLight3D
        {
            Name = "Light", OmniRange = radiusM, OmniAttenuation = GlowLook.Attenuation, LightEnergy = GlowLook.Energy(intensity),
            LightColor = color, LightSize = GlowLook.LightSizeM, LightSpecular = GlowLook.Specular, LightBakeMode = Light3D.BakeMode.Dynamic,
            ShadowEnabled = false,
        };
        if (Preset != null)
        {
            var s = Preset.Tuning.Shadows;
            glow.Light.ShadowBlur = s.BlurBase + s.BlurPerSoftness * Preset.ShadowSoftness;
            glow.Light.ShadowBias = s.LampBias;
            glow.Light.ShadowNormalBias = s.LampNormalBias;
        }
        glow.Root.AddChild(glow.Light);
        var haloSize = follow == null ? GlowLook.WispHaloM : body != null ? body.BodyHeightM * GlowLook.HaloBodyHeights : GlowLook.HaloDefaultM;
        glow.Halo = new MeshInstance3D
        {
            Name = "Halo", Mesh = new QuadMesh { Size = new Vector2(haloSize, haloSize) }, MaterialOverride = HaloMaterial(id, color, intensity),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        };
        glow.Root.AddChild(glow.Halo);
        if (follow == null)
        {
            // The wisp's own small bright heart: emissive, so the bloom catches it, and no shadow or GI of its own.
            glow.Core = new MeshInstance3D
            {
                Name = "Core", Mesh = new SphereMesh { Radius = GlowLook.WispCoreRadiusM, Height = GlowLook.WispCoreRadiusM * 2f, RadialSegments = 16, Rings = 8 },
                MaterialOverride = new StandardMaterial3D
                {
                    ResourceName = "glow core", AlbedoColor = new Color(0f, 0f, 0f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = GlowLook.WispCoreEnergy,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            glow.Root.AddChild(glow.Core);
        }
        glow.Root.Position = GlowPosition(glow, follow != null);
        AddChild(glow.Root);
        return glow;
    }

    /// <summary>A soft, painterly halo: a camera-facing disc in the glow's colour, added to what is behind it, with a faint brighter rim.</summary>
    private StandardMaterial3D HaloMaterial(string id, Color color, float intensity) => new()
    {
        ResourceName = "glow halo", ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true,
        AlbedoTexture = HaloTexture(), AlbedoColor = new Color(color.R, color.G, color.B, GlowLook.HaloAlpha(intensity)),
        ProximityFadeEnabled = true, ProximityFadeDistance = GlowLook.HaloProximityFadeM,
        DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha,
        DistanceFadeMinDistance = GlowLook.HaloFadeNearM, DistanceFadeMaxDistance = GlowLook.HaloFadeFullM,
    };

    private GradientTexture2D HaloTexture()
    {
        if (_haloTexture != null) return _haloTexture;
        var gradient = new Gradient
        {
            Offsets = new[] { 0f, 0.42f, 0.58f, 0.78f, 1f },
            Colors = new[] { new Color(1f, 1f, 1f, 0.75f), new Color(1f, 1f, 1f, 0.34f), new Color(1f, 1f, 1f, 0.4f), new Color(1f, 1f, 1f, 0.1f), new Color(1f, 1f, 1f, 0f) },
        };
        _haloTexture = new GradientTexture2D
        {
            Gradient = gradient, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 128, Height = 128,
        };
        return _haloTexture;
    }

    private static Color? AuraOf(Node3D? body)
    {
        if (body == null || !IsInstanceValid(body) || !body.HasMeta(AuraColorMeta)) return null;
        var value = body.GetMeta(AuraColorMeta);
        return value.VariantType == Variant.Type.Color ? value.AsColor() : null;
    }

    /// <summary>Where a glow's light is now: on its body (interpolated for drawing), or floating above its point (bobbing, when drawing).</summary>
    private Vector3 GlowPosition(Glow glow, bool drawing)
    {
        if (glow.Follow != null)
        {
            if (!IsInstanceValid(glow.Follow) || !glow.Follow.IsInsideTree()) return glow.Root.Position;
            var origin = drawing ? glow.Follow.GetGlobalTransformInterpolated().Origin : glow.Follow.GlobalPosition;
            return origin + Vector3.Up * glow.Lift;
        }
        if (!drawing) return glow.Center;
        return glow.Center + Vector3.Up * (GlowLook.BobAmplitudeM * Mathf.Sin((float)(_glowClock * Mathf.Tau / GlowLook.BobPeriodS) + glow.Phase));
    }

    private void UpdateGlows(double delta)
    {
        if (_glowOrder.Count == 0) return;
        _glowClock += delta;
        for (var i = _glowOrder.Count - 1; i >= 0; i--)
        {
            var glow = _glowOrder[i];
            // A halo whose body is gone goes with it.
            if (glow.Follow != null && !IsInstanceValid(glow.Follow)) { StopGlow(glow.Id); continue; }
            glow.Root.Position = GlowPosition(glow, drawing: true);
            var breath = 1f + GlowLook.BreathAmount * Mathf.Sin((float)(_glowClock * Mathf.Tau / GlowLook.BreathPeriodS) + glow.Phase);
            glow.Halo.Scale = new Vector3(breath, breath, breath);
        }
    }

    /// <summary>The shadow slots the preset's budget has left after the sun and the room's windows and lamps (counted whether lit or not).</summary>
    public int FreeShadowSlots()
    {
        if (Preset == null || !Preset.ShadowsEnabled) return 0;
        var used = Key != null && Key.ShadowEnabled && Key.Visible ? 1 : 0;
        foreach (var light in _roomLights) if (light.ShadowEnabled) used++;
        return Math.Max(0, Preset.MaxShadowedLights - used);
    }

    /// <summary>The oldest wisps take the free shadow slots, at most MaxShadowedGlows; a halo never does.</summary>
    private void AssignGlowShadows()
    {
        var slots = Math.Min(FreeShadowSlots(), GlowLook.MaxShadowedGlows);
        foreach (var glow in _glowOrder)
        {
            var shadowed = glow.Follow == null && slots > 0;
            glow.Light.ShadowEnabled = shadowed;
            if (shadowed) slots--;
        }
    }

    // ---------- the light-level estimate ----------

    /// <summary>
    /// How lit a spot is, from 0 (dark) to 1 (bright): an estimate for the game (the dusk moment, the smart ask), not the renderer. It
    /// counts the sun or moon by the look's own clock and the room's site (one ray toward it), the sky overhead (three rays: up and
    /// two tilted), each on the world layer only, and the room's lamps, window fills and glows within their range (no rays). Bounce
    /// light (GI) and leaves that do not collide are not counted. Deterministic for the same scene and clock, and allocation-free after
    /// the first call. Compare with DarkThreshold.
    /// </summary>
    public float LightLevelAt(Vector3 position) => EstimateLightAt(position).Level;

    /// <summary>What LightLevelAt counts at a spot, term by term.</summary>
    public LightEstimate EstimateLightAt(Vector3 position)
    {
        float key = 0f, sky = 0f, bounce = 0f, lamps = 0f, glows = 0f, visibility = 0f;
        var keySeen = false;
        var inTree = IsInsideTree();
        var probe = inTree ? Probe() : null;
        var origin = position + Vector3.Up * GlowLook.RayLiftM;
        if (Moment != null && Key != null)
        {
            if (Key.Visible && Key.LightEnergy > 0f)
            {
                var toKey = (inTree ? Key.GlobalBasis.Z : Key.Basis.Z).Normalized();
                if (toKey.Y > 1e-3f)
                {
                    var open = Key.LightEnergy * ColorGrade.Luma(Key.LightColor) * (GlowLook.KeyFloor + (1f - GlowLook.KeyFloor) * toKey.Y);
                    bounce += open;
                    if (Clear(probe, origin, toKey)) { keySeen = true; key = open; }
                }
            }
            var skyEnergy = Moment.AmbientEnergy * ColorGrade.Luma(Moment.AmbientColor) * OpenLandLight.AmbientScale;
            if (skyEnergy > 0f)
            {
                var side = 0.5f * (1f - GlowLook.ZenithWeight);
                visibility = (Clear(probe, origin, Vector3.Up) ? GlowLook.ZenithWeight : 0f)
                    + (Clear(probe, origin, GlowLook.TiltA) ? side : 0f) + (Clear(probe, origin, GlowLook.TiltB) ? side : 0f);
                sky = skyEnergy * visibility;
                bounce += skyEnergy;
            }
            bounce *= GlowLook.BounceFraction;
        }
        // The room's lights stand in the look's own space.
        var local = inTree ? ToLocal(position) : position;
        foreach (var lamp in _lamps)
            if (lamp.Visible && lamp is OmniLight3D omni)
                lamps += GlowLook.PointLight(omni.LightEnergy, omni.LightColor, omni.OmniRange, omni.OmniAttenuation, omni.Position.DistanceTo(local));
        foreach (var fill in _skyFills)
            if (fill.Visible) lamps += SpotAt(fill, local);
        if (!OpenLand) lamps *= 1f + GlowLook.IndoorBounce;
        foreach (var glow in _glowOrder)
            glows += GlowLook.PointLight(glow.Light.LightEnergy, glow.Light.LightColor, glow.RadiusM, glow.Light.OmniAttenuation, GlowPosition(glow, drawing: false).DistanceTo(position));
        return new LightEstimate(key, sky, bounce, lamps, glows, visibility, keySeen);
    }

    /// <summary>A window's sky fill at a spot: the point-light falloff inside Godot's spot cone (1 - rim^angle_attenuation).</summary>
    private static float SpotAt(SpotLight3D spot, Vector3 local)
    {
        var offset = local - spot.Position;
        var distance = offset.Length();
        if (distance >= spot.SpotRange || distance < 1e-5f) return 0f;
        var cutoff = Mathf.Cos(Mathf.DegToRad(spot.SpotAngle));
        var c = (offset / distance).Dot(-spot.Basis.Z);
        if (c <= cutoff) return 0f;
        var rim = Mathf.Max(1e-4f, (1f - c) / (1f - cutoff));
        var cone = 1f - Mathf.Pow(rim, spot.SpotAngleAttenuation);
        return cone * GlowLook.PointLight(spot.LightEnergy, spot.LightColor, spot.SpotRange, spot.SpotAttenuation, distance);
    }

    /// <summary>One ray the estimate reuses (no allocation per call); made on first use, so a look that is never asked has none.</summary>
    private RayCast3D Probe()
    {
        if (_probe != null && IsInstanceValid(_probe)) return _probe;
        _probe = new RayCast3D
        {
            Name = "LightProbe", Enabled = false, TopLevel = true, CollisionMask = LightMask, CollideWithAreas = false, CollideWithBodies = true,
            HitFromInside = false, HitBackFaces = true,
        };
        AddChild(_probe);
        return _probe;
    }

    /// <summary>Whether the sky (or the sun) is open from a point along a direction. Without a scene to cast in, it is.</summary>
    private static bool Clear(RayCast3D? probe, Vector3 origin, Vector3 direction)
    {
        if (probe == null) return true;
        probe.Position = origin;
        probe.TargetPosition = direction * GlowLook.RayLengthM;
        probe.ForceRaycastUpdate();
        return !probe.IsColliding();
    }
}
