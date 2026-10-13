using System.Linq;
using Godot;

namespace EnFractal.Native;

public partial class RoomHud
{
    private LineEdit _wishText = null!;
    private Label _wishLimit = null!;
    private Label _wishAiStatus = null!;
    private Label _wishQueueWords = null!;
    private Button _wishSubmit = null!;
    private Button _wishStop = null!;
    private CheckButton _wishUseTarget = null!;
    private CheckButton _ownWordsToggle = null!;
    /// <summary>Wish words leave the game: they go to the AI the player connected, and so to the company that runs it.</summary>
    public const string WishDisclosure = "The words of your wish are sent to the AI you connected to play the Gubble. If that AI is online, the company that runs it gets them too.";
    /// <summary>The grown-ups' test mode's setting, in the player's own words (the founder, 13 October).</summary>
    public const string OwnWordsSetting = "Grown-ups' test mode: let the AI speak in its own words";

    private void BuildWishPage(VBoxContainer page)
    {
        MagicHeading(page, "MAKE A WISH");
        page.AddChild(WrapMagicLabel("Tell the Gubble what you want to try. Wishes wait their turn, in the order you make them."));
        // The magic design's decided disclosure: always shown, whichever AI receives the wish.
        var disclosure = WrapMagicLabel(WishDisclosure); disclosure.Name = "WishDisclosure"; page.AddChild(disclosure);
        _wishAiStatus = WrapMagicLabel(""); _wishAiStatus.Name = "WishAiStatus"; page.AddChild(_wishAiStatus);
        _wishText = new LineEdit { Name = "WishText", PlaceholderText = "I wish…", MaxLength = 280, ExpandToTextLength = false };
        _wishText.TextChanged += _ => RefreshWishPage();
        _wishText.TextSubmitted += _ => SubmitWish();
        page.AddChild(_wishText);
        _wishLimit = new Label { Text = "0 / 280", HorizontalAlignment = HorizontalAlignment.Right }; page.AddChild(_wishLimit);
        _wishUseTarget = new CheckButton { Name = "WishUseTarget", Text = "About the selected thing" }; page.AddChild(_wishUseTarget);
        _wishSubmit = new Button { Name = "WishSubmitButton", Text = "Make this wish" };
        _wishSubmit.Pressed += SubmitWish; page.AddChild(_wishSubmit);
        _wishStop = new Button { Name = "WishStopButton", Text = "Stop this wish and clear the waiting wishes" };
        _wishStop.Pressed += () => { if (Host is { } host) MagicResult(host.PlayerWishStop(), "The Gubble has stopped. Waiting wishes are cancelled."); };
        page.AddChild(_wishStop);
        MagicHeading(page, "WISH JOURNAL");
        _wishQueueWords = WrapMagicLabel("Your wishes will appear here."); _wishQueueWords.Name = "WishQueueWords"; page.AddChild(_wishQueueWords);
        // A grown-up's setting, apart from the wish box: what the Gubble may say (RoomHud.Speech.cs).
        MagicHeading(page, "FOR GROWN-UPS");
        page.AddChild(WrapMagicLabel("Off: the Gubble says only its own short phrases, written for children. On, for a grown-ups' test: it may say anything the AI writes, as plain text, and the screen says so while it is on."));
        _ownWordsToggle = new CheckButton { Name = "OwnWordsToggle", Text = OwnWordsSetting, ButtonPressed = CompanionOwnWords };
        _ownWordsToggle.Toggled += SetCompanionOwnWords;
        page.AddChild(_ownWordsToggle);
    }

    private void SubmitWish()
    {
        if (Host is not { } host || string.IsNullOrWhiteSpace(_wishText.Text)) return;
        var result = host.PlayerWish(_wishText.Text, _wishUseTarget.ButtonPressed ? _magicAim.Entity : null);
        if (result["ok"]?.GetValue<bool>() == true) _wishText.Text = "";
        MagicResult(result, host.CompanionConnected ? "Your wish is waiting its turn." : "Your wish is waiting until your AI is connected.");
    }

    private void RefreshWishPage()
    {
        if (_wishText == null || _wishSubmit == null || Host is not { } host) return;
        _wishLimit.Text = $"{_wishText.Text.Length} / 280";
        _wishUseTarget.Disabled = _magicAim.Entity == null;
        if (_magicAim.Entity == null) _wishUseTarget.SetPressedNoSignal(false);
        _wishSubmit.Disabled = string.IsNullOrWhiteSpace(_wishText.Text) || host.Wishes.Items.Count(item => !item.Terminal) >= 16;
        _wishStop.Disabled = !host.Wishes.Items.Any(item => !item.Terminal);
        _ownWordsToggle.SetPressedNoSignal(CompanionOwnWords);
        var waiting = host.Wishes.Items.Count(item => item.Status == "queued");
        _wishAiStatus.Text = host.CompanionConnected ? "Your AI is connected and plays the Gubble. It takes your wishes one at a time, in order." :
            waiting > 0 ? "No AI is connected, so your wishes are waiting. They wait here until a grown-up connects your AI." :
            "No AI is connected yet. Your wishes will wait here until a grown-up connects your AI; the game never starts another program by itself.";
        if (host.WishNotice.Length > 0) _wishAiStatus.Text += "\n" + host.WishNotice;
        // Only the player's input box contains their words. Gubble feedback comes from these fixed lines,
        // never an AI's report, its prose, an untrusted note or a claimed success.
        _wishQueueWords.Text = host.Wishes.Items.Count == 0 ? "Your wishes will appear here." : string.Join("\n", host.Wishes.Items.Select((item, index) =>
            $"Wish {index + 1} · " + (item.Status switch
            {
                "queued" => "Waiting its turn", "running" => "Working on it", "granted" => "Granted",
                "partial" => "Partly granted", "unsupported" => "That magic is not available yet",
                "cancelled" => "Stopped", _ => "Could not finish this wish",
            })).TakeLast(16));
    }
}
