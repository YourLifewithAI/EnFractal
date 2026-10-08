"""Broad colours from pixels: the few colours a surface or an object is mostly made of.

The shell takes one base colour per surface and the inventory a few per object, as ``#rrggbb``.
Colours are clustered in CIELAB (distance there is close to how different two colours look), the
largest cluster's median is the surface's colour, and tiny clusters are dropped. Phone photos are
auto-exposed under mixed lamps, so these are *broad* colours (a warm grey wall, a grey-blue couch)
and the stylised look reads them as hints, never as paint samples.
"""

from __future__ import annotations

import numpy as np


def srgb_to_lab(rgb: np.ndarray) -> np.ndarray:
    """sRGB (0..255, shape (N, 3)) to CIELAB (D65)."""
    c = np.asarray(rgb, np.float64) / 255.0
    lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    m = np.array([[0.4124564, 0.3575761, 0.1804375], [0.2126729, 0.7151522, 0.0721750], [0.0193339, 0.1191920, 0.9503041]])
    xyz = lin @ m.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 216 / 24389, np.cbrt(xyz), (24389 / 27 * xyz + 16) / 116)
    return np.stack([116 * f[:, 1] - 16, 500 * (f[:, 0] - f[:, 1]), 200 * (f[:, 1] - f[:, 2])], -1)


def hex_of(rgb) -> str:
    r, g, b = (int(round(float(np.clip(v, 0, 255)))) for v in rgb)
    return f"#{r:02x}{g:02x}{b:02x}"


def rgb_of(hex_colour: str) -> np.ndarray:
    return np.array([int(hex_colour[i:i + 2], 16) for i in (1, 3, 5)], float)


def dominant_colours(rgb: np.ndarray, k: int = 3, *, min_fraction: float = 0.08, seed: int = 0,
                     max_samples: int = 20000) -> list[tuple[str, float]]:
    """Up to ``k`` colours as (``#rrggbb``, share of the pixels), largest first.

    Deterministic: a fixed seed and farthest-point initialisation. A cluster smaller than
    ``min_fraction`` is folded away, so a handful of stray pixels never become a colour.
    """
    rgb = np.asarray(rgb, np.float64).reshape(-1, 3)
    if len(rgb) == 0:
        return []
    if len(rgb) > max_samples:
        rgb = rgb[np.random.default_rng(seed).choice(len(rgb), max_samples, replace=False)]
    lab = srgb_to_lab(rgb)
    k = max(1, min(k, len(np.unique(rgb, axis=0))))
    centres = [lab[int(np.argmin(np.linalg.norm(lab - np.median(lab, 0), axis=1)))]]
    while len(centres) < k:
        d = np.min(np.stack([np.linalg.norm(lab - c, axis=1) for c in centres]), axis=0)
        centres.append(lab[int(np.argmax(d))])
    centres_arr = np.stack(centres)
    label = np.zeros(len(lab), int)
    for _ in range(25):
        dist = np.stack([np.linalg.norm(lab - c, axis=1) for c in centres_arr])
        label = np.argmin(dist, axis=0)
        new = np.stack([lab[label == j].mean(0) if np.any(label == j) else centres_arr[j] for j in range(k)])
        if np.allclose(new, centres_arr, atol=0.05):
            break
        centres_arr = new
    out = []
    for j in range(k):
        sel = label == j
        share = float(sel.mean())
        if share >= min_fraction:
            out.append((hex_of(np.median(rgb[sel], axis=0)), share))
    out.sort(key=lambda t: -t[1])
    if not out:
        out = [(hex_of(np.median(rgb, axis=0)), 1.0)]
    total = sum(s for _, s in out)
    return [(h, s / total) for h, s in out]


def chroma(hex_colour: str) -> float:
    lab = srgb_to_lab(rgb_of(hex_colour)[None])[0]
    return float(np.hypot(lab[1], lab[2]))


def lightness(hex_colour: str) -> float:
    return float(srgb_to_lab(rgb_of(hex_colour)[None])[0][0])
