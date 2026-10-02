"""Bounded, cross-language spatial-pin v1 fixture.

This is deliberately separate from the legacy Godot world-state save pin. The
wire profile uses sorted ASCII key/value records instead of JSON number or
Unicode escaping rules. It is not a general world serialization format.
"""

from __future__ import annotations

import hashlib
import json
import re
from decimal import Decimal, InvalidOperation
from pathlib import Path
from typing import Mapping


DOMAIN = b"enfractal-spatial-pin-v1\n"
FIELDS = (
    "axis_x", "axis_y", "axis_z", "center_lat_deg", "center_lon_deg",
    "center_projected_easting_m", "center_projected_northing_m",
    "features_sha256", "frame_id", "geographic_crs", "grid_side_samples",
    "height_decoder_id", "height_offset_m", "height_origin_m",
    "height_scale_m", "heights_sha256", "horizontal_crs", "map_format",
    "map_id", "sample_spacing_m", "side_m", "surface_algorithm_id",
    "surface_outside_id", "tile_side_m", "transform_pipeline_id",
    "vertical_datum_id", "vertical_scope", "vertical_transform_id",
)
assert FIELDS == tuple(sorted(FIELDS))

DECIMAL_FIELDS = frozenset((
    "center_lat_deg", "center_lon_deg", "center_projected_easting_m",
    "center_projected_northing_m", "height_offset_m", "height_origin_m",
    "height_scale_m", "sample_spacing_m", "side_m", "tile_side_m",
))
HASH_FIELDS = frozenset(("features_sha256", "heights_sha256"))
COUNT_FIELDS = frozenset(("grid_side_samples",))
TOKEN_FIELDS = frozenset(FIELDS) - DECIMAL_FIELDS - HASH_FIELDS - COUNT_FIELDS
_DECIMAL = re.compile(r"-?(?:0|[1-9][0-9]*)(?:\.[0-9]*[1-9])?\Z", re.ASCII)
_TOKEN = re.compile(r"[A-Za-z][A-Za-z0-9_:.+/-]{0,127}\Z", re.ASCII)
_HASH = re.compile(r"[0-9a-f]{64}\Z", re.ASCII)
_MAX_BYTES = 8192


def _number(name: str, value: object) -> Decimal:
    if not isinstance(value, str) or len(value) > 32 or not _DECIMAL.fullmatch(value):
        raise ValueError(f"{name}: expected normalized ASCII decimal string")
    try:
        number = Decimal(value)
    except InvalidOperation as error:
        raise ValueError(f"{name}: invalid decimal") from error
    if number.is_signed() and number.is_zero():
        raise ValueError(f"{name}: negative zero is forbidden")
    if abs(number) > Decimal("1000000000"):
        raise ValueError(f"{name}: outside bounded decimal range")
    return number


