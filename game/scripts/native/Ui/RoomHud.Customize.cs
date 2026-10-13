using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EnFractal.Native;

/// <summary>
/// The player's character in the Customize panel ("Your two avatars"; the characters round, K6, docs/runs/RUN-2-CHARACTERS.md):
/// a row of the plain prototype figure and the drawn characters the composition root supplies, each a button with a thumbnail
/// and a name. Choosing one sets Player.SetCharacterId (the composition root draws it); "Save appearance" keeps it in the profile
/// (CharacterProfileKey) and the next session restores it. The player's colour button colours the prototype figure only: it
/// hides while a drawn character is chosen, which keeps the colours the family drew. The Gubble's colour button is unchanged.
/// </summary>
public partial class RoomHud
{
    /// <summary>The profile's key for the chosen character: its id, or "" for the prototype figure.</summary>
    public const string CharacterProfileKey = "player_character";
    /// <summary>What shows in place of the player's colour button while a drawn character is chosen.</summary>
    public const string DrawnColorsNote = "Drawn characters keep the colors they were drawn with.";
    private const int CharacterButtonWidth = 100;
    private const int CharacterButtonHeight = 100;
    private const int CharacterRowColumns = 5;

    /// <summary>
    /// The seam the composition root fills from the character library: the drawn characters the player may choose (their ids,
    /// names and thumbnails, CharacterChoice). Set it before the HUD enters the tree, so the saved choice is restored when the HUD
    /// loads the profile; it is read again each time the panel opens, so return a list built once. Null, or nothing set: the row
    /// shows only the prototype figure. The HUD leaves out the Gubble, the prototype, ids that are not tokens and repeats.
    /// </summary>
    public Func<IReadOnlyList<CharacterChoice>?>? CharacterChoices { get; set; }

    private IReadOnlyList<CharacterChoice>? _rowChoices;
    private HFlowContainer? _characterRow;
    private readonly List<Button> _characterButtons = new();
    private readonly ButtonGroup _characterGroup = new();
    private Label? _characterChosen;
    private Button? _playerColorButton;
    private Label? _playerColorNote;
    private readonly Dictionary<int, ImageTexture> _prototypeSwatches = new();
    private readonly Dictionary<string, ImageTexture> _tileSwatches = new(StringComparer.Ordinal);

    /// <summary>The row's choices in order, the prototype figure first (the headless HUD suite reads it).</summary>
    public IReadOnlyList<CharacterChoice> CharacterRow => _rowChoices ??= ChoicesShown(ReadCharacterChoices());
    /// <summary>The row's buttons, in the row's order.</summary>
    public IReadOnlyList<Button> CharacterButtons => _characterButtons;
    /// <summary>"Change player color": it colours the prototype figure, and hides while a drawn character is chosen.</summary>
    public Button? PlayerColorButton => _playerColorButton;
    /// <summary>The note in the colour button's place while a drawn character is chosen.</summary>
    public Label? PlayerColorNote => _playerColorNote;
    /// <summary>The line that names the chosen character.</summary>
    public Label? CharacterChosenLabel => _characterChosen;
    /// <summary>
    /// The character the row shows as chosen: the player's CharacterId when the row has it, else the prototype figure (""): an id
    /// the library does not have (a room saved elsewhere, a character since removed) is drawn as the prototype.
    /// </summary>
    public string ChosenCharacterId => RowHas(Player.CharacterId) ? Player.CharacterId : CharacterChoice.PrototypeId;
    /// <summary>A drawn character is chosen (not the prototype figure).</summary>
    public bool DrawnCharacterChosen => ChosenCharacterId.Length > 0;

