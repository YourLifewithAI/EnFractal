using System;
using Godot;

namespace EnFractal.Native.Ui;

/// <summary>
/// The Gubble's pips: connection energy drawn as dots that hover round the Gubble and trail behind it when it moves
/// (docs/game/MECHANICS.md sections 1 and 5). A pip you have is a filled dot in the Gubble's aura colour; one spent is a ring, so
/// "four of eight" reads at a glance. In Creative the pool is a shadow: the same dots, drawn as soft translucent shadows, for it
/// drains and refills but never refuses. The dots are the HUD's own 2D drawing on the Gubble's place on screen, never a thing in
/// the world, so they are not in the AI's pictures and cost the look nothing.
/// The meter draws nothing while the Gubble is out of view (behind the camera or off the screen): the pips are then the row under
/// the stamina bar (PipsMarker, decision 38). It redraws every frame while it shows dots (they bob and ease after the Gubble), a
/// handful of circles.
/// </summary>
public partial class PipsTrail : Control
{
    /// <summary>Pips on one arc round the Gubble; more go to the next arc out.</summary>
    public const int PerArc = 12;
    /// <summary>How long a refused cast's flash lasts: the pool blinks to say it is empty.</summary>
    public const double FlashS = 0.6;

    public int Pips { get; private set; }
    public int MaxPips { get; private set; }
    public bool Shadow { get; private set; }
    public Color Aura { get; private set; } = new("a18cc3");
    public bool Following { get; private set; }
    /// <summary>Where the dots are on screen now, for the suite (the first of them).</summary>
    public Vector2 FirstDotAt => _dots.Length > 0 ? _dots[0] : Vector2.Zero;
    public double FlashLeftS { get; private set; }
    /// <summary>The pool in numbers ("Pips 6/8"), drawn over the dots only while the player's numbers option is on; empty otherwise.</summary>
    public string Numbers { get; private set; } = "";

    private Vector2[] _dots = new Vector2[0];
    private Vector2 _anchor;
    private float _bodyPx = 40f;
    private bool _placed;
    private double _clock;

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    /// <summary>The pool as it is now: pips left, the most, whether it is the shadow pool, the Gubble's colour. No dots when there is no pool.</summary>
    public void SetPool(int pips, int maxPips, bool shadow, Color aura)
    {
        pips = Math.Clamp(pips, 0, Math.Max(0, maxPips));
        if (pips == Pips && maxPips == MaxPips && shadow == Shadow && aura == Aura) return;
        if (maxPips != MaxPips)
        {
            var grown = new Vector2[Math.Max(0, maxPips)];
            Array.Copy(_dots, grown, Math.Min(_dots.Length, grown.Length));
            _dots = grown;
            _placed = false;
        }
        (Pips, MaxPips, Shadow, Aura) = (pips, maxPips, shadow, aura);
        if (Following) QueueRedraw();
    }

    /// <summary>The numbers option: the words over the dots, or nothing.</summary>
    public void SetNumbers(string words)
    {
        if (words == Numbers) return;
        Numbers = words;
        if (Following) QueueRedraw();
    }

    /// <summary>The pool blinks: a cast was refused for want of pips ("The Gubble needs a moment").</summary>
    public void Flash() => FlashLeftS = FlashS;

    /// <summary>
    /// Each frame: where the Gubble is on screen (null when it is out of view) and how tall it looks in pixels. The dots ease to
    /// their places round it, the outer ones slower, so they trail behind a Gubble that moves.
    /// </summary>
    public void Follow(Vector2? anchor, float bodyPx, double delta)
    {
        var shown = anchor != null && MaxPips > 0;
        if (!shown)
        {
            if (Following) { Following = false; QueueRedraw(); }
            _placed = false;
            return;
        }
        _anchor = anchor!.Value;
        _bodyPx = Mathf.Clamp(bodyPx, 16f, 140f);
        _clock += delta;
        FlashLeftS = Math.Max(0, FlashLeftS - delta);
        Following = true;
        var pull = 1f - Mathf.Exp(-(float)delta * 9f);
        for (var i = 0; i < _dots.Length; i++)
        {
            var wanted = Slot(i);
            _dots[i] = _placed ? _dots[i].Lerp(wanted, pull * (1f - 0.035f * Math.Min(i, 20))) : wanted;
        }
        _placed = true;
        QueueRedraw();
    }

    /// <summary>Where pip i rests: on an arc over the Gubble's head, a little bobbing; more than an arc's worth go to arcs further out.</summary>
    public Vector2 Slot(int i)
    {
        var arc = i / PerArc;
        var onArc = Math.Min(PerArc, Math.Max(1, MaxPips - arc * PerArc));
        var index = i - arc * PerArc;
        var spread = Mathf.DegToRad(Mathf.Min(150f, 22f * Math.Max(1, onArc - 1) + 30f));
        var angle = onArc == 1 ? 0f : -spread / 2 + spread * index / (onArc - 1);
        var radius = _bodyPx * 0.75f + 12f + arc * (PipRadius() * 3.2f);
        var bob = Mathf.Sin((float)(_clock * 2.2) + i * 0.7f) * 1.6f;
        // Over the head: up is -Y on screen; the arc opens upward.
        return _anchor + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * radius + new Vector2(0, bob);
    }

    /// <summary>A pip's radius in pixels: scales a little with how big the Gubble looks.</summary>
    public float PipRadius() => Mathf.Clamp(_bodyPx * 0.11f, 3f, 7f);

    public override void _Draw()
    {
        var began = MeterCost.Begin();
        DrawDots();
        MeterCost.End(began, MeterCost.Piece.Pips);
    }

    private void DrawDots()
    {
        if (!Following || MaxPips == 0) return;
        var radius = PipRadius();
        // Each pip is one picture (MeterTextures), a little larger than the dot so its dark rim shows.
        var size = Vector2.One * (radius * 2f + 4f);
        if (Numbers.Length > 0)
        {
            var font = GetThemeDefaultFont();
            var width = font.GetStringSize(Numbers, HorizontalAlignment.Left, -1, 15).X;
            var at = _anchor + new Vector2(-width / 2, -(_bodyPx * 0.75f + 12f + (MaxPips - 1) / PerArc * radius * 3.2f + radius + 8f));
            DrawStringOutline(font, at, Numbers, HorizontalAlignment.Left, -1, 15, 4, new Color("18332d"));
            DrawString(font, at, Numbers, HorizontalAlignment.Left, -1, 15, new Color("fff3d6"));
        }
        var flash = FlashLeftS > 0 && (int)(FlashLeftS * 10) % 2 == 0;
        var tint = flash ? new Color("ffe9a8") : Aura.Lightened(0.15f);
        var ringTint = flash ? new Color("ff9d8a") : Colors.White;
        for (var i = 0; i < _dots.Length; i++)
        {
            var rect = new Rect2(_dots[i] - size / 2, size);
            var has = i < Pips;
            // The shadow pool: soft dark discs, a pip you have a little stronger; never the aura's colour, for nothing is spent for real.
            if (Shadow) DrawTextureRect(MeterTextures.PipShadow, rect, false, has ? Colors.White : new Color(1, 1, 1, 0.5f));
            else if (has) DrawTextureRect(MeterTextures.PipFull, rect, false, tint);
            else DrawTextureRect(MeterTextures.PipSpent, rect, false, ringTint);
        }
    }
}
