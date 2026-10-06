using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using EnFractal.Native.Room;

namespace EnFractal.Native.Look;

/// <summary>Depth-of-field distances for one camera, in metres from the camera.</summary>
public readonly record struct DepthOfField(bool FarEnabled, float FarDistance, float FarTransition, bool NearEnabled, float NearDistance, float NearTransition, float Amount);

/// <summary>
/// Applies a style preset to a room (Track L, L2): environment and tone mapping, a warm key against a cool
/// ambient, the room's captured lights, VoxelGI baked to the room bounds, soft shadows, SSAO, restrained
/// glow, depth of field that keeps the player's reach crisp, and a colour grade driven by the preset's
/// palette, the hour and the season. Visual only: it never changes room data, collision or protection.
///
/// It dresses every mesh that enters the tree under its parent (the room, later rebuilds, creations): paint
/// seeds and box edges for painterly materials, the shell's visual layer and GI modes for room entities, and
/// a VoxelGI (re)bake whenever new shell geometry arrives. RoomWorld may also call Dress(room) directly.
/// The renderer is a project setting; when the machine renders with something else, the look says so.
/// </summary>
public partial class LookDirector : Node3D
{
    /// <summary>Visual layer for the room shell. The diorama key casts no shadows from it.</summary>
    public const uint ShellVisualLayer = 1u << 1;
    /// <summary>Meta marking a VoxelGI bake stand-in material; none may remain after a bake.</summary>
    public const string BakeStandInMeta = "look_bake_stand_in";
    /// <summary>Meta marking a mesh the look has already dressed.</summary>
    public const string DressedMeta = "look_dressed";
    private const int LutCacheSize = 12;

    public StylePreset Preset { get; private set; } = null!;
    public int RoomLightCount { get; private set; }
    /// <summary>Empty when the machine renders with the renderer the preset is designed for; otherwise what is missing.</summary>
    public string RendererNote { get; private set; } = "";
    public string GiNote { get; private set; } = "";
    public LookMoment Moment { get; private set; } = null!;
    /// <summary>True once room geometry with a shell has been dressed (and baked, on a GPU).</summary>
    public bool Dressed { get; private set; }
    public VoxelGI? Gi { get; private set; }
    public Godot.Environment Environment { get; private set; } = null!;
    public DirectionalLight3D Key { get; private set; } = null!;
    /// <summary>Grain and vignette (Forward+ compositor effect), when the preset asks for them.</summary>
    public LookPostEffect? Post { get; private set; }
    /// <summary>Whether the running renderer supports depth of field; without it no camera attributes are set.</summary>
    public bool DofSupported { get; private set; }
    /// <summary>The player body depth of field focuses on. If unset, the parent's first SmallPlayerController that is not a companion.</summary>
    public Node3D? FocusTarget { get; set; }
    /// <summary>The companion body, for presets that focus on the companion. If unset, the parent's first CompanionAvatar.</summary>
    public Node3D? FocusCompanion { get; set; }
    /// <summary>Things that went wrong with the look but did not stop the room: shown in reports and the review harness.</summary>
    public IReadOnlyList<string> Warnings => _warnings;
    /// <summary>LUTs built so far (cache misses), for tests and reports.</summary>
    public int GradeBuilds { get; private set; }

    private RoomData _room = null!;
    private float? _pinnedHour;
    private int? _pinnedDay;
    private double _clockTimer;
    private int _framesSinceApply;
    private readonly Dictionary<ulong, CameraAttributesPractical> _attributes = new();
    private readonly HashSet<ulong> _framed = new();
    private readonly List<Light3D> _roomLights = new();
    private readonly List<string> _warnings = new();
    private readonly HashSet<string> _warned = new();
    private readonly List<MeshInstance3D> _pending = new();
    private bool _flushQueued;
    private Node3D? _bakeQueuedRoot;
    private readonly Dictionary<GradeParams, ImageTexture3D> _luts = new();
    private readonly LinkedList<GradeParams> _lutOrder = new();
    private GradeParams? _wantedGrade;
    private Task<byte[]>? _lutTask;
    private GradeParams? _lutTaskKey;

