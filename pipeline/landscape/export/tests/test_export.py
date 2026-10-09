import collections
import hashlib
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

from pipeline.landscape.harness.common import PALETTE, SETUP, canonical
from pipeline.landscape.harness.kit import SIZES, prototype
from pipeline.landscape.harness.package import read_package, write_package
from pipeline.landscape.export import export_room
from pipeline.landscape.export.mesh import encode, ground, transform
from pipeline.landscape.export.mesh import cross
from pipeline.landscape.export.trees import TerrainHeights, climbing_parts, is_tree, WALK_CLEARANCE_M

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


def components(parts):
    """Independent connected components of the exported indexed geometry."""
    result = []
    for p in parts:
        groups = []
        for face in p['triangles']:
            touching = [g for g in groups if set(face) & g]
            merged = set(face).union(*touching)
            groups = [g for g in groups if g not in touching]+[merged]
        for group in groups:
            indices = sorted(group)
            remap = {old: new for new, old in enumerate(indices)}
            result.append(dict(role=p['role'], positions=[p['positions'][i] for i in indices],
                               triangles=[[remap[i] for i in t] for t in p['triangles'] if t[0] in group]))
    return result


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

    def test_reference_source_room_preserves_every_box_wall_and_opening(self):
        room = json.loads((ROOM/'room.json').read_bytes())
        inventory = json.loads((ROOM/'inventory.json').read_bytes())
        source = self.manifest['extensions']['x_landscape_source']
        self.assertEqual(source['room_id'], room['room_id'])
        for name in ('room', 'inventory'):
            self.assertEqual(source[name+'_sha256'], hashlib.sha256((ROOM/(name+'.json')).read_bytes()).hexdigest())
        self.assertEqual(source['bounds'], room['bounds'])
        self.assertEqual(len(source['objects']), len(inventory['objects']))
        self.assertEqual({o['id'] for o in source['objects']}, {o['id'] for o in inventory['objects']})
        parts = {p['id']: p for p in source['shell']['parts']}
        walls = [p for p in room['shell']['parts'] if p['role'] == 'wall']
        self.assertEqual(sum(p['role'] == 'wall' for p in parts.values()), len(walls))
        for part in room['shell']['parts']:
            self.assertEqual(parts[part['id']]['geometry'], part['geometry'])
            if part['role'] == 'wall':
                ys = [p[1] for p in part['geometry']['points_m']]
                self.assertEqual(parts[part['id']]['height_m'], max(ys)-min(ys))
        self.assertEqual(source['shell']['openings'],
                         sorted([{k: o[k] for k in ('id', 'kind', 'host_part_id', 'center_m', 'size_m')}
                                 for o in room['shell']['openings']], key=lambda o: o['id']))
        boxes = {o['id']: o for o in source['objects']}
        for obj in inventory['objects']:
            actual = boxes[obj['id']]
            self.assertEqual(actual['kind'], obj['kind'])
            self.assertEqual(actual['kind_confidence'], obj['confidence'])
            self.assertEqual(actual['size_m'], obj['box']['size_m'])
            for key in ('position_m', 'rotation', 'yaw_deg', 'support'):
                self.assertEqual(actual[key], obj['placement'][key])
            self.assertEqual(actual['colours'], sorted(obj['colours'], key=lambda c: (-c['share'], c['hex'])))

    def source_fixture(self, name, room, inventory):
        source = self.scratch/(name+'-source')
        os.makedirs(source)
        (source/'room.json').write_bytes(canonical(room))
        (source/'inventory.json').write_bytes(canonical(inventory))
        package = self.scratch/(name+'-package')
        write_package(package, source, meshes={'land': self.meshes['land']}, setup=dict(SETUP),
                      generator={'name':'test','version':'1'}, terrain=[{'mesh':'land'}])
        return source, package, self.scratch/('source_'+name)

    def test_source_labels_and_display_names_never_appear_in_any_output_file(self):
        room = json.loads((ROOM/'room.json').read_bytes())
        inventory = json.loads((ROOM/'inventory.json').read_bytes())
        forbidden = []

        def collect(value):
            if isinstance(value, dict):
                for key, child in value.items():
                    if key in {'label', 'labels', 'display_name', 'display-name'}:
                        if isinstance(child, str):
                            forbidden.append(child)
                        elif isinstance(child, list):
                            forbidden.extend(v for v in child if isinstance(v, str))
                    collect(child)
            elif isinstance(value, list):
                for child in value:
                    collect(child)

        collect(room)
        collect(inventory)
        for path in self.out.rglob('*'):
            if path.is_file():
                for label in forbidden:
                    self.assertNotIn(label.encode('utf-8'), path.read_bytes(), str(path))
        # Put unique free text at every dictionary depth, including geometry,
        # colour and support records. Re-pin the package through its real writer.
        def inject(value):
            if isinstance(value, dict):
                for child in list(value.values()):
                    inject(child)
                marker = 'UNTRUSTED_SOURCE_TEXT_'+str(len(forbidden))
                value['label'] = marker
                value['display_name'] = marker+' DISPLAY'
                value['notes'] = [marker+' NOTES']
                forbidden.extend([marker, marker+' DISPLAY', marker+' NOTES'])
            elif isinstance(value, list):
                for child in value:
                    inject(child)

        inject(room)
        inject(inventory)
        source, package, out = self.source_fixture('labels', room, inventory)
        export_room(package, source, out.name, out)
        for path in out.rglob('*'):
            if path.is_file():
                data = path.read_bytes()
                for label in forbidden:
                    self.assertNotIn(label.encode('utf-8'), data, str(path))

    def test_source_records_are_sorted_without_reordering_polygon_points(self):
        room = json.loads((ROOM/'room.json').read_bytes())
        inventory = json.loads((ROOM/'inventory.json').read_bytes())
        room['shell']['parts'].reverse()
        room['shell']['openings'].reverse()
        inventory['objects'].reverse()
        inventory['objects'][0]['colours'] = [{'hex':'#eeeeee', 'share':.2},
                                            {'hex':'#bbbbbb', 'share':.4}, {'hex':'#aaaaaa', 'share':.4}]
        source, package, out = self.source_fixture('sorting', room, inventory)
        export_room(package, source, out.name, out)
        intro = json.loads((out/'room.json').read_bytes())['extensions']['x_landscape_source']
        for values in (intro['objects'], intro['shell']['parts'], intro['shell']['openings']):
            self.assertEqual([v['id'] for v in values], sorted(v['id'] for v in values))
        box = next(o for o in intro['objects'] if o['id'] == inventory['objects'][0]['id'])
        self.assertEqual([c['hex'] for c in box['colours']], ['#aaaaaa', '#bbbbbb', '#eeeeee'])
        floor = next(p for p in intro['shell']['parts'] if p['role'] == 'floor')
        original = next(p for p in room['shell']['parts'] if p['role'] == 'floor')
        self.assertEqual(floor['geometry']['points_m'], original['geometry']['points_m'])

    def test_source_scan_pose_alternatives_and_unknown_surface_support(self):
        room = json.loads((ROOM/'room.json').read_bytes())
        inventory = json.loads((ROOM/'inventory.json').read_bytes())
        a, b = inventory['objects'][:2]
        del a['placement']['rotation']
        a['placement']['yaw_deg'] = 37
        a['placement']['support'] = {'kind':'surface', 'height_m':.4, 'target_id':None}
        del b['placement']['yaw_deg']
        del b['box']['yaw_deg']
        b['placement']['rotation'] = [0,0,1,0]  # preserve supplied full rotation
        source, package, out = self.source_fixture('poses', room, inventory)
        export_room(package, source, out.name, out)
        boxes = {o['id']: o for o in json.loads((out/'room.json').read_bytes())['extensions']['x_landscape_source']['objects']}
        self.assertNotIn('rotation', boxes[a['id']])
        self.assertEqual(boxes[a['id']]['yaw_deg'], 37)
        self.assertEqual(boxes[a['id']]['support'], a['placement']['support'])
        self.assertNotIn('yaw_deg', boxes[b['id']])
        self.assertEqual(boxes[b['id']]['rotation'], [0,0,1,0])

    def test_source_invalid_numbers_fail_before_output_creation(self):
        for field, value in (('confidence', 1.1), ('position', 1001), ('size', 0),
                             ('yaw', 361), ('rotation', 2), ('share', -.1), ('height', 1001),
                             ('confidence', float('nan')), ('position', float('inf'))):
            with self.subTest(field=field, value=value):
                room = json.loads((ROOM/'room.json').read_bytes())
                inventory = json.loads((ROOM/'inventory.json').read_bytes())
                obj = inventory['objects'][0]
                if field == 'confidence': obj['confidence'] = value
                elif field == 'position': obj['placement']['position_m'][0] = value
                elif field == 'size': obj['box']['size_m'][0] = value
                elif field == 'yaw': obj['placement']['yaw_deg'] = value
                elif field == 'rotation': obj['placement']['rotation'][3] = value
                elif field == 'share': obj['colours'][0]['share'] = value
                elif field == 'height': obj['placement']['support']['height_m'] = value
                # NaN/Infinity are deliberately malformed input, not pinned output.
                source = self.scratch/('invalid-source-'+uuid.uuid4().hex)
                os.makedirs(source)
                (source/'room.json').write_bytes(canonical(room))
                (source/'inventory.json').write_text(json.dumps(inventory), encoding='utf-8', newline='\n')
                package = self.scratch/('invalid-source-package-'+uuid.uuid4().hex)
                write_package(package, source, meshes={'land': self.meshes['land']}, setup=dict(SETUP),
                              generator={'name':'test','version':'1'}, terrain=[{'mesh':'land'}])
                out = self.scratch/('source_invalid_'+uuid.uuid4().hex)
                with self.assertRaises(ValueError):
                    export_room(package, source, out.name, out)
                self.assertFalse(out.exists())

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

    def test_spawns_sample_terrain_and_keep_source_xz(self):
        terrain = [p for r in self.package['terrain'] for p in self.meshes[r['mesh']]]
        source = json.loads((ROOM/'room.json').read_bytes())
        for a, b in zip(source['spawns'], self.manifest['spawns']):
            self.assertEqual([a['position_m'][0], a['position_m'][2]],
                             [b['position_m'][0], b['position_m'][2]])
            self.assertAlmostEqual(b['position_m'][1], ground(terrain, b['position_m'][0], b['position_m'][2]))

    def assert_faces(self, spawn, target):
        dx, dz = target[0]-spawn['position_m'][0], target[2]-spawn['position_m'][2]
        yaw = math.radians(spawn['yaw_deg'])
        forward = (-math.sin(yaw), -math.cos(yaw))
        error = math.degrees(math.atan2(abs(forward[0]*dz-forward[1]*dx),
                                       forward[0]*dx+forward[1]*dz))
        self.assertLessEqual(error, .5)

    def test_reference_spawns_face_nearest_promised_destination(self):
        # The reference's movable crate is closer than its cottage and tower.
        target = self.package['objects'][0]['position_m']
        for spawn in self.manifest['spawns']:
            self.assert_faces(spawn, target)

    def test_spawns_choose_by_type_and_horizontal_distance_individually(self):
        for mode in ('movable', 'populated_cottage', 'scatter_cottage', 'scatter_tower'):
            with self.subTest(mode=mode):
                # Fixed crates/boulders are closer, but promise neither carrying nor doors.
                fixed = dict(id='nearby_decoration', kind='container', prototype='crate',
                             position_m=[0,0,-3], yaw_deg=0, size_m=[.1,.1,.1],
                             mass_kg=1, carriable=False)
                objects = [fixed]
                scatter = [dict(prototype='boulder', position_m=[1,0,-3],
                                yaw_deg=0, scale=[1,1,1])]
                targets = [[-1,1.85,-2.5], [2,0,-2.5]]
                name = 'tower' if mode == 'scatter_tower' else 'cottage'
                for i, target in enumerate(targets):
                    if mode.startswith('scatter_'):
                        scatter.append(dict(prototype=name, position_m=target,
                                            yaw_deg=0, scale=[1,1,1]))
                    else:
                        objects.append(dict(id='destination_'+str(i), kind='decor',
                                            prototype='crate' if mode == 'movable' else name,
                                            position_m=target, yaw_deg=0, size_m=[.1,.1,.1],
                                            mass_kg=.1, carriable=mode == 'movable'))
                package = self.scratch/('heading-package-'+mode)
                write_package(package, ROOM, meshes={'land': self.meshes['land']}, setup=dict(SETUP),
                              generator={'name':'test','version':'1'}, terrain=[{'mesh':'land'}],
                              objects=objects, scatter=scatter)
                room_id = 'heading_'+mode
                out = self.scratch/room_id
                export_room(package, ROOM, room_id, out)
                spawns = json.loads((out/'room.json').read_bytes())['spawns']
                for spawn, target in zip(spawns, targets):
                    self.assert_faces(spawn, target)

    def test_no_promised_destination_keeps_source_yaw(self):
        source = self.scratch/'yaw-source'
        os.makedirs(source)
        room = json.loads((ROOM/'room.json').read_bytes())
        for spawn, yaw in zip(room['spawns'], [-37,123]):
            spawn['yaw_deg'] = yaw
        (source/'room.json').write_text(json.dumps(room)+'\n', encoding='utf-8', newline='\n')
        (source/'inventory.json').write_bytes((ROOM/'inventory.json').read_bytes())
        package = self.scratch/'no-destination-package'
        # Both populated and scatter records exist; neither is a promised destination.
        write_package(package, source, meshes={'land': self.meshes['land']}, setup=dict(SETUP),
                      generator={'name':'test','version':'1'}, terrain=[{'mesh':'land'}],
                      objects=[dict(id='fixed_crate', kind='container', prototype='crate',
                                    position_m=[0,0,0], yaw_deg=0, size_m=[.1,.1,.1],
                                    mass_kg=1, carriable=False)],
                      scatter=[dict(prototype='boulder', position_m=[1,0,0], yaw_deg=0, scale=[1,1,1])])
        out = self.scratch/'heading_no_destination'
        export_room(package, source, out.name, out)
        spawns = json.loads((out/'room.json').read_bytes())['spawns']
        self.assertEqual([s['yaw_deg'] for s in spawns], [-37,123])

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

    def tree_meshes(self):
        result = []
        for part in self.manifest['shell']['parts']:
            if part['id'].startswith('shell:tree_climb_'):
                self.assertEqual(part['role'], 'ground')
                self.assertTrue(part['collides'])
                self.assertIs(part['drawn'], False)
                result.extend(read_glb(self.out/part['geometry']['mesh'])[1])
        return result

    def test_every_reference_tree_has_a_continuous_pole_and_top_perch(self):
        exported = components(self.tree_meshes())
        poles = [p for p in exported if p['role'] == 'bark']
        caps = [p for p in exported if p['role'] == 'foliage']
        records = [r for r in self.package['scatter'] if r['prototype'] in ('broadleaf', 'conifer')]
        self.assertEqual(len(poles), len(records))
        self.assertEqual(self.stats['tree_count'], len(records))
        self.assertEqual(len(caps), 6)  # Five broadleaf lobes; highest conifer tier.
        for record in records:
            with self.subTest(tree=record['prototype']):
                original = transform(prototype(record['prototype']), record['scale'],
                                     record['position_m'], record['yaw_deg'])
                bark = next(p for p in original if p['role'] == 'bark')
                trunk_top = max(v[1] for v in bark['positions'])
                radius = max(math.hypot(v[0]-record['position_m'][0], v[2]-record['position_m'][2])
                             for v in bark['positions'] if abs(v[1]-trunk_top) < 1e-8)
                x, _, z = record['position_m']
                pole = min(poles, key=lambda p: math.hypot(p['positions'][0][0]-x, p['positions'][0][2]-z))
                self.assertAlmostEqual(min(v[1] for v in pole['positions']), trunk_top-.01, places=6)
                crown_top = max(v[1] for p in original if p['role'] == 'foliage' for v in p['positions'])
                self.assertAlmostEqual(max(v[1] for v in pole['positions']), crown_top-.015, places=6)
                # Kit fitting can make a trunk elliptical; preserve its actual
                # top perimeter rather than assuming every radius is equal.
                top_vertices = [v for v in bark['positions'] if abs(v[1]-trunk_top) < 1e-8]
                tx = (min(v[0] for v in top_vertices)+max(v[0] for v in top_vertices))/2
                tz = (min(v[2] for v in top_vertices)+max(v[2] for v in top_vertices))/2
                expected_xz = {(round(v[0],6),round(v[2],6)) for v in bark['positions']
                               if abs(v[1]-trunk_top) < 1e-8 and math.hypot(v[0]-tx,v[2]-tz) > 1e-8}
                actual_xz = {(round(v[0],6),round(v[2],6)) for v in pole['positions']}
                self.assertEqual(actual_xz, expected_xz)
                self.assertAlmostEqual(ground(caps, x, z), crown_top-.01, places=6)
                # A flat perch supports the capsule beside the climbing pole.
                self.assertAlmostEqual(ground(caps, x+radius+.02, z), crown_top-.01, places=6)
        self.assertLessEqual(self.stats['shell_parts'], 128)
        self.assertLessEqual(self.stats['files']-1, 2048)

    def test_exported_caps_have_only_upward_outward_front_faces(self):
        for cap_part in components(self.tree_meshes()):
            if cap_part['role'] != 'foliage':
                continue
            vs = cap_part['positions']
            cx = (min(v[0] for v in vs)+max(v[0] for v in vs))/2
            cz = (min(v[2] for v in vs)+max(v[2] for v in vs))/2
            flat_faces = 0
            for face in cap_part['triangles']:
                a, b, c = [vs[i] for i in face]
                n = cross([b[i]-a[i] for i in range(3)], [c[i]-a[i] for i in range(3)])
                self.assertGreater(n[1], 0)  # glTF CCW front; no underside/bottom.
                radial = [(a[0]+b[0]+c[0])/3-cx, (a[2]+b[2]+c[2])/3-cz]
                self.assertGreaterEqual(n[0]*radial[0]+n[2]*radial[1], -1e-9)
                if abs(n[0])+abs(n[2]) < 1e-8:
                    flat_faces += 1
            self.assertGreater(flat_faces, 0)

    def test_exported_caps_clear_walkers_over_reference_terrain(self):
        terrain = [p for r in self.package['terrain'] for p in self.meshes[r['mesh']]]
        for part in self.tree_meshes():
            if part['role'] != 'foliage':
                continue
            for face in part['triangles']:
                points = [part['positions'][i] for i in face]
                points += [[sum(v[i] for v in points)/3 for i in range(3)]]
                for x, y, z in points:
                    floor = ground(terrain, x, z)
                    if floor is not None:
                        self.assertGreaterEqual(y-floor, WALK_CLEARANCE_M-1e-6)

    def test_sloped_terrain_clearance_uses_the_whole_crown_not_trunk_sample(self):
        terrain = [dict(role='stone', positions=[[-1,0,-1],[1,.55,-1],[1,.55,1],[-1,0,1]],
                        triangles=[[0,2,1],[0,3,2]])]
        # Also test rotation and nonuniform scale with a tree rooted above ground.
        tree = transform(prototype('broadleaf'), [.8,1.1,1.3], [0,.28,0], 37)
        parts = climbing_parts('broadleaf', tree, TerrainHeights(terrain))
        for cap_part in parts[1:]:
            x0 = min(v[0] for v in cap_part['positions'])
            x1 = max(v[0] for v in cap_part['positions'])
            z0 = min(v[2] for v in cap_part['positions'])
            z1 = max(v[2] for v in cap_part['positions'])
            # Independent analytic maximum of this planar slope's footprint.
            floor_max = max(ground(terrain, x, z) for x in (x0,x1) for z in (z0,z1))
            self.assertGreaterEqual(min(v[1] for v in cap_part['positions'])-floor_max,
                                    WALK_CLEARANCE_M-1e-8)

    def test_terrain_rectangle_query_includes_an_interior_peak(self):
        terrain = [dict(role='stone', positions=[[-1,0,-1],[1,0,-1],[1,0,1],[-1,0,1],[.1,.64,0]],
                        triangles=[[0,1,4],[1,2,4],[2,3,4],[3,0,4]])]
        heights = TerrainHeights(terrain)
        self.assertAlmostEqual(heights.maximum((-.2,.2,-.2,.2)), .64)
        parts = climbing_parts('broadleaf', prototype('broadleaf'), heights)
        for p in parts[1:]:
            # Each cap stays above the terrain at all vertices and centroids.
            for face in p['triangles']:
                vs = [p['positions'][i] for i in face]
                for v in vs+[[sum(v[i] for v in vs)/3 for i in range(3)]]:
                    self.assertGreaterEqual(v[1]-ground(terrain,v[0],v[2]), .12-1e-8)

    def test_shrubs_grass_ferns_and_flowers_get_no_climbing_geometry(self):
        for name in ('shrub','grass_tuft','fern','flower_clump','rock','boulder'):
            self.assertFalse(is_tree(name, prototype(name)))
            self.assertEqual(climbing_parts(name, prototype(name), None), [])

    def test_only_decorative_plants_export_no_tree_shell(self):
        package = self.scratch/'plants-package'
        records = [dict(prototype=name, position_m=[0,0,0], yaw_deg=0, scale=[1,1,1])
                   for name in ('shrub','grass_tuft','fern','flower_clump')]
        write_package(package, ROOM, meshes={'land': self.meshes['land']}, setup=dict(SETUP),
                      generator={'name':'test','version':'1'}, terrain=[{'mesh':'land'}], scatter=records)
        out = self.scratch/'landscape_plants'
        stats = export_room(package, ROOM, out.name, out)
        room = json.loads((out/'room.json').read_bytes())
        self.assertEqual(stats['tree_count'], 0)
        self.assertEqual(stats['tree_climb_triangles'], 0)
        self.assertFalse(any(p['id'].startswith('shell:tree_climb_') for p in room['shell']['parts']))

    def test_fixed_populated_tree_uses_hidden_trunk_instead_of_canopy_box(self):
        package = self.scratch/'populated-tree-package'
        write_package(package, ROOM, meshes={'land': self.meshes['land']}, setup=dict(SETUP),
                      generator={'name':'test','version':'1'}, terrain=[{'mesh':'land'}],
                      objects=[dict(id='tree',kind='decor',prototype='conifer',position_m=[0,0,0],
                                    yaw_deg=45,size_m=SIZES['conifer'],mass_kg=100,carriable=False)])
        out = self.scratch/'landscape_populated_tree'
        stats = export_room(package, ROOM, out.name, out)
        room = json.loads((out/'room.json').read_bytes())
        asset = json.loads((out/room['objects'][0]['asset']).read_bytes())
        self.assertEqual(stats['tree_count'], 1)
        self.assertEqual(asset['collision']['kind'], 'none')
        self.assertFalse(asset['physics']['movable'])
        self.assertEqual(len([p for p in room['shell']['parts'] if p['id'].startswith('shell:tree_climb_')]), 2)


if __name__ == '__main__':
    unittest.main()
