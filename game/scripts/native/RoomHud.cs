using Godot;
using System.Globalization;
using EnFractal.Native.Look;

namespace EnFractal.Native;

/// <summary>Local controls and avatar preferences; no agent authority or world-save access.</summary>
public partial class RoomHud : CanvasLayer
{
    public SmallPlayerController Player { get; set; } = null!;
    public CompanionAvatar Companion { get; set; } = null!;
    /// <summary>Set by RoomWorld so the L key can switch the room's lamps; null in fixtures without a look.</summary>
    public global::EnFractal.Native.Look.LookDirector? Look { get; set; }
    public int ViewMode { get; private set; } = 1;
    public string RoomTitle { get; set; } = "ROOM";
    /// <summary>Set by RoomWorld: what the look and the style pin need the player to know (a renderer fallback, a style that did not verify).</summary>
    public string LookNotice { get; set; } = "";
    public bool Customizing => _customization.Visible;
    private const string ProfilePath = "user://single_player/room/avatar_profile_v1.cfg";
    private static readonly Color[] Palette = { new("d28f63"), new("65b9b0"), new("d7b765"), new("a18cc3"), new("75965c") };
    private Label _state = null!;
    private Label _notice = null!;
    private PanelContainer _footer = null!;
    private VBoxContainer _keyHelp = null!;
    private Label _helpHint = null!;
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

    // Playtest clock keys (founder, second playtest: the clock follows real time, so a 3 a.m. playtest only showed
    // night). T steps the hour through the day's own light; Shift+T steps the date through the solstices and equinoxes.
    // Each steps round and ends back on the real clock (the real calendar for Shift+T).
    /// <summary>The steps of the T key. Their hours come from the sun's real rise and set on the day shown (TimeStopHour).</summary>
    public static readonly string[] TimeStopNames = { "dawn", "morning", "noon", "late afternoon", "sunset", "dusk", "night" };
    /// <summary>The steps of the Shift+T key, by day of the year (a 365-day year). Named for the astronomy, so they hold in either hemisphere.</summary>
    public static readonly (string Name, int DayOfYear)[] SeasonStops =
    {
        ("March equinox", 79), ("June solstice", 172), ("September equinox", 265), ("December solstice", 355),
    };
    /// <summary>The T step now (an index into TimeStopNames), or -1 while the hour follows the real clock.</summary>
    public int TimeStop { get; private set; } = -1;
    /// <summary>The Shift+T step now (an index into SeasonStops), or -1 while the date follows the real calendar.</summary>
    public int SeasonStop { get; private set; } = -1;
    private double _clockTimer;
    private Label _clock = null!;

    /// <summary>
    /// The hour (local standard time) of a T step on a day of the year. Dawn is a quarter of an hour before the sun's
    /// centre crosses the horizon, sunset a quarter of an hour before it sets (the golden hour), dusk half an hour
    /// after (the sun six degrees down, where the moon takes over), night two and a half hours after sunset.
    /// </summary>
    public static float TimeStopHour(StylePreset preset, int stop, int dayOfYear)
    {
        var (rise, set) = LookClock.SunTimes(preset.Tuning.Sun, dayOfYear);
        var hour = stop switch
        {
            0 => rise - 0.25f, 1 => rise + 2.5f, 2 => (rise + set) * 0.5f, 3 => set - 2.0f, 4 => set - 0.25f, 5 => set + 0.5f, _ => set + 2.5f,
        };
        return Mathf.PosMod(hour, 24f);
    }

    /// <summary>T: the next time of day, then back to the real clock after night. Does nothing in a room without a look.</summary>
    public void StepTimeOfDay()
    {
        if (Look == null) return;
        TimeStop = TimeStop + 1 >= TimeStopNames.Length ? -1 : TimeStop + 1;
        ApplyClock();
    }

    /// <summary>Shift+T: the next solstice or equinox, then back to the real calendar after the December solstice.</summary>
    public void StepSeason()
    {
        if (Look == null) return;
        SeasonStop = SeasonStop + 1 >= SeasonStops.Length ? -1 : SeasonStop + 1;
        ApplyClock();
    }

    /// <summary>Pin the look to what the two steps ask, each part falling back to the real clock or calendar; release the pin when both do.</summary>
    private void ApplyClock()
    {
        if (Look == null) return;
        _clockTimer = 0;
        if (TimeStop < 0 && SeasonStop < 0) { Look.ReleaseClock(); return; }
        var real = LookClock.Now(Look.Preset);
        var day = SeasonStop >= 0 ? SeasonStops[SeasonStop].DayOfYear : real.DayOfYear;
        Look.SetClock(TimeStop >= 0 ? TimeStopHour(Look.Preset, TimeStop, day) : real.Hour, day);
    }

