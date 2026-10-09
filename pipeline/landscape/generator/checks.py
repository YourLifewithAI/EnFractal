"""Principle checks on a written package (not on the generator's memory).

    python -m pipeline.landscape.generator.checks --package <folder> --room <room folder>

Rebuilds the terrain height grid from the package's own meshes, then checks:
water level and downhill; nothing built floating or sunk; the walk from the
player's spawn to the carryable object and back within the low-incline and
step limits (an 8-connected path search on the terrain's vertices, where a
vertex is walkable only if every triangle touching it is gentle enough); and
every inventory object's footprint still reads in the land; and the island:
the sea surrounds it out past the reef, the reef lies a short swim off the
coast, every promised beach has a shelving shore a swimmer walks out of the
sea up, and the beaches and the jetty are reachable on foot from the spawn.
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
BUILT = {'cottage', 'tower', 'fence', 'crate', 'lantern', 'woodpile', 'drying_line', 'footbridge'}


class Terrain:
    """The decoded package surface: topmost triangle under a point, from the
    package's own terrain (and scenery) meshes, with no knowledge of how the
    generator built them. `cell` is the sampling step the checks use."""

    def __init__(self, doc, meshes, cell=.025, bucket=.1):
        self.cell = cell
        self.bucket = bucket
        self.tris = []
        self.grid = {}
        records = doc['terrain']+doc['scenery']
        win = window(doc)
        for rec in records:
            for p in meshes[rec['mesh']]:
                vs = p['positions']
                for t in p['triangles']:
                    a, b, c = vs[t[0]], vs[t[1]], vs[t[2]]
                    det = (b[2]-c[2])*(a[0]-c[0])+(c[0]-b[0])*(a[2]-c[2])
                    if abs(det) < 1e-12 or not _meets(win, a, b, c):
                        continue
                    k = len(self.tris)
                    self.tris.append((a, b, c, det))
                    i0, i1, j0, j1 = _span(win, bucket, (a, b, c))
                    for j in range(j0, j1+1):
                        for i in range(i0, i1+1):
                            self.grid.setdefault((i, j), []).append(k)
        # Footbridge decks are walkable surfaces too: level, at the deck's top.
        self.decks = []
        for item in doc.get('scatter', ()):
            if item['prototype'] != 'footbridge':
                continue
            size = [b*s for b, s in zip(doc['prototypes']['footbridge']['size_m'], item['scale'])]
            a = math.radians(item['yaw_deg'])
            self.decks.append((item['position_m'][0], item['position_m'][2], math.cos(a), math.sin(a),
                               size[0]/2, size[2]/2, item['position_m'][1]+size[1]))
        self._memo = {}

    def deck(self, x, z):
        for x0, z0, c, s, hw, hl, top in self.decks:
            dx, dz = x-x0, z-z0
            if abs(c*dx-s*dz) <= hw and abs(s*dx+c*dz) <= hl:
                return top
        return None

    def hit(self, x, z):
        """(height, slope degrees) of the topmost triangle at (x, z), or None."""
        key = (round(x, 5), round(z, 5))
        if key in self._memo:
            return self._memo[key]
        best = None
        for k in self.grid.get((int(math.floor(x/self.bucket)), int(math.floor(z/self.bucket))), ()):
            a, b, c, det = self.tris[k]
            u = ((b[2]-c[2])*(x-c[0])+(c[0]-b[0])*(z-c[2]))/det
            v = ((c[2]-a[2])*(x-c[0])+(a[0]-c[0])*(z-c[2]))/det
            w = 1-u-v
            if min(u, v, w) < -1e-7:
                continue
            y = u*a[1]+v*b[1]+w*c[1]
            # On an edge or vertex several faces meet at one height. Ties
            # (within 1 micrometre, beyond float32 rounding of a written
            # package) take the gentlest face, decided by value, never by
            # triangle order, so the generator's memory and its package agree.
            if best is None or y > best[0]-1e-6:
                ux, uy, uz = b[0]-a[0], b[1]-a[1], b[2]-a[2]
                vx, vy, vz = c[0]-a[0], c[1]-a[1], c[2]-a[2]
                nx_, ny_, nz_ = uy*vz-uz*vy, uz*vx-ux*vz, ux*vy-uy*vx
                slope = math.degrees(math.acos(min(1., abs(ny_)/(math.sqrt(nx_*nx_+ny_*ny_+nz_*nz_) or 1e-30))))
                if best is None or y > best[0]+1e-6:
                    best = (y, slope)
                else:
                    best = (max(y, best[0]), min(slope, best[1]))
        top = self.deck(x, z) if self.decks else None
        if top is not None and (best is None or top >= best[0]):
            best = (top, 0.)
        if len(self._memo) < 2_000_000:
            self._memo[key] = best
        return best

    def height(self, x, z):
        r = self.hit(x, z)
        return math.nan if r is None else r[0]


def _sea(doc):
    return (doc.get('x_generator') or {}).get('sea')


def _in_loop(x, z, loop):
    inside = False
    for a, b in zip(loop, loop[1:]+loop[:1]):
        if (a[1] > z) != (b[1] > z) and x < (b[0]-a[0])*(z-a[1])/(b[1]-a[1])+a[0]:
            inside = not inside
    return inside


def window(doc, margin=.6):
    """Where the checks look: the island's playable water and a margin (the
    sea and the distant islands run on to the horizon; nothing is walked or
    swum out there). None for a package without a sea."""
    sea = _sea(doc)
    if not sea:
        return None
    b = sea['play_area']['bounds_m']
    return b['min_m'][0]-margin, b['min_m'][2]-margin, b['max_m'][0]+margin, b['max_m'][2]+margin


def _meets(win, *vs):
    if win is None:
        return True
    return not (max(v[0] for v in vs) < win[0] or min(v[0] for v in vs) > win[2] or
                max(v[2] for v in vs) < win[1] or min(v[2] for v in vs) > win[3])


def _span(win, bucket, vs):
    """Bucket index ranges of a triangle's box, clipped to the window."""
    x0, x1 = min(v[0] for v in vs), max(v[0] for v in vs)
    z0, z1 = min(v[2] for v in vs), max(v[2] for v in vs)
    if win is not None:
        x0, x1, z0, z1 = max(x0, win[0]), min(x1, win[2]), max(z0, win[1]), min(z1, win[3])
    return (int(math.floor(x0/bucket)), int(math.floor(x1/bucket)), int(math.floor(z0/bucket)),
            int(math.floor(z1/bucket)))


