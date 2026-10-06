using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EnFractal.Native.Look;
using EnFractal.Native.Room;

namespace EnFractal.Native.Kernel;

/// <summary>A command the host is holding for the player's click. Never created from a request field.</summary>
public sealed class PendingApproval
{
    public string RequestId { get; init; } = "";
    public string Principal { get; init; } = "";
    public string ActionId { get; init; } = "";
    public string Fingerprint { get; init; } = "";
    public string Op { get; init; } = "";
    public string Reason { get; init; } = "";
    public string CommandText { get; init; } = "";
    public DateTime Created { get; init; }
    public DateTime Expires { get; init; }
    public Dictionary<string, int> Touched { get; init; } = new();
    /// <summary>pending, approved, denied or expired (an approval also lapses when what it touches changes).</summary>
    public string State { get; internal set; } = "pending";
    public JsonObject? Result { get; internal set; }
    internal JsonObject Held { get; set; } = new();
}

/// <summary>
/// The room's single command path (contracts/game-command.schema.json). Manual controls, the companion
/// adapter and tests send enfractal.command and enfractal.query; the host answers enfractal.result.
/// The principal comes from the trusted caller, never from the message. The host fingerprints the command
/// as received (SHA-256 of canonical JSON), checks expected_revision and expected_entities itself, never
/// refuses goal.stop or effect.stop on revisions, keeps transient receipts for goals, effects and
/// activations, refuses protect.unlock from the companion, and holds companion changes to the player's
/// creations for the player's click. Durable receipts live with the state in the creation authority.
/// </summary>
public partial class CommandHost : Node
{
    public const string PlayerPrincipal = "player:local";
    public const string CompanionPrincipal = "companion:local";
    public const string PlayerAvatar = "avatar:player";
    public const string CompanionAvatarId = "avatar:companion";
    public const int MaxMessageBytes = 65536;
    public const int MaxResultBytes = 262144;
    public const int MaxTransientReceipts = 2048;
    public const int MaxPendingApprovals = 8;
    public const int CompanionMessagesPerSecond = 30;
    public static readonly TimeSpan ApprovalLifetime = TimeSpan.FromMinutes(5);
    public const string RuntimeScript = "res://scripts/invention_runtime.gd";

