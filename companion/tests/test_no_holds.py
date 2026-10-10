"""Founder decision 2: ideally no approval clicks. Every safety property must hold with an empty held set.

The suites that do not depend on holding run again here under NO_HOLDS, and the new cases cover
what an unheld companion could otherwise do: undo the player's locks, outlast the player's stop,
outspend the budgets or slip past revisions. After every test, nothing may have been held.
"""
from __future__ import annotations

import dataclasses
import tempfile
from pathlib import Path

import test_host_boundary as base
import test_host_review_fixes as fixes
import test_perception as perception_tests
import test_perception_memory as memory_tests
from support import (COMPANION, IN_THE_OPEN, NO_HOLDS, PLAYER, HostPolicy, command, example, glow, new_host, query, retarget,
                     GARAGE_TO_TEST_ROOM)


class NoHoldsCase(base.HostCase):
    policy = NO_HOLDS

    def tearDown(self):
        super().tearDown()
        self.assertEqual(self.host.approvals, {}, "nothing may be held with an empty held set")

    def lock_as_player(self, target):
        result = self.host.player_command(command("protect.lock", {"targets": [target]}, f"p-lock-{target[4:]}",
                                                  expected_entities={target: self.host.entities[target].revision}))
        self.assertTrue(result["ok"], result)
        return result


# The existing boundary suites, unchanged, with nothing held.
class PrincipalSmugglingWithNothingHeld(NoHoldsCase, base.PrincipalSmuggling):
    pass


class UntrustedTextWithNothingHeld(NoHoldsCase, base.UntrustedText):
    pass


class UnknownEntitiesWithNothingHeld(NoHoldsCase, base.UnknownEntities):
    pass


class StaleRevisionsWithNothingHeld(NoHoldsCase, base.StaleRevisions):
    pass


class ReplayWithNothingHeld(NoHoldsCase, base.Replay):
    pass


class PlayerOnlyWithNothingHeld(NoHoldsCase, base.PlayerOnly):
    pass


class ObservationWithNothingHeld(NoHoldsCase, base.Observation):
    pass


class SizeLimitsWithNothingHeld(NoHoldsCase, base.SizeLimits):
    pass


