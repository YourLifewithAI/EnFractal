# Local household recipes

Run from the checkout root with system Python (standard library only) and an installed
Blender with its bundled glTF exporter and Cycles. Nothing is downloaded or hosted.
Set `BLENDER` to the executable if needed; otherwise the runner looks recursively under
`C:\Users\blues\AppData\Local\Programs\Blender`. Multiple installations require an explicit choice.

```powershell
python -B pipeline/recipes/build.py "$env:TEMP/box-input.json" --out "$env:TEMP/box-output"
python -B -m unittest pipeline.recipes.tests -v
```

The first command needs an input file such as:

```json
{
  "recipe": "cardboard_box",
  "size_m": [0.4, 0.25, 0.3],
  "colours": {"cardboard": "#ad804e"},
  "params": {"flap_angle_deg": 45}
}
```

`recipe` and `size_m` are required. Omitting `colours` and `params` uses every default.
`style` is optional: `"storybook"` (default) or `"plain"` for the original geometry
and materials. Both use the same clean review renderer. This is an author comparison
parameter, not a material medium or an alteration of the scanned colour slots.
Each size component is a finite number in **[0.02, 20] metres**; maximum/minimum
component ratio must be at most 100. These are engineering limits, assumed.
The array is **width, height, depth**, +Y up and -Z forward, and describes the **whole
object in its supplied pose**, including open flaps, lifted lids, knobs and handles.
Templates are fitted independently along all three axes to that outer box and translated
to bottom centre. The receipt records the fit scale. An inconsistent box and pose can
distort proportions; obtain the box for the current pose. A closed laptop typically needs
about 0.023 m height, while an open laptop needs its whole open height.

Colours are sRGB `#rrggbb`, case insensitive; shader constants convert to linear RGB.
Unknown top-level, colour and parameter keys, wrong types (including numeric booleans),
duplicates, non-finite numbers and invalid ranges are errors. No paths, scripts, arbitrary
text, URLs or launch arguments belong in this JSON. Params below are inclusive ranges.
Dimensionless thickness/height fractions scale with size rather than fixing tiny parts in metres.

| Recipe | Example size [W,H,D] m | Colour slots: default / material role |
|---|---|---|
| `cardboard_box` | [0.400,0.250,0.300] | `cardboard`: #ad804e / cardboard; `edges`: #c49b68 / cardboard |
| `couch` | [2.100,0.830,0.950] | `upholstery`: #50757b / fabric; `legs`: #775035 / wood |
| `gaming_laptop` | [0.358,0.240,0.278] | `shell`: #30343c / metal; `keyboard`: #161922 / plastic; `screen`: #497488 / emissive |
| `jam_jar` | [0.084,0.095,0.084] | `glass`: #f3fafb / glass; `lid`: #bb2939 / metal_painted |
| `french_press` | [0.170,0.245,0.107] | `beaker`: #e8f4f5 / glass or metal; `frame`: #aeb5bd / metal; `handle`: #272b30 / plastic |

| Recipe | Parameter | Default | Valid values and meaning |
|---|---|---|---|
| cardboard_box | `flap_angle_deg` | 0.0 | 0..120; 0 closed, 90 upright/open, 120 outward/open; intermediate values partly open |
| cardboard_box | `wall_fraction` | 0.012 | 0.005..0.03 times shortest input side, before fitting |
| couch | `cushion_count` | 3 | integer 1..6; this many seat cushions and this many back cushions |
| couch | `seat_height_fraction` | 0.53 | 0.4..0.65 times height; top of seat |
| couch | `leg_height_fraction` | 0.16 | 0.08..0.25 times height |
| gaming_laptop | `lid_angle_deg` | 105.0 | 0..135 degrees measured from closed; 0 closed |
| jam_jar | `lid_pattern` | gingham | `gingham`, `solid`, `stripes`; white plus the lid colour, no lettering |
| jam_jar | `lid_lift_fraction` | 0.0 | 0..0.3 times template height; lifts lid to expose the neck |
| jam_jar | `facets` | 12 | integer 8..16; faceted lower body |
| french_press | `beaker_material` | glass | `glass`, `metal`; changes the `beaker` material role and transmission |
| french_press | `plunger_fraction` | 0.1 | 0.05..0.75 times height; filter elevation (knob stays at full height) |

