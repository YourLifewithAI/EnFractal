using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnFractal.Native.Look;
using EnFractal.Native.Room;
using EnFractal.Native.Sandbox;

namespace EnFractal.Native.Kernel;

/// <summary>
/// P6, the world as data (Run 2): room state in the contract's form (contracts/room-state.schema.json), written from the
/// authority's state and read back into a room built from nothing; and the migration of a save between room manifests
/// (docs/engine/phase3/command-host.md), one step the player confirms, never automatic.
/// - Export: objects play changed (their pose, revision and lock), creations (source, its hash, pose, owner, lock), the
///   two avatars' identities, the durable receipts as contract results, the journal and the discovered map. What the
///   contract has no field for (compacted receipts, checkpoints, the companion's role, the next creation number, the open
///   tasks' goals) travels in x_play_ extensions, so a rebuild loses nothing.
/// - Import: into a host whose room has no play yet and the same manifest. The state becomes the authority's save envelope
///   and goes through the same load as a save (poses checked against the room, creations recompiled and re-placed, the
///   team block checked), then is saved; the scene is derived from it as from any load.
/// </summary>
public partial class CommandHost
{
    public const string RoomStateBuild = "enfractal-native";

    /// <summary>Room state now, in the contract's form. Null while the save is not loaded.</summary>
    public JsonObject? ExportRoomState()
    {
        if (!Authority.Call("is_ready").AsBool()) return null;
        var envelope = Normalized(KernelJson.ToJson(Authority.Call("export_envelope")))!;
        var entities = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
        var locks = envelope["locks"]!.AsObject();
        var revisions = envelope["entity_revisions"]!.AsObject();
        var poses = envelope["object_poses"]?.AsObject() ?? new JsonObject();
        var receipts = envelope["receipts"]!.AsObject();
        JsonObject Protection(string id) => locks[id] is JsonObject held
            ? new JsonObject { ["locked"] = true, ["locked_by"] = held["locked_by"]!.GetValue<string>(), ["locked_revision"] = held["locked_revision"]!.GetValue<int>() }
            : new JsonObject { ["locked"] = false };
        foreach (var id in revisions.Select(p => p.Key).Concat(locks.Select(p => p.Key)).Concat(poses.Select(p => p.Key)).Where(i => i.StartsWith("obj:", StringComparison.Ordinal)).Distinct())
        {
            var item = Room.Objects.First(o => o.Id == id);
            var entity = new JsonObject
            {
                ["id"] = id, ["kind"] = "object", ["revision"] = Math.Max(1, revisions[id]?.GetValue<int>() ?? 1),
                ["provenance"] = new JsonObject { ["kind"] = item.Asset.ProvenanceKind, ["created_by"] = "system:room", ["created_revision"] = 0 },
                ["protection"] = Protection(id),
            };
            if (poses[id] is JsonObject pose) entity["transform"] = new JsonObject { ["position_m"] = pose["position_m"]!.DeepClone(), ["rotation"] = pose["rotation"]!.DeepClone() };
            entities[id] = entity;
        }
        foreach (var node in envelope["instances"]!.AsArray())
        {
            var instance = node!.AsObject();
            var id = instance["id"]!.GetValue<string>();
            var owner = instance["owner_id"]!.GetValue<string>();
            var half = instance["yaw_deg"]!.GetValue<double>() * Math.PI / 360.0;
            var created = receipts.Select(r => r.Value!["receipt"]!).FirstOrDefault(r => r["created"]!.AsArray().Any(c => c!.GetValue<string>() == id))?["revision"]?.GetValue<int>() ?? 0;
            entities[id] = new JsonObject
            {
                ["id"] = id, ["kind"] = "creation", ["revision"] = instance["revision"]!.GetValue<int>(),
                ["transform"] = new JsonObject { ["position_m"] = instance["position_m"]!.DeepClone(), ["rotation"] = new JsonArray(0, Math.Sin(half), 0, Math.Cos(half)) },
                ["creation"] = new JsonObject { ["source"] = instance["source"]!.DeepClone(), ["source_sha256"] = CanonicalJson.Sha256Hex(CanonicalJson.Bytes(instance["source"]!)) },
                ["provenance"] = new JsonObject { ["kind"] = owner == CompanionPrincipal ? "ai_created" : "player_created", ["created_by"] = owner, ["created_revision"] = created },
                ["protection"] = Protection(id),
            };
        }
        foreach (var (id, body, role, name) in new (string, SmallPlayerController?, string, string)[] { (PlayerAvatar, Player, "player", "Player"), (CompanionAvatarId, Companion, "companion", Companion?.CompanionName ?? "Companion") })
        {
            if (body == null || !IsInstanceValid(body)) continue;
            entities[id] = new JsonObject
            {
                ["id"] = id, ["kind"] = "avatar", ["revision"] = 1,
                ["avatar"] = new JsonObject { ["role"] = role, ["display_name"] = KernelJson.DisplayText(name, 40), ["appearance"] = new JsonObject { ["color"] = "#" + body.AppearanceColor.ToHtml(false) } },
                ["provenance"] = new JsonObject { ["kind"] = "hand_authored", ["created_by"] = "system:room", ["created_revision"] = 0 },
                ["protection"] = new JsonObject { ["locked"] = false },
            };
        }
        var results = new JsonObject();
        foreach (var (key, record) in receipts.OrderBy(p => p.Key, StringComparer.Ordinal))
            results[key] = new JsonObject
            {
                ["fingerprint"] = record!["fingerprint"]!.GetValue<string>(),
                ["result"] = Durable(Authority.Call("receipt_for", record["principal"]!.GetValue<string>(), record["action_id"]!.GetValue<string>()).AsGodotDictionary(), replayed: false),
            };
        var style = Style ?? StylePreset.Resolve(RoomWorld.DefaultStyleId, RoomWorld.DefaultStyleVersion);
        return new JsonObject
        {
            ["schema"] = "enfractal.room_state", ["version"] = 1, ["room_id"] = Room.RoomId,
            ["room_pin"] = new JsonObject { ["manifest_sha256"] = Room.ManifestSha256 },
            ["style_pin"] = new JsonObject { ["preset_id"] = style.PresetId, ["preset_version"] = style.PresetVersion, ["preset_sha256"] = style.Sha256 },
            ["rules"] = new JsonObject { ["command_version"] = 1, ["compiler_version"] = 1 },
            ["store_revision"] = Revision,
            ["session"] = new JsonObject
            {
                ["session_id"] = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(), ["written_utc"] = Now(), ["build"] = RoomStateBuild,
            },
            ["entities"] = new JsonObject(entities.Select(p => KeyValuePair.Create(p.Key, p.Value))),
            ["receipts"] = results,
            ["journal"] = ExportJournal(),
            ["discovered"] = ExportDiscovered(),
            ["extensions"] = new JsonObject
            {
                ["x_play_companion_role"] = envelope["roles"]![CompanionPrincipal]!.GetValue<string>(),
                ["x_play_next_creation"] = envelope["next_id"]!.GetValue<int>(),
                ["x_play_compacted"] = envelope["compacted"]?.DeepClone() ?? new JsonObject(),
                ["x_play_checkpoints"] = envelope["checkpoints"]?.DeepClone() ?? new JsonArray(),
                ["x_play_task_goals"] = new JsonObject(_taskGoals.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, (JsonNode?)JsonValue.Create(p.Value)))),
            },
        };
    }

    /// <summary>The outcome of a rebuild or a migration: whether it happened, and what the player is told.</summary>
    public sealed record RoomStateOutcome(bool Ok, string Message, IReadOnlyList<string> Kept, IReadOnlyList<string> LeftBehind);

    private static RoomStateOutcome Refused(string message) => new(false, message, Array.Empty<string>(), Array.Empty<string>());

    /// <summary>
    /// Rebuild this room from room state, into a host whose room has no play yet (nothing saved, revision 0) and the same
    /// manifest. Everything goes through the authority's own load, then is saved; a state that does not load leaves the
    /// room not ready, as a save that does not load does.
    /// </summary>
    public RoomStateOutcome ImportRoomState(JsonObject given)
    {
        // Read as strict JSON reads it, whatever way it was built.
        if (Normalized(given) is not { } state) return Refused("That is not room state.");
        if (!Authority.Call("is_ready").AsBool() || Revision != 0 || Authority.Call("receipt_count").AsInt32() != 0)
            return Refused("Only a room with no play yet is rebuilt from room state.");
        if (state["schema"]?.GetValue<string>() != "enfractal.room_state" || state["version"]?.GetValue<int>() != 1 || state["room_id"]?.GetValue<string>() != Room.RoomId)
            return Refused("That is not room state for this room.");
        if (state["room_pin"]?["manifest_sha256"]?.GetValue<string>() != Room.ManifestSha256)
            return Refused("That room state was built on another version of this room; it migrates instead (MigrateFrom).");
        JsonObject envelope;
        try { envelope = EnvelopeOf(state); }
        catch (Exception error) when (error is InvalidOperationException or FormatException or KeyNotFoundException or NullReferenceException or ArgumentException)
        {
            return Refused("That room state is incomplete: " + error.Message);
        }
        return LoadEnvelope(envelope, envelope["instances"]!.AsArray().Select(i => i!["id"]!.GetValue<string>()).ToList(), Array.Empty<string>(), "The room was rebuilt from its state.");
    }

    /// <summary>The authority's save envelope (version 5) for a room state of this room.</summary>
    private JsonObject EnvelopeOf(JsonObject state)
    {
        var extensions = state["extensions"] as JsonObject ?? new JsonObject();
        var instances = new JsonArray();
        var locks = new JsonObject();
        var revisions = new JsonObject();
        var poses = new JsonObject();
        foreach (var (id, node) in state["entities"]!.AsObject())
        {
            var entity = node!.AsObject();
            if (entity["removed"]?.GetValue<bool>() == true) continue;
            var kind = entity["kind"]!.GetValue<string>();
            if (entity["protection"]?["locked"]?.GetValue<bool>() == true)
                locks[id] = new JsonObject { ["locked_by"] = entity["protection"]!["locked_by"]!.GetValue<string>(), ["locked_revision"] = entity["protection"]!["locked_revision"]!.GetValue<int>() };
            if (kind == "object")
            {
                var item = Room.Objects.FirstOrDefault(o => o.Id == id) ?? throw new InvalidOperationException(id + " is not in this room");
                revisions[id] = entity["revision"]!.GetValue<int>();
                if (entity["transform"] is not JsonObject transform) continue;
                var position = ReadNodeVector(transform["position_m"]!);
                var r = transform["rotation"]!.AsArray().Select(v => (float)v!.GetValue<double>()).ToArray();
                var bounds = SandboxPhysics.Bounds(position, new Quaternion(r[0], r[1], r[2], r[3]).Normalized(), item.Asset.DimensionsM * item.Scale);
                poses[id] = new JsonObject
                {
                    ["position_m"] = transform["position_m"]!.DeepClone(), ["rotation"] = transform["rotation"]!.DeepClone(),
                    ["bounds"] = new JsonObject { ["min_m"] = ExactNode(bounds.Position), ["max_m"] = ExactNode(bounds.End) },
                };
            }
            else if (kind == "creation")
            {
                var source = entity["creation"]!["source"]!;
                if (CanonicalJson.Sha256Hex(CanonicalJson.Bytes(source)) != entity["creation"]!["source_sha256"]!.GetValue<string>())
                    throw new InvalidOperationException(id + "'s source does not match its hash");
                var q = entity["transform"]!["rotation"]!.AsArray().Select(v => v!.GetValue<double>()).ToArray();
                if (Math.Abs(q[0]) > 1e-4 || Math.Abs(q[2]) > 1e-4) throw new InvalidOperationException(id + " does not stand upright");
                var yaw = Math.Round(2.0 * Math.Atan2(q[1], q[3]) * 180.0 / Math.PI, 9);
                if (yaw > 180.0) yaw -= 360.0;
                if (yaw <= -180.0) yaw += 360.0;
                instances.Add(new JsonObject
                {
                    ["id"] = id, ["owner_id"] = entity["provenance"]!["created_by"]!.GetValue<string>(), ["revision"] = entity["revision"]!.GetValue<int>(),
                    ["source"] = source.DeepClone(), ["position_m"] = entity["transform"]!["position_m"]!.DeepClone(), ["yaw_deg"] = yaw,
                });
            }
        }
        var receipts = new JsonObject();
        foreach (var (key, node) in state["receipts"]!.AsObject())
        {
            var result = node!["result"]!.AsObject();
            var op = result["op"]!.GetValue<string>();
            string Id(string list) => result[list]?.AsArray().Select(x => x!.GetValue<string>()).FirstOrDefault(x => x.StartsWith("creation:", StringComparison.Ordinal)) ?? "";
            var receipt = new JsonObject
            {
                ["ok"] = true, ["instance_id"] = op == "creation.place" ? Id("created") : Id("affected"), ["revision"] = result["revision"]!.GetValue<int>(),
                ["permission_revision"] = 0, ["replayed"] = false,
                ["affected"] = result["affected"]?.DeepClone() ?? new JsonArray(), ["created"] = result["created"]?.DeepClone() ?? new JsonArray(),
            };
            if (op == "journal.note") receipt["entry_id"] = result["data"]!["entry_id"]!.GetValue<string>();
            receipts[key] = new JsonObject
            {
                ["principal"] = result["principal"]!.GetValue<string>(), ["action_id"] = result["action_id"]!.GetValue<string>(), ["fingerprint"] = node["fingerprint"]!.GetValue<string>(),
                ["receipt"] = receipt,
                ["meta"] = new JsonObject { ["op"] = op, ["at_utc"] = result["at_utc"]?.GetValue<string>() ?? "", ["approved_by"] = result["approved_by"]?.GetValue<string>() ?? "" },
            };
        }
        var next = extensions["x_play_next_creation"]?.GetValue<int>() ??
            instances.Select(i => int.Parse(i!["id"]!.GetValue<string>()["creation:".Length..], System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max() + 1;
        var team = new JsonObject
        {
            ["journal"] = state["journal"]?.DeepClone() ?? new JsonObject { ["open_tasks"] = new JsonArray(), ["history"] = new JsonArray(), ["notes"] = new JsonArray() },
            ["discovered"] = state["discovered"]?.DeepClone() ?? new JsonObject { ["cell_m"] = DiscoverCellM, ["levels"] = new JsonArray(), ["entities"] = new JsonObject() },
            ["task_goals"] = extensions["x_play_task_goals"]?.DeepClone() ?? new JsonObject(),
        };
        return new JsonObject
        {
            ["schema"] = "enfractal.creation-world", ["version"] = 5, ["compiler_version"] = 1, ["style_version"] = "painterly_v1",
            ["room_pin"] = new JsonObject { ["room_id"] = Room.RoomId, ["manifest_sha256"] = Room.ManifestSha256 },
            ["revision"] = state["store_revision"]!.GetValue<int>(), ["permission_revision"] = 0, ["next_id"] = next,
            ["roles"] = new JsonObject { [PlayerPrincipal] = "owner", [CompanionPrincipal] = extensions["x_play_companion_role"]?.GetValue<string>() ?? "editor" },
            ["consent"] = new JsonObject { [PlayerPrincipal] = false, [CompanionPrincipal] = false },
            ["instances"] = instances, ["receipts"] = receipts,
            ["compacted"] = extensions["x_play_compacted"]?.DeepClone() ?? new JsonObject(),
            ["checkpoints"] = extensions["x_play_checkpoints"]?.DeepClone() ?? new JsonArray(),
            ["locks"] = locks, ["entity_revisions"] = revisions, ["object_poses"] = poses, ["team"] = team,
        };
    }

    private static JsonArray ExactNode(Vector3 value) => new(Exact(value.X), Exact(value.Y), Exact(value.Z));

    /// <summary>Load an envelope through the authority's own checks, save it, and derive the scene and the team from it.</summary>
    private RoomStateOutcome LoadEnvelope(JsonObject envelope, IReadOnlyList<string> kept, IReadOnlyList<string> leftBehind, string message)
    {
        var loaded = Authority.Call("load_envelope", KernelJson.ToVariant(envelope)).AsGodotDictionary();
        if (!loaded["ok"].AsBool())
        {
            ApplyObjectPoses(ObjectPoses());
            return Refused(loaded.ContainsKey("message") ? loaded["message"].AsString() : "The state did not load.");
        }
        LoadTeam(envelope["team"] as JsonObject, closeOpenTasks: false);
        // Persist it: the host's own save now holds the rebuilt (or migrated) room.
        var saved = Authority.Call("set_team", KernelJson.ToVariant(ExportTeam(_notes))).AsGodotDictionary();
        if (!saved["ok"].AsBool()) return Refused("The rebuilt room could not be saved: " + saved["message"].AsString());
        Runtime.Call("refresh_now");
        ApplyObjectPoses(ObjectPoses());
        SaveNotice = "";
        return new(true, message, kept, leftBehind);
    }

    // ---- saves of a re-exported room: migration (docs/engine/phase3/command-host.md) ----

    /// <summary>The newest save of this room under another manifest, when this room has none of its own yet; null otherwise.</summary>
    public string? MigrationCandidate()
    {
        if (Godot.FileAccess.FileExists(SavePath) || !Authority.Call("is_ready").AsBool() || Revision != 0) return null;
        var file = SavePath.GetFile();
        var manifestDirectory = SavePath.GetBaseDir();
        var roomDirectory = manifestDirectory.GetBaseDir();
        using var directory = DirAccess.Open(roomDirectory);
        if (directory == null) return null;
        return directory.GetDirectories()
            .Where(name => name != manifestDirectory.GetFile() && ManifestPrefix.IsMatch(name) && Godot.FileAccess.FileExists($"{roomDirectory}/{name}/{file}"))
            .Select(name => $"{roomDirectory}/{name}/{file}")
            .OrderByDescending(path => Godot.FileAccess.GetModifiedTime(path)).ThenBy(path => path, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>
    /// The player's one confirmed step: bring creations over from a save of this room under another manifest. Each saved
    /// creation is recompiled and its placement re-checked against this room (bounds, the surface under it at the height it
    /// stood, the ceiling); those that pass keep their ids and revisions, the rest are listed and stay in the old file.
    /// Locks on objects and creations that still exist carry over; the others drop. Moved objects stand where this manifest
    /// puts them, and the discovered map starts blank (the room changed); the journal carries over. The receipt ledger
    /// starts compacted at the migrated revision, so old action ids still replay and still refuse conflicting reuse. The
    /// old file is never modified.
    /// </summary>
    public RoomStateOutcome MigrateFrom(string oldSavePath)
    {
        if (MigrationCandidate() is not { } candidate || candidate != oldSavePath) return Refused("There is no earlier save of this room to bring over.");
        JsonObject old;
        try
        {
            using var document = CanonicalJson.Parse(Godot.FileAccess.GetFileAsString(oldSavePath));
            old = JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
        }
        catch (Exception) { return Refused("The earlier save is not readable; it is kept as it was."); }
        if (old["schema"]?.GetValue<string>() != "enfractal.creation-world" || old["room_pin"]?["room_id"]?.GetValue<string>() != Room.RoomId ||
            old["version"]?.GetValue<int>() is not (2 or 3 or 4 or 5))
            return Refused("The earlier save is not a save of this room; it is kept as it was.");
        var revision = old["revision"]!.GetValue<int>();
        var kept = new List<string>();
        var leftBehind = new List<string>();
        var instances = new JsonArray();
        foreach (var node in old["instances"]!.AsArray())
        {
            var instance = node!.AsObject();
            var id = instance["id"]!.GetValue<string>();
            var position = instance["position_m"]!.AsArray().Select(v => v!.GetValue<double>()).ToArray();
            var check = Authority.Call("preflight", instance["owner_id"]!.GetValue<string>(), KernelJson.ToVariant(instance["source"]), position[0], position[2],
                instance["yaw_deg"]!.GetValue<double>(), "", position[1], "", "").AsGodotDictionary();
            // It must stand where it stood: the same height (its support may have moved away in the new room).
            if (!check["ok"].AsBool() || Math.Abs(check["position_m"].AsGodotArray()[1].AsDouble() - position[1]) > 1e-5)
            {
                leftBehind.Add(id);
                continue;
            }
            instances.Add(instance.DeepClone());
            kept.Add(id);
        }
        var locks = new JsonObject();
        foreach (var (id, node) in old["locks"]?.AsObject() ?? new JsonObject())
            if (kept.Contains(id) || Room.Objects.Any(o => o.Id == id)) locks[id] = node!.DeepClone();
        var revisions = new JsonObject();
        foreach (var (id, node) in old["entity_revisions"]?.AsObject() ?? new JsonObject())
            if (Room.Objects.Any(o => o.Id == id) || Room.Shell.Any(p => p.Id == id)) revisions[id] = node!.DeepClone();
        foreach (var (id, _) in locks) if (id.StartsWith("obj:", StringComparison.Ordinal) && !revisions.ContainsKey(id)) revisions[id] = locks[id]!["locked_revision"]!.DeepClone();
        // The ledger starts compacted: every receipt replays and refuses conflicting reuse by revision and fingerprint prefix.
        var compacted = old["compacted"]?.DeepClone().AsObject() ?? new JsonObject();
        foreach (var (key, node) in old["receipts"]!.AsObject())
            compacted[key] = new JsonArray(node!["receipt"]!["revision"]!.GetValue<int>(), node["fingerprint"]!.GetValue<string>()[..16]);
        var checkpoints = old["checkpoints"]?.DeepClone().AsArray() ?? new JsonArray();
        var number = checkpoints.Count == 0 ? 1 : int.Parse(checkpoints[^1]!["id"]!.GetValue<string>()[2..], System.Globalization.CultureInfo.InvariantCulture) + 1;
        checkpoints.Add(new JsonObject { ["id"] = $"cp{number:0000}", ["revision"] = revision, ["label"] = "Brought over from an earlier version of this room" });
        while (checkpoints.Count > 16) checkpoints.RemoveAt(0);
        var oldTeam = old["team"] as JsonObject;
        var team = new JsonObject
        {
            ["journal"] = oldTeam?["journal"]?.DeepClone() ?? new JsonObject { ["open_tasks"] = new JsonArray(), ["history"] = new JsonArray(), ["notes"] = new JsonArray() },
            ["discovered"] = new JsonObject { ["cell_m"] = DiscoverCellM, ["levels"] = new JsonArray(), ["entities"] = new JsonObject() },
            ["task_goals"] = oldTeam?["task_goals"]?.DeepClone() ?? new JsonObject(),
        };
        var envelope = new JsonObject
        {
            ["schema"] = "enfractal.creation-world", ["version"] = 5, ["compiler_version"] = 1, ["style_version"] = "painterly_v1",
            ["room_pin"] = new JsonObject { ["room_id"] = Room.RoomId, ["manifest_sha256"] = Room.ManifestSha256 },
            ["revision"] = revision, ["permission_revision"] = old["permission_revision"]!.DeepClone(), ["next_id"] = old["next_id"]!.DeepClone(),
            ["roles"] = old["roles"]!.DeepClone(), ["consent"] = new JsonObject { [PlayerPrincipal] = false, [CompanionPrincipal] = false },
            ["instances"] = instances, ["receipts"] = new JsonObject(), ["compacted"] = compacted, ["checkpoints"] = checkpoints,
            ["locks"] = locks, ["entity_revisions"] = revisions, ["object_poses"] = new JsonObject(), ["team"] = team,
        };
        var message = leftBehind.Count == 0 ? $"Brought over {kept.Count} creations from the earlier version of this room."
            : $"Brought over {kept.Count} creations; {leftBehind.Count} no longer fit this version of the room and stay in the earlier save: {string.Join(", ", leftBehind)}.";
        return LoadEnvelope(envelope, kept, leftBehind, message);
    }
}
