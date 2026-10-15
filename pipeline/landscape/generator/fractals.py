"""Fractals and gates: the generator's part of the mechanics round (packet M4).

The island places a budget of Fractals (docs/game/MECHANICS.md section 6): real small things at crests the body can
reach by climbing, untyped by default (a heart or a band only once spent at a camp; `typed` puts the kind in the
manifest behind the rules pack's flag). The mechanics need the land to be a ladder: each rise between ledges is one band
of the stamina bar, so the player who has opened band k finds a Fractal on a ledge that needs band k, sees the next
ledge one band up, and climbs it when the next five are spent.

Built here, in two parts either side of the planting:

`plan` (before the ground is worked at 2.5 cm, on the base grid): finds places and builds the flights (routes.py):
    - the summit's corridor, cut through the tallest landmark in reach to its top (the upper ledges at the top bands),
    - ladders of gates (band 1, 2, 3, ... each one rise more), freestanding on flat meadow,
    - knack shortcuts (a higher ledge one knack at full rank opens a band sooner, the base body does not),
    - steps (one band-1 ledge: the climb a first-run player makes for a Fractal).
`finish` (on the planted island, after the exact walk): the climb-aware reach (reach.py) of the base body, one knack at
    full rank and the strongest pair; each bench's band proved (a gate that is not the band it was built as is
    reported, a bench no one reaches is dropped); the Fractals placed on benches, in hidden corners the feet reach, and
    one on high ground no band reaches; their ids from the place; the reach report.

Ids come from the place (the first 12 hex digits of a hash of where a Fractal rests), never a count, so an id says
nothing about how many the island holds or which was placed first.
"""
import hashlib
import math
import random

from . import gates, routes as R, sites

# The rules pack's `fractals` block (storybook_wild v5): the island's budget. The generator aims for TARGET.
BUDGET = dict(min=20, max=25, reachable_first_run=15)
TARGET = 24
LADDERS = ((1, 2, 3, 4), (1, 2, 3))   # ladders of gates on flat meadow (the bands of their ledges, foot to top)
SHORTCUT_BANDS = (1, 2)               # knack shortcuts: one for each band where the arithmetic holds (gates.py)
STEPS = 8                             # band-1 steps
FIRST_STEP_M = 12.                    # the nearest step stands about this far from where the player arrives
SUMMIT_MIN_M = 4.                     # a summit stands at least this far above the ground its corridor starts on
CORNER_SPACING_M = 6.                 # hidden corners lie this far apart
CORNER_RING_M = (1.5, 3.)             # ... and are measured closed in at these radii


def fractal_id(x, z):
    """A Fractal's id from where it rests (centimetres, x then z): 'fractal:' and 12 hex digits."""
    return 'fractal:'+hashlib.sha256(('fractal|%.2f|%.2f' % (x, z)).encode()).hexdigest()[:12]


class Plan:
    """What `plan` built: the flights, the summit's, and what the planting must leave alone."""

    def __init__(self, land, routes, summit, notes):
        self.land, self.routes, self.summit, self.notes = land, routes, summit, notes
        self.keep = set()

    def all_routes(self):
        return ([self.summit] if self.summit is not None else [])+list(self.routes)

    def hold(self, detail):
        """On the 10x world: a flight that lies in a detail block keeps the base's surface there (the surface the flight
        was built as, like the ways the body walks: `Detail.walks_on_base`), a cell beyond its edge included."""
        g = self.land.grid
        for route in self.all_routes():
            i0, i1, j0, j1 = sites.flight_box(route, 1)
            x0, x1, z0, z1 = g.xs[max(0, i0)], g.xs[min(g.nx-1, i1)], g.zs[max(0, j0)], g.zs[min(g.nz-1, j1)]

            def run(x0=x0, x1=x1, z0=z0, z1=z1):
                bench = detail.bench
                for k in detail.span(x0, x1, z0, z1):
                    bench[k] = 1.
            detail.op(run)


def landmarks(land):
    """The island's tall landforms, highest first (a landmark: an object that stands on the floor)."""
    objs = [o for o in land.objects if o['parent'] is None and o['sy'] >= SUMMIT_MIN_M and o['form'] not in ('dome', 'knoll')]
    return sorted(objs, key=lambda o: (-o['top'], o['id']))


