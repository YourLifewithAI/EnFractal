"""Reproducible Pfluger source foundation; no surveyed bridge or photo claims.

Run with ``python -m mapbuilder.pfluger build`` (or ``fetch`` / ``verify``).
The frozen Barton builder and runtime package are deliberately independent.
"""
from __future__ import annotations

import argparse
import gzip
import heapq
import importlib.metadata
import json
import math
from datetime import datetime, timezone
from pathlib import Path
import xml.etree.ElementTree as ET

import numpy as np
from PIL import Image, ImageDraw
import rasterio
from rasterio.features import rasterize
from rasterio.transform import Affine
import requests
from shapely import set_precision
from shapely.geometry import LineString, Point, Polygon, box, mapping
from shapely.ops import nearest_points, polygonize, substring, transform, unary_union

from .build import encode_height_grid, sample_height, vertex_grid_from_dem
from .contract import verify_spatial_manifest
from .fetch import fetch_all, sha256
from .geo import MapConfig
from .osm import _building_proxy, _parts, build_features, parse_snapshot
from .terrain import read_complete_dem
from .tiles import GEOGRAPHIC_TILE_SCHEME, LOCAL_TILE_SCHEME
from .verify import verify_height_payload

ROOT = Path(__file__).resolve().parents[1]
DEFAULT = ROOT / "maps/pfluger_district"
BOUNDARIES = {
    "north": {"West 6th Street"},
    "east": {"Congress Avenue", "South Congress Avenue"},
    "south": {"Barton Springs Road"},
    "west": {"North Mopac Expressway", "South Mopac Expressway"},
}
WATER_RELATION = 32671
DEPENDENCIES = ("numpy", "Pillow", "pyproj", "rasterio", "requests", "shapely")


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")


def coords(line) -> list[list[float]]:
    return [[round(x, 6), round(z, 6)] for x, z in line.coords]


def source_records(source_dir: Path) -> list[dict]:
    result = json.loads((source_dir / "sources.json").read_text(encoding="utf-8"))["osm"]
    result += json.loads((source_dir / "supplemental_sources.json").read_text(encoding="utf-8"))["osm"]
    return result


def validate_sources(config: MapConfig, source_dir: Path) -> dict:
    manifest = json.loads((source_dir / "sources.json").read_text(encoding="utf-8"))
    if manifest["map_id"] != config.map_id:
        raise ValueError("Source map ID differs from config")
    hashes = {"usgs_3dep_2m.tif": manifest["dem"]["sha256"]}
    if sha256((source_dir / "usgs_3dep_2m.tif").read_bytes()) != hashes["usgs_3dep_2m.tif"]:
        raise ValueError("Source DEM hash mismatch")
    for record in source_records(source_dir):
        if Path(record["file"]).name != record["file"]:
            raise ValueError("Source filename must be local")
        digest = sha256(gzip.decompress((source_dir / record["file"]).read_bytes()))
        if digest != record["sha256_uncompressed"]:
            raise ValueError(f"Source OSM hash mismatch: {record['file']}")
        hashes[record["file"]] = digest
        hashes[record["file"] + ":gzip"] = sha256((source_dir / record["file"]).read_bytes())
    declared = {record["file"] for record in source_records(source_dir)}
    if declared != {path.name for path in source_dir.glob("*.osm.gz")}:
        raise ValueError("OSM snapshot files differ from source manifest")
    for name in ("sources.json", "supplemental_sources.json"):
        hashes[name] = sha256((source_dir / name).read_bytes())
    return hashes


