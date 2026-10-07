"""C2 end to end on a synthetic room: ingest, batched poses, merge, map, guidance and report.

The pose model and the detector are replaced by ray-cast fakes (tests/scene.py), so this runs on
any machine without a GPU, a model download or a real photo.
"""

from __future__ import annotations

import json
import re
from pathlib import Path

import pytest
from PIL import Image

from roomscan.coverage.run import run_coverage


def test_coverage_end_to_end(garage_session):
    cams, source, captures = garage_session.cams, garage_session.source, garage_session.captures
    result = run_coverage(
        captures, "garage", chunk_size=10, anchors=3,
        backend_factory=garage_session.backend(), detector_factory=garage_session.detector(),
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
    # Photos without lens data (these synthetic ones carry none) are left to the model, and the run says so.
    assert run["poses"]["intrinsics"]["from_exif"] == 0 and len(run["poses"]["intrinsics"]["left_to_the_model"]) == len(cams)
    assert run["scale"]["scale_source"] == "seed_batch" and "factor" not in run["poses"]["scale"]

    # The report stays private and readable: no source path, plain words, the map embedded.
    page = (room_dir / "coverage-report.html").read_text(encoding="utf-8")
    assert str(source) not in page and "Drive" not in page
    assert "What to photograph next" in page and "data:image/png;base64," in page
    md = (room_dir / "coverage-report.md").read_text(encoding="utf-8")
    assert "## What to photograph next" in md and "coverage-map.png" in md
    assert b"\r\n" not in (cov / "coverage.json").read_bytes()


def test_bad_batch_is_left_out_not_misplaced(garage_session):
    captures = garage_session.captures
    result = run_coverage(
        captures, "garage", chunk_size=10, anchors=3, skip_detection=True,
        backend_factory=garage_session.backend(seed=5, corrupt_batches={2}),
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


def test_names_from_outside_are_escaped_in_both_reports(garage_session):
    # File names are data. Give every photo a name that tries to be markup, and make sure it comes out of
    # the HTML report and the Markdown report as the same text, never as a tag, a link or an entity.
    session = garage_session
    manifest_path = session.captures / "garage" / "sessions" / session.session_id / "manifest.json"
    manifest = json.loads(manifest_path.read_bytes())
    hostile = 'x"><img src=y onerror=alert(1)>&[a](http://evil.example)_*|.jpg'
    for i, photo in enumerate(manifest["photos"]):
        photo["name"] = f"{i:02d}{hostile}"
    names = [p["name"] for p in manifest["photos"]]
    manifest["photos"][0]["lens_kind"] = "front"  # a note that lists the photo, with its thumbnail
    manifest["flags"]["blurry"] = [names[1]]
    manifest["duplicates"]["exact"] = [[names[2], names[3]]]  # the table of exact copies
    manifest_path.write_bytes(json.dumps(manifest).encode("utf-8"))

    result = run_coverage(session.captures, "garage", chunk_size=10, anchors=3, skip_detection=True,
                          backend_factory=session.backend(), log=lambda *_: None)
    page = Path(result["report"]).read_text(encoding="utf-8")
    md = Path(result["markdown"]).read_text(encoding="utf-8")

    assert "<img src=y" not in page and "onerror=alert(1)>" not in page
    assert page.count("&lt;img src=y onerror=alert(1)&gt;") >= 3  # the note, the copies table and a step's photo
    assert "&quot;&gt;&lt;img" in page and "&amp;[a](http://evil.example)" in page
    assert not re.search(r"(?<!\\)[<>]", md)  # no angle bracket that is not backslash-escaped
    assert "\\<img src=y onerror=alert(1)\\>" in md and "[a](" not in md and "\\[a\\]" in md and "\\|" in md
    assert "\\&" in md and "\\_\\*" in md
