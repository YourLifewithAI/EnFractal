"""Where the pipeline may write: derived pictures and rooms of a real place never land where Git can track them.

Review of C3 and C4, finding 1: the review helpers saved to any path they were given, and the room manifest guard knew
only the checkout the code runs from. These tests fake a second checkout (a folder with a ``.git``) and a tracked ``docs``
folder next to the capture, and try to write into them.
"""

from __future__ import annotations

import json
import os
from pathlib import Path

import numpy as np
import pytest

import inv_scene
from roomscan.inventory.overlay import object_overlay_sheet
from roomscan.inventory.review import render_review
from roomscan.paths import OutputGuard, PathPolicyError, git_checkout_of
from roomscan.shell import manifest as mf
from roomscan.shell.overlay import overlay_sheet
from roomscan.shell.planes import ShellPlan
from roomscan.shell.spec import parse_spec

from test_shell import build, spec_doc, surfaces, X0, X1, Z0, Z1, H


@pytest.fixture(scope="module")
def scene(tmp_path_factory):
    return inv_scene.build_scene(tmp_path_factory.mktemp("outputs"))


@pytest.fixture()
def checkout(tmp_path):
    """Another clone of the repository: a ``.git`` folder, a tracked ``docs`` and a tracked ``game/rooms``."""
    root = tmp_path / "other-checkout"
    (root / ".git").mkdir(parents=True)
    (root / "docs").mkdir()
    (root / "game" / "rooms").mkdir(parents=True)
    return root


def table_entry():
    return {"id": "table_1", "kind": "table", "confidence": 0.7, "colours": [{"hex": "#aa7744", "share": 1.0}], "evidence": {},
            "box": {"centre_m": [0.6, 0.375, 1.0], "size_m": [0.8, 0.75, 0.8], "yaw_deg": 0.0, "footprint_m": [0.2, 0.6, 1.0, 1.4]},
            "placement": {"position_m": [0.6, 0.0, 1.0], "yaw_deg": 0.0, "front_known": True,
                          "support": {"kind": "floor", "height_m": 0.0}}}


def shell_inputs():
    plan = ShellPlan(X0, X1, Z0, Z1, H, surfaces(), 1.0, "seed_batch")
    return plan, parse_spec(spec_doc())


# --- review pictures stay in the capture ----------------------------------------------------------------------

def test_review_pictures_can_only_be_written_inside_the_capture(scene, checkout):
    plan, spec = shell_inputs()
    inventory = {"room": "garage", "objects": [table_entry()]}
    attempts = {
        "shell overlay": lambda p: overlay_sheet(scene, plan, spec, p),
        "object overlay": lambda p: object_overlay_sheet(scene, table_entry(), p),
        "review image": lambda p: render_review(scene, inventory, ["table_1"], p),
    }
    for name, write in attempts.items():
        for forbidden in (checkout / "docs" / "review.jpg", checkout / "review.png", scene.room_dir.parent / "elsewhere.jpg",
                          scene.room_dir / ".." / "outside.jpg"):
            with pytest.raises(PathPolicyError):
                write(forbidden)
            assert not Path(os.path.normpath(forbidden)).exists(), (name, forbidden)
        # Inside the capture, an absolute path and a path relative to the room folder both work.
        inside = scene.room_dir / "review" / f"{name.replace(' ', '_')}.jpg"
        assert write(inside) == inside.resolve() or write(inside) == inside
        assert inside.is_file()
        relative = write(Path("review") / f"{name.replace(' ', '_')}_relative.jpg")
        assert (scene.room_dir / "review" / f"{name.replace(' ', '_')}_relative.jpg").is_file()
    # Only picture formats, so a review helper cannot be made to write a script into the capture either.
    with pytest.raises(PathPolicyError):
        object_overlay_sheet(scene, table_entry(), scene.room_dir / "review" / "evil.py")


def test_a_capture_folder_inside_a_checkout_must_be_the_ignored_captures_folder(checkout):
    assert git_checkout_of(checkout / "docs" / "deep") == checkout
    assert git_checkout_of(checkout.parent) is None
    (checkout / "captures" / "garage").mkdir(parents=True)
    OutputGuard(checkout / "captures" / "garage")  # the ignored folder: fine
    for tracked in (checkout / "docs", checkout / "game" / "rooms", checkout, checkout / "capturesish"):
        tracked.mkdir(exist_ok=True)
        with pytest.raises(PathPolicyError, match="tracked|captures"):
            OutputGuard(tracked)
    # A worktree's ``.git`` is a file, not a folder.
    worktree = checkout.parent / "a-worktree"
    (worktree / "docs").mkdir(parents=True)
    (worktree / ".git").write_text("gitdir: /somewhere/else\n", encoding="utf-8")
    assert git_checkout_of(worktree / "docs") == worktree
    with pytest.raises(PathPolicyError):
        OutputGuard(worktree / "docs")
    OutputGuard(checkout.parent / "no-checkout-here")  # outside any checkout nothing is asked


# --- the room manifest never goes into any checkout ------------------------------------------------------------------

def test_a_manifest_is_never_written_into_any_checkout_of_the_repository(checkout, tmp_path):
    plan, _ = shell_inputs()
    room = build(plan)
    for destination in (checkout / "game" / "rooms", checkout / "docs", checkout / "game" / "rooms" / "nested" / "deeper"):
        with pytest.raises(mf.ShellError, match="checkout|repository"):
            mf.write_room(destination, room)
        assert not list(destination.rglob("room.json")) if destination.exists() else True
    # A worktree too (its .git is a file), and a symlinked rooms folder that resolves into a checkout.
    worktree = tmp_path / "wt"
    (worktree / "rooms").mkdir(parents=True)
    (worktree / ".git").write_text("gitdir: /x\n", encoding="utf-8")
    with pytest.raises(mf.ShellError):
        mf.write_room(worktree / "rooms", room)
    link = tmp_path / "user-data" / "rooms"
    link.mkdir(parents=True)
    try:
        os.symlink(checkout / "game" / "rooms", link / "garage", target_is_directory=True)
    except (OSError, NotImplementedError):
        pytest.skip("this machine cannot create directory symlinks")
    with pytest.raises(mf.ShellError):
        mf.write_room(link, room)
    assert not (checkout / "game" / "rooms" / "room.json").exists()
    # Outside every checkout, a room is written.
    written = mf.write_room(tmp_path / "user-data-ok" / "rooms", room)
    assert written.published and json.loads(written.path.read_bytes())["room_id"] == "garage"
