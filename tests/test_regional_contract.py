"""Small synthetic second-region checks; no downloaded geography is implied."""

from __future__ import annotations

import json
import hashlib
from dataclasses import replace
from pathlib import Path

import numpy as np
import pytest
from rasterio.io import MemoryFile
from rasterio.transform import from_bounds

from mapbuilder.build import encode_height_grid, vertex_grid_from_dem
from mapbuilder.contract import base_pin_material, verify_spatial_manifest
from mapbuilder.geo import MapConfig
from mapbuilder.osm import _height_with_evidence, build_features
from mapbuilder.terrain import read_complete_dem
from mapbuilder.tiles import geographic_tile, geographic_tile_bounds, local_tile, local_tile_bounds
from mapbuilder.verify import verify_height_payload


SECOND_REGION = MapConfig(
    map_id="synthetic_wellington_v0", display_name="Synthetic southern fixture",
    center_lat=-41.2865, center_lon=174.7767, crs_epsg=32760,
    side_m=4, sample_spacing_m=1, tile_side_m=2,
    vegetation_seed=1, vegetation_target=0, description="No sourced geography",
)


def _dem_bytes(values: np.ndarray, nodata: float | None = None) -> bytes:
    with MemoryFile() as memory:
        with memory.open(
            driver="GTiff", width=4, height=4, count=1, dtype="float32",
            crs="EPSG:32760", transform=from_bounds(*SECOND_REGION.bounds_xy, 4, 4),
            nodata=nodata,
        ) as raster:
            raster.write(values, 1)
        return memory.read()


def _read_dem(contents: bytes) -> np.ndarray:
    with MemoryFile(contents) as memory, memory.open() as raster:
        return read_complete_dem(raster, SECOND_REGION)


def test_second_region_coordinate_height_round_trip() -> None:
    assert SECOND_REGION.to_local(SECOND_REGION.center_lon, SECOND_REGION.center_lat) == pytest.approx((0, 0), abs=0.001)
    for x, z in ((-2, -2), (0, 0), (1.5, -0.5), (2, 2)):
        lon, lat = SECOND_REGION.local_to_geographic(x, z)
        assert SECOND_REGION.to_local(lon, lat) == pytest.approx((x, z), abs=0.001)
    source = np.array([
        [-5.0, -4.0, -3.0, -2.0],
        [-3.0, -2.0, -1.0, 0.0],
        [0.0, 1.0, 2.0, 3.0],
        [2.0, 3.0, 4.0, 5.0],
    ], dtype=np.float32)
    grid = vertex_grid_from_dem(_read_dem(_dem_bytes(source)))
    encoded, offset, scale = encode_height_grid(grid)
    restored = np.frombuffer(encoded, dtype="<u2").reshape(grid.shape) * scale + offset
    assert restored == pytest.approx(grid, abs=0.0051)
    assert float(restored.min()) < 0 < float(restored.max())


def test_synthetic_height_package_is_deterministic_and_source_bound(tmp_path: Path) -> None:
    source = np.arange(-8, 8, dtype=np.float32).reshape(4, 4)
    dem = _read_dem(_dem_bytes(source))
    grid = vertex_grid_from_dem(dem)
    payload, offset, scale = encode_height_grid(grid)
    assert encode_height_grid(grid) == (payload, offset, scale)
    (tmp_path / "heights.r16").write_bytes(payload)
    manifest = {
        "heights_file": "heights.r16",
        "heights_sha256": hashlib.sha256(payload).hexdigest(),
        "height_offset_m": offset, "height_scale_m": scale,
        "height_min_m": float(grid.min()), "height_max_m": float(grid.max()),
        "height_origin_m": float(grid[2, 2]),
    }
    assert verify_height_payload(SECOND_REGION, manifest, tmp_path, dem) == pytest.approx(grid[2, 2], abs=0.005)
    changed = bytearray(payload)
    changed[0] ^= 1
    (tmp_path / "heights.r16").write_bytes(changed)
    with pytest.raises(AssertionError, match="hash"):
        verify_height_payload(SECOND_REGION, manifest, tmp_path, dem)
    manifest["heights_sha256"] = hashlib.sha256(changed).hexdigest()
    with pytest.raises(AssertionError, match="source-derived"):
        verify_height_payload(SECOND_REGION, manifest, tmp_path, dem)


