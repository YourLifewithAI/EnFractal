using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EnFractal.Native;

namespace EnFractal.Tests.Look;

/// <summary>
/// Look review capture harness (Track L, packet L7 groundwork). Boots the room, drives the fixed review
/// cameras from tools/look/review_cameras.json at the review resolution, saves one PNG per camera and
/// measures frame time with vsync off. It renders through an offscreen SubViewport of exactly the review
/// size by default, so the result does not depend on the desktop size; --root-viewport renders in the
/// window instead (borderless, at the screen origin) as a cross-check.
///
/// It only uses RoomWorld's public surface, so the same file also captures older commits. Newer look
/// hooks (review clock, per-camera focus) are called by name when the LookDirector has them.
///
/// Needs a real window on the GPU: under --headless it reports that and exits with code 2.
/// After the cameras it checks the grain and vignette effect in the pixels (the first camera with and without
/// it) and writes the look's self-check (renderer fallback, missing GI bake, bake stand-ins left, post effect
/// never ran) to timings.json as look_problems. Any problem prints LOOK_CAPTURE_PROBLEM and exits with code 3,
/// so a capture never passes silently on a fallback renderer.
/// User arguments (after "--"): --cameras=PATH --out=DIR [--label=TEXT] [--warmup=N] [--frames=N]
/// [--only=ID,ID] [--sweep] [--root-viewport] [--commit=TEXT] [--note=TEXT] [--post-check=false] [--allow-problems]
/// </summary>
public partial class LookCaptureHarness : Node
{
    private const uint HiddenBodyLayer = 1u << 19;
    private readonly Dictionary<string, string> _args = new();
    private SubViewport? _target;
    private Camera3D _camera = null!;
    private RoomWorld _world = null!;
    private Node? _look;
    private readonly List<Dictionary<string, object>> _results = new();

