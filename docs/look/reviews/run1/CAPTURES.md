# Run 1 look captures

Fixed review cameras (`tools/look/review_cameras.json`) at 1920 × 1080 on the founder's NVIDIA RTX 2070 SUPER, 16:30 on 6 October (autumn), captured with `tools/look/capture-look.ps1`. Score them against [the look bible](../../LOOK-BIBLE.md). No scores or verdict yet: the look review rounds start from these.

| | Commit | Renderer | Look |
|---|---|---|---|
| [`before/`](before/) | `28fc364` (Run 0 head) | Compatibility (OpenGL) | Plain `StandardMaterial3D` per role, key, ambient and lamp from the seed preset |
| [`after/`](after/) | `ef9f19e` (`run1/look`) | Forward+ (Vulkan) | Draft `storybook_painterly@1` (sha256 `dc658f96…`): diorama key, VoxelGI, SSAO, glow, depth of field, grade LUT, painterly roles, grain and vignette |
| [`step3/`](step3/) | `run1/look` `57dfee6` merged with `run1/integration` `edf1db0` (a temporary merge, recorded as `5dbcd69` in `timings.json`) | Forward+ (Vulkan) | Draft `storybook_painterly@1` (sha256 `82112f20…`): the review fix round plus the 6 October art direction: stronger swings, the seasonal day, warm golden hour, tilt-shift from high views, and the two art-direction cameras |
| [`round2/`](round2/) | `run1/look` `8c20037` | Forward+ (Vulkan) | Draft `storybook_painterly@1` (sha256 `e1200d00…`), round 2: light only from real sources (the sun on a solar model at 30 degrees north, only through the west window; a light-blue sky fill; lamps that switch; the shell in VoxelGI as an interior), moonlit nights, the global orange cast removed, filtered sun shadows |

Files per folder: `player_eye.png`, `over_shoulder.png`, `companion.png`, `low_corner.png`, `ceiling_corner.png` and `timings.json`; `step3/` adds `diorama_high.png` and `iso_room.png`. `after/sweep_ceiling_corner.png` shows the ceiling-corner view in rows winter, spring, summer, autumn and columns 07:00, 12:00, 16:30, 21:00. `step3/sweep_diorama_high.png` shows the high-angle diorama view in rows winter, spring, summer and autumn (15 January, April, July and October) and columns 07:00, 12:00, 16:30, 18:00 and 21:00; the 18:00 column is the seasonal swing at one clock time (night in winter and autumn, sunset light in spring, golden in summer).

In `before/` and `after/` the avatars are the Run 0 bodies (player 0.30 m, companion 0.24 m). `step3/` was captured on a merge with the integration branch, so it shows the 10 cm player Lane P built and the 0.24 m companion. The companion's floating name and the HUD are hidden in captures. In `step3/over_shoulder.png` the companion appears to hover a little above the rug; that is the Run 1 companion body, not the look.

## Frame time (ms, vsync off, 120 warm-up and 300 measured frames)

Budget: 16.7 ms at 1920 × 1080 (`budgets.target_frame_ms`). Every run waited for a quiet GPU and sampled it throughout; no other job ran during any of them, and no game window was open for `step3/`.

| Camera | Before p50 | Before p99 | After p50 | After p95 | After p99 | After max | After GPU mean | After GPU p95 |
|---|---|---|---|---|---|---|---|---|
| player_eye | 0.96 | 2.04 | 10.60 | 11.53 | 12.06 | 12.31 | 8.28 | 9.11 |
| over_shoulder | 0.98 | 1.89 | 10.76 | 11.51 | 11.94 | 12.13 | 8.38 | 8.90 |
| companion | 0.96 | 1.55 | 10.92 | 11.43 | 11.66 | 12.02 | 8.55 | 8.98 |
| low_corner | 1.09 | 1.92 | 10.18 | 10.71 | 10.93 | 11.12 | 7.77 | 8.24 |
| ceiling_corner | 1.12 | 1.69 | 10.22 | 10.68 | 10.90 | 11.06 | 7.82 | 8.22 |

After: 481 MiB of video memory; the VoxelGI bake at room load took 162 ms. These are static cameras; walking, shader compilation at first launch and long thermal runs are not measured here.

