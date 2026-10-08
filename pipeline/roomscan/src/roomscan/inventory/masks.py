"""The segmentation stage: a mask for each prompt (a photo and a box), cached so only new prompts cost GPU time.

For every prompt the photo's crop goes to the segmenter, the mask is judged (does it look like an
object of about the box's size sitting where the box says), pushed onto the photo's stored point
map, and the result is kept: which stored pixels, a sample of the object's own pixel colours at full
resolution, and a small picture of the crop with the mask outlined, for the reviewer.

The cache is one ``.npz`` per session under ``captures/<room>/sessions/<id>/inventory/`` with no
pickled objects; a prompt is identified by its photo and its box rounded to half a stored pixel.
"""

from __future__ import annotations

import io
import json
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable

import numpy as np
from PIL import Image
from scipy import ndimage

from ..paths import OutputGuard
from ..scene import Scene
from .photos import open_photo, photo_to_stored_xy, stored_to_photo, stored_to_photo_xy
from .segment import SEGMENTER, crop_for_box, mask_on_stored_pixels

VERSION = 2  # 2: the stored mask covers the whole crop, not just the prompt box
MAX_PIXEL_SAMPLES = 1500
THUMB_SIDE = 200
MIN_AREA_FRACTION, MAX_AREA_FRACTION = 0.08, 1.9


@dataclass
class MaskResult:
    view: int
    box_stored: tuple[float, float, float, float]
    accepted: bool
    reason: str
    mask: np.ndarray  # (h, w) bool on the stored point map
    pixels: np.ndarray  # (K, 3) uint8 colours of mask pixels at full resolution
    thumb: bytes  # JPEG of the crop with the mask outlined

    @property
    def key(self) -> str:
        return prompt_key(self.view, self.box_stored)


def prompt_key(view: int, box: tuple[float, float, float, float]) -> str:
    return f"{view}:" + ",".join(str(round(c * 2) / 2) for c in box)


def judge_mask(mask: np.ndarray, box_in_crop: tuple[float, float, float, float]) -> tuple[bool, str]:
    """Does a mask look like the object the box was drawn round?"""
    x0, y0, x1, y1 = box_in_crop
    box_area = max(1.0, (x1 - x0) * (y1 - y0))
    area = float(mask.sum())
    if area < MIN_AREA_FRACTION * box_area:
        return False, "mask much smaller than the box"
    if area > MAX_AREA_FRACTION * box_area:
        return False, "mask much larger than the box"
    cy, cx = int((y0 + y1) / 2), int((x0 + x1) / 2)
    r = max(2, int(0.12 * max(x1 - x0, y1 - y0)))
    window = mask[max(0, cy - r):cy + r + 1, max(0, cx - r):cx + r + 1]
    if window.size == 0 or window.mean() < 0.35:
        return False, "the box centre is not on the mask"
    ys, xs = np.nonzero(mask)
    ix = max(0.0, min(xs.max(), x1) - max(xs.min(), x0))
    iy = max(0.0, min(ys.max(), y1) - max(ys.min(), y0))
    union = (xs.max() - xs.min() + 1) * (ys.max() - ys.min() + 1) + box_area - ix * iy
    if union <= 0 or ix * iy / union < 0.3:
        return False, "mask and box barely overlap"
    return True, "ok"


def _thumb(crop: Image.Image, mask: np.ndarray, accepted: bool) -> bytes:
    arr = np.asarray(crop).copy()
    edge = mask ^ ndimage.binary_erosion(mask, iterations=max(2, int(max(crop.size) / 250)))
    arr[edge] = (0, 255, 0) if accepted else (255, 0, 0)
    im = Image.fromarray(arr)
    im.thumbnail((THUMB_SIDE, THUMB_SIDE))
    buf = io.BytesIO()
    im.save(buf, format="JPEG", quality=80)
    return buf.getvalue()


