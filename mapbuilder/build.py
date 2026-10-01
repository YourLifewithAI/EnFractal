"""Turn archived geographic inputs into a compact, inspectable game package."""

from __future__ import annotations

import json
import math
import random
from pathlib import Path

import numpy as np
import rasterio
from PIL import Image, ImageDraw, ImageFilter

from .fetch import sha256
from .geo import MapConfig
from .osm import build_features, write_features
from .photo import build_photo_pilot


def vertex_grid_from_dem(dem: np.ndarray) -> np.ndarray:
    """Resample pixel-centered heights onto shared tile vertices.

    Edge vertices use nearest source samples for the half-cell outer margin.
    A single grid guarantees matching tile-edge heights across every LOD.
    """
    if dem.ndim != 2 or dem.shape[0] != dem.shape[1] or dem.shape[0] < 2:
        raise ValueError("Expected a square DEM with at least 2 pixels per side")
    side = dem.shape[0]
    positions = np.clip(np.arange(side + 1, dtype=np.float32) - 0.5, 0, side - 1)
    lower = np.floor(positions).astype(np.int32)
    upper = np.minimum(lower + 1, side - 1)
    mix = (positions - lower).astype(np.float32)
    along_x = dem[:, lower] * (1 - mix)[None, :] + dem[:, upper] * mix[None, :]
    return (along_x[lower, :] * (1 - mix)[:, None] + along_x[upper, :] * mix[:, None]).astype(np.float32)


def sample_height(grid: np.ndarray, config: MapConfig, x: float, z: float) -> float:
    half = config.side_m / 2
    column = max(0.0, min(grid.shape[1] - 1.0, (x + half) / config.sample_spacing_m))
    row = max(0.0, min(grid.shape[0] - 1.0, (z + half) / config.sample_spacing_m))
    x0, y0 = int(column), int(row)
    x1, y1 = min(x0 + 1, grid.shape[1] - 1), min(y0 + 1, grid.shape[0] - 1)
    fx, fy = column - x0, row - y0
    return float((1-fy)*((1-fx)*grid[y0,x0]+fx*grid[y0,x1])+fy*((1-fx)*grid[y1,x0]+fx*grid[y1,x1]))


def _pixel(config: MapConfig, point: list[float], preview_side: int) -> tuple[int, int]:
    half = config.side_m / 2
    return round((point[0] + half) / config.side_m * (preview_side - 1)), round((point[1] + half) / config.side_m * (preview_side - 1))


def _feature_masks(config: MapConfig, features: dict, side: int = 1024) -> dict[str, Image.Image]:
    masks = {kind: Image.new("L", (side, side), 0) for kind in ("obstacles", "vegetation", "developed", "creeks")}
    drawers = {kind: ImageDraw.Draw(image) for kind, image in masks.items()}
    for building in features["buildings"]:
        drawers["obstacles"].polygon([_pixel(config, p, side) for p in building["footprint"]], fill=255)
    for road in features["roads"]:
        highway = road["tags"].get("highway", "")
        width_m = 26 if highway in ("motorway", "trunk") else (18 if highway in ("primary", "secondary") else 10)
        width_px = max(2, round((width_m + 8) / config.side_m * side))
        drawers["obstacles"].line([_pixel(config, p, side) for p in road["points"]], fill=255, width=width_px, joint="curve")
    for trail in features["trails"]:
        drawers["obstacles"].line([_pixel(config, p, side) for p in trail["points"]], fill=255, width=2, joint="curve")
    for creek in features["waterways"]:
        drawers["creeks"].line([_pixel(config, p, side) for p in creek["points"]], fill=255, width=3, joint="curve")
        drawers["obstacles"].line([_pixel(config, p, side) for p in creek["points"]], fill=255, width=3, joint="curve")
    for water in features["water_areas"]:
        drawers["obstacles"].polygon([_pixel(config, p, side) for p in water["outline"]], fill=255)
    for vegetation in features["vegetation_areas"]:
        drawers["vegetation"].polygon([_pixel(config, p, side) for p in vegetation["outline"]], fill=255)
    for developed in features["developed_areas"]:
        drawers["developed"].polygon([_pixel(config, p, side) for p in developed["outline"]], fill=255)
    masks["obstacles"] = masks["obstacles"].filter(ImageFilter.MaxFilter(5))
    masks["creeks"] = masks["creeks"].filter(ImageFilter.GaussianBlur(32))
    return masks


