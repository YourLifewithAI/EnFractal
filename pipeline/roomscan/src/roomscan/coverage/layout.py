"""Room frame from the merged reconstruction: up, floor, ceiling and four walls.

Assumes a box-shaped room (Manhattan world): one floor, one ceiling, walls meeting at right
angles. That holds for the garage and most rooms; anything else shows up as a poor fit in the
report rather than silently.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import numpy as np

from .geometry import dominant_wall_yaw, grid_normals, normalize, ransac_plane, rotation_between, yaw_rotation


@dataclass
class PointSet:
    xyz: np.ndarray  # (N, 3) world
    normal: np.ndarray  # (N, 3) world, oriented toward the camera that saw the point
    view: np.ndarray  # (N,) view index
    conf: np.ndarray


@dataclass
class RoomFrame:
    world_to_room: np.ndarray  # 4x4 rigid, room frame is +Y up, floor at y = 0
    ceiling_y: float | None
    x_min: float
    x_max: float
    z_min: float
    z_max: float
    sources: dict[str, str] = field(default_factory=dict)

    @property
    def size(self) -> tuple[float, float, float | None]:
        return (self.x_max - self.x_min, self.z_max - self.z_min, self.ceiling_y)

    def as_dict(self) -> dict[str, Any]:
        return {
            "world_to_room": np.round(self.world_to_room, 6).tolist(),
            "ceiling_y_m": None if self.ceiling_y is None else round(self.ceiling_y, 3),
            "x_min_m": round(self.x_min, 3), "x_max_m": round(self.x_max, 3),
            "z_min_m": round(self.z_min, 3), "z_max_m": round(self.z_max, 3),
            "width_x_m": round(self.x_max - self.x_min, 3), "depth_z_m": round(self.z_max - self.z_min, 3),
            "sources": self.sources,
        }


def collect_points(preds: dict[int, dict[str, np.ndarray]], views: list[int], *, per_view: int = 6000,
                   seed: int = 0) -> PointSet:
    rng = np.random.default_rng(seed)
    xyz, nor, owner, conf = [], [], [], []
    for v in views:
        p = preds[v]
        pts = p["pts_cam"].astype(np.float64)
        n_cam = grid_normals(pts)
        valid = p["mask"] & np.isfinite(pts).all(-1) & (np.linalg.norm(n_cam, axis=-1) > 0.5)
        idx = np.flatnonzero(valid.ravel())
        if len(idx) == 0:
            continue
        pick = rng.choice(idx, size=min(per_view, len(idx)), replace=False)
        pc = pts.reshape(-1, 3)[pick]
        nc = n_cam.reshape(-1, 3)[pick]
        # Orient normals toward the camera (the camera sits at the origin of its own frame).
        flip = (nc * -pc).sum(1) < 0
        nc[flip] *= -1
        R, t = p["c2w"][:3, :3], p["c2w"][:3, 3]
        xyz.append(pc @ R.T + t)
        nor.append(nc @ R.T)
        owner.append(np.full(len(pick), v))
        conf.append(p["conf"].reshape(-1)[pick].astype(np.float64))
    return PointSet(np.concatenate(xyz), np.concatenate(nor), np.concatenate(owner), np.concatenate(conf))


def _outer_peak(values: np.ndarray, side: int, *, bin_m: float = 0.02, min_frac: float = 0.15) -> float | None:
    """Position of the outermost substantial plane among 1-D positions (side -1: lowest, +1: highest)."""
    if len(values) < 50:
        return None
    lo, hi = np.quantile(values, [0.001, 0.999])
    if hi - lo < 4 * bin_m:
        return float(np.median(values))
    edges = np.arange(lo - bin_m, hi + 2 * bin_m, bin_m)
    hist, edges = np.histogram(values, bins=edges)
    smooth = np.convolve(hist, np.ones(3) / 3, mode="same")
    peak_max = smooth.max()
    peaks = [i for i in range(1, len(smooth) - 1)
             if smooth[i] >= smooth[i - 1] and smooth[i] >= smooth[i + 1] and smooth[i] >= min_frac * peak_max]
    if not peaks:
        return None
    i = peaks[0] if side < 0 else peaks[-1]
    centre = (edges[i] + edges[i + 1]) / 2
    near = values[np.abs(values - centre) < 2.5 * bin_m]
    return float(np.median(near)) if len(near) else float(centre)


def wall_plane(facing: np.ndarray, everything: np.ndarray, side: int, *, bin_m: float = 0.05,
               min_frac: float = 0.08, max_beyond: float = 0.05) -> tuple[float, str] | None:
    """Where a wall stands along one axis, and why.

    Candidates are the peaks of the positions of points facing into the room. Shelf fronts and
    furniture make peaks inside the room; the wall is the strongest peak with almost nothing
    (``max_beyond`` of all points at wall height) seen behind it. Openings let a little through,
    which is why the limit is not zero. Without such a peak, the outermost one is used.
    """
    if len(facing) < 50:
        return None
    lo, hi = np.quantile(facing, [0.001, 0.999])
    edges = np.arange(lo - bin_m, hi + 2 * bin_m, bin_m)
    hist, edges = np.histogram(facing, bins=edges)
    smooth = np.convolve(hist, np.ones(3) / 3, mode="same")
    peaks = [i for i in range(len(smooth))
             if (i == 0 or smooth[i] >= smooth[i - 1]) and (i == len(smooth) - 1 or smooth[i] >= smooth[i + 1])
             and smooth[i] >= min_frac * smooth.max()]
    if not peaks:
        return None
    cands = []
    for i in peaks:
        centre = (edges[i] + edges[i + 1]) / 2
        near = facing[np.abs(facing - centre) < 1.5 * bin_m]
        pos = float(np.median(near)) if len(near) else float(centre)
        beyond = float(((everything - pos) * side > 0.10).mean()) if len(everything) else 0.0
        cands.append((pos, float(smooth[i]), beyond))
    closed = [c for c in cands if c[2] <= max_beyond]
    if closed:
        pos, _, beyond = max(closed, key=lambda c: c[1])
        return pos, f"strongest wall plane with {100 * beyond:.1f}% of points behind it"
    pos, _, beyond = max(cands, key=lambda c: c[0] * side)
    return pos, f"outermost wall plane ({100 * beyond:.1f}% of points behind it)"


def camera_forward(preds: dict[int, dict[str, np.ndarray]], view: int) -> np.ndarray:
    """World direction a photo looks along (+Z of the OpenCV camera)."""
    return preds[view]["c2w"][:3, :3] @ np.array([0.0, 0.0, 1.0])


def camera_up(preds: dict[int, dict[str, np.ndarray]], views: list[int]) -> np.ndarray:
    """Mean camera 'up' (-y in OpenCV) in world: people hold phones roughly level."""
    ups = np.stack([preds[v]["c2w"][:3, :3] @ np.array([0.0, -1.0, 0.0]) for v in views])
    return normalize(ups.mean(0))


def fit_room(points: PointSet, up0: np.ndarray, forward: np.ndarray | None = None) -> RoomFrame:
    """Box room fitted to the points. ``forward`` (a world direction, usually the first photo's
    view) is turned to point up the map (-Z), which fixes the otherwise arbitrary choice among the
    four wall directions, so the walls keep their letters from run to run."""
    sources: dict[str, str] = {}
    h = points.xyz @ up0
    low = (h < np.quantile(h, 0.3)) & ((points.normal @ up0) > np.cos(np.radians(35)))
    floor = ransac_plane(points.xyz[low], axis=up0, max_angle_deg=20, threshold=0.03, iterations=400)
    if floor is not None and floor[2].sum() > 200:
        up, d_floor = floor[0], floor[1]
        sources["floor"] = f"plane fit on {int(floor[2].sum())} points"
    else:
        up, d_floor = up0, float(np.quantile(h, 0.02))
        sources["floor"] = "lowest 2% of points (no clear floor plane)"
    R1 = rotation_between(up, np.array([0.0, 1.0, 0.0]))
    xyz = points.xyz @ R1.T
    nor = points.normal @ R1.T
    xyz[:, 1] -= d_floor  # floor at y = 0 (R1 maps up to +Y, so n.x = d becomes y = d)

    horiz = np.abs(nor[:, 1]) < 0.2
    yaw = dominant_wall_yaw(nor[horiz][:, [0, 2]]) if horiz.sum() > 100 else 0.0
    R2 = yaw_rotation(yaw)
    if forward is not None:
        f = R2 @ R1 @ np.asarray(forward, float)
        best = max(range(4), key=lambda k: float((yaw_rotation(k * np.pi / 2) @ f) @ np.array([0.0, 0.0, -1.0])))
        R2 = yaw_rotation(best * np.pi / 2) @ R2
        sources["orientation"] = "first photo looks up the map (toward wall A)"
    xyz = xyz @ R2.T
    nor = nor @ R2.T
    R = R2 @ R1

    bounds = {}
    band = (xyz[:, 1] > 0.3) & (xyz[:, 1] < 2.2)  # wall height, above skirting clutter
    for axis, key_lo, key_hi in ((0, "x_min", "x_max"), (2, "z_min", "z_max")):
        facing_pos = band & (nor[:, axis] > 0.8)  # a wall on the low side faces +axis into the room
        facing_neg = band & (nor[:, axis] < -0.8)
        everything = xyz[band, axis]
        for key, facing, side, q in ((key_lo, facing_pos, -1, 0.02), (key_hi, facing_neg, +1, 0.98)):
            found = wall_plane(xyz[facing, axis], everything, side)
            if found is None:
                bounds[key] = float(np.quantile(xyz[:, axis], q))
                sources[key] = f"{round(q * 100)}th percentile of all points (wall not seen face-on)"
            else:
                bounds[key], sources[key] = found

    down = (nor[:, 1] < -0.85) & (xyz[:, 1] > 1.5)
    ceiling = _outer_peak(xyz[down, 1], +1, min_frac=0.3)
    sources["ceiling"] = "downward-facing plane" if ceiling is not None else "not seen"

    # Centre the room on the origin in x and z.
    cx = (bounds["x_min"] + bounds["x_max"]) / 2
    cz = (bounds["z_min"] + bounds["z_max"]) / 2
    T = np.eye(4)
    T[:3, :3] = R
    T[:3, 3] = -(R2 @ np.array([0.0, d_floor, 0.0])) - np.array([cx, 0.0, cz])
    # Check: T maps a floor point to y = 0.
    return RoomFrame(T, ceiling, bounds["x_min"] - cx, bounds["x_max"] - cx, bounds["z_min"] - cz,
                     bounds["z_max"] - cz, sources)


def to_room(frame: RoomFrame, xyz: np.ndarray) -> np.ndarray:
    return xyz @ frame.world_to_room[:3, :3].T + frame.world_to_room[:3, 3]


def dir_to_room(frame: RoomFrame, vec: np.ndarray) -> np.ndarray:
    return vec @ frame.world_to_room[:3, :3].T
