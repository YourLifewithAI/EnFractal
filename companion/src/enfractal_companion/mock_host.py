"""A mock game host that implements the command contract faithfully enough for P1's host to replace it.

It loads a real room (`game/rooms/test_room` by default) through the room contract and answers
`enfractal.command` and `enfractal.query` messages with `enfractal.result` messages:

- the principal comes from the caller (the trusted transport), never from the message;
- every message is validated against the contract (with ECMA-262 patterns), plus the value rules
  (int64, finite numbers, no hidden characters) and the semantic rules a schema cannot express;
- commands are idempotent per principal and action_id, fingerprinted over the canonical command
  JSON as received (an explicit `"preview": false` is the same as none); same content replays the
  receipt, different content is `action_id_conflict`;
- `goal.stop` and `effect.stop` always apply: they never fail on revisions, rate limits, a full
  receipt ledger or a reused action_id, and the player's stop also stops the companion;
- `expected_revision` and `expected_entities` are checked here, never by the sender;
- goals, effects and grabs get transient receipts (bounded per principal, oldest dropped first);
  everything else gets a durable receipt (at most 4,096 per room, like room state, of which the
  last 256 only the player's commands may use), and a checkpoint compacts the durable ones, after
  which `receipt.lookup` answers `compacted: true`;
- a companion perceives only what is in line of sight of its avatar (perception.py), and
  `observe`, `entities.list`, `entity.inspect`, the counts in `room.describe` and command targets
  all use that perception;
- a companion remembers what its own avatar saw this session (perception memory): queries show
  remembered things marked `seen: "remembered"` with their age and `may_be_stale`, and goals that
  only move or turn the companion may aim at them, re-checked on arrival; anything that changes an
  entity still needs it in sight now. Memory is per companion, bounded, cleared with the session
  or the room, never saved, and never filled through another avatar;
- commands in the policy's held set wait for the player: the host mints a 128-bit `request_id`, the
  player approves or denies through `player_decide` (the game UI, never the companion's
  connection), and an approved command commits under its original principal and action_id with
  `approved_by`; approvals expire, and lapse if the entities they touch change. Every safety
  property also holds with an empty held set;
- protection is the player's: `protect.unlock` (every op in `$defs/player_only_ops`) is refused
  from companion principals, and no companion command (including `room.undo`) changes a protected
  entity or any protection state except adding a lock;
- a companion's `room.undo` steps back only over revisions its own commands made: undoing the
  player's changes is the player's alone;
- unknown, removed, foreign and unperceived entity ids all fail with the same `target_not_found`;
- world text is emitted only in contract fields and sanitised (textsafety.py);
- every result is validated before it leaves; one that would not validate becomes `internal_error`.

The world model is deliberately simple (boxes, no physics): Run 2 replaces it with the kernel.
What must carry over is the message behaviour, which the boundary tests pin down.
"""
from __future__ import annotations

import copy
import hashlib
import logging
import math
import secrets
import threading
import time
from collections import OrderedDict
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable

from . import perception, textsafety
from .canonical import CanonicalJsonError
from .contract import (
    COMMAND_SCHEMA,
    QUERY_SCHEMA,
    Contracts,
    DEFAULT_REPO_ROOT,
    creation_sources,
    find_forbidden_key,
    is_authority_key,
    value_problems,
)
from .refusals import HostError, base_result, failure, forbidden_key_error, map_schema_errors, value_error

log = logging.getLogger("enfractal.mock_host")

PLAYER = "player:local"
COMPANION = "companion:local"
OWN_AVATAR = {PLAYER: "avatar:player", COMPANION: "avatar:companion"}
STOP_OPS = frozenset({"goal.stop", "effect.stop"})
TRANSIENT_OPS = frozenset({"entity.grab", "creation.activate", "goal.set", "goal.stop", "effect.start", "effect.stop"})
# Durable, but they change no world state, so they do not move the room revision.
NO_REVISION_OPS = frozenset({"room.checkpoint"})
LOCKABLE_KINDS = frozenset({"object", "creation"})
# A new style.set may pin only a reviewed preset. Seed and draft files still change (contracts/README.md
# "Styles are versioned files"); a retired preset stays loadable for old saves but is not offered for new pins.
PINNABLE_STATUSES = frozenset({"candidate", "approved"})
OCCLUDING_KINDS = frozenset({"shell", "object", "creation"})
# What perception memory keeps: everything but the shell, which is always in sight.
REMEMBERED_KINDS = frozenset({"object", "creation", "avatar", "effect"})
# Goals that only move or turn the companion's own avatar may aim at something it remembers but
# cannot see now (founder decision, 6 October 2026). The host re-checks when the avatar arrives.
REMEMBERED_TARGET_GOALS = frozenset({"go_to", "look_at", "point_at", "come", "fetch"})

# Both avatars have the 10 cm body (perception.py): radius 0.02 m, height 0.10 m, as Entity half_extents.
_BODY = [perception.BODY_RADIUS_M, perception.BODY_HEIGHT_M, perception.BODY_RADIUS_M]

DEFAULT_ROOM_DIR = DEFAULT_REPO_ROOT / "game" / "rooms" / "test_room"
DEFAULT_STYLES_DIR = DEFAULT_REPO_ROOT / "game" / "styles"

# Effect capabilities the mock supports: name -> (category, {param: (min, max)}).
CAPABILITIES: dict[str, tuple[str, dict[str, tuple[float, float]]]] = {
    "wind_field": ("air", {"speed_mps": (0.0, 5.0), "direction_deg": (0.0, 360.0)}),
    "glow": ("light", {"intensity": (0.0, 1.0)}),
}

RECOMMENDED_HELD_OPS = frozenset({"entity.remove", "entity.transform", "creation.revise", "room.undo", "style.set"})


