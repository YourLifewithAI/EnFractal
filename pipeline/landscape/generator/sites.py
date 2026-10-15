"""Where flights go: the freestanding ones on flat meadow, the summit's corridor into a landmark.

Everything is read off the base grid after the island is made (the coast, the water, the hamlet, the ways) and before
the ground is worked at 2.5 cm: `Land` holds what a flight must keep clear of. A rectangle's verdict is one lookup
(`Rects`, prefix sums), so a few hundred thousand candidate places cost a couple of seconds.

A freestanding flight stands on ground that is flat (the flight is built up from it, so every side is a cliff and no
way round exists), dry, away from every other flight, the hamlet, the ways, the water, the coast and the spawn, and
inside the ground the exact walk reaches from the spawn: its first riser's foot is reached on foot.

The summit's corridor is a flight cut through a landmark's cliff to its top: it may run through the landform's
protected ground (the one exception), but not through anything else, and its last bench, a platform, lies on high
ground.
"""
import math

from . import gates, routes as R

MARGIN_M = .8         # clear, flat ground round a freestanding flight
SPACING_M = 1.5       # between two flights (beyond their margins)
RELIEF_M = .03        # a freestanding flight's ground varies at most this (a shortcut's: SHORTCUT_RELIEF_M)
SHORTCUT_RELIEF_M = .02  # a shortcut's two sides are cliffs of the rise less this at the highest ground: the base body must fall short
SLOPE_DEG = 14.       # ground a freestanding flight stands on is no steeper than this
WET_CLEAR_M = 2.      # from water
VIEW_CLEAR_M = 5.     # from where the player and the Gubble arrive
STRIDE = 5            # candidate origins every 0.5 m
SUMMIT_FILL_M = 2.5   # the corridor stands up from the old land by at most this
SUMMIT_PLATFORM_M = 1.6  # the last bench, on the high ground
SUMMIT_CLEAR_M = 1.   # a summit corridor's clear ground beyond its sides
SUMMIT_BELOW_M = 1.5  # the summit's platform is this close to its landmark's highest ground, at most
WALL_SHARE = .8       # the corridor is walled in (its sides stand over its floor) along this share of its cut


class Land:
    """The base grid and what a flight keeps clear of. Fields are per-vertex lists (grid.nx by grid.nz)."""

    def __init__(self, grid, h, inside, wet, sea, off_coast, protect, pads, hold, paths, blocked, fslope, blocks,
                 views, near, objects):
        self.grid, self.h, self.inside, self.wet, self.sea, self.off_coast = grid, h, inside, wet, sea, off_coast
        self.protect, self.pads, self.hold, self.paths, self.blocked = protect, pads, hold, paths, blocked
        self.fslope, self.blocks, self.views, self.near, self.objects = fslope, blocks, views, near, objects
        self._hard = self._soft = None

    # ---------------------------------------------------------------- what is in the way
    def hard(self):
        """Per vertex: 1 where no flight may touch (outside the floor, the sea and the coast, near water, the
        hamlet's pads and ways, the places the player and the Gubble arrive). Detail blocks are no hindrance: a flight
        inside one keeps the base's surface (`Plan.hold`)."""
        if self._hard is not None:
            return self._hard
        g, n = self.grid, self.grid.n
        nx = g.nx
        bad = bytearray(n)
        inside, sea, off, wet = self.inside, self.sea, self.off_coast, self.wet
        pads, hold, paths, blocked = self.pads, self.hold, self.paths, self.blocked
        for q in range(n):
            if (not inside[q]) or sea[q] or off[q] or wet[q] < WET_CLEAR_M or pads[q] > 0 or hold[q] or blocked[q] \
                    or paths[q] >= .05:
                bad[q] = 1
        for x, z in self.views:
            r = VIEW_CLEAR_M
            rx, rz = g.span(x-r, x+r, z-r, z+r)
            for j in rz:
                for i in rx:
                    if math.hypot(g.xs[i]-x, g.zs[j]-z) <= r:
                        bad[j*nx+i] = 1
        self._hard = bad
        return bad

    def soft(self):
        """`hard`, and ground a freestanding flight may not stand on: a landform's protected ground, and slopes."""
        if self._soft is not None:
            return self._soft
        bad = bytearray(self.hard())
        protect, fslope = self.protect, self.fslope
        for q in range(self.grid.n):
            if protect[q] or fslope[q] > SLOPE_DEG:
                bad[q] = 1
        self._soft = bad
        return bad

    def reached(self, x, z):
        return self.near.has((x, z))


