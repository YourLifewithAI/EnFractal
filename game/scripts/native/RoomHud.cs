using Godot;

namespace EnFractal.Native;

/// <summary>Local controls and avatar preferences; no agent authority or world-save access.</summary>
public partial class RoomHud : CanvasLayer
{
    public SmallPlayerController Player { get; set; } = null!;
    public CompanionAvatar Companion { get; set; } = null!;
    public int ViewMode { get; private set; } = 1;
    public string RoomTitle { get; set; } = "ROOM";
    /// <summary>Set by RoomWorld: what the look and the style pin need the player to know (a renderer fallback, a style that did not verify).</summary>
    public string LookNotice { get; set; } = "";
    public bool Customizing => _customization.Visible;
    private const string ProfilePath = "user://single_player/room/avatar_profile_v1.cfg";
    private static readonly Color[] Palette = { new("d28f63"), new("65b9b0"), new("d7b765"), new("a18cc3"), new("75965c") };
    private Label _state = null!;
    private Label _notice = null!;
    private PanelContainer _customization = null!;
    private SpringArm3D _arm = null!;
    private Camera3D _shoulder = null!;
    // F3, the diorama view: a high-angle camera orbiting the player (founder playtest, 6 October: F3 froze).
    // Framed for the 10 cm body; a narrow lens and the look's depth of field make the room read as a miniature.
    public const float DioramaMinPitchDeg = 20.0f;
    public const float DioramaMaxPitchDeg = 80.0f;
    public const float DioramaDefaultPitchDeg = 45.0f;
    public const float DioramaMinDistanceM = 0.30f;
    public const float DioramaMaxDistanceM = 2.4f;
    public const float DioramaDefaultDistanceM = 1.4f;
    public const float DioramaFovDeg = 40.0f;
    // F4, the isometric view (Look lane, 6 October art direction): the isometric angle, a long narrow lens, and a
    // heading that turns in quarter turns with Q and E, so the room reads as a diorama from above.
    public const float IsoPitchDeg = 35.26f;
    public const float IsoDistanceM = 2.7f;
    public const float IsoFovDeg = 30.0f;
    /// <summary>While the companion is this close, the high views frame both avatars, centred between them.</summary>
    public const float FrameCompanionWithinM = 0.8f;
    /// <summary>The isometric heading: 45 degrees off the room's axes plus whole quarter turns.</summary>
    public float IsoYaw { get; private set; } = Mathf.Pi / 4;
    public const float MouseRadiansPerPixel = 0.0025f;
    /// <summary>The orbit's heading (radians about +Y; 0 looks along -Z). Movement follows it in F3.</summary>
    public float DioramaYaw { get; private set; }
    /// <summary>How far the diorama camera looks down, in degrees.</summary>
    public float DioramaPitchDeg { get; private set; } = DioramaDefaultPitchDeg;
    /// <summary>The wanted distance from the player; walls and the ceiling may hold the camera closer.</summary>
    public float DioramaDistanceM { get; private set; } = DioramaDefaultDistanceM;
    public Camera3D DioramaCamera => _diorama;
    private Node3D _dioramaPivot = null!;
    private SpringArm3D _dioramaArm = null!;
    private Camera3D _diorama = null!;
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
        AddButton(actions, "1 Follow", () => Goal("follow"));
        AddButton(actions, "2 Wait", () => Goal("stay"));
        AddButton(actions, "3 Come", () => Goal("come"));
        AddButton(actions, "4 Stop", () => Goal("stop"));
        AddButton(actions, "5 Point", PointAhead);
        AddButton(actions, "Customize", ToggleCustomization);
        var footer = new PanelContainer { Theme = theme };
        AddChild(footer);
        footer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        footer.OffsetLeft = 18; footer.OffsetRight = -18; footer.OffsetTop = -140; footer.OffsetBottom = -18;
        var help = new VBoxContainer(); footer.AddChild(help);
        help.AddChild(new Label { Text = "WASD move · Shift run · Space jump · R recover · G gravity · click to look · Esc release" });
        help.AddChild(new Label { Text = "F1 eye · F2 shoulder · F3 diorama: mouse orbits, wheel zooms, WASD follows the view · F4 isometric: Q/E turn · C customize" });
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
        if (LookNotice.Length > 0) _noticeText = LookNotice;
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
        // Over-the-shoulder framing scales with the body (0.10 m player: 8 cm up, 32 cm behind).
        var height = Player.BodyHeightM;
        _arm = new SpringArm3D { Name = "FollowCameraArm", Position = new Vector3(Player.BodyRadiusM * 2.2f, height * 0.8f, 0), SpringLength = height * 3.2f, Margin = height * 0.1f, CollisionMask = 1 };
        Player.AddChild(_arm);
        _arm.AddExcludedObject(Player.GetRid());
        _shoulder = new Camera3D { Name = "FollowCamera", Near = 0.005f, Far = 100, Fov = 68 };
        _arm.AddChild(_shoulder);
        // The diorama rig is not a child of the body, so it orbits independently of the body's facing. The arm's
        // sphere keeps the lens out of walls, furniture and the ceiling; only world geometry (layer 1) stops it.
        _dioramaPivot = new Node3D { Name = "DioramaPivot" };
        Player.GetParent().AddChild(_dioramaPivot);
        _dioramaArm = new SpringArm3D
        {
            Name = "DioramaArm", SpringLength = DioramaDistanceM, Margin = 0.01f, CollisionMask = 1,
            Shape = new SphereShape3D { Radius = 0.02f }
        };
        _dioramaPivot.AddChild(_dioramaArm);
        _dioramaArm.AddExcludedObject(Player.GetRid());
        if (Companion != null) _dioramaArm.AddExcludedObject(Companion.GetRid());
        _diorama = new Camera3D { Name = "DioramaCamera", Near = 0.01f, Far = 100, Fov = DioramaFovDeg };
        _dioramaArm.AddChild(_diorama);
        PlaceDioramaRig(snap: true);
    }

    public void SetViewMode(int mode)
    {
        if (mode is < 0 or > 3 || _shoulder == null) return;
        var entering = mode == 2 && ViewMode != 2;
        // Enter the isometric view at the quarter-turn heading nearest the player's facing.
        if (mode == 3 && ViewMode != 3) IsoYaw = Mathf.Wrap(Mathf.Snapped(Player.GlobalRotation.Y - Mathf.Pi / 4, Mathf.Pi / 2) + Mathf.Pi / 4, -Mathf.Pi, Mathf.Pi);
        ViewMode = mode;
        Player.GetNode<Node3D>("OriginalPrototypeBody").Visible = mode != 0;
        if (mode == 0) Player.EyeCamera.MakeCurrent();
        else if (mode == 1) _shoulder.MakeCurrent();
        else
        {
            // Enter behind the player, at the last pitch and zoom the player chose.
            if (entering) DioramaYaw = Player.GlobalRotation.Y;
            _diorama.Fov = mode == 3 ? IsoFovDeg : DioramaFovDeg;
            PlaceDioramaRig(snap: true);
            _diorama.MakeCurrent();
        }
        Player.SetMovementFrame(mode switch { 2 => DioramaYaw, 3 => IsoYaw, _ => null });
    }

    /// <summary>Orbit the diorama camera by a mouse movement in pixels: sideways turns around the player, up and down tilts.</summary>
    public void OrbitDiorama(Vector2 mouseRelative)
    {
        if (!mouseRelative.IsFinite()) return;
        DioramaYaw = Mathf.Wrap(DioramaYaw - mouseRelative.X * MouseRadiansPerPixel, -Mathf.Pi, Mathf.Pi);
        DioramaPitchDeg = Mathf.Clamp(DioramaPitchDeg + Mathf.RadToDeg(mouseRelative.Y * MouseRadiansPerPixel), DioramaMinPitchDeg, DioramaMaxPitchDeg);
        if (ViewMode == 2) Player.SetMovementFrame(DioramaYaw);
    }

    /// <summary>Zoom by wheel steps: positive moves in, negative out. Each step is about 12 %.</summary>
    public void ZoomDiorama(float steps)
    {
        if (!float.IsFinite(steps)) return;
        DioramaDistanceM = Mathf.Clamp(DioramaDistanceM * Mathf.Pow(0.88f, steps), DioramaMinDistanceM, DioramaMaxDistanceM);
    }

    /// <summary>Turn the isometric view a quarter turn (Q: -1, E: +1).</summary>
    public void TurnIso(int quarterTurns)
    {
        IsoYaw = Mathf.Wrap(IsoYaw + quarterTurns * Mathf.Pi / 2, -Mathf.Pi, Mathf.Pi);
        if (ViewMode == 3) Player.SetMovementFrame(IsoYaw);
    }

    /// <summary>
    /// Centre the rig on the player's middle, or between the two avatars while the companion is near (fading over the
    /// last 30% of FrameCompanionWithinM so the view never jumps), eased so a jump or a step does not jolt the view.
    /// </summary>
    private void PlaceDioramaRig(bool snap, float delta = 0)
    {
        var centre = Player.GlobalPosition + Vector3.Up * (Player.BodyHeightM * 0.5f);
        if (Companion != null)
        {
            var apart = Companion.GlobalPosition.DistanceTo(Player.GlobalPosition);
            var share = 0.5f * (1f - Mathf.SmoothStep(0.7f * FrameCompanionWithinM, FrameCompanionWithinM, apart));
            centre = centre.Lerp(Companion.GlobalPosition + Vector3.Up * (Companion.BodyHeightM * 0.5f), share);
        }
        var weight = snap ? 1.0f : 1.0f - Mathf.Exp(-12.0f * delta);
        var position = _dioramaPivot.GlobalPosition.Lerp(centre, weight);
        var iso = ViewMode == 3;
        var pitch = iso ? IsoPitchDeg : DioramaPitchDeg;
        _dioramaPivot.GlobalTransform = new Transform3D(Basis.FromEuler(new Vector3(-Mathf.DegToRad(pitch), iso ? IsoYaw : DioramaYaw, 0), EulerOrder.Yxz), position);
        _dioramaArm.SpringLength = iso ? IsoDistanceM : DioramaDistanceM;
    }

    public override void _Process(double delta)
    {
        if (ViewMode >= 2) PlaceDioramaRig(snap: false, (float)delta);
        _arm.Rotation = new Vector3(Mathf.Clamp(Player.EyeCamera.Rotation.X - 0.18f, -1.1f, 0.8f), 0, 0);
        _state.Text = $"{Player.BodyHeightM * 100:0} cm player  ·  gravity {Player.WorldPhysicsId} (G)  ·  {Companion.CompanionName}: {Companion.CurrentIntent}" + (Companion.GoalBlocked ? " · path blocked" : "");
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
                case Key.F4: SetViewMode(3); break;
                case Key.Q when ViewMode == 3: TurnIso(-1); break;
                case Key.E when ViewMode == 3: TurnIso(1); break;
                // Companion keys are goal commands from the player, on the same path as the companion's own.
                case Key.Key1: Goal("follow"); break;
                case Key.Key2: Goal("stay"); break;
                case Key.Key3: Goal("come"); break;
                case Key.Key4: Goal("stop"); break;
                case Key.Key5: PointAhead(); break;
            }
        }
        if (!Customizing && input is InputEventMouseButton click && click.Pressed && click.ButtonIndex == MouseButton.Left)
            Input.MouseMode = Input.MouseModeEnum.Captured;
        if (ViewMode == 2 && !Customizing)
        {
            // The diorama camera owns the mouse in F3; the body does not turn with it (SetMovementFrame).
            if (input is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                OrbitDiorama(motion.Relative);
                GetViewport().SetInputAsHandled();
            }
            else if (input is InputEventMouseButton wheel && wheel.Pressed &&
                wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                ZoomDiorama(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private void PointAhead() => Goal("point_at", Player.GlobalPosition - Player.GlobalBasis.Z * 0.6f + Vector3.Up * 0.05f);

    private void Goal(string goal, Vector3? point = null)
    {
        var host = Kernel.CommandHost.Of(GetParent());
        if (host == null) { _noticeText = "The command host is not attached; companion keys are off."; return; }
        var result = host.PlayerGoal(goal, point);
        if (!result["ok"]!.GetValue<bool>()) _noticeText = result["error"]!["message"]!.GetValue<string>();
    }

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
