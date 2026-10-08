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
import base64
import binascii
import hashlib
import json
import math
import re
import bisect
import functools
import struct
import sys
from pathlib import Path, PurePosixPath

from jsonschema import Draft202012Validator, ValidationError, validators
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
    except ContractError:
        raise
    except ValueError:  # an integer literal longer than Python converts (4,300 digits by default)
        raise ContractError("a number is too long to read") from None
    except RecursionError:
        raise ContractError("the document nests too deeply to read") from None


# 'integer' means a JSON number written without a fraction, as the C# and GDScript readers require.
_STRICT_TYPES = Draft202012Validator.TYPE_CHECKER.redefine(
    "integer", lambda _checker, instance: isinstance(instance, int) and not isinstance(instance, bool))


def ecma_pattern(pattern: str) -> str:
    """Translate an ECMA-262 pattern (JSON Schema's dialect, no flags) for Python's re.

    ECMA-262's '$' matches only at the very end of the input; Python's (and .NET's) '$' also
    matches just before a final newline, so "approved\\n" passed '^[^...]*$'. Every '$' that is
    neither escaped nor inside a character class becomes '\\Z'. The contract's patterns use no
    other construct whose meaning differs."""
    out, in_class, i = [], False, 0
    while i < len(pattern):
        ch = pattern[i]
        if ch == "\\" and i + 1 < len(pattern):
            out.append(pattern[i:i + 2])
            i += 2
            continue
        if in_class:
            in_class = ch != "]"
        elif ch == "[":
            in_class = True
            prefix = "[^" if pattern.startswith("[^", i) else "["
            out.append(prefix)
            i += len(prefix)
            if i < len(pattern) and pattern[i] == "]":  # a ']' first in a class is a member
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
def _compiled(pattern: str):
    return re.compile(ecma_pattern(pattern))


def _ecma_pattern_keyword(validator, pattern, instance, schema):
    if validator.is_type(instance, "string") and not _compiled(pattern).search(instance):
        yield ValidationError(f"{instance!r} does not match {pattern!r}")


StrictValidator = validators.extend(Draft202012Validator, {"pattern": _ecma_pattern_keyword}, type_checker=_STRICT_TYPES)


# Extended_Pictographic (UTS #51, Unicode 15.1 emoji-data.txt), merged into ranges. It reserves whole
# blocks for future emoji, so new emoji need no table update.
_EXTENDED_PICTOGRAPHIC = (
    (0x00A9, 0x00A9), (0x00AE, 0x00AE), (0x203C, 0x203C), (0x2049, 0x2049), (0x2122, 0x2122), (0x2139, 0x2139),
    (0x2194, 0x2199), (0x21A9, 0x21AA), (0x231A, 0x231B), (0x2328, 0x2328), (0x2388, 0x2388), (0x23CF, 0x23CF),
    (0x23E9, 0x23F3), (0x23F8, 0x23FA), (0x24C2, 0x24C2), (0x25AA, 0x25AB), (0x25B6, 0x25B6), (0x25C0, 0x25C0),
    (0x25FB, 0x25FE), (0x2600, 0x2605), (0x2607, 0x2612), (0x2614, 0x2685), (0x2690, 0x2705), (0x2708, 0x2712),
    (0x2714, 0x2714), (0x2716, 0x2716), (0x271D, 0x271D), (0x2721, 0x2721), (0x2728, 0x2728), (0x2733, 0x2734),
    (0x2744, 0x2744), (0x2747, 0x2747), (0x274C, 0x274C), (0x274E, 0x274E), (0x2753, 0x2755), (0x2757, 0x2757),
    (0x2763, 0x2767), (0x2795, 0x2797), (0x27A1, 0x27A1), (0x27B0, 0x27B0), (0x27BF, 0x27BF), (0x2934, 0x2935),
    (0x2B05, 0x2B07), (0x2B1B, 0x2B1C), (0x2B50, 0x2B50), (0x2B55, 0x2B55), (0x3030, 0x3030), (0x303D, 0x303D),
    (0x3297, 0x3297), (0x3299, 0x3299), (0x1F000, 0x1F0FF), (0x1F10D, 0x1F10F), (0x1F12F, 0x1F12F), (0x1F16C, 0x1F171),
    (0x1F17E, 0x1F17F), (0x1F18E, 0x1F18E), (0x1F191, 0x1F19A), (0x1F1AD, 0x1F1E5), (0x1F201, 0x1F20F), (0x1F21A, 0x1F21A),
    (0x1F22F, 0x1F22F), (0x1F232, 0x1F23A), (0x1F23C, 0x1F23F), (0x1F249, 0x1F3FA), (0x1F400, 0x1F53D), (0x1F546, 0x1F64F),
    (0x1F680, 0x1F6FF), (0x1F774, 0x1F77F), (0x1F7D5, 0x1F7FF), (0x1F80C, 0x1F80F), (0x1F848, 0x1F84F), (0x1F85A, 0x1F85F),
    (0x1F888, 0x1F88F), (0x1F8AE, 0x1F8FF), (0x1F90C, 0x1F93A), (0x1F93C, 0x1F945), (0x1F947, 0x1FAFF), (0x1FC00, 0x1FFFD),
)
_PICTOGRAPHIC_STARTS = tuple(low for low, _high in _EXTENDED_PICTOGRAPHIC)
_KEYCAP_BASES = frozenset(map(ord, "0123456789#*"))
_VS15, _VS16, _ZWJ, _KEYCAP = 0xFE0E, 0xFE0F, 0x200D, 0x20E3


