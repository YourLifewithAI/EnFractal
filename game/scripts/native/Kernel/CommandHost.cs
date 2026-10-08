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
    /// <summary>The room revision when it was held, for a command that named expected_revision (null otherwise).</summary>
    public int? RoomRevision { get; init; }
    /// <summary>pending, approved, denied or expired (an approval also lapses, as expired, when what it touches changes).</summary>
    public string State { get; internal set; } = "pending";
    public JsonObject? Result { get; internal set; }
    internal JsonObject Held { get; set; } = new();
}

/// <summary>
/// The room's single command path (contracts/game-command.schema.json). Manual controls, the companion
/// adapter and tests send enfractal.command and enfractal.query; the host answers enfractal.result, and
/// always answers: an unexpected failure is internal_error, never an exception out of Handle.
/// The principal comes from the trusted caller, never from the message. The host fingerprints the command
/// as received (SHA-256 of canonical JSON), checks expected_revision and expected_entities itself, keeps
/// transient receipts for goals, stops, activations and the world's physics (bounded per principal, oldest
/// forgotten first), refuses the player-only ops (protect.unlock, world.set_physics) from the companion, holds
/// companion changes to the player's creations for the player's click, and gives the companion only what its
/// avatar can see now or remembers seeing (perception memory). Goals with a target run as jobs the host
/// finishes on arrival. goal.stop and effect.stop always apply: never refused on revisions, rate limits,
/// capacity or a reused action id, and never hiding a receipt under that id. Durable receipts live with the
/// state in the creation authority, which keeps the last 256 for the player.
/// </summary>
public partial class CommandHost : Node
{
    public const string PlayerPrincipal = "player:local";
    public const string CompanionPrincipal = "companion:local";
    public const string PlayerAvatar = "avatar:player";
    public const string CompanionAvatarId = "avatar:companion";
    public const int MaxMessageBytes = 65536;
    public const int MaxResultBytes = 262144;
    /// <summary>Transient receipts kept per principal for the session; the oldest is forgotten first, so nothing fails for capacity.</summary>
    public const int MaxTransientPerPrincipal = 1024;
    public const int MaxPendingApprovals = 8;
    public const int CompanionMessagesPerSecond = 30;
    /// <summary>Perception memory: at most this many entities per companion, the least recently seen forgotten first.</summary>
    public const int PerceptionMemoryEntries = 256;
    /// <summary>A remembered entity is marked may_be_stale once its memory is this old (or as soon as it changed).</summary>
    public const int PerceptionMemoryStaleAfterS = 60;
    /// <summary>Goal jobs kept per principal for jobs.status; the oldest finished is dropped first.</summary>
    public const int MaxJobsPerPrincipal = 256;
    /// <summary>On arrival, a target farther than this from where the goal aimed has moved (an avatar's reach).</summary>
    public const float ArrivalReachM = 0.15f;
    /// <summary>A go_to stops with the body's centre this close to the target's footprint (within the 10 cm body's reach).</summary>
    public const float GoToStopM = 0.08f;
    public static readonly TimeSpan ApprovalLifetime = TimeSpan.FromMinutes(5);
    public const string RuntimeScript = "res://scripts/invention_runtime.gd";

    // \A and \z, never ^ and $: in .NET, $ also matches before a final newline (Lane P review).
    private static readonly Regex ActionId = new(@"\A[A-Za-z0-9][A-Za-z0-9_-]{0,63}\z", RegexOptions.Compiled);
    private static readonly Regex Token = new(@"\A[a-z][a-z0-9_-]{0,63}\z", RegexOptions.Compiled);
    private static readonly Regex EntityId = new(@"\A(shell|obj|creation|avatar|effect|edit):[A-Za-z0-9_-]{1,64}\z", RegexOptions.Compiled);
    private static readonly Regex AvatarId = new(@"\Aavatar:[A-Za-z0-9_-]{1,64}\z", RegexOptions.Compiled);
    private static readonly Regex CreationId = new(@"\Acreation:[A-Za-z0-9_-]{1,64}\z", RegexOptions.Compiled);
    private static readonly Regex EffectId = new(@"\Aeffect:[A-Za-z0-9_-]{1,64}\z", RegexOptions.Compiled);
    private static readonly Regex RequestIdPattern = new(@"\A[a-f0-9]{32,64}\z", RegexOptions.Compiled);
    private static readonly Regex JobIdPattern = new(@"\Ajob-[a-z2-7]{26}\z", RegexOptions.Compiled);
    private static readonly Regex ParamName = new(@"\A[a-z][a-z0-9_]{0,31}\z", RegexOptions.Compiled);
    private static readonly Regex PathSegment = new(@"\A[A-Za-z0-9_.:-]{1,64}\z", RegexOptions.Compiled);
    private static readonly Regex ManifestPrefix = new(@"\A[0-9a-f]{16}\z", RegexOptions.Compiled);
    private static readonly HashSet<string> CommandOps = new()
    {
        "entity.grab", "entity.release", "entity.place", "entity.push", "entity.set_part", "entity.remove", "entity.transform",
        "creation.place", "creation.revise", "creation.activate", "protect.lock", "protect.unlock",
        "goal.set", "goal.stop", "effect.start", "effect.stop", "style.set", "room.checkpoint", "room.undo", "world.set_physics",
    };
    private static readonly HashSet<string> QueryOps = new()
    {
        "room.describe", "entities.list", "entity.inspect", "capabilities.list", "observe", "jobs.status", "receipt.lookup", "approval.status",
    };
    private static readonly HashSet<string> DestructiveOps = new() { "entity.remove", "entity.transform", "creation.revise", "protect.lock", "protect.unlock" };
    /// <summary>
    /// Ops that change no saved state, so their receipts are transient (contracts/README.md "Receipts"): goals, stops,
    /// effects, grabs, activations and, while it is not saved, the world's physics. Never entity.release: putting a
    /// thing down is saved state (kernel-host-gaps P8). Transient() refuses any other op.
    /// </summary>
    private static readonly HashSet<string> TransientOps = new() { "goal.set", "goal.stop", "effect.start", "effect.stop", "entity.grab", "creation.activate", "world.set_physics" };
    /// <summary>contracts $defs/player_only_ops: refused from a companion with permission_denied, never held.</summary>
    private static readonly HashSet<string> PlayerOnlyOps = new() { "protect.unlock", "world.set_physics" };
    private static readonly HashSet<string> StopOps = new() { "goal.stop", "effect.stop" };
    /// <summary>Goals that only move or turn the companion: they may aim at something it remembers but cannot see now, re-checked on arrival.</summary>
    private static readonly HashSet<string> RememberedTargetGoals = new() { "go_to", "look_at", "point_at", "come", "fetch" };
    /// <summary>What perception memory keeps: everything but the shell, which is always in sight.</summary>
    private static readonly HashSet<string> RememberedKinds = new() { "object", "creation", "avatar", "effect" };
    /// <summary>The goals whose arrival the host watches on the companion's body itself; go_to and fetch arrive through ReportArrival (A2).</summary>
    private static readonly HashSet<string> HostDrivenGoals = new() { "follow", "come", "look_at", "point_at", "go_to" };
    private static readonly HashSet<string> Affordances = new()
    {
        "walkable_top", "climbable", "sittable", "openable", "container", "soft", "breakable", "light_source", "switchable", "screen", "readable", "rideable", "hazard",
    };
    private static readonly string[] ReservedParams = { "principal", "action_id", "approval", "approval_id", "approved", "owner", "owner_id", "room_id", "actor", "grant", "role" };

    public RoomData Room { get; private set; } = null!;
    public SmallPlayerController? Player { get; private set; }
    public CompanionAvatar? Companion { get; private set; }
    public StylePreset? Style { get; private set; }
    public Node3D Runtime { get; private set; } = null!;
    public GodotObject Authority { get; private set; } = null!;
    public string SavePath { get; private set; } = "";
    /// <summary>Set when this room has saved creations under another manifest hash (the room was re-exported); shown to the player.</summary>
    public string SaveNotice { get; private set; } = "";
    /// <summary>Test seam for approval expiry; the game uses the system clock.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;
    /// <summary>Test seam: sees (and may spoil) every result before the host checks that it serialises.</summary>
    internal Action<JsonObject>? BeforeAnswerForTests { get; set; }
    /// <summary>Raised when the host starts holding a command for the player.</summary>
    public event Action<PendingApproval>? ApprovalRequested;
    /// <summary>Raised when a goal's job ends: actor, job id and its state (succeeded, failed or cancelled).</summary>
    public event Action<string, string, string>? GoalFinished;
    public IReadOnlyList<PendingApproval> PendingApprovals => _approvals.Values.Where(a => a.State == "pending").OrderBy(a => a.Created).ToList();
    /// <summary>Perception memory's size per companion (PerceptionMemoryEntries; 0 turns memory off). Tests lower it.</summary>
    public int PerceptionMemoryLimit { get; internal set; } = PerceptionMemoryEntries;
    /// <summary>The age at which a memory is marked may_be_stale (PerceptionMemoryStaleAfterS). Tests lower it.</summary>
    public TimeSpan PerceptionMemoryStaleAfter { get; internal set; } = TimeSpan.FromSeconds(PerceptionMemoryStaleAfterS);
    /// <summary>Goal jobs kept per principal (MaxJobsPerPrincipal). Tests lower it.</summary>
    public int JobLimit { get; internal set; } = MaxJobsPerPrincipal;

