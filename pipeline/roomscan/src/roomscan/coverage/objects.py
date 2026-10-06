"""Per-object view counts: open-vocabulary detection, lifted to 3-D with the point maps.

Detection only says *what* and *roughly where*; the view count of an object is geometric: a photo
counts when enough of its surface points fall inside the object's 3-D box. So a photo where the
detector missed the object still counts, and the angles it was seen from are measured, not guessed.

Labels come from a fixed vocabulary, never from text read in a photo.
"""

from __future__ import annotations

import math
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

from .backend import MODEL_REGISTRY, STORE_STRIDE, TARGET_SIZE, processed_geometry

DETECTOR = "google/owlv2-base-patch16-ensemble"

# query -> canonical name. Generic indoor and garage objects; nothing specific to one room.
VOCABULARY: dict[str, str] = {
    "a shelving unit": "shelving unit",
    "a bookshelf": "shelving unit",
    "a cabinet": "cabinet",
    "a chest of drawers": "chest of drawers",
    "a desk": "desk",
    "a table": "table",
    "an office chair": "office chair",
    "a chair": "chair",
    "a bean bag chair": "bean bag",
    "a sofa": "sofa",
    "a bicycle": "bicycle",
    "a computer monitor": "monitor",
    "a television": "television",
    "a door": "door",
    "a window": "window",
    "a garage door": "garage door",
    "a rug": "rug",
    "a painting": "painting",
    "a poster": "painting",
    "a ladder": "ladder",
    "a workbench": "workbench",
    "a trash can": "bin",
    "a portable air conditioner": "air conditioner",
    "a fan": "fan",
    "an easel": "easel",
    "a lamp": "lamp",
    "a cardboard box": "box",
    "a plastic storage box": "storage box",
    "a printer": "printer",
    "a speaker": "speaker",
    "a guitar": "guitar",
    "a refrigerator": "refrigerator",
    "a washing machine": "washing machine",
    "a toolbox": "toolbox",
    "a suitcase": "suitcase",
    "a basket": "basket",
    "a plant": "plant",
    "a mirror": "mirror",
}
# Objects that matter for a room shell or for the 10 cm player even when small.
MIN_SIZE_M = 0.25


@dataclass
class Detection:
    view: int
    label: str
    score: float
    box: tuple[float, float, float, float]  # stored-map pixels x0, y0, x1, y1
    centre: np.ndarray | None = None  # room frame
    lo: np.ndarray | None = None
    hi: np.ndarray | None = None


@dataclass
class ObjectInstance:
    id: str
    label: str
    detections: list[Detection]
    lo: np.ndarray
    hi: np.ndarray
    views: list[int] = field(default_factory=list)
    view_angles_deg: list[float] = field(default_factory=list)
    view_heights_m: list[float] = field(default_factory=list)
    view_distances_m: list[float] = field(default_factory=list)
    front: np.ndarray | None = None  # unit (x, z), from the object toward where it is viewed from
    wall: str | None = None

    @property
    def centre(self) -> np.ndarray:
        return (self.lo + self.hi) / 2

    @property
    def size(self) -> np.ndarray:
        return self.hi - self.lo


class Detector:
    def __init__(self, threshold: float = 0.22, log=print):
        import torch
        from transformers import Owlv2ForObjectDetection, Owlv2Processor

        self.torch = torch
        rev = MODEL_REGISTRY[DETECTOR]["revision"]
        self.processor = Owlv2Processor.from_pretrained(DETECTOR, revision=rev)
        self.model = Owlv2ForObjectDetection.from_pretrained(DETECTOR, revision=rev).to("cuda").eval()
        self.queries = list(VOCABULARY)
        self.threshold = threshold
        self.seconds = 0.0
        self.peak_mib = 0.0
        self.log = log

    def detect(self, image: Image.Image) -> list[tuple[str, float, tuple[float, float, float, float]]]:
        torch = self.torch
        side = max(image.size)  # OWLv2 pads to a square at the bottom and right
        inputs = self.processor(text=[self.queries], images=image, return_tensors="pt").to("cuda")
        torch.cuda.reset_peak_memory_stats()
        t0 = time.perf_counter()
        with torch.no_grad(), torch.autocast("cuda", dtype=torch.float16):
            outputs = self.model(**inputs)
        result = self.processor.post_process_grounded_object_detection(
            outputs=outputs, threshold=self.threshold, target_sizes=[(side, side)])[0]
        torch.cuda.synchronize()
        self.seconds += time.perf_counter() - t0
        self.peak_mib = max(self.peak_mib, torch.cuda.max_memory_allocated() / 2**20)
        found = []
        for score, label, box in zip(result["scores"].tolist(), result["labels"].tolist(), result["boxes"].tolist()):
            x0, y0, x1, y1 = box
            x0, y0 = max(0.0, x0), max(0.0, y0)
            x1, y1 = min(float(image.width), x1), min(float(image.height), y1)
            if x1 - x0 < 4 or y1 - y0 < 4:
                continue
            found.append((VOCABULARY[self.queries[label]], float(score), (x0, y0, x1, y1)))
        return _nms(found, 0.6)

    def close(self) -> None:
        self.model = None
        self.torch.cuda.empty_cache()