def fetch_sources(config: MapConfig, source_dir: Path) -> None:
    if source_dir.exists() and any(source_dir.iterdir()):
        raise FileExistsError("Refuse to replace an archived snapshot; select a new --sources directory")
    fetch_all(config, source_dir, with_osm=True)
    # The shared legacy fetcher uses platform-native newlines. New snapshots
    # use canonical LF metadata so source pins survive a Windows checkout.
    primary_manifest = source_dir / "sources.json"
    write_json(primary_manifest, json.loads(primary_manifest.read_text(encoding="utf-8")))
    url = f"https://api.openstreetmap.org/api/0.6/relation/{WATER_RELATION}/full"
    response = requests.get(url, timeout=120, headers={"User-Agent": "EnFractal-Pfluger-source-prep/1.0"})
    response.raise_for_status()
    filename = "lady_bird_lake_relation.osm.gz"
    (source_dir / filename).write_bytes(gzip.compress(response.content, mtime=0))
    write_json(source_dir / "supplemental_sources.json", {"osm": [{
        "osm_type": "relation", "osm_id": WATER_RELATION, "file": filename,
        "url": url, "retrieved_at_utc": datetime.now(timezone.utc).isoformat(),
        "sha256_uncompressed": sha256(response.content), "bytes_uncompressed": len(response.content),
        "license": "Open Database License 1.0", "attribution": "\u00a9 OpenStreetMap contributors",
    }]})


def trace_boundary(config: MapConfig, source_dir: Path) -> tuple[Polygon, dict]:
    nodes, ways = parse_snapshot(source_dir)
    groups, evidence = {}, {}
    for side, names in BOUNDARIES.items():
        evidence[side] = {}
        for osm_id, way in sorted(ways.items()):
            tags = way["tags"]
            if tags.get("name") not in names or tags.get("highway") in {None, "footway", "cycleway", "steps", "path"}:
                continue
            if not all(node in nodes for node in way["nodes"]):
                raise ValueError("Incomplete boundary way")
            evidence[side][osm_id] = LineString([config.to_local(*nodes[node]) for node in way["nodes"]])
        if not evidence[side]:
            raise ValueError(f"Missing boundary family: {side}")
        groups[side] = unary_union(list(evidence[side].values()))
    # At the western terminus Barton Springs meets the service road, not the
    # expressway centreline. Explicitly bridge this small cartographic gap.
    a, b = nearest_points(groups["south"], groups["west"])
    distance = a.distance(b)
    if distance > 30:
        raise ValueError("Boundary connector exceeds the reviewed 30 m allowance")
    dx, dz = b.x - a.x, b.y - a.y
    connector = LineString([(a.x - dx * .01, a.y - dz * .01), (b.x + dx * .01, b.y + dz * .01)])
    # 1 mm noding precision prevents floating point gaps at planar crossings;
    # it is a computational tolerance and makes no positional-accuracy claim.
    lines = unary_union([set_precision(line, .001) for line in [*groups.values(), connector]])
    anchor = Point(config.to_local(-97.756, 30.2655))
    candidates = [p for p in polygonize(lines) if p.covers(anchor)]
    if len(candidates) != 1:
        raise ValueError("Named boundaries do not enclose exactly one Pfluger district")
    # Interior traffic islands are part of the district; do not create holes.
    polygon = Polygon(candidates[0].exterior)
    if not polygon.is_valid or not 2_000_000 < polygon.area < 5_000_000:
        raise ValueError("Unexpected traced district area")
    if not box(-config.side_m / 2, -config.side_m / 2, config.side_m / 2, config.side_m / 2).covers(polygon):
        raise ValueError("District exceeds source envelope")
    side_evidence = {}
    for side, lines_by_id in evidence.items():
        ids = [osm_id for osm_id, line in lines_by_id.items() if polygon.boundary.buffer(.01).intersection(line).length > 1]
        if not ids:
            raise ValueError(f"Traced polygon omits {side}")
        side_evidence[side] = {"names": sorted(BOUNDARIES[side]), "osm_way_ids": ids}
    geographic = transform(config.local_to_geographic, polygon)
    audit = {
        "schema": "enfractal-district-boundary-v1", "status": "reviewable_source_trace",
        "area_m2": round(polygon.area, 2), "area_km2": round(polygon.area / 1e6, 4),
        "perimeter_m": round(polygon.length, 2), "bounds_local_m": list(polygon.bounds),
        "bounding_span_m": [round(polygon.bounds[2] - polygon.bounds[0], 2), round(polygon.bounds[3] - polygon.bounds[1], 2)],
        "outline_local_xz_m": coords(polygon.exterior), "geojson": mapping(geographic),
        "boundary_sources": side_evidence,
        "connector": {"between": ["south", "west"], "length_m": round(distance, 3), "points_local_xz_m": coords(LineString([a, b])), "reason": "Extend Barton Springs service-road junction to nearest mapped expressway carriageway centreline"},
        "convention": "Planar named road-centreline trace; paired carriageways select the enclosed face containing Pfluger Bridge; interior traffic islands included",
        "accuracy": "Projected polygon computation, not cadastral or survey accuracy; bridge crossings are planar boundary intersections, not traversable junctions",
    }
    return polygon, audit


