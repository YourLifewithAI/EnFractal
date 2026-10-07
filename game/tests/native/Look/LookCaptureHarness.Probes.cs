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
/// GPU probes of the capture harness (--probe=NAME): measurements that need a real renderer and are not review captures.
/// They write probe_NAME.json beside the captures and use only RoomWorld's public surface and the look through Godot's
/// property and method calls, so the same file runs against an older commit (tools/look/capture-look.ps1 -Baseline) and
/// before and after evidence comes from the same code.
///
///   rooms           the shapes captured rooms take: the contract's garage example, a room with a window and no sun hint, a
///                   single-sided wall, three windows: frame brightness, the sun, where the lights stand
///   window          the window seen from inside, by day and by night: the brightness of the pane
///   free-viewport   freeing a viewport that used the grain and vignette effect, and the look itself, while others render
///   shimmer         floorboards in motion under no anti-aliasing, FXAA, TAA and 4x MSAA: how much of the pattern flickers
/// </summary>
public partial class LookCaptureHarness
{
    private async Task RunProbe(string name, JsonElement root, string outDir)
    {
        var resolution = new Vector2I(root.GetProperty("resolution")[0].GetInt32(), root.GetProperty("resolution")[1].GetInt32());
        Engine.MaxFps = 0;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        var result = new Dictionary<string, object?> { ["probe"] = name, ["commit"] = Arg("commit", ""), ["captured_utc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) };
        switch (name)
        {
            case "rooms": result["rooms"] = await ProbeRooms(); break;
            case "window": result["window"] = await ProbeWindow(outDir); break;
            case "free-viewport": result["steps"] = await ProbeFreeViewport(outDir, resolution); break;
            case "shimmer": result["shimmer"] = await ProbeShimmer(outDir); break;
            default: throw new ArgumentException("unknown probe " + name);
        }
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, $"probe_{name}.json"), new UTF8Encoding(false).GetBytes(json.Replace("\r\n", "\n") + "\n"));
        GD.Print($"LOOK_PROBE_DONE {name} out={outDir}");
        GetTree().Quit(0);
    }

    // ---------- plumbing ----------

