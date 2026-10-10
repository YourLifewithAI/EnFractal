"""Glow, the Gubble's first ability (docs/runs/RUN-2-GLOW.md section 5): the companion's boundary on the mock host and
through the MCP surface.

test_host_alignment.py runs the steps the link can drive (the companion's casts, refusals, caps, expiry) on both hosts
with the same expected outcomes. This file adds what only the mock can stage, each against the kernel host's rules
(CommandHostEffects.cs): other packs (cast_by, targets, tiers, packs that fail), the player's own casts and stops, a
second companion, the engine's cap, the team's sight turned off, no Gubble in the room; and the same boundary through
MCP tool calls, where reserved parameter names never leave the adapter.
"""
from __future__ import annotations

import copy
import dataclasses
import json
import tempfile
import unittest
from pathlib import Path

from support import (BEHIND_BOX, COMPANION, GUBBLE, IN_THE_OPEN, NO_HOLDS, PLAYER, REPO, FakeClock, HostPolicy, command,
                     contract_problems, glow, new_host, query)

from mcp_harness import McpHarness

from enfractal_companion.mock_host import RulesError, rules_path
from enfractal_companion.server import INSTRUCTIONS

PACK = json.loads(rules_path().read_bytes())
SPOT_ONLY_THE_PLAYER_SEES = [0.6, 0.0, 0.2]  # with the companion behind the box (below), the box hides it from the companion
BEHIND_THE_BOX = [1.6, 0.0, 0.2]


def variant(**changes) -> dict:
    """The shipped pack with its glow changed."""
    pack = copy.deepcopy(PACK)
    pack["abilities"][0].update(changes)
    return pack


class GlowCase(unittest.TestCase):
    policy: HostPolicy | None = None

    def setUp(self):
        self.clock = FakeClock()
        self.host = new_host(policy=self.policy, clock=self.clock)

    def tearDown(self):
        for result in self.host.emitted:
            self.assertEqual(contract_problems(result), [], result)

    def send(self, message, principal=COMPANION):
        return self.host.handle(principal, message)

    def player(self, message):
        return self.host.player_command(message)

    def assertRefused(self, result, code, field_path=None):
        self.assertFalse(result["ok"], result)
        self.assertEqual(result["error"]["code"], code, result)
        if field_path is not None:
            self.assertEqual(result["error"].get("field_path"), field_path, result)


