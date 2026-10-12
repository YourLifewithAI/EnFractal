using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Look;

namespace EnFractal.Tests.Look;

/// <summary>Real-island fire capture and lifecycle check. All casts use the player command adapter.</summary>
public partial class FireIslandTest : Node
{
    private int _checks;
    private void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); _checks++; GD.Print("PASS: " + message); }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            var room = args.First(a => a.StartsWith("--island="))[9..];
            var output = args.FirstOrDefault(a => a.StartsWith("--capture-dir="))?[14..];
            CommandHost.SaveRoot = "user://tests/fire_island/" + Guid.NewGuid().ToString("N");
            var world = GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
            world.RoomDirectory = room; AddChild(world);
            for (var i = 0; i < 900 && !world.WorldReady && world.LoadError.Length == 0; i++) await Frames(1);
            Check(world.WorldReady && world.Room.RoomId != "test_room", "the island loads: " + world.LoadError);
            var hud = world.GetNode<RoomHud>("RoomHud");
            hud.SetViewMode(1);
            for (var i = 0; i < 4; i++) hud._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.T, Pressed = true });
            // Turn away from the spawn cliff through the normal mouse-control path.
            Input.MouseMode = Input.MouseModeEnum.Captured;
            world.Player._UnhandledInput(new InputEventMouseMotion { Relative = new Vector2(628.3185f, 0) });
            Input.MouseMode = Input.MouseModeEnum.Visible;
            world.Player.ReadKeyboard = false;
            world.Player.SetControlInput(new Vector2(0, 1));
            await Frames(60); world.Player.SetControlInput(Vector2.Zero);
            await Frames(90);
            var host = CommandHost.Of(world)!;
            Check(host.PlayerFire(false)["ok"]!.GetValue<bool>(), "a flame can be placed on the island through the command host");
            await Frames(75);
            var visual = host.GetChildren().OfType<FireVisual>().Single();
            GD.Print($"FIRE_CAPTURE_ORIGIN {visual.GlobalPosition}, player {world.Player.GlobalPosition}");
            var plumes = visual.FindChildren("*", "GPUParticles3D", true, false).OfType<GpuParticles3D>().ToArray();
            Check(plumes.Length == 3 && plumes.Sum(p => p.Amount) == 76, "bounded flame, smoke and ember emitters are present");
            Check(plumes.All(p => p.Emitting && p.LocalCoords), "particles follow the effect and emit");
            Check(!visual.FindChildren("*", "MeshInstance3D", true, false).Any(), "the old flat polygon tongues are gone");
            async Task Capture(string name)
            {
                if (output == null || DisplayServer.GetName() == "headless") return;
                System.IO.Directory.CreateDirectory(output);
                await Frames(5); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                Check(image.SavePng(System.IO.Path.Combine(output, name + ".png")) == Error.Ok, "captured " + name);
            }
            await Capture("island-flame");
            var camera = new Camera3D { Near = 0.005f, Fov = 55 };
            AddChild(camera);
            var target = visual.GlobalPosition + Vector3.Up * 0.05f;
            var back = world.Player.GlobalBasis.Z;
            camera.GlobalPosition = target + back * 0.23f + world.Player.GlobalBasis.X * 0.085f + Vector3.Up * 0.025f;
            camera.LookAt(target); camera.MakeCurrent();
            world.Look.FrameCamera(camera, target);
            await Capture("island-flame-close");
            if (DisplayServer.GetName() != "headless")
            {
                var light = visual.GetChildren().OfType<OmniLight3D>().Single();
                light.Visible = false;
                await Frames(4); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var flameImage = GetViewport().GetTexture().GetImage();
                foreach (var p in plumes) p.Visible = false;
                await Frames(4); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var noFlameImage = GetViewport().GetTexture().GetImage();
                var visiblePixels = 0;
                for (var y = flameImage.GetHeight() / 4; y < flameImage.GetHeight() * 3 / 4; y++)
                    for (var x = flameImage.GetWidth() * 2 / 5; x < flameImage.GetWidth() * 3 / 5; x++)
                    {
                        var on = flameImage.GetPixel(x,y); var off = noFlameImage.GetPixel(x,y);
                        if (on.R - off.R > 0.15f && on.G - off.G > 0.03f) visiblePixels++;
                    }
                Check(visiblePixels > 200, $"the flame body renders without its light: {visiblePixels} bright pixels");
                foreach (var p in plumes) p.Visible = true;
                light.Visible = true;
            }
            await Frames(35); await Capture("island-flame-later");
            hud.SetViewMode(1); camera.QueueFree();
            host.PlayerStopFire(); await Frames(3);
            Check(host.ActiveFireCount == 0 && !GodotObject.IsInstanceValid(visual), "dousing frees every emitter with its host effect");
            Check(host.PlayerFire(true)["ok"]!.GetValue<bool>(), "a firebolt launches on the island");
            await Frames(14); await Capture("island-firebolt");
            await Frames(140); Check(host.ActiveFireCount == 0, "the firebolt expires or collides on the island");
            hud._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.J, Pressed = true });
            await Capture("island-journal");
            CommandHost.SaveRoot = CommandHost.DefaultSaveRoot;
            GD.Print($"FIRE_ISLAND: {_checks}/{_checks} checks passed"); GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}
