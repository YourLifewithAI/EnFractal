"""Contract tests. Run from the repository root:

  python -m unittest discover -s contracts/tests -v
"""
from __future__ import annotations

import copy
import hashlib
import json
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

CONTRACTS = Path(__file__).resolve().parents[1]
ROOT = CONTRACTS.parent
sys.path.insert(0, str(CONTRACTS))

import validate  # noqa: E402
from jsonschema import Draft202012Validator  # noqa: E402

EXAMPLES = CONTRACTS / "examples"
TEST_ROOM = ROOT / "game" / "rooms" / "test_room"
PRESETS = ROOT / "game" / "styles"
SEED_PRESET = PRESETS / "storybook_painterly" / "v1.json"
# Presets past draft never change (AGENTS.md). storybook_painterly v1 was locked as a look-gate candidate on 7 October.
LOCKED_PRESETS = {
    "storybook_painterly/v1.json": "e571b1e6267fb2bc4fc5fdd545331f2bb420c43d370628e71cf80ac93e44ed7a",
}

# Each invalid example and the reason it must fail.
EXPECTED_FAILURES = {
    "command_with_principal": "'principal' was unexpected",
    "command_carries_approval": "'approval' was unexpected",
    "command_grab_without_target": "'target' is a required property",
    "command_fetch_without_target": "'target' is a required property",
    "command_effect_radius_too_large": "greater than the maximum of 10",
    "command_effect_params_smuggle_principal": "must not match",
    "command_creation_source_extra_key": "'principal' was unexpected",
    "command_bad_entity_namespace": "does not match",
    "command_unknown_op": "is not one of",
    "command_lock_nothing": "should be non-empty",
    "command_remove_without_expectation": "is not valid under any of the given schemas",
    "command_version_written_as_float": "is not of type 'integer'",
    "command_checkpoint_label_with_injected_line": "does not match",
    "command_physics_without_preset": "'preset' is a required property",
    "result_checkpoint_without_its_revision": "'data' is a required property",
    "result_remembered_without_staleness": "'may_be_stale' is a required property",
    "result_seen_now_with_an_age": "False schema does not allow 1.5",
    "result_ok_with_error": "must not match",
    "result_failure_without_error": "'error' is a required property",
    "result_text_marked_trusted": "True was expected",
    "result_name_with_injected_line": "does not match",
    "duplicate_keys": "duplicate key 'schema'",
}


def write_json(path: Path, document) -> None:
    # Bytes, not text: write_text turns "\n" into "\r\n" on Windows and the validator rejects CR in pinned files.
    path.write_bytes((json.dumps(document, indent=2) + "\n").encode("utf-8"))


def files_under(root: Path) -> dict[str, bytes]:
    return {str(p.relative_to(root)): p.read_bytes() for p in sorted(root.rglob("*")) if p.is_file() and p.suffix != ".py"}