class TheIslandsRules(GlowCase):
    def test_cast_by_withholds_a_caster(self):
        self.host.use_rules(variant(cast_by=["player"]))
        self.assertRefused(self.send(glow("c-1")), "permission_denied", "$.args.capability")
        self.assertTrue(self.player(glow("p-1"))["ok"])
        self.host.use_rules(variant(cast_by=["companion"]))
        self.assertEqual(self.host.active_effect_ids(), [], "new rules end the glows started under the old")
        self.assertRefused(self.player(glow("p-2")), "permission_denied", "$.args.capability")
        self.assertTrue(self.send(glow("c-2"))["ok"])

    def test_an_ability_without_self_or_without_point(self):
        self.host.use_rules(variant(targets=["point"]))
        refused = self.send(glow("c-1"))
        self.assertRefused(refused, "invalid_args", "$.args.targets")
        self.assertEqual(refused["error"]["allowed"], ["point"])
        self.assertTrue(self.send(glow("c-2", at=IN_THE_OPEN))["ok"])
        self.host.use_rules(variant(targets=["self"]))
        self.assertRefused(self.send(glow("c-3", at=IN_THE_OPEN)), "invalid_args", "$.args.targets")
        self.assertTrue(self.send(glow("c-4"))["ok"])

    def test_the_bounds_are_the_packs(self):
        self.host.use_rules(variant(duration_max_s=400, area_radius_max_m=1.0,
                                    params={"intensity": {"min": 0.3, "max": 0.5, "default": 0.4}}))
        self.assertRefused(self.send(glow("c-1", params={}, duration=450)), "invalid_args", "$.args.duration_s")
        self.assertRefused(self.send(glow("c-2", params={}, radius=1.2)), "invalid_args", "$.args.area.radius_m")
        too_bright = self.send(glow("c-3", params={"intensity": 0.6}))
        self.assertEqual(too_bright["error"]["allowed"], [0.3, 0.5])
        listed = self.send(query("capabilities.list", {}))["data"]["items"]
        self.assertEqual(listed, [{"capability": "glow", "category": "light", "params": {"intensity": {"min": 0.3, "max": 0.5}},
                                   "area_radius_max_m": 1.0, "duration_max_s": 400}])
        started = self.send(glow("c-4", params={}))
        self.assertEqual(started["data"]["params"], {"intensity": 0.4})

    def test_a_pack_that_fails_its_checks_leaves_no_abilities(self):
        broken = {
            "a reserved param name": variant(params={"owner": {"min": 0, "max": 1, "default": 0},
                                                     "intensity": {"min": 0.2, "max": 1.0, "default": 0.6}}),
            "an intensity over light.emit's 2.0": variant(params={"intensity": {"min": 0.2, "max": 2.5, "default": 0.6}}),
            "max_active over the engine's 8": variant(max_active=9),
            "a reach over light.emit's 5 m": variant(reach_m=6),
            "a default outside its range": variant(params={"intensity": {"min": 0.2, "max": 1.0, "default": 1.2}}),
            "a companion that is not a principal": variant(cast_by=["companion", "guest"]),
        }
        for label, pack in broken.items():
            with self.subTest(label):
                with self.assertRaises(RulesError):
                    self.host.use_rules(pack)
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "rules" / "storybook_wild" / "v1.json"
            path.parent.mkdir(parents=True)
            path.write_bytes(rules_path().read_bytes().replace(b'"reach_m": 2.0,', b'"reach_m": 2.0, "reach_m": 4.0,'))
            self.host.load_rules(path)  # a duplicate key: refused, never a crash
        self.assertIsNone(self.host.rules)
        self.assertIn("did not load", self.host.rules_notice)
        self.assertRefused(self.send(glow("c-1")), "unsupported_capability", "$.args.capability")
        self.assertEqual(self.send(query("capabilities.list", {}))["data"], {"items": []})
        self.assertTrue(self.send(command("effect.stop", {"effect": "all"}, "s-1"))["ok"], "stops still apply")
        self.host.load_rules(REPO / "game" / "rules" / "storybook_wild" / "v9.json")
        self.assertIsNone(self.host.rules)
        self.host.load_rules(rules_path())
        self.assertTrue(self.send(glow("c-2"))["ok"])

    def test_a_keyed_yes_ability_waits_for_the_players_click_even_with_nothing_else_held(self):
        self.host.policy = NO_HOLDS
        self.host.use_rules(variant(tier="keyed_yes"))
        self.assertRefused(self.send(glow("c-1", params={"intensity": 1.4})), "invalid_args")  # checked before it is held
        held = self.send(glow("c-2"))
        self.assertRefused(held, "approval_required")
        self.assertEqual(held["approval_needed"]["reason"], "The companion asks to use glow. Approve or deny it in the game.")
        self.assertEqual(self.host.active_effect_ids(), [])
        decision = self.host.player_decide(held["approval_needed"]["request_id"], approve=True)
        self.assertEqual(decision["state"], "approved")
        self.assertEqual((decision["result"]["approved_by"], decision["result"]["principal"]), (PLAYER, COMPANION))
        self.assertEqual(self.host.active_effect_ids(), decision["result"]["created"])
        self.assertTrue(self.player(glow("p-1", at=IN_THE_OPEN))["ok"], "the player's own runs at once")


