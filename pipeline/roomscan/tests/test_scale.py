"""Which scale the room is in, and what the founder's tape does to it.

Seeded from the Lane C review's ``test_scale_claim.py``: the recorded "factor" (the median batch's
scale) was never applied, so the room silently kept the seed batch's scale. These tests pin the
scale the room really has, in both cases: without measurements it is the seed batch's, with them it
is the one fitted to the tape.
"""

from __future__ import annotations

import json
import math
from dataclasses import replace
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import pytest

import scene
from roomscan.coverage import scale as sc
from roomscan.coverage.grid import SurfaceGrid
from roomscan.coverage.run import run_coverage

# --- the end to end runs --------------------------------------------------------------------------


class SeedLargeBackend(scene.FakeBackend):
    """The seed batch comes back 10% large and every other batch exactly metric, so a median-batch
    "correction" would give 4.0 x 5.0 m while keeping the seed batch's scale gives 4.4 x 5.5 m."""

    def batch_scale(self) -> float:
        return 1.10 if self.calls == 0 else 1.0


def coverage(session, **kwargs):
    return run_coverage(session.captures, "garage", chunk_size=10, anchors=3, skip_detection=True,
                        backend_factory=lambda: SeedLargeBackend(session.room, session.cams, names=session.names, seed=3),
                        log=lambda *_: None, **kwargs)


def read(result, name="coverage.json"):
    return json.loads((Path(result["report"]).parent / name).read_bytes())


@pytest.fixture()
def seed_run(garage_session):
    result = coverage(garage_session)
    return SimpleNamespace(session=garage_session, result=result, record=read(result),
                           markdown=Path(result["markdown"]).read_text(encoding="utf-8"),
                           html=Path(result["report"]).read_text(encoding="utf-8"))


def test_without_measurements_the_room_keeps_the_seed_batch_scale(seed_run):
    frame = seed_run.record["room_frame"]
    dims = sorted([frame["width_x_m"], frame["depth_z_m"]])
    assert dims == pytest.approx([4.4, 5.5], abs=0.15), "the room must be in the seed batch's scale"
    scale = seed_run.result["run"]["scale"]
    assert scale["scale_source"] == "seed_batch" and scale["applied_factor"] == 1.0 and scale["tape"] is None
    assert scale["room_m"] == scale["room_before_m"]
    # The old, never-applied "factor" is gone from every record; the batches' scales remain as an error bar.
    poses_scale = seed_run.result["run"]["poses"]["scale"]
    assert "factor" not in poses_scale and '"factor"' not in json.dumps(scale)
    assert poses_scale["batches"] == 4 and poses_scale["median_batch_scale"] == pytest.approx(1.10, abs=0.03)
    # Seed batch is 10% large; the other batches say the room is 1 / 1.10 of that: 9% smaller, never larger.
    assert poses_scale["vs_used_pct"]["low"] == pytest.approx(-9.1, abs=1.5)
    assert poses_scale["vs_used_pct"]["high"] == pytest.approx(0.0, abs=0.5)
    assert seed_run.record["scale"]["scale_source"] == "seed_batch"


def test_the_error_bar_is_relative_to_the_scale_in_use(seed_run):
    for text in (seed_run.markdown, seed_run.html):
        assert "9% smaller to no larger than drawn" in text
        # The sentence that called the room's size "in the right range" because people hold a phone at
        # 1.7 m hid a 7% error; it must not come back.
        assert "standing photos" not in text and "right range" not in text
    assert "measurements.json" in seed_run.markdown


