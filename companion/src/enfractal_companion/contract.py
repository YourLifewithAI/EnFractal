"""The game command contract, loaded from `contracts/` and turned into a companion tool catalogue.

The contract files are the single source of truth. This module loads the integrator's own
validator (`contracts/validate.py`) and its schema registry instead of re-implementing them, so
the adapter, the mock host and the tests accept and refuse what the contract accepts and refuses.
On top of that it applies the rules the contract means but Python does not enforce yet:

- `pattern` follows ECMA-262 like the C# and GDScript readers: `$` matches only at the very end,
  so "approved" plus a final newline no longer passes `^[^...]*$` (Python's `$` also matches before
  a final newline);
- no string anywhere in a message hides characters (see textsafety.is_hidden; newline and tab
  are left to the per-field patterns);
- integers fit a signed 64-bit integer, and every number is finite.

The tool catalogue is derived, not hand-written: every command and query op in the contract
becomes a tool, except the ops listed in `$defs/player_only_ops`, which are never exposed.
"""
from __future__ import annotations

import functools
import importlib.util
import json
import math
import re
import unicodedata
from dataclasses import dataclass
from pathlib import Path
from types import ModuleType
from typing import Any

from jsonschema import ValidationError, validators

from . import canonical, textsafety

PACKAGE_DIR = Path(__file__).resolve().parent
# companion/src/enfractal_companion/contract.py -> repository root is three levels above the package.
DEFAULT_CONTRACTS_DIR = PACKAGE_DIR.parents[2] / "contracts"
DEFAULT_REPO_ROOT = PACKAGE_DIR.parents[2]

COMMAND_SCHEMA = "enfractal.command"
QUERY_SCHEMA = "enfractal.query"
RESULT_SCHEMA = "enfractal.result"
CONTRACT_VERSION = 1
INT64_MIN, INT64_MAX = -(2 ** 63), 2 ** 63 - 1

# Tool names are portable across MCP clients and every major model vendor's function-calling
# rules: lowercase ASCII, digits and underscores, at most 64 characters. Contract ops use dots,
# which several vendors reject in function names, so `goal.set` is published as `goal_set`.
TOOL_NAME_PATTERN = re.compile(r"[a-z][a-z0-9_]{0,63}")

# Envelope fields of a command that a tool call may set. Everything else in the envelope
# (schema, version, room_id, op) is filled by the adapter; the principal never exists in a request.
COMMAND_ENVELOPE_FIELDS = ("action_id", "expected_revision", "expected_entities", "preview", "note")
STOP_OPS = frozenset({"goal.stop", "effect.stop"})

# Keys that name identity or authority are never accepted anywhere in a request, by the adapter
# or the mock host: only the trusted side assigns them. A key is refused when its folded form
# (NFKC, case-folded, letters and digits only) contains one of these stems, so 'Principal',
# 'principal ', a full-width 'principal' or 'approved_by_player' are all caught.
AUTHORITY_STEMS = ("principal", "approv", "owner", "grant", "behalf", "credential", "password", "passwd",
                   "secret", "token", "apikey", "authoriz", "permission", "impersonat")
AUTHORITY_WORDS = frozenset({"role", "roles", "auth", "sudo", "admin"})
# Inside `args`, every key is a plain lowercase token, as every contract field and every creation
# part, node and parameter name is. Anything else (look-alike letters, spaces, capitals) is refused.
ARGS_KEY_PATTERN = re.compile(r"[a-z][a-z0-9_]{0,63}")


def _fold(key: str) -> str:
    return "".join(ch for ch in unicodedata.normalize("NFKC", key).casefold() if ch.isalnum())


def is_authority_key(key: str) -> bool:
    folded = _fold(key)
    return folded in AUTHORITY_WORDS or any(stem in folded for stem in AUTHORITY_STEMS)


