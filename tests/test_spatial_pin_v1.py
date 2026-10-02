"""Spatial-pin v1 exact vectors and malformed-input rejection."""

from __future__ import annotations

import hashlib
from pathlib import Path

import pytest

from mapbuilder import spatial_pin_v1 as pin
from mapbuilder.spatial_pin_v1 import (
    barton_descriptor, decode, digest, encode, load_json_strict, validate,
)
from tests.spatial_pin_v1_compare import ROOT, compare


def test_golden_vectors_and_source_payloads() -> None:
    compare()
    descriptor = barton_descriptor(ROOT)
    assert digest(descriptor) == hashlib.sha256(encode(descriptor)).hexdigest()
    assert decode(encode(descriptor)) == descriptor


@pytest.mark.parametrize(("field", "value"), [
    ("frame_id", "café"),
    ("frame_id", "line\nbreak"),
    ("center_lat_deg", "9e1"),
    ("center_lat_deg", "90.0000000000000001"),
    ("height_offset_m", "-0"),
    ("height_scale_m", "-0.0"),
    ("height_scale_m", "NaN"),
    ("grid_side_samples", "9007199254740991"),
    ("grid_side_samples", 2049),
    ("sample_spacing_m", "0.0001"),
    ("vertical_scope", "source_declared"),
    ("surface_outside_id", "clamp"),
    ("side_m", "4095"),
    ("heights_sha256", "A15d168d5d84061e05b609b875ea82e306a539b3ad96c881528f96d06abe55d7"),
])
def test_rejects_unsafe_field(field: str, value: object) -> None:
    descriptor = barton_descriptor(ROOT)
    descriptor[field] = value
    with pytest.raises(ValueError):
        validate(descriptor)


def test_rejects_missing_unknown_and_duplicate_fields() -> None:
    descriptor = barton_descriptor(ROOT)
    del descriptor["frame_id"]
    with pytest.raises(ValueError):
        encode(descriptor)
    descriptor = barton_descriptor(ROOT)
    descriptor["unrecognized"] = "value"
    with pytest.raises(ValueError):
        encode(descriptor)
    with pytest.raises(ValueError, match="duplicate JSON key"):
        load_json_strict('{"frame_id":"first","frame_id":"second"}')
    raw = encode(barton_descriptor(ROOT))
    lines = raw.decode("ascii").split("\n")
    lines[2] = lines[1]  # Duplicate axis_x and remove axis_y.
    with pytest.raises(ValueError):
        decode("\n".join(lines).encode("ascii"))


def test_decode_rejects_reorder_non_ascii_and_truncated_bytes() -> None:
    raw = encode(barton_descriptor(ROOT))
    lines = raw.decode("ascii").split("\n")
    lines[1], lines[2] = lines[2], lines[1]
    with pytest.raises(ValueError):
        decode("\n".join(lines).encode("ascii"))
    with pytest.raises(ValueError):
        decode(raw.replace(b"axis_x=", "axis_x=é".encode("utf-8")))
    with pytest.raises(ValueError):
        decode(raw[:-1])


def test_datum_and_surface_algorithm_change_pin() -> None:
    source = barton_descriptor(ROOT)
    datum = {**source, "vertical_datum_id": "navd88", "vertical_scope": "source_declared",
             "vertical_transform_id": "source_heights_unchanged_v1"}
    diagonal = {**source, "surface_algorithm_id": "heightfield_abc_bdc_diagonal_ad_v1"}
    assert len({digest(source), digest(datum), digest(diagonal)}) == 3


@pytest.mark.parametrize(("field", "value"), [
    ("height_scale_m", 0.02),
    ("height_origin_m", 999.1),
])
def test_barton_descriptor_rejects_tampered_height_decoder(
    monkeypatch: pytest.MonkeyPatch, field: str, value: float,
) -> None:
    original = pin.load_json_strict

    def tampered_manifest(raw: str) -> object:
        loaded = original(raw)
        if isinstance(loaded, dict) and loaded.get("format") == "enfractal-map-v0":
            loaded[field] = value
        return loaded

    monkeypatch.setattr(pin, "load_json_strict", tampered_manifest)
    with pytest.raises(AssertionError):
        pin.barton_descriptor(ROOT)


@pytest.mark.parametrize(("field", "value"), [
    ("crs", "EPSG:32760"),
    ("center_projected_m", [0.0, 0.0]),
])
def test_barton_descriptor_rejects_tampered_frame(
    monkeypatch: pytest.MonkeyPatch, field: str, value: object,
) -> None:
    original = pin.load_json_strict

    def tampered_manifest(raw: str) -> object:
        loaded = original(raw)
        if isinstance(loaded, dict) and loaded.get("format") == "enfractal-map-v0":
            loaded[field] = value
        return loaded

    monkeypatch.setattr(pin, "load_json_strict", tampered_manifest)
    with pytest.raises(ValueError, match="spatial definition differs"):
        pin.barton_descriptor(ROOT)
