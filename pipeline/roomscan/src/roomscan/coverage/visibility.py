"""Which photos actually see each patch of floor, wall and ceiling.

Every surface cell is sampled with a few points on its plane. Each point is projected into every
fitted photo and compared with the depth that photo itself measured at that pixel:

- **seen**: the photo's depth agrees with the surface (within a tolerance), at a usable angle and
  distance;
- **covered**: the photo sees something in front of the surface, standing on it or right against
  it (furniture on that patch of floor, shelving in front of that bit of wall);
- **blocked from afar**: the photo sees something in front of the surface, but far from it
  (clutter between the camera and the spot). That spot is still a gap: a photo from closer would
  see it;
- **looked through**: the photo sees past where the surface should be (a doorway, a window, an
  open garage door, or a wall that stands further back than fitted);
- otherwise the photo did not point there.

This replaces counting stray points per cell, which clutter and depth-edge noise defeat.
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

SUB = 3  # sample points per cell side
MAX_DISTANCE_M = 6.0
MAX_INCIDENCE_DEG = 75.0
LOW_CAMERA_M = 0.8  # photos taken below this count as the 10 cm player's point of view


@dataclass
class Views:
    """Fitted photos expressed in the room frame, ready for projection."""

    ids: list[int]
    w2c: np.ndarray  # (m, 4, 4)
    K: np.ndarray  # (m, 3, 3) of the stored map
    depth: np.ndarray  # (m, h, w), 0 where invalid
    valid: np.ndarray  # (m, h, w)
    centres: np.ndarray  # (m, 3)

    @classmethod
    def from_preds(cls, preds: dict, views: list[int], world_to_room: np.ndarray) -> "Views":
        w2c, K, depth, valid, centres = [], [], [], [], []
        for v in views:
            p = preds[v]
            c2w_room = world_to_room @ p["c2w"]
            w2c.append(np.linalg.inv(c2w_room))
            K.append(p["K"])
            d = p["pts_cam"][..., 2].astype(np.float32)
            ok = p["mask"].astype(bool) & np.isfinite(d) & (d > 0)
            depth.append(np.where(ok, d, 0.0))
            valid.append(ok)
            centres.append(c2w_room[:3, 3])
        return cls(list(views), np.stack(w2c), np.stack(K), np.stack(depth), np.stack(valid), np.stack(centres))


@dataclass
class Visibility:
    seen: np.ndarray  # (rows, cols) photos that see the cell
    covered: np.ndarray  # photos that see something standing on or against it
    blocked_far: np.ndarray  # photos whose view of it is blocked by something further away
    through: np.ndarray  # photos that see past it
    low_seen: np.ndarray  # photos taken below LOW_CAMERA_M that see it
    spread: np.ndarray  # mean resultant length of the seeing directions (1 = all from one direction)
    view_sets: list[list[set[int]]]


def cell_samples(origin: np.ndarray, u_dir: np.ndarray, v_dir: np.ndarray, rows: int, cols: int,
                 cell: float) -> np.ndarray:
    """(rows, cols, SUB*SUB, 3) points spread over each cell of a planar grid."""
    offs = (np.arange(SUB) + 0.5) / SUB
    r = (np.arange(rows)[:, None] + offs[None, :]).reshape(-1)  # rows*SUB
    c = (np.arange(cols)[:, None] + offs[None, :]).reshape(-1)
    pts = origin + (c[None, :, None] * cell) * u_dir + (r[:, None, None] * cell) * v_dir  # (rows*SUB, cols*SUB, 3)
    return pts.reshape(rows, SUB, cols, SUB, 3).transpose(0, 2, 1, 3, 4).reshape(rows, cols, SUB * SUB, 3)


def classify(samples: np.ndarray, normal: np.ndarray, views: Views, *, front_slack: float, back_tol: float,
             near_m: float, rel_tol: float = 0.04) -> Visibility:
    """Per cell: photos that see it, see furniture on it, are blocked from afar, or see past it.

    ``front_slack``: how far in front of the plane a measured surface still counts as the surface
    (fitted walls can sit a little behind shelving and pose error). ``back_tol``: how far behind.
    Both grow by ``rel_tol`` times the distance. ``near_m``: an obstruction closer than this to the
    surface (measured across a floor or ceiling, or out from a wall) is furniture on that cell.
    """
    rows, cols, s, _ = samples.shape
    flat = samples.reshape(-1, 3)
    n_cells = rows * cols
    horizontal = abs(normal[1]) > 0.9  # floor or ceiling
    counts = {k: np.zeros(n_cells, int) for k in ("seen", "covered", "far", "through", "low")}
    dir_sum = np.zeros((n_cells, 3))
    sets: list[set[int]] = [set() for _ in range(n_cells)]
    centres = samples.mean(2).reshape(n_cells, 3)
    m, h, w = views.depth.shape
    cos_max = np.cos(np.radians(MAX_INCIDENCE_DEG))
    for k in range(m):
        R, t = views.w2c[k, :3, :3], views.w2c[k, :3, 3]
        cam = flat @ R.T + t
        z = cam[:, 2]
        front = z > 0.1
        zs = np.where(front, z, 1.0)
        u = views.K[k, 0, 0] * cam[:, 0] / zs + views.K[k, 0, 2]
        v = views.K[k, 1, 1] * cam[:, 1] / zs + views.K[k, 1, 2]
        ui, vi = np.floor(u).astype(int), np.floor(v).astype(int)
        inside = front & (ui >= 0) & (ui < w) & (vi >= 0) & (vi < h)
        if not inside.any():
            continue
        uc, vc = np.clip(ui, 0, w - 1), np.clip(vi, 0, h - 1)
        ok = inside & views.valid[k, vc, uc]
        d = views.depth[k, vc, uc]
        ray = flat - views.centres[k]
        dist = np.linalg.norm(ray, axis=1)
        facing = -(ray @ normal) / np.maximum(dist, 1e-6)  # cosine between the ray and the surface normal
        usable = ok & (dist <= MAX_DISTANCE_M) & (facing >= cos_max)
        diff = d - z  # positive: the photo sees past the point
        tol_front = front_slack + rel_tol * z
        tol_back = back_tol + rel_tol * z
        s_seen = usable & (diff >= -tol_front) & (diff <= tol_back)
        s_hidden = ok & (diff < -tol_front)
        s_through = ok & (diff > tol_back)
        # Where the obstruction is: the point the photo actually saw along this ray.
        hit = ((cam * (d / zs)[:, None]) - t) @ R
        gap = hit - flat
        if horizontal:
            near = np.hypot(gap[:, 0], gap[:, 2]) < near_m
        else:
            near = (gap @ normal) < near_m
        per_cell = lambda x: x.reshape(n_cells, s).sum(1)  # noqa: E731
        n_in = per_cell(ok)
        c_seen, c_hidden, c_through = per_cell(s_seen), per_cell(s_hidden), per_cell(s_through)
        c_near = per_cell(s_hidden & near)
        any_in = n_in > 0
        sees = any_in & (c_seen >= np.maximum(2, 0.3 * n_in))
        hides = any_in & ~sees & (c_hidden >= 0.5 * n_in)
        covered = hides & (c_near >= 0.5 * c_hidden)
        far = hides & ~covered
        passes = any_in & ~sees & ~hides & (c_through >= 0.5 * n_in)
        counts["seen"] += sees
        counts["covered"] += covered
        counts["far"] += far
        counts["through"] += passes
        if views.centres[k][1] < LOW_CAMERA_M:
            counts["low"] += sees
        direction = centres[sees] - views.centres[k]
        dir_sum[sees] += direction / np.maximum(np.linalg.norm(direction, axis=1, keepdims=True), 1e-6)
        for cell in np.flatnonzero(sees):
            sets[cell].add(int(views.ids[k]))
    spread = np.linalg.norm(dir_sum, axis=1) / np.maximum(counts["seen"], 1)
    shape = (rows, cols)
    return Visibility(counts["seen"].reshape(shape), counts["covered"].reshape(shape), counts["far"].reshape(shape),
                      counts["through"].reshape(shape), counts["low"].reshape(shape), spread.reshape(shape),
                      [[sets[r * cols + c] for c in range(cols)] for r in range(rows)])