class TheGubbleCarriesItOut(GlowCase):
    def test_the_player_may_cast_only_on_the_gubble_or_a_spot(self):
        for targets in (["avatar:player"], ["obj:rug"], ["obj:nothing_here"], [GUBBLE, "obj:rug"]):
            with self.subTest(targets=targets):
                self.assertRefused(self.player(glow(f"p-{len(targets)}-{targets[-1][-3:]}", targets=targets)),
                                   "invalid_args", "$.args.targets")
        self.assertEqual(self.host.active_effect_ids(), [])

    def test_no_gubble_in_the_room_is_not_ready(self):
        self.host.entities[GUBBLE].removed = True
        result = self.player(glow("p-1", at=IN_THE_OPEN))
        self.assertRefused(result, "not_ready")
        self.assertTrue(result["error"]["retryable"])

    def test_max_active_counts_both_principals_then_the_engine_caps_the_room(self):
        self.assertTrue(self.player(glow("p-1"))["ok"])
        self.assertTrue(self.player(glow("p-2", at=IN_THE_OPEN))["ok"])
        self.assertTrue(self.send(glow("c-1"))["ok"])
        for result in (self.send(glow("c-2")), self.player(glow("p-3"))):
            self.assertRefused(result, "budget_exceeded", "$.args.capability")
            self.assertEqual((result["error"]["allowed"], result["error"]["actual"]), (3, 3))
        self.player(command("effect.stop", {"effect": "all"}, "p-stop"))
        self.host.policy = dataclasses.replace(self.host.policy, max_active_effects=2)
        self.assertTrue(self.send(glow("c-3"))["ok"])
        self.assertTrue(self.player(glow("p-4"))["ok"])
        capped = self.send(glow("c-4", at=IN_THE_OPEN))
        self.assertRefused(capped, "budget_exceeded", "$.args.capability")
        self.assertEqual(capped["error"]["allowed"], 2)

    def test_a_companion_stops_only_its_own_glows_and_the_player_any(self):
        host = new_host(clock=self.clock, extra_companions={"companion:other": "avatar:other"})
        mine = host.handle(COMPANION, glow("c-1"))["created"][0]
        theirs = host.handle("companion:other", glow("o-1", targets=["avatar:other"]))["created"][0]
        players = host.player_command(glow("p-1", at=IN_THE_OPEN))["created"][0]
        self.assertEqual(host.handle(COMPANION, glow("c-2", targets=["avatar:other"]))["error"]["code"], "permission_denied")
        for which in (theirs, players, "all"):
            stop = host.handle(COMPANION, command("effect.stop", {"effect": which}, f"s-{which[-3:]}"))
            self.assertTrue(stop["ok"], stop)
        self.assertEqual(host.active_effect_ids(), [theirs, players])
        self.assertNotIn(mine, host.active_effect_ids())
        self.assertNotIn(players, host.handle(COMPANION, command("goal.stop", {}, "s-goal"))["affected"])
        everything = host.player_command(command("effect.stop", {"effect": "all"}, "p-stop"))
        self.assertEqual((everything["affected"], everything["data"]["effects_stopped"]), ([theirs, players], 2))
        for result in host.emitted:
            self.assertEqual(contract_problems(result), [], result)

    def test_a_preview_starts_nothing_and_a_previewed_stop_stops_nothing(self):
        preview = self.send(glow("c-1", at=IN_THE_OPEN, preview=True))
        self.assertTrue(preview["ok"] and preview["preview"], preview)
        self.assertEqual(self.host.active_effect_ids(), [])
        light = self.send(glow("c-2"))["created"][0]
        for message in (command("effect.stop", {"effect": "all"}, "s-1", preview=True),
                        command("goal.stop", {}, "s-2", preview=True)):
            result = self.send(message)
            self.assertTrue(result["ok"] and result["preview"], result)
            self.assertNotIn("affected", result)
        self.assertEqual(self.host.active_effect_ids(), [light])

    def test_glows_are_not_entities(self):
        before = (self.host.revision, copy.deepcopy(self.host._snapshot()))
        light = self.send(glow("c-1"))["created"][0]
        wisp = self.send(glow("c-2", at=IN_THE_OPEN))["created"][0]
        listed = json.dumps([self.send(query("entities.list", {}, "q-1")),
                             self.send(query("observe", {"actor": GUBBLE}, "q-2")),
                             self.send(query("room.describe", {}, "q-3")),
                             self.send(query("map.find", {"name": "glow"}, "q-4"))])
        self.assertNotIn(light, listed)
        self.assertNotIn(wisp, listed)
        self.assertRefused(self.send(query("entity.inspect", {"target": light}, "q-5")), "target_not_found")
        self.assertEqual((self.host.revision, self.host._snapshot()), before, "glows change no saved state")


class TheTeamsSight(GlowCase):
    def setUp(self):
        super().setUp()
        self.host.move_avatar(GUBBLE, BEHIND_THE_BOX)

    def test_with_the_teams_sight_the_companion_lights_a_spot_only_the_player_sees(self):
        self.assertNotIn("obj:book", self.host.perceived(COMPANION))  # the box is between them
        self.assertTrue(self.send(glow("c-1", at=SPOT_ONLY_THE_PLAYER_SEES))["ok"])


class OneAvatarsSight(GlowCase):
    policy = HostPolicy(shared_sight=False, team_sight_interval_s=0)

    def test_without_it_only_the_casters_own_eyes_count(self):
        self.host.move_avatar(GUBBLE, BEHIND_THE_BOX)
        self.assertRefused(self.send(glow("c-1", at=SPOT_ONLY_THE_PLAYER_SEES)), "target_not_found", "$.args.area.center_m")
        self.assertTrue(self.player(glow("p-1", at=SPOT_ONLY_THE_PLAYER_SEES))["ok"], "the player's own eyes count for the player")


