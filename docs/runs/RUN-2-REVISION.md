# Run 2, revised for the landscape (proposal)

**Status:** **approved by the founder on 8 October 2026** ("I approve the plan"). Written by the integrator for the founder's design ([ROOM-TO-LANDSCAPE.md](../ROOM-TO-LANDSCAPE.md)). It supersedes [RUN-2.md](RUN-2.md)'s goal, its Lane C, Lane L and Codex sections, and its exit evidence; RUN-2.md's founder decisions on the toolchain (Blender first, TripoSR, nothing under Stability's licence, nothing online or paid) still stand. Already done this run: Lane P's P3, P6, the map store, the journal writer and fetch on the host; Lane A's journal tools and a real client's fetch; Lane L's observe focus (v2 draft). See [RUN-2-STATUS.md](RUN-2-STATUS.md).

## Goal

The garage, scanned from the founder's photos, becomes a believable landscape grounded in its layout and playable at 10 cm: land from the room's shell and whole inventory, a populated layer of things to find, carry and use, under a real sky. Tested on the 24 synthetic corpus rooms as well as the garage.

## The founder's design decisions this plan builds on

- **A real sky lights the land.** The scanned window's direction (and the latitude) tells where the sun rises; light no longer comes only through the real windows. This changes the look bible's rule for landscapes (Lane L updates the bible).
- **Mostly wild, with a few settlements.** Places where people live or lived, never people themselves.
- **Setup questions before generation:** the gameplay mode (which decides what gets populated), how much water should feature, and the light (rough latitude and longitude, season). The first build uses fixed answers; the questions become UI later.
- **Everything reachable by low-incline paths** in the first pass. Climbing, swimming, bridges, ladders and magic come later.

## Lane C → the landscape generator (C5 replaced; C3 and C4 done)

- **C5:** `pipeline/landscape/` turns the shell and every inventory entry into land, following ROOM-TO-LANDSCAPE.md: uplift from volume, erosion, water, ecology, settlements, paths, then the populated layer. Deterministic from the same inputs and setup answers. It exports terrain and populated assets and a room manifest that validates, with collision from the same surfaces.
- **First step: the design's first build, as an A/B.** One complete synthetic garage landscape: sky, horizon, connected ground, a ridge feeding a stream, planting and a small settlement with a reachable, carryable object; simple generation and finished painterly surfaces.
  - **The same brief to a Claude agent and to Codex (GPT-6 Astra).** Each writes a generator in its own folder under `pipeline/landscape/`.
  - **One shared render harness first,** so the generator is judged and not the renderer: Blender 5.2.2 headless (Codex can run it, not Godot), fixed cameras (an overview and the 10 cm eye), one sky and sun from the setup answers, the same render settings and a common output format both generators write. A small Codex brief builds the harness.
  - **The founder judges blind,** from above and at 10 cm: does it feel like land, or still a room with lumps? The winner's generator continues, with the other's best ideas folded in, and the A/B log records it.
- **Then** the other corpus rooms, then the real garage (Lane C, on the machine with the garage data).
- **C7 (the guidance) moves to Run 3,** written from the generator's use. The recipe library stays as machinery for buildings and props.
- **Acceptance:** the garage loads in the game as a landscape from data; the founder judges it believable and recognisable; the corpus passes the checks (contradictions such as uphill streams, unsupported buildings, blocked routes, unreachable pickups or lost landmarks; determinism).

## Lane L → the landscape in the game (v3, after v2's focus round)

- Sky and sun from the setup answers, the horizon, living ground, the geography era's painterly ground shader, and the look bible updated for sky light. The founder's v2 verdict (the smaller blur) first.
- Starts when the winning generator's first terrain loads in the game.
- **Acceptance:** before and after captures within the capture budget, frame time, the founder's verdict.

## Lane P → play on the land

- Collision and navigation for both bodies on generated terrain; the populated layer's things through the sandbox verbs; the low-incline paths checked with real movement and a carried item; reach and slope numbers on real terrain.

## Lane A

- Its Run 2 work is done. Next: the companion's fetch and journal on the land, once Lane P's terrain play lands.

## Exit evidence

1. The Windows runners and `tools/linux/test-all.sh` pass.
2. The garage loads as a landscape from data; the founder's verdict on it and on the corpus contact sheet.
3. The carry playtest (passed in the test room) and a companion fetch on the land, the fetch shown by a real-client transcript (done in the test room).
4. The rebuild test with the journal and the discovered map (done).
5. The journal's boundary tests and a real client reading the journal after a fetch (done).
6. Look captures of the landscape in the game, and the founder's verdict.
7. RUN-2-REPORT.md.