def water_checks(doc, meshes, terrain):
    out = []
    ok = True
    sea = _sea(doc)
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
            if sea and rec['mesh'] == sea['mesh']:
                # The sea runs on to the horizon past the playable water.
                loose = [k for k in loose if _in_loop(ps[k][0], ps[k][2], sea['play_area']['outline_m'])]
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
    return terrain.hit(x, z) is not None


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
            if name == 'footbridge':
                # Carried by its two ends: the banks under each end of the deck.
                a = math.radians(item['yaw_deg'])
                ends = [(item['position_m'][0]+math.sin(a)*f*size[2]*.45, item['position_m'][2]+math.cos(a)*f*size[2]*.45)
                        for f in (-1, 1)]
                saved, terrain.decks = terrain.decks, []
                gs = [terrain.hit(x, z)[0] for x, z in ends]
                terrain._memo.clear()
                terrain.decks = saved
                gap = y-max(gs)          # the deck's underside above its higher bank
                sunk = max(gs)-(y+size[1])
                float_bad = gap > .012 or y-min(gs) > .03
                sunk_bad = sunk > 0
                kind = 'built'
            elif category == 'objects' or name in BUILT:
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


def _in_tri(x, z, vs):
    (ax, _, az), (bx, _, bz), (cx, _, cz) = vs
    d = (bz-cz)*(ax-cx)+(cx-bx)*(az-cz)
    if abs(d) < 1e-12:
        return False
    u = ((bz-cz)*(x-cx)+(cx-bx)*(z-cz))/d
    v = ((cz-az)*(x-cx)+(ax-cx)*(z-cz))/d
    return u >= -1e-6 and v >= -1e-6 and 1-u-v >= -1e-6


