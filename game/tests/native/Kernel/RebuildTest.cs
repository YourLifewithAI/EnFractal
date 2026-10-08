using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Room;

namespace EnFractal.Tests.Kernel;

/// <summary>
/// P6, the world as data (Run 2): play changes the test room (objects moved, creations built, locks, a note, a fetch's
/// task, discovered space), the host writes room state, the whole room is freed, and a room built from nothing (the
/// manifest, the avatars at their spawns, an empty save) is rebuilt from that state. The rebuilt room writes the same room
/// state, journal and discovered map included, its scene stands where the state says, its receipts replay, and its own
/// save loads again the same. Then the save migration between room manifests: a re-exported room (the box moved) brings
/// over what still fits, leaves the rest in the old file untouched, and keeps old action ids from conflicting reuse.
/// Pass "-- --dump=DIR" to write the room states for contracts/validate.py --state.
/// </summary>
public partial class RebuildTest : Node3D
{
    private const string TestRoot = "user://tests/rebuild";
    private const string Player = CommandHost.PlayerPrincipal;
    private const string Companion = CommandHost.CompanionPrincipal;
    private int _checks;
    private int _failures;
    private int _ids;
    private string? _dump;
    private RoomData _room = null!;
    private Node3D _world = null!;
    private CommandHost _host = null!;
    private SmallPlayerController _player = null!;
    private CompanionAvatar _companion = null!;
    private DateTime _now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    public override async void _Ready()
    {
        try
        {
            _dump = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--dump=", StringComparison.Ordinal))?["--dump=".Length..];
            if (_dump != null) System.IO.Directory.CreateDirectory(_dump);
            RemoveAll();
            CommandHost.SaveRoot = TestRoot + "/saves";
            _room = RoomData.Load(RoomWorld.DefaultRoom);
            await TestRebuildFromNothing();
            await TestMigrationBetweenManifests();
            GD.Print($"NATIVE_KERNEL_REBUILD: {_checks - _failures}/{_checks} checks passed; room state written, the room freed and rebuilt from nothing (shell, objects at their poses, creations, locks, avatars, journal and discovered map), and saves migrated between room manifests");
            RemoveAll();
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception error)
        {
            GD.PushError("Rebuild test exception: " + error);
            GetTree().Quit(1);
        }
    }

    // ---- save, free, rebuild from nothing, compare ----