    public void Apply(StylePreset preset, RoomData room)
    {
        Preset = preset;
        _room = room;
        MaterialLibrary.Configure(preset);
        Environment = BuildEnvironment(preset);
        var world = new WorldEnvironment { Name = "Environment", Environment = Environment };
        if (preset.Grain > 0f || preset.Vignette > 0f)
        {
            Post = new LookPostEffect { Grain = preset.Grain, Vignette = preset.Vignette, Tuning = preset.Tuning.Post };
            world.Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { Post } };
        }
        AddChild(world);
        BuildKey(preset, room);
        BuildRoomLights(preset, room);
        var running = RenderingServer.GetCurrentRenderingMethod();
        DofSupported = running is "forward_plus" or "mobile";
        RendererNote = RendererNoteFor(preset.PresetId, preset.RendererMethod, running);
        if (RendererNote.Length > 0) Warn(RendererNote);
        if (preset.KuwaharaEnabled || preset.OutlineEnabled)
            Warn("the preset enables kuwahara or outline post effects, which this runtime does not implement yet");
        if (preset.DofEnabled && preset.DofFocus == "cursor")
            Warn("depth-of-field focus 'cursor' is not implemented yet; focusing a fixed distance ahead instead");
        ApplyMoment(synchronous: true);
        _framesSinceApply = 0;
        // Meshes already under the parent (a room built before the look) are dressed too.
        if (GetParent() is { } parent)
            foreach (var mesh in parent.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()) Queue(mesh);
    }

    /// <summary>What the room loses when the machine does not render with the renderer the preset is designed for.</summary>
    public static string RendererNoteFor(string presetId, string designedFor, string running)
    {
        if (running == designedFor) return "";
        var loss = running == "gl_compatibility"
            ? "; the Compatibility renderer has no VoxelGI bounce, depth of field, TAA or grain and vignette, so the room looks flatter and harder-edged"
            : "";
        return $"preset {presetId} is designed for {designedFor}; this machine renders with {running}{loss}";
    }

    private void Warn(string message)
    {
        if (!_warned.Add(message)) return;
        _warnings.Add(message);
        GD.Print("LOOK WARNING: " + message);
    }

    // ---------- environment ----------

    private static Godot.Environment BuildEnvironment(StylePreset preset)
    {
        var ssao = preset.Tuning.Ssao;
        var glow = preset.Tuning.Glow;
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
            SsaoPower = ssao.Power,
            SsaoDetail = ssao.Detail,
            SsaoHorizon = ssao.Horizon,
            SsaoSharpness = ssao.Sharpness,
            SsaoLightAffect = ssao.LightAffect,
            SsaoAOChannelAffect = ssao.AoChannelAffect,
            SsilEnabled = preset.SsilEnabled,
            SsilRadius = preset.SsilRadiusM,
            SsilIntensity = preset.SsilIntensity,
            SsilSharpness = ssao.Sharpness,
            GlowEnabled = preset.GlowEnabled,
            GlowIntensity = preset.GlowIntensity,
            GlowStrength = glow.Strength,
            GlowBloom = preset.GlowBloom,
            GlowBlendMode = glow.BlendMode switch
            {
                "additive" => Godot.Environment.GlowBlendModeEnum.Additive,
                "screen" => Godot.Environment.GlowBlendModeEnum.Screen,
                "replace" => Godot.Environment.GlowBlendModeEnum.Replace,
                "mix" => Godot.Environment.GlowBlendModeEnum.Mix,
                _ => Godot.Environment.GlowBlendModeEnum.Softlight,
            },
            GlowHdrThreshold = glow.HdrThreshold,
            AdjustmentEnabled = true,
        };
        for (var level = 0; level < 7; level++) environment.SetGlowLevel(level, glow.Levels[level]);
        return environment;
    }

    // ---------- lights ----------

    private void BuildKey(StylePreset preset, RoomData room)
    {
        var diagonal = room.Bounds.Size.Length();
        var diorama = preset.KeyMode == "diorama";
        var softness = preset.ShadowSoftness;
        var s = preset.Tuning.Shadows;
        Key = new DirectionalLight3D
        {
            Name = "Key",
            ShadowEnabled = preset.KeyCastsShadows && preset.ShadowsEnabled,
            LightAngularDistance = s.KeyAngularBaseDeg + s.KeyAngularPerSoftnessDeg * softness,
            ShadowBlur = s.BlurBase + s.BlurPerSoftness * softness,
            DirectionalShadowMode = s.KeySplits switch
            {
                1 => DirectionalLight3D.ShadowMode.Orthogonal,
                4 => DirectionalLight3D.ShadowMode.Parallel4Splits,
                _ => DirectionalLight3D.ShadowMode.Parallel2Splits,
            },
            DirectionalShadowMaxDistance = Mathf.Max(s.KeyMinDistanceM, diagonal * s.KeyDistancePerDiagonal),
            DirectionalShadowBlendSplits = true,
            ShadowBias = s.Bias,
            ShadowNormalBias = s.NormalBias,
            LightBakeMode = Light3D.BakeMode.Dynamic,
        };
        // In diorama mode the key lights the room as if the ceiling were lifted off: the shell casts no key
        // shadows, and walls and ceilings stay out of the VoxelGI bake (see DressMesh), so the bounce follows.
        if (diorama) Key.ShadowCasterMask = 0xFFFFFu & ~ShellVisualLayer;
        AddChild(Key);
    }

    private void BuildRoomLights(StylePreset preset, RoomData room)
    {
        var diagonal = room.Bounds.Size.Length();
        var s = preset.Tuning.Shadows;
        var shadowBudget = Math.Max(0, preset.MaxShadowedLights - (Key.ShadowEnabled ? 1 : 0));
        foreach (var hint in room.LightHints.Where(h => h.PositionM != null))
        {
            var energy = hint.RelativeIntensity * preset.RoomLightEnergyScale * preset.HonorRoomLights;
            if (energy <= 0) continue;
            Light3D light;
            if (hint.Kind is "ceiling_lamp" or "lamp" or "screen")
                light = new OmniLight3D { OmniRange = diagonal, OmniAttenuation = preset.Tuning.Lamps.Attenuation };
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
            light.LightSize = s.LampSizeBaseM + s.LampSizePerSoftnessM * preset.ShadowSoftness;
            light.ShadowBlur = s.BlurBase + s.BlurPerSoftness * preset.ShadowSoftness;
            light.ShadowBias = s.Bias;
            light.ShadowNormalBias = s.NormalBias;
            light.SetMeta("light_hint_id", hint.Id);
            light.SetMeta("base_energy", energy);
            AddChild(light);
            _roomLights.Add(light);
            RoomLightCount++;
        }
    }

    // ---------- time of day and season ----------

    /// <summary>Pin the look to an hour and a day of the year (review captures, previews). The grade is rebuilt before this returns.</summary>
    public void SetClock(float hour, int dayOfYear)
    {
        _pinnedHour = hour;
        _pinnedDay = Math.Clamp(dayOfYear, 1, 366);
        ApplyMoment(synchronous: true);
    }

    /// <summary>Pin the clock but build a missing grade LUT off the main thread; the old grade stays until it is ready.</summary>
    public void SetClockAsync(float hour, int dayOfYear)
    {
        _pinnedHour = hour;
        _pinnedDay = Math.Clamp(dayOfYear, 1, 366);
        ApplyMoment(synchronous: false);
    }

    public void ReleaseClock()
    {
        _pinnedHour = null;
        _pinnedDay = null;
        ApplyMoment(synchronous: true);
    }

    /// <summary>Whether the grade for the current moment is the one on screen (false while an off-thread LUT is building).</summary>
    public bool GradeCurrent => _wantedGrade != null && _luts.TryGetValue(_wantedGrade, out var lut) && Environment.AdjustmentColorCorrection == lut;

    private void ApplyMoment(bool synchronous)
    {
        var hour = _pinnedHour ?? (Preset.FollowClock ? LookClock.NowHour() : Preset.DefaultHour);
        var day = _pinnedDay ?? (Preset.FollowCalendar ? LookClock.TodayDayOfYear() : 196);
        Moment = LookClock.At(Preset, hour, day);
        Key.LightColor = Moment.KeyColor;
        Key.LightEnergy = Moment.KeyEnergy;
        Key.RotationDegrees = new Vector3(-Moment.KeyElevationDeg, Moment.KeyAzimuthDeg, 0);
        foreach (var lamp in _roomLights)
            lamp.LightEnergy = (float)lamp.GetMeta("base_energy").AsDouble() * (1f + Preset.Tuning.Lamps.NightBoost * (1f - Moment.Daylight));
        Environment.AmbientLightColor = Moment.AmbientColor;
        Environment.AmbientLightEnergy = Moment.AmbientEnergy;
        ApplyGrade(GradeParams.For(Preset, Moment).Quantized(), synchronous);
    }

    private void ApplyGrade(GradeParams grade, bool synchronous)
    {
        _wantedGrade = grade;
        if (_luts.TryGetValue(grade, out var cached))
        {
            Environment.AdjustmentColorCorrection = cached;
            _lutOrder.Remove(grade);
            _lutOrder.AddFirst(grade);
            return;
        }
        if (synchronous)
        {
            CacheLut(grade, ColorGrade.LutTexture(grade));
            Environment.AdjustmentColorCorrection = _luts[grade];
            return;
        }
        if (_lutTask != null && Equals(_lutTaskKey, grade)) return;
        _lutTaskKey = grade;
        _lutTask = Task.Run(() => ColorGrade.LutBytes(grade));
    }

    private void CacheLut(GradeParams grade, ImageTexture3D texture)
    {
        GradeBuilds++;
        _luts[grade] = texture;
        _lutOrder.AddFirst(grade);
        while (_lutOrder.Count > LutCacheSize)
        {
            _luts.Remove(_lutOrder.Last!.Value);
            _lutOrder.RemoveLast();
        }
    }

    private void CollectLut()
    {
        if (_lutTask is not { IsCompleted: true } task || _lutTaskKey is not { } key) return;
        _lutTask = null;
        _lutTaskKey = null;
        if (!task.IsCompletedSuccessfully) { Warn("the colour grade could not be built: " + task.Exception?.GetBaseException().Message); return; }
        if (!_luts.ContainsKey(key)) CacheLut(key, ColorGrade.LutTexture(task.Result, key.Tuning.LutSize));
        if (Equals(_wantedGrade, key)) Environment.AdjustmentColorCorrection = _luts[key];
        else if (_wantedGrade != null && !_luts.ContainsKey(_wantedGrade)) ApplyGrade(_wantedGrade, synchronous: false);
    }

    // ---------- dressing ----------

    public override void _EnterTree() => GetTree().NodeAdded += OnNodeAdded;

    public override void _ExitTree()
    {
        GetTree().NodeAdded -= OnNodeAdded;
        Post?.Release();
    }

    private void OnNodeAdded(Node node)
    {
        if (Preset != null && node is MeshInstance3D mesh) Queue(mesh);
    }

    private void Queue(MeshInstance3D mesh)
    {
        if (mesh.HasMeta(DressedMeta) || GetParent() is not { } parent || !parent.IsAncestorOf(mesh) || IsAncestorOf(mesh)) return;
        _pending.Add(mesh);
        if (_flushQueued) return;
        _flushQueued = true;
        CallDeferred(MethodName.FlushPending);
    }

    private void FlushPending()
    {
        _flushQueued = false;
        var meshes = _pending.Where(m => IsInstanceValid(m) && m.IsInsideTree()).Distinct().ToArray();
        _pending.Clear();
        Node3D? bakeRoot = null;
        foreach (var mesh in meshes)
            if (DressMesh(mesh)) bakeRoot ??= SubtreeRoot(mesh);
        if (bakeRoot != null) Bake(bakeRoot);
    }

    /// <summary>
    /// Dress a built room or any later subtree now: paint seeds and box edges for painterly materials, the
    /// shell's visual layer and GI modes for room entities, and a VoxelGI bake when it brings shell geometry.
    /// Meshes already dressed are skipped, so calling it again, or after the automatic pass, is harmless.
    /// </summary>
    public void Dress(Node3D root)
    {
        var bake = false;
        foreach (var mesh in root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Append(root as MeshInstance3D).OfType<MeshInstance3D>())
            bake |= DressMesh(mesh);
        if (bake) Bake(root);
    }

    /// <summary>Dress one mesh; returns true when it is shell geometry that belongs in the GI bake.</summary>
    private bool DressMesh(MeshInstance3D mesh)
    {
        if (mesh.HasMeta(DressedMeta)) return false;
        mesh.SetMeta(DressedMeta, true);
        var owner = OwnerEntity(mesh);
        if (owner == null) return false; // not room data (an avatar, a gizmo): left alone
        var diorama = Preset.KeyMode == "diorama";
        if (MaterialLibrary.IsPainterly(mesh.MaterialOverride))
        {
            mesh.SetInstanceShaderParameter("paint_seed", Seed(owner.GetMeta("entity_id").AsString()));
            if (mesh.Mesh is BoxMesh box)
            {
                mesh.SetInstanceShaderParameter("box_edges", 1f);
                mesh.SetInstanceShaderParameter("box_half_extents", box.Size * 0.5f);
            }
        }
        var isShell = owner.HasMeta("surface_role");
        if (isShell && diorama) mesh.Layers = ShellVisualLayer;
        var movable = owner.HasMeta("movable") && owner.GetMeta("movable").AsBool();
        var surface = isShell ? owner.GetMeta("surface_role").AsString() : "";
        mesh.GIMode = isShell
            ? (diorama && surface != "floor" ? GeometryInstance3D.GIModeEnum.Disabled : GeometryInstance3D.GIModeEnum.Static)
            : (movable ? GeometryInstance3D.GIModeEnum.Dynamic : GeometryInstance3D.GIModeEnum.Static);
        return isShell;
    }

    private Node3D? SubtreeRoot(Node node)
    {
        var parent = GetParent();
        for (var current = node; current != null; current = current.GetParent())
            if (current.GetParent() == parent) return current as Node3D;
        return null;
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
    public static Aabb GiVolume(RoomData room, GiTuning gi) => room.Bounds.Grow(gi.MarginM);

    private void Bake(Node3D root)
    {
        Dressed = true;
        if (Preset.GiMode != "voxelgi") GiNote = $"gi mode {Preset.GiMode}: no global illumination node";
        else BakeVoxelGi(root);
        if (GiNote.Length > 0) GD.Print("LOOK: " + GiNote);
    }

    /// <summary>
    /// Run a bake with flat stand-ins of the painterly colours on every mesh that has one (VoxelGI voxelizes
    /// BaseMaterial3D albedo only), and put every original material back afterwards, even when the bake or the
    /// swap itself throws.
    /// </summary>
    public static void WithBakeStandIns(IEnumerable<GeometryInstance3D> meshes, Action bake)
    {
        var swaps = new List<(GeometryInstance3D Mesh, Material? Original)>();
        try
        {
            foreach (var mesh in meshes)
                if (MaterialLibrary.BakeAlbedo(mesh.MaterialOverride) is { } albedo)
                {
                    var standIn = new StandardMaterial3D { AlbedoColor = albedo };
                    standIn.SetMeta(BakeStandInMeta, true);
                    swaps.Add((mesh, mesh.MaterialOverride));
                    mesh.MaterialOverride = standIn;
                }
            bake();
        }
        finally
        {
            foreach (var (mesh, original) in swaps) mesh.MaterialOverride = original;
        }
    }

    private void BakeVoxelGi(Node3D root)
    {
        if (RenderingServer.GetRenderingDevice() == null)
        {
            GiNote = $"VoxelGI needs a GPU rendering device (Forward+ or Mobile); none here (display {DisplayServer.GetName()}, driver '{RenderingServer.GetCurrentRenderingDriverName()}'), so no GI";
            return;
        }
        var tuning = Preset.Tuning.Gi;
        var volume = GiVolume(_room, tuning);
        if (Gi == null)
        {
            Gi = new VoxelGI { Name = "RoomGI" };
            AddChild(Gi);
        }
        Gi.Size = volume.Size;
        Gi.Position = volume.GetCenter();
        Gi.Subdiv = tuning.Subdiv switch
        {
            64 => VoxelGI.SubdivEnum.Subdiv64,
            256 => VoxelGI.SubdivEnum.Subdiv256,
            512 => VoxelGI.SubdivEnum.Subdiv512,
            _ => VoxelGI.SubdivEnum.Subdiv128,
        };
        var clock = Stopwatch.StartNew();
        var meshes = root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
            .Where(m => m.GIMode == GeometryInstance3D.GIModeEnum.Static);
        WithBakeStandIns(meshes, () => Gi.Bake(root));
        var data = Gi.Data;
        if (data != null)
        {
            data.Energy = Preset.GiEnergy;
            data.Propagation = Mathf.Clamp(Preset.GiBounceFeedback, 0f, 1f);
            data.UseTwoBounces = Preset.GiBounceFeedback > tuning.TwoBouncesAbove;
            // Inside the volume VoxelGI replaces the environment's ambient light. A diorama has its lid off,
            // so cones that leave the room see the sky (the ambient colour); a fixed-key room is closed.
            data.Interior = Preset.KeyMode != "diorama";
            data.NormalBias = tuning.NormalBias;
            data.Bias = tuning.Bias;
        }
        GiNote = string.Format(CultureInfo.InvariantCulture, "VoxelGI {0:0.0#} x {1:0.0#} x {2:0.0#} m at subdiv {3} baked in {4} ms",
            volume.Size.X, volume.Size.Y, volume.Size.Z, tuning.Subdiv, clock.ElapsedMilliseconds);
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
        var t = preset.Tuning.Dof;
        var d = Mathf.Max(focusDistance, 0.05f);
        var tilt = preset.TiltShiftEnabled ? preset.TiltShiftStrength * Mathf.Clamp(lookingDown * t.TiltPitchGain, 0f, 1f) : 0f;
        var halfBand = 0.5f * preset.FocusBandM * (1f - t.TiltBandNarrowing * tilt);
        var farDistance = d + halfBand;
        var farTransition = (t.FarTransitionBaseM + t.FarTransitionPerM * d) * (t.FarBlurReference - preset.FarBlur);
        var nearDistance = Mathf.Max(preset.NearBlurDistanceM, d - halfBand);
        var nearTransition = nearDistance * (t.NearTransitionBase - t.NearTransitionPerBlur * preset.NearBlur);
        var amount = (t.AmountBase + t.AmountPerBlur * Mathf.Max(preset.FarBlur, preset.NearBlur)) * (1f + tilt);
        return new DepthOfField(preset.DofEnabled && preset.FarBlur > 0f, farDistance, Mathf.Max(farTransition, 0.05f),
            preset.DofEnabled && preset.NearBlur > 0f && nearDistance > 0.01f, nearDistance, Mathf.Max(nearTransition, 0.01f), amount);
    }

    /// <summary>Give a camera the preset's depth of field, focused on a world point. Used by the review harness and previews.</summary>
    public void FrameCamera(Camera3D camera, Vector3 focusPoint)
    {
        _framed.Add(camera.GetInstanceId());
        Focus(camera, focusPoint);
    }

    /// <summary>The depth-of-field attributes the look gave a camera, if any.</summary>
    public CameraAttributesPractical? AttributesFor(Camera3D camera) => _attributes.TryGetValue(camera.GetInstanceId(), out var a) ? a : null;

    private void Focus(Camera3D camera, Vector3 focusPoint)
    {
        if (!DofSupported) return;
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

    /// <summary>
    /// Where a camera should focus under the preset's focus mode. "player": a third-person camera focuses on
    /// the player's body (body_focus_height_fraction up it); the player's own EyeCamera focuses
    /// eye_focus_body_heights body heights ahead, just past its reach. "companion": the companion's body.
    /// "fixed" (and "cursor", not implemented yet): focus_band_m ahead of the camera.
    /// </summary>
    public Vector3 FocusPointFor(Camera3D camera)
    {
        var forward = -camera.GlobalBasis.Z;
        var dof = Preset.Tuning.Dof;
        var body = Preset.DofFocus switch
        {
            "player" => FocusTarget ?? GetParent()?.GetChildren().OfType<SmallPlayerController>().FirstOrDefault(c => c is not CompanionAvatar),
            "companion" => FocusCompanion ?? GetParent()?.GetChildren().OfType<CompanionAvatar>().FirstOrDefault(),
            _ => null,
        };
        if (body != null && IsInstanceValid(body))
        {
            if (body is SmallPlayerController controller)
            {
                if (camera == controller.EyeCamera) return camera.GlobalPosition + forward * (dof.EyeFocusBodyHeights * controller.BodyHeightM);
                return controller.GlobalPosition + Vector3.Up * (dof.BodyFocusHeightFraction * controller.BodyHeightM);
            }
            return body.GlobalPosition;
        }
        return camera.GlobalPosition + forward * Preset.FocusBandM;
    }

    public override void _Process(double delta)
    {
        if (Preset == null) return;
        CollectLut();
        if (_framesSinceApply++ == 2 && !Dressed)
            Warn("no room geometry with a shell was dressed after the first frames; the look has no GI bake and no shell layer");
        if (_pinnedHour == null && Preset.FollowClock)
        {
            _clockTimer += delta;
            if (_clockTimer > 60.0) { _clockTimer = 0; ApplyMoment(synchronous: false); }
        }
        if (!Preset.DofEnabled || !DofSupported) return;
        var camera = GetViewport()?.GetCamera3D();
        if (camera == null || _framed.Contains(camera.GetInstanceId())) return;
        Focus(camera, FocusPointFor(camera));
    }

    /// <summary>
    /// Problems with the look as rendered, for the review harness: an undressed room, a renderer fallback,
    /// VoxelGI without data, bake stand-ins left on meshes, a post effect that never ran, and any warnings.
    /// Empty means the GPU paths did what they should.
    /// </summary>
    public string[] SelfCheck()
    {
        var problems = new List<string>(_warnings);
        if (!Dressed) problems.Add("the room was never dressed");
        var gpu = RenderingServer.GetRenderingDevice() != null;
        if (gpu && Preset.GiMode == "voxelgi" && (Gi == null || Gi.Data == null)) problems.Add("VoxelGI has no baked data");
        var standIns = GetParent()?.FindChildren("*", "GeometryInstance3D", true, false).OfType<GeometryInstance3D>()
            .Count(g => g.MaterialOverride?.HasMeta(BakeStandInMeta) == true) ?? 0;
        if (standIns > 0) problems.Add($"{standIns} mesh(es) still carry VoxelGI bake stand-ins");
        if (gpu && Post != null && !Post.Ran) problems.Add("the grain and vignette effect never ran" + (Post.Error.Length > 0 ? ": " + Post.Error : ""));
        return problems.Distinct().ToArray();
    }

    /// <summary>A one-line description of what the look applied, for review reports.</summary>
    public string DescribeLook() => string.Format(CultureInfo.InvariantCulture,
        "{0}@{1} ({2}) sha256={3}; renderer={4}{5}; key={6} elevation {7:0.#} azimuth {8:0.#} energy {9:0.##}; hour {10:0.##} day {11} season {12}; {13}; ssao={14} ssil={15} glow={16} dof={17} tilt={18}; grain {19} vignette {20} post effect {21}",
        Preset.PresetId, Preset.PresetVersion, Preset.Status, Preset.Sha256, RenderingServer.GetCurrentRenderingMethod(),
        RendererNote.Length > 0 ? " (" + RendererNote + ")" : "", Preset.KeyMode, Moment.KeyElevationDeg, Moment.KeyAzimuthDeg, Moment.KeyEnergy,
        Moment.Hour, Moment.DayOfYear, Moment.Season, GiNote, Preset.AoEnabled, Preset.SsilEnabled, Preset.GlowEnabled, Preset.DofEnabled && DofSupported,
        Preset.TiltShiftEnabled ? Preset.TiltShiftStrength : 0f, Preset.Grain, Preset.Vignette,
        Post == null ? "off" : Post.Ran ? "ran" : Post.Error.Length > 0 ? "failed: " + Post.Error : "not run (no GPU device)");
}
