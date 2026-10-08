"""The scaled room scene of a coverage session, rebuilt from its cached poses (CPU only, no model).

C3 (the shell) and C4 (the inventory) both need what ``coverage.run`` computes before it draws
anything: every placed photo's camera and point map in the room frame, at the tape-fitted scale.
This rebuilds that from ``poses.npz`` and ``coverage.json`` in a few seconds, with the same
functions the coverage run uses, and refuses to go on when the result disagrees with the room box
the report recorded (a changed pose cache or a changed fit must not slip into the garage's shell).

Room frame: metres, +Y up, floor at y = 0, centred on the origin in x and z. The first photo looks
toward -Z (wall A). Walls: A at z_min, B at x_max, C at z_max, D at x_min.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import numpy as np

from .coverage.backend import STORE_STRIDE, load_model_view
from .coverage.geometry import transform_points
from .coverage.layout import RoomFrame, camera_forward, camera_up, collect_points, fit_room
from .coverage.run import load_poses, select_views
from .coverage.visibility import Views
from .coverage.scale import MEASUREMENTS_FILE, load_measurements, plan_scale, scale_frame, scale_prediction
from .ingest import resolve_session

# The rebuilt frame must agree with the one coverage.json recorded to within this (metres).
FRAME_TOLERANCE_M = 0.003


class SceneMismatch(RuntimeError):
    """The cached poses no longer give the room that the coverage report recorded."""


@dataclass
class Scene:
    room: str
    session_id: str
    room_dir: Path
    manifest: dict[str, Any]
    views: list[dict[str, Any]]  # the photo records the poses were computed for; index = pose index
    registered: list[int]  # photos that fit together (the ones with a usable pose)
    frame: RoomFrame  # at the applied scale
    factor: float
    scale_source: str  # "tape" or "seed_batch"
    c2w: dict[int, np.ndarray]  # camera to room, 4x4
    K: dict[int, np.ndarray]  # intrinsics of the stored point map (259 x 196 for the garage set)
    pts: dict[int, np.ndarray] = field(repr=False)  # (h, w, 3) point maps in the room frame
    valid: dict[int, np.ndarray] = field(repr=False)  # (h, w) usable points
    conf: dict[int, np.ndarray] = field(repr=False)
    _model_views: dict[int, np.ndarray] = field(default_factory=dict, repr=False)

    @property
    def bounds(self) -> tuple[float, float, float, float]:
        return (self.frame.x_min, self.frame.x_max, self.frame.z_min, self.frame.z_max)

    @property
    def height_m(self) -> float:
        return float(self.frame.ceiling_y or 2.5)

    @property
    def map_shape(self) -> tuple[int, int]:
        v = self.registered[0]
        return self.pts[v].shape[:2]

    def camera_centre(self, view: int) -> np.ndarray:
        return self.c2w[view][:3, 3]

    def photo_path(self, view: int) -> Path:
        return self.room_dir / self.views[view]["derived"]["jpeg"]

    def model_view(self, view: int) -> np.ndarray:
        """The photo as the pose model saw it (392 x 518, RGB uint8); stored map pixel (r, c) is its pixel (2r, 2c)."""
        if view not in self._model_views:
            image, _ = load_model_view(self.photo_path(view))
            self._model_views[view] = np.asarray(image.convert("RGB"))
        return self._model_views[view]

    def pixel_colours(self, view: int, rows: np.ndarray, cols: np.ndarray) -> np.ndarray:
        """sRGB colours (uint8, N x 3) of stored-map pixels, read from the photo the model saw."""
        img = self.model_view(view)
        r = np.clip(np.asarray(rows) * STORE_STRIDE, 0, img.shape[0] - 1)
        c = np.clip(np.asarray(cols) * STORE_STRIDE, 0, img.shape[1] - 1)
        return img[r, c]

    def coverage_views(self) -> Views:
        """The fitted photos as ``coverage.visibility.Views`` (room frame), for the coverage grids' sight-line tests."""
        ids = list(self.registered)
        w2c = np.stack([np.linalg.inv(self.c2w[v]) for v in ids])
        depth = []
        for v, m in zip(ids, w2c):
            z = (self.pts[v] @ m[:3, :3].T + m[:3, 3])[..., 2].astype(np.float32)
            depth.append(np.where(self.valid[v] & (z > 0), z, 0.0))
        return Views(ids, w2c, np.stack([self.K[v] for v in ids]), np.stack(depth), np.stack([d > 0 for d in depth]),
                     np.stack([self.c2w[v][:3, 3] for v in ids]))

    def project(self, view: int, xyz: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        """Room points to stored-map pixel coordinates (x, y) and depth along the optical axis."""
        w2c = np.linalg.inv(self.c2w[view])
        cam = transform_points(w2c, np.asarray(xyz, float))
        z = cam[..., 2]
        zs = np.where(np.abs(z) < 1e-9, 1e-9, z)
        K = self.K[view]
        return np.stack([K[0, 0] * cam[..., 0] / zs + K[0, 2], K[1, 1] * cam[..., 1] / zs + K[1, 2]], -1), z


def _check_frame(rebuilt: RoomFrame, recorded: dict[str, Any]) -> None:
    mine = rebuilt.as_dict()
    problems = []
    for key in ("x_min_m", "x_max_m", "z_min_m", "z_max_m", "ceiling_y_m"):
        a, b = mine[key], recorded.get(key)
        if a is None or b is None or abs(a - b) > FRAME_TOLERANCE_M:
            problems.append(f"{key}: rebuilt {a}, recorded {b}")
    if not np.allclose(np.asarray(mine["world_to_room"]), np.asarray(recorded["world_to_room"]), atol=FRAME_TOLERANCE_M):
        problems.append("world_to_room differs")
    if problems:
        raise SceneMismatch("The cached poses no longer give the recorded room (" + "; ".join(problems)
                            + "). Rerun `roomscan coverage` before exporting.")


def load_scene(captures_root: Path, room: str, session: str | None = "latest", *,
               measurements: Path | None = None, verify: bool = True) -> Scene:
    session_dir = resolve_session(captures_root, room, session)
    room_dir = session_dir.parents[1]
    manifest = json.loads((session_dir / "manifest.json").read_bytes())
    cov_dir = session_dir / "coverage"
    record = json.loads((cov_dir / "coverage.json").read_bytes())
    views = select_views(manifest)
    names = json.loads((cov_dir / "poses.json").read_bytes())["names"]
    if names != [p["name"] for p in views]:
        raise SceneMismatch("The pose cache was made for a different set of photos than the session's manifest selects.")
    preds = load_poses(cov_dir / "poses.npz")
    fitted = {p["index"] for p in record["photos"] if p.get("fitted")}
    registered = sorted(i for i in preds if i in fitted)
    if len(registered) < 2:
        raise SceneMismatch("Fewer than two fitted photos.")

    # Same steps as coverage.run: the box in the model's own units, then the tape's uniform scale.
    pts = collect_points(preds, registered)
    frame_model = fit_room(pts, camera_up(preds, registered), forward=camera_forward(preds, registered[0]))
    meas_path = Path(measurements) if measurements else room_dir / MEASUREMENTS_FILE
    tape = load_measurements(meas_path, manifest["room"]) if meas_path.is_file() else None
    plan = plan_scale(tape, manifest["session_id"], frame_model)
    if plan.source == "tape":
        preds = {v: scale_prediction(p, plan.factor) for v, p in preds.items()}
        frame = scale_frame(frame_model, plan.factor)
    else:
        frame = frame_model
    if verify:
        _check_frame(frame, record["room_frame"])

    T = frame.world_to_room
    c2w, K, pts_room, valid, conf = {}, {}, {}, {}, {}
    for v in registered:
        p = preds[v]
        c2w[v] = T @ p["c2w"]
        K[v] = p["K"]
        pr = transform_points(c2w[v], p["pts_cam"].astype(np.float64))
        ok = p["mask"].astype(bool) & np.isfinite(pr).all(-1) & (p["pts_cam"][..., 2].astype(np.float32) > 0)
        pts_room[v] = pr
        valid[v] = ok
        conf[v] = p["conf"].astype(np.float32)
    return Scene(manifest["room"], manifest["session_id"], room_dir, manifest, views, registered, frame,
                 float(plan.factor if plan.source == "tape" else 1.0), plan.source, c2w, K, pts_room, valid, conf)
