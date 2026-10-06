using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using EnFractal.Native.Room;

namespace EnFractal.Native.Look;

/// <summary>Depth-of-field distances for one camera, in metres from the camera.</summary>
public readonly record struct DepthOfField(bool FarEnabled, float FarDistance, float FarTransition, bool NearEnabled, float NearDistance, float NearTransition, float Amount);

/// <summary>
/// Applies a style preset to a room (Track L, L2): environment and tone mapping, a warm key against a cool
/// ambient, the room's captured lights, VoxelGI baked to the room bounds, soft shadows, SSAO, restrained
/// glow, depth of field that keeps the player's reach crisp, and a colour grade driven by the preset's
/// palette, the hour and the season. Visual only: it never changes room data, collision or protection.
/// The renderer itself is a project setting; a mismatch with the preset is reported, not fixed here.
/// </summary>
public partial class LookDirector : Node3D
{
    /// <summary>Visual layer for the room shell. The diorama key casts no shadows from it.</summary>
    public const uint ShellVisualLayer = 1u << 1;
    /// <summary>Where the eye camera focuses: just beyond the player's reach.</summary>
    public const float EyeFocusM = 0.3f;

    public StylePreset Preset { get; private set; } = null!;
    public int RoomLightCount { get; private set; }
    public string RendererNote { get; private set; } = "";
    public string GiNote { get; private set; } = "";
    public LookMoment Moment { get; private set; } = null!;
    public bool Dressed { get; private set; }
    public VoxelGI? Gi { get; private set; }
    public Godot.Environment Environment { get; private set; } = null!;
    public DirectionalLight3D Key { get; private set; } = null!;
    /// <summary>Grain and vignette (Forward+ compositor effect), when the preset asks for them.</summary>
    public LookPostEffect? Post { get; private set; }
    /// <summary>What depth of field focuses on when the preset says "player". RoomWorld's Player is found by name if this is not set.</summary>
    public Node3D? FocusTarget { get; set; }

    private RoomData _room = null!;
    private float? _pinnedHour;
    private int? _pinnedDay;
    private double _clockTimer;
    private readonly Dictionary<ulong, CameraAttributesPractical> _attributes = new();
    private readonly HashSet<ulong> _framed = new();
    private readonly List<Light3D> _roomLights = new();
    /// <summary>How much brighter room lamps glow at full night than in full daylight.</summary>
    public const float LampNightBoost = 1.5f;

