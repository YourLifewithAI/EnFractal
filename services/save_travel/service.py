"""Transactional local save/travel control plane. Game compilation remains in Godot.

The HTTP adapter binds identity. This module also admits a second explicit test
fixture identity; callers must never take ``principal`` from untrusted JSON.
"""
from __future__ import annotations

import copy
import hashlib
import json
import math
from pathlib import Path
import re
import secrets
import time

import psycopg
from psycopg.types.json import Jsonb

MAX_ENVELOPE_BYTES = 4 * 1024 * 1024
MAX_REQUEST_BYTES = MAX_ENVELOPE_BYTES + 65536  # envelope plus bounded transport fields
MAX_STATE_BYTES = 16 * 1024 * 1024
MAX_RECEIPTS = 4096
PRINCIPALS = {"local_player", "guest_player"}
TOKEN = re.compile(r"^[A-Za-z0-9_-]{1,96}$")
READS = {"status", "world_load", "reconcile", "action_lookup"}
RECOVERY = {"bootstrap", "return_home", "travel_arrive", "travel_cancel"}
MUTATIONS = {"bootstrap", "world_save", "create_sandbox", "set_rules", "invite", "revoke", "travel_prepare", "travel_freeze", "travel_commit", "travel_arrive", "travel_cancel", "return_home", "disconnect"}


class Rejected(Exception):
    def __init__(self, code, message):
        self.code, self.message = code, message
        super().__init__(message)


def reject(code, message):
    raise Rejected(code, message)


def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False)


def digest(value):
    return hashlib.sha256(canonical(value).encode("utf-8")).hexdigest()


def bounded_json(value, limit):
    def walk(item, depth=0):
        if depth > 32:
            reject("json_depth", "The document is nested too deeply.")
        if item is None or isinstance(item, (str, bool, int)):
            return
        if isinstance(item, float) and math.isfinite(item):
            return
        if isinstance(item, list):
            for child in item:
                walk(child, depth + 1)
            return
        if isinstance(item, dict) and all(isinstance(key, str) for key in item):
            for child in item.values():
                walk(child, depth + 1)
            return
        reject("json_invalid", "Use finite JSON values and string object keys.")
    walk(value)
    if len(canonical(value).encode("utf-8")) > limit:
        reject("size_limit", "This document exceeds its storage allowance.")


def integer(value, minimum=0, maximum=2**53 - 1):
    return not isinstance(value, bool) and isinstance(value, (int, float)) and math.isfinite(value) and value == int(value) and minimum <= value <= maximum


def validate_envelope(envelope, base_pin, empty=False):
    bounded_json(envelope, MAX_ENVELOPE_BYTES)
    keys = {"schema", "version", "compiler_version", "style_version", "base_pin", "revision", "permission_revision", "next_id", "roles", "consent", "instances", "receipts"}
    if not isinstance(envelope, dict) or set(envelope) != keys:
        reject("envelope_invalid", "The saved creation envelope has an unsupported shape.")
    if envelope["schema"] != "enfractal.creation-world" or envelope["version"] != 1 or envelope["compiler_version"] != 1 or envelope["style_version"] != "barton_painterly_v1" or envelope["base_pin"] != base_pin:
        reject("envelope_pin", "The saved creation envelope does not match this base or compiler.")
    if not all(integer(envelope[k]) for k in ("revision", "permission_revision", "next_id")) or envelope["next_id"] < 1:
        reject("envelope_invalid", "Saved creation revisions are invalid.")
    if not isinstance(envelope["instances"], list) or len(envelope["instances"]) > 16 or not isinstance(envelope["receipts"], dict) or len(envelope["receipts"]) > 2048:
        reject("envelope_invalid", "Saved creation counts exceed the host contract.")
    if not isinstance(envelope["roles"], dict) or set(envelope["roles"]) != PRINCIPALS or envelope["roles"]["local_player"] != "owner" or envelope["roles"]["guest_player"] not in ("visitor", "editor"):
        reject("envelope_invalid", "Saved local host roles are invalid.")
    if not isinstance(envelope["consent"], dict) or set(envelope["consent"]) != PRINCIPALS or not all(isinstance(v, bool) for v in envelope["consent"].values()):
        reject("envelope_invalid", "Saved consent flags are invalid.")
    if empty and (envelope["instances"] or envelope["receipts"] or envelope["revision"] or envelope["permission_revision"] or envelope["next_id"] != 1 or any(envelope["consent"].values())):
        reject("empty_world_invalid", "A sandbox must start with the empty pinned base, without inventions or grants.")