class NothingHeld(NoHoldsCase):
    def test_destructive_commands_commit_without_a_click(self):
        result = self.send(command("entity.remove", {"target": "obj:box"}, "rm-1", expected_entities={"obj:box": 0}))
        self.assertTrue(result["ok"], result)
        self.assertNotIn("approved_by", result)
        self.assertTrue(self.host.entities["obj:box"].removed)

    def test_companion_undo_cannot_unlock_the_players_lock(self):
        self.lock_as_player("obj:box")
        result = self.send(command("room.undo", {"to_revision": 0}, "undo-1", expected_revision=1))
        self.assertRefused(result, "target_protected", "$.args.to_revision")
        self.assertTrue(self.host.entities["obj:box"].protected)
        self.assertEqual(self.host.entities["obj:box"].protected_by, PLAYER)

    def test_companion_undo_cannot_bring_back_a_lock_the_player_removed(self):
        self.lock_as_player("obj:box")
        unlocked = self.host.player_command(command("protect.unlock", {"targets": ["obj:box"]}, "p-unlock",
                                                    expected_entities={"obj:box": 1}))
        self.assertTrue(unlocked["ok"])
        result = self.send(command("room.undo", {"to_revision": 1}, "undo-1", expected_revision=2))
        self.assertRefused(result, "target_protected")
        self.assertFalse(self.host.entities["obj:box"].protected)

    def test_companion_undo_cannot_move_a_protected_entity_back(self):
        self.host.player_command(command("entity.place", {"target": "obj:box", "placement": {"position_m": [1.2, 0, 0.3]}},
                                         "p-move"))
        self.lock_as_player("obj:box")
        result = self.send(command("room.undo", {"to_revision": 0}, "undo-1", expected_revision=2))
        self.assertRefused(result, "target_protected")
        self.assertEqual(self.host.entities["obj:box"].position, [1.2, 0, 0.3])

    def test_companion_undo_of_unprotected_changes_still_works(self):
        self.lock_as_player("obj:box")
        self.send(command("entity.place", {"target": "obj:book", "placement": {"position_m": [0.3, 0, 0.3]}}, "move-1"))
        result = self.send(command("room.undo", {"to_revision": 1}, "undo-1", expected_revision=2))
        self.assertTrue(result["ok"], result)
        self.assertEqual(result["affected"], ["obj:book"])
        self.assertEqual(self.host.entities["obj:book"].position, [0.45, 0.0, 0.1])
        self.assertTrue(self.host.entities["obj:box"].protected)

    def test_locks_resist_every_changing_command(self):
        self.lock_as_player("obj:box")
        source = retarget(example("command_transform_bean_bag_into_glider"), GARAGE_TO_TEST_ROOM)["args"]["into"]
        for message in (command("entity.remove", {"target": "obj:box"}, "rm-1", expected_entities={"obj:box": 1}),
                        command("entity.transform", {"target": "obj:box", "into": source}, "tf-1",
                                expected_entities={"obj:box": 1}),
                        command("entity.place", {"target": "obj:box", "placement": {"position_m": [0, 0, 0]}}, "pl-1"),
                        command("entity.grab", {"target": "obj:box"}, "gr-1")):
            with self.subTest(op=message["op"]):
                self.assertRefused(self.send(message), "target_protected")
        self.assertTrue(self.host.entities["obj:box"].protected)

    def test_the_players_stop_stops_the_companions_goals_and_effects(self):
        light = self.send(glow("glow-1", radius=1.5, duration=600))["created"][0]
        self.send(command("goal.set", {"actor": "avatar:companion", "goal": "wander"}, "g-1"))
        stop = self.host.player_command(command("goal.stop", {}, "p-stop"))
        self.assertTrue(stop["ok"])
        self.assertIn(light, stop["affected"])
        self.assertEqual(stop["data"]["effects_stopped"], 1)
        self.assertNotIn("avatar:companion", self.host.goals)
        self.assertEqual(self.host.active_effect_ids(), [])

    def test_the_players_effect_stop_stops_companion_effects_all_or_by_id(self):
        first, second = (self.send(glow(f"glow-{i}", at=IN_THE_OPEN))["created"][0] for i in range(2))
        by_id = self.host.player_command(command("effect.stop", {"effect": first}, "p-stop-1"))
        self.assertEqual((by_id["affected"], by_id["data"]), ([first], {"effects_stopped": 1}))
        all_of_them = self.host.player_command(command("effect.stop", {"effect": "all"}, "p-stop-2"))
        self.assertEqual(all_of_them["affected"], [second])
        self.assertEqual(self.host.active_effect_ids(), [])

    def test_the_companion_cannot_stop_the_players_effects(self):
        players = self.host.player_command(glow("p-glow", at=IN_THE_OPEN))["created"][0]
        for which in ("all", players):
            with self.subTest(which=which):
                result = self.send(command("effect.stop", {"effect": which}, f"stop-{which[-4:]}"))
                self.assertTrue(result["ok"], result)  # a stop always applies, and stops nothing it may not
                self.assertNotIn("affected", result)
                self.assertEqual(result["data"], {"effects_stopped": 0})
        stop = self.send(command("goal.stop", {}, "stop-goal"))
        self.assertNotIn(players, stop["affected"])
        self.assertEqual(self.host.active_effect_ids(), [players])

    def test_effect_and_creation_budgets_hold(self):
        self.host.policy = dataclasses.replace(self.host.policy, max_creations=1)
        for i in range(3):  # the pack's max_active for glow
            self.assertTrue(self.send(glow(f"glow-{i}", params={"intensity": 1}, radius=1))["ok"])
        self.assertRefused(self.send(glow("glow-3", params={"intensity": 1}, radius=1)), "budget_exceeded")
        source = example("command_creation_place_trigger_light")["args"]["source"]
        first = self.send(command("creation.place", {"source": source, "placement": {"position_m": [0.5, 0, 0.5]}}, "c-1"))
        self.assertTrue(first["ok"], first)
        self.assertRefused(self.send(command("creation.place", {"source": source, "placement": {"position_m": [0.6, 0, 0.5]}},
                                             "c-2")), "budget_exceeded")

    def test_style_set_still_refuses_an_unpinnable_preset(self):
        with tempfile.TemporaryDirectory() as tmp:
            self.host = new_host(self.policy, styles_dir=fixes.candidate_styles(Path(tmp)))
            result = self.send(command("style.set", {"preset_id": "sketchbook", "preset_version": 1}, "style-1"))
            self.assertRefused(result, "invalid_args", "$.args.preset_version")


