"""The annotated top-down review image: the room from above, every inventory object's box and the five picks.

This is what the founder looks at to judge the inventory before any stand-in is built: the scan seen
from straight above, the walls with their windows and doors, each object's footprint turned the way
its box is turned, an arrow on its front, and a number and a name. The picks are drawn heavy and
numbered, with a panel listing their sizes and colours. Local only: it is a picture of a real place.
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from ..ortho import top_down
from ..scene import Scene
from ..shell.overlay import opening_corners
from ..shell.planes import ShellPlan
from ..shell.spec import ShellSpec
from .fit import Box, box_corners_xz, forward_xz

KIND_COLOURS = {
    "furniture": (120, 200, 255), "container": (255, 210, 120), "electronics": (170, 255, 170), "other": (230, 230, 230),
}
GROUPS = {
    "couch": "furniture", "desk": "furniture", "table": "furniture", "shelving unit": "furniture", "cabinet": "furniture",
    "chest of drawers": "furniture", "office chair": "furniture", "bean bag": "furniture", "easel": "furniture",
    "step ladder": "furniture", "bicycle": "furniture",
    "cardboard box": "container", "storage tote": "container", "suitcase": "container", "cooler": "container", "bin": "container",
    "basket": "container", "backpack": "container", "paint can": "container", "jar": "container", "board game": "container",
    "laptop": "electronics", "monitor": "electronics", "desktop computer": "electronics", "printer": "electronics",
    "speaker": "electronics", "air conditioner": "electronics", "projector": "electronics",
}


def _font(size: int) -> ImageFont.ImageFont:
    try:
        return ImageFont.load_default(size=size)
    except TypeError:  # an older Pillow
        return ImageFont.load_default()


def _text(d: ImageDraw.ImageDraw, xy, text: str, font, fill=(255, 255, 255), halo=(0, 0, 0)) -> None:
    x, y = xy
    for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
        d.text((x + dx, y + dy), text, font=font, fill=halo)
    d.text((x, y), text, font=font, fill=fill)


def _box_from_entry(o: dict[str, Any]) -> Box:
    b = o["box"]
    return Box(tuple(b["centre_m"]), tuple(b["size_m"]), b["yaw_deg"], o["placement"]["position_m"][1])


def render_review(scene: Scene, inventory: dict[str, Any], picks: list[str], path: Path, *, plan: ShellPlan | None = None,
                  spec: ShellSpec | None = None, res_m: float = 0.02, scale: int = 4, min_confidence: float = 0.0,
                  title: str | None = None) -> Path:
    rgb, _, (ox, oz) = top_down(scene, res_m=res_m)
    # The scan from above is smeared by pose noise, so it is only a faint ground to draw on.
    base = Image.fromarray((rgb.astype(np.float32) * 0.28 + 14).astype(np.uint8)).resize((rgb.shape[1] * scale, rgb.shape[0] * scale), Image.Resampling.BICUBIC)
    panel_w = 460
    canvas = Image.new("RGB", (base.width + panel_w, max(base.height + 60, 90 + 225 * max(1, len(picks)))), (22, 22, 26))
    canvas.paste(base, (0, 60))
    d = ImageDraw.Draw(canvas, "RGBA")
    px = scale / res_m  # pixels per metre

    def to_px(x: float, z: float) -> tuple[float, float]:
        return ((x - ox) * px, (z - oz) * px + 60)

    small, mid, big = _font(13), _font(16), _font(22)
    x0, x1, z0, z1 = scene.bounds
    d.rectangle([to_px(x0, z0), to_px(x1, z1)], outline=(255, 255, 0, 230), width=3)
    for key, (lx, lz) in {"A": ((x0 + x1) / 2, z0 - 0.07), "C": ((x0 + x1) / 2, z1 + 0.02), "D": (x0 - 0.07, (z0 + z1) / 2), "B": (x1 + 0.02, (z0 + z1) / 2)}.items():
        _text(d, to_px(lx, lz), "wall " + key, mid, (255, 255, 0))
    if plan is not None and spec is not None:
        for i, o in enumerate(spec.openings):
            c = opening_corners(plan, o)
            a, b = c[0], c[1]
            colour = (255, 80, 220, 255) if o.kind == "window" else (80, 220, 255, 255)
            d.line([to_px(a[0], a[2]), to_px(b[0], b[2])], fill=colour, width=7)
            mid_pt = (a + b) / 2
            _text(d, to_px(mid_pt[0] + 0.03, mid_pt[2] + 0.03), o.id.replace("_", " "), small, colour[:3])
    objects = [o for o in inventory["objects"] if o["confidence"] >= min_confidence]
    number = {pid: n for n, pid in enumerate(picks, 1)}
    for o in objects:
        box = _box_from_entry(o)
        corners = box_corners_xz(box)
        poly = [to_px(*c) for c in corners]
        pick = o["id"] in number
        colour = (255, 140, 0) if pick else KIND_COLOURS[GROUPS.get(o["kind"], "other")]
        d.polygon(poly, fill=(*colour, 70 if not pick else 110), outline=(*colour, 255))
        d.line(poly + [poly[0]], fill=(*colour, 255), width=5 if pick else 2)
        f = forward_xz(box.yaw_deg)
        centre = np.array([box.centre_m[0], box.centre_m[2]])
        tip = centre + f * (box.size_m[2] / 2 + 0.07)
        side = np.array([-f[1], f[0]]) * 0.05
        base_pt = centre + f * (box.size_m[2] / 2)
        if o["placement"]["front_known"]:
            d.polygon([to_px(*tip), to_px(*(base_pt + side)), to_px(*(base_pt - side))], fill=(*colour, 255))
        label = f"{number[o['id']]} {o['kind']}" if pick else o["id"].replace("_", " ")
        cx, cz = to_px(box.centre_m[0], box.centre_m[2])
        _text(d, (cx - 3 * len(label), cz - 8), label, mid if pick else small, (255, 255, 255) if not pick else (255, 200, 120))
    # A scale bar and a title.
    bx, by = to_px(x0, z1 + 0.2)
    d.line([(bx, by), (bx + px, by)], fill=(255, 255, 255, 255), width=4)
    _text(d, (bx, by + 6), "1 m", small)
    _text(d, (10, 10), title or f"{inventory['room']}: what the scan holds, seen from above", big)
    _text(d, (10, 38), "yellow = the shell; orange and numbered = the five picks; arrows = which way an object faces", small, (210, 210, 210))
    # The side panel.
    left = base.width + 16
    y = 70
    _text(d, (left, y), "The five stand-ins", big, (255, 200, 120))
    y += 36
    by_id = {o["id"]: o for o in inventory["objects"]}
    for pid, n in number.items():
        o = by_id.get(pid)
        if not o:
            continue
        w, h, dep = o["box"]["size_m"]
        _text(d, (left, y), f"{n}. {o['kind']}  ({pid})", mid, (255, 255, 255))
        y += 22
        _text(d, (left + 16, y), f"{w:.2f} x {h:.2f} x {dep:.2f} m, confidence {o['confidence']:.2f}", small, (200, 200, 200))
        y += 18
        px_, py_, pz_ = o["placement"]["position_m"]
        sup = o["placement"]["support"]
        on = "on the floor" if sup["kind"] == "floor" else f"{sup['height_m']:.2f} m up" + (f", on {sup['target_id']}" if sup.get("target_id") else "")
        _text(d, (left + 16, y), f"at x {px_:.2f}, z {pz_:.2f}, turned {o['placement']['yaw_deg']:.0f} deg, {on}", small, (200, 200, 200))
        y += 18
        cx = left + 16
        for c in o["colours"][:3]:
            d.rectangle([cx, y, cx + 26, y + 16], fill=tuple(int(c["hex"][i:i + 2], 16) for i in (1, 3, 5)) + (255,), outline=(255, 255, 255, 255))
            _text(d, (cx + 30, y + 1), c["hex"], small, (200, 200, 200))
            cx += 108
        y += 26
        sheet_path = scene.room_dir / o["evidence"].get("sheet", "") if o["evidence"].get("sheet") else None
        if sheet_path is not None and sheet_path.is_file():
            thumb = Image.open(sheet_path).convert("RGB").crop((0, 0, 200, 200))
            thumb.thumbnail((110, 110))
            canvas.paste(thumb, (left + 16, int(y)))
            y += thumb.height + 8
        y += 10
    out = canvas.convert("RGB")
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    out.save(path, quality=92)
    return Path(path)
