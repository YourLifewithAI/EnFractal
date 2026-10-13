using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EnFractal.Native;
using Godot;

namespace EnFractal.Tests.Kernel;

/// <summary>
/// The player's character in the Customize panel (the characters round, K6): a row of the plain figure and the characters the
/// library supplies, never the Gubble; choosing by mouse or keyboard sets the body's CharacterId; "Save appearance" keeps it beside
/// every other profile key and the next session restores it; a bad saved value is the plain figure; the player's colour button
/// is for the plain figure only, and the Gubble's is unchanged.
/// </summary>
public partial class PlayHudTest
{
    /// <summary>The row the supplied list must give, in order: the plain figure, then the family's four.</summary>
    private static readonly string[] CharacterRowNames = { CharacterChoice.PrototypeName, "Potato Man", "Tomato Man", "Cloudpuff", "Stickbear" };

    /// <summary>What the library might supply, with what the row must leave out: the Gubble, a second plain figure, a non-token and a repeat.</summary>
    private static IReadOnlyList<CharacterChoice> SuppliedCharacters(Texture2D? potato) => new[]
    {
        new CharacterChoice(CharacterChoice.GubbleId, "the Gubble"),
        new CharacterChoice("potato_man", "Potato Man", potato),
        new CharacterChoice("tomato_man", "Tomato Man"),
        new CharacterChoice("", "Nobody"),
        new CharacterChoice("Cloud Puff!", "Not a token"),
        new CharacterChoice("cloudpuff", "Cloudpuff"),
        new CharacterChoice("stickbear", "  Stickbear\t"),
        new CharacterChoice("potato_man", "Potato Man again"),
    };

