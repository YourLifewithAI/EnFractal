#!/usr/bin/env python3
"""Write the typed schema for a preset's x_look_* extension blocks into the style preset contract, from the preset itself.

The look keeps every number it uses in the preset's `extensions` as `x_look_*` blocks (the integrator promotes or removes
extension keys; the Look review's M6 asked for this before the preset becomes a candidate). This tool reads a preset and
writes the JSON Schema that describes those blocks exactly as the preset states them: every block and every field
required, no unknown field allowed, colours `#rrggbb`, and the ranges and allowed values the C# reader (LookTuning.Parse)
enforces wherever a schema can say them. In contracts/style-preset.schema.json, `extensions` is the shared extensions
definition plus these typed `properties`, and the two `$defs` named below sit beside the contract's own.

  python tools/look/make_look_schema.py [PRESET]      print the typed pieces (default preset: storybook_painterly v1)
  python tools/look/make_look_schema.py --write       write them into contracts/style-preset.schema.json
  python tools/look/make_look_schema.py --check       fail unless the contract matches what --write would write

Standard library only.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_PRESET = ROOT / "game" / "styles" / "storybook_painterly" / "v1.json"
CONTRACT = ROOT / "contracts" / "style-preset.schema.json"
DEFS = ("look_role_marks_entry", "look_season_look")
TYPED = ("The Look lane's x_look_* blocks are typed (review M6): every field of every block is required and checked, and an "
         "unknown field in one of them is refused. Other x_ keys stay free, as in every extensions object.")
COLOUR = {"$ref": "common.schema.json#/$defs/color_hex"}

# Ranges the C# reader (LookTuning.Parse) enforces, as (minimum, maximum) by "block.field"; a missing side is open.
RANGES = {
    "x_look_role_marks.calm": (0, 1),
    "x_look_gi.environment_ambient_scale": (0, 1),
    "x_look_gi.two_bounces_above": (0, 1),
    "x_look_lamps.switch_on_below_daylight": (0, 1),
    "x_look_lamps.sky_fill_saturation": (0, 1),
    "x_look_lamps.lamp_tint_amount": (0, 1),
    "x_look_lamps.window_spot_angle_deg": (0, 90),
    "x_look_sun.reference_daylight": (0, 1),
    "x_look_sun.moon_bearing_deg": (0, 360),
    "x_look_seasons.hold": (0, 0.5),
    "x_look_grade.night_with_lamps": (0, 1),
    "x_look_grade.night_full_below": (0, 1),
    "x_look_grade.night_none_above": (0, 1),
    "x_look_grade.season_tint_shadow_fade": (0, 1),
    "x_look_grade.lut_size": (8, 65),
    "x_look_dof.tilt_transition_shortening": (0, 1),
    "x_look_dof.tilt_band_narrowing": (0, 1),
    "x_look_dof.observe_amount": (0, 1),
    "x_look_post.defocus_darken": (0, 1),
    "x_look_paint.mark_fade_start": (0, 0.5),
    "x_look_paint.mark_fade_end": (0, 0.5),
    "x_look_sky.ground_season_tint": (0, 1),
    "x_look_seasons.looks.cloud_amount": (0, 1),
    "x_look_seasons.fixed_day_of_year": (1, 366),
    "x_look_seasons.centre_days[]": (1, 365),
    "x_look_shadows.key_split_1": (0, 1),
    "x_look_sun.horizon_elevation_deg": (0, 10),
    "x_look_sun.moon_elevation_deg": (0, 85),
    "x_look_sun.sunrise_elevation_deg": (None, 0),
    "x_look_sun.twilight_elevation_deg": (None, 0),
    "x_look_role_marks.stroke_stretch": (1, None),
}
# Bounds the reader excludes, as (exclusive minimum, exclusive maximum) by "block.field"; None leaves that side to RANGES.
# Relations between two fields (twilight below sunrise, fade start below fade end) stay with the reader alone.
EXCLUSIVE = {
    "x_look_shadows.key_split_1": (0, 1),
    "x_look_lamps.range_per_diagonal": (0, None),
    "x_look_lamps.window_spot_angle_deg": (0, 90),
    "x_look_sun.moon_bearing_deg": (None, 360),
    "x_look_sun.moon_elevation_deg": (5, None),
    "x_look_sun.reference_daylight": (0, 1),
    "x_look_seasons.hold": (None, 0.5),
    "x_look_dof.tilt_transition_shortening": (None, 1),
    "x_look_dof.tilt_band_narrowing": (None, 1),
    "x_look_dof.observe_band_m": (0, None),
    "x_look_dof.observe_far_transition_m": (0, None),
    "x_look_dof.observe_near_transition_m": (0, None),
    "x_look_post.defocus_darken": (None, 1),
    "x_look_paint.mark_fade_start": (0, None),
    "x_look_role_marks.stroke_scale_m": (0, None),
    "x_look_role_marks.pattern_scale_m": (0, None),
    "x_look_role_marks.texture_scale_m": (0, None),
}
# Numbers that may be negative: every other number must not be.
INTEGERS = {"x_look_shadows.key_splits", "x_look_gi.subdiv", "x_look_grade.lut_size", "x_look_seasons.centre_days", "x_look_seasons.fixed_day_of_year"}
SIGNED = {"x_look_sun.sunrise_elevation_deg", "x_look_sun.twilight_elevation_deg", "x_look_grade.warmth_rgb", "x_look_grade.night_tint_rgb"}
ENUMS = {
    "x_look_shadows.key_splits": [1, 2, 4],
    "x_look_gi.subdiv": [64, 128, 256, 512],
    "x_look_glow.blend_mode": ["additive", "screen", "softlight", "replace", "mix"],
    "x_look_role_marks.pattern": ["none", "wood", "fabric", "plaster", "paper", "cardboard", "brushed", "speckle", "smooth", "stone"],
    "x_look_role_marks.stroke_axis": ["x", "y", "z"],
}


def leaf(path: str, value):
    if isinstance(value, bool):
        return {"type": "boolean"}
    if isinstance(value, (int, float)):
        if path in ENUMS:
            return {"enum": ENUMS[path]}
        # A number is a number whatever the file happens to write (1 or 1.0); only counts and sizes are integers.
        schema = {"type": "integer" if path in INTEGERS or path.rstrip("[]") in INTEGERS else "number"}
    elif isinstance(value, str):
        if path in ENUMS:
            return {"enum": ENUMS[path]}
        if value.startswith("#") and len(value) == 7:
            return dict(COLOUR)
        return {"type": "string", "maxLength": 128}
    elif isinstance(value, list):
        item = leaf(path + "[]", value[0]) if value else {}
        if path in SIGNED or any(isinstance(v, (int, float)) and v < 0 for v in value):
            item = {"type": "number"}
        return {"type": "array", "minItems": len(value), "maxItems": len(value), "items": item}
    else:
        raise SystemExit(f"{path}: cannot type {value!r}")
    low, high = RANGES.get(path, (None, None))
    if low is None and path not in SIGNED:
        low = 0
    open_low, open_high = EXCLUSIVE.get(path, (None, None))
    if open_low is not None:
        schema["exclusiveMinimum"] = open_low
    elif low is not None:
        schema["minimum"] = low
    if open_high is not None:
        schema["exclusiveMaximum"] = open_high
    elif high is not None:
        schema["maximum"] = high
    return schema


def block(name: str, value: dict, path: str | None = None):
    path = path or name
    properties = {}
    for key, item in value.items():
        child = f"{path}.{key}"
        properties[key] = block(key, item, child) if isinstance(item, dict) else leaf(child, item)
    return {"type": "object", "additionalProperties": False, "required": list(properties), "properties": properties}


def role_marks(value: dict):
    entry = block("entry", value["default"], "x_look_role_marks")
    return {
        "description": "How each material role is painted: the pattern, the brush marks and how the role takes light and colour. `default` covers every role not listed.",
        "type": "object", "required": ["default"], "propertyNames": {"pattern": "^[a-z][a-z0-9_]{0,63}$"},
        "additionalProperties": {"$ref": "#/$defs/look_role_marks_entry"},
    }, entry


def season_looks(value: dict):
    return {
        "type": "object", "additionalProperties": False, "required": ["winter", "spring", "summer", "autumn"],
        "properties": {name: {"$ref": "#/$defs/look_season_look"} for name in ("winter", "spring", "summer", "autumn")},
    }, block("season", value["winter"], "x_look_seasons.looks")


def build(preset: dict):
    extensions = preset.get("extensions", {})
    properties = {}
    defs = {}
    for name, value in extensions.items():
        if not name.startswith("x_look_"):
            continue
        if name == "x_look_key_mode":
            properties[name] = {"enum": ["fixed", "sun"]}
        elif name == "x_look_glaze_amount":
            properties[name] = {"type": "number", "minimum": 0, "maximum": 1}
        elif name == "x_look_role_marks":
            properties[name], defs["look_role_marks_entry"] = role_marks(value)
        elif name == "x_look_seasons":
            looks_schema, defs["look_season_look"] = season_looks(value["looks"])
            rest = {k: v for k, v in value.items() if k != "looks"}
            shape = block(name, rest)
            shape["required"].append("looks")
            shape["properties"]["looks"] = looks_schema
            properties[name] = shape
        else:
            properties[name] = block(name, value)
    return properties, defs


def render(preset_path: Path) -> dict:
    preset = json.loads(preset_path.read_text(encoding="utf-8"))
    properties, defs = build(preset)
    return {
        "_comment": "Generated by tools/look/make_look_schema.py from game/styles/storybook_painterly/v1.json; do not edit by hand.",
        "extensions_properties": properties,
        "defs": defs,
    }


def wire(contract: dict, pieces: dict) -> dict:
    """The contract with the typed pieces in place: extensions is the shared definition plus the typed properties."""
    wired = json.loads(json.dumps(contract))
    wired["properties"]["extensions"] = {"allOf": [
        {"$ref": "common.schema.json#/$defs/extensions"},
        {"description": TYPED, "properties": pieces["extensions_properties"]},
    ]}
    for name in DEFS:
        wired["$defs"].pop(name, None)
    wired["$defs"].update(pieces["defs"])
    return wired


def dump(document) -> bytes:
    return (json.dumps(document, indent=2, ensure_ascii=False) + "\n").encode("utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("preset", nargs="?", type=Path, default=DEFAULT_PRESET)
    parser.add_argument("--check", action="store_true", help="exit 1 unless contracts/style-preset.schema.json matches the preset")
    parser.add_argument("--write", action="store_true", help="write the typed pieces into contracts/style-preset.schema.json")
    args = parser.parse_args()
    pieces = render(args.preset)
    if not (args.write or args.check):
        sys.stdout.buffer.write(dump(pieces))
        return 0
    current = CONTRACT.read_bytes()
    wired = dump(wire(json.loads(current), pieces))
    if args.write:
        CONTRACT.write_bytes(wired)
        print(f"wrote {CONTRACT}")
        return 0
    if current != wired:
        print("the typed look schema in contracts/style-preset.schema.json is out of date: run tools/look/make_look_schema.py --write")
        return 1
    print("the typed look schema matches the preset")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