def _iou(a, b) -> float:
    ix = max(0.0, min(a[2], b[2]) - max(a[0], b[0]))
    iy = max(0.0, min(a[3], b[3]) - max(a[1], b[1]))
    inter = ix * iy
    union = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - inter
    return inter / union if union > 0 else 0.0


def _nms(found, iou_max: float):
    found = sorted(found, key=lambda f: -f[1])
    kept = []
    for f in found:
        if all(not (f[0] == k[0] and _iou(f[2], k[2]) > iou_max) for k in kept):
            kept.append(f)
    return kept


def detection_image(path: Path, long_side: int = 768) -> tuple[Image.Image, float]:
    """The photo cropped exactly like the pose model's input, at a size the detector likes.

    Returns the image and the factor from its pixels to the stored point-map pixels.
    """
    with Image.open(path) as im:
        im = im.convert("RGB")
        scale, left, top = processed_geometry(im.width, im.height)
        tw, th = TARGET_SIZE
        crop = (left / scale, top / scale, (left + tw) / scale, (top + th) / scale)
        im = im.crop(tuple(round(c) for c in crop))
        k = long_side / max(tw, th)
        im = im.resize((round(tw * k), round(th * k)), Image.Resampling.LANCZOS)
    return im, 1.0 / (k * STORE_STRIDE)


def lift(det: Detection, pts_room: np.ndarray, valid: np.ndarray, *, shrink: float = 0.25) -> bool:
    """3-D centre and extent of a detection from the points inside its (shrunk) box."""
    x0, y0, x1, y1 = det.box
    w, h = x1 - x0, y1 - y0
    xa, xb = int(x0 + shrink * w), int(math.ceil(x1 - shrink * w))
    ya, yb = int(y0 + shrink * h), int(math.ceil(y1 - shrink * h))
    inner = pts_room[ya:yb, xa:xb][valid[ya:yb, xa:xb]]
    full = pts_room[int(y0):int(math.ceil(y1)), int(x0):int(math.ceil(x1))][valid[int(y0):int(math.ceil(y1)), int(x0):int(math.ceil(x1))]]
    if len(inner) < 20 or len(full) < 40:
        return False
    centre = np.median(inner, axis=0)
    # Keep the points of the box that sit near the object's depth, not the wall behind it.
    near = full[np.linalg.norm(full - centre, axis=1) < max(0.6, 1.5 * np.linalg.norm(np.percentile(inner, 90, 0) - np.percentile(inner, 10, 0)))]
    if len(near) < 20:
        near = inner
    det.centre = centre
    det.lo = np.percentile(near, 8, axis=0)
    det.hi = np.percentile(near, 92, axis=0)
    return True


def _footprint_overlap(a_lo, a_hi, b_lo, b_hi) -> float:
    """Intersection over the smaller footprint, in the floor plane (x, z)."""
    ix = max(0.0, min(a_hi[0], b_hi[0]) - max(a_lo[0], b_lo[0]))
    iz = max(0.0, min(a_hi[2], b_hi[2]) - max(a_lo[2], b_lo[2]))
    area_a = max(1e-4, (a_hi[0] - a_lo[0]) * (a_hi[2] - a_lo[2]))
    area_b = max(1e-4, (b_hi[0] - b_lo[0]) * (b_hi[2] - b_lo[2]))
    return ix * iz / min(area_a, area_b)


