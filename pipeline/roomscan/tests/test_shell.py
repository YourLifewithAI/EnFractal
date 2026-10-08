"""C3, the shell, on synthetic rooms: planes checked against points, the manifest, its openings, spawns and the contract.

Everything is made from a ray-cast box room (tests/scene.py, tests/inv_scene.py). No real photo or place is involved.
"""

from __future__ import annotations

import json
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import pytest

import inv_scene
import scene as sc
from roomscan.coverage.run import run_coverage
from roomscan.paths import find_repo_root
from roomscan.scene import SceneMismatch, load_scene
from roomscan.shell import manifest as mf
from roomscan.shell.export import check_with_contract, export_shell, yaw_toward
from roomscan.shell.planes import ShellPlan, Surface, WALL_KEYS, plan_shell
from roomscan.shell.spec import SpecError, parse_spec

X0, X1, Z0, Z1, H = -2.0, 2.0, -2.5, 2.5, 2.5


def surfaces():
    out = [Surface("shell:floor", "floor", "floor", [("#8c8a86", 1.0)]), Surface("shell:ceiling", "ceiling", "ceiling", [("#e8e6e0", 1.0)])]
    out += [Surface(f"shell:wall_{k.lower()}", "wall", k, [("#b8b0a4", 0.7), ("#6a645c", 0.3)]) for k in WALL_KEYS]
    return out


@pytest.fixture()
def plan():
    return ShellPlan(X0, X1, Z0, Z1, H, surfaces(), 1.0, "seed_batch")


def spec_doc(**extra):
    doc = {"schema": "enfractal.shell_spec", "version": 1, "room": "garage", "session_id": "s-synthetic",
           "site": {"latitude_deg": 30, "neg_z_bearing_deg": 0, "solar_noon_h": 12},
           "openings": [
               {"id": "window_a", "kind": "window", "wall": "A", "u_m": 2.0, "width_m": 1.0, "v_bottom_m": 0.9, "height_m": 1.2,
                "state": "fixed", "traversable": False},
               {"id": "front_door", "kind": "door", "wall": "B", "u_m": 1.5, "width_m": 0.8, "v_bottom_m": 0.0, "height_m": 2.0,
                "state": "closed", "traversable": False},
               {"id": "big_door", "kind": "garage_door", "wall": "D", "u_m": 2.5, "width_m": 3.0, "v_bottom_m": 0.0, "height_m": 2.1,
                "state": "closed", "traversable": False}],
           "lamps": [{"id": "ceiling_light", "position_m": [0.0, 2.4, 0.0]}]}
    doc.update(extra)
    return doc


def fake_scene():
    photos = [{"status": "ok", "exif": {"make": "Apple", "model": "Synthetic phone"}} for _ in range(5)]
    return SimpleNamespace(session_id="s-synthetic", registered=[0, 1, 2], manifest={"photos": photos})


def newell(points):
    n = np.zeros(3)
    for i, p in enumerate(points):
        q = points[(i + 1) % len(points)]
        n += [(p[1] - q[1]) * (p[2] + q[2]), (p[2] - q[2]) * (p[0] + q[0]), (p[0] - q[0]) * (p[1] + q[1])]
    return n / np.linalg.norm(n)


# --- the spec --------------------------------------------------------------------------------------------------

def test_the_spec_is_read_strictly():
    spec = parse_spec(spec_doc())
    assert [o.id for o in spec.openings] == ["window_a", "front_door", "big_door"] and spec.site["latitude_deg"] == 30
    bad = [
        spec_doc(site={"latitude_deg": 30.5, "neg_z_bearing_deg": 0, "solar_noon_h": 12}),  # a finer latitude than the contract allows
        spec_doc(site={"latitude_deg": 30, "neg_z_bearing_deg": 0, "solar_noon_h": 12.1}),  # not a quarter hour
        spec_doc(site={"latitude_deg": 30, "neg_z_bearing_deg": 0, "solar_noon_h": 12, "longitude_deg": -97}),  # a longitude
        spec_doc(openings=[dict(spec_doc()["openings"][0], kind="skylight")]),
        spec_doc(openings=[dict(spec_doc()["openings"][0], wall="E")]),
        spec_doc(openings=[dict(spec_doc()["openings"][0], extra=1)]),
        spec_doc(openings=[spec_doc()["openings"][0]] * 2),  # the same id twice
        spec_doc(lamps=[{"id": "x", "position_m": [0, 2]}]),
        spec_doc(surface_colours={"A": "red"}),
        spec_doc(mystery=True),
        {"schema": "other", "version": 1, "room": "garage", "session_id": "s-x"},
    ]
    for doc in bad:
        with pytest.raises(SpecError):
            parse_spec(doc)


