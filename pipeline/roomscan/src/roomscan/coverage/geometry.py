"""Small, testable geometry used by C2. Numpy only.

Camera convention (OpenCV, as MapAnything returns): x right, y down, z forward; ``c2w`` maps camera
coordinates to world coordinates. The room frame built in ``layout`` is Godot's: +Y up, metres.
"""

from __future__ import annotations

import numpy as np


def normalize(v: np.ndarray, axis: int = -1, eps: float = 1e-12) -> np.ndarray:
    n = np.linalg.norm(v, axis=axis, keepdims=True)
    return v / np.maximum(n, eps)


def transform_points(T: np.ndarray, pts: np.ndarray) -> np.ndarray:
    """Apply a 4x4 (or a 3x4) transform to (..., 3) points."""
    return pts @ T[:3, :3].T + T[:3, 3]


def invert_rigid(T: np.ndarray) -> np.ndarray:
    R, t = T[:3, :3], T[:3, 3]
    out = np.eye(4)
    out[:3, :3] = R.T
    out[:3, 3] = -R.T @ t
    return out


def umeyama(src: np.ndarray, dst: np.ndarray, weights: np.ndarray | None = None,
            with_scale: bool = True) -> tuple[float, np.ndarray, np.ndarray]:
    """Least-squares similarity (s, R, t) with dst ~= s * R @ src + t (Umeyama 1991)."""
    src = np.asarray(src, dtype=np.float64)
    dst = np.asarray(dst, dtype=np.float64)
    w = np.ones(len(src)) if weights is None else np.asarray(weights, dtype=np.float64)
    w = w / w.sum()
    mu_s = (w[:, None] * src).sum(0)
    mu_d = (w[:, None] * dst).sum(0)
    xs, xd = src - mu_s, dst - mu_d
    cov = (w[:, None] * xd).T @ xs
    U, S, Vt = np.linalg.svd(cov)
    D = np.eye(3)
    if np.linalg.det(U) * np.linalg.det(Vt) < 0:
        D[2, 2] = -1
    R = U @ D @ Vt
    var_s = (w * (xs ** 2).sum(1)).sum()
    s = float(np.trace(np.diag(S) @ D) / var_s) if with_scale and var_s > 0 else 1.0
    t = mu_d - s * R @ mu_s
    return s, R, t


def sim3_matrix(s: float, R: np.ndarray, t: np.ndarray) -> np.ndarray:
    T = np.eye(4)
    T[:3, :3] = s * R
    T[:3, 3] = t
    return T


def apply_sim3_to_pose(S: np.ndarray, c2w: np.ndarray) -> np.ndarray:
    """Move a camera-to-world pose through a similarity; the result stays a rigid pose."""
    s = np.cbrt(np.linalg.det(S[:3, :3]))
    R = S[:3, :3] / s
    out = np.eye(4)
    out[:3, :3] = R @ c2w[:3, :3]
    out[:3, 3] = S[:3, :3] @ c2w[:3, 3] + S[:3, 3]
    return out


def rotation_angle_deg(Ra: np.ndarray, Rb: np.ndarray) -> float:
    cos = (np.trace(Ra.T @ Rb) - 1) / 2
    return float(np.degrees(np.arccos(np.clip(cos, -1.0, 1.0))))


def grid_normals(pts: np.ndarray) -> np.ndarray:
    """Per-pixel normals of an (h, w, 3) point grid via central differences (zero at borders)."""
    n = np.zeros_like(pts, dtype=np.float32)
    dx = pts[1:-1, 2:] - pts[1:-1, :-2]
    dy = pts[2:, 1:-1] - pts[:-2, 1:-1]
    n[1:-1, 1:-1] = normalize(np.cross(dx, dy))
    return n


def fit_plane(pts: np.ndarray) -> tuple[np.ndarray, float]:
    """Least-squares plane n.x = d through points; n is unit length."""
    c = pts.mean(0)
    _, _, Vt = np.linalg.svd(pts - c, full_matrices=False)
    n = Vt[2]
    return n, float(n @ c)


def ransac_plane(pts: np.ndarray, *, threshold: float = 0.03, iterations: int = 300,
                 axis: np.ndarray | None = None, max_angle_deg: float = 25.0,
                 seed: int = 0) -> tuple[np.ndarray, float, np.ndarray] | None:
    """Robust plane, optionally constrained to a normal within ``max_angle_deg`` of ``axis``.

    Returns (normal, d, inlier mask) with the normal oriented along ``axis`` when given.
    """
    if len(pts) < 3:
        return None
    rng = np.random.default_rng(seed)
    best_mask, best_count = None, 0
    cos_lim = np.cos(np.radians(max_angle_deg))
    for _ in range(iterations):
        sample = pts[rng.choice(len(pts), 3, replace=False)]
        n = np.cross(sample[1] - sample[0], sample[2] - sample[0])
        norm = np.linalg.norm(n)
        if norm < 1e-9:
            continue
        n = n / norm
        if axis is not None and abs(n @ axis) < cos_lim:
            continue
        d = n @ sample[0]
        mask = np.abs(pts @ n - d) < threshold
        count = int(mask.sum())
        if count > best_count:
            best_mask, best_count = mask, count
    if best_mask is None or best_count < 3:
        return None
    n, d = fit_plane(pts[best_mask])
    if axis is not None and n @ axis < 0:
        n, d = -n, -d
    mask = np.abs(pts @ n - d) < threshold
    return n, d, mask


def rotation_between(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    """Smallest rotation taking unit vector a to unit vector b."""
    a, b = normalize(np.asarray(a, float)), normalize(np.asarray(b, float))
    v = np.cross(a, b)
    c = float(a @ b)
    if np.linalg.norm(v) < 1e-12:
        if c > 0:
            return np.eye(3)
        perp = normalize(np.cross(a, [1.0, 0, 0] if abs(a[0]) < 0.9 else [0, 1.0, 0]))
        return 2 * np.outer(perp, perp) - np.eye(3)
    vx = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + vx + vx @ vx * (1 / (1 + c))


def yaw_rotation(theta: float) -> np.ndarray:
    """Rotation about +Y by theta radians."""
    c, s = np.cos(theta), np.sin(theta)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def dominant_wall_yaw(normals_xz: np.ndarray, weights: np.ndarray | None = None) -> float:
    """Yaw (radians, in [0, pi/2)) of the dominant pair of perpendicular wall directions.

    ``normals_xz`` are horizontal normals as (x, z). Angles are folded modulo 90 degrees and the
    circular mean of 4*angle gives a robust estimate (Manhattan-world assumption).
    """
    ang = np.arctan2(normals_xz[:, 1], normals_xz[:, 0])
    w = np.ones(len(ang)) if weights is None else weights
    c = (w * np.cos(4 * ang)).sum()
    s = (w * np.sin(4 * ang)).sum()
    return float((np.arctan2(s, c) / 4) % (np.pi / 2))


def project(K: np.ndarray, pts_cam: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Pinhole projection of (..., 3) camera points -> (u, v) pixel coordinates and z."""
    z = pts_cam[..., 2]
    zs = np.where(np.abs(z) < 1e-9, 1e-9, z)
    u = K[0, 0] * pts_cam[..., 0] / zs + K[0, 2]
    v = K[1, 1] * pts_cam[..., 1] / zs + K[1, 2]
    return np.stack([u, v], -1), z