    private async Task TestRebuildFromNothing()
    {
        await NewWorld(_room, $"{TestRoot}/a/inventions.json", sweep: true);
        Check(Ok(Send(Command(NextId("place"), "entity.place", new JsonObject { ["target"] = "obj:doorstop", ["placement"] = Placement(0.99, 0.3, 0.2, "obj:box") }), Player)), "the doorstop goes on the box");
        Check(Ok(Send(Command(NextId("place"), "entity.place", new JsonObject { ["target"] = "obj:book", ["placement"] = Placement(0.45, 0, -0.5) }), Player)), "the book goes north");
        var lamp = Command("lamp", "creation.place", new JsonObject { ["source"] = Source("Lamp"), ["placement"] = Turned(-1.5, 0, 0.9) });
        var lampId = Send(lamp, Player)["created"]?[0]?.GetValue<string>() ?? "";
        var arch = Send(Command(NextId("arch"), "creation.place", new JsonObject { ["source"] = Source("Arch"), ["placement"] = Placement(-1.5, 0, 0.5) }), Companion);
        Check(lampId.Length > 0 && Ok(arch), "the player builds a lamp turned a quarter, the companion an arch");
        Check(Ok(Send(Command(NextId("lock"), "protect.lock", new JsonObject { ["targets"] = new JsonArray(lampId, "obj:rug") }, expectedRevision: _host.Revision), Player)), "the player protects the lamp and the rug");
        Check(Ok(Send(Command(NextId("note"), "journal.note", new JsonObject { ["text"] = "The lamp lights the reading corner." }), Companion)), "the companion writes a note");
        await Frames(40);
        _host.TeamSightIntervalS = 0;
        var before = _host.ExportRoomState()!;
        var scene = SceneOf();
        var instances = CreationIds();
        Dump("room_state_before.json", before);
        Check(before["entities"]![lampId]?["protection"]?["locked"]?.GetValue<bool>() == true && before["entities"]!["obj:doorstop"]?["transform"] != null &&
            before["entities"]!["avatar:companion"]?["avatar"]?["role"]?.GetValue<string>() == "companion" && before["journal"]!["notes"]!.AsArray().Count == 1 &&
            before["journal"]!["history"]!.AsArray().Count >= 2 && before["discovered"]!["levels"]!.AsArray().Count > 0 && before["discovered"]!["entities"]!.AsObject().Count > 0,
            "room state holds the moved objects, the creations and their locks, the avatars, the journal and the discovered map");

        // Free everything: the room, the avatars and the host.
        _world.QueueFree();
        await Frames(3);
        await NewWorld(_room, $"{TestRoot}/b/inventions.json", sweep: false);
        Check(_host.Revision == 0 && CreationIds().Count == 0 && _host.ExportJournal()["notes"]!.AsArray().Count == 0, "a room built from nothing: no play, no creations, an empty journal");
        var rebuilt = _host.ImportRoomState(before);
        var after = _host.ExportRoomState()!;
        Dump("room_state_after.json", after);
        Check(rebuilt.Ok && Canonical(Unsessioned(after)) == Canonical(Unsessioned(before)),
            "rebuilt from room state, the room writes the same room state: entities, receipts, journal, discovered map and extensions: " + rebuilt.Message);
        Check(Canonical(after["journal"]) == Canonical(before["journal"]) && Canonical(after["discovered"]) == Canonical(before["discovered"]), "the journal and the discovered map came through whole");
        var sceneAfter = SceneOf();
        Check(scene.Count == sceneAfter.Count && scene.All(p => sceneAfter.TryGetValue(p.Key, out var t) && t.Origin.DistanceTo(p.Value.Origin) < 1e-4f && t.Basis.IsEqualApprox(p.Value.Basis)),
            "every object's node stands where it stood: the scene is derived from the state");
        Check(CreationIds().SequenceEqual(instances) && instances.Count == 2, "and the creations are back in the scene: " + string.Join(", ", CreationIds()));
        var entities = Summaries();
        Check(entities[lampId]["protected"]!.GetValue<bool>() && entities["obj:rug"]["protected"]!.GetValue<bool>() && !entities["obj:book"]["protected"]!.GetValue<bool>(), "the locks hold");
        var replay = Send(lamp, Player);
        Check(replay["replayed"]?.GetValue<bool>() == true && replay["created"]?[0]?.GetValue<string>() == lampId, "an earlier action id replays its receipt in the rebuilt room");
        Check(Code(Send(Command("lamp", "creation.place", new JsonObject { ["source"] = Source("Other"), ["placement"] = Placement(-1.0, 0, 0.9) }), Player)) == "action_id_conflict",
            "and refuses other content under it");
        Check(!_host.ImportRoomState(before).Ok, "a room that has play is never overwritten by an import");

        // The rebuilt room's own save loads again, the same.
        _world.QueueFree();
        await Frames(3);
        await NewWorld(_room, $"{TestRoot}/b/inventions.json", sweep: false);
        Check(_host.Authority.Call("is_ready").AsBool() && Canonical(Unsessioned(_host.ExportRoomState()!)) == Canonical(Unsessioned(before)), "the rebuilt room's save loads again into the same room state");

        // Tampered or foreign states are refused whole.
        _world.QueueFree();
        await Frames(3);
        await NewWorld(_room, $"{TestRoot}/c/inventions.json", sweep: false);
        var tampered = (JsonObject)before.DeepClone();
        tampered["entities"]![lampId]!["creation"]!["source"]!["name"] = "Not the lamp";
        var foreign = (JsonObject)before.DeepClone();
        foreign["room_pin"]!["manifest_sha256"] = new string('0', 64);
        Check(!_host.ImportRoomState(tampered).Ok && !_host.ImportRoomState(foreign).Ok && _host.Revision == 0, "a creation whose source does not match its hash, or another manifest's state, is refused");
        _world.QueueFree();
        await Frames(3);
    }

