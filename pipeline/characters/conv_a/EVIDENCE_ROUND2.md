# Brief 22 command evidence

Run in `C:/dev/EnFractal-codex/22-characters-round-2` on
`codex/22-characters-round-2`, 9 October 2026. Commands below show the actual
child commands and raw relevant output. Scratch is `%TEMP%/enfractal-brief22`.
Source drawings were read in place; none were copied into this checkout.

## Converter tests

```text
C:/Python314/python.exe -B pipeline/characters/conv_a/test_converter.py
Exit 0
AUDIT PASS Kite Bird: height=0.100000m ground=0.00000000m parts=13 primitives=12 triangles=10184; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
AUDIT PASS Kite Bird: height=0.100000m ground=0.00000000m parts=11 primitives=11 triangles=9224; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
Ran 12 tests in 4.779s
OK
```

The original nine tests remain; three new tests cover fuller default depth,
independent effect/material export and replay, and malformed hints. No test
failed or was weakened during this round.

## Five saved readings and conversions

Read all five round-two JPGs directly with the image viewer, alongside their
TXT descriptions and the family direction. Reused approved contour landmarks
after checking them against the drawings; revised observations, uncertainty,
depth, colour, effects and surface pivots. The saved output interpretation is
the full authored input, not a reference to an external reading.

```text
C:\Python314\python.exe -B pipeline/characters/conv_a/convert.py --photo C:/dev/EnFractal-art/characters/round2/cloudpuff.jpg --description C:/dev/EnFractal-art/characters/round2/cloudpuff.txt --interpretation C:\Users\blues\AppData\Local\Temp\enfractal-brief22\readings\cloudpuff.json --out captures/characters-out/round2/cloudpuff --no-render
CHARACTER {"name": "Cloudpuff", "height_m": 0.1, "width_m": 0.10156387825460401, "depth_m": 0.03529688120081245, "triangles": 26504}
PARTS body, arch_red, arch_orange, arch_yellow, arch_green, arch_blue, arch_indigo, arch_violet, arm_screen_left, hand_screen_left, arm_screen_right, hand_screen_right, leg_screen_left, foot_screen_left, leg_screen_right, foot_screen_right, eye_screen_left, pupil_screen_left, lid_screen_left, eye_screen_right, pupil_screen_right, lid_screen_right, brow_screen_left, brow_screen_right, nose, mouth, cheek_screen_left, cheek_screen_right
GLB_SHA256 2e5dd3a9dd82531b2c234a6f197a890b4744ea9b8b8632310cd3eadf35a6ec1d

EXIT 0
C:\Python314\python.exe -B pipeline/characters/conv_a/convert.py --photo C:/dev/EnFractal-art/characters/round2/stickbear.jpg --description C:/dev/EnFractal-art/characters/round2/stickbear.txt --interpretation C:\Users\blues\AppData\Local\Temp\enfractal-brief22\readings\stickbear.json --out captures/characters-out/round2/stickbear --no-render
CHARACTER {"name": "Stickbear", "height_m": 0.1, "width_m": 0.08199787914297343, "depth_m": 0.01907384442290603, "triangles": 34272}
PARTS body, head, horn_screen_left, horn_screen_right, horn_bands_left, horn_bands_right, arm_screen_left, arm_screen_right, paw_screen_left, paw_screen_right, heart_screen_left, heart_screen_right, digit_pad_left_0, digit_pad_left_1, digit_pad_left_2, digit_pad_right_0, digit_pad_right_1, digit_pad_right_2, digit_pad_right_3, claw_left_0, claw_left_1, claw_left_2, claw_left_3, claw_left_4, claw_right_0, claw_right_1, claw_right_2, claw_right_3, claw_right_4, leg_screen_left, leg_screen_right, foot_screen_left, foot_screen_right, eye_screen_left, eye_glint_screen_left, eye_screen_right, eye_glint_screen_right, mouth
GLB_SHA256 fa20791624b44311e3b16e1a894e56130f5e7ac20cdac9a470860a21ddee37de

EXIT 0
C:\Python314\python.exe -B pipeline/characters/conv_a/convert.py --photo C:/dev/EnFractal-art/characters/round2/gubble.jpg --description C:/dev/EnFractal-art/characters/round2/gubble.txt --interpretation C:\Users\blues\AppData\Local\Temp\enfractal-brief22\readings\gubble.json --out captures/characters-out/round2/gubble --no-render
CHARACTER {"name": "the Gubble", "height_m": 0.1, "width_m": 0.09833090750553714, "depth_m": 0.06505713920736225, "triangles": 16216}
PARTS body, eye_screen_left, eye_screen_right, mouth, arm_screen_right, hand_screen_right, torch, torch_flame, aura, floating_sparkle_0, floating_sparkle_1, floating_sparkle_2, floating_sparkle_3
GLB_SHA256 f01a973f3583ad889aac15f11fd094ae42a4ce83c7a671e0f228221bf23d8477

EXIT 0
C:\Python314\python.exe -B pipeline/characters/conv_a/convert.py --photo C:/dev/EnFractal-art/characters/round2/potato_man.jpg --description C:/dev/EnFractal-art/characters/round2/potato_man.txt --interpretation C:\Users\blues\AppData\Local\Temp\enfractal-brief22\readings\potato_man.json --out captures/characters-out/round2/potato_man --no-render
CHARACTER {"name": "Potato Man", "height_m": 0.1, "width_m": 0.07830205227153131, "depth_m": 0.04351299550277493, "triangles": 55464}
PARTS body, hair, hairline, hat, hat_band, eye_screen_left, eye_screen_right, nose, nostrils, cheeks, mouth, arm_screen_left, hand_screen_left, arm_screen_right, hand_screen_right, shoe_screen_left, straps_screen_left, shoe_screen_right, straps_screen_right
GLB_SHA256 b79bd79b98e93d39148a7eb0f83184bbba0556c7a305f64b9f402238328312a3

EXIT 0
C:\Python314\python.exe -B pipeline/characters/conv_a/convert.py --photo C:/dev/EnFractal-art/characters/round2/tomato_man.jpg --description C:/dev/EnFractal-art/characters/round2/tomato_man.txt --interpretation C:\Users\blues\AppData\Local\Temp\enfractal-brief22\readings\tomato_man.json --out captures/characters-out/round2/tomato_man --no-render
CHARACTER {"name": "Tomato Man", "height_m": 0.1, "width_m": 0.11661646073726131, "depth_m": 0.03933290125500164, "triangles": 29480}
PARTS body, hat, hat_band, eye_screen_left, eye_screen_right, mouth, arm_screen_left, hand_screen_left, wand, wand_sparkle_0, wand_sparkle_1, wand_sparkle_2, arm_screen_right, hand_screen_right, foot_screen_left, stripes_screen_left, foot_screen_right, stripes_screen_right, floating_sparkle_0, floating_sparkle_1, floating_sparkle_2, floating_sparkle_3, floating_sparkle_4
GLB_SHA256 dd680dc4852ba6695ffb44e8f5fda743f1f2929223a3958a7524632d0da3f583

EXIT 0
```

