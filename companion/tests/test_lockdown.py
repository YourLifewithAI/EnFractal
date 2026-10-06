"""The server process refuses everything outside the game: files, processes, network, credentials.

Each probe runs in a fresh Python process that installs the same lockdown the MCP server installs,
then tries one thing a hijacked server (or a dependency tricked by input) might try.
"""
from __future__ import annotations

import json
import os
import socket
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

from support import SRC

PROBE = textwrap.dedent(r'''
    import asyncio, ctypes, json, os, socket, subprocess, sys, urllib.request
    from pathlib import Path
    sys.path.insert(0, sys.argv[1])
    from enfractal_companion import lockdown
    allowed, outside, port = Path(sys.argv[2]), Path(sys.argv[3]), int(sys.argv[4])
    # Before the lockdown: this machine's own non-loopback address and a closed port on it. Connecting
    # there never leaves the machine (the local stack refuses it), so a let-through shows as a refusal.
    udp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        udp.connect(("192.0.2.1", 9))  # picks the outbound interface; sends nothing
        lan_ip = udp.getsockname()[0]
    except OSError:
        lan_ip = "192.0.2.1"
    udp.close()
    if lan_ip.startswith("127."):
        lan_ip = "192.0.2.1"
    # A child of this probe that outlives it by a few seconds on its own; nothing may end it early.
    child = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(8)"],
                             stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    removed = lockdown.scrub_environment()
    lockdown.install(read_files=[allowed], read_roots=[Path(sys.argv[1])])
    report = {"env": sorted(os.environ), "removed_secret": "ENFRACTAL_CANARY_API_KEY" in removed}

    def attempt(name, fn):
        try:
            fn()
            report[name] = "allowed"
        except PermissionError as error:
            report[name] = "blocked" if "sandbox" in str(error) else f"other: {error}"
        except Exception as error:
            report[name] = f"error: {type(error).__name__}"

    attempt("read_session_file", lambda: allowed.read_bytes())
    attempt("read_outside_file", lambda: outside.read_bytes())
    attempt("list_directory", lambda: os.listdir(outside.parent))
    attempt("write_new_file", lambda: open(outside.with_name("written.txt"), "w").write("x"))
    attempt("append_existing_file", lambda: open(outside, "a").write("x"))
    attempt("delete_file", lambda: os.remove(outside))
    attempt("rename_file", lambda: os.rename(outside, outside.with_name("renamed.txt")))
    attempt("make_directory", lambda: os.mkdir(outside.with_name("newdir")))
    attempt("subprocess", lambda: subprocess.run([sys.executable, "-c", "print(1)"], check=False))
    attempt("os_system", lambda: os.system("echo hi"))
    attempt("set_environment", lambda: os.environ.__setitem__("X", "1"))
    attempt("dns_lookup", lambda: socket.getaddrinfo("example.com", 443))
    attempt("connect_public_ip", lambda: socket.create_connection(("192.0.2.1", 443), timeout=1))
    attempt("fetch_url", lambda: urllib.request.urlopen("http://192.0.2.1/", timeout=1))
    attempt("connect_loopback_game", lambda: socket.create_connection(("127.0.0.1", port), timeout=2).close())
    async def async_connect():
        reader, writer = await asyncio.wait_for(asyncio.open_connection(lan_ip, 9), 2)
        writer.close()
    attempt("event_loop_connect_off_loopback", lambda: asyncio.run(async_connect()))
    if os.name == "nt":
        import _winapi, winreg
        attempt("registry", lambda: winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Software"))
        attempt("winapi_create_file", lambda: _winapi.CreateFile(str(outside.with_name("winapi.txt")), 0x40000000, 0, 0, 2, 0, 0))
        attempt("winapi_terminate_process", lambda: _winapi.TerminateProcess(_winapi.OpenProcess(1, False, child.pid), 9))
        attempt("ctypes_load_library", lambda: ctypes.WinDLL("kernel32"))
    else:
        attempt("ctypes_load_library", lambda: ctypes.CDLL("libc.so.6"))
    report["child_alive"] = child.poll() is None
    print(json.dumps(report))
''')


class Lockdown(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory(prefix="enfractal-lockdown-")
        base = Path(cls.tmp.name)
        cls.session = base / "session.json"
        cls.session.write_bytes(b'{"ok": true}\n')
        cls.outside = base / "outside" / "private.txt"
        cls.outside.parent.mkdir()
        cls.outside.write_bytes(b"not for the companion\n")
        listener = socket.socket()
        listener.bind(("127.0.0.1", 0))
        listener.listen(1)
        env = {k: v for k, v in os.environ.items()}
        env["ENFRACTAL_CANARY_API_KEY"] = "sk-canary-0000"
        try:
            completed = subprocess.run(
                [sys.executable, "-c", PROBE, str(SRC), str(cls.session), str(cls.outside),
                 str(listener.getsockname()[1])],
                capture_output=True, text=True, timeout=60, env=env)
        finally:
            listener.close()
        if completed.returncode != 0:
            raise AssertionError(completed.stderr)
        cls.report = json.loads(completed.stdout.strip().splitlines()[-1])

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def assertBlocked(self, name):
        self.assertEqual(self.report[name], "blocked", self.report)

    def test_reads_only_the_session_file(self):
        self.assertEqual(self.report["read_session_file"], "allowed")
        self.assertBlocked("read_outside_file")
        self.assertBlocked("list_directory")

    def test_refuses_writing_deleting_or_renaming_files(self):
        for name in ("write_new_file", "append_existing_file", "delete_file", "rename_file", "make_directory"):
            with self.subTest(name=name):
                self.assertBlocked(name)
        self.assertTrue(self.outside.exists())
        self.assertEqual(self.outside.read_bytes(), b"not for the companion\n")
        self.assertEqual(sorted(p.name for p in self.outside.parent.iterdir()), ["private.txt"])

    def test_refuses_starting_processes(self):
        self.assertBlocked("subprocess")
        self.assertBlocked("os_system")

    def test_refuses_network_beyond_loopback(self):
        self.assertBlocked("dns_lookup")
        self.assertBlocked("connect_public_ip")
        self.assertBlocked("fetch_url")
        self.assertEqual(self.report["connect_loopback_game"], "allowed")

    def test_holds_no_inherited_credentials(self):
        self.assertTrue(self.report["removed_secret"])
        self.assertLessEqual(set(self.report["env"]), {"SYSTEMROOT", "SystemRoot", "WINDIR", "windir"})
        self.assertBlocked("set_environment")

    @unittest.skipUnless(os.name == "nt", "the registry exists on Windows only")
    def test_refuses_the_registry(self):
        self.assertBlocked("registry")

    def test_refuses_event_loop_connects_beyond_loopback(self):
        self.assertBlocked("event_loop_connect_off_loopback")

    @unittest.skipUnless(os.name == "nt", "_winapi exists on Windows only")
    def test_refuses_winapi_files_and_processes(self):
        self.assertBlocked("winapi_create_file")
        self.assertBlocked("winapi_terminate_process")
        self.assertTrue(self.report["child_alive"])
        self.assertFalse(self.outside.with_name("winapi.txt").exists())

    def test_refuses_loading_native_libraries_through_ctypes(self):
        self.assertBlocked("ctypes_load_library")


if __name__ == "__main__":
    unittest.main()
