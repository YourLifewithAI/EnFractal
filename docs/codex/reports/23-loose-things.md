# Brief 23 — stopped on permission denial

Implemented closed-piece woodpile splitting, movable small stones/crates, a
64-entity loose cap, tests and per-room evidence. No commits, installs, paid calls,
GPU use or real-place data. Time: approximately 17 minutes (13:52–14:09 UTC).
**Full corpus verification is unfinished:** Windows denied the process query
below while I investigated speeding up its sequential CPU run. Following the
brief's stop rule, I interrupted only my corpus session and wrote this report.

Logs retain their drawn triangles, scale, yaw and tint; extraction joins exact
geometric edges across primitives/material seams, without assuming five logs.
Each garage log is 0.028939 kg. Lower collision boxes end at the next overlapping
log's bottom; ground-contact bottoms use the exact highest clipped terrain
triangle point. Analytic sloped-ground tests and corpus separating-axis checks
verify contact and no box overlap. Game stability remains **unverified**.

**Review the collision approximation:** the current contract's box uses
`dimensions_m`, so that field describes the inset gameplay box. Mesh envelopes
can protrude beyond it, preserving the original stack and ground sink. Actual
visual bounds are recorded separately. Manual stacking needs particular scrutiny.

The 64 cap reserves authored carryables, then whole piles, crates and stones.
Overflow remains fixed/merged with its geometry retained; authored promises over
the cap fail before writing. This bounds interactive bodies and adds at most 128
asset files; the 512-entity/2,048-listed-file limits remain. Frame timing is
**unverified**, and 64 is provisional.

Survey: sizes are unscaled prototype envelopes in centimetres; instances vary.
Masses are authoring assumptions, not measurements. Wood uses 600 kg/m³, stone
2,600 kg/m³; hollow crates scale from the generator's 120 g apple crate.
Sources: [kit](../../../pipeline/landscape/harness/kit.py),
[custom prototypes](../../../pipeline/landscape/generator/life.py),
[emission/scales](../../../pipeline/landscape/generator/generate.py).

| Kind | Size (cm) | Proposed mass | Movable / recommendation |
|---|---|---|---|
| woodpile → logs | pile 9×4.4×10; log ~2.84×2.4×10 | 0.029 kg/log | Yes; loose firewood |
| rock | 20×13×18 | 2,600 × mesh volume | Yes at ≤11 cm and ≤0.5 kg; larger stones fixed |
| boulder | 55×42×48 | same stone estimate | Only similarly small scaled instances; ordinary boulders fixed |
| crate | 16×14×14 | 0.12 × volume/(.08×.07×.07) kg | Yes at ≤10 cm and ≤0.5 kg; authored masses preserved |
| lantern | 8×15×8 | 0.1 kg, unverified | Fixed; enable only when its light follows the entity |
| fence | 60×24×8 | 0.3 kg, unverified | Fixed posts; recommend detachable rails later |
| drying_line / washing | 36×20×3 | cloth 0.005–0.02 kg/piece, unverified | Fixed; recommend separate cloth interaction, anchored posts |
| footbridge | 16×3×30, variable length | fixed 100 kg proxy | Fixed; essential footing |
| cottage / tower | 58×58×48 / 36×82×36 | fixed 100 kg proxy | Fixed structures |
| broadleaf / conifer | 65×95×65 / 48×110×48 | n/a | Rooted; fixed climbing collision |
| shrub | 32×24×32 | n/a | Rooted |
| grass_tuft / flower_clump / fern | 12×13×12 / 15×18×15 / 22×16×22 | n/a | Rooted/decorative |
| grass / wildflowers / daisies | 14×2.8×14 / 11×3×11 / 10×2.8×10 | n/a | Rooted/decorative |
| buttercups / heather / reeds | 9×2.7×9 / 13×2.2×13 / 8×7×8 | n/a | Rooted/decorative |

No Lane C change request. Spawns/destination selection and sea mapping are
unchanged. Each exported manifest's `x_landscape_loose` gives every movable ID,
mass, box pivot/size/yaw and support; logs also include visual bounds.

