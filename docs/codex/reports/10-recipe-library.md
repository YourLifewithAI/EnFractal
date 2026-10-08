# Brief 10: recipe library

**All three authorized failures fixed. The whole suite passed: 7 tests,
49 builds, 270.255 seconds, exit 0.** A five-build targeted check passed first.

The alternate couch left an 8.3 mm seat frame but requested a 14.94 mm bevel.
Opposing bevels collapsed faces at Blender's overlap limit. The shared recipe panel
constructor now caps each bevel at one quarter of the part's thinnest dimension
(2.075 mm for that frame), preserving a flat strip. The alternate now has zero
degenerate triangles. Added 24 builds covering all eight couch parameter endpoint
combinations at small, default and large sizes. Every range in `specs.py` remains
unchanged; the README explains the minimum thickness and bevel cap.

Flat-colour panels now remove unused cube UV layers before beveling. This removes
the previously observed UV-byte jitter. All five default recipes rebuilt
byte-identically, so the determinism assertion remains for all five. No UV or
position quantisation was needed. Tests also assert that the four entirely
flat-colour recipes export no UV attributes. Geometry/contract checks are intact.

Refreshed the five default CPU previews and metrics below. Triangle counts and
bounds are unchanged; UV removal reduced four GLBs. Glass previews remain noisy.
Founder appearance approval, playable collision and Godot glass behaviour remain
unverified. No new failures, scope exceptions or decisions. No installs, paid APIs,
real-place data, GPU use, commits or pushes; cost $0, GPU time 0. The integrator
commits and runs the branch scope checker. Game runners were not run because
their writes are outside this brief and engine code is unchanged.

| Default recipe | Triangles | Bounds [W,H,D] m | GLB bytes | Build seconds |
|---|---|---|---|---|
| cardboard_box | 972 | [0.400, 0.250, 0.300] | 56724 | 4.604 |
| couch | 1512 | [2.100, 0.830, 0.950] | 81432 | 4.430 |
| gaming_laptop | 8100 | [0.358, 0.240, 0.278] | 431892 | 4.988 |
| jam_jar | 3116 | [0.084, 0.095, 0.084] | 79096 | 6.350 |
| french_press | 2676 | [0.170, 0.245, 0.107] | 78948 | 4.933 |

