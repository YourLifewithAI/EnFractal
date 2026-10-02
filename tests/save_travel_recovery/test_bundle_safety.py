"""Corruption and destination guards; the separate drill uses real PostgreSQL."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("enfractal_recovery", ROOT / "tools/save-travel/recovery.py")
recovery = importlib.util.module_from_spec(spec)
spec.loader.exec_module(recovery)


class BundleSafety(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="enfractal_recovery_")
        self.bundle = Path(self.temporary.name)
        self.addCleanup(self.temporary.cleanup)
        self.content = {"game/maps/barton_creek/manifest.json": b'{"format":"test"}',
                        "game/styles/test.json": b'{"style":"test"}'}
        self.write_bundle()

    def write_bundle(self):
        records = []
        with zipfile.ZipFile(self.bundle / "content.zip", "w") as archive:
            for name, data in self.content.items():
                archive.writestr(name, data)
                records.append({"path": name, "bytes": len(data), "sha256": recovery.sha(data)})
        (self.bundle / "database.dump").write_bytes(b"PGDMP-test-safety-only")
        self.manifest = {"schema": recovery.FORMAT, "version": 1, "database_schema_version": 1,
                         "files": records, "content_bytes": sum(len(data) for data in self.content.values()),
                         "source_tree_sha256": recovery.sha(recovery.canonical(records)),
                         "database_dump_sha256": recovery.file_sha(self.bundle / "database.dump"),
                         "content_archive_sha256": recovery.file_sha(self.bundle / "content.zip"),
                         "pins": {"base_manifest_path": records[0]["path"], "base_manifest_sha256": records[0]["sha256"],
                                  "base_manifest_canonical_pin": "a" * 64, "source_base_pins": ["a" * 64],
                                  "style_tree_sha256": recovery.sha(recovery.canonical(sorted((record["path"], record["sha256"]) for record in records if record["path"].startswith("game/styles/") or record["path"].startswith("game/assets/art/"))))}}
        self.write_manifest()

    def write_manifest(self):
        (self.bundle / "manifest.json").write_bytes(recovery.canonical(self.manifest))

    def test_valid_inventory(self):
        self.assertEqual(recovery.verify(self.bundle)["files"], self.manifest["files"])

    def test_dump_corruption_rejected(self):
        with (self.bundle / "database.dump").open("ab") as stream:
            stream.write(b"corrupt")
        with self.assertRaisesRegex(recovery.RecoveryError, "database.dump"):
            recovery.verify(self.bundle)

    def test_content_corruption_rejected(self):
        with (self.bundle / "content.zip").open("ab") as stream:
            stream.write(b"corrupt")
        with self.assertRaisesRegex(recovery.RecoveryError, "content.zip"):
            recovery.verify(self.bundle)

    def test_wrong_schema_rejected(self):
        self.manifest["database_schema_version"] = 2
        self.write_manifest()
        with self.assertRaisesRegex(recovery.RecoveryError, "schema version"):
            recovery.verify(self.bundle)

    def test_inventory_corruption_rejected(self):
        self.manifest["files"][0]["sha256"] = "0" * 64
        self.write_manifest()
        with self.assertRaisesRegex(recovery.RecoveryError, "inventory"):
            recovery.verify(self.bundle)

    def test_base_pin_mismatch_rejected(self):
        self.manifest["pins"]["base_manifest_sha256"] = "0" * 64
        self.write_manifest()
        with self.assertRaisesRegex(recovery.RecoveryError, "Base map pin"):
            recovery.verify(self.bundle)

    def test_traversal_rejected_even_with_matching_archive_checksum(self):
        self.content = {"../outside.txt": b"no"}
        self.write_bundle()
        with self.assertRaisesRegex(recovery.RecoveryError, "unsafe content path"):
            recovery.verify(self.bundle)

    def test_style_pin_mismatch_rejected(self):
        self.manifest["pins"]["style_tree_sha256"] = "0" * 64
        self.write_manifest()
        with self.assertRaisesRegex(recovery.RecoveryError, "Style content pin"):
            recovery.verify(self.bundle)

    def test_saved_base_pin_mismatch_rejected(self):
        self.manifest["pins"]["source_base_pins"] = ["b" * 64]
        self.write_manifest()
        with self.assertRaisesRegex(recovery.RecoveryError, "Saved world pins"):
            recovery.verify(self.bundle)

    def test_map_resource_pin_mismatch_rejected(self):
        self.content["game/maps/barton_creek/manifest.json"] = recovery.canonical({"heights_file": "heights.r16", "heights_sha256": "a" * 64})
        self.content["game/maps/barton_creek/heights.r16"] = b"wrong height content"
        self.write_bundle()
        with self.assertRaisesRegex(recovery.RecoveryError, "map resource differs"):
            recovery.verify(self.bundle)

    def test_duplicate_inventory_rejected(self):
        self.manifest["files"].append(dict(self.manifest["files"][0]))
        self.manifest["source_tree_sha256"] = recovery.sha(recovery.canonical(self.manifest["files"]))
        self.write_manifest()
        with self.assertRaisesRegex(recovery.RecoveryError, "inventory"):
            recovery.verify(self.bundle)

    def test_unlisted_archive_member_rejected(self):
        with zipfile.ZipFile(self.bundle / "content.zip", "a") as archive:
            archive.writestr("unlisted.txt", b"no")
        self.manifest["content_archive_sha256"] = recovery.file_sha(self.bundle / "content.zip")
        self.write_manifest()
        with self.assertRaisesRegex(recovery.RecoveryError, "inventory differs"):
            recovery.verify(self.bundle)

    def test_unbounded_content_rejected_before_extract(self):
        self.manifest["files"][0]["bytes"] = recovery.MAX_CONTENT + 1
        self.manifest["source_tree_sha256"] = recovery.sha(recovery.canonical(self.manifest["files"]))
        self.write_manifest()
        with self.assertRaisesRegex(recovery.RecoveryError, "exceeds limits"):
            recovery.verify(self.bundle)

    def test_restore_rejects_unrelated_or_ambiguous_database_before_connect(self):
        for name in ("postgres", "production", "enfractal_home", "enfractal_restore_", "enfractal_restore_x;DROP DATABASE x", "ENFRACTAL_RESTORE_TEST"):
            with self.subTest(name=name), self.assertRaisesRegex(recovery.RecoveryError, "Restore target"):
                recovery.restore("unused", "unused", self.bundle, name)

    def test_existing_bundle_is_never_overwritten(self):
        # A destination guard must run before opening the source database.
        try:
            recovery.pg_modules()
        except recovery.RecoveryError:
            self.skipTest("psycopg required for backup entrypoint")
        with self.assertRaisesRegex(recovery.RecoveryError, "already exists"):
            recovery.backup("unused", "unused", self.bundle)


if __name__ == "__main__":
    unittest.main(verbosity=2)
