"""Checks that catch corrupt packages and geospatial mistakes before playtesting."""

from __future__ import annotations

import gzip
import json
from pathlib import Path

import numpy as np
import rasterio
from PIL import Image

from .build import vertex_grid_from_dem
from .contract import verify_spatial_manifest
from .fetch import sha256
from .geo import MapConfig
from .photo import read_evidence
from .terrain import read_complete_dem
from .tiles import GEOGRAPHIC_TILE_SCHEME, LOCAL_TILE_SCHEME


def verify_height_payload(config: MapConfig, manifest: dict, package_dir: Path, dem: np.ndarray) -> float:
    """Check every decoded height against the pinned source-derived vertex grid."""
    payload = (package_dir / manifest["heights_file"]).read_bytes()
    if sha256(payload) != manifest["heights_sha256"]:
        raise AssertionError("Height payload hash mismatch")
    heights = np.frombuffer(payload, dtype="<u2")
    if heights.size != config.grid_side ** 2:
        raise AssertionError("Height grid length mismatch")
    scale = manifest["height_scale_m"]
    offset = manifest["height_offset_m"]
    if not np.isfinite([scale, offset]).all() or scale <= 0:
        raise AssertionError("Height decoder invalid")
    decoded = (offset + heights.astype(np.float64) * scale).reshape(config.grid_side, config.grid_side)
    source_grid = vertex_grid_from_dem(dem)
    if float(np.max(np.abs(decoded - source_grid))) > scale * 0.5 + 0.001:
        raise AssertionError("Height payload differs from source-derived terrain")
    if abs(float(decoded.min()) - manifest["height_min_m"]) > 0.03:
        raise AssertionError("Height minimum mismatch")
    if abs(float(decoded.max()) - manifest["height_max_m"]) > 0.03:
        raise AssertionError("Height maximum mismatch")
    center = float(decoded[config.grid_side // 2, config.grid_side // 2])
    if abs(center - manifest["height_origin_m"]) > 0.03:
        raise AssertionError("Center height is inconsistent")
    return center


def verify_package(config: MapConfig, source_dir: Path, package_dir: Path, photo_evidence_dir: Path) -> dict:
    manifest = json.loads((package_dir / "manifest.json").read_text(encoding="utf-8"))
    sources = json.loads((source_dir / "sources.json").read_text(encoding="utf-8"))
    if manifest["map_id"] != config.map_id or sources["map_id"] != config.map_id:
        raise AssertionError("Map ID differs across config, sources and package")
    try:
        verify_spatial_manifest(config, manifest)
    except ValueError as error:
        raise AssertionError(str(error)) from error
    if sha256((source_dir / "usgs_3dep_2m.tif").read_bytes()) != sources["dem"]["sha256"]:
        raise AssertionError("Source DEM hash mismatch")
    for record in sources["osm"]:
        data = gzip.decompress((source_dir / record["file"]).read_bytes())
        if sha256(data) != record["sha256_uncompressed"]:
            raise AssertionError(f"Source OSM hash mismatch: {record['file']}")
    checked = {}
    for name, hash_key in (("heights.r16", "heights_sha256"), ("features.json", "features_sha256"), ("preview.png", "preview_sha256"), ("photo_pilot.json", "photo_pilot_sha256"), ("photo_overlay.png", "photo_overlay_sha256")):
        data = (package_dir / name).read_bytes()
        if sha256(data) != manifest[hash_key]:
            raise AssertionError(f"Package hash mismatch: {name}")
        checked[name] = len(data)
    with rasterio.open(source_dir / "usgs_3dep_2m.tif") as raster:
        try:
            dem = read_complete_dem(raster, config)
        except ValueError as error:
            raise AssertionError(f"Source DEM cannot become playable terrain: {error}") from error
    addressing = manifest.get("tile_addressing")
    if addressing is not None and (
        not isinstance(addressing, dict)
        or addressing.get("local") != LOCAL_TILE_SCHEME
        or addressing.get("geographic") != GEOGRAPHIC_TILE_SCHEME
    ):
        raise AssertionError("Tile addressing contract invalid")
    center = verify_height_payload(config, manifest, package_dir, dem)
    features = json.loads((package_dir / "features.json").read_text(encoding="utf-8"))
    for kind, count in manifest["feature_counts"].items():
        if len(features[kind]) != count:
            raise AssertionError(f"Feature count mismatch: {kind}")
    if not any(item["name"] == "Barton Creek Square Mall" for item in features["buildings"]):
        raise AssertionError("Named Barton Creek Square landmark missing")
    if not any("Barton Creek" in item["name"] for item in features["waterways"]):
        raise AssertionError("Barton Creek waterway missing")
    if len(features["trees"]) != config.vegetation_target:
        raise AssertionError("Vegetation count mismatch")
    read_evidence(photo_evidence_dir)
    pilot = json.loads((package_dir / "photo_pilot.json").read_text(encoding="utf-8"))
    if pilot["evidence_sha256"] != sha256((photo_evidence_dir / "evidence.json").read_bytes()):
        raise AssertionError("Photo evidence hash mismatch")
    if pilot["overlay_sha256"] != manifest["photo_overlay_sha256"] or len(pilot["rock_instances"]) < 30 or len(pilot["parking_markings"]) < 50:
        raise AssertionError("Photo pilot output invalid")
    with Image.open(package_dir / "photo_overlay.png") as overlay_image:
        if overlay_image.mode != "RGB" or overlay_image.size != (1024, 1024):
            raise AssertionError("Photo overlay dimensions invalid")
        overlay = np.asarray(overlay_image)
        if not all(np.count_nonzero(overlay[:, :, channel]) > 100 for channel in range(3)):
            raise AssertionError("A photo overlay channel is empty")
    half = config.side_m / 2
    for kind in ("buildings", "trees"):
        for item in features[kind]:
            position = item["proxy"] if kind == "buildings" else item
            if not (-half <= position["x"] <= half and -half <= position["z"] <= half):
                raise AssertionError(f"Out-of-bounds {kind} instance")
    return {
        "map_id": config.map_id,
        "status": "verified",
        "package_files_bytes": checked,
        "source_elevation_range_m": [sources["dem"]["minimum_m"], sources["dem"]["maximum_m"]],
        "feature_counts": manifest["feature_counts"],
        "center_height_m": round(float(center), 3),
        "photo_pilot_rocks": len(pilot["rock_instances"]),
    }