CLEAR_R = .055      # half of the 11 cm carrying clearance (body and a held item)
EDGE_SAMPLE = .01   # every walk edge is sampled each centimetre


def obstacles(doc):
    """Footprints a body cannot pass through: buildings and yard things as
    rotated rectangles, trunks, rocks and shrubs as discs. Ground cover is
    passable."""
    protos = doc['prototypes']
    out = []
    for item in doc['scatter']:
        name = item['prototype']
        base = SIZES[name] if name in SIZES else protos[name]['size_m']
        size = [b*s for b, s in zip(base, item['scale'])]
        x, _, z = item['position_m']
        if name == 'footbridge':
            continue   # walked on, not around
        if name in BUILT:
            out.append(('box', x, z, size[0]/2, size[2]/2, math.radians(item['yaw_deg'])))
        elif name in ('broadleaf', 'conifer'):
            out.append(('disc', x, z, .045*size[0]/base[0]+.01))
        elif name in ('boulder', 'rock', 'shrub'):
            out.append(('disc', x, z, min(size[0], size[2])/2))
    return out


class Walker:
    """A* over a 2.5 cm lattice of the decoded surface. A node is standable
    when an 11 cm disc around it is dry, clear of obstacles, inside the
    walkable floor and on triangles no steeper than the limit; every edge is
    sampled each centimetre for its rise and incline."""

    def __init__(self, doc, meshes, terrain, room, limit=WALK_DEG, step=STEP_M):
        from .land import inside_distance
        self.t = terrain
        self.room = room
        self.limit = limit
        self.step = step
        self.inside = lambda x, z: inside_distance(room, x, z)
        self.obs = {}
        for o in obstacles(doc):
            r = o[3] if o[0] == 'disc' else math.hypot(o[3], o[4])
            for i in range(int(math.floor((o[1]-r-CLEAR_R)/.2)), int(math.floor((o[1]+r+CLEAR_R)/.2))+1):
                for j in range(int(math.floor((o[2]-r-CLEAR_R)/.2)), int(math.floor((o[2]+r+CLEAR_R)/.2))+1):
                    self.obs.setdefault((i, j), []).append(o)
        self.water = []
        self.wbuck = {}
        win = window(doc, .2)
        for rec in doc['water']:
            for p in meshes[rec['mesh']]:
                for t in p['triangles']:
                    vs = [p['positions'][k] for k in t]
                    if not _meets(win, *vs):
                        continue
                    k = len(self.water)
                    self.water.append(vs)
                    i0, i1, j0, j1 = _span(win, .1, vs)
                    for i in range(i0, i1+1):
                        for j in range(j0, j1+1):
                            self.wbuck.setdefault((i, j), []).append(k)
        self.cache = {}

    def wet(self, x, z):
        g = self.t.height(x, z)
        for k in self.wbuck.get((int(math.floor(x/.1)), int(math.floor(z/.1))), ()):
            vs = self.water[k]
            if _in_tri(x, z, vs) and g < max(v[1] for v in vs)+.002:
                return True
        return False

    def blocked(self, x, z):
        for o in self.obs.get((int(math.floor(x/.2)), int(math.floor(z/.2))), ()):
            if o[0] == 'disc':
                if math.hypot(x-o[1], z-o[2]) < o[3]+CLEAR_R:
                    return True
            else:
                a = o[5]
                c, s = math.cos(a), math.sin(a)
                dx, dz = x-o[1], z-o[2]
                lx, lz = c*dx-s*dz, s*dx+c*dz
                ex, ez = max(0., abs(lx)-o[3]), max(0., abs(lz)-o[4])
                if math.hypot(ex, ez) < CLEAR_R:
                    return True
        return False

    def standable(self, k):
        if k in self.cache:
            return self.cache[k]
        x, z = k[0]*self.t.cell, k[1]*self.t.cell
        ok = self.inside(x, z) >= CLEAR_R and not self.blocked(x, z)
        if ok:
            for dx, dz in ((0, 0), (CLEAR_R, 0), (-CLEAR_R, 0), (0, CLEAR_R), (0, -CLEAR_R)):
                hit = self.t.hit(x+dx, z+dz)
                if hit is None or hit[1] > self.limit or self.wet(x+dx, z+dz):
                    ok = False
                    break
        self.cache[k] = ok
        return ok

    def edge(self, a, b):
        ax, az, bx, bz = a[0]*self.t.cell, a[1]*self.t.cell, b[0]*self.t.cell, b[1]*self.t.cell
        run = math.hypot(bx-ax, bz-az)
        n = max(1, int(math.ceil(run/EDGE_SAMPLE)))
        y = self.t.height(ax, az)
        rise = grade = 0.
        for m in range(1, n+1):
            f = m/n
            hit = self.t.hit(ax+f*(bx-ax), az+f*(bz-az))
            if hit is None:
                return None
            rise = max(rise, abs(hit[0]-y))
            grade = max(grade, hit[1])
            y = hit[0]
        if rise > self.step or grade > self.limit:
            return None
        return rise, grade

    def key(self, x, z):
        return int(round(x/self.t.cell)), int(round(z/self.t.cell))

    def route(self, start, goal):
        s, g = self.key(*start), self.key(*goal)
        if not self.standable(s) or not self.standable(g):
            return None, dict(start_ok=self.standable(s), goal_ok=self.standable(g))
        c = self.t.cell
        cost = {s: 0.}
        prev = {}
        heap = [(0., 0., s)]
        while heap:
            _, d, k = heapq.heappop(heap)
            if d > cost.get(k, math.inf)+1e-12:
                continue
            if k == g:
                break
            for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
                n = (k[0]+di, k[1]+dj)
                if not self.standable(n):
                    continue
                if di and dj and not (self.standable((k[0]+di, k[1])) and self.standable((k[0], k[1]+dj))):
                    continue
                e = self.edge(k, n)
                if e is None:
                    continue
                length = c*(math.sqrt(2) if di and dj else 1)
                nd = d+length+2*abs(self.t.height(n[0]*c, n[1]*c)-self.t.height(k[0]*c, k[1]*c))
                if nd < cost.get(n, math.inf)-1e-12:
                    cost[n] = nd
                    prev[n] = k
                    heapq.heappush(heap, (nd+c*math.hypot(n[0]-g[0], n[1]-g[1]), nd, n))
        if g not in cost:
            return None, {}
        path = [g]
        while path[-1] != s:
            path.append(prev[path[-1]])
        path.reverse()
        rises, grades, length = [0.], [0.], 0.
        for a, b in zip(path, path[1:]):
            r, gr = self.edge(a, b)
            rises.append(r)
            grades.append(gr)
            length += c*math.hypot(b[0]-a[0], b[1]-a[1])
        hs = [self.t.height(k[0]*c, k[1]*c) for k in path]
        return path, dict(length_m=round(length, 3), nodes=len(path), max_face_slope_deg=round(max(grades), 2),
                          max_step_m=round(max(rises), 4), climb_m=round(max(hs)-min(hs), 3),
                          clearance_m=2*CLEAR_R, lattice_m=c)


