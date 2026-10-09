# Brief 19: trees you can climb

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox, web search on).
**Branch:** `codex/19-climbable-trees`, from `run2/integration` (your checkout is already on it; the integrator commits your files).

**Context.** You wrote the exporter (`pipeline/landscape/export/`, brief 18 and its two follow-ups): a landscape package becomes a game room. The founder playtested the garage landscape and asked for "climbing cliffs and climbing trees". Read `docs/runs/RUN-2-CLIMB-SWIM.md` first. Lane P is building climbing in the game this round: any colliding surface too steep to stand on can be climbed, and at the top the body pulls itself over onto standable ground.

**Trees today:** `colliding_parts` keeps only a tree's bark cone as collision (`scatter_solid`). The leafy crowns are non-colliding backdrop. In `harness/kit.py` a broadleaf's trunk reaches about 56% of the tree's height and a conifer's about 35%, both well inside the leaves. So a climber could go up a trunk into the leaves, then have nothing to climb and nothing to stand on.

## The job

Make every exported tree climbable to the top:

1. **A hidden climbing pole:**
   - a simple collider that continues the trunk, at about its top radius, up through the leaves to just below the crown's top;
   - collision only, never drawn;
   - a climber goes up the trunk, then the pole, through the leaves.
2. **A top you can stand on:**
   - the crown's upper surface collides, so the climber comes out on top and stands there. Low-gravity leaps can land on crowns too;
   - make it a **one-sided** collider: faces pointing outward and upward, so a body coming up from inside passes through. The game builds trimesh collision with `backface_collision` off; read `game/scripts/native/Room/RoomBuilder.cs` and state what you rely on;
   - **only the upper part of the crown:** a walker must not snag on low foliage. The avatar is 10 cm tall, and today it walks freely under every tree; keep that. Conifers' low skirts in particular stay non-colliding;
   - simplified geometry is welcome (a coarse cap per crown rather than every foliage triangle).
3. **Shrubs, grass, ferns and flowers stay decorative.** Boulders, rocks and landmarks keep their current collision.
4. **Shapes:**
   - choose the shell parts (for example `scatter_tree_climb`, role `ground`, `collides: true`) and say why;
   - keep the 128-shell-part and file limits;
   - the visuals do not change;
   - output stays byte-deterministic.
5. **The README:** update the export README's mapping table. Fix its stale "no bounds barrier" note: the game's body now enforces the room's bounds (`SmallPlayerController.cs`, `WithinBounds` and `HoldInsideBounds`).

**Tests** (in `pipeline/landscape/export/tests/`, with the existing one-command `unittest` run):
- every tree in the reference package gets a pole and a cap;
- the pole runs from the trunk to below the crown top;
- the cap's faces all point outward and upward;
- no cap triangle sits lower than a 10 cm walker's head above the ground under that tree, measured from the terrain there (choose the clearance and say why);
- shrubs get nothing;
- determinism holds.

Then export the generator's current garage (`python -B -m pipeline.landscape.generator.generate --room pipeline/landscape/corpus/rooms/garage_nominal --out <empty folder>`, then the exporter as room id `landscape_garage_nominal`). Report the tree count, the added triangle count, the file sizes and the validator's output. Do not commit any exported room or package.

You cannot run Godot. Lane P verifies the climb in the game, and the integrator loads the room.

**Budget:** about 45 minutes of work. Stop with what works and say what is left.

## Deliver

- The exporter change, its README and its tests.
- `docs/codex/reports/19-climbable-trees.md`, about 300 words:
  - the colliders and why;
  - the clearance chosen;
  - the commands with their raw output;
  - the garage's numbers;
  - change requests for the game (`game/scripts/native/Room/**` is Lane P's), as exact diffs.

**Rules:** write only inside the scope below. Make working folders with `os.makedirs`, never `tempfile`. UTF-8 and LF. Stop and report on any sandbox denial.

```scope
pipeline/landscape/export/**
docs/codex/reports/19-climbable-trees.md
```
