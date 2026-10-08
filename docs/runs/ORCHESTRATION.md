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
| Second-opinion reviews, research surveys, docs audits and fixes, small scoped code changes | **Codex** (GPT-6.1 Sol, the founder's ChatGPT plan) through `tools/codex/run.ps1` | Another vendor's model catches what Claude misses, and it saves Claude tokens. Its own worktree and branch, the Windows sandbox, a scope block per brief; the integrator checks scope, verifies claims and merges. Never on a file a running lane owns |

**The founder, 7 October: Claude and Codex as work partners, each leaning into what it does best.** "I think you and Codex can do more together. [...] the quality of your work, especially in the artistic space interestingly enough, is better than Codex's 3D rendering. That's the kind of thing we'll see from one model to the next. So it's more about figuring out who's better at what and leaning into that." Token cost is not the reason. And then: "Let's not assume you or Codex are better or worse at this. We can do A/B tests and find out." So:
- **Decide by A/B tests, not assumptions.** For a task type that matters (the landscape's look, design drafts, kernel work, reviews), give the same bounded task to two models, judge the results blind where possible (the founder judges anything visual), and record the outcome in the A/B log below. Route that task type to the winner until a new model is worth retrying.
- **Evidence so far, not a verdict:** Codex's reviews found real majors in every lane branch this run; its research, docs and test tooling were clean. Its Blender renders (briefs 10 to 12) were judged basic or blockouts, but no Claude render was made to compare, so that is untested.
- **Models to try, through `tools/codex/run.ps1 -Model <slug>`** (the ChatGPT plan; no spend): `gpt-6.1-sol` (used so far), `gpt-6-astra` (the founder's default), `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`. List the current catalogue with `codex debug models`. OpenRouter models (DeepSeek V4.1 Flash, and hidden ones such as Gemini 3.8 Flash, GLM-5.3, Qwen3.8 Flash) go through the founder's local Codex router, which the launcher deliberately bypasses (it ignores the founder's config). The founder (7 October): skip OpenRouter for now; maybe later. Claude's models (Opus, Sonnet, Haiku) are the other side of each test.
- **Mixed work:** split it where the A/B log says, and until it says, keep the integrator's judgement and say why.

### A/B log

| Date | Task | Contenders | Judged by | Result |
|---|---|---|---|---|
| 2026-10-08 | Rewrite `docs/ROOM-TO-LANDSCAPE.md` around the four principles, at most 1,000 words, from the same inputs (brief 15) | The integrator (Opus 5.5) against Codex brief 15 (GPT-6 Astra, high effort), each blind to the other | The founder, blind (drafts numbered at random; the key is in the integrator's scratch folder) | Pending |
| 2026-10-08 | Second-opinion review of Lane P's round 2, parts 1–2 (`4eefb39..1f540bb`), the same prompt, read-only, in parallel | Codex GPT-6.1 Sol against GPT-6 Astra, both high effort | The integrator, each finding checked against the source | **Astra slightly ahead, one trial.** Six findings shared (save validation, shared sight, atomic facts, discovery starvation, the unreachable timer's gap, eviction). Astra alone: a journal task names an undiscovered target to the companion (a boundary finding) and the discovered map's unbounded save size. Sol alone: a walkable-top level follows a moved object without sight. All nine real; no false positives from either. Astra 108k tokens, Sol not shown |

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
| 2026-10-07 | Codex brief 02: non-Claude MCP client and red team against the mock | GPT-6.1 Sol (Codex CLI, locked down) | about 0.2M (estimated) | Clean; honest about gaps; did not bypass a blocked approval. 3.9 min. Integrator setup took longer than the run (the SYSTEMROOT and code-mode findings, now in the brief) |
| 2026-10-07 | L fix round and art pass (8 majors, minors, the founder's 7 October direction) | **Sonnet** | 971k | Clean, in scope; 5 of 5 mutations caught; found the GI wall leak itself. **The most tokens of any round** (the Opus L rounds took 642k to 734k), and 80 min. 37 Godot launches and about 350 images, about 10 of them avoidable (a broken shader, a hang, an extra GI variant); the capture budget came mid-round. Next L round: Sonnet with the budget from the start, and compare |

| 2026-10-07 | Codex: review of `729858a` (the Lane L change requests) | GPT-6.1 Sol, high effort | 102k | 4 findings: 2 real (fixed that night), 1 by design, 1 minor. Source-traced, honest about not running Godot |
| 2026-10-07 | Codex brief 03: image-to-3D survey | GPT-6.1 Sol, web search | 215k | Clean; 10 candidates, corrected 6 pipeline assumptions; spot-checked claims held |
| 2026-10-07 | Codex brief 04: BYOAI connection survey | GPT-6.1 Sol, web search | 175k | Clean; 10 clients, a one-page "Connect your AI" proposal |
| 2026-10-07 | Codex brief 05: docs audit | GPT-6.1 Sol | not shown | 42 findings with sources |
| 2026-10-07 | Codex brief 06: HUD declutter and name tag (third playtest) | GPT-6.1 Sol, high effort | 77k | Clean; compiled in its sandbox; play HUD 51 to 72 checks, all passing on the integrator's run |
| 2026-10-07 | Codex brief 08: docs fixes | GPT-6.1 Sol | 80k | 29 fixed, 1 correctly left; small edits in each file's voice |
| 2026-10-07 | Codex brief 07: observe focus diagnosis, then a source check | GPT-6.1 Sol, high effort; web search for the follow-up | 135k plus a small follow-up | Two real causes with arithmetic and exact diffs. It flagged its own uncertain third cause and settled it from Godot's tagged renderer source |

| 2026-10-07 | Codex brief 09: AI-built objects in Blender (a cardboard box and the Bonne Maman jar) | GPT-6.1 Sol, high effort, web search | 116k | Clean; headless Blender 5.2.2 in its sandbox, about 4 min of authoring, both scripts right first time, $0. Stopped honestly at its own audit over CRLF in two JSON files; the integrator normalised them |
| 2026-10-07 | Codex brief 10: the recipe library (five parametric Blender recipes, shared checks, runner, tests) | GPT-6.1 Sol, high effort, web search | 115k + 107k + 86k (three runs) | Clean and in scope. Run 1 stopped correctly at a sandbox denial (Python 3.14's owner-only `mkdtemp` folders; the integrator diagnosed it and added a rule to the Codex README); run 2 fixed it and stopped at 3 failing tests; run 3, authorised, fixed a couch bevel collapse and byte determinism. 7 tests, 49 builds, exit 0; the integrator's spot build outside the sandbox matched |
| 2026-10-07 | Run 2 contract round for the integrator (sandbox verbs, opaque `job_id`, journal and map queries, room state's `journal` and `discovered`) | Opus | 546k | Clean; 34 min; reused three existing verbs and added only `entity.push`; touched the host and mock only to mint opaque ids; 5 of 5 mutations caught; both Windows runners and the Linux suite green |
| 2026-10-07 | Codex brief 11: the storybook style in the recipes (after the founder judged brief 10 "basic") | GPT-6.1 Sol, high effort, web search | 171k | Clean and in scope; 9 tests, 64 builds; found why the laptop looked off (the deck ignored the lid overhang; not the lens) and that the game's material swap drops glass alpha (a Look change request). Stopped at its own 800 KB sheet cap (921 KB), which the integrator waived; the integrator's two spot builds were byte-identical |
| 2026-10-07 | P3 sandbox verbs (grab, carry, release, place, stack, push; save format 4) | Opus | 618k | Clean, in scope; 43 min; HUD 83/83, sandbox suite 96/96, 5 of 5 mutations caught (one found a save-order bug). Codex's review then found 4 majors (below) |
| 2026-10-07 | A2: the swap to the real host (C# link server), follow, come, look, point, fetch on the mock, per-client profiles | Opus | 574k | Clean, in scope; 43 min; companion 566 tests, real-host suite 39; drove a real Codex client through the real host. Codex's review then found 2 majors (below) |
| 2026-10-07 | Codex second-opinion reviews of P3 and A2, in parallel | GPT-6.1 Sol, high effort, read-only | 188k (P3), 123k (A2) | P3: 4 majors (grab skips readiness and role checks; save poses trusted; put-down through thin walls; removing a creation leaves objects floating). A2: 2 majors (two windows racing for the link; unauthenticated traffic floods the main thread) and 1 minor. Source-traced; the lanes fix them |
| 2026-10-07 | C3 garage shell and C4 inventory (from Run 1's poses; OWLv2 proposals, SAM 2.1 masks) | **Sonnet** | 821k | Clean, in scope, nothing of the garage committed; 96 min, the slowest and largest round yet; the shell loads in the game; 19 objects, sizes good to about 15% (the five picks measured by eye from overlays); 5 of 5 mutations caught; 3.5 min of GPU, 4.4 GB of wheels into its worktree |
| 2026-10-07 | Codex brief 12: spike, one synthetic room as a landscape three ways | GPT-6.1 Sol, high effort, web search | 44k + 199k (two runs) | Run 1 stopped at a wrong branch name in the brief (the integrator's error: `origin/geography-era-final`); run 2 clean and in scope. Measured grounding per landform, recommended the hybrid, found the geography era's painterly ground shader reusable. The renders are blockouts: still a room with lumps in it |
| 2026-10-07 | Codex second-opinion review of C3 and C4 | GPT-6.1 Sol, high effort, read-only | 134k | 1 blocker (review images could be saved where Git tracks them), 2 majors (rotated-box deduplication; a failed export replaced a valid room), 2 minors. Lane C fixes them |
| 2026-10-07 | P3 review fix round, plus Lane A's go_to diff | Opus (same agent, resumed) | 84k more (702k total) | Clean; every major shown failing first (14 of 127 checks); sandbox suite 127/127; command host 421/421 |
| 2026-10-07 | A2 review fix round | Opus (same agent, resumed) | 65k more (638k total) | Clean; every finding shown failing first; a lock file per account for the link; diagnostics bounded; companion 573 tests |
| 2026-10-07 | C3/C4 review fix round | Sonnet (same agent, resumed) | 84k more (905k total) | Clean; all five shown failing first; output guard against any Git checkout; 20 objects after the rotated-box fix; roomscan 136 |
| 2026-10-07 | A2 Linux path fix (Windows profile paths on Linux) | Opus (same agent, resumed) | 17k more | Clean; found by the integrator's Linux suite, not by the lane's Windows runs |
| 2026-10-07 | Codex brief 13: prior art and a first draft of `docs/ROOM-TO-LANDSCAPE.md` | GPT-6.1 Sol, high effort, web search | 167k | Clean, in scope; a useful draft and sources, but written before the founder's physics and looseness notes, so the next session rewrites it |
| 2026-10-07 | Codex brief 14: a synthetic room corpus (8 room types, each nominal and two scan-like variants) | GPT-6.1 Sol, high effort, web search | 165k + 102k (two runs) | Run 1 stopped honestly at a missing `cv2`; with Lane C's interpreter, run 2 fixed 8 real failures (boundary tolerances, a shelf taller than its room, PNG bytes varying with zlib, small objects the reader refused). 10 tests; the integrator pinned the files' bytes (`-text`) and added them to `test-all.sh` |

| 2026-10-08 | Codex brief 15: an independent rewrite of the landscape design (one side of the A/B above) | **GPT-6 Astra** (first use), high effort, no web | 59k | Clean, in scope (one file); under the word cap; carried the approved core word for word. Content notes wait until the founder has judged |
| 2026-10-08 | P round 2, parts 1–2: fetch on the real host with `target_unreachable`; the team's map store and journal writer; `journal.read`, `journal.note`, `map.find`; save format 5 | Opus | 470k | Clean, in scope; about 30 min; journal suite 48/48, sandbox 151/151, command host 423/423; 4 of 4 mutations caught. Stopped before P6 as told. The two Codex reviews then found 6 majors and 3 minors (A/B log) |
| 2026-10-08 | Codex second-opinion reviews of P round 2, in parallel (an A/B) | GPT-6.1 Sol and **GPT-6 Astra**, high effort, read-only | not shown (Sol), 108k (Astra) | Both source-traced and honest about not running Godot; every finding checked and real |

The first Codex launches failed at once, "blocked by policy". They had skipped the founder's config, which also turned off the Windows sandbox, and the sandbox then could not start the Store-packaged PowerShell 7. Codex stopped and reported, as its rules say. `tools/codex/run.ps1` now handles both.

The early runs were all on Opus, with long prompts and mutation sweeps. Use them as the baseline when trying Sonnet on the task types above.

As of 7 October, Sonnet's first two trials were both clean: a P round of interface and feel fixes, and a C pipeline fix round. Each used about as many tokens as an Opus round, at a lower price per token. Keep Sonnet for those task types, and keep Opus for reviews and the command host.
