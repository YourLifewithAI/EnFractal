"""Stand-in for the running game: the mock host behind a real companion link, plus a player console.

    enfractal-companion-mock-game [--session-file PATH] [--room DIR]

It listens on loopback, writes the session file the MCP server reads, and prints every held
command. Type at the console, as the player:

    list                 show commands waiting for approval
    approve <request>    approve one (a prefix of the request id is enough)
    deny <request>       deny one
    say <entity> <text>  put a sign or label with that text on an entity (untrusted world text)
    quit                 stop the game

The console is the player's UI. Nothing typed into the MCP client can reach it.
"""
from __future__ import annotations

import argparse
import asyncio
import logging
import sys
import threading
from pathlib import Path

from .contract import DEFAULT_REPO_ROOT, Contracts
from .link import LinkServer
from .mock_host import MockHost

DEFAULT_SESSION = DEFAULT_REPO_ROOT / "companion" / ".cache" / "session.json"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="enfractal-companion-mock-game", description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--session-file", type=Path, default=DEFAULT_SESSION)
    parser.add_argument("--room", type=Path, help="room directory (default: game/rooms/test_room)")
    parser.add_argument("--contracts-dir", type=Path)
    options = parser.parse_args(argv)
    logging.basicConfig(stream=sys.stderr, level=logging.WARNING)
    host = MockHost(Contracts(options.contracts_dir), room_dir=options.room)
    try:
        asyncio.run(_run(host, options.session_file.resolve()))
    except KeyboardInterrupt:
        pass
    return 0


async def _run(host: MockHost, session_path: Path) -> None:
    server = LinkServer(host.handle, host.room_id)
    info = await server.start(session_path)
    print(f"mock game: room {host.room_id} on {info.host}:{info.port}", flush=True)
    print(f"session file: {session_path}", flush=True)
    print("commands: list | approve <id> | deny <id> | say <entity> <text> | quit", flush=True)
    loop = asyncio.get_running_loop()
    host.listeners.append(lambda kind, payload: loop.call_soon_threadsafe(_announce, kind, payload))
    lines: asyncio.Queue[str | None] = asyncio.Queue()

    def read_console() -> None:
        for line in sys.stdin:
            loop.call_soon_threadsafe(lines.put_nowait, line.strip())
        loop.call_soon_threadsafe(lines.put_nowait, None)

    threading.Thread(target=read_console, daemon=True).start()
    try:
        while True:
            line = await lines.get()
            if line is None or line == "quit":
                break
            _console(host, line)
    finally:
        await server.close()
        try:
            session_path.unlink()
        except OSError:
            pass


def _announce(kind: str, payload: dict) -> None:
    if kind == "approval_requested":
        print(f"[held] {payload['request_id']} {payload['op']}: {payload['reason']}", flush=True)
    elif kind == "approval_decided":
        print(f"[decided] {payload['request_id']} -> {payload['state']}", flush=True)
    elif kind == "committed":
        print(f"[committed] {payload['principal']} {payload['op']} {payload['action_id']}", flush=True)


def _console(host: MockHost, line: str) -> None:
    parts = line.split(" ", 2)
    if not parts or not parts[0]:
        return
    word = parts[0]
    if word == "list":
        pending = host.pending_approvals()
        for item in pending:
            print(f"  {item['request_id']} {item['op']} until {item['expires_utc']}: {item['reason']}", flush=True)
        if not pending:
            print("  nothing is waiting", flush=True)
    elif word in ("approve", "deny") and len(parts) >= 2:
        matches = [a["request_id"] for a in host.pending_approvals() if a["request_id"].startswith(parts[1])]
        if len(matches) != 1:
            print("  no single waiting request matches that id", flush=True)
            return
        outcome = host.player_decide(matches[0], approve=word == "approve")
        print(f"  {matches[0]} {outcome['state']}", flush=True)
    elif word == "say" and len(parts) == 3:
        if parts[1] not in host.entities:
            print("  no such entity", flush=True)
            return
        host.add_world_text(parts[1], parts[2])
        print(f"  {parts[1]} now shows that text", flush=True)
    else:
        print("  commands: list | approve <id> | deny <id> | say <entity> <text> | quit", flush=True)


if __name__ == "__main__":
    sys.exit(main())
