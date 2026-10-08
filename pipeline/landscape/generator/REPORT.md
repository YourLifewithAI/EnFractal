# The garage as land: generator B

Renders: [sheet.png](renders/sheet.png) (final settings), [scan_17_draft_sheet.png](renders/scan_17_draft_sheet.png).

## How the room became land

- **Geology.** Every box is uplift on one 3 cm height grid, merged by a smooth maximum so neighbours share saddles. A confident kind picks the form: the couch is an escarpment with a bench terrace, the bean bags twin rounded hills, the shelf a crag, the desk a terrace, the easel and french press needles, the table, box and tote mesas, the bicycle a serrated fin, the jar the shelf's snow cap. Low confidence falls back to proportions. Warm colours become sandstone (`cliff`), cool ones slate (`rock`), tinted by the source colour. Outlines are noise-warped; flow accumulation cuts gullies, crests soften, talus aprons skirt cliffs.
- **Shell.** Walls become a broken ring of hills whose crests stay under each overview's sightline. The high country rises in the one corner no camera looks across. The garage door becomes the lowest pass, the window a col. Unreachable hills stand beyond, marked as scenery.
- **Water.** A spring at the foot of the snowy crag feeds a brook into a tarn on the open plain. The outlet river leaves through the door's pass to a distant lake. Water levels are fixed before carving: each cross-section is level and nothing rises downstream.
- **Ecology.** Surfaces follow slope, height, water and the 09:00 sun (a shadow march from the east). Moss and ferns grow in the shade, gold grass and heather on dry tops, reeds at the water's edge, broadleaves along the banks and conifers on the ridges and wild edges.
- **People.** A hamlet of three cottages, a woodpile and a lantern sits where flat ground, shelter and water meet. The site must be reachable from the spawn without crossing water. A graded path (at most 12° along it) leads from the spawn to the cottages. The carryable `apple_crate` waits at the first door.

**Invented:** the lake and the river are not in the room, and the hamlet and path have no source object. The spawns and the room centre each get a small level clearing as a resting place. Seed `20261008`.

## Checks (on the written package)

```
python -B -m pipeline.landscape.gen_b.generate --room pipeline/landscape/corpus/rooms/garage_nominal --out <pkg>
python -B -m pipeline.landscape.gen_b.checks --package <pkg> --room pipeline/landscape/corpus/rooms/garage_nominal
python -B -m unittest pipeline.landscape.gen_b.tests.test_gen_b -v     # 6 tests, OK (73 s)
```
```
WATER tarn level 0.00716 spread 0.0, rim open away from a stream 0; brook_0 0.0776->0.0077, river_0 0.0067->-0.015, tilt 0.0, rise 0.0
GROUNDED built=4 worst_hang_m=-0.0020 worst_sunk_m=0.0043 failures=0
WALK to apple_crate: 6.47 m, max face slope 18.34 deg, max grade 11.69, max step 0.0088 m
WALK back carrying: 6.47 m, 17.66 deg, step 0.0081 m; cottage doors 0-2 found (5.98-6.79 m)
CHECKS water=True grounded=True walk=True footprints=True overall=True
```
**Walk method:** an A* search over the terrain's vertices, 8-connected and rebuilt from the package. A vertex counts only when every triangle touching it is at most 20° steep. Each move may climb at most 0.02 m, and water, trunks, rocks and buildings are blocked.

| Object | Form | Box top | Land top | Δ | Rise |
|---|---|---|---|---|---|
| couch_1 | ridge | 0.830 | 0.897 | +0.067 | 0.619 |
| bean_bag_1 / _2 | dome | 0.65 / 0.60 | 0.661 / 0.681 | +0.011 / +0.081 | 0.54 / 0.59 |
| bicycle_1 | fin | 1.050 | 1.051 | +0.001 | 0.970 |
| bin_1 | stack | 0.500 | 0.574 | +0.074 | 0.477 |
| cardboard_box_1 | mesa | 0.300 | 0.390 | +0.090 | 0.257 |
| desk_1 | mesa | 0.750 | 0.996 | +0.246 | 0.718 |
| easel_1 | spire | 1.650 | 1.732 | +0.082 | 1.571 |
| french_press_1 | spire | 0.966 | 0.914 | −0.052 | 0.193 |
| jar_1 | cap (snow) | 2.140 | 2.143 | +0.003 | 0.123 |
| laptop_1 | slab | 0.790 | 0.973 | +0.183 | 0.223 |
| monitor_1 | crag | 1.170 | 1.160 | −0.010 | 0.410 |
| office_chair_1 | knoll | 1.050 | 1.017 | −0.033 | 0.859 |
| shelving_unit_1 | crag | 2.020 | 2.179 | +0.159 | 1.957 |
| storage_tote_1 | mesa | 0.380 | 0.452 | +0.072 | 0.375 |
| table_1 | mesa | 0.720 | 0.821 | +0.101 | 0.718 |

Every box stays where it was (land inside the box footprint, excluding taller boxes on top of it). The rise is measured against the ring 0.45–0.7 m around the box.

**Scan 17:** the layout survives: the same ridge, hills, needle, snowy crag, tarn and pass. The low-confidence mislabels turn into blockier mesas, the walk passes, and one check fails. The chair (labelled "bean bag" at 0.29) becomes a dome that swallows the desk terrace: `desk_1 delta +0.599 LOST`.

## Weak and next

The terrain is flat-shaded, so facets show at 10 cm. The bean-bag hills still read a little as mounds. The ridge's outer slope meets the shared far ground along visible lines. There are no bridges, so a hamlet across water is never chosen. The waterfall rule exists but this room produced none. Next: finer grid near the eyes, a proper hydraulic erosion pass, a bridge or ford rule, and the other 23 corpus rooms.