def test_tape_measurements_set_the_scale_of_the_room(seed_run):
    session = seed_run.session
    size = scene.garage().size  # the true room: 4 m (x) by 2.5 m high by 5 m (z)
    x_extent = seed_run.record["room_frame"]["width_x_m"]  # along B and D; which axis that is depends on the photos
    true_bd, true_ac = (4.0, 5.0) if x_extent < 5.0 else (5.0, 4.0)
    assert sorted([true_bd, true_ac]) == sorted([size[0], size[2]])
    hostile = 'the end <script>alert(1)</script> & "done"'
    measurements = {"room": "garage", "measurements": [
        {"id": "bd", "kind": "wall_to_wall", "between": ["B", "D"], "session_id": session.session_id,
         "value_m": true_bd, "sigma_m": 0.01, "where": hostile},
        {"id": "ac", "kind": "wall_to_wall", "between": ["A", "C"], "session_id": session.session_id,
         "value_m": true_ac, "sigma_m": 0.01},
        {"id": "up", "kind": "floor_to_ceiling", "between": [], "session_id": session.session_id, "value_m": size[1]},
        {"id": "door", "kind": "opening", "between": ["A"], "session_id": session.session_id, "value_m": 0.8}]}
    (session.captures / "garage" / "measurements.json").write_text(json.dumps(measurements), encoding="utf-8")

    result = coverage(session)
    record = read(result)
    frame = record["room_frame"]
    assert [frame["width_x_m"], frame["depth_z_m"]] == pytest.approx([true_bd, true_ac], abs=0.06)
    assert frame["ceiling_y_m"] == pytest.approx(size[1], abs=0.06)
    scale = result["run"]["scale"]
    assert scale["scale_source"] == "tape"
    assert scale["applied_factor"] == pytest.approx(1 / 1.10, abs=0.015)
    assert scale["room_before_m"]["x"] == pytest.approx(x_extent, abs=1e-3)  # the unscaled box is kept for comparison
    checks = scale["tape"]["checks"]
    rows = {r["id"]: r for r in checks["rows"]}
    assert checks["rms_cm"] < 6 and not any(r["flagged"] for r in checks["rows"])
    assert all(abs(rows[k]["residual_m"]) < 0.06 for k in ("bd", "ac", "up"))
    assert all(rows[k]["implied_scale"] == pytest.approx(1 / 1.10, abs=0.015) for k in ("bd", "ac", "up"))
    # The opening is only a check: this room has no doorway, so nothing was found and nothing was fitted.
    assert rows["door"]["check_only"] and rows["door"]["model_m"] is None and scale["tape"]["fitted"] == ["bd", "ac", "up"]
    assert record["scale"] == scale

    # What the founder reads: the sizes are scaled to the tape, and the free text from the file is data.
    md = Path(result["markdown"]).read_text(encoding="utf-8")
    page = Path(result["report"]).read_text(encoding="utf-8")
    assert "scaled to your tape measurements" in md and "Scale: your tape measurements set it" in md
    assert "<script>" not in page and "&lt;script&gt;" in page
    assert "<script>" not in md and "\\<script\\>" in md


# --- the fit, on the garage's own numbers -------------------------------------------------------------

def box(x=6.074, z=5.053, y=2.638):
    return SimpleNamespace(x_min=-x / 2, x_max=x / 2, z_min=-z / 2, z_max=z / 2, ceiling_y=y)


def founders_tape(session="s-1"):
    def m(ident, kind, between, value, sigma=0.01):
        return sc.Measurement(ident, kind, tuple(between), session, value, sigma, "")
    return [m("length", "wall_to_wall", "BD", 5.842), m("width", "wall_to_wall", "AC", 4.546),
            m("height", "floor_to_ceiling", "", 2.464), m("door", "opening", "B", 0.787)]


def test_one_uniform_scale_is_fitted_by_weighted_least_squares_on_log_ratios():
    frame, tape = box(), founders_tape()
    plan = sc.plan_scale(tape, "s-1", frame)
    assert plan.source == "tape" and plan.fit["used"] == ["length", "width", "height"]  # the opening is a check
    # By hand: sigma is hypot(1 cm, 10 cm) for each, relative to the tape value, weights are 1/sigma^2.
    values, model = np.array([5.842, 4.546, 2.464]), np.array([6.074, 5.053, 2.638])
    w = (values / math.hypot(0.01, 0.10)) ** 2
    expected = math.exp(float((w * np.log(values / model)).sum() / w.sum()))
    assert plan.factor == pytest.approx(expected, rel=1e-9)
    assert plan.factor == pytest.approx(0.9375, abs=0.001)  # the reviewer's equal-weight variants give 0.9315 to 0.9361
    assert plan.fit["sigma_pct"] == pytest.approx(100 / math.sqrt(w.sum()), rel=1e-9)
    # Never per axis: the same factor moves the length, the width and the height.
    scaled = box(*(v * plan.factor for v in (6.074, 5.053, 2.638)))
    spread = {"typical_m": None, "per_wall_m": {}}
    checks = sc.check_measurements(plan, frame, scaled, walls={"B": _gap_grid([2, 3, 4])}, spread=spread)
    rows = {r["id"]: r for r in checks["rows"]}
    assert rows["length"]["residual_m"] == pytest.approx(-0.148, abs=0.003)  # model too short
    assert rows["width"]["residual_m"] == pytest.approx(+0.191, abs=0.003)  # model too long
    assert rows["height"]["residual_m"] == pytest.approx(+0.009, abs=0.003)
    assert rows["width"]["residual_pct"] == pytest.approx(100 * 0.191 / 4.546, abs=0.1)
    # The per-axis scale each would ask for alone is a diagnostic: it differs by more than 6%.
    implied = [rows[k]["implied_scale"] for k in ("length", "width", "height")]
    assert implied == pytest.approx([5.842 / 6.074, 4.546 / 5.053, 2.464 / 2.638], rel=1e-9)
    assert checks["rms_cm"] == pytest.approx(100 * math.sqrt((0.148 ** 2 + 0.191 ** 2 + 0.009 ** 2) / 3), abs=0.2)