def find_forbidden_key(value: Any, path: list, *, token_keys: bool) -> tuple[list, str] | None:
    """(path, reason) of the first key that names authority, or that is not a plain token when
    `token_keys` is set. Reasons: 'authority' or 'key_format'."""
    if isinstance(value, dict):
        for key, item in value.items():
            if not isinstance(key, str):
                return path + ["?"], "key_format"
            if is_authority_key(key):
                return path + [key], "authority"
            if token_keys and not ARGS_KEY_PATTERN.fullmatch(key):
                return path + [key], "key_format"
            found = find_forbidden_key(item, path + [key], token_keys=token_keys)
            if found is not None:
                return found
    elif isinstance(value, list):
        for index, item in enumerate(value):
            found = find_forbidden_key(item, path + [index], token_keys=token_keys)
            if found is not None:
                return found
    return None


def value_problems(value: Any, path: list | None = None) -> list[tuple[list, str]]:
    """(path, problem) for values strict JSON and the contract rule out but a Python object can hold:
    non-finite numbers, integers outside int64, strings with hidden characters, non-string keys."""
    path = path or []
    found: list[tuple[list, str]] = []
    if isinstance(value, bool) or value is None:
        return found
    if isinstance(value, int):
        if not INT64_MIN <= value <= INT64_MAX:
            found.append((path, "integer_range"))
    elif isinstance(value, float):
        if not math.isfinite(value):
            found.append((path, "non_finite"))
    elif isinstance(value, str):
        if textsafety.hidden_characters(value, allow_newlines=True):
            found.append((path, "hidden_text"))
    elif isinstance(value, dict):
        for key, item in value.items():
            if not isinstance(key, str):
                found.append((path + ["?"], "key_type"))
                continue
            if textsafety.hidden_characters(key):
                found.append((path + ["?"], "hidden_text"))
            found.extend(value_problems(item, path + [key]))
    elif isinstance(value, list):
        for index, item in enumerate(value):
            found.extend(value_problems(item, path + [index]))
    else:
        found.append((path, "type"))
    return found


def ecma_to_python(pattern: str) -> str:
    """Translate an ECMA-262 pattern (no flags) so Python's re matches the same strings.

    Without the m flag, ECMA-262's '$' matches only at the very end of the input; Python's '$' also
    matches just before a final newline. Every '$' that is neither escaped nor inside a character
    class therefore becomes '\\Z', wherever it appears (end of pattern, inside a group, in a
    lookahead). The contract's patterns use no other construct whose meaning differs.
    """
    out: list[str] = []
    in_class = False
    i = 0
    while i < len(pattern):
        ch = pattern[i]
        if ch == "\\" and i + 1 < len(pattern):
            out.append(pattern[i:i + 2])
            i += 2
            continue
        if in_class:
            if ch == "]":
                in_class = False
        elif ch == "[":
            in_class = True
            # A ']' right after '[' or '[^' is a literal member of the class, not its end.
            prefix = "[^" if pattern.startswith("[^", i) else "["
            out.append(prefix)
            i += len(prefix)
            if i < len(pattern) and pattern[i] == "]":
                out.append("]")
                i += 1
            continue
        elif ch == "$":
            out.append(r"\Z")
            i += 1
            continue
        out.append(ch)
        i += 1
    return "".join(out)


@functools.lru_cache(maxsize=512)
def _ecma_regex(pattern: str) -> re.Pattern:
    return re.compile(ecma_to_python(pattern))