## Final CPU renders

The normal pass used the shared defaults: 640 px, 48 samples, CPU Cycles.
The first Gubble rim was visually too white; only the hinted bubble preview
was adjusted, then Gubble and the opaque regression GLB were re-rendered.
Below the first Gubble pass is omitted in favour of the final pass.

```text
C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe -b --factory-startup -P C:\dev\EnFractal-codex\22-characters-round-2\pipeline\characters\turntable.py -- --glb C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\cloudpuff\character.glb --out C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\cloudpuff
TURNTABLE {"glb": "character.glb", "height_m": 0.1, "width_m": 0.1016, "depth_m": 0.0353, "pivot_offset_m": [0.0006, 0.0, -0.0006], "meshes": 28, "triangles": 26504, "views": ["front.png", "three_quarter.png", "side.png", "back.png", "turntable.png"]}
EXIT 0
C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe -b --factory-startup -P C:\dev\EnFractal-codex\22-characters-round-2\pipeline\characters\turntable.py -- --glb C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\stickbear\character.glb --out C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\stickbear
TURNTABLE {"glb": "character.glb", "height_m": 0.1, "width_m": 0.082, "depth_m": 0.0191, "pivot_offset_m": [0.0061, -0.0, -0.0003], "meshes": 38, "triangles": 34272, "views": ["front.png", "three_quarter.png", "side.png", "back.png", "turntable.png"]}
EXIT 0
C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe -b --factory-startup -P C:\dev\EnFractal-codex\22-characters-round-2\pipeline\characters\turntable.py -- --glb C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\potato_man\character.glb --out C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\potato_man
TURNTABLE {"glb": "character.glb", "height_m": 0.1, "width_m": 0.0783, "depth_m": 0.0435, "pivot_offset_m": [0.0044, -0.0, -0.0019], "meshes": 19, "triangles": 55464, "views": ["front.png", "three_quarter.png", "side.png", "back.png", "turntable.png"]}
EXIT 0
C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe -b --factory-startup -P C:\dev\EnFractal-codex\22-characters-round-2\pipeline\characters\turntable.py -- --glb C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\tomato_man\character.glb --out C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\tomato_man
TURNTABLE {"glb": "character.glb", "height_m": 0.1, "width_m": 0.1166, "depth_m": 0.0393, "pivot_offset_m": [0.0036, 0.0, -0.0006], "meshes": 23, "triangles": 29480, "views": ["front.png", "three_quarter.png", "side.png", "back.png", "turntable.png"]}
EXIT 0
C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe -b --factory-startup -P C:\dev\EnFractal-codex\22-characters-round-2\pipeline\characters\turntable.py -- --glb C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\gubble\character.glb --out C:\dev\EnFractal-codex\22-characters-round-2\captures\characters-out\round2\gubble
TURNTABLE {"glb": "character.glb", "height_m": 0.1, "width_m": 0.0983, "depth_m": 0.0651, "pivot_offset_m": [-0.0049, 0.0, -0.0005], "meshes": 12, "triangles": 16216, "views": ["front.png", "three_quarter.png", "side.png", "back.png", "turntable.png"]}
EXIT 0
C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe -b --factory-startup -P C:\dev\EnFractal-codex\22-characters-round-2\pipeline\characters\turntable.py -- --glb C:\dev\EnFractal-art\characters\out\conv_a\cloudpuff\character.glb --out C:\Users\blues\AppData\Local\Temp\enfractal-brief22\opaque_final
TURNTABLE {"glb": "character.glb", "height_m": 0.1, "width_m": 0.1016, "depth_m": 0.0258, "pivot_offset_m": [0.0006, 0.0, -0.0006], "meshes": 28, "triangles": 26504, "views": ["front.png", "three_quarter.png", "side.png", "back.png", "turntable.png"]}
EXIT 0
```

