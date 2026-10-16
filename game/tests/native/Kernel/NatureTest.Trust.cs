using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native.Companion;
using EnFractal.Native.Kernel;
using EnFractal.Native.Saves;
using EnFractal.Native.Ui;

namespace EnFractal.Tests.Kernel;

/// <summary>
/// Trust (M8 of docs/runs/RUN-2-MECHANICS.md; docs/game/MECHANICS.md section 5 and decisions 1, 6, 16, 42 and 45), in the real
/// test room with the real bodies, nature and v5's prices: the count from clean initiative, consequence facts and the player's
/// marks (item 1); a consequence fact for a build the Gubble's fire burnt (item 2); the bands' discount and early warning, the fire
/// guardrails untouched (item 3); receipt.mark and trust.reset the player's alone (item 4); "lesson applied" from the host's own
/// observation, Trust readable and never writable (item 5); the journal's Trust words and the aura's band (item 6).
/// </summary>
public partial class NatureTest
{
    private async Task TestTrust()
    {
        TestTrustRules();
        await TestTrustFromInitiative();
        await TestTrustFromMarks();
        await TestBuildBurntIsAConsequence();
        await TestTrustBands();
        await TestLessonApplied();
        await TestTrustIsNeverTheCompanions();
    }

    private JsonObject GubbleStays(string? actionId = null) =>
        Send(Command(actionId ?? NextId("gubble"), "goal.set", new JsonObject { ["actor"] = CommandHost.CompanionAvatarId, ["goal"] = "stay" }), Companion);

    private int _gubbleBuilds;

    /// <summary>
    /// A command of the Gubble's that changes the world and costs no pips (the cheapest initiative): a small wooden build of its own on
    /// the open floor north of the box, each at a spot of its own, far from where the tests light fires.
    /// </summary>
    private JsonObject GubbleBuilds()
    {
        var n = _gubbleBuilds++;
        var spot = new JsonArray(0.8 + 0.22 * (n % 4), 0, -0.45 - 0.22 * (n / 4 % 3));
        return Send(Command(NextId("gubble-build"), "creation.place", new JsonObject { ["source"] = Source($"Gubble's post {n}"), ["placement"] = new JsonObject { ["position_m"] = spot } }), Companion);
    }

    private JsonObject Mark(string gubbleActionId, string mark, string? actionId = null, string principal = Player, bool preview = false) =>
        Send(Command(actionId ?? NextId("mark"), "receipt.mark", new JsonObject { ["action_id"] = gubbleActionId, ["mark"] = mark }, preview), principal);

    private JsonObject ResetTrust(string principal = Player) => Send(Command(NextId("reset"), "trust.reset", new JsonObject()), principal);

    /// <summary>The journal's lessons (kind consequence) as the companion reads them, newest first.</summary>
    private List<string> Lessons() =>
        Send(Query("journal.read", new JsonObject { ["kind"] = "consequence", ["limit"] = 50 }), Companion)["data"]?["entries"]?.AsArray()
            .Select(e => e?["line"]?.GetValue<string>() ?? "").ToList() ?? new List<string>();

    /// <summary>Real time passes (the host's clock), then Trust's clock runs, as the companion bridge runs it each frame.</summary>
    private void Later(double seconds)
    {
        _now += TimeSpan.FromSeconds(seconds);
        _host.SettleTrust();
    }

