"""Behavioural alignment: the same steps on the mock host and on the real game host, with the same expected outcome.

test_kernel_alignment.py compares the two hosts' constants; constants alone missed behaviour (Codex's review of Lane A's
round 2). Each scenario here is written once against a few moves (ask, walk the companion somewhere, finish a goal's
job) and runs on the mock below and on the kernel host in test_real_host.py (AlignmentOnTheRealHost). Both must give
the scenario's expected outcome.
"""
from __future__ import annotations

import unittest

from support import COMPANION, FakeClock, HostPolicy, command, contract_problems, new_host, query

BEHIND_THE_BOX = [1.6, 0.0, 0.2]  # the box hides the book from the companion; the player at its spawn still sees it


class AlignmentScenarios:
    """A subclass provides ask(message), companion_to(position) and finish(job_id) -> the job's final state."""

    async def ask(self, message: dict) -> dict:
        raise NotImplementedError

    async def companion_to(self, position: list[float]) -> None:
        raise NotImplementedError

    async def finish(self, job_id: str) -> str:
        raise NotImplementedError

    def next_id(self, prefix: str) -> str:
        self._n = getattr(self, "_n", 0) + 1
        return f"{prefix}-{id(self) % 100000}-{self._n}"

    async def test_a_target_only_the_player_sees_is_in_sight_and_its_arrival_succeeds(self):
        await self.companion_to(BEHIND_THE_BOX)
        observed = await self.ask(query("observe", {"actor": "avatar:companion"}, self.next_id("q")))
        own_sight = {item["id"] for item in observed["data"]["visible"]}
        listed = await self.ask(query("entity.inspect", {"target": "obj:book"}, self.next_id("q")))
        look = await self.ask(command("goal.set", {"actor": "avatar:companion", "goal": "look_at", "target": "obj:book"},
                                      self.next_id("look")))
        outcome = {
            "the companion's own eyes see the book": "obj:book" in own_sight,
            "the book is in sight now": listed["ok"] and listed["data"]["entity"].get("seen"),
            "look_at accepted": (look["ok"], look.get("data", {}).get("target_seen")),
            "its job": await self.finish(look["job_id"]) if look["ok"] else None,
        }
        self.assertEqual(outcome, {
            "the companion's own eyes see the book": False,
            "the book is in sight now": "now",
            "look_at accepted": (True, "now"),
            "its job": "succeeded",
        })


class AlignmentOnTheMock(AlignmentScenarios, unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.host = new_host(policy=HostPolicy(companion_messages_per_s=1_000_000), clock=FakeClock())

    def tearDown(self):
        for result in self.host.emitted:
            self.assertEqual(contract_problems(result), [], result)

    async def ask(self, message: dict) -> dict:
        return self.host.handle(COMPANION, message)

    async def companion_to(self, position: list[float]) -> None:
        self.host.move_avatar("avatar:companion", position)

    async def finish(self, job_id: str) -> str:
        self.host.goal_arrived("avatar:companion")
        return (await self.ask(query("jobs.status", {"job_id": job_id}, self.next_id("q"))))["data"]["state"]


if __name__ == "__main__":
    unittest.main()
