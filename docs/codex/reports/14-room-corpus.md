# Brief 14: synthetic room corpus

## Continuation: acceptance verified

**All 10 corpus tests pass; the contract CLI checks all 24 rooms.** Updated
`preview.py`, tests, README, generated fixtures and this report, within scope.
Lane C's interpreter ran read-only with `-B` from this checkout. No commit/push,
installation, environment write or sandbox denial.

The eight layouts retain their coverage: garage (clutter, neighbour group and five
recipe kinds), bedroom (bed envelope/underside oracle), kitchen (counter supports
and appliance fallback), living room (seating islands/shelf void), office
(chair-desk-bookcase/stacked boxes), workshop (41 objects/36 small vessels),
near-empty (two objects), awkward L (concavity/re-entrant corner beyond C3's
rectangular fitter). Each has nominal observations and seeds 17/73.

The six small-object reader failures are the six tolerance failures: C4 rounds
centre, base and height independently to millimetres. Their joint error budget
is 1.25 mm; corrected the test's 1 mm limit and allowed 1e-12 m for float arithmetic.
The shelf tuple describes an `(x,z)` corner touching the east wall; its height
is 2.02 m in a 2.7 m room. Corrected the outline test to include wall contact,
with explicit rejection checks outside walls and in the L's missing quadrant.

PNG bytes differed between Python 3.14's zlib-ng and Python 3.12's zlib. The
generator now emits canonical DEFLATE tokens after PNG Sub filtering. New tests
verify decoding; exact byte/hash checks compare fresh runs and fixtures regenerated
with Python 3.14 against Lane C's Python 3.12. Inventories/dimensions needed no
changes.

