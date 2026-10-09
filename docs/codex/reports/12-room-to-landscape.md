# Brief 12: one synthetic room, three landscapes

## Summary

Start with the [comparison sheet](../spikes/12-room-to-landscape/sheet.png). Seven CPU renders compare one 5.5 × 2.6 × 6 m synthetic garage: five plain recipe objects at default sizes, shelves, rug, window and door. Every view contains a measured 10 cm figure. The source manifest passes contract validation.

**By kind** gives the clearest landscape intention: slate ridge/valley, ochre butte, flooded ruin, ruby-tipped crystal, watchtower, terraced hamlet and heather meadow. Furniture details become destinations. Exact boxes constrain them: shelves remain a narrow cliff; the thin rug cannot contain lush vegetation. These are sparse blockouts, not a Look gate pass.

**By volume** needs no recognition. Box envelopes get rounded slopes, analytic weathering, basins and slope/facing colours; a Boolean union merges interior solids with the floor. Unknown objects work, but shelves and hollow vessels become solid land. Apertures and ceiling remain separate. It does not simulate scan-mesh erosion.

**Hybrid** blends automatic relief with recognised landmarks and unions the solids. I recommend it for the next prototype: automatic coverage with deliberate destinations. With mostly recognised inputs, its visible difference from kind is modest. Bounds and palette constrain both; its merged topology also needs cleanup before collision.

Capture must provide metric shell/openings, posed boxes, colours and supports; semantic routes also need recognition confidence. Preserving passages requires occupied meshes. The game needs derived collision, clearance, connected navigation and tested avatar slope/step limits. These renders certify none of those. Look materials need Godot review.

The companion could steer bounded themes or entity-ID landmarks through future typed commands. The trusted host validates a preview, holds player approval, then uses the shared kernel command path. No files, shell, URLs or request-supplied principal/approval enter that surface.

Founder decisions: preserve occupied space or envelopes? Allow ridges/ramps more floor? Prioritise overhead or avatar views?

## Method, grounding and limits

The [builder and README](../spikes/12-room-to-landscape/README.md) give reproduction details. Plain reference geometry imports the actual read-only recipe builders, resolves `style: "plain"`, constructs recipe materials and uses the existing fitting/manifold checks. No recipes or contracts changed. Source metadata uses primitive proxies; it does not pretend those proxies are the rendered recipe meshes or production terrain assets.

For every route, AABBs are measured from actual fitted contributor vertices, including decorations, in world metres before Boolean union. Full signed min/max deltas are in the four receipts. Union removes internal contact faces; these measurements audit each landform's contributing solid, not a segmentation of the final merged surface. Exact envelopes do **not** preserve the original occupied volume. The floor and maximum supported-object height are checked again after union, and used union materials must retain every object's principal source colour.

The volume route uses boxes, rather than semantic templates or the reference recipe meshes. It softens the interior of each footprint without moving its boundary, generates a mathematical basin/undulation rather than simulating erosion, and carries additional colour slots into crest accents. Water stays a separate visual surface. The hybrid blends 25% automatic relief with 75% authored heightfield relief; non-heightfield crystal/tower landmarks receive automatic foothills. Shell treatment is role-aware and opening-aware, never object-kind-aware. Rear/header slabs become broken ridgelines, retaining their slab extents and opening boundaries. They remain thin cliff curtains: a 10 cm wall slab cannot contain a broad, gentle mountain.

**Unresolved geometry diagnostic:** the hybrid union has 13 triangles with area ≤1e-14 m², reported under the receipt's `degenerate_triangles` field; that diagnostic includes tiny positive areas, not just exact zeros. Its nonmanifold edge count is zero. Donor parts passed the recipe's closed/manifold/positive-volume checks before union, but this merged output needs cleanup and renewed topology checks before collision/export. These author renders are not collision-ready deliverables.

Overviews share one high three-quarter camera fitted to the source room. The ceiling and two camera-facing walls are hidden for review. Low cameras sit at 0.10 m and retain the ceiling. Window, open door and ceiling fixture supply area lights plus cool world fill; this author rig does not validate the game's closed-shell lighting behaviour. The scenery is generated directly in Blender for this author spike; it is not gameplay bypassing the kernel.

