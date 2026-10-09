"""Bounded loose-item policy and closed-piece extraction, without generator edits."""
import math

from pipeline.landscape.harness.common import require
from .mesh import cross, transform
from .trees import TerrainHeights, clip

LOOSE_CAP = 64
WOOD_DENSITY_KG_M3 = 600.0  # Authoring assumption, not a measured material.
STONE_DENSITY_KG_M3 = 2600.0
PLAYER_LIMIT_KG = .5


def bounds(parts):
    vertices = [v for p in parts for v in p['positions']]
    return ([min(v[i] for v in vertices) for i in range(3)],
            [max(v[i] for v in vertices) for i in range(3)])


def closed_pieces(parts):
    """Edge-connected closed shells, across role seams and duplicate vertices.

    Exact coordinates join topology only; source indices, colours, blends and
    winding remain intact. Point-touching shells are separate. Open/nonmanifold
    pieces fail rather than quietly dropping triangles. Order is first face.
    """
    faces, edges = [], {}
    for pi, part in enumerate(parts):
        for ti, face in enumerate(part['triangles']):
            index = len(faces)
            faces.append((pi, ti))
            vs = [tuple(part['positions'][i]) for i in face]
            for a, b in zip(vs, vs[1:]+vs[:1]):
                edges.setdefault(tuple(sorted((a, b))), []).append(index)
    require(all(len(indices) == 2 for indices in edges.values()),
            'loose prototype must contain separate closed manifold pieces')
    adjacency = [[] for _ in faces]
    for a, b in edges.values():
        adjacency[a].append(b)
        adjacency[b].append(a)
    visited, result = set(), []
    for first in range(len(faces)):
        if first in visited:
            continue
        stack, group = [first], []
        visited.add(first)
        while stack:
            index = stack.pop()
            group.append(faces[index])
            for other in adjacency[index]:
                if other not in visited:
                    visited.add(other)
                    stack.append(other)
        selected = {}
        for pi, ti in sorted(group):
            selected.setdefault(pi, []).append(ti)
        piece = []
        for pi, tis in sorted(selected.items()):
            source = parts[pi]
            vertices = sorted({i for ti in tis for i in source['triangles'][ti]})
            remap = {old: new for new, old in enumerate(vertices)}
            out = dict(source, positions=[source['positions'][i] for i in vertices],
                       triangles=[[remap[i] for i in source['triangles'][ti]] for ti in tis])
            for key in ('tints', 'blend_weights'):
                if key in source:
                    out[key] = [source[key][i] for i in vertices]
            piece.append(out)
        result.append(piece)
    require(result, 'loose prototype has no pieces')
    return result


def volume(parts):
    """Tetrahedra about an interior centre; convex pieces need no winding repair."""
    lo, hi = bounds(parts)
    centre = [(lo[i]+hi[i])/2 for i in range(3)]
    total = 0.0
    for p in parts:
        for face in p['triangles']:
            a, b, c = [[p['positions'][j][i]-centre[i] for i in range(3)] for j in face]
            total += abs(sum(a[i]*cross(b, c)[i] for i in range(3)))/6
    return total


def estimated_mass(name, parts, size):
    """Clear cases only; do not mistake rooted or built geometry for loose items."""
    if name in {'rock', 'boulder'}:
        mass = volume(parts)*STONE_DENSITY_KG_M3
        # The largest eligible stone is roughly a body length; bigger ones are scenery.
        if max(size) <= .11 and mass <= PLAYER_LIMIT_KG:
            return round(max(.001, mass), 6)
    if name == 'crate' and max(size) <= .10:
        # Hollow small crate: scale the generator's 8 x 7 x 7 cm, 120 g apple crate.
        mass = .12*math.prod(size)/(.08*.07*.07)
        if mass <= PLAYER_LIMIT_KG:
            return round(max(.001, mass), 6)
    return None


