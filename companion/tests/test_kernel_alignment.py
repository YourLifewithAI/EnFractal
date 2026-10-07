"""The mock host against the kernel host it stands in for (Lane P's CommandHost.cs and creation_authority.gd).

Run 2 swaps the mock for the kernel host behind the same link, so every number both of them fix must be
the same. These tests read the kernel's own constants (read-only) and fail when either side drifts, then
check the mock's default policy at those boundaries.
"""
from __future__ import annotations

import re
import unittest

from support import COMPANION, PLAYER, REPO, FakeClock, HostPolicy, command, new_host, query

from enfractal_companion.mock_host import Receipt

COMMAND_HOST = REPO / "game" / "scripts" / "native" / "Kernel" / "CommandHost.cs"
AUTHORITY = REPO / "game" / "scripts" / "creation_authority.gd"


def _constant(path, pattern: str) -> int:
    match = re.search(pattern, path.read_text(encoding="utf-8"))
    if match is None:
        raise AssertionError(f"{path.name} no longer declares {pattern!r}; update this test with the kernel")
    return int(match.group(1))


@unittest.skipUnless(COMMAND_HOST.is_file() and AUTHORITY.is_file(), "the kernel host is not in this checkout")
class PolicyMatchesTheKernel(unittest.TestCase):
    def test_the_default_policy_uses_the_kernel_hosts_numbers(self):
        policy = HostPolicy()
        cs = lambda name: _constant(COMMAND_HOST, rf"\bconst int {name} = (\d+);")  # noqa: E731
        gd = lambda name: _constant(AUTHORITY, rf"\bconst {name} := (\d+)\b")  # noqa: E731
        expected = {
            "max_pending_approvals": cs("MaxPendingApprovals"),
            "companion_messages_per_s": cs("CompanionMessagesPerSecond"),
            "max_transient_receipts_per_principal": cs("MaxTransientPerPrincipal"),
            "perception_memory_entries": cs("PerceptionMemoryEntries"),
            "perception_memory_stale_after_s": cs("PerceptionMemoryStaleAfterS"),
            "max_jobs_per_principal": cs("MaxJobsPerPrincipal"),
            "max_durable_receipts": gd("MAX_RECEIPTS"),
            "player_receipt_reserve": gd("PLAYER_RECEIPT_RESERVE"),
            "max_compacted_receipts": gd("MAX_COMPACTED"),
            "max_checkpoints": gd("MAX_CHECKPOINTS"),
            "approval_ttl_s": 60 * _constant(COMMAND_HOST, r"ApprovalLifetime = TimeSpan\.FromMinutes\((\d+)\)"),
        }
        self.assertEqual({name: getattr(policy, name) for name in expected}, expected)
        # The activation receipts the authority keeps are bounded the same way as the host's transient ones.
        self.assertEqual(gd("MAX_TRANSIENT_PER_PRINCIPAL"), policy.max_transient_receipts_per_principal)


class DefaultPolicyBoundaries(unittest.TestCase):
    """The default policy at the kernel's boundaries (the mechanisms are tested with small numbers elsewhere)."""

    def setUp(self):
        self.clock = FakeClock()
        self.host = new_host(clock=self.clock)

    def tearDown(self):
        from support import contract_problems
        for result in self.host.emitted:
            self.assertEqual(contract_problems(result), [], result)

    def fill_ledger(self, count: int) -> None:
        """Durable receipts committed earlier (contents do not matter for the bound)."""
        for i in range(len(self.host.receipts), count):
            self.host.receipts[(PLAYER, f"earlier-{i}")] = Receipt("0" * 64, {"revision": 0, "op": "protect.lock"}, True)

    def lock(self, target, action_id, principal=COMPANION):
        self.clock.advance(1.1)  # keep the companion well inside its rate budget
        return self.host.handle(principal, command("protect.lock", {"targets": [target]}, action_id,
                                                   expected_entities={target: self.host.entities[target].revision}))

    def test_the_companion_fills_1792_receipts_and_the_player_the_last_256(self):
        self.fill_ledger(2048 - 256 - 1)
        self.assertTrue(self.lock("obj:box", "lock-1")["ok"])
        refused = self.lock("obj:book", "lock-2")
        self.assertFalse(refused["ok"])
        self.assertEqual(refused["error"]["code"], "receipt_limit")
        self.assertTrue(self.lock("obj:book", "p-lock-1", PLAYER)["ok"])
        self.fill_ledger(2048 - 1)
        self.assertTrue(self.lock("obj:rug", "p-lock-2", PLAYER)["ok"])
        self.assertEqual(self.lock("obj:doorstop", "p-lock-3", PLAYER)["error"]["code"], "receipt_limit")

    def test_a_companion_gets_30_messages_a_second(self):
        results = [self.host.handle(COMPANION, query("room.describe", {}, f"q-{i}")) for i in range(31)]
        self.assertTrue(all(r["ok"] for r in results[:30]))
        self.assertEqual(results[30]["error"]["code"], "rate_limited")
        self.clock.advance(1.01)
        self.assertTrue(self.host.handle(COMPANION, query("room.describe", {}, "q-late"))["ok"])


if __name__ == "__main__":
    unittest.main()