@dataclass
class HostPolicy:
    """The one policy table. Values the founder decides are marked; see docs/companion/SECURITY.md."""

    # Companion commands the host holds for the player's click. The founder's goal is none
    # (live play by conversation); every safety property is tested with this set empty.
    companion_approval_ops: frozenset[str] = RECOMMENDED_HELD_OPS
    approval_ttl_s: float = 300.0
    max_pending_approvals: int = 3
    # Token buckets per principal. Stop ops are never limited. (Founder decision; recommended values.)
    command_rate_per_s: float = 2.0
    command_burst: int = 10
    query_rate_per_s: float = 10.0
    query_burst: int = 30
    # Perception (founder decision 1: line of sight). See docs/companion/PERCEPTION.md.
    observe_max_radius_m: float = 20.0
    companion_targets_need_perception: bool = True  # commands may only name entities in sight now
    follow_player_out_of_sight: bool = True  # follow/come keep working when the player is not in sight
    # Perception memory (founder decision, 6 October 2026: the companion remembers what it saw).
    # At most this many entities per companion, least recently seen forgotten first; 0 turns it off.
    perception_memory_entries: int = 256
    # A remembered entity is flagged may_be_stale after this long, or as soon as it changes.
    perception_memory_stale_after_s: float = 60.0
    max_jobs_per_principal: int = 256  # goal jobs kept for jobs.status, oldest finished dropped first
    # Effects (open founder question: may companion effects touch the player's avatar?).
    companion_effects_may_target_player: bool = True
    max_effects_per_principal: int = 4
    max_creations: int = 32
    carry_limit_kg: dict[str, float] = field(default_factory=lambda: {"avatar:player": 0.5, "avatar:companion": 2.0})
    history_depth: int = 64
    max_checkpoints: int = 64  # room-state keeps at most 64
    max_durable_receipts: int = 4096  # per room, across principals, like room-state receipts
    # The last slots of the ledger are the player's: a companion that fills the ledger can never block
    # the player's own commands (a lock, above all) until a checkpoint compacts it.
    player_receipt_reserve: int = 256
    max_transient_receipts_per_principal: int = 1024
    max_compacted_receipts: int = 16384


NO_HOLDS = HostPolicy(companion_approval_ops=frozenset())


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
    # One answer for unknown, removed, other-room and unperceived ids: it confirms nothing about what exists.
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

    def summary(self, text: textsafety.TextRules) -> dict:
        out = {
            "id": self.id,
            "kind": self.kind,
            "display_name": text.display_text(self.display_name, 80),
            "position_m": [_r(v) for v in self.position],
            "bounds_m": self.bounds(),
            "affordances": list(dict.fromkeys(self.affordances)),
            "movable": self.movable,
            "protected": self.protected,
            "provenance_kind": self.provenance_kind,
            "revision": self.revision,
        }
        if self.category:
            out["category"] = text.display_text(self.category, 60)
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
class Compacted:
    fingerprint: str
    op: str
    revision: int


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


@dataclass
class Remembered:
    """One entity as a companion last saw it. Only `view` and the timing ever reach the companion."""

    view: dict  # {"summary", "parts", "protected_by"}: what was perceivable, sanitised, at the last sighting
    seen_at: float
    seen_revision: int  # the room revision at the last sighting
    mass_kg: float | None  # static asset data for the fetch check; never emitted
    changed: bool = False  # it changed in some way since (sticky until seen again); emitted only as may_be_stale


