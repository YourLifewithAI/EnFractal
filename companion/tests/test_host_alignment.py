"""Behavioural alignment: the same steps on the mock host and on the real game host, with the same expected outcome.

test_kernel_alignment.py compares the two hosts' constants; constants alone missed behaviour (Codex's review of Lane A's
round 2). Each scenario here is written once against a few moves (ask, walk the companion somewhere, finish a goal's
job) and runs on the mock below and on the kernel host in test_real_host.py (AlignmentOnTheRealHost). Both must give
the scenario's expected outcome.
"""
from __future__ import annotations

import json
import re
import unittest

from support import (BEHIND_BOX, COMPANION, FAR_IN_SIGHT, GUBBLE, IN_THE_OPEN, OUT_OF_REACH, OUTSIDE_ROOM, FakeClock,
                     HostPolicy, cast, command, contract_problems, glow, new_host, query)

BEHIND_THE_BOX = [1.6, 0.0, 0.2]  # the box hides the book from the companion; the player at its spawn still sees it
HIDDEN_SPOT = [-0.9, 0.0, -1.42]  # behind the table: neither avatar sees it from beside the player


def block(name: str) -> dict:
    """A 10 cm wooden block, the creation compiler's smallest part."""
    return {"schema": "enfractal.creation", "version": 1, "name": name, "seed": 7, "mount": "ground",
            "parts": [{"id": "block", "shape": "box", "position_m": [0, 0.05, 0], "rotation_deg": [0, 0, 0],
                       "size_m": [0.1, 0.1, 0.1], "material": "wood"}], "nodes": [], "edges": []}


EFFECT_ID = re.compile(r"effect:[a-z2-7]{26}")
# capabilities.list's items for storybook_wild v2 (game/rules/storybook_wild/v2.json), in the order both hosts list them.
GLOW_SUMMARY = {"capability": "glow", "category": "light", "params": {"intensity": {"min": 0.2, "max": 1.0}},
                "area_radius_max_m": 1.5, "duration_max_s": 600}
BUBBLES_SUMMARY = {"capability": "bubbles", "category": "float", "params": {"intensity": {"min": 0.2, "max": 1.0}},
                   "area_radius_max_m": 1.0, "duration_max_s": 60}
FIREWORKS_SUMMARY = {"capability": "fireworks", "category": "burst", "params": {"intensity": {"min": 0.3, "max": 1.0}},
                     "area_radius_max_m": 2.0, "duration_max_s": 6}
# An ability storybook_wild v2 does not grant (Bloom, the growth slot, comes later).
LACKING = "bloom"


def refusal(result: dict):
    """(code, field_path) of a refusal, or "ok"."""
    return "ok" if result["ok"] else (result["error"]["code"], result["error"].get("field_path"))