    /// <summary>The clock line of the top panel: the hour and where it comes from, then the season and date likewise.</summary>
    public string ClockText()
    {
        if (Look?.Moment is not { } moment) return "";
        var minutes = Mathf.PosMod(Mathf.RoundToInt(moment.Hour * 60f), 24 * 60);
        var hour = $"{minutes / 60:00}:{minutes % 60:00} " + (TimeStop >= 0 ? TimeStopNames[TimeStop] + " (T)" : "real clock (T)");
        var date = new System.DateTime(2026, 1, 1).AddDays(moment.DayOfYear - 1).ToString("d MMM", CultureInfo.InvariantCulture);
        return $"{hour}  ·  {moment.Season}, {date}, " + (SeasonStop >= 0 ? SeasonStops[SeasonStop].Name + " (Shift+T)" : "real date (Shift+T)");
    }

    private Node3D _dioramaPivot = null!;
    private SpringArm3D _dioramaArm = null!;
    private Camera3D _diorama = null!;
    private int _playerColor;
    private int _companionColor = 1;
    private string _noticeText = "Placeholder room · captured rooms and the AI connection come next";

    public override void _Ready()
    {
        Name = "RoomHud";
        // Place the rendered camera before LookDirector computes its focus depth.
        ProcessPriority = -1;
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
        var compactTheme = (Theme)theme.Duplicate();
        compactTheme.DefaultFontSize = 14;
        var compactShade = (StyleBoxFlat)shade.Duplicate();
        compactShade.ContentMarginLeft = compactShade.ContentMarginRight = 8;
        compactShade.ContentMarginTop = compactShade.ContentMarginBottom = 6;
        compactTheme.SetStylebox("panel", "PanelContainer", compactShade);
        compactTheme.SetConstant("separation", "VBoxContainer", 2);
        compactTheme.SetConstant("separation", "HBoxContainer", 3);
        var top = new PanelContainer { Position = new Vector2(18, 18), Theme = compactTheme };
        AddChild(top);
        var column = new VBoxContainer(); top.AddChild(column);
        column.AddChild(new Label { Text = RoomTitle });
        _state = new Label { Name = "State" }; column.AddChild(_state);
        _clock = new Label { Visible = false }; column.AddChild(_clock);
        var actions = new HBoxContainer { Name = "CompanionActions" }; column.AddChild(actions);
        AddButton(actions, "1 Follow", () => Goal("follow"), 26);
        AddButton(actions, "2 Wait", () => Goal("stay"), 26);
        AddButton(actions, "3 Come", () => Goal("come"), 26);
        AddButton(actions, "4 Stop", () => Goal("stop"), 26);
        AddButton(actions, "5 Point", PointAhead, 26);
        AddButton(actions, "Customize", ToggleCustomization, 26);
        // The hand keys send the same sandbox commands the companion uses (CommandHost.PlayerHands, PlayerPush).
        var hands = new HBoxContainer { Name = "HandActions" }; column.AddChild(hands);
        AddButton(hands, "F Pick up / put down", Hands, 26);
        AddButton(hands, "V Push", Push, 26);
        _footer = new PanelContainer { Name = "HelpFooter", Theme = compactTheme, GrowVertical = Control.GrowDirection.Begin };
        AddChild(_footer);
        _footer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        _footer.OffsetLeft = 18; _footer.OffsetRight = -18; _footer.OffsetTop = _footer.OffsetBottom = -18;
        var help = new VBoxContainer(); _footer.AddChild(help);
        _helpHint = new Label { Text = "H keys" }; help.AddChild(_helpHint);
        _keyHelp = new VBoxContainer { Name = "KeyHelp", Visible = false }; help.AddChild(_keyHelp);
        _keyHelp.AddChild(new Label { Text = "WASD move · Shift run · Space jump · R recover · G gravity · click to look · Esc release" });
        _keyHelp.AddChild(new Label { Text = "Climb: keep walking into a steep face, W up, S down, A/D across, Space lets go · Swim: deep water floats you, Space leaps" });
        _keyHelp.AddChild(new Label { Text = "F1 eye · F2 shoulder · F3 diorama: mouse orbits, wheel zooms, WASD follows the view · F4 isometric: Q/E turn the view" });
        _keyHelp.AddChild(new Label { Text = "T time of day · Shift+T season (each steps round to the real clock) · L lamps · O observe (a very tight tilt-shift view, best from F3 or F4) · C customize" });
        _keyHelp.AddChild(new Label { Text = "F pick up what you face · F again sets it down in front of you, or on top of what you face (the box, the book) · V push what you face 10 cm" });
        _notice = new Label { Name = "Notice", Text = _noticeText }; help.AddChild(_notice);
        help.MinimumSizeChanged += () => _footer.Size = new Vector2(_footer.Size.X, 0);
        _customization = new PanelContainer { Position = new Vector2(18, 190), Theme = theme, Visible = false };
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

    private static void AddButton(Node parent, string text, System.Action action, int minimumHeight = 38)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, minimumHeight), FocusMode = Control.FocusModeEnum.All };
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
        // The rig is moved every rendered frame, not every physics tick, so it must not be interpolated; it follows the
        // player's interpolated position instead (PlaceDioramaRig). Without physics interpolation the two are the same.
        _dioramaPivot = new Node3D { Name = "DioramaPivot", PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
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
        // Where the bodies are drawn this frame: the interpolated position when physics interpolation is on, else the physics one.
        var playerAt = Player.GetGlobalTransformInterpolated().Origin;
        var centre = playerAt + Vector3.Up * (Player.BodyHeightM * 0.5f);
        if (Companion != null)
        {
            var companionAt = Companion.GetGlobalTransformInterpolated().Origin;
            var apart = companionAt.DistanceTo(playerAt);
            var share = 0.5f * (1f - Mathf.SmoothStep(0.7f * FrameCompanionWithinM, FrameCompanionWithinM, apart));
            centre = centre.Lerp(companionAt + Vector3.Up * (Companion.BodyHeightM * 0.5f), share);
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
        var holding = Host?.HeldName(Kernel.CommandHost.PlayerAvatar) ?? "";
        _state.Text = $"{Player.BodyHeightM * 100:0} cm player  ·  gravity {Player.WorldPhysicsId} (G)  ·  {Companion.CompanionName}: {Companion.CurrentIntent}" + (Companion.GoalBlocked ? " · path blocked" : "") +
            (holding.Length > 0 ? $"  ·  holding {holding} (F)" : "") + (Look?.Observe == true ? "  ·  observe view (O)" : "");
        _notice.Text = _noticeText;
        var clock = ClockText();
        _clock.Visible = clock.Length > 0;
        _clock.Text = clock;
        // With the date pinned and the hour left on the real clock, the pin follows real time as the look itself would.
        if (Look != null && TimeStop < 0 && SeasonStop >= 0 && (_clockTimer += delta) > LookDirector.ClockUpdateSeconds) ApplyClock();
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
                case Key.H:
                    _keyHelp.Visible = !_keyHelp.Visible;
                    _helpHint.Text = _keyHelp.Visible ? "H hide keys" : "H keys";
                    break;
                case Key.F1: SetViewMode(0); break;
                case Key.F2: SetViewMode(1); break;
                case Key.F3: SetViewMode(2); break;
                case Key.F4: SetViewMode(3); break;
                case Key.L when Look != null: Look.SetLamps(!Look.LampsOn); break;
                case Key.O when Look != null: Look.Observe = !Look.Observe; break;
                case Key.T when key.ShiftPressed: StepSeason(); break;
                case Key.T: StepTimeOfDay(); break;
                // Q and E belong to the view: the invention workshop that also bound them is retired (CommandHost).
                case Key.Q when ViewMode == 3: TurnIso(-1); break;
                case Key.E when ViewMode == 3: TurnIso(1); break;
                // Companion keys are goal commands from the player, on the same path as the companion's own.
                case Key.Key1: Goal("follow"); break;
                case Key.Key2: Goal("stay"); break;
                case Key.Key3: Goal("come"); break;
                case Key.Key4: Goal("stop"); break;
                case Key.Key5: PointAhead(); break;
                // Hand keys: pick up, put down and push, as commands from the player.
                case Key.F: Hands(); break;
                case Key.V: Push(); break;
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

    /// <summary>The room's command host (null in fixtures without one).</summary>
    private Kernel.CommandHost? Host => GetParent() is { } parent ? Kernel.CommandHost.Of(parent) : null;

    /// <summary>F: pick up what the player faces, or put down what it holds (on what it faces, if that has a top).</summary>
    private void Hands() => _noticeText = Host?.PlayerHands().Message ?? "The command host is not attached; the hand keys are off.";

    /// <summary>V: push what the player faces.</summary>
    private void Push() => _noticeText = Host?.PlayerPush().Message ?? "The command host is not attached; the hand keys are off.";

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
