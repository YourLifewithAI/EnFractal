"""Assumed 370 g Bonne Maman proportions; measured TO82 inner lid diameter reference.

Revolved hollow glass profile, twelve-sided lower body, geometric helix and thin lid.
The gingham is generated analytically into a packed image; no photos or downloaded art.
"""
import argparse
import math
import sys
import time
from pathlib import Path

import bpy
from mathutils import Vector

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import deliver, material, mesh_object, reset


def lathe(name, profile, mat, segments=96, facets=0, smooth=True):
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
    for first, second in zip(rings, rings[1:]):
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


def gingham():
    size, checks = 256, 16
    image = bpy.data.images.new('procedural_red_white_gingham', width=size, height=size, alpha=False)
    # White + two pink stripe intersections + deep red. Tiny deterministic weave variation.
    colors = [(0.97, 0.95, 0.91), (0.86, 0.43, 0.44), (0.63, 0.035, 0.05)]
    pixels = []
    for y in range(size):
        for x in range(size):
            level = (x // (size // checks)) % 2 + (y // (size // checks)) % 2
            weave = 0.98 if (x + y) % 2 else 1.0
            pixels.extend([c * weave for c in colors[level]] + [1])
    image.pixels.foreach_set(pixels)
    image.pack()
    mat = material('red_white_gingham_enamel', (1, 1, 1), roughness=0.30, metallic=0.15)
    tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = image
    tex.interpolation = 'Linear'
    mat.node_tree.links.new(tex.outputs['Color'], mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    return mat


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


def build(height, body_diameter, lid_inner_diameter, glass_wall, lid_lift):
    if not (0.07 <= height <= 0.15 and 0.06 <= body_diameter <= 0.11):
        raise ValueError('Recipe is bounded to the 370 g jar family')
    if not (0.001 <= glass_wall <= 0.004 and 0 <= lid_lift <= 0.03):
        raise ValueError('Glass thickness 1..4 mm; lid lift 0..30 mm')
    if lid_inner_diameter < body_diameter * 0.94:
        raise ValueError('Lid too narrow for this neck profile')
    reset()
    glass = material('clear_glass', (0.98, 0.995, 1), roughness=0.055, transmission=1.0)
    r, h, t = body_diameter / 2, height, glass_wall
    neck = lid_inner_diameter / 2 - 0.0025
    # Continuous cross-section closes glass volume, while leaving the vessel mouth open.
    profile = [(0, 0, 0), (r * 0.84, 0, 1), (r * 0.96, h * 0.025, 1),
               (r, h * 0.07, 1), (r, h * 0.68, 1),
               (r * 0.985, h * 0.76, 0.6), (neck + 0.001, h * 0.82, 0),
               (neck, h * 0.855, 0), (neck, h * 0.94, 0),
               (neck - 0.0003, h * 0.95, 0), (neck - t, h * 0.95, 0),
               (neck - t, h * 0.86, 0), (neck - t + 0.001, h * 0.82, 0),
               (r * 0.985 - t, h * 0.76, 0.6), (r - t, h * 0.68, 1),
               (r - t, h * 0.09, 1), (r * 0.89 - t, h * 0.045, 1),
               (0, h * 0.045, 0)]
    body = lathe('hollow_faceted_glass_body', profile, glass, facets=12)
    ridge = thread(glass, neck - 0.00025, h * 0.86, h * 0.027, 2, 0.00075)
    # TO82 inside radius, 0.3 mm metal, 0.7 mm rolled edge; outside diameter assumed 84 mm.
    inside = lid_inner_diameter / 2
    outside = inside + 0.001
    bottom = h * 0.865 + lid_lift
    top = h + lid_lift
    lid_profile = [(0, top, 0), (outside - 0.001, top, 0),
                   (outside, top - 0.0007, 0), (outside, bottom + 0.0007, 0),
                   (outside - 0.0007, bottom, 0), (inside, bottom + 0.0003, 0),
                   (inside, top - 0.001, 0), (0, top - 0.001, 0)]
    lid = lathe('gingham_metal_lid', lid_profile, gingham())
    lid_uv(lid, outside * 2)
    return [body, ridge, lid]


if __name__ == '__main__':
    started = time.perf_counter()
    parser = argparse.ArgumentParser()
    parser.add_argument('--out', required=True)
    parser.add_argument('--height', type=float, default=0.095)
    parser.add_argument('--body-diameter', type=float, default=0.082)
    parser.add_argument('--lid-inner-diameter', type=float, default=0.082)
    parser.add_argument('--glass-wall', type=float, default=0.002)
    parser.add_argument('--lid-lift', type=float, default=0.0)
    parser.add_argument('--no-render', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    objects = build(args.height, args.body_diameter, args.lid_inner_diameter, args.glass_wall, args.lid_lift)
    result = deliver(objects, 'bonne_maman_jar', args.out, vars(args), started, samples=96,
                     render=not args.no_render)
    assert result['embedded_images'] == 1, 'Gingham image must be inside GLB'
    assert 'KHR_materials_transmission' in result['extensions_used'], 'Glass transmission must survive export'
