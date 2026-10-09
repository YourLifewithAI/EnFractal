"""A drawing becomes a character: photo + description + reading -> character.glb, character.json, turntable renders.

    python pipeline/characters/conv_b/convert.py --photo <drawing.jpg> --description <about.txt> \
        --reading <reading.json> --out <empty folder> [--no-render]

The reading is the interpretation step (READING.md): which marks are the body, the eyes, an arm, a prop, and which
colours go where. Everything after it is deterministic: this script measures the drawn shapes from the photo,
writes a build plan, and Blender turns the plan into a GLB (build_glb.py). Then the shared turntable renders it.
"""
import argparse
import hashlib
import json
import math
import os
import shutil
import subprocess
import sys
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import drawing  # noqa: E402

BLENDER = os.environ.get('BLENDER', r'C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe')
TURNTABLE = HERE.parent / 'turntable.py'
HEIGHT_M = 0.10
MIN_INK_M = 0.00032       # the thinnest line that still reads on a 10 cm character
STICKER_M = 0.0006        # how far a sticker (eye, pad, patch) stands off its part

ROLE_DEPTH = {'body': 1.0, 'head': 0.95, 'arm': 1.0, 'hand': 0.7, 'leg': 1.0, 'foot': 1.5, 'horn': 0.85,
              'ear': 0.5, 'tail': 1.0, 'wing': 0.35, 'hair': 0.8, 'hat': 0.9, 'prop': 0.8}
ROLE_FORWARD = {'foot': 0.6}   # feet drawn from the front reach forward, so the character stands on them
JOINTED = {'head', 'arm', 'hand', 'leg', 'foot', 'horn', 'ear', 'tail', 'wing', 'hat', 'hair', 'prop', 'other'}
NAMED_COLOURS = {
    'white': '#f4f6fb', 'black': '#24222a', 'grey': '#9a9ca6', 'gray': '#9a9ca6', 'brown': '#8a5a3c',
    'red': '#d2393c', 'orange': '#f08a2e', 'yellow': '#f6d33c', 'green': '#4bb05a', 'blue': '#4a83d8',
    'purple': '#8a5cc8', 'pink': '#f08bb4', 'gold': '#e2b33f', 'golden': '#e2b33f', 'silver': '#c4c8d0',
}


def parse_description(text):
    info = {'name': None, 'about': '', 'colours': ''}
    for line in text.splitlines():
        if ':' not in line:
            continue
        key, value = line.split(':', 1)
        key = key.strip().lower()
        if key == 'name':
            info['name'] = value.strip()
        elif key == 'about':
            info['about'] = value.strip()
        elif key in ('colours', 'colors', 'colour', 'color'):
            info['colours'] = value.strip()
    words = [w.strip('.,;:!').lower() for w in info['colours'].split()]
    info['palette'] = [NAMED_COLOURS[w] for w in words if w in NAMED_COLOURS]
    return info


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def piece_mask(sheet, piece, notes):
    if piece.get('blob'):
        return drawing.blob(sheet, piece, notes)
    return drawing.region(sheet, piece, notes)


def overlay(sheet, shapes, path):
    from PIL import Image
    base = np.stack([np.clip(sheet.ratio, 0, 1) * 255] * 3, axis=2)
    rng = np.random.default_rng(1)
    for (name, i), (kind, shape) in shapes.items():
        colour = rng.integers(40, 255, 3)
        if kind == 'mask':
            base[shape] = base[shape] * 0.45 + colour * 0.55
        else:
            for x, y in shape[0].astype(int):
                if 0 <= y < sheet.h and 0 <= x < sheet.w:
                    base[y, x] = (255, 0, 0)
    Image.fromarray(base.astype(np.uint8)).save(path)


