"""Where stored point-map pixels are in the original photo, and crops of the photo for a reviewer.

A stored point-map pixel (column c, row r) is the model-input pixel (2c, 2r); the model input is the
photo scaled and cropped or padded as ``backend.input_layout`` says. This turns a box in stored
pixels into a box in the original photo, so a reviewer (a person or a vision model) can look at
the real pixels of what was found.
"""

from __future__ import annotations

import numpy as np
from PIL import Image, ImageDraw

from ..coverage.backend import STORE_STRIDE, TARGET_SIZE, input_layout
from ..scene import Scene


def stored_to_photo(scene: Scene, view: int, box: tuple[float, float, float, float]) -> tuple[float, float, float, float]:
    """A box in stored point-map pixels to a box in the original photo's pixels (clipped to the photo)."""
    info = scene.views[view]["image"]
    width, height = int(info["width"]), int(info["height"])
    layout = input_layout(width, height, TARGET_SIZE)
    x0, y0, x1, y1 = (c * STORE_STRIDE for c in box)

    def to_x(mx: float) -> float:
        return (mx - layout.pad_x + layout.left) / layout.scale

    def to_y(my: float) -> float:
        return (my - layout.pad_y + layout.top) / layout.scale

    return (float(np.clip(to_x(x0), 0, width)), float(np.clip(to_y(y0), 0, height)),
            float(np.clip(to_x(x1), 0, width)), float(np.clip(to_y(y1), 0, height)))


def photo_to_stored_xy(scene: Scene, view: int, xy: np.ndarray) -> np.ndarray:
    """Original photo pixels (N, 2) to stored point-map pixels, the inverse of ``stored_to_photo``'s mapping."""
    info = scene.views[view]["image"]
    layout = input_layout(int(info["width"]), int(info["height"]), TARGET_SIZE)
    xy = np.asarray(xy, float)
    mx = xy[..., 0] * layout.scale + layout.pad_x - layout.left
    my = xy[..., 1] * layout.scale + layout.pad_y - layout.top
    return np.stack([mx / STORE_STRIDE, my / STORE_STRIDE], -1)


def open_photo(scene: Scene, view: int, max_side: int | None = None) -> Image.Image:
    im = Image.open(scene.photo_path(view))
    if max_side and max(im.size) > 2 * max_side and im.format == "JPEG":
        im.draft("RGB", (max_side, max_side))
    im = im.convert("RGB")
    if max_side and max(im.size) > max_side:
        k = max_side / max(im.size)
        im = im.resize((round(im.width * k), round(im.height * k)), Image.Resampling.LANCZOS)
    return im


def crop_with_box(scene: Scene, view: int, box: tuple[float, float, float, float], *, pad: float = 0.35,
                  out_side: int = 320, outline=(255, 220, 0), label: str | None = None) -> Image.Image:
    """The photo around a stored-pixel box, with the box drawn on it, scaled to fit ``out_side``."""
    x0, y0, x1, y1 = stored_to_photo(scene, view, box)
    w, h = x1 - x0, y1 - y0
    cx0, cy0 = max(0.0, x0 - pad * w), max(0.0, y0 - pad * h)
    cx1, cy1 = x1 + pad * w, y1 + pad * h
    im = open_photo(scene, view, max_side=2048)
    k = im.width / int(scene.views[view]["image"]["width"])
    crop = im.crop((round(cx0 * k), round(cy0 * k), round(cx1 * k), round(cy1 * k)))
    d = ImageDraw.Draw(crop)
    d.rectangle([(x0 - cx0) * k, (y0 - cy0) * k, (x1 - cx0) * k, (y1 - cy0) * k], outline=outline, width=2)
    s = out_side / max(crop.size)
    crop = crop.resize((max(1, round(crop.width * s)), max(1, round(crop.height * s))), Image.Resampling.LANCZOS)
    if label:
        d = ImageDraw.Draw(crop)
        d.rectangle([0, 0, 8 + 6 * len(label), 14], fill=(0, 0, 0))
        d.text((3, 1), label, fill=(255, 255, 0))
    return crop


