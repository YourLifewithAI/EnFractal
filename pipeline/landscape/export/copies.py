"""Plant copies (the 10x world): plants, trees and stones drawn as copies of one mesh per set.

contracts/README.md, "Plant copies". One set per scatter prototype: its mesh at full detail and up to three lighter
meshes for distance, and one table of fixed-width little-endian rows sorted by 8 m tile, then x, then z, behind a
tile index. Row n of set s is the entity plant:s-n. Trees carry their climbing collision in their rows, fitted to
the ground by trees.fit exactly as the merged tree_climb part was; stones collide as the convex hull of their mesh.

Groups: copies the generator placed together, found here because the package does not say: trees of any set
within GROVE_LINK_M of each other chain into a grove, and a patch (flowers of one species, reeds, ferns, heather)
chains its copies within PATCH_LINK_M. A grove needs at least 3 trees and a patch 6 copies; the rest are group 0.
Groups are numbered from 1 in table order (sets by name, rows in order), every number used.
"""
import math
import struct
import zlib
from array import array

from pipeline.landscape.harness.common import require
from .mesh import encode, transform
from .trees import TerrainHeights, fit, is_tree

TILE_M = 8
TABLE_MAGIC = b'EFPLANTS'
TABLE_VERSION = 1
HEADER = struct.Struct('<8s8I')
TILE_ENTRY = struct.Struct('<iiII')
ROW = struct.Struct('<11f3I')
TREE_TAIL = struct.Struct('<6fI')
CAP = struct.Struct('<8f')
KIND_CODES = {'plant': 0, 'tree': 1, 'stone': 2}
MAX_SETS = 64
MAX_CAP_SLOTS = 8
SUBMERGED = 1
STONE_ROLES = frozenset({'rock', 'stone', 'scree', 'gravel', 'cliff'})
SPECIES = {'grass_tuft': 'grass', 'flower_clump': 'flowers', 'boulder': 'rock'}
PLANT_FUEL = {'grass': 'grass', 'grass_tuft': 'grass', 'reeds': 'grass', 'shrub': 'shrub'}
GROVE_LINK_M, GROVE_MIN = .5, 3
PATCH_LINK_M, PATCH_MIN = .15, 6
PATCHES = frozenset({'daisies', 'buttercups', 'wildflowers', 'flowers', 'reeds', 'fern', 'heather'})
# Distance detail: (cells per axis, keep one in this many loose blades, cluster the whole prototype at once).
LOD_SPECS = ((3, 2, False), (2, 6, False), (2, 20, True))
SMALL_PIECE = 12  # a piece of fewer triangles (a blade, a flower head) is thinned, never clustered


def f32(value):
    return struct.unpack('<f', struct.pack('<f', value))[0]


def token(name, pattern_chars='abcdefghijklmnopqrstuvwxyz0123456789_-', limit=64):
    """A package name as a contract token: lowercase, other characters as '_', starting with a letter."""
    text = ''.join(c if c in pattern_chars else '_' for c in name.lower())
    if not text or not text[0].isalpha():
        text = 'p'+text
    return text[:limit]


def classify(name, parts, colliding):
    """'tree', 'plant' (leafy, colliding nowhere), 'stone' (rock all through) or None (stays merged)."""
    if is_tree(name, parts):
        return 'tree'
    roles = {p['role'] for p in parts}
    if 'foliage' in roles and not colliding:
        return 'plant'
    if roles <= STONE_ROLES:
        return 'stone'
    return None


def set_record_fields(name, kind):
    species = token(SPECIES.get(name, name))
    if kind == 'tree':
        return dict(kind=kind, species=species, collides='tree_climb', seeds=True,
                    fuel_class='conifer' if name == 'conifer' else 'broadleaf')
    if kind == 'stone':
        return dict(kind=kind, species=species, collides='solid', seeds=False, fuel_class='none')
    return dict(kind=kind, species=species, collides='none', seeds=True, fuel_class=PLANT_FUEL.get(name, 'herb'))


# ---------- distance detail for a prototype ----------

def _pieces(part):
    """Triangle index lists of the part's pieces joined by shared vertices, in first-triangle order."""
    parent = list(range(len(part['positions'])))

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for a, b, c in part['triangles']:
        ra, rb, rc = root(a), root(b), root(c)
        parent[rb] = ra
        parent[root(rc)] = ra
    pieces = {}
    for t, face in enumerate(part['triangles']):
        pieces.setdefault(root(face[0]), []).append(t)
    return list(pieces.values())


