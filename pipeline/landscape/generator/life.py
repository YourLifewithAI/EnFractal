"""Ecology and people: surfaces, planting, a hamlet, its paths and a carryable object."""
import math
import random

from pipeline.landscape.harness.kit import SIZES
from .field import clamp, lerp, slope_field, smooth
from .land import SOFT, dijkstra, geology_tint, inside_distance, local, room_shape
from .water import chaikin, resample

SNOWLINE = 1.5
WALK_LIMIT_DEG = 20.   # low-incline planning limit (the body manages 45 degrees)
STEP_LIMIT_M = .02


# ---------------------------------------------------------------- prototypes
def _fit(prims, size):
    vs = [v for p in prims for v in p['positions']]
    lo = [min(v[i] for v in vs) for i in range(3)]
    hi = [max(v[i] for v in vs) for i in range(3)]
    for p in prims:
        p['positions'] = [[round((v[i]-(lo[i] if i == 1 else (lo[i]+hi[i])/2))*size[i]/(hi[i]-lo[i]), 6)
                           for i in range(3)] for v in p['positions']]
    return prims


def _blades(rnd, count, height, spread, lean=.5):
    pos, tris, tints = [], [], []
    for k in range(count):
        a = rnd.random()*math.tau
        r = spread*math.sqrt(rnd.random())
        x, z = r*math.cos(a), r*math.sin(a)
        hgt = height*(.6+.4*rnd.random())
        w = .0018+.0014*rnd.random()
        ox, oz = math.cos(a)*hgt*lean*rnd.random(), math.sin(a)*hgt*lean*rnd.random()
        px, pz = -math.sin(a)*w, math.cos(a)*w
        base = len(pos)
        pos += [[x-px, 0, z-pz], [x+px, 0, z+pz], [x+ox, hgt, z+oz]]
        tris += [[base, base+1, base+2]]
        g = .55+.2*rnd.random()
        tints += [[g, g+.08, g*.8, 1], [g, g+.08, g*.8, 1], [1, 1, .78, 1]]
    return pos, tris, tints


def _heads(rnd, count, height, spread, size):
    pos, tris = [], []
    for k in range(count):
        a = rnd.random()*math.tau
        r = spread*math.sqrt(rnd.random())
        x, z, y = r*math.cos(a), r*math.sin(a), height*(.75+.25*rnd.random())
        s = size*(.8+.4*rnd.random())
        base = len(pos)
        pos += [[x+s, y, z], [x-s, y, z], [x, y+s*.7, z], [x, y-s*.5, z], [x, y, z+s], [x, y, z-s]]
        for a_, b_ in [(0, 4), (4, 1), (1, 5), (5, 0)]:
            tris += [[base+2, base+a_, base+b_][::-1], [base+3, base+a_, base+b_]]
    return pos, tris


_BOX_T = [[0, 2, 1], [0, 3, 2], [4, 5, 6], [4, 6, 7], [0, 1, 5], [0, 5, 4], [1, 2, 6], [1, 6, 5],
          [2, 3, 7], [2, 7, 6], [3, 0, 4], [3, 4, 7]]


def _box(x, y, z, sx, sy, sz):
    return [[x, y, z], [x+sx, y, z], [x+sx, y, z+sz], [x, y, z+sz],
            [x, y+sy, z], [x+sx, y+sy, z], [x+sx, y+sy, z+sz], [x, y+sy, z+sz]]