def _pictographic(code: int) -> bool:
    index = bisect.bisect_right(_PICTOGRAPHIC_STARTS, code) - 1
    return index >= 0 and code <= _EXTENDED_PICTOGRAPHIC[index][1]


def _misplaced_marker(text: str) -> str | None:
    """The first emoji marker out of place. The text patterns let VS15, VS16, the zero-width joiner and
    the keycap combiner through because emoji use them; they are allowed only where an emoji puts them:
    a selector right after an Extended_Pictographic character or a keycap base (0-9, # or *), so at most
    one per base; the joiner between two emoji (the one before may carry one VS16 or skin tone); the
    keycap after a keycap base, optionally with VS16. Alone or in runs they could carry hidden data."""
    for i, ch in enumerate(text):
        code = ord(ch)
        if code not in (_VS15, _VS16, _ZWJ, _KEYCAP):
            continue
        before = ord(text[i - 1]) if i else -1
        if code in (_VS15, _VS16):
            in_place = _pictographic(before) or before in _KEYCAP_BASES
        elif code == _KEYCAP:
            in_place = before in _KEYCAP_BASES or (before == _VS16 and i >= 2 and ord(text[i - 2]) in _KEYCAP_BASES)
        else:
            j = i - 1
            if j >= 0 and (ord(text[j]) == _VS16 or 0x1F3FB <= ord(text[j]) <= 0x1F3FF):
                j -= 1
            in_place = (i + 1 < len(text) and _pictographic(ord(text[i + 1])) and j >= 0
                        and _pictographic(ord(text[j])))
        if not in_place:
            return f"U+{code:04X}"
    return None


def _hidden_code_point(text: str) -> str | None:
    """Plane 14 (U+E0000-U+EFFFF: the TAG block, which can spell a hidden message, and the variation
    selector supplement), unpaired surrogates and misplaced emoji markers. The text patterns refuse
    plane 14 only in UTF-16 readers, through \\uDB40-\\uDB7F, and cannot see a marker's context;
    Python matches code points, so it checks here, in every string."""
    for ch in text:
        code = ord(ch)
        if 0xE0000 <= code <= 0xEFFFF or 0xD800 <= code <= 0xDFFF:
            return f"U+{code:04X}"
    return _misplaced_marker(text)


def _text_problems(value, path: str = "$") -> list[str]:
    if isinstance(value, str):
        found = _hidden_code_point(value)
        return [f"{path}: contains the invisible, unpaired or misplaced character {found}"] if found else []
    problems = []
    if isinstance(value, dict):
        for key, item in value.items():
            found = _hidden_code_point(key) if isinstance(key, str) else None
            if found:
                problems.append(f"{path}: a field name contains the invisible, unpaired or misplaced character {found}")
            problems += _text_problems(item, f"{path}.{key}")
    elif isinstance(value, list):
        for index, item in enumerate(value):
            problems += _text_problems(item, f"{path}[{index}]")
    return problems


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
    hidden = _text_problems(document)
    if hidden:
        return messages + hidden  # an unpaired surrogate cannot be measured in UTF-8 either
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
    manifest_objects, bounds, shell_ids = None, None, None
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
            shell_ids = {p["id"] for p in room["shell"]["parts"]}
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
    if "journal" in state:
        problems += _check_journal(state["journal"], store, label)
    if "discovered" in state:
        problems += _check_discovered(state["discovered"], store, label, manifest_objects, shell_ids, bounds)
    if check_style_pins:
        problems += check_style_pin(state["style_pin"], label, styles_dir)
    return problems


