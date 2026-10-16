# Brief 23: things you can pick up

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox, web search on).
**Branch:** `codex/23-loose-things`, from `run2/integration` (your checkout is already on it; the integrator commits your files).

## Context

You wrote the exporter (`pipeline/landscape/export/`, brief 18 and its follow-ups, brief 19): a landscape package becomes a game room. Read `docs/runs/RUN-2-OPEN-SEA.md` ("Things to touch").

The founder playtested the island garage and asked: "Can we start making more objects interactable? Like the pile of wood, for example." Eventually almost everything should be something the player can use.

**Today:**
- only a package object with `carriable: true` becomes movable (in the garage, the crates);
- a woodpile (`generator/life.py`: five logs, each a closed bark cylinder about 2.4 cm across and 10 cm long) is one fixed object;
- scatter is never movable.

**In the game:**
- the player is a capsule 10 cm tall with a 2 cm radius;
- F picks up or puts down the movable object it faces, and V pushes it;
- the player can carry 0.5 kg (`SandboxRules.PlayerCarryLimitKg`) and push 0.5 kg times `PushLimitFactor`; the Gubble carries 2 kg;
- a movable object needs an asset with `physics.movable: true`, a mass, and box collision (`contracts/asset.schema.json`, `contracts/room-manifest.schema.json`).

## The job

1. **A woodpile becomes its logs.**
   - Each log is its own movable object, exactly where it was drawn, stacked as before, with box collision, a plausible mass the player can lift, and a display name ("Log").
   - The split must be general: find the separate closed pieces of a prototype's mesh rather than hard-coding five logs, so a changed woodpile still splits.
   - The stack must sit still when the room loads. State how you checked that boxes rest on the log or ground below, without overlapping.
2. **Survey every prototype and scatter kind** the generator can emit. Decide which a 10 cm person could plausibly lift or push: loose stones, firewood, a small crate and the like. The principle is believability, not a rule. Then:
   - implement the clear cases;
   - list the unclear ones (lanterns, which are lights; fences; washing) with a recommendation, for the founder.
3. **A cap per room** keeps loose things within the frame budget, with a reason for the number. The existing entity cap (512) and the 2,048-file limit still hold.
4. **Nothing promised breaks:**
   - destinations, spawns and the sea extension stay as they are;
   - the room validates (`contracts/validate.py`);
   - the same package gives the same bytes;
   - the garage and the corpus (your tests and `pipeline/landscape/corpus`) still export.

You cannot run Godot. The integrator loads the room in the game and runs the landscape play check. So make the evidence the integrator needs easy to see: for each room, the number of loose things and their masses, and the woodpile's logs with their boxes.

## Tools

The exporter's own environment, as in brief 18. No installs and no paid services.

## Budget and delivery

**Budget:** about 60 minutes.

**Deliver:**
- the exporter changes and their tests;
- `docs/codex/reports/23-loose-things.md`, about 400 words:
  - what became movable, and why;
  - the survey table (kind, size, proposed mass, movable or not, reason);
  - the cap;
  - the commands with their raw output;
  - what the integrator must check in the game;
  - time spent.

**Rules:**
- Write only inside the scope below. If the generator should mark something differently, put the exact diff in your report for Lane C.
- Make working folders with `os.makedirs`, never `tempfile`.
- UTF-8 and LF.
- Stop and report on any sandbox denial.

```scope
pipeline/landscape/export/**
docs/codex/reports/23-loose-things.md
```