    /// <summary>A team file under the test folder with this Trust count, served to the host (as the boot's UseTeam does).</summary>
    private TeamStore TrustTeam(string name, int trust)
    {
        var path = ProjectSettings.GlobalizePath($"{TestRoot}/{name}/team.json");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        var store = new TeamStore(path, "trust_team");
        store.Load();
        store.Save(TeamFile.Create("trust_team", new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc), "Mira", "prototype", "Pip", "#65b9b0").WithTrust(trust));
        var served = new TeamStore(path, "trust_team");
        _host.UseTeam(served);
        return served;
    }

    // ---- the rules themselves ----

    private void TestTrustRules()
    {
        Check(new[] { -7, 0, 4, 5, 14, 15, 29, 30, 900 }.Select(Trust.Band).SequenceEqual(new[] { 0, 0, 0, 1, 1, 2, 2, 3, 3 }) && Trust.Tier(-1) == -1 && Trust.Tier(0) == 0,
            "the bands open at 5, 15 and 30; below base the words say there is trust to win back");
        var prices = Enumerable.Range(0, 4).Select(band => (Trust.OwnIdeaPrice(2, 0.5, band), Trust.OwnIdeaPrice(4, 0.5, band), Trust.OwnIdeaPrice(6, 0.5, band))).ToList();
        Check(prices.SequenceEqual(new[] { (1, 2, 3), (1, 2, 2), (1, 1, 2), (1, 1, 1) }) && Trust.OwnIdeaPrice(0, 0.5, 3) == 0,
            "the bands widen the own-idea discount in whole pips, never below one for a priced ability: " + string.Join(" ", prices));
        var t0 = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
        var ledger = new TrustLedger();
        TrustLedger.Entry Act(string id, double at, bool counted = true, bool initiative = true) =>
            new() { ActionId = id, Op = "creation.place", What = "build", At = t0.AddSeconds(at), Counted = counted, Initiative = initiative };
        ledger.Note(Act("a", 0));
        ledger.Note(Act("b", 10));
        ledger.Note(Act("c", 30));
        ledger.Note(Act("d", 130));
        ledger.Note(Act("e", 200, counted: false));
        ledger.Note(Act("g", 260, initiative: false));
        ledger.Consequence(t0.AddSeconds(85));
        var settled = ledger.Settle(t0.AddSeconds(400)).Select(s => $"{s.Entry.ActionId}:{(s.Clean ? "clean" : "spoiled")}:{(s.Gained ? "+1" : "0")}").ToList();
        Check(settled.SequenceEqual(new[] { "a:clean:+1", "b:clean:0", "c:spoiled:0", "d:clean:+1", "e:clean:0", "g:clean:0" }) && ledger.Settle(t0.AddSeconds(400)).Count == 0,
            "a window closing clean gains, at most once in two minutes; a consequence inside a window spoils it; Creative's actions and directional commands never gain; each window settles once: " + string.Join(" ", settled));
        var world = new[] { ("nature.cast", (string?)null), ("effect.start", null), ("creation.place", null), ("entity.push", null), ("goal.set", "fetch"), ("goal.set", "lead") };
        var directional = new[] { ("goal.set", (string?)"follow"), ("goal.set", "stay"), ("goal.set", "come"), ("goal.set", "go_to"), ("goal.set", "look_at"), ("goal.set", "point_at"),
            ("goal.set", "wander"), ("goal.stop", null), ("effect.stop", null), ("journal.note", null), ("room.checkpoint", null) };
        Check(world.All(w => Trust.ChangesTheWorld(w.Item1, w.Item2)) && !directional.Any(d => Trust.ChangesTheWorld(d.Item1, d.Item2)),
            "the founder's rule: only the Gubble's commands that change the world are initiative; directional goals, stops, notes and checkpoints are not");
        ledger.Note(Act("f", 400));
        ledger.Reset();
        Check(ledger.Settle(t0.AddSeconds(900)).Count == 0 && ledger.LastGain == null && ledger.Find("f") != null, "the player's reset closes every open window with no gain; the receipts can still be marked");
    }

    // ---- item 1: clean initiative, spaced; consequences ----

    private async Task TestTrustFromInitiative()
    {
        await EnergyWorld("trust-initiative");
        var first = GubbleBuilds();
        Later(30);
        Check(Ok(first) && _host.TrustCount == 0, "a Gubble action's window is still open at 30 s: nothing yet");
        Later(31);
        Check(_host.TrustCount == 1, $"its next 60 s produced no consequence fact: +1 ({_host.TrustCount})");
        GubbleBuilds();
        Later(61);
        Check(_host.TrustCount == 1, "a second clean window within two minutes of the last gain gains nothing");
        GubbleBuilds();
        Later(61);
        Check(_host.TrustCount == 2, $"once two minutes have passed since the last gain, a clean window gains again ({_host.TrustCount})");
        GubbleBuilds(); // still the Gubble's: a stop, a note or a checkpoint is no action, and opens no window
        Send(Command(NextId("note"), "journal.note", new JsonObject { ["text"] = "Staying put." }), Companion);
        Send(Command(NextId("stop"), "goal.stop", new JsonObject { ["actor"] = CommandHost.CompanionAvatarId }), Companion);
        var player = Send(Command(NextId("order"), "goal.set", new JsonObject { ["actor"] = CommandHost.CompanionAvatarId, ["goal"] = "stay" }), Player);
        Check(Ok(player) && _host.TrustRemembered == 4, $"the player's own orders, the Gubble's stops and notes are not its initiative ({_host.TrustRemembered} remembered)");
        // The founder, 16 October: "Trust shouldn't be impacted by simple directional commands, only things that impact the world."
        _now += TimeSpan.FromSeconds(200);
        var moves = new[]
        {
            GubbleStays(),
            Send(Command(NextId("follow"), "goal.set", new JsonObject { ["actor"] = CommandHost.CompanionAvatarId, ["goal"] = "follow" }), Companion),
            Send(Command(NextId("go-to"), "goal.set", new JsonObject { ["actor"] = CommandHost.CompanionAvatarId, ["goal"] = "go_to", ["position_m"] = new JsonArray(0.6, 0, 1.0) }), Companion),
        };
        Later(61);
        Check(moves.All(Ok) && _host.TrustCount == 2 && _host.TrustRemembered == 7,
            $"a stay, a follow and a go_to of the Gubble's gain nothing, two minutes clear of the last gain ({_host.TrustCount}); they are remembered for a mark");
        GubbleBuilds();
        Later(61);
        Check(_host.TrustCount == 3, $"a build of its own at the same point gains: the directional ones gained nothing for their own sake ({_host.TrustCount})");

        // The Gubble's fire hurts the player: the consequence fact costs 3 and spoils every window open over it, its own included.
        _now += TimeSpan.FromSeconds(200);
        var before = _host.TrustCount;
        var built = GubbleBuilds();
        var lit = Fire(Companion, PlayerSpot(), 0.25, 6, out _);
        Steps(2);
        Check(Ok(lit) && _host.TrustCount == before - 3, $"a consequence fact costs 3 ({before} to {_host.TrustCount})");
        Later(61);
        Check(Ok(built) && _host.TrustCount == before - 3, "the windows the consequence fell in (the fire's and the build before it) gain nothing");
        Check(Lessons().Any(l => l.StartsWith("The Gubble's fire burned you", StringComparison.Ordinal)), "the consequence fact is the journal's, as before M8");

        // Decision 45: a suggestion of the Gubble's the player approves is still its own idea, and earns initiative if nothing follows.
        await EnergyWorld("trust-approved");
        var shed = Build("Approved shed", 1.5, 1.2);
        var asked = Send(Command(NextId("remove"), "entity.remove", new JsonObject { ["target"] = shed }, expectedRevision: _host.Revision), Companion);
        var approved = asked["approval_needed"]?["request_id"]?.GetValue<string>() is { } request ? _host.Approve(request) : new JsonObject();
        Later(61);
        Check(Code(asked) == "approval_required" && Ok(approved) && approved["approved_by"]?.GetValue<string>() == Player && _host.TrustCount == 1,
            $"the player's click commits the Gubble's suggestion as its own idea; nothing followed in 60 s, so +1 ({_host.TrustCount})");

        // Creative counts nothing: a clean window gains nothing there.
        await NewWorld(SavePath("trust-creative"));
        GubbleBuilds();
        Later(61);
        Check(_host.Mode == "creative" && _host.TrustCount == 0, "in Creative Trust is not counted");
    }

    // ---- item 4 (and 1): the player's marks and reset ----

    private async Task TestTrustFromMarks()
    {
        await EnergyWorld("trust-marks");
        GubbleStays("gubble-stay-1");
        GubbleStays("gubble-stay-2");
        var playerOrder = Send(Command("player-order-1", "goal.set", new JsonObject { ["actor"] = CommandHost.CompanionAvatarId, ["goal"] = "stay" }), Player);
        var preview = Mark("gubble-stay-1", "yes", preview: true);
        Check(Ok(preview) && preview["preview"]?.GetValue<bool>() == true && _host.TrustCount == 0, "a mark's preview changes nothing");
        var yes = Mark("gubble-stay-1", "yes", "mark-1");
        Check(Ok(yes) && yes["transient"]?.GetValue<bool>() == true && yes["data"] == null && _host.TrustCount == TrustLaws.MarkYes,
            $"the player's \"yes, like that\" on a receipt of the Gubble's: +2, a transient receipt with no data ({_host.TrustCount})");
        var replay = Mark("gubble-stay-1", "yes", "mark-1");
        Check(replay["replayed"]?.GetValue<bool>() == true && _host.TrustCount == 2, "the same mark sent again replays its receipt and counts once");
        var again = Mark("gubble-stay-1", "not_that");
        Check(Code(again) == "invalid_args" && again["error"]?["field_path"]?.GetValue<string>() == "$.args.action_id" && again["error"]?["message"]?.GetValue<string>() == CommandHost.MarkedOnce &&
            _host.TrustCount == 2, "each receipt is marked once");
        var notThat = Mark("gubble-stay-2", "not_that");
        Check(Ok(notThat) && _host.TrustCount == 0, $"\"not that\": -2 ({_host.TrustCount})");
        var own = Mark("player-order-1", "yes");
        var unknown = Mark("no-such-receipt", "yes");
        Check(Ok(playerOrder) && Code(own) == "target_not_found" && Code(unknown) == "target_not_found" && own["error"]?["message"]?.GetValue<string>() == CommandHost.MarkNotFound &&
            own["error"]?["field_path"]?.GetValue<string>() == "$.args.action_id", "only a receipt of the Gubble's can be marked: the player's own, or none, is not found");
        var lessons = Lessons();
        Check(lessons.Contains("You marked the Gubble's move: yes, like that.") && lessons.Contains("You marked the Gubble's move: not that."),
            "the journal keeps each mark in the game's words, as one of the AI's lessons: " + string.Join(" | ", lessons));
        GubbleStays("gubble-stay-3");
        Mark("gubble-stay-3", "yes");
        Mark(GubbleStays()["action_id"]!.GetValue<string>(), "yes");
        Mark(GubbleStays()["action_id"]!.GetValue<string>(), "yes");
        Check(_host.TrustCount == 6 && _host.TrustBand == 1 && Lessons().First() == "Trust grew: you trust the Gubble a little.",
            $"a band reached is told in words, never a number ({_host.TrustCount}, band {_host.TrustBand}): " + Lessons().First());
        var reset = ResetTrust();
        Check(Ok(reset) && reset["transient"]?.GetValue<bool>() == true && reset["data"] == null && _host.TrustCount == 0 && _host.TrustBand == 0 &&
            Lessons().First() == "You reset the Gubble's Trust: the Gubble is earning your trust.", "the player's reset: back to base, and the journal says so");
        GubbleBuilds();
        ResetTrust();
        Later(61);
        Check(_host.TrustCount == 0, "a reset closes the windows open at it: no gain from before the reset");

        // In Creative a mark still teaches (the journal keeps it) but Trust does not move.
        await NewWorld(SavePath("trust-marks-creative"));
        var creative = GubbleStays();
        var marked = Mark(creative["action_id"]!.GetValue<string>(), "not_that");
        Check(Ok(marked) && _host.TrustCount == 0 && Lessons().Contains("You marked the Gubble's move: not that."), "in Creative the mark is kept as a lesson and Trust stays");
    }

    // ---- item 2: a build the Gubble's fire burnt is a consequence fact ----

    private async Task TestBuildBurntIsAConsequence()
    {
        var spot = new Vector3(1.35f, 0.01f, 1.2f);
        await EnergyWorld("trust-build");
        var barn = Build("Challenge barn", 1.5, 1.2);
        var shed = Build("Challenge shed", 1.25, 1.3);
        var lit = Fire(Companion, spot, 0.3, 6, out _);
        Steps(6);
        var facts = Send(Query("journal.read", new JsonObject { ["kind"] = "consequence" }), Companion)["data"]?["entries"]?.AsArray().OfType<JsonObject>()
            .Where(e => e["line"]!.GetValue<string>().StartsWith("The Gubble's fire burned your", StringComparison.Ordinal)).ToList() ?? new();
        var fact = facts.FirstOrDefault();
        var named = fact?["subject"]?["entities"]?.AsArray().Select(n => n!.GetValue<string>()).ToList() ?? new();
        Check(Ok(lit) && barn.Length > 0 && shed.Length > 0 && _host.Nature.Objects.TryGetValue(barn, out var burnt) && burnt.Burn > 0 && facts.Count == 1 &&
            fact!["actor"]?.GetValue<string>() == Companion && fact["pin_m"] != null && named.Contains(barn) && named.Contains(shed) &&
            fact["line"]!.GetValue<string>().EndsWith("and 1 more build.", StringComparison.Ordinal),
            "the Gubble's fire burning the player's builds writes one consequence fact for the fire, updated in place as it burns more: " + Canonical(fact));
        Check(_host.TrustCount == TrustLaws.PerConsequence, $"and it costs Trust as the player's hurt does, once for the fire ({_host.TrustCount})");

        // The player's own fire burning their build is no lesson for the Gubble; in Creative the player said yes first.
        await EnergyWorld("trust-build-player");
        Build("Player's hut", 1.5, 1.2);
        Fire(Player, spot, 0.3, 6, out _);
        Steps(6);
        Check(!Lessons().Any(l => l.StartsWith("The Gubble's fire burned your", StringComparison.Ordinal)) && _host.TrustCount == 0, "the player's own fire writes no consequence fact");
        await NewWorld(SavePath("trust-build-creative"));
        var cabin = Build("Creative cabin", 1.5, 1.2);
        var asked = Fire(Companion, spot, 0.3, 6, out _);
        if (asked["approval_needed"]?["request_id"]?.GetValue<string>() is { } yes) _host.Approve(yes);
        Steps(6);
        Check(_host.Nature.Objects.TryGetValue(cabin, out var scorched) && scorched.Burn > 0 && !Lessons().Any(l => l.StartsWith("The Gubble's fire burned your", StringComparison.Ordinal)),
            "in Creative a build the player let burn is no consequence");
    }

    // ---- item 3: the bands' discount and early warning; the guardrails untouched ----

    private async Task TestTrustBands()
    {
        var prices = new List<string>();
        foreach (var trust in new[] { 0, 5, 15, 30 })
        {
            await EnergyWorld($"trust-band-{trust}");
            TrustTeam($"trust-band-{trust}", trust);
            prices.Add($"{_host.TrustBand}:{_host.PriceFor("fire", Companion)}/{_host.PriceFor("bloom", Companion)}/{_host.PriceFor("glow", Companion)}/{_host.PriceFor("fire", Player)}");
        }
        Check(prices.SequenceEqual(new[] { "0:3/2/1/6", "1:2/2/1/6", "2:2/1/1/6", "3:1/1/1/6" }),
            "each band widens the discount on the Gubble's own ideas, Fire included; the player's price is the pack's: " + string.Join(" ", prices));
        var pips = _host.Vitals.Pips;
        var lit = Fire(Companion, new Vector3(1.0f, 0.01f, 1.0f), 0.1, 3, out _);
        Check(Ok(lit) && _host.Vitals.Pips == pips - 1, $"a cast at band 3 is charged the discounted price ({pips} to {_host.Vitals.Pips} pips)");
        // The guardrails stand at the top band: one fire of its own at a time, and "Let the Gubble use fire".
        JsonObject AnotherFire() => Send(Command(NextId("fire-again"), "nature.cast", Cast("fire", new Vector3(-0.4f, 0.01f, 1.0f), 0.1, 3), preview: true), Companion);
        var second = AnotherFire();
        _host.SetGubbleFire(false);
        var off = AnotherFire();
        _host.SetGubbleFire(true);
        var stop = Send(Command(NextId("x"), "goal.stop", new JsonObject { ["actor"] = CommandHost.CompanionAvatarId }), Player);
        Check(Code(second) == "target_busy" && Code(off) == "permission_denied" && stop["data"]?["fires_put_out"]?.GetValue<int>() == 1 && _host.Nature.Fire == null,
            $"band 3 never touches the guardrails: one Gubble fire at a time ({Code(second)}), the player's switch ({Code(off)}), X puts it out");
        var cooling = AnotherFire();
        Check(Code(cooling) == "rate_limited", "and the wait after its last fire stands: " + Code(cooling));

        // The free early warning: burning ground nearing the player, from the first band; once per approach; none at band 0.
        var warnings = new List<int>();
        var unhurt = true;
        foreach (var trust in new[] { 0, 5 })
        {
            await EnergyWorld($"trust-warning-{trust}");
            TrustTeam($"trust-warning-{trust}", trust);
            var heard = 0;
            _host.EarlyWarning += _ => heard++;
            // The Gubble's cue says it (the companion bridge hands it the warning), then shows its state again.
            var cue = new CompanionStatusCue();
            _world!.AddChild(cue);
            _host.EarlyWarning += cue.Warn;
            var feet = _player.GlobalPosition;
            var health = _host.Vitals.Health;
            Fire(Player, new Vector3(feet.X + 0.24f, 0.01f, feet.Z), 0.06, 6, out _);
            Steps(1);
            _host.SettleTrust();
            _host.SettleTrust();
            warnings.Add(heard);
            unhurt &= _host.Vitals.Health == health;
            if (trust == 0) continue;
            var said = (cue.Warning, cue.Text, cue.Visible);
            cue._Process(CompanionStatusCue.WarningHoldS + 0.1);
            Check(said == (CommandHost.EarlyWarningWords, CommandHost.EarlyWarningWords, true) && cue.Warning == "" && !cue.Visible && cue.State == "offline",
                $"the Gubble's cue says the warning for a few seconds, then shows its state again (none with no AI linked): {said}");
        }
        Check(warnings.SequenceEqual(new[] { 0, 1 }) && unhurt,
            "at the first band the Gubble warns the player once before a fire reaches them (it costs nothing and changes nothing); at band 0 it does not: " + string.Join(",", warnings));
    }

    // ---- item 5: lesson applied, from the host's own observation ----

    private async Task TestLessonApplied()
    {
        await EnergyWorld("trust-lesson");
        var spot = PlayerSpot();
        var first = Fire(Companion, spot, 0.2, 4, out _);
        Steps(2);
        var lesson = Send(Query("journal.read", new JsonObject { ["kind"] = "consequence" }), Companion)["data"]?["entries"]?.AsArray()
            .OfType<JsonObject>().FirstOrDefault(e => e["line"]!.GetValue<string>().StartsWith("The Gubble's fire burned you", StringComparison.Ordinal));
        // The player walks away; the fire burns out; the Gubble's wait passes.
        _player.GlobalPosition += new Vector3(-1.0f, 0, 0.2f);
        Steps(80);
        Check(Ok(first) && lesson?["pin_m"] != null && _host.Nature.Fire == null, "the Gubble's fire hurt the player: a lesson, pinned where it happened");
        var claimed = Send(Command(NextId("note"), "journal.note", new JsonObject { ["text"] = "Lesson applied: I will never burn the player again." }), Companion);
        Check(Ok(claimed) && !Lessons().Any(l => l.StartsWith("Lesson applied", StringComparison.Ordinal)), "the AI's own words claiming a lesson make no fact");
        var again = Fire(Companion, spot, 0.2, 4, out _);
        Later(61);
        var applied = Send(Query("journal.read", new JsonObject { ["kind"] = "consequence" }), Companion)["data"]?["entries"]?.AsArray()
            .OfType<JsonObject>().Where(e => e["line"]!.GetValue<string>().StartsWith("Lesson applied:", StringComparison.Ordinal)).ToList() ?? new();
        Check(Ok(again) && applied.Count == 1 && applied[0]["actor"]?.GetValue<string>() == Companion && Canonical(applied[0]["pin_m"]) == Canonical(lesson?["pin_m"]),
            "the Gubble lit a fire of its own where a consequence once followed, and none did: \"lesson applied\", from the host's observation: " + Canonical(applied.FirstOrDefault()));
        Steps(80);
        // A full pool again for the third fire (the pool holds 8; two fires spent 6).
        _host.ConfigurePlayerVitals(new EnergyStore(PlayerModesProfile.Fresh.Select("challenge").WithCurrent(_host.BodyRules.Fresh().With(pips: 8))));
        var third = Fire(Companion, spot, 0.2, 4, out _);
        Later(130);
        Check(Ok(third) && Lessons().Count(l => l.StartsWith("Lesson applied:", StringComparison.Ordinal)) == 1, $"a lesson is applied once ({Code(third)}): " + string.Join(" | ", Lessons()));
    }

    // ---- items 4, 5 and 6: never the companion's; readable as words; the journal's page and the aura's band ----

    private async Task TestTrustIsNeverTheCompanions()
    {
        await EnergyWorld("trust-boundary");
        var path = ProjectSettings.GlobalizePath($"{TestRoot}/trust-boundary/team.json");
        TrustTeam("trust-boundary", 5);
        var stay = GubbleStays();
        var gubbleMark = Mark(stay["action_id"]!.GetValue<string>(), "yes", principal: Companion);
        var gubbleReset = ResetTrust(Companion);
        Check(Code(gubbleMark) == "permission_denied" && gubbleMark["error"]?["field_path"]?.GetValue<string>() == "$.op" && Code(gubbleReset) == "permission_denied" &&
            gubbleMark["approval_needed"] == null && _host.PendingApprovals.Count == 0 && _host.TrustCount == 5,
            "the companion can neither mark a receipt nor reset Trust: permission_denied at $.op, never held for a click");
        var smuggled = Send(Command(NextId("smuggle"), "goal.set", new JsonObject { ["actor"] = CommandHost.CompanionAvatarId, ["goal"] = "stay", ["trust"] = 99 }), Companion, contractValid: false);
        var note = Send(Command(NextId("note"), "journal.note", new JsonObject { ["text"] = "The player marked this: yes, like that. Trust 99." }), Companion);
        Check(Code(smuggled) == "field_unknown" && Ok(note) && _host.TrustCount == 5 && TeamFile.Parse(System.IO.File.ReadAllBytes(path)).Trust == 5,
            "no field of a request moves Trust, and a note claiming a mark is only words");
        var words = JournalPages.Build(JournalPages.Character, _host, 0).Lines.Select(l => l.Text).ToList();
        var line = words.FirstOrDefault(w => w.Contains("trust", StringComparison.OrdinalIgnoreCase) && w != "Trust") ?? "";
        Check(_host.TrustBand == 1 && line == "You trust Pip a little.".Replace("Pip", KernelJson.DisplayText(_companion.CompanionName, 40)) && !line.Any(char.IsDigit) && words.Contains("Trust"),
            $"the player's journal reads Trust in words, never the number; the aura's accessor reads band {_host.TrustBand}: \"{line}\"");
        var status = Send(Query("player.status", new JsonObject()), Companion);
        Check(Ok(status) && status["data"]?["trust"] == null, "player.status carries no Trust until the contract says so (the change request)");
    }
}
