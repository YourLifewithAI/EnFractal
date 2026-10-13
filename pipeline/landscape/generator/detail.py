"""Multi-scale ground (the 10x world, packet S4): a 10 cm base grid with sparse
2.5 cm detail blocks of 1 m where a body-sized feature lies.

At 10x the island is about 0.6 million base vertices on a 10 cm grid; a 2.5 cm
grid everywhere would be ten million. The body meets the land in a small part
of it: the paths, the streams and their banks, the tarn's shore, the beaches
and their wading shelf, the hamlet, the jetty's landing, the clearings where a
body arrives, and the lips of the climbs. There the ground is worked again at
2.5 cm in blocks of 1 m (`Detail`), stitched to the base without a crack.

How the detail is made. The generator shapes the base grid as before; every
step that shapes the ground (the coast, the jetty's landing, the tarn, the
streams, the glades, the cottages' pads, the paths and their grading, the
tarn's rim) also records what it did as a rule that can be applied at any
point: its decided numbers (a level, a path's graded line, a stream's
surface) are the base's, and only where the rule is applied changes. Once the
base is done the blocks are chosen round those features, and every rule is
replayed, in order, on the blocks' 2.5 cm vertices, starting from the same
landforms (today's island magnified, read at each fine point exactly as the
base reads it). Thin rock a climber reaches is planed there at the body's
size (`lips`).

The seam. A block's vertices on a side that meets base ground are set on the
straight line between the two base vertices at its ends, so the base
triangle beside it is split into a fan through them (`assemble`): the same
plane, no T-junction, no crack. Base vertices inside a block take the
block's height, so a base vertex is never two heights.

Walks stay fast: the base grid is still one regular grid (the checks vouch
for its plain cells as before), and the blocks are regular 2.5 cm grids the
checks vouch for the same way (`checks.Terrain`, the package's
`x_generator.detail`).
"""
import math

from .field import Grid, clamp, lerp, smooth
from . import sea as Sea

R = 4          # fine cells per base cell (10 cm -> 2.5 cm)
B = 10         # base cells per block side (1 m on the 10 cm grid)
BR = B*R       # fine cells per block side
N1 = BR+1      # fine vertices per block side
LIP_TILE = 4   # detail blocks per side of one reading of the climbs (`Detail.lips`)