    private async Task<RoomWorld> LoadWorld(string roomDirectory)
    {
        var world = GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
        world.RoomDirectory = roomDirectory;
        if (Arg("style", "").Length > 0) world.StylePresetPath = Arg("style", "");
        AddChild(world);
        for (var i = 0; i < 600 && !world.WorldReady && world.LoadError.Length == 0; i++) await NextFrame();
        if (!world.WorldReady) throw new InvalidOperationException($"room {roomDirectory} did not load: {world.LoadError}");
        foreach (var layer in world.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = false;
        foreach (var label in world.FindChildren("*", "Label3D", true, false).OfType<Label3D>()) label.Visible = false;
        world.Player.ReadKeyboard = false;
        world.Player.SetInputEnabled(false);
        world.Companion.Stay();
        _world = world;
        _look = world.Look;
        return world;
    }

    private async Task UnloadWorld()
    {
        _world.QueueFree();
        await NextFrame();
        await NextFrame();
    }

    private void FreeTarget()
    {
        _target?.QueueFree();
        _target = null;
        GetNodeOrNull("Preview")?.QueueFree();
        GetViewport().Disable3D = false;
    }

    private static double MeanLuma(Image image, Rect2I? region = null, int step = 3)
    {
        var rect = region ?? new Rect2I(0, 0, image.GetWidth(), image.GetHeight());
        rect = rect.Intersection(new Rect2I(0, 0, image.GetWidth(), image.GetHeight()));
        double sum = 0;
        var count = 0;
        for (var y = rect.Position.Y; y < rect.End.Y; y += step)
            for (var x = rect.Position.X; x < rect.End.X; x += step)
            {
                sum += Luma(image.GetPixel(x, y));
                count++;
            }
        return count == 0 ? 0 : sum / count;
    }

    private async Task<Image> Shot(Vector3 position, Vector3 lookAt, float fov, Vector3 focus, int frames = 50)
    {
        _camera.GlobalPosition = position;
        _camera.LookAt(lookAt, Mathf.Abs((lookAt - position).Normalized().Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up);
        _camera.Fov = fov;
        _camera.Near = 0.005f;
        _camera.Far = 50f;
        _camera.CullMask = 0xFFFFFu & ~HiddenBodyLayer;
        if (_look != null && _look.HasMethod("FrameCamera")) _look.Call("FrameCamera", _camera, focus);
        for (var i = 0; i < frames; i++) await NextFrame();
        return Grab();
    }

    private static Dictionary<string, object?> Lights(RoomWorld world)
    {
        var look = world.Look;
        var info = new Dictionary<string, object?>();
        info["sun_scale"] = Math.Round(Convert.ToDouble(look.GetType().GetProperty("SunScale")?.GetValue(look) ?? 0f), 3);
        info["key_visible"] = ((Light3D)look.Get("Key").AsGodotObject()).Visible;
        info["key_energy"] = Math.Round(((Light3D)look.Get("Key").AsGodotObject()).LightEnergy, 3);
        var bounds = world.Room.Bounds.Grow(0.001f);
        var fills = new List<Dictionary<string, object?>>();
        foreach (var fill in (look.GetType().GetProperty("SkyFills")?.GetValue(look) as IEnumerable<SpotLight3D>) ?? Array.Empty<SpotLight3D>())
            fills.Add(new Dictionary<string, object?>
            {
                ["id"] = fill.GetMeta("light_hint_id").AsString(), ["shadow"] = fill.ShadowEnabled, ["inside_room"] = bounds.HasPoint(fill.GlobalPosition),
                ["position"] = new[] { Math.Round(fill.GlobalPosition.X, 2), Math.Round(fill.GlobalPosition.Y, 2), Math.Round(fill.GlobalPosition.Z, 2) },
            });
        info["sky_fills"] = fills;
        info["sky_fills_standing_outside_the_room"] = fills.Count(f => f["inside_room"] is false);
        return info;
    }

    // ---------- rooms ----------

    private async Task<List<Dictionary<string, object?>>> ProbeRooms()
    {
        var resolution = new Vector2I(960, 540);
        var results = new List<Dictionary<string, object?>>();
        var fixtures = new List<(string Label, string Directory, double Hour, int Day, Vector3 Camera, Vector3 Target, string Note)>
        {
            ("test_room", RoomWorld.DefaultRoom, 12, 196, new Vector3(-1.84f, 2.26f, 1.36f), new Vector3(0.15f, 0.05f, 0.25f), "the shipped room at noon in July, from the ceiling corner (the reference the review compared a captured room with)"),
            ("window_no_sun_hint", LookFixtureRooms.WindowWithoutSunHint(), 12, 196, new Vector3(-1.84f, 2.26f, 1.36f), new Vector3(0.15f, 0.05f, 0.25f), "the test room with its sun hint taken away: a window is still a way in for the sun (M1)"),
            ("captured_like", LookFixtureRooms.CapturedLike(), 12, 196, new Vector3(-1.84f, 2.26f, 1.36f), new Vector3(0.15f, 0.05f, 0.25f), "no sun hint, the window only an opening in a solid wall: a captured room's shape (M1 needs the builder to cut the opening too)"),
            ("single_sided_wall", LookFixtureRooms.SingleSidedWestWall(), 16.5, 105, new Vector3(1.8f, 2.0f, 1.2f), new Vector3(-1.0f, 0.1f, 0.0f), "the west wall one single-sided quad and no window, the sun outside at 16:30 on 15 April: it must stay out (M4)"),
            ("three_windows", LookFixtureRooms.ThreeWindows(), 12, 196, new Vector3(-1.84f, 2.26f, 1.36f), new Vector3(0.15f, 0.05f, 0.25f), "three windows against a budget of three shadowed lights (M3)"),
        };
        var garage = Arg("garage", "");
        if (garage.Length > 0)
            fixtures.Add(("garage_example", garage, 12, 196, new Vector3(-2.6f, 2.0f, 2.3f), new Vector3(0f, 0.6f, 0f), "the contract's captured-room example at noon in July (review M1: 0.06 against 0.58)"));
        foreach (var f in fixtures)
        {
            var world = await LoadWorld(f.Directory);
            var entry = new Dictionary<string, object?> { ["room"] = f.Label, ["note"] = f.Note };
            SetClock(f.Hour, f.Day);
            BuildTarget(resolution);
            for (var i = 0; i < 20; i++) await NextFrame();
            var image = await Shot(f.Camera, f.Target, 65f, f.Target);
            entry["mean_luma"] = Math.Round(MeanLuma(image), 4);
            entry["lights"] = Lights(world);
            // The sun's own share of the frame: the same view with the sun off.
            var key = (Light3D)_look!.Get("Key").AsGodotObject();
            var wasVisible = key.Visible;
            key.Visible = false;
            for (var i = 0; i < 30; i++) await NextFrame();
            var withoutSun = Grab();
            key.Visible = wasVisible;
            entry["mean_luma_without_sun"] = Math.Round(MeanLuma(withoutSun), 4);
            entry["sun_share_of_frame"] = Math.Round(MeanLuma(image) - MeanLuma(withoutSun), 4);
            // The sun on the floor: floor points whose line to the sun is open (by physics) against how much brighter the sun makes them.
            await NextFrame();
            if (key.Visible)
            {
                var toSun = key.GlobalBasis.Z.Normalized();
                var space = _world.GetWorld3D().DirectSpaceState;
                var bounds = world.Room.Bounds;
                var floorOpen = 0;
                var floorTotal = 0;
                for (var x = bounds.Position.X + 0.1f; x < bounds.End.X - 0.1f; x += 0.2f)
                    for (var z = bounds.Position.Z + 0.1f; z < bounds.End.Z - 0.1f; z += 0.2f)
                    {
                        floorTotal++;
                        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(x, 0.01f, z), new Vector3(x, 0.01f, z) + toSun * 20f));
                        if (hit.Count == 0) floorOpen++;
                    }
                entry["floor_points_with_open_line_to_sun"] = floorOpen;
                entry["floor_points_total"] = floorTotal;
            }
            image.SavePng(System.IO.Path.Combine(Arg("out", "."), $"probe_room_{f.Label}.png"));
            FreeTarget();
            await UnloadWorld();
            results.Add(entry);
            GD.Print($"LOOK_PROBE rooms {f.Label} mean_luma={entry["mean_luma"]} sun_share={entry["sun_share_of_frame"]}");
        }
        return results;
    }