    public void Apply(StylePreset preset, RoomData room)
    {
        Preset = preset;
        _room = room;
        MaterialLibrary.Configure(preset);
        Environment = BuildEnvironment(preset);
        var world = new WorldEnvironment { Name = "Environment", Environment = Environment };
        if (preset.Grain > 0f || preset.Vignette > 0f)
        {
            Post = new LookPostEffect { Grain = preset.Grain, Vignette = preset.Vignette };
            world.Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { Post } };
        }
        AddChild(world);
        BuildKey(preset, room);
        BuildRoomLights(preset, room);
        var current = ProjectSettings.GetSetting("rendering/renderer/rendering_method").AsString();
        if (current != preset.RendererMethod)
        {
            RendererNote = $"preset {preset.PresetId} is designed for {preset.RendererMethod}; project renders with {current}";
            GD.Print("LOOK: " + RendererNote);
        }
        if (preset.KuwaharaEnabled || preset.OutlineEnabled)
            GD.Print("LOOK: the preset enables kuwahara or outline post effects, which this runtime does not implement yet");
        ApplyMoment();
        // RoomWorld builds the room after applying the look; dress it once it is in the tree.
        CallDeferred(MethodName.DressSibling);
    }

    // ---------- environment ----------

    private static Godot.Environment BuildEnvironment(StylePreset preset)
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = preset.Background,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
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
            SsaoEnabled = preset.AoEnabled,
            SsaoRadius = preset.AoRadiusM,
            SsaoIntensity = preset.AoIntensity,
            SsaoPower = 1.4f,
            SsaoDetail = 0.6f,
            SsaoHorizon = 0.06f,
            SsaoSharpness = 0.98f,
            SsaoLightAffect = 0.15f,
            SsaoAOChannelAffect = 0.5f,
            SsilEnabled = preset.SsilEnabled,
            SsilRadius = preset.SsilRadiusM,
            SsilIntensity = preset.SsilIntensity,
            SsilSharpness = 0.98f,
            GlowEnabled = preset.GlowEnabled,
            GlowIntensity = preset.GlowIntensity,
            GlowStrength = 1.0f,
            GlowBloom = preset.GlowBloom,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
            GlowHdrThreshold = 0.9f,
            AdjustmentEnabled = true,
        };
        // Wide, soft glow levels; the tight levels make edges sparkle.
        for (var level = 0; level < 7; level++) environment.SetGlowLevel(level, level is >= 2 and <= 4 ? 1f : 0f);
        return environment;
    }

    // ---------- lights ----------

    private void BuildKey(StylePreset preset, RoomData room)
    {
        var diagonal = room.Bounds.Size.Length();
        var diorama = preset.KeyMode == "diorama";
        var softness = preset.ShadowSoftness;
        Key = new DirectionalLight3D
        {
            Name = "Key",
            ShadowEnabled = preset.KeyCastsShadows && preset.ShadowsEnabled,
            LightAngularDistance = 0.5f + 3.5f * softness,
            ShadowBlur = 1f + softness,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            DirectionalShadowMaxDistance = Mathf.Max(6f, diagonal * 1.6f),
            DirectionalShadowBlendSplits = true,
            ShadowBias = 0.03f,
            ShadowNormalBias = 1.0f,
            LightBakeMode = Light3D.BakeMode.Dynamic,
        };
        // In diorama mode the key lights the room as if the ceiling were lifted off: the shell casts no key
        // shadows, and walls and ceilings stay out of the VoxelGI bake (see Dress), so the bounce follows.
        if (diorama) Key.ShadowCasterMask = 0xFFFFFu & ~ShellVisualLayer;
        AddChild(Key);
    }

    private void BuildRoomLights(StylePreset preset, RoomData room)
    {
        var diagonal = room.Bounds.Size.Length();
        var shadowBudget = Math.Max(0, preset.MaxShadowedLights - (Key.ShadowEnabled ? 1 : 0));
        foreach (var hint in room.LightHints.Where(h => h.PositionM != null))
        {
            var energy = hint.RelativeIntensity * preset.RoomLightEnergyScale * preset.HonorRoomLights;
            if (energy <= 0) continue;
            Light3D light;
            if (hint.Kind is "ceiling_lamp" or "lamp" or "screen")
                light = new OmniLight3D { OmniRange = diagonal, OmniAttenuation = 1.2f };
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
            light.ShadowEnabled = preset.ShadowsEnabled && shadowBudget-- > 0;
            light.LightSize = 0.05f + 0.25f * preset.ShadowSoftness;
            light.ShadowBlur = 1f + preset.ShadowSoftness;
            light.ShadowBias = 0.03f;
            light.ShadowNormalBias = 1.0f;
            light.SetMeta("light_hint_id", hint.Id);
            light.SetMeta("base_energy", energy);
            AddChild(light);
            _roomLights.Add(light);
            RoomLightCount++;
        }
    }

    // ---------- time of day and season ----------

    /// <summary>Pin the look to an hour and a day of the year (review captures, previews). Unpinned, the preset decides.</summary>
    public void SetClock(float hour, int dayOfYear)
    {
        _pinnedHour = hour;
        _pinnedDay = Math.Clamp(dayOfYear, 1, 366);
        ApplyMoment();
    }

    public void ReleaseClock()
    {
        _pinnedHour = null;
        _pinnedDay = null;
        ApplyMoment();
    }

    private void ApplyMoment()
    {
        var hour = _pinnedHour ?? (Preset.FollowClock ? LookClock.NowHour() : Preset.DefaultHour);
        var day = _pinnedDay ?? (Preset.FollowCalendar ? LookClock.TodayDayOfYear() : 196);
        Moment = LookClock.At(Preset, hour, day);
        Key.LightColor = Moment.KeyColor;
        Key.LightEnergy = Moment.KeyEnergy;
        Key.RotationDegrees = new Vector3(-Moment.KeyElevationDeg, Moment.KeyAzimuthDeg, 0);
        foreach (var lamp in _roomLights)
            lamp.LightEnergy = (float)lamp.GetMeta("base_energy").AsDouble() * (1f + LampNightBoost * (1f - Moment.Daylight));
        Environment.AmbientLightColor = Moment.AmbientColor;
        Environment.AmbientLightEnergy = Moment.AmbientEnergy;
        Environment.AdjustmentColorCorrection = ColorGrade.LutTexture(GradeParams.For(Preset, Moment));
    }

    // ---------- dressing the built room ----------

    private void DressSibling()
    {
        if (Dressed || !IsInsideTree()) return;
        if (GetParent()?.GetNodeOrNull<Node3D>("Room") is { } room) Dress(room);
    }

    /// <summary>
    /// Finish the look on a built room: per-instance paint seeds, softened box edges, the shell on its own
    /// visual layer, GI modes and the VoxelGI bake. Idempotent. GI modes: movable objects are dynamic,
    /// fixed ones static; in diorama mode only floors join the bake from the shell, because walls and
    /// ceilings would block the lifted-lid key in the voxels (they still receive GI).
    /// </summary>
    public void Dress(Node3D room)
    {
        if (Dressed) return;
        Dressed = true;
        var diorama = Preset.KeyMode == "diorama";
        foreach (var mesh in room.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            var owner = OwnerEntity(mesh);
            var entity = owner?.HasMeta("entity_id") == true ? owner.GetMeta("entity_id").AsString() : mesh.Name.ToString();
            mesh.SetInstanceShaderParameter("paint_seed", Seed(entity));
            if (mesh.Mesh is BoxMesh box)
            {
                mesh.SetInstanceShaderParameter("box_edges", 1f);
                mesh.SetInstanceShaderParameter("box_half_extents", box.Size * 0.5f);
            }
            var isShell = owner?.HasMeta("surface_role") == true;
            if (isShell && diorama) mesh.Layers = ShellVisualLayer;
            var movable = owner?.HasMeta("movable") == true && owner.GetMeta("movable").AsBool();
            var surface = isShell ? owner!.GetMeta("surface_role").AsString() : "";
            mesh.GIMode = isShell
                ? (diorama && surface != "floor" ? GeometryInstance3D.GIModeEnum.Disabled : GeometryInstance3D.GIModeEnum.Static)
                : (movable ? GeometryInstance3D.GIModeEnum.Dynamic : GeometryInstance3D.GIModeEnum.Static);
        }
        if (Preset.GiMode == "voxelgi") BakeVoxelGi(room);
        else GiNote = $"gi mode {Preset.GiMode}: no global illumination node";
        if (GiNote.Length > 0) GD.Print("LOOK: " + GiNote);
    }

    private static Node3D? OwnerEntity(Node node)
    {
        for (var current = node.GetParent(); current != null; current = current.GetParent())
            if (current is Node3D spatial && spatial.HasMeta("entity_id")) return spatial;
        return null;
    }

    /// <summary>A stable per-entity seed in [0, 100) so two identical props are painted differently.</summary>
    public static float Seed(string entityId)
    {
        var hash = 2166136261u;
        foreach (var c in entityId) hash = (hash ^ c) * 16777619u;
        return hash / (float)uint.MaxValue * 100f;
    }

    /// <summary>The VoxelGI volume for a room: its bounds plus a margin so the shell's thickness is inside.</summary>
    public static Aabb GiVolume(RoomData room)
    {
        const float margin = 0.3f;
        return room.Bounds.Grow(margin);
    }

    private void BakeVoxelGi(Node3D room)
    {
        if (RenderingServer.GetRenderingDevice() == null)
        {
            GiNote = $"VoxelGI needs a GPU rendering device (Forward+ or Mobile); none here (display {DisplayServer.GetName()}, driver '{RenderingServer.GetCurrentRenderingDriverName()}'), so no GI";
            return;
        }
        var volume = GiVolume(_room);
        Gi = new VoxelGI { Name = "RoomGI", Size = volume.Size, Position = volume.GetCenter(), Subdiv = VoxelGI.SubdivEnum.Subdiv128 };
        AddChild(Gi);
        // VoxelGI voxelizes BaseMaterial3D albedo only; bake with flat proxies of the painterly colours.
        var swaps = new List<(GeometryInstance3D Mesh, Material? Original)>();
        foreach (var mesh in room.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            if (mesh.GIMode == GeometryInstance3D.GIModeEnum.Static && MaterialLibrary.BakeAlbedo(mesh.MaterialOverride) is { } albedo)
            {
                swaps.Add((mesh, mesh.MaterialOverride));
                mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = albedo };
            }
        var clock = Stopwatch.StartNew();
        try { Gi.Bake(room); }
        finally { foreach (var (mesh, original) in swaps) mesh.MaterialOverride = original; }
        var data = Gi.Data;
        if (data != null)
        {
            data.Energy = Preset.GiEnergy;
            data.Propagation = Mathf.Clamp(Preset.GiBounceFeedback, 0f, 1f);
            data.UseTwoBounces = Preset.GiBounceFeedback > 0.2f;
            // Inside the volume VoxelGI replaces the environment's ambient light. A diorama has its lid off,
            // so cones that leave the room see the sky (the ambient colour); a fixed-key room is closed.
            data.Interior = Preset.KeyMode != "diorama";
            data.NormalBias = 0f;
            data.Bias = 1.5f;
        }
        GiNote = string.Format(CultureInfo.InvariantCulture, "VoxelGI {0:0.0#} x {1:0.0#} x {2:0.0#} m at subdiv 128 baked in {3} ms",
            volume.Size.X, volume.Size.Y, volume.Size.Z, clock.ElapsedMilliseconds);
    }

    // ---------- depth of field ----------

    /// <summary>
    /// Depth of field for a camera focused at a distance. The in-focus band is the preset's focus_band_m
    /// around the focus; far blur ramps over a distance that grows with the focus distance; near blur covers
    /// what is closer than near_blur_distance_m (or the band). Tilt-shift narrows the band and strengthens
    /// the blur as the camera looks down on the room, which is what makes a high view read as a miniature.
    /// </summary>
    public static DepthOfField DepthOfFieldFor(StylePreset preset, float focusDistance, float lookingDown)
    {
        var d = Mathf.Max(focusDistance, 0.05f);
        var tilt = preset.TiltShiftEnabled ? preset.TiltShiftStrength * Mathf.Clamp(lookingDown * 1.5f, 0f, 1f) : 0f;
        var halfBand = 0.5f * preset.FocusBandM * (1f - 0.4f * tilt);
        var farDistance = d + halfBand;
        var farTransition = (0.25f + 0.35f * d) * (1.3f - preset.FarBlur);
        var nearDistance = Mathf.Max(preset.NearBlurDistanceM, d - halfBand);
        var nearTransition = nearDistance * (0.9f - 0.5f * preset.NearBlur);
        var amount = (0.02f + 0.10f * Mathf.Max(preset.FarBlur, preset.NearBlur)) * (1f + tilt);
        return new DepthOfField(preset.DofEnabled && preset.FarBlur > 0f, farDistance, Mathf.Max(farTransition, 0.05f),
            preset.DofEnabled && preset.NearBlur > 0f && nearDistance > 0.01f, nearDistance, Mathf.Max(nearTransition, 0.01f), amount);
    }

    /// <summary>Give a camera the preset's depth of field, focused on a world point. Used by the review harness and previews.</summary>
    public void FrameCamera(Camera3D camera, Vector3 focusPoint)
    {
        _framed.Add(camera.GetInstanceId());
        Focus(camera, focusPoint);
    }

    private void Focus(Camera3D camera, Vector3 focusPoint)
    {
        if (!_attributes.TryGetValue(camera.GetInstanceId(), out var attributes))
        {
            attributes = new CameraAttributesPractical();
            _attributes[camera.GetInstanceId()] = attributes;
        }
        var forward = -camera.GlobalBasis.Z;
        var distance = (focusPoint - camera.GlobalPosition).Dot(forward);
        var dof = DepthOfFieldFor(Preset, distance, Mathf.Max(0f, -forward.Y));
        attributes.DofBlurFarEnabled = dof.FarEnabled;
        attributes.DofBlurFarDistance = dof.FarDistance;
        attributes.DofBlurFarTransition = dof.FarTransition;
        attributes.DofBlurNearEnabled = dof.NearEnabled;
        attributes.DofBlurNearDistance = dof.NearDistance;
        attributes.DofBlurNearTransition = dof.NearTransition;
        attributes.DofBlurAmount = dof.Amount;
        if (camera.Attributes != attributes) camera.Attributes = attributes;
    }

    public override void _ExitTree() => Post?.Release();

    public override void _Process(double delta)
    {
        if (Preset == null) return;
        if (_pinnedHour == null && Preset.FollowClock)
        {
            _clockTimer += delta;
            if (_clockTimer > 60.0) { _clockTimer = 0; ApplyMoment(); }
        }
        if (!Preset.DofEnabled) return;
        var camera = GetViewport()?.GetCamera3D();
        if (camera == null || _framed.Contains(camera.GetInstanceId())) return;
        var target = FocusTarget ?? GetParent()?.GetNodeOrNull<Node3D>("Player");
        Vector3 focus;
        if (Preset.DofFocus == "player" && target != null && IsInstanceValid(target))
        {
            var eye = target.IsAncestorOf(camera) && camera.GlobalPosition.DistanceTo(target.GlobalPosition) < 0.5f;
            focus = eye ? camera.GlobalPosition - camera.GlobalBasis.Z * EyeFocusM : target.GlobalPosition + Vector3.Up * 0.05f;
        }
        else focus = camera.GlobalPosition - camera.GlobalBasis.Z * Mathf.Max(EyeFocusM, Preset.FocusBandM);
        Focus(camera, focus);
    }

    /// <summary>A one-line description of what the look applied, for review reports.</summary>
    public string DescribeLook() => string.Format(CultureInfo.InvariantCulture,
        "{0}@{1} ({2}) sha256={3}; renderer={4}{5}; key={6} elevation {7:0.#} azimuth {8:0.#} energy {9:0.##}; hour {10:0.##} day {11} season {12}; {13}; ssao={14} ssil={15} glow={16} dof={17} tilt={18}; grain {19} vignette {20} post effect {21}",
        Preset.PresetId, Preset.PresetVersion, Preset.Status, Preset.Sha256, RenderingServer.GetCurrentRenderingMethod(),
        RendererNote.Length > 0 ? " (" + RendererNote + ")" : "", Preset.KeyMode, Moment.KeyElevationDeg, Moment.KeyAzimuthDeg, Moment.KeyEnergy,
        Moment.Hour, Moment.DayOfYear, Moment.Season, GiNote, Preset.AoEnabled, Preset.SsilEnabled, Preset.GlowEnabled, Preset.DofEnabled,
        Preset.TiltShiftEnabled ? Preset.TiltShiftStrength : 0f, Preset.Grain, Preset.Vignette,
        Post == null ? "off" : Post.Ran ? "ran" : Post.Error.Length > 0 ? "failed: " + Post.Error : "not run (no GPU device)");
}