def _near_reach(land, obj):
    """Whether the walk reaches ground near a landmark (within a few metres of its box)."""
    r = max(obj['sx'], obj['sz'])/2+5.
    for k in range(16):
        a = k*math.pi/8
        if land.reached(obj['cx']+r*math.cos(a), obj['cz']+r*math.sin(a)):
            return True
    return False


def plan(land, seed, spawn, recipe=None):
    """Find places and build the flights into `land.h`. `spawn` is (x, z) where the player arrives. Returns a Plan."""
    recipe = recipe or dict(ladders=LADDERS, shortcuts=SHORTCUT_BANDS, steps=STEPS)
    grid, h = land.grid, land.h
    siting = sites.Siting(land, seed_bits=seed & 0xffff)
    notes = []

    def spawn_distance(x, z):
        return math.hypot(x-spawn[0], z-spawn[1])

    summit = None
    for obj in landmarks(land):
        if not _near_reach(land, obj):
            continue
        summit = sites.plan_summit(land, siting, obj, lambda height: gates.ladder_rises(height) if height >= SUMMIT_MIN_M
                                   else None)
        if summit is not None:
            summit.id = 'summit'
            summit.landmark = obj['id']
            siting.take(summit)
            break
    if summit is None:
        notes.append('no landmark in reach has a corridor to its top')
    placed = []
    counter = [0]

    def ident(kind):
        counter[0] += 1
        return '%s%d' % (kind[0], counter[0])

    def spread(x, z, d_taken):
        return min(d_taken, 14.)+.1*min(spawn_distance(x, z), 25.)

    # Ladders first (the longest), then the shortcuts, then the steps; the first step near the spawn.
    for bands in recipe['ladders']:
        route = siting.place(lambda i0, j0, di, dj, z0, b=bands: R.ladder('ladder', grid, i0, j0, di, dj, z0, b, ident('ladder')),
                             prefer=spread)
        if route is None:
            notes.append('no flat meadow for a ladder of %d ledges' % len(bands))
        else:
            placed.append(route)
    for band in recipe['shortcuts']:
        route = siting.place(lambda i0, j0, di, dj, z0, b=band: R.shortcut(grid, i0, j0, di, dj, z0, b, ident('shortcut')),
                             prefer=spread)
        if route is None:
            notes.append('no flat meadow for a knack shortcut over band %d' % band)
        else:
            placed.append(route)
    for k in range(recipe['steps']):
        near_first = (lambda x, z, d: -abs(spawn_distance(x, z)-FIRST_STEP_M)) if k == 0 else spread
        route = siting.place(lambda i0, j0, di, dj, z0: R.ladder('step', grid, i0, j0, di, dj, z0, (1,), ident('step')),
                             prefer=near_first)
        if route is None:
            notes.append('no flat meadow for step %d' % (k+1))
            break
        placed.append(route)
    out = Plan(land, placed, summit, notes)
    # Build them, and keep the planting off them (a bare skirt round each flight).
    nx = grid.nx
    skirt = int(round(.5/grid.cell))
    for route in out.all_routes():
        route.apply(grid, h)
        i0, i1, j0, j1 = sites.flight_box(route, skirt)
        for j in range(max(0, j0), min(grid.nz, j1+1)):
            row = j*nx
            for i in range(max(0, i0), min(nx, i1+1)):
                out.keep.add(row+i)
    return out


# ------------------------------------------------------------------------------------------- after the walk


def route_from_row(grid, row):
    """A flight rebuilt from what the package says of it (`Route.describe`)."""
    c = grid.cell
    ledges = [R.Ledge(l['rise_m'], l['band'], int(round(l['riser_run_m']/c)), int(round(l['bench_length_m']/c)))
              for l in row['ledges']]
    i0, j0 = grid.nearest(*row['origin_m'])
    route = R.Route(row['kind'], i0, j0, row['direction'][0], row['direction'][1], row['foot_level_m'], ledges,
                    half=int(round(row['half_width_m']/c)), ident=row['id'])
    if row['kind'] == 'shortcut':
        route.over = row['ledges'][0]['band']-1   # the band whose bar falls short of it
    return route


def _expected_band(route, k):
    """The band a bench is reached from by design: the highest band of the rises up to it."""
    return max(l.band for l in route.ledges[:k+1])


