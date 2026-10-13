using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Ui;
using Godot;

namespace EnFractal.Tests.Kernel;

/// <summary>
/// The journal book (Codex's leather journal of 12 October, ported as the game's one journal), headless in the real room: it
/// docks at the lower right, J or a click opens it at the index and J, Esc or its link close it, a reversal midway always
/// reopens at the index, it holds every gameplay key, the Gubble's ask, wheel and the look drag while it is open or moving, a
/// later Space never reopens it, every page is written from the host's data, nothing undiscovered shows, and a note written in
/// it survives a reload of the room.
/// </summary>
public partial class PlayHudTest
{
    /// <summary>Frames for an open page to be read again from the host (JournalPanel.RefreshSeconds at the suite's 60 fps), and a few more.</summary>
    private static readonly int JournalRefreshFrames = (int)Math.Ceiling(JournalPanel.RefreshSeconds * 60) + 6;
    /// <summary>Frames for the book to open (0.95 s) or close (0.8 s) completely, and a few more.</summary>
    private const int JournalMoveFrames = 66;
    private const string JournalNoteText = "[b]Remember the reef[/b]";

    private async Task TestJournalBook()
    {
        var host = CommandHost.Of(_world)!;
        var player = _world.Player;
        var book = _hud.Book;
        var panel = _hud.Journal;
        player.ReadKeyboard = false;
        _hud.LightForTests = _ => 0.9f;
        _hud.AimRayForTests = Air();
        _hud.SetViewMode(1);
        host.PlayerGoal("stay");
        await Frames(2);

        // The dock: 18 px from the lower-right corner, the help footer stopping short of it, never focusable.
        var dock = book.Dock.GetGlobalRect();
        var screen = _hud.GetViewport().GetVisibleRect();
        var footer = _hud.GetNode<PanelContainer>("HelpFooter").GetGlobalRect();
        Check(book.Dock.Visible && !_hud.JournalOpen && Mathf.Abs(screen.End.X - dock.End.X - 18) < 1 && Mathf.Abs(screen.End.Y - dock.End.Y - 18) < 1 && footer.End.X < dock.Position.X,
            $"the closed book rests 18 px from the lower-right corner, the help footer stopping short of it ({dock} in {screen}, footer to {footer.End.X})");
        Check(book.Dock.FocusMode == Control.FocusModeEnum.None && book.Dock.TooltipText == "Open the journal · J",
            "the dock can never take the keyboard focus, and names its key from the input map: " + book.Dock.TooltipText);

        // J opens it at the index, and from that first frame the book holds the controls.
        _hud._UnhandledInput(Press(Key.J));
        Check(_hud.JournalOpen && book.WantsOpen && panel.Page == JournalPanel.IndexPage && !player.InputEnabled && Input.MouseMode == Input.MouseModeEnum.Visible,
            "J opens the journal at its index and holds the body and frees the cursor at once");
        await Frames(15);
        Check(book.IsAnimating && !book.Dock.Visible, $"it is still opening a quarter of a second later (progress {book.Progress:0.00})");
        await NothingReachesTheGame("while it opens");
        await Frames(JournalMoveFrames);
        Check(book.Progress == 1 && panel.Visible && !book.Dock.Visible, "after 0.95 s it lies open at the spread, with no second book in the dock");

        // The index, and each page written from the host's data.
        var links = panel.Lines.Where(line => line.Ink == JournalInk.Link).Select(line => line.Target).ToArray();
        Check(links.SequenceEqual(JournalPages.Sections), "the index turns to Discoveries, Abilities, Character, Map, and Notes & memories: " + string.Join(", ", links));
        await TestJournalPages(host);

        // Closing: Esc starts it; the controls stay held until the book is back in its dock; J midway turns it back to the index.
        panel.OpenPage(JournalPages.Map);
        _hud._UnhandledInput(Press(Key.Escape));
        await Frames(10);
        Check(_hud.JournalOpen && !book.WantsOpen && book.IsAnimating && !player.InputEnabled, "Esc closes the cover, and the controls stay with the book while it shrinks back");
        await NothingReachesTheGame("while it closes");
        _hud._UnhandledInput(Press(Key.J));
        Check(book.WantsOpen && panel.Page == JournalPanel.IndexPage, "J while it closes turns it back, open at the index (not the map it was closed on)");
        await Frames(JournalMoveFrames);
        Check(book.Progress == 1 && panel.Visible && !book.Dock.Visible, "the reversed closing settles on one open book");
        _hud._UnhandledInput(Press(Key.Escape));
        await Frames(JournalMoveFrames);
        Check(book.Progress == 0 && book.Dock.Visible && !panel.Visible && !_hud.JournalOpen && player.InputEnabled, "closed, the book is back in its dock and the body moves again");
        // A reversal while it opens: the dock, then Esc a quarter of a second in, then J.
        book.Dock.EmitSignal(Button.SignalName.Pressed);
        await Frames(15);
        _hud._UnhandledInput(Press(Key.Escape));
        await Frames(5);
        Check(_hud.JournalOpen && !book.WantsOpen && book.Progress > 0, "Esc while it opens turns it back towards its dock");
        _hud._UnhandledInput(Press(Key.J));
        await Frames(JournalMoveFrames);
        Check(book.Progress == 1 && panel.Page == JournalPanel.IndexPage && !book.Dock.Visible, "and J turns it round again: a rapid reversal opens cleanly at the index");
        ((Button)panel.FindChild("JournalCloseButton", true, false)!).EmitSignal(Button.SignalName.Pressed);
        await Frames(JournalMoveFrames);
        Check(!_hud.JournalOpen && book.Dock.Visible && player.InputEnabled, "the cover's own close link closes it too");

        // Space never reopens it. A real click through the viewport opens it; once it is closed, nothing of it holds the focus,
        // so Space and Enter (the engine's ui_accept) go nowhere near it.
        await ClickThroughViewport(book.Dock);
        Check(_hud.JournalOpen, "a real mouse click on the book, through the viewport, opens it");
        _hud._UnhandledInput(Press(Key.Escape));
        await Frames(JournalMoveFrames);
        var owner = GetViewport().GuiGetFocusOwner();
        Check(!_hud.JournalOpen && (owner == null || (owner != book.Dock && !book.IsAncestorOf(owner))), $"closed, no part of the book holds the keyboard focus ({owner?.Name})");
        foreach (var key in new[] { Key.Space, Key.Enter })
        {
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true }, true);
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false }, true);
            await Frames(3);
            Check(!_hud.JournalOpen && book.Progress == 0, $"{key} after the book closed (a jump; Enter is the wish box's) does not reopen it");
        }
    }

    /// <summary>Gameplay keys, the Gubble's ask and wheel, and the look drag, all handed to the HUD: none gets through the book.</summary>
    private async Task NothingReachesTheGame(string when)
    {
        var host = CommandHost.Of(_world)!;
        var companion = _world.Companion;
        var effects = host.ActiveEffectIds.Count;
        var receipts = host.TransientReceiptCount(CommandHost.PlayerPrincipal);
        var intent = companion.CurrentIntent;
        var help = _hud.GetNode<PanelContainer>("HelpFooter").GetChild<VBoxContainer>(0).GetNode<VBoxContainer>("KeyHelp").Visible;
        var view = _hud.ViewMode;
        foreach (var key in new[] { Key.Key1, Key.Key3, Key.Q, Key.X, Key.F, Key.V, Key.H, Key.F3, Key.C, Key.T })
            _hud._UnhandledInput(Press(key));
        _hud._UnhandledInput(RightButton(true));
        await Frames(20);
        var wheel = _hud.Wheel.Held || _hud.Wheel.Open || GetTree().Paused;
        _hud._UnhandledInput(RightButton(false));
        _hud._UnhandledInput(LeftButton(true));
        _hud._UnhandledInput(Motion(new Vector2(40, 0)));
        var drag = _hud.Drag.Pressed || _hud.Drag.Dragging;
        _hud._UnhandledInput(LeftButton(false));
        Check(host.ActiveEffectIds.Count == effects && host.TransientReceiptCount(CommandHost.PlayerPrincipal) == receipts && companion.CurrentIntent == intent &&
              _hud.GetNode<PanelContainer>("HelpFooter").GetChild<VBoxContainer>(0).GetNode<VBoxContainer>("KeyHelp").Visible == help &&
              _hud.ViewMode == view && !_hud.Customizing && _hud.TimeStop == -1 && !wheel && !drag && !_world.Player.InputEnabled,
            $"{when}, no gameplay key, no order to the Gubble, no ask, no wheel and no look drag gets through the book");
    }

    /// <summary>Each page is the host's data: the Glow progress and the discovered map, the rules pack and the input map, the player's status, the map, the notes.</summary>
    private async Task TestJournalPages(CommandHost host)
    {
        var panel = _hud.Journal;
        var player = _world.Player;

        // Discoveries: places and things are exactly the team's discovered entities, by their names.
        panel.OpenPage(JournalPages.Discoveries);
        var discovered = host.ExportDiscovered()["entities"]!.AsObject().Select(p => KernelJson.DisplayText(p.Value!["entity"]!["display_name"]!.GetValue<string>(), 60).Trim()).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var places = panel.Lines.SkipWhile(line => line.Text != "Places & things").Where(line => line.Ink == JournalInk.Title).Select(line => line.Text).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Check(discovered.Length > 0 && places.Length == Math.Min(discovered.Length, JournalPages.PerLeaf) && places.All(discovered.Contains),
            $"Places & things lists the team's discovered things and nothing else: {string.Join(", ", places)}");
        Check(panel.PageText.Contains($"Glow can hold {host.EffectiveEffectLimit("glow")} lights", StringComparison.Ordinal) && panel.PageText.Contains("Creative mode", StringComparison.Ordinal),
            "Magic we found is the host's Glow progress in the current mode");

        // Abilities: the loaded pack's abilities, each with its slot's key from the input map; a remapped key shows at once.
        panel.OpenPage(JournalPages.Abilities);
        var rules = host.Rules!;
        var titles = panel.Lines.Where(line => line.Ink == JournalInk.Title).Select(line => line.Text).ToArray();
        string Expected(IslandAbility ability)
        {
            var slot = Array.IndexOf(GubbleMagic.SlotCategories, ability.Category);
            return slot >= 0 ? $"{PlayerControls.Label(Act.GubbleSlots[slot])} · {ability.DisplayName}" : $"{ability.DisplayName} · in the Magic panel";
        }
        Check(titles.SequenceEqual(rules.Abilities.Select(Expected)), "Abilities lists the island pack's abilities in its order, each under its key: " + string.Join(" | ", titles));
        Check(panel.PageText.Contains("right-click: ask for what fits where you point", StringComparison.Ordinal) && panel.PageText.Contains("WASD walk · Shift run · Space jump", StringComparison.Ordinal) &&
              !panel.PageText.Contains("seed", StringComparison.OrdinalIgnoreCase) && !panel.PageText.Contains("Firebolt", StringComparison.Ordinal),
            "and the Gubble's orders and the ways through the world under their keys, with nothing the island does not have");
        SaveMap();
        try
        {
            Bind(Act.GubbleSlot1, Key.K);
            Bind(Act.Jump, Key.Y);
            await Frames(JournalRefreshFrames);
            Check(panel.PageText.Contains("K · Glow", StringComparison.Ordinal) && panel.PageText.Contains("Y jump", StringComparison.Ordinal) && !panel.PageText.Contains("1 · Glow", StringComparison.Ordinal),
                "with Glow's slot on K and jump on Y, the open page says K and Y: the keys are read from the input map, never written in");
        }
        finally { RestoreMap(); }
        await Frames(JournalRefreshFrames);
        Check(panel.PageText.Contains("1 · Glow", StringComparison.Ordinal), "and back to the defaults it says 1 again");

        // Character: the body's real values and player.status in each mode.
        panel.OpenPage(JournalPages.Character);
        Check(panel.PageText.Contains($"Height {player.BodyHeightM * 100:0} cm · reach {player.ReachM * 100:0} cm", StringComparison.Ordinal) &&
              panel.PageText.Contains($"Walk {player.WalkSpeedMps * 100:0} cm/s · run {player.RunSpeedMps * 100:0} cm/s", StringComparison.Ordinal) &&
              panel.PageText.Contains("Creative mode", StringComparison.Ordinal) && !panel.PageText.Contains("Health", StringComparison.Ordinal),
            "the character sheet gives the body's height, reach and speeds, and Creative mode has no health to show");
        Check(host.PlayerMode("challenge")["ok"]!.GetValue<bool>(), "(the island switched to Challenge through the host)");
        await Frames(JournalRefreshFrames);
        var status = host.PlayerQuery("player.status")["data"]!;
        Check(panel.PageText.Contains("Challenge mode", StringComparison.Ordinal) &&
              panel.PageText.Contains($"Health {host.Vitals.Health:0}/{host.Vitals.MaxHealth:0}", StringComparison.Ordinal) &&
              panel.PageText.Contains($"climbing capacity {host.Vitals.MaxStamina:0.00} m", StringComparison.Ordinal) && status["mode"]!.GetValue<string>() == "challenge",
            "in Challenge mode the open sheet shows player.status: health, stamina and climbing capacity, as the host keeps them");
        host.PlayerMode("creative");

        // Map: the discovered things and ground only, and a compass to the room's own north.
        panel.OpenPage(JournalPages.Map);
        var map = panel.MapView;
        var known = host.ExportDiscovered()["entities"]!.AsObject().Count;
        Check(map != null && map.KnownObjectCount == known && map.DiscoveredSpanCount > 0,
            $"the map draws the team's {known} discovered things and its discovered ground ({map?.DiscoveredSpanCount} strips)");
        Check(map != null && map.ShowsCompass && map.North.IsEqualApprox(Vector2.Up), "this room declares its site with -Z facing north: the compass points to the top");
        Check(JournalMap.NorthOnPage(90).IsEqualApprox(Vector2.Left) && JournalMap.NorthOnPage(180).IsEqualApprox(Vector2.Down) && JournalMap.NorthOnPage(270).IsEqualApprox(Vector2.Right),
            "and a room facing east, south or west turns it: the top of the map is labelled north only when it is");

        // Notes & memories: the player's note through journal.note, shown literally; the AI's own notes stay off the page.
        panel.OpenPage(JournalPages.Notes);
        var note = (LineEdit)panel.FindChild("JournalNote", true, false)!;
        note.Text = JournalNoteText;
        panel.SaveNote();
        Check(panel.Page == JournalPages.Notes && note.Text == "" && panel.Footer == "Your note is saved.", "saving a note keeps the memories page, clears the editor only on success and says so");
        var entries = host.PlayerQuery("journal.read", new JsonObject { ["kind"] = "note" })["data"]!["entries"]!.AsArray();
        Check(entries.Any(n => n?["text"]?.GetValue<string>() == JournalNoteText && n["author"]?.GetValue<string>() == CommandHost.PlayerPrincipal),
            "the note is in the host's journal (journal.read), attributed to the player");
        Check(panel.FindChildren("*", "Label", true, false).OfType<Label>().Any(l => l.Text == JournalNoteText) && panel.PageText.Contains("Your note\n" + JournalNoteText, StringComparison.Ordinal),
            "and on the page under \"Your note\", its markup shown literally, not interpreted");
        var aiNote = host.HandleObject(CanonicalJson.Text(new JsonObject
        {
            ["schema"] = "enfractal.command", ["version"] = 1, ["action_id"] = "journal-book-ai-note", ["room_id"] = host.Room.RoomId,
            ["op"] = "journal.note", ["args"] = new JsonObject { ["text"] = "A note in the AI's own words" },
        }), CommandHost.CompanionPrincipal);
        await Frames(JournalRefreshFrames);
        Check(aiNote["ok"]!.GetValue<bool>() && !panel.PageText.Contains("the AI's own words", StringComparison.Ordinal),
            "a note the companion's AI wrote stays in the journal but not on the player's page (the Gubble's words need their curated path first)");
        note.Text = "";
        panel.SaveNote();
        Check(panel.Footer.Length > 0 && panel.Footer != "Your note is saved." && panel.Page == JournalPages.Notes, "an empty note is refused with the host's reason on the page: " + panel.Footer);
        panel.OpenPage(JournalPanel.IndexPage);
    }

    /// <summary>
    /// A left click through the viewport (motion, press, release), as the GUI receives a real one. The point is in the viewport's
    /// own coordinates (in_local_coords), whatever size the headless window has under the project's stretch.
    /// </summary>
    private async Task ClickThroughViewport(Control control)
    {
        var point = control.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false }, true);
        await Frames(1);
    }

    /// <summary>Boot the room again in this suite's saves (the first world is gone): the note written in the book is on its page.</summary>
    private async Task TestJournalAfterReload()
    {
        var world = await BootRoom();
        if (world == null) return;
        var hud = world.GetNode<RoomHud>("RoomHud");
        hud.OpenJournal();
        hud.Journal.OpenPage(JournalPages.Notes);
        Check(hud.JournalText.Contains("Your note\n" + JournalNoteText, StringComparison.Ordinal), "a note written in the journal survives a complete reload of the room, on its Notes & memories page");
        world.Free();
        await Frames(2);
    }

    /// <summary>
    /// A fresh room whose team has seen nothing (empty saves, the host's sight sweep off): the journal names none of the room's
    /// things and its map is blank, though the room has them.
    /// </summary>
    private async Task TestJournalNothingUnseen()
    {
        var previousSight = CommandHost.DefaultTeamSightIntervalS;
        CommandHost.SaveRoot = HudSaves + "/unseen";
        CommandHost.DefaultTeamSightIntervalS = 0;
        try
        {
            var world = await BootRoom();
            if (world == null) return;
            var host = CommandHost.Of(world)!;
            var hud = world.GetNode<RoomHud>("RoomHud");
            hud.OpenJournal();
            hud.Journal.OpenPage(JournalPages.Discoveries);
            var names = host.Entities().Where(e => e["kind"]!.GetValue<string>() == "object").Select(e => e["display_name"]!.GetValue<string>()).ToArray();
            var text = hud.JournalText;
            Check(names.Length > 0 && host.ExportDiscovered()["entities"]!.AsObject().Count == 0 && text.Contains("A fresh world", StringComparison.Ordinal) &&
                  !names.Any(name => text.Contains(name, StringComparison.Ordinal)),
                $"in a room whose team has seen nothing, Discoveries names none of its {names.Length} things");
            hud.Journal.OpenPage(JournalPages.Map);
            Check(hud.Journal.MapView is { KnownObjectCount: 0, DiscoveredSpanCount: 0 }, "and its map is blank parchment: no ground and no things");
            world.Free();
            await Frames(2);
        }
        finally
        {
            CommandHost.DefaultTeamSightIntervalS = previousSight;
            CommandHost.SaveRoot = HudSaves;
        }
    }

    private async Task<RoomWorld?> BootRoom()
    {
        var world = GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
        AddChild(world);
        for (var i = 0; i < 900 && !world.WorldReady && world.LoadError.Length == 0; i++) await Frames(1);
        Check(world.WorldReady, "(the room boots again: " + world.LoadError + ")");
        if (world.WorldReady) await Frames(5);
        return world.WorldReady ? world : null;
    }
}
