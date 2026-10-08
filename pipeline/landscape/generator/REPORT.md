# The garage as land: the landscape generator (C5)

Renders: [sheet.png](renders/sheet.png) (final settings), [scan_17_draft_sheet.png](renders/scan_17_draft_sheet.png). Seed `20261008`.

## How the room becomes land
- **Geology.** Every box is uplift on one 3 cm grid, merged by a smooth maximum. Confident kinds pick forms (couch escarpment, shelf crag, desk terrace, easel needle); low confidence falls back to proportions and spreads less far. **Soft kinds are hills:** a summit ridge, spurs with gullies, a bench, and a long lee tail all hills share (high country toward the pass). **Territory:** a form may not rise over a neighbour about as sure as itself; a support's certainty includes what it carries. Three rounds of stream-power erosion branch the gullies.
- **Shell.** Walls become a vegetated ridge ring along the floor outline (any polygon), highest farthest from the outlet so the land drains to the pass. It rises behind objects against the wall, never over them, and runs into the shared surround as a moss skirt (no seam). Distant hills surround the land. Nothing is shaped to a camera.
- **Water, ecology, people** are as in gen_b; the hamlet search relaxes step by step when a room offers no ideal site. Paths keep an 11 cm lane clear; a yard item no lane reaches is left out.

## What changed and why
The founder's favourites are kept: detail, garage features legible in the land, a believable surround. gen_a's checks are folded in. Of gen_b's six weak points, four are fixed; bridges are not built and the laundry is generated but dropped (see Weak). Harness: smooth shading and the overview cutaway (see its README).

## Checks
```
python -B -m pipeline.landscape.generator.generate --room pipeline/landscape/corpus/rooms/garage_nominal --out <pkg>
python -B -m pipeline.landscape.generator.checks --package <pkg> --room pipeline/landscape/corpus/rooms/garage_nominal
python -B -m unittest pipeline.landscape.generator.tests.test_generator -v   # 7 tests OK (207 s)
```
```
GROUNDED built=6 worst_hang_m=-0.0020 worst_sunk_m=0.0061 failures=0
WALK to apple_crate 6.446 m, 8.76 deg, step 0.0009 m; back carrying 6.446 m, 11.9 deg
WALK cottage doors 0-2: 6.315 / 6.813 / 6.267 m
CHECKS water=True grounded=True walk=True footprints=True overall=True
```
Walk method: A* on a 2.5 cm lattice of the decoded triangles (topmost hit). An 11 cm disc must be clear of water, trunks, rocks and buildings, on faces of 20° or less. Every edge is sampled each centimetre (step 0.02 m).

| Object | Form | Box top | Land top | Δ | Rise |
|---|---|---|---|---|---|
| couch_1 | ridge | 0.830 | 0.896 | +0.066 | 0.596 |
| bean_bag_1 / _2 | hill | 0.65 / 0.60 | 0.786 / 0.735 | +0.136 / +0.135 | 0.68 / 0.64 |
| bicycle_1 | fin | 1.050 | 1.051 | +0.001 | 0.966 |
| bin_1 | stack | 0.500 | 0.574 | +0.074 | 0.455 |
| cardboard_box_1 | mesa | 0.300 | 0.390 | +0.090 | 0.222 |
| desk_1 | mesa | 0.750 | 0.991 | +0.241 | 0.652 |
| easel_1 | spire | 1.650 | 1.730 | +0.080 | 1.193 |
| french_press_1 | spire | 0.966 | 0.912 | −0.054 | 0.191 |
| jar_1 | cap (snow) | 2.140 | 2.142 | +0.002 | 0.122 |
| laptop_1 | slab | 0.790 | 0.970 | +0.180 | 0.220 |
| monitor_1 | crag | 1.170 | 1.158 | −0.012 | 0.408 |
| office_chair_1 | knoll | 1.050 | 1.077 | +0.027 | 0.854 |
| shelving_unit_1 | crag | 2.020 | 2.177 | +0.157 | 1.863 |
| storage_tote_1 | mesa | 0.380 | 0.452 | +0.072 | 0.373 |
| table_1 | mesa | 0.720 | 0.821 | +0.101 | 0.713 |

**Scan 17:** every footprint reads (desk +0.142, was +0.599). Water and grounding pass. **The walk fails:** the doors and crate are unreachable with 11 cm clearance.

## Weak
- The laundry line and woodpiles are generated but dropped as unreachable in the garage; the lantern loses to the path lane.
- No bridge or ford yet.
- Scan 17's walk fails.
- In the SW overview the high country's flank fills the near side.
- Hills still look rounded up close.
