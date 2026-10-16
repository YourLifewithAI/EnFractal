"""The twenty-minute fixture: it is what the builder writes, it is valid under the contract, and it holds what the tests lean on."""
from __future__ import annotations

import json
import os
import subprocess
import sys
import unittest

import support
import build_fixture


def contracts_python():
    """An interpreter that can run contracts/validate.py (it needs jsonschema and referencing): this one, or the contracts'
    environment at the conventional place (.cache/contracts-venv, or .venv)."""
    where = ("Scripts", "python.exe") if os.name == "nt" else ("bin", "python")  # this platform's own interpreters only
    candidates = [sys.executable] + [str(p) for p in (support.REPO / ".cache" / "contracts-venv" / where[0] / where[1],
                                                       support.REPO / ".venv" / where[0] / where[1]) if p.is_file()]
    for python in candidates:
        probe = subprocess.run([python, "-I", "-c", "import jsonschema, referencing"], capture_output=True, timeout=60)
        if probe.returncode == 0:
            return python
    return None


class FixtureTests(unittest.TestCase):
    def test_the_committed_fixture_is_what_the_builder_writes(self):
        for name, data in build_fixture.fixture_files().items():
            self.assertEqual((support.FIXTURE_PLAY / name).read_bytes(), data, f"{name} is stale: run tools/playtest/tests/build_fixture.py")

    def test_the_fixture_is_valid_under_the_contract(self):
        python = contracts_python()
        if python is None:
            self.skipTest("no interpreter that can run contracts/validate.py (jsonschema and referencing: the contracts environment, .cache/contracts-venv)")
        done = subprocess.run([python, "-I", str(support.REPO / "contracts" / "validate.py"), str(support.FIXTURE_PLAY)],
                              capture_output=True, text=True, timeout=120)
        self.assertEqual(done.returncode, 0, done.stdout + done.stderr)
        self.assertIn("0 problem(s)", done.stdout)

    def test_it_is_twenty_minutes_with_what_the_reader_is_tested_on(self):
        manifest = json.loads((support.FIXTURE_PLAY / "manifest.json").read_text(encoding="utf-8"))
        events = support.read_events(support.FIXTURE_PLAY)
        self.assertAlmostEqual(manifest["played_s"], 1204.3)
        self.assertEqual(manifest["counts"], {"events": len(events), "stills": 193, "marks": 5})
        kinds = {e["kind"] for e in events}
        self.assertEqual(kinds, set(report_kinds()))
        self.assertEqual({e["principal"] for e in events if e["kind"] == "command"}, {"player:local", "companion:local"})

    def test_no_picture_is_committed(self):
        pictures = [p for p in support.TOOL.rglob("*") if p.suffix.lower() in (".jpg", ".jpeg", ".png")]
        self.assertEqual(pictures, [], "stills are never in Git; the tests make theirs at run time")
        self.assertFalse((support.FIXTURE_PLAY / "stills").exists())


def report_kinds():
    return support.report.KINDS


if __name__ == "__main__":
    unittest.main()
