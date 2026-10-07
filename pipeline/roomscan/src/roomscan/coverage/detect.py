"""Run the detector over registered photos and turn detections into room objects (cached)."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import numpy as np

from ..jsonio import json_bytes, sha256_hex
from ..paths import OutputGuard
from .backend import MODEL_REGISTRY, wait_for_vram
from .geometry import transform_points
from .grid import WALLS, wall_axes
from .objects import (DETECTOR, VOCABULARY, Detection, Detector, build_instances, cluster, detection_image, lift,
                      measure_views)

THRESHOLD = 0.22


def _cache_key(views: list[dict[str, Any]], indices: list[int], detector: str = DETECTOR) -> str:
    return sha256_hex(json_bytes({
        "views": [[views[i]["name"], views[i]["sha256"]] for i in indices],
        "detector": detector, "revision": MODEL_REGISTRY[DETECTOR]["revision"],
        "vocabulary": VOCABULARY, "threshold": THRESHOLD, "version": 2,  # 2: draft-mode decoding
    }))


def run_detector(guard: OutputGuard, cov_rel: Path, room_dir: Path, views: list[dict[str, Any]], indices: list[int],
                 detector_factory=None, log=print) -> tuple[dict[int, list[tuple[str, float, list[float]]]], dict[str, Any]]:
    """Detections per photo in stored point-map pixels, from cache when nothing changed.

    ``detector_factory`` replaces the GPU detector (tests); its ``detect(image, photo=...)``
    returns (label, score, box in ``image`` pixels) like ``Detector.detect``.
    """
    name = DETECTOR if detector_factory is None else "injected"
    key = _cache_key(views, indices, name)
    cache = guard.path(cov_rel / "detections.json")
    if cache.is_file() and detector_factory is None:
        data = json.loads(cache.read_bytes())
        if data.get("key") == key:
            return {int(k): v for k, v in data["detections"].items()}, data["info"] | {"reused_from_cache": True}
    if detector_factory is None:
        wait_for_vram(1500, log=log)
        det = Detector(threshold=THRESHOLD, log=log)
    else:
        det = detector_factory()
    out: dict[int, list[tuple[str, float, list[float]]]] = {}
    for n, i in enumerate(indices, 1):
        image, factor = detection_image(room_dir / views[i]["derived"]["jpeg"])
        found = det.detect(image, photo=views[i])
        out[i] = [(label, round(score, 4), [round(c * factor, 2) for c in box]) for label, score, box in found]
        if n % 25 == 0:
            log(f"  detected objects in {n}/{len(indices)} photos")
    info = {"detector": name, "threshold": THRESHOLD, "photos": len(indices),
            "gpu_seconds": round(det.seconds, 2), "peak_allocated_mib": round(det.peak_mib),
            "reused_from_cache": False}
    det.close()
    guard.write_bytes(cov_rel / "detections.json", json_bytes({"key": key, "info": info,
                                                               "detections": {str(k): v for k, v in out.items()}}))
    return out, info


def nearest_wall(lo: np.ndarray, hi: np.ndarray, bounds, *, within: float = 0.7) -> dict[str, Any] | None:
    x0, x1, z0, z1 = bounds
    gaps = {"A": lo[2] - z0, "B": x1 - hi[0], "C": z1 - hi[2], "D": lo[0] - x0}
    key = min(gaps, key=gaps.get)
    if gaps[key] > within:
        return None
    return wall_axes(key, bounds)


def extend_to_wall(inst, bounds, *, touching_m: float = 0.25, tall_m: float = 1.0, tall_gap_m: float = 0.7) -> None:
    """Photos only show the front of furniture standing against a wall, so its box comes out as
    thin as its front face. Extend it back to the wall when it touches the wall, or when it is tall
    (shelving, cabinets) and near it."""
    if inst.wall is None:
        return
    x0, x1, z0, z1 = bounds
    gap = {"A": inst.lo[2] - z0, "B": x1 - inst.hi[0], "C": z1 - inst.hi[2], "D": inst.lo[0] - x0}[inst.wall]
    if not (gap < touching_m or (inst.size[1] > tall_m and gap < tall_gap_m)):
        return
    if inst.wall == "A":
        inst.lo[2] = z0
    elif inst.wall == "B":
        inst.hi[0] = x1
    elif inst.wall == "C":
        inst.hi[2] = z1
    else:
        inst.lo[0] = x0


def detect_objects(guard: OutputGuard, cov_rel: Path, room_dir: Path, views: list[dict[str, Any]],
                   preds: dict[int, dict[str, np.ndarray]], registered: list[int], T: np.ndarray, bounds,
                   xyz: np.ndarray, owner: np.ndarray, cams: dict[int, np.ndarray], detector_factory=None,
                   log=print):
    raw, info = run_detector(guard, cov_rel, room_dir, views, registered, detector_factory=detector_factory, log=log)
    dets: list[Detection] = []
    for i in registered:
        pr = preds[i]
        pts_room = transform_points(T @ pr["c2w"], pr["pts_cam"].astype(np.float64))
        valid = pr["mask"] & np.isfinite(pts_room).all(-1)
        for label, score, box in raw.get(i, []):
            d = Detection(i, label, float(score), tuple(box))
            if lift(d, pts_room, valid):
                x0, x1, z0, z1 = bounds
                c = d.centre
                if x0 - 0.3 <= c[0] <= x1 + 0.3 and z0 - 0.3 <= c[2] <= z1 + 0.3 and -0.2 <= c[1] <= 3.5:
                    dets.append(d)
    instances = build_instances(cluster(dets))
    pts_by_view = {v: xyz[owner == v] for v in registered}
    room_centre = np.array([(bounds[0] + bounds[1]) / 2, 1.0, (bounds[2] + bounds[3]) / 2])
    for inst in instances:
        measure_views(inst, pts_by_view, cams, room_centre, nearest_wall(inst.lo, inst.hi, bounds), min_points=30)
        extend_to_wall(inst, bounds)
    info = dict(info)
    info.update({"detections": sum(len(v) for v in raw.values()), "lifted": len(dets), "objects": len(instances)})
    log(f"Objects: {len(instances)} from {len(dets)} lifted detections")
    return instances, info
