using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Room;

namespace EnFractal.Tests;

/// <summary>
/// The Bubble (M9 of docs/runs/RUN-2-MECHANICS.md; Kernel/Bubble, SmallPlayerController.Bubble.cs) in the real test room with the
/// real bodies and host, under storybook_wild v5's prices, with a 3 cm sheet of shallow water laid on the floor west of the player:
/// the consent (the ghost preview, its lead, the step-in), every refusal, the price by principal, the pops and the receipts
/// (BubbleTest.cs); the ride, the deflate, the lift, the Gubble's steering, the step-in and the boat (BubbleTest.Body.cs); and the
/// security notes' row (docs/companion/SECURITY.md, "the companion casting on, or moving, the player's avatar"), its tests named
/// for it. Pass "-- --dump=DIR" to write every message and result for contracts/validate.py, and "-- --verbose" to print every check.
/// </summary>
public partial class BubbleTest : Node3D
{
    private const string TestRoot = "user://tests/bubble";
    private const string Player = CommandHost.PlayerPrincipal;
    private const string Companion = CommandHost.CompanionPrincipal;
    private const double Tick = 1.0 / 60.0;
    /// <summary>The shallow water's surface (3 cm over the floor) and its extent, west of the player's spawn.</summary>
    private const float WaterY = 0.03f;
    private static readonly Vector2 WaterMin = new(-1.3f, 0.1f), WaterMax = new(-0.35f, 1.3f);
    /// <summary>A deep pool by the rug: its surface 15 cm over the floor, its bed (deeper than the body swims in, 8 cm).</summary>
    private const float PoolY = 0.15f;
    private static readonly Vector2 PoolMin = new(0.5f, 0.9f), PoolMax = new(1.1f, 1.4f);
    private int _checks;
    private int _failures;
    private int _ids;
    private int _dumped;
    private string? _dump;
    /// <summary>"-- --verbose" prints every check, passed or not.</summary>
    private bool _verbose;
    private ScriptErrors? _errors;
    private RoomData _room = null!;
    private Node3D? _world;
    private CommandHost _host = null!;
    private SmallPlayerController _player = null!;
    private CompanionAvatar _companion = null!;
    private DateTime _now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private readonly List<string> _learned = new();

    public override async void _Ready()
    {
        try
        {
            _errors = new ScriptErrors();
            OS.AddLogger(_errors);
            _dump = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--dump=", StringComparison.Ordinal))?["--dump=".Length..];
            if (_dump != null) System.IO.Directory.CreateDirectory(_dump);
            _verbose = OS.GetCmdlineUserArgs().Contains("--verbose");
            RemoveAll();
            _room = RoomData.Load(RoomWorld.DefaultRoom);
            await TestConsent();
            await TestRefusals();
            await TestPriceByPrincipal();
            await TestPopsAndReceipts();
            await TestRideAndDeflate();
            await TestLiftAndSteering();
            await TestStepIn();
            await TestDeflateIntoWater();
            await TestBoat();
            await TestSecurityRow();
            await TestBudget();
            await FreeWorld();
            OS.RemoveLogger(_errors);
            Check(_errors.Count == 0, $"no script or engine error while the suite ran ({_errors.Count}; first: {_errors.First})");
            GD.Print($"NATIVE_KERNEL_BUBBLE: {_checks - _failures}/{_checks} checks passed; the Bubble's carry, boat and lift: the ghost preview and its lead, the step-in, " +
                "every refusal, the price by principal, the pops (the dive key, X, the timer, anything else) and the receipts, the ride, the deflate, the lift, the Gubble's " +
                $"steering, the boat, the security row{(_dump != null ? $"; {_dumped} messages dumped" : "")}");
            RemoveAll();
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception error)
        {
            GD.PushError("Bubble test exception: " + error);
            GetTree().Quit(1);
        }
    }

    // ---- the world ----

    private sealed class Store(PlayerModesProfile profile) : IPlayerVitalsStore
    {
        private PlayerModesProfile _saved = profile;
        public PlayerModesProfile Load() => _saved;
        public void Save(PlayerModesProfile next) => _saved = next;
    }

