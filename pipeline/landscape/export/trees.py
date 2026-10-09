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


class TerrainHeights:
    """Index triangles, then bound terrain Y over an entire cap rectangle.

    Clipping includes triangle interiors and edges, not just point samples. A
    constant cap floor above this maximum clears every projected cap triangle.
    """
    CELL_M = .25

    def __init__(self, terrain):
        self.cells = {}
        self.triangles = []
        for part in terrain:
            for face in part['triangles']:
                points = [part['positions'][i] for i in face]
                index = len(self.triangles)
                self.triangles.append(points)
                for cell in self.cells_in(self.bounds(points)):
                    self.cells.setdefault(cell, []).append(index)

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
        for index in sorted(indices):
            points = self.triangles[index]
            for axis, value, above in ((0, bounds[0], True), (0, bounds[1], False),
                                       (2, bounds[2], True), (2, bounds[3], False)):
                points = clip(points, axis, value, above)
                if not points:
                    break
            if points:
                y = max(v[1] for v in points)
                best = y if best is None else max(best, y)
        return best


def ring(cx, cz, rx, rz, y):
    return [[cx+rx*math.cos(i*math.tau/SEGMENTS), y,
             cz+rz*math.sin(i*math.tau/SEGMENTS)] for i in range(SEGMENTS)]


def cap(crown, pole_radius, terrain, conifer):
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
    positions = ring(cx, cz, rx, rz, rim_y)+ring(cx, cz, tx, tz, top_y)
    positions.append([cx, top_y, cz])
    triangles = []
    for i in range(SEGMENTS):
        k = (i+1)%SEGMENTS
        triangles += [[i, SEGMENTS+i, SEGMENTS+k], [i, SEGMENTS+k, k],
                      [2*SEGMENTS, SEGMENTS+k, SEGMENTS+i]]
    return dict(role='foliage', positions=positions, triangles=triangles)


def climbing_parts(name, parts, terrain):
    """One open pole and upper caps; glTF front faces are CCW, outward/up.

    Called after scale/yaw/translation, so clearances and offsets stay metric.
    No bottom faces: inside-upwards traversal relies on one-sided trimeshes.
    """
    if not is_tree(name, parts):
        return []
    bark = [p for p in parts if p['role'] == 'bark']
    trunk_top = max(v[1] for p in bark for v in p['positions'])
    top_ring = [v for p in bark for v in p['positions'] if abs(v[1]-trunk_top) < 1e-8]
    cx = (min(v[0] for v in top_ring)+max(v[0] for v in top_ring))/2
    cz = (min(v[2] for v in top_ring)+max(v[2] for v in top_ring))/2
    pole_radius = max(math.hypot(v[0]-cx, v[2]-cz) for v in top_ring)
    require(pole_radius > 0, 'tree needs a nonzero trunk top radius')
    # Sort the perimeter independently of its source indices; exclude cap centre.
    perimeter = sorted({tuple(v) for v in top_ring if math.hypot(v[0]-cx, v[2]-cz) > 1e-8},
                       key=lambda v: (math.atan2(v[2]-cz, v[0]-cx), v))
    crowns = [p for p in parts if p['role'] == 'foliage']
    highest = max(crowns, key=lambda p: max(v[1] for v in p['positions']))
    if name == 'conifer':
        crowns = [highest]  # Lower overlapping skirts remain decoration.
    caps = [c for p in crowns if (c := cap(p, pole_radius, terrain, name == 'conifer')) is not None]
    require(caps, 'tree crown cannot clear terrain by 12 cm')
    top_y = max(v[1] for c in caps for v in c['positions'])-POLE_TOP_GAP_M
    require(abs(top_y-(max(v[1] for v in highest['positions'])-TOP_INSET_M-POLE_TOP_GAP_M)) < 1e-8,
            'highest tree crown cannot clear terrain by 12 cm')
    require(top_y > trunk_top, 'tree crown top must be above trunk top')
    positions = [[v[0], trunk_top-POLE_OVERLAP_M, v[2]] for v in perimeter]
    positions += [[v[0], top_y, v[2]] for v in perimeter]
    n = len(perimeter)
    triangles = []
    for i in range(n):
        k = (i+1)%n
        triangles += [[i, n+i, n+k], [i, n+k, k]]
    return [dict(role='bark', positions=positions, triangles=triangles), *caps]
