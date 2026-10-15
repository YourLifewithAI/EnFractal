using Godot;
using EnFractal.Native.Ui;

namespace EnFractal.Native;

/// <summary>
/// Spending Fractals in play (decision 41 of docs/game/MECHANICS.md; the host's fractal.spend, Ui/FractalSpend.cs): no key of its
/// own. The journal's Character page offers the two buttons at the camp, and a button sends the player's own command through
/// the host's one command path like every other player action here (Gather, Hands); the host decides, and its words (the new heart,
/// or its child-friendly refusal) are the HUD's notice and the page's foot. A short hint points to the journal when the player
/// stands at the camp with enough in hand.
/// </summary>
public partial class RoomHud
{
    /// <summary>The hint is looked for this often (a few times a second is plenty for a player who stands still at the camp).</summary>
    public const double SpendHintEveryS = 0.25;
    private double _spendHintAge;
    private bool _spendHintShown;
    private object? _campRoom;
    private Vector3 _campAt;

    /// <summary>The words of the last spend hint (empty until one was given), and how many have been given, for the suite.</summary>
    public string SpendHint { get; private set; } = "";
    public int SpendHintCount { get; private set; }

    /// <summary>
    /// A button on a journal page asked for something (JournalPanel.ButtonPressed): the Fractals' two buttons spend. The words it
    /// answers with go to the page's foot.
    /// </summary>
    private string JournalButton(string action) => action switch
    {
        FractalSpend.HeartTarget => SpendFractals(FractalSpend.HeartGrow),
        FractalSpend.BandTarget => SpendFractals(FractalSpend.BandGrow),
        _ => "",
    };

    /// <summary>
    /// Spend five Fractals (the pack's price) for "heart" or "band": the player's fractal.spend. The host decides (at the camp, with
    /// enough in hand, below the cap); what it answers is the notice. Returns the words.
    /// </summary>
    public string SpendFractals(string grow)
    {
        var words = Host is { } host ? FractalSpend.Spend(host, grow) : "The command host is not attached; spending is off.";
        _noticeText = words;
        return words;
    }

    /// <summary>
    /// Whether the player stands at the camp, as the host decides it (the room's player spawn, within CommandHost.CampRadiusM), asked
    /// without building anything: the host's own question builds a small object every time (about 128 bytes), and this one is asked
    /// a few times a second for as long as the player holds enough Fractals to spend. The suite holds the two to the same answer.
    /// </summary>
    public bool AtCamp(Kernel.CommandHost host)
    {
        if (host.Player is not { } body || !IsInstanceValid(body) || host.Room == null) return false;
        if (!ReferenceEquals(_campRoom, host.Room))
        {
            _campRoom = host.Room;
            _campAt = host.Room.SpawnFor("player").PositionM;
        }
        return body.GlobalPosition.DistanceTo(_campAt) <= Kernel.CommandHost.CampRadiusM;
    }

    /// <summary>
    /// Each frame (from the meters' update): a few times a second, the hint when the player has just got to the camp with enough
    /// Fractals in hand. Once for each arrival: it comes again after the player has been away or has spent the Fractals down.
    /// While the book is open the player is already where the buttons are, so no hint is given.
    /// </summary>
    private void TickSpendHint(Kernel.CommandHost host, double delta)
    {
        _spendHintAge += delta;
        if (_spendHintAge < SpendHintEveryS) return;
        _spendHintAge = 0;
        if (!FractalSpend.CanAfford(host) || !AtCamp(host))
        {
            _spendHintShown = false;
            return;
        }
        if (_spendHintShown) return;
        _spendHintShown = true;
        // A held island's notice says why it is held (RoomHud._Process); nothing is said over it.
        if (JournalOpen || host.IslandHeld) return;
        SpendHintCount++;
        _noticeText = SpendHint = FractalSpend.HintWords(host, K(Act.Journal));
    }
}