class GlowThroughMcp(unittest.IsolatedAsyncioTestCase):
    def assertRefused(self, result, code, field_path=None):
        self.assertFalse(result["ok"], result)
        self.assertEqual(result["error"]["code"], code, result)
        if field_path is not None:
            self.assertEqual(result["error"].get("field_path"), field_path, result)

    @staticmethod
    def args(action_id, **kwargs) -> dict:
        message = glow(action_id, **kwargs)
        return {"action_id": action_id, **message["args"], **{k: v for k, v in message.items() if k == "preview"}}

    async def test_the_companion_glows_itself_and_a_spot_and_stops_them(self):
        async with McpHarness() as h:
            listed = await h.call("capabilities_list", {})
            self.assertEqual([i["capability"] for i in listed["data"]["items"]], ["glow"])
            own = await h.call("effect_start", self.args("g-1", params={}))
            spot = await h.call("effect_start", self.args("g-2", at=IN_THE_OPEN))
            self.assertEqual((own["data"]["target"], spot["data"]["target"]), ("self", "point"))
            stop = await h.call("effect_stop", {"effect": spot["created"][0]})
            self.assertEqual((stop["affected"], stop["data"]["effects_stopped"]), (spot["created"], 1))
            ended = await h.call("goal_stop", {})
            self.assertIn(own["created"][0], ended["affected"])
            self.assertEqual(h.host.active_effect_ids(), [])

    async def test_the_boundary_holds_through_tool_calls(self):
        async with McpHarness() as h:
            players = h.host.player_command(glow("p-1", at=IN_THE_OPEN))["created"][0]
            for i, (targets, code) in enumerate(((["avatar:player"], "permission_denied"), (["obj:rug"], "permission_denied"),
                                                 ([players], "target_not_found"))):
                with self.subTest(targets=targets):
                    self.assertRefused(await h.call("effect_start", self.args(f"t-{i}", targets=targets)), code, "$.args.targets")
            self.assertRefused(await h.call("effect_start", self.args("b-1", params={"intensity": 1.5})),
                               "invalid_args", "$.args.params.intensity")
            self.assertRefused(await h.call("effect_start", self.args("b-2", at=BEHIND_BOX)),
                               "target_not_found", "$.args.area.center_m")
            for i in range(2):
                self.assertTrue((await h.call("effect_start", self.args(f"g-{i}")))["ok"])
            self.assertRefused(await h.call("effect_start", self.args("g-2")), "budget_exceeded")
            mine = await h.call("effect_stop", {"effect": "all"})
            self.assertEqual(mine["data"]["effects_stopped"], 2)
            self.assertEqual(h.host.active_effect_ids(), [players], "the player's glow outlasts the companion's stops")
            self.assertEqual((await h.call("effect_stop", {"effect": players}))["data"]["effects_stopped"], 0)

    async def test_reserved_parameter_names_never_leave_the_adapter(self):
        async with McpHarness() as h:
            for name in ("principal", "owner", "owner_id", "approval", "approval_id", "approved", "grant", "role"):
                with self.subTest(name=name):
                    self.assertRefused(await h.call("effect_start", self.args(f"r-{name}", params={name: 1})),
                                       "field_unknown", f"$.params.{name}")
            for name in ("actor", "room_id", "action_id"):
                with self.subTest(name=name):
                    self.assertRefused(await h.call("effect_start", self.args(f"r-{name}", params={name: 1})),
                                       "request_invalid", "$.args.params")
            self.assertEqual(h.host.emitted, [], "nothing reached the game")

    async def test_the_tools_say_what_self_and_point_mean(self):
        async with McpHarness() as h:
            tools = {tool.name: tool for tool in (await h.client.list_tools()).tools}
        start = tools["effect_start"]
        for words in ("self", "point", "your own avatar", "area.center_m", "capabilities_list", "effect_stop"):
            self.assertIn(words, start.description)
        properties = start.input_schema["properties"]
        self.assertIn("avatar:companion", properties["targets"]["description"])
        self.assertIn("capabilities_list", properties["capability"]["description"])
        self.assertIn("default", properties["params"]["description"])
        self.assertIn("effect_start", tools["effect_stop"].input_schema["properties"]["effect"]["description"])
        self.assertIn("glow", tools["capabilities_list"].description)
        self.assertIn("Glow is a light", INSTRUCTIONS)


if __name__ == "__main__":
    unittest.main()
