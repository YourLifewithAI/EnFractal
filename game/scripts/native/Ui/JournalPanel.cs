using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using EnFractal.Native.Kernel;

namespace EnFractal.Native.Ui;

/// <summary>A book of selected memories, backed by the host's journal and discovered map.</summary>
public partial class JournalPanel : Control
{
    public Func<CommandHost?> FindHost { get; set; } = () => null;
    public Action CloseRequested { get; set; } = () => { };
    public string Page { get; private set; } = "Index";
    public static readonly string[] Sections = { "Discoveries", "Abilities", "Character", "Map", "Notes & memories" };
    private VBoxContainer _entries = null!;
    private Label _status = null!;
    private Label _heading = null!;
    private LineEdit _note = null!;
    private VBoxContainer _editor = null!;
    private ScrollContainer _scroll = null!;
    private Button _more = null!;
    private int _offset;
    private double _refresh;
    public JournalMap? MapView { get; private set; }

    public override void _Ready()
    {
        Name = "JournalPanel"; MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        OffsetLeft = 28; OffsetTop = 24; OffsetRight = -28; OffsetBottom = -24;
        Theme = BookTheme();
        var paper = new JournalPaper(); AddChild(paper); paper.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margins = new MarginContainer(); AddChild(margins); margins.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margins.AddThemeConstantOverride("margin_left", 50); margins.AddThemeConstantOverride("margin_right", 50);
        margins.AddThemeConstantOverride("margin_top", 32); margins.AddThemeConstantOverride("margin_bottom", 32);
        var spread = new HBoxContainer(); spread.AddThemeConstantOverride("separation", 72); margins.AddChild(spread);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.84f };
        left.AddThemeConstantOverride("separation", 14); spread.AddChild(left);
        Text(left, "ENFRACTAL", 16, new Color("816344"));
        Text(left, "Our field\njournal", 42);
        Text(left, "A shared adventure", 23);
        Text(left, "You & the Gubble", 20, new Color("846849"));
        left.AddChild(new HSeparator());
        Text(left, "Places discovered, small wonders,\nand things worth remembering.", 19);
        Button(left, "Return to the index", () => OpenPage("Index"));
        left.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        Text(left, "Some pages are still unwritten.", 17, new Color("816344"));
        Button(left, "Close the journal · Esc", CloseRequested);
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 10); spread.AddChild(right);
        _heading = Text(right, "Index", 30); right.AddChild(new HSeparator());
        _scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        right.AddChild(_scroll);
        _entries = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _entries.AddThemeConstantOverride("separation", 16); _scroll.AddChild(_entries);
        _more = Button(right, "Next page →", () => { _offset += 12; Refresh(); });
        _status = Text(right, "", 15, new Color("806447")); _status.Name = "JournalStatus";
        _editor = new VBoxContainer(); right.AddChild(_editor);
        Text(_editor, "Leave a thought on the page…", 16);
        _note = new LineEdit { Name = "JournalNote", MaxLength = 280, PlaceholderText = "Something worth remembering" }; _editor.AddChild(_note);
        _note.TextSubmitted += _ => SaveNote(); Button(_editor, "Save this note", SaveNote);
        OpenPage("Index");
    }
    private static Theme BookTheme()
    {
        var theme = new Theme { DefaultFontSize = 19,
            DefaultFont = new SystemFont { FontNames = new[] { "Segoe Print", "Chilanka", "Comic Sans MS", "cursive" } } };
        var ink = new Color("443326");
        foreach (var type in new[] { "Label", "Button", "LineEdit" })
        { theme.SetColor("font_color", type, ink); theme.SetColor("font_hover_color", type, new Color("8c482c"));
          theme.SetColor("font_pressed_color", type, new Color("a46030")); theme.SetColor("font_focus_color", type, ink); }
        foreach (var state in new[] { "normal", "hover", "pressed", "focus" })
            theme.SetStylebox(state, "Button", new StyleBoxFlat { BgColor = new Color(0.45f, 0.29f, 0.12f, state == "hover" ? 0.12f : 0),
                BorderColor = new Color(0.45f, 0.29f, 0.12f, state == "focus" ? 0.55f : 0.15f), BorderWidthBottom = 1,
                ContentMarginTop = 7, ContentMarginBottom = 7, ContentMarginLeft = 8, ContentMarginRight = 8 });
        theme.SetStylebox("normal", "LineEdit", new StyleBoxFlat { BgColor = new Color(1, 0.95f, 0.8f, 0.18f),
            BorderColor = new Color("9c8057"), BorderWidthBottom = 1, ContentMarginLeft = 7, ContentMarginRight = 7, ContentMarginTop = 6, ContentMarginBottom = 6 });
        theme.SetColor("font_placeholder_color", "LineEdit", new Color("887352"));
        theme.SetColor("caret_color", "LineEdit", ink); return theme;
    }
    private static Label Text(Node parent, string text, int size = 19, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size); if (color != null) label.AddThemeColorOverride("font_color", color.Value);
        parent.AddChild(label); return label;
    }
    private static Button Button(Node parent, string text, Action pressed)
    {
        var button = new Button { Text = text, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 40) };
        button.Pressed += pressed; parent.AddChild(button); return button;
    }
    public void OpenPage(string page)
    {
        Page = page; _offset = 0; if (_scroll != null) _scroll.ScrollVertical = 0; Refresh();
    }
    public override void _Process(double delta)
    {
        if (Visible && Page == "Map" && (_refresh += delta) >= 2)
        { _refresh = 0; var host = FindHost(); if (host != null) MapView?.UpdateFrom(host); }
    }
    public void SaveNote()
    {
        var host = FindHost(); if (host == null) { _status.Text = "The journal is not ready yet."; return; }
        var result = host.PlayerJournalNote(_note.Text.Trim());
        if (result["ok"]?.GetValue<bool>() != true)
        { _status.Text = result["error"]?["message"]?.GetValue<string>() ?? "The note could not be saved."; return; }
        _note.Text = ""; OpenPage("Notes & memories"); _status.Text = "Your note is saved.";
    }
    public void Refresh()
    {
        if (_entries == null) return;
        foreach (var child in _entries.GetChildren()) { _entries.RemoveChild(child); child.QueueFree(); }
        MapView = null; _heading.Text = Page; _more.Visible = false; _editor.Visible = Page == "Notes & memories"; _status.Text = "";
        if (Page == "Index")
        {
            _entries.AddThemeConstantOverride("separation", 8);
            Text(_entries, "Where shall we turn?", 20);
            var descriptions = new[] { "Places & things we have seen", "Magic & the ways we move", "Your character sheet", "The world we know together", "Creations, plans & thoughts" };
            for (var i = 0; i < Sections.Length; i++)
            {
                var section = Sections[i];
                var item = new VBoxContainer(); item.AddThemeConstantOverride("separation", 0); _entries.AddChild(item);
                Button(item, $"{i + 1:00}   {section}", () => OpenPage(section)); Text(item, descriptions[i], 15, new Color("846849"));
            }
            _status.Text = "Written together, a little at a time."; return;
        }
        _entries.AddThemeConstantOverride("separation", 16);
        var host = FindHost(); if (host == null) { Text(_entries, "The journal is not ready yet."); return; }
        if (Page == "Map")
        {
            Text(_entries, "Only the ground our eyes have found.", 16);
            MapView = new JournalMap { CustomMinimumSize = new Vector2(0, 325), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _entries.AddChild(MapView); MapView.UpdateFrom(host);
            Text(_entries, "● You     ◇ The Gubble\nDarker ink is your level. Other heights fade.\nBlank parchment is still unexplored.", 15); return;
        }
        if (Page == "Abilities")
        {
            Text(_entries, "A little fire magic", 24);
            Text(_entries, "Place a flame · 6\nFace a nearby surface. A warm flame lights it for twelve seconds.");
            Text(_entries, "Firebolt · 7\nSend a spark straight ahead. It stops at solid ground or fades after two seconds.");
            Text(_entries, "Douse · 8\nPut out spells and burning landscape. Scorching remains.");
            Text(_entries, host.Room.Ecology.Count > 0 ? "Flames ignite plants and wooden stock parts. Player-grown plants are protected." : "This room has visual fire only.", 16);
            Text(_entries, "Tending the island", 24);
            Text(_entries, "9 - Collect ripe seeds nearby\nN - Choose a seed from your pouch\n0 - Plant on grass or soil ahead of you\nShift+0 - Restore a lost plant at its old site\nB - Repair a scorched stock part\nNew plants are protected. Saplings grow over thirty seconds; seeds replenish in a minute.");
            var pouch = host.EcologySnapshot()["seeds"]!.AsObject();
            Text(_entries, "Seed pouch: " + (pouch.Count == 0 ? "empty" : string.Join(", ", pouch.Select(p => p.Key.Replace('_',' ') + " x" + p.Value))), 16);
            Text(_entries, "Ways through the world", 24);
            Text(_entries, "Walk & run · WASD / Shift\nJump · Space\nClimb · walk into a steep face; Space lets go\nSwim · step into deep water\nCarry / put down · F     Push · V"); return;
        }
        if (Page == "Character")
        {
            var player = host.Player; Text(_entries, "The small adventurer", 24);
            Text(_entries, "A thumb-sized explorer, in a world made from your room.");
            if (player != null)
            {
                Text(_entries, $"Height                 {player.BodyHeightM * 100:0} cm\nReach                  {player.ReachM * 100:0} cm\nWalk                    {player.WalkSpeedMps * 100:0} cm/s\nRun                      {player.RunSpeedMps * 100:0} cm/s\nJump                    {player.JumpApexNowM * 100:0.#} cm");
                Text(_entries, "Carrying: " + (host.HeldBy(CommandHost.PlayerAvatar) == null ? "empty hands" : "an object"));
            }
            Text(_entries, "Your companion", 24);
            Text(_entries, (host.Companion?.CompanionName ?? "The Gubble") + "\nFloats beside you, crosses water, and helps fetch the things you find.");
            Text(_entries, "Character art is still being prepared. Health, experience and equipment are not part of this build yet.", 16); return;
        }
        if (Page == "Discoveries")
        {
            var found = host.ExportDiscovered()["entities"]!.AsObject().Select(p => p.Value!).OrderByDescending(e => e["last_seen_utc"]!.GetValue<string>()).ToList();
            Text(_entries, "Things we have found", 24);
            if (found.Count == 0) Text(_entries, "A fresh world. Explore with the Gubble to fill these pages.");
            foreach (var known in found.Skip(_offset).Take(12))
            {
                var entity = known["entity"]!; Text(_entries, entity["display_name"]!.GetValue<string>(), 22);
                Text(_entries, (entity["category"]?.GetValue<string>() ?? "A discovery") + " · last seen together" +
                    (known["may_be_stale"]?.GetValue<bool>() == true ? "\nIt may have changed since our last visit." : ""), 16);
            }
            _more.Visible = _offset + 12 < found.Count; _status.Text = "Our shared knowledge, remembered as last seen."; return;
        }
        // Select memories, not a transcript: routine task completions (including repeated fetches) stay off the page.
        var journal = host.ExportJournal();
        var memories = journal["history"]!.AsArray().Where(e => e?["kind"]?.GetValue<string>() is "built" or "changed" or "removed" or "found")
            .Concat(journal["notes"]!.AsArray()).OfType<JsonObject>().OrderByDescending(e => e["at_utc"]!.GetValue<string>()).ToList();
        if (_offset == 0)
            foreach (var plan in journal["open_tasks"]!.AsArray().OfType<JsonObject>().Where(e => e["line"]?.GetValue<string>().StartsWith("Fetching", StringComparison.Ordinal) == true).Take(3))
                Text(_entries, "Underway · " + plan["line"]!.GetValue<string>(), 18);
        if (memories.Count == 0) Text(_entries, "These pages are for the things worth keeping: a creation, a discovery, a thought for another day.");
        foreach (var entry in memories.Skip(_offset).Take(12))
        {
            var note = entry["kind"]?.GetValue<string>() == "note";
            var author = entry["author"]?.GetValue<string>() == CommandHost.PlayerPrincipal ? "Your note" : "The Gubble’s note";
            Text(_entries, note ? author : "A shared memory", 16, new Color("846849"));
            Text(_entries, (note ? entry["text"] : entry["line"])?.GetValue<string>() ?? "");
        }
        _more.Visible = _offset + 12 < memories.Count;
    }
}
