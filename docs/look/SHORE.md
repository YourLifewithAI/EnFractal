# The living shore

**Run 2, the first recorded play's fix round, Lane L (16 October).** The founder's mark 2, asked about afterwards: "The coastline is a perfect line where the water meets the land. This should be dynamic, with waves gently washing against the shore." (MECHANICS.md decision 66, its look half; debrief change 6). The generator's coast and meshes are exactly as they were; the look does the moving. The code is `game/scripts/native/Look/ShoreWash.cs` (its numbers, `ShoreLook`, and a mirror of the shaders' wave), the shared function `game/shaders/shore_wash.gdshaderinc`, and the two shaders that use it: `painterly_water.gdshader` (where the sea's edge draws back) and `painterly_land.gdshader` (where the wash climbs the sand and the sand stays damp). The checks are `game/tests/native/Look/LookPresetTest.Shore.cs`.

## What it looks like

- **A wash every few seconds, never surf.** Every 8 s a wash climbs the beach (the first 40% of the period, 3.2 s, eased at both ends), reaches its crest, and draws back slowly (4.8 s) below the sea's old edge before the next one comes. On the island garage's beaches (a slope of about 0.16 above the sea, 0.4 below it) the crest is up to 4.5 cm over the sea's level, about 30 cm of beach, a few body lengths, and the draw-back lays 4 cm of shallows bare, about 10 cm. The edge moves at most 3.8 cm/s up and down, 24 cm/s along a beach: slower than the body walks (32 cm/s).
- **The edge is never a ruler's line, and the shore never breathes in unison.** Each 1.3 m stretch of shore keeps its own time (up to 0.8 of a period from its neighbour's) and each wave its own height (60 to 100% of the run-up), so the wash is a row of tongues running up and back at different moments. The edge itself is brushed by a small noise (4 mm of height), like the water's other edges.
- **A wet band on the sand.** The ground the wash has reached stays damp: darker (a multiplier of 0.7) and a little shinier, from a few centimetres under the sea's level up to the highest a wash can reach plus a margin of 12 mm, fading out over 12 mm. On the garage's beaches that is a band about 40 cm wide, whatever the moment.
- **The wash over the sand** is the sea's own shallow colour, as clear as the water shader makes the same depth of water (it hides more the more slanting the eye's path through it), with the sand's marks showing through the thin edge.
- **A little foam at the water's edge**: a lace of white, broken like a brushed edge, 8 mm of depth wide (about 5 cm on the beach), on the leading edge of the wash over the sand and on the thin edge of the sea where it has drawn back.

## What stays as it was

The lagoon is light and the open sea deep: the water shader does the shore's work only where the sea is shallower than the draw-back plus 3 cm (7 cm) and within 0.8 m of the coast, so every deeper pixel is painted by exactly the code that painted it before (with the shore off, `shore_wash` is 0 and nothing in the shader changes). The sea mist, the wrap's seam and the view under water are untouched: no Look number but the shore's moved, and the shore is not read from below at all (`FRONT_FACING` only). The reef's foam and the beach's old foam bands are still there, now measured from the moving edge.

## How

- **One wave, two shaders.** `shore_level` (in `shore_wash.gdshaderinc`) gives, at a place and a moment, how high the wash stands over the sea's level (negative: drawn back) and where the damp band ends. The water shader makes the sea that much deeper or shallower and thins the water to nothing where it has drawn back; the land shader, above the sea's level, paints the wash where it stands higher than the ground and the damp band under it. At the old waterline both use the same eye path through the same depth of water, so the sea's edge and the wash on the sand meet without a seam.
- **The coast field.** The land shader and the water shader read one small texture, built at load from the island's coast outline (`ShoreWash.BuildField`, about 30 ms for the 10x island, R8, 0.1 m a texel): each place's distance to the coast. The shore only works within 0.8 m of the coast (`ShoreLook.ReachM`), and on ground that slopes, so low land inland, a flat, a pond's bank and a reef out at sea are left exactly as they were. The look suite checks it on the real island's terrain, vertex by vertex.
- **The clock is the engine's `TIME`.** Nothing is set on any material each frame. (The first version moved a clock on every land material, five hundred of them, one for each terrain tile's own bake colour: 48 KB and 515 wrappers left for the finalizer a frame, found by `IslandFrameProbe --parts` and removed before any capture. A capture holds the wash at a second with `OpenSea.HoldShoreAt`, so a before and an after show the same moment.)
- **The intro waits.** While the room-to-island intro plays the land is not where it will be and the sea is still rising, so both shaders leave the shore off until it ends (`intro_rise.w`).
- **Who is on the shore.** `OpenSea.Build` turns it on for the sea's water and for every land material of the dressed meshes, and the look hands every land mesh it dresses later to `OpenSea.AdoptLand`. When the sea leaves the scene the shared materials are left with the shore off, so a room without a sea is painted as before.

