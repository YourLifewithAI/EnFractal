using Godot;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Look;
using EnFractal.Native.Room;
using FileAccess = Godot.FileAccess;

namespace EnFractal.Tests.Look;

/// <summary>
/// Look track checks that run headless: the preset reader, painterly materials per role, the colour-grade
/// LUT, time of day and seasons, depth of field, and the look director's dressing of the test room.
/// Rendering itself (VoxelGI, the post effect, the images) needs a GPU and is checked by the capture
/// harness (tools/look/capture-look.ps1), not here.
/// </summary>
public partial class LookPresetTest : Node3D
{
    private int _checks;
    private int _failures;

    public override async void _Ready()
    {
        try
        {
            var preset = StylePreset.Resolve(RoomWorld.DefaultStyleId, RoomWorld.DefaultStyleVersion, FileAccess.GetSha256(RoomWorld.DefaultStyle));
            var room = RoomData.Load(RoomWorld.DefaultRoom);
            CheckPreset(preset, room);
            CheckFailClosed();
            CheckMaterials(preset, room);
            CheckGrade(preset);
            CheckClock(preset);
            CheckDepthOfField(preset);
            await CheckDirector(preset, room);
            GD.Print($"NATIVE_LOOK: {_checks - _failures}/{_checks} checks passed; preset reader, role materials, grade LUT, clock, depth of field, room dressing");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError("Look test exception: " + exception);
            GetTree().Quit(1);
        }
    }

    private void CheckPreset(StylePreset preset, RoomData room)
    {
        Check(preset.Status is "seed" or "draft" or "candidate" or "approved", $"the preset has a known status ({preset.Status})");
        Check(preset.RendererMethod == "forward_plus" && ProjectSettings.GetSetting("rendering/renderer/rendering_method").AsString() == "forward_plus",
            "the preset and the project both use Forward+");
        Check(preset.GiMode == "voxelgi" && preset.GiEnergy > 0 && preset.AoEnabled && preset.GlowEnabled && preset.ShadowsEnabled,
            "VoxelGI, SSAO, glow and shadows are on");
        Check(preset.DofEnabled && preset.FocusBandM > 0 && preset.NearBlurDistanceM < 0.15f, "depth of field is on and near blur starts inside the 15 cm reach");
        Check(preset.TimeKeys.Count >= 2 && preset.SeasonGrades.Count == 4, "time-of-day keys and four season grades are read");
        Check(preset.KeyMode == "diorama", "the experimental key mode extension is read");
        Check(preset.TargetFrameMs > 0 && preset.MaxShadowedLights >= 1, "budgets are read");
        Check(preset.RoleRoughness.ContainsKey("wood") && Mathf.IsEqualApprox(preset.DefaultRoughness, preset.DefaultTreatment.Roughness),
            "the roughness views RoomWorld's tests rely on still work");
        var roles = room.Shell.Select(s => s.MaterialRole).Concat(room.Objects.SelectMany(o => o.Asset.Materials.Select(m => m.Role))).Distinct().ToArray();
        var missing = roles.Where(r => !preset.RoleTreatments.ContainsKey(r)).ToArray();
        Check(missing.Length == 0, "every material role in the test room has its own treatment: missing " + string.Join(",", missing));
        var unpainted = preset.RoleTreatments.Keys.Where(r => !MaterialLibrary.RoleLooks.ContainsKey(r)).ToArray();
        Check(unpainted.Length == 0, "every role the preset treats has a painted look: missing " + string.Join(",", unpainted));
    }

    private void CheckFailClosed()
    {
        var bytes = FileAccess.GetFileAsBytes(RoomWorld.DefaultStyle);
        var text = Encoding.UTF8.GetString(bytes);
        var broken = Encoding.UTF8.GetBytes(text.Replace("\"focus_band_m\"", "\"focus_band\""));
        CheckThrows(() => StylePreset.Parse(broken, "probe.json"), "focus_band_m", "a preset missing a field the look needs is refused, naming it");
        var unordered = Encoding.UTF8.GetBytes(text.Replace("\"hour\": 21.0", "\"hour\": 3.0"));
        CheckThrows(() => StylePreset.Parse(unordered, "probe.json"), "increasing hour order", "time-of-day keys out of order are refused");
        var withoutExtensions = Encoding.UTF8.GetBytes(text[..text.IndexOf(",\n  \"extensions\"", StringComparison.Ordinal)] + "\n}\n");
        var fallback = StylePreset.Parse(withoutExtensions, "probe.json");
        Check(fallback.KeyMode == "fixed" && !fallback.SsilEnabled && Mathf.IsEqualApprox(fallback.GlazeAmount, 0.35f), "a preset without x_look extensions loads with defaults");
    }

