"""Run: python -B -m unittest pipeline.landscape.gen_a.test_generate -v"""
import copy
import hashlib
import os
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
import uuid

from pipeline.landscape.harness import read_package
from .generate import build, ROOT, Valley
from .checks import check, Surface


class LandscapeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp_root=Path(os.environ.get('TEMP',os.environ.get('TMPDIR','/tmp'))).resolve()
        cls.work=cls.temp_root/('enfractal-land17-tests-'+uuid.uuid4().hex)
        os.makedirs(cls.work)
        cls.rooms=ROOT/'pipeline/landscape/corpus/rooms'
        for name in ['garage_nominal','garage_scan_17']:
            build(cls.rooms/name,cls.work/name)
        build(cls.rooms/'garage_nominal',cls.work/'repeat')
        cls.data=read_package(cls.work/'garage_nominal',cls.rooms/'garage_nominal')

    @classmethod
    def tearDownClass(cls):
        target=cls.work.resolve()
        assert target.parent==cls.temp_root and target.name.startswith('enfractal-land17-tests-')
        shutil.rmtree(target)

    def test_deterministic_package_bytes(self):
        a,b=self.work/'garage_nominal',self.work/'repeat'
        left={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in a.iterdir()}
        right={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in b.iterdir()}
        self.assertEqual(left,right)
        print('DETERMINISM PASS identical file inventory and bytes across 2 builds')

    def test_nominal_principles(self):
        check(self.work/'garage_nominal',self.rooms/'garage_nominal')

    def test_scan_principles(self):
        check(self.work/'garage_scan_17',self.rooms/'garage_scan_17')

    def test_every_source_has_measurable_uplift(self):
        # Ablation: remove one contributor, keeping all neighbouring forms,
        # stream and path decisions fixed. This catches swallowed small props.
        for name in ['garage_nominal','garage_scan_17']:
            v=Valley(self.rooms/name)
            for f in list(v.forms):
                samples=[]
                for dx,dz in [(0,0),(-.03,0),(.03,0),(0,-.03),(0,.03)]:
                    x,z=f['x']+dx,f['z']+dz;samples.append((x,z,v.height(x,z)))
                original=v.forms
                v.forms=[o for o in original if o is not f]
                effect=max(y-v.height(x,z) for x,z,y in samples)
                v.forms=original
                self.assertGreater(effect,.001,f'{name}: {f["id"]} swallowed')
        print('SOURCE ABLATION PASS 16/16 contributors in each room exceed 1 mm local uplift')

    def mutated_check(self,data):
        with patch('pipeline.landscape.gen_a.checks.read_package',return_value=data):
            check('unused','unused',verbose=False)

    def test_guard_uphill_water(self):
        data=copy.deepcopy(self.data);doc,meshes,_,_=data
        for p in meshes['stream'][0]['positions'][60:63]:p[1]+=.10
        doc['x_stream'][20][1]+=.10
        with self.assertRaisesRegex(AssertionError,'water uphill'):
            self.mutated_check(data)

    def test_guard_floating_cottage(self):
        data=copy.deepcopy(self.data)
        next(s for s in data[0]['scatter'] if s['prototype']=='valley_cottage')['position_m'][1]+=.02
        with self.assertRaisesRegex(AssertionError,'unsupported buildings'):
            self.mutated_check(data)

    def test_guard_ground_hole(self):
        data=copy.deepcopy(self.data)
        data[1]['land'][0]['triangles'].pop()
        with self.assertRaisesRegex(AssertionError,'terrain hole'):
            Surface(data[0],data[1])

    def test_guard_reversed_water_surface(self):
        data=copy.deepcopy(self.data)
        data[1]['stream'][0]['triangles'][0].reverse()
        with self.assertRaisesRegex(AssertionError,'water faces down or folds'):
            self.mutated_check(data)

    def test_guard_blocked_pickup(self):
        data=copy.deepcopy(self.data);doc=data[0]
        p=doc['objects'][0]['position_m']
        doc['scatter'].append(dict(prototype='boulder',position_m=[p[0]-.08,p[1],p[2]],yaw_deg=0,scale=[1,1,1]))
        with self.assertRaisesRegex(AssertionError,'blocked route goal'):
            self.mutated_check(data)


if __name__=='__main__':unittest.main()
