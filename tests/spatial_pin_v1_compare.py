"""Check the spatial-pin v1 Python vectors and optional independent Godot output."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from mapbuilder.spatial_pin_v1 import barton_descriptor, decode, digest, encode, load_json_strict


ROOT = Path(__file__).resolve().parents[1]
CASES = ROOT / "tests/fixtures/spatial_pin_v1_cases.json"
GOLDEN = ROOT / "tests/fixtures/spatial_pin_v1_golden.json"


def vectors() -> list[dict[str, str]]:
    fixture = load_json_strict(CASES.read_text(encoding="utf-8"))
    assert fixture["base"] == barton_descriptor(ROOT)
    results = []
    for case in fixture["cases"]:
        descriptor = {**fixture["base"], **case["changes"]}
        if case["id"] == "second_frame":
            from pyproj import Transformer

            east, north = Transformer.from_crs(4326, 32760, always_xy=True).transform(
                float(descriptor["center_lon_deg"]), float(descriptor["center_lat_deg"]),
            )
            assert (repr(east), repr(north)) == (
                descriptor["center_projected_easting_m"], descriptor["center_projected_northing_m"],
            )
        payloads = case.get("payloads_ascii")
        if payloads:
            assert hashlib.sha256(payloads["heights"].encode("ascii")).hexdigest() == descriptor["heights_sha256"]
            assert hashlib.sha256(payloads["features"].encode("ascii")).hexdigest() == descriptor["features_sha256"]
        raw = encode(descriptor)
        assert decode(raw) == descriptor
        results.append({"id": case["id"], "bytes_hex": raw.hex(), "sha256": digest(descriptor)})
    return results


def compare(godot_path: Path | None = None) -> None:
    expected = json.loads(GOLDEN.read_text(encoding="utf-8"))["results"]
    assert vectors() == expected, "Python bytes/hash differ from checked-in goldens"
    if godot_path is not None:
        godot = json.loads(godot_path.read_text(encoding="utf-8"))
        assert godot["engine"].startswith("4.7.2"), "unexpected Godot version"
        assert godot["results"] == expected, "Godot bytes/hash differ from checked-in goldens"


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--godot", type=Path)
    parser.add_argument("--write-golden", action="store_true")
    args = parser.parse_args()
    if args.write_golden:
        GOLDEN.write_text(json.dumps({"profile": "enfractal-spatial-pin-v1", "results": vectors()}, indent=2) + "\n", encoding="utf-8")
        print(f"wrote {GOLDEN}")
    else:
        compare(args.godot)
        print(f"spatial-pin v1 exact bytes/hash match for {len(vectors())} cases")