def validate(descriptor: Mapping[str, object]) -> None:
    if not isinstance(descriptor, Mapping) or set(descriptor) != set(FIELDS):
        raise ValueError("spatial pin fields missing or unknown")
    for name in FIELDS:
        value = descriptor[name]
        if name in DECIMAL_FIELDS:
            _number(name, value)
        elif name in HASH_FIELDS:
            if not isinstance(value, str) or not _HASH.fullmatch(value):
                raise ValueError(f"{name}: expected lowercase SHA-256")
        elif name in COUNT_FIELDS:
            if not isinstance(value, str) or not re.fullmatch(r"[1-9][0-9]{0,4}", value, re.ASCII) or not 2 <= int(value) <= 65536:
                raise ValueError(f"{name}: count outside 2..65536")
        elif not isinstance(value, str) or not _TOKEN.fullmatch(value):
            raise ValueError(f"{name}: unsafe or non-ASCII token")

    if not -90 <= _number("center_lat_deg", descriptor["center_lat_deg"]) <= 90:
        raise ValueError("center_lat_deg: outside geographic range")
    if not -180 <= _number("center_lon_deg", descriptor["center_lon_deg"]) <= 180:
        raise ValueError("center_lon_deg: outside geographic range")
    for name in ("height_scale_m", "sample_spacing_m", "side_m", "tile_side_m"):
        if _number(name, descriptor[name]) <= 0:
            raise ValueError(f"{name}: must be positive")
    for name in ("sample_spacing_m", "side_m", "tile_side_m"):
        value = descriptor[name]
        if _number(name, value) > 1000000 or ("." in value and len(value.partition(".")[2]) > 3):
            raise ValueError(f"{name}: geometry precision/range exceeds v1")
    side = _number("side_m", descriptor["side_m"])
    spacing = _number("sample_spacing_m", descriptor["sample_spacing_m"])
    tile = _number("tile_side_m", descriptor["tile_side_m"])
    if (int(descriptor["grid_side_samples"]) - 1) * spacing != side:
        raise ValueError("grid_side_samples/spacing do not equal side_m")
    if tile > side or side % tile or tile % spacing:
        raise ValueError("tile/side/sample spacing are not integral")
    if descriptor["vertical_datum_id"] == "unknown":
        if descriptor["vertical_scope"] != "local_only" or descriptor["vertical_transform_id"] != "none":
            raise ValueError("unknown vertical datum must be local_only with no transform")
    elif descriptor["vertical_scope"] != "source_declared":
        raise ValueError("identified vertical datum is only source_declared in v1")
    if descriptor["surface_algorithm_id"] not in (
        "heightfield_acb_bcd_diagonal_bc_v1", "heightfield_abc_bdc_diagonal_ad_v1",
    ):
        raise ValueError("unsupported surface algorithm")
    if descriptor["surface_outside_id"] != "closed_square_error_outside_v1":
        raise ValueError("unsupported surface boundary rule")
    if descriptor["height_decoder_id"] != "uint16le_row_north_offset_scale_v1":
        raise ValueError("unsupported height decoder")


def encode(descriptor: Mapping[str, object]) -> bytes:
    """Return exactly one canonical, domain-separated ASCII byte string."""
    validate(descriptor)
    body = "".join(f"{name}={descriptor[name]}\n" for name in FIELDS).encode("ascii")
    result = DOMAIN + body
    if len(result) > _MAX_BYTES:
        raise ValueError("spatial pin too large")
    return result


def decode(raw: bytes) -> dict[str, str]:
    """Reject reordered, duplicate, unknown, non-ASCII and noncanonical records."""
    if not isinstance(raw, bytes) or len(raw) > _MAX_BYTES:
        raise ValueError("invalid spatial pin bytes")
    try:
        lines = raw.decode("ascii").split("\n")
    except UnicodeDecodeError as error:
        raise ValueError("spatial pin must be ASCII") from error
    if len(lines) != len(FIELDS) + 2 or lines[0] != DOMAIN.decode("ascii").rstrip("\n") or lines[-1] != "":
        raise ValueError("invalid spatial pin framing")
    descriptor: dict[str, str] = {}
    for expected, line in zip(FIELDS, lines[1:-1], strict=True):
        name, separator, value = line.partition("=")
        if not separator or name != expected:
            raise ValueError("spatial pin field order/identity mismatch")
        descriptor[name] = value
    if encode(descriptor) != raw:
        raise ValueError("noncanonical spatial pin")
    return descriptor


def digest(descriptor: Mapping[str, object]) -> str:
    return hashlib.sha256(encode(descriptor)).hexdigest()


def load_json_strict(raw: str) -> object:
    """Use for a JSON envelope before encoding; JSON duplicate keys fail closed."""
    def unique_pairs(pairs: list[tuple[str, object]]) -> dict[str, object]:
        result: dict[str, object] = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"duplicate JSON key: {key}")
            result[key] = value
        return result

    def reject_constant(value: str) -> None:
        raise ValueError(f"non-finite JSON number: {value}")

    return json.loads(raw, object_pairs_hook=unique_pairs, parse_constant=reject_constant)