def _area(part, face):
    a, b, c = [part['positions'][i] for i in face]
    u, v = [b[i]-a[i] for i in range(3)], [c[i]-a[i] for i in range(3)]
    n = [u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0]]
    return math.sqrt(sum(x*x for x in n))/2


def _bounds(points):
    return [min(p[i] for p in points) for i in range(3)], [max(p[i] for p in points) for i in range(3)]


def simplify(parts, cells, divisor, whole):
    """A lighter copy of a prototype: pieces of SMALL_PIECE triangles or more cluster their vertices on a grid of
    `cells` per axis (over the piece, or over the whole prototype when `whole`), each cluster at its points' mean;
    smaller pieces (blades) are thinned to the largest 1 in `divisor` (at least one per part). Then re-pivoted at
    the bottom centre."""
    lo_all, hi_all = _bounds([v for p in parts for v in p['positions']])
    out = []
    for part in parts:
        pieces = _pieces(part)
        big = [p for p in pieces if len(p) >= SMALL_PIECE]
        small = [p for p in pieces if len(p) < SMALL_PIECE]
        new = dict(role=part['role'], positions=[], triangles=[])
        has_tints, has_blend = 'tints' in part, 'blend_role' in part
        if has_tints:
            new['tints'] = []
        if has_blend:
            new['blend_role'], new['blend_weights'] = part['blend_role'], []
        groups = [[t for p in big for t in p]] if whole and big else big
        for faces in groups:
            used = sorted({i for t in faces for i in part['triangles'][t]})
            lo, hi = (lo_all, hi_all) if whole else _bounds([part['positions'][i] for i in used])
            size = [(hi[i]-lo[i])/cells or 1. for i in range(3)]
            keys, members = {}, []
            cluster = {}
            for i in used:
                v = part['positions'][i]
                key = tuple(min(cells-1, int((v[k]-lo[k])/size[k])) for k in range(3))
                if key not in keys:
                    keys[key] = len(members)
                    members.append([])
                members[keys[key]].append(i)
                cluster[i] = keys[key]
            local = {}
            seen = set()
            for t in faces:
                ids = [cluster[i] for i in part['triangles'][t]]
                if len(set(ids)) < 3 or tuple(sorted(ids)) in seen:
                    continue
                seen.add(tuple(sorted(ids)))
                face = []
                for cid in ids:
                    if cid not in local:
                        local[cid] = len(new['positions'])
                        group = members[cid]
                        new['positions'].append([sum(part['positions'][i][k] for i in group)/len(group)
                                                 for k in range(3)])
                        if has_tints:
                            new['tints'].append([sum(part['tints'][i][k] for i in group)/len(group)
                                                 for k in range(3)]+[1])
                        if has_blend:
                            new['blend_weights'].append(sum(part['blend_weights'][i] for i in group)/len(group))
                    face.append(local[cid])
                a, b, c = [new['positions'][i] for i in face]
                u, v = [b[i]-a[i] for i in range(3)], [c[i]-a[i] for i in range(3)]
                if sum(x*x for x in (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0])) > 1e-18:
                    new['triangles'].append(face)
        if small:
            ranked = sorted(range(len(small)), key=lambda k: (-sum(_area(part, part['triangles'][t]) for t in small[k]), k))
            keep = sorted(ranked[:max(1 if not new['triangles'] else 0, math.ceil(len(small)/divisor))])
            for k in keep:
                remap = {}
                for t in small[k]:
                    face = []
                    for i in part['triangles'][t]:
                        if i not in remap:
                            remap[i] = len(new['positions'])
                            new['positions'].append(list(part['positions'][i]))
                            if has_tints:
                                new['tints'].append(list(part['tints'][i]))
                            if has_blend:
                                new['blend_weights'].append(part['blend_weights'][i])
                        face.append(remap[i])
                    new['triangles'].append(face)
        if new['triangles']:
            out.append(new)
    if not out:
        return []
    lo, hi = _bounds([v for p in out for v in p['positions']])
    shift = [-(lo[0]+hi[0])/2, -lo[1], -(lo[2]+hi[2])/2]
    for p in out:
        p['positions'] = [[v[0]+shift[0], v[1]+shift[1], v[2]+shift[2]] for v in p['positions']]
    return out


def triangles_in(parts):
    return sum(len(p['triangles']) for p in parts)


