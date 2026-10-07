"""C1 ingest on synthetic photos: privacy, read-only source, dedupe, quality and sessions."""

from __future__ import annotations

import hashlib
import json
import os
import shutil
from pathlib import Path

import pytest
from PIL import Image

import synth
from roomscan import exif as exifmod
from roomscan import quality
from roomscan.ingest import (INGEST_VERSION, SessionConflictError, ingest, photo_sort_key, render_report,
                             resolve_session, scan_source, session_id_for)
from roomscan.textutil import md_text
from roomscan.paths import OutputGuard, PathPolicyError


def snapshot(folder: Path) -> dict[str, tuple[int, int, str]]:
    out = {}
    for p in sorted(folder.rglob("*")):
        if p.is_file():
            out[p.relative_to(folder).as_posix()] = (p.stat().st_size, p.stat().st_mtime_ns,
                                                     hashlib.sha256(p.read_bytes()).hexdigest())
    return out


@pytest.fixture()
def layout(tmp_path: Path):
    # Spaces and parentheses like "G:\My Drive\Enfractal\Photos for space generation\Garage".
    source = tmp_path / "My Drive" / "Photos for space generation" / "Garage"
    source.mkdir(parents=True)
    captures = tmp_path / "repo" / "captures"
    return source, captures


def make_set(source: Path) -> None:
    gps = synth.exif_with_gps()
    synth.save_jpeg(synth.view(1, (0, 0)), source / "IMG_0001.jpg", exif=gps, xmp=synth.FAKE_XMP)
    synth.save_heic(synth.view(1, (300, 200)), source / "IMG_0002.HEIC", exif=gps, xmp=synth.FAKE_XMP)
    synth.save_jpeg(synth.view(1, (700, 500)), source / "IMG_0003.jpg", exif=gps)
    # Byte-identical copy with the " (1)" name Google Drive gives a second upload.
    shutil.copyfile(source / "IMG_0001.jpg", source / "IMG_0001 (1).jpg")
    # Same frame re-encoded: a near-duplicate, not an exact one.
    synth.save_jpeg(synth.view(1, (300, 200)), source / "IMG_0004.jpg", exif=gps, quality=70)
    # Motion-blurred and badly exposed frames.
    synth.save_jpeg(synth.blurred(synth.view(1, (900, 100)), 8), source / "IMG_0005.jpg", exif=gps)
    synth.save_jpeg(Image.new("RGB", (640, 480), (254, 254, 254)), source / "IMG_0006.jpg", exif=gps)
    synth.save_jpeg(Image.new("RGB", (640, 480), (2, 2, 2)), source / "IMG_0007.jpg", exif=gps)


def run(source: Path, captures: Path, workers: int = 1) -> dict:
    return ingest(source, "garage", captures, workers=workers, log=lambda *_: None)


def test_gps_never_survives(layout):
    source, captures = layout
    make_set(source)
    result = run(source, captures, workers=2)  # also exercises the process pool
    room = captures / "garage"
    written = [p for p in room.rglob("*") if p.is_file()]
    assert written
    for p in written:
        data = p.read_bytes()
        for marker in (b"GPSLatitude", b"GPSLongitude", b"48,51.4959N", b"MAKERNOTE-SECRET", b"exif:GPS"):
            assert marker not in data, (p, marker)
        if p.suffix == ".jpg":
            exifmod.assert_no_location(data, p.name)
            with Image.open(p) as im:
                assert not im.getexif().get_ifd(0x8825)
                assert 0x927C not in im.getexif().get_ifd(0x8769)
                assert "xmp" not in im.info
    manifest = result["manifest"]
    text = json.dumps(manifest)
    assert "29.59" not in text and "40.12" not in text  # the fake coordinates
    assert manifest["summary"]["sources_with_location"] >= 7


def test_kept_metadata(layout):
    source, captures = layout
    make_set(source)
    manifest = run(source, captures)["manifest"]
    photo = next(p for p in manifest["photos"] if p["name"] == "IMG_0002.HEIC")
    e = photo["exif"]
    assert e["make"] == "TestMaker" and e["model"] == "TestPhone 1"
    assert e["focal_length_mm"] == pytest.approx(5.96) and e["focal_length_35mm"] == 26
    assert e["exposure_time"] == "1/40" and e["f_number"] == pytest.approx(1.6) and e["iso"] == 500
    assert e["datetime_original"] == "2026:10:04 07:56:46" and e["offset_time_original"] == "-05:00"
    with Image.open(captures / "garage" / photo["derived"]["jpeg"]) as im:
        fields = exifmod.read_fields(im)
    assert fields["model"] == "TestPhone 1" and fields["iso"] == 500 and fields["focal_length_35mm"] == 26
    assert fields["exposure_time"] == "1/40" and fields["datetime_original"] == "2026:10:04 07:56:46"


