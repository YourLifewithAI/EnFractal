# Run 1: the charming box

**Goal:** the placeholder room, with primitive props only, reads as a charming handmade toy diorama from fixed review cameras; the player is a 10 cm body that feels right; the garage photo set yields a coverage report with specific capture guidance; and the AI command surface passes its boundary tests against a mock game host. If a box room cannot be made charming, captured assets will not save it, which is why the Look lane starts here.

Read first: [AGENTS.md](../../AGENTS.md), [ownership](OWNERSHIP.md), [contracts](../../contracts/README.md), [direction](../ROOM-SCALE-DIRECTION.md).

## Team

| Role | Who | Responsibility |
|---|---|---|
| Integrator | the orchestrating session | Owns contracts and shared files, merges lanes, runs the full suite, applies cross-lane change requests, writes the run report |
| Lane L | one builder agent | L1 look bible, L2 renderer baseline, L3 material system |
| Lane P | one builder agent | P1 kernel generalization, P2 body and physics |
| Lane C | one builder agent | C1 ingest, C2 coverage and guidance |
| Lane A | one builder agent | A1 model-neutral command surface |
| Reviewers | two agents that wrote none of the code | One for correctness and tests, one scoring the look against the look bible; neither marks a gate passed |
| Founder | the project owner | Chooses reference frames, plays the 10 cm body, judges the look and the capture guidance; final say on every gate |

Branches: each lane works on `run1/<lane>` from the Run 0 head and reports with a summary, the commands it ran, their output, open questions and any change requests for files it does not own. The integrator merges into `run1/integration` and opens one PR.

## Where each lane can run

| Lane | Cloud container | Founder's Windows machine |
|---|---|---|
| P | Yes: Godot .NET builds and runs headless after `tools/linux/setup-toolchain.sh` | Yes |
| A | Yes: Python only | Yes |
| C | C1 yes; C2 pose estimation needs a GPU, so the founder's RTX 2070 Super or a rented GPU (see [compute options](../pipeline/COMPUTE-OPTIONS.md)) | Preferred |
| L | Code yes, but headless Godot renders nothing; review captures and frame timings need the founder's GPU | Required for captures and timings |

## Lane L: Look (L1, L2, L3)

**Owns:** `docs/look/**`, `game/styles/**`, `game/shaders/**`, `game/scripts/native/Look/**`, `game/scripts/painterly_assets.gd`, `tools/look/**`, `game/tests/native/Look/**`. **Run 1 grant:** may change lines in the `[rendering]` section of `game/project.godot` only (renderer method, lights and shadows, global illumination, anti-aliasing, environment defaults), recording before and after values and measured frame times in the report.

