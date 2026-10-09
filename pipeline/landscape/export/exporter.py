"""Mapping policy and deterministic room writer; no third-party dependencies."""
import math
import re
from pathlib import Path
from pipeline.landscape.harness.common import canonical, sha, require
from pipeline.landscape.harness.kit import SIZES, prototype
from pipeline.landscape.harness.package import read_package
from .materials import CONTRACT_ROLES, PALETTE, hex_color, material
from .mesh import encode, ground, open_edges, transform
from .sea import sea_data
from .source import source_data
from .trees import TerrainHeights, climbing_parts, is_tree

CREATED = '2026-10-08T00:00:00Z'
LANDMARKS = {'cottage', 'tower', 'boulder', 'fence', 'crate', 'lantern'}


def triangle_count(parts):
    return sum(len(p['triangles']) for p in parts)


def scatter_policy(name, parts, size):
    """Geometry/size fallback for custom prototypes; never inspect generator keys."""
    roles = {p['role'] for p in parts}
    if name in LANDMARKS or roles & {'roof', 'stone_masonry'}:
        return 'landmark'
    if not roles & {'foliage', 'bark'} and max(size) >= .3:
        return 'landmark'
    return 'merged'


def colliding_parts(name, parts):
    # Small plants stay decorative. Original foliage is still visual only;
    # tree climbing geometry is added separately. Loose kit rocks are low-poly
    # ellipsoids, merged into one static triangle surface rather than 20k nodes.
    roles = {p['role'] for p in parts}
    if roles & {'foliage', 'bark'}:
        return [p for p in parts if p['role'] == 'bark']
    if name in {'grass_tuft', 'flower_clump', 'fern', 'shrub'}:
        return []
    return [p for p in parts if p['role'] not in {'cloth', 'still_water', 'flowing_water'}]