def _ecma_pattern(validator, pattern, instance, schema):
    if validator.is_type(instance, "string") and not _ecma_regex(pattern).search(instance):
        yield ValidationError(f"{instance!r} does not match {pattern!r}")


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
        # The integrator's strict validator (integers without a fraction), with ECMA-262 patterns.
        ecma = validators.extend(self.validate.StrictValidator, {"pattern": _ecma_pattern})
        message_schema = self.validate.load_strict(self.dir / self.validate.SCHEMA_FILES[COMMAND_SCHEMA])
        self._validator = ecma(message_schema, registry=self.validate._registry())
        # Warm everything now so nothing opens files once the server is locked down.
        for name in (COMMAND_SCHEMA, QUERY_SCHEMA, RESULT_SCHEMA):
            self.validate.validator_for(name)
        list(self._validator.iter_errors({"schema": RESULT_SCHEMA}))

    # ----- validation -----

    def iter_errors(self, document: dict):
        """Raw jsonschema errors for a command, query or result, with ECMA-262 pattern semantics."""
        return self._validator.iter_errors(document)

    def schema_errors(self, document: Any) -> list[str]:
        """Every reason `document` is not a valid contract message: the schema (ECMA patterns),
        the value rules (hidden characters, int64, finite numbers) and the size limits."""
        if not isinstance(document, dict) or document.get("schema") not in (COMMAND_SCHEMA, QUERY_SCHEMA, RESULT_SCHEMA):
            return ["$: document must be an enfractal command, query or result"]
        problems = [f"{textsafety.field_path(e.absolute_path)}: {e.message[:200]}" for e in self.iter_errors(document)]
        problems += [f"{textsafety.field_path(path)}: {kind}" for path, kind in value_problems(document)]
        if not problems:
            problems += self.size_problems(document)
        return problems

    def size_problems(self, document: dict) -> list[str]:
        """The contract's size limits, measured in canonical JSON v1 (see canonical.py)."""
        try:
            size = len(self.canonical_bytes(document))
        except canonical.CanonicalJsonError as error:
            return [f"$: {error}"]
        limit = self.message_limits.get(document.get("schema"))
        problems = []
        if limit and size > limit:
            problems.append(f"$: message is {size} bytes of canonical JSON; the limit is {limit}")
        for path, source in creation_sources(document):
            if len(self.canonical_bytes(source)) > self.creation_source_limit:
                problems.append(f"{path}: a creation source exceeds {self.creation_source_limit} bytes of canonical JSON")
        return problems

    def canonical_bytes(self, document: Any) -> bytes:
        """Canonical JSON v1, the bytes every fingerprint and size limit is measured on."""
        return canonical.canonical_bytes(document)

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

    def tool_specs(self, profile: str = "full") -> list[ToolSpec]:
        specs = []
        for op, kind in self.companion_ops():
            name = op.replace(".", "_")
            if not TOOL_NAME_PATTERN.fullmatch(name):
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
                if op in STOP_OPS:
                    properties["action_id"] = dict(properties["action_id"], description=(
                        "Optional for a stop: the adapter mints one. A stop always applies, even if this "
                        "action_id was used before."))
                else:
                    required = ["action_id"] + required
            schema: dict = {"type": "object", "additionalProperties": False, "properties": properties}
            if required:
                schema["required"] = required
            if profile == "minimal":
                schema = minimal_schema(schema)
            elif profile != "full":
                raise ContractLoadError(f"unknown schema profile {profile!r}")
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
    "room.describe": "Describe the current room: name, revision, bounds, style and counts of what the companion can see.",
    "entities.list": "List the entities the companion's avatar can see (paged). Filter by kind, category group, affordance, provenance or distance.",
    "entity.inspect": "Inspect one entity the companion's avatar can see, by id: summary, parts and who protected it.",
    "capabilities.list": "List the effect capabilities the game supports and their parameter bounds.",
    "observe": "What the companion's own avatar can perceive within a radius: visible entities and words seen in the world.",
    "jobs.status": "Status of a long-running job by job_id.",
    "receipt.lookup": "Ask whether one of your earlier actions committed, by its action_id. Use this before retrying.",
    "approval.status": "Poll a held command by the request_id the game returned. The player approves or denies it in the game; you cannot.",
    "entity.grab": "Pick up a movable entity with the companion's avatar.",
    "entity.release": "Put down what the companion holds, optionally at a placement.",
    "entity.place": "Move an entity to a placement.",
    "entity.set_part": "Set a part of an entity (a door, a lid) to a value from 0 to 1.",
    "entity.remove": "Remove an entity from the room. Destructive: name expected_entities or expected_revision. The game may hold it for the player's approval.",
    "entity.transform": "Turn an entity into a creation while keeping its identity. Destructive: name expected_entities or expected_revision. The game may hold it for the player's approval.",
    "creation.place": "Place a new creation from an enfractal.creation manifest.",
    "creation.revise": "Change a creation's manifest or placement. Destructive: name expected_entities or expected_revision. The game may hold it for the player's approval.",
    "creation.activate": "Trigger a creation's interact behaviour.",
    "protect.lock": "Protect entities so they resist changes. Only the player can unlock them. Destructive: name expected_entities or expected_revision.",
    "goal.set": "Give the companion's own avatar a goal: follow, stay, come, look_at, point_at, go_to, fetch or wander. look_at, point_at and go_to need a target or position_m; fetch needs a target.",
    "goal.stop": "Stop the companion's goals and effects. Always permitted.",
    "effect.start": "Start a bounded effect from a supported capability in an area for a duration.",
    "effect.stop": "Stop one of your effects, or 'all' of them. Always permitted.",
    "style.set": "Switch the room to another style preset version. The game may hold it for the player's approval.",
    "room.checkpoint": "Record a checkpoint of the room.",
    "room.undo": "Undo your own recent changes, back to an earlier revision. Requires expected_revision. Refused if it would undo the player's changes or change anything protected. The game may hold it for the player's approval.",
}


