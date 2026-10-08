"""Rebuild the synthetic fixture, CPU-only renders and labelled comparison.

Run with system Python from any directory. No downloads or exported meshes.
Blender's scratch/configuration directories are isolated in the system temp folder.
"""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
sys.path.insert(0, str(ROOT / 'pipeline' / 'recipes'))
from specs import RECIPES


def write_json(path, value):
    os.makedirs(path.parent, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + '\n', encoding='utf-8', newline='\n')


def fixture():
    template = json.loads((ROOT / 'game/rooms/test_room/room.json').read_text())
    asset_template = json.loads((ROOT / 'game/rooms/test_room/objects/box_proxy/asset.json').read_text())
    # Positions are bottom centres in Godot coordinates. Every dimension is assumed.
    layout = [
        ('couch', [-1.05, 0, -2.36], 'floor', None),
        ('cardboard_box', [-0.35, 0, 0.20], 'floor', None),
        ('gaming_laptop', [-0.35, 0.25, 0.20], 'object', 'cardboard_box'),
        ('shelves', [2.45, 0, -0.70], 'floor', None),
        ('jam_jar', [2.60, 1.70, -0.36], 'object', 'shelves'),
        ('french_press', [2.58, 1.70, -0.88], 'object', 'shelves'),
        ('rug', [-0.60, 0, 1.15], 'floor', None),
    ]
    inventory = []
    room = {k: copy.deepcopy(v) for k, v in template.items()
            if k not in ('shell', 'objects', 'files', 'site', 'light_hints')}
    room.update(room_id='room', display_name='Synthetic garage landscape spike',
                description='Assumed 5.5 x 2.6 x 6 m garage-like room. No capture or real place.',
                created_utc='2026-10-07T00:00:00Z',
                bounds={'min_m': [-2.75, 0, -3], 'max_m': [2.75, 2.6, 3]},
                objects=[], files=[])
    room['spawns'][0]['position_m'] = [-0.55, 0, 0.53]
    room['spawns'][1]['position_m'] = [-0.80, 0, 0.65]
    for kind, position, support, target in layout:
        spec = RECIPES.get(kind)
        size = spec['example_size_m'] if spec else ([0.50, 1.70, 1.80] if kind == 'shelves' else [1.65, 0.008, 1.15])
        colours = {k: v[0] for k, v in spec['colours'].items()} if spec else {'body': '#775035' if kind == 'shelves' else '#b68c98'}
        role = list(spec['colours'].values())[0][1] if spec else ('wood' if kind == 'shelves' else 'fabric')
        asset = copy.deepcopy(asset_template)
        asset.update(asset_id=kind, display_name=kind.replace('_', ' ').title(), category=kind.replace('_', ' '),
                     category_group='other', dimensions_m=size, affordances=[],
                     materials=[{'slot': 'body', 'role': role, 'base_color': next(iter(colours.values()))}],
                     review={'status': 'draft', 'notes': 'Synthetic source box only; not a landscape approval.'})
        asset['provenance']['notes'] = 'Assumed synthetic bounds. Recipe defaults for five recognised objects; no scan.'
        rel = f'objects/{kind}/asset.json'
        path = HERE / 'room' / rel
        write_json(path, asset)
        raw = path.read_bytes()
        room['files'].append({'path': rel, 'sha256': hashlib.sha256(raw).hexdigest(), 'bytes': len(raw)})
        room['objects'].append({'id': 'obj:' + kind, 'asset': rel,
                                'transform': {'position_m': position, 'rotation': [0, 0, 0, 1]},
                                'support': {'kind': support, 'target_id': 'obj:' + target if target else 'shell:floor'}})
        inventory.append({'id': kind, 'size_m': size, 'position_m': position, 'colours': colours,
                          'recipe': {'recipe': kind, 'size_m': size, 'style': 'plain'} if spec else None})
    parts = []
    def shell(name, role, points, colour):
        if name in ('floor', 'ceiling', 'south') or name.startswith('west_'):
            points = list(reversed(points))
        parts.append({'id': 'shell:' + name, 'role': role,
                      'geometry': {'kind': 'polygon', 'points_m': points, 'thickness_m': 0.10},
                      'collides': True, 'material_role': 'concrete' if role == 'floor' else 'painted_wall',
                      'base_color': colour})
    shell('floor', 'floor', [[-2.75,0,-3],[2.75,0,-3],[2.75,0,3],[-2.75,0,3]], '#a7aa95')
    shell('ceiling', 'ceiling', [[-2.75,2.6,-3],[-2.75,2.6,3],[2.75,2.6,3],[2.75,2.6,-3]], '#ece8df')
    shell('east', 'wall', [[2.75,0,-3],[2.75,0,3],[2.75,2.6,3],[2.75,2.6,-3]], '#c4bdac')
    shell('south', 'wall', [[-2.75,0,3],[2.75,0,3],[2.75,2.6,3],[-2.75,2.6,3]], '#c4bdac')
    # West window: z -2.40..-1.20, y 1.00..2.00. North door: x 1.40..2.30, y 0..2.10.
    for name, lo, hi, bot, top in [('west_sill',-3,3,0,1), ('west_head',-3,3,2,2.6),
                                 ('west_front',-1.2,3,1,2), ('west_back',-3,-2.4,1,2)]:
        shell(name, 'wall', [[-2.75,bot,lo],[-2.75,bot,hi],[-2.75,top,hi],[-2.75,top,lo]], '#c4bdac')
    for name, lo, hi, bot, top in [('north_left',-2.75,1.4,0,2.6), ('north_right',2.3,2.75,0,2.6),
                                 ('north_head',1.4,2.3,2.1,2.6)]:
        shell(name, 'wall', [[lo,bot,-3],[hi,bot,-3],[hi,top,-3],[lo,top,-3]], '#c4bdac')
    room['shell'] = {'parts': parts, 'openings': [
        {'id':'window_west','kind':'window','host_part_id':'shell:west_sill','center_m':[-2.75,1.5,-1.8],
         'size_m':[1.2,1.0],'state':'fixed','traversable':False},
        {'id':'door_north','kind':'door','host_part_id':'shell:north_left','center_m':[1.85,1.05,-3],
         'size_m':[0.9,2.1],'state':'open','traversable':True}]}
    write_json(HERE / 'room/room.json', room)
    write_json(HERE / 'inventory.json', {'units':'metres', 'axes':'y_up_neg_z_forward',
                'assumed':True, 'objects':inventory})
    print('FIXTURE 5.5 x 2.6 x 6 m; seven source boxes; window and open door; no site metadata', flush=True)