    // ---------- the window seen from inside ----------

    private async Task<List<Dictionary<string, object?>>> ProbeWindow(string outDir)
    {
        var world = await LoadWorld(RoomWorld.DefaultRoom);
        BuildTarget(new Vector2I(1280, 720));
        for (var i = 0; i < 20; i++) await NextFrame();
        var results = new List<Dictionary<string, object?>>();
        // The opening: x = -2, 0.5 to 2.0 m up, z from -0.3 to 0.9 (the test room's west window).
        var corners = new[] { new Vector3(-2f, 0.5f, -0.3f), new Vector3(-2f, 0.5f, 0.9f), new Vector3(-2f, 2.0f, -0.3f), new Vector3(-2f, 2.0f, 0.9f) };
        foreach (var (label, hour, day) in new[] { ("noon_july", 12.0, 196), ("golden_hour_april", 17.4, 105), ("overcast_winter_noon", 12.0, 15), ("night", 2.0, 279) })
        {
            SetClock(hour, day);
            var image = await Shot(new Vector3(1.2f, 1.1f, 0.3f), new Vector3(-2f, 1.25f, 0.3f), 62f, new Vector3(-1.0f, 1.0f, 0.3f), 40);
            var points = corners.Select(c => _camera.UnprojectPosition(c)).ToArray();
            var inset = 0.12f;
            var minX = points.Min(p => p.X); var maxX = points.Max(p => p.X); var minY = points.Min(p => p.Y); var maxY = points.Max(p => p.Y);
            var rect = new Rect2I((int)(minX + (maxX - minX) * inset), (int)(minY + (maxY - minY) * inset), (int)((maxX - minX) * (1 - 2 * inset)), (int)((maxY - minY) * (1 - 2 * inset)));
            var sum = new Color(0, 0, 0);
            var count = 0;
            for (var y = rect.Position.Y; y < rect.End.Y; y += 4)
                for (var x = rect.Position.X; x < rect.End.X; x += 4)
                {
                    var c = image.GetPixel(Mathf.Clamp(x, 0, image.GetWidth() - 1), Mathf.Clamp(y, 0, image.GetHeight() - 1));
                    sum += c;
                    count++;
                }
            var mean = new Color(sum.R / count, sum.G / count, sum.B / count);
            results.Add(new Dictionary<string, object?>
            {
                ["moment"] = label, ["pane_mean_luma"] = Math.Round(Luma(mean), 4), ["pane_mean_rgb"] = new[] { Math.Round(mean.R, 3), Math.Round(mean.G, 3), Math.Round(mean.B, 3) },
                ["pane_hex"] = "#" + mean.ToHtml(false), ["frame_mean_luma"] = Math.Round(MeanLuma(image), 4),
            });
            image.SavePng(System.IO.Path.Combine(outDir, $"probe_window_{label}.png"));
            GD.Print($"LOOK_PROBE window {label} pane_luma={results[^1]["pane_mean_luma"]} #{mean.ToHtml(false)}");
        }
        return results;
    }

