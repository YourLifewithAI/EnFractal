"""Tests for the converter: determinism, our own test drawing, and the output contract.

    python <this folder>/tests/test_converter.py

Outputs go to CHARACTER_TEST_OUT (default: _tests in this converter's folder under
C:\\dev\\EnFractal-art\\characters\\out\\), outside Git. Characters already built there are checked too.
"""
import json
import os
import shutil
import struct
import subprocess
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
CONVERTER = HERE.parent / 'convert.py'
sys.path.insert(0, str(HERE))
import make_test_drawing  # noqa: E402

ART_OUT = Path(r'C:\dev\EnFractal-art\characters\out') / HERE.parent.name
OUT = Path(os.environ.get('CHARACTER_TEST_OUT', str(ART_OUT / '_tests')))
BUILT = OUT.parent  # where the judged characters live


def read_glb(path):
    data = Path(path).read_bytes()
    magic, version, length = struct.unpack_from('<III', data, 0)
    assert magic == 0x46546C67 and version == 2 and length == len(data)
    chunk_len, chunk_type = struct.unpack_from('<II', data, 12)
    assert chunk_type == 0x4E4F534A
    return json.loads(data[20:20 + chunk_len])


def world_bounds(gltf):
    """Bounds of every mesh in the scene, from accessor min/max and the node translations above it."""
    nodes = gltf['nodes']
    parent = {}
    for i, n in enumerate(nodes):
        for c in n.get('children', []):
            parent[c] = i
    lo, hi = [1e9] * 3, [-1e9] * 3
    for i, n in enumerate(nodes):
        if 'mesh' not in n:
            continue
        offset, j = [0.0, 0.0, 0.0], i
        while j is not None:
            node = nodes[j]
            assert 'rotation' not in node and 'scale' not in node and 'matrix' not in node, node.get('name')
            t = node.get('translation', [0, 0, 0])
            offset = [offset[k] + t[k] for k in range(3)]
            j = parent.get(j)
        for prim in gltf['meshes'][n['mesh']]['primitives']:
            acc = gltf['accessors'][prim['attributes']['POSITION']]
            lo = [min(lo[k], acc['min'][k] + offset[k]) for k in range(3)]
            hi = [max(hi[k], acc['max'][k] + offset[k]) for k in range(3)]
    return lo, hi


def convert(photo, description, reading, out):
    if out.exists():
        shutil.rmtree(out)
    run = subprocess.run([sys.executable, str(CONVERTER), '--photo', str(photo), '--description', str(description),
                          '--reading', str(reading), '--out', str(out), '--no-render'],
                         capture_output=True, text=True, encoding='utf-8', errors='replace')
    if run.returncode != 0:
        raise AssertionError(run.stdout + run.stderr)
    return run.stdout


class ContractChecks:
    """Checks every character must pass: size, pivot, axes, named parts, self-contained."""

    def check_character(self, folder, required=('body',)):
        gltf = read_glb(folder / 'character.glb')
        info = json.loads((folder / 'character.json').read_text(encoding='utf-8'))
        lo, hi = world_bounds(gltf)
        height = hi[1] - lo[1]
        self.assertTrue(0.095 <= height <= 0.105, f'height {height:.4f} m')
        self.assertAlmostEqual(lo[1], 0.0, delta=0.0005)  # stands on the origin
        # Self-contained, and no picture of the drawing inside: no images, textures or external files at all.
        self.assertNotIn('images', gltf)
        self.assertNotIn('textures', gltf)
        for b in gltf.get('buffers', []):
            self.assertNotIn('uri', b)
        names = {n.get('name') for n in gltf['nodes']}
        for name in required:
            self.assertIn(name, names)
        parts = {p['name']: p for p in info['parts']}
        self.assertEqual(set(parts), {n.get('name') for n in gltf['nodes'] if 'mesh' in n})
        for p in parts.values():
            self.assertEqual(len(p['pivot']), 3)
            if p['parent']:
                self.assertIn(p['parent'], parts)
        self.assertIn(info['motion'], ('floats', 'waddles', 'hops', 'strides'))
        self.assertLess(info['triangles'], 40000)
        # Faces -Z: the eyes sit in front of (at smaller z than) the part they are on.
        for p in parts.values():
            if p['role'] == 'eye' and p['parent'] in parts:
                self.assertLess(p['pivot'][2], parts[p['parent']]['pivot'][2])
        # The origin is between the feet: inside the span of whatever touches the ground.
        feet = [p['pivot'][0] for p in parts.values() if p['role'] in ('leg', 'foot')]
        if len(feet) >= 2:
            self.assertTrue(min(feet) - 0.005 <= 0.0 <= max(feet) + 0.005, feet)
        return gltf, info


class TestOwnDrawing(unittest.TestCase, ContractChecks):
    @classmethod
    def setUpClass(cls):
        os.makedirs(OUT, exist_ok=True)
        cls.inputs = make_test_drawing.make(str(OUT / 'blobby_input'))
        cls.first = OUT / 'blobby_1'
        cls.second = OUT / 'blobby_2'
        cls.log = convert(*cls.inputs, cls.first)
        convert(*cls.inputs, cls.second)

    def test_deterministic(self):
        for name in ('character.glb', 'character.json', 'build_plan.json'):
            a = (self.first / name).read_bytes()
            b = (self.second / name).read_bytes()
            self.assertEqual(a, b, f'{name} differs between two runs')

    def test_contract(self):
        gltf, info = self.check_character(self.first, required=(
            'body', 'hat', 'eye_left', 'eye_right', 'mouth', 'arm_left', 'arm_right', 'leg_left', 'leg_right'))
        self.assertEqual(info['name'], 'Blobby')
        self.assertEqual(info['motion'], 'waddles')

    def test_reads_the_drawing(self):
        info = json.loads((self.first / 'character.json').read_text(encoding='utf-8'))
        parts = {p['name']: p for p in info['parts']}
        # Hat on top, legs below, the arms out to the sides, the character's left arm on the image's right (-x).
        self.assertGreater(parts['hat']['pivot'][1], parts['eye_left']['pivot'][1])
        self.assertLess(parts['leg_left']['pivot'][1], parts['body']['pivot'][1] + 0.01)
        self.assertLess(parts['arm_left']['pivot'][0], 0)
        self.assertGreater(parts['arm_right']['pivot'][0], 0)
        self.assertNotIn('skipped', self.log)
        # Colours come from the reading: green body, yellow hat.
        self.assertIn('#5cb85c', parts['body']['colours'])
        self.assertIn('#f2d23c', parts['hat']['colours'])

    def test_empty_output_folder_required(self):
        run = subprocess.run([sys.executable, str(CONVERTER), '--photo', self.inputs[0], '--description',
                              self.inputs[1], '--reading', self.inputs[2], '--out', str(self.first), '--no-render'],
                             capture_output=True, text=True)
        self.assertNotEqual(run.returncode, 0)


class TestJudgedCharacters(unittest.TestCase, ContractChecks):
    def test_built_characters(self):
        found = [d for d in sorted(BUILT.iterdir()) if (d / 'character.glb').exists()] if BUILT.exists() else []
        if not found:
            self.skipTest('no built characters')
        for folder in found:
            with self.subTest(folder.name):
                self.check_character(folder)


if __name__ == '__main__':
    unittest.main(verbosity=2)
