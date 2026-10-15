using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Room;

namespace EnFractal.Tests.Kernel;

/// <summary>
/// The mechanics round's energy and vitals through the command path (M1 of docs/runs/RUN-2-MECHANICS.md), under storybook_wild
/// v5 whatever the shipped default: the price by principal charged once at commit (never on a preview, a replay, a stop or a
/// refusal), energy_exhausted with the balance and the price, Creative's shadow pool, a held command paying its own
/// principal's price at the click, a running wish making the Gubble's commands pay as ordered, the refill by distance and
/// difficulty, the status's energy, hearts and bar, fire in quarters and healing in whole quarters, the time away, and the
/// player's pickup of a Fractal from a manifest that places some.
/// </summary>
public partial class NatureTest
{
    private sealed class EnergyStore(PlayerModesProfile profile) : IPlayerVitalsStore
    {
        public PlayerModesProfile Saved { get; private set; } = profile;
        public int Saves { get; private set; }
        public PlayerModesProfile Load() => Saved;
        public void Save(PlayerModesProfile next) { Saved = next; Saves++; }
    }

    private sealed class EnergyGlow(GlowProgress progress) : IGlowProgressStore
    {
        public GlowProgress Load() => progress;
        public void Save(GlowProgress state) => progress = state;
    }

    private async Task TestEnergy()
    {
        await TestPriceByPrincipal();
        await TestShadowPool();
        await TestHeldCommandPays();
        await TestWishPaysAsOrdered();
        await TestRefillAndStatus();
        await TestQuartersInPlay();
        await TestVitalsTimeAway();
        await TestFractals();
    }

    /// <summary>A world under v5 with a body of our choosing in a mode (and difficulty).</summary>
    private async Task<EnergyStore> EnergyWorld(string name, string mode = "challenge", int pips = 8, string difficulty = "standard", Func<PlayerVitals, PlayerVitals>? body = null,
        DateTime? written = null, RoomData? room = null)
    {
        await NewWorld(SavePath(name), roomOverride: room);
        _host.LoadRules("storybook_wild", 5);
        _host.UseDifficulty(difficulty);
        var rules = VitalsRules.For(_host.Rules, mode, difficulty);
        var vitals = rules.Fresh().With(pips: pips);
        if (body != null) vitals = body(vitals);
        var modes = PlayerModesProfile.Modes.ToDictionary(m => m, m => m == mode ? vitals : PlayerVitals.Fresh);
        var stamps = written is { } at ? new Dictionary<string, DateTime> { [mode] = at } : null;
        var store = new EnergyStore(PlayerModesProfile.Restore(mode, modes, stamps));
        _host.ConfigurePlayerVitals(store);
        return store;
    }

    private int Pips => _host.Vitals.Pips;

    private JsonObject GlowSelf(string principal, string? actionId = null, bool preview = false) =>
        Send(Command(actionId ?? NextId("glow"), "effect.start", new JsonObject
        {
            ["capability"] = "glow", ["params"] = new JsonObject(),
            ["area"] = new JsonObject { ["center_m"] = Vec(_companion.GlobalPosition), ["radius_m"] = 0.6 }, ["duration_s"] = 30,
            ["targets"] = new JsonArray(CommandHost.CompanionAvatarId),
        }, preview), principal);

    private static JsonArray Vec(Vector3 at) => new(at.X, at.Y, at.Z);

    private static bool Exhausted(JsonObject result, int balance, int price) =>
        Code(result) == "energy_exhausted" && result["error"]?["balance"]?.GetValue<int>() == balance && result["error"]?["price"]?.GetValue<int>() == price &&
        result["error"]?["message"]?.GetValue<string>() == CommandHost.EnergyExhaustedMessage && result["error"]?["retryable"]?.GetValue<bool>() == true &&
        result["error"]?["field_path"]?.GetValue<string>() == "$.args.capability";

