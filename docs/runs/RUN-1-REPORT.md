# Run 1 report: the charming box

**Dates:** 6 to 7 October 2026. **Integration branch:** `run1/integration` (draft PR #7). **Brief:** [RUN-1.md](RUN-1.md). The day-by-day record, with every founder decision and playtest note, is [RUN-1-STATUS.md](RUN-1-STATUS.md); this page is the summary against the brief.

## Outcome

Run 1 met its goal. The founder accepted the look of the placeholder room and the garage capture guidance. The command surface passes its boundary tests against the mock, and a non-Claude client completed the live check. The body was tuned through two playtests.

Three items remain:
- the Linux suite has not yet run on the merged head;
- the founder's word that the body feels right comes with the third playtest;
- no reviewer scored the look against the bible's rubric. The founder's verdict passed the look instead.

| Goal (RUN-1.md) | Result |
|---|---|
| The placeholder room reads as a charming handmade toy diorama from fixed review cameras | **Passed by the founder,** 7 October: "The captures are fine. No big changes necessary." The preset `storybook_painterly` v1 is locked as `candidate`, and `contracts/tests` pins its bytes |
| The player is a 10 cm body that feels right | **Built and tuned through two playtests.** The 0.10 m profile, faster run, floatier gravity, the companion at 10 cm, and loose follow with routing round the big box. The founder's word on feel is pending the third playtest |
| The garage photo set yields a coverage report with specific capture guidance | **Passed by the founder,** 6 October: "the guidance is useful". The room's scale is fitted to the founder's tape measurements, within 2 to 4% on each axis |
| The AI command surface passes its boundary tests against a mock game host | **Passed.** The companion suite is at 501 tests. Codex (GPT-6.1 Sol) listed the 25 tools, completed `observe` and `goal.set` against the mock, and its red team found no breaks |

## Exit evidence

| # | Evidence | State |
|---|---|---|
| 1 | `tools/linux/test-all.sh` and the Windows runners pass on the merged head | **Windows: pass** (authority 266, canonical JSON 31/31, command host 419/419, play HUD 72/72, companion 501, small avatar 160/160, room navigation 10/10, room data 56/56, look 367/367, contracts 44, release probe). **Linux: not yet run.** WSL Ubuntu on the second machine lacks `unzip` and `uv`, so it can run there once the founder agrees to install them. A Codex cloud task would need an EnFractal environment first |
| 2 | Look captures and scores in `docs/look/reviews/run1/`, with the founder's verdict | Captures: one before and one after set of the final round, at 1920 × 1080 on the RTX 2070 SUPER. Verdict: recorded. **No rubric scores:** the two Lane L reviews were for correctness and tests, not the look rubric |
| 3 | The garage coverage report and the founder's verdict, with photos kept out of Git | Done. The report and its map stay in the capture worktree's `captures/garage/` on the first machine, never committed |
| 4 | The A1 boundary-test report and the real-client transcript summary | Done: the companion suite, and [the Codex brief 02 report](../codex/reports/02-mcp-client-red-team.md) |
| 5 | This report | This page |

## What each lane built

**Lane P (play).** P1 and P2, all merged.
- **The kernel:** it runs on room bounds and has a C# command host that handles `enfractal.command` and `enfractal.query`. The host has receipts, idempotent replay, conflicts, approvals, rate limits, text rules and the player-only operations. A golden canonical-JSON fixture is reproduced byte for byte by GDScript, C# and Python.
- **The body:** the 0.10 m profile on Jolt at 1 unit = 1 metre, with no ×10 import scale.
- **The cameras:** F3 diorama and F4 isometric views.
- **The clock:** T and Shift+T step the time of day and the season.
- **The kernel round:** it closed the eight gaps between the real host and the mock (P1 to P8), so A2 in Run 2 can swap the mock for the real host. `world.set_physics` now carries the G key through the command path.
- **Reviews:** one independent review found 1 blocker and 6 majors, all fixed with tests.

**Lane A (AI companion).** A1, merged.
- **The server:** a model-neutral MCP server with 25 tools mapped to the contract. It never lists the player-only operations.
- **The mock host:** it implements receipts, transient receipts, `expected_entities`, the approval flow and untrusted text. It is aligned with the real host's limits, and a test reads the host's constants so the two cannot drift.
- **Also built:** perception memory as plumbing (Run 2 redesigns it into the journal), emoji markers in names, and a play-only client profile.
- **Reviews:** every review finding was fixed.

**Lane L (look).** L1 to L3, merged.
- **The look bible:** written with the founder's reference frames and the 7 October Look direction.
- **The renderer:** Forward+ with VoxelGI, soft shadows, SSAO, glow, depth of field and a grade driven by the preset.
- **Materials:** painterly materials for each material role.
- **Light from real sources only:** the sun enters through the west window on a 30°N solar model, with a sky fill, moonlit nights and lamps at dusk. A 5 cm GI margin stops the sun leaking through walls.
- **Seasons:** grades for each season, and an observe view with very tight tilt-shift.
- **Reviews:** one review found 8 majors, 7 of them about rooms other than the test room, all fixed. The change requests that followed were applied late on 7 October: the room `site` key, typed look tuning in the contract, window holes in `RoomBuilder`, and the O key.

**Lane C (capture).** C1 and C2, merged.
- **Ingest:** HEIC conversion, EXIF with GPS stripped, exact and near-duplicate detection (165 exact copies found), and blur and exposure scores.
- **Poses and coverage:** feed-forward poses in batches that fit 8 GB, with Apache-2.0 weights only. 180 of 205 photos join into one model.
- **The report:** a coverage map with plain-language guidance.
- **The tape fit:** it found and fixed a scale factor that was recorded but never applied. The garage is now 5.72 × 4.71 × 2.46 m, with a scale of 0.926 (about ±1.3%).
- **Tests:** 84.
- **Reviews:** one review found 2 majors and 10 minors, all fixed.

## Measured times and costs

- **Frame time** (RTX 2070 SUPER, 1920 × 1080, the final after set): p50 12.7 to 14.0 ms against the preset's 16.7 ms target. The observe view's p95 is 17.3 ms, over budget; run-to-run noise is about 2 ms. This is open for the next look round, which goes into a v2 preset.
- **GPU:** Lane C's pose runs, the last of them 94 s. Lane L's capture rounds; the final fix round used 37 Godot launches and about 350 images, before the capture budget existed.
- **Money:** $0. No hosted APIs and no rented GPUs. Codex runs on the founder's ChatGPT plan.
- **Agent tokens:**
  - 16 Claude agent runs took about 8.6 million tokens (the [routing log](ORCHESTRATION.md#routing-log)): 13 on Opus and 3 on Sonnet trials;
  - the Sonnet trials were clean but used as many tokens as Opus, or more;
  - Codex brief 02 used about 0.2 million;
  - the integrator sessions are not counted.

## Founder decisions

They are recorded in full in [RUN-1-STATUS.md](RUN-1-STATUS.md) and summarised in [ROOM-SCALE-DIRECTION.md](../ROOM-SCALE-DIRECTION.md). The ones that change later runs:
- **Bring your own AI:** players connect their own AI or harness to the MCP surface, and no model is bundled.
- **No approval clicks:** host-enforced tiers replace them.
- **Voice is deferred** until the baseline game works.
- **Light only from real sources;** darkness and the light switch become 10 cm puzzles.
- **Style before medium;** felt figurine avatars.
- **Building is a conversation over a ghost draft,** using a Victorian kit ([BUILDING.md](../companion/BUILDING.md), with its open questions answered).
- **Shared knowledge for the pair:** a selective memory, a journal and a minimap that starts blank ([JOURNAL.md](../companion/JOURNAL.md)). This replaces "the companion sees only its own line of sight".
- **Restricted cameras in challenge modes;** the modes themselves are designed in Run 3 or 4.
- **Run 2 keeps its spine:** real objects first.

## What did not go to plan

- **The look rubric was never scored** by an independent reviewer. The founder judged the captures directly. Run 3's L7 review harness should restore scoring if the founder wants it.
- **The Linux suite** waited all run for a cloud session. It still waits, on a small install in WSL or a Codex cloud environment.
- **The path trap:** PowerShell's `cd` does not move .NET's working directory. An agent in a worktree corrupted the integrator's `project.godot` through it. The trap is now in `AGENTS.md`.
- **Duplicated GPU work** in look rounds: 10 or so avoidable captures in the last round. The capture budget in ORCHESTRATION.md came mid-run.
- **The integrator's inverted reading** of the garage scale was caught by the Lane C reviewer. This argues for independent review of the integrator's own analysis, which Codex now provides.

## Codex joins (late on 7 October)

At the founder's request, Codex (GPT-6.1 Sol, on the founder's ChatGPT plan) became the integrator's assistant. It runs through `tools/codex/run.ps1`, in its own worktree, inside the Windows sandbox. In its first evening:
- **A review of `729858a`** found four issues. Two were fixed the same night: stray faces where an opening meets a wall's edge, and `2.0` accepted where the reader wants `2`. One was by design (the schema requires every field). One minor finding was noted (very large numbers). It took about 100k tokens.
- **Surveys:** image-to-3D generators (brief 03) and connecting players' AI clients (brief 04). Both feed the Run 2 plan. The integrator spot-checked key claims against the sources.
- **The docs audit** (brief 05) found 42 stale statements. Codex fixed 29 of them in brief 08, and the integrator fixed the rest in the files it owns.
- **The third playtest's HUD notes** (brief 06): the key help folds away behind H, the top panel is compact, and the name tag is bounded to 2.5% of the screen and hides near the camera. Play HUD went from 51 to 72 checks.
- **A diagnosis of the observe view's focus** (brief 07), for the v2 preset.

Setup lessons, now in the launcher:
- The founder's Codex defaults are full access with no approvals, so every job ignores the user config.
- The sandbox cannot start PowerShell 7 when it is a Store package, so the launcher hides those folders from Codex's PATH.

## What carries into Run 2

The Run 2 plan is [RUN-2.md](RUN-2.md).
- **The swap from the mock host to the real one** (A2). Its seams are `RunningGoal`, `ReportArrival` and `GoalFinished`.
- **Opaque `job_id` handles.**
- **Save migration between room manifests:** today the player only gets a notice.
- **The observe view's frame budget,** in a v2 preset.
- **Captured rooms:** they need their openings listed and a `site`. The builder and look now handle both.
