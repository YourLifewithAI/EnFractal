"""A mock game host that implements the command contract faithfully enough for P1's host to replace it.

It loads a real room (`game/rooms/test_room` by default) through the room contract and answers
`enfractal.command` and `enfractal.query` messages with `enfractal.result` messages:

- the principal comes from the caller (the trusted transport), never from the message;
- every message is validated against the contract, plus the semantic rules a schema cannot express;
- commands are idempotent per principal and action_id, fingerprinted over the canonical command
  JSON as received; same content replays the receipt, different content is `action_id_conflict`;
- `expected_revision` and `expected_entities` are checked here, never by the sender, and
  `goal.stop` / `effect.stop` never fail on revisions or rate limits;
- goals, effects and grabs get transient receipts; everything else gets a durable receipt;
- commands that need the player's consent are held: the host mints a 128-bit `request_id`, the
  player approves or denies through `player_decide` (the game UI, never the companion's
  connection), and an approved command commits under its original principal and action_id with
  `approved_by`; approvals expire, and lapse if the entities they touch change;
- `protect.unlock` (every op in `$defs/player_only_ops`) is refused from companion principals;
- a companion observes and acts only through its own avatar;
- unknown, removed and foreign entity ids all fail with the same `target_not_found` answer;
- world text is emitted only in contract fields and sanitised to the contract's character rules;
- every result is validated against the contract before it leaves; a result that would not
  validate is replaced by `internal_error`.

The world model is deliberately simple (no physics, no line of sight): Run 2 replaces it with the
kernel. What must carry over is the message behaviour, which the boundary tests pin down.
"""
from __future__ import annotations

import copy
import hashlib
import logging
import math
import secrets
import threading
import time
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable

from . import textsafety
from .refusals import HostError, base_result, failure, map_schema_errors
from .contract import (
    AUTHORITY_KEYS,
    COMMAND_SCHEMA,
    QUERY_SCHEMA,
    RESULT_SCHEMA,
    Contracts,
    DEFAULT_REPO_ROOT,
)

log = logging.getLogger("enfractal.mock_host")

PLAYER = "player:local"
COMPANION = "companion:local"
OWN_AVATAR = {PLAYER: "avatar:player", COMPANION: "avatar:companion"}
STOP_OPS = frozenset({"goal.stop", "effect.stop"})
TRANSIENT_OPS = frozenset({"entity.grab", "creation.activate", "goal.set", "goal.stop", "effect.start", "effect.stop"})
LOCKABLE_KINDS = frozenset({"object", "creation"})

DEFAULT_ROOM_DIR = DEFAULT_REPO_ROOT / "game" / "rooms" / "test_room"
DEFAULT_STYLES_DIR = DEFAULT_REPO_ROOT / "game" / "styles"

# Effect capabilities the mock supports: name -> (category, {param: (min, max)}).
CAPABILITIES: dict[str, tuple[str, dict[str, tuple[float, float]]]] = {
    "wind_field": ("air", {"speed_mps": (0.0, 5.0), "direction_deg": (0.0, 360.0)}),
    "glow": ("light", {"intensity": (0.0, 1.0)}),
}


@dataclass
class HostPolicy:
    """Numbers the founder decides. These are the recommended defaults (see docs/companion/SECURITY.md)."""

    # Companion commands the host holds for the player's click.
    companion_approval_ops: frozenset[str] = frozenset(
        {"entity.remove", "entity.transform", "creation.revise", "room.undo", "style.set"})
    approval_ttl_s: float = 300.0
    max_pending_approvals: int = 3
    # Token buckets per principal. Stop ops are never limited.
    command_rate_per_s: float = 2.0
    command_burst: int = 10
    query_rate_per_s: float = 10.0
    query_burst: int = 30
    observe_default_radius_m: float = 3.0
    max_effects_per_principal: int = 4
    max_creations: int = 32
    carry_limit_kg: dict[str, float] = field(default_factory=lambda: {"avatar:player": 0.5, "avatar:companion": 2.0})
    history_depth: int = 64
    max_receipts_per_principal: int = 4096


class SystemClock:
    def now(self) -> float:
        return time.time()


class FakeClock:
    """Deterministic clock for tests."""

    def __init__(self, start: float = 1_790_000_000.0):
        self.t = start

    def now(self) -> float:
        return self.t

    def advance(self, seconds: float) -> None:
        self.t += seconds


