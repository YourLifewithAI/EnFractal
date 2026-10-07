# What the companion perceives

**The founder's rule (6 October 2026):** the companion perceives anything within its avatar's line of
sight. The rule applies to every companion query and every command, not only `observe`. The companion
never perceives through the player's avatar.

The mock host implements the rule in `companion/src/enfractal_companion/perception.py` and
`mock_host.py`; `companion/tests/test_perception.py` pins it, and `test_no_holds.py` runs the same
tests again with no approval holds configured. In Run 2 the kernel host replaces the geometry with
physics ray casts against the real colliders. The rule and the tests carry over; the geometry does not.

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
| `observe` | Not in `visible`, and its signs are not in `texts`. Also bounded by the radius (default and maximum 20 m). |
| `entities.list` | Not listed, whatever the filter (`near` included). |
| `entity.inspect` | `target_not_found`, byte-identical to inspecting an id that does not exist. |
| `room.describe` | Not counted. A count of everything would say how many things are hidden. |
| Every command that names an entity (`target`, `targets`, placement `on`, effect `targets`, `expected_entities`) | `target_not_found`, the same answer as for an unknown, removed or other-room id. Nothing changes and nothing is held. |
| `actor` on any op | The companion's own avatar only. The player's avatar, or any other, is `actor_denied`, at the adapter and again at the host. |

Tests: `LineOfSight` and `EverySurface` in `test_perception.py`. `EverySurface` runs every query op in
the contract with the companion hidden behind the box and checks that no hidden id or sign text appears
anywhere in any result, and it tries every command that names an entity against hidden ones.

The player's own controls and UI see the whole room.

## What the companion still learns

These are outside the rule on purpose. Each is listed so nobody mistakes it for a leak.

- **The room revision** is in every result (the contract requires it). It tells the companion that
  something changed, not what or where.
- **Room metadata**: the room's name, bounds and style.
- **Its own history**: its receipts (`receipt.lookup`), its held commands (`approval.status`) and the
  `affected` ids of its own commands. A companion's `room.undo` steps back only over its own changes
  (SECURITY.md), so its result names only things the companion itself changed.
- **Follow and come** keep working when the player is out of sight (see below). The result says only
  that the goal was set; the game moves the avatar, and nothing about the player's position comes back.

## Open questions for the founder

Each is one policy flag in `HostPolicy`; the defaults are the recommendations.

| Question | Default | Flag |
|---|---|---|
| May "follow me" and "come here" work when the player is out of the companion's sight? | Yes: a companion that loses sight of the player would otherwise stop following at the first corner. | `follow_player_out_of_sight` |
| Does the companion remember what it saw, for naming in commands? | No memory. With memory, recently seen things can be named in commands for N seconds but never shown in queries. | `perception_memory_s` |
| May commands name things out of sight at all? | No. Queries stay limited either way. | `companion_targets_need_perception` |
| May companion effects target the player's avatar? | Yes, within the effect bounds. | `companion_effects_may_target_player` |
