using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Ui;

namespace EnFractal.Tests.Kernel;

public partial class PlayHudTest
{
    private async Task TestFireAndJournal()
    {
        var host = CommandHost.Of(_world)!;
        var player = _world.Player;
        player.TryTeleportTo(new Vector3(0, 0.02f, 0.8f));
        player.Rotation = Vector3.Zero;
        await Frames(10);
        var revision = host.Revision;
        _hud._UnhandledInput(Press(Key.Key6));
        Check(host.ActiveFireCount == 1, "6 places a visible flame through the command host");
        _hud._UnhandledInput(Press(Key.Key7));
        Check(host.ActiveFireCount == 2 && host.Revision == revision, "7 casts a firebolt; effects are transient, leaving the saved revision alone");
        _hud._UnhandledInput(Press(Key.Key8));
        Check(host.ActiveFireCount == 0, "8 douses all fire immediately");

        JsonObject Cast(string id, bool bolt = false, bool companion = false) => new()
        {
            ["schema"] = "enfractal.command", ["version"] = 1, ["action_id"] = id, ["room_id"] = host.Room.RoomId, ["op"] = "effect.start",
            ["args"] = new JsonObject { ["capability"] = bolt ? "firebolt" : "flame", ["duration_s"] = bolt ? 2 : 12,
                ["params"] = bolt ? new JsonObject { ["direction_x"] = 0, ["direction_y"] = 0, ["direction_z"] = -1 } : new JsonObject(),
                ["area"] = new JsonObject { ["center_m"] = KernelJson.Vector((companion ? _world.Companion : player).EyeCamera.GlobalPosition + Vector3.Forward * 0.05f), ["radius_m"] = 0.04 } }
        };
        JsonObject Send(JsonObject command, string principal = CommandHost.PlayerPrincipal) => host.HandleObject(command.ToJsonString(), principal);
        bool Ok(JsonObject result) => result["ok"]?.GetValue<bool>() == true;
        string? Code(JsonObject result) => result["error"]?["code"]?.GetValue<string>();
        var command = Cast("fire-replay");
        var preview = (JsonObject)command.DeepClone(); preview["preview"] = true;
        Check(Ok(Send(preview)) && host.ActiveFireCount == 0, "preview validates fire without lighting it");
        var first = Send(command);
        Check(Ok(first) && Ok(Send(command)) && host.ActiveFireCount == 1, "replaying one cast never duplicates a flame");
        command["args"]!["duration_s"] = 11;
        Check(Code(Send(command)) == "action_id_conflict", "reusing a cast id with changed contents conflicts");
        for (var i = 1; i < CommandHost.FireLimit; i++) Check(Ok(Send(Cast("fire-budget-" + i))), "a cast within the room-wide fire budget is admitted");
        Check(Code(Send(Cast("fire-too-many"))) == "budget_exceeded" && host.ActiveFireCount == CommandHost.FireLimit, "splitting fire into many commands cannot evade the room budget");
        var stop = new JsonObject { ["schema"] = "enfractal.command", ["version"] = 1, ["room_id"] = host.Room.RoomId,
            ["action_id"] = "fire-stop", ["expected_revision"] = 0, ["op"] = "effect.stop", ["args"] = new JsonObject { ["effect"] = "all" } };
        Check(Ok(Send(stop)) && host.ActiveFireCount == 0, "a stale revision never prevents dousing fire");
        var invalid = Cast("fire-long"); invalid["args"]!["duration_s"] = 100;
        Check(Code(Send(invalid)) == "invalid_args", "long-lived fire is refused");
        invalid = Cast("fire-big"); invalid["args"]!["area"]!["radius_m"] = 1;
        Check(Code(Send(invalid)) == "invalid_args", "oversized fire is refused");
        invalid = Cast("fire-far"); invalid["args"]!["area"]!["center_m"] = new JsonArray(900, 0, 900);
        Check(Code(Send(invalid)) == "out_of_bounds", "remote or off-room fire is refused");
        invalid = Cast("fire-unknown"); invalid["args"]!["params"]!["damage"] = 100;
        Check(Code(Send(invalid)) == "field_unknown", "fire cannot smuggle in damage or extra parameters");
        invalid = Cast("fire-direction", true); invalid["args"]!["params"]!["direction_z"] = 0;
        Check(Code(Send(invalid)) == "invalid_args", "a zero firebolt direction is refused");
        invalid = Cast("fire-principal"); invalid["args"]!["params"]!["principal"] = CommandHost.PlayerPrincipal;
        Check(Code(Send(invalid, CommandHost.CompanionPrincipal)) == "request_invalid", "identity cannot travel inside fire parameters");
        var playerCast = Cast("fire-owned-player"); Check(Ok(Send(playerCast)), "player flame created for ownership check");
        var companionCast = Cast("fire-owned-companion", companion: true);
        Check(Ok(Send(companionCast, CommandHost.CompanionPrincipal)), "the companion casts through the same effect.start command");
        Check(Ok(Send(stop, CommandHost.CompanionPrincipal)) && host.ActiveFireCount == 1, "the companion can stop only its own flames");
        Check(Ok(Send(stop)) && host.ActiveFireCount == 0, "the player can stop everyone's fire");
        var shortCast = Cast("fire-expiry"); shortCast["args"]!["duration_s"] = 0.1;
        Check(Ok(Send(shortCast)), "short flame created");
        await Frames(20);
        Check(host.ActiveFireCount == 0, "fire expires without another command");

        // Place a real wall directly in the bolt path; physics must stop it well before its lifetime.
        var wall = new StaticBody3D { Position = player.EyeCamera.GlobalPosition + Vector3.Forward * 0.18f, CollisionLayer = 1 };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.15f, 0.15f, 0.01f) } }); _world.AddChild(wall);
        await Frames(2);
        invalid = Cast("fire-wall"); invalid["args"]!["area"]!["center_m"] = KernelJson.Vector(player.EyeCamera.GlobalPosition + Vector3.Forward * 0.3f);
        Check(Code(Send(invalid)) == "occupied", "a flame cannot be placed through a wall");
        Check(Ok(Send(Cast("fire-hit-wall", true))), "firebolt starts before the wall");
        await Frames(20);
        Check(host.ActiveFireCount == 0, "a moving firebolt stops at world geometry");
        wall.QueueFree();

        _hud._UnhandledInput(Press(Key.J));
        Check(_hud.JournalOpen && !player.InputEnabled && Input.MouseMode == Input.MouseModeEnum.Visible, "J opens the paper journal and releases movement and mouse look");
        _hud._UnhandledInput(Press(Key.Key7));
        Check(host.ActiveFireCount == 0, "gameplay hotkeys cannot cast while reading the journal");
        var panel = (JournalPanel)_hud.FindChild("JournalPanel", true, false)!;
        Check(panel.Page == "Index", "the journal always opens at its index");
        await Frames(70);
        await CaptureFireJournal("index");
        panel.OpenPage("Abilities"); await CaptureFireJournal("abilities");
        panel.OpenPage("Character"); await CaptureFireJournal("character");
        panel.OpenPage("Map");
        Check(panel.MapView != null && panel.MapView.KnownObjectCount == host.ExportDiscovered()["entities"]!.AsObject().Count,
            "the map draws only the team's discovered entities");
        await CaptureFireJournal("map");
        panel.OpenPage("Discoveries"); await CaptureFireJournal("discoveries");
        panel.OpenPage("Notes & memories");
        var input = (LineEdit)panel.FindChild("JournalNote", true, false)!;
        input.Text = "[b]Remember the reef[/b]"; panel.SaveNote();
        Check(panel.Page == "Notes & memories" && input.Text == "", "saving a note moves to the memories and clears the editor only on success");
        var notes = host.PlayerJournal()["data"]!["entries"]!.AsArray();
        Check(notes.Any(n => n?["text"]?.GetValue<string>() == "[b]Remember the reef[/b]" && n["author"]?.GetValue<string>() == CommandHost.PlayerPrincipal), "the UI note is in the host's journal, attributed to the player");
        Check(panel.FindChildren("*", "Label", true, false).OfType<Label>().Any(l => l.Text.Contains("[b]Remember the reef[/b]")), "note markup is displayed literally, not interpreted");
        await CaptureFireJournal("journal");
        input.Text = ""; panel.SaveNote();
        Check(((Label)panel.FindChild("JournalStatus", true, false)!).Text != "Your note is saved.", "invalid notes give visible feedback");
        _hud._UnhandledInput(Press(Key.Escape));
        Check(_hud.JournalOpen && !player.InputEnabled, "Escape keeps gameplay blocked while the cover closes");
        await Frames(60);
        Check(!_hud.JournalOpen && player.InputEnabled, "closing animation restores player input at the dock");
        var presentation = (JournalPresentation)_hud.FindChild("JournalPresentation", true, false)!;
        presentation.Dock.EmitSignal(Button.SignalName.Pressed);
        Check(panel.Page == "Index", "reopening the journal returns to the index from another section");
        Check(_hud.JournalOpen && !player.InputEnabled, "the book icon opens the journal and locks gameplay");
        await Frames(15);
        _hud._UnhandledInput(Press(Key.Escape));
        await Frames(5);
        _hud._UnhandledInput(Press(Key.J));
        await Frames(70);
        Check(presentation.Progress == 1 && panel.Visible && !presentation.Dock.Visible, "reversing a closing animation settles on one open book");
        _hud._UnhandledInput(Press(Key.Escape));
        await Frames(60);
        Check(presentation.Progress == 0 && presentation.Dock.Visible && !panel.Visible && player.InputEnabled, "the closed book returns to its dock after reversal");
        if (OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--capture-dir=")))
        {
            _hud.SetViewMode(1);
            _hud._UnhandledInput(Press(Key.Key6));
            await Frames(12);
            await CaptureFireJournal("fire");
            host.PlayerStopFire();
            _hud._UnhandledInput(Press(Key.Key7));
            await Frames(10);
            await CaptureFireJournal("firebolt");
            host.PlayerStopFire();
        }

        // Save representative requests and answers for the independent Python contract validator.
        var dump = ProjectSettings.GlobalizePath("user://tests/fire-contracts"); System.IO.Directory.CreateDirectory(dump);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dump, "command.json"), playerCast.ToJsonString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(dump, "result.json"), first.ToJsonString());
        var query = new JsonObject { ["schema"] = "enfractal.query", ["version"] = 1, ["query_id"] = "fire-capabilities", ["room_id"] = host.Room.RoomId, ["op"] = "capabilities.list", ["args"] = new JsonObject() };
        System.IO.File.WriteAllText(System.IO.Path.Combine(dump, "capabilities.json"), Send(query).ToJsonString());

        _world.QueueFree(); await Frames(3);
        _world = GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>(); AddChild(_world);
        for (var i = 0; i < 900 && !_world.WorldReady && _world.LoadError.Length == 0; i++) await Frames(1);
        host = CommandHost.Of(_world)!;
        Check(_world.WorldReady && host.PlayerJournal()["data"]!["entries"]!.AsArray().Any(n => n?["text"]?.GetValue<string>() == "[b]Remember the reef[/b]"), "a note written through the journal UI survives a complete room reload");
        Check(host.ActiveFireCount == 0, "transient fire never returns from a save");
    }

    private async Task CaptureFireJournal(string name)
    {
        var directory = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--capture-dir="))?[14..];
        if (directory == null || DisplayServer.GetName() == "headless") return;
        System.IO.Directory.CreateDirectory(directory);
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(System.IO.Path.Combine(directory, name + ".png")) == Error.Ok, "capture saved: " + name);
    }
}
