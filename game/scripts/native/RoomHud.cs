using Godot;

namespace EnFractal.Native;

/// <summary>Local controls and avatar preferences; no agent authority or world-save access.</summary>
public partial class RoomHud : CanvasLayer
{
    public SmallPlayerController Player { get; set; } = null!;
    public CompanionAvatar Companion { get; set; } = null!;
    public int ViewMode { get; private set; } = 1;
    public string RoomTitle { get; set; } = "ROOM";
    public bool Customizing => _customization.Visible;
    private const string ProfilePath = "user://single_player/room/avatar_profile_v1.cfg";
    private static readonly Color[] Palette = { new("d28f63"), new("65b9b0"), new("d7b765"), new("a18cc3"), new("75965c") };
    private Label _state = null!;
    private Label _notice = null!;
    private PanelContainer _customization = null!;
    private SpringArm3D _arm = null!;
    private Camera3D _shoulder = null!;
    private Camera3D _reference = null!;
    private int _playerColor;
    private int _companionColor = 1;
    private string _noticeText = "Placeholder room · captured rooms and the AI connection come next";

    public override void _Ready()
    {
        Name = "RoomHud";
        BuildCameras();
        LoadPreferences();
        var theme = new Theme { DefaultFontSize = 18 };
        var shade = new StyleBoxFlat
        {
            BgColor = new Color(0.075f, 0.13f, 0.13f, 0.94f),
            ContentMarginLeft = 16, ContentMarginRight = 16,
            ContentMarginTop = 12, ContentMarginBottom = 12,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12
        };
        theme.SetStylebox("panel", "PanelContainer", shade);
        theme.SetColor("font_color", "Label", new Color("f0e6cf"));
        var top = new PanelContainer { Position = new Vector2(18, 18), Theme = theme };
        AddChild(top);
        var column = new VBoxContainer(); top.AddChild(column);
        column.AddChild(new Label { Text = RoomTitle });
        _state = new Label(); column.AddChild(_state);
        var actions = new HBoxContainer(); column.AddChild(actions);
        AddButton(actions, "1 Follow", Companion.Follow);
        AddButton(actions, "2 Wait", Companion.Stay);
        AddButton(actions, "3 Come", Companion.Come);
        AddButton(actions, "4 Stop", Companion.Stop);
        AddButton(actions, "5 Point", PointAhead);
        AddButton(actions, "Customize", ToggleCustomization);
        var footer = new PanelContainer { Theme = theme };
        AddChild(footer);
        footer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        footer.OffsetLeft = 18; footer.OffsetRight = -18; footer.OffsetTop = -140; footer.OffsetBottom = -18;
        var help = new VBoxContainer(); footer.AddChild(help);
        help.AddChild(new Label { Text = "WASD move · Shift run · Space jump · R recover · click to look · Esc release" });
        help.AddChild(new Label { Text = "F1 eye · F2 follow camera · F3 reference · C customize" });
        _notice = new Label { Text = _noticeText }; help.AddChild(_notice);
        _customization = new PanelContainer { Position = new Vector2(18, 164), Theme = theme, Visible = false };
        AddChild(_customization);
        var options = new VBoxContainer(); _customization.AddChild(options);
        options.AddChild(new Label { Text = "YOUR TWO AVATARS" });
        var name = new LineEdit { Text = Companion.CompanionName, MaxLength = 40, PlaceholderText = "Companion name", CustomMinimumSize = new Vector2(360, 40) };
        options.AddChild(name);
        name.TextChanged += value => Companion.SetDisplayName(value);
        AddButton(options, "Change player color", () => { _playerColor = (_playerColor + 1) % Palette.Length; Player.SetAppearance(Palette[_playerColor]); });
        AddButton(options, "Change companion color", () => { _companionColor = (_companionColor + 1) % Palette.Length; Companion.SetAppearance(Palette[_companionColor]); });
        options.AddChild(new Label { Text = $"Player height: {Player.BodyHeightM * 100:0} cm. Appearance keeps each avatar's identity." });
        AddButton(options, "Save appearance and return", () => { SavePreferences(); ToggleCustomization(); });
        SetViewMode(1);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private static void AddButton(Node parent, string text, System.Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 38), FocusMode = Control.FocusModeEnum.All };
        button.Pressed += action;
        parent.AddChild(button);
    }

    private void BuildCameras()
    {
        _arm = new SpringArm3D { Name = "FollowCameraArm", Position = new Vector3(0.13f, 0.24f, 0), SpringLength = 0.95f, Margin = 0.025f, CollisionMask = 1 };
        Player.AddChild(_arm);
        _arm.AddExcludedObject(Player.GetRid());
        _shoulder = new Camera3D { Name = "FollowCamera", Near = 0.01f, Far = 2500, Fov = 68 };
        _arm.AddChild(_shoulder);
        _reference = new Camera3D { Name = "ReferenceCamera", Near = 0.02f, Far = 2500, Fov = 65 };
        Player.GetParent().AddChild(_reference);
    }

    public void SetViewMode(int mode)
    {
        if (mode is < 0 or > 2 || _shoulder == null) return;
        ViewMode = mode;
        Player.GetNode<Node3D>("OriginalPrototypeBody").Visible = mode != 0;
        if (mode == 0) Player.EyeCamera.MakeCurrent();
        else if (mode == 1) _shoulder.MakeCurrent();
        else
        {
            _reference.GlobalPosition = Player.GlobalPosition + Player.GlobalBasis.Z * 3 + Vector3.Up * 2;
            _reference.LookAt(Player.GlobalPosition + Vector3.Up * 0.20f);
            _reference.MakeCurrent();
        }
    }

    public override void _Process(double delta)
    {
        _arm.Rotation = new Vector3(Mathf.Clamp(Player.EyeCamera.Rotation.X - 0.18f, -1.1f, 0.8f), 0, 0);
        _state.Text = $"{Player.BodyHeightM * 100:0} cm player  ·  {Companion.CompanionName}: {Companion.CurrentIntent}" + (Companion.GoalBlocked ? " · path blocked" : "");
        _notice.Text = _noticeText;
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey key && key.Pressed && !key.Echo)
        {
            var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
            if (code == Key.Escape)
            {
                if (Customizing) ToggleCustomization();
                Input.MouseMode = Input.MouseModeEnum.Visible;
            }
            if (code == Key.C) ToggleCustomization();
            if (Customizing) return;
            switch (code)
            {
                case Key.F1: SetViewMode(0); break;
                case Key.F2: SetViewMode(1); break;
                case Key.F3: SetViewMode(2); break;
                case Key.Key1: Companion.Follow(); break;
                case Key.Key2: Companion.Stay(); break;
                case Key.Key3: Companion.Come(); break;
                case Key.Key4: Companion.Stop(); break;
                case Key.Key5: PointAhead(); break;
            }
        }
        if (!Customizing && input is InputEventMouseButton click && click.Pressed && click.ButtonIndex == MouseButton.Left)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void PointAhead() => Companion.PointAt(Player.GlobalPosition - Player.GlobalBasis.Z * 2 + Vector3.Up * 0.15f);

    private void ToggleCustomization()
    {
        _customization.Visible = !_customization.Visible;
        Player.SetInputEnabled(!_customization.Visible);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void LoadPreferences()
    {
        var config = new ConfigFile();
        var result = config.Load(ProfilePath);
        if (result == Error.FileNotFound) return;
        if (result != Error.Ok || config.GetValue("profile", "version", 0).VariantType != Variant.Type.Int || config.GetValue("profile", "version", 0).AsInt32() != 1)
        { _noticeText = "Appearance preferences could not be loaded; defaults are active."; return; }
        var playerColor = config.GetValue("profile", "player_color", 0);
        var companionColor = config.GetValue("profile", "companion_color", 1);
        if (playerColor.VariantType == Variant.Type.Int) _playerColor = Mathf.PosMod(playerColor.AsInt32(), Palette.Length);
        if (companionColor.VariantType == Variant.Type.Int) _companionColor = Mathf.PosMod(companionColor.AsInt32(), Palette.Length);
        var name = config.GetValue("profile", "companion_name", "Wisp");
        if (name.VariantType == Variant.Type.String) Companion.SetDisplayName(name.AsString());
        Player.SetAppearance(Palette[_playerColor]);
        Companion.SetAppearance(Palette[_companionColor]);
    }

    private void SavePreferences()
    {
        var directory = ProjectSettings.GlobalizePath("user://single_player/room");
        var config = new ConfigFile();
        config.SetValue("profile", "version", 1);
        config.SetValue("profile", "player_color", _playerColor);
        config.SetValue("profile", "companion_color", _companionColor);
        config.SetValue("profile", "companion_name", Companion.CompanionName);
        var temporary = ProfilePath + ".tmp";
        var prepared = DirAccess.MakeDirRecursiveAbsolute(directory) == Error.Ok && config.Save(temporary) == Error.Ok;
        var committed = prepared && DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(temporary), ProjectSettings.GlobalizePath(ProfilePath)) == Error.Ok;
        _noticeText = committed ? "Appearance saved. Room changes are not saved by this placeholder." : "Could not save appearance; current session is still usable.";
    }
}
