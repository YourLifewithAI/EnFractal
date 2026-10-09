"""What you climb, you can stand on top of.

A climber grabs a face steeper than about 55 degrees and pulls over where the
ground turns standable again. So a face a body can reach from standable land
should end in a lip it can stand on (about 5 cm of ground at 35 degrees or
less) before the land falls away again, and a needle should not leave a
climber clinging to a point. This is a principle, not a quota: landforms keep
their faces, crests and spires; only a climb that tops out on a knife edge is
found (`knife_edges`) and given a small level cap of rock (`cap_crests`),
the way a hard caprock weathers out on a ridge or a stack.

Lane P's climb sampler (the game's landscape check) reads the land the same
way: from a standable foot, a face over 55 degrees starting within 5 cm along
one of eight headings, then 5 cm of 35 degree ground before the line drops
2 cm under the crest.
"""
import math

FACE_DEG = 55.     # a face a climber grabs
FOOT_DEG = 30.     # a foot a body stands on
LIP_DEG = 35.      # a lip a body stands on
STEEP_DEG = 45.    # past a lip, steep again: the lip did not count
LIP_M = .05        # how deep the lip must be
MIN_RISE = .06     # lower faces are steps, not climbs
DROP_M = .02       # the line falls this far under the crest: over the top
REACH_M = .6       # how far along a heading a climb is followed
BESIDE_M = .1      # standable ground within a body length of a crest is a top a climber pulls onto
HEADINGS = [(math.cos(k*math.pi/4), math.sin(k*math.pi/4)) for k in range(8)]


def tri_hit(grid, h, x, z):
    """(height, slope degrees) on the grid's own triangles at (x, z), or None
    outside the grid."""
    fx = (x-grid.x0)/grid.cell
    fz = (z-grid.z0)/grid.cell
    if fx < 0 or fz < 0 or fx > grid.nx-1 or fz > grid.nz-1:
        return None
    i = min(int(fx), grid.nx-2)
    j = min(int(fz), grid.nz-2)
    u, v = fx-i, fz-j
    nx = grid.nx
    a = h[j*nx+i]
    b = h[j*nx+i+1]
    c = h[(j+1)*nx+i]
    d = h[(j+1)*nx+i+1]
    if (i+j) % 2 == 0:
        if v > u:
            gu, gv = d-c, c-a
            y = a+gu*u+gv*v
        else:
            gu, gv = b-a, d-b
            y = a+gu*u+gv*v
    else:
        if u+v < 1:
            gu, gv = b-a, c-a
            y = a+gu*u+gv*v
        else:
            gu, gv = d-c, d-b
            y = d+(c-d)*(1-u)+(b-d)*(1-v)
    return y, math.degrees(math.atan(math.hypot(gu, gv)/grid.cell))


def _uphill(grid, h, x, z, e=.004):
    a, b = tri_hit(grid, h, x+e, z), tri_hit(grid, h, x-e, z)
    c, d = tri_hit(grid, h, x, z+e), tri_hit(grid, h, x, z-e)
    if None in (a, b, c, d):
        return None
    gx, gz = a[0]-b[0], c[0]-d[0]
    g = math.hypot(gx, gz)
    return None if g < 1e-9 else (gx/g, gz/g)


