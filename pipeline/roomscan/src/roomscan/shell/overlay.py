"""The shell drawn back onto the photos, so a reviewer can see whether it sits where the room does.

Each wall's rectangle is drawn in yellow and each opening in a colour of its own, projected with the
pose of the photo. Pose error moves everything together by a few centimetres, so what matters is
that edges land on edges (a window's frame, a door's jamb) in several photos, not in one.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image

from ..inventory.photos import contact_sheet, draw_polyline3d, open_photo
from ..paths import OutputGuard
from ..scene import Scene
from .manifest import opening_centre, wall_frame
from .planes import WALL_KEYS, ShellPlan
from .spec import ShellSpec

COLOURS = [(255, 0, 255), (0, 255, 255), (255, 128, 0), (0, 255, 0), (255, 64, 64), (128, 128, 255)]


def opening_corners(plan: ShellPlan, o) -> np.ndarray:
    f = wall_frame(plan, o.wall)
    origin, u = np.array(f["origin"]), np.array(f["u"])
    up = np.array([0.0, 1.0, 0.0])
    a, b = o.u_m - o.width_m / 2, o.u_m + o.width_m / 2
    v0, v1 = o.v_bottom_m, o.v_bottom_m + o.height_m
    return np.array([origin + u * a + up * v0, origin + u * b + up * v0, origin + u * b + up * v1, origin + u * a + up * v1])


def overlay_image(scene: Scene, plan: ShellPlan, spec: ShellSpec, view: int, *, max_side: int = 900,
                  walls: bool = True) -> Image.Image:
    im = open_photo(scene, view, max_side=max_side)
    k = im.width / int(scene.views[view]["image"]["width"])
    if walls:
        for key in WALL_KEYS:
            f = wall_frame(plan, key)
            o, u = np.array(f["origin"]), np.array(f["u"])
            rect = np.array([o, o + u * f["length"], o + u * f["length"] + [0, plan.height, 0], o + [0, plan.height, 0]])
            draw_polyline3d(scene, view, im, rect, colour=(255, 255, 0), width=2, scale=k)
    for i, o in enumerate(spec.openings):
        draw_polyline3d(scene, view, im, opening_corners(plan, o), colour=COLOURS[i % len(COLOURS)], width=3, scale=k)
    return im


def best_views_for(scene: Scene, plan: ShellPlan, o, count: int = 3) -> list[int]:
    """Photos that look at an opening roughly square-on from a reasonable distance, best first."""
    centre = np.array(opening_centre(plan, o))
    n = np.array(wall_frame(plan, o.wall)["n"])
    scored = []
    for v in scene.registered:
        cam = scene.camera_centre(v)
        ray = centre - cam
        dist = float(np.linalg.norm(ray))
        pix, z = scene.project(v, centre[None])
        h, w = scene.pts[v].shape[:2]
        if z[0] <= 0.1 or not (4 < pix[0, 0] < w - 4 and 4 < pix[0, 1] < h - 4) or dist > 5:
            continue
        cos = abs(float(ray @ n)) / dist
        scored.append((cos / (0.7 + dist), v))
    return [v for _, v in sorted(scored, reverse=True)[:count]]


def overlay_sheet(scene: Scene, plan: ShellPlan, spec: ShellSpec, path: Path, *, per_opening: int = 2,
                  cols: int = 4, cell: int = 480) -> Path:
    """The openings drawn onto the photos that see them best, in one picture saved under the capture (``path`` is absolute or
    relative to the room's capture folder; anywhere else is refused)."""
    guard = OutputGuard(scene.room_dir)
    guard.image_path(path)  # refuse a bad place before any work
    views: list[int] = []
    for o in spec.openings:
        for v in best_views_for(scene, plan, o, per_opening):
            if v not in views:
                views.append(v)
    images = []
    for v in views:
        im = overlay_image(scene, plan, spec, v)
        im.thumbnail((cell, cell))
        images.append(im)
    return guard.write_image(path, contact_sheet(images, cols=cols, cell=cell), quality=88)
