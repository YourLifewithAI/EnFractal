"""Objects, guidance wording and the GPU-free parts of the backend, without a model."""

from __future__ import annotations

import numpy as np
import pytest
from PIL import Image

from roomscan.coverage import backend as be
from roomscan.coverage import guidance as gd
from roomscan.coverage.detect import extend_to_wall
from roomscan.coverage.grid import wall_axes
from roomscan.coverage.objects import Detection, ObjectInstance, build_instances, cluster, measure_views, sector_of

BOUNDS = (-2.0, 2.0, -2.5, 2.5)


def _obj(label, lo, hi, wall=None, oid=None):
    o = ObjectInstance(oid or label.replace(" ", "_"), label, [], np.array(lo, float), np.array(hi, float))
    o.wall = wall
    return o


def test_sectors_follow_the_viewer_facing_the_object():
    assert sector_of(0) == "front" and sector_of(50) == "front-left" and sector_of(-90) == "right side"
    assert sector_of(180) == "back" and sector_of(-170) == "back" and sector_of(400) == "front-left"
    # An object against wall D (x min) faces +x. A camera to its +z side is on the viewer's left
    # when they face the object (looking toward -x, their left is +z).
    o = _obj("shelving unit", (-2.0, 0.0, -0.5), (-1.6, 1.8, 0.5))
    pts = {0: np.array([[-1.7, 1.0, 0.0]] * 80), 1: np.array([[-1.7, 1.0, 0.0]] * 80)}
    cams = {0: np.array([-0.5, 1.5, 0.0]), 1: np.array([-1.0, 1.5, 1.4])}
    measure_views(o, pts, cams, np.array([0.0, 1.0, 0.0]), wall_axes("D", BOUNDS), min_points=30)
    assert [sector_of(a) for a in o.view_angles_deg] == ["front", "front-left"]


def test_only_well_evidenced_objects_are_kept_once():
    def det(view, label, score, c, half=0.4):
        d = Detection(view, label, score, (0, 0, 1, 1))
        d.centre = np.array(c, float)
        d.lo, d.hi = d.centre - half, d.centre + half
        return d

    dets = ([det(v, "table", 0.3, (0.05 * v, 0.4, 0)) for v in range(5)]  # many weak sightings: kept
            + [det(v, "desk", 0.25, (0.05 * v, 0.45, 0.02)) for v in range(3)]  # same place, other name: merged
            + [det(7, "sofa", 0.6, (2.0, 0.4, 2.0)), det(8, "sofa", 0.55, (2.05, 0.4, 2.0))]  # two confident photos
            + [det(9, "chair", 0.25, (-1, 0.5, -1))]  # one weak sighting: dropped
            + [det(10, "bin", 0.9, (-1.5, 0.3, 1.5))])  # one photo, however confident: dropped
    objs = build_instances(cluster(dets))
    assert sorted(o.label for o in objs) == ["sofa", "table"]
    assert [o.id for o in objs if o.label == "table"] == ["table"]  # a single table is not numbered


def test_tall_furniture_near_a_wall_is_extended_to_it():
    shelf = _obj("shelving unit", (-1.55, 0.0, -1.0), (-1.5, 1.8, 0.4), wall="D")
    extend_to_wall(shelf, BOUNDS)
    assert shelf.lo[0] == -2.0
    table = _obj("table", (-1.4, 0.0, -1.0), (-0.6, 0.75, 0.4), wall="D")
    extend_to_wall(table, BOUNDS)
    assert table.lo[0] == -1.4  # low and 0.6 m from the wall: left where it is


def test_words_and_distances_read_naturally():
    assert gd.words(3) == "three" and gd.words(21) == "21"
    assert gd.metres(0.9) == "one metre" and gd.metres(1.4) == "one and a half metres" and gd.metres(0.2) == "half a metre"
    assert gd._join(["a", "b", "c"]) == "a, b and c"


def test_span_phrases_name_furniture_on_the_wall():
    axes = wall_axes("D", BOUNDS)
    shelf = _obj("shelving unit", (-2.0, 0, -0.5), (-1.6, 1.8, 0.5), wall="D")
    door = _obj("door", (-2.0, 0, 1.5), (-1.9, 2.0, 2.3), wall="D")
    prof = {"objects": [shelf, door]}
    lo_s, hi_s = gd.object_u_range(shelf, axes)
    lo_d, hi_d = gd.object_u_range(door, axes)
    assert hi_s <= lo_d or hi_d <= lo_s
    left, right = sorted([(lo_s, hi_s, "shelving unit"), (lo_d, hi_d, "door")])
    between = gd.span_phrase(left[1] + 0.1, right[0] - 0.1, prof, axes)
    assert between.startswith(f"between the {left[2]} and the {right[2]}")
    assert gd.span_phrase(0.0, axes["length"], {"objects": []}, axes) == "along its whole length"
    assert "around the shelving unit" in gd.span_phrase(lo_s, hi_s, prof, axes)


