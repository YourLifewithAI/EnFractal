"""Tighten the merged batches against each other, and settle the room's scale.

Each GPU batch comes back in its own metric frame and the anchor fit in ``chunks`` brings it into
the room frame, but two predictions of the same photo never agree exactly, so walls can come out
doubled where batches meet. Here every batch's similarity is refined by a trimmed ICP against the
surfaces of all the other batches (nearest points within a shrinking radius, normals required to
agree), batch by batch, for a few rounds. The reference batch stays fixed.

Each batch also carries its own metric estimate. The room keeps the median of them rather than
the reference batch's alone, and the spread is reported as the scale uncertainty.
"""

from __future__ import annotations

from typing import Any

import numpy as np

from .chunks import move_prediction
from .geometry import grid_normals, normalize, sim3_matrix, umeyama

RADII = (0.40, 0.30, 0.20, 0.15, 0.10)


def _cloud(preds: dict[int, dict[str, np.ndarray]], views: list[int], per_view: int, rng) -> tuple[np.ndarray, np.ndarray]:
    xyz, nor = [], []
    for v in views:
        p = preds[v]
        pts = p["pts_cam"].astype(np.float64)
        n_cam = grid_normals(pts)
        ok = p["mask"].astype(bool) & np.isfinite(pts).all(-1) & (np.linalg.norm(n_cam, axis=-1) > 0.5)
        idx = np.flatnonzero(ok.ravel())
        if len(idx) == 0:
            continue
        pick = rng.choice(idx, size=min(per_view, len(idx)), replace=False)
        pc, nc = pts.reshape(-1, 3)[pick], n_cam.reshape(-1, 3)[pick]
        nc[(nc * -pc).sum(1) < 0] *= -1  # toward the camera
        R, t = p["c2w"][:3, :3], p["c2w"][:3, 3]
        xyz.append(pc @ R.T + t)
        nor.append(nc @ R.T)
    if not xyz:
        return np.zeros((0, 3)), np.zeros((0, 3))
    return np.concatenate(xyz), np.concatenate(nor)


