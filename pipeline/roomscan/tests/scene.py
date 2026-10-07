"""A synthetic box room for the C2 tests: ray-cast point maps, a fake pose backend and detector.

No GPU, no model and no real photo. The room frame is the pipeline's: +Y up, metres, floor at
y = 0. Cameras follow OpenCV (x right, y down, z forward), like MapAnything's output.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

import numpy as np

MAP_H, MAP_W = 72, 54  # stored point-map size, portrait like the real one
V_FOV_DEG = 62.0


@dataclass
class Box:
    lo: np.ndarray
    hi: np.ndarray
    label: str


@dataclass
class Room:
    size: tuple[float, float, float] = (4.0, 2.5, 5.0)  # x width, y height, z depth
    boxes: list[Box] = field(default_factory=list)

    @property
    def lo(self) -> np.ndarray:
        return np.array([-self.size[0] / 2, 0.0, -self.size[2] / 2])

    @property
    def hi(self) -> np.ndarray:
        return np.array([self.size[0] / 2, self.size[1], self.size[2] / 2])

    def add(self, lo, hi, label: str) -> "Room":
        self.boxes.append(Box(np.asarray(lo, float), np.asarray(hi, float), label))
        return self


def garage() -> Room:
    """4 x 5 m, 2.5 m high: shelving against the left wall (x min), a table in the room."""
    return (Room()
            .add((-2.0, 0.0, -1.0), (-1.55, 1.8, 0.4), "shelving unit")
            .add((0.2, 0.0, 0.6), (1.0, 0.75, 1.4), "table"))


def intrinsics(h: int = MAP_H, w: int = MAP_W, v_fov_deg: float = V_FOV_DEG) -> np.ndarray:
    f = (h / 2) / np.tan(np.radians(v_fov_deg) / 2)
    return np.array([[f, 0.0, w / 2], [0.0, f, h / 2], [0.0, 0.0, 1.0]])


def look_at(eye, target, up=(0.0, 1.0, 0.0)) -> np.ndarray:
    eye, target, up = (np.asarray(v, float) for v in (eye, target, up))
    f = target - eye
    f /= np.linalg.norm(f)
    r = np.cross(f, up)
    r /= np.linalg.norm(r)
    d = np.cross(f, r)
    T = np.eye(4)
    T[:3, :3] = np.column_stack([r, d, f])
    T[:3, 3] = eye
    return T


def render(room: Room, c2w: np.ndarray, K: np.ndarray, h: int = MAP_H, w: int = MAP_W) -> np.ndarray:
    """Camera-frame point map (h, w, 3) of the first surface each pixel ray hits."""
    v, u = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    d_cam = np.stack([(u - K[0, 2]) / K[0, 0], (v - K[1, 2]) / K[1, 1], np.ones_like(u)], -1)
    d = d_cam @ c2w[:3, :3].T
    o = c2w[:3, 3]
    with np.errstate(divide="ignore", invalid="ignore"):
        inv = 1.0 / d
        # Leaving the room: the nearest wall in the direction of travel.
        t_room = np.min(np.where(d > 0, (room.hi - o) * inv, np.where(d < 0, (room.lo - o) * inv, np.inf)), -1)
        t = t_room
        for box in room.boxes:
            t1, t2 = (box.lo - o) * inv, (box.hi - o) * inv
            near = np.max(np.minimum(t1, t2), -1)
            far = np.min(np.maximum(t1, t2), -1)
            hit = (near <= far) & (near > 1e-6)
            t = np.where(hit & (near < t), near, t)
    return d_cam * t[..., None]  # d_cam has z = 1, so t is the depth


def prediction(room: Room, c2w_room: np.ndarray, frame: np.ndarray | None = None, scale: float = 1.0,
               K: np.ndarray | None = None) -> dict[str, np.ndarray]:
    """A backend-shaped prediction, expressed in an arbitrary similarity frame of the room.

    ``frame`` (rigid 4x4) and ``scale`` take room coordinates to the batch's own frame, like the
    arbitrary frame and slightly different metric scale each real batch comes back in.
    """
    K = intrinsics() if K is None else K
    pts = render(room, c2w_room, K)
    finite = np.isfinite(pts).all(-1) & (pts[..., 2] > 0) & (pts[..., 2] < 50)  # a camera outside the room
    pts[~finite] = 0.0
    G = np.eye(4) if frame is None else frame
    c2w = np.eye(4)
    c2w[:3, :3] = G[:3, :3] @ c2w_room[:3, :3]
    c2w[:3, 3] = scale * (G[:3, :3] @ c2w_room[:3, 3]) + G[:3, 3]
    return {
        "c2w": c2w,
        "K": K.copy(),
        "K_model": np.diag([2.0, 2.0, 1.0]) @ K,
        "pts_cam": (pts * scale).astype(np.float16),
        "conf": np.ones(pts.shape[:2], np.float16),
        "mask": finite,
    }


def random_frame(rng: np.random.Generator) -> np.ndarray:
    q = rng.normal(size=4)
    q /= np.linalg.norm(q)
    a, b, c, d = q
    R = np.array([[a * a + b * b - c * c - d * d, 2 * (b * c - a * d), 2 * (b * d + a * c)],
                  [2 * (b * c + a * d), a * a - b * b + c * c - d * d, 2 * (c * d - a * b)],
                  [2 * (b * d - a * c), 2 * (c * d + a * b), a * a - b * b - c * c + d * d]])
    T = np.eye(4)
    T[:3, :3] = R
    T[:3, 3] = rng.normal(scale=2.0, size=3)
    return T


def ring_cameras(n: int, *, radius: float = 0.9, height: float = 1.5, centre=(0.0, 0.0),
                 from_deg: float = 0.0, to_deg: float = 360.0, outward: bool = True,
                 pitch_target_y: float | None = None) -> list[np.ndarray]:
    """Cameras on a circle looking outward (at the walls) or inward, spread over an arc."""
    out = []
    for a in np.radians(np.linspace(from_deg, to_deg, n, endpoint=(to_deg - from_deg) < 360)):
        dx, dz = np.cos(a), np.sin(a)
        eye = np.array([centre[0] + radius * dx, height, centre[1] + radius * dz])
        ty = height if pitch_target_y is None else pitch_target_y
        target = eye + np.array([dx, 0.0, dz]) * (1 if outward else -1)
        target[1] = ty
        out.append(look_at(eye, target))
    return out


class FakeBackend:
    """Stands in for MapAnythingBackend: every batch comes back in its own random frame and scale."""

    name = "synthetic"
    model_id = "synthetic-raycast"

    def __init__(self, room: Room, cameras: dict[str, np.ndarray], *, seed: int = 0, scale_jitter: float = 0.03,
                 corrupt_batches: set[int] | None = None):
        from roomscan.coverage.backend import GpuStats

        self.room, self.cameras = room, cameras
        self.rng = np.random.default_rng(seed)
        self.scale_jitter = scale_jitter
        self.corrupt = corrupt_batches or set()
        self.stats = GpuStats()
        self.weights_mib = 0.0
        self.calls = 0

    def load(self) -> None:
        pass

    def unload(self) -> None:
        pass

    def predict(self, paths: list[Path], intrinsics=None) -> list[dict[str, np.ndarray]]:
        frame = random_frame(self.rng)
        scale = 1.0 + self.rng.uniform(-self.scale_jitter, self.scale_jitter)
        out = []
        for p in paths:
            c2w = self.cameras[photo_key(p)]
            if self.calls in self.corrupt:  # a batch the model got wrong: poses scrambled
                c2w = random_frame(self.rng) @ c2w
            out.append(prediction(self.room, c2w, frame, scale))
        self.stats.batches.append({"views": len(paths), "seconds": 0.0, "peak_allocated_mib": 0,
                                   "peak_reserved_mib": 0})
        self.calls += 1
        return out


def photo_key(path: Path | str) -> str:
    """Original photo stem from a derived JPEG name ``<stem>-<sha10>.jpg``."""
    return Path(path).stem.rsplit("-", 1)[0]


class FakeDetector:
    """Returns the true boxes of the scene's furniture in each photo, in detection-image pixels."""

    def __init__(self, room: Room, cameras: dict[str, np.ndarray]):
        from roomscan.coverage.objects import detection_factor

        self.room, self.cameras = room, cameras
        self.seconds, self.peak_mib = 0.0, 0.0
        self.to_image = 1.0 / detection_factor()  # stored-map pixels -> detection-image pixels

    def detect(self, image, photo=None):
        c2w = self.cameras[Path(photo["name"]).stem]
        K = intrinsics()
        sx = sy = self.to_image
        found = []
        full = render(self.room, c2w, K)
        world = full @ c2w[:3, :3].T + c2w[:3, 3]
        for box in self.room.boxes:
            inside = np.all((world >= box.lo - 1e-3) & (world <= box.hi + 1e-3), -1)
            if inside.sum() < 6:
                continue
            ys, xs = np.nonzero(inside)
            found.append((box.label, 0.9, (xs.min() * sx, ys.min() * sy, (xs.max() + 1) * sx, (ys.max() + 1) * sy)))
        return found

    def close(self) -> None:
        pass
