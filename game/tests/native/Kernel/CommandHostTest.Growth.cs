using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Look;

namespace EnFractal.Tests.Kernel;

public partial class CommandHostTest
{
    /// <summary>Host observations earn progress only after a real cast and a successful save.</summary>
    private void TestGlowGrowth(LookDirector look)
    {
        TestGlowProgressState();
        TestGlowProgressStore();
        TestGlowProgressCommands(look);
        TestGlowEarnedLimits(look);
        _host.ConfigureGlowProgress(new GrowthTestStore());
        _host.LoadRules(RoomWorld.DefaultRulesId, RoomWorld.DefaultRulesVersion);
    }

    private void TestGlowProgressState()
    {
        var fresh = GlowProgress.Fresh;
        var lantern = fresh.AfterCast(true);
        var repeated = lantern.AfterCast(true);
        Check(fresh.SuccessfulCasts == 0 && fresh.Discoveries.Count == 0 && fresh.ActiveLightAllowance == 3 &&
            lantern.SuccessfulCasts == 1 && lantern.HasDiscovery("lantern") && repeated.SuccessfulCasts == 2 &&
            repeated.Discoveries.SequenceEqual(new[] { "lantern" }) && repeated.ActiveLightAllowance == 3,
            "Glow starts at three lights; a real Lantern observation records one discovery without changing the previous state or farming duplicates");
        var four = GlowProgress.Restore(2, new[] { "torch", "lantern" }, Array.Empty<string>());
        var five = GrowthComplete();
        Check(four.ActiveLightAllowance == 4 && four.Discoveries.SequenceEqual(new[] { "lantern", "torch" }) && five.ActiveLightAllowance == 5,
            "two distinct restored discoveries allow four lights, and all five allow five");
        var points = fresh;
        for (var i = 0; i < GlowProgress.HintAfterCasts - 1; i++) points = points.AfterCast(false);
        Check(points.Discoveries.Count == 0 && points.DeliveredHints.Count == 0, "ordinary point Glow neither invents a future discovery nor nudges early");
        points = points.AfterCast(false);
        Check(points.SuccessfulCasts == 10 && points.DeliveredHints.SequenceEqual(new[] { GlowProgress.LanternHint }) &&
            points.AfterCast(false).DeliveredHints.Count == 1 && points.AfterCast(true).HasDiscovery("lantern") &&
            GlowProgress.Restore(9, new[] { "lantern" }, Array.Empty<string>()).AfterCast(false).DeliveredHints.Count == 0,
            "ten successful uses nudge only the undiscovered Lantern, once, and its later discovery remains valid");
        Check(GrowthRefused(() => GlowProgress.Restore(-1, Array.Empty<string>(), Array.Empty<string>())) &&
            GrowthRefused(() => GlowProgress.Restore(GlowProgress.MaxSuccessfulCasts + 1, Array.Empty<string>(), Array.Empty<string>())) &&
            GrowthRefused(() => GlowProgress.Restore(0, new[] { "lantern" }, Array.Empty<string>())) &&
            GrowthRefused(() => GlowProgress.Restore(2, new[] { "lantern", "lantern" }, Array.Empty<string>())) &&
            GrowthRefused(() => GlowProgress.Restore(2, new[] { "make-me-powerful" }, Array.Empty<string>())) &&
            GrowthRefused(() => GlowProgress.Restore(9, Array.Empty<string>(), new[] { GlowProgress.LanternHint })),
            "progress restoration refuses impossible counts, duplicate or unknown discoveries, and premature hints");
        Check(GlowProgress.Restore(GlowProgress.MaxSuccessfulCasts, new[] { "lantern" }, Array.Empty<string>()).AfterCast(true).SuccessfulCasts == GlowProgress.MaxSuccessfulCasts,
            "the bounded use counter saturates without wrapping or resetting progress");
    }

