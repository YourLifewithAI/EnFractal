# A drawing becomes a character

A photo of a child's drawing, its short description and a **reading** of the drawing become a 3D character for
EnFractal: `character.glb`, `character.json` and the shared turntable's renders.

## The command

```
python <this folder>/convert.py --photo <drawing.jpg> --description <about.txt> \
    --reading <reading.json> --out <empty folder>
```

- Python 3.14 with numpy and Pillow; Blender 5.2.2 (the path is built in; set `BLENDER` to override). Offline.
- Writes into the empty folder: `character.glb`, `character.json`, `reading.json` (a copy of the reading),
  `build_plan.json` (the measured shapes, as balls in metres) and the turntable's `front.png`, `three_quarter.png`,
  `side.png`, `back.png` and `turntable.png`, rendered by the unchanged `pipeline/characters/turntable.py`.
- `--no-render` skips the turntable; `--debug-overlay <png>` draws what was found over the drawing, for checking a
  reading (it shows the drawing, so write it to scratch, never into Git).
- About 15 to 30 s per character before the turntable.

## How it reads a drawing

Reading a child's drawing has two halves, and each is done by what is good at it.

1. **What the marks are** is a judgement: which closed area is the body, which squiggle is a blush, which line is a
   tail, what is handwriting, which colour goes where. A vision-capable AI does this by following
   [READING.md](READING.md) and writes `reading.json`. It points *into* areas (a seed point inside the blank paper
   of each area) and puts boxes around lines; it never traces shapes. `grid.py` draws a labelled grid over the photo
   so positions can be read off it.
2. **The exact shapes** are measured from the photo (`drawing.py`, numpy and Pillow only):
   - the paper is normalised against its own local brightness, so shadows and uneven light drop out;
   - lines are found at a firm level first, then fainter pencil if the firm reading leaves areas open;
   - each seed floods out to the drawn lines, bridging pencil gaps of up to 7 working pixels; an area that leaks to
     the figure's edge, grows too big, or swallows another piece's seed is retried with wider bridging;
   - lines in a box are thinned to centrelines (Zhang-Suen), and solid filled areas (pupils) are peeled off them.

The 3D comes from the drawing's own outline. Every point inside an area is the centre of the largest disc that fits
there. Each disc becomes a ball whose depth grows a little faster than its width. The union of those balls (Blender
voxel remesh, smoothed and reduced) is a rounded, plush body: from the front it is exactly the drawn silhouette with
all its wobble, and from the side and back it is believably round. Wide flat bodies (a cloud) get extra depth. Limbs,
horns and props continue inside the part they grow from, so joints are solid. Stickers (eye whites, irises, pads)
and ink lines (mouths, brows, eye outlines, blush) are laid on the front surface by casting rays, in the order the
reading stacks them. A part grows under its own stickers. A banded area (a rainbow) is split into stripes that follow
its shape.

Feet are pushed forward so the character stands. Everything is scaled to 10 cm tall, with the origin at the bottom
centre between the feet. The GLB holds Godot axes (+Y up, facing -Z), one node per part with its origin at the part's
pivot (its joint with its parent, or a feature's own centre), parented like the reading, and plain base colours.
There are no textures: no pixel of the photo goes into the output.

## Limits

- The reading is the step that varies: two readers place seeds differently (shapes barely change, because they come
  from the lines) and may make different calls on colours or on what a line is. READING.md asks them to write those
  calls down. Everything after the reading is deterministic: the same photo, description and reading give the same
  bytes (tested).
- Only what the drawing shows from the front is measured; depth is invented by the inflation, so a character drawn
  side-on comes out as a flat-ish relief of its profile.
- Areas filled with heavy pencil shading cannot be seeded; the reading uses `silhouette` with a rough outline (and
  `bands` for stripes) instead. Lines too faint to find can be given as points.
- Very thin drawn details (under about 1 mm at 10 cm) are thickened to stay visible; a pose is kept as drawn, so
  wide-open arms reach well beyond the 2 cm capsule.
- Triangle counts land around 8k to 20k per character.

## Files

- `convert.py`: the command; measures, writes the plan, runs Blender, writes `character.json`, runs the turntable.
- `drawing.py`: the image processing.
- `build_glb.py`: the Blender half (balls to meshes, stickers, parts, pivots, GLB export).
- `READING.md`: the AI step's instructions. `grid.py`: the coordinate grid helper.
- `tests/test_converter.py`: determinism, our own test drawing (`tests/make_test_drawing.py`, lines drawn with
  Pillow, with a shadow and handwriting) and the output contract (10 cm, standing on the origin, between the feet,
  facing -Z, named parts with pivots, no images or external files). Run
  `python <this folder>/tests/test_converter.py`; outputs go to `CHARACTER_TEST_OUT`, by default a `_tests`
  folder beside this converter's characters under `C:\dev\EnFractal-art\characters\out\`, outside Git.
