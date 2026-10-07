"""Geometry helpers, GPU batch planning, merging batches and the ICP refinement, on synthetic data."""

from __future__ import annotations

import numpy as np
import pytest

import scene
from roomscan.coverage.chunks import merge_batches, plan_batches
from roomscan.coverage.geometry import (apply_sim3_to_pose, dominant_wall_yaw, ransac_plane, rotation_angle_deg,
                                        rotation_between, sim3_matrix, transform_points, umeyama)
from roomscan.coverage.refine import consensus_scale, refine_batches


def test_umeyama_recovers_a_similarity():
    rng = np.random.default_rng(0)
    R = scene.random_frame(rng)[:3, :3]
    src = rng.normal(size=(500, 3))
    dst = 1.7 * src @ R.T + np.array([0.3, -2.0, 5.0]) + rng.normal(scale=1e-3, size=(500, 3))
    s, R_est, t = umeyama(src, dst)
    assert s == pytest.approx(1.7, rel=1e-3)
    assert rotation_angle_deg(R_est, R) < 0.1
    assert np.allclose(t, [0.3, -2.0, 5.0], atol=5e-3)


def test_similarity_moves_a_camera_and_its_points_together():
    rng = np.random.default_rng(1)
    c2w = scene.random_frame(rng)
    pts_cam = rng.normal(size=(50, 3)) + [0, 0, 3]
    S = sim3_matrix(0.8, scene.random_frame(rng)[:3, :3], np.array([1.0, 2.0, 3.0]))
    moved = apply_sim3_to_pose(S, c2w)
    assert np.allclose(transform_points(S, transform_points(c2w, pts_cam)), transform_points(moved, 0.8 * pts_cam))
    assert np.allclose(moved[:3, :3] @ moved[:3, :3].T, np.eye(3))


def test_ransac_plane_ignores_clutter_and_respects_the_axis():
    rng = np.random.default_rng(2)
    floor = np.column_stack([rng.uniform(-2, 2, 800), rng.normal(0, 0.005, 800), rng.uniform(-2, 2, 800)])
    wall = np.column_stack([np.full(600, 1.5), rng.uniform(0, 2, 600), rng.uniform(-2, 2, 600)])
    clutter = rng.uniform(-2, 2, size=(300, 3))
    n, d, mask = ransac_plane(np.vstack([floor, wall, clutter]), axis=np.array([0, 1.0, 0]), max_angle_deg=20)
    assert n[1] > 0.99 and abs(d) < 0.01 and mask[:800].mean() > 0.95


def test_wall_yaw_and_rotation_between():
    yaw = np.radians(23.0)
    normals = np.array([[np.cos(yaw + k * np.pi / 2), np.sin(yaw + k * np.pi / 2)] for k in range(4)] * 20)
    assert dominant_wall_yaw(normals) == pytest.approx(yaw, abs=1e-6)
    a, b = np.array([0.2, 0.9, -0.1]), np.array([0.0, 1.0, 0.0])
    R = rotation_between(a, b)
    assert np.allclose(R @ (a / np.linalg.norm(a)), b)
    assert np.allclose(rotation_between(b, -b) @ b, -b)


def test_plan_batches_covers_every_photo_once():
    batches = plan_batches(50, 12, 4)
    assert all(len(b.views) <= 12 for b in batches)
    keyframes = set(batches[0].views)
    new = [v for b in batches[1:] for v in b.new]
    assert sorted(new + sorted(keyframes)) == list(range(50))
    assert len(new) == len(set(new))
    assert all(set(b.anchors) <= keyframes and len(b.anchors) == 4 for b in batches[1:])
    assert [b.views for b in plan_batches(9, 12, 4)] == [list(range(9))]


def _setup(n_cams=24, seed=0):
    room = scene.garage()
    cams = scene.ring_cameras(n_cams, radius=0.7, height=1.4, centre=(-0.3, -0.6), pitch_target_y=1.0)
    rng = np.random.default_rng(seed)
    return room, cams, rng


def _predict(room, cams, batches, rng, corrupt=()):
    results = []
    for k, b in enumerate(batches):
        frame = scene.random_frame(rng)
        scale = 1 + rng.uniform(-0.05, 0.05)
        preds = {}
        for v in b.views:
            c2w = cams[v] if k not in corrupt else scene.random_frame(rng) @ cams[v]
            preds[v] = scene.prediction(room, c2w, frame, scale)
        results.append(preds)
    return results


def _relative_errors(merged, cams):
    """Camera positions after the best similarity to the truth: the merge is right up to one."""
    views = sorted(merged)
    est = np.array([merged[v]["c2w"][:3, 3] for v in views])
    true = np.array([cams[v][:3, 3] for v in views])
    s, R, t = umeyama(est, true)
    return np.linalg.norm(s * est @ R.T + t - true, axis=1)


def test_merge_puts_every_batch_in_one_frame():
    room, cams, rng = _setup()
    batches = plan_batches(len(cams), 10, 3)
    merged, reports = merge_batches(batches, _predict(room, cams, batches, rng), log=lambda *_: None)
    assert sorted(merged) == list(range(len(cams)))
    assert all(r["merged"] for r in reports)
    assert _relative_errors(merged, cams).max() < 0.02


def test_merge_refuses_a_batch_its_anchors_disagree_with():
    room, cams, rng = _setup(seed=4)
    batches = plan_batches(len(cams), 10, 3)
    merged, reports = merge_batches(batches, _predict(room, cams, batches, rng, corrupt={2}), log=lambda *_: None)
    assert reports[2]["merged"] is False
    assert not set(batches[2].new) & set(merged)
    assert _relative_errors(merged, cams).max() < 0.02


def test_refinement_pulls_a_slipped_batch_back():
    room, cams, rng = _setup(seed=7)
    batches = plan_batches(len(cams), 10, 3)
    merged, _ = merge_batches(batches, _predict(room, cams, batches, rng), log=lambda *_: None)
    # Slip batch 1 by 3 degrees and 12 cm, as when two predictions of the anchors disagree.
    a = np.radians(3.0)
    slip = sim3_matrix(1.02, np.array([[np.cos(a), 0, np.sin(a)], [0, 1, 0], [-np.sin(a), 0, np.cos(a)]]),
                       np.array([0.12, 0.0, -0.05]))
    from roomscan.coverage.chunks import move_prediction

    slipped = {v: (move_prediction(p, slip) | {"batch": p["batch"]}) if p["batch"] == 1 else p for v, p in merged.items()}
    before = _relative_errors(slipped, cams).max()
    refined, report = refine_batches(slipped, log=lambda *_: None)
    after = _relative_errors(refined, cams).max()
    assert before > 0.08 and after < 0.03, (before, after)
    assert any(h["batch"] == 1 and h["accepted"] for h in report["history"])


def test_consensus_scale_reports_the_spread():
    out = consensus_scale({0: 1.0, 1: 0.9, 2: 0.8})
    assert out["median_batch_scale"] == pytest.approx(0.9)
    assert out["factor"] == pytest.approx(1 / 0.9)
    assert out["spread_pct"] == pytest.approx(11.1, abs=0.1)
