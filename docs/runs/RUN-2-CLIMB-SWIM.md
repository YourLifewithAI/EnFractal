# Run 2: climbing, swimming and fish (the founder's playtest, 8 October, late night)

**Read with** [RUN-2-STATUS.md](RUN-2-STATUS.md), [AGENTS.md](../../AGENTS.md) and [ROOM-TO-LANDSCAPE.md](../ROOM-TO-LANDSCAPE.md). This page is the round's brief for Lanes P, C and L and for Codex brief 19.

## What the founder asked for

From a playtest of the garage landscape:

1. "Climbing cliffs and climbing trees."
2. "Can we have deeper ponds or water features? It should feel like you're swimming when you're in deep water (over the head of the avatar). Maybe the character goes sideways and they slow down a little bit."
3. "Can there be fish in the water?"

**The founder's answers:**
- **Climbing:** climb any steep face, with no stamina for now. Walk into a cliff, rock or tree trunk and you grab on and climb slowly. Jump lets go.
- **Swimming:** at the surface first, diving later. Over-the-head water makes you swim at the surface, the body tipped to lie along the water, a bit slower. Diving and an underwater view come in a second pass, once surface swimming feels right.
- **Fish:** living scenery. Small schools swim around the ponds and dart away when you come close. No catching yet. Bigger ponds get more fish.
- **The Gubble** floats over water and up cliffs. It never climbs or swims, which suits a ghost and keeps it from getting stuck behind the player.

ROOM-TO-LANDSCAPE.md already expected these mechanics: "Required journeys may depend on climbing mechanics, swimming across ponds and rivers [...] since those mechanics don't exist yet, please begin the first pass design around making everything accessible via low-incline pathways." **That first pass stands:** promised destinations stay reachable on foot by gentle paths. Climbing and swimming add freedom and fun on top; they are not yet required to reach anything.

## What exists today (surveyed 8 October)

- **No climbing or swimming of any kind.** `"climbable"` is a contract affordance nothing reads.
- **Water is paint.**
  - The generator's tarn is at most 5.6 cm deep (`generator/water.py`), and its streams 2.6 to 3.2 cm. The 10 cm avatar wades along the bed.
  - The exporter makes water a non-colliding `backdrop` with material `water`, and drops its level and extent.
  - The game draws it opaque and single-sided (`painterly_land.gdshader`), so from below the surface vanishes.
  - The game's navigation mesh is baked from colliders only, so the companion's routes run through ponds along their beds.
- **Trees:** trunks (bark cones) collide through `scatter_solid`. Leafy crowns are non-colliding backdrop. A trunk reaches about half to a third of the way up into its crown.
- **Cliffs:** the terrain is one colliding mesh per material role. About 37% of the garage's land inside the bounds is steeper than 45°, so it cannot be stood on today; under this round it can all be climbed.
- **No animals anywhere**, in the generator, the package or the game.

## Principles (inspiration, not a checklist)

Judge by feel and by what a player would notice, not by numbers. The numbers below are starting points to tune.

- **Climbing should feel deliberate, not sticky.** You grab a face because you pushed into it. Brushing past a slope, or walking into a step you can step up, never grabs. Climbing is slow and steady, and the top of a climb is a reward: you pull yourself over onto the ledge.
- **Water should feel like water.** The deeper you wade, the slower you go, until the water is over your head and you swim. Swimming is calmer and slower than walking. A fall into deep water is broken by it. You can always get out: walk up a shelving shore or climb a steep bank.
- **The land should look like it holds that water.** Ponds have shelving shores, drop-offs and a deep middle, not a round bowl. Water still runs downhill, and still water is level, held by its lowest rim.
- **Fish belong where fish would live:** deep, still water first, perhaps a few in river pools, none in a trickle. They behave like a school: loosely together, turning at the edges, scattering from a swimmer and settling back.
- **The Gubble is a ghost bubble, set apart from the world.** It drifts over water and rises up cliffs. It never climbs, swims or gets stuck.
  It is "like a ghost only the player can see" (the founder): animals never react to it.

## The shared convention: the water layer

`game/project.godot` now names the physics layers: 1 `world`, 2 `player`, 3 `companion`, 4 `water` (bit value 8).

- **Lane P** gives water surfaces query-only colliders on layer 4, with a small query other code can use (`RoomWater`, in `game/scripts/native/Room/`).
- **Nothing collides with layer 4:**
  - bodies' masks stay 1 (and 2 for the companion);
  - navigation bakes from the world layer only;
  - camera arms, sight rays, reach and drops keep mask 1.
- **Lane L does not depend on it this round.** It finds water from the built room's water shell parts, and depth by a downward ray to layer 1. Switching to `RoomWater` later is a small change.

## Lane P (Opus): climb and swim

**Part 1, the player (about 90 minutes):**

1. **Water to the game.** Water shell parts (material `water`) become query-only colliders on layer 4, through `RoomWater`, which answers "where is the water surface above or below this point" and "how deep is it here". Check that the navigation bake, the sight sweep, the camera arms, reach and drops all ignore it.
2. **Wading and swimming for the player:**
   - wading slows the body gradually with depth;
   - **in water over the head, the body swims at the surface.** The visible body tips to lie along the water: tilt `VisualRoot`, never the capsule or the eye camera. It moves slower than walking (start near 60% of walk), with a gentle bob, and the eye stays above the water;
   - swimming into a steep bank grabs it (climbing), and walking up a shelving shore ends the swim;
   - a fall into deep water lands in it and is not a fall recovery;
   - all three gravity modes work;
   - **decide and say why:** what jump does in the water, and whether you can swim while carrying something.
