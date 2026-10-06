#!/usr/bin/env python3
"""Validate EnFractal contract documents.

Usage:
  python contracts/validate.py FILE_OR_ROOM_DIR [...]
  python contracts/validate.py --room game/rooms/test_room
  python contracts/validate.py --state path/to/state.json --room path/to/room_dir
  python contracts/validate.py --pin FILE            # print the SHA-256 pin of a file

A directory containing room.json is validated as a room: the manifest, every asset.json it
references, every listed file hash, and the semantic rules JSON Schema cannot express.
Exit status is 0 only when everything passes. Requires the packages in contracts/requirements.txt.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path, PurePosixPath

from jsonschema import Draft202012Validator
from jsonschema.exceptions import best_match
from referencing import Registry, Resource
from referencing.jsonschema import DRAFT202012

CONTRACTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = CONTRACTS_DIR.parent
SCHEMA_FILES = {
    "enfractal.room": "room-manifest.schema.json",
    "enfractal.asset": "asset.schema.json",
    "enfractal.style": "style-preset.schema.json",
    "enfractal.command": "game-command.schema.json",
    "enfractal.query": "game-command.schema.json",
    "enfractal.result": "game-command.schema.json",
    "enfractal.room_state": "room-state.schema.json",
}
QUAT_TOLERANCE = 1e-3
PLANE_TOLERANCE_M = 1e-3
BOUNDS_TOLERANCE_M = 1e-3


class ContractError(Exception):
    """A document cannot be loaded or violates a contract rule."""


def sha256_file(path: Path) -> str:
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


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


def _registry() -> Registry:
    resources = []
    for path in sorted(CONTRACTS_DIR.glob("*.schema.json")):
        contents = load_strict(path)
        Draft202012Validator.check_schema(contents)
        resources.append((contents["$id"], Resource.from_contents(contents, default_specification=DRAFT202012)))
    return Registry().with_resources(resources)


_REGISTRY: Registry | None = None
_VALIDATORS: dict[str, Draft202012Validator] = {}


def validator_for(schema_name: str) -> Draft202012Validator:
    global _REGISTRY
    if schema_name not in SCHEMA_FILES:
        raise ContractError(f"unknown schema {schema_name!r}; expected one of {sorted(SCHEMA_FILES)}")
    file_name = SCHEMA_FILES[schema_name]
    if file_name not in _VALIDATORS:
        if _REGISTRY is None:
            _REGISTRY = _registry()
        schema = load_strict(CONTRACTS_DIR / file_name)
        _VALIDATORS[file_name] = Draft202012Validator(schema, registry=_REGISTRY)
    return _VALIDATORS[file_name]


def _json_path(error) -> str:
    path = "$"
    for part in error.absolute_path:
        path += f"[{part}]" if isinstance(part, int) else f".{part}"
    return path


def schema_errors(document) -> list[str]:
    if not isinstance(document, dict) or not isinstance(document.get("schema"), str):
        return ["$: document must be an object with a string 'schema' field"]
    validator = validator_for(document["schema"])
    messages = []
    for error in sorted(validator.iter_errors(document), key=lambda e: list(map(str, e.absolute_path))):
        detail = error.message
        if error.validator == "not":
            detail = f"must not match {json.dumps(error.validator_value, sort_keys=True)}"
        if error.context:
            deepest = best_match(error.context)
            detail = f"{detail} (closest: {_json_path(deepest)}: {deepest.message})"
        messages.append(f"{_json_path(error)}: {detail}")
    return messages


# ---------- semantic checks ----------

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
    return [c / length for c in n] if length > 1e-12 else None


def _inside(point, bounds, tolerance=BOUNDS_TOLERANCE_M) -> bool:
    return all(bounds["min_m"][i] - tolerance <= point[i] <= bounds["max_m"][i] + tolerance for i in range(3))


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
    for path, entry in sorted(listed.items()):
        target = (base / PurePosixPath(path)).resolve()
        if base.resolve() not in target.parents and target != base.resolve():
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
    return problems


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
    else:
        slots = [m["slot"] for m in asset["materials"]]
        if slots != ["body"]:
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


def check_room(room_dir: Path, check_style_pins: bool = False) -> list[str]:
    """Validate room.json, its assets and files. Returns a list of problems (empty when valid)."""
    room_dir = Path(room_dir)
    label = f"{room_dir.name}/room.json"
    try:
        room = load_strict(room_dir / "room.json")
    except (OSError, ContractError) as error:
        return [f"{label}: {error}"]
    problems = [f"{label} {message}" for message in schema_errors(room)]
    if problems:
        return problems
    if room["room_id"] != room_dir.name:
        problems.append(f"{label}: room_id {room['room_id']!r} must match its directory name {room_dir.name!r}")
    bounds = room["bounds"]
    if any(bounds["min_m"][i] >= bounds["max_m"][i] for i in range(3)):
        problems.append(f"{label}: bounds min must be below max on every axis")
        return problems
    centre = [(bounds["min_m"][i] + bounds["max_m"][i]) / 2 for i in range(3)]

    ids = []
    referenced = set()
    shell_roles = {}
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
        origin = points[0]
        if any(abs(_dot(_sub(p, origin), normal)) > PLANE_TOLERANCE_M for p in points):
            problems.append(f"{label}: {part['id']} polygon is not planar")
        polygon_centre = [sum(p[i] for p in points) / len(points) for i in range(3)]
        if part["role"] in ("floor", "wall", "ceiling", "platform", "stair"):
            if _dot(normal, _sub(centre, polygon_centre)) <= 0:
                problems.append(f"{label}: {part['id']} winding faces away from the room interior")
        elif part["role"] == "ground" and normal[1] < 0.5:
            problems.append(f"{label}: {part['id']} ground polygon must face upward")

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
            asset_problems = [f"{asset_label} {message}" for message in schema_errors(asset)]
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
        problems += check_style_pin(room["default_style"], label)
    return problems


def check_style_pin(pin: dict, label: str, styles_dir: Path = REPO_ROOT / "game" / "styles") -> list[str]:
    path = styles_dir / f"{pin['preset_id']}.json"
    if not path.is_file():
        return [f"{label}: style preset {pin['preset_id']} not found in {styles_dir}"]
    preset = load_strict(path)
    problems = []
    if preset.get("preset_version") != pin["preset_version"]:
        problems.append(f"{label}: style pin wants version {pin['preset_version']}, preset file is {preset.get('preset_version')}")
    if sha256_file(path) != pin["preset_sha256"]:
        problems.append(f"{label}: style pin hash does not match {path.name}")
    return problems


def check_style(preset: dict, label: str) -> list[str]:
    hours = [key["hour"] for key in preset["time_of_day"]["keys"]]
    problems = []
    if hours != sorted(hours) or len(hours) != len(set(hours)):
        problems.append(f"{label}: time_of_day keys must be in strictly increasing hour order")
    if preset["time_of_day"]["enabled"] and not hours:
        problems.append(f"{label}: time_of_day is enabled but has no keys")
    return problems


def check_state(state: dict, label: str, room_dir: Path | None = None) -> list[str]:
    problems = []
    for key, entity in state["entities"].items():
        if entity["id"] != key:
            problems.append(f"{label}: entity key {key} does not match its id {entity['id']}")
        if "transform" in entity and not _quat_ok(entity["transform"]["rotation"]):
            problems.append(f"{label}: {key} rotation is not a unit quaternion")
        if entity["revision"] > max(state["store_revision"], 1):
            problems.append(f"{label}: {key} revision is newer than the store revision")
    for key, receipt in state["receipts"].items():
        principal, action_id = key.split("|", 1)
        result = receipt["result"]
        if result["principal"] != principal or result.get("action_id") != action_id:
            problems.append(f"{label}: receipt {key} does not match its result's principal and action_id")
        if result["revision"] > state["store_revision"]:
            problems.append(f"{label}: receipt {key} is newer than the store revision")
        if result["room_id"] != state["room_id"]:
            problems.append(f"{label}: receipt {key} belongs to another room")
    if room_dir is not None:
        manifest = Path(room_dir) / "room.json"
        if not manifest.is_file():
            problems.append(f"{label}: room {room_dir} has no room.json")
        elif sha256_file(manifest) != state["room_pin"]["manifest_sha256"]:
            problems.append(f"{label}: room_pin does not match {manifest}")
        elif load_strict(manifest)["room_id"] != state["room_id"]:
            problems.append(f"{label}: room_id does not match the pinned room")
    return problems


def check_document(path: Path) -> list[str]:
    """Schema plus per-kind semantic checks for a single file (rooms: use check_room)."""
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
        return check_room(Path(path).parent)
    if kind == "enfractal.asset":
        return check_asset(document, Path(path).parent, label)
    if kind == "enfractal.style":
        return check_style(document, label)
    if kind == "enfractal.room_state":
        return check_state(document, label)
    return []


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("paths", nargs="*", type=Path)
    parser.add_argument("--room", type=Path, help="room directory (with --state: the room the state must pin)")
    parser.add_argument("--state", type=Path, help="room state file to validate against --room")
    parser.add_argument("--check-style-pins", action="store_true", help="also verify default_style pins against game/styles")
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
            errors = [f"{args.state} {m}" for m in schema_errors(state)]
            problems += errors or check_state(state, str(args.state), args.room)
        except (OSError, ContractError) as error:
            problems.append(f"{args.state}: {error}")
    elif args.room:
        checked += 1
        problems += check_room(args.room, args.check_style_pins)
    for path in args.paths:
        checked += 1
        if path.is_dir():
            problems += check_room(path, args.check_style_pins)
        else:
            problems += check_document(path)
    if not checked:
        parser.print_usage()
        return 2
    for problem in problems:
        print(f"FAIL {problem}")
    print(f"{'OK' if not problems else 'FAILED'}: {checked} item(s) checked, {len(problems)} problem(s)")
    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
