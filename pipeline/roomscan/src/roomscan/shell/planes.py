"""The room's shell as planes: floor, ceiling and four walls from the fitted room box, with the evidence
that each plane is where the box says, and one broad colour for each.

The coverage run already fitted the box (``layout.fit_room``) and scaled it to the tape. This does
not fit it again (the ceiling moved 8 to 10 cm with the fifth digit of the scale when it was refitted);
it checks the box against the points of every photo: how many points lie on each plane, how far the
points are from it, and how many photos saw it. Colours come from the pixels of the points that are
on a plane (within a few centimetres of it), so shelving, posters and furniture standing in front of
a wall do not paint it.

Wall letters follow the coverage map: A at z_min (the wall the first photo faced), B at x_max, C at
z_max and D at x_min; looking down with -Z up the page, +X is to the right.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import numpy as np

from ..colours import dominant_colours
from ..coverage.geometry import grid_normals
from ..scene import Scene

WALL_KEYS = ("A", "B", "C", "D")
# Inward normal (x, z) of each wall and the axis it stands on.
INWARD = {"A": (0.0, 1.0), "B": (-1.0, 0.0), "C": (0.0, -1.0), "D": (1.0, 0.0)}
MATERIAL = {"floor": "concrete", "ceiling": "plaster", "wall": "painted_wall"}
NEAR_PLANE_M = 0.04  # a point this close to a plane is on it
EDGE_MARGIN_M = 0.15  # corners and the skirting are not wall colour
MIN_COS = 0.8


@dataclass
class Surface:
    id: str
    role: str  # floor, wall, ceiling
    key: str  # "floor", "ceiling" or the wall letter
    colours: list[tuple[str, float]]  # broad colours with their shares, largest first
    evidence: dict[str, Any] = field(default_factory=dict)

    @property
    def base_color(self) -> str:
        return self.colours[0][0]

    @property
    def material_role(self) -> str:
        return MATERIAL[self.role]


@dataclass
class ShellPlan:
    x_min: float
    x_max: float
    z_min: float
    z_max: float
    height: float
    surfaces: list[Surface]
    scale_factor: float
    scale_source: str

    def surface(self, key: str) -> Surface:
        return next(s for s in self.surfaces if s.key == key)


def _plane_masks(scene: Scene, view: int, normals: np.ndarray) -> dict[str, np.ndarray]:
    """Which points of one photo lie on each of the six planes, facing the room."""
    p = scene.pts[view]
    ok = scene.valid[view]
    x0, x1, z0, z1 = scene.bounds
    H = scene.height_m
    nx, ny, nz = normals[..., 0], normals[..., 1], normals[..., 2]
    inside_x = (p[..., 0] > x0 + EDGE_MARGIN_M) & (p[..., 0] < x1 - EDGE_MARGIN_M)
    inside_z = (p[..., 2] > z0 + EDGE_MARGIN_M) & (p[..., 2] < z1 - EDGE_MARGIN_M)
    inside_y = (p[..., 1] > EDGE_MARGIN_M) & (p[..., 1] < H - EDGE_MARGIN_M)
    masks = {
        "floor": ok & (np.abs(p[..., 1]) < NEAR_PLANE_M) & (np.abs(ny) > 0.9) & inside_x & inside_z,
        "ceiling": ok & (np.abs(p[..., 1] - H) < NEAR_PLANE_M) & (np.abs(ny) > 0.9) & inside_x & inside_z,
        "A": ok & (np.abs(p[..., 2] - z0) < NEAR_PLANE_M) & (np.abs(nz) > MIN_COS) & inside_x & inside_y,
        "C": ok & (np.abs(p[..., 2] - z1) < NEAR_PLANE_M) & (np.abs(nz) > MIN_COS) & inside_x & inside_y,
        "D": ok & (np.abs(p[..., 0] - x0) < NEAR_PLANE_M) & (np.abs(nx) > MIN_COS) & inside_z & inside_y,
        "B": ok & (np.abs(p[..., 0] - x1) < NEAR_PLANE_M) & (np.abs(nx) > MIN_COS) & inside_z & inside_y,
    }
    return masks


def _offsets(scene: Scene, view: int, normals: np.ndarray) -> dict[str, np.ndarray]:
    """Signed distances of the points that face each plane and are within 25 cm of it, for the residual statistics."""
    p = scene.pts[view]
    ok = scene.valid[view]
    x0, x1, z0, z1 = scene.bounds
    H = scene.height_m
    nx, ny, nz = normals[..., 0], normals[..., 1], normals[..., 2]
    near = 0.25
    sel = {
        "floor": ok & (np.abs(p[..., 1]) < near) & (np.abs(ny) > 0.9),
        "ceiling": ok & (np.abs(p[..., 1] - H) < near) & (np.abs(ny) > 0.9),
        "A": ok & (np.abs(p[..., 2] - z0) < near) & (np.abs(nz) > MIN_COS),
        "C": ok & (np.abs(p[..., 2] - z1) < near) & (np.abs(nz) > MIN_COS),
        "D": ok & (np.abs(p[..., 0] - x0) < near) & (np.abs(nx) > MIN_COS),
        "B": ok & (np.abs(p[..., 0] - x1) < near) & (np.abs(nx) > MIN_COS),
    }
    off = {"floor": p[..., 1], "ceiling": p[..., 1] - H, "A": p[..., 2] - z0, "C": p[..., 2] - z1,
           "D": p[..., 0] - x0, "B": p[..., 0] - x1}
    return {k: off[k][sel[k]] for k in sel}


def plan_shell(scene: Scene, *, max_colour_pixels: int = 60000) -> ShellPlan:
    x0, x1, z0, z1 = scene.bounds
    H = scene.height_m
    pixels: dict[str, list[np.ndarray]] = {k: [] for k in ("floor", "ceiling", *WALL_KEYS)}
    offsets: dict[str, list[np.ndarray]] = {k: [] for k in pixels}
    photos: dict[str, set[int]] = {k: set() for k in pixels}
    for view in scene.registered:
        normals = grid_normals(scene.pts[view].astype(np.float64))
        masks = _plane_masks(scene, view, normals)
        for key, m in masks.items():
            if int(m.sum()) < 30:
                continue
            rr, cc = np.nonzero(m)
            if len(rr) > 4000:
                pick = np.random.default_rng(view).choice(len(rr), 4000, replace=False)
                rr, cc = rr[pick], cc[pick]
            pixels[key].append(scene.pixel_colours(view, rr, cc))
            photos[key].add(view)
        for key, off in _offsets(scene, view, normals).items():
            if len(off):
                offsets[key].append(off[:: max(1, len(off) // 3000)])
    surfaces = []
    areas = {"floor": (x1 - x0) * (z1 - z0), "ceiling": (x1 - x0) * (z1 - z0), "A": (x1 - x0) * H, "C": (x1 - x0) * H,
             "B": (z1 - z0) * H, "D": (z1 - z0) * H}
    for key in ("floor", "ceiling", *WALL_KEYS):
        rgb = np.concatenate(pixels[key]) if pixels[key] else np.zeros((0, 3))
        if len(rgb) > max_colour_pixels:
            rgb = rgb[np.random.default_rng(0).choice(len(rgb), max_colour_pixels, replace=False)]
        off = np.concatenate(offsets[key]) if offsets[key] else np.zeros(0)
        colours = dominant_colours(rgb, 3) if len(rgb) else [("#b3aea4", 1.0)]
        within = off[np.abs(off) < 0.12]
        evidence = {
            "photos_on_plane": len(photos[key]),
            "colour_pixels": int(len(rgb)),
            "points_within_25cm": int(len(off)),
            "median_offset_cm": round(float(np.median(within)) * 100, 1) if len(within) else None,
            "spread_cm": round(float(np.percentile(np.abs(within), 75)) * 100, 1) if len(within) else None,
            "area_m2": round(areas[key], 2),
        }
        role = "floor" if key == "floor" else "ceiling" if key == "ceiling" else "wall"
        sid = "shell:" + ("floor" if key == "floor" else "ceiling" if key == "ceiling" else f"wall_{key.lower()}")
        surfaces.append(Surface(sid, role, key, colours if len(rgb) else [("#b3aea4", 1.0)], evidence))
    return ShellPlan(x0, x1, z0, z1, H, surfaces, scene.factor, scene.scale_source)