# --- the manifest ------------------------------------------------------------------------------------------------

def test_every_shell_polygon_faces_into_the_room_and_walls_run_past_each_other_at_the_corners(plan):
    parts = {p["id"]: p for p in mf.shell_parts(plan, parse_spec(spec_doc()))}
    assert set(parts) == {"shell:floor", "shell:ceiling", *(f"shell:wall_{k.lower()}" for k in WALL_KEYS)}
    assert np.allclose(newell(parts["shell:floor"]["geometry"]["points_m"]), [0, 1, 0])
    assert np.allclose(newell(parts["shell:ceiling"]["geometry"]["points_m"]), [0, -1, 0])
    for key in WALL_KEYS:
        points = parts[f"shell:wall_{key.lower()}"]["geometry"]["points_m"]
        assert np.allclose(newell(points), mf.wall_frame(plan, key)["n"]), key
        ys = [p[1] for p in points]
        assert min(ys) == 0.0 and max(ys) == H
    # A and C (the z walls) run 12 cm past the inside corner; B and D stop at it.
    a = parts["shell:wall_a"]["geometry"]["points_m"]
    assert min(p[0] for p in a) == pytest.approx(X0 - 0.12) and max(p[0] for p in a) == pytest.approx(X1 + 0.12)
    b = parts["shell:wall_b"]["geometry"]["points_m"]
    assert min(p[2] for p in b) == pytest.approx(Z0) and max(p[2] for p in b) == pytest.approx(Z1)
    assert parts["shell:wall_a"]["base_color"] == "#b8b0a4" and parts["shell:floor"]["material_role"] == "concrete"


def test_an_opening_sits_on_its_wall_plane_at_the_height_the_spec_says(plan):
    spec = parse_spec(spec_doc())
    openings = {o["id"]: o for o in mf.build_openings(plan, spec)}
    window = openings["window_a"]
    assert window["host_part_id"] == "shell:wall_a" and window["center_m"][2] == pytest.approx(Z0)
    assert window["center_m"][0] == pytest.approx(X0 + 2.0) and window["center_m"][1] == pytest.approx(0.9 + 0.6)
    assert window["size_m"] == [1.0, 1.2]
    door = openings["front_door"]  # wall B stands at x max; u runs along +z from z min
    assert door["center_m"][0] == pytest.approx(X1) and door["center_m"][2] == pytest.approx(Z0 + 1.5) and door["center_m"][1] == pytest.approx(1.0)
    big = openings["big_door"]  # wall D stands at x min; u runs along -z from z max
    assert big["center_m"][0] == pytest.approx(X0) and big["center_m"][2] == pytest.approx(Z1 - 2.5)


def test_openings_that_run_off_a_wall_or_overlap_are_refused(plan):
    window = spec_doc()["openings"][0]
    for opening, why in [(dict(window, u_m=0.2), "runs off"), (dict(window, v_bottom_m=1.8), "taller than the room"),
                         (dict(window, u_m=4.9, width_m=0.5, id="far"), "runs off")]:
        with pytest.raises(mf.ShellError, match=why):
            mf.build_openings(plan, parse_spec(spec_doc(openings=[opening])))
    with pytest.raises(mf.ShellError, match="overlap"):
        mf.build_openings(plan, parse_spec(spec_doc(openings=[window, dict(window, id="window_b", u_m=2.4)])))
    mf.build_openings(plan, parse_spec(spec_doc(openings=[window, dict(window, id="window_b", u_m=3.1)])))  # side by side: fine


def test_windows_light_the_room_and_a_sun_is_always_hinted(plan):
    hints = {h["id"]: h for h in mf.light_hints(plan, parse_spec(spec_doc()))}
    assert set(hints) == {"window_a_light", "ceiling_light", "sun"}  # a door gives no light
    outside = hints["window_a_light"]["position_m"]
    assert outside[2] < Z0  # outside the wall, aimed into the room (+z) and a little down
    assert hints["window_a_light"]["direction"][2] > 0 > hints["window_a_light"]["direction"][1]
    assert all(h["estimated"] for h in hints.values())


