# Look captures, Run 1

The latest round only: `before/` and `after/` of the fix round and art pass of 7 October. Every older set (`before/`, `after/`, `step3/` and `round2/` of rounds 1 and 2, 58 MB of PNGs) was removed from the tree in this round and is in Git's history at `a0f12c9`; see "Where captures go" below for why. Score the frames against [the look bible](../../LOOK-BIBLE.md); only the founder passes the look gate.

All frames: the fixed review cameras (`tools/look/review_cameras.json`) at 1920 x 1080 on the founder's NVIDIA RTX 2070 SUPER (Forward+, Vulkan), at 17:00 on 15 April standard time (the low sun comes through the west window onto both avatars), lamps pinned on, FXAA. The nine cameras are the five Run 1 baseline cameras (the companion camera reframed for the 10 cm body), `diorama_high` and `iso_room` (6 October), and `window_view` and `observe_view` (7 October).

| Folder | What | Files |
|---|---|---|
| [`before/`](before/) | `a0f12c9` (the head this round started from), the same cameras, clock and lamps | the nine cameras as JPEG (quality 90), `sweep_diorama_high.jpg`, `timings.json` |
| [`after/`](after/) | `a5d4e7c` (this round; the night sky was lifted afterwards in `cb102eb`, which the 17:00 frames do not use) | the nine cameras as PNG, `sweep_diorama_high.jpg`, `timings.json` |

`sweep_diorama_high.jpg` is the high-angle diorama view in rows winter, spring, summer and autumn (15 January, April, July and October) and columns 07:00, 12:00, 15:00, 17:00, 18:00 and 21:00 standard time (lamps switch themselves): the **season palettes** to judge. In `before/`, `observe_view` is the diorama pose with no observe profile (the old look has none) and `window_view` shows the dark slate pane.

## Frame time (ms, vsync off, 120 warm-up and 300 measured frames, 1920 x 1080)

Budget: 16.7 ms (`budgets.target_frame_ms`). Both runs waited for a quiet GPU and sampled it throughout; another lane's job held about 1.5 GB of the card's memory in both, and neither ran with a game window open. Before and after are the same cameras at the same clock with the lamps on (three shadowed lights).

| Camera | Before p50 | Before p95 | Before GPU mean | After p50 | After p95 | After p99 | After max | After GPU mean | After GPU p95 |
|---|---|---|---|---|---|---|---|---|---|
| player_eye | 12.55 | 13.92 | 9.43 | 13.35 | 14.55 | 14.98 | 15.49 | 9.45 | 10.32 |
| over_shoulder | 12.11 | 13.21 | 9.11 | 13.63 | 14.82 | 15.30 | 15.69 | 9.72 | 10.94 |
| companion | 11.95 | 12.85 | 8.82 | 13.04 | 13.93 | 14.36 | 14.61 | 9.14 | 10.04 |
| low_corner | 11.58 | 12.47 | 8.46 | 12.73 | 13.54 | 13.82 | 14.03 | 8.76 | 9.36 |
| ceiling_corner | 12.41 | 13.04 | 9.33 | 13.74 | 14.63 | 14.90 | 15.12 | 9.83 | 10.65 |
| diorama_high | 12.44 | 13.26 | 9.25 | 13.98 | 14.91 | 15.69 | 16.18 | 10.07 | 11.06 |
| iso_room | 12.48 | 13.36 | 9.22 | 13.86 | 14.73 | 15.05 | 15.47 | 9.89 | 10.81 |
| window_view | 11.74 | 12.59 | 8.60 | 12.67 | 13.65 | 14.17 | 14.87 | 8.76 | 9.60 |
| observe_view | 12.63 | 13.46 | 9.31 | 15.90 | 17.34 | 17.85 | 18.17 | 11.98 | 13.22 |

