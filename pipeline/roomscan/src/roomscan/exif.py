"""EXIF: read the fields the pipeline needs, write a clean copy with no location.

Kept: device (make, model, lens), focal length, exposure (time, f-number, ISO, bias) and the
capture timestamp. Everything else is dropped on write, by allow-list rather than deny-list, so
GPS, maker notes (which can carry location-derived data), XMP, IPTC and unique ids never survive.
"""

from __future__ import annotations

from fractions import Fraction
from typing import Any

from PIL import Image, TiffImagePlugin

# Base IFD
MAKE = 0x010F
MODEL = 0x0110
ORIENTATION = 0x0112
SOFTWARE = 0x0131
DATETIME = 0x0132
EXIF_IFD = 0x8769
GPS_IFD = 0x8825
# Exif IFD
EXPOSURE_TIME = 0x829A
F_NUMBER = 0x829D
ISO = 0x8827
DATETIME_ORIGINAL = 0x9003
OFFSET_TIME_ORIGINAL = 0x9011
EXPOSURE_BIAS = 0x9204
FOCAL_LENGTH = 0x920A
SUBSEC_ORIGINAL = 0x9291
FOCAL_35MM = 0xA405
DIGITAL_ZOOM = 0xA404
LENS_MAKE = 0xA433
LENS_MODEL = 0xA434

KEEP_BASE = (MAKE, MODEL, SOFTWARE, DATETIME)
KEEP_EXIF = (
    EXPOSURE_TIME,
    F_NUMBER,
    ISO,
    DATETIME_ORIGINAL,
    OFFSET_TIME_ORIGINAL,
    EXPOSURE_BIAS,
    FOCAL_LENGTH,
    SUBSEC_ORIGINAL,
    FOCAL_35MM,
    DIGITAL_ZOOM,
    LENS_MAKE,
    LENS_MODEL,
)

# Byte patterns that would betray location data in a written file (EXIF GPS tag names appear in XMP).
LOCATION_MARKERS = (b"GPSLatitude", b"GPSLongitude", b"exif:GPS", b"GPSPosition", b"GPSAltitude")


def _num(value: Any) -> float | None:
    if value is None:
        return None
    try:
        if isinstance(value, tuple) and len(value) == 2:
            return float(value[0]) / float(value[1]) if value[1] else None
        out = float(value)
    except (TypeError, ValueError, ZeroDivisionError):
        return None
    if out != out or out in (float("inf"), float("-inf")):
        return None
    return out


def _text(value: Any) -> str | None:
    if value is None:
        return None
    if isinstance(value, bytes):
        value = value.decode("utf-8", "replace")
    text = str(value).replace("\x00", "").strip()
    return text or None


def _exposure_fraction(seconds: float | None) -> str | None:
    if not seconds or seconds <= 0:
        return None
    if seconds >= 1:
        return f"{seconds:g}"
    return f"1/{round(1 / seconds)}"


def read_fields(img: Image.Image) -> dict[str, Any]:
    """The allow-listed fields as plain JSON values (never any location)."""
    exif = img.getexif()
    sub = exif.get_ifd(EXIF_IFD)
    exposure = _num(sub.get(EXPOSURE_TIME))
    focal = _num(sub.get(FOCAL_LENGTH))
    f35 = _num(sub.get(FOCAL_35MM))
    fields = {
        "make": _text(exif.get(MAKE)),
        "model": _text(exif.get(MODEL)),
        "software": _text(exif.get(SOFTWARE)),
        "lens_make": _text(sub.get(LENS_MAKE)),
        "lens_model": _text(sub.get(LENS_MODEL)),
        "focal_length_mm": round(focal, 3) if focal else None,
        "focal_length_35mm": int(round(f35)) if f35 else None,
        "digital_zoom": round(_num(sub.get(DIGITAL_ZOOM)) or 0, 3) or None,
        "exposure_time_s": round(exposure, 6) if exposure else None,
        "exposure_time": _exposure_fraction(exposure),
        "f_number": round(_num(sub.get(F_NUMBER)) or 0, 2) or None,
        "iso": int(_num(sub.get(ISO))) if _num(sub.get(ISO)) else None,
        "exposure_bias_ev": round(_num(sub.get(EXPOSURE_BIAS)), 2) if _num(sub.get(EXPOSURE_BIAS)) is not None else None,
        "datetime_original": _text(sub.get(DATETIME_ORIGINAL)) or _text(exif.get(DATETIME)),
        "offset_time_original": _text(sub.get(OFFSET_TIME_ORIGINAL)),
        "subsec_original": _text(sub.get(SUBSEC_ORIGINAL)),
        "orientation": int(exif.get(ORIENTATION) or 1),
    }
    return fields