## The numbers (`ShoreLook.Default`)

| | |
|---|---|
| Period | 8 s: the climb 40% (3.2 s), the draw-back 60% (4.8 s) |
| Run-up, draw-back | up to 4.5 cm over the sea's level, 4 cm under it |
| Lane, wave sizes | 1.3 m; up to 0.8 of a period apart; 60 to 100% of the run-up |
| Edge | soft 3 mm of depth; foam 8 mm of depth; brush 9 cm, 4 mm |
| Damp band | 12 mm above the highest wash, fading over 12 mm; darker by 0.7; shinier by 0.35 |
| Reach, flat | within 0.8 m of the coast (soft from 0.56 m); ground flatter than a slope of 0.03 is left alone, beaches (0.06 and over) are whole |

## On the island garage

The look suite's real-island check (the 1x fixture): of the 14,130 ground vertices within the beach's heights (4 cm under the sea's level to 6 cm over it), 9,357 are touched, the farthest 79 cm from the coast; 2,319 lie farther than the reach, low ground inland, and none is touched.

**Captures** (`tools/look/shore_cameras.json`, `capture-look.ps1 -Room <the 10x island> -Cameras tools/look/shore_cameras.json -Only shore_along,shore_along_back,shore_beach -NoLightChecks -Jpg 90`; `-Baseline e646082` for the before): the HUD's shoulder view (F2) from mark 2's spot on the jetty's gravel looking west along the shore at 16 October 08:30 (the founder's own morning: at noon the sun stands behind the island's cliffs and the whole beach is in their shade), and from the jetty looking down the beach to the east at noon. A camera's `shore_clock_s` holds the wash at a second of its clock, so a before and an after show the same moment: `shore_along` at 1.2 s (the wash coming up), `shore_along_back` at 5.9 s (a tongue up the sand and the sea drawn back beside it), `shore_beach` at 3.1 s. The harness now skips the room-to-island intro (it was drawing the room's walls over every island capture) and builds the ground under a staged player before placing him.

| frame time (ms, 1920 x 1080, vsync off) | before p50 | p95 | GPU mean | after p50 | p95 | GPU mean |
|---|---|---|---|---|---|---|
| `shore_along` | 7.95 | 10.85 | 7.57 | 8.32 | 11.71 | 8.08 |
| `shore_along_back` | | | | 8.56 | 11.82 | 8.13 |
| `shore_beach` | 6.19 | 8.75 | 5.69 | 6.65 | 9.13 | 6.11 |

The shore costs about 0.4 to 0.5 ms of GPU time (6 to 7%) in the views that hold it, p95 under 12 ms against the preset's 16.7; the land shader's extra interpolators and the shore block, which only runs in the beach's band of heights, are most of it. On the CPU it costs nothing a frame: the engine's clock moves the wash, nothing is set on a material, and the sea's frame allocates 0 bytes.

## Not done

- No spray, no travelling swell and no foam trailing out to sea: a wash on the sand and the sea's edge only, the founder's "gently".
- Ponds, brooks and the flowing water are painted as before; the shore is the sea's.
- A shore whose coast the generator draws jagged or whose slope is under 0.06 gets a thinner wash (it is cut by the slope, never widened).
