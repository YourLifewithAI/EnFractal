# Brief 17: the landscape's first build

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox, web search off).
**Branch:** `codex/17-landscape-first-build`, from `run2/integration` with the frozen harness (your checkout is already on it; the integrator commits your files).

**This is one side of a blind A/B test.** Another contender, a different AI model, gets this same brief at the same time, with its own folder, and neither sees the other's work. The founder judges the two landscapes from renders without knowing whose is whose. So build the best landscape you can, in your own way; do not hedge toward what you think the other side will do, and do not look for its work.

**Context.** EnFractal turns a scanned room into a believable landscape grounded in the room's layout, played by a 10 cm avatar. Read, in this order:
1. `docs/ROOM-TO-LANDSCAPE.md`: **the founder's design, your brief's real subject.** Guiding principles, not rigid rules: the founder warned that agents overfit to rules. "Water always runs downhill" and "tall mountains often have snow on them" are the kind of principle that must hold; a lake, castle or forest in every room is not required.
2. `docs/runs/RUN-2-REVISION.md`: the plan; your job is its "first step".
3. `pipeline/landscape/harness/README.md` and its reference package and renders (`pipeline/landscape/harness/reference/`): the landscape package you write (`write_package`) and the renderer you use. The reference is a format fixture, not a design target. The harness (materials by role, the prototype kit, cameras, sky, sun, haze, render settings) is shared by both contenders and fixed: you choose roles, tints, forms and where things go, not shaders or cameras. You may add your own prototype meshes to the package; they use the same shared roles.
4. `pipeline/landscape/corpus/README.md` and your room, `pipeline/landscape/corpus/rooms/garage_nominal/`: `room.json` (the shell, openings, spawns) and `inventory.json` (16 objects as posed boxes with kinds, confidences, colours and supports). **Never read `truth.json`:** it is the test oracle, and a real scan has no such file.
5. For grounding as needed: `docs/look/LOOK-BIBLE.md` (Tiny Glade's warmth; palette and seasons), the 10 cm body in `docs/ROOM-SCALE-DIRECTION.md`, and brief 12's spike (`docs/codex/reports/12-room-to-landscape.md`, its sheet): the last attempt, which the founder judged "still a room with lumps in it". Learn from it.

Never read `captures/`, the founder's Drive, or any photo of a real place.

## The job: the design's first build

Write a generator in **`pipeline/landscape/gen_a/`** that turns the synthetic garage into a landscape package and renders it through the harness. As the design's "First build" says: **one complete landscape: sky, horizon, connected ground, a ridge feeding a stream, planting and a small settlement with a reachable, carryable object. Simple generation and finished painterly surfaces.** The question it must answer for the founder: **does it feel like land, or still a room with lumps?**

- **The room supplies the tectonics.** Every inventory object and the shell become land (the design's "Geology"); the layout should stay recognisable to someone who knows the room, the land believable as if it formed there. Volume is the foundation, kind gives character where it is confident, colour comes through volume and kind into one palette, neighbours shape whole places. Then water, ecology and the signs of people give it a history.
- **The fixed setup answers:** gameplay mode `sandbox`, water `some`, latitude 30, `neg_z_bearing_deg` 0 (the window on wall B, +X, faces east), summer, 21 June, 09:00 solar time. Mostly wild with a few settlements; places where people live or lived, never people.
- **Play at 10 cm:** the body is 0.10 m tall, walks slopes up to 45° and steps up 0.02 m. Everything the landscape promises must be reachable from the player's spawn by **low-incline paths** (well under those limits), including the settlement and the carryable object, and the way back carrying it. Scenic peaks may stay out of reach if they look it.
- **Deterministic:** the same room, inventory and setup answers give identical package bytes. Seeded randomness is fine; record the seed.
- **Your own checks, run on your output:** no water running uphill or still water off level; nothing built floating or sunk; the walk from the spawn to the carryable object and back stays within the slope and step limits (a path search on your own terrain is enough; say how you measured it); every inventory object's footprint still reads in the land (report, per object, where its box went and how far the land's rise is from its box). Checks catch broken principles; they do not demand features.
- **Robustness, briefly:** run your generator on `garage_scan_17` too (sizes, places, labels and colours off), draft renders only, and say whether the place keeps its character.

**Render through the harness with `--expect-setup pipeline/landscape/harness/ab-setup.json`** (the README gives the commands). **Look at your own renders and iterate.** Draft renders (`--draft`) are cheap; use them freely. **Final-quality renders: at most three runs** (the founder's rule against duplicated work). The integrator re-renders your final package itself for the blind comparison, so your final render is for your own judgement.

**Budget:** aim to finish within about 90 minutes of work. If you run out of time, stop with what renders and say what you would do next. Web search is off for this job for both contenders; you have what you need.

## Deliver, in `pipeline/landscape/gen_a/`

- The generator, runnable as `python -m pipeline.landscape.gen_a.generate --room <corpus room folder> --out <package folder>` (standard-library Python plus Blender's bundled Python if you build geometry in Blender; nothing installed), and its tests (determinism and your checks), runnable with one command you give.
- `renders/`: your last final render's views, `sheet.png` and `receipt.json`, and the `garage_scan_17` draft sheet. No other binaries; nothing over 2 MB; **commit no package** (the integrator regenerates it).
- `REPORT.md`, about 400 words, **anonymous** (never name a model, a vendor or this A/B; the founder may read it blind): how the room became land, principle by principle; what you invented and why it is believable; the checks' results with the commands and their output; the per-object grounding table; what is weak, and what you would do next.

**Rules:** Blender headless only, never its window, never the GPU. Do not edit the harness, the corpus or anything outside your folder: if the harness blocks you or has a bug, stop and report it with the exact diff you propose (the integrator fixes it for both contenders). Make working folders with `os.makedirs`, never `tempfile`. UTF-8 and LF for text. Stop and report on any sandbox denial.

```scope
pipeline/landscape/gen_a/**
```