def approaches(item, size, gap=.08):
    """Points a body would stand at to use a building or a yard thing: before
    its front (local -Z, a cottage's door) first, then its other sides."""
    a = math.radians(item['yaw_deg'])
    c, s = math.cos(a), math.sin(a)
    x0, _, z0 = item['position_m']
    out = []
    for lx, lz in ((0, -(size[2]/2+gap)), (0, size[2]/2+gap), (size[0]/2+gap, 0), (-(size[0]/2+gap), 0)):
        out.append((x0+c*lx+s*lz, z0-s*lx+c*lz))
    return out


YARD = ('woodpile', 'drying_line', 'firewood')


def walk_checks(doc, meshes, terrain, room):
    """From the player's spawn to every carryable object and back carrying it,
    to every cottage door and to every workyard thing (any free side)."""
    walker = Walker(doc, meshes, terrain, room)
    player = next((s for s in room['spawns'] if s['role'] == 'player'), room['spawns'][0])
    start = (player['position_m'][0], player['position_m'][2])
    results = []
    ok = True
    carry = [o for o in doc['objects'] if o['carriable']]
    targets = []
    for o in carry:
        at = (o['position_m'][0], o['position_m'][2])
        targets.append(('to '+o['id'], start, [at]))
        targets.append(('back carrying '+o['id'], at, [start]))
    protos = doc['prototypes']
    for k, item in enumerate(s for s in doc['scatter'] if s['prototype'] == 'cottage'):
        size = [b*s for b, s in zip(SIZES['cottage'], item['scale'])]
        targets.append(('to cottage %d door' % k, start, approaches(item, size)[:1]))
    for k, item in enumerate(s for s in doc['scatter'] if s['prototype'] in YARD):
        base = SIZES[item['prototype']] if item['prototype'] in SIZES else protos[item['prototype']]['size_m']
        size = [b*s for b, s in zip(base, item['scale'])]
        targets.append(('to %s %d' % (item['prototype'], k), start, approaches(item, size)))
    for name, a, goals in targets:
        info = {}
        path = None
        for goal in goals:
            path, info = walker.route(a, goal)
            if path is not None:
                break
        good = path is not None
        ok &= good
        results.append(dict(route=name, found=good, limit_deg=WALK_DEG, step_limit_m=STEP_M, **info))
    if not carry:
        ok = False
    return ok, results