def l_shaped_room(base: Path, reverse_wall: str | None = None) -> Path:
    """An L-shaped room with a raised platform: the bounds centre sits at the inner corner."""
    room = base / "l_room"
    room.mkdir()
    height, thickness = 2.4, 0.1
    corners = [(0, 0), (4, 0), (4, 2), (2, 2), (2, 4), (0, 4)]  # A..F; walking A to F keeps the interior on the e x Y side
    names = ["wall_ab", "wall_bc", "wall_cd", "wall_de", "wall_ef", "wall_fa"]
    floor = [[x, 0.0, z] for x, z in corners]
    if validate._newell_normal(floor)[1] < 0:
        floor.reverse()
    parts = [{"id": "shell:floor", "role": "floor", "geometry": {"kind": "polygon", "points_m": floor, "thickness_m": thickness},
              "collides": True, "material_role": "wood"}]
    for index, name in enumerate(names):
        (ax, az), (bx, bz) = corners[index], corners[(index + 1) % len(corners)]
        points = [[ax, 0.0, az], [bx, 0.0, bz], [bx, height, bz], [ax, height, az]]
        if name == reverse_wall:
            points.reverse()
        parts.append({"id": f"shell:{name}", "role": "wall", "geometry": {"kind": "polygon", "points_m": points, "thickness_m": thickness},
                      "collides": True, "material_role": "painted_wall"})
    platform = [[0.5, 1.6, 0.5], [0.5, 1.6, 1.5], [1.5, 1.6, 1.5], [1.5, 1.6, 0.5]]
    if validate._newell_normal(platform)[1] < 0:
        platform.reverse()
    parts.append({"id": "shell:shelf_platform", "role": "platform", "geometry": {"kind": "polygon", "points_m": platform, "thickness_m": 0.05},
                  "collides": True, "material_role": "wood"})
    manifest = {"schema": "enfractal.room", "version": 1, "room_id": "l_room", "display_name": "L room", "created_utc": "2026-10-06T00:00:00Z",
                "units": "m", "axes": "y_up_neg_z_forward", "source": {"kind": "hand_built"},
                "bounds": {"min_m": [0.0, 0.0, 0.0], "max_m": [4.0, height, 4.0]}, "shell": {"parts": parts}, "objects": [],
                "spawns": [{"id": "player_start", "role": "player", "position_m": [1.0, 0.0, 1.0], "yaw_deg": 0.0},
                           {"id": "companion_start", "role": "companion", "position_m": [1.5, 0.0, 1.0], "yaw_deg": 0.0}],
                "files": []}
    write_json(room / "room.json", manifest)
    return room


class SchemaTests(unittest.TestCase):
    def test_every_schema_is_valid_draft_2020_12(self):
        schemas = sorted(CONTRACTS.glob("*.schema.json"))
        self.assertEqual(len(schemas), 7)
        for path in schemas:
            with self.subTest(schema=path.name):
                Draft202012Validator.check_schema(validate.load_strict(path))

    def test_every_dispatch_target_exists(self):
        for name, file_name in validate.SCHEMA_FILES.items():
            with self.subTest(schema=name):
                self.assertTrue((CONTRACTS / file_name).is_file())


class ExampleTests(unittest.TestCase):
    def test_valid_messages_pass(self):
        paths = sorted((EXAMPLES / "messages" / "valid").glob("*.json"))
        self.assertGreaterEqual(len(paths), 20)
        for path in paths:
            with self.subTest(example=path.name):
                self.assertEqual(validate.check_document(path), [])

    def test_invalid_messages_fail_for_the_documented_reason(self):
        paths = {p.stem: p for p in (EXAMPLES / "messages" / "invalid").glob("*.json")}
        self.assertEqual(set(paths), set(EXPECTED_FAILURES))
        for name, reason in EXPECTED_FAILURES.items():
            with self.subTest(example=name):
                problems = validate.check_document(paths[name])
                self.assertTrue(problems, f"{name} unexpectedly passed")
                self.assertTrue(any(reason in problem for problem in problems), problems)

    def test_every_command_and_query_op_has_a_valid_example(self):
        schema = validate.load_strict(CONTRACTS / "game-command.schema.json")
        ops = set(schema["$defs"]["command_op"]["enum"]) | set(schema["$defs"]["query_op"]["enum"])
        seen = set()
        for path in (EXAMPLES / "messages" / "valid").glob("*.json"):
            document = validate.load_strict(path)
            if document["schema"] in ("enfractal.command", "enfractal.query"):
                seen.add(document["op"])
        missing = ops - seen - {"entity.place", "entity.remove", "creation.revise", "creation.activate", "protect.unlock",
                                "entity.inspect"}
        self.assertEqual(missing, set(), "add an example for each newly added op")

    def test_garage_example_room_and_state(self):
        self.assertEqual(validate.check_room(EXAMPLES / "rooms" / "garage_example"), [])
        state_path = EXAMPLES / "room_state" / "garage_example_state.json"
        state = validate.load_strict(state_path)
        self.assertEqual(validate.schema_errors(state), [])
        self.assertEqual(validate.check_state(state, "state", EXAMPLES / "rooms" / "garage_example"), [])

    def test_examples_are_reproducible(self):
        with tempfile.TemporaryDirectory() as temporary:
            out = Path(temporary)
            subprocess.run([sys.executable, "-I", str(EXAMPLES / "build_examples.py"), "--out", str(out)], check=True, capture_output=True)
            committed = {k: v for k, v in files_under(EXAMPLES).items() if not k.startswith("fixtures")}
            self.assertEqual(files_under(out), committed, "rerun contracts/examples/build_examples.py and commit the result")