def climb_from(grid, h, x, z, dx, dz, foot_y, beside=None):
    """Climb from a foot: toward the face along (dx, dz), then straight up its
    fall line while it is steep (as a climber does), then on over the top in
    the last direction. Returns None (no face here, or a face taller than a
    climb is followed), or a dict: `topped` (a lip found), the face's `rise`,
    and where its crest is. A dip in the face that the land climbs back out
    of within 10 cm is a bump on the way up, not the top. `beside(x, z, y)`
    says whether a standable patch lies beside a crest (a climber pulls over
    sideways onto it)."""
    started = False
    crest, crest_at = -math.inf, None
    flat = 0
    over = None   # where the line first fell under the crest
    r = .02
    px, pz = x+dx*r, z+dz*r
    while r <= REACH_M+1e-9:
        here = tri_hit(grid, h, px, pz)
        if here is None:
            return None
        y, s = here
        if not started:
            if s > FACE_DEG:
                started = True
            elif r > .06+1e-9:
                return None
        elif over is not None:
            if y > crest:
                over, flat = None, 0   # a bump: the face goes on up
            elif r-over >= .1-1e-9:
                topped = beside is not None and beside(crest_at[0], crest_at[1], crest)
                return dict(topped=topped, rise=crest-foot_y, crest=crest_at, crest_y=crest, r=over, flat=flat)
        elif y < crest-DROP_M:
            over = r
        if started and over is None:
            if y > crest:
                crest, crest_at = y, (px, pz)
            if s <= LIP_DEG:
                flat += 1
                if flat*.01 >= LIP_M-1e-9:
                    return dict(topped=True, rise=crest-foot_y, crest=crest_at, crest_y=crest, r=r, flat=flat)
            elif s > STEEP_DEG:
                flat = 0
                up = _uphill(grid, h, px, pz)
                if up is not None and up[0]*dx+up[1]*dz > .3:
                    dx, dz = up
        r += .01
        px, pz = px+dx*.01, pz+dz*.01
    if over is not None:
        topped = beside is not None and beside(crest_at[0], crest_at[1], crest)
        return dict(topped=topped, rise=crest-foot_y, crest=crest_at, crest_y=crest, r=over, flat=flat)
    return None


def knife_edges(grid, h, standable, min_rise=MIN_RISE, near=None):
    """Climbs that top out on a knife edge or a needle. A climber faces the
    face it climbs: from every steep triangle (over 55 degrees) that starts
    within 5 cm of standable ground (`standable(q)`: dry land, not the sea),
    the climb is followed straight up its slope and on over the top. Returns
    the bad climbs, one per crest cell, highest rise first. `near`, a list of
    (x, z, radius), limits the search to faces within those discs."""
    nx, c = grid.nx, grid.cell
    cells = None
    if near is not None:
        cells = {(i, j) for x, z, r in near for j in grid.span(x-r, x+r, z-r, z+r)[1]
                 for i in grid.span(x-r, x+r, z-r, z+r)[0]}
    # Standable patches: a vertex whose four cells are all 35 degrees or
    # gentler (a 6 cm square a body stands on).
    patch = [True]*grid.n
    for j in range(grid.nz-1):
        for i in range(nx-1):
            for fu, fv in ((.33, .67), (.67, .33)):
                if tri_hit(grid, h, grid.xs[i]+c*fu, grid.zs[j]+c*fv)[1] > LIP_DEG:
                    for q in (j*nx+i, j*nx+i+1, (j+1)*nx+i, (j+1)*nx+i+1):
                        patch[q] = False
                    break

    def beside(x, z, y):
        """A climber at a crest pulls over sideways onto a standable patch
        within a body length that is no more than 3 cm under the crest."""
        rx, rz = grid.span(x-BESIDE_M, x+BESIDE_M, z-BESIDE_M, z+BESIDE_M)
        for jj in rz:
            for ii in rx:
                q = jj*nx+ii
                if patch[q] and h[q] >= y-.03 and math.hypot(grid.xs[ii]-x, grid.zs[jj]-z) <= BESIDE_M:
                    return True
        return False
    bad = {}
    for j in range(grid.nz-1):
        for i in range(nx-1):
            if cells is not None and (i, j) not in cells:
                continue
            for fu, fv in ((.33, .67), (.67, .33)):
                x, z = grid.xs[i]+c*fu, grid.zs[j]+c*fv
                y, s = tri_hit(grid, h, x, z)
                if s <= FACE_DEG:
                    continue
                # The way up this face: its steepest ascent.
                e = .004
                gx = tri_hit(grid, h, x+e, z)[0]-tri_hit(grid, h, x-e, z)[0] if 0 < x-e and x+e < grid.xs[-1] else 0.
                gz = tri_hit(grid, h, x, z+e)[0]-tri_hit(grid, h, x, z-e)[0] if 0 < z-e-grid.z0 and z+e < grid.zs[-1] else 0.
                g = math.hypot(gx, gz)
                if g < 1e-9:
                    continue
                dx, dz = gx/g, gz/g
                # A foot: standable ground within 5 cm below, straight back down.
                foot = None
                for back in (.01, .02, .03, .04, .05):
                    fx, fz = x-dx*back, z-dz*back
                    got = tri_hit(grid, h, fx, fz)
                    if got is None:
                        break
                    if got[1] <= FOOT_DEG:
                        k = j*nx+i
                        ii, jj = grid.nearest(fx, fz)
                        if standable(jj*nx+ii):
                            foot = (fx, got[0], fz)
                        break
                if foot is None:
                    continue
                got = climb_from(grid, h, foot[0], foot[2], dx, dz, foot[1], beside)
                if got is None or got['topped'] or got['rise'] < min_rise:
                    continue
                key = grid.nearest(*got['crest'])
                if key not in bad or got['rise'] > bad[key]['rise']:
                    bad[key] = dict(got, foot=foot, heading=(dx, dz))
    return sorted(bad.values(), key=lambda b: (-b['rise'], b['crest']))


