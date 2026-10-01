from pathlib import Path

import numpy as np
from PIL import Image

from mapbuilder.photo import read_evidence


EVIDENCE = Path("maps/barton_creek/photo_pilot")
OVERLAY = Path("game/maps/barton_creek/photo_overlay.png")


def _mask_at(image: np.ndarray, x: float, z: float) -> np.ndarray:
    column = round((x + 2048) / 4096 * 1023)
    row = round((z + 2048) / 4096 * 1023)
    return image[row, column]


def test_curated_photo_evidence_is_pinned_and_sampleable() -> None:
    evidence, samples = read_evidence(EVIDENCE)
    assert {photo["id"] for photo in evidence["photos"]} == {
        "greenbelt_limestone_2007", "mall_west_2020"
    }
    assert all(len(color) == 3 and all(0 <= channel <= 255 for channel in color)
               for photo_samples in samples.values() for color in photo_samples.values())
    assert evidence["photos"][0]["location_quality"].endswith("unavailable")
    assert evidence["photos"][1]["camera_heading_deg"] == 90


def test_photo_overlay_targets_mapped_pilot_areas() -> None:
    overlay = np.asarray(Image.open(OVERLAY).convert("RGB"))
    assert _mask_at(overlay, -280, 70)[0] > 50  # Barton Creek bank
    assert _mask_at(overlay, -280, 70)[1] > 100  # nearby mapped trail
    assert _mask_at(overlay, 35, -763)[2] > 200  # west mall parking
    assert np.count_nonzero(_mask_at(overlay, 0, 0)) == 0  # untouched pin
    assert np.count_nonzero(_mask_at(overlay, 1400, 1400)) == 0
