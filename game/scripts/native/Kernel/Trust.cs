using Godot;
using System;
using System.Collections.Generic;

namespace EnFractal.Native.Kernel;

/// <summary>
/// Trust's provisional numbers (docs/game/MECHANICS.md section 5, "Trust, the later dial"), named in one place until the rules
/// pack has a trust block (the change request in M8's report). Every number here is the founder's to move.
/// </summary>
public static class TrustLaws
{
    /// <summary>A Gubble action on its own initiative whose next CleanWindowS produced no consequence fact gains this.</summary>
    public const int CleanInitiative = 1;
    /// <summary>The window after a Gubble action in which a consequence fact spoils its gain (seconds, real time).</summary>
    public const double CleanWindowS = 60;
    /// <summary>At most one gain from clean initiative in this long (seconds, real time).</summary>
    public const double GainSpacingS = 120;
    /// <summary>Each consequence fact (the Gubble's fire hurt the player; the Gubble's fire burnt a build) moves Trust by this.</summary>
    public const int PerConsequence = -3;
    /// <summary>The player's "yes, like that" on a receipt.</summary>
    public const int MarkYes = 2;
    /// <summary>The player's "not that" on a receipt.</summary>
    public const int MarkNotThat = -2;
    /// <summary>The bands: Trust at or above each starts the next band (1, 2, 3).</summary>
    public static readonly int[] BandAt = { 5, 15, 30 };
    /// <summary>
    /// The Gubble's own-idea price at each band (0 to 3), as a share of the pack's own-idea price, rounded to whole pips and never
    /// below one pip for an ability that has a price: the bands widen the discount on its own ideas, every ability alike, Fire
    /// included (decision 42). Band 0 is the pack's price.
    /// </summary>
    public static readonly double[] OwnIdeaShare = { 1.0, 0.75, 0.5, 0.25 };
    /// <summary>
    /// The free early warning (the player told before a hazard reaches them): how near burning ground may come before the Gubble
    /// warns, at each band (0: none). Fire hurts within NatureLaws.PlayerBurnM (0.12 m), so each reach is well before that.
    /// </summary>
    public static readonly float[] EarlyWarningM = { 0f, 0.3f, 0.45f, 0.6f };
    /// <summary>"Lesson applied": a clean fire of the Gubble's own within this of where a consequence once followed one of its fires.</summary>
    public const float LessonPlaceM = 2.0f;
    /// <summary>The Gubble's commands the host remembers this session, for the windows and the marks; the oldest go first.</summary>
    public const int MaxRemembered = 1024;
}

/// <summary>Trust's pure rules: the band, the words (never a number on screen), the discounted own-idea price.</summary>
public static class Trust
{
    public const int Bound = 1_000_000;

    public static int Clamp(long count) => (int)Math.Clamp(count, -Bound, Bound);

    /// <summary>The band, 0 to 3: what the bands at 5, 15 and 30 open (the discount, the early warning, the aura's mark).</summary>
    public static int Band(int count)
    {
        var band = 0;
        foreach (var at in TrustLaws.BandAt)
            if (count >= at) band++;
        return band;
    }

    /// <summary>The words' tier: -1 below base (trust to win back), else the band. The journal says so when the tier changes.</summary>
    public static int Tier(int count) => count < 0 ? -1 : Band(count);

    /// <summary>The own-idea price at a band: the pack's own-idea price times the band's share, whole pips, at least one when priced.</summary>
    public static int OwnIdeaPrice(int ordered, double ownIdeaFactor, int band)
    {
        var share = TrustLaws.OwnIdeaShare[Math.Clamp(band, 0, TrustLaws.OwnIdeaShare.Length - 1)];
        var price = (int)Math.Round(ordered * ownIdeaFactor * share, MidpointRounding.AwayFromZero);
        return ordered > 0 ? Math.Max(1, price) : 0;
    }

    /// <summary>The words for the player's page (you, the player; the Gubble by its name), one tier each, no number.</summary>
    public static string PlayerWords(int count, string gubble) => Tier(count) switch
    {
        -1 => $"{gubble} has your trust to win back.",
        0 => $"{gubble} is earning your trust.",
        1 => $"You trust {gubble} a little.",
        2 => $"You trust {gubble}.",
        _ => $"You trust {gubble} deeply.",
    };

