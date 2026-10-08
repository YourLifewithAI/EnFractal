"""The play-only client profile: the one MCP server a play session needs, and nothing else.

Players bring their own AI: any MCP client or agent harness. The client session used for play
should hold this server only, with no shell, file, browser, web or other MCP tools, so that a sign
in the room that talks a model into "doing something" has nothing to do it with except game
commands, which the game checks. The game cannot see or limit what else a client loads; this
profile is the configuration that makes it true, written in the widely used `mcpServers` JSON shape
(one entry: a command line for a stdio server).

    python -m enfractal_companion.profile                       # the running game's session file
    python -m enfractal_companion.profile --mock                # the built-in mock game
    python -m enfractal_companion.profile --session-file PATH   # another session file

prints the profile as JSON. The command is the interpreter running this module, so run it with the
companion's own environment. The profile sets no environment variables: the server needs no keys
or tokens, and it clears whatever environment it inherits before it serves anything.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

SERVER_KEY = "enfractal-companion"


def play_only_profile(*, mock: bool = False, session_file: Path | None = None, python: str | None = None,
                      schema_profile: str = "full") -> dict:
    """The profile as a dict: exactly one stdio server, this package, no environment."""
    if mock and session_file is not None:
        raise ValueError("choose --mock or --session-file, not both")
    args = ["-m", "enfractal_companion"]
    if mock:
        args.append("--mock")
    elif session_file is not None:
        args += ["--session-file", str(Path(session_file).resolve())]
    if schema_profile != "full":
        args += ["--schema-profile", schema_profile]
    # absolute(), not resolve(): a POSIX virtual environment's python is a symlink, and following it
    # would launch the base interpreter without the companion's packages.
    command = str(Path(python or sys.executable).absolute())
    return {"mcpServers": {SERVER_KEY: {"type": "stdio", "command": command, "args": args}}}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="python -m enfractal_companion.profile",
                                     description="Print the play-only MCP client profile for the EnFractal companion.")
    source = parser.add_mutually_exclusive_group()
    source.add_argument("--mock", action="store_true", help="serve the built-in mock game (no game needed)")
    source.add_argument("--session-file", type=Path, help="the game's companion session file (default: the game's own)")
    parser.add_argument("--schema-profile", default="full", choices=["full", "minimal"],
                        help="minimal: tool schemas with only the most widely supported JSON Schema keywords")
    options = parser.parse_args(argv)
    profile = play_only_profile(mock=options.mock, session_file=options.session_file,
                                schema_profile=options.schema_profile)
    sys.stdout.write(json.dumps(profile, indent=2) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