def utc(ts: float) -> str:
    return datetime.fromtimestamp(ts, timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _not_found(field_path: str) -> HostError:
    # One answer for unknown, removed and other-room ids: it confirms nothing about what exists.
    return HostError("target_not_found", "No entity with that id is in this room.", field_path=field_path)


@dataclass
class Entity:
    id: str
    kind: str
    display_name: str
    position: list[float]
    half_extents: list[float]  # x, z half sizes and full height in y, measured from the bottom-centre pivot
    affordances: list[str]
    movable: bool
    provenance_kind: str
    revision: int = 0
    category: str | None = None
    category_group: str | None = None
    protected: bool = False
    protected_by: str | None = None
    held_by: str | None = None
    mass_kg: float | None = None
    parts: dict[str, float] = field(default_factory=dict)
    removed: bool = False
    texts: list[str] = field(default_factory=list)
    explicit_bounds: dict | None = None
    created_by: str | None = None
    expires_at: float | None = None  # effects only

    def bounds(self) -> dict:
        if self.explicit_bounds is not None:
            return self.explicit_bounds
        x, y, z = self.position
        hx, height, hz = self.half_extents
        return {"min_m": [_r(x - hx), _r(y), _r(z - hz)], "max_m": [_r(x + hx), _r(y + height), _r(z + hz)]}

    def summary(self) -> dict:
        out = {
            "id": self.id,
            "kind": self.kind,
            "display_name": textsafety.display_text(self.display_name, 80),
            "position_m": [_r(v) for v in self.position],
            "bounds_m": self.bounds(),
            "affordances": list(dict.fromkeys(self.affordances)),
            "movable": self.movable,
            "protected": self.protected,
            "provenance_kind": self.provenance_kind,
            "revision": self.revision,
        }
        if self.category:
            out["category"] = textsafety.display_text(self.category, 60)
        if self.category_group:
            out["category_group"] = self.category_group
        if self.held_by:
            out["held_by"] = self.held_by
        return out

    def durable_state(self) -> dict:
        return {
            "kind": self.kind, "display_name": self.display_name, "position": list(self.position),
            "half_extents": list(self.half_extents), "affordances": list(self.affordances),
            "movable": self.movable, "provenance_kind": self.provenance_kind, "revision": self.revision,
            "category": self.category, "category_group": self.category_group, "protected": self.protected,
            "protected_by": self.protected_by, "mass_kg": self.mass_kg, "parts": dict(self.parts),
            "removed": self.removed, "texts": list(self.texts), "explicit_bounds": copy.deepcopy(self.explicit_bounds),
            "created_by": self.created_by,
        }


def _r(value: float) -> float:
    rounded = round(float(value), 4)
    return 0.0 if rounded == 0 else rounded


@dataclass
class Receipt:
    fingerprint: str
    result: dict
    durable: bool


@dataclass
class Approval:
    request_id: str
    principal: str
    action_id: str
    fingerprint: str
    message: dict
    expires_at: float
    touched: dict[str, int]
    room_revision: int
    pending_result: dict
    state: str = "pending"
    result: dict | None = None


class _Bucket:
    def __init__(self, rate: float, burst: int, now: float):
        self.rate, self.burst, self.tokens, self.t = rate, float(burst), float(burst), now

    def take(self, now: float) -> bool:
        self.tokens = min(self.burst, self.tokens + (now - self.t) * self.rate)
        self.t = now
        if self.tokens >= 1.0:
            self.tokens -= 1.0
            return True
        return False


class MockHost:
    """The game side of the companion boundary, minus physics."""

    def __init__(self, contracts: Contracts, room_dir: Path | None = None, styles_dir: Path | None = None,
                 policy: HostPolicy | None = None, clock=None):
        self.contracts = contracts
        self.policy = policy or HostPolicy()
        self.clock = clock or SystemClock()
        self._lock = threading.RLock()
        self.room_dir = Path(room_dir or DEFAULT_ROOM_DIR)
        self.styles = self._index_styles(Path(styles_dir or DEFAULT_STYLES_DIR))
        self.entities: dict[str, Entity] = {}
        self.revision = 0
        self.receipts: dict[tuple[str, str], Receipt] = {}
        self.terminal: dict[tuple[str, str], Receipt] = {}  # denied or lapsed approvals, replayed as-is
        self.approvals: dict[str, Approval] = {}
        self.approval_by_action: dict[tuple[str, str], str] = {}
        self.goals: dict[str, dict] = {}
        self.holding: dict[str, str] = {}
        self.checkpoints: list[dict] = []
        self.history: dict[int, dict] = {}
        self.buckets: dict[tuple[str, str], _Bucket] = {}
        self._creation_counter = 0
        self._effect_counter = 0
        self._load_room()
        self.history[0] = self._snapshot()
        self.listeners: list[Callable[[str, dict], None]] = []
        self._known_ops = frozenset(contracts.command_ops) | frozenset(contracts.query_ops)

    # ------------------------------------------------------------------ room loading

    def _index_styles(self, styles_dir: Path) -> dict[tuple[str, int], str]:
        found: dict[tuple[str, int], str] = {}
        if styles_dir.is_dir():
            for path in sorted(styles_dir.glob("*/v*.json")):
                try:
                    preset = self.contracts.validate.load_strict(path)
                    key = (preset["preset_id"], int(preset["preset_version"]))
                except Exception:  # an unreadable preset is simply not offered
                    continue
                found[key] = hashlib.sha256(path.read_bytes()).hexdigest()
        return found

    def _load_room(self) -> None:
        load = self.contracts.validate.load_strict
        room = load(self.room_dir / "room.json")
        problems = self.contracts.schema_errors(room)
        if problems:
            raise ValueError(f"room {self.room_dir} does not validate: {problems[:3]}")
        self.room = room
        self.room_id = room["room_id"]
        self.room_bounds = room["bounds"]
        provenance = "hand_authored" if room["source"]["kind"] == "hand_built" else (
            "procedural" if room["source"]["kind"] == "procedural" else "captured_scanned")
        for part in room["shell"]["parts"]:
            geometry = part["geometry"]
            bounds = None
            if geometry["kind"] == "polygon":
                pts = geometry["points_m"]
                bounds = {"min_m": [_r(min(p[i] for p in pts)) for i in range(3)],
                          "max_m": [_r(max(p[i] for p in pts)) for i in range(3)]}
            centre = [(bounds["min_m"][i] + bounds["max_m"][i]) / 2 for i in range(3)] if bounds else [0.0, 0.0, 0.0]
            self.entities[part["id"]] = Entity(
                id=part["id"], kind="shell", display_name=part["role"].replace("_", " ").capitalize(),
                position=centre, half_extents=[0, 0, 0], affordances=["walkable_top"] if part["role"] in ("floor", "platform") else [],
                movable=False, provenance_kind=provenance, explicit_bounds=bounds,
                category=part["role"], category_group="structure")
        assets: dict[str, dict] = {}
        for instance in room["objects"]:
            if instance["asset"] not in assets:
                assets[instance["asset"]] = load(self.room_dir / instance["asset"])
            asset = assets[instance["asset"]]
            dims = asset["dimensions_m"]
            scale = instance["transform"].get("scale", 1.0)
            self.entities[instance["id"]] = Entity(
                id=instance["id"], kind="object",
                display_name=instance.get("display_name", asset["display_name"]),
                position=list(instance["transform"]["position_m"]),
                half_extents=[dims[0] * scale / 2, dims[1] * scale, dims[2] * scale / 2],
                affordances=list(asset["affordances"]), movable=bool(asset["physics"]["movable"]),
                provenance_kind=asset["provenance"]["kind"], category=asset.get("category"),
                category_group=asset.get("category_group"), mass_kg=asset["physics"].get("mass_kg"),
                parts={p["id"]: float(p.get("default", 0.0)) for p in asset.get("parts", [])},
            )
        spawns = {s["role"]: s for s in room["spawns"]}
        player_spawn = spawns.get("player", {"position_m": [0.0, 0.0, 0.0]})
        companion_spawn = spawns.get("companion", player_spawn)
        self.entities["avatar:player"] = Entity(
            id="avatar:player", kind="avatar", display_name="Player", position=list(player_spawn["position_m"]),
            half_extents=[0.02, 0.10, 0.02], affordances=[], movable=False, provenance_kind="hand_authored")
        self.entities["avatar:companion"] = Entity(
            id="avatar:companion", kind="avatar", display_name="Wisp", position=list(companion_spawn["position_m"]),
            half_extents=[0.055, 0.24, 0.055], affordances=[], movable=False, provenance_kind="hand_authored")
        style = room.get("default_style")
        if style is None:
            key = next(iter(sorted(self.styles)), ("storybook_painterly", 1))
            style = {"preset_id": key[0], "preset_version": key[1], "preset_sha256": self.styles.get(key, "0" * 64)}
        self.style = dict(style)

    # ------------------------------------------------------------------ world fixtures (game side, not the wire)

    def add_world_text(self, entity_id: str, text: str) -> None:
        """A label or sign the companion can read. Untrusted: it may say anything."""
        with self._lock:
            self.entities[entity_id].texts.append(text)

    def rename_entity(self, entity_id: str, name: str) -> None:
        """A player-chosen or captured name. Untrusted: it may say anything."""
        with self._lock:
            self.entities[entity_id].display_name = name

    def move_avatar(self, avatar_id: str, position: list[float]) -> None:
        with self._lock:
            self.entities[avatar_id].position = list(position)

    # ------------------------------------------------------------------ the wire

    def handle(self, principal: str, message: Any) -> dict:
        """Answer one parsed message from `principal`, which the trusted transport assigned."""
        if principal not in OWN_AVATAR:
            raise ValueError(f"the transport passed an unknown principal {principal!r}")
        with self._lock:
            self._expire()
            try:
                result = self._handle(principal, message)
            except HostError as error:
                result = self._fail(principal, message, error)
            except RecursionError:
                result = self._fail(principal, None, HostError("request_invalid", "The request is nested too deeply."))
            except Exception:  # never let a bug leak a stack trace to the requester
                log.exception("mock host failed on a request")
                result = self._fail(principal, message, HostError(
                    "internal_error", "The game could not handle that request.", retryable=True))
            if self.contracts.schema_errors(result):
                log.error("mock host built an invalid result: %s", self.contracts.schema_errors(result)[:3])
                result = self._fail(principal, None, HostError(
                    "internal_error", "The game could not handle that request.", retryable=True))
            return result

    def handle_bytes(self, principal: str, raw: bytes) -> dict:
        """Parse strictly (no duplicate keys, NaN or overflow), then answer."""
        try:
            message = self.contracts.loads_strict(raw.decode("utf-8"))
        except (UnicodeDecodeError, self.contracts.ContractError):
            with self._lock:
                return self._fail(principal, None, HostError(
                    "request_invalid", "The request is not valid JSON (duplicate keys and non-finite numbers are refused)."))
        return self.handle(principal, message)

    # ------------------------------------------------------------------ dispatch

    def _handle(self, principal: str, message: Any) -> dict:
        if not isinstance(message, dict) or message.get("schema") not in (COMMAND_SCHEMA, QUERY_SCHEMA):
            raise HostError("request_invalid", "Send an enfractal.command or enfractal.query.")
        is_command = message["schema"] == COMMAND_SCHEMA
        op = message.get("op") if isinstance(message.get("op"), str) else None
        if not (is_command and op in STOP_OPS):
            bucket_kind = "command" if is_command else "query"
            if not self._take(principal, bucket_kind):
                raise HostError("rate_limited", "Too many requests; wait a moment and try again.", retryable=True)
        self._check_schema(message)
        if message["room_id"] != self.room_id:
            raise HostError("room_mismatch", "That room is not the one loaded in the game.", field_path="$.room_id")
        hit = _find_authority_key(message.get("args"), ["args"])
        if hit is not None:
            raise HostError("field_unknown", "Identity and approval fields are never accepted in a request.",
                            field_path=textsafety.field_path(hit))
        if op in self.contracts.player_only_ops and not principal.startswith("player:"):
            raise HostError("permission_denied", "Only the player can do that, directly in the game.", field_path="$.op")
        if is_command:
            return self._command(principal, message)
        return self._query(principal, message)

    def _take(self, principal: str, kind: str) -> bool:
        now = self.clock.now()
        key = (principal, kind)
        if key not in self.buckets:
            rate, burst = ((self.policy.command_rate_per_s, self.policy.command_burst) if kind == "command"
                           else (self.policy.query_rate_per_s, self.policy.query_burst))
            self.buckets[key] = _Bucket(rate, burst, now)
        return self.buckets[key].take(now)

    def _check_schema(self, message: dict) -> None:
        canonical = self.contracts.canonical_bytes(message)
        limit = self.contracts.message_limits[message["schema"]]
        if len(canonical) > limit:
            raise HostError("request_invalid", f"The request is larger than {limit} bytes of canonical JSON.",
                            field_path="$", allowed=limit, actual=len(canonical))
        errors = list(self.contracts.iter_errors(message))
        if errors:
            raise map_schema_errors(errors)
        args = message.get("args", {})
        for key in ("source",):
            for holder, path in ((args, "$.args.source"), (args.get("into") if isinstance(args.get("into"), dict) else {}, "$.args.into.source")):
                source = holder.get(key) if isinstance(holder, dict) else None
                if isinstance(source, dict):
                    size = len(self.contracts.canonical_bytes(source))
                    if size > self.contracts.creation_source_limit:
                        raise HostError("budget_exceeded", "The creation is larger than the size limit.",
                                        field_path=path, allowed=self.contracts.creation_source_limit, actual=size)

    # ------------------------------------------------------------------ results

    def _base(self, principal: str, message: Any) -> dict:
        return base_result(principal=principal, room_id=self.room_id, revision=self.revision,
                           at_utc=utc(self.clock.now()), message=message, known_ops=self._known_ops)

    def _fail(self, principal: str, message: Any, error: HostError) -> dict:
        return failure(self._base(principal, message), error)

    def _emit(self, kind: str, payload: dict) -> None:
        for listener in list(self.listeners):
            try:
                listener(kind, payload)
            except Exception:
                log.exception("listener failed")

    # ------------------------------------------------------------------ commands

    def _command(self, principal: str, message: dict) -> dict:
        op = message["op"]
        action_id = message["action_id"]
        key = (principal, action_id)
        fingerprint = hashlib.sha256(self.contracts.canonical_bytes(message)).hexdigest()
        preview = bool(message.get("preview", False))
        if not preview:
            for store in (self.receipts, self.terminal):
                if key in store:
                    return self._replay(store[key], fingerprint, message)
            request_id = self.approval_by_action.get(key)
            if request_id is not None:
                approval = self.approvals[request_id]
                if approval.fingerprint != fingerprint:
                    raise HostError("action_id_conflict", "That action id was already used for a different command.",
                                    field_path="$.action_id")
                replay = copy.deepcopy(approval.pending_result)
                replay["replayed"] = True
                replay["at_utc"] = utc(self.clock.now())
                return replay
        self._check_revisions(principal, message)
        handler = getattr(self, "_op_" + op.replace(".", "_"))
        prediction = handler(principal, message, False, None)
        if preview:
            result = self._base(principal, message)
            result["preview"] = True
            result.update(prediction)
            result.pop("transient", None)
            return result
        if principal != PLAYER and op in self.policy.companion_approval_ops:
            return self._hold(principal, message, fingerprint, prediction)
        self._check_receipt_room(principal)
        result = self._commit(principal, message, handler)
        self._store_receipt(key, fingerprint, result)
        return result

    def _commit(self, principal: str, message: dict, handler, approved_by: str | None = None) -> dict:
        op = message["op"]
        durable = op not in TRANSIENT_OPS
        new_revision = self.revision + 1 if durable else None
        outcome = handler(principal, message, True, new_revision)
        if durable:
            self.revision = new_revision
            self.history[new_revision] = self._snapshot()
            for old in [r for r in self.history if r < new_revision - self.policy.history_depth]:
                del self.history[old]
        result = self._base(principal, message)
        result.update(outcome)
        result["transient"] = not durable
        if approved_by:
            result["approved_by"] = approved_by
        self._emit("committed", {"principal": principal, "op": op, "action_id": message["action_id"]})
        return result

    def _check_receipt_room(self, principal: str) -> None:
        count = sum(1 for (p, _a) in self.receipts if p == principal)
        if count >= self.policy.max_receipts_per_principal:
            raise HostError("receipt_limit", "The receipt ledger is full; take a checkpoint.", retryable=False)

    def _store_receipt(self, key: tuple[str, str], fingerprint: str, result: dict) -> None:
        self.receipts[key] = Receipt(fingerprint, copy.deepcopy(result), durable=not result.get("transient", False))

    def _replay(self, receipt: Receipt, fingerprint: str, message: dict) -> dict:
        if receipt.fingerprint != fingerprint:
            raise HostError("action_id_conflict", "That action id was already used for a different command.",
                            field_path="$.action_id")
        replay = copy.deepcopy(receipt.result)
        replay["replayed"] = True
        return replay

    def _check_revisions(self, principal: str, message: dict) -> None:
        op = message["op"]
        if op in STOP_OPS:
            return
        if "expected_revision" in message and message["expected_revision"] != self.revision:
            raise HostError("revision_conflict", "The room changed since you looked. Observe again and retry.",
                            field_path="$.expected_revision", retryable=True,
                            allowed=self.revision, actual=message["expected_revision"])
        expected = message.get("expected_entities", {})
        for entity_id in sorted(expected):
            entity = self._entity(entity_id)
            if entity is None:
                raise _not_found("$.expected_entities")
            if entity.revision != expected[entity_id]:
                raise HostError("revision_conflict", "An entity changed since you looked. Observe again and retry.",
                                field_path=textsafety.field_path(["expected_entities", entity_id]), retryable=True,
                                allowed=entity.revision, actual=expected[entity_id])
        if op in self.contracts.destructive_ops and "expected_revision" not in message:
            for target in _targets(message["args"]):
                if target not in expected:
                    raise HostError("request_invalid",
                                    "A destructive command must name every target in expected_entities, or name expected_revision.",
                                    field_path="$.expected_entities")

    def _hold(self, principal: str, message: dict, fingerprint: str, prediction: dict) -> dict:
        pending = [a for a in self.approvals.values() if a.principal == principal and a.state == "pending"]
        if len(pending) >= self.policy.max_pending_approvals:
            raise HostError("rate_limited", "Several changes are already waiting for the player.", retryable=True)
        request_id = secrets.token_hex(16)
        expires_at = self.clock.now() + self.policy.approval_ttl_s
        touched = {}
        for entity_id in _targets(message["args"]):
            entity = self._entity(entity_id)
            if entity is not None:
                touched[entity_id] = entity.revision
        result = self._base(principal, message)
        result["ok"] = False
        result["approval_needed"] = {
            "request_id": request_id,
            "reason": _approval_reason(message),
            "expires_utc": utc(expires_at),
        }
        result["error"] = {"code": "approval_required", "message": "Waiting for the player to approve this change in the game.",
                           "retryable": True}
        approval = Approval(request_id, principal, message["action_id"], fingerprint, copy.deepcopy(message),
                            expires_at, touched, self.revision, copy.deepcopy(result))
        self.approvals[request_id] = approval
        self.approval_by_action[(principal, message["action_id"])] = request_id
        self._emit("approval_requested", {"request_id": request_id, "op": message["op"], "principal": principal,
                                          "reason": result["approval_needed"]["reason"]})
        return result

    # ------------------------------------------------------------------ the player's side (game UI only)

    def pending_approvals(self) -> list[dict]:
        with self._lock:
            self._expire()
            return [{"request_id": a.request_id, "principal": a.principal, "op": a.message["op"],
                     "reason": a.pending_result["approval_needed"]["reason"], "expires_utc": utc(a.expires_at)}
                    for a in self.approvals.values() if a.state == "pending"]

    def player_decide(self, request_id: str, approve: bool) -> dict:
        """The player's click in the game UI. Never reachable from the companion's connection."""
        with self._lock:
            self._expire()
            approval = self.approvals.get(request_id)
            if approval is None:
                raise KeyError("no such approval request")
            if approval.state != "pending":
                return {"state": approval.state, "result": approval.result}
            message = approval.message
            key = (approval.principal, approval.action_id)
            if not approve:
                self._finish(approval, "denied", HostError("permission_denied", "The player declined this change."))
            elif self._lapsed(approval):
                self._finish(approval, "expired", HostError(
                    "approval_mismatch", "The room changed before the player approved; ask again.", retryable=True))
            else:
                try:
                    self._check_revisions(approval.principal, message)
                    handler = getattr(self, "_op_" + message["op"].replace(".", "_"))
                    handler(approval.principal, message, False, None)
                    self._check_receipt_room(approval.principal)
                    result = self._commit(approval.principal, message, handler, approved_by=PLAYER)
                    self._store_receipt(key, approval.fingerprint, result)
                    approval.state, approval.result = "approved", result
                except HostError as error:
                    self._finish(approval, "expired", HostError(
                        "approval_mismatch", "The change can no longer be made as approved; ask again.", retryable=True,
                    ) if error.code != "receipt_limit" else error)
            del self.approval_by_action[key]
            self._emit("approval_decided", {"request_id": request_id, "state": approval.state})
            return {"state": approval.state, "result": approval.result}

    def player_command(self, message: dict) -> dict:
        """A manual control in the game: the same command path, under the player's principal."""
        return self.handle(PLAYER, message)

    def _finish(self, approval: Approval, state: str, error: HostError) -> None:
        result = self._fail(approval.principal, approval.message, error)
        approval.state, approval.result = state, result
        key = (approval.principal, approval.action_id)
        self.terminal[key] = Receipt(approval.fingerprint, copy.deepcopy(result), durable=False)

    def _lapsed(self, approval: Approval) -> bool:
        if "expected_revision" in approval.message and self.revision != approval.room_revision:
            return True
        for entity_id, revision in approval.touched.items():
            entity = self._entity(entity_id)
            if entity is None or entity.revision != revision:
                return True
        return False

    def _expire(self) -> None:
        now = self.clock.now()
        for approval in list(self.approvals.values()):
            if approval.state == "pending" and now >= approval.expires_at:
                self._finish(approval, "expired", HostError("approval_expired", "The player did not answer in time.",
                                                            retryable=True))
                self.approval_by_action.pop((approval.principal, approval.action_id), None)
        for entity in list(self.entities.values()):
            if entity.kind == "effect" and not entity.removed and entity.expires_at is not None and now >= entity.expires_at:
                entity.removed = True

    # ------------------------------------------------------------------ helpers

    def _entity(self, entity_id: str) -> Entity | None:
        entity = self.entities.get(entity_id)
        if entity is None or entity.removed:
            return None
        return entity

    def _require(self, entity_id: str, field_path: str) -> Entity:
        entity = self._entity(entity_id)
        if entity is None:
            raise _not_found(field_path)
        return entity

    def _actor(self, principal: str, args: dict) -> str:
        actor = args.get("actor", OWN_AVATAR[principal])
        allowed = {OWN_AVATAR[principal]}
        if principal == PLAYER:
            allowed.add("avatar:companion")  # a player may direct the companion; never the reverse
        if actor not in allowed:
            raise HostError("actor_denied", "A companion acts and observes only through its own avatar.",
                            field_path="$.args.actor")
        self._require(actor, "$.args.actor")
        return actor

    def _check_placement(self, placement: dict | None, field_path: str) -> None:
        if not placement:
            return
        if not _inside(placement["position_m"], self.room_bounds):
            raise HostError("out_of_bounds", "That position is outside the room.", field_path=field_path + ".position_m")
        if "on" in placement:
            support = self._require(placement["on"], field_path + ".on")
            if "walkable_top" not in support.affordances:
                raise HostError("invalid_args", "Things can only be placed on surfaces with a walkable top.",
                                field_path=field_path + ".on")
        rotation = placement.get("rotation")
        if rotation is not None and abs(math.sqrt(sum(c * c for c in rotation)) - 1.0) > 1e-3:
            raise HostError("invalid_args", "A rotation must be a unit quaternion.", field_path=field_path + ".rotation")

    def _check_changeable(self, entity: Entity, field_path: str, what: str = "changed") -> None:
        if entity.kind in ("shell", "avatar", "effect"):
            raise HostError("permission_denied", f"That cannot be {what}.", field_path=field_path)
        if entity.protected:
            raise HostError("target_protected", "That is protected. Only the player can unlock it.", field_path=field_path)

    def _touch(self, entity: Entity, new_revision: int) -> None:
        entity.revision = new_revision

    def _snapshot(self) -> dict:
        return {"entities": {k: e.durable_state() for k, e in self.entities.items() if e.kind not in ("effect", "avatar")},
                "style": dict(self.style)}

    # ------------------------------------------------------------------ op handlers
    # Each handler validates first. With apply=False it only predicts the outcome; with apply=True it
    # changes the world. Durable ops receive the new room revision to stamp on what they touch.

    def _op_entity_grab(self, principal, message, apply, new_revision):
        args = message["args"]
        actor = self._actor(principal, args)
        target = self._require(args["target"], "$.args.target")
        if not target.movable or target.kind != "object" and target.kind != "creation":
            raise HostError("permission_denied", "That cannot be picked up.", field_path="$.args.target")
        if target.protected:
            raise HostError("target_protected", "That is protected. Only the player can unlock it.", field_path="$.args.target")
        if target.held_by and target.held_by != actor:
            raise HostError("target_busy", "Someone else is holding that.", field_path="$.args.target")
        if self.holding.get(actor) not in (None, target.id):
            raise HostError("target_busy", "The avatar is already holding something.", field_path="$.args.actor")
        limit = self.policy.carry_limit_kg.get(actor, 0.0)
        if target.mass_kg is not None and target.mass_kg > limit:
            raise HostError("target_too_heavy", "That is too heavy for this avatar.", field_path="$.args.target",
                            allowed=limit, actual=target.mass_kg)
        if not apply:
            return {"affected": [target.id]}
        target.held_by = actor
        self.holding[actor] = target.id
        return {"affected": [target.id]}

    def _op_entity_release(self, principal, message, apply, new_revision):
        args = message["args"]
        actor = self._actor(principal, args)
        held_id = self.holding.get(actor)
        if held_id is None or self._entity(held_id) is None:
            raise HostError("invalid_args", "The avatar is not holding anything.", field_path="$.args")
        placement = args.get("placement")
        self._check_placement(placement, "$.args.placement")
        if not apply:
            return {"affected": [held_id]}
        held = self.entities[held_id]
        held.position = list(placement["position_m"]) if placement else list(self.entities[actor].position)
        held.held_by = None
        del self.holding[actor]
        self._touch(held, new_revision)
        return {"affected": [held_id]}

    def _op_entity_place(self, principal, message, apply, new_revision):
        args = message["args"]
        target = self._require(args["target"], "$.args.target")
        self._check_changeable(target, "$.args.target", "moved")
        if not target.movable:
            raise HostError("permission_denied", "That cannot be moved.", field_path="$.args.target")
        if target.held_by and target.held_by != OWN_AVATAR[principal]:
            raise HostError("target_busy", "Someone is holding that.", field_path="$.args.target")
        self._check_placement(args["placement"], "$.args.placement")
        if not apply:
            return {"affected": [target.id]}
        target.position = list(args["placement"]["position_m"])
        self._touch(target, new_revision)
        return {"affected": [target.id]}

    def _op_entity_set_part(self, principal, message, apply, new_revision):
        args = message["args"]
        target = self._require(args["target"], "$.args.target")
        self._check_changeable(target, "$.args.target")
        if args["part_id"] not in target.parts:
            raise HostError("invalid_args", "That entity has no part with that id.", field_path="$.args.part_id")
        if not apply:
            return {"affected": [target.id]}
        target.parts[args["part_id"]] = float(args["value"])
        self._touch(target, new_revision)
        return {"affected": [target.id]}

    def _op_entity_remove(self, principal, message, apply, new_revision):
        target = self._require(message["args"]["target"], "$.args.target")
        self._check_changeable(target, "$.args.target", "removed")
        if not apply:
            return {"affected": [target.id]}
        target.removed = True
        if target.held_by:
            self.holding.pop(target.held_by, None)
            target.held_by = None
        self._touch(target, new_revision)
        return {"affected": [target.id]}

    def _op_entity_transform(self, principal, message, apply, new_revision):
        args = message["args"]
        target = self._require(args["target"], "$.args.target")
        self._check_changeable(target, "$.args.target", "transformed")
        source = args["into"]["source"]
        if not apply:
            return {"affected": [target.id]}
        target.display_name = source["name"]
        target.provenance_kind = "ai_created" if principal.startswith("companion:") else "player_created"
        self._touch(target, new_revision)
        return {"affected": [target.id]}

    def _op_creation_place(self, principal, message, apply, new_revision):
        args = message["args"]
        self._check_placement(args["placement"], "$.args.placement")
        live = sum(1 for e in self.entities.values() if e.kind == "creation" and not e.removed)
        if live >= self.policy.max_creations:
            raise HostError("budget_exceeded", "The room already holds as many creations as it can.",
                            allowed=self.policy.max_creations, actual=live)
        if not apply:
            return {}
        self._creation_counter += 1
        entity_id = f"creation:{self._creation_counter:08d}"
        source = args["source"]
        sizes = [p.get("size_m") for p in source["parts"] if isinstance(p.get("size_m"), list) and len(p["size_m"]) == 3]
        half = [0.1, 0.2, 0.1]
        if sizes and all(isinstance(v, (int, float)) for s in sizes for v in s):
            half = [max(s[0] for s in sizes) / 2, max(s[1] for s in sizes), max(s[2] for s in sizes) / 2]
            half = [min(max(v, 0.01), 5.0) for v in half]
        self.entities[entity_id] = Entity(
            id=entity_id, kind="creation", display_name=source["name"], position=list(args["placement"]["position_m"]),
            half_extents=half, affordances=[], movable=True,
            provenance_kind="ai_created" if principal.startswith("companion:") else "player_created",
            revision=new_revision, mass_kg=0.5, created_by=principal)
        return {"created": [entity_id], "affected": [entity_id]}

    def _op_creation_revise(self, principal, message, apply, new_revision):
        args = message["args"]
        target = self._require(args["target"], "$.args.target")
        self._check_changeable(target, "$.args.target", "revised")
        if "placement" in args:
            self._check_placement(args["placement"], "$.args.placement")
        if not apply:
            return {"affected": [target.id]}
        if "source" in args:
            target.display_name = args["source"]["name"]
        if "placement" in args:
            target.position = list(args["placement"]["position_m"])
        self._touch(target, new_revision)
        return {"affected": [target.id]}

    def _op_creation_activate(self, principal, message, apply, new_revision):
        target = self._require(message["args"]["target"], "$.args.target")
        if target.kind != "creation":
            raise HostError("invalid_args", "Only creations can be activated.", field_path="$.args.target")
        return {"affected": [target.id]}

    def _op_protect_lock(self, principal, message, apply, new_revision):
        targets = [self._require(t, f"$.args.targets[{i}]") for i, t in enumerate(message["args"]["targets"])]
        for i, target in enumerate(targets):
            if target.kind not in LOCKABLE_KINDS:
                raise HostError("invalid_args", "Only objects and creations can be protected.", field_path=f"$.args.targets[{i}]")
        if not apply:
            return {"affected": [t.id for t in targets]}
        for target in targets:
            target.protected, target.protected_by = True, principal
            self._touch(target, new_revision)
        return {"affected": [t.id for t in targets]}

    def _op_protect_unlock(self, principal, message, apply, new_revision):
        if not principal.startswith("player:"):  # defence in depth; _handle already refused it
            raise HostError("permission_denied", "Only the player can do that, directly in the game.", field_path="$.op")
        targets = [self._require(t, f"$.args.targets[{i}]") for i, t in enumerate(message["args"]["targets"])]
        if not apply:
            return {"affected": [t.id for t in targets]}
        for target in targets:
            target.protected, target.protected_by = False, None
            self._touch(target, new_revision)
        return {"affected": [t.id for t in targets]}

    def _op_goal_set(self, principal, message, apply, new_revision):
        args = message["args"]
        actor = self._actor(principal, args)
        if "target" in args:
            target = self._require(args["target"], "$.args.target")
            if args["goal"] == "fetch":
                if target.kind not in ("object", "creation") or not target.movable:
                    raise HostError("permission_denied", "That cannot be fetched.", field_path="$.args.target")
                if target.protected:
                    raise HostError("target_protected", "That is protected. Only the player can unlock it.",
                                    field_path="$.args.target")
                limit = self.policy.carry_limit_kg.get(actor, 0.0)
                if target.mass_kg is not None and target.mass_kg > limit:
                    raise HostError("target_too_heavy", "That is too heavy for this avatar.", field_path="$.args.target",
                                    allowed=limit, actual=target.mass_kg)
        if "position_m" in args and not _inside(args["position_m"], self.room_bounds):
            raise HostError("out_of_bounds", "That position is outside the room.", field_path="$.args.position_m")
        if "area" in args:
            for corner in ("min_m", "max_m"):
                if not _inside(args["area"][corner], self.room_bounds):
                    raise HostError("out_of_bounds", "That area is outside the room.", field_path=f"$.args.area.{corner}")
        outcome = {"affected": [actor], "data": {"actor": actor, "goal": args["goal"]}}
        if apply:
            goal = {k: copy.deepcopy(v) for k, v in args.items() if k != "actor"}
            goal["started_utc"] = utc(self.clock.now())
            goal["set_by"] = principal
            self.goals[actor] = goal
        return outcome

    def _op_goal_stop(self, principal, message, apply, new_revision):
        args = message["args"]
        if "actor" in args:
            actors = [self._actor(principal, args)]
        else:
            actors = [OWN_AVATAR[principal]] + (["avatar:companion"] if principal == PLAYER else [])
        stopped_effects = [e.id for e in self.entities.values()
                           if e.kind == "effect" and not e.removed and e.created_by == principal]
        if apply:
            for actor in actors:
                self.goals.pop(actor, None)
            for effect_id in stopped_effects:
                self.entities[effect_id].removed = True
        return {"affected": actors + stopped_effects}

    def _op_effect_start(self, principal, message, apply, new_revision):
        args = message["args"]
        capability = CAPABILITIES.get(args["capability"])
        if capability is None:
            raise HostError("unsupported_capability", "The game has no effect by that name. Call capabilities_list.",
                            field_path="$.args.capability")
        bounds = capability[1]
        for name, value in args["params"].items():
            if name not in bounds:
                raise HostError("invalid_args", "That effect does not take a parameter by that name.",
                                field_path=textsafety.field_path(["args", "params", name]))
            low, high = bounds[name]
            if isinstance(value, bool) or not isinstance(value, (int, float)) or not low <= value <= high:
                raise HostError("invalid_args", "An effect parameter is out of range.",
                                field_path=textsafety.field_path(["args", "params", name]), allowed=[low, high])
        if not _inside(args["area"]["center_m"], self.room_bounds):
            raise HostError("out_of_bounds", "The effect's centre is outside the room.", field_path="$.args.area.center_m")
        for i, target_id in enumerate(args.get("targets", [])):
            target = self._require(target_id, f"$.args.targets[{i}]")
            if target.protected:
                raise HostError("target_protected", "A target is protected. Only the player can unlock it.",
                                field_path=f"$.args.targets[{i}]")
        live = [e for e in self.entities.values() if e.kind == "effect" and not e.removed and e.created_by == principal]
        if len(live) >= self.policy.max_effects_per_principal:
            raise HostError("budget_exceeded", "Too many of your effects are running; stop one first.",
                            allowed=self.policy.max_effects_per_principal, actual=len(live))
        if not apply:
            return {}
        self._effect_counter += 1
        effect_id = f"effect:{self._effect_counter:04d}"
        centre, radius = args["area"]["center_m"], args["area"]["radius_m"]
        self.entities[effect_id] = Entity(
            id=effect_id, kind="effect", display_name=args["capability"].replace("_", " "), position=list(centre),
            half_extents=[0, 0, 0], affordances=[], movable=False,
            provenance_kind="ai_created" if principal.startswith("companion:") else "player_created",
            explicit_bounds={"min_m": [_clamp(c - radius) for c in centre], "max_m": [_clamp(c + radius) for c in centre]},
            created_by=principal, expires_at=self.clock.now() + float(args["duration_s"]))
        return {"created": [effect_id], "affected": [effect_id]}

    def _op_effect_stop(self, principal, message, apply, new_revision):
        which = message["args"]["effect"]
        mine = [e for e in self.entities.values() if e.kind == "effect" and not e.removed and e.created_by == principal]
        stopping = [e for e in mine if which == "all" or e.id == which]
        if apply:
            for effect in stopping:
                effect.removed = True
        return {"affected": [e.id for e in stopping]}

    def _op_style_set(self, principal, message, apply, new_revision):
        args = message["args"]
        key = (args["preset_id"], args["preset_version"])
        if key not in self.styles:
            raise HostError("invalid_args", "No style preset with that id and version is installed.", field_path="$.args.preset_id")
        if not apply:
            return {}
        self.style = {"preset_id": key[0], "preset_version": key[1], "preset_sha256": self.styles[key]}
        return {}

    def _op_room_checkpoint(self, principal, message, apply, new_revision):
        if not apply:
            return {}
        label = textsafety.display_text(message["args"].get("label", ""), 80) if message["args"].get("label") else None
        checkpoint = {"id": f"cp{len(self.checkpoints) + 1:04d}", "revision": new_revision}
        if label:
            checkpoint["label"] = label
        self.checkpoints.append(checkpoint)
        return {"data": {"checkpoint_id": checkpoint["id"]}}

    def _op_room_undo(self, principal, message, apply, new_revision):
        to_revision = message["args"]["to_revision"]
        if to_revision >= self.revision or to_revision not in self.history:
            raise HostError("invalid_args", "That revision cannot be restored.", field_path="$.args.to_revision",
                            allowed=[min(self.history), max(self.revision - 1, 0)], actual=to_revision)
        if not apply:
            return {}
        snapshot = self.history[to_revision]
        changed = []
        for entity_id, state in snapshot["entities"].items():
            entity = self.entities.get(entity_id)
            current = entity.durable_state() if entity else None
            if current is not None and {k: v for k, v in current.items() if k != "revision"} == \
                    {k: v for k, v in state.items() if k != "revision"}:
                continue
            restored = Entity(id=entity_id, **{k: copy.deepcopy(v) for k, v in state.items()})
            restored.revision = new_revision
            self.entities[entity_id] = restored
            changed.append(entity_id)
        for entity_id, entity in self.entities.items():
            if entity.kind in ("object", "creation") and entity_id not in snapshot["entities"] and not entity.removed:
                entity.removed = True
                entity.revision = new_revision
                changed.append(entity_id)
        for actor, held in list(self.holding.items()):
            if held in changed:
                del self.holding[actor]
                self.entities[held].held_by = None
        self.style = dict(snapshot["style"])
        return {"affected": sorted(changed)[:256]}

    # ------------------------------------------------------------------ queries

    def _query(self, principal: str, message: dict) -> dict:
        op = message["op"]
        args = message["args"]
        result = self._base(principal, message)
        if op == "room.describe":
            counts = {"objects": 0, "creations": 0, "shell_parts": 0}
            for entity in self.entities.values():
                if entity.removed:
                    continue
                if entity.kind == "object":
                    counts["objects"] += 1
                elif entity.kind == "creation":
                    counts["creations"] += 1
                elif entity.kind == "shell":
                    counts["shell_parts"] += 1
            result["data"] = {
                "room_id": self.room_id, "display_name": textsafety.display_text(self.room["display_name"], 80),
                "revision": self.revision, "source_kind": self.room["source"]["kind"], "bounds_m": self.room_bounds,
                "style": dict(self.style), "counts": counts,
            }
        elif op == "entities.list":
            items = [e for e in sorted(self.entities.values(), key=lambda e: e.id) if not e.removed]
            items = [e for e in items if _matches(e, args.get("filter", {}))]
            offset = _cursor(args.get("cursor"))
            limit = args.get("limit", 50)
            page = items[offset:offset + limit]
            result["data"] = {"items": [e.summary() for e in page]}
            if offset + limit < len(items):
                result["data"]["next_cursor"] = str(offset + limit)
        elif op == "entity.inspect":
            entity = self._require(args["target"], "$.args.target")
            data: dict = {"entity": entity.summary()}
            if entity.parts:
                data["parts"] = dict(entity.parts)
            if entity.protected_by:
                data["protected_by"] = entity.protected_by
            result["data"] = data
        elif op == "capabilities.list":
            names = sorted(n for n, (cat, _p) in CAPABILITIES.items() if args.get("category") in (None, cat))
            offset = _cursor(args.get("cursor"))
            limit = args.get("limit", 50)
            items = [{"capability": n, "category": CAPABILITIES[n][0],
                      "params": {p: {"min": lo, "max": hi} for p, (lo, hi) in CAPABILITIES[n][1].items()},
                      "area_radius_max_m": 10, "duration_max_s": 600}
                     for n in names[offset:offset + limit]]
            result["data"] = {"items": items}
            if offset + limit < len(names):
                result["data"]["next_cursor"] = str(offset + limit)
        elif op == "observe":
            actor = self._actor(principal, args)
            radius = float(args.get("radius_m", self.policy.observe_default_radius_m))
            origin = self.entities[actor].position
            visible = []
            for entity in self.entities.values():
                if entity.removed or entity.kind == "shell" or entity.id == actor:
                    continue
                distance = _distance_to_box(origin, entity.bounds())
                if distance <= radius:
                    visible.append((distance, entity.id, entity))
            visible.sort(key=lambda item: (item[0], item[1]))
            visible = visible[:100]
            texts = []
            for _d, _id, entity in visible:
                for text in entity.texts:
                    if len(texts) < 50:
                        texts.append({"source": entity.id, "text": textsafety.long_text(text, 500), "untrusted": True})
            result["data"] = {"actor": actor, "visible": [e.summary() for _d, _i, e in visible], "texts": texts}
        elif op == "jobs.status":
            raise HostError("target_not_found", "No job with that id is running.", field_path="$.args.job_id")
        elif op == "receipt.lookup":
            receipt = self.receipts.get((principal, args["action_id"]))
            result["data"] = {"found": receipt is not None}
            if receipt is not None:
                result["data"]["receipt"] = copy.deepcopy(receipt.result)
        elif op == "approval.status":
            approval = self.approvals.get(args["request_id"])
            if approval is None or approval.principal != principal:
                # Unknown and other principals' requests look identical.
                raise HostError("target_not_found", "No approval request with that id is yours.",
                                field_path="$.args.request_id")
            data = {"request_id": approval.request_id, "state": approval.state}
            if approval.result is not None:
                data["result"] = copy.deepcopy(approval.result)
            result["data"] = data
        else:  # pragma: no cover - the schema already restricts ops
            raise HostError("request_invalid", "Unknown operation.", field_path="$.op")
        return result


def _targets(args: dict) -> list[str]:
    found = []
    if isinstance(args.get("target"), str):
        found.append(args["target"])
    for target in args.get("targets", []) if isinstance(args.get("targets"), list) else []:
        if isinstance(target, str):
            found.append(target)
    return found


def _find_authority_key(value: Any, path: list) -> list | None:
    if isinstance(value, dict):
        for key, item in value.items():
            if isinstance(key, str) and key.lower() in AUTHORITY_KEYS:
                return path + [key]
            hit = _find_authority_key(item, path + [key])
            if hit is not None:
                return hit
    elif isinstance(value, list):
        for index, item in enumerate(value):
            hit = _find_authority_key(item, path + [index])
            if hit is not None:
                return hit
    return None


def _approval_reason(message: dict) -> str:
    op = message["op"]
    target = message["args"].get("target")
    words = {
        "entity.remove": "remove", "entity.transform": "transform", "creation.revise": "change",
        "room.undo": "undo the room to an earlier revision", "style.set": "change the room's style",
    }
    verb = words.get(op, op)
    if isinstance(target, str):
        return f"Your companion wants to {verb} {target}. Approve or deny in the game."
    return f"Your companion wants to {verb}. Approve or deny in the game."


def _inside(point, bounds, tolerance: float = 1e-3) -> bool:
    return all(bounds["min_m"][i] - tolerance <= point[i] <= bounds["max_m"][i] + tolerance for i in range(3))


def _clamp(value: float) -> float:
    return _r(max(-1000.0, min(1000.0, value)))


def _distance_to_box(point, box) -> float:
    total = 0.0
    for i in range(3):
        low, high = box["min_m"][i], box["max_m"][i]
        nearest = min(max(point[i], low), high)
        total += (point[i] - nearest) ** 2
    return math.sqrt(total)


def _matches(entity: Entity, flt: dict) -> bool:
    if "kind" in flt and entity.kind != flt["kind"]:
        return False
    if "category_group" in flt and entity.category_group != flt["category_group"]:
        return False
    if "affordance" in flt and flt["affordance"] not in entity.affordances:
        return False
    if "provenance_kind" in flt and entity.provenance_kind != flt["provenance_kind"]:
        return False
    if "near" in flt and _distance_to_box(flt["near"]["center_m"], entity.bounds()) > flt["near"]["radius_m"]:
        return False
    return True


def _cursor(cursor: str | None) -> int:
    if cursor is None:
        return 0
    if not cursor.isascii() or not cursor.isdigit() or len(cursor) > 6:
        raise HostError("invalid_args", "That cursor did not come from this game.", field_path="$.args.cursor")
    return int(cursor)
