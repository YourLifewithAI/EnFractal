using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EnFractal.Native.Kernel;

/// <summary>
/// Wishes (docs/magic/README.md "Making a wish"; the founder's decision of 13 October: no separate runner). The player
/// makes a wish in the wish box; it waits its turn, first in first out, in a saved journal. The AI that is already
/// connected and playing as the Gubble takes it through its ordinary companion surface:
/// - wish.list and wish.wait read the queue (the companion's adapter makes wish.wait a bounded wait), wish.take makes the
///   next wish active, and wish.report says done, unable or unsupported;
/// - every companion result carries wishes_waiting while wishes wait, a count and never a wish's words;
/// - while a wish is active, the host itself ties every command the companion sends to it (its own record of who sent
///   what when, never an action id the AI chose), bounded at WishQueue.MaxCommands;
/// - the host alone decides "granted", from its own receipts and from goal jobs it saw finish (941185d): a report of
///   done while one of the wish's goals still runs is not_ready, and a report never writes a success;
/// - the player's Stop cancels the active and waiting wishes at once, denies the commands they left waiting for a click,
///   and stops the Gubble; an AI that disconnects mid-wish leaves it interrupted. With no AI connected, wishes wait.
/// </summary>
public partial class CommandHost
{
    public static string DefaultWishSaveRoot { get; set; } = "user://single_player/wishes";
    public WishQueue Wishes { get; private set; } = WishQueue.Fresh("unconfigured");
    public string WishNotice { get; private set; } = "";
    /// <summary>Whether the player's AI is connected now (a companion link session is open). Wishes wait while none is.</summary>
    public bool CompanionConnected => _companionSessions > 0;
    private int _companionSessions;
    private IWishQueueStore? _wishStore;
    private bool _wishesUnavailable;
    /// <summary>The host's own record: a companion command held for the player's click while a wish was active, by action id, and that wish.</summary>
    private readonly Dictionary<string, string> _wishOfHeld = new(StringComparer.Ordinal);
    /// <summary>Companion ops that are never one of a wish's actions: taking and reporting wishes, speaking, noting.</summary>
    private static readonly HashSet<string> WishBookkeepingOps = new() { "wish.take", "wish.report", "companion.say", "journal.note" };

    public void ConfigureWishes(IWishQueueStore store)
    {
        _wishStore = store;
        _wishOfHeld.Clear();
        try { Wishes = store.Load(); _wishesUnavailable = false; WishNotice = ""; }
        catch (WishQueueException) { _wishesUnavailable = true; WishNotice = "The wish journal could not be loaded. The existing file was kept."; }
    }
    public JsonObject PlayerWish(string text, string? aimId = null)
    {
        var args = new JsonObject { ["text"] = text };
        if (aimId != null) args["aim_id"] = aimId;
        return PlayerCommand("wish.submit", args);
    }
    public JsonObject PlayerWishStop() => PlayerCommand("wish.stop", new JsonObject());
    public JsonObject PlayerWishes() => WishesRead(PlayerPrincipal);
    private int WishesWaiting => Wishes.Items.Count(item => item.Status == "queued");