Plain jar wall thickness is 2.4% of the shortest horizontal input side. Other internal
proportions, bevels and tessellation are fixed recipe implementation details, assumed.
Panel bevel widths are capped at one quarter of each panel's thinnest dimension,
leaving a flat strip between opposing bevels even on the couch's thinnest seat frame.
The couch's full parameter ranges remain valid; every combination of their endpoints
is tested at 0.5x, 1x and 2x the example size. Its minimum frame thickness is 1% of height
(seat fraction 0.4 minus cushion fraction 0.14 minus leg fraction 0.25).
The neck helix is a visual approximation, not a compatible manufactured closure.
The press uses separate bands, struts, grip, lid, rod and filter; its modelled plunger
setting is a static filter position, not a mechanically linked animation.

## Sources for proportions

- Carton's 400 x 250 x 300 mm example and wall ratio are **assumed**, inherited from brief 09.
- Couch example box is **assumed**. Its 0.53 x 0.83 = 0.440 m seat height is based on
  [IKEA LANDSKRONA's 44 cm seat height](https://www.ikea.com/no/en/p/landskrona-armchair-gunnared-blue-wood-s79393405/),
  used as a seating reference, not a claim to reproduce that armchair. Cushion/leg layout is **assumed**.
- Laptop width/depth are rounded from Lenovo's 357.7 x 277.7 mm Legion 9i specification;
  that source also gives 18.99..22.7 mm closed thickness. The open example's 240 mm
  height, 105-degree lid default and internals are **assumed**.
  [Lenovo specification table](https://news.lenovo.com/pressroom/press-releases/legion-gaming-ecosystem-helping-gamers-reach-their-impossible/).
- Jam jar's example height and outside dimensions are **assumed**, from brief 09.
  [Bonne Maman's 370 g replacement-lid page](https://www.bonne-maman.com/products/lot-de-6-couvercles-et-etiquettes-pour-pots-de-confiture-370g)
  gives 8.2 cm inside lid diameter; the recipe's outside lid is approximately 8.4 cm.
  Empty vessel only; no fill, label or brand text.
- Press's 170 x 245 x 107 mm whole box follows
  [Bodum CHAMBORD 1 L specifications](https://www.bodum.com/us/en/1928-57-chambord).
  Body, handle, frame and filter ratios are **assumed**, not exact replicas.

## Outputs and checks

A fresh output folder receives `<recipe>.glb`, `<recipe>.receipt.json`, `<recipe>.png`
and a UTF-8 LF `<recipe>.log`. Existing files for that recipe are refused, without
overwriting them. Build work, configuration directories and temporary files live in
a fresh `<out>/.blender-work` folder and are removed with `shutil.rmtree` afterwards.
Directories use `os.makedirs` with its default mode, including the tests' UUID working
folders under `RECIPE_TEST_ARTIFACTS` or the system temp folder. Failed Blender runs leave a log
and no new GLB in the caller's folder. No `asset.json` is generated.

GLBs embed all buffers and images, use metre coordinates, have a single named identity
root at bottom centre, and contain uniquely named parts. Exactly one material exists per
slot; its `extras.material_role` and the receipt's `materials` use the current contract enum.
Gingham/stripes use a generated, packed 64 x 64 sRGB PNG. No photo textures are used.
Flat-colour panels discard unused cube UV layers before beveling/export, avoiding
the observed process-to-process UV interpolation jitter without quantising geometry.
CPU-only Cycles previews are 512 x 384, 64 samples and four threads, with
OpenImageDenoise explicitly on the CPU. A 30-degree perspective lens, high three-quarter
angle, warm disk key light and cool fill frame the actual projected bounds with a margin.
Plain and storybook use the same renderer/camera rules. All object detail stays in focus
for review; the game's preset supplies its own depth of field and brushwork.
The launcher uses `--background --factory-startup --python-exit-code 1` plus fixed worker
paths. See [Blender's CLI/environment documentation](https://docs.blender.org/manual/en/4.3/advanced/command_line/arguments.html).
Export conventions follow [Blender's glTF documentation](https://docs.blender.org/manual/en/3.2/addons/import_export/scene_gltf2.html).
The inherited helpers in `geometry.py` are copied from brief 09; that spike is read-only.

Shared checks reject non-finite or mismatched bounds (1 mm), invalid pivot/root,
degenerate triangles, nonmanifold edges/vertices, inconsistent winding or nonpositive
part volume, wrong material slots/roles, and triangle budget overflow. Every part's
decoded GLB bounds and triangle count are compared; decoded triangle areas/indices,
finite positions and resource containment are checked. An actual Blender re-import
also compares total bounds, part count and triangles. Only after CPU rendering and the
contract GLB check succeed does the runner publish the outputs.

`contract_check.py` executes the live `ContractError`, `loads_strict` and `check_glb`
definitions from `contracts/validate.py`, selected verbatim as AST nodes. This allows
the standard-library runner and Blender Python to execute the **actual** binary check
without installing JSON Schema dependencies. It does not implement schema validation;
future additional dependencies fail closed and require adapter review. The integrator
still validates the Capture lane's completed asset package with `contracts/validate.py`.

Receipt bounds, part bounds and collision shapes use asset coordinates (+Y up).
Collision hints are conservative, axis-aligned solid boxes per substantial part,
excluding keys, screen, thread and tiny internal pieces. They fill hollow vessels and
can over-cover rotated panels; Capture must choose/rebuild collision for container access.
Per-part manifold checks do not certify a watertight union or an absence of intentional
overlap. Founder appearance review, Godot glass behaviour and playable collision remain
outside this recipe library's evidence.

Tests build all five default recipes at 0.5x, 1x and 2x size, alternate states, one
repeat per default for byte determinism, and all 24 couch endpoint/size combinations.
They also build plain twice and explicit storybook once per recipe, verifying plain
determinism, default/explicit equivalence, distinct exports, bounds and glass alpha.
They inspect receipts, materials, hashes, the absence of unused panel UVs,
PNG headers and rejection before Blender launch. There are no installs or skipped builds.
Set trusted local `RECIPE_TEST_ARTIFACTS` to a temp directory to retain evidence; otherwise
the test artifacts are cleaned up. Reproducibility is checked on the installed Blender,
not promised across Blender/exporter versions. Receipt timing varies even when GLB bytes match.

For C7: expose only recipe names, measured outer boxes, bounded params and colour slots to
an AI. Track whether measurements are measured, sourced or assumed upstream; receipt
`source` tracks caller-given versus recipe-default only. Keep executable selection, paths,
environment, scripts, export/check code and command-line construction owned by a trusted
local worker. This CLI is author tooling, not a proposed game-companion shell/file capability.
Capture owns IDs, pose in the room, mass, affordances, review status and the final asset package.

## Shared storybook layer and C7 rules for new recipes

The trusted worker calls `style.configure(data)` before materials and geometry, then
`style.finish(parts)` before fitting, checks and export. Every recipe therefore gets
the shared material response and deterministic taper, lean and bow by default. Use
`geometry.panel` for box parts: its storybook bevel is built in unit space before
scaling, so thin panels have broad plan corners; cushions and arms get pillowy rounding.
Plain retains the previous small bevel and bypasses all deformations. Curved vessels
still need hand-authored rounded lathe profiles: the layer cannot invent a rounded
profile or a recognizable silhouette from arbitrary sharp geometry.

- Measure the whole posed box in metres, including open lids and handles. Keep kind,
  size, placement and broad slot colours faithful; exaggerate only internal details.
- Use thick shells, fewer larger details and soft profiles. Keep the telltale features:
  flap seams/tape, uneven cushions, a keyboard/palm rest, jar facets/gingham, press frame/knob.
- Use `style.enabled()` for recipe-specific proportions and `style.signed(part, channel)`
  for small repeatable variation. The SHA-256 seed covers canonical resolved input,
  excluding given/default provenance. Never use Python's randomized `hash()`, time or
  vertex noise. Shared wonk is at most 1.8% lean, 2.2% taper and 1.2% bow of each part;
  cushion/flap rotations are about one degree. Fit afterward and retain every check.
- Preserve material names, roles and exact base colours. Storybook metal uses restrained
  metallic response and broad rough highlights; fabric stays fabric and cardboard stays
  cardboard. No photographic textures or preview-only shader trick is needed for charm.
- Glass uses a thick shell, unchanged slot tint, alpha 0.22 and zero transmission, exported
  as core glTF `alphaMode=BLEND`; physical refraction is reserved for plain. This avoids
  relying on a transmission extension ([Blender glTF material documentation](https://docs.blender.org/manual/en/3.6/addons/import_export/scene_gltf2.html)).
  Layered transparency and the game's role-material replacement need a Look/Capture
  check in Godot; Blender re-import and binary alpha checks do not certify that runtime.
- Return named closed parts. Do not bypass the worker, change triangle budgets, or expose
  paths/scripts/launch settings to the game companion. Add both styles to the build tests.

The budgets remain 20,000 triangles for a couch and 10,000 for each other recipe.
The supplied comparison sheet shows both styles with identical preview settings;
the five individual previews are storybook. A new style default intentionally changes
GLB hashes from brief 10; determinism is within the same Blender/exporter version.
