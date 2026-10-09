# Brief 22: the characters' second round

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox, web search on).
**Branch:** `codex/22-characters-round-2`, from `run2/integration` (your checkout is already on it; the integrator commits your files).

## Context

You won brief 20. The founder and their two kids judged five characters blind, and your converter (`pipeline/characters/conv_a/`, now merged) won every character, every vote. Read your own README, `INTERPRET.md`, `FORMAT.md` and report, then `docs/runs/RUN-2-STATUS.md` ("The fifth session's close": the vote and the family's notes; "The fifth session": what the readers found unclear).

This round makes all five characters what the family pictured. It also makes the converter better for every player's drawing, not only these five.

## The family's notes

- **Cloudpuff:** keep yours, with the other converter's body thickness.
- **Stickbear** (the paw creature now has a name): keep yours, with the other converter's colour palette.
- **The Gubble:** yours rendered better, but it "didn't come out as envisioned". It should be clear like a soap bubble: it shimmers, and its outline is visible, "but that's it". Its colour, which you drew as lines on a white model, "is meant to be more of an aura".
- **Potato Man:** keep yours, with more shape and width, as for Cloudpuff, and golden skin.
- **Tomato Man:** keep yours. The family likes the little sparkles, ideally moving. Make it wider, with more of a real tomato's shape.

The other converter is `conv_b`, a Claude Opus agent's, on branch `run2/characters` (worktree `C:\dev\EnFractal-run2\characters`, read only). Its outputs are in `C:\dev\EnFractal-art\characters\out\conv_b\<name>\`. Learn from how it inflates bodies (fuller depth and width), and read its colours for Stickbear. Don't copy its approach wholesale: yours won.

## The job

1. **Fuller bodies, in general.** Rounded volumes have real depth and width, like a plush toy rather than a cookie, while the silhouette from the front stays the drawing's. Make this the converter's default for any drawing, not a per-character setting.
2. **Effects as their own parts:**
   - sparkles, glows, motion marks and similar effects become separate named parts, kind `effect` in `character.json`, with their pivots, so the game can animate them later;
   - describe their intended motion as a hint (for example twinkle, orbit or drift);
   - the readers left the Gubble's floating sparkles out; include them now.
3. **Material hints for what the game paints:**
   - a part can say it is see-through (for the Gubble's body: `bubble`, clear with a shimmering rim) and carry an aura colour;
   - put the hint in `character.json` and in the GLB (material name and glTF `extras`), with a sensible opaque fallback colour;
   - the game's Look lane draws the bubble in its own shader later, so you only describe it.
4. **The reading instructions** (`INTERPRET.md`, `FORMAT.md`) close the gaps the fresh reader found:
   - whether tube ends are flat or rounded;
   - colour bands pinching where volumes thin out;
   - whether every mark needs its own child part;
   - several sketches on one page (pick the clearest, and say which);
   - loose effect marks;
   - feet at different heights;
   - see-through things.

   Keep them general and short.
5. **Re-read all five drawings** with your updated instructions. Save the interpretations, then convert all five. Inputs are in `C:\dev\EnFractal-art\characters\round2\`: `<name>.jpg` and `<name>.txt` for `cloudpuff`, `stickbear`, `gubble`, `potato_man` and `tomato_man`. The family's notes above are this round's direction, beside each description. Read only that folder, `out\` and `readings\` under `C:\dev\EnFractal-art\characters\`.
6. **The turntable** (`pipeline/characters/turntable.py`) may now honour the see-through hint, so the family can see a bubble on the sheet: for example glass-like transmission with a thin-film shimmer in Cycles on the CPU. Opaque characters must render exactly as before. Check one round-1 GLB before and after, and say how.
7. **A sheet per character** for the family: round 1 (your brief 20 output, in `out\conv_a\`) beside round 2, front and three-quarter views, labelled "Before" and "After".

**Principles, not a checklist:** it is still their drawing, come to life. Don't smooth away the wobble, the expression or the asymmetry. "Wider" and "rounder" mean the volume, not a different character.

## Tools

- Python 3.14 (`C:\Python314\python.exe`) with numpy and Pillow.
- Blender 5.2.2 headless (`C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe`).
- No installs and no paid services. Web search is for research only; the converter runs offline.

## Budget and delivery

**Budget:** about 2 hours. Iterate with `--no-render`, and render final quality only for your final versions.

**Deliver:**
- your converter changes, with tests: determinism, fuller depth on your own synthetic drawing, effect parts and the material hint in both the GLB and `character.json`, height and pivot;
- **outputs** in your checkout under `captures/characters-out/round2/<name>/` (Git ignores `captures/`; the integrator copies them out): `character.glb`, `character.json`, `interpretation.json`, the turntable renders, and `sheet.png`;
- `docs/codex/reports/22-characters-round-2.md`, about 400 words:
  - what changed in the converter and the instructions, and why;
  - the commands with their raw output;
  - for each character: height, width, depth, triangle count, parts and effect parts;
  - how the turntable change keeps opaque renders identical;
  - what you were unsure of;
  - time spent.

**Rules:**
- Write only inside the scope below. Never copy a drawing or a crop of one into your checkout; the outputs carry no photo pixels.
- Make working folders with `os.makedirs`, never `tempfile`.
- UTF-8 and LF.
- Stop and report on any sandbox denial.

```scope
pipeline/characters/conv_a/**
pipeline/characters/turntable.py
docs/codex/reports/22-characters-round-2.md
```