def build(plan, **extra):
    spec = parse_spec(spec_doc(**extra))
    spawns = [{"id": "player_start", "role": "player", "position_m": [0.5, 0.0, 0.5], "yaw_deg": 45.0},
              {"id": "companion_start", "role": "companion", "position_m": [1.0, 0.0, 0.5], "yaw_deg": 45.0}]
    return mf.build_manifest(plan, spec, fake_scene(), created_utc="2026-10-08T00:00:00Z", spawns=spawns)


def test_the_manifest_validates_against_the_contract_and_is_the_same_bytes_every_time(plan, tmp_path):
    room = build(plan)
    path, sha = mf.write_room(tmp_path / "rooms", room)
    assert path == tmp_path / "rooms" / "garage" / "room.json"
    assert check_with_contract(path.parent) == []
    again, sha2 = mf.write_room(tmp_path / "rooms2", build(plan))
    assert sha == sha2 and path.read_bytes() == again.read_bytes()
    raw = path.read_bytes()
    assert raw.endswith(b"\n") and b"\r" not in raw and not raw.startswith(b"\xef\xbb\xbf")
    document = json.loads(raw)
    assert document["site"] == {"latitude_deg": 30, "neg_z_bearing_deg": 0, "solar_noon_h": 12}
    assert document["objects"] == [] and document["files"] == [] and document["source"]["capture"]["privacy"]["shareable"] is False
    assert document["source"]["capture"]["devices"] == ["Apple Synthetic phone"]
    # The location rules: no key of the manifest holds a longitude, a place or GPS.
    text = raw.decode().lower()
    assert "longitude" not in text and "gps" not in text and "address" not in text


def test_a_shell_the_contract_would_refuse_is_caught(plan, tmp_path):
    room = build(plan)
    room["shell"]["openings"][0]["host_part_id"] = "shell:wall_z"
    path, _ = mf.write_room(tmp_path / "rooms", room)
    problems = check_with_contract(path.parent)
    assert problems and any("wall_z" in p for p in problems)
    room = build(plan)
    room["spawns"][0]["position_m"] = [9.0, 0.0, 0.0]  # outside the bounds
    path, _ = mf.write_room(tmp_path / "rooms3", room)
    assert any("outside the room bounds" in p for p in check_with_contract(path.parent))


def test_a_captured_room_is_never_written_into_the_repository(plan):
    repo = find_repo_root()
    assert repo is not None
    inside = repo / "game" / "rooms" / "never_written"
    with pytest.raises(mf.ShellError, match="inside the repository"):
        mf.write_room(inside, build(plan))
    assert not inside.exists()


def test_the_default_folder_is_the_games_user_data(monkeypatch, tmp_path):
    monkeypatch.setattr(mf.sys, "platform", "win32")
    monkeypatch.setenv("APPDATA", str(tmp_path / "Roaming"))
    assert mf.default_rooms_dir() == tmp_path / "Roaming" / "Godot" / "app_userdata" / "EnFractal" / "rooms"
    monkeypatch.setattr(mf.sys, "platform", "linux")
    monkeypatch.setenv("XDG_DATA_HOME", str(tmp_path / "share"))
    assert mf.default_rooms_dir() == tmp_path / "share" / "godot" / "app_userdata" / "EnFractal" / "rooms"


def test_spawns_are_on_clear_floor_away_from_walls_and_picked_objects():
    clear = np.zeros((20, 20), bool)
    clear[2:18, 2:18] = True
    clear[8:12, 8:12] = False  # something stands in the middle
    bounds = (0.0, 5.0, 0.0, 5.0)
    spots = mf.free_spawns(clear, (0.0, 0.0), 0.25, bounds)
    assert len(spots) >= 2
    for x, z, c in spots:
        assert 0.5 < x < 4.5 and 0.5 < z < 4.5 and c >= 0.3
        assert not (2.0 <= x <= 3.0 and 2.0 <= z <= 3.0)
    assert all(np.hypot(a[0] - b[0], a[1] - b[1]) >= 1.0 for i, a in enumerate(spots) for b in spots[i + 1:])
    first = mf.free_spawns(clear, (0.0, 0.0), 0.25, bounds, avoid=[(spots[0][0] - 0.5, spots[0][1] - 0.5, spots[0][0] + 0.5, spots[0][1] + 0.5)])
    assert (first[0][0], first[0][1]) != (spots[0][0], spots[0][1])
    assert mf.free_spawns(np.zeros((20, 20), bool), (0.0, 0.0), 0.25, bounds) == []
    assert yaw_toward(0, -1) == 0.0 and yaw_toward(-1, 0) == 90.0 and yaw_toward(1, 0) == -90.0