    private void PublishWishes(WishQueue candidate)
    {
        if (ReferenceEquals(candidate, Wishes)) return;
        if (_wishesUnavailable || _wishStore == null) throw new WishQueueException("not_ready", "The wish journal is not ready.");
        _wishStore.Save(candidate);
        Wishes = candidate;
        WishNotice = "";
    }
    private JsonObject WishSubmit(JsonElement args, string principal, string actionId, string fingerprint, bool preview)
    {
        if (principal != PlayerPrincipal) throw new Refusal("permission_denied", "Only the player can make a wish.");
        try
        {
            if (_wishesUnavailable || _wishStore == null) throw new WishQueueException("not_ready", "The wish journal is not ready.");
            var aimId = Str(args, "aim_id");
            if (aimId != null && !KnownToTeam(aimId)) throw new Refusal("target_not_found", "Neither of you knows that target.", "$.args.aim_id");
            // Preview checks capacity and text but allocates no persisted ID or receipt.
            var candidate = Wishes.Submit(actionId, fingerprint, Str(args, "text")!, aimId, out var entry);
            if (preview) return Previewed("wish.submit", principal, actionId);
            var replay = ReferenceEquals(candidate, Wishes);
            PublishWishes(candidate);
            var result = WishSubmitReceipt(entry);
            if (replay) result["replayed"] = true;
            if (!CompanionConnected) WishNotice = "Your wish is waiting. It waits until your AI is connected; the game never starts another program for you.";
            return result;
        }
        catch (WishQueueException error) { throw new Refusal(error.Code, error.Message, retryable: error.Code == "not_ready"); }
    }
    private JsonObject WishSubmitReceipt(WishEntry entry)
    {
        var result = Result("wish.submit", PlayerPrincipal, entry.ActionId, null, true);
        result["transient"] = false;
        result["data"] = new JsonObject { ["wish_id"] = entry.Id, ["status"] = "queued" };
        return result;
    }
    /// <summary>Wish submission owns its action id across every operation, as other durable receipts do.</summary>
    private JsonObject? ReplayWish(string principal, string actionId, string fingerprint)
    {
        if (principal != PlayerPrincipal || Wishes.Items.FirstOrDefault(entry => entry.ActionId == actionId) is not { } entry) return null;
        if (entry.Fingerprint != fingerprint) throw new Refusal("action_id_conflict", "That action id already names another wish.");
        var receipt = WishSubmitReceipt(entry);
        receipt["replayed"] = true;
        return receipt;
    }
    /// <summary>Fallback for receipt.lookup: wish receipts live with this bounded local wish ledger.</summary>
    private JsonObject? WishReceiptLookup(string principal, string actionId) => principal == PlayerPrincipal &&
        Wishes.Items.FirstOrDefault(entry => entry.ActionId == actionId) is { } entry
            ? new JsonObject { ["found"] = true, ["receipt"] = WishSubmitReceipt(entry) } : null;

    private JsonObject WishStop(string principal, string actionId, bool preview)
    {
        if (principal != PlayerPrincipal) throw new Refusal("permission_denied", "Only the player can stop the wish queue.");
        if (preview) return Previewed("wish.stop", principal, actionId);
        var pending = Wishes.Items.Where(entry => !entry.Terminal).ToArray();
        var cancelled = Wishes.CancelAll();
        try { PublishWishes(cancelled); }
        catch (WishQueueException)
        {
            Wishes = cancelled; _wishesUnavailable = true;
            WishNotice = "The Gubble and this session's wishes stopped. The journal could not be saved; recover it before starting more wishes.";
        }
        // Revocation is immediate, including a command waiting for a click. A later wish never approves one.
        DenyWishHeld(wishId => pending.Any(entry => entry.Id == wishId));
        PlayerGoal("stop");
        var result = Result("wish.stop", principal, actionId, null, true);
        result["transient"] = true;
        result["data"] = new JsonObject { ["cancelled"] = pending.Length };
        return result;
    }

    private JsonObject WishesRead(string principal)
    {
        if (principal != PlayerPrincipal) throw new Refusal("permission_denied", "The wish journal belongs to the player.", "$.op");
        return new JsonObject { ["items"] = new JsonArray(Wishes.Items.Select(entry => (JsonNode)WishSummary(entry)).ToArray()) };
    }
    private static JsonObject WishSummary(WishEntry entry)
    {
        var row = new JsonObject
        {
            ["wish_id"] = entry.Id, ["text"] = entry.Text, ["status"] = entry.Status,
            ["action_count"] = entry.CommandCount,
            ["refusal_codes"] = new JsonArray(entry.Refusals.Select(value => (JsonNode?)JsonValue.Create(value.Code)).ToArray()),
        };
        if (entry.AimId != null) row["aim_id"] = entry.AimId;
        if (entry.ResultCode != null) row["result_code"] = entry.ResultCode;
        return row;
    }

    // ---- the connected AI's side: wish.list, wish.wait, wish.take, wish.report ----

    /// <summary>A waiting or active wish as the AI sees it (contracts $defs/companion_wish): the player's words and the host's own counts.</summary>
    private static JsonObject CompanionWish(WishEntry entry)
    {
        var row = new JsonObject
        {
            ["wish_id"] = entry.Id, ["text"] = entry.Text, ["status"] = entry.Status,
            ["action_count"] = entry.CommandCount, ["pending_count"] = entry.PendingActionIds.Count,
            ["refusal_codes"] = new JsonArray(entry.Refusals.Select(value => (JsonNode?)JsonValue.Create(value.Code)).ToArray()),
        };
        if (entry.AimId != null) row["aim_id"] = entry.AimId;
        return row;
    }

