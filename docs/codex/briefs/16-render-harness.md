# Brief 16: a shared render harness for the landscape A/B

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox, web search on).
**Branch:** `codex/16-render-harness`, from `run2/integration` (your checkout is already on it; the integrator commits your files).

**Context.** EnFractal turns a scanned room into a believable landscape grounded in the room's layout: read [docs/ROOM-TO-LANDSCAPE.md](../../ROOM-TO-LANDSCAPE.md) (the founder's design) and [docs/runs/RUN-2-REVISION.md](../../runs/RUN-2-REVISION.md) (the approved plan). The design's first build is about to be made twice, as a blind A/B: two generators, written by different models, each turn the synthetic garage (`pipeline/landscape/corpus/rooms/garage_nominal/`) into a landscape, and the founder judges the renders without knowing whose is whose: **does it feel like land, or still a room with lumps?**

**Your job is the part both generators share,** so that the founder judges the generator and not the renderer: a landscape package format, its writer and validator, and a Blender renderer with fixed cameras, one sky and sun, fixed settings and a contact sheet. You do not build a generator. The harness must not favour any style of landscape.

Read first: `pipeline/landscape/corpus/README.md` (the 24 synthetic rooms and their formats), `contracts/room-manifest.schema.json` (`site`, `spawns`, `shell`, `openings`), `docs/look/LOOK-BIBLE.md` (above all "What handmade means per material role" and the reference frames' palette notes), and brief 12's spike for what worked in headless Blender here (`docs/codex/spikes/12-room-to-landscape/README.md`, `render_worker.py`: CPU Cycles, OIDN on the CPU, fixed threads and seed, metadata stripped). Never read `captures/`, the founder's Drive, or any photo of a real place.

## 1. The landscape package

A folder that a generator writes and the harness renders. It will later be what the game loads (Lanes L and P), so use **JSON for records and glTF 2.0 binary (`.glb`) for meshes** (Godot imports both), metres, and the room's frame (+Y up, −Z forward, floor at y = 0, the same coordinates as the room manifest).

It holds:
- **`package.json`:** a format id and version; the source room's id and the SHA-256 of its `room.json` and `inventory.json`; the **setup answers** (below); the generator's name and version (recorded, never printed on a render); every other file with its SHA-256; and a list of material roles used.
- **Terrain:** one or more triangle meshes, the whole walkable and visible land, including the ridgelines that replace the walls. A generator that thinks in heightfields writes them as meshes; overhangs and caves must be possible. **Material role per face** (one glTF primitive per role is the obvious way), an optional **per-vertex tint** (`COLOR_0`), and a way for ground roles to **meet softly** (grass fading into scree, rock into snow); you choose that encoding and document it.
- **Water surfaces:** meshes with a kind (`still`, `flowing`, `falling`), rendered with the shared water material.
- **Scenery:** distant land beyond the room's real boundary (the hazy horizon), flagged as not reachable.
- **Scatter instances:** trees, shrubs, grass, flowers, rocks and buildings, each a prototype reference with position, yaw, scale and an optional tint. Prototypes come from the harness's kit (below) or from the package's own meshes, which use the same material roles.
- **Populated objects:** the things to find, carry and use, each with an id, kind, prototype, bottom-centre position, yaw, size in metres, mass in kilograms and whether it can be carried.
- Anything else a generator wants to keep (paths, debug data) under `x_` keys, which the harness ignores.

**The setup answers** are the founder's questions before generation: `gameplay_mode` (`sandbox` for now), `water` (`none`, `a_little`, `some`, `plenty`), `latitude_deg`, `neg_z_bearing_deg` (as in the manifest's `site`: the compass bearing of the room's −Z, clockwise from north), `season`, a day of the year and a local **solar** time for the render. Longitude is not needed for a sun from solar time, and the manifest deliberately refuses it (AGENTS.md strips location data), so leave it out. **The A/B's fixed answers,** which your reference package uses too: sandbox, water `some`, latitude 30, `neg_z_bearing_deg` **0** (the synthetic garage's window is on wall B, +X, so this makes it face east, as the real garage's windows do), summer, 21 June, 09:00 solar time.

Give it **a small writer and reader** (`pipeline/landscape/harness/`) that a generator imports: standard-library Python only (it must run on the system Python 3.14 and inside Blender's), plain lists in, byte-deterministic files out. And **a validator** that rejects what would make a render lie or the game choke: wrong units or frame, NaN or infinite values, unknown roles or prototypes, missing or mismatched hashes, degenerate triangles, a package bigger than sensible limits (you set them, for example 2 million triangles and 50 MB), objects outside the room's bounds. It checks the format, not the design: no rules about what a landscape should contain.

## 2. The shared look: materials and prototypes

**Decided: the material library is the harness's, shared by role, and contenders may tint it per vertex.** The Blender renders are a judging tool; the game's own surfaces come later from the Look lane's Godot shaders. So the A/B should compare form, composition and colour, not who wrote the better Blender shader. Build **one painterly material per role**, following the look bible's handmade guidance (stylised, never photographic; soft value and temperature variation, brush-like marks, gentle pastel softening): at least grass or meadow, soil, worn path, sand or gravel, scree, rock (showing **strata** on cut faces, for example bands by height), cliff, moss, snow, still water, flowing water, foliage, bark, timber, stone masonry, roof, and a few for populated objects (wood, stone, cloth, metal). Tint shifts a role's colour (so a blue couch can become slate) and keeps its marks. A Kuwahara or similar painterly pass in Blender's compositor is allowed if it is deterministic and the same for every package.

**A small prototype kit:** a broadleaf tree, a conifer, a shrub, a grass tuft, a flower clump, a fern, a rock, a boulder, a cottage, a tower or keep, a wall or fence piece, a crate, a lantern. Simple, rounded, Tiny Glade in spirit, built in code like the recipes (no downloaded models), each with its pivot at bottom centre and its size in metres. Keep them plain enough that a landscape is judged on where things are, not on one hero asset.

## 3. The renderer

`python -m pipeline.landscape.harness.render --package <folder> --room <corpus room folder> --label "<text>" --out <folder> [--draft]`, launching **Blender 5.2.2 headless** (CPU only: `--background --factory-startup`; the executable from `BLENDER`, else under `C:\Users\blues\AppData\Local\Programs\Blender`, as `pipeline/recipes/build.py` finds it). It renders only the package and the sky: no room shell.

- **Cameras, fixed from the room manifest, never from the package,** so both contenders get identical views:
  - **two or three overviews:** high three-quarter views (the look bible's diorama view) framing the room's whole footprint from fixed corners;
  - **two or three eye views at 10 cm:** the eye at 0.087 m above the ground (the body profile in `docs/ROOM-SCALE-DIRECTION.md`), at the manifest's player and companion spawns looking along their yaw, and one more you choose (for example the room's centre looking towards the window). Each takes its (x, z) and direction from the room and its height from **a ray cast down onto the package's terrain**, so a hill where the spawn was lifts the camera with it; report the ground height used and flag an eye that would sit inside geometry.
- **A 10 cm scale figure,** the same in every view, standing at the player spawn's ground point.
- **One sky and sun from the setup answers:** compute the sun's direction from latitude, day and solar time (the NOAA solar position algorithm is fine; test it against a published value), turn it into the room's frame with `neg_z_bearing_deg`, and light with a stylised, painterly sky (not photographic) whose colour follows the hour and season, a soft-edged sun and the sky's fill. Add the same depth-based **haze** for every package, so the scenery fades into the horizon.
- **Fixed settings** for every package: resolution (final at least 1280 × 720; `--draft` small and quick for a generator's iteration), samples, seed, a fixed thread count, OIDN on the CPU, a fixed depth of field (a gentle tilt-shift on the overviews, the near ground and the figure in focus at 10 cm), PNG with metadata stripped. Aim for a full final set in under ten minutes on this machine; report the times.
- **Byte-deterministic:** the same package, room and settings give identical PNG bytes on this machine. Prove it with a test.
- **A contact sheet** (`sheet.png`, under 2 MB): every view, labelled with its view name, the `--label` text and the setup answers, **never the generator's name**: the founder judges blind.
- **A receipt** (`receipt.json`): Blender's version, settings, camera positions, ground heights, the sun's altitude and azimuth, per-view seconds and bytes, the package's hash.
- **Should have:** a diagnostic overview coloured by slope (walkable, climbable, too steep; take the thresholds from brief 12's report and mark them assumed), outside the founder's sheet, so a generator can check its low-incline paths.

## 4. A reference package and tests

- **A tiny reference package** for `garage_nominal`, built by a script in the harness folder, that exercises every part of the format: terrain with several roles, tints and a soft role transition, a still pool, a flowing stream and a small fall, scenery, scatter of every kit prototype, one custom prototype, a populated object. It is a **test fixture, not a design attempt:** keep it visibly simple, so it does not prime either contender. Put it in `pipeline/landscape/harness/reference/` (pinned `-text` in `.gitattributes`, so Git keeps its bytes and its hashes hold on any machine) with its final renders, sheet and receipt, the package under 1 MB.
- **Tests** in `pipeline/landscape/harness/tests/`: the writer's bytes are deterministic; the validator rejects each kind of bad package; the sun matches the published value; cameras come from the manifest and eye heights from the ray cast; the render is byte-identical twice (a small resolution is fine). Tests that need Blender **skip cleanly** when it is absent (the Linux suite has none); the rest use the standard library only. Give the exact command to run them (the corpus README explains the namespace-package quirk).
- **`pipeline/landscape/harness/README.md`:** the package format as a short spec, how a generator writes and renders a package, the material roles and the kit with a picture, and the fixed settings.

## Report

In `docs/codex/reports/16-render-harness.md`, about 300 words of summary, then evidence (commands, exit codes, the lines that matter): what you built, the format's decisions and why, render times, what you could not verify. **And as a colleague:** if anything in this brief would make the A/B unfair or the harness a poor base for the game later, say so and propose the change.

**Rules:** Blender headless only, never its window, never the GPU. Make working folders with `os.makedirs`, never `tempfile`. UTF-8 and LF for text. No binary over 2 MB; the only `.glb` files you commit are the reference package's. Do not edit `pipeline/recipes/` or the corpus (import or copy what you need). Stop and report on any sandbox denial.

```scope
pipeline/landscape/harness/**
docs/codex/reports/16-render-harness.md
```