    // ---- saves of a re-exported room ----

    private async Task TestMigrationBetweenManifests()
    {
        // The test room again, played in: a block on the floor, a lamp on the box, the box and the lamp protected.
        await NewWorld(_room, CommandHost.SavePathFor(_room), sweep: false);
        var block = Send(Command("block", "creation.place", new JsonObject { ["source"] = Source("Block"), ["placement"] = Placement(-1.5, 0, 0.9) }), Player)["created"]?[0]?.GetValue<string>() ?? "";
        var onBox = Send(Command("on-box", "creation.place", new JsonObject { ["source"] = Source("Box lamp"), ["placement"] = Placement(1.1, 0.3, 0.2, "obj:box") }), Player)["created"]?[0]?.GetValue<string>() ?? "";
        Check(block.Length > 0 && onBox.Length > 0, "a block on the floor and a lamp on the box");
        Check(Ok(Send(Command("lock-all", "protect.lock", new JsonObject { ["targets"] = new JsonArray(block, onBox, "obj:box") }, expectedRevision: _host.Revision), Player)), "the player protects both and the box");
        Check(Ok(Send(Command(NextId("note"), "journal.note", new JsonObject { ["text"] = "Building by the door." }), Companion)), "the companion writes a note");
        var revision = _host.Revision;
        var blockRevision = Summaries()[block]["revision"]!.GetValue<int>();
        var oldPath = CommandHost.SavePathFor(_room);
        var oldBytes = System.IO.File.ReadAllBytes(ProjectSettings.GlobalizePath(oldPath));
        _world.QueueFree();
        await Frames(3);

        // The room re-exported with the box moved north: a new manifest, so a new save folder.
        var moved = ReExport(new Vector3(1.1f, 0, 0.2f), new Vector3(1.1f, 0, -0.6f));
        Check(moved.ManifestSha256 != _room.ManifestSha256, "the re-exported room has a new manifest");
        await NewWorld(moved, CommandHost.SavePathFor(moved), sweep: false);
        Check(_host.Revision == 0 && CreationIds().Count == 0 && _host.SaveNotice.Length > 0, "nothing loads by itself; the player is told about the earlier save");
        var candidate = _host.MigrationCandidate();
        Check(candidate == oldPath, "the earlier save is offered: " + candidate);
        Check(!_host.MigrateFrom($"{TestRoot}/elsewhere/inventions.json").Ok, "only the offered save can be brought over");
        var outcome = _host.MigrateFrom(candidate!);
        Check(outcome.Ok && outcome.Kept.SequenceEqual(new[] { block }) && outcome.LeftBehind.SequenceEqual(new[] { onBox }),
            "the block comes over; the lamp, whose box moved away, is listed and left behind: " + outcome.Message);
        var entities = Summaries();
        Check(entities.ContainsKey(block) && !entities.ContainsKey(onBox) && entities[block]["revision"]!.GetValue<int>() == blockRevision && blockRevision == 2 && _host.Revision == revision,
            "the block keeps its id and revision, and the room its revision");
        Check(entities[block]["protected"]!.GetValue<bool>() && entities["obj:box"]["protected"]!.GetValue<bool>(), "locks on what still exists carry over (the block, the box)");
        Check(Node(_world, "obj:box")!.GlobalPosition.DistanceTo(new Vector3(1.1f, 0, -0.6f)) < 1e-4f, "objects stand where the new manifest puts them");
        Check(_host.ExportJournal()["notes"]!.AsArray().Count == 1 && _host.ExportDiscovered()["levels"]!.AsArray().Count == 0, "the journal comes over; the discovered map starts blank");
        var replay = Send(Command("block", "creation.place", new JsonObject { ["source"] = Source("Block"), ["placement"] = Placement(-1.5, 0, 0.9) }), Player);
        Check(replay["replayed"]?.GetValue<bool>() == true && Code(Send(Command("block", "creation.place", new JsonObject { ["source"] = Source("Other"), ["placement"] = Placement(-1.0, 0, 0.9) }), Player)) == "action_id_conflict",
            "the ledger starts compacted: an old action id replays and refuses conflicting reuse");
        Check(System.IO.File.ReadAllBytes(ProjectSettings.GlobalizePath(oldPath)).SequenceEqual(oldBytes), "the earlier save is never modified");
        Check(_host.MigrationCandidate() == null && !_host.MigrateFrom(candidate!).Ok, "once the room has its own save, nothing is offered again");
        Dump("room_state_migrated.json", _host.ExportRoomState()!);
        _world.QueueFree();
        await Frames(3);
        await NewWorld(moved, CommandHost.SavePathFor(moved), sweep: false);
        Check(_host.Authority.Call("is_ready").AsBool() && Summaries().ContainsKey(block), "the migrated room's own save loads");
        _world.QueueFree();
        await Frames(3);
    }

