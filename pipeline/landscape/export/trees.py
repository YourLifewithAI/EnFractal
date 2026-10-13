"""Hidden tree collision, in world metres; source visual geometry is untouched."""
import math

from pipeline.landscape.harness.common import require

WALK_CLEARANCE_M = .12  # 10 cm body plus 2 cm for margins/uneven ground.
TOP_INSET_M = .01
POLE_OVERLAP_M = .01
POLE_TOP_GAP_M = .005
SEGMENTS = 12


def is_tree(name, parts):
    if name in {'shrub', 'grass_tuft', 'fern', 'flower_clump'}:
        return False
    return {'bark', 'foliage'} <= {p['role'] for p in parts}


def clip(points, axis, value, above):
    """Clip a polygon to an axis plane, preserving order and interpolated Y."""
    result = []
    for a, b in zip(points, points[1:]+points[:1]):
        da, db = a[axis]-value, b[axis]-value
        inside_a, inside_b = (da >= 0, db >= 0) if above else (da <= 0, db <= 0)
        if inside_a:
            result.append(a)
        if inside_a != inside_b:
            t = da/(da-db)
            result.append([a[i]+t*(b[i]-a[i]) for i in range(3)])
    return result


class _Cells:
    """The index's cells, built a block at a time on first use (a dict's get, for loose.py and the queries)."""

    def __init__(self, owner):
        self.owner, self.cells, self.built = owner, {}, set()

    def get(self, cell, default=None):
        block = (cell[0]//self.owner.BLOCK, cell[1]//self.owner.BLOCK)
        if block not in self.built:
            self.owner.build(block)
        return self.cells.get(cell, default)


class TerrainHeights:
    """Index triangles, then bound terrain Y over an entire cap rectangle.

    Clipping includes triangle interiors and edges, not just point samples. A
    constant cap floor above this maximum clears every projected cap triangle.
    Each triangle is listed in every cell its bounds (grown by EPS_M) touch, so a
    point query sees every triangle mesh.ground would hit there. Triangles are
    sorted into blocks of BLOCK x BLOCK cells first; a block's cells are built
    when a query first reaches it (a 10x island holds a million triangles and an
    export asks about a few hundred places), so a triangle in two blocks is
    listed once in each.
    """
    CELL_M = .25
    EPS_M = 1e-5
    BLOCK = 16

    def __init__(self, terrain, region=None):
        """region (x0, x1, z0, z1): only cells there are indexed (water under a room's bounds)."""
        self.terrain = terrain
        self.cells = _Cells(self)
        self.triangles = []
        self.parts = []  # the terrain part of each triangle
        self.blocks = {}
        size, eps, floor, block = self.CELL_M, self.EPS_M, math.floor, self.CELL_M*self.BLOCK
        self.limit = None if region is None else [floor(region[0]/size), floor(region[1]/size),
                                                  floor(region[2]/size), floor(region[3]/size)]
        blocks, n = self.blocks, self.BLOCK
        outer = None if region is None else [v//n for v in self.limit]  # the region's blocks
        for k, part in enumerate(terrain):
            positions = part['positions']
            lx = [floor((v[0]-eps)/block) for v in positions]
            hx = [floor((v[0]+eps)/block) for v in positions]
            lz = [floor((v[2]-eps)/block) for v in positions]
            hz = [floor((v[2]+eps)/block) for v in positions]
            for t, (a, b, c) in enumerate(part['triangles']):
                x0, z0 = lx[a], lz[a]
                if (x0 == lx[b] == lx[c] == hx[a] == hx[b] == hx[c] and z0 == lz[b] == lz[c] == hz[a] == hz[b] == hz[c]
                        and (outer is None or outer[0] <= x0 <= outer[1] and outer[2] <= z0 <= outer[3])):
                    entry = blocks.get((x0, z0))
                    if entry is None:
                        blocks[(x0, z0)] = [(k, t)]
                    else:
                        entry.append((k, t))
                    continue
                x0, x1 = min(x0, lx[b], lx[c]), max(hx[a], hx[b], hx[c])
                z0, z1 = min(z0, lz[b], lz[c]), max(hz[a], hz[b], hz[c])
                if outer is not None:
                    x0, x1, z0, z1 = max(x0, outer[0]), min(x1, outer[1]), max(z0, outer[2]), min(z1, outer[3])
                for x in range(x0, x1+1):
                    for z in range(z0, z1+1):
                        blocks.setdefault((x, z), []).append((k, t))

    def build(self, block):
        """List the block's triangles in its cells (within the region, if any)."""
        self.cells.built.add(block)
        size, eps, floor, n = self.CELL_M, self.EPS_M, math.floor, self.BLOCK
        bx0, bz0 = block[0]*n, block[1]*n
        bx1, bz1 = bx0+n-1, bz0+n-1
        if self.limit is not None:
            bx0, bx1 = max(bx0, self.limit[0]), min(bx1, self.limit[1])
            bz0, bz1 = max(bz0, self.limit[2]), min(bz1, self.limit[3])
        cells = self.cells.cells
        for k, t in self.blocks.get(block, ()):
            positions = self.terrain[k]['positions']
            a, b, c = [positions[i] for i in self.terrain[k]['triangles'][t]]
            x0, x1 = max(bx0, floor((min(a[0], b[0], c[0])-eps)/size)), min(bx1, floor((max(a[0], b[0], c[0])+eps)/size))
            z0, z1 = max(bz0, floor((min(a[2], b[2], c[2])-eps)/size)), min(bz1, floor((max(a[2], b[2], c[2])+eps)/size))
            if x0 > x1 or z0 > z1:
                continue
            index = len(self.triangles)
            self.triangles.append([a, b, c])
            self.parts.append(k)
            for x in range(x0, x1+1):
                for z in range(z0, z1+1):
                    cells.setdefault((x, z), []).append(index)

    @staticmethod
    def bounds(points):
        return (min(v[0] for v in points), max(v[0] for v in points),
                min(v[2] for v in points), max(v[2] for v in points))

    def cells_in(self, bounds):
        x0, x1, z0, z1 = [math.floor(v/self.CELL_M) for v in bounds]
        for x in range(x0, x1+1):
            for z in range(z0, z1+1):
                yield x, z

    def maximum(self, bounds):
        indices = {i for cell in self.cells_in(bounds) for i in self.cells.get(cell, [])}
        best = None
        bx0, bx1, bz0, bz1 = bounds
        for index in sorted(indices):
            points = self.triangles[index]
            xs, zs = [v[0] for v in points], [v[2] for v in points]
            if bx0 <= min(xs) and max(xs) <= bx1 and bz0 <= min(zs) and max(zs) <= bz1:
                y = max(v[1] for v in points)  # wholly inside: clipping keeps every point
                best = y if best is None else max(best, y)
                continue
            for axis, value, above in ((0, bx0, True), (0, bx1, False),
                                       (2, bz0, True), (2, bz1, False)):
                points = clip(points, axis, value, above)
                if not points:
                    break
            if points:
                y = max(v[1] for v in points)
                best = y if best is None else max(best, y)
        return best

    def hit(self, x, z):
        """(y, part) of the topmost triangle over (x, z), the first part on ties (mesh.ground's answer, and the
        part support_at names), or (None, None)."""
        best, part = None, None
        for index in self.cells.get((math.floor(x/self.CELL_M), math.floor(z/self.CELL_M)), ()):
            a, b, c = self.triangles[index]
            det = (b[2]-c[2])*(a[0]-c[0])+(c[0]-b[0])*(a[2]-c[2])
            if abs(det) < 1e-14:
                continue
            u = ((b[2]-c[2])*(x-c[0])+(c[0]-b[0])*(z-c[2]))/det
            v = ((c[2]-a[2])*(x-c[0])+(a[0]-c[0])*(z-c[2]))/det
            if min(u, v, 1-u-v) >= -1e-7:
                y = u*a[1]+v*b[1]+(1-u-v)*c[1]
                k = self.parts[index]
                if best is None or y > best or (y == best and k < part):
                    best, part = y, k
        return best, part


def ring(cx, cz, rx, rz, y):
    return [[cx+rx*math.cos(i*math.tau/SEGMENTS), y,
             cz+rz*math.sin(i*math.tau/SEGMENTS)] for i in range(SEGMENTS)]


def cap_shape(crown, pole_radius, terrain, conifer):
    """A crown's perch: (x, z, rim_half_x, rim_half_z, rim_y, top_y, top_half_x, top_half_z), or None for a low
    lobe buried in terrain. The collar runs from the rim ellipse up to the flat top ellipse."""
    vs = crown['positions']
    x0, x1, z0, z1 = TerrainHeights.bounds(vs)
    low, high = min(v[1] for v in vs), max(v[1] for v in vs)
    cx, cz = (x0+x1)/2, (z0+z1)/2
    # Broadleaf upper quarter; conifer upper half of its highest tier only.
    # Round needle tips into a small flat perch large enough for a 2 cm capsule.
    fraction = .5 if conifer else .75
    rx, rz = (x1-x0)/2*(.5 if conifer else .7), (z1-z0)/2*(.5 if conifer else .7)
    top_radius = pole_radius+.025
    tx, tz = max(top_radius, rx*.3), max(top_radius, rz*.3)
    # Keep the collar outward-facing even for tiny/nonuniformly scaled crowns.
    rx, rz = max(rx, tx*1.25), max(rz, tz*1.25)
    ground_y = terrain.maximum((cx-rx, cx+rx, cz-rz, cz+rz))
    rim_y = low+(high-low)*fraction
    if ground_y is not None:
        rim_y = max(rim_y, ground_y+WALK_CLEARANCE_M)
    top_y = high-TOP_INSET_M
    if rim_y >= top_y:
        return None  # A low lobe buried in terrain need not become a platform.
    return cx, cz, rx, rz, rim_y, top_y, tx, tz


def cap(crown, pole_radius, terrain, conifer):
    shape = cap_shape(crown, pole_radius, terrain, conifer)
    return None if shape is None else cap_mesh(shape)


def cap_mesh(shape):
    cx, cz, rx, rz, rim_y, top_y, tx, tz = shape
    positions = ring(cx, cz, rx, rz, rim_y)+ring(cx, cz, tx, tz, top_y)
    positions.append([cx, top_y, cz])
    triangles = []
    for i in range(SEGMENTS):
        k = (i+1)%SEGMENTS
        triangles += [[i, SEGMENTS+i, SEGMENTS+k], [i, SEGMENTS+k, k],
                      [2*SEGMENTS, SEGMENTS+k, SEGMENTS+i]]
    return dict(role='foliage', positions=positions, triangles=triangles)


def fit(name, parts, terrain):
    """A tree's climbing collision, fitted to the ground after scale/yaw/translation, so clearances and offsets
    stay metric: the trunk's axis (the centre of its top ring), its radius at the base and at the top, the trunk's
    top, the hidden pole's top and the crowns' perches (cap_shape). None for anything that is not a tree.
    These are the numbers a plant copy's tree row carries (contracts/README.md, "Plant copies")."""
    if not is_tree(name, parts):
        return None
    bark = [p for p in parts if p['role'] == 'bark']
    trunk_top = max(v[1] for p in bark for v in p['positions'])
    trunk_base = min(v[1] for p in bark for v in p['positions'])
    top_ring = [v for p in bark for v in p['positions'] if abs(v[1]-trunk_top) < 1e-8]
    cx = (min(v[0] for v in top_ring)+max(v[0] for v in top_ring))/2
    cz = (min(v[2] for v in top_ring)+max(v[2] for v in top_ring))/2
    pole_radius = max(math.hypot(v[0]-cx, v[2]-cz) for v in top_ring)
    require(pole_radius > 0, 'tree needs a nonzero trunk top radius')
    base_radius = max(math.hypot(v[0]-cx, v[2]-cz) for p in bark for v in p['positions']
                      if abs(v[1]-trunk_base) < 1e-8)
    crowns = [p for p in parts if p['role'] == 'foliage']
    highest = max(crowns, key=lambda p: max(v[1] for v in p['positions']))
    if name == 'conifer':
        crowns = [highest]  # Lower overlapping skirts remain decoration.
    caps = [c for p in crowns if (c := cap_shape(p, pole_radius, terrain, name == 'conifer')) is not None]
    require(caps, 'tree crown cannot clear terrain by 12 cm')
    top_y = max(c[5] for c in caps)-POLE_TOP_GAP_M
    require(abs(top_y-(max(v[1] for v in highest['positions'])-TOP_INSET_M-POLE_TOP_GAP_M)) < 1e-8,
            'highest tree crown cannot clear terrain by 12 cm')
    require(top_y > trunk_top, 'tree crown top must be above trunk top')
    return dict(axis=(cx, cz), trunk_radius=max(base_radius, pole_radius), pole_radius=pole_radius,
                trunk_top=trunk_top, pole_top=top_y, caps=caps, top_ring=top_ring)


def climbing_parts(name, parts, terrain):
    """One open pole and upper caps; glTF front faces are CCW, outward/up.

    Called after scale/yaw/translation, so clearances and offsets stay metric.
    No bottom faces: inside-upwards traversal relies on one-sided trimeshes.
    """
    tree = fit(name, parts, terrain)
    if tree is None:
        return []
    cx, cz = tree['axis']
    # Sort the perimeter independently of its source indices; exclude cap centre.
    perimeter = sorted({tuple(v) for v in tree['top_ring'] if math.hypot(v[0]-cx, v[2]-cz) > 1e-8},
                       key=lambda v: (math.atan2(v[2]-cz, v[0]-cx), v))
    trunk_top, top_y = tree['trunk_top'], tree['pole_top']
    positions = [[v[0], trunk_top-POLE_OVERLAP_M, v[2]] for v in perimeter]
    positions += [[v[0], top_y, v[2]] for v in perimeter]
    n = len(perimeter)
    triangles = []
    for i in range(n):
        k = (i+1)%n
        triangles += [[i, n+i, n+k], [i, n+k, k]]
    return [dict(role='bark', positions=positions, triangles=triangles), *[cap_mesh(c) for c in tree['caps']]]
