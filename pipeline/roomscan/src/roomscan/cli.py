"""Command line: ``roomscan ingest`` (C1) and ``roomscan coverage`` (C2)."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from .paths import default_captures_root, set_model_caches


def _captures(args: argparse.Namespace) -> Path:
    return Path(args.captures_root) if args.captures_root else default_captures_root()


def cmd_ingest(args: argparse.Namespace) -> int:
    from .ingest import ingest

    result = ingest(Path(args.source), args.room, _captures(args), workers=args.workers)
    summary = result["manifest"]["summary"]
    print(f"session {result['session_id']}  files {summary['files']}  usable {summary['usable_for_poses']}")
    print(f"manifest: {Path(result['session_dir']) / 'manifest.json'}")
    return 0


def cmd_coverage(args: argparse.Namespace) -> int:
    set_model_caches()
    from .coverage.run import run_coverage

    result = run_coverage(
        _captures(args),
        args.room,
        session=args.session,
        max_views=args.max_views,
        chunk_size=args.chunk_size,
        skip_detection=args.skip_detection,
        reuse_poses=not args.recompute,
    )
    print(f"report: {result['report']}")
    print(f"map:    {result['map']}")
    return 0


def cmd_sessions(args: argparse.Namespace) -> int:
    import json

    from .paths import room_root

    index = room_root(_captures(args), args.room) / "sessions" / "index.json"
    if not index.is_file():
        print("no sessions")
        return 1
    print(json.dumps(json.loads(index.read_bytes()), indent=2))
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="roomscan", description=__doc__)
    parser.add_argument("--captures-root", help="Folder for derived data (default: <repo>/captures)")
    sub = parser.add_subparsers(dest="command", required=True)

    p = sub.add_parser("ingest", help="C1: convert, read EXIF, dedupe and score a folder of photos")
    p.add_argument("--source", required=True, help="Folder of original photos; read in place, never written")
    p.add_argument("--room", required=True, help="Room token, for example 'garage'")
    p.add_argument("--workers", type=int, default=None, help="Parallel workers (default: up to 6)")
    p.set_defaults(func=cmd_ingest)

    p = sub.add_parser("coverage", help="C2: poses, view graph, coverage map and capture guidance")
    p.add_argument("--room", required=True)
    p.add_argument("--session", default="latest", help="Session id from ingest (default: latest)")
    p.add_argument("--max-views", type=int, default=None, help="Use at most this many photos (testing)")
    p.add_argument("--chunk-size", type=int, default=None, help="Photos per GPU batch (default: from free VRAM)")
    p.add_argument("--skip-detection", action="store_true", help="No object detection (no per-object counts)")
    p.add_argument("--recompute", action="store_true", help="Recompute poses even if cached for this session")
    p.set_defaults(func=cmd_coverage)

    p = sub.add_parser("sessions", help="List ingested sessions for a room")
    p.add_argument("--room", required=True)
    p.set_defaults(func=cmd_sessions)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
