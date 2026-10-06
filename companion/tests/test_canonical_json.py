"""The mock host fingerprints receipts with Lane P's canonical JSON v1, byte for byte.

The golden fixture is a copy of game/tests/fixtures/kernel/ from run1/play (see
fixtures/canonical_json_v1/SOURCE.txt); at integration this test points at the shared fixture.
"""
from __future__ import annotations

import hashlib
import unittest
from pathlib import Path

from support import COMPANION, command, new_host

from enfractal_companion import canonical

FIXTURE = Path(__file__).resolve().parent / "fixtures" / "canonical_json_v1"


class CanonicalJsonV1(unittest.TestCase):
    def test_reproduces_the_golden_fixture_byte_for_byte(self):
        document = canonical.loads_strict((FIXTURE / "canonical_input.json").read_bytes().decode("utf-8"))
        produced = canonical.canonical_bytes(document)
        expected = (FIXTURE / "canonical_expected.json").read_bytes()
        self.assertEqual(produced, expected)
        digest = (FIXTURE / "canonical_expected.sha256").read_bytes().decode("ascii").strip()
        self.assertEqual(hashlib.sha256(produced).hexdigest(), digest)
        self.assertEqual(canonical.sha256_hex(document), digest)

    def test_the_contract_layer_measures_and_fingerprints_with_v1(self):
        from support import contracts
        self.assertEqual(contracts().canonical_bytes({"b": 2.0, "a": [1e0, -0.0, 0.5]}), b'{"a":[1,0,0.5],"b":2}')

    def test_refuses_what_v1_refuses(self):
        for text in ('{"a":1,"a":2}', '{"a":NaN}', '{"a":1e999}'):
            with self.subTest(text=text), self.assertRaises(canonical.CanonicalJsonError):
                canonical.loads_strict(text)
        with self.assertRaises(canonical.CanonicalJsonError):
            canonical.canonical_bytes({"s": "lone " + chr(0xD800)})
        deep: dict = {}
        node = deep
        for _ in range(130):
            node["x"] = {}
            node = node["x"]
        with self.assertRaises(canonical.CanonicalJsonError):
            canonical.canonical_bytes(deep)

    def test_receipts_match_on_v1_so_1_0_and_1_are_the_same_command(self):
        host = new_host()
        first = host.handle(COMPANION, command("entity.place", {"target": "obj:book", "placement": {"position_m": [1, 0, 1]}},
                                               "place-1"))
        again = host.handle(COMPANION, command("entity.place", {"target": "obj:book",
                                                                "placement": {"position_m": [1.0, 0.0, 1e0]}}, "place-1"))
        self.assertTrue(first["ok"])
        self.assertTrue(again["replayed"], again)
        receipt = host.receipts[(COMPANION, "place-1")]
        expected = canonical.sha256_hex(command("entity.place", {"target": "obj:book", "placement": {"position_m": [1, 0, 1]}},
                                                "place-1"))
        self.assertEqual(receipt.fingerprint, expected)


if __name__ == "__main__":
    unittest.main()