    /// <summary>
    /// The row as shown, from what the composition root supplies: the prototype figure first, then each supplied character in
    /// order, leaving out the Gubble, the prototype's own id, ids that are not tokens and repeats (the first stays); names are
    /// cleaned (CharacterChoice.ShownName).
    /// </summary>
    public static IReadOnlyList<CharacterChoice> ChoicesShown(IReadOnlyList<CharacterChoice>? supplied)
    {
        var row = new List<CharacterChoice> { new(CharacterChoice.PrototypeId, CharacterChoice.PrototypeName) };
        var seen = new HashSet<string>(StringComparer.Ordinal) { CharacterChoice.PrototypeId, CharacterChoice.GubbleId };
        foreach (var choice in supplied ?? Array.Empty<CharacterChoice>())
        {
            if (choice == null || !SmallPlayerController.IsCharacterToken(choice.Id) || !seen.Add(choice.Id)) continue;
            row.Add(choice with { DisplayName = CharacterChoice.ShownName(choice.DisplayName, choice.Id) });
        }
        return row;
    }

    private IReadOnlyList<CharacterChoice>? ReadCharacterChoices() => CharacterChoices?.Invoke();

    private bool RowHas(string id) => CharacterRow.Any(choice => choice.Id == id);

    /// <summary>The panel's character lines: the chosen one's name, the row, and the player's colour button with its note.</summary>
    private void BuildCharacterChoice(Node options)
    {
        _characterChosen = new Label { Name = "CharacterChosen" };
        options.AddChild(_characterChosen);
        _characterRow = new HFlowContainer
        {
            Name = "CharacterRow",
            CustomMinimumSize = new Vector2(CharacterRowColumns * CharacterButtonWidth + (CharacterRowColumns - 1) * 6, 0),
        };
        _characterRow.AddThemeConstantOverride("h_separation", 6);
        _characterRow.AddThemeConstantOverride("v_separation", 6);
        options.AddChild(_characterRow);
        _playerColorButton = new Button
        {
            Name = "PlayerColor", Text = "Change player color", CustomMinimumSize = new Vector2(0, 38), FocusMode = Control.FocusModeEnum.All,
        };
        _playerColorButton.Pressed += CyclePlayerColor;
        options.AddChild(_playerColorButton);
        _playerColorNote = new Label { Name = "PlayerColorNote", Text = DrawnColorsNote, Visible = false };
        options.AddChild(_playerColorNote);
        FillCharacterRow();
    }

