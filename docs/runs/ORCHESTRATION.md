# How runs are orchestrated

**Owner:** the integrator. Read this at the start of every integrating session. Update it at major milestones (the end of each run, or when a lane completes a roadmap chunk), using what the routing log shows. Lane agents follow [AGENTS.md](../../AGENTS.md); this page is for the session that plans and dispatches work. Its rules come from the founder, 6 October 2026.

## Sessions

- **Budget:** aim to end an integrating session at about **25% of its context window.** Long sessions re-send their whole history every turn, which is the biggest avoidable token cost.
- **Before you reach it:** find a clean stopping point. Every running lane has reported and been merged or parked; nothing is half-applied. Then:
  1. update the run status page (`RUN-<n>-STATUS.md`) as the handoff;
  2. push;
  3. tell the founder to start a fresh session.

  A new session picks up from the status page, this page and `AGENTS.md`.
- **Stage lane work to fit.** Dispatch chunks that can finish and report within the session's budget. Don't start an agent whose report would land after the handoff, because a new session cannot receive it.

## Doing work: agent or integrator

| Work | Who |
|---|---|
| Doc edits, decision records, tuning numbers, applying a change request, a small fix in an integrator-owned file | **The integrator, directly.** No agent: an agent re-reads every project doc first |
| A lane packet: new behaviour, a fix round, an art pass, the pipeline | **One agent per lane**, with a short prompt pointing to `AGENTS.md`, the run brief and the status page |
| A whole-lane review | **One reviewer agent** that wrote none of the code, only when a lane has completed a full chunk of its roadmap (see below) |

Fewer agents in parallel is better than more, because they share one machine and one GPU.

## Model routing

Choose by **capability for the task, then cost.** A cheap agent whose work needs cleaning up later costs more than a capable one. Record every agent run in the routing log, and move a task type to a cheaper model only when the log shows that model does it cleanly.

| Task type | Start with | Why |
|---|---|---|
| Security boundary, command host, kernel, contracts, persistence | **Opus** | Subtle, high-stakes; reviews keep finding majors here |
| Independent reviews | **Opus** | Must find what the builder missed |
| Design docs and cross-lane architecture | **Opus** (often the integrator directly) | Judgement-heavy |
| Look passes (tuning, captures, critique), body and feel tuning, the capture pipeline | **Sonnet**, then promote to Opus if the log shows rework | Bounded, well-tested, visual or numeric |
| Mechanical work: regenerating files, applying a known patch, running a checklist, collecting results | **Haiku** or the integrator | No judgement needed |

## Testing

- **Integrator:**
  - one full suite run per merge, or per batch of merges, before pushing to the integration branch;
  - only the affected suites for a small change;
  - none for doc-only commits.
- **Agents:**
  - targeted tests while iterating, and the full runners once at the end;
  - mutation checks capped at five key protections per round;
  - failing-before evidence for blockers and majors only.
- **GPU captures (Look), a budget per round.** A full set is about 36 shots: 7 cameras, repeated under the summer sun, at night with the lamps and with the lamps on and off, plus foot close-ups and a time-of-day sweep. **Duplicated work slows progress and costs tokens** (the founder, 7 October). The window on screen is fine. On 7 October one round took two full `before` sets and whole-set renders to compare one tuning number.
  - **One full `before` set and one full `after` set per round.** Change the cameras or the harness first, then take `before` once.
  - **Probes and tuning variants render only the views in question:** `-Only <camera>`, with `-NoLightChecks` unless the light is what is being probed.
  - **Tune by numbers first** with the headless look suite and pixel metrics; render only to confirm.
  - **Diagnose a hung or failed run before relaunching it.**
  - **Reports state** the number of capture launches and images rendered.

## Reviews

- **Partial work** (a fix round, a tuning pass, part of a packet): the integrator reviews what changed. Check the scope, look at the test results, and spot-check the risky change.
- **A whole-lane review** happens only when a lane has finished a complete chunk of its roadmap in a session (for example all of P1 and P2, or all of A1), not after a partial build.

## Reports

Agent final reports are about 250 words:
- what changed;
- the final pass and fail lines;
- decisions the founder must make;
- commit hashes.

Agents cannot write report files in this environment, so ask the agent (`SendMessage`) for details only when something looks off.

## Routing log

Add one row per agent run.

| Date | Lane / task | Model | Tokens | Outcome |
|---|---|---|---|---|
| 2026-10-06 | A1 review fix round | Opus | 578k | Clean; every finding fixed and tested |
| 2026-10-06 | L1–L3 fix round and first art pass | Opus | 734k | Clean |
| 2026-10-06 | P playtest quick fixes and navigation | Opus | 433k | Clean |
| 2026-10-06 | P 10 cm companion and hover diagnosis | Opus | 374k | Clean; first direct Godot launch raised a .NET dialog |
| 2026-10-06 | Independent review of P | Opus | 434k | Found 1 blocker and 6 majors; wrote one file outside its worktree (path trap) |
| 2026-10-06 | A memory and emoji | Opus | 517k | Clean |
| 2026-10-06 | L round 2: real-source lighting | Opus | 642k | Clean |
| 2026-10-06 | C1 and C2 ingest, coverage and the garage report | Opus | 556k | Clean; chose Apache-2.0 weights unprompted; 5.5 GB downloads |
| 2026-10-06 | P review fix round (1 blocker, 6 majors, minors) | Opus | 704k | Clean; every finding fixed with a test |
| 2026-10-07 | Independent review of C | Opus | 363k | 0 blockers, 2 majors, 10 minors; 4 of 5 mutations survived; caught the integrator's inverted reading of the scale |
| 2026-10-07 | A: align the mock with the real host | Opus | 357k | Clean; 492 tests; 8 change requests for P and 2 for the contracts |
| 2026-10-07 | P second-playtest fix round (keys, clock, name tag, running blur) | **Sonnet** (first trial) | 486k | Clean; every fix measured before and after; 5 mutations killed; diffs for the integrator were exact. More tokens than the Opus P rounds (374k to 433k), at a lower price per token |
| 2026-10-07 | C review fix round and tape scale fit | **Sonnet** | 528k | Clean; all 12 findings fixed; 84 tests; the 4 surviving mutations now caught; GPU rerun at $0. Slow (54 min) |
| 2026-10-07 | Independent review of L | Opus | 398k | 0 blockers, 8 majors (7 about rooms other than the test room, 1 latent crash), 7 minors; 5 of 5 mutations survived; clean worktrees |
| 2026-10-07 | P kernel round: Lane A's gaps P1–P8 and `world.set_physics` | Opus | 515k | Clean; command host 201 to 419 checks; 5 of 5 mutations caught; dump validates with 0 problems; found two mock bugs and sent exact diffs. About 37 min |
| 2026-10-07 | L fix round and art pass (8 majors, minors, the founder's 7 October direction) | **Sonnet** | 971k | Clean, in scope; 5 of 5 mutations caught; found the GI wall leak itself. **The most tokens of any round** (the Opus L rounds took 642k to 734k), and 80 min. 37 Godot launches and about 350 images, about 10 of them avoidable (a broken shader, a hang, an extra GI variant); the capture budget came mid-round. Next L round: Sonnet with the budget from the start, and compare |

The early runs were all on Opus, with long prompts and mutation sweeps. Use them as the baseline when trying Sonnet on the task types above.

As of 7 October, Sonnet's first two trials were both clean: a P round of interface and feel fixes, and a C pipeline fix round. Each used about as many tokens as an Opus round, at a lower price per token. Keep Sonnet for those task types, and keep Opus for reviews and the command host.