    /// <summary>The active wish and those waiting, oldest first. Finished wishes are the player's, never listed to an AI.</summary>
    private JsonObject WishList()
    {
        var data = new JsonObject { ["waiting"] = new JsonArray(Wishes.Items.Where(item => item.Status == "queued").Select(item => (JsonNode)CompanionWish(item)).ToArray()) };
        if (Wishes.Active is { } active) data["active"] = CompanionWish(active);
        return data;
    }

    /// <summary>The host answers at once; the companion's adapter asks again for up to 30 s while nothing is waiting.</summary>
    private JsonObject WishWait()
    {
        var data = new JsonObject { ["waiting"] = WishesWaiting };
        if (Wishes.Active is { } active) data["active_wish_id"] = active.Id;
        return data;
    }

    private JsonObject WishTake(string principal, string actionId, string fingerprint, bool preview)
    {
        if (principal != CompanionPrincipal) throw new Refusal("permission_denied", "Wishes are taken by the player's AI; the player makes them in the wish box.", "$.op");
        try
        {
            if (_wishesUnavailable || _wishStore == null) throw new WishQueueException("not_ready", "The wish journal is not ready.");
            if (preview) return Previewed("wish.take", principal, actionId);
            // One wish at a time, in order: with one already active, that one is the answer.
            var candidate = Wishes.ClaimNext(out var entry);
            PublishWishes(candidate);
            var data = new JsonObject { ["waiting"] = WishesWaiting };
            if (entry != null) data["wish"] = CompanionWish(Wishes.Items.First(item => item.Id == entry.Id));
            return Transient("wish.take", principal, actionId, fingerprint, new JsonArray(), data);
        }
        catch (WishQueueException error) { throw new Refusal(error.Code, error.Message, retryable: error.Code == "not_ready"); }
    }

    private JsonObject WishReport(JsonElement args, string principal, string actionId, string fingerprint, bool preview)
    {
        if (principal != CompanionPrincipal) throw new Refusal("permission_denied", "Only the player's AI reports the wish it took.", "$.op");
        var id = Str(args, "wish_id")!;
        var code = Str(args, "outcome") switch { "done" => "completed", "unsupported" => "unsupported", _ => "failed" };
        try
        {
            if (_wishesUnavailable || _wishStore == null) throw new WishQueueException("not_ready", "The wish journal is not ready.");
            if (Wishes.Active is not { } active || active.Id != id) throw new Refusal("target_not_found", "That is not the wish you are working on.", "$.args.wish_id");
            // Done waits for the host to see every action it started finish (941185d); ask again later.
            if (code == "completed" && active.PendingActionIds.Count > 0)
                throw new Refusal("not_ready", "This wish's actions are still running: follow them with jobs_status, then report again.", retryable: true);
            if (preview) return Previewed("wish.report", principal, actionId);
            // A report cannot leave a held action alive. Deny while the wish is active, so the host's normal approval
            // result is part of its evidence; then end its running goal, recorded as cancelled.
            DenyWishHeld(wishId => wishId == id);
            if (Wishes.Active is { } ending && ending.Id == id) StopWishGoal(ending);
            if (Wishes.Active?.Id == id) PublishWishes(Wishes.Complete(id, code));
            var status = Wishes.Items.First(entry => entry.Id == id).Status;
            return Transient("wish.report", principal, actionId, fingerprint, new JsonArray(), new JsonObject { ["wish_id"] = id, ["status"] = status });
        }
        catch (WishQueueException error) { throw new Refusal(error.Code, error.Message, retryable: error.Code == "not_ready"); }
    }

