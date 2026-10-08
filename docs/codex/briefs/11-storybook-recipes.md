# Brief 11: give the recipes the storybook style

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox, web search on).
**Branch:** `codex/11-storybook-recipes`, from `run2/integration` (your checkout is already on it; the integrator commits your files).

**Context.** Brief 10 built five parametric Blender recipes in `pipeline/recipes/` (read [its report](../reports/10-recipe-library.md) and `pipeline/recipes/README.md` first). Its brief asked for plain, accurate shapes and said the Look lane would add the style later. The founder looked at the five previews on 7 October and judged them **basic**:
- the proportions look mostly correct, but **the laptop looks off**, perhaps from the perspective;
- the French press and the glass look **grainy**;
- **nothing about them is stylised or charming.** "If the point of the exercise is to reproduce an object, I'd say that's been achieved." It is not the point.

The point is the founder's objective for Run 2: a scanned room becomes a space that is **faithful in kind, size, place and broad colour, and stylised in its detail.** The founder chose (7 October) to put that style **in the recipes**, so every stand-in, and every recipe a player's AI writes later, starts charming.

**Read [the look bible](../../look/LOOK-BIBLE.md) first, all of it.** The target is one stylised storybook style in the spirit of Tiny Glade: cozy and fanciful, cottage-core, handmade, never photorealistic; a real room that looks like **a handmade toy diorama on a tabletop**, seen by a 10 cm person; **soft, slightly wonky, rounded forms instead of sharp, sterile ones**; a subtle tactile feel, as if made by hand. Its table "What handmade means per material role" says how each material should read. You may look up Tiny Glade and similar stylised games to understand the shape language; cite what you use. You may not read the founder's Drive or any photo of a real place.

**Build a shared style layer** in `pipeline/recipes/` that every recipe uses, so a new recipe gets the style by default. The storybook style becomes the default; keep the plain build available as a parameter (for example `style: "plain"`) so the two can be compared. What the style should do (use your judgement, and say what you chose and why):
- **Soft, rounded forms.** Generous rounding sized to the object, not a 0.7 mm edge bevel: pillowy cushions, rounded box edges and flaps, a softened laptop shell, a rounded lid and knob.
- **A slight wonk.** Small, deterministic irregularities: a lean, a taper, uneven cushions, a flap that sits askew, a softly bowed panel. Seed them from the input, so the same input still builds the same bytes, and keep the object's bounds within `size_m` (to 1 mm), so the stand-in still fits its scanned box.
- **Toy-like, chunky proportions** within that box: thicker panels, lids, legs and handles; fewer, larger details (larger keys, a chunkier handle); the object's telltale features a little exaggerated (the jar's facets and gingham lid, the press's plunger knob and frame). A viewer should still know at once what each object is.
- **Colour that reads as painted:** faithful to the given colour slots, flat or gently varied, never photographic. The game's style preset adds the brushwork per material role, so keep each material's role and base colour honest.
- **Stylised glass:** in a toy diorama, glass reads as thick, tinted and softly translucent, not as physically exact refraction. Choose the look that will survive the game's renderer (glTF transmission, or a tinted alpha surface), and say which.

**Fix the laptop.** Work out why it looks off (the camera's lens, the lid's angle, the base's thickness, the screen's bezel, where the keyboard sits) and fix the causes, in the recipe or the preview.

**Previews the founder can judge:**
- grain-free: Cycles on the CPU with denoising, or another CPU renderer that gives clean images;
- a longer lens than now (the review cameras use about 30 degrees), a high three-quarter view, warm soft light, so the shape reads as a miniature;
- the five storybook previews replace the old ones in `pipeline/recipes/previews/` (512 × 384 or larger up to 640 × 480, each under 400 KB);
- one comparison sheet, `pipeline/recipes/previews/plain-vs-storybook.png` (under 800 KB): each object plain and styled, side by side.

**Keep what brief 10 built working:** the same input and output shapes, the checks before export, the triangle budgets (raise them only if the rounding needs it, and say by how much), byte determinism for all five, and the tests (add tests for the style layer: bounds still fit, determinism holds, `plain` still builds). Run the whole test set once at the end.

**Rules that apply here:**
- Blender headless only, as in briefs 09 and 10. Never open its window and never use the GPU.
- Make working folders with `os.makedirs`, never `tempfile` (see the Codex README).
- Every text file UTF-8 with LF line endings. Commit no GLB.
- Touch nothing of a real place: no photos, no `captures/`, no Drive.

**Report** in `docs/codex/reports/11-storybook-recipes.md` (about 250 words of summary, then raw evidence):
- per recipe, what the style changed, with triangles, GLB bytes and build seconds before and after;
- what was wrong with the laptop, and the fix;
- the test run's command and its last lines;
- notes for the capture guidance (C7): how a player's AI should apply the style to a new recipe, in a few rules;
- what you are unsure of, for the founder to judge from the comparison sheet.

```scope
pipeline/recipes/**
docs/codex/reports/11-storybook-recipes.md
```