def prototypes():
    """Custom ground-cover meshes at 10 cm scale, in the shared roles."""
    out = {}
    rnd = random.Random(7)
    p, t, c = _blades(rnd, 24, .028, .06, .35)
    out['grass'] = ([dict(role='foliage', positions=p, triangles=t, tints=c)], [.14, .028, .14])
    p, t, c = _blades(rnd, 12, .022, .045, .35)
    hp, ht = _heads(rnd, 7, .026, .045, .0045)
    out['wildflowers'] = ([dict(role='foliage', positions=p, triangles=t, tints=c),
                           dict(role='cloth', positions=hp, triangles=ht)], [.11, .03, .11])
    p, t, c = _blades(rnd, 12, .02, .045, .35)
    hp, ht = _heads(rnd, 8, .024, .045, .004)
    out['daisies'] = ([dict(role='foliage', positions=p, triangles=t, tints=c),
                       dict(role='snow', positions=hp, triangles=ht)], [.1, .028, .1])
    p, t, c = _blades(rnd, 12, .02, .04, .35)
    hp, ht = _heads(rnd, 7, .023, .04, .004)
    out['buttercups'] = ([dict(role='foliage', positions=p, triangles=t, tints=c),
                          dict(role='gravel', positions=hp, triangles=ht, tints=[[1, .92, .25, 1]]*len(hp))],
                         [.09, .027, .09])
    p, t, c = _blades(rnd, 14, .016, .055, .9)
    hp, ht = _heads(rnd, 22, .016, .055, .0045)
    out['heather'] = ([dict(role='foliage', positions=p, triangles=t, tints=[[.55, .5, .35, 1]]*len(p)),
                       dict(role='cloth', positions=hp, triangles=ht, tints=[[.8, .55, 1, 1]]*len(hp))],
                      [.13, .022, .13])
    p, t, c = _blades(rnd, 14, .07, .03, .25)
    out['reeds'] = ([dict(role='foliage', positions=p, triangles=t,
                          tints=[[.85, .9, .55, 1] if k % 3 < 2 else [1, .95, .6, 1] for k in range(len(p))])],
                    [.08, .07, .08])
    # A woodpile: logs stacked beside a grove or workshop.
    logs = []
    for row, (n, y) in enumerate([(3, .012), (2, .032)]):
        for k in range(n):
            x0 = (k-(n-1)/2)*.026
            seg = 8
            pos = []
            tri = []
            for e, zz in enumerate((-.05, .05)):
                for s in range(seg):
                    a = math.tau*s/seg
                    pos.append([x0+.012*math.cos(a), y+.012*math.sin(a), zz])
            for s in range(seg):
                a, b = s, (s+1) % seg
                tri += [[a, b, seg+b], [a, seg+b, seg+a]]
            tri += [[0, (s+1) % seg, s] for s in range(1, seg-1)]
            tri += [[seg, seg+s, seg+(s+1) % seg] for s in range(1, seg-1)]
            logs.append(dict(role='bark', positions=pos, triangles=tri))
    out['woodpile'] = (logs, [.09, .044, .1])
    # A drying line: two posts, a line and the washing hung out in the sun.
    line = [dict(role='timber', positions=_box(x, .0, -.006, .012, .2, .012), triangles=_BOX_T)
            for x in (-.174, .162)]
    line.append(dict(role='wood', positions=_box(-.17, .186, -.0015, .344, .003, .003), triangles=_BOX_T))
    for k, (x0, w, drop, tint) in enumerate([(-.14, .07, .07, [1, .93, .75, 1]), (-.055, .06, .09, [.65, .8, 1, 1]),
                                             (.03, .08, .065, [1, .75, .7, 1]), (.12, .045, .08, [1, 1, .95, 1])]):
        pos, tri = [], []
        for j in range(4):
            for i in range(4):
                pos.append([x0+w*i/3, .186-drop*j/3, .006*math.sin(i*1.7+k)*j/3])
                if i and j:
                    a_ = j*4+i
                    tri += [[a_-5, a_-1, a_], [a_-5, a_, a_-4], [a_-5, a_, a_-1], [a_-5, a_-4, a_]]
        line.append(dict(role='cloth', positions=pos, triangles=tri, tints=[tint]*len(pos)))
    out['drying_line'] = (line, [.36, .2, .03])
    # A footbridge: two stringers and a plank deck, level, no rails to trip on.
    deck = [dict(role='timber', positions=_box(x, 0., -.15, .016, .02, .3), triangles=_BOX_T) for x in (-.07, .054)]
    for k in range(12):
        deck.append(dict(role='wood', positions=_box(-.08, .02, -.15+k*.025+.001, .16, .01, .023),
                         triangles=_BOX_T, tints=[[1, .9-.04*(k % 3), .8, 1]]*8))
    out['footbridge'] = (deck, list(BRIDGE))
    return {k: (_fit(v[0], v[1]), v[1]) for k, v in out.items()}


