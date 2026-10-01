"""Curated photo evidence -> compact, deterministic rendering hints.

The reference photographs are *not* painted onto geometry or loaded by the
game. Their sampled color ranges guide a deliberately illustrative pilot in
two small parts of the existing USGS/OSM map.
"""

from __future__ import annotations

import hashlib
import json
import math
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from .geo import MapConfig


OVERLAY_SIDE = 1024
SUPPORTED_LICENSES = {"Public domain; author-dedicated", "CC BY 4.0"}


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _sample_rect(image: np.ndarray, box: list[int]) -> list[int]:
    if len(box) != 4:
        raise ValueError("Photo sample rectangles need four coordinates")
    left, top, right, bottom = box
    if not (0 <= left < right <= image.shape[1] and 0 <= top < bottom <= image.shape[0]):
        raise ValueError(f"Photo sample rectangle outside image: {box}")
    pixels = image[top:bottom, left:right, :3].reshape(-1, 3)
    return [int(round(value)) for value in np.median(pixels, axis=0)]


def _mix(color: list[int], target: tuple[int, int, int], weight: float) -> list[int]:
    return [int(round((1 - weight) * color[i] + weight * target[i])) for i in range(3)]


def read_evidence(evidence_dir: Path) -> tuple[dict, dict[str, dict[str, list[int]]]]:
    record_path = evidence_dir / "evidence.json"
    evidence = json.loads(record_path.read_text(encoding="utf-8"))
    if evidence["format"] != "enfractal-photo-evidence-v0":
        raise ValueError("Unknown photo evidence format")
    samples: dict[str, dict[str, list[int]]] = {}
    for photo in evidence["photos"]:
        if photo["license"] not in SUPPORTED_LICENSES:
            raise ValueError(f"Unsupported photo license in pilot: {photo['license']}")
        path = evidence_dir / photo["file"]
        if _sha256(path) != photo["sha256"]:
            raise ValueError(f"Photo source hash mismatch: {photo['id']}")
        with Image.open(path) as opened:
            image = np.asarray(opened.convert("RGB"))
        samples[photo["id"]] = {
            name: _sample_rect(image, box)
            for name, box in photo["sample_rectangles_px"].items()
        }
    return evidence, samples


def _pixel(config: MapConfig, point: list[float]) -> tuple[int, int]:
    half = config.side_m / 2
    return (
        max(0, min(OVERLAY_SIDE - 1, round((point[0] + half) / config.side_m * (OVERLAY_SIDE - 1)))),
        max(0, min(OVERLAY_SIDE - 1, round((point[1] + half) / config.side_m * (OVERLAY_SIDE - 1)))),
    )


def _line_mask(config: MapConfig, items: list[dict], width: int, blur: int) -> np.ndarray:
    image = Image.new("L", (OVERLAY_SIDE, OVERLAY_SIDE), 0)
    draw = ImageDraw.Draw(image)
    for item in items:
        points = item["points"]
        if len(points) > 1:
            draw.line([_pixel(config, point) for point in points], fill=255, width=width, joint="curve")
    if blur:
        image = image.filter(ImageFilter.GaussianBlur(blur))
    return np.asarray(image, dtype=np.float32) / 255.0


def _parking_mask(config: MapConfig, features: dict) -> np.ndarray:
    image = Image.new("L", (OVERLAY_SIDE, OVERLAY_SIDE), 0)
    draw = ImageDraw.Draw(image)
    for item in features["developed_areas"]:
        if item["tags"].get("amenity") == "parking":
            draw.polygon([_pixel(config, point) for point in item["outline"]], fill=255)
    return np.asarray(image.filter(ImageFilter.GaussianBlur(1)), dtype=np.float32) / 255.0


def _zone(config: MapConfig, x: float, z: float, radius: float, fade: float = 65) -> np.ndarray:
    axis = np.linspace(-config.side_m / 2, config.side_m / 2, OVERLAY_SIDE, dtype=np.float32)
    xx, zz = np.meshgrid(axis, axis)
    distance = np.hypot(xx - x, zz - z)
    return np.clip((radius - distance) / fade, 0, 1)


