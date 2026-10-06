"""Shared test helpers. Results are checked with the integrator's own validator, imported by path."""
from __future__ import annotations

import asyncio
import copy
import json
import logging
import os
import sys
import tempfile
import threading
from pathlib import Path

COMPANION_DIR = Path(__file__).resolve().parents[1]
REPO = COMPANION_DIR.parent
# ENFRACTAL_CONTRACTS_DIR points the suite at another copy of contracts/, to check a proposed change.
CONTRACTS = Path(os.environ.get("ENFRACTAL_CONTRACTS_DIR") or REPO / "contracts").resolve()
EXAMPLES = CONTRACTS / "examples" / "messages"
SRC = COMPANION_DIR / "src"

for path in (str(SRC), str(CONTRACTS)):
    if path not in sys.path:
        sys.path.insert(0, path)

import validate as contract_validate  # noqa: E402  (contracts/validate.py, the integrator's validator)

from enfractal_companion.contract import Contracts  # noqa: E402
from enfractal_companion.link import LinkServer  # noqa: E402
from enfractal_companion.mock_host import COMPANION, NO_HOLDS, PLAYER, FakeClock, HostPolicy, MockHost  # noqa: E402

_CONTRACTS: Contracts | None = None
contract_validate.validator_for("enfractal.result")  # warm the validator once, off any event loop
logging.getLogger("enfractal").setLevel(logging.CRITICAL)  # refusals are asserted, not logged


def contracts() -> Contracts:
    global _CONTRACTS
    if _CONTRACTS is None:
        _CONTRACTS = Contracts(CONTRACTS)
    return _CONTRACTS


def contract_problems(document) -> list[str]:
    """Problems according to contracts/validate.py, independent of the companion package."""
    return contract_validate.schema_errors(document)


class RecordingHost(MockHost):
    """A mock host that keeps every result it emits, so tests can prove all of them are contract-valid."""

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.emitted: list[dict] = []

    def handle(self, principal, message):
        result = super().handle(principal, message)
        self.emitted.append(copy.deepcopy(result))
        return result


def new_host(policy: HostPolicy | None = None, clock: FakeClock | None = None, **kwargs) -> RecordingHost:
    return RecordingHost(contracts(), policy=policy, clock=clock or FakeClock(), **kwargs)


def command(op: str, args: dict, action_id: str, room_id: str = "test_room", **extra) -> dict:
    message = {"schema": "enfractal.command", "version": 1, "action_id": action_id, "room_id": room_id,
               "op": op, "args": args}
    message.update(extra)
    return message


def query(op: str, args: dict, query_id: str = "q-1", room_id: str = "test_room") -> dict:
    return {"schema": "enfractal.query", "version": 1, "query_id": query_id, "room_id": room_id, "op": op, "args": args}


def example(name: str, kind: str = "valid") -> dict:
    return json.loads((EXAMPLES / kind / f"{name}.json").read_bytes())


def retarget(message: dict, mapping: dict[str, str]) -> dict:
    """Point a garage example at the test room: room id and entity ids."""
    text = json.dumps(message)
    for old, new in mapping.items():
        text = text.replace(old, new)
    return json.loads(text)


GARAGE_TO_TEST_ROOM = {"garage_example": "test_room", "obj:paint_clutter": "obj:box", "obj:bean_bag": "obj:box",
                       "obj:shelving_right": "obj:table", "obj:shelving_left": "obj:table"}


class ThreadedGame:
    """The mock game in its own thread and event loop, behind a real loopback link and session file."""

    def __init__(self, host: MockHost, session_dir: Path | None = None):
        self.host = host
        self._tmp = None
        if session_dir is None:
            self._tmp = tempfile.TemporaryDirectory(prefix="enfractal-companion-")
            session_dir = Path(self._tmp.name)
        self.session_path = Path(session_dir) / "session.json"
        self.loop = asyncio.new_event_loop()
        self.server: LinkServer | None = None
        self._thread = threading.Thread(target=self.loop.run_forever, daemon=True)

    def __enter__(self) -> "ThreadedGame":
        self._thread.start()
        self.server = LinkServer(self.host.handle, self.host.room_id)
        asyncio.run_coroutine_threadsafe(self.server.start(self.session_path), self.loop).result(10)
        return self

    def __exit__(self, *exc) -> None:
        asyncio.run_coroutine_threadsafe(self.server.close(), self.loop).result(10)
        self.loop.call_soon_threadsafe(self.loop.stop)
        self._thread.join(10)
        self.loop.close()
        if self._tmp is not None:
            self._tmp.cleanup()