def _corner_score(land, x, z):
    """How closed in a place is: of 24 places on two rings round it, how many are steep, water, a landform's own
    ground or outside the floor."""
    g = land.grid
    nx = g.nx
    n = 0
    for r in CORNER_RING_M:
        for k in range(12):
            a = k*math.pi/6
            i, j = g.nearest(x+r*math.cos(a), z+r*math.sin(a))
            q = j*nx+i
            if land.fslope[q] > 35 or land.protect[q] or land.wet[q] < .3 or not land.inside[q]:
                n += 1
    return n


def hidden_corners(land, labels, taken, count, spawn, jitter):
    """Places the feet reach (band 1, no climb) that are closed in on most sides, spread apart: where a Fractal is
    tucked away. `taken` are (x, z) already used."""
    g = land.grid
    nx = g.nx
    found = []
    for j in range(0, g.nz, 10):
        for i in range(0, nx, 10):
            q = j*nx+i
            if not land.inside[q] or land.wet[q] < 2. or land.paths[q] >= .05 or land.pads[q] > 0 or land.protect[q] \
                    or land.fslope[q] > 12 or land.blocked[q]:
                continue
            x, z = g.xs[i], g.zs[j]
            if labels.band_at(x, z) != 1 or math.hypot(x-spawn[0], z-spawn[1]) < CORNER_SPACING_M:
                continue
            found.append((-_corner_score(land, x, z), jitter(i, j), x, z))
    found.sort()
    picked = []
    for _, _, x, z in found:
        if len(picked) >= count:
            break
        if all(math.hypot(x-a, z-b) >= CORNER_SPACING_M for a, b in picked+list(taken)):
            picked.append((x, z))
    return picked


HIGH_MIN_M = 3.      # high ground is at least this high
HIGH_NEAR_M = 12.    # ... and no farther than this from ground the feet reach (a Fractal one can see from where one walks)
HIGH_WALK_DEG = 40.  # ... and closed in by cliffs: ground no steeper than this (a body walks 45) leads no farther than ...
HIGH_GENTLE_DEG = 15.  # ... and gentle where it rests
HIGH_REGION_CELLS = 40000   # ... this many cells (a 400 m2 plateau), else a hillside leads down from it


def enclosed(land, i, j, max_deg=HIGH_WALK_DEG, limit=HIGH_REGION_CELLS):
    """Whether the ground round vertex (i, j), walked over land no steeper than `max_deg`, is closed in (a plateau all
    cliffs round it) rather than a hill one can walk up from below. Returns (closed, the vertices walked): when it is
    not closed in, the vertices are a hill's, and none of them is closed in either."""
    g = land.grid
    nx, nz = g.nx, g.nz
    fs = land.fslope
    start = j*nx+i
    seen = {start}
    stack = [start]
    while stack:
        q = stack.pop()
        qi, qj = q % nx, q//nx
        for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ni, nj = qi+di, qj+dj
            if not (0 <= ni < nx and 0 <= nj < nz):
                return False, seen
            n = nj*nx+ni
            if n in seen or fs[n] > max_deg:
                continue
            seen.add(n)
            if len(seen) > limit:
                return False, seen
            stack.append(n)
    return True, seen


def high_unreached(land, labels, keep=(), min_height=HIGH_MIN_M):
    """The highest place no band reaches and cliffs close in (a landmark's top) within sight of reached ground: ground the
    body stands on (gentle all round it, nothing planted in its lane), the place a Bubble lift or a built ladder would
    bring the player to. It is not walked to, so it need not be a node the strict walk stands on. Never on a flight
    (`keep`: the vertices of the flights and their skirts)."""
    g = land.grid
    nx, nz = g.nx, g.nz
    w = labels.walker
    reached = []
    for j in range(0, nz, 20):
        for i in range(0, nx, 20):
            if labels.band_at(g.xs[i], g.zs[j]):
                reached.append((g.xs[i], g.zs[j]))
    if not reached:
        return None
    ring = 6    # 0.6 m, in cells
    found = []
    for j in range(ring, nz-ring, 10):
        for i in range(ring, nx-ring, 10):
            q = j*nx+i
            if land.h[q] < min_height or not land.inside[q] or land.fslope[q] > HIGH_GENTLE_DEG:
                continue
            around = [(j+dj)*nx+i+di for di, dj in ((ring, 0), (-ring, 0), (0, ring), (0, -ring))]
            if any(land.fslope[a] > HIGH_GENTLE_DEG or a in keep for a in around) or q in keep:
                continue
            x, z = g.xs[i], g.zs[j]
            n = labels.node(x, z)
            if (n is not None and labels.label[n]) or w.blocked(x, z):
                continue
            if min(math.hypot(x-a, z-b) for a, b in reached) <= HIGH_NEAR_M:
                found.append((-land.h[q], i, j))
    found.sort()
    open_ground = set()
    for _, i, j in found:
        if j*nx+i in open_ground:
            continue
        closed, region = enclosed(land, i, j)
        if closed:
            return g.xs[i], g.zs[j]
        open_ground |= region    # a hillside: every candidate on it is the same hill
    return None


