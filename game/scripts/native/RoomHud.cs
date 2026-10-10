using Godot;
using System.Collections.Generic;
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
    /// <summary>The player's profile (appearance, the Gubble's name and colour, has_used_glow). A test seam: the HUD's tests point it elsewhere.</summary>
    public static string ProfilePath { get; set; } = DefaultProfilePath;
    public const string DefaultProfilePath = "user://single_player/room/avatar_profile_v1.cfg";
    private static readonly Color[] Palette = { new("d28f63"), new("65b9b0"), new("d7b765"), new("a18cc3"), new("75965c") };
    private Label _state = null!;
    /// <summary>The wash-ashore fade (the sea's edge): a full-screen veil the player's body darkens and lifts.</summary>
    private ColorRect _washVeil = null!;
    // The focus (RUN-2-OPEN-SEA.md, "Things to touch"): the thing the next hand key would act on, lit by the look, with a tag.
    private Label _focusTag = null!;
    private Node3D? _focusNode;
    private Vector3 _focusTop;
    private double _focusAge = double.MaxValue;
    private Vector3 _focusFrom;
    private float _focusYaw;
    private string _focusHeld = "";
    /// <summary>The focus is asked for again at most this often while the player moves or turns, and at least this often anyway (things move).</summary>
    public const double FocusQueryS = 0.1;
    public const double FocusRefreshS = 0.5;
    /// <summary>The thing the focus is on (null for none).</summary>
    public Node3D? FocusTarget => _focusNode;
    /// <summary>The focus tag (its words, and whether and where it shows).</summary>
    public Label FocusTag => _focusTag;
    /// <summary>Times the focus was asked for (the test keeps it cheap).</summary>
    public int FocusQueries { get; private set; }
    private float _armLength;
    /// <summary>How far over the bed the shoulder camera keeps its lens (the open-sea bed has no collider for the arm to meet).</summary>
    public const float ArmBedClearanceM = 0.02f;

    /// <summary>
    /// The shoulder arm's length so its end stays ArmBedClearanceM over the water's bed (Codex Astra's review: at the open-sea
    /// bed, looking up, the camera went 15 cm under it). The arm runs from origin along direction (unit) for up to full.
    /// </summary>
    public static float ArmLengthOverBed(Vector3 origin, Vector3 direction, float full, float bedY, float clearance)
    {
        if (!float.IsFinite(bedY) || direction.Y >= -1e-4f) return full;
        var room = origin.Y - (bedY + clearance);
        return room <= 0 ? 0.01f : Mathf.Clamp(room / -direction.Y, 0.01f, full);
    }
    /// <summary>The movement keys, with B home from anywhere (the founder, 9 October: to the jetty, or the beach, or the start).</summary>
    private string MoveKeys => $"{PlayerControls.MoveLabel()} move · {K(Act.MoveSprint)} run · {K(Act.Jump)} jump · {K(Act.BodyHome)} back to {Player.HomeName} · {K(Act.BodyRecover)} recover · {PlayerControls.DragLabel()} to look around";
    /// <summary>What an action's key is now, for the words on screen: they follow the input map, so a remapped key shows at once.</summary>
    private static string K(StringName action) => PlayerControls.Label(action);
    /// <summary>Every label and button that names a key, with the words it shows now. Rebuilt every frame (RefreshKeyText); a label whose words did not change is not touched.</summary>
    private readonly List<(System.Action<string> Set, System.Func<string> Words)> _keyTexts = new();
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
    // heading that turns in quarter turns with [ and ], so the room reads as a diorama from above.
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
        var hour = $"{minutes / 60:00}:{minutes % 60:00} " + (TimeStop >= 0 ? TimeStopNames[TimeStop] : "real clock") + $" ({K(Act.TimeStep)})";
        var date = new System.DateTime(2026, 1, 1).AddDays(moment.DayOfYear - 1).ToString("d MMM", CultureInfo.InvariantCulture);
        return $"{hour}  ·  {moment.Season}, {date}, " + (SeasonStop >= 0 ? SeasonStops[SeasonStop].Name : "real date") + $" ({K(Act.SeasonStep)})";
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
        // The Gubble's orders as buttons too: the same commands as the keys, the wheel and the smart ask (RoomHud.Gubble.cs).
        var actions = new HBoxContainer { Name = "CompanionActions" }; column.AddChild(actions);
        // The Gubble's keys under its name (the Glow playtest: whose abilities these are).
        AddKeyLine(actions, () => Companion.NameTag + ":", "CompanionName");
        AddButton(actions, "Follow", () => Order("follow"), 26);
        AddButton(actions, "Wait", () => Order("stay"), 26);
        AddKeyButton(actions, () => $"{K(Act.GubbleRecall)} Come", Recall, 26);
        AddKeyButton(actions, () => $"{K(Act.GubbleStop)} Stop", StopGubble, 26);
        AddKeyButton(actions, () => $"{K(Act.GubbleSlot1)} {SlotName(0)}", () => CastSlot(0, AimNow()), 26);
        AddButton(actions, "Customize", ToggleCustomization, 26);
        // The hand keys send the same sandbox commands the companion uses (CommandHost.PlayerHands, PlayerPush).
        var hands = new HBoxContainer { Name = "HandActions" }; column.AddChild(hands);
        AddKeyButton(hands, () => $"{K(Act.Hands)} Pick up / put down", Hands, 26);
        AddKeyButton(hands, () => $"{K(Act.Push)} Push", Push, 26);
        _focusTag = new Label { Name = "FocusTag", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Theme = compactTheme };
        AddChild(_focusTag);
        _washVeil = new ColorRect { Name = "HomeVeil", Color = new Color(0.06f, 0.13f, 0.18f, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_washVeil);
        _washVeil.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Player.WentHome += body => _noticeText = body.LastHome switch
        {
            "jetty" => "Home: back at the jetty.",
            "spawn" => "Home: back where you started.",
            "" => "There was no room at home; you are back where you last stood.",
            _ => "Home: back on the beach.",
        };
        _footer = new PanelContainer { Name = "HelpFooter", Theme = compactTheme, GrowVertical = Control.GrowDirection.Begin };
        AddChild(_footer);
        _footer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        _footer.OffsetLeft = 18; _footer.OffsetRight = -18; _footer.OffsetTop = _footer.OffsetBottom = -18;
        var help = new VBoxContainer(); _footer.AddChild(help);
        _helpHint = new Label(); help.AddChild(_helpHint);
        _keyTexts.Add((words => _helpHint.Text = words, () => $"{K(Act.HudHelp)} " + (_keyHelp.Visible ? "hide keys" : "keys")));
        _keyHelp = new VBoxContainer { Name = "KeyHelp", Visible = false }; help.AddChild(_keyHelp);
        // Every key named below comes from the input map (PlayerControls.Label), so a remapped key shows here.
        AddKeyLine(_keyHelp, () => MoveKeys, "MoveKeys");
        AddKeyLine(_keyHelp, () => $"Climb: keep walking into a steep face, {K(Act.MoveForward)} up, {K(Act.MoveBack)} down, {K(Act.MoveLeft)}/{K(Act.MoveRight)} across, {K(Act.Jump)} lets go · Swim: deep water floats you, {K(Act.Jump)} leaps · Dive: hold {K(Act.MoveDive)}, {K(Act.Jump)} rises, {K(Act.MoveForward)} swims the way you look, let go to drift up");
        AddKeyLine(_keyHelp, () => $"{K(Act.ViewEye)} eye · {K(Act.ViewShoulder)} shoulder · {K(Act.ViewDiorama)} diorama: {PlayerControls.DragLabel()} orbits, {PlayerControls.ZoomLabel()} zooms, {PlayerControls.MoveLabel()} follows the view · {K(Act.ViewIso)} isometric: {K(Act.IsoTurnLeft)}/{K(Act.IsoTurnRight)} turn the view");
        AddKeyLine(_keyHelp, () => $"{K(Act.ViewObserve)} observe (a very tight tilt-shift view, best from {K(Act.ViewDiorama)} or {K(Act.ViewIso)}) · {K(Act.HudCustomize)} customize ({K(Act.MouseRelease)} closes it)");
        AddKeyLine(_keyHelp, () => $"{K(Act.Hands)} pick up what you face · {K(Act.Hands)} again sets it down in front of you, or on top of what you face (the box, the book) · {K(Act.Push)} push what you face 10 cm");
        // The Gubble's keys, under its name (the Glow playtest: the player should see whose magic this is).
        AddKeyLine(_keyHelp, () => $"{Companion.NameTag}, your companion:", "GubbleHeading");
        AddKeyLine(_keyHelp, () => $"{K(Act.GubbleAsk)} asks for what fits where you point (wait or follow, fetch, light the dark, go and look); hold it for the wheel · {K(Act.GubbleRecall)} come, then follow · {K(Act.GubbleStop)} stop", "GubbleKeys");
        AddKeyLine(_keyHelp, () => $"{K(Act.GubbleSlot1)} {SlotName(0)} where you point (it comes closer first if it must; at the sky, on itself) · {K(Act.GubbleSlot2)}/{K(Act.GubbleSlot3)}/{K(Act.GubbleSlot4)}/{K(Act.GubbleSlot5)} magic still to come · it floats after you, over water and up cliffs", "GubbleSlots");
        // The toggles that bend the world for testing, apart from the keys the game is played with.
        _keyHelp.AddChild(new Label { Name = "TestingHeading", Text = "Testing" });
        AddKeyLine(_keyHelp, () => $"{K(Act.PhysicsNext)} gravity · {K(Act.TimeStep)} time of day · {K(Act.SeasonStep)} season (each steps round to the real clock) · {K(Act.Lamps)} lamps", "TestingKeys");
        _notice = new Label { Name = "Notice", Text = _noticeText }; help.AddChild(_notice);
        help.MinimumSizeChanged += () => _footer.Size = new Vector2(_footer.Size.X, 0);
        _customization = new PanelContainer { Position = new Vector2(18, 190), Theme = theme, Visible = false };
        AddChild(_customization);
        var options = new VBoxContainer(); _customization.AddChild(options);
        options.AddChild(new Label { Text = "YOUR TWO AVATARS" });
        var name = new LineEdit { Text = Companion.CompanionName, MaxLength = 40, PlaceholderText = "Your companion's name (the Gubble)", CustomMinimumSize = new Vector2(360, 40) };
        options.AddChild(name);
        name.TextChanged += value => Companion.SetDisplayName(value);
        AddButton(options, "Change player color", () => { _playerColor = (_playerColor + 1) % Palette.Length; Player.SetAppearance(Palette[_playerColor]); });
        AddButton(options, "Change the Gubble's color", () => { _companionColor = (_companionColor + 1) % Palette.Length; Companion.SetAppearance(Palette[_companionColor]); ApplyAura(); });
        options.AddChild(new Label { Text = $"Player height: {Player.BodyHeightM * 100:0} cm. Appearance keeps each avatar's identity." });
        AddButton(options, "Save appearance and return", () => { SavePreferences(); ToggleCustomization(); });
        BuildGubbleControls(compactTheme);
        ApplyAura();
        SetViewMode(1);
        if (LookNotice.Length > 0) _noticeText = LookNotice;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        RefreshKeyText();
    }

    /// <summary>A help line: its words are rebuilt from the input map every frame.</summary>
    private void AddKeyLine(Node parent, System.Func<string> words, string name = "")
    {
        var label = new Label();
        if (name.Length > 0) label.Name = name;
        parent.AddChild(label);
        _keyTexts.Add((text => label.Text = text, words));
    }

    /// <summary>A button that names its key: its words are rebuilt from the input map every frame.</summary>
    private void AddKeyButton(Node parent, System.Func<string> words, System.Action action, int minimumHeight = 38)
    {
        var button = new Button { CustomMinimumSize = new Vector2(0, minimumHeight), FocusMode = Control.FocusModeEnum.All };
        button.Pressed += action;
        parent.AddChild(button);
        _keyTexts.Add((text => button.Text = text, words));
    }

    private void RefreshKeyText()
    {
        foreach (var (set, words) in _keyTexts) set(words());
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
        _armLength = _arm.SpringLength;
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
        IgnoreCollisionOnlyParts();
    }

    /// <summary>
    /// A shell part that is never drawn ("drawn": false: a tree's hidden climbing pole and crown caps) is no obstacle to a
    /// camera: the arms pass through it as through the leaves around it. (Founder's playtest, 9 October: inside a crown the
    /// follow camera was pulled in to the climber's head by the hidden caps.)
    /// </summary>
    private void IgnoreCollisionOnlyParts()
    {
        if (GetParent() is not RoomWorld { Built: { } built }) return;
        foreach (var node in built.FindChildren("*", "StaticBody3D", true, false))
            if (node is StaticBody3D body && body.HasMeta("drawn") && !body.GetMeta("drawn").AsBool())
            {
                _arm.AddExcludedObject(body.GetRid());
                _dioramaArm.AddExcludedObject(body.GetRid());
            }
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

    /// <summary>Turn the isometric view a quarter turn ([: -1, ]: +1).</summary>
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
        // A drag whose release the HUD never saw (let go outside the window, a lost focus) ends: the cursor comes back.
        if (_drag.Pressed && !(DragHeldForTests?.Invoke() ?? PlayerControls.Held(Act.LookDrag))) EndDrag();
        // While the wheel has the game paused, only the wheel's clock runs.
        _wheel.Tick(delta);
        if (GubblePaused) return;
        if (ViewMode >= 2) PlaceDioramaRig(snap: false, (float)delta);
        _arm.Rotation = new Vector3(Mathf.Clamp(Player.EyeCamera.Rotation.X - 0.18f, -1.1f, 0.8f), 0, 0);
        _arm.SpringLength = ArmLengthOverBed(_arm.GlobalPosition, _arm.GlobalBasis.Z, _armLength, Player.Water.Wet ? Player.Water.BedY : float.NegativeInfinity, ArmBedClearanceM);
        var holding = Host?.HeldName(Kernel.CommandHost.PlayerAvatar) ?? "";
        _state.Text = $"{Player.BodyHeightM * 100:0} cm player  ·  gravity {Player.WorldPhysicsId} ({K(Act.PhysicsNext)})  ·  {Companion.CompanionName}: {Companion.CurrentIntent}" +
            (Companion.FloatingThere ? " · floating there" : "") + (Companion.GoalBlocked ? " · path blocked" : "") +
            (holding.Length > 0 ? $"  ·  holding {holding} ({K(Act.Hands)})" : "") + (Look?.Observe == true ? $"  ·  observe view ({K(Act.ViewObserve)})" : "");
        _notice.Text = _noticeText;
        UpdateFocus(delta);
        UpdateAskTag(delta);
        UpdateChains();
        UpdateDusk(delta);
        _washVeil.Color = new Color(_washVeil.Color, Player.HomeFade);
        RefreshKeyText();
        var clock = ClockText();
        _clock.Visible = clock.Length > 0;
        _clock.Text = clock;
        // With the date pinned and the hour left on the real clock, the pin follows real time as the look itself would.
        if (Look != null && TimeStop < 0 && SeasonStop >= 0 && (_clockTimer += delta) > LookDirector.ClockUpdateSeconds) ApplyClock();
    }

    /// <summary>
    /// The focus, every frame: the look's highlight on the thing the next hand key would act on, in every view, and the tag
    /// beside it. None while climbing, diving or on the way home, or with nothing to act on. The host is asked again only
    /// when the player has moved, turned or picked up or put down, at most every FocusQueryS, and every FocusRefreshS anyway.
    /// </summary>
    private void UpdateFocus(double delta)
    {
        var host = Host;
        _focusAge += delta;
        if (host == null || Player.IsClimbing || Player.IsDiving || Player.GoingHome || Customizing)
        {
            SetFocus(null, default, "");
            _focusAge = double.MaxValue;
        }
        else
        {
            var held = host.HeldBy(Kernel.CommandHost.PlayerAvatar) ?? "";
            var changed = Player.GlobalPosition.DistanceSquaredTo(_focusFrom) > 1e-6f || Mathf.Abs(Mathf.AngleDifference(Player.GlobalRotation.Y, _focusYaw)) > 0.02f || held != _focusHeld;
            if ((changed && _focusAge >= FocusQueryS) || _focusAge >= FocusRefreshS)
            {
                _focusAge = 0;
                _focusFrom = Player.GlobalPosition;
                _focusYaw = Player.GlobalRotation.Y;
                _focusHeld = held;
                FocusQueries++;
                var focus = host.PlayerFocus();
                SetFocus(string.IsNullOrEmpty(focus.Id) ? null : host.ObjectNodeOf(focus.Id), focus.Box, focus.Tag ?? "");
            }
        }
        PlaceFocusTag();
    }

    private void SetFocus(Node3D? node, Aabb box, string tag)
    {
        if (node != null && !IsInstanceValid(node)) node = null;
        if (node != _focusNode)
        {
            _focusNode = node;
            Look?.SetFocusHighlight(node);
        }
        // The tag rides over the middle of the thing's top, as an offset from its node, so it follows a push at once.
        if (node != null) _focusTop = new Vector3(box.GetCenter().X, box.End.Y, box.GetCenter().Z) - node.GlobalPosition;
        if (node == null) tag = "";
        if (_focusTag.Text != tag) _focusTag.Text = tag;
    }

    /// <summary>Beside the thing on screen, never in the top-right corner (the minimap's); hidden when the thing is behind the view.</summary>
    private void PlaceFocusTag()
    {
        var camera = GetViewport().GetCamera3D();
        if (_focusNode == null || !IsInstanceValid(_focusNode) || camera == null || _focusTag.Text.Length == 0)
        {
            _focusTag.Visible = false;
            return;
        }
        var top = _focusNode.GlobalPosition + _focusTop;
        if (camera.IsPositionBehind(top))
        {
            _focusTag.Visible = false;
            return;
        }
        var screen = GetViewport().GetVisibleRect().Size;
        var size = _focusTag.GetCombinedMinimumSize();
        var at = camera.UnprojectPosition(top) + new Vector2(Sandbox.SandboxControls.FocusWords.TagRightPx, -Sandbox.SandboxControls.FocusWords.TagUpPx - size.Y);
        at = new Vector2(Mathf.Clamp(at.X, 0, Mathf.Max(0, screen.X - size.X)), Mathf.Clamp(at.Y, 0, Mathf.Max(0, screen.Y - size.Y)));
        if (at.X + size.X > screen.X - Sandbox.SandboxControls.FocusWords.MinimapWidthPx && at.Y < Sandbox.SandboxControls.FocusWords.MinimapHeightPx)
            at.Y = Sandbox.SandboxControls.FocusWords.MinimapHeightPx;
        _focusTag.Position = at;
        _focusTag.Visible = true;
    }

    public override void _UnhandledInput(InputEvent input)
    {
        // The controls are input actions (project.godot, [input]); Hit is a fresh press of one, not a held key repeating.
        var press = PlayerControls.Normalise(input);
        var fresh = PlayerControls.Fresh(press);
        // The look drag (the cursor is free): once the pointer has moved LookDrag.StartPx with the button down, the cursor hides
        // and the motion turns the view as the captured mouse did; the release brings the cursor back where it was pressed.
        if (_drag.Pressed)
        {
            if (input is InputEventMouseMotion dragged)
            {
                var turn = _drag.Move(dragged.Relative, out var started);
                if (started) Input.MouseMode = Input.MouseModeEnum.Captured;
                if (_drag.Dragging)
                {
                    TurnView(turn);
                    GetViewport().SetInputAsHandled();
                }
                return;
            }
            if (PlayerControls.Released(press, Act.LookDrag))
            {
                EndDrag();
                GetViewport().SetInputAsHandled();
                return;
            }
        }
        // The ask button held: the mouse is the wheel's (a flick picks a wedge), and its release is the tap or the choice.
        if (_wheel.Held)
        {
            if (input is InputEventMouseMotion held)
            {
                _wheel.Move(held.Relative);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (PlayerControls.Released(press, Act.GubbleAsk))
            {
                EndAsk();
                GetViewport().SetInputAsHandled();
                return;
            }
            // Esc cancels the wheel; every other key waits while it is held (the game is paused under it).
            if (fresh && PlayerControls.Hit(press, Act.MouseRelease))
            {
                CancelAsk();
                GetViewport().SetInputAsHandled();
                return;
            }
        }
        if (GubblePaused) return;
        if (fresh)
        {
            // Esc closes the panel and calls off a drag; the cursor is never captured, so there is nothing to free.
            if (PlayerControls.Hit(press, Act.MouseRelease))
            {
                if (Customizing) ToggleCustomization();
                EndDrag();
            }
            if (PlayerControls.Hit(press, Act.HudCustomize)) ToggleCustomization();
            if (Customizing) return;
            // One key does one thing, the first below that it is (Shift+T is the season before it is a T).
            if (PlayerControls.Hit(press, Act.HudHelp))
            {
                _keyHelp.Visible = !_keyHelp.Visible;
                RefreshKeyText();
            }
            else if (PlayerControls.Hit(press, Act.ViewEye)) SetViewMode(0);
            else if (PlayerControls.Hit(press, Act.ViewShoulder)) SetViewMode(1);
            else if (PlayerControls.Hit(press, Act.ViewDiorama)) SetViewMode(2);
            else if (PlayerControls.Hit(press, Act.ViewIso)) SetViewMode(3);
            else if (PlayerControls.Hit(press, Act.Lamps) && Look != null) Look.SetLamps(!Look.LampsOn);
            else if (PlayerControls.Hit(press, Act.ViewObserve) && Look != null) Look.Observe = !Look.Observe;
            else if (PlayerControls.Hit(press, Act.SeasonStep)) StepSeason();
            else if (PlayerControls.Hit(press, Act.TimeStep)) StepTimeOfDay();
            // [ and ] turn the F4 view (Q and E moved to the Gubble's recall and stay free).
            else if (PlayerControls.Hit(press, Act.IsoTurnLeft) && ViewMode == 3) TurnIso(-1);
            else if (PlayerControls.Hit(press, Act.IsoTurnRight) && ViewMode == 3) TurnIso(1);
            // The Gubble's keys are the player's commands, carried out by the Gubble, on the same path as the companion's own.
            else if (PlayerControls.Hit(press, Act.GubbleAsk)) { BeginAsk(); GetViewport().SetInputAsHandled(); }
            else if (PlayerControls.Hit(press, Act.GubbleRecall)) Recall();
            else if (PlayerControls.Hit(press, Act.GubbleStop)) StopGubble();
            else if (SlotPressed(press) is { } slot) CastSlot(slot, AimNow());
            // Hand keys: pick up, put down and push, as commands from the player.
            else if (PlayerControls.Hit(press, Act.Hands)) Hands();
            else if (PlayerControls.Hit(press, Act.Push)) Push();
            // The look drag arms on the press; a click that never moves does nothing. F4 turns only with its keys.
            else if (PlayerControls.Hit(press, Act.LookDrag))
            {
                if (DragTurnsView && !_wheel.Held) _drag.Press(GetViewport().GetMousePosition());
            }
        }
        if (ViewMode == 2 && !Customizing)
        {
            // The diorama camera orbits with the look drag; the body does not turn with it (SetMovementFrame).
            if (fresh && PlayerControls.Hit(press, Act.ViewZoomIn))
            {
                ZoomDiorama(1);
                GetViewport().SetInputAsHandled();
            }
            else if (fresh && PlayerControls.Hit(press, Act.ViewZoomOut))
            {
                ZoomDiorama(-1);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    /// <summary>The look drag: armed by the drag button, turning the view once the pointer has moved.</summary>
    public LookDrag Drag => _drag;
    private readonly LookDrag _drag = new();
    /// <summary>Test seam: whether the drag button is held (events handed straight to the HUD never reach Input's own state).</summary>
    internal System.Func<bool>? DragHeldForTests { get; set; }

    /// <summary>Whether a drag turns this view: F1 and F2 turn the body and the eye, F3 orbits; F4 turns only by quarter turns.</summary>
    public bool DragTurnsView => ViewMode != 3;

    /// <summary>A drag's motion, in pixels, as the view's turn: the body and the eye in F1 and F2, the orbit in F3.</summary>
    public void TurnView(Vector2 relative)
    {
        if (ViewMode == 2) OrbitDiorama(relative);
        else if (ViewMode is 0 or 1) Player.TurnView(relative);
    }

    /// <summary>The drag ends (released, Esc, the panel, a lost focus): a drag that turned the view gives the cursor back where it was pressed.</summary>
    private void EndDrag()
    {
        if (!_drag.Pressed) return;
        var anchor = _drag.Anchor;
        if (!_drag.Release()) return;
        if (Input.MouseMode == Input.MouseModeEnum.Captured) Input.MouseMode = Input.MouseModeEnum.Visible;
        GetViewport().WarpMouse(anchor);
    }

    /// <summary>Which ability slot key (0-based) the event presses, if any.</summary>
    private static int? SlotPressed(InputEvent press)
    {
        for (var i = 0; i < Act.GubbleSlots.Length; i++)
            if (PlayerControls.Hit(press, Act.GubbleSlots[i])) return i;
        return null;
    }

    /// <summary>The room's command host (null in fixtures without one).</summary>
    private Kernel.CommandHost? Host => GetParent() is { } parent ? Kernel.CommandHost.Of(parent) : null;

    /// <summary>F: pick up what the player faces, or put down what it holds (on what it faces, if that has a top).</summary>
    private void Hands() => _noticeText = Host?.PlayerHands().Message ?? "The command host is not attached; the hand keys are off.";

    /// <summary>V: push what the player faces.</summary>
    private void Push() => _noticeText = Host?.PlayerPush().Message ?? "The command host is not attached; the hand keys are off.";

    private void ToggleCustomization()
    {
        CancelAsk();
        EndDrag();
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
        var usedGlow = config.GetValue("profile", "has_used_glow", false);
        if (usedGlow.VariantType == Variant.Type.Bool) HasUsedGlow = usedGlow.AsBool();
        var name = config.GetValue("profile", "companion_name", CompanionAvatar.DefaultName);
        // A profile saved while the companion was still called Wisp takes the founder's name for it, the Gubble.
        if (name.VariantType == Variant.Type.String)
            Companion.SetDisplayName(CompanionAvatar.SavedName(name.AsString()));
        Player.SetAppearance(Palette[_playerColor]);
        Companion.SetAppearance(Palette[_companionColor]);
    }

    private void SavePreferences()
    {
        var config = new ConfigFile();
        config.SetValue("profile", "version", 1);
        config.SetValue("profile", "player_color", _playerColor);
        config.SetValue("profile", "companion_color", _companionColor);
        config.SetValue("profile", "companion_name", Companion.CompanionName);
        config.SetValue("profile", "has_used_glow", HasUsedGlow);
        _noticeText = WriteProfile(config) ? "Appearance saved. Room changes are not saved by this placeholder." : "Could not save appearance; current session is still usable.";
    }

    /// <summary>
    /// One value written into the profile as it is on disk, the rest left as saved (an unsaved colour or name is not saved with
    /// it). A profile that does not load is left untouched: the value then lasts this session only.
    /// </summary>
    private bool SaveProfileValue(string key, Variant value)
    {
        var config = new ConfigFile();
        var loaded = config.Load(ProfilePath);
        if (loaded != Error.Ok && loaded != Error.FileNotFound) return false;
        if (loaded == Error.Ok && (config.GetValue("profile", "version", 0).VariantType != Variant.Type.Int || config.GetValue("profile", "version", 0).AsInt32() != 1)) return false;
        config.SetValue("profile", "version", 1);
        config.SetValue("profile", key, value);
        return WriteProfile(config);
    }

    /// <summary>The profile is written whole to a temporary file, then moved over the old one.</summary>
    private static bool WriteProfile(ConfigFile config)
    {
        var directory = ProfilePath.GetBaseDir();
        var temporary = ProfilePath + ".tmp";
        var prepared = DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(directory)) == Error.Ok && config.Save(temporary) == Error.Ok;
        return prepared && DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(temporary), ProjectSettings.GlobalizePath(ProfilePath)) == Error.Ok;
    }
}
