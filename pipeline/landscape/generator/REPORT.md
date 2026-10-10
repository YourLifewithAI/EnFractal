# The garage as land: the landscape generator (C5)

## The clean coast (Run 2, Bubbles and Fireworks round, 10 October; generator version 5)

The founder, after swimming out from the garage island: "We have the land meet the sea on mostly beaches with smooth gradual transitions into deep ocean and sheer cliffs plummeting deep into the ocean but not with weird walls off shore."

- **Nothing stands offshore** (`sea.py`):
  - the reef's rock band and the rocks breaking the surface are gone;
  - so are the sea stacks;
  - so are the distant islands. The far sea floor mesh keeps its name `distant_islands` and its floor (the game's `RoomSea.OpenSeaBedAt`), with nothing rising from it.
- **The reef line is the shelf's edge under water.** Off the soft shores a shelf at the lagoon's depth (0.22 m) runs out to it, then falls to the open sea (0.62 m) over 0.7 m (was 0.4 m), so the slope is gentler.
- **Cliffs plummet into deep water.** At a cliff's foot there is no shelf: the 72 degree face runs straight down to the open sea's floor.
- **Mostly beaches between the headlands.** The low 30 degree rocky shore now takes 2 to 18% of a room's coast (was 4 to 39%). Headlands still go where furniture stands near a wall, so a crowded room like the garage keeps a mostly cliffed coast (see "For the founder").
- **The seabed** is scree, with rock at a cliff's foot; no ring marks the reef line.
- **Kept:**
  - the jetty, its harbour and the pass in the data;
  - B home;
  - the beaches' walk-out and wade-in (8 degree wading shelf, then 22 degrees);
  - the swim and dive depths;
  - the climbs;
  - the promised beaches.

### The package's `x_generator.sea`

The fields are unchanged, so the exporter's `x_landscape_sea`, `RoomSea` and `OpenSea` need no change. Three values change:
- `reef.crest_y_m` is the shelf's nominal floor, `level - 0.22` (was `level - 0.022`, the rock's crest). The game only prints it.
- `stacks` is `[]`.
- `distant_islands` is `[]`.

### Checks

`sea` gains `offshore`. The sea is the water joined to the grid's edge. Everything else, plus the sea within 0.1 m of the surface (a body's height), is the island and its shallows. Shallows not joined to the island the player stands on are walls offshore: reef rocks, a stack, a rock breaking the surface. The check fails on any.

It also reports `cliff_foot_depth_m`: the depth 0.3 m out from each coast face over 60 degrees (min, median).

### The garage, before and after

| | Before (`90d1f1d`) | After |
|---|---|---|
| Walls offshore | 7: four stacks up to 0.32 m above the sea, three reef stretches breaking it by 3 cm | none |
| Cliffs: depth 0.1 / 0.2 / 0.3 / 0.8 m out (median) | 0.21 / 0.22 / 0.20 / 0.59 m (a lagoon at the foot) | 0.32 / 0.60 / 0.62 / 0.61 m |
| beach_00, walking out | 8 cm deep at 0.63 m, then a reef ridge 9 cm high (a 77 degree step) | 8 cm deep at 0.63 m, 22 cm at 0.98 m, 60 cm at 1.92 m; never steeper than 22 degrees, never rising |
| Distant islands | six, up to 2.6 m high | none; the far floor lies at -0.68 m |
| Coast (garage) | 79% cliff, 10% beach-like, 11% between | unchanged |

### Corpus: 23 of 24 pass all checks (was 21 of 24 at `90d1f1d`)

- **Fixed:**
  - awkward_l_scan_17 (walk, a cottage door);
  - living_room_scan_73 (grounding and the beach);
  - workshop_nominal now also meets the climb principle.
- **Still failing: home_office_scan_73.** Its jetty landing is unreachable, as before. A woodpile and the promised beach on the west coast are now unreachable too. A new beach there blends into a short stretch of rocky shore and leaves a bluff 2 to 4 cm high (35 to 38 degrees) between the beach and the plain. That bluff cuts off a strip the walk used to cross. Starting the rocky shore's steep rise half a metre inland, like the beach's, did not fix it; left for the next round.
- **No room has a wall offshore.**
- **Cliff feet:** in 22 rooms the water 0.3 m off the coast's steep faces is 0.59 to 0.62 m deep (median). In awkward_l nominal and scan 73 it is 0.23 to 0.24 m. The measure counts every face over 60 degrees at the waterline, so those two are not yet explained.