    private void CheckMaterials(StylePreset preset, RoomData room)
    {
        MaterialLibrary.Configure(preset);
        foreach (var role in new[] { "painted_wall", "plaster", "wood", "fabric", "paper", "cardboard", "rubber" })
        {
            var color = new Color("8a7a66");
            var material = MaterialLibrary.For(role, color) as ShaderMaterial;
            var treatment = preset.TreatmentFor(role);
            Check(material != null && material.Shader?.ResourcePath == MaterialLibrary.ShaderPath, $"{role} gets the painterly shader");
            if (material == null) continue;
            Check(Mathf.IsEqualApprox(material.GetShaderParameter("albedo_softening").AsSingle(), treatment.AlbedoSoftening)
                && Mathf.IsEqualApprox(material.GetShaderParameter("stroke_normal_strength").AsSingle(), treatment.StrokeNormalStrength)
                && Mathf.IsEqualApprox(material.GetShaderParameter("edge_wear").AsSingle(), treatment.EdgeWear)
                && Mathf.IsEqualApprox(material.GetShaderParameter("roughness_value").AsSingle(), treatment.Roughness),
                $"{role} carries the preset's softening, stroke strength, edge wear and roughness");
            Check(material.GetShaderParameter("pattern").AsInt32() == (int)MaterialLibrary.LookFor(role).Pattern, $"{role} uses its role pattern");
            Check(MaterialLibrary.BakeAlbedo(material) is { } bake && bake.IsEqualApprox(MaterialLibrary.Glaze(color, treatment.Tint, preset.GlazeAmount)),
                $"{role} records the flat colour VoxelGI bakes with");
            Check(ReferenceEquals(material, MaterialLibrary.For(role, color)), $"{role} materials are cached per role and colour");
        }
        var metal = (ShaderMaterial)MaterialLibrary.For("metal", new Color("888888"));
        Check(Mathf.IsEqualApprox(metal.GetShaderParameter("metallic_value").AsSingle(), preset.TreatmentFor("metal").Metallic), "metallic comes from the role treatment");
        var unknown = (ShaderMaterial)MaterialLibrary.For("emissive", new Color("888888"));
        Check(Mathf.IsEqualApprox(unknown.GetShaderParameter("roughness_value").AsSingle(), preset.DefaultTreatment.Roughness)
            && unknown.GetShaderParameter("pattern").AsInt32() == (int)PaintPattern.None, "a role without its own treatment uses the default treatment, unpatterned");
        var base1 = new Color("b9b4ab");
        Check(MaterialLibrary.Glaze(base1, null, 0.35f).IsEqualApprox(base1) && MaterialLibrary.Glaze(base1, new Color("c79a63"), 0f).IsEqualApprox(base1),
            "no tint or no glaze leaves the base colour alone");
        var glazed = MaterialLibrary.Glaze(base1, new Color("c79a63"), 1f);
        Check(Mathf.Abs(ColorGrade.Luma(glazed) - ColorGrade.Luma(base1)) < 0.01f && glazed.R > glazed.B, "a full glaze takes the tint's hue at the base colour's lightness");
        var before = MaterialLibrary.For("wood", new Color("9a6f45"));
        MaterialLibrary.Configure(preset);
        Check(!ReferenceEquals(before, MaterialLibrary.For("wood", new Color("9a6f45"))), "configuring a preset clears the material cache");
    }