def landing_route(config: MapConfig, source_dir: Path, district: Polygon) -> dict:
    nodes, ways = parse_snapshot(source_dir)
    graph: dict[int, list] = {}
    for osm_id, way in sorted(ways.items()):
        if way["tags"].get("highway") not in {"cycleway", "footway", "path"}:
            continue
        for a, b in zip(way["nodes"], way["nodes"][1:]):
            if a not in nodes or b not in nodes:
                continue
            pa, pb = Point(config.to_local(*nodes[a])), Point(config.to_local(*nodes[b]))
            if district.covers(pa) and district.covers(pb):
                length = pa.distance(pb)
                graph.setdefault(a, []).append((b, length, osm_id))
                graph.setdefault(b, []).append((a, length, osm_id))
    def nearest(lon, lat):
        point = Point(config.to_local(lon, lat))
        return min(graph, key=lambda node: point.distance(Point(config.to_local(*nodes[node]))))
    start, end = nearest(-97.75495, 30.26708), nearest(-97.75616, 30.26511)
    distances, previous, queue = {start: 0.0}, {}, [(0.0, start)]
    while queue:
        cost, node = heapq.heappop(queue)
        if cost != distances[node]:
            continue
        if node == end:
            break
        for other, length, osm_id in graph[node]:
            candidate = cost + length
            if candidate < distances.get(other, math.inf):
                distances[other], previous[other] = candidate, (node, osm_id)
                heapq.heappush(queue, (candidate, other))
    if end not in previous:
        raise ValueError("Mapped bridge approaches do not connect")
    sequence, ids = [end], []
    while sequence[-1] != start:
        prior, osm_id = previous[sequence[-1]]
        sequence.append(prior)
        ids.append(osm_id)
    sequence.reverse()
    ids.reverse()
    line = LineString([config.to_local(*nodes[node]) for node in sequence])
    if line.length < 150:
        raise ValueError("Candidate route is shorter than 150 m")
    route = substring(line, 0, 150)
    used, total = [], 0.0
    for a, b, osm_id in zip(sequence, sequence[1:], ids):
        if total >= 150:
            break
        used.append(osm_id)
        total += Point(config.to_local(*nodes[a])).distance(Point(config.to_local(*nodes[b])))
    return {
        "schema": "enfractal-landing-route-v1", "name": "North Pfluger approach to bridge, 150 m horizontal candidate",
        "length_horizontal_m": round(route.length, 3), "points_local_xz_m": coords(route),
        "geojson": mapping(transform(config.local_to_geographic, route)),
        "source_osm_way_ids": sorted(set(used)), "start_osm_node_id": start,
        "walkability_verified": False, "vertical_status": "requires_bridge_deck_and_ramp_audit",
        "player_height_m": .30,
        "limitations": ["Horizontal source alignment only; never ground bridge vertices directly onto bare-earth terrain", "OSM paths do not establish railing clearances, surface continuity, gradient, deck thickness, or collision geometry", "Route excludes mapped steps but has not been audited for small-avatar accessibility"],
    }


