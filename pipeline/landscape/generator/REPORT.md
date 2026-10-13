# The garage as land: the landscape generator (C5)

## Ground for the 10x world (Run 2, scale packet S4, 13 October)

The experiment's 10x ground was today's 3 cm samples stretched over 10 cm cells: a 10 cm brook was one cell wide,
path and material edges showed as triangles, and thin rock was planed 30 cm wide. Now:

- **A 10 cm base with 2.5 cm detail blocks of 1 m** (`detail.py`) round what the body meets: the paths and graded ways,
  the streams and their banks, the tarn's shore, the beaches' wading shelf, the hamlet, the jetty's landing, the glades
  and the climbs. On the garage: 483 blocks (about a tenth of the land), 0.78 million fine vertices.
- **How the detail is made:** every step that shapes the base (the coast, the landing, the tarn, the streams, the
  glades, the pads, the paths and their grading, the tarn's rim) also records its rule; once the base is done the rules
  are replayed on the blocks, from today's landforms read at each fine point. Decisions (a level, a path's graded line,
  a stream's surface) stay the base's; streams and paths are read along their lines, not sample by sample.
- **Where the body walks** (benches, pads, forecourts, glades, the landing) the blocks keep the base's surface, the one
  the walk was graded on; only their surface is painted at 2.5 cm there. Elsewhere (banks, beds, shores, the shelf, the
  lips) the shape is the detail's.
- **No crack:** a block side that meets base ground has its fine vertices on the straight line between the base
  vertices, and the base triangle beside it is a fan through them (one plane, no T-junction). The package's land is one
  conforming mesh: every inner edge is shared by exactly two triangles (`tests.DetailSeams`; on the 10x garage 2.54
  million triangles, 0 open edges, 88,000 rays across the blocks' sides all find the land).
- **The package says where:** `x_generator.detail` (`cell_m` 0.025, `block_m`, `origin_m`, `blocks`, `reasons`).
- **Walks stay fast:** the checks vouch for whole 2.5 cm cells as they do for the base's (`Terrain.plain_slope`);
  only the seams' fans are read in full. The walk of the yards reads the meshes in single precision from the start, so
  the beaches are proved on the same reading.
- **Magnified inside the generator** from the true-size room (`--world-scale 10`; `scale.magnify`): the package's source
  is the true-size room (its hashes), so the source contract's 20 m opening holds; the harness reads the package against
  the room magnified by the package's `x_generator.world_scale` (the harness's `magnify_room`, merged with S5), and the
  exporter exports that magnified room. The x10 room file is gone.
- **The 10x gaps:**
  - the hamlet's third door: its forecourt lies 6 cm under the next cottage's, and the cottages were levelled again
    after grading, over the ramp graded to it. On the 10x world the hamlet is levelled before the walk, and every way
    graded keeps the forecourts' and the landing's levels;
  - the reef's distance is the body's (a short swim, the plan's BODY list): the coast it is measured from is the
    farthest-reaching within .2 and .35 m, not x W; the check's 0.2 to 1.0 m stays (now 0.24 to 0.84 m);
  - the footprint rule is the room's (the plan's ROOM list: a third of a box's height, 15 cm x W): the check reads its
    metres x W, as the generator's own protection of a landform's top already did (the laptop reads again);
  - thin rock is planed at the body's size, 10 cm at 2.5 cm (then 15 cm round a knife edge left), in every block with a
    face over 55 degrees; the base is no longer planed 30 cm wide. Climb (beside `overall`): 3 crests (S3: 3), an
    object's protected top, a fin by the river, a coastal rib;
  - a stream's bank is cut where the land lies within 10 cm of it; a landform it runs beside keeps its face, rising from
    the water (no 5 m wall cut into a cliff's foot), and a spring cuts nothing behind it;
  - a path's bench is never narrower than the walk resolves (10.5 cm each side, the 11 cm disc plus one 5 cm step).
- **1x is unchanged:** byte-identical (four corpus rooms; the 25 unit tests pass).

The 10x garage passes `overall` (walk 5 of 5, sea, footprints 16 of 16, water, grounded). Two yard things (a woodpile
and the laundry) are still left out at 10x, as in S3. The land is 0.99 million base triangles (5,960 of them fans) and
1.55 million detail triangles (land.glb 75 MB). Generation, S3's and this run back to back on the same loaded machine:
184 s and 227 s (S3 alone earlier: 133 s). The detail's own stages: climbs 4 s, detail 11 s, lips 7 s (the base's
30 cm planing, 13 s, is gone), the surface and the stitched mesh +20 s, the yards' walk over the finer land +10 s,
writing the bigger land +15 s.

```
python -B -m pipeline.landscape.generator.generate --room pipeline/landscape/corpus/rooms/garage_nominal --world-scale 10 --out <pkg>
python -B -m pipeline.landscape.generator.checks --package <pkg> --room pipeline/landscape/corpus/rooms/garage_nominal --world-scale 10
```

## Generation fast enough at world scale (Run 2, scale packet S3, 13 October)

The founder moved the game to a 10x world (13 October): the room magnified ten times round today's body. The scale
experiment's 10x garage took 6.9 minutes to generate, and its walk checks gave up on some goals and took others on
trust. Now:

- **The walk is exact again, island-wide** (`checks.Walker`): one flood from the spawn per planting state answers every
  goal (the crate, the doors, the yards, the beaches, the jetty); no node budget, nothing taken on trust. The lattice
  is 2.5 cm on today's room and 5 cm on a magnified one; the body's limits are unchanged (11 cm disc, 2 cm steps sampled
  each centimetre, 20 degrees). Grading's ways are the same A* searches as before on today's room; on a magnified one a
  goal shut in a pocket is found so by a small flood back from it, and one least-cost flood serves the others.
- **The same answers, faster:** the package's own land grid vouches for plain buckets (whole cells, corners shared
  exactly, nothing on top, no deck, no water), so a node there needs no sample read; answers about the ground are shared
  by every walk over it; a flood goes on after a lane is cleared; a graded Terrain is updated where the land moved.
  Tests pin every shortcut against the full reading (and on the 10x garage the fast floods equal the full ones).
- **The package is written once:** the beach check reads the meshes as the package will hold them (float32) and proves
  each flood's way there, with no scratch package written and read back.
- **1x is unchanged:** all 24 corpus rooms come out byte-identical to the generator before this packet (3 to 5 times
  faster: the garage 136 s to 37 s with three rooms generating at once).

On the 10x garage the generator is no longer the slow part: the harness's `write_package` takes a third of the time
(see the change request in the S3 report). The checks now see what the experiment did not: the hamlet's third door is
unreachable at 10x, and the 1x thresholds for the reef's distance, footprints and climbs do not hold there yet.

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
The walk is a flood from the spawn on a 2.5 cm lattice (5 cm on a magnified room) of the decoded triangles plus footbridge decks. An 11 cm disc must be dry, clear of obstacles and on faces of 20° or less, and every edge is sampled each centimetre (step 0.02 m).

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
