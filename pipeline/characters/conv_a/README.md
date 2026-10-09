# A drawing becomes a rounded character

An offline converter with an explicit visual interpretation step. It produces
a self-contained, coloured GLB, joint metadata, the saved interpretation, and
the unchanged shared CPU turntable's front / three-quarter / side / back views.

## Convert

First give a vision-capable model **[INTERPRET.md](INTERPRET.md)** and
**[FORMAT.md](FORMAT.md)** alongside the photograph and the player's description.
Save its JSON response locally. Follow the same instructions for every drawing;
the generator contains no character names, filename matching or example lookup.
Review uncertain anatomy with the player when possible.

One PowerShell command performs the rest (paths containing spaces are supported):

```powershell
& 'C:\Python314\python.exe' -B pipeline/characters/conv_a/convert.py `
  --photo 'C:\path\drawing.jpg' `
  --description 'C:\path\description.txt' `
  --interpretation 'C:\path\interpretation.json' `
  --out 'captures\characters-out\conv_a\my-character'
```

The destination must be absent or empty. This intentionally refuses to replace
an existing character. A supplied `--blender` overrides the brief's pinned
Blender 5.2.2 executable path. `--no-render` is for geometry iteration and tests;
the default runs the shared turntable at its unchanged 640 px / 48 samples.
The default uses CPU Cycles; no GPU, network, installs or paid services.

Requirements: the brief's Python 3.14, NumPy and Pillow; Blender for renders.
No additional packages are needed by the converter. Keep source photographs
outside Git. No photograph or cropped photograph is copied to the destination;
only input SHA-256 hashes and upright pixel dimensions are retained as provenance.

## How it works

The AI distinguishes the creature from paper, binding, shadows and handwriting.
It records silhouette landmarks, separate jointed parts, features, family
colours and uncertainty. Its interpretation is the authoritative shape input:
**the Python program does not perform automatic segmentation or understand the
description**. Changing the photograph without revising the interpretation
changes provenance, not geometry. This boundary is deliberate and tested.

The mesher supports concave rounded silhouettes, ellipsoids and tapered curves.
It triangulates an outline, refines the surface, improves the triangles, then
smooths an inflated front and back with fixed silhouette boundaries. Small
features can follow that surface. Thickness creates closed solids; all colours
are material base colours. No textures, raster tracing or image bytes are used.
Facial ink is thin geometry. The drawing's asymmetries live in the interpretation.

The longest vertical extent, including props, becomes exactly 0.10 m. The
lowest vertex is seated on y=0. The supplied midpoint between the feet becomes
the root x/z origin; it need not equal the centre of the full visual bounding
box for an asymmetric creature. Image-right maps to -X so the supplied front
camera sees the original image handedness; +Y is up and the character faces -Z.

Each part becomes a named GLB node at its joint, with mesh vertices relative to
that joint and children relative to the parent's joint. Rotate those nodes for
procedural motion. `character.json` records both world and parent-local pivots,
the parent tree, motion hint, bounds and a collision capsule hint. It is a
**draft converter-local format**, not an established engine contract or a wired
game import feature. No shared schema is changed.

The binary writer follows the [Khronos glTF 2.0 specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html)
for GLB chunks, accessors, triangle winding, node transforms and linear material
base colours. It writes only an embedded buffer, with no external resources.

## Tests and an original drawing

```powershell
& 'C:\Python314\python.exe' -B pipeline/characters/conv_a/test_converter.py
& 'C:\Python314\python.exe' -B pipeline/characters/conv_a/synthetic.py --out "$env:TEMP\kite-bird-input"
& 'C:\Python314\python.exe' -B pipeline/characters/conv_a/convert.py `
  --photo "$env:TEMP\kite-bird-input\drawing.png" `
  --description "$env:TEMP\kite-bird-input\description.txt" `
  --interpretation "$env:TEMP\kite-bird-input\interpretation.json" `
  --out "$env:TEMP\kite-bird-output"
& 'C:\Python314\python.exe' -B pipeline/characters/conv_a/audit.py "$env:TEMP\kite-bird-output"
```

The original Pillow drawing is a three-eyed kite bird with one spring leg,
pointed wings and a curled tail. It has binding and a label that the authored
interpretation excludes. Tests run the CLI twice in separate processes with
different Python hash seeds, compare every non-render output byte, independently
read GLB accessors, and check height, ground, named parts, pivots, winding,
watertightness and absence of image resources. They also test orientation,
joint behaviour and refusal of malformed input / occupied output folders.
Working folders use `os.makedirs` in the system temp directory.

## Repeatability and limits

The same photo bytes, description bytes and saved interpretation produce the
same GLB, metadata and interpretation bytes on the same Python/NumPy platform.
There are no clocks, random geometry or absolute input paths in these files.
The shared turntable fixes CPU rendering and seed. Blender adds dates and render
durations to individual PNGs, so a final converter pass removes text, time and
EXIF chunks without recompressing pixels or changing colour-profile chunks.
Render byte repeatability is tested on the delivery machine, not promised across
Blender versions or CPUs. The shared turntable itself is never modified.
The AI interpretation is the nondeterministic step; keep its output for replay.

This is an author-assisted converter, not an autonomous image-to-3D model. It
invents depth from one view. It supports simple outline polygons without holes,
opaque colours and smooth geometry; it does not reproduce transparency, fur,
complex material layering or skeletal animation. Contour smoothing may overshoot
very sharp landmarks; use `smooth: false` or split an outline. Very tight tubes
can intersect themselves; keep bend radius larger than tube thickness.
Volumes are softly inflated shapes, so extremely thin features belong in tubes.

Preserving a wide drawing may put hands or props substantially beyond the 2 cm
collision radius. Metadata flags large overhangs; it does not certify gameplay
collision or fit. Founder judgment of resemblance and playability remains open.
Next work: a player-facing interpretation preview and correction step, followed
by the unchanged held-out drawing test and the game's procedural motion import.
