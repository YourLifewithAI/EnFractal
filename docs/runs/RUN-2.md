# Run 2: five real objects (draft plan, for the founder's approval)

**Status:** **approved by the founder, late on 7 October 2026,** after the founder's corrections (below). **Revised for the landscape and approved on 8 October: [RUN-2-REVISION.md](RUN-2-REVISION.md) supersedes this page's goal, Lane C, Lane L and Codex sections and its exit evidence.** The live handoff is [RUN-2-STATUS.md](RUN-2-STATUS.md).

**Goal:** the garage, scanned from the founder's existing photos, becomes a generally faithful, stylised, playable space in the game: its shell and five of its objects as stand-ins at their scanned sizes and places. The player picks one up and carries it. The companion fetches one, through the real game host instead of the mock. The room rebuilds from data. The journal's data layer exists. This is the roadmap's Run 2 ([ROOM-SCALE-DIRECTION.md](../ROOM-SCALE-DIRECTION.md#the-runs)) with the garage's shell pulled forward from Run 3, because scanning a room into a playable space is the product. The test room stays as the regression room.

Read first: [AGENTS.md](../../AGENTS.md), [ORCHESTRATION.md](ORCHESTRATION.md), [OWNERSHIP.md](OWNERSHIP.md), [the Run 1 report](RUN-1-REPORT.md).

## The founder's decisions (late on 7 October)

1. **The objective: scan a room, get a generally faithful space, stylise it.**
   - Exact replicas of real objects are out of scope ("overkill and a different objective").
   - So there is **no photography of single objects.** Everything comes from the room scan: the garage set already in Drive.
   - An object is faithful in kind, size, place and broad colour, and stylised in its detail.
2. **The player's own AI makes the objects.**
   - It uses software on the player's computer, or its own connectors and tools, guided by the game's MCP.
   - No hosted or paid service is ever required, and Run 2 spends nothing online.
   - The capture guidance (C7: MCP tools and a skill) is therefore part of the product.
3. **The toolchain has no strings attached, so anyone can use it.**
   - **Blender first.** A Blender script per kind of object is a recipe (Codex built a box and a jar this way on 7 October: [brief 09](../codex/reports/09-blender-spike.md)).
   - **TripoSR (MIT) is the image-to-3D tool** for shapes a recipe cannot make well.
   - **Tools under Stability's Community License (SPAR3D, SF3D) are out.**
4. **The first recipes:** a cardboard box, a couch with wooden legs, a gaming laptop, an empty Bonne Maman jam jar and a metal French press. They are generic recipes, sized and coloured from whatever the scan finds; the garage's own contents decide which recipes come next.
5. **The journal's data work is in Run 2** (see Lanes P and A).

## Lanes

Four lanes, as in Run 1. Each builder agent owns its files ([OWNERSHIP.md](OWNERSHIP.md)) and reports in about 250 words. Codex runs alongside as the integrator's assistant (below).

### Lane C: Capture (C3, C4, C5, and the start of C7)

**The pipeline:** room photos, then the shell and an inventory of objects (kind, size, place, broad colour), then a stand-in for each object built by the AI from a recipe, then the room as data.

- **C3 shell.** The garage's floor, walls, ceiling and openings as planes, from the poses Run 1 already computed:
  - `shell.openings` included, which the builder now cuts as real holes;
  - a `site`, coarse as the contract requires;
  - the room at the tape-fitted scale (0.926);
  - exported as a room manifest that validates.
- **C4 inventory.** Find the garage's objects across its photos: each one's kind, a 3D box (size and place in room coordinates), and its broad colours. Pick five varied objects for Run 2. An annotated top-down review image stays local.
- **C5 stand-ins.** For each of the five:
  - choose a recipe, or TripoSR when no recipe fits;
  - build it at the scanned size, in the scanned colours;
  - then collision, mass and affordances, an `asset.json` that validates, and placement in the room manifest.
- **C7, started: the guidance an AI follows.**
  - The recipe library (the five founder-named recipes, plus any the garage needs), the order of steps, and the checks before export.
  - It is written as a skill and sketched as MCP tools.
  - **Codex builds the recipes and stand-ins as the stand-in for a player's AI,** working from this guidance, so the guidance is tested by use.