    private readonly Dictionary<string, (string Fingerprint, JsonObject Result)> _transient = new();
    private readonly Dictionary<string, LinkedList<string>> _transientOrder = new();
    private readonly Dictionary<string, PendingApproval> _approvals = new();
    private readonly Dictionary<string, string> _approvalByAction = new();
    private readonly Queue<DateTime> _companionCalls = new();
    private string _session = "";
    private int _counter;
    private PanelContainer? _prompt;
    private Label? _promptText;
    /// <summary>What the requesting principal perceives, computed once per message; null until needed.</summary>
    private HashSet<string>? _perceived;
    /// <summary>True while the player's click commits a held command: the player approved it, so the companion's sight no longer matters.</summary>
    private bool _approving;
    /// <summary>Perception memory per companion principal. In memory only: never in a receipt, a snapshot or a save.</summary>
    private readonly Dictionary<string, PerceptionMemory> _memory = new();
    /// <summary>Goal jobs per principal, oldest first. Job ids are opaque random tokens (contracts: common job_id), never counters, so they say nothing about how many jobs exist or whose they are.</summary>
    private readonly Dictionary<string, List<GoalJob>> _jobs = new();
    /// <summary>The running job of each actor's current goal (at most one: a new goal or a stop cancels it).</summary>
    private readonly Dictionary<string, GoalJob> _runningGoals = new();
    private Func<string, bool>? _physicsSink;
    private Func<string, bool>? _previousPhysicsSink;

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
    public static string SavePathFor(RoomData room) => $"{SaveRoot}/{room.RoomId}/{room.ManifestSha256[..16]}/inventions.json";

    /// <summary>The host attached under a room world, if any.</summary>
    public static CommandHost? Of(Node world) => world.GetNodeOrNull<CommandHost>("CommandHost");

    public override void _Ready()
    {
        _session = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        Runtime = (Node3D)GD.Load<GDScript>(RuntimeScript).New().AsGodotObject();
        Runtime.Name = "InventionRuntime";
        Runtime.Set("save_path", SavePath);
        // The workshop (INVENTIONS panel, editor, keys B F E V Q K) is retired for players: the founder's second
        // playtest found its Q and E blocking the isometric view's turn keys. The runtime still renders and runs creations.
        Runtime.Set("workshop_enabled", false);
        Runtime.Call("configure", RoomDictionary(Room), Player!, Companion!);
        // The scene is derived: the authority hands every saved object pose to the room's nodes, while a save loads too.
        Runtime.Set("object_pose_sink", new Callable(this, MethodName.ApplyObjectPoses));
        // A save's object poses are checked against the room as it is before they are believed (asset size, collision, support).
        Runtime.Set("object_pose_check", new Callable(this, MethodName.CheckObjectPoses));
        AddChild(Runtime);
        Authority = Runtime.Get("authority").AsGodotObject();
        // Whatever the load did, the scene shows the authority's state: a save that failed to load leaves the manifest's places.
        ApplyObjectPoses(ObjectPoses());
        Runtime.Set("command_sink", new Callable(this, MethodName.RuntimeCommand));
        SaveNotice = OtherManifestNotice(SavePath);
        if (SaveNotice.Length > 0)
        {
            GD.Print("COMMAND_HOST " + SaveNotice);
            Runtime.Call("notice", SaveNotice);
        }
        // The G key sends world.set_physics through this host as the player: one command path for every world change.
        _physicsSink = preset => IsInstanceValid(this) && HandleObjectOk(PlayerPhysics(preset));
        if (Player != null)
        {
            _previousPhysicsSink = Player.WorldPhysicsRequest;
            Player.WorldPhysicsRequest = _physicsSink;
        }
        BuildPrompt();
        AddChild(new Sandbox.Carrying { Name = "Carrying", Carried = CarriedNow });
    }

    public override void _ExitTree()
    {
        // A host freed while another is attached (a test's second host) hands the key back.
        if (Player != null && IsInstanceValid(Player) && Player.WorldPhysicsRequest == _physicsSink) Player.WorldPhysicsRequest = _previousPhysicsSink;
    }

    private static bool HandleObjectOk(JsonObject result) => result["ok"]?.GetValue<bool>() == true;

    /// <summary>
    /// A room re-exported with a new manifest gets a fresh save folder, so its old creations silently vanished
    /// (Lane P review). Until saves migrate between manifests, the player is told when other manifests of this
    /// room have saved creations, which stay untouched on disk.
    /// </summary>
    public static string OtherManifestNotice(string savePath)
    {
        var file = savePath.GetFile();
        var manifestDirectory = savePath.GetBaseDir();
        var roomDirectory = manifestDirectory.GetBaseDir();
        using var directory = DirAccess.Open(roomDirectory);
        if (directory == null) return "";
        var others = directory.GetDirectories()
            .Where(name => name != manifestDirectory.GetFile() && ManifestPrefix.IsMatch(name) && Godot.FileAccess.FileExists($"{roomDirectory}/{name}/{file}"))
            .OrderBy(name => name, StringComparer.Ordinal).ToList();
        if (others.Count is 0) return "";
        return $"Creations saved for an earlier version of this room (manifest {string.Join(", ", others.Take(3))}) were not loaded, because the room changed since. They are kept on disk.";
    }

    public override void _Process(double delta)
    {
        // A lapse shows at once: the prompt never offers a request whose entities have changed.
        foreach (var approval in _approvals.Values) Refresh(approval);
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
        _perceived = null;
        try { return HandleChecked(message, principal); }
        catch (Exception error)
        {
            // The host always answers (Lane P review: a result that could not be serialised threw out of Handle).
            GD.Print("COMMAND_HOST internal error: " + error.GetType().Name);
            return InternalError("invalid", principal);
        }
        finally { _perceived = null; }
    }

    private JsonObject HandleChecked(string message, string principal)
    {
        // The companion's rate limit comes before parsing and every other check, so a flood of invalid messages
        // is limited too. Stops are exempt: when no token is left, only text that may be a stop is parsed at all.
        var companion = principal == CompanionPrincipal;
        if (companion && !TokensLeft() && !MightBeStop(message)) return RateLimited(principal);
        if (message.Length > MaxMessageBytes * 4)
            return companion && !TakeToken() ? RateLimited(principal) : Fail("invalid", principal, null, null, new Refusal("request_invalid", "The message is larger than 64 KiB."));
        JsonDocument document;
        try { document = CanonicalJson.Parse(message); }
        catch (CanonicalJsonException error)
        {
            return companion && !TakeToken() ? RateLimited(principal)
                : Fail("invalid", principal, null, null, new Refusal("request_invalid", "The message is not strict JSON: " + error.Message));
        }
        using (document)
        {
            var root = document.RootElement;
            var stop = root.ValueKind == JsonValueKind.Object && Str(root, "schema") == "enfractal.command" && StopOps.Contains(Op(root));
            if (companion && !stop && !TakeToken()) return RateLimited(principal);
            if (root.ValueKind != JsonValueKind.Object)
                return Fail("invalid", principal, null, null, new Refusal("request_invalid", "A message is a JSON object."));
            var canonical = CanonicalJson.Bytes(root);
            var schema = Str(root, "schema");
            JsonObject result;
            if (schema == "enfractal.command") result = HandleCommand(root, canonical, principal);
            else if (schema == "enfractal.query") result = HandleQuery(root, canonical, principal);
            else result = Fail("invalid", principal, null, null, new Refusal("request_invalid", "Send an enfractal.command or an enfractal.query.", "$.schema"));
            BeforeAnswerForTests?.Invoke(result);
            byte[] bytes;
            try { bytes = CanonicalJson.Bytes(result); }
            catch (CanonicalJsonException) { return InternalError(Op(root), principal); }
            if (bytes.Length > MaxResultBytes)
                result = Fail(Op(root), principal, null, null, new Refusal("internal_error", "The answer would be larger than 256 KiB."));
            return result;
        }
    }

    private JsonObject InternalError(string op, string principal) =>
        Fail(op, principal, null, null, new Refusal("internal_error", "The game could not handle that request.", retryable: true));

    /// <summary>The GDScript runtime's command sink: manual edits from the invention editor, always as the player.</summary>
    public Godot.Collections.Dictionary RuntimeCommand(Godot.Collections.Dictionary command)
    {
        string text;
        try { text = CanonicalJson.Text(KernelJson.ToJson(command)); }
        catch (CanonicalJsonException error) { text = "{\"schema\":\"invalid\",\"reason\":" + JsonSerializer.Serialize(error.Message) + "}"; }
        return KernelJson.ToVariant(HandleObject(text, PlayerPrincipal)).AsGodotDictionary();
    }

