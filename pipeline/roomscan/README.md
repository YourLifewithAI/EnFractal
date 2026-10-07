# roomscan: room capture pipeline (Lane C)

Turns a folder of room photos into a coverage report: which parts of the room the photos cover,
and specific instructions for the photos to take next. It is stages C1 (ingest) and C2 (coverage
and guidance) of the [room capture pipeline](../../docs/pipeline/ROOM-CAPTURE-PIPELINE.md).

- **C1 `roomscan ingest`**: HEIC to JPEG, EXIF kept by allow-list (device, lens, focal length,
  exposure, timestamp) with GPS and every other location field stripped, exact and near-duplicate
  detection, blur and exposure-clipping scores, and a session manifest of every photo's name, size
  and SHA-256. CPU only.
- **C2 `roomscan coverage`**: camera poses from MapAnything in GPU batches that fit 8 GB, merged
  into one frame and refined; a view graph of which photos see the same surfaces; the room's floor,
  walls and ceiling fitted as a box; coverage grids (25 cm cells) of each surface; furniture found
  by an open-vocabulary detector, with per-object photo counts and the sides it was seen from; and
  ranked, plain-language guidance, a top-down coverage map and an HTML report.

## Rules it follows

- The source folder is **read in place and never written**. Everything derived goes under
  `<repo>/captures/<room>/`, which Git ignores. Every write goes through `OutputGuard`, which refuses
  paths outside that folder or inside the source folder.
- Written images carry no GPS, maker notes, XMP or IPTC; each write is checked and fails closed.
- A session is identified by the sorted (name, size, SHA-256) listing of the photos together with the
  ingest version and the quality thresholds, so the same set under the same rules always maps to the same
  session, and adding photos or changing a rule makes a new session without touching the old one. An
  existing session's manifest is never overwritten by a different one.
- Derived JPEGs and thumbnails are named by the first 16 hex digits of the source's SHA-256 and nothing
  else, so the same bytes always give the same file.
- Names of files and EXIF text are treated as data: escaped in the HTML report, and escaped for Markdown in
  both Markdown reports.
- Model weights and caches go to `<repo>/.cache/` (ignored by Git), never the user profile.
- GPU stages pause while a window titled `EnFractal...` is open (the founder may be playing on the
  same card) and wait for free VRAM.
- Photos, derived images and reports of a real room are never committed.

## Install

