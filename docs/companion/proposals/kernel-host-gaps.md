# The kernel host against the mock: change requests before the Run 2 swap

Written 6 October 2026 (night), against `run1/integration` at `6cb502c`. In Run 2 the companion's link
serves Lane P's `game/scripts/native/Kernel/CommandHost.cs` instead of Lane A's mock host. This page
records what was aligned on the mock's side and what the kernel host needed then. Line numbers are at
`6cb502c`. Lane A does not edit `game/**` or `contracts/**`; the integrator applies or hands these on.

> **Status, 7 October 2026:** P1-P8 are closed on the real host, and C1-C2 are integrated in the contracts. The requests and diffs below are the 6 October record, not outstanding work. See [the Run 1 status](../../runs/RUN-1-STATUS.md#still-open) and [the host record](../../engine/phase3/command-host.md). The transport swap remains Run 2 A2 work.

## Aligned on the mock's side (Lane A, this round)

| Behaviour | Now, in both hosts | Pinned by |
|---|---|---|
| Durable ledger | 2,048 receipts, the last 256 the player's; a checkpoint compacts to a revision and a 64-bit fingerprint prefix, 4,096 kept; 16 checkpoints | `test_kernel_alignment.py` reads the kernel's constants and fails on drift |
| Rate limit | 30 messages per companion in any one second, commands and queries together, counted before parsing; stops exempt; the player never limited; a refusal echoes nothing of the message | `RateLimits`, `test_a_companion_gets_30_messages_a_second` |
| Pending approvals | 8 per principal | `test_refuses_more_than_the_pending_approval_limit` |
| `"preview": false` | Part of the fingerprinted content: a retry that only adds it is `action_id_conflict`; so is a preview under a committed action id. The MCP tools send `preview` only when true | `Previews`, `test_a_tool_call_with_preview_false_is_the_same_command_because_it_is_never_sent` |
| Approval lapse | Shown in `approval.status` at once, before any click; the player may read any request's status | `test_approval_status_reports_a_lapse_at_once`, `test_the_player_can_read_a_companions_approval_status` |
| `room.checkpoint` data | `{"checkpoint_revision": N}` only | `test_a_checkpoint_answers_its_revision_and_replays_it` |

Left different on purpose, because Run 2's host-enforced tiers (LIVE-VOICE.md) replace both: which
companion commands are held (the mock's recommended set for any target; the kernel's removal and
revision of the player's creations), and the approval reason (the mock quotes sanitised names and the
concrete effect, a Run 1 review fix; the kernel names entity ids only).

## Change requests for Lane P

> **7 October 2026, closed:** `Capabilities` returns only `items`, currently empty.

**P1. `capabilities.list` answers a payload the contract refuses (blocks the swap).**
`CommandHost.cs` `Capabilities()` (lines 824-831) returns `commands`, `queries`, `goals`, `player_only`
and `needs_player_approval`. `$defs/data/capabilities.list` requires `items` (of `capability_summary`)
and allows nothing else. The adapter validates every result and will not relay this one, so after the
swap `capabilities_list` always answers `internal_error`. There are no effects yet, so:

```diff
-    private static readonly string[] SupportedGoals = { "follow", "stay", "come", "look_at", "point_at" };
...
-    private static JsonObject Capabilities() => new()
-    {
-        ["commands"] = new JsonArray("creation.place", "creation.revise", "creation.activate", "entity.remove", "protect.lock", "protect.unlock", "goal.set", "goal.stop", "effect.stop", "room.checkpoint"),
-        ["queries"] = new JsonArray(QueryOps.Where(o => o != "jobs.status").OrderBy(o => o, StringComparer.Ordinal).Select(o => (JsonNode?)JsonValue.Create(o)).ToArray()),
-        ["goals"] = new JsonArray(SupportedGoals.Select(g => (JsonNode?)JsonValue.Create(g)).ToArray()),
-        ["player_only"] = new JsonArray("protect.unlock"),
-        ["needs_player_approval"] = new JsonArray("entity.remove of the player's creation", "creation.revise of the player's creation"),
-    };
+    /// <summary>Effect capabilities as contract capability_summary items; none until effect.start arrives (Run 3).</summary>
+    private static JsonObject Capabilities() => new() { ["items"] = new JsonArray() };
```

Test, `CommandHostTest.cs` line 121:

```diff
-        Check(Ok(capabilities) && capabilities["data"]!["player_only"]!.AsArray().Any(o => o!.GetValue<string>() == "protect.unlock"), "capabilities name protect.unlock as player-only");
+        Check(Ok(capabilities) && capabilities["data"]!.AsObject().Count == 1 && capabilities["data"]!["items"]!.AsArray().Count == 0, "capabilities.list answers the contract's items: no effects yet");
```

and run the `--dump` output through `contracts/validate.py` with a `capabilities.list` in it.

> **7 October 2026, closed:** `Replay` and `Lookup` read durable, then compacted, then transient records.

**P2. A stop that reuses a durable command's action id hides that command's receipt.**
`Replay` (line 605) and `Lookup` (line 866) look in `_transient` before the authority's durable record,
and `Transient()` stores a stop under the reused key. After `goal.stop` reuses `lock-1`, resending the
lock answers `action_id_conflict` and `receipt.lookup lock-1` returns the stop, although the contract
says to look a receipt up before retrying. Check `Authority.receipt_for` and `compacted_receipt` first,
then `_transient`, in both. Test: lock under `lock-1`, `goal.stop` under `lock-1`, resend the lock:
`replayed: true` with the lock's revision; `receipt.lookup` returns the lock (the mock's
`test_a_stop_reusing_a_durable_action_id_leaves_that_receipt_replayable`).