# ---------------------------------------------------------------- surfaces
def surface(grid, h, base, slope, owner, objects, shadow, wet, acc, outside, paths, pads, fields, noise, skirt=None):
    """Per-vertex (role weights, tint): slope, height, water, light and use."""
    out_w = []
    out_t = []
    nx = grid.nx
    wall_tint = (.98, .93, .86)
    for q in range(grid.n):
        x, z = grid.xs[q % nx], grid.zs[q//nx]
        s = slope[q]
        o = objects[owner[q]] if owner[q] >= 0 else None
        rel = h[q]-base[q]
        soft = o is None or o['form'] in SOFT
        rock_role = o['rock_role'] if o else 'cliff'
        rock_tint = o['tint'] if o else wall_tint
        if outside[q] and (o is None or rel > .02):
            rock_role, rock_tint = ('cliff', wall_tint) if o is None else (rock_role, rock_tint)
        n1 = noise(x*5.3+11, z*5.3)
        veg_limit = (43 if o is None else 45 if soft else 28)+4*n1
        bare = smooth((s-veg_limit+5)/10)
        rock = bare*smooth((s-(50 if soft and o is not None else 40)+6*n1)/12)
        scree = bare-rock
        veg = 1-bare
        sh = shadow[q]
        w = wet[q]
        moist = smooth((.45-w)/.45)
        moss = veg*clamp(smooth((sh-.35)/.4)*.75+moist*smooth((s-8)/14)*.35)
        soil = veg*smooth((s-veg_limit+13)/8)*.65
        gravel = veg*smooth((.035-w)/.03)*.9 if w < .035 else 0.
        meadow = max(0., veg-moss-soil-gravel)
        weights = {'meadow': meadow, 'moss': moss, 'soil': soil, 'gravel': gravel,
                   'scree': scree, rock_role: rock+(0 if rock_role != 'scree' else 0)}
        if fields[q] > 0:
            f = fields[q]
            row = .5+.5*math.sin((x*math.cos(.6)+z*math.sin(.6))*95)
            for k in weights:
                weights[k] *= 1-f
            weights['soil'] = weights.get('soil', 0)+f*row
            weights['meadow'] = weights.get('meadow', 0)+f*(1-row)
        p = max(paths[q], pads[q])
        if p > 0:
            for k in weights:
                weights[k] *= 1-p
            weights['worn_path'] = p
        if h[q] > SNOWLINE:
            sn = smooth((h[q]-SNOWLINE)/.22+.3*n1)*(1-smooth((s-58)/10))
            for k in weights:
                weights[k] *= 1-sn
            weights['snow'] = sn
        # Tints: dry high grass goes gold, lush low meadow deep green;
        # rock and soil carry the source object's colour.
        lush = clamp(moist*.7+smooth((.25-rel)/.25)*.3+.15*n1)
        if outside[q]:
            lush = lush*.6
        tints = {'meadow': (lerp(.97, .86, lush), lerp(.86, 1., lush), lerp(.55, .78, lush)),
                 'moss': (.9, 1., .9), 'soil': tuple(lerp(1, c, .35) for c in rock_tint),
                 'gravel': (1., .97, .92), 'scree': tuple(lerp(1, c, .6) for c in rock_tint),
                 'cliff': rock_tint, 'rock': rock_tint, 'worn_path': (1., .96, .9),
                 'snow': (1., 1., 1.)}
        total = sum(weights.values()) or 1.
        top = sorted(((v, k) for k, v in weights.items() if v > 1e-4), reverse=True)[:2]
        if not top:
            top = [(1., 'meadow')]
        tsum = sum(v for v, _ in top)
        tint = [0., 0., 0.]
        for v, k in top:
            for i in range(3):
                tint[i] += tints[k][i]*v/tsum
        weights = {k: v/tsum for v, k in top}
        sk = skirt[q] if skirt else 0.
        if sk > 0:
            # Where the ridge has run down into the shared surround, take on
            # its moss and plain tint so the two meet without a seam.
            weights = {k: v*(1-sk) for k, v in weights.items()}
            weights['moss'] = weights.get('moss', 0.)+sk
            top2 = sorted(((v, k) for k, v in weights.items()), reverse=True)[:2]
            t2 = sum(v for v, _ in top2) or 1.
            weights = {k: v/t2 for v, k in top2}
            tint = [lerp(c, 1., sk) for c in tint]
        out_w.append(weights)
        out_t.append(tuple(round(clamp(c), 4) for c in tint))
        del total
    return out_w, out_t


def build_mesh(grid, h, weights, tints, tris):
    """Group triangles by their two dominant roles into blended primitives."""
    groups = {}
    for t in tris:
        score = {}
        for q in t:
            for k, v in weights[q].items():
                score[k] = score.get(k, 0.)+v
        ranked = sorted(score.items(), key=lambda kv: (-kv[1], kv[0]))
        a = ranked[0][0]
        b = ranked[1][0] if len(ranked) > 1 and ranked[1][1] > .05 else None
        groups.setdefault((a, b), []).append(t)
    prims = []
    nx = grid.nx
    for (a, b) in sorted(groups, key=lambda k: (k[0], k[1] or '')):
        index = {}
        pos, tint, blend, out = [], [], [], []
        for t in groups[(a, b)]:
            loc = []
            for q in t:
                if q not in index:
                    index[q] = len(pos)
                    pos.append([grid.xs[q % nx], round(h[q], 5), grid.zs[q//nx]])
                    tint.append([*tints[q], 1])
                    wa = weights[q].get(a, 0.)
                    wb = weights[q].get(b, 0.) if b else 0.
                    blend.append(round(wb/(wa+wb), 4) if wa+wb > 0 else (1. if b else 0.))
                loc.append(index[q])
            out.append(loc)
        p = dict(role=a, positions=pos, triangles=out, tints=tint)
        if b:
            p['blend_role'] = b
            p['blend_weights'] = blend
        prims.append(p)
    return prims


# ---------------------------------------------------------------- people
def find_hamlet(grid, h, slope, wet, inside, spawns, avoid, indist, reachable=None):
    """The best site where water, flat ground and shelter meet; when a room
    offers none, the demands relax a step at a time (steeper ground, farther
    from water) before giving up."""
    for flat_max, far_water, near in ((14, 1.2, .9), (17, 1.8, .9), (20, 3., .9), (24, 9., .6)):
        site = _find_hamlet(grid, h, slope, wet, inside, spawns, avoid, indist, reachable, flat_max, far_water, near)
        if site is not None:
            return site
    return None


def _find_hamlet(grid, h, slope, wet, inside, spawns, avoid, indist, reachable, flat_max, far_water, near_spawn=.9):
    nx = grid.nx
    best = None
    for j in range(0, grid.nz, 2):
        for i in range(0, nx, 2):
            q = j*nx+i
            if not inside[q]:
                continue
            x, z = grid.xs[i], grid.zs[j]
            if indist(x, z) < (.6 if near_spawn > .8 else .45):
                continue
            w = wet[q]
            if w < .25 or w > far_water:
                continue
            if any(math.hypot(x-s[0], z-s[2]) < near_spawn for s in spawns):
                continue
            if not avoid(x, z) or (reachable is not None and q not in reachable):
                continue
            flat = 0.
            rx, rz = grid.span(x-.4, x+.4, z-.4, z+.4)
            for jj in rz:
                for ii in rx:
                    flat = max(flat, slope[jj*nx+ii])
            if flat > flat_max:
                continue
            shelter = 0
            for k in range(12):
                a = math.tau*k/12
                rise = max(grid.sample(h, x+math.cos(a)*r, z+math.sin(a)*r) for r in (.6, .9, 1.2))-h[q]
                shelter += rise > .22
            near = min(math.hypot(x-s[0], z-s[2]) for s in spawns)
            score = .25*min(shelter, 6)-abs(w-.45)*1.2-flat*.03-.12*max(0., near-2.4)
            if best is None or score > best[0]+1e-9:
                best = (score, x, z)
    return None if best is None else (best[1], best[2])


def inside_distance_q(grid, inside, i, j):
    return min(i, j, grid.nx-1-i, grid.nz-1-j)*grid.cell if inside[j*grid.nx+i] else 0.


def flatten(grid, h, x, z, hx, hz, yaw, margin=.07, blend=.12, level=None):
    """Level a house pad to its mean height (or a given level) and feather it into the land."""
    a = math.radians(yaw)
    c, s = math.cos(a), math.sin(a)
    R = max(hx, hz)+margin+blend
    rx, rz = grid.span(x-R, x+R, z-R, z+R)
    inner = []
    for j in rz:
        for i in rx:
            dx, dz = grid.xs[i]-x, grid.zs[j]-z
            lx, lz = c*dx-s*dz, s*dx+c*dz
            if abs(lx) <= hx+margin and abs(lz) <= hz+margin:
                inner.append(h[j*grid.nx+i])
    if level is None:
        level = sorted(inner)[len(inner)//2]
    for j in rz:
        for i in rx:
            dx, dz = grid.xs[i]-x, grid.zs[j]-z
            lx, lz = c*dx-s*dz, s*dx+c*dz
            d = max(abs(lx)-hx-margin, abs(lz)-hz-margin, 0.)
            q = j*grid.nx+i
            t = smooth(d/blend)
            h[q] = lerp(level, h[q], t)
    return level


def walk_graph(grid, h, face_slope, wet, inside, blocked_mask, limit=WALK_LIMIT_DEG):
    nx = grid.nx

    def blocked(q, p):
        if not inside[p] or blocked_mask[p] or wet[p] < .03 or face_slope[p] > limit:
            return True
        return abs(h[p]-h[q]) > STEP_LIMIT_M
    return blocked


def walk(grid, h, face_slope, wet, inside, blocked_mask, a, b, limit=WALK_LIMIT_DEG):
    nx = grid.nx
    cost = [1.+3*(face_slope[q]/max(limit, 1))**2 for q in range(grid.n)]
    ia, ja = grid.nearest(*a)
    ib, jb = grid.nearest(*b)
    path = dijkstra(grid, cost, ja*nx+ia, [jb*nx+ib], 1,
                    walk_graph(grid, h, face_slope, wet, inside, blocked_mask, limit))
    return path


def paint_path(grid, path, mask, width=.035):
    nx = grid.nx
    pts = [(grid.xs[q % nx], grid.zs[q//nx]) for q in path]
    for x, z in pts[::2]:
        rx, rz = grid.span(x-width*2, x+width*2, z-width*2, z+width*2)
        for j in rz:
            for i in rx:
                d = math.hypot(grid.xs[i]-x, grid.zs[j]-z)
                v = 1-smooth((d-width)/width)
                q = j*nx+i
                if v > mask[q]:
                    mask[q] = v


ROAD_GRADE_DEG = 12.


BRIDGE = [.16, .03, .3]   # footbridge prototype: width, deck depth, length (m)


def build_road(grid, h, wet, inside, blocked, pads, a, b, width=.085, crossable=None, bridges=None):
    """A worn path is built, not found: route the easiest way, then grade it
    (cut and fill) so it climbs no steeper than ROAD_GRADE_DEG. Where it must
    cross a stream (never a lake), it crosses once, square to the water, on a
    level timber footbridge; the banks are filled up to the deck at each end."""
    nx = grid.nx
    s = slope_field(grid, h)
    cost = [1+8*(s[q]/25)**2+(60 if s[q] > 38 else 0) for q in range(grid.n)]
    if crossable is not None:
        for q in range(grid.n):
            if wet[q] < .24 and crossable(q):
                cost[q] += 40

    def blocked_fn(q, p):
        if (not inside[p]) or blocked[p]:
            return True
        return wet[p] < .24 and not (crossable is not None and crossable(p))

    ia, ja = grid.nearest(*a)
    ib, jb = grid.nearest(*b)
    path = dijkstra(grid, cost, ja*nx+ia, [jb*nx+ib], 1, blocked_fn)
    if path is None:
        return None
    pts = resample(chaikin([(grid.xs[q % nx], grid.zs[q//nx]) for q in path], 2), .03)
    g = [grid.sample(h, x, z) for x, z in pts]
    for _ in range(4):
        g = [sum(g[max(0, k-6):k+7])/len(g[max(0, k-6):k+7]) for k in range(len(g))]
    m = math.tan(math.radians(ROAD_GRADE_DEG))*.03
    wq = [wet[grid.nearest(x, z)[1]*nx+grid.nearest(x, z)[0]] for x, z in pts]
    decks = []
    k = 0
    while k < len(pts):
        if wq[k] >= .02:
            k += 1
            continue
        k1 = k
        while k1+1 < len(pts) and wq[k1+1] < .08:
            k1 += 1
        a_, b_ = k, k1
        while a_ > 0 and wq[a_] < .1:
            a_ -= 1
        while b_ < len(pts)-1 and wq[b_] < .1:
            b_ += 1
        top = max(grid.sample(h, *pts[a_]), grid.sample(h, *pts[b_]))+.012
        decks.append((a_, b_, top))
        k = b_+1
    for a_, b_, top in decks:
        for kk in range(a_, b_+1):
            g[kk] = top-.006
    for _ in range(2):
        for k in range(1, len(g)):
            g[k] = min(max(g[k], g[k-1]-m), g[k-1]+m)
        for k in range(len(g)-2, -1, -1):
            g[k] = min(max(g[k], g[k+1]-m), g[k+1]+m)
    if bridges is not None:
        for a_, b_, top in decks:
            (xa, za), (xb, zb) = pts[a_], pts[b_]
            length = math.hypot(xb-xa, zb-za)+.08
            bridges.append(dict(x=(xa+xb)/2, z=(za+zb)/2, yaw=math.degrees(math.atan2(xb-xa, zb-za)),
                                length=length, top=top))
    best = {}
    reach = width+.1
    for k, (x, z) in enumerate(pts):
        rx, rz = grid.span(x-reach, x+reach, z-reach, z+reach)
        for j in rz:
            for i in rx:
                d = math.hypot(grid.xs[i]-x, grid.zs[j]-z)
                q = j*nx+i
                if d <= reach and (q not in best or d < best[q][0]):
                    best[q] = (d, g[k])
    for q in sorted(best):
        d, level = best[q]
        if pads[q] > .3 or wet[q] < .1:
            continue
        w = 1-smooth((d-width)/(reach-width))
        h[q] = lerp(h[q], level, w)
    return pts


def grade_line(grid, h, wet, pads, pts, width=.085):
    """Cut and fill along a line of points so a body's lane follows it at no
    more than ROAD_GRADE_DEG and level across (used where a walk proved the
    land too steep for the way a path must go)."""
    nx = grid.nx
    g = [grid.sample(h, x, z) for x, z in pts]
    for _ in range(4):
        g = [sum(g[max(0, k-6):k+7])/len(g[max(0, k-6):k+7]) for k in range(len(g))]
    m = math.tan(math.radians(ROAD_GRADE_DEG))*.025
    for k in range(1, len(g)):
        g[k] = min(max(g[k], g[k-1]-m), g[k-1]+m)
    for k in range(len(g)-2, -1, -1):
        g[k] = min(max(g[k], g[k+1]-m), g[k+1]+m)
    best = {}
    reach = width+.1
    for k, (x, z) in enumerate(pts):
        rx, rz = grid.span(x-reach, x+reach, z-reach, z+reach)
        for j in rz:
            for i in rx:
                d = math.hypot(grid.xs[i]-x, grid.zs[j]-z)
                q = j*nx+i
                if d <= reach and (q not in best or d < best[q][0]):
                    best[q] = (d, g[k])
    for q in sorted(best):
        d, level = best[q]
        if pads[q] > .3 or wet[q] < .1:
            continue
        h[q] = lerp(h[q], level, 1-smooth((d-width)/(reach-width)))


def paint_line(grid, pts, mask, width=.035):
    nx = grid.nx
    for x, z in pts[::2]:
        rx, rz = grid.span(x-width*2, x+width*2, z-width*2, z+width*2)
        for j in rz:
            for i in rx:
                d = math.hypot(grid.xs[i]-x, grid.zs[j]-z)
                v = 1-smooth((d-width)/width)
                q = j*nx+i
                if v > mask[q]:
                    mask[q] = v


def reachable_from(grid, h, wet, inside, start, max_slope=38., crossable=None):
    """Ground a built path could reach from the spawn: dry, or across a stream
    a footbridge can span (never across a lake)."""
    nx = grid.nx
    s = slope_field(grid, h)
    i, j = grid.nearest(*start)
    seen = {j*nx+i}
    stack = [j*nx+i]
    while stack:
        q = stack.pop()
        i, j = q % nx, q//nx
        for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ii, jj = i+di, j+dj
            if 0 <= ii < nx and 0 <= jj < grid.nz:
                p = jj*nx+ii
                if p not in seen and inside[p] and s[p] <= max_slope and                         (wet[p] >= .24 or (crossable is not None and crossable(p))):
                    seen.add(p)
                    stack.append(p)
    return seen
