"""View graph, room frame and coverage grids on a ray-cast synthetic room."""

from __future__ import annotations

import numpy as np
import pytest

import scene
from roomscan.coverage.geometry import transform_points
from roomscan.coverage.graph import build_view_graph, overlap_matrix
from roomscan.coverage.grid import GOOD_VIEWS, WALLS, floor_grid, regions, runs, wall_axes, wall_grid
from roomscan.coverage.visibility import Views
from roomscan.coverage.layout import camera_forward, camera_up, collect_points, fit_room


def test_overlap_counts_shared_surfaces_and_respects_occlusion():
    room = scene.Room().add((-0.5, 0.0, -1.6), (0.5, 1.5, -1.2), "box")
    facing_a = scene.prediction(room, scene.look_at((0.0, 1.2, 1.0), (0.0, 1.2, -2.5)))
    facing_a2 = scene.prediction(room, scene.look_at((0.4, 1.2, 0.8), (0.0, 1.2, -2.5)))
    facing_c = scene.prediction(room, scene.look_at((0.0, 1.2, -0.5), (0.0, 1.2, 2.5)))
    # Same wall, but the camera stands behind the box: the wall behind it is hidden.
    behind = scene.prediction(room, scene.look_at((0.0, 0.6, -1.0), (0.0, 0.6, -2.5)))
    O = overlap_matrix([facing_a, facing_a2, facing_c, behind])
    assert O[0, 1] > 0.5 and O[1, 0] > 0.5
    assert O[0, 2] < 0.05 and O[2, 0] < 0.05
    assert np.allclose(np.diag(O), 1.0)
    # The box face seen from in front is not on the far wall that the 'behind' camera sees.
    assert O[0, 3] < O[0, 1]


def test_a_misplaced_photo_falls_out_of_the_graph():
    room = scene.garage()
    cams = scene.ring_cameras(24, radius=0.7, height=1.4, centre=(-0.3, -0.6), pitch_target_y=1.0)
    preds = [scene.prediction(room, c) for c in cams]
    wrong = dict(preds[5])
    turn = np.eye(4)
    turn[:3, :3] = np.array([[-1.0, 0, 0], [0, 1, 0], [0, 0, -1.0]])  # half a turn about the vertical
    turn[:3, 3] = [0.8, 0.6, 0.3]
    wrong["c2w"] = turn @ preds[5]["c2w"]  # the model put it somewhere no other photo agrees with
    graph = build_view_graph(overlap_matrix(preds[:5] + [wrong] + preds[6:] + [None]))
    assert graph["registered"][5] is False and 5 in graph["not_fitted"]
    assert graph["unplaced"] == [24]
    assert sum(graph["registered"]) == 23


def _scan(room, cams, frame):
    preds = {i: scene.prediction(room, c, frame) for i, c in enumerate(cams)}
    views = list(preds)
    pts = collect_points(preds, views)
    return preds, pts, fit_room(pts, camera_up(preds, views), forward=camera_forward(preds, 0))


def standard_cameras():
    ring = scene.ring_cameras(20, radius=0.7, height=1.5, centre=(-0.3, -0.6), pitch_target_y=1.1)
    up = [scene.look_at((x, 1.5, z), (x + 0.5, 2.1, z + 0.4)) for x, z in [(-0.8, -1.0), (0.2, -0.4), (-0.4, 0.2)]]
    return ring + up


def test_room_frame_is_found_in_any_world_frame():
    room = scene.garage()
    cams = standard_cameras()
    preds, pts, frame = _scan(room, cams, scene.random_frame(np.random.default_rng(3)))
    # Which side runs along x depends on where the first photo looks; the sizes do not.
    dims = sorted([frame.x_max - frame.x_min, frame.z_max - frame.z_min])
    assert dims == pytest.approx([4.0, 5.0], abs=0.1)
    assert frame.ceiling_y == pytest.approx(2.5, abs=0.1)
    T = frame.world_to_room
    heights = [transform_points(T, preds[i]["c2w"][:3, 3])[1] for i in preds]
    assert np.allclose(heights, 1.5, atol=0.05)
    # The first photo looks up the map (toward -z), so wall letters are stable across runs.
    look = T[:3, :3] @ camera_forward(preds, 0)
    assert look[2] < -0.5 * np.hypot(look[0], look[2])


