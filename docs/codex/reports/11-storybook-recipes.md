# Brief 11: storybook recipes

The five recipes now default to a shared storybook layer; `style: "plain"` keeps
the earlier geometry and material response. Normalized-space bevels soften thin
panels as well as cushions. SHA-256 of resolved input and part names seeds small
analytic taper, lean, bow and cushion/flap rotations. Final fitting and all
topology, decoded-GLB, Blender re-import and contract checks remain. Slot colours
and roles stay honest; broad rough highlights replace photographic material
response. Triangle budgets increase by **zero**.

The laptop's old preview was orthographic, so lens distortion was not its cause.
Its deck used the whole supplied depth before accounting for lid overhang;
independent axis fitting squeezed its proportions. Thin shells, tiny bevels and
70 small keys reinforced the awkward look. Storybook uses pose-aware deck depth,
a wider bezel, thicker rounded shells, 40 larger keys and a spacebar. The supplied
lid-angle parameter remains authoritative. Both styles share a 30-degree high
three-quarter camera, warm soft light and 64-sample CPU Cycles with CPU denoising.

Glass exports as thick tinted alpha surfaces (`BLEND`, alpha 0.22, no transmission).
**Integration blocker:** Godot's current captured-material replacement discards
alpha. The Look-owned fix below is proposed only; those files are untouched.
Runtime glass and founder charm approval remain unfinished. The comparison PNG
also failed its 800 KB delivery limit (921232 bytes); it is retained
as an oversized review candidate. Generation stopped at that check. Judge the jar's milky
tint, carton corner gaps and laptop toy proportions from the comparison sheet.
No real-place data, GPU, installs, paid APIs, commits or pushes; cost $0, GPU time 0.

The whole recipe suite passed: 9 tests, 64 builds, exit 0.

Game runners were not run: engine code is unchanged and their generated writes
exceed this brief's scope. The integrator owns commits and the branch scope checker.

## Recipe changes and measurements

Before is a fresh build of the original checkout; after is the final suite's default
storybook build. Seconds include launch, checks/export and CPU rendering, so the
cleaner previews raise build time.

| Recipe | Shape changes | Triangles before / after | GLB bytes before / after | Seconds before / after |
|---|---|---|---|---|
| cardboard_box | Thicker walls, rounded askew flaps, coarse packing tape. | 972 / 3000 | 56724 / 131528 | 4.310 / 10.756 |
| couch | Pillowy uneven seats/backs/arms, wider arms and legs. | 1512 / 5224 | 81432 / 85604 | 4.131 / 11.613 |
| gaming_laptop | Thicker rounded shells, 40 larger keys, pose-aware deck/display. | 8100 / 5436 | 431892 / 138628 | 5.164 / 11.673 |
| jam_jar | Double glass wall, strong facet planes, rounded gingham lid/ridge. | 3116 / 3052 | 79096 / 89988 | 6.463 / 15.787 |
| french_press | Thick rounded beaker, chunky grip/struts, domed lid/larger knob. | 2676 / 4468 | 78948 / 94156 | 4.862 / 12.542 |


## C7 guidance