def lod_meshes(parts):
    """Up to three lighter meshes, each lighter than the one before."""
    levels, previous = [], triangles_in(parts)
    for cells, divisor, whole in LOD_SPECS:
        lighter = simplify(parts, cells, divisor, whole)
        count = triangles_in(lighter)
        if not 0 < count < previous:
            break
        levels.append(lighter)
        previous = count
    return levels


# ---------- groves and patches ----------

def _family(kind, species):
    if kind == 'tree':
        return ('grove',), GROVE_LINK_M, GROVE_MIN
    if kind == 'plant' and species in PATCHES:
        return ('patch', species), PATCH_LINK_M, PATCH_MIN
    return None, None, None


def number_groups(sets):
    """Each set's rows get group numbers (list per set, in table order), dense from 1 in table order."""
    parent = {}

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    families = {}
    for s, record in enumerate(sets):
        family, link, minimum = _family(record['kind'], record['species'])
        if family is None:
            continue
        families.setdefault(family, (link, minimum, []))[2].extend(
            (s, r, x, z) for r, (x, z) in enumerate(record['xz']))
    minimum_of = {}
    for family, (link, minimum, points) in sorted(families.items()):
        grid = {}
        for point in points:
            key = (math.floor(point[2]/link), math.floor(point[3]/link))
            grid.setdefault(key, []).append(point)
            parent[point[:2]] = point[:2]
            minimum_of[point[:2]] = minimum
        limit = link*link
        for (gx, gz), cell in grid.items():
            for dx, dz in ((0, 0), (1, -1), (1, 0), (1, 1), (0, 1)):
                other = grid.get((gx+dx, gz+dz))
                if other is None:
                    continue
                for i, a in enumerate(cell):
                    for b in (cell[i+1:] if (dx, dz) == (0, 0) else other):
                        if (a[2]-b[2])**2+(a[3]-b[3])**2 <= limit:
                            ra, rb = root(a[:2]), root(b[:2])
                            if ra != rb:
                                parent[max(ra, rb)] = min(ra, rb)
    sizes = {}
    for key in parent:
        r = root(key)
        sizes[r] = sizes.get(r, 0)+1
    numbers, result = {}, []
    for s, record in enumerate(sets):
        groups = []
        for r in range(len(record['xz'])):
            key = (s, r)
            group = 0
            if key in parent:
                top = root(key)
                if sizes[top] >= minimum_of[key]:
                    if top not in numbers:
                        numbers[top] = len(numbers)+1
                    group = numbers[top]
            groups.append(group)
        result.append(groups)
    return result, len(numbers)


# ---------- the tables ----------

def water_surface(water, bounds):
    """Topmost water over a point, for the submerged flag; None where there is none."""
    if not water:
        return None, None
    lo, hi = bounds['min_m'], bounds['max_m']
    heights = TerrainHeights(water, region=(lo[0], hi[0], lo[2], hi[2]))
    top = max((v[1] for p in water for v in p['positions']), default=None)
    return heights, top


