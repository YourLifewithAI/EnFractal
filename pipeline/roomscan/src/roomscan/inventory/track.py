"""Following one object through the photos: which photos see it, and where to point the segmenter in each.

The detector finds an object in a few photos and misses it in most (a jar is twenty pixels across in
a wide shot). Its 3-D position is enough to find it everywhere: the point projects into every photo,
and a photo sees it when its own depth there agrees. Those photos get a box prompt for the segmenter
round the projected point, sized from what the object is expected to measure. A good set of photos
is close, roughly square-on to it, and spread round it, not ten neighbours from the same spot.
"""

from __future__ import annotations

import math
from dataclasses import dataclass

import numpy as np

from ..scene import Scene


@dataclass
class Prompt:
    view: int
    box_stored: tuple[float, float, float, float]  # where to look, in stored point-map pixels
    distance_m: float
    azimuth_deg: float  # direction of the camera from the object, in the floor plane
    source: str  # "detection" or "projection"
    score: float = 0.0


def visible_views(scene: Scene, centre: np.ndarray, size_hint_m: float, *, min_distance_m: float = 0.5,
                  max_distance_m: float = 5.0, margin_px: float = 6.0) -> list[tuple[int, float, np.ndarray]]:
    """Photos in which a point of the room is in frame and not hidden: (view, distance, pixel (x, y) in the stored map)."""
    out = []
    tol = max(0.20, 0.8 * size_hint_m)
    for view in scene.registered:
        pix, z = scene.project(view, centre[None])
        h, w = scene.pts[view].shape[:2]
        x, y = float(pix[0, 0]), float(pix[0, 1])
        if z[0] <= 0.1 or not (margin_px < x < w - margin_px and margin_px < y < h - margin_px):
            continue
        dist = float(np.linalg.norm(scene.camera_centre(view) - centre))
        if not (min_distance_m <= dist <= max_distance_m):
            continue
        # Something nearer in the way: the photo's own distance at that pixel is well short of the object's.
        r0, r1 = max(0, int(y) - 2), min(h, int(y) + 3)
        c0, c1 = max(0, int(x) - 2), min(w, int(x) + 3)
        patch = scene.pts[view][r0:r1, c0:c1]
        ok = scene.valid[view][r0:r1, c0:c1]
        if not ok.any():
            continue
        seen = np.median(np.linalg.norm(patch[ok] - scene.camera_centre(view), axis=-1))
        if seen < dist - tol:
            continue
        out.append((view, dist, np.array([x, y])))
    return out


def projected_box(scene: Scene, view: int, pixel: np.ndarray, distance_m: float, size_hint_m: float,
                  *, spread: float = 0.75) -> tuple[float, float, float, float]:
    """A square box in stored pixels round a projected centre, ``spread`` times the expected size on a side."""
    f = float(scene.K[view][0, 0])
    half = max(3.0, 0.5 * spread * size_hint_m / max(distance_m, 0.2) * f)
    return (float(pixel[0] - half), float(pixel[1] - half), float(pixel[0] + half), float(pixel[1] + half))


def azimuth_deg(scene: Scene, view: int, centre: np.ndarray) -> float:
    d = scene.camera_centre(view) - centre
    return math.degrees(math.atan2(d[2], d[0]))


def pick_prompts(scene: Scene, centre: np.ndarray, size_hint_m: float, detections: list[tuple[int, float, tuple[float, float, float, float]]],
                 *, max_views: int = 10, ideal_distance_m: float = 1.6) -> list[Prompt]:
    """Up to ``max_views`` photos to segment the object in: the detector's own boxes where it found it, projected
    boxes elsewhere, chosen close, square-on and spread round the object.

    ``detections`` are (view, score, box in stored pixels) for sightings the detector made of this object.
    """
    seen = {v: (d, p) for v, d, p in visible_views(scene, centre, size_hint_m)}
    best_detection: dict[int, tuple[float, tuple[float, float, float, float]]] = {}
    for view, score, box in detections:
        if view in seen and (view not in best_detection or score > best_detection[view][0]):
            best_detection[view] = (score, box)
    candidates: list[Prompt] = []
    for view, (dist, pixel) in seen.items():
        if view in best_detection:
            score, box = best_detection[view]
            source = "detection"
        else:
            box, score, source = projected_box(scene, view, pixel, dist, size_hint_m), 0.0, "projection"
        # Close to the ideal distance (the object fills a useful part of the frame, but is not cut off), and
        # a detection counts for more than a guess.
        closeness = math.exp(-((math.log(dist / ideal_distance_m)) ** 2) / 0.5)
        x0, y0, x1, y1 = box
        h, w = scene.pts[view].shape[:2]
        fully_in = 1.0 if (x0 > 1 and y0 > 1 and x1 < w - 1 and y1 < h - 1) else 0.6
        value = closeness * fully_in * (1.0 + 1.5 * score + (0.5 if source == "detection" else 0.0))
        candidates.append(Prompt(view, box, dist, azimuth_deg(scene, view, centre), source, value))
    chosen: list[Prompt] = []
    pool = sorted(candidates, key=lambda p: -p.score)
    while pool and len(chosen) < max_views:
        if not chosen:
            pick = pool[0]
        else:
            # The next photo adds the most direction: score weighted by the angle to the nearest chosen one.
            def gain(p: Prompt) -> float:
                gap = min(abs((p.azimuth_deg - c.azimuth_deg + 180) % 360 - 180) for c in chosen)
                return p.score * (0.35 + min(gap, 60.0) / 60.0)
            pick = max(pool, key=gain)
        chosen.append(pick)
        pool.remove(pick)
    return chosen


def mean_viewing_direction(scene: Scene, centre: np.ndarray, views: list[int]) -> np.ndarray:
    """Mean position (x, z) of the cameras that saw the object."""
    return np.array([[scene.camera_centre(v)[0], scene.camera_centre(v)[2]] for v in views])

