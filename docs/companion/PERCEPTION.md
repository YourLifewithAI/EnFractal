# What the companion perceives and remembers

**The founder's rule (6 October 2026):** the companion perceives anything within its avatar's line of
sight. The rule applies to every companion query and every command, not only `observe`. The companion
never perceives through the player's avatar.

**The founder's answer on memory (evening of 6 October 2026):** the companion remembers what it has
seen and where, and says when that may be out of date. Goals that only move or turn it may aim at a
remembered thing; anything that changes a thing still needs it in sight now.

The mock host implements both in `companion/src/enfractal_companion/perception.py` and `mock_host.py`.
`companion/tests/test_perception.py` and `test_perception_memory.py` pin them, and `test_no_holds.py`
runs the same suites again with no approval holds configured. In Run 2 the kernel host replaces the
geometry with physics ray casts against the real colliders. The rules and the tests carry over; the
geometry does not.

## What counts as in sight

- The eye is the avatar's position raised to its eye height: 0.087 m for both avatars. The companion
  shrank to the player's 10 cm body (radius 0.02 m, eye 0.087 m, reach 0.15 m;
  `WorldScaleProfile.SmallPlayer`); until 6 October 2026 it was 0.24 m tall with its eye at 0.205 m.
- Each candidate entity is sampled at 15 points on its bounding box: the centre, the eight corners and
  the six face centres, each pulled 1 cm (or a quarter of the box, whichever is smaller) inside.
- A sample is seen when the segment from the eye to it crosses no occluder. Occluders are the room
  shell (floor, walls, ceiling, as their polygons' boxes, padded to 1 mm where flat) and every object
  and creation box, except the entity itself and any box that contains the eye.
- The entity is perceived when at least one sample is seen.
- Always perceived: the companion's own avatar, whatever it holds, and the room shell it stands in.
- Avatars and effects never occlude.

## Where the rule applies

| Surface | Out of sight means |
|---|---|
| `observe` | Not in `visible`, and its signs are not in `texts`. Also bounded by the radius (default and maximum 20 m). Things it remembers are listed apart, under `remembered` (below). |
| `entities.list` | Not listed as seen now, whatever the filter (`near` included). A remembered thing is listed with `seen: "remembered"`. |
| `entity.inspect` | A remembered thing answers from memory. Anything never seen (or seen gone) is `target_not_found`, byte-identical to inspecting an id that does not exist. |
| `room.describe` | Not counted. A count of everything would say how many things are hidden; the counts are what is in sight now. |
| Every command that names an entity (`target`, `targets`, placement `on`, effect `targets`, `expected_entities`) | `target_not_found`, the same answer as for an unknown, removed or other-room id. Nothing changes and nothing is held. The one exception is a goal that only moves or turns the companion (below). |
| `actor` on any op | The companion's own avatar only. The player's avatar, or any other, is `actor_denied`, at the adapter and again at the host. |

The player's own controls and UI see the whole room.

## Perception memory

**What is remembered.** Each companion has its own memory. On every request from that companion the
host takes its avatar's line of sight and remembers each entity in it (objects, creations, other
avatars and effects; the shell is always in sight): its summary as seen (sanitised name, position,
bounds, revision and the rest), its parts and who protected it, the time and the room revision. That
is the only thing that fills memory. The player's queries, the player looking through the companion's
eyes, another companion's sight and the true room state never do. Asking what an avatar perceives
(`perceived`) remembers nothing.

**How results say so.** A remembered entity is its summary as last seen, plus:

| Field | Meaning |
|---|---|
| `seen` | `"remembered"`. Things in sight now carry `"now"` in a companion's results; the player's results leave the field out. |
| `last_seen_ago_s` | Seconds since the companion last saw it. |
| `last_seen_revision` | The room revision when it last saw it. |
| `may_be_stale` | `true` once the memory is 60 s old (`perception_memory_stale_after_s`) or the entity has changed in any way since: moved, edited, renamed, picked up, locked or gone. |

