# Run 2, revised for the landscape (proposal)

**Status:** drafted by the integrator on 8 October 2026; **not yet shown to the founder.** Show it after the founder picks a landscape design draft (the first build step depends on the pick), get approval, then fold it into [RUN-2.md](RUN-2.md). Lane P's P3, P6, map store, journal writer and host fetch and Lane A's journal tools and real-client fetch are already done; see [RUN-2-STATUS.md](RUN-2-STATUS.md).

## Goal (replaces)
The garage, scanned from the founder's photos, becomes a believable landscape grounded in its layout, playable at 10 cm: the land from the room's shell and whole inventory, a populated layer of things to find, carry and use, the companion fetching one through the real host, the room rebuilt from data, the journal's data layer. Tested on the 24 synthetic corpus rooms as well as the garage.

## Lane C → the landscape generator (C5 replaced; C3, C4 done)
- C5: `pipeline/landscape/` turns the shell and every inventory entry into land, per ROOM-TO-LANDSCAPE.md: uplift from volume (kind for character, shape fallback, colour for rock), erosion over the whole field, drainage, ecology, people, then the populated layer. Deterministic. Exports terrain and populated assets and a room manifest that validates; collision from the same surfaces.
- **First step: an A/B on the look.** The same brief to a Claude agent and to Codex (GPT-6 Astra): the design's first build step for the corpus's synthetic garage, rendered with one shared harness (Blender 5.2.2 headless, CPU; the same cameras: overview and 10 cm eye; the same render settings), so the generator is judged, not the renderer. The founder judges blind. The winner's generator continues; the other's best ideas are folded in.
- Then the other corpus rooms, then the real garage (Lane C, locally, on the machine with the data).
- C7 (guidance) moves to Run 3, written from the generator's use. The recipe library stays as machinery.
- Acceptance: the garage loads in the game as a landscape from data; the founder judges it believable and recognisable; the corpus passes the checks (broken principles, broken play, determinism).

## Lane L → the landscape in the game (v2)
- Sky, horizon and living ground; the geography era's painterly ground shader; the light decided with the founder; brief 07's focus diffs (A1–A3, C1) and the frame budget.
- Starts when the generator's first terrain loads in the game.
- Acceptance: before and after captures within the capture budget, frame time, the founder's verdict.

## Lane P (done this run: P3, P6, the map store and journal writer, host fetch)
- Next: play on generated terrain: collision and navigation for both bodies on the land; the populated layer's things through the sandbox verbs; reach and slope numbers checked on real terrain.

## Lane A (unchanged)
- Fetch through the real host from a real client; `journal.read`, `journal.note`, `map.find` and the journal resource; mock parity (shared sight, eviction).

## Exit evidence (revised)
1. Windows runners and `tools/linux/test-all.sh` pass.
2. The garage loads as a landscape from data; the founder's verdict on it and on the corpus contact sheet.
3. The carry playtest and a companion fetch on the land, the fetch shown by a real-client transcript.
4. The rebuild test with the journal and the discovered map (done in P6).
5. The journal's boundary tests and a real client reading the journal after a fetch.
6. Look captures of the landscape in the game, and the founder's verdict.
7. RUN-2-REPORT.md.
