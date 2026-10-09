# Brief 24: the mock host's Gubble floats

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox).
**Branch:** `codex/24-mock-floating-gubble`, from `run2/integration` (your checkout is already on it; the integrator commits your files).

## Context

The companion server (`companion/`) has a **mock host**, `companion/src/enfractal_companion/mock_host.py`, a Python stand-in for the game that the boundary tests and AI clients run against. It must behave like the real game host (`game/scripts/native/Kernel/`, `game/scripts/native/CompanionAvatar.cs`) wherever a client can observe it.

The game's companion changed in Run 2:
- it is now called **the Gubble**;
- it **floats**: it hovers 2 cm over ground or water, with a 4 mm bob every 2.6 s, and never climbs or swims;
- when the walking route doesn't reach a goal, it floats straight there, up and over faces;
- `target_unreachable` still comes after 5 s blocked.

Read `docs/runs/RUN-2-STATUS.md` ("The sixth session": Lane P part 2 and the change request for Lane A's mock) and `AGENTS.md`. Lane A owns `companion/`, but it is not running, so this brief applies its change request.

## The job

Apply the change request, as the real host behaves:
1. `"Wisp"` becomes `"the Gubble"` (`mock_host.py`, the avatar name, about line 453). An old save or profile that names "Wisp" should still read as the Gubble, if the mock reads names anywhere.
2. Add `HOVER_M = 0.02`. Add it to the companion's spawn y, and in `_fetch_returned` make `player[1]` become `_r(player[1] + HOVER_M)`. Comparisons of the companion's y need ±5 mm for the bob, if the mock models a bob at all; say whether it does.
3. Fixtures that use an open pen or a wall to produce `target_unreachable` should use a closed pen, because a floating Gubble rises over a wall.
4. Arrival puts the companion level with its target: about 0.32 m after a fetch from the 30 cm box top.
5. No contract change. If a test seems to need one, describe it in your report instead.

Check every test and fixture that depends on the old name, the old height or the old walls. Keep the mock's boundary checks exactly as strict as they are: change behaviour, never a guard.

## Tools

The integrator's companion environment, with your checkout's source first on the path (as `tools/linux/test-all.sh` does): `$env:PYTHONPATH = "$PWD\companion\src"; C:\dev\EnFractal\companion\.venv\Scripts\python.exe -B -m unittest discover -s companion/tests`. The tests use `unittest`, not pytest. The integrator's baseline in your checkout: `Ran 625 tests`, `OK (skipped=54)`. No installs, no network, no paid services. `test_real_host.py` needs the game and will skip or fail without it. Say which, and don't change it to pass.

## Budget and delivery

**Budget:** about 45 minutes.

**Deliver:**
- the mock and test changes;
- `docs/codex/reports/24-mock-floating-gubble.md`, about 300 words:
  - what changed;
  - the full test command with its raw last lines (passed and failed counts, before and after);
  - anything in the mock that still differs from the real host's floating Gubble;
  - time spent.

**Rules:**
- Write only inside the scope below.
- Make working folders with `os.makedirs`, never `tempfile`.
- UTF-8 and LF.
- Stop and report on any sandbox denial.

```scope
companion/src/enfractal_companion/mock_host.py
companion/tests/**
docs/codex/reports/24-mock-floating-gubble.md
```