Bounds rounded to three decimals; raw receipts below retain full precision.
Build seconds measure launch through checks/render/publication preparation.
Receipts identify Blender 5.2.2 LTS, build `d13f752e3b9c`.
Proportion sources and assumed values remain in
[the README](../../../pipeline/recipes/README.md#sources-for-proportions).

## Receipt-to-asset mapping

| Receipt field | `contracts/asset.schema.json` destination or Capture decision |
|---|---|
| `schema`, `version` | Recipe receipt identifier only; cannot be used as the asset schema/version. |
| `inputs_used.recipe` | Hint for Capture's `category`; Capture assigns `asset_id`, `display_name`, `category_group` and `tier`. |
| `inputs_used.size_m`, `dimensions_m` | `dimensions_m`; use validated exported dimensions. |
| `inputs_used.colours`, `materials` | `materials[].slot`, `.role`, `.base_color`; latter already matches the contract's entry shape. |
| `inputs_used.params`, all input `source` markers | Provenance/audit evidence; given/default does not mean measured/assumed. Capture retains measurement provenance separately. |
| `pivot`, `root_transform`, `bounds`, `template_fit_scale_xyz_blender` | Convert receipt `bottom_centre` to the contract's `pivot=bottom_center`; other values are verification evidence, with no direct asset fields. |
| `triangle_count`, `glb.path` | `geometry.kind=mesh`, `geometry.triangle_count`, `geometry.mesh` after packaging. |
| `glb.sha256`, `glb.bytes`, `glb.path` | `files[]` entry: `sha256`, `bytes`, `path`; packaging preserves hashed bytes. |
| `blender`, `runtime_seconds`, `checks`, `parts` | Build provenance/audit evidence; not direct asset properties. Capture supplies the required provenance kind/license/time and may retain the receipt as a listed file. |
| `preview` | Optional packaged preview/file entry; Capture computes its hash/bytes. No dedicated asset preview property. |
| `collision_shapes`, `collision_note` | Hints to generate `collision.kind=convex_decomposition`, optional collision GLB and `hull_count`; shapes are **not** a contract collision block. Alternatively Capture may choose `collision.kind=box` using overall dimensions. |

Capture must supply physics/mass/movability, affordances, articulated parts if any,
walkable surfaces, style variants and review status. Closed visual parts do not establish
playable collision. Collision hints fill hollow vessels and over-cover rotated panels;
the Capture lane must review them. No contract changes are requested.

## Notes towards C7

Tell the AI the fixed recipe names, colour slots, bounded params, metre units and
`size_m` order: width/height/depth. The outer box includes the entire current pose,
including handles, knobs, lifted lids or open flaps. Defaults and internal proportions
are stylised assumptions, not replicas. A closed laptop needs its closed height;
independent axis fitting can distort an inconsistent measured box. Report measurement
confidence upstream and treat scene names as data. The AI receives job results/receipts;
it should never control output/input paths, `BLENDER`, the process environment,
Blender command line, worker code, exporters or validation. A trusted local adapter
owns these. This CLI is author tooling; the game companion retains its no-files/no-shell
boundary.

## Current raw evidence

Full logs and rebuilt artifacts retained at:

```text
C:\Users\blues\AppData\Local\Temp\enfractal-10-fixes-2880972d6d674bf89923212c56b57cb8
```

Targeted command, **exit 0**:

```powershell
$env:PYTHONDONTWRITEBYTECODE='1'
$env:RECIPE_TEST_ARTIFACTS = python -B -c "import os, tempfile, uuid; path=os.path.join(tempfile.gettempdir(), 'enfractal-10-fixes-' + uuid.uuid4().hex); os.makedirs(path); print(path)"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output "EVIDENCE $env:RECIPE_TEST_ARTIFACTS"
@'
import hashlib
from pipeline.recipes.specs import RECIPES
from pipeline.recipes.tests.test_recipes import BuildTests
BuildTests.setUpClass()
try:
    test = BuildTests()
    test.build_case('couch', 'alternate', RECIPES['couch']['example_size_m'],
                    {'cushion_count': 6, 'seat_height_fraction': 0.4, 'leg_height_fraction': 0.25})
    for name in ('cardboard_box', 'gaming_laptop'):
        size = RECIPES[name]['example_size_m']
        _, original = test.build_case(name, 'default', size)
        _, repeated = test.build_case(name, 'repeat', size)
        test.assertEqual(original, repeated, f'{name}: GLB bytes differ')
        print(f'DETERMINISM {name} byte_identical=True sha256={hashlib.sha256(repeated).hexdigest()}', flush=True)
finally:
    BuildTests.tearDownClass()
'@ | python -B 2>&1 | Tee-Object -FilePath (Join-Path $env:RECIPE_TEST_ARTIFACTS 'targeted.log')
exit $LASTEXITCODE
```

```text
EVIDENCE C:\Users\blues\AppData\Local\Temp\enfractal-10-fixes-2880972d6d674bf89923212c56b57cb8
PASS couch: triangles=2160 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=116524 seconds=11.491
PASS cardboard_box: triangles=972 bounds=[0.4000000059604645, 0.25, 0.30000001192092896] bytes=56724 seconds=5.075
PASS cardboard_box: triangles=972 bounds=[0.4000000059604645, 0.25, 0.30000001192092896] bytes=56724 seconds=4.969
DETERMINISM cardboard_box byte_identical=True sha256=2aa978a9fe1e798e29e44bb201defae68b4c4d56fec9d2df157989d0f920a69f
PASS gaming_laptop: triangles=8100 bounds=[0.3580000102519989, 0.23999999463558197, 0.27799999713897705] bytes=431892 seconds=5.907
PASS gaming_laptop: triangles=8100 bounds=[0.3580000102519989, 0.23999999463558197, 0.27799999713897705] bytes=431892 seconds=6.129
DETERMINISM gaming_laptop byte_identical=True sha256=cfe5b1d50adc99337ce0b254982f7968d5df54323ce2b9c6bb604eac2c736612
BUILD_TEST_SECONDS 34.418
ARTIFACTS C:\Users\blues\AppData\Local\Temp\enfractal-10-fixes-2880972d6d674bf89923212c56b57cb8\recipe-tests-bc6835ab5d544553a081c70c093e30c0
```

Whole-suite command, run once after fixes, **exit 0**. `-f` stops on a new failure.
PowerShell's NativeCommandError formatting is from unittest's stderr stream;
the test summary and process exit code are successful.

```powershell
$env:PYTHONDONTWRITEBYTECODE='1'
$env:RECIPE_TEST_ARTIFACTS='C:\Users\blues\AppData\Local\Temp\enfractal-10-fixes-2880972d6d674bf89923212c56b57cb8'
python -B -m unittest pipeline.recipes.tests -v -f 2>&1 | Tee-Object -FilePath (Join-Path $env:RECIPE_TEST_ARTIFACTS 'suite.log')
exit $LASTEXITCODE
```

```text
PASS cardboard_box: triangles=972 bounds=[0.4000000059604645, 0.25, 0.30000001192092896] bytes=56724 seconds=4.604
PASS cardboard_box: triangles=972 bounds=[0.20000000298023224, 0.125, 0.15000000596046448] bytes=56740 seconds=5.064
PASS cardboard_box: triangles=972 bounds=[0.800000011920929, 0.5, 0.6000000238418579] bytes=56708 seconds=5.285
PASS couch: triangles=1512 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=81432 seconds=4.430
PASS couch: triangles=1512 bounds=[1.0499999523162842, 0.41499999165534973, 0.4749999940395355] bytes=81464 seconds=4.362
PASS couch: triangles=1512 bounds=[4.199999809265137, 1.659999966621399, 1.899999976158142] bytes=81396 seconds=4.578
PASS gaming_laptop: triangles=8100 bounds=[0.3580000102519989, 0.23999999463558197, 0.27799999713897705] bytes=431892 seconds=4.988
PASS gaming_laptop: triangles=8100 bounds=[0.17900000512599945, 0.11999999731779099, 0.13899999856948853] bytes=432020 seconds=5.254
PASS gaming_laptop: triangles=8100 bounds=[0.7160000205039978, 0.47999998927116394, 0.5559999942779541] bytes=431804 seconds=5.111
PASS jam_jar: triangles=3116 bounds=[0.08399999886751175, 0.0949999988079071, 0.08399999886751175] bytes=79096 seconds=6.350
PASS jam_jar: triangles=3116 bounds=[0.041999999433755875, 0.04749999940395355, 0.041999999433755875] bytes=79108 seconds=6.532
PASS jam_jar: triangles=3116 bounds=[0.1679999977350235, 0.1899999976158142, 0.1679999977350235] bytes=79084 seconds=6.496
PASS french_press: triangles=2676 bounds=[0.17000000178813934, 0.24500000476837158, 0.10700000077486038] bytes=78948 seconds=4.933
PASS french_press: triangles=2676 bounds=[0.08500000089406967, 0.12250000238418579, 0.05350000038743019] bytes=78984 seconds=4.708
PASS french_press: triangles=2676 bounds=[0.3400000035762787, 0.49000000953674316, 0.21400000154972076] bytes=78932 seconds=4.690
python : test_01_default_small_large (pipeline.recipes.tests.test_recipes.BuildTests.test_01_default_small_large) ... 
ok
At line:4 char:1
+ python -B -m unittest pipeline.recipes.tests -v -f 2>&1 | Tee-Object  ...
+ ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : NotSpecified: (test_01_default...l_large) ... ok:String) [], RemoteException
    + FullyQualifiedErrorId : NativeCommandError
 
PASS cardboard_box: triangles=972 bounds=[0.4000000059604645, 0.25, 0.30000001192092896] bytes=56008 seconds=4.569
PASS couch: triangles=2160 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=116524 seconds=4.129
PASS gaming_laptop: triangles=8100 bounds=[0.3580000102519989, 0.23999999463558197, 0.27799999713897705] bytes=430968 seconds=5.124
PASS jam_jar: triangles=3116 bounds=[0.08399999886751175, 0.0949999988079071, 0.08399999886751175] bytes=78112 seconds=7.027
PASS french_press: triangles=2676 bounds=[0.17000000178813934, 0.24500000476837158, 0.10700000077486038] bytes=78812 seconds=4.472
test_02_alternate_states (pipeline.recipes.tests.test_recipes.BuildTests.test_02_alternate_states) ... ok
PASS cardboard_box: triangles=972 bounds=[0.4000000059604645, 0.25, 0.30000001192092896] bytes=56724 seconds=4.402
DETERMINISM cardboard_box byte_identical=True sha256=2aa978a9fe1e798e29e44bb201defae68b4c4d56fec9d2df157989d0f920a69f
PASS couch: triangles=1512 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=81432 seconds=4.079
DETERMINISM couch byte_identical=True sha256=56f9c3df07de2c7b73923cd30dde4e7557f032e51cc544bff45c74b944a1d970
PASS gaming_laptop: triangles=8100 bounds=[0.3580000102519989, 0.23999999463558197, 0.27799999713897705] bytes=431892 seconds=5.285
DETERMINISM gaming_laptop byte_identical=True sha256=cfe5b1d50adc99337ce0b254982f7968d5df54323ce2b9c6bb604eac2c736612
PASS jam_jar: triangles=3116 bounds=[0.08399999886751175, 0.0949999988079071, 0.08399999886751175] bytes=79096 seconds=6.771
DETERMINISM jam_jar byte_identical=True sha256=29e256ce66c590943a91df0083ec95a0e480d9ab7121ea7fa9f6baabd3d18228
PASS french_press: triangles=2676 bounds=[0.17000000178813934, 0.24500000476837158, 0.10700000077486038] bytes=78948 seconds=4.953
DETERMINISM french_press byte_identical=True sha256=1fb84dabe7b0dcf56d7b0affd9d3848833fab7bd3db235f78d07f53949f1326e
test_03_determinism (pipeline.recipes.tests.test_recipes.BuildTests.test_03_determinism) ... ok
PASS couch: triangles=1080 bounds=[1.0499999523162842, 0.41499999165534973, 0.4749999940395355] bytes=58576 seconds=5.931
COUCH_EXTREMES params={"cushion_count": 1, "leg_height_fraction": 0.08, "seat_height_fraction": 0.4} size=small PASS
PASS couch: triangles=1080 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=58548 seconds=4.761
COUCH_EXTREMES params={"cushion_count": 1, "leg_height_fraction": 0.08, "seat_height_fraction": 0.4} size=default PASS
PASS couch: triangles=1080 bounds=[4.199999809265137, 1.659999966621399, 1.899999976158142] bytes=58532 seconds=4.762
COUCH_EXTREMES params={"cushion_count": 1, "leg_height_fraction": 0.08, "seat_height_fraction": 0.4} size=large PASS
PASS couch: triangles=1080 bounds=[1.0499999523162842, 0.41499999165534973, 0.4749999940395355] bytes=59352 seconds=4.900
COUCH_EXTREMES params={"cushion_count": 1, "leg_height_fraction": 0.25, "seat_height_fraction": 0.4} size=small PASS
PASS couch: triangles=1080 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=59328 seconds=4.708
COUCH_EXTREMES params={"cushion_count": 1, "leg_height_fraction": 0.25, "seat_height_fraction": 0.4} size=default PASS
PASS couch: triangles=1080 bounds=[4.199999809265137, 1.659999966621399, 1.899999976158142] bytes=59312 seconds=4.720
COUCH_EXTREMES params={"cushion_count": 1, "leg_height_fraction": 0.25, "seat_height_fraction": 0.4} size=large PASS
PASS couch: triangles=1080 bounds=[1.0499999523162842, 0.41499999165534973, 0.4749999940395355] bytes=58572 seconds=4.789
COUCH_EXTREMES params={"cushion_count": 1, "leg_height_fraction": 0.08, "seat_height_fraction": 0.65} size=small PASS
PASS couch: triangles=1080 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=58548 seconds=4.660
COUCH_EXTREMES params={"cushion_count": 1, "leg_height_fraction": 0.08, "seat_height_fraction": 0.65} size=default PASS
PASS couch: triangles=1080 bounds=[4.199999809265137, 1.659999966621399, 1.899999976158142] bytes=58532 seconds=4.781
COUCH_EXTREMES params={"cushion_count": 1, "leg_height_fraction": 0.08, "seat_height_fraction": 0.65} size=large PASS
PASS couch: triangles=1080 bounds=[1.0499999523162842, 0.41499999165534973, 0.4749999940395355] bytes=58572 seconds=4.725
COUCH_EXTREMES params={"cushion_count": 1, "leg_height_fraction": 0.25, "seat_height_fraction": 0.65} size=small PASS
PASS couch: triangles=1080 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=58548 seconds=4.686
COUCH_EXTREMES params={"cushion_count": 1, "leg_height_fraction": 0.25, "seat_height_fraction": 0.65} size=default PASS
PASS couch: triangles=1080 bounds=[4.199999809265137, 1.659999966621399, 1.899999976158142] bytes=58532 seconds=4.805
COUCH_EXTREMES params={"cushion_count": 1, "leg_height_fraction": 0.25, "seat_height_fraction": 0.65} size=large PASS
PASS couch: triangles=2160 bounds=[1.0499999523162842, 0.41499999165534973, 0.4749999940395355] bytes=115808 seconds=5.117
COUCH_EXTREMES params={"cushion_count": 6, "leg_height_fraction": 0.08, "seat_height_fraction": 0.4} size=small PASS
PASS couch: triangles=2160 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=115748 seconds=6.738
COUCH_EXTREMES params={"cushion_count": 6, "leg_height_fraction": 0.08, "seat_height_fraction": 0.4} size=default PASS
PASS couch: triangles=2160 bounds=[4.199999809265137, 1.659999966621399, 1.899999976158142] bytes=115716 seconds=6.550
COUCH_EXTREMES params={"cushion_count": 6, "leg_height_fraction": 0.08, "seat_height_fraction": 0.4} size=large PASS
PASS couch: triangles=2160 bounds=[1.0499999523162842, 0.41499999165534973, 0.4749999940395355] bytes=116580 seconds=6.623
COUCH_EXTREMES params={"cushion_count": 6, "leg_height_fraction": 0.25, "seat_height_fraction": 0.4} size=small PASS
PASS couch: triangles=2160 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=116524 seconds=6.650
COUCH_EXTREMES params={"cushion_count": 6, "leg_height_fraction": 0.25, "seat_height_fraction": 0.4} size=default PASS
PASS couch: triangles=2160 bounds=[4.199999809265137, 1.659999966621399, 1.899999976158142] bytes=116492 seconds=6.263
COUCH_EXTREMES params={"cushion_count": 6, "leg_height_fraction": 0.25, "seat_height_fraction": 0.4} size=large PASS
PASS couch: triangles=2160 bounds=[1.0499999523162842, 0.41499999165534973, 0.4749999940395355] bytes=115796 seconds=6.274
COUCH_EXTREMES params={"cushion_count": 6, "leg_height_fraction": 0.08, "seat_height_fraction": 0.65} size=small PASS
PASS couch: triangles=2160 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=115748 seconds=6.264
COUCH_EXTREMES params={"cushion_count": 6, "leg_height_fraction": 0.08, "seat_height_fraction": 0.65} size=default PASS
PASS couch: triangles=2160 bounds=[4.199999809265137, 1.659999966621399, 1.899999976158142] bytes=115708 seconds=6.106
COUCH_EXTREMES params={"cushion_count": 6, "leg_height_fraction": 0.08, "seat_height_fraction": 0.65} size=large PASS
PASS couch: triangles=2160 bounds=[1.0499999523162842, 0.41499999165534973, 0.4749999940395355] bytes=115796 seconds=6.331
COUCH_EXTREMES params={"cushion_count": 6, "leg_height_fraction": 0.25, "seat_height_fraction": 0.65} size=small PASS
PASS couch: triangles=2160 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=115748 seconds=6.258
COUCH_EXTREMES params={"cushion_count": 6, "leg_height_fraction": 0.25, "seat_height_fraction": 0.65} size=default PASS
PASS couch: triangles=2160 bounds=[4.199999809265137, 1.659999966621399, 1.899999976158142] bytes=115708 seconds=6.222
COUCH_EXTREMES params={"cushion_count": 6, "leg_height_fraction": 0.25, "seat_height_fraction": 0.65} size=large PASS
test_04_couch_param_extremes (pipeline.recipes.tests.test_recipes.BuildTests.test_04_couch_param_extremes) ... ok
BUILD_TEST_SECONDS 270.124
ARTIFACTS C:\Users\blues\AppData\Local\Temp\enfractal-10-fixes-2880972d6d674bf89923212c56b57cb8\recipe-tests-5e04edfceb584b5b83d5e3d5d569c590
test_bad_inputs (pipeline.recipes.tests.test_recipes.InputTests.test_bad_inputs) ... ok
test_cli_refuses_before_launch (pipeline.recipes.tests.test_recipes.InputTests.test_cli_refuses_before_launch) ... ok
test_defaults_and_sources (pipeline.recipes.tests.test_recipes.InputTests.test_defaults_and_sources) ... ok

----------------------------------------------------------------------
Ran 7 tests in 270.255s

OK
```

Default metric extraction and preview refresh, **exit 0**:

```powershell
python -B 'C:\Users\blues\AppData\Local\Temp\enfractal-10-fixes-2880972d6d674bf89923212c56b57cb8\delivery.py' 2>&1 | Tee-Object -FilePath 'C:\Users\blues\AppData\Local\Temp\enfractal-10-fixes-2880972d6d674bf89923212c56b57cb8\delivery.log'
exit $LASTEXITCODE
```

The temp script used by that command:

```python
import json
from pathlib import Path
import re
import shutil

base = Path(__file__).resolve().parent

def read_log(path):
    raw = path.read_bytes()
    return raw.decode('utf-16' if raw.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig')

suite = read_log(base / 'suite.log')
assert re.search(r'^OK\s*$', suite, re.MULTILINE), 'Whole suite did not pass'
root = Path(re.search(r'^ARTIFACTS (.+)$', suite, re.MULTILINE)[1].strip())
previews = Path('pipeline/recipes/previews').resolve()
for name in ('cardboard_box', 'couch', 'gaming_laptop', 'jam_jar', 'french_press'):
    folder = root / name / 'default'
    receipt = json.loads((folder / (name + '.receipt.json')).read_text(encoding='utf-8'))
    preview = folder / (name + '.png')
    assert preview.stat().st_size < 400000
    shutil.copyfile(preview, previews / preview.name)
    print('DEFAULT', name, json.dumps({key: receipt[key] for key in
          ('triangle_count', 'dimensions_m', 'glb', 'runtime_seconds', 'blender')}))
    print('PREVIEW', preview.name, preview.stat().st_size, 'bytes')
print('SUITE_RECEIPTS', len(list(root.glob('*/*/*.receipt.json'))))
print('WORK_FOLDERS_REMAINING', list(root.rglob('.blender-work')))
print('INVALID_FOLDERS_REMAINING', list(base.glob('recipe-invalid-*')))
```

```text
DEFAULT cardboard_box {"triangle_count": 972, "dimensions_m": [0.4000000059604645, 0.25, 0.30000001192092896], "glb": {"path": "cardboard_box.glb", "bytes": 56724, "sha256": "2aa978a9fe1e798e29e44bb201defae68b4c4d56fec9d2df157989d0f920a69f"}, "runtime_seconds": {"worker": 3.6557250000041677, "export_and_checks": 0.6595128999906592, "render": 2.9702218000020366, "process": 4.603674099998898}, "blender": {"version": "5.2.2 LTS", "build_hash": "d13f752e3b9c"}}
PREVIEW cardboard_box.png 320992 bytes
DEFAULT couch {"triangle_count": 1512, "dimensions_m": [2.0999999046325684, 0.8299999833106995, 0.949999988079071], "glb": {"path": "couch.glb", "bytes": 81432, "sha256": "56f9c3df07de2c7b73923cd30dde4e7557f032e51cc544bff45c74b944a1d970"}, "runtime_seconds": {"worker": 3.4310760000080336, "export_and_checks": 0.6524416000029305, "render": 2.73045980000461, "process": 4.430013200006215}, "blender": {"version": "5.2.2 LTS", "build_hash": "d13f752e3b9c"}}
PREVIEW couch.png 293095 bytes
DEFAULT gaming_laptop {"triangle_count": 8100, "dimensions_m": [0.3580000102519989, 0.23999999463558197, 0.27799999713897705], "glb": {"path": "gaming_laptop.glb", "bytes": 431892, "sha256": "cfe5b1d50adc99337ce0b254982f7968d5df54323ce2b9c6bb604eac2c736612"}, "runtime_seconds": {"worker": 4.060295000002952, "export_and_checks": 0.8006685000000289, "render": 2.850397200003499, "process": 4.987724899998284}, "blender": {"version": "5.2.2 LTS", "build_hash": "d13f752e3b9c"}}
PREVIEW gaming_laptop.png 304768 bytes
DEFAULT jam_jar {"triangle_count": 3116, "dimensions_m": [0.08399999886751175, 0.0949999988079071, 0.08399999886751175], "glb": {"path": "jam_jar.glb", "bytes": 79096, "sha256": "29e256ce66c590943a91df0083ec95a0e480d9ab7121ea7fa9f6baabd3d18228"}, "runtime_seconds": {"worker": 5.440625999995973, "export_and_checks": 0.5786036000063177, "render": 4.689391200008686, "process": 6.350082499993732}, "blender": {"version": "5.2.2 LTS", "build_hash": "d13f752e3b9c"}}
PREVIEW jam_jar.png 350142 bytes
DEFAULT french_press {"triangle_count": 2676, "dimensions_m": [0.17000000178813934, 0.24500000476837158, 0.10700000077486038], "glb": {"path": "french_press.glb", "bytes": 78948, "sha256": "1fb84dabe7b0dcf56d7b0affd9d3848833fab7bd3db235f78d07f53949f1326e"}, "runtime_seconds": {"worker": 3.9999314000015147, "export_and_checks": 0.6207432000082918, "render": 3.336048600001959, "process": 4.933273899994674}, "blender": {"version": "5.2.2 LTS", "build_hash": "d13f752e3b9c"}}
PREVIEW french_press.png 317547 bytes
SUITE_RECEIPTS 49
WORK_FOLDERS_REMAINING []
INVALID_FOLDERS_REMAINING []
```

The earlier failed suite and decoded UV differences below are failing-before
evidence. Those results are historical and superseded by the current passing run.

## Previous authorized suite (historical; three failures now fixed)

Full logs and successful build artifacts retained at:

```text
C:\Users\blues\AppData\Local\Temp\enfractal-10-evidence-c2eaacc38ca34d509215354fe97e8796
```

Whole-suite command, corrected evidence-folder setup; **exit 1**:

```powershell
$env:PYTHONDONTWRITEBYTECODE='1'
$env:RECIPE_TEST_ARTIFACTS = python -B -c "import os, tempfile, uuid; path=os.path.join(tempfile.gettempdir(), 'enfractal-10-evidence-' + uuid.uuid4().hex); os.makedirs(path); print(path)"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output "EVIDENCE $env:RECIPE_TEST_ARTIFACTS"
python -B -m unittest pipeline.recipes.tests -v 2>&1 | Tee-Object -FilePath (Join-Path $env:RECIPE_TEST_ARTIFACTS 'suite.log')
exit $LASTEXITCODE
```

Raw suite output (PowerShell wraps some stderr lines; its NativeCommandError
formatting of unittest stderr is distinct from the three test failures):

```text
PASS cardboard_box: triangles=972 bounds=[0.4000000059604645, 0.25, 0.30000001192092896] bytes=73588 seconds=4.426
PASS cardboard_box: triangles=972 bounds=[0.20000000298023224, 0.125, 0.15000000596046448] bytes=73608 seconds=4.391
PASS cardboard_box: triangles=972 bounds=[0.800000011920929, 0.5, 0.6000000238418579] bytes=73572 seconds=4.455
PASS couch: triangles=1512 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=107668 seconds=4.277
PASS couch: triangles=1512 bounds=[1.0499999523162842, 0.41499999165534973, 0.4749999940395355] bytes=107700 seconds=4.340
PASS couch: triangles=1512 bounds=[4.199999809265137, 1.659999966621399, 1.899999976158142] bytes=107632 seconds=4.609
PASS gaming_laptop: triangles=8100 bounds=[0.3580000102519989, 0.23999999463558197, 0.27799999713897705] bytes=572660 seconds=5.377
PASS gaming_laptop: triangles=8100 bounds=[0.17900000512599945, 0.11999999731779099, 0.13899999856948853] bytes=572788 seconds=5.514
PASS gaming_laptop: triangles=8100 bounds=[0.7160000205039978, 0.47999998927116394, 0.5559999942779541] bytes=572572 seconds=5.391
PASS jam_jar: triangles=3116 bounds=[0.08399999886751175, 0.0949999988079071, 0.08399999886751175] bytes=79096 seconds=7.039
PASS jam_jar: triangles=3116 bounds=[0.041999999433755875, 0.04749999940395355, 0.041999999433755875] bytes=79108 seconds=7.006
PASS jam_jar: triangles=3116 bounds=[0.1679999977350235, 0.1899999976158142, 0.1679999977350235] bytes=79084 seconds=7.151
PASS french_press: triangles=2676 bounds=[0.17000000178813934, 0.24500000476837158, 0.10700000077486038] bytes=92068 seconds=5.063
PASS french_press: triangles=2676 bounds=[0.08500000089406967, 0.12250000238418579, 0.05350000038743019] bytes=92104 seconds=5.048
PASS french_press: triangles=2676 bounds=[0.3400000035762787, 0.49000000953674316, 0.21400000154972076] bytes=92052 seconds=5.246
python : test_01_default_small_large (pipeline.recipes.tests.test_recipes.BuildTests.test_01_default_small_large) ... 
ok
At line:6 char:1
+ python -B -m unittest pipeline.recipes.tests -v 2>&1 | Tee-Object -Fi ...
+ ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : NotSpecified: (test_01_default...l_large) ... ok:String) [], RemoteException
    + FullyQualifiedErrorId : NativeCommandError
 
PASS cardboard_box: triangles=972 bounds=[0.4000000059604645, 0.25, 0.30000001192092896] bytes=72872 seconds=4.753
test_02_alternate_states (pipeline.recipes.tests.test_recipes.BuildTests.test_02_alternate_states) ... 
  test_02_alternate_states (pipeline.recipes.tests.test_recipes.BuildTests.test_02_alternate_states) (recipe='couch') 
... FAIL
PASS gaming_laptop: triangles=8100 bounds=[0.3580000102519989, 0.23999999463558197, 0.27799999713897705] bytes=571736 seconds=5.592
PASS jam_jar: triangles=3116 bounds=[0.08399999886751175, 0.0949999988079071, 0.08399999886751175] bytes=78112 seconds=6.790
PASS french_press: triangles=2676 bounds=[0.17000000178813934, 0.24500000476837158, 0.10700000077486038] bytes=91928 seconds=4.656
PASS cardboard_box: triangles=972 bounds=[0.4000000059604645, 0.25, 0.30000001192092896] bytes=73588 seconds=4.865
test_03_determinism (pipeline.recipes.tests.test_recipes.BuildTests.test_03_determinism) ... 
  test_03_determinism (pipeline.recipes.tests.test_recipes.BuildTests.test_03_determinism) (recipe='cardboard_box') 
... FAIL
PASS couch: triangles=1512 bounds=[2.0999999046325684, 0.8299999833106995, 0.949999988079071] bytes=107668 seconds=4.584
DETERMINISM couch byte_identical=True sha256=a6dc9902b3aac6e52f868e6e828130909cbaeda9ed30bfe801b8bb390a7c731c
PASS gaming_laptop: triangles=8100 bounds=[0.3580000102519989, 0.23999999463558197, 0.27799999713897705] bytes=572660 seconds=5.784
  test_03_determinism (pipeline.recipes.tests.test_recipes.BuildTests.test_03_determinism) (recipe='gaming_laptop') 
... FAIL
PASS jam_jar: triangles=3116 bounds=[0.08399999886751175, 0.0949999988079071, 0.08399999886751175] bytes=79096 seconds=7.054
DETERMINISM jam_jar byte_identical=True sha256=29e256ce66c590943a91df0083ec95a0e480d9ab7121ea7fa9f6baabd3d18228
PASS french_press: triangles=2676 bounds=[0.17000000178813934, 0.24500000476837158, 0.10700000077486038] bytes=92068 seconds=5.306
DETERMINISM french_press byte_identical=True sha256=839f14134d731dac9bb246db204541b68efd825f9637cce62a5b21e964be12a2
BUILD_TEST_SECONDS 133.359
ARTIFACTS C:\Users\blues\AppData\Local\Temp\enfractal-10-evidence-c2eaacc38ca34d509215354fe97e8796\recipe-tests-558f949ad5d4482ea051079b7e34ba58
test_bad_inputs (pipeline.recipes.tests.test_recipes.InputTests.test_bad_inputs) ... ok
test_cli_refuses_before_launch (pipeline.recipes.tests.test_recipes.InputTests.test_cli_refuses_before_launch) ... ok
test_defaults_and_sources (pipeline.recipes.tests.test_recipes.InputTests.test_defaults_and_sources) ... ok

======================================================================
FAIL: test_02_alternate_states (pipeline.recipes.tests.test_recipes.BuildTests.test_02_alternate_states) 
(recipe='couch')
----------------------------------------------------------------------
Traceback (most recent call last):
  File "C:\dev\EnFractal-codex\10-recipe-library\pipeline\recipes\tests\test_recipes.py", line 155, in 
test_02_alternate_states
    receipt, _ = self.build_case(name, 'alternate', RECIPES[name]['example_size_m'], params)
                 ~~~~~~~~~~~~~~~^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
  File "C:\dev\EnFractal-codex\10-recipe-library\pipeline\recipes\tests\test_recipes.py", line 102, in build_case
    self.assertEqual(process.returncode, 0, process.stdout + process.stderr)
    ~~~~~~~~~~~~~~~~^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
AssertionError: 1 != 0 : ERROR: Blender exited 1; see C:\Users\blues\AppData\Local\Temp\enfractal-10-evidence-c2eaacc38
ca34d509215354fe97e8796\recipe-tests-558f949ad5d4482ea051079b7e34ba58\couch\alternate\couch.log


======================================================================
FAIL: test_03_determinism (pipeline.recipes.tests.test_recipes.BuildTests.test_03_determinism) (recipe='cardboard_box')
----------------------------------------------------------------------
Traceback (most recent call last):
  File "C:\dev\EnFractal-codex\10-recipe-library\pipeline\recipes\tests\test_recipes.py", line 164, in 
test_03_determinism
    self.assertEqual(original, repeated, f'{name}: GLB bytes differ')
    ~~~~~~~~~~~~~~~~^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
AssertionError: b'glT[142289 chars]=?\xb4;\xc0>\x00\x00@?\xb4;\xc0>\x00\x00@?\xb4[44248 chars]x00?' != b'glT[142289 
chars]=?\xb3;\xc0>\x00\x00@?\xb3;\xc0>\x00\x00@?\xb3[44248 chars]x00?' : cardboard_box: GLB bytes differ

======================================================================
FAIL: test_03_determinism (pipeline.recipes.tests.test_recipes.BuildTests.test_03_determinism) (recipe='gaming_laptop')
----------------------------------------------------------------------
Traceback (most recent call last):
  File "C:\dev\EnFractal-codex\10-recipe-library\pipeline\recipes\tests\test_recipes.py", line 164, in 
test_03_determinism
    self.assertEqual(original, repeated, f'{name}: GLB bytes differ')
    ~~~~~~~~~~~~~~~~^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
AssertionError: b'glT[191522 chars]00 ?\x08\t\x81>\x00\x00 ?\x08\t\x81>\x00\x00 ?[1333416 chars]x00?' != b'glT[191522 
chars]00 ?\n\t\x81>\x00\x00 ?\n\t\x81>\x00\x00 ?\n\t[1333416 chars]x00?' : gaming_laptop: GLB bytes differ

----------------------------------------------------------------------
Ran 6 tests in 133.466s

FAILED (failures=3)

```

Read-only failure diagnosis command; **exit 0**:

```powershell
Get-Content 'C:\Users\blues\AppData\Local\Temp\enfractal-10-evidence-c2eaacc38ca34d509215354fe97e8796\recipe-tests-558f949ad5d4482ea051079b7e34ba58\couch\alternate\couch.log' -Tail 40
Get-Content pipeline/recipes/couch.py
```

Relevant raw Blender output:

```text
Blender 5.2.2 LTS (hash d13f752e3b9c built 2026-09-15 01:37:04)

Blender quit
Traceback (most recent call last):
  File "C:\dev\EnFractal-codex\10-recipe-library\pipeline\recipes\worker.py", line 245, in <module>
    main()
    ~~~~^^
  File "C:\dev\EnFractal-codex\10-recipe-library\pipeline\recipes\worker.py", line 163, in main
    root, topology, box, scale = fit_and_check(parts, name, data['size_m'])
                                 ~~~~~~~~~~~~~^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
  File "C:\dev\EnFractal-codex\10-recipe-library\pipeline\recipes\worker.py", line 115, in fit_and_check
    raise ValueError(f'{obj.name}: invalid closed part: nonmanifold={nonmanifold}, '
                     f'bad_vertices={bad_vertices}, degenerate={degenerate}, volume={volume}')
ValueError: seat_frame: invalid closed part: nonmanifold=0, bad_vertices=0, degenerate=24, volume=0.013431297681386623

Error: script failed, file: 'C:\dev\EnFractal-codex\10-recipe-library\pipeline\recipes\worker.py', exiting.

```

Default metrics, repeated-byte diagnosis and preview delivery: **exit 0**.
The Python stdin body below was run in PowerShell via a single-quoted here-string
piped to `python -B 2>&1 | Tee-Object -FilePath '<evidence folder>\delivery.log'`,
followed by `exit $LASTEXITCODE`. No builds were launched by this command.

```python
import hashlib, json, shutil, struct
from pathlib import Path
base = Path(r'C:\Users\blues\AppData\Local\Temp\enfractal-10-evidence-c2eaacc38ca34d509215354fe97e8796')
root = base / 'recipe-tests-558f949ad5d4482ea051079b7e34ba58'
previews = Path('pipeline/recipes/previews').resolve()
previews.mkdir(exist_ok=True)
for name in ('cardboard_box', 'couch', 'gaming_laptop', 'jam_jar', 'french_press'):
    folder = root / name / 'default'
    receipt = json.loads((folder / (name + '.receipt.json')).read_text(encoding='utf-8'))
    assert all(item['passed'] for item in receipt['checks'].values())
    preview = folder / (name + '.png')
    assert preview.stat().st_size < 400000
    shutil.copyfile(preview, previews / preview.name)
    print('DEFAULT', name, json.dumps({key: receipt[key] for key in ('triangle_count', 'dimensions_m', 'glb', 'runtime_seconds', 'blender')}))
    print('PREVIEW', preview.name, preview.stat().st_size, 'bytes')
    original = (folder / (name + '.glb')).read_bytes()
    repeated = (root / name / 'repeat' / (name + '.glb')).read_bytes()
    print('DETERMINISM', name, 'byte_identical=' + str(original == repeated), 'default_sha256=' + hashlib.sha256(original).hexdigest(), 'repeat_sha256=' + hashlib.sha256(repeated).hexdigest())
    if original != repeated:
        length = struct.unpack_from('<I', original, 12)[0]
        doc = json.loads(original[20:20 + length])
        assert original[:28 + length] == repeated[:28 + length]
        binary_a, binary_b = original[28 + length:], repeated[28 + length:]
        differences = []
        for mesh in doc['meshes']:
            for primitive in mesh['primitives']:
                for semantic, index in primitive['attributes'].items():
                    acc = doc['accessors'][index]
                    view = doc['bufferViews'][acc['bufferView']]
                    start = view.get('byteOffset', 0) + acc.get('byteOffset', 0)
                    count = acc['count'] * {'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[acc['type']]
                    a, b = binary_a[start:start + count * 4], binary_b[start:start + count * 4]
                    if a != b:
                        delta = max(abs(x[0] - y[0]) for x, y in zip(struct.iter_unpack('<f', a), struct.iter_unpack('<f', b)))
                        differences.append({'mesh': mesh['name'], 'attribute': semantic, 'max_float_delta': delta})
        print('BINARY_DIFFERENCES', name, 'identical_json=True', json.dumps(differences))
print('WORK_FOLDERS_REMAINING', list(root.rglob('.blender-work')))
print('INVALID_FOLDERS_REMAINING', list(base.glob('recipe-invalid-*')))
print('FAILED_COUCH_GLB_EXISTS', (root / 'couch/alternate/couch.glb').exists())
```

Raw output:

```text
DEFAULT cardboard_box {"triangle_count": 972, "dimensions_m": [0.4000000059604645, 0.25, 0.30000001192092896], "glb": {"path": "cardboard_box.glb", "bytes": 73588, "sha256": "177c6cbdf5a8406a2803bf53b858be427a69f60810e9e18d85d0c03602bb03c1"}, "runtime_seconds": {"worker": 3.438861300004646, "export_and_checks": 0.6131558000051882, "render": 2.793879900011234, "process": 4.425603600000613}, "blender": {"version": "5.2.2 LTS", "build_hash": "d13f752e3b9c"}}
PREVIEW cardboard_box.png 320992 bytes
DETERMINISM cardboard_box byte_identical=False default_sha256=177c6cbdf5a8406a2803bf53b858be427a69f60810e9e18d85d0c03602bb03c1 repeat_sha256=94e200e97170673c93b3dfdd67198fabf8e910d25397926822775d35d49fa860
BINARY_DIFFERENCES cardboard_box identical_json=True [{"mesh": "Cube.002", "attribute": "TEXCOORD_0", "max_float_delta": 2.9802322387695312e-08}]
DEFAULT couch {"triangle_count": 1512, "dimensions_m": [2.0999999046325684, 0.8299999833106995, 0.949999988079071], "glb": {"path": "couch.glb", "bytes": 107668, "sha256": "a6dc9902b3aac6e52f868e6e828130909cbaeda9ed30bfe801b8bb390a7c731c"}, "runtime_seconds": {"worker": 3.3443992999964394, "export_and_checks": 0.621110400010366, "render": 2.6754134000075283, "process": 4.277087599999504}, "blender": {"version": "5.2.2 LTS", "build_hash": "d13f752e3b9c"}}
PREVIEW couch.png 293095 bytes
DETERMINISM couch byte_identical=True default_sha256=a6dc9902b3aac6e52f868e6e828130909cbaeda9ed30bfe801b8bb390a7c731c repeat_sha256=a6dc9902b3aac6e52f868e6e828130909cbaeda9ed30bfe801b8bb390a7c731c
DEFAULT gaming_laptop {"triangle_count": 8100, "dimensions_m": [0.3580000102519989, 0.23999999463558197, 0.27799999713897705], "glb": {"path": "gaming_laptop.glb", "bytes": 572660, "sha256": "a05fec77788899611467fb9a0d465c4e638f1115a85ce55c4a81da542ed4b005"}, "runtime_seconds": {"worker": 4.476049299992155, "export_and_checks": 0.8660103000001982, "render": 3.1755054999957792, "process": 5.377334600008908}, "blender": {"version": "5.2.2 LTS", "build_hash": "d13f752e3b9c"}}
PREVIEW gaming_laptop.png 304727 bytes
DETERMINISM gaming_laptop byte_identical=False default_sha256=a05fec77788899611467fb9a0d465c4e638f1115a85ce55c4a81da542ed4b005 repeat_sha256=bc6e88f6ca0735b50d7d201d17ad9fa48cab7591c37bb3bbd3e6482e53e95ab4
BINARY_DIFFERENCES gaming_laptop identical_json=True [{"mesh": "Cube.008", "attribute": "TEXCOORD_0", "max_float_delta": 5.960464477539063e-08}, {"mesh": "Cube.010", "attribute": "TEXCOORD_0", "max_float_delta": 5.960464477539063e-08}, {"mesh": "Cube.016", "attribute": "TEXCOORD_0", "max_float_delta": 5.960464477539063e-08}, {"mesh": "Cube.021", "attribute": "TEXCOORD_0", "max_float_delta": 5.960464477539063e-08}, {"mesh": "Cube.028", "attribute": "TEXCOORD_0", "max_float_delta": 5.960464477539063e-08}, {"mesh": "Cube.044", "attribute": "TEXCOORD_0", "max_float_delta": 5.960464477539063e-08}, {"mesh": "Cube.056", "attribute": "TEXCOORD_0", "max_float_delta": 5.960464477539063e-08}, {"mesh": "Cube.058", "attribute": "TEXCOORD_0", "max_float_delta": 5.960464477539063e-08}]
DEFAULT jam_jar {"triangle_count": 3116, "dimensions_m": [0.08399999886751175, 0.0949999988079071, 0.08399999886751175], "glb": {"path": "jam_jar.glb", "bytes": 79096, "sha256": "29e256ce66c590943a91df0083ec95a0e480d9ab7121ea7fa9f6baabd3d18228"}, "runtime_seconds": {"worker": 6.11038009999902, "export_and_checks": 0.5907084999926155, "render": 5.339348900000914, "process": 7.038588399998844}, "blender": {"version": "5.2.2 LTS", "build_hash": "d13f752e3b9c"}}
PREVIEW jam_jar.png 350142 bytes
DETERMINISM jam_jar byte_identical=True default_sha256=29e256ce66c590943a91df0083ec95a0e480d9ab7121ea7fa9f6baabd3d18228 repeat_sha256=29e256ce66c590943a91df0083ec95a0e480d9ab7121ea7fa9f6baabd3d18228
DEFAULT french_press {"triangle_count": 2676, "dimensions_m": [0.17000000178813934, 0.24500000476837158, 0.10700000077486038], "glb": {"path": "french_press.glb", "bytes": 92068, "sha256": "839f14134d731dac9bb246db204541b68efd825f9637cce62a5b21e964be12a2"}, "runtime_seconds": {"worker": 4.12658170000941, "export_and_checks": 0.6185699000052409, "render": 3.4606668000051286, "process": 5.062883000005968}, "blender": {"version": "5.2.2 LTS", "build_hash": "d13f752e3b9c"}}
PREVIEW french_press.png 317547 bytes
DETERMINISM french_press byte_identical=True default_sha256=839f14134d731dac9bb246db204541b68efd825f9637cce62a5b21e964be12a2 repeat_sha256=839f14134d731dac9bb246db204541b68efd825f9637cce62a5b21e964be12a2
WORK_FOLDERS_REMAINING []
INVALID_FOLDERS_REMAINING []
FAILED_COUCH_GLB_EXISTS False

```

Report generation initially exited **1** because PowerShell's Tee-Object wrote
UTF-16 evidence logs and the report script attempted UTF-8 decoding:

```text
UnicodeDecodeError: 'utf-8' codec can't decode byte 0xff in position 0: invalid start byte
```

Corrected the evidence reader to detect the BOM. The report is UTF-8/LF;
this was a report-generation error, without a sandbox denial or test rerun.

## Earlier raw evidence (historical; temp-directory bug now fixed)

Commands ran in `C:\dev\EnFractal-codex\10-recipe-library` on branch
`codex/10-recipe-library`. No commit was made.

Initial read/discovery batch (exit 1 at unavailable `rg`; relevant output):

```powershell
git status --short
git branch --show-current
rg --files pipeline docs/codex/spikes/09-blender contracts docs/runs | Select-Object -First 100
```

```text
codex/10-recipe-library
rg : The term 'rg' is not recognized as the name of a cmdlet, function, script file, or operable program.
```

Read-only discovery continued with PowerShell built-ins. Blender executable discovery
and Python version were in a successful read batch (exit 0); relevant commands/output:

```powershell
Get-ChildItem 'C:\Users\blues\AppData\Local\Programs\Blender' -Recurse -Filter blender.exe
python --version
```

```text
LastWriteTime : 9/15/2026 1:38:34 AM
Length        : 113022936
Name          : blender.exe

Python 3.14.2
```

The installed Blender version was not queried in this brief. Brief 09's version is
historical evidence, not a current run here.

An optional environment query at the end of a read batch also returned exit 1:

```powershell
Get-ChildItem Env:BLENDER
```

```text
Get-ChildItem : Cannot find path 'BLENDER' because it does not exist.
```

Full suite command (exit **1**; exactly as run, including the setup typo):

```powershell
$env:PYTHONDONTWRITEBYTECODE='1'
$env:RECIPE_TEST_ARTIFACTS = python -B -c "import tempfile; print(tempfile.mkdtemp(prefix='enfractal-10-evidence-')) )"
python -B -m unittest pipeline.recipes.tests -v
```

Relevant raw lines (the tool truncated the repetitive 791-line output):

```text
  File "<string>", line 1
    import tempfile; print(tempfile.mkdtemp(prefix='enfractal-10-evidence-')) )
                                                                              ^
SyntaxError: unmatched ')'
test_01_default_small_large (pipeline.recipes.tests.test_recipes.BuildTests.test_01_default_small_large) ...
  test_01_default_small_large (pipeline.recipes.tests.test_recipes.BuildTests.test_01_default_small_large) (recipe='cardboard_box', size='default') ... ERROR
BUILD_TEST_SECONDS 0.033
tearDownClass (pipeline.recipes.tests.test_recipes.BuildTests) ... ERROR
test_bad_inputs (pipeline.recipes.tests.test_recipes.InputTests.test_bad_inputs) ... ok
test_cli_refuses_before_launch (pipeline.recipes.tests.test_recipes.InputTests.test_cli_refuses_before_launch) ... ERROR
test_defaults_and_sources (pipeline.recipes.tests.test_recipes.InputTests.test_defaults_and_sources) ... ok

  File "C:\dev\EnFractal-codex\10-recipe-library\pipeline\recipes\tests\test_recipes.py", line 88, in build_case
    out.mkdir(parents=True, exist_ok=True)
PermissionError: [WinError 5] Access is denied: 'C:\\Users\\blues\\AppData\\Local\\Temp\\recipe-tests-krx9bwhb\\french_press'

  File "C:\dev\EnFractal-codex\10-recipe-library\pipeline\recipes\tests\test_recipes.py", line 55, in test_cli_refuses_before_launch
    path.write_text('{"recipe":"couch","size_m":[1,1,1],"shell":"oops"}\n',
PermissionError: [Errno 13] Permission denied: 'C:\\Users\\blues\\AppData\\Local\\Temp\\recipe-invalid-p0nhofrs\\bad.json'

  File "C:\Python314\Lib\tempfile.py", line 276, in _dont_follow_symlinks
    func(path, *args, follow_symlinks=False)
    ~~~~^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
PermissionError: [WinError 5] Access is denied: 'C:\\Users\\blues\\AppData\\Local\\Temp\\recipe-invalid-p0nhofrs'

----------------------------------------------------------------------
Ran 6 tests in 0.056s

FAILED (errors=27)
```

Stopped here under `docs/codex/README.md`: "Stop and report it: a failing setup step,
a permission prompt, a sandbox limit, a test that will not pass" and "no retrying the
same blocked action". The suite itself continued its subtests automatically; no second
test or build invocation followed. No default metrics or deterministic hashes exist.

Read-only delivery inventory (exit **0**). The committed-branch scope checker is
left to the integrator because these changes are uncommitted.

```powershell
git status --short --untracked-files=all
@'
from pathlib import Path
paths=sorted(Path('pipeline/recipes').rglob('*'))+[Path('docs/codex/reports/10-recipe-library.md')]
files=[p for p in paths if p.is_file()]
print('DELIVERY_FILES',len(files))
print('TEXT_CR_FILES',[p.as_posix() for p in files if p.suffix in ('.py','.md','.json') and b'\r' in p.read_bytes()])
print('DELIVERED_GLBS',[p.as_posix() for p in files if p.suffix=='.glb'])
print('DELIVERED_PREVIEWS',[p.as_posix() for p in files if p.suffix=='.png'])
'@ | python -B
```

```text
?? docs/codex/reports/10-recipe-library.md
?? pipeline/recipes/README.md
?? pipeline/recipes/__init__.py
?? pipeline/recipes/build.py
?? pipeline/recipes/cardboard_box.py
?? pipeline/recipes/contract_check.py
?? pipeline/recipes/couch.py
?? pipeline/recipes/french_press.py
?? pipeline/recipes/gaming_laptop.py
?? pipeline/recipes/geometry.py
?? pipeline/recipes/jam_jar.py
?? pipeline/recipes/specs.py
?? pipeline/recipes/tests/__init__.py
?? pipeline/recipes/tests/test_recipes.py
?? pipeline/recipes/worker.py
DELIVERY_FILES 15
TEXT_CR_FILES []
DELIVERED_GLBS []
DELIVERED_PREVIEWS []
```


## Previous delivery audit (historical)

Command; exit 0:

```powershell
git branch --show-current
git status --short --untracked-files=all
@'
from pathlib import Path
import subprocess
files = sorted(p for p in Path('pipeline/recipes').rglob('*') if p.is_file())
files.append(Path('docs/codex/reports/10-recipe-library.md'))
text_files = [p for p in files if p.suffix in ('.py', '.md', '.json')]
for path in text_files:
    path.read_bytes().decode('utf-8')
print('TEXT_CR_FILES', [p.as_posix() for p in text_files if b'\r' in p.read_bytes()])
print('CHECKOUT_GLBS', [p.as_posix() for p in files if p.suffix == '.glb'])
print('PREVIEWS', [(p.as_posix(), p.stat().st_size) for p in files if p.suffix == '.png'])
print('CHECKOUT_WORK_FOLDERS', list(Path('pipeline/recipes').rglob('.blender-work')))
changed = subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=all'], text=True).splitlines()
outside = [line[3:] for line in changed if not (line[3:].startswith('pipeline/recipes/') or line[3:] == 'docs/codex/reports/10-recipe-library.md')]
print('OUT_OF_SCOPE_CHANGED_PATHS', outside)
assert not outside
'@ | python -B
exit $LASTEXITCODE
```

Raw output:

```text
codex/10-recipe-library
?? docs/codex/reports/10-recipe-library.md
?? pipeline/recipes/README.md
?? pipeline/recipes/__init__.py
?? pipeline/recipes/build.py
?? pipeline/recipes/cardboard_box.py
?? pipeline/recipes/contract_check.py
?? pipeline/recipes/couch.py
?? pipeline/recipes/french_press.py
?? pipeline/recipes/gaming_laptop.py
?? pipeline/recipes/geometry.py
?? pipeline/recipes/jam_jar.py
?? pipeline/recipes/previews/cardboard_box.png
?? pipeline/recipes/previews/couch.png
?? pipeline/recipes/previews/french_press.png
?? pipeline/recipes/previews/gaming_laptop.png
?? pipeline/recipes/previews/jam_jar.png
?? pipeline/recipes/specs.py
?? pipeline/recipes/tests/__init__.py
?? pipeline/recipes/tests/test_recipes.py
?? pipeline/recipes/worker.py
TEXT_CR_FILES []
CHECKOUT_GLBS []
PREVIEWS [('pipeline/recipes/previews/cardboard_box.png', 320992), ('pipeline/recipes/previews/couch.png', 293095), ('pipeline/recipes/previews/french_press.png', 317547), ('pipeline/recipes/previews/gaming_laptop.png', 304727), ('pipeline/recipes/previews/jam_jar.png', 350142)]
CHECKOUT_WORK_FOLDERS []
OUT_OF_SCOPE_CHANGED_PATHS []
```

## Current delivery audit

Command, **exit 0**:

```powershell
python -B 'C:\Users\blues\AppData\Local\Temp\enfractal-10-fixes-2880972d6d674bf89923212c56b57cb8\audit.py'
exit $LASTEXITCODE
```

The temp script checks the branch, UTF-8/LF text, absence of checkout GLBs/work folders,
preview sizes and all changed paths against the brief scope. Raw output:

```text
BRANCH codex/10-recipe-library
TEXT_CR_FILES []
CHECKOUT_GLBS []
CHECKOUT_WORK_FOLDERS []
PREVIEWS [('pipeline/recipes/previews/cardboard_box.png', 320992), ('pipeline/recipes/previews/couch.png', 293095), ('pipeline/recipes/previews/french_press.png', 317547), ('pipeline/recipes/previews/gaming_laptop.png', 304768), ('pipeline/recipes/previews/jam_jar.png', 350142)]
OUT_OF_SCOPE_CHANGED_PATHS []
AUDIT PASS
```