    /// <summary>
    /// A fresh test room under storybook_wild v5 in a mode (Creative's shadow pool unless a check is about the price or harm) with a
    /// pool of pips (at most the pack's maximum), its avatars at their spawns (the Gubble staying put),
    /// shallow water on the floor, and a host whose clock and bubble tick the suite drives (Frames).
    /// </summary>
    private async Task NewWorld(string name, string mode = "creative", int pips = 40, int rulesVersion = 5)
    {
        await FreeWorld();
        _world = new Node3D { Name = "World" };
        AddChild(_world);
        _world.AddChild(RoomBuilder.Build(_room));
        _world.AddChild(Water(WaterMin, WaterMax, WaterY));
        _world.AddChild(Water(PoolMin, PoolMax, PoolY));
        await Frames(2);
        _player = new SmallPlayerController { Name = "Player", ReadKeyboard = false, Position = _room.SpawnFor("player").PositionM + Vector3.Up * 0.008f };
        _world.AddChild(_player);
        _companion = new CompanionAvatar { Name = "Companion", Position = _room.SpawnFor("companion", _room.SpawnFor("player")).PositionM + Vector3.Up * 0.008f };
        _world.AddChild(_companion);
        _companion.BindPlayer(_player);
        _companion.Stay();
        _host = CommandHost.Create(_world, _room, _player, _companion, null, $"{TestRoot}/{name}/inventions.json");
        _host.Clock = () => _now;
        _host.TeamSightIntervalS = 0;
        _host.SetProcess(false);
        _host.SetPhysicsProcess(false);
        _host.LoadRules("storybook_wild", rulesVersion);
        _host.UseDifficulty("standard");
        var rules = VitalsRules.For(_host.Rules, mode, "standard");
        var vitals = rules.Fresh().With(pips: Math.Min(pips, rules.MaxPips));
        var modes = PlayerModesProfile.Modes.ToDictionary(m => m, m => m == mode ? vitals : PlayerVitals.Fresh);
        _host.ConfigurePlayerVitals(new Store(PlayerModesProfile.Restore(mode, modes, null)));
        _learned.Clear();
        _host.BubbleLearnedForTests = _learned.Add;
        await Frames(6);
    }

