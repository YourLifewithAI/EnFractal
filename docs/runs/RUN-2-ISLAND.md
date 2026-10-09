# Run 2: the island in an endless sea (planned 9 October, early morning)

**Read with** [ROOM-TO-LANDSCAPE.md](../ROOM-TO-LANDSCAPE.md) ("The island and the sea", the founder's decision of 9 October), [RUN-2-STATUS.md](RUN-2-STATUS.md) and [AGENTS.md](../../AGENTS.md). This is the round's plan for the next session to dispatch. Nothing here has been started.

## What the founder asked for

"My daughter hates the invisible wall." The founder chose: "Let's lean into the island mechanic."
- **Every room becomes an island in an endless, interconnected sea.**
- **The door becomes a jetty or a harbour.**
- **The coast is a combination of beaches and cliffs, but ragged as well.**
- **The edge at sea:** a reef marks it, a current turns a swimmer back, and anyone who keeps going washes up on the nearest beach. The founder accepted this together with the island.

## How the pieces depend on each other

1. **Lane C (the generator) first.**
   - **The coast:** the shell ring becomes coast. Cliffs and headlands go where landforms meet the wall; coves, beaches and sea stacks go between them. Rivers run to the sea.
   - **The door** becomes a jetty or a harbour.
   - **The sea:** at a sea level below the land, out to the horizon, with distant islands where the far hills were.
   - **The reef:** an offshore ring of rocks and breakers, at a short swim's distance (start at about 30 to 60 cm off the coast at 10 cm scale).
   - **The package:** it needs the sea, the reef and the playable sea area. Describe them in the harness's package as the `x_generator` extension first, or propose a package field.
   - **The corpus:** all 24 rooms become islands. The checks gain "a beach or shelving shore to walk out of the sea" and keep "promised destinations reachable on foot".
2. **The exporter (Codex, a brief 21) and a contract change (the integrator):**
   - the room's bounds grow to take in the sea out to the reef and a little past it;
   - the sea is still water (layer 4 water, so `RoomWater` and the fish work there);
   - the reef's outline and the beaches go to the game. **Decide:** a contract field (a room `boundary` of kind `sea`, with the reef polygon and wash-ashore points), or an `x_landscape_sea` extension first.

   The integrator writes the contract change with examples and tests before Lane P starts the edge.
3. **Lane P (the edge, then part 2):**
   - **The sea edge:** past the reef, a current gently turns the swimmer back toward the island. Push on, and the swimmer washes up on the nearest beach (a fade, then standing on the sand). Nothing falls off the world.
   - **The invisible bounds clamp goes.** It stays only as the last safety net, far out. With no wall to climb, raise or drop the bounds' top over land.
   - **Then part 2 from RUN-2-CLIMB-SWIM.md:**
     - the Gubble floats, and as a ghost only the player can see, animals ignore it;
     - the backlog;
     - a collision layer 5 "hidden" for `drawn: false` parts, so sight and placement rays skip them (Lane P's request; the integrator names the layer in `project.godot`).
4. **Lane L (the sea's look):**
   - the open sea's colour and depth;
   - foam and breakers on the reef, and gentle waves at the beaches;
   - the horizon where sea meets sky, and the distant islands under the haze;
   - fish in the sea's deeper water where it suits (the existing `Look/Fauna` rules).
   - Within the frame budget.

## Principles (not a checklist)

- **Clear and believable:** from anywhere on the island, the player can see where the world ends and why.
- **Recognisable:** the island's outline is the room's floor plan, and the landforms are the furniture. The walls no longer read as mountains; the founder accepted that.
- **The coast:** cliffs where hard rock and landforms meet the sea, beaches where soft ground slopes into it. Ragged, never a smooth ring.
- **No punishment at the edge:** washing ashore is gentle. The Gubble floats over the sea and is never lost.
- **Later, not now:** sailing between islands (a house as an archipelago, friends' islands with multiplayer).

## Order and budget

1. Lane C, about 90 minutes, then the merge.
2. Codex brief 21 and the contract change, in parallel with Lane L's sea look.
3. Lane P's edge.
4. The founder's playtest.

Lane P's part 2 can run alongside Lane C, because it touches different files.
