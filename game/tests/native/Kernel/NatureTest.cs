using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Room;

namespace EnFractal.Tests.Kernel;

/// <summary>CPU/headless checks of simulation bounds, durable publication, approvals and restoration.</summary>
public partial class NatureTest : Node3D
{
    private int _checks, _failed;
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public override async void _Ready()
    {
        try { Pure(); Store(); await Host(); }
        catch (Exception e) { _failed++; GD.PrintErr("NATURE exception: " + e); }
        GD.Print($"NATIVE_KERNEL_NATURE: {_checks - _failed}/{_checks} checks passed");
        GetTree().Quit(_failed == 0 ? 0 : 1);
    }
    private void Check(bool pass, string label) { _checks++; if (!pass) { _failed++; GD.PrintErr("NATURE FAIL " + label); } }
    private static NatureCell Soil(int x, int z) => new() { X = x, Z = z, Y = 0, Surface = "shell:soil", Material = "grass", Fertile = true, Fuel = .7f, Moisture = .35f };
    private static NatureState Fresh() => NatureState.Fresh("synthetic", Hash);
    private static void Tick(NatureState state, int count, Func<int, int, NatureCell?>? sample = null, Func<NatureCell, bool>? protectedCell = null, float light = 1)
    { for (var i = 0; i < count; i++) state.Step(.5f, sample ?? ((x, z) => Soil(x, z)), protectedCell ?? (_ => false), _ => light); }
    private void Pure()
    {
        var at = new Vector3(.025f, 0, .025f);
        var state = Fresh(); state.BeginUndo("Bloom", 0);
        var planted = state.Bloom(at, .25f, 30, (x, z) => x < 0 ? null : x == 0 ? new NatureCell { X = x, Z = z, Material = "stone" } : Soil(x, z));
        Check(planted > 0 && state.Cells.Values.All(c => c.X > 0 && c.Fertile), "Bloom respects sampled gaps and nonsoil materials");
        Check(state.Cells.Values.All(c => new Vector2(c.Position.X - at.X, c.Position.Z - at.Z).Length() <= .25f), "painting remains inside the requested circle");
        var first = state.Cells.Values.First(); var initial = first.Growth;
        Tick(state, 10); Check(first.Growth > initial, "moist lit flowers grow");
        var healthy = first.Growth; Tick(state, 20, light: 0); Check(first.Growth < healthy, "darkness makes growth fade");
        Tick(state, 160); Check(state.Cells.Values.All(c => c.Growth == 0), "flowers fade after their finite lifespan");
        state.UndoLast(); Check(state.Cells.Count == 0 && state.Revision == 2, "one undo restores the painted ground");

        var fire = Fresh(); fire.BeginUndo("Fire", 7);
        Check(fire.Ignite(at, .45f, 30, 7, new(), (x, z) => Soil(x, z)) > 0, "dry fuel ignites");
        var start = fire.Cells.Count;
        Tick(fire, 30);
        Check(fire.Cells.Count > start, "fire spreads to neighboring fuel");
        Check(fire.Cells.Values.Count(c => c.BurningS > 0) <= 200, "burning cell budget remains bounded");
        Check(fire.Cells.Values.All(c => new Vector2(c.Position.X - at.X, c.Position.Z - at.Z).Length() <= .45f + .001f), "spread cannot escape its preview radius");
        Check(fire.Cells.Values.Any(c => c.Ash > 0), "fire leaves ash");
        fire.RememberObject("obj:wood"); fire.Objects["obj:wood"] = new NatureObject { Id = "obj:wood", Burn = 1 };
        fire.UndoLast(); Check(fire.Cells.Count == 0 && fire.Objects.Count == 0 && fire.Fire == null, "one fire undo groups ground and object damage");

        var rain = Fresh(); rain.WeatherS = 180; rain.BeginUndo("Fire", 0);
        rain.Ignite(at, 1.5f, 60, 0, new(), (x, z) => Soil(x, z)); Tick(rain, 6);
        Check(rain.Cells.Values.All(c => c.BurningS <= 0) && rain.Weather.Rain01 == 1, "the same rain state extinguishes fire");
        var wet = Fresh(); wet.BeginUndo("Fire", 0);
        Check(wet.Ignite(at, 1, 30, 0, new(), (x, z) => new NatureCell { X = x, Z = z, Fuel = 1, Moisture = 1 }) == 0, "wet fuel refuses ignition");
        var gap = Fresh(); gap.BeginUndo("Fire", 0);
        NatureCell? Gap(int x, int z) => x >= 3 && x <= 5 ? null : Soil(x, z);
        gap.Ignite(at, 1.5f, 30, 0, new(), Gap); Tick(gap, 40, Gap);
        Check(gap.Cells.Values.All(c => c.X < 3), "water/nonfuel gap prevents spread across the break");
        var protectedFire = Fresh(); protectedFire.BeginUndo("Fire", 0); protectedFire.Ignite(at, 1, 30, 0, new(), (x, z) => Soil(x, z));
        Tick(protectedFire, 1, protectedCell: _ => true);
        Check(protectedFire.Cells.Values.All(c => c.BurningS == 0), "a new protection extinguishes already burning cells");

        var ash = Fresh(); var burned = Soil(0, 0); burned.Ash = 1; burned.Fuel = 0; burned.Moisture = .8f; ash.Cells[burned.Key] = burned;
        ash.WeatherS = 180; Tick(ash, 30);
        Check(burned.Ash < 1 && burned.Fuel > 0, "moist ash gradually restores ground fuel");
        var saved = LocalNatureStore.Parse(JsonSerializer.SerializeToUtf8Bytes(ash, NatureState.JsonOptions), "synthetic", Hash);
        Check(saved.Weather == ash.Weather && saved.Cells.Count == ash.Cells.Count, "weather and nature survive serialization exactly");
        Check(NatureState.Grid(new Vector3(-.001f, 0, -.001f)) == (-1, -1), "negative grid coordinates floor rather than truncate");
        var refused = false; try { Tick(ash, 1); ash.Step(float.NaN, (x, z) => Soil(x, z), _ => false, _ => 1); } catch (ArgumentOutOfRangeException) { refused = true; }
        Check(refused, "nonfinite simulation steps are refused");
    }
    private void Store()
    {
        var directory = ProjectSettings.GlobalizePath("user://tests/nature/" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "nature.json");
        var store = new LocalNatureStore(path); var state = store.Load("synthetic", Hash);
        state.WeatherS = 142.25; store.Save(state);
        var competing = new LocalNatureStore(path); var other = competing.Load("synthetic", Hash);
        state.WeatherS = 150; store.Save(state);
        var failed = false; try { other.WeatherS = 999; competing.Save(other); } catch (NatureStateException) { failed = true; }
        Check(failed && new LocalNatureStore(path).Load("synthetic", Hash).WeatherS == 150, "stale writers cannot replace a newer saved weather state");
        var original = File.ReadAllBytes(path);
        File.WriteAllText(path, "{\"schema\":\"enfractal.nature_state\"}");
        failed = false; try { new LocalNatureStore(path).Load("synthetic", Hash); } catch (NatureStateException) { failed = true; }
        Check(failed && File.ReadAllText(path).Contains("schema"), "invalid partial saves fail closed and stay unchanged");
        File.WriteAllBytes(path, original);
        failed = false; try { new LocalNatureStore(path).Load("other_room", Hash); } catch (NatureStateException) { failed = true; }
        Check(failed, "manifest/room identity pins prevent cross-room loading");
        failed = false; try { LocalNatureStore.Parse(Encoding.UTF8.GetBytes("{\"schema\":1,\"schema\":2}"), "synthetic", Hash); } catch (NatureStateException) { failed = true; }
        Check(failed, "duplicate save keys are rejected");
        state.BeginUndo("Saved ownership", 0, CommandHost.CompanionPrincipal);
        var owned = state.ToJson();
        Check(LocalNatureStore.Parse(CanonicalJson.Bytes(owned), "synthetic", Hash).Undo!.Principal == CommandHost.CompanionPrincipal, "undo ownership survives the strict saved state parser");
        owned["undo"]!.AsObject().Remove("principal");
        Check(LocalNatureStore.Parse(CanonicalJson.Bytes(owned), "synthetic", Hash).Undo!.Principal == CommandHost.PlayerPrincipal, "legacy undo without ownership defaults safely to the player");
        owned["undo"]!["principal"] = "companion:untrusted";
        failed = false; try { LocalNatureStore.Parse(CanonicalJson.Bytes(owned), "synthetic", Hash); } catch (NatureStateException) { failed = true; }
        Check(failed, "invalid saved undo principals are refused");
        File.Delete(path); File.Delete(path + ".lock"); Directory.Delete(directory);
    }

