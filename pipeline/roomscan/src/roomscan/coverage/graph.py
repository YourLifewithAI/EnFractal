"""View graph: which photos see the same surfaces, and the largest group they form.

The graph is built from the merged reconstruction itself: photo i links to photo j when a good share
of the surface points photo i sees project into photo j, in front of its camera, at the depth photo
j measured there. A photo the model placed somewhere wrong sees surfaces that no other photo
confirms, so it falls out of the largest component and is reported as not fitted.
"""

from __future__ import annotations

from typing import Any

import numpy as np

MIN_OVERLAP = 0.10


def overlap_matrix(preds: list[dict[str, np.ndarray] | None], *, samples: int = 1500, rel_tol: float = 0.10,
                   abs_tol: float = 0.05, seed: int = 0) -> np.ndarray:
    """O[i, j]: the share of photo i's sampled surface points that photo j also sees.

    A point of photo i counts as seen by photo j when it lands inside j's image in front of the
    camera and j's own depth there agrees within ``rel_tol`` of that depth plus ``abs_tol`` metres,
    so a wall hidden behind a shelf in photo j does not count. ``None`` (an unplaced photo) gives a
    zero row and column. The diagonal is 1 for placed photos.
    """
    n = len(preds)
    O = np.zeros((n, n), np.float32)
    placed = [i for i, p in enumerate(preds) if p is not None]
    if not placed:
        return O
    rng = np.random.default_rng(seed)
    shapes = {preds[j]["pts_cam"].shape[:2] for j in placed}
    if len(shapes) != 1:
        raise ValueError(f"point maps differ in size: {sorted(shapes)}")
    h, w = shapes.pop()
    depth = np.stack([preds[j]["pts_cam"][..., 2].astype(np.float32) for j in placed])  # (m, h, w)
    valid = np.stack([preds[j]["mask"].astype(bool) & np.isfinite(depth[k]) & (depth[k] > 0)
                      for k, j in enumerate(placed)])
    w2c = np.stack([np.linalg.inv(preds[j]["c2w"]) for j in placed])  # (m, 4, 4)
    K = np.stack([preds[j]["K"] for j in placed])  # (m, 3, 3) of the stored map
    R, t = w2c[:, :3, :3], w2c[:, :3, 3]
    for i in placed:
        p = preds[i]
        pts = p["pts_cam"].astype(np.float64)
        ok = p["mask"].astype(bool) & np.isfinite(pts).all(-1) & (pts[..., 2] > 0)
        idx = np.flatnonzero(ok.ravel())
        if len(idx) == 0:
            continue
        pick = rng.choice(idx, size=min(samples, len(idx)), replace=False)
        cam_pts = pts.reshape(-1, 3)[pick]
        world = cam_pts @ p["c2w"][:3, :3].T + p["c2w"][:3, 3]  # (s, 3)
        Xc = np.einsum("mab,sb->msa", R, world) + t[:, None, :]  # (m, s, 3)
        z = Xc[..., 2]
        zs = np.where(z > 1e-6, z, 1.0)
        u = K[:, 0, 0, None] * Xc[..., 0] / zs + K[:, 0, 2, None]
        v = K[:, 1, 1, None] * Xc[..., 1] / zs + K[:, 1, 2, None]
        ui = np.floor(u).astype(np.int64)
        vi = np.floor(v).astype(np.int64)
        inside = (z > 0.05) & (ui >= 0) & (ui < w) & (vi >= 0) & (vi < h)
        m_idx = np.broadcast_to(np.arange(len(placed))[:, None], ui.shape)
        uc, vc = np.clip(ui, 0, w - 1), np.clip(vi, 0, h - 1)
        d_j = depth[m_idx, vc, uc]
        ok_j = valid[m_idx, vc, uc]
        seen = inside & ok_j & (np.abs(z - d_j) < rel_tol * d_j + abs_tol)
        O[i, placed] = seen.mean(1)
        O[i, i] = 1.0
    return O


def build_view_graph(O: np.ndarray, *, min_overlap: float = MIN_OVERLAP) -> dict[str, Any]:
    """Links, components and the photos fitted into the room (the largest component).

    Two photos link when either sees at least ``min_overlap`` of what the other sees: a close-up
    of a shelf sits inside a wide shot of the wall even though the wide shot sees far more.
    """
    n = len(O)
    S = np.maximum(O, O.T)
    placed = np.diag(O) > 0
    adj = (S >= min_overlap) & placed[:, None] & placed[None, :]
    np.fill_diagonal(adj, False)
    comps = [c for c in components(adj) if placed[c[0]]]
    main = set(comps[0]) if comps else set()
    edges = [(int(i), int(j), round(float(S[i, j]), 3)) for i, j in zip(*np.nonzero(np.triu(adj, 1)))]
    degree = adj.sum(1).astype(int)
    return {
        "min_overlap": min_overlap,
        "edges": edges,
        "components": comps,
        "registered": [bool(i in main) for i in range(n)],
        "placed": placed.tolist(),
        "degree": degree.tolist(),
        "weakly_linked": [i for i in sorted(main) if degree[i] == 1],
        "not_fitted": [i for i in range(n) if placed[i] and i not in main],
        "unplaced": [i for i in range(n) if not placed[i]],
    }


def components(adjacency: np.ndarray) -> list[list[int]]:
    """Connected components of a boolean adjacency matrix, largest first."""
    n = len(adjacency)
    seen = [False] * n
    comps = []
    for s in range(n):
        if seen[s]:
            continue
        stack, comp = [s], []
        seen[s] = True
        while stack:
            a = stack.pop()
            comp.append(a)
            for b in np.flatnonzero(adjacency[a]):
                if not seen[b]:
                    seen[b] = True
                    stack.append(int(b))
        comps.append(sorted(comp))
    comps.sort(key=lambda c: (-len(c), c[0]))
    return comps
