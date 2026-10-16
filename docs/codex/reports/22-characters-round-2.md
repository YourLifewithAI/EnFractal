# Brief 22: characters, round two

Delivered all five models, interpretations, four CPU views, turntable strips
and labelled **Before / After** sheets under
[captures/characters-out/round2](../../../captures/characters-out/round2/).
No source pixels are included. The integrator commits; no commits or pushes made.

The converter now defaults volumes to 95% of their narrower outline dimension,
with fuller shoulders. This retains the drawn front contours and fills their
width through depth. Explicit thickness still supports shallow marks and props.
I learned from conv_b's fuller inflation without replacing the winning mesher.

Parts now distinguish solids and effects, with pivots and motion hints in
character.json and GLB node extras. Bubble parts carry see-through, shimmering
rim and aura-colour hints in metadata and named GLB materials; opaque fallback
colours remain usable without a shader. These are converter-local fields.

The short reading guidance now covers flat tube caps, band pinching, grouped
marks, multiple sketches, loose effects, uneven feet and transparent bodies.
I re-read all five photos, retained the approved contour landmarks, and saved
revised observations and uncertainty. Stickbear uses conv_b's requested palette;
Potato Man has richer golden skin. Cloudpuff, Potato Man and Tomato Man gain
volume rather than a different front silhouette. The Gubble uses the bottom
sketch, loses painted shimmer stripes, and gains four floating sparkles, a
flickering flame and an aura node. Tomato Man has eight independent stars.

| Character / sheet | Height m | Width m | Depth m | Triangles | Parts | Effects |
|---|---:|---:|---:|---:|---:|---:|
| [Cloudpuff](../../../captures/characters-out/round2/cloudpuff/sheet.png) | .100 | .101564 | .035297 | 26,504 | 28 | 0 |
| [Stickbear](../../../captures/characters-out/round2/stickbear/sheet.png) | .100 | .081998 | .019074 | 34,272 | 38 | 0 |
| [Gubble](../../../captures/characters-out/round2/gubble/sheet.png) | .100 | .098331 | .065057 | 16,216 | 13 | 6 |
| [Potato Man](../../../captures/characters-out/round2/potato_man/sheet.png) | .100 | .078302 | .043513 | 55,464 | 19 | 0 |
| [Tomato Man](../../../captures/characters-out/round2/tomato_man/sheet.png) | .100 | .116616 | .039333 | 29,480 | 23 | 8 |

