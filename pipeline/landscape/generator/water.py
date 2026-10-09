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


def clear_hollow(room, objects, spawns, x, z):
    """How far a pond centred here could spread before it met a wall, a
    landform's foot, a spawn's glade or the land's middle (metres)."""
    lo, hi, _ = room_shape(room)
    cx, cz = (lo[0]+hi[0])/2, (lo[2]+hi[2])/2
    free = inside_distance(room, x, z)-.45
    for o in objects:
        if o['parent']:
            continue
        lx, lz = local(o, x, z)
        free = min(free, rr_dist(lx, lz, o['sx']/2, o['sz']/2, .05)-(.25+.45*o['sy']))
    for s in spawns:
        free = min(free, math.hypot(x-s[0], z-s[2])-.9)
    return min(free, math.hypot(x-cx, z-cz)-.55)


def water_character(room, objects, spawns, setup):
    """What the room suggests: a deep tarn where its floor leaves a broad
    clear hollow, a dry upland with only a rill where it is small and crowded,
    otherwise a river with deep pools. Rivers and lakes are opportunities,
    not required features. Returns (character, why)."""
    if setup['water'] == 'none':
        return 'none', 'the player chose no water'
    lo, hi, _ = room_shape(room)
    step = .09
    broad = -1.
    for a in range(int((hi[0]-lo[0])/step)+1):
        for b in range(int((hi[2]-lo[2])/step)+1):
            broad = max(broad, clear_hollow(room, objects, spawns, lo[0]+a*step, lo[2]+b*step))
    area = (hi[0]-lo[0])*(hi[2]-lo[2])
    cover = sum(o['sx']*o['sz'] for o in objects if not o['parent'])/area
    need = {'a_little': 9., 'some': .8, 'plenty': .65}.get(setup['water'], .8)
    why = 'clear hollow %.2f m, floor %.0f%% covered' % (broad, 100*cover)
    if broad >= need:
        return 'tarn', why
    if broad < .55 and cover >= .2 and setup['water'] != 'plenty':
        return 'dry', why
    return 'river', why


def choose_lake(room, objects, spawns, peak, outlet, setup):
    lo, hi, _ = room_shape(room)
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
            free = clear_hollow(room, objects, spawns, x, z)
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
            # The second-lowest bank: a single narrow gully crossing the bank
            # is closed by the bank carve() raises, rather than draining the
            # whole river into it.
            gs = sorted(grid.sample(h, a, b) for a, b in samples)
            g = gs[1] if len(gs) >= 3 else gs[0]
            W = min(W, g-FREEBOARD)
        W = max(W, floor_level)
        out.append(W)
    return out


def _each(v, n):
    return list(v) if isinstance(v, (list, tuple)) else [v]*n


def carve(grid, h, pts, W, half, depth, wet, bank=.6, keep=None):
    """Lower the bed under the surface and shape banks by the water's passage.
    `half` and `depth` may vary along the stream (a pool widens and deepens
    it); `keep(q)` marks ground another water body shapes (a tarn's bed)."""
    nx = grid.nx
    halves, depths = _each(half, len(pts)), _each(depth, len(pts))
    best = {}
    for k, (x, z) in enumerate(pts):
        reach_m = halves[k]+.16
        rx, rz = grid.span(x-reach_m, x+reach_m, z-reach_m, z+reach_m)
        for j in rz:
            for i in rx:
                d = math.hypot(grid.xs[i]-x, grid.zs[j]-z)
                if d > reach_m:
                    continue
                q = j*nx+i
                if q not in best or d < best[q][0]:
                    best[q] = (d, k)
    for q in sorted(best):
        d, k = best[q]
        level, hw, dp = W[k], halves[k], depths[k]
        if keep is not None and keep(q):
            continue
        if d < hw:
            bed = level-dp*(1-(d/hw)**2)-.004
            h[q] = min(h[q], bed)
        else:
            h[q] = min(h[q], level+FREEBOARD*.5+bank*(d-hw))
            h[q] = max(h[q], level+.004)
        wet[q] = min(wet[q], max(0., d-hw))


