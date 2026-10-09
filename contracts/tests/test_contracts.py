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
    # Opaque job ids (Run 2): a counter never passes, padded or not.
    "query_jobs_status_counter_job_id": "'goal-000001' does not match",
    "query_jobs_status_padded_counter": "does not match '^job-[a-z2-7]{26}$'",
    "result_goal_with_counter_job_id": "'goal-000001' does not match",
    # The sandbox verbs (Run 2).
    "command_push_too_far": "greater than the maximum of 1",
    "result_release_that_names_what_lies_beneath": "False schema does not allow {'rests_on'",
    # The journal and the map (Run 2): the companion writes notes only, and nothing beside the answer.
    "command_journal_note_writes_a_fact": "('directed_by', 'kind' were unexpected)",
    "command_journal_note_too_long": "is too long",
    "command_journal_note_empty": "should be non-empty",
    "command_journal_note_with_injected_line": "does not match",
    "result_journal_note_posing_as_a_fact": "False schema does not allow \"Built a castle",
    "result_journal_fact_in_the_companions_words": "False schema does not allow 'The player said",
    "result_journal_fact_without_direction": "'directed_by' is a required property",
    "result_journal_open_task_that_is_done": "'active' was expected",
    "result_journal_read_without_data": "'data' is a required property",
    "result_journal_note_without_its_entry": "'data' is a required property",
    "query_map_find_with_nothing_to_find": "is not valid under any of the given schemas",
    "result_map_find_counting_the_unseen": "('not_yet_seen' was unexpected)",
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
        # Before Run 2 (no journal, nothing discovered) and after: both pin the same room and both validate.
        for name in ("garage_example_state.json", "garage_example_state_run2.json"):
            with self.subTest(state=name):
                state = validate.load_strict(EXAMPLES / "room_state" / name)
                self.assertEqual(validate.schema_errors(state), [])
                self.assertEqual(validate.check_state(state, "state", EXAMPLES / "rooms" / "garage_example"), [])

    def test_examples_are_reproducible(self):
        with tempfile.TemporaryDirectory() as temporary:
            out = Path(temporary)
            subprocess.run([sys.executable, "-I", str(EXAMPLES / "build_examples.py"), "--out", str(out)], check=True, capture_output=True)
            committed = {k: v for k, v in files_under(EXAMPLES).items() if not k.startswith("fixtures")}
            self.assertEqual(files_under(out), committed, "rerun contracts/examples/build_examples.py and commit the result")


COMMAND_SCHEMA = CONTRACTS / "game-command.schema.json"
RUN2_STATE = EXAMPLES / "room_state" / "garage_example_state_run2.json"
GARAGE = EXAMPLES / "rooms" / "garage_example"
# The room state example as written before Run 2 (commit 4b44357). Its bytes stay as they were and it stays valid:
# an old save needs no converter, and its reader treats the missing journal and map as empty.
PRE_RUN2_STATE_SHA256 = "9883c8a6da85cc801b6d3b916a168cd1929b30f043d691ffe30e8870fca7f2a5"
SANDBOX_VERBS = ("entity.grab", "entity.release", "entity.place", "entity.push")
FACT_FIELDS = ("line", "subject", "actor", "directed_by", "pin_m", "state", "quantity", "job_id")
NOTE_FIELDS = ("author", "text", "untrusted")


def valid_example(name: str) -> dict:
    return validate.load_strict(EXAMPLES / "messages" / "valid" / f"{name}.json")


def host_minted_job_id() -> str:
    import base64
    import secrets
    return "job-" + base64.b32encode(secrets.token_bytes(16)).decode("ascii").rstrip("=").lower()