    /// <summary>Transient receipts the host keeps for principal this session.</summary>
    public int TransientReceiptCount(string principal) => _transientOrder.TryGetValue(principal, out var order) ? order.Count : 0;

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
        return PlayerCommand(op, args);
    }

    /// <summary>The G key: world.set_physics from the player (player-only, transient, the revision does not move).</summary>
    public JsonObject PlayerPhysics(string preset)
    {
        var result = PlayerCommand("world.set_physics", new JsonObject { ["preset"] = preset });
        if (HandleObjectOk(result)) Player?.PrintWorldPhysics();
        return result;
    }

    private JsonObject PlayerCommand(string op, JsonObject args)
    {
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
        if (!_approvals.TryGetValue(requestId, out var approval))
            return Fail("approval.status", PlayerPrincipal, null, null, new Refusal("target_not_found", "There is no waiting request with that id."));
        Refresh(approval);
        if (approval.State != "pending")
            return approval.Result != null && approval.State != "approved" ? (JsonObject)approval.Result.DeepClone()
                : Fail("approval.status", PlayerPrincipal, null, null, new Refusal("target_not_found", "There is no waiting request with that id."));
        using var document = JsonDocument.Parse(approval.CommandText);
        JsonObject result;
        _perceived = null;
        _approving = true;
        try { result = Execute(document.RootElement, approval.Principal, approval.Fingerprint, PlayerPrincipal); }
        finally { _approving = false; _perceived = null; }
        if (result["ok"]!.GetValue<bool>())
        {
            approval.State = "approved";
            approval.Result = result;
            return result;
        }
        // Approved, but it can no longer be made as asked (an unrelated change met its expected_revision, a full
        // ledger): a clear terminal state, never "approved" with ok false, and never an invitation to resend it.
        var code = result["error"]?["code"]?.GetValue<string>();
        Finish(approval, code == "receipt_limit"
            ? new Refusal("receipt_limit", "The receipt ledger is full; nothing was changed. A room.checkpoint compacts it; then ask again with a new action_id.")
            : new Refusal("approval_mismatch", "The change can no longer be made as approved; nothing was changed. To ask again, use a new action_id."));
        return (JsonObject)approval.Result!.DeepClone();
    }

    public JsonObject Deny(string requestId)
    {
        if (!_approvals.TryGetValue(requestId, out var approval))
            return Fail("approval.status", PlayerPrincipal, null, null, new Refusal("target_not_found", "There is no waiting request with that id."));
        Refresh(approval);
        if (approval.State != "pending")
            return Fail("approval.status", PlayerPrincipal, null, null, new Refusal("target_not_found", "There is no waiting request with that id."));
        Finish(approval, new Refusal("permission_denied", "The player declined this request. To ask again, use a new action_id."), "denied");
        return (JsonObject)approval.Result!.DeepClone();
    }

    // ---- commands ----

    private JsonObject HandleCommand(JsonElement root, byte[] canonical, string principal)
    {
        var op = Op(root);
        var actionId = root.TryGetProperty("action_id", out var a) && a.ValueKind == JsonValueKind.String && ActionId.IsMatch(a.GetString()!) ? a.GetString() : null;
        var stop = StopOps.Contains(op);
        try
        {
            CheckEnvelope(root, command: true, stop);
            if (canonical.Length > MaxMessageBytes) throw new Refusal("request_invalid", "A command is at most 65,536 bytes of canonical JSON.");
            CheckText(root, "$", stop);
            var fingerprint = CanonicalJson.Sha256Hex(canonical);
            // A stop always applies, whatever an earlier use of its action id says: stopping twice is harmless.
            if (!stop)
            {
                // Every companion message is a look: what its avatar sees now fills its perception memory. Stops skip
                // it, so the stops the rate limit exempts never cost a sweep of rays.
                if (principal == CompanionPrincipal) Look(principal);
                var replay = Replay(op, principal, actionId!, fingerprint);
                if (replay != null) return replay;
                var held = HeldAnswer(principal, actionId!, fingerprint);
                if (held != null) return held;
            }
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
            // Player-only ops are refused outright from the companion, never held for a click.
            if (PlayerOnlyOps.Contains(op) && principal != PlayerPrincipal)
                throw new Refusal("permission_denied", op == "protect.unlock"
                    ? "Only the player can unlock, directly in the game." : "Only the player can change the world's physics, directly in the game.", "$.op");
            var unsupported = Unsupported(op, args, principal);
            if (unsupported != null) throw new Refusal("unsupported_capability", unsupported, "$.op");
            if (!StopOps.Contains(op)) CheckExpectations(root, op, args, principal);
            CheckPerceived(op, args, principal);
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
                "room.checkpoint" => Checkpoint(args, principal, actionId, meta, preview),
                "world.set_physics" => SetPhysics(args, principal, actionId, fingerprint, preview),
                "entity.grab" => Grab(args, principal, actionId, fingerprint, preview),
                "entity.release" => Release(args, principal, actionId, meta, preview),
                "entity.place" => PlaceObject(args, principal, actionId, meta, preview),
                "entity.push" => PushObject(args, principal, actionId, meta, preview),
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
        // A held thing could be carried off and put down elsewhere after the lock: put it down first (as the mock).
        if (op == "protect.lock" && targets.Any(t => _held.Values.Any(h => h.Target == t)))
            throw new Refusal("target_busy", "Someone is holding that; it can be protected once it is put down.", "$.args.targets");
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
        var data = new JsonObject { ["actor"] = actor, ["goal"] = goal };
        string? target = null;
        Aabb aim = default;
        if (args.TryGetProperty("target", out var named))
        {
            target = named.GetString()!;
            // Out of the companion's sight, a goal that only moves or turns it aims at the memory alone (CheckPerceived
            // admitted only those), so the answer cannot say what became of the thing since it was seen.
            var remembered = principal != PlayerPrincipal && !_approving && !Perceives(principal, target) ? Recall(principal, target) : null;
            if (remembered != null)
            {
                aim = BoundsOf(remembered.Summary);
                data["target_seen"] = "remembered";
                data["last_seen_ago_s"] = AgeSeconds(remembered);
                data["may_be_stale"] = MayBeStale(remembered);
            }
            else
            {
                var summary = Entities().FirstOrDefault(e => e["id"]!.GetValue<string>() == target)
                    ?? throw new Refusal("target_not_found", "That is not in this room.", "$.args.target");
                aim = BoundsOf(summary);
                if (principal != PlayerPrincipal) data["target_seen"] = "now";
            }
        }
        Vector3? point = null;
        if (goal is "look_at" or "point_at") point = target != null ? aim.GetCenter() : KernelJson.ReadVector(args.GetProperty("position_m"));
        if (preview) return Previewed("goal.set", principal, actionId, actor);
        // A new goal replaces the old one, and with it the old goal's job.
        CancelGoal(actor);
        switch (goal)
        {
            case "follow": Companion.Follow(); break;
            case "stay": Companion.Stay(); break;
            case "come": Companion.Come(); break;
            case "look_at": Companion.LookAtPoint(point!.Value); break;
            case "point_at": Companion.PointAt(point!.Value); break;
            case "go_to":
                Companion.GoTo(target != null ? aim : new Aabb(KernelJson.ReadVector(args.GetProperty("position_m")), Vector3.Zero), GoToStopM);
                break;
        }
        // A goal with a target runs as a job: the host re-checks the target when the avatar arrives.
        var job = target != null ? StartJob(principal, actor, actionId, goal, target, aim, Companion.IntentSerial) : null;
        return Transient("goal.set", principal, actionId, fingerprint, new JsonArray(actor), data, job?.Id);
    }

    /// <summary>
    /// Stops goals and effects. The player's stop with no actor covers everything the player directs: the
    /// companion's goals and every effect. Naming an avatar covers that avatar's goals and effects. The
    /// companion's stop covers only its own; it may not name the player's avatar.
    /// </summary>
    private JsonObject GoalStop(JsonElement args, string principal, string actionId, string fingerprint, bool preview)
    {
        var actor = args.TryGetProperty("actor", out var a) ? a.GetString() : null;
        if (actor != null && actor != PlayerAvatar && actor != CompanionAvatarId)
            throw new Refusal("target_not_found", "There is no such actor in this room.", "$.args.actor");
        if (actor == PlayerAvatar && principal != PlayerPrincipal)
            throw new Refusal("actor_denied", "A companion may not direct the player.", "$.args.actor");
        if (preview) return Previewed("goal.stop", principal, actionId);
        var whose = actor == null ? (principal == PlayerPrincipal ? "" : principal) : actor == CompanionAvatarId ? CompanionPrincipal : PlayerPrincipal;
        var affected = new JsonArray();
        if (whose != PlayerPrincipal && Companion != null)
        {
            CancelGoal(CompanionAvatarId);
            Companion.Stop();
            affected.Add(CompanionAvatarId);
        }
        var stopped = Runtime.Call("stop_effects", whose).AsInt32();
        return Transient("goal.stop", principal, actionId, fingerprint, affected, new JsonObject { ["effects_stopped"] = stopped });
    }

    private JsonObject EffectStop(JsonElement args, string principal, string actionId, string fingerprint, bool preview)
    {
        var effect = Str(args, "effect")!;
        if (preview) return Previewed("effect.stop", principal, actionId);
        // Creation effects have no effect: ids yet, so a named effect is already stopped. "all" is everything for
        // the player and the companion's own effects for the companion.
        var stopped = effect == "all" ? Runtime.Call("stop_effects", principal == PlayerPrincipal ? "" : principal).AsInt32() : 0;
        return Transient("effect.stop", principal, actionId, fingerprint, new JsonArray(), new JsonObject { ["effects_stopped"] = stopped });
    }

    /// <summary>room.checkpoint: compacts the durable ledger in the authority. It changes no world state and never fails for a full ledger.</summary>
    private JsonObject Checkpoint(JsonElement args, string principal, string actionId, Godot.Collections.Dictionary meta, bool preview)
    {
        if (preview) return Previewed("room.checkpoint", principal, actionId);
        var request = new Godot.Collections.Dictionary
        {
            ["op"] = "checkpoint", ["action_id"] = actionId, ["label"] = Str(args, "label") ?? "",
            ["expected_revision"] = Revision, ["expected_permission_revision"] = PermissionRevision,
        };
        return Submit(request, principal, actionId, meta);
    }

    /// <summary>
    /// world.set_physics (player-only): the room's gravity, air and wind become a world_physics_profile.gd preset. Not
    /// saved in room state yet, so the receipt is transient and the room revision does not move. Both bodies adopt it.
    /// </summary>
    private JsonObject SetPhysics(JsonElement args, string principal, string actionId, string fingerprint, bool preview)
    {
        var preset = Str(args, "preset")!;
        var script = GD.Load<GDScript>(SmallPlayerController.WorldPhysicsScript);
        var presets = script.GetScriptConstantMap()["PRESET_IDS"].AsGodotArray().Select(id => id.AsString()).ToArray();
        if (!presets.Contains(preset))
            throw new Refusal("invalid_args", "The game has no world physics preset by that name.", "$.args.preset",
                allowed: new JsonArray(presets.OrderBy(id => id, StringComparer.Ordinal).Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()));
        if (Player == null || !IsInstanceValid(Player)) throw new Refusal("not_ready", "The player's body is not in the room yet.", retryable: true);
        if (preview) return Previewed("world.set_physics", principal, actionId);
        var profile = script.Call("preset", preset, Player.WorldPhysicsRevision + 1).AsGodotDictionary();
        if (!Player.SetWorldPhysics(profile)) throw new Refusal("internal_error", "The game could not change the world's physics.", retryable: true);
        // The companion lives under the same world physics; it would also adopt the player's newer profile next tick.
        if (Companion != null && IsInstanceValid(Companion)) Companion.SetWorldPhysics(profile);
        return Transient("world.set_physics", principal, actionId, fingerprint, new JsonArray(), null);
    }

    private JsonObject Submit(Godot.Collections.Dictionary request, string principal, string actionId, Godot.Collections.Dictionary meta)
    {
        var outcome = Authority.Call("submit", principal, request, meta).AsGodotDictionary();
        if (!outcome["ok"].AsBool()) throw Translate(outcome);
        // The scene is derived at once: a placed, revised or removed creation's colliders are where the state says before
        // the next command looks (object poses reach the scene through the authority's pose seam during the commit).
        Runtime.Call("refresh_now");
        // The first answer is rebuilt from the durable receipt, exactly as every replay will be.
        var record = Authority.Call("receipt_for", principal, actionId).AsGodotDictionary();
        return Durable(record, replayed: false);
    }

    /// <summary>
    /// A transient receipt for the session. Never fails: each principal keeps its latest MaxTransientPerPrincipal and
    /// the oldest is forgotten, so a full store can never turn an action that already happened into a refusal.
    /// </summary>
    private JsonObject Transient(string op, string principal, string actionId, string fingerprint, JsonArray affected, JsonObject? data, string? jobId = null)
    {
        // Only ops that change no saved state answer with a transient receipt (P8: never entity.release).
        if (!TransientOps.Contains(op)) throw new InvalidOperationException(op + " keeps a durable receipt");
        var result = Result(op, principal, actionId, null, true);
        result["transient"] = true;
        if (affected.Count > 0) result["affected"] = affected;
        if (jobId != null) result["job_id"] = jobId;
        if (data != null) result["data"] = data;
        var key = principal + "|" + actionId;
        if (!_transientOrder.TryGetValue(principal, out var order)) _transientOrder[principal] = order = new LinkedList<string>();
        if (_transient.ContainsKey(key)) order.Remove(key);
        _transient[key] = (fingerprint, (JsonObject)result.DeepClone());
        order.AddLast(key);
        while (order.Count > MaxTransientPerPrincipal)
        {
            _transient.Remove(order.First!.Value);
            order.RemoveFirst();
        }
        return result;
    }

    private JsonObject Previewed(string op, string principal, string actionId, params string[] affected)
    {
        var result = Result(op, principal, actionId, null, true);
        result["preview"] = true;
        if (affected.Length > 0) result["affected"] = new JsonArray(affected.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
        return result;
    }

    /// <summary>Whether op answers with a transient receipt (tests pin the contract's list, kernel-host-gaps P8).</summary>
    internal static bool IsTransientOp(string op) => TransientOps.Contains(op);

    /// <summary>
    /// The answer for an action id this principal already committed, or null if it is new. Durable receipts come
    /// first, then compacted ones, then transient ones: a stop that reused a durable command's action id (stops
    /// apply under any id) keeps a transient receipt under it, which must never hide the durable one (P2).
    /// </summary>
    private JsonObject? Replay(string op, string principal, string actionId, string fingerprint)
    {
        var record = Authority.Call("receipt_for", principal, actionId).AsGodotDictionary();
        if (record.Count > 0)
        {
            if (record["fingerprint"].AsString() != fingerprint)
                throw new Refusal("action_id_conflict", "This action id was already used for different content.", "$.action_id");
            return Durable(record, replayed: true);
        }
        // A checkpoint compacted it: it still replays, and still refuses other content under its id.
        var compacted = Authority.Call("compacted_receipt", principal, actionId).AsGodotDictionary();
        if (compacted.Count > 0)
        {
            if (compacted["fingerprint_prefix"].AsString() != fingerprint[..16])
                throw new Refusal("action_id_conflict", "This action id was already used for different content.", "$.action_id");
            var replayed = Result(op, principal, actionId, null, true);
            replayed["revision"] = compacted["revision"].AsInt32();
            replayed["replayed"] = true;
            replayed["transient"] = false;
            // Same fingerprint, same command: a compacted checkpoint still answers the revision it compacted at.
            if (op == "room.checkpoint") replayed["data"] = new JsonObject { ["checkpoint_revision"] = compacted["revision"].AsInt32() };
            return replayed;
        }
        if (!_transient.TryGetValue(principal + "|" + actionId, out var transient)) return null;
        if (transient.Fingerprint != fingerprint)
            throw new Refusal("action_id_conflict", "This action id was already used for different content.", "$.action_id");
        var copy = (JsonObject)transient.Result.DeepClone();
        copy["replayed"] = true;
        return copy;
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
        if (meta["op"].AsString() == "room.checkpoint") result["data"] = new JsonObject { ["checkpoint_revision"] = receipt["revision"].AsInt32() };
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
            RoomRevision = root.TryGetProperty("expected_revision", out _) ? Revision : null,
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

    /// <summary>
    /// The answer for a command that is (or was) held: the same request while pending; once denied, expired or
    /// lapsed, the same final refusal (not retryable) for that action id, which says to use a new one.
    /// </summary>
    private JsonObject? HeldAnswer(string principal, string actionId, string fingerprint)
    {
        if (!_approvalByAction.TryGetValue(principal + "|" + actionId, out var requestId)) return null;
        var approval = _approvals[requestId];
        Refresh(approval);
        if (approval.Fingerprint != fingerprint)
            throw new Refusal("action_id_conflict", "This action id was already used for different content.", "$.action_id");
        if (approval.State == "pending")
        {
            var held = (JsonObject)approval.Held.DeepClone();
            held["at_utc"] = Now();
            return held;
        }
        if (approval.Result == null) return null;
        var final = (JsonObject)approval.Result.DeepClone();
        final["replayed"] = true;
        final["at_utc"] = Now();
        return final;
    }

    /// <summary>A pending approval expires after its lifetime, and lapses (expired) as soon as an entity it touches, or the room revision it expected, changes.</summary>
    private void Refresh(PendingApproval approval)
    {
        if (approval.State != "pending") return;
        if (Clock() >= approval.Expires)
            Finish(approval, new Refusal("approval_expired", "The player did not answer in time. To ask again, use a new action_id."));
        else if (approval.Touched.Any(t => EntityRevision(t.Key) != t.Value) || (approval.RoomRevision is { } room && room != Revision))
            Finish(approval, new Refusal("approval_mismatch", "What the request would change has changed since it was asked; nothing was changed. To ask again, use a new action_id."));
    }

    private void Finish(PendingApproval approval, Refusal refusal, string state = "expired")
    {
        approval.State = state;
        approval.Result = Fail(approval.Op, approval.Principal, approval.ActionId, null, refusal);
    }

    // ---- queries ----

    private JsonObject HandleQuery(JsonElement root, byte[] canonical, string principal)
    {
        var op = Op(root);
        var queryId = root.TryGetProperty("query_id", out var q) && q.ValueKind == JsonValueKind.String && ActionId.IsMatch(q.GetString()!) ? q.GetString() : null;
        try
        {
            CheckEnvelope(root, command: false, stop: false);
            if (canonical.Length > MaxMessageBytes) throw new Refusal("request_invalid", "A query is at most 65,536 bytes of canonical JSON.");
            CheckText(root, "$", stop: false);
            var args = root.GetProperty("args");
            CheckQueryArgs(op, args);
            if (principal == CompanionPrincipal) Look(principal);
            var result = Result(op, principal, null, queryId, true);
            result["data"] = op switch
            {
                "room.describe" => Describe(principal),
                "entities.list" => List(args, principal),
                "entity.inspect" => Inspect(args, principal),
                "capabilities.list" => Capabilities(args),
                "observe" => Observe(args, principal),
                "receipt.lookup" => Lookup(args, principal),
                "approval.status" => Status(args, principal),
                "jobs.status" => JobStatus(args, principal),
                _ => throw new Refusal("request_invalid", "That operation does not exist.", "$.op"),
            };
            return result;
        }
        catch (Refusal refusal) { return Fail(op, principal, null, queryId, refusal); }
    }

    /// <summary>The room's identity and counts; for the companion the counts are of what it can see, never of what is hidden.</summary>
    private JsonObject Describe(string principal)
    {
        var style = Style ?? StylePreset.Resolve(RoomWorld.DefaultStyleId, RoomWorld.DefaultStyleVersion);
        var entities = Visible(principal);
        return new JsonObject
        {
            ["room_id"] = Room.RoomId, ["display_name"] = KernelJson.DisplayText(Room.DisplayName, 80), ["revision"] = Revision,
            ["source_kind"] = Room.SourceKind, ["bounds_m"] = KernelJson.Box(Room.Bounds),
            ["style"] = new JsonObject { ["preset_id"] = style.PresetId, ["preset_version"] = style.PresetVersion, ["preset_sha256"] = style.Sha256 },
            ["counts"] = new JsonObject
            {
                ["objects"] = entities.Count(e => e["kind"]!.GetValue<string>() == "object"),
                ["creations"] = entities.Count(e => e["kind"]!.GetValue<string>() == "creation"),
                ["shell_parts"] = entities.Count(e => e["kind"]!.GetValue<string>() == "shell"),
            },
        };
    }

    private JsonObject List(JsonElement args, string principal)
    {
        // For the companion: what it sees now (seen "now") and what it remembers out of sight (seen "remembered"),
        // one list by id. Filters apply to what it saw, never to a remembered entity's state now.
        IEnumerable<JsonObject> items = Visible(principal)
            .Concat(RememberedOutOfSight(principal).Select(r => RememberedSummary(r.Value)))
            .OrderBy(e => e["id"]!.GetValue<string>(), StringComparer.Ordinal);
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

    private JsonObject Inspect(JsonElement args, string principal)
    {
        var target = Str(args, "target")!;
        var entity = Visible(principal).FirstOrDefault(e => e["id"]!.GetValue<string>() == target);
        if (entity == null)
        {
            // Out of sight: the companion's memory of it, exactly as it was seen; otherwise byte-identical to not in the room.
            var remembered = principal == PlayerPrincipal ? null : Recall(principal, target);
            if (remembered == null) throw new Refusal("target_not_found", "That is not in this room.", "$.args.target");
            var memory = new JsonObject { ["entity"] = RememberedSummary(remembered) };
            if (remembered.ProtectedBy.Length > 0) memory["protected_by"] = remembered.ProtectedBy;
            return memory;
        }
        var data = new JsonObject { ["entity"] = entity };
        if (LockOwners().TryGetValue(target, out var owner)) data["protected_by"] = owner;
        return data;
    }

    /// <summary>Effect capabilities as contract capability_summary items: none until effect.start arrives (Run 3), kernel-host-gaps P1.</summary>
    private static JsonObject Capabilities(JsonElement args)
    {
        if (args.TryGetProperty("cursor", out var cursor) && cursor.GetString() != "0")
            throw new Refusal("invalid_args", "That cursor is not from this list.", "$.args.cursor");
        return new JsonObject { ["items"] = new JsonArray() };
    }

    private JsonObject Observe(JsonElement args, string principal)
    {
        var actor = Str(args, "actor")!;
        if (principal == CompanionPrincipal && actor != CompanionAvatarId)
            throw new Refusal("actor_denied", "A companion observes only through its own avatar.", "$.args.actor");
        SmallPlayerController? body = actor == CompanionAvatarId ? Companion : actor == PlayerAvatar ? Player : null;
        if (body == null) throw new Refusal("target_not_found", "There is no such actor in this room.", "$.args.actor");
        // The founder's rule: everything in line of sight, so the radius defaults to its 20 m maximum (P4).
        var radius = args.TryGetProperty("radius_m", out var r) ? (float)CanonicalJson.ReadNumber(r) : 20.0f;
        var eye = body.EyeCamera.GlobalPosition;
        var entities = Entities();
        // A companion's own sight was taken (and remembered) for this request; the player looking through either
        // avatar's eyes never touches a companion's memory.
        var own = actor == (principal == CompanionPrincipal ? CompanionAvatarId : PlayerAvatar);
        var sight = own ? Perception(principal, entities) : Perceive(body, actor, entities);
        var visible = new List<(float Distance, JsonObject Entity)>();
        foreach (var entity in entities)
        {
            var id = entity["id"]!.GetValue<string>();
            // The shell is always in sight and listed by room.describe and entities.list; here it would only use up
            // the 100 places (P5).
            if (id == actor || entity["kind"]!.GetValue<string>() == "shell" || !sight.Contains(id)) continue;
            var distance = Distance(entity, eye);
            if (distance <= radius) visible.Add((distance, entity));
        }
        var seen = visible.OrderBy(v => v.Distance).ThenBy(v => v.Entity["id"]!.GetValue<string>(), StringComparer.Ordinal).Take(100).Select(v => v.Entity).ToList();
        // Every name seen in the world is untrusted text, never instructions. Signs are read only while in sight.
        var texts = seen.Take(50).Select(e => (JsonNode?)new JsonObject
        {
            ["source"] = e["id"]!.GetValue<string>(), ["text"] = e["display_name"]!.GetValue<string>(), ["untrusted"] = true,
        }).ToArray();
        if (principal == CompanionPrincipal) foreach (var entity in seen) entity["seen"] = "now";
        var data = new JsonObject
        {
            ["actor"] = actor,
            ["visible"] = new JsonArray(seen.Select(e => (JsonNode?)e).ToArray()),
            ["texts"] = new JsonArray(texts),
        };
        // What the companion remembers out of sight, within the radius of where each was last seen, nearest first.
        var remembered = RememberedOutOfSight(principal)
            .Select(r => (Distance: Distance(r.Value.Summary, eye), Id: r.Key, Entry: r.Value))
            .Where(r => r.Distance <= radius)
            .OrderBy(r => r.Distance).ThenBy(r => r.Id, StringComparer.Ordinal).Take(50).ToList();
        if (remembered.Count > 0) data["remembered"] = new JsonArray(remembered.Select(r => (JsonNode?)RememberedSummary(r.Entry)).ToArray());
        return data;
    }

    /// <summary>
    /// A receipt by action id, durable first, then compacted, then transient: a stop under a reused id never hides
    /// the durable receipt the contract says to look up before retrying (P2).
    /// </summary>
    private JsonObject Lookup(JsonElement args, string principal)
    {
        var actionId = Str(args, "action_id")!;
        var record = Authority.Call("receipt_for", principal, actionId).AsGodotDictionary();
        if (record.Count > 0) return new JsonObject { ["found"] = true, ["receipt"] = Durable(record, replayed: false) };
        if (Authority.Call("compacted_receipt", principal, actionId).AsGodotDictionary().Count > 0) return new JsonObject { ["found"] = true, ["compacted"] = true };
        if (_transient.TryGetValue(principal + "|" + actionId, out var transient))
            return new JsonObject { ["found"] = true, ["receipt"] = transient.Result.DeepClone() };
        return new JsonObject { ["found"] = false };
    }

    /// <summary>A goal's job, for the principal that set it; anyone else's job id, like an unknown one, is target_not_found.</summary>
    private JsonObject JobStatus(JsonElement args, string principal)
    {
        var jobId = Str(args, "job_id")!;
        var job = _jobs.TryGetValue(principal, out var mine) ? mine.FirstOrDefault(j => j.Id == jobId) : null;
        if (job == null) throw new Refusal("target_not_found", "No job with that id is running.", "$.args.job_id");
        var data = new JsonObject { ["job_id"] = job.Id, ["state"] = job.State };
        if (job.Result != null) data["result"] = job.Result.DeepClone();
        return data;
    }

    private JsonObject Status(JsonElement args, string principal)
    {
        var requestId = Str(args, "request_id")!;
        if (!_approvals.TryGetValue(requestId, out var approval) || (approval.Principal != principal && principal != PlayerPrincipal))
            throw new Refusal("target_not_found", "There is no request with that id.", "$.args.request_id");
        Refresh(approval);
        var data = new JsonObject { ["request_id"] = requestId, ["state"] = approval.State };
        if (approval.Result != null) data["result"] = approval.Result.DeepClone();
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
        // Objects stand where play last put them (the authority's poses), ride over their holder while carried, and
        // otherwise stand where the manifest puts them.
        var poses = snapshot.ContainsKey("object_poses") ? snapshot["object_poses"].AsGodotDictionary() : new Godot.Collections.Dictionary();
        foreach (var item in Room.Objects)
        {
            var (position, rotation) = PoseOf(item, poses);
            var box = Sandbox.SandboxPhysics.Bounds(position, rotation, item.Asset.DimensionsM * item.Scale);
            var holder = _held.FirstOrDefault(h => h.Value.Target == item.Id).Key;
            list.Add(Summary(item.Id, "object", item.DisplayName ?? item.Asset.DisplayName, item.Asset.Category, item.Asset.CategoryGroup, position, box,
                item.Asset.Affordances, item.Asset.Movable, locks.ContainsKey(item.Id), item.Asset.ProvenanceKind, EntityRevision(item.Id), holder));
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
        IEnumerable<string> affordances, bool movable, bool locked, string provenance, int revision, string? heldBy = null)
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
        if (heldBy != null) summary["held_by"] = heldBy;
        return summary;
    }

    // ---- perception (docs/companion/PERCEPTION.md) ----

    /// <summary>Every entity for the player; for the companion, only what its avatar can see now, each marked seen "now".</summary>
    private List<JsonObject> Visible(string principal)
    {
        var entities = Entities();
        if (principal == PlayerPrincipal || _approving) return entities;
        var sight = Perception(principal, entities);
        var visible = entities.Where(e => sight.Contains(e["id"]!.GetValue<string>())).ToList();
        foreach (var entity in visible) entity["seen"] = "now";
        return visible;
    }

    private HashSet<string> Perception(string principal, List<JsonObject>? entities = null)
    {
        if (_perceived != null) return _perceived;
        var body = principal == CompanionPrincipal ? Companion : Player;
        _perceived = Perceive(body, principal == CompanionPrincipal ? CompanionAvatarId : PlayerAvatar, entities ?? Entities());
        return _perceived;
    }

    /// <summary>Whether principal may name this entity: the player always; the companion only what it can see now.</summary>
    private bool Perceives(string principal, string id) => principal == PlayerPrincipal || _approving || Perception(principal).Contains(id);

    /// <summary>
    /// Line of sight from a body's eye (PERCEPTION.md, with physics ray casts against the real colliders in place of
    /// the mock's boxes). Each entity is sampled at 15 points of its bounds (the centre, eight corners and six face
    /// centres, each pulled 1 cm or a quarter of the box inside); a sample is seen when a ray from the eye reaches it
    /// or the entity itself first. Rays test only the world layer, so avatars and effects never occlude. The body's
    /// own avatar and the room shell are always perceived.
    /// </summary>
    public HashSet<string> Perceive(SmallPlayerController? body, string ownAvatar, List<JsonObject> entities)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (body == null || !IsInstanceValid(body) || !body.IsInsideTree()) return seen;
        foreach (var entity in entities)
        {
            var id = entity["id"]!.GetValue<string>();
            // Its own avatar, the shell it stands in and whatever it holds are always perceived.
            if (id == ownAvatar || entity["kind"]!.GetValue<string>() == "shell" || entity["held_by"]?.GetValue<string>() == ownAvatar || SeesBox(body, BoundsOf(entity)))
                seen.Add(id);
        }
        return seen;
    }

    /// <summary>
    /// Whether a ray from the body's eye reaches one of the box's 15 samples, or hits inside the box first: an
    /// entity there is in sight, and an empty place there is seen to be empty.
    /// </summary>
    private static bool SeesBox(SmallPlayerController body, Aabb bounds)
    {
        var eye = body.EyeCamera.GlobalPosition;
        var space = body.GetWorld3D().DirectSpaceState;
        var exclude = new Godot.Collections.Array<Rid> { body.GetRid() };
        var box = bounds.Grow(0.002f);
        foreach (var sample in Samples(bounds.Position, bounds.End))
        {
            var ray = PhysicsRayQueryParameters3D.Create(eye, sample, RoomBuilder.WorldLayer, exclude);
            var hit = space.IntersectRay(ray);
            if (hit.Count == 0 || box.HasPoint(hit["position"].AsVector3())) return true;
        }
        return false;
    }

    private static IEnumerable<Vector3> Samples(Vector3 low, Vector3 high)
    {
        var centre = (low + high) * 0.5f;
        var size = high - low;
        var reach = new Vector3(Mathf.Max(0, size.X * 0.5f - Mathf.Min(0.01f, size.X * 0.25f)),
            Mathf.Max(0, size.Y * 0.5f - Mathf.Min(0.01f, size.Y * 0.25f)), Mathf.Max(0, size.Z * 0.5f - Mathf.Min(0.01f, size.Z * 0.25f)));
        yield return centre;
        foreach (var x in new[] { -1f, 1f })
        foreach (var y in new[] { -1f, 1f })
        foreach (var z in new[] { -1f, 1f })
            yield return centre + reach * new Vector3(x, y, z);
        foreach (var axis in new[] { Vector3.Right, Vector3.Up, Vector3.Back })
        {
            yield return centre + reach * axis;
            yield return centre - reach * axis;
        }
    }

    /// <summary>
    /// A companion command may name only what the companion can see now (the player's avatar included, P3): anything
    /// else is target_not_found, as if it did not exist. The one exception is the target of a goal that only moves or
    /// turns the companion, which may be something it remembers; the host re-checks it on arrival.
    /// </summary>
    private void CheckPerceived(string op, JsonElement args, string principal)
    {
        if (principal == PlayerPrincipal || _approving) return;
        var goal = op == "goal.set" ? Str(args, "goal") : null;
        foreach (var (id, path) in Named(op, args))
        {
            if (Perceives(principal, id)) continue;
            if (path == "$.args.target" && goal != null && RememberedTargetGoals.Contains(goal) && Recall(principal, id) != null) continue;
            throw new Refusal("target_not_found", "That is not in this room.", path);
        }
    }

    // ---- perception memory (docs/companion/PERCEPTION.md; the mock's test_perception_memory.py) ----

    /// <summary>One entity as a companion last saw it. Only the summary as seen and the timing ever reach the companion.</summary>
    private sealed class Remembered
    {
        public required JsonObject Summary { get; init; }
        public string ProtectedBy { get; init; } = "";
        public DateTime SeenAt { get; init; }
        public int SeenRevision { get; init; }
        /// <summary>It changed in some way since (sticky until seen again); emitted only as may_be_stale.</summary>
        public bool Changed { get; set; }
    }

    /// <summary>What one companion has seen in this room, least recently seen first.</summary>
    private sealed class PerceptionMemory
    {
        public string RoomKey { get; init; } = "";
        public List<string> Order { get; } = new();
        public Dictionary<string, Remembered> Entries { get; } = new(StringComparer.Ordinal);

        public void Remove(string id)
        {
            if (Entries.Remove(id)) Order.Remove(id);
        }

        public void Add(string id, Remembered entry)
        {
            Remove(id);
            Entries[id] = entry;
            Order.Add(id);
        }

        public void Clear()
        {
            Entries.Clear();
            Order.Clear();
        }
    }

    private string RoomKey => Room.RoomId + "@" + Room.ManifestSha256;

    private PerceptionMemory MemoryOf(string principal)
    {
        if (!_memory.TryGetValue(principal, out var memory) || memory.RoomKey != RoomKey)
            _memory[principal] = memory = new PerceptionMemory { RoomKey = RoomKey };
        return memory;
    }

    /// <summary>
    /// A companion looks: its avatar's sight now is taken for this request and fills its memory. Only this fills memory,
    /// and only from that avatar's own line of sight, never through the player's.
    /// - What it remembers but cannot see is checked against the room: any change at all (moved, edited, locked or gone)
    ///   marks it changed, which the companion learns only as may_be_stale.
    /// - If the place it was last seen is in sight now and it is not there, the companion has looked again: the memory
    ///   is dropped. Until then, a thing removed out of sight is remembered as it was.
    /// - What it sees now is remembered afresh, nearest last, and the least recently seen are forgotten past the bound.
    /// </summary>
    private void Look(string principal)
    {
        _perceived = null;
        var entities = Entities();
        var sight = Perception(principal, entities);
        if (principal == PlayerPrincipal) return;
        var memory = MemoryOf(principal);
        if (PerceptionMemoryLimit <= 0)
        {
            memory.Clear();
            return;
        }
        var body = Companion;
        if (body == null || !IsInstanceValid(body) || !body.IsInsideTree()) return;
        var byId = entities.ToDictionary(e => e["id"]!.GetValue<string>(), StringComparer.Ordinal);
        var owners = LockOwners();
        foreach (var id in memory.Order.ToList())
        {
            if (sight.Contains(id)) continue;
            var entry = memory.Entries[id];
            if (!entry.Changed && (!byId.TryGetValue(id, out var now) || !SameAsSeen(entry, now, owners.GetValueOrDefault(id, ""))))
                entry.Changed = true;
            if (SeesBox(body, BoundsOf(entry.Summary))) memory.Remove(id);
        }
        var eye = body.EyeCamera.GlobalPosition;
        var fresh = entities
            .Where(e => sight.Contains(e["id"]!.GetValue<string>()) && e["id"]!.GetValue<string>() != CompanionAvatarId && RememberedKinds.Contains(e["kind"]!.GetValue<string>()))
            .OrderByDescending(e => Distance(e, eye)).ThenBy(e => e["id"]!.GetValue<string>(), StringComparer.Ordinal);
        foreach (var entity in fresh)
        {
            var id = entity["id"]!.GetValue<string>();
            memory.Add(id, new Remembered
            {
                Summary = (JsonObject)entity.DeepClone(), ProtectedBy = owners.GetValueOrDefault(id, ""), SeenAt = Clock(), SeenRevision = Revision,
            });
        }
        while (memory.Order.Count > PerceptionMemoryLimit) memory.Remove(memory.Order[0]);
    }

    /// <summary>
    /// Whether an entity is as the companion saw it: every field the same, and its place within 5 mm (a body at rest on
    /// the floor settles by a fraction of a millimetre, which is no change).
    /// </summary>
    private static bool SameAsSeen(Remembered entry, JsonObject now, string protectedBy)
    {
        if (entry.ProtectedBy != protectedBy) return false;
        static JsonObject Unplaced(JsonObject summary)
        {
            var copy = (JsonObject)summary.DeepClone();
            copy.Remove("position_m");
            copy.Remove("bounds_m");
            copy.Remove("seen");
            return copy;
        }
        if (CanonicalJson.Text(Unplaced(entry.Summary)) != CanonicalJson.Text(Unplaced(now))) return false;
        var was = BoundsOf(entry.Summary);
        var box = BoundsOf(now);
        return ReadNodeVector(entry.Summary["position_m"]!).DistanceTo(ReadNodeVector(now["position_m"]!)) <= 0.005f &&
            was.Position.DistanceTo(box.Position) <= 0.005f && was.End.DistanceTo(box.End) <= 0.005f;
    }

    /// <summary>The companion's memory of an entity it cannot see now; null for the player, for anything in sight and for anything it never saw (or has seen is gone).</summary>
    private Remembered? Recall(string principal, string id)
    {
        if (principal == PlayerPrincipal || _approving || Perceives(principal, id)) return null;
        return _memory.TryGetValue(principal, out var memory) && memory.RoomKey == RoomKey && memory.Entries.TryGetValue(id, out var entry) ? entry : null;
    }

    /// <summary>Everything the companion remembers that is out of its sight now, by id.</summary>
    private IEnumerable<KeyValuePair<string, Remembered>> RememberedOutOfSight(string principal)
    {
        if (principal == PlayerPrincipal || _approving || !_memory.TryGetValue(principal, out var memory) || memory.RoomKey != RoomKey) return Array.Empty<KeyValuePair<string, Remembered>>();
        var sight = Perception(principal);
        return memory.Entries.Where(e => !sight.Contains(e.Key)).ToList();
    }

    /// <summary>A remembered entity as the companion saw it, marked: the flag is one bit (old, or changed in any way) and never says which.</summary>
    private JsonObject RememberedSummary(Remembered entry)
    {
        var summary = (JsonObject)entry.Summary.DeepClone();
        summary["seen"] = "remembered";
        summary["last_seen_ago_s"] = AgeSeconds(entry);
        summary["last_seen_revision"] = entry.SeenRevision;
        summary["may_be_stale"] = MayBeStale(entry);
        return summary;
    }

    private double AgeSeconds(Remembered entry) => Math.Round(Math.Max(0, (Clock() - entry.SeenAt).TotalSeconds), 1, MidpointRounding.AwayFromZero);

    private bool MayBeStale(Remembered entry) => entry.Changed || Clock() - entry.SeenAt >= PerceptionMemoryStaleAfter;

    /// <summary>
    /// The session hook the Run 2 bridge calls when a companion's link session starts or ends ("start", "end", the mock's
    /// session_event): perception memory never outlives a session.
    /// </summary>
    public void SessionEvent(string principal, string sessionEvent)
    {
        if (sessionEvent is "start" or "end") ClearPerceptionMemory(principal);
    }

    /// <summary>Forget everything this principal's avatar has seen.</summary>
    public void ClearPerceptionMemory(string principal) => _memory.Remove(principal);

    /// <summary>Test seam: the ids a companion remembers, least recently seen first.</summary>
    internal IReadOnlyList<string> RememberedIds(string principal) =>
        _memory.TryGetValue(principal, out var memory) ? memory.Order.ToList() : Array.Empty<string>();

    /// <summary>Who protected each locked entity.</summary>
    private Dictionary<string, string> LockOwners()
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var snapshot = Authority.Call("snapshot", PlayerPrincipal).AsGodotDictionary();
        if (snapshot.ContainsKey("locks"))
            foreach (var (key, value) in snapshot["locks"].AsGodotDictionary())
                owners[key.AsString()] = value.AsGodotDictionary()["locked_by"].AsString();
        return owners;
    }

    private static Aabb BoundsOf(JsonObject summary)
    {
        var low = ReadNodeVector(summary["bounds_m"]!["min_m"]!);
        var high = ReadNodeVector(summary["bounds_m"]!["max_m"]!);
        return new Aabb(low, high - low);
    }

    // ---- goal jobs (the goal runner's side; P7) ----

    /// <summary>A goal with a target, running until the avatar arrives (then re-checked), a new goal or a stop.</summary>
    private sealed class GoalJob
    {
        public required string Id { get; init; }
        public required string Principal { get; init; }
        public required string Actor { get; init; }
        public required string ActionId { get; init; }
        public required string Goal { get; init; }
        public required string Target { get; init; }
        /// <summary>Where the goal aimed: the target's bounds as the requester saw (or remembered) them.</summary>
        public Aabb Aim { get; init; }
        /// <summary>The body's IntentSerial for this goal; a different serial means a newer goal replaced it.</summary>
        public int Serial { get; init; }
        public string State { get; set; } = "running";
        public JsonObject? Result { get; set; }
    }

    private GoalJob StartJob(string principal, string actor, string actionId, string goal, string target, Aabb aim, int serial)
    {
        var job = new GoalJob
        {
            // 'job-' and 26 lowercase base32 characters (130 random bits): the contract's pattern, which no counter can match.
            Id = "job-" + System.Security.Cryptography.RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz234567", 26), Principal = principal, Actor = actor, ActionId = actionId, Goal = goal, Target = target, Aim = aim, Serial = serial,
        };
        if (!_jobs.TryGetValue(principal, out var mine)) _jobs[principal] = mine = new List<GoalJob>();
        mine.Add(job);
        // At most JobLimit per principal: the oldest finished go first (a running job is never dropped).
        while (mine.Count > JobLimit && mine.FirstOrDefault(j => j.State != "running") is { } finished) mine.Remove(finished);
        _runningGoals[actor] = job;
        return job;
    }

    /// <summary>A new goal or a stop cancels the actor's running job.</summary>
    private void CancelGoal(string actor)
    {
        if (!_runningGoals.Remove(actor, out var job)) return;
        job.State = "cancelled";
        GoalFinished?.Invoke(actor, job.Id, job.State);
    }

    /// <summary>Test seam: the jobs kept for a principal.</summary>
    internal int JobCount(string principal) => _jobs.TryGetValue(principal, out var mine) ? mine.Count : 0;

    /// <summary>
    /// The A2 goal runner's seam: the actor's running goal with a target (job id, goal, target and where it aimed),
    /// or null. The host itself watches follow, come, look_at and point_at on the companion's body.
    /// </summary>
    public (string JobId, string Goal, string Target, Aabb Aim)? RunningGoal(string actor) =>
        _runningGoals.TryGetValue(actor, out var job) ? (job.Id, job.Goal, job.Target, job.Aim) : null;

    /// <summary>
    /// The avatar has reached its goal (walked there, or turned to look or point). The host re-checks the target from
    /// where the avatar is now, with that avatar's own line of sight (arriving is looking: the companion's memory is
    /// refreshed or dropped), and finishes the job honestly. Returns the job's state, or null with no goal running.
    /// - succeeded: the target is in sight, within ArrivalReachM of where the goal aimed (a followed or approached
    ///   player is wherever they are now);
    /// - failed, revision_conflict: it is in sight, but has moved since (the companion sees where);
    /// - failed, target_not_found: it is not in sight. Moved out of sight and gone give the same answer.
    /// The A2 runner calls this for go_to and fetch; the host calls it for look_at, point_at and come.
    /// </summary>
    public string? ReportArrival(string actor)
    {
        if (!_runningGoals.Remove(actor, out var job)) return null;
        var principal = actor == CompanionAvatarId ? CompanionPrincipal : PlayerPrincipal;
        HashSet<string> sight;
        _perceived = null;
        try
        {
            if (principal == CompanionPrincipal) Look(principal);
            sight = Perception(principal);
        }
        finally { _perceived = null; }
        var target = Entities().FirstOrDefault(e => e["id"]!.GetValue<string>() == job.Target);
        Refusal? error = null;
        if (target == null || !sight.Contains(job.Target))
            error = new Refusal("target_not_found", "The target is not where it was seen. Observe and try again.", "$.args.target");
        else if (job.Goal is not ("follow" or "come") && Gap(BoundsOf(target), job.Aim) > ArrivalReachM)
            error = new Refusal("revision_conflict", "The target has moved since it was seen. Observe and try again.", "$.args.target", retryable: true);
        job.State = error == null ? "succeeded" : "failed";
        if (error != null) job.Result = Fail("goal.set", job.Principal, job.ActionId, null, error);
        GoalFinished?.Invoke(actor, job.Id, job.State);
        return job.State;
    }

    /// <summary>The gap between two boxes (0 when they touch or overlap).</summary>
    private static float Gap(Aabb a, Aabb b)
    {
        var gap = new Vector3(
            Mathf.Max(0, Mathf.Max(a.Position.X - b.End.X, b.Position.X - a.End.X)),
            Mathf.Max(0, Mathf.Max(a.Position.Y - b.End.Y, b.Position.Y - a.End.Y)),
            Mathf.Max(0, Mathf.Max(a.Position.Z - b.End.Z, b.Position.Z - a.End.Z)));
        return gap.Length();
    }

    /// <summary>
    /// Watches the companion's body for the goals the host drives itself: a come arrives when the body reaches the player,
    /// a look_at or point_at when it faces the target, and a follow runs until replaced. A goal the body dropped for a
    /// newer one (its IntentSerial moved on) is cancelled.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        if (Companion == null || !IsInstanceValid(Companion) || !_runningGoals.TryGetValue(CompanionAvatarId, out var job) || !HostDrivenGoals.Contains(job.Goal)) return;
        if ((job.Goal == "come" && Companion.ComeArrivedSerial == job.Serial) || (job.Goal == "go_to" && Companion.GoToArrivedSerial == job.Serial))
        {
            ReportArrival(CompanionAvatarId);
            return;
        }
        if (Companion.IntentSerial != job.Serial)
        {
            CancelGoal(CompanionAvatarId);
            return;
        }
        if (job.Goal is "look_at" or "point_at" && Companion.FacesLookTarget) ReportArrival(CompanionAvatarId);
    }

    private static IEnumerable<(string Id, string Path)> Named(string op, JsonElement args)
    {
        if (args.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String) yield return (target.GetString()!, "$.args.target");
        if (args.TryGetProperty("targets", out var targets) && targets.ValueKind == JsonValueKind.Array)
            foreach (var item in targets.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) yield return (item.GetString()!, "$.args.targets");
        if (args.TryGetProperty("placement", out var placement) && placement.ValueKind == JsonValueKind.Object && placement.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.String)
            yield return (on.GetString()!, "$.args.placement.on");
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

    private sealed class Refusal(string code, string message, string? path = null, bool retryable = false, JsonNode? actual = null, JsonNode? allowed = null) : Exception(message)
    {
        public string Code { get; } = code;
        public string? Path { get; } = path;
        public bool Retryable { get; } = retryable;
        public JsonNode? Actual { get; } = actual;
        public JsonNode? Allowed { get; } = allowed;
    }

    private void CheckEnvelope(JsonElement root, bool command, bool stop)
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
        if (root.TryGetProperty("note", out var note) && (note.ValueKind != JsonValueKind.String || KernelText.CodePoints(note.GetString()!).Length > 280 || KernelText.HasHidden(note.GetString()!)))
            throw new Refusal("request_invalid", "A note is one line of at most 280 visible characters.", "$.note");
        // A stop applies whatever expectations come with it, malformed ones included: they are ignored.
        if (stop) return;
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

    /// <summary>
    /// Refuses a hidden character (KernelText: controls, format characters, blanks, variation selectors, plane 14,
    /// emoji markers out of place) in any string or key of a request, wherever it is. A stop's expectations are
    /// ignored, so they are not checked either.
    /// </summary>
    private static void CheckText(JsonElement element, string path, bool stop, int depth = 0)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (stop && depth == 0 && property.Name is "expected_revision" or "expected_entities") continue;
                    var inner = path + "." + (PathSegment.IsMatch(property.Name) ? property.Name : "?");
                    if (KernelText.HasHidden(property.Name))
                        throw new Refusal("request_invalid", "Names in a request may not contain control or invisible characters.", inner);
                    CheckText(property.Value, inner, stop, depth + 1);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray()) CheckText(item, $"{path}[{index++}]", stop, depth + 1);
                break;
            case JsonValueKind.String:
                if (KernelText.HasHidden(element.GetString()!))
                    throw new Refusal("request_invalid", "Text in a request may not contain control or invisible characters.", path);
                break;
        }
    }

    private void CheckExpectations(JsonElement root, string op, JsonElement args, string principal)
    {
        if (root.TryGetProperty("expected_revision", out var expected) && expected.GetInt64() != Revision)
            throw new Refusal("revision_conflict", "The room changed. Look again before retrying.", "$.expected_revision", true, JsonValue.Create(Revision));
        if (root.TryGetProperty("expected_entities", out var entities))
        {
            foreach (var entry in entities.EnumerateObject())
            {
                // Out of the companion's sight is the same as not in the room.
                var current = Perceives(principal, entry.Name) ? EntityRevision(entry.Name) : -1;
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
            "entity.push" => new[] { "target", "actor", "toward_m", "distance_m" },
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
            "world.set_physics" => new[] { "preset" },
            _ => Array.Empty<string>(),
        };
        foreach (var property in args.EnumerateObject())
            if (!allowed.Contains(property.Name))
                throw new Refusal("field_unknown", "This argument is not part of the operation.", "$.args." + KernelJson.DisplayText(property.Name, 64));
        string[] required = op switch
        {
            "entity.grab" or "entity.remove" or "creation.activate" or "creation.revise" => new[] { "target" },
            "entity.place" => new[] { "target", "placement" },
            "entity.push" => new[] { "target", "distance_m" },
            "entity.set_part" => new[] { "target", "part_id", "value" },
            "entity.transform" => new[] { "target", "into" },
            "creation.place" => new[] { "source", "placement" },
            "protect.lock" or "protect.unlock" => new[] { "targets" },
            "goal.set" => new[] { "actor", "goal" },
            "effect.start" => new[] { "capability", "params", "area", "duration_s" },
            "effect.stop" => new[] { "effect" },
            "style.set" => new[] { "preset_id", "preset_version" },
            "room.undo" => new[] { "to_revision" },
            "world.set_physics" => new[] { "preset" },
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
        if (args.TryGetProperty("toward_m", out var toward)) CheckVector(toward, "$.args.toward_m");
        if (op == "entity.push")
        {
            var distance = args.GetProperty("distance_m");
            if (distance.ValueKind != JsonValueKind.Number || distance.GetDouble() is <= 0 or > 1)
                throw new Refusal("request_invalid", "distance_m is more than 0 and at most 1 metre.", "$.args.distance_m");
        }
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
        if (op == "world.set_physics" && (args.GetProperty("preset").ValueKind != JsonValueKind.String || !Token.IsMatch(args.GetProperty("preset").GetString()!)))
            throw new Refusal("request_invalid", "preset is a preset id, a lowercase token.", "$.args.preset");
        if (op == "room.checkpoint" && args.TryGetProperty("label", out var label) &&
            (label.ValueKind != JsonValueKind.String || KernelText.CodePoints(label.GetString()!).Length > 80))
            throw new Refusal("request_invalid", "A checkpoint label is one line of at most 80 characters.", "$.args.label");
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
                if (!args.TryGetProperty("job_id", out var job) || job.ValueKind != JsonValueKind.String || !JobIdPattern.IsMatch(job.GetString()!))
                    throw new Refusal("request_invalid", "job_id is the opaque id the host returned.", "$.args.job_id");
                break;
            case "capabilities.list":
                if (args.TryGetProperty("category", out var category) && (category.ValueKind != JsonValueKind.String || !Token.IsMatch(category.GetString()!)))
                    throw new Refusal("request_invalid", "category is a token.", "$.args.category");
                if (args.TryGetProperty("limit", out var capabilityLimit) && (!IsIntegerLiteral(capabilityLimit) || capabilityLimit.GetDouble() is < 1 or > 100))
                    throw new Refusal("request_invalid", "limit is between 1 and 100.", "$.args.limit");
                if (args.TryGetProperty("cursor", out var capabilityCursor) && (capabilityCursor.ValueKind != JsonValueKind.String || capabilityCursor.GetString()!.Length > 128))
                    throw new Refusal("request_invalid", "cursor is the string a previous page returned.", "$.args.cursor");
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
        "entity.set_part" => "Moving parts of objects (a lid, a door) is not available yet.",
        "entity.transform" => "Transforming objects arrives with the first magic (Run 3).",
        "effect.start" => "Free-standing effects arrive with the first magic (Run 3).",
        "style.set" => "Restyling the room from a command arrives with the look runtime.",
        "room.undo" => "Undo arrives with room saves (Run 3).",
        "goal.set" when Str(args, "goal") is "fetch" => "Fetching arrives with the sandbox verbs (Run 2, P3).",
        "goal.set" when Str(args, "goal") is "wander" => "Wandering is not available yet.",
        // Today the body follows and comes to the player only; staying by something waits for the goal runner too.
        "goal.set" when Str(args, "goal") is "follow" or "stay" or "come" && Str(args, "target") is { } target && (target != PlayerAvatar || Str(args, "goal") == "stay") =>
            "Following, coming to or staying by anything but the player arrives with the companion's embodiment (Run 2).",
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

    // ---- the companion's rate limit: 30 messages a second, stops exempt ----

    private bool TokensLeft()
    {
        var now = Clock();
        while (_companionCalls.Count > 0 && now - _companionCalls.Peek() > TimeSpan.FromSeconds(1)) _companionCalls.Dequeue();
        return _companionCalls.Count < CompanionMessagesPerSecond;
    }

    private bool TakeToken()
    {
        if (!TokensLeft()) return false;
        _companionCalls.Enqueue(Clock());
        return true;
    }

    /// <summary>
    /// Whether text could be a goal.stop or effect.stop. A JSON string can spell a letter or '.' only literally or
    /// with a \u escape, so text with neither op name and no \u escape is never a stop and needs no parsing.
    /// </summary>
    private static bool MightBeStop(string message) =>
        message.Contains("goal.stop", StringComparison.Ordinal) || message.Contains("effect.stop", StringComparison.Ordinal) || message.Contains("\\u", StringComparison.Ordinal);

    private JsonObject RateLimited(string principal) =>
        Fail("invalid", principal, null, null, new Refusal("rate_limited", "Too many messages; wait a moment.", retryable: true));

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
        if (refusal.Allowed != null) error["allowed"] = refusal.Allowed.DeepClone();
        if (refusal.Actual != null) error["actual"] = refusal.Actual.DeepClone();
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