### Walk and climb interpretation

Slope statistics use **assumed** thresholds: up to 35° is a walking candidate, 35–55° a climbing candidate, and above 55° too steep. Receipts measure upward-facing surface area, not reachable routes. Floor corridors and the shallow meadow are candidates; the 0.9 m doorway spans nine avatar heights. Couch foothills may be climb candidates, while butte steps, shelf risers and spire/tower faces require another traversal mechanic. The box top is 0.25 m high and the shelf top 1.70 m high: neither is reachable merely because its summit is flat. Lakes need a separate water rule. Narrow ledges, roof slopes, clearance, drops and support changes need actual avatar/collision tests.

### Committed test room

The read-only [test room](../../../game/rooms/test_room/room.json) is 4 × 2.4 × 3 m. Kind would map its table to a mesa/outpost, box to a butte, book to a layered outcrop, rug to meadow, and rug-supported doorstop to a small crag. Preserve its west window and ceiling. Volume needs only their asset dimensions/poses/colours and shell data; it would fill the proxy table volume rather than inventing leg passages. Hybrid adds recognised landmarks and falls back to volume for low-confidence kinds. Keep the doorstop's 0.006 m rug support offset. These test-room transformations are design proposals, **unverified** by rendering or game tests; only the requested synthetic garage was built.

### Geography-era look-back

The corrected `origin/geography-era-final` ref is readable. Read-only inspection of `game/scripts/terrain_mesh_job.gd` found source-derived normals, relief/steepness attributes and stitched LOD borders. `game/shaders/painterly_ground.gdshader` supplies stable-space paint, normal-based stone, contact/path soil masks and rough surface response. Those material controls, normal generation and seam tests can carry over; geographic tile scale and a single outdoor heightfield cannot represent room ceilings, shelf voids or stacked objects at 10 cm. Resample at room/avatar scale, preserve multilevel solids, and derive collision from the same data. The historical `docs/engine/phase1/painterly-render-audit.md` also supports carrying forward winding/light calibration checks before judging artwork. No historical PNGs, photo overlays or real-place inputs were opened, and no history code was modified.

## Evidence

Evidence below records the final outputs and the initial fixture validation failure. The earlier provisional report is replaced. No commit or push was attempted; the integrator commits this working tree.

### Final builds and renders

Executed commands, each exit **0**:

```powershell
python -B docs/codex/spikes/12-room-to-landscape/build.py --blender 'C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe'
python -B docs/codex/spikes/12-room-to-landscape/build.py --blender 'C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe' --route kind
python -B docs/codex/spikes/12-room-to-landscape/build.py --blender 'C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe' --route volume
```

Plain/hybrid final renders came from the full invocation; kind/volume were rerendered after the final colour-accent change. Raw relevant output from those artifact-producing invocations follows. Render byte counts precede metadata stripping. Full Blender stdout/stderr: [plain](../spikes/12-room-to-landscape/plain.log), [kind](../spikes/12-room-to-landscape/kind.log), [volume](../spikes/12-room-to-landscape/volume.log), [hybrid](../spikes/12-room-to-landscape/hybrid.log).