def plane_thin_tops(grid, h, can_move, half=1, near=None, max_cut=None):
    """Weather away rock too thin to stand on: a grey-scale opening of the
    land with a flat square of (2*half+1) vertices (9 cm on the 3 cm grid).
    Every needle, knife edge or sliver of a cut corner narrower than that is
    planed down to where it is that wide, so its top is a ledge a body stands
    on; slopes, plateaus, hollows and anything wider stay exactly as they are,
    and nothing is ever raised. `can_move(q)` says which vertices may be
    planed (not water, paths, the hamlet, the jetty or the sea); `near`, a
    list of (x, z, radius), limits it to those discs. Returns the number of
    vertices planed and the deepest cut."""
    nx, nz = grid.nx, grid.nz

    def window_min(f):
        # Separable: rows then columns.
        row = [0.]*grid.n
        for j in range(nz):
            base = j*nx
            for i in range(nx):
                row[base+i] = min(f[base+max(0, i-half):base+min(nx-1, i+half)+1])
        out = [0.]*grid.n
        for j in range(nz):
            for i in range(nx):
                out[j*nx+i] = min(row[jj*nx+i] for jj in range(max(0, j-half), min(nz-1, j+half)+1))
        return out

    def window_max(f):
        row = [0.]*grid.n
        for j in range(nz):
            base = j*nx
            for i in range(nx):
                row[base+i] = max(f[base+max(0, i-half):base+min(nx-1, i+half)+1])
        out = [0.]*grid.n
        for j in range(nz):
            for i in range(nx):
                out[j*nx+i] = max(row[jj*nx+i] for jj in range(max(0, j-half), min(nz-1, j+half)+1))
        return out
    opened = window_max(window_min(h))
    moved, deepest = 0, 0.
    qs = range(grid.n)
    if near is not None:
        qs = sorted({j*nx+i for x, z, r in near for j in grid.span(x-r, x+r, z-r, z+r)[1]
                     for i in grid.span(x-r, x+r, z-r, z+r)[0]
                     if math.hypot(grid.xs[i]-x, grid.zs[j]-z) <= r})
    for q in qs:
        cut = h[q]-opened[q]
        if max_cut is not None:
            cut = min(cut, max_cut(q))
        if cut > 1e-4 and can_move(q):
            h[q] -= cut
            moved += 1
            deepest = max(deepest, cut)
    return moved, deepest


def give_lips(grid, h, standable, can_move, max_cut=None):
    """Plane rock too thin to stand on (a 9 cm opening), at most `max_cut(q)`
    metres deep where that is given; then, round any climb still topping out
    on a knife edge, a wider opening (15 cm, the same depth limit). Returns
    (vertices planed, deepest cut, knife edges left)."""
    moved, deepest = plane_thin_tops(grid, h, can_move, max_cut=max_cut)
    bad = knife_edges(grid, h, standable)
    if bad:
        m, d = plane_thin_tops(grid, h, can_move, 2, [(b['crest'][0], b['crest'][1], .15) for b in bad], max_cut)
        moved, deepest = moved+m, max(deepest, d)
        bad = knife_edges(grid, h, standable)
    return moved, deepest, bad
