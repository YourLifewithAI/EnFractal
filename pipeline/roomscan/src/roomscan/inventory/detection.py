"""2-D detections for the inventory: the inventory vocabulary over every fitted photo, whole and tiled.

The coverage run asked OWLv2 for furniture on whole photos at 768 px. The inventory also wants
small things (a laptop, a jar, a cooler), so each photo is asked twice: whole, at 768 px, and as a
2 x 2 grid of overlapping tiles cut from a 1536 px copy, which gives a thing a third of the width
of the picture twice the detector's pixels. Boxes are stored in point-map pixels (the same
coordinates as the stored point maps), so a box lifts to 3-D with the pose's own point map.

The detector is never trusted on its own: ``cluster`` needs the same kind of thing at the same
place in several photos, and the reviewer (a person or a vision model) checks what is left.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Callable

from ..coverage.backend import MODEL_REGISTRY, wait_for_vram
from ..coverage.objects import DETECTOR, Detector, detection_image
from ..jsonio import json_bytes, sha256_hex
from ..paths import OutputGuard
from .vocabulary import QUERY_KIND

THRESHOLD = 0.14
LONG_SIDE_WHOLE = 768
LONG_SIDE_TILES = 1536
TILE_GRID = (2, 2)
VERSION = 1


def cache_key(views: list[dict[str, Any]], indices: list[int], vocabulary: dict[str, str], name: str = DETECTOR) -> str:
    return sha256_hex(json_bytes({
        "views": [[views[i]["name"], views[i]["sha256"]] for i in indices],
        "detector": name, "revision": MODEL_REGISTRY[DETECTOR]["revision"], "vocabulary": vocabulary,
        "threshold": THRESHOLD, "long_sides": [LONG_SIDE_WHOLE, LONG_SIDE_TILES], "grid": list(TILE_GRID),
        "version": VERSION}))


def run_inventory_detector(guard: OutputGuard, rel: Path, room_dir: Path, views: list[dict[str, Any]],
                           indices: list[int], *, detector_factory: Callable[[], Any] | None = None,
                           vocabulary: dict[str, str] | None = None, log=print
                           ) -> tuple[dict[int, list[list[Any]]], dict[str, Any]]:
    """Detections per photo as ``[kind, score, [x0, y0, x1, y1]]`` in stored point-map pixels, cached.

    ``detector_factory`` replaces the GPU detector in tests; its ``detect`` and ``detect_tiled`` take an
    image and return (kind, score, box in that image's pixels).
    """
    vocabulary = dict(QUERY_KIND if vocabulary is None else vocabulary)
    key = cache_key(views, indices, vocabulary, "injected" if detector_factory else DETECTOR)
    cache = guard.path(rel / "detections.json")
    if cache.is_file():
        data = json.loads(cache.read_bytes())
        if data.get("key") == key:
            return {int(k): v for k, v in data["detections"].items()}, data["info"] | {"reused_from_cache": True}
    if detector_factory is None:
        wait_for_vram(1500, log=log)
        det = Detector(threshold=THRESHOLD, log=log, vocabulary=vocabulary)
    else:
        det = detector_factory()
    out: dict[int, list[list[Any]]] = {}
    for n, i in enumerate(indices, 1):
        path = room_dir / views[i]["derived"]["jpeg"]
        whole, f_whole = detection_image(path, LONG_SIDE_WHOLE)
        found = [[k, round(s, 4), [round(c * f_whole, 2) for c in b]] for k, s, b in det.detect(whole, photo=views[i])]
        big, f_big = detection_image(path, LONG_SIDE_TILES)
        found += [[k, round(s, 4), [round(c * f_big, 2) for c in b]]
                  for k, s, b in det.detect_tiled(big, grid=TILE_GRID, photo=views[i])]
        out[i] = found
        if n % 25 == 0:
            log(f"  inventory detections in {n}/{len(indices)} photos")
    info = {"detector": "injected" if detector_factory else DETECTOR, "threshold": THRESHOLD, "photos": len(indices),
            "queries": len(vocabulary), "gpu_seconds": round(getattr(det, "seconds", 0.0), 2),
            "peak_allocated_mib": round(getattr(det, "peak_mib", 0.0)), "reused_from_cache": False,
            "detections": sum(len(v) for v in out.values())}
    if hasattr(det, "close"):
        det.close()
    guard.write_bytes(rel / "detections.json", json_bytes({"key": key, "info": info,
                                                           "detections": {str(k): v for k, v in out.items()}}))
    return out, info