| Camera (`step3/`) | p50 | p95 | p99 | max | GPU mean | GPU p95 |
|---|---|---|---|---|---|---|
| player_eye | 11.84 | 12.54 | 12.99 | 13.66 | 8.83 | 8.96 |
| over_shoulder | 12.19 | 12.89 | 13.09 | 13.30 | 9.11 | 9.22 |
| companion | 12.37 | 13.05 | 13.19 | 13.87 | 9.33 | 9.45 |
| low_corner | 11.40 | 12.06 | 12.26 | 14.72 | 8.33 | 8.44 |
| ceiling_corner | 12.07 | 12.72 | 13.12 | 13.74 | 8.96 | 9.08 |
| diorama_high | 12.29 | 12.96 | 13.06 | 13.55 | 9.21 | 9.32 |
| iso_room | 12.16 | 12.86 | 13.15 | 13.92 | 9.08 | 9.19 |

`step3/`: 492 MiB of video memory; the VoxelGI bake took 151 ms. Frame time is about 1.2 ms (GPU about 0.6 ms) above `after/`, still within budget; the stronger tilt-shift blur and the merged integration code (Jolt physics, the command host, the new bodies) are the likely causes, not yet separated. The look's self-check found no problems, and the pixel check of the grain and vignette passed (corner darkening 0.059; grain correlation 0.172 against a noise scale of 0.011).

## Round 2 (`round2/`)

Captured once on `8c20037` (no uncommitted changes), uncontended: no game window open, the GPU idle before launch and 26 to 670 MiB in use during the run (the capture's own). Review clock 16:30 on 6 October (standard time): the sun stands 14 degrees up at bearing 256 and lands through the west window north and east of the spawns, so `iso_room` and `ceiling_corner` show the gold patch and the baseline frames around the avatars are in the window's cool fill.

Files: the seven review cameras; `sweep_diorama_high.png` (rows 15 January, April, July and October; columns 07:00, 12:00, 15:00, 16:30, 18:00 and 21:00 standard time; lamps switch themselves); `night_ceiling_corner_lamps_off.png`, `night_ceiling_corner_lamps_on.png`, `night_over_shoulder_lamps_off.png` and `night_over_shoulder_lamps_on.png` (02:00 on 6 October); `summer_sun_<camera>.png` (17:30 on 15 July, the low sun across the spawns) and `lamp_night_<camera>.png` (02:00, lamps on) for `over_shoulder`, `low_corner` and `companion`; and `contact_*.png`, close crops of each avatar's feet from those frames and from the review frames, for judging contact shadows.

| Camera | p50 | p95 | p99 | max | GPU mean | GPU p95 |
|---|---|---|---|---|---|---|
| player_eye | 10.72 | 11.53 | 11.75 | 11.96 | 7.26 | 7.33 |
| over_shoulder | 11.02 | 11.80 | 12.04 | 12.38 | 7.53 | 7.60 |
| companion | 10.87 | 11.66 | 11.84 | 12.09 | 7.35 | 7.44 |
| low_corner | 10.45 | 11.20 | 11.45 | 11.52 | 6.94 | 7.04 |
| ceiling_corner | 11.37 | 12.13 | 12.28 | 12.54 | 7.82 | 7.91 |
| diorama_high | 11.65 | 12.37 | 12.58 | 12.68 | 8.04 | 8.11 |
| iso_room | 11.36 | 12.16 | 12.35 | 12.73 | 7.80 | 7.87 |

About 1 ms faster than `step3/` although there are now three shadowed lights and a whole-shell VoxelGI bake (402 ms at load): the sun's shadow is filtered instead of PCSS, which cost about 2 ms. Light checks (`light_checks` in `timings.json`): floor points in the sun's patch brighten by 0.287 in luma with the sun on, points behind the walls by 0.027 (its bounce); at 02:00 with the lamps off the frames' mean luma is 0.155 and 0.188 (95th percentile 0.30 and 0.26: dark, shapes readable), with the lamps on 0.49 and 0.43. The grain and vignette check passed (corner darkening 0.070; grain correlation 0.158 against a noise scale of 0.005). The look's self-check found no problems.
