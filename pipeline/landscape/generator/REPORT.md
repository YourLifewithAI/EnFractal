# The garage as land: the landscape generator (C5)

Renders: [sheet.png](renders/sheet.png) (final), [scan_17_draft_sheet.png](renders/scan_17_draft_sheet.png), [corpus_contact_sheet.png](renders/corpus_contact_sheet.png), [corpus_variants_sheet.png](renders/corpus_variants_sheet.png). Seed `20261008`.

## How the room becomes land
- **Geology.** Every box is uplift on a 3 cm grid, merged smoothly. Confident kinds pick forms; low confidence spreads less. Soft kinds are hills (summit ridge, spurs, gullies, a shared lee tail). A form gives way to a sure neighbour outside its own box, and every landform is at least 18 cm wide. Three rounds of erosion.
- **Shell.** A grassy ridge ring follows the floor outline and is highest far from the outlet. It rises behind objects against the wall and sinks into the shared surround without a seam. Distant hills surround the land. Nothing is shaped to a camera.
- **Water (C6).** Each room's floor chooses its water:
  - **a tarn** where a broad hollow is clear (clear radius 0.8 m or more): shelving shores toward the walker's side and the brook's delta, drop-offs of about 58° under low cut banks, and a deep middle of 14 to 24 cm (the garage's is 23 cm);
  - **a dry upland** with only a rill where the room is small and crowded (clear radius under 0.55 m and 20% or more of the floor covered);
  - **otherwise a river** from a far hillside, with up to three deep pools (17 cm) below steep stretches or on bends.

  Levels are held by the lowest rim, and water never runs uphill.
- **People.**
  - A hamlet where water, flat ground and shelter meet; its demands relax step by step if a room offers no ideal site, but it never covers an object.
  - Cottages keep lanes apart and have level forecourts.
  - Graded 17 cm paths, with a level timber footbridge where one must cross a stream.
  - Woodpiles and a laundry line in reachable yards.
  - Where a body cannot reach the crate or a door, the land is cut and filled to grade.

## Checks
```
python -B -m pipeline.landscape.generator.generate --room <room> --out <pkg>
python -B -m pipeline.landscape.generator.checks --package <pkg> --room <room>
python -B -m unittest pipeline.landscape.generator.tests.test_generator -v   # 8 tests OK
```
garage_nominal:
```
GROUNDED built=9 worst_hang_m=-0.0020 worst_sunk_m=0.0114 failures=0
WALK crate 6.045 m (7.52 deg), back carrying 6.045 m (11.15 deg); doors 5.93/6.61/5.97 m;
     woodpiles 6.42/6.13/6.79 m; laundry 6.45 m
CHECKS water=True grounded=True walk=True footprints=True overall=True
```
The walk is an A* search on a 2.5 cm lattice of the decoded triangles plus footbridge decks. An 11 cm disc must be dry, clear of obstacles and on faces of 20° or less, and every edge is sampled each centimetre (step 0.02 m).

Garage grounding (box top → land top, Δ):

| Object | Box top | Land top | Δ |
|---|---|---|---|
| couch | 0.830 | 0.896 | +0.066 |
| bean bags | 0.650 / 0.600 | 0.786 / 0.735 | +0.136 / +0.135 |
| bicycle | 1.050 | 1.051 | +0.001 |
| bin | 0.500 | 0.574 | +0.074 |
| box | 0.300 | 0.390 | +0.090 |
| desk | 0.750 | 0.991 | +0.241 |
| easel | 1.650 | 1.730 | +0.080 |
| french press | 0.966 | 0.912 | −0.054 |
| jar | 2.140 | 2.142 | +0.002 |
| laptop | 0.790 | 0.970 | +0.180 |
| monitor | 1.170 | 1.158 | −0.012 |
| chair | 1.050 | 1.077 | +0.027 |
| shelf | 2.020 | 2.177 | +0.157 |
| tote | 0.380 | 0.452 | +0.072 |
| table | 0.720 | 0.821 | +0.101 |

## Corpus (water / grounded / walk / footprints, walk to crate in m, invented)

C6 (8 October, late night): 23 of 24 pass. bedroom_scan_73's walk fails because its rill has no footbridge. The table below is C5 part 2's and predates C6's water choice.

In C5, every room invented a tarn and a river.

| Room | Checks | Walk | Invented beyond the tarn and river |
|---|---|---|---|
| awkward_l_nominal | all pass | 5.447 | 2 cottages, footbridge, laundry, 2 woodpiles |
| awkward_l_scan_17 | all pass | 4.364 | 2 cottages, footbridge, laundry, 1 woodpile |
| awkward_l_scan_73 | all pass | 6.345 | 2 cottages, footbridge, laundry, 2 woodpiles |
| bedroom_nominal | **water FAIL** | 1.415 | 1 cottage, laundry, 1 woodpile |
| bedroom_scan_17 | all pass | 1.566 | 2 cottages, laundry, 2 woodpiles |
| bedroom_scan_73 | all pass | 6.66 | 3 cottages, laundry, 2 woodpiles |
| garage_nominal | all pass | 6.045 | 3 cottages, laundry, 3 woodpiles |
| garage_scan_17 | all pass | 3.405 | 3 cottages, laundry, 3 woodpiles |
| garage_scan_73 | all pass | 4.193 | 3 cottages, laundry, 2 woodpiles |
| home_office_nominal | all pass | 6.584 | 2 cottages, laundry, 2 woodpiles |
| home_office_scan_17 | all pass | 4.194 | 3 cottages, laundry, 3 woodpiles |
| home_office_scan_73 | all pass | 6.948 | 2 cottages, laundry, 2 woodpiles |
| kitchen_nominal | all pass | 1.314 | 3 cottages, laundry, 3 woodpiles |
| kitchen_scan_17 | all pass | 1.613 | 3 cottages, laundry, 3 woodpiles |
| kitchen_scan_73 | all pass | 4.165 | 3 cottages, laundry, 2 woodpiles |
| living_room_nominal | all pass | 4.363 | 3 cottages, laundry, 2 woodpiles |
| living_room_scan_17 | all pass | 5.773 | 3 cottages, laundry, 2 woodpiles |
| living_room_scan_73 | all pass | 4.387 | 3 cottages, laundry, 3 woodpiles |
| near_empty_nominal | all pass | 3.066 | 3 cottages, footbridge, laundry, 3 woodpiles |
| near_empty_scan_17 | all pass | 4.3 | 3 cottages, laundry, 3 woodpiles |
| near_empty_scan_73 | all pass | 3.84 | 3 cottages, laundry, 3 woodpiles |
| workshop_nominal | **walk FAIL** | – | 3 cottages, laundry, 3 woodpiles |
| workshop_scan_17 | all pass | 4.999 | 2 cottages, laundry, 1 woodpile |
| workshop_scan_73 | all pass | 3.57 | 3 cottages, laundry, 3 woodpiles |

## Weak
- bedroom_nominal's tarn edge is breached at 3 points.
- workshop_nominal's walk is blocked beside a small protected landform.
- Every room gets a tarn at water "some".
- In the SW overview the high country's flank fills the near side.
- Hills are still rounded up close.
- Some player eyes face a hillside (spawn facing belongs to the exporter).
