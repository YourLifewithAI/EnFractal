using Godot;
using System;

namespace EnFractal.Native.Kernel;

/// <summary>The three Bubbles expertise (contracts: nature.cast's capability).</summary>
public enum BubbleKind { Carry, Boat, Lift }

/// <summary>How a bubble ended (its receipt's ended): the player's dive key or X, its timer, or anything else.</summary>
public enum BubbleEnd { PoppedByPlayer, TimedOut, Popped }

/// <summary>
/// One bubble in the room: transient, like an effect (it lives for the session and nothing about it is saved), held by the command
/// host and drawn by the look. The kernel decides whether it holds the player; the body rides it (SmallPlayerController.Bubble.cs).
/// </summary>
internal sealed class Bubble
{
    /// <summary>'effect:' and 26 random base32 characters: effect.stop names it, and goal.stop of the Gubble pops it.</summary>
    public required string Id { get; init; }
    public required string Capability { get; init; }
    public required BubbleKind Kind { get; init; }
    /// <summary>Who cast it: the player stops any bubble, a companion only its own.</summary>
    public required string Principal { get; init; }
    public required string ActionId { get; init; }
    /// <summary>The cast's fingerprint, so a later update writes to this cast's receipt and no other.</summary>
    public required string Fingerprint { get; init; }
    /// <summary>The avatar a carry or a lift was cast on (previewed first), or null for one cast at a point (the step-in).</summary>
    public string? Target { get; init; }
    public required float Radius { get; init; }
    public required DateTime Expires { get; init; }
    /// <summary>The rules pack it was cast under: it pops when the rules change, as effects end.</summary>
    public IslandRules? Rules { get; init; }
    /// <summary>The bubble's centre (a carry's and a lift's sphere; the boat's deck, at its middle).</summary>
    public Vector3 Center { get; set; }
    /// <summary>Its motion over the last tick, which the body riding it is carried by (m/s).</summary>
    public Vector3 Velocity { get; set; }
    /// <summary>Where a carry or a lift began to hold the player: its hover and its rise are measured from here.</summary>
    public float BaseY { get; set; }
    /// <summary>It holds the player now: enveloped (a carry, a lift) or aboard (the boat).</summary>
    public bool Holding { get; set; }
    /// <summary>It has held the player: from the cast for one on the player, from the step-in, from the first step aboard.</summary>
    public bool Carried { get; set; }
    /// <summary>A lift has risen the player its whole rise.</summary>
    public bool Risen { get; set; }
    /// <summary>The player left the boat inside its rim (the dive key): it takes them again only once they have stepped off it.</summary>
    public bool BoardLatch { get; set; }
    /// <summary>How long the body that it holds has been outside it.</summary>
    public double RiderLostS { get; set; }

    /// <summary>The receipt's word for how it ended.</summary>
    public static string EndWord(BubbleEnd end) => end switch
    {
        BubbleEnd.PoppedByPlayer => "popped_by_player",
        BubbleEnd.TimedOut => "timed_out",
        _ => "popped",
    };

    public static BubbleKind KindOf(string capability) => capability switch
    {
        "bubble_boat" => BubbleKind.Boat,
        "bubble_lift" => BubbleKind.Lift,
        _ => BubbleKind.Carry,
    };
}
