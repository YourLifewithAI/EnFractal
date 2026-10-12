using Godot;
using System;

namespace EnFractal.Native.Kernel;

/// <summary>
/// The single-player Glow trial's profile, separate from a room's undoable state. Only verified,
/// successful casts can advance it. Scene nodes and companion-supplied journal notes are never evidence.
/// Effects and their receipts still last for the session; restarting and casting again is a new use.
/// </summary>
public partial class CommandHost
{
    /// <summary>Single-player trial profile; boot fixtures redirect this before creating RoomWorld.</summary>
    public static string DefaultGlowProgressPath { get; set; } = LocalGlowProgressStore.DefaultUserPath;
    public GlowProgress GlowProgress { get; private set; } = GlowProgress.Fresh;
    public string GlowProgressNotice { get; private set; } = "";
    private IGlowProgressStore? _glowProgressStore;
    private bool _glowProgressUnavailable;

    /// <summary>Trusted boot/test wiring only. No command or companion query can supply a profile.</summary>
    public void ConfigureGlowProgress(IGlowProgressStore store)
    {
        if (_effects.Count != 0) throw new InvalidOperationException("Configure growth before starting effects.");
        _glowProgressStore = store;
        try
        {
            var loaded = store.Load();
            GlowProgress = loaded;
            _glowProgressUnavailable = false;
            GlowProgressNotice = "";
        }
        catch (GlowProgressException)
        {
            _glowProgressUnavailable = true;
            GlowProgressNotice = "The Glow journal could not be loaded. Your saved progress has been kept; Glow is waiting for it to be recovered.";
        }
    }

    /// <summary>The learned allowance never exceeds either the pack or the engine's room limit.</summary>
    public int EffectiveEffectLimit(string capability)
    {
        var ability = Rules?.Ability(capability);
        if (ability == null) return 0;
        return Math.Min(Math.Min(ability.MaxActive, EffectLimit), capability == "glow" ? GlowProgress.ActiveLightAllowance : ability.MaxActive);
    }

    private void CheckGlowProgressAvailable(string capability)
    {
        if (capability == "glow" && _glowProgressUnavailable)
            throw new Refusal("not_ready", GlowProgressNotice);
    }

    /// <summary>
    /// Called after the look accepts a cast and before the effect/receipt is published. A write failure
    /// leaves this state untouched; the caller removes the temporary light before returning a refusal.
    /// The only implemented discovery is Lantern: a real Glow following the Gubble itself.
    /// </summary>
    private void RecordGlowCast(bool self)
    {
        var candidate = GlowProgress.AfterCast(lantern: self);
        try { _glowProgressStore?.Save(candidate); }
        catch (GlowProgressException)
        {
            GlowProgressNotice = "The Glow journal could not be saved. This cast was cancelled; your earlier progress is safe. Try again once saving is available.";
            throw new Refusal("not_ready", GlowProgressNotice, retryable: true);
        }
        GlowProgress = candidate;
        GlowProgressNotice = "";
    }
}
