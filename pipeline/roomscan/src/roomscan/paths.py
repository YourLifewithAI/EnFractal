"""Where the pipeline may read and write.

Rules (AGENTS.md, docs/runs/RUN-1.md):
- Originals are read in place and never written: the source folder is read-only to us.
- Everything derived goes under ``<captures root>/<room>/``; Git ignores ``captures/``.
- Model caches stay inside the repository's ``.cache/`` so nothing lands in the user profile.
"""

from __future__ import annotations

import os
import re
from pathlib import Path

ROOM_TOKEN = re.compile(r"^[a-z0-9][a-z0-9_-]{0,63}$")


class PathPolicyError(ValueError):
    """A path would break the read-in-place or write-only-under-captures rules."""


def find_repo_root(start: Path | None = None) -> Path | None:
    """The EnFractal checkout containing this package (has AGENTS.md and contracts/)."""
    here = (start or Path(__file__)).resolve()
    for candidate in [here, *here.parents]:
        if (candidate / "AGENTS.md").is_file() and (candidate / "contracts").is_dir():
            return candidate
    return None


def default_captures_root() -> Path:
    root = find_repo_root()
    if root is None:
        raise PathPolicyError("Cannot find the repository root; pass --captures-root explicitly.")
    return root / "captures"


def validate_room(room: str) -> str:
    if not ROOM_TOKEN.match(room):
        raise PathPolicyError(f"Room name must be a lowercase token like 'garage', got {room!r}.")
    return room


def _is_within(child: Path, parent: Path) -> bool:
    try:
        child.relative_to(parent)
        return True
    except ValueError:
        return False


def room_root(captures_root: Path, room: str) -> Path:
    return Path(captures_root).resolve() / validate_room(room)


def check_source_and_output(source: Path, out_root: Path) -> None:
    """Refuse layouts where writing the output could touch the source folder or vice versa."""
    src = Path(source).resolve()
    out = Path(out_root).resolve()
    if not src.is_dir():
        raise PathPolicyError(f"Source folder not found: {src}")
    if _is_within(out, src) or _is_within(src, out):
        raise PathPolicyError(
            "The output folder and the source folder overlap. Originals are read in place and "
            f"nothing may be written next to them (source {src}, output {out})."
        )


class OutputGuard:
    """Every write goes through here so nothing escapes ``<captures>/<room>/``."""

    def __init__(self, room_dir: Path, forbidden: Path | None = None):
        self.root = Path(room_dir).resolve()
        self.forbidden = Path(forbidden).resolve() if forbidden else None

    def path(self, *parts: str | os.PathLike[str]) -> Path:
        target = self.root.joinpath(*parts).resolve()
        if not _is_within(target, self.root):
            raise PathPolicyError(f"Refusing to write outside {self.root}: {target}")
        if self.forbidden is not None and _is_within(target, self.forbidden):
            raise PathPolicyError(f"Refusing to write into the source folder: {target}")
        return target

    def mkdir(self, *parts: str | os.PathLike[str]) -> Path:
        target = self.path(*parts)
        target.mkdir(parents=True, exist_ok=True)
        return target

    def write_bytes(self, rel: str | os.PathLike[str], data: bytes) -> Path:
        target = self.path(rel)
        target.parent.mkdir(parents=True, exist_ok=True)
        tmp = target.with_name(target.name + ".part")
        tmp.write_bytes(data)
        os.replace(tmp, target)
        return target

    def rel(self, target: Path) -> str:
        return Path(target).resolve().relative_to(self.root).as_posix()


def set_model_caches(repo_root: Path | None = None) -> dict[str, str]:
    """Point Hugging Face and torch caches into the checkout's .cache/ unless already set."""
    root = repo_root or find_repo_root()
    if root is None:
        return {}
    cache = root / ".cache"
    wanted = {
        "HF_HOME": cache / "hf",
        "TORCH_HOME": cache / "torch",
        "XDG_CACHE_HOME": cache / "xdg",
    }
    applied: dict[str, str] = {}
    for key, value in wanted.items():
        if not os.environ.get(key):
            os.environ[key] = str(value)
        applied[key] = os.environ[key]
    # No telemetry, no implicit login prompts.
    os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")
    os.environ.setdefault("DO_NOT_TRACK", "1")
    return applied