def generate_vegetation(config: MapConfig, grid: np.ndarray, features: dict) -> list[dict]:
    """Scatter representative trees; positions are explicitly synthetic."""
    masks = _feature_masks(config, features)
    obstacles = np.asarray(masks["obstacles"])
    vegetated = np.asarray(masks["vegetation"])
    developed = np.asarray(masks["developed"])
    creeks = np.asarray(masks["creeks"])
    randomizer = random.Random(config.vegetation_seed)
    half = config.side_m / 2
    side = obstacles.shape[0]
    trees: list[dict] = []
    for _ in range(config.vegetation_target * 50):
        if len(trees) >= config.vegetation_target:
            break
        x = randomizer.uniform(-half + 8, half - 8)
        z = randomizer.uniform(-half + 8, half - 8)
        col = min(side - 1, max(0, int((x + half) / config.side_m * side)))
        row = min(side - 1, max(0, int((z + half) / config.side_m * side)))
        if obstacles[row, col]:
            continue
        density = 0.30 + (0.52 if vegetated[row, col] else 0) + (creeks[row, col] / 255) * 0.48
        if developed[row, col]:
            density *= 0.12
        if randomizer.random() > min(density, 0.98):
            continue
        elevation = sample_height(grid, config, x, z)
        slope = math.hypot(
            sample_height(grid, config, x+2, z)-sample_height(grid, config, x-2, z),
            sample_height(grid, config, x, z+2)-sample_height(grid, config, x, z-2),
        ) / 4
        if slope > 1.6:
            continue
        trees.append({
            "x": round(x, 1), "z": round(z, 1),
            "elevation_m": round(elevation, 2),
            "height_m": round(randomizer.uniform(5.0, 12.0), 1),
            "kind": "cedar_like" if randomizer.random() < 0.58 else "oak_like",
        })
    if len(trees) < config.vegetation_target:
        raise RuntimeError(f"Only placed {len(trees)} of {config.vegetation_target} trees")
    return trees


def _hillshade(config: MapConfig, grid: np.ndarray) -> np.ndarray:
    preview_side = 1024
    samples = grid[::2, ::2][:preview_side, :preview_side]
    dz, dx = np.gradient(samples, config.sample_spacing_m * 2)
    relief = (samples - float(grid.min())) / max(1.0, float(grid.max() - grid.min()))
    slope = np.hypot(dx, dz)
    nx, ny, nz = -dx, np.ones_like(dx), -dz
    norm = np.sqrt(nx*nx + ny*ny + nz*nz)
    shade = np.clip((nx*(-0.55) + ny*0.72 + nz*(-0.42))/norm, 0.18, 1.0)
    low = np.array([61, 107, 75], dtype=np.float32)
    high = np.array([172, 159, 114], dtype=np.float32)
    color = low[None,None,:] * (1-relief[:,:,None]) + high[None,None,:] * relief[:,:,None]
    rock = np.clip((slope-0.45)/0.7, 0, 0.85)
    color = color*(1-rock[:,:,None]) + np.array([187, 174, 140], dtype=np.float32)[None,None,:]*rock[:,:,None]
    return np.uint8(np.clip(color * (0.62 + 0.53*shade[:,:,None]), 0, 255))


def write_preview(config: MapConfig, grid: np.ndarray, features: dict, target: Path) -> None:
    side = 1024
    image = Image.fromarray(_hillshade(config, grid), "RGB")
    draw = ImageDraw.Draw(image)
    for area in features["water_areas"]:
        draw.polygon([_pixel(config,p,side) for p in area["outline"]], fill=(50, 109, 140))
    for creek in features["waterways"]:
        draw.line([_pixel(config,p,side) for p in creek["points"]], fill=(57, 145, 169), width=3)
    for road in features["roads"]:
        highway = road["tags"].get("highway", "")
        width = 5 if highway in ("motorway", "trunk") else (3 if highway in ("primary", "secondary") else 1)
        draw.line([_pixel(config,p,side) for p in road["points"]], fill=(211, 195, 157), width=width, joint="curve")
    for trail in features["trails"]:
        draw.line([_pixel(config,p,side) for p in trail["points"]], fill=(215, 145, 80), width=1, joint="curve")
    for building in features["buildings"]:
        draw.polygon([_pixel(config,p,side) for p in building["footprint"]], fill=(178, 165, 147), outline=(77, 76, 75))
    center = side // 2
    draw.line((center-8,center,center+8,center), fill=(245, 53, 48), width=2)
    draw.line((center,center-8,center,center+8), fill=(245, 53, 48), width=2)
    mall = next((building for building in features["buildings"] if building["name"] == "Barton Creek Square Mall"), None)
    if mall:
        point = _pixel(config, [mall["proxy"]["x"], mall["proxy"]["z"]], side)
        draw.text((point[0]+7, point[1]-14), "Barton Creek Square Mall", fill=(255, 247, 224), stroke_width=2, stroke_fill=(35, 41, 37))
    draw.text((14, 14), "BARTON CREEK • 4.096 KM PROTOTYPE", fill=(255, 247, 224), stroke_width=2, stroke_fill=(32, 43, 38))
    draw.text((14, side-26), "USGS 3DEP relief + OSM mapped features • vegetation illustrative", fill=(255, 247, 224), stroke_width=2, stroke_fill=(32, 43, 38))
    target.parent.mkdir(parents=True, exist_ok=True)
    image.save(target, optimize=True)


