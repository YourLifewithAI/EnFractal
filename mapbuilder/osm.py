"""Convert a bounded OSM snapshot into separate, attributed map features."""

from __future__ import annotations

import gzip
import json
import math
import xml.etree.ElementTree as ET
from pathlib import Path

from shapely.geometry import LineString, Polygon, box

from .geo import MapConfig


TRAIL_CLASSES = {"path", "footway", "cycleway", "bridleway", "steps", "pedestrian", "track"}
VEGETATION_TAGS = {
    ("natural", "wood"), ("natural", "scrub"), ("natural", "grassland"),
    ("landuse", "forest"), ("landuse", "meadow"), ("landuse", "grass"),
    ("leisure", "park"), ("leisure", "nature_reserve"),
}
DEVELOPED_TAGS = {
    ("landuse", "retail"), ("landuse", "commercial"), ("landuse", "industrial"),
    ("amenity", "parking"),
}
KEPT_TAGS = {
    "name", "highway", "building", "building:levels", "height", "bridge", "tunnel",
    "layer", "surface", "waterway", "natural", "water", "landuse", "leisure", "oneway", "amenity",
}


def parse_snapshot(source_dir: Path) -> tuple[dict[int, tuple[float, float]], dict[int, dict]]:
    nodes: dict[int, tuple[float, float]] = {}
    ways: dict[int, dict] = {}
    files = sorted(source_dir.glob("osm_*.osm.gz"))
    if not files:
        raise FileNotFoundError("No OSM snapshot files; run fetch first")
    for path in files:
        with gzip.open(path, "rb") as stream:
            for _, element in ET.iterparse(stream, events=("end",)):
                if element.tag == "node":
                    nodes[int(element.attrib["id"])] = (float(element.attrib["lon"]), float(element.attrib["lat"]))
                    element.clear()
                elif element.tag == "way":
                    tags = {child.attrib["k"]: child.attrib["v"] for child in element.findall("tag") if child.attrib["k"] in KEPT_TAGS}
                    if any(key in tags for key in ("highway", "building", "waterway", "natural", "landuse", "leisure", "amenity")):
                        ways[int(element.attrib["id"])] = {
                            "nodes": [int(child.attrib["ref"]) for child in element.findall("nd")],
                            "tags": tags,
                        }
                    element.clear()
    return nodes, ways


def _coords(geometry, geometry_type: str) -> list[list[float]]:
    if geometry_type == "polygon":
        raw = list(geometry.exterior.coords)
    else:
        raw = list(geometry.coords)
    return [[round(x, 2), round(z, 2)] for x, z in raw]


def _parts(geometry, kind: str):
    if geometry.is_empty:
        return
    if geometry.geom_type == kind:
        yield geometry
    elif hasattr(geometry, "geoms"):
        for member in geometry.geoms:
            yield from _parts(member, kind)


def _height_m(tags: dict) -> float:
    raw = tags.get("height", "").strip().lower().removesuffix("m").strip()
    try:
        value = float(raw)
        if 2 <= value <= 150:
            return value
    except ValueError:
        pass
    try:
        levels = float(tags.get("building:levels", ""))
        if 1 <= levels <= 50:
            return round(levels * 3.2, 1)
    except ValueError:
        pass
    if tags.get("building") in ("retail", "commercial", "supermarket", "mall"):
        return 10.0
    if tags.get("building") in ("garage", "shed", "carport"):
        return 3.5
    return 7.0


def _building_proxy(polygon: Polygon) -> dict:
    rectangle = polygon.minimum_rotated_rectangle
    corners = list(rectangle.exterior.coords)[:4]
    edges = []
    for i in range(2):
        dx, dz = corners[i + 1][0] - corners[i][0], corners[i + 1][1] - corners[i][1]
        edges.append((math.hypot(dx, dz), dx, dz))
    length, dx, dz = max(edges, key=lambda edge: edge[0])
    width = min(edge[0] for edge in edges)
    center = polygon.centroid
    return {
        "x": round(center.x, 2),
        "z": round(center.y, 2),
        "width_m": round(max(width, 1.0), 2),
        "length_m": round(max(length, 1.0), 2),
        "yaw_rad": round(math.atan2(dx, dz), 5),
    }


