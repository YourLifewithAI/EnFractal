"""Turn a corpus room into a landscape package.

    python -m pipeline.landscape.generator.generate --room <room folder> --out <empty folder>

Deterministic: the same room, inventory and setup answers give identical bytes.
"""
import argparse
import math
import random
from pathlib import Path

from pipeline.landscape.harness import write_package
from pipeline.landscape.harness.common import load_json
from pipeline.landscape.harness.kit import SIZES
from .field import Grid, Noise, clamp, distance_field, face_slope_max, lerp, slope_field, smooth
from .land import (FAR_Y, high_country, inside_distance, local, outside_distance, parse_objects, room_shape,
                   shell_land, sun_shadow, uplift, weather, rr_dist)
from .life import (BRIDGE, grade_line, STEP_LIMIT_M, WALK_LIMIT_DEG, build_mesh, build_road, find_hamlet, flatten, paint_line,
                   paint_path, prototypes, reachable_from, surface, walk)
from .water import DISTANT_LAKE_Y, choose_outlet, lake_rho, plan_and_carve, resample

GENERATOR = {'name': 'landscape-generator', 'version': '3'}
SEED = 20261008
CELL = .03
MARGIN = 2.1
REPO = Path(__file__).resolve().parents[3]
SETUP_PATH = REPO/'pipeline'/'landscape'/'harness'/'ab-setup.json'


def eyes(room):
    """Where the review eyes and figures stand, so planting keeps them clear."""
    lo, hi, _ = room_shape(room)
    cx, cz = (lo[0]+hi[0])/2, (lo[2]+hi[2])/2
    out = []
    for s in room['spawns']:
        x, _, z = s['position_m']
        ax, az = cx, cz
        if math.hypot(cx-x, cz-z) < 1.5:
            ax, az = max([(cx, lo[2]), (hi[0], cz), (cx, hi[2]), (lo[0], cz)],
                         key=lambda p: math.hypot(p[0]-x, p[1]-z))
        out.append((x, z, ax, az))
    wins = sorted([o for o in room['shell'].get('openings', []) if o['kind'] == 'window'], key=lambda w: w['id'])
    if wins:
        out.append((cx, cz, wins[0]['center_m'][0], wins[0]['center_m'][2]))
    else:
        out.append((cx, cz, cx, lo[2]))
    return out


def clear_of_eyes(views, x, z, radius, corridor=None):
    """A glade where a body arrives (each spawn) and at the land's middle; no
    sightline is kept open for any particular camera."""
    for ex, ez, ax, az in views:
        d = math.hypot(x-ex, z-ez)
        if d < radius:
            return False
    return True


def distant_hills(room, noise, outlet):
    """Unreachable hills all round beyond the boundary ridges, rising out of the
    shared surround; a gap opens where the river leaves for the distant lake.
    Shaped for a viewer anywhere, never to a camera."""
    lo, hi, _ = room_shape(room)
    cx, cz = (lo[0]+hi[0])/2, (lo[2]+hi[2])/2
    r0 = max(hi[0]-cx, hi[2]-cz)+MARGIN-.3
    radii = [r0+.55*k for k in range(30)]
    ring = 144
    pos, tri, tints, blend = [], [], [], []
    lake = dict(x=cx+outlet['nx']*7.6+(outlet['x']-cx)*abs(outlet['nz'])*.6,
                z=cz+outlet['nz']*8.1+(outlet['z']-cz)*abs(outlet['nx'])*.6)
    for r in radii:
        for k in range(ring):
            a = 2*math.pi*k/ring
            x, z = cx+math.cos(a)*r*1.05, cz+math.sin(a)*r
            env = smooth((r-r0-.2)/3.)
            hgt = env*(.9+1.6*max(0., noise.fbm(x*.17+3, z*.17-5, 4)+.25)+.5*noise.ridged(x*.3, z*.3))
            dl = math.hypot((x-lake['x'])/6.4, (z-lake['z'])/4.0)
            hgt *= smooth((dl-.95)/.6)
            pos.append([round(x, 4), round(FAR_Y+hgt, 4), round(z, 4)])
    for m in range(len(radii)-1):
        for k in range(ring):
            a = m*ring+k
            b = m*ring+(k+1) % ring
            c = (m+1)*ring+k
            d = (m+1)*ring+(k+1) % ring
            tri += [[a, b, d], [a, d, c]]
    for v in pos:
        y = v[1]-FAR_Y
        # Moss at the foot, like the shared surround it rises from; rock high up.
        blend.append(round(smooth((y-1.1)/.9), 4))
        g = clamp(.75+.1*noise(v[0], v[2]))
        f = smooth(y/.35)
        tints.append([round(lerp(1, g*.82, f), 4), round(lerp(1, g*.9, f), 4), round(lerp(1, g, f), 4), 1])
    return [dict(role='moss', positions=pos, triangles=tri, tints=tints, blend_role='rock', blend_weights=blend)]


def grounding(grid, h, objects, base):
    rows = []
    for o in objects:
        a, b = o['sx']/2, o['sz']/2
        R = math.hypot(a, b)+.05
        rx, rz = grid.span(o['cx']-R, o['cx']+R, o['cz']-R, o['cz']+R)
        vals = []
        for j in rz:
            for i in rx:
                lx, lz = local(o, grid.xs[i], grid.zs[j])
                if abs(lx) <= a and abs(lz) <= b:
                    vals.append(h[j*grid.nx+i])
        if not vals:
            ci, cj = grid.nearest(o['cx'], o['cz'])
            vals = [h[cj*grid.nx+ci]]
        top = max(vals)
        rows.append(dict(id=o['id'], kind=o['kind'], form=o['form'], basis=o['form_basis'],
                         rock=o['rock_role'], centre=[round(o['cx'], 3), round(o['cz'], 3)],
                         box_top=round(o['top'], 3), land_top=round(top, 3),
                         land_mean=round(sum(vals)/len(vals), 3), delta=round(top-o['top'], 3)))
    return rows