def build_copies(candidates, prototypes, terrain_heights, water, bounds, put, fits=None):
    """candidates: {name: (kind, [(scatter index, record), ...])} in package order. prototypes(name) -> (parts,
    size). fits: trees.fit answers by scatter index, already worked out (landwork.py); others are fitted here.
    Writes each set's meshes and table through put(path, bytes); returns (plant_copies block, stats)."""
    names, used = {}, set()
    for name in sorted(candidates):
        base = token(name, 'abcdefghijklmnopqrstuvwxyz0123456789_', 32)
        unique, n = base, 1
        while unique in used:
            n += 1
            unique = f'{base[:28]}_{n}'
        used.add(unique)
        names[name] = unique
    order = sorted(candidates, key=lambda name: names[name])
    require(len(order) <= MAX_SETS, 'more than 64 plant copy sets')
    surface, surface_top = water_surface(water, bounds)
    sets = []
    for name in order:
        kind, pairs = candidates[name]
        indices, records = [i for i, _ in pairs], [r for _, r in pairs]
        parts, _ = prototypes(name)
        fields = set_record_fields(name, kind)
        xs = array('f', [r['position_m'][0] for r in records])
        ys = array('f', [r['position_m'][1] for r in records])
        zs = array('f', [r['position_m'][2] for r in records])
        rows = sorted(range(len(records)),
                      key=lambda i: (math.floor(xs[i]/TILE_M), math.floor(zs[i]/TILE_M), xs[i], zs[i], i))
        tails = None
        if kind == 'tree':
            tails = []
            for i in rows:
                record = records[i]
                tree = (fits or {}).get(indices[i])
                if tree is None:
                    tree = fit(name, transform(parts, record['scale'], record['position_m'], record['yaw_deg']),
                               terrain_heights)
                caps = sorted(tree['caps'], key=lambda c: -c[5])[:MAX_CAP_SLOTS]
                tails.append(dict(tree, caps=[c for c in tree['caps'] if c in caps]))
        sets.append(dict(fields, name=name, set=names[name], records=records, rows=rows,
                         xyz=(xs, ys, zs), tails=tails, xz=[(xs[i], zs[i]) for i in rows]))
    group_numbers, group_count = number_groups(sets)
    block, stats = [], {}
    for record, groups in zip(sets, group_numbers):
        name, kind = record['name'], record['kind']
        parts, _ = prototypes(name)
        base = transform(parts)
        height = max(f32(v[1]) for p in base for v in p['positions'])
        mesh_path = f"copies/{record['set']}.glb"
        put(mesh_path, encode(base))
        entry = {'set': record['set'], 'prototype': token(name), 'kind': kind, 'species': record['species'],
                 'collides': record['collides'], 'fuel_class': record['fuel_class'], 'seeds': record['seeds'],
                 'mesh': mesh_path, 'triangle_count': triangles_in(base)}
        lods = []
        for level, lighter in enumerate(lod_meshes(base), 1):
            path = f"copies/{record['set']}_lod{level}.glb"
            put(path, encode(lighter))
            lods.append({'mesh': path, 'triangle_count': triangles_in(lighter)})
        if lods:
            entry['lods'] = lods
        slots = 0
        if kind == 'tree':
            slots = max(len(t['caps']) for t in record['tails'])
            entry['cap_slots'] = slots
        xs, ys, zs = record['xyz']
        body = bytearray()
        tiles = []
        prefix = record['set'].encode('ascii')
        for n, i in enumerate(record['rows']):
            source = record['records'][i]
            x, y, z = xs[i], ys[i], zs[i]
            tile = (math.floor(x/TILE_M), math.floor(z/TILE_M))
            if tiles and (tiles[-1][0], tiles[-1][1]) == tile:
                tiles[-1][3] += 1
            else:
                tiles.append([tile[0], tile[1], n, 1])
            yaw = (source['yaw_deg']+180) % 360-180
            sx, sy, sz = source['scale']
            tint = source.get('tint', (1, 1, 1))
            flags = 0
            if surface is not None and y < surface_top:
                water_y, _ = surface.hit(x, z)
                if water_y is not None and water_y > y:
                    flags |= SUBMERGED
            seed = zlib.crc32(struct.pack('<ff', x, z)+prefix)
            top = y+height*f32(sy)
            body += ROW.pack(x, y, z, yaw, sx, sy, sz, tint[0], tint[1], tint[2], top, flags, groups[n], seed)
            if kind == 'tree':
                tree = record['tails'][n]
                body += TREE_TAIL.pack(tree['axis'][0], tree['axis'][1], tree['trunk_radius'], tree['pole_radius'],
                                       tree['trunk_top'], tree['pole_top'], len(tree['caps']))
                for cap in tree['caps']:
                    cx, cz, rx, rz, rim, top_y, tx, tz = cap
                    body += CAP.pack(cx, cz, rx, rz, rim, top_y, tx, tz)
                body += bytes(CAP.size*(slots-len(tree['caps'])))
        row_bytes = ROW.size+(TREE_TAIL.size+CAP.size*slots if kind == 'tree' else 0)
        require(len(body) == row_bytes*len(record['rows']), 'plant table row size')
        table = bytearray(HEADER.pack(TABLE_MAGIC, TABLE_VERSION, KIND_CODES[kind], row_bytes, len(record['rows']),
                                      len(tiles), slots, TILE_M, 0))
        for tx, tz, first, count in tiles:
            table += TILE_ENTRY.pack(tx, tz, first, count)
        table += body
        table_path = f"copies/{record['set']}.bin"
        put(table_path, bytes(table))
        entry.update(table=table_path, tiles=len(tiles), count=len(record['rows']))
        block.append(entry)
        stats[record['set']] = len(record['rows'])
    return {'tile_m': TILE_M, 'groups': group_count, 'sets': block}, dict(copies=stats, groups=group_count)
