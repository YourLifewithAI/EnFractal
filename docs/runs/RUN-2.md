# Run 2: five real objects (draft plan, for the founder's approval)

**Status:** draft, 7 October 2026. The founder answered its questions late on 7 October (below). It starts when the founder approves this plan; one question remains open (SPAR3D or TripoSR).

**Goal:** five real household objects stand in the styled test room with collision. The player picks one up, carries it and puts it down. The companion fetches one, through the real game host instead of the mock. The room rebuilds from data. The journal's data layer exists. This is the roadmap's Run 2 ([ROOM-SCALE-DIRECTION.md](../ROOM-SCALE-DIRECTION.md#the-runs)), and the founder kept its spine on 7 October: real objects first, with building after the sandbox verbs and the real host.

Read first: [AGENTS.md](../../AGENTS.md), [ORCHESTRATION.md](ORCHESTRATION.md), [OWNERSHIP.md](OWNERSHIP.md), [the Run 1 report](RUN-1-REPORT.md).

## The founder's decisions (late on 7 October)

1. **The five objects:**
   - a cardboard box;
   - a couch with wooden legs;
   - a gaming laptop;
   - an empty Bonne Maman jam jar;
   - a metal French press.

   They are household objects, not garage objects.
2. **No online spending on 3D generation.**
   - Everything runs on the founder's machine: Blender, which is installed, and local models. Downloading free software is fine.
   - Hosted generators are out: TRELLIS.2 and SAM 3D Objects on fal.ai, and any paid service.
3. **The principle behind it: the player's own AI makes the objects.**
   - It uses software on the player's computer, or its own connectors and tools, guided by the game's MCP.
   - The game never requires a hosted service.
   - So the capture guidance (C7: MCP tools and a skill) is the product, and Run 2's pilot is run by an AI following written guidance, as a player's AI would.
4. **The journal's data work is in Run 2** (see Lanes P and A).
5. **Still open: SPAR3D (Stability Community License) or TripoSR (MIT)** for local image-to-3D.
   - Both are free for EnFractal now. Stability's license asks for free registration for commercial use, a "Powered by Stability AI" credit, and no training of other models on its outputs. It ends above $1M a year in revenue unless a paid licence is agreed.
   - TripoSR is MIT with no strings, but older and rougher.
   - The integrator proposes trying both in the pilot. Neither is bundled with the game: if the guidance recommends one, the player's AI downloads it on the player's machine.

**Photos needed (the founder):** for each object, 8 to 12 photos taken all round it on the 1x back camera, plus one tape measurement (its height or width). They go in Google Drive under `Enfractal/Photos for space generation/Objects/<object>/`, beside the garage set. The Drive folder stays their only home, as with the garage.

## Lanes

Four lanes, as in Run 1. Each builder agent owns its files ([OWNERSHIP.md](OWNERSHIP.md)) and reports in about 250 words. Codex runs alongside as the integrator's assistant (below).

### Lane C: Capture (C0, C5, and the start of C7)

- **C0 pilot, local only.** The five objects, from photos and measurements to assets in the room. There are two routes, compared object by object:
  - **an AI-built model in Blender:** the AI writes a parametric Blender Python script from the photos and measurements, run headless (`blender --background`). This suits boxes, jars, laptops and presses, and handles glass and metal, which defeat image-to-3D. [Codex brief 09](../codex/briefs/09-blender-spike.md) tests it on the box and the jar first;
  - **local image-to-3D:** TripoSR, and SPAR3D if the founder agrees, on the RTX 2070 SUPER (SPAR3D's low-memory mode needs about 7 GB of the 8). Best for soft or irregular shapes, such as the couch's cushions.

  Each result then goes through the same chain: fitted to the measured size, collision, mass and affordances, the asset contract, and Godot import. The founder judges the results side by side.
- **C5 assets.** Generation behind one interface (Blender script or local model), plus:
  - fitting to the measured box;
  - collision (a convex hull, or a decomposition for the couch);
  - mass and affordances;
  - the five `asset.json` files, validated.
- **C7, started: the guidance an AI follows.** The steps, the measurements to take, the checks before export, and which route suits which object, written as a skill and sketched as MCP tools. The pilot is run from this guidance, so it is tested by use. **Codex runs the Blender route as the stand-in for a player's AI.**
- **Moved to Run 3, with the garage:** C3 (the garage shell) and C4 (finding objects in room photos). The five objects are photographed one by one, so Run 2 needs only to cut each object out of its own photos.
- **Acceptance:**
  - the five assets validate with `contracts/validate.py`;
  - they load in the test room at the right size;
  - the founder judges them recognisable and in keeping;
  - an AI that had not seen the pilot can follow the written guidance for one object.
- **Photos and derived data stay local** (`captures/`, `user://rooms/`). Only code, tests, guidance and synthetic fixtures are committed.

### Lane P: Play (P3, P6, and the journal's map store)

- **P3 sandbox verbs**, all through the command path: pick up, carry, drop, push, place on a surface with snapping, and stack.
  - These are a new contract op set; the integrator applies the change.
  - The carry limit stays as now until modes are designed. The founder's rule for later is in RUN-1-STATUS.
  - Objects collide at 10 cm scale on Jolt.
- **P6 world as data.** The scene is rebuilt from room state: shell, objects with their current poses, creations, locks and avatars.
  - A test saves the state, rebuilds the room from nothing, and compares.
  - This answers the open "save migration between manifests" item too: the proposal in `command-host.md` becomes code.
- **The journal's data, the host side** ([JOURNAL.md](../companion/JOURNAL.md), "Work order"):
  - the map store replaces the host's perception memory. It is the team's knowledge (player and companion), saved with the room, holding the discovered space and its levels;
  - the journal writer records goals, completed actions and at whose direction, and how things were later changed. It does not record every step or sight;
  - room state gains `journal` and `discovered`.
- **Acceptance:**
  - the player picks up one of the five objects, carries it across the room and sets it on the box;
  - the rebuild test passes, including the journal and the discovered map;
  - kernel and HUD suites stay green;
  - a founder playtest says carrying feels right.

### Lane A: AI companion (A2, and the journal's queries)

- **The swap.** The MCP server talks to the real game host instead of the mock.
  - The seams are in place from the P kernel round: `RunningGoal`, `ReportArrival` and `GoalFinished`.
  - The mock stays as the test double, and the alignment test keeps the two honest.
- **Embodiment:** follow, come, look, point and **fetch** as goals through the kernel, with the visible state (listening, planning, acting) on the avatar.
- **Opaque `job_id` handles:** random tokens, never counters, never shown to the player. This is a contract change.
- **The journal's data, the AI side:** `journal.read`, `journal.note` (the companion's own notes), `map.find`, and the journal as an MCP resource. The mock matches the host, and tests check that:
  - the AI cannot write facts, only notes marked as its own;
  - names cannot forge journal entries;
  - nothing about undiscovered places or things leaks.
- **An easier connection** (from [brief 04](../codex/reports/04-byoai-connect-survey.md)):
  - the profile generator emits `SYSTEMROOT` on Windows, the cause of the known `WinError 10106`;
  - it writes per-client configs for Claude Code, Codex, VS Code, Continue, Hermes Agent and OpenClaw.
  - The in-game "Connect your AI" panel is UI, for Run 3.
- **Acceptance:**
  - a real MCP client asks the companion to fetch one of the five objects, and it does, through the real host;
  - the boundary tests pass against the real host;
  - `goal.stop` always works.

### Lane L: Look (L4, L6, and the v2 preset)

- **v2 preset.** `storybook_painterly` v1 is locked, so all tuning goes in `v2.json`:
  - the observe view's frame budget (its p95 is 17.3 ms against 16.7);
  - **the observe view's focus.** The founder's third playtest found nothing sharp in F3 or F4 with O on, and it should hold the player clearly in focus. Codex's [brief 07](../codex/briefs/07-observe-focus.md) diagnoses it first.
- **L4 handmade geometry:** bevels, a slight wobble and charm-keeping decimation for proxies and the five generated assets, so captured objects sit in the style.
- **L6 asset restyle,** scoped to the five assets: a per-asset texture pass for the preset, so they read as part of the painterly room rather than as photographs. The method (shader-side, or an offline image pass) is the lane's proposal; any hosted spend needs the founder's approval.
- **Acceptance:**
  - before and after captures of the five objects in the room, within the capture budget;
  - frame time within the v2 budget;
  - the founder's verdict.

## Codex in Run 2

Codex works as the integrator's assistant. The integrator writes the briefs and Codex runs them through [`tools/codex/run.ps1`](../../tools/codex/run.ps1), each in its own checkout and branch. The Windows sandbox lets it read anything and write only in that checkout. The integrator checks scope with `tools/codex/check_scope.py`, verifies the claims and merges.

They don't overlap: a Codex brief's scope never names a file a running lane owns.

Planned Codex work:
- **A second-opinion review** of each lane's branch before it merges, alongside the Opus whole-lane reviews (first trial: commit `729858a`, four real findings in about 100k tokens).
- **The Linux suite** on the merged head, in WSL or a Codex cloud task.
- **Research** (generator licences, client config formats) and docs upkeep.
- **Mechanical, well-scoped code,** such as the HUD change in brief 06.

The routing log records every Codex run, so later runs can move more work to it if it stays clean.

## Integration and exit

Merge order:
1. contract changes first: the sandbox verbs, opaque `job_id`, and the journal (`journal.read`, `journal.note`, `map.find`, and room state's `journal` and `discovered`);
2. then P;
3. then A, which needs P's verbs for fetch;
4. then C's assets;
5. then L.

Exit evidence:
1. The Windows runners and `tools/linux/test-all.sh` pass on the merged head.
2. The five assets validate, and the founder's verdict on them is recorded.
3. The carry playtest and the companion fetch, the fetch shown by a real-client transcript summary.
4. The room rebuild test, with the journal and the discovered map surviving it.
5. The journal's boundary tests (no forged facts, no leaks about undiscovered things), and a real client reading the journal after a fetch.
6. Look captures of the five objects, and the founder's verdict.
7. The capture guidance (C7's start), and one object built by an AI that followed it cold.
8. `RUN-2-REPORT.md`.

**Design docs during Run 2,** written by the integrator with the founder:
- **The journal:** drafted ([JOURNAL.md](../companion/JOURNAL.md)).
- **Building:** answered ([BUILDING.md](../companion/BUILDING.md)).
- **Modes:** deferred to Run 3 or 4.

**Proposed for later, not decided:**
- **Run 3, "build with your companion":** the Victorian kit, the journal's notepad and the minimap, felt avatars, and "Connect your AI"; plus C3 and C4 with the garage, if it comes before Run 4.
- **Run 4, the whole garage.**
- Voice waits until the baseline game works.