class UndoIsOnlyForTheCompanionsOwnChanges(NoHoldsCase):
    """Review finding 1 and LIVE-VOICE's "never exposed" list: with nothing held, a companion's room.undo
    may neither touch protection nor step back over the player's changes."""

    def place_as_player(self, target, position, action_id):
        result = self.host.player_command(command("entity.place", {"target": target, "placement": {"position_m": position}},
                                                  action_id))
        self.assertTrue(result["ok"], result)

    def test_companion_undo_cannot_undo_the_players_changes(self):
        self.place_as_player("obj:book", [0.3, 0, 0.3], "p-move")
        before = self.world()
        result = self.send(command("room.undo", {"to_revision": 0}, "undo-1", expected_revision=1))
        self.assertRefused(result, "permission_denied", "$.args.to_revision")
        self.assertEqual(self.world(), before)
        self.assertEqual(self.host.entities["obj:book"].position, [0.3, 0, 0.3])

    def test_companion_undo_cannot_take_away_the_players_creation(self):
        sample = example("command_creation_place_trigger_light")["args"]["source"]
        placed = self.host.player_command(command("creation.place", {"source": sample,
                                                                     "placement": {"position_m": [0.5, 0, 0.5]}}, "p-place"))
        self.send(command("entity.place", {"target": "obj:book", "placement": {"position_m": [0.3, 0, 0.3]}}, "move-1"))
        result = self.send(command("room.undo", {"to_revision": 0}, "undo-1", expected_revision=2))
        self.assertRefused(result, "permission_denied")
        self.assertFalse(self.host.entities[placed["created"][0]].removed)

    def test_companion_undo_steps_back_over_its_own_changes_only(self):
        self.place_as_player("obj:rug", [0.1, 0, 0.6], "p-move")
        self.send(command("entity.place", {"target": "obj:book", "placement": {"position_m": [0.3, 0, 0.3]}}, "move-1"))
        self.send(command("entity.place", {"target": "obj:box", "placement": {"position_m": [1.2, 0, 0.3]}}, "move-2"))
        self.assertRefused(self.send(command("room.undo", {"to_revision": 0}, "undo-0", expected_revision=3)),
                           "permission_denied")
        result = self.send(command("room.undo", {"to_revision": 1}, "undo-1", expected_revision=3))
        self.assertTrue(result["ok"], result)
        self.assertEqual(result["affected"], ["obj:book", "obj:box"])
        self.assertEqual(self.host.entities["obj:rug"].position, [0.1, 0, 0.6])

    def test_companion_undo_cannot_remove_even_its_own_lock(self):
        locked = self.send(command("protect.lock", {"targets": ["obj:box"]}, "lock-1", expected_entities={"obj:box": 0}))
        self.assertTrue(locked["ok"])
        result = self.send(command("room.undo", {"to_revision": 0}, "undo-1", expected_revision=1))
        self.assertRefused(result, "target_protected")
        self.assertTrue(self.host.entities["obj:box"].protected)

    def test_the_players_own_undo_is_unrestricted(self):
        self.send(command("protect.lock", {"targets": ["obj:box"]}, "lock-1", expected_entities={"obj:box": 0}))
        self.place_as_player("obj:book", [0.3, 0, 0.3], "p-move")
        result = self.host.player_command(command("room.undo", {"to_revision": 0}, "p-undo", expected_revision=2))
        self.assertTrue(result["ok"], result)
        self.assertFalse(self.host.entities["obj:box"].protected)


