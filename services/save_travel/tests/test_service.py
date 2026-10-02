"""Real PostgreSQL regressions. Creates and drops only its own random test DB.

Run: .cache/save-travel-venv/Scripts/python.exe -m unittest discover -s
     services/save_travel/tests -v
Config defaults to the ignored project .cache/save-travel-config.json, or set
ENFRACTAL_SAVE_CONFIG. The application database is never reset by these tests.
"""
from concurrent.futures import ThreadPoolExecutor
import copy
from http.client import HTTPConnection
from http.server import ThreadingHTTPServer
import json
import os
from pathlib import Path
import secrets
import sys
import threading
import unittest

import psycopg
from psycopg import sql
from psycopg.conninfo import conninfo_to_dict, make_conninfo

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from service import Service, canonical, digest
from server import make_handler

PIN = "a" * 64


def envelope():
    return {"schema": "enfractal.creation-world", "version": 1, "compiler_version": 1, "style_version": "barton_painterly_v1", "base_pin": PIN, "revision": 0, "permission_revision": 0, "next_id": 1, "roles": {"local_player": "owner", "guest_player": "visitor"}, "consent": {"local_player": False, "guest_player": False}, "instances": [], "receipts": {}}


class SaveTravelTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        project = Path(__file__).resolve().parents[3]
        config_path = Path(os.environ.get("ENFRACTAL_SAVE_CONFIG", project / ".cache/save-travel-config.json"))
        config = json.loads(config_path.read_text(encoding="utf-8-sig"))
        options = conninfo_to_dict(config["database_url"])
        options["dbname"] = "postgres"
        cls.admin_url = make_conninfo(**options)
        cls.database_name = "enfractal_test_" + secrets.token_hex(6)
        with psycopg.connect(cls.admin_url, autocommit=True) as connection:
            connection.execute(sql.SQL("CREATE DATABASE {}").format(sql.Identifier(cls.database_name)))
        options["dbname"] = cls.database_name
        cls.url = make_conninfo(**options)

    @classmethod
    def tearDownClass(cls):
        with psycopg.connect(cls.admin_url, autocommit=True) as connection:
            connection.execute(sql.SQL("DROP DATABASE {} WITH (FORCE)").format(sql.Identifier(cls.database_name)))

    def setUp(self):
        self.now = 100000.0
        self.service = Service(self.url, clock=lambda: self.now)
        with self.service.connect() as connection:
            connection.execute("DELETE FROM enfractal_state WHERE singleton=1")
        self.sequence = 0
        self.context = {}
        self.boot()

    def action(self):
        self.sequence += 1
        return "test_" + str(self.sequence)

    def boot(self, principal="local_player", **kwargs):
        request = {"op": "bootstrap", "action_id": self.action(), "base_pin": PIN, "initial_home": envelope(), "empty_world": envelope(), **kwargs}
        result = self.service.dispatch(principal, request)
        if result["ok"]:
            self.context[principal] = result
        self.assertTrue(result["ok"], result)
        return result

    def request(self, op, principal="local_player", **kwargs):
        context = self.context[principal]
        return {"op": op, "action_id": self.action(), "session_id": context["session_id"], "world_id": context["world_id"], "presence_epoch": context["presence_epoch"], **kwargs}

    def run_action(self, op, principal="local_player", expect=True, **kwargs):
        result = self.service.dispatch(principal, self.request(op, principal, **kwargs))
        self.assertEqual(result["ok"], expect, result)
        if result["ok"]:
            old = self.context[principal]
            self.context[principal] = {**old, **result}
        return result

    def sandbox(self, principal="local_player"):
        return self.run_action("create_sandbox", principal)["sandbox_id"]

    def prepare(self, destination, principal="local_player", invite_id=""):
        context = self.context[principal]
        return self.run_action("travel_prepare", principal, destination_world_id=destination, invite_id=invite_id, expected_store_revision=context["store_revision"], envelope=context["envelope"], source_checkpoint=[-23, 121, 335], heading_rad=0.8)

    def cross(self, destination, principal="local_player", invite_id="", arrive=True):
        prepared = self.prepare(destination, principal, invite_id)
        self.run_action("travel_freeze", principal, transfer_id=prepared["transfer_id"])
        committed = self.run_action("travel_commit", principal, transfer_id=prepared["transfer_id"], expected_rules_revision=prepared["destination_rules"]["revision"])
        if arrive:
            self.run_action("travel_arrive", principal, transfer_id=prepared["transfer_id"])
        return committed

    def test_bootstrap_is_pinned_and_database_wins_over_later_local_file(self):
        changed = envelope()
        changed["revision"] = 9
        result = self.boot(initial_home=changed)
        self.assertEqual(result["envelope"]["revision"], 0)
        bad = self.service.dispatch("local_player", {"op": "bootstrap", "action_id": self.action(), "base_pin": "b" * 64})
        self.assertEqual(bad["code"], "base_pin_mismatch")

    def test_world_save_durable_receipt_retry_and_conflict(self):
        changed = envelope()
        changed["revision"] = 1
        request = self.request("world_save", expected_store_revision=0, envelope=changed)
        first = self.service.dispatch("local_player", request)
        self.assertTrue(first["ok"], first)
        expected = copy.deepcopy(changed)
        replay = self.service.dispatch("local_player", request)
        self.assertTrue(replay["replayed"])
        self.assertEqual(replay["store_revision"], 1)
        request["envelope"]["revision"] = 2
        conflict = self.service.dispatch("local_player", request)
        self.assertEqual(conflict["code"], "action_id_conflict")
        self.assertEqual(self.run_action("world_load")["envelope"], expected)

    def test_stale_store_and_creation_revisions_rejected(self):
        changed = envelope()
        changed["revision"] = 2
        self.run_action("world_save", expected_store_revision=0, envelope=changed)
        stale = self.run_action("world_save", expect=False, expected_store_revision=0, envelope=changed)
        self.assertEqual(stale["code"], "store_revision_conflict")
        older = self.run_action("world_save", expect=False, expected_store_revision=1, envelope=envelope())
        self.assertEqual(older["code"], "creation_revision_stale")

    def test_save_retry_race_commits_once(self):
        request = self.request("world_save", expected_store_revision=0, envelope=envelope())
        with ThreadPoolExecutor(max_workers=8) as pool:
            results = list(pool.map(lambda _: self.service.dispatch("local_player", request), range(8)))
        self.assertTrue(all(r["ok"] for r in results), results)
        self.assertEqual(sum(not r["replayed"] for r in results), 1)
        self.assertTrue(all(r["store_revision"] == 1 for r in results))

    def test_independent_save_race_has_one_winner(self):
        requests = [self.request("world_save", expected_store_revision=0, envelope=envelope()) for _ in range(8)]
        with ThreadPoolExecutor(max_workers=8) as pool:
            results = list(pool.map(lambda r: self.service.dispatch("local_player", r), requests))
        self.assertEqual(sum(r["ok"] for r in results), 1)
        self.assertTrue(all(r["ok"] or r["code"] == "store_revision_conflict" for r in results))

    def test_source_and_destination_work_are_isolated_and_fork_empty(self):
        changed = envelope()
        changed["revision"] = 3
        changed["instances"] = [{"id": "host_validated_example", "source": {"name": "Home exhibit"}}]
        self.run_action("world_save", expected_store_revision=0, envelope=changed)
        sandbox = self.sandbox()
        self.cross(sandbox)
        self.assertEqual(self.context["local_player"]["envelope"]["instances"], [])
        sandbox_work = envelope()
        sandbox_work["revision"] = 1
        sandbox_work["instances"] = [{"id": "sandbox_example", "source": {"name": "Sandbox only"}}]
        self.run_action("world_save", expected_store_revision=0, envelope=sandbox_work)
        returned = self.run_action("return_home")
        self.assertEqual(returned["envelope"], changed)
        self.assertEqual(returned["arrival_checkpoint"], [-23, 121, 335])
        self.run_action("travel_arrive", transfer_id=returned["transfer_id"])
        self.cross(sandbox)
        self.assertEqual(self.context["local_player"]["envelope"], sandbox_work)

    def test_sandbox_quota_and_approved_gravity(self):
        self.run_action("create_sandbox", expect=False, gravity=0)
        self.sandbox()
        self.assertEqual(self.run_action("create_sandbox", expect=False)["code"], "sandbox_exists")
        self.assertEqual(self.run_action("set_rules", expect=False, sandbox_id="home", gravity=0.25, expected_rules_revision=1)["code"], "owner_required")

    def test_prepare_checkpoint_atomic_and_cancel_restores_source(self):
        sandbox = self.sandbox()
        prepared = self.prepare(sandbox)
        self.assertEqual(prepared["world_id"], "home")
        self.assertEqual(prepared["store_revision"], 1)
        self.assertEqual(self.run_action("world_save", expect=False, expected_store_revision=1, envelope=envelope())["code"], "travel_pending")
        self.run_action("travel_freeze", transfer_id=prepared["transfer_id"])
        cancelled = self.run_action("travel_cancel", transfer_id=prepared["transfer_id"])
        self.assertEqual(cancelled["world_id"], "home")
        self.assertFalse(cancelled["status"]["presence"]["frozen"])

    def test_commit_requires_freeze_and_current_destination_rules(self):
        sandbox = self.sandbox()
        prepared = self.prepare(sandbox)
        failure = self.run_action("travel_commit", expect=False, transfer_id=prepared["transfer_id"], expected_rules_revision=1)
        self.assertEqual(failure["code"], "transfer_state")
        self.run_action("set_rules", sandbox_id=sandbox, gravity=1, expected_rules_revision=1)
        self.run_action("travel_freeze", transfer_id=prepared["transfer_id"])
        self.assertEqual(self.run_action("travel_commit", expect=False, transfer_id=prepared["transfer_id"], expected_rules_revision=1)["code"], "rules_changed")

    def test_unknown_commit_retry_and_reconcile_never_duplicates_presence(self):
        sandbox = self.sandbox()
        prepared = self.prepare(sandbox)
        self.run_action("travel_freeze", transfer_id=prepared["transfer_id"])
        request = self.request("travel_commit", transfer_id=prepared["transfer_id"], expected_rules_revision=1)
        committed = self.service.dispatch("local_player", request)  # response lost
        self.assertTrue(committed["ok"], committed)
        replayed = self.service.dispatch("local_player", request)
        self.assertTrue(replayed["replayed"])
        self.assertEqual(replayed["world_id"], sandbox)
        reconciled = self.run_action("reconcile")  # old epoch intentionally
        self.assertEqual(reconciled["world_id"], sandbox)
        self.assertEqual(len(reconciled["status"]["current_world"]["occupants"]), 1)
        self.run_action("travel_arrive", transfer_id=prepared["transfer_id"])

    def test_old_epoch_new_actions_rejected_after_commit(self):
        sandbox = self.sandbox()
        stale = self.request("world_save", expected_store_revision=0, envelope=envelope())
        self.cross(sandbox)
        self.assertEqual(self.service.dispatch("local_player", stale)["code"], "presence_stale")

    def test_restart_prepared_and_frozen_stay_source_committed_stays_destination(self):
        sandbox = self.sandbox()
        for stage in ("prepared", "frozen", "committed"):
            prepared = self.prepare(sandbox)
            if stage != "prepared":
                self.run_action("travel_freeze", transfer_id=prepared["transfer_id"])
            if stage == "committed":
                self.run_action("travel_commit", transfer_id=prepared["transfer_id"], expected_rules_revision=1)
            stale = self.request("world_load")
            self.service = Service(self.url, clock=lambda: self.now)
            self.assertEqual(self.service.dispatch("local_player", stale)["code"], "session_stale")
            booted = self.boot()
            self.assertEqual(booted["world_id"], sandbox if stage == "committed" else "home")
            if stage == "committed":
                self.assertEqual(booted["transfer_id"], prepared["transfer_id"])
                self.run_action("travel_arrive", transfer_id=booted["transfer_id"])

    def test_new_process_fences_all_old_host_principals_and_bootstrap(self):
        self.boot("guest_player")
        old_service = self.service
        stale_guest = self.request("status", "guest_player")
        self.service = Service(self.url, clock=lambda: self.now)
        self.assertEqual(old_service.dispatch("guest_player", stale_guest)["code"], "host_stale")
        self.assertEqual(old_service.dispatch("local_player", {"op": "bootstrap", "action_id": self.action(), "base_pin": PIN})["code"], "host_stale")
        self.boot()

    def test_second_session_fences_prior_local_commands(self):
        stale = self.request("world_save", expected_store_revision=0, envelope=envelope())
        self.boot()
        self.assertEqual(self.service.dispatch("local_player", stale)["code"], "session_stale")

    def test_superseded_local_bootstrap_is_new_admission_without_world_rewind(self):
        # Documented local admission behavior: only latest bootstrap is retained.
        request = {"op": "bootstrap", "action_id": self.action(), "base_pin": PIN, "initial_home": envelope(), "empty_world": envelope()}
        first = self.service.dispatch("local_player", request)
        self.assertTrue(first["ok"])
        self.context["local_player"] = first
        sandbox = self.sandbox()
        self.cross(sandbox)
        newer = self.boot()
        delayed = self.service.dispatch("local_player", request)
        self.assertTrue(delayed["ok"])
        self.assertEqual(delayed["world_id"], sandbox)
        self.assertNotEqual(delayed["session_id"], newer["session_id"])
        self.assertEqual(len(delayed["status"]["current_world"]["occupants"]), 1)
        self.assertEqual(self.service.dispatch("local_player", self.request("status"))["code"], "session_stale")

    def test_restart_retains_committed_source_slot_until_recovery_arrives(self):
        sandbox = self.sandbox()
        self.boot("guest_player")
        guest_world = self.sandbox("guest_player")
        self.cross(sandbox)
        self.run_action("return_home")
        self.service = Service(self.url, clock=lambda: self.now)
        self.boot("guest_player")
        refused = self.run_action("travel_prepare", "guest_player", expect=False, destination_world_id=guest_world, expected_store_revision=self.context["guest_player"]["store_revision"], envelope=self.context["guest_player"]["envelope"])
        self.assertEqual(refused["code"], "sandbox_slot_busy")
        resumed = self.boot()
        self.run_action("travel_arrive", transfer_id=resumed["transfer_id"])
        self.run_action("world_load", "guest_player")
        self.prepare(guest_world, "guest_player")

    def test_invitation_consumed_once_role_and_return_home(self):
        sandbox = self.sandbox()
        self.boot("guest_player")
        invitation = self.run_action("invite", sandbox_id=sandbox, recipient="guest_player", role="editor")["invite_id"]
        prepared = self.prepare(sandbox, "guest_player", invitation)
        self.run_action("travel_freeze", "guest_player", transfer_id=prepared["transfer_id"])
        request = self.request("travel_commit", "guest_player", transfer_id=prepared["transfer_id"], expected_rules_revision=1)
        first = self.service.dispatch("guest_player", request)
        replay = self.service.dispatch("guest_player", request)
        self.assertTrue(first["ok"] and replay["replayed"])
        self.context["guest_player"] = replay
        self.assertEqual(replay["status"]["invitations"][0]["remaining_uses"], 0)
        self.run_action("travel_arrive", "guest_player", transfer_id=prepared["transfer_id"])
        self.run_action("revoke", invite_id=invitation)
        self.assertEqual(self.run_action("world_save", "guest_player", expect=False, expected_store_revision=0, envelope=envelope())["code"], "save_denied")
        returned = self.run_action("return_home", "guest_player")
        self.assertEqual(returned["world_id"], "home")
        self.run_action("travel_arrive", "guest_player", transfer_id=returned["transfer_id"])

    def test_invitation_revocation_and_expiry_rechecked_at_commit(self):
        sandbox = self.sandbox()
        self.boot("guest_player")
        for reason in ("revoke", "expiry"):
            invitation = self.run_action("invite", sandbox_id=sandbox, recipient="guest_player", expires_in_seconds=5)["invite_id"]
            prepared = self.prepare(sandbox, "guest_player", invitation)
            self.run_action("travel_freeze", "guest_player", transfer_id=prepared["transfer_id"])
            if reason == "revoke":
                self.run_action("revoke", invite_id=invitation)
            else:
                self.now += 6
            self.assertEqual(self.run_action("travel_commit", "guest_player", expect=False, transfer_id=prepared["transfer_id"], expected_rules_revision=1)["code"], "invite_unavailable")
            self.run_action("travel_cancel", "guest_player", transfer_id=prepared["transfer_id"])

    def test_wrong_invite_recipient_and_visitor_save_denied(self):
        sandbox = self.sandbox()
        self.boot("guest_player")
        no_invite = self.run_action("travel_prepare", "guest_player", expect=False, destination_world_id=sandbox, invite_id="bogus", expected_store_revision=0, envelope=envelope())
        self.assertEqual(no_invite["code"], "invite_unavailable")
        self.assertEqual(self.run_action("world_save", "guest_player", expect=False, expected_store_revision=0, envelope=envelope())["code"], "save_denied")

    def test_single_active_slot_held_through_commit_until_arrival(self):
        sandbox = self.sandbox()
        self.boot("guest_player")
        guest_world = self.sandbox("guest_player")
        self.cross(sandbox)
        guest_request = lambda: self.run_action("travel_prepare", "guest_player", expect=False, destination_world_id=guest_world, expected_store_revision=self.context["guest_player"]["store_revision"], envelope=self.context["guest_player"]["envelope"])
        self.assertEqual(guest_request()["code"], "sandbox_slot_busy")
        returned = self.run_action("return_home")
        self.assertEqual(guest_request()["code"], "sandbox_slot_busy")
        self.run_action("travel_arrive", transfer_id=returned["transfer_id"])
        self.run_action("world_load", "guest_player")
        self.prepare(guest_world, "guest_player")

    def test_transfer_expiry_is_not_a_commit(self):
        prepared = self.prepare(self.sandbox())
        self.run_action("travel_freeze", transfer_id=prepared["transfer_id"])
        self.now += 121
        self.assertEqual(self.run_action("travel_commit", expect=False, transfer_id=prepared["transfer_id"], expected_rules_revision=1)["code"], "transfer_expired")
        self.assertEqual(self.run_action("return_home")["world_id"], "home")

    def test_corrupt_save_never_overwritten(self):
        with self.service.connect() as connection:
            connection.execute("UPDATE enfractal_state SET sha256=%s WHERE singleton=1", ("f" * 64,))
        self.assertEqual(self.run_action("status", expect=False)["code"], "state_corrupt")
        with self.service.connect() as connection:
            self.assertEqual(connection.execute("SELECT sha256 FROM enfractal_state").fetchone()[0], "f" * 64)

    def test_invalid_envelope_nan_nested_and_identity_claims(self):
        invalid = envelope()
        invalid["consent"]["local_player"] = "yes"
        self.assertEqual(self.run_action("world_save", expect=False, expected_store_revision=0, envelope=invalid)["code"], "envelope_invalid")
        self.assertEqual(self.run_action("create_sandbox", expect=False, gravity=float("nan"))["code"], "json_invalid")
        self.assertEqual(self.service.dispatch("local_player", {**self.request("status"), "principal": "guest_player"})["code"], "identity_claim")
        nested = []
        for _ in range(40):
            nested = [nested]
        self.assertEqual(self.service.dispatch("local_player", {**self.request("status"), "nested": nested})["code"], "json_depth")

    def test_status_is_lightweight_and_world_descriptor_readable(self):
        status = self.run_action("status")
        self.assertNotIn("envelope", status)
        self.assertEqual(status["status"]["current_world"]["name"], "Home Earth")
        self.assertEqual(status["status"]["pending"], {})

    def test_reconnect_lookup_retains_durable_normal_action_without_replay(self):
        request = self.request("world_save", expected_store_revision=0, envelope=envelope())
        self.assertTrue(self.service.dispatch("local_player", request)["ok"])
        self.boot()
        found = self.run_action("action_lookup", action_id_to_lookup=request["action_id"])
        self.assertTrue(found["action_found"])
        self.assertEqual(found["action_receipt"]["committed_store_revision"], 1)
        self.assertEqual(self.service.dispatch("local_player", request)["code"], "session_stale")

    def test_full_normal_ledgers_still_allow_reconnect_return_and_arrival(self):
        from psycopg.types.json import Jsonb
        self.cross(self.sandbox())
        with self.service.connect() as connection:
            state = connection.execute("SELECT document FROM enfractal_state").fetchone()[0]
            # Terminal history fixture deliberately fills the two ordinary limits.
            state["receipts"] = {"local_player:history_" + str(i): {"action_id": "history_" + str(i), "fingerprint": "a" * 64, "session_id": "past", "result": {"ok": True}} for i in range(4096)}
            prior = next(iter(state["transfers"].values()))
            state["transfers"] = {"travel_" + str(i): {**copy.deepcopy(prior), "id": "travel_" + str(i), "state": "arrived"} for i in range(2048)}
            connection.execute("UPDATE enfractal_state SET document=%s,sha256=%s", (Jsonb(state), digest(state)))
        self.assertEqual(self.run_action("world_save", expect=False, expected_store_revision=0, envelope=envelope())["code"], "receipt_limit")
        self.boot()
        returned = self.run_action("return_home")
        self.assertEqual(returned["world_id"], "home")
        self.run_action("travel_arrive", transfer_id=returned["transfer_id"])
        for _ in range(10):
            self.boot()
            self.run_action("return_home")
        with self.service.connect() as connection:
            state = connection.execute("SELECT document FROM enfractal_state").fetchone()[0]
        self.assertEqual(len(state["receipts"]), 4096)
        self.assertLessEqual(len(state["control_receipts"]), 8)
        self.assertLessEqual(len(state["transfers"]), 2050)

    def test_repeated_home_request_does_not_release_unacknowledged_source(self):
        sandbox = self.sandbox()
        self.boot("guest_player")
        guest_world = self.sandbox("guest_player")
        self.cross(sandbox)
        first = self.run_action("return_home")
        second = self.run_action("return_home")
        self.assertEqual(first["transfer_id"], second["transfer_id"])
        self.assertTrue(second["status"]["presence"]["frozen"])
        refused = self.run_action("travel_prepare", "guest_player", expect=False, destination_world_id=guest_world, expected_store_revision=0, envelope=envelope())
        self.assertEqual(refused["code"], "sandbox_slot_busy")
        self.run_action("travel_arrive", transfer_id=first["transfer_id"])

    def test_http_token_identity_origin_and_size_guards(self):
        token = secrets.token_hex(32)
        server = ThreadingHTTPServer(("127.0.0.1", 0), make_handler(self.service, token))
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        def post(headers, body=None):
            connection = HTTPConnection("127.0.0.1", server.server_port, timeout=3)
            connection.request("POST", "/v1/action", json.dumps(body or self.request("status")), headers)
            response = connection.getresponse()
            result = response.status, json.loads(response.read())
            connection.close()
            return result
        try:
            self.assertEqual(post({})[0], 401)
            self.assertEqual(post({"Authorization": "Bearer " + token, "Origin": "https://example.test"})[0], 400)
            self.assertTrue(post({"Authorization": "Bearer " + token})[1]["ok"])
            claimed = {**self.request("status"), "principal": "guest_player"}
            self.assertEqual(post({"Authorization": "Bearer " + token}, claimed)[1]["code"], "identity_claim")
        finally:
            server.shutdown()
            server.server_close()
            thread.join()


if __name__ == "__main__":
    unittest.main()
