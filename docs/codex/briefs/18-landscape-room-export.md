# Brief 18: a landscape package becomes a game room

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox, web search on).
**Branch:** `codex/18-landscape-room-export`, from `run2/integration` (your checkout is already on it; the integrator commits your files).

**Context.** EnFractal turns a scanned room into a landscape the 10 cm player walks in. A generator writes a **landscape package** (`pipeline/landscape/harness/README.md`, "Package v1 spec"), which the shared harness renders in Blender for review. The founder chose the first landscape from those renders. The next step is to walk it in the game, and nothing turns a package into something the game loads yet. That is your job. Another agent is improving the generator at the same time (`pipeline/landscape/generator/`, moved from `gen_b/`); you do not touch it, and you depend only on the package format, which does not change in this brief.

The game already loads what you need. A room is a directory with `room.json` (`contracts/room-manifest.schema.json`), its listed files, and `objects/<asset_id>/asset.json` (`contracts/asset.schema.json`). A shell part's geometry may be a GLB mesh (`"kind": "mesh"`), and with `"collides": true` the game gives it trimesh collision (`game/scripts/native/Room/RoomData.cs`, `RoomBuilder.cs`: read both, including the GLB reader's limits). So a landscape can load as a room **without a contract change**.

Read: `AGENTS.md`; `docs/ROOM-TO-LANDSCAPE.md` (the design); `docs/runs/RUN-2-REVISION.md` (C5's acceptance: "a room manifest that validates, with collision from the same surfaces"); the harness README and `pipeline/landscape/harness/package.py` (use `read_package` to read; never reimplement validation); the two contracts; `contracts/validate.py`; `game/scripts/native/Room/**`; how the sandbox decides what can be carried (`game/scripts/native/Sandbox/**`); and an existing room writer for conventions (`tools/rooms/build_test_room.py`, and the scan exporter in `pipeline/roomscan/`). Never read `captures/`, the founder's Drive, or any real place's data.

## The job

Write **`pipeline/landscape/export/`**: `python -m pipeline.landscape.export --package <folder> --room <source room folder> --room-id <id> --out <empty folder>` writes a room directory the game loads, standard-library Python only.

- **Terrain** becomes colliding ground: shell parts with `"kind": "mesh"`, role `ground`, `collides: true`. The avatar walks on exactly the surfaces that are drawn.
- **Scenery** (`reachable: false`) becomes non-colliding backdrop. **Water** is drawn and does not collide (the stream bed beneath it is terrain); choose the closest existing role and say why.
- **Materials, for now:** the game does not yet read the harness's material roles or its `_ROLE_BLEND` attribute (Lane L will write that loader later). Write plain GLBs the game's importer shows sensibly: one primitive per harness role with a base colour taken from the harness's role library (`harness/worker.py`; keep the table in one place and say it mirrors the harness), the package's per-vertex tints and soft blends **baked into `COLOR_0`**, and **smooth `NORMAL`s** (shared vertices smooth, split vertices keep a crease). Pick each shell part's contract `material_role` as the closest match and record the harness role in the GLB material's `extras` so Lane L's loader can use it later.
- **Scatter and objects:** the package's populated `objects` (its `carriable` ones above all) become manifest objects with assets, named, with mass and size, so the sandbox's pick-up and the companion's fetch work on them as they do on the test room's doorstop. **Read the sandbox code to find what makes a thing carryable,** and make the carryable object carryable. For the dense `scatter` (grass, flowers, ferns, trees, rocks, buildings), decide what becomes an entity and what becomes static merged geometry, weighing the game's cost per node, the 20,000-instance package limit, and what the companion should be able to name ("the cottage", "the big boulder"). Small plants do not collide; trunks, boulders and buildings do, with simple shapes. Non-uniform scatter scales must come out right.
- **Spawns** come from the source room's spawns, lifted to the terrain under them (sample the terrain mesh). **The sky and sun:** carry the package's setup answers into the manifest's `site` where the contract has fields for them. Drop the source room's indoor lamps unless the contract requires them; lanterns may become light hints if the contract allows.
- **The edge of the world:** the avatar must not walk off the land or out of the room's bounds. Find what the game does at the bounds today. If the terrain alone does not close the playable area, say exactly where it is open, and propose (do not invent) the smallest contract or loader change that would close it, as a diff in your report.
- **Determinism and pins:** the same package and arguments give identical bytes; every file is listed with its hash as the manifest requires; canonical UTF-8/LF JSON.

**Tests** (in `pipeline/landscape/export/tests/`, runnable with one `python -B -S -m unittest ...` command from the repository root): the harness's reference package (`pipeline/landscape/harness/reference/package/`, with its room) exports, validates with `contracts/validate.py`, and is byte-deterministic; the exported terrain's triangles match the package's (count and bounds); spawns sit on the terrain; the carryable object is carryable by the rules you found; a broken input fails cleanly. **Then export a real generator's output:** generate the garage with `python -B -m pipeline.landscape.gen_b.generate --room pipeline/landscape/corpus/rooms/garage_nominal --out <empty folder>` (in your checkout `gen_b/` has not moved) and export it as room id `landscape_garage_nominal`. Report its file sizes, triangle counts, entity counts and the validator's output. Do not commit any exported room or package.

You cannot run Godot. If you can compile C#, try a small check that the GLB reader in `RoomData.cs` accepts your files, or say you could not. The integrator loads the room in the game.

**Budget:** about 60 minutes of work. Stop with what works and say what is left.

## Deliver

- `pipeline/landscape/export/` (the exporter, a short `README.md` with the command and the mapping table from package records to room records, and the tests).
- `docs/codex/reports/18-landscape-room-export.md`, about 400 words: the mapping and the decisions you made (with why), the commands with their raw output, the garage export's numbers, what the game will and will not show yet, and any change requests for the game (`game/scripts/native/Room/**` is Lane P's, the material loader Lane L's) or the contracts as exact diffs.

**Rules:** write only inside the scope below. Make working folders with `os.makedirs`, never `tempfile`. UTF-8 and LF. Stop and report on any sandbox denial.

```scope
pipeline/landscape/export/**
docs/codex/reports/18-landscape-room-export.md
```