NATURAL_CLUSTER_M = 1.5   # climbs whose tops lie within this of each other are one way up in the report


def natural_ways(plan, variants):
    """The ways up the land itself offers (climbs not in a flight) that the exact walk used, one per top place:
    where the climb ends, its rise, and the lowest band each variant of the body takes it from."""
    grid = plan.land.grid
    nx = grid.nx
    keep = plan.keep

    def built(xz):
        i, j = grid.nearest(*xz)
        return j*nx+i in keep
    w = variants['base'].walker
    ways = {}
    for name, labels in variants.items():
        for c, band in labels.climbs:
            if built(c.foot_xz) or built(c.top_xz):
                continue
            key = (int(round(c.top_xz[0]/NATURAL_CLUSTER_M)), int(round(c.top_xz[1]/NATURAL_CLUSTER_M)))
            row = ways.setdefault(key, dict(top_m=[round(c.top_xz[0], 3), round(w.t.height(*c.top_xz), 3),
                                                  round(c.top_xz[1], 3)], rise_m=round(c.rise, 3), reached_from={}))
            row['rise_m'] = min(row['rise_m'], round(c.rise, 3))
            got = row['reached_from'].get(name)
            if got is None or band < got:
                row['reached_from'][name] = band
    return sorted(ways.values(), key=lambda r: (min(r['reached_from'].values()), r['top_m']))