The renderer changes only explicitly hinted materials. The final bubble preview
mixes transparency, a coloured rim and [Blender thin film](https://docs.blender.org/manual/en/latest/render/shader_nodes/shader/principled.html).
All five images from the same round-one Cloudpuff GLB match the original
renderer pixel-for-pixel and byte-for-byte after PNG metadata normalization.

[Commands and raw output](../../../pipeline/characters/conv_a/EVIDENCE_ROUND2.md):
12 tests pass, all five geometry audits and contract GLB checks pass. No unit-test failures. A final formatting check caught inherited CRLF in edited
Python files; normalized to LF. Geometry checks include determinism, fuller synthetic depth, hints,
10 cm height, ground and pivots.

Uncertainty: Gubble sparkle placement is inferred; two low Tomato stars were
repositioned to preserve ground contact. Family approval, game animation,
collision fit and the Look lane shader remain **unverified**. Native runners
were not run: this checkout lacks its native cache. No out-of-scope change
requests. About 20 minutes; $0, no installs, CPU rendering only, zero GPU time.


## Follow-up

Re-read all five TXT descriptions and the three requested drawings. The actual
Stickbear and Gubble descriptions have no `Notes:` line; their approved outputs
remain byte-identical. The three revised interpretations now apply the player's
notes to the **front outline**, superseding the contour-preservation decision
above. INTERPRET.md and FORMAT.md make this general precedence explicit while
keeping facial character, expression, features and wobble. Optional
`applied_notes` records the note and concrete change in the saved interpretation
and character.json. The converter validates and preserves that field; all shape
choices remain authored coordinates, with no character names or special cases
in converter code.

Cloudpuff has fuller unequal scallops and 0.049023 m of body depth. Potato Man
has broad, slightly lumpy golden flanks. His intact U-smile moves upward to remain
visible on the fuller surface. Tomato Man has a gently lobed body 1.248 times as
wide as tall, a small five-leaf green calyx under the hat, and eight separate
pivoted effects, all with `twinkle` hints. Surface pivots follow the new bodies;
limbs reconnect without scaling the facial strokes. Heights remain 0.10 m,
ground remains y=0, and root pivots remain [0,0,0].

Whole-character bounds (metres; checkpoint is the first brief-22 pass):

| Character / new sheet | Checkpoint W x D | New W x D | Height | Triangles | Parts / effects |
|---|---|---|---:|---:|---:|
| [Cloudpuff](../../../captures/characters-out/round2/cloudpuff/sheet.png) | .101564 x .035297 | .109689 x .050364 | .100 | 26,504 | 28 / 0 |
| [Potato Man](../../../captures/characters-out/round2/potato_man/sheet.png) | .078302 x .043513 | .099351 x .062279 | .100 | 55,848 | 19 / 0 |
| [Tomato Man](../../../captures/characters-out/round2/tomato_man/sheet.png) | .116616 x .039333 | .146842 x .054325 | .100 | 33,080 | 24 / 8 |

The exported **body mesh**, independently measured from GLB POSITION accessors:

| Character | Checkpoint body W | New body W x H x D (m) | Front width increase |
|---|---:|---|---:|
| Cloudpuff | .101564 | .109689 x .044250 x .049023 | 8.0% |
| Potato Man | .041884 | .066208 x .061090 x .058036 | 58.1% |
| Tomato Man | .040192 | .070138 x .056203 x .053393 | 74.5% |

Regenerated the three GLBs, metadata, interpretations, four CPU views, turntable
strips and Before/After sheets in `captures/characters-out/round2/`. Sheets use
round one as Before, as the original brief requests. Inspected all three sheets
and front/three-quarter views. The existing renderer frames each complete
character independently, so equal image dimensions do not imply equal on-sheet
metre scale. The renderer itself is unchanged; its prior opaque regression
remains the earlier evidence, not a newly run claim.

14 tests pass, including two new tests for note retention/replay, reshaped front
width and invalid note data. All five final geometry audits and contract GLB
checks pass. No failing tests, setup failures or sandbox denials. `rg` was
unavailable; used PowerShell/Python for reads. Native runners remain unrun because
this checkout has neither native cache directory (raw check below). Family
approval, game animation and collision fit remain **unverified**. No out-of-scope
requests, no commits/pushes (checkpoint `3b4d66b`), no paid services/installations,
$0 and zero GPU time. About 15 minutes including CPU renders.

### Commands and raw output

The converter requires an empty destination. Converted into fresh folders under
`%TEMP%/enfractal-brief22-followup`, retained the checkpoint there, then copied
only the regenerated derived files into the three authorized output folders.
The commands below are the actual subprocess commands; render logs are reduced
to their relevant raw lines. Potato Man received one visual refinement to raise
his smile; the final command/output is shown.

```text
C:\Python314\python.exe -B pipeline/characters/conv_a/convert.py --photo C:/dev/EnFractal-art/characters/round2/cloudpuff.jpg --description C:/dev/EnFractal-art/characters/round2/cloudpuff.txt --interpretation C:\Users\blues\AppData\Local\Temp\enfractal-brief22-followup\readings\cloudpuff.json --out C:\Users\blues\AppData\Local\Temp\enfractal-brief22-followup\final\cloudpuff
CHARACTER {"name": "Cloudpuff", "height_m": 0.1, "width_m": 0.10968898851497232, "depth_m": 0.05036421407997208, "triangles": 26504}
PARTS body, arch_red, arch_orange, arch_yellow, arch_green, arch_blue, arch_indigo, arch_violet, arm_screen_left, hand_screen_left, arm_screen_right, hand_screen_right, leg_screen_left, foot_screen_left, leg_screen_right, foot_screen_right, eye_screen_left, pupil_screen_left, lid_screen_left, eye_screen_right, pupil_screen_right, lid_screen_right, brow_screen_left, brow_screen_right, nose, mouth, cheek_screen_left, cheek_screen_right
GLB_SHA256 e39f8b3eb2a56536bfd6ebd39885ebe6527d3f8a4be0bf58d92db63d94af018e
TURNTABLE {"glb": "character.glb", "height_m": 0.1, "width_m": 0.1097, "depth_m": 0.0504, "pivot_offset_m": [0.0006, 0.0, -0.0007], "meshes": 28, "triangles": 26504, "views": ["front.png", "three_quarter.png", "side.png", "back.png", "turntable.png"]}
EXIT 0
C:\Python314\python.exe -B pipeline/characters/conv_a/convert.py --photo C:/dev/EnFractal-art/characters/round2/tomato_man.jpg --description C:/dev/EnFractal-art/characters/round2/tomato_man.txt --interpretation C:\Users\blues\AppData\Local\Temp\enfractal-brief22-followup\readings\tomato_man.json --out C:\Users\blues\AppData\Local\Temp\enfractal-brief22-followup\final\tomato_man
CHARACTER {"name": "Tomato Man", "height_m": 0.1, "width_m": 0.14684213213519862, "depth_m": 0.054324720932250437, "triangles": 33080}
PARTS body, calyx, hat, hat_band, eye_screen_left, eye_screen_right, mouth, arm_screen_left, hand_screen_left, wand, wand_sparkle_0, wand_sparkle_1, wand_sparkle_2, arm_screen_right, hand_screen_right, foot_screen_left, stripes_screen_left, foot_screen_right, stripes_screen_right, floating_sparkle_0, floating_sparkle_1, floating_sparkle_2, floating_sparkle_3, floating_sparkle_4
GLB_SHA256 2542fdb0459ec330ae0ffc4e542d64bb4e44d02ed7b007bef1c179f496bcaca7
TURNTABLE {"glb": "character.glb", "height_m": 0.1, "width_m": 0.1468, "depth_m": 0.0543, "pivot_offset_m": [0.0024, 0.0, -0.0005], "meshes": 24, "triangles": 33080, "views": ["front.png", "three_quarter.png", "side.png", "back.png", "turntable.png"]}
EXIT 0
C:\Python314\python.exe -B pipeline/characters/conv_a/convert.py --photo C:/dev/EnFractal-art/characters/round2/potato_man.jpg --description C:/dev/EnFractal-art/characters/round2/potato_man.txt --interpretation C:\Users\blues\AppData\Local\Temp\enfractal-brief22-followup\readings\potato_man.json --out C:\Users\blues\AppData\Local\Temp\enfractal-brief22-followup\final-refined\potato_man
CHARACTER {"name": "Potato Man", "height_m": 0.1, "width_m": 0.099350991054201, "depth_m": 0.062279424019318255, "triangles": 55848}
PARTS body, hair, hairline, hat, hat_band, eye_screen_left, eye_screen_right, nose, nostrils, cheeks, mouth, arm_screen_left, hand_screen_left, arm_screen_right, hand_screen_right, shoe_screen_left, straps_screen_left, shoe_screen_right, straps_screen_right
GLB_SHA256 aad8961031b3f3f7bfc50060edfe99f99e55475fc9f0a517edfe5a982418ddb3
TURNTABLE {"glb": "character.glb", "height_m": 0.1, "width_m": 0.0994, "depth_m": 0.0623, "pivot_offset_m": [0.0044, -0.0, -0.0021], "meshes": 19, "triangles": 55848, "views": ["front.png", "three_quarter.png", "side.png", "back.png", "turntable.png"]}
EXIT 0
```

Comparison-sheet commands, raw output:

```text
DELIVERED C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\cloudpuff
C:\Python314\python.exe -B pipeline/characters/conv_a/compare_sheet.py --before C:/dev/EnFractal-art/characters/out/conv_a/cloudpuff --after C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\cloudpuff --out C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\cloudpuff\sheet.png --title Cloudpuff
SHEET Cloudpuff: 1280x1452 -> C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\cloudpuff\sheet.png
EXIT 0
DELIVERED C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\potato_man
C:\Python314\python.exe -B pipeline/characters/conv_a/compare_sheet.py --before C:/dev/EnFractal-art/characters/out/conv_a/potato_man --after C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\potato_man --out C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\potato_man\sheet.png --title "Potato Man"
SHEET Potato Man: 1280x1452 -> C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\potato_man\sheet.png
EXIT 0
DELIVERED C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\tomato_man
C:\Python314\python.exe -B pipeline/characters/conv_a/compare_sheet.py --before C:/dev/EnFractal-art/characters/out/conv_a/tomato_man --after C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\tomato_man --out C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\tomato_man\sheet.png --title "Tomato Man"
SHEET Tomato Man: 1280x1452 -> C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\tomato_man\sheet.png
EXIT 0
```

```text
C:/Python314/python.exe -B pipeline/characters/conv_a/test_converter.py
Exit 0
Ran 14 tests in 7.903s
OK

C:/Python314/python.exe -B pipeline/characters/conv_a/audit.py captures/characters-out/round2/cloudpuff captures/characters-out/round2/stickbear captures/characters-out/round2/gubble captures/characters-out/round2/potato_man captures/characters-out/round2/tomato_man
Exit 0
AUDIT PASS Cloudpuff: height=0.100000m ground=-0.00000000m parts=28 primitives=34 triangles=26504; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
AUDIT PASS Stickbear: height=0.100000m ground=0.00000000m parts=38 primitives=43 triangles=34272; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
AUDIT PASS the Gubble: height=0.100000m ground=0.00000000m parts=13 primitives=15 triangles=16216; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
AUDIT PASS Potato Man: height=0.100000m ground=0.00000000m parts=19 primitives=61 triangles=55848; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
AUDIT PASS Tomato Man: height=0.100000m ground=-0.00000000m parts=24 primitives=34 triangles=33080; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers

Test-Path .cache/dotnet; Test-Path .cache/godot
Exit 0
False
False
```

Contracts, preserved outputs and shape requirements ran through
`C:/Python314/python.exe -B -` (PowerShell here-string piped to stdin), exit 0:

```python
from pathlib import Path
from contracts.validate import check_glb
import json,hashlib,os,sys
import numpy as np
sys.path.insert(0,str(Path('pipeline/characters/conv_a').resolve()))
from audit import read_glb,accessor
root=Path('captures/characters-out/round2')
for p in sorted(root.glob('*/character.glb')):
 errors=check_glb(p.read_bytes(),p.parent.name)
 assert not errors,errors
 print('CONTRACT GLB PASS',p.parent.name)
r=Path(os.environ['TEMP'])/'enfractal-brief22-followup'
for p,h in json.loads((r/'kept-hashes.json').read_bytes()).items():assert hashlib.sha256(Path(p).read_bytes()).hexdigest()==h,p
print('UNCHANGED: all 18 Stickbear and Gubble files match checkpoint SHA256')
for name in ('cloudpuff','potato_man','tomato_man'):
 p=root/name;meta=json.loads((p/'character.json').read_bytes());spec=json.loads((p/'interpretation.json').read_bytes())
 assert meta['applied_notes']==spec['applied_notes'] and spec['applied_notes']
 assert meta['provenance']['description_sha256']==hashlib.sha256((Path('C:/dev/EnFractal-art/characters/round2')/(name+'.txt')).read_bytes()).hexdigest()
 def body_size(folder):
  doc,b=read_glb(folder/'character.glb');node=next(n for n in doc['nodes'] if n['name']=='body')
  v=np.concatenate([accessor(doc,b,pr['attributes']['POSITION']) for pr in doc['meshes'][node['mesh']]['primitives']]);return np.ptp(v,axis=0)
 before=body_size(r/'checkpoint'/name);after=body_size(p)
 print(f'BODY {name}: W/H/D={after[0]:.6f}/{after[1]:.6f}/{after[2]:.6f} m; front width growth={(after[0]/before[0]-1)*100:.1f}%')
 if name=='cloudpuff':assert after[2]>=0.046
 else:assert after[0]>before[0]*1.4
 if name=='tomato_man':
  assert after[0]>after[1]*1.2
  assert any(p['name']=='calyx' for p in meta['parts'])
  effects=[p for p in meta['parts'] if p['kind']=='effect'];assert len(effects)==8
  assert all(p['motion_hint']['kind']=='twinkle' for p in effects)
print('FOLLOWUP CHECK PASS: applied notes/provenance, body widths, cloud depth, tomato proportions/calyx/effects')
```

Raw output:

```text
CONTRACT GLB PASS cloudpuff
CONTRACT GLB PASS gubble
CONTRACT GLB PASS potato_man
CONTRACT GLB PASS stickbear
CONTRACT GLB PASS tomato_man
UNCHANGED: all 18 Stickbear and Gubble files match checkpoint SHA256
BODY cloudpuff: W/H/D=0.109689/0.044250/0.049023 m; front width growth=8.0%
BODY potato_man: W/H/D=0.066208/0.061090/0.058036 m; front width growth=58.1%
BODY tomato_man: W/H/D=0.070138/0.056203/0.053393 m; front width growth=74.5%
FOLLOWUP CHECK PASS: applied notes/provenance, body widths, cloud depth, tomato proportions/calyx/effects
```

Final workspace check ran via `C:/Python314/python.exe -B -`, exit 0:

```python
from pathlib import Path
import subprocess,re,fnmatch
brief=Path('docs/codex/briefs/22-characters-round-2.md').read_text(encoding='utf-8')
globs=[p.strip() for p in re.search(r'```scope\n(.*?)```',brief,re.S).group(1).splitlines() if p.strip()]
changed=subprocess.check_output(['git','diff','--name-only'],text=True).splitlines()
new=subprocess.check_output(['git','ls-files','--others','--exclude-standard'],text=True).splitlines()
paths=sorted(set(changed+new))
for path in paths:
 assert any(fnmatch.fnmatch(path,g) for g in globs),path
 raw=Path(path).read_bytes();raw.decode('utf-8')
 Path(path).write_bytes(raw.replace(b'\r\n',b'\n'))
 assert b'\r' not in Path(path).read_bytes(),path
print('WORKTREE SCOPE PASS:',len(paths),'changed/new files match the brief scope; UTF-8 LF')
for path in paths:print(path)
assert not subprocess.check_output(['git','ls-files','captures/characters-out/round2'],text=True)
files=list(Path('captures/characters-out/round2').glob('*/*'))
assert len(files)==45
print('OUTPUTS UNTRACKED: 45 derived files in captures/characters-out/round2')
```

Raw relevant output:

```text
WORKTREE SCOPE PASS: 6 changed/new files match the brief scope; UTF-8 LF
docs/codex/reports/22-characters-round-2.md
pipeline/characters/conv_a/FORMAT.md
pipeline/characters/conv_a/INTERPRET.md
pipeline/characters/conv_a/README.md
pipeline/characters/conv_a/convert.py
pipeline/characters/conv_a/test_converter.py
OUTPUTS UNTRACKED: 45 derived files in captures/characters-out/round2
```

`git diff --check`: exit 0, no whitespace errors. Git emitted its existing
checkout line-ending warning for the six edited files; example raw line:

```text
warning: in the working copy of 'pipeline/characters/conv_a/FORMAT.md', LF will be replaced by CRLF the next time Git touches it
```

Files currently on disk were verified UTF-8/LF above.