Needs [uv](https://docs.astral.sh/uv/). uv provides Python 3.12 and installs the locked versions into
`pipeline/roomscan/.venv`. Nothing is installed globally.

```powershell
cd pipeline/roomscan
uv sync --locked                 # C1 and the tests: CPU only, about 150 MB
uv sync --locked --extra pose    # adds C2 on an NVIDIA GPU: torch 2.14.1 + CUDA 12.6, about 4 GB
```

With the `pose` extra installed, **always pass `--extra pose`** to `uv run` and `uv sync`. Without it,
uv's exact sync removes the GPU packages again.

The first `roomscan coverage` downloads the models into `<repo>/.cache/`:

| Model | Use | Size | Licence |
|---|---|---|---|
| `facebook/map-anything-apache` @ `00f9c24` | camera poses, depth | 4.91 GB | Apache-2.0 |
| `google/owlv2-base-patch16-ensemble` @ `cfd3195` | furniture detection | 0.62 GB | Apache-2.0 |
| `facebookresearch/dinov2` @ `7764ea0` (torch.hub, code only) | encoder definition MapAnything builds on | 4 MB | Apache-2.0 |

All three allow commercial use. One caveat on the third: at the pinned commit, DINOv2's `hubconf.py` also
imports the Cell-DINO (CC BY 4.0 code) and X-Ray-DINO modules, whose weights are non-commercial research
only. Importing it runs that code, but no Cell-DINO or X-Ray-DINO weights are fetched or used (the encoder
weights come from the MapAnything checkpoint). Not used, because they are non-commercial or gated:
`facebook/map-anything` (CC BY-NC 4.0), `facebook/VGGT-1B` (CC BY-NC 4.0), `naver/MASt3R`
(CC BY-NC-SA 4.0), `facebook/VGGT-1B-Commercial` (gated application). See `MODEL_REGISTRY` and
`REJECTED_MODELS` in `src/roomscan/coverage/backend.py`.

## Run

Pass the photo folder as an argument; never hard-code it. On the founder's machine Google Drive for
desktop shows it as a local folder; `tools/check-run1-readiness.ps1` finds it.

```powershell
cd pipeline/roomscan
uv run --locked --extra pose roomscan ingest --source "<folder of photos>" --room garage
uv run --locked --extra pose roomscan coverage --room garage
uv run --locked --extra pose roomscan sessions --room garage    # list sessions
```

`coverage` uses the latest session unless `--session s-...` is given. Other options:
`--chunk-size N` (photos per GPU batch; default from free VRAM, at most 32),
`--skip-detection` (no furniture, no GPU detector), `--recompute` (ignore cached poses),
`--max-views N` (a quick test on a subset), `--measurements PATH` (tape measurements; default
`captures/<room>/measurements.json` when it exists) and `--no-measurements` (keep the model's own scale).

Outputs, all under `captures/<room>/`:

| Path | What |
|---|---|
| `coverage-report.html` | **The report for the founder**: one self-contained page with the map embedded |
| `coverage-map.png`, `walls.png` | Top-down coverage map with numbered next photos; the walls unfolded |
| `coverage-report.md` | The same report as Markdown |
| `photos/`, `thumbs/` | Clean full-size JPEGs and 384 px previews, no location data |
| `sessions/<id>/manifest.json` | C1 result: every photo's name, size, SHA-256, EXIF, scores and flags |
| `sessions/<id>/ingest-report.md` | Duplicates, flags and lenses in brief |
| `sessions/<id>/coverage/coverage.json` | Every number behind the report, including the scale and the tape fit |
| `sessions/<id>/coverage/coverage-run.json` | Stage times, GPU seconds, peak VRAM, models and licences |
| `sessions/<id>/coverage/poses.npz`, `detections.json` | Caches; reused when the photos and settings match |

## Measured on the RTX 2070 SUPER (8 GB), October 2026

- MapAnything, fp16 trunk: 32 photos per batch, about 14 s each, peak 5.8 GB allocated
  (6.5 GB reserved). 8 photos: 3.9 GB; 24 photos: 5.2 GB.
- Model load: about 30 s from the cache (about 4 min the first time, including the download).
- Ingest of 370 iPhone HEIC files (911 MB): about 50 s on 6 CPU workers.
- Coverage of the 180 usable garage photos, from scratch: about 6 min. That is 96 s of GPU
  inference for poses, 17 s for furniture, and the rest CPU. A re-run with cached poses and
  detections takes about 1.5 min.

## Test

```powershell
cd pipeline/roomscan
uv run --locked --extra pose pytest -q    # or: uv run --locked pytest -q (CPU-only environment)
```

The tests use only synthetic images and a ray-cast synthetic room (`tests/synth.py`,
`tests/scene.py`). A fake pose backend returns each batch in its own random frame and scale, and a
fake detector returns the true furniture boxes, so the whole C2 pipeline runs end to end without a
GPU, a model or a real photo. The one test that touches torch is skipped when torch is absent.

## How it works

1. **Ingest** (`ingest.py`, `exif.py`, `quality.py`). Blur is judged on two measures: the variance
   of the Laplacian on the sharpest quarter of tiles (relative to the session median), and the
   contrast-independent ratio of Laplacian to gradient on the strongest edges. A photo is *blurry*
   only when both are low. A low score with crisp edges is a plain surface (`low_detail`): it is
   kept, because walls and floors matter for coverage.
2. **Poses** (`coverage/backend.py`, `chunks.py`). One keyframe batch spread over the capture order
   defines the room frame; every other batch shares six keyframes (anchors) with it. Each batch is
   aligned to the room by a similarity fitted on the anchors' point maps. A batch whose anchors
   disagree is left out and its photos are reported, never placed somewhere wrong.
3. **Refinement** (`coverage/refine.py`). A trimmed ICP with normal checks pulls each batch onto the
   surfaces of all the others. The reference batch fixes the merged room's scale; see "Room scale" below.
4. **View graph** (`coverage/graph.py`). Photo i links to photo j when at least 10% of what i sees
   lands in j's view at the depth j measured. Photos outside the largest group are "not fitted".
5. **Room and grids** (`coverage/layout.py`, `grid.py`, `visibility.py`). Floor plane by RANSAC,
   walls by the dominant directions (Manhattan world), each at the strongest plane with almost
   nothing seen behind it; ceiling by downward-facing points. The map is turned so the first photo
   looks up the map, which keeps the wall letters stable between runs. Every 25 cm cell is
   projected into every photo's depth map. Each photo either sees the cell, sees furniture standing
   on or against it, is blocked by clutter further away (still a gap), or sees past it (a doorway
   or window). A cell is well covered when three photos from different directions see it. Photos
   taken below 0.8 m are counted separately, because that is the player's point of view.
6. **Furniture** (`coverage/objects.py`, `detect.py`). OWLv2 with a fixed word list, lifted to 3-D
   with the point maps and clustered across photos. A photo counts for an object when its surface
   points fall inside the object's box, and the side it was seen from is measured.
7. **Guidance and report** (`coverage/guidance.py`, `render.py`, `report.py`). Ranked
   instructions: lower walls and floor first, because the player is 10 cm tall.

## Room scale: the model's guess and the founder's tape

Each GPU batch comes back in its own metric frame and the batches disagree by tens of percent: in the
garage set, taking any one batch's own scale as right would put the room from 1.6% smaller to 26% larger
than the first (seed) batch does, and every one of them put it larger than the founder's tape does.
**The merged room is in the seed batch's scale** and that spread is only an error bar. Nothing in the
photos fixes the scale; a tape does.

`captures/<room>/measurements.json` (local, never committed) lists what was measured:

```json
{"room": "garage", "measurements": [
  {"id": "length", "kind": "wall_to_wall", "between": ["B", "D"], "session_id": "s-...", "value_m": 5.842,
   "sigma_m": 0.01, "where": "inside length"},
  {"id": "height", "kind": "floor_to_ceiling", "between": [], "session_id": "s-...", "value_m": 2.464},
  {"id": "corner", "kind": "diagonal", "between": ["AB", "CD"], "session_id": "s-...", "value_m": 7.4},
  {"id": "back-door", "kind": "opening", "between": ["B"], "session_id": "s-...", "value_m": 0.787}]}
```

- `between` is two opposite walls (`A`/`C` or `B`/`D`), two opposite corners, or the one wall an opening is in.
  Wall letters are on the coverage map (wall A is the one the first photo faced). `session_id` is the session
  whose map you read them from: they follow the first photo, so a measurement is used only for that session.
- `sigma_m` is the tape's own error (default 1 cm); the fit adds 10 cm for where the model puts a wall, the
  floor or the ceiling.
- **Fit**: one uniform scale, by weighted least squares on `ln(tape / model)`, never one scale per axis;
  openings are checks only. It is applied to the merged poses and the room box before any grid is built, so
  the 25 cm cells, the 0.8 m low-photo threshold and the guidance distances are in real metres. The box is the
  one fitted before scaling, scaled with the poses: fitting it again on the scaled points moved the garage
  ceiling by 8 to 10 cm with the fifth digit of the factor.
- **The report** gives each measurement against the model in cm and per cent, the scale each would ask for
  alone (information only), the RMS, and flags a residual above twice the typical spread between the batches'
  own copies of a wall (at least 5 cm). `coverage-run.json` and `coverage.json` record `scale_source`
  (`tape` or `seed_batch`) and the applied factor.
- A file with a mistake stops the run before any work, saying what to fix; measurements for another session,
  or an implausible scale (outside 0.6 to 1.6), are left out and the report says why.

## Input handling

- **Digital zoom.** iPhone photos taken zoomed in (DigitalZoomRatio above 1.05) get no EXIF intrinsics: the
  model estimates their focal length itself. The garage set shows why: eight photos at ratio 1.42 keep the
  26 mm equivalent of an unzoomed photo, while the one at 1.71 records 44 mm, which is 26 mm times the zoom.
  EXIF does not say consistently whether the equivalent includes the zoom, and a wrong focal length bends the
  batch it is in. The report lists these photos and asks for 1x.
- **Photo shape.** The model takes 3:4 portrait input. A photo within 8% of that shape is scaled to cover it
  and trimmed (an iPhone portrait loses a pixel or two). Any other shape, landscape or 9:16, is scaled to fit
  and padded with grey, never cropped (a landscape photo cropped to portrait lost 43% of its width); the
  padding is masked out of the point map and the principal point follows the photo.

## Limits

- Room sizes are only as good as the model's metric scale. Without tape measurements they can be off by
  tens of percent (see "Room scale").
- The room is assumed to be a box. An L-shaped room or an alcove shows up as a poor wall fit.
- Furniture detection misses and mislabels things; it is a starting point for the inventory (C3).
- The blur threshold was calibrated by eye on one photo set (the garage, October 2026).