def lake_geometry(config: MapConfig, source_dir: Path) -> tuple[object, dict]:
    root = ET.fromstring(gzip.decompress((source_dir / "lady_bird_lake_relation.osm.gz").read_bytes()))
    nodes = {int(e.attrib["id"]): config.to_local(float(e.attrib["lon"]), float(e.attrib["lat"])) for e in root.findall("node")}
    ways = {int(e.attrib["id"]): [int(n.attrib["ref"]) for n in e.findall("nd")] for e in root.findall("way")}
    relation = next(e for e in root.findall("relation") if int(e.attrib["id"]) == WATER_RELATION)
    roles = {"outer": [], "inner": []}
    for member in relation.findall("member"):
        role, osm_id = member.attrib["role"], int(member.attrib["ref"])
        if member.attrib["type"] != "way" or role not in roles or osm_id not in ways:
            raise ValueError("Incomplete or unsupported lake relation member")
        refs = ways[osm_id]
        if any(ref not in nodes for ref in refs):
            raise ValueError("Incomplete lake node references")
        roles[role].append(LineString([nodes[ref] for ref in refs]))
    outer = unary_union(list(polygonize(unary_union(roles["outer"]))))
    inner = unary_union(list(polygonize(unary_union(roles["inner"]))))
    if outer.is_empty or not outer.is_valid:
        raise ValueError("Lake outer ring does not close")
    # Verify that every member is represented in a closed ring; no silent loss.
    for role, geometry in (("outer", outer), ("inner", inner)):
        if unary_union(roles[role]).difference(geometry.boundary.buffer(.001)).length > .01:
            raise ValueError(f"Unclosed {role} lake member")
    return outer.difference(inner), {"relation_id": WATER_RELATION, "version": relation.attrib["version"], "member_counts": {k: len(v) for k, v in roles.items()}, "complete": True}


def district_features(config: MapConfig, source_dir: Path, district: Polygon, lake) -> dict:
    features = build_features(config, source_dir)
    for kind, records in list(features.items()):
        if not isinstance(records, list):
            continue
        clipped = []
        for record in records:
            field = "points" if "points" in record else ("footprint" if "footprint" in record else "outline")
            geometry = LineString(record[field]) if field == "points" else Polygon(record[field])
            if not geometry.is_valid:
                geometry = geometry.buffer(0)
            for index, part in enumerate(_parts(geometry.intersection(district), "LineString" if field == "points" else "Polygon")):
                if part.length < .1 or (field != "points" and part.area < .1):
                    continue
                item = {**record, "feature_id": record["feature_id"] + f":district:{index}", field: coords(part if field == "points" else part.exterior)}
                if field != "points":
                    item["holes"] = [coords(ring) for ring in part.interiors]
                if kind == "buildings":
                    item["proxy"] = _building_proxy(part)
                    # Austin includes towers beyond the legacy Barton range.
                    # Preserve explicit metre tags through a wider sanity limit.
                    raw = item["tags"].get("height", "").strip()
                    try:
                        value = float(raw.lower().removesuffix("m").strip())
                    except ValueError:
                        value = 0
                    if 150 < value <= 1000:
                        item.update(height_m=value, height_is_estimate=False, height_evidence={"basis": "osm_height_tag", "source_tag": "height", "raw_value": raw, "qualification": "OSM contributor report; not independently surveyed"})
                clipped.append(item)
        features[kind] = clipped
    digest = next(r["sha256_uncompressed"] for r in source_records(source_dir) if r.get("osm_id") == WATER_RELATION)
    for index, part in enumerate(_parts(lake.intersection(district), "Polygon")):
        features["water_areas"].append({
            "feature_id": f"{config.map_id}:osm:relation:{WATER_RELATION}:water_areas:{index}",
            "osm_type": "relation", "osm_id": WATER_RELATION, "name": "Lady Bird Lake", "tags": {"natural": "water", "water": "reservoir"},
            "outline": coords(part.exterior), "holes": [coords(ring) for ring in part.interiors],
            "source_snapshot_sha256": digest, "vertical_status": "water_surface_elevation_not_verified",
        })
    features["trees"] = []
    features["metadata"]["counts"] = {kind: len(value) for kind, value in features.items() if isinstance(value, list)}
    features["metadata"]["feature_id_scheme"] = "Existing OSM way ID plus district clipping index; lake relation IDs explicitly identify relation geometry"
    features["metadata"]["limitations"] += ["No individual tree inventory or species is represented", "Multipolygon relation support is limited to the explicitly audited Lady Bird Lake relation; other relation-only features may be absent", "Mapped roads/trails retain legacy 1-2 m simplification: source context, not small-avatar collision detail", "Polygon holes are retained in data; consumers must implement hole-aware rendering"]
    return features