def test_source_is_read_only_and_writes_stay_in_room(layout):
    source, captures = layout
    make_set(source)
    before = snapshot(source)
    run(source, captures)
    assert snapshot(source) == before
    tmp_root = source.parents[2]
    for p in tmp_root.rglob("*"):
        if p.is_file() and not p.is_relative_to(source):
            assert p.is_relative_to(captures / "garage"), p


def test_refuses_output_inside_source(layout):
    source, _ = layout
    make_set(source)
    with pytest.raises(PathPolicyError):
        ingest(source, "garage", source / "captures", workers=1, log=lambda *_: None)
    assert not (source / "captures").exists()


def test_output_guard_blocks_escape(tmp_path):
    guard = OutputGuard(tmp_path / "captures" / "garage", forbidden=tmp_path / "src")
    with pytest.raises(PathPolicyError):
        guard.path("..", "elsewhere.txt")
    with pytest.raises(PathPolicyError):
        guard.path(tmp_path / "src" / "x.jpg")


def test_duplicates_and_near_duplicates(layout):
    source, captures = layout
    make_set(source)
    manifest = run(source, captures)["manifest"]
    photos = {p["name"]: p for p in manifest["photos"]}
    assert photos["IMG_0001 (1).jpg"]["status"] == "duplicate"
    assert photos["IMG_0001 (1).jpg"]["duplicate_of"] == "IMG_0001.jpg"
    assert photos["IMG_0001.jpg"]["status"] == "ok"
    assert manifest["duplicates"]["exact"] == [["IMG_0001.jpg", "IMG_0001 (1).jpg"]]
    near = manifest["duplicates"]["near"]
    assert len(near) == 1
    assert {near[0]["keep"], *near[0]["others"]} == {"IMG_0002.HEIC", "IMG_0004.jpg"}
    # Overlapping but different viewpoints are not near-duplicates.
    assert photos["IMG_0003.jpg"]["status"] == "ok"


def test_quality_flags(layout):
    source, captures = layout
    make_set(source)
    manifest = run(source, captures)["manifest"]
    flags = manifest["flags"]
    assert "IMG_0005.jpg" in flags["blurry"]
    assert "IMG_0006.jpg" in flags["highlights_clipped"] and "IMG_0006.jpg" in flags["overexposed"]
    assert "IMG_0007.jpg" in flags["shadows_crushed"] and "IMG_0007.jpg" in flags["underexposed"]
    sharp = {p["name"]: p for p in manifest["photos"]}["IMG_0003.jpg"]
    assert sharp["quality"]["flags"] == []


def test_session_is_stable_and_new_photos_make_a_new_session(layout):
    source, captures = layout
    make_set(source)
    first = run(source, captures)
    manifest_path = Path(first["session_dir"]) / "manifest.json"
    first_bytes = manifest_path.read_bytes()
    assert b"\r\n" not in first_bytes and first_bytes.endswith(b"\n")
    again = run(source, captures)  # second run is served from the per-photo cache
    assert again["session_id"] == first["session_id"]
    assert manifest_path.read_bytes() == first_bytes
    assert again["run"]["cached"] == again["run"]["converted"] + again["run"]["cached"]

    synth.save_jpeg(synth.view(2, (100, 100)), source / "IMG_0100.jpg")
    later = run(source, captures)
    assert later["session_id"] != first["session_id"]
    assert manifest_path.read_bytes() == first_bytes
    assert later["run"]["converted"] == 1
    assert resolve_session(captures, "garage", "latest") == Path(later["session_dir"]).resolve()
    names = [p["name"] for p in later["manifest"]["photos"]]
    for p in later["manifest"]["photos"]:
        assert set(p) >= {"name", "size_bytes", "sha256", "status"}
        assert p["sha256"] == hashlib.sha256((source / p["name"]).read_bytes()).hexdigest()
        assert p["size_bytes"] == (source / p["name"]).stat().st_size
    assert "IMG_0100.jpg" in names


