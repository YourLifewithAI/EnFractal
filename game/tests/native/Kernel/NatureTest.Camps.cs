using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native.Kernel;
using EnFractal.Native.Look;

namespace EnFractal.Tests.Kernel;

/// <summary>
/// Fire's camp (MECHANICS.md decisions 49, 61, 62, 70 and 71; CommandHostCamps): the firepits' rule, the start spot kept per mode in
/// the island save (and the clock beside it), the starting campfire, resting by sitting at a fire (which teaches Fire once), firepits
/// lit by the Gubble's Fire as campfires saved with the island (the first teaching Campfire), and a lit campfire as a full camp.
/// </summary>
public partial class NatureTest
{
    private static readonly JsonObject UtcClock = new() { ["zone_id"] = "Etc/UTC", ["day_scale"] = 1, ["seasons_per_year"] = 4, ["phase_h"] = 0, ["hemisphere"] = "north" };

    private async Task TestCamps()
    {
        TestFirepitRule();
        await TestStartSpot();
        await TestStartingCampfireAndRest();
        await TestFirepits();
    }

    /// <summary>Item 5's rule: farthest-point sampling from the starting campfire, FirepitCount at most, none nearer than the spacing.</summary>
    private void TestFirepitRule()
    {
        var grid = Enumerable.Range(0, 11).SelectMany(x => Enumerable.Range(0, 11).Select(z => new Vector3(x * 2, 0, z * 2))).ToList();
        var picks = CampLaws.ChooseFirepits(Vector3.Zero, grid, CampLaws.FirepitCount, CampLaws.FirepitMinSpacingM);
        var spread = picks.Select((a, i) => picks.Skip(i + 1).Select(b => CampLaws.Flat(a, b)).Append(CampLaws.Flat(a, Vector3.Zero)).Min()).Min();
        Check(picks.Count == CampLaws.FirepitCount && picks[0] == new Vector3(20, 0, 20) && spread >= CampLaws.FirepitMinSpacingM &&
              picks.SequenceEqual(CampLaws.ChooseFirepits(Vector3.Zero, grid, CampLaws.FirepitCount, CampLaws.FirepitMinSpacingM)),
            $"firepits are chosen by farthest-point sampling from the starting campfire: {picks.Count}, the far corner first, at least {spread:0.0} m apart, the same every time");
        Check(CampLaws.ChooseFirepits(Vector3.Zero, new[] { new Vector3(1, 0, 0), new Vector3(3, 0, 1) }, 6, CampLaws.FirepitMinSpacingM).Count == 0,
            "a bench nearer than the spacing to the camp is never a firepit");
    }

