using System.Collections.Generic;
using Godot;

namespace EnFractal.Native;

/// <summary>
/// The Gubble's answer to an order or a cast (the magic design's "What the player sees"): at once, a thought bubble over its name
/// tag shows the order's or ability's icon; "done" (a tick and a happy wiggle) only when the host's result says so; a refusal is a
/// head-shake with the host's reason, short, beside the bubble; a slot the island has no ability for is a shrug and "not yet".
/// The dusk moment adds a shiver and a dim. The bubble is drawn like the name tag (solid, alpha-cut, billboarded, a fixed screen
/// size) and sits above it; the gestures move only the drawn body, never the avatar (the scene, not the world). The
/// CompanionStatusCue under the tag is the AI link's own state, which this does not replace. Looks are a first pass for the
/// founder's eye.
/// </summary>
public partial class GubbleCue : Node
{
    /// <summary>How long a finished answer (done, refused, a shrug) stays before the bubble clears.</summary>
    public const double ShowS = 2.5;
    public const double ShakeS = 0.6;
    public const double ShiverS = 1.2;
    public const double WiggleS = 0.45;
    public const double ShrugS = 0.6;
    /// <summary>How dark the Gubble's body goes when it dims (an overlay's alpha).</summary>
    public const float DimAlpha = 0.45f;

    public enum Gesture { None, Shake, Shiver, Wiggle, Shrug }

    public CompanionAvatar Companion { get; set; } = null!;
    /// <summary>The bubble's icon now ("" when it shows nothing).</summary>
    public string Icon { get; private set; } = "";
    /// <summary>What the bubble's state is: "", thinking, done, refused, shrug.</summary>
    public string State { get; private set; } = "";
    /// <summary>The short text beside the bubble: the host's reason after a refusal, "not yet" after a shrug, else "".</summary>
    public string Reason { get; private set; } = "";
    /// <summary>The gesture playing now, and the last one played (tests).</summary>
    public Gesture Playing { get; private set; }
    public Gesture LastGesture { get; private set; }
    public bool Dimmed { get; private set; }
    /// <summary>The bubble's and the reason's 3D labels.</summary>
    public Label3D Bubble { get; private set; } = null!;
    public Label3D ReasonLabel { get; private set; } = null!;

    private double _showFor;
    private double _gestureAge;
    private double _gestureS;
    private Node3D? _body;
    private readonly List<(Node3D Node, Transform3D Pose)> _rest = new();
    private readonly List<(GeometryInstance3D Mesh, Material? Before)> _dimmed = new();
    private static readonly StandardMaterial3D DimOverlay = new()
    {
        ResourceName = "gubble dim", AlbedoColor = new Color(0.02f, 0.03f, 0.05f, DimAlpha), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };

    public override void _Ready()
    {
        Name = "GubbleCue";
        Bubble = MakeLabel("GubbleThought", 30, new Vector2(0, 46));
        ReasonLabel = MakeLabel("GubbleReason", 20, new Vector2(0, 82));
        var anchor = Companion.GetNodeOrNull<Node3D>("CompanionLabel") ?? Companion;
        anchor.AddChild(Bubble);
        anchor.AddChild(ReasonLabel);
        _body = Companion.GetNodeOrNull<Node3D>("OriginalPrototypeBody");
        Redraw();
    }

    private static Label3D MakeLabel(string name, int size, Vector2 offset) => new()
    {
        Name = name, FontSize = size, OutlineSize = 5, PixelSize = 0.0003f, FixedSize = true, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = false, AlphaCut = Label3D.AlphaCutMode.Discard, AlphaScissorThreshold = 0.5f, Modulate = new Color("fff3d6"),
        OutlineModulate = new Color("18332d"), Offset = offset, Visible = false,
    };

    /// <summary>The order or cast was asked: the icon shows at once, while the host decides.</summary>
    public void Think(string icon)
    {
        Icon = icon;
        State = "thinking";
        Reason = "";
        _showFor = double.MaxValue;
        Redraw();
    }

    /// <summary>The host's result says it happened: a tick and a happy wiggle.</summary>
    public void Done()
    {
        State = "done";
        Reason = "";
        _showFor = ShowS;
        Play(Gesture.Wiggle, WiggleS);
        Redraw();
    }

    /// <summary>The host refused: a head-shake and its reason, short (GubbleMagic.ShortReason).</summary>
    public void Refuse(string? hostReason)
    {
        State = "refused";
        Reason = GubbleMagic.ShortReason(hostReason);
        _showFor = ShowS;
        Play(Gesture.Shake, ShakeS);
        Redraw();
    }

