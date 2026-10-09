"""Verify bytes, real contract validation, C4 consumers and perturbation budgets."""

import hashlib
import importlib.util
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

from pipeline.landscape.corpus import generate as gen
from pipeline.landscape.corpus.fixtures import layouts
from pipeline.landscape.corpus.preview import corners

REPO = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location("corpus_contract_validator",REPO/"contracts"/"validate.py")
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)

# Real CPU-only scan consumers, not corpus-specific clones. No stage is invoked.
from roomscan.inventory.overlay import entry_box
from roomscan.inventory.recipes import validate_request, request_for, SLOTS
from roomscan.shell.export import picked_footprints
from roomscan.shell.manifest import shell_parts
from roomscan.shell.planes import ShellPlan, Surface
from roomscan.shell.spec import ShellSpec


def load(path):
    return json.loads(path.read_bytes())


def tree_bytes(folder):
    return {p.relative_to(folder).as_posix():p.read_bytes() for p in folder.rglob("*") if p.is_file()}


def inside_outline(x,z,outline):
    inside = False
    for a,b in zip(outline,outline[1:]+outline[:1]):
        # Furniture may touch a wall. Include the segment itself, including
        # re-entrant edges, without admitting the L-shaped room's absent quadrant.
        length = math.dist(a,b)
        cross = (b[0]-a[0])*(z-a[1])-(b[1]-a[1])*(x-a[0])
        if (abs(cross) <= 1e-12*length and
                min(a[0],b[0])-1e-12 <= x <= max(a[0],b[0])+1e-12 and
                min(a[1],b[1])-1e-12 <= z <= max(a[1],b[1])+1e-12):
            return True
        if (a[1] > z) != (b[1] > z) and x < (b[0]-a[0])*(z-a[1])/(b[1]-a[1])+a[0]:
            inside = not inside
    return inside


class CorpusTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # os.makedirs avoids Python 3.13+'s owner-only tempfile ACLs.
        cls.scratch = Path(os.environ.get("TEMP",os.environ.get("TMP","/tmp"))).resolve()/f"enfractal-corpus14-{uuid.uuid4().hex}"
        os.makedirs(cls.scratch)
        cls.addClassCleanup(shutil.rmtree,cls.scratch)
        cls.first = cls.scratch/"first"
        cls.second = cls.scratch/"second"
        cls.index = gen.generate(cls.first)

    def test_every_generated_and_checked_in_room_validates(self):
        for item in self.index["rooms"]:
            for root in (self.first,gen.OUTPUT):
                folder = root/item["id"]
                with self.subTest(room=item["id"],root=str(root)):
                    self.assertEqual(validator.check_room(folder),[])
                    self.assertEqual(load(folder/"room.json")["objects"],[])

    def test_inventory_reads_through_actual_roomscan_consumers(self):
        self.assertEqual(gen.SLOTS,SLOTS)
        for item in self.index["rooms"]:
            folder = self.first/item["id"]
            inv = load(folder/"inventory.json")
            self.assertEqual(inv["schema"],"enfractal.inventory")
            self.assertEqual(inv["room"],load(folder/"room.json")["room_id"])
            self.assertEqual(inv["session_id"],"s-synthetic-"+item["id"])
            ids = {o["id"] for o in inv["objects"]}
            self.assertEqual(len(ids),item["object_count"])
            picked = {p["id"] for p in inv["picks"]}
            expected = [tuple(o["box"]["footprint_m"]) for o in inv["objects"] if o["id"] in picked and o["placement"]["support"]["kind"] == "floor"]
            self.assertEqual(picked_footprints(folder),expected)
            for o in inv["objects"]:
                with self.subTest(room=item["id"],object=o["id"]):
                    self.assertIn(o["kind"],gen.BY_NAME)
                    box = entry_box(o)
                    self.assertEqual(list(box.size_m),o["box"]["size_m"])
                    for actual,wanted in zip(box.footprint,o["box"]["footprint_m"]):
                        self.assertAlmostEqual(actual,wanted,delta=.0015)
                    # C4 independently rounds centre, base and height to 1 mm:
                    # 0.5 mm + 0.5 mm + half of 0.5 mm = 1.25 mm. The tiny
                    # epsilon handles float subtraction at the inclusive limit.
                    self.assertAlmostEqual(box.centre_m[1],box.base_y+box.size_m[1]/2,delta=.00125+1e-12)
                    self.assertAlmostEqual(sum(v*v for v in o["placement"]["rotation"]),1,delta=.000003)
                    self.assertEqual(o["placement"]["yaw_deg"],o["box"]["yaw_deg"])
                    self.assertTrue((folder/o["evidence"]["sheet"]).is_file())
                    self.assertRegex(o["colours"][0]["hex"],r"^#[0-9a-f]{6}$")
                    self.assertEqual(sum(c["share"] for c in o["colours"]),1)
                    target = o["placement"]["support"].get("target_id")
                    if target:
                        self.assertIn(target,ids)
                        parent = next(e for e in inv["objects"] if e["id"] == target)
                        top = parent["placement"]["position_m"][1]+parent["box"]["size_m"][1]
                        self.assertAlmostEqual(box.base_y,top,delta=.0015)
                        self.assertAlmostEqual(o["placement"]["support"]["height_m"],box.base_y)
                    if o["recipe"]:
                        validate_request(o["recipe"])
                        req = o["recipe"]
                        self.assertEqual(req,request_for(req["recipe"],tuple(o["box"]["size_m"]),req["colours"]))
                        self.assertEqual(set(o["recipe_slots"]["from_scan"])|set(o["recipe_slots"]["defaulted"]),set(SLOTS[req["recipe"]]))
                    else:
                        self.assertEqual(o["recipe_needed"]["for_kind"],o["kind"])
                        self.assertEqual(o["recipe_needed"]["size_m"],o["box"]["size_m"])
            for rank,p in enumerate(inv["picks"],1):
                chosen = next(o for o in inv["objects"] if o["id"] == p["id"])
                self.assertEqual(chosen["pick"],{"rank":rank,"why":p["why"]})
                self.assertGreaterEqual(chosen["confidence"],.4)

    def test_rectangular_shells_match_c3_shell_parts(self):
        for layout in layouts():
            if len(layout["outline_xz_m"]) != 4:
                continue
            w,h,d = layout["dimensions_m"]
            surfaces = [Surface("shell:floor","floor","floor",[(layout["floor_colour"],1)]),
                        Surface("shell:ceiling","ceiling","ceiling",[(layout["ceiling_colour"],1)])]
            surfaces += [Surface("shell:wall_"+k.lower(),"wall",k,[(layout["wall_colour"],1)]) for k in "ABCD"]
            scan_plan = ShellPlan(-w/2,w/2,-d/2,d/2,h,surfaces,1,"synthetic metric truth")
            expected = shell_parts(scan_plan,ShellSpec(room=layout["id"],session_id="s-synthetic"))
            actual = load(self.first/(layout["id"]+"_nominal")/"room.json")["shell"]["parts"]
            self.assertEqual(actual,expected)

    def test_generation_is_byte_deterministic_and_checked_in_outputs_are_fresh(self):
        gen.generate(self.second)
        self.assertEqual(tree_bytes(self.first),tree_bytes(self.second))
        self.assertEqual(tree_bytes(self.first),tree_bytes(gen.OUTPUT))
        for item in self.index["rooms"]:
            for name,digest in item["files_sha256"].items():
                self.assertEqual(hashlib.sha256((self.first/item["id"]/name).read_bytes()).hexdigest(),digest)

    def test_stdlib_only_generator_in_isolated_process(self):
        # -S removes installed site packages (numpy, Pillow, torch, etc.).
        script = "import sys; sys.path.insert(0,sys.argv[1]); from pipeline.landscape.corpus.generate import generate; generate(sys.argv[2]); print('STDLIB_GENERATOR_OK')"
        result = subprocess.run([sys.executable,"-B","-S","-c",script,str(REPO),str(self.scratch/"stdlib")],capture_output=True,text=True,check=False)
        self.assertEqual(result.returncode,0,result.stdout+result.stderr)
        self.assertEqual(result.stdout.strip(),"STDLIB_GENERATOR_OK")
        self.assertEqual(tree_bytes(self.first),tree_bytes(self.scratch/"stdlib"))

    def test_imperfect_variants_respect_bounds_and_exercise_each_imperfection(self):
        for layout in layouts():
            nominal = load(self.first/(layout["id"]+"_nominal")/"inventory.json")
            base = {o["id"]:o for o in nominal["objects"]}
            variants = []
            for seed in gen.SEEDS:
                folder = self.first/f"{layout['id']}_scan_{seed}"
                inv,truth = load(folder/"inventory.json"),load(folder/"truth.json")
                variants.append(inv)
                changed_kinds = changed_sizes = changed_positions = changed_colours = low = 0
                for o in inv["objects"]:
                    b = base[o["id"]]
                    with self.subTest(room=folder.name,object=o["id"]):
                        for value,original in zip(o["box"]["size_m"],b["box"]["size_m"]):
                            self.assertLessEqual(abs(value-original),original*.15+.000501)
                        p,q = o["placement"]["position_m"],b["placement"]["position_m"]
                        self.assertLessEqual(math.dist(p,q),.1)
                        self.assertEqual(o["box"]["yaw_deg"],b["box"]["yaw_deg"])
                        if o["placement"]["support"]["kind"] == "floor":
                            self.assertEqual(p[1],0)
                        expected_colour = gen.shifted(b["colours"][0]["hex"],truth["lamp_channel_shift"])
                        self.assertEqual(o["colours"][0]["hex"],expected_colour)
                        for i in (1,3,5):
                            self.assertLessEqual(abs(int(o["colours"][0]["hex"][i:i+2],16)-int(b["colours"][0]["hex"][i:i+2],16)),24)
                        changed_kinds += o["kind"] != b["kind"]
                        changed_sizes += o["box"]["size_m"] != b["box"]["size_m"]
                        changed_positions += p != q
                        changed_colours += o["colours"] != b["colours"]
                        low += o["confidence"] <= .38
                for count in (changed_kinds,changed_sizes,changed_positions,changed_colours,low):
                    self.assertGreater(count,0,folder.name)
                self.assertLessEqual(changed_kinds,max(1,len(base)//4))
            self.assertNotEqual(variants[0],variants[1])

    def test_authored_layouts_supports_voids_groups_and_clear_spawns(self):
        rooms = layouts()
        l_outline = next(r for r in rooms if r["id"] == "awkward_l")["outline_xz_m"]
        self.assertTrue(inside_outline(0,1,l_outline))
        self.assertFalse(inside_outline(.01,1,l_outline))
        self.assertFalse(inside_outline(3.01,-1,l_outline))
        self.assertEqual(len(rooms),8)
        self.assertGreaterEqual(len(next(r for r in rooms if r["id"] == "workshop")["objects"]),40)
        self.assertEqual(len(next(r for r in rooms if r["id"] == "near_empty")["objects"]),2)
        for layout in rooms:
            by_id = {o["id"]:o for o in layout["objects"]}
            for o in layout["objects"]:
                self.assertIn(o["size_basis"],gen.SOURCES)
                for x,z in corners(o["position_m"],o["size_m"],o["yaw_deg"]):
                    self.assertTrue(inside_outline(x,z,layout["outline_xz_m"]),(layout["id"],o["id"],x,z))
                self.assertLessEqual(o["position_m"][1]+o["size_m"][1],layout["dimensions_m"][1])
                if o["support_target"]:
                    parent = by_id[o["support_target"]]
                    self.assertAlmostEqual(o["position_m"][1],parent["position_m"][1]+parent["size_m"][1])
                if "void" in o:
                    v = o["void"]
                    self.assertGreaterEqual(v["position_local_m"][1],0)
                    self.assertLessEqual(v["position_local_m"][1]+v["size_m"][1],o["size_m"][1])
                    self.assertLess(v["size_m"][0],o["size_m"][0])
                    self.assertLess(v["size_m"][2],o["size_m"][2])
            for group in layout["neighbour_groups"]:
                self.assertEqual({by_id[i]["kind"] for i in group},{"office chair","desk","shelving unit"})
                for a,b in zip(group,group[1:]):
                    self.assertLess(math.dist(by_id[a]["position_m"],by_id[b]["position_m"]),1.4)
        for item in self.index["rooms"]:
            inv = load(self.first/item["id"]/"inventory.json")
            shell = load(self.first/item["id"]/"room.json")
            for spawn in shell["spawns"]:
                x,_,z = spawn["position_m"]
                for o in inv["objects"]:
                    if o["placement"]["support"]["kind"] == "floor":
                        x0,z0,x1,z1 = o["box"]["footprint_m"]
                        self.assertFalse(x0-.3 <= x <= x1+.3 and z0-.3 <= z <= z1+.3)

    def test_pngs_are_small_decodable_and_have_no_metadata_text_is_lf(self):
        from PIL import Image
        for path in self.first.rglob("*"):
            if not path.is_file():
                continue
            data = path.read_bytes()
            if path.suffix == ".json":
                self.assertNotIn(b"\r",data)
                data.decode("utf-8")
                self.assertTrue(data.endswith(b"\n"))
                continue
            self.assertEqual(data[:8],b"\x89PNG\r\n\x1a\n")
            self.assertLess(len(data),1_500_000 if path.name == "contact-sheet.png" else 150_000)
            chunks,offset = [],8
            while offset < len(data):
                length = struct.unpack(">I",data[offset:offset+4])[0]
                chunks.append(data[offset+4:offset+8])
                offset += 12+length
            self.assertEqual(chunks,[b"IHDR",b"IDAT",b"IEND"])
            with Image.open(path) as image:
                image.load()
                self.assertEqual(image.size,(2560,960) if path.name == "contact-sheet.png" else (640,480))


if __name__ == "__main__":
    unittest.main()
