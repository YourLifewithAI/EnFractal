"""Founder decision 2: ideally no approval clicks. Every safety property must hold with an empty held set.

The suites that do not depend on holding run again here under NO_HOLDS, and the new cases cover
what an unheld companion could otherwise do: undo the player's locks, outlast the player's stop,
outspend the budgets or slip past revisions. After every test, nothing may have been held.
"""
from __future__ import annotations

import dataclasses

import test_host_boundary as base
from support import COMPANION, NO_HOLDS, PLAYER, command, example, query, retarget, GARAGE_TO_TEST_ROOM


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
        self.send(command("effect.start", {"capability": "wind_field", "params": {"speed_mps": 5},
                                           "area": {"center_m": [0, 0.3, 0], "radius_m": 3}, "duration_s": 600}, "wind-1"))
        self.send(command("goal.set", {"actor": "avatar:companion", "goal": "wander"}, "g-1"))
        stop = self.host.player_command(command("goal.stop", {}, "p-stop"))
        self.assertTrue(stop["ok"])
        self.assertIn("effect:0001", stop["affected"])
        self.assertNotIn("avatar:companion", self.host.goals)
        self.assertTrue(self.host.entities["effect:0001"].removed)

    def test_the_players_effect_stop_stops_companion_effects_all_or_by_id(self):
        for i in range(2):
            self.send(command("effect.start", {"capability": "glow", "params": {"intensity": 1},
                                               "area": {"center_m": [0, 0.3, 0], "radius_m": 1}, "duration_s": 60}, f"glow-{i}"))
        by_id = self.host.player_command(command("effect.stop", {"effect": "effect:0001"}, "p-stop-1"))
        self.assertEqual(by_id["affected"], ["effect:0001"])
        all_of_them = self.host.player_command(command("effect.stop", {"effect": "all"}, "p-stop-2"))
        self.assertEqual(all_of_them["affected"], ["effect:0002"])
        self.assertTrue(all(e.removed for e in self.host.entities.values() if e.kind == "effect"))

    def test_the_companion_cannot_stop_the_players_effects(self):
        self.host.player_command(command("effect.start", {"capability": "glow", "params": {"intensity": 1},
                                                          "area": {"center_m": [0, 0.3, 0], "radius_m": 1}, "duration_s": 60},
                                         "p-glow"))
        result = self.send(command("effect.stop", {"effect": "all"}, "stop-1"))
        self.assertEqual(result["affected"], [])
        self.assertFalse(self.host.entities["effect:0001"].removed)

    def test_effect_and_creation_budgets_hold(self):
        self.host.policy = dataclasses.replace(self.host.policy, max_creations=1)
        for i in range(4):
            self.assertTrue(self.send(command("effect.start", {"capability": "glow", "params": {"intensity": 1},
                                                              "area": {"center_m": [0, 0.3, 0], "radius_m": 1},
                                                              "duration_s": 60}, f"glow-{i}"))["ok"])
        self.assertRefused(self.send(command("effect.start", {"capability": "glow", "params": {"intensity": 1},
                                                              "area": {"center_m": [0, 0.3, 0], "radius_m": 1},
                                                              "duration_s": 60}, "glow-4")), "budget_exceeded")
        source = example("command_creation_place_spinner")["args"]["source"]
        first = self.send(command("creation.place", {"source": source, "placement": {"position_m": [0.5, 0, 0.5]}}, "c-1"))
        self.assertTrue(first["ok"], first)
        self.assertRefused(self.send(command("creation.place", {"source": source, "placement": {"position_m": [0.6, 0, 0.5]}},
                                             "c-2")), "budget_exceeded")

    def test_style_set_still_refuses_an_unpinnable_preset(self):
        result = self.send(command("style.set", {"preset_id": "storybook_painterly", "preset_version": 1}, "style-1"))
        self.assertRefused(result, "invalid_args", "$.args.preset_version")


if __name__ == "__main__":
    import unittest
    unittest.main()
