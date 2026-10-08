# Command evidence

Commands were run from the repository root. Temporary packages and intermediate
renders were outside the repository. Only the final selected images are delivered.
The output excerpts below are raw relevant lines, with exit codes recorded separately.

## Iteration failures and corrections

Command, exit **1**:

```powershell
python -B -m unittest pipeline.landscape.gen_a.test_generate -v
```

```text
test_scan_principles (pipeline.landscape.gen_a.test_generate.LandscapeTests.test_scan_principles) ... FAIL
AssertionError: blocked route start (0.5, -3.0)
Ran 8 tests in 125.048s
FAILED (failures=1)
```

The noisy manifest moves both spawns. The initial implementation had used the
nominal starting coordinates. Both path starts now come from the manifest.
The checks were retained.

After adding the workyard destinations, command, exit **1**:

```powershell
python -B -m pipeline.landscape.gen_a.checks --package "$env:TEMP/enfractal-land17-a-draft3-package" --room pipeline/landscape/corpus/rooms/garage_nominal
```

```text
AssertionError: blocked route goal (1.55, 1.6199999999999999)
```

The laundry approach needed grading. Short paths now connect both workyard
details to the village lanes. No check was removed or weakened. A geometry
review also found reversed water winding; it was corrected and a fault-injection
test added. All these checks belong to this generator; shared checks were untouched.

## First candidate measurements (superseded)

Command, exit **0**:

```powershell
python -B -m pipeline.landscape.gen_a.generate --room pipeline/landscape/corpus/rooms/garage_nominal --out "$env:TEMP/enfractal-land17-a-candidate-package"
```

```text
GENERATED garage_nominal seed=170621 forms=16 instances=572
```

Command, exit **0**:

```powershell
python -B -m pipeline.landscape.gen_a.checks --package "$env:TEMP/enfractal-land17-a-candidate-package" --room pipeline/landscape/corpus/rooms/garage_nominal
```

```text
PACKAGE PASS triangles=337716 instances=572
WATER PASS sections=158 uphill=0 depth_m=0.01969..0.03790; still_water=none
SUPPORT PASS cottages=3 details=2 pickup=1 max_error_m=0.00000000
OUTWARD PASS length_m=5.104 max_triangle_deg=4.957 max_1cm_rise_m=0.00040 clearance_diameter_m=0.11 steps=0
CARRY_RETURN PASS length_m=5.104 max_triangle_deg=1.395 max_1cm_rise_m=0.00023 clearance_diameter_m=0.11 steps=0
DESTINATIONS PASS all 3 cottage and 2 workyard approaches reachable
GROUNDING id centre_xz box_top land_peak delta_top peak_offset_m
bean_bag_1 -0.900,-1.350 0.650 0.787 +0.137 0.000
bean_bag_2 0.250,-1.200 0.600 0.718 +0.118 0.219
bicycle_1 2.650,-1.900 1.050 1.287 +0.237 0.351
bin_1 2.550,1.150 0.500 0.769 +0.269 0.196
cardboard_box_1 -2.500,0.250 0.300 0.558 +0.258 0.262
couch_1 -1.500,-2.950 0.830 1.143 +0.313 0.329
desk_1 1.350,3.200 0.750 1.370 +0.620 0.181
easel_1 -2.550,2.600 1.650 1.704 +0.054 0.000
french_press_1 -1.400,1.350 0.966 0.993 +0.027 0.053
jar_1 2.530,2.950 2.140 2.099 -0.041 0.017
laptop_1 1.250,3.200 0.790 1.364 +0.574 0.222
monitor_1 1.580,3.350 1.170 1.370 +0.200 0.097
office_chair_1 0.350,3.120 1.050 1.199 +0.149 0.188
shelving_unit_1 2.530,2.950 2.020 2.099 +0.079 0.000
storage_tote_1 2.450,0.150 0.380 0.629 +0.249 0.304
table_1 -1.650,1.350 0.720 0.993 +0.273 0.206
```

## Source contract validation

Command, exit **0**:

```powershell
python -B contracts/validate.py --room pipeline/landscape/corpus/rooms/garage_nominal --room pipeline/landscape/corpus/rooms/garage_scan_17
```

```text
OK: 1 item(s) checked, 0 problem(s)
```

The validator uses the last `--room` argument: that command checks the scan only.
The nominal room was therefore checked separately. Command, exit **0**:

```powershell
python -B contracts/validate.py --room pipeline/landscape/corpus/rooms/garage_nominal
```

```text
OK: 1 item(s) checked, 0 problem(s)
```

Landscape packages use the harness format, not the room-manifest contract; their
format, mesh topology, bounds and hashes are validated by the harness reader.


