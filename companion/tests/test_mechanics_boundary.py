"""The mechanics round's contract at the companion boundary (M0, 14 October 2026; docs/companion/SECURITY.md, the rows marked
planned for M0): the player's new ops have no tool and refuse the companion, a Fractal is never the companion's to gather,
the Bubbles carry lands on the player's avatar only under the targets rule, and no field of a request changes a price.

test_host_alignment.py holds the companion's side of these on both hosts; this file holds the player's side on the mock, the
adapter's refusals before anything is sent, and the prices the mock reads from the pack.
"""
from __future__ import annotations

import inspect
import unittest

from mcp_harness import McpHarness
from support import COMPANION, GUBBLE, IN_THE_OPEN, PLAYER, command, contract_problems, contracts, new_host

from enfractal_companion import mock_host
from enfractal_companion.mock_host import load_rules, rules_path

NEW_PLAYER_OPS = {
    "item.pickup": {"target": "fractal:k3x9q2"},
    "knack.set": {"slot": 1, "knack": "quick_feet"},
    "receipt.mark": {"action_id": "bloom-0002", "mark": "not_that"},
    "trust.reset": {},
}
CARRY = {"capability": "bubble_carry", "center_m": IN_THE_OPEN, "radius_m": 0.08, "duration_s": 20, "target": "avatar:player"}


def refusal(result: dict):
    return "ok" if result["ok"] else (result["error"]["code"], result["error"].get("field_path"), result["error"]["message"])


