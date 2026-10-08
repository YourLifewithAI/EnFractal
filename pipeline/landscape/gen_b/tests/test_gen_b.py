"""Determinism and principle checks for landscape generator B.

    python -B -m unittest pipeline.landscape.gen_b.tests.test_gen_b -v
"""
import os
import shutil
import tempfile
import unittest
from pathlib import Path

from pipeline.landscape.gen_b import checks
from pipeline.landscape.gen_b.generate import generate

ROOT = Path(__file__).resolve().parents[4]
ROOMS = ROOT/'pipeline'/'landscape'/'corpus'/'rooms'


def workdir(name):
    # Plain os.makedirs: tempfile.mkdtemp's owner-only ACL blocks sandboxed writes.
    path = os.path.join(tempfile.gettempdir(), 'gen_b_tests', name)
    shutil.rmtree(path, ignore_errors=True)
    os.makedirs(path)
    return path


def tree(folder):
    return {p.relative_to(folder).as_posix(): p.read_bytes() for p in sorted(Path(folder).rglob('*')) if p.is_file()}


class GarageLandscape(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.room = ROOMS/'garage_nominal'
        cls.a = os.path.join(workdir('a'), 'pkg')
        cls.b = os.path.join(workdir('b'), 'pkg')
        generate(cls.room, cls.a)
        generate(cls.room, cls.b)
        cls.result = checks.run(cls.a, cls.room)

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(os.path.join(tempfile.gettempdir(), 'gen_b_tests'), ignore_errors=True)

    def test_identical_bytes(self):
        first, second = tree(self.a), tree(self.b)
        self.assertEqual(sorted(first), sorted(second))
        for name in first:
            self.assertEqual(first[name], second[name], name)

    def test_water_level_and_downhill(self):
        self.assertTrue(self.result['water_ok'], self.result['water'])
        kinds = {w['kind'] for w in self.result['water']}
        self.assertIn('still', kinds)
        self.assertIn('flowing', kinds)

    def test_nothing_floating_or_sunk(self):
        self.assertTrue(self.result['grounded_ok'], [g for g in self.result['grounding'] if not g['ok']][:5])

    def test_walk_to_object_and_back(self):
        self.assertTrue(self.result['walk_ok'], self.result['walks'])
        for w in self.result['walks']:
            self.assertLessEqual(w['max_face_slope_deg'], checks.WALK_DEG)
            self.assertLessEqual(w['max_step_m'], checks.STEP_M)

    def test_every_footprint_reads(self):
        self.assertTrue(self.result['footprints_ok'], [f for f in self.result['footprints'] if not f['reads']])
        self.assertEqual(len(self.result['footprints']), 16)


class CheckerCatchesContradictions(unittest.TestCase):
    """The checks are not vacuous: a broken stream or a floating cottage fails."""

    def test_uphill_stream_and_floating_cottage_fail(self):
        from pipeline.landscape.harness import read_package
        room = ROOMS/'garage_nominal'
        src = os.path.join(workdir('m'), 'pkg')
        generate(room, src)
        doc, meshes, _, _ = read_package(src, room)
        river = next(r['mesh'] for r in doc['water'] if r['kind'] == 'flowing')
        terrain = checks.Terrain(doc, meshes)
        pos = meshes[river][0]['positions']
        pos[-1][1] += .05
        pos[-2][1] += .05
        ok, _, _ = checks.water_checks(doc, meshes, terrain)
        self.assertFalse(ok)
        cottage = next(s for s in doc['scatter'] if s['prototype'] == 'cottage')
        cottage['position_m'][1] += .05
        ok, _ = checks.grounding_checks(doc, terrain)
        self.assertFalse(ok)


if __name__ == '__main__':
    unittest.main()