    private sealed class MemoryStore : INatureStore
    {
        public NatureState? Saved; public bool Fail;
        public NatureState Load(string roomId, string manifest) => Saved?.Copy() ?? NatureState.Fresh(roomId, manifest);
        public void Save(NatureState state) { if (Fail) throw new NatureStateException("Synthetic disk full."); Saved = state.Copy(); }
    }
    private sealed class ChallengeVitals : IPlayerVitalsStore
    {
        public PlayerModesProfile Load() => PlayerModesProfile.Fresh.Select("challenge");
        public void Save(PlayerModesProfile profile) { }
    }
    private async Task Host()
    {
        var room = RoomData.Load(RoomWorld.DefaultRoom);
        var built = RoomBuilder.Build(room); AddChild(built);
        await Frames(2);
        var player = new SmallPlayerController { Name = "Player", ReadKeyboard = false, Position = room.SpawnFor("player").PositionM + Vector3.Up * .008f };
        AddChild(player);
        var companion = new CompanionAvatar { Name = "Companion", Position = room.SpawnFor("companion").PositionM + Vector3.Up * .008f };
        AddChild(companion); companion.BindPlayer(player); companion.Stay();
        var host = CommandHost.Create(this, room, player, companion, null, "user://tests/nature/" + Guid.NewGuid().ToString("N") + "/inventions.json");
        var store = new MemoryStore(); host.ConfigureNature(store); host.LoadRules("storybook_wild", 4); host.SetProcess(false);
        await Frames(3);
        var at = companion.GlobalPosition;
        var bloomArgs = new JsonObject { ["capability"] = "bloom", ["center_m"] = KernelJson.Vector(at), ["radius_m"] = .2, ["duration_s"] = 120 };
        JsonObject Send(string action, string op, JsonObject args, bool preview = false, string principal = CommandHost.PlayerPrincipal)
        {
            var command = new JsonObject { ["schema"] = "enfractal.command", ["version"] = 1, ["action_id"] = action, ["room_id"] = room.RoomId, ["op"] = op, ["args"] = args.DeepClone(), ["preview"] = preview };
            if (op is "protect.lock" or "protect.unlock") command["expected_revision"] = host.Revision;
            return host.HandleObject(CanonicalJson.Text(command), principal);
        }
        var physicalArgs = (JsonObject)bloomArgs.DeepClone(); physicalArgs["center_m"] = new JsonArray(1.4, .01, 1.2);
        var noSoil = Send("actual-wood-floor", "nature.cast", physicalArgs, true);
        Check(noSoil["ok"]!.GetValue<bool>() == false, "actual physics wood floor refuses Bloom");
        var floor = built.GetNode<Node3D>("Shell/floor"); floor.SetMeta("material_role", "grass");
        var roof = new StaticBody3D { Position = new Vector3(1.4f, .5f, 1.2f), CollisionLayer = RoomBuilder.WorldLayer, CollisionMask = 0 };
        roof.SetMeta("material_role", "wood"); roof.SetMeta("entity_id", "shell:test_arbor");
        roof.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(.7f, .04f, .7f) } }); AddChild(roof);
        await Frames(2);
        var underCover = Send("actual-grass-under-roof", "nature.cast", physicalArgs, true);
        Check(underCover["ok"]!.GetValue<bool>() && underCover["data"]?["cell_count"]?.GetValue<int>() > 0, "actual physics finds fertile ground below an arbor");
        floor.SetMeta("material_role", "wood"); roof.QueueFree(); await Frames(2);
        host.NatureSampleForTests = (x, z) => Soil(x, z);
        var preview = Send("bloom-preview", "nature.cast", bloomArgs, true);
        Check(preview["ok"]!.GetValue<bool>() && host.Nature.Cells.Count == 0 && store.Saved == null, "preview never changes or saves ground");
        store.Fail = true;
        var denied = Send("disk-full", "nature.cast", bloomArgs);
        Check(denied["ok"]!.GetValue<bool>() == false && host.Nature.Cells.Count == 0 && host.Nature.Receipts.Count == 0, "save failure publishes neither flowers nor receipts");
        store.Fail = false;
        var planted = Send("bloom-one", "nature.cast", bloomArgs);
        Check(planted["ok"]!.GetValue<bool>() && host.Nature.Cells.Count > 0, "manual Bloom uses the validated command path");
        var count = host.Nature.Cells.Count; var revision = host.Nature.Revision;
        var again = Send("bloom-one", "nature.cast", bloomArgs);
        Check(again["replayed"]!.GetValue<bool>() && host.Nature.Revision == revision && host.Nature.Cells.Count == count, "receipt replay does not plant twice");
        var changed = (JsonObject)bloomArgs.DeepClone(); changed["radius_m"] = .3;
        Check(Send("bloom-one", "nature.cast", changed)["error"]?["code"]?.GetValue<string>() == "action_id_conflict", "same nature action cannot change meaning");
        host.ConfigureNature(store);
        Check(Send("bloom-one", "nature.cast", bloomArgs)["replayed"]!.GetValue<bool>(), "nature receipts replay after reload");
        var fireArgs = new JsonObject { ["capability"] = "fire", ["center_m"] = KernelJson.Vector(at), ["radius_m"] = .3, ["duration_s"] = 10 };
        Check(Send("fire-no-preview", "nature.cast", fireArgs)["ok"]!.GetValue<bool>() == false && host.Nature.Fire == null, "Fire requires a host-created preview");
        var firePreview = Send("fire-preview", "nature.cast", fireArgs, true);
        Check(firePreview["ok"]!.GetValue<bool>() && firePreview["data"]?["preview_id"] != null, "Fire preview describes the bounded area");
        fireArgs["preview_id"] = firePreview["data"]!["preview_id"]!.DeepClone();
        Send("trail-change", "nature.trail", new JsonObject { ["enabled"] = false });
        Check(Send("fire-stale", "nature.cast", fireArgs)["error"]?["code"]?.GetValue<string>() == "revision_conflict", "another nature command invalidates an old preview");
        var undo = host.PlayerNatureUndo();
        Check(undo["ok"]!.GetValue<bool>() && host.Nature.TrailEnabled, "room.undo restores the latest nature command");
        Send("ai-own-trail", "nature.trail", new JsonObject { ["enabled"] = false }, principal: CommandHost.CompanionPrincipal);
        host.ConfigureNature(store);
        Check(host.Nature.Undo?.Principal == CommandHost.CompanionPrincipal && Send("ai-own-undo", "room.undo", new JsonObject { ["to_revision"] = host.Nature.Revision - 1 }, principal: CommandHost.CompanionPrincipal)["ok"]!.GetValue<bool>() && host.Nature.TrailEnabled, "companion can undo its own most recent saved nature event");
        Send("ai-own-trail-two", "nature.trail", new JsonObject { ["enabled"] = false }, principal: CommandHost.CompanionPrincipal);
        Check(host.PlayerNatureUndo()["ok"]!.GetValue<bool>() && host.Nature.TrailEnabled, "player can undo the companion's latest nature event");
        Send("player-owned-trail", "nature.trail", new JsonObject { ["enabled"] = false });
        var beforeApprovalRevision = host.Nature.Revision;
        var undoArgs = new JsonObject { ["to_revision"] = host.Nature.Revision - 1 };
        var undoHeld = Send("ai-player-undo", "room.undo", undoArgs, principal: CommandHost.CompanionPrincipal);
        Check(undoHeld["error"]?["code"]?.GetValue<string>() == "approval_required" && !host.Nature.TrailEnabled && host.Nature.Revision == beforeApprovalRevision, "companion undo of a player event waits without changing state");
        Check(Send("ai-player-undo", "room.undo", undoArgs, principal: CommandHost.CompanionPrincipal)["error"]?["code"]?.GetValue<string>() == "approval_required" && host.Nature.Revision == beforeApprovalRevision, "resending a held undo cannot impersonate the player's approval");
        if (undoHeld["approval_needed"]?["request_id"] is { } undoApproval)
        {
            var approvedUndo = host.Approve(undoApproval.GetValue<string>());
            Check(approvedUndo["ok"]!.GetValue<bool>() && approvedUndo["approved_by"]?.GetValue<string>() == CommandHost.PlayerPrincipal && host.Nature.TrailEnabled, "only trusted player approval commits the held undo");
        }

        var pack = JsonNode.Parse(Godot.FileAccess.GetFileAsString("res://rules/storybook_wild/v4.json"))!.AsObject();
        var bloom = pack["abilities"]!.AsArray().First(a => a!["capability"]!.GetValue<string>() == "bloom")!;
        bloom["tier"] = "keyed_yes"; host.UseRules(IslandRules.Parse(CanonicalJson.Bytes(pack)));
        var keyedBloom = Send("keyed-bloom", "nature.cast", bloomArgs, principal: CommandHost.CompanionPrincipal);
        Check(keyedBloom["error"]?["code"]?.GetValue<string>() == "approval_required", "keyed Bloom requires player approval for the companion");
        if (keyedBloom["approval_needed"]?["request_id"] is { } bloomApproval) host.Deny(bloomApproval.GetValue<string>());
        var keyedTrail = Send("keyed-trail", "nature.trail", new JsonObject { ["enabled"] = true }, principal: CommandHost.CompanionPrincipal);
        Check(keyedTrail["error"]?["code"]?.GetValue<string>() == "approval_required", "keyed flower trail requires player approval for the companion");
        if (keyedTrail["approval_needed"]?["request_id"] is { } trailApproval) Check(host.Approve(trailApproval.GetValue<string>())["ok"]!.GetValue<bool>(), "approved companion trail records its permission context");
        bloom["cast_by"] = new JsonArray("player"); host.UseRules(IslandRules.Parse(CanonicalJson.Bytes(pack)));
        Check(Send("forbidden-trail", "nature.trail", new JsonObject { ["enabled"] = true }, principal: CommandHost.CompanionPrincipal)["error"]?["code"]?.GetValue<string>() == "permission_denied", "player-only Bloom cannot be enabled through companion trail");
        var beforeTrail = host.Nature.Cells.Count; companion.GlobalPosition += Vector3.Right * .3f; host.AdvanceNature(.5);
        Check(host.Nature.Cells.Count == beforeTrail, "changing rules also stops a previously enabled unauthorized trail");
        companion.GlobalPosition -= Vector3.Right * .3f; host.LoadRules("storybook_wild", 4);

        var original = new JsonObject
        {
            ["schema"] = "enfractal.creation", ["version"] = 1, ["name"] = "Nature repair fixture", ["seed"] = 1, ["mount"] = "ground",
            ["parts"] = new JsonArray(new JsonObject { ["id"] = "base", ["shape"] = "box", ["position_m"] = new JsonArray(0, .06, 0), ["rotation_deg"] = new JsonArray(0, 0, 0), ["size_m"] = new JsonArray(.12, .12, .12), ["material"] = "wood" }),
            ["nodes"] = new JsonArray(new JsonObject { ["id"] = "use", ["op"] = "interact", ["part_id"] = "base", ["params"] = new JsonObject() }, new JsonObject { ["id"] = "lamp", ["op"] = "light", ["part_id"] = "base", ["params"] = new JsonObject { ["intensity"] = 1, ["duration_s"] = 1 } }),
            ["edges"] = new JsonArray(new JsonObject { ["from"] = "use", ["to"] = "lamp" }),
        };
        var made = Send("wood-fixture", "creation.place", new JsonObject { ["source"] = original, ["placement"] = new JsonObject { ["position_m"] = new JsonArray(1.5, 0, 1.2) } });
        Check(made["ok"]!.GetValue<bool>(), "wood creation fixture enters through the real authority");
        if (made["ok"]!.GetValue<bool>())
        {
            var id = made["created"]![0]!.GetValue<string>(); await Frames(2);
            var targeted = new JsonObject { ["capability"] = "fire", ["center_m"] = new JsonArray(1.35, .01, 1.2), ["radius_m"] = .3, ["duration_s"] = 10 };
            var quote = Send("scorch-preview", "nature.cast", targeted, true);
            if (quote["ok"]?.GetValue<bool>() != true || quote["data"]?["requires_approval"]?.GetValue<bool>() != true) GD.PrintErr("NATURE scorch preview: " + CanonicalJson.Text(quote));
            Check(quote["ok"]!.GetValue<bool>() && quote["data"]?["requires_approval"]?.GetValue<bool>() == true, "preview identifies creation damage before casting");
            if (quote["ok"]!.GetValue<bool>())
            {
                targeted["preview_id"] = quote["data"]!["preview_id"]!.DeepClone();
                var held = Send("scorch-fire", "nature.cast", targeted);
                Check(held["error"]?["code"]?.GetValue<string>() == "approval_required" && host.Nature.Fire == null, "even a manual fire waits for keyed approval near a creation");
                if (held["approval_needed"]?["request_id"] is { } approval)
                {
                    var lit = host.Approve(approval.GetValue<string>());
                    Check(lit["ok"]!.GetValue<bool>() && lit["approved_by"]?.GetValue<string>() == CommandHost.PlayerPrincipal, "trusted player approval starts the exact held fire");
                    host.AdvanceNature(.5);
                    Check(host.Nature.Objects.TryGetValue(id, out var scorched) && scorched.Burn > 0 && scorched.RepairCost.GetValueOrDefault("wood") == 1 && scorched.SourceHash.Length == 64, "scorch retains a hash-pinned original material quote");
                    Check(Send("burnt-use", "creation.activate", new JsonObject { ["target"] = id })["ok"]!.GetValue<bool>() == false, "a scorched creation cannot activate");
                    Check(host.PlayerNatureUndo()["ok"]!.GetValue<bool>() && !host.Nature.Objects.ContainsKey(id) && host.Nature.Fire == null, "one undo restores a scorched creation and its ground");
                    targeted.Remove("preview_id");
                    var repairFirePreview = Send("repair-fire-preview", "nature.cast", targeted, true);
                    targeted["preview_id"] = repairFirePreview["data"]!["preview_id"]!.DeepClone();
                    var repairHeld = Send("repair-fire", "nature.cast", targeted);
                    host.Approve(repairHeld["approval_needed"]!["request_id"]!.GetValue<string>());
                    for (var t = 0; t < 21; t++) host.AdvanceNature(.5);
                    host.ConfigurePlayerVitals(new ChallengeVitals());
                    var woodBefore = host.Nature.Wallet["wood"];
                    store.Fail = true; var failedRepair = host.PlayerNatureRepair(id); store.Fail = false;
                    Check(failedRepair["ok"]!.GetValue<bool>() == false && host.IsNatureUnavailable(id) && host.Nature.Wallet["wood"] == woodBefore, "repair save failure preserves scorch and the entire material wallet");
                    var repaired = host.PlayerNatureRepair(id);
                    Check(repaired["ok"]!.GetValue<bool>() && !host.IsNatureUnavailable(id) && host.Nature.Wallet["wood"] == woodBefore - 1, "Challenge repair restores the creation for its original material cost");
                    Check(host.PlayerNatureUndo()["ok"]!.GetValue<bool>() && host.IsNatureUnavailable(id) && host.Nature.Wallet["wood"] == woodBefore, "repair undo restores scorch and the spent materials together");
                    host.PlayerNatureRepair(id);
                }
            }
            var lockResult = Send("protect-fire-fixture", "protect.lock", new JsonObject { ["targets"] = new JsonArray(id) });
            if (lockResult["ok"]?.GetValue<bool>() != true) GD.PrintErr("NATURE lock fixture: " + CanonicalJson.Text(lockResult));
            Check(lockResult["ok"]!.GetValue<bool>(), "protection fixture uses the authority's current lock");
            Check(host.PlayerNatureUndo()["error"]?["code"]?.GetValue<string>() == "revision_conflict", "nature undo refuses to cross a newer authority edit");
            var lockedPreview = Send("locked-preview", "nature.cast", new JsonObject { ["capability"] = "fire", ["center_m"] = new JsonArray(1.35, .01, 1.2), ["radius_m"] = .3, ["duration_s"] = 10 }, true);
            Check(lockedPreview["ok"]!.GetValue<bool>() && !lockedPreview["data"]!["affected_ids"]!.AsArray().Any(v => v!.GetValue<string>() == id), "protected creations never enter the fire's authorized target list");
        }
        host.QueueFree(); player.QueueFree(); companion.QueueFree(); await Frames(2);
    }
    private async Task Frames(int n) { for (var i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
}