def build_map(config: MapConfig, source_dir: Path, package_dir: Path, photo_evidence_dir: Path) -> dict:
    sources = json.loads((source_dir / "sources.json").read_text(encoding="utf-8"))
    dem_file = source_dir / "usgs_3dep_2m.tif"
    if sha256(dem_file.read_bytes()) != sources["dem"]["sha256"]:
        raise RuntimeError("Archived DEM differs from source manifest")
    for osm in sources["osm"]:
        import gzip
        contents = gzip.decompress((source_dir / osm["file"]).read_bytes())
        if sha256(contents) != osm["sha256_uncompressed"]:
            raise RuntimeError(f"Archived OSM snapshot differs: {osm['file']}")
    with rasterio.open(dem_file) as dataset:
        dem = dataset.read(1).astype(np.float32)
        if dataset.crs.to_epsg() != config.crs_epsg or dataset.width != config.grid_side - 1:
            raise RuntimeError("DEM no longer matches map configuration")
        if np.any(~np.isfinite(dem)):
            raise RuntimeError("Invalid DEM samples")
    grid = vertex_grid_from_dem(dem)
    features = build_features(config, source_dir)
    features["trees"] = generate_vegetation(config, grid, features)
    features["metadata"]["counts"]["trees"] = len(features["trees"])
    features["metadata"]["vegetation_source"] = "Procedural illustration constrained by mapped features and slope"

    package_dir.mkdir(parents=True, exist_ok=True)
    offset = math.floor(float(grid.min()) * 100) / 100
    scale = max(0.01, (float(grid.max()) - offset) / 65535)
    quantized = np.rint((grid - offset) / scale).astype("<u2")
    height_file = package_dir / "heights.r16"
    height_file.write_bytes(quantized.tobytes(order="C"))
    features_file = package_dir / "features.json"
    write_features(features, features_file)
    preview_file = package_dir / "preview.png"
    write_preview(config, grid, features, preview_file)
    photo_pilot = build_photo_pilot(config, grid, features, photo_evidence_dir, package_dir)
    photo_file = package_dir / "photo_pilot.json"
    photo_overlay_file = package_dir / photo_pilot["overlay_file"]

    center_height = sample_height(grid, config, 0, 0)
    manifest = {
        "format": "enfractal-map-v0",
        "map_id": config.map_id,
        "display_name": config.display_name,
        "description": config.description,
        "center_lat": config.center_lat,
        "center_lon": config.center_lon,
        "crs": f"EPSG:{config.crs_epsg}",
        "center_projected_m": [round(v, 3) for v in config.center_xy],
        "axes": "+X east, +Z south, +Y up; local origin at founder coordinate",
        "side_m": config.side_m,
        "sample_spacing_m": config.sample_spacing_m,
        "grid_side": config.grid_side,
        "tile_side_m": config.tile_side_m,
        "height_encoding": "uint16 little-endian row-major, north row first; metres = offset + sample * scale",
        "height_offset_m": offset,
        "height_scale_m": scale,
        "height_origin_m": round(center_height, 4),
        "height_min_m": round(float(grid.min()), 4),
        "height_max_m": round(float(grid.max()), 4),
        "heights_file": height_file.name,
        "heights_sha256": sha256(height_file.read_bytes()),
        "features_file": features_file.name,
        "features_sha256": sha256(features_file.read_bytes()),
        "preview_file": preview_file.name,
        "preview_sha256": sha256(preview_file.read_bytes()),
        "photo_pilot_file": photo_file.name,
        "photo_pilot_sha256": sha256(photo_file.read_bytes()),
        "photo_overlay_file": photo_overlay_file.name,
        "photo_overlay_sha256": sha256(photo_overlay_file.read_bytes()),
        "feature_counts": features["metadata"]["counts"],
        "source_manifest": (source_dir / "sources.json").as_posix(),
        "rights": [
            "Terrain: USGS 3DEP; public-domain USGS data, cite source.",
            "Mapped features: © OpenStreetMap contributors; ODbL 1.0. Feature database is distributed separately under ODbL.",
            "Representative trees and untagged building heights are generated, not surveyed.",
            "Photo pilot references: Julia Duffy (public domain) and Larry D. Moore (CC BY 4.0); see game/CREDITS.md.",
            "Photo pilot parking markings derive from OSM aisle centerlines and remain attributed to OSM contributors.",
        ],
        "limitations": [
            "Elevation comes from a dynamic 3DEP mosaic sampled at 2 m; exact native source tile and vertical datum not identified.",
            "Relative local Y is suitable for prototype rendering, not yet for global geodetic interchange.",
            "Road centerlines do not model lane widths, bridge decks, tunnels or collision-grade pavement.",
            "No commercial satellite imagery is packaged.",
            "Photo pilot material colors and rock/facade/parking details are illustrative, not a surveyed reconstruction.",
        ],
    }
    (package_dir / "manifest.json").write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return manifest