def _gap_grid(columns, rows=10, cols=24):
    """A wall grid whose cells at these columns, between 0.3 m and 1.8 m up, are 'photos see past it'."""
    opening = np.zeros((rows, cols), bool)
    opening[1:8, columns] = True
    views = np.zeros((rows, cols), int)
    return SurfaceGrid("wall B", views, np.zeros_like(opening), (0.0, cols * 0.25, 0.0, rows * 0.25), opening=opening)


def test_a_residual_is_flagged_above_twice_the_wall_copy_spread():
    frame, plan = box(), sc.plan_scale(founders_tape(), "s-1", box())
    scaled = box(*(v * plan.factor for v in (6.074, 5.053, 2.638)))
    walls = {"B": _gap_grid([2, 3, 4])}
    tight = sc.check_measurements(plan, frame, scaled, walls, {"typical_m": 0.04, "per_wall_m": {"A": 0.04}})
    flagged = {r["id"] for r in tight["rows"] if r["flagged"]}
    assert flagged == {"length", "width"} and tight["flag_limit_cm"] == 8.0 and tight["wall_copy_spread_cm"] == 4.0
    loose = sc.check_measurements(plan, frame, scaled, walls, {"typical_m": 0.12, "per_wall_m": {}})
    assert not any(r["flagged"] for r in loose["rows"]) and loose["flag_limit_cm"] == 24.0
    # Without a spread to judge by, the wall-plane fit's own 10 cm is used, and 5 cm is the floor.
    unknown = sc.check_measurements(plan, frame, scaled, walls, {"typical_m": None, "per_wall_m": {}})
    assert unknown["flag_limit_cm"] == 20.0
    tiny = sc.check_measurements(plan, frame, scaled, walls, {"typical_m": 0.001, "per_wall_m": {}})
    assert tiny["flag_limit_cm"] == 5.0


def test_an_opening_is_checked_against_the_gap_the_photos_saw_and_never_fitted():
    frame = box()
    with_door = sc.plan_scale(founders_tape(), "s-1", frame)
    without = sc.plan_scale([m for m in founders_tape() if m.kind != "opening"], "s-1", frame)
    assert with_door.factor == without.factor  # the door does not move the fit
    spread = {"typical_m": None, "per_wall_m": {}}
    match = sc.check_measurements(with_door, frame, frame, {"B": _gap_grid([5, 6, 7])}, spread)  # 3 cells = 0.75 m
    door = next(r for r in match["rows"] if r["id"] == "door")
    assert door["check_only"] and door["model_m"] == pytest.approx(0.75) and not door["flagged"]
    assert door["residual_m"] == pytest.approx(0.75 - 0.787)
    wide = sc.check_measurements(with_door, frame, frame, {"B": _gap_grid(list(range(3, 13)))}, spread)
    assert next(r for r in wide["rows"] if r["id"] == "door")["flagged"]  # 2.5 m is not 0.787 m
    none = sc.check_measurements(with_door, frame, frame, {"B": _gap_grid([])}, spread)
    assert next(r for r in none["rows"] if r["id"] == "door")["model_m"] is None


