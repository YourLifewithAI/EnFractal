using System;
using System.Linq;
using Godot;

namespace EnFractal.Native;

/// <summary>
/// What the player can ask the Gubble without words (the magic design's "Directing the Gubble without words"; RUN-2-GLOW.md
/// section 4): the five ability slots, fixed by category, the wheel's eight fixed wedges, and the smart ask's rules. One
/// icon set serves the wheel, the number keys and the thought bubble, so each teaches the others. The icons and words are a
/// first pass for the founder's eye, kept here together to tune.
/// </summary>
public static class GubbleMagic
{
    /// <summary>The ability slots by category, fixed by the engine: slot 1 light, 2 growth, 3 float, 4 burst, 5 build. Keys 1 to 5 are these.</summary>
    public static readonly string[] SlotCategories = { "light", "growth", "float", "burst", "build" };
    /// <summary>A slot's name while the island has no ability in its category (the island's own display_name replaces it).</summary>
    public static readonly string[] SlotNames = { "Glow", "Bloom", "Bubbles", "Fireworks", "Build" };
    public static readonly string[] SlotIcons = { "☀", "✿", "○", "✸", "⌂" };

    // The orders' icons, and the thought bubble's own.
    public const string FollowIcon = "→";
    public const string StayIcon = "‖";
    public const string ComeIcon = "↩";
    public const string StopIcon = "■";
    public const string LookIcon = "◎";
    public const string FetchIcon = "✋";
    /// <summary>Put down what the Gubble holds (the wheel's Fetch wedge while it holds something, and the smart ask).</summary>
    public const string PutDownIcon = "↓";
    /// <summary>The Fetch wedge's name while the Gubble holds something.</summary>
    public const string PutDownName = "Put down";
    /// <summary>A put-down asked of a Gubble holding nothing: the HUD's own words (no command was sent).</summary>
    public const string NothingHeld = "holding nothing";
    public const string DoneMark = "✓";
    public const string RefusedMark = "✕";
    public const string UnknownIcon = "?";
    /// <summary>What the Gubble says, with a shrug, of a slot the island has no ability for.</summary>
    public const string NotYet = "not yet";
    /// <summary>A fetch asked of nothing: the HUD's own words (no command was sent).</summary>
    public const string NothingToFetch = "aim at something to fetch";
    /// <summary>A tap aimed at nothing (the sky): the HUD's own words.</summary>
    public const string NothingThere = "aim at a spot";
    /// <summary>A walk that could not arrive (the host fails it after its unreachable timeout): the HUD's own words.</summary>
    public const string CannotGetThere = "can't get there";
    /// <summary>The dusk moment's hint: {0} is slot 1's key, {1} the ask button's.</summary>
    public const string DuskHint = "{0} or {1}: Light!";

    public enum WedgeKind { Ability, Come, Stay, Fetch }

    /// <summary>One wedge of the wheel: an ability slot (0-based Slot), or an order.</summary>
    public readonly record struct Wedge(string Id, WedgeKind Kind, int Slot, string Icon, string Name);

    /// <summary>
    /// The wheel's eight wedges, clockwise from the top, never reflowed (muscle memory needs them fixed): Glow up, Fireworks upper
    /// right, Stay right, Bloom lower right, Build down, Fetch lower left, Come left, Bubbles upper left.
    /// </summary>
    public static readonly Wedge[] Wedges =
    {
        new("glow", WedgeKind.Ability, 0, SlotIcons[0], SlotNames[0]),
        new("fireworks", WedgeKind.Ability, 3, SlotIcons[3], SlotNames[3]),
        new("stay", WedgeKind.Stay, -1, StayIcon, "Stay"),
        new("bloom", WedgeKind.Ability, 1, SlotIcons[1], SlotNames[1]),
        new("build", WedgeKind.Ability, 4, SlotIcons[4], SlotNames[4]),
        new("fetch", WedgeKind.Fetch, -1, FetchIcon, "Fetch"),
        new("come", WedgeKind.Come, -1, ComeIcon, "Come"),
        new("bubbles", WedgeKind.Ability, 2, SlotIcons[2], SlotNames[2]),
    };