def preview(config, dem, features, district, route, target):
    side = 1200
    # Contextual terrain tint, not an aerial image or artistic reconstruction.
    shade = np.clip((dem - 125) / 75, 0, 1)
    rgb = np.stack([159 + shade * 35, 175 + shade * 28, 137 + shade * 35], axis=-1).astype(np.uint8)
    base = Image.fromarray(rgb).resize((side, side))
    image = Image.new("RGB", (side, side + 110), "#f4f1e9")
    image.paste(base, (0, 60)); draw = ImageDraw.Draw(image)
    def xy(p): return (round((p[0] / config.side_m + .5) * side), 60 + round((p[1] / config.side_m + .5) * side))
    for water in features["water_areas"]:
        draw.polygon([xy(p) for p in water["outline"]], fill="#80adb4")
        for ring in water.get("holes", []): draw.polygon([xy(p) for p in ring], fill="#bac69b")
    for kind, color, width in (("roads", "#e8dfc7", 2), ("trails", "#bc7d56", 1)):
        for line in features[kind]: draw.line([xy(p) for p in line["points"]], fill=color, width=width)
    for building in features["buildings"]:
        draw.polygon([xy(p) for p in building["footprint"]], fill="#8b8278")
    draw.line([xy(p) for p in district.exterior.coords], fill="#5935a0", width=5)
    draw.line([xy(p) for p in route["points_local_xz_m"]], fill="#ef3340", width=6)
    point = xy(route["points_local_xz_m"][0]); draw.ellipse((point[0]-5,point[1]-5,point[0]+5,point[1]+5),fill="white",outline="#ef3340",width=2)
    draw.text((18, 12), "PFLUGER / LADY BIRD LAKE - SOURCE FOUNDATION", fill="#252c30")
    draw.text((18, 32), "Purple: road-traced district | Red: 150 m bridge approach candidate | Terrain: USGS 3DEP; map: \u00a9 OpenStreetMap contributors / ODbL", fill="#252c30")
    draw.text((18, 80), "N (grid)", fill="#252c30")
    draw.line([(38,130),(38,100),(32,109),(38,100),(44,109)], fill="#252c30", width=2)
    draw.line([(18,side+30),(18+round(500/config.side_m*side),side+30)], fill="#252c30", width=4)
    draw.text((18,side+10), "500 m", fill="#252c30")
    draw.text((18, side+70), f"4096 m source envelope; district ~{district.area/1e6:.2f} km2. Bridge heights, fine collision, tree structure and photographic art reconstruction remain unaudited.", fill="#252c30")
    image.save(target)


