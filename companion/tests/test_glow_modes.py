"""New command parity: host-observed Glow, cosmetic touches, mode isolation and honest native-only boundaries."""
from __future__ import annotations

import copy
import unittest

from support import (COMPANION, IN_THE_OPEN, NO_HOLDS, PLAYER, FakeClock, cast, command,
                     contract_problems, glow, new_host, query)
from enfractal_companion.glow_progress import GlowProgress


class GlowAndModes(unittest.TestCase):
    def setUp(self):
        self.clock = FakeClock()
        self.host = new_host(clock=self.clock, policy=NO_HOLDS)
        self.serial = 0

    def tearDown(self):
        for result in self.host.emitted:
            self.assertEqual(contract_problems(result), [], result)

    def send(self, op, args, principal=PLAYER, **options):
        self.serial += 1
        self.clock.advance(0.1)
        return self.host.handle(principal, command(op, args, f"extended-{self.serial}", **options))

    def cast(self, *, point=None, principal=PLAYER, variant=None, touch=None):
        self.serial += 1
        message = glow(f"extended-glow-{self.serial}", at=point)
        if variant is not None:
            message["args"]["variant"] = variant
        if touch is not None:
            message["args"]["touch"] = touch
        return self.host.handle(principal, message)

    def stop(self):
        self.assertTrue(self.send("effect.stop", {"effect": "all"})["ok"])

    def test_touch_and_audio_configuration_is_player_owned_previewed_and_applies_to_future_casts(self):
        args = {"touch": "chime", "sound_volume": 0.2, "muted": True}
        preview = self.send("glow.configure", args, preview=True)
        self.assertEqual(preview["data"], args)
        self.assertEqual(self.host.glow_progress.touch, "plain")
        self.assertEqual(self.send("glow.configure", args, COMPANION)["error"]["code"], "permission_denied")
        self.assertTrue(self.send("glow.configure", args)["ok"])
        result = self.cast()
        self.assertEqual((result["data"]["variant"], result["data"]["touch"]), ("lantern", "chime"))
        self.assertEqual((self.host.glow_progress.sound_volume, self.host.glow_progress.muted), (0.2, True))
        self.assertEqual(self.host.glow_progress.discoveries, {"lantern"})
        explicit = self.cast(touch="ember")
        self.assertEqual(explicit["data"]["touch"], "ember")
        self.assertEqual(self.host.glow_progress.touch, "chime", "a cast's touch does not rewrite the player's preference")

    def test_configuration_rejects_invalid_values_without_publication(self):
        for args in ({}, {"touch": "fire"}, {"sound_volume": 2}, {"sound_volume": True}, {"muted": "yes"}):
            with self.subTest(args=args):
                self.assertFalse(self.send("glow.configure", args)["ok"])
        self.assertEqual((self.host.glow_progress.touch, self.host.glow_progress.sound_volume), ("plain", 0.35))

    def test_real_cast_constellation_counts_once_and_earns_the_fourth_light_after_lantern(self):
        self.assertTrue(self.cast()["ok"])
        self.stop()
        ids = []
        for offset in (0, 0.08, 0.16):
            point = [IN_THE_OPEN[0] + offset, 0, IN_THE_OPEN[2]]
            result = self.cast(point=point, variant="plain")
            self.assertTrue(result["ok"], result)
            ids.append(result["created"][0])
        self.assertEqual(self.host.glow_progress.discoveries, {"lantern", "constellation"})
        self.assertEqual(self.host.glow_progress.earned_light_limit, 4)
        for effect_id in ids:
            self.assertEqual(self.host.effects[effect_id].connections, set(ids) - {effect_id})
        listed = self.host.handle(COMPANION, query("capabilities.list", {"category": "light"}))
        self.assertEqual(listed["data"]["items"][0]["max_active"], 4)

    def test_constellations_do_not_combine_different_casters(self):
        self.cast(point=IN_THE_OPEN, principal=PLAYER)
        self.cast(point=IN_THE_OPEN, principal=COMPANION)
        self.cast(point=IN_THE_OPEN, principal=COMPANION)
        self.assertNotIn("constellation", self.host.glow_progress.discoveries)

    def test_overlapping_lights_do_not_farm_the_constellation_discovery(self):
        for _ in range(3):
            self.assertTrue(self.cast(point=IN_THE_OPEN, variant="plain")["ok"])
        self.assertNotIn("constellation", self.host.glow_progress.discoveries)
        self.assertTrue(all(not effect.connections for effect in self.host.effects.values()))

    def test_glowing_bubbles_needs_a_real_stream_and_ends_with_it(self):
        refused = self.cast(variant="glowing_bubbles")
        self.assertEqual(refused["error"]["code"], "target_not_found")
        bubbles = self.host.handle(PLAYER, cast("bubbles", "stream"))
        bubble_id = bubbles["created"][0]
        result = self.cast(variant="glowing_bubbles", touch="frost")
        self.assertEqual(result["data"]["variant"], "glowing_bubbles")
        self.assertEqual(self.host.glow_progress.discoveries, set())
        stopped = self.send("effect.stop", {"effect": bubble_id})
        self.assertEqual(set(stopped["affected"]), {bubble_id, result["created"][0]})
        self.assertEqual(self.host.active_effect_ids(), [])

    def test_adjustment_requires_fifth_light_checks_ownership_and_previews_without_change(self):
        first = self.cast(point=IN_THE_OPEN, variant="plain")["created"][0]
        args = {"effect": first, "center_m": [IN_THE_OPEN[0] + 0.05, 0, IN_THE_OPEN[2]], "connections": []}
        self.assertEqual(self.send("effect.adjust", args)["error"]["code"], "permission_denied")
        self.host.glow_progress = GlowProgress(5, GlowProgress.DISCOVERY_ORDER)
        second = self.cast(point=IN_THE_OPEN, variant="plain")["created"][0]
        args["connections"] = [second]
        original = copy.deepcopy(self.host.effects[first])
        self.assertTrue(self.send("effect.adjust", args, preview=True)["ok"])
        self.assertEqual(self.host.effects[first], original)
        self.assertEqual(self.send("effect.adjust", args, COMPANION)["error"]["code"], "permission_denied")
        moved = self.send("effect.adjust", args)
        self.assertEqual(moved["data"], args)
        self.assertEqual(self.host.effects[first].connections, {second})
        self.assertEqual(self.host.effects[second].connections, {first})
        self.assertTrue(self.send("effect.adjust", {"effect": first, "connections": []})["ok"])
        self.assertEqual(self.host.effects[second].connections, set())
        bad = self.send("effect.adjust", {"effect": first, "connections": [first]})
        self.assertEqual(bad["error"]["code"], "target_not_found")

    def test_mode_changes_are_player_owned_and_preserve_separate_progress_and_configuration(self):
        self.cast()
        self.send("glow.configure", {"touch": "frost"})
        self.assertTrue(self.send("world.set_mode", {"mode": "challenge"}, preview=True)["ok"])
        self.assertEqual(self.host.mode, "creative")
        self.assertEqual(self.send("world.set_mode", {"mode": "challenge"}, COMPANION)["error"]["code"], "permission_denied")
        self.assertTrue(self.send("world.set_mode", {"mode": "challenge"})["ok"])
        self.assertEqual(self.host.glow_progress.discoveries, set())
        self.assertEqual(self.host.glow_progress.touch, "plain")
        self.assertEqual(self.host.active_effect_ids(), [])
        status = self.host.handle(COMPANION, query("player.status", {}))["data"]
        self.assertEqual(status, {"mode": "challenge", "health": 100, "max_health": 100, "stamina": 0.45,
                                  "max_stamina": 0.45, "completed_climbs": 0, "recoveries": 0, "consequences": True})
        # Trusted saved-state fixture: wire messages cannot set these values.
        self.host._vitals_profiles["challenge"]["health"] = 80
        self.assertTrue(self.send("world.set_mode", {"mode": "live"})["ok"])
        self.assertEqual(self.host.handle(COMPANION, query("player.status", {}))["data"]["health"], 100)
        self.send("world.set_mode", {"mode": "creative"})
        self.assertEqual((self.host.glow_progress.discoveries, self.host.glow_progress.touch), ({"lantern"}, "frost"))
        self.send("world.set_mode", {"mode": "challenge"})
        self.host.load_room(self.host.room_dir)
        self.assertEqual(self.host.handle(COMPANION, query("player.status", {}))["data"]["health"], 80)

    def test_native_only_features_refuse_explicitly_without_fake_receipts_or_discoveries(self):
        for variant in ("torch", "bioluminesce", "skylight"):
            with self.subTest(variant=variant):
                result = self.cast(point=IN_THE_OPEN, variant=variant)
                self.assertEqual(result["error"]["code"], "unsupported_capability")
                self.assertIn("native game runtime", result["error"]["message"])
        messages = [
            ("nature.cast", {"capability": "bloom", "center_m": IN_THE_OPEN, "radius_m": 0.25, "duration_s": 30}),
            ("nature.trail", {"enabled": True}),
            ("nature.repair", {"target": "creation:unbuilt"}),
        ]
        before = (self.host.revision, len(self.host.receipts), len(self.host.transient))
        for op, args in messages:
            with self.subTest(op=op):
                result = self.send(op, args)
                self.assertEqual(result["error"]["code"], "unsupported_capability", result)
                self.assertIn("native game runtime", result["error"]["message"])
        result = self.host.handle(PLAYER, query("nature.describe", {}))
        self.assertEqual(result["error"]["code"], "unsupported_capability")
        # The wish queue is simulated (mock_wishes.py, test_wishes.py); its journal stays the player's.
        self.assertEqual(self.host.handle(PLAYER, query("wishes.read", {}))["data"], {"items": []})
        self.assertEqual(self.host.handle(COMPANION, query("wishes.read", {}))["error"]["code"], "permission_denied")
        self.assertEqual((self.host.revision, len(self.host.receipts), len(self.host.transient)), before)
        self.assertEqual(self.host.glow_progress.discoveries, set())
        items = self.host.handle(COMPANION, query("capabilities.list", {}))["data"]["items"]
        for item in items:
            nature = item["capability"] in {"bloom", "fire"}
            self.assertEqual(item["native_required"], nature)
            self.assertEqual(item["command_op"], "nature.cast" if nature else "effect.start")


class ProgressiveHints(unittest.TestCase):
    def test_hints_follow_missing_discoveries_at_ten_use_intervals_and_never_repeat(self):
        progress = GlowProgress()
        for _ in range(50):
            progress.record_observed(None)
        self.assertEqual(progress.delivered_hints, set(GlowProgress.HINT_ORDER))
        for _ in range(10):
            progress.record_observed("lantern")
        self.assertEqual(progress.delivered_hints, set(GlowProgress.HINT_ORDER))
        self.assertEqual(progress.discoveries, {"lantern"})