def build_features(config: MapConfig, source_dir: Path) -> dict:
    nodes, ways = parse_snapshot(source_dir)
    half = config.side_m / 2
    bounds = box(-half, -half, half, half)
    features: dict[str, list] = {key: [] for key in ("roads", "trails", "waterways", "water_areas", "vegetation_areas", "developed_areas", "buildings")}
    missing_refs = 0
    for osm_id, way in sorted(ways.items()):
        tags = way["tags"]
        refs = way["nodes"]
        if len(refs) < 2 or any(ref not in nodes for ref in refs):
            missing_refs += 1
            continue
        points = [config.to_local(*nodes[ref]) for ref in refs]
        base = {"osm_id": osm_id, "name": tags.get("name", ""), "tags": tags}

        if "highway" in tags or "waterway" in tags:
            line = LineString(points)
            if not line.is_valid or line.length < 1:
                continue
            line = line.intersection(bounds)
            target = "waterways" if "waterway" in tags else ("trails" if tags["highway"] in TRAIL_CLASSES else "roads")
            tolerance = 1.0 if target == "trails" else 2.0
            for part in _parts(line, "LineString"):
                part = part.simplify(tolerance, preserve_topology=True)
                if part.length >= 2:
                    features[target].append({**base, "points": _coords(part, "line")})

        if refs[0] != refs[-1] or len(refs) < 4:
            continue
        polygon = Polygon(points)
        if not polygon.is_valid:
            polygon = polygon.buffer(0)
        if polygon.is_empty:
            continue
        polygon = polygon.intersection(bounds)
        if tags.get("building", "no") != "no":
            for part in _parts(polygon, "Polygon"):
                if part.area >= 8:
                    features["buildings"].append({
                        **base,
                        "footprint": _coords(part.simplify(0.5, preserve_topology=True), "polygon"),
                        "proxy": _building_proxy(part),
                        "height_m": _height_m(tags),
                        "height_is_estimate": not ("height" in tags or "building:levels" in tags),
                    })
        if tags.get("natural") == "water" or "water" in tags:
            for part in _parts(polygon, "Polygon"):
                if part.area >= 10:
                    features["water_areas"].append({**base, "outline": _coords(part.simplify(1.0, preserve_topology=True), "polygon")})
        if any((key, value) in VEGETATION_TAGS for key, value in tags.items()):
            for part in _parts(polygon, "Polygon"):
                if part.area >= 50:
                    features["vegetation_areas"].append({**base, "outline": _coords(part.simplify(2.0, preserve_topology=True), "polygon")})
        if any((key, value) in DEVELOPED_TAGS for key, value in tags.items()):
            for part in _parts(polygon, "Polygon"):
                if part.area >= 50:
                    features["developed_areas"].append({**base, "outline": _coords(part.simplify(2.0, preserve_topology=True), "polygon")})

    features["metadata"] = {
        "source": "OpenStreetMap contributor snapshot",
        "license": "Open Database License 1.0",
        "attribution": "© OpenStreetMap contributors",
        "license_url": "https://www.openstreetmap.org/copyright",
        "projected_coordinate_units": "local metres; +X east, +Z south",
        "unresolved_way_references": missing_refs,
        "counts": {key: len(value) for key, value in features.items() if isinstance(value, list)},
        "limitations": [
            "Mapped geometry and tags may be incomplete or out of date.",
            "Building heights without source tags are illustrative estimates.",
            "Road and trail lines are centerlines, not measured widths or overpass elevations.",
        ],
    }
    return features


def write_features(features: dict, target: Path) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(features, separators=(",", ":"), ensure_ascii=False) + "\n", encoding="utf-8")