class Service:
    def __init__(self, database_url, *, clock=time.time, boot_id=None):
        self.database_url = database_url
        self.clock = clock
        self.boot_id = boot_id or secrets.token_hex(16)
        with self.connect() as connection:
            connection.execute("SELECT pg_advisory_xact_lock(138221204)")
            connection.execute(Path(__file__).with_name("migrations").joinpath("001_initial.sql").read_text(encoding="utf-8"))
            if connection.execute("SELECT version FROM enfractal_meta WHERE singleton=1").fetchone() != (1,):
                reject("migration_unknown", "This database requires a supported migration.")
            connection.execute("UPDATE enfractal_meta SET host_fence=%s WHERE singleton=1", (self.boot_id,))

    def connect(self):
        return psycopg.connect(self.database_url, connect_timeout=5, options="-c statement_timeout=10000 -c lock_timeout=5000 -c synchronous_commit=on")

    def dispatch(self, principal, request):
        try:
            if principal not in PRINCIPALS:
                reject("principal_unknown", "The host has not admitted this account.")
            bounded_json(request, MAX_REQUEST_BYTES)
            if not isinstance(request, dict) or request.get("op") not in READS | MUTATIONS:
                reject("operation_invalid", "Choose a supported save or travel operation.")
            if any(k in request for k in ("principal", "owner_id", "role_override", "boot_id")):
                reject("identity_claim", "Account identity is supplied by the local host adapter.")
            with self.connect() as connection:
                connection.execute("SELECT pg_advisory_xact_lock(138221205)")
                host = connection.execute("SELECT host_fence FROM enfractal_meta WHERE singleton=1 FOR SHARE").fetchone()
                if host != (self.boot_id,):
                    reject("host_stale", "A newer local save host owns this database. Reconnect to that host.")
                row = connection.execute("SELECT document,sha256 FROM enfractal_state WHERE singleton=1 FOR UPDATE").fetchone()
                if row:
                    state = row[0]
                    self.validate_state(state, row[1])
                elif request["op"] == "bootstrap":
                    state = self.initial(request)
                else:
                    reject("not_initialized", "Open the saved world before sending commands.")
                result = self.apply(state, principal, request)
                if request["op"] in MUTATIONS:
                    bounded_json(state, MAX_STATE_BYTES if request["op"] in RECOVERY else MAX_STATE_BYTES - 65536)
                    connection.execute("INSERT INTO enfractal_state(singleton,document,sha256) VALUES(1,%s,%s) ON CONFLICT(singleton) DO UPDATE SET document=EXCLUDED.document,sha256=EXCLUDED.sha256", (Jsonb(state), digest(state)))
            # Context manager commits before an acknowledged result leaves dispatch.
            return result
        except Rejected as error:
            return {"ok": False, "code": error.code, "message": error.message}
        except (ValueError, TypeError, KeyError, OverflowError, RecursionError):
            return {"ok": False, "code": "data_invalid", "message": "This request or stored state could not be validated. Nothing was overwritten."}
        except psycopg.Error:
            # No SQL, database URL, server diagnostics or credentials in client errors.
            return {"ok": False, "code": "database_unavailable", "message": "The save service could not confirm a durable commit. Reconnect and reconcile before trying another action."}

    def initial(self, request):
        pin = request.get("base_pin")
        if not isinstance(pin, str) or not re.fullmatch("[0-9a-f]{64}", pin):
            reject("base_pin_invalid", "A SHA256 pinned base is required.")
        validate_envelope(request.get("initial_home"), pin)
        validate_envelope(request.get("empty_world"), pin, empty=True)
        return {"schema": "enfractal.save-travel", "version": 1, "base_pin": pin, "empty_world": copy.deepcopy(request["empty_world"]), "worlds": {"home": self.new_world("home", "Home Earth", "local_player", 1.0, request["initial_home"])}, "presence": {}, "transfers": {}, "invites": {}, "receipts": {}, "control_receipts": {}, "sequence": 1}

    def new_world(self, world_id, name, owner, gravity, envelope):
        return {"id": world_id, "name": name, "owner_id": owner, "base_pin": envelope["base_pin"], "rules": {"revision": 1, "gravity_scale": gravity, "creation_compiler": 1, "style_version": "barton_painterly_v1"}, "store_revision": 0, "envelope": copy.deepcopy(envelope), "envelope_sha256": digest(envelope), "checkpoints": {}, "grants": {owner: "owner"}}

    def validate_state(self, state, checksum):
        bounded_json(state, MAX_STATE_BYTES)
        if digest(state) != checksum or state.get("schema") != "enfractal.save-travel" or state.get("version") != 1:
            reject("state_corrupt", "The saved control plane failed its version or checksum check. Restore a verified backup.")
        if not isinstance(state.get("worlds"), dict) or "home" not in state["worlds"] or len(state["worlds"]) > 3:
            reject("state_corrupt", "The saved world registry is invalid.")
        validate_envelope(state["empty_world"], state["base_pin"], empty=True)
        for world_id, world in state["worlds"].items():
            if world["id"] != world_id or world["base_pin"] != state["base_pin"] or world["owner_id"] not in PRINCIPALS or not integer(world["store_revision"]) or world["rules"]["gravity_scale"] not in (0.25, 1.0) or not integer(world["rules"]["revision"], 1):
                reject("state_corrupt", "A saved world's identity, rules or revision is invalid.")
            validate_envelope(world["envelope"], state["base_pin"])
            if digest(world["envelope"]) != world["envelope_sha256"]:
                reject("state_corrupt", "A saved creation envelope failed its checksum check.")
        if state["worlds"]["home"]["rules"]["gravity_scale"] != 1.0:
            reject("state_corrupt", "Home Earth's common gravity may not be changed.")
        for principal, presence in state["presence"].items():
            if principal not in PRINCIPALS or presence["world_id"] not in state["worlds"] or not integer(presence["epoch"], 1):
                reject("state_corrupt", "Saved account presence is invalid.")
        if len(state["receipts"]) > MAX_RECEIPTS or len(state.get("control_receipts", {})) > 8 or len(state["invites"]) > 128 or len(state["transfers"]) > 2050:
            reject("state_corrupt", "Saved control records exceed their limits.")

    def apply(self, state, principal, request):
        op = request["op"]
        presence = state["presence"].get(principal)
        if op != "bootstrap":
            if not presence or request.get("session_id") != presence["session_id"] or presence["boot_id"] != self.boot_id or not presence["active"]:
                reject("session_stale", "Reconnect to establish a fresh local session.")
        receipt_key = None
        ledger = state["receipts"]
        if op in MUTATIONS:
            action = request.get("action_id")
            if not isinstance(action, str) or not TOKEN.fullmatch(action):
                reject("action_id_invalid", "A bounded unique action ID is required.")
            receipt_key = principal + ":" + action
            if op in RECOVERY:
                # Four fixed recovery slots per admitted account keep reconnect,
                # cancellation and Return Home usable when the normal ledger is
                # full. Superseded recovery commands are fenced by epoch/session.
                ledger = state.setdefault("control_receipts", {})
                receipt_key = principal + ":" + op
            previous = ledger.get(receipt_key)
            if previous and previous.get("action_id", action) != action:
                previous = None
            if previous:
                if previous["fingerprint"] != digest(request):
                    reject("action_id_conflict", "This action ID was already used for another request.")
                if op == "bootstrap" and (not presence or previous["session_id"] != presence["session_id"] or presence["boot_id"] != self.boot_id):
                    reject("session_stale", "That startup belongs to an earlier session. Start a new connection.")
                return self.response(state, principal, {**copy.deepcopy(previous["result"]), "replayed": True}, include_envelope=op not in {"status", "world_save"})
            if op not in RECOVERY and len(state["receipts"]) >= MAX_RECEIPTS:
                reject("receipt_limit", "This local pilot's durable action ledger is full. Export it before starting a new pilot database.")
        if op not in ("bootstrap", "reconcile", "status", "action_lookup"):
            if request.get("world_id") != presence["world_id"] or request.get("presence_epoch") != presence["epoch"]:
                reject("presence_stale", "This command belongs to an earlier world or travel epoch.")
        handler = getattr(self, "op_" + op)
        extra = handler(state, principal, request)
        if receipt_key:
            ledger[receipt_key] = {"action_id": request["action_id"], "fingerprint": digest(request), "session_id": state["presence"][principal]["session_id"], "result": copy.deepcopy(extra)}
        return self.response(state, principal, {**extra, "replayed": False}, include_envelope=op not in {"status", "world_save"})

    def active(self, presence):
        return presence.get("active") and presence.get("boot_id") == self.boot_id

    def pending(self, state, principal):
        return next((t for t in state["transfers"].values() if t["principal"] == principal and t["state"] in ("prepared", "frozen", "committed")), None)

    def ensure_slot(self, state, destination, principal):
        if destination == "home":
            return
        occupied = {p["world_id"] for who, p in state["presence"].items() if who != principal and self.active(p) and p["world_id"] != "home"}
        reserved = {t["destination"] for t in state["transfers"].values() if t["principal"] != principal and t["state"] in ("prepared", "frozen") and t["boot_id"] == self.boot_id and t["destination"] != "home"}
        # A committed source still owns its worker until arrival acknowledges
        # teardown. Neither a retry nor a changed presence may release it early.
        reserved |= {t["source"] for t in state["transfers"].values() if t["principal"] != principal and t["state"] == "committed" and t["source"] != "home"}
        if any(world != destination for world in occupied | reserved):
            reject("sandbox_slot_busy", "Another sandbox is active. Return its occupants Home before opening this one.")
        occupants = sum(1 for who, p in state["presence"].items() if who != principal and self.active(p) and p["world_id"] == destination)
        reservations = sum(1 for t in state["transfers"].values() if t["principal"] != principal and t["state"] in ("prepared", "frozen") and t["boot_id"] == self.boot_id and t["destination"] == destination)
        if occupants + reservations >= 4:
            reject("world_full", "This sandbox has reserved all four places.")

    def id(self, state, prefix):
        number = state["sequence"]
        state["sequence"] += 1
        return f"{prefix}_{number}"

    def op_bootstrap(self, state, principal, request):
        if request.get("base_pin") != state["base_pin"]:
            reject("base_pin_mismatch", "This database belongs to another pinned map. Its saved work was not changed.")
        # Initial local files are migration inputs only; existing database wins.
        old = state["presence"].get(principal)
        destination = old["world_id"] if old else "home"
        self.ensure_slot(state, destination, principal)
        new_session = secrets.token_hex(24)
        pending_id = ""
        for transfer in state["transfers"].values():
            if transfer["principal"] == principal:
                if transfer["state"] in ("prepared", "frozen"):
                    transfer["state"] = "cancelled"
                elif transfer["state"] == "committed":
                    # Reconnect adopts the committed destination. The source slot
                    # stays held until the new client loads it and acknowledges.
                    transfer["session_id"] = new_session
                    transfer["boot_id"] = self.boot_id
                    pending_id = transfer["id"]
        state["presence"][principal] = {"session_id": new_session, "boot_id": self.boot_id, "world_id": destination, "epoch": (old["epoch"] + 1) if old else 1, "active": True, "frozen": bool(pending_id)}
        result = {"migration_used": old is None and len(state["presence"]) == 1}
        if pending_id:
            result["transfer_id"] = pending_id
        return result

    def op_status(self, state, principal, request):
        return {}

    op_world_load = op_status
    op_reconcile = op_status

    def op_action_lookup(self, state, principal, request):
        action = request.get("action_id_to_lookup")
        if not isinstance(action, str) or not TOKEN.fullmatch(action):
            reject("action_id_invalid", "Choose the original action ID to inspect.")
        receipt = state["receipts"].get(principal + ":" + action)
        if not receipt:
            receipt = next((r for key, r in state.get("control_receipts", {}).items() if key.startswith(principal + ":") and r.get("action_id") == action), None)
        return {"action_found": bool(receipt), "action_receipt": copy.deepcopy(receipt["result"]) if receipt else {}}

    def require_unfrozen(self, state, principal):
        if self.pending(state, principal):
            reject("travel_pending", "Finish or cancel the current crossing before changing this world.")

    def save(self, state, principal, request):
        world = state["worlds"][state["presence"][principal]["world_id"]]
        role = world["grants"].get(principal, "visitor")
        if role not in ("owner", "editor"):
            reject("save_denied", "A visitor cannot change this world's saved inventions.")
        if request.get("expected_store_revision") != world["store_revision"]:
            reject("store_revision_conflict", "This world's saved work changed. Reload before confirming.")
        envelope = request.get("envelope")
        validate_envelope(envelope, state["base_pin"])
        if envelope["revision"] < world["envelope"]["revision"] or envelope["permission_revision"] < world["envelope"]["permission_revision"]:
            reject("creation_revision_stale", "The creation host supplied an older save revision.")
        world["envelope"] = copy.deepcopy(envelope)
        world["envelope_sha256"] = digest(envelope)
        world["store_revision"] += 1
        return world

    def checkpoint(self, world, principal, request):
        point = request.get("source_checkpoint", [-22.0, 0.0, 340.0])
        heading = request.get("heading_rad", 0.0)
        if not isinstance(point, list) or len(point) != 3 or any(isinstance(v, bool) or not isinstance(v, (float, int)) or not math.isfinite(v) or abs(v) > 10000 for v in point) or isinstance(heading, bool) or not isinstance(heading, (float, int)) or not math.isfinite(heading) or abs(heading) > 100000:
            reject("checkpoint_invalid", "The source checkpoint must be a finite bounded position and heading.")
        world["checkpoints"][principal] = {"position": point, "heading_rad": heading}

    def op_world_save(self, state, principal, request):
        self.require_unfrozen(state, principal)
        world = self.save(state, principal, request)
        if "source_checkpoint" in request:
            self.checkpoint(world, principal, request)
        return {"committed_store_revision": world["store_revision"]}

    def op_create_sandbox(self, state, principal, request):
        self.require_unfrozen(state, principal)
        world_id = "sandbox_" + principal
        if world_id in state["worlds"]:
            reject("sandbox_exists", "Each admitted account has one saved sandbox.")
        name = request.get("name", "My sandbox")
        gravity = request.get("gravity", 0.25)
        if not isinstance(name, str) or not 1 <= len(name.strip()) <= 48 or gravity not in (0.25, 1.0) or isinstance(gravity, bool):
            reject("sandbox_invalid", "Use a short world name and quarter or normal gravity.")
        state["worlds"][world_id] = self.new_world(world_id, name.strip(), principal, gravity, state["empty_world"])
        return {"sandbox_id": world_id}

    def owner_world(self, state, principal, request):
        world = state["worlds"].get(request.get("sandbox_id"))
        if not world or world["id"] == "home" or world["owner_id"] != principal:
            reject("owner_required", "Only this sandbox's owner can manage its rules and invitations.")
        return world

    def op_set_rules(self, state, principal, request):
        world = self.owner_world(state, principal, request)
        if request.get("expected_rules_revision") != world["rules"]["revision"] or request.get("gravity") not in (0.25, 1.0) or isinstance(request.get("gravity"), bool):
            reject("rules_conflict", "Refresh the destination rules and choose quarter or normal gravity.")
        # No hot physics mutation: everyone must leave before rules can change.
        if any(self.active(p) and p["world_id"] == world["id"] for p in state["presence"].values()):
            reject("world_active", "Return this sandbox's occupants Home before changing gravity.")
        world["rules"]["gravity_scale"] = request["gravity"]
        world["rules"]["revision"] += 1
        return {"rules_revision": world["rules"]["revision"]}

    def op_invite(self, state, principal, request):
        world = self.owner_world(state, principal, request)
        recipient, role = request.get("recipient"), request.get("role", "visitor")
        expiry, uses = request.get("expires_in_seconds", 3600), request.get("max_uses", 1)
        if recipient not in PRINCIPALS or recipient == principal or role not in ("visitor", "editor") or not integer(expiry, 1, 86400) or not integer(uses, 1, 8):
            reject("invite_invalid", "Choose an admitted recipient, visitor/editor role, expiry up to one day and up to eight visits.")
        if len(state["invites"]) >= 128:
            reject("invite_limit", "This pilot's invitation allowance is full.")
        invite_id = self.id(state, "invite")
        state["invites"][invite_id] = {"id": invite_id, "world_id": world["id"], "owner_id": principal, "recipient": recipient, "role": role, "expires_at": self.clock() + expiry, "remaining_uses": uses, "revoked": False, "revision": 1}
        return {"invite_id": invite_id}

    def op_revoke(self, state, principal, request):
        invitation = state["invites"].get(request.get("invite_id"))
        if not invitation or invitation["owner_id"] != principal:
            reject("invite_denied", "Only the issuing owner can revoke this invitation.")
        invitation["revoked"] = True
        invitation["revision"] += 1
        state["worlds"][invitation["world_id"]]["grants"].pop(invitation["recipient"], None)
        return {"invite_id": invitation["id"]}

    def invitation(self, state, principal, destination, invite_id):
        world = state["worlds"][destination]
        if destination == "home" or world["owner_id"] == principal:
            return None
        invitation = state["invites"].get(invite_id)
        if not invitation or invitation["world_id"] != destination or invitation["recipient"] != principal or invitation["revoked"] or invitation["expires_at"] <= self.clock() or invitation["remaining_uses"] <= 0:
            reject("invite_unavailable", "This destination needs a current invitation for your account.")
        return invitation

    def op_travel_prepare(self, state, principal, request):
        self.require_unfrozen(state, principal)
        presence = state["presence"][principal]
        destination = request.get("destination_world_id")
        if destination not in state["worlds"] or destination == presence["world_id"]:
            reject("destination_invalid", "Choose another saved world.")
        self.ensure_slot(state, destination, principal)
        invitation = self.invitation(state, principal, destination, request.get("invite_id", ""))
        # Visitors save no world content, but still verify a current source revision.
        world = state["worlds"][presence["world_id"]]
        if world["grants"].get(principal) in ("owner", "editor"):
            world = self.save(state, principal, request)
        elif request.get("expected_store_revision") != world["store_revision"] or digest(request.get("envelope")) != world["envelope_sha256"]:
            reject("visitor_checkpoint", "A visitor may checkpoint only the unchanged saved world.")
        self.checkpoint(world, principal, request)
        if sum(key.startswith("travel_") for key in state["transfers"]) >= 2048:
            reject("transfer_limit", "This local pilot's transfer ledger is full.")
        transfer_id = self.id(state, "travel")
        rules = state["worlds"][destination]["rules"]
        state["transfers"][transfer_id] = {"id": transfer_id, "principal": principal, "source": presence["world_id"], "destination": destination, "source_epoch": presence["epoch"], "session_id": presence["session_id"], "boot_id": self.boot_id, "state": "prepared", "rules_revision": rules["revision"], "rules_sha256": digest(rules), "invite_id": invitation["id"] if invitation else "", "invite_revision": invitation["revision"] if invitation else 0, "prepared_at": self.clock()}
        return {"transfer_id": transfer_id, "destination_rules": copy.deepcopy(rules), "destination_rules_sha256": digest(rules)}

    def transfer(self, state, principal, request):
        transfer = state["transfers"].get(request.get("transfer_id"))
        if not transfer or transfer["principal"] != principal or transfer["session_id"] != state["presence"][principal]["session_id"] or transfer["boot_id"] != self.boot_id:
            reject("transfer_stale", "This crossing belongs to another session.")
        return transfer

    def op_travel_freeze(self, state, principal, request):
        transfer = self.transfer(state, principal, request)
        if transfer["state"] != "prepared":
            reject("transfer_state", "Only a prepared crossing can freeze its source.")
        transfer["state"] = "frozen"
        state["presence"][principal]["frozen"] = True
        return {"transfer_id": transfer["id"]}

    def op_travel_commit(self, state, principal, request):
        transfer = self.transfer(state, principal, request)
        if transfer["state"] != "frozen":
            reject("transfer_state", "Freeze the source before committing this crossing.")
        rules = state["worlds"][transfer["destination"]]["rules"]
        if request.get("expected_rules_revision") != transfer["rules_revision"] or rules["revision"] != transfer["rules_revision"] or digest(rules) != transfer["rules_sha256"]:
            reject("rules_changed", "The destination rules changed. Cancel and review the destination again.")
        if self.clock() > transfer["prepared_at"] + 120:
            reject("transfer_expired", "This crossing expired. Cancel it and try again.")
        self.ensure_slot(state, transfer["destination"], principal)
        invitation = self.invitation(state, principal, transfer["destination"], transfer["invite_id"])
        if invitation:
            if invitation["revision"] != transfer["invite_revision"]:
                reject("invite_changed", "This invitation changed after the crossing was prepared.")
            invitation["remaining_uses"] -= 1
            state["worlds"][transfer["destination"]]["grants"][principal] = invitation["role"]
        presence = state["presence"][principal]
        presence["world_id"] = transfer["destination"]
        presence["epoch"] += 1
        transfer["state"] = "committed"
        transfer["destination_epoch"] = presence["epoch"]
        # No invention, item or blueprint copy occurs here; only presence changes.
        return {"transfer_id": transfer["id"], "committed": True}

    def op_travel_arrive(self, state, principal, request):
        transfer = self.transfer(state, principal, request)
        if transfer["state"] != "committed":
            reject("transfer_state", "Only the committed destination can acknowledge arrival.")
        transfer["state"] = "arrived"
        state["presence"][principal]["frozen"] = False
        return {"transfer_id": transfer["id"], "arrived": True}

    def op_travel_cancel(self, state, principal, request):
        transfer = self.transfer(state, principal, request)
        if transfer["state"] not in ("prepared", "frozen"):
            reject("transfer_committed", "A committed crossing cannot roll back. Arrive or Return Home.")
        transfer["state"] = "cancelled"
        state["presence"][principal]["frozen"] = False
        return {"transfer_id": transfer["id"], "cancelled": True}

    def op_return_home(self, state, principal, request):
        presence = state["presence"][principal]
        current = state["worlds"][presence["world_id"]]
        # Optional source checkpoint is validated/saved atomically, never required
        # to escape a revoked grant, unknown commit or incompatible sandbox.
        if "envelope" in request and current["grants"].get(principal) in ("owner", "editor"):
            self.save(state, principal, request)
        if "source_checkpoint" in request:
            self.checkpoint(current, principal, request)
        existing = self.pending(state, principal)
        if presence["world_id"] == "home" and existing and existing["state"] == "committed":
            return {"returned_home": True, "transfer_id": existing["id"]}
        for transfer in state["transfers"].values():
            if transfer["principal"] == principal and transfer["state"] in ("prepared", "frozen", "committed"):
                transfer["state"] = "arrived" if transfer["state"] == "committed" else "cancelled"
        source = presence["world_id"]
        presence["world_id"] = "home"
        if source != "home":
            presence["epoch"] += 1
        presence["frozen"] = source != "home"
        result = {"returned_home": True}
        if source != "home":
            # A fixed recovery transfer per account cannot be exhausted by the
            # ordinary outward-travel history. Its prior epoch is already fenced.
            transfer_id = "return_" + principal
            state["transfers"][transfer_id] = {"id": transfer_id, "principal": principal, "source": source, "destination": "home", "source_epoch": presence["epoch"] - 1, "destination_epoch": presence["epoch"], "session_id": presence["session_id"], "boot_id": self.boot_id, "state": "committed", "rules_revision": state["worlds"]["home"]["rules"]["revision"], "rules_sha256": digest(state["worlds"]["home"]["rules"]), "invite_id": "", "invite_revision": 0, "prepared_at": self.clock()}
            result["transfer_id"] = transfer_id
        return result

    def op_disconnect(self, state, principal, request):
        self.require_unfrozen(state, principal)
        state["presence"][principal]["active"] = False
        return {"disconnected": True}

    def response(self, state, principal, extra, *, include_envelope=True):
        presence = state["presence"][principal]
        world = state["worlds"][presence["world_id"]]
        checkpoint = world["checkpoints"].get(principal, {"position": [-22.0, 0.0, 340.0], "heading_rad": math.pi})
        worlds = []
        for item in state["worlds"].values():
            occupants = [who for who, p in state["presence"].items() if self.active(p) and p["world_id"] == item["id"]]
            reserved = any(t["destination"] == item["id"] and t["state"] in ("prepared", "frozen") and t["boot_id"] == self.boot_id for t in state["transfers"].values())
            draining = any(t["source"] == item["id"] and t["state"] == "committed" for t in state["transfers"].values())
            can_visit = item["id"] == "home" or item["owner_id"] == principal or any(i["world_id"] == item["id"] and i["recipient"] == principal and not i["revoked"] and i["expires_at"] > self.clock() and i["remaining_uses"] > 0 for i in state["invites"].values())
            worlds.append({"id": item["id"], "name": item["name"], "gravity_scale": item["rules"]["gravity_scale"], "owner_id": item["owner_id"], "status": "active" if occupants else "draining" if draining else "reserved" if reserved else "sleeping", "can_visit": can_visit, "occupants": occupants, "rules_revision": item["rules"]["revision"]})
        invitations = [copy.deepcopy(i) for i in state["invites"].values() if principal in (i["owner_id"], i["recipient"])]
        result = {"ok": True, "session_id": presence["session_id"], "world_id": presence["world_id"], "presence_epoch": presence["epoch"], "store_revision": world["store_revision"], "rules": copy.deepcopy(world["rules"]), "arrival_checkpoint": copy.deepcopy(checkpoint["position"]), "heading_rad": checkpoint["heading_rad"], "status": {"current_world": next(w for w in worlds if w["id"] == world["id"]), "worlds": worlds, "presence": copy.deepcopy(presence), "pending": copy.deepcopy(self.pending(state, principal)) or {}, "invitations": invitations, "message": "Local PostgreSQL save and travel service"}, **extra}
        if include_envelope:
            result["envelope"] = copy.deepcopy(world["envelope"])
        return result