`entities.list` and `entity.inspect` answer from memory for anything out of sight, and filters apply to
what was seen, never to the true state. `observe` keeps `visible` for what is in sight now and adds
`remembered`: things seen earlier this session, out of sight now, within the radius of where they were
last seen, nearest first, at most 50. Signs are read only while in sight. "Where's the mug?" is
`entities_list` (or `observe`'s `remembered`) and the model reads the names; the game's fixed command
grammar (LIVE-VOICE.md) answers it from the same perceived-and-remembered set.

**What the flag reveals.** `may_be_stale` is one bit, and only one: old, or changed in some way. It is
the same bit for every kind of change, so it never says what happened or what the thing is like now.
Once raised it stays raised until the companion sees the thing again, so a thing moved away and back
does not reveal the round trip. A thing removed out of sight stays remembered exactly as it was
(flagged) until the companion looks at its place again. The room revision in every result already says
that something changed somewhere; the flag narrows that to "this thing may differ", which is what the
founder asked for.

**Looking again.** When the place a remembered thing was last seen is in sight and the thing is not
there (moved or removed), the companion has seen that it is gone from there: the memory is dropped,
and the thing answers like any unseen id. If it is in sight somewhere else, it is simply seen now.

**Commands on remembered things: non-destructive goals only.** `goal.set` with `go_to`, `look_at`,
`point_at`, `come` or `fetch` may name a remembered entity. The host judges it on the memory alone
(kind, movability, the lock it saw, the asset's mass), so the answer cannot reveal what became of it.
The result says `target_seen: "remembered"`, `last_seen_ago_s` and `may_be_stale`, and carries a
`job_id`. When the avatar arrives (the game's goal runner; `MockHost.goal_arrived` in the mock), the
host re-checks from where the avatar is now and finishes the job honestly:

| On arrival | Job |
|---|---|
| In sight, within reach (0.15 m) of where the goal aimed | `succeeded` |
| In sight, but moved further | `failed`, `revision_conflict` (the companion now sees where it is) |
| Not in sight: moved out of sight, or gone | `failed`, `target_not_found`, the same answer for both |

`jobs_status` reports the state and, on failure, the failure result. Jobs belong to whoever set the
goal; another principal's job id looks unknown. A new goal or a stop cancels the running job. The mock
models only the arrival check; carrying a fetched thing back is the Run 2 goal runner's.

`follow`, `stay` and `wander` with a target, and every command that changes an entity (grab, place,
place on, set part, remove, transform, revise, activate, lock, effect targets, release on) still need it
in sight now, and so does every id in `expected_entities`. The provenance rule (LIVE-VOICE.md) stands:
a destructive target must trace to the player's words or pointing, never to a sign or a memory.

**Bounds.** At most 256 entities per companion (`perception_memory_entries`; 0 turns memory off). The
least recently seen are forgotten first; among things seen at the same moment the nearest are kept.
Memory is cleared when a link session starts or ends (`LinkServer` tells the host, see TRANSPORT.md)
and when the room changes (`load_room`); entries from another room are never used. It lives only in
the host's memory: never in a snapshot, a receipt, room state or a save.

**Today's contract.** The result fields above are a proposed contract change
(`proposals/contracts-run1.diff`). Until it is applied the host emits no memory fields: queries stay in
sight only, while goals may already aim at remembered things (command `data` is free-form). The tests
run every memory path on a copy of `contracts/` with the proposed fields merged
(`companion/tests/fixtures/contract_memory_v1.json`), and check that the fields match once applied.

## What the companion still learns

These are outside the rule on purpose. Each is listed so nobody mistakes it for a leak.

- **The room revision** is in every result (the contract requires it). It tells the companion that
  something changed, not what or where.
- **Room metadata**: the room's name, bounds and style.
- **Its own history**: its receipts (`receipt.lookup`), its held commands (`approval.status`), its jobs
  (`jobs.status`) and the `affected` ids of its own commands. A companion's `room.undo` steps back only
  over its own changes (SECURITY.md), so its result names only things the companion itself changed.
- **Its memory**, as above, with the one-bit staleness flag.
- **Follow and come** keep working when the player is out of sight (see below). The result says only
  that the goal was set; the game moves the avatar, and nothing about the player's position comes back.

## The four choices

Each is one policy flag in `HostPolicy`.

| Question | Answer | Flag |
|---|---|---|
| May "follow me" and "come here" work when the player is out of the companion's sight? | Yes (recommended, unchanged): a companion that loses sight of the player would otherwise stop following at the first corner. | `follow_player_out_of_sight` |
| Does the companion remember what it saw? | **Yes** (founder, 6 October 2026), as above: bounded, per session and room, never saved, one-bit staleness. | `perception_memory_entries`, `perception_memory_stale_after_s` |
| May commands name things out of sight? | **Only non-destructive goals, at remembered things** (founder, 6 October 2026); everything that changes a thing needs it in sight now. Turning the flag off lets commands name anything room-wide (queries stay limited). | `companion_targets_need_perception` |
| May companion effects target the player's avatar? | Yes, within the effect bounds (recommended, unchanged): no reason found to change it. Memory does not widen it: an effect target must be in sight now. | `companion_effects_may_target_player` |