3. **Climbing for the player:**
   - any colliding surface too steep to stand on (over `FloorMaxAngle`, 45°) can be climbed. Push into it to grab; it never grabs by accident;
   - the move keys climb up, down and sideways along the face, and the body faces it. Start near 0.12 m/s, against a walk of 0.32;
   - jump lets go with a small kick away. A leap or fall into a steep face may grab it in mid-air (low gravity makes this lovely);
   - at the top, the body pulls itself over onto standable ground; at the bottom, it steps off into walking;
   - it works on cliffs, rock, tree trunks, cottage walls and the test room's furniture;
   - **decide and say why:** overhangs, climbing while carrying, the follow camera while climbing, and the bounds' top;
   - **trees:** Codex brief 19 gives each tree's crown a top you can stand on and a hidden climbing pole through the leaves. Make sure a climber goes up the pole through the leaves and comes out on top. The crown's collider is one-sided (`backface_collision` off); verify that under Jolt.
4. **Tests,** in the avatar suite (`SmallAvatarPhysicsTest`) with synthetic geometry:
   - a 70° face and a vertical face climbed and topped out;
   - letting go;
   - no grab from a walkable slope or a steppable ledge;
   - a pool: the body swims, is slower and stays at the surface, and climbs and walks out;
   - a fall into deep water;
   - a one-sided crown cap passed up through from inside.

   Then add a landscape check: the player climbs one cliff of the generated garage. The generated garage's water stays shallow until Lane C's deeper ponds merge, so use your own synthetic pool for swimming.

**Part 2, the Gubble and the follow-ups (about 60 minutes, after part 1 is merged):**

- **The Gubble floats:**
  - it hovers a little above the ground or the water, with a gentle bob;
  - when its walking route cannot reach (up a cliff, across water), it floats there, rising over obstacles rather than through them;
  - same capsule; fetch and carry still work while it floats;
  - `target_unreachable` remains for targets that really cannot be reached;
  - the host's behaviour changes, so **list what Lane A's mock must change to match** as a change request (`companion/**` is Lane A's).
- **The backlog:**
  - `go_to` aims at the nearest reachable side of a target;
  - choose the navigation cell size after measuring whether a re-bake stalls a frame;
  - **"the Gubble"** in the HUD's name tag and messages.

## Lane C (Opus): deeper water, then the backlog (about 90 minutes)

1. **Ponds deep enough to swim in:**
   - the deep middle of still water well over the avatar's 10 cm head (about 15 to 25 cm);
   - at least one gently shelving shore to wade in from and walk out of;
   - drop-offs that read believably;
   - deep pools in a river where it makes sense.

   The levels follow the existing principles: still water is level, held just under its lowest rim, and water runs downhill. **The garage keeps a swimmable pond:** it is the founder's playtest room.
2. **More variety between rooms** (from the backlog): not every room gets a tarn. Use a river with deep pools, a lake behind a scree dam, or a dry upland, as the room suggests.
3. **The two failing corpus rooms** (`bedroom_nominal` water, `workshop_nominal` walk).
4. **If time remains:** the founder's far scenery (trees, rock, water, perhaps a distant settlement on the far hills), and hills still too rounded up close.

Keep promised destinations reachable by gentle paths. Do not route required paths through water or up cliffs yet.

## Lane L (Opus): water you can see into, and fish (about 90 minutes)

1. **The water look:**
   - see-through with depth: clear shallows, and a deep middle that darkens and cools;
   - visible from below and from the side when a camera dips under;
   - painterly, as the bible asks;
   - within the frame budget.
2. **Fish, as living scenery:**
   - **Where:** small schools in still water deep enough for them (start near 6 cm or more), more in bigger ponds, perhaps a few in deep river pools, none in shallow brooks.
   - **What:** painterly little fish about 1.5 to 3 cm long, in a couple of colours. Perhaps an occasional larger one in a big pond.
   - **How they move:** in loose schools, turning at the edges, staying between the bed and the surface, darting away when an avatar comes close in or at the water, then settling back.
   - **Rules:** placement is deterministic from the room (seeded by its id and the water); the motion runs freely. Keep it cheap (a MultiMesh or a few nodes). Fish are not entities, not saved, and not in the kernel, sight or map.
   - **Code and wiring:** code in `game/scripts/native/Look/Fauna/**` (a new folder, Lane L's), plus shaders. Wiring into `RoomWorld.cs` is a change request to the integrator.
3. **GPU courtesy:** the founder may be playing. Capture only when no `EnFractal*` window is open, under the capture budget in ORCHESTRATION.md.

## Codex brief 19: trees you can climb

[docs/codex/briefs/19-climbable-trees.md](../codex/briefs/19-climbable-trees.md): the exporter gives each tree a hidden climbing pole through its crown and a top you can stand on, without snagging a walker under low branches.

## After the round (the integrator)

- **Merge** P part 1, Codex 19, C, then L. Run one full suite per batch.
- **Refresh the founder's installed landscape** (`%APPDATA%\Godot\app_userdata\EnFractal\rooms\landscape_garage_nominal`) from a fresh fixture.
- **Wire the fish** into `RoomWorld.cs`.
- **Ask the founder to playtest:** climbing, swimming and fish.
- **Then** P part 2 and Lane A's mock changes.