    // ---------- freeing a viewport that used the grain and vignette effect (review M8) ----------

    private async Task<List<string>> ProbeFreeViewport(string outDir, Vector2I resolution)
    {
        var steps = new List<string>();
        var progress = System.IO.Path.Combine(outDir, "probe_free-viewport.progress.txt");
        System.IO.File.WriteAllText(progress, "");
        void Step(string text)
        {
            steps.Add(text);
            System.IO.File.AppendAllText(progress, text + "\n");
            GD.Print("LOOK_PROBE free-viewport " + text);
        }
        await LoadWorld(RoomWorld.DefaultRoom);
        BuildTarget(new Vector2I(960, 540));
        await Shot(new Vector3(-1.84f, 2.26f, 1.36f), new Vector3(0.15f, 0.05f, 0.25f), 60f, new Vector3(0f, 0.06f, 0.6f), 30);
        Step("main review target renders with the effect");

        SubViewport Extra(string name, Vector2I size)
        {
            var viewport = new SubViewport { Name = name, Size = size, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(viewport);
            var camera = new Camera3D { Name = name + "Camera", Fov = 60f };
            viewport.AddChild(camera);
            camera.GlobalPosition = new Vector3(1.5f, 1.4f, 1.2f);
            camera.LookAt(new Vector3(0f, 0.2f, 0.4f));
            camera.Current = true;
            return viewport;
        }

        // 1. A second viewport sharing the world is freed while the first keeps rendering.
        var second = Extra("SecondTarget", new Vector2I(640, 360));
        for (var i = 0; i < 30; i++) await NextFrame();
        Step("a second viewport rendered with the effect");
        second.Free();
        for (var i = 0; i < 30; i++) await NextFrame();
        Step("the second viewport was freed (immediately) and the first kept rendering");

        // 2. Several viewports freed through the queue, in the middle of rendering.
        var several = Enumerable.Range(0, 4).Select(i => Extra("Many" + i, new Vector2I(480 + 32 * i, 270))).ToArray();
        for (var i = 0; i < 20; i++) await NextFrame();
        foreach (var viewport in several) viewport.QueueFree();
        for (var i = 0; i < 20; i++) await NextFrame();
        Step("four viewports were freed through the queue");

        // 3. A new viewport after all that: the effect must still be usable.
        var third = Extra("ThirdTarget", new Vector2I(800, 450));
        for (var i = 0; i < 30; i++) await NextFrame();
        var post = _look!.Get("Post").AsGodotObject();
        Step($"a new viewport rendered afterwards (post effect ran: {post?.GetType().GetProperty("Ran")?.GetValue(post)}, error '{post?.GetType().GetProperty("Error")?.GetValue(post)}')");

        // 4. The main target is freed while the third keeps rendering.
        FreeTarget();
        for (var i = 0; i < 30; i++) await NextFrame();
        Step("the review target (the one that measured the effect) was freed while another viewport rendered");

        // 5. Resize and free in the same frame, repeatedly.
        for (var round = 0; round < 12; round++)
        {
            var burst = Extra("Burst" + round, new Vector2I(320 + round * 16, 180));
            await NextFrame();
            if (round % 2 == 0) burst.Size = new Vector2I(400, 225);
            burst.QueueFree();
            await NextFrame();
        }
        Step("twelve viewports were created, resized and freed within a frame or two");

        // 6. The look is taken out of the tree while a viewport still renders with its compositor.
        third.Size = new Vector2I(512, 288);
        await NextFrame();
        _look!.GetParent().RemoveChild(_look);
        for (var i = 0; i < 20; i++) await NextFrame();
        Step("the look was removed from the tree while the third viewport rendered");
        _look.Free();
        for (var i = 0; i < 20; i++) await NextFrame();
        Step("the look was freed");
        // The garbage collector finalizes what the look dropped (its compositor effect) on its own thread, as it does in a
        // running game after a room unloads: freeing GPU objects from there is what used to fail.
        _look = null;
        for (var round = 0; round < 3; round++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await NextFrame();
        }
        Step("the garbage collector finalized what the look dropped");
        third.QueueFree();
        for (var i = 0; i < 10; i++) await NextFrame();
        Step("the last viewport was freed");
        await UnloadWorld();
        Step("the room was unloaded");
        return steps;
    }

    // ---------- floorboards in motion (shimmer under FXAA) ----------

    private async Task<List<Dictionary<string, object?>>> ProbeShimmer(string outDir)
    {
        await LoadWorld(RoomWorld.DefaultRoom);
        var window = GetViewport();
        var results = new List<Dictionary<string, object?>>();
        var configurations = new (string Label, Viewport.ScreenSpaceAAEnum Aa, bool Taa, Viewport.Msaa Msaa, double Glide)[]
        {
            ("no anti-aliasing", Viewport.ScreenSpaceAAEnum.Disabled, false, Viewport.Msaa.Disabled, 0.0006),
            ("FXAA (the project's setting)", Viewport.ScreenSpaceAAEnum.Fxaa, false, Viewport.Msaa.Disabled, 0.0006),
            ("TAA (the old setting)", Viewport.ScreenSpaceAAEnum.Disabled, true, Viewport.Msaa.Disabled, 0.0006),
            ("MSAA 4x", Viewport.ScreenSpaceAAEnum.Disabled, false, Viewport.Msaa.Msaa4X, 0.0006),
            ("FXAA, camera still (the noise floor)", Viewport.ScreenSpaceAAEnum.Fxaa, false, Viewport.Msaa.Disabled, 0.0),
            ("FXAA, a faster glide (3 px a frame)", Viewport.ScreenSpaceAAEnum.Fxaa, false, Viewport.Msaa.Disabled, 0.0026),
        };
        SetClock(16.5, 279);
        // The grain is fixed to the screen (so anti-aliasing keeps it) and the floor moves under it, so with the grain on a
        // moving pattern never matches its last frame; the measurement is of the floor's own flicker, so the effect is off.
        if (_look?.Get("Post").AsGodotObject() is CompositorEffect post) post.Enabled = false;
        foreach (var (label, aa, taa, msaa, glide) in configurations)
        {
            BuildTarget(new Vector2I(1920, 1080));
            _target!.ScreenSpaceAA = aa;
            _target.UseTaa = taa;
            _target.Msaa3D = msaa;
            for (var i = 0; i < 30; i++) await NextFrame();
            // A camera gliding sideways over the floor at 0.6 mm a frame (a little under a pixel), the planks in the crisp band.
            var origin = new Vector3(-0.4f, 0.55f, 1.1f);
            var focus = new Vector3(0f, 0.0f, 0.2f);
            var frames = new List<float[]>();
            var patch = 200;
            Vector2I? centre = null;
            var steps = 90;
            for (var step = 0; step < steps; step++)
            {
                _camera.GlobalPosition = origin + new Vector3((float)(glide * step), 0f, 0f);
                _camera.LookAt(focus + new Vector3((float)(glide * step), 0f, 0f), Vector3.Up);
                _camera.Fov = 50f;
                _camera.Near = 0.005f;
                _camera.Far = 50f;
                if (step == 0 && _look != null && _look.HasMethod("FrameCamera")) _look.Call("FrameCamera", _camera, focus);
                await NextFrame();
                await NextFrame();
                var image = Grab();
                centre ??= new Vector2I((int)_camera.UnprojectPosition(focus).X, (int)_camera.UnprojectPosition(focus).Y);
                if (step < 6) continue;
                var data = new float[patch * patch];
                for (var y = 0; y < patch; y++)
                    for (var x = 0; x < patch; x++)
                        data[y * patch + x] = (float)Luma(image.GetPixel(Mathf.Clamp(centre.Value.X - patch / 2 + x, 0, image.GetWidth() - 1), Mathf.Clamp(centre.Value.Y - patch / 2 + y, 0, image.GetHeight() - 1)));
                frames.Add(data);
                if (step == 6 && glide > 0.0005 && glide < 0.001) image.SavePng(System.IO.Path.Combine(outDir, $"probe_shimmer_{label.Split(' ')[0].ToLowerInvariant()}.png"));
            }
            var (mean, p95, contrast) = ShimmerScore(frames, patch);
            results.Add(new Dictionary<string, object?>
            {
                ["configuration"] = label, ["glide_m_per_frame"] = glide, ["frames"] = frames.Count, ["residual_rms_mean"] = Math.Round(mean, 5), ["residual_rms_p95"] = Math.Round(p95, 5),
                ["patch_contrast_std"] = Math.Round(contrast, 4), ["residual_over_contrast"] = Math.Round(mean / Math.Max(contrast, 1e-6), 4),
            });
            GD.Print($"LOOK_PROBE shimmer {label}: residual {mean:0.00000} (over contrast {mean / Math.Max(contrast, 1e-6):0.0000})");
            FreeTarget();
            await NextFrame();
            await NextFrame();
        }
        window.Disable3D = false;
        return results;
    }

    /// <summary>
    /// How much a gliding pattern flickers instead of just moving. For each pair of frames the second is compared with the
    /// first shifted by the best sub-pixel offset (found by search); what no shift explains is flicker. Returns the mean and
    /// 95th percentile of the residual's RMS over the frame pairs, and the patch's own contrast for scale.
    /// </summary>
    private static (double Mean, double P95, double Contrast) ShimmerScore(List<float[]> frames, int size)
    {
        double Sample(float[] data, double x, double y)
        {
            var x0 = (int)Math.Floor(x); var y0 = (int)Math.Floor(y);
            var fx = x - x0; var fy = y - y0;
            double At(int px, int py) => data[Math.Clamp(py, 0, size - 1) * size + Math.Clamp(px, 0, size - 1)];
            return (At(x0, y0) * (1 - fx) + At(x0 + 1, y0) * fx) * (1 - fy) + (At(x0, y0 + 1) * (1 - fx) + At(x0 + 1, y0 + 1) * fx) * fy;
        }
        double Rms(float[] a, float[] b, double dx, double dy)
        {
            double sum = 0;
            var count = 0;
            for (var y = 24; y < size - 24; y += 2)
                for (var x = 24; x < size - 24; x += 2)
                {
                    var d = b[y * size + x] - Sample(a, x - dx, y - dy);
                    sum += d * d;
                    count++;
                }
            return Math.Sqrt(sum / count);
        }
        var residuals = new List<double>();
        for (var t = 1; t < frames.Count; t++)
        {
            double best = double.MaxValue, bx = 0, by = 0;
            for (var dx = -3.0; dx <= 3.0; dx += 0.25)
                for (var dy = -3.0; dy <= 3.0; dy += 0.25)
                {
                    var r = Rms(frames[t - 1], frames[t], dx, dy);
                    if (r < best) { best = r; bx = dx; by = dy; }
                }
            for (var dx = bx - 0.25; dx <= bx + 0.25; dx += 0.05)
                for (var dy = by - 0.25; dy <= by + 0.25; dy += 0.05)
                    best = Math.Min(best, Rms(frames[t - 1], frames[t], dx, dy));
            residuals.Add(best);
        }
        residuals.Sort();
        var all = frames[0];
        var mean = all.Average();
        var contrast = Math.Sqrt(all.Average(v => (v - mean) * (v - mean)));
        return (residuals.Average(), residuals[(int)(residuals.Count * 0.95)], contrast);
    }
}