    /// <summary>A slot the island has no ability for: a shrug and "not yet" (no command was sent).</summary>
    public void Shrug(string icon, string words = GubbleMagic.NotYet)
    {
        Icon = icon;
        State = "shrug";
        Reason = words;
        _showFor = ShowS;
        Play(Gesture.Shrug, ShrugS);
        Redraw();
    }

    /// <summary>The dusk moment: it shivers (and the HUD dims it).</summary>
    public void Shiver() => Play(Gesture.Shiver, ShiverS);

    /// <summary>Dim or undim the Gubble's body: an overlay on its drawn meshes, which the look's focus highlight also uses in its own way.</summary>
    public void Dim(bool on)
    {
        if (on == Dimmed) return;
        Dimmed = on;
        if (on && _body != null)
        {
            foreach (var node in _body.FindChildren("*", "GeometryInstance3D", true, false))
                if (node is GeometryInstance3D mesh)
                {
                    _dimmed.Add((mesh, mesh.MaterialOverlay));
                    mesh.MaterialOverlay = DimOverlay;
                }
            return;
        }
        foreach (var (mesh, before) in _dimmed)
            if (IsInstanceValid(mesh) && mesh.MaterialOverlay == DimOverlay) mesh.MaterialOverlay = before;
        _dimmed.Clear();
    }

    /// <summary>Clear the bubble now.</summary>
    public void Clear()
    {
        Icon = "";
        State = "";
        Reason = "";
        Redraw();
    }

    private void Redraw()
    {
        if (Bubble == null) return;
        var text = State switch
        {
            "" => "",
            "done" => $"( {Icon} {GubbleMagic.DoneMark} )",
            "refused" => $"( {Icon} {GubbleMagic.RefusedMark} )",
            _ => $"( {Icon} )",
        };
        Bubble.Text = text;
        Bubble.Visible = text.Length > 0;
        ReasonLabel.Text = Reason;
        ReasonLabel.Visible = Reason.Length > 0 && text.Length > 0;
        Bubble.Modulate = State == "refused" ? new Color("f4b0a0") : State == "shrug" ? new Color("cfd8d4") : new Color("fff3d6");
    }

    private void Play(Gesture gesture, double seconds)
    {
        if (_body == null || !IsInstanceValid(_body)) return;
        if (Playing == Gesture.None)
        {
            _rest.Clear();
            foreach (var child in _body.GetChildren())
                if (child is Node3D part) _rest.Add((part, part.Transform));
        }
        Playing = gesture;
        LastGesture = gesture;
        _gestureAge = 0;
        _gestureS = seconds;
    }

    public override void _Process(double delta)
    {
        if (_showFor != double.MaxValue && State.Length > 0)
        {
            _showFor -= delta;
            if (_showFor <= 0) Clear();
        }
        if (Playing == Gesture.None) return;
        _gestureAge += delta;
        var t = (float)(_gestureAge / _gestureS);
        if (t >= 1f)
        {
            Restore();
            return;
        }
        var fade = 1f - t;
        var h = Companion.BodyHeightM;
        var pose = Playing switch
        {
            // Side to side about the body's upright: no.
            Gesture.Shake => new Transform3D(new Basis(Vector3.Up, 0.45f * fade * Mathf.Sin(t * Mathf.Tau * 3f)), Vector3.Zero),
            // A fast small tremble.
            Gesture.Shiver => new Transform3D(Basis.Identity, new Vector3(0.012f * h * Mathf.Sin(t * Mathf.Tau * 14f), 0, 0)),
            // A happy bounce.
            Gesture.Wiggle => new Transform3D(new Basis(Vector3.Back, 0.18f * fade * Mathf.Sin(t * Mathf.Tau * 2f)), Vector3.Up * (0.08f * h * Mathf.Sin(t * Mathf.Pi))),
            // Up and down once: a shrug.
            _ => new Transform3D(Basis.Identity, Vector3.Up * (0.06f * h * Mathf.Sin(t * Mathf.Pi))),
        };
        // Turned about the body's middle, so a shake turns it in place.
        var pivot = Vector3.Up * (h * 0.5f);
        var about = new Transform3D(Basis.Identity, pivot) * pose * new Transform3D(Basis.Identity, -pivot);
        foreach (var (node, pose0) in _rest)
            if (IsInstanceValid(node)) node.Transform = about * pose0;
    }

    private void Restore()
    {
        foreach (var (node, pose0) in _rest)
            if (IsInstanceValid(node)) node.Transform = pose0;
        _rest.Clear();
        Playing = Gesture.None;
    }

    public override void _ExitTree()
    {
        Restore();
        Dim(false);
    }
}