## Final package measurements

The later visual refinement weathered the shell alignment and lowered the
distant hills. A draft exposed coplanar overlap with the harness far plane;
the generated apron and distant skirt now stay 1 mm above it. The harness
was not changed. The following measurements are of the delivery packages.

Commands, each exit **0**:

```powershell
python -B -m pipeline.landscape.gen_a.generate --room pipeline/landscape/corpus/rooms/garage_nominal --out "$env:TEMP/enfractal-land17-a-delivery-package"
python -B -m pipeline.landscape.gen_a.generate --room pipeline/landscape/corpus/rooms/garage_scan_17 --out "$env:TEMP/enfractal-land17-a-delivery-scan-package"
```

```text
GENERATED garage_nominal seed=170621 forms=16 instances=599
GENERATED garage_scan_17 seed=170621 forms=16 instances=557
```

Command, exit **0**:

```powershell
python -B -m pipeline.landscape.gen_a.checks --package "$env:TEMP/enfractal-land17-a-delivery-package" --room pipeline/landscape/corpus/rooms/garage_nominal
```

```text
PACKAGE PASS triangles=337716 instances=599
WATER PASS sections=158 uphill=0 depth_m=0.01969..0.03790; still_water=none
SUPPORT PASS cottages=3 details=2 pickup=1 max_error_m=0.00000000
OUTWARD PASS length_m=5.104 max_triangle_deg=4.957 max_1cm_rise_m=0.00040 clearance_diameter_m=0.11 steps=0
CARRY_RETURN PASS length_m=5.104 max_triangle_deg=1.395 max_1cm_rise_m=0.00023 clearance_diameter_m=0.11 steps=0
DESTINATIONS PASS all 3 cottage and 2 workyard approaches reachable
GROUNDING id centre_xz box_top land_peak delta_top peak_offset_m
bean_bag_1 -0.900,-1.350 0.650 0.787 +0.137 0.000
bean_bag_2 0.250,-1.200 0.600 0.718 +0.118 0.219
bicycle_1 2.650,-1.900 1.050 1.327 +0.277 0.114
bin_1 2.550,1.150 0.500 0.780 +0.280 0.196
cardboard_box_1 -2.500,0.250 0.300 0.454 +0.154 0.180
couch_1 -1.500,-2.950 0.830 1.152 +0.322 0.329
desk_1 1.350,3.200 0.750 1.346 +0.596 0.181
easel_1 -2.550,2.600 1.650 1.687 +0.037 0.000
french_press_1 -1.400,1.350 0.966 0.993 +0.027 0.053
jar_1 2.530,2.950 2.140 2.106 -0.034 0.017
laptop_1 1.250,3.200 0.790 1.342 +0.552 0.213
monitor_1 1.580,3.350 1.170 1.347 +0.177 0.133
office_chair_1 0.350,3.120 1.050 1.213 +0.163 0.106
shelving_unit_1 2.530,2.950 2.020 2.105 +0.085 0.000
storage_tote_1 2.450,0.150 0.380 0.678 +0.298 0.246
table_1 -1.650,1.350 0.720 0.993 +0.273 0.206
```

Command, exit **0**:

```powershell
python -B -m pipeline.landscape.gen_a.checks --package "$env:TEMP/enfractal-land17-a-delivery-scan-package" --room pipeline/landscape/corpus/rooms/garage_scan_17
```

```text
PACKAGE PASS triangles=337708 instances=557
WATER PASS sections=156 uphill=0 depth_m=0.01968..0.03722; still_water=none
SUPPORT PASS cottages=3 details=2 pickup=1 max_error_m=0.00000000
OUTWARD PASS length_m=5.170 max_triangle_deg=4.957 max_1cm_rise_m=0.00060 clearance_diameter_m=0.11 steps=0
CARRY_RETURN PASS length_m=5.170 max_triangle_deg=2.362 max_1cm_rise_m=0.00027 clearance_diameter_m=0.11 steps=0
DESTINATIONS PASS all 3 cottage and 2 workyard approaches reachable
GROUNDING id centre_xz box_top land_peak delta_top peak_offset_m
bean_bag_2 0.336,-1.227 0.536 0.680 +0.144 0.168
bean_bag_1 -0.886,-1.261 0.620 0.758 +0.138 0.054
office_chair_1 0.401,3.046 1.098 1.253 +0.155 0.110
bin_1 2.639,1.138 0.486 0.821 +0.335 0.182
cardboard_box_1 -2.515,0.161 0.316 0.504 +0.188 0.244
couch_1 -1.458,-2.871 0.782 1.065 +0.283 0.359
desk_1 1.347,3.290 0.752 1.313 +0.561 0.184
easel_1 -2.625,2.550 1.647 1.733 +0.086 0.046
french_press_1 -1.324,1.349 0.912 0.947 +0.036 0.055
laptop_1 1.243,3.276 0.790 1.313 +0.523 0.214
monitor_1 1.584,3.426 1.135 1.317 +0.181 0.113
jar_1 2.481,2.888 2.192 2.135 -0.056 0.022
shelving_unit_1 2.483,2.873 2.056 2.134 +0.078 0.000
bicycle_1 2.690,-1.980 1.038 1.318 +0.280 0.340
storage_tote_1 2.389,0.084 0.394 0.636 +0.242 0.270
table_1 -1.560,1.354 0.688 0.947 +0.259 0.189
```