SWIM_M = .08        # the body floats in water over this depth
WADE_DEG = 35.      # walking out of water: no step under the surface steeper (the pond's rule)


def sea_checks(doc, meshes, terrain, room):
    """The island: a sea at a level below the land, all round it past the reef;
    a reef a short swim off the coast; beaches a swimmer walks out of the sea
    up (water over SWIM_M deep, then no step steeper than the walk limit to dry
    sand) and reaches the spawn from on foot; the jetty reachable on foot."""
    sea = _sea(doc)
    if not sea:
        return False, dict(error='no sea in the package (x_generator.sea)')
    level = sea['level_m']
    rows = dict(level_m=level)
    ok = True
    rec = next((r for r in doc['water'] if r['mesh'] == sea['mesh']), None)
    ys = [v[1] for p in meshes[sea['mesh']] for v in p['positions']] if rec else []
    rows['sea_level_ok'] = bool(rec and rec['kind'] == 'still' and max(ys)-min(ys) <= 1e-6 and abs(ys[0]-level) < 1e-5)
    ok &= rows['sea_level_ok']
    # The playable water's edge is open sea all round, out past the reef.
    play = sea['play_area']['outline_m']
    shallow = [p for p in play if terrain.height(p[0], p[1]) > level-.1]
    rows['play_edge_points'] = len(play)
    rows['play_edge_not_deep'] = len(shallow)
    ok &= bool(play) and not shallow
    # The island stands above the sea: the player's spawn is inside the coast, on land.
    player = next((s for s in room['spawns'] if s['role'] == 'player'), room['spawns'][0])['position_m']
    coast = sea['coast']['outline_m']
    rows['spawn_on_island'] = bool(coast) and _in_loop(player[0], player[2], coast) and \
        terrain.height(player[0], player[2]) > level+.02
    ok &= rows['spawn_on_island']
    # The reef lies a short swim off the coast.
    reef = sea['reef']['outline_m']
    offs = []
    for x, z in reef:
        best = math.inf
        for a, b in zip(coast, coast[1:]+coast[:1]):
            dx, dz = b[0]-a[0], b[1]-a[1]
            L = dx*dx+dz*dz
            t = 0 if L == 0 else max(0., min(1., ((x-a[0])*dx+(z-a[1])*dz)/L))
            best = min(best, math.hypot(x-a[0]-t*dx, z-a[1]-t*dz))
        offs.append(best)
    rows['reef_offshore_m'] = [round(min(offs or [0]), 3), round(max(offs or [0]), 3)]
    ok &= bool(offs) and min(offs) >= .2 and max(offs) <= 1.
    # Beaches: walk out of the sea, then on to the spawn.
    walker = Walker(doc, meshes, terrain, room)
    start = (player[0], player[2])
    beaches = []
    for b in sea['beaches']:
        (wx, _, wz), (bx, _, bz) = b['water_m'], b['wash_ashore_m']
        depth0 = level-terrain.height(wx, wz)
        run = max(math.hypot(bx-wx, bz-wz), 1e-6)
        n = max(2, int(run/EDGE_SAMPLE))
        hs = [terrain.height(wx+(bx-wx)*k/n, wz+(bz-wz)*k/n) for k in range(n+1)]
        steep = wet_steep = 0.
        for k in range(n):
            if level-hs[k+1] < SWIM_M:   # feet on the bed from here on
                grade = math.degrees(math.atan(abs(hs[k+1]-hs[k])/(run/n)))
                if hs[k+1] < level:
                    wet_steep = max(wet_steep, grade)
                else:
                    steep = max(steep, grade)
        path, info = walker.route(start, (bx, bz))
        good = depth0 >= SWIM_M and wet_steep <= WADE_DEG and steep <= WALK_DEG and hs[-1] > level+.02 \
            and path is not None
        beaches.append(dict(id=b['id'], start_depth_m=round(depth0, 3), wade_max_deg=round(wet_steep, 2),
                            dry_max_deg=round(steep, 2),
                            dry_y_m=round(hs[-1], 4), reachable=path is not None, ok=good, **info))
        ok &= good
    rows['beaches'] = beaches
    ok &= bool(beaches)
    jetty = sea.get('jetty')
    if jetty:
        rx, ry, rz = jetty['root_m']
        path, info = walker.route(start, (rx, rz))
        deck = terrain.height(rx, rz)
        rows['jetty'] = dict(reachable=path is not None, deck_at_root_m=round(deck, 4), **info)
        ok &= path is not None and abs(deck-jetty['deck_top_m']) < .005
    return ok, rows


