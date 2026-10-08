# Blender household-object spike

These are synthetic recipe prototypes, built without the founder's photos. Each object has one
parametric builder (`box.py`, `jar.py`); `common.py` handles geometry checks, GLB export and CPU
presentation. Only the installed Blender and its bundled Python are needed for generation.
`check_contract_glbs.py` additionally uses the existing system Python contract dependencies.
Tested Blender: **5.2.2 LTS, d13f752e3b9c**. No install, paid API, GPU render or game launch.

From the checkout root, in PowerShell:

```powershell
powershell -NoProfile -File docs/codex/spikes/09-blender/run.ps1 -Object box
powershell -NoProfile -File docs/codex/spikes/09-blender/run.ps1 -Object jar
powershell -NoProfile -File docs/codex/spikes/09-blender/run.ps1 -Object verify
python -B docs/codex/spikes/09-blender/check_contract_glbs.py "$env:TEMP/enfractal-09-blender-spike/artifacts"
```

`run.ps1` sets `BLENDER_USER_RESOURCES`, `BLENDER_USER_CONFIG`, `BLENDER_USER_DATAFILES`,
`BLENDER_USER_SCRIPTS`, `TEMP` and `TMP` to dedicated temp subdirectories, then runs:

```text
blender.exe --background --factory-startup --python-exit-code 1 --python <object.py> -- --out <absolute-temp-output>
```

Use a fresh shell for each invocation, as in the commands above: the wrapper changes its own
process environment. `--python-exit-code 1` makes an exception fail the process. Cycles uses the
CPU, four threads, 48 box / 96 jar samples, no denoiser, 512 x 384 pixels. The material and stage
settings are in `common.py`; lights, camera and floor are created **after** export.
Read [Blender's environment and CLI documentation](https://docs.blender.org/manual/en/4.3/advanced/command_line/arguments.html).
The exact installed export properties and shader sockets were queried locally; see the report.

Parameter overrides through the wrapper require PowerShell array syntax:

```powershell
& ./docs/codex/spikes/09-blender/run.ps1 -Object box -OutputDirectory "$env:TEMP/enfractal-09-custom-box" -RecipeArguments @('--width','0.6','--depth','0.4','--height','0.35','--flap-deg','0')
```

The same overrides can be passed directly after `--` when launching Blender with the isolated
environment above. Box defaults: width 0.40, depth 0.30, wall height 0.25, thickness 0.004 m,
flaps 4 degrees. Raised flaps increase total height to 0.263723 m. Jar defaults: total closed
height 0.095, maximum glass body diameter 0.082, lid inside diameter 0.082, glass wall 0.002 m;
`--lid-lift` exposes the neck for inspection. The jar's outside lid diameter is inside diameter
plus 0.002 m. `--no-render` generates GLB and metrics only.

Bonne Maman identifies its 370 g replacement lid as TO82, with **8.2 cm inside diameter**.
[Manufacturer source](https://www.bonne-maman.com/products/lot-de-6-couvercles-et-etiquettes-pour-pots-de-confiture-370g).
All other dimensions and profile proportions here are assumptions, **unverified on a real jar**.
370 g is the product's fill mass, not a measured empty-jar mass or an asserted volume.

The GLB stores triangle geometry in metre units with +Y up. Blender +Y is used as the builder's
front, which becomes glTF/Godot -Z. Children have baked transforms; the asset root is identity
at the horizontal bounds centre and minimum height. Jar orientation is rotationally symmetric.
See [Blender's GLB, axis and material documentation](https://docs.blender.org/manual/en/3.2/addons/import_export/scene_gltf2.html).
Gingham is generated directly into a 256 x 256 packed image and linked to Principled Base Color;
no Blender procedural node graph needs to travel to the engine. Glass exports
`KHR_materials_transmission`, with factor 1, opaque alpha, and default IOR 1.5. This spike does
**not** export `KHR_materials_volume` or prove physical refraction in Godot.
[Transmission/volume guidance](https://docs.blender.org/manual/en/5.3/addons/scene_gltf2.html).

## Notes towards capture guidance and an MCP recipe

1. Ask for dimensions in millimetres, convert once to metres, and label each as measured,
   sourced or assumed. Box: outside width/depth/wall height, wall thickness, flap overlap and
   angle. Jar: overall height, maximum/body/base/neck diameters, shoulder height, wall and base
   thickness, lid inner/outer diameter and depth, facet count, thread/lug shape and pitch.
2. Request front, side, top and bottom views against a ruler. For a vessel, add an open-mouth
   view and a separate lid/neck close-up. Glass and polished metal need silhouette and profile
   measurements; do not infer dimensions from reflections. No real photos were tested here.
3. Choose a constructive recipe: panels for a carton; a continuous outer/inner lathe profile
   for a vessel; mesh tubes for ridges/handles. Build large dimensions before small detail.
   Treat unknown thread form as an explicit approximation. Keep lid and body separate.
4. Apply small bevels in metres. Use exportable PBR constants/image textures. Generate or bake
   patterned textures into small embedded images. View the target engine's glass treatment
   before spending time on a perfect offline render.
5. Before export, check positive signed volume, no boundary/nonmanifold edges, no degenerate
   triangles, finite bounds, intended wall thickness and no unintended component intersections.
   Current checks prove each component is a solid; they do **not** prove their union is a single
   solid. Box panels deliberately meet/overlap; jar ridge overlaps neck glass.
6. Bake transforms, move the asset bottom centre to zero, export selected asset parts only,
   and decode the actual GLB. Confirm metre/Y-up bounds, identity root, indices, triangle count,
   embedded images/buffer, expected materials, no stage objects and byte budget. Re-import
   once and compare. Save source parameters, Blender version, hashes, time and assumptions.
7. Make a small CPU preview, obtain the founder's appearance decision, then hand off to Capture
   for collision and asset metadata. Hollow visual geometry does not establish playable
   container collision. Neither object in this spike is a game-ready asset package.

The eventual MCP should expose bounded recipe parameters and job receipts, with a trusted
local worker owning paths, Blender launch and validation. It must preserve the companion's
no-shell/no-files boundary; these raw Python recipes are tooling for that worker, not proposed
companion commands. This is guidance only; no MCP or skill was implemented in this brief.

## Remaining objects: engineering estimates, unverified

| Object | Assessment | Proposed route and hard part |
|---|---|---|
| Couch with wooden legs | Moderate | Bevelled cushion/block recipe and tapered legs make a useful stylized proxy. Measure seat, arms, back, cushion count and leg layout. Upholstery sag, seams and irregular pillows need photographs and more modelling work. |
| Gaming laptop | Easy for a closed proxy; moderate open | Thin bevelled panels plus a hinge, screen and generated keyboard texture. Measure lid/base thickness, hinge axis, opening angle and screen border. Exact ports, legends and branded details are harder than the main shape. |
| Metal French press | Moderate to hard | Lathe body/lid, tube handle/spout and plunger rod. Measure profiles and handle clearances from side views. Reflective finish, a sculpted spout, internal filter and handle joins are the hard parts; explicit geometry is my preferred first trial, with a simplified interior. |

These estimates are judgments from this construction exercise, not results from building the
three objects or benchmarking them against image-to-3D.
