"""Shared Blender helpers, derived from brief 09; CPU rendering only."""
import math
import struct
import json
from pathlib import Path
import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

def reset():
    if not bpy.app.background:
        raise RuntimeError('Recipes require --background')
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0


def material(name, color, roughness=0.5, metallic=0.0, transmission=0.0):
    mat = bpy.data.materials.new(name)
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    if bsdf is None:  # Compatibility with Blender before nodes became the default.
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Roughness'].default_value = roughness
    bsdf.inputs['Metallic'].default_value = metallic
    bsdf.inputs['Transmission Weight'].default_value = transmission
    bsdf.inputs['IOR'].default_value = 1.5
    return mat


def mesh_object(name, vertices, faces, mat, smooth=False):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    for polygon in mesh.polygons:
        polygon.use_smooth = smooth
    return obj


def panel(name, size, location, mat, bevel):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    # These panels have flat-colour materials. Cube UV interpolation during bevel
    # can jitter across processes; unused layers need not reach the exporter.
    for layer in list(obj.data.uv_layers):
        obj.data.uv_layers.remove(layer)
    mod = obj.modifiers.new('small_real_edge_bevel', 'BEVEL')
    # Leave a flat strip even on thin frames/screens. Blender's overlap clamp at
    # half the shortest side can collapse opposing bevels into zero-area faces.
    mod.width = min(bevel, min(size) * 0.25)
    mod.segments = 2
    bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.data.materials.append(mat)
    return obj


def bounds(objects):
    points = [obj.matrix_world @ v.co for obj in objects for v in obj.data.vertices]
    return ([min(p[i] for p in points) for i in range(3)],
            [max(p[i] for p in points) for i in range(3)])


