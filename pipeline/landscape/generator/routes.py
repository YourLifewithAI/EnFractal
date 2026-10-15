"""Routes: a flight of ledges built into the base grid.

A route is a straight flight along one of the grid's four axes, so every riser is a row of cells and its faces are the
same all the way across. It starts where the ground is (the first riser's foot is the ground) and rises ledge by ledge:

    riser  a steep face (66 degrees or more) that climbs `rise` metres over `riser_cells` cells,
    bench  a flat ledge `bench_cells` long and `2*half+1` vertices wide that the walker can stand on,

each bench the foot of the next riser. Every vertex of the flight is set to the profile's height (cut and fill alike);
beyond the flight's edge the land is as it was, so a bench standing out of flat ground has a cliff on every side (a
step of one cell, 75 degrees or steeper). A flight is a gate only if no way round it exists, so freestanding flights
stand on flat ground and the summit's corridor is cut into a cliff.

Everything is metres. `describe` writes the flight for the package (`x_generator.fractals.routes`): where each bench
is, the rise below it and the band that rise needs.
"""
import math

from . import gates

RISER_MIN_DEG = 66.     # a riser is at least this steep (the climb rule's face is 55, the body's floor is 45)
BENCH_M = 1.2           # a bench's length along the flight
HALF_CELLS = 7          # a bench is 2*7+1 vertices wide: 1.4 m between its outer vertices
FOOT_BACK_M = .08       # a ledge's foot: this far before its riser, on the level below
_TAN_MIN = math.tan(math.radians(RISER_MIN_DEG))
AXES = ((1, 0), (-1, 0), (0, 1), (0, -1))


class Ledge:
    """One riser and the bench on top of it."""
    __slots__ = ('rise', 'band', 'riser_cells', 'bench_cells')

    def __init__(self, rise, band, riser_cells, bench_cells):
        self.rise, self.band, self.riser_cells, self.bench_cells = rise, band, riser_cells, bench_cells


def riser_cells(rise, cell):
    """How many cells a riser of `rise` metres runs over: the most that keeps its face at 66 degrees or more."""
    return max(1, int(math.floor(rise/_TAN_MIN/cell+1e-9)))


def make_ledges(rises, cell, bench_m=BENCH_M, last_bench_m=None):
    """Ledges for [(rise, band)] (band: what the rise needs): the last bench may be longer (`last_bench_m`)."""
    out = []
    for k, (rise, band) in enumerate(rises):
        length = bench_m if k < len(rises)-1 or last_bench_m is None else last_bench_m
        out.append(Ledge(rise, band, riser_cells(rise, cell), max(1, int(round(length/cell)))))
    return out


