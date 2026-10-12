using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EnFractal.Native;

/// <summary>
/// The Glow journal presents only discoveries the trusted host has recorded. Its words are curated;
/// command arguments, world names and AI notes never supply this page's text or unlock anything.
/// </summary>
public partial class RoomHud
{
    private PanelContainer _glowJournal = null!;
    private Label _glowJournalText = null!;

    public bool GlowJournalOpen => _glowJournal != null && _glowJournal.Visible;
    /// <summary>The words actually shown on the page, also readable by the headless HUD suite.</summary>
    public string GlowJournalText => _glowJournalText?.Text ?? "";

    private void BuildGlowJournal(Node actions, Theme theme)
    {
        var button = new Button { Name = "GlowJournalButton", Text = "Glow journal", CustomMinimumSize = new Vector2(0, 26) };
        button.Pressed += ToggleGlowJournal;
        actions.AddChild(button);
        _glowJournal = new PanelContainer
        {
            Name = "GlowJournal", Position = new Vector2(18, 190), Theme = theme,
            CustomMinimumSize = new Vector2(340, 0), Visible = false,
        };
        AddChild(_glowJournal);
        var page = new VBoxContainer();
        _glowJournal.AddChild(page);
        page.AddChild(new Label { Text = "GLOW JOURNAL" });
        _glowJournalText = new Label
        {
            Name = "GlowJournalText", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(320, 0), MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        page.AddChild(_glowJournalText);
        AddButton(page, "Close", ToggleGlowJournal, 26);
        RefreshGlowJournal();
    }

    /// <summary>A local reading panel, with no new key binding. Esc closes it through the existing HUD action.</summary>
    public void ToggleGlowJournal()
    {
        CancelAsk();
        EndDrag();
        if (!GlowJournalOpen && Customizing) ToggleCustomization();
        if (!GlowJournalOpen && MagicPanelOpen) SetMagicPanelOpen(false);
        _glowJournal.Visible = !GlowJournalOpen;
        Player.SetInputEnabled(!GlowJournalOpen);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        RefreshGlowJournal();
    }

    private void RefreshGlowJournal()
    {
        if (_glowJournalText == null) return;
        var lines = new List<string>();
        if (Host is not { } host)
            lines.Add("Your Glow journal will be ready when the island is ready.");
        else
        {
            var limit = host.EffectiveEffectLimit("glow");
            lines.Add(limit > 0 ? $"Glow can hold {limit} {(limit == 1 ? "light" : "lights")} at a time." : "Glow is not available on this island.");
            var progress = host.GlowProgress;
            if (progress.Discoveries.Count == 0)
                lines.Add("Your Glow discoveries will appear here.");
            foreach (var discovery in progress.Discoveries)
            {
                var words = discovery switch
                {
                    "lantern" => "Lantern\nA light that travels with the Gubble.",
                    "torch" => "Torch\nA bright light resting on a high place.",
                    "bioluminesce" => "Bioluminesce\nTiny living-looking lights gather across the water.",
                    "constellation" when progress.ActiveLightAllowance >= 5 => "Constellation\nLights joined into your own pattern. You can now move its points and change their links.",
                    "constellation" => "Constellation\nNearby lights connect into a small pattern.",
                    "skylight" => "Skylight\nA small star shining above the island.",
                    _ => "",
                };
                if (words.Length > 0) lines.Add(words);
            }
            // Only hints already delivered by the host appear. No cast counters or hidden thresholds are revealed.
            foreach (var hint in progress.DeliveredHints)
            {
                var words = hint switch
                {
                    "lantern_self" when !progress.HasDiscovery("lantern") => "Try asking me to glow on myself.",
                    "torch_high_place" when !progress.HasDiscovery("torch") => "I wonder how a light would feel in a high place.",
                    "bioluminesce_water" when !progress.HasDiscovery("bioluminesce") => "The water might have its own way to shine.",
                    "constellation_nearby" when !progress.HasDiscovery("constellation") => "Maybe a few nearby lights could make a pattern.",
                    "skylight_sky" when !progress.HasDiscovery("skylight") => "Could we try a light above us, under the open sky?",
                    _ => "",
                };
                if (words.Length > 0) lines.Add("A thought from the Gubble\n" + words);
            }
            if (host.GlowProgressNotice.Length > 0) lines.Add(host.GlowProgressNotice);
        }
        var text = string.Join("\n\n", lines);
        if (_glowJournalText.Text != text) _glowJournalText.Text = text;
    }
}