class SandboxVerbTests(unittest.TestCase):
    """Pick up, carry, drop, push, place with snapping and stack: who may use each, and what a result may say."""

    def setUp(self):
        self.schema = validate.load_strict(COMMAND_SCHEMA)["$defs"]

    def test_the_verbs_and_the_journal_are_open_to_the_companion_and_only_two_ops_are_the_players(self):
        ops = set(self.schema["command_op"]["enum"]) | set(self.schema["query_op"]["enum"])
        self.assertLessEqual(set(SANDBOX_VERBS) | {"journal.note", "journal.read", "map.find"}, ops)
        self.assertEqual(set(self.schema["player_only_ops"]["enum"]), {"protect.unlock", "world.set_physics"})

    def test_verbs_done_by_a_body_name_it_as_an_avatar(self):
        # The adapter fills the companion's own avatar and refuses any other; the host refuses a companion directing the player.
        for op in ("entity.grab", "entity.release", "entity.push"):
            with self.subTest(op=op):
                self.assertEqual(self.schema["args"][op]["properties"]["actor"]["$ref"], "#/$defs/avatar_id")
        self.assertNotIn("actor", self.schema["args"]["entity.place"]["properties"])

    def test_a_verb_result_has_no_data_to_leak_through(self):
        for op in SANDBOX_VERBS:
            with self.subTest(op=op):
                document = {**valid_example("result_push_ok"), "op": op}
                self.assertEqual(validate.schema_errors(document), [])
                self.assertTrue(validate.schema_errors({**document, "data": {}}))
                self.assertTrue(validate.schema_errors({**document, "ok": False, "data": {"rests_on": "obj:hidden"},
                                                        "error": {"code": "invalid_args", "message": "x", "retryable": False}}))

    def test_placing_on_a_thing_stacks_on_it_and_drop_needs_no_placement(self):
        stacked = valid_example("command_release_stacked_on_bean_bag")
        self.assertEqual(stacked["args"]["placement"]["on"], "obj:bean_bag")
        self.assertEqual(valid_example("command_drop_what_you_hold")["args"], {})


class OpaqueJobIdTests(unittest.TestCase):
    def test_job_id_is_the_opaque_id_wherever_it_travels(self):
        defs = validate.load_strict(COMMAND_SCHEMA)["$defs"]
        opaque = "common.schema.json#/$defs/job_id"
        self.assertEqual(defs["result"]["properties"]["job_id"]["$ref"], opaque)
        self.assertEqual(defs["args"]["jobs.status"]["properties"]["job_id"]["$ref"], opaque)
        self.assertEqual(defs["data"]["jobs.status"]["properties"]["job_id"]["$ref"], opaque)
        self.assertEqual(defs["journal_entry"]["properties"]["job_id"]["$ref"], opaque)

    def test_counters_never_pass_and_host_minted_tokens_do(self):
        query = valid_example("query_jobs_status")
        counters = ["goal-000001", "1", "job-1", "job-42", "job-" + "0" * 26, "job-" + format(42, "026d"),
                    "job-" + format(4242, "026x"), "job-" + "9" * 26, "JOB-" + "a" * 26, "job-" + "A" * 26, "job-" + "a" * 25,
                    "job-" + "a" * 27, "job_" + "a" * 26]
        for job_id in counters:
            with self.subTest(job_id=job_id):
                self.assertTrue(validate.schema_errors({**query, "args": {"job_id": job_id}}))
        for _ in range(50):
            job_id = host_minted_job_id()
            self.assertEqual(validate.schema_errors({**query, "args": {"job_id": job_id}}), [], job_id)


