using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace EnFractal.Native.Kernel;

/// <summary>
/// One ability's list of expertise, fixed by the magic design (docs/magic/README.md): the ids the host may record, and the ids of the
/// hints that point at them. The store validates every id against its ability's list, so a name from a command, a journal or a
/// model can never be recorded. Glow's list is the original (its own record is GlowProgress's fields); the others are
/// ExpertiseRecords in GlowProgress.Others.
/// </summary>
public sealed class ExpertiseList
{
    public string Ability { get; }
    /// <summary>The expertise ids the store accepts, in the list's order.</summary>
    public IReadOnlyList<string> Order { get; }
    /// <summary>
    /// The hint ids the store accepts, one for each expertise in the same order, or none: Bubbles' and Fire's lists are not final and
    /// have no hints yet, so nothing is delivered for them and the store refuses any hint record under them.
    /// </summary>
    public IReadOnlyList<string> HintOrder { get; }

    internal ExpertiseList(string ability, IEnumerable<string> order, IEnumerable<string>? hintOrder = null)
    {
        Ability = ability;
        Order = new ReadOnlyCollection<string>(order.ToArray());
        HintOrder = new ReadOnlyCollection<string>((hintOrder ?? Array.Empty<string>()).ToArray());
        if ((HintOrder.Count != 0 && Order.Count != HintOrder.Count) || Order.Count > 5)
            throw new InvalidOperationException("An expertise list has at most five entries, each with a hint (or none of them has one yet).");
    }

    public bool Knows(string id) => Order.Contains(id, StringComparer.Ordinal);
}

/// <summary>The abilities with expertise in the store, their ids and their lists.</summary>
public static class ExpertiseLists
{
    public const string Glow = "glow";
    public const string Bubbles = "bubbles";
    public const string Fire = "fire";
    /// <summary>Grow (decision 52): Bloom's successor takes its place in the store; a store that holds "bloom" is read as Grow (LegacyBloom).</summary>
    public const string Grow = "grow";
    /// <summary>The id an older store may hold for Grow's record: Bloom's, read as Grow and written as Grow from then on.</summary>
    public const string LegacyBloom = "bloom";

    // Bubbles: the Bubble's own (packet M9), learned by the first carry that set the player down safely, the first step into a boat
    // and the first lift that rose the player.
    public const string BubbleCarry = "bubble_carry";
    public const string BubbleBoat = "bubble_boat";
    public const string BubbleLift = "bubble_lift";

    // Fire (the founder's redesign of 15 October: firepits that become campfires, a fire line, snuff, a will-o'-wisp on water). Its
    // behaviours are a later Fire packet's; nothing records these ids yet.
    public const string FirstFire = "first_fire";
    public const string Campfire = "campfire";
    public const string Fireline = "fireline";
    public const string Snuff = "snuff";
    public const string WillOWisp = "will_o_wisp";

    /// <summary>
    /// The ids the store accepts for Bubbles, in order: one constant, for the Bubbles redesign's later packet to extend to five (the
    /// founder, 15 October: one player-sized bubble cast at a spot, the boat found by casting it on water).
    /// </summary>
    public static readonly string[] BubblesOrder = { BubbleCarry, BubbleBoat, BubbleLift };

    /// <summary>The ids the store accepts for Fire, in order: one constant, for the later Fire packet to change as its design settles.</summary>
    public static readonly string[] FireOrder = { FirstFire, Campfire, Fireline, Snuff, WillOWisp };

    /// <summary>
    /// The ids the store accepts for Grow: none yet. Grow's list (tall_plant, thicket, green_ash, rain_garden, waterside in
    /// docs/magic/README.md) waits for the founder, so the store holds Grow's record with nothing learned.
    /// </summary>
    public static readonly string[] GrowOrder = Array.Empty<string>();

    public static readonly ExpertiseList GlowList = new(Glow, GlowProgress.DiscoveryOrder, GlowProgress.HintOrder);
    // No hints for Bubbles or Fire until their lists are final: nothing is delivered, and the store refuses any hint under them.
    public static readonly ExpertiseList BubblesList = new(Bubbles, BubblesOrder);
    public static readonly ExpertiseList FireList = new(Fire, FireOrder);
    public static readonly ExpertiseList GrowList = new(Grow, GrowOrder);

