"""Determinism and principle checks for the landscape generator.

    python -B -m unittest pipeline.landscape.generator.tests.test_generator -v
"""
import math
import os
import shutil
import tempfile
import unittest
from pathlib import Path

from pipeline.landscape.generator import checks
from pipeline.landscape.generator.generate import generate

ROOT = Path(__file__).resolve().parents[4]
ROOMS = ROOT/'pipeline'/'landscape'/'corpus'/'rooms'


def workdir(name):
    # Plain os.makedirs: tempfile.mkdtemp's owner-only ACL blocks sandboxed writes.
    path = os.path.join(tempfile.gettempdir(), 'landscape_generator_tests', name)
    shutil.rmtree(path, ignore_errors=True)
    os.makedirs(path)
    return path


_GARAGE = {}


def garage():
    """The garage's package, generated once for every test that only reads it."""
    if 'path' not in _GARAGE:
        _GARAGE['path'] = os.path.join(workdir('a'), 'pkg')
        generate(ROOMS/'garage_nominal', _GARAGE['path'])
    return _GARAGE['path']


def tearDownModule():
    shutil.rmtree(os.path.join(tempfile.gettempdir(), 'landscape_generator_tests'), ignore_errors=True)


def tree(folder):
    return {p.relative_to(folder).as_posix(): p.read_bytes() for p in sorted(Path(folder).rglob('*')) if p.is_file()}


class GarageLandscape(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.room = ROOMS/'garage_nominal'
        cls.a = garage()
        cls.b = os.path.join(workdir('b'), 'pkg')
        generate(cls.room, cls.b)
        cls.result = checks.run(cls.a, cls.room)

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

    def test_pond_deep_enough_to_swim_with_a_shore_to_walk_out(self):
        """The founder's playtest pond, judged from the written package: a deep
        middle well over the 10 cm head, a shelving shore a swimmer can walk
        out up (no step on the way steeper than 35 degrees from water over the
        head to the dry shore), and a drop-off somewhere (a face over 45)."""
        from pipeline.landscape.harness import read_package
        doc, meshes, _, _ = read_package(self.a, self.room)
        terrain = checks.Terrain(doc, meshes)
        tarn = meshes['tarn'][0]
        level = tarn['positions'][0][1]
        xs = [v[0] for v in tarn['positions']]
        zs = [v[2] for v in tarn['positions']]
        c = .025
        cells = {}
        for i in range(int(min(xs)/c)-2, int(max(xs)/c)+3):
            for j in range(int(min(zs)/c)-2, int(max(zs)/c)+3):
                cells[i, j] = level-terrain.height(i*c, j*c)
        deepest = max(cells.values())
        self.assertGreaterEqual(deepest, .15)
        self.assertLessEqual(deepest, .30)

        def grade(a, b):
            run = c*math.hypot(a[0]-b[0], a[1]-b[1])
            return math.degrees(math.atan(abs(cells[a]-cells[b])/run))
        steps = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)]
        seen = {k for k, d in cells.items() if d >= .11}
        todo = list(seen)
        shore = False
        while todo and not shore:
            k = todo.pop()
            for di, dj in steps:
                n = (k[0]+di, k[1]+dj)
                if n in cells and n not in seen and grade(k, n) <= 35:
                    if cells[n] <= 0:
                        shore = True
                        break
                    seen.add(n)
                    todo.append(n)
        self.assertTrue(shore, 'no shelving way out of the deep water')
        wet = [k for k, d in cells.items() if d > .02]
        steepest = max(grade(k, (k[0]+di, k[1]+dj)) for k in wet for di, dj in steps[:4] if (k[0]+di, k[1]+dj) in cells)
        self.assertGreater(steepest, 45)

    def test_island_in_the_sea(self):
        """The founder's island: a sea below the land all round, out past a reef
        a short swim off the coast; beaches a swimmer walks out of the sea up and
        on to the spawn; the door a jetty reachable on foot."""
        self.assertTrue(self.result['sea_ok'], {k: v for k, v in self.result['sea'].items()})
        from pipeline.landscape.harness import read_package
        doc, meshes, room, _ = read_package(self.a, self.room)
        sea = doc['x_generator']['sea']
        self.assertLess(sea['level_m'], 0)
        self.assertEqual({'mesh': 'sea', 'kind': 'still'}, next(r for r in doc['water'] if r['mesh'] == 'sea'))
        self.assertGreaterEqual(sea['reef']['offshore_m'][0], .2)
        self.assertLessEqual(sea['reef']['offshore_m'][1], .85)
        self.assertGreaterEqual(len(sea['beaches']), 2)
        self.assertEqual(sea['jetty']['door_id'], 'entry')
        self.assertIn({'mesh': 'jetty'}, doc['terrain'])
        # The playable water reaches past the room's walls on every side.
        lo, hi = room['bounds']['min_m'], room['bounds']['max_m']
        b = sea['play_area']['bounds_m']
        self.assertTrue(b['min_m'][0] < lo[0]-.5 and b['min_m'][2] < lo[2]-.5 and b['max_m'][0] > hi[0]+.5
                        and b['max_m'][2] > hi[2]+.5, b)
        self.assertLess(b['min_m'][1], sea['level_m']-.3)
        # A ragged coast: cliffs (faces over 45 degrees, climbable) and beaches (sand under 20) both meet the sea.
        terrain = checks.Terrain(doc, meshes)
        coast = sea['coast']['outline_m']
        cliffs = beaches = 0
        for x, z in coast:
            slopes = [terrain.hit(x+dx, z+dz)[1] for dx in (-.03, 0, .03) for dz in (-.03, 0, .03)]
            cliffs += max(slopes) > 45
            beaches += max(slopes) < 20
        self.assertGreater(cliffs, 5)
        self.assertGreater(beaches, 20)


