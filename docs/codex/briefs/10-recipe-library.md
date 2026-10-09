# Brief 10: the recipe library

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox, web search on).
**Branch:** `codex/10-recipe-library`, from `run2/integration` (your checkout is already on it; the integrator commits your files).

**Context.** Run 2 turns the founder's scanned garage into a playable room ([RUN-2.md](../../runs/RUN-2.md), Lane C). The capture lane's inventory (C4) will give each object a **kind**, a **3D box** (its size in metres) and its **broad colours**. A stand-in is then built for it from a **recipe**: a generic, parametric Blender script for one kind of object, sized and coloured from those numbers. The founder's rule: an object is faithful in kind, size, place and broad colour, and stylised in its detail. Exact replicas are out of scope, and no object is photographed on its own.

The recipes are also what a player's own AI will run on the player's computer, so they must work for any capable AI with nothing hosted and nothing paid. You built a cardboard box and a jam jar this way in brief 09: read [its report](../reports/09-blender-spike.md) and [its notes](../spikes/09-blender/README.md) first, and build on that code.

**Build `pipeline/recipes/`**, a new directory, with one recipe for each of the founder's five objects:
1. a cardboard box (flaps closed, open or partly open as a parameter);
2. a couch with wooden legs (seat and back cushions, arms; the cushion count as a parameter);
3. a gaming laptop (closed, or open at an angle given as a parameter; a keyboard and a screen, no brand marks);
4. an empty jam jar of the Bonne Maman kind (a faceted glass body, a threaded neck, a lid whose pattern is a parameter, gingham by default; no label or brand text);
5. a metal French press (a glass or metal beaker, a frame, a handle, a lid with a plunger knob).

**One input shape for every recipe.** A recipe takes a small JSON document:
- `recipe`: the recipe's name;
- `size_m`: `[width, height, depth]` in metres, Y-up, the object's outer box. The built object's bounds match it to within 1 mm;
- `colours`: named slots, each an sRGB `#rrggbb` (for example a couch's `upholstery` and `legs`). Every slot has a default, and a recipe with only `size_m` given still builds;
- `params`: optional settings for the recipe (flap angle, cushion count, lid angle, and so on), each with a default and a valid range. Defaults scale with `size_m`, so a recipe given only a size builds something sensible.

Document every recipe's slots and params in `pipeline/recipes/README.md`, and reject unknown keys and out-of-range values with a clear message rather than guessing.

**One output shape for every recipe**, in an output folder the caller names:
- a `.glb`: metres, Y-up, the pivot at the bottom centre, an identity root, embedded resources only, every part named;
- one material per colour slot, named after the slot, with a material role from `contracts/common.schema.json` (`material_role`) recorded for it;
- a receipt JSON with the inputs as used (each value marked as given or as the recipe's default), the Blender version, the triangle count, the bounds, the GLB's SHA-256, the run time, the checks and their results, and simple collision shapes (boxes or cylinders, in the asset's coordinates) that a capture lane could turn into an asset's collision. Do **not** write an `asset.json`: the Capture lane owns the asset package. Say in your report how each receipt field maps to `contracts/asset.schema.json`;
- a small preview PNG (no larger than 640 × 480), rendered on the CPU.

**Keep the detail stylised and light.** Clean shapes with small bevels, flat colours or simple procedural patterns baked into small embedded images; no photo textures. Aim for no more than 10,000 triangles a small object and 20,000 for the couch, and report what each recipe produces. The Look lane adds its own wobble and restyle later, so do not try to make the objects painterly.

**The checks before export, as code shared by every recipe** (brief 09's notes are the starting list): finite bounds that match `size_m`; bottom-centre pivot and identity root; no degenerate triangles; each closed part manifold with positive volume; materials named after their slots; the decoded GLB re-read and compared; and `contracts/validate.py`'s `check_glb`. A recipe whose checks fail exits non-zero and writes no GLB.

**A runner and tests.**
- A command-line entry point, `python pipeline/recipes/build.py <input.json> --out <folder>`, using only Python's standard library. It finds Blender from the `BLENDER` environment variable, falling back to the founder's install at `C:\Users\blues\AppData\Local\Programs\Blender`, and runs it headless with `--background --factory-startup` and Blender's user config and temp directories pointed into the output folder or your temp folder.
- Tests that build every recipe at its defaults and at two other sizes (one small, one large), check the bounds and the checks, and check that bad input is refused. Put them under `pipeline/recipes/tests/`, runnable with `python -m unittest` from the repository root with no installs. Report how long the whole set takes.
- **Determinism:** report whether the same input builds the same GLB bytes twice. Make it so if that is cheap; say what stops it if not.

**Look things up** (web search is on) to choose sensible defaults for each object's proportions, such as a three-seat couch's seat height or a 1-litre French press's height. Cite each source, or mark a value as assumed.

**Rules that apply here:**
- Blender headless only, as in brief 09. Never open its window and never use the GPU: render with Cycles on the CPU, or Workbench, at small sizes.
- Every text file is UTF-8 with LF line endings, including the JSON you generate (brief 09 tripped on `Path.write_text`'s newline conversion on Windows; pass `newline='\n'`).
- **Commit no GLB.** They rebuild from the recipes. You may commit one preview PNG per recipe, at its defaults, under `pipeline/recipes/previews/`, each under 400 KB.
- Do not edit the spike's files in `docs/codex/spikes/09-blender/`: copy what you reuse.
- Touch nothing of a real place: no photos, no `captures/`, no Drive.

**Report** in `docs/codex/reports/10-recipe-library.md` (about 250 words of summary, then raw evidence):
- a table of the five recipes at their defaults: triangles, bounds, GLB bytes, build seconds;
- the test run's command and its last lines;
- the receipt-to-`asset.schema.json` mapping;
- what an AI calling these recipes needs to be told, as notes towards the capture guidance (C7), and which parts of a recipe a player's AI should never be allowed to change (paths, the Blender command line);
- what was hard, and what you did not finish.

```scope
pipeline/recipes/**
docs/codex/reports/10-recipe-library.md
```
