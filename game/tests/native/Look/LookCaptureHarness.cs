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
/// User arguments (after "--"): --cameras=PATH --out=DIR [--label=TEXT] [--warmup=N] [--frames=N]
/// [--only=ID,ID] [--sweep] [--root-viewport] [--commit=TEXT] [--note=TEXT]
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
            WriteReport(outDir, camerasPath, resolution, warmup, frames);
            GD.Print($"LOOK_CAPTURE_DONE cameras={_results.Count} out={outDir}");
            GetTree().Quit(0);
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
        var cameraId = Arg("sweep-camera", "ceiling_corner");
        var entry = root.GetProperty("cameras").EnumerateArray().First(c => c.GetProperty("id").GetString() == cameraId);
        Frame(entry);
        var dates = new[] { ("winter", "2026-01-15"), ("spring", "2026-04-15"), ("summer", "2026-07-15"), ("autumn", "2026-10-15") };
        var hours = new[] { 7.0, 12.0, 16.5, 21.0 };
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
        GD.Print($"LOOK_CAPTURE sweep={cameraId} rows=winter,spring,summer,autumn columns=07:00,12:00,16:30,21:00");
    }

    private void WriteReport(string outDir, string camerasPath, Vector2I resolution, int warmup, int frames)
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
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, "timings.json"), new UTF8Encoding(false).GetBytes(json.Replace("\r\n", "\n") + "\n"));
    }
}