def build_package(config_path: Path, source_dir: Path, package_dir: Path) -> dict:
    config = MapConfig.load(config_path)
    hashes = validate_sources(config, source_dir)
    with rasterio.open(source_dir / "usgs_3dep_2m.tif") as dataset:
        dem = read_complete_dem(dataset, config)
    district, boundary = trace_boundary(config, source_dir)
    route = landing_route(config, source_dir, district)
    lake, lake_audit = lake_geometry(config, source_dir)
    features = district_features(config, source_dir, district, lake)
    grid = vertex_grid_from_dem(dem)
    payload, offset, scale = encode_height_grid(grid)
    package_dir.mkdir(parents=True, exist_ok=True)
    (package_dir / "heights.r16").write_bytes(payload)
    write_json(package_dir / "features.json", features)
    write_json(package_dir / "district_boundary.json", boundary)
    write_json(package_dir / "landing_route.json", route)
    preview(config, dem, features, district, route, package_dir / "preview.png")
    mask = rasterize([(district, 1)], out_shape=dem.shape, transform=Affine(config.sample_spacing_m, 0, -config.side_m/2, 0, config.sample_spacing_m, -config.side_m/2), dtype="uint8").astype(bool)
    row, column = np.unravel_index(np.argmin(dem), dem.shape)
    min_point = [(-config.side_m/2 + (column+.5)*config.sample_spacing_m), (-config.side_m/2 + (row+.5)*config.sample_spacing_m)]
    audit = {
        "schema": "enfractal-pfluger-source-audit-v1", "status": "source_foundation_only",
        "district_area_m2": boundary["area_m2"], "route_length_horizontal_m": route["length_horizontal_m"],
        "lake_relation": lake_audit, "feature_counts": features["metadata"]["counts"],
        "terrain": {"valid_fraction": 1.0, "source_sample_spacing_m": config.sample_spacing_m,
            "district_pixel_center_elevation_range_m": [float(dem[mask].min()), float(dem[mask].max())],
            "envelope_elevation_range_m": [float(dem.min()), float(dem.max())],
            "envelope_lowest_sample_local_xz_m": min_point, "envelope_lowest_sample_inside_district": bool(mask[row,column]),
            "unresolved": "Envelope low outlier preserved without correction; vertical datum and exact native source tiles unconfirmed; bare earth cannot establish bridge deck geometry"},
        "pending_gates": ["Review traced polygon and western connector convention", "Reconcile lake surface and hydroflattening with terrain", "Survey/photographic evidence for bridge deck, ramps, underpasses, railings and clearances", "Permitted photos and source-specific rights registry before any imagery-derived redistribution", "Ground-level/detail reconstruction and collision appropriate to 0.30 m avatar", "Terrain source/datum and outlier review before general district traversal"],
        "photos_included": False, "city_of_austin_layers_included": False, "collision_validated": False,
    }
    write_json(package_dir / "audit.json", audit)
    manifest = {
        "format": "enfractal-map-v0", "map_id": config.map_id, "display_name": config.display_name, "description": config.description,
        "package_status": "source_foundation_only", "player_height_m": .30,
        "center_lat": config.center_lat, "center_lon": config.center_lon, "center_projected_m": [round(x,3) for x in config.center_xy],
        "crs": f"EPSG:{config.crs_epsg}", "axes": "+X east, +Z south, +Y up; local origin at founder coordinate",
        "coordinate_qualification": "Projected grid metres, not true ENU; source envelope is not gameplay boundary",
        "side_m": config.side_m, "sample_spacing_m": config.sample_spacing_m, "grid_side": config.grid_side, "tile_side_m": config.tile_side_m,
        "height_encoding": "uint16 little-endian row-major, north row first; metres = offset + sample * scale",
        "height_offset_m": offset, "height_scale_m": scale, "height_origin_m": round(sample_height(grid,config,0,0),4),
        "height_min_m": round(float(grid.min()),4), "height_max_m": round(float(grid.max()),4),
        "feature_counts": features["metadata"]["counts"], "source_hashes": hashes,
        "config_sha256": sha256(config_path.read_bytes()),
        "builder_hash_encoding": "UTF-8 source text with CRLF normalized to LF",
        "builder_files_sha256": {str(path.relative_to(ROOT)).replace('\\','/'): sha256(path.read_text(encoding="utf-8").encode("utf-8")) for path in sorted((ROOT/'mapbuilder').glob('*.py'))},
        "dependencies": {name: importlib.metadata.version(name) for name in DEPENDENCIES},
        "tile_addressing": {"local": LOCAL_TILE_SCHEME, "geographic": GEOGRAPHIC_TILE_SCHEME},
        "rights": ["Terrain: USGS 3DEP, public-domain USGS data; retain source acknowledgement", "OSM source and derived feature/boundary/route databases: Open Database License 1.0; attribution: \u00a9 OpenStreetMap contributors", "No imagery or third-party Austin layers redistributed; listed candidates are metadata only"],
        "limitations": audit["pending_gates"],
    }
    for key, name in (("heights","heights.r16"),("features","features.json"),("preview","preview.png"),("district_boundary","district_boundary.json"),("landing_route","landing_route.json"),("audit","audit.json")):
        manifest[key+"_file"], manifest[key+"_sha256"] = name, sha256((package_dir/name).read_bytes())
    write_json(package_dir / "manifest.json", manifest)
    return verify_package(config_path, source_dir, package_dir)