The look's self-check found no problems in either run. Two cautions on the table: run-to-run noise on this machine is about 2 ms (a single-camera run of `over_shoulder` read 10.8 ms p50 on the same code, with the card cooler), so read the before and after columns as a pair from the same hour, not as absolute; and `observe_view`, the opt-in inspection view, is the one frame over budget at p95 (17.3 ms): its blur is wide, and `x_look_dof.observe_amount` (0.3) is the number to trade. The focus pass and the sky add well under 1 ms. Light checks (`light_checks` in `timings.json`): floor points in the sun's patch brighten by 0.190 in luma with the sun on (before 0.247), points behind the walls by 0.003 (before 0.020): the sun's bounce, no longer carried through the walls. At 02:00 the frames' mean luma with the lamps off and on was 0.093 and 0.659 (ceiling corner), 0.123 and 0.546 (over the shoulder) at `a5d4e7c`; those lamps-off nights were darker than round 2's 0.155 and 0.188 because the moonlight that used to leak through the walls is gone, so the night sky's energy was lifted a quarter (`cb102eb`), and a one-camera check at that commit read 0.111 and 0.141 with the lamps off (95th percentile 0.27 and 0.21: dark, shapes readable) and 0.662 and 0.552 with them on. The 17:00 review frames are unchanged by it. The grain and vignette check passed (corner darkening 0.116; grain correlation 0.133 against a noise scale of 0.005). The focus pass (depth read, colour lift and highlight lift) is in these numbers.

## What the probes measured

`tools/look/capture-look.ps1 -Probe NAME` runs a GPU probe through the same harness against any commit (`-Baseline`), so before and after are the same code. Probes write to a scratch folder and are not committed.

### The shapes captured rooms take (`rooms`)

Mean frame luma, and how much switching the sun on lights a floor point the shell hides from it (the median luma gain over floor points whose line to the sun a wall or the ceiling blocks; it should be about 0). Moment 17:00 on 15 April (sun 18 degrees up in the west) unless the row says otherwise.

| Room | Before: luma | Before: gain behind the shell | Round's fixes, GI margin 30 cm: luma | gain behind | Final, GI margin 5 cm: luma | gain behind | With the RoomBuilder patch: luma | |
|---|---|---|---|---|---|---|---|---|
| test room, 17:00 on 15 April | 0.470 | +0.039 | 0.480 | +0.039 | 0.379 | +0.004 | 0.379 | the shipped room |
| test room, noon in July (no direct sun can reach the floor) |  |  | 0.474 | +0.099 | 0.360 | +0.023 | 0.362 | every bit of gain is light carried through the shell |
| window, no sun hint | 0.318 | +0.000 | 0.481 | +0.039 | 0.379 | +0.004 | 0.379 | M1: the sun is back |
| captured-like: no sun hint, window only listed in a solid wall | 0.065 | +0.000 | 0.181 | +0.083 | 0.008 | +0.000 | 0.381 | M1: dark until the builder cuts the opening; with the patch it reads as the test room |
| single-sided west wall, no window, sun outside | 0.448 | +0.450 | 0.082 | +0.033 | 0.007 | +0.000 | 0.007 | M4: the sun floods the room before, stays out after |
| three windows | 0.599 | +0.019 | 0.600 | +0.020 | 0.526 | +0.005 | 0.527 | M3 |
| the contract's garage example, noon in July | 0.055 | +0.000 | 0.550 | +0.441 | 0.034 | +0.007 | 0.172 | the review read 0.06 at noon, and this old frame reads 0.055; with the patch the open house door and the sky fill light it |

Reading it: the first fixes alone (M1 to M4) bring the sun back to a room with a window but no sun hint and keep it out of a single-sided wall (0.45 to 0.03 behind it), but they also showed that **VoxelGI carried the sun through every closed shell**: with the volume reaching 30 cm past the room (so the slabs' sunlit outer faces were inside it) a closed room brightened by 0.10 (test room at noon) to 0.44 (garage) with the sun on and no way in. Pulling the volume in to 5 cm (less than the slab's thickness) leaves 0.01 to 0.02, and the sunlit patch itself loses about a tenth (0.338 to 0.331). Without the builder cutting `shell.openings` a captured room is honestly dark (the garage reads 0.03); with `roombuilder-openings.diff` applied in a scratch copy, the captured-like room reads 0.38 against the test room's 0.38, and the garage 0.17.

