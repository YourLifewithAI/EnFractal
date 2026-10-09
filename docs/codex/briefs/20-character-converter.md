# Brief 20: a drawing becomes a character (a blind A/B)

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox, web search on).
**Branch:** `codex/20-character-converter`, from `run2/integration` (your checkout is already on it; the integrator commits your files).

**Your folder:** `pipeline/characters/conv_a/`. Another contender builds the same thing, independently, in another folder. You never see each other's work. The founder and their kids judge blind.

## Context

EnFractal is a sandbox game. The player is a 10 cm avatar in a landscape made from their real room. The founder and their kids drew five characters for it. The founder's direction: "Every player should be able to make their own character." So the product is a **drawing-to-character converter**: a photo of a child's drawing plus a short description becomes a character you can play as.

You get two of the five drawings. **The other three are kept back,** and the winner's converter runs on them unchanged. So build a converter, not two models.

Read `AGENTS.md`, `docs/runs/RUN-2-STATUS.md` (the fourth session: "Characters from the family's drawings") and `pipeline/characters/turntable.py`.

## The inputs

`C:\dev\EnFractal-art\characters\contest\` (outside Git) holds two pairs:
- `cloudpuff.jpg` with `cloudpuff.txt`;
- `paw_creature.jpg` with `paw_creature.txt`.

Each pair is a phone photo of a pen or pencil drawing on paper (about 3000 × 4000 pixels) and the short description a player would type: a name, a line about it, the family's colours.

**Read only that folder** of `C:\dev\EnFractal-art\`. The paper, its shadows, the spiral binding and the handwritten names are part of the input; a real player's photo will have them too.

## The job

Write the converter in your folder. One documented command takes a photo and its description and writes, into an empty output folder, `character.glb`, `character.json` and the shared turntable's renders.

**What makes a good character (principles; the founder's and the kids' eyes judge, not a checklist):**
- **It is their drawing, come to life.** A child should recognise it at once: its shapes, proportions, features, expression and wobble. Keep its personality. Don't "correct" it into a generic model.
- **It is 3D and reads from every side.** The drawing shows one view. Invent the rest so it is believable and still theirs.
- **It wears the family's colours** from the description, placed where they make sense when the description doesn't say.
- **It fits the game:**
  - about 10 cm tall; the player's body is a capsule 10 cm tall with a 2 cm radius. Big paws, horns or a rainbow may reach beyond the capsule a little;
  - metres, Godot axes (+Y up, facing -Z), and the pivot at the bottom centre between the feet;
  - a self-contained GLB with no external files, and modest triangle counts.
  - The game paints characters with its own painterly shaders, so clean base or vertex colours read best. Ink outlines, if you want them, are geometry.
- **It is ready to move:**
  - separate, named parts with their pivots at the joints (body, head, arms, legs, eyes, props);
  - `character.json` lists the parts and their pivots, plus a motion hint read from the drawing (it floats, waddles, hops or strides). The game adds bouncy procedural motion later, so no rig is needed.
- **The drawing's pixels never go into the output:** no photo projected as a texture. The drawings must never reach Git, and the models may. Shapes, traced lines and colours derived from the drawing are fine.

**General, not tailored:**
- Nothing in the code may be specific to these two drawings. Any child's drawing of a figure or creature should give a charming result.
- Show that with at least one test drawing of your own (for example, lines drawn on white with Pillow).
- Reading a child's drawing (which marks are the body, the eyes, an arm, a prop, the handwriting or the paper) is the hard part. Any of these is fine:
  - do it with image processing;
  - make it an interpretation step a vision-capable AI performs from written instructions (in the product, the player's own AI would do it, and the description may carry hints);
  - mix the two.
- **If an AI performs a step:**
  - put its instructions in a file in your folder;
  - produce your two characters by following those instructions yourself, as written, and save the interpretations they produce;
  - make the rest of the converter take only the photo, the description and that interpretation.

  The integrator tests generality by giving those instructions to a fresh model on the held-out drawings.
- **Deterministic:** the same inputs give the same bytes. Any AI step is the exception; say what varies.

## Tools

- Python 3.14 (`C:\Python314\python.exe`) with numpy and Pillow.
- Blender 5.2.2 headless (`C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe`), with its own Python (bpy, numpy).
- Nothing else: no installs and no paid services. Web search is for research only; the converter itself runs offline.

## How it is judged

- **Render** each of your two characters with the shared turntable, and do not change it:

  ```
  blender -b --factory-startup -P pipeline/characters/turntable.py -- --glb <glb> --out <folder>
  ```

  The integrator renders both contenders' GLBs through it for the founder's blind sheet. Your own renders are for your report.
- **The founder and the kids judge from that sheet:** does it look like our drawing came to life, and would you want to play as it?
- **Then the winner runs unchanged** on the three held-out drawings.

## Budget and delivery

**Budget:** about 90 minutes of work. Render final quality only for your final versions.

**Deliver:**
- your folder: the converter, a `README.md` (the command, how it reads a drawing, its limits) and tests (determinism, your own test drawing, and output checks: height, pivot, named parts);
- your two characters in `C:\dev\EnFractal-art\characters\out\conv_a\<name>\` (outside Git): `character.glb`, `character.json`, any interpretation files, and the turntable renders;
- `docs/codex/reports/20-character-converter.md`, about 400 words:
  - how your converter reads a drawing, and why;
  - the commands with their raw output;
  - each character's height, triangle count and parts;
  - what is general and what you were unsure of;
  - what you would do next;
  - time spent.

**Rules:**
- Write only inside your folder (and `C:\dev\EnFractal-art\characters\out\conv_a\`).
- Make working folders with `os.makedirs`, never `tempfile`.
- UTF-8 and LF.
- Stop and report on any sandbox denial.

```scope
pipeline/characters/conv_a/**
docs/codex/reports/20-character-converter.md
```
