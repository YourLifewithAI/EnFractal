using System;
using System.Threading.Tasks;
using EnFractal.Native.Kernel;
using Godot;

namespace EnFractal.Tests.Kernel;

public partial class PlayHudTest
{
    private const string HudGlowProfile = "user://tests/play_hud/profile/glow_progress_v1.json";
    private ulong _lastGlowPlaybackId;
    private ulong _lastGlowVoiceId;

    private static bool HudAudioResourceAlive(ulong id)
    {
        // IsInstanceIdValid creates a managed wrapper too, but does not dispose
        // it. Release this observation's reference so it cannot retain the sound.
        using var resource = InstanceFromId(id);
        return resource != null;
    }

    private async Task TestGlowJournalFresh()
    {
        var host = CommandHost.Of(_world)!;
        var button = (Button)_hud.FindChild("GlowJournalButton", true, false)!;
        Check(!_hud.GlowJournalOpen && !host.GlowProgress.HasDiscovery("lantern"), "a fresh trial starts with its Glow journal closed and no discovery claimed");
        button.EmitSignal(Button.SignalName.Pressed);
        await Frames(1);
        Check(_hud.GlowJournalOpen && !_world.Player.InputEnabled && !GetTree().Paused,
            "the journal button opens a local reading panel and holds player movement while the island continues");
        Check(_hud.GlowJournalText.Contains("3 lights", StringComparison.Ordinal) &&
              _hud.GlowJournalText.Contains("Your Glow discoveries will appear here.", StringComparison.Ordinal) &&
              !_hud.GlowJournalText.Contains("Lantern", StringComparison.Ordinal) && NoGlowSpoilers(_hud.GlowJournalText),
            "the fresh page shows the current light allowance without naming undiscovered abilities or their unlock conditions");
        var receipts = host.TransientReceiptCount(CommandHost.PlayerPrincipal);
        _hud._UnhandledInput(Press(Key.Key1));
        Check(host.TransientReceiptCount(CommandHost.PlayerPrincipal) == receipts && host.ActiveEffectIds.Count == 0,
            "reading the journal does not cast a light when an ability key is pressed");
        var previousView = _hud.ViewMode;
        _hud.SetViewMode(2);
        var distance = _hud.DioramaDistanceM;
        _hud._UnhandledInput(LeftButton(true));
        _hud._UnhandledInput(new InputEventMouseMotion { Relative = new Vector2(50, 20) });
        _hud._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
        _hud._UnhandledInput(RightButton(true));
        Check(!_hud.Drag.Pressed && !_hud.Wheel.Held && _hud.DioramaDistanceM == distance,
            "mouse presses over the open journal cannot begin a look drag, zoom or open the magic wheel");
        _hud.SetViewMode(previousView);
        _hud._UnhandledInput(Press(Key.C));
        Check(!_hud.GlowJournalOpen && _hud.Customizing && !_world.Player.InputEnabled,
            "customize closes the journal and keeps movement held by its own panel");
        button.EmitSignal(Button.SignalName.Pressed);
        Check(_hud.GlowJournalOpen && !_hud.Customizing && !_world.Player.InputEnabled,
            "opening the journal from customize leaves only the journal open");
        _hud._UnhandledInput(Press(Key.Escape));
        Check(!_hud.GlowJournalOpen && !_hud.Customizing && _world.Player.InputEnabled,
            "the existing Escape action closes the journal and restores player control");
    }

    private async Task TestGlowJournalLearned()
    {
        var host = CommandHost.Of(_world)!;
        _hud._UnhandledInput(Press(Key.X));
        // Seed only validated host metadata to exercise the persistent hint's presentation. A command cannot supply it.
        host.ConfigureGlowProgress(new HudGlowStore(GlowProgress.Restore(10, Array.Empty<string>(), new[] { "lantern_self" })));
        _hud.ToggleGlowJournal();
        await Frames(1);
        Check(_hud.GlowJournalText.Contains("Try asking me to glow on myself.", StringComparison.Ordinal) && NoGlowSpoilers(_hud.GlowJournalText),
            "a restored hint uses its curated words and suggests only the implemented self Glow");
        var cast = host.PlayerEffect("glow");
        await Frames(1);
        var chime = _world.Look.GlowRoot(host.ActiveEffectIds[0])!.GetNode<AudioStreamPlayer3D>("GlowChime");
        Check(chime.Playing && chime.HasStreamPlayback(), "the learned journal cast starts a real chime before its teardown check");
        _lastGlowVoiceId = chime.Stream.GetInstanceId();
        using (var playback = chime.GetStreamPlayback()) _lastGlowPlaybackId = playback.GetInstanceId();
        Check(cast["ok"]!.GetValue<bool>() && host.GlowProgress.HasDiscovery("lantern") &&
              _hud.GlowJournalText.Contains("Lantern\nA light that travels with the Gubble.", StringComparison.Ordinal),
            "a successful host cast records Lantern and the open journal shows its verified discovery");
        Check(!_hud.GlowJournalText.Contains("Try asking me", StringComparison.Ordinal) && NoGlowSpoilers(_hud.GlowJournalText),
            "the learned page retires the now-completed hint without revealing future discoveries");
        _hud._UnhandledInput(Press(Key.Escape));
        _hud._UnhandledInput(Press(Key.X));
        host.ConfigureGlowProgress(new LocalGlowProgressStore(ProjectSettings.GlobalizePath(HudGlowProfile)));
    }

    private static bool NoGlowSpoilers(string text) =>
        !text.Contains("Torch", StringComparison.Ordinal) && !text.Contains("Bioluminesce", StringComparison.Ordinal) &&
        !text.Contains("Constellation", StringComparison.Ordinal) && !text.Contains("Skylight", StringComparison.Ordinal) &&
        !text.Contains("casts", StringComparison.OrdinalIgnoreCase) && !text.Contains("XP", StringComparison.Ordinal);

    private sealed class HudGlowStore(GlowProgress state) : IGlowProgressStore
    {
        public GlowProgress Load() => state;
        public void Save(GlowProgress next) => state = next;
    }
}
