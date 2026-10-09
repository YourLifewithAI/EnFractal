"""The island and the sea: the walls become a ragged coast, the door a jetty.

Every room is an island in an endless sea. The island keeps the room's floor
plan; its coast runs near the walls: cliffs and headlands where landforms meet
the wall line, coves with beaches between them, a low rocky shore elsewhere,
and sea stacks off the headlands. A reef with breaking rocks rings the island a
short swim offshore; past it the sea deepens toward distant islands. The door
becomes a jetty in a small harbour, and a pass through the reef lies before it.

All heights are metres in the room frame (y up, -Z forward). SEA_Y is the sea
level. Polygons are [x, z] pairs with the room floor polygons' winding.
"""
import math
import random

from .field import bell, clamp, lerp, rr_dist, smooth
from .land import local, openings, point_in_poly, room_shape, wall_frame

SEA_Y = -.02            # the sea's surface, a little under the room's floor (y 0)
LAGOON_DEPTH = .22      # the lagoon between the coast and the reef: well over the 10 cm head
DEEP_DEPTH = .62        # the open sea past the reef
REEF_OFFSHORE = (.3, .6)   # the reef's distance off the coast (metres; a short swim at 10 cm)
PLAY_PAST_REEF = .3     # the playable sea reaches this far past the reef's crest
JETTY_WIDTH = .16
JETTY_DECK = .05        # deck top above the sea
SWIM_DEPTH = .08        # the body floats in water over this depth (Lane P)
T9, T16, T8, T22, T30, T72 = (math.tan(math.radians(a)) for a in (9, 16, 8, 22, 30, 72))
WALK_OUT_WET_DEG = 35.   # a step under water where the feet touch (the pond's walk-out rule)
WALK_OUT_DRY_DEG = 20.   # the low-incline walk on dry sand


def _door(room):
    doors = sorted([o for o in openings(room) if 'door' in o['kind']], key=lambda o: (-o['width'], o['id']))
    if not doors:
        return None
    o = doors[0]
    _, px, pz, ox, oz = wall_frame(room, o['x'], o['z'])
    # The outward normal of the door's wall, from a point just inside it.
    lo, hi, _ = room_shape(room)
    cx, cz = (lo[0]+hi[0])/2, (lo[2]+hi[2])/2
    ix, iz = o['x']+(cx-o['x'])*.05, o['z']+(cz-o['z'])*.05
    s, px, pz, ox, oz = wall_frame(room, ix, iz)
    return dict(id=o['id'], x=px, z=pz, nx=ox, nz=oz, width=o['width'], door_x=px, door_z=pz)


def _place_jetty(room, door, objects):
    """The jetty and its harbour stand at the door, unless a landform stands
    behind it (a cabinet scanned in front of the door): then they slide along
    the door's wall to the nearest place a path can come down to the water."""
    if door is None:
        return None
    nx_, nz_ = door['nx'], door['nz']
    ax, az = -nz_, nx_
    solid = [o for o in objects if o['sy'] >= .1]

    def clear(px, pz):
        # Still the door's own wall, well clear of its ends.
        for f in (-.45, 0., .45):
            s, _, _, ox, oz = wall_frame(room, px+ax*f-nx_*.05, pz+az*f-nz_*.05)
            if abs(s+.05) > .01 or ox*nx_+oz*nz_ < .99:
                return False
        return all(rr_dist(*local(o, px-nx_*t, pz-nz_*t), o['sx']/2, o['sz']/2, .03) > .3
                   for o in solid for t in (.05, .2, .35, .5))
    for k in range(0, 61):
        for sign in ((1,) if k == 0 else (1, -1)):
            off = sign*k*.05
            px, pz = door['x']+ax*off, door['z']+az*off
            if clear(px, pz):
                return dict(door, x=px, z=pz)
    return door


def _shore(d, hl, bw):
    """Highest land at d metres past the waterline (negative: inland). A blend
    of a cliff (72 degree face), a beach (9 then 16 degrees inland, an 8 degree
    wading shelf then 22 degrees down) and a low rocky shore (30 degrees).
    Half a metre inland the cap rises like a cliff: a landform standing there
    meets the beach as a bluff and keeps its own height."""
    cliff = SEA_Y+.015-T72*d
    if d <= 0:
        beach = SEA_Y+T9*min(-d, .25)+T16*min(max(0., -d-.25), .25)+T72*max(0., -d-.5)
        rocky = SEA_Y+.01+T30*min(-d, .3)+T72*max(0., -d-.3)
        if hl > .5:
            return math.inf   # a headland keeps its landform's height to the waterline, then drops
    else:
        beach = SEA_Y-T8*min(d, .29)-T22*max(0., d-.29)
        rocky = SEA_Y+.01-T30*d
    return hl*cliff+(1-hl)*(bw*beach+(1-bw)*rocky)


