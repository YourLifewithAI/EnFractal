using Godot;
using EnFractal.Native.Kernel;
using EnFractal.Native.Look;
using EnFractal.Native.Ui;

namespace EnFractal.Native;

/// <summary>
/// Through the Gubble's eyes (the founder, 13 October: "there should be a small icon the player can select that allows them to
/// see through the eyes of the Gubble"). The eye icon above the journal's book, or its key (I, from the input map), fills the
/// screen with the live view from the Gubble's eye: the AI's eye rig (Look/EyeView.cs), drawing every frame while the view is
/// open and nothing otherwise, with the same cull mask, so the Gubble never sees its own body. A drag looks around from the
/// Gubble's head within the AI's bounds (CommandHost.LookMaxYawDeg and LookMaxPitchDeg) without turning the Gubble. The icon,
/// its key or Esc return. While the view is open the player's body waits and every other key waits too, as with the journal
/// (ControlContext.Looking). It is a view, not a command: it sends nothing to the host and changes no world state. When the
/// player's AI takes a picture through the Gubble's eyes (view.look), the icon blinks; nothing shows on the Gubble itself.
/// </summary>
public partial class RoomHud
{
    /// <summary>The icon's gap above the journal's book in its dock.</summary>
    public const float EyesIconGapPx = 8;
    private GubbleEyesIcon _eyesIcon = null!;
    private Control _gubbleView = null!;
    private TextureRect _gubbleViewPicture = null!;
    private Label _gubbleViewTitle = null!;
    private Label _gubbleViewHint = null!;
    private EyeView _gubbleEye = null!;
    private CommandHost? _eyesListening;
    private bool _gubbleViewOpen;
    private bool _main3DOffForView;
    /// <summary>Whether the window's own 3D waits now because the Gubble's view covers the screen (tests).</summary>
    public bool Main3DOffForView => _main3DOffForView;

    /// <summary>Whether the player is looking through the Gubble's eyes now.</summary>
    public bool GubbleViewOpen => _gubbleViewOpen;
    public GubbleEyesIcon EyesIcon => _eyesIcon;
    /// <summary>The live eye (its viewport and camera), for tests and captures.</summary>
    public EyeView GubbleEye => _gubbleEye;
    /// <summary>The full-screen picture of the view (the eye's live texture).</summary>
    public TextureRect GubbleViewPicture => _gubbleViewPicture;
    /// <summary>The view's plain label, "Through the Gubble's eyes".</summary>
    public string GubbleViewTitle => _gubbleViewTitle.Text;
    /// <summary>How far the player has turned the Gubble's eye from its facing (degrees: right is positive) and tilted it (up is positive).</summary>
    public float GubbleViewYawDeg { get; private set; }
    public float GubbleViewPitchDeg { get; private set; }

    /// <summary>
    /// The one place that decides whether the player may look through the Gubble's eyes. True from the start for now (the founder,
    /// 13 October); a later progression can gate it here (docs/ideas/GUBBLE-EYES-AND-STEALTH.md: the player might earn it).
    /// </summary>
    public bool MaySeeThroughGubbleEyes => true;

    /// <summary>The icon over the journal's dock, and the full-screen view under it (built after the journal, so it covers the book's dock).</summary>
    private void BuildGubbleView(Theme compactTheme)
    {
        _gubbleEye = new EyeView { Name = "GubbleEyesView" };
        AddChild(_gubbleEye);
        // Mouse filter Pass: the picture keeps clicks from the controls under it (the journal's dock, the top panel) and passes
        // them on to the HUD's own input, where a drag looks around and every other press waits.
        _gubbleView = new Control { Name = "GubbleView", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_gubbleView);
        _gubbleView.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _gubbleViewPicture = new TextureRect
        {
            Name = "GubbleViewPicture", MouseFilter = Control.MouseFilterEnum.Pass,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
        };
        _gubbleView.AddChild(_gubbleViewPicture);
        _gubbleViewPicture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var label = new PanelContainer { Name = "GubbleViewLabel", Theme = compactTheme, MouseFilter = Control.MouseFilterEnum.Ignore };
        _gubbleView.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        label.GrowHorizontal = Control.GrowDirection.Both;
        label.OffsetTop = 18;
        var lines = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddChild(lines);
        _gubbleViewTitle = new Label { Name = "GubbleViewTitle", HorizontalAlignment = HorizontalAlignment.Center };
        lines.AddChild(_gubbleViewTitle);
        _keyTexts.Add((words => _gubbleViewTitle.Text = words, () => $"Through {Companion.CompanionName}'s eyes"));
        _gubbleViewHint = new Label { Name = "GubbleViewHint", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 1, 1, 0.72f) };
        _gubbleViewHint.AddThemeFontSizeOverride("font_size", 12);
        lines.AddChild(_gubbleViewHint);
        _keyTexts.Add((words => _gubbleViewHint.Text = words, () => $"{PlayerControls.DragLabel()} to look around · {K(Act.GubbleEyes)} or {K(Act.MouseRelease)} to return"));

