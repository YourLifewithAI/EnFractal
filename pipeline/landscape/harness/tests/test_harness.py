import copy
import math
import os
from pathlib import Path
import shutil
import struct
import subprocess
import unittest
import uuid
from unittest.mock import patch

from pipeline.landscape.harness.build_reference import ROOM, REFERENCE, build, quad
from pipeline.landscape.harness.common import FRAME, MAX_BYTES, SETUP, canonical, load_json, sha, harness_revision
from pipeline.landscape.harness.glb import encode_mesh, decode_mesh
from pipeline.landscape.harness.kit import SIZES, prototype
from pipeline.landscape.harness.package import read_package, validate_package, footprint_inside
from pipeline.landscape.harness.png import encode, decode
from pipeline.landscape.harness.render import find_blender, render, DRAFT, FINAL
from pipeline.landscape.harness.views import ground_height, camera_plan, sun_position, project_bounds


class Workspace(unittest.TestCase):
    def setUp(self):
        self.work=Path(os.environ.get('TEMP',os.environ.get('TMPDIR','/tmp')))/('enfractal-test-'+uuid.uuid4().hex)
        os.makedirs(self.work)

    def tearDown(self):
        shutil.rmtree(self.work)


class PackageTests(Workspace):
    def setUp(self):
        super().setUp(); self.folder=self.work/'package'; build(self.folder)

    def mutate(self,change):
        doc=load_json(self.folder/'package.json'); change(doc)
        (self.folder/'package.json').write_bytes(canonical(doc))

    def rejects(self,change):
        original=(self.folder/'package.json').read_bytes()
        self.mutate(change)
        with self.assertRaises(ValueError): validate_package(self.folder,ROOM)
        (self.folder/'package.json').write_bytes(original)

    def test_deterministic_writer_and_pinned_reference(self):
        second=self.work/'second'; build(second)
        for p in self.folder.iterdir():
            self.assertEqual(p.read_bytes(),(second/p.name).read_bytes())
            self.assertEqual(p.read_bytes(),(REFERENCE/p.name).read_bytes())
        self.assertLess(sum(p.stat().st_size for p in self.folder.iterdir()),1_000_000)
        self.assertGreater(validate_package(self.folder,ROOM)['triangles'],0)

    def test_wrong_format_frame_and_units(self):
        for key,value in [('format','wrong'),('version',2),('units','cm'),('axes','z_up'),('floor_y_m',1)]:
            with self.subTest(key=key): self.rejects(lambda d:d.update({key:value}))

    def test_unknown_fields_and_setup(self):
        self.rejects(lambda d:d.update({'camera':{}}))
        self.rejects(lambda d:d['setup'].update(longitude_deg=0))
        self.rejects(lambda d:d['setup'].update(water='ocean'))
        self.rejects(lambda d:d['setup'].update(latitude_deg=30.5))
        self.rejects(lambda d:d['setup'].update(day_of_year=366))
        self.rejects(lambda d:d['scenery'][0].update(reachable=True))

    def test_hash_missing_tamper_source_and_extra_file(self):
        self.rejects(lambda d:d['files'].update({'land.glb':'0'*64}))
        self.rejects(lambda d:d['files'].pop('land.glb'))
        self.rejects(lambda d:d['source'].update(room_sha256='0'*64))
        self.rejects(lambda d:d['source'].update(inventory_sha256='0'*64))
        self.rejects(lambda d:d['source'].update(room_id='another_room'))
        (self.folder/'extra.txt').write_bytes(b'extra')
        with self.assertRaises(ValueError): validate_package(self.folder,ROOM)

    def test_unknown_roles_and_prototypes(self):
        self.rejects(lambda d:d['material_roles'].append('plastic'))
        self.rejects(lambda d:d['scatter'][0].update(prototype='unknown'))
        self.rejects(lambda d:d['prototypes']['marker'].update(mesh='absent'))
        self.rejects(lambda d:d['prototypes']['marker'].update(size_m=[1,1,1]))

    def test_objects_bounds_and_properties(self):
        self.rejects(lambda d:d['objects'][0].update(position_m=[10,0,0]))
        self.rejects(lambda d:d['objects'][0].update(position_m=[2.99,0,0],yaw_deg=45))
        self.rejects(lambda d:d['objects'][0].update(mass_kg=-1))
        self.rejects(lambda d:d['objects'][0].update(carriable='yes'))
        self.rejects(lambda d:d['scatter'][0].update(position_m=[0,2.7,0]))
        self.rejects(lambda d:d['scatter'][0].update(scale=[1,0,1]))
        self.rejects(lambda d:d['objects'].append(copy.deepcopy(d['objects'][0])))

    def test_nonfinite_json(self):
        original=(self.folder/'package.json').read_bytes()
        for bad in [b'NaN',b'Infinity',b'-Infinity']:
            (self.folder/'package.json').write_bytes(original.replace(b'"mass_kg": 0.05',b'"mass_kg": '+bad))
            with self.assertRaises(ValueError): validate_package(self.folder,ROOM)

    def test_limits(self):
        with patch('pipeline.landscape.harness.package.MAX_BYTES',10):
            with self.assertRaises(ValueError): validate_package(self.folder,ROOM)
        with patch('pipeline.landscape.harness.package.MAX_TRIANGLES',1):
            with self.assertRaises(ValueError): validate_package(self.folder,ROOM)
        with patch('pipeline.landscape.harness.package.MAX_INSTANCES',1):
            with self.assertRaises(ValueError): validate_package(self.folder,ROOM)
        # Stored mesh count remains small while repetition exceeds the new cap.
        with patch('pipeline.landscape.harness.package.MAX_EXPANDED_TRIANGLES',100):
            with self.assertRaisesRegex(ValueError,'expanded triangle limit'): validate_package(self.folder,ROOM)

    def test_setup_pinned_before_blender_or_output(self):
        expected=self.work/'setup.json'; expected.write_bytes(canonical(SETUP))
        self.mutate(lambda d:d['setup'].update(water='none'))
        with patch('pipeline.landscape.harness.render.find_blender') as finder:
            with self.assertRaisesRegex(ValueError,'setup differs'):
                render(self.folder,ROOM,'A',self.work/'out',expect_setup=expected)
            finder.assert_not_called()
        self.assertFalse((self.work/'out').exists())

    def test_revision_tracks_source_and_library_versions(self):
        source=self.work/'source'; source.mkdir()
        (source/'worker.py').write_bytes(b'first\n')
        (source/'kit.py').write_bytes(b'kit\n')
        (source/'ab-setup.json').write_bytes(canonical(SETUP))
        first=harness_revision(source)
        self.assertEqual(first,harness_revision(source))
        (source/'kit.py').write_bytes(b'changed\n')
        self.assertNotEqual(first['sha256'],harness_revision(source)['sha256'])
        with patch('pipeline.landscape.harness.common.MATERIALS_VERSION',2):
            self.assertNotEqual(first['sha256'],harness_revision(source)['sha256'])

    def test_glb_bad_geometry_and_features(self):
        p=quad(-1,1,-1,1,0,'meadow')
        cases=[]
        v=copy.deepcopy(p); v['triangles']=[[0,0,1]]; cases.append(v)
        v=copy.deepcopy(p); v['positions'][0][0]=math.nan; cases.append(v)
        v=copy.deepcopy(p); v['positions'][0][0]=math.inf; cases.append(v)
        v=copy.deepcopy(p); v['role']='unknown'; cases.append(v)
        v=copy.deepcopy(p); v['triangles']=[[0,1,999]]; cases.append(v)
        v=copy.deepcopy(p); v['blend_role']='soil'; cases.append(v)
        v=copy.deepcopy(p); v['tints']=[[1,1,1,.5]]*4; cases.append(v)
        for v in cases:
            with self.subTest(value=str(v)[:70]),self.assertRaises(ValueError): encode_mesh([v])
        data=encode_mesh([p])
        self.assertEqual(encode_mesh(decode_mesh(data)),data)
        with self.assertRaises(ValueError): decode_mesh(data[:-4])
        # Rehashed but malformed data must still fail, independent of hash guard.
        (self.folder/'land.glb').write_bytes(data[:-4])
        self.mutate(lambda d:d['files'].update({'land.glb':sha(data[:-4])}))
        with self.assertRaises(ValueError): validate_package(self.folder,ROOM)

    def test_kit_bottom_centre_and_exact_sizes(self):
        for name,size in SIZES.items():
            ps=decode_mesh(encode_mesh(prototype(name)))
            vs=[v for p in ps for v in p['positions']]
            lo=[min(v[i] for v in vs) for i in range(3)]; hi=[max(v[i] for v in vs) for i in range(3)]
            self.assertAlmostEqual(lo[1],0,places=6)
            self.assertAlmostEqual(lo[0]+hi[0],0,places=6)
            self.assertAlmostEqual(lo[2]+hi[2],0,places=6)
            for i in range(3): self.assertAlmostEqual(hi[i]-lo[i],size[i],places=6)

    def test_concave_floor_bounds(self):
        room=load_json(ROOM/'room.json')
        floor=next(p for p in room['shell']['parts'] if p['role']=='floor')
        floor['geometry']['points_m']=[[-3,0,-3],[-3,0,3],[0,0,3],[0,0,0],[3,0,0],[3,0,-3]]
        with self.assertRaises(ValueError): footprint_inside([1,0,1],[.2,.2,.2],0,room)
        footprint_inside([-1,0,1],[.2,.2,.2],45,room)