    private async Task TestPriceByPrincipal()
    {
        await EnergyWorld("energy-price");
        var status = Send(Query("player.status", new JsonObject()), Companion)["data"];
        Check(status?["energy"]?["pips"]?.GetValue<int>() == 8 && status["energy"]?["max_pips"]?.GetValue<int>() == 8 && status["energy"]?["shadow"]?.GetValue<bool>() == false &&
              status["hearts"]?["quarters"]?.GetValue<int>() == 20 && status["hearts"]?["max_quarters"]?.GetValue<int>() == 20 &&
              status["bar"]?["bands"]?.GetValue<int>() == 1 && status["bar"]?["slowed"]?.GetValue<bool>() == false && status["max_health"]?.GetValue<double>() == 100,
            "player.status carries the pool (8 of 8), the hearts in quarters (20 of 20) and the bar (one band, not slowed): " + Canonical(status));
        var own = GlowSelf(Companion, "own-idea-glow");
        Check(Ok(own) && Pips == 7, $"the Gubble's own Glow costs its own-idea price, one pip ({Pips} left)");
        var previewed = GlowSelf(Companion, preview: true);
        var replayed = GlowSelf(Companion, "own-idea-glow");
        Check(Ok(previewed) && previewed["preview"]?.GetValue<bool>() == true && Ok(replayed) && replayed["replayed"]?.GetValue<bool>() == true && Pips == 7,
            "a preview and a replay of the same command cost nothing");
        var far = Send(Command(NextId("far"), "effect.start", new JsonObject
        {
            ["capability"] = "glow", ["params"] = new JsonObject(), ["area"] = new JsonObject { ["center_m"] = new JsonArray(1.9, 0.05, -1.4), ["radius_m"] = 0.6 }, ["duration_s"] = 30,
        }), Companion);
        Check(Code(far) == "out_of_bounds" && Pips == 7, "a refused command costs nothing: " + Code(far));
        var ordered = GlowSelf(Player);
        Check(Ok(ordered) && Pips == 5, $"the player's order of the same Glow pays the ordered price, two pips ({Pips} left)");
        var stop = Send(Command(NextId("stop"), "effect.stop", new JsonObject { ["effect"] = "all" }), Player);
        Check(Ok(stop) && Pips == 5, "a stop costs nothing");
        var bloom = Send(Command(NextId("bloom"), "nature.cast", Cast("bloom", NearGubble, 0.15, 60)), Companion);
        Check(Ok(bloom) && Pips == 3 && bloom["transient"]?.GetValue<bool>() == false, $"the Gubble's own Bloom costs two pips at commit, a durable change ({Pips} left)");
        var revision = _host.Revision;
        var blocked = Send(Command(NextId("bloom-player"), "nature.cast", Cast("bloom", NearGubble + new Vector3(0.2f, 0, 0), 0.15, 60)), Player);
        Check(Exhausted(blocked, 3, 4) && _host.Revision == revision && Pips == 3,
            "the player's Bloom at four pips with three in the pool is refused energy_exhausted, carrying the balance and the price, and nothing changes: " + Canonical(blocked["error"]));
        var lit = Fire(Companion, new Vector3(0.9f, 0.01f, 1.2f), 0.15, 6, out var firePreview);
        Check(Ok(firePreview) && Ok(lit) && Pips == 0, $"the Gubble's Fire is previewed free and costs three pips when it is lit ({Pips} left)");
        var effects = _host.ActiveEffectIds.Count;
        var empty = GlowSelf(Companion);
        Check(Exhausted(empty, 0, 1) && _host.ActiveEffectIds.Count == effects, "an empty pool refuses even the cheapest cast, and nothing starts: " + Canonical(empty["error"]));
        var stillStops = _host.PlayerGoal("stop");
        Check(Ok(stillStops) && Send(Query("player.status", new JsonObject()), Companion)["data"]?["energy"]?["pips"]?.GetValue<int>() == 0,
            "stops still apply with an empty pool, and cost nothing");
    }

    private async Task TestShadowPool()
    {
        await EnergyWorld("energy-shadow", mode: "creative", pips: 2);
        var lit = Fire(Companion, new Vector3(0.9f, 0.01f, 1.2f), 0.15, 6, out _);
        var glow = GlowSelf(Companion);
        var status = Send(Query("player.status", new JsonObject()), Player)["data"];
        Check(Ok(lit) && Ok(glow) && Pips == 0 && status?["energy"]?["shadow"]?.GetValue<bool>() == true && status["consequences"]?.GetValue<bool>() == false,
            $"Creative's shadow pool drains with every cast, never below empty, and never refuses ({Pips} left): " + Canonical(status?["energy"]));
    }

