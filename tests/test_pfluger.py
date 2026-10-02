"""Geographic/source integrity gates for the new district, independent of Barton."""
import gzip
import json
from pathlib import Path
import shutil

import pytest
from shapely.geometry import LineString, Point, Polygon

from mapbuilder.geo import MapConfig
from mapbuilder.pfluger import (
    DEFAULT, build_package, fetch_sources, lake_geometry, landing_route,
    trace_boundary, validate_sources, verify_package,
)

CONFIG_PATH = DEFAULT / "config.json"
SOURCES = DEFAULT / "sources/2026-10-02"
PACKAGE = DEFAULT / "package"


@pytest.fixture(scope="module")
def config():
    return MapConfig.load(CONFIG_PATH)


def test_boundary_follows_four_named_families_and_covers_bridge(config):
    district, evidence = trace_boundary(config, SOURCES)
    assert district.covers(Point(config.to_local(-97.75585, 30.26555)))
    assert not district.covers(Point(config.to_local(-97.743, 30.271)))
    assert set(evidence["boundary_sources"]) == {"north", "south", "east", "west"}
    assert all(side["osm_way_ids"] for side in evidence["boundary_sources"].values())
    assert 3.3e6 < district.area < 3.4e6
    assert evidence["connector"]["length_m"] < 30
    assert evidence["geojson"]["type"] == "Polygon"


def test_landing_alignment_is_bounded_and_does_not_claim_walkability(config):
    district, _ = trace_boundary(config, SOURCES)
    route = landing_route(config, SOURCES, district)
    line = LineString(route["points_local_xz_m"])
    assert 149.999 < line.length < 150.001
    assert district.covers(line)
    assert route["source_osm_way_ids"]
    assert route["player_height_m"] == .30
    assert route["walkability_verified"] is False
    assert route["vertical_status"] == "requires_bridge_deck_and_ramp_audit"


def test_complete_lake_has_island_holes(config):
    lake, audit = lake_geometry(config, SOURCES)
    assert audit["complete"] is True
    assert audit["member_counts"] == {"outer": 5, "inner": 10}
    assert lake.is_valid and lake.covers(Point(config.to_local(-97.756, 30.2655)))
    parts = [lake] if isinstance(lake, Polygon) else lake.geoms
    assert sum(len(p.interiors) for p in parts) >= 1


def test_source_mutation_and_unmanifested_input_are_rejected(config, tmp_path):
    source = tmp_path / "source"
    shutil.copytree(SOURCES, source)
    extra = source / "osm_unpinned.osm.gz"
    extra.write_bytes(gzip.compress(b"<osm/>"))
    with pytest.raises(ValueError, match="files differ"):
        validate_sources(config, source)
    extra.unlink()
    metadata = json.loads((source / "sources.json").read_text(encoding="utf-8"))
    original = source / metadata["osm"][0]["file"]
    original.write_bytes(gzip.compress(b"<osm/>"))
    with pytest.raises(ValueError, match="OSM hash mismatch"):
        validate_sources(config, source)


def test_fetch_refuses_to_replace_snapshot(config):
    with pytest.raises(FileExistsError, match="Refuse"):
        fetch_sources(config, SOURCES)


def test_offline_build_is_byte_reproducible_and_detects_corruption(tmp_path):
    package = tmp_path / "package"
    result = build_package(CONFIG_PATH, SOURCES, package)
    assert result["status"] == "verified_source_foundation"
    assert result["collision_validated"] is False
    assert sorted(p.name for p in package.iterdir()) == sorted(p.name for p in PACKAGE.iterdir())
    for file in package.iterdir():
        assert file.read_bytes() == (PACKAGE / file.name).read_bytes(), file.name
    heights = package / "heights.r16"
    data = bytearray(heights.read_bytes())
    data[len(data)//2] ^= 1
    heights.write_bytes(data)
    with pytest.raises(ValueError, match="Package hash mismatch: heights"):
        verify_package(CONFIG_PATH, SOURCES, package)


def test_audit_exposes_unresolved_geometry_and_outlier():
    audit = json.loads((PACKAGE / "audit.json").read_text(encoding="utf-8"))
    assert audit["terrain"]["envelope_lowest_sample_inside_district"] is False
    assert audit["terrain"]["district_pixel_center_elevation_range_m"][0] > 120
    assert not audit["photos_included"] and not audit["collision_validated"]
    assert audit["feature_counts"]["trees"] == 0
    assert len(audit["pending_gates"]) >= 5


def test_pinned_text_metadata_uses_checkout_stable_lf():
    for path in DEFAULT.rglob("*.json"):
        assert b"\r\n" not in path.read_bytes(), path
