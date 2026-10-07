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

The early runs were all on Opus, with long prompts and mutation sweeps. Use them as the baseline when trying Sonnet on the task types above.