    public override async void _Ready()
    {
        try
        {
            foreach (var argument in OS.GetCmdlineUserArgs())
            {
                var text = argument.TrimStart('-');
                var split = text.IndexOf('=');
                _args[split < 0 ? text : text[..split]] = split < 0 ? "true" : text[(split + 1)..];
            }
            if (DisplayServer.GetName() == "headless")
            {
                GD.Print("LOOK_CAPTURE: needs a real window on the GPU; --headless renders nothing. Run tools/look/capture-look.ps1.");
                GetTree().Quit(2);
                return;
            }
            var camerasPath = Require("cameras");
            var outDir = Require("out");
            var warmup = int.Parse(Arg("warmup", "120"), CultureInfo.InvariantCulture);
            var frames = int.Parse(Arg("frames", "300"), CultureInfo.InvariantCulture);
            using var document = JsonDocument.Parse(System.IO.File.ReadAllBytes(camerasPath));
            var root = document.RootElement;
            var resolution = new Vector2I(root.GetProperty("resolution")[0].GetInt32(), root.GetProperty("resolution")[1].GetInt32());
            System.IO.Directory.CreateDirectory(outDir);

            Engine.MaxFps = 0;
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);

            _world = GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
            AddChild(_world);
            for (var i = 0; i < 600 && !_world.WorldReady && _world.LoadError.Length == 0; i++) await NextFrame();
            if (!_world.WorldReady) throw new InvalidOperationException("room did not load: " + _world.LoadError);
            _look = _world.Look;
            // Review captures show the room, not the interface: hide the HUD and the companion's floating name.
            foreach (var layer in _world.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = false;
            foreach (var label in _world.FindChildren("*", "Label3D", true, false).OfType<Label3D>()) label.Visible = false;
            _world.Player.ReadKeyboard = false;
            _world.Player.SetInputEnabled(false);
            _world.Companion.Stay();
            foreach (var mesh in _world.Player.GetNode("OriginalPrototypeBody").FindChildren("*", "GeometryInstance3D", true, false).OfType<GeometryInstance3D>())
                mesh.Layers = HiddenBodyLayer;

            if (root.TryGetProperty("clock", out var clock)) SetClock(clock.GetProperty("hour").GetDouble(), DayOfYear(clock.GetProperty("date").GetString()!));
            BuildTarget(resolution);
            // Let the avatars settle on the floor and the companion come to rest.
            for (var i = 0; i < 45; i++) await NextFrame();

            var only = Arg("only", "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            foreach (var entry in root.GetProperty("cameras").EnumerateArray())
            {
                var id = entry.GetProperty("id").GetString()!;
                if (only.Count > 0 && !only.Contains(id)) continue;
                Frame(entry);
                for (var i = 0; i < warmup; i++) await NextFrame();
                var timing = await Measure(frames);
                var image = Grab();
                var file = $"{id}.png";
                image.SavePng(System.IO.Path.Combine(outDir, file));
                timing["camera"] = id;
                timing["file"] = file;
                timing["image_size"] = new[] { image.GetWidth(), image.GetHeight() };
                _results.Add(timing);
                GD.Print($"LOOK_CAPTURE camera={id} frame_ms_p50={timing["frame_ms_p50"]} frame_ms_p95={timing["frame_ms_p95"]} gpu_ms_mean={timing["gpu_ms_mean"]} image={image.GetWidth()}x{image.GetHeight()}");
            }
            if (Arg("sweep", "false") == "true") await Sweep(root, outDir);
            if (Arg("post-check", "true") == "true") _postCheck = await PostEffectCheck(root);
            var problems = LookProblems();
            WriteReport(outDir, camerasPath, resolution, warmup, frames, problems);
            foreach (var problem in problems) GD.Print("LOOK_CAPTURE_PROBLEM " + problem);
            GD.Print($"LOOK_CAPTURE_DONE cameras={_results.Count} out={outDir} problems={problems.Count}");
            // A renderer fallback, a post effect that did nothing or a broken bake must not pass silently as a review capture.
            GetTree().Quit(problems.Count == 0 || Arg("allow-problems", "false") == "true" ? 0 : 3);
        }
        catch (Exception error)
        {
            GD.PushError("Look capture failed: " + error);
            GetTree().Quit(1);
        }
    }

    private string Arg(string name, string fallback) => _args.TryGetValue(name, out var value) ? value : fallback;

    private string Require(string name) => _args.TryGetValue(name, out var value) && value.Length > 0
        ? value : throw new ArgumentException($"missing --{name}=...");

    private static int DayOfYear(string isoDate) =>
        DateTime.ParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayOfYear;

    private bool SetClock(double hour, int dayOfYear)
    {
        if (_look == null || !_look.HasMethod("SetClock")) return false;
        _look.Call("SetClock", hour, dayOfYear);
        return true;
    }

    private void BuildTarget(Vector2I size)
    {
        var window = GetViewport();
        if (Arg("root-viewport", "false") == "true")
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
            DisplayServer.WindowSetSize(size);
            DisplayServer.WindowSetPosition(Vector2I.Zero);
            _camera = new Camera3D { Name = "ReviewCamera" };
            AddChild(_camera);
            _camera.MakeCurrent();
            return;
        }
        // Mirror the window's 3D quality settings so the offscreen target renders what a player would see.
        _target = new SubViewport
        {
            Name = "ReviewTarget", Size = size, RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = window.Msaa3D, ScreenSpaceAA = window.ScreenSpaceAA, UseTaa = window.UseTaa,
            UseDebanding = window.UseDebanding, UseOcclusionCulling = window.UseOcclusionCulling,
            MeshLodThreshold = window.MeshLodThreshold, Scaling3DMode = window.Scaling3DMode,
            Scaling3DScale = window.Scaling3DScale, FsrSharpness = window.FsrSharpness,
            TextureMipmapBias = window.TextureMipmapBias, AnisotropicFilteringLevel = window.AnisotropicFilteringLevel,
            PositionalShadowAtlasSize = window.PositionalShadowAtlasSize, PositionalShadowAtlas16Bits = window.PositionalShadowAtlas16Bits,
        };
        for (var quadrant = 0; quadrant < 4; quadrant++)
            _target.SetPositionalShadowAtlasQuadrantSubdiv(quadrant, window.GetPositionalShadowAtlasQuadrantSubdiv(quadrant));
        AddChild(_target);
        _camera = new Camera3D { Name = "ReviewCamera" };
        _target.AddChild(_camera);
        _camera.Current = true;
        // The window shows a scaled preview only; it no longer renders the room itself.
        window.Disable3D = true;
        var preview = new CanvasLayer { Name = "Preview", Layer = 100 };
        AddChild(preview);
        var rect = new TextureRect { Texture = _target.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        preview.AddChild(rect);
        RenderingServer.ViewportSetMeasureRenderTime(_target.GetViewportRid(), true);
    }

    private void Frame(JsonElement entry)
    {
        var position = Vec(entry.GetProperty("position_m"));
        var lookAt = Vec(entry.GetProperty("look_at_m"));
        _camera.GlobalPosition = position;
        _camera.LookAt(lookAt, Mathf.Abs((lookAt - position).Normalized().Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up);
        _camera.Fov = (float)entry.GetProperty("fov_deg").GetDouble();
        _camera.Near = 0.005f;
        _camera.Far = 50f;
        var hideBody = entry.TryGetProperty("hide_player_body", out var hide) && hide.GetBoolean();
        _camera.CullMask = hideBody ? 0xFFFFFu & ~HiddenBodyLayer : 0xFFFFFu;
        var focus = entry.TryGetProperty("focus_m", out var focusElement) ? Vec(focusElement) : lookAt;
        if (_look != null && _look.HasMethod("FrameCamera")) _look.Call("FrameCamera", _camera, focus);
    }

    private static Vector3 Vec(JsonElement array) =>
        new((float)array[0].GetDouble(), (float)array[1].GetDouble(), (float)array[2].GetDouble());

    private async Task NextFrame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private Image Grab()
    {
        var image = (_target != null ? _target.GetTexture() : GetViewport().GetTexture()).GetImage();
        image.Convert(Image.Format.Rgb8);
        return image;
    }

    private async Task<Dictionary<string, object>> Measure(int frames)
    {
        var wall = new List<double>(frames);
        var gpu = new List<double>(frames);
        var cpu = new List<double>(frames);
        var drawCalls = new List<double>(frames);
        var primitives = new List<double>(frames);
        var rid = _target?.GetViewportRid() ?? GetViewport().GetViewportRid();
        if (_target == null) RenderingServer.ViewportSetMeasureRenderTime(rid, true);
        await NextFrame();
        var last = Time.GetTicksUsec();
        for (var i = 0; i < frames; i++)
        {
            await NextFrame();
            var now = Time.GetTicksUsec();
            wall.Add((now - last) / 1000.0);
            last = now;
            gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid));
            cpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid));
            drawCalls.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
            primitives.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
        }
        return new Dictionary<string, object>
        {
            ["frames"] = frames,
            ["frame_ms_mean"] = Round(wall.Average()),
            ["frame_ms_p50"] = Round(Percentile(wall, 0.50)),
            ["frame_ms_p95"] = Round(Percentile(wall, 0.95)),
            ["frame_ms_p99"] = Round(Percentile(wall, 0.99)),
            ["frame_ms_max"] = Round(wall.Max()),
            ["gpu_ms_mean"] = Round(gpu.Average()),
            ["gpu_ms_p95"] = Round(Percentile(gpu, 0.95)),
            ["render_cpu_ms_mean"] = Round(cpu.Average()),
            ["draw_calls_mean"] = Round(drawCalls.Average()),
            ["primitives_mean"] = Round(primitives.Average()),
            ["video_mem_mib"] = Round(Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0),
        };
    }

    private static double Percentile(List<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        var index = Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    private static double Round(double value) => Math.Round(value, 3);

    /// <summary>One camera across four seasons and four hours, composed into a single contact sheet.</summary>
    private async Task Sweep(JsonElement root, string outDir)
    {
        if (_look == null || !_look.HasMethod("SetClock"))
        {
            GD.Print("LOOK_CAPTURE: this build has no SetClock hook; skipping the time and season sweep.");
            return;
        }
        // The cameras file may name the sweep's camera, hours and dates ("sweep"); --sweep-camera overrides the camera.
        var sweep = root.TryGetProperty("sweep", out var configured) ? configured : default;
        var cameraId = Arg("sweep-camera", sweep.ValueKind == JsonValueKind.Object && sweep.TryGetProperty("camera", out var named) ? named.GetString()! : "ceiling_corner");
        var entry = root.GetProperty("cameras").EnumerateArray().First(c => c.GetProperty("id").GetString() == cameraId);
        Frame(entry);
        var dates = sweep.ValueKind == JsonValueKind.Object && sweep.TryGetProperty("dates", out var dateList)
            ? dateList.EnumerateArray().Select(d => (d.GetString()!, d.GetString()!)).ToArray()
            : new[] { ("winter", "2026-01-15"), ("spring", "2026-04-15"), ("summer", "2026-07-15"), ("autumn", "2026-10-15") };
        var hours = sweep.ValueKind == JsonValueKind.Object && sweep.TryGetProperty("hours", out var hourList)
            ? hourList.EnumerateArray().Select(h => h.GetDouble()).ToArray()
            : new[] { 7.0, 12.0, 16.5, 21.0 };
        var cell = new Vector2I(480, 270);
        var sheet = Image.CreateEmpty(cell.X * hours.Length, cell.Y * dates.Length, false, Image.Format.Rgb8);
        for (var row = 0; row < dates.Length; row++)
            for (var column = 0; column < hours.Length; column++)
            {
                SetClock(hours[column], DayOfYear(dates[row].Item2));
                for (var i = 0; i < 20; i++) await NextFrame();
                var image = Grab();
                image.Resize(cell.X, cell.Y, Image.Interpolation.Lanczos);
                sheet.BlitRect(image, new Rect2I(Vector2I.Zero, cell), new Vector2I(column * cell.X, row * cell.Y));
            }
        sheet.SavePng(System.IO.Path.Combine(outDir, $"sweep_{cameraId}.png"));
        var clock = root.GetProperty("clock");
        SetClock(clock.GetProperty("hour").GetDouble(), DayOfYear(clock.GetProperty("date").GetString()!));
        GD.Print($"LOOK_CAPTURE sweep={cameraId} rows={string.Join(",", dates.Select(d => d.Item1))} columns={string.Join(",", hours.Select(h => $"{(int)h:00}:{(int)Math.Round(h % 1 * 60):00}"))}");
    }

    /// <summary>
    /// The post effect, checked on the GPU: the first camera rendered with the effect and without it. With it, the
    /// corners must be darker relative to the centre (the vignette) and neighbouring pixels must differ by a fixed
    /// pattern (the grain). Older commits without the effect report that and are not judged.
    /// </summary>
    private async Task<Dictionary<string, object>> PostEffectCheck(JsonElement root)
    {
        var result = new Dictionary<string, object>();
        var post = _look != null && _look.HasMethod("SelfCheck") ? _look.Get("Post").AsGodotObject() as CompositorEffect : null;
        if (post == null)
        {
            result["ran"] = false;
            result["note"] = "this build has no grain and vignette effect to check";
            return result;
        }
        Frame(root.GetProperty("cameras")[0]);
        post.Enabled = true;
        for (var i = 0; i < 30; i++) await NextFrame();
        var with = Grab();
        post.Enabled = false;
        for (var i = 0; i < 30; i++) await NextFrame();
        var without = Grab();
        post.Enabled = true;
        for (var i = 0; i < 5; i++) await NextFrame();
        var w = with.GetWidth();
        var h = with.GetHeight();
        var patch = Math.Max(8, h / 12);
        double Mean(Image image, int x0, int y0)
        {
            double sum = 0;
            for (var y = y0; y < y0 + patch; y++)
                for (var x = x0; x < x0 + patch; x++)
                {
                    var c = image.GetPixel(x, y);
                    sum += 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
                }
            return sum / (patch * patch) + 1e-4;
        }
        var corners = new[] { (0, 0), (w - patch, 0), (0, h - patch), (w - patch, h - patch) };
        var cornerRatio = corners.Average(c => Mean(with, c.Item1, c.Item2) / Mean(without, c.Item1, c.Item2));
        var centreRatio = Mean(with, w / 2 - patch / 2, h / 2 - patch / 2) / Mean(without, w / 2 - patch / 2, h / 2 - patch / 2);
        // Grain: the per-pixel ratio between the two images, correlated with the pattern the shader is known to
        // multiply in (LookPostEffect.Factor, found by reflection so this file still builds against older commits).
        // Tone mapping, 8-bit output and TAA shrink a 5% grain to well under one 8-bit level, so its spread alone
        // cannot be told from frame-to-frame noise; the correlation can (noise alone gives about 1/sqrt(pixels)).
        var factor = post.GetType().GetMethod("Factor", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var pushConstants = post.GetType().GetMethod("PushConstants")?.Invoke(post, new object[] { new Vector2I(w, h) }) as float[];
        var measured = new List<double>();
        var expected = new List<double>();
        for (var y = h / 2 - patch / 2; y < h / 2 + patch / 2; y++)
            for (var x = w / 2 - patch / 2; x < w / 2 + patch / 2; x++)
            {
                var a = with.GetPixel(x, y);
                var b = without.GetPixel(x, y);
                var lb = 0.2126 * b.R + 0.7152 * b.G + 0.0722 * b.B;
                if (lb <= 0.08) continue;
                measured.Add((0.2126 * a.R + 0.7152 * a.G + 0.0722 * a.B) / lb);
                expected.Add(factor != null && pushConstants != null ? (float)factor.Invoke(null, new object[] { pushConstants, new Vector2I(x, y) })! : 1.0);
            }
        var spread = Spread(measured);
        var correlation = Correlation(measured, expected);
        var vignetteSeen = centreRatio - cornerRatio;
        result["ran"] = true;
        result["corner_to_centre_darkening"] = Round(vignetteSeen);
        result["grain_spread"] = Round(spread);
        result["grain_correlation"] = Round(correlation);
        result["noise_correlation_scale"] = Round(measured.Count > 0 ? 1.0 / Math.Sqrt(measured.Count) : 1.0);
        result["centre_pixels_compared"] = measured.Count;
        // The vignette (0.15 in the preset) darkens the corners about 5% after tone mapping; the grain must
        // correlate with its known pattern far above the noise level.
        result["passed"] = vignetteSeen > 0.02 && measured.Count > 1000 && correlation > 8.0 / Math.Sqrt(measured.Count);
        return result;
    }

    private Dictionary<string, object>? _postCheck;

    private static double Spread(List<double> values)
    {
        if (values.Count < 2) return 0.0;
        var mean = values.Average();
        return Math.Sqrt(values.Average(v => (v - mean) * (v - mean)));
    }

    private static double Correlation(List<double> a, List<double> b)
    {
        if (a.Count < 2 || a.Count != b.Count) return 0.0;
        var ma = a.Average();
        var mb = b.Average();
        double sab = 0, saa = 0, sbb = 0;
        for (var i = 0; i < a.Count; i++)
        {
            sab += (a[i] - ma) * (b[i] - mb);
            saa += (a[i] - ma) * (a[i] - ma);
            sbb += (b[i] - mb) * (b[i] - mb);
        }
        return saa <= 0 || sbb <= 0 ? 0.0 : sab / Math.Sqrt(saa * sbb);
    }

    /// <summary>The look's self-check (renderer fallback, missing GI, bake stand-ins left, post effect never ran) and the pixel check.</summary>
    private List<string> LookProblems()
    {
        var problems = new List<string>();
        if (_look != null && _look.HasMethod("SelfCheck")) problems.AddRange(_look.Call("SelfCheck").AsStringArray());
        if (_postCheck != null && _postCheck.TryGetValue("passed", out var passed) && passed is false)
            problems.Add($"the grain and vignette effect did not show in the pixels (corner darkening {_postCheck["corner_to_centre_darkening"]}, grain correlation {_postCheck["grain_correlation"]} against a noise scale of {_postCheck["noise_correlation_scale"]})");
        return problems;
    }

    private void WriteReport(string outDir, string camerasPath, Vector2I resolution, int warmup, int frames, List<string> problems)
    {
        var report = new Dictionary<string, object?>
        {
            ["label"] = Arg("label", ""),
            ["commit"] = Arg("commit", ""),
            ["note"] = Arg("note", ""),
            ["captured_utc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["engine"] = Engine.GetVersionInfo()["string"].AsString(),
            ["rendering_method"] = RenderingServer.GetCurrentRenderingMethod(),
            ["rendering_driver"] = RenderingServer.GetCurrentRenderingDriverName(),
            ["adapter"] = RenderingServer.GetVideoAdapterName(),
            ["adapter_driver"] = string.Join(" ", OS.GetVideoAdapterDriverInfo()),
            ["target"] = _target != null ? "offscreen SubViewport (window shows a scaled preview)" : "window root viewport, borderless at the screen origin",
            ["resolution"] = new[] { resolution.X, resolution.Y },
            ["vsync"] = "disabled",
            ["warmup_frames"] = warmup,
            ["measured_frames"] = frames,
            ["style"] = _world.HasMeta("style") ? _world.GetMeta("style").AsString() : "",
            ["style_note"] = _world.StyleNote,
            ["room_manifest_sha256"] = _world.Room.ManifestSha256,
            ["cameras_file"] = camerasPath,
            ["msaa_3d"] = (_target != null ? _target.Msaa3D : GetViewport().Msaa3D).ToString(),
            ["screen_space_aa"] = (_target != null ? _target.ScreenSpaceAA : GetViewport().ScreenSpaceAA).ToString(),
            ["use_taa"] = _target != null ? _target.UseTaa : GetViewport().UseTaa,
            ["cameras"] = _results,
        };
        if (_look != null && _look.HasMethod("DescribeLook")) report["look"] = _look.Call("DescribeLook").AsString();
        report["look_problems"] = problems;
        if (_postCheck != null) report["post_effect_check"] = _postCheck;
        if (_look != null && _look.HasMethod("SelfCheck")) report["player_notice"] = _look.Get("PlayerNotice").AsString();
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, "timings.json"), new UTF8Encoding(false).GetBytes(json.Replace("\r\n", "\n") + "\n"));
    }
}
