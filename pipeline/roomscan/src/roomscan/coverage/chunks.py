"""Batching a photo set to fit the GPU, and merging the batches into one metric frame.

Plan: one *keyframe* batch spread evenly over the capture sequence defines the room frame. Every
other photo goes into a batch together with the keyframes nearest to it in capture order (the
*anchors*). Each batch is reconstructed in its own metric frame; a similarity transform fitted on
the anchors' point maps (pixel-for-pixel correspondences between the two predictions of the same
photo) moves it into the room frame. Anchor disagreement after the fit is the merge's error bar.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import numpy as np

from .geometry import apply_sim3_to_pose, rotation_angle_deg, sim3_matrix, transform_points, umeyama


@dataclass
class Batch:
    views: list[int]  # indices into the ordered view list; anchors come first
    anchors: list[int] = field(default_factory=list)

    @property
    def new(self) -> list[int]:
        return [v for v in self.views if v not in self.anchors]


def plan_batches(n: int, batch_size: int, anchors: int) -> list[Batch]:
    if n <= 0:
        return []
    if n <= batch_size:
        return [Batch(list(range(n)))]
    if anchors >= batch_size - 1:
        raise ValueError("anchors must leave room for new views in each batch")
    keyframes = sorted(set(int(round(x)) for x in np.linspace(0, n - 1, batch_size)))
    batches = [Batch(keyframes)]
    rest = [i for i in range(n) if i not in set(keyframes)]
    group = batch_size - anchors
    count = int(np.ceil(len(rest) / group))
    # Even groups (no tiny last batch).
    for chunk in np.array_split(np.array(rest), count):
        chunk = [int(x) for x in chunk]
        center = (chunk[0] + chunk[-1]) / 2
        near = sorted(keyframes, key=lambda k: (abs(k - center), k))[:anchors]
        near.sort()
        batches.append(Batch(near + chunk, anchors=near))
    return batches


def _grow(seed: int, pool: set[int], size: int, W: np.ndarray, extra: set[int] | None = None) -> list[int]:
    """Greedy: keep adding the pool photo most strongly linked to the group (and to ``extra``)."""
    group = [seed]
    linked_to = set(group) | (extra or set())
    candidates = set(pool) - {seed}
    while len(group) < size and candidates:
        cand = sorted(candidates)
        scores = W[np.ix_(cand, sorted(linked_to))].sum(1)
        best = int(np.argmax(scores))
        if scores[best] <= 0:
            break
        pick = cand[best]
        group.append(pick)
        linked_to.add(pick)
        candidates.remove(pick)
    return group


def plan_graph_batches(W: np.ndarray, nodes: list[int], batch_size: int, anchors: int) -> list[Batch]:
    """Batches that follow the feature graph, so every photo in a batch overlaps others in it.

    ``W`` is a symmetric matrix of verified match counts (zero where photos are not linked).
    The first batch grows from the best-connected photo; each later batch starts at the remaining
    photo most linked to what is already done, grows through remaining photos, and takes as anchors
    the done photos most linked to its new photos.
    """
    pool = set(nodes)
    if not pool:
        return []
    order = sorted(pool)
    seed = max(order, key=lambda n: (W[n, order].sum(), -n))
    first = _grow(seed, pool, batch_size, W)
    batches = [Batch(sorted(first))]
    done = set(first)
    while pool - done:
        rem = pool - done
        rem_list, done_list = sorted(rem), sorted(done)
        link = W[np.ix_(rem_list, done_list)].sum(1)
        if link.max() <= 0:
            break  # the rest is not linked to what is done (cannot happen inside one component)
        start = rem_list[int(np.argmax(link))]
        new = _grow(start, rem, batch_size - anchors, W, extra=done)
        weight_to_new = W[np.ix_(done_list, new)].sum(1)
        ranked = [done_list[i] for i in np.argsort(-weight_to_new, kind="stable") if weight_to_new[i] > 0]
        anc = sorted(ranked[:anchors])
        if len(anc) < 2:  # too few links: still try, with the strongest done photos
            anc = sorted(done_list[i] for i in np.argsort(-weight_to_new, kind="stable")[:anchors])
        batches.append(Batch(anc + sorted(new), anchors=anc))
        done |= set(new)
    return batches


def _world_points(pred: dict[str, np.ndarray], scale: float = 1.0) -> tuple[np.ndarray, np.ndarray]:
    pts = pred["pts_cam"].astype(np.float64) * scale
    return transform_points(pred["c2w"], pts), pred["conf"].astype(np.float64)


def _anchor_errors(S: np.ndarray, room_preds, batch_preds, anchors) -> list[tuple[float, float]]:
    out = []
    for a in anchors:
        moved = apply_sim3_to_pose(S, batch_preds[a]["c2w"])
        ref = room_preds[a]["c2w"]
        out.append((float(np.linalg.norm(moved[:3, 3] - ref[:3, 3])), rotation_angle_deg(moved[:3, :3], ref[:3, :3])))
    return out


def consensus_anchors(room_preds, batch_preds, anchors: list[int], *, centre_tol: float = 0.35,
                      rot_tol: float = 12.0) -> list[int]:
    """Anchors that agree with each other: each anchor proposes the similarity its own two
    predictions imply; the proposal most other anchors agree with wins."""
    best: list[int] = []
    for a in anchors:
        try:
            fit = fit_batch_to_room(room_preds, batch_preds, [a], iterations=2, use_consensus=False)
        except ValueError:
            continue
        errs = _anchor_errors(fit["sim3"], room_preds, batch_preds, anchors)
        agree = [b for b, (c, r) in zip(anchors, errs) if c < centre_tol and r < rot_tol]
        if len(agree) > len(best):
            best = agree
    return best


def fit_batch_to_room(room_preds: dict[int, dict[str, np.ndarray]], batch_preds: dict[int, dict[str, np.ndarray]],
                      anchors: list[int], *, samples_per_anchor: int = 3000, seed: int = 0,
                      iterations: int = 3, keep_fraction: float = 0.8, use_consensus: bool = True) -> dict[str, Any]:
    """Similarity taking the batch frame to the room frame, from the anchors' point maps."""
    all_anchors = list(anchors)
    if use_consensus and len(anchors) >= 3:
        agreeing = consensus_anchors(room_preds, batch_preds, anchors)
        if len(agreeing) >= 2:
            anchors = agreeing
    rng = np.random.default_rng(seed)
    src_all, dst_all, w_all, owner = [], [], [], []
    for a in anchors:
        pr, pb = room_preds[a], batch_preds[a]
        dst, conf_r = _world_points(pr)
        src, conf_b = _world_points(pb)
        valid = pr["mask"] & pb["mask"] & np.isfinite(dst).all(-1) & np.isfinite(src).all(-1)
        idx = np.flatnonzero(valid.ravel())
        if len(idx) < 50:
            continue
        pick = rng.choice(idx, size=min(samples_per_anchor, len(idx)), replace=False)
        src_all.append(src.reshape(-1, 3)[pick])
        dst_all.append(dst.reshape(-1, 3)[pick])
        w_all.append(np.minimum(conf_r.ravel()[pick], conf_b.ravel()[pick]))
        owner.append(np.full(len(pick), a))
    if not src_all:
        raise ValueError("no usable anchor points to align this batch")
    src, dst, w, owner_arr = (np.concatenate(x) for x in (src_all, dst_all, w_all, owner))
    w = np.clip(w, 1e-3, None)
    keep = np.ones(len(src), bool)
    for _ in range(iterations):
        s, R, t = umeyama(src[keep], dst[keep], w[keep])
        res = np.linalg.norm(s * src @ R.T + t - dst, axis=1)
        cut = np.quantile(res, keep_fraction)
        keep = res <= cut
    s, R, t = umeyama(src[keep], dst[keep], w[keep])
    S = sim3_matrix(s, R, t)
    res = np.linalg.norm(s * src @ R.T + t - dst, axis=1)
    # Per-anchor agreement of camera poses after the fit (all anchors, used or not).
    per_anchor = []
    for a in all_anchors:
        moved = apply_sim3_to_pose(S, batch_preds[a]["c2w"])
        ref = room_preds[a]["c2w"]
        per_anchor.append({
            "view": a,
            "center_error_m": float(np.linalg.norm(moved[:3, 3] - ref[:3, 3])),
            "rotation_error_deg": rotation_angle_deg(moved[:3, :3], ref[:3, :3]),
            "point_residual_m": float(np.median(res[owner_arr == a])) if (owner_arr == a).any() else None,
            "used": a in anchors,
        })
    return {
        "sim3": S,
        "scale": float(s),
        "median_point_residual_m": float(np.median(res[keep])),
        "anchors": per_anchor,
        "anchors_used": len(anchors),
        "median_center_error_m": float(np.median([p["center_error_m"] for p in per_anchor if p["used"]])),
        "median_rotation_error_deg": float(np.median([p["rotation_error_deg"] for p in per_anchor if p["used"]])),
    }