An explicit before-fix check against the earlier water mesh, exit **1**:

```powershell
python -B -m pipeline.landscape.gen_a.checks --package "$env:TEMP/enfractal-land17-a-draft4-package" --room pipeline/landscape/corpus/rooms/garage_nominal
```

```text
AssertionError: water faces down or folds
```



## Rendering and delivery

Two final-quality runs were made; the second is delivered. Both used the
required setup guard and fresh full sets. Seven draft sets were inspected
during iteration, including two noisy-room drafts. Earlier images stay in
temporary folders, outside the delivery.

First final command, exit **0**:

```powershell
python -B -m pipeline.landscape.harness.render --package "$env:TEMP/enfractal-land17-a-candidate-package" --room pipeline/landscape/corpus/rooms/garage_nominal --label "SUMMER VALLEY" --out "$env:TEMP/enfractal-land17-a-final-renders" --expect-setup pipeline/landscape/harness/ab-setup.json
```

```text
BLENDER_EXIT 0
```

Delivered final command, exit **0**:

```powershell
python -B -m pipeline.landscape.harness.render --package "$env:TEMP/enfractal-land17-a-delivery-package" --room pipeline/landscape/corpus/rooms/garage_nominal --label "SUMMER VALLEY" --out "$env:TEMP/enfractal-land17-a-delivery-renders" --expect-setup pipeline/landscape/harness/ab-setup.json
```

```text
BLENDER_EXIT 0
```

Delivered noisy-room draft command, exit **0**:

```powershell
python -B -m pipeline.landscape.harness.render --package "$env:TEMP/enfractal-land17-a-delivery-scan-package" --room pipeline/landscape/corpus/rooms/garage_scan_17 --label "SUMMER VALLEY - SCAN" --out "$env:TEMP/enfractal-land17-a-delivery-scan-renders" --expect-setup pipeline/landscape/harness/ab-setup.json --draft
```

```text
BLENDER_EXIT 0
```

Final copy and audit command, exit **0**:

```powershell
@'
from pathlib import Path
import hashlib, json, os, struct, subprocess
from pipeline.landscape.harness.common import harness_revision, load_json
root=Path.cwd().resolve()
owned=root/'pipeline/landscape/gen_a'
temp=Path(os.environ['TEMP'])
source=temp/'enfractal-land17-a-delivery-renders'
scan=temp/'enfractal-land17-a-delivery-scan-renders'
receipt=load_json(source/'receipt.json')
scan_receipt=load_json(scan/'receipt.json')
setup=load_json(root/'pipeline/landscape/harness/ab-setup.json')
assert receipt['settings']['device']=='CPU' and not receipt['settings']['denoising_use_gpu']
assert receipt['settings']['samples']==48 and receipt['settings']['width']==1280
assert receipt['setup']==scan_receipt['setup']==setup
assert scan_receipt['settings']['samples']==12 and scan_receipt['settings']['device']=='CPU'
assert receipt['harness_revision']==scan_receipt['harness_revision']==harness_revision()
assert not receipt['partial_rerender'] and receipt['uniform_harness_revision']
pin=hashlib.sha256((temp/'enfractal-land17-a-delivery-package/package.json').read_bytes()).hexdigest()
assert receipt['package_sha256']==pin
flags=['ground_missing','inside_geometry','figure_ground_missing','figure_inside_geometry']
assert not [(v['name'],k) for r in [receipt,scan_receipt] for v in r['views'] for k in flags if v.get(k)]
dest=owned/'renders'
os.makedirs(dest,exist_ok=True)
for entry in receipt['views']+[receipt['diagnostic']]:
    data=(source/entry['path']).read_bytes()
    assert hashlib.sha256(data).hexdigest()==entry['sha256']
    (dest/entry['path']).write_bytes(data)
for name in ['sheet.png','receipt.json']:
    (dest/name).write_bytes((source/name).read_bytes())
(dest/'garage_scan_17_sheet.png').write_bytes((scan/'sheet.png').read_bytes())
pngs=sorted(dest.glob('*.png'))
assert len(pngs)==9
for p in pngs:
    b=p.read_bytes();assert len(b)<2_000_000 and b[:8]==b'\x89PNG\r\n\x1a\n'
    i=8;chunks=[]
    while i<len(b):
        n=struct.unpack_from('>I',b,i)[0];chunks.append(b[i+4:i+8]);i+=n+12
    assert i==len(b) and set(chunks)<={b'IHDR',b'IDAT',b'IEND'}
    print('PNG_PASS',p.name,len(b))
files=[p for p in owned.rglob('*') if p.is_file()]
assert len(files)==17
for p in files:
    assert p.suffix in ['.py','.md','.json','.png'] and p.stat().st_size<2_000_000
    if p.suffix!='.png':
        data=p.read_bytes();data.decode('utf-8')
        assert b'\r' not in data and not data.startswith(b'\xef\xbb\xbf')
status=subprocess.run(['git','status','--porcelain','--untracked-files=all'],capture_output=True,text=True,check=True).stdout
assert all(line[3:].startswith('pipeline/landscape/gen_a/') for line in status.splitlines())
print('DELIVERY_PASS files=17 pngs=9 scope=owned_only text=UTF8_LF packages=none')
print('RIG_PASS CPU final=1280x720/48 scan=480x270/12 setup=matched flags=clear full_fresh_set=true')
print('PACKAGE_PIN',pin)
print('FINAL_RENDER_SECONDS',receipt['total_render_seconds'])
print('FINAL_WALL_SECONDS',receipt['total_wall_seconds'])
print('SCAN_RENDER_SECONDS',scan_receipt['total_render_seconds'])
print('HARNESS_PIN',receipt['harness_revision']['sha256'])
'@ | python -B -
```

```text
PNG_PASS eye_companion.png 747142
PNG_PASS eye_player.png 667848
PNG_PASS eye_window.png 649577
PNG_PASS garage_scan_17_sheet.png 768919
PNG_PASS overview_ne.png 733624
PNG_PASS overview_se.png 743348
PNG_PASS overview_sw.png 765545
PNG_PASS sheet.png 780493
PNG_PASS slope.png 491633
DELIVERY_PASS files=17 pngs=9 scope=owned_only text=UTF8_LF packages=none
RIG_PASS CPU final=1280x720/48 scan=480x270/12 setup=matched flags=clear full_fresh_set=true
PACKAGE_PIN 35a9436dc070d29b1a1041df534fa6b71e87a54b2dec0207c416998181a301e1
FINAL_RENDER_SECONDS 685.216309
FINAL_WALL_SECONDS 765.280538
SCAN_RENDER_SECONDS 59.701451
HARNESS_PIN fa84a0de0d7eac433bd873e14e4cd9fb0f6043c2107109d4e82868c410718f51
```

The final sheet and full-resolution player view were opened after this copy.
All six final views were visually reviewed through the sheet. The report's
visual judgement is subjective; these receipts and checks do not pass the
founder's look gate or certify runtime collision. No commits or pushes were
made. No setup guard, shared check, material, camera, corpus file or harness
source was changed. No paid service, GPU render, game window, installation,
real-place data or oracle file was used.

## Final-source test suite
Command, exit **0**:

```powershell
python -B -S -m unittest pipeline.landscape.gen_a.test_generate -v
```