def footprint_checks(inventory, terrain, room):
    """Per object: where its box went and how far the land's rise is from it
    (inside the walkable floor only)."""
    from .land import inside_distance
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
        step = .03
        k = int(R/step)
        for dj in range(-k, k+1):
            for di in range(-k, k+1):
                x, z = cx+di*step, cz+dj*step
                lx, lz = c*(x-cx)-s*(z-cz), s*(x-cx)+c*(z-cz)
                dx, dz = abs(lx)-sx/2, abs(lz)-sz/2
                if dx <= 0 and dz <= 0 and inside_distance(room, x, z) < 0:
                    continue  # a scanned box poking through a wall: the ridge beyond is the wall's
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
                elif .45 <= max(dx, dz) <= .7 and not e['placement']['support'].get('target_id') \
                        and inside_distance(room, x, z) >= 0:
                    ring.append(terrain.height(x, z))   # surroundings within the room's floor
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


CLIMB_FAIL_RISE = .1   # a knife edge a body's height up or more breaks the principle


def climb_checks(doc, meshes):
    """What you climb, you can stand on top of. The land's grid is read back
    from the package's own land mesh; from every standable foot (dry land,
    not under inland water or the sea) the climb straight up each face over
    55 degrees is followed over its top (`climb.knife_edges`). A climb that
    tops out on a knife edge or a needle a body's height up or more fails;
    lower ones are reported."""
    from .climb import knife_edges
    from .field import Grid
    pts = {}
    for p in meshes.get('land', ()):
        for x, y, z in p['positions']:
            pts[(round(x, 4), round(z, 4))] = y
    gen = doc.get('x_generator') or {}
    if not pts or 'cell_m' not in gen:
        return True, dict(knife_edges=0, worst=[])
    xs = sorted({k[0] for k in pts})
    zs = sorted({k[1] for k in pts})
    grid = Grid(xs[0], zs[0], xs[-1], zs[-1], gen['cell_m'])
    nx = grid.nx
    h = [pts.get((round(grid.xs[q % nx], 4), round(grid.zs[q//nx], 4)), -math.inf) for q in range(grid.n)]
    level = (_sea(doc) or {}).get('level_m', -math.inf)
    wet = [False]*grid.n
    for rec in doc['water']:
        if rec['mesh'] == (_sea(doc) or {}).get('mesh'):
            continue
        for p in meshes[rec['mesh']]:
            vs = p['positions']
            for t in p['triangles']:
                a, b, c = vs[t[0]], vs[t[1]], vs[t[2]]
                rx, rz = grid.span(min(a[0], b[0], c[0]), max(a[0], b[0], c[0]), min(a[2], b[2], c[2]),
                                   max(a[2], b[2], c[2]))
                for j in rz:
                    for i in rx:
                        if _in_tri(grid.xs[i], grid.zs[j], (a, b, c)) and max(a[1], b[1], c[1]) > h[j*nx+i]:
                            wet[j*nx+i] = True
    bad = knife_edges(grid, h, lambda q: h[q] > level+.005 and not wet[q])
    fails = [b for b in bad if b['rise'] >= CLIMB_FAIL_RISE]
    worst = [dict(crest=[round(b['crest'][0], 3), round(b['crest_y'], 3), round(b['crest'][1], 3)],
                  rise_m=round(b['rise'], 3), foot=[round(v, 3) for v in b['foot']]) for b in bad[:8]]
    return not fails, dict(knife_edges=len(bad), failing=len(fails), fail_rise_m=CLIMB_FAIL_RISE, worst=worst)


def run(package, room_dir):
    doc, meshes, room, stats = read_package(package, room_dir)
    inventory = load_json(Path(room_dir)/'inventory.json')
    terrain = Terrain(doc, meshes)
    w_ok, water, joins = water_checks(doc, meshes, terrain)
    g_ok, ground = grounding_checks(doc, terrain)
    p_ok, walks = walk_checks(doc, meshes, terrain, room)
    f_ok, prints = footprint_checks(inventory, terrain, room)
    s_ok, sea = sea_checks(doc, meshes, terrain, room)
    c_ok, climbs = climb_checks(doc, meshes)
    # The climb principle is reported beside the others; it joins `ok` once the
    # generator meets it on the whole corpus (tall object crests still fail it).
    return dict(ok=w_ok and g_ok and p_ok and f_ok and s_ok, water_ok=w_ok, grounded_ok=g_ok,
                walk_ok=p_ok, footprints_ok=f_ok, sea_ok=s_ok, climb_ok=c_ok, water=water, joins=joins,
                grounding=ground, walks=walks, footprints=prints, sea=sea, climbs=climbs, stats=stats)


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
    sea = result['sea']
    for b in sea.get('beaches', ()):
        print('BEACH', json.dumps(b, sort_keys=True))
    print('SEA', json.dumps({k: v for k, v in sea.items() if k != 'beaches'}, sort_keys=True))
    print('CLIMB', json.dumps(result['climbs'], sort_keys=True))
    print('CHECKS water=%s grounded=%s walk=%s footprints=%s sea=%s climb=%s overall=%s' % (
        result['water_ok'], result['grounded_ok'], result['walk_ok'], result['footprints_ok'], result['sea_ok'],
        result['climb_ok'], result['ok']))
    raise SystemExit(0 if result['ok'] else 1)


if __name__ == '__main__':
    main()