def lens_kind(fields: dict[str, Any] | None) -> str:
    """'front' (selfie camera), 'ultra_wide' (under 20 mm equivalent), 'main', 'zoom' or 'unknown'."""
    fields = fields or {}
    lens = (fields.get("lens_model") or "").lower()
    f35 = fields.get("focal_length_35mm")
    if "front" in lens:
        return "front"
    if not f35:
        return "unknown"
    if f35 < 20:
        return "ultra_wide"
    if f35 > 35:
        return "zoom"
    return "main"


# A photo is "digitally zoomed" above this DigitalZoomRatio. iPhones write 1.001 for an unzoomed
# photo, so 1.05 is clear of rounding and below the smallest real zoom in the garage set (1.09).
DIGITAL_ZOOM_LIMIT = 1.05


def digitally_zoomed(fields: dict[str, Any] | None) -> bool:
    """Whether the camera zoomed in (a crop of the sensor) when the photo was taken.

    What an iPhone records is not consistent. In the October 2026 garage set (iPhone 17, main
    camera, 5.96 mm lens) eight photos have DigitalZoomRatio 1.42 yet keep a 35 mm equivalent of
    26 mm, the same as unzoomed ones, while the one photo at 1.71 records 44 mm, which is 26 mm times
    the zoom. So the equivalent focal length cannot be trusted to include the zoom, and a photo that
    has one is not given EXIF intrinsics (see ``coverage.backend.exif_intrinsics``).
    """
    return float((fields or {}).get("digital_zoom") or 1.0) > DIGITAL_ZOOM_LIMIT


def had_location(img: Image.Image) -> bool:
    """Whether the source carried any GPS data (reported as a yes/no, never the values)."""
    exif = img.getexif()
    if exif.get_ifd(GPS_IFD):
        return True
    xmp = img.info.get("xmp") or b""
    if isinstance(xmp, str):
        xmp = xmp.encode("utf-8", "replace")
    return any(marker in xmp for marker in LOCATION_MARKERS)


def _rational(value: float) -> Fraction:
    return Fraction(value).limit_denominator(1_000_000)


def clean_exif(img: Image.Image) -> Image.Exif:
    """A fresh EXIF block holding only the allow-listed tags; orientation is normalised to 1."""
    src = img.getexif()
    src_sub = src.get_ifd(EXIF_IFD)
    out = Image.Exif()
    for tag in KEEP_BASE:
        value = src.get(tag)
        if value is not None:
            out[tag] = value
    out[ORIENTATION] = 1
    sub: dict[int, Any] = {}
    for tag in KEEP_EXIF:
        value = src_sub.get(tag)
        if value is None:
            continue
        if isinstance(value, float):
            value = TiffImagePlugin.IFDRational(_rational(value))
        sub[tag] = value
    if sub:
        out[EXIF_IFD] = sub
    return out


def assert_no_location(data: bytes, where: str = "output") -> None:
    """Fail closed if written bytes contain a GPS IFD or GPS markers."""
    import io

    for marker in LOCATION_MARKERS:
        if marker in data:
            raise RuntimeError(f"Location marker {marker!r} found in {where}; refusing to keep it.")
    with Image.open(io.BytesIO(data)) as check:
        if check.getexif().get_ifd(GPS_IFD):
            raise RuntimeError(f"GPS IFD found in {where}; refusing to keep it.")
        if check.info.get("xmp"):
            raise RuntimeError(f"XMP found in {where}; refusing to keep it.")
