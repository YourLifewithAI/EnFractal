"""Water: snowmelt springs below the highest ground, a stream finds the low
ground, a tarn fills a hollow on the plain, and its outlet leaves through the
widest door's pass into a distant lake. Levels are fixed before carving, so a
surface is level across and never rises downstream."""
import math

from .field import clamp, lerp, rr_dist, smooth
from .land import dijkstra, inside_distance, local, openings, outside_distance, room_shape

FREEBOARD = .012
DISTANT_LAKE_Y = -.015


def lake_rho(lake, noise, x, z):
    dx, dz = x-lake['x'], z-lake['z']
    a = math.radians(lake['yaw'])
    c, s = math.cos(a), math.sin(a)
    u, v = (c*dx-s*dz)/lake['rx'], (s*dx+c*dz)/lake['rz']
    ang = math.atan2(v, u)
    wobble = 1+.12*noise(math.cos(ang)*1.7+31, math.sin(ang)*1.7+7)
    return math.hypot(u, v)/wobble


def choose_outlet(room, spawns):
    lo, hi, _ = room_shape(room)
    doors = sorted([o for o in openings(room) if 'door' in o['kind']],
                   key=lambda o: (-o['width'], o['id']))
    if doors:
        o = doors[0]
        half = max(0., o['width']/2-.35)
        best = None
        for k in range(21):
            t = -half+2*half*k/20
            x = o['x']+t if o['along'] == 'x' else o['x']
            z = o['z']+t if o['along'] == 'z' else o['z']
            score = min(math.hypot(x-s[0], z-s[2]) for s in spawns)
            if best is None or score > best[0]+1e-9:
                best = (score, x, z)
        x, z = best[1], best[2]
    else:
        mids = [((lo[0]+hi[0])/2, lo[2]), ((lo[0]+hi[0])/2, hi[2]), (lo[0], (lo[2]+hi[2])/2), (hi[0], (lo[2]+hi[2])/2)]
        x, z = max(mids, key=lambda p: (min(math.hypot(p[0]-s[0], p[1]-s[2]) for s in spawns), p))
    # Outward unit normal of that wall.
    d = [(abs(x-lo[0]), (-1, 0)), (abs(x-hi[0]), (1, 0)), (abs(z-lo[2]), (0, -1)), (abs(z-hi[2]), (0, 1))]
    normal = min(d)[1]
    return dict(x=x, z=z, nx=normal[0], nz=normal[1])


def choose_lake(room, objects, spawns, peak, outlet, setup):
    lo, hi, _ = room_shape(room)
    cx, cz = (lo[0]+hi[0])/2, (lo[2]+hi[2])/2
    size = {'a_little': .0, 'some': .75, 'plenty': 1.1}.get(setup['water'], 0)
    if size <= 0:
        return None
    best = None
    step = .09
    nxs = int((hi[0]-lo[0])/step)
    nzs = int((hi[2]-lo[2])/step)
    for a in range(nxs+1):
        for b in range(nzs+1):
            x, z = lo[0]+a*step, lo[2]+b*step
            free = inside_distance(room, x, z)-.45
            for o in objects:
                if o['parent']:
                    continue
                lx, lz = local(o, x, z)
                free = min(free, rr_dist(lx, lz, o['sx']/2, o['sz']/2, .05)-(.25+.45*o['sy']))
            for s in spawns:
                free = min(free, math.hypot(x-s[0], z-s[2])-.9)
            free = min(free, math.hypot(x-cx, z-cz)-.55)
            if free < .3:
                continue
            # Between the spring's peak and the outlet, nearer the high ground.
            px, pz = peak
            ox, oz = outlet['x'], outlet['z']
            line = abs((ox-px)*(pz-z)-(px-x)*(oz-pz))/max(1e-6, math.hypot(ox-px, oz-pz))
            toward_outlet = math.hypot(x-ox, z-oz)
            score = min(free, size)-.25*line+.05*min(toward_outlet, 3.)
            if best is None or score > best[0]+1e-9:
                best = (score, x, z, free)
    if best is None:
        return None
    # The whole shore stays inside the walls (rx reaches 1.15 r, the shore 1.35 rx).
    r = clamp(min(best[3]*.9, (best[3]+.45)/1.6), .3, size)
    return dict(x=best[1], z=best[2], rx=r*1.15, rz=r*.85, yaw=25.)


