"""Per-photo sharpness, exposure and perceptual hashes, computed on a fixed-size grey copy.

All measures run on the same reduced image (long side ``ANALYSIS_LONG_SIDE``) so scores do not
depend on the camera resolution and stay comparable across phones.
"""

from __future__ import annotations

from dataclasses import dataclass

import cv2
import numpy as np
from PIL import Image

ANALYSIS_LONG_SIDE = 1024
TILE_GRID = 8


@dataclass(frozen=True)
class Thresholds:
    """Flag thresholds. Blur is judged relative to the session as well as absolutely."""

    blur_abs_min: float = 25.0  # tile sharpness below this is blurry whatever the session
    blur_rel_min: float = 0.35  # ... or below this fraction of the session median
    # ... and only when the strongest edges are soft too. Low sharpness alone also catches crisp
    # photos of plain walls, doors and floors. Calibrated by eye on the October 2026 garage set:
    # every motion-blurred photo checked scored 0.351 or less, every crisp plain one 0.353 or more.
    blur_edge_max: float = 0.352
    highlight_level: int = 250
    shadow_level: int = 5
    highlight_clip_max: float = 0.04  # share of pixels at or above highlight_level
    shadow_clip_max: float = 0.20  # share of pixels at or below shadow_level
    dark_mean_max: float = 45.0  # mean luma (0-255) below this is underexposed
    bright_mean_min: float = 215.0
    near_dup_hash_bits: int = 6  # perceptual hash distance for a near-duplicate candidate
    near_dup_rms_max: float = 0.045  # and normalised thumbnail RMS difference below this

    def as_dict(self) -> dict[str, float]:
        return dict(self.__dict__)


def analysis_gray(img: Image.Image) -> np.ndarray:
    """Luma as float32 0-255 at a fixed long side (deterministic Pillow resize)."""
    rgb = img if img.mode == "RGB" else img.convert("RGB")
    w, h = rgb.size
    scale = ANALYSIS_LONG_SIDE / max(w, h)
    if scale < 1:
        rgb = rgb.resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.Resampling.BOX)
    gray = np.asarray(rgb.convert("L"), dtype=np.float32)
    return gray


