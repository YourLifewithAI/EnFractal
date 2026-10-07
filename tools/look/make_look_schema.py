#!/usr/bin/env python3
"""Write the typed schema for a preset's x_look_* extension blocks, from the preset itself.

The look keeps every number it uses in the preset's `extensions` as `x_look_*` blocks (the integrator promotes or removes
extension keys; review M6 asked for this before the preset becomes a candidate). This tool reads a preset and prints the
JSON Schema that describes those blocks exactly as the preset states them: every block and every field required, no
unknown field allowed, numbers finite, colours `#rrggbb`, and the ranges the C# reader enforces. Wiring it in takes one
change to contracts/style-preset.schema.json: the `extensions` property becomes the shared extensions definition plus the
typed `properties` below, and the `$defs` below are added. docs/look/proposals/style-preset-look-tuning.diff is that change.

  python tools/look/make_look_schema.py [PRESET] [--defs]     print the typed pieces (default preset: storybook_painterly v1)
  python tools/look/make_look_schema.py --check               fail unless the proposal in docs/look/proposals is current

Standard library only.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_PRESET = ROOT / "game" / "styles" / "storybook_painterly" / "v1.json"
PROPOSAL = ROOT / "docs" / "look" / "proposals" / "style-preset-look-tuning.schema.json"
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
}
# Numbers that may be negative: every other number must not be.
INTEGERS = {"x_look_shadows.key_splits", "x_look_gi.subdiv", "x_look_grade.lut_size", "x_look_seasons.centre_days", "x_look_seasons.fixed_day_of_year"}
SIGNED = {"x_look_sun.sunrise_elevation_deg", "x_look_sun.twilight_elevation_deg", "x_look_grade.warmth_rgb", "x_look_grade.night_tint_rgb"}
ENUMS = {
    "x_look_glow.blend_mode": ["additive", "screen", "softlight", "replace", "mix"],
    "x_look_role_marks.pattern": ["none", "wood", "fabric", "plaster", "paper", "cardboard", "brushed", "speckle", "smooth", "stone"],
    "x_look_role_marks.stroke_axis": ["x", "y", "z"],
}


def leaf(path: str, value):
    if isinstance(value, bool):
        return {"type": "boolean"}
    if isinstance(value, (int, float)):
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
    if low is not None:
        schema["minimum"] = low
    if high is not None:
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


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("preset", nargs="?", type=Path, default=DEFAULT_PRESET)
    parser.add_argument("--check", action="store_true", help="exit 1 unless docs/look/proposals/style-preset-look-tuning.schema.json matches the preset")
    parser.add_argument("--write", action="store_true", help="write the proposal file")
    args = parser.parse_args()
    text = json.dumps(render(args.preset), indent=2, ensure_ascii=False) + "\n"
    if args.write:
        PROPOSAL.parent.mkdir(parents=True, exist_ok=True)
        PROPOSAL.write_bytes(text.encode("utf-8"))
        print(f"wrote {PROPOSAL}")
        return 0
    if args.check:
        if not PROPOSAL.exists() or PROPOSAL.read_bytes().decode("utf-8") != text:
            print("the typed look schema in docs/look/proposals is out of date: run tools/look/make_look_schema.py --write")
            return 1
        print("the typed look schema matches the preset")
        return 0
    sys.stdout.write(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