Integrator: complete the corpus command, load the garage, run the landscape play
check, verify idle stacks, F/V, support-busy refusals, Gubble fetch, drop/stack,
save/reload and frame timing at the cap. Godot/full engine runners were forbidden
by this brief. The newly added optional corpus sharding arguments were not run.

Working synthetic outputs remain in
`C:/Users/blues/AppData/Local/Temp/enfractal-loose-corpus-e8210005d86e441785c7a530742b231f`;
garage room: `rooms/landscape_garage_nominal`. It has 28 loose things: 10 logs,
17 stones and the original apple crate. The integrator can copy these out.

## Raw evidence

`python -B -S -m unittest discover -s pipeline/landscape/export/tests -t . -v`
Exit 0; final lines:

```text
----------------------------------------------------------------------
Ran 40 tests in 9.285s

OK
VALIDATOR exit=0 OK: 1 item(s) checked, 0 problem(s)
LOOSE_VALIDATOR exit=0 OK: 1 item(s) checked, 0 problem(s)
```

`python -B -S -m pipeline.landscape.export.tests.corpus_smoke --scratch C:/Users/blues/AppData/Local/Temp/enfractal-loose-corpus-e8210005d86e441785c7a530742b231f`
Exit 1 after I sent Ctrl+C to session 44113 following the denial. Verified output
before interruption (remaining rooms unverified):

```text
VALIDATOR awkward_l_nominal exit=0 OK: 1 item(s) checked, 0 problem(s)
CORPUS_PASS {"checked_logs": 10, "deterministic": true, "files": 80, "kinds": {"Apple crate": 1, "Log": 10, "Stone": 14}, "loose": 25, "masses_kg": {"Apple crate": [0.12, 0.12], "Log": [0.028939, 0.028939], "Stone": [0.029557, 0.206553]}, "objects": 29, "room": "awkward_l_nominal", "skipped": {}}
VALIDATOR awkward_l_scan_17 exit=0 OK: 1 item(s) checked, 0 problem(s)
CORPUS_PASS {"checked_logs": 10, "deterministic": true, "files": 72, "kinds": {"Apple crate": 1, "Crate": 1, "Log": 10, "Stone": 9}, "loose": 21, "masses_kg": {"Apple crate": [0.12, 0.12], "Crate": [0.08748, 0.08748], "Log": [0.028939, 0.028939], "Stone": [0.032341, 0.155674]}, "objects": 25, "room": "awkward_l_scan_17", "skipped": {}}
```

Garage export command (exit 0):

```powershell
python -B -S -m pipeline.landscape.export --package C:/Users/blues/AppData/Local/Temp/enfractal-loose-corpus-e8210005d86e441785c7a530742b231f/packages/garage_nominal --room pipeline/landscape/corpus/rooms/garage_nominal --room-id landscape_garage_nominal --out C:/Users/blues/AppData/Local/Temp/enfractal-loose-corpus-e8210005d86e441785c7a530742b231f/rooms/landscape_garage_nominal
```

```text
ROOM_EXPORTED {"bytes": 25996203, "files": 88, "lantern_hints": 1, "loose_cap": 64, "loose_skipped": {}, "loose_things": 28, "merged_scatter": 2629, "objects": 33, "populated_objects": 1, "room_id": "landscape_garage_nominal", "scatter_entities": 24, "scatter_solid_triangles": 2256, "scatter_visual_triangles": 175854, "scenery_triangles": 18000, "shell_parts": 21, "terrain_open_edges": 1320, "terrain_triangles": 217546, "tree_cap_triangles": 6552, "tree_climb_triangles": 7680, "tree_count": 47, "tree_pole_triangles": 1128, "water_triangles": 129682}
```

Denied command, exit 1; no retry or substitute process query:

```powershell
Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'python.exe' -and $_.CommandLine -like '*pipeline.landscape.export.tests.corpus_smoke*enfractal-loose-corpus-e8210005d86e441785c7a530742b231f*' } | Select-Object ProcessId,CommandLine
```

```text
Get-CimInstance : Access denied
At line:2 char:1
+ Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'python.ex ...
+ ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : PermissionDenied: (root\cimv2:Win32_Process:String) [Get-CimInstance], CimException
    + FullyQualifiedErrorId : HRESULT 0x80041003,Microsoft.Management.Infrastructure.CimCmdlets.GetCimInstanceCommand
```