class StopsAndTheLedgerWithNothingHeld(NoHoldsCase):
    """Review findings 2 and 3 with nothing held: stops apply whatever the ledger and the rate limits say,
    the player's stop reaches the companion, and the companion cannot spend the player's share of the ledger."""

    policy = dataclasses.replace(NO_HOLDS, max_durable_receipts=4, player_receipt_reserve=2)

    def lock(self, target, action_id, principal=COMPANION):
        return self.host.handle(principal, command("protect.lock", {"targets": [target]}, action_id,
                                                   expected_entities={target: self.host.entities[target].revision}))

    def light(self, action_id):
        result = self.send(glow(action_id, params={"intensity": 1}, radius=1.5, duration=600))
        self.assertTrue(result["ok"], result)
        return result["created"][0]

    def test_the_companion_cannot_spend_the_players_share_of_the_ledger(self):
        self.assertTrue(self.lock("obj:box", "lock-1")["ok"])
        self.assertTrue(self.lock("obj:book", "lock-2")["ok"])
        self.assertRefused(self.lock("obj:rug", "lock-3"), "receipt_limit")
        self.assertTrue(self.lock("obj:rug", "p-lock-1", PLAYER)["ok"])  # the player's own lock still lands
        self.assertTrue(self.lock("obj:doorstop", "p-lock-2", PLAYER)["ok"])
        self.assertRefused(self.lock("obj:table", "p-lock-3", PLAYER), "receipt_limit")

    def test_stops_apply_with_the_ledger_full_and_the_rate_budget_spent(self):
        light = self.light("glow-1")
        self.send(command("goal.set", {"actor": "avatar:companion", "goal": "wander"}, "g-1"))
        self.lock("obj:box", "lock-1")
        self.lock("obj:book", "lock-2")
        self.lock("obj:rug", "p-lock-1", PLAYER)
        self.lock("obj:doorstop", "p-lock-2", PLAYER)
        for i in range(40):  # spend the companion's budget; the player has none to spend
            self.send(command("goal.set", {"actor": "avatar:companion", "goal": "stay"}, f"flood-{i}"))
            self.host.player_command(command("goal.set", {"actor": "avatar:player", "goal": "stay"}, f"p-flood-{i}"))
        self.assertRefused(self.send(command("goal.set", {"actor": "avatar:companion", "goal": "stay"}, "late")),
                           "rate_limited")
        self.assertRefused(self.lock("obj:table", "p-lock-3", PLAYER), "receipt_limit")  # never rate limited
        stop = self.host.player_command(command("goal.stop", {}, "p-stop"))
        self.assertTrue(stop["ok"], stop)
        self.assertNotIn("avatar:companion", self.host.goals)
        self.assertNotIn(light, self.host.active_effect_ids())
        for message in (command("goal.stop", {}, "stop-1"), command("effect.stop", {"effect": "all"}, "stop-2"),
                        command("goal.stop", {}, "lock-1"), command("effect.stop", {"effect": "all"}, "stop-2")):
            with self.subTest(action_id=message["action_id"]):
                self.assertTrue(self.send(message)["ok"])

    def test_the_players_stop_naming_the_companion_stops_its_goals_and_effects(self):
        light = self.light("glow-1")
        self.send(command("goal.set", {"actor": "avatar:companion", "goal": "wander"}, "g-1"))
        stop = self.host.player_command(command("goal.stop", {"actor": "avatar:companion"}, "p-stop"))
        self.assertEqual(stop["affected"], ["avatar:companion", light])
        self.assertEqual(self.host.active_effect_ids(), [])
        self.assertNotIn("avatar:companion", self.host.goals)

    def test_the_players_stop_of_their_own_avatar_leaves_the_companion_alone(self):
        light = self.light("glow-1")
        stop = self.host.player_command(command("goal.stop", {"actor": "avatar:player"}, "p-stop"))
        self.assertEqual((stop["affected"], stop["data"]), (["avatar:player"], {"effects_stopped": 0}))
        self.assertEqual(self.host.active_effect_ids(), [light])


# Every other host suite whose protections do not depend on holding, again with nothing held. (The
# approval suites need holds by definition; the style suite builds its own hosts.)
class _NothingHeld:
    def tearDown(self):
        super().tearDown()
        self.assertEqual(self.host.approvals, {}, "nothing may be held with an empty held set")


def _without_holds(suite):
    policy = dataclasses.replace(suite.policy or HostPolicy(), companion_approval_ops=frozenset())
    return type(f"{suite.__name__}WithNothingHeld", (_NothingHeld, suite), {"policy": policy, "__module__": __name__})


for _suite in (base.RateLimits, base.ContractShape, fixes.Receipts, fixes.Stops, fixes.Checkpoints, fixes.Locks,
               fixes.Previews, fixes.Numbers, fixes.Text, fixes.Budgets, perception_tests.LineOfSight,
               perception_tests.EdgeCasePolicies, perception_tests.EverySurface, memory_tests.Remembering,
               memory_tests.Staleness, memory_tests.LookingAgain, memory_tests.Commands, memory_tests.Arrival,
               memory_tests.NeverThroughOthers, memory_tests.Bounds, memory_tests.NothingHiddenLeaks):
    _variant = _without_holds(_suite)
    globals()[_variant.__name__] = _variant
del _suite, _variant


if __name__ == "__main__":
    import unittest
    unittest.main()