def contact_sheet(images: list[Image.Image], *, cols: int = 6, cell: int = 320, background=(24, 24, 24)) -> Image.Image:
    """A grid of crops in one picture for a reviewer to look at. It writes nothing: a picture of a real room is saved
    only through ``OutputGuard.write_image``, which keeps it inside the capture."""
    rows = max(1, -(-len(images) // cols))
    out = Image.new("RGB", (cols * cell, rows * cell), background)
    for i, im in enumerate(images):
        out.paste(im, ((i % cols) * cell, (i // cols) * cell))
    return out


def stored_to_photo_xy(scene: Scene, view: int, xy: np.ndarray) -> np.ndarray:
    """Stored point-map pixels (N, 2) as (x, y) to original photo pixels (not clipped)."""
    info = scene.views[view]["image"]
    layout = input_layout(int(info["width"]), int(info["height"]), TARGET_SIZE)
    xy = np.asarray(xy, float) * STORE_STRIDE
    return np.stack([(xy[..., 0] - layout.pad_x + layout.left) / layout.scale,
                     (xy[..., 1] - layout.pad_y + layout.top) / layout.scale], -1)


def project_to_photo(scene: Scene, view: int, xyz: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Room points to original photo pixels (x, y) and the depth along the camera's axis."""
    pix, z = scene.project(view, xyz)
    return stored_to_photo_xy(scene, view, pix), z


def draw_polyline3d(scene: Scene, view: int, image: Image.Image, xyz: np.ndarray, *, colour=(255, 0, 255),
                    width: int = 3, closed: bool = True, scale: float = 1.0) -> None:
    """Draw a 3-D polyline (room coordinates) on a photo (or a copy scaled by ``scale``), dropping what is behind the camera."""
    xy, z = project_to_photo(scene, view, np.asarray(xyz, float))
    xy = xy * scale
    pts = list(range(len(xy))) + ([0] if closed else [])
    d = ImageDraw.Draw(image)
    for a, b in zip(pts[:-1], pts[1:]):
        if z[a] > 0.05 and z[b] > 0.05:
            d.line([tuple(xy[a]), tuple(xy[b])], fill=colour, width=width)


def gridded(scene: Scene, view: int, *, box: tuple[float, float, float, float] | None = None, max_side: int = 1100,
            step_px: int = 200) -> Image.Image:
    """The photo (or a crop of it, in original pixels) with a labelled pixel grid, for reading positions off by eye."""
    im = open_photo(scene, view)
    full_w = im.width
    x0, y0, x1, y1 = box if box else (0, 0, im.width, im.height)
    crop = im.crop((int(x0), int(y0), int(x1), int(y1)))
    k = max_side / max(crop.size)
    crop = crop.resize((round(crop.width * k), round(crop.height * k)), Image.Resampling.LANCZOS)
    d = ImageDraw.Draw(crop)
    gx = int(np.ceil(x0 / step_px) * step_px)
    while gx < x1:
        X = (gx - x0) * k
        d.line([(X, 0), (X, crop.height)], fill=(255, 255, 0), width=1)
        d.text((X + 2, 2), str(gx), fill=(255, 255, 0))
        gx += step_px
    gy = int(np.ceil(y0 / step_px) * step_px)
    while gy < y1:
        Y = (gy - y0) * k
        d.line([(0, Y), (crop.width, Y)], fill=(0, 255, 255), width=1)
        d.text((2, Y + 2), str(gy), fill=(0, 255, 255))
        gy += step_px
    return crop


def unproject_to_plane(scene: Scene, view: int, photo_xy: np.ndarray, plane_point, plane_normal) -> np.ndarray:
    """Original photo pixels (N, 2) to the room points where their rays meet a plane (NaN where they do not)."""
    xy = photo_to_stored_xy(scene, view, np.asarray(photo_xy, float))
    K = scene.K[view]
    ray_cam = np.stack([(xy[:, 0] - K[0, 2]) / K[0, 0], (xy[:, 1] - K[1, 2]) / K[1, 1], np.ones(len(xy))], -1)
    R, o = scene.c2w[view][:3, :3], scene.c2w[view][:3, 3]
    ray = ray_cam @ R.T
    n = np.asarray(plane_normal, float)
    denom = ray @ n
    with np.errstate(divide="ignore", invalid="ignore"):
        t = ((np.asarray(plane_point, float) - o) @ n) / denom
    t = np.where(t > 0, t, np.nan)
    return o + ray * t[:, None]