def sharpness(gray: np.ndarray) -> dict[str, float]:
    """Variance of the Laplacian, whole frame and the mean of the sharpest quarter of tiles.

    The tile score keeps a photo of a plain wall with one crisp edge from reading as blurry,
    while motion blur lowers every tile.
    """
    lap = cv2.Laplacian(gray, cv2.CV_32F, ksize=3)
    whole = float(lap.var())
    h, w = lap.shape
    th, tw = max(1, h // TILE_GRID), max(1, w // TILE_GRID)
    tiles = []
    for r in range(TILE_GRID):
        for c in range(TILE_GRID):
            tile = lap[r * th : (r + 1) * th, c * tw : (c + 1) * tw]
            if tile.size:
                tiles.append(float(tile.var()))
    tiles.sort(reverse=True)
    top = tiles[: max(1, len(tiles) // 4)]
    return {"laplacian_var": round(whole, 3), "sharpness": round(float(np.mean(top)), 3)}


def edge_sharpness(gray: np.ndarray, grid: int = TILE_GRID, top: float = 0.25) -> float:
    """Contrast-independent crispness of the strongest edges (about 0.2 smeared to 0.5 crisp).

    For an edge of contrast c spread over w pixels the gradient peaks near c/w and the Laplacian
    near c/w^2, so their ratio falls as 1/w whatever the contrast. A plain wall with a few crisp
    edges keeps a high ratio although its Laplacian variance is low; motion blur lowers both.
    Measured per tile on the strongest gradients, then the median over the quarter of tiles with
    the strongest edges.
    """
    g = cv2.GaussianBlur(gray, (0, 0), 0.8)
    gx = cv2.Sobel(g, cv2.CV_32F, 1, 0, ksize=3)
    gy = cv2.Sobel(g, cv2.CV_32F, 0, 1, ksize=3)
    mag = cv2.magnitude(gx, gy)
    lap = np.abs(cv2.Laplacian(g, cv2.CV_32F, ksize=3))
    h, w = g.shape
    th, tw = max(1, h // grid), max(1, w // grid)
    rows: list[tuple[float, float]] = []
    for r in range(grid):
        for c in range(grid):
            m = mag[r * th : (r + 1) * th, c * tw : (c + 1) * tw]
            lp = lap[r * th : (r + 1) * th, c * tw : (c + 1) * tw]
            if m.size == 0:
                continue
            strong = m >= max(float(np.percentile(m, 95)), 20.0)
            if strong.sum() < 30:
                continue  # no real edge in this tile
            ratio = float(np.percentile(lp, 99) / (np.percentile(m, 99) + 1e-6))
            rows.append((float(m[strong].mean()), ratio))
    if not rows:
        return 0.0
    rows.sort(key=lambda x: -x[0])
    best = rows[: max(1, int(len(rows) * top))]
    return round(float(np.median([ratio for _, ratio in best])), 4)


def exposure(gray: np.ndarray, t: Thresholds = Thresholds()) -> dict[str, float]:
    n = gray.size
    return {
        "mean_luma": round(float(gray.mean()), 3),
        "highlight_clip": round(float((gray >= t.highlight_level).sum()) / n, 5),
        "shadow_clip": round(float((gray <= t.shadow_level).sum()) / n, 5),
        "p01": round(float(np.percentile(gray, 1)), 2),
        "p99": round(float(np.percentile(gray, 99)), 2),
    }


def phash(gray: np.ndarray) -> int:
    """64-bit DCT perceptual hash."""
    small = cv2.resize(gray, (32, 32), interpolation=cv2.INTER_AREA).astype(np.float32)
    dct = cv2.dct(small)[:8, :8]
    flat = dct.flatten()
    median = np.median(flat[1:])
    bits = 0
    for value in flat:
        bits = (bits << 1) | int(value > median)
    return bits


def thumb_vector(gray: np.ndarray) -> np.ndarray:
    """A 32x24 (or 24x32) grey thumbnail (0-1) for near-duplicate confirmation."""
    h, w = gray.shape
    size = (32, 24) if w >= h else (24, 32)
    small = cv2.resize(gray, size, interpolation=cv2.INTER_AREA).astype(np.float32) / 255.0
    return small


def hamming(a: int, b: int) -> int:
    return (a ^ b).bit_count()


def thumb_rms(a: np.ndarray, b: np.ndarray) -> float:
    """Root-mean-square difference of two grey thumbnails (0-1); brightness differences count."""
    if a.shape != b.shape:
        return 1.0
    return float(np.sqrt(np.mean((a - b) ** 2)))


def flag_photos(records: list[dict], t: Thresholds = Thresholds()) -> float:
    """Add quality flags in place; returns the session median sharpness used for blur."""
    values = [r["quality"]["sharpness"] for r in records if r.get("quality")]
    median = float(np.median(values)) if values else 0.0
    blur_floor = max(t.blur_abs_min, t.blur_rel_min * median)
    for r in records:
        q = r.get("quality")
        if not q:
            continue
        flags = []
        soft_edges = q.get("edge_sharpness") is None or q["edge_sharpness"] < t.blur_edge_max
        if q["sharpness"] < blur_floor and soft_edges:
            flags.append("blurry")
        elif q["sharpness"] < blur_floor:
            flags.append("low_detail")  # plain surface with crisp edges: kept, not blurry
        if q["highlight_clip"] > t.highlight_clip_max:
            flags.append("highlights_clipped")
        if q["shadow_clip"] > t.shadow_clip_max:
            flags.append("shadows_crushed")
        if q["mean_luma"] < t.dark_mean_max:
            flags.append("underexposed")
        elif q["mean_luma"] > t.bright_mean_min:
            flags.append("overexposed")
        q["flags"] = flags
        q["sharpness_vs_median"] = round(q["sharpness"] / median, 3) if median else None
    return round(median, 3)
