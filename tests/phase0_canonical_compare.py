"""Compare the existing Godot WorldState hash bytes with Python's JSON encoder.

This is an E02 feasibility probe, not the future network canonicalization rule.
The checked-in Godot output can be regenerated with the documented headless
probe. No player save or runtime schema is changed by this script.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

from mapbuilder.contract import SPATIAL_PIN_FIELDS


ROOT = Path(__file__).resolve().parents[1]
CASES = ROOT / "tests/fixtures/phase0_canonical_cases.json"
GODOT = ROOT / "tests/fixtures/phase0_canonical_godot_4.7.2.json"
MANIFEST = ROOT / "game/maps/barton_creek/manifest.json"


def _godot_parsed_numbers(value):
    """Observed Godot JSON.parse_string behavior; not a production encoder."""
    if isinstance(value, dict):
        return {key: _godot_parsed_numbers(item) for key, item in value.items()}
    if isinstance(value, list):
        return [_godot_parsed_numbers(item) for item in value]
    if isinstance(value, bool) or value is None:
        return value
    if isinstance(value, (int, float)):
        return 0.0 if value == 0 else float(value)
    return value


def _python_bytes(value) -> bytes:
    return json.dumps(
        value, sort_keys=True, separators=(",", ":"), ensure_ascii=False,
        allow_nan=False,
    ).encode("utf-8")


def compare() -> list[tuple[str, bool, bool, bytes, bytes, bytes]]:
    cases = json.loads(CASES.read_text(encoding="utf-8"))["cases"]
    godot = json.loads(GODOT.read_text(encoding="utf-8"))
    assert godot["engine"].startswith("4.7.2")
    observed = {result["id"]: result for result in godot["results"]}
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    assert set(observed) == {case["id"] for case in cases}
    rows = []
    for case in cases:
        value = case.get("value")
        if case.get("kind") == "barton_spatial":
            value = {key: manifest[key] for key in SPATIAL_PIN_FIELDS}
        python_bytes = _python_bytes(value)
        normalized_bytes = _python_bytes(_godot_parsed_numbers(value))
        godot_bytes = bytes.fromhex(observed[case["id"]]["json_utf8_hex"])
        assert hashlib.sha256(godot_bytes).hexdigest() == observed[case["id"]]["sha256"]
        if case["id"] == "barton_spatial":
            assert observed[case["id"]]["sha256"] == godot["world_state_spatial_sha256"]
        rows.append((case["id"], python_bytes == godot_bytes,
                     normalized_bytes == godot_bytes, python_bytes,
                     normalized_bytes, godot_bytes))
    return rows


if __name__ == "__main__":
    for case_id, raw_same, normalized_same, python_bytes, normalized_bytes, godot_bytes in compare():
        print(f"{case_id}: raw={'same' if raw_same else 'different'}, parsed-number normalization={'same' if normalized_same else 'different'}")
        print(f"  Godot SHA-256: {hashlib.sha256(godot_bytes).hexdigest()}")
        if not normalized_same:
            print(f"  Normalized Python: {normalized_bytes!r}")
            print(f"  Godot:             {godot_bytes!r}")