    /// <summary>The row's buttons, made again from CharacterRow.</summary>
    private void FillCharacterRow()
    {
        if (_characterRow == null) return;
        foreach (var button in _characterButtons)
        {
            button.ButtonGroup = null;
            _characterRow.RemoveChild(button);
            button.QueueFree();
        }
        _characterButtons.Clear();
        var chosen = new StyleBoxFlat
        {
            BgColor = new Color(0.22f, 0.36f, 0.33f), BorderColor = new Color("f0e6cf"),
            BorderWidthLeft = 3, BorderWidthTop = 3, BorderWidthRight = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        foreach (var choice in CharacterRow)
        {
            var id = choice.Id;
            var button = new Button
            {
                Name = id.Length > 0 ? "Character_" + id : "PlainFigure", Text = choice.DisplayName, TooltipText = choice.DisplayName,
                ToggleMode = true, ButtonGroup = _characterGroup, FocusMode = Control.FocusModeEnum.All,
                CustomMinimumSize = new Vector2(CharacterButtonWidth, CharacterButtonHeight),
                Icon = choice.Thumbnail ?? SwatchFor(id), ExpandIcon = true,
                IconAlignment = HorizontalAlignment.Center, VerticalIconAlignment = VerticalAlignment.Top,
                ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            button.AddThemeFontSizeOverride("font_size", 15);
            button.AddThemeStyleboxOverride("pressed", chosen);
            button.AddThemeStyleboxOverride("hover_pressed", chosen);
            button.Pressed += () => ChooseCharacter(id);
            _characterRow.AddChild(button);
            _characterButtons.Add(button);
        }
        // The arrows move along the row and wrap at its ends; left to the engine, the colour button under the row's full width
        // is the nearer control to the right of a character.
        for (var i = 0; i < _characterButtons.Count; i++)
        {
            var button = _characterButtons[i];
            button.FocusNeighborLeft = button.GetPathTo(_characterButtons[(i + _characterButtons.Count - 1) % _characterButtons.Count]);
            button.FocusNeighborRight = button.GetPathTo(_characterButtons[(i + 1) % _characterButtons.Count]);
        }
        SyncCharacterRow();
    }

    /// <summary>The panel opens: the library's list is read again (a character converted since shows), the row shows the body's choice, and the chosen button takes the keyboard focus.</summary>
    private void OnCustomizeOpened()
    {
        var fresh = ChoicesShown(ReadCharacterChoices());
        if (!fresh.SequenceEqual(CharacterRow))
        {
            _rowChoices = fresh;
            FillCharacterRow();
        }
        SyncCharacterRow();
        var index = CharacterRow.ToList().FindIndex(choice => choice.Id == ChosenCharacterId);
        if (index >= 0 && index < _characterButtons.Count) _characterButtons[index].GrabFocus();
    }

    /// <summary>
    /// Choose a character in the row by its id ("" for the prototype figure): the body takes it at once (Player.SetCharacterId,
    /// which tells CharacterChanged only when it changes). An id the row does not show is refused.
    /// </summary>
    public bool ChooseCharacter(string id)
    {
        var chosen = id != null && RowHas(id) && Player.SetCharacterId(id);
        SyncCharacterRow();
        return chosen;
    }

    /// <summary>The row, the name line and the colour button as the body's choice is now.</summary>
    private void SyncCharacterRow()
    {
        var chosen = ChosenCharacterId;
        var row = CharacterRow;
        for (var i = 0; i < _characterButtons.Count && i < row.Count; i++)
            if (row[i].Id != chosen) _characterButtons[i].SetPressedNoSignal(false);
        for (var i = 0; i < _characterButtons.Count && i < row.Count; i++)
            if (row[i].Id == chosen) _characterButtons[i].SetPressedNoSignal(true);
        if (_characterChosen != null) _characterChosen.Text = "Your character: " + (row.FirstOrDefault(choice => choice.Id == chosen)?.DisplayName ?? CharacterChoice.PrototypeName);
        var drawn = chosen.Length > 0;
        if (_playerColorButton != null) _playerColorButton.Visible = !drawn;
        if (_playerColorNote != null) _playerColorNote.Visible = drawn;
    }

    /// <summary>"Change player color": the prototype figure's next colour of the five. A drawn character keeps its own colours.</summary>
    private void CyclePlayerColor()
    {
        if (DrawnCharacterChosen) return;
        _playerColor = (_playerColor + 1) % Palette.Length;
        Player.SetAppearance(Palette[_playerColor]);
        RefreshPrototypeSwatch();
    }

    /// <summary>The prototype's swatch (the row's first button) follows its colour.</summary>
    private void RefreshPrototypeSwatch()
    {
        if (_characterButtons.Count > 0) _characterButtons[0].Icon = SwatchFor(CharacterChoice.PrototypeId);
    }

    /// <summary>The swatch of a choice without a thumbnail: the prototype figure in its colour now, or a drawn character's tile.</summary>
    private ImageTexture SwatchFor(string id)
    {
        if (id.Length == 0)
        {
            if (!_prototypeSwatches.TryGetValue(_playerColor, out var figure))
                _prototypeSwatches[_playerColor] = figure = CharacterChoice.Swatch(id, Palette[_playerColor]);
            return figure;
        }
        if (!_tileSwatches.TryGetValue(id, out var tile)) _tileSwatches[id] = tile = CharacterChoice.Swatch(id, default);
        return tile;
    }

    /// <summary>
    /// The profile's character, loaded: a character the row shows is worn (through Player.SetCharacterId); anything else (a
    /// missing key, not a string, not a token, the Gubble, a character the library does not have) is the prototype figure.
    /// </summary>
    private void RestoreCharacter(Variant saved)
    {
        var id = saved.VariantType == Variant.Type.String ? saved.AsString() : CharacterChoice.PrototypeId;
        Player.SetCharacterId(RowHas(id) ? id : CharacterChoice.PrototypeId);
    }
}
