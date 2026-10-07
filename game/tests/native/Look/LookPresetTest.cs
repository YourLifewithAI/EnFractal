using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Look;
using EnFractal.Native.Room;
using FileAccess = Godot.FileAccess;

namespace EnFractal.Tests.Look;

/// <summary>
/// Look track checks that run headless: the preset reader and the look numbers it carries, painterly materials
/// per role and the shader's uniforms, the colour grade and its season tint, time of day at every minute of the
/// day, depth of field and where it focuses, the grain and vignette effect's parameters, the VoxelGI bake
/// stand-ins, the look director's dressing of rooms, the grade cache, and the renderer fallback notice.
/// Rendering itself (the VoxelGI bake, the post effect's pixels, the images) needs a GPU and is checked by the
/// capture harness (tools/look/capture-look.ps1), not here.
/// </summary>
public partial class LookPresetTest : Node3D
{
    private int _checks;
    private int _failures;
    private string _presetText = "";

    public override async void _Ready()
    {
        try
        {
            var preset = StylePreset.Resolve(RoomWorld.DefaultStyleId, RoomWorld.DefaultStyleVersion, FileAccess.GetSha256(RoomWorld.DefaultStyle));
            _presetText = Encoding.UTF8.GetString(FileAccess.GetFileAsBytes(RoomWorld.DefaultStyle));
            var room = RoomData.Load(RoomWorld.DefaultRoom);
            CheckPreset(preset, room);
            CheckLookNumbersLiveInThePreset(preset);
            CheckFailClosed();
            CheckMaterials(preset);
            CheckShaderUniforms(preset);
            CheckGrade(preset);
            CheckSeasonTint(preset);
            CheckClock(preset);
            CheckClockAtEveryMinute(preset);
            CheckRealClockAndSwings(preset);
            CheckReviewCameras(preset, room);
            CheckDepthOfField(preset);
            CheckPostEffectParameters(preset);
            CheckBakeStandIns(preset, room);
            CheckRendererFallback(preset, room);
            await CheckDirector(preset, room);
            await CheckDressing(preset, room);
            await CheckFocus(preset, room);
            await CheckGradeCache(preset, room);
            await CheckArtDirectionFocus(preset, room);
            MaterialLibrary.Configure(preset);
            GD.Print($"NATIVE_LOOK: {_checks - _failures}/{_checks} checks passed; preset reader and look numbers, role materials and shader uniforms, grade and season tint, clock at every minute, depth of field and focus, post effect parameters, bake stand-ins, room dressing, grade cache, renderer notice");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError("Look test exception: " + exception);
            GetTree().Quit(1);
        }
    }