def finish(plan, cr, spawn, typed=False, budget=BUDGET):
    """Prove the flights on the planted island and place the Fractals. `cr` is the reach.ClimbReach of the land as it
    will be written. Returns the package block (`x_generator.fractals`)."""
    land = plan.land
    grid = land.grid
    variants = cr.variants()
    base = variants['base']
    w = cr.w
    rng = random.Random(0x4d34)
    benches, route_rows = [], []
    slots = []   # (priority, kind, route id, ledge index, x, z) candidate Fractals on benches
    for route in plan.all_routes():
        row = route.describe(grid)
        for k, ledge in enumerate(row['ledges']):
            x, y, z = ledge['bench_m']
            exp = _expected_band(route, k)
            bands = {name: v.band_at(x, z) for name, v in variants.items()}
            got = bands['base']
            row['ledges'][k]['expected_band'] = exp
            row['ledges'][k]['reached_from'] = bands
            row['ledges'][k]['proved'] = got == exp
            if got is not None and got < exp:
                row['ledges'][k]['bypassed'] = True
            early = {name: b for name, b in bands.items() if b is not None and b < exp}
            row['ledges'][k]['opened_early_by'] = early
            benches.append(dict(route=route.id, kind=route.kind, ledge=k+1, band=exp, rise_m=ledge['rise_m'],
                                bench_m=ledge['bench_m'], reached_from=bands, proved=got == exp))
            if got is not None:
                slots.append((route, k, x, z, got))
        route_rows.append(row)
    items, detail = [], []
    taken = []

    def add(x, z, band, why, route=None, ledge=None):
        y = w.t.height(x, z)    # the land as written, at the Fractal's own place
        if y != y:
            y = land.grid.tri_height(land.h, x, z)
        item = dict(id=fractal_id(x, z), position_m=[round(x, 4), round(y, 4), round(z, 4)])
        if band is not None:
            item['reach_band'] = band
        items.append(item)
        info = dict(id=item['id'], why=why, reach_band=band,
                    reached_from={name: v.band_at(x, z) for name, v in variants.items()})
        if route is not None:
            info.update(route=route.id, ledge=ledge+1)
        detail.append(info)
        taken.append((x, z))
    # On benches: every ledge of the ladders, the shortcuts, the steps, the summit's platform.
    for route, k, x, z, got in slots:
        if route.kind == 'summit':
            if k == len(route.ledges)-1:
                add(x, z, got, 'summit', route, k)
        else:
            add(x, z, got, route.kind, route, k)
    # Hidden corners the feet reach, to make the first run's count.
    first_run = sum(1 for it in items if it.get('reach_band') == 1)
    want = max(0, budget['reachable_first_run']-first_run)

    def jitter(i, j):
        return ((i*73856093 ^ j*19349663) & 0xffff)/65535.
    for x, z in hidden_corners(land, base, taken, want, spawn, jitter):
        add(x, z, 1, 'corner')
    # One on high ground no band reaches: only the Gubble's lift or a built ladder (the design's other ways).
    top = high_unreached(land, base, plan.keep)
    if top is not None and len(items) < budget['max']:
        add(top[0], top[1], None, 'high')
    # The budget's ceiling, then typed kinds.
    items = items[:budget['max']] if len(items) > budget['max'] else items
    ids = {it['id'] for it in items}
    detail = [d for d in detail if d['id'] in ids]
    if len(ids) != len(items):
        raise ValueError('two Fractals share an id')
    if typed:
        for it in items:
            it['kind'] = 'heart' if int(it['id'][-1], 16) % 2 == 0 else 'band'
    items.sort(key=lambda it: it['id'])
    detail.sort(key=lambda d: d['id'])
    by_band = {}
    for it in items:
        key = str(it['reach_band']) if 'reach_band' in it else 'none'
        by_band[key] = by_band.get(key, 0)+1
    shortcuts = []
    for route in plan.routes:
        if route.kind != 'shortcut':
            continue
        row = next(r for r in route_rows if r['id'] == route.id)
        ledge = row['ledges'][0]
        reached = ledge['reached_from']
        d = next((d for d in detail if d.get('route') == route.id), None)
        shortcuts.append(dict(
            route=route.id, rise_m=ledge['rise_m'], bench_m=ledge['bench_m'],
            base_band=reached['base'], one_knack_band=reached['one_knack'], pair_band=reached['pair'],
            over_band=route.over,
            base_falls_short_m=round(ledge['rise_m']-gates.cap(route.over), 4),
            knack_clears_by_m=round(gates.cap(route.over, gates.FULL_RANK)-ledge['rise_m'], 4),
            fractal=None if d is None else d['id']))
    summit_row = None
    if plan.summit is not None:
        sr = next(r for r in route_rows if r['id'] == 'summit')
        summit_row = dict(landmark=plan.summit.landmark, ledges=len(sr['ledges']), length_m=sr['length_m'],
                          top_band=max(l['band'] for l in sr['ledges']), top_m=sr['ledges'][-1]['bench_m'],
                          foot_level_m=sr['foot_level_m'], height_m=round(sr['ledges'][-1]['level_m']-sr['foot_level_m'], 3),
                          reached_from=sr['ledges'][-1]['reached_from'])
    counts = dict(placed=len(items), by_band=dict(sorted(by_band.items())), benches=len(benches),
                  benches_proved=sum(1 for b in benches if b['proved']),
                  benches_bypassed=sum(1 for r in route_rows for l in r['ledges'] if l.get('bypassed')),
                  routes=len(route_rows),
                  budget=budget, reached_nodes={name: v.histogram() for name, v in variants.items()})
    return dict(typed=typed, budget=budget, items=items, detail=detail, routes=route_rows, benches=benches,
                shortcuts=shortcuts, natural=natural_ways(plan, variants), summit=summit_row, counts=counts,
                notes=list(plan.notes),
                arithmetic=gates.describe_arithmetic())


# ------------------------------------------------------------------------------------------- the report