```text
FIXTURE 5.5 x 2.6 x 6 m; seven source boxes; window and open door; no site metadata
OK: 1 item(s) checked, 0 problem(s)
CONTRACT_EXIT 0
SPIKE_RENDER route=plain view=overview engine=CYCLES device=CPU denoise=OIDN_CPU pixels=960x540 samples=48 threads=4 seconds=15.130 bytes=374461
SPIKE_BOUNDS route=plain objects=7 max_error_m=0.000000200
BLENDER_EXIT route=plain code=0
SPIKE_RENDER route=kind view=overview engine=CYCLES device=CPU denoise=OIDN_CPU pixels=960x540 samples=48 threads=4 seconds=17.360 bytes=375895
SPIKE_RENDER route=kind view=low engine=CYCLES device=CPU denoise=OIDN_CPU pixels=960x540 samples=48 threads=4 seconds=48.613 bytes=389199
SPIKE_BOUNDS route=kind objects=7 max_error_m=0.000000200
BLENDER_EXIT route=kind code=0
SPIKE_UNION vertices=14178 triangles=28342
SPIKE_RENDER route=volume view=overview engine=CYCLES device=CPU denoise=OIDN_CPU pixels=960x540 samples=48 threads=4 seconds=18.108 bytes=375261
SPIKE_RENDER route=volume view=low engine=CYCLES device=CPU denoise=OIDN_CPU pixels=960x540 samples=48 threads=4 seconds=46.782 bytes=372994
SPIKE_BOUNDS route=volume objects=7 max_error_m=0.000000200
BLENDER_EXIT route=volume code=0
SPIKE_UNION vertices=14065 triangles=28110
SPIKE_RENDER route=hybrid view=overview engine=CYCLES device=CPU denoise=OIDN_CPU pixels=960x540 samples=48 threads=4 seconds=19.004 bytes=374821
SPIKE_RENDER route=hybrid view=low engine=CYCLES device=CPU denoise=OIDN_CPU pixels=960x540 samples=48 threads=4 seconds=51.665 bytes=383568
SPIKE_BOUNDS route=hybrid objects=7 max_error_m=0.000000200
BLENDER_EXIT route=hybrid code=0
```

Actual trusted child invocation from the final volume run:

```text
COMMAND C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe --background --factory-startup --threads 4 --python-exit-code 1 --python C:\dev\EnFractal-codex\12-room-to-landscape\docs\codex\spikes\12-room-to-landscape\render_worker.py -- --route volume --scratch C:\Users\blues\AppData\Local\Temp\enfractal-landscape-1e1cb3ed2530441da9bb025b419367e0
```

### Metadata cleanup and final comparison

The first verification invocation (`python -B docs/codex/spikes/12-room-to-landscape/verify.py`, exit **1**) reported:

```text
AssertionError: Unexpected image metadata
```

The builder now creates fresh PNGs from the same rendered RGB bytes, discarding Blender's metadata without changing pixels. A subsequent verifier invocation also exited **1** because its new image-integrity check used Pillow after `getexif()` had loaded the image:

```text
RuntimeError: verify must be called directly after open
```

Corrected the check to verify a freshly opened image, then separately inspect its dimensions/EXIF. No assertion was removed or weakened. Finalization command, exit **0**:

```powershell
python -B -c "import runpy; builder=runpy.run_path('docs/codex/spikes/12-room-to-landscape/build.py'); builder['finalise_images'](); builder['sheet']()"
```

Raw output:

```text
FINAL_IMAGE plain_overview.png metadata=none bytes=370970
FINAL_IMAGE kind_overview.png metadata=none bytes=372063
FINAL_IMAGE kind_low.png metadata=none bytes=383191
FINAL_IMAGE volume_overview.png metadata=none bytes=371642
FINAL_IMAGE volume_low.png metadata=none bytes=367595
FINAL_IMAGE hybrid_overview.png metadata=none bytes=370959
FINAL_IMAGE hybrid_low.png metadata=none bytes=377673
SHEET 1440x1190 bytes=248407
```

### Final delivery verification

Command, exit **0**:

```powershell
python -B docs/codex/spikes/12-room-to-landscape/verify.py
```

Raw output:

```text
IMAGE plain_overview.png 960x540 bytes=370970 PASS
BOUNDS plain seven objects max_error_m=0.000000200 PASS
IMAGE kind_overview.png 960x540 bytes=372063 PASS
IMAGE kind_low.png 960x540 bytes=383191 PASS
BOUNDS kind seven objects max_error_m=0.000000200 PASS
UNION_AUDIT volume nonmanifold_edges=0 degenerate_triangles=0
IMAGE volume_overview.png 960x540 bytes=371642 PASS
IMAGE volume_low.png 960x540 bytes=367595 PASS
BOUNDS volume seven objects max_error_m=0.000000200 PASS
UNION_AUDIT hybrid nonmanifold_edges=0 degenerate_triangles=13
IMAGE hybrid_overview.png 960x540 bytes=370959 PASS
IMAGE hybrid_low.png 960x540 bytes=377673 PASS
BOUNDS hybrid seven objects max_error_m=0.000000200 PASS
IMAGE sheet.png 1440x1190 bytes=248407 PASS
SCOPE working tree 30 file(s), all within brief globs PASS
VERIFY PASS: bounds, inherited layout, CPU receipts, seven PNGs, sheet, LF, binary caps, no GLB, scope
```

