"""Synthetic photos for tests. No real photo is ever used in a test."""

from __future__ import annotations

import io
from functools import lru_cache
from pathlib import Path

import numpy as np
from PIL import Image

# A made-up location, so a leak is easy to spot in bytes.
FAKE_LAT = (48.0, 51.0, 29.59)
FAKE_LON = (2.0, 17.0, 40.12)
FAKE_XMP = (
    b'<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">'
    b'<rdf:Description xmlns:exif="http://ns.adobe.com/exif/1.0/" exif:GPSLatitude="48,51.4959N" '
    b'exif:GPSLongitude="2,17.4012E"/></rdf:RDF></x:xmpmeta>'
)


@lru_cache(maxsize=8)
def canvas(seed: int = 0, size: tuple[int, int] = (1600, 1200)) -> np.ndarray:
    """A textured 'room' picture: gradients, rectangles, discs and fine noise."""
    rng = np.random.default_rng(seed)
    w, h = size
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    img = np.stack([80 + 60 * x / w, 90 + 50 * y / h, 110 + 30 * (x + y) / (w + h)], axis=-1)
    for _ in range(60):
        x0, y0 = rng.integers(0, w - 40), rng.integers(0, h - 40)
        rw, rh = rng.integers(20, 200), rng.integers(20, 200)
        img[y0 : y0 + rh, x0 : x0 + rw] = rng.integers(20, 235, size=3)
    for _ in range(40):
        cx, cy, r = rng.integers(0, w), rng.integers(0, h), rng.integers(8, 60)
        mask = (x - cx) ** 2 + (y - cy) ** 2 < r * r
        img[mask] = rng.integers(20, 235, size=3)
    img += rng.normal(0, 6, size=img.shape)
    out = np.clip(img, 0, 255).astype(np.uint8)
    out.setflags(write=False)
    return out


def view(seed: int = 0, offset: tuple[int, int] = (0, 0), size: tuple[int, int] = (640, 480)) -> Image.Image:
    """A 'photo' cropped from the canvas; different offsets are different viewpoints."""
    big = canvas(seed)
    ox, oy = offset
    w, h = size
    return Image.fromarray(big[oy : oy + h, ox : ox + w])


def exif_with_gps(*, orientation: int = 1, model: str = "TestPhone 1") -> Image.Exif:
    exif = Image.Exif()
    exif[0x010F] = "TestMaker"
    exif[0x0110] = model
    exif[0x0112] = orientation
    exif[0x0132] = "2026:10:04 07:56:46"
    exif[0x8769] = {
        0x829A: 1 / 40,  # ExposureTime
        0x829D: 1.6,  # FNumber
        0x8827: 500,  # ISO
        0x9003: "2026:10:04 07:56:46",
        0x9011: "-05:00",
        0x9291: "123",
        0x920A: 5.96,  # FocalLength
        0xA405: 26,  # 35 mm equivalent
        0xA434: "TestPhone back camera 5.96mm f/1.6",
        0x927C: b"MAKERNOTE-SECRET-48.8582N",  # maker note must not survive
    }
    exif[0x8825] = {1: "N", 2: FAKE_LAT, 3: "E", 4: FAKE_LON, 6: 35.0}
    return exif


def save_jpeg(img: Image.Image, path: Path, *, exif: Image.Exif | None = None, xmp: bytes | None = None,
              quality: int = 95) -> Path:
    kwargs = {"quality": quality}
    if exif is not None:
        kwargs["exif"] = exif.tobytes()
    if xmp is not None:
        kwargs["xmp"] = xmp
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, "JPEG", **kwargs)
    return path


def save_heic(img: Image.Image, path: Path, *, exif: Image.Exif | None = None, xmp: bytes | None = None) -> Path:
    import pillow_heif

    pillow_heif.register_heif_opener()
    kwargs = {"quality": 90}
    if exif is not None:
        kwargs["exif"] = exif.tobytes()
    if xmp is not None:
        kwargs["xmp"] = xmp
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, "HEIF", **kwargs)
    return path


def blurred(img: Image.Image, radius: float = 6) -> Image.Image:
    from PIL import ImageFilter

    return img.filter(ImageFilter.GaussianBlur(radius))


def jpeg_bytes(img: Image.Image, quality: int) -> bytes:
    buf = io.BytesIO()
    img.save(buf, "JPEG", quality=quality)
    return buf.getvalue()
