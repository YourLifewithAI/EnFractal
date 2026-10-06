"""Contract tests. Run from the repository root:

  python -m unittest discover -s contracts/tests -v
"""
from __future__ import annotations

import copy
import json
import shutil
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

# Each invalid example and the reason it must fail.
EXPECTED_FAILURES = {
    "command_with_principal": "'principal' was unexpected",
    "command_grab_without_target": "'target' is a required property",
    "command_fetch_without_target": "'target' is a required property",
    "command_effect_radius_too_large": "greater than the maximum of 10",
    "command_bad_entity_namespace": "does not match",
    "command_unknown_op": "is not one of",
    "command_lock_nothing": "should be non-empty",
    "command_companion_mints_approval": "'approved' was unexpected",
    "result_ok_with_error": "must not match",
    "result_failure_without_error": "'error' is a required property",
    "result_text_marked_trusted": "True was expected",
    "duplicate_keys": "duplicate key 'schema'",
}


def write_json(path: Path, document) -> None:
    path.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")


def files_under(root: Path) -> dict[str, bytes]:
    return {str(p.relative_to(root)): p.read_bytes() for p in sorted(root.rglob("*")) if p.is_file() and p.suffix != ".py"}


class SchemaTests(unittest.TestCase):
    def test_every_schema_is_valid_draft_2020_12(self):
        schemas = sorted(CONTRACTS.glob("*.schema.json"))
        self.assertEqual(len(schemas), 6)
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
                                "entity.inspect", "capabilities.list", "jobs.status"}
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

    def test_shipped_presets_are_valid(self):
        presets = sorted(PRESETS.glob("*.json"))
        self.assertTrue(presets)
        for path in presets:
            with self.subTest(preset=path.name):
                self.assertEqual(validate.check_document(path), [])
                self.assertEqual(validate.load_strict(path)["preset_id"], path.stem)


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

    def test_state_pin_mismatch_is_detected(self):
        state = validate.load_strict(EXAMPLES / "room_state" / "garage_example_state.json")
        state["room_id"] = "test_room"
        for receipt in state["receipts"].values():
            receipt["result"]["room_id"] = "test_room"
        self.assertProblem(validate.check_state(state, "state", self.room), "room_pin does not match")

    def test_style_pin_check(self):
        preset = PRESETS / "storybook_painterly.json"
        document = validate.load_strict(preset)
        good = {"preset_id": "storybook_painterly", "preset_version": document["preset_version"], "preset_sha256": validate.sha256_file(preset)}
        self.assertEqual(validate.check_style_pin(good, "pin"), [])
        stale = dict(good, preset_sha256="0" * 64)
        self.assertProblem(validate.check_style_pin(stale, "pin"), "hash does not match")


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

    def test_rejects_bom_crlf_and_duplicates(self):
        self.check_rejects(b'\xef\xbb\xbf{"a": 1}\n', "byte-order mark")
        self.check_rejects(b'{"a": 1}\r\n', "CR line endings")
        self.check_rejects(b'{"a": 1, "a": 2}\n', "duplicate key")


if __name__ == "__main__":
    unittest.main()