def _grid_height(grid: np.ndarray, config: MapConfig, x: float, z: float) -> float:
    half = config.side_m / 2
    column = max(0.0, min(grid.shape[1] - 1.0, (x + half) / config.sample_spacing_m))
    row = max(0.0, min(grid.shape[0] - 1.0, (z + half) / config.sample_spacing_m))
    x0, z0 = int(column), int(row)
    x1, z1 = min(x0 + 1, grid.shape[1] - 1), min(z0 + 1, grid.shape[0] - 1)
    fx, fz = column - x0, row - z0
    return float((1-fz)*((1-fx)*grid[z0,x0] + fx*grid[z0,x1]) + fz*((1-fx)*grid[z1,x0] + fx*grid[z1,x1]))


def _rock_instances(config: MapConfig, grid: np.ndarray, overlay: np.ndarray, zone: dict) -> list[dict]:
    """Sparse representative limestone pieces on mapped creek banks."""
    rng = random.Random(config.vegetation_seed + 47)
    rocks: list[dict] = []
    for _ in range(14000):
        if len(rocks) >= 25:
            break
        x = float(zone["center_x_m"]) + rng.uniform(-float(zone["radius_m"]), float(zone["radius_m"]))
        z = float(zone["center_z_m"]) + rng.uniform(-float(zone["radius_m"]), float(zone["radius_m"]))
        if math.hypot(x - zone["center_x_m"], z - zone["center_z_m"]) > zone["radius_m"] - 35:
            continue
        column, row = _pixel(config, [x, z])
        rock, trail, parking = overlay[row, column] / 255.0
        if rock < 0.30 or trail > 0.25 or parking > 0.1 or rng.random() > rock:
            continue
        dx = _grid_height(grid, config, x + 2, z) - _grid_height(grid, config, x - 2, z)
        dz = _grid_height(grid, config, x, z + 2) - _grid_height(grid, config, x, z - 2)
        if math.hypot(dx, dz) / 4 < 0.08:
            continue
        rocks.append({
            "kind": "bank_stone",
            "x": round(x, 1), "z": round(z, 1),
            "elevation_m": round(_grid_height(grid, config, x, z), 2),
            "length_m": round(rng.uniform(4.5, 10.5), 2),
            "width_m": round(rng.uniform(2.0, 4.6), 2),
            "height_m": round(rng.uniform(0.35, 0.85), 2),
            "yaw_rad": round(rng.uniform(0, math.tau), 3),
        })
    if len(rocks) < 20:
        raise RuntimeError(f"Creek pilot yielded too few representative rocks: {len(rocks)}")
    # One low limestone shelf makes the photo study visible at exploration scale.
    # Its position is constrained to a sloping bank near the mapped creek/trail;
    # the exact formation is illustrative, not a photogrammetric reconstruction.
    for x, z, length, width, height in (
        (-219, 45, 18, 8, 0.8), (-213, 60, 21, 9, 0.9),
        (-208, 75, 23, 10, 1.0), (-210, 91, 20, 9, 0.8),
        (-217, 107, 17, 8, 0.7),
    ):
        rocks.append({
            "kind": "illustrative_shelf",
            "x": x, "z": z,
            "elevation_m": round(_grid_height(grid, config, x, z), 2),
            "length_m": length, "width_m": width, "height_m": height,
            "yaw_rad": 1.35,
        })
    return rocks