def test_orientation_is_applied(layout):
    source, captures = layout
    synth.save_jpeg(synth.view(3, (0, 0), (60, 40)), source / "rotated.jpg", exif=synth.exif_with_gps(orientation=6))
    manifest = run(source, captures)["manifest"]
    photo = manifest["photos"][0]
    assert (photo["image"]["width"], photo["image"]["height"]) == (40, 60)
    with Image.open(captures / "garage" / photo["derived"]["jpeg"]) as im:
        assert im.size == (40, 60)
        assert im.getexif().get(0x0112) == 1


def test_unreadable_file_is_reported_not_fatal(layout):
    source, captures = layout
    synth.save_jpeg(synth.view(4, (0, 0)), source / "good.jpg")
    (source / "broken.jpg").write_bytes(b"not a jpeg at all")
    manifest = run(source, captures)["manifest"]
    photos = {p["name"]: p for p in manifest["photos"]}
    assert photos["broken.jpg"]["status"] == "unreadable"
    assert photos["good.jpg"]["status"] == "ok"


def test_scan_order_and_names(tmp_path):
    for name in ["IMG_2671 (1).jpg", "IMG_2671.jpg", "IMG_2670.HEIC", "notes.txt", "sub dir/IMG_9.heic"]:
        p = tmp_path / name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(b"x")
    names = [p.name for p in scan_source(tmp_path)]
    assert names == ["IMG_2670.HEIC", "IMG_2671.jpg", "IMG_2671 (1).jpg", "sub dir/IMG_9.heic"]
    assert photo_sort_key("IMG_2671 (2).jpg") > photo_sort_key("IMG_2671 (1).jpg")
    assert os.sep not in "".join(names) or os.sep == "/"


def test_lens_kind():
    assert exifmod.lens_kind({"lens_model": "iPhone 17 front camera 2.7mm", "focal_length_35mm": 30}) == "front"
    assert exifmod.lens_kind({"lens_model": "back dual wide 2.22mm", "focal_length_35mm": 14}) == "ultra_wide"
    assert exifmod.lens_kind({"focal_length_35mm": 26}) == "main"
    assert exifmod.lens_kind({"focal_length_35mm": 52}) == "zoom"
    assert exifmod.lens_kind({}) == "unknown"


def test_edge_sharpness_separates_blur_from_plain_surfaces():
    sharp = quality.analysis_gray(synth.view(5, (0, 0), (1024, 768)))
    blurry = quality.analysis_gray(synth.blurred(synth.view(5, (0, 0), (1024, 768)), 4))
    # A plain wall with one faint, crisp-edged panel on it: little detail, but nothing smeared.
    wall = Image.new("RGB", (1024, 768), (200, 198, 190))
    wall.paste((170, 168, 160), (400, 250, 640, 520))
    plain = quality.analysis_gray(wall)
    assert quality.edge_sharpness(blurry) < quality.Thresholds().blur_edge_max < quality.edge_sharpness(sharp)
    assert quality.edge_sharpness(plain) > quality.Thresholds().blur_edge_max
    records = [{"quality": {**quality.sharpness(g), "edge_sharpness": quality.edge_sharpness(g), **quality.exposure(g)}}
               for g in (sharp, sharp, sharp, blurry, plain)]
    quality.flag_photos(records)
    assert "blurry" in records[3]["quality"]["flags"]
    assert "blurry" not in records[4]["quality"]["flags"] and "low_detail" in records[4]["quality"]["flags"]


def test_manifest_counts_lenses(layout):
    source, captures = layout
    make_set(source)
    manifest = run(source, captures)["manifest"]
    assert manifest["summary"]["lens_counts"] == {"main": 7}  # every file except the exact copy
    assert {p.get("lens_kind") for p in manifest["photos"] if p["status"] == "ok"} == {"main"}


def test_clean_exif_leaves_the_gps_block_out_itself(tmp_path):
    # Ingest also fails closed when a location survives, but the clean copy must be clean on its own.
    path = synth.save_jpeg(synth.view(1, (0, 0)), tmp_path / "gps.jpg", exif=synth.exif_with_gps(), xmp=synth.FAKE_XMP)
    with Image.open(path) as im:
        assert im.getexif().get_ifd(exifmod.GPS_IFD)  # the source does carry one
        clean = exifmod.clean_exif(im)
    assert exifmod.GPS_IFD not in clean and not clean.get_ifd(exifmod.GPS_IFD)
    sub = clean[exifmod.EXIF_IFD]  # held as a plain dict until it is written
    assert 0x927C not in sub  # the maker note is gone too
    assert clean[exifmod.MODEL] == "TestPhone 1" and sub[exifmod.FOCAL_35MM] == 26