def _apply(S: np.ndarray, xyz: np.ndarray, nor: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    s = np.cbrt(np.linalg.det(S[:3, :3]))
    return xyz @ S[:3, :3].T + S[:3, 3], nor @ (S[:3, :3] / s).T


def icp_sim3(src: np.ndarray, src_n: np.ndarray, dst: np.ndarray, dst_n: np.ndarray, *, radii=RADII,
             iterations: int = 6, trim: float = 0.7, normal_cos: float = 0.8, min_pairs: int = 300,
             tree=None) -> dict[str, Any]:
    """Similarity S with dst ~= S(src), by trimmed nearest-neighbour ICP with normal agreement."""
    from scipy.spatial import cKDTree

    tree = tree or cKDTree(dst)
    S = np.eye(4)

    def pairs(radius: float):
        moved, moved_n = _apply(S, src, src_n)
        dist, idx = tree.query(moved, distance_upper_bound=radius, workers=-1)
        ok = np.isfinite(dist)
        ok[ok] &= (moved_n[ok] * dst_n[idx[ok]]).sum(1) > normal_cos
        return ok, dist, idx

    ok0, dist0, _ = pairs(radii[-1])
    before = {"inlier_share": float(ok0.mean()), "median_m": float(np.median(dist0[ok0])) if ok0.any() else None}
    for radius in radii:
        for _ in range(iterations):
            ok, dist, idx = pairs(radius)
            if ok.sum() < min_pairs:
                break
            cut = np.quantile(dist[ok], trim)
            keep = ok & (dist <= cut)
            s, R, t = umeyama(src[keep], dst[idx[keep]])
            S_new = sim3_matrix(s, R, t)
            step = np.abs(S_new - S).max()
            S = S_new
            if step < 1e-4:
                break
    ok1, dist1, _ = pairs(radii[-1])
    after = {"inlier_share": float(ok1.mean()), "median_m": float(np.median(dist1[ok1])) if ok1.any() else None}
    return {"sim3": S, "before": before, "after": after}


def _describe(S: np.ndarray) -> dict[str, float]:
    s = float(np.cbrt(np.linalg.det(S[:3, :3])))
    R = S[:3, :3] / s
    angle = float(np.degrees(np.arccos(np.clip((np.trace(R) - 1) / 2, -1, 1))))
    return {"scale": round(s, 4), "rotation_deg": round(angle, 2), "shift_m": round(float(np.linalg.norm(S[:3, 3])), 3)}


def refine_batches(preds: dict[int, dict[str, np.ndarray]], *, reference: int = 0, rounds: int = 2,
                   per_view: int = 2500, seed: int = 0, max_rotation_deg: float = 12.0, max_shift_m: float = 0.6,
                   log=print) -> tuple[dict[int, dict[str, np.ndarray]], dict[str, Any]]:
    """Each batch's similarity refined against all the others; returns new preds and a report.

    A refinement is kept only when it moves the batch by less than ``max_rotation_deg`` and
    ``max_shift_m`` (relative to the batch's own centre) and more of the batch's surface then
    agrees with the rest; otherwise the batch stays where the anchor fit put it.
    """
    from scipy.spatial import cKDTree

    rng = np.random.default_rng(seed)
    by_batch: dict[int, list[int]] = {}
    for v, p in preds.items():
        by_batch.setdefault(int(p.get("batch", 0)), []).append(v)
    out = {v: dict(p) for v, p in preds.items()}
    clouds = {b: _cloud(preds, vs, per_view, rng) for b, vs in sorted(by_batch.items())}
    total = {b: np.eye(4) for b in clouds}
    history = []
    for rnd in range(rounds):
        for b in sorted(clouds):
            if b == reference or len(clouds) < 2:
                continue
            others = [c for k, c in clouds.items() if k != b and len(c[0])]
            if not others or not len(clouds[b][0]):
                continue
            dst = np.concatenate([c[0] for c in others])
            dst_n = np.concatenate([c[1] for c in others])
            src, src_n = clouds[b]
            # Express the fit about the batch's centroid so "shift" means how far the batch moves.
            centre = src.mean(0)
            C = np.eye(4)
            C[:3, 3] = -centre
            fit = icp_sim3(src - centre, src_n, dst - centre, dst_n, tree=cKDTree(dst - centre))
            Sc = fit["sim3"]
            S = np.linalg.inv(C) @ Sc @ C
            d = _describe(Sc)
            gain = (fit["after"]["inlier_share"] or 0) - (fit["before"]["inlier_share"] or 0)
            accepted = d["rotation_deg"] <= max_rotation_deg and d["shift_m"] <= max_shift_m and gain > 0
            history.append({"round": rnd, "batch": b, "accepted": accepted, **d,
                            "agree_before": round(fit["before"]["inlier_share"], 3),
                            "agree_after": round(fit["after"]["inlier_share"], 3)})
            if accepted:
                clouds[b] = _apply(S, *clouds[b])
                total[b] = S @ total[b]
    for b, vs in by_batch.items():
        if not np.allclose(total[b], np.eye(4)):
            for v in vs:
                out[v] = move_prediction(out[v], total[b])
                out[v]["batch"] = b
    log("  refinement: " + ", ".join(f"batch {h['batch']} {'moved' if h['accepted'] else 'kept'} "
                                     f"({h['rotation_deg']} deg, {h['shift_m']} m, agree {h['agree_before']}->{h['agree_after']})"
                                     for h in history if h["round"] == rounds - 1))
    return out, {"rounds": rounds, "history": history,
                 "per_batch": {str(b): _describe(total[b]) for b in sorted(total)}}


def consensus_scale(batch_scales: dict[int, float]) -> dict[str, float]:
    """The room scale factor that gives the median batch its own metric scale, and the spread.

    ``batch_scales`` maps batch -> the scale applied to that batch's raw prediction to bring it
    into the room frame (the reference batch is 1). Multiplying the room by the returned
    ``factor`` makes the median batch metric.
    """
    values = np.array(sorted(batch_scales.values()), float)
    med = float(np.median(values))
    return {"factor": 1.0 / med, "median_batch_scale": med,
            "spread_pct": round(100 * float(values.max() - values.min()) / med / 2, 1) if len(values) > 1 else 0.0}


def wall_spread(xyz: np.ndarray, nor: np.ndarray, frame) -> dict[str, float]:
    """How thick the walls come out: median distance of wall-facing points to each wall plane."""
    out = {}
    for name, axis, side in (("x_min", 0, -1), ("x_max", 0, 1), ("z_min", 2, -1), ("z_max", 2, 1)):
        plane = getattr(frame, name)
        facing = nor[:, axis] * -side > 0.8
        near = facing & (np.abs(xyz[:, axis] - plane) < 0.6)
        out[name] = round(float(np.median(np.abs(xyz[near, axis] - plane))), 3) if near.sum() > 50 else None
    return out


def scale_prediction(pred: dict[str, np.ndarray], factor: float) -> dict[str, np.ndarray]:
    """Uniformly rescale a prediction about the frame origin."""
    S = np.eye(4)
    S[:3, :3] *= factor
    return move_prediction(pred, S)


__all__ = ["refine_batches", "consensus_scale", "wall_spread", "icp_sim3", "scale_prediction", "normalize"]
