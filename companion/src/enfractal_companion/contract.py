"""The game command contract, loaded from `contracts/` and turned into a companion tool catalogue.

The contract files are the single source of truth. This module loads the integrator's own
validator (`contracts/validate.py`) instead of re-implementing it, so the adapter, the mock host
and the tests all accept and refuse exactly what the contract accepts and refuses.

The tool catalogue is derived, not hand-written: every command and query op in the contract
becomes a tool, except the ops listed in `$defs/player_only_ops`, which are never exposed.
"""
from __future__ import annotations

import importlib.util
import json
import re
from dataclasses import dataclass
from pathlib import Path
from types import ModuleType
from typing import Any

PACKAGE_DIR = Path(__file__).resolve().parent
# companion/src/enfractal_companion/contract.py -> repository root is three levels above the package.
DEFAULT_CONTRACTS_DIR = PACKAGE_DIR.parents[2] / "contracts"
DEFAULT_REPO_ROOT = PACKAGE_DIR.parents[2]

COMMAND_SCHEMA = "enfractal.command"
QUERY_SCHEMA = "enfractal.query"
RESULT_SCHEMA = "enfractal.result"
CONTRACT_VERSION = 1

# Tool names are portable across MCP clients and every major model vendor's function-calling
# rules: lowercase ASCII, digits and underscores, at most 64 characters. Contract ops use dots,
# which several vendors reject in function names, so `goal.set` is published as `goal_set`.
TOOL_NAME_PATTERN = re.compile(r"^[a-z][a-z0-9_]{0,63}$")

# Envelope fields of a command that a tool call may set. Everything else in the envelope
# (schema, version, room_id, op) is filled by the adapter; the principal never exists in a request.
COMMAND_ENVELOPE_FIELDS = ("action_id", "expected_revision", "expected_entities", "preview", "note")

# Keys that are never accepted anywhere in a request, at any depth, by the adapter or the mock
# host: they name identity or authority, which only the trusted side assigns.
AUTHORITY_KEYS = frozenset({
    "principal", "principal_id", "approval", "approval_id", "approved", "approved_by", "approve",
    "owner", "owner_id", "grant", "grants", "role", "roles", "token", "session_token", "auth",
    "authorization", "credential", "credentials", "password", "api_key", "apikey", "secret",
})


class ContractLoadError(RuntimeError):
    """The contracts directory is missing or does not contain what the adapter needs."""