    private static void CheckWishArgs(string op, JsonElement args)
    {
        var allowed = op == "wish.report" ? new[] { "wish_id", "outcome" } : Array.Empty<string>();
        foreach (var property in args.EnumerateObject())
            if (!allowed.Contains(property.Name))
                throw new Refusal("field_unknown", "This argument is not part of the operation.", "$.args." + KernelJson.DisplayText(property.Name, 64));
        if (op != "wish.report") return;
        foreach (var name in allowed)
            if (!args.TryGetProperty(name, out _)) throw new Refusal("invalid_args", $"This operation needs '{name}'.", "$.args." + name);
        if (args.GetProperty("wish_id").ValueKind != JsonValueKind.String || !WishIdPattern.IsMatch(args.GetProperty("wish_id").GetString()!))
            throw new Refusal("invalid_args", "wish_id is a wish's id, as wish_take gave it.", "$.args.wish_id");
        if (args.GetProperty("outcome").ValueKind != JsonValueKind.String || args.GetProperty("outcome").GetString() is not ("done" or "unable" or "unsupported"))
            throw new Refusal("invalid_args", "outcome is done, unable or unsupported.", "$.args.outcome");
    }
    private static readonly System.Text.RegularExpressions.Regex WishIdPattern = new(@"\Awish:[a-z2-7]{26}\z", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>On every companion result while wishes wait: their count, never their words (the adapter adds a fixed notice).</summary>
    private JsonObject WithWishNotice(JsonObject result)
    {
        var waiting = WishesWaiting;
        if (waiting == 0) return result;
        var noticed = (JsonObject)result.DeepClone();
        noticed["wishes_waiting"] = waiting;
        return noticed;
    }

    // ---- the host's own record of a wish's actions ----

    /// <summary>
    /// Before a companion command runs while a wish is active: it is one of the wish's actions, inside the wish's budget.
    /// Stops always apply; bookkeeping (wish ops, speech, notes) is not an action; a held command the player approves
    /// later was admitted when it was asked.
    /// </summary>
    private void CheckWishCommandBudget(JsonElement root, string principal)
    {
        if (principal != CompanionPrincipal || _approving || Str(root, "schema") != "enfractal.command") return;
        var op = Op(root);
        if (StopOps.Contains(op) || WishBookkeepingOps.Contains(op) || Wishes.Active is not { } active) return;
        if (root.TryGetProperty("preview", out var preview) && preview.ValueKind == JsonValueKind.True) return;
        if (_wishesUnavailable)
            throw new Refusal("not_ready", "The wish journal needs recovery before this wish can go on.", retryable: true);
        if (active.Recorded(Str(root, "action_id") ?? ""))
            throw new Refusal("action_id_conflict", "That action already has a receipt for this wish; look it up.");
        if (active.CommandCount >= WishQueue.MaxCommands)
            throw new Refusal("budget_exceeded", "This wish has reached its action limit. Report it with wish_report.");
    }

    /// <summary>
    /// The receipt itself is evidence, recorded against the wish that was active when the companion sent the command.
    /// An AI's report and its words never write a success. An accepted goal with a job (fetch, go_to, come, look_at,
    /// point_at) has not happened yet: it stays pending until <see cref="WishGoalEnded"/> sees the job end. A follow runs
    /// until something replaces it, so it counts once accepted. A command held for the player's click belongs to the wish
    /// active when it was asked, and its final result (approved, denied, lapsed) counts only if that wish is still active.
    /// </summary>
    private void ObserveWishCommand(JsonElement root, JsonObject result, string principal)
    {
        if (principal != CompanionPrincipal || Str(root, "schema") != "enfractal.command" || Wishes.Active is not { } active ||
            root.TryGetProperty("preview", out var preview) && preview.ValueKind == JsonValueKind.True ||
            result["preview"]?.GetValue<bool>() == true || result["replayed"]?.GetValue<bool>() == true) return;
        var op = Op(root);
        if (StopOps.Contains(op) || WishBookkeepingOps.Contains(op)) return;
        var actionId = Str(root, "action_id") ?? "";
        var ok = result["ok"]?.GetValue<bool>() == true;
        var code = result["error"]?["code"]?.GetValue<string>() ?? "completed";
        // Still pending work: the final result is observed after the player's click, for the wish it was held under only.
        if (code == "approval_required") return;
        if (_approvalByAction.ContainsKey(principal + "|" + actionId) && (!_wishOfHeld.Remove(actionId, out var heldFor) || heldFor != active.Id)) return;
        if (active.Recorded(actionId)) return;
        var pending = ok && op == "goal.set" && _runningGoals.TryGetValue(CompanionAvatarId, out var job) &&
            job.ActionId == actionId && job.Principal == principal && job.Goal != "follow";
        try
        {
            // A refused 33rd action cannot fit another receipt. End this bounded wish honestly;
            // reaching the action cap is not a journal-storage failure.
            if (active.CommandCount >= WishQueue.MaxCommands)
            {
                StopWishGoal(active);
                PublishWishes(Wishes.Complete(active.Id, "failed"));
            }
            else PublishWishes(Wishes.RecordReceipt(active.Id, actionId, ok, code, pending));
        }
        catch (WishQueueException)
        {
            _wishesUnavailable = true;
            WishNotice = "The action finished, but its wish record could not be saved. Further wish actions are waiting for recovery.";
        }
    }

    /// <summary>Hold calls this: a companion command held for the player's click while a wish is active belongs to that wish.</summary>
    private void HeldForWish(string principal, string actionId)
    {
        if (principal == CompanionPrincipal && Wishes.Active is { } active) _wishOfHeld[actionId] = active.Id;
    }

    /// <summary>Deny the commands held for the player's click that belong to the wishes the filter names.</summary>
    private void DenyWishHeld(Func<string, bool> forWish)
    {
        foreach (var approval in PendingApprovals.Where(approval => approval.Principal == CompanionPrincipal &&
            _wishOfHeld.TryGetValue(approval.ActionId, out var wishId) && forWish(wishId)).ToArray())
            Deny(approval.RequestId);
        // Their record ends with them (a denial observed while the wish was active already removed its own).
        foreach (var actionId in _wishOfHeld.Where(pair => forWish(pair.Value)).Select(pair => pair.Key).ToArray()) _wishOfHeld.Remove(actionId);
    }

    /// <summary>CancelGoal and Finish call this when a goal job ends: a wish's pending goal becomes its success or refusal.</summary>
    private void WishGoalEnded(GoalJob job)
    {
        if (job.Principal != CompanionPrincipal || Wishes.Active is not { } active || !active.PendingActionIds.Contains(job.ActionId)) return;
        var succeeded = job.State == "succeeded";
        var code = succeeded ? "completed" : job.Result?["error"]?["code"]?.GetValue<string>() ?? job.State;
        try { PublishWishes(Wishes.Settle(active.Id, job.ActionId, succeeded, code)); }
        catch (WishQueueException)
        {
            _wishesUnavailable = true;
            WishNotice = "A wish action ended, but its wish record could not be saved. Further wish actions are waiting for recovery.";
        }
    }

    /// <summary>A wish that ends without completing takes its still-running goal with it, recorded as cancelled.</summary>
    private void StopWishGoal(WishEntry wish)
    {
        if (Companion == null || !_runningGoals.TryGetValue(CompanionAvatarId, out var job) || !wish.PendingActionIds.Contains(job.ActionId)) return;
        CancelGoal(CompanionAvatarId);
        Companion.Stop();
    }

    /// <summary>
    /// The bridge's session events (SessionEvent): an AI is connected while a companion session is open. One that leaves in
    /// the middle of a wish leaves it interrupted, with what it held for a click denied and its running goal stopped;
    /// waiting wishes keep waiting for the next AI.
    /// </summary>
    private void WishCompanionSession(string principal, string sessionEvent)
    {
        if (principal != CompanionPrincipal) return;
        _companionSessions = Math.Max(0, _companionSessions + sessionEvent switch { "start" => 1, "end" => -1, _ => 0 });
        if (_companionSessions == 0) InterruptActiveWish("Your AI disconnected during a wish, and the wish journal could not be saved.");
    }

    /// <summary>The room is closing: an active wish is saved as interrupted now, rather than at the next load. Its body is going too.</summary>
    private void WishesClosing()
    {
        try { PublishWishes(Wishes.InterruptActive()); }
        catch (WishQueueException) { }
    }

    private void InterruptActiveWish(string notice)
    {
        if (Wishes.Active is not { } active) return;
        DenyWishHeld(wishId => wishId == active.Id);
        if (Wishes.Active is { } still && still.Id == active.Id) StopWishGoal(still);
        try { PublishWishes(Wishes.InterruptActive()); }
        catch (WishQueueException) { _wishesUnavailable = true; WishNotice = notice; }
    }
}