class Route:
    """A flight: origin vertex (i0, j0) (where the first riser starts, its foot's level `z0`), a direction along an
    axis, the ledges, the half width in cells. `kind` names what it is for (ladder, step, shortcut, summit)."""

    def __init__(self, kind, i0, j0, di, dj, z0, ledges, half=HALF_CELLS, ident=None):
        if (di, dj) not in AXES:
            raise ValueError('a flight runs along an axis')
        self.kind, self.i0, self.j0, self.di, self.dj, self.z0 = kind, i0, j0, di, dj, z0
        self.ledges, self.half, self.id = list(ledges), half, ident
        self.tower = None   # a summit's corridor: the stair built on its landmark's top (a second Route), if it has one
        self.stair = False  # this flight is such a stair: built over a landform's top, which the footprint rule reads without it
        self.li, self.lj = -dj, di
        # Cell counts where each riser and bench begins, and the level above each.
        self.starts, self.levels = [], [z0]
        s = 0
        for ledge in self.ledges:
            self.starts.append(s)
            s += ledge.riser_cells+ledge.bench_cells
            self.levels.append(self.levels[-1]+ledge.rise)
        self.length = s   # cells: the flight runs over s = 0 .. length

    # ---------------------------------------------------------------- the profile
    def level_at(self, s):
        """The profile's height at s cells along the flight (0 <= s <= length)."""
        for k, (a, ledge) in enumerate(zip(self.starts, self.ledges)):
            top = a+ledge.riser_cells
            if s <= top:
                return self.levels[k]+(self.levels[k+1]-self.levels[k])*(s-a)/ledge.riser_cells
            if s <= top+ledge.bench_cells:
                return self.levels[k+1]
        return self.levels[-1]

    def vertex(self, s, m, nx):
        """The grid vertex index at s cells along and m cells across."""
        return (self.j0+self.dj*s+self.lj*m)*nx+(self.i0+self.di*s+self.li*m)

    def cells(self, nx):
        """Every vertex of the flight, as (index, s, m)."""
        for s in range(self.length+1):
            for m in range(-self.half, self.half+1):
                yield self.vertex(s, m, nx), s, m

    def fits(self, grid, pad=1):
        """Whether the whole flight, and `pad` cells round it, lies inside the grid."""
        last = self.length
        for s in (0, last):
            for m in (-self.half, self.half):
                i = self.i0+self.di*s+self.li*m
                j = self.j0+self.dj*s+self.lj*m
                if not (pad <= i < grid.nx-pad and pad <= j < grid.nz-pad):
                    return False
        # A straight flight's corners are its extremes.
        return True

    def apply(self, grid, h):
        """Set every vertex of the flight to the profile. Returns (deepest cut, highest fill) in metres."""
        nx = grid.nx
        cut = fill = 0.
        for q, s, m in self.cells(nx):
            new = self.level_at(s)
            d = new-h[q]
            if d < cut:
                cut = d
            if d > fill:
                fill = d
            h[q] = new
        return -cut, fill

    # ---------------------------------------------------------------- places
    def xz(self, grid, s, m=0.):
        """World (x, z) at s cells along (may be fractional, or negative: before the flight) and m across."""
        return (grid.x0+(self.i0+self.di*s+self.li*m)*grid.cell, grid.z0+(self.j0+self.dj*s+self.lj*m)*grid.cell)

    def bench_centre(self, grid, k):
        """World (x, z) of the middle of ledge k's bench."""
        a, ledge = self.starts[k], self.ledges[k]
        return self.xz(grid, a+ledge.riser_cells+ledge.bench_cells/2.)

    def foot(self, grid, k):
        """World (x, z) of ledge k's foot: just before its riser, on the level below."""
        return self.xz(grid, self.starts[k]-FOOT_BACK_M/grid.cell)

    def vertices(self, nx):
        return {q for q, _, _ in self.cells(nx)}

    def bench_vertices(self, nx):
        """Vertices of every bench (the flat, standable part)."""
        out = set()
        for a, ledge in zip(self.starts, self.ledges):
            for s in range(a+ledge.riser_cells, a+ledge.riser_cells+ledge.bench_cells+1):
                for m in range(-self.half, self.half+1):
                    out.add(self.vertex(s, m, nx))
        return out

    def describe(self, grid, h=None):
        """The flight for the package: the ledges' benches, the rise below each and the band it needs."""
        ledges = []
        for k, ledge in enumerate(self.ledges):
            cx, cz = self.bench_centre(grid, k)
            fx, fz = self.foot(grid, k)
            row = dict(band=ledge.band, rise_m=round(ledge.rise, 4), level_m=round(self.levels[k+1], 4),
                       bench_m=[round(cx, 4), round(self.levels[k+1], 4), round(cz, 4)],
                       foot_m=[round(fx, 4), round(fz, 4)], bench_length_m=round(ledge.bench_cells*grid.cell, 4),
                       riser_run_m=round(ledge.riser_cells*grid.cell, 4))
            ledges.append(row)
        ox, oz = self.xz(grid, 0)
        row = dict(id=self.id, kind=self.kind, origin_m=[round(ox, 4), round(oz, 4)], direction=[self.di, self.dj],
                   foot_level_m=round(self.z0, 4), half_width_m=round(self.half*grid.cell, 4),
                   length_m=round(self.length*grid.cell, 4), ledges=ledges)
        if self.stair:
            row['stair'] = True
        return row


def ladder(kind, grid, i0, j0, di, dj, z0, bands, ident=None, last_bench_m=None):
    """A ladder of gates: ledge k is the gate of band bands[k] (rises `gates.gate_rise`)."""
    return Route(kind, i0, j0, di, dj, z0,
                 make_ledges([(gates.gate_rise(b), b) for b in bands], grid.cell, last_bench_m=last_bench_m),
                 ident=ident)


def shortcut(grid, i0, j0, di, dj, z0, band, ident=None):
    """A knack shortcut over `band`: one higher ledge (`gates.shortcut_rise`), the base body of `band` falls short of."""
    rise = gates.shortcut_rise(band)
    need = gates.band_for(rise)
    route = Route('shortcut', i0, j0, di, dj, z0, make_ledges([(rise, need)], grid.cell), ident=ident)
    route.over = band   # the band whose bar falls short of it
    return route


def summit(grid, i0, j0, di, dj, z0, rises, ident=None, last_bench_m=None):
    """The summit's corridor: the ladder up `rises` [(rise, band)] in the order given."""
    return Route('summit', i0, j0, di, dj, z0, make_ledges(rises, grid.cell, last_bench_m=last_bench_m), ident=ident)
