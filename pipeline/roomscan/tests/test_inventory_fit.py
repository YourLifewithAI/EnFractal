"""Box fitting for the inventory: sizes, turn, which way it faces, on synthetic points."""

from __future__ import annotations

import math

import numpy as np
import pytest

from roomscan.inventory.fit import (Box, box_corners_xz, face_away_from, fit_box, forward_xz,
                                    front_from_height_profile, front_from_viewers, right_xz, to_local_xz)


def box_surface_points(centre, size, yaw_deg, n=4000, seed=0):
    """Points on the top and four sides of a box turned by ``yaw_deg``, as a depth camera would see them, with 5 mm noise."""
    rng = np.random.default_rng(seed)
    w, h, d = size
    x = rng.uniform(-w / 2, w / 2, n)
    z = rng.uniform(-d / 2, d / 2, n)
    y = rng.uniform(0, h, n)
    face = rng.integers(0, 5, n)
    x = np.where(face == 0, -w / 2, np.where(face == 1, w / 2, x))
    z = np.where(face == 2, -d / 2, np.where(face == 3, d / 2, z))
    y = np.where(face == 4, h, y)
    r, back = right_xz(yaw_deg), -forward_xz(yaw_deg)
    xz = np.array([centre[0], centre[2]]) + x[:, None] * r + z[:, None] * back
    pts = np.stack([xz[:, 0], y + centre[1], xz[:, 1]], -1)
    return pts + rng.normal(scale=0.005, size=pts.shape)


@pytest.mark.parametrize("yaw", [0.0, 30.0, -60.0, 90.0, 135.0])
def test_a_turned_box_comes_back_with_its_size_place_and_turn(yaw):
    pts = box_surface_points((1.0, 0.0, -0.5), (0.8, 0.3, 0.5), yaw)
    box = fit_box(pts)
    assert box.size_m == pytest.approx((0.8, 0.3, 0.5), abs=0.03)
    assert box.centre_m[0] == pytest.approx(1.0, abs=0.02) and box.centre_m[2] == pytest.approx(-0.5, abs=0.02)
    assert box.base_y == 0.0 and box.size_m[1] == pytest.approx(0.3, abs=0.02)
    # The width's direction matches the true one up to a half turn.
    got, want = right_xz(box.yaw_deg), right_xz(yaw)
    assert abs(float(got @ want)) > math.cos(math.radians(3))


def test_the_wider_side_is_the_width_and_a_surface_object_keeps_its_height_off_the_floor():
    pts = box_surface_points((0.0, 0.75, 0.0), (0.3, 0.02, 0.4), 20.0, n=3000)
    box = fit_box(pts)
    assert box.size_m[0] > box.size_m[2]  # 0.4 wide, 0.3 deep after the swap
    assert box.base_y == pytest.approx(0.75, abs=0.01)
    assert any("surface" in n for n in box.notes)


def test_round_things_get_a_square_axis_aligned_box():
    rng = np.random.default_rng(1)
    a = rng.uniform(0, 2 * math.pi, 3000)
    pts = np.stack([0.04 * np.cos(a) + 1.0, rng.uniform(0.0, 0.1, 3000), 0.04 * np.sin(a) - 1.0], -1)
    box = fit_box(pts, round_footprint=True)
    assert box.size_m[0] == pytest.approx(box.size_m[2], abs=0.005)
    assert box.size_m[0] == pytest.approx(0.08, abs=0.01)
    assert box.yaw_deg == 0.0


def test_a_sofa_faces_away_from_its_backrest():
    # A 1.8 x 0.9 m sofa: 0.4 m seat in front, a backrest 0.8 m tall along the +Z (behind) side.
    rng = np.random.default_rng(2)
    seat = np.stack([rng.uniform(-0.9, 0.9, 1500), rng.uniform(0.0, 0.4, 1500), rng.uniform(-0.45, 0.15, 1500)], -1)
    back = np.stack([rng.uniform(-0.9, 0.9, 1500), rng.uniform(0.0, 0.8, 1500), rng.uniform(0.15, 0.45, 1500)], -1)
    local = np.concatenate([seat, back])
    yaw = 50.0
    r, behind = right_xz(yaw), -forward_xz(yaw)
    xz = local[:, 0:1] * r + local[:, 2:3] * behind + np.array([2.0, -1.0])
    pts = np.stack([xz[:, 0], local[:, 1], xz[:, 1]], -1)
    box = fit_box(pts)
    front = front_from_height_profile(pts, box)
    assert front is not None
    # The true front is the sofa's own -Z direction.
    assert float(front @ forward_xz(yaw)) > 0.95
    turned = face_away_from(box, front)
    assert turned.front_known and float(forward_xz(turned.yaw_deg) @ front) > 0.99


def test_no_height_difference_means_no_opinion_and_viewers_decide():
    rng = np.random.default_rng(3)
    pts = np.stack([rng.uniform(-0.3, 0.3, 800), rng.uniform(0, 0.02, 800), rng.uniform(-0.2, 0.2, 800)], -1)
    box = fit_box(pts)
    assert front_from_height_profile(pts, box) is None
    cams = np.array([[0.0, 2.0], [0.4, 1.8]])
    f = front_from_viewers(box, cams)
    assert f[1] > 0.9  # toward +Z, where the cameras are


def test_footprint_and_local_coordinates_agree():
    box = Box((1.0, 0.5, 2.0), (0.6, 1.0, 0.4), 30.0, 0.0)
    corners = box_corners_xz(box)
    local = to_local_xz(corners, (1.0, 2.0), 30.0)
    assert sorted(np.round(np.abs(local[:, 0]), 3).tolist()) == [0.3] * 4
    assert sorted(np.round(np.abs(local[:, 1]), 3).tolist()) == [0.2] * 4
    x0, z0, x1, z1 = box.footprint
    assert x0 < 1.0 < x1 and z0 < 2.0 < z1


def test_too_few_points_are_refused():
    with pytest.raises(ValueError):
        fit_box(np.zeros((3, 3)))
