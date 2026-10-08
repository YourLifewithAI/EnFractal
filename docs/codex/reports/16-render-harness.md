# Brief 16: shared-look revision and fresh final reference

Completed this round inside the existing harness scope. The package, validator,
setup pin, solar computation, accepted eye cameras and figures remain intact.
Manifest-only perspective overviews now fit all eight floor/roof corners:
**87% frame width**, with the floor itself spanning **82.1%**, from three corners.
Floor corners retain 40-pixel margins. Fitting the full 2.7 m height in 16:9
requires a lower 15-degree pitch; cameras remain 4.48 m above floor.

A shared ground surround sits 0.025 m below floor, outside the footprint; the
world's lower hemisphere also has a ground tone. Horizontal-distance haze keeps
room land clear from above, grows gently through the eye views and strongly
beyond them. Haze reaches 4.8% at 7 m and 76.6% at 20 m. The summer rig now uses
a stronger golden-white 0.65-degree sun, blue sky/cool fill, paler horizon and
Standard/Medium High Contrast at -0.7 EV. Matching worn-path patches in the window
view measure **RGB 168.438/149.529/127.339 sunlit**, versus
**42.099/56.636/72.347 shaded**; shade's blue/red ratio rises from 0.7560 to 1.7185.

Cliff strata have warped heights, variable thickness and steep-face weighting;
tops fade out and loose rock/boulders retain only a hint. Ground washes are
broader and quieter; fine strokes/normals fade with distance. Eye focus stays at
0.4 m with f/32: calculated blur is 2.37 pixels at 0.15 m and 1.14 at 2 m,
growing gently farther away. Every final view was inspected individually again,
along with the sheet, diagnostic and kit.

The **full fresh final run took 508.021 s (8m28s)**, including all artifacts;
no images were retained. Receipt/source/view/kit hashes match. All **21 tests
pass**, including determinism for seven view PNGs, sheet and kit. Still weak:
water is simple blue strips, vegetation is coarse, the fixture horizon is a
flat card, and the window view is dominated by a cottage. Founder look approval,
larger-package timing and game/collision equivalence remain unverified.
Cost $0; GPU time zero. No out-of-scope changes, installs, commits or pushes.

## Evidence

Commands ran from `C:\dev\EnFractal-codex\16-render-harness`, branch
`codex/16-render-harness`. Scratch scripts/logs are in `$env:TEMP`.
Only background Blender 5.2.2, CPU Cycles and CPU OIDN were used.
No game window, paid API, photographs or real-place data was accessed.

### Complete harness suite

```text
python -B -S -m unittest pipeline.landscape.harness.tests.test_harness -v
Exit code: 0
----------------------------------------------------------------------
Ran 21 tests in 40.341s

OK
GEOMETRY_FLAGS_PASS closed solids, overlapping solids, open surface
OVERVIEW_PROJECTION_PASS eight floor/roof corners, 87 percent width
BLENDER_EXIT 0
BLENDER_EXIT 0
SUMMER_SKY_PASS upper hemisphere blue
DETERMINISM_PASS 7 view PNGs + sheet + kit; Blender 5.2.2 CPU OIDN, 96x54
BLENDER_EXIT 0
PARTIAL_RERENDER_PASS retained bytes, sheet, source revision; changed inputs/tampering rejected
SUITE_EXIT 0
```

No skips. The projection probe uses Blender's own `world_to_camera_view`,
independently of the standard-library projection. Existing enclosure and
retention assertions were kept. The floor framing check now additionally checks
roof corners and 80-90% floor width. New checks cover near-reach blur, blue summer
sky pixels and kit determinism. Draft review exposed a reversed world-normal
sign (ground above the horizon); it was corrected before the final suite/run.
The sky regression protects that correction.

### Full fresh final run