def generate(room_dir, out_dir, setup=None, seed=SEED, return_state=False):
    room_dir = Path(room_dir)
    room = load_json(room_dir/'room.json')
    inventory = load_json(room_dir/'inventory.json')
    setup = setup if setup is not None else load_json(SETUP_PATH)
    noise = Noise(seed)
    rnd = random.Random(seed)
    objects = parse_objects(inventory)
    lo, hi, _ = room_shape(room)
    grid = Grid(lo[0]-MARGIN, lo[2]-MARGIN, hi[0]+MARGIN, hi[2]+MARGIN, CELL)
    nx = grid.nx
    spawns = [s['position_m'] for s in room['spawns']]
    inside = [outside_distance(room, grid.xs[q % nx], grid.zs[q//nx]) <= 0 for q in range(grid.n)]
    outlet = choose_outlet(room, spawns)
    diag = math.hypot(hi[0]-lo[0], hi[2]-lo[2])
    # The plain: living ground tilting gently toward the pass where water leaves.
    base = []
    for q in range(grid.n):
        x, z = grid.xs[q % nx], grid.zs[q//nx]
        t = clamp(math.hypot(x-outlet['x'], z-outlet['z'])/diag)
        base.append(.035+.055*t+.018*noise.fbm(x*.55+2, z*.55-1, 3))
    owner = [-1]*grid.n
    # One lee for the whole land: soft hills trail their long gentle side the
    # way the land drains, from the high country toward the pass, so every
    # hill shares the same weathering, like crag-and-tail country.
    hx_, hz_ = high_country(room, outlet)
    for o in objects:
        a = math.radians(o['yaw'])
        dx, dz = outlet['x']-hx_, outlet['z']-hz_
        o['tail'] = math.atan2(math.sin(a)*dx+math.cos(a)*dz, math.cos(a)*dx-math.sin(a)*dz)
    h = uplift(grid, objects, base, noise, owner)
    outside, skirt = shell_land(grid, room, h, noise, outlet, objects)
    slope = slope_field(grid, h)
    h, acc = weather(grid, h, outside, slope, noise)
    water = plan_and_carve(grid, room, objects, h, base, inside, spawns, setup, noise, outside)
    wet = water['wet']
    reach = distance_field(grid, [w <= 0 for w in wet])
    for q in range(grid.n):
        if reach[q] < wet[q]:
            wet[q] = reach[q]
    views = eyes(room)
    # Resting places where the review eyes stand: a small level clearing.
    for ex, ez, _, _ in views:
        R, blend = .16, .26
        rx, rz = grid.span(ex-R-blend, ex+R+blend, ez-R-blend, ez+R+blend)
        ring = sorted(h[j*nx+i] for j in rz for i in rx if math.hypot(grid.xs[i]-ex, grid.zs[j]-ez) <= R)
        if not ring or min(wet[j*nx+i] for j in rz for i in rx) < .1:
            continue
        level = ring[len(ring)//2]
        for j in rz:
            for i in rx:
                d = math.hypot(grid.xs[i]-ex, grid.zs[j]-ez)
                h[j*nx+i] = lerp(level, h[j*nx+i], smooth((d-R)/blend))
    # ---------------- the hamlet: water, flat ground and shelter meet
    slope = slope_field(grid, h)
    avoid = lambda x, z: clear_of_eyes(views, x, z, .75, (2.3, .5))
    player0 = next((s for s in room['spawns'] if s['role'] == 'player'), room['spawns'][0])['position_m']
    lake = water.get('lake')

    def crossable(q):
        """A stream a footbridge can span (inside the land, never the lake)."""
        if not inside[q]:
            return False
        return lake is None or lake_rho(lake, noise, grid.xs[q % nx], grid.zs[q//nx]) > 1.15
    dry = reachable_from(grid, h, wet, inside, (player0[0], player0[2]), crossable=crossable)
    site = find_hamlet(grid, h, slope, wet, inside, spawns, avoid, lambda x, z: inside_distance(room, x, z), dry)
    # Every object's own footprint keeps its landform: no cottage, path or
    # grading cuts into it (paths go round).
    protect = [False]*grid.n
    for o in objects:
        if o['parent'] is not None:
            continue
        R = math.hypot(o['sx'], o['sz'])/2+.05
        rx_, rz_ = grid.span(o['cx']-R, o['cx']+R, o['cz']-R, o['cz']+R)
        for j in rz_:
            for i in rx_:
                if rr_dist(*local(o, grid.xs[i], grid.zs[j]), max(o['sx']/2, .06), max(o['sz']/2, .06), .02) <= .03:
                    protect[j*nx+i] = True
    houses = []
    pads = [0.]*grid.n
    fields = [0.]*grid.n
    blocked = [False]*grid.n
    scatter = []
    objects_out = []
    hamlet = None
    if site is not None:
        sx, sz = site
        # Nearest water direction: doors face the water.
        best = None
        for j in range(grid.nz):
            for i in range(nx):
                q = j*nx+i
                if wet[q] == 0 and inside[q]:
                    d = math.hypot(grid.xs[i]-sx, grid.zs[j]-sz)
                    if best is None or d < best[0]:
                        best = (d, grid.xs[i], grid.zs[j])
        wx, wz = (best[1], best[2]) if best else (sx, sz-1)
        toward = math.atan2(wz-sz, wx-sx)
        cot = SIZES['cottage']
        for k, (da, r, sc) in enumerate([(0., .0, .74), (1.9, .72, .68), (-1.75, .7, .64), (3.1, .74, .7),
                                         (1.0, .8, .66), (-.9, .8, .66)]):
            a = toward+da
            x, z = sx+math.cos(a)*r, sz+math.sin(a)*r
            hx, hz = cot[0]*sc/2, cot[2]*sc/2
            # Face the water: local -Z (the door) points at the nearest water.
            yaw = math.degrees(math.atan2(-(wx-x), -(wz-z)))
            yaw = round(yaw/5)*5
            if inside_distance(room, x, z) < math.hypot(hx, hz)+.08:
                continue
            i, j = grid.nearest(x, z)
            if wet[j*nx+i] < math.hypot(hx, hz)+.06:
                continue
            cs, sn = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
            if any(protect[grid.nearest(x+cs*lx_+sn*lz_, z-sn*lx_+cs*lz_)[1]*nx+grid.nearest(x+cs*lx_+sn*lz_, z-sn*lx_+cs*lz_)[0]]
                   for lx_ in (-hx-.08, 0, hx+.08) for lz_ in (-hz-.2, -hz, 0, hz+.08)):
                continue   # never build over an object's landform
            # Room for an 11 cm lane (and a yard) between any two cottages.
            if any(math.hypot(x-hh['x'], z-hh['z']) < math.hypot(hx, hz)+math.hypot(hh['hx'], hh['hz'])+.16
                   for hh in houses):
                continue
            if not clear_of_eyes(views, x, z, .6, (2.0, .3)):
                continue
            level = flatten(grid, h, x, z, hx, hz, yaw)
            # A level forecourt before the door: the land gives way to the threshold.
            ya = math.radians(yaw)
            flatten(grid, h, x-math.sin(ya)*(hz+.12), z-math.cos(ya)*(hz+.12), hx, .1, yaw, margin=.03,
                    blend=.14, level=level)
            houses.append(dict(x=x, z=z, yaw=yaw, scale=sc, y=level, hx=hx, hz=hz))
            if len(houses) == 3:
                break
        nx_ = grid.nx
        for hh in houses:
            R = max(hh['hx'], hh['hz'])+.1
            rx, rz = grid.span(hh['x']-R, hh['x']+R, hh['z']-R, hh['z']+R)
            a = math.radians(hh['yaw'])
            c, s = math.cos(a), math.sin(a)
            for j in rz:
                for i in rx:
                    dx, dz = grid.xs[i]-hh['x'], grid.zs[j]-hh['z']
                    lx, lz = c*dx-s*dz, s*dx+c*dz
                    q = j*nx_+i
                    if abs(lx) <= hh['hx']+.01 and abs(lz) <= hh['hz']+.01:
                        blocked[q] = True
                    d = max(abs(lx)-hh['hx'], abs(lz)-hh['hz'], 0.)
                    pads[q] = max(pads[q], .55*(1-smooth(d/.08)))
        hamlet = dict(x=sx, z=sz, water=[wx, wz], houses=len(houses))
    # ---------------- paths (built and graded) and the carryable object
    paths = [0.]*grid.n
    player = next((s for s in room['spawns'] if s['role'] == 'player'), room['spawns'][0])
    px, pz = player['position_m'][0], player['position_m'][2]
    carry = None
    route = None
    roads = []
    bridges = []
    # Paths keep an 11 cm carrying lane clear of walls: route them that far off.
    near_built = distance_field(grid, blocked)
    lane_blocked = [d < .07 or protect[q] for q, d in enumerate(near_built)]
    keep_out = [1. if protect[q] else pads[q] for q in range(grid.n)]
    if houses:
        door = houses[0]
        a = math.radians(door['yaw'])
        # In front of the first cottage's door, beside the path.
        fx = door['x']-math.sin(a)*(door['hz']+.12)+math.cos(a)*.13
        fz = door['z']-math.cos(a)*(door['hz']+.12)-math.sin(a)*.13
        road = build_road(grid, h, wet, inside, lane_blocked, keep_out, (px, pz), (fx, fz),
                          crossable=crossable, bridges=bridges)
        if road:
            roads.append(road)
            paint_line(grid, road, paths)
            for hh in houses[1:]:
                a = math.radians(hh['yaw'])
                dx_, dz_ = hh['x']-math.sin(a)*(hh['hz']+.08), hh['z']-math.cos(a)*(hh['hz']+.08)
                r2 = build_road(grid, h, wet, inside, lane_blocked, keep_out, (fx, fz), (dx_, dz_), .075,
                                crossable=crossable, bridges=bridges)
                if r2:
                    roads.append(r2)
                    paint_line(grid, r2, paths, .028)
    # Walk the graded land as a body would (11 cm lane, cottages in the way,
    # bridges underfoot). Where it cannot reach the crate or a door, the land
    # gives way: the route a steeper walk finds is cut and filled to grade.
    if houses and roads:
        from .checks import Terrain as _T, Walker as _W
        keep_tris = [list(t) for t in grid.triangles() if inside_tri(grid, inside, t)]
        wrec = [{'mesh': w['name'], 'kind': w['kind']} for w in water['meshes']]
        wmesh = {w['name']: [dict(role='still_water', positions=w['positions'], triangles=w['triangles'])]
                 for w in water['meshes']}
        cots = [dict(prototype='cottage', position_m=[hh['x'], hh['y'], hh['z']], yaw_deg=hh['yaw'],
                     scale=[hh['scale']]*3) for hh in houses]
        decks = [dict(prototype='footbridge', position_m=[b['x'], b['top']-BRIDGE[1], b['z']], yaw_deg=b['yaw'],
                      scale=[1., 1., b['length']/BRIDGE[2]]) for b in bridges]
        goals = [(fx, fz)]+[(hh['x']-math.sin(math.radians(hh['yaw']))*(hh['hz']+.08),
                             hh['z']-math.cos(math.radians(hh['yaw']))*(hh['hz']+.08)) for hh in houses]
        for _ in range(2):
            land = {'land': [dict(role='meadow', positions=[[grid.xs[q % nx], h[q], grid.zs[q//nx]] for q in range(grid.n)],
                                  triangles=keep_tris)], **wmesh}
            probe0 = dict(terrain=[{'mesh': 'land'}], scenery=[], water=wrec, scatter=cots+decks, objects=[],
                          prototypes={'footbridge': {'size_m': list(BRIDGE)}})
            terr0 = _T(probe0, land)
            strict = _W(probe0, land, terr0, room)
            loose = _W(probe0, land, terr0, room, limit=40., step=.04)
            fixed = 0
            for goal in goals:
                if strict.route((px, pz), goal)[0]:
                    continue
                path = loose.route((px, pz), goal)[0]
                if path:
                    grade_line(grid, h, wet, keep_out, resample([(k[0]*terr0.cell, k[1]*terr0.cell) for k in path], .025),
                               bridges=bridges)
                    fixed += 1
            if not fixed:
                break
    # Whatever was levelled after the tarn was filled (glades, pads, paths)
    # may not breach its shore: the rim stands above the water again, except
    # where a stream carries the water on.
    if lake is not None:
        R = max(lake['rx'], lake['rz'])*1.6
        channel = set()
        for st in water['streams']:
            halves = st['half'] if isinstance(st['half'], list) else [st['half']]*len(st['pts'])
            for (x_, z_), hw in zip(st['pts'], halves):
                if math.hypot(x_-lake['x'], z_-lake['z']) > R+.2:
                    continue
                sx_, sz_ = grid.span(x_-hw-.05, x_+hw+.05, z_-hw-.05, z_+hw+.05)
                channel.update(j*nx+i for j in sz_ for i in sx_
                               if math.hypot(grid.xs[i]-x_, grid.zs[j]-z_) <= hw+.05)
        rx, rz = grid.span(lake['x']-R, lake['x']+R, lake['z']-R, lake['z']+R)
        for j in rz:
            for i in rx:
                q = j*nx+i
                if q not in channel and 1. <= lake_rho(lake, noise, grid.xs[i], grid.zs[j]) <= 1.35:
                    h[q] = max(h[q], lake['level']+.006)
    slope = slope_field(grid, h)
    tris = grid.triangles()
    fslope = face_slope_max(grid, h, tris)
    if houses and roads:
        route = roads[0]
        if route:
            gy = grid.tri_height(h, fx, fz)
            carry = dict(id='apple_crate', kind='crate', prototype='crate',
                         position_m=[round(fx, 4), round(gy-.002, 4), round(fz, 4)],
                         yaw_deg=round(door['yaw']+20, 1), size_m=[.08, .07, .07],
                         mass_kg=.12, carriable=True, tint=[1., .82, .7])
            objects_out.append(carry)
    shadow = sun_shadow(grid, h, water_sun(setup))
    weights, tints = surface(grid, h, base, fslope, owner, objects, shadow, wet, acc, outside,
                             paths, pads, fields, noise, skirt)
    terrain_tris = [t for t in tris if inside_tri(grid, inside, t)]
    ridge_tris = [t for t in tris if not inside_tri(grid, inside, t)]
    meshes = {'land': build_mesh(grid, h, weights, tints, terrain_tris),
              'ridges': build_mesh(grid, h, weights, tints, ridge_tris),
              'far_hills': distant_hills(room, noise, water.get('outlet', outlet))}
    water_records = []
    for w in water['meshes']:
        meshes[w['name']] = [dict(role='still_water' if w['kind'] == 'still' else 'flowing_water',
                                  positions=w['positions'], triangles=w['triangles'])]
        water_records.append({'mesh': w['name'], 'kind': w['kind']})
    protos = prototypes()
    proto_records = {}
    for name, (prims, size) in protos.items():
        meshes['proto_'+name] = prims
        proto_records[name] = {'mesh': 'proto_'+name, 'size_m': size}
    # Footbridges where the paths cross a stream: level decks, ends on the filled banks.
    for b in bridges:
        scatter.append(dict(prototype='footbridge', position_m=[round(b['x'], 4), round(b['top']-BRIDGE[1], 4),
                                                              round(b['z'], 4)],
                            yaw_deg=round(b['yaw'], 1), scale=[1., 1., round(b['length']/BRIDGE[2], 3)]))
    # ---------------- planting
    # Things a body cannot pass keep an 11 cm carrying lane clear beside every path.
    path_gap = distance_field(grid, [v >= .5 for v in paths])
    plant = Planter(grid, h, fslope, wet, inside, shadow, paths, pads, blocked, base, owner, objects,
                    views, room, rnd, noise, acc)
    plant.path_gap = path_gap
    plant.bridge_rects = [(b['x'], b['z'], b['yaw'], b['length']) for b in bridges]
    for b in bridges:
        plant.occupy(b['x'], b['z'], b['length']/2+.05)
    for hh in houses:
        scatter.append(dict(prototype='cottage', position_m=[round(hh['x'], 4), round(hh['y']-.004, 4), round(hh['z'], 4)],
                            yaw_deg=hh['yaw'], scale=[hh['scale']]*3))
        plant.occupy(hh['x'], hh['z'], max(hh['hx'], hh['hz'])+.06)
    if carry:
        plant.occupy(carry['position_m'][0], carry['position_m'][2], .08)
    if houses:
        scatter += plant.hamlet_props(houses)
        # A trodden lane from the first door to each yard thing keeps it reachable.
        lane = [1. if v >= .5 else 0. for v in paths]
        road_pts = [pt for r in roads for pt in r]
        for rec in scatter:
            if rec['prototype'] in ('drying_line', 'woodpile'):
                ay = math.radians(rec['yaw_deg'])
                fronts = [(rec['position_m'][0]-math.sin(ay)*f, rec['position_m'][2]-math.cos(ay)*f) for f in (.1, -.1)]
                front, door = min(((f, min(road_pts, key=lambda pt: math.hypot(pt[0]-f[0], pt[1]-f[1])))
                                   for f in fronts), key=lambda fd: math.hypot(fd[0][0]-fd[1][0], fd[0][1]-fd[1][1]))
                steps = max(2, int(math.hypot(front[0]-door[0], front[1]-door[1])/.02))
                paint_line(grid, [(door[0]+(front[0]-door[0])*k/steps, door[1]+(front[1]-door[1])*k/steps)
                                  for k in range(steps+1)], lane, .02)
        plant.path_gap = distance_field(grid, [v >= .5 for v in lane])
    scatter += plant.everything()
    # Promise only what a body can reach: a yard thing no 11 cm lane reaches
    # from the spawn is left out (the same walk the checks run).
    from .checks import YARD, Terrain, Walker, approaches
    probe = dict(terrain=[{'mesh': 'land'}], scenery=[], water=water_records, scatter=scatter,
                 prototypes=proto_records, objects=objects_out)
    # Where planting closed the way to the crate, a door or a yard (laundry,
    # woodpile), the way is cleared: trees, shrubs, stones and fences within a
    # carrying lane of the route past the buildings are taken out. Only a yard
    # thing that even a cleared lane cannot reach is left out.
    keep = {'cottage', 'footbridge'} | set(YARD)
    terr = Terrain(probe, meshes)

    def size_of(rec):
        base_size = SIZES[rec['prototype']] if rec['prototype'] in SIZES else proto_records[rec['prototype']]['size_m']
        return [b*s for b, s in zip(base_size, rec['scale'])]
    targets = [(None, [(carry['position_m'][0], carry['position_m'][2])])] if carry else []
    targets += [(None, [(hh['x']-math.sin(math.radians(hh['yaw']))*(hh['hz']+.08),
                         hh['z']-math.cos(math.radians(hh['yaw']))*(hh['hz']+.08))]) for hh in houses]
    targets += [(rec, approaches(rec, size_of(rec))) for rec in scatter if rec['prototype'] in YARD]
    dropped = []
    for rec_, goals_ in targets:
        if rec_ is not None and rec_ not in scatter:
            continue
        full = Walker(probe, meshes, terr, room)
        if any(full.route((px, pz), g)[0] for g in goals_):
            continue
        # Toward the crate or a door even a yard thing may stand aside; toward
        # a yard, the other yards stay.
        hold = keep if rec_ is not None else {'cottage', 'footbridge'}
        fixed = Walker(dict(probe, scatter=[r for r in scatter if r['prototype'] in hold]), meshes, terr, room)
        path = next((pth for pth in (fixed.route((px, pz), g)[0] for g in goals_) if pth), None)
        if path:
            pts = [(k[0]*terr.cell, k[1]*terr.cell) for k in path]
            for rec in [r for r in scatter if r['prototype'] in BLOCKING and r['prototype'] not in hold and r is not rec_]:
                x, z = rec['position_m'][0], rec['position_m'][2]
                near = [(a, b) for a, b in pts if math.hypot(x-a, z-b) < .4]
                if near:
                    one = Walker(dict(probe, scatter=[rec]), meshes, terr, room)
                    if any(one.blocked(a, b) for a, b in near):
                        scatter.remove(rec)
                        if rec['prototype'] in YARD:
                            dropped.append(rec['prototype'])
        if rec_ is not None and not any(Walker(probe, meshes, terr, room).route((px, pz), g)[0] for g in goals_):
            # Move the yard rather than lose it: the first level, reachable spot
            # around any cottage.
            scatter.remove(rec_)
            moved = None
            size = DRYING_LINE if rec_['prototype'] == 'drying_line' else [.09, .044, .1]
            for hh in houses:
                if moved:
                    break
                a = math.radians(hh['yaw'])
                c, s_ = math.cos(a), math.sin(a)
                for lx, lz, turn in [(hh['hx']+.32, -hh['hz']*.6, 90), (-hh['hx']-.32, -hh['hz']*.6, 90),
                                     (hh['hx']+.32, .1, 90), (-hh['hx']-.32, .1, 90), (0., hh['hz']+.32, 0),
                                     (hh['hx']+.45, -hh['hz']-.2, 45), (-hh['hx']-.45, -hh['hz']-.2, -45)]:
                    cand = plant.put(rec_['prototype'], hh['x']+c*lx+s_*lz, hh['z']-s_*lx+c*lz, rec_['scale'],
                                     hh['yaw']+turn, .02, size, sink=.002, built=True)
                    if not cand:
                        continue
                    one = Walker(dict(probe, scatter=scatter+[cand]), meshes, terr, room)
                    if any(one.route((px, pz), g)[0] for g in approaches(cand, size_of(cand))) and                             all(one.route((px, pz), g[0])[0] for _, g in targets if _ is None):
                        moved = cand
                        break
            if moved:
                scatter.append(moved)
            else:
                dropped.append(rec_['prototype'])
    extensions = {'x_generator': dict(seed=seed, cell_m=CELL, margin_m=MARGIN,
                                  walk_limit_deg=WALK_LIMIT_DEG, step_limit_m=STEP_LIMIT_M,
                                  water_character=water.get('character'), water_why=water.get('why'),
                                  lake=water['lake'], pools=water.get('pools', []),
                                  spring=[round(v, 3) for v in water.get('spring', (0, 0))],
                                  hamlet=hamlet, bridges=len(bridges), dropped_yard=dropped,
                                  forms={o['id']: [o['form'], o['form_basis'], o['rock_role']] for o in objects},
                                  grounding=grounding(grid, h, objects, base))}
    doc = write_package(out_dir, room_dir, meshes=meshes, setup=setup, generator=GENERATOR,
                        terrain=[{'mesh': 'land'}], water=water_records,
                        scenery=[{'mesh': 'ridges', 'reachable': False}, {'mesh': 'far_hills', 'reachable': False}],
                        prototypes=proto_records, scatter=scatter, objects=objects_out, extensions=extensions)
    if return_state:
        return doc, dict(grid=grid, h=h, route=route, carry=carry, houses=houses)
    return doc


def water_sun(setup):
    from pipeline.landscape.harness.views import sun_position
    return sun_position(setup['latitude_deg'], setup['day_of_year'], setup['solar_time_h'],
                        setup['neg_z_bearing_deg'])['direction_room']


def inside_tri(grid, inside, t):
    return sum(inside[q] for q in t) >= 2


DRYING_LINE = [.36, .2, .03]
BLOCKING = {'broadleaf', 'conifer', 'rock', 'boulder', 'shrub', 'cottage', 'woodpile', 'fence', 'lantern',
            'crate', 'drying_line'}


class Planter:
    """Plants follow water, slope, height and light; nothing roots in water,
    on a path, a pad or in front of a review eye."""

    def __init__(self, grid, h, fslope, wet, inside, shadow, paths, pads, blocked, base, owner, objects,
                 views, room, rnd, noise, acc):
        self.__dict__.update(locals())
        self.taken = []   # (x, z, r)
        self.cells = {}

    def occupy(self, x, z, r):
        self.taken.append((x, z, r))
        key = (int(math.floor(x/.5)), int(math.floor(z/.5)))
        self.cells.setdefault(key, []).append((x, z, r))

    def free(self, x, z, r):
        kx, kz = int(math.floor(x/.5)), int(math.floor(z/.5))
        for dx in (-1, 0, 1):
            for dz in (-1, 0, 1):
                for ox, oz, orr in self.cells.get((kx+dx, kz+dz), ()):
                    if math.hypot(x-ox, z-oz) < r+orr:
                        return False
        return True

    def at(self, x, z):
        i, j = self.grid.nearest(x, z)
        return j*self.grid.nx+i

    def ground(self, x, z, r):
        g = self.grid
        pts = [(x, z)]+[(x+r*math.cos(a), z+r*math.sin(a)) for a in (0, 1.57, 3.14, 4.71)]
        hs = [g.tri_height(self.h, a, b) for a, b in pts]
        return min(hs), max(hs)

    def fits(self, x, z, size, scale):
        sx, sy, sz = size[0]*scale[0], size[1]*scale[1], size[2]*scale[2]
        r = math.hypot(sx, sz)/2
        return inside_distance(self.room, x, z) > r+.005

    def put(self, proto, x, z, scale, yaw, root, size, tint=None, sink=.004, clear=.0, built=False):
        if not self.fits(x, z, size, scale):
            return None
        q = self.at(x, z)
        if not self.inside[q] or self.wet[q] < .02+root or self.paths[q] > .2 or self.pads[q] > .05 or self.blocked[q]:
            return None
        for bx, bz, by, bl in getattr(self, 'bridge_rects', ()):
            a_ = math.radians(by)
            dx, dz = x-bx, z-bz
            if abs(math.cos(a_)*dx-math.sin(a_)*dz) < .12 and abs(math.sin(a_)*dx+math.cos(a_)*dz) < bl/2+.05:
                return None   # nothing grows under or on a footbridge
        if proto in BLOCKING:
            if proto in ('broadleaf', 'conifer'):
                r = .045*scale[0]+.01
            elif proto in ('rock', 'boulder', 'shrub'):
                r = min(size[0]*scale[0], size[2]*scale[2])/2
            else:
                r = math.hypot(size[0]*scale[0], size[2]*scale[2])/2
            if self.path_gap[q] < r+.075:
                return None
        if built:
            # Built things need level ground under the whole footprint.
            a = math.radians(yaw)
            c, s = math.cos(a), math.sin(a)
            hx, hz = size[0]*scale[0]/2, size[2]*scale[2]/2
            hs = [self.grid.tri_height(self.h, x+c*lx+s*lz, z-s*lx+c*lz)
                  for lx in (-hx, 0, hx) for lz in (-hz, 0, hz)]
            lo_, hi_ = min(hs), max(hs)
            if hi_-lo_ > .012:
                return None
            sink = min(sink, .002)
        else:
            lo_, hi_ = self.ground(x, z, root)
            if hi_-lo_ > max(.012, .25*size[1]*scale[1]):
                return None
        y = lo_-sink
        if y < .0005:
            return None
        if y+size[1]*scale[1] > self.room['bounds']['max_m'][1]-.01:
            return None
        rec = dict(prototype=proto, position_m=[round(x, 4), round(y, 4), round(z, 4)],
                   yaw_deg=round(yaw, 1), scale=[round(s, 3) for s in scale])
        if tint:
            rec['tint'] = [round(c, 3) for c in tint]
        if clear:
            self.occupy(x, z, clear)
        return rec

    def hamlet_props(self, houses):
        out = []
        rnd = self.rnd
        for k, hh in enumerate(houses):
            a = math.radians(hh['yaw'])
            c, s = math.cos(a), math.sin(a)
            # Behind each cottage: a woodpile or crates; beside the first, a lantern.
            # A woodpile stacked against the gable or the back wall, with room to reach it.
            for lx, lz in [(hh['hx']+.14, .08), (-hh['hx']-.14, .08), (0., hh['hz']+.14), (hh['hx']+.14, -.1)]:
                x = hh['x']+c*lx+s*lz
                z = hh['z']-s*lx+c*lz
                rec = self.put('woodpile', x, z, [1.]*3, hh['yaw']+rnd.uniform(-15, 15), .02, [.09, .044, .1],
                               sink=.002, clear=.06, built=True)
                if rec:
                    out.append(rec)
                    break
            if (k+5) % 2 == 0:
                x = hh['x']-c*(hh['hx']+.05)+s*.1
                z = hh['z']+s*(hh['hx']+.05)+c*.1
                rec = self.put('crate', x, z, [.45]*3, hh['yaw']+rnd.uniform(-15, 15), .02, SIZES['crate'],
                               sink=.002, clear=.06, built=True)
                if rec:
                    out.append(rec)
        # Laundry drying on a line in the open beside a cottage.
        done = False
        for hh in houses:
            if done:
                break
            a = math.radians(hh['yaw'])
            c, s = math.cos(a), math.sin(a)
            # Door side first (local -Z), where the paths arrive.
            for lx, lz, turn in [(hh['hx']+.3, -hh['hz']*.6, 90), (-hh['hx']-.3, -hh['hz']*.6, 90),
                                 (hh['hx']+.3, 0., 90), (-hh['hx']-.3, 0., 90), (0., hh['hz']+.3, 0)]:
                x = hh['x']+c*lx+s*lz
                z = hh['z']-s*lx+c*lz
                rec = self.put('drying_line', x, z, [1., 1., 1.], hh['yaw']+turn, .02, DRYING_LINE, sink=.002,
                               clear=.3, built=True)
                if rec:
                    out.append(rec)
                    done = True
                    break
        hh = houses[0]
        a = math.radians(hh['yaw'])
        x = hh['x']-math.sin(a)*(hh['hz']+.1)-math.cos(a)*(hh['hx']+.02)
        z = hh['z']-math.cos(a)*(hh['hz']+.1)+math.sin(a)*(hh['hx']+.02)
        rec = self.put('lantern', x, z, [.5]*3, hh['yaw'], .01, SIZES['lantern'], sink=.002, clear=.04, built=True)
        if rec:
            out.append(rec)
        # A fenced garden on the sunny side of the hamlet.
        for hh in houses[:2]:
            a = math.radians(hh['yaw'])
            for side in (1,):
                for n in range(2):
                    lx, lz = side*(hh['hx']+.12), -hh['hz']+.05+n*.3
                    x = hh['x']+math.cos(a)*lx+math.sin(a)*lz
                    z = hh['z']-math.sin(a)*lx+math.cos(a)*lz
                    rec = self.put('fence', x, z, [.5, .55, .5], hh['yaw']+90, .03, SIZES['fence'], sink=.004, clear=.05,
                                   built=True)
                    if rec:
                        out.append(rec)
        return out

    def everything(self):
        g = self.grid
        out = []
        rnd = self.rnd
        noise = self.noise
        lo, hi, _ = room_shape(self.room)

        def lattice(spacing):
            pts = []
            nxs = int((hi[0]-lo[0])/spacing)
            nzs = int((hi[2]-lo[2])/spacing)
            for b in range(nzs):
                for a in range(nxs):
                    pts.append((lo[0]+(a+rnd.random())*spacing, lo[2]+(b+rnd.random())*spacing))
            return pts

        def env(x, z):
            q = self.at(x, z)
            o = self.objects[self.owner[q]] if self.owner[q] >= 0 else None
            return q, self.h[q]-self.base[q], self.fslope[q], self.wet[q], self.shadow[q], o

        # Trees: riparian broadleaves; conifers on ridges, tops and the wild edges.
        for x, z in lattice(.2):
            q, rel, s, w, sh, o = env(x, z)
            if s > 30 or not clear_of_eyes(self.views, x, z, .45, (1.1, .25)):
                continue
            edge = smooth((.9-inside_distance(self.room, x, z))/.6)
            riparian = smooth((.55-w)/.35)*(w > .06)
            high = smooth((rel-.18)/.25)
            n = noise.fbm(x*1.6+9, z*1.6, 3)
            groves = smooth((n+.05)/.35)
            hills = smooth((rel-.04)/.1)*(1-smooth((rel-.5)/.2))
            p_broad = .6*riparian+.22*groves*(1-high)*(1-edge)+.3*hills*groves
            p_con = .6*high*(1-smooth((s-26)/8))+.55*edge*groves+.2*groves*(sh > .5)
            u = rnd.random()
            if u < p_con*.6 and self.h[q] < 1.45:
                sc = .38+.3*rnd.random()
                scale = [sc, sc*(.9+.3*rnd.random()), sc]
                if self.free(x, z, .1*sc+.06):
                    rec = self.put('conifer', x, z, scale, rnd.uniform(0, 360), .03*sc, SIZES['conifer'],
                                   tint=(lerp(.75, .95, rnd.random()), lerp(.85, 1, rnd.random()), lerp(.8, .95, rnd.random())),
                                   sink=.008, clear=.1*sc+.06)
                    if rec:
                        out.append(rec)
            elif u < (p_con*.6+p_broad*.75):
                sc = .4+.3*rnd.random()
                scale = [sc, sc*(.85+.3*rnd.random()), sc]
                if self.free(x, z, .16*sc+.06):
                    rec = self.put('broadleaf', x, z, scale, rnd.uniform(0, 360), .035*sc, SIZES['broadleaf'],
                                   tint=(lerp(.85, 1, rnd.random()), lerp(.9, 1, rnd.random()), lerp(.7, .9, rnd.random())),
                                   sink=.008, clear=.16*sc+.06)
                    if rec:
                        out.append(rec)
        # Shrubs at grove edges and on banks; loose stones below cliffs.
        for x, z in lattice(.13):
            q, rel, s, w, sh, o = env(x, z)
            if not clear_of_eyes(self.views, x, z, .3, (.8, .18)):
                continue
            u = rnd.random()
            n = noise.fbm(x*1.6+9, z*1.6, 3)
            if s < 32 and u < .22*smooth((n+.2)/.4)+.2*smooth((.4-w)/.3):
                sc = .35+.35*rnd.random()
                if self.free(x, z, .1*sc):
                    rec = self.put('shrub', x, z, [sc, sc*(.8+.4*rnd.random()), sc], rnd.uniform(0, 360), .05*sc,
                                   SIZES['shrub'], tint=(lerp(.8, 1, rnd.random()), lerp(.9, 1, rnd.random()), .8),
                                   sink=.01, clear=.1*sc)
                    if rec:
                        out.append(rec)
            elif o is not None and o['form'] not in ('dome',) and 22 < s < 42 and u < .5:
                sc = .18+.35*rnd.random()
                if self.free(x, z, .07*sc+.01):
                    rec = self.put('rock', x, z, [sc, sc*(.7+.5*rnd.random()), sc], rnd.uniform(0, 360), .06*sc,
                                   SIZES['rock'], tint=tuple(lerp(1, c, .7) for c in o['tint']), sink=.01*sc+.004)
                    if rec:
                        out.append(rec)
        # Ground cover: grass everywhere it can grow; flowers in the morning sun;
        # heather on high dry tops; ferns in shade; reeds where feet get wet.
        for x, z in lattice(.07):
            q, rel, s, w, sh, o = env(x, z)
            if s > 34 or not clear_of_eyes(self.views, x, z, .22, (.45, .08)):
                continue
            u = rnd.random()
            yaw = rnd.uniform(0, 360)
            if w < .09:
                if u < .55 and w > .025:
                    sc = .7+.5*rnd.random()
                    rec = self.put('reeds', x, z, [sc, sc, sc], yaw, .02, [.08, .07, .08], sink=.004)
                    if rec:
                        out.append(rec)
                continue
            if sh > .55 and u < .5:
                sc = .2+.15*rnd.random()
                rec = self.put('fern', x, z, [sc, sc, sc], yaw, .03, SIZES['fern'], tint=(.8, 1, .85), sink=.004)
            elif rel > .3 and sh < .4 and w > .5 and u < .55:
                sc = .8+.5*rnd.random()
                rec = self.put('heather', x, z, [sc, sc, sc], yaw, .03, [.13, .022, .13], sink=.003)
            elif sh < .35 and u < .16+.12*smooth((.8-w)/.6):
                kind = ('wildflowers', 'daisies', 'buttercups')[int(3*((noise(x*2.3, z*2.3)+1)/2*.999))]
                size = {'wildflowers': [.11, .03, .11], 'daisies': [.1, .028, .1], 'buttercups': [.09, .027, .09]}[kind]
                sc = .8+.5*rnd.random()
                tint = None
                if kind == 'wildflowers':
                    tint = [(1, .55, .5), (1, .8, 1), (.75, .6, 1)][int(rnd.random()*2.999)]
                rec = self.put(kind, x, z, [sc, sc, sc], yaw, .03, size, tint=tint, sink=.004)
            elif u < .78:
                sc = .8+.6*rnd.random()
                lush = smooth((.6-w)/.5)
                rec = self.put('grass', x, z, [sc, sc*(.8+.5*rnd.random()), sc], yaw, .03, [.14, .028, .14],
                               tint=(lerp(1, .85, lush), 1, lerp(.7, .85, lush)), sink=.004)
            else:
                rec = None
            if rec:
                out.append(rec)
        return out


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--room', required=True)
    parser.add_argument('--out', required=True)
    parser.add_argument('--setup', default=str(SETUP_PATH))
    parser.add_argument('--seed', type=int, default=SEED)
    args = parser.parse_args()
    doc = generate(args.room, args.out, load_json(args.setup), args.seed)
    print('LANDSCAPE_PACKAGE', args.out, 'meshes', len(doc['meshes']), 'scatter', len(doc['scatter']),
          'objects', len(doc['objects']))


if __name__ == '__main__':
    main()