def test_the_session_follows_the_photos_the_rules_and_the_version(layout):
    source, captures = layout
    make_set(source)
    first = run(source, captures)
    listing = [{k: p[k] for k in ("name", "size_bytes", "sha256")} for p in first["manifest"]["photos"]]
    same = session_id_for(listing)
    assert same == first["session_id"] and session_id_for(list(reversed(listing))) == same
    assert session_id_for(listing, quality.Thresholds(blur_abs_min=30.0)) != same
    assert session_id_for(listing, ingest_version=INGEST_VERSION + 1) != same

    # Re-running after a threshold change makes a new session and leaves the old one exactly as it was.
    old = Path(first["session_dir"]) / "manifest.json"
    old_bytes = old.read_bytes()
    stricter = ingest(source, "garage", captures, workers=1, thresholds=quality.Thresholds(blur_abs_min=30.0),
                      log=lambda *_: None)
    assert stricter["session_id"] != first["session_id"]
    assert stricter["manifest"]["thresholds"]["blur_abs_min"] == 30.0
    assert old.read_bytes() == old_bytes and old_bytes.count(b'"blur_abs_min": 25.0') == 1


def test_a_session_is_never_overwritten_by_a_different_manifest(layout):
    source, captures = layout
    make_set(source)
    first = run(source, captures)
    manifest = Path(first["session_dir"]) / "manifest.json"
    manifest.write_bytes(manifest.read_bytes() + b" ")  # as if the code had changed without a new version
    changed = manifest.read_bytes()
    with pytest.raises(SessionConflictError, match="INGEST_VERSION"):
        run(source, captures)
    assert manifest.read_bytes() == changed


def test_derived_files_are_named_by_the_bytes_alone(layout, tmp_path):
    source, captures = layout
    make_set(source)
    first = run(source, captures)
    derived = {p["sha256"]: p["derived"] for p in first["manifest"]["photos"] if p.get("derived")}
    assert derived and all(d["jpeg"] == f"photos/{sha[:16]}.jpg" and d["thumb"] == f"thumbs/{sha[:16]}.jpg"
                           for sha, d in derived.items())
    # The same bytes under other names, converted from scratch somewhere else, land in the same files.
    other = tmp_path / "other" / "Drive"
    other.mkdir(parents=True)
    for k, p in enumerate(sorted(source.iterdir())):
        shutil.copyfile(p, other / f"renamed_{k:02d}{p.suffix}")
    elsewhere = ingest(other, "garage", tmp_path / "other" / "captures", workers=1, log=lambda *_: None)
    assert {p["sha256"]: p["derived"]["jpeg"] for p in elsewhere["manifest"]["photos"] if p.get("derived")} ==         {sha: d["jpeg"] for sha, d in derived.items()}
    # And a cache hit does not remember which name converted the bytes first.
    (source / "IMG_0003.jpg").rename(source / "A_renamed.jpg")
    again = run(source, captures)
    moved = next(p for p in again["manifest"]["photos"] if p["name"] == "A_renamed.jpg")
    assert moved["derived"]["jpeg"] == f"photos/{moved['sha256'][:16]}.jpg"
    assert again["run"]["converted"] == 0


def test_the_markdown_report_escapes_file_names_and_lens_text():
    hostile = "[x](http://evil.example)<script>alert(1)</script>|*a*_b_`c`&amp;\nsecond line"
    m = {"room": "garage", "session_id": "s-1",
         "summary": {"files": 2, "total_bytes": 10, "status_counts": {"ok": 2}, "usable_for_poses": 2,
                     "session_median_sharpness": 1.0, "sources_with_location": 0},
         "duplicates": {"exact": [[hostile, hostile + " (1)"]], "near": [{"keep": hostile, "others": [hostile + "2"]}]},
         "flags": {"blurry": [hostile]},
         "photos": [{"name": hostile, "status": "ok", "exif": {"focal_length_35mm": 26, "lens_model": hostile}}]}
    text = render_report(m)
    for raw in ("[x](", "<script>", "*a*", "_b_", "`c`", "\nsecond"):
        assert raw not in text, raw
    assert "\\[x\\]" in text and "\\<script\\>" in text and "\\&amp;" in text
    assert md_text("a\nb\r\n| c |") == "a b \\| c \\|"