def verify_package(config_path: Path, source_dir: Path, package_dir: Path) -> dict:
    config = MapConfig.load(config_path)
    manifest = json.loads((package_dir / "manifest.json").read_text(encoding="utf-8"))
    if validate_sources(config, source_dir) != manifest["source_hashes"]:
        raise ValueError("Source pin mismatch")
    if sha256(config_path.read_bytes()) != manifest["config_sha256"]:
        raise ValueError("Config pin mismatch")
    verify_spatial_manifest(config, manifest)
    for key in ("heights","features","preview","district_boundary","landing_route","audit"):
        if sha256((package_dir/manifest[key+"_file"]).read_bytes()) != manifest[key+"_sha256"]:
            raise ValueError(f"Package hash mismatch: {key}")
    with rasterio.open(source_dir / "usgs_3dep_2m.tif") as raster:
        dem = read_complete_dem(raster, config)
    verify_height_payload(config, manifest, package_dir, dem)
    boundary = json.loads((package_dir / "district_boundary.json").read_text(encoding="utf-8"))
    district = Polygon(boundary["outline_local_xz_m"])
    traced, _ = trace_boundary(config, source_dir)
    if district.symmetric_difference(traced).area > .02 or abs(district.area-boundary["area_m2"]) > .02:
        raise ValueError("District differs from archived road trace")
    route = json.loads((package_dir / "landing_route.json").read_text(encoding="utf-8"))
    line = LineString(route["points_local_xz_m"])
    if not 100 <= line.length <= 200 or not district.buffer(.001).covers(line):
        raise ValueError("Landing route must be 100-200 m inside the district")
    if abs(line.length - route["length_horizontal_m"]) > .001 or json.dumps(route, sort_keys=True) != json.dumps(landing_route(config, source_dir, traced), sort_keys=True):
        raise ValueError("Route differs from archived bridge approach")
    features = json.loads((package_dir / "features.json").read_text(encoding="utf-8"))
    for kind, count in manifest["feature_counts"].items():
        if len(features[kind]) != count:
            raise ValueError(f"Feature count mismatch: {kind}")
        for item in features[kind]:
            field = "points" if "points" in item else ("footprint" if "footprint" in item else "outline")
            geometry = LineString(item[field]) if field == "points" else Polygon(item[field], item.get("holes", []))
            if not geometry.is_valid or not district.buffer(.001).covers(geometry):
                raise ValueError(f"Invalid/out-of-district feature: {item['feature_id']}")
    if not any(x.get("osm_type") == "relation" and x.get("osm_id") == WATER_RELATION for x in features["water_areas"]):
        raise ValueError("Lake relation missing")
    if manifest["player_height_m"] != .30 or manifest["package_status"] != "source_foundation_only":
        raise ValueError("Source-foundation contract changed")
    return {"status": "verified_source_foundation", "map_id": config.map_id, "district_area_km2": boundary["area_km2"], "route_length_horizontal_m": route["length_horizontal_m"], "feature_counts": manifest["feature_counts"], "collision_validated": False}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("fetch", "build", "verify"))
    parser.add_argument("--config", type=Path, default=DEFAULT/"config.json")
    parser.add_argument("--sources", type=Path, default=DEFAULT/"sources/2026-10-02")
    parser.add_argument("--package", type=Path, default=DEFAULT/"package")
    args = parser.parse_args()
    if args.command == "fetch":
        fetch_sources(MapConfig.load(args.config), args.sources)
        print("New source snapshot archived; review its metadata before building.")
    else:
        operation = build_package if args.command == "build" else verify_package
        print(json.dumps(operation(args.config,args.sources,args.package),indent=2))


if __name__ == "__main__":
    main()