def move_prediction(pred: dict[str, np.ndarray], S: np.ndarray) -> dict[str, np.ndarray]:
    """A prediction expressed in the room frame: rigid pose moved, point map rescaled."""
    s = float(np.cbrt(np.linalg.det(S[:3, :3])))
    out = dict(pred)
    out["c2w"] = apply_sim3_to_pose(S, pred["c2w"])
    out["pts_cam"] = (pred["pts_cam"].astype(np.float32) * s).astype(np.float16)
    return out


def merge_batches(batches: list[Batch], results: list[dict[int, dict[str, np.ndarray]]], *,
                  max_rotation_deg: float = 15.0, max_centre_m: float = 0.5,
                  log=print) -> tuple[dict[int, dict[str, np.ndarray]], list[dict[str, Any]]]:
    """All views in the first batch's frame.

    A batch whose anchors do not agree after the fit (rotation or centre error above the limits,
    or fewer than two agreeing anchors) is not merged: its new photos are reported as unplaced
    rather than put somewhere wrong.
    """
    room: dict[int, dict[str, np.ndarray]] = dict(results[0])
    for v in results[0]:
        room[v]["batch"] = 0
    reports: list[dict[str, Any]] = [{"batch": 0, "views": len(batches[0].views), "role": "seed", "merged": True}]
    for b, (batch, preds) in enumerate(zip(batches[1:], results[1:]), start=1):
        anchors = [a for a in batch.anchors if a in room]
        report: dict[str, Any] = {"batch": b, "views": len(batch.views), "new_views": len(batch.new)}
        fit = None
        if len(anchors) >= 2:
            try:
                fit = fit_batch_to_room(room, preds, anchors)
            except ValueError:
                fit = None
        ok = (fit is not None and fit["anchors_used"] >= 2 and fit["median_rotation_error_deg"] <= max_rotation_deg
              and fit["median_center_error_m"] <= max_centre_m)
        if fit is not None:
            report.update({k: v for k, v in fit.items() if k != "sim3"})
        report["merged"] = bool(ok)
        if ok:
            for v in batch.new:
                room[v] = move_prediction(preds[v], fit["sim3"])
                room[v]["batch"] = b
        reports.append(report)
        if fit is None:
            log(f"  batch {b}: not merged (fewer than two usable anchors)")
        else:
            log(f"  batch {b}: {'merged' if ok else 'NOT merged'}; {fit['anchors_used']}/{len(anchors)} anchors agree, "
                f"scale {fit['scale']:.3f}, centre error {fit['median_center_error_m']:.3f} m, "
                f"rotation {fit['median_rotation_error_deg']:.1f} deg")
    return room, reports