def _load_validator(contracts_dir: Path) -> ModuleType:
    path = contracts_dir / "validate.py"
    if not path.is_file():
        raise ContractLoadError(f"contract validator not found at {path}")
    spec = importlib.util.spec_from_file_location("enfractal_contracts_validate", path)
    if spec is None or spec.loader is None:
        raise ContractLoadError(f"cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@dataclass(frozen=True)
class ToolSpec:
    """One MCP tool and the contract op it maps to."""

    name: str
    op: str
    kind: str  # "command" or "query"
    description: str
    input_schema: dict
    read_only: bool
    destructive: bool
    # Contract arg properties whose value the adapter fills with the companion's own avatar
    # when the caller leaves them out.
    actor_field: str | None
    args_properties: tuple[str, ...]


class Contracts:
    """Loaded contract schemas plus the integrator's validator module."""

    def __init__(self, contracts_dir: Path | None = None):
        self.dir = Path(contracts_dir or DEFAULT_CONTRACTS_DIR).resolve()
        self.validate = _load_validator(self.dir)
        self.command_schema = self.validate.load_strict(self.dir / "game-command.schema.json")
        self.common_schema = self.validate.load_strict(self.dir / "common.schema.json")
        defs = self.command_schema["$defs"]
        self.command_ops: tuple[str, ...] = tuple(defs["command_op"]["enum"])
        self.query_ops: tuple[str, ...] = tuple(defs["query_op"]["enum"])
        self.player_only_ops: frozenset[str] = frozenset(defs["player_only_ops"]["enum"])
        self.destructive_ops: frozenset[str] = frozenset(defs["destructive_ops"]["enum"])
        self.error_codes: frozenset[str] = frozenset(defs["error_code"]["enum"])
        self.message_limits: dict[str, int] = dict(self.validate.MESSAGE_LIMITS)
        self.creation_source_limit: int = int(self.validate.CREATION_SOURCE_LIMIT)
        self._args_def_for_op = self._map_args_defs()
        # Validators warm up lazily inside validate.py; do it now so nothing opens files later.
        for name in (COMMAND_SCHEMA, QUERY_SCHEMA, RESULT_SCHEMA):
            self.validate.validator_for(name)

    # ----- the integrator's validator -----

    def schema_errors(self, document: Any) -> list[str]:
        """Contract schema errors plus size limits, exactly as `contracts/validate.py` reports them."""
        return self.validate.schema_errors(document)

    def iter_errors(self, document: dict):
        """Raw jsonschema errors (for mapping onto contract error codes)."""
        return self.validate.validator_for(document["schema"]).iter_errors(document)

    def canonical_bytes(self, document: Any) -> bytes:
        return self.validate.canonical_bytes(document)

    def loads_strict(self, text: str):
        return self.validate.loads_strict(text)

    @property
    def ContractError(self):  # noqa: N802 - re-export of the validator's exception type
        return self.validate.ContractError

    # ----- schema helpers -----

    def _map_args_defs(self) -> dict[str, str]:
        """op -> name under `$defs/args`, read from the contract's if/then dispatch."""
        found: dict[str, str] = {}
        defs = self.command_schema["$defs"]
        for container in (defs["command"], defs["query"]):
            for clause in container.get("allOf", []):
                ref = clause.get("if", {}).get("$ref", "")
                then_args = clause.get("then", {}).get("properties", {}).get("args", {}).get("$ref", "")
                if ref.startswith("#/$defs/is_op/") and then_args.startswith("#/$defs/args/"):
                    found[ref.rsplit("/", 1)[1]] = then_args.rsplit("/", 1)[1]
        missing = [op for op in self.command_ops + self.query_ops if op not in found]
        if missing:
            raise ContractLoadError(f"contract has no args schema for {missing}")
        return found

    def args_schema(self, op: str) -> dict:
        """The contract's args schema for `op`, with every reference resolved inline."""
        name = self._args_def_for_op[op]
        return self.portable(self.command_schema["$defs"]["args"][name])

    def _resolve(self, ref: str, base: str) -> tuple[dict, str]:
        """Resolve a reference relative to the document it appears in ('command' or 'common')."""
        if ref.startswith("common.schema.json#/"):
            document_name, pointer = "common", ref[len("common.schema.json#/"):]
        elif ref.startswith("#/"):
            document_name, pointer = base, ref[2:]
        else:
            raise ContractLoadError(f"unsupported reference {ref}")
        node: Any = self.common_schema if document_name == "common" else self.command_schema
        for part in pointer.split("/"):
            node = node[part]
        return node, document_name

    def portable(self, schema: Any, base: str = "command") -> Any:
        """Inline references and drop keywords that older MCP clients and some model vendors reject.

        The published tool schemas are hints for the model. The adapter and the host validate
        every message against the full contract, so dropping `if`/`then`/`not` here never
        weakens enforcement; the conditions they express are restated in the tool description.
        """
        if isinstance(schema, list):
            return [self.portable(item, base) for item in schema]
        if not isinstance(schema, dict):
            return schema
        if "$ref" in schema:
            target, target_base = self._resolve(schema["$ref"], base)
            resolved = self.portable(target, target_base)
            extra = self.portable({k: v for k, v in schema.items() if k != "$ref"}, base)
            return {**resolved, **extra}
        out: dict = {}
        for key, value in schema.items():
            if key in ("if", "then", "else", "not", "$schema", "$id", "$comment"):
                continue
            if key in ("properties", "$defs", "patternProperties"):
                out[key] = {name: self.portable(sub, base) for name, sub in value.items()}
            else:
                out[key] = self.portable(value, base)
        if "allOf" in out:
            out = _merge_all_of(out)
        if "propertyNames" in out and isinstance(out["propertyNames"], dict):
            out["propertyNames"] = {k: v for k, v in out["propertyNames"].items() if k in ("pattern", "type", "maxLength")}
        return out

    # ----- the tool catalogue -----

    def companion_ops(self) -> list[tuple[str, str]]:
        """(op, kind) for every op the companion may call: all of them except player-only ops."""
        ops = [(op, "query") for op in self.query_ops] + [(op, "command") for op in self.command_ops]
        return [(op, kind) for op, kind in ops if op not in self.player_only_ops]

    def tool_specs(self) -> list[ToolSpec]:
        specs = []
        for op, kind in self.companion_ops():
            name = op.replace(".", "_")
            if not TOOL_NAME_PATTERN.match(name):
                raise ContractLoadError(f"op {op} does not map to a portable tool name")
            args = self.args_schema(op)
            properties = dict(args.get("properties", {}))
            required = [r for r in args.get("required", [])]
            actor_field = "actor" if "actor" in properties else None
            if actor_field and actor_field in required:
                # The adapter fills the companion's own avatar; it is the only one it may name.
                required.remove(actor_field)
            if actor_field:
                properties[actor_field] = dict(properties[actor_field])
                properties[actor_field]["description"] = (
                    "Optional. The companion acts and observes only through its own avatar; "
                    "the adapter fills it and refuses any other avatar."
                )
            if kind == "command":
                envelope = self._command_envelope_properties()
                for key in envelope:
                    if key in properties:
                        raise ContractLoadError(f"{op} arg {key} collides with a command envelope field")
                properties = {**envelope, **properties}
                required = ["action_id"] + required
            schema: dict = {"type": "object", "additionalProperties": False, "properties": properties}
            if required:
                schema["required"] = required
            specs.append(ToolSpec(
                name=name,
                op=op,
                kind=kind,
                description=_describe(op, kind, self.destructive_ops),
                input_schema=schema,
                read_only=kind == "query",
                destructive=op in self.destructive_ops or op in ("room.undo", "style.set"),
                actor_field=actor_field,
                args_properties=tuple(args.get("properties", {}).keys()),
            ))
        return specs

    def _command_envelope_properties(self) -> dict:
        props = self.command_schema["$defs"]["command"]["properties"]
        out = {}
        for key in COMMAND_ENVELOPE_FIELDS:
            out[key] = self.portable(props[key])
        out["action_id"]["description"] = (
            "Your idempotency key for this action (letters, digits, '-' or '_'). Reusing it with the same "
            "arguments returns the original receipt with replayed=true; reusing it with different arguments "
            "is refused with action_id_conflict. After an unclear outcome, call receipt_lookup before retrying."
        )
        out["expected_entities"]["description"] = (
            "Entity revisions you last saw, for example {\"obj:box\": 0}. Each must still match or the command "
            "is refused with revision_conflict. Destructive ops need this or expected_revision."
        )
        out["preview"]["description"] = "Validate and predict without committing; records no receipt."
        out["note"]["description"] = "Optional short reason shown to the player. Shown as text only."
        return out


def _merge_all_of(schema: dict) -> dict:
    """Collapse `allOf` of plain constraint objects (the display_text + maxLength pattern) into one."""
    parts = schema.pop("allOf")
    merged = dict(schema)
    for part in parts:
        if not isinstance(part, dict) or any(k in part for k in ("anyOf", "oneOf", "allOf", "properties")):
            merged.setdefault("allOf", []).append(part)
            continue
        for key, value in part.items():
            if key in ("maxLength", "maxItems", "maxProperties", "maximum") and key in merged:
                merged[key] = min(merged[key], value)
            elif key in ("minLength", "minItems", "minimum") and key in merged:
                merged[key] = max(merged[key], value)
            elif key == "pattern" and "pattern" in merged and merged["pattern"] != value:
                merged.setdefault("allOf", []).append({"pattern": value})
            elif key == "description" and "description" in merged:
                continue
            else:
                merged[key] = value
    return merged


_OP_DESCRIPTIONS = {
    "room.describe": "Describe the current room: name, revision, bounds, style and counts.",
    "entities.list": "List entities in the room (paged). Filter by kind, category group, affordance, provenance or distance.",
    "entity.inspect": "Inspect one entity by id: summary, parts and who protected it.",
    "capabilities.list": "List the effect capabilities the game supports and their parameter bounds.",
    "observe": "What the companion's own avatar can perceive within a radius: visible entities and words seen in the world.",
    "jobs.status": "Status of a long-running job by job_id.",
    "receipt.lookup": "Ask whether one of your earlier actions committed, by its action_id. Use this before retrying.",
    "approval.status": "Poll a held command by the request_id the game returned. The player approves or denies it in the game; you cannot.",
    "entity.grab": "Pick up a movable entity with the companion's avatar.",
    "entity.release": "Put down what the companion holds, optionally at a placement.",
    "entity.place": "Move an entity to a placement.",
    "entity.set_part": "Set a part of an entity (a door, a lid) to a value from 0 to 1.",
    "entity.remove": "Remove an entity from the room. Destructive: name expected_entities or expected_revision. Needs the player's approval.",
    "entity.transform": "Turn an entity into a creation while keeping its identity. Destructive: name expected_entities or expected_revision. Needs the player's approval.",
    "creation.place": "Place a new creation from an enfractal.creation manifest.",
    "creation.revise": "Change a creation's manifest or placement. Destructive: name expected_entities or expected_revision. Needs the player's approval.",
    "creation.activate": "Trigger a creation's interact behaviour.",
    "protect.lock": "Protect entities so they resist changes. Only the player can unlock them. Destructive: name expected_entities or expected_revision.",
    "goal.set": "Give the companion's own avatar a goal: follow, stay, come, look_at, point_at, go_to, fetch or wander. look_at, point_at and go_to need a target or position_m; fetch needs a target.",
    "goal.stop": "Stop the companion's goals and effects. Always permitted.",
    "effect.start": "Start a bounded effect from a supported capability in an area for a duration.",
    "effect.stop": "Stop one of your effects, or 'all' of them. Always permitted.",
    "style.set": "Switch the room to another style preset version. Needs the player's approval.",
    "room.checkpoint": "Record a checkpoint of the room.",
    "room.undo": "Undo the room to an earlier revision. Requires expected_revision. Needs the player's approval.",
}


def _describe(op: str, kind: str, destructive: frozenset[str]) -> str:
    base = _OP_DESCRIPTIONS.get(op, f"The contract operation {op}.")
    if kind == "query":
        return f"{base} Read-only. Maps to the game query '{op}'."
    return f"{base} Maps to the game command '{op}'."


def dumps_compact(document: Any) -> str:
    """One-line JSON with every non-ASCII character escaped: world text cannot add lines or hide characters."""
    return json.dumps(document, ensure_ascii=True, sort_keys=True, separators=(",", ":"), allow_nan=False)