def _check_journal(journal: dict, store: int, label: str) -> list[str]:
    """Saved journal entries: one id each, none newer than the store, each list oldest first by revision (the room
    revision is monotonic; a wall clock may step back)."""
    problems, seen = [], set()
    for part in ("open_tasks", "history", "notes"):
        previous = -1
        for entry in journal[part]:
            if entry["entry_id"] in seen:
                problems.append(f"{label}: journal entry {entry['entry_id']} appears twice")
            seen.add(entry["entry_id"])
            if entry["revision"] > store:
                problems.append(f"{label}: journal entry {entry['entry_id']} is newer than the store revision")
            if entry["revision"] < previous:
                problems.append(f"{label}: journal {part} must be oldest first; {entry['entry_id']} is out of order")
            previous = max(previous, entry["revision"])
    return problems


def _check_discovered(discovered: dict, store: int, label: str, manifest_objects: set | None,
                      shell_ids: set | None, bounds: dict | None) -> list[str]:
    problems = []
    for key, item in discovered["entities"].items():
        entity = item["entity"]
        if entity["id"] != key:
            problems.append(f"{label}: discovered {key} does not match its summary's id {entity['id']}")
        if STATE_KINDS.get(key.split(":", 1)[0]) != entity["kind"]:
            problems.append(f"{label}: discovered {key} cannot be of kind {entity['kind']}")
        if item["last_seen_revision"] > store:
            problems.append(f"{label}: discovered {key} was seen after the store revision")
        if manifest_objects is not None and key.startswith("obj:") and key not in manifest_objects:
            problems.append(f"{label}: discovered {key} is not an object in the pinned room")
    cell = discovered["cell_m"]
    levels = set()
    for index, level in enumerate(discovered["levels"]):
        where = f"{label}: discovered level {index} ({level['support']})"
        identity = (level["support"], level.get("surface"))
        if identity in levels:
            problems.append(f"{where} is listed twice")
        levels.add(identity)
        support = level["support"]
        if support.startswith("shell:") and shell_ids is not None and support not in shell_ids:
            problems.append(f"{where} rests on a shell part the pinned room does not have")
        if support.startswith("obj:") and manifest_objects is not None and support not in manifest_objects:
            problems.append(f"{where} rests on an object the pinned room does not have")
        try:
            cells = base64.b64decode(level["cells"], validate=True)
        except (binascii.Error, ValueError):
            problems.append(f"{where}: cells is not standard base64")
            continue
        count = level["columns"] * level["rows"]
        if len(cells) != (count + 7) // 8:
            problems.append(f"{where}: cells holds {len(cells)} bytes; {level['columns']} x {level['rows']} cells need {(count + 7) // 8}")
        elif count % 8 and cells[-1] >> (count % 8):
            problems.append(f"{where}: the unused bits after the last cell must be 0")
        if bounds is not None:
            low = [level["min_xz_m"][0], level["min_xz_m"][1]]
            high = [low[0] + level["columns"] * cell, low[1] + level["rows"] * cell]
            for axis, i, j in (("x", 0, 0), ("z", 2, 1)):
                if low[j] < bounds["min_m"][i] - cell or high[j] > bounds["max_m"][i] + cell:
                    problems.append(f"{where}: its grid reaches beyond the room bounds along {axis}")
    return problems


def check_message(document: dict, label: str) -> list[str]:
    """Rules a schema cannot express for results: the journal newest first with one id per entry, and map.find
    answers nearest first."""
    if document.get("schema") != "enfractal.result" or document.get("ok") is not True:
        return []
    problems = []
    data = document.get("data") or {}
    if document["op"] == "journal.read":
        seen = set()
        for part in ("open_tasks", "entries"):
            previous = None
            for entry in data[part]:
                if entry["entry_id"] in seen:
                    problems.append(f"{label}: journal entry {entry['entry_id']} appears twice")
                seen.add(entry["entry_id"])
                if previous is not None and entry["revision"] > previous:
                    problems.append(f"{label}: journal {part} must be newest first; {entry['entry_id']} is out of order")
                previous = entry["revision"]
    elif document["op"] == "map.find":
        distances = [item["distance_m"] for item in data["items"]]
        if distances != sorted(distances):
            problems.append(f"{label}: map.find items must be nearest first")
        ids = [item["entity"]["id"] for item in data["items"]]
        if len(ids) != len(set(ids)):
            problems.append(f"{label}: map.find lists an entity twice")
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
    return check_message(document, label)


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
