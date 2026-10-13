using System.Collections.Generic;
using Godot;

namespace EnFractal.Native.Characters;

/// <summary>
/// The spring chains (K1's hair, hats, dangling legs, arms on springs, wands and sparkles) through a tumble: held still from the
/// flop to the end of the get-up, every part riding the hull the ground holds, then reset to the pose they ride in and let go.
/// Measured in the suite: free, a chain lagging 20° behind a body turning at 6 rad/s sweeps a hat's brim or a hand up to 1.7 cm
/// into the ground the body rolls onto, and still 1 to 3 mm while the body rises; a spring collider (a plane on the ground) only
/// keeps the chain's centre line out, never the part's mesh. The rig's own setup is restored as it was.
/// </summary>
public sealed class TumbleSprings
{
    private readonly CharacterBody _body;
    private readonly List<(SpringBoneSimulator3D Simulator, bool WasActive)> _held = new();

    public TumbleSprings(CharacterBody body) => _body = body;

    /// <summary>The springs are held still.</summary>
    public bool Held => _held.Count > 0;

    /// <summary>Every chain rides its parent as posed.</summary>
    public void Hold()
    {
        if (Held) return;
        foreach (var simulator in new[] { _body.Secondary.LegSimulator, _body.Secondary.Simulator })
        {
            if (simulator == null || !GodotObject.IsInstanceValid(simulator)) continue;
            _held.Add((simulator, simulator.Active));
            simulator.Active = false;
        }
    }

    /// <summary>The springs start again from the pose they ride in (no jump from where they last were), as active as the rig had them.</summary>
    public void Release()
    {
        foreach (var (simulator, wasActive) in _held)
        {
            if (!GodotObject.IsInstanceValid(simulator)) continue;
            simulator.Reset();
            simulator.Active = wasActive;
        }
        _held.Clear();
    }
}
