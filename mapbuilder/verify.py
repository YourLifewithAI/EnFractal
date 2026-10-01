"""Checks that catch corrupt packages and geospatial mistakes before playtesting."""

from __future__ import annotations

import gzip
import json
from pathlib import Path

import numpy as np
import rasterio
from PIL import Image

from .fetch import sha256
from .geo import MapConfig
from .photo import read_evidence


def verify_package(config: MapConfig, source_dir: Path, package_dir: Path, photo_evidence_dir: Path) -> dict:
    manifest = json.loads((package_dir / "manifest.json").read_text(encoding="utf-8"))
    sources = json.loads((source_dir / "sources.json").read_text(encoding="utf-8"))
    if manifest["map_id"] != config.map_id or sources["map_id"] != config.map_id:
        raise AssertionError("Map ID differs across config, sources and package")
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
        expected_bounds = config.bounds_xy
        if raster.crs.to_epsg() != config.crs_epsg or raster.width != config.grid_side - 1 or raster.height != config.grid_side - 1:
            raise AssertionError("Source projection or bounds invalid")
        if max(abs(actual - expected) for actual, expected in zip(raster.bounds, expected_bounds)) > 0.01:
            raise AssertionError("Source extent differs from requested region")
    heights = np.fromfile(package_dir / "heights.r16", dtype="<u2")
    if heights.size != config.grid_side ** 2:
        raise AssertionError("Height grid length mismatch")
    decoded = manifest["height_offset_m"] + heights.astype(np.float32) * manifest["height_scale_m"]
    if not np.isfinite(decoded).all() or abs(float(decoded.min()) - manifest["height_min_m"]) > 0.03:
        raise AssertionError("Height decode failed")
    if abs(float(decoded.max()) - manifest["height_max_m"]) > 0.03:
        raise AssertionError("Height maximum mismatch")
    center = decoded.reshape(config.grid_side, config.grid_side)[config.grid_side//2, config.grid_side//2]
    if abs(float(center) - manifest["height_origin_m"]) > 0.03:
        raise AssertionError("Center height is inconsistent")
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