def finalise_images(routes=('plain','kind','volume','hybrid')):
    """Strip Blender's image metadata without altering its rendered RGB pixels."""
    from PIL import Image
    for route in routes:
        path=HERE/f'{route}.receipt.json'
        receipt=json.loads(path.read_text())
        for render in receipt['renders']:
            image_path=HERE/render['path']
            with Image.open(image_path) as original:
                rgb=original.convert('RGB')
                clean=Image.frombytes('RGB',rgb.size,rgb.tobytes())
            clean.save(image_path,optimize=True,compress_level=9)
            render['final_bytes']=image_path.stat().st_size
            render['metadata_stripped']=True
            assert render['final_bytes']<600_000
            print(f'FINAL_IMAGE {image_path.name} metadata=none bytes={render["final_bytes"]}',flush=True)
        write_json(path,receipt)


def sheet():
    from PIL import Image, ImageDraw, ImageFont
    font_path = Path('C:/Windows/Fonts/segoeui.ttf')
    title = ImageFont.truetype(str(font_path), 28)
    font = ImageFont.truetype(str(font_path), 18)
    small = ImageFont.truetype(str(font_path), 15)
    canvas = Image.new('RGB', (1440, 1190), '#ece7dc')
    draw = ImageDraw.Draw(canvas)
    draw.text((22,14), 'ONE ROOM / THREE LANDSCAPES', fill='#303f47', font=title)
    draw.text((22,51), 'Same metres, positions and source palette. Orange figure = 10 cm. CPU Cycles / cutaway review.', fill='#465660', font=font)
    canvas.paste(Image.open(HERE/'plain_overview.png').convert('RGB').resize((480,270)), (22,98))
    draw.text((22,78), 'SOURCE / plain recipes', fill='#303f47', font=small)
    draw.text((535,116), '5.5 m wide x 6 m deep x 2.6 m high', fill='#303f47', font=font)
    draw.text((535,155), 'Blue couch / ochre box / dark laptop / pale vessels', fill='#303f47', font=font)
    draw.text((535,194), 'Brown shelves / dusty rose rug / warm stone shell', fill='#303f47', font=font)
    draw.text((535,240), 'Ceiling and two camera-facing walls hidden for overviews.', fill='#303f47', font=small)
    draw.text((535,267), 'Low cameras at 0.10 m; ceiling retained. No Godot play test.', fill='#303f47', font=small)
    for row, (route, label) in enumerate([('kind','01 BY KIND / authored landmarks'),
                                         ('volume','02 BY VOLUME / automatic box union'),
                                         ('hybrid','03 HYBRID / relief + landmarks')]):
        y = 392 + row*265
        draw.text((22,y), label, fill='#303f47', font=font)
        for col, view in enumerate(['overview','low']):
            x = 420 + col*496
            draw.text((x+8,y-10), 'THREE-QUARTER' if view=='overview' else '0.10 m EYE HEIGHT', fill='#303f47', font=small)
        # Images are displayed at 400x225 to avoid overlap with the next row.
        for col, view in enumerate(['overview','low']):
            x=420+col*496
            canvas.paste(Image.open(HERE/f'{route}_{view}.png').convert('RGB').resize((400,225)), (x,y+18))
    # Final sheet uses lossless palette compression, no photograph inputs.
    canvas.quantize(colors=192).save(HERE/'sheet.png', optimize=True)
    assert (HERE/'sheet.png').stat().st_size < 2_000_000
    print(f'SHEET 1440x1190 bytes={(HERE/"sheet.png").stat().st_size}', flush=True)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--blender', required=True, type=Path)
    parser.add_argument('--route', choices=['plain','kind','volume','hybrid','all'], default='all')
    args=parser.parse_args()
    fixture()
    # Fail closed on contract errors before launching Blender.
    checked=subprocess.run([sys.executable,'-B',str(ROOT/'contracts/validate.py'),'--room',str(HERE/'room')],
                           cwd=ROOT, capture_output=True, text=True)
    print(checked.stdout, end='')
    print(checked.stderr, end='')
    print(f'CONTRACT_EXIT {checked.returncode}', flush=True)
    if checked.returncode:
        raise SystemExit(checked.returncode)
    scratch=Path(os.environ.get('TEMP', os.environ.get('TMP','/tmp'))) / ('enfractal-landscape-'+uuid.uuid4().hex)
    os.makedirs(scratch)
    env=os.environ.copy()
    for key in ['BLENDER_USER_CONFIG','BLENDER_USER_SCRIPTS','BLENDER_USER_DATAFILES']:
        folder=scratch/key.lower()
        os.makedirs(folder)
        env[key]=str(folder)
    routes=['plain','kind','volume','hybrid'] if args.route=='all' else [args.route]
    try:
        for route in routes:
            command=[str(args.blender),'--background','--factory-startup','--threads','4','--python-exit-code','1',
                     '--python',str(HERE/'render_worker.py'),'--','--route',route,'--scratch',str(scratch)]
            print('COMMAND '+subprocess.list2cmdline(command), flush=True)
            run=subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True)
            log=run.stdout+'\n'+run.stderr
            (HERE/f'{route}.log').write_text(log, encoding='utf-8', newline='\n')
            for line in log.splitlines():
                if any(word in line for word in ['SPIKE_', 'Error','Traceback','Permission']):
                    print(line, flush=True)
            print(f'BLENDER_EXIT route={route} code={run.returncode}', flush=True)
            if run.returncode:
                raise SystemExit(run.returncode)
            finalise_images([route])
        if args.route=='all':
            sheet()
    finally:
        shutil.rmtree(scratch)


if __name__=='__main__':
    main()
