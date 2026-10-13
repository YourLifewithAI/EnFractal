using System;
using Godot;
using EnFractal.Native.Kernel;

namespace EnFractal.Native;

/// <summary>
/// The Gubble's words (companion.say) over its head, in the player's view: screen text placed over the name tag every
/// frame, above the thought bubble when one shows, in the Gubble cue's style (its font colour and dark outline, so the
/// words read over any landscape). The words come only from the host (<see cref="CommandHost.GubbleSaid"/>): a curated
/// phrase, or in the grown-ups' test mode the AI's own words, in quotation marks and another tint so they never pass for
/// the game's own. While that mode is on, a banner says so plainly at the top of the screen.
/// It also hands the player's setting to the host each frame (the host may attach after the HUD).
/// </summary>
public partial class GubbleSpeechCaption : Control
{
    public const int CaptionFontPx = 24;
    public const int BannerFontPx = 18;
    /// <summary>The widest a caption gets before it wraps, in pixels.</summary>
    public const float MaxWidthPx = 420f;
    /// <summary>How long words stay: a base, plus a little per character, up to a cap.</summary>
    public const double ShowBaseS = 3.5, ShowPerCharS = 0.06, ShowMaxS = 9.0;
    public const string BannerText = "Grown-ups' test mode is on: the AI speaks in its own words.";

    public CompanionAvatar Companion { get; set; } = null!;
    public GubbleCue? Cue { get; set; }
    public Func<CommandHost?> HostOf { get; set; } = () => null;
    /// <summary>The player's setting (the HUD's profile value).</summary>
    public Func<bool> OwnWordsSetting { get; set; } = () => false;
    public Label Caption { get; private set; } = null!;
    public Label Banner { get; private set; } = null!;
    /// <summary>The words showing now ("" when none), and whether they are the AI's own.</summary>
    public string Words { get; private set; } = "";
    public bool OwnWords { get; private set; }

    private int _serial;
    private double _showFor;

    public override void _Ready()
    {
        Name = "GubbleSpeech";
        MouseFilter = MouseFilterEnum.Ignore;
        Caption = new Label
        {
            Name = "GubbleWords", Visible = false, MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        Caption.AddThemeFontSizeOverride("font_size", CaptionFontPx);
        Caption.AddThemeColorOverride("font_outline_color", new Color("10241f"));
        Caption.AddThemeConstantOverride("outline_size", GubbleCue.OutlinePx);
        AddChild(Caption);
        Banner = new Label { Name = "OwnWordsBanner", Text = BannerText, Visible = false, MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
        Banner.AddThemeFontSizeOverride("font_size", BannerFontPx);
        Banner.AddThemeColorOverride("font_color", new Color("ffe08a"));
        Banner.AddThemeColorOverride("font_outline_color", new Color("10241f"));
        Banner.AddThemeConstantOverride("outline_size", 6);
        AddChild(Banner);
    }

    public override void _Process(double delta)
    {
        var host = HostOf();
        var own = OwnWordsSetting();
        if (host != null && host.CompanionOwnWords != own) host.PlayerSetCompanionOwnWords(own);
        Banner.Visible = own;
        if (Banner.Visible)
        {
            var size = Banner.GetCombinedMinimumSize();
            Banner.Size = size;
            Banner.Position = new Vector2((GetViewport().GetVisibleRect().Size.X - size.X) * 0.5f, 6);
        }
        if (host?.GubbleSaid is { } said && said.Serial != _serial)
        {
            _serial = said.Serial;
            Words = said.Text;
            OwnWords = said.OwnWords;
            Caption.Text = said.OwnWords ? $"“{said.Text}”" : said.Text;
            Caption.AddThemeColorOverride("font_color", said.OwnWords ? new Color("d6ecff") : new Color("fff3d6"));
            _showFor = Math.Min(ShowMaxS, ShowBaseS + ShowPerCharS * said.Text.Length);
        }
        if (Words.Length > 0 && (_showFor -= delta) <= 0) { Words = ""; Caption.Text = ""; }
        Place();
    }

    /// <summary>Over the name tag, above the thought bubble and its reason when they show, kept whole on screen; hidden off it.</summary>
    private void Place()
    {
        var top = Words.Length > 0 ? Cue?.TagTopOnScreen() : null;
        Caption.Visible = top != null;
        if (top is not { } anchor) return;
        var above = anchor.Y;
        if (Cue is { } cue)
        {
            if (cue.Bubble.Visible) above = Math.Min(above, cue.Bubble.Position.Y - GubbleCue.GapPx);
            if (cue.ReasonLabel.Visible) above = Math.Min(above, cue.ReasonLabel.Position.Y - GubbleCue.GapPx);
        }
        Caption.CustomMinimumSize = Vector2.Zero;
        var natural = Caption.GetCombinedMinimumSize();
        var width = Math.Min(MaxWidthPx, Caption.GetThemeFont("font").GetStringSize(Caption.Text, HorizontalAlignment.Left, -1, CaptionFontPx).X + 2 * GubbleCue.OutlinePx);
        Caption.Size = new Vector2(Math.Max(width, natural.X), 0);
        var size = new Vector2(Caption.Size.X, Caption.GetCombinedMinimumSize().Y);
        Caption.Size = size;
        var screen = GetViewport().GetVisibleRect().Size;
        Caption.Position = new Vector2(Mathf.Clamp(anchor.X - size.X * 0.5f, 0, Mathf.Max(0, screen.X - size.X)), Mathf.Clamp(above - size.Y, 0, Mathf.Max(0, screen.Y - size.Y)));
    }
}