def inspect_glb(path):
    """Check actual GLB position/index bytes, hierarchy, materials and containment."""
    raw = Path(path).read_bytes()
    magic, version, size = struct.unpack_from('<III', raw)
    assert magic == 0x46546C67 and version == 2 and size == len(raw)
    json_size, json_type = struct.unpack_from('<II', raw, 12)
    assert json_type == 0x4E4F534A
    doc = json.loads(raw[20:20 + json_size])
    offset = 20 + json_size
    bin_size, bin_type = struct.unpack_from('<II', raw, offset)
    assert bin_type == 0x004E4942
    binary = raw[offset + 8:offset + 8 + bin_size]
    assert not any('uri' in b for b in doc.get('buffers', []))
    assert not any('uri' in i for i in doc.get('images', []))
    assert not doc.get('cameras') and not doc.get('animations')

    def accessor(index):
        acc = doc['accessors'][index]
        assert not acc.get('sparse')
        view = doc['bufferViews'][acc['bufferView']]
        formats = {5121: 'B', 5123: 'H', 5125: 'I', 5126: 'f'}
        components = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}
        fmt = '<' + formats[acc['componentType']] * components[acc['type']]
        start = view.get('byteOffset', 0) + acc.get('byteOffset', 0)
        stride = view.get('byteStride', struct.calcsize(fmt))
        return [struct.unpack_from(fmt, binary, start + stride * i) for i in range(acc['count'])]

    points, triangles, parts = [], 0, {}
    scene_nodes = doc['scenes'][doc.get('scene', 0)]['nodes']
    assert len(scene_nodes) == 1, 'One asset root required'
    root_node = doc['nodes'][scene_nodes[0]]
    assert 'mesh' not in root_node

    def transform(node):
        if 'matrix' in node:
            return Matrix([node['matrix'][i:i + 4] for i in range(0, 16, 4)]).transposed()
        translation = Matrix.Translation(Vector(node.get('translation', [0, 0, 0])))
        x, y, z, w = node.get('rotation', [0, 0, 0, 1])
        rotation = Quaternion((w, x, y, z)).to_matrix().to_4x4()
        scale = Matrix.Diagonal(Vector((*node.get('scale', [1, 1, 1]), 1)))
        return translation @ rotation @ scale

    assert all(abs(transform(root_node)[i][j] - (1 if i == j else 0)) < 1e-7
               for i in range(4) for j in range(4)), 'Root must be identity at bottom centre'

    def walk(index, parent):
        nonlocal triangles
        node = doc['nodes'][index]
        world = parent @ transform(node)
        if 'mesh' in node:
            assert node.get('name') and node['name'] not in parts, 'Unique named parts required'
            part_points, part_triangles = [], 0
            for primitive in doc['meshes'][node['mesh']]['primitives']:
                assert primitive.get('mode', 4) == 4
                positions = accessor(primitive['attributes']['POSITION'])
                indices = accessor(primitive['indices'])
                assert len(indices) % 3 == 0
                assert all(0 <= value[0] < len(positions) for value in indices)
                transformed = [world @ Vector(p) for p in positions]
                assert all(math.isfinite(c) for p in transformed for c in p)
                for start in range(0, len(indices), 3):
                    a, b, c = [transformed[indices[start + j][0]] for j in range(3)]
                    assert (b - a).cross(c - a).length > 0, 'Decoded degenerate triangle'
                triangles += len(indices) // 3
                part_triangles += len(indices) // 3
                part_points.extend(transformed)
                points.extend(transformed)
            parts[node['name']] = {'triangles': part_triangles,
                                  'min_m': [min(p[i] for p in part_points) for i in range(3)],
                                  'max_m': [max(p[i] for p in part_points) for i in range(3)]}
        for child in node.get('children', []):
            walk(child, world)

    walk(scene_nodes[0], Matrix.Identity(4))
    lo = [min(p[i] for p in points) for i in range(3)]
    hi = [max(p[i] for p in points) for i in range(3)]
    assert all(math.isfinite(x) for x in lo + hi)
    assert abs(lo[1]) < 1e-6
    assert abs(lo[0] + hi[0]) < 1e-6 and abs(lo[2] + hi[2]) < 1e-6
    return {'triangle_count': triangles, 'bbox_min_m_y_up': lo, 'bbox_max_m_y_up': hi,
            'dimensions_m_y_up': [hi[i] - lo[i] for i in range(3)],
            'glb_bytes': len(raw), 'extensions_used': doc.get('extensionsUsed', []),
            'materials': doc.get('materials', []), 'embedded_images': len(doc.get('images', [])),
            'parts': parts,
            'checks': 'PASS: embedded resources, triangle indices, finite Y-up bounds, bottom-centre identity root'}