Size sources retained: [MICKE](https://www.ikea.com/gb/en/p/micke-desk-white-80213074/),
[BILLY](https://www.ikea.com/gb/en/p/billy-bookcase-white-00263850/),
[KIVIK](https://www.ikea.com/es/en/p/kivik-3-seat-sofa-frame-00519361/),
[MALM](https://www.ikea.com/be/en/p/malm-bed-frame-high-white-stained-oak-veneer-s19022549/),
[NKBA pp. 4/6](https://nkba-ps.com/images/downloads/Awards/nkba_kitchen_planning_guidelines_pre_2023.pdf).
Other values are assumed. Real scans must establish occlusion, segmentation,
depth/scale errors and mixed lighting; boxes cannot certify cavities, collision,
fun or recognisability. **Unfinished: none within corpus acceptance.** Native
runners were not run; game behavior remains unverified. No shared-file requests
or founder decisions. Cost $0; GPU time 0.

Implementation references: C4
[`entry_for`](../../../pipeline/roomscan/src/roomscan/inventory/build.py)
rounds the three fields independently; the correct combined bound is
0.0005 + 0.0005 + 0.00025 = 0.00125 m. The encoder follows
[RFC 1951, sections 3.2.5-3.2.6](https://www.rfc-editor.org/rfc/rfc1951#section-3.2.5)
and the [PNG Sub filter](https://www.w3.org/TR/png-3/#9Filter-type-1-Sub).
It emits literals and distance-1 zero runs; zlib supplies only checksums.

## Continuation evidence

The founder supplied the integrator's failing-before run: 8 tests, 88 s,
`FAILED (failures=8)`. That run was not executed here. Read-only diagnosis before
fixes, exit **0**:

```powershell
@'
import json, sys, zlib
from pathlib import Path
from pipeline.landscape.corpus import generate as gen
from pipeline.landscape.corpus.fixtures import layouts
from pipeline.landscape.corpus.tests.test_corpus import inside_outline
from roomscan.inventory.overlay import entry_box
print('Interpreter:', sys.version.split()[0], 'zlib:', zlib.ZLIB_VERSION, zlib.ZLIB_RUNTIME_VERSION)
for folder in sorted(gen.OUTPUT.iterdir()):
    if not folder.is_dir():
        continue
    inv = json.loads((folder/'inventory.json').read_bytes())
    for o in inv['objects']:
        box = entry_box(o)
        gap = abs(box.centre_m[1]-(box.base_y+box.size_m[1]/2))
        if gap > .001:
            print('CENTRE_GAP', folder.name, o['id'], repr(gap), 'centre/base/height', box.centre_m[1], box.base_y, box.size_m[1])
for layout in layouts():
    for o in layout['objects']:
        for x,z in gen.corners(o['position_m'],o['size_m'],o['yaw_deg']):
            if not inside_outline(x,z,layout['outline_xz_m']):
                print('OUTLINE', layout['id'],o['id'],x,z,'height/top',o['size_m'][1],o['position_m'][1]+o['size_m'][1])
png = gen.OUTPUT/'garage_nominal'/'preview.png'
data = png.read_bytes()
length = int.from_bytes(data[33:37],'big')
stream = data[41:41+length]
rows = zlib.decompress(stream)
print('PNG current recompression matches stored:', zlib.compress(rows,9)==stream)
print('PNG recompression preserves decoded rows:', zlib.decompress(zlib.compress(rows,9))==rows)
'@ | & 'C:\dev\EnFractal-run2\capture\pipeline\roomscan\.venv\Scripts\python.exe' -B -
```

```text
Interpreter: 3.12.15 zlib: 1.3.2 1.3.2
CENTRE_GAP home_office_scan_73 mug_1 0.001000000000000112 centre/base/height 0.815 0.764 0.104
CENTRE_GAP kitchen_scan_17 jar_1 0.0010000000000000009 centre/base/height 0.972 0.901 0.144
CENTRE_GAP workshop_scan_17 bottle_16 0.0010000000000000009 centre/base/height 0.964 0.826 0.278
CENTRE_GAP workshop_scan_17 bottle_12 0.0010000000000000009 centre/base/height 0.947 0.826 0.244
CENTRE_GAP workshop_scan_73 jar_14 0.0010000000000000009 centre/base/height 0.91 0.849 0.124
CENTRE_GAP workshop_scan_73 paint_can_5 0.0010000000000000009 centre/base/height 0.945 0.849 0.194
OUTLINE living_room shelving_unit_1 2.75 2.57 height/top 2.02 2.02
OUTLINE living_room shelving_unit_1 2.75 2.85 height/top 2.02 2.02
PNG current recompression matches stored: False
PNG recompression preserves decoded rows: True
```

Original interpreter identity, exit **0**:

```text
python -B -c "import sys,zlib; print('Original generator interpreter:',sys.version.split()[0],'zlib:',zlib.ZLIB_VERSION,zlib.ZLIB_RUNTIME_VERSION)"
Original generator interpreter: 3.14.2 zlib: 1.3.1.zlib-ng 1.3.1.zlib-ng
```

Focused encoder verification, exit **0**:

```text
& 'C:\dev\EnFractal-run2\capture\pipeline\roomscan\.venv\Scripts\python.exe' -B -m unittest pipeline.landscape.corpus.tests.test_preview -v
test_deflate_roundtrips_literals_and_every_match_length (pipeline.landscape.corpus.tests.test_preview.PreviewTests.test_deflate_roundtrips_literals_and_every_match_length) ... ok
test_png_sub_filter_preserves_rgb_bytes_across_row_boundaries (pipeline.landscape.corpus.tests.test_preview.PreviewTests.test_png_sub_filter_preserves_rgb_bytes_across_row_boundaries) ... ok

----------------------------------------------------------------------
Ran 2 tests in 0.173s

OK
```

Final regeneration, exit **0**:

```text
python -B -m pipeline.landscape.corpus.generate
Generated 24 rooms: 8 layouts x (nominal, scan_17, scan_73)
Synthetic only; standard library; GPU seconds 0; API cost $0
Output: C:\dev\EnFractal-codex\14-room-corpus\pipeline\landscape\corpus\rooms
```

Full discovery run (run once after fixes), exit **0**:

```text
& 'C:\dev\EnFractal-run2\capture\pipeline\roomscan\.venv\Scripts\python.exe' -B -m unittest discover -s pipeline/landscape/corpus/tests -t .
..........
----------------------------------------------------------------------
Ran 10 tests in 15.530s

OK
```

Contract CLI, exit **0**:

```powershell
$corpusRoomDirs = Get-ChildItem pipeline/landscape/corpus/rooms -Directory | Sort-Object Name | ForEach-Object FullName
& 'C:\dev\EnFractal-run2\capture\pipeline\roomscan\.venv\Scripts\python.exe' -B contracts/validate.py $corpusRoomDirs
```

```text
OK: 24 item(s) checked, 0 problem(s)
```

Read-only delivery audit (before updating this report), exit **0**:

```powershell
@'
import json, subprocess
from pathlib import Path
root = Path.cwd()
paths = subprocess.check_output(['git','ls-files','--others','--exclude-standard'],text=True).splitlines()
tracked = subprocess.check_output(['git','diff','--name-only'],text=True).splitlines()
allowed = lambda p: p.startswith('pipeline/landscape/') or p == 'docs/codex/reports/14-room-corpus.md'
print('Branch:',subprocess.check_output(['git','branch','--show-current'],text=True).strip())
print('Out-of-scope changed/untracked files:',[p for p in paths+tracked if not allowed(p)])
corpus = root/'pipeline'/'landscape'/'corpus'
text_files = [p for p in corpus.rglob('*') if p.is_file() and p.suffix in ('.py','.md','.json')]
print('Corpus text files with CR:',[str(p.relative_to(root)) for p in text_files if b'\r' in p.read_bytes()])
print('Corpus bytecode files:',len(list(corpus.rglob('*.pyc'))))
rooms = corpus/'rooms'
print('Inventory directories:',len(json.loads((rooms/'index.json').read_bytes())['rooms']))
previews = list(rooms.rglob('preview.png'))
print('Preview count:',len(previews))
print('Largest preview bytes:',max(p.stat().st_size for p in previews))
print('Contact sheet bytes:',(rooms/'contact-sheet.png').stat().st_size)
'@ | & 'C:\dev\EnFractal-run2\capture\pipeline\roomscan\.venv\Scripts\python.exe' -B -
```

```text
Branch: codex/14-room-corpus
Out-of-scope changed/untracked files: []
Corpus text files with CR: []
Corpus bytecode files: 0
Inventory directories: 24
Preview count: 24
Largest preview bytes: 65581
Contact sheet bytes: 305398
```