    /// <summary>The test room copied to a user folder with one object's place changed: a re-exported room, a new manifest hash.</summary>
    private RoomData ReExport(Vector3 from, Vector3 to)
    {
        var source = ProjectSettings.GlobalizePath(RoomWorld.DefaultRoom);
        var target = ProjectSettings.GlobalizePath($"{TestRoot}/rooms/test_room");
        foreach (var file in System.IO.Directory.GetFiles(source, "*", System.IO.SearchOption.AllDirectories).Where(f => !f.EndsWith(".import", StringComparison.Ordinal) && !f.EndsWith(".uid", StringComparison.Ordinal)))
        {
            var destination = System.IO.Path.Combine(target, System.IO.Path.GetRelativePath(source, file));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
            System.IO.File.Copy(file, destination, true);
        }
        var manifest = System.IO.Path.Combine(target, "room.json");
        var text = System.IO.File.ReadAllText(manifest);
        var old = $"\"id\": \"obj:box\",\n      \"asset\": \"objects/box_proxy/asset.json\",\n      \"transform\": {{\n        \"position_m\": [\n          {from.X:0.0###},\n          0.0,\n          {from.Z:0.0###}";
        var replacement = $"\"id\": \"obj:box\",\n      \"asset\": \"objects/box_proxy/asset.json\",\n      \"transform\": {{\n        \"position_m\": [\n          {to.X:0.0###},\n          0.0,\n          {to.Z:0.0###}";
        Check(text.Contains(old, StringComparison.Ordinal), "the room manifest has the box where the test expects it");
        System.IO.File.WriteAllBytes(manifest, System.Text.Encoding.UTF8.GetBytes(text.Replace(old, replacement)));
        return RoomData.Load($"{TestRoot}/rooms/test_room");
    }

    // ---- helpers ----

    private async Task NewWorld(RoomData room, string savePath, bool sweep)
    {
        _world = new Node3D { Name = "World" };
        AddChild(_world);
        _world.AddChild(RoomBuilder.Build(room));
        await Frames(2);
        _player = new SmallPlayerController { Name = "Player", ReadKeyboard = false, Position = room.SpawnFor("player").PositionM + Vector3.Up * 0.008f };
        _world.AddChild(_player);
        _companion = new CompanionAvatar { Name = "Companion", Position = room.SpawnFor("companion", room.SpawnFor("player")).PositionM + Vector3.Up * 0.008f };
        _world.AddChild(_companion);
        _companion.BindPlayer(_player);
        _host = CommandHost.Create(_world, room, _player, _companion, null, savePath);
        _host.Clock = () => _now;
        _host.TeamSightIntervalS = sweep ? 0.25 : 0;
        await Frames(4);
    }

