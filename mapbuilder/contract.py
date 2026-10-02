"""Base-map fields that a sparse world delta must keep pinned."""

from __future__ import annotations

import math
import re

from .geo import MapConfig


SPATIAL_PIN_FIELDS = (
    "format", "crs", "axes", "center_lat", "center_lon",
    "center_projected_m", "side_m", "grid_side", "sample_spacing_m",
    "tile_side_m", "height_encoding", "height_offset_m",
    "height_scale_m", "height_origin_m", "height_min_m", "height_max_m",
)


def base_pin_material(manifest: dict) -> dict:
    """Return the immutable base references, excluding bulky source payloads.

    Godot's WorldState stores a digest of the spatial fields, not this complete
    dictionary. The field list is kept explicit here for package verification;
    a serialized cross-language digest is a separate schema decision.
    """
    map_id = manifest.get("map_id")
    if not isinstance(map_id, str) or not map_id:
        raise ValueError("Base map ID missing")
    hashes = {}
    for name in ("heights_sha256", "features_sha256"):
        value = manifest.get(name)
        if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{64}", value):
            raise ValueError(f"Base map {name} invalid")
        hashes[name] = value
    try:
        spatial = {name: manifest[name] for name in SPATIAL_PIN_FIELDS}
    except KeyError as error:
        raise ValueError(f"Base map spatial field missing: {error.args[0]}") from error
    for name in ("format", "crs", "axes", "height_encoding"):
        if not isinstance(spatial[name], str) or not spatial[name]:
            raise ValueError(f"Base map {name} invalid")
    for name in ("center_lat", "center_lon", "height_offset_m", "height_scale_m", "height_origin_m", "height_min_m", "height_max_m"):
        value = spatial[name]
        if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
            raise ValueError(f"Base map {name} invalid")
    projected = spatial["center_projected_m"]
    if not isinstance(projected, list) or len(projected) != 2 or any(
        isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value)
        for value in projected
    ):
        raise ValueError("Base map projected center invalid")
    for name in ("side_m", "grid_side", "sample_spacing_m", "tile_side_m"):
        value = spatial[name]
        if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
            raise ValueError(f"Base map {name} invalid")
    return {"map_id": map_id, **hashes, "spatial": spatial}


def verify_spatial_manifest(config: MapConfig, manifest: dict) -> dict:
    """Ensure a package's pinned spatial definition matches its build config."""
    pin = base_pin_material(manifest)
    spatial = pin["spatial"]
    expected = {
        "crs": f"EPSG:{config.crs_epsg}",
        "center_lat": config.center_lat,
        "center_lon": config.center_lon,
        "center_projected_m": [round(value, 3) for value in config.center_xy],
        "side_m": config.side_m,
        "grid_side": config.grid_side,
        "sample_spacing_m": config.sample_spacing_m,
        "tile_side_m": config.tile_side_m,
    }
    if pin["map_id"] != config.map_id or any(spatial[name] != value for name, value in expected.items()):
        raise ValueError("Base map spatial definition differs from build config")
    if spatial["height_scale_m"] <= 0 or spatial["height_min_m"] > spatial["height_max_m"]:
        raise ValueError("Base map height decoder invalid")
    return pin