class OnTheMock(unittest.TestCase):
    def setUp(self):
        self.host = new_host()
        self.n = 0

    def tearDown(self):
        for result in self.host.emitted:
            self.assertEqual(contract_problems(result), [], result)

    def send(self, principal, op, args, **extra):
        self.n += 1
        return self.host.handle(principal, command(op, args, f"m0-{self.n}", **extra))

    def test_the_new_ops_are_the_players_and_not_built_yet(self):
        for op, args in NEW_PLAYER_OPS.items():
            with self.subTest(op=op):
                self.assertEqual(refusal(self.send(COMPANION, op, args))[:2], ("permission_denied", "$.op"))
                self.assertEqual(refusal(self.send(PLAYER, op, args)), ("unsupported_capability", "$.op", mock_host.MECHANICS_NOT_YET))

    def test_a_difficulty_is_chosen_at_the_menu_and_the_mode_alone_still_changes_today(self):
        self.assertEqual(refusal(self.send(PLAYER, "world.set_mode", {"mode": "challenge", "difficulty": "brave"})),
                         ("unsupported_capability", "$.op", mock_host.DIFFICULTY_NOT_YET))
        # Until the teams packet (M3) refuses it after boot, the mode alone changes as before.
        self.assertTrue(self.send(PLAYER, "world.set_mode", {"mode": "challenge"})["ok"])
        self.assertEqual(self.host.mode, "challenge")

    def test_the_companion_never_gathers_a_fractal_and_the_answer_says_nothing_about_it(self):
        answers = []
        for target in ("fractal:k3x9q2", "fractal:zz9", "fractal:" + "q" * 32):
            for op, args in (("entity.grab", {"target": target, "actor": GUBBLE}),
                             ("goal.set", {"actor": GUBBLE, "goal": "fetch", "target": target}),
                             ("entity.place", {"target": target, "placement": {"position_m": IN_THE_OPEN}}),
                             ("entity.push", {"target": target, "distance_m": 0.2, "actor": GUBBLE})):
                with self.subTest(op=op, target=target):
                    answer = refusal(self.send(COMPANION, op, args))
                    self.assertEqual(answer, ("permission_denied", "$.args.target", mock_host.FRACTAL_REFUSAL))
                    answers.append(answer)
        self.assertEqual(len(set(answers)), 1, "the same answer whether or not the Fractal exists")
        # Finding is the Gubble's: it may go to one (here none is placed, so it is an unknown id like any other).
        self.assertEqual(refusal(self.send(COMPANION, "goal.set", {"actor": GUBBLE, "goal": "go_to", "target": "fractal:k3x9q2"}))[0],
                         "target_not_found")
        # The player is not refused for gathering: a Fractal no room places is simply not there.
        self.assertEqual(refusal(self.send(PLAYER, "entity.grab", {"target": "fractal:k3x9q2"}))[0], "target_not_found")

    def test_the_targets_rule(self):
        self.assertEqual(refusal(self.send(COMPANION, "nature.cast", CARRY))[:2], ("request_invalid", "$"), "a carry on an avatar is previewed first")
        self.assertEqual(refusal(self.send(COMPANION, "nature.cast", CARRY, preview=True)),
                         ("unsupported_capability", "$.op", mock_host.BUBBLES_NOT_YET))
        self.assertEqual(refusal(self.send(COMPANION, "nature.cast", {**CARRY, "preview_id": "a" * 32})),
                         ("unsupported_capability", "$.op", mock_host.BUBBLES_NOT_YET))
        for capability in ("bloom", "fire", "bubble_boat"):
            with self.subTest(capability=capability):
                self.assertEqual(refusal(self.send(COMPANION, "nature.cast", {**CARRY, "capability": capability}, preview=True))[:2],
                                 ("request_invalid", "$.args.capability"))
        self.assertEqual(refusal(self.send(COMPANION, "nature.cast", {**CARRY, "target": "obj:box"}, preview=True))[:2],
                         ("request_invalid", "$.args.target"))
        # Every other ability still refuses the player's avatar: Bubbles as an effect cannot be cast on the player.
        bubbles = self.send(COMPANION, "effect.start", {"capability": "bubbles", "params": {"intensity": 0.6},
                                                        "area": {"center_m": IN_THE_OPEN, "radius_m": 0.5}, "duration_s": 10,
                                                        "targets": ["avatar:player"]})
        self.assertFalse(bubbles["ok"], bubbles)

    def test_no_field_of_a_request_changes_a_price(self):
        """The price is a function of the ability and the principal alone (the mock reads it; M1 charges it)."""
        rules = load_rules(contracts(), rules_path(rules_version=5))
        self.assertEqual(list(inspect.signature(rules.price).parameters), ["capability", "principal"])
        self.assertEqual((rules.price("fire", PLAYER), rules.price("fire", COMPANION)), (6, 3))
        for smuggled in ("price", "cost", "ordered", "own_idea_factor", "directed_by", "ordered_by"):
            with self.subTest(field=smuggled):
                result = self.send(COMPANION, "nature.cast", {"capability": "bloom", "center_m": IN_THE_OPEN, "radius_m": 0.25,
                                                              "duration_s": 30, smuggled: 0}, preview=True)
                self.assertEqual(refusal(result)[0], "field_unknown")


class ThroughMcp(unittest.IsolatedAsyncioTestCase):
    async def test_no_tool_reaches_the_players_new_ops(self):
        async with McpHarness() as harness:
            tools = {tool.name for tool in (await harness.client.list_tools()).tools}
            for op in NEW_PLAYER_OPS:
                self.assertNotIn(op.replace(".", "_"), tools)
            self.assertIn("nature_cast", tools)

    async def test_the_adapter_refuses_an_unpreviewed_carry_and_a_fractal_grab_before_sending_anything(self):
        async with McpHarness() as harness:
            carry = await harness.call("nature_cast", {"action_id": "carry-1", **CARRY})
            self.assertFalse(carry["ok"], carry)
            self.assertEqual(carry["error"]["code"], "request_invalid")
            self.assertEqual(harness.host.emitted, [], "refused before anything reached the game")
            grab = await harness.call("entity_grab", {"action_id": "grab-1", "target": "fractal:k3x9q2"})
            self.assertEqual((grab["error"]["code"], grab["error"]["field_path"]), ("permission_denied", "$.args.target"))
            fetch = await harness.call("goal_set", {"action_id": "fetch-1", "goal": "fetch", "target": "fractal:k3x9q2"})
            self.assertEqual(fetch["error"]["code"], "permission_denied")
            self.assertEqual(harness.host.emitted, [], "refused before anything reached the game")


if __name__ == "__main__":
    unittest.main()
