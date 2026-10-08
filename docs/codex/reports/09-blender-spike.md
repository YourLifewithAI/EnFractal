# Brief 09: Blender construction spike

**Result:** both synthetic objects built, exported and CPU-rendered headlessly with installed
Blender **5.2.2 LTS (d13f752e3b9c)**. Four acceptance cases passed. This supports the recipe route
for simple household shapes; fidelity to the founder's objects remains **unverified**.
**Delivery audit failed on CRLF metrics JSON; stopped at that check under the contractor rule.**

| Object | Triangles | Bounds size X/Y/Z, metres | GLB bytes | Script / process seconds | CPU preview seconds |
|---|---:|---|---:|---|---:|
| Cardboard box | 972 | 0.400000 / 0.263723 / 0.300000 | 75,176 | 7.846 / 8.644 | 5.584 |
| Empty jar | 7,308 | 0.084000 / 0.095000 / 0.084000 | 175,652 | 21.033 / 21.812 | 20.145 |

Both previews are 512 x 384 PNGs. Box panels are 4 mm thick, with 0.7 mm bevels and 4-degree
raised flaps; wall height is 0.25 m. Jar has a hollow revolved profile, twelve-sided lower body,
shoulder, geometric neck helix and separate gingham lid. The 82 mm inner lid diameter comes
from [Bonne Maman's 370 g replacement-lid specification](https://www.bonne-maman.com/products/lot-de-6-couvercles-et-etiquettes-pour-pots-de-confiture-370g).
Height, body size, thickness and remaining proportions are **assumed**, not real-object measurements.

**Authoring:** approximately one minute for box logic and two for jar logic; these are estimates,
**unverified as isolated timers**. Both were written during one approximately 3m50s authoring
window (23:58:00–00:01:50 UTC), including common helpers and the runner. Research/report time
is additional. No modelling iteration failed; the first executions passed.

**Difficulties:** exportable gingham required an embedded image; CPU glass is noisy without
denoising. The helix approximates a neck ridge, not the actual closure's lug/thread specification.
Parts overlap intentionally; per-part manifold checks do not certify their union or collision.
The API probe warned that `Material.use_nodes` is deprecated; builders use the current default.
`rg` and `Get-FileHash` were unavailable. Some documentation fetches returned HTTP 402; source
claims below use search excerpts and the successful local API probe.

**Handoff:** [scripts, previews, GLBs, metrics and recipe guidance](../spikes/09-blender/README.md).
The [results directory](../spikes/09-blender/results/) contains both binaries, each below 1 MB,
plus full Blender logs. No commit/push, shared-file diff, install, paid API, photo access or GPU
render; cost $0, GPU render time 0. No decisions are required to review this spike.

Remaining work: real measurements and founder appearance review; Godot glass/refraction,
collision and asset metadata; correcting the generator's JSON newline handling and regenerating
metrics before the delivery audit can pass. Full game runners were not run: PowerShell 7 is unavailable,
and they write `.cache/` and `game/` outside this brief's scope. Binary containment was checked
using the existing contract validator; no asset/room contract package is claimed.
Couch: moderate (cushions/seams); laptop: easy closed, moderate open; French press: moderate
to hard (handle/spout/interior). These are untested engineering judgments; details and capture
parameters/order/checks are in the guidance.

## Raw evidence

Commands ran from `C:\dev\EnFractal-codex\09-blender-spike`. Timestamps span 7–8 October UTC,
7 October local. Excerpts retain the lines relevant to the reported results; full modelling
output is in `results/box.log`, `results/jar.log`, `results/verify.log`.

Executable discovery and version (exit 0):

```powershell
Get-ChildItem 'C:\Users\blues\AppData\Local\Programs\Blender' -Recurse -Filter blender.exe | Select-Object FullName
& 'C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe' --background --factory-startup --version
```

```text
C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe
Blender 5.2.2 LTS (hash d13f752e3b9c built 2026-09-15 01:37:04)
```

Local API probe, after setting the same isolated environment as `run.ps1` (exit 0):

```powershell
& 'C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe' --background --factory-startup --python-exit-code 1 --python 'C:\Users\blues\AppData\Local\Temp\enfractal-09-blender-spike\probe.py'
```

```text
DeprecationWarning: 'Material.use_nodes' is expected to be removed in Blender 6.0
PROBE background= True version= 5.2.2 LTS
PROBE temp= C:\Users\blues\AppData\Local\Temp\enfractal-09-blender-spike\tmp
EXPORT export_yup BOOLEAN
EXPORT export_apply BOOLEAN
EXPORT export_materials ENUM
EXPORT use_selection BOOLEAN
EXPORT export_cameras BOOLEAN
EXPORT export_lights BOOLEAN
EXPORT export_image_format ENUM
EXPORT export_keep_originals BOOLEAN
EXPORT export_animations BOOLEAN
PROBE cycles= True
Blender quit
```

Box command (exit 0). The first invocation included an unnecessary execution-policy flag;
no policy rejection preceded it. Subsequent commands and the documented recipe omit that flag.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs/codex/spikes/09-blender/run.ps1 -Object box
```

```text
RUN object=box background=true config=C:\Users\blues\AppData\Local\Temp\enfractal-09-blender-spike\user\config temp=C:\Users\blues\AppData\Local\Temp\enfractal-09-blender-spike\tmp
RENDER engine=CYCLES device=CPU threads=4 samples=48 size=512x384
00:08.391  render           | Saved: 'C:\Users\blues\AppData\Local\Temp\enfractal-09-blender-spike\artifacts\cardboard_box.png'
RESULT {"triangle_count": 972, "dimensions_m_y_up": [0.4000000059604645, 0.2637231945991516, 0.30000001192092896], "glb_bytes": 75176, "export_seconds": 2.2219221999985166, "render_seconds": 5.584093999990728, "script_seconds": 7.846118500005105, "checks": "PASS: embedded resources, triangle indices, finite Y-up bounds, bottom-centre identity root"}
PROCESS object=box exit=0 wall_seconds=8.644
```

Jar command (exit 0):

```powershell
powershell -NoProfile -File docs/codex/spikes/09-blender/run.ps1 -Object jar
```

```text
RENDER engine=CYCLES device=CPU threads=4 samples=96 size=512x384
00:21.593  render           | Saved: 'C:\Users\blues\AppData\Local\Temp\enfractal-09-blender-spike\artifacts\bonne_maman_jar.png'
RESULT {"triangle_count": 7308, "dimensions_m_y_up": [0.08399999886751175, 0.0949999988079071, 0.08399999886751175], "glb_bytes": 175652, "export_seconds": 0.4791668999969261, "render_seconds": 20.145334399989224, "script_seconds": 21.033493399998406, "checks": "PASS: embedded resources, triangle indices, finite Y-up bounds, bottom-centre identity root"}
PROCESS object=jar exit=0 wall_seconds=21.812
```

Acceptance command (exit 0):

```powershell
powershell -NoProfile -File docs/codex/spikes/09-blender/run.ps1 -Object verify
```

```text
PASS cardboard_box: GLB roundtrip bounds/triangles, 9 asset meshes, 512x384 PNG
PASS bonne_maman_jar: GLB roundtrip bounds/triangles, 3 asset meshes, 512x384 PNG
PASS box parameters: 0.20x0.16x0.12 m, 4 mm thickness, closed flaps
PASS jar parameters: resized body/lid, 2.5 mm wall, 20 mm lid lift, solid geometric thread
SPIKE_ACCEPTANCE: 4/4 cases passed
PROCESS object=verify exit=0 wall_seconds=2.171
```

Contract binary checks (exit 0):

```powershell
python -B docs/codex/spikes/09-blender/check_contract_glbs.py 'C:\Users\blues\AppData\Local\Temp\enfractal-09-blender-spike\artifacts'
```

```text
PASS cardboard_box.glb: contracts/validate.py check_glb
PASS bonne_maman_jar.glb: contracts/validate.py check_glb
CONTRACT_GLBS: 2 checked, 0 problems
```

Utility errors, not Blender/setup/sandbox failures:

```powershell
rg --files docs/runs
```

```text
rg : The term 'rg' is not recognized as the name of a cmdlet, function, script file, or operable program.
```

The surrounding read batch exited 1. Discovery continued using `Get-ChildItem`/`Select-String`.
The copy batch delivered all nine artifacts before its final optional hash command failed
(batch exit 1; copied files were not retried):

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $artifactTarget 'cardboard_box.glb'),(Join-Path $artifactTarget 'bonne_maman_jar.glb') | Format-List
```

```text
DELIVERED cardboard_box.glb bytes=75176
DELIVERED cardboard_box.png bytes=289482
DELIVERED bonne_maman_jar.glb bytes=175652
DELIVERED bonne_maman_jar.png bytes=319936
DELIVERED cardboard_box.metrics.json bytes=3244
DELIVERED bonne_maman_jar.metrics.json bytes=2393
DELIVERED box.log bytes=1928
DELIVERED jar.log bytes=1464
DELIVERED verify.log bytes=3726
Get-FileHash : The term 'Get-FileHash' is not recognized as the name of a cmdlet, function, script file, or operable program.
```

The read-only delivery audit below uses Python's built-in hashlib, checks every working-tree
change against the brief's exact globs, and checks binary sizes and text line endings. This
does not replace the integrator's eventual committed-branch `tools/codex/check_scope.py` check.

```powershell
python -B docs/codex/spikes/09-blender/audit_delivery.py
```

Exit **1**, raw output:

```text
SHA256 bonne_maman_jar.glb 70673e6c7ee7d7245719f35dab634e76c46876586002c1f89325ca3b96dbd11a
Traceback (most recent call last):
  File "C:\dev\EnFractal-codex\09-blender-spike\docs\codex\spikes\09-blender\audit_delivery.py", line 22, in <module>
    assert b'\r\n' not in data, f'CRLF: {name}'
           ^^^^^^^^^^^^^^^^^^^
AssertionError: CRLF: docs/codex/spikes/09-blender/results/bonne_maman_jar.metrics.json
```

`Path.write_text` in `common.py` used the platform default newline conversion. The extra audit
requires LF throughout the delivery and caught this Windows API detail. No audit retry, test
change, hand edit of generated metrics or builder rerun followed. The contractor instruction
in [docs/codex/README.md](../README.md) says: "Stop and report it: ... a test that will not pass".

Proposed correction for the integrator, **not applied or verified**:

```diff
-    (out / (name + '.metrics.json')).write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
+    (out / (name + '.metrics.json')).write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8', newline='\n')
```

After that correction, regenerate the metrics with the recipes (do not hand-edit them), rerun
acceptance and copy the regenerated metrics to `results/`, then rerun the delivery audit.
The suggested fix is within brief scope; no shared-file change is requested.