    private Dictionary<string, Transform3D> SceneOf() =>
        _world.GetNode("Room/Objects").GetChildren().OfType<Node3D>().ToDictionary(n => n.GetMeta("entity_id").AsString(), n => n.GlobalTransform);

    private static Node3D? Node(Node world, string id) => world.GetNode("Room/Objects").GetChildren().OfType<Node3D>().FirstOrDefault(n => n.GetMeta("entity_id").AsString() == id);

    private List<string> CreationIds() => _host.Runtime.Get("instances").AsGodotDictionary().Keys.Select(k => k.AsString()).OrderBy(k => k, StringComparer.Ordinal).ToList();

    private Dictionary<string, JsonObject> Summaries() => _host.Entities().ToDictionary(e => e["id"]!.GetValue<string>(), e => e);

    private static JsonObject Unsessioned(JsonObject state)
    {
        var copy = (JsonObject)state.DeepClone();
        copy.Remove("session");
        return copy;
    }

    private void Dump(string name, JsonObject document)
    {
        if (_dump != null) System.IO.File.WriteAllBytes(System.IO.Path.Combine(_dump, name), CanonicalJson.Bytes(document));
    }

    private string NextId(string prefix) => $"{prefix}-{++_ids}";

    private JsonObject Command(string actionId, string op, JsonObject args, int? expectedRevision = null)
    {
        var command = new JsonObject { ["schema"] = "enfractal.command", ["version"] = 1, ["action_id"] = actionId, ["room_id"] = "test_room", ["op"] = op, ["args"] = args };
        if (expectedRevision != null) command["expected_revision"] = expectedRevision;
        return command;
    }

    private JsonObject Send(JsonObject message, string principal)
    {
        _now += TimeSpan.FromMilliseconds(50);
        return JsonNode.Parse(_host.Handle(message.ToJsonString(), principal))!.AsObject();
    }

    private static JsonObject Source(string name) => new()
    {
        ["schema"] = "enfractal.creation", ["version"] = 1, ["name"] = name, ["seed"] = 1, ["mount"] = "ground",
        ["parts"] = new JsonArray(new JsonObject
        {
            ["id"] = "base", ["shape"] = "box", ["position_m"] = new JsonArray(0, 0.05, 0), ["rotation_deg"] = new JsonArray(0, 0, 0),
            ["size_m"] = new JsonArray(0.1, 0.1, 0.1), ["material"] = "wood",
        }),
        ["nodes"] = new JsonArray(new JsonObject { ["id"] = "use", ["op"] = "interact", ["part_id"] = "base", ["params"] = new JsonObject() }),
        ["edges"] = new JsonArray(),
    };

    private static JsonObject Placement(double x, double y, double z, string? on = null)
    {
        var placement = new JsonObject { ["position_m"] = new JsonArray(x, y, z) };
        if (on != null) placement["on"] = on;
        return placement;
    }

    private static JsonObject Turned(double x, double y, double z) =>
        new() { ["position_m"] = new JsonArray(x, y, z), ["rotation"] = new JsonArray(0, Math.Sqrt(0.5), 0, Math.Sqrt(0.5)) };

    private static bool Ok(JsonObject result) => result["ok"]!.GetValue<bool>();
    private static string? Code(JsonObject result) => result["error"]?["code"]?.GetValue<string>();
    private static string Canonical(JsonNode? node) => node == null ? "" : CanonicalJson.Text(node);

    private void RemoveAll()
    {
        CommandHost.SaveRoot = CommandHost.DefaultSaveRoot;
        var directory = ProjectSettings.GlobalizePath(TestRoot);
        if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(bool condition, string label)
    {
        _checks++;
        if (condition) return;
        _failures++;
        GD.PushError("Rebuild: " + label);
    }
}
