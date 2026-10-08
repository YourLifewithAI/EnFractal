# Run 2 status

**Run 2 started on 7 October 2026 (evening, local).** This page is the handoff for the next integrating session.

## Now (7 October, evening)

- **PR #7 is merged** (the founder's approval): Run 1 is on `main` at `b0ecf18`. `run2/integration` starts there.
- **Lane worktrees:** `C:\dev\EnFractal-run2\{play,companion,capture,look}` on `run2/<lane>`, plus `contracts` on `run2/contracts` for the contract round; each has the `.cache` junctions.
- **Run 1's garage capture data was on this machine** after all, in `C:\dev\EnFractal-run1\capture\captures` (774 MB, session `s-27647e23c354`). It is copied into `C:\dev\EnFractal-run2\capture\captures`, so Lane C runs here and the poses need no rerun. Keep the Run 1 copy until Run 2's capture work is merged.
- **Merged: Codex brief 10, the recipe library** (`pipeline/recipes/**`, now in OWNERSHIP.md; [report](../codex/reports/10-recipe-library.md)).
  - Five parametric Blender recipes: cardboard box, couch, gaming laptop, jam jar, French press. One input shape (`recipe`, `size_m` as `[w, h, d]`, colour slots, `params`), one output (GLB, receipt, CPU preview), shared checks before export including `check_glb`.
  - 7 tests and 49 builds pass in about 4.5 min (`python -B -m unittest pipeline.recipes.tests -v` from the repository root, with Blender installed); all five rebuild byte-identically. Not in the Windows or Linux runners: they need Blender.
  - Previews in `pipeline/recipes/previews/`.
- **The founder's verdict on brief 10's previews (7 October): basic.** The proportions are mostly right, the laptop looks off, the French press and glass are grainy, and nothing is stylised or charming: "If the point of the exercise is to reproduce an object, I'd say that's been achieved." Brief 10 had asked for plain shapes and left the style to Lane L's L4. **The founder chose to put the style in the recipes now:** [brief 11](../codex/briefs/11-storybook-recipes.md), a shared storybook shape layer from the look bible (soft, rounded, slightly wonky, toy-like forms; painted colour; stylised glass), grain-free previews with a longer lens, the laptop fixed, and a plain-versus-storybook comparison sheet. Lane L's in-game treatment (L4, L6) still adds to it.
- **Merged: the contract round** (`a1eee3d`): `entity.push` is the one new verb (pick up, drop, place with snapping and stack reuse `entity.grab`, `entity.release` and `entity.place`, now documented); `job_id` is `job-` plus 26 random base32 characters, minted by a cryptographic generator in both the host and the mock; `journal.read`, `journal.note` and `map.find`; room state's optional `journal` and `discovered` (older states stay valid). Until Lanes P and A build them, the host answers the four new ops "operation does not exist" and the mock refuses them. Bounds chosen by the integrator's agent, for the founder to see: at most 32 open tasks, `map.find` answers at most 10, a push moves at most 1 m. A fetch ends with the companion holding the object beside the player; setting it down is a separate command. Windows runners: command host 420/420, HUD 72/72, companion 501, avatar 160/160, navigation 10/10, room data 56/56, look 367/367; contracts 64.
- **Running:**
  - Codex brief 11, the storybook recipes;
  - Lane C (Sonnet): C3 shell and C4 inventory.
- **Next:** Lanes P and A, now that the contracts are merged; Lane L when Lane C's GPU work is done.

## The handoff from Run 1 (7 October, night)

The rest of this page is the handoff written when Run 1 closed. Read it with [ORCHESTRATION.md](ORCHESTRATION.md) (session budget, model routing, testing, reviews), the approved plan [RUN-2.md](RUN-2.md) and [AGENTS.md](../../AGENTS.md). Run 1's record is [RUN-1-REPORT.md](RUN-1-REPORT.md), with the day-by-day detail in [RUN-1-STATUS.md](RUN-1-STATUS.md).

## Where things stand

- **Run 1 is complete.**
  - The founder signed off the 10 cm body on the night of 7 October.
  - The look gate passed: `storybook_painterly` v1 is locked as `candidate`.
  - The garage guidance was accepted.
  - A1 passed with a real, non-Claude client.
  - The Windows runners and `tools/linux/test-all.sh` (in WSL) both pass on `run1/integration`.
  - The one gap: no reviewer scored the look against the bible's rubric, because the founder's verdict passed it instead.