    // ---------- preset ----------

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
        var unpainted = preset.RoleTreatments.Keys.Where(r => !preset.Tuning.RoleMarks.ContainsKey(r)).ToArray();
        Check(unpainted.Length == 0, "every role the preset treats has its own brush marks in the preset: missing " + string.Join(",", unpainted));
    }

    /// <summary>Review finding: look constants lived in C#. The shipped preset now states every look number itself.</summary>
    private void CheckLookNumbersLiveInThePreset(StylePreset preset)
    {
        Check(preset.Tuning.Defaulted.Count == 0, "the preset states every look number; none is left to the code's defaults: " + string.Join(", ", preset.Tuning.Defaulted.Take(12)));
        Check(LookTuning.Blocks.All(StylePreset.KnownLookExtensions.Contains), "every tuning block is a known look extension");
        // The numbers really come from the file: change a few and the parsed look follows.
        var edited = PresetWith(Field("night_boost", "3.25"), Field("lut_size", "17"), Field("vignette_start", "0.3"), Field("tilt_pitch_gain", "0.75"));
        Check(Mathf.IsEqualApprox(edited.Tuning.Lamps.NightBoost, 3.25f) && edited.Tuning.Grade.LutSize == 17 && Mathf.IsEqualApprox(edited.Tuning.Post.VignetteStart, 0.3f)
            && Mathf.IsEqualApprox(edited.Tuning.Dof.TiltPitchGain, 0.75f), "edited tuning numbers in the preset reach the runtime");
        // A role's marks come from the preset, not a table in the code.
        var restyled = PresetWith(("\"wood\": { \"pattern\": \"wood\"", "\"wood\": { \"pattern\": \"stone\""));
        MaterialLibrary.Configure(restyled);
        var wood = (ShaderMaterial)MaterialLibrary.For("wood", new Color("9a6f45"));
        var other = (ShaderMaterial)MaterialLibrary.For("emissive", new Color("9a6f45"));
        Check(wood.GetShaderParameter("pattern").AsInt32() == (int)PaintPattern.Stone, "a role's pattern is read from the preset's role marks");
        Check(other.GetShaderParameter("pattern").AsInt32() == (int)restyled.Tuning.DefaultMarks.Pattern
            && Mathf.IsEqualApprox(other.GetShaderParameter("stroke_scale_m").AsSingle(), restyled.Tuning.DefaultMarks.StrokeScaleM),
            "a role the preset does not list takes the preset's default marks");
        MaterialLibrary.Configure(preset);
    }

    private void CheckFailClosed()
    {
        var bytes = Encoding.UTF8.GetBytes(_presetText.Replace("\"focus_band_m\"", "\"focus_band\""));
        CheckThrows(() => StylePreset.Parse(bytes, "probe.json"), "focus_band_m", "a preset missing a field the look needs is refused, naming it");
        var lastHour = _presetText[_presetText.LastIndexOf("{ \"hour\": ", StringComparison.Ordinal)..].Split(',')[0];
        CheckThrows(() => PresetWith((lastHour, "{ \"hour\": 3.0")), "increasing hour order", "time-of-day keys out of order are refused");
        CheckThrows(() => PresetWith(("\"x_look_paint\": {", "\"x_look_paint\": { \"tone_wsah\": 1,")), "tone_wsah", "a misspelled tuning field is refused, naming it");
        CheckThrows(() => PresetWith(("\"x_look_post\": {", "\"x_look_psot\": {")), "x_look_psot", "a misspelled look block is refused, naming it");
        CheckThrows(() => PresetWith(("\"default\": { \"pattern\"", "\"fallback\": { \"pattern\"")), "default", "role marks without a default entry are refused");
        CheckThrows(() => PresetWith(("\"mark_fade_end\": 0.45", "\"mark_fade_end\": 0.8")), "alias", "marks that would alias before fading are refused");
        var withoutExtensions = Encoding.UTF8.GetBytes(_presetText[.._presetText.IndexOf(",\n  \"extensions\"", StringComparison.Ordinal)] + "\n}\n");
        var fallback = StylePreset.Parse(withoutExtensions, "probe.json");
        Check(fallback.KeyMode == "fixed" && !fallback.SsilEnabled && Mathf.IsEqualApprox(fallback.GlazeAmount, 0.35f)
            && fallback.Tuning.Defaulted.Contains("x_look_paint.tone_wash") && fallback.Tuning.Defaulted.Contains("x_look_role_marks"),
            "a preset without x_look extensions loads with the code's defaults and lists what it defaulted");
    }

    // ---------- materials and the shader ----------

    private void CheckMaterials(StylePreset preset)
    {
        MaterialLibrary.Configure(preset);
        foreach (var role in new[] { "painted_wall", "plaster", "wood", "fabric", "paper", "cardboard", "rubber" })
        {
            var color = new Color("8a7a66");
            var material = MaterialLibrary.For(role, color) as ShaderMaterial;
            var treatment = preset.TreatmentFor(role);
            var marks = preset.Tuning.MarksFor(role);
            Check(material != null && material.Shader?.ResourcePath == MaterialLibrary.ShaderPath, $"{role} gets the painterly shader");
            if (material == null) continue;
            Check(Mathf.IsEqualApprox(material.GetShaderParameter("albedo_softening").AsSingle(), treatment.AlbedoSoftening)
                && Mathf.IsEqualApprox(material.GetShaderParameter("stroke_normal_strength").AsSingle(), treatment.StrokeNormalStrength)
                && Mathf.IsEqualApprox(material.GetShaderParameter("edge_wear").AsSingle(), treatment.EdgeWear)
                && Mathf.IsEqualApprox(material.GetShaderParameter("roughness_value").AsSingle(), treatment.Roughness),
                $"{role} carries the preset's softening, stroke strength, edge wear and roughness");
            Check(material.GetShaderParameter("pattern").AsInt32() == (int)marks.Pattern
                && Mathf.IsEqualApprox(material.GetShaderParameter("stroke_scale_m").AsSingle(), marks.StrokeScaleM)
                && Mathf.IsEqualApprox(material.GetShaderParameter("specular_strength").AsSingle(), marks.Specular)
                && material.GetShaderParameter("stroke_axis").AsVector3().IsEqualApprox(marks.StrokeAxis),
                $"{role} uses its role's pattern, stroke size, axis and highlight");
            Check(MaterialLibrary.BakeAlbedo(material) is { } bake && bake.IsEqualApprox(MaterialLibrary.Glaze(color, treatment.Tint, preset.GlazeAmount)),
                $"{role} records the flat colour VoxelGI bakes with");
            Check(ReferenceEquals(material, MaterialLibrary.For(role, color)), $"{role} materials are cached per role and colour");
        }
        var metal = (ShaderMaterial)MaterialLibrary.For("metal", new Color("888888"));
        Check(Mathf.IsEqualApprox(metal.GetShaderParameter("metallic_value").AsSingle(), preset.TreatmentFor("metal").Metallic), "metallic comes from the role treatment");
        var unknown = (ShaderMaterial)MaterialLibrary.For("emissive", new Color("888888"));
        Check(Mathf.IsEqualApprox(unknown.GetShaderParameter("roughness_value").AsSingle(), preset.DefaultTreatment.Roughness)
            && unknown.GetShaderParameter("pattern").AsInt32() == (int)PaintPattern.None, "a role without its own treatment uses the default treatment, unpatterned");
        var paint = preset.Tuning.Paint;
        Check(Mathf.IsEqualApprox(unknown.GetShaderParameter("tone_gain").AsSingle(), paint.ToneGain)
            && Mathf.IsEqualApprox(unknown.GetShaderParameter("mark_fade_end").AsSingle(), paint.MarkFadeEnd)
            && Mathf.IsEqualApprox(unknown.GetShaderParameter("shadow_fill").AsSingle(), paint.ShadowFillBase + paint.ShadowFillPerSoftness * preset.ShadowSoftness),
            "the shader's shared paint numbers come from the preset");
        var base1 = new Color("b9b4ab");
        Check(MaterialLibrary.Glaze(base1, null, 0.35f).IsEqualApprox(base1) && MaterialLibrary.Glaze(base1, new Color("c79a63"), 0f).IsEqualApprox(base1),
            "no tint or no glaze leaves the base colour alone");
        var glazed = MaterialLibrary.Glaze(base1, new Color("c79a63"), 1f);
        Check(Mathf.Abs(ColorGrade.Luma(glazed) - ColorGrade.Luma(base1)) < 0.01f && glazed.R > glazed.B, "a full glaze takes the tint's hue at the base colour's lightness");
        // Detail fades before a mark aliases: all of it while a pixel covers under mark_fade_start of the mark, none past half a mark.
        Check(Mathf.IsEqualApprox(MaterialLibrary.Detail(0.04f, 0.04f * paint.MarkFadeStart * 0.9f, paint), 1f)
            && MaterialLibrary.Detail(0.04f, 0.04f * 0.5f, paint) <= 1e-4f
            && MaterialLibrary.Detail(0.04f, 0.04f * 0.3f, paint) is > 0f and < 1f, "brush marks fade out between mark_fade_start and half a mark (no aliasing)");
        var before = MaterialLibrary.For("wood", new Color("9a6f45"));
        MaterialLibrary.Configure(preset);
        Check(!ReferenceEquals(before, MaterialLibrary.For("wood", new Color("9a6f45"))), "configuring a preset clears the material cache");
    }

    /// <summary>
    /// Review finding: a uniform could be ignored and every check still passed. Now: every parameter MaterialLibrary
    /// sets is a uniform the shader declares (Godot silently keeps a misspelled one), every uniform the shader
    /// declares is set from the preset (none sits at the shader's own default), and the shader's code reads every
    /// uniform and instance uniform it declares.
    /// </summary>
    private void CheckShaderUniforms(StylePreset preset)
    {
        MaterialLibrary.Configure(preset);
        foreach (var role in preset.RoleTreatments.Keys.Append("emissive")) MaterialLibrary.For(role, new Color("8a7a66"));
        var shader = GD.Load<Shader>(MaterialLibrary.ShaderPath);
        var declared = shader.GetShaderUniformList().Select(u => u.AsGodotDictionary()["name"].AsString()).ToHashSet();
        var set = MaterialLibrary.ParameterNames.ToHashSet();
        Check(declared.Count >= 30, $"the shader lists its uniforms ({declared.Count})");
        var unknown = set.Except(declared).ToArray();
        Check(unknown.Length == 0, "every parameter MaterialLibrary sets is a uniform of the shader: not declared " + string.Join(",", unknown));
        var unset = declared.Except(set).ToArray();
        Check(unset.Length == 0, "every uniform of the shader is set from the preset, none left at the shader default: unset " + string.Join(",", unset));
        var source = Regex.Replace(Regex.Replace(shader.Code, @"/\*.*?\*/", "", RegexOptions.Singleline), @"//[^\n]*", "");
        var declarations = Regex.Matches(source, @"^\s*(?:instance\s+)?uniform\s+\w+\s+(\w+)[^;]*;", RegexOptions.Multiline);
        var body = Regex.Replace(source, @"^\s*(?:instance\s+)?uniform\s+[^;]*;", "", RegexOptions.Multiline);
        var names = declarations.Select(m => m.Groups[1].Value).ToArray();
        var ignored = names.Where(n => !Regex.IsMatch(body, $@"\b{n}\b")).ToArray();
        Check(names.Length == declared.Count + 3 && ignored.Length == 0, "the shader reads every uniform it declares (and the three per-instance ones): ignored " + string.Join(",", ignored));
        var instance = Regex.Matches(source, @"^\s*instance\s+uniform\s+\w+\s+(\w+)", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToHashSet();
        Check(instance.SetEquals(new[] { "box_edges", "box_half_extents", "paint_seed" }), "the per-instance uniforms are the ones the look director sets: " + string.Join(",", instance));
    }

    // ---------- grade ----------

    private void CheckGrade(StylePreset preset)
    {
        var samples = new[] { new Color(0, 0, 0), new Color(1, 1, 1), new Color(0.2f, 0.5f, 0.8f), new Color(0.9f, 0.4f, 0.1f) };
        Check(samples.All(c => ColorGrade.Apply(GradeParams.Identity, c).IsEqualApprox(c)), "the identity grade changes nothing");
        var lut = ColorGrade.LutBytes(GradeParams.Identity);
        var size = GradeParams.Identity.Tuning.LutSize;
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
        var night = GradeParams.For(preset, LookClock.At(preset, 23f, 279));
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
        Check(texture.GetWidth() == preset.Tuning.Grade.LutSize && texture.GetDepth() == preset.Tuning.Grade.LutSize, "the grade builds a 3D texture of the preset's LUT size");
        Check(day.Quantized() == GradeParams.For(preset, LookClock.At(preset, preset.DefaultHour + 0.001f, 279)).Quantized(),
            "moments a fraction of a minute apart share one quantized grade (and so one cached LUT)");
    }

    /// <summary>
    /// Review finding: removing the season tint from the grade left every check passing, because winter is also
    /// cooler through its warmth. These checks hold everything else equal and look at the tint alone.
    /// </summary>
    private void CheckSeasonTint(StylePreset preset)
    {
        // Solar noon: the same point of the day in every season, whatever the day length.
        var (rise, set) = LookClock.DayLimits(preset);
        var noon = (rise + set) * 0.5f;
        var winterMoment = LookClock.At(preset, noon, 15);
        var summerMoment = LookClock.At(preset, noon, 196);
        var winter = GradeParams.For(preset, winterMoment);
        var summer = GradeParams.For(preset, summerMoment);
        Check(winter.SeasonTint.IsEqualApprox(winterMoment.SeasonTint) && winterMoment.SeasonTint.IsEqualApprox(preset.SeasonGrades["winter"].Tint)
            && summerMoment.SeasonTint.IsEqualApprox(preset.SeasonGrades["summer"].Tint), "mid-season days carry their season's tint into the grade");
        var neutral = new Color(0.5f, 0.5f, 0.5f);
        var grey = new Color(0.45f, 0.45f, 0.45f);
        foreach (var (name, grade) in new[] { ("winter", winter), ("summer", summer), ("autumn", GradeParams.For(preset, LookClock.At(preset, noon, 288))) })
        {
            var tinted = ColorGrade.Apply(grade, grey);
            var untinted = ColorGrade.Apply(grade with { SeasonTint = neutral }, grey);
            var shift = new Vector3(tinted.R - untinted.R, tinted.G - untinted.G, tinted.B - untinted.B);
            var tint = preset.SeasonGrades[name].Tint;
            var tintLuma = ColorGrade.Luma(tint);
            var chroma = new Vector3(tint.R - tintLuma, tint.G - tintLuma, tint.B - tintLuma);
            Check(shift.Length() > 0.01f && shift.Normalized().Dot(chroma.Normalized()) > 0.9f,
                $"the {name} tint alone shifts a mid grey toward the tint's hue (shift {shift.Length():0.###}, alignment {shift.Normalized().Dot(chroma.Normalized()):0.##})");
        }
        // The season tint lives in the mid-tones and lights: at noon (no night grading), a dark grey stays cool in every
        // season, and the tint moves it far less than it moves a mid grey.
        foreach (var (name, day) in new[] { ("winter", 15), ("spring", 105), ("summer", 196), ("autumn", 288) })
        {
            var grade = GradeParams.For(preset, LookClock.At(preset, noon, day));
            var dark = new Color(0.12f, 0.12f, 0.12f);
            var darkShift = Shift(grade, dark, neutral);
            var midShift = Shift(grade, grey, neutral);
            var graded = ColorGrade.Apply(grade, dark);
            Check(graded.B >= graded.R - 0.01f && darkShift < 0.3f * midShift,
                $"{name} at noon: shade stays cool (blue minus red {graded.B - graded.R:0.###}) and the season tint barely touches it ({darkShift:0.####} against {midShift:0.####} in the mid-tones)");
        }
        var winterLut = ColorGrade.LutBytes(winter);
        var winterUntinted = ColorGrade.LutBytes(winter with { SeasonTint = neutral });
        var size = winter.Tuning.LutSize;
        var mid = ((size / 2 * size + size / 2) * size + size / 2) * 3;
        Check(winterLut[mid + 2] - winterLut[mid] > winterUntinted[mid + 2] - winterUntinted[mid], "the winter LUT's mid-grey texel is bluer with the season tint than without");
        var winterKey = winterMoment.KeyColor;
        var summerKey = summerMoment.KeyColor;
        Check(winterKey.B / winterKey.R > summerKey.B / summerKey.R && winterMoment.SeasonWarmth < summerMoment.SeasonWarmth,
            "at noon, winter sunlight is bluer and cooler than summer sunlight");
    }

    /// <summary>How far the season tint alone moves a colour: the grade with the tint against the grade with a neutral tint.</summary>
    private static float Shift(GradeParams grade, Color input, Color neutral)
    {
        var tinted = ColorGrade.Apply(grade, input);
        var untinted = ColorGrade.Apply(grade with { SeasonTint = neutral }, input);
        return new Vector3(tinted.R - untinted.R, tinted.G - untinted.G, tinted.B - untinted.B).Length();
    }

    // ---------- clock ----------

    private void CheckClock(StylePreset preset)
    {
        var seasons = preset.Tuning.Seasons;
        Check(LookClock.SeasonAt(279, "north", seasons) == "autumn" && LookClock.SeasonAt(279, "south", seasons) == "spring", "6 October is autumn in the north and spring in the south");
        Check(LookClock.SeasonAt(15, "north", seasons) == "winter" && LookClock.SeasonAt(196, "north", seasons) == "summer" && LookClock.SeasonAt(105, "north", seasons) == "spring",
            "mid-January, mid-April and mid-July land in winter, spring and summer");
        var (from, to, t) = LookClock.Blend(365, "north", seasons);
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
        // On the keys' own reference day (no seasonal day length), the default hour puts the key exactly where the preset does.
        var reference = PresetWith((Regex.Match(_presetText, @"""day_length_h"": \[[^\]]*\]").Value, "\"day_length_h\": []"));
        var atDefault = LookClock.At(reference, reference.DefaultHour, 279);
        Check(Mathf.IsEqualApprox(atDefault.KeyElevationDeg, preset.KeyElevationDeg, 0.01f) && Mathf.IsEqualApprox(atDefault.KeyAzimuthDeg, preset.KeyAzimuthDeg, 0.01f),
            $"on the reference day, at the default hour, the key sits exactly where the preset puts it ({atDefault.KeyElevationDeg:0.##}, {atDefault.KeyAzimuthDeg:0.##})");
        var (sunrise, sunset) = LookClock.DayLimits(preset);
        var noon = LookClock.At(preset, (sunrise + sunset) * 0.5f, 279);
        Check(noon.KeyElevationDeg <= Mathf.Max(preset.Tuning.Sun.MaxElevationDeg, preset.KeyElevationDeg) + 1e-3f && noon.KeyElevationDeg >= preset.KeyElevationDeg,
            $"the noon sun is higher than the afternoon key but never overhead ({noon.KeyElevationDeg:0.#} degrees)");
        var deepNight = LookClock.At(preset, 2f, 279);
        Check(deepNight.MoonWeight >= 0.999f && Mathf.IsEqualApprox(deepNight.KeyElevationDeg, preset.Tuning.Sun.MoonElevationDeg, 0.01f),
            "deep in the night the key is the moon at its preset elevation");
        var afternoon = LookClock.At(preset, preset.DefaultHour, 279);
        var lateNight = LookClock.At(preset, 23f, 279);
        Check(afternoon.Season == "autumn" && afternoon.Daylight > 0.6f && lateNight.Daylight < 0.2f && lateNight.KeyEnergy < afternoon.KeyEnergy * 0.3f,
            "the afternoon is bright and late night is dark");
        Check(LookClock.At(preset, 2f, 279).KeyEnergy < LookClock.At(preset, 21f, 279).KeyEnergy, "2 a.m. is darker than 9 p.m.");
    }

    /// <summary>
    /// Review finding: the key flipped direction at dusk and dawn while still near full energy, and 2 a.m. was
    /// brighter than 9 p.m. Sampled at every minute of four days, one per season: the light is continuous, it
    /// rises once from its darkest minute (in the night) to its brightest and falls once back, the key only swings
    /// fast while it is dim, it is pure sun whenever daylight is at least half, and it never comes from below the
    /// horizon. Ambient light obeys the same rules.
    /// </summary>
    private void CheckClockAtEveryMinute(StylePreset preset)
    {
        var sun = preset.Tuning.Sun;
        const int minutes = 24 * 60;
        foreach (var day in new[] { 15, 105, 196, 288 })
        {
            var moments = Enumerable.Range(0, minutes).Select(m => LookClock.At(preset, m / 60f, day)).ToArray();
            var energy = moments.Select(m => m.KeyEnergy).ToArray();
            var ambient = moments.Select(m => m.AmbientEnergy).ToArray();
            var biggestStep = Enumerable.Range(0, minutes).Max(m => Mathf.Abs(energy[m] - energy[(m + minutes - 1) % minutes]));
            var biggestAmbientStep = Enumerable.Range(0, minutes).Max(m => Mathf.Abs(ambient[m] - ambient[(m + minutes - 1) % minutes]));
            Check(biggestStep <= 0.02f * preset.KeyEnergy && biggestAmbientStep <= 0.01f,
                $"day {day}: key and ambient energy change smoothly, minute to minute (largest steps {biggestStep:0.####}, {biggestAmbientStep:0.####})");
            var perUpdate = biggestStep * (float)LookDirector.ClockUpdateSeconds / 60f;
            Check(perUpdate <= 0.01f * preset.KeyEnergy,
                $"day {day}: following the real clock, one {LookDirector.ClockUpdateSeconds:0} s update changes the key by under 1% ({perUpdate / preset.KeyEnergy:P2})");
            Check(Unimodal(energy, out var darkest, out var brightest) && Unimodal(ambient, out _, out _),
                $"day {day}: the key rises once from its darkest minute ({darkest / 60}:{darkest % 60:00}) to its brightest ({brightest / 60}:{brightest % 60:00}) and falls once back, and so does the ambient");
            Check(darkest >= 22 * 60 || darkest <= 5 * 60, $"day {day}: the darkest minute is in the night ({darkest / 60}:{darkest % 60:00})");
            Check(energy[2 * 60] < energy[21 * 60] && energy[3 * 60] < energy[20 * 60], $"day {day}: 2 a.m. is darker than 9 p.m. and 3 a.m. darker than 8 p.m.");
            var worstBright = 0f;
            var worstAny = 0f;
            var moonWhileBright = 0;
            var belowHorizon = 0;
            for (var m = 0; m < minutes; m++)
            {
                var a = moments[(m + minutes - 1) % minutes];
                var b = moments[m];
                var step = Mathf.RadToDeg(LookClock.Direction(a.KeyElevationDeg, a.KeyAzimuthDeg).AngleTo(LookClock.Direction(b.KeyElevationDeg, b.KeyAzimuthDeg)));
                worstAny = Mathf.Max(worstAny, step);
                if (Mathf.Min(a.Daylight, b.Daylight) >= sun.SunDaylight) worstBright = Mathf.Max(worstBright, step);
                if (b.Daylight >= 0.5f && b.MoonWeight > 0f) moonWhileBright++;
                if (b.KeyElevationDeg < sun.HorizonElevationDeg - 0.01f && b.MoonWeight <= 0f) belowHorizon++;
            }
            var sunPerMinute = (sun.DegreesPerHour + sun.MaxElevationDeg) / 60f;
            Check(worstBright <= sunPerMinute, $"day {day}: while daylight is at least {sun.SunDaylight}, the key moves only as the sun does (at most {worstBright:0.###} degrees a minute)");
            Check(worstAny <= 10f, $"day {day}: the key never jumps; it swings to the moon only while dim (at most {worstAny:0.##} degrees a minute)");
            Check(moonWhileBright == 0, $"day {day}: whenever daylight is at least half the key is pure sun ({moonWhileBright} minutes otherwise)");
            Check(belowHorizon == 0 && moments.All(m => m.KeyElevationDeg >= Mathf.Min(sun.HorizonElevationDeg, sun.MoonElevationDeg) - 0.01f),
                $"day {day}: the key never shines from below the horizon");
        }
    }

    /// <summary>True when a daily curve rises (never falling) from its minimum to its maximum and falls (never rising) back, wrapping at midnight.</summary>
    private static bool Unimodal(float[] values, out int minimum, out int maximum)
    {
        var n = values.Length;
        var lo = 0;
        var hi = 0;
        for (var i = 1; i < n; i++)
        {
            if (values[i] < values[lo]) lo = i;
            if (values[i] > values[hi]) hi = i;
        }
        minimum = lo;
        maximum = hi;
        const float tolerance = 1e-5f;
        for (var i = lo; i != hi; i = (i + 1) % n)
            if (values[(i + 1) % n] < values[i] - tolerance) return false;
        for (var i = hi; i != lo; i = (i + 1) % n)
            if (values[(i + 1) % n] > values[i] + tolerance) return false;
        return true;
    }

    /// <summary>
    /// The founder's 6 October direction: time of day follows the real clock by default (seasons already follow the
    /// calendar), with a deterministic override for reviews and tests; and the swings are stronger: short winter
    /// days and long summer ones, darker nights, warm twilight pastels, and seasons that clearly differ.
    /// </summary>
    private void CheckRealClockAndSwings(StylePreset preset)
    {
        Check(preset.FollowClock && preset.FollowCalendar, "the preset follows the real clock and calendar by default");
        string? NoEnv(string name) => null;
        var both = LookClock.ClockOverride(new[] { "--look-clock=18:45", "--look-date=2026-01-15" }, NoEnv);
        Check(both.Hour is { } h && Mathf.IsEqualApprox(h, 18.75f) && both.DayOfYear == 15 && both.Error.Length == 0, "--look-clock and --look-date pin the hour and the day");
        var fromEnv = LookClock.ClockOverride(Array.Empty<string>(), name => name == "ENFRACTAL_LOOK_CLOCK" ? "7:05" : null);
        Check(fromEnv.Hour is { } e && Mathf.IsEqualApprox(e, 7f + 5f / 60f) && fromEnv.DayOfYear == null, "ENFRACTAL_LOOK_CLOCK pins the hour alone; the day still follows the calendar");
        var argsWin = LookClock.ClockOverride(new[] { "--look-clock=09:00" }, name => name == "ENFRACTAL_LOOK_CLOCK" ? "21:00" : null);
        Check(argsWin.Hour is { } a && Mathf.IsEqualApprox(a, 9f), "the command line wins over the environment");
        var bad = LookClock.ClockOverride(new[] { "--look-clock=25:00", "--look-date=2026-13-01" }, NoEnv);
        Check(bad.Hour == null && bad.DayOfYear == null && bad.Error.Contains("25:00") && bad.Error.Contains("2026-13-01"), "an unreadable override is reported and ignored: " + bad.Error);
        var none = LookClock.ClockOverride(Array.Empty<string>(), NoEnv);
        Check(none.Hour == null && none.DayOfYear == null && none.Error.Length == 0, "without an override the look follows the preset");

        // Seasonal day length: a continuous, increasing map from the real hour onto the keys' reference day.
        var (rise, set) = LookClock.DayLimits(preset);
        foreach (var day in new[] { 15, 105, 196, 288 })
        {
            var previous = LookClock.KeyHour(preset, 0f, day);
            var worst = 0f;
            var backwards = 0;
            for (var m = 1; m <= 24 * 60; m++)
            {
                var current = LookClock.KeyHour(preset, m / 60f, day);
                var step = Mathf.PosMod(current - previous, 24f);
                worst = Mathf.Max(worst, step);
                if (step > 12f) backwards++;
                previous = current;
            }
            var length = LookClock.DayLength(preset, day);
            var noon = (rise + set) * 0.5f;
            Check(backwards == 0 && worst < 0.1f && Mathf.IsEqualApprox(LookClock.KeyHour(preset, noon - length * 0.5f, day), rise, 1e-3f)
                && Mathf.IsEqualApprox(LookClock.KeyHour(preset, noon + length * 0.5f, day), set, 1e-3f),
                $"day {day}: the real day ({length:0.#} h) maps continuously onto the reference day, sunrise to sunrise and sunset to sunset");
        }
        int DaylightMinutes(int day) => Enumerable.Range(0, 24 * 60).Count(m => LookClock.At(preset, m / 60f, day).Daylight >= preset.Tuning.Sun.MoonDaylight);
        var winterDay = DaylightMinutes(15) / 60f;
        var summerDay = DaylightMinutes(196) / 60f;
        Check(Mathf.Abs(winterDay - preset.Tuning.Seasons.DayLengthH[0]) < 0.1f && Mathf.Abs(summerDay - preset.Tuning.Seasons.DayLengthH[2]) < 0.1f && summerDay - winterDay >= 4f,
            $"winter days are short and summer days long ({winterDay:0.##} h against {summerDay:0.##} h of daylight)");
        GD.Print($"LOOK_INFO: daylight {winterDay:0.##} h in mid-winter, {summerDay:0.##} h in mid-summer; reference day {rise:0.##} to {set:0.##}; review moment 16:30 on 6 October reads the keys at {LookClock.KeyHour(preset, 16.5f, 279):0.##}");
        var winterSix = LookClock.At(preset, 18f, 15);
        var summerSix = LookClock.At(preset, 18f, 196);
        Check(winterSix.Daylight < preset.Tuning.Sun.MoonDaylight && summerSix.Daylight > 0.5f,
            $"at six in the evening it is night in winter and still bright in summer (daylight {winterSix.Daylight:0.##} against {summerSix.Daylight:0.##})");

        // Stronger swings.
        var keys = preset.TimeKeys;
        var peak = keys.Max(k => k.KeyEnergy);
        var deep = LookClock.Sample(keys, 2f);
        var midday = LookClock.Sample(keys, 13f);
        Check(deep.KeyEnergy <= 0.05f * peak && deep.AmbientEnergy <= 0.3f * midday.AmbientEnergy && deep.AmbientColor.B > deep.AmbientColor.R + 0.1f,
            $"nights are deep and blue (key {deep.KeyEnergy / peak:P0} of its peak, ambient {deep.AmbientEnergy / midday.AmbientEnergy:P0} of midday's)");
        // The evening moment the key falls to half its peak: the sunset.
        var sunsetHour = Enumerable.Range(0, 12 * 12).Select(i => preset.DefaultHour + i / 12f).First(h => LookClock.Sample(keys, h).KeyEnergy <= 0.5f * peak);
        var dusk = LookClock.Sample(keys, sunsetHour);
        Check(dusk.KeyColor.R > 1.5f * dusk.KeyColor.B && dusk.AmbientColor.B > dusk.AmbientColor.R + 0.1f,
            $"the sunset key (at {sunsetHour:0.##} on the reference day) is orange-gold under a light-blue sky: the twilight pastels");
        var grey = new Color(0.45f, 0.45f, 0.45f);
        var winterGrey = ColorGrade.Apply(GradeParams.For(preset, LookClock.At(preset, 13f, 15)), grey);
        var summerGrey = ColorGrade.Apply(GradeParams.For(preset, LookClock.At(preset, 13f, 196)), grey);
        var autumnGrey = ColorGrade.Apply(GradeParams.For(preset, LookClock.At(preset, 13f, 288)), grey);
        Check((winterGrey.B - winterGrey.R) - (summerGrey.B - summerGrey.R) >= 0.08f && autumnGrey.R - autumnGrey.B > 0.05f,
            $"the seasons clearly differ: winter blue, summer and autumn warm (blue minus red: winter {winterGrey.B - winterGrey.R:0.###}, summer {summerGrey.B - summerGrey.R:0.###}, autumn {autumnGrey.B - autumnGrey.R:0.###})");
        Check(preset.Tuning.Lamps.NightBoost >= 2f, "room lamps glow at least three times as bright at night as by day");
        // Twilight is not night: the golden hour keeps its warm colour, and the night look comes in only as the light goes.
        var golden = LookClock.At(preset, 16.5f, 279);
        var deepNight = LookClock.At(preset, 2f, 279);
        Check(golden.Daylight >= preset.Tuning.Grade.NightNoneAbove && GradeParams.For(preset, golden).Night == 0f && GradeParams.For(preset, deepNight).Night == 1f,
            $"the golden hour (daylight {golden.Daylight:0.##}) gets none of the night grade and deep night all of it");
        var sweep = Enumerable.Range(0, 24 * 60).Select(m => GradeParams.For(preset, LookClock.At(preset, m / 60f, 15)).Night).ToArray();
        var nightStep = Enumerable.Range(1, sweep.Length - 1).Max(i => Mathf.Abs(sweep[i] - sweep[i - 1]));
        Check(nightStep < 0.05f, $"the night grade comes and goes smoothly (largest step {nightStep:0.###} a minute in mid-winter)");
    }

    /// <summary>
    /// The review cameras: the five baseline cameras are unchanged, every camera stands inside the room and looks at
    /// its focus, and the high-angle art-direction cameras read as a tilt-shift miniature: both avatars inside the
    /// crisp band, the floor at the bottom of the frame nearer than the band and the top of the frame farther.
    /// </summary>
    private void CheckReviewCameras(StylePreset preset, RoomData room)
    {
        var path = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://") + "../tools/look/review_cameras.json");
        using var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllBytes(path));
        var root = document.RootElement;
        var cameras = root.GetProperty("cameras").EnumerateArray().ToArray();
        var ids = cameras.Select(c => c.GetProperty("id").GetString()).ToArray();
        Check(ids.Take(5).SequenceEqual(new[] { "player_eye", "over_shoulder", "companion", "low_corner", "ceiling_corner" }) && ids.Distinct().Count() == ids.Length,
            "the five baseline review cameras come first, in order, and ids are unique: " + string.Join(",", ids));
        var baseline = new Dictionary<string, (Vector3 Position, Vector3 LookAt, float Fov)>
        {
            ["player_eye"] = (new Vector3(0f, 0.087f, 0.6f), new Vector3(-0.25f, 0.14f, -0.6f), 70f),
            ["over_shoulder"] = (new Vector3(-0.15f, 0.22f, 1.12f), new Vector3(0.12f, 0.06f, 0.1f), 68f),
            ["companion"] = (new Vector3(0.5f, 0.16f, 0.02f), new Vector3(0.12f, 0.07f, 0.72f), 55f),
            ["low_corner"] = (new Vector3(1.84f, 0.05f, -1.34f), new Vector3(-0.2f, 0.5f, 0.8f), 65f),
            ["ceiling_corner"] = (new Vector3(-1.84f, 2.26f, 1.36f), new Vector3(0.15f, 0.05f, 0.25f), 60f),
        };
        Vector3 V(System.Text.Json.JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
        var clock = root.GetProperty("clock");
        Check(root.GetProperty("resolution")[0].GetInt32() == 1920 && root.GetProperty("resolution")[1].GetInt32() == 1080
            && Mathf.IsEqualApprox(clock.GetProperty("hour").GetSingle(), 16.5f) && clock.GetProperty("date").GetString() == "2026-10-06",
            "reviews stay at 1920 x 1080, pinned to 16:30 on 6 October");
        var inside = room.Bounds.Grow(-0.02f);
        foreach (var camera in cameras)
        {
            var id = camera.GetProperty("id").GetString()!;
            var position = V(camera.GetProperty("position_m"));
            var lookAt = V(camera.GetProperty("look_at_m"));
            var focus = V(camera.GetProperty("focus_m"));
            var fov = camera.GetProperty("fov_deg").GetSingle();
            if (baseline.TryGetValue(id, out var original))
                Check(position.IsEqualApprox(original.Position) && lookAt.IsEqualApprox(original.LookAt) && Mathf.IsEqualApprox(fov, original.Fov), $"{id} is unchanged");
            var forward = (lookAt - position).Normalized();
            Check(inside.HasPoint(position) && (focus - position).Dot(forward) > 0.05f && fov is >= 20f and <= 90f, $"{id} stands inside the room and looks at its focus");
        }
        var player = room.SpawnFor("player").PositionM;
        var companion = room.SpawnFor("companion", room.SpawnFor("player")).PositionM;
        foreach (var camera in cameras.Where(c => c.GetProperty("id").GetString() is "diorama_high" or "iso_room"))
        {
            var id = camera.GetProperty("id").GetString()!;
            var position = V(camera.GetProperty("position_m"));
            var forward = (V(camera.GetProperty("look_at_m")) - position).Normalized();
            var halfFov = Mathf.DegToRad(camera.GetProperty("fov_deg").GetSingle()) * 0.5f;
            var pitch = Mathf.Asin(-forward.Y);
            var focusDistance = (V(camera.GetProperty("focus_m")) - position).Dot(forward);
            var dof = LookDirector.DepthOfFieldFor(preset, focusDistance, -forward.Y);
            float Depth(Vector3 point) => (point - position).Dot(forward);
            var avatars = new[] { player + Vector3.Up * 0.05f, companion + Vector3.Up * 0.05f };
            var framed = avatars.All(a => (a - position).Normalized().AngleTo(forward) < halfFov * 0.8f);
            var crisp = avatars.All(a => Depth(a) > dof.NearDistance && Depth(a) < dof.FarDistance);
            // The centre column's top and bottom rays: where they meet the floor or the room's walls.
            var right = forward.Cross(Vector3.Up).Normalized();
            var (bottom, top) = forward.Rotated(right, halfFov).Y < forward.Rotated(right, -halfFov).Y
                ? (forward.Rotated(right, halfFov), forward.Rotated(right, -halfFov))
                : (forward.Rotated(right, -halfFov), forward.Rotated(right, halfFov));
            float Hit(Vector3 ray) => RayToRoom(room.Bounds, position, ray) * ray.Dot(forward);
            var bottomDepth = Hit(bottom);
            var topDepth = Hit(top);
            GD.Print($"LOOK_INFO: {id} pitch {Mathf.RadToDeg(pitch):0} degrees; crisp {dof.NearDistance:0.##} to {dof.FarDistance:0.##} m (focus {focusDistance:0.##} m, blur {dof.Amount:0.###}); avatars at {Depth(avatars[0]):0.##} and {Depth(avatars[1]):0.##} m; frame foot {bottomDepth:0.##} m, top {topDepth:0.##} m");
            Check(Mathf.RadToDeg(pitch) >= 30f && framed && crisp && bottomDepth < dof.NearDistance && topDepth > dof.FarDistance + 0.5f * dof.FarTransition,
                $"{id} looks down {Mathf.RadToDeg(pitch):0} degrees with both avatars framed and crisp ({dof.NearDistance:0.##} to {dof.FarDistance:0.##} m), the frame's foot nearer than the band ({bottomDepth:0.##} m) and its top deep in the far blur ({topDepth:0.##} m)");
        }
    }

    /// <summary>Distance along a ray from inside the room to the room's bounds.</summary>
    private static float RayToRoom(Aabb bounds, Vector3 origin, Vector3 direction)
    {
        var best = float.PositiveInfinity;
        for (var axis = 0; axis < 3; axis++)
        {
            if (Mathf.Abs(direction[axis]) < 1e-6f) continue;
            var plane = direction[axis] > 0f ? bounds.End[axis] : bounds.Position[axis];
            best = Mathf.Min(best, (plane - origin[axis]) / direction[axis]);
        }
        return best;
    }

    // ---------- depth of field ----------

    private void CheckDepthOfField(StylePreset preset)
    {
        const float body = 0.10f;
        var eyeFocus = preset.Tuning.Dof.EyeFocusBodyHeights * body;
        var eye = LookDirector.DepthOfFieldFor(preset, eyeFocus, 0f, preset.Tuning.Dof.EyeCrispBodyHeights * body);
        Check(eye.NearEnabled && eye.NearDistance <= 0.15f && eye.FarEnabled && eye.FarDistance >= 0.45f,
            $"from a 10 cm body's eye the 15 cm reach is crisp and the far room softens (near {eye.NearDistance:0.###}, far {eye.FarDistance:0.###})");
        var high = LookDirector.DepthOfFieldFor(preset, 2.9f, 0.7f);
        var level = LookDirector.DepthOfFieldFor(preset, 2.9f, 0f);
        Check(high.Amount > level.Amount && high.FarDistance < level.FarDistance && high.NearDistance > level.NearDistance,
            "looking down on the room narrows the band and strengthens the blur (tilt-shift)");
        Check(high.NearDistance < 2.9f && high.FarDistance > 2.9f, "the focus distance itself is always inside the band");
        var near = LookDirector.DepthOfFieldFor(preset, 0.6f, 0f);
        var far = LookDirector.DepthOfFieldFor(preset, 3f, 0f);
        Check(far.FarTransition > near.FarTransition, "the far blur ramps over a longer distance when the focus is farther");
        foreach (var distance in new[] { 0.3f, 0.6f, 1.1f, 2.0f, 2.9f })
            foreach (var down in new[] { 0f, 0.4f, 0.8f })
            {
                var dof = LookDirector.DepthOfFieldFor(preset, distance, down);
                if (!(dof.NearDistance < distance && dof.FarDistance > distance && dof.FarTransition > 0f && dof.NearTransition > 0f && dof.Amount is > 0f and <= 1f))
                    Check(false, $"depth of field is well formed at {distance} m looking down {down}: {dof}");
            }
    }

    // ---------- post effect ----------

    /// <summary>
    /// Review finding: removing the grain and vignette effect left every check passing. Headless there is no GPU, so
    /// these checks pin what the effect sends and does: the push constants match the compute shader's Params block
    /// name for name, carry the preset's numbers, and, run through a CPU mirror of the shader's arithmetic, darken the
    /// corners and add a grain of the preset's size; the shader applies both to the colour. The capture harness checks
    /// the rendered pixels on the GPU (post_effect_check in timings.json).
    /// </summary>
    private void CheckPostEffectParameters(StylePreset preset)
    {
        var block = Regex.Match(LookPostEffect.ComputeSource, @"uniform\s+Params\s*\{(?<fields>.*?)\}\s*params;", RegexOptions.Singleline);
        var declared = new List<string>();
        foreach (Match field in Regex.Matches(block.Groups["fields"].Value, @"(vec2|float)\s+(\w+);"))
        {
            if (field.Groups[1].Value == "vec2") { declared.Add(field.Groups[2].Value + ".x"); declared.Add(field.Groups[2].Value + ".y"); }
            else declared.Add(field.Groups[2].Value);
        }
        Check(block.Success && declared.SequenceEqual(LookPostEffect.PushConstantNames) && declared.Count * 4 % 16 == 0,
            "the effect's push constants match the compute shader's Params block, name for name, in a 16-byte multiple: " + string.Join(",", declared));
        var effect = new LookPostEffect { Grain = preset.Grain, Vignette = preset.Vignette, Tuning = preset.Tuning.Post };
        var size = new Vector2I(1920, 1080);
        var constants = effect.PushConstants(size);
        float P(string name) => constants[Array.IndexOf(LookPostEffect.PushConstantNames, name)];
        var post = preset.Tuning.Post;
        Check(constants.Length == LookPostEffect.PushConstantNames.Length && P("size.x") == 1920f && P("size.y") == 1080f && P("grain") == preset.Grain
            && P("vignette") == preset.Vignette && P("vignette_start") == post.VignetteStart && P("vignette_end") == post.VignetteEnd
            && P("grain_fine") == post.GrainFine && P("grain_soft") == post.GrainSoft && P("grain_soft_px") == post.GrainSoftPx,
            "the effect pushes the preset's grain, vignette and their shape");
        var centre = Patch(constants, new Vector2I(size.X / 2 - 32, size.Y / 2 - 32));
        var corner = Patch(constants, Vector2I.Zero);
        var expectedSpread = preset.Grain * Mathf.Sqrt((post.GrainFine * post.GrainFine + post.GrainSoft * post.GrainSoft) / 12f);
        Check(Mathf.Abs(centre.Mean - 1f) < 0.01f && corner.Mean < centre.Mean - 0.6f * preset.Vignette,
            $"the vignette darkens the corners and leaves the centre (centre {centre.Mean:0.###}, corner {corner.Mean:0.###})");
        Check(centre.Spread > 0.5f * expectedSpread && centre.Spread < 2f * expectedSpread,
            $"the grain varies pixel to pixel by about the preset's amount (spread {centre.Spread:0.####}, expected about {expectedSpread:0.####})");
        Check(Regex.IsMatch(LookPostEffect.ComputeSource, @"color\.rgb\s*\*=\s*vignette\s*\*\s*grain\s*;") && LookPostEffect.ComputeSource.Contains("imageStore(color_image, pixel, color)"),
            "the compute shader multiplies the colour by the vignette and the grain and writes it back");
        Check(effect.EffectCallbackType == CompositorEffect.EffectCallbackTypeEnum.PostTransparent && effect.Enabled, "the effect runs after transparent geometry, before tone mapping");
    }

    private static (float Mean, float Spread) Patch(float[] constants, Vector2I origin)
    {
        var values = new List<float>();
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
                values.Add(LookPostEffect.Factor(constants, origin + new Vector2I(x, y)));
        var mean = values.Average();
        return (mean, Mathf.Sqrt(values.Average(v => (v - mean) * (v - mean))));
    }

    // ---------- VoxelGI bake stand-ins ----------

    /// <summary>
    /// Review finding: the bake's flat stand-in materials could stay on the room and every check still passed. The
    /// swap is exercised here without a GPU: during the bake every painterly mesh wears a flat stand-in of its bake
    /// colour, and afterwards every original material is back, also when the bake throws.
    /// </summary>
    private void CheckBakeStandIns(StylePreset preset, RoomData room)
    {
        MaterialLibrary.Configure(preset);
        var built = RoomBuilder.Build(room);
        var meshes = built.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
        var originals = meshes.ToDictionary(m => m, m => m.MaterialOverride);
        var painterly = meshes.Count(m => MaterialLibrary.BakeAlbedo(m.MaterialOverride) != null);
        var swapped = 0;
        var flat = 0;
        LookDirector.WithBakeStandIns(meshes, () =>
        {
            foreach (var mesh in meshes)
            {
                if (mesh.MaterialOverride is not StandardMaterial3D standIn || !standIn.HasMeta(LookDirector.BakeStandInMeta)) continue;
                swapped++;
                if (MaterialLibrary.BakeAlbedo(originals[mesh]) is { } albedo && standIn.AlbedoColor.IsEqualApprox(albedo)) flat++;
            }
        });
        Check(painterly == meshes.Length && painterly == room.Shell.Count + room.Objects.Count && swapped == painterly && flat == painterly,
            $"during a bake every painterly mesh wears a flat stand-in of its bake colour ({swapped}/{painterly})");
        Check(meshes.All(m => ReferenceEquals(m.MaterialOverride, originals[m])), "after a bake every original material is back");
        var threw = false;
        try { LookDirector.WithBakeStandIns(meshes, () => throw new InvalidOperationException("bake failed")); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw && meshes.All(m => ReferenceEquals(m.MaterialOverride, originals[m])), "when the bake throws, the error surfaces and every original material is still restored");
        built.Free();
    }

    // ---------- renderer fallback ----------

    /// <summary>Review finding: a renderer fallback went unreported. The director now says so to the log, the review harness and the player.</summary>
    private void CheckRendererFallback(StylePreset preset, RoomData room)
    {
        Check(LookDirector.RendererNoteFor("p", "forward_plus", "forward_plus").Length == 0, "no note when the machine renders with the preset's renderer");
        var note = LookDirector.RendererNoteFor("p", "forward_plus", "gl_compatibility");
        Check(note.Contains("gl_compatibility") && note.Contains("flatter"), "a fallback to Compatibility says what the room loses: " + note);
        Check(RenderingServer.GetCurrentRenderingMethod() == "forward_plus", $"this test runs the Forward+ rendering method ({RenderingServer.GetCurrentRenderingMethod()})");
        // A preset designed for another renderer than the one running: the director reports it everywhere.
        var other = PresetWith(("\"method\": \"forward_plus\"", "\"method\": \"mobile\""));
        var holder = new Node3D { Name = "FallbackHolder" };
        AddChild(holder);
        var look = new LookDirector { Name = "Look" };
        holder.AddChild(look);
        look.Apply(other, room);
        Check(look.RendererNote.Contains("mobile") && look.Warnings.Contains(look.RendererNote) && look.PlayerNotice.Contains(look.RendererNote)
            && look.SelfCheck().Contains(look.RendererNote), "a renderer mismatch reaches the warnings, the self-check and the player's notice");
        holder.Free();
        MaterialLibrary.Configure(preset);
    }

    // ---------- the look director ----------

    private async Task CheckDirector(StylePreset preset, RoomData room)
    {
        var (holder, look) = NewDirector(preset, room, "DirectorHolder");
        look.SetClock(preset.DefaultHour, 279);
        var built = RoomBuilder.Build(room);
        holder.AddChild(built);
        await Frames(2);
        Check(look.Dressed && look.Bakes == 1, "the look dresses the built room after it is added, baking once");
        var environment = look.GetChildren().OfType<WorldEnvironment>().Single();
        Check(environment.Environment.SsaoEnabled && environment.Environment.GlowEnabled && environment.Environment.AdjustmentEnabled
            && environment.Environment.AdjustmentColorCorrection is ImageTexture3D, "the environment has SSAO, glow and the grade LUT");
        Check(Mathf.IsEqualApprox(environment.Environment.SsaoPower, preset.Tuning.Ssao.Power) && Mathf.IsEqualApprox(environment.Environment.GlowHdrThreshold, preset.Tuning.Glow.HdrThreshold),
            "SSAO and glow shape come from the preset");
        Check(environment.Compositor?.CompositorEffects.Count == 1 && look.Post != null && environment.Compositor.CompositorEffects[0] == look.Post
            && Mathf.IsEqualApprox(look.Post.Grain, preset.Grain) && Mathf.IsEqualApprox(look.Post.Vignette, preset.Vignette) && look.Post.Tuning == preset.Tuning.Post,
            "grain and vignette come from the preset through the environment's compositor effect");
        var hintLights = look.GetChildren().OfType<Light3D>().Where(l => l.HasMeta("light_hint_id")).ToArray();
        Check(hintLights.Length == look.RoomLightCount && hintLights.All(l => room.LightHints.Any(h => h.Id == l.GetMeta("light_hint_id").AsString()))
            && hintLights.OfType<OmniLight3D>().Any(l => l.GetMeta("light_hint_id").AsString() == "ceiling_lamp")
            && look.Key.ShadowEnabled && (look.Key.ShadowCasterMask & LookDirector.ShellVisualLayer) == 0,
            $"the room's light hints are lit ({look.RoomLightCount}, the ceiling lamp among them) and the diorama key casts no shadows from the shell");
        var lamp = hintLights.OfType<OmniLight3D>().First(l => l.GetMeta("light_hint_id").AsString() == "ceiling_lamp");
        Check(Mathf.IsEqualApprox(lamp.OmniRange, room.Bounds.Size.Length() * preset.Tuning.Lamps.RangePerDiagonal) && Mathf.IsEqualApprox(lamp.OmniAttenuation, preset.Tuning.Lamps.Attenuation),
            "the lamp's reach and falloff come from the preset");
        var meshes = built.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
        var shell = meshes.Where(m => m.GetParent().HasMeta("surface_role")).ToArray();
        var objects = meshes.Except(shell).ToArray();
        Check(shell.Length == room.Shell.Count && shell.All(m => m.Layers == LookDirector.ShellVisualLayer), "shell visuals move to the shell layer");
        Check(shell.All(m => m.GIMode == (m.GetParent().GetMeta("surface_role").AsString() == "floor" ? GeometryInstance3D.GIModeEnum.Static : GeometryInstance3D.GIModeEnum.Disabled)),
            "only the floor joins the GI bake from the shell; walls and ceiling just receive it");
        Check(objects.Length == room.Objects.Count && objects.All(m => m.GIMode == GeometryInstance3D.GIModeEnum.Dynamic), "movable props are dynamic for GI");
        Check(objects.All(m => m.GetInstanceShaderParameter("box_edges").AsSingle() == 1f && m.GetInstanceShaderParameter("box_half_extents").AsVector3().X > 0f),
            "box props get softened, worn edges with their half extents");
        var seeds = meshes.Select(m => m.GetInstanceShaderParameter("paint_seed").AsSingle()).ToArray();
        Check(seeds.Distinct().Count() == seeds.Length && Mathf.IsEqualApprox(LookDirector.Seed("obj:table"), LookDirector.Seed("obj:table")),
            "each entity gets its own stable paint seed");
        Check(look.Gi == null && look.GiNote.Contains("no GI"), "without a GPU the VoxelGI bake is skipped and says so: " + look.GiNote);
        Check(LookDirector.GiVolume(room, preset.Tuning.Gi).Encloses(room.Bounds), "the GI volume encloses the room bounds");
        var afternoonEnergy = look.Key.LightEnergy;
        look.SetClock(23f, 279);
        Check(look.Key.LightEnergy < afternoonEnergy * 0.3f && look.Moment.Daylight < 0.2f, "pinning the clock to night dims the key");
        Check(lamp.LightEnergy > (float)lamp.GetMeta("base_energy").AsDouble() * 1.5f, "room lamps glow brighter at night");
        look.SetClock(16.5f, 196);
        Check(Mathf.IsEqualApprox(lamp.LightEnergy, (float)lamp.GetMeta("base_energy").AsDouble()), "on a bright summer afternoon the lamps are at their plain daytime level");
        look.SetClock(preset.DefaultHour, 279);
        Check(Mathf.IsEqualApprox(look.Key.LightEnergy, afternoonEnergy) && look.Moment.Season == "autumn", "pinning back to the default hour restores the afternoon");
        Check(look.RendererNote.Length == 0 && look.PlayerNotice.Length == 0, "no renderer mismatch is reported under Forward+");
        Check(look.DescribeLook().Contains("storybook_painterly@1") && look.DescribeLook().Contains("season autumn"), "the look describes itself for review reports");
        Check(look.SelfCheck().All(p => p.Contains("VoxelGI") || p.Contains("grain")) && !look.SelfCheck().Any(p => p.Contains("stand-in")), "the self-check finds nothing wrong but the missing GPU");
        objects[0].MaterialOverride = new StandardMaterial3D();
        objects[0].MaterialOverride.SetMeta(LookDirector.BakeStandInMeta, true);
        Check(look.SelfCheck().Any(p => p.Contains("stand-in")), "the self-check reports a bake stand-in left on a mesh");
        holder.QueueFree();
        await Frames(1);
    }

    /// <summary>
    /// Review finding: dressing was fragile (it found the room by node name, once). Now any mesh that enters under the
    /// director's parent is dressed, whichever order things arrive in and whatever the nodes are called; explicit and
    /// automatic dressing never double up; a rebuilt room is dressed and rebaked; meshes that are not room entities are
    /// left alone.
    /// </summary>
    private async Task CheckDressing(StylePreset preset, RoomData room)
    {
        // A room built before the look is applied, under any name.
        var early = new Node3D { Name = "EarlyHolder" };
        AddChild(early);
        MaterialLibrary.Configure(preset);
        var first = RoomBuilder.Build(room);
        first.Name = "Diorama";
        early.AddChild(first);
        var lookEarly = new LookDirector { Name = "Look" };
        early.AddChild(lookEarly);
        lookEarly.Apply(preset, room);
        await Frames(2);
        Check(lookEarly.Bakes == 1 && IsDressed(first, room), "a room built before the look, named anything, is dressed");
        early.QueueFree();

        // A look applied before it enters the tree, then placed beside a room.
        var late = new Node3D { Name = "LateHolder" };
        AddChild(late);
        var second = RoomBuilder.Build(room);
        late.AddChild(second);
        var lookLate = new LookDirector { Name = "Look" };
        lookLate.Apply(preset, room);
        late.AddChild(lookLate);
        await Frames(2);
        Check(lookLate.Bakes == 1 && IsDressed(second, room), "a look applied before it enters the tree dresses the room it joins");
        late.QueueFree();

        // RoomWorld's order: the look first, then the room, then an explicit Dress; the automatic pass must not bake again.
        var (holder, look) = NewDirector(preset, room, "OrderHolder");
        var built = RoomBuilder.Build(room);
        holder.AddChild(built);
        look.Dress(built);
        Check(look.Bakes == 1 && IsDressed(built, room), "an explicit Dress dresses and bakes at once");
        await Frames(2);
        look.Dress(built);
        Check(look.Bakes == 1, "the automatic pass and a second Dress do not dress or bake again");
        // Things that are not room entities: an avatar's meshes, and a mesh outside the look's parent.
        var avatar = new Node3D { Name = "Avatar" };
        var avatarMesh = new MeshInstance3D { Mesh = new BoxMesh(), MaterialOverride = MaterialLibrary.For("fabric", new Color("d28f63")) };
        avatar.AddChild(avatarMesh);
        holder.AddChild(avatar);
        var outside = new MeshInstance3D { Mesh = new BoxMesh(), MaterialOverride = MaterialLibrary.For("wood", new Color("9a6f45")) };
        var outsideOwner = new Node3D();
        outsideOwner.SetMeta("entity_id", "obj:elsewhere");
        outsideOwner.AddChild(outside);
        AddChild(outsideOwner);
        await Frames(2);
        Check(avatarMesh.Layers == 1u && avatarMesh.GetInstanceShaderParameter("paint_seed").VariantType == Variant.Type.Nil && look.Bakes == 1,
            "a mesh that is not a room entity keeps its layer and gets no paint seed or bake");
        Check(!outside.HasMeta(LookDirector.DressedMeta) && outside.GetInstanceShaderParameter("paint_seed").VariantType == Variant.Type.Nil,
            "a mesh outside the look's parent is not touched");
        outsideOwner.QueueFree();
        // The room is rebuilt (an undo, a restyle): the new geometry is dressed and baked again.
        built.QueueFree();
        var rebuilt = RoomBuilder.Build(room);
        holder.AddChild(rebuilt);
        await Frames(2);
        Check(look.Bakes == 2 && IsDressed(rebuilt, room), "a rebuilt room is dressed and baked again");
        // A later object (a creation) that is a room entity but not shell: dressed, no rebake.
        var creation = new Node3D { Name = "Creation" };
        creation.SetMeta("entity_id", "creation:tower");
        var creationMesh = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.1f, 0.2f, 0.1f) }, MaterialOverride = MaterialLibrary.For("wood", new Color("9a6f45")) };
        creation.AddChild(creationMesh);
        holder.AddChild(creation);
        await Frames(2);
        Check(look.Bakes == 2 && Mathf.IsEqualApprox(creationMesh.GetInstanceShaderParameter("paint_seed").AsSingle(), LookDirector.Seed("creation:tower"))
            && creationMesh.GetInstanceShaderParameter("box_edges").AsSingle() == 1f && creationMesh.GIMode == GeometryInstance3D.GIModeEnum.Static,
            "a later creation is painted with its own seed and box edges, without a rebake");
        holder.QueueFree();
        await Frames(1);
    }

    private static bool IsDressed(Node3D built, RoomData room)
    {
        var meshes = built.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
        return meshes.Length == room.Shell.Count + room.Objects.Count && meshes.All(m => m.HasMeta(LookDirector.DressedMeta) && m.GetInstanceShaderParameter("paint_seed").VariantType != Variant.Type.Nil)
            && meshes.Where(m => m.GetParent().HasMeta("surface_role")).All(m => m.Layers == LookDirector.ShellVisualLayer);
    }

    /// <summary>
    /// Review finding: removing where depth of field focuses left every check passing. Now the current camera's blur
    /// band is checked against the player's body as it moves, from a follow camera and from the player's own eye, and
    /// against the other focus modes; framed review cameras keep their own focus.
    /// </summary>
    private async Task CheckFocus(StylePreset preset, RoomData room)
    {
        var (holder, look) = NewDirector(preset, room, "FocusHolder");
        var player = new SmallPlayerController { Name = "Player", ReadKeyboard = false };
        holder.AddChild(player);
        player.SetPhysicsProcess(false);
        player.GlobalPosition = new Vector3(0f, 0f, 0.6f);
        var dof = preset.Tuning.Dof;
        var camera = new Camera3D { Name = "Follow" };
        holder.AddChild(camera);
        camera.GlobalPosition = new Vector3(0f, 0.6f, 1.4f);
        camera.LookAt(player.GlobalPosition);
        camera.MakeCurrent();
        await Frames(2);
        var bodyPoint = player.GlobalPosition + Vector3.Up * (dof.BodyFocusHeightFraction * player.BodyHeightM);
        Check(look.FocusPointFor(camera).IsEqualApprox(bodyPoint), "a follow camera focuses on the player's body (found without FocusTarget)");
        Check(InFocus(camera, bodyPoint, out var band) && !InFocus(camera, bodyPoint + (camera.GlobalPosition - bodyPoint).Normalized() * 0.9f, out _),
            $"the current camera's crisp band holds the player and not what is far nearer the camera ({band})");
        player.GlobalPosition = new Vector3(0.3f, 0f, -0.6f);
        camera.LookAt(player.GlobalPosition);
        await Frames(2);
        var movedPoint = player.GlobalPosition + Vector3.Up * (dof.BodyFocusHeightFraction * player.BodyHeightM);
        Check(InFocus(camera, movedPoint, out band) && !InFocus(camera, bodyPoint, out _) && Distance(camera, movedPoint) > Distance(camera, bodyPoint) + 0.5f,
            $"the band follows the player a metre away and leaves its old place ({band})");
        var stand = new Node3D { Name = "Stand-in" };
        holder.AddChild(stand);
        stand.GlobalPosition = new Vector3(-0.5f, 0.05f, 0.9f);
        look.FocusTarget = stand;
        await Frames(2);
        Check(look.FocusPointFor(camera).IsEqualApprox(stand.GlobalPosition) && InFocus(camera, stand.GlobalPosition, out _), "an explicit FocusTarget wins over the parent's player");
        look.FocusTarget = player;
        player.EyeCamera.MakeCurrent();
        await Frames(2);
        var eyeForward = -player.EyeCamera.GlobalBasis.Z;
        var eyePoint = player.EyeCamera.GlobalPosition + eyeForward * (dof.EyeFocusBodyHeights * player.BodyHeightM);
        var reach = dof.EyeCrispBodyHeights * player.BodyHeightM;
        Check(look.FocusPointFor(player.EyeCamera).IsEqualApprox(eyePoint) && InFocus(player.EyeCamera, eyePoint, out band) && band.Near <= reach + 1e-4f,
            $"the player's eye focuses just past its reach and keeps the reach ({reach:0.##} m) crisp ({band})");
        var review = new Camera3D { Name = "Review" };
        holder.AddChild(review);
        review.GlobalPosition = new Vector3(-1.8f, 2.2f, 1.3f);
        review.LookAt(Vector3.Zero);
        var fixedPoint = new Vector3(0.5f, 0.05f, 0.2f);
        look.FrameCamera(review, fixedPoint);
        review.MakeCurrent();
        await Frames(2);
        Check(InFocus(review, fixedPoint, out _) && look.AttributesFor(review) == review.Attributes, "a framed review camera keeps the focus it was given");
        holder.QueueFree();
        await Frames(1);

        var fixedPreset = PresetWith(("\"focus\": \"player\"", "\"focus\": \"fixed\""));
        var (fixedHolder, fixedLook) = NewDirector(fixedPreset, room, "FixedFocusHolder");
        var still = new Camera3D();
        fixedHolder.AddChild(still);
        still.GlobalPosition = new Vector3(0f, 0.3f, 1f);
        still.LookAt(Vector3.Zero);
        Check(fixedLook.FocusPointFor(still).IsEqualApprox(still.GlobalPosition - still.GlobalBasis.Z * fixedPreset.FocusBandM), "\"fixed\" focuses focus_band_m ahead of the camera");
        var companionPreset = PresetWith(("\"focus\": \"player\"", "\"focus\": \"companion\""));
        var (companionHolder, companionLook) = NewDirector(companionPreset, room, "CompanionFocusHolder");
        var friend = new Node3D();
        companionHolder.AddChild(friend);
        friend.GlobalPosition = new Vector3(0.45f, 0f, 0.6f);
        companionLook.FocusCompanion = friend;
        Check(companionLook.FocusPointFor(still).IsEqualApprox(friend.GlobalPosition), "\"companion\" focuses on the companion");
        fixedHolder.QueueFree();
        companionHolder.QueueFree();
        MaterialLibrary.Configure(preset);
        await Frames(1);
    }

    private readonly record struct Band(float Near, float Far)
    {
        public override string ToString() => $"crisp {Near:0.###} to {Far:0.###} m";
    }

    private static float Distance(Camera3D camera, Vector3 point) => (point - camera.GlobalPosition).Dot(-camera.GlobalBasis.Z);

    /// <summary>Whether a point lies in the crisp band of the camera's depth-of-field attributes.</summary>
    private static bool InFocus(Camera3D camera, Vector3 point, out Band band)
    {
        band = default;
        if (camera.Attributes is not CameraAttributesPractical attributes || !attributes.DofBlurFarEnabled) return false;
        band = new Band(attributes.DofBlurNearEnabled ? attributes.DofBlurNearDistance : 0f, attributes.DofBlurFarDistance);
        var distance = Distance(camera, point);
        return distance >= band.Near && distance <= band.Far;
    }

    /// <summary>Review finding: a 15 ms LUT rebuild. Grades are cached by their quantized inputs and missing ones build off the main thread.</summary>
    /// <summary>
    /// The founder's depth-of-field direction: while moving, focus follows the player and the companion (the band
    /// stretches to keep a nearby companion crisp); while building, it follows the cursor or the free camera
    /// (FocusOverride); and a high view reads as a tilt-shift miniature (narrow band, short ramps, stronger blur).
    /// </summary>
    private async Task CheckArtDirectionFocus(StylePreset preset, RoomData room)
    {
        var high = LookDirector.DepthOfFieldFor(preset, 1.1f, Mathf.Sin(Mathf.DegToRad(45f)));
        var level = LookDirector.DepthOfFieldFor(preset, 1.1f, 0f);
        Check(high.FarDistance - high.NearDistance <= 0.35f && high.Amount >= 0.12f && high.FarTransition < level.FarTransition && high.NearTransition < level.NearTransition,
            $"from 45 degrees up, 1.1 m away, the crisp band is a miniature's ({high.NearDistance:0.##} to {high.FarDistance:0.##} m, blur {high.Amount:0.###}) with shorter ramps than a level view");
        var (holder, look) = NewDirector(preset, room, "ArtFocusHolder");
        look.SetClock(preset.DefaultHour, 279);
        var player = new SmallPlayerController { Name = "Player", ReadKeyboard = false };
        holder.AddChild(player);
        player.SetPhysicsProcess(false);
        player.GlobalPosition = new Vector3(0f, 0f, 0.6f);
        var friend = new Node3D { Name = "Friend" };
        holder.AddChild(friend);
        look.FocusCompanion = friend;
        var camera = new Camera3D();
        holder.AddChild(camera);
        camera.GlobalPosition = new Vector3(0f, 0.85f, 1.45f);
        camera.LookAt(player.GlobalPosition);
        camera.MakeCurrent();
        var playerPoint = player.GlobalPosition + Vector3.Up * (preset.Tuning.Dof.BodyFocusHeightFraction * player.BodyHeightM);
        friend.GlobalPosition = player.GlobalPosition + new Vector3(0.1f, 0f, -0.44f);
        await Frames(2);
        Check(InFocus(camera, playerPoint, out var band) && InFocus(camera, friend.GlobalPosition, out _),
            $"a companion 45 cm behind the player is kept crisp with it ({band})");
        var farEdges = new List<float>();
        for (var step = 0; step <= 12; step++)
        {
            friend.GlobalPosition = player.GlobalPosition + new Vector3(0f, 0f, -0.5f - 0.025f * step);
            await Frames(1);
            InFocus(camera, playerPoint, out var edge);
            farEdges.Add(edge.Far);
        }
        var biggestJump = farEdges.Zip(farEdges.Skip(1)).Max(p => Mathf.Abs(p.Second - p.First));
        Check(biggestJump < 0.15f, $"as the companion walks away the band shrinks back without a jump (largest step {biggestJump:0.###} m per 2.5 cm walked; a hard cut would jump about 0.4 m)");
        friend.GlobalPosition = player.GlobalPosition + new Vector3(0.2f, 0f, -2.0f);
        await Frames(2);
        Check(InFocus(camera, playerPoint, out band) && !InFocus(camera, friend.GlobalPosition, out _),
            $"a companion two metres away is not, and the player stays crisp ({band})");
        var cursor = new Vector3(-1.2f, 0.75f, -0.9f);
        look.FocusOverride = cursor;
        await Frames(2);
        Check(InFocus(camera, cursor, out band) && !InFocus(camera, playerPoint, out _), $"while building, focus follows the cursor or free camera's point ({band})");
        look.FocusOverride = null;
        await Frames(2);
        Check(InFocus(camera, playerPoint, out _), "clearing the override returns focus to the player");
        Check(look.ClockNote.Contains("pinned"), "the look reports a pinned clock: " + look.ClockNote);
        look.ReleaseClock();
        Check(look.ClockNote.StartsWith("real clock, real calendar", StringComparison.Ordinal), "released, it follows the real clock and calendar: " + look.ClockNote);
        holder.QueueFree();
        await Frames(1);
        // The deterministic override, as a reviewer or playtester sets it (the environment form; the command line reads the same way).
        var savedClock = OS.GetEnvironment("ENFRACTAL_LOOK_CLOCK");
        var savedDate = OS.GetEnvironment("ENFRACTAL_LOOK_DATE");
        OS.SetEnvironment("ENFRACTAL_LOOK_CLOCK", "05:15");
        OS.SetEnvironment("ENFRACTAL_LOOK_DATE", "2026-07-15");
        var (pinnedHolder, pinned) = NewDirector(preset, room, "PinnedClockHolder");
        Check(Mathf.IsEqualApprox(pinned.Moment.Hour, 5.25f) && pinned.Moment.DayOfYear == 196 && pinned.ClockNote.Contains("ENFRACTAL_LOOK_CLOCK"),
            $"ENFRACTAL_LOOK_CLOCK and ENFRACTAL_LOOK_DATE pin the look when it is applied ({pinned.Moment.Hour:0.##} h, day {pinned.Moment.DayOfYear}; {pinned.ClockNote})");
        pinnedHolder.QueueFree();
        if (savedClock.Length > 0) OS.SetEnvironment("ENFRACTAL_LOOK_CLOCK", savedClock); else OS.UnsetEnvironment("ENFRACTAL_LOOK_CLOCK");
        if (savedDate.Length > 0) OS.SetEnvironment("ENFRACTAL_LOOK_DATE", savedDate); else OS.UnsetEnvironment("ENFRACTAL_LOOK_DATE");
        await Frames(1);
    }

    private async Task CheckGradeCache(StylePreset preset, RoomData room)
    {
        var clock = Stopwatch.StartNew();
        ColorGrade.LutBytes(GradeParams.For(preset, LookClock.At(preset, 9f, 100)));
        GD.Print($"LOOK_INFO: one {preset.Tuning.Grade.LutSize}-cubed grade LUT builds in {clock.Elapsed.TotalMilliseconds:0.#} ms on this machine");
        var (holder, look) = NewDirector(preset, room, "CacheHolder");
        look.SetClock(preset.DefaultHour, 279);
        var builds = look.GradeBuilds;
        for (var i = 0; i < 10; i++) look.SetClock(preset.DefaultHour, 279);
        await Frames(30);
        Check(look.GradeBuilds == builds && look.GradeCurrent, "re-pinning the same moment and thirty frames build no new grade");
        look.SetClock(23f, 15);
        var night = look.Environment.AdjustmentColorCorrection;
        look.SetClock(preset.DefaultHour, 279);
        var day = look.Environment.AdjustmentColorCorrection;
        look.SetClock(23f, 15);
        Check(look.GradeBuilds == builds + 1 && look.Environment.AdjustmentColorCorrection == night && night != day, "returning to a moment reuses its cached grade");
        look.SetClockAsync(4f, 196);
        var pending = !look.GradeCurrent;
        var waited = Stopwatch.StartNew();
        while (!look.GradeCurrent && waited.ElapsedMilliseconds < 5000) await Frames(1);
        Check(pending && look.GradeCurrent && look.GradeBuilds == builds + 2, $"a new moment's grade builds off the main thread and replaces the old one when ready (pending {pending}, current {look.GradeCurrent}, builds {look.GradeBuilds - builds})");
        for (var hour = 0; hour < 24; hour++) look.SetClock(hour, 50);
        Check(look.CachedGrades <= LookDirector.GradeCacheSize, $"the grade cache stays bounded ({look.CachedGrades} of {LookDirector.GradeCacheSize})");
        holder.QueueFree();
        await Frames(1);
    }

    // ---------- helpers ----------

    private (Node3D Holder, LookDirector Look) NewDirector(StylePreset preset, RoomData room, string name)
    {
        var holder = new Node3D { Name = name };
        AddChild(holder);
        var look = new LookDirector { Name = "Look" };
        holder.AddChild(look);
        look.Apply(preset, room);
        return (holder, look);
    }

    /// <summary>A replacement that sets one numeric field of the shipped preset, whatever its current value.</summary>
    private (string From, string To) Field(string name, string value)
    {
        var match = Regex.Match(_presetText, $"\"{name}\": -?[0-9.]+");
        if (!match.Success) throw new InvalidOperationException($"test edit target not found: {name}");
        return (match.Value, $"\"{name}\": {value}");
    }

    /// <summary>The shipped preset with text replacements, parsed (not loaded from disk, so no pin applies).</summary>
    private StylePreset PresetWith(params (string From, string To)[] edits)
    {
        var text = _presetText;
        foreach (var (from, to) in edits)
        {
            if (!text.Contains(from, StringComparison.Ordinal)) throw new InvalidOperationException($"test edit target not found: {from}");
            text = text.Replace(from, to);
        }
        return StylePreset.Parse(Encoding.UTF8.GetBytes(text), "variant.json");
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
