"""Image decoding helpers shared by every stage."""

from __future__ import annotations

_registered = False


def register_heif() -> None:
    """Teach Pillow to open HEIC/HEIF (iPhone photos). Safe to call repeatedly, in any process."""
    global _registered
    if _registered:
        return
    import pillow_heif

    pillow_heif.register_heif_opener()
    _registered = True