def export_room(package_folder, source_room_folder, room_id, out):
    """Return counts; validate inputs and construct bytes before creating output.

    The output folder's basename must equal room_id, as RoomData.Load requires.
    It must be empty; no replacement of a previously pinned room is permitted.
    """
    require(re.fullmatch(r'[a-z][a-z0-9_-]{0,63}', room_id) is not None, 'invalid room id')
    out = Path(out).absolute()
    require(out.name == room_id, 'output directory name must equal room-id (game loader rule)')
    require(not out.is_symlink(), 'output symlinks forbidden')
    require(not out.exists() or (out.is_dir() and not any(out.iterdir())), 'output folder must be empty')
    doc, meshes, source, _ = read_package(package_folder, source_room_folder)
    intro_source = source_data(source, doc['source'], source_room_folder)
    # An island's sea (generator v4 on): the bounds grow out past the reef, and the game gets the reef,
    # the beaches and the jetty. A package without one keeps the room's own bounds.
    sea = doc.get('x_generator', {}).get('sea') if isinstance(doc.get('x_generator'), dict) else None
    sea, bounds = sea_data(sea, intro_source['bounds']) if sea is not None else (None, intro_source['bounds'])
    require(doc['terrain'], 'landscape has no terrain')
    require(len(doc['objects']) <= 512, 'room contract allows at most 512 populated objects')
    blobs, shell, objects = {}, [], []
    counts = {}

    def put(path, data):
        blobs[path] = data
        return {'path': path, 'sha256': sha(data), 'bytes': len(data)}

    def add_shell(label, parts, role, collides, drawn=True):
        # One part per role gives the existing loader the closest contract role
        # instead of treating every surface of the landscape as one material.
        groups = {}
        for p in parts:
            groups.setdefault(p['role'], []).append(p)
        for harness_role, group in sorted(groups.items()):
            path = f'shell/{label}_{harness_role}.glb'
            put(path, encode(group))
            shell.append({'id': f'shell:{label}_{harness_role}', 'role': role,
                          'geometry': {'kind': 'mesh', 'mesh': path}, 'collides': collides,
                          'material_role': CONTRACT_ROLES[harness_role],
                          'base_color': hex_color(PALETTE[harness_role])})
            if not drawn:
                shell[-1]['drawn'] = False
        counts[label+'_triangles'] = triangle_count(parts)

    terrain = [p for r in doc['terrain'] for p in meshes[r['mesh']]]
    add_shell('terrain', terrain, 'ground', True)
    tree_terrain = None
    tree_parts = []
    counts.update(tree_count=0, tree_pole_triangles=0, tree_cap_triangles=0)

    def add_tree(name, transformed, include_trunk=False):
        nonlocal tree_terrain
        if not is_tree(name, transformed):
            return
        if tree_terrain is None:
            tree_terrain = TerrainHeights(terrain)
        climb = climbing_parts(name, transformed, tree_terrain)
        counts['tree_count'] += 1
        counts['tree_pole_triangles'] += triangle_count(climb[:1])
        counts['tree_cap_triangles'] += triangle_count(climb[1:])
        tree_parts.extend(climb)
        if include_trunk:
            tree_parts.extend(p for p in transformed if p['role'] == 'bark')
    # The harness overrides water primitive roles using each water record.
    water = [dict(p, role='still_water' if r['kind'] == 'still' else 'flowing_water')
             for r in doc['water'] for p in meshes[r['mesh']]]
    add_shell('water', water, 'backdrop', False)
    add_shell('scenery', [p for r in doc['scenery'] for p in meshes[r['mesh']]], 'backdrop', False)

    prototypes = {}

    def get_prototype(name):
        if name not in prototypes:
            prototypes[name] = ((prototype(name), SIZES[name]) if name in SIZES else
                (meshes[doc['prototypes'][name]['mesh']], doc['prototypes'][name]['size_m']))
        return prototypes[name]

    def support_at(position):
        best, support = None, None
        for p in terrain:
            y = ground([p], position[0], position[2])
            if y is not None and (best is None or y > best):
                best, support = y, 'shell:terrain_'+p['role']
        return best, support

    def add_object(record, index, scatter=False):
        name = record['prototype']
        parts, original_size = get_prototype(name)
        size = ([original_size[i]*record['scale'][i] for i in range(3)] if scatter else record['size_m'])
        movable = False if scatter else record['carriable']
        tree = is_tree(name, parts)
        require(not tree or not movable, 'climbable trees must be fixed (static shell collision)')
        mass = 100.0 if scatter else record['mass_kg']
        require(0 < mass <= 5000, 'object mass must fit asset contract (0,5000] kg')
        require(not movable or mass <= 2.0, 'carriable object exceeds companion carry limit (2 kg)')
        require(all(0 < v <= 100 for v in size), 'object size exceeds asset contract')
        roles = sorted({p['role'] for p in parts})
        require(len(roles) <= 16, 'asset allows at most 16 material slots')
        asset_id = f'landscape_{index:04d}'
        instance_id = f'obj:scatter_{index:04d}' if scatter else 'obj:'+record['id']
        require(re.fullmatch(r'obj:[A-Za-z0-9_-]{1,64}', instance_id) is not None,
                'package object id does not fit room entity id')
        kind = name if scatter else record['kind']
        display = (record['id'] if not scatter else name).replace('_', ' ').replace('-', ' ').capitalize()
        if scatter:
            display += f' {index+1}'
        display = display[:80]
        scaled = transform(parts, scale=[size[i]/original_size[i] for i in range(3)],
                           tint=record.get('tint', (1, 1, 1)))
        asset_dir = f'objects/{asset_id}'
        geometry = put(asset_dir+'/mesh.glb', encode(scaled))
        geometry['path'] = 'mesh.glb'
        collision = 'none' if tree or (not colliding_parts(name, parts) and not movable) else 'box'
        # A canopy box would block paths and collide with every leaf. Vegetation
        # never becomes a landmark entity; populated vegetation uses decoration
        # collision unless explicitly carriable.
        asset = {
            'schema': 'enfractal.asset', 'version': 1, 'asset_id': asset_id,
            'display_name': display, 'category': kind[:60],
            'category_group': ('structure' if name in {'cottage', 'tower', 'fence'} or 'roof' in roles
                               else 'lighting' if name == 'lantern' else 'container' if name == 'crate' else 'decor'),
            'tier': 'hero' if movable else 'standard',
            'provenance': {'kind': 'procedural', 'license': 'Inherited from source package; licensing unverified',
                           'created_utc': CREATED, 'notes': 'Landscape package v1 geometry, transformed without texture or shader dependencies.'},
            'pivot': 'bottom_center', 'dimensions_m': list(size),
            'geometry': {'kind': 'mesh', 'mesh': 'mesh.glb', 'triangle_count': triangle_count(parts)},
            'collision': {'kind': collision}, 'physics': {'movable': movable, 'mass_kg': mass},
            'materials': [{'slot': r, 'role': CONTRACT_ROLES[r],
                           'base_color': hex_color(material(r)['pbrMetallicRoughness']['baseColorFactor'][:3])}
                          for r in roles],
            'affordances': ['walkable_top'] if collision != 'none' else [],
            'review': {'status': 'draft', 'notes': 'Exported for founder review; no gameplay or look gate certified.'},
            'files': [geometry],
        }
        if name == 'crate':
            asset['affordances'].append('container')
        if name == 'lantern':
            asset['affordances'].append('light_source')
        put(asset_dir+'/asset.json', canonical(asset))
        if tree:
            add_tree(name, transform(parts, [size[i]/original_size[i] for i in range(3)],
                                     record['position_m'], record['yaw_deg']), include_trunk=True)
        _, support = support_at(record['position_m'])
        angle = math.radians(record['yaw_deg'])/2
        objects.append({'id': instance_id, 'asset': asset_dir+'/asset.json', 'display_name': display,
                        'transform': {'position_m': record['position_m'],
                                      'rotation': [0, math.sin(angle), 0, math.cos(angle)]},
                        'support': {'kind': 'shell', 'target_id': support} if support else {'kind': 'none'}})

    for index, record in enumerate(doc['objects']):
        add_object(record, index)
    entity_scatter = 0
    merged_visual, merged_solid = [], []
    for record in doc['scatter']:
        name = record['prototype']
        parts, size = get_prototype(name)
        final_size = [size[i]*record['scale'][i] for i in range(3)]
        if scatter_policy(name, parts, final_size) == 'landmark' and len(objects) < 512:
            add_object(record, len(objects), scatter=True)
            entity_scatter += 1
            continue
        solid = colliding_parts(name, parts)
        # Transform before normals: non-uniform scale needs inverse-transpose
        # normals, obtained equivalently from transformed face cross products.
        transformed = transform(parts, record['scale'], record['position_m'], record['yaw_deg'],
                                record.get('tint', (1, 1, 1)))
        add_tree(name, transformed)
        for source_part, p in zip(parts, transformed):
            (merged_solid if any(source_part is q for q in solid) else merged_visual).append(p)
    add_shell('scatter_visual', merged_visual, 'backdrop', False)
    add_shell('scatter_solid', merged_solid, 'ground', True)
    # Two merged shell parts regardless of tree count. Lane P hides their GLB
    # scenes after extracting one-sided trimesh collision (see README/report).
    # Collision only: the trees' own meshes stay the drawn ones.
    add_shell('tree_climb', tree_parts, 'ground', True, drawn=False)
    require(len(shell) <= 128, 'output exceeds room shell part limit (128)')
    require(len(blobs) <= 2048, 'output exceeds room file limit (2048)')
    require(len({o['id'] for o in objects}) == len(objects), 'populated id collides with generated scatter entity id')

    # Package v1 carriable objects become movable assets. Only cottage/tower
    # prototypes promise a door; other fixed landmarks are not destinations.
    # Include buildings merged after the entity budget, since they remain drawn.
    destinations = [r['position_m'] for r in doc['objects']
                    if r['carriable'] or r['prototype'] in {'cottage', 'tower'}]
    destinations.extend(r['position_m'] for r in doc['scatter']
                        if r['prototype'] in {'cottage', 'tower'})
    spawns = []
    for spawn in source['spawns']:
        spawn = {'id': spawn['id'], 'role': spawn['role'],
                 'position_m': list(spawn['position_m']), 'yaw_deg': spawn['yaw_deg']}
        y = ground(terrain, spawn['position_m'][0], spawn['position_m'][2])
        require(y is not None, 'spawn '+spawn['id']+' has no terrain underneath')
        spawn['position_m'][1] = y
        require(all(source['bounds']['min_m'][i]-1e-6 <= spawn['position_m'][i] <= source['bounds']['max_m'][i]+1e-6
                    for i in range(3)), 'lifted spawn outside source room bounds')
        if destinations:
            x, _, z = spawn['position_m']
            # min keeps package order on ties. Height never affects selection.
            target = min(destinations, key=lambda p: (p[0]-x)**2 + (p[2]-z)**2)
            dx, dz = target[0]-x, target[2]-z
            if dx != 0 or dz != 0:
                # Godot +Y yaw turns -Z towards -X; a coincident target has no heading.
                spawn['yaw_deg'] = math.degrees(math.atan2(-dx, -dz))
        spawns.append(spawn)
    setup = doc['setup']
    edges = open_edges(terrain)
    lights = [{'id': 'sun', 'kind': 'sun', 'color': '#fff1d2', 'relative_intensity': 1.0, 'estimated': True}]
    lanterns = [r for r in [*doc['objects'], *doc['scatter']] if r['prototype'] == 'lantern']
    for i, r in enumerate(lanterns[:31]):
        lights.append({'id': f'lantern_{i:04d}', 'kind': 'lamp',
                       'position_m': r['position_m'], 'color': '#ffe7bf',
                       'relative_intensity': .25, 'estimated': True})
    room = {
        'schema': 'enfractal.room', 'version': 1, 'room_id': room_id,
        'display_name': room_id.replace('_', ' ').replace('-', ' ').capitalize()[:80],
        'created_utc': CREATED, 'units': 'm', 'axes': 'y_up_neg_z_forward',
        'source': {'kind': 'procedural', 'pipeline': {'name': 'landscape-room-export', 'version': '1'}},
        'bounds': bounds, 'shell': {'parts': shell, 'openings': []},
        'objects': objects, 'spawns': spawns, 'light_hints': lights,
        'site': {'latitude_deg': setup['latitude_deg'], 'neg_z_bearing_deg': setup['neg_z_bearing_deg'],
                 'solar_noon_h': 12},
        'files': [{'path': path, 'sha256': sha(data), 'bytes': len(data)} for path, data in sorted(blobs.items())],
        'extensions': {'x_landscape_setup': setup,
                       'x_landscape_source': intro_source,
                       'x_landscape_package_sha256': sha((Path(package_folder)/'package.json').read_bytes()),
                       'x_landscape_terrain_open_edges': edges,
                       **({'x_landscape_sea': sea} if sea is not None else {})},
    }
    blobs['room.json'] = canonical(room)
    # Output creation begins only after package, ids, budgets, and spawns pass.
    out.mkdir(parents=True, exist_ok=True)
    for path, data in sorted(blobs.items()):
        target = out/path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    return dict(counts, room_id=room_id, files=len(blobs), bytes=sum(map(len, blobs.values())),
                shell_parts=len(shell), objects=len(objects), populated_objects=len(doc['objects']),
                scatter_entities=entity_scatter, merged_scatter=len(doc['scatter'])-entity_scatter,
                terrain_open_edges=len(edges), lantern_hints=len(lights)-1)
