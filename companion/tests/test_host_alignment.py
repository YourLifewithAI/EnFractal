"""Behavioural alignment: the same steps on the mock host and on the real game host, with the same expected outcome.

test_kernel_alignment.py compares the two hosts' constants; constants alone missed behaviour (Codex's review of Lane A's
round 2). Each scenario here is written once against a few moves (ask, walk the companion somewhere, finish a goal's
job) and runs on the mock below and on the kernel host in test_real_host.py (AlignmentOnTheRealHost). Both must give
the scenario's expected outcome.
"""
from __future__ import annotations

import json
import unittest

from support import COMPANION, FakeClock, HostPolicy, command, contract_problems, new_host, query

BEHIND_THE_BOX = [1.6, 0.0, 0.2]  # the box hides the book from the companion; the player at its spawn still sees it
HIDDEN_SPOT = [-0.9, 0.0, -1.42]  # behind the table: neither avatar sees it from beside the player


def block(name: str) -> dict:
    """A 10 cm wooden block, the creation compiler's smallest part."""
    return {"schema": "enfractal.creation", "version": 1, "name": name, "seed": 7, "mount": "ground",
            "parts": [{"id": "block", "shape": "box", "position_m": [0, 0.05, 0], "rotation_deg": [0, 0, 0],
                       "size_m": [0.1, 0.1, 0.1], "material": "wood"}], "nodes": [], "edges": []}


class AlignmentScenarios:
    """A subclass provides ask(message), companion_to(position) and finish(job_id) -> the job's final state."""

    async def ask(self, message: dict) -> dict:
        raise NotImplementedError

    async def companion_to(self, position: list[float]) -> None:
        raise NotImplementedError

    async def finish(self, job_id: str) -> str:
        raise NotImplementedError

    # Outcome keys a host cannot yet be held to (each with its reason in the subclass).
    unchecked: tuple[str, ...] = ()

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


    async def test_a_build_neither_avatar_sees_stays_unnamed_and_cannot_be_changed_or_removed_unseen(self):
        """Hidden build, unseen revision, unseen removal: a creation the companion places where neither avatar's eyes
        reach is "something" in the journal, with no id and no pin, and the team cannot name it: revising or removing it
        is refused exactly as for an id that does not exist, and writes nothing."""
        placed = await self.ask(command("creation.place", {"source": block("Hidden vault"),
                                                           "placement": {"position_m": HIDDEN_SPOT}}, self.next_id("hide")))
        self.assertTrue(placed["ok"], placed)
        made = placed["created"][0]
        inspect = await self.ask(query("entity.inspect", {"target": made}, self.next_id("q")))
        journal = (await self.ask(query("journal.read", {"kind": "built", "limit": 50}, self.next_id("q"))))["data"]
        built = [e for e in journal["entries"] if e["revision"] == placed["revision"]]
        before = (await self.ask(query("journal.read", {"limit": 50}, self.next_id("q"))))["data"]

        async def refused(op, args_for):
            answers = []
            for target in (made, "creation:zz-not-here"):
                result = await self.ask(command(op, args_for(target), self.next_id("try"), expected_revision=placed["revision"]))
                answers.append(None if result["ok"] else (result["error"]["code"], result["error"]["field_path"],
                                                           result["error"]["message"]))
            return answers[0] if answers[0] == answers[1] else answers

        revise = await refused("creation.revise", lambda t: {"target": t, "source": block("Renamed vault")})
        remove = await refused("entity.remove", lambda t: {"target": t})
        after = (await self.ask(query("journal.read", {"limit": 50}, self.next_id("q"))))["data"]
        outcome = {
            "inspect": inspect["ok"] or inspect["error"]["code"],
            "built facts": [(e["line"], "subject" in e, "pin_m" in e) for e in built],
            "revise refused like an unknown id": revise and revise[0],
            "remove refused like an unknown id": remove and remove[0],
            "nothing written since": after == before,
            "named anywhere": "Hidden vault" in json.dumps(after) or made in json.dumps(after),
        }
        expected = {
            "inspect": "target_not_found",
            "built facts": [("Built something, on its own initiative", False, False)],
            "revise refused like an unknown id": "target_not_found",
            "remove refused like an unknown id": "target_not_found",
            "nothing written since": True,
            "named anywhere": False,
        }
        for key in self.unchecked:
            outcome.pop(key)
            expected.pop(key)
        self.assertEqual(outcome, expected)


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