def test_measurements_for_another_session_or_with_an_implausible_scale_are_not_applied():
    other = sc.plan_scale(founders_tape("s-old"), "s-new", box())
    assert other.source == "seed_batch" and other.factor == 1.0 and "s-old" in other.notes[0] and "s-new" in other.notes[0]
    mixed = founders_tape("s-new")[:1] + founders_tape("s-old")[1:]
    plan = sc.plan_scale(mixed, "s-new", box())  # only the one for this session counts
    assert plan.source == "tape" and plan.fit["used"] == ["length"] and plan.notes
    wrong_units = sc.plan_scale(founders_tape(), "s-1", box(x=60.0, z=50.0, y=26.0))  # a model in decimetres
    assert wrong_units.source == "seed_batch" and wrong_units.factor == 1.0 and "not plausible" in wrong_units.notes[0]
    only_door = sc.plan_scale(founders_tape()[3:], "s-1", box())
    assert only_door.source == "seed_batch" and "None of the measurements could be fitted" in only_door.notes[0]
    no_ceiling = sc.plan_scale(founders_tape()[2:3], "s-1", box(y=None))
    assert no_ceiling.source == "seed_batch"
    assert sc.plan_scale(None, "s-1", box()).source == "seed_batch" and sc.plan_scale([], "s-1", box()).notes == []


def test_a_diagonal_measures_the_floor_corner_to_corner():
    frame = box(x=4.0, z=3.0)
    diag = sc.Measurement("d", "diagonal", ("AB", "CD"), "s-1", 5.0, 0.01, "")
    assert sc.model_length(diag, frame) == pytest.approx(5.0)
    assert sc.plan_scale([diag], "s-1", frame).factor == pytest.approx(1.0)


@pytest.mark.parametrize("entry, message", [
    ({"kind": "wall_to_wall", "between": ["A", "B"]}, "opposite walls"),
    ({"kind": "wall_to_wall", "between": ["A"]}, "opposite walls"),
    ({"kind": "floor_to_ceiling", "between": ["A"]}, "takes no walls"),
    ({"kind": "diagonal", "between": ["AB", "AD"]}, "opposite corners"),
    ({"kind": "opening", "between": ["B", "D"]}, "one wall"),
    ({"kind": "sideways", "between": []}, "'kind' must be one of"),
    ({"kind": "floor_to_ceiling", "between": [], "value_m": -1}, "above zero"),
    ({"kind": "floor_to_ceiling", "between": [], "value_m": "2.4"}, "above zero"),
    ({"kind": "floor_to_ceiling", "between": [], "sigma_m": 0}, "above zero"),
    ({"kind": "floor_to_ceiling", "between": [], "session_id": ""}, "session_id"),
])
def test_a_bad_measurement_says_what_to_fix(tmp_path, entry, message):
    base = {"id": "x", "kind": "floor_to_ceiling", "between": [], "session_id": "s-1", "value_m": 2.4}
    path = tmp_path / "measurements.json"
    path.write_text(json.dumps({"measurements": [{**base, **entry}]}), encoding="utf-8")
    with pytest.raises(sc.MeasurementError, match=message):
        sc.load_measurements(path)


def test_a_measurements_file_is_checked_as_a_whole(tmp_path):
    path = tmp_path / "measurements.json"
    good = {"id": "h", "kind": "floor_to_ceiling", "between": [], "session_id": "s-1", "value_m": 2.464}
    path.write_text(json.dumps({"room": "garage", "measurements": [good]}), encoding="utf-8")
    (loaded,) = sc.load_measurements(path, "garage")
    assert loaded.sigma_m == sc.DEFAULT_TAPE_SIGMA_M and loaded.where == "" and loaded.between == ()
    with pytest.raises(sc.MeasurementError, match="not 'kitchen'"):
        sc.load_measurements(path, "kitchen")
    path.write_text(json.dumps({"measurements": [good, good]}), encoding="utf-8")
    with pytest.raises(sc.MeasurementError, match="unique"):
        sc.load_measurements(path)
    path.write_text("{not json", encoding="utf-8")
    with pytest.raises(sc.MeasurementError, match="not valid JSON"):
        sc.load_measurements(path)
    path.write_text(json.dumps([good]), encoding="utf-8")
    with pytest.raises(sc.MeasurementError, match="'measurements' list"):
        sc.load_measurements(path)


# --- the model's own estimate ---------------------------------------------------------------------

def test_batch_scales_become_an_error_bar_relative_to_the_seed_batch():
    out = sc.batch_scale_summary({0: 1.0, 1: 0.9, 2: 0.8})
    assert out["median_batch_scale"] == pytest.approx(0.9) and out["batches"] == 3
    assert out["vs_used_pct"] == {"low": 0.0, "high": 25.0}
    # The garage's seven batches: one is a little over the seed's scale, the smallest is 21% under it.
    garage = sc.batch_scale_summary({0: 1.0, 1: 0.9716, 2: 0.8614, 3: 0.8474, 4: 0.7921, 5: 0.8783, 6: 1.0166})
    assert garage["vs_used_pct"] == {"low": -1.6, "high": 26.2}
    assert sc.batch_scale_summary({"0": 1.0})["vs_used_pct"] == {"low": 0.0, "high": 0.0}