def barton_descriptor(repo_root: Path) -> dict[str, str]:
    """Construct the read-only Barton fixture and verify its immutable payloads.

    The full-precision projected center comes from the source build config, not
    the millimetre-rounded existing manifest field. An environment with a
    different transform implementation must create a reviewed new frame.
    """
    import pyproj
    import rasterio

    from .contract import verify_spatial_manifest
    from .geo import MapConfig
    from .terrain import read_complete_dem
    from .verify import verify_height_payload

    if (pyproj.__version__, pyproj.proj_version_str) != ("3.8.0", "9.8.1"):
        raise ValueError("Barton fixture requires pyproj 3.8.0 / PROJ 9.8.1")
    config = MapConfig.load(repo_root / "maps/barton_creek/config.json")
    source_dir = repo_root / "maps/barton_creek/sources"
    package = repo_root / "game/maps/barton_creek"
    manifest = load_json_strict((package / "manifest.json").read_text(encoding="utf-8"))
    if not isinstance(manifest, dict) or manifest.get("map_id") != config.map_id:
        raise ValueError("Barton manifest/config mismatch")
    verify_spatial_manifest(config, manifest)
    sources = load_json_strict((source_dir / "sources.json").read_text(encoding="utf-8"))
    dem_path = source_dir / "usgs_3dep_2m.tif"
    if not isinstance(sources, dict) or sources.get("map_id") != config.map_id:
        raise ValueError("Barton source metadata mismatch")
    if hashlib.sha256(dem_path.read_bytes()).hexdigest() != sources["dem"]["sha256"]:
        raise ValueError("Barton source DEM hash mismatch")
    for payload, field in (("heights.r16", "heights_sha256"), ("features.json", "features_sha256")):
        if hashlib.sha256((package / payload).read_bytes()).hexdigest() != manifest.get(field):
            raise ValueError(f"Barton {payload} payload hash mismatch")
    with rasterio.open(dem_path) as raster:
        dem = read_complete_dem(raster, config)
    verify_height_payload(config, manifest, package, dem)
    center_east, center_north = config.center_xy
    if [round(center_east, 3), round(center_north, 3)] != manifest.get("center_projected_m"):
        raise ValueError("Barton manifest rounded projected center mismatch")
    descriptor: dict[str, str] = {
        "axis_x": "utm_grid_easting_offset_m",
        "axis_y": "decoded_height_minus_origin_m",
        "axis_z": "negative_utm_grid_northing_offset_m",
        "center_lat_deg": repr(config.center_lat),
        "center_lon_deg": repr(config.center_lon),
        "center_projected_easting_m": repr(center_east),
        "center_projected_northing_m": repr(center_north),
        "features_sha256": manifest["features_sha256"],
        "frame_id": "barton_creek_v1_local",
        "geographic_crs": "EPSG:4326",
        "grid_side_samples": str(config.grid_side),
        "height_decoder_id": "uint16le_row_north_offset_scale_v1",
        "height_offset_m": str(manifest["height_offset_m"]),
        "height_origin_m": str(manifest["height_origin_m"]),
        "height_scale_m": str(manifest["height_scale_m"]),
        "heights_sha256": manifest["heights_sha256"],
        "horizontal_crs": manifest["crs"],
        "map_format": manifest["format"],
        "map_id": config.map_id,
        "sample_spacing_m": str(config.sample_spacing_m),
        "side_m": str(config.side_m),
        "surface_algorithm_id": "heightfield_acb_bcd_diagonal_bc_v1",
        "surface_outside_id": "closed_square_error_outside_v1",
        "tile_side_m": str(config.tile_side_m),
        "transform_pipeline_id": "epsg4326_to_epsg32614_xy_pyproj3.8.0_proj9.8.1_subtract_v1",
        "vertical_datum_id": "unknown",
        "vertical_scope": "local_only",
        "vertical_transform_id": "none",
    }
    validate(descriptor)
    return descriptor
