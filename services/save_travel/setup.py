"""Opt-in Windows setup; installs only beneath this checkout's ignored .cache.

Run with an existing Python 3.11+ installation. Existing app configuration and
PostgreSQL data are never overwritten. No Windows service is installed.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import secrets
import shutil
import socket
import subprocess
import sys
import urllib.request
import zipfile

ARCHIVE_URL = "https://get.enterprisedb.com/postgresql/postgresql-17.6-1-windows-x64-binaries.zip"
ARCHIVE_SHA256 = "d378882abd001a186735acd6f6ba716bca6ccd192e800412d4fd15ed25376b3e"
PG_PORT = 55432


def within(path, parent):
    result = path.resolve()
    if not result.is_relative_to(parent.resolve()) or result == parent.resolve():
        raise RuntimeError("Setup destination must remain inside this checkout's cache.")
    return result


def checked_archive(archive, target):
    with archive.open("rb") as stream:
        if hashlib.file_digest(stream, "sha256").hexdigest() != ARCHIVE_SHA256:
            raise RuntimeError("PostgreSQL archive checksum does not match the pinned download.")
    with zipfile.ZipFile(archive) as bundle:
        for member in bundle.infolist():
            name = PurePosixPath(member.filename.replace("\\", "/"))
            if name.is_absolute() or ".." in name.parts or ":" in member.filename:
                raise RuntimeError("PostgreSQL archive contains an unsafe path.")
            within(target.joinpath(*name.parts), target)
        bundle.extractall(target)


def run(arguments, *, env=None, input_text=None, silent=False):
    result = subprocess.run([str(arg) for arg in arguments], env=env, input=input_text, text=True, encoding="utf-8", errors="replace", capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        # Inputs may contain database credentials. Never echo commands, arguments,
        # stdout or stderr on failure; the relevant tool and return code suffice.
        raise RuntimeError(f"{Path(str(arguments[0])).name} failed (exit {result.returncode}). No existing data was replaced.")
    if not silent and result.stdout:
        print(result.stdout.strip())
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--existing-dsn-env", help="Name of an environment variable holding an existing loopback PostgreSQL DSN; no server download or cluster management.")
    args = parser.parse_args()
    if os.name != "nt":
        raise SystemExit("This optional portable setup supports Windows. Use an existing local PostgreSQL installation on other systems.")
    repo = Path(__file__).resolve().parents[2]
    cache = within(repo / ".cache", repo)
    cache.mkdir(exist_ok=True)
    config_path = within(cache / "save-travel-config.json", cache)
    venv = within(cache / "save-travel-venv", cache)
    python = venv / "Scripts/python.exe"
    if not python.exists():
        print("Preparing the project-local Python environment...")
        run([sys.executable, "-m", "venv", venv], silent=True)
    run([python, "-m", "pip", "install", "--disable-pip-version-check", "-r", Path(__file__).with_name("requirements.txt")], silent=True)
    if config_path.exists():
        print("Project-local Python dependencies are ready. Existing save configuration retained. Run run-save-travel.ps1 to start it.")
        return
    pending = within(cache / "save-travel-config.pending.json", cache)
    if pending.exists():
        raise RuntimeError("Interrupted setup credentials already exist in .cache/save-travel-config.pending.json. Complete or repair that matching cluster; setup will not overwrite those credentials.")
    config = {"token": secrets.token_hex(32), "port": 8765, "manage_postgres": not bool(args.existing_dsn_env)}
    if args.existing_dsn_env:
        database_url = os.environ.get(args.existing_dsn_env)
        if not database_url:
            raise RuntimeError("The requested database environment variable is empty.")
        # Read the credential on stdin, never as a process argument or log value.
        validator = "import json,sys,psycopg; from psycopg.conninfo import conninfo_to_dict; d=json.load(sys.stdin); c=conninfo_to_dict(d['database_url']); assert c.get('host') in ('127.0.0.1','localhost','::1'), 'loopback required'; p=psycopg.connect(d['database_url'],connect_timeout=5); p.close()"
        run([python, "-c", validator], input_text=json.dumps({"database_url": database_url}), silent=True)
        config["database_url"] = database_url
    else:
        pg_root = within(cache / "postgresql", cache)
        pg_root.mkdir(exist_ok=True)
        archive = within(pg_root / "postgresql.zip", pg_root)
        binaries = within(pg_root / "pgsql/bin", pg_root)
        data = within(pg_root / "data", pg_root)
        if not (binaries / "initdb.exe").exists():
            if not archive.exists():
                print("Downloading the pinned official portable PostgreSQL archive...")
                partial = within(pg_root / "postgresql.zip.download", pg_root)
                with urllib.request.urlopen(ARCHIVE_URL, timeout=60) as response, partial.open("wb") as output:
                    shutil.copyfileobj(response, output)
                with partial.open("rb") as stream:
                    if hashlib.file_digest(stream, "sha256").hexdigest() != ARCHIVE_SHA256:
                        raise RuntimeError("Downloaded PostgreSQL archive failed its checksum; it was not installed.")
                partial.replace(archive)
            print("Verifying and extracting PostgreSQL inside the project cache...")
            checked_archive(archive, pg_root)
        if data.exists():
            raise RuntimeError("A PostgreSQL data directory already exists without the expected configuration. Restore its matching configuration; setup will not overwrite or reinitialize it.")
        with socket.socket() as probe:
            if probe.connect_ex(("127.0.0.1", PG_PORT)) == 0:
                raise RuntimeError("The local PostgreSQL port is already in use. Use --existing-dsn-env for an existing local database.")
        password = secrets.token_hex(32)
        database_url = f"postgresql://enfractal:{password}@127.0.0.1:{PG_PORT}/enfractal_local"
        config.update(database_url=database_url, postgres_bin=str(binaries), postgres_data=str(data))
        # Preserve matching credentials before initdb can create any data. An
        # interruption at any subsequent stage retains this recovery record.
        # Exclusive creation also refuses a competing setup's pending record.
        with pending.open("x", encoding="utf-8") as stream:
            json.dump(config, stream, indent=2)
            stream.flush()
            os.fsync(stream.fileno())
        password_file = within(pg_root / ("init-password-" + secrets.token_hex(4)), pg_root)
        password_file.write_text(password, encoding="utf-8")
        try:
            print("Creating the local SCRAM-authenticated PostgreSQL cluster...")
            run([binaries / "initdb.exe", "-D", data, "-U", "enfractal", "--auth-host=scram-sha-256", "--auth-local=scram-sha-256", "--encoding=UTF8", "--locale=C", "--data-checksums", "--pwfile=" + str(password_file)], silent=True)
        finally:
            password_file.unlink(missing_ok=True)
        # Persist loopback-only binding, small fixture memory and durable settings;
        # later pg_ctl starts are safe without relying on remembered CLI flags.
        with (data / "postgresql.conf").open("a", encoding="utf-8") as stream:
            stream.write("\n# Enfractal local pilot\nlisten_addresses = '127.0.0.1'\nport = 55432\nshared_buffers = '128MB'\nmax_connections = 30\nfsync = on\nfull_page_writes = on\nsynchronous_commit = on\n")
        run([binaries / "pg_ctl.exe", "-D", data, "-l", pg_root / "postgres.log", "-w", "start"], silent=True)
        database_env = {**os.environ, "PGPASSWORD": password}
        run([binaries / "createdb.exe", "-h", "127.0.0.1", "-p", str(PG_PORT), "-U", "enfractal", "enfractal_local"], env=database_env, silent=True)
        pending.replace(config_path)
    if not config_path.exists():
        config_path.write_text(json.dumps(config, indent=2), encoding="utf-8")
    print("Local save setup is ready. Credentials remain in the ignored .cache configuration. Run run-save-travel.ps1.")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, ValueError, zipfile.BadZipFile) as error:
        # urllib exceptions may include a URL but never a credential from our
        # pinned download. Runtime errors intentionally avoid secret diagnostics.
        raise SystemExit(str(error)) from None