    /// <summary>Item 1: the start spot per mode in the island save, the fallback to the starting campfire, the first load there; x_clock kept.</summary>
    private async Task TestStartSpot()
    {
        await NewWorld(SavePath("camp-spot"));
        var spawn = _room.SpawnFor("player").PositionM;
        Check(_host.PlaceAtStartSpot() == "first" && CampLaws.Flat(_player.GlobalPosition, spawn) < 0.03f && _host.TeamBlock()["camp"]?["spots"]?["creative"] is JsonObject,
            "the first load of an island starts at the starting campfire, and the island save keeps the mode's spot from then on");
        var there = new Vector3(-0.5f, 0.01f, -0.3f);
        Check(_player.TryTeleportTo(there) && _companion.TryTeleportTo(there + new Vector3(0.25f, 0, 0)), "(the player and the Gubble walk off)");
        await Frames(3);
        var stood = _player.LastSafePosition;
        var gubble = _companion.GlobalPosition;
        _host.NoteCampSpots();
        await NewWorld(SavePath("camp-spot"));
        Check(_host.PlaceAtStartSpot() == "saved" && _player.GlobalPosition.DistanceTo(stood) < 0.03f && _companion.GlobalPosition.DistanceTo(gubble) < 0.05f,
            "the next load puts the player and the Gubble back where they last stood");
        // Unsafe spots fall back to the starting campfire: mid-air, and inside the box. The challenge spot stays its own (one per mode).
        var state = _host.ExportRoomState()!;
        var camp = state["extensions"]!["x_play_camp"]!.AsObject();
        camp["spots"] = new JsonObject
        {
            ["challenge"] = new JsonObject { ["player_m"] = new JsonArray(-0.5, 0.0, -0.3) },
            ["creative"] = new JsonObject { ["player_m"] = new JsonArray(0.0, 1.5, 0.0), ["companion_m"] = new JsonArray(1.1, 0.0, 0.2) },
        };
        state["extensions"]!["x_clock"] = UtcClock.DeepClone();
        await NewWorld(SavePath("camp-air"));
        var rebuilt = _host.ImportRoomState((JsonObject)state.DeepClone());
        var companionSpawn = _room.SpawnFor("companion", _room.SpawnFor("player")).PositionM;
        Check(rebuilt.Ok && _host.PlaceAtStartSpot() == "fallback" && CampLaws.Flat(_player.GlobalPosition, spawn) < 0.03f && CampLaws.Flat(_companion.GlobalPosition, companionSpawn) < 0.05f,
            "a spot in mid-air (and the Gubble's inside the box) falls back to the starting campfire: " + rebuilt.Message);
        Check(_host.TeamBlock()["camp"]?["spots"]?["challenge"]?["player_m"]?[0]?.GetValue<double>() == -0.5, "another mode's spot is kept as it was: one spot per mode");
        camp["spots"]!["creative"] = new JsonObject { ["player_m"] = new JsonArray(1.1, 0.0, 0.2) };
        await NewWorld(SavePath("camp-inside"));
        Check(_host.ImportRoomState((JsonObject)state.DeepClone()).Ok && _host.PlaceAtStartSpot() == "fallback" && CampLaws.Flat(_player.GlobalPosition, spawn) < 0.03f,
            "a spot inside something (the box) falls back too");
        await NewWorld(SavePath("camp-inside"));
        Check(_host.ClockRecord?["zone_id"]?.GetValue<string>() == "Etc/UTC" && _host.TeamBlock()["clock"] is JsonObject && _host.ExportRoomState()!["extensions"]?["x_clock"] is JsonObject,
            "the island's clock record (x_clock) is kept in the island save and read back on the next load");
    }

    /// <summary>Items 2 and 3: the starting campfire, a real fire spot, lit, drawn; sitting by it rests, and the first rest teaches Fire once.</summary>
    private async Task TestStartingCampfireAndRest()
    {
        await NewWorld(SavePath("camp-rest"));
        var store = new CountingProgressStore();
        _host.ConfigureGlowProgress(store);
        var spawn = _room.SpawnFor("player").PositionM;
        var start = _host.StartingCampfire!;
        Check(start.Id == CampLaws.StartingId && start.Starting && CampLaws.Flat(start.PositionM, spawn + new Vector3(0, 0, -CampLaws.StartingFireAheadM)) < 0.01f &&
              Math.Abs(start.PositionM.Y) < 0.02f && _host.IsCampfireLit(start.Id) && _host.CampSites.Count == 1,
            $"the island's starting campfire burns 0.2 m in front of the spawn, on the floor, lit ({start.PositionM}); an indoor room has no firepits");
        var look = CampfireLook.Create(_host);
        _world!.AddChild(look);
        look.Rebuild();
        Check(look.Drawn == 1 && look.Burning == 1, "the look draws it burning");
        Check(_player.TryTeleportTo(spawn + new Vector3(0.8f, 0, 0)), "(the player stands away from the fire)");
        await Frames(3);
        var away = _host.PlayerRest();
        Check(!away.Ok && away.Words == CommandHost.RestNearFire && !_host.PlayerResting && store.Saves == 0, "away from a fire the rest key says to come closer: " + away.Words);
        Check(_player.TryTeleportTo(spawn), "(the player stands by the fire)");
        await Frames(3);
        var rest = _host.PlayerRest();
        Check(rest.Ok && rest.LearnedFire && rest.Campfire == CampLaws.StartingId && _host.GlowProgress.Has(ExpertiseLists.Fire, ExpertiseLists.FirstFire) && store.Saves == 1 && _host.PlayerResting,
            "sitting down by the starting campfire rests, and the first rest teaches the Gubble Fire: " + rest.Words);
        var again = _host.PlayerRest();
        Check(again.Ok && !again.LearnedFire && store.Saves == 1, "a second rest teaches nothing more: once per team");
        Check(_player.TryTeleportTo(spawn + new Vector3(0.1f, 0, 0)) && !_host.PlayerResting, "moving stands the player up");
    }