class ViewTests(unittest.TestCase):
    def test_overview_floor_corners_fit_with_margin(self):
        room=load_json(ROOM/'room.json')
        for view in camera_plan(room,{},[])[:3]:
            points=project_bounds(view,room['bounds'],FINAL['overview_lens_mm'])
            self.assertEqual(len(points),8)
            for x,y in points:
                self.assertTrue(0<x<1 and 0<y<1,(view['name'],x,y))
            floor=[points[i] for i in (0,1,4,5)]
            for x,y in floor:
                self.assertTrue(40<=x*1280<=1280-40,(view['name'],x))
                self.assertTrue(40-1e-9<=y*720<=720-40,(view['name'],y))
            self.assertAlmostEqual(max(x for x,y in points)-min(x for x,y in points),.87)
            self.assertTrue(.80<=max(x for x,y in floor)-min(x for x,y in floor)<=.90)

    def test_eye_reach_blur_circle(self):
        # Thin-lens circle of confusion in final pixels, focused at the figure.
        focal=FINAL['eye_lens_mm']/1000; focus=FINAL['eye_focus_m']
        def blur(distance):
            return focal*focal*abs(focus-distance)/(FINAL['eye_fstop']*distance*(focus-focal))/.036*1280
        self.assertLess(blur(.15),2.5)
        self.assertEqual(blur(.4),0)
        self.assertLess(blur(2),1.2)
        self.assertGreater(blur(7),blur(2))

    def test_sun_published_nrel_spa_example(self):
        # NREL/TP-560-34302 Appendix A. H=11.105900 degrees converts to
        # apparent solar time 12+H/15. Geometric elevation before refraction
        # is 39.872046; azimuth 194.340241. Approximate NOAA tolerance 0.35 deg.
        sun=sun_position(39.742476,290,12+11.105900/15)
        self.assertAlmostEqual(sun['altitude_deg'],39.872046,delta=.35)
        self.assertAlmostEqual(sun['azimuth_deg'],194.340241,delta=.35)

    def test_room_bearing_and_morning_east(self):
        sun=sun_position(30,172,9)
        self.assertGreater(sun['direction_room'][0],0)
        self.assertGreater(sun['altitude_deg'],40)
        rotated=sun_position(30,172,9,90)
        self.assertAlmostEqual(rotated['direction_room'][2],-sun['direction_room'][0])

    def test_manifest_cameras_and_triangle_ray_height(self):
        room=load_json(ROOM/'room.json'); mesh={'land':[quad(-4,4,-4,4,.8,'meadow')]}
        terrain=[{'mesh':'land'}]; views=camera_plan(room,mesh,terrain)
        player=next(v for v in views if v['name']=='eye_player')
        self.assertEqual(player['position_m'],[0,.8+.087,-3])
        self.assertEqual(player['target_m'],[0,.8+.087,0])
        self.assertEqual(player['ground_height_m'],.8)
        changed=camera_plan(room,{'land':[quad(-4,4,-4,4,1.8,'meadow')]},terrain)
        self.assertEqual(views[:3],changed[:3])
        self.assertIsNone(ground_height(mesh,terrain,10,10))
        tilted={'land':[dict(role='rock',positions=[[0,0,0],[0,1,1],[1,2,0]],triangles=[[0,1,2]])]}
        self.assertAlmostEqual(ground_height(tilted,terrain,.25,.25),.75)
        room['spawns'][0]['yaw_deg']=90
        yaw=camera_plan(room,mesh,terrain)[3]
        self.assertEqual(yaw['target_m'],player['target_m'])
        room['spawns'][0]['position_m']=[0,0,-.1]
        close=camera_plan(room,mesh,terrain)[3]
        self.assertEqual(close['target_m'],[0,.8+.087,3.5])
        self.assertTrue(all(v['projection']=='perspective' for v in views[:3]))

    def test_png_rgb_roundtrip(self):
        pixels=bytes(range(24)); data=encode(4,2,pixels)
        w,h,p=decode(data); self.assertEqual((w,h,p),(4,2,pixels))
        self.assertNotIn(b'tEXt',data); self.assertNotIn(b'eXIf',data)