class JournalAndMapTests(unittest.TestCase):
    """The AI writes only notes in its own words; facts are the host's; no result shape can say more than it should."""

    def setUp(self):
        self.schema = validate.load_strict(COMMAND_SCHEMA)
        self.read = valid_example("result_journal_read")
        self.note = next(e for e in self.read["data"]["entries"] if e["kind"] == "note")
        self.fact = next(e for e in self.read["data"]["entries"] if e["kind"] == "built")

    def with_entry(self, entry):
        return {**self.read, "data": {"open_tasks": [], "entries": [entry]}}

    def test_journal_note_takes_nothing_but_the_words(self):
        args = self.schema["$defs"]["args"]["journal.note"]
        self.assertEqual((set(args["properties"]), args["required"], args["additionalProperties"]), ({"text"}, ["text"], False))

    def test_no_command_takes_a_facts_fields(self):
        for op, args in self.schema["$defs"]["args"].items():
            with self.subTest(op=op):
                self.assertFalse(set(args.get("properties", {})) & {"line", "directed_by", "author", "untrusted", "quantity", "pin_m"})

    def test_a_note_never_passes_for_a_fact_and_a_fact_never_carries_words(self):
        self.assertEqual(validate.schema_errors(self.with_entry(self.note)), [])
        self.assertEqual(validate.schema_errors(self.with_entry(self.fact)), [])
        for name in FACT_FIELDS:
            with self.subTest(note_with=name):
                value = {"line": "Built a castle", "subject": self.fact["subject"], "actor": "companion:local", "directed_by": "player:local",
                         "pin_m": [0, 0, 0], "state": "done", "quantity": {"count": 1}, "job_id": host_minted_job_id()}[name]
                self.assertTrue(validate.schema_errors(self.with_entry({**self.note, name: value})))
        for name, value in (("author", "companion:local"), ("text", "my words"), ("untrusted", True)):
            with self.subTest(fact_with=name):
                self.assertTrue(validate.schema_errors(self.with_entry({**self.fact, name: value})))
        for kind in ("task", "built", "changed", "removed", "found", "gathered"):
            with self.subTest(note_as=kind):
                self.assertTrue(validate.schema_errors(self.with_entry({**self.note, "kind": kind})))
        untrusted_dropped = {k: v for k, v in self.note.items() if k != "untrusted"}
        self.assertTrue(validate.schema_errors(self.with_entry(untrusted_dropped)), "a note always says it is untrusted")

    def test_every_fact_says_what_happened_who_acted_and_at_whose_direction(self):
        for name in ("line", "actor", "directed_by"):
            with self.subTest(without=name):
                self.assertTrue(validate.schema_errors(self.with_entry({k: v for k, v in self.fact.items() if k != name})))
        self.assertTrue(validate.schema_errors(self.with_entry({**self.fact, "kind": "task"})), "a task has a state")
        self.assertTrue(validate.schema_errors(self.with_entry({**self.fact, "kind": "gathered"})), "gathering has a quantity")
        self.assertEqual(validate.schema_errors(self.with_entry({**self.fact, "kind": "gathered", "quantity": {"count": 6, "needed": 10}})), [])

    def test_journal_answers_are_newest_first_with_one_id_each(self):
        self.assertEqual(validate.check_message(self.read, "read"), [])
        reordered = copy.deepcopy(self.read)
        reordered["data"]["entries"].reverse()
        self.assertTrue(any("newest first" in p for p in validate.check_message(reordered, "read")))
        twice = copy.deepcopy(self.read)
        twice["data"]["entries"].append(copy.deepcopy(twice["data"]["entries"][-1]))
        self.assertTrue(any("appears twice" in p for p in validate.check_message(twice, "read")))

    def test_map_find_answers_nearest_first(self):
        found = valid_example("result_map_find")
        self.assertEqual(validate.check_message(found, "find"), [])
        far = copy.deepcopy(found["data"]["items"][0])
        near = {**copy.deepcopy(far), "entity": {**far["entity"], "id": "obj:bean_bag"}, "distance_m": 0.5}
        found["data"]["items"] = [far, near]
        self.assertEqual(validate.schema_errors(found), [])
        self.assertTrue(any("nearest first" in p for p in validate.check_message(found, "find")))

    def test_no_journal_or_map_answer_has_a_free_form_field(self):
        """Every object in these answers lists its fields: there is nowhere to put an undiscovered place or thing."""
        defs = self.schema["$defs"]
        common = validate.load_strict(CONTRACTS / "common.schema.json")["$defs"]

        def walk(node, where, seen, base=None):
            base = defs if base is None else base  # the $defs a '#/' reference in this node resolves against
            if isinstance(node, list):
                for item in node:
                    walk(item, where, seen, base)
                return
            if not isinstance(node, dict):
                return
            reference = node.get("$ref")
            if isinstance(reference, str) and reference not in seen:
                seen.add(reference)
                document, pointer = reference.split("#/$defs/")
                target_base = {"common.schema.json": common, "game-command.schema.json": defs, "": base}[document]
                target = target_base
                for part in pointer.split("/"):
                    target = target[part]
                walk(target, reference, seen, target_base)
            if node.get("type") == "object":
                if "properties" in node:
                    self.assertIs(node.get("additionalProperties"), False, f"{where}: an object with free-form fields")
                else:  # a map: its values are typed
                    self.assertIsInstance(node.get("additionalProperties"), dict, f"{where}: an untyped object")
            for key, value in node.items():
                if key not in ("$ref", "description", "if", "not"):  # conditions describe, they do not add fields
                    walk(value, where, seen, base)

        for name in ("journal.read", "journal.note", "map.find"):
            with self.subTest(data=name):
                walk(defs["data"][name], f"data/{name}", set())
        with self.subTest(data="the saved journal and map"):
            walk(validate.load_strict(CONTRACTS / "room-state.schema.json")["properties"]["journal"], "journal", set())


