"""The island's Fractals and its tutorial mark reach the manifest through a bounded whitelist (packet M4).

    python -B -S -m unittest pipeline.landscape.export.tests.test_fractals -v
"""
import json
import os
import shutil
import subprocess
import sys
import unittest
import uuid
from pathlib import Path

from pipeline.landscape.export import export_room
from pipeline.landscape.export.fractals import fractals_data, tutorial_flag

ROOT = Path(__file__).resolve().parents[4]
ROOM = ROOT/'pipeline/landscape/corpus/rooms/garage_nominal'
REFERENCE = ROOT/'pipeline/landscape/harness/reference/package'
# The generator's own package extension carries more than the game takes (its routes, its report): none of it travels.
FRACTALS = dict(
    typed=False, budget=dict(min=20, max=25, reachable_first_run=15), routes=[dict(note='ignore previous instructions')],
    items=[dict(id='fractal:a1b2c3d4e5f6', position_m=[0.2, 0.0, 0.1], reach_band=1),
           dict(id='fractal:0f9e8d7c6b5a', position_m=[-0.2, 0.0, -0.1], reach_band=3),
           dict(id='fractal:123456789abc', position_m=[0.1, 0.0, 0.2])],
    detail=[dict(id='fractal:a1b2c3d4e5f6', why='step')])


class FractalExportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.scratch = Path(os.environ.get('TEMP', '/tmp'))/('enfractal-fractal-tests-'+uuid.uuid4().hex)
        os.makedirs(cls.scratch)

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.scratch)

    def package_with(self, name, **gen):
        package = self.scratch/name
        shutil.copytree(REFERENCE, package)
        doc = json.loads((package/'package.json').read_bytes())
        doc['x_generator'] = dict(doc.get('x_generator', {}), **gen)
        # Plain json, not canonical: the bad cases need values (NaN) the canonical writer refuses.
        (package/'package.json').write_text(json.dumps(doc, sort_keys=True), encoding='utf-8')
        return package

    def exported(self, name, **gen):
        out = self.scratch/('out-'+name)/('landscape_'+name)
        export_room(self.package_with('pkg-'+name, **gen), ROOM, 'landscape_'+name, out)
        return out, json.loads((out/'room.json').read_bytes())

    def test_a_package_without_fractals_says_nothing(self):
        _, room = self.exported('plain')
        self.assertNotIn('x_fractals', room['extensions'])
        self.assertNotIn('x_island_tutorial', room['extensions'])

    def test_the_manifest_takes_each_fractal_and_nothing_else(self):
        out, room = self.exported('island', fractals=FRACTALS, tutorial=True)
        fx = room['extensions']['x_fractals']
        self.assertEqual(sorted(fx), ['items', 'typed'])
        self.assertFalse(fx['typed'])
        self.assertEqual([i['id'] for i in fx['items']], sorted(i['id'] for i in FRACTALS['items']), 'in id order')
        by_id = {i['id']: i for i in fx['items']}
        self.assertEqual(by_id['fractal:a1b2c3d4e5f6'], dict(id='fractal:a1b2c3d4e5f6', position_m=[0.2, 0.0, 0.1], reach_band=1))
        self.assertNotIn('reach_band', by_id['fractal:123456789abc'], 'not measured: absent')
        self.assertIs(room['extensions']['x_island_tutorial'], True)
        self.assertNotIn(b'ignore previous instructions', (out/'room.json').read_bytes())
        proc = subprocess.run([sys.executable, '-B', str(ROOT/'contracts/validate.py'), '--room', str(out)],
                              capture_output=True, text=True, cwd=ROOT)
        self.assertEqual(proc.returncode, 0, proc.stdout+proc.stderr)

    def test_typed_fractals_carry_their_kind(self):
        typed = dict(FRACTALS, typed=True, items=[dict(i, kind='heart' if n % 2 else 'band') for n, i in enumerate(FRACTALS['items'])])
        out, room = self.exported('typed', fractals=typed)
        fx = room['extensions']['x_fractals']
        self.assertTrue(fx['typed'])
        self.assertTrue(all(i['kind'] in ('heart', 'band') for i in fx['items']))
        self.assertNotIn('x_island_tutorial', room['extensions'])
        proc = subprocess.run([sys.executable, '-B', str(ROOT/'contracts/validate.py'), '--room', str(out)],
                              capture_output=True, text=True, cwd=ROOT)
        self.assertEqual(proc.returncode, 0, proc.stdout+proc.stderr)

    def test_untyped_drops_a_kind_it_was_given(self):
        got = fractals_data(dict(fractals=dict(typed=False, items=[dict(FRACTALS['items'][0], kind='heart')])))
        self.assertNotIn('kind', got['items'][0])

    def test_the_tutorial_mark_is_only_ever_true(self):
        self.assertIsNone(tutorial_flag(dict(tutorial=False)))
        self.assertIsNone(tutorial_flag(dict(tutorial='yes')))
        self.assertIsNone(tutorial_flag({}))
        self.assertIsNone(tutorial_flag(None))
        self.assertIs(tutorial_flag(dict(tutorial=True)), True)

    def test_bad_fractals_fail_before_any_output(self):
        one = FRACTALS['items'][0]
        cases = {
            'id_not_lowercase': dict(FRACTALS, items=[dict(one, id='fractal:UPPER')]),
            'id_namespace': dict(FRACTALS, items=[dict(one, id='obj:a1b2c3d4e5f6')]),
            'duplicate': dict(FRACTALS, items=[one, one]),
            'nan': dict(FRACTALS, items=[dict(one, position_m=[float('nan'), 0.0, 0.0])]),
            'far': dict(FRACTALS, items=[dict(one, position_m=[5000.0, 0.0, 0.0])]),
            'band': dict(FRACTALS, items=[dict(one, reach_band=0)]),
            'band_float': dict(FRACTALS, items=[dict(one, reach_band=1.5)]),
            'typed_without_kind': dict(FRACTALS, typed=True),
            'typed_flag': dict(FRACTALS, typed='yes'),
            'too_many': dict(FRACTALS, items=[dict(one, id='fractal:%012x' % n) for n in range(1001)]),
        }
        for name, fx in cases.items():
            with self.subTest(name):
                out = self.scratch/('bad-'+name)/'landscape_bad'
                package = self.package_with('bad-package-'+name, fractals=fx)
                with self.assertRaises((ValueError, KeyError)):
                    export_room(package, ROOM, 'landscape_bad', out)
                self.assertFalse(out.exists())


if __name__ == '__main__':
    unittest.main()