```text
$finalClock = [System.Diagnostics.Stopwatch]::StartNew()
python -B -m pipeline.landscape.harness.render --package pipeline/landscape/harness/reference/package --room pipeline/landscape/corpus/rooms/garage_nominal --label REFERENCE --expect-setup pipeline/landscape/harness/ab-setup.json --out pipeline/landscape/harness/reference --kit-picture
$finalExit = $LASTEXITCODE
$finalClock.Stop()
Write-Output "FINAL_EXIT $finalExit"
Write-Output ('FINAL_WALL_SECONDS {0:F3}' -f $finalClock.Elapsed.TotalSeconds)
Exit code: 0
BLENDER_EXIT 0
RENDER_COMPLETE pipeline/landscape/harness/reference
FINAL_EXIT 0
FINAL_WALL_SECONDS 508.021

Select-String -LiteralPath pipeline/landscape/harness/reference/blender.log -Pattern 'HARNESS_' | ForEach-Object { $_.Line }
Exit code: 0
HARNESS_RENDER view=overview_ne seconds=67.578 bytes=560669
HARNESS_RENDER view=overview_sw seconds=65.186 bytes=543130
HARNESS_RENDER view=overview_se seconds=65.092 bytes=541430
HARNESS_RENDER view=eye_player seconds=55.000 bytes=557846
HARNESS_RENDER view=eye_companion seconds=53.532 bytes=551884
HARNESS_RENDER view=eye_window seconds=62.907 bytes=508588
HARNESS_RENDER view=slope seconds=15.335 bytes=393656
HARNESS_KIT seconds=66.753099 bytes=524741
HARNESS_COMPLETE seconds=369.295668
```

Final settings are 1280x720, 48 samples, four threads, seed 16, CPU Cycles/OIDN.
The receipt records **451.383900 s** of render calls including slope and kit;
**507.862928 s** of harness wall time includes validation, launch, PNG
finalization and sheet construction. The outer 508.021 s also includes Python
startup. There are seven `HARNESS_RENDER` lines plus the kit, zero retain lines.
The transient Blender log was copied to `$env:TEMP/enfractal-brief16-round-final.log`
and omitted from delivery; its raw timing lines are above.

The final revision is `fa84a0de0d7eac433bd873e14e4cd9fb0f6043c2107109d4e82868c410718f51`.
`partial_rerender: false`, `retained_views: []`, and
`uniform_harness_revision: true`. Every view, slope and kit records that same
`rendered_harness_sha256`. Source bytes were frozen before the suite and final
run; subsequent edits are only the README and this report, outside the source
revision inventory. The pinned package's bytes are unchanged (writer comparison
passes); its 17,742 bytes remain below 1 MB.

### Colour samples and individual review

```text
$colourScript = Join-Path $env:TEMP 'enfractal-brief16-colour.py'
python -B $colourScript
Exit code: 0
COLOUR_PATCH sunlit role worn_path centre_px [250, 560] size_px 11x11 ground_m [0.34027, 0, -0.16965] mean_RGB [168.438, 149.529, 127.339] blue_red_ratio 0.756
COLOUR_PATCH shade role worn_path centre_px [250, 490] size_px 11x11 ground_m [0.52349, 0, -0.261] mean_RGB [42.099, 56.636, 72.347] blue_red_ratio 1.7185
SUN_MINUS_SHADE_RGB [126.339, 92.893, 54.992]
SHADE_MINUS_SUN_BLUE_RED_RATIO 0.9625
```

These are arithmetic means of final PNG RGB bytes, over 11x11 patches, with
coordinates measured from the top-left. Both rays hit the reference's +X
worn-path quad; their ground positions are back-projected using the fixed eye
camera. Patches avoid objects and shadow edges. These are output colour
measurements, not correlated colour-temperature claims for a coloured material.
Sunlit ground is warm (R > G > B); shaded ground is cool (B > G > R), well above
black. The patches are at different distances and sample a painted wash, so they
are evidence of the resulting look, not an isolated illuminant measurement.

- `overview_ne`: all corners fit, warm path/cool cast shadows; loose boulder is plain.
- `overview_sw`: whole footprint fits; shared land fills below the horizon; broad washes.
- `overview_se`: whole footprint fits; greenery stays clear, horizon fades.
- `eye_player`: figure and near ground readable; warm head/cloth highlights, cool shadows.
- `eye_companion`: reach stays readable, gentle distant softness; coarse kit forms remain.
- `eye_window`: reach readable, strong warm/cool ground contrast; cottage still blocks context.
- `slope`: green floor/red ridge readable; no DOF, outside blind sheet.
- `kit`: all thirteen entries visible and labelled, grounded on the shared look.
- `sheet`: every final view included; blind label and fixed answers only.

These are my observations, not a founder gate pass. The horizon card, rectangular
land edge and simple water belong to this unchanged format fixture; contenders
supply their own land and scenery. Freeze this same rig for both A/B contenders.

### Artifact, contract and scope audit