class TestRoomTests(unittest.TestCase):
    def test_test_room_is_valid(self):
        self.assertEqual(validate.check_room(TEST_ROOM), [])

    def test_test_room_is_reproducible(self):
        with tempfile.TemporaryDirectory() as temporary:
            out = Path(temporary) / "test_room"
            subprocess.run([sys.executable, "-I", str(ROOT / "tools" / "rooms" / "build_test_room.py"), "--out", str(out)], check=True, capture_output=True)
            self.assertEqual(files_under(out), files_under(TEST_ROOM), "rerun tools/rooms/build_test_room.py and commit the result")

    def test_locked_presets_never_change(self):
        # A preset that is candidate, approved or retired is locked: its bytes are its identity, and saves pin them.
        # Tuning a locked preset means a new version file. Add each newly locked version here with its SHA-256.
        for path in sorted(PRESETS.glob("*/v*.json")):
            name = str(path.relative_to(PRESETS)).replace("\\", "/")
            status = validate.load_strict(path)["status"]
            with self.subTest(preset=name):
                if status in ("candidate", "approved", "retired"):
                    self.assertIn(name, LOCKED_PRESETS, f"{name} is {status}: record its SHA-256 in LOCKED_PRESETS")
                if name in LOCKED_PRESETS:
                    self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), LOCKED_PRESETS[name],
                                     f"{name} is locked and must never change; put the new numbers in a new version")
        for name in LOCKED_PRESETS:
            self.assertTrue((PRESETS / name).is_file(), f"locked preset {name} is missing")

    def test_shipped_presets_are_valid_and_versioned(self):
        presets = sorted(PRESETS.glob("*/v*.json"))
        self.assertTrue(presets)
        self.assertEqual(sorted(PRESETS.glob("*.json")), [], "presets live at game/styles/<preset_id>/v<N>.json")
        for path in presets:
            with self.subTest(preset=str(path.relative_to(PRESETS))):
                self.assertEqual(validate.check_document(path), [])
                document = validate.load_strict(path)
                self.assertEqual((document["preset_id"], f"v{document['preset_version']}.json"), (path.parent.name, path.name))


class SemanticTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.room = Path(self.temporary.name) / "test_room"
        shutil.copytree(TEST_ROOM, self.room)
        self.manifest = validate.load_strict(self.room / "room.json")

    def tearDown(self):
        self.temporary.cleanup()

    def rewrite(self, mutate):
        document = copy.deepcopy(self.manifest)
        mutate(document)
        write_json(self.room / "room.json", document)
        return validate.check_room(self.room)

    def assertProblem(self, problems, fragment):
        self.assertTrue(any(fragment in p for p in problems), problems)

    def test_tampered_asset_is_detected(self):
        asset = self.room / "objects" / "box_proxy" / "asset.json"
        asset.write_bytes(asset.read_bytes().replace(b'"mass_kg": 1.5', b'"mass_kg": 150.0'))
        self.assertProblem(validate.check_room(self.room), "hash does not match")

    def test_unlisted_file_reference_is_detected(self):
        self.assertProblem(self.rewrite(lambda d: d["files"].pop()), "is not in the files list")

    def test_inverted_wall_is_detected(self):
        def flip(d):
            d["shell"]["parts"][2]["geometry"]["points_m"].reverse()
        self.assertProblem(self.rewrite(flip), "faces away from the room interior")

    def test_non_planar_polygon_is_detected(self):
        def bend(d):
            d["shell"]["parts"][2]["geometry"]["points_m"][2][2] += 0.2
        self.assertProblem(self.rewrite(bend), "not planar")

    def test_spawn_outside_room_is_detected(self):
        def move(d):
            d["spawns"][0]["position_m"] = [9.0, 0.0, 0.0]
        self.assertProblem(self.rewrite(move), "outside the room bounds")

    def test_duplicate_ids_and_unknown_support_are_detected(self):
        def break_ids(d):
            d["objects"][1]["id"] = d["objects"][0]["id"]
            d["objects"][2]["support"] = {"kind": "object", "target_id": "obj:missing"}
        problems = self.rewrite(break_ids)
        self.assertProblem(problems, "must be unique")
        self.assertProblem(problems, "unknown entity obj:missing")

    def test_non_unit_quaternion_is_detected(self):
        def skew(d):
            d["objects"][0]["transform"]["rotation"] = [0.0, 0.5, 0.0, 0.5]
        self.assertProblem(self.rewrite(skew), "not a unit quaternion")

    def test_room_id_must_match_directory(self):
        self.assertProblem(self.rewrite(lambda d: d.update(room_id="other_room")), "must match its directory name")

    def test_state_rules(self):
        garage = EXAMPLES / "rooms" / "garage_example"
        base = validate.load_strict(EXAMPLES / "room_state" / "garage_example_state.json")
        self.assertEqual(validate.check_state(base, "state", garage), [])
        cases = {
            "cannot be saved as kind": lambda s: s["entities"].update({"shell:floor": dict(s["entities"]["obj:bean_bag"], id="shell:floor")}),
            "is not an object in the pinned room": lambda s: s["entities"].update({"obj:ghost": dict(s["entities"]["obj:bean_bag"], id="obj:ghost")}),
            "is outside the room bounds": lambda s: s["entities"]["obj:bean_bag"]["transform"].update(position_m=[999.0, -50.0, 999.0]),
            "checkpoint ids must be unique": lambda s: s["checkpoints"].append(dict(s["checkpoints"][0])),
            "lock is newer than the store revision": lambda s: s["entities"]["creation:00000001"]["protection"].update(locked_revision=40),
        }
        for fragment, mutate in cases.items():
            with self.subTest(rule=fragment):
                state = copy.deepcopy(base)
                mutate(state)
                self.assertProblem(validate.check_state(state, "state", garage), fragment)

    def test_failed_and_query_results_are_not_durable_receipts(self):
        state = validate.load_strict(EXAMPLES / "room_state" / "garage_example_state.json")
        receipt = next(iter(state["receipts"].values()))["result"]
        for change in ({"ok": False, "error": {"code": "internal_error", "message": "x", "retryable": False}}, {"op": "observe"}):
            with self.subTest(change=sorted(change)):
                broken = copy.deepcopy(state)
                next(iter(broken["receipts"].values()))["result"].update(change)
                self.assertTrue(validate.schema_errors(broken))
        self.assertTrue(receipt["ok"])

    def test_state_pin_mismatch_is_detected(self):
        state = validate.load_strict(EXAMPLES / "room_state" / "garage_example_state.json")
        state["room_id"] = "test_room"
        for receipt in state["receipts"].values():
            receipt["result"]["room_id"] = "test_room"
        self.assertProblem(validate.check_state(state, "state", self.room), "room_pin does not match")

    def test_style_pin_check(self):
        document = validate.load_strict(SEED_PRESET)
        good = {"preset_id": "storybook_painterly", "preset_version": document["preset_version"], "preset_sha256": validate.sha256_file(SEED_PRESET)}
        self.assertEqual(validate.check_style_pin(good, "pin"), [])
        self.assertProblem(validate.check_style_pin(dict(good, preset_sha256="0" * 64), "pin"), "must never change")
        self.assertProblem(validate.check_style_pin(dict(good, preset_version=99), "pin"), "not found")

    def test_preset_must_live_at_its_versioned_path(self):
        styles = Path(self.temporary.name) / "styles" / "wrong_name"
        styles.mkdir(parents=True)
        shutil.copy(SEED_PRESET, styles / "v1.json")
        self.assertProblem(validate.check_document(styles / "v1.json"), "styles/<preset_id>/v<preset_version>.json")

    def test_named_room_file_is_validated_not_its_sibling(self):
        def move(d):
            for spawn in d["spawns"]:
                spawn["position_m"] = [99.0, 0.0, 99.0]
        candidate = copy.deepcopy(self.manifest)
        move(candidate)
        write_json(self.room / "candidate.json", candidate)
        self.assertProblem(validate.check_document(self.room / "candidate.json"), "outside the room bounds")

    def test_asset_slot_pointing_at_another_document_is_reported_not_crashed(self):
        target = self.room / "objects" / "box_proxy" / "asset.json"
        shutil.copy(SEED_PRESET, target)
        self.assertProblem(validate.check_room(self.room), "expected 'enfractal.asset'")

    def test_path_aliases_are_rejected(self):
        for alias in ("objects/box_proxy/./asset.json", "objects/box_proxy//asset.json", "objects/../box_proxy/asset.json", "asset.json"):
            with self.subTest(alias=alias):
                def point(d, alias=alias):
                    d["objects"][1]["asset"] = alias
                self.assertProblem(self.rewrite(point), "does not match")

    def test_coordinates_are_bounded(self):
        def huge(d):
            d["bounds"]["max_m"][0] = 1e300
        self.assertProblem(self.rewrite(huge), "greater than the maximum of 1000")

    def test_external_glb_buffer_is_rejected(self):
        gltf = json.dumps({"asset": {"version": "2.0"}, "buffers": [{"uri": "payload.bin", "byteLength": 4}]}).encode()
        gltf += b" " * (-len(gltf) % 4)
        glb = struct.pack("<4sII", b"glTF", 2, 20 + len(gltf)) + struct.pack("<I4s", len(gltf), b"JSON") + gltf
        self.assertProblem(validate.check_glb(glb, "mesh.glb"), "references external file 'payload.bin'")
        self.assertProblem(validate.check_glb(b"not a mesh", "mesh.glb"), "not a binary glTF")

        def glb(text: bytes, padding: bytes = b" ", chunk_length=None):
            body = text + padding * (4 - len(text) % 4)
            declared = len(body) if chunk_length is None else chunk_length
            return struct.pack("<4sII", b"glTF", 2, 20 + len(body)) + struct.pack("<I4s", declared, b"JSON") + body

        self.assertProblem(validate.check_glb(glb(b'{"asset":{"version":"2.0"}}', chunk_length=0xFFFFFFFF), "mesh.glb"), "does not start with a JSON chunk")
        self.assertProblem(validate.check_glb(glb(b"[]"), "mesh.glb"), "not an object")
        self.assertProblem(validate.check_glb(glb(b'{"asset":{"version":"2.0"},"buffers":{}}'), "mesh.glb"), "is not an array")
        # The C# loader accepts the same NUL padding, so a room that validates here also loads in the game.
        self.assertEqual(validate.check_glb(glb(b'{"asset":{"version":"2.0"}}', padding=b"\x00"), "mesh.glb"), [])
        example = EXAMPLES / "rooms" / "garage_example" / "objects" / "bean_bag" / "mesh.glb"
        self.assertEqual(validate.check_glb(example.read_bytes(), "bean_bag"), [])

    def test_l_shaped_room_and_raised_platform_are_valid(self):
        self.assertEqual(validate.check_room(l_shaped_room(Path(self.temporary.name))), [])

    def test_reversed_inner_corner_wall_is_rejected(self):
        room = l_shaped_room(Path(self.temporary.name), reverse_wall="wall_de")
        self.assertProblem(validate.check_room(room), "wall_de winding faces away")

    def test_downward_floor_is_rejected(self):
        def flip(d):
            d["shell"]["parts"][0]["geometry"]["points_m"].reverse()
        self.assertProblem(self.rewrite(flip), "must face upward")

    def test_creation_source_size_limit(self):
        command = validate.load_strict(EXAMPLES / "messages" / "valid" / "command_creation_place_spinner.json")
        command["args"]["source"]["parts"][0]["material"] = "x" * 40000
        self.assertProblem(validate.schema_errors(command), "exceeds 32768 bytes")