def test_grids_count_views_and_mark_what_furniture_hides():
    room = scene.garage()
    # The standard ring plus three photos looking down at the table from different sides.
    cams = standard_cameras() + [scene.look_at(eye, (0.6, 0.0, 1.0)) for eye in
                                 [(0.6, 1.6, -0.2), (-0.4, 1.6, 0.6), (0.2, 1.7, 0.1)]]
    preds, pts, frame = _scan(room, cams, None)
    T = frame.world_to_room
    xyz = transform_points(T, pts.xyz)
    bounds = (frame.x_min, frame.x_max, frame.z_min, frame.z_max)
    seen_by = Views.from_preds(preds, list(preds), T)
    walls = {k: wall_grid(k, seen_by, bounds, frame.ceiling_y) for k in WALLS}
    # Every wall faces some cameras of the ring, so each has well-covered cells.
    for k, g in walls.items():
        assert (g.views >= GOOD_VIEWS).any(), k
    # The shelving unit hides the wall behind it: blocked, not missing, and never asked for.
    shelf_wall = max(walls, key=lambda k: walls[k].blocked.sum())
    assert walls[shelf_wall].blocked.sum() >= 6
    floor = floor_grid(seen_by, xyz, bounds)
    assert floor.blocked.sum() >= 6  # the table and the shelving cover floor
    st = floor.stats()
    assert st["good"] + st["thin"] + st["unseen"] + st["blocked"] + st["opening"] == st["cells"]
    # The cells under the table and the shelving are not counted as seen.
    table = room.boxes[1]
    centre = transform_points(T, (table.lo + table.hi) / 2)
    r = int((centre[2] - bounds[2]) / 0.25)
    c = int((centre[0] - bounds[0]) / 0.25)
    assert floor.views[r, c] == 0 and floor.blocked[r, c]


def test_a_doorway_shows_as_an_opening_not_a_gap():
    # A box room with a doorway cut in the far wall: photos looking at it see past the wall.
    room = scene.Room(size=(4.0, 2.5, 5.0))
    cams = [scene.look_at((x, 1.2, 1.0), (x, 1.2, -2.5)) for x in (-0.6, 0.0, 0.6)]
    preds = {i: scene.prediction(room, c) for i, c in enumerate(cams)}
    for p in preds.values():  # pretend the wall is 1 m further back where the doorway is
        cam = p["pts_cam"].astype(np.float64)
        world = cam @ p["c2w"][:3, :3].T + p["c2w"][:3, 3]
        door = (np.abs(world[..., 0]) < 0.45) & (world[..., 1] < 2.0) & (world[..., 2] < -2.45)
        cam[door] *= (cam[door][:, 2:3] + 1.0) / cam[door][:, 2:3]
        p["pts_cam"] = cam.astype(np.float16)
    bounds = (-2.0, 2.0, -2.5, 2.5)
    wall = wall_grid("A", Views.from_preds(preds, list(preds), np.eye(4)), bounds, 2.5)
    axes = wall_axes("A", bounds)
    c = int((0.0 - axes["left_end"][0]) / 0.25)
    assert wall.opening[3, c] and not wall.blocked[3, c]
    assert wall.views[3, c - 4] >= GOOD_VIEWS


def test_runs_and_regions():
    assert runs(np.array([0, 1, 1, 0, 1], bool)) == [(1, 3), (4, 5)]
    mask = np.zeros((4, 5), bool)
    mask[0, 0:2] = True
    mask[2:4, 3:5] = True
    found = regions(mask)
    assert [len(r) for r in found] == [4, 2]
