"""Bounded logical backup and isolated restore for Enfractal's local Phase 4 host.

Credentials enter through ENFRACTAL_DATABASE_URL; they are never written to bundles.
Only trusted, operator-created bundles may be restored: PostgreSQL archives can
contain executable database objects. Checksums detect corruption, not authorship.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import time
import uuid
import zipfile
from datetime import datetime, timezone

FORMAT = "enfractal.recovery-bundle"
MAX_ARCHIVE = 256 * 1024 * 1024
MAX_CONTENT = 256 * 1024 * 1024
MAX_FILES = 5000
ROOT = Path(__file__).resolve().parents[2]
SOURCE_ROOTS = ("game/scripts", "game/shaders", "game/scenes", "game/maps", "game/styles",
                "game/assets", "game/creation_templates", "services/save_travel", "tools/save-travel",
                "assets/art_sources", "tools/art")
SOURCE_FILES = ("game/project.godot", "game/CREDITS.md", "README.md")
SOURCE_SUFFIXES = {".py", ".sql", ".txt", ".toml", ".json", ".gd", ".gdshader",
                   ".tscn", ".tres", ".glb", ".blend", ".png", ".r16", ".uid", ".md", ".import"}
DB_NAME = re.compile(r"^enfractal_[a-z0-9_]{1,48}$")
RESTORE_NAME = re.compile(r"^enfractal_restore_[a-z0-9_]{1,40}$")


class RecoveryError(RuntimeError):
    pass


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False,
                      allow_nan=False).encode("utf-8")


def sha(data):
    return hashlib.sha256(data).hexdigest()


def file_sha(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def pg_modules():
    try:
        import psycopg
        from psycopg import conninfo, sql
    except ImportError as error:
        raise RecoveryError("Use the save-travel Python environment with psycopg installed.") from error
    return psycopg, conninfo, sql


def pg_env(database_url, database=None):
    _, conninfo, _ = pg_modules()
    fields = conninfo.conninfo_to_dict(database_url)
    allowed = {"host", "port", "user", "password", "dbname", "sslmode", "connect_timeout"}
    if set(fields) - allowed:
        raise RecoveryError("Recovery accepts explicit host/port/user/password/dbname/sslmode connection fields only.")
    env = os.environ.copy()
    # Avoid ambient service files or options changing the chosen recovery endpoint.
    for key in tuple(env):
        if key.startswith("PG"):
            del env[key]
    for field, value in fields.items():
        env["PG" + ("DATABASE" if field == "dbname" else field.upper())] = str(value)
    env["PGCONNECT_TIMEOUT"] = "10"
    if database is not None:
        env["PGDATABASE"] = database
    return env


def pg_tool(bin_dir, name, args, env, timeout=120):
    executable = Path(bin_dir) / (name + (".exe" if os.name == "nt" else ""))
    if not executable.is_file():
        raise RecoveryError(f"PostgreSQL tool is missing: {executable}")
    result = subprocess.run([str(executable), *args], env=env, capture_output=True,
                            text=True, timeout=timeout, check=False)
    if result.returncode:
        # Do not echo command lines, environment or database URLs.
        raise RecoveryError(f"{name} failed with exit {result.returncode}: {result.stderr.strip()[:1000]}")
    return result.stdout.strip()


def checked_relative(value):
    if not isinstance(value, str):
        raise RecoveryError("Bundle contains an unsafe content path.")
    path = PurePosixPath(value)
    if (not value or "\\" in value or ":" in value
            or path.is_absolute() or ".." in path.parts or str(path) != value):
        raise RecoveryError("Bundle contains an unsafe content path.")
    return path


def source_archive(repo, destination):
    paths = set()
    for relative in SOURCE_ROOTS:
        directory = repo / relative
        if directory.is_dir():
            paths.update(p for p in directory.rglob("*") if p.is_file()
                         and p.suffix in SOURCE_SUFFIXES and "__pycache__" not in p.parts
                         and (relative != "services/save_travel" or p.suffix in {".py", ".sql"} or p.name == "requirements.txt"))
    paths.update(repo / name for name in SOURCE_FILES if (repo / name).is_file())
    if len(paths) > MAX_FILES:
        raise RecoveryError("Source file count exceeds the recovery bundle limit.")
    records, total = [], 0
    with zipfile.ZipFile(destination, "x", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(paths):
            if path.is_symlink() or not path.resolve().is_relative_to(repo.resolve()):
                raise RecoveryError("Source symlinks and paths outside the repository are not supported.")
            data = path.read_bytes()
            total += len(data)
            if total > MAX_CONTENT:
                raise RecoveryError("Source content exceeds the recovery bundle limit.")
            name = path.relative_to(repo).as_posix()
            archive.writestr(name, data)
            records.append({"path": name, "bytes": len(data), "sha256": sha(data)})
    return records, total


def database_snapshot(connection):
    version = connection.execute("SELECT version FROM enfractal_meta").fetchall()
    rows = connection.execute("SELECT singleton, document, sha256 FROM enfractal_state ORDER BY singleton").fetchall()
    if version != [(1,)] or len(rows) != 1 or rows[0][0] != 1 or not isinstance(rows[0][1], dict):
        raise RecoveryError("Expected exactly the Phase 4 v1 metadata and singleton state document.")
    state = rows[0][1]
    if sha(canonical(state)) != rows[0][2]:
        raise RecoveryError("The stored state checksum failed; preserve the database for diagnosis.")
    return state


def is_style_path(name):
    return (name.startswith(("game/styles/", "game/shaders/", "game/assets/art/", "assets/art_sources/", "tools/art/"))
            or name.startswith("game/scripts/painterly"))


def inspect_pins(state, records, base_pin):
    """Record source pins without serializing identities, invitations or credentials."""
    indexed = {record["path"]: record["sha256"] for record in records}
    base_path = "game/maps/barton_creek/manifest.json"
    style_paths = sorted(name for name in indexed if is_style_path(name))
    pins = set()
    envelopes = []

    def visit(value):
        if isinstance(value, dict):
            if value.get("schema") == "enfractal.creation-world":
                envelopes.append(value)
                if value.get("base_pin"):
                    pins.add(str(value["base_pin"]))
            for child in value.values():
                visit(child)
        elif isinstance(value, list):
            for child in value:
                visit(child)

    visit(state)
    if base_path not in indexed:
        raise RecoveryError("Barton base manifest is absent from the source archive.")
    if state.get("base_pin") != base_pin or pins != {base_pin}:
        raise RecoveryError("Saved worlds do not match the exact bundled Barton base pin.")
    if any(envelope.get("compiler_version") != 1 or envelope.get("style_version") != "barton_painterly_v1" for envelope in envelopes):
        raise RecoveryError("Saved creations require a compiler or style version absent from this bundle.")
    return {"base_manifest_path": base_path, "base_manifest_sha256": indexed[base_path],
            "base_manifest_canonical_pin": base_pin,
            "source_base_pins": sorted(pins), "editable_envelopes": len(envelopes),
            "style_tree_sha256": sha(canonical([(name, indexed[name]) for name in style_paths]))}


def godot_base_pin(repo, godot_bin=None):
    executable = Path(godot_bin or os.getenv("ENFRACTAL_GODOT") or
                      repo / ".cache/godot/Godot_v4.7.2-stable_win64.exe")
    if not executable.is_file():
        raise RecoveryError("Set ENFRACTAL_GODOT to the Godot executable to verify the compiler's exact base pin.")
    result = subprocess.run([str(executable), "--headless", "--path", str(repo / "game"),
                             "--script", str(repo / "tools/save-travel/content_pin.gd")],
                            capture_output=True, text=True, timeout=30, check=False)
    lines = [line.removeprefix("ENFRACTAL_BASE_PIN|") for line in result.stdout.splitlines()
             if line.startswith("ENFRACTAL_BASE_PIN|")]
    if result.returncode or len(lines) != 1:
        raise RecoveryError("Godot could not verify this content version's exact base pin.")
    return json.loads(lines[0])


def backup(database_url, bin_dir, output, repo=ROOT, godot_bin=None):
    psycopg, _, _ = pg_modules()
    output, repo = Path(output).resolve(), Path(repo).resolve()
    if output.exists():
        raise RecoveryError("Backup destination already exists; choose a new bundle directory.")
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name + ".incomplete-" + uuid.uuid4().hex)
    temporary.mkdir()
    started, wall_started = time.monotonic(), datetime.now(timezone.utc).isoformat()
    with psycopg.connect(database_url) as connection:
        connection.isolation_level = psycopg.IsolationLevel.REPEATABLE_READ
        connection.read_only = True
        name = connection.execute("SELECT current_database()").fetchone()[0]
        if not DB_NAME.fullmatch(name):
            raise RecoveryError("Backup source must be an explicit enfractal_ database.")
        state = database_snapshot(connection)
        snapshot = connection.execute("SELECT pg_export_snapshot()").fetchone()[0]
        database_version = connection.execute("SHOW server_version").fetchone()[0]
        pg_tool(bin_dir, "pg_dump", ["--format=custom", "--no-owner", "--no-privileges", "--no-password",
                "--snapshot=" + snapshot, "--file=" + str(temporary / "database.dump")], pg_env(database_url))
    dump_path = temporary / "database.dump"
    if dump_path.stat().st_size > MAX_ARCHIVE:
        raise RecoveryError("Database archive exceeds the bounded recovery size.")
    records, total = source_archive(repo, temporary / "content.zip")
    base = godot_base_pin(repo, godot_bin)
    pins = inspect_pins(state, records, base["pin"])
    if pins["base_manifest_sha256"] != base["raw_sha256"]:
        raise RecoveryError("The base changed during packaging; retry from a stable source version.")
    commit = subprocess.run(["git", "-C", str(repo), "rev-parse", "HEAD"], capture_output=True, text=True, check=True).stdout.strip()
    dirty = bool(subprocess.run(["git", "-C", str(repo), "status", "--porcelain"], capture_output=True, text=True, check=True).stdout.strip())
    manifest = {"schema": FORMAT, "version": 1, "database_schema_version": 1,
                "created_utc": wall_started, "database_snapshot_sha256": sha(canonical(state)),
                "database_server_version": database_version,
                "pg_dump_version": pg_tool(bin_dir, "pg_dump", ["--version"], pg_env(database_url)),
                "repository_commit": commit, "repository_dirty": dirty,
                "source_tree_sha256": sha(canonical(records)), "pins": pins,
                "content_bytes": total, "files": records,
                "database_dump_sha256": file_sha(dump_path),
                "content_archive_sha256": file_sha(temporary / "content.zip"),
                "backup_seconds": round(time.monotonic() - started, 3),
                "scope": "portable local logical snapshot; off-host recovery has not been demonstrated"}
    (temporary / "manifest.json").write_bytes(canonical(manifest) + b"\n")
    verify(temporary)
    temporary.rename(output)
    return {"ok": True, "bundle": str(output), "backup_seconds": manifest["backup_seconds"],
            "snapshot_sha256": manifest["database_snapshot_sha256"]}


def verify(bundle):
    bundle = Path(bundle).resolve()
    manifest_path = bundle / "manifest.json"
    if not manifest_path.is_file() or manifest_path.stat().st_size > 2 * 1024 * 1024:
        raise RecoveryError("Missing or oversized bundle manifest.")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("schema") != FORMAT or manifest.get("version") != 1 or manifest.get("database_schema_version") != 1:
        raise RecoveryError("Unsupported recovery bundle or database schema version.")
    for name, key in (("database.dump", "database_dump_sha256"), ("content.zip", "content_archive_sha256")):
        path = bundle / name
        if not path.is_file() or path.is_symlink() or path.stat().st_size > MAX_ARCHIVE or file_sha(path) != manifest.get(key):
            raise RecoveryError(f"Checksum or size validation failed for {name}.")
    records = manifest.get("files", [])
    if not isinstance(records, list) or not records or len(records) > MAX_FILES or sha(canonical(records)) != manifest.get("source_tree_sha256"):
        raise RecoveryError("Source file inventory is invalid.")
    expected, total = {}, 0
    for record in records:
        name = str(checked_relative(record["path"]))
        if name in expected or type(record["bytes"]) is not int or record["bytes"] < 0:
            raise RecoveryError("Source file inventory is invalid.")
        expected[name] = record
        total += record["bytes"]
    if total > MAX_CONTENT or total != manifest.get("content_bytes"):
        raise RecoveryError("Source content exceeds limits or has an inconsistent size.")
    with zipfile.ZipFile(bundle / "content.zip") as archive:
        entries = archive.infolist()
        if len(entries) != len(expected) or {entry.filename for entry in entries} != set(expected):
            raise RecoveryError("Source archive inventory differs from its manifest.")
        for entry in entries:
            record = expected[entry.filename]
            if entry.file_size != record["bytes"] or sha(archive.read(entry)) != record["sha256"]:
                raise RecoveryError("Source content checksum mismatch.")
        map_path = "game/maps/barton_creek/manifest.json"
        if map_path not in expected:
            raise RecoveryError("The bundled Barton base manifest is missing.")
        map_manifest = json.loads(archive.read(map_path))
        for key, relative in map_manifest.items():
            if key.endswith("_file"):
                checked_relative(relative)
                name = "game/maps/barton_creek/" + relative
                recorded = expected.get(name)
                if not recorded or recorded["sha256"] != map_manifest.get(key.removesuffix("_file") + "_sha256"):
                    raise RecoveryError("A map resource differs from its pinned base manifest.")
    pins = manifest.get("pins", {})
    if expected.get(pins.get("base_manifest_path"), {}).get("sha256") != pins.get("base_manifest_sha256"):
        raise RecoveryError("Base map pin does not match the bundled content.")
    style_paths = sorted(name for name in expected if is_style_path(name))
    if sha(canonical([(name, expected[name]["sha256"]) for name in style_paths])) != pins.get("style_tree_sha256"):
        raise RecoveryError("Style content pin does not match the bundled content.")
    if pins.get("source_base_pins") != [pins.get("base_manifest_canonical_pin")]:
        raise RecoveryError("Saved world pins disagree with the bundled base pin.")
    return manifest


def restore(database_url, bin_dir, bundle, destination):
    if not RESTORE_NAME.fullmatch(destination):
        raise RecoveryError("Restore target must be a new enfractal_restore_ name with lowercase letters, digits or underscores.")
    manifest = verify(bundle)
    psycopg, conninfo, sql = pg_modules()
    started = time.monotonic()
    with psycopg.connect(database_url, autocommit=True) as admin:
        if admin.execute("SELECT 1 FROM pg_database WHERE datname = %s", (destination,)).fetchone():
            raise RecoveryError("Restore target already exists; refusing to change it.")
        admin.execute(sql.SQL("CREATE DATABASE {} TEMPLATE template0").format(sql.Identifier(destination)))
    # No DROP, --clean or --create options. A failed restore leaves its newly
    # created database for inspection; the source database is never touched.
    pg_tool(bin_dir, "pg_restore", ["--single-transaction", "--exit-on-error", "--no-owner", "--no-password",
            "--no-privileges", "--dbname=" + destination, str(Path(bundle).resolve() / "database.dump")],
            pg_env(database_url, destination))
    target_url = conninfo.make_conninfo(database_url, dbname=destination)
    with psycopg.connect(target_url) as restored:
        state = database_snapshot(restored)
        if sha(canonical(state)) != manifest["database_snapshot_sha256"]:
            raise RecoveryError("Restored state differs from the consistent backup snapshot; do not start this host.")
    return {"ok": True, "database": destination, "snapshot_sha256": manifest["database_snapshot_sha256"],
            "restore_seconds": round(time.monotonic() - started, 3),
            "note": "Verify pins and invariants, then bootstrap a fresh host session before use."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("backup", "verify", "restore"))
    parser.add_argument("--bundle", required=True, type=Path)
    parser.add_argument("--pg-bin", default=os.getenv("ENFRACTAL_PG_BIN"))
    parser.add_argument("--destination")
    args = parser.parse_args()
    try:
        if args.command == "verify":
            manifest = verify(args.bundle)
            result = {"ok": True, "snapshot_sha256": manifest["database_snapshot_sha256"], "files": len(manifest["files"])}
        else:
            database_url = os.getenv("ENFRACTAL_DATABASE_URL", "")
            if not database_url or not args.pg_bin:
                raise RecoveryError("Set ENFRACTAL_DATABASE_URL and ENFRACTAL_PG_BIN.")
            result = backup(database_url, args.pg_bin, args.bundle) if args.command == "backup" else restore(database_url, args.pg_bin, args.bundle, args.destination or "")
        print(json.dumps(result, sort_keys=True))
    except (RecoveryError, ValueError, OSError, subprocess.SubprocessError) as error:
        print(json.dumps({"ok": False, "error": str(error)}), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
