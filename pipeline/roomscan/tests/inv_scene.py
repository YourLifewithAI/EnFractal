"""A synthetic ``Scene`` built straight from the ray-cast room, at the real stored-map size (259 x 196).

The C2 fakes in ``scene.py`` use a small 72 x 54 map, which is fine for coverage but does not line up with
the photo-to-map arithmetic the inventory relies on (a stored pixel is two model pixels of a 392 x 518 view).
Here the cameras are known exactly, the point maps are ray-cast at 259 x 196, and the photos are small
synthetic JPEGs of the right shape (3:4 portrait, 300 x 400), so lifting, masks, boxes and colours can be checked against the truth.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np

import scene as sc
import synth
from roomscan.coverage.layout import RoomFrame
from roomscan.scene import Scene

MAP_H, MAP_W = 259, 196
PHOTO_W, PHOTO_H = 300, 400  # portrait 3:4 like the real photos, and the size of the JPEGs on disk


def room_with_boxes() -> sc.Room:
    """4 x 5 m, 2.5 m high: a table, a shelving unit and a small box on the table."""
    return (sc.Room()
            .add((0.2, 0.0, 0.6), (1.0, 0.75, 1.4), "table")
            .add((-2.0, 0.0, -1.0), (-1.55, 1.8, 0.4), "shelving unit")
            .add((0.45, 0.75, 0.9), (0.75, 0.87, 1.15), "laptop"))


def ring(centre, radius: float, height: float, count: int, look_at_y: float = 0.5) -> list[np.ndarray]:
    cams = []
    for a in np.radians(np.linspace(0, 360, count, endpoint=False)):
        eye = np.array([centre[0] + radius * np.cos(a), height, centre[1] + radius * np.sin(a)])
        cams.append(sc.look_at(eye, np.array([centre[0], look_at_y, centre[1]])))
    return cams


def build_scene(tmp_path: Path, room: sc.Room | None = None, cameras: list[np.ndarray] | None = None) -> Scene:
    room = room or room_with_boxes()
    K = sc.intrinsics(MAP_H, MAP_W)
    cameras = cameras or (ring((0.6, 1.0), 1.7, 1.35, 12) + ring((0.6, 1.0), 1.2, 0.9, 8, 0.4))
    photos_dir = tmp_path / "room" / "photos"
    photos_dir.mkdir(parents=True, exist_ok=True)
    views, c2w, Ks, pts, valid, conf = [], {}, {}, {}, {}, {}
    for i, cam in enumerate(cameras):
        name = f"view_{i:02d}"
        synth.save_jpeg(synth.view(500 + i, (i * 37 % 800, i * 53 % 600)).resize((PHOTO_W, PHOTO_H)), photos_dir / f"{name}.jpg")
        views.append({"name": f"{name}.HEIC", "sha256": f"{i:064x}", "status": "ok",
                      "derived": {"jpeg": f"photos/{name}.jpg"}, "image": {"width": PHOTO_W, "height": PHOTO_H},
                      "exif": {"make": "Apple", "model": "Synthetic phone"}})
        cam_pts = sc.render(room, cam, K, MAP_H, MAP_W)
        finite = np.isfinite(cam_pts).all(-1) & (cam_pts[..., 2] > 0)
        cam_pts = np.where(finite[..., None], cam_pts, 0.0)
        world = cam_pts @ cam[:3, :3].T + cam[:3, 3]
        c2w[i], Ks[i], pts[i], valid[i], conf[i] = cam, K, world, finite, np.ones((MAP_H, MAP_W), np.float32)
    frame = RoomFrame(np.eye(4), room.size[1], -room.size[0] / 2, room.size[0] / 2, -room.size[2] / 2, room.size[2] / 2)
    manifest = {"room": "garage", "session_id": "s-synthetic", "photos": views}
    return Scene("garage", "s-synthetic", tmp_path / "room", manifest, views, list(range(len(cameras))), frame, 1.0, "seed_batch",
                 c2w, Ks, pts, valid, conf)


def true_box_mask(scene: Scene, view: int, lo, hi, pad: float = 0.02) -> np.ndarray:
    """Stored pixels of a photo whose point lies inside a box: the mask a perfect segmenter would give."""
    p = scene.pts[view]
    lo, hi = np.asarray(lo) - pad, np.asarray(hi) + pad
    return scene.valid[view] & np.all((p >= lo) & (p <= hi), -1)