Three windows (M3): before, all three sky fills stood outside the room and the one with no shadow (`window_south`) shone through its wall; after, the two brightest have shadows outside and the third stands inside the room at its opening.

### The window seen from inside (`window`)

The mean colour of the window pane, from the east side of the room.

| Moment | Before | After |
|---|---|---|
| noon july | #383e47 (luma 0.24) | #cfdce3 (luma 0.85) |
| golden hour april | #353e48 (luma 0.24) | #f7e3ce (luma 0.90) |
| overcast winter noon | #373e48 (luma 0.24) | #d0d4d9 (luma 0.83) |
| night | #373d47 (luma 0.24) | #25315d (luma 0.19) |

Before, the pane was the preset's dark slate at noon, at sunset and at night alike (M2). The after pane is the sky of the hour: bright and pale at noon and under a winter overcast, a pastel dusk at 17:00 in April, deep blue with stars at night.

### Freeing a viewport that used the grain and vignette effect (`free-viewport`, M8)

Viewports created, freed through the queue and resized, the look removed from the tree and freed, and the garbage collector forced to run, with the effect on. Before (`a0f12c9`): the process died with an access violation (exit -1073741819), after a flood of `free_rid can only be called from the render thread` from the finalizer thread; the room probes crashed the same way at exit after the third unloaded room. Cause: the effect's render callback held C# wrappers of the viewport's render buffers; when a viewport was freed the wrapper was the last reference and the buffers' textures died on the garbage collector's thread. After: exit 0, no engine errors. The fix is one `finally` that drops the wrapper on the render thread, and `Release` frees the shader on the render thread; nothing is freed from a destructor.

### Floorboards in motion under FXAA (`shimmer`)

A camera glides sideways over the floor 0.6 mm a frame (a little under a pixel) with the planks in the crisp band, 60 poses; each configuration's frames are compared with the same poses rendered at four times the pixels with 4x MSAA and averaged down. At every pixel the error is the frame minus that reference; **flicker** is how much the error changes from pose to pose (a steady blur is not flicker, a pattern that crawls is), averaged over a 200 x 200 patch. Two renders of the same poses were identical to the last bit, so the measurement has no noise of its own.

| Configuration | Flicker (luma) | Over the patch's contrast | Steady error |
|---|---|---|---|
| no anti-aliasing | 0.00275 | 0.062 | 0.00507 |
| FXAA (the project's setting) | 0.00275 | 0.062 | 0.00491 |
| TAA (the old setting) | 0.00213 | 0.048 | 0.00287 |
| MSAA 4x | 0.00275 | 0.062 | 0.00494 |

**FXAA adds no shimmer to the floorboards: its flicker equals no anti-aliasing and 4x MSAA** (0.0028 of luma, about 0.7 of an 8-bit level and 6% of the patch's own contrast, the same in all three, so it is not edge aliasing; it is the painterly detail that appears as the pixel footprint shrinks), and TAA's is lower only because it averages frames. The founder's playtest should still look at the boards while running; the measurement says to expect none.

## Where captures go (a proposal for review images)

A 1920 x 1080 PNG with this look's grain is about 1.7 MB and Git keeps every version for ever: round 2 left 59 MB of them in the tree, and one more full set would add 15 to 25 MB. The proposal this round follows:

1. Captures are written to a scratch folder, never straight into `docs/`.
2. The tree holds **one latest `after/`** (the nine review cameras as PNG, the record) and **one `before/`** it is judged against (JPEG at quality 90, a reference only), plus `timings.json` for each: about 20 MB. Each round overwrites them; older rounds are in Git's history, named in this file.
3. Sweeps, light checks, contact crops, night frames, probe images and tuning renders stay in scratch. Anything the founder needs to see (the season sweep) is one JPEG.
4. If the history still grows too fast, move the PNGs to Git LFS or to release assets and keep only the JPEGs and `timings.json` in the tree; that needs the integrator.
