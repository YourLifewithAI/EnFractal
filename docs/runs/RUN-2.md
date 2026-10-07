# Run 2: five real objects (draft plan, for the founder's approval)

**Status:** draft, 7 October 2026. Nothing starts until the founder answers "Decisions before Run 2 starts" below.

**Goal:** five garage objects stand in the styled test room with collision. The player picks one up, carries it and puts it down. The companion fetches one, through the real game host instead of the mock. The room rebuilds from data. This is the roadmap's Run 2 ([ROOM-SCALE-DIRECTION.md](../ROOM-SCALE-DIRECTION.md#the-runs)), and the founder kept its spine on 7 October: real objects first, with building after the sandbox verbs and the real host.

Read first: [AGENTS.md](../../AGENTS.md), [ORCHESTRATION.md](ORCHESTRATION.md), [OWNERSHIP.md](OWNERSHIP.md), [the Run 1 report](RUN-1-REPORT.md).

## Decisions before Run 2 starts (the founder)

1. **Which five objects.**
   - The pilot wants plain household items that differ in shape: a box, a bin, a toolbox, something with a thin handle, and one shelf unit or part of one.
   - Lane C proposes five from the garage set; the founder picks.
2. **Up to $5 for hosted image-to-3D, and uploading those objects' photos to a provider.**
   - Codex's survey ([brief 03](../codex/reports/03-image-to-3d-survey.md)) recommends piloting three generators on the same five objects:
     - **SPAR3D** locally (about 7 GB in its low-memory mode);
     - **TRELLIS.2** on fal.ai (it needs 24 GB, more than the RTX 2070 SUPER has);
     - **SAM 3D Objects** on fal.ai (it needs 32 GB).
   - The first pass costs about $1.60.
   - Uploading means cropped photos of the garage objects leave the machine for fal.ai. That needs the founder's yes.
   - The alternative is local-only (SPAR3D, with TripoSR as a fallback). It costs nothing, but tests fewer kinds of generator.
3. **Stability's Community License** for SPAR3D: free below $1M a year in revenue, with registration for commercial use. Or skip SPAR3D and use TripoSR (MIT) locally.
4. **The journal's data work: in Run 2, or wait for Run 3?**
   - That means the map store, the journal writer, `journal.read` and the contract ([JOURNAL.md](../companion/JOURNAL.md)).
   - The integrator recommends Run 3, beside its notepad UI, so Run 2 stays on its spine.

## Lanes

Four lanes, as in Run 1. Each builder agent owns its files ([OWNERSHIP.md](OWNERSHIP.md)) and reports in about 250 words. Codex runs alongside as the integrator's assistant (below).

### Lane C: Capture (C0, then C3, C4, C5)

- **C0 pilot.** The five objects, by hand, through the full chain: generation, then fitting to the measured size, then collision, mass and affordances, then the asset contract, then Godot import.
  - Run the three generators from decision 2 on the same crops, recording time, peak VRAM and repair time for each.
  - The founder judges the results side by side. The pilot decides the backend and settings.
- **C3 shell.** Floor, walls, ceiling and openings as planes from the garage poses, including `shell.openings`, which the builder now cuts as real holes, and a `site`.
  - Run 2 needs only the planes and openings. The full garage export is Run 3 (C6).
- **C4 inventory**, scoped to the five objects:
  - detection and segmentation across views;
  - 3D boxes and poses in room coordinates;
  - an annotated top-down review image (kept local).
- **C5 assets.** Generation behind one interface (local, or fal.ai within the cap):
  - fitting to the measured box;
  - collision (convex hull, or a decomposition for the shelf);
  - mass and affordances;
  - the five `asset.json` files, validated.
- **Acceptance:** the five assets validate with `contracts/validate.py`, load in the test room at the right size, and the founder judges them recognisable.
- **Photos and derived data stay local** (`captures/`, `user://rooms/`). Only code, tests and synthetic fixtures are committed.
- **Machine:** the garage photos are read in place from Google Drive. The first machine has Run 1's local captures and the 5.5 GB of weights.

### Lane P: Play (P3, P6)

- **P3 sandbox verbs**, all through the command path: pick up, carry, drop, push, place on a surface with snapping, and stack.
  - These are a new contract op set; the integrator applies the change.
  - The carry limit stays as now until modes are designed. The founder's rule for later is in RUN-1-STATUS.
  - Objects collide at 10 cm scale on Jolt.
- **P6 world as data.** The scene is rebuilt from room state: shell, objects with their current poses, creations, locks and avatars.
  - A test saves the state, rebuilds the room from nothing, and compares.
  - This answers the open "save migration between manifests" item too: the proposal in `command-host.md` becomes code.
- **Acceptance:**
  - the player picks up one of the five objects, carries it across the room and sets it on the box;
  - the rebuild test passes;
  - kernel and HUD suites stay green;
  - a founder playtest says carrying feels right.

### Lane A: AI companion (A2)

- **The swap.** The MCP server talks to the real game host instead of the mock.
  - The seams are in place from the P kernel round: `RunningGoal`, `ReportArrival` and `GoalFinished`.
  - The mock stays as the test double, and the alignment test keeps the two honest.
- **Embodiment:** follow, come, look, point and **fetch** as goals through the kernel, with the visible state (listening, planning, acting) on the avatar.
- **Opaque `job_id` handles:** random tokens, never counters, never shown to the player. This is a contract change.
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
1. contract changes first (sandbox verbs, opaque `job_id`);
2. then P;
3. then A, which needs P's verbs for fetch;
4. then C's assets;
5. then L.

Exit evidence:
1. The Windows runners and `tools/linux/test-all.sh` pass on the merged head.
2. The five assets validate, and the founder's verdict on them is recorded.
3. The carry playtest and the companion fetch, the fetch shown by a real-client transcript summary.
4. The room rebuild test.
5. Look captures of the five objects, and the founder's verdict.
6. `RUN-2-REPORT.md`.

**Design docs during Run 2,** written by the integrator with the founder:
- **The journal:** drafted ([JOURNAL.md](../companion/JOURNAL.md)).
- **Building:** answered ([BUILDING.md](../companion/BUILDING.md)).
- **Modes:** deferred to Run 3 or 4.

**Proposed for later, not decided:**
- **Run 3, "build with your companion":** the Victorian kit, the journal and minimap, selective memory, felt avatars and "Connect your AI".
- **Run 4, the whole garage.**
- Voice waits until the baseline game works.
