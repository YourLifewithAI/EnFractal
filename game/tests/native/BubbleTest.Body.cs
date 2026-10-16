using Godot;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;

namespace EnFractal.Tests;

/// <summary>
/// The body and the bubble: the ride, the deflate (no harm, no tumble, a slow drift down wherever a bubble pops in the air), the lift's
/// rise, the Gubble leading a bubble, the step-in, the boat on shallow water, the security row's tests by name, and the budgets.
/// </summary>
public partial class BubbleTest
{
    private static float LevelGap(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>
    /// Frames until the body stands (or the water has it) and no bubble moves it, at most maxFrames; the largest landing speed it
    /// reported on the way (a fall with no deflate reports its speed here, and hurts).
    /// </summary>
    private async Task<float> UntilDown(int maxFrames = 900)
    {
        var landing = 0f;
        for (var i = 0; i < maxFrames && (_player.Ride != SmallPlayerController.BubbleRide.None || !(_player.IsOnFloor() || _player.IsSwimming)); i++)
        {
            await Frames(1);
            if (_player.LastBodyState.Landed) landing = Math.Max(landing, _player.LastBodyState.LandingSpeedMps);
        }
        await Frames(3);
        if (_player.LastBodyState.Landed) landing = Math.Max(landing, _player.LastBodyState.LandingSpeedMps);
        return landing;
    }

    // ---- the ride and the deflate ----

    private async Task TestRideAndDeflate()
    {
        await NewWorld("ride", mode: "challenge");
        var start = _player.GlobalPosition;
        var health = Health;
        var carry = CarryOnPlayer(Companion, out _);
        await Frames(45);
        var raised = _player.GlobalPosition.Y - start.Y;
        Check(Ok(carry) && _player.Ride == SmallPlayerController.BubbleRide.Enveloped && raised >= BubbleLaws.CarryHoverM * 0.6f && raised < 0.2f,
            $"the carry lifts the body off the floor and holds it ({raised * 100:0.0} cm up, {_player.Ride})");
        _player.SetDiveInput(true, false);
        await Frames(2);
        var poppedRide = _player.Ride;
        _player.SetDiveInput(false, false);
        Check(!_host.PlayerInBubble && _player.BubblePops == 1 && poppedRide is SmallPlayerController.BubbleRide.Deflating or SmallPlayerController.BubbleRide.None &&
              ReceiptData(Companion, carry)?["ended"]?.GetValue<string>() == "popped_by_player",
            $"the dive key pops the Gubble's carry at once, popped by the player ({poppedRide}): " + Canonical(ReceiptData(Companion, carry)));
        var landing = await UntilDown();
        Check(_player.Ride == SmallPlayerController.BubbleRide.None && _player.IsOnFloor() && Health == health && landing <= 0.01f,
            $"the body drifts down and stands, unhurt ({health} to {Health}), reporting no landing speed ({landing:0.000} m/s)");
        Check(_learned.Contains("bubble_carry") && _host.GlowProgress.Has(ExpertiseLists.Bubbles, ExpertiseLists.BubbleCarry), "a carry that set the player down safely is a carry learned, kept in the team's expertise store (the seam, wired at the M9 and M11 merge): " + string.Join(",", _learned));
        // Put down somewhere else while it holds them (a recovery, a teleport), the body has left the bubble: it pops and never drags them back.
        Wait(BubbleLaws.PopRespectS);
        var again = CarryOnPlayer(Companion, out _);
        await Frames(30);
        var elsewhere = start + new Vector3(0.5f, 0, -0.3f);
        var teleported = _player.TryTeleportTo(elsewhere);
        await Frames(30);
        Check(Ok(again) && teleported && LevelGap(_player.GlobalPosition, elsewhere) < 0.02f && !_host.PlayerInBubble &&
              ReceiptData(Companion, again)?["ended"]?.GetValue<string>() == "popped",
            $"a body put down elsewhere has left its bubble: it pops (popped) and the body stays where it was put ({LevelGap(_player.GlobalPosition, elsewhere):0.000} m)");
    }

    // ---- the lift, the deflate from high up, and the Gubble's steering ----

    private async Task TestLiftAndSteering()
    {
        await NewWorld("lift", mode: "challenge");
        var start = _player.GlobalPosition;
        var health = Health;
        var lift = CarryOnPlayer(Companion, out _, "bubble_lift", duration: 60);
        var risenAfter = 0;
        for (var i = 0; i < 900 && !_learned.Contains("bubble_lift"); i++)
        {
            await Frames(1);
            risenAfter = i;
        }
        await Frames(20);
        var rise = _player.GlobalPosition.Y - start.Y;
        Check(Ok(lift) && _learned.Contains("bubble_lift") && Math.Abs(rise - BubbleLaws.LiftRiseM) < 0.12f && _player.Ride == SmallPlayerController.BubbleRide.Enveloped,
            $"the lift rises the body its whole rise ({rise:0.00} m of {BubbleLaws.LiftRiseM} m, in {risenAfter / 60.0:0.0} s) and that is a lift learned: {string.Join(",", _learned)}");
        // The Gubble leads the bubble like a balloon on a string; the body goes only where its bubble goes.
        var before = _player.GlobalPosition;
        GubbleAt(start + new Vector3(-1.0f, 0, -0.2f));
        await Frames(240);
        var led = LevelGap(_player.GlobalPosition, before);
        var stillAtHeight = _player.GlobalPosition.Y - start.Y;
        Check(led > 0.5f && LevelGap(_player.GlobalPosition, _companion.GlobalPosition) < BubbleLaws.LeashM + 0.15f && stillAtHeight > 1.0f && _player.Ride == SmallPlayerController.BubbleRide.Enveloped,
            $"the Gubble moving away leads the lift after it ({led:0.00} m), the body inside it, still high ({stillAtHeight:0.00} m)");
        // The dive key pops it even while a tumble holds control; the pop in the air deflates into a slow drift down.
        _player.HoldControl(2f);
        _player.SetDiveInput(true, false);
        await Frames(2);
        _player.SetDiveInput(false, false);
        var top = _player.GlobalPosition.Y;
        await Frames(60);
        var fell = top - _player.GlobalPosition.Y;
        Check(_player.Ride == SmallPlayerController.BubbleRide.Deflating && Math.Abs(fell - BubbleLaws.DeflateDescentMps) < 0.03f && ReceiptData(Companion, lift)?["ended"]?.GetValue<string>() == "popped_by_player",
            $"popped in the air (control held by a tumble, the key still works), the body drifts down at the deflate's pace ({fell:0.000} m in 1 s, {_player.Ride})");
        // With no bubble, the Gubble's moves no longer move the body.
        var drifting = _player.GlobalPosition;
        GubbleAt(start + new Vector3(0.4f, 0, 0.6f));
        await Frames(30);
        Check(LevelGap(_player.GlobalPosition, drifting) < 0.02f, $"after the pop the Gubble's moves leave the body alone ({LevelGap(_player.GlobalPosition, drifting):0.000} m)");
        var landing = await UntilDown();
        Check(_player.Ride == SmallPlayerController.BubbleRide.None && Health == health && landing <= 0.01f,
            $"from {BubbleLaws.LiftRiseM} m up the deflate lands softly: no harm ({health} to {Health}), no landing speed ({landing:0.000} m/s), so no tumble");
        // A lift whose time runs out in the air deflates the same.
        Wait(BubbleLaws.PopRespectS);
        await PlayerAt(start);
        var timed = CarryOnPlayer(Companion, out _, "bubble_lift", duration: 4);
        await Frames(60 * 4 + 10);
        var whenTimed = _player.GlobalPosition.Y - start.Y;
        var timedRide = _player.Ride;
        landing = await UntilDown();
        Check(Ok(timed) && whenTimed > 0.3f && timedRide == SmallPlayerController.BubbleRide.Deflating && ReceiptData(Companion, timed)?["ended"]?.GetValue<string>() == "timed_out" &&
              Health == health && landing <= 0.01f,
            $"a lift timed out {whenTimed:0.00} m up deflates ({timedRide}) and lands softly ({landing:0.000} m/s, health {Health})");
    }

    // ---- a deflate over deep water ----

    private async Task TestDeflateIntoWater()
    {
        await NewWorld("deflate-water", mode: "challenge");
        var health = Health;
        var lift = CarryOnPlayer(Companion, out _, "bubble_lift", duration: 60);
        await Frames(300);
        var pool = new Vector3((PoolMin.X + PoolMax.X) / 2, 0, (PoolMin.Y + PoolMax.Y) / 2);
        GubbleAt(pool);
        for (var i = 0; i < 600 && LevelGap(_player.GlobalPosition, pool) > BubbleLaws.LeashM + 0.05f; i++) await Frames(1);
        var over = LevelGap(_player.GlobalPosition, pool);
        var height = _player.GlobalPosition.Y - PoolY;
        _host.PlayerGoal("stop");
        var landing = await UntilDown();
        await Frames(30);
        Check(Ok(lift) && over < 0.3f && height > 0.5f && _player.IsSwimming && Health == health && landing <= 0.01f,
            $"a lift popped {height:0.00} m over deep water deflates into it and the body just swims (swimming {_player.IsSwimming}, " +
            $"landing {landing:0.000} m/s, health {health} to {Health})");
    }

    // ---- the step-in ----

    private async Task TestStepIn()
    {
        await NewWorld("step-in");
        var spot = _player.GlobalPosition + new Vector3(0.25f, 0, 0.15f);
        var waiting = AtPoint(Companion, "bubble_carry", spot);
        var at = _host.ActiveBubbles.Single().Center;
        GubbleAt(_player.GlobalPosition + new Vector3(-0.6f, 0, -0.3f));
        await Frames(90);
        Check(Ok(waiting) && !_host.PlayerInBubble && _host.ActiveBubbles.Single().Center.DistanceTo(at) < 0.001f && _player.Ride == SmallPlayerController.BubbleRide.None,
            "a bubble at a point waits where it was cast, never drifting toward the player or after the Gubble, and holds no one");
        await PlayerAt(spot);
        await Frames(10);
        Check(_host.PlayerInBubble && _player.Ride == SmallPlayerController.BubbleRide.Enveloped && ReceiptData(Companion, waiting)?["carried"]?.GetValue<bool>() == true,
            "the player stepping in is the consent: it holds them, and its receipt says carried: " + Canonical(ReceiptData(Companion, waiting)));
        await Frames(60);
        Check(LevelGap(_player.GlobalPosition, _companion.GlobalPosition) < LevelGap(spot, _companion.GlobalPosition) - 0.2f,
            "once it holds the player, the Gubble leads it");
        _player.SetDiveInput(true, false);
        await Frames(2);
        _player.SetDiveInput(false, false);
        await UntilDown();
        Check(!_host.PlayerInBubble && ReceiptData(Companion, waiting)?["ended"]?.GetValue<string>() == "popped_by_player", "and the dive key pops it");
    }

    // ---- the boat ----

    private async Task TestBoat()
    {
        await NewWorld("boat");
        var wading = new Vector3(-0.5f, 0, 0.6f);
        await PlayerAt(wading);
        var deck = new Vector3(-0.85f, WaterY, 0.6f);
        var onThePlayer = AtPoint(Companion, "bubble_boat", wading with { Y = WaterY }, radius: 0.12, duration: 60);
        var boat = AtPoint(Companion, "bubble_boat", deck, radius: 0.12, duration: 60);
        Check(Code(onThePlayer) == "invalid_args" && Path(onThePlayer) == "$.args.center_m" && Ok(boat) && boat["data"]?["carried"]?.GetValue<bool>() == false &&
              boat["data"]?["target"] == null && Math.Abs(_host.ActiveBubbles.Single().Center.Y - (WaterY + BubbleLaws.BoatDeckAboveWaterM)) < 0.005f,
            "a boat is cast on shallow water beside the player, never under them, and floats at the surface: " + Canonical(boat["error"] ?? boat["data"]));
        // The Gubble (east, on the floor) leads the empty boat along the water, and it stops short of the player between them.
        await Frames(240);
        var empty = _host.ActiveBubbles.Single();
        Check(!empty.Holding && empty.Center.X > deck.X + 0.05f && LevelGap(empty.Center, _player.GlobalPosition) > 0.12f + BubbleLaws.StepInClearanceM,
            $"the Gubble nudges the empty boat toward it ({empty.Center.X - deck.X:0.00} m), and it never slides under the player ({LevelGap(empty.Center, _player.GlobalPosition):0.00} m away)");
        // Stepping aboard is the consent.
        await PlayerAt(empty.Center with { Y = 0 });
        await Frames(10);
        Check(_host.PlayerInBubble && _player.Ride == SmallPlayerController.BubbleRide.Aboard && Math.Abs(_player.GlobalPosition.Y - empty.Center.Y) < 0.01f &&
              _learned.Count(c => c == "bubble_boat") == 1 && ReceiptData(Companion, boat)?["carried"]?.GetValue<bool>() == true,
            $"the player steps aboard: the deck holds them ({_player.GlobalPosition.Y:0.000} m) and the first step aboard is a boat learned: {string.Join(",", _learned)}");
        // The Gubble directs the occupied boat; the player rides it.
        var aboard = _player.GlobalPosition;
        GubbleAt(new Vector3(-1.2f, 0, 1.15f));
        await Frames(180);
        Check(_player.Ride == SmallPlayerController.BubbleRide.Aboard && LevelGap(_player.GlobalPosition, aboard) > 0.2f,
            $"the Gubble directs the occupied boat and the player rides it ({LevelGap(_player.GlobalPosition, aboard):0.00} m)");
        // The dive key leaves it at once: the boat floats on, and takes the player again only once they have stepped off it.
        _player.SetDiveInput(true, false);
        await Frames(3);
        _player.SetDiveInput(false, false);
        await Frames(30);
        Check(!_host.PlayerInBubble && _host.ActiveBubbles.Count == 1 && _player.Ride == SmallPlayerController.BubbleRide.None && ReceiptData(Companion, boat)?["ended"] == null,
            $"the dive key leaves the boat; the boat floats on, and standing inside its rim does not board it again ({_player.Ride})");
        var floating = _host.ActiveBubbles.Single().Center;
        GubbleAt(floating + new Vector3(0, 0, 0.1f));
        await PlayerAt(floating + new Vector3(0.12f + 0.08f, -floating.Y, 0));
        await PlayerAt(floating with { Y = 0 });
        await Frames(6);
        Check(_host.PlayerInBubble && _player.Ride == SmallPlayerController.BubbleRide.Aboard && _learned.Count(c => c == "bubble_boat") == 1,
            "stepped off and back on, it holds them again (learned once)");
        // Walking off the rim leaves it too.
        _player.SetControlInput(new Vector2(1, 0));
        for (var i = 0; i < 120 && _host.PlayerInBubble; i++) await Frames(1);
        _player.SetControlInput(Vector2.Zero);
        await Frames(20);
        Check(!_host.PlayerInBubble && _host.ActiveBubbles.Count == 1 && _player.Ride == SmallPlayerController.BubbleRide.None,
            "walking off the rim leaves the boat, which floats on");
        Wait(61);
        await Frames(2);
        Check(_host.ActiveBubbles.Count == 0 && ReceiptData(Companion, boat)?["ended"]?.GetValue<string>() == "timed_out", "the boat pops at its time, timed_out");
    }

    // ---- the security notes' row (docs/companion/SECURITY.md), each test by its name ----

    private async Task TestSecurityRow()
    {
        await TheDiveKeyPopsItAtAnyMoment();
        await APopInTheAirDeflates();
        await EveryOtherAbilityStillRefusesThePlayersAvatar();
        await TheCompanionCannotKeepAPlayerInABubbleThePlayerWantsOutOf();
        await ACompanionCastNeverTargetsThePlayerWithoutThePreviewOrTheStepIn();
    }

    private async Task TheDiveKeyPopsItAtAnyMoment()
    {
        await NewWorld("security-dive");
        var start = _player.GlobalPosition;
        // As it forms: the dive key held when the Gubble's cast lands pops it straight away.
        _player.SetDiveInput(true, false);
        var held = CarryOnPlayer(Companion, out _);
        await Frames(2);
        var asItForms = Ok(held) && !_host.PlayerInBubble && ReceiptData(Companion, held)?["ended"]?.GetValue<string>() == "popped_by_player";
        _player.SetDiveInput(false, false);
        await UntilDown();
        // Halfway up a lift, and at its top, and while a tumble holds control.
        var moments = new (string Name, int Frames, bool Tumble)[] { ("halfway up", 240, false), ("at the top", 560, false), ("in a tumble", 120, true) };
        var popped = 0;
        foreach (var (name, frames, tumble) in moments)
        {
            Wait(BubbleLaws.PopRespectS);
            await PlayerAt(start);
            var lift = CarryOnPlayer(Companion, out _, "bubble_lift", duration: 60);
            await Frames(frames);
            if (tumble) _player.HoldControl(2f);
            _player.SetDiveInput(true, false);
            await Frames(2);
            _player.SetDiveInput(false, false);
            if (Ok(lift) && !_host.PlayerInBubble && ReceiptData(Companion, lift)?["ended"]?.GetValue<string>() == "popped_by_player") popped++;
            else GD.PushError($"Bubble: not popped {name}");
            await UntilDown();
        }
        Check(asItForms && popped == moments.Length, $"the dive key pops it at any moment: as it forms ({asItForms}), halfway up, at the top, in a tumble ({popped} of {moments.Length})");
    }

    private async Task APopInTheAirDeflates()
    {
        var results = new System.Collections.Generic.List<string>();
        // Each way a bubble pops in the air: X, the timer, the Gubble's own stop; each in a fresh Challenge world (its full pool).
        foreach (var how in new[] { "x", "timer", "gubble" })
        {
            await NewWorld("security-deflate-" + how, mode: "challenge");
            var start = _player.GlobalPosition;
            var health = Health;
            var lift = CarryOnPlayer(Companion, out _, "bubble_lift", duration: how == "timer" ? 4 : 60);
            await Frames(60 * 4 - 20);
            if (how == "x") _host.PlayerGoal("stop");
            if (how == "gubble") Send(Command(NextId("stop"), "goal.stop", new JsonObject()), Companion);
            await Frames(30);
            var ride = _player.Ride;
            var height = _player.GlobalPosition.Y - start.Y;
            var landing = await UntilDown();
            results.Add($"{how}: {ride} at {height:0.00} m, landed {landing:0.000} m/s, ended {ReceiptData(Companion, lift)?["ended"]}");
            Check(Ok(lift) && ride == SmallPlayerController.BubbleRide.Deflating && height > 0.3f && landing <= 0.01f && Health == health,
                $"a pop in the air deflates ({how}): {results.Last()}, health {Health}");
        }
        var ends = results.Select(r => r[(r.LastIndexOf(' ') + 1)..]).ToArray();
        Check(ends.SequenceEqual(new[] { "popped_by_player", "timed_out", "popped" }), "each pop's receipt says how it ended: " + string.Join("; ", results));
    }

    private async Task EveryOtherAbilityStillRefusesThePlayersAvatar()
    {
        await NewWorld("security-others");
        var feet = _player.GlobalPosition;
        var bloom = Send(Command(NextId("bloom"), "nature.cast", new JsonObject
        {
            ["capability"] = "bloom", ["center_m"] = Vec(feet), ["radius_m"] = 0.25, ["duration_s"] = 30, ["target"] = CommandHost.PlayerAvatar,
        }, preview: true), Companion, contractValid: false);
        var fire = Send(Command(NextId("fire"), "nature.cast", new JsonObject
        {
            ["capability"] = "fire", ["center_m"] = Vec(feet), ["radius_m"] = 0.25, ["duration_s"] = 10, ["target"] = CommandHost.PlayerAvatar,
        }, preview: true), Companion, contractValid: false);
        var boat = Send(Command(NextId("boat"), "nature.cast", BubbleArgs("bubble_boat", feet, CommandHost.PlayerAvatar, radius: 0.12), preview: true), Companion, contractValid: false);
        var stream = Send(Command(NextId("bubbles"), "effect.start", new JsonObject
        {
            ["capability"] = "bubbles", ["params"] = new JsonObject(), ["area"] = new JsonObject { ["center_m"] = Vec(feet), ["radius_m"] = 0.5 }, ["duration_s"] = 10,
            ["targets"] = new JsonArray(CommandHost.PlayerAvatar),
        }), Companion);
        var glow = Send(Command(NextId("glow"), "effect.start", new JsonObject
        {
            ["capability"] = "glow", ["params"] = new JsonObject(), ["area"] = new JsonObject { ["center_m"] = Vec(feet), ["radius_m"] = 0.5 }, ["duration_s"] = 10,
            ["targets"] = new JsonArray(CommandHost.PlayerAvatar),
        }), Companion);
        var directed = Send(Command(NextId("go"), "goal.set", new JsonObject { ["actor"] = CommandHost.PlayerAvatar, ["goal"] = "go_to", ["position_m"] = Vec(feet + Vector3.Right * 0.3f) }), Companion);
        var outcome = string.Join(" ", new[] { bloom, fire, boat, stream, glow, directed }.Select(r => $"{Code(r)}@{Path(r)}"));
        Check(outcome == "request_invalid@$.args.capability request_invalid@$.args.capability request_invalid@$.args.capability permission_denied@$.args.targets " +
                         "permission_denied@$.args.targets actor_denied@$.args.actor" && _player.Ride == SmallPlayerController.BubbleRide.None,
            "every other ability still refuses the player's avatar (Bloom, Fire, the boat, a stream of bubbles, Glow) and the companion never directs it: " + outcome);
    }

    private async Task TheCompanionCannotKeepAPlayerInABubbleThePlayerWantsOutOf()
    {
        await NewWorld("security-keep");
        var carry = CarryOnPlayer(Companion, out _, "bubble_lift", duration: 60);
        await Frames(60);
        // While it holds the player, nothing the companion sends holds them in: another offer is refused, its moves only lead the bubble.
        var second = Preview(Companion);
        var stop = Send(Command(NextId("go"), "goal.set", new JsonObject { ["actor"] = CommandHost.CompanionAvatarId, ["goal"] = "go_to", ["position_m"] = Vec(new Vector3(-0.8f, 0, 0.2f)) }), Companion);
        _player.SetDiveInput(true, false);
        await Frames(2);
        _player.SetDiveInput(false, false);
        var out1 = !_host.PlayerInBubble && ReceiptData(Companion, carry)?["ended"]?.GetValue<string>() == "popped_by_player";
        // Straight after, the Gubble cannot offer again (its preview and its cast wait): the player said no.
        var again = Preview(Companion);
        var token = again["data"]?["preview_id"]?.GetValue<string>();
        Wait(BubbleLaws.GhostLeadS + 0.1);
        var cast = Send(Command(NextId("carry"), "nature.cast", BubbleArgs("bubble_carry", _player.GlobalPosition, CommandHost.PlayerAvatar, previewId: token ?? new string('c', 32))), Companion);
        await UntilDown();
        // Turning a ghost away (the dive key while it shows) is a no too: its cast is refused, and the next offer waits.
        Wait(BubbleLaws.PopRespectS);
        var ghost = Preview(Companion);
        _player.SetDiveInput(true, false);
        await Frames(2);
        _player.SetDiveInput(false, false);
        Wait(BubbleLaws.GhostLeadS + 0.1);
        var turnedAway = Send(Command(NextId("carry"), "nature.cast",
            BubbleArgs("bubble_carry", _player.GlobalPosition, CommandHost.PlayerAvatar, previewId: ghost["data"]?["preview_id"]?.GetValue<string>())), Companion);
        // Once the respect has passed, an offer may come again, and the exit works again.
        Wait(BubbleLaws.PopRespectS);
        var later = CarryOnPlayer(Companion, out _);
        await Frames(10);
        _player.SetDiveInput(true, false);
        await Frames(2);
        _player.SetDiveInput(false, false);
        Check(Code(second) == "target_busy" && Ok(stop) && out1 && Code(again) == "not_ready" && Path(again) == "$.args.target" && Code(cast) is "not_ready" or "revision_conflict" &&
              Ok(ghost) && _host.BubbleGhostCount == 0 && Code(turnedAway) is "not_ready" or "revision_conflict" && Ok(later) && !_host.PlayerInBubble,
            "the companion cannot keep a player in a bubble the player wants out of: " +
            $"a second offer while held {Code(second)}, its moves {Ok(stop)}, the dive key out {out1}, at once again {Code(again)}/{Code(cast)}, a ghost turned away {Code(turnedAway)}, later {Ok(later)}");
        await UntilDown();
    }

    private async Task ACompanionCastNeverTargetsThePlayerWithoutThePreviewOrTheStepIn()
    {
        await NewWorld("security-consent");
        var feet = _player.GlobalPosition;
        var unpreviewed = Send(Command(NextId("carry"), "nature.cast", BubbleArgs("bubble_lift", feet, CommandHost.PlayerAvatar)), Companion, contractValid: false);
        var madeUp = Send(Command(NextId("carry"), "nature.cast", BubbleArgs("bubble_lift", feet, CommandHost.PlayerAvatar, previewId: new string('d', 32))), Companion);
        var atTheFeet = AtPoint(Companion, "bubble_lift", feet);
        var ghost = Preview(Companion, "bubble_lift");
        var noLead = Send(Command(NextId("carry"), "nature.cast", BubbleArgs("bubble_lift", feet, CommandHost.PlayerAvatar, previewId: ghost["data"]?["preview_id"]?.GetValue<string>())), Companion);
        // A bubble at a point beside the player stays put, whatever the Gubble does, until the player steps in.
        var beside = AtPoint(Companion, "bubble_lift", feet + new Vector3(0.3f, 0, 0));
        var formed = _host.ActiveBubbles.Count;
        var at = _host.ActiveBubbles.LastOrDefault().Center;
        GubbleAt(feet + new Vector3(-0.2f, 0, 0));
        await Frames(120);
        Check(Code(unpreviewed) == "request_invalid" && Code(madeUp) == "revision_conflict" && Code(atTheFeet) == "invalid_args" && Code(noLead) == "not_ready" &&
              Ok(beside) && formed == 1 && _host.ActiveBubbles.Count == 1 && _host.ActiveBubbles[0].Center.DistanceTo(at) < 0.001f && !_host.PlayerInBubble &&
              _player.Ride == SmallPlayerController.BubbleRide.None,
            "a companion cast never targets the player without the preview or the step-in: " +
            $"unpreviewed {Code(unpreviewed)}, a made-up preview {Code(madeUp)}, at their feet {Code(atTheFeet)}, before the ghost's lead {Code(noLead)}, beside them it waits");
    }

    // ---- the budgets ----

    private async Task TestBudget()
    {
        await NewWorld("budget");
        var first = AtPoint(Companion, "bubble_carry", _player.GlobalPosition + new Vector3(0.3f, 0, 0.1f));
        var second = CarryOnPlayer(Companion, out _);
        var third = AtPoint(Player, "bubble_carry", _player.GlobalPosition + new Vector3(-0.3f, 0, 0.1f));
        Check(Ok(first) && Ok(second) && Code(third) == "budget_exceeded" && third["error"]?["allowed"]?.GetValue<int>() == BubbleLaws.MaxBubbles,
            $"at most {BubbleLaws.MaxBubbles} bubbles at once: " + Canonical(third["error"]));
        // Time the bubbles' tick and the body's ride with a carry drifting after the Gubble: under 0.5 ms a frame together.
        GubbleAt(_player.GlobalPosition + new Vector3(-1.0f, 0, 0));
        await Frames(10);
        var watch = new Stopwatch();
        const int frames = 240;
        for (var i = 0; i < frames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            _now += TimeSpan.FromSeconds(Tick);
            watch.Start();
            _host.AdvanceBubbles(Tick);
            watch.Stop();
        }
        var kernelMs = watch.Elapsed.TotalMilliseconds / frames;
        GD.Print($"BUBBLE_BUDGET the bubbles' tick {kernelMs:0.000} ms a frame with {_host.ActiveBubbles.Count} bubbles, one carrying the player after the Gubble");
        Check(kernelMs < 0.5, $"the bubbles' tick stays under 0.5 ms a frame ({kernelMs:0.000} ms)");
        // Within the room's effect cap: a bubble is refused while the effects already fill it.
        _host.PlayerGoal("stop");
        _host.EffectLimit = 1;
        var stream = _host.PlayerEffect("bubbles");
        var capped = AtPoint(Player, "bubble_carry", _player.GlobalPosition + new Vector3(0.3f, 0, 0.1f));
        _host.EffectLimit = CommandHost.MaxActiveEffects;
        Check(Ok(stream) && Code(capped) == "budget_exceeded" && capped["error"]?["allowed"]?.GetValue<int>() == 1,
            "a bubble counts toward the room's effect cap: " + Canonical(capped["error"]));
    }
}