# Emoji markers (VS15, VS16, the zero-width joiner, the keycap combiner) only where an emoji puts them.
# The same vectors are in companion/tests/test_text_rules.py and game/tests/native/RoomDataTest.cs.
EMOJI_ALLOWED = [
    ("heart, emoji presentation", "\u2764\ufe0f"),
    ("heart, text presentation", "\u2764\ufe0e"),
    ("smiley with a redundant VS16", "\U0001f600\ufe0f"),
    ("copyright sign as emoji", "\u00a9\ufe0f"),
    ("keycap one", "1\ufe0f\u20e3"),
    ("keycap hash without a selector", "#\u20e3"),
    ("keycap star", "*\ufe0f\u20e3"),
    ("digit, text presentation", "7\ufe0e"),
    ("family", "\U0001f468\u200d\U0001f469\u200d\U0001f467"),
    ("rainbow flag", "\U0001f3f3\ufe0f\u200d\U0001f308"),
    ("technologist, medium skin tone", "\U0001f469\U0001f3fd\u200d\U0001f4bb"),
    ("handshake, two skin tones", "\U0001faf1\U0001f3fb\u200d\U0001faf2\U0001f3fc"),
    ("pirate flag", "\U0001f3f4\u200d\u2620\ufe0f"),
    ("eye in speech bubble", "\U0001f441\ufe0f\u200d\U0001f5e8\ufe0f"),
    ("thumbs up, dark skin tone", "\U0001f44d\U0001f3ff"),
    ("flag of Japan", "\U0001f1ef\U0001f1f5"),
    ("a mug's name", "Mug \u2615\ufe0f"),
    ("letters and scripts", "Caf\u00e9 \u6728\u306e\u7bb1 \u05e2\u05d1\u05e8\u05d9\u05ea"),
]
EMOJI_REFUSED = [
    ("VS16 after a letter", "a\ufe0f"),
    ("VS15 at the start", "\ufe0eabc"),
    ("two selectors on one emoji", "\u2764\ufe0f\ufe0f"),
    ("text then emoji selector", "\u2764\ufe0e\ufe0f"),
    ("a selector after a skin tone", "\U0001f44d\U0001f3ff\ufe0f"),
    ("another variation selector after an emoji", "\u2764\ufe00"),
    ("VS14 after an emoji", "\u2764\ufe0d"),
    ("a supplement selector after an emoji", "\u2764\U000e0100"),
    ("a joiner between letters", "a\u200db"),
    ("a joiner at the end", "\U0001f600\u200d"),
    ("a joiner at the start", "\u200d\U0001f600"),
    ("two joiners", "\U0001f468\u200d\u200d\U0001f469"),
    ("a joiner before a letter", "\U0001f468\u200dx"),
    ("a joiner after a text selector", "\u2764\ufe0e\u200d\U0001f525"),
    ("a keycap on a letter", "A\u20e3"),
    ("a keycap alone", "\u20e3"),
    ("two keycaps", "1\u20e3\u20e3"),
    ("a keycap after a text selector", "1\ufe0e\u20e3"),
    ("a tag-sequence flag (England)", "\U0001f3f4\U000e0067\U000e0062\U000e0065\U000e006e\U000e0067\U000e007f"),
    ("smuggling in a run of selectors", "\U0001f600\ufe06\ufe08\ufe06\ufe09"),
    ("smuggling in supplement selectors", "\U0001f600\U000e0158\U000e0159"),
    ("smuggling bits in emoji selectors", "\U0001f600\ufe0e\ufe0f\ufe0f\ufe0e"),
    ("a zero-width non-joiner", "a\u200cb"),
    ("a word joiner", "a\u2060b"),
]