class Coast:
    """The coast's plan, read per wall point (the nearest point of the walkable
    floor's boundary), so it runs coherently along the coast."""

    def __init__(self, room, objects, spawns, noise):
        self.room, self.noise = room, noise
        self.objects = [o for o in objects if o['parent'] is None and o['sy'] >= .05]
        self.spawns = [(s[0], s[2], max(0., -wall_frame(room, s[0], s[2])[0])) for s in spawns]
        self.door = _place_jetty(room, _door(room), self.objects)
        self.memo = {}

    def at(self, px, pz):
        key = (round(px, 4), round(pz, 4))
        if key in self.memo:
            return self.memo[key]
        c, hl, bw, hw = self._coast(px, pz)
        # The reef a short swim off: off the farthest-reaching coast nearby (so a
        # headland next door never brings it close), and farther off a beach, so
        # the beach's shelf reaches swimming water first.
        near = max([c]+[self._coast(px+dx, pz+dz)[0] for dx, dz in ((.2, 0), (-.2, 0), (0, .2), (0, -.2),
                                                                       (.35, 0), (-.35, 0), (0, .35), (0, -.35))])
        n = self.noise
        lo = near+lerp(REEF_OFFSHORE[0], .55, bw)
        reef = clamp(.48+.12*n(px*1.1+41, pz*1.1-23), lo, max(lo, c+REEF_OFFSHORE[1]))
        out = (c, hl, bw, hw, reef)
        self.memo[key] = out
        return out

    def _coast(self, px, pz):
        n = self.noise
        # Headlands where a landform meets the wall line, and a few rocky points.
        hl = 0.
        for o in self.objects:
            d = rr_dist(*local(o, px, pz), o['sx']/2, o['sz']/2, .03)
            # A taller landform spreads farther, so it meets the sea as a cliff from farther in.
            reach = .5+.35*min(o['sy'], 1.5)
            hl = max(hl, smooth((reach-d)/.3)*smooth((o['sy']-.05)/.15))
        hl = max(hl, .8*smooth((n.fbm(px*.9+5, pz*.9-3, 2)-.3)/.15))
        n1 = n.fbm(px*2.2+13, pz*2.2-7, 3)
        n2 = n(px*5.5-3, pz*5.5+11)
        cove = -.12-.16*smooth((n1+.1)/.5)-.05*n2
        head = .1+.14*smooth((n1+.3)/.6)+.04*n2
        bw = (1-hl)*smooth((n.fbm(px*1.3-17, pz*1.3+29, 2)+.12)/.25)
        c = lerp(cove, head, hl)
        hw = 0.
        if self.door is not None:
            dd = math.hypot(px-self.door['x'], pz-self.door['z'])
            hw = 1-smooth((dd-self.door['width']/2-.1)/.3)
        c = lerp(c, -.06, hw)
        hl *= 1-hw
        bw = lerp(bw, 1., hw)
        # Arrivals keep their dry ground: the water stays .4 m from a spawn.
        for sx, sz, sd in self.spawns:
            w = 1-smooth((math.hypot(px-sx, pz-sz)-sd-.4)/.6)
            c = lerp(c, max(c, .4-sd), w)
        return c, hl, bw, hw