# --- planes from points, and the whole export, on the synthetic garage -----------------------------------------------------

def outward_cameras():
    cams = []
    for k in range(16):
        a = np.radians(k * 22.5)
        eye = np.array([-0.3, 1.4, -0.3])
        look = eye + np.array([np.cos(a), -0.15, np.sin(a)])
        cams.append(sc.look_at(eye, look))
    for k in range(8):
        a = np.radians(k * 45 + 20)
        eye = np.array([-0.3, 1.2, -0.3])
        cams.append(sc.look_at(eye, eye + np.array([np.cos(a), -1.0, np.sin(a)])))  # at the floor
        cams.append(sc.look_at(eye, eye + np.array([np.cos(a), 1.2, np.sin(a)])))  # at the ceiling
    return cams


def test_the_planes_are_checked_against_the_points_and_coloured_from_them(tmp_path):
    scene = inv_scene.build_scene(tmp_path, inv_scene.room_with_boxes(), outward_cameras())
    plan = plan_shell(scene)
    assert (plan.x_min, plan.x_max, plan.z_min, plan.z_max, plan.height) == (X0, X1, Z0, Z1, H)
    by = {s.key: s for s in plan.surfaces}
    assert set(by) == {"floor", "ceiling", *WALL_KEYS}
    for key, surface in by.items():
        ev = surface.evidence
        assert ev["points_within_25cm"] > 500, key
        assert abs(ev["median_offset_cm"]) < 1.0 and ev["spread_cm"] < 3.0, (key, ev)
        assert surface.colours and surface.base_color.startswith("#") and len(surface.base_color) == 7
        assert sum(s for _, s in surface.colours) == pytest.approx(1.0)


def test_export_end_to_end_makes_a_manifest_the_contract_accepts(garage_session, tmp_path):
    run_coverage(garage_session.captures, "garage", chunk_size=10, anchors=3, backend_factory=garage_session.backend(),
                 detector_factory=garage_session.detector(), log=lambda *_: None)
    captures = garage_session.captures
    scene = load_scene(captures, "garage")
    assert scene.scale_source == "seed_batch" and len(scene.registered) >= 20
    dims = sorted([scene.bounds[1] - scene.bounds[0], scene.bounds[3] - scene.bounds[2]])
    assert dims == pytest.approx([4.0, 5.0], abs=0.15)
    room_dir = captures / "garage"
    spec = spec_doc(session_id=garage_session.session_id, lamps=[])
    spec["openings"] = [dict(spec["openings"][0], u_m=1.5)]  # one window in wall A
    (room_dir / "shell-spec.json").write_text(json.dumps(spec), encoding="utf-8")
    result = export_shell(captures, "garage", rooms_dir=tmp_path / "rooms", created_utc="2026-10-08T00:00:00Z", log=lambda *_: None)
    assert result["problems"] == [], result["problems"]
    assert (room_dir / "shell" / "plan.json").is_file() and len(result["pictures"]) == 4
    document = json.loads(result["manifest"].read_bytes())
    lo, hi = document["bounds"]["min_m"], document["bounds"]["max_m"]
    spawns = {s["role"]: s for s in document["spawns"]}
    assert set(spawns) == {"player", "companion"}
    for s in spawns.values():
        x, y, z = s["position_m"]
        assert lo[0] < x < hi[0] and lo[2] < z < hi[2] and y == 0.0
        # Not inside the synthetic furniture (a table and a shelving unit), which the photos show as blocked floor.
        assert not (0.2 <= x <= 1.0 and 0.6 <= z <= 1.4) and not (-2.0 <= x <= -1.55 and -1.0 <= z <= 0.4)
    # The wall letters follow the first photo, so a spec made for another session is refused.
    (room_dir / "shell-spec.json").write_text(json.dumps(dict(spec, session_id="s-someone-else")), encoding="utf-8")
    with pytest.raises(SpecError, match="belongs to its session"):
        export_shell(captures, "garage", rooms_dir=tmp_path / "rooms", log=lambda *_: None)
    # And a pose cache that no longer gives the recorded room is refused rather than exported.
    cov = room_dir / "sessions" / garage_session.session_id / "coverage" / "coverage.json"
    record = json.loads(cov.read_bytes())
    record["room_frame"]["x_max_m"] += 0.5
    cov.write_text(json.dumps(record), encoding="utf-8")
    with pytest.raises(SceneMismatch):
        load_scene(captures, "garage")
