"""CPU-only corpus generation/export evidence; outputs belong in system temp.

python -B -S -m pipeline.landscape.export.tests.corpus_smoke --scratch <temp-folder>
"""
import argparse
from collections import Counter
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import uuid

from pipeline.landscape.export import export_room
from pipeline.landscape.export.loose import footprint_maximum
from pipeline.landscape.export.mesh import ground
from pipeline.landscape.export.trees import TerrainHeights
from pipeline.landscape.generator.generate import generate
from pipeline.landscape.harness.package import read_package

ROOT = Path(__file__).resolve().parents[4]


def check_log_contacts(room, package, source):
    doc, meshes, source_room, _ = read_package(package, source)
    terrain = [p for r in doc['terrain'] for p in meshes[r['mesh']]]
    heights = TerrainHeights(terrain)
    items = room['extensions']['x_landscape_loose']['items']
    by_id = {i['id']:i for i in items}
    checked = 0
    for item in items:
        if item['name'] != 'Log':
            continue
        position, size = item['position_m'], item['box_m']
        low = [-size[0]/2, 0, -size[2]/2]
        high = [size[0]/2, size[1], size[2]/2]
        floor = footprint_maximum(heights, low, high, position, item['yaw_deg'])
        assert floor is not None and position[1] >= floor-1e-8, item['id']+' enters ground'
        support = item['support']
        if support['kind'] == 'object':
            below = by_id[support['target_id']]
            assert abs(position[1]-(below['position_m'][1]+below['box_m'][1])) < 1e-8
        else:
            assert abs(position[1]-floor) < 1e-8
        # A separating-axis test for yawed rectangular footprints. Together
        # with Y intervals it also catches another pile occupying the stack.
        for other in items:
            if other['id'] == item['id']:
                continue
            if min(position[1]+size[1], other['position_m'][1]+other['box_m'][1])-max(position[1], other['position_m'][1]) <= 1e-8:
                continue
            separated = False
            for yaw in (item['yaw_deg'], other['yaw_deg']):
                a = math.radians(yaw)
                for axis in ((math.cos(a), -math.sin(a)), (math.sin(a), math.cos(a))):
                    distance = abs(sum((position[k]-other['position_m'][k])*axis[j] for j, k in enumerate((0,2))))
                    reach = 0.0
                    for box in (item, other):
                        angle = math.radians(box['yaw_deg'])
                        reach += (abs(axis[0]*math.cos(angle)-axis[1]*math.sin(angle))*box['box_m'][0]/2+
                                  abs(axis[0]*math.sin(angle)+axis[1]*math.cos(angle))*box['box_m'][2]/2)
                    separated |= distance >= reach-1e-8
            assert separated, item['id']+' overlaps '+other['id']
        checked += 1
    # Independently reproduce the pre-brief spawn rule. Loose additions must not
    # become new destinations or change ties, heights, IDs or horizontal poses.
    destinations = [r['position_m'] for r in doc['objects'] if r['carriable'] or r['prototype'] in {'cottage','tower'}]
    destinations += [r['position_m'] for r in doc['scatter'] if r['prototype'] in {'cottage','tower'}]
    for original, actual in zip(source_room['spawns'], room['spawns']):
        x, _, z = original['position_m']
        assert actual['id'] == original['id'] and actual['role'] == original['role']
        assert actual['position_m'][::2] == [x,z]
        assert abs(actual['position_m'][1]-ground(terrain, x, z)) < 1e-8
        yaw = original['yaw_deg']
        if destinations:
            target = min(destinations, key=lambda p: (p[0]-x)**2+(p[2]-z)**2)
            dx, dz = target[0]-x, target[2]-z
            if dx or dz:
                yaw = math.degrees(math.atan2(-dx, -dz))
        assert abs(actual['yaw_deg']-yaw) < 1e-8
    return checked


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--scratch', type=Path, required=True)
    parser.add_argument('--room', help='Optional single corpus folder name')
    parser.add_argument('--shard-count', type=int, default=1)
    parser.add_argument('--shard-index', type=int, default=0)
    args = parser.parse_args()
    scratch = args.scratch.absolute()
    os.makedirs(scratch, exist_ok=True)
    rooms = sorted((ROOT/'pipeline/landscape/corpus/rooms').iterdir())
    rooms = [r for r in rooms if r.is_dir() and (args.room is None or r.name == args.room)]
    assert args.shard_count > 0 and 0 <= args.shard_index < args.shard_count
    rooms = rooms[args.shard_index::args.shard_count]
    assert rooms, 'no matching corpus room'
    start = time.monotonic()
    repeat_root = scratch/('repeat-'+uuid.uuid4().hex)
    for source in rooms:
        package = scratch/'packages'/source.name
        if not package.exists():
            generate(source, package)
        room_id = 'landscape_'+source.name
        out = scratch/'rooms'/room_id
        if not out.exists():
            export_room(package, source, room_id, out)
        room = json.loads((out/'room.json').read_bytes())
        repeat = repeat_root/room_id
        stats = export_room(package, source, room_id, repeat)
        assert {p.relative_to(out):p.read_bytes() for p in out.rglob('*') if p.is_file()} == {
            p.relative_to(repeat):p.read_bytes() for p in repeat.rglob('*') if p.is_file()}, 'byte mismatch'
        assert len(room['objects']) <= 512 and len(room['files']) <= 2048
        checked = check_log_contacts(room, package, source)
        proc = subprocess.run([sys.executable, '-B', str(ROOT/'contracts/validate.py'), '--room', str(out)],
                              capture_output=True, text=True, cwd=ROOT)
        print('VALIDATOR '+source.name+' exit='+str(proc.returncode)+' '+proc.stdout.strip(), flush=True)
        assert proc.returncode == 0, proc.stdout+proc.stderr
        loose = room['extensions']['x_landscape_loose']
        counts = Counter(i['name'] for i in loose['items'])
        mass_ranges = {name: [min(i['mass_kg'] for i in loose['items'] if i['name'] == name),
                              max(i['mass_kg'] for i in loose['items'] if i['name'] == name)] for name in counts}
        print('CORPUS_PASS '+json.dumps(dict(room=source.name, loose=loose['count'], kinds=counts,
                                            masses_kg=mass_ranges, checked_logs=checked, skipped=loose['skipped'],
                                            objects=stats['objects'], files=stats['files'], deterministic=True),
                                       sort_keys=True), flush=True)
        shutil.rmtree(repeat)
    print('CORPUS_COMPLETE rooms='+str(len(rooms))+' seconds='+str(round(time.monotonic()-start, 1)), flush=True)


if __name__ == '__main__':
    main()
