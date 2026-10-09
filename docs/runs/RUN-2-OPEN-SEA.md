# Run 2: the open sea, diving and things to touch (planned 9 October, midday)

**Read with** [RUN-2-ISLAND.md](RUN-2-ISLAND.md), [RUN-2-STATUS.md](RUN-2-STATUS.md) and [AGENTS.md](../../AGENTS.md). This is the round's plan after the founder's playtest of the island garage on DiamondAge.

## What the founder found (9 October, the island garage)

- **The edge is still a wall.** "If you swim past or up to the invisible wall, the avatar is teleported back somewhere else." The founder thought the edge was designed well, "but the kids want to be able to swim forever."
  - **Their daughter** "wants to be able to swim on forever, though she doesn't necessarily care if she reaches anything."
  - **Their son** "wants to be able to dive into the water and/or swim down under the surface." The founder suggests a key to start swimming underwater, then swimming the way you look.
- **Climbing works a lot better.**
- **The Gubble vibrates while the player climbs.** In the founder's video (4 s, the player on a conifer), the HUD flips between "the Gubble: follow" and "follow · floating there" every few frames, and the companion shakes beside the player. Floating off to the side is fine for the Gubble; the shaking is not.
- **The blur hides too much.** "Players are going to want to see the landscape a little further out." Blur only what is far in F1 (eye), F2 (shoulder) and F3 (diorama). Keep the tilt-shift in F4, "since that's where we're trying to see the charming/macro-scene view anyway".
- **More things to touch, and a sign of what can be touched.** The woodpile is the founder's example. "Eventually almost everything in the game world will be something we can interact with." The founder's own idea: the thing the avatar faces and can use gets an indicator (a soft aura, an outline, or a descriptor box; the top right is kept for a minimap).

## Decisions

- **The sea has no edge.** Past the reef the swimmer swims on for as long as they like. There is no current that turns them back and no teleport. The reef still marks the island's own waters.
  - **Going home is the player's choice:** a key (suggested **B, back to the beach**) does the wash-ashore fade onto the nearest beach. The HUD shows it only out at sea.
  - **The Gubble comes along** over the open sea.
  - **The cost** (the integrator's answer to the founder): an endless sea costs the same wherever the swimmer is, because the water surface follows the camera, fish are kept near the swimmer, and nothing new loads. Memory stays flat. The only real limit is numeric: positions are 32-bit floats, so they get coarse kilometres out. At the swim speed (0.19 m/s, 0.32 with Shift), 1 km is about 50 minutes of fast swimming without a break. Lane P measures where motion stops looking smooth, then either re-centres the world quietly or puts a last safety net beyond that point, far past where any child swims.
- **Diving:** a key dives (suggested: **hold Ctrl to dive, Space to rise**). Under water, W swims the way you look in F1 and F2. There is no breath limit and no harm, since the island's rule is no punishment. Let go and you drift slowly up, so nobody gets stuck on the bottom. The founder tunes all of this by playing.
- **Things to touch: one focus, not a glow on everything.** When almost everything can be used, a glow on everything means nothing. It would also turn the world into a menu. So:
  - **the focus:** the one thing the next key press would act on (`SandboxControls.ThingAhead` already chooses it) gets a soft outline or rim light, faint enough to keep the scene painterly but clear on grass, sand and rock;
  - **a small tag beside it** names the keys that work there ("F pick up · V push"), or the honest reason ("too heavy"). It sits by the thing, where the player is already looking, never in the top-right corner, which stays free for the minimap;
  - **things that can't be used stay plain.** That tells the player something too;
  - **later:** ask the Gubble to show what is near (the Point command's spirit). It sparkles over usable things within a few metres for a moment. This is how a player finds the woodpile from across the yard.
- **The woodpile becomes logs you can lift,** carry and stack. Other small things follow where a 10 cm person could plausibly lift or push them. A cap keeps the count within the frame budget.

## Packets

1. **Lane P (Opus), part 1, on `run2/play`.** The branch starts with Lane C's fix round restored (the integrator reverted `f29854a` there).
   1. **The Gubble stops shaking while the player climbs.** Show it failing first: count float starts or mode flips per second with a climbing player.
   2. **The climb sampler skips faces below sea level,** and finds out why the cliff at (-2.39, 0.068, 1.07) is grabbed but not topped. If the cause is the land's shape, send Lane C an exact change request. Goal: the landscape check passes on Lane C's reshaped garage.
   3. **The open sea:**
      - the current and the push-on teleport go;
      - water past the island answers from `RoomSea` (below sea level, outside the coast) with an open-sea bed, wherever the meshes stop;
      - the bounds no longer hold a swimmer on the sea side;
      - B (or another free key, reported) goes back to the beach;
      - the Gubble follows anywhere at sea;
      - Lane P measures float precision far out and chooses re-centring or a far net.
   4. **Report, then part 2 (resumed after the merge): diving.** As above: the dive key, swimming the way you look, rising, the eye and camera arms under water, the bed, no breath limit. Fish still flee a diver, never the Gubble.
   5. **Part 3, later: the focus wiring.** `ThingAhead` and `SupportAhead` drive Lane L's highlight and the tag beside it, in every view.
2. **Lane L (Opus, the GPU), on `run2/look`:**
   1. **The views' blur:**
      - F1, F2 and F3 keep the island crisp and soften only what is far: the far shore, the distant islands, the horizon;
      - F4 and O (observe) keep the tilt-shift;
      - per-view settings go in the preset's `x_look_*` extensions (no contract change);
      - the founder judges by eye.
   2. **The endless sea's surface,** to the horizon from wherever the swimmer is (it follows the camera), with the open-sea bed beyond the generated floor. This must land with Lane P's open sea, or water past the meshes is invisible.
   3. **The focus highlight** as a Look API (for example `LookDirector.SetFocus(Node3D? target)`): soft, painterly, readable on every ground. Show it in captures on grass, sand, rock and wood.
   4. **Report, then (resumed):** the sea's look from RUN-2-ISLAND.md (colour and depth, foam on the reef, waves at the beaches, the horizon and distant islands, fish in deeper water near the swimmer), and the view under water for diving (tint, haze, the surface seen from below).
3. **Codex brief 22 (GPT-6 Astra): the characters' second round.** The family's notes, on all five drawings (see [the brief](../codex/briefs/22-characters-round-2.md)).
4. **Codex brief 23 (GPT-6.1 Sol): things you can pick up.** In the exporter, woodpiles become logs, and other small things become movable where that is plausible (see [the brief](../codex/briefs/23-loose-things.md)).
5. **After Lane P's part 1 merges: Lane C** (the last five corpus rooms, the ragged coast). The open sea needs nothing from the generator, but the generator may later mark more loose things for brief 23's rules.
6. **Codex second-opinion reviews** of Lane P's open sea and diving (Sol against Astra, continuing the A/B), once part 1 reports.

## Principles (not a checklist)

- **No wall a child meets.** Anything a child would really do (swim out for an hour, dive to the bottom, swim under the jetty) just works.
- **Home is always one key away,** and the Gubble is always with you.
- **The island stays the centre.** From near it, the reef, the beaches and the island read clearly. Far out, it fades into the haze.
- **Signs, not clutter.** One focus at a time. The scene stays painterly.
