# Summer valley

[Nominal views](renders/sheet.png) · [Noisy-room draft](renders/garage_scan_17_sheet.png)

Seed **170621**. Every posed volume contributes uplift. Confident soft kinds round
hills; proportions shape uncertain objects; supported objects add crest details.
Neighbours merge into foothills, with observed hues drawn into one palette.
Connected ground, weathered wall rims and distant hills replace the enclosure
beneath the shared solar sky.

An invented northern spring feeds a carved stream and southern outlet; no pond.
Groves favour moist toes, ferns line banks, and steep crests stay mostly bare.
Three cottages, laundry, firewood and a **15 g supply crate** occupy a sheltered,
graded clearing. Open doorways and worn paths suggest habitation without people.

Checks sample decoded triangles: 2.5 cm terrain, 4 cm A*, 11 cm carrying
clearance, centimetre edge samples, 20° slope cap. Command, exit **0**:

```powershell
python -B -m pipeline.landscape.gen_a.checks --package "$env:TEMP/enfractal-land17-a-delivery-package" --room pipeline/landscape/corpus/rooms/garage_nominal
```

```text
WATER PASS sections=158 uphill=0 depth_m=0.01969..0.03790; still_water=none
SUPPORT PASS cottages=3 details=2 pickup=1 max_error_m=0.00000000
OUTWARD PASS length_m=5.104 max_triangle_deg=4.957 max_1cm_rise_m=0.00040 clearance_diameter_m=0.11 steps=0
CARRY_RETURN PASS length_m=5.104 max_triangle_deg=1.395 max_1cm_rise_m=0.00023 clearance_diameter_m=0.11 steps=0
DESTINATIONS PASS all 3 cottage and 2 workyard approaches reachable
```

Grounding, metres: unchanged centres; rise is the maximum of 17×17 original-footprint
samples; Δ compares box top; shift measures centre-to-peak. The desk merges adjacent uplift.

| Object | Centre x,z | Rise y | Δ top | Peak shift |
|---|---:|---:|---:|---:|
| bean_bag_1 | -0.900,-1.350 | 0.787 | +0.137 | 0.000 |
| bean_bag_2 | 0.250,-1.200 | 0.718 | +0.118 | 0.219 |
| bicycle_1 | 2.650,-1.900 | 1.327 | +0.277 | 0.114 |
| bin_1 | 2.550,1.150 | 0.780 | +0.280 | 0.196 |
| cardboard_box_1 | -2.500,0.250 | 0.454 | +0.154 | 0.180 |
| couch_1 | -1.500,-2.950 | 1.152 | +0.322 | 0.329 |
| desk_1 | 1.350,3.200 | 1.346 | +0.596 | 0.181 |
| easel_1 | -2.550,2.600 | 1.687 | +0.037 | 0.000 |
| french_press_1 | -1.400,1.350 | 0.993 | +0.027 | 0.053 |
| jar_1 | 2.530,2.950 | 2.106 | -0.034 | 0.017 |
| laptop_1 | 1.250,3.200 | 1.342 | +0.552 | 0.213 |
| monitor_1 | 1.580,3.350 | 1.347 | +0.177 | 0.133 |
| office_chair_1 | 0.350,3.120 | 1.213 | +0.163 | 0.106 |
| shelving_unit_1 | 2.530,2.950 | 2.105 | +0.085 | 0.000 |
| storage_tote_1 | 2.450,0.150 | 0.678 | +0.298 | 0.246 |
| table_1 | -1.650,1.350 | 0.993 | +0.273 | 0.206 |

The noisy draft preserves the valley's character; its journey is **5.170 m**.
[Evidence](EVIDENCE.md) records corrected spawn, workyard and water-winding bugs.
Final suite, exit **0**:

```powershell
python -B -S -m unittest pipeline.landscape.gen_a.test_generate -v
```

```text
Ran 9 tests in 462.305s
OK
DETERMINISM PASS identical file inventory and bytes across 2 builds
SOURCE ABLATION PASS 16/16 contributors in each room exceed 1 mm local uplift
```

The valley reads as land to me, but tall crags remain conical and path cuts feel
engineered. Drainage and settlement are garage-specific. Next: varied cliff
profiles and general watershed/placement search.

Godot movement, collision, boundaries and interiors remain **unverified** pending
an imported-scene carry playtest; native runners were not run. No out-of-scope
changes proposed. **Two final-quality runs; second delivered. $0; GPU time 0.**
No commit made. The founder's visual judgement remains open.
