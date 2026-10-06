"""View graph summaries: components, links between consecutive photos, pose checks."""

from __future__ import annotations

from typing import Any

import numpy as np

from .geometry import rotation_angle_deg


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


def summarize(inliers: np.ndarray, min_inliers: int) -> dict[str, Any]:
    """Links, components and how often a photo links to the one taken just before it."""
    adj = inliers >= min_inliers
    np.fill_diagonal(adj, False)
    comps = components(adj)
    n = len(inliers)
    consecutive = [bool(adj[i, i + 1]) for i in range(n - 1)]
    return {
        "min_inliers": min_inliers,
        "links": int(np.triu(adj, 1).sum()),
        "degree": adj.sum(1).astype(int).tolist(),
        "components": comps,
        "main": comps[0] if comps else [],
        "consecutive_linked": consecutive,
        "consecutive_link_rate": round(float(np.mean(consecutive)), 3) if consecutive else 0.0,
    }


def rotation_check(preds: dict[int, dict[str, np.ndarray]], rotations: dict[tuple[int, int], Any],
                   inliers: np.ndarray, *, min_inliers: int = 60) -> dict[str, Any]:
    """Compare relative rotations of the merged poses with the SIFT essential-matrix rotations."""
    errs, cross = [], []
    for (i, j), R_sift in rotations.items():
        if inliers[i, j] < min_inliers or i not in preds or j not in preds:
            continue
        R_pose = preds[j]["c2w"][:3, :3].T @ preds[i]["c2w"][:3, :3]
        e = rotation_angle_deg(np.asarray(R_sift), R_pose)
        errs.append(e)
        if preds[i].get("batch") != preds[j].get("batch"):
            cross.append(e)
    def stats(values):
        if not values:
            return {"pairs": 0}
        v = np.asarray(values)
        return {"pairs": len(v), "median_deg": round(float(np.median(v)), 2),
                "within_5_deg": round(float((v < 5).mean()), 3), "within_10_deg": round(float((v < 10).mean()), 3)}
    return {"min_inliers": min_inliers, "all": stats(errs), "across_batches": stats(cross)}