> **7 October 2026, closed:** no blanket avatar exemption in `CheckPerceived`; only the documented remembered-goal targets are exempt.

**P3. Commands may name the player's avatar out of sight.**
`CheckPerceived` (line 1031) skips every `avatar:` id. PERCEPTION.md and the mock exempt only the
companion's own avatar, which `Perceive` already always includes. Today a companion behind the box can
`look_at` or `point_at` `avatar:player`.

```diff
-            if (!id.StartsWith("avatar:", StringComparison.Ordinal) && !Perceives(principal, id))
+            if (!Perceives(principal, id))
```

Test: companion behind `obj:box`, player out of its sight: `goal.set look_at` with target
`avatar:player` is `target_not_found`, byte-identical to target `avatar:nobody`. With memory (P6) a
remembered player becomes a valid target for the goals that only move or turn the companion.

> **7 October 2026, closed:** omitted `radius_m` defaults to `20.0f`.

**P4. `observe` defaults to 3 m.** `Observe` (line 840) uses `3.0f` when `radius_m` is absent. The
founder's rule is everything in line of sight, and PERCEPTION.md gives 20 m as both default and
maximum, so a model that omits the radius misses what it can see. Proposed: `: 20.0f`. Test: a
creation in clear sight more than 3 m from the companion is in `visible` with no `radius_m`.

> **7 October 2026, closed:** `Observe` skips shell entities.

**P5. `observe` lists shell parts.** `Observe` (line 848) skips only the actor, so the always-perceived
floor, walls and ceiling fill `visible` and count against its 100 items, which a captured room with many
shell parts will exhaust. The mock leaves them out (`room.describe` and `entities.list` cover them):

```diff
-            if (id == actor || !sight.Contains(id)) continue;
+            if (id == actor || entity["kind"]!.GetValue<string>() == "shell" || !sight.Contains(id)) continue;
```

Test: no `visible` item has kind `shell`.

> **7 October 2026, closed:** the host has bounded perception memory, remembered results and clearing hooks. It remains interim code; [shared knowledge and selective memory](../JOURNAL.md) are chosen but not implemented.

**P6. Perception memory** (known; `command-host.md` lists the parts). From the mock, precisely:
`seen: "now"` on a companion's in-sight summaries; remembered summaries as last seen plus
`last_seen_ago_s`, `last_seen_revision`, `may_be_stale` (60 s, proposed, or any change, sticky until
seen again); `observe.remembered` (within the radius of where last seen, nearest first, at most 50);
`entities.list` and `entity.inspect` answering from memory; the memory of a thing dropped when its
last-seen place is in sight and it is not there; filled only from the companion's own avatar; at most
256 entries, least recently seen first; cleared on room load and by a session hook the Run 2 bridge
calls (the mock's `session_event`). Tests: port `companion/tests/test_perception_memory.py` (suites
`Remembering`, `Staleness`, `LookingAgain`, `Commands`, `Arrival`, `NeverThroughOthers`, `Bounds`,
`NothingHiddenLeaks`).

> **7 October 2026, closed:** targeted goals return `job_id`, and `JobStatus` reports the actual job state. The A2 goal runner remains later work.

**P7. Goal jobs.** `goal.set` with a target returns no `job_id`, and `jobs.status` is always
`target_not_found`. The mock: a goal with a target returns `job_id`; the job is `running` until the goal
runner reports arrival, then `succeeded`, or `failed` with `target_not_found` (out of sight, moved or
gone alike) or `revision_conflict` (in sight, moved beyond reach); a new goal or a stop cancels it; at
most 256 jobs per principal; another principal's job id looks unknown. The goal runner is Lane A's
(`game/scripts/native/Companion/`); the job store and the query are the host's. Tests: the mock's
`Arrival` suite and its `jobs.status` cases.

> **7 October 2026, closed:** `entity.release` is absent from `TransientOps`. The sandbox release operation itself is not implemented yet.

**P8. `entity.release` is listed as transient** (`TransientOps`, line 84). A release puts an object
down, which is saved state; the contract gives transient receipts to goals, effects and grabs only. When
the sandbox verbs land, remove it from `TransientOps`. Test: a release answers `transient: false` and
moves the revision.

## Contract requests

> **7 October 2026, integrated:** the schema states the default of 20 m.

**C1. State the `observe` default** (with P4), in `game-command.schema.json` `$defs/args/observe`
(line 2000):

```diff
           "radius_m": {
+            "description": "Defaults to 20, the maximum: an avatar perceives everything in its line of sight.",
             "type": "number",
```

> **7 October 2026, integrated:** the Idempotency paragraph explains `"preview": false` as different content.

**C2. Say what `"preview": false` is**, in `contracts/README.md` line 44 (Idempotency):

```diff
-- **Idempotency.** The fingerprint is the SHA-256 of the canonical command JSON as received from the sender, never of an internal translation. Same principal + same `action_id` + same content returns the original receipt with `replayed: true`. Same `action_id` with different content is refused with `action_id_conflict`. Previews record no receipt. After an uncertain result, query `receipt.lookup` before retrying.
+- **Idempotency.** The fingerprint is the SHA-256 of the canonical command JSON as received from the sender, never of an internal translation. Same principal + same `action_id` + same content returns the original receipt with `replayed: true`. Same `action_id` with different content is refused with `action_id_conflict`. Previews record no receipt. `preview` is part of the content: a retry that only adds `"preview": false` is a different command (`action_id_conflict`), so senders leave it out unless it is true. After an uncertain result, query `receipt.lookup` before retrying.
```

**C3. `room.checkpoint` result data** is Lane P's open request; the mock now answers exactly
`{"checkpoint_revision": N}`, matching the kernel.
