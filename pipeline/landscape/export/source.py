"""Bounded source-room data for Lane L's intro; never copy descriptive text."""
import math
import re
from pathlib import Path

from pipeline.landscape.harness.common import load_json, require, sha


def number(value, low, high):
    require(type(value) in (int, float) and low <= value <= high and math.isfinite(value),
            'source number must be finite and bounded')
    return value


def vector(value, count=3, low=-1000, high=1000):
    require(isinstance(value, list) and len(value) == count, 'invalid source vector')
    return [number(v, low, high) for v in value]


def identifier(value, pattern=r'[A-Za-z0-9_-]{1,64}'):
    require(isinstance(value, str) and re.fullmatch(pattern, value) is not None,
            'invalid source identifier')
    return value


def records(value, limit):
    require(isinstance(value, list) and len(value) <= limit and
            all(isinstance(r, dict) for r in value), 'invalid source record list')
    return value


def sorted_ids(value):
    require(len({r['id'] for r in value}) == len(value), 'duplicate source id')
    return sorted(value, key=lambda r: r['id'])


def source_data(room, pins, folder):
    """Whitelist the C3 shell and C4 inventory shared by corpus and roomscan.

    Poses use the bottom-centre pivot, metres, +Y up, -Z forward. Polygon winding
    stays authored; sorting points would destroy geometry. Never read truth.json,
    assets, evidence sheets, labels, display names, recipes or capture metadata.
    """
    inventory_path = Path(folder)/'inventory.json'
    require(sha(inventory_path.read_bytes()) == pins['inventory_sha256'], 'inventory hash mismatch')
    inventory = load_json(inventory_path)
    bounds = {k: vector(room['bounds'][k]) for k in ('min_m', 'max_m')}
    require(all(a < b for a, b in zip(bounds['min_m'], bounds['max_m'])), 'invalid source bounds')
    parts = []
    for part in records(room['shell']['parts'], 128):
        if part['role'] not in {'floor', 'wall', 'ceiling'}:
            continue
        geometry = part['geometry']
        require(geometry['kind'] == 'polygon', 'source intro requires polygon shell geometry')
        points = geometry['points_m']
        require(isinstance(points, list) and 3 <= len(points) <= 64, 'invalid source polygon')
        points = [vector(p) for p in points]
        thickness = number(geometry['thickness_m'], 0, 1)
        require(thickness > 0, 'source thickness must be positive')
        record = {'id': identifier(part['id'], r'shell:[A-Za-z0-9_-]{1,64}'),
                  'role': part['role'],
                  'geometry': {'kind': 'polygon', 'points_m': points, 'thickness_m': thickness}}
        if part['role'] == 'wall':
            record['height_m'] = max(p[1] for p in points)-min(p[1] for p in points)
            require(record['height_m'] > 0, 'source wall height must be positive')
        parts.append(record)
    parts = sorted_ids(parts)
    part_ids = {p['id'] for p in parts}
    openings = []
    for opening in records(room['shell'].get('openings', []), 64):
        require(opening['kind'] in {'door', 'garage_door', 'window', 'archway', 'vent', 'pet_door', 'portal'},
                'invalid source opening kind')
        host = identifier(opening['host_part_id'], r'shell:[A-Za-z0-9_-]{1,64}')
        require(host in part_ids, 'source opening host missing')
        size = vector(opening['size_m'], 2, 0, 20)
        require(all(v > 0 for v in size), 'source opening size must be positive')
        openings.append({'id': identifier(opening['id']), 'kind': opening['kind'],
                         'host_part_id': host, 'center_m': vector(opening['center_m']), 'size_m': size})
    objects = []
    for obj in records(inventory['objects'], 20_000):
        pose, box = obj['placement'], obj['box']
        kind = identifier(obj['kind'], r'[a-z][a-z0-9 _-]{0,59}')
        size = vector(box['size_m'], low=0, high=1000)
        require(all(v > 0 for v in size), 'source object size must be positive')
        record = {'id': identifier(obj['id']), 'kind': kind,
                  'kind_confidence': number(obj['confidence'], 0, 1),
                  'position_m': vector(pose['position_m']), 'size_m': size}
        # C4 supplies both; retain either when a scan only supplies one.
        require('rotation' in pose or 'yaw_deg' in pose or 'yaw_deg' in box, 'source object rotation missing')
        if 'rotation' in pose:
            rotation = vector(pose['rotation'], 4, -1, 1)
            require(abs(math.sqrt(sum(v*v for v in rotation))-1) <= 1e-3, 'source rotation must be unit length')
            record['rotation'] = rotation
        if 'yaw_deg' in pose or 'yaw_deg' in box:
            record['yaw_deg'] = number(pose.get('yaw_deg', box.get('yaw_deg')), -360, 360)
        colours = []
        for colour in records(obj['colours'], 3):
            require(isinstance(colour['hex'], str) and re.fullmatch(r'#[0-9a-fA-F]{6}', colour['hex']),
                    'invalid source colour')
            colours.append({'hex': colour['hex'], 'share': number(colour['share'], 0, 1)})
        record['colours'] = sorted(colours, key=lambda c: (-c['share'], c['hex']))
        support = pose['support']
        require(support['kind'] in {'floor', 'object', 'surface'}, 'invalid source support kind')
        record['support'] = {'kind': support['kind']}
        if 'height_m' in support:
            record['support']['height_m'] = number(support['height_m'], -1000, 1000)
        if 'target_id' in support:
            target = support['target_id']
            record['support']['target_id'] = None if target is None else identifier(target)
        objects.append(record)
    return {'room_id': identifier(room['room_id'], r'[a-z][a-z0-9_-]{0,63}'),
            'room_sha256': pins['room_sha256'], 'inventory_sha256': pins['inventory_sha256'],
            'bounds': bounds, 'shell': {'parts': parts, 'openings': sorted_ids(openings)},
            'objects': sorted_ids(objects)}