Command, per room, as before; the run uses `generate` then `checks` on each of the 24 rooms.

### For the founder

"Mostly beaches": crowded rooms still have mostly cliffed coasts, because furniture near a wall makes a headland. The garage measures 74% headland from its furniture. Shorter headlands would give more beach but cut into the landforms' footprints. That is the founder's call to make by playing.

## Climbing and the last rooms (Run 2, the open sea round, 9 October)

- **What you climb, you can stand on top of** (`climb.py`, Lane P's measurements on the garage). A climber goes up the fall line of a face over 55 degrees from standable ground and over its top; a knife edge or needle is a crest with no 5 cm lip at 35 degrees or less and no standable patch within a body length.
  - The generator weathers rock too thin to stand on: a 9 cm grey-scale opening of the land, then 15 cm round any knife edge left. It never raises land. Water, banks, paths, the hamlet, the jetty and the sea stay; an object's own landform weathers at most to the height its footprint still reads by.
  - Spires over 20 cm keep a 5 cm caprock summit (hoodoos, not points); summits are weathered smooth of the flank noise.
  - Lane P's two examples are gone: the table corner the coast had cut into a sliver, and the knife ridge beside it.
  - `checks` prints `CLIMB` and `climb=`; it is reported beside `overall`, not in it, because tall crests of object landforms (crags and the chair's tor, 0.4 to 1.9 m up) still fail it in most rooms.
- **Beaches are promised only if the written package reaches them** (single precision), not only the generator's memory; with none, the harbour's sand beside the jetty is tried.
- **Corpus: 21 of 24** (the full run gave 18; three rooms lost a footprint to planing, fixed by an absolute weathering floor and rerun with three others, all passing). Fixed: home_office_nominal, living_room_scan_17. Still failing: awkward_l_scan_17 (walk, a cottage door), home_office_scan_73 (the jetty's landing is a pocket below a bluff), living_room_scan_73 (no beach a body reaches; a cottage hangs 1.4 cm).
- **Not done: the ragged coast.** The coast still reads as a rounded rectangle.

## The island (Run 2, the island round, 9 October; generator version 4)

Every room is an island in an endless sea (`sea.py`). Synthetic top-down pictures were made outside the repository; no Blender renders yet.

- **The coast** follows the walkable floor's outline, so an L-shaped room is an L-shaped island. The walls' ridges are gone. Read along the wall line:
  - **cliffs and headlands** where a landform stands within 0.5 m of the wall, plus a few rocky points. The coast reaches 0.06 to 0.28 m past the wall there. The landform keeps its height to the waterline, then drops at 72 degrees or steeper (climbable);
  - **coves with beaches** elsewhere, the water 0.07 to 0.33 m inside the wall: sand at 9 then 16 degrees, an 8 degree wading shelf, then 22 degrees down. Half a metre inland the beach stops cutting, so a landform meets it as a bluff and keeps its height;
  - **a low rocky shore** (30 degrees) between them;
  - up to four **sea stacks** off the headlands, standing alone in the lagoon.
- **The door becomes a jetty** in a harbour cove: a level plank deck 0.16 m wide, 0.05 m above the sea, on timber posts, square to the door's wall, from a landing on the shore out over the lagoon. A worn path leads to it, and a pass through the reef lies before it.
- **The sea:** the level is `SEA_Y = -0.02` m, 2 cm under the room's floor. The lagoon is 0.22 m deep. The reef lies 0.3 to 0.6 m off the coast (0.55 to 0.6 m off a beach, so its shelf reaches swimming water first): rock 2 cm under the surface, with rocks breaking it. Past the reef the sea falls to 0.62 m. The sea's still surface covers the grid wherever the sea floor lies under it, then runs on as a strip to 60 m. **Distant islands** (six, 5 to 19 m out) rise where the far hills were.
- **Rivers run to the sea:** the river (or a dry room's rill) leaves the tarn or its spring for the sea, never below sea level. It ends in an estuary on a low shore, or falls over a cliff. The distant lake is gone.
- **Beaches promised to a swimmer:** at the middle of each beach stretch, a wash-ashore place on dry sand. Under water, no step where the feet touch is steeper than 35 degrees (the pond's rule); on dry sand none is steeper than 20. Each place is reachable on foot from the spawn; at most five are promised, at least 1.2 m apart.

### The package's `x_generator.sea` (for brief 21 and the contract change)

Units are metres and degrees, in the room frame (y up, -Z forward). Outlines are closed loops of `[x, z]` in the room's floor-polygon winding, with the first point not repeated.

| Field | Meaning |
|---|---|
| `level_m` | sea surface y (-0.02) |
| `mesh` | `"sea"`: the still water record (`water` kind `still`), the island cut out |
| `swim_depth_m`, `lagoon_depth_m`, `open_sea_depth_m` | 0.08 (the body floats), 0.22, 0.62 |
| `coast.outline_m` | the island's waterline (the loop round the player's spawn) |
| `reef.outline_m` | the reef's crest line |
| `reef.crest_y_m`, `reef.band_half_width_m` | crest y (level - 0.022), half width (0.085) |
| `reef.offshore_m` | `[min, max]` distance of the crest from the coast |
| `reef.passes[]` | `centre_m [x, z]` and `width_m` of the pass before the jetty |
| `play_area.outline_m` | the playable sea's edge: 0.3 m past the reef (`past_reef_m`); the current belongs between the reef and this line |
| `play_area.bounds_m` | `min_m`/`max_m` `[x, y, z]`: the room bounds to grow to (deepest sea floor to the room's top) |
| `beaches[]` | `id`, `wash_ashore_m [x, y, z]` (dry sand, standable, reachable on foot), `yaw_deg` (Godot yaw facing inland), `water_m [x, level, z]` (swimming water straight off it), `water_depth_m` |
| `jetty` | `mesh` `"jetty"` (a `terrain` record), `door_id`, `root_m`/`end_m [x, deck_top, z]`, `yaw_deg` (root to end), `width_m`, `length_m`, `deck_top_m` |
| `stacks[]` | `centre_m [x, z]`, `radius_m`, `top_m` |
| `river_mouths[]` | `[x, z]` where a stream meets the sea |
| `distant_islands[]` | `x`, `z`, `radius_m`, `top_m` (scenery mesh `distant_islands`) |

**Package records:**
- `terrain`: `land` (the whole grid: island, beaches, cliffs, lagoon floor, reef and stacks, so the swimmer has a bed) and `jetty`;
- `water`: `sea` and the inland water;
- `scenery`: `distant_islands` (the sea floor beyond the grid, and the islands).

The walls' `ridges`, `far_hills` and `distant_lake` are gone.

### Checks

The checks gain `sea`:
- the sea is level at `level_m`;
- every point of the playable edge lies in water at least 0.1 m deep;
- the player's spawn is on the island;
- the reef lies 0.2 to 1.0 m off the coast (measured to the nearest coast point, which stretches at convex corners);
- every promised beach has swimming water off it and a walk out of the sea (35 degrees under water, 20 on dry sand), and is reachable on foot from the spawn; there is at least one beach;
- the jetty's landing end is reachable on foot.

`walk` keeps the crate, the doors and the yards. The checks look only at the island and its water (the playable bounds plus 0.6 m), not out to the horizon.

### Corpus (9 October): 14 of 24 pass all checks (C6 had 23 of 24 before the island)

Command, per room:
```
python3 -B -m pipeline.landscape.generator.generate --room <room> --out <pkg>
python3 -B -m pipeline.landscape.generator.checks --package <pkg> --room <room>
```

**Pass:**
- awkward_l nominal and scan 73;
- bedroom nominal and scan 17;
- garage, all three;
- home_office, all three;
- living_room_nominal;
- near_empty_nominal;
- workshop nominal and scan 73.

The garage: `CHECKS water=True grounded=True walk=True footprints=True sea=True overall=True`. It has its tarn (the pond test still passes), climbable headland cliffs, one promised beach and the jetty.

**Fail, by cause:**
- **The jetty's landing is not reachable on foot:** kitchen, all three; bedroom_scan_73, whose spawn is not standable, as in C6.
- **No beach could be promised:** living_room scan 17 and scan 73; workshop_scan_17; bedroom_scan_73.
- **A promised beach the generator's walk reached is unreachable in the written package:** near_empty scan 17 and scan 73; kitchen_scan_73. A difference between the generator's in-memory probe and the package remains to be found.
- **A footprint is lost:**
  - kitchen_scan_73's cabinet_2;
  - living_room_scan_17's lamp_1.
- **Other:**
  - living_room_scan_73: a cottage is not grounded;
  - awkward_l_scan_17: a cottage door is unreachable.

**Not yet done:**
- **Generation time:** about 110 s for the garage (was 50 s).
- **The look:** distant islands carry no trees or settlements yet. Foam and breakers are Lane L's.


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
