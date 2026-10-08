"""Artifact and recipe acceptance checks, run with Blender --background."""
import argparse
import json
import struct
import sys
import time
from pathlib import Path

import bpy

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import bounds, deliver, inspect_glb, reset
import box
import jar


def check_roundtrip(out, name, mesh_count):
    result = inspect_glb(out / (name + '.glb'))
    metrics = json.loads((out / (name + '.metrics.json')).read_text(encoding='utf-8'))
    assert result['triangle_count'] == metrics['triangle_count']
    assert result['dimensions_m_y_up'] == metrics['dimensions_m_y_up']
    png = (out / (name + '.png')).read_bytes()
    assert png[:8] == b'\x89PNG\r\n\x1a\n'
    assert struct.unpack_from('>II', png, 16) == (512, 384)
    reset()
    bpy.ops.import_scene.gltf(filepath=str(out / (name + '.glb')))
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    assert len(meshes) == mesh_count
    lo, hi = bounds(meshes)
    dimensions = [hi[0] - lo[0], hi[2] - lo[2], hi[1] - lo[1]]
    assert all(abs(a - b) < 1e-6 for a, b in zip(dimensions, result['dimensions_m_y_up']))
    assert abs(lo[2]) < 1e-6
    for obj in meshes:
        obj.data.calc_loop_triangles()
    assert sum(len(o.data.loop_triangles) for o in meshes) == result['triangle_count']
    if name == 'bonne_maman_jar':
        assert result['embedded_images'] == 1
        glass = next(m for m in result['materials'] if m['name'] == 'clear_glass')
        assert glass['extensions']['KHR_materials_transmission']['transmissionFactor'] == 1
        lid = next(m for m in result['materials'] if m['name'] == 'red_white_gingham_enamel')
        assert 'baseColorTexture' in lid['pbrMetallicRoughness']
    print(f'PASS {name}: GLB roundtrip bounds/triangles, {mesh_count} asset meshes, 512x384 PNG', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--out', required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    out = Path(args.out).resolve()
    check_roundtrip(out, 'cardboard_box', 9)
    check_roundtrip(out, 'bonne_maman_jar', 3)
    # Change all size parameters, close the flaps, and independently check outer dimensions.
    started = time.perf_counter()
    objects = box.build(0.2, 0.16, 0.12, 0.004, 0)
    result = deliver(objects, 'cardboard_box', out / 'variant_box', {}, started, render=False)
    assert all(abs(a - b) < 1e-6 for a, b in zip(result['dimensions_m_y_up'], [0.2, 0.12, 0.16]))
    print('PASS box parameters: 0.20x0.16x0.12 m, 4 mm thickness, closed flaps', flush=True)
    # Raised lid reveals that neck thread is actual geometry, with its own nonzero volume.
    started = time.perf_counter()
    objects = jar.build(0.105, 0.088, 0.086, 0.0025, 0.02)
    result = deliver(objects, 'bonne_maman_jar', out / 'variant_jar', {}, started, render=False)
    assert abs(result['dimensions_m_y_up'][1] - 0.125) < 1e-6
    assert result['topology']['neck_thread_visual_approximation']['signed_volume_m3'] > 0
    print('PASS jar parameters: resized body/lid, 2.5 mm wall, 20 mm lid lift, solid geometric thread', flush=True)
    print('SPIKE_ACCEPTANCE: 4/4 cases passed', flush=True)
