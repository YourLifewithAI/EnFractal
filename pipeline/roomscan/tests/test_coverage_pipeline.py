"""C2 end to end on a synthetic room: ingest, batched poses, merge, map, guidance and report.

The pose model and the detector are replaced by ray-cast fakes (tests/scene.py), so this runs on
any machine without a GPU, a model download or a real photo.
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pytest
from PIL import Image

import scene
import synth
from roomscan.coverage.run import run_coverage
from roomscan.ingest import ingest


def capture_plan(room: scene.Room) -> dict[str, np.ndarray]:
    """Cameras that look at every wall except the one at +z, from standing and knee height."""
    cams = {}
    ring = scene.ring_cameras(18, radius=0.6, height=1.5, centre=(-0.5, -0.8), from_deg=135, to_deg=405,
                              pitch_target_y=1.2)
    low = scene.ring_cameras(6, radius=0.6, height=0.45, centre=(-0.5, -0.8), from_deg=150, to_deg=390,
                             pitch_target_y=0.3)
    for k, c2w in enumerate(ring + low):
        cams[f"cam_{k:02d}"] = c2w
    # Three looks up toward the ceiling, tilted so the top of a wall is in the picture too.
    for k, (x, z, dx, dz) in enumerate([(-0.9, -1.2, -0.6, -0.4), (-0.3, -0.6, 0.6, -0.5), (0.3, -1.4, 0.4, -0.6)]):
        cams[f"cam_8{k}"] = scene.look_at((x, 1.5, z), (x + dx, 2.0, z + dz))
    # Two looks at the shelving unit from its front.
    cams["cam_90"] = scene.look_at((-0.6, 1.2, -0.3), (-1.8, 0.9, -0.3))
    cams["cam_91"] = scene.look_at((-0.6, 1.2, 0.0), (-1.8, 0.9, -0.5))
    return cams


@pytest.fixture()
def garage_session(tmp_path: Path):
    room = scene.garage()
    cams = capture_plan(room)
    source = tmp_path / "Drive" / "Garage"
    for k, name in enumerate(sorted(cams)):
        synth.save_jpeg(synth.view(100 + k, (k * 20 % 900, k * 13 % 700)), source / f"{name}.jpg")
    captures = tmp_path / "repo" / "captures"
    ingest(source, "garage", captures, workers=1, log=lambda *_: None)
    return room, cams, source, captures


def test_coverage_end_to_end(garage_session):
    room, cams, source, captures = garage_session
    result = run_coverage(
        captures, "garage", chunk_size=10, anchors=3,
        backend_factory=lambda: scene.FakeBackend(room, cams, seed=3),
        detector_factory=lambda: scene.FakeDetector(room, cams),
        log=lambda *_: None,
    )
    room_dir = captures / "garage"
    cov = Path(result["report"]).parent
    for name in ("coverage.json", "coverage-map.png", "walls.png", "coverage-report.md", "coverage-report.html",
                 "coverage-run.json", "poses.npz", "poses.json"):
        assert (cov / name).is_file(), name
    for name in ("coverage-report.html", "coverage-report.md", "coverage-map.png", "walls.png"):
        assert (room_dir / name).is_file(), name
    with Image.open(room_dir / "coverage-map.png") as im:
        assert im.width > 600 and im.height > 600

    record = json.loads((cov / "coverage.json").read_bytes())
    frame = record["room_frame"]
    dims = sorted([frame["width_x_m"], frame["depth_z_m"]])
    assert dims == pytest.approx([4.0, 5.0], abs=0.15)
    assert frame["ceiling_y_m"] == pytest.approx(2.5, abs=0.1)
    assert all(p["fitted"] for p in record["photos"])

    # The wall nobody faced is reported as unseen, and the guidance asks for it first among walls.
    surfaces = record["surfaces"]
    worst = min((k for k in surfaces if k.startswith("wall")), key=lambda k: surfaces[k]["good_pct"])
    assert surfaces[worst]["unseen_pct"] > 50
    items = result["guidance"]["items"]
    wall_items = [it for it in items if it["kind"] == "wall"]
    assert wall_items and wall_items[0]["wall"] == worst.split()[-1]
    assert "No photo shows" in wall_items[0]["why"]
    assert any(it["kind"] == "wall" and it["wall"] == worst.split()[-1] and "low" in it["bands"] for it in items)

    # Furniture is found and the shelving unit, seen only from the front, gets a request for its sides.
    labels = {o["label"] for o in record["objects"]}
    assert {"shelving unit", "table"} <= labels
    shelf_items = [it for it in items + result["guidance"]["more"] if it.get("label") == "shelving unit"]
    assert shelf_items, "the shelving unit needs photos from its sides"
    assert set(shelf_items[0]["missing_sides"]) & {"front-left", "front-right"}
    text = shelf_items[0]["text"]
    assert "shelving unit" in text and "metre" in text and "photos" in text

    # Every instruction is specific: a count, a place and a distance or height.
    for it in items:
        assert it["photos"] >= 1 and (it["text"][0].isupper() or it["text"][0].isdigit())
    run = json.loads((cov / "coverage-run.json").read_bytes())
    assert run["money_spent_usd"] == 0 and run["poses"]["merged_views"] == len(cams)

    # The report stays private and readable: no source path, plain words, the map embedded.
    page = (room_dir / "coverage-report.html").read_text(encoding="utf-8")
    assert str(source) not in page and "Drive" not in page
    assert "What to photograph next" in page and "data:image/png;base64," in page
    md = (room_dir / "coverage-report.md").read_text(encoding="utf-8")
    assert "## What to photograph next" in md and "coverage-map.png" in md
    assert b"\r\n" not in (cov / "coverage.json").read_bytes()


def test_bad_batch_is_left_out_not_misplaced(garage_session):
    room, cams, _, captures = garage_session
    result = run_coverage(
        captures, "garage", chunk_size=10, anchors=3, skip_detection=True,
        backend_factory=lambda: scene.FakeBackend(room, cams, seed=5, corrupt_batches={2}),
        log=lambda *_: None,
    )
    fits = result["run"]["poses"]["fits"]
    assert fits[2]["merged"] is False
    assert all(f["merged"] for k, f in enumerate(fits) if k != 2)
    record = json.loads((Path(result["report"]).parent / "coverage.json").read_bytes())
    unplaced = [p for p in record["photos"] if not p["placed"]]
    assert len(unplaced) == fits[2]["new_views"]
    bridge = [it for it in result["guidance"]["items"] + result["guidance"]["more"] if it["kind"] == "bridge"]
    assert bridge and all(p["name"] in bridge[0]["photos_named"] for p in unplaced)
