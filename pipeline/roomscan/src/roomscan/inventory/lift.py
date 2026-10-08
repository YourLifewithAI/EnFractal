"""From a 2-D detection to 3-D points of the thing it found.

A detection is a box in one photo. The photo's own point map says where every pixel is in the
room, so the pixels of the box are 3-D points: some on the object, some on whatever is behind it
(a wall, a shelf), a few in front (clutter). The object's points are the ones near the object's
own distance from the camera, where the object is judged by the middle of the box.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field

import numpy as np

from ..scene import Scene

MAX_POINTS = 600


@dataclass
class Sighting:
    view: int
    kind: str
    score: float
    box: tuple[float, float, float, float]  # stored point-map pixels x0, y0, x1, y1
    points: np.ndarray = field(repr=False)  # (N, 3) room frame
    rows: np.ndarray = field(repr=False)  # (N,) stored-map pixel rows of the points
    cols: np.ndarray = field(repr=False)
    centre: np.ndarray = field(default_factory=lambda: np.zeros(3))
    lo: np.ndarray = field(default_factory=lambda: np.zeros(3))
    hi: np.ndarray = field(default_factory=lambda: np.zeros(3))
    distance_m: float = 0.0
    extent_m: float = 0.0  # the larger of the box's width and height at the object's distance


def lift_sighting(scene: Scene, view: int, kind: str, score: float, box: tuple[float, float, float, float], *,
                  shrink: float = 0.2, min_points: int = 6, seed: int = 0) -> Sighting | None:
    pts, valid = scene.pts[view], scene.valid[view]
    h, w = pts.shape[:2]
    x0, y0, x1, y1 = box
    xa, xb = max(0, int(math.floor(x0))), min(w, int(math.ceil(x1)))
    ya, yb = max(0, int(math.floor(y0))), min(h, int(math.ceil(y1)))
    if xb - xa < 2 or yb - ya < 2:
        return None
    region = pts[ya:yb, xa:xb]
    ok = valid[ya:yb, xa:xb]
    cam = scene.camera_centre(view)
    dist = np.linalg.norm(region - cam, axis=-1)
    # The middle of the box says what the object is: its distance from the camera.
    iw, ih = (xb - xa) * shrink, (yb - ya) * shrink
    inner = ok.copy()
    inner[:, :max(1, int(round(iw)))] = False
    inner[:, xb - xa - max(1, int(round(iw))):] = False
    inner[:max(1, int(round(ih))), :] = False
    inner[yb - ya - max(1, int(round(ih))):, :] = False
    if int(inner.sum()) < min_points:
        inner = ok
        if int(inner.sum()) < min_points:
            return None
    d0 = float(np.median(dist[inner]))
    f = float(scene.K[view][0, 0])
    extent = max(x1 - x0, y1 - y0) / f * d0
    tol = float(np.clip(0.6 * extent, 0.06, 1.2))
    keep = ok & (np.abs(dist - d0) <= tol)
    if int(keep.sum()) < min_points:
        return None
    rr, cc = np.nonzero(keep)
    p = region[rr, cc]
    # Stray points far from the rest (depth-edge flyers) are not the object.
    med = np.median(p, axis=0)
    near = np.linalg.norm(p - med, axis=1) <= max(0.08, 1.2 * extent)
    p, rr, cc = p[near], rr[near], cc[near]
    if len(p) < min_points:
        return None
    if len(p) > MAX_POINTS:
        pick = np.random.default_rng(seed + view).choice(len(p), MAX_POINTS, replace=False)
        p, rr, cc = p[pick], rr[pick], cc[pick]
    return Sighting(view, kind, float(score), (float(x0), float(y0), float(x1), float(y1)), p,
                    rr + ya, cc + xa, np.median(p, axis=0), np.percentile(p, 5, axis=0), np.percentile(p, 95, axis=0),
                    d0, float(extent))


def lift_all(scene: Scene, detections: dict[int, list[list]], *, min_score: float = 0.14,
             inside_margin_m: float = 0.4) -> list[Sighting]:
    """Every detection that lifts to a point inside the room's box."""
    x0, x1, z0, z1 = scene.bounds
    top = scene.height_m
    out: list[Sighting] = []
    for view in scene.registered:
        for kind, score, box in detections.get(view, []):
            if score < min_score:
                continue
            s = lift_sighting(scene, view, kind, score, tuple(box))
            if s is None:
                continue
            c = s.centre
            if x0 - inside_margin_m <= c[0] <= x1 + inside_margin_m and z0 - inside_margin_m <= c[2] <= z1 + inside_margin_m \
                    and -0.2 <= c[1] <= top + 0.3:
                out.append(s)
    return out