class Rects:
    """Counts of flagged vertices in a rectangle in O(1) (a summed-area table of a bytearray over the grid)."""

    def __init__(self, grid, flags):
        nx, nz = grid.nx, grid.nz
        w = nx+1
        ps = [0]*(w*(nz+1))
        for j in range(nz):
            run = 0
            row, prev = (j+1)*w, j*w
            base = j*nx
            for i in range(nx):
                run += flags[base+i]
                ps[row+i+1] = ps[prev+i+1]+run
        self.ps, self.w, self.nx, self.nz = ps, w, nx, nz

    def count(self, i0, i1, j0, j1):
        """Flagged vertices with i0 <= i <= i1 and j0 <= j <= j1 (clipped to the grid: outside counts as flagged)."""
        nx, nz, w, ps = self.nx, self.nz, self.w, self.ps
        out = 0
        if i0 < 0 or j0 < 0 or i1 > nx-1 or j1 > nz-1:
            out = 1
            i0, j0, i1, j1 = max(0, i0), max(0, j0), min(nx-1, i1), min(nz-1, j1)
            if i1 < i0 or j1 < j0:
                return out
        return out+ps[(j1+1)*w+i1+1]-ps[j0*w+i1+1]-ps[(j1+1)*w+i0]+ps[j0*w+i0]


def flight_box(route, pad=0):
    """The grid-index rectangle (i0, i1, j0, j1) a flight covers, `pad` cells beyond it."""
    corners = []
    for s in (0, route.length):
        for m in (-route.half, route.half):
            corners.append((route.i0+route.di*s+route.li*m, route.j0+route.dj*s+route.lj*m))
    return (min(c[0] for c in corners)-pad, max(c[0] for c in corners)+pad,
            min(c[1] for c in corners)-pad, max(c[1] for c in corners)+pad)


def boxes_apart(a, b, gap):
    """Whether two rectangles (i0, i1, j0, j1) are at least `gap` cells apart."""
    return a[1]+gap < b[0] or b[1]+gap < a[0] or a[3]+gap < b[2] or b[3]+gap < a[2]


