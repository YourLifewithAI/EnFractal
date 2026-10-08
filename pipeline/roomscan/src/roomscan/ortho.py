"""Orthographic pictures of a scanned room, made from the photos and their point maps.

``rectify_plane`` unfolds one flat surface (a wall, the floor) into a picture at a fixed scale,
every pixel taken from the photo that sees that spot best and without anything in the way; a
doorway or a window can then be measured off the picture in metres. ``top_down`` looks straight
down on the whole scan: every cell shows its highest point's colour, which is what the founder
sees from above and what the inventory's boxes are drawn on.

Nothing here writes a file; callers save the arrays (under ``captures/`` only).
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from .scene import Scene


@dataclass
class PlanePicture:
    rgb: np.ndarray  # (rows, cols, 3) uint8; row 0 is the top (the highest v)
    seen: np.ndarray  # (rows, cols) bool: some photo saw this spot clear
    origin: np.ndarray  # room position of u = 0, v = 0
    u_dir: np.ndarray  # unit vector, left to right
    v_dir: np.ndarray  # unit vector, bottom to top
    res_m: float

    def to_uv(self, row: float, col: float) -> tuple[float, float]:
        return col * self.res_m, (self.rgb.shape[0] - row) * self.res_m


def _bilinear(img: np.ndarray, x: np.ndarray, y: np.ndarray) -> np.ndarray:
    h, w = img.shape[:2]
    x = np.clip(x, 0, w - 1.001)
    y = np.clip(y, 0, h - 1.001)
    x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
    fx, fy = (x - x0)[:, None], (y - y0)[:, None]
    a = img[y0, x0].astype(np.float32)
    b = img[y0, x0 + 1].astype(np.float32)
    c = img[y0 + 1, x0].astype(np.float32)
    d = img[y0 + 1, x0 + 1].astype(np.float32)
    return (a * (1 - fx) * (1 - fy) + b * fx * (1 - fy) + c * (1 - fx) * fy + d * fx * fy)


def rectify_plane(scene: Scene, origin, u_dir, v_dir, size_uv: tuple[float, float], *, res_m: float = 0.01,
                  depth_tol_m: float = 0.07, recess_m: float = 0.0, min_cos: float = 0.5, views: list[int] | None = None,
                  max_distance_m: float = 5.0, keep_best: int = 3) -> PlanePicture:
    """The flat rectangle spanned by ``u_dir`` (width) and ``v_dir`` (height) from ``origin``, as a picture.

    Each pixel is the median colour of the ``keep_best`` photos that see that spot best (square-on and close)
    with nothing in the way, so one photo's pose error or a passing clutter edge does not show. A photo sees
    "clear" when its own depth is no more than ``depth_tol_m`` in front of the surface and no more than
    ``recess_m`` (at least ``depth_tol_m``) behind it: a window or a door is set back into the wall.
    """
    origin, u_dir, v_dir = (np.asarray(a, float) for a in (origin, u_dir, v_dir))
    cols, rows = int(round(size_uv[0] / res_m)), int(round(size_uv[1] / res_m))
    uu = (np.arange(cols) + 0.5) * res_m
    vv = (rows - np.arange(rows) - 0.5) * res_m
    P = origin + uu[None, :, None] * u_dir + vv[:, None, None] * v_dir  # (rows, cols, 3)
    flat = P.reshape(-1, 3)
    normal = np.cross(u_dir, v_dir)
    n = len(flat)
    best = np.full((keep_best, n), -1.0, np.float32)  # scores, best first
    colour = np.zeros((keep_best, n, 3), np.float32)
    for view in views if views is not None else scene.registered:
        cam = scene.camera_centre(view)
        pix, z = scene.project(view, flat)
        h, w = scene.pts[view].shape[:2]
        ok = (z > 0.05) & (pix[:, 0] >= 1) & (pix[:, 0] < w - 2) & (pix[:, 1] >= 1) & (pix[:, 1] < h - 2)
        if not ok.any():
            continue
        idx = np.nonzero(ok)[0]
        ray = flat[idx] - cam
        dist = np.linalg.norm(ray, axis=1)
        cos = np.abs((ray / dist[:, None]) @ normal)
        good = (cos > min_cos) & (dist < max_distance_m)
        idx, pix_i, dist, cos = idx[good], pix[idx[good]], dist[good], cos[good]
        if len(idx) == 0:
            continue
        # Nothing in the way: the photo's own depth at that pixel agrees with the surface's depth.
        depth_map = np.linalg.norm(scene.pts[view] - cam, axis=-1) * scene.valid[view]
        px = np.clip(np.round(pix_i[:, 0]).astype(int), 0, w - 1)
        py = np.clip(np.round(pix_i[:, 1]).astype(int), 0, h - 1)
        seen_dist = depth_map[py, px]
        clear = (seen_dist > 0) & (seen_dist - dist > -depth_tol_m) & (seen_dist - dist < max(depth_tol_m, recess_m))
        idx, pix_i, dist, cos = idx[clear], pix_i[clear], dist[clear], cos[clear]
        if len(idx) == 0:
            continue
        score = (cos / (0.5 + dist)).astype(np.float32)
        img = scene.model_view(view)
        sample = _bilinear(img, pix_i[:, 0] * 2.0, pix_i[:, 1] * 2.0)
        # Insert into each pixel's best-first list; a sample that is displaced carries on to the next place down.
        for slot in range(keep_best):
            better = score > best[slot, idx]
            if not better.any():
                continue
            sel = idx[better]
            displaced_score, displaced_colour = best[slot, sel].copy(), colour[slot, sel].copy()
            best[slot, sel], colour[slot, sel] = score[better], sample[better]
            score[better], sample[better] = displaced_score, displaced_colour
    count = (best >= 0).sum(0)
    picture = np.zeros((n, 3), np.float32)
    for i in range(1, keep_best + 1):
        sel = count == i
        if sel.any():
            picture[sel] = np.median(colour[:i, sel], axis=0)
    return PlanePicture(np.clip(picture, 0, 255).astype(np.uint8).reshape(rows, cols, 3), (count > 0).reshape(rows, cols),
                        origin, u_dir, v_dir, res_m)


def top_down(scene: Scene, *, res_m: float = 0.02, views: list[int] | None = None, stride: int = 1,
             lowest_m: float = -0.05, highest_m: float | None = None, margin_m: float = 0.1,
             fill_px: int = 4) -> tuple[np.ndarray, np.ndarray, tuple[float, float]]:
    """A picture of the scan seen from straight above: each cell shows its highest point's colour.

    Returns (rgb, height_m, (x_min, z_min)) where pixel column c is x = x_min + (c + 0.5) * res_m and
    row r is z = z_min + (r + 0.5) * res_m, so +X is right and +Z is down: -Z up the picture, as in the
    coverage map.
    """
    x0, x1, z0, z1 = scene.bounds
    x0, z0, x1, z1 = x0 - margin_m, z0 - margin_m, x1 + margin_m, z1 + margin_m
    cols, rows = int(np.ceil((x1 - x0) / res_m)), int(np.ceil((z1 - z0) / res_m))
    top = highest_m if highest_m is not None else scene.height_m - 0.04
    height = np.full((rows, cols), -9.0, np.float32)
    rgb = np.zeros((rows, cols, 3), np.uint8)
    for view in views if views is not None else scene.registered:
        pts, valid = scene.pts[view], scene.valid[view].copy()
        valid[:] = valid & (pts[..., 1] > lowest_m) & (pts[..., 1] < top)
        if stride > 1:
            keep = np.zeros_like(valid)
            keep[::stride, ::stride] = True
            valid &= keep
        rr, cc = np.nonzero(valid)
        if len(rr) == 0:
            continue
        p = pts[rr, cc]
        col = np.clip(((p[:, 0] - x0) / res_m).astype(int), 0, cols - 1)
        row = np.clip(((p[:, 2] - z0) / res_m).astype(int), 0, rows - 1)
        img = scene.model_view(view)
        colour = img[np.clip(rr * 2, 0, img.shape[0] - 1), np.clip(cc * 2, 0, img.shape[1] - 1)]
        order = np.argsort(p[:, 1])  # the highest point of a cell is written last
        col, row, colour, y = col[order], row[order], colour[order], p[order, 1]
        higher = y > height[row, col]
        height[row[higher], col[higher]] = y[higher]
        rgb[row[higher], col[higher]] = colour[higher]
    if fill_px > 0:
        from scipy import ndimage

        empty = height < -8.0
        dist, (ri, ci) = ndimage.distance_transform_edt(empty, return_indices=True)
        near = empty & (dist <= fill_px)
        rgb[near] = rgb[ri[near], ci[near]]
        height[near] = height[ri[near], ci[near]]
    return rgb, height, (x0, z0)


def floor_occupancy(scene: Scene, *, res_m: float = 0.04, floor_band_m: float = 0.05, obstacle_low_m: float = 0.15,
                    obstacle_high_m: float = 1.8, margin_m: float = 0.1) -> tuple[np.ndarray, np.ndarray, tuple[float, float]]:
    """Per floor cell, how many points sit on the floor and how many stand above it (furniture, clutter).

    A cell is free floor when it has floor points and next to no points between ``obstacle_low_m`` and
    ``obstacle_high_m``: the pose model's own noise is several centimetres, so a cell's tallest point says
    nothing, but a handful of points half a metre up says something stands there. Returns
    (floor_points, obstacle_points, (x_min, z_min)); row r is z = z_min + (r + 0.5) * res_m.
    """
    x0, x1, z0, z1 = scene.bounds
    x0, z0, x1, z1 = x0 - margin_m, z0 - margin_m, x1 + margin_m, z1 + margin_m
    cols, rows = int(np.ceil((x1 - x0) / res_m)), int(np.ceil((z1 - z0) / res_m))
    floor_n = np.zeros(rows * cols, np.int64)
    block_n = np.zeros(rows * cols, np.int64)
    for view in scene.registered:
        p = scene.pts[view][scene.valid[view]]
        col = np.clip(((p[:, 0] - x0) / res_m).astype(int), 0, cols - 1)
        row = np.clip(((p[:, 2] - z0) / res_m).astype(int), 0, rows - 1)
        flat = row * cols + col
        on_floor = np.abs(p[:, 1]) < floor_band_m
        above = (p[:, 1] > obstacle_low_m) & (p[:, 1] < obstacle_high_m)
        floor_n += np.bincount(flat[on_floor], minlength=rows * cols)
        block_n += np.bincount(flat[above], minlength=rows * cols)
    return floor_n.reshape(rows, cols), block_n.reshape(rows, cols), (x0, z0)