    private void CheckGrade(StylePreset preset)
    {
        var samples = new[] { new Color(0, 0, 0), new Color(1, 1, 1), new Color(0.2f, 0.5f, 0.8f), new Color(0.9f, 0.4f, 0.1f) };
        Check(samples.All(c => ColorGrade.Apply(GradeParams.Identity, c).IsEqualApprox(c)), "the identity grade changes nothing");
        var lut = ColorGrade.LutBytes(GradeParams.Identity);
        var size = ColorGrade.LutSize;
        var worst = 0;
        for (var z = 0; z < size; z += 4)
            for (var y = 0; y < size; y += 4)
                for (var x = 0; x < size; x += 4)
                {
                    var index = ((z * size + y) * size + x) * 3;
                    worst = Math.Max(worst, Math.Abs(lut[index] - (int)MathF.Round((x + 0.5f) / size * 255f)));
                    worst = Math.Max(worst, Math.Abs(lut[index + 2] - (int)MathF.Round((z + 0.5f) / size * 255f)));
                }
        Check(lut.Length == size * size * size * 3 && worst <= 1, $"the identity LUT holds texel-centre inputs (red along x, blue along z; worst {worst}/255)");
        var day = GradeParams.For(preset, LookClock.At(preset, preset.DefaultHour, 279));
        var night = GradeParams.For(preset, LookClock.At(preset, 22f, 279));
        var grey = new Color(0.4f, 0.4f, 0.4f);
        var dayGrey = ColorGrade.Apply(day, grey);
        var nightGrey = ColorGrade.Apply(night, grey);
        Check(ColorGrade.Luma(nightGrey) < ColorGrade.Luma(dayGrey) && nightGrey.B - nightGrey.R > dayGrey.B - dayGrey.R, "nights grade darker and bluer than the afternoon");
        var shadow = ColorGrade.Apply(day, new Color(0.15f, 0.15f, 0.15f));
        Check(shadow.B > shadow.R - 0.01f, "the palette's shadow tint keeps dark greys from going warm");
        var flat = ColorGrade.Apply(day, new Color(0.9f, 0.3f, 0.2f));
        var luma = ColorGrade.Luma(flat);
        Check(Mathf.Abs(flat.R - luma) < Mathf.Abs(0.9f - ColorGrade.Luma(new Color(0.9f, 0.3f, 0.2f))), "the palette desaturates strong colour");
        var texture = ColorGrade.LutTexture(day);
        Check(texture.GetWidth() == size && texture.GetDepth() == size, "the grade builds a 33-cubed 3D texture");
    }

