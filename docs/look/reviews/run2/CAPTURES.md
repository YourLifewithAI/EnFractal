# Run 2, Lane L: the observe view's focus and frame budget

Captured on this Windows machine (NVIDIA RTX 2070 SUPER, Forward+, Vulkan, 1920 x 1080, vsync off, 120 warm-up and 300 measured frames, 17:00 on 15 April, lamps on, FXAA), with no game window open and the card quiet before each launch. Lane A ran headless tests on the same machine during the `after` capture (its p99 spikes to about 16.7 ms on four unchanged cameras are that, not the look).

**Capture launches: 3; review frames rendered: 24** (2 in a pilot, 11 in `before` and 11 in `after`; each set also cuts 6 avatar-foot contact crops from frames it already has). The pilot (`observe_f3` and `observe_f4` only) proved the new HUD-rig cameras before the sets were taken; `before` ran once on the baseline `00517c4` (v1, the old HUD) with the new harness and cameras, `after` once on `e384df1` with `-Style game/styles/storybook_painterly/v2.json`. Neither set has the season sweep or the light checks (`-NoLightChecks`): this round changes only the observe views' focus, band and blur, nothing the light checks or the sweep measure.

## For the founder

`observe_before_after.jpg`: three rows, `observe_view`, `observe_f3` and `observe_f4`, before (left, v1 with the old HUD) and after (right, v2 draft). `observe_f3` and `observe_f4` are the real HUD's F3 and F4 views with O on. Look at F4: before, the player (the orange figure) is a blurred sliver; after, it is whole and in focus. The companion (green) softens when it stands outside the player's band, by design (observe does not stretch for it). In F3 the two stand side by side at the same depth, so both were already roughly in focus; the F4 view, where they are at different depths, is where the pivot between them failed. The background blur is a third weaker (amount 0.3 to 0.2); it is still strong, but it is the thing to judge.

## Frame time (ms)

`timings-before.json` and `timings-after.json` hold everything. The machine ran about 4% slower in the second run (compare the cameras this round did not change), so read the observe rows against `diorama_high` of the same run.

| Camera | Before p50 | Before p95 | Before GPU mean | After p50 | After p95 | After p99 | After GPU mean |
|---|---|---|---|---|---|---|---|
| player_eye | 10.55 | 12.05 | 7.39 | 11.27 | 13.43 | 16.68 | 7.96 |
| over_shoulder | 10.86 | 12.23 | 7.59 | 11.47 | 13.26 | 16.38 | 8.13 |
| companion | 10.62 | 11.67 | 7.39 | 11.04 | 12.68 | 16.71 | 7.76 |
| low_corner | 10.44 | 11.53 | 7.16 | 10.79 | 11.76 | 14.64 | 7.52 |
| ceiling_corner | 11.13 | 12.17 | 7.86 | 11.63 | 12.58 | 14.96 | 8.33 |
| diorama_high | 11.37 | 12.40 | 8.12 | 11.84 | 12.75 | 13.95 | 8.53 |
| iso_room | 11.21 | 12.25 | 7.94 | 11.64 | 12.66 | 15.24 | 8.30 |
| window_view | 10.45 | 11.57 | 7.16 | 10.82 | 11.69 | 13.92 | 7.46 |
| observe_view | 12.85 | 13.96 | 9.48 | 12.21 | 13.10 | 15.44 | 8.86 |
| observe_f3 | 12.98 | 14.45 | 9.63 | 12.28 | 13.33 | 15.62 | 8.94 |
| observe_f4 | 13.08 | 13.99 | 9.67 | 12.41 | 13.68 | 15.55 | 9.08 |

**Why the first machine's 17.34 ms is not on this table.** Run 1 measured `observe_view` at 17.34 ms p95 on a machine that ran every camera about 19% slower than this one (its `diorama_high` was 14.91 ms against 12.40 here). On this machine the observe view was already inside 16.7 ms before the change (13.96). The ratio to `diorama_high` of the same run is the number that carries over: before, `observe_view` was 1.126 times it here and 1.163 times it in Run 1 (the cost sits in the blur amount, which the ratio isolates); after, 1.027. Applied to Run 1's `diorama_high` (14.91 ms), the observe view's p95 reads **16.8 before (Run 1 measured 17.3) and 15.3 after**; `observe_f3` 17.4 before and 15.6 after; `observe_f4` 16.8 before and 16.0 after. These are extrapolations from a ratio, not measurements on the other machine; the founder's machine confirms them.

## What the cost is (numbers first, rendering to confirm)

Godot's circular bokeh gathers about (64 x amount)^2 / (2 x blur_scale) samples at every half-resolution pixel whatever the frame holds, with blur_scale 1.0 at the project's bokeh quality 2: 184 samples a pixel at the observe amount 0.3, 82 at 0.2, 55 at the tilt-shift's 0.165. Run 1's two cameras give 30.4 ms of GPU time per unit of amount squared; a check in the look test pins v2's amount on that model (p95 at least 0.5 ms inside the budget). The render agrees: the observe views' GPU time fell by 0.6 ms (0.7 ms in `observe_f3`) while the unchanged cameras rose by 0.4 ms, and their GPU time against `diorama_high` fell from 1.17 to 1.04.