    private async Task TestHeldCommandPays()
    {
        await EnergyWorld("energy-held");
        var text = File.ReadAllText(ProjectSettings.GlobalizePath(IslandRules.PathFor("storybook_wild", 5)));
        const string glowTier = "\"display_name\": \"Glow\",\n      \"cast_by\": [\"player\", \"companion\"],\n      \"tier\": \"auto\"";
        if (!text.Contains(glowTier, StringComparison.Ordinal)) throw new InvalidOperationException("v5's Glow no longer reads as expected");
        _host.UseRules(IslandRules.Parse(Encoding.UTF8.GetBytes(text.Replace(glowTier, glowTier.Replace("\"auto\"", "\"keyed_yes\""), StringComparison.Ordinal))));
        var held = GlowSelf(Companion);
        var request = held["error"]?["actual"]?.GetValue<string>() ?? held["request_id"]?.GetValue<string>() ?? _host.PendingApprovals.FirstOrDefault()?.RequestId;
        Check(Code(held) == "approval_required" && Pips == 8 && request != null, "a held command costs nothing while it waits for the player's click");
        var approved = _host.Approve(request!);
        Check(Ok(approved) && approved["approved_by"]?.GetValue<string>() == Player && Pips == 7,
            $"the click commits it under its own principal at that principal's price: the Gubble's one pip ({Pips} left)");
        _host.ConfigurePlayerVitals(new EnergyStore(PlayerModesProfile.Fresh.Select("challenge").WithCurrent(_host.BodyRules.Fresh().With(pips: 0))));
        var second = GlowSelf(Companion);
        var again = _host.PendingApprovals.FirstOrDefault()?.RequestId;
        var refused = again == null ? new JsonObject() : _host.Approve(again);
        Check(Code(second) == "approval_required" && Exhausted(refused, 0, 1) && _host.PendingApprovals.Count == 0,
            "a click that finds the pool empty ends the request with energy_exhausted, the balance and the price: " + Canonical(refused["error"]));
    }

    /// <summary>While a wish runs, the Gubble's commands pay as ordered, whatever their action ids say; a "w-" tag alone changes nothing.</summary>
    private async Task TestWishPaysAsOrdered()
    {
        await EnergyWorld("energy-wish");
        _host.ConfigureWishes(new LocalWishQueueStore(ProjectSettings.GlobalizePath($"{TestRoot}/energy-wish/wishes.json"), _room.RoomId));
        var tagged = GlowSelf(Companion, "w-not-a-wish");
        Check(!Ok(tagged) && Pips == 8, "a Gubble command tagged as a wish's with no wish running is refused and costs nothing: " + Code(tagged));
        var plain = GlowSelf(Companion);
        Check(Ok(plain) && Pips == 7, "without a wish the Gubble's own idea pays its own-idea price");
        Check(Ok(_host.PlayerWish("light the way")) && _host.HandleWishRunner("{\"op\":\"wish.wait\"}")["ok"]?.GetValue<bool>() == true && _host.Wishes.Active != null,
            "the player's wish is running");
        var during = GlowSelf(Companion);
        Check(Ok(during) && Pips == 5, $"while a wish runs the Gubble's command pays as ordered, two pips, though its action id names no wish ({Pips} left)");
    }

    private async Task TestRefillAndStatus()
    {
        await EnergyWorld("energy-refill", pips: 0);
        var near = _player.GlobalPosition.DistanceTo(_companion.GlobalPosition);
        _player.VitalsTick!(4.9f, true);
        var before = Pips;
        _player.VitalsTick!(0.2f, true);
        Check(near <= 2 && before == 0 && Pips == 1, $"with the Gubble within 2 m ({near:0.00} m) a pip refills every 5 s");
        _companion.GlobalPosition = _player.GlobalPosition + new Vector3(0, 0, -2.05f);
        var apart = _player.GlobalPosition.DistanceTo(_companion.GlobalPosition);
        _player.VitalsTick!(14.5f, true);
        var stillOne = Pips;
        _player.VitalsTick!(0.5f, true);
        Check(apart > 2 && stillOne == 1 && Pips == 2, $"apart ({apart:0.00} m) one every 15 s");
        _player.VitalsTick!(600, true);
        Check(Pips == 8, "and never past the pool's maximum");
        await EnergyWorld("energy-gentle", pips: 0, difficulty: "gentle");
        _player.VitalsTick!(10.1f, true);
        var gentle = Pips;
        await EnergyWorld("energy-brave", pips: 0, difficulty: "brave");
        _player.VitalsTick!(10.1f, true);
        Check(gentle == 3 && Pips == 1, $"Gentle refills 1.5 times as fast (3 in 10 s), Brave 0.75 times (1 in 10 s; got {gentle} and {Pips})");
        await EnergyWorld("energy-expertise", pips: 0);
        _host.ConfigureGlowProgress(new EnergyGlow(GlowProgress.Restore(40, new[] { "lantern", "torch", "bioluminesce", "constellation", "skylight" }, Array.Empty<string>())));
        var status = Send(Query("player.status", new JsonObject()), Companion)["data"];
        Check(status?["energy"]?["max_pips"]?.GetValue<int>() == 9, "a team that holds Glow's five expertise has a pool of 9: " + Canonical(status?["energy"]));
        await EnergyWorld("energy-creative-refill", mode: "creative", pips: 0);
        _player.VitalsTick!(5.1f, true);
        Check(Pips == 1, "Creative's shadow pool refills at the Standard pace");
    }

