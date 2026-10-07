"""Check that a codex/ branch changed only the files its brief allows.

Usage: python tools/codex/check_scope.py codex/<brief> [--base origin/run1/integration]

The brief is docs/codex/briefs/<brief>.md; its ```scope fenced block lists globs (fnmatch, where * also
matches /). Exits 1 and lists the offending paths if the branch touched anything else.
"""
from __future__ import annotations

import argparse
import fnmatch
import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]


def git(*args: str) -> str:
    return subprocess.run(["git", *args], cwd=REPO, check=True, capture_output=True, text=True).stdout


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("branch")
    parser.add_argument("--base", default="origin/run1/integration")
    args = parser.parse_args()
    name = args.branch.removeprefix("origin/").removeprefix("codex/")
    brief = REPO / "docs" / "codex" / "briefs" / f"{name}.md"
    if not brief.is_file():
        print(f"No brief at {brief.relative_to(REPO)}", file=sys.stderr)
        return 2
    block = re.search(r"```scope\n(.*?)```", brief.read_text(encoding="utf-8").replace("\r\n", "\n"), re.S)
    globs = [line.strip() for line in (block.group(1).splitlines() if block else []) if line.strip()]
    ref = args.branch if args.branch.startswith("origin/") else f"origin/{args.branch}"
    changed = git("diff", "--name-only", f"{git('merge-base', args.base, ref).strip()}..{ref}").split()
    outside = [path for path in changed if not any(fnmatch.fnmatch(path, glob) for glob in globs)]
    print(f"{ref}: {len(changed)} file(s) changed; scope {globs or '(none: report-only brief)'}")
    for path in outside:
        print(f"  OUTSIDE SCOPE: {path}")
    return 1 if outside else 0


if __name__ == "__main__":
    sys.exit(main())