def render_preview(path, objects, samples):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = samples
    scene.cycles.seed = 10
    scene.cycles.use_denoising = False
    scene.cycles.max_bounces = 6
    scene.cycles.transmission_bounces = 4
    scene.render.threads_mode = 'FIXED'
    scene.render.threads = 4
    scene.render.resolution_x = 512
    scene.render.resolution_y = 384
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = str(path)
    scene.view_settings.view_transform = 'AgX'
    lo, hi = bounds(objects)
    height = hi[2]
    span = max(hi[i] - lo[i] for i in range(3))
    floor = material('preview_floor_only', (0.35, 0.39, 0.43), roughness=0.85)
    bpy.ops.mesh.primitive_plane_add(size=span * 200, location=(0, 0, -0.0001))
    bpy.context.object.data.materials.append(floor)
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.65, 0.72, 0.85, 1)
    scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.35
    for name, xyz, power, size in [
            ('key', (-2, -3, 4), 220, 2.8), ('rim', (2, 1, 3), 150, 2),
            ('fill', (0, -2, 1), 35, 1.2)]:
        light = bpy.data.lights.new(name, 'AREA')
        light.energy = power * span * span
        light.shape = 'DISK'
        light.size = size * span
        obj = bpy.data.objects.new(name, light)
        bpy.context.collection.objects.link(obj)
        obj.location = Vector(xyz) * span
        obj.rotation_euler = (Vector((0, 0, height * 0.45)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    camera = bpy.data.cameras.new('preview_camera')
    obj = bpy.data.objects.new('preview_camera', camera)
    bpy.context.collection.objects.link(obj)
    obj.location = Vector((1.7 * span, -2.5 * span, height * 0.5 + 1.5 * span))
    obj.rotation_euler = (Vector((0, 0, height * 0.45)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    camera.type = 'ORTHO'
    camera.ortho_scale = max(span * 1.7, height * 1.9)
    scene.camera = obj
    assert bpy.app.background and scene.cycles.device == 'CPU'
    print(f'RENDER engine={scene.render.engine} device={scene.cycles.device} '
          f'threads={scene.render.threads} samples={samples} size=512x384', flush=True)
    bpy.ops.render.render(write_still=True)


def lathe(name, profile, mat, segments=96, facets=0, smooth=True, close_profile=False):
    # profile entries: radius, height, polygon blend. Zero radius uses one pole vertex.
    vertices, rings, faces = [], [], []
    for radius, height, blend in profile:
        ring = []
        if radius == 0:
            ring.append(len(vertices))
            vertices.append((0, 0, height))
        else:
            for j in range(segments):
                theta = math.tau * j / segments
                factor = 1.0
                if facets and blend:
                    sector = math.tau / facets
                    polygon = math.cos(sector / 2) / math.cos(theta % sector - sector / 2)
                    factor = 1 - blend + blend * polygon
                ring.append(len(vertices))
                vertices.append((radius * factor * math.cos(theta), radius * factor * math.sin(theta), height))
        rings.append(ring)
    following = rings[1:] + ([rings[0]] if close_profile else [])
    for first, second in zip(rings, following):
        for j in range(segments):
            k = (j + 1) % segments
            if len(first) == 1:
                faces.append((first[0], second[k], second[j]))
            elif len(second) == 1:
                faces.append((first[j], first[k], second[0]))
            else:
                faces.append((first[j], first[k], second[k], second[j]))
    obj = mesh_object(name, vertices, faces, mat, smooth)
    # Split normals at polygon corners, preserving cylindrical shoulder smoothing.
    if facets:
        for polygon in obj.data.polygons:
            if polygon.center.z < profile[4][1]:
                polygon.use_smooth = False
    return obj


def thread(mat, radius, z_start, pitch, turns, tube_radius, steps=192):
    vertices, faces = [], []
    sides = 8
    for i in range(steps + 1):
        theta = math.tau * turns * i / steps
        radial = Vector((math.cos(theta), math.sin(theta), 0))
        center = radial * radius + Vector((0, 0, z_start + pitch * turns * i / steps))
        tangent = Vector((-radius * math.sin(theta), radius * math.cos(theta), pitch / math.tau)).normalized()
        second = tangent.cross(radial).normalized()
        for j in range(sides):
            phi = math.tau * j / sides
            vertices.append(center + tube_radius * (radial * math.cos(phi) + second * math.sin(phi)))
    for i in range(steps):
        for j in range(sides):
            k = (j + 1) % sides
            faces.append((i * sides + j, i * sides + k, (i + 1) * sides + k, (i + 1) * sides + j))
    faces.append(tuple(reversed(range(sides))))
    faces.append(tuple(steps * sides + j for j in range(sides)))
    return mesh_object('neck_thread_visual_approximation', vertices, faces, mat, smooth=True)


def lid_uv(obj, diameter):
    uv = obj.data.uv_layers.new(name='planar_top_wrapped_skirt')
    for polygon in obj.data.polygons:
        skirt = abs(polygon.normal.z) < 0.5
        coords = [obj.data.vertices[obj.data.loops[i].vertex_index].co for i in polygon.loop_indices]
        angles = [math.atan2(v.y, v.x) / math.tau % 1 for v in coords]
        if max(angles) - min(angles) > 0.5:
            angles = [a + 1 if a < 0.5 else a for a in angles]
        for index, v, angle in zip(polygon.loop_indices, coords, angles):
            if skirt:
                uv.data[index].uv = (angle * 3, (v.z - min(c.co.z for c in obj.data.vertices)) / diameter)
            else:
                uv.data[index].uv = (v.x / diameter + 0.5, v.y / diameter + 0.5)
