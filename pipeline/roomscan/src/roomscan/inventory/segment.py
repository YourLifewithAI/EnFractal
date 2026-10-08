"""Object masks from detector boxes (SAM 2.1 small, Apache-2.0), run on crops of the full-size photo.

A detector box says where something is; the mask says which pixels are it. The mask is what makes
a small object's points and colours its own and not its table's: a 9 cm jar is a handful of
pixels in a whole-photo mask but fills a crop. Each box is cut out with some surround, the crop is
what SAM sees, and the mask comes back in the original photo's pixels.
"""

from __future__ import annotations

import time
from typing import Any

import numpy as np
from PIL import Image

from ..coverage.backend import MODEL_REGISTRY

SEGMENTER = "facebook/sam2.1-hiera-small"


class Segmenter:
    def __init__(self, log=print):
        import torch
        from transformers import Sam2Model, Sam2Processor

        self.torch = torch
        rev = MODEL_REGISTRY[SEGMENTER]["revision"]
        self.processor = Sam2Processor.from_pretrained(SEGMENTER, revision=rev)
        self.model = Sam2Model.from_pretrained(SEGMENTER, revision=rev).to("cuda").eval()
        self.seconds = 0.0
        self.peak_mib = 0.0
        self.calls = 0
        self.log = log

    def mask_in_crop(self, crop: Image.Image, box: tuple[float, float, float, float]) -> np.ndarray:
        """A boolean mask (crop height x width) for the object inside ``box`` (crop pixels)."""
        torch = self.torch
        inputs = self.processor(images=crop, input_boxes=[[list(map(float, box))]], return_tensors="pt").to("cuda")
        torch.cuda.reset_peak_memory_stats()
        t0 = time.perf_counter()
        with torch.no_grad():
            outputs = self.model(**inputs, multimask_output=False)
        masks = self.processor.post_process_masks(outputs.pred_masks.cpu(), inputs["original_sizes"])[0]
        torch.cuda.synchronize()
        self.seconds += time.perf_counter() - t0
        self.peak_mib = max(self.peak_mib, torch.cuda.max_memory_allocated() / 2**20)
        self.calls += 1
        return masks[0, 0].numpy().astype(bool)

    def close(self) -> None:
        self.model = None
        self.torch.cuda.empty_cache()


def crop_for_box(image: Image.Image, box: tuple[float, float, float, float], *, surround: float = 0.4,
                 min_side: int = 96) -> tuple[Image.Image, tuple[int, int], tuple[float, float, float, float]]:
    """The photo around a box with ``surround`` of its size on every side: (crop, its top-left in the photo, the box in crop pixels)."""
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    side = max(w, h)
    pad_x, pad_y = max(surround * w, (min_side - w) / 2 if w < min_side else 0), max(surround * h, (min_side - h) / 2 if h < min_side else 0)
    cx0, cy0 = max(0, int(x0 - pad_x)), max(0, int(y0 - pad_y))
    cx1, cy1 = min(image.width, int(x1 + pad_x) + 1), min(image.height, int(y1 + pad_y) + 1)
    crop = image.crop((cx0, cy0, cx1, cy1))
    return crop, (cx0, cy0), (x0 - cx0, y0 - cy0, x1 - cx0, y1 - cy0)


def mask_on_stored_pixels(mask: np.ndarray, offset: tuple[int, int], stored_to_photo: Any,
                          box_stored: tuple[float, float, float, float], shape: tuple[int, int]) -> np.ndarray:
    """A crop mask as a boolean grid on the stored point map, over the stored pixels around a box.

    Each stored pixel is sampled at nine points inside it, found through ``stored_to_photo`` (stored pixels
    (N, 2) to photo pixels); it is in the mask when at least half of them are. Pixels outside the box's
    surround are False.
    """
    x0, y0, x1, y1 = box_stored
    ca, cb = max(0, int(np.floor(x0)) - 1), min(shape[1], int(np.ceil(x1)) + 1)
    ra, rb = max(0, int(np.floor(y0)) - 1), min(shape[0], int(np.ceil(y1)) + 1)
    out = np.zeros(shape, bool)
    if cb <= ca or rb <= ra:
        return out
    cc, rr = np.meshgrid(np.arange(ca, cb), np.arange(ra, rb))
    inside = np.zeros(cc.shape, np.float32)
    for dx in (-0.3, 0.0, 0.3):
        for dy in (-0.3, 0.0, 0.3):
            xy = stored_to_photo(np.stack([cc.ravel() + 0.5 + dx, rr.ravel() + 0.5 + dy], -1))
            px = np.floor(xy[:, 0] - offset[0]).astype(int)
            py = np.floor(xy[:, 1] - offset[1]).astype(int)
            ok = (px >= 0) & (px < mask.shape[1]) & (py >= 0) & (py < mask.shape[0])
            hit = np.zeros(len(px), np.float32)
            hit[ok] = mask[py[ok], px[ok]]
            inside += hit.reshape(cc.shape)
    out[ra:rb, ca:cb] = inside >= 4.5
    return out