class Siting:
    """The places flights take: `place(spec)` finds one for a flight made by `spec(i0, j0, di, dj, z0)`."""

    def __init__(self, land, seed_bits=0):
        self.land = land
        g = land.grid
        self.soft = Rects(g, land.soft())
        self.hard = Rects(g, land.hard())
        self.taken = []       # boxes of the flights placed, with their margin
        self.centres = []     # (x, z) of each flight's middle
        self.bits = seed_bits

    def _jitter(self, i, j, d):
        """A fixed small number from a place, so ties are broken the same way every run."""
        v = (i*73856093 ^ j*19349663 ^ d*83492791 ^ self.bits) & 0xffff
        return v/65535.

    def _flat_ok(self, route):
        land = self.land
        g, h = land.grid, land.h
        nx = g.nx
        pad = int(round(MARGIN_M/g.cell))
        i0, i1, j0, j1 = flight_box(route, pad)
        if self.soft.count(i0, i1, j0, j1):
            return None
        lo = math.inf
        hi = -math.inf
        limit = SHORTCUT_RELIEF_M if route.kind == 'shortcut' else RELIEF_M
        di, dj, li, lj, half = route.di, route.dj, route.li, route.lj, route.half
        for s in range(0, route.length+1, 2):
            bi, bj = route.i0+di*s, route.j0+dj*s
            for m in range(-half, half+1, 2):
                v = h[(bj+lj*m)*nx+bi+li*m]
                if v < lo:
                    lo = v
                if v > hi:
                    hi = v
            if hi-lo > limit:
                return None
        # The approach and the flight are inside the ground the walk reaches from the spawn.
        for s, m in ((-3, 0), (0, 0), (route.length//2, 0), (route.length, 0), (0, -route.half-2), (0, route.half+2)):
            x, z = route.xz(g, s, m)
            if not land.reached(x, z):
                return None
        return lo

    def place(self, make, prefer=None):
        """Find the best place for a flight. `make(i0, j0, di, dj, z0)` builds it; `prefer(x, z, d_taken)` scores a
        place (higher is better; None: spread). Returns the Route placed (its ground level z0 the lowest of its
        ground), or None. The route is added to the places taken."""
        land = self.land
        g = land.grid
        probe = make(0, 0, 1, 0, 0.)
        pad = int(round(MARGIN_M/g.cell))
        gap = int(round(SPACING_M/g.cell))
        best = None
        nx, nz = g.nx, g.nz
        length, half = probe.length, probe.half
        taken, soft = self.taken, self.soft
        inside = land.inside
        for di, dj in R.AXES:
            li, lj = -dj, di
            # The flight's rectangle from its origin: s in 0 .. length along (di, dj), m in -half .. half across.
            ei, ej = di*length, dj*length
            ai = min(0, ei)+min(li*-half, li*half)-pad
            bi = max(0, ei)+max(li*-half, li*half)+pad
            aj = min(0, ej)+min(lj*-half, lj*half)-pad
            bj = max(0, ej)+max(lj*-half, lj*half)+pad
            for j0 in range(max(pad, 1-aj), min(nz-pad, nz-1-bj), STRIDE):
                for i0 in range(max(pad, 1-ai), min(nx-pad, nx-1-bi), STRIDE):
                    if not inside[j0*nx+i0]:
                        continue
                    box = (i0+ai, i0+bi, j0+aj, j0+bj)
                    if soft.count(*box):
                        continue
                    if any(not boxes_apart(box, t, gap) for t in taken):
                        continue
                    route = make(i0, j0, di, dj, 0.)
                    z0 = self._flat_ok(route)
                    if z0 is None:
                        continue
                    cx, cz = route.xz(g, route.length/2.)
                    d_taken = min([math.hypot(cx-x, cz-z) for x, z in self.centres] or [40.])
                    score = (prefer(cx, cz, d_taken) if prefer else min(d_taken, 14.)) + .05*self._jitter(i0, j0, di*3+dj)
                    if best is None or score > best[0]:
                        best = (score, i0, j0, di, dj, z0)
        if best is None:
            return None
        _, i0, j0, di, dj, z0 = best
        route = make(i0, j0, di, dj, z0)
        self.taken.append(flight_box(route, pad))
        self.centres.append(route.xz(g, route.length/2.))
        return route

    def take(self, route):
        """Record a flight placed elsewhere (the summit's) so the others keep apart from it."""
        g = self.land.grid
        self.taken.append(flight_box(route, int(round(MARGIN_M/g.cell))))
        self.centres.append(route.xz(g, route.length/2.))


# ------------------------------------------------------------------------------------------- the summit


def _object_cells(land, obj):
    """The grid-index ranges round a landmark, a couple of metres past its box."""
    g = land.grid
    r = max(obj['sx'], obj['sz'])/2+2.
    return g.span(obj['cx']-r, obj['cx']+r, obj['cz']-r, obj['cz']+r)


def _in_object(g, q, obj):
    """Whether vertex q lies in the landmark's own protected ground (its box, a little rounded, and a metre past)."""
    from .field import rr_dist
    from .land import local
    lx, lz = local(obj, g.xs[q % g.nx], g.zs[q//g.nx])
    return rr_dist(lx, lz, obj['sx']/2, obj['sz']/2, .06*10) <= 1.5


def plan_summit(land, siting, obj, rises_for, stats=None):
    """A corridor through landmark `obj`'s cliff to its top, or None.

    For each axis and each place for the platform (the last bench, 3 m long, on high ground) every half metre across
    the landmark: the platform's level is the high ground under it (the 80th percentile); the flight is the ladder up
    from the ground at its start to that level (`rises_for(height)` gives the ledges [(rise, band)], or None past the
    top band); its start is where that ladder's length puts it, found by trying again with the ground there. A
    candidate stands only if the whole flight and a metre round it are clear of everything but the landmark's own
    protected ground, its start is reached by the walk, the ground in front of its first riser is flat, and it stands
    up from the old land by at most SUMMIT_FILL_M. The best is the one that stands up least and cuts least."""
    g, h = land.grid, land.h
    nx, nz = g.nx, g.nz
    cell = g.cell
    pad = int(round(SUMMIT_CLEAR_M/cell))
    gap = int(round(SPACING_M/cell))
    half = R.HALF_CELLS
    plat = int(round(SUMMIT_PLATFORM_M/cell))
    ri, rj = _object_cells(land, obj)
    # The landmark's own highest ground: the platform lies within SUMMIT_BELOW_M of it.
    top = max([h[j*nx+i] for j in rj for i in ri if _in_object(g, j*nx+i, obj)] or [-math.inf])
    best = None
    for di, dj in R.AXES:
        li, lj = -dj, di
        for je in range(rj.start, rj.stop, STRIDE):
            for ie in range(ri.start, ri.stop, STRIDE):
                # The platform: back from the end vertex (ie, je) along the flight.
                ground = []
                ok = True
                for k in range(0, plat+1, 2):
                    for m in range(-half, half+1, 2):
                        i, j = ie-di*k+li*m, je-dj*k+lj*m
                        if not (1 <= i < nx-1 and 1 <= j < nz-1):
                            ok = False
                            break
                        ground.append(h[j*nx+i])
                    if not ok:
                        break
                if not ok:
                    _n(stats, 'off grid')
                    continue
                ground.sort()
                z_p = ground[int(.8*(len(ground)-1))]
                if ground[0] < z_p-1.2 or z_p < top-SUMMIT_BELOW_M:
                    _n(stats, 'platform over an edge or low')
                    continue    # the platform would stand out over an edge, or lie well under the landmark's top
                # Where the flight starts: iterate, since the ladder's length depends on the ground it starts on.
                length, z0 = 80, None
                for _ in range(5):
                    i0, j0 = ie-di*length, je-dj*length
                    if not (pad+2 <= i0 < nx-pad-2 and pad+2 <= j0 < nz-pad-2):
                        z0 = None
                        break
                    # the ground the first riser rises from: the lowest of the approach behind the origin and the
                    # flight's first cells (so no side of the first riser is taller than its rise)
                    z0 = min(h[(j0+dj*k+lj*m)*nx+i0+di*k+li*m] for k in (-13, -9, -5, -1, 0, 4, 8) for m in (-half, 0, half))
                    rises = rises_for(z_p-z0)
                    if not rises:
                        z0 = None
                        break
                    ledges = R.make_ledges(rises, cell, last_bench_m=SUMMIT_PLATFORM_M)
                    new = sum(l.riser_cells+l.bench_cells for l in ledges)
                    if new == length:
                        break
                    length = new
                else:
                    z0 = None
                if z0 is None:
                    _n(stats, 'no ladder from there')
                    continue
                _n(stats, 'ladders')
                route = R.Route('summit', i0, j0, di, dj, z0, ledges)
                box = flight_box(route, pad)
                if box[0] < 1 or box[2] < 1 or box[1] > nx-2 or box[3] > nz-2 or siting.hard.count(*box):
                    _n(stats, 'hard ground in the way')
                    continue
                if any(not boxes_apart(box, t, gap) for t in siting.taken):
                    _n(stats, 'too near another flight')
                    continue
                if not all(land.reached(*route.xz(g, s, m)) for s, m in ((-3, 0), (-10, 0), (0, -half-2), (0, half+2))):
                    _n(stats, 'start not reached')
                    continue
                approach = [h[route.vertex(-s, m, nx)] for s in range(1, 14, 2) for m in (-half, 0, half)]
                if max(approach)-z0 > 2*RELIEF_M:
                    _n(stats, 'approach not flat')
                    continue
                cut = fill = vol = 0.
                clear = True
                for q, s, m in route.cells(nx):
                    d = route.level_at(s)-h[q]
                    if d > fill:
                        fill = d
                    if d < 0:
                        vol -= d
                        cut = max(cut, -d)
                    if land.protect[q] and not _in_object(g, q, obj):
                        clear = False
                        break
                if not clear:
                    _n(stats, 'other protected ground')
                    continue
                if fill > SUMMIT_FILL_M:
                    _n(stats, 'stands up too far')
                    continue
                if not _walled(land, route):
                    _n(stats, 'not cut into a cliff')
                    continue
                _n(stats, 'candidates')
                score = 10*fill+vol*cell*cell/4.-3*(z_p-z0)
                if best is None or score < best[0]:
                    best = (score, route)
    return None if best is None else best[1]


def _walled(land, route):
    """Whether the corridor is cut into a cliff: where it runs more than 0.4 m under the old land, the old land a little
    beyond both its sides stands at least 0.3 m above its floor (walls, not a slope to walk round), along WALL_SHARE of
    the cut, and the cut is long enough to say."""
    g = land.grid
    nx = g.nx
    cut = walled = 0
    for s in range(1, route.length, 3):
        level = route.level_at(s)
        if land.h[route.vertex(s, 0, nx)]-level < .4:
            continue
        cut += 1
        if all(land.h[route.vertex(s, m, nx)]-level >= .3 for m in (-route.half-3, route.half+3)):
            walled += 1
    return cut >= 5 and walled >= WALL_SHARE*cut


def _n(stats, why):
    if stats is not None:
        stats[why] = stats.get(why, 0)+1