    /// <summary>The wedge a pointer offset from the wheel's centre falls in (screen pixels, +Y down), or null inside centrePx: the centre cancels.</summary>
    public static int? WedgeAt(Vector2 offset, float centrePx)
    {
        if (!offset.IsFinite() || offset.Length() < centrePx) return null;
        // 0 is straight up, increasing clockwise; each wedge is an eighth of the turn, centred on its direction.
        var angle = Mathf.Atan2(offset.X, -offset.Y);
        return Mathf.PosMod(Mathf.RoundToInt(angle / (Mathf.Pi / 4)), Wedges.Length);
    }

    /// <summary>The wedge of an ability slot (0-based).</summary>
    public static int WedgeOfSlot(int slot) => Array.FindIndex(Wedges, w => w.Kind == WedgeKind.Ability && w.Slot == slot);

    /// <summary>The smart ask's rules, the first that matches wins. While the Gubble holds something: put it down here, or put it there.</summary>
    public enum AskRule { None, Toggle, Fetch, Glow, Look, PutHere, PutThere }

    /// <summary>What the aim is on: whether it hit anything, where, the room entity it hit (an id, never a name), and whether it is the Gubble.</summary>
    public readonly record struct Aim(bool Hit, Vector3 Point, string? Entity, bool AtGubble);

    /// <summary>What a tap would ask, at what point and of what thing.</summary>
    public readonly record struct Ask(AskRule Rule, Aim Aim)
    {
        public Vector3 Point => Aim.Point;
        public string? Target => Aim.Entity;
    }

    /// <summary>
    /// The smart ask (the first rule that matches wins): aimed at the Gubble, switch stay and follow; at a carryable thing, fetch it;
    /// at a dark spot within Glow's reach, glow there; otherwise go and look there. Nothing hit: nothing to ask. While the Gubble
    /// holds something (the founder's playtest: it could pick a thing up but not put it down), aimed at the Gubble it puts it down
    /// here, and aimed at a spot it puts it there.
    /// </summary>
    public static Ask Decide(Aim aim, Func<string, bool> carryable, Func<Vector3, bool> darkWithinReach, bool gubbleHolds = false)
    {
        if (gubbleHolds && aim.AtGubble) return new(AskRule.PutHere, aim);
        if (gubbleHolds) return new(aim.Hit ? AskRule.PutThere : AskRule.None, aim);
        if (aim.AtGubble) return new(AskRule.Toggle, aim);
        if (aim.Entity != null && carryable(aim.Entity)) return new(AskRule.Fetch, aim);
        if (!aim.Hit) return new(AskRule.None, aim);
        if (darkWithinReach(aim.Point)) return new(AskRule.Glow, aim);
        return new(AskRule.Look, aim);
    }

    /// <summary>The reticle's words for what a tap would do now (icons and fixed words only, never a thing's name).</summary>
    public static string AskWords(AskRule rule, bool following, string glowName) => rule switch
    {
        AskRule.Toggle => following ? $"{StayIcon} wait here" : $"{FollowIcon} follow me",
        AskRule.Fetch => $"{FetchIcon} fetch it",
        AskRule.Glow => $"{SlotIcons[0]} {glowName.ToLowerInvariant()} there",
        AskRule.Look => $"{LookIcon} go and look",
        AskRule.PutHere => $"{PutDownIcon} put it down here",
        AskRule.PutThere => $"{PutDownIcon} put it there",
        _ => "",
    };

    /// <summary>
    /// The host's reason, short, beside the head-shake: its first clause, at most 60 characters. The host's refusals are its own
    /// templates (never a name or the player's words), so the text is the host's.
    /// </summary>
    public static string ShortReason(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "can't";
        var text = message.Trim();
        var cut = text.IndexOfAny(new[] { '.', ';', '!', '?' });
        if (cut > 0) text = text[..cut];
        text = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return text.Length <= 60 ? text : text[..59].TrimEnd() + "…";
    }
}