def test_wall_copy_spread_is_how_far_apart_the_batches_put_a_wall():
    rng = np.random.default_rng(0)
    n = 400
    pts, normals, batch = [], [], []
    for b, offset in ((0, 0.0), (1, 0.08), (2, 0.02)):
        # Wall A: z = z_min + offset, facing +z, from batch b; heights 0.5 to 2.0 m.
        pts.append(np.column_stack([rng.uniform(-1, 1, n), rng.uniform(0.5, 2.0, n), np.full(n, -2.5 + offset)]))
        normals.append(np.tile([0.0, 0.0, 1.0], (n, 1)))
        batch.append(np.full(n, b))
    xyz, nor, bat = np.concatenate(pts), np.concatenate(normals), np.concatenate(batch)
    out = sc.wall_copy_spread(xyz, nor, bat, (-2.0, 2.0, -2.5, 2.5))
    assert out["per_wall_m"]["A"] == pytest.approx(float(np.std([0.0, 0.08, 0.02])), abs=0.002)
    assert out["per_wall_m"]["B"] is None and out["per_wall_m"]["C"] is None  # nobody saw those walls face on
    assert out["typical_m"] == out["per_wall_m"]["A"]
    few = sc.wall_copy_spread(xyz[:100], nor[:100], bat[:100], (-2.0, 2.0, -2.5, 2.5))  # one batch: no spread to speak of
    assert few["typical_m"] is None


def test_scaling_a_prediction_moves_its_camera_and_its_points_together():
    room = scene.garage()
    pred = scene.prediction(room, scene.look_at((0.0, 1.5, 0.5), (0.0, 1.2, -2.5)))
    scaled = sc.scale_prediction(pred, 0.9)
    assert np.allclose(scaled["c2w"][:3, 3], 0.9 * pred["c2w"][:3, 3])
    assert np.allclose(scaled["c2w"][:3, :3], pred["c2w"][:3, :3])
    assert np.allclose(scaled["pts_cam"].astype(float), 0.9 * pred["pts_cam"].astype(float), atol=5e-3)
    assert np.array_equal(scaled["K"], pred["K"]) and scaled["pts_cam"].dtype == np.float16


def test_the_box_is_scaled_with_the_poses_not_fitted_again():
    from roomscan.coverage.geometry import transform_points
    from roomscan.coverage.layout import camera_forward, camera_up, collect_points, fit_room

    room = scene.garage()
    cams = scene.ring_cameras(20, radius=0.7, height=1.5, centre=(-0.3, -0.6), pitch_target_y=1.1)
    cams += [scene.look_at((x, 1.5, z), (x + 0.5, 2.1, z + 0.4)) for x, z in [(-0.8, -1.0), (0.2, -0.4), (-0.4, 0.2)]]
    preds = {i: scene.prediction(room, c, scene.random_frame(np.random.default_rng(3))) for i, c in enumerate(cams)}
    pts = collect_points(preds, list(preds))
    frame = fit_room(pts, camera_up(preds, list(preds)), forward=camera_forward(preds, 0))
    f = 0.9
    scaled = sc.scale_frame(frame, f)
    assert (scaled.x_max - scaled.x_min) == pytest.approx(f * (frame.x_max - frame.x_min))
    assert (scaled.z_max - scaled.z_min) == pytest.approx(f * (frame.z_max - frame.z_min))
    assert scaled.ceiling_y == pytest.approx(f * frame.ceiling_y)
    assert np.array_equal(scaled.world_to_room[:3, :3], frame.world_to_room[:3, :3])
    # A world point scaled by f lands where the old room position scaled by f was: the box and the poses agree.
    X = pts.xyz[:500]
    assert np.allclose(transform_points(scaled.world_to_room, f * X), f * transform_points(frame.world_to_room, X))
    assert "tape" in scaled.sources["scale"] and sc.scale_frame(frame, 1.0).ceiling_y == frame.ceiling_y
    assert sc.scale_frame(replace(frame, ceiling_y=None), f).ceiling_y is None  # a room whose ceiling was not found
