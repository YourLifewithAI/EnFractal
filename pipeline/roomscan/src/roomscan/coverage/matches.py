"""Feature-verified view graph (CPU): SIFT matches checked against an essential matrix.

This is the independent check on the learned poses. Two photos are linked when enough SIFT
matches agree with one rigid camera motion, using the EXIF focal length. Components of this graph
are the islands a reconstruction can actually hold together; a photo outside the largest island is
not registered, whatever pose a feed-forward model invents for it.
"""

from __future__ import annotations

import concurrent.futures as cf
import io
import os
from pathlib import Path
from typing import Any

import cv2
import numpy as np

SIFT_VERSION = 1
LONG_SIDE = 1024
FEATURES = 3000
MIN_INLIERS = 40


def _intrinsics(photo: dict[str, Any], scale: float, shape: tuple[int, int]) -> np.ndarray | None:
    exif = photo.get("exif") or {}
    f35 = exif.get("focal_length_35mm")
    W, H = photo["image"]["width"], photo["image"]["height"]
    if not f35:
        return None
    f = f35 / 43.2666 * float(np.hypot(W, H)) * scale
    return np.array([[f, 0, shape[1] / 2], [0, f, shape[0] / 2], [0, 0, 1.0]])


def extract(job: dict[str, Any]) -> dict[str, Any]:
    """Worker: SIFT keypoints (normalised camera coordinates) and descriptors for one photo."""
    cache = Path(job["cache"])
    if cache.is_file():
        data = np.load(cache)
        return {"sha": job["sha"], "pts": data["pts"], "des": data["des"]}
    img = cv2.imread(job["jpeg"], cv2.IMREAD_GRAYSCALE)
    s = LONG_SIDE / max(img.shape)
    img = cv2.resize(img, None, fx=s, fy=s, interpolation=cv2.INTER_AREA)
    K = _intrinsics(job["photo"], s, img.shape)
    sift = cv2.SIFT_create(nfeatures=FEATURES)
    kp, des = sift.detectAndCompute(img, None)
    if des is None or K is None:
        pts, des = np.zeros((0, 2), np.float32), np.zeros((0, 128), np.float32)
    else:
        raw = np.float32([k.pt for k in kp]).reshape(-1, 1, 2)
        pts = cv2.undistortPoints(raw, K, None).reshape(-1, 2).astype(np.float32)
        des = des.astype(np.float32)
    buf = io.BytesIO()
    np.savez_compressed(buf, pts=pts, des=des)
    cache.parent.mkdir(parents=True, exist_ok=True)
    tmp = cache.with_name(cache.name + ".part")
    tmp.write_bytes(buf.getvalue())
    os.replace(tmp, cache)
    return {"sha": job["sha"], "pts": pts, "des": des}


_FEATS: dict[int, tuple[np.ndarray, np.ndarray]] = {}


def _init_worker(feats: dict[int, tuple[np.ndarray, np.ndarray]]) -> None:
    global _FEATS
    _FEATS = feats


def match_pair(pair: tuple[int, int], focal_px: float = 800.0) -> tuple[int, int, int, list[list[float]] | None]:
    """Worker: inlier count and relative rotation (camera i to camera j) for one pair."""
    i, j = pair
    pi, di = _FEATS[i]
    pj, dj = _FEATS[j]
    if len(di) < 20 or len(dj) < 20:
        return i, j, 0, None
    flann = cv2.FlannBasedMatcher({"algorithm": 1, "trees": 4}, {"checks": 48})
    knn = flann.knnMatch(di, dj, k=2)
    good = [m[0] for m in knn if len(m) == 2 and m[0].distance < 0.75 * m[1].distance]
    if len(good) < MIN_INLIERS:
        return i, j, len(good), None
    a = pi[[g.queryIdx for g in good]].reshape(-1, 1, 2)
    b = pj[[g.trainIdx for g in good]].reshape(-1, 1, 2)
    E, mask = cv2.findEssentialMat(a, b, np.eye(3), method=cv2.RANSAC, prob=0.999, threshold=1.5 / focal_px)
    if E is None or E.shape != (3, 3):
        return i, j, 0, None
    n, R, _, _ = cv2.recoverPose(E, a, b, np.eye(3), mask=mask)
    return i, j, int(n), R.tolist()


def feature_graph(room_dir: Path, views: list[dict[str, Any]], *, workers: int = 6, log=print) -> dict[str, Any]:
    cache_dir = room_dir / "cache" / f"sift-v{SIFT_VERSION}"
    jobs = [{"sha": p["sha256"], "jpeg": str(room_dir / p["derived"]["jpeg"]), "photo": p,
             "cache": str(cache_dir / f"{p['sha256']}.npz")} for p in views]
    feats: dict[int, tuple[np.ndarray, np.ndarray]] = {}
    with cf.ProcessPoolExecutor(max_workers=workers) as pool:
        for idx, result in enumerate(pool.map(extract, jobs)):
            feats[idx] = (result["pts"], result["des"])
    n = len(views)
    pairs = [(i, j) for i in range(n) for j in range(i + 1, n)]
    log(f"Matching {len(pairs)} photo pairs on the CPU ({workers} workers)...")
    inliers = np.zeros((n, n), int)
    rotations: dict[tuple[int, int], list[list[float]]] = {}
    with cf.ProcessPoolExecutor(max_workers=workers, initializer=_init_worker, initargs=(feats,)) as pool:
        for i, j, count, R in pool.map(match_pair, pairs, chunksize=64):
            inliers[i, j] = inliers[j, i] = count
            if R is not None and count >= MIN_INLIERS:
                rotations[(i, j)] = R
    return {"inliers": inliers, "rotations": rotations, "min_inliers": MIN_INLIERS}
