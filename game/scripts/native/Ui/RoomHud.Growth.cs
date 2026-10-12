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
            if (progress.HasDiscovery("lantern"))
                lines.Add("Lantern\nA light that travels with the Gubble.");
            else
                lines.Add("Your Glow discoveries will appear here.");
            // Each future live discovery gets its own curated entry when its behaviour is implemented.
            // No undiscovered names, hidden conditions or cast counters are revealed by this page.
            if (!progress.HasDiscovery("lantern") && progress.DeliveredHints.Contains("lantern_self"))
                lines.Add("A thought from the Gubble\nTry asking me to glow on myself.");
            if (host.GlowProgressNotice.Length > 0) lines.Add(host.GlowProgressNotice);
        }
        var text = string.Join("\n\n", lines);
        if (_glowJournalText.Text != text) _glowJournalText.Text = text;
    }
}
