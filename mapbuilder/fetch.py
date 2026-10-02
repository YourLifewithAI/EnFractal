"""Fetch a bounded USGS terrain sample and OSM snapshot for offline builds."""

from __future__ import annotations

import gzip
import hashlib
import json
import time
from datetime import datetime, timezone
from pathlib import Path

import rasterio
import requests
from rasterio.io import MemoryFile

from .geo import MapConfig
from .terrain import read_complete_dem


USGS_EXPORT = "https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer/exportImage"
USGS_INDEX = "https://index.nationalmap.gov/arcgis/rest/services/3DEPElevationIndex/MapServer/1/query"
OSM_MAP = "https://api.openstreetmap.org/api/0.6/map"
USER_AGENT = "EnFractalMapPrototype/0.1 (https://github.com/YourLifewithAI/EnFractal)"


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _response(session: requests.Session, url: str, params: dict, timeout: int = 90) -> requests.Response:
    for attempt in range(3):
        try:
            response = session.get(url, params=params, timeout=timeout)
            if response.status_code in (429, 502, 503, 504) and attempt < 2:
                time.sleep(2 ** attempt)
                continue
            response.raise_for_status()
            return response
        except (requests.Timeout, requests.ConnectionError):
            if attempt == 2:
                raise
            time.sleep(2 ** attempt)
    raise AssertionError("unreachable")


def check_one_meter_coverage(config: MapConfig, session: requests.Session) -> None:
    """Check center and four corners against USGS's published 1 m index."""
    west, south, east, north = config.bounds_xy
    for x, y in ((west, south), (west, north), (east, south), (east, north), config.center_xy):
        lon, lat = config.to_geographic.transform(x, y)
        params = {
            "geometry": f"{lon:.9f},{lat:.9f}",
            "geometryType": "esriGeometryPoint",
            "inSR": 4326,
            "spatialRel": "esriSpatialRelIntersects",
            "outFields": "OBJECTID",
            "returnGeometry": "false",
            "f": "pjson",
        }
        result = _response(session, USGS_INDEX, params).json()
        if result.get("error") or not result.get("features"):
            raise RuntimeError(f"1 m 3DEP coverage not confirmed at {lat:.6f}, {lon:.6f}: {result}")


def fetch_dem(config: MapConfig, target: Path, session: requests.Session) -> dict:
    """Archive the exact GeoTIFF bytes returned by the official USGS service."""
    west, south, east, north = config.bounds_xy
    side = config.side_m // config.sample_spacing_m
    params = {
        "bbox": f"{west:.3f},{south:.3f},{east:.3f},{north:.3f}",
        "bboxSR": config.crs_epsg,
        "imageSR": config.crs_epsg,
        "size": f"{side},{side}",
        "format": "tiff",
        "pixelType": "F32",
        "interpolation": "RSP_BilinearInterpolation",
        "f": "image",
    }
    response = _response(session, USGS_EXPORT, params, timeout=180)
    if not response.content.startswith((b"II*\x00", b"MM\x00*")):
        raise RuntimeError(f"USGS did not return a TIFF: {response.text[:400]}")
    with MemoryFile(response.content) as memory_file, memory_file.open() as dataset:
        try:
            band = read_complete_dem(dataset, config)
        except ValueError as error:
            raise RuntimeError(f"USGS DEM cannot become playable terrain: {error}") from error
        coverage = 1.0
        minimum = float(band.min())
        maximum = float(band.max())
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(response.content)
    return {
        "url": response.url,
        "retrieved_at_utc": datetime.now(timezone.utc).isoformat(),
        "sha256": sha256(response.content),
        "bytes": len(response.content),
        "source_service": "USGS 3DEP bare-earth dynamic image service",
        "source_index": USGS_INDEX,
        "source_sample_spacing_m": config.sample_spacing_m,
        "service_coverage_may_mix_source_resolutions": True,
        "vertical_datum": "not established by image-service response; use relative local heights only",
        "valid_fraction": coverage,
        "minimum_m": minimum,
        "maximum_m": maximum,
    }


def _osm_quadrants(config: MapConfig):
    west, south, east, north = config.bounds_xy
    mid_east, mid_north = config.center_xy
    for row, (bottom, top) in enumerate(((south, mid_north), (mid_north, north))):
        for col, (left, right) in enumerate(((west, mid_east), (mid_east, east))):
            yield f"{row}{col}", (left, bottom, right, top)


def fetch_osm(config: MapConfig, target_dir: Path, session: requests.Session) -> list[dict]:
    """Use the OSM map endpoint in small requests; recursively split if needed."""
    target_dir.mkdir(parents=True, exist_ok=True)
    records: list[dict] = []

    def visit(name: str, projected_bounds: tuple[float, float, float, float], depth: int) -> None:
        bbox = config.geographic_bbox_for_projected(projected_bounds)
        params = {"bbox": ",".join(f"{v:.8f}" for v in bbox)}
        for attempt in range(3):
            try:
                response = session.get(OSM_MAP, params=params, timeout=90)
                break
            except (requests.Timeout, requests.ConnectionError):
                if attempt == 2:
                    raise
                time.sleep(2 ** attempt)
        if response.status_code == 400 and b"too many nodes" in response.content.lower():
            if depth >= 3:
                raise RuntimeError(f"OSM area still exceeds node limit at {name}")
            left, bottom, right, top = projected_bounds
            middle_x, middle_y = (left+right)/2, (bottom+top)/2
            for suffix, child in enumerate(((left,bottom,middle_x,middle_y), (middle_x,bottom,right,middle_y), (left,middle_y,middle_x,top), (middle_x,middle_y,right,top))):
                visit(f"{name}{suffix}", child, depth+1)
            return
        response.raise_for_status()
        if not response.content.startswith(b"<?xml"):
            raise RuntimeError(f"OSM did not return XML for {name}")
        filename = f"osm_{name}.osm.gz"
        (target_dir / filename).write_bytes(gzip.compress(response.content, compresslevel=9, mtime=0))
        records.append({
            "file": filename,
            "url": response.url,
            "retrieved_at_utc": datetime.now(timezone.utc).isoformat(),
            "sha256_uncompressed": sha256(response.content),
            "bytes_uncompressed": len(response.content),
            "license": "OpenStreetMap contributors, Open Database License 1.0",
        })
        time.sleep(0.25)

    for name, bounds in _osm_quadrants(config):
        visit(name, bounds, 0)
    return records


def fetch_all(config: MapConfig, source_dir: Path, *, with_osm: bool = True) -> dict:
    source_dir.mkdir(parents=True, exist_ok=True)
    session = requests.Session()
    session.headers.update({"User-Agent": USER_AGENT})
    check_one_meter_coverage(config, session)
    dem_record = fetch_dem(config, source_dir / "usgs_3dep_2m.tif", session)
    osm_records = fetch_osm(config, source_dir, session) if with_osm else []
    manifest = {
        "map_id": config.map_id,
        "center": {"lat": config.center_lat, "lon": config.center_lon},
        "projected_crs": f"EPSG:{config.crs_epsg}",
        "dem": dem_record,
        "osm": osm_records,
        "notes": [
            "No Maxar or other noncommercial imagery is included.",
            "OSM snapshot is a separate ODbL-derived database; retain attribution and share-alike terms.",
            "The service response does not establish the source vertical datum, so game Y is local relative height only.",
        ],
    }
    (source_dir / "sources.json").write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return manifest