def footprint_maximum(terrain, lo, hi, position, yaw):
    """Exact terrain triangle clipping under a yawed box, including interior peaks."""
    angle = math.radians(yaw)
    c, s = math.cos(angle), math.sin(angle)
    corners = [[position[0]+c*x+s*z, 0, position[2]-s*x+c*z]
               for x in (lo[0], hi[0]) for z in (lo[2], hi[2])]
    indices = {i for cell in terrain.cells_in(TerrainHeights.bounds(corners))
               for i in terrain.cells.get(cell, [])}
    best = None
    for index in sorted(indices):
        points = [[c*(v[0]-position[0])-s*(v[2]-position[2]), v[1],
                   s*(v[0]-position[0])+c*(v[2]-position[2])] for v in terrain.triangles[index]]
        for axis, value, above in ((0, lo[0], True), (0, hi[0], False),
                                   (2, lo[2], True), (2, hi[2], False)):
            points = clip(points, axis, value, above)
            if not points:
                break
        if points:
            height = max(v[1] for v in points)
            best = height if best is None else max(best, height)
    return best


def overlaps_xz(a, b):
    return all(min(a[1][i], b[1][i])-max(a[0][i], b[0][i]) > 1e-8 for i in (0, 2))


def log_boxes(parts, scale, position, yaw, terrain):
    """Preserve drawn vertices; inset gameplay boxes to contact without overlap.

    Lower boxes end at the next overlapping piece's drawn bottom, not at the
    enclosing cylinder's top. Ground-contact bottoms use the maximum terrain
    height over the entire footprint. The contract has no separate box size;
    dimensions_m therefore describes this approximation. Visual bounds are
    retained explicitly in the evidence. No mesh is stretched or moved.
    """
    pieces = [transform(piece, scale) for piece in closed_pieces(parts)]
    extents = [bounds(piece) for piece in pieces]
    boxes = [None]*len(pieces)
    for index in sorted(range(len(pieces)), key=lambda i: (extents[i][0][1], i)):
        lo, hi = extents[index]
        lower = [(j, box) for j, box in enumerate(boxes) if box is not None
                 and overlaps_xz(extents[index], extents[j]) and extents[j][0][1] < lo[1]-1e-8]
        ground_y = footprint_maximum(terrain, lo, hi, position, yaw)
        require(ground_y is not None, 'log has no ground beneath its footprint')
        bottom = ground_y
        support = None
        for j, box in lower:
            if box['top_y_m'] > bottom:
                bottom, support = box['top_y_m'], j
        upper_bottoms = [position[1]+other[0][1] for other in extents
                         if other[0][1] > lo[1]+1e-8 and overlaps_xz((lo, hi), other)]
        top = min([position[1]+hi[1], *upper_bottoms])
        require(top-bottom >= .001, 'log cannot fit a resting box between terrain and next piece')
        # Bound the collision approximation to the drawn piece's vertical envelope
        # (allow generator ground sink, but reject floating or buried stacks).
        require(position[1]+lo[1] <= bottom+1e-5 and bottom < position[1]+hi[1],
                'log geometry does not reach its proposed support')
        pivot = [(lo[0]+hi[0])/2, bottom-position[1], (lo[2]+hi[2])/2]
        world_pivot = transform([dict(role='bark', positions=[pivot], triangles=[])],
                                position=position, yaw=yaw)[0]['positions'][0]
        local = transform(pieces[index], position=[-v for v in pivot])
        mass = round(max(.001, volume(pieces[index])*WOOD_DENSITY_KG_M3), 6)
        require(mass <= PLAYER_LIMIT_KG, 'individual log exceeds player carry limit (0.5 kg)')
        boxes[index] = dict(parts=local, position_m=world_pivot,
                            size_m=[hi[0]-lo[0], top-bottom, hi[2]-lo[2]], mass_kg=mass,
                            support_index=support, top_y_m=top,
                            visual_bounds_m={'min_m': [lo[i]-pivot[i] for i in range(3)],
                                             'max_m': [hi[i]-pivot[i] for i in range(3)]})
    return boxes