    /// <summary>Every ability the store holds, Glow first: the order the store writes them.</summary>
    public static readonly IReadOnlyList<ExpertiseList> All = new ReadOnlyCollection<ExpertiseList>(new[] { GlowList, BubblesList, FireList, GrowList });

    public static ExpertiseList? Find(string? ability) => ability == null ? null : All.FirstOrDefault(list => list.Ability == (ability == LegacyBloom ? Grow : ability));
}

/// <summary>
/// What the team learned in one ability besides Glow (Glow's is GlowProgress's own fields, the founder's original store): the
/// expertise learned, the verified casts and the hints delivered (the last two stay at none until a packet records them). Immutable;
/// every change is a candidate the host saves before it publishes. An expertise is recorded once, whatever happens again.
/// </summary>
public sealed class ExpertiseRecord
{
    public ExpertiseList List { get; }
    public int SuccessfulCasts { get; }
    public IReadOnlyList<string> Discoveries { get; }
    public IReadOnlyList<string> DeliveredHints { get; }

    private ExpertiseRecord(ExpertiseList list, int casts, string[] discoveries, string[] hints)
    {
        List = list;
        SuccessfulCasts = casts;
        Discoveries = new ReadOnlyCollection<string>(discoveries);
        DeliveredHints = new ReadOnlyCollection<string>(hints);
    }

    public string Ability => List.Ability;
    public bool IsEmpty => SuccessfulCasts == 0 && Discoveries.Count == 0 && DeliveredHints.Count == 0;
    public bool Has(string id) => Discoveries.Contains(id, StringComparer.Ordinal);

    public static ExpertiseRecord Empty(ExpertiseList list) => new(list, 0, Array.Empty<string>(), Array.Empty<string>());

    /// <summary>A view of a record that was already checked (Glow's own fields, which carry the constellation's one-cast slack): no validation.</summary>
    internal static ExpertiseRecord Unchecked(ExpertiseList list, int casts, IEnumerable<string> discoveries, IEnumerable<string> hints) =>
        new(list, casts, discoveries.ToArray(), hints.ToArray());

    /// <summary>
    /// An expertise the host verified from the simulation itself (the Bubble's carry, boat and lift): recorded once, in the list's
    /// order, however often it happens again; an id already learned returns this record. An id the list does not hold is refused.
    /// </summary>
    public ExpertiseRecord WithLearned(string id)
    {
        if (!List.Knows(id)) throw new GlowProgressException("Expertise has an invalid discovery.");
        if (Has(id)) return this;
        return Restore(List, SuccessfulCasts, Discoveries.Append(id), DeliveredHints);
    }

    /// <summary>Validated restoration (a saved store, never a command or the companion): known ids, each once, counts that agree.</summary>
    public static ExpertiseRecord Restore(ExpertiseList list, int casts, IEnumerable<string> discoveries, IEnumerable<string> hints)
    {
        if (casts < 0 || casts > GlowProgress.MaxSuccessfulCasts) throw new GlowProgressException("Expertise has an invalid use count.");
        var found = Validated(discoveries, list.Order, "Expertise has invalid discoveries.");
        var delivered = Validated(hints, list.HintOrder, "Expertise has invalid hint records.");
        // The expertise here are events, never counted casts, so only the hints (one per ten casts) are checked against the count.
        if (casts < delivered.Length * GlowProgress.HintAfterCasts) throw new GlowProgressException("Expertise has inconsistent use records.");
        return new ExpertiseRecord(list, casts, found, delivered);
    }

    public bool Same(ExpertiseRecord? other) => ReferenceEquals(this, other) || (other != null && List == other.List && SuccessfulCasts == other.SuccessfulCasts &&
        Discoveries.SequenceEqual(other.Discoveries, StringComparer.Ordinal) && DeliveredHints.SequenceEqual(other.DeliveredHints, StringComparer.Ordinal));

    private static string[] Validated(IEnumerable<string> values, IReadOnlyList<string> known, string error)
    {
        if (values == null) throw new GlowProgressException(error);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
            if (value == null || !known.Contains(value, StringComparer.Ordinal) || !seen.Add(value)) throw new GlowProgressException(error);
        return known.Where(seen.Contains).ToArray();
    }
}