- **L1 look bible.** `docs/look/LOOK-BIBLE.md`: chosen reference frames (ask the founder; start from the art notes in the founder's Drive folder), a scoring rubric for palette and desaturation, warm key against cool shadow, edge softness, material handmade-ness per role, clutter density, depth of field and the diorama feel. Define five fixed review cameras in the test room in `tools/look/review_cameras.json` (player eye, over-the-shoulder, companion, low corner, ceiling corner).
- **L2 renderer baseline.** Forward+ with VoxelGI sized to the room bounds, soft shadows, SSAO, restrained glow, depth of field that keeps the player's reach crisp and softens the far room, colour grading driven by the preset's palette, time of day and season. Everything reads from `game/styles/storybook_painterly/v1.json`. While it is a seed or draft, edit it in place; when you submit it for the look gate, set `status` to `candidate`, and from then on every change is a new version file (`v2.json`) because rooms and saves may pin it.
- **L3 material system.** Replace the plain `MaterialLibrary` with painterly materials per material role, driven by the preset's `materials.roles`, reusing the existing painterly shaders where they help.
- **Acceptance:** captures from the five cameras before and after, at 1920 × 1080 on the founder's GPU; frame time within the preset budget; the look reviewer's scores against the bible with specific critiques; the founder's verdict. All existing suites still pass.
- **Stop when** the founder accepts the look or after three review rounds, reporting what still misses.

## Lane P: Play (P1, P2)

**Owns:** the kernel scripts and tests, `game/scripts/native/Kernel/**`, `game/tests/native/Kernel/**`, `game/tests/native_kernel_*.tscn`, `game/tests/fixtures/kernel/**`, `tools/kernel/**`, `game/native/WorldScaleProfile.cs`, `SmallPlayerController.cs`, `CompanionAvatar.cs` (body only), `game/tests/native/SmallAvatarPhysicsTest.cs`, `game/scenes/player_test.tscn`, `docs/engine/phase3/**`. **Run 1 grant:** may change lines in the `[physics]` section of `game/project.godot` only, recording before and after values.

- **P1 kernel generalization.** The authority uses room bounds from `RoomData` instead of the Barton plot rectangle; protected zones become locks; the terrain height callable becomes a physics surface query; principals become `player:local` and `companion:local`; a C# command host (`Kernel/CommandHost.cs`, with a static `Attach(RoomWorld)` the integrator wires in) accepts `enfractal.command` and `enfractal.query` messages and returns `enfractal.result`. It fingerprints the contract command as received, checks `expected_revision` and `expected_entities` itself, never refuses `goal.stop` or `effect.stop` on revisions, keeps transient receipts for goals, effects and grabs, refuses `protect.unlock` from the companion, and implements approvals as the contracts README describes: hold the command, return a 128-bit `request_id`, commit on the player's click under the original principal with `approved_by`, answer `approval.status`; `world_state.gd` pins a room manifest hash instead of a map manifest. Rewire the invention runtime to the C# controller and retire `player_controller.gd`, `player_test.tscn` and the legacy fixture paths. Add a golden canonical-JSON fixture that GDScript, C# and Python all reproduce byte for byte.
- **P2 body and physics.** Make the default profile about 0.10 m tall (radius about 0.02 m, eye about 0.087 m, reach about 0.15 m); retune walk, run, jump, step height, floor snap and safe margin; decide whether real gravity or a tuned profile feels right. Run the jitter spike: stand, walk and step on box edges, the 4 cm book, the 6 mm rug and slopes; if the 0.02 m capsule jitters, measure the ×10 import-scale alternative and record the decision. The companion keeps its own profile.
- **Coordination:** the release probe and `native_interop_smoke.gd` currently assert 0.30 m. Send the integrator the new expected values; the integrator updates the probe and docs in the merge.
- **Acceptance:** all kernel suites pass against room bounds; a command-host test drives place, revise, remove, lock and goal commands through `enfractal.command` with receipts, idempotent replay and conflicts; the small-avatar fixture passes at the new profile; a ten-minute founder playtest says the body feels right.
- **Stop when** both acceptance lists pass or a physics limitation needs the founder's decision.

## Lane C: Capture (C1, C2)

**Owns:** `pipeline/roomscan/**`, `docs/pipeline/**`, local `captures/` (never committed).

- **C1 ingest.** A Python package with a pinned environment: HEIC to JPEG, EXIF (device, focal length, exposure, timestamp; strip GPS from anything written), exact and near-duplicate detection, blur and exposure-clipping scores, and a per-session manifest. Unit tests on synthetic images.
- **C2 coverage and guidance.** Fast feed-forward poses (VGGT or MASt3R) in batches that fit 8 GB, a view graph, a coverage map of floor, walls and ceiling, per-object view counts, and plain-language guidance ("three more photos of the shelving from the left at shelf height, about one metre away"). Record GPU time and any money spent.
- **Input:** the founder's garage photos from Google Drive (`Enfractal / Photos for space generation / Garage`), downloaded to `captures/garage/`. **Photos, splats, derived images and exported rooms of real places are never committed** without the founder's explicit approval; exports go to `user://rooms/`.
- **Acceptance:** a coverage report for the garage set with a rendered top-down coverage map and specific guidance the founder judges useful; runtime and cost recorded; tests pass.
- **Stop when** the founder accepts the guidance or the pose backend cannot run within the [compute options](../pipeline/COMPUTE-OPTIONS.md) budget, in which case report the measured blocker.

## Lane A: AI companion (A1)

**Owns:** `companion/**`, `game/scripts/native/Companion/**`, `docs/companion/**`.

- **A1 command surface.** A model-neutral MCP server whose tools map onto the contract's commands and queries (`observe`, `entities.list`, `entity.inspect`, `goal.set`, `goal.stop`, `entity.grab`, `creation.place`, `protect.lock`, `approval.status` and the rest) **except the player-only operations** in `$defs/player_only_ops`, which it never exposes. A companion observes only through `avatar:companion`. The mock game host implements receipts, `expected_entities`, transient receipts, the approval flow (held command, `request_id`, `approval.status`, commit under the original principal) and untrusted text faithfully, so P1's real host can replace it in Run 2. The transport between the MCP server and the running game is a loopback connection with a per-session token; define it in `docs/companion/` and propose any contract addition to the integrator.
- **Security boundary tests:** a principal smuggled into arguments or effect parameters, any attempt to send an approval, display names and sign text that try to issue instructions (presented to the model as data), entity ids that do not exist or are not in this room (fail with `target_not_found` and leak nothing), stale revisions, replay and conflicting replay, `protect.unlock` not listed and refused, observing through the player's avatar refused, messages over the size limits, rate limits, and the server refusing anything outside the game (no files, shell, URLs or credentials).
- **Acceptance:** boundary tests pass against the mock host; a real MCP client (Claude Code is acceptable) lists the tools and completes `observe` then `goal.set` against the mock; nothing in the surface is specific to one model vendor.
- **Stop when** acceptance passes; wiring to the real kernel host is Run 2 (A2 with P1's host).

## Integration and exit

The integrator merges lanes in the order P, A, L, C, resolving contract change requests first. Exit evidence for the run:

1. `tools/linux/test-all.sh` (cloud) and the PowerShell runners (Windows) pass on the merged head.
2. Look captures and scores in `docs/look/reviews/run1/`, the founder's verdict recorded.
3. The garage coverage report and the founder's verdict recorded, with photos kept out of Git.
4. The A1 boundary-test report and the real-client transcript summary.
5. A run report in `docs/runs/RUN-1-REPORT.md`: what passed, what did not, measured times and costs, and the decisions the founder made.