class Detail:
    """The detail blocks of one island: recorded rules, then the blocks, their
    fine vertices, heights and surface, and the stitched land mesh."""

    def __init__(self, grid, W, noise, landform):
        self.g = grid
        self.W = W
        self.noise = noise
        self.fc = grid.cell/R
        self.fnx = (grid.nx-1)*R+1
        self.fnz = (grid.nz-1)*R+1
        self.landform = landform    # (xs, zs) -> rows of (h, base, owner, acc), as the base reads them
        self.ops = []               # rules replayed on the fine vertices, in order
        self.marks = {}             # block -> set of reasons
        self.lip_marks = set()
        self.built = False
        # Per fine vertex (filled by build): position and fields.
        self.x, self.z, self.h = [], [], []

    # ------------------------------------------------------------ blocks
    def block_range(self):
        g = self.g
        return (g.nx-1)//B, (g.nz-1)//B

    def mark_box(self, x0, x1, z0, z1, why):
        """Mark every block a box reaches."""
        g, size = self.g, self.g.cell*B
        nbx, nbz = self.block_range()
        bi0 = max(0, int(math.floor((x0-g.x0)/size)))
        bi1 = min(nbx-1, int(math.floor((x1-g.x0)/size)))
        bj0 = max(0, int(math.floor((z0-g.z0)/size)))
        bj1 = min(nbz-1, int(math.floor((z1-g.z0)/size)))
        marks = self.marks
        for bj in range(bj0, bj1+1):
            for bi in range(bi0, bi1+1):
                got = marks.get((bi, bj))
                if got is None:
                    marks[(bi, bj)] = {why}
                else:
                    got.add(why)

    def mark_line(self, pts, r, why):
        for x, z in pts:
            self.mark_box(x-r, x+r, z-r, z+r, why)

    def mark_vertex(self, q, why):
        g = self.g
        x, z = g.xs[q % g.nx], g.zs[q//g.nx]
        self.mark_box(x, x, z, z, why)

    def block_of(self, x, z):
        """The block (bi, bj) holding (x, z), or None off the grid's blocks."""
        g = self.g
        size = g.cell*B
        bi, bj = int(math.floor((x-g.x0)/size)), int(math.floor((z-g.z0)/size))
        nbx, nbz = self.block_range()
        if 0 <= bi < nbx and 0 <= bj < nbz:
            return bi, bj
        return None

    # ------------------------------------------------------------ the fine vertices
    def _make_vertices(self):
        g, fc = self.g, self.fc
        nx = g.nx
        fid = {}
        X, Z = self.x, self.z
        loc, own = {}, {}
        gx0, gz0 = g.x0, g.z0
        for b in self.blocks:
            bi, bj = b
            fi0, fj0 = bi*BR, bj*BR
            ids = [0]*(N1*N1)
            mine = bytearray(N1*N1)
            for lj in range(N1):
                fj = fj0+lj
                z = g.zs[fj//R] if fj % R == 0 else round(gz0+fj*fc, 6)
                row = lj*N1
                for li in range(N1):
                    fi = fi0+li
                    key = fj*self.fnx+fi
                    k = fid.get(key)
                    if k is None:
                        k = fid[key] = len(X)
                        X.append(g.xs[fi//R] if fi % R == 0 else round(gx0+fi*fc, 6))
                        Z.append(z)
                        mine[row+li] = 1
                    ids[row+li] = k
            loc[b] = ids
            own[b] = mine
        self.fid, self.loc, self.own = fid, loc, own
        n = len(X)
        # Which fine vertices are base vertices (their base index), else -1.
        base_q = [-1]*n
        for key, k in fid.items():
            fj, fi = divmod(key, self.fnx)
            if fi % R == 0 and fj % R == 0:
                base_q[k] = (fj//R)*nx+fi//R
        self.base_q = base_q
        self.n = n

    def span(self, x0, x1, z0, z1):
        """The fine vertices in the fine lattice's span of a box (each once)."""
        g, fc = self.g, self.fc
        fi0 = max(0, int(math.floor((x0-g.x0)/fc)))
        fi1 = min(self.fnx-1, int(math.ceil((x1-g.x0)/fc)))
        fj0 = max(0, int(math.floor((z0-g.z0)/fc)))
        fj1 = min(self.fnz-1, int(math.ceil((z1-g.z0)/fc)))
        if fi1 < fi0 or fj1 < fj0:
            return []
        out = []
        loc, own = self.loc, self.own
        for bj in range(max(0, (fj0-1)//BR), fj1//BR+1):
            for bi in range(max(0, (fi0-1)//BR), fi1//BR+1):
                ids = loc.get((bi, bj))
                if ids is None:
                    continue
                mine = own[(bi, bj)]
                li0, li1 = max(0, fi0-bi*BR), min(BR, fi1-bi*BR)
                lj0, lj1 = max(0, fj0-bj*BR), min(BR, fj1-bj*BR)
                if li1 < li0 or lj1 < lj0:
                    continue
                for lj in range(lj0, lj1+1):
                    row = lj*N1
                    for p in range(row+li0, row+li1+1):
                        if mine[p]:
                            out.append(ids[p])
        return out

    def interp(self, field):
        """A base per-vertex field read bilinearly at every fine vertex."""
        g = self.g
        nx = g.nx
        out = [0.]*self.n
        for b in self.blocks:
            bi, bj = b
            i0, j0 = bi*B, bj*B
            ids, mine = self.loc[b], self.own[b]
            for lj in range(N1):
                jj, v = (lj//R, (lj % R)/R) if lj < BR else (B-1, 1.)
                r0, r1 = (j0+jj)*nx+i0, (j0+jj+1)*nx+i0
                row = lj*N1
                for li in range(N1):
                    p = row+li
                    if not mine[p]:
                        continue
                    ii, u = (li//R, (li % R)/R) if li < BR else (B-1, 1.)
                    a, b_, c, d = field[r0+ii], field[r0+ii+1], field[r1+ii], field[r1+ii+1]
                    out[ids[p]] = (a*(1-u)+b_*u)*(1-v)+(c*(1-u)+d*u)*v
        return out

    # ------------------------------------------------------------ recorded rules
    def op(self, fn):
        """A rule to replay on the fine vertices; once they are built, it is applied now too."""
        self.ops.append(fn)
        if self.built:
            fn()

    def coast(self, plan, capped):
        """carve_coast at each fine vertex: the coast's plan read bilinearly
        from the base (the wall's distance, the coast, the reef, the beach and
        headland weights: smooth at the room's size), the cap pointwise, in
        every block where the base's cap bit (`capped`: base vertices whose
        height the cap lowered) and the blocks beside them."""
        noise = self.noise
        near = set()
        g = self.g
        for q in capped:
            i, j = q % g.nx, q//g.nx
            for bi in (i//B-1, i//B, (i-1)//B, (i+1)//B):
                for bj in (j//B-1, j//B, (j-1)//B, (j+1)//B):
                    near.add((bi, bj))

        def run():
            S, C, Rf, BW, HL = (self.interp(plan[k]) for k in ('s', 'c', 'reef', 'bw', 'hl'))
            self.S, self.C, self.BW, self.HL = S, C, BW, HL
            H, X, Z = self.h, self.x, self.z
            cap_store = self.sea_cap = {}
            shore, smooth_, lerp_ = Sea._shore, smooth, lerp
            ks = [k for b in self.blocks if b in near for k, m in zip(self.loc[b], self.own[b]) if m]
            for k in ks:
                s, c, reef = S[k], C[k], Rf[k]
                d = s-c
                x, z = X[k], Z[k]
                n = noise(x*3.7+5, z*3.7-9)
                past = s-reef
                shelf = Sea.SEA_Y-(Sea.LAGOON_DEPTH+.04*n)-(Sea.DEEP_DEPTH-Sea.LAGOON_DEPTH)*smooth_((past+.1)/Sea.SHELF_FALL)
                deep = Sea.SEA_Y-(Sea.DEEP_DEPTH+.04*n)
                floor = lerp_(shelf, deep, smooth_((HL[k]-Sea.CLIFF_DEEP[0])/Sea.CLIFF_DEEP[1]))
                cap = max(shore(d, HL[k], BW[k]), floor)
                if cap < H[k]:
                    H[k] = cap
                if d > .03:
                    cap_store[k] = H[k]
        self.op(run)

    def sea_cap(self):
        def run():
            H = self.h
            for k, v in self.sea_cap.items():
                if v < H[k]:
                    H[k] = v
        self.op(run)

    def jetty(self, jetty):
        if jetty is None:
            return
        rx_, rz_ = jetty['root']
        level = jetty['top']-.004
        Rr, blend = .09, .22
        self.mark_box(rx_-Rr-blend, rx_+Rr+blend, rz_-Rr-blend, rz_+Rr+blend, 'jetty')

        def run():
            H, X, Z, bench = self.h, self.x, self.z, self.bench
            for k in self.span(rx_-Rr-blend, rx_+Rr+blend, rz_-Rr-blend, rz_+Rr+blend):
                d = math.hypot(X[k]-rx_, Z[k]-rz_)
                t = smooth((d-Rr)/blend)
                H[k] = lerp(level, H[k], t)
                if 1-t > bench[k]:
                    bench[k] = 1-t
        self.op(run)

    def lake_fill(self, lake, level, ws, Rspan):
        from .water import lake_rho
        noise = self.noise

        def run():
            H, X, Z, wet = self.h, self.x, self.z, self.wet
            m = min(lake['rx'], lake['rz'])
            for k in self.span(lake['x']-Rspan, lake['x']+Rspan, lake['z']-Rspan, lake['z']+Rspan):
                r = lake_rho(lake, noise, X[k], Z[k])
                if r < 1.0:
                    depth = .05*(1-r*r)**.6+.006
                    H[k] = min(H[k], level-depth)
                    wet[k] = 0.
                elif r < 1.35:
                    shore = level+.004+.10*ws*(r-1.0)
                    if H[k] < level+.006:
                        H[k] = level+.006
                    H[k] = min(H[k], max(shore, level+.006))
                    wet[k] = min(wet[k], (r-1.0)*m)
        self.op(run)

    def carve(self, pts, W, halves, depths, bank, keep_lake):
        """water.carve at each fine vertex: the nearest stream sample within
        its reach (the first on a tie), its surface, bed and bank."""
        from .water import BANK_CUT, FREEBOARD, bank_height, lake_rho
        noise = self.noise
        for (x, z), hw in zip(pts, halves):
            r = hw+.16+.05
            self.mark_box(x-r, x+r, z-r, z+r, 'stream')

        def run():
            H, X, Z, wet, face = self.h, self.x, self.z, self.wet, self.face
            last = len(pts)-1
            for k, (d, k_, t) in self._along(pts, lambda m: halves[m]+.16).items():
                k1 = min(k_+1, last)
                hw = halves[k_]+(halves[k1]-halves[k_])*t
                if d > hw+.16:
                    continue
                if k_ == 0 and t == 0. and last > 0 and \
                        (X[k]-pts[0][0])*(pts[1][0]-pts[0][0])+(Z[k]-pts[0][1])*(pts[1][1]-pts[0][1]) < 0:
                    continue   # behind the spring: the water rises there, and the land behind it stays
                if keep_lake is not None and lake_rho(keep_lake, noise, X[k], Z[k]) < 1.:
                    continue
                level, dp = W[k_]+(W[k1]-W[k_])*t, depths[k_]+(depths[k1]-depths[k_])*t
                if d < hw:
                    bed = level-dp*(1-(d/hw)**2)-.004
                    H[k] = min(H[k], bed)
                else:
                    cap = level+FREEBOARD*.5+bank*(d-hw)
                    if H[k]-cap > 2*BANK_CUT:
                        face[k] = True     # a landform's face the stream runs beside, not its bank
                    H[k] = bank_height(H[k], cap)
                    H[k] = max(H[k], level+.004)
                wet[k] = min(wet[k], max(0., d-hw))
        self.op(run)

    def tarn_bed(self, lake, Rspan, weight, frame_angle, ray, deep, streams):
        """deepen_tarn at each fine vertex: the bed inside the shore, the low cut
        bank over each drop-off (open where a stream carries the water on)."""
        from .water import lake_rho, pond_depth, pond_reach
        noise = self.noise
        level = lake['level']
        chan = [(x, z, hw+.05) for st in streams
                for (x, z), hw in zip(st['pts'], st['half'] if isinstance(st['half'], list) else [st['half']]*len(st['pts']))]

        def run():
            H, X, Z, wet = self.h, self.x, self.z, self.wet
            ks = self.span(lake['x']-Rspan, lake['x']+Rspan, lake['z']-Rspan, lake['z']+Rspan)
            channel = self._near_points(chan, ks)
            for k in ks:
                x, z = X[k], Z[k]
                r = lake_rho(lake, noise, x, z)
                if r < 1.:
                    t = frame_angle(x, z)
                    w = weight(t)
                    R_t = ray(t)
                    squeeze = max(1., pond_reach(w, deep)/(.75*R_t))
                    local_deep = deep*(1+.07*noise(x*3.1+41, z*3.1-17))
                    depth = pond_depth((1-r)*R_t*squeeze, w, local_deep)+.004
                    H[k] = level-depth
                    wet[k] = 0.
                elif r < 1.2 and k not in channel:
                    w = weight(frame_angle(x, z))
                    lip = .022*(1-w)*(1-smooth((r-1.08)/.12))
                    H[k] = max(H[k], level+.006+lip)
        self.op(run)

    def _along(self, pts, reach_of):
        """For every fine vertex within reach of a line of points: (its
        distance to the line, the segment k from point k to k+1, and t along
        it) of the line's nearest point (a line of one point is that point)."""
        X, Z = self.x, self.z
        best = {}
        n = len(pts)
        hypot = math.hypot
        for k_ in range(n):
            ax, az = pts[k_]
            bx, bz = pts[k_+1] if k_+1 < n else (ax, az)
            dx, dz = bx-ax, bz-az
            L = dx*dx+dz*dz
            r = max(reach_of(k_), reach_of(min(k_+1, n-1)))
            for k in self.span(min(ax, bx)-r, max(ax, bx)+r, min(az, bz)-r, max(az, bz)+r):
                px, pz = X[k]-ax, Z[k]-az
                t = 0. if L == 0 else (px*dx+pz*dz)/L
                t = 0. if t < 0 else 1. if t > 1 else t
                d = hypot(px-t*dx, pz-t*dz)
                got = best.get(k)
                if got is None or d < got[0]:
                    best[k] = (d, k_, t)
        return best

    def _near_points(self, discs, ks=None):
        """The fine vertices within any disc (x, z, r) (among `ks` if given)."""
        allowed = None if ks is None else set(ks)
        out = set()
        X, Z = self.x, self.z
        for x, z, r in discs:
            for k in self.span(x-r, x+r, z-r, z+r):
                if (allowed is None or k in allowed) and math.hypot(X[k]-x, Z[k]-z) <= r:
                    out.add(k)
        return out

    def wet_final(self, wet, sea):
        """The base's last word on water (the sea and every distance to water):
        a fine vertex is sea where it lies under the sea's level in a cell the
        sea reaches; farther than 30 cm from the streams and the tarn read
        here, the base's distance to water is taken."""
        g = self.g

        def run():
            nx = g.nx
            far = self.interp(wet)
            seaf = self.interp([1. if s else 0. for s in sea])
            W_, H = self.wet, self.h
            for k in range(self.n):
                if seaf[k] > 0 and H[k] < Sea.SEA_Y:
                    W_[k] = 0.
                elif W_[k] >= .3 and far[k] < W_[k]:
                    W_[k] = far[k]
            self.sea = [seaf[k] > 0 and H[k] < Sea.SEA_Y for k in range(self.n)]
        self.op(run)

    def glade(self, ex, ez, Rr, blend, level):
        self.mark_box(ex-Rr-blend, ex+Rr+blend, ez-Rr-blend, ez+Rr+blend, 'glade')

        def run():
            H, X, Z, bench = self.h, self.x, self.z, self.bench
            for k in self.span(ex-Rr-blend, ex+Rr+blend, ez-Rr-blend, ez+Rr+blend):
                d = math.hypot(X[k]-ex, Z[k]-ez)
                t = smooth((d-Rr)/blend)
                H[k] = lerp(level, H[k], t)
                if 1-t > bench[k]:
                    bench[k] = 1-t
        self.op(run)

    def flatten(self, x, z, hx, hz, yaw, margin, blend, level, why='hamlet'):
        a = math.radians(yaw)
        c, s = math.cos(a), math.sin(a)
        Rr = max(hx, hz)+margin+blend
        self.mark_box(x-Rr, x+Rr, z-Rr, z+Rr, why)

        def run():
            H, X, Z, bench = self.h, self.x, self.z, self.bench
            for k in self.span(x-Rr, x+Rr, z-Rr, z+Rr):
                dx, dz = X[k]-x, Z[k]-z
                lx, lz = c*dx-s*dz, s*dx+c*dz
                d = max(abs(lx)-hx-margin, abs(lz)-hz-margin, 0.)
                t = smooth(d/blend)
                H[k] = lerp(level, H[k], t)
                if 1-t > bench[k]:
                    bench[k] = 1-t
        self.op(run)

    def hamlet(self, houses):
        """The cottages' pads and footprints (the base's `pads` and `blocked`), and the yards round them."""
        for hh in houses:
            Rr = max(hh['hx'], hh['hz'])+.55
            self.mark_box(hh['x']-Rr, hh['x']+Rr, hh['z']-Rr, hh['z']+Rr, 'hamlet')

        def run():
            X, Z, pads, blocked = self.x, self.z, self.pads, self.blocked
            for hh in houses:
                Rr = max(hh['hx'], hh['hz'])+.1
                a = math.radians(hh['yaw'])
                c, s = math.cos(a), math.sin(a)
                for k in self.span(hh['x']-Rr, hh['x']+Rr, hh['z']-Rr, hh['z']+Rr):
                    dx, dz = X[k]-hh['x'], Z[k]-hh['z']
                    lx, lz = c*dx-s*dz, s*dx+c*dz
                    if abs(lx) <= hh['hx']+.01 and abs(lz) <= hh['hz']+.01:
                        blocked[k] = True
                    d = max(abs(lx)-hh['hx'], abs(lz)-hh['hz'], 0.)
                    pads[k] = max(pads[k], .55*(1-smooth(d/.08)))
        self.op(run)

    def road(self, pts, g, width, reach, fill_only_wet=.1, grade=False):
        """build_road's (or grade_line's) cut and fill at each fine vertex:
        the nearest point of the graded line within reach, its level (read
        along the line between its samples, so the bench is as smooth at
        2.5 cm as the line is)."""
        self.mark_line(pts, reach+.05, 'path')

        def run():
            H, wet, pads, protect, bench = self.h, self.wet, self.pads, self.protect, self.bench
            last = len(pts)-1
            for k, (d, k_, t) in self._along(pts, lambda m: reach).items():
                if d > reach:
                    continue
                level = g[k_]+(g[min(k_+1, last)]-g[k_])*t
                keep_out = 1. if protect[k] else pads[k]
                if keep_out > .3 or wet[k] <= 0.:
                    continue
                # Toward a cottage's pad the cut and fill eases off (at 2.5 cm a hard edge is a step).
                w = (1-smooth((d-width)/(reach-width)))*(1-smooth(keep_out/.3))
                if grade:
                    target = lerp(H[k], level, w)
                    H[k] = max(H[k], target) if wet[k] < fill_only_wet else target
                elif wet[k] < fill_only_wet:
                    H[k] = max(H[k], lerp(H[k], level, w))
                else:
                    H[k] = lerp(H[k], level, w)
                if w > bench[k]:
                    bench[k] = w
        self.op(run)

    def walks_on_base(self, h):
        """Where the body walks (the paths' and graded ways' benches, the
        pads, forecourts and glades, the jetty's landing: the weight each rule
        that shaped it for walking gave a fine vertex), the fine land is eased
        onto the base's own surface (`h`, before any merging), the surface the
        generator's walk was graded on: overlapping benches and a pad's edge,
        sharp at 2.5 cm, are as smooth as the base made them, and every walk the
        base grading proved holds here too."""
        g = self.g
        H, X, Z, ww = self.h, self.x, self.z, self.bench
        for k in range(self.n):
            w = ww[k]
            if w > 0.:
                H[k] = lerp(H[k], g.tri_height(h, X[k], Z[k]), w)

    def paint(self, pts, width):
        """paint_line at each fine vertex (the paths' worn surface), at the body's width."""
        width = max(width, .6*self.fc)

        def run():
            X, Z, paths = self.x, self.z, self.paths
            hypot = math.hypot
            for x, z in pts[::2]:
                for k in self.span(x-width*2, x+width*2, z-width*2, z+width*2):
                    v = 1-smooth((hypot(X[k]-x, Z[k]-z)-width)/width)
                    if v > paths[k]:
                        paths[k] = v
        self.op(run)

    def tarn_rim(self, lake, Rspan, channel_discs):
        from .water import lake_rho
        noise = self.noise
        level = lake['level']

        def run():
            H, X, Z = self.h, self.x, self.z
            ks = self.span(lake['x']-Rspan, lake['x']+Rspan, lake['z']-Rspan, lake['z']+Rspan)
            channel = self._near_points(channel_discs, ks)
            for k in ks:
                if k not in channel and 1. <= lake_rho(lake, noise, X[k], Z[k]) <= 1.35:
                    H[k] = max(H[k], level+.006)
        self.op(run)

    # ------------------------------------------------------------ the shoreline and the tarn's shore
    def mark_water_edges(self, h, plan, lake, sea):
        """Blocks along the beaches' waterline and wading shelf, and round the tarn's shore."""
        from .water import lake_rho
        g = self.g
        nx = g.nx
        bw, hl = plan['bw'], plan['hl']
        for q in range(g.n):
            if Sea.SEA_Y-.26 < h[q] < Sea.SEA_Y+.12 and bw[q]*(1-hl[q]) > .4:
                self.mark_vertex(q, 'beach')
        if lake is not None:
            Rr = 1.3*1.12*max(lake['rx'], lake['rz'])
            rx, rz = g.span(lake['x']-Rr, lake['x']+Rr, lake['z']-Rr, lake['z']+Rr)
            for j in rz:
                for i in rx:
                    r = lake_rho(lake, self.noise, g.xs[i], g.zs[j])
                    if .92 <= r <= 1.04:
                        self.mark_vertex(j*nx+i, 'tarn')

    # ------------------------------------------------------------ build
    def build(self, h, objects, lips_marks=(), wet=None):
        """Choose the blocks, make their fine vertices and replay every rule on
        them (`merge` then hands their heights to the base vertices they
        share). `h` is the base's land before any planing; `wet` the base's
        distance to water."""
        g = self.g
        self.base_h = h[:]
        self.base_wet = wet
        nbx, nbz = self.block_range()
        for q in lips_marks:
            self.mark_vertex(q, 'lip')
        self.blocks = sorted(b for b in self.marks if 0 <= b[0] < nbx and 0 <= b[1] < nbz)
        self.block_set = set(self.blocks)
        self._make_vertices()
        n = self.n
        # The landforms, as the base reads them, at every fine vertex.
        H, BASE, OWN, ACC = [0.]*n, [0.]*n, [-1]*n, [0.]*n
        for b in self.blocks:
            bi, bj = b
            ids, mine = self.loc[b], self.own[b]
            xs = [self.x[ids[li]] for li in range(N1)]
            zs = [self.z[ids[lj*N1]] for lj in range(N1)]
            rows = self.landform(xs, zs)
            for p, (hv, bv, ov, av) in enumerate(rows):
                if mine[p]:
                    k = ids[p]
                    H[k], BASE[k], OWN[k], ACC[k] = hv, bv, ov, av
        self.h, self.base, self.owner, self.acc = H, BASE, OWN, ACC
        self.wet = [9.]*n
        self.bench = [0.]*n
        self.face = [False]*n
        self.pads = [0.]*n
        self.paths = [0.]*n
        self.blocked = [False]*n
        self.sea_cap = {}
        self.S = None
        # Every object's own footprint keeps its landform (generate's `protect` and `floor_at`).
        W = self.W
        from .land import local
        from .field import rr_dist
        protect, floor_at = [False]*n, [-math.inf]*n
        X, Z = self.x, self.z
        for o in objects:
            if o['parent'] is not None:
                continue
            Rr = math.hypot(o['sx'], o['sz'])/2+.05*W
            for k in self.span(o['cx']-Rr, o['cx']+Rr, o['cz']-Rr, o['cz']+Rr):
                if rr_dist(*local(o, X[k], Z[k]), max(o['sx']/2, .06*W), max(o['sz']/2, .06*W), .02*W) <= .03*W:
                    protect[k] = True
                    floor_at[k] = max(floor_at[k], o['top']-max(.15*W, o['sy']/3)+.04*W)
        self.protect, self.floor_at = protect, floor_at
        for run in self.ops:
            run()
        self.built = True

    def merge(self, h):
        """Base vertices inside the blocks take their fine height; a block's
        fine vertices on a side that meets base ground lie on the straight
        line between that side's base vertices (the seam)."""
        H, base_q = self.h, self.base_q
        for k, q in enumerate(base_q):
            if q >= 0:
                h[q] = H[k]
        for k in self.seam_vertices():
            H[k] = self.seam_height(k, h)

    def seam_vertices(self):
        """Fine vertices (not base vertices) on a block side that meets base ground."""
        if getattr(self, '_seam', None) is not None:
            return self._seam
        out = []
        bs = self.block_set
        for b in self.blocks:
            bi, bj = b
            ids = self.loc[b]
            for side, nb in ((0, (bi-1, bj)), (1, (bi+1, bj)), (2, (bi, bj-1)), (3, (bi, bj+1))):
                if nb in bs:
                    continue
                for t in range(1, BR):
                    if t % R == 0:
                        continue
                    li, lj = ((0, t), (BR, t), (t, 0), (t, BR))[side]
                    out.append(ids[lj*N1+li])
        self._seam = sorted(set(out))
        return self._seam

    def seam_height(self, k, h):
        g = self.g
        fi = int(round((self.x[k]-g.x0)/self.fc))
        fj = int(round((self.z[k]-g.z0)/self.fc))
        nx = g.nx
        if fi % R == 0:
            i, j0, t = fi//R, fj//R, (fj % R)/R
            return lerp(h[j0*nx+i], h[(j0+1)*nx+i], t)
        j, i0, t = fj//R, fi//R, (fi % R)/R
        return lerp(h[j*nx+i0], h[j*nx+i0+1], t)

    # ------------------------------------------------------------ what you climb, you can stand on top of
    def lips(self, h, plane_ok, halo=24):
        """What you climb, you can stand on top of, at the body's size: rock
        too thin to stand on is planed with a 10 cm opening at 2.5 cm, then
        15 cm round any climb still topping out on a knife edge
        (climb.give_lips's rule), in every detail block holding a face over 55
        degrees and every block round a climb the base found. The blocks are
        read in tiles of up to 4 x 4, each on one 2.5 cm grid reaching a
        climb's length past them (the base's land where there is no detail:
        `h`, merged). A cut eases to nothing toward a block's side that meets
        base ground. Returns (vertices planed, deepest cut, knife edges left)."""
        from .climb import FACE_DEG, knife_edges, plane_thin_tops
        g, fc = self.g, self.fc
        H, wet, floor_at = self.h, self.wet, self.floor_at
        fsl = self.face_slopes()
        want = {b for b in self.blocks if 'lip' in self.marks.get(b, ())}
        for b in self.blocks:
            if b not in want and any(fsl[k] > FACE_DEG for k in self.loc[b]):
                want.add(b)
        self.lip_blocks = sorted(want)
        tiles = {}
        for b in self.lip_blocks:
            tiles.setdefault((b[0]//LIP_TILE, b[1]//LIP_TILE), []).append(b)
        seam_d = self.seam_distance()
        seam_k = set(self.seam_vertices())
        nxb = g.nx
        moved_all, deepest_all, left = 0, 0., []
        for tile in sorted(tiles):
            grp = tiles[tile]
            here = set(grp)
            bi0, bi1 = min(b[0] for b in grp), max(b[0] for b in grp)
            bj0, bj1 = min(b[1] for b in grp), max(b[1] for b in grp)
            fi0 = max(0, bi0*BR-halo)
            fi1 = min(self.fnx-1, (bi1+1)*BR+halo)
            fj0 = max(0, bj0*BR-halo)
            fj1 = min(self.fnz-1, (bj1+1)*BR+halo)
            gx = Grid(g.x0+fi0*fc, g.z0+fj0*fc, g.x0+fi1*fc, g.z0+fj1*fc, fc)
            gx.nx, gx.nz = fi1-fi0+1, fj1-fj0+1
            gx.n = gx.nx*gx.nz
            gx.xs = [g.xs[fi//R] if fi % R == 0 else round(g.x0+fi*fc, 6) for fi in range(fi0, fi1+1)]
            gx.zs = [g.zs[fj//R] if fj % R == 0 else round(g.z0+fj*fc, 6) for fj in range(fj0, fj1+1)]
            ks, hh, mine = [], [], []
            fid, fnx = self.fid, self.fnx
            for fj in range(fj0, fj1+1):
                for fi in range(fi0, fi1+1):
                    k = fid.get(fj*fnx+fi, -1)
                    ks.append(k)
                    if k >= 0:
                        hh.append(H[k])
                        # A fine vertex of this tile's blocks (a side counts for the blocks either side).
                        mine.append(k not in seam_k and any((bi_, bj_) in here for bi_ in {fi//BR, (fi-1)//BR}
                                                            for bj_ in {fj//BR, (fj-1)//BR}))
                    else:
                        hh.append(g.tri_height(h, gx.xs[fi-fi0], gx.zs[fj-fj0]))
                        mine.append(False)

            def can_move(q):
                return mine[q] and plane_ok(ks[q])

            def max_cut(q):
                k = ks[q]
                return min(max(0., hh[q]-floor_at[k]), 2.*seam_d[k]) if k >= 0 else 0.

            def standable(q):
                k = ks[q]
                if k >= 0:
                    return hh[q] > Sea.SEA_Y+.005 and wet[k] > 0
                fj, fi = divmod(q, gx.nx)
                qb = ((fj+fj0+R//2)//R)*nxb+(fi+fi0+R//2)//R
                return hh[q] > Sea.SEA_Y+.005 and self.base_wet[qb] > 0
            m, d = plane_thin_tops(gx, hh, can_move, 2, max_cut=max_cut)
            moved_all += m
            deepest_all = max(deepest_all, d)
            near = [(g.x0+(bi*BR+BR/2)*fc, g.z0+(bj*BR+BR/2)*fc, .72*BR*fc) for bi, bj in grp]
            bad = [b for b in knife_edges(gx, hh, standable, near=near) if self.block_of(*b['crest']) in here]
            if bad:
                m, d = plane_thin_tops(gx, hh, can_move, 3, [(b['crest'][0], b['crest'][1], .15) for b in bad], max_cut)
                moved_all += m
                deepest_all = max(deepest_all, d)
                bad = [b for b in knife_edges(gx, hh, standable, near=near) if self.block_of(*b['crest']) in here]
            left += bad
            for q, k in enumerate(ks):
                if mine[q]:
                    H[k] = hh[q]
        return moved_all, deepest_all, left

    def seam_distance(self):
        """Per fine vertex, how far it lies from the nearest block side that meets base ground."""
        out = [math.inf]*self.n
        bs, fc = self.block_set, self.fc
        for b in self.blocks:
            bi, bj = b
            sides = [sd for sd, nb in ((0, (bi-1, bj)), (1, (bi+1, bj)), (2, (bi, bj-1)), (3, (bi, bj+1)))
                     if nb not in bs]
            if not sides:
                continue
            ids = self.loc[b]
            for lj in range(N1):
                for li in range(N1):
                    d = min((li, BR-li, lj, BR-lj)[sd] for sd in sides)*fc
                    k = ids[lj*N1+li]
                    if d < out[k]:
                        out[k] = d
        return out

    def _in_blocks(self, xz, blocks):
        return self.block_of(*xz) in set(blocks)

    # ------------------------------------------------------------ the surface
    def surface(self, objects, shadow, noise, weights, tints):
        """Each fine vertex's surface (life.surface and sea.sand_and_seabed on
        each block's own 2.5 cm grid); the base vertices inside a block take
        it too. Returns (weights, tints) per fine vertex."""
        from .life import surface
        n = self.n
        fsl = self.face_slopes()
        sh = self.interp(shadow)
        outside = [v > 0 for v in self.S]
        FW, FT = [None]*n, [None]*n
        zero = [0.]*(N1*N1)
        for b in self.blocks:
            bg = self.block_grid(b)
            L = self.local
            hl = L(b, self.h)
            w, t = surface(bg, hl, L(b, self.base), L(b, fsl), L(b, self.owner), objects, L(b, sh), L(b, self.wet),
                           L(b, self.acc), L(b, outside), L(b, self.paths), L(b, self.pads), zero, noise)
            w, t = Sea.sand_and_seabed(bg, hl, dict(bw=L(b, self.BW), hl=L(b, self.HL)), L(b, self.sea), w, t)
            ids, mine = self.loc[b], self.own[b]
            for p in range(N1*N1):
                if mine[p]:
                    FW[ids[p]], FT[ids[p]] = w[p], t[p]
        for k, q in enumerate(self.base_q):
            if q >= 0:
                weights[q], tints[q] = FW[k], FT[k]
        return FW, FT

    # ------------------------------------------------------------ per block grids
    def block_grid(self, b):
        """A Grid over block b's fine vertices (local index lj*41+li)."""
        ids = self.loc[b]
        X, Z = self.x, self.z
        bg = Grid(X[ids[0]], Z[ids[0]], X[ids[BR]], Z[ids[BR*N1]], self.fc)
        bg.nx = bg.nz = N1
        bg.n = N1*N1
        bg.xs = [X[ids[li]] for li in range(N1)]
        bg.zs = [Z[ids[lj*N1]] for lj in range(N1)]
        return bg

    def local(self, b, field):
        ids = self.loc[b]
        return [field[k] for k in ids]

    def face_slopes(self):
        """Per fine vertex, the steepest fine face touching it."""
        from .field import face_slope_max
        out = [0.]*self.n
        tris = None
        for b in self.blocks:
            bg = self.block_grid(b)
            if tris is None:
                tris = bg.triangles()    # every block's cells are the same, by local index
            ids = self.loc[b]
            got = face_slope_max(bg, self.local(b, self.h), tris)
            for p, s in enumerate(got):
                k = ids[p]
                if s > out[k]:
                    out[k] = s
        return out

    # ------------------------------------------------------------ heights on the land as built
    def ground(self, h):
        return Ground(self, h)

    def fine_height(self, b, x, z):
        """The height on block b's own triangles (Grid.tri_height's arithmetic)."""
        ids = self.loc[b]
        H = self.h
        X0, Z0 = self.x[ids[0]], self.z[ids[0]]
        fc = self.fc
        fx = clamp((x-X0)/fc, 0, BR-.000001)
        fz = clamp((z-Z0)/fc, 0, BR-.000001)
        i, j = int(fx), int(fz)
        u, v = fx-i, fz-j
        p = j*N1+i
        a, b_, c, d = H[ids[p]], H[ids[p+1]], H[ids[p+N1]], H[ids[p+N1+1]]
        if (i+j) % 2 == 0:
            return a+(d-c)*u+(c-a)*v if v > u else a+(b_-a)*u+(d-b_)*v
        return a+(b_-a)*u+(c-a)*v if u+v < 1 else d+(c-d)*(1-u)+(b_-d)*(1-v)

    # ------------------------------------------------------------ the land mesh
    def triangles(self, keep=None):
        """The land's triangles by global vertex id (a base vertex q is q, a
        fine vertex k that is no base vertex is n+k, a fan's centre n+fine+c):
        base cells outside the blocks (split into fans where they meet a
        block's side; `keep(t)`, if given, says which base triangles are
        kept), then the blocks' 2.5 cm cells. Returns (triangles, the
        centres' base triangles, how many come from base cells, fans)."""
        g = self.g
        nx, nz, n = g.nx, g.nz, g.n
        bs = self.block_set
        base_q = self.base_q
        # Global vertex ids: base vertex q is q; a fine vertex that is not a base vertex is n+k.
        gid = [q if q >= 0 else n+k for k, q in enumerate(base_q)]
        tris = []
        fans = 0
        # Fine vertices along each base edge on a seam, in order from the edge's lower to its upper end.
        fid, fnx = self.fid, self.fnx

        def edge_points(i0, j0, i1, j1):
            """Fine vertices strictly between base vertices (i0, j0) and (i1, j1) (one apart along x or z)."""
            out = []
            for t in range(1, R):
                fi = i0*R+(i1-i0)*t
                fj = j0*R+(j1-j0)*t
                k = fid.get(fj*fnx+fi)
                if k is None:
                    return []
                out.append(n+k)
            return out

        def in_block(i, j):
            return 0 <= i < nx-1 and 0 <= j < nz-1 and (i//B, j//B) in bs
        centroids = []
        cbase = n+self.n     # a centroid's id: cbase + its index
        for j in range(nz-1):
            for i in range(nx-1):
                if (i//B, j//B) in bs:
                    continue
                a = j*nx+i
                b_ = a+1
                c = a+nx
                d = c+1
                if (i+j) % 2 == 0:
                    pair = ((a, c, d), (a, d, b_))
                else:
                    pair = ((a, c, b_), (b_, c, d))
                sides = None
                if (i % B == 0 and in_block(i-1, j)) or (i % B == B-1 and in_block(i+1, j)) or \
                        (j % B == 0 and in_block(i, j-1)) or (j % B == B-1 and in_block(i, j+1)):
                    sides = {}
                    if in_block(i-1, j):
                        sides[(a, c)] = edge_points(i, j, i, j+1)
                    if in_block(i+1, j):
                        sides[(b_, d)] = edge_points(i+1, j, i+1, j+1)
                    if in_block(i, j-1):
                        sides[(a, b_)] = edge_points(i, j, i+1, j)
                    if in_block(i, j+1):
                        sides[(c, d)] = edge_points(i, j+1, i+1, j+1)
                if keep is not None:
                    pair = [t for t in pair if keep(t)]
                if not sides:
                    tris.extend(pair)
                    continue
                for t in pair:
                    chain, split, opp = [], 0, None
                    for e in range(3):
                        p0, p1 = t[e], t[(e+1) % 3]
                        chain.append(p0)
                        mid = sides.get((p0, p1))
                        if mid is None:
                            mid = sides.get((p1, p0))
                            mid = None if mid is None else mid[::-1]
                        if mid:
                            chain.extend(mid)
                            split += 1
                            opp = t[(e+2) % 3]
                    if split == 0:
                        tris.append(t)
                    elif split == 1:
                        # Fan from the corner opposite the split side: the same plane.
                        at = chain.index(opp)
                        ring = chain[at:]+chain[:at]
                        for m in range(1, len(ring)-1):
                            tris.append((opp, ring[m], ring[m+1]))
                        fans += 1
                    else:
                        # Two split sides: a point at the triangle's centroid (in its plane), fanned to every edge.
                        cid = cbase+len(centroids)
                        centroids.append(t)
                        for m in range(len(chain)):
                            tris.append((cid, chain[m], chain[(m+1) % len(chain)]))
                        fans += 1
        base_tris = len(tris)
        # The blocks' own cells.
        for b in self.blocks:
            ids = self.loc[b]
            for lj in range(BR):
                for li in range(BR):
                    p = lj*N1+li
                    a, b_, c, d = gid[ids[p]], gid[ids[p+1]], gid[ids[p+N1]], gid[ids[p+N1+1]]
                    if (li+lj) % 2 == 0:
                        tris.append((a, c, d))
                        tris.append((a, d, b_))
                    else:
                        tris.append((a, c, b_))
                        tris.append((b_, c, d))
        return tris, centroids, base_tris, fans

    def assemble(self, h, weights, tints, fweights, ftints):
        """The land mesh (`triangles`), its triangles grouped by their two
        dominant roles as `life.build_mesh` does. Returns (primitives, counts)."""
        g = self.g
        nx, n = g.nx, g.n
        tris, centroids, base_tris, fans = self.triangles()
        cbase = n+self.n
        X, Z, H = self.x, self.z, self.h
        # Every vertex's weights and tint by global id (base, fine, centroid).
        VW = list(weights)+list(fweights)
        VT = list(tints)+list(ftints)
        cpos = []
        for t in centroids:
            ps = [self._pos(v, h) for v in t]
            cpos.append([sum(q[0] for q in ps)/3, sum(q[1] for q in ps)/3, sum(q[2] for q in ps)/3])
            wsum = {}
            for v in t:
                for key, val in VW[v].items():
                    wsum[key] = wsum.get(key, 0.)+val/3
            top = sorted(((val, key) for key, val in wsum.items()), reverse=True)[:2]
            sw = sum(val for val, _ in top) or 1.
            VW.append({key: val/sw for val, key in top})
            VT.append(tuple(round(sum(VT[v][i] for v in t)/3, 4) for i in range(3)))
        groups = {}
        VI = [tuple(w.items()) for w in VW]
        for t in tris:
            score = {}
            for v in t:
                for key, val in VI[v]:
                    score[key] = score.get(key, 0.)+val
            # The two dominant roles (highest score first, a tie by name), as life.build_mesh ranks them.
            a = b_ = None
            va = vb = -1.
            for key, val in score.items():
                if val > va or (val == va and key < a):
                    b_, vb = a, va
                    a, va = key, val
                elif val > vb or (val == vb and key < b_):
                    b_, vb = key, val
            if b_ is not None and vb <= .05:
                b_ = None
            got = groups.get((a, b_))
            if got is None:
                groups[(a, b_)] = [t]
            else:
                got.append(t)
        prims = []
        xs, zs = g.xs, g.zs
        for (a, b_) in sorted(groups, key=lambda k: (k[0], k[1] or '')):
            index = {}
            P, T, BL, out = [], [], [], []
            for t in groups[(a, b_)]:
                loc_ = []
                for v in t:
                    got = index.get(v)
                    if got is None:
                        got = index[v] = len(P)
                        if v < n:
                            P.append([xs[v % nx], round(h[v], 5), zs[v//nx]])
                        elif v < cbase:
                            P.append([X[v-n], round(H[v-n], 5), Z[v-n]])
                        else:
                            cp = cpos[v-cbase]
                            P.append([cp[0], round(cp[1], 5), cp[2]])
                        T.append([*VT[v], 1])
                        w = VW[v]
                        wa = w.get(a, 0.)
                        wb = w.get(b_, 0.) if b_ else 0.
                        BL.append(round(wb/(wa+wb), 4) if wa+wb > 0 else (1. if b_ else 0.))
                    loc_.append(got)
                out.append(loc_)
            prim = dict(role=a, positions=P, triangles=out, tints=T)
            if b_:
                prim['blend_role'] = b_
                prim['blend_weights'] = BL
            prims.append(prim)
        counts = dict(base_triangles=base_tris, detail_triangles=len(tris)-base_tris, seam_fans=fans,
                      centroids=len(centroids), blocks=len(self.blocks), fine_vertices=self.n)
        return prims, counts

    def _pos(self, v, h):
        g, n = self.g, self.g.n
        if v < n:
            return [g.xs[v % g.nx], h[v], g.zs[v//g.nx]]
        return [self.x[v-n], self.h[v-n], self.z[v-n]]

    def describe(self):
        """For the package (`x_generator.detail`): the detail's lattice and its blocks."""
        reasons = {}
        for b in self.blocks:
            for why in self.marks.get(b, ()):
                reasons[why] = reasons.get(why, 0)+1
        g = self.g
        return dict(cell_m=self.fc, block_m=round(g.cell*B, 6), origin_m=[g.x0, g.z0],
                    blocks=[[b[0], b[1]] for b in self.blocks], reasons=dict(sorted(reasons.items())))


def plane_ok_fine(detail, jroot):
    """generate's `plane_ok` at a fine vertex: water, its banks, paths, the
    hamlet, the jetty's landing and the sea past the waterline stay. A
    landform's face a stream runs beside (no bank of it: `Detail.carve`) is
    rock like any other: a fin of it left between the water and lower ground
    is planed."""
    d = detail

    def ok(k):
        if (d.wet[k] < .04 and not (d.face[k] and d.wet[k] > 0)) or d.paths[k] >= .3 or d.pads[k] > 0 or d.blocked[k] or d.S[k] > d.C[k]:
            return False
        return jroot is None or math.hypot(d.x[k]-jroot[0], d.z[k]-jroot[1]) >= .35
    return ok


class Ground:
    """Heights on the land as it is built: a block's own 2.5 cm triangles
    inside a detail block, the base grid's elsewhere (a base cell split into a
    fan along a block's side lies in the same plane). Called like
    `Grid.tri_height(h, x, z)`; any field but the land's heights is read on
    the base grid."""

    def __init__(self, detail, h):
        self.d, self.h, self.g = detail, h, detail.g
        self.size = detail.g.cell*B

    def tri_height(self, f, x, z):
        if f is self.h:
            g, size = self.g, self.size
            b = (int(math.floor((x-g.x0)/size)), int(math.floor((z-g.z0)/size)))
            if b in self.d.block_set:
                return self.d.fine_height(b, x, z)
        return self.g.tri_height(f, x, z)
