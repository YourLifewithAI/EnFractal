"""CLI for the first EnFractal geographic package."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from .build import build_map
from .fetch import fetch_all
from .geo import MapConfig
from .verify import verify_package


DEFAULT_CONFIG = Path("maps/barton_creek/config.json")
DEFAULT_SOURCES = Path("maps/barton_creek/sources")
DEFAULT_PACKAGE = Path("game/maps/barton_creek")


def main() -> None:
    parser = argparse.ArgumentParser(description="Build EnFractal's Barton Creek geographic package")
    parser.add_argument("action", choices=("fetch", "build", "verify"))
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--sources", type=Path, default=DEFAULT_SOURCES)
    parser.add_argument("--package", type=Path, default=DEFAULT_PACKAGE)
    parser.add_argument("--photo-evidence", type=Path, default=None,
                        help="curated photo evidence directory (defaults beside --sources)")
    args = parser.parse_args()
    config = MapConfig.load(args.config)
    photo_evidence = args.photo_evidence or args.sources.parent / "photo_pilot"
    if args.action == "fetch":
        if args.sources.exists() and any(args.sources.iterdir()):
            parser.error("Source directory is not empty. Use a new --sources path to preserve the existing snapshot.")
        result = fetch_all(config, args.sources)
    elif args.action == "build":
        result = build_map(config, args.sources, args.package, photo_evidence)
    else:
        result = verify_package(config, args.sources, args.package, photo_evidence)
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