def build_plan(photo, description, reading, debug_overlay=None):
    notes = []
    sheet = drawing.Sheet(photo, reading['figure_box'])
    info = parse_description(description)
    fallback = info['palette'] or ['#c9b8a6']
    parts = reading['parts']
    names = [p['name'] for p in parts]
    if len(set(names)) != len(names):
        raise SystemExit('reading: part names must be unique')
    for p in parts:
        if p.get('parent') and p['parent'] not in names:
            raise SystemExit(f"reading: {p['name']} has an unknown parent {p['parent']}")

    # 1. The shapes, in working pixels. A banded piece (a rainbow) becomes one piece per stripe.
    shapes = {}
    part_mask = {}
    expanded = []
    for p in parts:
        pieces = []
        own = np.zeros((sheet.h, sheet.w), bool)
        for i, piece in enumerate(p['pieces']):
            piece = dict(piece, name=f"{p['name']}[{i}]")
            kind = piece.get('kind', 'puff')
            if kind in ('puff', 'sticker'):
                limit = 0.45 if kind == 'puff' else 0.06
                mask = drawing.blob(sheet, piece, notes) if piece.get('blob') else                     drawing.region(sheet, piece, notes, limit)
                for other in piece.get('minus_parts', []):
                    mask &= ~part_mask.get(other, False)
                if piece.get('bands'):
                    inner = np.zeros_like(mask)
                    for seed in piece.get('minus_seeds', []):
                        inner |= drawing.region(sheet, {'seeds': [seed]}, notes)
                    solid = np.zeros_like(mask)
                    for other in piece.get('minus_parts', []):
                        solid |= part_mask.get(other, False)
                    stripes = drawing.bands(mask, len(piece['bands']), inner if inner.any() else None, solid)
                    for stripe, colour in zip(stripes, piece['bands']):
                        pieces.append(dict(piece, colour=colour, bands=None))
                        shapes[(p['name'], len(pieces) - 1)] = ('mask', stripe)
                else:
                    pieces.append(piece)
                    shapes[(p['name'], len(pieces) - 1)] = ('mask', mask)
                if kind == 'puff':
                    own |= mask
            elif kind in ('ink', 'tube'):
                pieces.append(piece)
                shapes[(p['name'], len(pieces) - 1)] = ('line', drawing.stroke(sheet, piece, notes))
            else:
                raise SystemExit(f"reading: unknown piece kind {kind}")
        part_mask[p['name']] = own
        expanded.append(dict(p, pieces=pieces))
    parts = expanded

    # Limbs, horns and props continue into whatever they grow out of, so joints are solid, not pinched.
    by_name = {p['name']: p for p in parts}
    for p in parts:
        parent = p.get('parent')
        if not parent or not part_mask[p['name']].any() or not part_mask[parent].any():
            continue
        for i, piece in enumerate(p['pieces']):
            kind, shape = shapes[(p['name'], i)]
            if kind != 'mask' or piece.get('kind', 'puff') != 'puff':
                continue
            dmax = drawing.distance(shape).max() if shape.any() else 0
            grow = int(max(2, round(dmax * 0.9)))
            extra = drawing._dilate(shape, grow) & part_mask[parent] & ~shape
            if extra.any():
                shapes[(p['name'], i)] = ('mask', shape | extra)

    if debug_overlay:
        overlay(sheet, shapes, debug_overlay)

    # 2. Scale and pivot: the whole figure is HEIGHT_M tall, the origin at the bottom centre between the feet.
    union = np.zeros((sheet.h, sheet.w), bool)
    for (name, i), (kind, shape) in shapes.items():
        if kind == 'mask':
            union |= shape
        else:
            pts, half = shape
            for x, y in pts.astype(int):
                if 0 <= y < sheet.h and 0 <= x < sheet.w:
                    union[y, x] = True
    if not union.any():
        raise SystemExit('nothing found in the drawing; check the reading')
    ys, xs = np.nonzero(union)
    top, ground = ys.min(), ys.max()
    scale = HEIGHT_M / max(1, ground - top + 1)
    feet = xs[ys >= ground - 0.03 * (ground - top)]
    cx = (feet.min() + feet.max()) / 2.0

    def world(x, y):
        return [round(-(x - cx) * scale, 7), round((ground + 0.5 - y) * scale, 7)]

    # 3. Pieces in metres.
    plan_parts = []
    for p in parts:
        role = p.get('role', 'other')
        mask = part_mask[p['name']]
        pieces = []
        for i, piece in enumerate(p['pieces']):
            kind = piece.get('kind', 'puff')
            colour = piece.get('colour') or fallback[min(len(plan_parts), len(fallback) - 1)]
            _, shape = shapes[(p['name'], i)]
            entry = {'kind': kind, 'colour': colour.lower()}
            if kind in ('puff', 'sticker'):
                balls, dmax = drawing.mask_balls(shape)
                if not len(balls):
                    notes.append(f"{p['name']}[{i}]: empty, skipped")
                    continue
                entry['balls'] = [world(x, y) + [round(r * scale, 7)] for x, y, r in balls]
                entry['dmax_m'] = round(dmax * scale, 7)
                if kind == 'puff':
                    depth = piece.get('depth', p.get('depth'))
                    if depth is None:
                        depth = ROLE_DEPTH.get(role, 0.8)
                        if role in ('body', 'head') and dmax > 0:
                            # A wide, flat body drawn from the front is deeper than its drawn height suggests.
                            ys_, xs_ = np.nonzero(shape)
                            width = xs_.max() - xs_.min() + 1
                            depth = float(min(1.8, max(depth, 0.45 * width / (2 * dmax))))
                    entry['depth'] = round(float(depth), 4)
                    entry['forward_m'] = round(float(piece.get('forward', ROLE_FORWARD.get(role, 0.0))) * dmax * scale, 7)
                else:
                    entry['thickness_m'] = float(piece.get('thickness', 1.0)) * STICKER_M
            else:
                pts, half = shape
                if not len(pts):
                    continue
                if kind == 'ink':
                    r = max(MIN_INK_M, half * scale * 1.15) * float(piece.get('weight', 1.0))
                else:
                    r = float(piece['radius']) * HEIGHT_M if piece.get('radius') else max(half * scale * 2.5, 0.0012)
                entry['balls'] = [world(x, y) + [round(r, 7)] for x, y in pts]
                entry['dmax_m'] = round(r, 7)
                if kind == 'tube':
                    entry['depth'] = 1.0
                    entry['forward_m'] = 0.0
                    entry['behind'] = float(piece.get('behind', 0.0))
            pieces.append(entry)
        # The joint: where this part meets its parent; features pivot at their own centre.
        pivot = None
        parent = p.get('parent')
        own = mask.copy()
        if not own.any():
            for i, piece in enumerate(p['pieces']):
                kind, shape = shapes[(p['name'], i)]
                if kind == 'mask':
                    own |= shape
                else:
                    for x, y in shape[0].astype(int):
                        if 0 <= y < sheet.h and 0 <= x < sheet.w:
                            own[y, x] = True
        if role in JOINTED and parent and part_mask.get(parent, np.zeros(1)).any() and own.any():
            joint = drawing._dilate(own, 3) & part_mask[parent]
            if joint.any():
                jy, jx = np.nonzero(joint)
                pivot = world(jx.mean(), jy.mean())
        if pivot is None and own.any():
            oy, ox = np.nonzero(own)
            if not parent:
                pivot = world(ox.mean(), oy.max())
            else:
                pivot = world(ox.mean(), oy.mean())
        plan_parts.append({'name': p['name'], 'role': role, 'parent': parent, 'pivot': pivot or [0.0, 0.0],
                           'pieces': pieces, 'surface': all(e['kind'] in ('sticker', 'ink') for e in pieces)})

    leg_parts = [pp for pp in plan_parts if pp['role'] in ('leg', 'foot')]
    leg_len = 0.0
    for pp in leg_parts:
        zs = [b[1] for e in pp['pieces'] for b in e['balls']]
        if zs:
            leg_len = max(leg_len, max(zs) - min(zs))
    if not leg_parts:
        heuristic = 'floats'
    elif leg_len > 0.33 * HEIGHT_M:
        heuristic = 'strides'
    elif leg_len > 0.15 * HEIGHT_M:
        heuristic = 'hops'
    else:
        heuristic = 'waddles'
    return {
        'name': reading.get('name') or info['name'] or 'Unnamed',
        'about': info['about'],
        'motion': reading.get('motion') or heuristic,
        'motion_why': reading.get('motion_why', 'from leg length' if not reading.get('motion') else ''),
        'motion_from_shape': heuristic,
        'height_m': HEIGHT_M,
        'parts': plan_parts,
        'notes': notes,
    }


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--photo', required=True)
    ap.add_argument('--description', required=True)
    ap.add_argument('--reading', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--no-render', action='store_true', help='skip the turntable (tests)')
    ap.add_argument('--samples', default='48')
    ap.add_argument('--debug-overlay', help='write a picture of what was found over the drawing (never into Git)')
    args = ap.parse_args(argv)

    out = Path(args.out)
    if out.exists() and any(out.iterdir()):
        raise SystemExit(f'{out} is not empty')
    os.makedirs(out, exist_ok=True)
    description = Path(args.description).read_text(encoding='utf-8')
    reading_text = Path(args.reading).read_text(encoding='utf-8')
    reading = json.loads(reading_text)
    shutil.copyfile(args.reading, out / 'reading.json')
    plan = build_plan(args.photo, description, reading, args.debug_overlay)
    plan['source'] = {'photo_sha256': sha256(args.photo), 'description_sha256': sha256(args.description),
                      'reading_sha256': sha256(args.reading)}
    plan_path = out / 'build_plan.json'
    plan_path.write_text(json.dumps(plan, indent=1, sort_keys=True) + '\n', encoding='utf-8', newline='\n')

    glb = out / 'character.glb'
    report = out / 'build_report.json'
    cmd = [BLENDER, '-b', '--factory-startup', '-noaudio', '-P', str(HERE / 'build_glb.py'), '--',
           '--plan', str(plan_path), '--glb', str(glb), '--report', str(report)]
    run = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace')
    if run.returncode != 0 or not glb.exists():
        sys.stderr.write(run.stdout[-4000:] + run.stderr[-4000:])
        raise SystemExit('Blender build failed')
    built = json.loads(report.read_text(encoding='utf-8'))
    report.unlink()

    character = {
        'format': 'enfractal.character/conv_b-1',
        'name': plan['name'],
        'about': plan['about'],
        'motion': plan['motion'],
        'motion_why': plan['motion_why'],
        'motion_from_shape': plan['motion_from_shape'],
        'units': 'metres; Godot axes: +Y up, facing -Z; origin at the bottom centre between the feet',
        'height_m': built['height_m'],
        'width_m': built['width_m'],
        'depth_m': built['depth_m'],
        'triangles': built['triangles'],
        'parts': built['parts'],
        'source': plan['source'],
        'notes': plan['notes'],
    }
    (out / 'character.json').write_text(json.dumps(character, indent=1, sort_keys=True) + '\n',
                                        encoding='utf-8', newline='\n')
    print(f"CONVERT {plan['name']}: {built['height_m']} m tall, {built['triangles']} triangles, "
          f"{len(built['parts'])} parts, motion {plan['motion']}")
    for n in plan['notes']:
        print('  note: ' + n)

    if not args.no_render:
        cmd = [BLENDER, '-b', '--factory-startup', '-noaudio', '-P', str(TURNTABLE), '--',
               '--glb', str(glb), '--out', str(out), '--samples', args.samples]
        run = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace')
        lines = [ln for ln in run.stdout.splitlines() if ln.startswith('TURNTABLE ')]
        if run.returncode != 0 or not lines:
            sys.stderr.write(run.stdout[-3000:] + run.stderr[-3000:])
            raise SystemExit('turntable failed')
        print(lines[-1])


if __name__ == '__main__':
    main()