    private async Task TestQuartersInPlay()
    {
        await EnergyWorld("energy-fire");
        var lit = Fire(Player, PlayerSpot(), 0.25, 30, out _);
        Steps(1);
        var half = _host.Vitals.Health;
        Steps(1);
        var one = _host.Vitals.Health;
        Steps(2);
        var two = _host.Vitals.Health;
        Check(Ok(lit) && half == 100 && one == 95 && two == 90, $"fire takes one quarter a second, in whole quarters (after 0.5 s {half}, 1 s {one}, 2 s {two})");
        await EnergyWorld("energy-fire-gentle", difficulty: "gentle");
        Fire(Player, PlayerSpot(), 0.25, 30, out _);
        Steps(3);
        var gentleEarly = _host.Vitals.Health;
        Steps(1);
        Check(gentleEarly == 100 && _host.Vitals.Health == 95, $"Gentle halves fire and removes none: one quarter every 2 s ({gentleEarly}, then {_host.Vitals.Health})");
        await EnergyWorld("energy-heal");
        _host.ApplyPlayerDamage(14, PlayerDamageSource.Fall);
        var hurt = _host.Vitals.Health;
        _player.VitalsTick!(9.9f, true);
        var waiting = _host.Vitals.Health;
        _player.VitalsTick!(0.2f, true);
        _player.VitalsTick!(2.4f, true);
        var healed = _host.Vitals.Health;
        _player.VitalsTick!(5.1f, true);
        Check(hurt == 85 && waiting == 85 && healed == 90 && _host.Vitals.Health == 100,
            $"a hurt of 14 health takes three quarters (85); nothing heals for 10 s, then a whole quarter every 2.5 s on safe ground ({waiting}, {healed}, {_host.Vitals.Health})");
        _host.ApplyPlayerDamage(PlayerVitals.ContractMaxHealth, PlayerDamageSource.Fall);
        var status = Send(Query("player.status", new JsonObject()), Companion)["data"];
        Check(_host.Vitals.Recoveries == 1 && _host.Vitals.Health == 50 && Pips >= 4 && status?["hearts"]?["quarters"]?.GetValue<int>() == 10,
            $"a knockout wakes at camp with half the hearts (10 quarters) and the pool at least half full ({Pips})");
        var bigHeart = await EnergyWorld("energy-big-heart");
        _host.UseKnacks(new Dictionary<string, int> { ["big_heart"] = 2 });
        var fall = PlayerVitals.FallDamage(MathF.Sqrt(2 * 3.5f * 1.5f), false);
        var top = _host.Vitals.MaxHealth;
        _host.ApplyPlayerDamage(fall, PlayerDamageSource.Fall);
        Check(top == 120 && _host.Vitals.Health == 120 - 16 * 6 && bigHeart.Saves > 0,
            $"with Big heart at full rank the maximum is 120 and the same 1.5 m fall takes 16 quarters of 6, not 19 ({_host.Vitals.Health} left)");
    }

    private async Task TestVitalsTimeAway()
    {
        await EnergyWorld("energy-away", pips: 1, body: v => v.With(health: 50, stamina: 0), written: _now - TimeSpan.FromSeconds(60));
        Check(_host.Vitals.Health == 100 && Pips == 4 && Math.Abs(_host.Vitals.Stamina - _host.Vitals.MaxStamina) < 1e-6,
            $"a minute away: the hearts healed at the resting pace, the bar refilled, and the pool woke half full ({_host.Vitals.Health}, {Pips}, {_host.Vitals.Stamina})");
        await EnergyWorld("energy-away-short", pips: 6, body: v => v.With(health: 50), written: _now - TimeSpan.FromSeconds(12.5));
        Check(_host.Vitals.Health == 55 && Pips == 6, $"12.5 s away heals one quarter after the 10 s wait, and a fuller pool keeps what it had ({_host.Vitals.Health}, {Pips})");
        await EnergyWorld("energy-away-never", pips: 1, body: v => v.With(health: 50));
        Check(_host.Vitals.Health == 50 && Pips == 1, "a mode never written by this host has no time away to catch up");
    }

