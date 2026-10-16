"""The mechanics round's contract at the companion boundary (M0, 14 October 2026; docs/companion/SECURITY.md, the rows marked
planned for M0, and the row built with M9): the player's new ops have no tool and refuse the companion, a Fractal is never the
companion's to gather, the Bubbles carry lands on the player's avatar only under the targets rule (the preview or the step-in the
consent, the dive key or X the exit), and no field of a request changes a price.

test_host_alignment.py holds the companion's side of these on both hosts; this file holds the player's side on the mock, the
adapter's refusals before anything is sent, and the prices the mock reads from the pack.
"""
from __future__ import annotations

import inspect
import unittest

from mcp_harness import McpHarness
from support import COMPANION, GUBBLE, IN_THE_OPEN, PLAYER, command, contract_problems, contracts, new_host, query

from enfractal_companion import mock_host
from enfractal_companion.mock_host import load_rules, rules_path

NEW_PLAYER_OPS = {
    "item.pickup": {"target": "fractal:k3x9q2"},
    "item.gather": {"target": "obj:book"},
    "knack.set": {"slot": 1, "knack": "quick_feet"},
    "receipt.mark": {"action_id": "bloom-0002", "mark": "not_that"},
    "trust.reset": {},
    "fractal.spend": {"grow": "heart"},
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
        # M9: a carry on the player is centred where they stand; previewed, it answers the ghost's preview_id.
        self.assertEqual(refusal(self.send(COMPANION, "nature.cast", CARRY, preview=True))[:2], ("invalid_args", "$.args.center_m"))
        on_them = {**CARRY, "center_m": list(self.host.entities["avatar:player"].position)}
        previewed = self.send(COMPANION, "nature.cast", on_them, preview=True)
        self.assertTrue(previewed["ok"] and len(previewed["data"]["preview_id"]) == 32, previewed)
        self.assertEqual(refusal(self.send(COMPANION, "nature.cast", {**on_them, "preview_id": "a" * 32})),
                         ("revision_conflict", "$.args.preview_id", mock_host.BUBBLE_PREVIEW_AGAIN))
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


    def test_the_status_reads_the_packs_body_and_pool_as_the_kernel_does(self):
        """M1: under a pack with traits, player.status is the kernel's (VitalsRules.Fresh and PlayerStatus): five hearts in
        quarters, the first band, a pool of 8 (the shadow pool in Creative); without them, today's numbers and no new field."""
        host = new_host(rules_file=rules_path(rules_version=5))
        status = host.handle(COMPANION, query("player.status", {}))["data"]
        self.assertEqual(status, {"mode": "creative", "health": 100.0, "max_health": 100.0, "stamina": 0.4, "max_stamina": 0.4,
                                  "completed_climbs": 0, "recoveries": 0, "consequences": False,
                                  "energy": {"pips": 8, "max_pips": 8, "shadow": True},
                                  "hearts": {"quarters": 20, "max_quarters": 20}, "bar": {"bands": 1, "slowed": False}})
        self.assertTrue(host.handle(PLAYER, command("world.set_mode", {"mode": "challenge"}, "m1-mode"))["ok"])
        self.assertEqual(host.handle(COMPANION, query("player.status", {}))["data"]["energy"], {"pips": 8, "max_pips": 8, "shadow": False})
        for result in host.emitted:
            self.assertEqual(contract_problems(result), [], result)
        today = new_host(rules_file=rules_path(rules_version=4)).handle(COMPANION, query("player.status", {}))["data"]
        self.assertFalse({"energy", "hearts", "bar"} & set(today))
        self.assertEqual((today["max_health"], today["max_stamina"]), (100.0, 0.45))


class TheBubbleOnTheMock(unittest.TestCase):
    """M9 on the mock (no physics: a bubble neither drifts nor deflates; the kernel suite native_kernel_bubble holds the body's
    side, the deflate among it): the security notes' row, by name, and the receipt the player marks."""

    def setUp(self):
        self.host = new_host()
        self.n = 0
        self.feet = list(self.host.entities["avatar:player"].position)

    def tearDown(self):
        for result in self.host.emitted:
            self.assertEqual(contract_problems(result), [], result)

    def send(self, principal, op, args, **extra):
        self.n += 1
        return self.host.handle(principal, command(op, args, f"m9-{self.n}", **extra))

    def carry_args(self, capability="bubble_carry", **extra):
        return {"capability": capability, "center_m": self.feet, "radius_m": 0.08, "duration_s": 20, "target": "avatar:player", **extra}

    def carry(self, principal=COMPANION, capability="bubble_carry", wait=True):
        """Previewed, the ghost's lead waited out, then cast naming its preview: the commit's result."""
        preview = self.send(principal, "nature.cast", self.carry_args(capability), preview=True)
        if wait:
            self.host.clock.advance(mock_host.BUBBLE_GHOST_LEAD_S + 0.05)
        return self.send(principal, "nature.cast", self.carry_args(capability, preview_id=preview.get("data", {}).get("preview_id", "e" * 32)))

    def lookup(self, principal, committed):
        return self.host.handle(principal, query("receipt.lookup", {"action_id": committed["action_id"]}, f"q-{self.n}"))["data"]["receipt"]["data"]

    def test_a_companion_cast_never_targets_the_player_without_the_preview_or_the_step_in(self):
        unpreviewed = self.send(COMPANION, "nature.cast", self.carry_args())
        made_up = self.send(COMPANION, "nature.cast", self.carry_args(preview_id="d" * 32))
        at_the_feet = self.send(COMPANION, "nature.cast", {k: v for k, v in self.carry_args().items() if k != "target"})
        too_soon = self.carry(wait=False)
        self.assertEqual([refusal(r)[:2] for r in (unpreviewed, made_up, at_the_feet, too_soon)],
                         [("request_invalid", "$"), ("revision_conflict", "$.args.preview_id"), ("invalid_args", "$.args.center_m"),
                          ("not_ready", "$.args.preview_id")])
        self.assertEqual(refusal(too_soon)[2], mock_host.BUBBLE_GHOST_LEAD)
        self.assertFalse(self.host.bubbles, "no refusal forms a bubble")
        # Beside the player a bubble waits, carrying no one, until they step in.
        beside = self.send(COMPANION, "nature.cast", {"capability": "bubble_carry", "center_m": [self.feet[0] + 0.3, self.feet[1], self.feet[2] + 0.1],
                                                       "radius_m": 0.08, "duration_s": 20})
        self.assertTrue(beside["ok"] and beside["transient"] and beside["data"]["carried"] is False and "target" not in beside["data"], beside)
        self.assertTrue(self.host.player_steps_into(beside["data"]["bubble"]))
        self.assertIs(self.lookup(COMPANION, beside)["carried"], True)

    def test_the_dive_key_pops_it_at_any_moment(self):
        carry = self.carry()
        self.assertTrue(carry["ok"] and carry["transient"] and carry["created"] == [carry["data"]["bubble"]], carry)
        self.assertEqual(carry["affected"], [carry["data"]["bubble"], "avatar:player"])
        self.assertTrue(self.host.player_pops_bubble())
        self.assertEqual(self.lookup(COMPANION, carry)["ended"], "popped_by_player")
        self.assertFalse(self.host.bubbles)

    def test_the_companion_cannot_keep_a_player_in_a_bubble_the_player_wants_out_of(self):
        carry = self.carry()
        held = self.send(COMPANION, "nature.cast", self.carry_args(), preview=True)
        self.assertEqual(refusal(held)[:2], ("target_busy", "$.args.target"), "no second offer while one holds them")
        x = self.send(PLAYER, "goal.stop", {"actor": GUBBLE})
        self.assertIn(carry["data"]["bubble"], x["affected"])
        self.assertEqual(self.lookup(COMPANION, carry)["ended"], "popped_by_player", "X pops it, popped by the player")
        again = self.send(COMPANION, "nature.cast", self.carry_args(), preview=True)
        self.assertEqual(refusal(again), ("not_ready", "$.args.target", mock_host.BUBBLE_POP_RESPECT), "the Gubble waits after a no")
        self.host.clock.advance(mock_host.BUBBLE_POP_RESPECT_S)
        later = self.carry()
        self.assertTrue(later["ok"], later)
        # The Gubble can never stop the player's own exit, and its stops pop only its own bubbles.
        mine = self.send(PLAYER, "nature.cast", {"capability": "bubble_carry", "center_m": [self.feet[0] + 0.3, self.feet[1], self.feet[2] + 0.1],
                                                  "radius_m": 0.08, "duration_s": 20})
        theirs = self.send(COMPANION, "effect.stop", {"effect": mine["data"]["bubble"]})
        self.assertEqual(theirs["data"]["effects_stopped"], 0)
        self.assertTrue(self.host.player_pops_bubble())

    def test_every_other_ability_still_refuses_the_players_avatar(self):
        outcome = {}
        for capability in ("bloom", "fire", "bubble_boat"):
            outcome[capability] = refusal(self.send(COMPANION, "nature.cast", {**self.carry_args(), "capability": capability}, preview=True))[:2]
        outcome["bubbles"] = refusal(self.send(COMPANION, "effect.start", {"capability": "bubbles", "params": {}, "area": {"center_m": self.feet, "radius_m": 0.5},
                                                                           "duration_s": 10, "targets": ["avatar:player"]}))[:2]
        outcome["directing"] = refusal(self.send(COMPANION, "goal.set", {"actor": "avatar:player", "goal": "go_to", "position_m": IN_THE_OPEN}))[:2]
        outcome["the Gubble"] = refusal(self.send(COMPANION, "nature.cast", {**self.carry_args(), "target": GUBBLE}, preview=True))[:2]
        self.assertEqual(outcome, {"bloom": ("request_invalid", "$.args.capability"), "fire": ("request_invalid", "$.args.capability"),
                                   "bubble_boat": ("request_invalid", "$.args.capability"), "bubbles": ("permission_denied", "$.args.targets"),
                                   "directing": ("actor_denied", "$.args.actor"), "the Gubble": ("invalid_args", "$.args.target")})

    def test_a_bubbles_receipt_says_how_it_ended(self):
        """The receipt the player marks (M8): the kind, the target, carried, and ended: timed out, popped by the Gubble's own stop."""
        timed = self.carry()
        self.host.clock.advance(21)
        self.assertEqual(self.lookup(COMPANION, timed)["ended"], "timed_out")
        self.host.clock.advance(mock_host.BUBBLE_POP_RESPECT_S)
        lift = self.carry(capability="bubble_lift")
        self.send(COMPANION, "goal.stop", {})
        ended = self.lookup(COMPANION, lift)
        self.assertEqual((ended["capability"], ended["target"], ended["carried"], ended["ended"]), ("bubble_lift", "avatar:player", True, "popped"))
        # A replay answers the receipt as it stands now, and a boat is refused on the box world's dry floor.
        replay = self.host.handle(COMPANION, command("nature.cast", self.carry_args("bubble_lift", preview_id="f" * 32), lift["action_id"]))
        self.assertEqual(replay["error"]["code"], "action_id_conflict")
        boat = self.send(COMPANION, "nature.cast", {"capability": "bubble_boat", "center_m": IN_THE_OPEN, "radius_m": 0.12, "duration_s": 60})
        self.assertEqual(refusal(boat), ("invalid_args", "$.args.center_m", mock_host.BUBBLE_NO_WATER))


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