    private static readonly Regex ActionId = new(@"^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex Token = new(@"^[a-z][a-z0-9_-]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex EntityId = new(@"^(shell|obj|creation|avatar|effect|edit):[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex AvatarId = new(@"^avatar:[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex CreationId = new(@"^creation:[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex EffectId = new(@"^effect:[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex RequestIdPattern = new(@"^[a-f0-9]{32,64}$", RegexOptions.Compiled);
    private static readonly Regex ParamName = new(@"^[a-z][a-z0-9_]{0,31}$", RegexOptions.Compiled);
    private static readonly Regex UnsafeText = new("[\u0000-\u001F\u007F-\u009F\u200B-\u200F\u2028\u2029\u202A-\u202E\u2060-\u2069\uFEFF]", RegexOptions.Compiled);
    private static readonly HashSet<string> CommandOps = new()
    {
        "entity.grab", "entity.release", "entity.place", "entity.set_part", "entity.remove", "entity.transform",
        "creation.place", "creation.revise", "creation.activate", "protect.lock", "protect.unlock",
        "goal.set", "goal.stop", "effect.start", "effect.stop", "style.set", "room.checkpoint", "room.undo",
    };
    private static readonly HashSet<string> QueryOps = new()
    {
        "room.describe", "entities.list", "entity.inspect", "capabilities.list", "observe", "jobs.status", "receipt.lookup", "approval.status",
    };
    private static readonly HashSet<string> DestructiveOps = new() { "entity.remove", "entity.transform", "creation.revise", "protect.lock", "protect.unlock" };
    private static readonly HashSet<string> TransientOps = new() { "goal.set", "goal.stop", "effect.start", "effect.stop", "entity.grab", "entity.release", "creation.activate" };
    private static readonly HashSet<string> Affordances = new()
    {
        "walkable_top", "climbable", "sittable", "openable", "container", "soft", "breakable", "light_source", "switchable", "screen", "readable", "rideable", "hazard",
    };
    private static readonly string[] ReservedParams = { "principal", "action_id", "approval", "approval_id", "approved", "owner", "owner_id", "room_id", "actor", "grant", "role" };
    private static readonly string[] SupportedGoals = { "follow", "stay", "come", "look_at", "point_at" };

    public RoomData Room { get; private set; } = null!;
    public SmallPlayerController? Player { get; private set; }
    public CompanionAvatar? Companion { get; private set; }
    public StylePreset? Style { get; private set; }
    public Node3D Runtime { get; private set; } = null!;
    public GodotObject Authority { get; private set; } = null!;
    public string SavePath { get; private set; } = "";
    /// <summary>Test seam for approval expiry; the game uses the system clock.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;
    /// <summary>Raised when the host starts holding a command for the player.</summary>
    public event Action<PendingApproval>? ApprovalRequested;
    public IReadOnlyList<PendingApproval> PendingApprovals => _approvals.Values.Where(a => a.State == "pending").OrderBy(a => a.Created).ToList();

    private readonly Dictionary<string, (string Fingerprint, JsonObject Result)> _transient = new();
    private readonly Dictionary<string, PendingApproval> _approvals = new();
    private readonly Dictionary<string, string> _approvalByAction = new();
    private readonly Queue<DateTime> _companionCalls = new();
    private string _session = "";
    private int _counter;
    private PanelContainer? _prompt;
    private Label? _promptText;

    /// <summary>The integrator's one-line wiring: RoomWorld calls CommandHost.Attach(this) once its room, player and companion exist.</summary>
    public static CommandHost Attach(RoomWorld world) =>
        Create(world, world.Room, world.Player, world.Companion, world.Look?.Preset, SavePathFor(world.Room));

    public static CommandHost Create(Node parent, RoomData room, SmallPlayerController? player, CompanionAvatar? companion, StylePreset? style, string savePath)
    {
        var host = new CommandHost { Name = "CommandHost", Room = room, Player = player, Companion = companion, Style = style, SavePath = savePath };
        parent.AddChild(host);
        return host;
    }

    /// <summary>Saves are per room and per manifest: a re-exported room starts a fresh file instead of failing to load the old one.</summary>
    public static string SavePathFor(RoomData room) => $"user://saves/rooms/{room.RoomId}/{room.ManifestSha256[..16]}/inventions.json";

    /// <summary>The host attached under a room world, if any.</summary>
    public static CommandHost? Of(Node world) => world.GetNodeOrNull<CommandHost>("CommandHost");

    public override void _Ready()
    {
        _session = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        Runtime = (Node3D)GD.Load<GDScript>(RuntimeScript).New().AsGodotObject();
        Runtime.Name = "InventionRuntime";
        Runtime.Set("save_path", SavePath);
        Runtime.Call("configure", RoomDictionary(Room), Player!, Companion!);
        AddChild(Runtime);
        Authority = Runtime.Get("authority").AsGodotObject();
        Runtime.Set("command_sink", new Callable(this, MethodName.RuntimeCommand));
        BuildPrompt();
    }

    public override void _Process(double delta)
    {
        foreach (var approval in _approvals.Values) Expire(approval);
        if (_prompt == null || _promptText == null) return;
        var next = PendingApprovals.FirstOrDefault();
        _prompt.Visible = next != null;
        if (next != null) _promptText.Text = next.Reason;
    }

    public int Revision => Authority.Get("revision").AsInt32();
    private int PermissionRevision => Authority.Get("permission_revision").AsInt32();
    private string Now() => KernelJson.Utc(Clock());

    // ---- entry points ----

    /// <summary>Handle one enfractal.command or enfractal.query sent by a trusted adapter for principal; returns canonical enfractal.result text.</summary>
    public string Handle(string message, string principal) => CanonicalJson.Text(HandleObject(message, principal));

    public JsonObject HandleObject(string message, string principal)
    {
        if (principal is not (PlayerPrincipal or CompanionPrincipal))
            return Fail("invalid", "system:host", null, null, new Refusal("principal_unknown", "The host did not admit this caller."));
        if (message.Length > MaxMessageBytes * 4)
            return Fail("invalid", principal, null, null, new Refusal("request_invalid", "The message is larger than 64 KiB."));
        JsonDocument document;
        try { document = CanonicalJson.Parse(message); }
        catch (CanonicalJsonException error) { return Fail("invalid", principal, null, null, new Refusal("request_invalid", "The message is not strict JSON: " + error.Message)); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Fail("invalid", principal, null, null, new Refusal("request_invalid", "A message is a JSON object."));
            var canonical = CanonicalJson.Bytes(root);
            var schema = Str(root, "schema");
            JsonObject result;
            if (schema == "enfractal.command") result = HandleCommand(root, canonical, principal);
            else if (schema == "enfractal.query") result = HandleQuery(root, canonical, principal);
            else result = Fail("invalid", principal, null, null, new Refusal("request_invalid", "Send an enfractal.command or an enfractal.query.", "$.schema"));
            if (CanonicalJson.Bytes(result).Length > MaxResultBytes)
                result = Fail(Op(root), principal, null, null, new Refusal("internal_error", "The answer would be larger than 256 KiB."));
            return result;
        }
    }

    /// <summary>The GDScript runtime's command sink: manual edits from the invention editor, always as the player.</summary>
    public Godot.Collections.Dictionary RuntimeCommand(Godot.Collections.Dictionary command)
    {
        string text;
        try { text = CanonicalJson.Text(KernelJson.ToJson(command)); }
        catch (CanonicalJsonException error) { text = "{\"schema\":\"invalid\",\"reason\":" + JsonSerializer.Serialize(error.Message) + "}"; }
        return KernelJson.ToVariant(HandleObject(text, PlayerPrincipal)).AsGodotDictionary();
    }

    /// <summary>Manual companion controls (HUD keys) as goal.set / goal.stop commands from the player.</summary>
    public JsonObject PlayerGoal(string goal, Vector3? point = null)
    {
        var args = new JsonObject { ["actor"] = CompanionAvatarId };
        var op = goal == "stop" ? "goal.stop" : "goal.set";
        if (op == "goal.set")
        {
            args["goal"] = goal;
            if (point is { } at) args["position_m"] = KernelJson.Vector(at);
        }
        var command = new JsonObject
        {
            ["schema"] = "enfractal.command", ["version"] = 1, ["action_id"] = $"hud-{_session}-{++_counter}",
            ["room_id"] = Room.RoomId, ["op"] = op, ["args"] = args,
        };
        return HandleObject(CanonicalJson.Text(command), PlayerPrincipal);
    }

    /// <summary>The player's click: commit the held command under its original principal, recorded as approved by the player.</summary>
    public JsonObject Approve(string requestId)
    {
        if (!_approvals.TryGetValue(requestId, out var approval) || approval.State != "pending")
            return Fail("approval.status", PlayerPrincipal, null, null, new Refusal("target_not_found", "There is no waiting request with that id."));
        Expire(approval);
        if (approval.State == "expired")
            return Fail(approval.Op, approval.Principal, approval.ActionId, null, new Refusal("approval_expired", "That request expired before it was approved."));
        if (approval.Touched.Any(t => EntityRevision(t.Key) != t.Value))
        {
            approval.State = "expired";
            approval.Result = Fail(approval.Op, approval.Principal, approval.ActionId, null, new Refusal("approval_mismatch", "What the request would change has changed since it was asked; it was not applied."));
            return approval.Result;
        }
        using var document = JsonDocument.Parse(approval.CommandText);
        var result = Execute(document.RootElement, approval.Principal, approval.Fingerprint, PlayerPrincipal);
        approval.State = "approved";
        approval.Result = result;
        return result;
    }

    public JsonObject Deny(string requestId)
    {
        if (!_approvals.TryGetValue(requestId, out var approval) || approval.State != "pending")
            return Fail("approval.status", PlayerPrincipal, null, null, new Refusal("target_not_found", "There is no waiting request with that id."));
        approval.State = "denied";
        approval.Result = Fail(approval.Op, approval.Principal, approval.ActionId, null, new Refusal("permission_denied", "The player declined this request."));
        return approval.Result;
    }

    // ---- commands ----

    private JsonObject HandleCommand(JsonElement root, byte[] canonical, string principal)
    {
        var op = Op(root);
        var actionId = root.TryGetProperty("action_id", out var a) && a.ValueKind == JsonValueKind.String && ActionId.IsMatch(a.GetString()!) ? a.GetString() : null;
        try
        {
            CheckEnvelope(root, command: true);
            if (canonical.Length > MaxMessageBytes) throw new Refusal("request_invalid", "A command is at most 65,536 bytes of canonical JSON.");
            RateLimit(principal);
            var fingerprint = CanonicalJson.Sha256Hex(canonical);
            var replay = Replay(principal, actionId!, fingerprint);
            if (replay != null) return replay;
            var held = HeldAnswer(principal, actionId!, fingerprint);
            if (held != null) return held;
            return Execute(root, principal, fingerprint, null);
        }
        catch (Refusal refusal) { return Fail(op, principal, actionId, null, refusal); }
    }

    private JsonObject Execute(JsonElement root, string principal, string fingerprint, string? approvedBy)
    {
        var op = Op(root);
        var actionId = Str(root, "action_id")!;
        try
        {
            var args = root.GetProperty("args");
            var preview = root.TryGetProperty("preview", out var p) && p.ValueKind == JsonValueKind.True;
            CheckArgs(op, args);
            if (op == "protect.unlock" && principal != PlayerPrincipal)
                throw new Refusal("permission_denied", "Only the player can unlock, directly in the game.", "$.op");
            var unsupported = Unsupported(op, args, principal);
            if (unsupported != null) throw new Refusal("unsupported_capability", unsupported, "$.op");
            if (op is not ("goal.stop" or "effect.stop")) CheckExpectations(root, op, args);
            if (approvedBy == null && !preview && principal == CompanionPrincipal && NeedsApproval(op, args, out var reason, out var touched))
                return Hold(root, principal, actionId, fingerprint, op, reason, touched);
            var meta = new Godot.Collections.Dictionary { ["fingerprint"] = fingerprint, ["op"] = op, ["at_utc"] = Now() };
            if (approvedBy != null) meta["approved_by"] = approvedBy;
            return op switch
            {
                "creation.place" or "creation.revise" => PlaceOrRevise(op, args, principal, actionId, meta, preview, approvedBy),
                "entity.remove" => Remove(args, principal, actionId, meta, preview, approvedBy),
                "protect.lock" or "protect.unlock" => Protect(op, args, principal, actionId, meta, preview),
                "creation.activate" => Activate(args, principal, actionId, fingerprint, meta, preview),
                "goal.set" => GoalSet(args, principal, actionId, fingerprint, preview),
                "goal.stop" => GoalStop(args, principal, actionId, fingerprint, preview),
                "effect.stop" => EffectStop(args, principal, actionId, fingerprint, preview),
                _ => throw new Refusal("unsupported_capability", "This operation is not available yet.", "$.op"),
            };
        }
        catch (Refusal refusal) { return Fail(op, principal, actionId, null, refusal); }
    }

    private JsonObject PlaceOrRevise(string op, JsonElement args, string principal, string actionId, Godot.Collections.Dictionary meta, bool preview, string? approvedBy)
    {
        var instanceId = "";
        Variant source;
        double x, y, z, yaw;
        var on = "";
        if (op == "creation.revise")
        {
            instanceId = Str(args, "target")!;
            var current = Instance(instanceId) ?? throw new Refusal("target_not_found", "That is not in this room.", "$.args.target");
            var position = current["position_m"].AsGodotArray();
            (x, y, z, yaw) = (position[0].AsDouble(), position[1].AsDouble(), position[2].AsDouble(), current["yaw_deg"].AsDouble());
            source = args.TryGetProperty("source", out var revised) ? KernelJson.ToVariant(revised) : current["source"];
            if (args.TryGetProperty("placement", out var placement)) (x, y, z, yaw, on) = ReadPlacement(placement, yaw);
        }
        else
        {
            source = KernelJson.ToVariant(args.GetProperty("source"));
            (x, y, z, yaw, on) = ReadPlacement(args.GetProperty("placement"), 0);
        }
        if (preview)
        {
            var checkedPlacement = Authority.Call("preflight", principal, source, x, z, yaw, instanceId, y, on, approvedBy ?? "").AsGodotDictionary();
            if (!checkedPlacement["ok"].AsBool()) throw Translate(checkedPlacement);
            var answer = Result(op, principal, actionId, null, true);
            answer["preview"] = true;
            answer["data"] = new JsonObject
            {
                ["position_m"] = KernelJson.ToJson(checkedPlacement["position_m"]),
                ["surface"] = checkedPlacement["surface_entity"].AsString(),
            };
            return answer;
        }
        var request = new Godot.Collections.Dictionary
        {
            ["op"] = op == "creation.place" ? "place" : "revise", ["action_id"] = actionId,
            ["expected_revision"] = Revision, ["expected_permission_revision"] = PermissionRevision,
            ["source"] = source, ["x_m"] = x, ["z_m"] = z, ["yaw_deg"] = yaw, ["y_m"] = y,
        };
        if (on.Length > 0) request["on"] = on;
        if (instanceId.Length > 0) request["instance_id"] = instanceId;
        return Submit(request, principal, actionId, meta);
    }

    private JsonObject Remove(JsonElement args, string principal, string actionId, Godot.Collections.Dictionary meta, bool preview, string? approvedBy)
    {
        var target = Str(args, "target")!;
        if (EntityRevision(target) < 0) throw new Refusal("target_not_found", "That is not in this room.", "$.args.target");
        if (!target.StartsWith("creation:", StringComparison.Ordinal))
            throw new Refusal("unsupported_capability", target.StartsWith("obj:", StringComparison.Ordinal)
                ? "Removing room objects arrives with the sandbox verbs (Run 2)." : "That part of the room cannot be removed.", "$.args.target");
        if (Authority.Call("is_locked", target).AsBool()) throw new Refusal("target_protected", "That is protected. Only the player can unlock it.", "$.args.target");
        if (preview) return Previewed("entity.remove", principal, actionId, target);
        var request = new Godot.Collections.Dictionary
        {
            ["op"] = "remove", ["action_id"] = actionId, ["instance_id"] = target,
            ["expected_revision"] = Revision, ["expected_permission_revision"] = PermissionRevision,
        };
        return Submit(request, principal, actionId, meta);
    }

    private JsonObject Protect(string op, JsonElement args, string principal, string actionId, Godot.Collections.Dictionary meta, bool preview)
    {
        var targets = args.GetProperty("targets").EnumerateArray().Select(t => t.GetString()!).ToArray();
        foreach (var target in targets)
            if (EntityRevision(target) < 0) throw new Refusal("target_not_found", "That is not in this room.", "$.args.targets");
        if (preview)
        {
            foreach (var target in targets)
                if (Authority.Call("is_locked", target).AsBool() == (op == "protect.lock"))
                    throw new Refusal("invalid_args", op == "protect.lock" ? "That is already protected." : "That is not protected.", "$.args.targets");
            return Previewed(op, principal, actionId, targets);
        }
        var request = new Godot.Collections.Dictionary
        {
            ["op"] = op == "protect.lock" ? "lock" : "unlock", ["action_id"] = actionId,
            ["targets"] = new Godot.Collections.Array(targets.Select(t => (Variant)t)),
            ["expected_revision"] = Revision, ["expected_permission_revision"] = PermissionRevision,
        };
        return Submit(request, principal, actionId, meta);
    }

    private JsonObject Activate(JsonElement args, string principal, string actionId, string fingerprint, Godot.Collections.Dictionary meta, bool preview)
    {
        var target = Str(args, "target")!;
        if (Instance(target) == null) throw new Refusal("target_not_found", "That is not in this room.", "$.args.target");
        if (preview) return Previewed("creation.activate", principal, actionId, target);
        var request = new Godot.Collections.Dictionary
        {
            ["op"] = "activate", ["action_id"] = actionId, ["instance_id"] = target,
            ["expected_revision"] = Revision, ["expected_permission_revision"] = PermissionRevision,
        };
        var outcome = Runtime.Call("execute_activation", principal, request, meta).AsGodotDictionary();
        if (!outcome["ok"].AsBool()) throw Translate(outcome);
        return Transient("creation.activate", principal, actionId, fingerprint, new JsonArray(target), null);
    }

    private JsonObject GoalSet(JsonElement args, string principal, string actionId, string fingerprint, bool preview)
    {
        var actor = Str(args, "actor")!;
        if (actor == PlayerAvatar)
        {
            if (principal != PlayerPrincipal) throw new Refusal("actor_denied", "A companion may not direct the player.", "$.args.actor");
            throw new Refusal("unsupported_capability", "Your body follows your own controls.", "$.args.actor");
        }
        if (actor != CompanionAvatarId || Companion == null) throw new Refusal("target_not_found", "There is no such actor in this room.", "$.args.actor");
        var goal = Str(args, "goal")!;
        Vector3? point = null;
        if (goal is "look_at" or "point_at")
        {
            if (args.TryGetProperty("target", out var target))
            {
                var summary = Entities().FirstOrDefault(e => e["id"]!.GetValue<string>() == target.GetString())
                    ?? throw new Refusal("target_not_found", "That is not in this room.", "$.args.target");
                var box = summary["bounds_m"]!;
                var low = ReadNodeVector(box["min_m"]!);
                var high = ReadNodeVector(box["max_m"]!);
                point = (low + high) * 0.5f;
            }
            else point = KernelJson.ReadVector(args.GetProperty("position_m"));
        }
        if (preview) return Previewed("goal.set", principal, actionId, actor);
        switch (goal)
        {
            case "follow": Companion.Follow(); break;
            case "stay": Companion.Stay(); break;
            case "come": Companion.Come(); break;
            case "look_at": Companion.LookAtPoint(point!.Value); break;
            case "point_at": Companion.PointAt(point!.Value); break;
        }
        return Transient("goal.set", principal, actionId, fingerprint, new JsonArray(actor), null);
    }

    private JsonObject GoalStop(JsonElement args, string principal, string actionId, string fingerprint, bool preview)
    {
        var actor = args.TryGetProperty("actor", out var a) ? a.GetString() : null;
        if (actor != null && actor != PlayerAvatar && actor != CompanionAvatarId)
            throw new Refusal("target_not_found", "There is no such actor in this room.", "$.args.actor");
        if (preview) return Previewed("goal.stop", principal, actionId);
        var affected = new JsonArray();
        if ((actor == null || actor == CompanionAvatarId) && Companion != null)
        {
            Companion.Stop();
            affected.Add(CompanionAvatarId);
        }
        var stopped = actor == null ? Runtime.Call("stop_effects").AsInt32() : 0;
        return Transient("goal.stop", principal, actionId, fingerprint, affected, new JsonObject { ["effects_stopped"] = stopped });
    }

    private JsonObject EffectStop(JsonElement args, string principal, string actionId, string fingerprint, bool preview)
    {
        var effect = Str(args, "effect")!;
        if (preview) return Previewed("effect.stop", principal, actionId);
        // Creation effects have no effect: ids yet, so a named effect is already stopped.
        var stopped = effect == "all" ? Runtime.Call("stop_effects").AsInt32() : 0;
        return Transient("effect.stop", principal, actionId, fingerprint, new JsonArray(), new JsonObject { ["effects_stopped"] = stopped });
    }

    private JsonObject Submit(Godot.Collections.Dictionary request, string principal, string actionId, Godot.Collections.Dictionary meta)
    {
        var outcome = Authority.Call("submit", principal, request, meta).AsGodotDictionary();
        if (!outcome["ok"].AsBool()) throw Translate(outcome);
        // The first answer is rebuilt from the durable receipt, exactly as every replay will be.
        var record = Authority.Call("receipt_for", principal, actionId).AsGodotDictionary();
        return Durable(record, replayed: false);
    }

    private JsonObject Transient(string op, string principal, string actionId, string fingerprint, JsonArray affected, JsonObject? data)
    {
        if (_transient.Count >= MaxTransientReceipts)
            throw new Refusal("receipt_limit", "The session's transient receipt limit has been reached.");
        var result = Result(op, principal, actionId, null, true);
        result["transient"] = true;
        if (affected.Count > 0) result["affected"] = affected;
        if (data != null) result["data"] = data;
        _transient[principal + "|" + actionId] = (fingerprint, (JsonObject)result.DeepClone());
        return result;
    }

    private JsonObject Previewed(string op, string principal, string actionId, params string[] affected)
    {
        var result = Result(op, principal, actionId, null, true);
        result["preview"] = true;
        if (affected.Length > 0) result["affected"] = new JsonArray(affected.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
        return result;
    }

    private JsonObject? Replay(string principal, string actionId, string fingerprint)
    {
        if (_transient.TryGetValue(principal + "|" + actionId, out var transient))
        {
            if (transient.Fingerprint != fingerprint)
                throw new Refusal("action_id_conflict", "This action id was already used for different content.", "$.action_id");
            var copy = (JsonObject)transient.Result.DeepClone();
            copy["replayed"] = true;
            return copy;
        }
        var record = Authority.Call("receipt_for", principal, actionId).AsGodotDictionary();
        if (record.Count == 0) return null;
        if (record["fingerprint"].AsString() != fingerprint)
            throw new Refusal("action_id_conflict", "This action id was already used for different content.", "$.action_id");
        return Durable(record, replayed: true);
    }

    private JsonObject Durable(Godot.Collections.Dictionary record, bool replayed)
    {
        var receipt = record["receipt"].AsGodotDictionary();
        var meta = record["meta"].AsGodotDictionary();
        var result = Result(meta["op"].AsString(), record["principal"].AsString(), record["action_id"].AsString(), null, true);
        result["revision"] = receipt["revision"].AsInt32();
        result["replayed"] = replayed;
        result["transient"] = false;
        if (meta["at_utc"].AsString().Length > 0) result["at_utc"] = meta["at_utc"].AsString();
        foreach (var key in new[] { "affected", "created" })
        {
            var list = receipt[key].AsGodotArray();
            if (list.Count > 0) result[key] = new JsonArray(list.Select(v => (JsonNode?)JsonValue.Create(v.AsString())).ToArray());
        }
        if (meta["approved_by"].AsString().Length > 0) result["approved_by"] = meta["approved_by"].AsString();
        return result;
    }

    // ---- approvals ----

    private bool NeedsApproval(string op, JsonElement args, out string reason, out Dictionary<string, int> touched)
    {
        reason = "";
        touched = new Dictionary<string, int>();
        if (op is not ("entity.remove" or "creation.revise")) return false;
        var target = Str(args, "target")!;
        var instance = Instance(target);
        if (instance == null || instance["owner_id"].AsString() != PlayerPrincipal) return false;
        touched[target] = EntityRevision(target);
        // Names are untrusted world text, so the prompt names the entity id only.
        reason = op == "entity.remove"
            ? $"The companion asks to remove {target}, which you made. Approve or deny it in the game."
            : $"The companion asks to change {target}, which you made. Approve or deny it in the game.";
        return true;
    }

    private JsonObject Hold(JsonElement root, string principal, string actionId, string fingerprint, string op, string reason, Dictionary<string, int> touched)
    {
        if (_approvals.Values.Count(a => a.Principal == principal && a.State == "pending") >= MaxPendingApprovals)
            throw new Refusal("rate_limited", "Too many requests are already waiting for the player.", retryable: true);
        var approval = new PendingApproval
        {
            RequestId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            Principal = principal, ActionId = actionId, Fingerprint = fingerprint, Op = op, Reason = reason,
            CommandText = CanonicalJson.Text(root), Created = Clock(), Expires = Clock() + ApprovalLifetime, Touched = touched,
        };
        var result = Fail(op, principal, actionId, null, new Refusal("approval_required", "Waiting for the player to approve this change.", retryable: true));
        result["approval_needed"] = new JsonObject
        {
            ["request_id"] = approval.RequestId, ["reason"] = reason, ["expires_utc"] = KernelJson.Utc(approval.Expires),
        };
        approval.Held = (JsonObject)result.DeepClone();
        _approvals[approval.RequestId] = approval;
        _approvalByAction[principal + "|" + actionId] = approval.RequestId;
        ApprovalRequested?.Invoke(approval);
        if (_prompt != null) Input.MouseMode = Input.MouseModeEnum.Visible;
        return result;
    }

    /// <summary>The answer for a command that is (or was) held: the same request while pending, a refusal once denied.</summary>
    private JsonObject? HeldAnswer(string principal, string actionId, string fingerprint)
    {
        if (!_approvalByAction.TryGetValue(principal + "|" + actionId, out var requestId)) return null;
        var approval = _approvals[requestId];
        Expire(approval);
        if (approval.State == "expired")
        {
            // An expired request can be asked again (a new request id); a lapsed one too.
            _approvalByAction.Remove(principal + "|" + actionId);
            return null;
        }
        if (approval.Fingerprint != fingerprint)
            throw new Refusal("action_id_conflict", "This action id was already used for different content.", "$.action_id");
        if (approval.State == "pending")
        {
            var held = (JsonObject)approval.Held.DeepClone();
            held["at_utc"] = Now();
            return held;
        }
        if (approval.State == "denied") throw new Refusal("permission_denied", "The player declined this request.");
        return approval.Result == null ? null : (JsonObject)approval.Result.DeepClone();
    }

    private void Expire(PendingApproval approval)
    {
        if (approval.State == "pending" && Clock() >= approval.Expires) approval.State = "expired";
    }

    // ---- queries ----

    private JsonObject HandleQuery(JsonElement root, byte[] canonical, string principal)
    {
        var op = Op(root);
        var queryId = root.TryGetProperty("query_id", out var q) && q.ValueKind == JsonValueKind.String && ActionId.IsMatch(q.GetString()!) ? q.GetString() : null;
        try
        {
            CheckEnvelope(root, command: false);
            if (canonical.Length > MaxMessageBytes) throw new Refusal("request_invalid", "A query is at most 65,536 bytes of canonical JSON.");
            RateLimit(principal);
            var args = root.GetProperty("args");
            CheckQueryArgs(op, args);
            var result = Result(op, principal, null, queryId, true);
            result["data"] = op switch
            {
                "room.describe" => Describe(),
                "entities.list" => List(args),
                "entity.inspect" => Inspect(args),
                "capabilities.list" => Capabilities(),
                "observe" => Observe(args, principal),
                "receipt.lookup" => Lookup(args, principal),
                "approval.status" => Status(args, principal),
                _ => throw new Refusal("target_not_found", "There is no such job.", "$.args.job_id"),
            };
            return result;
        }
        catch (Refusal refusal) { return Fail(op, principal, null, queryId, refusal); }
    }

    private JsonObject Describe()
    {
        var style = Style ?? StylePreset.Resolve(RoomWorld.DefaultStyleId, RoomWorld.DefaultStyleVersion);
        var creations = Authority.Call("snapshot", PlayerPrincipal).AsGodotDictionary();
        var count = creations.ContainsKey("instances") ? creations["instances"].AsGodotDictionary().Count : 0;
        return new JsonObject
        {
            ["room_id"] = Room.RoomId, ["display_name"] = KernelJson.DisplayText(Room.DisplayName, 80), ["revision"] = Revision,
            ["source_kind"] = Room.SourceKind, ["bounds_m"] = KernelJson.Box(Room.Bounds),
            ["style"] = new JsonObject { ["preset_id"] = style.PresetId, ["preset_version"] = style.PresetVersion, ["preset_sha256"] = style.Sha256 },
            ["counts"] = new JsonObject { ["objects"] = Room.Objects.Count, ["creations"] = count, ["shell_parts"] = Room.Shell.Count },
        };
    }

    private JsonObject List(JsonElement args)
    {
        IEnumerable<JsonObject> items = Entities();
        if (args.TryGetProperty("filter", out var filter))
        {
            if (filter.TryGetProperty("kind", out var kind)) items = items.Where(e => e["kind"]!.GetValue<string>() == kind.GetString());
            if (filter.TryGetProperty("category_group", out var group)) items = items.Where(e => e["category_group"]?.GetValue<string>() == group.GetString());
            if (filter.TryGetProperty("provenance_kind", out var provenance)) items = items.Where(e => e["provenance_kind"]!.GetValue<string>() == provenance.GetString());
            if (filter.TryGetProperty("affordance", out var affordance)) items = items.Where(e => e["affordances"]!.AsArray().Any(x => x!.GetValue<string>() == affordance.GetString()));
            if (filter.TryGetProperty("near", out var near))
            {
                var centre = KernelJson.ReadVector(near.GetProperty("center_m"));
                var radius = (float)CanonicalJson.ReadNumber(near.GetProperty("radius_m"));
                items = items.Where(e => Distance(e, centre) <= radius);
            }
        }
        var all = items.ToList();
        var start = 0;
        if (args.TryGetProperty("cursor", out var cursor) && (!int.TryParse(cursor.GetString(), out start) || start < 0 || start > all.Count))
            throw new Refusal("invalid_args", "That cursor is not from this list.", "$.args.cursor");
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 50;
        var page = all.Skip(start).Take(limit).ToList();
        var data = new JsonObject { ["items"] = new JsonArray(page.Select(e => (JsonNode?)e).ToArray()) };
        if (start + page.Count < all.Count) data["next_cursor"] = (start + page.Count).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return data;
    }

    private JsonObject Inspect(JsonElement args)
    {
        var target = Str(args, "target")!;
        var entity = Entities().FirstOrDefault(e => e["id"]!.GetValue<string>() == target)
            ?? throw new Refusal("target_not_found", "That is not in this room.", "$.args.target");
        var data = new JsonObject { ["entity"] = entity };
        var locks = Authority.Call("snapshot", PlayerPrincipal).AsGodotDictionary();
        if (locks.ContainsKey("locks") && locks["locks"].AsGodotDictionary().TryGetValue(target, out var info))
            data["protected_by"] = info.AsGodotDictionary()["locked_by"].AsString();
        return data;
    }

    private static JsonObject Capabilities() => new()
    {
        ["commands"] = new JsonArray("creation.place", "creation.revise", "creation.activate", "entity.remove", "protect.lock", "protect.unlock", "goal.set", "goal.stop", "effect.stop"),
        ["queries"] = new JsonArray(QueryOps.Where(o => o != "jobs.status").OrderBy(o => o, StringComparer.Ordinal).Select(o => (JsonNode?)JsonValue.Create(o)).ToArray()),
        ["goals"] = new JsonArray(SupportedGoals.Select(g => (JsonNode?)JsonValue.Create(g)).ToArray()),
        ["player_only"] = new JsonArray("protect.unlock"),
        ["needs_player_approval"] = new JsonArray("entity.remove of the player's creation", "creation.revise of the player's creation"),
    };

    private JsonObject Observe(JsonElement args, string principal)
    {
        var actor = Str(args, "actor")!;
        if (principal == CompanionPrincipal && actor != CompanionAvatarId)
            throw new Refusal("actor_denied", "A companion observes only through its own avatar.", "$.args.actor");
        CharacterBody3D? body = actor == CompanionAvatarId ? Companion : actor == PlayerAvatar ? Player : null;
        if (body == null) throw new Refusal("target_not_found", "There is no such actor in this room.", "$.args.actor");
        var radius = args.TryGetProperty("radius_m", out var r) ? (float)CanonicalJson.ReadNumber(r) : 3.0f;
        var eye = body.GlobalPosition + Vector3.Up * ((body as SmallPlayerController)?.BodyHeightM ?? 0.1f) * 0.87f;
        var space = GetViewport().World3D.DirectSpaceState;
        var visible = new List<(float Distance, JsonObject Entity)>();
        foreach (var entity in Entities())
        {
            var id = entity["id"]!.GetValue<string>();
            if (id == actor) continue;
            var distance = Distance(entity, eye);
            if (distance > radius) continue;
            var low = ReadNodeVector(entity["bounds_m"]!["min_m"]!);
            var high = ReadNodeVector(entity["bounds_m"]!["max_m"]!);
            var box = new Aabb(low, high - low);
            var ray = PhysicsRayQueryParameters3D.Create(eye, (low + high) * 0.5f, 1);
            ray.Exclude = new Godot.Collections.Array<Rid> { body.GetRid() };
            var hit = space.IntersectRay(ray);
            if (hit.Count > 0 && !box.Grow(0.05f).HasPoint(hit["position"].AsVector3())) continue;
            visible.Add((distance, entity));
        }
        var seen = visible.OrderBy(v => v.Distance).ThenBy(v => v.Entity["id"]!.GetValue<string>(), StringComparer.Ordinal).Take(100).Select(v => v.Entity).ToList();
        // Every name seen in the world is untrusted text, never instructions.
        var texts = seen.Take(50).Select(e => (JsonNode?)new JsonObject
        {
            ["source"] = e["id"]!.GetValue<string>(), ["text"] = e["display_name"]!.GetValue<string>(), ["untrusted"] = true,
        }).ToArray();
        return new JsonObject
        {
            ["actor"] = actor,
            ["visible"] = new JsonArray(seen.Select(e => (JsonNode?)e).ToArray()),
            ["texts"] = new JsonArray(texts),
        };
    }

    private JsonObject Lookup(JsonElement args, string principal)
    {
        var actionId = Str(args, "action_id")!;
        if (_transient.TryGetValue(principal + "|" + actionId, out var transient))
            return new JsonObject { ["found"] = true, ["receipt"] = transient.Result.DeepClone() };
        var record = Authority.Call("receipt_for", principal, actionId).AsGodotDictionary();
        if (record.Count == 0) return new JsonObject { ["found"] = false };
        return new JsonObject { ["found"] = true, ["receipt"] = Durable(record, replayed: false) };
    }

    private JsonObject Status(JsonElement args, string principal)
    {
        var requestId = Str(args, "request_id")!;
        if (!_approvals.TryGetValue(requestId, out var approval) || (approval.Principal != principal && principal != PlayerPrincipal))
            throw new Refusal("target_not_found", "There is no request with that id.", "$.args.request_id");
        Expire(approval);
        var data = new JsonObject { ["request_id"] = requestId, ["state"] = approval.State };
        if (approval.Result != null && approval.State is "approved" or "denied") data["result"] = approval.Result.DeepClone();
        return data;
    }

    // ---- entities ----

    /// <summary>Every entity in the room as contract entity summaries, sorted by id. Names are untrusted data.</summary>
    public List<JsonObject> Entities()
    {
        var list = new List<JsonObject>();
        var snapshot = Authority.Call("snapshot", PlayerPrincipal).AsGodotDictionary();
        var locks = snapshot.ContainsKey("locks") ? snapshot["locks"].AsGodotDictionary() : new Godot.Collections.Dictionary();
        var shellProvenance = Room.SourceKind switch { "capture" => "captured_scanned", "procedural" => "procedural", _ => "hand_authored" };
        foreach (var part in Room.Shell)
        {
            var box = ShellBounds(part);
            list.Add(Summary(part.Id, "shell", Readable(part.Id), null, null, box.GetCenter(), box,
                part.Role == "floor" ? new[] { "walkable_top" } : Array.Empty<string>(), false, false, shellProvenance, EntityRevision(part.Id)));
        }
        foreach (var item in Room.Objects)
        {
            var box = ObjectBounds(item);
            list.Add(Summary(item.Id, "object", item.DisplayName ?? item.Asset.DisplayName, item.Asset.Category, item.Asset.CategoryGroup, item.PositionM, box,
                item.Asset.Affordances, item.Asset.Movable, locks.ContainsKey(item.Id), item.Asset.ProvenanceKind, EntityRevision(item.Id)));
        }
        if (snapshot.ContainsKey("instances"))
            foreach (var (key, value) in snapshot["instances"].AsGodotDictionary())
            {
                var instance = value.AsGodotDictionary();
                var position = instance["position_m"].AsGodotArray();
                var origin = new Vector3((float)position[0].AsDouble(), (float)position[1].AsDouble(), (float)position[2].AsDouble());
                var bounds = instance["artifact"].AsGodotDictionary()["bounds"].AsGodotDictionary();
                var local = new Aabb(ArrayVector(bounds["min"].AsGodotArray()), ArrayVector(bounds["max"].AsGodotArray()) - ArrayVector(bounds["min"].AsGodotArray()));
                var box = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad((float)instance["yaw_deg"].AsDouble())), origin) * local;
                var name = instance["source"].AsGodotDictionary()["name"].AsString();
                list.Add(Summary(key.AsString(), "creation", name, "creation", null, origin, box, Array.Empty<string>(), false,
                    locks.ContainsKey(key.AsString()), instance["owner_id"].AsString() == CompanionPrincipal ? "ai_created" : "player_created", instance["revision"].AsInt32()));
            }
        foreach (var (id, body, name) in new (string, SmallPlayerController?, string)[] { (PlayerAvatar, Player, "Player"), (CompanionAvatarId, Companion, Companion?.CompanionName ?? "Companion") })
        {
            if (body == null || !IsInstanceValid(body)) continue;
            var box = new Aabb(body.GlobalPosition - new Vector3(body.BodyRadiusM, 0, body.BodyRadiusM), new Vector3(body.BodyRadiusM * 2, body.BodyHeightM, body.BodyRadiusM * 2));
            list.Add(Summary(id, "avatar", name, null, null, body.GlobalPosition, box, Array.Empty<string>(), true, false, "hand_authored", 0));
        }
        return list.OrderBy(e => e["id"]!.GetValue<string>(), StringComparer.Ordinal).ToList();
    }

    private static JsonObject Summary(string id, string kind, string name, string? category, string? group, Vector3 position, Aabb bounds,
        IEnumerable<string> affordances, bool movable, bool locked, string provenance, int revision)
    {
        var summary = new JsonObject
        {
            ["id"] = id, ["kind"] = kind, ["display_name"] = KernelJson.DisplayText(name, 80), ["position_m"] = KernelJson.Vector(position),
            ["bounds_m"] = KernelJson.Box(bounds),
            ["affordances"] = new JsonArray(affordances.Where(Affordances.Contains).Distinct().Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
            ["movable"] = movable, ["protected"] = locked, ["provenance_kind"] = provenance, ["revision"] = Math.Max(0, revision),
        };
        if (category != null) summary["category"] = KernelJson.DisplayText(category, 60);
        if (group != null) summary["category_group"] = group;
        return summary;
    }

    public int EntityRevision(string id)
    {
        if (id == PlayerAvatar) return Player != null ? 0 : -1;
        if (id == CompanionAvatarId) return Companion != null ? 0 : -1;
        return Authority.Call("entity_revision", id).AsInt32();
    }

    private Godot.Collections.Dictionary? Instance(string id)
    {
        var snapshot = Authority.Call("snapshot", PlayerPrincipal).AsGodotDictionary();
        if (!snapshot.ContainsKey("instances")) return null;
        var instances = snapshot["instances"].AsGodotDictionary();
        return instances.TryGetValue(id, out var item) ? item.AsGodotDictionary() : null;
    }

    /// <summary>The room as the GDScript authority reads it: id, manifest pin, bounds and lockable extents.</summary>
    public static Godot.Collections.Dictionary RoomDictionary(RoomData room)
    {
        var entities = new Godot.Collections.Dictionary();
        foreach (var part in room.Shell) entities[part.Id] = KernelJson.VariantBox(ShellBounds(part));
        foreach (var item in room.Objects) entities[item.Id] = KernelJson.VariantBox(ObjectBounds(item));
        return new Godot.Collections.Dictionary
        {
            ["room_id"] = room.RoomId, ["manifest_sha256"] = room.ManifestSha256,
            ["bounds"] = KernelJson.VariantBox(room.Bounds), ["entities"] = entities,
        };
    }

    public static Aabb ShellBounds(ShellPart part)
    {
        if (part.Points.Length == 0) return new Aabb(Vector3.Zero, Vector3.Zero);
        var normal = RoomBuilder.NewellNormal(part.Points);
        var box = new Aabb(part.Points[0], Vector3.Zero);
        foreach (var point in part.Points)
        {
            box = box.Expand(point);
            box = box.Expand(point - normal * part.ThicknessM);
        }
        return box;
    }

    public static Aabb ObjectBounds(ObjectInstance item)
    {
        var size = item.Asset.DimensionsM;
        var basis = new Basis(item.Rotation).Scaled(Vector3.One * item.Scale);
        Aabb? box = null;
        foreach (var x in new[] { -0.5f, 0.5f })
        foreach (var y in new[] { 0f, 1f })
        foreach (var z in new[] { -0.5f, 0.5f })
        {
            var corner = item.PositionM + basis * new Vector3(size.X * x, size.Y * y, size.Z * z);
            box = box == null ? new Aabb(corner, Vector3.Zero) : box.Value.Expand(corner);
        }
        return box!.Value;
    }

    private static string Readable(string id) => id[(id.IndexOf(':') + 1)..].Replace('_', ' ');

    private static float Distance(JsonObject entity, Vector3 point)
    {
        var low = ReadNodeVector(entity["bounds_m"]!["min_m"]!);
        var high = ReadNodeVector(entity["bounds_m"]!["max_m"]!);
        return point.DistanceTo(point.Clamp(low, high));
    }

    private static Vector3 ReadNodeVector(JsonNode node) =>
        new((float)node[0]!.GetValue<double>(), (float)node[1]!.GetValue<double>(), (float)node[2]!.GetValue<double>());

    private static Vector3 ArrayVector(Godot.Collections.Array values) =>
        new((float)values[0].AsDouble(), (float)values[1].AsDouble(), (float)values[2].AsDouble());

    // ---- validation ----

    private sealed class Refusal(string code, string message, string? path = null, bool retryable = false, JsonNode? actual = null) : Exception(message)
    {
        public string Code { get; } = code;
        public string? Path { get; } = path;
        public bool Retryable { get; } = retryable;
        public JsonNode? Actual { get; } = actual;
    }

    private void CheckEnvelope(JsonElement root, bool command)
    {
        var allowed = command
            ? new[] { "schema", "version", "action_id", "room_id", "expected_revision", "op", "args", "preview", "note", "expected_entities" }
            : new[] { "schema", "version", "query_id", "room_id", "op", "args" };
        foreach (var property in root.EnumerateObject())
            if (!allowed.Contains(property.Name))
                throw new Refusal("field_unknown", property.Name is "principal" or "approval" or "approved_by" or "approval_id"
                    ? "Identity and approval are never part of a message." : "This field is not part of the message.", "$." + KernelJson.DisplayText(property.Name, 64));
        foreach (var required in command ? new[] { "version", "action_id", "room_id", "op", "args" } : new[] { "version", "query_id", "room_id", "op", "args" })
            if (!root.TryGetProperty(required, out _)) throw new Refusal("request_invalid", $"The message needs '{required}'.", "$." + required);
        var version = root.GetProperty("version");
        if (version.ValueKind != JsonValueKind.Number || !IsIntegerLiteral(version))
            throw new Refusal("request_invalid", "version is the integer 1.", "$.version");
        if (version.GetRawText() != "1") throw new Refusal("version_unsupported", "Only message version 1 is supported.", "$.version");
        var id = Str(root, command ? "action_id" : "query_id");
        if (id == null || !ActionId.IsMatch(id))
            throw new Refusal(command ? "action_id_invalid" : "request_invalid", "Use an id of 1 to 64 letters, digits, '-' or '_', starting with a letter or digit.", command ? "$.action_id" : "$.query_id");
        var room = Str(root, "room_id");
        if (room == null || !Token.IsMatch(room)) throw new Refusal("request_invalid", "room_id is a lowercase token.", "$.room_id");
        if (room != Room.RoomId) throw new Refusal("room_mismatch", "This host serves a different room.", "$.room_id");
        var op = Str(root, "op");
        if (op == null || !(command ? CommandOps : QueryOps).Contains(op)) throw new Refusal("request_invalid", "That operation does not exist.", "$.op");
        if (root.GetProperty("args").ValueKind != JsonValueKind.Object) throw new Refusal("request_invalid", "args is an object.", "$.args");
        if (!command) return;
        if (root.TryGetProperty("preview", out var preview) && preview.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new Refusal("request_invalid", "preview is true or false.", "$.preview");
        if (root.TryGetProperty("note", out var note) && (note.ValueKind != JsonValueKind.String || note.GetString()!.Length > 280 || UnsafeText.IsMatch(note.GetString()!)))
            throw new Refusal("request_invalid", "A note is one line of at most 280 characters.", "$.note");
        if (root.TryGetProperty("expected_revision", out var expected) && (!IsIntegerLiteral(expected) || expected.GetDouble() < 0))
            throw new Refusal("request_invalid", "expected_revision is a non-negative integer.", "$.expected_revision");
        if (root.TryGetProperty("expected_entities", out var entities))
        {
            if (entities.ValueKind != JsonValueKind.Object || entities.EnumerateObject().Count() > 64)
                throw new Refusal("request_invalid", "expected_entities maps up to 64 entity ids to revisions.", "$.expected_entities");
            foreach (var entry in entities.EnumerateObject())
                if (!EntityId.IsMatch(entry.Name) || !IsIntegerLiteral(entry.Value) || entry.Value.GetDouble() < 0)
                    throw new Refusal("request_invalid", "expected_entities maps entity ids to non-negative integer revisions.", "$.expected_entities");
        }
        if (DestructiveOps.Contains(op) && !root.TryGetProperty("expected_revision", out _) && !root.TryGetProperty("expected_entities", out _))
            throw new Refusal("request_invalid", "This change must name expected_revision or expected_entities.", "$");
    }

    private void CheckExpectations(JsonElement root, string op, JsonElement args)
    {
        if (root.TryGetProperty("expected_revision", out var expected) && expected.GetInt64() != Revision)
            throw new Refusal("revision_conflict", "The room changed. Look again before retrying.", "$.expected_revision", true, JsonValue.Create(Revision));
        if (root.TryGetProperty("expected_entities", out var entities))
        {
            foreach (var entry in entities.EnumerateObject())
            {
                var current = EntityRevision(entry.Name);
                if (current < 0) throw new Refusal("target_not_found", "That is not in this room.", "$.expected_entities");
                if (current != entry.Value.GetInt64())
                    throw new Refusal("revision_conflict", "Something it names has changed. Look again before retrying.", "$.expected_entities", true, JsonValue.Create(current));
            }
            if (DestructiveOps.Contains(op) && !root.TryGetProperty("expected_revision", out _))
                foreach (var target in Targets(op, args))
                    if (!entities.TryGetProperty(target, out _))
                        throw new Refusal("invalid_args", "expected_entities must cover every entity the change touches.", "$.expected_entities");
        }
    }

    private static IEnumerable<string> Targets(string op, JsonElement args) =>
        args.TryGetProperty("targets", out var many) ? many.EnumerateArray().Select(t => t.GetString()!)
        : args.TryGetProperty("target", out var one) ? new[] { one.GetString()! } : Array.Empty<string>();

    private static void CheckArgs(string op, JsonElement args)
    {
        string[] allowed = op switch
        {
            "entity.grab" => new[] { "target", "actor" },
            "entity.release" => new[] { "actor", "placement" },
            "entity.place" => new[] { "target", "placement" },
            "entity.set_part" => new[] { "target", "part_id", "value" },
            "entity.remove" or "creation.activate" => new[] { "target" },
            "entity.transform" => new[] { "target", "into" },
            "creation.place" => new[] { "source", "placement" },
            "creation.revise" => new[] { "target", "source", "placement" },
            "protect.lock" or "protect.unlock" => new[] { "targets" },
            "goal.set" => new[] { "actor", "goal", "target", "position_m", "area", "duration_s" },
            "goal.stop" => new[] { "actor" },
            "effect.start" => new[] { "capability", "params", "area", "duration_s", "targets" },
            "effect.stop" => new[] { "effect" },
            "style.set" => new[] { "preset_id", "preset_version" },
            "room.checkpoint" => new[] { "label" },
            "room.undo" => new[] { "to_revision" },
            _ => Array.Empty<string>(),
        };
        foreach (var property in args.EnumerateObject())
            if (!allowed.Contains(property.Name))
                throw new Refusal("field_unknown", "This argument is not part of the operation.", "$.args." + KernelJson.DisplayText(property.Name, 64));
        string[] required = op switch
        {
            "entity.grab" or "entity.remove" or "creation.activate" or "creation.revise" => new[] { "target" },
            "entity.place" => new[] { "target", "placement" },
            "entity.set_part" => new[] { "target", "part_id", "value" },
            "entity.transform" => new[] { "target", "into" },
            "creation.place" => new[] { "source", "placement" },
            "protect.lock" or "protect.unlock" => new[] { "targets" },
            "goal.set" => new[] { "actor", "goal" },
            "effect.start" => new[] { "capability", "params", "area", "duration_s" },
            "effect.stop" => new[] { "effect" },
            "style.set" => new[] { "preset_id", "preset_version" },
            "room.undo" => new[] { "to_revision" },
            _ => Array.Empty<string>(),
        };
        foreach (var name in required)
            if (!args.TryGetProperty(name, out _)) throw new Refusal("request_invalid", $"This operation needs '{name}'.", "$.args." + name);
        if (args.TryGetProperty("target", out var target) && (target.ValueKind != JsonValueKind.String || !EntityId.IsMatch(target.GetString()!)))
            throw new Refusal("request_invalid", "target is an entity id.", "$.args.target");
        if (op == "creation.revise" && !CreationId.IsMatch(target.GetString()!))
            throw new Refusal("request_invalid", "Only creations can be revised.", "$.args.target");
        if (op == "creation.revise" && !args.TryGetProperty("source", out _) && !args.TryGetProperty("placement", out _))
            throw new Refusal("request_invalid", "A revision changes the source, the placement or both.", "$.args");
        if (args.TryGetProperty("targets", out var targets))
        {
            if (targets.ValueKind != JsonValueKind.Array || targets.GetArrayLength() is < 1 or > 64)
                throw new Refusal("request_invalid", "targets lists 1 to 64 entity ids.", "$.args.targets");
            var names = targets.EnumerateArray().Select(t => t.ValueKind == JsonValueKind.String ? t.GetString()! : "").ToArray();
            if (names.Any(n => !EntityId.IsMatch(n)) || names.Distinct().Count() != names.Length)
                throw new Refusal("request_invalid", "targets lists distinct entity ids.", "$.args.targets");
        }
        if (args.TryGetProperty("actor", out var actor) && (actor.ValueKind != JsonValueKind.String || !AvatarId.IsMatch(actor.GetString()!)))
            throw new Refusal("request_invalid", "actor is an avatar id.", "$.args.actor");
        if (args.TryGetProperty("source", out var source)) CheckSource(source);
        if (args.TryGetProperty("placement", out var placement)) CheckPlacement(placement);
        if (args.TryGetProperty("position_m", out var position)) CheckVector(position, "$.args.position_m");
        if (op == "goal.set")
        {
            var goal = args.GetProperty("goal");
            if (goal.ValueKind != JsonValueKind.String || goal.GetString() is not ("follow" or "stay" or "come" or "look_at" or "point_at" or "go_to" or "fetch" or "wander"))
                throw new Refusal("request_invalid", "That goal does not exist.", "$.args.goal");
            if (goal.GetString() is "look_at" or "point_at" or "go_to" && !args.TryGetProperty("target", out _) && !args.TryGetProperty("position_m", out _))
                throw new Refusal("request_invalid", "This goal needs a target or a position.", "$.args");
            if (goal.GetString() == "fetch" && !args.TryGetProperty("target", out _))
                throw new Refusal("request_invalid", "Fetch needs a target.", "$.args.target");
            if (args.TryGetProperty("duration_s", out var duration) && (duration.ValueKind != JsonValueKind.Number || duration.GetDouble() is <= 0 or > 3600))
                throw new Refusal("request_invalid", "duration_s is between 0 and 3600 seconds.", "$.args.duration_s");
        }
        if (op == "effect.stop")
        {
            var effect = args.GetProperty("effect");
            if (effect.ValueKind != JsonValueKind.String || (effect.GetString() != "all" && !EffectId.IsMatch(effect.GetString()!)))
                throw new Refusal("request_invalid", "effect is an effect id or \"all\".", "$.args.effect");
        }
        if (op == "effect.start")
        {
            var parameters = args.GetProperty("params");
            if (parameters.ValueKind != JsonValueKind.Object || parameters.EnumerateObject().Count() > 16)
                throw new Refusal("request_invalid", "params holds up to 16 scalars.", "$.args.params");
            foreach (var parameter in parameters.EnumerateObject())
                if (!ParamName.IsMatch(parameter.Name) || ReservedParams.Contains(parameter.Name) || parameter.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null)
                    throw new Refusal("request_invalid", "Effect parameters are bounded scalars under plain lowercase names; identity and authority names are refused.", "$.args.params");
        }
    }

    private static void CheckSource(JsonElement source)
    {
        // The contract fixes the creation source envelope; the creation compiler is the authority on the rest.
        var keys = new[] { "schema", "version", "name", "seed", "mount", "parts", "nodes", "edges" };
        if (source.ValueKind != JsonValueKind.Object || source.EnumerateObject().Any(p => !keys.Contains(p.Name)) || keys.Any(k => !source.TryGetProperty(k, out _)))
            throw new Refusal("request_invalid", "A creation source has exactly schema, version, name, seed, mount, parts, nodes and edges.", "$.args.source");
        if (CanonicalJson.Bytes(source).Length > 32768) throw new Refusal("budget_exceeded", "A creation source is at most 32,768 bytes.", "$.args.source");
    }

    private static void CheckPlacement(JsonElement placement)
    {
        if (placement.ValueKind != JsonValueKind.Object || placement.EnumerateObject().Any(p => p.Name is not ("position_m" or "rotation" or "on")) || !placement.TryGetProperty("position_m", out var position))
            throw new Refusal("request_invalid", "A placement has position_m and optionally rotation and on.", "$.args.placement");
        CheckVector(position, "$.args.placement.position_m");
        if (placement.TryGetProperty("rotation", out var rotation))
        {
            if (rotation.ValueKind != JsonValueKind.Array || rotation.GetArrayLength() != 4 || rotation.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number || Math.Abs(v.GetDouble()) > 1))
                throw new Refusal("request_invalid", "rotation is a unit quaternion (x, y, z, w).", "$.args.placement.rotation");
            var length = Math.Sqrt(rotation.EnumerateArray().Sum(v => v.GetDouble() * v.GetDouble()));
            if (Math.Abs(length - 1) > 1e-3) throw new Refusal("invalid_args", "rotation must be unit length.", "$.args.placement.rotation");
        }
        if (placement.TryGetProperty("on", out var on) && (on.ValueKind != JsonValueKind.String || !EntityId.IsMatch(on.GetString()!)))
            throw new Refusal("request_invalid", "on is an entity id.", "$.args.placement.on");
    }

    private static void CheckVector(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3 || value.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number || Math.Abs(v.GetDouble()) > 1000))
            throw new Refusal("request_invalid", "Positions are three numbers within 1000 m.", path);
    }

    private static void CheckQueryArgs(string op, JsonElement args)
    {
        string[] allowed = op switch
        {
            "entities.list" => new[] { "filter", "cursor", "limit" },
            "entity.inspect" => new[] { "target" },
            "capabilities.list" => new[] { "category", "cursor", "limit" },
            "observe" => new[] { "actor", "radius_m" },
            "jobs.status" => new[] { "job_id" },
            "receipt.lookup" => new[] { "action_id" },
            "approval.status" => new[] { "request_id" },
            _ => Array.Empty<string>(),
        };
        foreach (var property in args.EnumerateObject())
            if (!allowed.Contains(property.Name))
                throw new Refusal("field_unknown", "This argument is not part of the query.", "$.args." + KernelJson.DisplayText(property.Name, 64));
        switch (op)
        {
            case "entity.inspect":
                if (!args.TryGetProperty("target", out var target) || target.ValueKind != JsonValueKind.String || !EntityId.IsMatch(target.GetString()!))
                    throw new Refusal("request_invalid", "target is an entity id.", "$.args.target");
                break;
            case "observe":
                if (!args.TryGetProperty("actor", out var actor) || actor.ValueKind != JsonValueKind.String || !AvatarId.IsMatch(actor.GetString()!))
                    throw new Refusal("request_invalid", "actor is an avatar id.", "$.args.actor");
                if (args.TryGetProperty("radius_m", out var radius) && (radius.ValueKind != JsonValueKind.Number || radius.GetDouble() is <= 0 or > 20))
                    throw new Refusal("request_invalid", "radius_m is between 0 and 20.", "$.args.radius_m");
                break;
            case "receipt.lookup":
                if (!args.TryGetProperty("action_id", out var action) || action.ValueKind != JsonValueKind.String || !ActionId.IsMatch(action.GetString()!))
                    throw new Refusal("request_invalid", "action_id is required.", "$.args.action_id");
                break;
            case "approval.status":
                if (!args.TryGetProperty("request_id", out var request) || request.ValueKind != JsonValueKind.String || !RequestIdPattern.IsMatch(request.GetString()!))
                    throw new Refusal("request_invalid", "request_id is the hex id the host returned.", "$.args.request_id");
                break;
            case "jobs.status":
                if (!args.TryGetProperty("job_id", out var job) || job.ValueKind != JsonValueKind.String || !Token.IsMatch(job.GetString()!))
                    throw new Refusal("request_invalid", "job_id is a lowercase token.", "$.args.job_id");
                break;
            case "entities.list":
                if (args.TryGetProperty("limit", out var limit) && (!IsIntegerLiteral(limit) || limit.GetDouble() is < 1 or > 100))
                    throw new Refusal("request_invalid", "limit is between 1 and 100.", "$.args.limit");
                if (args.TryGetProperty("cursor", out var cursor) && (cursor.ValueKind != JsonValueKind.String || cursor.GetString()!.Length > 128))
                    throw new Refusal("request_invalid", "cursor is the string a previous page returned.", "$.args.cursor");
                if (args.TryGetProperty("filter", out var filter))
                {
                    if (filter.ValueKind != JsonValueKind.Object || filter.EnumerateObject().Any(p => p.Name is not ("kind" or "category_group" or "affordance" or "provenance_kind" or "near")))
                        throw new Refusal("field_unknown", "That filter does not exist.", "$.args.filter");
                    if (filter.TryGetProperty("near", out var near))
                    {
                        if (near.ValueKind != JsonValueKind.Object || !near.TryGetProperty("center_m", out var centre) || !near.TryGetProperty("radius_m", out var nearRadius) || near.EnumerateObject().Count() != 2)
                            throw new Refusal("request_invalid", "near has center_m and radius_m.", "$.args.filter.near");
                        CheckVector(centre, "$.args.filter.near.center_m");
                        if (nearRadius.ValueKind != JsonValueKind.Number || nearRadius.GetDouble() is <= 0 or > 50)
                            throw new Refusal("request_invalid", "radius_m is between 0 and 50.", "$.args.filter.near.radius_m");
                    }
                    foreach (var name in new[] { "kind", "category_group", "affordance", "provenance_kind" })
                        if (filter.TryGetProperty(name, out var value) && (value.ValueKind != JsonValueKind.String || !Token.IsMatch(value.GetString()!)))
                            throw new Refusal("request_invalid", $"{name} is a token.", "$.args.filter." + name);
                }
                break;
        }
    }

    private static string? Unsupported(string op, JsonElement args, string principal) => op switch
    {
        "entity.grab" or "entity.release" or "entity.place" => "Picking up, carrying and placing room objects arrive with the sandbox verbs (Run 2).",
        "entity.set_part" => "Moving parts of objects arrives with the sandbox verbs (Run 2).",
        "entity.transform" => "Transforming objects arrives with the first magic (Run 3).",
        "effect.start" => "Free-standing effects arrive with the first magic (Run 3).",
        "style.set" => "Restyling the room from a command arrives with the look runtime.",
        "room.checkpoint" or "room.undo" => "Checkpoints and undo arrive with room saves (Run 3).",
        "goal.set" when Str(args, "goal") is "go_to" or "fetch" or "wander" => "That goal arrives with the companion's embodiment (Run 2).",
        _ => null,
    };

    private static (double X, double Y, double Z, double Yaw, string On) ReadPlacement(JsonElement placement, double yaw)
    {
        var position = placement.GetProperty("position_m").EnumerateArray().Select(CanonicalJson.ReadNumber).ToArray();
        if (placement.TryGetProperty("rotation", out var rotation))
        {
            var q = rotation.EnumerateArray().Select(CanonicalJson.ReadNumber).ToArray();
            // Creations stand upright: only a turn about the vertical axis is accepted.
            if (Math.Abs(q[0]) > 1e-4 || Math.Abs(q[2]) > 1e-4)
                throw new Refusal("invalid_args", "Creations stand upright; use a rotation about the vertical axis only.", "$.args.placement.rotation");
            yaw = 2.0 * Math.Atan2(q[1], q[3]) * 180.0 / Math.PI;
            if (yaw > 180.0) yaw -= 360.0;
            if (yaw < -180.0) yaw += 360.0;
            yaw = Math.Round(yaw, 9);
        }
        var on = placement.TryGetProperty("on", out var surface) ? surface.GetString()! : "";
        return (position[0], position[1], position[2], yaw, on);
    }

    private void RateLimit(string principal)
    {
        if (principal != CompanionPrincipal) return;
        var now = Clock();
        while (_companionCalls.Count > 0 && now - _companionCalls.Peek() > TimeSpan.FromSeconds(1)) _companionCalls.Dequeue();
        if (_companionCalls.Count >= CompanionMessagesPerSecond)
            throw new Refusal("rate_limited", "Too many messages; wait a moment.", retryable: true);
        _companionCalls.Enqueue(now);
    }

    // ---- results ----

    private JsonObject Result(string op, string principal, string? actionId, string? queryId, bool ok)
    {
        var result = new JsonObject
        {
            ["schema"] = "enfractal.result", ["version"] = 1, ["ok"] = ok, ["op"] = op, ["principal"] = principal,
            ["room_id"] = Room.RoomId, ["revision"] = Authority == null ? 0 : Revision, ["replayed"] = false, ["preview"] = false, ["at_utc"] = Now(),
        };
        if (actionId != null) result["action_id"] = actionId;
        if (queryId != null) result["query_id"] = queryId;
        return result;
    }

    private JsonObject Fail(string op, string principal, string? actionId, string? queryId, Refusal refusal)
    {
        var result = Result(CommandOps.Contains(op) || QueryOps.Contains(op) ? op : "invalid", principal, actionId, queryId, false);
        var error = new JsonObject
        {
            ["code"] = refusal.Code, ["message"] = KernelJson.DisplayText(refusal.Message, 500), ["retryable"] = refusal.Retryable,
        };
        if (refusal.Path != null) error["field_path"] = KernelJson.DisplayText(refusal.Path, 200);
        if (refusal.Actual != null) error["actual"] = refusal.Actual;
        result["error"] = error;
        return result;
    }

    /// <summary>Maps the GDScript kernel's codes onto the contract's error codes (contracts/README.md).</summary>
    private static Refusal Translate(Godot.Collections.Dictionary failure)
    {
        var code = failure.ContainsKey("code") ? failure["code"].AsString() : "internal_error";
        var path = failure.ContainsKey("path") ? failure["path"].AsString() : "";
        var message = failure.ContainsKey("message") ? failure["message"].AsString() : "The change was refused.";
        var contract = code switch
        {
            "request_invalid" or "operation_invalid" => "request_invalid",
            "field_unknown" => "field_unknown",
            "action_id_invalid" => "action_id_invalid",
            "action_id_conflict" => "action_id_conflict",
            "revision_conflict" or "permission_revision_conflict" => "revision_conflict",
            "save_not_ready" or "configuration_invalid" or "save_missing" or "save_invalid" => "not_ready",
            "principal_unknown" => "principal_unknown",
            "build_denied" or "ownership_denied" or "role_denied" or "activation_denied" or "consent_denied" or "unlock_denied" or "activation_context" => "permission_denied",
            "instance_not_found" => "target_not_found",
            "protected_zone" or "target_locked" => "target_protected",
            "room_bounds" or "placement_invalid" or "surface_unavailable" or "surface_uneven" => "out_of_bounds",
            "occupied_placement" or "avatar_occupied" or "occupancy_unavailable" => "occupied",
            "runtime_budget" or "json_budget" or "part_budget" or "node_budget" or "edge_budget" or "world_capacity" or "owner_capacity" or "avatar_slot_full" => "budget_exceeded",
            "receipt_limit" or "activation_receipt_limit" => "receipt_limit",
            "state_limit" or "save_size" => "state_limit",
            "durable_save_pending" or "save_write" or "save_replace" or "save_directory" => "persistence_uncertain",
            "surface_mismatch" or "surface_target_invalid" or "targets_invalid" or "target_invalid" or "already_locked" or "not_locked" => "invalid_args",
            "internal_error" => "internal_error",
            // Everything else comes from the creation compiler: the source itself is invalid.
            _ => "invalid_args",
        };
        var retryable = contract is "revision_conflict" or "not_ready" or "occupied" or "persistence_uncertain" || code == "runtime_budget";
        var fieldPath = path.StartsWith("$", StringComparison.Ordinal) ? "$.args.source" + path[1..] : path.Length > 0 ? "$.args." + path : null;
        return new Refusal(contract, message, fieldPath, retryable);
    }

    private static string Op(JsonElement root) => root.TryGetProperty("op", out var op) && op.ValueKind == JsonValueKind.String ? op.GetString()! : "invalid";

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsIntegerLiteral(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.GetRawText().All(c => char.IsAsciiDigit(c) || c == '-') && value.TryGetInt64(out _);

    // ---- the player's approval prompt ----

    private void BuildPrompt()
    {
        var layer = new CanvasLayer { Name = "ApprovalPrompt", Layer = 6 };
        AddChild(layer);
        _prompt = new PanelContainer { Name = "ApprovalPanel", Visible = false, Position = new Vector2(380, 240), CustomMinimumSize = new Vector2(520, 0) };
        var style = new StyleBoxFlat { BgColor = new Color(0.09f, 0.08f, 0.06f, 0.95f), ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12 };
        style.SetCornerRadiusAll(10);
        _prompt.AddThemeStyleboxOverride("panel", style);
        layer.AddChild(_prompt);
        var column = new VBoxContainer();
        _prompt.AddChild(column);
        _promptText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(488, 0) };
        column.AddChild(_promptText);
        var row = new HBoxContainer();
        column.AddChild(row);
        var approve = new Button { Name = "ApproveButton", Text = "Approve" };
        approve.Pressed += () => { var next = PendingApprovals.FirstOrDefault(); if (next != null) Approve(next.RequestId); };
        var deny = new Button { Name = "DenyButton", Text = "Deny" };
        deny.Pressed += () => { var next = PendingApprovals.FirstOrDefault(); if (next != null) Deny(next.RequestId); };
        row.AddChild(approve);
        row.AddChild(deny);
    }
}