    private void CheckClock(StylePreset preset)
    {
        Check(LookClock.SeasonAt(279, "north") == "autumn" && LookClock.SeasonAt(279, "south") == "spring", "6 October is autumn in the north and spring in the south");
        Check(LookClock.SeasonAt(15, "north") == "winter" && LookClock.SeasonAt(196, "north") == "summer" && LookClock.SeasonAt(105, "north") == "spring",
            "mid-January, mid-April and mid-July land in winter, spring and summer");
        var (from, to, t) = LookClock.Blend(365, "north");
        Check(from == "autumn" && to == "winter" && t is > 0f and < 1f, "the year wraps from autumn into winter");
        var keys = preset.TimeKeys;
        Check(keys.All(k => LookClock.Sample(keys, k.Hour).KeyColor.IsEqualApprox(k.KeyColor) && Mathf.IsEqualApprox(LookClock.Sample(keys, k.Hour).KeyEnergy, k.KeyEnergy)),
            "sampling at a key's hour returns that key");
        var beforeMidnight = LookClock.Sample(keys, 23.999f);
        var afterMidnight = LookClock.Sample(keys, 0.001f);
        var ambientStep = Mathf.Max(Mathf.Abs(beforeMidnight.AmbientColor.R - afterMidnight.AmbientColor.R),
            Mathf.Max(Mathf.Abs(beforeMidnight.AmbientColor.G - afterMidnight.AmbientColor.G), Mathf.Abs(beforeMidnight.AmbientColor.B - afterMidnight.AmbientColor.B)));
        Check(Mathf.Abs(beforeMidnight.KeyEnergy - afterMidnight.KeyEnergy) < 0.01f && ambientStep < 0.01f,
            $"night interpolation is continuous across midnight (ambient step {ambientStep:0.#####})");
        var night = LookClock.Sample(keys, 2f);
        Check(night.KeyEnergy >= Mathf.Min(keys[0].KeyEnergy, keys[^1].KeyEnergy) - 1e-4f && night.KeyEnergy <= Mathf.Max(keys[0].KeyEnergy, keys[^1].KeyEnergy) + 1e-4f,
            "02:00 sits between the evening and morning keys");
        var (elevation, azimuth) = LookClock.KeyDirection(preset.DefaultHour, preset.DefaultHour, preset.KeyElevationDeg, preset.KeyAzimuthDeg);
        Check(Mathf.IsEqualApprox(elevation, preset.KeyElevationDeg) && Mathf.IsEqualApprox(azimuth, preset.KeyAzimuthDeg), "at the default hour the key sits exactly where the preset puts it");
        var noon = LookClock.KeyDirection(12f, preset.DefaultHour, preset.KeyElevationDeg, preset.KeyAzimuthDeg);
        Check(noon.Elevation <= Mathf.Max(LookClock.MaxSunElevationDeg, preset.KeyElevationDeg) + 1e-3f && noon.Elevation >= preset.KeyElevationDeg,
            "the noon key is higher than the afternoon key but never overhead");
        Check(Mathf.IsEqualApprox(LookClock.KeyDirection(23f, preset.DefaultHour, preset.KeyElevationDeg, preset.KeyAzimuthDeg).Elevation, 35f), "at night the key is a moon at 35 degrees");
        var afternoon = LookClock.At(preset, preset.DefaultHour, 279);
        var lateNight = LookClock.At(preset, 22f, 279);
        Check(afternoon.Season == "autumn" && afternoon.Daylight > 0.8f && lateNight.Daylight < 0.2f && lateNight.KeyEnergy < afternoon.KeyEnergy * 0.3f,
            "the afternoon is bright and late night is dark");
        var winter = LookClock.At(preset, preset.DefaultHour, 15);
        var summer = LookClock.At(preset, preset.DefaultHour, 196);
        Check(winter.KeyColor.B / winter.KeyColor.R > summer.KeyColor.B / summer.KeyColor.R && winter.SeasonWarmth < summer.SeasonWarmth,
            "winter light is bluer and cooler than summer light");
    }

    private void CheckDepthOfField(StylePreset preset)
    {
        var eye = LookDirector.DepthOfFieldFor(preset, LookDirector.EyeFocusM, 0f);
        Check(eye.NearEnabled && eye.NearDistance <= 0.15f && eye.FarEnabled && eye.FarDistance >= 0.45f,
            $"from the eye the 15 cm reach is crisp and the far room softens (near {eye.NearDistance:0.###}, far {eye.FarDistance:0.###})");
        var high = LookDirector.DepthOfFieldFor(preset, 2.9f, 0.7f);
        var level = LookDirector.DepthOfFieldFor(preset, 2.9f, 0f);
        Check(high.Amount > level.Amount && high.FarDistance < level.FarDistance && high.NearDistance > level.NearDistance,
            "looking down on the room narrows the band and strengthens the blur (tilt-shift)");
        Check(high.NearDistance < 2.9f && high.FarDistance > 2.9f, "the focus distance itself is always inside the band");
        var near = LookDirector.DepthOfFieldFor(preset, 0.6f, 0f);
        var far = LookDirector.DepthOfFieldFor(preset, 3f, 0f);
        Check(far.FarTransition > near.FarTransition, "the far blur ramps over a longer distance when the focus is farther");
    }

