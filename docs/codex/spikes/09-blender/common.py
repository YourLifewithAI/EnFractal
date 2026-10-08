"""Headless, CPU-only presentation/export helpers. No external dependencies."""
import json
import math
import struct
import time
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector


def reset():
    if not bpy.app.background:
        raise RuntimeError('This spike requires --background')
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
    mod = obj.modifiers.new('small_real_edge_bevel', 'BEVEL')
    mod.width = bevel
    mod.segments = 2
    bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.data.materials.append(mat)
    return obj


def bounds(objects):
    points = [obj.matrix_world @ v.co for obj in objects for v in obj.data.vertices]
    return ([min(p[i] for p in points) for i in range(3)],
            [max(p[i] for p in points) for i in range(3)])


def finish_geometry(objects, name):
    bpy.context.view_layer.update()
    lo, hi = bounds(objects)
    shift = Vector(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2]))
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)
    topology = {}
    for obj in objects:
        world = obj.matrix_world.copy()
        for vertex in obj.data.vertices:
            vertex.co = world @ vertex.co - shift
        obj.matrix_world = Matrix.Identity(4)
        obj.parent = root
        obj.data.update()
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        boundary = sum(1 for e in bm.edges if not e.is_manifold)
        volume = bm.calc_volume(signed=True)
        bm.free()
        obj.data.calc_loop_triangles()
        degenerate = sum(1 for tri in obj.data.loop_triangles if tri.area < 1e-14)
        if boundary or degenerate or volume <= 0:
            raise RuntimeError(f'{obj.name}: invalid solid: {boundary=}, {degenerate=}, {volume=}')
        topology[obj.name] = {'triangles': len(obj.data.loop_triangles),
                              'nonmanifold_edges': boundary, 'degenerate_triangles': degenerate,
                              'signed_volume_m3': volume}
    bpy.context.view_layer.update()
    return root, topology


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

    points, triangles = [], 0
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
            for primitive in doc['meshes'][node['mesh']]['primitives']:
                assert primitive.get('mode', 4) == 4
                positions = accessor(primitive['attributes']['POSITION'])
                indices = accessor(primitive['indices'])
                assert len(indices) % 3 == 0
                assert all(0 <= value[0] < len(positions) for value in indices)
                triangles += len(indices) // 3
                points.extend(world @ Vector(p) for p in positions)
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
            'checks': 'PASS: embedded resources, triangle indices, finite Y-up bounds, bottom-centre identity root'}


def render_preview(path, objects, samples):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = samples
    scene.cycles.seed = 9
    scene.cycles.use_denoising = False
    scene.cycles.max_bounces = 12
    scene.cycles.transmission_bounces = 8
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


def deliver(objects, name, out_dir, params, started, samples=48, render=True):
    out = Path(out_dir).resolve()
    out.mkdir(parents=True, exist_ok=True)
    root, topology = finish_geometry(objects, name)
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    for obj in objects:
        obj.select_set(True)
    export_started = time.perf_counter()
    glb = out / (name + '.glb')
    bpy.ops.export_scene.gltf(filepath=str(glb), export_format='GLB', use_selection=True,
                              export_yup=True, export_apply=True, export_materials='EXPORT',
                              export_image_format='AUTO', export_animations=False,
                              export_cameras=False, export_lights=False)
    export_seconds = time.perf_counter() - export_started
    result = inspect_glb(glb)
    assert result['triangle_count'] == sum(t['triangles'] for t in topology.values())
    lo, hi = bounds(objects)
    expected = [hi[0] - lo[0], hi[2] - lo[2], hi[1] - lo[1]]
    assert all(abs(a - b) < 1e-6 for a, b in zip(expected, result['dimensions_m_y_up']))
    render_started = time.perf_counter()
    if render:
        render_preview(out / (name + '.png'), objects, samples)
    result.update({'blender': bpy.app.version_string, 'parameters': params, 'topology': topology,
                   'export_seconds': export_seconds,
                   'render_seconds': time.perf_counter() - render_started if render else 0,
                   'script_seconds': time.perf_counter() - started,
                   'render_device': 'CPU' if render else None,
                   'preview_pixels': [512, 384] if render else None})
    (out / (name + '.metrics.json')).write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print('RESULT ' + json.dumps({k: result[k] for k in ['triangle_count', 'dimensions_m_y_up',
          'glb_bytes', 'export_seconds', 'render_seconds', 'script_seconds', 'checks']}), flush=True)
    return result