- **`run1/integration`** is pushed and holds everything (draft PR #7 into `main`). **Merging Run 1 into `main` is the founder's call: ask first.**
- **Run 2's plan is approved** (the founder, 7 October, night). In short:
  - the scanned garage's shell plus five of its objects, as stylised stand-ins that are generally faithful in kind, size, place and colour;
  - carry and fetch through the real host;
  - the room rebuilt from data;
  - the journal's data layer.
- **The toolchain:** Blender recipes first, TripoSR (MIT) second, nothing under Stability's license, nothing online or paid. Objects come from the room scan; no object is photographed on its own.

## The founder on scale (7 October, night)

- **The 10 cm body is signed off.**
- **Smaller bodies are worth trying later** (1 cm, say), once a fully rendered, stylised room exists to play in. The question is how much play one avatar gets in a normal-sized room.
- **What it would take** (integrator's note):
  - the body is a profile (`WorldScaleProfile`), so trying it is cheap;
  - at 1 cm, Jolt's precision for a 2 mm capsule, the step heights and the camera's near plane mean revisiting P2's ×10 import-scale question;
  - room detail matters more at that size (a rug's pile becomes terrain).
- Plan it as an experiment after the garage is rendered, not before.

## The first session of Run 2

Stage the work to the session budget in ORCHESTRATION.md: dispatch only what can report back before the handoff.

1. **Ask the founder** whether to merge PR #7 into `main` first. Then create `run2/integration` (from `main` if merged, else from `run1/integration`).
2. **Set up the lane worktrees:** `C:\dev\EnFractal-run2\{play,companion,capture,look}` on `run2/<lane>`. Each worktree's `.cache\dotnet` and `.cache\godot` are junctions into `C:\dev\EnFractal\.cache`, as in Run 1. Retiring Run 1's worktrees (`C:\dev\EnFractal-run1\*`, including `review-play`) is for the founder to do by hand: unlink the junctions first, and the safety check blocks agents from deleting worktrees.
3. **The integrator writes the contract changes first:**
   - the sandbox verbs;
   - opaque `job_id`;
   - `journal.read`, `journal.note` and `map.find`;
   - room state's `journal` and `discovered`.

   [JOURNAL.md](../companion/JOURNAL.md) lists the additions. Apply them with examples and tests, and run one full suite.
4. **Then dispatch, as RUN-2.md's "Who does what" table says:**
   - **Codex, at once** (it needs no contract change): brief 10, the recipe library. The five founder-named recipes go in as generic parametric Blender scripts (box, couch, laptop, jam jar, French press), with the checks before export, building on [brief 09](../codex/reports/09-blender-spike.md). Write the brief with a scope that no lane owns, for example `pipeline/recipes/**` (a new directory, so record it in OWNERSHIP.md).
   - **Lane P (Opus)** after the contract change: P3 first, then P6 and the map store.
   - **Lane A (Opus)** after the contract change: the A2 swap, then the journal's queries.
   - **Lane C (Sonnet):** C3 and C4 on the garage. The poses and Run 1's capture data are in `captures/garage/` on the **first machine**, so run it there, or rerun the poses here from the Drive photos (about 94 s of GPU). Check this machine with `tools/check-run1-readiness.ps1` first.
   - **Lane L (Sonnet), when the GPU is free:** the v2 preset with brief 07's diffs A1–A3 and C1 (omit B1–B3: Godot's near blur was verified), then the observe frame budget, under the capture budget.
5. Keep **fewer agents at once rather than more:** they share one machine and one GPU.

## Codex: how to use it

- **Launcher:** `pwsh -NoProfile -File tools/codex/run.ps1 -Brief <name> -Base run2/integration -Out <scratch>\<name>.md`. Add `-Search` for web research, and `-ReadOnly` for reviews. Run it in the background.
- **Where it works:** each job gets `C:\dev\EnFractal-codex\<brief>` on `codex/<brief>`. It reads anything and writes only there, and it cannot commit.
- **Merging:** the integrator commits on that branch, runs `python tools/codex/check_scope.py codex/<brief> --ref codex/<brief> --base <base>`, verifies the claims, merges, and then runs `git worktree remove` (there are no junctions inside).
- **What it can do:** compile C# with `C:\dev\EnFractal\.cache\dotnet\dotnet.exe`, and run Blender 5.2.2 headless (`C:\Users\blues\AppData\Local\Programs\Blender`).
- **What it cannot do:** run Godot, use the network for commands, or start WSL.
- **Its first nine runs** were clean and stayed in scope, at about 75k to 215k tokens each. Log every run in ORCHESTRATION.md's routing log.
- **The cloud environment:** the founder's account has two Codex cloud environments, both named "EnFractal" (one is probably a duplicate). Neither ID is known here, and cloud jobs are not needed while the Linux suite runs in WSL.

## The Linux suite (second machine)

```powershell
wsl -d Ubuntu -u root -- bash -lc "cd /root/enfractal-linux && git pull -q && tools/linux/test-all.sh"
```

- The clone's origin is `C:\dev\EnFractal` through `/mnt/c`, so it tests whatever that checkout has committed.
- The toolchain is installed under `.cache/linux/`, and the suite needs no network.
- The default user is root; `test-all.sh` silences Godot's root warning itself.

## Carried into Run 2

- **Save migration between room manifests:** do it in P6 (the proposal is in `command-host.md`).
- **The observe view's frame budget** (p95 17.3 ms against 16.7), in v2.
- **A minor finding from Codex's review:** the typed look schema accepts numbers too large for a float (such as `1e40`). It is harmless for first-party presets; cap it when the schema is next regenerated.
- **C7, the capture guidance,** grows out of the recipe and stand-in work.
- **The founder's remaining look observations:** the floorboards shimmer little under FXAA, which does not bother the founder; no action.