def ribbon(pts, W, half):
    """Level cross-sections; returns positions in [left, right] pairs."""
    halves = _each(half, len(pts))
    pos = []
    for k, (x, z) in enumerate(pts):
        a = pts[max(0, k-1)]
        b = pts[min(len(pts)-1, k+1)]
        tx, tz = b[0]-a[0], b[1]-a[1]
        L = math.hypot(tx, tz) or 1.
        nx_, nz_ = -tz/L, tx/L
        hw = halves[k]
        pos.append([x+nx_*hw, W[k], z+nz_*hw])
        pos.append([x-nx_*hw, W[k], z-nz_*hw])
    return pos


def ribbon_meshes(name, pts, W, half, kind_split=40.):
    """Split a stream into flowing and falling runs by surface gradient."""
    halves = _each(half, len(pts))
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
        pos = ribbon(p, w, [halves[k]*(1.25 if fall else 1.) for k in idx])
        tris = []
        for k in range(len(idx)-1):
            a, b, c, d = 2*k, 2*k+1, 2*k+2, 2*k+3
            tris += [[a, c, d], [a, d, b]]
        meshes.append(dict(name=f'{name}_{n}', kind='falling' if fall else 'flowing',
                           positions=[[round(v, 5) for v in q] for q in pos], triangles=tris))
    return meshes


# ---------------------------------------------------------------- deep water
SWIM_DEPTH = (.14, .24)    # a deep middle well over the 10 cm body's head
WADE_DEPTH = .06           # a shelving shore's wading shelf ends about here
POOL_DEPTH = (.12, .17)    # a river's deep pools, over the head too


def pond_shelves(grid, h, lake, noise, spawns, inflow):
    """Where the tarn shelves and where it drops off, as a weight per angle in
    the lake's own frame (1 shelving, 0 a drop-off). The bed continues the
    land: it shelves where the shore is low open ground (the side a walker
    comes from) and at the brook's delta, and drops off under rising banks."""
    a = math.radians(lake['yaw'])
    c, s = math.cos(a), math.sin(a)

    def world(t, rho):
        U, V = rho*math.cos(t)*lake['rx'], rho*math.sin(t)*lake['rz']
        return lake['x']+c*U+s*V, lake['z']-s*U+c*V

    def frame_angle(x, z):
        dx, dz = x-lake['x'], z-lake['z']
        return math.atan2((s*dx+c*dz)/lake['rz'], (c*dx-s*dz)/lake['rx'])
    ts = [math.tau*k/36 for k in range(36)]
    rise = [sum(grid.sample(h, *world(t, r)) for r in (1.3, 1.6, 1.9))/3-lake['level'] for t in ts]
    near = min(spawns, key=lambda p: math.hypot(p[0]-lake['x'], p[2]-lake['z']))
    t_walk = frame_angle(near[0], near[2])
    k_shelf = min(range(36), key=lambda k: (rise[k]+.04*(1-math.cos(ts[k]-t_walk)), k))
    t_shelf = ts[k_shelf]
    t_in = frame_angle(*inflow) if inflow else None

    def bump(t, centre, half_width):
        d = abs(math.atan2(math.sin(t-centre), math.cos(t-centre)))
        return 1-smooth((d-half_width*.5)/half_width)

    def weight(t):
        w = bump(t, t_shelf, math.radians(70))
        if t_in is not None:
            w = max(w, .75*bump(t, t_in, math.radians(30)))
        return w
    return weight, frame_angle, dict(shelf_deg=round(math.degrees(t_shelf) % 360, 1),
                                     inflow_deg=None if t_in is None else round(math.degrees(t_in) % 360, 1))


def _shelf(w):
    """(shelf slope, shelf depth, drop slope) for a shelving weight w."""
    return (lerp(math.tan(math.radians(26)), math.tan(math.radians(10)), w), lerp(.025, WADE_DEPTH, w),
            lerp(math.tan(math.radians(58)), math.tan(math.radians(30)), w))