class LShapedIsland(unittest.TestCase):
    """An L-shaped room becomes an L-shaped island: the notch is sea, both arms land."""

    def test_l_room_l_island(self):
        room = ROOMS/'awkward_l_nominal'
        out = os.path.join(workdir('l'), 'pkg')
        generate(room, out)
        result = checks.run(out, room)
        self.assertTrue(result['sea_ok'], result['sea'])
        from pipeline.landscape.harness import read_package
        doc, meshes, _, _ = read_package(out, room)
        terrain = checks.Terrain(doc, meshes)
        level = doc['x_generator']['sea']['level_m']
        self.assertLess(terrain.height(1.8, 1.8), level-.1)      # the notch
        self.assertGreater(terrain.height(-1.5, 1.5), level+.02)  # one arm
        self.assertGreater(terrain.height(1.5, -1.5), level+.02)  # the other


class CheckerCatchesContradictions(unittest.TestCase):
    """The checks are not vacuous: a broken stream or a floating cottage fails."""

    def test_uphill_stream_and_floating_cottage_fail(self):
        from pipeline.landscape.harness import read_package
        room = ROOMS/'garage_nominal'
        doc, meshes, _, _ = read_package(garage(), room)
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
        cottage['position_m'][1] -= .05
        # A boulder dropped on the crate leaves no 11 cm place to stand there.
        crate = doc['objects'][0]['position_m']
        doc['scatter'].append(dict(prototype='boulder', position_m=list(crate), yaw_deg=0, scale=[.4, .4, .4]))
        from pipeline.landscape.harness.common import load_json
        ok, walks = checks.walk_checks(doc, meshes, terrain, load_json(room/'room.json'))
        self.assertFalse(ok)
        self.assertFalse(walks[0]['found'])
        # A beach that drops off steeply, or a reef run up onto the coast, fails.
        sea = doc['x_generator']['sea']
        beach = sea['beaches'][0]
        beach['water_m'] = list(beach['wash_ashore_m'])
        ok, _ = checks.sea_checks(doc, meshes, terrain, load_json(room/'room.json'))
        self.assertFalse(ok)


class NoisyScanKeepsItsPlace(unittest.TestCase):
    """Scan 17: the desk is no longer swallowed (confidence limits spread) and
    the land gives way so the doors, the crate and the yards are reachable."""

    def test_scan_17_all_checks(self):
        room = ROOMS/'garage_scan_17'
        out = os.path.join(workdir('s17'), 'pkg')
        generate(room, out)
        result = checks.run(out, room)
        self.assertTrue(result['footprints_ok'], [r for r in result['footprints'] if not r['reads']])
        self.assertTrue(result['ok'], [w for w in result['walks'] if not w['found']])


class RoomsDiffer(unittest.TestCase):
    """Not every room gets a tarn: the room's own floor suggests a tarn, a
    river with deep pools or a dry upland (no generation; the choice only)."""

    def test_water_follows_the_room(self):
        from pipeline.landscape.generator.land import parse_objects
        from pipeline.landscape.generator.water import water_character
        from pipeline.landscape.harness.common import load_json
        setup = {'water': 'some'}
        got = {}
        for name in ('garage_nominal', 'bedroom_nominal', 'workshop_nominal', 'near_empty_nominal'):
            room = load_json(ROOMS/name/'room.json')
            objects = parse_objects(load_json(ROOMS/name/'inventory.json'))
            got[name] = water_character(room, objects, [s['position_m'] for s in room['spawns']], setup)[0]
        self.assertEqual(got, {'garage_nominal': 'tarn', 'bedroom_nominal': 'dry', 'workshop_nominal': 'river',
                               'near_empty_nominal': 'tarn'})


class StreamCrossing(unittest.TestCase):
    """A path that must cross a stream does so on a level footbridge that the
    walk check uses as ground (near_empty_nominal's hamlet lies across water)."""

    def test_footbridge_carries_the_walk(self):
        room = ROOMS/'near_empty_nominal'
        out = os.path.join(workdir('ne'), 'pkg')
        doc = generate(room, out)
        self.assertTrue(any(s['prototype'] == 'footbridge' for s in doc['scatter']))
        result = checks.run(out, room)
        self.assertTrue(result['ok'], [w for w in result['walks'] if not w['found']])


if __name__ == '__main__':
    unittest.main()
