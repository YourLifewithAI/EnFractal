#!/usr/bin/env python3
"""Copy the shipped characters into the game: assets/characters/<id>/ -> game/characters/<id>/.

The game reads only three files per character, as raw bytes, never through Godot's importer:
  character.glb   copied byte for byte (binary: Git never converts it);
  character.json  re-encoded with LF line endings (the bytes in a Windows working tree may be CRLF);
  rig.json        the optional hand-tuned sidecar, re-encoded with LF line endings.
Beside each GLB it writes character.glb.import with importer="keep", so the editor never imports the model and an
export packs the file exactly as it is. Renders, drawings and interpretation.json stay in assets/.

A character folder is any folder holding character.json and character.glb; others (drawings/) are skipped.
Folders under game/characters/ with no source are removed. Same inputs, same outputs; no clocks, no randomness.

  python tools/characters/sync_characters.py            # write game/characters/
  python tools/characters/sync_characters.py --check    # exit 1 and name the difference if the copies are stale

Never hash JSON text: under Git's autocrlf the working-tree bytes differ by machine. --check compares JSON by
its parsed value and the GLB by its bytes.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "assets" / "characters"
TARGET = ROOT / "game" / "characters"
SHIPPED = ("character.glb", "character.json", "rig.json")
KEEP_IMPORT = b'[remap]\n\nimporter="keep"\n'
ID = re.compile(r"[a-z][a-z0-9_]{0,63}")


def lf(data: bytes) -> bytes:
    """The JSON bytes with LF line endings, no byte-order mark, and exactly one final newline."""
    if data.startswith(b"\xef\xbb\xbf"):
        data = data[3:]
    return data.replace(b"\r\n", b"\n").rstrip(b"\n") + b"\n"


def parsed(data: bytes):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError(f"duplicate JSON key: {key}")
            result[key] = value
        return result
    return json.loads(data.decode("utf-8-sig"), object_pairs_hook=pairs)


def plan() -> dict[str, dict[str, bytes]]:
    """Every output file, keyed by character id then file name."""
    result: dict[str, dict[str, bytes]] = {}
    for folder in sorted(p for p in SOURCE.iterdir() if p.is_dir()):
        if not (folder / "character.json").is_file() or not (folder / "character.glb").is_file():
            continue
        if not ID.fullmatch(folder.name):
            raise SystemExit(f"{folder.name}: a character folder name must match [a-z][a-z0-9_]{{0,63}}")
        files: dict[str, bytes] = {}
        for name in SHIPPED:
            path = folder / name
            if not path.is_file():
                continue
            data = path.read_bytes()
            if name.endswith(".json"):
                parsed(data)  # refuse to ship JSON that does not parse
                data = lf(data)
            files[name] = data
        files["character.glb.import"] = KEEP_IMPORT
        result[folder.name] = files
    return result


def check(expected: dict[str, dict[str, bytes]]) -> list[str]:
    problems = []
    present = sorted(p.name for p in TARGET.iterdir() if p.is_dir()) if TARGET.is_dir() else []
    for stale in sorted(set(present) - set(expected)):
        problems.append(f"game/characters/{stale}/ has no source in assets/characters/")
    for cid, files in expected.items():
        folder = TARGET / cid
        actual = sorted(p.name for p in folder.iterdir()) if folder.is_dir() else []
        for extra in sorted(set(actual) - set(files)):
            problems.append(f"game/characters/{cid}/{extra} is not a shipped file")
        for name, data in files.items():
            path = folder / name
            if not path.is_file():
                problems.append(f"game/characters/{cid}/{name} is missing")
                continue
            have = path.read_bytes()
            if name.endswith(".json"):
                same = parsed(have) == parsed(data)
            elif name.endswith(".import"):
                same = lf(have) == data
            else:
                same = have == data
            if not same:
                problems.append(f"game/characters/{cid}/{name} differs from assets/characters/{cid}/")
    return problems


def write(expected: dict[str, dict[str, bytes]]) -> None:
    TARGET.mkdir(parents=True, exist_ok=True)
    for stale in sorted(p for p in TARGET.iterdir() if p.is_dir() and p.name not in expected):
        shutil.rmtree(stale)
    for cid, files in expected.items():
        folder = TARGET / cid
        folder.mkdir(exist_ok=True)
        for extra in sorted(p for p in folder.iterdir() if p.name not in files):
            if extra.is_dir():
                shutil.rmtree(extra)
            else:
                extra.unlink()
        for name, data in files.items():
            (folder / name).write_bytes(data)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="verify only; exit 1 if game/characters/ is stale")
    args = parser.parse_args()
    expected = plan()
    if args.check:
        problems = check(expected)
        for line in problems:
            print("STALE " + line)
        print(f"CHARACTERS_SYNC: {len(expected)} characters, {'stale' if problems else 'fresh'}")
        return 1 if problems else 0
    write(expected)
    for cid, files in expected.items():
        print(f"{cid}: character.glb sha256 {hashlib.sha256(files['character.glb']).hexdigest()}"
              f"{', rig.json' if 'rig.json' in files else ''}")
    print(f"CHARACTERS_SYNC: wrote {len(expected)} characters to game/characters/")
    return 0


if __name__ == "__main__":
    sys.exit(main())