def pond_reach(w, deep):
    """How far in from the shore the profile reaches the deep middle."""
    shelf_slope, shelf_depth, drop_slope = _shelf(w)
    return shelf_depth/shelf_slope+max(0., deep-shelf_depth)/drop_slope


def pond_depth(d, w, deep):
    """Depth below the surface at d metres in from the shore: a shelf (gentle
    where it shelves, a narrow ledge at a drop-off), then a drop to the deep
    middle, met with a soft knee."""
    shelf_slope, shelf_depth, drop_slope = _shelf(w)
    shelf_w = shelf_depth/shelf_slope
    if d <= shelf_w:
        raw = shelf_slope*d
    else:
        raw = shelf_depth+drop_slope*(d-shelf_w)
    k = .03   # soft minimum against the deep middle
    if raw >= deep+k:
        return deep
    if raw <= deep-k:
        return raw
    t = (raw-(deep-k))/(2*k)
    return lerp(raw, deep, t*t)


def channel_cells(grid, streams, margin=.05):
    """Grid vertices a stream's channel runs through (its banks stay open)."""
    nx = grid.nx
    out = set()
    for st in streams:
        halves = _each(st['half'], len(st['pts']))
        for (x, z), hw in zip(st['pts'], halves):
            r = hw+margin
            rx, rz = grid.span(x-r, x+r, z-r, z+r)
            out.update(j*nx+i for j in rz for i in rx if math.hypot(grid.xs[i]-x, grid.zs[j]-z) <= r)
    return out