def resample(points, spacing):
    out = [points[0]]
    acc = 0.
    for a, b in zip(points, points[1:]):
        seg = math.hypot(b[0]-a[0], b[1]-a[1])
        if seg <= 1e-9:
            continue
        t = spacing-acc
        while t <= seg:
            out.append((a[0]+(b[0]-a[0])*t/seg, a[1]+(b[1]-a[1])*t/seg))
            t += spacing
        acc = seg-(t-spacing)
    if math.hypot(out[-1][0]-points[-1][0], out[-1][1]-points[-1][1]) > spacing*.3:
        out.append(points[-1])
    return out


def chaikin(points, rounds=3):
    for _ in range(rounds):
        new = [points[0]]
        for a, b in zip(points, points[1:]):
            new.append((.75*a[0]+.25*b[0], .75*a[1]+.25*b[1]))
            new.append((.25*a[0]+.75*b[0], .25*a[1]+.75*b[1]))
        new.append(points[-1])
        points = new
    return points


def route(grid, h, base, noise, start, goal_test, spawns, avoid=(), step=2):
    nx = grid.nx
    cost = [0.]*grid.n
    for q in range(grid.n):
        x, z = grid.xs[q % nx], grid.zs[q//nx]
        rel = max(0., h[q]-base[q]-.025)
        c = 1+30*rel+.45*(noise(x*2.7+50, z*2.7)+1)
        for s in spawns:
            c += 40*max(0., 1-math.hypot(x-s[0], z-s[2])/1.1)
        for ax, az, r in avoid:
            c += 20*max(0., 1-math.hypot(x-ax, z-az)/r)
        cost[q] = c
    i, j = grid.nearest(*start)
    i -= i % step
    j -= j % step
    goals = [q for q in range(grid.n) if (q % nx) % step == 0 and (q//nx) % step == 0 and goal_test(q)]
    path = dijkstra(grid, cost, j*nx+i, goals, step=step)
    if path is None:
        raise ValueError('no water route')
    return [(grid.xs[q % nx], grid.zs[q//nx]) for q in path]


def levels(grid, h, pts, half, start_level=None, floor_level=-1., ignore=None):
    """Water surface per sample: running minimum of the banks less a freeboard."""
    out = []
    W = math.inf if start_level is None else start_level
    for k, (x, z) in enumerate(pts):
        a = pts[max(0, k-1)]
        b = pts[min(len(pts)-1, k+1)]
        tx, tz = b[0]-a[0], b[1]-a[1]
        L = math.hypot(tx, tz) or 1.
        nx_, nz_ = -tz/L, tx/L
        samples = [(x+nx_*half*f, z+nz_*half*f) for f in (-1.3, -.6, 0, .6, 1.3)]
        if ignore is not None:
            samples = [p for p in samples if not ignore(*p)]
        if samples:
            g = min(grid.sample(h, a, b) for a, b in samples)
            W = min(W, g-FREEBOARD)
        W = max(W, floor_level)
        out.append(W)
    return out


def carve(grid, h, pts, W, half, depth, wet, bank=.6):
    """Lower the bed under the surface and shape banks by the water's passage."""
    nx = grid.nx
    best = {}
    reach_m = half+.16
    for k, (x, z) in enumerate(pts):
        rx, rz = grid.span(x-reach_m, x+reach_m, z-reach_m, z+reach_m)
        for j in rz:
            for i in rx:
                d = math.hypot(grid.xs[i]-x, grid.zs[j]-z)
                if d > reach_m:
                    continue
                q = j*nx+i
                if q not in best or d < best[q][0]:
                    best[q] = (d, W[k])
    for q in sorted(best):
        d, level = best[q]
        if d < half:
            bed = level-depth*(1-(d/half)**2)-.004
            h[q] = min(h[q], bed)
        else:
            h[q] = min(h[q], level+FREEBOARD*.5+bank*(d-half))
            h[q] = max(h[q], level+.004)
        wet[q] = min(wet[q], max(0., d-half))


def ribbon(pts, W, half):
    """Level cross-sections; returns positions in [left, right] pairs."""
    pos = []
    for k, (x, z) in enumerate(pts):
        a = pts[max(0, k-1)]
        b = pts[min(len(pts)-1, k+1)]
        tx, tz = b[0]-a[0], b[1]-a[1]
        L = math.hypot(tx, tz) or 1.
        nx_, nz_ = -tz/L, tx/L
        pos.append([x+nx_*half, W[k], z+nz_*half])
        pos.append([x-nx_*half, W[k], z-nz_*half])
    return pos


def ribbon_meshes(name, pts, W, half, kind_split=40.):
    """Split a stream into flowing and falling runs by surface gradient."""
    runs = []
    cur = [0]
    falling = None
    for k in range(1, len(pts)):
        ds = math.hypot(pts[k][0]-pts[k-1][0], pts[k][1]-pts[k-1][1]) or 1e-6
        f = math.degrees(math.atan((W[k-1]-W[k])/ds)) > kind_split
        if falling is None:
            falling = f
        if f != falling:
            runs.append((falling, cur))
            cur = [k-1]
            falling = f
        cur.append(k)
    runs.append((bool(falling), cur))
    meshes = []
    for n, (fall, idx) in enumerate(runs):
        if len(idx) < 2:
            continue
        p = [pts[k] for k in idx]
        w = [W[k] for k in idx]
        pos = ribbon(p, w, half*(1.25 if fall else 1.))
        tris = []
        for k in range(len(idx)-1):
            a, b, c, d = 2*k, 2*k+1, 2*k+2, 2*k+3
            tris += [[a, c, d], [a, d, b]]
        meshes.append(dict(name=f'{name}_{n}', kind='falling' if fall else 'flowing',
                           positions=[[round(v, 5) for v in q] for q in pos], triangles=tris))
    return meshes


def plan_and_carve(grid, room, objects, h, base, inside, spawns, setup, noise, outside_mask):
    """Returns water records, the lake, stream polylines and a wetness map."""
    wet = [9.]*grid.n
    result = dict(meshes=[], lake=None, streams=[], wet=wet, notes=[])
    if setup['water'] == 'none':
        return result
    nx = grid.nx
    peak_q = max((q for q in range(grid.n) if inside[q]), key=lambda q: (h[q], -q))
    peak = (grid.xs[peak_q % nx], grid.zs[peak_q//nx])
    outlet = choose_outlet(room, spawns)
    lake = choose_lake(room, objects, spawns, peak, outlet, setup)
    if lake is None:
        target = (outlet['x'], outlet['z'])
    else:
        target = (lake['x'], lake['z'])
    # Spring: on the peak's flank, toward the water's goal, where land falls to
    # about 40% of the peak's rise (snowmelt seeps out of the rock there).
    dx, dz = target[0]-peak[0], target[1]-peak[1]
    L = math.hypot(dx, dz) or 1
    spring_h = base[peak_q]+min(.55, .4*(h[peak_q]-base[peak_q]))
    spring = peak
    for k in range(1, 400):
        x, z = peak[0]+dx/L*k*.02, peak[1]+dz/L*k*.02
        if grid.sample(h, x, z) <= spring_h:
            spring = (x, z)
            break
    result['spring'] = spring
    result['outlet'] = outlet
    lo, hi, _ = room_shape(room)

    if lake is not None:
        # Lake level: just under the lowest point of its rim.
        rim = []
        for j in range(grid.nz):
            for i in range(nx):
                x, z = grid.xs[i], grid.zs[j]
                r = lake_rho(lake, noise, x, z)
                if 1.05 <= r <= 1.25:
                    rim.append(h[j*nx+i])
        level = min(rim)-FREEBOARD
        lake['level'] = round(level, 5)
        for j in range(grid.nz):
            for i in range(nx):
                x, z = grid.xs[i], grid.zs[j]
                r = lake_rho(lake, noise, x, z)
                q = j*nx+i
                if r < 1.0:
                    depth = .05*(1-r*r)**.6+.006
                    h[q] = min(h[q], level-depth)
                    wet[q] = 0.
                elif r < 1.35:
                    shore = level+.004+.10*(r-1.0)
                    if h[q] < level+.006:
                        h[q] = level+.006
                    h[q] = min(h[q], max(shore, level+.006))
                    wet[q] = min(wet[q], (r-1.0)*min(lake['rx'], lake['rz']))
        result['lake'] = lake

    # Inflow: spring to lake (or straight to the outlet when there is no lake).
    def reach_lake(q):
        x, z = grid.xs[q % nx], grid.zs[q//nx]
        return lake_rho(lake, noise, x, z) < .8

    def reach_far(q):
        x, z = grid.xs[q % nx], grid.zs[q//nx]
        return outside_mask[q] and outside_distance(room, x, z) > .55 and h[q] < DISTANT_LAKE_Y-.01 \
            and (x-outlet['x'])*outlet['nx']+(z-outlet['z'])*outlet['nz'] > .5

    streams = []
    if lake is not None:
        raw = route(grid, h, base, noise, spring, reach_lake, [(s[0], 0, s[2]) for s in spawns])
        pts = resample(chaikin(raw, 3), .025)
        cut = len(pts)
        for k, (x, z) in enumerate(pts):
            if lake_rho(lake, noise, x, z) < .97:
                cut = k+1
                break
        pts = pts[:cut]
        W = levels(grid, h, pts, .05, floor_level=lake['level'])
        # Ease the last stretch down onto the lake surface.
        total = 0.
        for k in range(len(pts)-1, -1, -1):
            if k < len(pts)-1:
                total += math.hypot(pts[k+1][0]-pts[k][0], pts[k+1][1]-pts[k][1])
            W[k] = max(lake['level']+.0005, min(W[k], lake['level']+.0005+.05*total))
        for k in range(1, len(W)):
            W[k] = min(W[k], W[k-1])
        streams.append(dict(name='brook', pts=pts, W=W, half=.05, depth=.022))
        start = (lake['x'], lake['z'])
    else:
        start = spring
    raw = route(grid, h, base, noise, start, reach_far, [(s[0], 0, s[2]) for s in spawns])
    pts = resample(chaikin(raw, 3), .025)
    if lake is not None:
        first = 0
        for k, (x, z) in enumerate(pts):
            if lake_rho(lake, noise, x, z) >= .95:   # the river starts in the lake, covering its mouth
                first = max(0, k-1)
                break
        pts = pts[first:]
        W = levels(grid, h, pts, .065, start_level=lake['level']-.0005, floor_level=DISTANT_LAKE_Y,
                   ignore=lambda x, z: lake_rho(lake, noise, x, z) < 1.03)
    else:
        W = levels(grid, h, pts, .065, floor_level=DISTANT_LAKE_Y)
    end = len(pts)
    for k, w in enumerate(W):
        if w <= DISTANT_LAKE_Y+1e-6:
            end = k+1
            break
    streams.append(dict(name='river', pts=pts[:end], W=W[:end], half=.065, depth=.028))
    for s in streams:
        carve(grid, h, s['pts'], s['W'], s['half'], s['depth'], wet)
    result['streams'] = streams
    # Water meshes.
    meshes = []
    if lake is not None:
        pos = {}
        tris = []
        for t in grid.triangles(lambda i, j: lake_rho(lake, noise, grid.xs[i]+grid.cell/2, grid.zs[j]+grid.cell/2) <= 1.06):
            idx = []
            for q in t:
                if q not in pos:
                    pos[q] = len(pos)
                idx.append(pos[q])
            tris.append(idx)
        verts = [None]*len(pos)
        for q, k in pos.items():
            verts[k] = [grid.xs[q % nx], lake['level'], grid.zs[q//nx]]
        meshes.append(dict(name='tarn', kind='still', positions=verts, triangles=tris))
    for s in streams:
        meshes += ribbon_meshes(s['name'], s['pts'], s['W'], s['half'])
    meshes.append(distant_lake(room, outlet, noise))
    result['meshes'] = meshes
    return result


def distant_lake(room, outlet, noise):
    """A broad lake beyond the pass the river leaves through (scenery water)."""
    lo, hi, _ = room_shape(room)
    cx, cz = (lo[0]+hi[0])/2, (lo[2]+hi[2])/2
    half = (abs(outlet['nx'])*(hi[0]-lo[0])+abs(outlet['nz'])*(hi[2]-lo[2]))/2
    ox = cx+outlet['nx']*(half+4.6)+(outlet['x']-cx)*abs(outlet['nz'])*.6
    oz = cz+outlet['nz']*(half+4.6)+(outlet['z']-cz)*abs(outlet['nx'])*.6
    ring = 48
    pos = [[ox, DISTANT_LAKE_Y, oz]]
    for k in range(ring):
        a = 2*math.pi*k/ring
        r = 4.4*(1+.18*noise(math.cos(a)*1.3+70, math.sin(a)*1.3))
        pos.append([round(ox+math.cos(a)*r*1.5, 4), DISTANT_LAKE_Y, round(oz+math.sin(a)*r, 4)])
    tris = [[0, 1+(k+1) % ring, 1+k] for k in range(ring)]
    return dict(name='distant_lake', kind='still', positions=pos, triangles=tris, scenery=True)
