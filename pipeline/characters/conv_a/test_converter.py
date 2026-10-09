import copy
from io import BytesIO
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import unittest
import uuid

import numpy as np
from PIL import Image, PngImagePlugin

from audit import accessor, audit, read_glb
from convert import build, canonical, canonical_png, convert, read_json, validate
from synthetic import create

HERE = Path(__file__).resolve().parent


class ConverterTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root = Path(os.environ.get("TEMP","/tmp"))/('enfractal-conv-a-test-'+uuid.uuid4().hex)
        os.makedirs(cls.root)
        create(cls.root/"input")
        cls.spec = read_json(cls.root/"input/interpretation.json")
        cls.outputs = []
        for i in range(2):
            out = cls.root/str(i)
            result = subprocess.run([sys.executable,"-B",str(HERE/"convert.py"),
                "--photo",str(cls.root/"input/drawing.png"),
                "--description",str(cls.root/"input/description.txt"),
                "--interpretation",str(cls.root/"input/interpretation.json"),
                "--out",str(out),"--no-render"],capture_output=True,text=True,
                env={**os.environ,"PYTHONHASHSEED":str(31+i)})
            if result.returncode:
                raise AssertionError(result.stdout+result.stderr)
            cls.outputs.append(out)

    @classmethod
    def tearDownClass(cls):
        # Only the exact fresh test directory under the system temp folder.
        shutil.rmtree(cls.root)

    def test_separate_process_byte_determinism(self):
        for name in ("character.glb","character.json","interpretation.json"):
            self.assertEqual((self.outputs[0]/name).read_bytes(),(self.outputs[1]/name).read_bytes())

    def test_third_drawing_geometry_and_parts(self):
        doc,meta = audit(self.outputs[0])
        self.assertEqual(meta["motion"]["kind"],"hop")
        self.assertEqual(sum(p["role"]=="eye" for p in meta["parts"]),3)
        self.assertEqual(sum(p["role"]=="leg" for p in meta["parts"]),1)
        self.assertLess(meta["triangles"],40000)
        self.assertEqual(len(doc["nodes"]),len(self.spec["parts"])+1)

    def test_front_is_negative_z_and_preserves_image_handedness(self):
        doc,binary = read_glb(self.outputs[0]/"character.glb")
        meta = json.loads((self.outputs[0]/"character.json").read_bytes())
        parts = {p["name"]:p for p in meta["parts"]}
        self.assertGreater(parts["eye_0"]["pivot_m"][0],parts["eye_2"]["pivot_m"][0])
        node = doc["nodes"][parts["pupil_1"]["node_index"]]
        primitive = doc["meshes"][node["mesh"]]["primitives"][0]
        v = accessor(doc,binary,primitive["attributes"]["POSITION"])+parts["pupil_1"]["pivot_m"]
        self.assertLess(v[:,2].max(),0)

    def test_pivot_rotation_preserves_joint_and_carries_child(self):
        doc,binary = read_glb(self.outputs[0]/"character.glb")
        meta = json.loads((self.outputs[0]/"character.json").read_bytes())
        parts = {p["name"]:p for p in meta["parts"]}
        leg,foot = parts["spring_leg"],parts["foot"]
        self.assertEqual(foot["parent"],"spring_leg")
        rotation = np.array([[0,-1,0],[1,0,0],[0,0,1]])
        pivot = np.array(leg["pivot_m"])
        rotated = pivot+rotation@(np.array(foot["pivot_m"])-pivot)
        self.assertGreater(np.linalg.norm(rotated-foot["pivot_m"]),0.01)
        np.testing.assert_allclose(pivot+rotation@(pivot-pivot),pivot)
        node = doc["nodes"][foot["node_index"]]
        np.testing.assert_allclose(pivot+node["translation"],foot["pivot_m"],atol=1e-10)

    def test_refuses_overwriting_output(self):
        before = (self.outputs[0]/"character.glb").read_bytes()
        with self.assertRaisesRegex(ValueError,"empty"):
            convert(self.root/"input/drawing.png",self.root/"input/description.txt",
                    self.root/"input/interpretation.json",self.outputs[0],render=False)
        self.assertEqual(before,(self.outputs[0]/"character.glb").read_bytes())

    def test_rejects_invalid_interpretation(self):
        for mutation in ("parent","surface","radius","crossing","nonfinite"):
            with self.subTest(mutation=mutation):
                s = copy.deepcopy(self.spec)
                if mutation=="parent":s["parts"][0]["parent"]="tail"
                if mutation=="surface":s["parts"][1]["shapes"][0]["surface"]="unknown"
                if mutation=="radius":s["parts"][1]["shapes"][0]["radii"][0]=-1
                if mutation=="crossing":s["parts"][0]["shapes"][0]["contour"]=[[0,0],[100,100],[100,0],[0,100]]
                if mutation=="nonfinite":s["parts"][0]["pivot"][0]=float("inf")
                with self.assertRaises(ValueError):validate(s)

    def test_strict_json(self):
        path = self.root/"bad.json"
        for value in ('{"version":1,"version":2}','{"x":NaN}'):
            path.write_bytes(value.encode())
            with self.assertRaises(ValueError):read_json(path)

    def test_png_metadata_normalization_preserves_pixels(self):
        image = Image.new("RGB",(17,13),(11,117,211))
        versions = []
        for date in ("2026/10/08 20:00:00","2026/10/08 20:04:00"):
            info = PngImagePlugin.PngInfo()
            info.add_text("Date",date)
            data = BytesIO()
            image.save(data,format="PNG",pnginfo=info)
            versions.append(canonical_png(data.getvalue()))
        self.assertEqual(versions[0],versions[1])
        np.testing.assert_array_equal(np.asarray(Image.open(BytesIO(versions[0]))),np.asarray(image))
        self.assertEqual(versions[0],canonical_png(versions[0]))
        broken = bytearray(versions[0]); broken[-5] ^= 1
        with self.assertRaisesRegex(ValueError,"checksum"):canonical_png(bytes(broken))

    def test_input_content_changes_provenance_without_embedding_pixels(self):
        # Two photographs with the same authored interpretation have the same
        # geometry but different provenance. This makes the AI boundary explicit.
        a,ma = build(self.spec,{"photo_sha256":"a"})
        b,mb = build(self.spec,{"photo_sha256":"b"})
        self.assertEqual(a,b)
        self.assertNotEqual(ma,mb)
        self.assertNotIn(b"drawing.png",a)
        self.assertNotIn(b"file:",a)


if __name__=="__main__":
    unittest.main(testRunner=unittest.TextTestRunner(stream=sys.stdout,verbosity=2))
