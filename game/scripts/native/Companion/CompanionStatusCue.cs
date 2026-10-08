using Godot;
using System.Collections.Generic;

namespace EnFractal.Native.Companion;

/// <summary>
/// The companion's state, shown on its avatar while the player's AI is linked (A2; LIVE-VOICE.md's accessibility
/// list): listening, planning, acting, or waiting for the player's yes. One short line under the name tag, in the tag's
/// style (solid, alpha-cut, billboarded, a fixed screen size), each state in its own colour; nothing when no AI is
/// linked. The words and colours are a first pass for the founder's eye, not settled art.
/// </summary>
public partial class CompanionStatusCue : Label3D
{
    public static readonly IReadOnlyDictionary<string, (string Text, Color Colour)> Looks = new Dictionary<string, (string, Color)>
    {
        ["listening"] = ("listening", new Color("bfe3dc")),
        ["planning"] = ("planning…", new Color("f2d58a")),
        ["acting"] = ("acting", new Color("f4a46a")),
        ["waiting"] = ("waiting for your yes", new Color("f0a8b8")),
    };

    public string State { get; private set; } = "offline";

    public override void _Ready()
    {
        Name = "CompanionState";
        FontSize = 22;
        OutlineSize = 4;
        PixelSize = 0.0003f;
        FixedSize = true;
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        NoDepthTest = false;
        AlphaCut = AlphaCutMode.Discard;
        AlphaScissorThreshold = 0.5f;
        OutlineModulate = new Color("18332d");
        // One line below the name tag (offsets are in label pixels, so the gap holds at every distance).
        Offset = new Vector2(0, -34);
        Display(State);
    }

    public void Display(string state)
    {
        State = state;
        if (!Looks.TryGetValue(state, out var look))
        {
            Visible = false;
            return;
        }
        Text = look.Text;
        Modulate = look.Colour;
        Visible = true;
    }
}