def test_global_quadtree_dateline_poles_and_boundary_assignment() -> None:
    assert geographic_tile(-180, 0, 3) == geographic_tile(180, 0, 3)
    assert geographic_tile(-179.9, 0, 3).column == 0
    assert geographic_tile(179.9, 0, 3).column == 7
    assert geographic_tile(0, 90, 3).row == 0
    assert geographic_tile(0, -90, 3).row == 7
    assert geographic_tile(0, 0, 1).key == "geo-v1/1/1/1"
    assert geographic_tile_bounds(geographic_tile(179.9, -89, 1)) == (0, -90, 180, 0)
    with pytest.raises(ValueError):
        geographic_tile(0, 90.001, 2)
    with pytest.raises(ValueError):
        geographic_tile(float("nan"), 0, 2)
    with pytest.raises(ValueError):
        geographic_tile(181, 0, 2)


def test_local_tiles_include_outer_edge_but_reject_outside() -> None:
    assert local_tile(SECOND_REGION, -2, -2) == (0, 0)
    assert local_tile(SECOND_REGION, 0, 0) == (1, 1)
    assert local_tile(SECOND_REGION, 2, 2) == (1, 1)
    assert local_tile(SECOND_REGION, 2.0001, 0) is None
    assert local_tile_bounds(SECOND_REGION, 1, 1) == (0, 0, 2, 2)


@pytest.mark.parametrize("bad_sample,nodata", [(float("nan"), None), (-9999.0, -9999.0)])
def test_source_gaps_are_rejected_not_filled(bad_sample: float, nodata: float | None) -> None:
    source = np.zeros((4, 4), dtype=np.float32)
    source[2, 1] = bad_sample
    with pytest.raises(ValueError, match="1 missing or invalid source cells"):
        _read_dem(_dem_bytes(source, nodata))


def test_sparse_delta_pin_material_changes_with_spatial_frame() -> None:
    manifest = json.loads(Path("game/maps/barton_creek/manifest.json").read_text(encoding="utf-8"))
    pin = base_pin_material(manifest)
    assert pin["map_id"] == "barton_creek_v0"
    assert len(pin["heights_sha256"]) == len(pin["features_sha256"]) == 64
    assert verify_spatial_manifest(MapConfig.load(Path("maps/barton_creek/config.json")), manifest) == pin
    shifted = json.loads(json.dumps(manifest))
    shifted["center_projected_m"][0] += 1
    assert base_pin_material(shifted) != pin
    with pytest.raises(ValueError, match="differs from build config"):
        verify_spatial_manifest(MapConfig.load(Path("maps/barton_creek/config.json")), shifted)


def test_barton_mall_level_height_is_marked_inferred_in_v1() -> None:
    with pytest.raises(ValueError, match="frozen legacy"):
        build_features(MapConfig.load(Path("maps/barton_creek/config.json")), Path("maps/barton_creek/sources"))
    config = replace(MapConfig.load(Path("maps/barton_creek/config.json")), map_id="barton_creek_v1")
    features = build_features(config, Path("maps/barton_creek/sources"))
    mall = next(item for item in features["buildings"] if item["osm_id"] == 27453848)
    assert mall["height_m"] == 6.4
    assert mall["height_is_estimate"] is True
    assert mall["height_evidence"]["basis"] == "inferred_from_osm_levels"
    assert mall["height_evidence"]["raw_value"] == "2"
    assert mall["footprint_evidence"]["basis"] == "osm_way_geometry"
    assert mall["osm_type"] == "way"
    assert mall["feature_id"].startswith("barton_creek_v1:osm:way:27453848:buildings:")
    assert mall["source_snapshot_sha256"] == features["metadata"]["source_snapshot_sha256"]
    ids = [item["feature_id"] for kind in features["metadata"]["counts"] if kind != "trees" for item in features[kind]]
    assert len(ids) == len(set(ids))
    assert len(ids) == sum(features["metadata"]["counts"].values())
    assert features["metadata"]["projected_coordinate_units"].endswith("not true ENU")

    legacy = build_features(MapConfig.load(Path("maps/barton_creek/config.json")), Path("maps/barton_creek/sources"), provenance_version="legacy-v0")
    legacy_mall = next(item for item in legacy["buildings"] if item["osm_id"] == 27453848)
    assert legacy_mall["height_is_estimate"] is False  # Historical error retained only to reproduce v0 bytes.
    old_features = json.loads(Path("game/maps/barton_creek/features.json").read_text(encoding="utf-8"))
    for kind in legacy["metadata"]["counts"]:
        assert legacy[kind] == old_features[kind]


def test_building_height_evidence_distinguishes_tag_levels_and_default() -> None:
    assert _height_with_evidence({"height": "12 m", "building:levels": "3"})[:2] == (12.0, False)
    assert _height_with_evidence({"building:levels": "2"})[2]["basis"] == "inferred_from_osm_levels"
    assert _height_with_evidence({"height": "unknown", "building": "retail"}) == (
        10.0, True, {"basis": "illustrative_default", "source_tag": "building", "raw_value": "retail"}
    )
