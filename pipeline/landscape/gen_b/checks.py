"""Principle checks on a written package (not on the generator's memory).

    python -m pipeline.landscape.gen_b.checks --package <folder> --room <room folder>

Rebuilds the terrain height grid from the package's own meshes, then checks:
water level and downhill; nothing built floating or sunk; the walk from the
player's spawn to the carryable object and back within the low-incline and
step limits (an 8-connected path search on the terrain's vertices, where a
vertex is walkable only if every triangle touching it is gentle enough); and
every inventory object's footprint still reads in the land.
"""
import argparse
import heapq
import json
import math
from pathlib import Path

from pipeline.landscape.harness import read_package
from pipeline.landscape.harness.common import load_json
from pipeline.landscape.harness.kit import SIZES

WALK_DEG = 20.     # low incline, well under the body's 45 degrees
CARRY_DEG = 20.
STEP_M = .02       # the body's step-up
BUILT = {'cottage', 'tower', 'fence', 'crate', 'lantern', 'woodpile'}


class Terrain:
    def __init__(self, doc, meshes):
        cell = doc['x_gen_b']['cell_m']
        verts = [v for rec in doc['terrain']+[s for s in doc['scenery'] if s['mesh'] == 'ridges']
                 for p in meshes[rec['mesh']] for v in p['positions']]
        self.cell = cell
        self.x0 = min(v[0] for v in verts)
        self.z0 = min(v[2] for v in verts)
        pts = {(round((x-self.x0)/cell), round((z-self.z0)/cell)): y for x, y, z in verts}
        self.i0 = self.j0 = 0
        self.nx = max(k[0] for k in pts)+1
        self.nz = max(k[1] for k in pts)+1
        self.h = [math.nan]*(self.nx*self.nz)
        for (i, j), y in pts.items():
            self.h[j*self.nx+i] = y

    def x(self, i):
        return self.x0+i*self.cell

    def z(self, j):
        return self.z0+j*self.cell

    def height(self, x, z):
        """On the triangulated surface (checkerboard diagonals, as written)."""
        fx = (x-self.x0)/self.cell
        fz = (z-self.z0)/self.cell
        i = min(max(int(math.floor(fx)), 0), self.nx-2)
        j = min(max(int(math.floor(fz)), 0), self.nz-2)
        u, v = fx-i, fz-j
        n = self.nx
        a, b, c, d = self.h[j*n+i], self.h[j*n+i+1], self.h[(j+1)*n+i], self.h[(j+1)*n+i+1]
        if (i+self.i0+j+self.j0) % 2 == 0:
            return a+(d-c)*u+(c-a)*v if v > u else a+(b-a)*u+(d-b)*v
        return a+(b-a)*u+(c-a)*v if u+v < 1 else d+(c-d)*(1-u)+(b-d)*(1-v)

    def face_slopes(self):
        """Per vertex: steepest incident triangle, degrees."""
        n = self.nx
        out = [0.]*(n*self.nz)
        c = self.cell
        for j in range(self.nz-1):
            for i in range(n-1):
                a, b, cc, d = j*n+i, j*n+i+1, (j+1)*n+i, (j+1)*n+i+1
                if (i+self.i0+j+self.j0) % 2 == 0:
                    tris = [(a, cc, d), (a, d, b)]
                else:
                    tris = [(a, cc, b), (b, cc, d)]
                for t in tris:
                    ys = [self.h[k] for k in t]
                    if any(math.isnan(y) for y in ys):
                        continue
                    # Right-isoceles triangles on the lattice: gradient from legs.
                    ps = [(k % n, k//n, self.h[k]) for k in t]
                    (x0, z0, y0), (x1, z1, y1), (x2, z2, y2) = ps
                    ux, uy, uz = (x1-x0)*c, y1-y0, (z1-z0)*c
                    vx, vy, vz = (x2-x0)*c, y2-y0, (z2-z0)*c
                    nxx, nyy, nzz = uy*vz-uz*vy, uz*vx-ux*vz, ux*vy-uy*vx
                    s = math.degrees(math.acos(min(1., abs(nyy)/math.sqrt(nxx*nxx+nyy*nyy+nzz*nzz))))
                    for k in t:
                        out[k] = max(out[k], s)
        return out


def water_checks(doc, meshes, terrain):
    out = []
    ok = True
    for rec in doc['water']:
        ps = [v for p in meshes[rec['mesh']] for v in p['positions']]
        if rec['kind'] == 'still':
            ys = [v[1] for v in ps]
            spread = max(ys)-min(ys)
            # The still surface's own edge must sit under its banks.
            tris = [t for p in meshes[rec['mesh']] for t in p['triangles']]
            edges = {}
            for t in tris:
                for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
                    k = (min(a, b), max(a, b))
                    edges[k] = edges.get(k, 0)+1
            rim = sorted({k for e, n in edges.items() if n == 1 for k in e})
            inside_terrain = [k for k in rim if _covered(terrain, ps[k][0], ps[k][2])]
            exposed = [k for k in inside_terrain if terrain.height(ps[k][0], ps[k][2]) < ps[k][1]-.002]
            # A rim vertex may meet a stream that carries the water on.
            streams = [v for r in doc['water'] if r['kind'] != 'still' for p in meshes[r['mesh']] for v in p['positions']]
            loose = [k for k in exposed if not any(math.hypot(ps[k][0]-v[0], ps[k][2]-v[2]) < .12 for v in streams)]
            scenery = rec['mesh'] == 'distant_lake'
            good = spread <= 1e-6 and (scenery or not loose)
            out.append(dict(mesh=rec['mesh'], kind='still', level=round(ys[0], 5), level_spread=spread,
                            rim_vertices=len(inside_terrain), rim_below_surface=len(exposed),
                            rim_open_not_at_stream=len(loose), scenery=scenery, ok=good))
            ok &= good
        else:
            ys = [v[1] for v in ps]
            across = max(abs(ys[2*k]-ys[2*k+1]) for k in range(len(ys)//2))
            rises = [ys[2*k+2]-ys[2*k] for k in range(len(ys)//2-1)]
            uphill = max([0.]+rises)
            good = across <= 1e-6 and uphill <= 1e-6
            out.append(dict(mesh=rec['mesh'], kind=rec['kind'], top=round(ys[0], 4), bottom=round(ys[-1], 4),
                            max_tilt_across_m=across, max_rise_downstream_m=uphill, ok=good))
            ok &= good
    # Consecutive runs of one stream must not step up where they join.
    joins = []
    names = sorted(r['mesh'] for r in doc['water'] if r['kind'] != 'still')
    for a, b in zip(names, names[1:]):
        if a.rsplit('_', 1)[0] != b.rsplit('_', 1)[0]:
            continue
        ya = meshes[a][0]['positions'][-1][1]
        yb = meshes[b][0]['positions'][0][1]
        joins.append(dict(upstream=a, downstream=b, step_m=round(yb-ya, 6)))
        ok &= yb <= ya+1e-6
    return ok, out, joins


def _covered(terrain, x, z):
    fx = (x-terrain.x0)/terrain.cell
    fz = (z-terrain.z0)/terrain.cell
    return 0 <= fx < terrain.nx-1 and 0 <= fz < terrain.nz-1


def footprint_points(item, size):
    a = math.radians(item['yaw_deg'])
    c, s = math.cos(a), math.sin(a)
    x0, _, z0 = item['position_m']
    pts = [(x0, z0)]
    for fx, fz in ((-.5, -.5), (.5, -.5), (.5, .5), (-.5, .5), (0, -.5), (0, .5), (-.5, 0), (.5, 0)):
        lx, lz = fx*size[0], fz*size[2]
        pts.append((x0+c*lx+s*lz, z0-s*lx+c*lz))
    return pts


def grounding_checks(doc, terrain):
    """Built things sit on the ground (whole footprint); plants root at their stem."""
    rows = []
    bad = 0
    protos = doc['prototypes']
    for category in ('scatter', 'objects'):
        for item in doc[category]:
            name = item['prototype']
            base = SIZES[name] if name in SIZES else protos[name]['size_m']
            size = item['size_m'] if category == 'objects' else [b*s for b, s in zip(base, item['scale'])]
            y = item['position_m'][1]
            if category == 'objects' or name in BUILT:
                gs = [terrain.height(x, z) for x, z in footprint_points(item, size)]
                gap = y-min(gs)          # >0: some of the base hangs in the air
                sunk = max(gs)-y         # >0: the ground covers the base
                float_bad = gap > .012
                sunk_bad = sunk > max(.025, .2*size[1])
                kind = 'built'
            else:
                g = terrain.height(item['position_m'][0], item['position_m'][2])
                gap = y-g
                sunk = g-y
                float_bad = gap > .002
                sunk_bad = sunk > .012+.3*size[1]
                kind = 'plant'
            if float_bad or sunk_bad:
                bad += 1
            if kind == 'built' or float_bad or sunk_bad:
                rows.append(dict(prototype=name, kind=kind, position=[round(v, 3) for v in item['position_m']],
                                 hang_m=round(gap, 4), sunk_m=round(sunk, 4),
                                 ok=not (float_bad or sunk_bad)))
    return bad == 0, rows


def obstacles(doc, terrain, keep=()):
    """Vertices a body cannot stand on: water, buildings, trunks, rocks."""
    blocked = set()
    n = terrain.nx
    protos = doc['prototypes']

    def stamp(x, z, r):
        i0 = int(math.floor((x-r-terrain.x0)/terrain.cell))
        i1 = int(math.ceil((x+r-terrain.x0)/terrain.cell))
        j0 = int(math.floor((z-r-terrain.z0)/terrain.cell))
        j1 = int(math.ceil((z+r-terrain.z0)/terrain.cell))
        for j in range(max(0, j0), min(terrain.nz, j1+1)):
            for i in range(max(0, i0), min(n, i1+1)):
                if math.hypot(terrain.x(i)-x, terrain.z(j)-z) <= r:
                    blocked.add(j*n+i)
    for item in doc['scatter']:
        name = item['prototype']
        base = SIZES[name] if name in SIZES else protos[name]['size_m']
        size = [b*s for b, s in zip(base, item['scale'])]
        x, _, z = item['position_m']
        if name in BUILT:
            for px, pz in footprint_points(item, size):
                stamp(px, pz, .02)
            stamp(x, z, min(size[0], size[2])/2)
        elif name in ('broadleaf', 'conifer'):
            stamp(x, z, .045*size[0]/base[0]+.01)
        elif name in ('boulder', 'rock', 'shrub'):
            stamp(x, z, min(size[0], size[2])/2)
    for rec in doc['water']:
        if rec['mesh'] == 'distant_lake':
            continue
    return blocked


def water_mask(doc, meshes, terrain):
    wet = set()
    n = terrain.nx
    for rec in doc['water']:
        for p in meshes[rec['mesh']]:
            for t in p['triangles']:
                vs = [p['positions'][k] for k in t]
                xs = [v[0] for v in vs]
                zs = [v[2] for v in vs]
                i0 = int(math.floor((min(xs)-terrain.x0)/terrain.cell))
                i1 = int(math.ceil((max(xs)-terrain.x0)/terrain.cell))
                j0 = int(math.floor((min(zs)-terrain.z0)/terrain.cell))
                j1 = int(math.ceil((max(zs)-terrain.z0)/terrain.cell))
                for j in range(max(0, j0), min(terrain.nz, j1+1)):
                    for i in range(max(0, i0), min(n, i1+1)):
                        if _in_tri(terrain.x(i), terrain.z(j), vs):
                            y = terrain.h[j*n+i]
                            # Under the surface means wet; dry land above it is fine.
                            if y < max(v[1] for v in vs)+.002:
                                wet.add(j*n+i)
    return wet


def _in_tri(x, z, vs):
    (ax, _, az), (bx, _, bz), (cx, _, cz) = vs
    d = (bz-cz)*(ax-cx)+(cx-bx)*(az-cz)
    if abs(d) < 1e-12:
        return False
    u = ((bz-cz)*(x-cx)+(cx-bx)*(z-cz))/d
    v = ((cz-az)*(x-cx)+(ax-cx)*(z-cz))/d
    return u >= -1e-6 and v >= -1e-6 and 1-u-v >= -1e-6


def search(terrain, slopes, blocked, room, start, goal, limit, step):
    lo, hi = room['bounds']['min_m'], room['bounds']['max_m']
    n = terrain.nx

    def node(x, z):
        return int(round((z-terrain.z0)/terrain.cell))*n+int(round((x-terrain.x0)/terrain.cell))

    def ok(q):
        i, j = q % n, q//n
        x, z = terrain.x(i), terrain.z(j)
        return lo[0] <= x <= hi[0] and lo[2] <= z <= hi[2] and q not in blocked and slopes[q] <= limit
    s, g = node(*start), node(*goal)
    if not ok(s) or not ok(g):
        return None, dict(start_ok=ok(s), goal_ok=ok(g))
    gx, gz = terrain.x(g % n), terrain.z(g//n)
    dist = {s: 0.}
    prev = {}
    heap = [(0., s)]
    while heap:
        f, q = heapq.heappop(heap)
        if q == g:
            break
        d = dist[q]
        if f > d+math.hypot(terrain.x(q % n)-gx, terrain.z(q//n)-gz)+1e-9:
            continue
        i, j = q % n, q//n
        for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            ii, jj = i+di, j+dj
            if not (0 <= ii < n and 0 <= jj < terrain.nz):
                continue
            p = jj*n+ii
            if not ok(p) or abs(terrain.h[p]-terrain.h[q]) > step:
                continue
            nd = d+terrain.cell*(math.sqrt(2) if di and dj else 1)
            if nd < dist.get(p, math.inf)-1e-12:
                dist[p] = nd
                prev[p] = q
                heapq.heappush(heap, (nd+math.hypot(terrain.x(ii)-gx, terrain.z(jj)-gz), p))
    if g not in dist:
        return None, {}
    path = [g]
    while path[-1] != s:
        path.append(prev[path[-1]])
    path.reverse()
    steps = [abs(terrain.h[b]-terrain.h[a]) for a, b in zip(path, path[1:])]
    grades = []
    for a, b in zip(path, path[1:]):
        run = terrain.cell*(math.sqrt(2) if (a % n != b % n and a//n != b//n) else 1)
        grades.append(math.degrees(math.atan(abs(terrain.h[b]-terrain.h[a])/run)))
    return path, dict(length_m=round(dist[g], 3), vertices=len(path),
                      max_face_slope_deg=round(max(slopes[q] for q in path), 2),
                      max_step_m=round(max(steps), 4), max_grade_deg=round(max(grades), 2),
                      climb_m=round(max(terrain.h[q] for q in path)-min(terrain.h[q] for q in path), 3))


def walk_checks(doc, meshes, terrain, room):
    slopes = terrain.face_slopes()
    blocked = obstacles(doc, terrain) | water_mask(doc, meshes, terrain)
    player = next((s for s in room['spawns'] if s['role'] == 'player'), room['spawns'][0])
    start = (player['position_m'][0], player['position_m'][2])
    results = []
    ok = True
    carry = [o for o in doc['objects'] if o['carriable']]
    targets = [('to ' + o['id'], start, (o['position_m'][0], o['position_m'][2]), WALK_DEG) for o in carry]
    targets += [('back carrying ' + o['id'], (o['position_m'][0], o['position_m'][2]), start, CARRY_DEG) for o in carry]
    for k, item in enumerate([s for s in doc['scatter'] if s['prototype'] == 'cottage']):
        a = math.radians(item['yaw_deg'])
        half = SIZES['cottage'][2]*item['scale'][2]/2
        door = (item['position_m'][0]-math.sin(a)*(half+.06), item['position_m'][2]-math.cos(a)*(half+.06))
        targets.append((f'to cottage {k} door', start, door, WALK_DEG))
    for name, a, b, limit in targets:
        path, info = search(terrain, slopes, blocked, room, a, b, limit, STEP_M)
        good = path is not None
        ok &= good
        results.append(dict(route=name, found=good, limit_deg=limit, step_limit_m=STEP_M, **info))
    if not carry:
        ok = False
    return ok, results


def footprint_checks(inventory, terrain):
    """Per object: where its box went and how far the land's rise is from it."""
    rows = []
    ok = True
    objs = inventory['objects']
    children = {}
    for e in objs:
        t = e['placement']['support'].get('target_id')
        if t:
            children.setdefault(t, []).append(e)
    for e in objs:
        b = e['box']
        sx, sy, sz = b['size_m']
        cx, cy, cz = b['centre_m']
        base, top = cy-sy/2, cy+sy/2
        a = math.radians(b['yaw_deg'])
        c, s = math.cos(a), math.sin(a)
        inside, ring = [], []
        R = math.hypot(sx, sz)/2+.7
        step = terrain.cell
        k = int(R/step)
        for dj in range(-k, k+1):
            for di in range(-k, k+1):
                x, z = cx+di*step, cz+dj*step
                lx, lz = c*(x-cx)-s*(z-cz), s*(x-cx)+c*(z-cz)
                dx, dz = abs(lx)-sx/2, abs(lz)-sz/2
                if dx <= 0 and dz <= 0:
                    # Skip where a taller box (a child or an overlapping
                    # neighbour) stands on this footprint: that land is its.
                    covered = False
                    for other in objs:
                        ob = other['box']
                        if other is e or ob['centre_m'][1]+ob['size_m'][1]/2 <= top:
                            continue
                        oa = math.radians(ob['yaw_deg'])
                        ox, oz = x-ob['centre_m'][0], z-ob['centre_m'][2]
                        olx, olz = math.cos(oa)*ox-math.sin(oa)*oz, math.sin(oa)*ox+math.cos(oa)*oz
                        if abs(olx) <= ob['size_m'][0]/2 and abs(olz) <= ob['size_m'][2]/2:
                            covered = True
                    if not covered:
                        inside.append(terrain.height(x, z))
                elif .45 <= max(dx, dz) <= .7 and not e['placement']['support'].get('target_id'):
                    ring.append(terrain.height(x, z))
        land_top = max(inside)
        land_mean = sum(inside)/len(inside)
        around = sorted(ring)[len(ring)//2] if ring else base
        rise = land_top-around
        box_rise = top-(around if not e['placement']['support'].get('target_id') else base)
        # Reads if the land rises at least half the box's own rise above its
        # surroundings and its top lies within a third of the box height (min 0.15 m).
        reads = land_top-top >= -max(.15, sy/3) and land_top-top <= max(.25, sy*.45) \
            and (rise >= .5*min(box_rise, sy) or e['placement']['support'].get('target_id'))
        ok &= bool(reads)
        rows.append(dict(id=e['id'], kind=e['kind'], centre=[round(cx, 2), round(cz, 2)],
                         box_base=round(base, 3), box_top=round(top, 3), land_top=round(land_top, 3),
                         land_mean=round(land_mean, 3), top_delta_m=round(land_top-top, 3),
                         rise_above_surroundings_m=round(rise, 3), reads=bool(reads)))
    return ok, rows


def run(package, room_dir):
    doc, meshes, room, stats = read_package(package, room_dir)
    inventory = load_json(Path(room_dir)/'inventory.json')
    terrain = Terrain(doc, meshes)
    w_ok, water, joins = water_checks(doc, meshes, terrain)
    g_ok, ground = grounding_checks(doc, terrain)
    p_ok, walks = walk_checks(doc, meshes, terrain, room)
    f_ok, prints = footprint_checks(inventory, terrain)
    return dict(ok=w_ok and g_ok and p_ok and f_ok, water_ok=w_ok, grounded_ok=g_ok, walk_ok=p_ok,
                footprints_ok=f_ok, water=water, joins=joins, grounding=ground, walks=walks,
                footprints=prints, stats=stats)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', required=True)
    parser.add_argument('--room', required=True)
    parser.add_argument('--json', help='write the full result here')
    args = parser.parse_args()
    result = run(args.package, args.room)
    if args.json:
        Path(args.json).write_text(json.dumps(result, indent=2, sort_keys=True)+'\n', encoding='utf-8', newline='\n')
    for w in result['water']:
        print('WATER', json.dumps(w, sort_keys=True))
    for j in result['joins']:
        print('JOIN', json.dumps(j, sort_keys=True))
    built = [g for g in result['grounding'] if g['kind'] == 'built']
    bad = [g for g in result['grounding'] if not g['ok']]
    print('GROUNDED built=%d worst_hang_m=%.4f worst_sunk_m=%.4f failures=%d' % (
        len(built), max([g['hang_m'] for g in built] or [0]), max([g['sunk_m'] for g in built] or [0]), len(bad)))
    for g in bad[:10]:
        print('  NOT_GROUNDED', json.dumps(g, sort_keys=True))
    for w in result['walks']:
        print('WALK', json.dumps(w, sort_keys=True))
    for f in result['footprints']:
        print('FOOTPRINT %-16s %-14s top %.3f land %.3f delta %+.3f rise %.3f %s' % (
            f['id'], f['kind'], f['box_top'], f['land_top'], f['top_delta_m'], f['rise_above_surroundings_m'],
            'reads' if f['reads'] else 'LOST'))
    print('CHECKS water=%s grounded=%s walk=%s footprints=%s overall=%s' % (
        result['water_ok'], result['grounded_ok'], result['walk_ok'], result['footprints_ok'], result['ok']))
    raise SystemExit(0 if result['ok'] else 1)


if __name__ == '__main__':
    main()