        // The icon stands just above the journal's book, in the same dock at the lower right, clear of the help footer.
        _eyesIcon = new GubbleEyesIcon
        {
            KeyLabel = () => K(Act.GubbleEyes), GubbleName = () => Companion.CompanionName,
            IrisColor = () => Companion.AuraColor ?? Companion.AppearanceColor,
        };
        AddChild(_eyesIcon);
        _eyesIcon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
        var dock = _book.Dock;
        _eyesIcon.OffsetRight = dock.OffsetLeft + (dock.OffsetRight - dock.OffsetLeft + GubbleEyesIcon.IconSize.X) * 0.5f;
        _eyesIcon.OffsetLeft = _eyesIcon.OffsetRight - GubbleEyesIcon.IconSize.X;
        _eyesIcon.OffsetBottom = dock.OffsetTop - EyesIconGapPx;
        _eyesIcon.OffsetTop = _eyesIcon.OffsetBottom - GubbleEyesIcon.IconSize.Y;
        _eyesIcon.Pressed += ToggleGubbleView;
        // The window's own 3D comes back if the HUD goes while the view is open (a room unloaded).
        TreeExiting += () => SetMain3D(on: true);
    }

    /// <summary>The icon or its key: look through the Gubble's eyes, or come back.</summary>
    public void ToggleGubbleView()
    {
        if (_gubbleViewOpen) CloseGubbleView();
        else OpenGubbleView();
    }

    /// <summary>Fill the screen with the Gubble's live view, closing the panels and calling off the wheel, a drag or a spot being chosen.</summary>
    public void OpenGubbleView()
    {
        if (_gubbleViewOpen || !MaySeeThroughGubbleEyes || !GubbleThere()) return;
        _gubbleViewOpen = true;
        CancelAsk();
        EndDrag();
        if (_pickingMagicSpot) _noticeText = "Spot selection cancelled.";
        _pickingMagicSpot = false;
        _movingGlow = null;
        if (Customizing) ToggleCustomization();
        if (MagicPanelOpen) SetMagicPanelOpen(false);
        CloseJournalNow();
        GubbleViewYawDeg = GubbleViewPitchDeg = 0;
        EyeView.MarkBody(Companion, EyeView.GubbleBodyLayer);
        _gubbleEye.StartLive(GubbleViewSize(), EyeView.GubbleEyeCullMask);
        PlaceGubbleEye();
        _gubbleViewPicture.Texture = _gubbleEye.EyeViewport?.GetTexture();
        _gubbleView.Visible = true;
        _eyesIcon.Looking = true;
        // The screen shows only the Gubble's view: the window's own 3D waits, so the world is drawn once a frame, not twice.
        SetMain3D(on: false);
        Player.SetInputEnabled(false);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        RefreshKeyText();
    }

    /// <summary>Back to the player's own eyes: the eye goes idle and the body moves again.</summary>
    public void CloseGubbleView()
    {
        if (!_gubbleViewOpen) return;
        EndDrag();
        _gubbleViewOpen = false;
        _gubbleEye.StopLive();
        _gubbleView.Visible = false;
        _eyesIcon.Looking = false;
        SetMain3D(on: true);
        Player.SetInputEnabled(!ReadingPanelOpen);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>The look drag while the view is open: the Gubble's eye turns and tilts within the AI's bounds; its body does not.</summary>
    private void TurnGubbleEye(Vector2 relative)
    {
        if (!relative.IsFinite()) return;
        GubbleViewYawDeg = Mathf.Clamp(GubbleViewYawDeg + Mathf.RadToDeg(relative.X * MouseRadiansPerPixel), -(float)CommandHost.LookMaxYawDeg, (float)CommandHost.LookMaxYawDeg);
        GubbleViewPitchDeg = Mathf.Clamp(GubbleViewPitchDeg - Mathf.RadToDeg(relative.Y * MouseRadiansPerPixel), -(float)CommandHost.LookMaxPitchDeg, (float)CommandHost.LookMaxPitchDeg);
        PlaceGubbleEye();
    }

    /// <summary>
    /// The view's keys, before any other (RoomHud._UnhandledInput): while the view is open, its key and Esc return, the drag
    /// button arms the look drag, and every other key waits. True when the view is open (the press was the view's).
    /// </summary>
    private bool HandleGubbleViewKeys(InputEvent press)
    {
        if (!_gubbleViewOpen) return false;
        if (PlayerControls.Hit(press, Act.GubbleEyes) || PlayerControls.Hit(press, Act.MouseRelease)) CloseGubbleView();
        else if (PlayerControls.Hit(press, Act.LookDrag)) _drag.Press(GetViewport().GetMousePosition());
        return true;
    }

    /// <summary>Each frame: the icon follows the journal (it rests while the book is out) and the open view follows the Gubble.</summary>
    private void TickGubbleView()
    {
        if (_eyesListening == null && Host is { } host)
        {
            _eyesListening = host;
            host.PictureTaken += GubbleLooked;
        }
        _eyesIcon.Visible = MaySeeThroughGubbleEyes && (_gubbleViewOpen || !JournalOpen);
        if (!_gubbleViewOpen) return;
        if (!MaySeeThroughGubbleEyes || !GubbleThere())
        {
            CloseGubbleView();
            return;
        }
        // Parts the Gubble's body gains later (its name tag, a cue) join its layer, so its eye never sees them.
        EyeView.MarkBody(Companion, EyeView.GubbleBodyLayer);
        _gubbleEye.ResizeLive(GubbleViewSize());
        PlaceGubbleEye();
    }

    /// <summary>The player's AI took a picture through the Gubble's eyes: the icon blinks.</summary>
    private void GubbleLooked(string principal)
    {
        if (principal == CommandHost.CompanionPrincipal && IsInstanceValid(_eyesIcon)) _eyesIcon.Blink();
    }

    /// <summary>
    /// The Gubble's eye now: at its eye, where its body is drawn this frame, facing the way the body faces, turned and tilted by the
    /// player's drag (the same arithmetic as the AI's look, EyeView.LookBasis).
    /// </summary>
    public Transform3D GubbleEyePose()
    {
        var body = Companion.GetGlobalTransformInterpolated();
        var eye = body * Companion.EyeCamera.Position;
        return new Transform3D(EyeView.LookBasis(-body.Basis.Z, GubbleViewYawDeg, GubbleViewPitchDeg), eye);
    }

    private void PlaceGubbleEye()
    {
        var lens = Companion.EyeCamera;
        _gubbleEye.PlaceLive(GubbleEyePose(), lens.Fov, lens.Near, lens.Far);
    }

    private bool GubbleThere() => Companion != null && IsInstanceValid(Companion) && Companion.IsInsideTree();

    /// <summary>The view's size in pixels: the HUD's whole screen at the window's own resolution, keeping its shape.</summary>
    private Vector2I GubbleViewSize()
    {
        var screen = GetViewport().GetVisibleRect().Size;
        var window = (Vector2)GetWindow().Size;
        var scale = screen.X > 0 && screen.Y > 0 ? Mathf.Min(window.X / screen.X, window.Y / screen.Y) : 1;
        return new Vector2I(Mathf.RoundToInt(screen.X * scale), Mathf.RoundToInt(screen.Y * scale));
    }

    /// <summary>The window's own 3D drawing: off while the Gubble's view covers the screen, back after (only if the view turned it off).</summary>
    private void SetMain3D(bool on)
    {
        if (!IsInsideTree()) return;
        var window = GetViewport();
        if (!on && !window.Disable3D)
        {
            window.Disable3D = true;
            _main3DOffForView = true;
        }
        else if (on && _main3DOffForView)
        {
            window.Disable3D = false;
            _main3DOffForView = false;
        }
    }
}
