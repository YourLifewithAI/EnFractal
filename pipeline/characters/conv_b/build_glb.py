"""Blender half of the converter: a build plan (balls in metres) -> one self-contained GLB with named parts.

    blender -b --factory-startup -P build_glb.py -- --plan <build_plan.json> --glb <character.glb> --report <report.json>

Each piece is a union of balls (a drawn area inflated into a rounded body, a stroke into a tube), fused into one
clean surface by a voxel remesh, smoothed and reduced. Stickers (eyes, pads, patches) and ink lines (mouths, brows)
are laid on the front surface of what they sit on by casting rays. Blender is Z-up with the character facing +Y;
the glTF exporter turns that into Godot's +Y up, facing -Z.
"""
import json
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree


def arguments():
    argv = sys.argv[sys.argv.index('--') + 1:]
    opts = dict(zip(argv[::2], argv[1::2]))
    return opts['--plan'], opts['--glb'], opts['--report']


def icosphere():
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0)
    verts = np.array([v.co[:] for v in bm.verts], np.float64)
    faces = np.array([[v.index for v in f.verts] for f in bm.faces], np.int64)
    bm.free()
    return verts, faces


UNIT_V, UNIT_F = icosphere()


def mesh_object(name, verts, faces):
    mesh = bpy.data.meshes.new(name)
    mesh.vertices.add(len(verts))
    mesh.vertices.foreach_set('co', np.asarray(verts, np.float32).ravel())
    mesh.loops.add(faces.size)
    mesh.loops.foreach_set('vertex_index', np.asarray(faces, np.int32).ravel())
    mesh.polygons.add(len(faces))
    mesh.polygons.foreach_set('loop_start', (np.arange(len(faces)) * 3).astype(np.int32))
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def balls_object(name, centres, radii):
    """Many ellipsoids in one mesh: centres (n,3), radii (n,3)."""
    n = len(centres)
    verts = (UNIT_V[None, :, :] * radii[:, None, :] + centres[:, None, :]).reshape(-1, 3)
    faces = (UNIT_F[None, :, :] + (np.arange(n) * len(UNIT_V))[:, None, None]).reshape(-1, 3)
    return mesh_object(name, verts, faces)


