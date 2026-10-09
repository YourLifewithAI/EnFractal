# Brief 20 command evidence

Run from `C:\dev\EnFractal-codex\20-character-converter`, branch
`codex/20-character-converter`, 8 October 2026 local time. Raw output excerpts
below retain the relevant lines. Scratch/log root: `%TEMP%\enfractal-conv-a`.

## Delivery commands

```powershell
& 'C:\Python314\python.exe' -B pipeline/characters/conv_a/convert.py --photo 'C:\dev\EnFractal-art\characters\contest\cloudpuff.jpg' --description 'C:\dev\EnFractal-art\characters\contest\cloudpuff.txt' --interpretation "$env:TEMP\enfractal-conv-a\cloudpuff.interpretation.json" --out captures/characters-out/conv_a/cloudpuff
```

Exit **0**, stdout in `cloudpuff-final.log`:

```text
CHARACTER {"name": "Cloudpuff", "height_m": 0.1, "width_m": 0.10156387825460401, "depth_m": 0.025776938126806717, "triangles": 26504}
PARTS body, arch_red, arch_orange, arch_yellow, arch_green, arch_blue, arch_indigo, arch_violet, arm_screen_left, hand_screen_left, arm_screen_right, hand_screen_right, leg_screen_left, foot_screen_left, leg_screen_right, foot_screen_right, eye_screen_left, pupil_screen_left, lid_screen_left, eye_screen_right, pupil_screen_right, lid_screen_right, brow_screen_left, brow_screen_right, nose, mouth, cheek_screen_left, cheek_screen_right
GLB_SHA256 3daec498d724ef9ddc98ab6baa773517b4c6eb78bec04159da401d9ef3f5d117
TURNTABLE {"glb": "character.glb", "height_m": 0.1, "width_m": 0.1016, "depth_m": 0.0258, "pivot_offset_m": [0.0006, 0.0, -0.0006], "meshes": 28, "triangles": 26504, "views": ["front.png", "three_quarter.png", "side.png", "back.png", "turntable.png"]}
Blender quit
```

```powershell
& 'C:\Python314\python.exe' -B pipeline/characters/conv_a/convert.py --photo 'C:\dev\EnFractal-art\characters\contest\paw_creature.jpg' --description 'C:\dev\EnFractal-art\characters\contest\paw_creature.txt' --interpretation "$env:TEMP\enfractal-conv-a\paw_creature.interpretation.json" --out captures/characters-out/conv_a/paw_creature
```

Exit **0**, `paw_creature-final.log`:

```text
CHARACTER {"name": "Unnamed paw creature", "height_m": 0.1, "width_m": 0.08199787914297343, "depth_m": 0.012196947510818885, "triangles": 34272}
PARTS body, head, horn_screen_left, horn_screen_right, horn_bands_left, horn_bands_right, arm_screen_left, arm_screen_right, paw_screen_left, paw_screen_right, heart_screen_left, heart_screen_right, digit_pad_left_0, digit_pad_left_1, digit_pad_left_2, digit_pad_right_0, digit_pad_right_1, digit_pad_right_2, digit_pad_right_3, claw_left_0, claw_left_1, claw_left_2, claw_left_3, claw_left_4, claw_right_0, claw_right_1, claw_right_2, claw_right_3, claw_right_4, leg_screen_left, leg_screen_right, foot_screen_left, foot_screen_right, eye_screen_left, eye_glint_screen_left, eye_screen_right, eye_glint_screen_right, mouth
GLB_SHA256 56ee92df8be90816360234581e9bf8f136e47db3622ef2c794aeeffecb3729c9
TURNTABLE {"glb": "character.glb", "height_m": 0.1, "width_m": 0.082, "depth_m": 0.0122, "pivot_offset_m": [0.0061, -0.0, -0.0003], "meshes": 38, "triangles": 34272, "views": ["front.png", "three_quarter.png", "side.png", "back.png", "turntable.png"]}
Blender quit
```

The CLI called the unchanged shared turntable at 640 px, 48 samples, CPU.
These final renders preceded the metadata fix below; their delivered JSON and
GLB bytes have not changed. The saved interpretations are the actual inputs.

## Tests and geometry audit

```powershell
& 'C:\Python314\python.exe' -B pipeline/characters/conv_a/test_converter.py
```

Exit **0**:

```text
test_front_is_negative_z_and_preserves_image_handedness (__main__.ConverterTests.test_front_is_negative_z_and_preserves_image_handedness) ... ok
test_input_content_changes_provenance_without_embedding_pixels (__main__.ConverterTests.test_input_content_changes_provenance_without_embedding_pixels) ... ok
test_pivot_rotation_preserves_joint_and_carries_child (__main__.ConverterTests.test_pivot_rotation_preserves_joint_and_carries_child) ... ok
test_png_metadata_normalization_preserves_pixels (__main__.ConverterTests.test_png_metadata_normalization_preserves_pixels) ... ok
test_refuses_overwriting_output (__main__.ConverterTests.test_refuses_overwriting_output) ... ok
test_rejects_invalid_interpretation (__main__.ConverterTests.test_rejects_invalid_interpretation) ... ok
test_separate_process_byte_determinism (__main__.ConverterTests.test_separate_process_byte_determinism) ... ok
test_strict_json (__main__.ConverterTests.test_strict_json) ... ok
test_third_drawing_geometry_and_parts (__main__.ConverterTests.test_third_drawing_geometry_and_parts) ... AUDIT PASS Kite Bird: height=0.100000m ground=0.00000000m parts=11 primitives=11 triangles=9224; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
ok

----------------------------------------------------------------------
Ran 9 tests in 3.047s

OK
```

```powershell
& 'C:\Python314\python.exe' -B pipeline/characters/conv_a/audit.py captures/characters-out/conv_a/cloudpuff captures/characters-out/conv_a/paw_creature
```

Exit **0**:

```text
AUDIT PASS Cloudpuff: height=0.100000m ground=-0.00000000m parts=28 primitives=34 triangles=26504; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
AUDIT PASS Unnamed paw creature: height=0.100000m ground=0.00000000m parts=38 primitives=43 triangles=34272; closed outward meshes, unit normals, hierarchy/pivots, no images or external buffers
```

Existing contract check, executed as Python stdin
(`@' ... '@ | & 'C:\Python314\python.exe' -B -`), exit **0**:

```python
from pathlib import Path
from contracts.validate import check_glb
for name in ('cloudpuff','paw_creature'):
    path=Path('captures/characters-out/conv_a')/name/'character.glb'
    errors=check_glb(path.read_bytes(),name)
    assert not errors,errors
    print('CONTRACT GLB PASS '+name)
```

```text
CONTRACT GLB PASS cloudpuff
CONTRACT GLB PASS paw_creature
```

This is the existing GLB self-containment check, not schema validation of the
converter-local character metadata. No shared schema was changed.

## Original drawing render

The original Pillow input and its authored interpretation were created by
`synthetic.create(Path(os.environ['TEMP'])/'enfractal-conv-a/synthetic-input')`,
the same creator the tests call. The converter command was:

```powershell
& 'C:\Python314\python.exe' -B pipeline/characters/conv_a/convert.py --photo "$env:TEMP\enfractal-conv-a\synthetic-input\drawing.png" --description "$env:TEMP\enfractal-conv-a\synthetic-input\description.txt" --interpretation "$env:TEMP\enfractal-conv-a\synthetic-input\interpretation.json" --out "$env:TEMP\enfractal-conv-a\synthetic-final"
```

Exit **0**, `synthetic-final.log`:

```text
CHARACTER {"name": "Kite Bird", "height_m": 0.1, "width_m": 0.0824171392998351, "depth_m": 0.02641591944581993, "triangles": 9224}
PARTS body, eye_0, pupil_0, eye_1, pupil_1, eye_2, pupil_2, mouth, spring_leg, foot, tail
GLB_SHA256 088b32d5549aea8bdb88caa1a822c37c381348d7b455da4e8c44ca1a442ea45c
TURNTABLE {"glb": "character.glb", "height_m": 0.1, "width_m": 0.0824, "depth_m": 0.0264, "pivot_offset_m": [-0.0028, 0.0, -0.001], "meshes": 11, "triangles": 9224, "views": ["front.png", "three_quarter.png", "side.png", "back.png", "turntable.png"]}
Blender quit
```

## Failures fixed in my own work

Initial radial-fan contour validation rejected the cloud; I implemented a
general concave-polygon mesher. Previews revealed ridges, fixed with triangle
improvement and smooth inflation. The synthetic suite then exposed degenerate
normals and a zero-thickness internal chord. I fixed the geometry, preserving
the checks. Relevant output from two earlier runs of the test command (each
exit **1**), followed by the final passing run above:

```text
ValueError: degenerate mesh normals
Ran 0 tests in 1.455s
FAILED (errors=1)
```

```text
AssertionError: closed mesh: body
Ran 8 tests in 4.583s
FAILED (failures=1)
```

The first full replay used the program below with `replay_` instead of
`replay_final_`. Exit **1**:

```text
AssertionError: back.png
```

Read-only Pillow inspection found identical pixel arrays in all five images.
The four individual views differed in Blender's `Date`, `RenderTime` and
Cycles timing text tags; the strip already matched. The converter now removes
ancillary text/time/EXIF chunks without recompressing pixels or altering colour
chunks. Applying that final post-render stage to existing deliveries used this
Python stdin command, exit **0**:

```python
from pathlib import Path
import sys,os
sys.path.insert(0,str(Path('pipeline/characters/conv_a').resolve()))
from convert import normalize_renders
for path in [Path('captures/characters-out/conv_a/cloudpuff'),Path('captures/characters-out/conv_a/paw_creature'),Path(os.environ['TEMP'])/'enfractal-conv-a/synthetic-final']:
    normalize_renders(path)
    print('PNG METADATA NORMALIZED '+path.name)
```

```text
PNG METADATA NORMALIZED cloudpuff
PNG METADATA NORMALIZED paw_creature
PNG METADATA NORMALIZED synthetic-final
```

## Full final replay

Python stdin program invoking the final converter into fresh empty folders,
including all rendering and normalization. Exit **0**:

```python
from pathlib import Path
import os, subprocess, sys
root=Path.cwd()
scratch=Path(os.environ['TEMP'])/'enfractal-conv-a'
source=Path('C:/dev/EnFractal-art/characters/contest')
for name in ('cloudpuff','paw_creature'):
    delivered=root/'captures/characters-out/conv_a'/name
    replay=scratch/('replay_final_'+name)
    command=[sys.executable,'-B','pipeline/characters/conv_a/convert.py','--photo',str(source/(name+'.jpg')),'--description',str(source/(name+'.txt')),'--interpretation',str(delivered/'interpretation.json'),'--out',str(replay)]
    result=subprocess.run(command,capture_output=True,text=True)
    (scratch/(name+'-replay-final.log')).write_text(result.stdout+result.stderr,encoding='utf-8')
    if result.returncode:
        print(result.stdout+result.stderr)
        raise SystemExit(result.returncode)
    original=sorted(p.name for p in delivered.iterdir())
    assert original==sorted(p.name for p in replay.iterdir())
    for filename in original:
        assert (delivered/filename).read_bytes()==(replay/filename).read_bytes(),filename
    print('REPLAY PASS '+name+': '+str(len(original))+' files byte-identical, including all five PNG renders',flush=True)
```

```text
REPLAY PASS cloudpuff: 8 files byte-identical, including all five PNG renders
REPLAY PASS paw_creature: 8 files byte-identical, including all five PNG renders
```

No game runner was attempted. This brief restricts tools to Python/Blender.
`print('CHECKOUT_NATIVE_CACHE_PRESENT', (Path.cwd()/'.cache').exists())` printed
`CHECKOUT_NATIVE_CACHE_PRESENT False` (exit 0). No paid services, GPU or installs.

## Working-tree scope and encoding

Python stdin command, exit **0** (the standard branch scope checker only checks
commits, so this checks uncommitted/untracked delivery files):

```python
from pathlib import Path
import subprocess,fnmatch,re
root=Path.cwd()
brief=(root/'docs/codex/briefs/20-character-converter.md').read_text(encoding='utf-8')
patterns=re.search(r'```scope\n(.*?)```',brief,re.S).group(1).splitlines()
changed=set(subprocess.check_output(['git','diff','--name-only'],text=True).splitlines())
changed.update(subprocess.check_output(['git','ls-files','--others','--exclude-standard'],text=True).splitlines())
for path in sorted(changed):
    assert any(fnmatch.fnmatch(path,p) for p in patterns),path
    raw=(root/path).read_bytes()
    assert b'\r' not in raw and not raw.startswith(b'\xef\xbb\xbf'),path
    raw.decode('utf-8')
print('SCOPE PASS: '+str(len(changed))+' changed/untracked files, all inside brief scope; UTF-8 LF')
for name in ('cloudpuff','paw_creature'):
    path='captures/characters-out/conv_a/'+name+'/character.glb'
    subprocess.run(['git','check-ignore','-q',path],check=True)
print('OUTPUT IGNORE PASS: both delivery folders ignored by Git')
print('BRANCH '+subprocess.check_output(['git','branch','--show-current'],text=True).strip())
```

```text
SCOPE PASS: 9 changed/untracked files, all inside brief scope; UTF-8 LF
OUTPUT IGNORE PASS: both delivery folders ignored by Git
BRANCH codex/20-character-converter
```

`git diff --check`: exit **0**, no output. No commit/push was attempted.
