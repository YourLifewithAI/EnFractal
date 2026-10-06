# The command host

> **Kernel record, 6 October 2026.** `game/scripts/native/Kernel/CommandHost.cs` is the room's single command path for [game-command.schema.json](../../../contracts/game-command.schema.json). Manual controls, the companion adapter (A1/A2) and tests send `enfractal.command` and `enfractal.query` as JSON text; the host answers `enfractal.result`.

## Wiring

`RoomWorld` calls `Kernel.CommandHost.Attach(this);` once its room, player and companion exist. `Attach` creates the GDScript invention runtime (which creates and owns `creation_authority.gd`), points the runtime's `command_sink` at the host so the invention editor's commits travel the same path, and adds a small approve/deny prompt. Saves go to `user://saves/rooms/<room_id>/<first 16 hex of the manifest hash>/inventions.json`, so a re-exported room starts a fresh file instead of refusing to load.

Callers: `string Handle(string message, string principal)` (canonical result text) or `JsonObject HandleObject(...)`. The principal is the caller's identity (`player:local` or `companion:local`), assigned by the trusted adapter; any other value is refused with `principal_unknown`, and any `principal`, `approval` or `approved_by` field in a message is `field_unknown`. The player's click calls `Approve(request_id)` or `Deny(request_id)`; nothing in a message can. `PlayerGoal(goal)` sends a goal command as the player for HUD keys.

## Commands

| Op | Behaviour |
|---|---|
| `creation.place`, `creation.revise` | Through the authority. Placement `position_m` is a height hint: the host probes for the support just above it (a spot under the table stays under it); `on` must match the support. Rotation must be about the vertical axis. A revise with only a placement keeps the source; with only a source keeps the placement. `preview: true` runs the same checks and records nothing. |
| `entity.remove` | Creations only; captured objects wait for the sandbox verbs (`unsupported_capability`). |
| `protect.lock`, `protect.unlock` | Objects and creations. A locked thing cannot be revised or removed, even by the player, and is a no-build, no-effect zone. **`protect.unlock` from the companion is `permission_denied`, never held.** |
| `creation.activate` | Runs the creation's Use graph through the runtime; transient receipt. |
| `goal.set` | On `avatar:companion`: `follow`, `stay`, `come`, `look_at`, `point_at` (target or position). `go_to`, `fetch`, `wander` are `unsupported_capability` until A2. A companion directing `avatar:player` is `actor_denied`. |
| `goal.stop`, `effect.stop` | Never refused on revisions. `goal.stop` without an actor stops the companion and every running creation effect; `effect.stop "all"` stops every running creation effect. |
| `entity.grab/release/place/set_part/transform`, `effect.start`, `style.set`, `room.checkpoint`, `room.undo` | Validated structurally, then `unsupported_capability` naming the packet that will add them. |

Order of checks: envelope and size (64 KiB canonical), rate limit (companion, 30 messages per second), fingerprint, **replay** (a committed `(principal, action_id)` with the same fingerprint returns its receipt with `replayed: true` before any permission or revision check; different content is `action_id_conflict`), held approvals, argument structure, player-only ops, unsupported ops, `expected_revision` and `expected_entities` (skipped for the two stop ops; destructive ops must name one, and `expected_entities` must cover their targets), approval policy, execution.

## Receipts

- **Durable** (place, revise, remove, lock, unlock): stored by the authority in the same atomic save as the state, keyed `principal|action_id`, with the contract fingerprint, op, time and approver. The host rebuilds every answer from that receipt, so the first answer and every replay are identical apart from `replayed`, and they survive a reload.
- **Transient** (goals, effect stops, activations): held by the host for the session, at most 2,048.
- Failed commands consume nothing; a preview records nothing.

## Approvals

A companion `entity.remove` or `creation.revise` of a creation the player owns is held. The host answers `approval_required` (`retryable: true`) with `approval_needed.request_id` (128 random bits, 32 hex characters), a reason that names the entity id only (names are untrusted world text) and `expires_utc` five minutes ahead. Sending the same command again returns the same request; the same action id with other content is `action_id_conflict`. On `Approve`, the host re-checks that the touched entities are unchanged (otherwise `approval_mismatch` and state `expired`), then commits the held command under the companion's principal and action id with `approved_by: player:local` in the durable receipt. `Deny` makes that action id `permission_denied` for good; an expired request can be asked again with a new id. `approval.status` answers `pending`, `approved` (with the result), `denied` or `expired` to the requesting principal or the player, and `target_not_found` otherwise. At most eight requests per principal wait at once.

## Queries

`room.describe`, `entities.list` (filters, `limit`, numeric `cursor`), `entity.inspect` (with `protected_by`), `observe` (radius up to 20 m, a line-of-sight ray per entity, at most 100 entities and 50 texts, every name marked `untrusted: true`; **a companion observes only through `avatar:companion`**), `receipt.lookup` (principal-bound, durable and transient), `approval.status`, `capabilities.list`. `jobs.status` has no jobs yet.

## Error mapping

The GDScript kernel's codes map onto the contract's as in [contracts/README.md](../../../contracts/README.md), with these additions: `target_locked` and `protected_zone` → `target_protected`; `room_bounds`, `surface_unavailable`, `surface_uneven` → `out_of_bounds`; `surface_mismatch`, `already_locked`, `not_locked`, `target_invalid` → `invalid_args`; `unlock_denied` and `activation_context` → `permission_denied`; creation compiler codes → `invalid_args` with `field_path` under `$.args.source`.

## Evidence

`game/tests/native_kernel_command_host.tscn` (95 checks) drives place, revise, remove, lock and goal commands in the real test room with the real bodies: receipts, replay (including key order and after a reload), conflicts, stale and uncovered expectations, previews, `on` placement found by a physics surface query, the approval flow (approve, deny, lapse, expiry, re-ask), unlock refusal, principals and approvals smuggled into messages, size, version and room checks, untrusted observation and rate limits. With `-- --dump=DIR` it writes every message and result; `contracts/validate.py` accepted all 175 of them.
