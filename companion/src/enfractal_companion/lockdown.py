"""Process lockdown for the companion MCP server: nothing outside the game, enforced, not just omitted.

Having no file, shell or URL tools is the first boundary. This is the second: once the server has
loaded the contracts and knows where the session file is, it

1. clears its own environment down to what Windows sockets need, so no inherited developer
   secret (API keys, tokens, cloud credentials) exists in the process to leak, and
2. installs a Python audit hook (PEP 578) that refuses, for the rest of the process's life,
   - writing, creating, deleting or renaming any file,
   - reading any file except the session file and the Python installation's own modules,
   - starting processes (subprocess, os.system, exec, spawn, startfile),
   - network connections, binds and name lookups to anything but loopback,
   - URL fetching, the browser, the registry and sqlite.

Audit hooks cannot be removed once installed. This is defence in depth inside one Python process,
not an OS sandbox: native code could bypass it. The MCP client should still launch the server
under the player's normal account with no extra privileges (docs/companion/SECURITY.md).
"""
from __future__ import annotations

import ipaddress
import os
import sys
from pathlib import Path

# What Windows needs for Winsock and the event loop; nothing else survives the scrub.
KEEP_ENV = ("SYSTEMROOT", "SystemRoot", "WINDIR", "windir")

_DENIED_EVENTS = (
    "subprocess.Popen", "os.system", "os.exec", "os.posix_spawn", "os.spawn", "os.startfile", "os.fork",
    "os.forkpty", "pty.spawn", "os.kill", "os.killpg", "signal.pthread_kill",
    "os.remove", "os.unlink", "os.rename", "os.replace", "os.rmdir", "os.mkdir", "os.makedirs", "os.chmod",
    "os.chown", "os.truncate", "os.link", "os.symlink", "os.utime", "os.chdir", "os.chflags", "os.lchflags",
    "os.lchmod", "os.lchown", "os.mkfifo", "os.mknod", "os.removexattr", "os.setxattr",
    "shutil.copyfile", "shutil.copymode", "shutil.copystat", "shutil.copytree", "shutil.move", "shutil.rmtree",
    "shutil.make_archive", "shutil.unpack_archive", "tempfile.mkstemp", "tempfile.mkdtemp",
    "urllib.Request", "webbrowser.open", "sqlite3.connect", "sqlite3.connect/handle",
    "ftplib.connect", "smtplib.connect", "imaplib.open", "poplib.connect", "nntplib.connect",
    "telnetlib.Telnet.open", "os.putenv", "os.unsetenv",
)
_DENIED_PREFIXES = ("winreg.", "_winapi.CreateProcess", "_winapi.CreateNamedPipe")
_NETWORK_HOST_EVENTS = ("socket.connect", "socket.bind", "socket.sendto", "socket.sendmsg")
_LOOKUP_EVENTS = ("socket.getaddrinfo", "socket.gethostbyname", "socket.gethostbyname_ex", "socket.gethostbyaddr",
                  "socket.getnameinfo")
_LIST_EVENTS = ("os.listdir", "os.scandir", "glob.glob", "glob.glob/2")
_WRITE_FLAGS = os.O_WRONLY | os.O_RDWR | os.O_APPEND | os.O_CREAT | os.O_TRUNC

_installed = False


class SandboxViolation(PermissionError):
    pass


def scrub_environment() -> list[str]:
    """Remove every environment variable except the few Windows sockets need. Returns removed names."""
    removed = [key for key in list(os.environ) if key not in KEEP_ENV]
    for key in removed:
        del os.environ[key]
    return removed


def _norm(path) -> str | None:
    if isinstance(path, int):
        return None
    try:
        text = os.fsdecode(path)
    except TypeError:
        return ""
    return os.path.normcase(os.path.abspath(text))


def _under(path: str, roots: tuple[str, ...]) -> bool:
    return any(path == root or path.startswith(root.rstrip("\\/") + os.sep) for root in roots)


def _host_is_loopback(host) -> bool:
    if host is None:
        return True
    if isinstance(host, bytes):
        host = host.decode("ascii", "replace")
    if not isinstance(host, str):
        return False
    if host in ("localhost", ""):
        return host == "localhost"
    try:
        return ipaddress.ip_address(host.split("%", 1)[0]).is_loopback
    except ValueError:
        return False


def _address_is_loopback(address) -> bool:
    if isinstance(address, tuple) and address:
        return _host_is_loopback(address[0])
    return False  # AF_UNIX paths and anything else are refused


def install(read_files: list[Path] = (), read_roots: list[Path] = ()) -> None:
    """Install the audit hook. Idempotent per process; cannot be undone."""
    global _installed
    if _installed:
        return
    # The Python installation and its site-packages (where the stdlib and every dependency live),
    # plus the roots the caller names (the package and the contracts). Deliberately not the
    # current directory, which is wherever the MCP client happened to start the server.
    roots = {sys.prefix, sys.base_prefix, sys.exec_prefix, sys.base_exec_prefix}
    roots.update(str(r) for r in read_roots)
    allowed_roots = tuple(sorted({os.path.normcase(os.path.abspath(r)) for r in roots}))
    allowed_files = frozenset(os.path.normcase(os.path.abspath(str(f))) for f in read_files)
    null_device = os.path.normcase(os.path.abspath(os.devnull))

    def deny(event: str) -> None:
        raise SandboxViolation(f"blocked by the EnFractal companion sandbox: {event}")

    def hook(event: str, args: tuple) -> None:
        if event == "open":
            path, mode, flags = (tuple(args) + (None, None, None))[:3]
            target = _norm(path)
            if target is None or target == null_device:
                return  # an already-open descriptor, or the null device
            writing = (isinstance(mode, str) and any(c in mode for c in "wax+")) or \
                      (isinstance(flags, int) and flags & _WRITE_FLAGS)
            if writing:
                deny(f"open for writing {os.path.basename(target)}")
            if target in allowed_files or _under(target, allowed_roots):
                return
            deny(f"open {os.path.basename(target)}")
        elif event in _DENIED_EVENTS or event.startswith(_DENIED_PREFIXES):
            deny(event)
        elif event in _NETWORK_HOST_EVENTS:
            if not _address_is_loopback(args[1] if len(args) > 1 else None):
                deny(event)
        elif event in _LOOKUP_EVENTS:
            if not _host_is_loopback(args[0] if args else None):
                deny(event)
        elif event == "http.client.connect":
            if not _host_is_loopback(args[1] if len(args) > 1 else None):
                deny(event)
        elif event in _LIST_EVENTS:
            target = _norm(args[0] if args else ".")
            if target is not None and not _under(target, allowed_roots):
                deny(event)

    sys.dont_write_bytecode = True
    sys.addaudithook(hook)
    _installed = True