```text
$verifyScript = Join-Path $env:TEMP 'enfractal-brief16-round-verify.py'
python -B $verifyScript
Exit code: 0
FINAL_IMAGE overview_ne.png seconds 67.57843 bytes 560669
FRAME_WIDTH overview_ne full 0.87 floor 0.821087
FINAL_IMAGE overview_sw.png seconds 65.185724 bytes 543130
FRAME_WIDTH overview_sw full 0.87 floor 0.821087
FINAL_IMAGE overview_se.png seconds 65.092195 bytes 541430
FRAME_WIDTH overview_se full 0.87 floor 0.821087
FINAL_IMAGE eye_player.png seconds 55.000472 bytes 557846
EYE_CLEAR eye_player ground 0.0 height 0.087 focus 0.4000000059604645 fstop 32.0
FINAL_IMAGE eye_companion.png seconds 53.532032 bytes 551884
EYE_CLEAR eye_companion ground 0.0 height 0.087 focus 0.4000000059604645 fstop 32.0
FINAL_IMAGE eye_window.png seconds 62.906815 bytes 508588
EYE_CLEAR eye_window ground 0.0 height 0.087 focus 0.4000000059604645 fstop 32.0
FINAL_IMAGE slope.png seconds 15.335133 bytes 393656
FRAME_WIDTH overview_ne full 0.87 floor 0.821087
FINAL_IMAGE kit.png seconds 66.753099 bytes 524741
PNG_PASS nine fresh metadata-free images, dimensions and size limits
RECEIPT_MATCH_PASS no reuse, source + all view/kit hashes
SOURCE_REVISION fa84a0de0d7eac433bd873e14e4cd9fb0f6043c2107109d4e82868c410718f51
TOTAL_RENDER_SECONDS 451.3839
TOTAL_WALL_SECONDS 507.862928
PACKAGE_STATS {'triangles': 26, 'expanded_triangles': 3958, 'instances': 15}
WORKING_TREE_SCOPE_PASS 33 uncommitted files
FRESH_RUN_LOG_PASS seven renders + kit, zero retains
VERIFY_EXIT 0

python -B contracts/validate.py pipeline/landscape/corpus/rooms/garage_nominal
Exit code: 0
OK: 1 item(s) checked, 0 problem(s)

git check-attr text -- pipeline/landscape/harness/reference/package/package.json pipeline/landscape/harness/reference/receipt.json
Exit code: 0
pipeline/landscape/harness/reference/package/package.json: text: unset
pipeline/landscape/harness/reference/receipt.json: text: unset
```

The verifier checks exact source/image hashes, no reuse, dimensions, PNG chunk
whitelists (IHDR/IDAT/IEND), size limits, eye placement/flags, framing, LF text and
all uncommitted paths against the two scope globs. The formal branch scope
checker awaits the integrator's commit. Windows game runners were not run: this
brief permits CPU Blender author tooling and forbids the game/GPU captures.
No source setup or test remains blocked. Larger packages and other machines
need their own timing/determinism evidence; game loading and collision remain
outside this harness's verification.

## Delivery

This round changed `views.py`, `worker.py`, `render.py`, `common.py`,
`tests/test_harness.py` and `README.md` under `pipeline/landscape/harness/`.
All nine reference PNGs and `receipt.json` were freshly regenerated, and this
report was replaced. Format/writer/validator/kit geometry/setup/reference-package
bytes stay as reviewed. No shared-file change requests or commit hashes.

The shared materials version is now 2; the geometry kit version remains 1.
[Look bible](../../look/LOOK-BIBLE.md) supplies the written visual direction;
Drive frames were not accessed. The existing solar regression cites
[NOAA](https://gml.noaa.gov/grad/solcalc/solareqns.PDF) and
[NREL Appendix A](https://docs.nlr.gov/docs/fy08osti/34302.pdf).
The existing format follows [glTF 2.0](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html).

Final artifact audit after rewriting this report and omitting the transient log:

```text
$verifyScript = Join-Path $env:TEMP 'enfractal-brief16-round-verify.py'
python -B $verifyScript
Exit code: 0
PNG_PASS nine fresh metadata-free images, dimensions and size limits
RECEIPT_MATCH_PASS no reuse, source + all view/kit hashes
WORKING_TREE_SCOPE_PASS 32 uncommitted files
FINAL_VERIFY_EXIT 0
```