    /// <summary>A copy of the test room whose manifest places two Fractals: one in front of the player's spawn, one across the room.</summary>
    private RoomData FractalRoom()
    {
        var source = ProjectSettings.GlobalizePath(RoomWorld.DefaultRoom);
        var target = ProjectSettings.GlobalizePath($"{TestRoot}/fractals/test_room");
        if (Directory.Exists(target)) Directory.Delete(target, true);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".import", StringComparison.Ordinal)))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(target, "room.json")))!.AsObject();
        manifest["extensions"] = new JsonObject
        {
            ["x_fractals"] = new JsonObject
            {
                ["typed"] = false,
                ["items"] = new JsonArray(
                    new JsonObject { ["id"] = "fractal:near01", ["position_m"] = new JsonArray(0.0, 0.0, 0.5), ["reach_band"] = 1 },
                    new JsonObject { ["id"] = "fractal:far02", ["position_m"] = new JsonArray(1.2, 0.0, 0.6) }),
            },
        };
        File.WriteAllText(Path.Combine(target, "room.json"), manifest.ToJsonString(), new UTF8Encoding(false));
        return RoomData.Load($"{TestRoot}/fractals/test_room");
    }

    private async Task TestFractals()
    {
        // The contract's example manifest's Fractals read as the kernel reads them.
        var example = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(ProjectSettings.GlobalizePath("res://") + "../contracts/examples/rooms/island_example/room.json"));
        var parsed = CommandHost.ParseFractals(example.RootElement.GetProperty("extensions").GetProperty("x_fractals"), new Aabb(new Vector3(-16, -1, -16), new Vector3(32, 7, 16)), out var typed);
        Check(parsed is { Count: 3 } && !typed && parsed.Select(f => f.Id).SequenceEqual(new[] { "fractal:k3x9q2", "fractal:p7m2c5", "fractal:w4n8t6" }) && parsed[1].ReachBand == 2,
            "the contract's example island places three untyped Fractals, read in id order with their reach bands");
        example.Dispose();
        var room = FractalRoom();
        var store = await EnergyWorld("energy-fractals", room: room);
        var near = "fractal:near01";
        Check(_host.AllFractals.Count == 2 && _host.FractalsNotice.Length == 0, "the host reads the manifest's two Fractals: " + _host.FractalsNotice);
        var listed = Send(Query("entities.list", new JsonObject { ["filter"] = new JsonObject { ["kind"] = "fractal" } }), Player)["data"]?["items"]?.AsArray();
        var summary = listed?.FirstOrDefault(e => e?["id"]?.GetValue<string>() == near);
        Check(listed?.Count == 2 && summary?["kind"]?.GetValue<string>() == "fractal" && summary["movable"]?.GetValue<bool>() == false &&
              summary["provenance_kind"]?.GetValue<string>() == "procedural" && summary["display_name"]?.GetValue<string>() == "Fractal",
            "Fractals are entities of kind fractal: listed, small, not movable, placed by the generator: " + Canonical(summary));
        var seen = Send(Query("observe", new JsonObject { ["actor"] = CommandHost.CompanionAvatarId }), Companion)["data"]?["visible"]?.AsArray()
            .FirstOrDefault(e => e?["id"]?.GetValue<string>() == near);
        Check(seen?["kind"]?.GetValue<string>() == "fractal" && seen["seen"]?.GetValue<string>() == "now",
            "the Gubble sees a Fractal in its line of sight, kind fractal (it may find one, go to it and look at it): " + Canonical(seen));
        var grab = Send(Command(NextId("grab"), "entity.grab", new JsonObject { ["target"] = near, ["actor"] = CommandHost.CompanionAvatarId }), Companion);
        var mine = Send(Command(NextId("grab"), "entity.grab", new JsonObject { ["target"] = near, ["actor"] = CommandHost.PlayerAvatar }), Player);
        Check(Code(grab) == "permission_denied" && Code(mine) == "invalid_args" && mine["error"]?["message"]?.GetValue<string>().Contains("item.pickup", StringComparison.Ordinal) == true,
            "the Gubble never gathers one; the player's own grab says a Fractal is picked up, not carried");
        if (!_host.FractalPickupsKept)
        {
            var stub = Send(Command(NextId("pickup"), "item.pickup", new JsonObject { ["target"] = near }), Player);
            Check(Code(stub) == "unsupported_capability" && stub["error"]?["message"]?.GetValue<string>() == CommandHost.MechanicsNotYet,
                "while the room's authority does not keep a pickup's receipt (its NATURE_OPS), the pickup answers as not available yet");
            // Past this point the authority's receipt is kept for the session only (this test never reloads such a save).
            _host.FractalPickupsKept = true;
        }
        var far = Send(Command(NextId("pickup"), "item.pickup", new JsonObject { ["target"] = "fractal:far02" }), Player);
        Check(Code(far) == "out_of_bounds" && far["error"]?["retryable"]?.GetValue<bool>() == true, "a Fractal across the room is out of reach: come closer");
        var revision = _host.Revision;
        var preview = Send(Command(NextId("pickup"), "item.pickup", new JsonObject { ["target"] = near }, preview: true), Player);
        Check(Ok(preview) && _host.Revision == revision && _host.Fractals.Count() == 2, "a preview picks nothing up");
        var pickup = Command("pickup-near", "item.pickup", new JsonObject { ["target"] = near });
        var picked = Send(pickup, Player);
        Check(Ok(picked) && picked["transient"]?.GetValue<bool>() == false && picked["revision"]?.GetValue<int>() == revision + 1 && picked["data"] == null &&
              picked["affected"]?.AsArray().Select(a => a!.GetValue<string>()).SequenceEqual(new[] { near }) == true && Pips == 8,
            "the player picks it up: one durable room revision, the Fractal in affected, no data, no pips: " + Canonical(picked));
        Check(_host.Vitals.Fractals == new PlayerFractals(1, 1, 0, 0) && store.Saved.For("challenge").Fractals.Found == 1 && _host.Fractals.Count() == 1 &&
              _host.Nature.FractalsPicked.SetEquals(new[] { near }),
            "it leaves the island into this mode's count (found 1, in hand 1), saved with the vitals, and the island keeps it as picked up");
        var replay = Send(pickup, Player);
        var again = Send(Command(NextId("pickup"), "item.pickup", new JsonObject { ["target"] = near }), Player);
        var lookup = Send(Query("receipt.lookup", new JsonObject { ["action_id"] = "pickup-near" }), Player);
        Check(replay["replayed"]?.GetValue<bool>() == true && Code(again) == "target_not_found" && lookup["data"]?["found"]?.GetValue<bool>() == true && _host.Vitals.Fractals.Found == 1,
            "a retry replays the receipt, a second pickup finds nothing there, and the count moves once");
        var fact = Send(Query("journal.read", new JsonObject { ["kind"] = "found" }), Player)["data"]?["entries"]?.AsArray().LastOrDefault();
        Check(fact?["line"]?.GetValue<string>() == "You found a Fractal (1 found)." && fact["quantity"]?["count"]?.GetValue<int>() == 1,
            "the journal keeps the reward's pace line, in the game's words: " + Canonical(fact));
        var state = _host.ExportRoomState();
        Check(state?["extensions"]?["x_play_nature"]?["fractals"]?.AsArray().Select(a => a!.GetValue<string>()).SequenceEqual(new[] { near }) == true &&
              NatureBlock.Problem(state["extensions"]!["x_play_nature"], _host.Revision, room) == null,
            "room state keeps the Fractal picked up in the host's nature block, which checks whole");
        var companionList = Send(Query("entities.list", new JsonObject { ["filter"] = new JsonObject { ["kind"] = "fractal" } }), Companion)["data"]?["items"]?.AsArray();
        Check(companionList != null && companionList.All(e => e?["id"]?.GetValue<string>() != near), "a Fractal picked up is gone for the Gubble too");
        if (_host.Authority.GetScript().AsGodotObject() is Script script && script.GetScriptConstantMap().TryGetValue("NATURE_OPS", out var ops) && ops.AsGodotArray().Contains("item.pickup"))
        {
            // The authority keeps the receipt (the integrator's line): the island save reloads with the Fractal still gone.
            await EnergyWorld("energy-fractals", room: room);
            var replayed = Send(pickup, Player);
            Check(_host.Fractals.Select(f => f.Id).SequenceEqual(new[] { "fractal:far02" }) && replayed["replayed"]?.GetValue<bool>() == true,
                "after a reload the Fractal is still gone and its receipt still replays");
        }
        else GD.Print("NATURE_NOTE the authority does not keep item.pickup receipts yet: the reload check waits for its NATURE_OPS line");
    }
}