def carve_coast(grid, room, h, base, noise, objects, spawns, seed):
    """Shape the coast, the lagoon, the reef and the stacks into h. Returns the
    plan: per-vertex wall offset s, coast offset c, reef offset, beach weight,
    the sea stacks, the jetty and the coast reader."""
    rnd = random.Random(seed+11)
    coast = Coast(room, objects, [s for s in spawns], noise)
    nx = grid.nx
    S, C, R, BW, HL, PX, PZ, NX, NZ = ([0.]*grid.n for _ in range(9))
    for q in range(grid.n):
        x, z = grid.xs[q % nx], grid.zs[q//nx]
        s, px, pz, ox, oz = wall_frame(room, x, z)
        c, hl, bw, hw, reef = coast.at(px, pz)
        S[q], C[q], R[q], BW[q], HL[q], PX[q], PZ[q], NX[q], NZ[q] = s, c, reef, bw, hl, px, pz, ox, oz
    # Sea stacks off the headlands, between the cliffs and the reef.
    stacks = []
    cands = sorted(set((round(PX[q], 2), round(PZ[q], 2)) for q in range(0, grid.n, 7) if HL[q] > .55 and abs(S[q]) < .05))
    rnd.shuffle(cands)
    for px, pz in cands:
        if len(stacks) >= 4:
            break
        if any(math.hypot(px-a['wx'], pz-a['wz']) < 1.0 for a in stacks):
            continue
        c, hl, bw, hw, reef = coast.at(px, pz)
        r = .04+.025*rnd.random()
        # Clear of the cliff foot and of the reef, so it stands alone in the lagoon.
        off = c+.2+r+.06*rnd.random()
        if off+r+.18 > reef:
            continue
        ux, uz = _outward(room, px, pz)
        stacks.append(dict(wx=px, wz=pz, x=round(px+ux*off, 4), z=round(pz+uz*off, 4),
                           r=round(r, 4), top=round(SEA_Y+.12+.22*rnd.random(), 4)))
    # The jetty: from a landing in the harbour, square to the door's wall, out over the lagoon.
    jetty = None
    if coast.door is not None:
        d = coast.door
        c, hl, bw, hw, reef = coast.at(d['x'], d['z'])
        ux, uz = d['nx'], d['nz']
        s_root, s_end = c-.16, reef-.18
        jetty = dict(door_id=d['id'], ux=ux, uz=uz,
                     root=(d['x']+ux*s_root, d['z']+uz*s_root), end=(d['x']+ux*s_end, d['z']+uz*s_end),
                     top=SEA_Y+JETTY_DECK, width=JETTY_WIDTH, wall=(d['x'], d['z']), reef=reef)
    sea_cap = {}
    for q in range(grid.n):
        x, z = grid.xs[q % nx], grid.zs[q//nx]
        s, c, reef = S[q], C[q], R[q]
        d = s-c
        n = noise(x*3.7+5, z*3.7-9)
        floor = SEA_Y-(LAGOON_DEPTH+.04*n)
        past = s-reef
        floor -= (DEEP_DEPTH-LAGOON_DEPTH)*smooth((past-.05)/.4)
        cap = max(_shore(d, HL[q], BW[q]), floor)
        v = min(h[q], cap)
        # The reef: a band of rock just under the surface, with rocks breaking it.
        t = abs(past)/.085
        if t < 1:
            rocks = .055*smooth((noise.fbm(x*6.1-3, z*6.1+8, 2)-.12)/.25)
            crest = SEA_Y-.022+rocks
            if jetty is not None:
                # A pass through the reef before the jetty, for boats one day.
                ax = abs((x-jetty['wall'][0])*jetty['uz']-(z-jetty['wall'][1])*jetty['ux'])
                crest = lerp(crest, SEA_Y-.14, 1-smooth((ax-.18)/.12))
            v = max(v, crest-(1-bell(t))*.4)
        for st in stacks:
            r = math.hypot(x-st['x'], z-st['z'])
            if r < st['r']+.3:
                wob = 1+.18*noise(x*14+st['x'], z*14-st['z'])
                top = st['top']-.04*(r/st['r'])**2
                v = max(v, top if r < st['r']*wob else top-T72*(r-st['r']*wob)*3.)
        h[q] = v
        if d > .03:
            sea_cap[q] = v
    plan = dict(s=S, c=C, reef=R, bw=BW, hl=HL, stacks=stacks, jetty=jetty, coast=coast, sea_cap=sea_cap)
    return plan


def _outward(room, px, pz):
    """Outward unit normal of the floor's boundary at a wall point."""
    best = None
    for a in range(16):
        ang = math.tau*a/16
        ux, uz = math.cos(ang), math.sin(ang)
        s = wall_frame(room, px+ux*.05, pz+uz*.05)[0]
        if best is None or s > best[0]+1e-9:
            best = (s, ux, uz)
    return best[1], best[2]


def land_jetty(grid, h, jetty):
    """A level landing where the jetty meets the land, eased into the shore."""
    if jetty is None:
        return
    nx = grid.nx
    rx_, rz_ = jetty['root']
    level = jetty['top']-.004
    R, blend = .09, .22
    rx, rz = grid.span(rx_-R-blend, rx_+R+blend, rz_-R-blend, rz_+R+blend)
    for j in rz:
        for i in rx:
            q = j*nx+i
            d = math.hypot(grid.xs[i]-rx_, grid.zs[j]-rz_)
            h[q] = lerp(level, h[q], smooth((d-R)/blend))


def sea_mask(grid, h, keep_out=frozenset()):
    """Sea vertices: under the sea level and joined to the grid's edge (inland
    water below sea level, such as a tarn's bed, is not the sea)."""
    nx, nz = grid.nx, grid.nz
    sea = [False]*grid.n
    stack = [q for q in range(grid.n) if (q % nx in (0, nx-1) or q//nx in (0, nz-1)) and h[q] < SEA_Y]
    for q in stack:
        sea[q] = True
    while stack:
        q = stack.pop()
        i, j = q % nx, q//nx
        # Eight neighbours: the surface's triangles join diagonal vertices too.
        for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            ii, jj = i+di, j+dj
            if 0 <= ii < nx and 0 <= jj < nz:
                p = jj*nx+ii
                if not sea[p] and h[p] < SEA_Y and p not in keep_out:
                    sea[p] = True
                    stack.append(p)
    return sea


def sea_surface(grid, sea, far=60.):
    """The sea's still surface: every grid triangle touching the sea, then a
    strip from the grid's edge out to the horizon (all at SEA_Y)."""
    nx, nz = grid.nx, grid.nz
    index, pos, tris = {}, [], []

    def vid(key, x, z):
        if key not in index:
            index[key] = len(pos)
            pos.append([round(x, 5), SEA_Y, round(z, 5)])
        return index[key]
    for t in grid.triangles(lambda i, j: sea[j*nx+i] or sea[j*nx+i+1] or sea[(j+1)*nx+i] or sea[(j+1)*nx+i+1]):
        tris.append([vid(('g', q), grid.xs[q % nx], grid.zs[q//nx]) for q in t])
    x0, x1, z0, z1 = grid.xs[0], grid.xs[-1], grid.zs[0], grid.zs[-1]
    cx, cz = (x0+x1)/2, (z0+z1)/2
    X0, X1, Z0, Z1 = cx-far, cx+far, cz-far, cz+far
    sides = [[(i, 0) for i in range(nx)], [(nx-1, j) for j in range(nz)],
             [(i, nz-1) for i in range(nx-1, -1, -1)], [(0, j) for j in range(nz-1, -1, -1)]]
    for side in sides:
        inner, outer = [], []
        for i, j in side:
            x, z = grid.xs[i], grid.zs[j]
            inner.append(vid(('g', j*nx+i), x, z))
            ox = X0+(x-x0)/(x1-x0)*(X1-X0)
            oz = Z0+(z-z0)/(z1-z0)*(Z1-Z0)
            outer.append(vid(('f', round(ox, 3), round(oz, 3)), ox, oz))
        for k in range(len(side)-1):
            for t in ([inner[k], outer[k], outer[k+1]], [inner[k], outer[k+1], inner[k+1]]):
                tris.append(t)
    # Counter-clockwise seen from +Y, like the grid's own triangles.
    out = []
    for a, b, c in tris:
        pa, pb, pc = pos[a], pos[b], pos[c]
        cross = (pb[2]-pa[2])*(pc[0]-pa[0])-(pb[0]-pa[0])*(pc[2]-pa[2])
        if abs(cross) < 1e-14:
            continue
        out.append([a, b, c] if cross > 0 else [a, c, b])
    return pos, out


def distant_islands(grid, room, noise, seed, far=60.):
    """The sea floor beyond the grid and distant islands rising out of the sea
    where the far hills were: unreachable, shaped for a viewer anywhere."""
    rnd = random.Random(seed+23)
    x0, x1, z0, z1 = grid.xs[0], grid.xs[-1], grid.zs[0], grid.zs[-1]
    cx, cz = (x0+x1)/2, (z0+z1)/2
    r0 = min(x1-cx, z1-cz)-.25
    islands = []
    a0 = rnd.random()*math.tau
    count = 6
    for k in range(count):
        a = a0+math.tau*(k+.15+.7*rnd.random())/count
        dist = r0+5.+14.*rnd.random()
        islands.append(dict(x=cx+math.cos(a)*dist, z=cz+math.sin(a)*dist, r=1.6+3.4*rnd.random(),
                            top=.5+2.*rnd.random(), stretch=.7+.6*rnd.random(), turn=rnd.random()*math.pi))
    radii = [r0+.4*k+.014*k*k for k in range(46)]
    radii = [r for r in radii if r < far*.9]
    ring = 200
    pos, tri, tints, blend = [], [], [], []
    for r in radii:
        for k in range(ring):
            a = math.tau*k/ring
            x, z = cx+math.cos(a)*r, cz+math.sin(a)*r
            y = SEA_Y-DEEP_DEPTH-.04-.6*smooth((r-r0)/12.)
            for isl in islands:
                dx, dz = x-isl['x'], z-isl['z']
                c, s = math.cos(isl['turn']), math.sin(isl['turn'])
                u, v = (c*dx-s*dz)/isl['r'], (s*dx+c*dz)/(isl['r']*isl['stretch'])
                rho = math.hypot(u, v)*(1+.22*noise.fbm(x*.31+isl['x'], z*.31-isl['z'], 3))
                if rho < 1.6:
                    rise = (isl['top']+.5)*bell(rho/1.6)**1.2+.25*isl['top']*noise.ridged(x*.4, z*.4)*bell(rho/1.2)
                    y = max(y, SEA_Y-.5+rise)
            pos.append([round(x, 4), round(y, 4), round(z, 4)])
    for m in range(len(radii)-1):
        for k in range(ring):
            a = m*ring+k
            b = m*ring+(k+1) % ring
            c = (m+1)*ring+k
            d = (m+1)*ring+(k+1) % ring
            tri += [[a, d, b], [a, c, d]]
    for v in pos:
        y = v[1]-SEA_Y
        blend.append(round(smooth((y-.6)/.8), 4))
        g = clamp(.75+.1*noise(v[0], v[2]))
        f = smooth((y+.02)/.25)
        tints.append([round(lerp(1, g*.82, f), 4), round(lerp(.95, g*.9, f), 4), round(lerp(.85, g, f), 4), 1])
    record = [dict(x=round(i['x'], 3), z=round(i['z'], 3), radius_m=round(i['r'], 3),
                   top_m=round(SEA_Y-.5+i['top']+.5, 3)) for i in islands]
    return [dict(role='moss', positions=pos, triangles=tri, tints=tints, blend_role='rock', blend_weights=blend)], record


def _box(x0, x1, y0, y1, w0, w1):
    """An axis box in the jetty's frame (u along, w across)."""
    return [[x0, y0, w0], [x1, y0, w0], [x1, y0, w1], [x0, y0, w1], [x0, y1, w0], [x1, y1, w0], [x1, y1, w1], [x0, y1, w1]]


_BOX_T = [[0, 1, 2], [0, 2, 3], [4, 6, 5], [4, 7, 6], [0, 5, 1], [0, 4, 5], [1, 6, 2], [1, 5, 6],
          [2, 7, 3], [2, 6, 7], [3, 4, 0], [3, 7, 4]]


def jetty_mesh(grid, h, jetty):
    """Timber posts and stringers under a level plank deck, landward end on the landing."""
    rx, rz = jetty['root']
    ux, uz = jetty['ux'], jetty['uz']
    wx, wz = -uz, ux
    L = math.hypot(jetty['end'][0]-rx, jetty['end'][1]-rz)
    top = jetty['top']
    half = jetty['width']/2
    timber, wood = [], []

    def world(box):
        return [[round(rx+ux*u+wx*w, 5), round(y, 5), round(rz+uz*u+wz*w, 5)] for u, y, w in box]
    planks = int(L/.026)
    for k in range(planks):
        u0 = k*L/planks+.001
        wood.append(world(_box(u0, u0+L/planks-.002, top-.01, top, -half, half)))
    for w in (-half+.012, half-.028):
        timber.append(world(_box(0., L, top-.026, top-.01, w, w+.016)))
    k = 0
    while True:
        u = .12+k*.17
        if u > L-.01:
            break
        for w in (-half+.004, half-.02):
            x, z = rx+ux*u+wx*(w+.008), rz+uz*u+wz*(w+.008)
            bed = grid.tri_height(h, x, z)
            if bed < top-.03:
                timber.append(world(_box(u-.008, u+.008, bed-.02, top-.026, w, w+.016)))
        k += 1
    # A bollard at the end, for the boats to come.
    timber.append(world(_box(L-.03, L-.014, top, top+.02, -.008, .008)))
    out = []
    for role, boxes in (('timber', timber), ('wood', wood)):
        pos, tris = [], []
        for b in boxes:
            base = len(pos)
            pos += b
            tris += [[base+i for i in t] for t in _BOX_T]
        out.append(dict(role=role, positions=pos, triangles=tris))
    return out


# ---------------------------------------------------------------- outlines
def contour(grid, f, keep=None):
    """Closed loops of f = 0 (marching squares on the grid's vertices), as
    lists of (x, z); f < 0 is inside. Loops touching the grid's edge are open
    and dropped."""
    nx, nz = grid.nx, grid.nz
    xs, zs = grid.xs, grid.zs

    def point(a, b):
        fa, fb = f[a], f[b]
        t = fa/(fa-fb)
        return (xs[a % nx]+(xs[b % nx]-xs[a % nx])*t, zs[a//nx]+(zs[b//nx]-zs[a//nx])*t)
    links = {}

    def link(e1, e2):
        links.setdefault(e1, []).append(e2)
        links.setdefault(e2, []).append(e1)
    for j in range(nz-1):
        for i in range(nx-1):
            a, b, c, d = j*nx+i, j*nx+i+1, (j+1)*nx+i+1, (j+1)*nx+i
            ins = [f[a] < 0, f[b] < 0, f[c] < 0, f[d] < 0]
            if all(ins) or not any(ins):
                continue
            edges = [(a, b), (b, c), (d, c), (a, d)]
            cut = [e for e, (p, q) in zip(edges, ((0, 1), (1, 2), (3, 2), (0, 3))) if ins[p] != ins[q]]
            if len(cut) == 2:
                link(cut[0], cut[1])
            else:
                centre = (f[a]+f[b]+f[c]+f[d])/4 < 0
                # Saddle: join so the inside stays connected through the centre or not.
                if centre == ins[0]:
                    link(edges[0], edges[1])
                    link(edges[2], edges[3])
                else:
                    link(edges[0], edges[3])
                    link(edges[1], edges[2])
    seen = set()
    loops = []
    for start in sorted(links):
        if start in seen or len(links[start]) != 2:
            continue
        loop = [start]
        seen.add(start)
        prev, cur = None, start
        closed = False
        while True:
            nxt = [e for e in links[cur] if e != prev]
            if not nxt:
                break
            e = nxt[0]
            if e == start:
                closed = True
                break
            if e in seen or len(links[e]) != 2:
                break
            seen.add(e)
            loop.append(e)
            prev, cur = cur, e
        if closed and len(loop) >= 8:
            loops.append([point(*e) for e in loop])
    return loops


def simplify(pts, tol=.012):
    """Douglas-Peucker on a closed loop."""
    if len(pts) < 8:
        return pts
    k = max(range(len(pts)), key=lambda i: (pts[i][0]-pts[0][0])**2+(pts[i][1]-pts[0][1])**2)
    out = []
    for part in (pts[:k+1], pts[k:]+pts[:1]):
        out += _dp(part, tol)[:-1]
    return out


def _dp(pts, tol):
    if len(pts) < 3:
        return pts
    (ax, az), (bx, bz) = pts[0], pts[-1]
    L = math.hypot(bx-ax, bz-az) or 1e-9
    best, k = -1., 0
    for i in range(1, len(pts)-1):
        d = abs((bx-ax)*(az-pts[i][1])-(ax-pts[i][0])*(bz-az))/L
        if d > best:
            best, k = d, i
    if best <= tol:
        return [pts[0], pts[-1]]
    return _dp(pts[:k+1], tol)[:-1]+_dp(pts[k:], tol)


def area(poly):
    return sum(a[0]*b[1]-b[0]*a[1] for a, b in zip(poly, poly[1:]+poly[:1]))/2


def oriented(poly, sign):
    """The loop in the winding whose shoelace area has this sign (the room's floor's)."""
    return poly if area(poly)*sign > 0 else poly[::-1]


def main_loop(loops, at):
    """The largest loop around a point."""
    inside = [l for l in loops if point_in_poly(at[0], at[1], [(p[0], 0, p[1]) for p in l])]
    return max(inside or loops, key=lambda l: abs(area(l))) if loops else []


def poly_seg_dist(x, z, poly):
    best = math.inf
    for a, b in zip(poly, poly[1:]+poly[:1]):
        dx, dz = b[0]-a[0], b[1]-a[1]
        L = dx*dx+dz*dz
        t = 0 if L == 0 else clamp(((x-a[0])*dx+(z-a[1])*dz)/L)
        best = min(best, math.hypot(x-a[0]-t*dx, z-a[1]-t*dz))
    return best


def floor_boundary(room, step=.05):
    """The walkable floor's boundary in order (the floor polygon clipped to the
    room's bounds), as (x, z, inward nx, inward nz) every `step` metres."""
    lo, hi, floors = room_shape(room)
    poly = [(p[0], p[2]) for p in max(floors, key=lambda p: abs(area([(v[0], v[2]) for v in p])))] if floors else \
        [(lo[0], lo[2]), (lo[0], hi[2]), (hi[0], hi[2]), (hi[0], lo[2])]
    for axis, bound, keep_lo in ((0, lo[0], False), (0, hi[0], True), (1, lo[2], False), (1, hi[2], True)):
        out = []
        for a, b in zip(poly, poly[1:]+poly[:1]):
            ina = a[axis] <= bound if keep_lo else a[axis] >= bound
            inb = b[axis] <= bound if keep_lo else b[axis] >= bound
            if ina:
                out.append(a)
            if ina != inb:
                t = (bound-a[axis])/(b[axis]-a[axis])
                out.append((a[0]+(b[0]-a[0])*t, a[1]+(b[1]-a[1])*t))
        poly = out
    sign = 1. if area(poly) > 0 else -1.
    pts = []
    for a, b in zip(poly, poly[1:]+poly[:1]):
        L = math.hypot(b[0]-a[0], b[1]-a[1])
        if L < 1e-9:
            continue
        tx, tz = (b[0]-a[0])/L, (b[1]-a[1])/L
        # Inward normal: for a positive (x, z) shoelace loop the inside lies to the left of (tx, tz) as (-tz, tx).
        ix, iz = (-tz*sign, tx*sign)
        n = max(1, int(L/step))
        for k in range(n):
            t = (k+.5)*L/n
            pts.append((a[0]+tx*t, a[1]+tz*t, ix, iz))
    return pts


def find_beaches(grid, h, room, coast, jetty, limit=5):
    """Wash-ashore places: the middle of each beach stretch, on dry sand a body
    can stand on, with swimming water straight off it."""
    ring = floor_boundary(room)
    n = len(ring)
    good = []
    for x, z, ix, iz in ring:
        c, hl, bw, hw, reef = coast.at(*wall_frame(room, x+ix*.01, z+iz*.01)[1:3])
        good.append(bw*(1-hl) > .55)
    if all(good) or not any(good):
        runs = [list(range(n))] if all(good) else []
    else:
        first = next(k for k in range(n) if not good[k])
        runs, cur = [], []
        for m in range(1, n+1):
            k = (first+m) % n
            if good[k]:
                cur.append(k)
            elif cur:
                runs.append(cur)
                cur = []
        if cur:
            runs.append(cur)
    cands = []
    for run in runs:
        if len(run)*.05 < .35:
            continue
        order = sorted(run, key=lambda k: abs(run.index(k)-len(run)/2))
        for k in order:
            x, z, ix, iz = ring[k]
            if jetty is not None and abs((x-jetty['wall'][0])*jetty['uz']-(z-jetty['wall'][1])*jetty['ux']) < .2 \
                    and math.hypot(x-jetty['wall'][0], z-jetty['wall'][1]) < .6:
                continue
            spot = _ashore(grid, h, x, z, ix, iz)
            if spot is not None:
                cands.append((len(run), k, spot))
                break
    # Spread them round the island: the longest first, then the farthest.
    cands.sort(key=lambda c: (-c[0], c[1]))
    chosen = []
    for c in cands:
        if len(chosen) >= limit:
            break
        if all(math.hypot(c[2]['stand'][0]-o[2]['stand'][0], c[2]['stand'][1]-o[2]['stand'][1]) > 1.2 for o in chosen):
            chosen.append(c)
    return [c[2] for c in chosen]


def _ashore(grid, h, x, z, ix, iz):
    """Walk in from the wall line until dry sand a body can stand on; out until
    water a body swims in."""
    stand = None
    for k in range(0, 90):
        t = -.4+k*.01
        px, pz = x+ix*t, z+iz*t
        y = grid.tri_height(h, px, pz)
        if y >= max(SEA_Y+.03, .006) and t >= .1:
            px, pz = px+ix*.05, pz+iz*.05
            # Level enough for the walker's 11 cm disc wherever its lattice puts it.
            if _steepest(grid, h, px, pz) <= 18.5:
                stand = (px, pz)
            break
    if stand is None:
        return None
    water = None
    for k in range(0, 120):
        t = k*.01
        px, pz = stand[0]-ix*t, stand[1]-iz*t
        if SEA_Y-grid.tri_height(h, px, pz) >= .1:
            water = (px, pz)
            break
    if water is None:
        return None
    if not walk_out_ok(grid, h, stand, water):
        return None
    return dict(stand=stand, water=water, inward=(ix, iz))


def _steepest(grid, h, x, z, r=.06, e=.0125):
    worst = 0.
    for dx in (-r, -r/2, 0, r/2, r):
        for dz in (-r, -r/2, 0, r/2, r):
            a, b = x+dx, z+dz
            gx = (grid.tri_height(h, a+e, b)-grid.tri_height(h, a-e, b))/(2*e)
            gz = (grid.tri_height(h, a, b+e)-grid.tri_height(h, a, b-e))/(2*e)
            worst = max(worst, math.degrees(math.atan(math.hypot(gx, gz))))
    return worst


def walk_out_ok(grid, h, stand, water):
    """The walk out, as the checks judge it: under water where the feet touch,
    no step over WALK_OUT_WET_DEG; on the dry sand none over WALK_OUT_DRY_DEG."""
    run = math.hypot(stand[0]-water[0], stand[1]-water[1])
    n = max(2, int(run/.01))
    hs = [grid.tri_height(h, water[0]+(stand[0]-water[0])*k/n, water[1]+(stand[1]-water[1])*k/n) for k in range(n+1)]
    if SEA_Y-hs[0] < SWIM_DEPTH+.005 or hs[-1] < SEA_Y+.025:
        return False
    for k in range(n):
        if SEA_Y-hs[k+1] < SWIM_DEPTH:
            limit = WALK_OUT_WET_DEG if hs[k+1] < SEA_Y else WALK_OUT_DRY_DEG
            if math.degrees(math.atan(abs(hs[k+1]-hs[k])/(run/n))) > limit-1.:
                return False
    return True


def sand_and_seabed(grid, h, plan, sea, weights, tints):
    """Beaches are sand; the shallows sandy; the deeper seabed rock and scree;
    the reef rough rock. Overrides the land's own surface there."""
    nx = grid.nx
    for q in range(grid.n):
        y = h[q]
        bw = plan['bw'][q]*(1-plan['hl'][q])
        sand = 0.
        if SEA_Y-.07 < y < SEA_Y+.07:
            sand = bw*smooth((SEA_Y+.07-y)/.03)*smooth((y-(SEA_Y-.07))/.03)
        under = SEA_Y-y if sea[q] else 0.
        if under > .04 or (sea[q] and sand < .5):
            deep = smooth((under-.05)/.18)
            reef = abs(plan['s'][q]-plan['reef'][q]) < .085
            rough = 'rock' if reef else 'scree'
            w = {'gravel': (1-deep)*.8, rough: max(.2, deep)}
            t = tuple(lerp(1., c, deep) for c in (.86, .9, .92))
            if reef:
                t = (.98, .9, .84)
            sw = 1.
        elif sand > 0:
            w = {'gravel': 1.}
            t = (1., .95, .8)
            sw = sand
        else:
            continue
        old = weights[q]
        mix = {k: v*(1-sw) for k, v in old.items()}
        for k, v in w.items():
            mix[k] = mix.get(k, 0.)+v*sw
        top = sorted(((v, k) for k, v in mix.items() if v > 1e-4), reverse=True)[:2]
        tsum = sum(v for v, _ in top) or 1.
        weights[q] = {k: v/tsum for v, k in top}
        tints[q] = tuple(round(clamp(lerp(a, b, sw)), 4) for a, b in zip(tints[q], t))
    return weights, tints