- **Acceptance:**
  - the garage loads in the game from data, at its fitted scale, with its openings;
  - five stand-ins stand at their scanned places, and the founder judges them generally faithful and charming;
  - an AI that had not seen the work can follow the guidance to add one more object.
- **Photos and derived data stay local** (`captures/`, `user://rooms/`). The founder's photos are read in place. Only code, recipes, guidance and synthetic fixtures are committed.
- **Machine:** the poses and Run 1's capture data are on the first machine (`captures/garage/`). Either work there, or rerun the poses here from the Drive photos (about 94 s of GPU).

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

**Who does what (decided by the integrator, 7 October).** The rule: Codex takes work that is bounded, checkable and owned by no running lane. Claude lanes keep the kernel, the command host, the companion's security boundary, the contracts and anything needing the GPU.

| Work | Who | Why |
|---|---|---|
| Contract changes: sandbox verbs, opaque `job_id`, `journal.read`, `journal.note`, `map.find`, room state's `journal` and `discovered` | **The integrator** | Contracts are the integration point |
| P3 sandbox verbs, P6 world as data, the map store and journal writer | **Claude, Lane P (Opus)** | Kernel and host: reviews keep finding majors here |
| A2 swap to the real host, fetch, the journal's queries, `job_id`, per-client profiles | **Claude, Lane A (Opus)** | The security boundary |
| C3 shell and C4 inventory from the garage poses | **Claude, Lane C (Sonnet)** | Needs the GPU and the local capture data |
| **The recipe library:** the five founder-named recipes, then one per kind of object C4 finds | **Codex** | Bounded and checkable, and it tests "the player's AI makes it". Brief 09 showed it works |
| **The stand-ins:** building the five garage objects from C4's inventory, with the recipes or TripoSR | **Codex,** with Lane C's fitting, collision and asset code | Codex plays the player's AI |
| **C7 guidance:** the skill text and the MCP tool sketch, written from what Codex did | **Codex drafts, the integrator edits** | The guidance should come from real use |
| **The cold test:** a fresh Codex session adds one object using only the guidance | **Codex** | A non-Claude AI is the honest test |
| TripoSR's local install and a trial on the RTX 2070 SUPER | **Claude, Lane C** | GPU, with the courtesy rules |
| The v2 preset: the observe focus (brief 07's diffs A1–A3 and C1) and its frame budget; then L4 and L6 | **Claude, Lane L (Sonnet)** under the capture budget | GPU captures; the diffs are already exact |
| A second-opinion review of every lane branch before it merges | **Codex** | Another vendor catches what Claude misses |
| Research (open-license detection and segmentation models for C4) and docs upkeep | **Codex** | Cheap and checkable |
| The Linux suite on each merged head | **The integrator** (one command in WSL) | Codex's sandbox has no network and cannot start WSL |

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
2. The garage loads from data with its five stand-ins; the assets validate; the founder's verdict on them is recorded.
3. The carry playtest and the companion fetch, the fetch shown by a real-client transcript summary.
4. The room rebuild test, with the journal and the discovered map surviving it.
5. The journal's boundary tests (no forged facts, no leaks about undiscovered things), and a real client reading the journal after a fetch.
6. Look captures of the garage and its stand-ins, and the founder's verdict.
7. The capture guidance (C7's start) with its recipe library, and one more object added by an AI that followed it cold.
8. `RUN-2-REPORT.md`.

**Design docs during Run 2,** written by the integrator with the founder:
- **The journal:** drafted ([JOURNAL.md](../companion/JOURNAL.md)).
- **Building:** answered ([BUILDING.md](../companion/BUILDING.md)).
- **Modes:** deferred to Run 3 or 4.

**Proposed for later, not decided:**
- **Run 3, "build with your companion":** the Victorian kit, the journal's notepad and the minimap, felt avatars, and "Connect your AI"; plus the rest of the garage's objects, and saves.
- **Run 4, the whole garage.**
- Voice waits until the baseline game works.
