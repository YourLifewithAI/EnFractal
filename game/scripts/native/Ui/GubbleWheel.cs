using System.Collections.Generic;
using Godot;

namespace EnFractal.Native;

/// <summary>
/// The Gubble wheel (the magic design's "The wheel"; research 4, marking menus): hold the ask button and, after HoldOpenS, eight
/// fixed wedges open around the pointer and the game pauses. Moving the mouse picks a wedge; letting go chooses it. A flick and
/// release chooses one before the wheel is drawn (an expert never waits for it), and letting go in the centre cancels. A release
/// before HoldOpenS with no flick is a tap: the smart ask. The wheel only says what was chosen; RoomHud carries it out.
/// </summary>
public partial class GubbleWheel : Control
{
    /// <summary>How long the button is held before the wheel opens: a shorter press is a tap.</summary>
    public const double HoldOpenS = 0.25;
    /// <summary>A pointer move this far before the wheel is drawn is a flick: its direction chooses the wedge on release.</summary>
    public const float FlickPx = 24f;
    /// <summary>Inside this radius of the open wheel's centre, letting go cancels.</summary>
    public const float CentrePx = 30f;
    /// <summary>The wheel's radius on screen, to the middle of its wedges.</summary>
    public const float RadiusPx = 120f;

    public enum Outcome { None, Tap, Wedge, Cancel }

    /// <summary>Whether the ask button is held (the wheel may not be drawn yet).</summary>
    public bool Held { get; private set; }
    /// <summary>Whether the wheel is drawn (and the game paused).</summary>
    public bool Open { get; private set; }
    /// <summary>How far the pointer has moved since the press, in screen pixels.</summary>
    public Vector2 Offset { get; private set; }
    /// <summary>The wedge the pointer is over now, or null for the centre.</summary>
    public int? Hovered => GubbleMagic.WedgeAt(Offset, Open ? CentrePx : FlickPx);

    private double _held;
    private bool _pausedTree;
    private Vector2 _centre;
    private readonly List<Label> _wedgeLabels = new();
    private Label _centreLabel = null!;

    /// <summary>The words on each wedge now, clockwise from the top (tests).</summary>
    public IReadOnlyList<Label> WedgeLabels => _wedgeLabels;

    public override void _Ready()
    {
        Name = "GubbleWheel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        // The wheel runs while the game it paused waits.
        ProcessMode = ProcessModeEnum.Always;
        for (var i = 0; i < GubbleMagic.Wedges.Length; i++)
        {
            var label = new Label { Name = "Wedge" + i, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            label.AddThemeFontSizeOverride("font_size", 18);
            label.AddThemeColorOverride("font_outline_color", new Color("18332d"));
            label.AddThemeConstantOverride("outline_size", 4);
            AddChild(label);
            _wedgeLabels.Add(label);
        }
        _centreLabel = new Label { Name = "WheelCentre", Text = "cancel", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _centreLabel.AddThemeFontSizeOverride("font_size", 13);
        AddChild(_centreLabel);
    }

    /// <summary>
    /// The wedges' words: an ability slot shows its icon and the island's display_name, or a dim "?" when the island has none in
    /// that category; an order shows its icon, name and key where it has one.
    /// </summary>
    public void SetWedges(System.Func<int, string?> abilityName, System.Func<GubbleMagic.Wedge, string> orderWords)
    {
        for (var i = 0; i < _wedgeLabels.Count; i++)
        {
            var wedge = GubbleMagic.Wedges[i];
            var label = _wedgeLabels[i];
            var name = wedge.Kind == GubbleMagic.WedgeKind.Ability ? abilityName(wedge.Slot) : orderWords(wedge);
            var known = name != null;
            var text = known ? (wedge.Kind == GubbleMagic.WedgeKind.Ability ? $"{wedge.Icon}\n{wedge.Slot + 1} {name}" : name!) : GubbleMagic.UnknownIcon;
            if (label.Text != text) label.Text = text;
            label.Modulate = new Color(1, 1, 1, known ? 1f : 0.35f);
        }
    }

    /// <summary>The ask button went down at this screen point: the hold starts.</summary>
    public void Begin(Vector2 centre)
    {
        Held = true;
        Open = false;
        _held = 0;
        Offset = Vector2.Zero;
        _centre = centre;
    }

    /// <summary>The pointer moved while the button is held (relative pixels; works while the mouse is captured).</summary>
    public void Move(Vector2 relative)
    {
        if (!Held || !relative.IsFinite()) return;
        Offset = (Offset + relative).LimitLength(RadiusPx * 1.5f);
        QueueRedraw();
    }

    /// <summary>Time passes while the button is held: after HoldOpenS the wheel opens and the game pauses.</summary>
    public void Tick(double delta)
    {
        if (!Held || Open) return;
        _held += delta;
        if (_held < HoldOpenS) return;
        Open = true;
        Visible = true;
        if (IsInsideTree() && !GetTree().Paused)
        {
            GetTree().Paused = true;
            _pausedTree = true;
        }
        Layout();
        QueueRedraw();
    }

    /// <summary>The button let go: what was chosen (a wedge's index in wedge), a tap, or a cancel. The wheel closes and the game resumes.</summary>
    public Outcome End(out int wedge)
    {
        wedge = -1;
        if (!Held) return Outcome.None;
        var hovered = Hovered;
        var outcome = hovered != null ? Outcome.Wedge : Open ? Outcome.Cancel : Outcome.Tap;
        if (hovered is { } index) wedge = index;
        Close();
        return outcome;
    }

    /// <summary>Close without choosing (the panel opened, the HUD left the tree).</summary>
    public void Close()
    {
        Held = false;
        Open = false;
        Visible = false;
        Offset = Vector2.Zero;
        if (_pausedTree && IsInsideTree()) GetTree().Paused = false;
        _pausedTree = false;
    }

    public override void _ExitTree()
    {
        if (_pausedTree && IsInsideTree()) GetTree().Paused = false;
        _pausedTree = false;
    }

    private void Layout()
    {
        var screen = GetViewportRect().Size;
        // Kept whole on screen, wherever the button was pressed.
        var margin = RadiusPx + 48f;
        _centre = new Vector2(Mathf.Clamp(_centre.X, margin, Mathf.Max(margin, screen.X - margin)), Mathf.Clamp(_centre.Y, margin, Mathf.Max(margin, screen.Y - margin)));
        for (var i = 0; i < _wedgeLabels.Count; i++)
        {
            var angle = i * Mathf.Pi / 4;
            var at = _centre + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * RadiusPx;
            var size = _wedgeLabels[i].GetCombinedMinimumSize();
            _wedgeLabels[i].Position = at - size / 2;
        }
        _centreLabel.Position = _centre - _centreLabel.GetCombinedMinimumSize() / 2;
    }

    public override void _Draw()
    {
        if (!Open) return;
        DrawCircle(_centre, RadiusPx + 44f, new Color(0.075f, 0.13f, 0.13f, 0.82f));
        DrawCircle(_centre, CentrePx, new Color(0.16f, 0.24f, 0.23f, 0.95f));
        if (Hovered is { } hovered)
        {
            var angle = hovered * Mathf.Pi / 4;
            DrawCircle(_centre + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * RadiusPx, 40f, new Color(0.95f, 0.84f, 0.54f, 0.35f));
        }
        DrawLine(_centre, _centre + Offset, new Color(0.95f, 0.84f, 0.54f, 0.8f), 2f);
    }
}
