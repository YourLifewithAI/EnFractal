"""Setup failure guards use mocks: no downloads, installs or live DB mutations."""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("enfractal_setup", Path(__file__).resolve().parents[1] / "setup.py")
setup = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(setup)


@unittest.skipUnless(os.name == "nt", "Optional portable setup is Windows-only")
class SetupGuards(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="enfractal_setup_guard_")
        self.addCleanup(self.temporary.cleanup)
        self.repo = Path(self.temporary.name)
        self.cache = self.repo / ".cache"
        self.cache.mkdir()
        self.script = self.repo / "services/save_travel/setup.py"
        self.calls = []

    def invoke(self, runner):
        with patch.object(setup, "__file__", str(self.script)), patch.object(setup.sys, "argv", [str(self.script)]), patch.object(setup, "run", side_effect=runner), patch.object(setup.urllib.request, "urlopen", side_effect=AssertionError("No download expected")), patch.object(setup.socket, "socket") as socket_factory, contextlib.redirect_stdout(io.StringIO()):
            socket_factory.return_value.__enter__.return_value.connect_ex.return_value = 1
            setup.main()

    def binaries(self):
        binaries = self.cache / "postgresql/pgsql/bin"
        binaries.mkdir(parents=True)
        (binaries / "initdb.exe").write_bytes(b"mock")
        return binaries

    def test_existing_configuration_repairs_missing_python_without_touching_db(self):
        config = self.cache / "save-travel-config.json"
        original = '{"database_url":"retained-private-dsn","token":"retained"}\n'
        config.write_text(original, encoding="utf-8")
        self.invoke(lambda args, **kwargs: self.calls.append([str(a) for a in args]))
        self.assertEqual(config.read_text(encoding="utf-8"), original)
        self.assertEqual(len(self.calls), 2)
        self.assertIn("venv", self.calls[0])
        self.assertIn("pip", self.calls[1])
        self.assertFalse((self.cache / "postgresql").exists())
        self.assertFalse((self.cache / "save-travel-config.pending.json").exists())

    def test_dependency_repair_failure_preserves_existing_configuration(self):
        config = self.cache / "save-travel-config.json"
        config.write_bytes(b"existing configuration bytes")
        with self.assertRaisesRegex(RuntimeError, "simulated dependency failure"):
            self.invoke(lambda *args, **kwargs: (_ for _ in ()).throw(RuntimeError("simulated dependency failure")))
        self.assertEqual(config.read_bytes(), b"existing configuration bytes")
        self.assertFalse((self.cache / "postgresql").exists())

    def test_initialization_failure_retains_matching_credentials_before_data_exists(self):
        self.binaries()
        pending = self.cache / "save-travel-config.pending.json"
        observed = {}
        def runner(args, **kwargs):
            if Path(str(args[0])).name == "initdb.exe":
                self.assertTrue(pending.exists())
                saved = json.loads(pending.read_text(encoding="utf-8"))
                password_path = Path(next(str(a).split("=", 1)[1] for a in args if str(a).startswith("--pwfile=")))
                password = password_path.read_text(encoding="utf-8")
                self.assertIn(":" + password + "@127.0.0.1:55432/", saved["database_url"])
                self.assertEqual(saved["postgres_data"], str(self.cache / "postgresql/data"))
                observed["pending"] = pending.read_bytes()
                observed["password_path"] = password_path
                raise RuntimeError("simulated initdb interruption")
        with self.assertRaisesRegex(RuntimeError, "simulated initdb interruption"):
            self.invoke(runner)
        self.assertEqual(pending.read_bytes(), observed["pending"])
        self.assertFalse(observed["password_path"].exists())
        self.assertFalse((self.cache / "save-travel-config.json").exists())

    def test_existing_pending_credentials_are_never_replaced(self):
        pending = self.cache / "save-travel-config.pending.json"
        pending.write_bytes(b"matching recovery credentials")
        with self.assertRaisesRegex(RuntimeError, "Interrupted setup credentials already exist"):
            self.invoke(lambda args, **kwargs: self.calls.append([str(a) for a in args]))
        self.assertEqual(pending.read_bytes(), b"matching recovery credentials")
        self.assertFalse((self.cache / "postgresql").exists())
        self.assertTrue(all("initdb.exe" not in " ".join(c) for c in self.calls))

    def test_existing_data_refused_before_any_new_credentials_are_written(self):
        self.binaries()
        data = self.cache / "postgresql/data"
        data.mkdir()
        marker = data / "existing-data"
        marker.write_bytes(b"preserve")
        with self.assertRaisesRegex(RuntimeError, "data directory already exists"):
            self.invoke(lambda args, **kwargs: self.calls.append([str(a) for a in args]))
        self.assertEqual(marker.read_bytes(), b"preserve")
        self.assertFalse((self.cache / "save-travel-config.pending.json").exists())


if __name__ == "__main__":
    unittest.main()