def _describe(op: str, kind: str, destructive: frozenset[str]) -> str:
    base = _OP_DESCRIPTIONS.get(op, f"The contract operation {op}.")
    if kind == "query":
        return f"{base} Read-only. Maps to the game query '{op}'."
    return f"{base} Maps to the game command '{op}'."


def dumps_compact(document: Any) -> str:
    """One-line JSON with every non-ASCII character escaped: world text cannot add lines or hide characters."""
    return json.dumps(document, ensure_ascii=True, sort_keys=True, separators=(",", ":"), allow_nan=False)


def creation_sources(document: Any) -> list[tuple[str, dict]]:
    """(path, source) for the creation manifests a command carries."""
    if not isinstance(document, dict) or document.get("schema") != COMMAND_SCHEMA:
        return []
    args = document.get("args") if isinstance(document.get("args"), dict) else {}
    into = args.get("into") if isinstance(args.get("into"), dict) else {}
    found = [("$.args.source", args.get("source")), ("$.args.into.source", into.get("source"))]
    return [(path, source) for path, source in found if isinstance(source, dict)]


# Keywords every function-calling validator we know of accepts. Anything else is dropped from a
# minimal-profile tool schema; map-style objects (expected_entities, effect params) become plain
# objects described in words. The adapter still validates every call against the full contract.
_MINIMAL_KEYWORDS = frozenset({"type", "properties", "required", "description", "enum", "items", "minimum", "maximum",
                               "minItems", "maxItems", "minLength", "maxLength", "pattern"})


def minimal_schema(schema: Any) -> Any:
    if isinstance(schema, list):
        return [minimal_schema(item) for item in schema]
    if not isinstance(schema, dict):
        return schema
    out: dict = {}
    for key, value in schema.items():
        if key == "properties":
            out[key] = {name: minimal_schema(sub) for name, sub in value.items()}
        elif key == "const":
            out["enum"] = [value]
        elif key == "anyOf":
            types = {branch.get("type") for branch in value if isinstance(branch, dict)}
            if len(types) == 1 and None not in types:
                out["type"] = types.pop()
        elif key in _MINIMAL_KEYWORDS:
            out[key] = minimal_schema(value)
    if out.get("type") == "object" and "properties" not in out and isinstance(schema.get("additionalProperties"), dict):
        out["description"] = (out.get("description", "") + " An object whose keys and values follow the rules above; "
                              "the game checks them.").strip()
    return out