class BlenderTests(Workspace):
    def test_geometry_enclosure_open_surfaces_and_overlapping_solids(self):
        try: blender=find_blender()
        except ValueError as exc: self.skipTest(str(exc))
        config=self.work/'config.json'
        config.write_bytes(canonical(dict(repo=str(ROOM.parents[4]),out=str(self.work),settings=DRAFT)))
        script=self.work/'probe.py'
        script.write_text('''import sys
sys.argv=['blender','--',sys.argv[-1]]
import json
from pathlib import Path
sys.path.insert(0,json.loads(Path(sys.argv[-1]).read_bytes())['repo'])
from pipeline.landscape.harness.worker import add_mesh, geometry_bvh, eye_blocked, xyz
from pipeline.landscape.harness.kit import box
from mathutils import Vector
import bpy
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
objects=add_mesh('solid',[box([1,1,1],[0,.5,0],'stone')])
objects+=add_mesh('overlap',[box([1,1,1],[.1,.5,0],'stone')])
bpy.context.view_layer.update()
bvh=geometry_bvh(objects)
assert eye_blocked(bvh,Vector(xyz([0,.5,0])))
assert not eye_blocked(bvh,Vector(xyz([2,.5,0])))
# An open face ahead of an outside eye used to create odd-crossing false flags.
objects=add_mesh('open',[dict(role='rock',positions=[[1,0,-1],[1,0,1],[1,2,0]],triangles=[[0,1,2]])])
bpy.context.view_layer.update()
assert not eye_blocked(geometry_bvh(objects),Vector(xyz([0,.5,0])))
from pipeline.landscape.harness.views import camera_plan
from pipeline.landscape.harness.common import load_json
from pipeline.landscape.harness.build_reference import ROOM
from bpy_extras.object_utils import world_to_camera_view
room=load_json(ROOM/'room.json')
scene=bpy.context.scene; scene.render.resolution_x=1280; scene.render.resolution_y=720
cam=bpy.data.objects.new('projection_probe',bpy.data.cameras.new('projection_probe'))
bpy.context.collection.objects.link(cam)
for v in camera_plan(room,{},[])[:3]:
    cam.location=xyz(v['position_m'])
    cam.rotation_euler=(Vector(xyz(v['target_m']))-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.lens=40; cam.data.shift_x=v['shift_x']; cam.data.shift_y=v['shift_y']
    bpy.context.view_layer.update()
    pts=[]
    for x in room['bounds']['min_m'][0],room['bounds']['max_m'][0]:
        for y in room['bounds']['min_m'][1],room['bounds']['max_m'][1]:
            for z in room['bounds']['min_m'][2],room['bounds']['max_m'][2]:
                q=world_to_camera_view(scene,cam,Vector(xyz([x,y,z])))
                assert 0<q.x<1 and 0<q.y<1 and q.z>0,(v['name'],tuple(q))
                pts.append(q)
    assert abs(max(q.x for q in pts)-min(q.x for q in pts)-.87)<1e-5
print('OVERVIEW_PROJECTION_PASS eight floor/roof corners, 87 percent width')
print('GEOMETRY_FLAGS_PASS closed solids, overlapping solids, open surface')
''',encoding='utf-8',newline='\n')
        env=dict(os.environ,PYTHONDONTWRITEBYTECODE='1',PYTHONPATH=str(ROOM.parents[4]))
        result=subprocess.run([str(blender),'--background','--factory-startup','--python-exit-code','1',
                               '--python',str(script),'--',str(config)],env=env,capture_output=True,text=True)
        self.assertEqual(result.returncode,0,result.stdout+result.stderr)
        self.assertIn('GEOMETRY_FLAGS_PASS',result.stdout)
        print('GEOMETRY_FLAGS_PASS closed solids, overlapping solids, open surface')
        print('OVERVIEW_PROJECTION_PASS eight floor/roof corners, 87 percent width')

    def test_repeated_cpu_render_bytes_and_camera_receipt(self):
        try: find_blender()
        except ValueError as exc: self.skipTest(str(exc))
        settings=dict(DRAFT,width=96,height=54,samples=4)
        for name in ['a','b']:
            render(REFERENCE,ROOM,'BLIND A',self.work/name,settings=settings,
                   expect_setup=REFERENCE.parents[1]/'ab-setup.json',kit_picture=True)
        for name in [v['name'] for v in camera_plan(load_json(ROOM/'room.json'),
                       read_package(REFERENCE,ROOM)[1],read_package(REFERENCE,ROOM)[0]['terrain'])]+['slope','sheet','kit']:
            a=(self.work/'a'/(name+'.png')).read_bytes(); b=(self.work/'b'/(name+'.png')).read_bytes()
            self.assertEqual(a,b,name)
        receipt=load_json(self.work/'a/receipt.json')
        self.assertEqual(receipt['blender'],'5.2.2')
        self.assertEqual(receipt['blender_display'],'5.2.2 LTS')
        self.assertEqual(receipt['harness_revision'],harness_revision())
        self.assertEqual(receipt['expected_setup'],SETUP)
        self.assertEqual(receipt['settings']['device'],'CPU')
        self.assertFalse(receipt['settings']['denoising_use_gpu'])
        self.assertEqual(receipt['views'][3]['ground_height_m'],0)
        self.assertNotIn('generator',receipt)
        w,h,pixels=decode((self.work/'a/slope.png').read_bytes())
        green=sum(pixels[i+1]>pixels[i]+15 and pixels[i+1]>pixels[i+2]+15
                  for i in range(0,len(pixels),3))
        self.assertGreater(green,w*h*.05,'flat terrain must visibly use the green slope material')
        for view in receipt['views']:
            self.assertEqual(view['projection'],'PERSP')
            if view['kind']=='eye':
                self.assertFalse(view['inside_geometry'])
                self.assertFalse(view['figure_inside_geometry'])
                self.assertAlmostEqual(math.hypot(view['figure_position_m'][0]-view['position_m'][0],
                                                view['figure_position_m'][2]-view['position_m'][2]),
                                       math.hypot(.4,.07))
        self.assertEqual(receipt['kit']['rendered_harness_sha256'],harness_revision()['sha256'])
        w,h,pixels=decode((self.work/'a/eye_player.png').read_bytes())
        sky=[pixels[(y*w+x)*3:(y*w+x)*3+3] for y in range(1,5) for x in range(w)]
        self.assertGreater(sum(b>r+15 and b>g+5 for r,g,b in sky),len(sky)*.5,
                           'summer upper sky must read blue, not the ground hemisphere')
        print('SUMMER_SKY_PASS upper hemisphere blue')
        print('DETERMINISM_PASS 7 view PNGs + sheet + kit; Blender 5.2.2 CPU OIDN, 96x54')
        render(REFERENCE,ROOM,'BLIND A',self.work/'a',settings=settings,
               expect_setup=REFERENCE.parents[1]/'ab-setup.json',views=['overview_ne'])
        partial=load_json(self.work/'a/receipt.json')
        self.assertTrue(partial['partial_rerender'])
        self.assertTrue(partial['uniform_harness_revision'])
        self.assertEqual(len(partial['retained_views']),6)
        for entry in partial['views']+[partial['diagnostic']]:
            self.assertEqual((self.work/'a'/entry['path']).read_bytes(),
                             (self.work/'b'/entry['path']).read_bytes())
        self.assertEqual((self.work/'a/sheet.png').read_bytes(),(self.work/'b/sheet.png').read_bytes())
        with patch('pipeline.landscape.harness.render.subprocess.run') as launch:
            with self.assertRaisesRegex(ValueError,'inputs/settings differ'):
                render(REFERENCE,ROOM,'BLIND A',self.work/'a',settings=dict(settings,samples=5),
                       views=['overview_ne'])
            launch.assert_not_called()
        (self.work/'a/eye_player.png').write_bytes(b'edited')
        with patch('pipeline.landscape.harness.render.subprocess.run') as launch:
            with self.assertRaisesRegex(ValueError,'bytes differ'):
                render(REFERENCE,ROOM,'BLIND A',self.work/'a',settings=settings,views=['overview_ne'])
            launch.assert_not_called()
        print('PARTIAL_RERENDER_PASS retained bytes, sheet, source revision; changed inputs/tampering rejected')


if __name__=='__main__': unittest.main()