    /// <summary>Items 5 and 6: firepits unlit at first, lit by the Gubble's Fire as campfires saved with the island; a lit one is a full camp.</summary>
    private async Task TestFirepits()
    {
        var pits = new[] { new Vector3(-1.2f, 0, 1.0f), new Vector3(1.5f, 0, -1.1f) };
        await NewWorld(SavePath("camp-pits"));
        var store = new CountingProgressStore();
        _host.ConfigureGlowProgress(store);
        _host.FirepitsForTests = pits;
        var spawn = _room.SpawnFor("player").PositionM;
        Check(_host.CampSites.Count == 3 && _host.CampSites[1].Id == "firepit:1" && !_host.IsCampfireLit("firepit:1") && !_host.IsCampfireLit("firepit:2"), "the firepits start unlit");
        var beside = pits[0] + new Vector3(0.15f, 0.01f, 0);
        Check(_player.TryTeleportTo(beside), "(the player stands by the first firepit)");
        await Frames(3);
        Check(!_host.PlayerAtCamp() && _host.CampHeartsFactorNow() == 1f && _host.PlayerRest().Words == CommandHost.RestUnlit, "an unlit firepit is no camp yet, and sitting there says so");
        Check(_companion.TryTeleportTo(pits[0] + new Vector3(0, 0.01f, -0.3f)), "(the Gubble floats beside it)");
        await Frames(2);
        var revision = _host.Revision;
        var lit = Fire(Companion, pits[0] + new Vector3(0, 0.01f, 0), 0.05, 6, out var preview);
        Check(Ok(preview) && preview["data"]?["cell_count"]?.GetValue<int>() == 1 && Ok(lit) && _host.Revision == revision + 1 && _host.IsCampfireLit("firepit:1") &&
              _host.Nature.Fire == null && _host.Nature.Cells.Values.All(c => c.BurningS <= 0),
            "the Gubble's Fire cast on the firepit lights it, one durable revision, and nothing else catches: " + (lit["error"]?["message"]?.GetValue<string>() ?? ""));
        Check(_host.GlowProgress.Has(ExpertiseLists.Fire, ExpertiseLists.Campfire) && store.Saves == 1, "the first firepit lit teaches Campfire");
        // A lit campfire is a full camp: the camp's spends and knacks (PlayerAtCamp), hearts faster, and the camp the player wakes at.
        Check(_host.PlayerAtCamp() && _host.CampHeartsFactorNow() == CampLaws.CampHeartsFactor, "beside the lit campfire the player is at a camp, and hearts refill faster");
        var rest = _host.PlayerRest();
        Check(rest.Ok && rest.NewCamp && _host.HomeCamp == "firepit:1" && !rest.LearnedFire, "sitting by it makes it the player's camp: " + rest.Words);
        var sat = _player.GlobalPosition;
        Check(_player.TryTeleportTo(spawn) && _player.RecoverAtSpawn() && CampLaws.Flat(_player.GlobalPosition, sat) < 0.05f, "and a knockout's recovery wakes the player there");
        // Lit campfires are saved with the island.
        await NewWorld(SavePath("camp-pits"));
        _host.FirepitsForTests = pits;
        Check(_host.IsCampfireLit("firepit:1") && !_host.IsCampfireLit("firepit:2") && _host.HomeCamp == "firepit:1", "the lit campfire and the player's camp are saved with the island");
    }
}