class AlignmentScenarios:
    """A subclass provides ask(message), companion_to(position), finish(job_id) -> the job's final state,
    companion_home() (the companion beside the player or at its spawn, as the glow spots in support.py assume) and
    wait(seconds) on the host's clock."""

    async def ask(self, message: dict) -> dict:
        raise NotImplementedError

    async def companion_to(self, position: list[float]) -> None:
        raise NotImplementedError

    async def finish(self, job_id: str) -> str:
        raise NotImplementedError

    async def companion_home(self) -> None:
        raise NotImplementedError

    async def wait(self, seconds: float) -> None:
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


    # ----- Glow (docs/runs/RUN-2-GLOW.md section 5): the island's rules, on both hosts -----

    async def stop_all_glows(self) -> None:
        """Only the companion casts through the link, so its stop of all ends every glow a scenario left."""
        self.assertTrue((await self.ask(command("effect.stop", {"effect": "all"}, self.next_id("clear"))))["ok"])

    async def stop_one(self, effect_id: str) -> tuple:
        """A stop by id: (the ids it ended, how many). By id, the count is the glows only on both hosts."""
        result = await self.ask(command("effect.stop", {"effect": effect_id}, self.next_id("stop")))
        self.assertTrue(result["ok"], result)
        return result.get("affected", []), result["data"]["effects_stopped"]

    async def test_capabilities_list_answers_the_islands_rules(self):
        async def listed(args):
            result = await self.ask(query("capabilities.list", args, self.next_id("q")))
            return result["data"] if result["ok"] else refusal(result)

        outcome = {
            "all": await listed({}),
            "light": await listed({"category": "light"}),
            "float": await listed({"category": "float"}),
            "burst": await listed({"category": "burst"}),
            "air": await listed({"category": "air"}),
            "first page of one": await listed({"limit": 1, "cursor": "0"}),
            "the next page": await listed({"limit": 1, "cursor": "1"}),
            "the last page": await listed({"limit": 1, "cursor": "2"}),
            "past the last": await listed({"limit": 1, "cursor": "3"}),
            "the rest from the second": await listed({"cursor": "1"}),
            "beyond the end": await listed({"cursor": "4"}),
            "a signed cursor": await listed({"cursor": "+0"}),
            "a cursor of words": await listed({"cursor": "next"}),
        }
        self.assertEqual(outcome, {
            "all": {"items": [BUBBLES_SUMMARY, FIREWORKS_SUMMARY, GLOW_SUMMARY]},
            "light": {"items": [GLOW_SUMMARY]},
            "float": {"items": [BUBBLES_SUMMARY]},
            "burst": {"items": [FIREWORKS_SUMMARY]},
            "air": {"items": []},
            "first page of one": {"items": [BUBBLES_SUMMARY], "next_cursor": "1"},
            "the next page": {"items": [FIREWORKS_SUMMARY], "next_cursor": "2"},
            "the last page": {"items": [GLOW_SUMMARY]},
            "past the last": {"items": []},
            "the rest from the second": {"items": [FIREWORKS_SUMMARY, GLOW_SUMMARY]},
            "beyond the end": ("invalid_args", "$.args.cursor"),
            "a signed cursor": ("invalid_args", "$.args.cursor"),
            "a cursor of words": ("invalid_args", "$.args.cursor"),
        })

    async def test_the_companion_glows_itself_and_a_spot_in_sight(self):
        await self.companion_home()
        await self.stop_all_glows()
        own = glow(self.next_id("glow"), params={})  # the defaults fill what is left out
        mine = await self.ask(own)
        spot = await self.ask(glow(self.next_id("glow"), at=IN_THE_OPEN, params={"intensity": 1}, radius=1.5, duration=600))
        preview = await self.ask(glow(self.next_id("glow"), at=IN_THE_OPEN, params={"intensity": 0.9}, preview=True))
        replay = await self.ask(own)
        conflict = await self.ask(glow(own["action_id"], params={"intensity": 0.3}))
        lookup = await self.ask(query("receipt.lookup", {"action_id": own["action_id"]}, self.next_id("q")))
        light, wisp = mine.get("created", ["?"])[0], spot.get("created", ["?"])[0]
        data = mine.get("data", {})
        outcome = {
            "self": refusal(mine),
            "self receipt": (mine.get("transient"), mine.get("created") == mine.get("affected") == [light],
                             bool(EFFECT_ID.fullmatch(light)), data.get("effect") == light),
            "self data": (sorted(data), data.get("capability"), data.get("category"), data.get("target"), data.get("params"),
                          data.get("area", {}).get("radius_m"), data.get("duration_s")),
            "point": refusal(spot),
            "point data": (spot["data"]["target"], spot["data"]["area"], spot["data"]["params"], spot["data"]["duration_s"])
            if spot["ok"] else None,
            "preview": (refusal(preview), preview["preview"], "created" in preview, preview.get("data", {}).get("target"),
                        "effect" in preview.get("data", {}), "expires_utc" in preview.get("data", {}),
                        preview.get("data", {}).get("params")),
            "replay": (replay["replayed"], replay.get("created")) == (True, [light]),
            "conflict": refusal(conflict),
            "lookup": (lookup["data"]["found"], lookup["data"].get("receipt", {}).get("created")) == (True, [light]),
            "the point by id": await self.stop_one(wisp),
            "the same stop again": await self.stop_one(wisp),
            "an unknown id": await self.stop_one("effect:" + "a" * 26),
        }
        stop = await self.ask(command("goal.stop", {}, self.next_id("stop")))
        outcome["goal.stop ends its own glow"] = stop["ok"] and light in stop["affected"]
        outcome["nothing left to stop"] = await self.stop_one(light)
        self.assertEqual(outcome, {
            "self": "ok",
            "self receipt": (True, True, True, True),
            "self data": (["area", "capability", "category", "duration_s", "effect", "expires_utc", "params", "target"],
                          "glow", "light", "self", {"intensity": 0.6}, 0.5, 60),
            "point": "ok",
            "point data": ("point", {"center_m": IN_THE_OPEN, "radius_m": 1.5}, {"intensity": 1}, 600),
            "preview": ("ok", True, False, "point", False, False, {"intensity": 0.9}),
            "replay": True,
            "conflict": ("action_id_conflict", "$.action_id"),
            "lookup": True,
            "the point by id": ([wisp], 1),
            "the same stop again": ([], 0),
            "an unknown id": ([], 0),
            "goal.stop ends its own glow": True,
            "nothing left to stop": ([], 0),
        })

    async def test_the_companion_cannot_glow_beyond_the_islands_rules(self):
        """Each refusal with the kernel's code and field, the order of the checks where two apply at once, and
        nothing started by any of them: afterwards the pack's three glows still fit, and a fourth does not."""
        await self.companion_home()
        await self.stop_all_glows()
        cases = {
            "the player": glow("x", targets=["avatar:player"]),
            "a thing in sight": glow("x", targets=["obj:rug"]),
            "itself and a thing": glow("x", targets=[GUBBLE, "obj:rug"]),
            "a thing that is not there": glow("x", targets=["obj:zz_not_here"]),
            "an ability the island lacks": glow("x", capability=LACKING),
            "a param it does not take": glow("x", params={"speed": 1}),
            "too bright": glow("x", params={"intensity": 1.5}),
            "a word for a number": glow("x", params={"intensity": "bright"}),
            "too wide for the ability": glow("x", radius=2.0),
            "too wide for the contract": glow("x", radius=12),
            "too long for the contract": glow("x", duration=700),
            "beyond its reach": glow("x", at=OUT_OF_REACH),
            "outside the room": glow("x", at=OUTSIDE_ROOM),
            "a spot neither avatar sees": glow("x", at=BEHIND_BOX),
            # Two problems at once: the kernel's order of checks decides.
            "unseen target and unknown ability": glow("x", capability=LACKING, targets=["obj:zz_not_here"]),
            "unknown ability and the player": glow("x", capability=LACKING, targets=["avatar:player"]),
            "the player and too bright": glow("x", targets=["avatar:player"], params={"intensity": 1.5}),
            "too bright and beyond reach": glow("x", at=OUT_OF_REACH, params={"intensity": 1.5}),
        }
        for name in ("owner", "actor", "room_id", "principal", "approval_id", "role"):
            cases[f"a reserved param {name}"] = glow("x", params={name: 1})
        outcome = {}
        for label, message in cases.items():
            message["action_id"] = self.next_id("no")
            result = await self.ask(message)
            outcome[label] = refusal(result)
            if label in ("too bright", "too wide for the ability", "beyond its reach"):
                outcome[label + ": bounds"] = (result["error"].get("allowed"), result["error"].get("actual"))
            if label in ("beyond its reach", "outside the room"):
                outcome[label + ": retryable"] = result["error"]["retryable"]
        started = [await self.ask(glow(self.next_id("glow"), at=at)) for at in (None, IN_THE_OPEN, [0.5, 0.0, 0.9])]
        hidden_when_full = await self.ask(glow(self.next_id("glow"), at=BEHIND_BOX))
        fourth = await self.ask(glow(self.next_id("glow")))
        outcome["three glows"] = [refusal(r) for r in started]
        outcome["hidden, with the cap reached"] = refusal(hidden_when_full)
        outcome["a fourth"] = (refusal(fourth), fourth["error"].get("allowed"), fourth["error"].get("actual")) \
            if not fourth["ok"] else "ok"
        outcome["each stops by id"] = [(await self.stop_one(r["created"][0]))[1] for r in started if r["ok"]]
        expected = {
            "the player": ("permission_denied", "$.args.targets"),
            "a thing in sight": ("permission_denied", "$.args.targets"),
            "itself and a thing": ("permission_denied", "$.args.targets"),
            "a thing that is not there": ("target_not_found", "$.args.targets"),
            "an ability the island lacks": ("unsupported_capability", "$.args.capability"),
            "a param it does not take": ("invalid_args", "$.args.params.speed"),
            "too bright": ("invalid_args", "$.args.params.intensity"),
            "too bright: bounds": ([0.2, 1.0], 1.5),
            "a word for a number": ("invalid_args", "$.args.params.intensity"),
            "too wide for the ability": ("invalid_args", "$.args.area.radius_m"),
            "too wide for the ability: bounds": (1.5, 2.0),
            "too wide for the contract": ("request_invalid", "$.args.area.radius_m"),
            "too long for the contract": ("request_invalid", "$.args.duration_s"),
            "beyond its reach": ("out_of_bounds", "$.args.area.center_m"),
            "beyond its reach: bounds": (2.0, outcome.get("beyond its reach: bounds", (None, None))[1]),
            "beyond its reach: retryable": True,
            "outside the room": ("out_of_bounds", "$.args.area.center_m"),
            "outside the room: retryable": False,
            "a spot neither avatar sees": ("target_not_found", "$.args.area.center_m"),
            "unseen target and unknown ability": ("target_not_found", "$.args.targets"),
            "unknown ability and the player": ("unsupported_capability", "$.args.capability"),
            "the player and too bright": ("permission_denied", "$.args.targets"),
            "too bright and beyond reach": ("invalid_args", "$.args.params.intensity"),
            "three glows": ["ok", "ok", "ok"],
            "hidden, with the cap reached": ("target_not_found", "$.args.area.center_m"),
            "a fourth": (("budget_exceeded", "$.args.capability"), 3, 3),
            "each stops by id": [1, 1, 1],
        }
        for name in ("owner", "actor", "room_id", "principal", "approval_id", "role"):
            expected[f"a reserved param {name}"] = ("request_invalid", "$.args.params")
        self.assertEqual(outcome, expected)
        self.assertGreater(outcome["beyond its reach: bounds"][1], 2.0)

    async def test_each_kind_ends_at_its_duration(self):
        """A glow, a stream of bubbles and a firework each end at their own duration_s; a longer one still runs."""
        await self.companion_home()
        await self.stop_all_glows()
        brief = [await self.ask(cast(kind, self.next_id(kind), duration=1)) for kind in ("glow", "bubbles", "fireworks")]
        longer = await self.ask(cast("bubbles", self.next_id("bubbles"), at=IN_THE_OPEN, duration=60))
        await self.wait(2.0)
        outcome = {"started": ([refusal(r) for r in brief], refusal(longer)),
                   "the brief ones have ended": [await self.stop_one(r["created"][0]) for r in brief if r["ok"]],
                   "the longer one still runs": await self.stop_one(longer["created"][0])}
        self.assertEqual(outcome, {"started": (["ok", "ok", "ok"], "ok"), "the brief ones have ended": [([], 0)] * 3,
                                   "the longer one still runs": (longer["created"], 1)})

    # ----- Bubbles and Fireworks (docs/runs/RUN-2-BUBBLES-FIREWORKS.md piece 5): the same rules, on both hosts -----

    async def test_the_companion_casts_bubbles_and_fireworks_on_itself_and_at_a_spot(self):
        """Each on the Gubble (self, the pack's defaults filling the params left out) and at a spot in sight; Fireworks
        reach further than Bubbles; a preview starts nothing; goal.stop ends every kind the companion cast."""
        await self.companion_home()
        await self.stop_all_glows()
        bubbles = await self.ask(cast("bubbles", self.next_id("bubbles"), params={}))
        stream = await self.ask(cast("bubbles", self.next_id("bubbles"), at=IN_THE_OPEN, params={"intensity": 1},
                                     radius=1.0, duration=60))
        burst = await self.ask(cast("fireworks", self.next_id("fireworks"), params={}, radius=1.0, duration=6))
        rocket = await self.ask(cast("fireworks", self.next_id("fireworks"), at=FAR_IN_SIGHT, params={"intensity": 0.3},
                                     radius=2.0, duration=6))
        too_far = await self.ask(cast("bubbles", self.next_id("bubbles"), at=FAR_IN_SIGHT))
        preview = await self.ask(cast("fireworks", self.next_id("fireworks"), at=IN_THE_OPEN, preview=True))

        def about(result):
            data = result.get("data", {})
            return (refusal(result), result.get("transient"), bool(EFFECT_ID.fullmatch(data.get("effect", ""))),
                    data.get("capability"), data.get("category"), data.get("target"), data.get("params"),
                    data.get("area", {}).get("radius_m"), data.get("duration_s"))

        started = [r["created"][0] for r in (bubbles, stream, burst, rocket) if r["ok"]]
        stop = await self.ask(command("goal.stop", {}, self.next_id("stop")))
        outcome = {
            "bubbles on itself": about(bubbles),
            "bubbles at a spot": about(stream),
            "the stream's spot": stream.get("data", {}).get("area", {}).get("center_m"),
            "fireworks over itself": about(burst),
            "fireworks at a far spot": about(rocket),
            "the rocket's spot": rocket.get("data", {}).get("area", {}).get("center_m"),
            "bubbles at the far spot": (refusal(too_far), too_far.get("error", {}).get("allowed"),
                                        too_far.get("error", {}).get("retryable")),
            "preview": (refusal(preview), preview["preview"], "created" in preview, "effect" in preview.get("data", {}),
                        preview.get("data", {}).get("category")),
            "goal.stop ends every kind": stop["ok"] and set(started) <= set(stop["affected"]),
            "nothing left to stop": [await self.stop_one(e) for e in started],
        }
        self.assertEqual(outcome, {
            "bubbles on itself": ("ok", True, True, "bubbles", "float", "self", {"intensity": 0.6}, 0.5, 20),
            "bubbles at a spot": ("ok", True, True, "bubbles", "float", "point", {"intensity": 1}, 1.0, 60),
            "the stream's spot": IN_THE_OPEN,
            "fireworks over itself": ("ok", True, True, "fireworks", "burst", "self", {"intensity": 0.8}, 1.0, 6),
            "fireworks at a far spot": ("ok", True, True, "fireworks", "burst", "point", {"intensity": 0.3}, 2.0, 6),
            "the rocket's spot": FAR_IN_SIGHT,
            "bubbles at the far spot": (("out_of_bounds", "$.args.area.center_m"), 2.0, True),
            "preview": ("ok", True, False, False, "burst"),
            "goal.stop ends every kind": True,
            "nothing left to stop": [([], 0)] * 4,
        })

    async def test_the_companion_cannot_cast_bubbles_or_fireworks_beyond_the_islands_rules(self):
        """Bubbles' and Fireworks' own bounds, reach and max_active, with the kernel's codes, fields and numbers; the
        player and other things are never targets; one ability's cap leaves the others free."""
        await self.companion_home()
        await self.stop_all_glows()
        cases = {
            "bubbles on the player": cast("bubbles", "x", targets=["avatar:player"]),
            "fireworks on the player": cast("fireworks", "x", targets=["avatar:player"]),
            "bubbles on a thing in sight": cast("bubbles", "x", targets=["obj:rug"]),
            "fireworks on itself and a thing": cast("fireworks", "x", targets=[GUBBLE, "obj:rug"]),
            "fireworks on a thing that is not there": cast("fireworks", "x", targets=["obj:zz_not_here"]),
            "a param bubbles do not take": cast("bubbles", "x", params={"speed": 1}),
            "bubbles too thick": cast("bubbles", "x", params={"intensity": 1.5}),
            "fireworks too faint": cast("fireworks", "x", params={"intensity": 0.25}),
            "bubbles too wide": cast("bubbles", "x", radius=1.2),
            "fireworks too wide": cast("fireworks", "x", radius=2.5),
            "bubbles too long": cast("bubbles", "x", duration=90),
            "fireworks too long": cast("fireworks", "x", duration=8),
            "fireworks outside the room": cast("fireworks", "x", at=OUTSIDE_ROOM),
            "fireworks at a spot neither avatar sees": cast("fireworks", "x", at=BEHIND_BOX),
            "the player and too thick": cast("bubbles", "x", targets=["avatar:player"], params={"intensity": 1.5}),
        }
        bounded = ("bubbles too thick", "fireworks too faint", "bubbles too wide", "fireworks too wide", "bubbles too long",
                   "fireworks too long")
        outcome = {}
        for label, message in cases.items():
            message["action_id"] = self.next_id("no")
            result = await self.ask(message)
            outcome[label] = refusal(result)
            if label in bounded:
                outcome[label + ": bounds"] = (result["error"].get("allowed"), result["error"].get("actual"))
        # The caps, each ability's own while the others run. Fireworks last at most 6 s: they start and stop well inside that.
        streams = [await self.ask(cast("bubbles", self.next_id("bubbles"), at=at)) for at in (None, IN_THE_OPEN)]
        third = await self.ask(cast("bubbles", self.next_id("bubbles"), at=[0.5, 0.0, 0.9]))
        rockets = [await self.ask(cast("fireworks", self.next_id("fireworks"), at=at, duration=6))
                   for at in (None, IN_THE_OPEN, FAR_IN_SIGHT)]
        fourth = await self.ask(cast("fireworks", self.next_id("fireworks"), duration=6))
        outcome["three fireworks"] = [refusal(r) for r in rockets]
        outcome["a fourth"] = (refusal(fourth), fourth.get("error", {}).get("allowed"), fourth.get("error", {}).get("actual"))
        outcome["the fireworks stop by id"] = [(await self.stop_one(r["created"][0]))[1] for r in rockets if r["ok"]]
        light = await self.ask(glow(self.next_id("glow")))
        outcome["two streams"] = [refusal(r) for r in streams]
        outcome["a third stream"] = (refusal(third), third.get("error", {}).get("allowed"), third.get("error", {}).get("actual"))
        outcome["a glow still fits"] = refusal(light)
        outcome["the rest stop by id"] = [(await self.stop_one(r["created"][0]))[1] for r in streams + [light] if r["ok"]]
        expected = {
            "bubbles on the player": ("permission_denied", "$.args.targets"),
            "fireworks on the player": ("permission_denied", "$.args.targets"),
            "bubbles on a thing in sight": ("permission_denied", "$.args.targets"),
            "fireworks on itself and a thing": ("permission_denied", "$.args.targets"),
            "fireworks on a thing that is not there": ("target_not_found", "$.args.targets"),
            "a param bubbles do not take": ("invalid_args", "$.args.params.speed"),
            "bubbles too thick": ("invalid_args", "$.args.params.intensity"),
            "bubbles too thick: bounds": ([0.2, 1.0], 1.5),
            "fireworks too faint": ("invalid_args", "$.args.params.intensity"),
            "fireworks too faint: bounds": ([0.3, 1.0], 0.25),
            "bubbles too wide": ("invalid_args", "$.args.area.radius_m"),
            "bubbles too wide: bounds": (1.0, 1.2),
            "fireworks too wide": ("invalid_args", "$.args.area.radius_m"),
            "fireworks too wide: bounds": (2.0, 2.5),
            "bubbles too long": ("invalid_args", "$.args.duration_s"),
            "bubbles too long: bounds": (60, 90),
            "fireworks too long": ("invalid_args", "$.args.duration_s"),
            "fireworks too long: bounds": (6, 8),
            "fireworks outside the room": ("out_of_bounds", "$.args.area.center_m"),
            "fireworks at a spot neither avatar sees": ("target_not_found", "$.args.area.center_m"),
            "the player and too thick": ("permission_denied", "$.args.targets"),
            "three fireworks": ["ok", "ok", "ok"],
            "a fourth": (("budget_exceeded", "$.args.capability"), 3, 3),
            "the fireworks stop by id": [1, 1, 1],
            "two streams": ["ok", "ok"],
            "a third stream": (("budget_exceeded", "$.args.capability"), 2, 2),
            "a glow still fits": "ok",
            "the rest stop by id": [1, 1, 1],
        }
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

    async def companion_home(self) -> None:
        pass  # a fresh mock host per test: the companion is at its spawn

    async def wait(self, seconds: float) -> None:
        self.host.clock.advance(seconds)


if __name__ == "__main__":
    unittest.main()