    private async Task TestCharacterChoice()
    {
        var player = _world.Player;
        var companion = _world.Companion;
        var profileFile = ProjectSettings.GlobalizePath(HudProfile);
        var profileBefore = System.IO.File.Exists(profileFile) ? System.IO.File.ReadAllBytes(profileFile) : null;
        var previousTold = player.CharacterChanged;
        var told = new List<string>();
        player.CharacterChanged = id => { told.Add(id); previousTold?.Invoke(id); };
        try
        {
            // Nothing supplied (no library on this branch): the plain figure alone, chosen, with its colour button.
            await OpenCustomize();
            Check(_hud.Customizing && _hud.CharacterRow.Select(c => c.DisplayName).SequenceEqual(new[] { CharacterChoice.PrototypeName }) &&
                  _hud.CharacterButtons.Count == 1 && _hud.CharacterButtons[0].ButtonPressed && _hud.PlayerColorButton!.IsVisibleInTree() &&
                  !_hud.PlayerColorNote!.Visible && player.CharacterId == "",
                "with no characters supplied the row shows only the plain figure, chosen, and \"Change player color\" is there");
            CloseCustomize();

            var root = ProjectSettings.GlobalizePath("res://").TrimEnd('/').GetBaseDir();
            var potato = CharacterChoice.LoadThumbnail(root + "/assets/characters/potato_man");
            Check(potato != null && Math.Max(potato.GetWidth(), potato.GetHeight()) == CharacterChoice.ThumbnailPx &&
                  CharacterChoice.LoadThumbnail(root + "/assets/characters/no_such_character") == null,
                $"a character folder's {CharacterChoice.ThumbnailFile} loads as a {CharacterChoice.ThumbnailPx} px thumbnail; a folder without one gives none");
            _hud.CharacterChoices = () => SuppliedCharacters(potato);
            await OpenCustomize();
            var buttons = _hud.CharacterButtons;
            var (colourButton, note, chosenLabel) = (_hud.PlayerColorButton!, _hud.PlayerColorNote!, _hud.CharacterChosenLabel!);
            Check(_hud.CharacterRow.Select(c => c.DisplayName).SequenceEqual(CharacterRowNames) && buttons.Select(b => b.Text).SequenceEqual(CharacterRowNames) &&
                  _hud.CharacterRow.Select(c => c.Id).SequenceEqual(new[] { "", "potato_man", "tomato_man", "cloudpuff", "stickbear" }),
                "opened again, the row lists the plain figure, Potato Man, Tomato Man, Cloudpuff and Stickbear in order: never the Gubble, a second plain figure, a non-token or a repeat");
            Check(buttons.All(b => b.FocusMode == Control.FocusModeEnum.All && b.ToggleMode && b.Icon != null && b.IsVisibleInTree()) && buttons[1].Icon == potato &&
                  buttons[2].Icon != buttons[3].Icon,
                "each is a focusable button with a picture: Potato Man's own thumbnail, a swatch of its own for each of the others");
            Check(buttons[0].ButtonPressed && buttons.Skip(1).All(b => !b.ButtonPressed) && chosenLabel.Text == "Your character: Plain figure" &&
                  GetViewport().GuiGetFocusOwner() == buttons[0],
                "the chosen one, the plain figure, is pressed and named, and holds the keyboard focus");
            var panel = buttons[0].GetParent().GetParent().GetParent<PanelContainer>().GetGlobalRect();
            // Where the help sits depends on the window's history, so the panel is measured against the folded help at the screen's foot.
            var screen = GetViewport().GetVisibleRect();
            var footer = _hud.GetNode<PanelContainer>("HelpFooter");
            var foldedHelpTop = screen.End.Y - 18 - footer.GetCombinedMinimumSize().Y;
            Check(!footer.GetChild<VBoxContainer>(0).GetNode<VBoxContainer>("KeyHelp").Visible && screen.Encloses(panel) && panel.End.Y <= foldedHelpTop &&
                  buttons.All(b => panel.Encloses(b.GetGlobalRect()) && Mathf.IsEqualApprox(b.GlobalPosition.Y, buttons[0].GlobalPosition.Y)),
                $"with five characters the panel holds the row on one line and fits on the screen above the folded help ({panel}, screen {screen.Size}, help from {foldedHelpTop})");

            // The mouse: a click on Potato Man, then another.
            await ClickThroughViewport(buttons[1]);
            Check(player.CharacterId == "potato_man" && told.SequenceEqual(new[] { "potato_man" }) && buttons[1].ButtonPressed && !buttons[0].ButtonPressed &&
                  chosenLabel.Text == "Your character: Potato Man",
                $"a click on Potato Man chooses him: the body's CharacterId, CharacterChanged told once, his button pressed and named ({string.Join(",", told)})");
            await ClickThroughViewport(buttons[1]);
            Check(told.Count == 1 && buttons[1].ButtonPressed && player.CharacterId == "potato_man", "a second click on him changes nothing and tells nothing; he stays pressed");
            Check(!colourButton.Visible && note.IsVisibleInTree() && note.Text == RoomHud.DrawnColorsNote,
                "with a drawn character chosen the player's colour button hides and a short note says why");

            // The Gubble's colour button chooses its aura as before.
            var gubbleColour = _hud.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == "Change the Gubble's color");
            var gubbleBefore = companion.AppearanceColor;
            gubbleColour.EmitSignal(BaseButton.SignalName.Pressed);
            Check(!companion.AppearanceColor.IsEqualApprox(gubbleBefore) && companion.AuraColor is { } aura && aura.IsEqualApprox(companion.AppearanceColor) &&
                  player.CharacterId == "potato_man" && told.Count == 1,
                "\"Change the Gubble's color\" still changes the Gubble's colour and aura, and leaves the player's character alone");
            for (var i = 0; i < 4; i++) gubbleColour.EmitSignal(BaseButton.SignalName.Pressed);
            Check(companion.AppearanceColor.IsEqualApprox(gubbleBefore), "(the Gubble's colour put back)");

            // The keyboard: the arrows move along the row and Enter chooses.
            buttons[1].GrabFocus();
            await TapKey(Key.Right);
            var focused = GetViewport().GuiGetFocusOwner();
            await TapKey(Key.Enter);
            Check(focused == buttons[2] && player.CharacterId == "tomato_man" && told.SequenceEqual(new[] { "potato_man", "tomato_man" }) && buttons[2].ButtonPressed &&
                  !buttons[1].ButtonPressed,
                $"the keyboard works too: from Potato Man the right arrow moves to Tomato Man and Enter chooses him ({focused?.Name})");
            await TapKey(Key.Left);
            await TapKey(Key.Left);
            focused = GetViewport().GuiGetFocusOwner();
            await TapKey(Key.Enter);
            Check(focused == buttons[0] && player.CharacterId == "" && told.SequenceEqual(new[] { "potato_man", "tomato_man", "" }) && buttons[0].ButtonPressed &&
                  colourButton.IsVisibleInTree() && !note.Visible,
                $"left twice to the plain figure and Enter choose the prototype again, and the player's colour button comes back ({focused?.Name})");
            await TapKey(Key.Right);
            var right = GetViewport().GuiGetFocusOwner();
            await TapKey(Key.Left);
            await TapKey(Key.Left);
            var wrapped = GetViewport().GuiGetFocusOwner();
            Check(right == buttons[1] && wrapped == buttons[buttons.Count - 1] && player.CharacterId == "" && told.Count == 3,
                $"with the colour button showing under the row the arrows still move along it (right to Potato Man) and wrap at its ends (left from the plain figure to Stickbear), choosing nothing ({right?.Name}, {wrapped?.Name})");

            // The player's colour button colours the prototype, and its swatch with it.
            var colourBefore = player.AppearanceColor;
            var swatchBefore = buttons[0].Icon;
            colourButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(!player.AppearanceColor.IsEqualApprox(colourBefore) && buttons[0].Icon != swatchBefore, "\"Change player color\" still cycles the plain figure's colour, and its swatch follows");
            for (var i = 0; i < 4; i++) colourButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(player.AppearanceColor.IsEqualApprox(colourBefore) && buttons[0].Icon == swatchBefore, "(the colour put back)");

            // "Save appearance" writes the choice, and only its own fields: every other key stays as it was.
            Check(_hud.ChooseCharacter("stickbear") && !_hud.ChooseCharacter(CharacterChoice.GubbleId) && !_hud.ChooseCharacter("dragon") && player.CharacterId == "stickbear",
                "Stickbear is chosen; the Gubble and a character the row does not show are refused");
            _hud.SavePreferences();
            Check(ProfileValue(RoomHud.CharacterProfileKey).AsString() == "stickbear", "\"Save appearance\" writes the choice to the profile");
            var config = new ConfigFile();
            config.Load(HudProfile);
            config.SetValue("profile", "a_later_setting", "kept");
            config.SetValue("later_section", "a_value", 3);
            config.Save(HudProfile);
            var kept = ProfileSnapshot();
            kept.Remove("profile/" + RoomHud.CharacterProfileKey);
            _hud.ChooseCharacter("cloudpuff");
            _hud.SavePreferences();
            var saved = ProfileSnapshot();
            var lost = kept.Where(pair => !saved.TryGetValue(pair.Key, out var value) || value != pair.Value).Select(pair => $"{pair.Key}: {pair.Value} -> {saved.GetValueOrDefault(pair.Key, "(gone)")}");
            Check(ProfileValue(RoomHud.CharacterProfileKey).AsString() == "cloudpuff" && saved.Count == kept.Count + 1 && !lost.Any(),
                $"saving again writes the new choice beside every other key, each kept exactly ({string.Join(", ", kept.Keys)}){string.Concat(lost.Select(l => "; changed " + l))}");
            CloseCustomize();

            // A new session restores it through SetCharacterId.
            var fresh = await FreshCharacterHud(() => SuppliedCharacters(null));
            Check(fresh.Id == "cloudpuff" && fresh.Told.SequenceEqual(new[] { "cloudpuff" }) && fresh.Pressed == "Cloudpuff" && !fresh.ColorButton,
                $"a new session restores Cloudpuff: the body wears her (CharacterChanged told once), her button is pressed, the colour button hidden ({fresh.Id}, {fresh.Pressed})");

            // A bad saved value is the plain figure.
            foreach (var (value, why) in new (Variant? Value, string Why)[]
                     { ("Cloud Puff!", "not a token"), ("dragon", "a character the library does not have"), (CharacterChoice.GubbleId, "the Gubble"), (7, "not a string"), (null, "missing") })
            {
                SetProfileCharacter(value);
                fresh = await FreshCharacterHud(() => SuppliedCharacters(null));
                Check(fresh.Id == "" && fresh.Told.Length == 0 && fresh.Pressed == CharacterChoice.PrototypeName && fresh.ColorButton && fresh.Buttons == CharacterRowNames.Length,
                    $"a saved character that is {why} loads as the plain figure, with its colour button ({fresh.Id}, {fresh.Pressed})");
            }

            // Nothing supplied, or a list that is null: the plain figure alone, and nothing breaks.
            SetProfileCharacter("stickbear");
            var none = await FreshCharacterHud(null);
            var nothing = await FreshCharacterHud(() => null);
            Check(none.Id == "" && none.Buttons == 1 && none.ColorButton && nothing.Id == "" && nothing.Buttons == 1 && nothing.ColorButton,
                "with no characters supplied (or a null list) a saved Stickbear is the plain figure, the row holds it alone, and nothing breaks");
        }
        finally
        {
            if (_hud.Customizing) CloseCustomize();
            _hud.CharacterChoices = null;
            player.SetCharacterId("");
            player.CharacterChanged = previousTold;
            if (profileBefore != null) System.IO.File.WriteAllBytes(profileFile, profileBefore);
        }
    }

    private async Task OpenCustomize()
    {
        if (!_hud.Customizing) _hud._UnhandledInput(Press(Key.C));
        await Frames(2);
    }

    private void CloseCustomize()
    {
        if (_hud.Customizing) _hud._UnhandledInput(Press(Key.C));
    }

    /// <summary>A key pressed and let go through the viewport, as the GUI receives a real one (the panel's focus moves and presses with it).</summary>
    private async Task TapKey(Key key)
    {
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true }, true);
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false }, true);
        await Frames(1);
    }

    /// <summary>Every key of the suite's profile and its value, as text ("section/key").</summary>
    private static Dictionary<string, string> ProfileSnapshot()
    {
        var config = new ConfigFile();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (config.Load(HudProfile) != Error.Ok) return values;
        foreach (var section in config.GetSections())
            foreach (var key in config.GetSectionKeys(section))
                values[section + "/" + key] = GD.VarToStr(config.GetValue(section, key));
        return values;
    }

    /// <summary>The profile's saved character set to a value, or taken out (null); every other key left as it is.</summary>
    private static void SetProfileCharacter(Variant? value)
    {
        var config = new ConfigFile();
        config.Load(HudProfile);
        if (value is { } saved) config.SetValue("profile", RoomHud.CharacterProfileKey, saved);
        else if (config.HasSectionKey("profile", RoomHud.CharacterProfileKey)) config.EraseSectionKey("profile", RoomHud.CharacterProfileKey);
        config.Save(HudProfile);
    }

    /// <summary>
    /// A new session's HUD on the suite's profile, with these characters supplied as the composition root would (before the HUD
    /// enters the tree): what the body wears, what CharacterChanged was told, and, with the panel open, what is pressed and whether
    /// the player's colour button shows.
    /// </summary>
    private async Task<(string Id, string[] Told, string Pressed, bool ColorButton, int Buttons)> FreshCharacterHud(Func<IReadOnlyList<CharacterChoice>?>? choices)
    {
        var world = new Node3D();
        AddChild(world);
        var player = new SmallPlayerController { ReadKeyboard = false };
        var told = new List<string>();
        player.CharacterChanged = told.Add;
        world.AddChild(player);
        var companion = new CompanionAvatar();
        world.AddChild(companion);
        var hud = new RoomHud { Player = player, Companion = companion, RoomTitle = "CHARACTER", CharacterChoices = choices };
        world.AddChild(hud);
        await Frames(2);
        hud._UnhandledInput(Press(Key.C));
        await Frames(1);
        var pressed = string.Join(",", hud.CharacterButtons.Where(b => b.ButtonPressed).Select(b => b.Text));
        var result = (player.CharacterId, told.ToArray(), pressed, hud.PlayerColorButton?.IsVisibleInTree() == true, hud.CharacterButtons.Count);
        world.QueueFree();
        await Frames(1);
        return result;
    }
}
