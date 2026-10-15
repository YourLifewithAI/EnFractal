using System;
using System.Collections.Generic;

namespace EnFractal.Native.Kernel;

/// <summary>
/// The rules the player's body and the pool play by now (VitalsRules): the island's pack, the mode, the difficulty, the
/// team's knacks and the expertise learned. This file is the modes packet's (M10 of docs/runs/RUN-2-MECHANICS.md): the
/// difficulty and the knacks are set here by the boot (the teams packet, M3, from the island save and the team file),
/// never by a request; the Live rows stay refused at the menu and play as Challenge Standard meanwhile (VitalsRules).
/// </summary>
public partial class CommandHost
{
    /// <summary>The Challenge difficulty this save plays at: gentle, standard (the default, today's) or brave. Not read in Creative or Live.</summary>
    public string Difficulty { get; private set; } = "standard";
    /// <summary>The team's knacks by id with their ranks (rank 1 first), as the team file holds them. Empty: no knacks, today's body.</summary>
    public IReadOnlyDictionary<string, int> Knacks => _knacks;
    /// <summary>The rules the body and the pool play by now.</summary>
    public VitalsRules BodyRules => _vitalsRules;
    private IReadOnlyDictionary<string, int> _knacks = new Dictionary<string, int>(StringComparer.Ordinal);
    private VitalsRules _vitalsRules = VitalsRules.For(null, "creative");

    /// <summary>The boot's difficulty for this save (the menu's choice, kept in the island save). Anything else keeps the current one.</summary>
    public void UseDifficulty(string difficulty)
    {
        if (difficulty is not ("gentle" or "standard" or "brave")) return;
        Difficulty = difficulty;
        ResolveVitalsRules();
    }

    /// <summary>The team's knacks (the team file's), by id and rank: a knack the pack lacks or has not made available counts for nothing.</summary>
    public void UseKnacks(IReadOnlyDictionary<string, int> ranks)
    {
        var copy = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, rank) in ranks)
            if (!string.IsNullOrEmpty(id) && rank is >= 1 and <= 3) copy[id] = rank;
        _knacks = copy;
        ResolveVitalsRules();
    }

    /// <summary>The expertise learned by the pair, which raises the pool's maximum one pip per expertise_per_pip (today Glow's discoveries).</summary>
    public int ExpertiseCount() => GlowProgress.Discoveries.Count;

    private VitalsRules ResolvedRules(string mode) => VitalsRules.For(Rules, mode, Difficulty, _knacks, ExpertiseCount());

    /// <summary>
    /// The rules again, after the pack, the mode, the difficulty, the knacks or the expertise changed: the current body is
    /// brought to them (its maxima, whole quarters, the pool within its maximum). With pace (a new expertise), a raised pool
    /// maximum is a pace line.
    /// </summary>
    private void ResolveVitalsRules(bool pace = false)
    {
        var before = _vitalsRules;
        _vitalsRules = ResolvedRules(Mode);
        if (!_vitalsRules.InBands) _slowed = false;
        if (_playerVitalsUnavailable) return;
        var normalized = _vitalsRules.Normalize(Vitals);
        if (!ReferenceEquals(normalized, Vitals))
        {
            _playerModes = _playerModes.WithCurrent(normalized);
            _vitalsDirty = true;
        }
        if (pace && _vitalsRules.HasPool && before.HasPool && before.Mode == _vitalsRules.Mode && _vitalsRules.MaxPips > before.MaxPips)
            NotePace("pips", _vitalsRules.MaxPips);
    }
}
