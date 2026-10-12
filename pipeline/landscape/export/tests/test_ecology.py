import json
import struct
import unittest
from pipeline.landscape.export.ecology import tag, bind
from pipeline.landscape.export.mesh import encode
from pipeline.landscape.harness.kit import box


class EcologyExportTests(unittest.TestCase):
    def test_flower_heads_keep_their_material_and_plant_identity(self):
        from pipeline.landscape.generator.life import prototypes
        for species, head_role in [('daisies', 'snow'), ('buttercups', 'gravel')]:
            parts, _ = prototypes()[species]
            records = []
            tag(parts, 'plant_s0001', species, records, [0, 0, 0])
            self.assertEqual({p['role'] for p in records}, {'foliage', head_role})
            self.assertTrue(all(p.get('ecology_id') for p in parts))
            self.assertTrue(all(p['species'] == species for p in records))
        gravel = [box([.1, .1, .1], [0, .05, 0], 'gravel')]
        records = []
        tag(gravel, 'plant_s0002', 'rock', records, [0, 0, 0])
        self.assertEqual(records, [])

    def test_species_are_not_collapsed_and_rock_is_not_fuel(self):
        records = []
        for index, species in enumerate(('daisies', 'buttercups', 'reeds', 'broadleaf')):
            parts = [box([.1, .1, .1], [0, .05, 0], 'foliage'),
                     box([.1, .1, .1], [0, .05, 0], 'rock')]
            tag(parts, f'plant_s{index:04d}', species, records, [0, 0, 0])
            bind(parts, 'shell:scatter_visual_foliage', records)
            self.assertNotIn('ecology_id', parts[1])
        self.assertEqual({r['species'] for r in records}, {'daisies', 'buttercups', 'reeds', 'broadleaf'})
        self.assertEqual(len({r['id'] for r in records}), 4)
        self.assertTrue(all(r['target'] == 'shell:scatter_visual_foliage' for r in records))

    def test_render_identities_preserve_all_geometry(self):
        parts = [box([.1, .2, .1], [0, .1, 0], 'foliage'),
                 box([.1, .2, .1], [.5, .1, 0], 'foliage')]
        records = []
        for i, p in enumerate(parts):
            tag([p], f'plant_s{i:04d}', 'shrub', records, [i*.5, 0, 0])
        data = encode(parts)
        size = struct.unpack_from('<I', data, 12)[0]
        doc = json.loads(data[20:20+size])
        self.assertEqual({n['name'] for n in doc['nodes'] if 'name' in n},
                         {'eco_plant_s0000_foliage', 'eco_plant_s0001_foliage'})
        primitives = [p for m in doc['meshes'] for p in m['primitives']]
        self.assertEqual(sum(doc['accessors'][p['indices']]['count'] for p in primitives),
                         sum(len(p['triangles'])*3 for p in parts))
        self.assertTrue(all(m['primitives'] for m in doc['meshes']))


if __name__ == '__main__':
    unittest.main()
