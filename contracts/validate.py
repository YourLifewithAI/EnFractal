#!/usr/bin/env python3
"""Validate EnFractal contract documents.

Usage:
  python contracts/validate.py FILE_OR_ROOM_DIR [...]
  python contracts/validate.py --room game/rooms/test_room
  python contracts/validate.py --state path/to/state.json --room path/to/room_dir
  python contracts/validate.py --check-style-pins game/rooms/<room>     # also verify style pins
  python contracts/validate.py --pin FILE                                # print a file's SHA-256 pin

A directory containing room.json is validated as a room: the manifest, every asset.json it
references, every listed file (hash, size, self-contained GLB), and the semantic rules JSON
Schema cannot express. A .json file is validated by its own 'schema' field. Exit status is 0
only when everything passes. Requires the packages in contracts/requirements.txt.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import struct
import sys
from pathlib import Path, PurePosixPath

from jsonschema import Draft202012Validator, validators
from jsonschema.exceptions import best_match
from referencing import Registry, Resource
from referencing.jsonschema import DRAFT202012

CONTRACTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = CONTRACTS_DIR.parent
STYLES_DIR = REPO_ROOT / "game" / "styles"
SCHEMA_FILES = {
    "enfractal.room": "room-manifest.schema.json",
    "enfractal.asset": "asset.schema.json",
    "enfractal.style": "style-preset.schema.json",
    "enfractal.command": "game-command.schema.json",
    "enfractal.query": "game-command.schema.json",
    "enfractal.result": "game-command.schema.json",
    "enfractal.room_state": "room-state.schema.json",
}
MESSAGE_LIMITS = {"enfractal.command": 65536, "enfractal.query": 65536, "enfractal.result": 262144}
CREATION_SOURCE_LIMIT = 32768
QUAT_TOLERANCE = 1e-3
PLANE_TOLERANCE_M = 1e-3
BOUNDS_TOLERANCE_M = 1e-3
WALL_PROBE_M = 0.05
STATE_KINDS = {"obj": "object", "creation": "creation", "avatar": "avatar"}


class ContractError(Exception):
    """A document cannot be loaded or violates a contract rule."""


def sha256_file(path: Path) -> str:
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def canonical_bytes(document) -> bytes:
    return json.dumps(document, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def load_strict(path: Path):
    """Parse JSON the way pinned files must be written: UTF-8, LF, no duplicate keys, finite numbers."""
    raw = Path(path).read_bytes()
    if raw.startswith(b"\xef\xbb\xbf"):
        raise ContractError("UTF-8 byte-order mark is not allowed")
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError as error:
        raise ContractError(f"not UTF-8: {error}") from None
    if "\r" in text:
        raise ContractError("CR line endings are not allowed; pins hash LF bytes")
    return loads_strict(text)


def loads_strict(text: str):
    def unique_pairs(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ContractError(f"duplicate key {key!r}")
            result[key] = value
        return result

    def reject_constant(name):
        raise ContractError(f"non-finite number {name} is not allowed")

    def finite_float(text_value):
        value = float(text_value)
        if not math.isfinite(value):
            raise ContractError(f"number {text_value} overflows to a non-finite value")
        return value

    try:
        return json.loads(text, object_pairs_hook=unique_pairs, parse_constant=reject_constant, parse_float=finite_float)
    except json.JSONDecodeError as error:
        raise ContractError(f"invalid JSON: {error}") from None


# 'integer' means a JSON number written without a fraction, as the C# and GDScript readers require.
_STRICT_TYPES = Draft202012Validator.TYPE_CHECKER.redefine(
    "integer", lambda _checker, instance: isinstance(instance, int) and not isinstance(instance, bool))
StrictValidator = validators.extend(Draft202012Validator, type_checker=_STRICT_TYPES)


def _registry() -> Registry:
    resources = []
    for path in sorted(CONTRACTS_DIR.glob("*.schema.json")):
        contents = load_strict(path)
        Draft202012Validator.check_schema(contents)
        resources.append((contents["$id"], Resource.from_contents(contents, default_specification=DRAFT202012)))
    return Registry().with_resources(resources)


_REGISTRY: Registry | None = None
_VALIDATORS: dict[str, Draft202012Validator] = {}


def validator_for(schema_name: str):
    global _REGISTRY
    if schema_name not in SCHEMA_FILES:
        raise ContractError(f"unknown schema {schema_name!r}; expected one of {sorted(SCHEMA_FILES)}")
    file_name = SCHEMA_FILES[schema_name]
    if file_name not in _VALIDATORS:
        if _REGISTRY is None:
            _REGISTRY = _registry()
        _VALIDATORS[file_name] = StrictValidator(load_strict(CONTRACTS_DIR / file_name), registry=_REGISTRY)
    return _VALIDATORS[file_name]


def _json_path(error) -> str:
    path = "$"
    for part in error.absolute_path:
        path += f"[{part}]" if isinstance(part, int) else f".{part}"
    return path


def schema_errors(document, expected: str | None = None) -> list[str]:
    if not isinstance(document, dict) or not isinstance(document.get("schema"), str):
        return ["$: document must be an object with a string 'schema' field"]
    if expected is not None and document["schema"] != expected:
        return [f"$.schema: expected {expected!r}, found {document['schema']!r}"]
    if document["schema"] not in SCHEMA_FILES:
        return [f"$.schema: unknown schema {document['schema']!r}"]
    messages = []
    for error in sorted(validator_for(document["schema"]).iter_errors(document), key=lambda e: list(map(str, e.absolute_path))):
        detail = error.message
        if error.validator == "not":
            detail = f"must not match {json.dumps(error.validator_value, sort_keys=True)}"
        if error.context:
            deepest = best_match(error.context)
            detail = f"{detail} (closest: {_json_path(deepest)}: {deepest.message})"
        messages.append(f"{_json_path(error)}: {detail}")
    limit = MESSAGE_LIMITS.get(document["schema"])
    if limit and len(canonical_bytes(document)) > limit:
        messages.append(f"$: message is {len(canonical_bytes(document))} bytes of canonical JSON; the limit is {limit}")
    for source in _creation_sources(document):
        if len(canonical_bytes(source)) > CREATION_SOURCE_LIMIT:
            messages.append(f"$: a creation source exceeds {CREATION_SOURCE_LIMIT} bytes of canonical JSON")
    return messages


def _creation_sources(document):
    if document.get("schema") != "enfractal.command":
        return []
    args = document.get("args") if isinstance(document.get("args"), dict) else {}
    found = [args.get("source"), (args.get("into") or {}).get("source") if isinstance(args.get("into"), dict) else None]
    return [s for s in found if isinstance(s, dict)]


# ---------- binary checks ----------

def check_glb(data: bytes, label: str) -> list[str]:
    """A pinned GLB must be self-contained: every buffer is the embedded BIN chunk or a data: URI."""
    if len(data) < 20 or data[:4] != b"glTF":
        return [f"{label}: not a binary glTF file"]
    _, version, length = struct.unpack_from("<4sII", data, 0)
    if version != 2 or length != len(data):
        return [f"{label}: GLB header says version {version} and {length} bytes; file is {len(data)} bytes"]
    chunk_length, chunk_type = struct.unpack_from("<I4s", data, 12)
    if chunk_type != b"JSON" or 20 + chunk_length > len(data):
        return [f"{label}: GLB does not start with a JSON chunk"]
    try:
        gltf = loads_strict(data[20:20 + chunk_length].decode("utf-8").rstrip(" \x00"))
    except (UnicodeDecodeError, ContractError) as error:
        return [f"{label}: GLB JSON chunk is invalid: {error}"]
    if not isinstance(gltf, dict):
        return [f"{label}: GLB JSON chunk is not an object"]
    problems = []
    for kind in ("buffers", "images"):
        items = gltf.get(kind, [])
        if not isinstance(items, list):
            problems.append(f"{label}: GLB {kind!r} entry is not an array")
            continue
        for index, item in enumerate(items):
            uri = item.get("uri") if isinstance(item, dict) else None
            if uri is not None and not (isinstance(uri, str) and uri.startswith("data:")):
                problems.append(f"{label}: {kind}[{index}] references external file {uri!r}; meshes must be self-contained")
    return problems


# ---------- geometry helpers ----------

def _quat_ok(q) -> bool:
    return abs(math.sqrt(sum(c * c for c in q)) - 1.0) <= QUAT_TOLERANCE


def _sub(a, b):
    return [a[i] - b[i] for i in range(3)]


def _dot(a, b):
    return sum(a[i] * b[i] for i in range(3))


def _newell_normal(points):
    n = [0.0, 0.0, 0.0]
    for i, p in enumerate(points):
        q = points[(i + 1) % len(points)]
        n[0] += (p[1] - q[1]) * (p[2] + q[2])
        n[1] += (p[2] - q[2]) * (p[0] + q[0])
        n[2] += (p[0] - q[0]) * (p[1] + q[1])
    length = math.sqrt(_dot(n, n))
    return [c / length for c in n] if length > 1e-12 and math.isfinite(length) else None


def _inside(point, bounds, tolerance=BOUNDS_TOLERANCE_M) -> bool:
    return all(bounds["min_m"][i] - tolerance <= point[i] <= bounds["max_m"][i] + tolerance for i in range(3))


def _inside_xz_polygon(x: float, z: float, polygon) -> bool:
    inside = False
    for i, a in enumerate(polygon):
        b = polygon[(i + 1) % len(polygon)]
        if (a[2] > z) != (b[2] > z):
            crossing = a[0] + (z - a[2]) * (b[0] - a[0]) / (b[2] - a[2])
            if x < crossing:
                inside = not inside
    return inside


# ---------- files ----------

def _check_listed_files(base: Path, files: list, referenced: set[str], label: str) -> list[str]:
    problems = []
    listed = {}
    for entry in files:
        if entry["path"] in listed:
            problems.append(f"{label}: file {entry['path']} is listed twice")
        listed[entry["path"]] = entry
    for path in sorted(referenced):
        if path not in listed:
            problems.append(f"{label}: referenced file {path} is not in the files list")
    root = base.resolve()
    for path, entry in sorted(listed.items()):
        target = (base / PurePosixPath(path)).resolve()
        if root not in target.parents:
            problems.append(f"{label}: file {path} escapes its directory")
            continue
        if not target.is_file():
            problems.append(f"{label}: listed file {path} does not exist")
            continue
        data = target.read_bytes()
        if len(data) != entry["bytes"]:
            problems.append(f"{label}: {path} is {len(data)} bytes, files list says {entry['bytes']}")
        if hashlib.sha256(data).hexdigest() != entry["sha256"]:
            problems.append(f"{label}: {path} hash does not match the files list")
        if path.endswith(".glb"):
            problems += check_glb(data, f"{label}: {path}")
    return problems


# ---------- assets and rooms ----------

def check_asset(asset: dict, asset_dir: Path, label: str) -> list[str]:
    problems = []
    referenced = set()
    geometry = asset["geometry"]
    if geometry["kind"] == "mesh":
        referenced.add(geometry["mesh"])
        for lod in geometry.get("lods", []):
            referenced.add(lod["mesh"])
            if lod["triangle_count"] >= geometry["triangle_count"]:
                problems.append(f"{label}: LOD {lod['mesh']} is not lighter than the base mesh")
    elif [m["slot"] for m in asset["materials"]] != ["body"]:
        problems.append(f"{label}: a primitive has exactly one material slot named 'body'")
    if "file" in asset["collision"]:
        referenced.add(asset["collision"]["file"])
    for variant in asset.get("styles", {}).values():
        if "mesh" in variant:
            referenced.add(variant["mesh"])
        referenced.update(variant.get("textures", []))
    slots = [m["slot"] for m in asset["materials"]]
    if len(slots) != len(set(slots)):
        problems.append(f"{label}: material slot names must be unique")
    part_ids = [p["id"] for p in asset.get("parts", [])]
    if len(part_ids) != len(set(part_ids)):
        problems.append(f"{label}: part ids must be unique")
    if "openable" in asset["affordances"] and not part_ids:
        problems.append(f"{label}: an openable asset needs at least one part")
    problems += _check_listed_files(asset_dir, asset["files"], referenced, label)
    return problems


def check_room(room_dir: Path, check_style_pins: bool = False, manifest_name: str = "room.json", styles_dir: Path = STYLES_DIR) -> list[str]:
    """Validate a room manifest, its assets and files. Returns a list of problems (empty when valid)."""
    room_dir = Path(room_dir)
    label = f"{room_dir.name}/{manifest_name}"
    try:
        room = load_strict(room_dir / manifest_name)
    except (OSError, ContractError) as error:
        return [f"{label}: {error}"]
    problems = [f"{label} {message}" for message in schema_errors(room, "enfractal.room")]
    if problems:
        return problems
    if room["room_id"] != room_dir.name:
        problems.append(f"{label}: room_id {room['room_id']!r} must match its directory name {room_dir.name!r}")
    bounds = room["bounds"]
    if any(bounds["min_m"][i] >= bounds["max_m"][i] for i in range(3)):
        return problems + [f"{label}: bounds min must be below max on every axis"]

    ids, referenced, shell_roles = [], set(), {}
    footprints, walls = [], []
    for part in room["shell"]["parts"]:
        ids.append(part["id"])
        shell_roles[part["id"]] = part["role"]
        geometry = part["geometry"]
        if "texture" in part:
            referenced.add(part["texture"])
        if geometry["kind"] == "mesh":
            referenced.add(geometry["mesh"])
            continue
        points = geometry["points_m"]
        normal = _newell_normal(points)
        if normal is None:
            problems.append(f"{label}: {part['id']} polygon is degenerate")
            continue
        if any(abs(_dot(_sub(p, points[0]), normal)) > PLANE_TOLERANCE_M for p in points):
            problems.append(f"{label}: {part['id']} polygon is not planar")
        role = part["role"]
        if role in ("floor", "ground", "platform", "stair") and normal[1] <= 0.5:
            problems.append(f"{label}: {part['id']} must face upward (its winding points its normal down or sideways)")
        elif role == "ceiling" and normal[1] >= -0.5:
            problems.append(f"{label}: {part['id']} must face downward into the room")
        if role in ("floor", "ground"):
            footprints.append(points)
        if role == "wall":
            walls.append((part["id"], points, normal, geometry["thickness_m"]))
    # A wall faces the room when a point just beyond its own thickness, along its normal, is inside the
    # room: within the bounds and over a floor polygon. This holds for L-shaped rooms and dividers.
    for part_id, points, normal, thickness in walls:
        centre = [sum(p[i] for p in points) / len(points) for i in range(3)]
        probe = [centre[i] + normal[i] * (thickness + WALL_PROBE_M) for i in range(3)]
        inside_bounds = all(bounds["min_m"][i] - BOUNDS_TOLERANCE_M <= probe[i] <= bounds["max_m"][i] + BOUNDS_TOLERANCE_M for i in (0, 2))
        over_floor = not footprints or any(_inside_xz_polygon(probe[0], probe[2], f) for f in footprints)
        if not (inside_bounds and over_floor):
            problems.append(f"{label}: {part_id} winding faces away from the room interior")

    opening_ids = []
    for opening in room["shell"].get("openings", []):
        opening_ids.append(opening["id"])
        if opening["host_part_id"] not in shell_roles:
            problems.append(f"{label}: opening {opening['id']} hosts on unknown part {opening['host_part_id']}")
        elif shell_roles[opening["host_part_id"]] not in ("wall", "ceiling", "floor", "ground"):
            problems.append(f"{label}: opening {opening['id']} must sit in a wall, ceiling, floor or ground part")
        if not _inside(opening["center_m"], bounds, 0.5):
            problems.append(f"{label}: opening {opening['id']} centre is outside the room")
    if len(opening_ids) != len(set(opening_ids)):
        problems.append(f"{label}: opening ids must be unique")

    asset_cache: dict[str, dict] = {}
    for instance in room["objects"]:
        ids.append(instance["id"])
        transform = instance["transform"]
        if not _quat_ok(transform["rotation"]):
            problems.append(f"{label}: {instance['id']} rotation is not a unit quaternion")
        if not _inside(transform["position_m"], bounds):
            problems.append(f"{label}: {instance['id']} pivot is outside the room bounds")
        referenced.add(instance["asset"])
        if instance["asset"] not in asset_cache:
            asset_path = room_dir / PurePosixPath(instance["asset"])
            asset_label = f"{room_dir.name}/{instance['asset']}"
            try:
                asset = load_strict(asset_path)
            except (OSError, ContractError) as error:
                problems.append(f"{asset_label}: {error}")
                asset_cache[instance["asset"]] = {}
                continue
            asset_problems = [f"{asset_label} {message}" for message in schema_errors(asset, "enfractal.asset")]
            if not asset_problems:
                asset_problems = check_asset(asset, asset_path.parent, asset_label)
                if asset["asset_id"] != asset_path.parent.name:
                    asset_problems.append(f"{asset_label}: asset_id must match its directory name")
            problems += asset_problems
            asset_cache[instance["asset"]] = asset if not asset_problems else {}
        asset = asset_cache[instance["asset"]]
        for part_id in instance.get("initial_parts", {}):
            if asset and part_id not in {p["id"] for p in asset.get("parts", [])}:
                problems.append(f"{label}: {instance['id']} sets unknown part {part_id}")

    if len(ids) != len(set(ids)):
        problems.append(f"{label}: shell part and object ids must be unique")
    known = set(ids)
    for instance in room["objects"]:
        target = instance["support"].get("target_id")
        if target is not None and target not in known:
            problems.append(f"{label}: {instance['id']} rests on unknown entity {target}")
        if target == instance["id"]:
            problems.append(f"{label}: {instance['id']} cannot rest on itself")

    spawn_ids = []
    for spawn in room["spawns"]:
        spawn_ids.append(spawn["id"])
        if not _inside(spawn["position_m"], bounds):
            problems.append(f"{label}: spawn {spawn['id']} is outside the room bounds")
    if len(spawn_ids) != len(set(spawn_ids)):
        problems.append(f"{label}: spawn ids must be unique")
    hint_ids = [hint["id"] for hint in room.get("light_hints", [])]
    if len(hint_ids) != len(set(hint_ids)):
        problems.append(f"{label}: light hint ids must be unique")

    reference = room.get("reference", {})
    if "splat" in reference:
        referenced.add(reference["splat"])
    for view in reference.get("views", []):
        referenced.add(view["image"])
        if not _quat_ok(view["camera"]["transform"]["rotation"]):
            problems.append(f"{label}: reference view {view['id']} rotation is not a unit quaternion")

    problems += _check_listed_files(room_dir, room["files"], referenced, label)
    if check_style_pins and "default_style" in room:
        problems += check_style_pin(room["default_style"], label, styles_dir)
    return problems


# ---------- styles ----------

def preset_path(preset_id: str, version: int, styles_dir: Path = STYLES_DIR) -> Path:
    return styles_dir / preset_id / f"v{version}.json"


def check_style_pin(pin: dict, label: str, styles_dir: Path = STYLES_DIR) -> list[str]:
    path = preset_path(pin["preset_id"], pin["preset_version"], styles_dir)
    if not path.is_file():
        return [f"{label}: style {pin['preset_id']} v{pin['preset_version']} not found at {path}"]
    problems = []
    preset = load_strict(path)
    if preset.get("preset_id") != pin["preset_id"] or preset.get("preset_version") != pin["preset_version"]:
        problems.append(f"{label}: {path} does not declare {pin['preset_id']} v{pin['preset_version']}")
    if sha256_file(path) != pin["preset_sha256"]:
        problems.append(f"{label}: style pin hash does not match {path.name}; a pinned preset version must never change")
    return problems


def check_style(preset: dict, label: str, path: Path | None = None) -> list[str]:
    hours = [key["hour"] for key in preset["time_of_day"]["keys"]]
    problems = []
    if hours != sorted(hours) or len(hours) != len(set(hours)):
        problems.append(f"{label}: time_of_day keys must be in strictly increasing hour order")
    if preset["time_of_day"]["enabled"] and not hours:
        problems.append(f"{label}: time_of_day is enabled but has no keys")
    if path is not None and path.parent.parent.name == "styles":
        if path.parent.name != preset["preset_id"] or path.name != f"v{preset['preset_version']}.json":
            problems.append(f"{label}: a preset lives at styles/<preset_id>/v<preset_version>.json")
    return problems


# ---------- room state ----------

def check_state(state: dict, label: str, room_dir: Path | None = None, check_style_pins: bool = False, styles_dir: Path = STYLES_DIR) -> list[str]:
    problems = []
    store = state["store_revision"]
    manifest_objects, bounds = None, None
    if room_dir is not None:
        manifest = Path(room_dir) / "room.json"
        if not manifest.is_file():
            problems.append(f"{label}: room {room_dir} has no room.json")
        elif sha256_file(manifest) != state["room_pin"]["manifest_sha256"]:
            problems.append(f"{label}: room_pin does not match {manifest}")
        else:
            room = load_strict(manifest)
            if room["room_id"] != state["room_id"]:
                problems.append(f"{label}: room_id does not match the pinned room")
            manifest_objects = {o["id"] for o in room["objects"]}
            bounds = room["bounds"]
    for key, entity in state["entities"].items():
        namespace = key.split(":", 1)[0]
        if entity["id"] != key:
            problems.append(f"{label}: entity key {key} does not match its id {entity['id']}")
        if STATE_KINDS.get(namespace) != entity["kind"]:
            problems.append(f"{label}: {key} cannot be saved as kind {entity['kind']}; saved entities are obj: objects, creation: creations and avatar: avatars")
        if entity["revision"] > max(store, 1):
            problems.append(f"{label}: {key} revision is newer than the store revision")
        if entity["provenance"]["created_revision"] > store:
            problems.append(f"{label}: {key} was created after the store revision")
        locked = entity["protection"].get("locked_revision")
        if locked is not None and locked > store:
            problems.append(f"{label}: {key} lock is newer than the store revision")
        if "transform" in entity:
            if not _quat_ok(entity["transform"]["rotation"]):
                problems.append(f"{label}: {key} rotation is not a unit quaternion")
            if bounds is not None and not entity.get("removed") and not _inside(entity["transform"]["position_m"], bounds):
                problems.append(f"{label}: {key} is outside the room bounds")
        if manifest_objects is not None and namespace == "obj" and key not in manifest_objects:
            problems.append(f"{label}: {key} is not an object in the pinned room")
    for key, receipt in state["receipts"].items():
        principal, action_id = key.split("|", 1)
        result = receipt["result"]
        if result["principal"] != principal or result.get("action_id") != action_id:
            problems.append(f"{label}: receipt {key} does not match its result's principal and action_id")
        if result["revision"] > store:
            problems.append(f"{label}: receipt {key} is newer than the store revision")
        if result["room_id"] != state["room_id"]:
            problems.append(f"{label}: receipt {key} belongs to another room")
    checkpoint_ids = [c["id"] for c in state.get("checkpoints", [])]
    if len(checkpoint_ids) != len(set(checkpoint_ids)):
        problems.append(f"{label}: checkpoint ids must be unique")
    for checkpoint in state.get("checkpoints", []):
        if checkpoint["revision"] > store:
            problems.append(f"{label}: checkpoint {checkpoint['id']} is newer than the store revision")
    if check_style_pins:
        problems += check_style_pin(state["style_pin"], label, styles_dir)
    return problems


def check_document(path: Path, check_style_pins: bool = False) -> list[str]:
    """Schema plus per-kind semantic checks for one file, validated as that file."""
    path = Path(path)
    label = str(path)
    try:
        document = load_strict(path)
    except (OSError, ContractError) as error:
        return [f"{label}: {error}"]
    problems = [f"{label} {message}" for message in schema_errors(document)]
    if problems:
        return problems
    kind = document["schema"]
    if kind == "enfractal.room":
        return check_room(path.parent, check_style_pins, manifest_name=path.name)
    if kind == "enfractal.asset":
        return check_asset(document, path.parent, label)
    if kind == "enfractal.style":
        return check_style(document, label, path)
    if kind == "enfractal.room_state":
        return check_state(document, label, check_style_pins=check_style_pins)
    return []


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("paths", nargs="*", type=Path)
    parser.add_argument("--room", type=Path, help="room directory (with --state: the room the state must pin)")
    parser.add_argument("--state", type=Path, help="room state file to validate against --room")
    parser.add_argument("--check-style-pins", action="store_true", help="also verify style pins against game/styles/<id>/v<N>.json")
    parser.add_argument("--pin", type=Path, help="print the SHA-256 pin of a file and exit")
    args = parser.parse_args(argv)
    if args.pin:
        print(sha256_file(args.pin))
        return 0
    problems: list[str] = []
    checked = 0
    if args.state:
        checked += 1
        try:
            state = load_strict(args.state)
            errors = [f"{args.state} {m}" for m in schema_errors(state, "enfractal.room_state")]
            problems += errors or check_state(state, str(args.state), args.room, args.check_style_pins)
        except (OSError, ContractError) as error:
            problems.append(f"{args.state}: {error}")
    elif args.room:
        checked += 1
        problems += check_room(args.room, args.check_style_pins)
    for path in args.paths:
        checked += 1
        problems += check_room(path, args.check_style_pins) if path.is_dir() else check_document(path, args.check_style_pins)
    if not checked:
        parser.print_usage()
        return 2
    for problem in problems:
        print(f"FAIL {problem}")
    print(f"{'OK' if not problems else 'FAILED'}: {checked} item(s) checked, {len(problems)} problem(s)")
    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
