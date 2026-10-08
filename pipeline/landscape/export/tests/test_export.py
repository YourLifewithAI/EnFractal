import collections
import json
import math
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import unittest
import uuid

from pipeline.landscape.harness.common import PALETTE, SETUP
from pipeline.landscape.harness.kit import SIZES, prototype
from pipeline.landscape.harness.package import read_package, write_package
from pipeline.landscape.export import export_room
from pipeline.landscape.export.mesh import encode, ground, transform

ROOT = Path(__file__).resolve().parents[4]
ROOM = ROOT/'pipeline/landscape/corpus/rooms/garage_nominal'
REFERENCE = ROOT/'pipeline/landscape/harness/reference/package'


def read_glb(path=None, data=None):
    data = data if data is not None else Path(path).read_bytes()
    magic, version, length = struct.unpack_from('<4sII', data)
    assert (magic, version, length) == (b'glTF', 2, len(data))
    size = struct.unpack_from('<I', data, 12)[0]
    doc = json.loads(data[20:20+size])
    binary = data[28+size:]

    def get(index):
        a = doc['accessors'][index]
        view = doc['bufferViews'][a['bufferView']]
        n = {'VEC3': 3, 'VEC4': 4, 'SCALAR': 1}[a['type']]
        code = 'f' if a['componentType'] == 5126 else 'I'
        offset = view.get('byteOffset', 0)+a.get('byteOffset', 0)
        return list(struct.iter_unpack('<'+code*n, binary[offset:offset+a['count']*n*4]))

    parts = []
    for p in doc['meshes'][0]['primitives']:
        positions = get(p['attributes']['POSITION'])
        ids = [i[0] for i in get(p['indices'])]
        mat = doc['materials'][p['material']]
        parts.append({'role': mat['extras']['role'], 'positions': positions,
                      'triangles': [ids[i:i+3] for i in range(0, len(ids), 3)],
                      'normals': get(p['attributes']['NORMAL']),
                      'colors': get(p['attributes']['COLOR_0']), 'material': mat})
    return doc, parts


def triangles(parts):
    return collections.Counter(tuple(tuple(p['positions'][i]) for i in t)
                               for p in parts for t in p['triangles'])


class ExportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.scratch = Path(os.environ.get('TEMP', '/tmp'))/('enfractal-export-tests-'+uuid.uuid4().hex)
        os.makedirs(cls.scratch)
        cls.out = cls.scratch/'a'/'landscape_reference'
        cls.stats = export_room(REFERENCE, ROOM, 'landscape_reference', cls.out)
        cls.manifest = json.loads((cls.out/'room.json').read_bytes())
        cls.package, cls.meshes, _, _ = read_package(REFERENCE, ROOM)

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.scratch)

    def test_reference_validates_and_all_files_are_pinned(self):
        proc = subprocess.run([sys.executable, '-B', str(ROOT/'contracts/validate.py'), '--room', str(self.out)],
                              capture_output=True, text=True, cwd=ROOT)
        print('VALIDATOR exit='+str(proc.returncode)+' '+proc.stdout.strip())
        self.assertEqual(proc.returncode, 0, proc.stdout+proc.stderr)
        self.assertEqual({p.relative_to(self.out).as_posix() for p in self.out.rglob('*') if p.is_file()}-{'room.json'},
                         {e['path'] for e in self.manifest['files']})
        for p in self.out.rglob('*.json'):
            data = p.read_bytes()
            self.assertNotIn(b'\r', data)
            self.assertTrue(data.endswith(b'\n'))

    def test_byte_deterministic(self):
        other = self.scratch/'b'/'landscape_reference'
        self.assertEqual(self.stats, export_room(REFERENCE, ROOM, 'landscape_reference', other))
        a = {p.relative_to(self.out).as_posix(): p.read_bytes() for p in self.out.rglob('*') if p.is_file()}
        b = {p.relative_to(other).as_posix(): p.read_bytes() for p in other.rglob('*') if p.is_file()}
        self.assertEqual(a, b)

    def test_terrain_triangles_and_bounds_match_exactly(self):
        original = [p for r in self.package['terrain'] for p in self.meshes[r['mesh']]]
        exported = []
        for part in self.manifest['shell']['parts']:
            if part['id'].startswith('shell:terrain_'):
                self.assertEqual((part['role'], part['collides']), ('ground', True))
                exported.extend(read_glb(self.out/part['geometry']['mesh'])[1])
        # Full oriented triangle multiset is stronger than count and bounds.
        self.assertEqual(triangles(original), triangles(exported))
        for i in range(3):
            for f in (min, max):
                self.assertEqual(f(v[i] for p in original for v in p['positions']),
                                 f(v[i] for p in exported for v in p['positions']))

    def test_spawns_sample_terrain_and_keep_source_xz_yaw(self):
        terrain = [p for r in self.package['terrain'] for p in self.meshes[r['mesh']]]
        source = json.loads((ROOM/'room.json').read_bytes())
        for a, b in zip(source['spawns'], self.manifest['spawns']):
            self.assertEqual([a['position_m'][0], a['position_m'][2], a['yaw_deg']],
                             [b['position_m'][0], b['position_m'][2], b['yaw_deg']])
            self.assertAlmostEqual(b['position_m'][1], ground(terrain, b['position_m'][0], b['position_m'][2]))

    def test_carryable_object_matches_host_mass_movable_rules(self):
        rules = (ROOT/'game/scripts/native/Sandbox/SandboxRules.cs').read_text()
        import re
        player = float(re.search(r'PlayerCarryLimitKg = ([\d.]+)f', rules)[1])
        companion = float(re.search(r'CompanionCarryLimitKg = ([\d.]+)f', rules)[1])
        obj = next(o for o in self.manifest['objects'] if o['id'] == 'obj:findable_crate')
        asset = json.loads((self.out/obj['asset']).read_bytes())
        self.assertTrue(asset['physics']['movable'])
        self.assertLessEqual(asset['physics']['mass_kg'], min(player, companion))
        self.assertEqual(asset['physics']['mass_kg'], .05)
        self.assertEqual(asset['dimensions_m'], [.12, .105, .105])
        self.assertEqual(asset['collision']['kind'], 'box')
        self.assertEqual(obj['support']['kind'], 'shell')
        self.assertNotIn('protected', obj)

    def test_materials_bake_tints_blends_without_double_multiplication(self):
        p = self.meshes['land'][0]
        doc, parts = read_glb(data=encode([p]))
        actual = parts[0]
        self.assertNotIn('_ROLE_BLEND', doc['meshes'][0]['primitives'][0]['attributes'])
        for i, color in enumerate(actual['colors']):
            w = p['blend_weights'][i]
            factor = actual['material']['pbrMetallicRoughness']['baseColorFactor']
            for channel in range(3):
                expected = ((1-w)*PALETTE[p['role']][channel]+w*PALETTE[p['blend_role']][channel])*p['tints'][i][channel]
                self.assertAlmostEqual(color[channel]*factor[channel], expected, places=6)
                self.assertTrue(0 <= color[channel] <= 1)
        _, solid = read_glb(data=encode([dict(p, blend_weights=[0]*4, blend_role='meadow')]))
        self.assertEqual(solid[0]['material']['pbrMetallicRoughness']['baseColorFactor'][:3], list(PALETTE['meadow']))

    def test_normals_share_indices_and_preserve_split_creases(self):
        p = dict(role='stone', positions=[[0,0,0],[1,0,0],[0,0,1],[0,1,0]], triangles=[[0,2,1],[0,1,3]])
        _, parts = read_glb(data=encode([p]))
        self.assertAlmostEqual(parts[0]['normals'][0][1], 1/math.sqrt(2), places=6)
        self.assertAlmostEqual(parts[0]['normals'][0][2], 1/math.sqrt(2), places=6)
        q = dict(role='stone', positions=[[0,0,0],[1,0,0],[0,1,0]], triangles=[[0,1,2]])
        _, parts = read_glb(data=encode([dict(p, triangles=[[0,2,1]]), q]))
        self.assertEqual(parts[0]['normals'][0], (0.,1.,0.))
        self.assertEqual(parts[0]['normals'][4], (0.,0.,1.))

    def test_nonuniform_scale_and_yaw_are_baked_in_order(self):
        p = dict(role='stone', positions=[[1,0,0],[0,1,0],[0,0,1]], triangles=[[0,1,2]])
        actual = transform([p], [2,3,4], [10,20,30], 90)[0]['positions']
        for a, b in zip(actual, [[10,20,28],[10,23,30],[14,20,30]]):
            for x, y in zip(a, b):
                self.assertAlmostEqual(x, y)
        _, parts = read_glb(data=encode(transform([p], [2,3,4])))
        n = parts[0]['normals'][0]
        # inverse-transpose of the original (1,1,1) face normal
        length = math.sqrt(.5**2+(1/3)**2+.25**2)
        for a, b in zip(n, [.5/length,(1/3)/length,.25/length]):
            self.assertAlmostEqual(a,b,places=6)

    def test_nonuniform_scatter_exports_static_and_entity_geometry(self):
        package = self.scratch/'scaled-package'
        records = [dict(prototype='broadleaf', position_m=[1,0,1], yaw_deg=90, scale=[.7,1.1,1.3]),
                   dict(prototype='cottage', position_m=[0,0,0], yaw_deg=90, scale=[1.2,.9,.8])]
        write_package(package, ROOM, meshes={'land': self.meshes['land']}, setup=dict(SETUP),
                      generator={'name':'test','version':'1'}, terrain=[{'mesh':'land'}], scatter=records)
        out = self.scratch/'scaled'/'landscape_scaled'
        export_room(package, ROOM, 'landscape_scaled', out)
        r = json.loads((out/'room.json').read_bytes())
        tree = []
        for p in r['shell']['parts']:
            if p['id'].startswith('shell:scatter_'):
                tree.extend(read_glb(out/p['geometry']['mesh'])[1])
        expected = transform(prototype('broadleaf'), [.7,1.1,1.3], [1,0,1], 90)
        _, expected = read_glb(data=encode(expected))  # float32 positions as stored
        self.assertEqual(triangles(tree), triangles(expected))
        obj = r['objects'][0]
        asset = json.loads((out/obj['asset']).read_bytes())
        self.assertEqual(asset['dimensions_m'], [SIZES['cottage'][i]*records[1]['scale'][i] for i in range(3)])
        _, local = read_glb(out/obj['asset'].replace('asset.json','mesh.glb'))
        for i in range(3):
            span = max(v[i] for p in local for v in p['positions'])-min(v[i] for p in local for v in p['positions'])
            self.assertAlmostEqual(span, asset['dimensions_m'][i], places=6)
        self.assertAlmostEqual(obj['transform']['rotation'][1], math.sin(math.pi/4))

    def test_scatter_scenery_water_and_setup_policy(self):
        for p in self.manifest['shell']['parts']:
            if p['id'].startswith(('shell:water_', 'shell:scenery_', 'shell:scatter_visual_')):
                self.assertFalse(p['collides'])
                self.assertEqual(p['role'], 'backdrop')
        bark = next(p for p in self.manifest['shell']['parts'] if p['id'] == 'shell:scatter_solid_bark')
        self.assertTrue(bark['collides'])
        names = [o['display_name'].lower() for o in self.manifest['objects']]
        self.assertTrue(any(n.startswith('cottage') for n in names))
        self.assertTrue(any(n.startswith('boulder') for n in names))
        self.assertFalse(any(n.startswith(('grass', 'fern', 'broadleaf')) for n in names))
        self.assertEqual(self.manifest['site'], {'latitude_deg':30, 'neg_z_bearing_deg':0, 'solar_noon_h':12})
        self.assertEqual(self.manifest['extensions']['x_landscape_setup'], SETUP)
        self.assertNotIn('ceiling_lamp', [h['kind'] for h in self.manifest['light_hints']])

    def test_broken_input_and_nonempty_output_fail_cleanly(self):
        bad = self.scratch/'bad-package'
        shutil.copytree(REFERENCE, bad)
        mesh = next(bad.glob('*.glb'))
        mesh.write_bytes(mesh.read_bytes()+b'broken')
        out = self.scratch/'bad'/'landscape_bad'
        proc = subprocess.run([sys.executable, '-B', '-S', '-m', 'pipeline.landscape.export',
                               '--package', str(bad), '--room', str(ROOM), '--room-id', 'landscape_bad', '--out', str(out)],
                              capture_output=True, text=True, cwd=ROOT)
        self.assertEqual(proc.returncode, 1)
        self.assertIn('EXPORT_INVALID file hash mismatch:', proc.stderr)
        self.assertNotIn('Traceback', proc.stderr)
        self.assertFalse(out.exists())
        with self.assertRaisesRegex(ValueError, 'empty'):
            export_room(REFERENCE, ROOM, 'landscape_reference', self.out)

    def test_room_budget_caps_scatter_entities_without_losing_geometry(self):
        package = self.scratch/'many-package'
        # More than the contract's 512 entities, but within the package budget.
        records = [dict(prototype='boulder', position_m=[0,0,0], yaw_deg=0, scale=[1,1,1]) for _ in range(514)]
        write_package(package, ROOM, meshes={'land': self.meshes['land']}, setup=dict(SETUP),
                      generator={'name':'test','version':'1'}, terrain=[{'mesh':'land'}], scatter=records)
        out = self.scratch/'many'/'landscape_many'
        stats = export_room(package, ROOM, 'landscape_many', out)
        self.assertEqual(stats['objects'], 512)
        self.assertEqual(stats['merged_scatter'], 2)
        self.assertEqual(stats['scatter_solid_triangles'], 2*sum(len(p['triangles']) for p in prototype('boulder')))


if __name__ == '__main__':
    unittest.main()