    private void TestGlowProgressStore()
    {
        var path = ProjectSettings.GlobalizePath($"{TestRoot}/growth/{Guid.NewGuid():N}/progress.json");
        var store = new LocalGlowProgressStore(path);
        Check(store.Load().SuccessfulCasts == 0 && !File.Exists(path), "a missing progression profile loads fresh without writing a save");
        var state = GlowProgress.Fresh;
        for (var i = 0; i < 10; i++) state = state.AfterCast(false);
        state = state.AfterCast(true);
        store.Save(state);
        var bytes = File.ReadAllBytes(path);
        var loaded = new LocalGlowProgressStore(path).Load();
        Check(loaded.SuccessfulCasts == 11 && loaded.HasDiscovery("lantern") && loaded.DeliveredHints.SequenceEqual(new[] { GlowProgress.LanternHint }) &&
            bytes[0] == (byte)'{' && bytes[^1] == (byte)'\n' && !bytes.Contains((byte)'\r'),
            "the local profile roundtrips uses, discoveries and delivered hints as UTF-8 without BOM and with LF");
        var validText = Encoding.UTF8.GetString(bytes);
        var malformed = new[]
        {
            "{", validText.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal),
            validText.Replace("\"lantern\"", "\"wish-granted\"", StringComparison.Ordinal),
            validText.Replace("\"successful_casts\":11", "\"successful_casts\":1000001", StringComparison.Ordinal),
            validText.Replace("\"successful_casts\":11", "\"successful_casts\":11,\"successful_casts\":11", StringComparison.Ordinal),
            validText.Replace("\"schema\":", "\"unexpected\":true,\"schema\":", StringComparison.Ordinal),
        };
        Check(malformed.All(text => GrowthRefused(() => LocalGlowProgressStore.Parse(Encoding.UTF8.GetBytes(text)))) &&
            GrowthRefused(() => LocalGlowProgressStore.Parse(new byte[LocalGlowProgressStore.MaxSaveBytes + 1])) &&
            GrowthRefused(() => LocalGlowProgressStore.Parse(new byte[] { (byte)'{', 0xff, (byte)'}' })),
            "progress saves reject malformed JSON, newer versions, unknown IDs, excessive counts, duplicate or extra fields and oversized input");
        using (var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, FileShare.None))
            Check(GrowthRefused(() => store.Save(state.AfterCast(false))) && File.ReadAllBytes(path).SequenceEqual(bytes),
                "an active profile writer lease prevents a competing write without damaging the saved progress");
        store.Save(state.AfterCast(false));
        Check(new LocalGlowProgressStore(path).Load().SuccessfulCasts == 12,
            "saving recovers after the other writer releases its profile lease");
        var changed = new LocalGlowProgressStore(path);
        changed.Load();
        store.Save(state.AfterCast(false).AfterCast(false));
        var latest = File.ReadAllBytes(path);
        Check(GrowthRefused(() => changed.Save(state)) && changed.IsReadOnly && File.ReadAllBytes(path).SequenceEqual(latest),
            "a stale store cannot overwrite newer progression saved by another session");
        File.WriteAllText(path, "{\"version\":999}", new UTF8Encoding(false));
        var corruptBytes = File.ReadAllBytes(path);
        var corrupt = new LocalGlowProgressStore(path);
        Check(GrowthRefused(() => corrupt.Load()) && corrupt.IsReadOnly && GrowthRefused(() => corrupt.Save(GlowProgress.Fresh)) &&
            File.ReadAllBytes(path).SequenceEqual(corruptBytes), "a corrupt or unsupported profile is kept byte-for-byte and is never replaced with fresh progress");
    }

    private void TestGlowProgressCommands(LookDirector look)
    {
        _host.LoadRules(RoomWorld.DefaultRulesId, RoomWorld.DefaultRulesVersion);
        var store = new GrowthTestStore();
        _host.ConfigureGlowProgress(store);
        var preview = Glow("growth-preview", SelfArgs());
        preview["preview"] = true;
        Check(Ok(Send(preview, Companion)) && _host.GlowProgress.SuccessfulCasts == 0 && store.Saves == 0 && look.GlowCount == 0,
            "previewing Lantern neither casts, learns nor saves progression");
        var refused = Send(Glow("growth-invalid", SelfArgs(new JsonObject { ["intensity"] = 1.4 })), Player);
        Check(Code(refused) == "invalid_args" && store.Saves == 0 && _host.GlowProgress.Discoveries.Count == 0,
            "a refused Glow command earns no progress");
        for (var i = 0; i < GlowLook.MaxGlows; i++) look.StartGlow("growth-other-" + i, null, InTheOpen, 0.3f, 0.5f);
        var lookRefused = Send(Glow("growth-look-full", SelfArgs()), Companion);
        Check(Code(lookRefused) == "budget_exceeded" && _host.ActiveEffectIds.Count == 0 && store.Saves == 0 && _host.GlowProgress.SuccessfulCasts == 0,
            "a renderer refusal cannot award a discovery for an effect it never showed");
        look.StopAllGlows();
        var firstCommand = Glow("growth-player-lantern", SelfArgs());
        var first = Send(firstCommand, Player);
        var replay = Send(firstCommand, Player);
        Check(Ok(first) && Ok(replay) && replay["replayed"]!.GetValue<bool>() && _host.GlowProgress.SuccessfulCasts == 1 &&
            _host.GlowProgress.HasDiscovery("lantern") && store.Saves == 1 && look.GlowCount == 1,
            "the player's successful Lantern learns and saves once, and replay never earns twice");
        var companion = Send(Glow("growth-companion-lantern", SelfArgs()), Companion);
        Check(Ok(companion) && _host.GlowProgress.SuccessfulCasts == 2 && _host.GlowProgress.Discoveries.Count == 1 && store.Saves == 2,
            "a connected companion's successful cast advances the same team progress without a second Lantern discovery");
        GrowthStop("after-lanterns");
        Check(Ok(Send(Glow("growth-point", PointArgs(InTheOpen)), Companion)) && _host.GlowProgress.SuccessfulCasts == 3 &&
            _host.GlowProgress.Discoveries.SequenceEqual(new[] { "lantern" }), "a point cast counts as practice without awarding an unimplemented Glow behaviour");
        Check(Ok(Send(Glow("growth-bubbles", SelfArgs(capability: "bubbles")), Player)) && _host.GlowProgress.SuccessfulCasts == 3 && store.Saves == 3,
            "a successful Bubbles cast does not count as Glow practice");
        _host.UseRules(Rules(Variant("\"tier\": \"auto\"", "\"tier\": \"keyed_yes\"")));
        var held = Send(Glow("growth-held", SelfArgs()), Companion);
        var approval = held["approval_needed"]?["request_id"]?.GetValue<string>() ?? "";
        Check(Code(held) == "approval_required" && _host.GlowProgress.SuccessfulCasts == 3 && store.Saves == 3 && look.GlowCount == 0,
            "a held Glow request earns nothing before the player approves it");
        var approved = _host.Approve(approval);
        Check(Ok(approved) && _host.GlowProgress.SuccessfulCasts == 4 && store.Saves == 4,
            "an approved Glow request earns progress when it actually starts");
        _host.LoadRules(RoomWorld.DefaultRulesId, RoomWorld.DefaultRulesVersion);
        var saveFailure = new GrowthTestStore { FailSave = true };
        _host.ConfigureGlowProgress(saveFailure);
        var retryCommand = Glow("growth-save-retry", SelfArgs());
        var failed = Send(retryCommand, Player);
        var receipt = Query("receipt.lookup", new JsonObject { ["action_id"] = "growth-save-retry" }, Player);
        Check(Code(failed) == "not_ready" && _host.GlowProgress.SuccessfulCasts == 0 && _host.ActiveEffectIds.Count == 0 && look.GlowCount == 0 &&
            !receipt["data"]!["found"]!.GetValue<bool>() && _host.GlowProgressNotice.Length > 0,
            "a progression save failure rolls back the staged light, progress and receipt, and reports the problem");
        saveFailure.FailSave = false;
        Check(Ok(Send(retryCommand, Player)) && _host.GlowProgress.SuccessfulCasts == 1 && look.GlowCount == 1 && _host.GlowProgressNotice.Length == 0,
            "retrying the same command after storage recovers starts and earns exactly once");
        GrowthStop("after-retry");
        _host.ConfigureGlowProgress(new GrowthTestStore { FailLoad = true });
        Check(_host.GlowProgressNotice.Length > 0 && Code(Send(Glow("growth-load-failed", SelfArgs()), Player)) == "not_ready" &&
            _host.ActiveEffectIds.Count == 0 && look.GlowCount == 0,
            "a profile that cannot load produces a clear notice and cannot be overwritten by a cast");
    }

    private void TestGlowEarnedLimits(LookDirector look)
    {
        // Restored fixtures exercise the future thresholds; no live command awards these unimplemented variants.
        var levels = new[]
        {
            GlowProgress.Fresh,
            GlowProgress.Restore(2, new[] { "lantern", "torch" }, Array.Empty<string>()),
            GrowthComplete(),
        };
        foreach (var level in levels)
        {
            _host.LoadRules(RoomWorld.DefaultRulesId, RoomWorld.DefaultRulesVersion);
            _host.ConfigureGlowProgress(new GrowthTestStore(level));
            var limit = level.ActiveLightAllowance;
            Check(_host.EffectiveEffectLimit("glow") == limit, $"the host exposes the earned {limit}-light allowance under v3");
            for (var i = 0; i < limit; i++)
                Check(Ok(Send(Glow($"growth-limit-{limit}-{i}", SelfArgs()), i % 2 == 0 ? Player : Companion)), $"earned allowance {limit} admits light {i + 1}");
            var over = Send(Glow($"growth-limit-{limit}-over", SelfArgs()), Companion);
            Check(Code(over) == "budget_exceeded" && over["error"]!["allowed"]!.GetValue<int>() == limit && look.GlowCount == limit &&
                _host.GlowProgress.SuccessfulCasts == level.SuccessfulCasts + limit,
                $"the earned {limit}-light cap applies across both principals and excess attempts earn nothing");
        }
        _host.LoadRules("storybook_wild", 2);
        Check(_host.EffectiveEffectLimit("glow") == 3, "a fully learned Gubble still obeys the older v2 island's three-light rule");
        for (var i = 0; i < 3; i++) Send(Glow($"growth-v2-{i}", SelfArgs()), Player);
        Check(Code(Send(Glow("growth-v2-over", SelfArgs()), Companion)) == "budget_exceeded" && look.GlowCount == 3,
            "progression never raises the pinned v2 island limit");
        _host.LoadRules(RoomWorld.DefaultRulesId, RoomWorld.DefaultRulesVersion);
        for (var i = 0; i < 5; i++) Send(Glow($"growth-global-glow-{i}", SelfArgs()), Player);
        for (var i = 0; i < 2; i++) Send(Glow($"growth-global-bubbles-{i}", SelfArgs(capability: "bubbles")), Player);
        Send(Glow("growth-global-fireworks", SelfArgs(capability: "fireworks", duration: 4)), Player);
        var ninth = Send(Glow("growth-global-over", SelfArgs(capability: "fireworks", duration: 4)), Companion);
        Check(_host.ActiveEffectIds.Count == 8 && look.EffectCount == 8 && Code(ninth) == "budget_exceeded" && ninth["error"]!["allowed"]!.GetValue<int>() == 8,
            "five earned lights share the unchanged eight-effect room cap with Bubbles and Fireworks");
        GrowthStop("finished");
    }

    private void GrowthStop(string suffix) => Send(Command("growth-stop-" + suffix, "effect.stop", new JsonObject { ["effect"] = "all" }), Player);

    private static GlowProgress GrowthComplete() => GlowProgress.Restore(5,
        new[] { "lantern", "torch", "bioluminesce", "constellation", "skylight" }, Array.Empty<string>());

    private static bool GrowthRefused(Action action)
    {
        try { action(); return false; }
        catch (GlowProgressException) { return true; }
    }

    private sealed class GrowthTestStore : IGlowProgressStore
    {
        private GlowProgress _state;
        public int Saves { get; private set; }
        public bool FailSave { get; set; }
        public bool FailLoad { get; set; }

        public GrowthTestStore(GlowProgress? state = null) => _state = state ?? GlowProgress.Fresh;
        public GlowProgress Load() => FailLoad ? throw new GlowProgressException("The test profile could not load.") : _state;
        public void Save(GlowProgress state)
        {
            if (FailSave) throw new GlowProgressException("The test profile could not save.");
            _state = state;
            Saves++;
        }
    }
}
