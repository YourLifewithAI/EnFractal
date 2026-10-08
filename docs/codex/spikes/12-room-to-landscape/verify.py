"""Delivery checks, including all uncommitted files rather than branch commits."""
import fnmatch
import json
import math
from pathlib import Path
import re
import subprocess
import sys

sys.dont_write_bytecode=True
from PIL import Image

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[3]


def main():
    inventory=json.loads((HERE/'inventory.json').read_text())['objects']
    ids={o['id'] for o in inventory}
    assert len(ids)==7
    for route in ['plain','kind','volume','hybrid']:
        receipt=json.loads((HERE/f'{route}.receipt.json').read_text())
        assert receipt['cpu_only'] and receipt['denoising']=='OPENIMAGEDENOISE_CPU'
        assert receipt['avatar_height_m']==.10
        assert {o['id'] for o in receipt['objects']}==ids
        for obj in receipt['objects']:
            assert obj['max_error_m']<1e-5
            assert obj['checked_closed_parts']==obj['parts']
            assert all(math.isfinite(v) for key in ['min_m','max_m'] for v in obj['measured'][key])
            # Every route inherits exactly the same assumed box and pose.
            source=next(o for o in inventory if o['id']==obj['id'])
            p=source['position_m']
            w,h,d=source['size_m']
            assert obj['expected']=={'min_m':[p[0]-w/2,p[1],p[2]-d/2],
                                      'max_m':[p[0]+w/2,p[1]+h,p[2]+d/2]}
        if receipt['union']:
            union=receipt['union']
            assert union['vertices']>100
            lo,hi=union['bounds_blender_m']
            assert abs(hi[2]-1.945)<1e-5, 'Highest supported object lost in union'
            assert all(abs(a-b)<1e-5 for a,b in zip(lo,[-2.75,-3,-.08]))
            assert abs(hi[0]-2.75)<1e-5 and abs(hi[1]-3)<1e-5
            palette={m['source_srgb'] or m['base_srgb'] for m in union['used_materials']}
            assert all(next(iter(o['colours'].values())) in palette for o in inventory), 'Union discarded a source palette'
            print(f'UNION_AUDIT {route} nonmanifold_edges={union["nonmanifold_edges"]} degenerate_triangles={union["degenerate_triangles"]}')
        views=['overview'] if route=='plain' else ['overview','low']
        for view in views:
            path=HERE/f'{route}_{view}.png'
            with Image.open(path) as image:
                image.verify()
            with Image.open(path) as image:
                assert image.size==(960,540)
                assert not image.getexif(), 'Unexpected image metadata'
            assert path.stat().st_size<600_000
            print(f'IMAGE {path.name} 960x540 bytes={path.stat().st_size} PASS')
        print(f'BOUNDS {route} seven objects max_error_m={max(o["max_error_m"] for o in receipt["objects"]):.9f} PASS')
    path=HERE/'sheet.png'
    with Image.open(path) as image:
        assert image.size==(1440,1190)
        image.verify()
    assert path.stat().st_size<2_000_000
    print(f'IMAGE sheet.png 1440x1190 bytes={path.stat().st_size} PASS')
    for path in HERE.rglob('*'):
        if path.is_file():
            assert path.suffix!='.glb'
            assert path.stat().st_size<2_000_000
            if path.suffix in ('.py','.json','.md','.log'):
                raw=path.read_bytes()
                raw.decode('utf-8')
                assert b'\r' not in raw, str(path)
    branch=subprocess.run(['git','branch','--show-current'],cwd=ROOT,capture_output=True,text=True,check=True).stdout.strip()
    assert branch=='codex/12-room-to-landscape'
    brief=(ROOT/'docs/codex/briefs/12-room-to-landscape.md').read_text()
    block=re.search(r'```scope\n(.*?)```',brief,re.S)
    globs=block.group(1).splitlines()
    # Read-only Git, including both changed tracked files and untracked delivery.
    changed=subprocess.run(['git','diff','--name-only','HEAD'],cwd=ROOT,capture_output=True,text=True,check=True).stdout.splitlines()
    untracked=subprocess.run(['git','ls-files','--others','--exclude-standard'],cwd=ROOT,capture_output=True,text=True,check=True).stdout.splitlines()
    paths=changed+untracked
    outside=[p for p in paths if not any(fnmatch.fnmatch(p,g) for g in globs)]
    assert not outside, outside
    print(f'SCOPE working tree {len(paths)} file(s), all within brief globs PASS')
    print('VERIFY PASS: bounds, inherited layout, CPU receipts, seven PNGs, sheet, LF, binary caps, no GLB, scope')


if __name__=='__main__':
    main()