class MaskStore:
    def __init__(self, guard: OutputGuard, rel: Path, session_id: str):
        self.guard, self.rel, self.session_id = guard, Path(rel), session_id
        self.results: dict[str, MaskResult] = {}
        self.dirty = False
        self._load()

    @property
    def path(self) -> Path:
        return self.guard.path(self.rel / "masks.npz")

    def _load(self) -> None:
        if not self.path.is_file():
            return
        with np.load(self.path, allow_pickle=False) as data:
            meta = json.loads(bytes(data["meta"]).decode("utf-8"))
            if meta.get("version") != VERSION or meta.get("model") != SEGMENTER or meta.get("session") != self.session_id:
                return
            shape = tuple(meta["shape"])
            for i, m in enumerate(meta["items"]):
                packed = data[f"m{i}"]
                mask = np.unpackbits(packed)[: shape[0] * shape[1]].reshape(shape).astype(bool)
                r = MaskResult(m["view"], tuple(m["box"]), m["accepted"], m["reason"], mask, data[f"p{i}"], bytes(data[f"t{i}"]))
                self.results[r.key] = r

    def save(self) -> None:
        if not self.dirty or not self.results:
            return
        arrays: dict[str, np.ndarray] = {}
        items = []
        shape = next(iter(self.results.values())).mask.shape
        for i, r in enumerate(self.results.values()):
            arrays[f"m{i}"] = np.packbits(r.mask.ravel())
            arrays[f"p{i}"] = r.pixels
            arrays[f"t{i}"] = np.frombuffer(r.thumb, np.uint8)
            items.append({"view": r.view, "box": list(r.box_stored), "accepted": r.accepted, "reason": r.reason})
        meta = {"version": VERSION, "model": SEGMENTER, "session": self.session_id, "shape": list(shape), "items": items}
        arrays["meta"] = np.frombuffer(json.dumps(meta).encode("utf-8"), np.uint8)
        buf = io.BytesIO()
        np.savez_compressed(buf, **arrays)
        self.guard.write_bytes(self.rel / "masks.npz", buf.getvalue())
        self.dirty = False


def segment_prompts(scene: Scene, prompts: dict[int, list[tuple[float, float, float, float]]], store: MaskStore,
                    segmenter_factory: Callable[[], Any], *, log=print) -> dict[str, Any]:
    """Masks for every (photo, box) not already in the store; returns what the stage cost."""
    todo = {v: [b for b in boxes if prompt_key(v, b) not in store.results] for v, boxes in prompts.items()}
    todo = {v: b for v, b in todo.items() if b}
    count = sum(len(b) for b in todo.values())
    info = {"prompts": sum(len(b) for b in prompts.values()), "new": count, "gpu_seconds": 0.0, "peak_allocated_mib": 0.0}
    if not count:
        return info
    segmenter = segmenter_factory()
    t0 = time.perf_counter()
    done = 0
    shape = scene.map_shape
    for view in sorted(todo):
        photo = open_photo(scene, view)
        for box in todo[view]:
            pb = stored_to_photo(scene, view, box)
            if pb[2] - pb[0] < 4 or pb[3] - pb[1] < 4:
                continue
            crop, offset, cbox = crop_for_box(photo, pb)
            mask = segmenter.mask_in_crop(crop, cbox)
            ok, reason = judge_mask(mask, cbox)
            if ok:
                ch, cw = mask.shape
                corners = photo_to_stored_xy(scene, view, np.array([[offset[0], offset[1]], [offset[0] + cw, offset[1]],
                                                                   [offset[0], offset[1] + ch], [offset[0] + cw, offset[1] + ch]], float))
                crop_stored = (float(corners[:, 0].min()), float(corners[:, 1].min()), float(corners[:, 0].max()), float(corners[:, 1].max()))
                stored = mask_on_stored_pixels(mask, offset, lambda xy, v=view: stored_to_photo_xy(scene, v, xy), crop_stored, shape)
            else:
                stored = np.zeros(shape, bool)
            ys, xs = np.nonzero(mask)
            if len(xs) > MAX_PIXEL_SAMPLES:
                pick = np.random.default_rng(view).choice(len(xs), MAX_PIXEL_SAMPLES, replace=False)
                ys, xs = ys[pick], xs[pick]
            pixels = np.asarray(crop)[ys, xs] if ok else np.zeros((0, 3), np.uint8)
            result = MaskResult(view, tuple(box), ok, reason, stored, pixels, _thumb(crop, mask, ok))
            store.results[result.key] = result
            store.dirty = True
            done += 1
        if done and done % 100 < len(todo[view]):
            log(f"  masks {done}/{count}")
    info["gpu_seconds"] = round(getattr(segmenter, "seconds", time.perf_counter() - t0), 1)
    info["peak_allocated_mib"] = round(getattr(segmenter, "peak_mib", 0.0))
    if hasattr(segmenter, "close"):
        segmenter.close()
    store.save()
    return info