def _parking_markings(config: MapConfig, features: dict, overlay: np.ndarray, mall_x: float, mall_z: float, radius: float) -> list[list[float]]:
    """Representative stall paint aligned to mapped parking aisles, not surveyed bays."""
    markings: list[list[float]] = []
    for road in features["roads"]:
        if road["tags"].get("service") != "parking_aisle":
            continue
        points = road["points"]
        for start, end in zip(points, points[1:]):
            dx, dz = end[0] - start[0], end[1] - start[1]
            length = math.hypot(dx, dz)
            if length < 4:
                continue
            ux, uz = dx / length, dz / length
            for step in range(1, int(length // 5) + 1):
                center_x = start[0] + ux * step * 5
                center_z = start[1] + uz * step * 5
                if math.hypot(center_x - mall_x, center_z - mall_z) > radius - 25:
                    continue
                for side in (-1, 1):
                    ax, az = center_x - uz * side * 3.2, center_z + ux * side * 3.2
                    bx, bz = center_x - uz * side * 7.1, center_z + ux * side * 7.1
                    col_a, row_a = _pixel(config, [ax, az])
                    col_b, row_b = _pixel(config, [bx, bz])
                    if overlay[row_a, col_a, 2] < 150 or overlay[row_b, col_b, 2] < 150:
                        continue
                    markings.append([round(ax, 1), round(az, 1), round(bx, 1), round(bz, 1)])
    if len(markings) < 50:
        raise RuntimeError(f"Mall pilot yielded too few parking markings: {len(markings)}")
    return markings


def build_photo_pilot(config: MapConfig, grid: np.ndarray, features: dict, evidence_dir: Path, package_dir: Path) -> dict:
    evidence, samples = read_evidence(evidence_dir)
    creek = samples["greenbelt_limestone_2007"]
    mall = samples["mall_west_2020"]
    colors = {
        "limestone": _mix(creek["limestone"], (205, 194, 171), 0.35),
        "foliage": _mix(creek["foliage"], (60, 96, 48), 0.65),
        "creek_water": _mix(creek["creek_water"], (35, 91, 103), 0.65),
        "trail_soil": _mix(creek["limestone"], (150, 109, 75), 0.55),
        "mall_stone": mall["stone"],
        "mall_glass": _mix(mall["glass"], (52, 71, 77), 0.5),
        "pavement": _mix(mall["pavement"], (91, 93, 92), 0.72),
    }
    pilot = evidence["greenbelt_pilot"]
    mall_zone = evidence["mall_pilot"]
    mall_x, mall_z = config.to_local(mall_zone["center_lon"], mall_zone["center_lat"])
    downsampled = grid[::2, ::2][:OVERLAY_SIDE, :OVERLAY_SIDE]
    dz, dx = np.gradient(downsampled, config.sample_spacing_m * 2)
    slope = np.hypot(dx, dz)
    creek_items = [item for item in features["waterways"] if "Barton Creek" in item["name"]]
    creek_mask = _line_mask(config, creek_items, 28, 8)
    trail_mask = _line_mask(config, features["trails"], 7, 2)
    greenbelt_zone = _zone(config, pilot["center_x_m"], pilot["center_z_m"], pilot["radius_m"])
    parking_zone = _zone(config, mall_x, mall_z, mall_zone["radius_m"])
    rock = np.clip(creek_mask * (0.38 + 0.62 * np.clip(slope / 0.65, 0, 1)) * greenbelt_zone, 0, 1)
    trail = np.clip(trail_mask * greenbelt_zone * 0.86, 0, 1)
    parking = np.clip(_parking_mask(config, features) * parking_zone, 0, 1)
    overlay = np.uint8(np.rint(np.stack([rock, trail, parking], axis=2) * 255))
    overlay_path = package_dir / "photo_overlay.png"
    Image.fromarray(overlay, "RGB").save(overlay_path, optimize=True)
    rocks = _rock_instances(config, grid, overlay, pilot)
    markings = _parking_markings(config, features, overlay, mall_x, mall_z, mall_zone["radius_m"])
    result = {
        "format": "enfractal-photo-pilot-v0",
        "evidence_sha256": _sha256(evidence_dir / "evidence.json"),
        "source_ids": [photo["id"] for photo in evidence["photos"]],
        "sampled_rgb": samples,
        "display_rgb": colors,
        "overlay_file": overlay_path.name,
        "overlay_sha256": _sha256(overlay_path),
        "overlay_channels": ["representative limestone near mapped creek", "trail soil near mapped trail", "pavement in mapped mall parking"],
        "greenbelt_pilot": pilot,
        "mall_pilot": {**mall_zone, "center_x_m": round(mall_x, 2), "center_z_m": round(mall_z, 2)},
        "rock_instances": rocks,
        "parking_markings": markings,
        "credits": [
            {"author": photo["author"], "source_page": photo["source_page"], "license": photo["license"], "license_url": photo["license_url"]}
            for photo in evidence["photos"]
        ],
        "limitations": [
            "Photos provide material/color cues, not surveyed rock, tree or trail locations.",
            "The 2007 Greenbelt photo is not geolocated within the prototype; use is regional reference only.",
            "The 2020 mall photo faces east from the west parking area; facade pattern is simplified, not a precise reconstruction.",
            "Creek water level in photographs is seasonal; no photo-derived water level is applied.",
            "Parking lines are schematic paint aligned to OSM aisle centerlines, not surveyed individual stalls.",
        ],
    }
    target = package_dir / "photo_pilot.json"
    target.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return result
