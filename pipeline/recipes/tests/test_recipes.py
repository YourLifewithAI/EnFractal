import hashlib
import itertools
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import time
import unittest
import uuid

sys.dont_write_bytecode = True
from pipeline.recipes.specs import RECIPES, resolve, strict_loads
from pipeline.recipes.contract_check import check_glb
from pipeline.recipes import style as shape_style

REPO = Path(__file__).resolve().parents[3]
RUNNER = REPO / 'pipeline' / 'recipes' / 'build.py'


def working_folder(prefix):
    base = Path(os.environ.get('RECIPE_TEST_ARTIFACTS') or tempfile.gettempdir()).resolve()
    root = base / (prefix + uuid.uuid4().hex)
    os.makedirs(root)
    return root


class InputTests(unittest.TestCase):
    def test_bad_inputs(self):
        base = {'recipe': 'couch', 'size_m': [2.1, 0.83, 0.95]}
        changes = [{'unexpected': 1}, {'recipe': '../evil'}, {'size_m': [1, 2]},
                   {'size_m': [True, 1, 1]}, {'size_m': [0, 1, 1]},
                   {'size_m': [1e309, 1, 1]}, {'size_m': [21, 1, 1]},
                   {'size_m': [20, 0.02, 1]}, {'params': {'cushion_count': 7}},
                   {'params': {'cushion_count': 2.0}}, {'params': {'cushion_count': False}},
                   {'params': {'arbitrary': 1}}, {'colours': {'legs': '#12345g'}},
                   {'colours': {'arbitrary': '#123456'}}, {'colours': []}, {'params': None},
                   {'style': None}, {'style': 'photo'}, {'style': True}]
        for change in changes:
            with self.subTest(change=change), self.assertRaises(ValueError):
                resolve({**base, **change})
        for name, spec in RECIPES.items():
            for param, rule in spec['params'].items():
                bad = ['unknown'] if len(rule) == 2 else [rule[1] - 1, rule[2] + 1]
                for value in bad:
                    with self.subTest(recipe=name, param=param, value=value), self.assertRaises(ValueError):
                        resolve({'recipe': name, 'size_m': spec['example_size_m'], 'params': {param: value}})
        for text in ('{"recipe":"couch","recipe":"couch"}', '{"x":NaN}', '{"x":Infinity}'):
            with self.assertRaises(ValueError):
                strict_loads(text)

    def test_defaults_and_sources(self):
        used = resolve({'recipe': 'couch', 'size_m': [2, 1, 1],
                        'colours': {'legs': '#AaBbCc'}, 'params': {'cushion_count': 2}})
        self.assertEqual(used['colours']['legs'], {'value': '#aabbcc', 'source': 'given'})
        self.assertEqual(used['colours']['upholstery']['source'], 'default')
        self.assertEqual(used['params']['cushion_count']['source'], 'given')
        self.assertEqual(used['params']['seat_height_fraction']['source'], 'default')
        self.assertEqual(used['style'], {'value': 'storybook', 'source': 'default'})
        self.assertEqual(resolve({'recipe': 'couch', 'size_m': [2, 1, 1],
                                  'style': 'plain'})['style']['value'], 'plain')

    def test_style_seed(self):
        data = {'style': 'storybook', 'size_m': [2, 1, 1], 'colours': {'legs': '#112233'}}
        shape_style.configure(data)
        first = shape_style.signed('arm_1')
        shape_style.configure(dict(reversed(list(data.items()))))
        self.assertEqual(first, shape_style.signed('arm_1'))
        self.assertNotEqual(first, shape_style.signed('arm_-1'))
        shape_style.configure({**data, 'colours': {'legs': '#112234'}})
        self.assertNotEqual(first, shape_style.signed('arm_1'))
        self.assertLessEqual(abs(first), 1)

    def test_cli_refuses_before_launch(self):
        root = working_folder('recipe-invalid-')
        try:
            path = root / 'bad.json'
            path.write_text('{"recipe":"couch","size_m":[1,1,1],"shell":"oops"}\n',
                            encoding='utf-8', newline='\n')
            env = {**os.environ, 'BLENDER': str(root / 'nonexistent.exe')}
            process = subprocess.run([sys.executable, '-B', str(RUNNER), str(path), '--out', str(root / 'out')],
                                     env=env, capture_output=True, text=True)
            self.assertNotEqual(process.returncode, 0)
            self.assertIn('unknown keys: shell', process.stderr)
            self.assertFalse((root / 'out').exists())
        finally:
            shutil.rmtree(root)


class BuildTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.started = time.perf_counter()
        cls.retain_artifacts = bool(os.environ.get('RECIPE_TEST_ARTIFACTS'))
        cls.root = working_folder('recipe-tests-')
        cls.receipts = {}

    @classmethod
    def tearDownClass(cls):
        print(f'BUILD_TEST_SECONDS {time.perf_counter() - cls.started:.3f}', flush=True)
        if not cls.retain_artifacts:
            shutil.rmtree(cls.root)
        else:
            print(f'ARTIFACTS {cls.root}', flush=True)

    def build_case(self, name, case, size, params=None, style=None):
        out = self.root / name / case
        out.mkdir(parents=True, exist_ok=True)
        document = {'recipe': name, 'size_m': size}
        if params is not None:
            document['params'] = params
        if style is not None:
            document['style'] = style
        path = out / 'input.json'
        path.write_text(json.dumps(document) + '\n', encoding='utf-8', newline='\n')
        process = subprocess.run([sys.executable, '-B', str(RUNNER), str(path), '--out', str(out)],
                                 capture_output=True, text=True, timeout=660)
        self.assertEqual(process.returncode, 0, process.stdout + process.stderr)
        self.assertFalse((out / '.blender-work').exists())
        receipt = json.loads((out / (name + '.receipt.json')).read_text(encoding='utf-8'))
        self.assertTrue(all(check['passed'] for check in receipt['checks'].values()))
        for axis in range(3):
            self.assertAlmostEqual(receipt['dimensions_m'][axis], size[axis], delta=0.001)
            lo, hi = receipt['bounds']['min_m'], receipt['bounds']['max_m']
            self.assertAlmostEqual(lo[axis], 0 if axis == 1 else -size[axis] / 2, delta=0.001)
            self.assertAlmostEqual(hi[axis], size[axis] if axis == 1 else size[axis] / 2, delta=0.001)
        raw = (out / (name + '.glb')).read_bytes()
        self.assertEqual(check_glb(raw, name), [])
        self.assertEqual(hashlib.sha256(raw).hexdigest(), receipt['glb']['sha256'])
        length = struct.unpack_from('<I', raw, 12)[0]
        gltf = json.loads(raw[20:20 + length])
        self.assertEqual({m['name'] for m in gltf['materials']}, set(RECIPES[name]['colours']))
        if name != 'jam_jar':
            self.assertTrue(all(not any(key.startswith('TEXCOORD_') for key in primitive['attributes'])
                                for mesh in gltf['meshes'] for primitive in mesh['primitives']))
        for mat in receipt['materials']:
            exported = next(m for m in gltf['materials'] if m['name'] == mat['slot'])
            self.assertEqual(exported['extras']['material_role'], mat['role'])
            if receipt['inputs_used']['style']['value'] == 'storybook' and mat['role'] == 'glass':
                self.assertEqual(exported['alphaMode'], 'BLEND')
                self.assertAlmostEqual(exported['pbrMetallicRoughness']['baseColorFactor'][3], 0.22)
                self.assertNotIn('KHR_materials_transmission', exported.get('extensions', {}))
        for part in receipt['parts'].values():
            self.assertEqual(part['degenerate_triangles'], 0)
            self.assertEqual(part['nonmanifold_edges'], 0)
            self.assertGreater(part['signed_volume_m3'], 0)
        self.assertLessEqual(receipt['triangle_count'], 20000 if name == 'couch' else 10000)
        png = (out / (name + '.png')).read_bytes()
        self.assertEqual(png[:8], b'\x89PNG\r\n\x1a\n')
        width, height = struct.unpack_from('>II', png, 16)
        self.assertLessEqual(width, 640)
        self.assertLessEqual(height, 480)
        self.assertEqual(receipt['preview']['device'], 'CPU')
        self.assertEqual(receipt['preview']['denoising'], 'OPENIMAGEDENOISE_CPU')
        self.assertEqual(receipt['preview']['camera_angle_deg'], 30)
        self.assertLess(len(png), 400000)
        self.assertTrue(receipt['collision_shapes'])
        for shape in receipt['collision_shapes']:
            self.assertEqual(shape['shape'], 'box')
            self.assertTrue(all(v > 0 for v in shape['size_m']))
        self.assertNotIn(b'\r', (out / (name + '.receipt.json')).read_bytes())
        print(process.stdout.strip(), flush=True)
        return receipt, raw

    def test_01_default_small_large(self):
        for name, spec in RECIPES.items():
            for case, factor in [('default', 1), ('small', 0.5), ('large', 2)]:
                size = [value * factor for value in spec['example_size_m']]
                with self.subTest(recipe=name, size=case):
                    receipt, _ = self.build_case(name, case, size)
                    self.receipts[(name, case)] = receipt

    def test_02_alternate_states(self):
        cases = {'cardboard_box': {'flap_angle_deg': 120},
                 'couch': {'cushion_count': 6, 'seat_height_fraction': 0.4, 'leg_height_fraction': 0.25},
                 'gaming_laptop': {'lid_angle_deg': 0},
                 'jam_jar': {'lid_pattern': 'stripes', 'lid_lift_fraction': 0.3, 'facets': 8},
                 'french_press': {'beaker_material': 'metal', 'plunger_fraction': 0.75}}
        for name, params in cases.items():
            with self.subTest(recipe=name):
                receipt, _ = self.build_case(name, 'alternate', RECIPES[name]['example_size_m'], params)
                if name == 'french_press':
                    self.assertEqual(receipt['materials'][0]['role'], 'metal')

    def test_03_determinism(self):
        for name, spec in RECIPES.items():
            with self.subTest(recipe=name):
                _, repeated = self.build_case(name, 'repeat', spec['example_size_m'])
                original = (self.root / name / 'default' / (name + '.glb')).read_bytes()
                self.assertEqual(original, repeated, f'{name}: GLB bytes differ')
                print(f'DETERMINISM {name} byte_identical=True sha256={hashlib.sha256(repeated).hexdigest()}', flush=True)

    def test_04_couch_param_extremes(self):
        spec = RECIPES['couch']
        keys = tuple(spec['params'])
        endpoints = [(rule[1], rule[2]) for rule in spec['params'].values()]
        for combination in itertools.product(*endpoints):
            params = dict(zip(keys, combination))
            for label, factor in [('small', 0.5), ('default', 1), ('large', 2)]:
                size = [value * factor for value in spec['example_size_m']]
                case = 'endpoints_' + '_'.join(map(str, combination)) + '_' + label
                with self.subTest(params=params, size=label):
                    self.build_case('couch', case, size, params)
                    print(f'COUCH_EXTREMES params={json.dumps(params, sort_keys=True)} size={label} PASS',
                          flush=True)

    def test_05_plain_and_default_style(self):
        for name, spec in RECIPES.items():
            with self.subTest(recipe=name):
                _, plain = self.build_case(name, 'plain', spec['example_size_m'], style='plain')
                _, repeat = self.build_case(name, 'plain_repeat', spec['example_size_m'], style='plain')
                self.assertEqual(plain, repeat, f'{name}: plain GLB bytes differ')
                _, explicit = self.build_case(name, 'explicit_storybook', spec['example_size_m'], style='storybook')
                default = (self.root / name / 'default' / (name + '.glb')).read_bytes()
                self.assertEqual(default, explicit, f'{name}: omitted style differs from storybook')
                self.assertNotEqual(plain, explicit, f'{name}: style has no exported effect')
                print(f'PLAIN_AND_STORYBOOK {name} bounds_fit=True byte_identical=True distinct_styles=True', flush=True)


if __name__ == '__main__':
    unittest.main()