class RoomStateRun2Tests(unittest.TestCase):
    """Room state's journal and discovered blocks: bounded as JOURNAL.md says, and an older state stays valid."""

    def setUp(self):
        self.state = validate.load_strict(RUN2_STATE)

    def problems(self, mutate):
        state = copy.deepcopy(self.state)
        mutate(state)
        return validate.schema_errors(state) or validate.check_state(state, "state", GARAGE)

    def assertRefused(self, mutate, fragment):
        problems = self.problems(mutate)
        self.assertTrue(any(fragment in p for p in problems), problems)

    def test_a_room_state_written_before_run2_stays_valid(self):
        path = EXAMPLES / "room_state" / "garage_example_state.json"
        self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), PRE_RUN2_STATE_SHA256,
                         "the pre-Run-2 example must stay byte for byte as it was written")
        old = validate.load_strict(path)
        self.assertNotIn("journal", old)
        self.assertNotIn("discovered", old)
        self.assertEqual(validate.schema_errors(old), [])
        self.assertEqual(validate.check_state(old, "state", GARAGE), [])
        self.assertEqual(self.problems(lambda s: None), [])

    def test_the_saved_journal_keeps_facts_notes_and_open_tasks_apart(self):
        note = self.state["journal"]["notes"][0]
        task = self.state["journal"]["open_tasks"][0]
        self.assertRefused(lambda s: s["journal"]["history"].append(copy.deepcopy(note)), "must not match")
        self.assertRefused(lambda s: s["journal"]["notes"].append(copy.deepcopy(s["journal"]["history"][0])), "'note' was expected")
        self.assertRefused(lambda s: s["journal"]["history"].append(copy.deepcopy(task)), "must not match")
        self.assertRefused(lambda s: s["journal"]["open_tasks"][0].update(job_id=host_minted_job_id()), "False schema")
        self.assertRefused(lambda s: s["journal"]["notes"][0].update(text="a" * 281), "is too long")

    def test_the_journal_is_bounded(self):
        def grow(part, count, entry):
            def mutate(s):
                s["journal"][part] = [dict(copy.deepcopy(entry), entry_id=f"entry-{i:026d}".replace("0", "a").replace("1", "b")
                                           .replace("8", "c").replace("9", "d")) for i in range(count)]
            return mutate
        self.assertEqual(validate.schema_errors(self.mutated(grow("history", 500, self.state["journal"]["history"][0]))), [])
        self.assertRefused(grow("history", 501, self.state["journal"]["history"][0]), "is too long")
        self.assertRefused(grow("notes", 101, self.state["journal"]["notes"][0]), "is too long")
        self.assertRefused(grow("open_tasks", 33, self.state["journal"]["open_tasks"][0]), "is too long")

    def mutated(self, mutate):
        state = copy.deepcopy(self.state)
        mutate(state)
        return state

    def test_the_discovered_map_is_bounded(self):
        bean = self.state["discovered"]["entities"]["obj:bean_bag"]

        def known(count):
            def mutate(s):
                s["discovered"]["entities"] = {f"creation:c{i:04d}": dict(copy.deepcopy(bean), entity=dict(bean["entity"], id=f"creation:c{i:04d}", kind="creation"))
                                               for i in range(count)}
            return mutate
        self.assertEqual(validate.schema_errors(self.mutated(known(1024))), [])
        self.assertRefused(known(1025), "has too many properties")
        self.assertRefused(lambda s: s["discovered"].update(cell_m=0.001), "less than the minimum of 0.01")
        self.assertRefused(lambda s: s["discovered"]["levels"][0].update(columns=2048), "greater than the maximum of 1024")

    def test_the_discovered_map_holds_only_what_the_team_saw_as_it_saw_it(self):
        self.assertRefused(lambda s: s["discovered"]["entities"].update({"shell:floor": copy.deepcopy(s["discovered"]["entities"]["obj:bean_bag"])}),
                           "does not match")
        self.assertRefused(lambda s: s["discovered"]["entities"]["obj:bean_bag"]["entity"].update(held_by="avatar:player"), "False schema")
        self.assertRefused(lambda s: s["discovered"]["entities"]["obj:bean_bag"]["entity"].update(seen="now"), "False schema")
        self.assertRefused(lambda s: s["discovered"]["entities"].update({"obj:ghost": copy.deepcopy(s["discovered"]["entities"]["obj:bean_bag"])}),
                           "does not match its summary's id")
        self.assertRefused(lambda s: s["discovered"]["entities"]["obj:bean_bag"]["entity"].update(kind="creation"), "cannot be of kind")
        self.assertRefused(lambda s: s["discovered"]["entities"]["obj:bean_bag"].update(last_seen_revision=40), "seen after the store revision")

        def ghost(s):
            s["discovered"]["entities"]["obj:ghost"] = copy.deepcopy(s["discovered"]["entities"]["obj:bean_bag"])
            s["discovered"]["entities"]["obj:ghost"]["entity"]["id"] = "obj:ghost"
        self.assertRefused(ghost, "is not an object in the pinned room")

    def test_saved_journal_entries_are_unique_ordered_and_not_from_the_future(self):
        self.assertRefused(lambda s: s["journal"]["notes"].append(copy.deepcopy(s["journal"]["notes"][0])), "appears twice")
        self.assertRefused(lambda s: s["journal"]["history"].reverse(), "oldest first")
        self.assertRefused(lambda s: s["journal"]["notes"][0].update(revision=40), "newer than the store revision")

    def test_discovered_cells_are_a_bitmap_of_exactly_their_grid(self):
        import base64
        floor = self.state["discovered"]["levels"][0]
        self.assertEqual(len(base64.b64decode(floor["cells"])), (floor["columns"] * floor["rows"] + 7) // 8)
        self.assertRefused(lambda s: s["discovered"]["levels"][0].update(rows=111), "need")
        self.assertRefused(lambda s: s["discovered"]["levels"][0].update(cells="not base64!"), "does not match")

        def odd_grid_with_a_stray_bit(s):
            s["discovered"]["levels"][1].update(columns=3, rows=3, cells=base64.b64encode(bytes([0xFF, 0x03])).decode("ascii"))
        self.assertRefused(odd_grid_with_a_stray_bit, "unused bits")
        self.assertRefused(lambda s: s["discovered"]["levels"].append(copy.deepcopy(s["discovered"]["levels"][0])), "listed twice")
        self.assertRefused(lambda s: s["discovered"]["levels"][0].update(min_xz_m=[50.0, 0.0]), "beyond the room bounds")
        self.assertRefused(lambda s: s["discovered"]["levels"][1].update(support="obj:ghost"), "object the pinned room does not have")
        self.assertRefused(lambda s: s["discovered"]["levels"][0].update(support="shell:attic"), "shell part the pinned room does not have")


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

    def test_collision_only_part(self):
        def hide(d):
            d["shell"]["parts"][-1]["drawn"] = False
        self.assertEqual(self.rewrite(hide), [])

        def hide_and_ghost(d):
            hide(d)
            d["shell"]["parts"][-1]["collides"] = False
        self.assertProblem(self.rewrite(hide_and_ghost), "neither drawn nor collides")

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
            "two shadow splits written as 2.0": (lambda e: e["x_look_shadows"].__setitem__("key_splits", 2.0), "is not of type 'integer'"),
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