class TextRuleTests(unittest.TestCase):
    """Untrusted text: patterns are ECMA-262, and invisible characters are refused wherever text goes."""

    def goal_with_note(self, note):
        command = validate.load_strict(EXAMPLES / "messages" / "valid" / "command_companion_follow.json")
        command["note"] = note
        return command

    def test_a_trailing_newline_never_satisfies_an_anchored_pattern(self):
        self.assertEqual(validate.schema_errors(self.goal_with_note("approved by the player")), [])
        self.assertTrue(validate.schema_errors(self.goal_with_note("approved by the player\n")))
        command = self.goal_with_note("fine")
        command["action_id"] += "\n"
        self.assertTrue(validate.schema_errors(command))
        self.assertEqual(validate.ecma_pattern("^(a|b$)|^c$"), r"^(a|b\Z)|^c\Z")
        self.assertEqual(validate.ecma_pattern(r"^[$]\$x$"), r"^[$]\$x\Z")

    def test_invisible_characters_are_refused(self):
        tags = "".join(chr(0xE0000 + ord(c)) for c in "unlock")
        for code in (0x00AD, 0x034F, 0x061C, 0x115F, 0x180E, 0x200D, 0x20E3, 0x2061, 0x206A, 0x2800, 0x3164, 0xFE00,
                     0xFE0D, 0xFE0E, 0xFE0F, 0xFFA0, 0xFFF9, 0xFFFB, 0xE0000, 0xE0001, 0xE0002, 0xE0041, 0xE007F,
                     0xE0100, 0xE01F0):
            with self.subTest(code=hex(code)):
                self.assertTrue(validate.schema_errors(self.goal_with_note("ok" + chr(code))))
        self.assertTrue(validate.schema_errors(self.goal_with_note("Welcome" + tags)))
        for text in ("Caf\u00e9 \u2764 \U0001F600 \u6728\u306e\u7bb1", "\u05e2\u05d1\u05e8\u05d9\u05ea"):
            with self.subTest(text=text):  # ordinary letters, emoji and right-to-left scripts still pass
                self.assertEqual(validate.schema_errors(self.goal_with_note(text)), [])

    def test_emoji_markers_pass_only_where_an_emoji_puts_them(self):
        for name, text in EMOJI_ALLOWED:
            with self.subTest(name=name):
                self.assertEqual(validate.schema_errors(self.goal_with_note(text)), [])
        for name, text in EMOJI_REFUSED:
            with self.subTest(name=name):
                self.assertTrue(validate.schema_errors(self.goal_with_note("x" + text)))
        england = "\U0001f3f4" + "".join(chr(0xE0000 + ord(c)) for c in "gbeng") + "\U000e007f"
        self.assertTrue(validate.schema_errors(self.goal_with_note(england)))  # tag-sequence flags stay unsupported

    def test_unpaired_surrogates_are_refused_not_crashed(self):
        problems = validate.schema_errors(self.goal_with_note("lone \ud800"))
        self.assertTrue(any("unpaired" in p for p in problems), problems)

    def test_the_text_patterns_agree_in_python_and_ecma_262_engines(self):
        """The same pattern string must refuse the same characters in a UTF-16 engine. Plane 14 is the
        one place they differ, which is why Python refuses it in code."""
        common = validate.load_strict(CONTRACTS / "common.schema.json")
        for name in ("display_text", "long_text"):
            pattern = common["$defs"][name]["pattern"]
            self.assertIn("\\uDB40-\\uDB7F", pattern)
            self.assertIn("\\u00AD", pattern)
            self.assertIn("\\uFFF9-\\uFFFB", pattern)
            self.assertIn("\\uFE00-\\uFE0D", pattern)
            regex = re.compile(validate.ecma_pattern(pattern))
            for marker in ("\u2764\ufe0f", "\u2764\ufe0e", "\U0001f468\u200d\U0001f469", "1\u20e3"):
                self.assertIsNotNone(regex.search(marker), ascii(marker))  # context is checked in code
            for hidden in ("\ufe00", "\ufe0d", "\u200c", "\u200b", "\u2060"):
                self.assertIsNone(regex.search(hidden), ascii(hidden))