@dataclass
class PerceptionMemory:
    """What one companion has seen in this session and room, least recently seen first."""

    room_id: str
    entries: OrderedDict = field(default_factory=OrderedDict)  # entity id -> Remembered


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
                 policy: HostPolicy | None = None, clock=None, extra_companions: dict[str, str] | None = None):
        self.contracts = contracts
        self.policy = policy or HostPolicy()
        self.clock = clock or SystemClock()
        self._lock = threading.RLock()
        self.room_dir = Path(room_dir or DEFAULT_ROOM_DIR)
        self.style_status: dict[tuple[str, int], str] = {}
        self.styles = self._index_styles(Path(styles_dir or DEFAULT_STYLES_DIR))
        # principal -> its own avatar. Tests add a second companion to prove principals stay apart.
        self.avatars: dict[str, str] = dict(OWN_AVATAR)
        self.avatars.update(extra_companions or {})
        self.text = contracts.text_rules  # the untrusted-text rule this contract can carry
        self.buckets: dict[tuple[str, str], _Bucket] = {}
        self._view: set[str] | None = None  # what the current requester may name; None means everything
        self.listeners: list[Callable[[str, dict], None]] = []
        self._known_ops = frozenset(contracts.command_ops) | frozenset(contracts.query_ops)
        self._reset_room()

    def _reset_room(self) -> None:
        """Everything that belongs to one loaded room, perception memory included."""
        self.entities: dict[str, Entity] = {}
        self.revision = 0
        self.receipts: dict[tuple[str, str], Receipt] = {}  # durable, at most max_durable_receipts
        self.transient: OrderedDict[tuple[str, str], Receipt] = OrderedDict()
        self.compacted: OrderedDict[tuple[str, str], Compacted] = OrderedDict()
        self.terminal: dict[tuple[str, str], Receipt] = {}  # denied or lapsed approvals, replayed as-is
        self.approvals: dict[str, Approval] = {}
        self.approval_by_action: dict[tuple[str, str], str] = {}
        self.goals: dict[str, dict] = {}
        self.jobs: OrderedDict[str, dict] = OrderedDict()  # goal jobs for jobs.status, oldest first
        self.holding: dict[str, str] = {}
        self.checkpoints: list[dict] = []
        self.history: dict[int, dict] = {}
        self.committed_by: dict[int, str] = {}  # revision -> the principal whose command made it
        # Perception memory, per companion principal. In memory only: never in a snapshot, a receipt or a save.
        self.memory: dict[str, PerceptionMemory] = {}
        self._creation_counter = 0
        self._effect_counter = 0
        self._checkpoint_counter = 0
        self._job_counter = 0
        self._load_room()
        self.history[0] = self._snapshot()

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
                self.style_status[key] = preset.get("status", "seed")
        return found

    def _load_room(self) -> None:
        load = self.contracts.validate.load_strict
        room = load(self.room_dir / "room.json")
        problems = self.contracts.validate.schema_errors(room)
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
            half_extents=list(_BODY), affordances=[], movable=False, provenance_kind="hand_authored")
        for offset, (principal, avatar) in enumerate(sorted((p, a) for p, a in self.avatars.items() if p != PLAYER)):
            position = list(companion_spawn["position_m"])
            position[0] += 0.15 * offset
            name = "Wisp" if avatar == "avatar:companion" else avatar.split(":", 1)[1].capitalize()
            self.entities[avatar] = Entity(
                id=avatar, kind="avatar", display_name=name, position=position,
                half_extents=list(_BODY), affordances=[], movable=False, provenance_kind="hand_authored")
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

    def move_entity(self, entity_id: str, position: list[float]) -> None:
        """The world moving something by itself (physics, the player's hands) between requests."""
        with self._lock:
            self.entities[entity_id].position = list(position)

    def load_room(self, room_dir: Path) -> None:
        """The game switches rooms: a fresh room state, and every companion's perception memory goes."""
        with self._lock:
            self.room_dir = Path(room_dir)
            self._reset_room()

    def session_event(self, principal: str, event: str) -> None:
        """The link reports a companion session starting or ending ("start", "end"). Perception memory
        never outlives a session: either event clears that companion's memory."""
        with self._lock:
            self.memory.pop(principal, None)

    # ------------------------------------------------------------------ perception

    def perceived(self, principal: str) -> set[str]:
        """Ids the principal's avatar perceives now: line of sight from its eye, plus the room shell
        (it stands inside it), its own avatar and whatever it holds. Pure: it remembers nothing."""
        with self._lock:
            avatar_id = self.avatars[principal]
            avatar = self.entities[avatar_id]
            eye = perception.eye_point(avatar.position, "player" if principal.startswith("player:") else "companion")
            occluders = self._occluders()
            seen = {avatar_id}
            for entity in self.entities.values():
                if entity.removed:
                    continue
                if entity.kind == "shell" or entity.held_by == avatar_id:
                    seen.add(entity.id)
                elif entity.id != avatar_id:
                    box = entity.bounds()
                    if perception.visible(eye, entity.id, box["min_m"], box["max_m"], occluders):
                        seen.add(entity.id)
            return seen

    def _view_for(self, principal: str, *, for_command: bool) -> set[str] | None:
        if principal.startswith("player:"):
            return None  # the player's own controls and UI see the whole room
        seen = self.perceived(principal)
        self._look(principal, seen)
        if for_command and not self.policy.companion_targets_need_perception:
            return None
        return seen

    # ------------------------------------------------------------------ perception memory

    def _perceivable(self, entity: Entity) -> dict:
        """What a look at the entity shows: its summary, parts and who protected it, sanitised."""
        return {"summary": entity.summary(self.text), "parts": dict(entity.parts), "protected_by": entity.protected_by}

    def _memory(self, principal: str) -> PerceptionMemory:
        memory = self.memory.get(principal)
        if memory is None or memory.room_id != self.room_id:
            memory = self.memory[principal] = PerceptionMemory(self.room_id)
        return memory

    def _look(self, principal: str, seen: set[str]) -> None:
        """Update a companion's memory from what its own avatar sees now. Only this fills memory, and
        only from that avatar's line of sight: never through the player's avatar or another companion's.

        - What it remembers but cannot see is checked against the room: any change at all (moved,
          edited, picked up, locked or gone) marks it changed, which the companion learns only as
          may_be_stale.
        - If the place it was last seen is in sight now and it is not there, the companion has looked
          again: the memory is dropped. Until then, a thing removed out of sight is remembered as it was.
        - What it sees now is remembered afresh, nearest last, and the least recently seen are
          forgotten beyond the size bound.
        """
        limit = self.policy.perception_memory_entries
        memory = self._memory(principal)
        if limit <= 0:
            memory.entries.clear()
            return
        avatar = self.entities[self.avatars[principal]]
        eye = perception.eye_point(avatar.position, "companion")
        occluders = None
        for entity_id, entry in list(memory.entries.items()):
            if entity_id in seen:
                continue
            entity = self.entities.get(entity_id)
            if not entry.changed and (entity is None or entity.removed or self._perceivable(entity) != entry.view):
                entry.changed = True
            if occluders is None:
                occluders = self._occluders()
            box = entry.view["summary"]["bounds_m"]
            if perception.visible(eye, entity_id, box["min_m"], box["max_m"], occluders):
                del memory.entries[entity_id]
        now = self.clock.now()
        fresh = sorted((self.entities[e] for e in seen
                        if e != avatar.id and self.entities[e].kind in REMEMBERED_KINDS),
                       key=lambda e: (-_distance_to_box(eye, e.bounds()), e.id))
        for entity in fresh:
            memory.entries.pop(entity.id, None)
            memory.entries[entity.id] = Remembered(self._perceivable(entity), now, self.revision, entity.mass_kg)
        while len(memory.entries) > limit:
            memory.entries.popitem(last=False)

    def _occluders(self) -> list:
        return [(e.id, *perception.padded(e.bounds()["min_m"], e.bounds()["max_m"]))
                for e in self.entities.values() if not e.removed and e.kind in OCCLUDING_KINDS]

    def _recall(self, principal: str, entity_id: str) -> Remembered | None:
        """The companion's memory of an entity it cannot see now; None for the player, for anything in
        sight and for anything it never saw (or has seen is gone)."""
        if principal.startswith("player:") or self._view is None or entity_id in self._view:
            return None
        memory = self.memory.get(principal)
        if memory is None or memory.room_id != self.room_id:
            return None
        return memory.entries.get(entity_id)

    def _remembered(self, principal: str) -> dict[str, Remembered]:
        """Every remembered entity out of sight now, when the contract can say so in results."""
        if not self.contracts.memory_fields or principal.startswith("player:") or self._view is None:
            return {}
        memory = self.memory.get(principal)
        if memory is None or memory.room_id != self.room_id:
            return {}
        return {entity_id: entry for entity_id, entry in memory.entries.items() if entity_id not in self._view}

    def _memory_fields(self, entry: Remembered) -> dict:
        """How a remembered entity is marked. may_be_stale is one bit: old, or changed in any way. It
        never says which, or anything about the entity now."""
        age = max(0.0, self.clock.now() - entry.seen_at)
        return {"seen": "remembered", "last_seen_ago_s": round(age, 1), "last_seen_revision": entry.seen_revision,
                "may_be_stale": entry.changed or age >= self.policy.perception_memory_stale_after_s}

    def _summary(self, principal: str, entity: Entity) -> dict:
        """An entity in sight now, as the requester sees it."""
        summary = entity.summary(self.text)
        if self.contracts.memory_fields and not principal.startswith("player:"):
            summary["seen"] = "now"
        return summary

    def _remembered_summary(self, entry: Remembered) -> dict:
        return {**copy.deepcopy(entry.view["summary"]), **self._memory_fields(entry)}

    # ------------------------------------------------------------------ the wire

    def handle(self, principal: str, message: Any) -> dict:
        """Answer one parsed message from `principal`, which the trusted transport assigned."""
        if principal not in self.avatars:
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
            finally:
                self._view = None
            problems = self.contracts.schema_errors(result)
            if problems:
                log.error("mock host built an invalid result: %s", problems[:3])
                result = self._fail(principal, None, HostError(
                    "internal_error", "The game could not handle that request.", retryable=True))
            return result

    def handle_bytes(self, principal: str, raw: bytes) -> dict:
        """Parse strictly (no duplicate keys, NaN or overflow), then answer."""
        try:
            message = self.contracts.loads_strict(raw.decode("utf-8"))
        except (UnicodeDecodeError, self.contracts.ContractError, ValueError, RecursionError):
            # ValueError: an integer literal longer than Python converts; RecursionError: absurd nesting.
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
            raise forbidden_key_error(hit, "authority" if is_authority_key(str(hit[-1])) else "key_format")
        if op in self.contracts.player_only_ops and not principal.startswith("player:"):
            raise HostError("permission_denied", "Only the player can do that, directly in the game.", field_path="$.op")
        if is_command:
            self._view = self._view_for(principal, for_command=True)
            return self._command(principal, message)
        self._view = self._view_for(principal, for_command=False)
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
        problems = value_problems(message)
        if message.get("schema") == COMMAND_SCHEMA and message.get("op") in STOP_OPS:
            # A stop ignores revisions, so a revision outside int64 is no reason to refuse it.
            problems = [(path, kind) for path, kind in problems
                        if not (kind == "integer_range" and path[:1] in (["expected_revision"], ["expected_entities"]))]
        if problems:
            raise value_error(problems)
        try:
            canonical_size = len(self.contracts.canonical_bytes(message))
        except CanonicalJsonError:
            raise HostError("request_invalid", "The request cannot be written as canonical JSON (too deeply nested).") from None
        limit = self.contracts.message_limits[message["schema"]]
        if canonical_size > limit:
            raise HostError("request_invalid", f"The request is larger than {limit} bytes of canonical JSON.",
                            field_path="$", allowed=limit, actual=canonical_size)
        errors = list(self.contracts.iter_errors(message))
        if errors:
            raise map_schema_errors(errors)
        for path, source in creation_sources(message):
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

    def _fingerprint(self, message: dict) -> str:
        """SHA-256 of the command as received in canonical JSON v1 (the kernel host's form, see
        canonical.py); an explicit `"preview": false` equals none."""
        if message.get("preview") is False:
            message = {k: v for k, v in message.items() if k != "preview"}
        return hashlib.sha256(self.contracts.canonical_bytes(message)).hexdigest()

    def _command(self, principal: str, message: dict) -> dict:
        op = message["op"]
        action_id = message["action_id"]
        key = (principal, action_id)
        handler = getattr(self, "_op_" + op.replace(".", "_"))
        preview = message.get("preview") is True
        if op in STOP_OPS and not preview:
            # A stop always applies, whatever the ledger or an earlier use of this action id says.
            result = self._commit(principal, message, handler)
            self._store_receipt(key, self._fingerprint(message), result)
            return result
        fingerprint = self._fingerprint({k: v for k, v in message.items() if k != "preview"} if preview else message)
        earlier = self._earlier(key, fingerprint, message)
        if earlier is not None:
            return earlier
        self._check_revisions(principal, message)
        prediction = handler(principal, message, False, None)
        if preview:
            result = self._base(principal, message)
            result["preview"] = True
            result.update(prediction)
            result.pop("transient", None)
            return result
        if principal != PLAYER and op in self.policy.companion_approval_ops:
            return self._hold(principal, message, fingerprint, prediction)
        if op not in TRANSIENT_OPS and op != "room.checkpoint":
            self._check_receipt_room(principal)
        result = self._commit(principal, message, handler)
        self._store_receipt(key, fingerprint, result)
        return result

    def _earlier(self, key: tuple[str, str], fingerprint: str, message: dict) -> dict | None:
        """The answer for an action id this principal already used, or None if it is new."""
        for store in (self.receipts, self.transient, self.terminal):
            if key in store:
                return self._replay(store[key], fingerprint, message)
        if key in self.compacted:
            entry = self.compacted[key]
            if entry.fingerprint != fingerprint:
                raise HostError("action_id_conflict", "That action id was already used for a different command.",
                                field_path="$.action_id")
            replay = self._base(key[0], message)
            replay.update({"replayed": True, "revision": entry.revision, "transient": False})
            return replay
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
        return None

    def _commit(self, principal: str, message: dict, handler, approved_by: str | None = None) -> dict:
        op = message["op"]
        durable = op not in TRANSIENT_OPS
        moves_revision = durable and op not in NO_REVISION_OPS
        new_revision = self.revision + 1 if moves_revision else None
        outcome = handler(principal, message, True, new_revision)
        if moves_revision:
            self.revision = new_revision
            self.history[new_revision] = self._snapshot()
            self.committed_by[new_revision] = principal
            for old in [r for r in self.history if r < new_revision - self.policy.history_depth]:
                del self.history[old]
                self.committed_by.pop(old, None)
        result = self._base(principal, message)
        result.update(outcome)
        result["transient"] = not durable
        if approved_by:
            result["approved_by"] = approved_by
        self._emit("committed", {"principal": principal, "op": op, "action_id": message["action_id"]})
        return result

    def _check_receipt_room(self, principal: str) -> None:
        limit = self.policy.max_durable_receipts
        if not principal.startswith("player:"):
            limit = max(0, limit - self.policy.player_receipt_reserve)
        if len(self.receipts) >= limit:
            raise HostError("receipt_limit", "The receipt ledger is full. A checkpoint compacts it.", retryable=True)

    def _store_receipt(self, key: tuple[str, str], fingerprint: str, result: dict) -> None:
        receipt = Receipt(fingerprint, copy.deepcopy(result), durable=not result.get("transient", False))
        if receipt.durable:
            self.receipts[key] = receipt
            return
        self.transient.pop(key, None)
        self.transient[key] = receipt
        mine = [k for k in self.transient if k[0] == key[0]]
        for old in mine[:max(0, len(mine) - self.policy.max_transient_receipts_per_principal)]:
            del self.transient[old]

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
        for entity_id in _targets(message["args"]) + list(prediction.get("affected", [])):
            entity = self._entity(entity_id)
            if entity is not None:
                touched[entity_id] = entity.revision
        result = self._base(principal, message)
        result["ok"] = False
        result["approval_needed"] = {
            "request_id": request_id,
            "reason": self._approval_reason(message),
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

    def _approval_reason(self, message: dict) -> str:
        """What the player is asked to approve, stated as its concrete effect."""
        op, args = message["op"], message["args"]

        def named(entity_id: str) -> str:
            entity = self.entities.get(entity_id)
            name = self.text.display_text(entity.display_name, 40) if entity else "unknown"
            revision = entity.revision if entity else 0
            return f'{entity_id} ("{name}", revision {revision})'

        if op == "entity.remove":
            what = f"remove {named(args['target'])} from the room"
        elif op == "entity.transform":
            new_name = self.text.display_text(args["into"]["source"]["name"], 40)
            what = f'turn {named(args["target"])} into "{new_name}"'
        elif op == "creation.revise":
            parts = []
            if "source" in args:
                parts.append(f'rebuild it as "{self.text.display_text(args["source"]["name"], 40)}"')
            if "placement" in args:
                x, y, z = (round(v, 2) for v in args["placement"]["position_m"])
                parts.append(f"move it to ({x}, {y}, {z})")
            what = f"change {named(args['target'])}: " + " and ".join(parts)
        elif op == "room.undo":
            changed = self._undo_plan(args["to_revision"])
            listed = ", ".join(entity_id for entity_id, _state in changed[:6])
            more = f" and {len(changed) - 6} more" if len(changed) > 6 else ""
            what = (f"undo the room from revision {self.revision} to revision {args['to_revision']}, "
                    f"changing {len(changed)} entities: {listed or 'none'}{more}")
        elif op == "style.set":
            what = (f"switch the room style from {self.style['preset_id']} v{self.style['preset_version']} "
                    f"to {args['preset_id']} v{args['preset_version']}")
        else:
            targets = ", ".join(_targets(args)) or "the room"
            what = f"{op} on {targets}"
        reason = f"Your companion wants to {what}. Approve or deny in the game."
        return reason if len(reason) <= 280 else reason[:276] + "..."

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
            self._view = None
            try:
                if not approve:
                    self._finish(approval, "denied", HostError(
                        "permission_denied", "The player declined this change. To ask again, use a new action_id."))
                elif self._lapsed(approval):
                    self._finish(approval, "expired", HostError(
                        "approval_mismatch", "The room changed before the player approved. To ask again, use a new action_id."))
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
                        self._finish(approval, "expired", error if error.code == "receipt_limit" else HostError(
                            "approval_mismatch", "The change can no longer be made as approved. To ask again, use a new action_id."))
            finally:
                self._view = None
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
                self._finish(approval, "expired", HostError(
                    "approval_expired", "The player did not answer in time. To ask again, use a new action_id."))
                self.approval_by_action.pop((approval.principal, approval.action_id), None)
        for entity in list(self.entities.values()):
            if entity.kind == "effect" and not entity.removed and entity.expires_at is not None and now >= entity.expires_at:
                entity.removed = True

    # ------------------------------------------------------------------ helpers

    def _entity(self, entity_id: str) -> Entity | None:
        entity = self.entities.get(entity_id)
        if entity is None or entity.removed:
            return None
        if self._view is not None and entity_id not in self._view:
            return None  # out of sight: indistinguishable from an id that does not exist
        return entity

    def _require(self, entity_id: str, field_path: str) -> Entity:
        entity = self._entity(entity_id)
        if entity is None:
            raise _not_found(field_path)
        return entity

    def _directable(self, principal: str) -> set[str]:
        """Avatars this principal may direct: its own, and the player may direct every companion."""
        own = {self.avatars[principal]}
        if principal.startswith("player:"):
            own |= {a for p, a in self.avatars.items() if not p.startswith("player:")}
        return own

    def _principal_of(self, avatar_id: str) -> str | None:
        return next((p for p, a in self.avatars.items() if a == avatar_id), None)

    def _actor(self, principal: str, args: dict) -> str:
        actor = args.get("actor", self.avatars[principal])
        if actor not in self._directable(principal):
            raise HostError("actor_denied", "A companion acts and observes only through its own avatar.",
                            field_path="$.args.actor")
        if self.entities.get(actor) is None:
            raise _not_found("$.args.actor")
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

    def _undo_plan(self, to_revision: int) -> list[tuple[str, dict | None]]:
        """(entity id, the state it would get, or None to remove it) for every entity undo would change."""
        snapshot = self.history.get(to_revision)
        if snapshot is None:
            return []
        changes: list[tuple[str, dict | None]] = []
        for entity_id, state in snapshot["entities"].items():
            entity = self.entities.get(entity_id)
            current = entity.durable_state() if entity else None
            if current is not None and {k: v for k, v in current.items() if k != "revision"} == \
                    {k: v for k, v in state.items() if k != "revision"}:
                continue
            changes.append((entity_id, state))
        for entity_id, entity in self.entities.items():
            if entity.kind in ("object", "creation") and entity_id not in snapshot["entities"] and not entity.removed:
                changes.append((entity_id, None))
        return sorted(changes, key=lambda item: item[0])

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
        if held_id is None or self.entities.get(held_id) is None or self.entities[held_id].removed:
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
        if target.held_by and target.held_by != self.avatars[principal]:
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
            if target.held_by:
                # A held thing could be carried off and put down elsewhere after the lock. Put it down first.
                raise HostError("target_busy", "Someone is holding that; it can be protected once it is put down.",
                                field_path=f"$.args.targets[{i}]")
        if not apply:
            return {"affected": [t.id for t in targets]}
        for target in targets:
            if target.protected:
                continue  # already protected: the lock and who set it stay as they are
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
        if args["goal"] in ("follow", "come") and "target" not in args and not principal.startswith("player:") \
                and not self.policy.follow_player_out_of_sight and "avatar:player" not in (self._view or {"avatar:player"}):
            raise HostError("target_not_found", "The player is not in sight.", field_path="$.args.goal")
        data = {"actor": actor, "goal": args["goal"]}
        aim = None
        if "target" in args:
            target = self._entity(args["target"])
            if target is not None:
                kind, movable, protected, mass, aim = (target.kind, target.movable, target.protected, target.mass_kg,
                                                       target.bounds())
                if not principal.startswith("player:"):
                    data["target_seen"] = "now"
            else:
                # Out of sight: only a goal that moves or turns the companion's own avatar may aim at a
                # remembered thing, and it is judged on the memory alone, so the answer cannot reveal
                # what became of it (moved, locked, removed). Changing things needs them in sight now.
                entry = self._recall(principal, args["target"]) if args["goal"] in REMEMBERED_TARGET_GOALS else None
                if entry is None:
                    raise _not_found("$.args.target")
                summary = entry.view["summary"]
                kind, movable, protected, mass, aim = (summary["kind"], summary["movable"], summary["protected"],
                                                       entry.mass_kg, summary["bounds_m"])
                data["target_seen"] = "remembered"
                fields = self._memory_fields(entry)
                data["last_seen_ago_s"], data["may_be_stale"] = fields["last_seen_ago_s"], fields["may_be_stale"]
            if args["goal"] == "fetch":
                if kind not in ("object", "creation") or not movable:
                    raise HostError("permission_denied", "That cannot be fetched.", field_path="$.args.target")
                if protected:
                    raise HostError("target_protected", "That is protected. Only the player can unlock it.",
                                    field_path="$.args.target")
                limit = self.policy.carry_limit_kg.get(actor, 0.0)
                if mass is not None and mass > limit:
                    raise HostError("target_too_heavy", "That is too heavy for this avatar.", field_path="$.args.target",
                                    allowed=limit, actual=mass)
        if "position_m" in args and not _inside(args["position_m"], self.room_bounds):
            raise HostError("out_of_bounds", "That position is outside the room.", field_path="$.args.position_m")
        if "area" in args:
            for corner in ("min_m", "max_m"):
                if not _inside(args["area"][corner], self.room_bounds):
                    raise HostError("out_of_bounds", "That area is outside the room.", field_path=f"$.args.area.{corner}")
        outcome = {"affected": [actor], "data": data}
        if apply:
            self._cancel_job(actor)
            goal = {k: copy.deepcopy(v) for k, v in args.items() if k != "actor"}
            goal["started_utc"] = utc(self.clock.now())
            goal["set_by"] = principal
            if aim is not None:
                # A goal with a target runs as a job: the host re-checks the target when the avatar arrives.
                outcome["job_id"] = goal["job_id"] = self._start_job(principal, actor, message["action_id"])
                goal["aim_bounds"] = copy.deepcopy(aim)
            self.goals[actor] = goal
        return outcome

    # ------------------------------------------------------------------ goal jobs (the goal runner's side)

    def _start_job(self, principal: str, actor: str, action_id: str) -> str:
        self._job_counter += 1
        job_id = f"goal-{self._job_counter:06d}"
        self.jobs[job_id] = {"principal": principal, "actor": actor, "action_id": action_id, "state": "running"}
        mine = [k for k, job in self.jobs.items() if job["principal"] == principal and job["state"] != "running"]
        excess = sum(1 for job in self.jobs.values() if job["principal"] == principal) - self.policy.max_jobs_per_principal
        for old in mine[:max(0, excess)]:
            del self.jobs[old]
        return job_id

    def _cancel_job(self, actor: str) -> None:
        goal = self.goals.get(actor)
        job = self.jobs.get(goal.get("job_id", "")) if goal else None
        if job is not None and job["state"] == "running":
            job["state"] = "cancelled"

    def goal_arrived(self, actor_id: str) -> str:
        """The game's goal runner: the avatar has reached its goal (walked there, or turned to look or
        point). The host re-checks the target from where the avatar is now, with the avatar's own line
        of sight, and finishes the goal's job honestly. Returns the job's state.

        - succeeded: the target is in sight and within reach (0.15 m) of where the goal aimed;
        - failed, revision_conflict: it is in sight, but has moved since (the companion sees where);
        - failed, target_not_found: it is not in sight. Moved out of sight and gone give the same answer.

        The mock models the arrival check only; carrying a fetched thing back is the Run 2 goal runner's.
        """
        with self._lock:
            goal = self.goals.get(actor_id)
            job = self.jobs.get(goal.get("job_id", "")) if goal else None
            if job is None or job["state"] != "running":
                raise KeyError("that avatar has no goal with a target running")
            principal = self._principal_of(actor_id) or job["principal"]
            seen = self.perceived(principal)
            if not principal.startswith("player:"):
                self._look(principal, seen)  # arriving is looking: memory is refreshed or dropped
            target = self.entities.get(goal["target"])
            error = None
            if target is None or target.removed or goal["target"] not in seen:
                error = HostError("target_not_found", "The target is not where it was seen. Observe and try again.",
                                  field_path="$.args.target")
            elif _box_gap(target.bounds(), goal["aim_bounds"]) > perception.REACH_M:
                error = HostError("revision_conflict", "The target has moved since it was seen. Observe and try again.",
                                  field_path="$.args.target", retryable=True)
            del self.goals[actor_id]
            if error is None:
                job["state"] = "succeeded"
            else:
                job["state"] = "failed"
                message = {"schema": COMMAND_SCHEMA, "op": "goal.set", "action_id": job["action_id"]}
                job["result"] = self._fail(job["principal"], message, error)
            self._emit("goal_finished", {"actor": actor_id, "job_id": goal["job_id"], "state": job["state"]})
            return job["state"]

    def _op_goal_stop(self, principal, message, apply, new_revision):
        # Always permitted. Without an actor it stops every actor the principal may direct, their
        # goals and their effects; the player's stop therefore stops the companion as well.
        args = message["args"]
        actors = [self._actor(principal, args)] if "actor" in args else sorted(self._directable(principal))
        owners = {self._principal_of(actor) for actor in actors}
        stopped_effects = sorted(e.id for e in self.entities.values()
                                 if e.kind == "effect" and not e.removed and e.created_by in owners)
        if apply:
            for actor in actors:
                self._cancel_job(actor)
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
            if target.id == "avatar:player" and not principal.startswith("player:") \
                    and not self.policy.companion_effects_may_target_player:
                raise HostError("permission_denied", "Companion effects may not target the player.",
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
        # Always permitted. The player may stop any companion's effects; a companion only its own.
        which = message["args"]["effect"]
        owners = {self._principal_of(avatar) for avatar in self._directable(principal)}
        stoppable = [e for e in self.entities.values() if e.kind == "effect" and not e.removed and e.created_by in owners]
        stopping = sorted((e for e in stoppable if which == "all" or e.id == which), key=lambda e: e.id)
        if apply:
            for effect in stopping:
                effect.removed = True
        return {"affected": [e.id for e in stopping]}

    def _op_style_set(self, principal, message, apply, new_revision):
        args = message["args"]
        key = (args["preset_id"], args["preset_version"])
        if key not in self.styles:
            raise HostError("invalid_args", "No style preset with that id and version is installed.", field_path="$.args.preset_id")
        if self.style_status.get(key) not in PINNABLE_STATUSES:
            # contracts/README.md "Styles are versioned files": nothing may pin a seed or draft preset.
            raise HostError("invalid_args", "That preset version is a seed, draft or retired preset and cannot be pinned.",
                            field_path="$.args.preset_version", allowed=sorted(PINNABLE_STATUSES))
        if not apply:
            return {}
        self.style = {"preset_id": key[0], "preset_version": key[1], "preset_sha256": self.styles[key]}
        return {}

    def _op_room_checkpoint(self, principal, message, apply, new_revision):
        # Records the current revision; does not move it, so a checkpoint never stales anyone's
        # expected_revision. Compacts the durable receipts it covers.
        if not apply:
            return {}
        self._checkpoint_counter += 1
        label = self.text.display_text(message["args"]["label"], 80) if message["args"].get("label") else None
        checkpoint = {"id": f"cp{self._checkpoint_counter:04d}", "revision": self.revision}
        if label:
            checkpoint["label"] = label
        self.checkpoints.append(checkpoint)
        del self.checkpoints[:max(0, len(self.checkpoints) - self.policy.max_checkpoints)]
        for key, receipt in list(self.receipts.items()):
            if receipt.result["revision"] <= self.revision:
                self.compacted[key] = Compacted(receipt.fingerprint, receipt.result["op"], receipt.result["revision"])
                del self.receipts[key]
        while len(self.compacted) > self.policy.max_compacted_receipts:
            self.compacted.popitem(last=False)
        return {"data": {"checkpoint_id": checkpoint["id"], "checkpoint_revision": self.revision}}

    def _op_room_undo(self, principal, message, apply, new_revision):
        to_revision = message["args"]["to_revision"]
        if to_revision >= self.revision or to_revision not in self.history:
            raise HostError("invalid_args", "That revision cannot be restored.", field_path="$.args.to_revision",
                            allowed=[min(self.history), max(self.revision - 1, 0)], actual=to_revision)
        changes = self._undo_plan(to_revision)
        if not principal.startswith("player:"):
            # Protection is the player's. A companion's undo may not touch a protected entity, and may
            # not change any entity's protection, in either direction.
            for entity_id, state in changes:
                entity = self.entities.get(entity_id)
                now_protected = bool(entity and not entity.removed and entity.protected)
                then_protected = bool(state and not state["removed"] and state["protected"])
                if now_protected or then_protected != now_protected:
                    raise HostError("target_protected",
                                    "Undoing to that revision would change something protected. Only the player can do that.",
                                    field_path="$.args.to_revision")
            # Undoing the player's edits is never the companion's (docs/companion/LIVE-VOICE.md, "never
            # exposed"): a companion's undo may only step back over revisions its own commands made.
            if any(self.committed_by.get(r) != principal for r in range(to_revision + 1, self.revision + 1)):
                raise HostError("permission_denied",
                                "A companion can undo only its own changes. Undoing the player's changes is the player's.",
                                field_path="$.args.to_revision")
        if not apply:
            return {"affected": [entity_id for entity_id, _state in changes][:256]}
        snapshot = self.history[to_revision]
        changed = []
        for entity_id, state in changes:
            if state is None:
                entity = self.entities[entity_id]
                entity.removed = True
                entity.revision = new_revision
            else:
                restored = Entity(id=entity_id, **{k: copy.deepcopy(v) for k, v in state.items()})
                restored.revision = new_revision
                self.entities[entity_id] = restored
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
            # Counts cover what the requester perceives: a count of everything would tell a
            # companion how many things are hidden from it.
            counts = {"objects": 0, "creations": 0, "shell_parts": 0}
            for entity in self.entities.values():
                if self._entity(entity.id) is None:
                    continue
                if entity.kind == "object":
                    counts["objects"] += 1
                elif entity.kind == "creation":
                    counts["creations"] += 1
                elif entity.kind == "shell":
                    counts["shell_parts"] += 1
            result["data"] = {
                "room_id": self.room_id, "display_name": self.text.display_text(self.room["display_name"], 80),
                "revision": self.revision, "source_kind": self.room["source"]["kind"], "bounds_m": self.room_bounds,
                "style": dict(self.style), "counts": counts,
            }
        elif op == "entities.list":
            # In sight now, plus what the companion remembers (marked seen: remembered); filters apply
            # to what it saw, never to the entity's true state.
            summaries = {e.id: self._summary(principal, e) for e in self.entities.values() if self._entity(e.id) is not None}
            for entity_id, entry in self._remembered(principal).items():
                summaries[entity_id] = self._remembered_summary(entry)
            items = [summaries[k] for k in sorted(summaries) if _matches(summaries[k], args.get("filter", {}))]
            offset = _cursor(args.get("cursor"))
            limit = args.get("limit", 50)
            result["data"] = {"items": items[offset:offset + limit]}
            if offset + limit < len(items):
                result["data"]["next_cursor"] = str(offset + limit)
        elif op == "entity.inspect":
            entity = self._entity(args["target"])
            entry = self._remembered(principal).get(args["target"]) if entity is None else None
            if entity is not None:
                data: dict = {"entity": self._summary(principal, entity)}
                parts, protected_by = entity.parts, entity.protected_by
            elif entry is not None:
                data = {"entity": self._remembered_summary(entry)}
                parts, protected_by = entry.view["parts"], entry.view["protected_by"]
            else:
                raise _not_found("$.args.target")
            if parts:
                data["parts"] = dict(parts)
            if protected_by:
                data["protected_by"] = protected_by
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
            radius = min(float(args.get("radius_m", self.policy.observe_max_radius_m)), self.policy.observe_max_radius_m)
            # A companion's own view was taken for this request (and remembered); the player may look
            # through a companion's eyes too, which never touches that companion's memory.
            seen = self._view if self._view is not None else self.perceived(self._principal_of(actor) or principal)
            origin = self.entities[actor].position
            visible = []
            for entity_id in seen:
                entity = self.entities[entity_id]
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
                        texts.append({"source": entity.id, "text": self.text.long_text(text, 500), "untrusted": True})
            result["data"] = {"actor": actor, "visible": [self._summary(principal, e) for _d, _i, e in visible],
                              "texts": texts}
            remembered = sorted((_distance_to_box(origin, entry.view["summary"]["bounds_m"]), entity_id, entry)
                                for entity_id, entry in self._remembered(principal).items())
            remembered = [(d, i, entry) for d, i, entry in remembered if d <= radius][:50]
            if remembered:
                result["data"]["remembered"] = [self._remembered_summary(entry) for _d, _i, entry in remembered]
        elif op == "jobs.status":
            job = self.jobs.get(args["job_id"])
            if job is None or job["principal"] != principal:
                # Unknown and other principals' jobs look identical.
                raise HostError("target_not_found", "No job with that id is running.", field_path="$.args.job_id")
            result["data"] = {"job_id": args["job_id"], "state": job["state"]}
            if "result" in job:
                result["data"]["result"] = copy.deepcopy(job["result"])
        elif op == "receipt.lookup":
            key = (principal, args["action_id"])
            receipt = self.receipts.get(key) or self.transient.get(key)
            if receipt is not None:
                result["data"] = {"found": True, "receipt": copy.deepcopy(receipt.result)}
            elif key in self.compacted:
                result["data"] = {"found": True, "compacted": True}
            else:
                result["data"] = {"found": False}
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
    """Path to the first key that names identity or authority, or is not a plain lowercase token."""
    found = find_forbidden_key(value, path, token_keys=True)
    return found[0] if found is not None else None


def _inside(point, bounds, tolerance: float = 1e-3) -> bool:
    return all(bounds["min_m"][i] - tolerance <= point[i] <= bounds["max_m"][i] + tolerance for i in range(3))


def _clamp(value: float) -> float:
    return _r(max(-1000.0, min(1000.0, value)))


def _box_gap(a, b) -> float:
    """The distance between two boxes (0 when they touch or overlap)."""
    total = 0.0
    for i in range(3):
        gap = max(a["min_m"][i] - b["max_m"][i], b["min_m"][i] - a["max_m"][i], 0.0)
        total += gap * gap
    return math.sqrt(total)


def _distance_to_box(point, box) -> float:
    total = 0.0
    for i in range(3):
        low, high = box["min_m"][i], box["max_m"][i]
        nearest = min(max(point[i], low), high)
        total += (point[i] - nearest) ** 2
    return math.sqrt(total)


def _matches(summary: dict, flt: dict) -> bool:
    """An entities.list filter against a summary: what is in sight now, or what was seen."""
    if "kind" in flt and summary["kind"] != flt["kind"]:
        return False
    if "category_group" in flt and summary.get("category_group") != flt["category_group"]:
        return False
    if "affordance" in flt and flt["affordance"] not in summary["affordances"]:
        return False
    if "provenance_kind" in flt and summary["provenance_kind"] != flt["provenance_kind"]:
        return False
    if "near" in flt and _distance_to_box(flt["near"]["center_m"], summary["bounds_m"]) > flt["near"]["radius_m"]:
        return False
    return True


def _cursor(cursor: str | None) -> int:
    if cursor is None:
        return 0
    if not cursor.isascii() or not cursor.isdigit() or len(cursor) > 6:
        raise HostError("invalid_args", "That cursor did not come from this game.", field_path="$.args.cursor")
    return int(cursor)
