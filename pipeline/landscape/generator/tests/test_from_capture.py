"""A captured shell and inventory become a generator room (synthetic inputs only: the corpus's invented garage).

    python -B -m unittest pipeline.landscape.generator.tests.test_from_capture -v
"""
import copy
import math
import os
import shutil
import tempfile
import unittest
import uuid
from pathlib import Path

from pipeline.landscape.generator import from_capture as fc
from pipeline.landscape.harness.common import load_json

ROOT = Path(__file__).resolve().parents[4]
GARAGE = ROOT/'pipeline'/'landscape'/'corpus'/'rooms'/'garage_nominal'


def shell_and_inventory():
    return load_json(GARAGE/'room.json'), load_json(GARAGE/'inventory.json')


class CaptureRoom(unittest.TestCase):
    def setUp(self):
        # os.makedirs with a unique name, as the generator's tests do (mkdtemp's owner-only ACL on Windows).
        self.out = Path(tempfile.gettempdir())/('landscape_from_capture-%d-%s' % (os.getpid(), uuid.uuid4().hex[:12]))
        os.makedirs(self.out)
        self.addCleanup(shutil.rmtree, self.out, True)

    def test_bearing_from_the_wall_that_faces_a_direction(self):
        shell, _ = shell_and_inventory()
        # Walls A at -Z, B at +X, C at +Z, D at -X. The A/B setup's note: the window on wall B faces east with -Z north.
        self.assertEqual(fc.neg_z_bearing(shell, 'B', 90), 0)
        self.assertEqual(fc.neg_z_bearing(shell, 'A', 90), 90)
        self.assertEqual(fc.neg_z_bearing(shell, 'A', 0), 0)
        self.assertEqual(fc.neg_z_bearing(shell, 'C', 90), 270)
        self.assertEqual(fc.neg_z_bearing(shell, 'D', 0), 90)
        self.assertEqual(fc.neg_z_bearing(shell, 'shell:wall_b', 135), 45)
        with self.assertRaises(ValueError):
            fc.neg_z_bearing(shell, 'E', 90)

    def test_converted_room_passes_the_corpus_checks(self):
        shell, inventory = shell_and_inventory()
        room, inv, setup, notes = fc.convert(shell, inventory, 'captured_garage', 'A', 90)
        folder, setup_path = fc.write(self.out, room, inv, setup)
        self.assertEqual(fc.problems(folder, setup_path), [])
        self.assertEqual(sorted(p.name for p in self.out.iterdir()), ['captured_garage', 'captured_garage.setup.json'])
        self.assertEqual(sorted(p.name for p in folder.iterdir()), ['inventory.json', 'room.json'])
        # The shell is the capture's, every object is kept as it is; only the ids and the site change.
        self.assertEqual(room['shell'], shell['shell'])
        self.assertEqual(inv['objects'], inventory['objects'])
        self.assertEqual(inv['room'], 'captured_garage')
        self.assertEqual(room['spawns'], shell['spawns'])
        self.assertEqual(notes['moved_spawns'], [])
        # No site in the shell: the look's fallback numbers, with the bearing worked out.
        self.assertEqual(room['site'], {'latitude_deg': 30, 'neg_z_bearing_deg': 90, 'solar_noon_h': 12})
        self.assertEqual({k: v for k, v in setup.items() if k != 'neg_z_bearing_deg'},
                         {k: v for k, v in load_json(fc.SETUP_PATH).items() if k != 'neg_z_bearing_deg'})
        self.assertEqual(setup['neg_z_bearing_deg'], 90)

    def test_the_shells_own_site_numbers_are_kept(self):
        shell, inventory = shell_and_inventory()
        shell['site'] = {'latitude_deg': -12, 'neg_z_bearing_deg': 3, 'solar_noon_h': 12.25}
        room, _, setup, _ = fc.convert(shell, inventory, 'captured_garage', 'B', 90)
        self.assertEqual(room['site'], {'latitude_deg': -12, 'neg_z_bearing_deg': 0, 'solar_noon_h': 12.25})
        self.assertEqual((setup['latitude_deg'], setup['neg_z_bearing_deg']), (-12, 0))

    def test_spawns_move_off_what_becomes_land(self):
        shell, inventory = shell_and_inventory()
        player = next(s for s in shell['spawns'] if s['role'] == 'player')
        x, _, z = player['position_m']
        # A box resting on a surface the inventory does not hold rises from the plain, so the player moves off it too.
        thing = copy.deepcopy(next(o for o in inventory['objects'] if o['id'] == 'laptop_1'))
        thing['id'] = 'tray_1'
        thing['box']['centre_m'] = [x, .9, z]
        thing['box'].pop('footprint_m')
        thing['placement']['position_m'] = [x, .88, z]
        thing['placement']['support'] = {'kind': 'surface', 'height_m': .88, 'target_id': None}
        inventory['objects'].append(thing)
        room, inv, setup, notes = fc.convert(shell, inventory, 'captured_garage', 'A', 90)
        self.assertIn('player_start', notes['moved_spawns'])
        self.assertIn('tray_1', notes['on_unheld_surfaces'])
        moved = next(s for s in room['spawns'] if s['role'] == 'player')['position_m']
        self.assertGreater(math.dist((moved[0], moved[2]), (x, z)), fc.CLEAR_M)
        folder, setup_path = fc.write(self.out, room, inv, setup)
        self.assertEqual(fc.problems(folder, setup_path), [])

    def test_checks_catch_a_blocked_spawn_and_a_disagreeing_setup(self):
        shell, inventory = shell_and_inventory()
        room, inv, setup, _ = fc.convert(shell, inventory, 'captured_garage', 'A', 90)
        couch = next(o for o in inv['objects'] if o['id'] == 'couch_1')
        room['spawns'][0]['position_m'] = [couch['box']['centre_m'][0], 0., couch['box']['centre_m'][2]]
        setup['neg_z_bearing_deg'] = 0
        folder, setup_path = fc.write(self.out, room, inv, setup)
        found = fc.problems(folder, setup_path)
        self.assertTrue(any('player_start is not clear' in p for p in found), found)
        self.assertTrue(any('disagree on the site' in p for p in found), found)

    def test_a_real_room_is_never_written_where_git_tracks_it(self):
        shell, inventory = shell_and_inventory()
        room, inv, setup, _ = fc.convert(shell, inventory, 'captured_garage', 'A', 90)
        tracked = ROOT/'pipeline'/'landscape'/'corpus'
        with self.assertRaises(SystemExit):
            fc.write(tracked, room, inv, setup)
        self.assertFalse((tracked/'captured_garage').exists())


if __name__ == '__main__':
    unittest.main()
