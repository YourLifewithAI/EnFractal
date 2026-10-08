"""Command line: ``roomscan ingest`` (C1), ``coverage`` (C2), ``shell`` (C3) and ``inventory`` (C4)."""

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
        measurements=Path(args.measurements) if args.measurements else None,
        use_measurements=not args.no_measurements,
    )
    print(f"report: {result['report']}")
    print(f"map:    {result['map']}")
    return 0


def cmd_shell(args: argparse.Namespace) -> int:
    from .shell.export import export_shell

    result = export_shell(
        _captures(args), args.room, session=args.session,
        rooms_dir=Path(args.rooms_dir) if args.rooms_dir else None, created_utc=args.created_utc,
        spec_path=Path(args.spec) if args.spec else None, pictures=not args.no_pictures)
    if result["published"]:
        print(f"manifest: {result['manifest']}")
    else:
        kept = f"; the earlier room at {result['manifest']} is untouched" if result["manifest"] else ""
        print(f"NOT published{kept}")
    for problem in result["problems"]:
        print(f"PROBLEM: {problem}")
    return 0 if result["published"] else 1


def cmd_inventory(args: argparse.Namespace) -> int:
    set_model_caches()
    from .inventory.build import build_inventory
    from .inventory.review import render_review
    from .shell.planes import ShellPlan
    from .shell.spec import SPEC_FILE, load_spec

    result = build_inventory(
        _captures(args), args.room, session=args.session,
        curation_path=Path(args.curation) if args.curation else None, min_evidence=args.min_evidence)
    scene, inventory = result["scene"], result["inventory"]
    x0, x1, z0, z1 = scene.bounds
    spec_file = scene.room_dir / SPEC_FILE
    spec = load_spec(spec_file) if spec_file.is_file() else None
    plan = ShellPlan(x0, x1, z0, z1, scene.height_m, [], scene.factor, scene.scale_source)
    image = render_review(scene, inventory, [p["id"] for p in inventory["picks"]], scene.room_dir / "inventory-review.jpg",
                          plan=plan if spec else None, spec=spec)
    print(f"inventory: {result['path']}")
    print(f"review image: {image}")
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
    p.add_argument("--measurements", help="Tape measurements (default: captures/<room>/measurements.json if it exists)")
    p.add_argument("--no-measurements", action="store_true", help="Ignore tape measurements: keep the model's own scale")
    p.set_defaults(func=cmd_coverage)

    p = sub.add_parser("shell", help="C3: the room's shell as a room manifest in the player's user data")
    p.add_argument("--room", required=True)
    p.add_argument("--session", default="latest")
    p.add_argument("--spec", help="Shell spec (default: captures/<room>/shell-spec.json)")
    p.add_argument("--rooms-dir", help="Where captured rooms go (default: the game's user://rooms; never inside the repository)")
    p.add_argument("--created-utc", help="Timestamp for the manifest (default: now); fix it to rebuild the same bytes")
    p.add_argument("--no-pictures", action="store_true", help="Skip the rectified wall pictures")
    p.set_defaults(func=cmd_shell)

    p = sub.add_parser("inventory", help="C4: the room's objects (kind, box, colours), the five picks and a top-down review image")
    p.add_argument("--room", required=True)
    p.add_argument("--session", default="latest")
    p.add_argument("--curation", help="Reviewer's corrections (default: captures/<room>/inventory-curation.json)")
    p.add_argument("--min-evidence", type=float, default=1.8, help="Smallest photos-times-score a detector cluster needs")
    p.set_defaults(func=cmd_inventory)

    p = sub.add_parser("sessions", help="List ingested sessions for a room")
    p.add_argument("--room", required=True)
    p.set_defaults(func=cmd_sessions)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