    private async Task CheckDirector(StylePreset preset, RoomData room)
    {
        var holder = new Node3D { Name = "LookHolder" };
        AddChild(holder);
        var look = new LookDirector { Name = "Look" };
        holder.AddChild(look);
        look.Apply(preset, room);
        var built = RoomBuilder.Build(room);
        holder.AddChild(built);
        await Frames(2);
        Check(look.Dressed, "the look dresses the built room after RoomWorld adds it");
        var environment = look.GetChildren().OfType<WorldEnvironment>().Single();
        Check(environment.Environment.SsaoEnabled && environment.Environment.GlowEnabled && environment.Environment.AdjustmentEnabled
            && environment.Environment.AdjustmentColorCorrection is ImageTexture3D, "the environment has SSAO, glow and the grade LUT");
        Check(environment.Compositor?.CompositorEffects.Count == 1 && look.Post != null && Mathf.IsEqualApprox(look.Post.Grain, preset.Grain)
            && Mathf.IsEqualApprox(look.Post.Vignette, preset.Vignette), "grain and vignette come from the preset through a compositor effect");
        Check(look.RoomLightCount == 1 && look.Key.ShadowEnabled && (look.Key.ShadowCasterMask & LookDirector.ShellVisualLayer) == 0,
            "the lamp is lit and the diorama key casts no shadows from the shell");
        var meshes = built.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
        var shell = meshes.Where(m => m.GetParent().HasMeta("surface_role")).ToArray();
        var objects = meshes.Except(shell).ToArray();
        Check(shell.Length == 6 && shell.All(m => m.Layers == LookDirector.ShellVisualLayer), "shell visuals move to the shell layer");
        Check(shell.All(m => m.GIMode == (m.GetParent().GetMeta("surface_role").AsString() == "floor" ? GeometryInstance3D.GIModeEnum.Static : GeometryInstance3D.GIModeEnum.Disabled)),
            "only the floor joins the GI bake from the shell; walls and ceiling just receive it");
        Check(objects.Length == 5 && objects.All(m => m.GIMode == GeometryInstance3D.GIModeEnum.Dynamic), "movable props are dynamic for GI");
        Check(objects.All(m => m.GetInstanceShaderParameter("box_edges").AsSingle() == 1f && m.GetInstanceShaderParameter("box_half_extents").AsVector3().X > 0f),
            "box props get softened, worn edges with their half extents");
        var seeds = meshes.Select(m => m.GetInstanceShaderParameter("paint_seed").AsSingle()).ToArray();
        Check(seeds.Distinct().Count() == seeds.Length && Mathf.IsEqualApprox(LookDirector.Seed("obj:table"), LookDirector.Seed("obj:table")),
            "each entity gets its own stable paint seed");
        Check(look.Gi == null && look.GiNote.Contains("no GI"), "without a GPU the VoxelGI bake is skipped and says so: " + look.GiNote);
        Check(LookDirector.GiVolume(room).Encloses(room.Bounds), "the GI volume encloses the room bounds");
        var afternoonEnergy = look.Key.LightEnergy;
        look.SetClock(22f, 279);
        Check(look.Key.LightEnergy < afternoonEnergy * 0.3f && look.Moment.Daylight < 0.2f, "pinning the clock to night dims the key");
        look.SetClock(preset.DefaultHour, 279);
        Check(Mathf.IsEqualApprox(look.Key.LightEnergy, afternoonEnergy) && look.Moment.Season == "autumn", "pinning back to the default hour restores the afternoon");
        var camera = new Camera3D();
        holder.AddChild(camera);
        camera.GlobalPosition = new Vector3(0, 0.22f, 1.12f);
        camera.LookAt(new Vector3(0, 0.06f, 0.1f));
        look.FrameCamera(camera, new Vector3(0, 0.06f, 0.6f));
        Check(camera.Attributes is CameraAttributesPractical attributes && attributes.DofBlurFarEnabled && attributes.DofBlurNearEnabled
            && attributes.DofBlurNearDistance < 0.52f && attributes.DofBlurFarDistance > 0.52f, "a framed camera gets depth of field around its focus point");
        Check(look.RendererNote.Length == 0, "no renderer mismatch is reported under Forward+");
        Check(look.DescribeLook().Contains("storybook_painterly@1") && look.DescribeLook().Contains("season autumn"), "the look describes itself for review reports");
        holder.QueueFree();
        await Frames(1);
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void CheckThrows(Action action, string fragment, string label)
    {
        try { action(); Check(false, label + " (no error)"); }
        catch (Exception error) { Check(error.Message.Contains(fragment), $"{label} ({error.Message})"); }
    }

    private void Check(bool condition, string label)
    {
        _checks++;
        if (condition) return;
        _failures++;
        GD.PushError("Look: " + label);
    }
}