This checks uncommitted tracked changes and untracked files, since no commit is allowed. `tools/codex/check_scope.py` checks committed refs; it was not substituted for this working-tree check. Union diagnostics are reported, not treated as collision certification.

Independent room validation command, exit **0**:

```powershell
python -B contracts/validate.py --room docs/codex/spikes/12-room-to-landscape/room
```

```text
OK: 1 item(s) checked, 0 problem(s)
```

### Per-object bounds and slope evidence

Each number below is the largest absolute difference among all six world-space AABB coordinates, in metres. Full signed deltas and positions are in [kind](../spikes/12-room-to-landscape/kind.receipt.json), [volume](../spikes/12-room-to-landscape/volume.receipt.json) and [hybrid](../spikes/12-room-to-landscape/hybrid.receipt.json) receipts. The maximum is approximately 0.0002 mm, from float rounding. Union slope fractions include the large floor, so they must not be read as a landmark accessibility score.

Exact extraction command, exit **0**:

```powershell
python -B -c "import json;from pathlib import Path;p=Path('docs/codex/spikes/12-room-to-landscape');rs={k:json.loads((p/(k+'.receipt.json')).read_text()) for k in ['kind','volume','hybrid']};print('BOUNDS_DELTA_M object kind volume hybrid');[(print(o['id'],*[format(next(t for t in rs[k]['objects'] if t['id']==o['id'])['max_error_m'],'.9f') for k in rs])) for o in rs['kind']['objects']];[(print('SLOPE_AREA_PERCENT',k,*[round(rs[k]['union']['slopes'][s]*100,1) for s in ['walk_candidate_fraction','climb_candidate_fraction','too_steep_fraction']])) for k in ['volume','hybrid']];print('FINAL_RENDER_WALL_SECONDS',round(sum(v['seconds'] for q in p.glob('*.receipt.json') for v in json.loads(q.read_text())['renders']),3))"
```

Raw output (slope columns: walk candidate, climb candidate, too steep):

```text
BOUNDS_DELTA_M object kind volume hybrid
couch 0.000000200 0.000000200 0.000000200
cardboard_box 0.000000024 0.000000024 0.000000024
gaming_laptop 0.000000016 0.000000016 0.000000016
shelves 0.000000095 0.000000095 0.000000095
jam_jar 0.000000151 0.000000151 0.000000151
french_press 0.000000114 0.000000114 0.000000114
rug 0.000000095 0.000000095 0.000000095
SLOPE_AREA_PERCENT volume 72.6 1.3 26.1
SLOPE_AREA_PERCENT hybrid 79.3 2.1 18.6
FINAL_RENDER_WALL_SECONDS 216.661
```

Final seven renders took 216.661 seconds of measured render wall time; earlier iterations and geometry processing add time. Total CPU time across iterations is **unverified**, not claimed from that sum.

### Initial fixture validation, corrected

Command (exit **1**, before any Blender launch):

```powershell
python -B docs/codex/spikes/12-room-to-landscape/build.py --blender 'C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe'
```

Raw relevant output:

```text
FAIL room/room.json: room_id 'synthetic_garage_landscape' must match its directory name 'room'
FAIL room/room.json: shell:floor must face upward (its winding points its normal down or sideways)
FAIL room/room.json: shell:ceiling must face downward into the room
FAIL room/room.json: shell:south winding faces away from the room interior
FAIL room/room.json: shell:west_sill winding faces away from the room interior
FAIL room/room.json: shell:west_head winding faces away from the room interior
FAIL room/room.json: shell:west_front winding faces away from the room interior
FAIL room/room.json: shell:west_back winding faces away from the room interior
FAILED: 1 item(s) checked, 8 problem(s)
CONTRACT_EXIT 1
```

