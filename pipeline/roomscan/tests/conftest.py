"""Shared fixtures: the synthetic garage, its photos on a pretend Drive, and an ingested session."""

from __future__ import annotations

import dataclasses
import shutil
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import pytest

import scene
import synth
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


@dataclass
class Session:
    room: scene.Room
    cams: dict[str, np.ndarray]
    source: Path
    captures: Path
    names: dict[str, str]
    session_id: str = ""

    def backend(self, seed: int = 3, **kwargs):
        return lambda: scene.FakeBackend(self.room, self.cams, names=self.names, seed=seed, **kwargs)

    def detector(self):
        return lambda: scene.FakeDetector(self.room, self.cams)


def build_garage_session(tmp_path: Path) -> Session:
    room = scene.garage()
    cams = capture_plan(room)
    source = tmp_path / "Drive" / "Garage"
    for k, name in enumerate(sorted(cams)):
        synth.save_jpeg(synth.view(100 + k, (k * 20 % 900, k * 13 % 700)), source / f"{name}.jpg")
    captures = tmp_path / "repo" / "captures"
    result = ingest(source, "garage", captures, workers=1, log=lambda *_: None)
    return Session(room, cams, source, captures, scene.camera_names(result["manifest"]), result["session_id"])


@pytest.fixture(scope="session")
def garage_master(tmp_path_factory) -> Session:
    """The photos are made and ingested once for the whole run (about 30 s); tests work on copies."""
    return build_garage_session(tmp_path_factory.mktemp("garage-master"))


@pytest.fixture()
def garage_session(garage_master: Session, tmp_path: Path) -> Session:
    """A private copy of the ingested garage, safe to write coverage output, measurements and edits into."""
    captures = tmp_path / "repo" / "captures"
    shutil.copytree(garage_master.captures, captures)
    return dataclasses.replace(garage_master, captures=captures)