def deepen_tarn(grid, h, lake, noise, spawns, inflow, setup, wet, channel=frozenset()):
    """Carve the tarn's bed: shelving shores to wade in from and walk out of,
    drop-offs under rising banks, and a deep middle to swim in. The surface
    stays where its lowest rim holds it; only the bed moves."""
    nx = grid.nx
    level = lake['level']
    R = max(lake['rx'], lake['rz'])*1.5
    rx_, rz_ = grid.span(lake['x']-R, lake['x']+R, lake['z']-R, lake['z']+R)
    inner = {}
    for j in rz_:
        for i in rx_:
            r = lake_rho(lake, noise, grid.xs[i], grid.zs[j])
            if r < 1.:
                inner[j*nx+i] = r
    weight, frame_angle, info = pond_shelves(grid, h, lake, noise, spawns, inflow)

    def ray(t):
        """Metres from the centre to the shore along the lake-frame angle t."""
        wobble = 1+.12*noise(math.cos(t)*1.7+31, math.sin(t)*1.7+7)
        return wobble*math.hypot(lake['rx']*math.cos(t), lake['rz']*math.sin(t))
    radius = min(ray(math.tau*k/72) for k in range(72))
    extra = {'plenty': .03}.get(setup['water'], 0.)
    deep = clamp(.32*radius+.035+extra, *SWIM_DEPTH)
    deepest = 0.
    for q in sorted(inner):
        x, z = grid.xs[q % nx], grid.zs[q//nx]
        t = frame_angle(x, z)
        w = weight(t)
        R_t = ray(t)
        # Each ray reaches the deep middle by three quarters of the way in, so
        # a small pond steepens its shelf rather than losing its deep middle.
        squeeze = max(1., pond_reach(w, deep)/(.75*R_t))
        local_deep = deep*(1+.07*noise(x*3.1+41, z*3.1-17))
        depth = pond_depth((1-inner[q])*R_t*squeeze, w, local_deep)+.004
        h[q] = level-depth
        deepest = max(deepest, depth)
        wet[q] = 0.
    # A low cut bank stands over each drop-off; open shores stay low.
    band = {}
    for j in rz_:
        for i in rx_:
            q = j*nx+i
            if q in inner or q in channel:
                continue
            r = lake_rho(lake, noise, grid.xs[i], grid.zs[j])
            if r < 1.2:
                band[q] = r
    for q in sorted(band):
        x, z = grid.xs[q % nx], grid.zs[q//nx]
        w = weight(frame_angle(x, z))
        lip = .022*(1-w)*(1-smooth((band[q]-1.08)/.12))
        h[q] = max(h[q], level+.006+lip)
    lake.update(depth=round(deepest, 4), deep_target=round(deep, 4), **info)
    return lake


def pool_sites(room, objects, spawns, pts, W, count, avoid=None):
    """Deep pools where a river would scour them: below a steep reach where
    the water slows (a plunge pool) or on a sharp bend. Away from walls,
    landforms, spawns and each other."""
    n = len(pts)
    L = 12   # 0.3 m of stream either side
    cands = []
    for k in range(L, n-L):
        x, z = pts[k]
        if inside_distance(room, x, z) < .45:
            continue
        if any(math.hypot(x-s[0], z-s[2]) < .75 for s in spawns):
            continue
        if avoid is not None and avoid(x, z):
            continue
        clear = min([rr_dist(*local(o, x, z), o['sx']/2, o['sz']/2, .05) for o in objects if not o['parent']] or [9.])
        if clear < .45:   # the pool and its banks keep off a landform
            continue
        drop_up = W[k-L]-W[k]
        drop_down = W[k]-W[k+L]
        ax, az = pts[k][0]-pts[k-L][0], pts[k][1]-pts[k-L][1]
        bx, bz = pts[k+L][0]-pts[k][0], pts[k+L][1]-pts[k][1]
        turn = abs(math.atan2(ax*bz-az*bx, ax*bx+az*bz))
        score = drop_up-1.5*drop_down+.04*turn
        cands.append((score, -k, k))
    cands.sort(reverse=True)
    chosen = []
    for score, _, k in cands:
        if len(chosen) >= count:
            break
        if score <= .0:
            break
        if all(abs(k-c) > 40 for c in chosen):
            chosen.append(k)
    return sorted(chosen)


def shape_pools(pts, W, half, depth, sites, wet_room):
    """Widen and deepen the stream at each pool site into a level pool."""
    n = len(pts)
    halves, depths = [half]*n, [depth]*n
    W = list(W)
    pools = []
    for k in sites:
        span = 11
        k0, k1 = max(0, k-span), min(n-1, k+span)
        level = W[k1]
        for m in range(k0, k1+1):
            W[m] = level
        hp = .15+.03*wet_room
        dp = lerp(*POOL_DEPTH, wet_room)
        for m in range(k0, k1+1):
            t = (m-k0)/max(1, k1-k0)
            b = math.sin(math.pi*t)
            halves[m] = max(halves[m], half+(hp-half)*b**.7)
            depths[m] = max(depths[m], depth+(dp-depth)*b**.5)
        pools.append(dict(x=round(pts[k][0], 3), z=round(pts[k][1], 3), level=round(level, 4),
                          depth=round(dp+.004, 4), half_width=round(hp, 3)))
    return W, halves, depths, pools


def plan_and_carve(grid, room, objects, h, base, inside, spawns, setup, noise, outside_mask, sea=None):
    """Returns water records, the lake, stream polylines and a wetness map.
    With a sea (dict: level, goal(q), avoid [(x, z, r)]) the river runs to the
    sea, never below its level, and the distant lake is gone."""
    wet = [9.]*grid.n
    result = dict(meshes=[], lake=None, streams=[], wet=wet, notes=[], pools=[])
    character, why = water_character(room, objects, spawns, setup)
    result['character'] = character
    result['why'] = why
    if character == 'none':
        return result
    nx = grid.nx
    outlet = choose_outlet(room, spawns)
    peak_q = max((q for q in range(grid.n) if inside[q]), key=lambda q: (h[q], -q))
    peak = (grid.xs[peak_q % nx], grid.zs[peak_q//nx])
    lake = choose_lake(room, objects, spawns, peak, outlet, setup) if character == 'tarn' else None
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
    if character != 'tarn':
        # A river's spring seeps from a hillside as far from where it leaves as
        # the room allows, so it has the length to run, fall and pool.
        def off_landforms(x, z):
            return all(rr_dist(*local(o, x, z), o['sx']/2, o['sz']/2, .05) > .2 for o in objects if not o['parent'])
        seeps = [q for q in range(0, grid.n, 2) if inside[q] and .15 <= h[q]-base[q] <= .55
                 and inside_distance(room, grid.xs[q % nx], grid.zs[q//nx]) >= .3
                 and off_landforms(grid.xs[q % nx], grid.zs[q//nx])]
        if seeps:
            q = max(seeps, key=lambda q: (math.hypot(grid.xs[q % nx]-outlet['x'], grid.zs[q//nx]-outlet['z']), -q))
            spring = (grid.xs[q % nx], grid.zs[q//nx])
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
    floor_y = DISTANT_LAKE_Y if sea is None else sea['level']
    goal = reach_far if sea is None else sea['goal']
    sea_avoid = [] if sea is None else list(sea['avoid'])

    streams = []
    inflow = None
    if lake is not None:
        raw = route(grid, h, base, noise, spring, reach_lake, [(s[0], 0, s[2]) for s in spawns])
        pts = resample(chaikin(raw, 3), .025)
        cut = len(pts)
        for k, (x, z) in enumerate(pts):
            if lake_rho(lake, noise, x, z) < .97:
                cut = k+1
                break
        pts = pts[:cut]
        inflow = pts[-1]
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
    footprints = [(o['cx'], o['cz'], math.hypot(o['sx'], o['sz'])/2+.35) for o in objects if not o['parent']]
    raw = route(grid, h, base, noise, start, goal, [(s[0], 0, s[2]) for s in spawns],
                avoid=(footprints if character != 'tarn' else [])+sea_avoid)
    pts = resample(chaikin(raw, 3), .025)
    # A dry upland keeps only a rill; elsewhere a river.
    name, half, depth = ('rill', .035, .012) if character == 'dry' else ('river', .065, .028)
    if lake is not None:
        first = 0
        for k, (x, z) in enumerate(pts):
            if lake_rho(lake, noise, x, z) >= .95:   # the river starts in the lake, covering its mouth
                first = max(0, k-1)
                break
        pts = pts[first:]
        W = levels(grid, h, pts, half, start_level=lake['level']-.0005, floor_level=floor_y,
                   ignore=lambda x, z: lake_rho(lake, noise, x, z) < 1.03)
    else:
        W = levels(grid, h, pts, half, floor_level=floor_y)
    end = len(pts)
    for k, w in enumerate(W):
        if w <= floor_y+1e-6:
            end = k+1
            break
    pts, W = pts[:end], W[:end]
    halves, depths = half, depth
    if character != 'dry':
        # Deep pools where the river would scour them: the river's room gets a
        # few; below a tarn, at most one (the tarn is the deep water there).
        far_from_lake = None if lake is None else \
            (lambda x, z: lake_rho(lake, noise, x, z) < 1.9)
        sites = pool_sites(room, objects, spawns, pts, W, 1 if lake is not None else 3, far_from_lake)
        W, halves, depths, pools = shape_pools(pts, W, half, depth, sites, 0. if lake is not None else 1.)
        result['pools'] = pools
    streams.append(dict(name=name, pts=pts, W=W, half=halves, depth=depths))
    in_lake = None if lake is None else \
        (lambda q: lake_rho(lake, noise, grid.xs[q % nx], grid.zs[q//nx]) < 1.)
    for s in streams:
        carve(grid, h, s['pts'], s['W'], s['half'], s['depth'], wet, keep=in_lake)
    if lake is not None:
        deepen_tarn(grid, h, lake, noise, spawns, inflow, setup, wet, channel_cells(grid, streams))
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
    if sea is None:
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
