# Run 1 look captures

Five fixed review cameras (`tools/look/review_cameras.json`) at 1920 × 1080 on the founder's NVIDIA RTX 2070 SUPER, 16:30 on 6 October (autumn), captured with `tools/look/capture-look.ps1`. Score them against [the look bible](../../LOOK-BIBLE.md). No scores or verdict yet: the look review rounds start from these.

| | Commit | Renderer | Look |
|---|---|---|---|
| [`before/`](before/) | `28fc364` (Run 0 head) | Compatibility (OpenGL) | Plain `StandardMaterial3D` per role, key, ambient and lamp from the seed preset |
| [`after/`](after/) | `ef9f19e` (`run1/look`) | Forward+ (Vulkan) | Draft `storybook_painterly@1` (sha256 `dc658f96…`): diorama key, VoxelGI, SSAO, glow, depth of field, grade LUT, painterly roles, grain and vignette |

Files per folder: `player_eye.png`, `over_shoulder.png`, `companion.png`, `low_corner.png`, `ceiling_corner.png` and `timings.json`. `after/sweep_ceiling_corner.png` shows the ceiling-corner view in rows winter, spring, summer, autumn and columns 07:00, 12:00, 16:30, 21:00.

The avatars are still the Run 0 bodies (player 0.30 m, companion 0.24 m); the cameras are framed for the 0.10 m body Lane P is building, so the avatars will look smaller after that merge. The companion's floating name and the HUD are hidden in captures.

## Frame time (ms, vsync off, 120 warm-up and 300 measured frames)

Budget: 16.7 ms at 1920 × 1080 (`budgets.target_frame_ms`). Both runs waited for a quiet GPU and sampled it throughout: no other job ran during either capture; ordinary desktop use held the card at about 20% before launch.

| Camera | Before p50 | Before p99 | After p50 | After p95 | After p99 | After max | After GPU mean | After GPU p95 |
|---|---|---|---|---|---|---|---|---|
| player_eye | 0.96 | 2.04 | 10.60 | 11.53 | 12.06 | 12.31 | 8.28 | 9.11 |
| over_shoulder | 0.98 | 1.89 | 10.76 | 11.51 | 11.94 | 12.13 | 8.38 | 8.90 |
| companion | 0.96 | 1.55 | 10.92 | 11.43 | 11.66 | 12.02 | 8.55 | 8.98 |
| low_corner | 1.09 | 1.92 | 10.18 | 10.71 | 10.93 | 11.12 | 7.77 | 8.24 |
| ceiling_corner | 1.12 | 1.69 | 10.22 | 10.68 | 10.90 | 11.06 | 7.82 | 8.22 |

After: 481 MiB of video memory; the VoxelGI bake at room load took 162 ms. These are static cameras; walking, shader compilation at first launch and long thermal runs are not measured here.