def apply_modifiers(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = bpy.data.meshes.new_from_object(evaluated)
    old = obj.data
    obj.modifiers.clear()
    obj.data = mesh
    bpy.data.meshes.remove(old)


def area(obj):
    return sum(p.area for p in obj.data.polygons)


def target_for(surface_area, lo, hi):
    return int(min(hi, max(lo, surface_area / (0.0011 ** 2) * 1.6)))


def fuse(obj, voxel, smooth, lo, hi):
    """Remesh the overlapping balls into one surface, smooth the voxel steps, reduce to a budget by area."""
    rm = obj.modifiers.new('remesh', 'REMESH')
    rm.mode = 'VOXEL'
    rm.voxel_size = voxel
    rm.adaptivity = 0.0
    sm = obj.modifiers.new('smooth', 'SMOOTH')
    sm.factor = 0.5
    sm.iterations = smooth
    obj.modifiers.new('tri', 'TRIANGULATE')
    apply_modifiers(obj)
    tris = len(obj.data.polygons)
    target = target_for(area(obj), lo, hi)
    if tris > target:
        dec = obj.modifiers.new('decimate', 'DECIMATE')
        dec.decimate_type = 'COLLAPSE'
        dec.ratio = max(0.01, target / tris)
        obj.modifiers.new('tri', 'TRIANGULATE')
        apply_modifiers(obj)


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


MATERIALS = {}


def material(hex_colour):
    if hex_colour not in MATERIALS:
        h = hex_colour.lstrip('#')
        rgb = [srgb_to_linear(int(h[i:i + 2], 16) / 255.0) for i in (0, 2, 4)]
        m = bpy.data.materials.new('colour_' + h)
        m.use_nodes = True
        bsdf = m.node_tree.nodes['Principled BSDF']
        bsdf.inputs['Base Color'].default_value = (*rgb, 1.0)
        bsdf.inputs['Roughness'].default_value = 0.8
        bsdf.inputs['Metallic'].default_value = 0.0
        MATERIALS[hex_colour] = m
    return MATERIALS[hex_colour]


def surface_tree(objects):
    verts, polys = [], []
    offset = 0
    for o in objects:
        n = len(o.data.vertices)
        co = np.empty(n * 3, np.float32)
        o.data.vertices.foreach_get('co', co)
        verts.extend(Vector(v) for v in co.reshape(-1, 3).tolist())
        idx = np.empty(len(o.data.polygons) * 3, np.int32)
        o.data.polygons.foreach_get('vertices', idx)
        polys.extend((idx.reshape(-1, 3) + offset).tolist())
        offset += n
    if not polys:
        return None
    return BVHTree.FromPolygons(verts, polys)


def front_y(tree, x, z):
    hit = tree.ray_cast(Vector((x, 1.0, z)), Vector((0.0, -1.0, 0.0)), 2.0)
    return None if hit[0] is None else hit[0].y


def join(name, objs, pivot):
    """The pieces of one part as one mesh (a material slot per colour), vertices relative to the pivot."""
    verts, faces, mats, slots = [], [], [], []
    offset = 0
    for o in objs:
        n = len(o.data.vertices)
        co = np.empty(n * 3, np.float32)
        o.data.vertices.foreach_get('co', co)
        verts.append(co.reshape(-1, 3) - np.array(pivot, np.float32))
        idx = np.empty(len(o.data.polygons) * 3, np.int32)
        o.data.polygons.foreach_get('vertices', idx)
        faces.append(idx.reshape(-1, 3) + offset)
        m = o.data.materials[0]
        if m not in slots:
            slots.append(m)
        mats.append(np.full(len(o.data.polygons), slots.index(m), np.int32))
        offset += n
    for o in objs:
        old = o.data
        bpy.data.objects.remove(o)
        bpy.data.meshes.remove(old)
    obj = mesh_object(name, np.concatenate(verts), np.concatenate(faces))
    mesh = obj.data
    mesh.polygons.foreach_set('material_index', np.concatenate(mats))
    mesh.polygons.foreach_set('use_smooth', np.ones(len(mesh.polygons), bool))
    for m in slots:
        mesh.materials.append(m)
    mesh.update()
    return obj


def main():
    plan_path, glb_path, report_path = arguments()
    with open(plan_path, encoding='utf-8') as f:
        plan = json.load(f)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    parts = plan['parts']
    pieces_by_part = {p['name']: [] for p in parts}
    halfthick = {}

    # Pass 1: the rounded bodies (puffs) and free tubes, parents first as the reading lists them.
    for part in parts:
        for k, piece in enumerate(part['pieces']):
            if piece['kind'] not in ('puff', 'tube'):
                continue
            b = np.array(piece['balls'], np.float64)
            r = b[:, 2]
            y = np.full(len(b), piece.get('forward_m', 0.0))
            if piece['kind'] == 'tube' and piece.get('behind') and part['parent'] in halfthick:
                y = y - piece['behind'] * halfthick[part['parent']]
            centres = np.stack([b[:, 0], y, b[:, 1]], axis=1)
            radii = np.stack([r, r * piece['depth'], r], axis=1)
            obj = balls_object(f"{part['name']}_{k}", centres, radii)
            thin = min(piece['dmax_m'] * min(1.0, piece['depth']), 0.004)
            voxel = max(0.00016, min(0.0006, thin / 3.0))
            obj.data.materials.append(material(piece['colour']))
            fuse(obj, voxel, 3, 150, 3000)
            pieces_by_part[part['name']].append(obj)
            halfthick[part['name']] = max(halfthick.get(part['name'], 0.0), piece['dmax_m'] * piece['depth'])

    # Pass 2: stickers and ink, laid on whatever is frontmost, in the reading's order.
    surfaces = [o for objs in pieces_by_part.values() for o in objs]
    for part in parts:
        for k, piece in enumerate(part['pieces']):
            if piece['kind'] not in ('sticker', 'ink'):
                continue
            tree = surface_tree(surfaces)
            if tree is None:
                continue
            b = np.array(piece['balls'], np.float64)
            centres, radii = [], []
            for x, z, r in b:
                fy = front_y(tree, x, z)
                if fy is None:
                    continue
                if piece['kind'] == 'sticker':
                    t = piece['thickness_m']
                    centres.append((x, fy + t * 0.25, z))
                    radii.append((r, min(t, r), r))
                else:
                    centres.append((x, fy + r * 0.2, z))
                    radii.append((r, r * 0.8, r))
            if not centres:
                continue
            obj = balls_object(f"{part['name']}_{k}", np.array(centres), np.array(radii))
            thin = piece['thickness_m'] if piece['kind'] == 'sticker' else float(b[0, 2])
            voxel = max(0.00008, min(0.0003, thin / 2.5))
            obj.data.materials.append(material(piece['colour']))
            fuse(obj, voxel, 2, 60, 900)
            pieces_by_part[part['name']].append(obj)
            surfaces.append(obj)

    # Join each part's pieces into one object whose origin is its pivot; parent them like the reading.
    root = bpy.data.objects.new('character', None)
    scene.collection.objects.link(root)
    part_objects, pivots = {}, {}
    for part in parts:
        objs = pieces_by_part[part['name']]
        if not objs:
            continue
        px, pz = part['pivot']
        pivot = (px, 0.0, pz)
        if part.get('surface'):
            co = np.concatenate([np.array([v.co[:] for v in o.data.vertices]) for o in objs])
            pivot = tuple(round(float(c), 6) for c in co.mean(axis=0))
        part_objects[part['name']] = join(part['name'], objs, pivot)
        pivots[part['name']] = Vector(pivot)
    for part in parts:
        obj = part_objects.get(part['name'])
        if obj is None:
            continue
        parent = part_objects.get(part['parent']) if part['parent'] else None
        obj.parent = parent or root
        obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.location = pivots[part['name']] - (pivots[part['parent']] if parent else Vector())
    bpy.context.view_layer.update()

    # Report: bounds in Godot axes, parts and pivots.
    meshes = [part_objects[p['name']] for p in parts if p['name'] in part_objects]
    pts = np.concatenate([np.array([list(o.matrix_world @ v.co) for v in o.data.vertices]) for o in meshes])
    lo, hi = pts.min(axis=0), pts.max(axis=0)
    report_parts = []
    for part in parts:
        obj = part_objects.get(part['name'])
        if obj is None:
            continue
        t = obj.matrix_world.translation
        report_parts.append({
            'name': part['name'], 'role': part['role'], 'parent': part['parent'],
            'pivot': [round(t.x, 4), round(t.z, 4), round(-t.y, 4)],
            'colours': sorted({p['colour'] for p in part['pieces']}),
            'triangles': len(obj.data.polygons),
        })
    report = {
        'height_m': round(float(hi[2] - lo[2]), 4), 'width_m': round(float(hi[0] - lo[0]), 4),
        'depth_m': round(float(hi[1] - lo[1]), 4), 'bottom_m': round(float(lo[2]), 5),
        'triangles': int(sum(len(o.data.polygons) for o in meshes)), 'parts': report_parts,
    }
    bpy.ops.export_scene.gltf(filepath=glb_path, export_format='GLB', export_yup=True, export_apply=True,
                              export_texcoords=False, export_animations=False)
    with open(report_path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(report, f, indent=1)


main()