Use the shared worker, `geometry.panel`, `style.enabled()` and input-seeded
`style.signed(name, channel)`; return named closed parts and fit/check afterward.
Use soft profiles, chunky identifying features and fewer larger details inside the
measured posed box. Preserve slot colour and role; let the game's preset add brushwork.
Build both styles and repeat exports before delivery. Keep paths, scripts, launcher
flags and validation in the trusted adapter, outside the game AI surface.
See [the recipe guide](../../../pipeline/recipes/README.md#shared-storybook-layer-and-c7-rules-for-new-recipes).

## Out-of-scope integration change request

Read-only diagnosis: `LookDirector.PaintCaptured` replaces GLB materials through
`MaterialLibrary.ForCaptured`; `Glaze` drops alpha and the shader declares a vec3
base colour with no ALPHA output. Recipe alpha cannot survive that path. This is
a source-code inference, not a Godot render measurement.

The minimal proposed diff preserves recipe glass until the Look lane supplies a
painterly transparent shader. It is **unverified**; the integrator/Look lane must
apply and test it, including transparency sorting, GI and material-role review.

```diff
--- a/game/scripts/native/Look/LookDirector.cs
+++ b/game/scripts/native/Look/LookDirector.cs
@@
             if (mesh.GetActiveMaterial(surface) is not BaseMaterial3D source) continue;
             var role = RoleFor(owner, source.ResourceName);
+            // The painterly shader currently has no alpha path; retain recipe glass.
+            if (role == "glass" && source.Transparency != BaseMaterial3D.TransparencyEnum.Disabled) continue;
             mesh.SetSurfaceOverrideMaterial(surface, MaterialLibrary.ForCaptured(role, source.AlbedoColor, source.AlbedoTexture));
```

Sources: [look bible](../../look/LOOK-BIBLE.md) for shape/material direction;
[Blender glTF materials](https://docs.blender.org/manual/en/3.6/addons/import_export/scene_gltf2.html)
for alpha versus transmission; [Blender render settings](https://docs.blender.org/manual/en/5.0/render/cycles/render_settings/index.html)
for rendering and denoising controls. Actual installed flags and exported alpha
are verified by code/tests, not inferred from a different manual version.
[Pillow image API](https://pillow.readthedocs.io/en/stable/reference/Image.html)
is used only by the scratch sheet compositor (already installed Pillow 12.1.1);
recipe builds/tests have no new dependency. No reference image or optional Tiny
Glade lookup was used; the complete local look bible supplied the direction.

## Raw evidence

Retained logs, receipts, synthetic previews and uncommitted GLBs:

```text
C:\Users\blues\AppData\Local\Temp\enfractal-11-50be820614484a4799a4167c56a888af
```

Baseline command, exit 0 (before all edits): the following stdin was run as a
PowerShell single-quoted here-string piped to `python -B`.

```python
import os, tempfile, uuid, json
from pathlib import Path
from pipeline.recipes.build import build
from pipeline.recipes.specs import RECIPES
root=Path(tempfile.gettempdir()) / ('enfractal-11-' + uuid.uuid4().hex)
os.makedirs(root)
print('EVIDENCE',root,flush=True)
for name,spec in RECIPES.items():
    receipt=build({'recipe':name,'size_m':spec['example_size_m']},root/'before'/name)
    print('BEFORE',name,json.dumps({k:receipt[k] for k in ('triangle_count','glb','runtime_seconds')}),flush=True)
```

Raw PASS lines from that command's output:

```text
PASS cardboard_box: triangles=972 bounds=[0.4000000059604645, 0.25, 0.30000001192092896] bytes=56724 seconds=4.310
PASS couch: triangles=1512 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=81432 seconds=4.131
PASS gaming_laptop: triangles=8100 bounds=[0.3580000102519989, 0.23999999463558197, 0.27799999713897705] bytes=431892 seconds=5.164
PASS jam_jar: triangles=3116 bounds=[0.08399999886751175, 0.0949999988079071, 0.08399999886751175] bytes=79096 seconds=6.463
PASS french_press: triangles=2676 bounds=[0.17000000178813934, 0.24500000476837158, 0.10700000077486038] bytes=78948 seconds=4.862
```

Full-suite command, exit 0; run once using `subprocess.run` with a UTF-8 log:

```powershell
$env:RECIPE_TEST_ARTIFACTS = "C:\Users\blues\AppData\Local\Temp\enfractal-11-50be820614484a4799a4167c56a888af"
$env:PYTHONDONTWRITEBYTECODE = '1'
python -B -m unittest pipeline.recipes.tests -v
```

Relevant raw determinism lines and last lines:

```text
DETERMINISM cardboard_box byte_identical=True sha256=a49099dfa746ee4b5becaa1dfddc9f996f976f6f6efba96955639f6194879b04
DETERMINISM couch byte_identical=True sha256=82aa78941c91616d30e2f955337fc5d9e2a66936bf4d1b4a0a88020319d7f610
DETERMINISM gaming_laptop byte_identical=True sha256=c1f3affbf0b4c07b601787c00fc14392d3ab43083a0bfbc65ce694c92db9ad4b
DETERMINISM jam_jar byte_identical=True sha256=d009d19407a3c3a91785f9e913151df784abb1d6b46e045c7b5e3b8749ced0f0
DETERMINISM french_press byte_identical=True sha256=67bf97a4039a9816b412da1cc17351f46b45aedffb49d16f5d153843fd0eea17
PASS couch: triangles=1512 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=81432 seconds=10.365
PASS couch: triangles=1512 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=81432 seconds=10.262
PASS couch: triangles=5224 bounds=[2.0999999046325684, 0.8300000429153442, 0.949999988079071] bytes=85604 seconds=10.784
PLAIN_AND_STORYBOOK couch bounds_fit=True byte_identical=True distinct_styles=True
PASS gaming_laptop: triangles=8100 bounds=[0.3580000102519989, 0.23999999463558197, 0.27799999713897705] bytes=431892 seconds=10.904
PASS gaming_laptop: triangles=8100 bounds=[0.3580000102519989, 0.23999999463558197, 0.27799999713897705] bytes=431892 seconds=12.423
PASS gaming_laptop: triangles=5436 bounds=[0.3580000102519989, 0.23999999463558197, 0.27799999713897705] bytes=138628 seconds=11.923
PLAIN_AND_STORYBOOK gaming_laptop bounds_fit=True byte_identical=True distinct_styles=True
PASS jam_jar: triangles=3116 bounds=[0.08399999886751175, 0.0949999988079071, 0.08399999886751175] bytes=79096 seconds=15.130
PASS jam_jar: triangles=3116 bounds=[0.08399999886751175, 0.0949999988079071, 0.08399999886751175] bytes=79096 seconds=15.396
PASS jam_jar: triangles=3052 bounds=[0.08399999886751175, 0.0950000062584877, 0.08399999886751175] bytes=89988 seconds=15.999
PLAIN_AND_STORYBOOK jam_jar bounds_fit=True byte_identical=True distinct_styles=True
PASS french_press: triangles=2676 bounds=[0.17000000178813934, 0.24500000476837158, 0.10700000077486038] bytes=78948 seconds=11.683
PASS french_press: triangles=2676 bounds=[0.17000000178813934, 0.24500000476837158, 0.10700000077486038] bytes=78948 seconds=11.948
PASS french_press: triangles=4468 bounds=[0.17000000178813934, 0.24500000476837158, 0.10700000077486038] bytes=94156 seconds=14.074
PLAIN_AND_STORYBOOK french_press bounds_fit=True byte_identical=True distinct_styles=True
ok
BUILD_TEST_SECONDS 805.335
ARTIFACTS C:\Users\blues\AppData\Local\Temp\enfractal-11-50be820614484a4799a4167c56a888af\recipe-tests-19a5e5c838a94da9b73b53d44ad42ddf
test_bad_inputs (pipeline.recipes.tests.test_recipes.InputTests.test_bad_inputs) ... ok
test_cli_refuses_before_launch (pipeline.recipes.tests.test_recipes.InputTests.test_cli_refuses_before_launch) ... ok
test_defaults_and_sources (pipeline.recipes.tests.test_recipes.InputTests.test_defaults_and_sources) ... ok
test_style_seed (pipeline.recipes.tests.test_recipes.InputTests.test_style_seed) ... ok

----------------------------------------------------------------------
Ran 9 tests in 805.444s

OK
```

Initial line-ending audit, exit 1 (checked-out CRLF):

```python
for path in files:
    if path.suffix in ('.py','.md','.json'):
        path.read_bytes().decode('utf-8')
        assert b'\r' not in path.read_bytes(), path
```

```text
Traceback (most recent call last):
  File "<stdin>", line 7, in <module>
AssertionError: pipeline\recipes\__init__.py
```

Normalized scoped recipe text, exit 0:

```python
for path in Path('pipeline/recipes').rglob('*'):
    if path.is_file() and path.suffix in ('.py','.md','.json'):
        text=path.read_text(encoding='utf-8')
        path.write_text(text,encoding='utf-8',newline='\n')
        print('NORMALIZED_UTF8_LF',path.as_posix(),flush=True)
```

```text
NORMALIZED_UTF8_LF pipeline/recipes/README.md
NORMALIZED_UTF8_LF pipeline/recipes/style.py
NORMALIZED_UTF8_LF pipeline/recipes/worker.py
NORMALIZED_UTF8_LF pipeline/recipes/__init__.py
NORMALIZED_UTF8_LF pipeline/recipes/tests/test_recipes.py
NORMALIZED_UTF8_LF pipeline/recipes/tests/__init__.py
```

No validation checks/assertions were disabled. A scratch report-preparation command
also exited 1 with `SyntaxError: unterminated string literal`; it wrote no artifact.
The corrected report writer below does not launch tests or Blender.

Preview/metric delivery command, **exit 1** (comparison sheet exceeds 800,000 bytes):

```powershell
python -B "C:\Users\blues\AppData\Local\Temp\enfractal-11-50be820614484a4799a4167c56a888af\deliver.py" "C:\Users\blues\AppData\Local\Temp\enfractal-11-50be820614484a4799a4167c56a888af"
```

This scratch script reads passing receipts, copies five default PNGs and composes
the plain/default sheet at 384x288 cells. Its sheet-size assertion failed; all five
individual PNGs are under 400,000 bytes. It launches no builds. No compression retry,
size-check change or additional test invocation followed.
Raw output:

```text
METRICS cardboard_box {"before": {"triangle_count": 972, "glb": {"path": "cardboard_box.glb", "bytes": 56724, "sha256": "2aa978a9fe1e798e29e44bb201defae68b4c4d56fec9d2df157989d0f920a69f"}, "runtime_seconds": {"worker": 3.373276199999964, "export_and_checks": 0.6250157999893418, "render": 2.709789100001217, "process": 4.309822199997143}, "dimensions_m": [0.4000000059604645, 0.25, 0.30000001192092896]}, "storybook": {"triangle_count": 3000, "glb": {"path": "cardboard_box.glb", "bytes": 131528, "sha256": "a49099dfa746ee4b5becaa1dfddc9f996f976f6f6efba96955639f6194879b04"}, "runtime_seconds": {"worker": 9.765637100004824, "export_and_checks": 0.6534728999977233, "render": 9.04631380000501, "process": 10.755834499999764}, "dimensions_m": [0.4000000059604645, 0.25, 0.30000001192092896]}, "plain": {"triangle_count": 972, "glb": {"path": "cardboard_box.glb", "bytes": 56724, "sha256": "2aa978a9fe1e798e29e44bb201defae68b4c4d56fec9d2df157989d0f920a69f"}, "runtime_seconds": {"worker": 9.440898899993044, "export_and_checks": 0.5879205000092043, "render": 8.821921200011275, "process": 10.354224499998963}, "dimensions_m": [0.4000000059604645, 0.25, 0.30000001192092896]}}
PREVIEW cardboard_box 213059 bytes
RENDER cardboard_box {'version': '5.2.2 LTS', 'build_hash': 'd13f752e3b9c'} {'path': 'cardboard_box.png', 'pixels': [512, 384], 'engine': 'CYCLES', 'device': 'CPU', 'samples': 64, 'denoising': 'OPENIMAGEDENOISE_CPU', 'camera_angle_deg': 30}
BYTE_CHECK cardboard_box storybook= True plain= True
METRICS couch {"before": {"triangle_count": 1512, "glb": {"path": "couch.glb", "bytes": 81432, "sha256": "56f9c3df07de2c7b73923cd30dde4e7557f032e51cc544bff45c74b944a1d970"}, "runtime_seconds": {"worker": 3.186968199996045, "export_and_checks": 0.6341313000011723, "render": 2.506462899997132, "process": 4.131152499991003}, "dimensions_m": [2.0999999046325684, 0.8299999833106995, 0.949999988079071]}, "storybook": {"triangle_count": 5224, "glb": {"path": "couch.glb", "bytes": 85604, "sha256": "82aa78941c91616d30e2f955337fc5d9e2a66936bf4d1b4a0a88020319d7f610"}, "runtime_seconds": {"worker": 10.607408800002304, "export_and_checks": 0.7125246000068728, "render": 9.787414999998873, "process": 11.61283809998713}, "dimensions_m": [2.0999999046325684, 0.8300000429153442, 0.949999988079071]}, "plain": {"triangle_count": 1512, "glb": {"path": "couch.glb", "bytes": 81432, "sha256": "56f9c3df07de2c7b73923cd30dde4e7557f032e51cc544bff45c74b944a1d970"}, "runtime_seconds": {"worker": 9.443681299992022, "export_and_checks": 0.593270899989875, "render": 8.805116100003943, "process": 10.364809999999125}, "dimensions_m": [2.0999999046325684, 0.8299999833106995, 0.949999988079071]}}
PREVIEW couch 237630 bytes
RENDER couch {'version': '5.2.2 LTS', 'build_hash': 'd13f752e3b9c'} {'path': 'couch.png', 'pixels': [512, 384], 'engine': 'CYCLES', 'device': 'CPU', 'samples': 64, 'denoising': 'OPENIMAGEDENOISE_CPU', 'camera_angle_deg': 30}
BYTE_CHECK couch storybook= True plain= True
METRICS gaming_laptop {"before": {"triangle_count": 8100, "glb": {"path": "gaming_laptop.glb", "bytes": 431892, "sha256": "cfe5b1d50adc99337ce0b254982f7968d5df54323ce2b9c6bb604eac2c736612"}, "runtime_seconds": {"worker": 4.2053715999936685, "export_and_checks": 0.9132881999976235, "render": 2.867268400004832, "process": 5.1639002000010805}, "dimensions_m": [0.3580000102519989, 0.23999999463558197, 0.27799999713897705]}, "storybook": {"triangle_count": 5436, "glb": {"path": "gaming_laptop.glb", "bytes": 138628, "sha256": "c1f3affbf0b4c07b601787c00fc14392d3ab43083a0bfbc65ce694c92db9ad4b"}, "runtime_seconds": {"worker": 10.616938699997263, "export_and_checks": 0.7430027000082191, "render": 9.604820699998527, "process": 11.67334770000889}, "dimensions_m": [0.3580000102519989, 0.23999999463558197, 0.27799999713897705]}, "plain": {"triangle_count": 8100, "glb": {"path": "gaming_laptop.glb", "bytes": 431892, "sha256": "cfe5b1d50adc99337ce0b254982f7968d5df54323ce2b9c6bb604eac2c736612"}, "runtime_seconds": {"worker": 9.933391900005518, "export_and_checks": 0.8121879999962403, "render": 8.667729499997222, "process": 10.904342400011956}, "dimensions_m": [0.3580000102519989, 0.23999999463558197, 0.27799999713897705]}}
PREVIEW gaming_laptop 228020 bytes
RENDER gaming_laptop {'version': '5.2.2 LTS', 'build_hash': 'd13f752e3b9c'} {'path': 'gaming_laptop.png', 'pixels': [512, 384], 'engine': 'CYCLES', 'device': 'CPU', 'samples': 64, 'denoising': 'OPENIMAGEDENOISE_CPU', 'camera_angle_deg': 30}
BYTE_CHECK gaming_laptop storybook= True plain= True
METRICS jam_jar {"before": {"triangle_count": 3116, "glb": {"path": "jam_jar.glb", "bytes": 79096, "sha256": "29e256ce66c590943a91df0083ec95a0e480d9ab7121ea7fa9f6baabd3d18228"}, "runtime_seconds": {"worker": 5.5103835000045365, "export_and_checks": 0.6107310000079451, "render": 4.716942200000631, "process": 6.463080600005924}, "dimensions_m": [0.08399999886751175, 0.0949999988079071, 0.08399999886751175]}, "storybook": {"triangle_count": 3052, "glb": {"path": "jam_jar.glb", "bytes": 89988, "sha256": "d009d19407a3c3a91785f9e913151df784abb1d6b46e045c7b5e3b8749ced0f0"}, "runtime_seconds": {"worker": 14.837271200012765, "export_and_checks": 0.5693958000047132, "render": 13.997534099995391, "process": 15.786847999988822}, "dimensions_m": [0.08399999886751175, 0.0950000062584877, 0.08399999886751175]}, "plain": {"triangle_count": 3116, "glb": {"path": "jam_jar.glb", "bytes": 79096, "sha256": "29e256ce66c590943a91df0083ec95a0e480d9ab7121ea7fa9f6baabd3d18228"}, "runtime_seconds": {"worker": 14.20457339999848, "export_and_checks": 0.6041900999989593, "render": 13.405896500000381, "process": 15.13045900000725}, "dimensions_m": [0.08399999886751175, 0.0949999988079071, 0.08399999886751175]}}
PREVIEW jam_jar 213525 bytes
RENDER jam_jar {'version': '5.2.2 LTS', 'build_hash': 'd13f752e3b9c'} {'path': 'jam_jar.png', 'pixels': [512, 384], 'engine': 'CYCLES', 'device': 'CPU', 'samples': 64, 'denoising': 'OPENIMAGEDENOISE_CPU', 'camera_angle_deg': 30}
BYTE_CHECK jam_jar storybook= True plain= True
METRICS french_press {"before": {"triangle_count": 2676, "glb": {"path": "french_press.glb", "bytes": 78948, "sha256": "1fb84dabe7b0dcf56d7b0affd9d3848833fab7bd3db235f78d07f53949f1326e"}, "runtime_seconds": {"worker": 3.9074729999992996, "export_and_checks": 0.6261458000080893, "render": 3.236588200001279, "process": 4.862374899996212}, "dimensions_m": [0.17000000178813934, 0.24500000476837158, 0.10700000077486038]}, "storybook": {"triangle_count": 4468, "glb": {"path": "french_press.glb", "bytes": 94156, "sha256": "67bf97a4039a9816b412da1cc17351f46b45aedffb49d16f5d153843fd0eea17"}, "runtime_seconds": {"worker": 11.622410600000876, "export_and_checks": 0.6012947999988683, "render": 10.946352700004354, "process": 12.542102699997486}, "dimensions_m": [0.17000000178813934, 0.24500000476837158, 0.10700000077486038]}, "plain": {"triangle_count": 2676, "glb": {"path": "french_press.glb", "bytes": 78948, "sha256": "1fb84dabe7b0dcf56d7b0affd9d3848833fab7bd3db235f78d07f53949f1326e"}, "runtime_seconds": {"worker": 10.691201100009494, "export_and_checks": 0.6001178000005893, "render": 10.047349000000395, "process": 11.6829088999948}, "dimensions_m": [0.17000000178813934, 0.24500000476837158, 0.10700000077486038]}}
PREVIEW french_press 211669 bytes
RENDER french_press {'version': '5.2.2 LTS', 'build_hash': 'd13f752e3b9c'} {'path': 'french_press.png', 'pixels': [512, 384], 'engine': 'CYCLES', 'device': 'CPU', 'samples': 64, 'denoising': 'OPENIMAGEDENOISE_CPU', 'camera_angle_deg': 30}
BYTE_CHECK french_press storybook= True plain= True
Traceback (most recent call last):
  File "C:\Users\blues\AppData\Local\Temp\enfractal-11-50be820614484a4799a4167c56a888af\deliver.py", line 42, in <module>
    assert (previews/'plain-vs-storybook.png').stat().st_size<800000
           ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
AssertionError
```

Final delivery audit command, **exit 1** (same oversized comparison):

```powershell
python -B "C:\Users\blues\AppData\Local\Temp\enfractal-11-50be820614484a4799a4167c56a888af\final-audit.py"
```

The scratch audit checks branch, all recipe/report text as UTF-8/LF, Python parsing,
PNG headers/sizes, scope, and absence of checkout GLBs/work folders. Raw output:

```text
BRANCH codex/11-storybook-recipes
UTF8_LF_AND_PYTHON_PARSE PASS 16 text files
CHECKOUT_GLBS []
CHECKOUT_WORK_FOLDERS []
OUT_OF_SCOPE_CHANGED_PATHS []
PNG cardboard_box.png 512x384 213059 bytes
PNG couch.png 512x384 237630 bytes
PNG french_press.png 512x384 211669 bytes
PNG gaming_laptop.png 512x384 228020 bytes
PNG jam_jar.png 512x384 213525 bytes
Traceback (most recent call last):
  File "C:\Users\blues\AppData\Local\Temp\enfractal-11-50be820614484a4799a4167c56a888af\final-audit.py", line 24, in <module>
    assert len(raw)<(800000 if path.stem=='plain-vs-storybook' else 400000)
           ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
AssertionError
```


The final audit passed UTF-8/LF, Python parsing, scope and absence of checkout GLBs/
work folders, then failed on the comparison size. The report writer itself exited 0
(21,014 bytes before this correction). Its initial exit-0 labels for the two failed
scratch commands were corrected here; the raw failures above are retained.

Unfinished: compliant comparison-sheet compression, Godot glass integration, and
founder appearance approval. No other scope changes are requested.