def report_text(block):
    """The reach report for the founder (markdown): what was placed at each band, every gate and shortcut, and where
    the arithmetic of the two kinds of gate gives out."""
    c = block['counts']
    out = ['# Reach report: Fractals and gates', '',
           'Placed %d Fractals (budget %d to %d, %d reachable on a first run); by the lowest band that reaches them: %s.'
           % (c['placed'], block['budget']['min'], block['budget']['max'], block['budget']['reachable_first_run'],
              ', '.join('band %s: %d' % (k, v) for k, v in c['by_band'].items())),
           '%d benches in %d routes, %d proved by the exact walk (the band each was built as is the band that reaches it)%s.'
           % (c['benches'], c['routes'], c['benches_proved'],
              '; %d reached sooner than built, by the land beside the summit corridor (marked below)' % c['benches_bypassed']
              if c.get('benches_bypassed') else ''), '']
    if block['notes']:
        out += ['Notes: '+'; '.join(block['notes'])+'.', '']
    out += ['## Knack shortcuts', '',
            'A higher ledge one climb knack at full rank (Big bar or Steady climber, +20 percent) opens a band sooner and the '
            'base body does not. Nothing is reachable only with a knack.', '',
            '| Route | Rise (m) | Base body | One knack | Strongest pair | Base falls short by | Knack clears by | Fractal |',
            '|---|---|---|---|---|---|---|---|']
    for s in block['shortcuts']:
        out.append('| %s | %.3f | band %s | band %s | band %s | %.3f m | %.3f m | %s |' % (
            s['route'], s['rise_m'], s['base_band'], s['one_knack_band'], s['pair_band'], s['base_falls_short_m'],
            s['knack_clears_by_m'], s['fractal']))
    out += ['', '## The gates', '',
            'A band gate is a rise of the band less %.2f m. The table says from which lower band a body stretched by a '
            'knack climbs it (nothing listed: no knack opens it early).' % gates.GATE_SLACK_M, '',
            '| Band | Reach (m) | Gate rise (m) | Opened from a lower band by |', '|---|---|---|---|']
    for g in block['arithmetic']['gates']:
        early = ', '.join('%s from band %s' % (k, v) for k, v in g['opened_from'].items()) or 'none'
        out.append('| %d | %.2f | %.2f | %s |' % (g['band'], g['cap_m'], g['gate_rise_m'], early))
    out += ['', '## Routes', '']
    for r in block['routes']:
        out.append('- **%s** (%s), %.1f m long, from ground at %.2f m:' % (r['id'], r['kind'], r['length_m'], r['foot_level_m']))
        for k, l in enumerate(r['ledges']):
            early = '' if r['kind'] == 'shortcut' or not l['opened_early_by'] else                 ', opened early by '+', '.join('%s (band %s)' % kv for kv in l['opened_early_by'].items())
            if r['kind'] == 'shortcut':
                early = ', one knack at full rank from band %s, the strongest pair from band %s' % (
                    l['reached_from']['one_knack'], l['reached_from']['pair'])
            out.append('  - ledge %d: rise %.2f m (needs band %d), bench at %s, base body from band %s%s%s' % (
                k+1, l['rise_m'], l['band'], l['bench_m'], l['reached_from']['base'],
                '' if l['proved'] else ' (NOT the band it was built as)', early))
    if block.get('natural'):
        out += ['', '## Ways up the land itself offers', '',
                'Climbs the exact walk found outside the built flights (a real room may offer shortcuts of its own geometry), one '
                'per place the climb ends: the rise, and the lowest band each body takes it from (none: not from any band).',
                '', '| Top at (x, y, z) | Rise (m) | Base body | One knack | Strongest pair |', '|---|---|---|---|---|']
        for n in block['natural']:
            r = n['reached_from']
            out.append('| %s | %.2f | %s | %s | %s |' % (n['top_m'], n['rise_m'], r.get('base', 'none'), r.get('one_knack', 'none'),
                                                        r.get('pair', 'none')))
    if block['summit']:
        s = block['summit']
        out += ['', '## The summit', '',
                'A corridor of %d ledges cut through %s to %.2f m above its foot, %.1f m long; the top is reached from band %s '
                '(one knack: %s, the strongest pair: %s); Bubble lift or a built ladder reach it sooner.'
                % (s['ledges'], s['landmark'], s['height_m'], s['length_m'], s['reached_from']['base'],
                   s['reached_from']['one_knack'], s['reached_from']['pair'])]
    return '\n'.join(out)+'\n'