def test_low_lap_is_asked_for_only_when_few_photos_are_low():
    looks = {i: np.array([0, 0, -1.0]) for i in range(10)}
    high = {i: np.array([0, 1.5, 0]) for i in range(10)}
    ctx = {"cams": high, "registered": list(range(10)), "looks": looks}
    item = gd.height_item(ctx)
    assert item and "knee height" in item[0]["text"] and "10 cm" in item[0]["why"]
    mixed = {i: np.array([0, 0.4 if i < 3 else 1.5, 0]) for i in range(10)}
    assert gd.height_item({"cams": mixed, "registered": list(range(10)), "looks": looks}) == []


def test_notes_flag_lenses_blur_and_copies():
    m = {"flags": {"blurry": ["a.jpg"]}, "summary": {"files": 4},
         "duplicates": {"exact": [["b.jpg", "b (1).jpg"]]},
         "photos": [{"name": "c.jpg", "status": "ok", "lens_kind": "front"},
                    {"name": "d.jpg", "status": "ok", "lens_kind": "ultra_wide"}]}
    kinds = [n["kind"] for n in gd.notes({"manifest": m})]
    assert kinds == ["retake", "lens", "lens", "duplicates"]


def test_model_input_geometry_and_exif_intrinsics():
    scale, left, top = be.processed_geometry(4284, 5712)
    assert (left, top) == (0, 0) or left >= 0
    K = be.exif_intrinsics({"exif": {"focal_length_35mm": 26}, "image": {"width": 4284, "height": 5712}})
    w, h = be.TARGET_SIZE
    assert K[0, 2] == pytest.approx(w / 2, abs=1.0) and K[1, 2] == pytest.approx(h / 2, abs=1.0)
    # 26 mm equivalent on a 3:4 portrait frame: about 53 degrees across the short side.
    fov = 2 * np.degrees(np.arctan(w / 2 / K[0, 0]))
    assert fov == pytest.approx(53.0, abs=1.5)
    assert be.exif_intrinsics({"exif": {}, "image": {"width": 10, "height": 10}}) is None
    assert be.batch_size_for(8192) == 32 and be.batch_size_for(5000) == 8


def test_draft_decoding_keeps_enough_pixels(tmp_path):
    big = Image.fromarray(np.random.default_rng(0).integers(0, 255, (3200, 2400, 3), dtype=np.uint8))
    path = tmp_path / "big.jpg"
    big.save(path, quality=80)
    with be.open_reduced(path, be.TARGET_SIZE) as im:
        assert im.size[0] >= 2 * be.TARGET_SIZE[0] - 1 and im.size[0] < 2400
    assert be.load_model_input(path).size == be.TARGET_SIZE


def test_game_window_check_needs_the_game_program():
    playing = [("EnFractal (DEBUG)", "Godot_v4.7.2-stable_mono_win64.exe"), ("Inbox", "outlook.exe")]
    assert be.game_window_open(playing) is True
    assert be.game_window_open([("EnFractal", "EnFractal.exe")]) is True
    # A folder or an editor named after the project is not the game.
    assert be.game_window_open([("EnFractal", "explorer.exe"), ("EnFractal - VS Code", "Code.exe")]) is False
    if be.sys.platform.startswith("win"):
        import time

        t0 = time.perf_counter()
        windows = be.visible_windows()  # the real call: fast, and returns (title, program) pairs
        assert time.perf_counter() - t0 < 2.0
        assert all(isinstance(t, str) and isinstance(e, str) for t, e in windows)


def test_torch_hub_is_pinned_to_a_commit():
    torch = pytest.importorskip("torch")
    calls = []
    real = torch.hub.load
    torch.hub.load = lambda repo, model, *a, **k: calls.append((repo, model, k)) or "model"
    try:
        with be.pinned_torch_hub():
            torch.hub.load("facebookresearch/dinov2", "dinov2_vitg14", pretrained=False)
            torch.hub.load("someone/else", "x")
    finally:
        torch.hub.load = real
    ref = be.TORCH_HUB_PINS["facebookresearch/dinov2"]["ref"]
    assert calls[0][0] == f"facebookresearch/dinov2:{ref}" and calls[0][2]["skip_validation"] is True
    assert calls[0][2]["force_reload"] is False
    assert calls[1][0] == "someone/else"


def test_every_downloaded_model_has_a_recorded_licence():
    for model, info in be.MODEL_REGISTRY.items():
        assert info["licence"] and info["revision"] and "NC" not in info["licence"], model
    assert all("NC" in why or "gated" in why for why in be.REJECTED_MODELS.values())