def cluster(dets: list[Detection], *, link_m: float = 0.45, overlap_min: float = 0.35) -> list[list[Detection]]:
    """Same-label detections that overlap in the floor plane or sit close together are one object."""
    groups: list[list[Detection]] = []
    by_label: dict[str, list[Detection]] = {}
    for d in dets:
        if d.centre is not None:
            by_label.setdefault(d.label, []).append(d)
    for label, items in by_label.items():
        parent = list(range(len(items)))

        def find(i):
            while parent[i] != i:
                parent[i] = parent[parent[i]]
                i = parent[i]
            return i

        for i in range(len(items)):
            for j in range(i + 1, len(items)):
                a, b = items[i], items[j]
                close = np.linalg.norm((a.centre - b.centre)[[0, 2]]) < link_m and abs(a.centre[1] - b.centre[1]) < 0.6
                if close or _footprint_overlap(a.lo, a.hi, b.lo, b.hi) > overlap_min:
                    parent[find(j)] = find(i)
        sets: dict[int, list[Detection]] = {}
        for i, d in enumerate(items):
            sets.setdefault(find(i), []).append(d)
        groups.extend(sets.values())
    return groups


def build_instances(groups: list[list[Detection]], *, min_detections: int = 2) -> list[ObjectInstance]:
    out = []
    counters: dict[str, int] = {}
    groups = sorted(groups, key=lambda g: (-len({d.view for d in g}), g[0].label))
    for g in groups:
        views = {d.view for d in g}
        best = max(d.score for d in g)
        if len(views) < min_detections and best < 0.4:
            continue  # one weak sighting: more likely a false detection than an object
        lo = np.percentile(np.stack([d.lo for d in g]), 25, axis=0)
        hi = np.percentile(np.stack([d.hi for d in g]), 75, axis=0)
        size = hi - lo
        if max(size[0], size[2], size[1]) < MIN_SIZE_M:
            continue
        label = g[0].label
        counters[label] = counters.get(label, 0) + 1
        out.append(ObjectInstance(f"{label.replace(' ', '_')}_{counters[label]}", label, g, lo, hi))
    # Number only labels that occur more than once.
    totals: dict[str, int] = {}
    for inst in out:
        totals[inst.label] = totals.get(inst.label, 0) + 1
    for inst in out:
        if totals[inst.label] == 1:
            inst.id = inst.label.replace(" ", "_")
    return out


def measure_views(inst: ObjectInstance, pts_by_view: dict[int, np.ndarray], cams: dict[int, np.ndarray],
                  room_centre: np.ndarray, wall_of: dict[str, Any] | None, *, min_points: int = 60,
                  margin: float = 0.05) -> None:
    """Which photos saw the object (points inside its box), and from what angle, height and distance."""
    lo, hi = inst.lo - margin, inst.hi + margin
    views = []
    for v, pts in pts_by_view.items():
        inside = np.all((pts >= lo) & (pts <= hi), axis=1)
        if inside.sum() >= min_points:
            views.append(v)
    inst.views = sorted(views)
    c = inst.centre
    if wall_of is not None:
        inst.wall = wall_of["key"]
        inst.front = np.asarray(wall_of["inward"], float)
    else:
        if inst.views:
            mean_dir = np.mean([cams[v][[0, 2]] - c[[0, 2]] for v in inst.views], axis=0)
        else:
            mean_dir = room_centre[[0, 2]] - c[[0, 2]]
        n = np.linalg.norm(mean_dir)
        inst.front = mean_dir / n if n > 1e-6 else np.array([0.0, 1.0])
    f = inst.front
    left = np.array([-f[1], f[0]])  # viewer faces -f; with +Y up their left is (-f_z, f_x)
    angles, heights, dists = [], [], []
    for v in inst.views:
        d = cams[v][[0, 2]] - c[[0, 2]]
        angles.append(float(np.degrees(np.arctan2(d @ left, d @ f))))
        heights.append(float(cams[v][1]))
        dists.append(float(np.linalg.norm(cams[v] - c)))
    inst.view_angles_deg, inst.view_heights_m, inst.view_distances_m = angles, heights, dists


SECTORS = [  # (name, from_deg, to_deg); positive angles are toward the viewer's left
    ("front", -25, 25),
    ("front-left", 25, 70),
    ("left side", 70, 115),
    ("front-right", -70, -25),
    ("right side", -115, -70),
    ("back", 115, 245),
]


def sector_of(angle: float) -> str:
    a = (angle + 180) % 360 - 180
    for name, lo, hi in SECTORS[:-1]:
        if lo <= a < hi:
            return name
    return "back"