    /// <summary>A sheet of water over the floor (the room's kind of water collider, RoomWater): 3 cm is shallow, where a boat is cast; 15 cm deep.</summary>
    private static Area3D Water(Vector2 min, Vector2 max, float surface)
    {
        var sheet = new SurfaceTool();
        sheet.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var corner in new[] { min, new Vector2(max.X, min.Y), max, min, max, new Vector2(min.X, max.Y) })
            sheet.AddVertex(new Vector3(corner.X, surface, corner.Y));
        return RoomWater.CreateCollider(new[] { ((Shape3D)sheet.Commit().CreateTrimeshShape(), Transform3D.Identity) });
    }

    private async Task FreeWorld()
    {
        if (_world == null || !IsInstanceValid(_world)) return;
        _world.QueueFree();
        _world = null;
        await Frames(3);
    }

    /// <summary>Physics frames, each a tick of the host's clock and its bubbles before the bodies move (as the game's own tick).</summary>
    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            _now += TimeSpan.FromSeconds(Tick);
            if (_world != null && IsInstanceValid(_host)) _host.AdvanceBubbles(Tick);
        }
    }

    /// <summary>The host's clock moves on without the bodies (a ghost's lead, a pop's respect).</summary>
    private void Wait(double seconds) => _now += TimeSpan.FromSeconds(seconds);

    /// <summary>Puts the player's feet at a spot on the floor, still.</summary>
    private async Task PlayerAt(Vector3 feet)
    {
        _player.SetDiveInput(false, false);
        _player.SetControlInput(Vector2.Zero);
        Check(_player.TryTeleportTo(feet), $"the player stands at {feet}");
        await Frames(4);
    }

    private void GubbleAt(Vector3 at)
    {
        _companion.GlobalPosition = at + Vector3.Up * 0.008f;
        _companion.ResetPhysicsInterpolation();
    }

    // ---- messages ----

    private string NextId(string prefix) => $"{prefix}-{++_ids}";

    private JsonObject Command(string actionId, string op, JsonObject args, bool preview = false)
    {
        var command = new JsonObject { ["schema"] = "enfractal.command", ["version"] = 1, ["action_id"] = actionId, ["room_id"] = _room.RoomId, ["op"] = op, ["args"] = args.Parent == null ? args : args.DeepClone() };
        if (preview) command["preview"] = true;
        return command;
    }

    private JsonObject Query(string op, JsonObject args) =>
        new() { ["schema"] = "enfractal.query", ["version"] = 1, ["query_id"] = NextId("q"), ["room_id"] = _room.RoomId, ["op"] = op, ["args"] = args };

    /// <summary>Sends a message as principal; contractValid false keeps a deliberately malformed message out of the dump.</summary>
    private JsonObject Send(JsonObject message, string principal, bool contractValid = true)
    {
        var result = JsonNode.Parse(_host.Handle(message.ToJsonString(), principal))!.AsObject();
        if (_dump != null)
        {
            var index = ++_dumped;
            if (contractValid) System.IO.File.WriteAllBytes(System.IO.Path.Combine(_dump, $"{index:0000}_message.json"), CanonicalJson.Bytes(message));
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(_dump, $"{index:0000}_result.json"), CanonicalJson.Bytes(result));
        }
        return result;
    }

    private static JsonArray Vec(Vector3 at) => new(Math.Round(at.X, 4), Math.Round(at.Y, 4), Math.Round(at.Z, 4));

    private static JsonObject BubbleArgs(string capability, Vector3 center, string? target = null, double radius = 0.08, double duration = 20, string? previewId = null)
    {
        var args = new JsonObject { ["capability"] = capability, ["center_m"] = Vec(center), ["radius_m"] = radius, ["duration_s"] = duration };
        if (target != null) args["target"] = target;
        if (previewId != null) args["preview_id"] = previewId;
        return args;
    }

    /// <summary>Previews a cast on the player as principal: the preview's result.</summary>
    private JsonObject Preview(string principal, string capability = "bubble_carry", double duration = 20, Vector3? at = null) =>
        Send(Command(NextId("ghost"), "nature.cast", BubbleArgs(capability, at ?? _player.GlobalPosition, CommandHost.PlayerAvatar, duration: duration), preview: true), principal);

    /// <summary>
    /// A cast on the player done right: previewed, the ghost's lead waited out (the Gubble's), then cast naming the preview. Returns the
    /// commit's result; the preview's comes back in preview.
    /// </summary>
    private JsonObject CarryOnPlayer(string principal, out JsonObject preview, string capability = "bubble_carry", double duration = 20)
    {
        var at = _player.GlobalPosition;
        preview = Send(Command(NextId("ghost"), "nature.cast", BubbleArgs(capability, at, CommandHost.PlayerAvatar, duration: duration), preview: true), principal);
        if (principal != Player) Wait(BubbleLaws.GhostLeadS + 0.05);
        var token = preview["data"]?["preview_id"]?.GetValue<string>();
        return Send(Command(NextId("carry"), "nature.cast", BubbleArgs(capability, at, CommandHost.PlayerAvatar, duration: duration, previewId: token)), principal);
    }

    private JsonObject AtPoint(string principal, string capability, Vector3 center, double radius = 0.08, double duration = 20) =>
        Send(Command(NextId(capability), "nature.cast", BubbleArgs(capability, center, radius: radius, duration: duration)), principal);

    private JsonObject Lookup(string principal, string actionId) =>
        Send(Query("receipt.lookup", new JsonObject { ["action_id"] = actionId }), principal);

    private JsonNode? ReceiptData(string principal, JsonObject committed) =>
        Lookup(principal, committed["action_id"]!.GetValue<string>())["data"]?["receipt"]?["data"];

    private static bool Ok(JsonObject result) => result["ok"]?.GetValue<bool>() == true;
    private static string? Code(JsonObject result) => result["error"]?["code"]?.GetValue<string>();
    private static string? Path(JsonObject result) => result["error"]?["field_path"]?.GetValue<string>();
    private static string Canonical(JsonNode? node) => node == null ? "" : CanonicalJson.Text(node);
    private int Pips => _host.Vitals.Pips;
    private float Health => _host.Vitals.Health;
    private string BubbleOf(JsonObject committed) => committed["data"]?["bubble"]?.GetValue<string>() ?? "";

    private void RemoveAll()
    {
        var directory = ProjectSettings.GlobalizePath(TestRoot);
        if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
    }

    private void Check(bool condition, string label)
    {
        _checks++;
        if (_verbose) GD.Print((condition ? "BUBBLE_PASS " : "BUBBLE_FAIL ") + label);
        if (condition) return;
        _failures++;
        GD.PushError("Bubble: " + label);
    }

    /// <summary>Counts every script or engine error while the suite runs, so one fails the suite itself.</summary>
    private sealed partial class ScriptErrors : Logger
    {
        private readonly object _lock = new();
        public int Count { get; private set; }
        public string First { get; private set; } = "";

        public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
        {
            if (errorType == (int)ErrorType.Warning || function == "push_error" || code.StartsWith("Bubble", StringComparison.Ordinal)) return;
            lock (_lock)
            {
                Count++;
                if (First.Length == 0) First = $"{file}:{line} {code} {rationale}";
            }
        }
    }

    // ---- the consent: the ghost preview, its lead, and a cast at a point ----

    private async Task TestConsent()
    {
        await NewWorld("consent");
        var unpreviewed = Send(Command(NextId("carry"), "nature.cast", BubbleArgs("bubble_carry", _player.GlobalPosition, CommandHost.PlayerAvatar)), Companion, contractValid: false);
        Check(Code(unpreviewed) == "request_invalid" && Path(unpreviewed) == "$" && _host.ActiveBubbles.Count == 0,
            "a Gubble's carry on the player without its preview is refused request_invalid at $, and nothing forms: " + Canonical(unpreviewed["error"]));
        var pips = Pips;
        var preview = Preview(Companion);
        var token = preview["data"]?["preview_id"]?.GetValue<string>() ?? "";
        Check(Ok(preview) && preview["preview"]?.GetValue<bool>() == true && preview["data"]?["capability"]?.GetValue<string>() == "bubble_carry" &&
              preview["data"]?["target"]?.GetValue<string>() == CommandHost.PlayerAvatar && token.Length == 32 && preview["affected"]?[0]?.GetValue<string>() == CommandHost.PlayerAvatar &&
              preview["data"]?["cell_count"]?.GetValue<int>() == 0 && preview["data"]?["affected_ids"] == null && _host.BubbleGhostCount == 1 && _host.ActiveBubbles.Count == 0 && Pips == pips,
            "the Gubble's preview of a carry on the player answers its preview_id and shows the ghost; nothing forms and nothing is charged: " + Canonical(preview));
        var args = BubbleArgs("bubble_carry", _player.GlobalPosition, CommandHost.PlayerAvatar, previewId: token);
        var tooSoon = Send(Command(NextId("carry"), "nature.cast", args), Companion);
        Check(Code(tooSoon) == "not_ready" && Path(tooSoon) == "$.args.preview_id" && tooSoon["error"]?["retryable"]?.GetValue<bool>() == true && _host.ActiveBubbles.Count == 0,
            "cast at once, before the player has seen the ghost for BubbleLaws.GhostLeadS, it waits (not_ready, retryable) and nothing forms: " + Canonical(tooSoon["error"]));
        var moved = Send(Command(NextId("carry"), "nature.cast", BubbleArgs("bubble_carry", _player.GlobalPosition + new Vector3(0.03f, 0, 0), CommandHost.PlayerAvatar, previewId: token)), Companion);
        Wait(BubbleLaws.GhostLeadS);
        var others = Send(Command(NextId("carry"), "nature.cast", args), Player);
        Check(Code(moved) == "revision_conflict" && Path(moved) == "$.args.preview_id" && Code(others) == "revision_conflict",
            $"a cast that is not exactly its preview, or names another caster's preview, is refused revision_conflict at $.args.preview_id ({Code(moved)}, {Code(others)})");
        var carry = Send(Command("carry-ok", "nature.cast", args), Companion);
        var id = BubbleOf(carry);
        Check(Ok(carry) && carry["transient"]?.GetValue<bool>() == true && carry["preview"]?.GetValue<bool>() == false && carry["created"]?[0]?.GetValue<string>() == id &&
              id.StartsWith("effect:", StringComparison.Ordinal) && carry["affected"]?.AsArray().Select(n => n!.GetValue<string>()).SequenceEqual(new[] { id, CommandHost.PlayerAvatar }) == true &&
              carry["data"]?["carried"]?.GetValue<bool>() == true && carry["data"]?["target"]?.GetValue<string>() == CommandHost.PlayerAvatar && carry["data"]?["ended"] == null &&
              carry["data"]?["expires_utc"] != null && _host.PlayerInBubble && _host.BubbleGhostCount == 0,
            "after the lead the cast naming its preview forms the bubble round the player: a transient receipt, its effect: id created, the player carried: " + Canonical(carry));
        var replay = Send(Command("carry-ok", "nature.cast", args), Companion);
        Check(Ok(replay) && replay["replayed"]?.GetValue<bool>() == true && BubbleOf(replay) == id && _host.ActiveBubbles.Count == 1,
            "the same cast again replays its receipt and forms nothing more");
        var again = Send(Command(NextId("carry"), "nature.cast", args), Companion);
        Check(Code(again) == "target_busy", "while a bubble holds the player, another carry on them is refused target_busy: " + Code(again));
        await Frames(30);
        Check(_player.Ride == SmallPlayerController.BubbleRide.Enveloped, "the body rides the bubble: " + _player.Ride);
        // A cast at a point never forms round the player: it waits beside them for the step-in.
        _host.PlayerGoal("stop");
        await Frames(120);
        var onThePlayer = AtPoint(Companion, "bubble_carry", _player.GlobalPosition);
        var beside = AtPoint(Companion, "bubble_carry", _player.GlobalPosition + new Vector3(0.3f, 0, 0.1f));
        await Frames(5);
        Check(Code(onThePlayer) == "invalid_args" && Path(onThePlayer) == "$.args.center_m" && Ok(beside) && beside["data"]?["carried"]?.GetValue<bool>() == false &&
              beside["data"]?["target"] == null && beside["affected"]?.AsArray().Count == 1 && !_host.PlayerInBubble && _host.ActiveBubbles.Count == 1,
            "a carry at a point where the player stands is refused invalid_args; beside them it waits, carrying no one: " + Canonical(onThePlayer["error"]) + " " + Canonical(beside["data"]));
        // The player's own carry on themselves is previewed too, with no lead (they ordered it).
        _host.PlayerGoal("stop");
        Wait(BubbleLaws.PopRespectS);
        var own = CarryOnPlayer(Player, out var ownPreview);
        Check(Ok(ownPreview) && Ok(own) && _host.PlayerInBubble, "the player's own carry on themselves: previewed, then cast at once: " + Canonical(own["error"]));
        _host.PlayerGoal("stop");
        var ownToken = ownPreview["data"]?["preview_id"]?.GetValue<string>();
        var reused = Send(Command(NextId("carry"), "nature.cast", BubbleArgs("bubble_carry", ownPreview["data"]?["center_m"] is JsonArray c
            ? new Vector3(c[0]!.GetValue<float>(), c[1]!.GetValue<float>(), c[2]!.GetValue<float>()) : Vector3.Zero, CommandHost.PlayerAvatar, previewId: ownToken)), Player);
        Check(Code(reused) == "revision_conflict", "a preview is used once: " + Code(reused));
    }

    // ---- every refusal ----

    private async Task TestRefusals()
    {
        await NewWorld("refusals");
        var feet = _player.GlobalPosition;
        JsonObject Carry(string principal, JsonObject args, bool preview = true, bool valid = true) => Send(Command(NextId("refused"), "nature.cast", args, preview), principal, valid);
        var outcome = new Dictionary<string, (string? Code, string? Path)>
        {
            ["a carry on the Gubble"] = Pair(Carry(Player, BubbleArgs("bubble_carry", _companion.GlobalPosition, CommandHost.CompanionAvatarId))),
            ["a carry on an avatar not here (the player)"] = Pair(Carry(Player, BubbleArgs("bubble_carry", feet, "avatar:visitor"))),
            ["a carry on an avatar not here (the Gubble)"] = Pair(Carry(Companion, BubbleArgs("bubble_carry", feet, "avatar:visitor"))),
            ["a carry on the player centred away from them"] = Pair(Carry(Companion, BubbleArgs("bubble_carry", feet + new Vector3(0.4f, 0, 0), CommandHost.PlayerAvatar))),
            ["a boat on the dry floor"] = Pair(Carry(Companion, BubbleArgs("bubble_boat", feet + new Vector3(0.3f, 0, 0), radius: 0.12))),
            ["a carry too small to hold the body"] = Pair(Carry(Companion, BubbleArgs("bubble_carry", feet, CommandHost.PlayerAvatar, radius: 0.05))),
            ["a carry past the island's radius"] = Pair(Carry(Companion, BubbleArgs("bubble_carry", feet, CommandHost.PlayerAvatar, radius: 1.2))),
            ["a lift past the island's duration"] = Pair(Carry(Companion, BubbleArgs("bubble_lift", feet, CommandHost.PlayerAvatar, duration: 90))),
            ["a carry beyond the Gubble's reach"] = Pair(Carry(Companion, BubbleArgs("bubble_carry", new Vector3(-1.6f, 0, -1.2f)))),
            ["a carry outside the island"] = Pair(Carry(Companion, BubbleArgs("bubble_carry", new Vector3(0.6f, 0, 1.6f)))),
            ["a boat on the player"] = Pair(Carry(Companion, BubbleArgs("bubble_boat", feet, CommandHost.PlayerAvatar, radius: 0.12), valid: false)),
            ["Bloom on the player"] = Pair(Send(Command(NextId("bloom"), "nature.cast", new JsonObject
            {
                ["capability"] = "bloom", ["center_m"] = Vec(feet), ["radius_m"] = 0.25, ["duration_s"] = 30, ["target"] = CommandHost.PlayerAvatar,
            }, preview: true), Companion, contractValid: false)),
        };
        var radiusAllowed = Carry(Companion, BubbleArgs("bubble_carry", feet, CommandHost.PlayerAvatar, radius: 0.05))["error"]?["allowed"];
        Check(Canonical(radiusAllowed) == "[0.07,0.2]", "a carry's size refusal names its bounds: " + Canonical(radiusAllowed));
        var expected = new Dictionary<string, (string? Code, string? Path)>
        {
            ["a carry on the Gubble"] = ("invalid_args", "$.args.target"),
            ["a carry on an avatar not here (the player)"] = ("target_not_found", "$.args.target"),
            ["a carry on an avatar not here (the Gubble)"] = ("target_not_found", "$.args.target"),
            ["a carry on the player centred away from them"] = ("invalid_args", "$.args.center_m"),
            ["a boat on the dry floor"] = ("invalid_args", "$.args.center_m"),
            ["a carry too small to hold the body"] = ("invalid_args", "$.args.radius_m"),
            ["a carry past the island's radius"] = ("invalid_args", "$.args.radius_m"),
            ["a lift past the island's duration"] = ("invalid_args", "$.args.duration_s"),
            ["a carry beyond the Gubble's reach"] = ("out_of_bounds", "$.args.center_m"),
            ["a carry outside the island"] = ("out_of_bounds", "$.args.center_m"),
            ["a boat on the player"] = ("request_invalid", "$.args.capability"),
            ["Bloom on the player"] = ("request_invalid", "$.args.capability"),
        };
        foreach (var (name, want) in expected)
            Check(outcome[name] == want, $"{name}: {outcome[name]} (expected {want})");
        Check(_host.ActiveBubbles.Count == 0 && _host.BubbleGhostCount == 0, "no refusal forms a bubble or shows a ghost");
        // An island whose pack has no Bubbles (v1: Glow alone) grants none of the three.
        _host.LoadRules("storybook_wild", 1);
        var none = Carry(Player, BubbleArgs("bubble_carry", feet, CommandHost.PlayerAvatar));
        Check(Code(none) == "unsupported_capability" && Path(none) == "$.args.capability", "a pack without Bubbles grants no carry: " + Canonical(none["error"]));
        await Task.CompletedTask;
    }

    private static (string?, string?) Pair(JsonObject result) => (Code(result), Path(result));

    // ---- the price by principal ----

    private async Task TestPriceByPrincipal()
    {
        // The pool of a fresh team: 8 pips (storybook_wild v5).
        await NewWorld("price", mode: "challenge", pips: 8);
        var start = Pips;
        var preview = Preview(Companion);
        Check(Ok(preview) && Pips == start && start == 8, $"a preview costs nothing ({start} pips)");
        var own = CarryOnPlayer(Companion, out _);
        Check(Ok(own) && Pips == start - 2, $"the Gubble's own carry costs its own-idea price, 2 pips ({start - Pips})");
        var conflict = Send(Command(own["action_id"]!.GetValue<string>(), "nature.cast",
            BubbleArgs("bubble_carry", _player.GlobalPosition, CommandHost.PlayerAvatar, previewId: "0" + new string('a', 31))), Companion);
        var stop = _host.PlayerGoal("stop");
        Check(Ok(stop) && Pips == start - 2 && Code(conflict) == "action_id_conflict", "a pop costs nothing, nor does a refused command");
        await Frames(10);
        var ordered = CarryOnPlayer(Player, out _);
        _host.PlayerGoal("stop");
        Check(Ok(ordered) && Pips == start - 6, $"the player's ordered carry pays the ordered price, 4 pips ({start - Pips} spent)");
        await Frames(10);
        Wait(BubbleLaws.PopRespectS);
        var poor = CarryOnPlayer(Companion, out _, "bubble_lift");
        Check(Code(poor) == "energy_exhausted" && poor["error"]?["balance"]?.GetValue<int>() == 2 && poor["error"]?["price"]?.GetValue<int>() == 3 && !_host.PlayerInBubble && Pips == 2,
            "the Gubble's lift at 3 pips with 2 in the pool is refused energy_exhausted with the balance and the price, and nothing forms: " + Canonical(poor["error"]));
        var last = CarryOnPlayer(Companion, out _);
        Check(Ok(last) && Pips == 0, $"a carry at 2 pips still forms ({Pips} left)");
        await NewWorld("price-lift", mode: "challenge", pips: 8);
        var lift = CarryOnPlayer(Companion, out _, "bubble_lift");
        _host.PlayerGoal("stop");
        Check(Ok(lift) && Pips == 5, $"the Gubble's own lift costs 3 ({8 - Pips})");
        await NewWorld("price-ordered-lift", mode: "challenge", pips: 8);
        var orderedLift = CarryOnPlayer(Player, out _, "bubble_lift");
        Check(Ok(orderedLift) && Pips == 2, $"the player's ordered lift costs 6 ({8 - Pips})");
        // Creative's shadow pool drains and never refuses.
        await NewWorld("price-creative", mode: "creative", pips: 1);
        var shadow = CarryOnPlayer(Companion, out _, "bubble_lift");
        Check(Ok(shadow) && Pips == 0, $"in Creative the shadow pool pays what it can and never refuses ({Pips} left)");
    }

    // ---- the pops and the receipts ----

    private async Task TestPopsAndReceipts()
    {
        await NewWorld("pops");
        // X: the player's stop pops the Gubble's carry, as popped by the player.
        var carry = CarryOnPlayer(Companion, out _);
        var id = BubbleOf(carry);
        var x = _host.PlayerGoal("stop");
        var ended = ReceiptData(Companion, carry);
        Check(Ok(x) && x["affected"]?.AsArray().Any(n => n!.GetValue<string>() == id) == true && x["data"]?["effects_stopped"]?.GetValue<int>() >= 1 && !_host.PlayerInBubble &&
              ended?["ended"]?.GetValue<string>() == "popped_by_player" && ended?["capability"]?.GetValue<string>() == "bubble_carry" &&
              ended?["target"]?.GetValue<string>() == CommandHost.PlayerAvatar && ended?["carried"]?.GetValue<bool>() == true,
            "X (the player's goal.stop) pops the Gubble's carry, and its receipt keeps the kind, the target and popped_by_player: " + Canonical(ended));
        var replayed = Send(Command(carry["action_id"]!.GetValue<string>(), "nature.cast",
            BubbleArgs("bubble_carry", Vector3.Zero, CommandHost.PlayerAvatar, previewId: new string('b', 32))), Companion);
        Check(Code(replayed) == "action_id_conflict", "the cast's action id stays its own");
        // The timer: a bubble at its duration pops, timed_out.
        await Frames(10);
        var waiting = AtPoint(Companion, "bubble_carry", _player.GlobalPosition + new Vector3(0.3f, 0, 0.1f), duration: 2);
        Wait(2.1);
        await Frames(2);
        Check(Ok(waiting) && _host.ActiveBubbles.Count == 0 && ReceiptData(Companion, waiting)?["ended"]?.GetValue<string>() == "timed_out",
            "a bubble at its duration pops, and its receipt says timed_out: " + Canonical(ReceiptData(Companion, waiting)));
        // Anything else: the Gubble's own stop pops its own bubble, popped.
        var other = AtPoint(Companion, "bubble_carry", _player.GlobalPosition + new Vector3(0.3f, 0, 0.1f));
        var gubbleStop = Send(Command(NextId("stop"), "goal.stop", new JsonObject()), Companion);
        Check(Ok(gubbleStop) && gubbleStop["affected"]?.AsArray().Any(n => n!.GetValue<string>() == BubbleOf(other)) == true &&
              ReceiptData(Companion, other)?["ended"]?.GetValue<string>() == "popped",
            "the Gubble's own stop pops its own bubble, popped (by something else): " + Canonical(ReceiptData(Companion, other)));
        // effect.stop names a bubble: the player any, a companion only its own.
        var players = AtPoint(Player, "bubble_carry", _player.GlobalPosition + new Vector3(0.3f, 0, 0.1f));
        var notYours = Send(Command(NextId("stop"), "effect.stop", new JsonObject { ["effect"] = BubbleOf(players) }), Companion);
        var yours = Send(Command(NextId("stop"), "effect.stop", new JsonObject { ["effect"] = BubbleOf(players) }), Player);
        Check(Ok(notYours) && notYours["data"]?["effects_stopped"]?.GetValue<int>() == 0 && Ok(yours) && yours["data"]?["effects_stopped"]?.GetValue<int>() == 1 &&
              ReceiptData(Player, players)?["ended"]?.GetValue<string>() == "popped_by_player",
            "effect.stop by a bubble's id: the Gubble cannot pop the player's, the player can (popped_by_player)");
        // The rules changing pops what was cast under the old ones.
        var underOld = AtPoint(Companion, "bubble_carry", _player.GlobalPosition + new Vector3(0.3f, 0, 0.1f));
        _host.LoadRules("storybook_wild", 5);
        await Frames(2);
        Check(_host.ActiveBubbles.Count == 0 && ReceiptData(Companion, underOld)?["ended"]?.GetValue<string>() == "popped", "new rules pop the old rules' bubbles");
    }
}