The converter's normalize_renders removes ancillary PNG timestamps and text,
without changing pixels. compare_sheet.sheet(before, after, after/'sheet.png',
title) was run for all five (Stickbear's before folder is paw_creature).
Its equivalent public CLI is:

```powershell
& C:/Python314/python.exe -B pipeline/characters/conv_a/compare_sheet.py --before C:/dev/EnFractal-art/characters/out/conv_a/cloudpuff --after captures/characters-out/round2/cloudpuff --out captures/characters-out/round2/cloudpuff/sheet.png --title Cloudpuff
```

Actual function-call output, in the render wrapper (exit 0):

```text
SHEET Cloudpuff: 1280x1452 -> captures\characters-out\round2\cloudpuff\sheet.png
SHEET Stickbear: 1280x1452 -> captures\characters-out\round2\stickbear\sheet.png
SHEET The Gubble: 1280x1452 -> captures\characters-out\round2\gubble\sheet.png
SHEET Potato Man: 1280x1452 -> captures\characters-out\round2\potato_man\sheet.png
SHEET Tomato Man: 1280x1452 -> captures\characters-out\round2\tomato_man\sheet.png
```

## Opaque render regression

Before editing, saved the original turntable to the scratch folder and ran:

```powershell
& 'C:/Users/blues/AppData/Local/Programs/Blender/blender-5.2.2-windows-x64/blender.exe' -b --factory-startup -P "$env:TEMP/enfractal-brief22/turntable_before.py" -- --glb C:/dev/EnFractal-art/characters/out/conv_a/cloudpuff/character.glb --out "$env:TEMP/enfractal-brief22/opaque_before"
```

Raw last lines, exit 0:

```text
TURNTABLE {"glb": "character.glb", "height_m": 0.1, "width_m": 0.1016, "depth_m": 0.0258, "pivot_offset_m": [0.0006, 0.0, -0.0006], "meshes": 28, "triangles": 26504, "views": ["front.png", "three_quarter.png", "side.png", "back.png", "turntable.png"]}
Blender 5.2.2 LTS (hash d13f752e3b9c built 2026-09-15 01:37:04)
Blender quit
```

The final renderer command is above. The comparison ran through
`C:/Python314/python.exe -B -` with this body (exit 0):

```python
from pathlib import Path
import os, sys, hashlib
import numpy as np
from PIL import Image
sys.path.insert(0,str(Path('pipeline/characters/conv_a').resolve()))
from convert import canonical_png
root=Path(os.environ['TEMP'])/'enfractal-brief22'
for name in ['front','three_quarter','side','back','turntable']:
    a=root/'opaque_before'/f'{name}.png'; b=root/'opaque_final'/f'{name}.png'
    assert np.array_equal(np.asarray(Image.open(a)),np.asarray(Image.open(b)))
    assert canonical_png(a.read_bytes())==canonical_png(b.read_bytes())
    print('OPAQUE IDENTICAL',name,hashlib.sha256(canonical_png(b.read_bytes())).hexdigest())
```

Raw output:

```text
OPAQUE IDENTICAL front a9b527e0e0d1e5a339a907bd78c9f76b818624194fb8876ad5832414729f802a
OPAQUE IDENTICAL three_quarter 9b72ecac0da684a09f7bc1d4c8f3555caee5ae1033ece6a8fe8b70aad3ede3df
OPAQUE IDENTICAL side 1681a969fbb968893295eaf09c5aa2d8c44973249f40fda1b646eb1fa2758a64
OPAQUE IDENTICAL back d243538e4c90c0480fe850f327b8c9a5d2792e35c9c1a93052752d96f6c2cbf1
OPAQUE IDENTICAL turntable 09604feee3bff64b3c15a6188e94872e08f8a1b3d54e8e316d6b07a9471e76c9
```

## Final geometry and contracts

```text
C:/Python314/python.exe -B pipeline/characters/conv_a/audit.py captures/characters-out/round2/cloudpuff captures/characters-out/round2/stickbear captures/characters-out/round2/gubble captures/characters-out/round2/potato_man captures/characters-out/round2/tomato_man
Exit 0
AUDIT PASS Cloudpuff: height=0.100000m ground=-0.00000000m parts=28 primitives=34 triangles=26504; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
AUDIT PASS Stickbear: height=0.100000m ground=0.00000000m parts=38 primitives=43 triangles=34272; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
AUDIT PASS the Gubble: height=0.100000m ground=0.00000000m parts=13 primitives=15 triangles=16216; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
AUDIT PASS Potato Man: height=0.100000m ground=0.00000000m parts=19 primitives=61 triangles=55464; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
AUDIT PASS Tomato Man: height=0.100000m ground=-0.00000000m parts=23 primitives=29 triangles=29480; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
```

`C:/Python314/python.exe -B -`, exit 0, with:

```python
from pathlib import Path
from contracts.validate import check_glb
for p in sorted(Path('captures/characters-out/round2').glob('*/character.glb')):
    errors=check_glb(p.read_bytes(),p.parent.name)
    assert not errors,errors
    print('CONTRACT GLB PASS',p.parent.name)
```

```text
CONTRACT GLB PASS cloudpuff
CONTRACT GLB PASS gubble
CONTRACT GLB PASS potato_man
CONTRACT GLB PASS stickbear
CONTRACT GLB PASS tomato_man
```

Native runners were not run: `Test-Path .cache/dotnet; Test-Path .cache/godot`
returned `False`, `False` (exit 0). No bootstrap or installation attempted.
Character JSON is a converter-local draft; no shared character schema exists.


## Final workspace check

A new formatting assertion initially failed (Python exit 1; the containing
PowerShell command ended with git diff --check, exit 0):

```text
Traceback (most recent call last):
  File "<stdin>", line 11, in <module>
AssertionError: pipeline/characters/conv_a/convert.py
```

The edited Python files retained some checkout CRLF lines. Converted CRLF to LF
inside the owned files; no assertion or existing check changed. This was not a
sandbox denial. The following final check ran via `C:/Python314/python.exe -B -`
(exit 0):

```python
from pathlib import Path
import subprocess, re, fnmatch
brief=Path('docs/codex/briefs/22-characters-round-2.md').read_text(encoding='utf-8')
globs=[p.strip() for p in re.search(r'```scope\n(.*?)```',brief,re.S).group(1).splitlines() if p.strip()]
changed=subprocess.check_output(['git','diff','--name-only'],text=True).splitlines()
new=subprocess.check_output(['git','ls-files','--others','--exclude-standard'],text=True).splitlines()
paths=sorted(set(changed+new))
for path in paths:
    assert any(fnmatch.fnmatch(path,g) for g in globs),path
    raw=Path(path).read_bytes();raw.decode('utf-8')
    assert b'\r' not in raw,path
print('WORKTREE SCOPE PASS:',len(paths),'changed/new files match the brief scope; UTF-8 LF')
for path in paths:print(path)
assert not subprocess.check_output(['git','ls-files','captures/characters-out/round2'],text=True)
assert len(list(Path('captures/characters-out/round2').glob('*/*')))==45
print('OUTPUTS UNTRACKED: 45 derived files in captures/characters-out/round2')
```

Raw relevant output:

```text
WORKTREE SCOPE PASS: 9 changed/new files match the brief scope; UTF-8 LF
docs/codex/reports/22-characters-round-2.md
pipeline/characters/conv_a/EVIDENCE_ROUND2.md
pipeline/characters/conv_a/FORMAT.md
pipeline/characters/conv_a/INTERPRET.md
pipeline/characters/conv_a/README.md
pipeline/characters/conv_a/compare_sheet.py
pipeline/characters/conv_a/convert.py
pipeline/characters/conv_a/test_converter.py
pipeline/characters/turntable.py
OUTPUTS UNTRACKED: 45 derived files in captures/characters-out/round2
```

`git diff --check`: exit 0, no whitespace errors. Git warns that its checkout
policy will replace LF with CRLF next time it touches the tracked files; the
files delivered now are UTF-8 LF. This checks uncommitted files because the
integrator will commit; it does not claim a committed-branch scope check.