```text
test_deterministic_package_bytes (pipeline.landscape.gen_a.test_generate.LandscapeTests.test_deterministic_package_bytes) ... ok
test_every_source_has_measurable_uplift (pipeline.landscape.gen_a.test_generate.LandscapeTests.test_every_source_has_measurable_uplift) ... ok
test_guard_blocked_pickup (pipeline.landscape.gen_a.test_generate.LandscapeTests.test_guard_blocked_pickup) ... ok
test_guard_floating_cottage (pipeline.landscape.gen_a.test_generate.LandscapeTests.test_guard_floating_cottage) ... ok
test_guard_ground_hole (pipeline.landscape.gen_a.test_generate.LandscapeTests.test_guard_ground_hole) ... ok
test_guard_reversed_water_surface (pipeline.landscape.gen_a.test_generate.LandscapeTests.test_guard_reversed_water_surface) ... ok
test_guard_uphill_water (pipeline.landscape.gen_a.test_generate.LandscapeTests.test_guard_uphill_water) ... ok
test_nominal_principles (pipeline.landscape.gen_a.test_generate.LandscapeTests.test_nominal_principles) ... ok
test_scan_principles (pipeline.landscape.gen_a.test_generate.LandscapeTests.test_scan_principles) ... ok

----------------------------------------------------------------------
Ran 9 tests in 462.305s

OK
GENERATED garage_nominal seed=170621 forms=16 instances=599
GENERATED garage_scan_17 seed=170621 forms=16 instances=557
GENERATED garage_nominal seed=170621 forms=16 instances=599
DETERMINISM PASS identical file inventory and bytes across 2 builds
SOURCE ABLATION PASS 16/16 contributors in each room exceed 1 mm local uplift
PACKAGE PASS triangles=337716 instances=599
WATER PASS sections=158 uphill=0 depth_m=0.01969..0.03790; still_water=none
SUPPORT PASS cottages=3 details=2 pickup=1 max_error_m=0.00000000
OUTWARD PASS length_m=5.104 max_triangle_deg=4.957 max_1cm_rise_m=0.00040 clearance_diameter_m=0.11 steps=0
CARRY_RETURN PASS length_m=5.104 max_triangle_deg=1.395 max_1cm_rise_m=0.00023 clearance_diameter_m=0.11 steps=0
DESTINATIONS PASS all 3 cottage and 2 workyard approaches reachable
GROUNDING id centre_xz box_top land_peak delta_top peak_offset_m
bean_bag_1 -0.900,-1.350 0.650 0.787 +0.137 0.000
bean_bag_2 0.250,-1.200 0.600 0.718 +0.118 0.219
bicycle_1 2.650,-1.900 1.050 1.327 +0.277 0.114
bin_1 2.550,1.150 0.500 0.780 +0.280 0.196
cardboard_box_1 -2.500,0.250 0.300 0.454 +0.154 0.180
couch_1 -1.500,-2.950 0.830 1.152 +0.322 0.329
desk_1 1.350,3.200 0.750 1.346 +0.596 0.181
easel_1 -2.550,2.600 1.650 1.687 +0.037 0.000
french_press_1 -1.400,1.350 0.966 0.993 +0.027 0.053
jar_1 2.530,2.950 2.140 2.106 -0.034 0.017
laptop_1 1.250,3.200 0.790 1.342 +0.552 0.213
monitor_1 1.580,3.350 1.170 1.347 +0.177 0.133
office_chair_1 0.350,3.120 1.050 1.213 +0.163 0.106
shelving_unit_1 2.530,2.950 2.020 2.105 +0.085 0.000
storage_tote_1 2.450,0.150 0.380 0.678 +0.298 0.246
table_1 -1.650,1.350 0.720 0.993 +0.273 0.206
PACKAGE PASS triangles=337708 instances=557
WATER PASS sections=156 uphill=0 depth_m=0.01968..0.03722; still_water=none
SUPPORT PASS cottages=3 details=2 pickup=1 max_error_m=0.00000000
OUTWARD PASS length_m=5.170 max_triangle_deg=4.957 max_1cm_rise_m=0.00060 clearance_diameter_m=0.11 steps=0
CARRY_RETURN PASS length_m=5.170 max_triangle_deg=2.362 max_1cm_rise_m=0.00027 clearance_diameter_m=0.11 steps=0
DESTINATIONS PASS all 3 cottage and 2 workyard approaches reachable
GROUNDING id centre_xz box_top land_peak delta_top peak_offset_m
bean_bag_2 0.336,-1.227 0.536 0.680 +0.144 0.168
bean_bag_1 -0.886,-1.261 0.620 0.758 +0.138 0.054
office_chair_1 0.401,3.046 1.098 1.253 +0.155 0.110
bin_1 2.639,1.138 0.486 0.821 +0.335 0.182
cardboard_box_1 -2.515,0.161 0.316 0.504 +0.188 0.244
couch_1 -1.458,-2.871 0.782 1.065 +0.283 0.359
desk_1 1.347,3.290 0.752 1.313 +0.561 0.184
easel_1 -2.625,2.550 1.647 1.733 +0.086 0.046
french_press_1 -1.324,1.349 0.912 0.947 +0.036 0.055
laptop_1 1.243,3.276 0.790 1.313 +0.523 0.214
monitor_1 1.584,3.426 1.135 1.317 +0.181 0.113
jar_1 2.481,2.888 2.192 2.135 -0.056 0.022
shelving_unit_1 2.483,2.873 2.056 2.134 +0.078 0.000
bicycle_1 2.690,-1.980 1.038 1.318 +0.280 0.340
storage_tote_1 2.389,0.084 0.394 0.636 +0.242 0.270
table_1 -1.560,1.354 0.688 0.947 +0.259 0.189
```