Fixed only the new fixture builder's room ID and winding; validator and tests stayed unchanged. Separately, visual review found Boolean material indices inheriting the floor palette. The final union explicitly transfers materials, and verification checks the used source colours. Earlier renders/logs were replaced by the final outputs.

### Historical reads

Exact commands (composite shell exit **0**, native Git results below):

```powershell
git log origin/geography-era-final -4 --oneline; Write-Output "git_exit=$LASTEXITCODE"
git show origin/geography-era-final:game/scripts/terrain_mesh_job.gd | Select-String 'source-derived|normals\[vertex_index\]|colors\[vertex_index\]|stitched ring' | ForEach-Object { $_.Line }; Write-Output "git_exit=$LASTEXITCODE"
git show origin/geography-era-final:game/shaders/painterly_ground.gdshader | Select-String 'stable_xz =|float stone =|ROUGHNESS =' | ForEach-Object { $_.Line }; Write-Output "git_exit=$LASTEXITCODE"
git show origin/geography-era-final:docs/engine/phase1/painterly-render-audit.md | Select-Object -First 3; Write-Output "git_exit=$LASTEXITCODE"
```

Raw output:

```text
9604b29 Record the single-player invariants that keep multiplayer possible
3664bea Redirect EnFractal to a photographed room with a 10 cm player and AI avatar
5e2c73b Merge pull request #3 from YourLifewithAI/codex/pfluger-photo-audit
14480c2 Catalog Pfluger photo references for S1 art pass
git_exit=0
			normals[vertex_index] = Vector3(-dx, 1.0, -dz).normalized()
			# Store source-derived measures; the style recipe chooses their colors.
			colors[vertex_index] = Color(relief, steepness, 0.0, 1.0)
				continue # A narrow stitched ring replaces the coarse outer cells below.
git_exit=0
	vec2 stable_xz = map_position.xz + geographic_origin_xz;
	float stone = max(source_weights.r * photo_strength, smoothstep(0.20, 0.56, 1.0 - world_normal.y));
	ROUGHNESS = 0.94;
git_exit=0
# Painterly renderer audit — 2026-10-02

This research audit found a real lighting defect as well as missing artwork. It does **not** certify the current scene, complete the art milestone, or demonstrate the intended painterly style. Production rendering code was not changed. See the [pipeline diagnosis](../../roadmap/research/13-painterly-pipeline-diagnosis.md) for the broader art workflow.
git_exit=-1
```

The final three-line audit excerpt reported native exit **-1**; cause **unverified**, no retry. An earlier read of that same document through `Select-Object -First 100` returned `git_exit=0` and supplied its winding/calibration discussion. The terrain and shader reads succeeded; no look-back input was needed from photos or captures.

CPU configuration is evidenced by runtime assertions/receipts and the read-only [recipe renderer](../../../pipeline/recipes/geometry.py). Online Blender [operator docs](https://docs.blender.org/api/current/bpy.ops.object.html) and [sampling docs](https://docs.blender.org/manual/en/latest/render/cycles/render_settings/sampling.html) could not be independently retrieved: **unverified online**, both fetches returned `(402) Payment Required`. No payment or installation was attempted.

## Delivery and unfinished verification

Written only within the two brief globs: this report, and the spike's source scripts, README, synthetic room/asset JSON, inventory, receipts, four Blender logs, seven renders and comparison sheet. No proposed out-of-scope diffs. Money: **$0**. GPU time: **0**. Blender used only background CPU rendering; no game or Blender window opened, no installs or paid APIs, no capture/Drive access, and no sandbox denial.

The requested synthetic experiment and historical code review are delivered. The hybrid's 13 near-zero-area triangles remain unresolved. Production landscape conversion, Godot appearance/collision, connected walkability, runtime performance, physical erosion and transformations of the committed test room remain **unverified** and were not implemented. Native engine/room runners were not run for this author-only spike; only its contract, donor geometry, render and delivery checks are claimed. Only the founder can judge the landscape/look direction.