class StrictLoaderTests(unittest.TestCase):
    def check_rejects(self, raw: bytes, fragment: str):
        with tempfile.NamedTemporaryFile(suffix=".json", delete=False) as handle:
            handle.write(raw)
        try:
            with self.assertRaises(validate.ContractError) as caught:
                validate.load_strict(Path(handle.name))
            self.assertIn(fragment, str(caught.exception))
        finally:
            Path(handle.name).unlink()

    def test_rejects_non_finite_and_overflowing_numbers(self):
        self.check_rejects(b'{"a": NaN}\n', "non-finite")
        self.check_rejects(b'{"a": 1e400}\n', "overflows")

    def test_rejects_absurd_numbers_and_nesting_as_contract_errors(self):
        self.check_rejects(b'{"a": ' + b"9" * 5000 + b'}\n', "too long")
        self.check_rejects(b"[" * 100000 + b"]" * 100000 + b"\n", "nests too deeply")

    def test_rejects_bom_crlf_and_duplicates(self):
        self.check_rejects(b'\xef\xbb\xbf{"a": 1}\n', "byte-order mark")
        self.check_rejects(b'{"a": 1}\r\n', "CR line endings")
        self.check_rejects(b'{"a": 1, "a": 2}\n', "duplicate key")


class SiteAndLookTuningTests(unittest.TestCase):
    """The room `site` and the typed x_look_* blocks (Look review M5 and M6)."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.room = Path(self.temporary.name) / "test_room"
        shutil.copytree(TEST_ROOM, self.room)
        self.manifest = validate.load_strict(self.room / "room.json")
        self.preset = validate.load_strict(SEED_PRESET)

    def tearDown(self):
        self.temporary.cleanup()

    def site_problems(self, site):
        document = copy.deepcopy(self.manifest)
        document["site"] = site
        write_json(self.room / "room.json", document)
        return validate.check_room(self.room)

    def preset_problems(self, mutate):
        document = copy.deepcopy(self.preset)
        mutate(document["extensions"])
        path = Path(self.temporary.name) / "styles" / "storybook_painterly" / "v1.json"
        path.parent.mkdir(parents=True, exist_ok=True)
        write_json(path, document)
        return validate.check_document(path)

    def test_site_carries_no_location(self):
        self.assertEqual(self.manifest["site"], {"latitude_deg": 30, "neg_z_bearing_deg": 0, "solar_noon_h": 12})
        refused = {
            "a longitude": ({"longitude_deg": -3}, "was unexpected"),
            "a place name": ({"place": "anywhere"}, "was unexpected"),
            "a finer latitude": ({"latitude_deg": 45.5}, "is not of type 'integer'"),
            "a polar latitude": ({"latitude_deg": 70}, "greater than the maximum of 66"),
            "a solar noon off the quarter hour": ({"solar_noon_h": 12.1}, "is not a multiple of 0.25"),
        }
        for label, (change, fragment) in refused.items():
            with self.subTest(label):
                site = {**self.manifest["site"], **change}
                problems = self.site_problems(site)
                self.assertTrue(any(fragment in p for p in problems), problems)
        self.assertEqual(self.site_problems({"latitude_deg": -41, "neg_z_bearing_deg": 270, "solar_noon_h": 12.75}), [])

    def test_look_blocks_are_typed(self):
        def remove_hold(e):
            del e["x_look_seasons"]["hold"]

        def drop_summer(e):
            del e["x_look_seasons"]["looks"]["summer"]

        refused = {
            "an unknown field": (lambda e: e["x_look_gi"].__setitem__("glossiness", 1), "was unexpected"),
            "a missing field": (remove_hold, "'hold' is a required property"),
            "a bad colour": (lambda e: e["x_look_sky"].__setitem__("ground", "#12345"), "does not match"),
            "a calm of 3": (lambda e: e["x_look_role_marks"]["default"].__setitem__("calm", 3), "greater than the maximum of 1"),
            "a site field left in x_look_sun": (lambda e: e["x_look_sun"].__setitem__("latitude_deg", 30), "was unexpected"),
            "a missing season": (drop_summer, "'summer' is a required property"),
            "three shadow splits": (lambda e: e["x_look_shadows"].__setitem__("key_splits", 3), "is not one of [1, 2, 4]"),
            "a window cone of 90 degrees": (lambda e: e["x_look_lamps"].__setitem__("window_spot_angle_deg", 90), "greater than or equal to the maximum of 90"),
        }
        for label, (mutate, fragment) in refused.items():
            with self.subTest(label):
                problems = self.preset_problems(mutate)
                self.assertTrue(any(fragment in p for p in problems), problems)
        self.assertEqual(self.preset_problems(lambda e: e.__setitem__("x_other_experiment", {"anything": True})), [], "other x_ keys stay free")

    def test_typed_look_schema_matches_its_generator(self):
        result = subprocess.run([sys.executable, str(ROOT / "tools" / "look" / "make_look_schema.py"), "--check"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
