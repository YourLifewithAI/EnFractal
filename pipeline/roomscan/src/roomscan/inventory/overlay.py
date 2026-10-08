"""An inventory object's box drawn back onto photos of it, to check by eye that the box is the object.

The box (12 edges, the front edge heavier and a different colour so the facing can be judged) is
projected with each photo's pose into the photos that saw the object best. Pose error moves the box
a few centimetres as a whole; what must hold is its size against the thing and its turn.
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image, ImageDraw

from ..scene import Scene
from .fit import Box, box_corners_xz
from .photos import open_photo, project_to_photo, sheet
from .track import visible_views


def box_edges(box: Box) -> list[tuple[np.ndarray, np.ndarray, str]]:
    """The box's edges as (start, end, kind) with kind "front" for the bottom and top edges at the front face."""
    xz = box_corners_xz(box)  # back-left, ... order: -x -z(front), +x -z, +x +z, -x +z in local terms
    lo, hi = box.base_y, box.base_y + box.size_m[1]
    bottom = [np.array([x, lo, z]) for x, z in xz]
    top = [np.array([x, hi, z]) for x, z in xz]
    edges = []
    for i in range(4):
        j = (i + 1) % 4
        kind = "front" if i == 0 else "box"
        edges += [(bottom[i], bottom[j], kind), (top[i], top[j], kind), (bottom[i], top[i], "box")]
    return edges


def draw_box(scene: Scene, view: int, image: Image.Image, box: Box, *, scale: float, colour=(0, 255, 0), front=(255, 0, 255), width: int = 3) -> None:
    d = ImageDraw.Draw(image)
    for a, b, kind in box_edges(box):
        pa, za = project_to_photo(scene, view, a[None])
        pb, zb = project_to_photo(scene, view, b[None])
        if za[0] > 0.05 and zb[0] > 0.05:
            d.line([tuple(pa[0] * scale), tuple(pb[0] * scale)], fill=front if kind == "front" else colour, width=width)


def entry_box(entry: dict[str, Any]) -> Box:
    b = entry["box"]
    return Box(tuple(b["centre_m"]), tuple(b["size_m"]), b["yaw_deg"], entry["placement"]["position_m"][1])


def best_views(scene: Scene, entry: dict[str, Any], count: int = 6) -> list[int]:
    box = entry_box(entry)
    centre = np.array(box.centre_m)
    hint = max(box.size_m)
    seen = visible_views(scene, centre, hint, min_distance_m=0.4, max_distance_m=5.0)
    scored = []
    for view, dist, pixel in seen:
        h, w = scene.pts[view].shape[:2]
        edge = min(pixel[0], w - pixel[0], pixel[1], h - pixel[1]) / min(h, w)
        scored.append((edge / (0.8 + abs(dist - 1.8)), view))
    return [v for _, v in sorted(scored, reverse=True)[:count]]


def object_overlay_sheet(scene: Scene, entry: dict[str, Any], path: Path, *, count: int = 6, cell: int = 520, max_side: int = 1100) -> Path:
    box = entry_box(entry)
    images = []
    for v in best_views(scene, entry, count):
        im = open_photo(scene, v, max_side=max_side)
        k = im.width / int(scene.views[v]["image"]["width"])
        draw_box(scene, v, im, box, scale=k, width=3)
        # Crop round the box so the object fills the picture.
        pts = np.array([a for a, _, _ in box_edges(box)] + [b for _, b, _ in box_edges(box)])
        xy, z = project_to_photo(scene, v, pts)
        xy = xy[z > 0.05] * k
        if len(xy):
            x0, y0 = xy.min(0)
            x1, y1 = xy.max(0)
            pad = 0.6 * max(x1 - x0, y1 - y0, 60)
            im = im.crop((int(max(0, x0 - pad)), int(max(0, y0 - pad)), int(min(im.width, x1 + pad)), int(min(im.height, y1 + pad))))
        im.thumbnail((cell, cell))
        images.append(im)
    return sheet(images, path, cols=3, cell=cell)