    /// <summary>The same tier as the journal's facts say it (the game's own words, for the player and the AI alike).</summary>
    public static string Phrase(int tier) => tier switch
    {
        -1 => "the Gubble has your trust to win back",
        0 => "the Gubble is earning your trust",
        1 => "you trust the Gubble a little",
        2 => "you trust the Gubble",
        _ => "you trust the Gubble deeply",
    };

    /// <summary>The journal's line when the tier changes: grew or fell, and the words now.</summary>
    public static string ChangeLine(int before, int after) => (after > before ? "Trust grew: " : "Trust fell: ") + Phrase(after) + ".";
}

/// <summary>
/// The Gubble's committed commands this session, as Trust needs them (host memory only; never saved, never in a receipt): each
/// one's 60 s window, whether a consequence fact spoiled it, and the player's mark. A receipt is marked once.
/// </summary>
public sealed class TrustLedger
{
    public sealed class Entry
    {
        public required string ActionId { get; init; }
        public required string Op { get; init; }
        /// <summary>The ability or goal it used (nature.cast's and effect.start's capability, goal.set's goal), or null.</summary>
        public string? Capability { get; init; }
        /// <summary>The words a mark's line names it by ("fire", "bubble carry", "fetch").</summary>
        public required string What { get; init; }
        /// <summary>Where it aimed, when the command named a place.</summary>
        public Vector3? Place { get; init; }
        public DateTime At { get; init; }
        /// <summary>Whether Trust counted when it committed (Challenge and Live): only those gain.</summary>
        public bool Counted { get; init; }
        /// <summary>The player when the player's click committed it (an approved suggestion, decision 45), else null.</summary>
        public string? ApprovedBy { get; init; }
        public bool Spoiled { get; internal set; }
        public bool Settled { get; internal set; }
        /// <summary>yes or not_that once the player marked its receipt.</summary>
        public string? Mark { get; internal set; }
        public DateTime Closes => At + TimeSpan.FromSeconds(TrustLaws.CleanWindowS);
    }

    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);

    /// <summary>When clean initiative last gained (the window's close), or null since the session began or the last reset.</summary>
    public DateTime? LastGain { get; private set; }
    public int Count => _entries.Count;

    public Entry? Find(string actionId) => _byId.GetValueOrDefault(actionId);

    /// <summary>A committed command of the Gubble's; the oldest is forgotten past TrustLaws.MaxRemembered.</summary>
    public void Note(Entry entry)
    {
        if (_byId.Remove(entry.ActionId, out var earlier)) _entries.Remove(earlier);
        _entries.Add(entry);
        _byId[entry.ActionId] = entry;
        while (_entries.Count > TrustLaws.MaxRemembered)
        {
            _byId.Remove(_entries[0].ActionId);
            _entries.RemoveAt(0);
        }
    }

    /// <summary>A consequence fact at this moment: every window still open over it is spoiled.</summary>
    public void Consequence(DateTime at)
    {
        foreach (var entry in _entries)
            if (!entry.Settled && entry.At <= at && at <= entry.Closes) entry.Spoiled = true;
    }

    /// <summary>
    /// The windows closed by now, in the order they closed, each settled once: whether it was clean and whether it gained (a clean,
    /// counted window gains when no gain came in the GainSpacingS before its close).
    /// </summary>
    public List<(Entry Entry, bool Clean, bool Gained)> Settle(DateTime now)
    {
        var closed = new List<Entry>();
        foreach (var entry in _entries)
            if (!entry.Settled && entry.Closes <= now) closed.Add(entry);
        closed.Sort((a, b) => a.Closes.CompareTo(b.Closes));
        var outcome = new List<(Entry, bool, bool)>(closed.Count);
        foreach (var entry in closed)
        {
            entry.Settled = true;
            var clean = !entry.Spoiled;
            var gained = clean && entry.Counted && (LastGain is not { } last || (entry.Closes - last).TotalSeconds >= TrustLaws.GainSpacingS);
            if (gained) LastGain = entry.Closes;
            outcome.Add((entry, clean, gained));
        }
        return outcome;
    }

    /// <summary>The player's reset: every open window ends with no gain and the spacing starts again. Marks stay: each receipt once.</summary>
    public void Reset()
    {
        foreach (var entry in _entries) entry.Settled = true;
        LastGain = null;
    }
}
