"""The swap (A2): the boundary and embodiment tests against the real game host, not the mock.

One Godot process runs the test room headless with the kernel's command host and the C# end of the companion link
(game/scripts/native/Companion/), in a temporary user folder (enfractal_companion.real_game). The tests reach it the
way the MCP server does: through the session file and the loopback link, as companion:local. They cover
- the link's own cases (test_link.py's, now against the C# listener): the handshake, the token, busy, frame limits,
  frames that carry a principal or an approval, malformed frames;
- the security boundary at the real host: principals and approvals smuggled in, player-only ops, the player's avatar,
  unknown and foreign ids, the room, hidden characters, the rate limit with stops exempt, results always this
  principal's and contract-valid;
- follow, come, look_at and point_at as goals through the kernel, their jobs, the visible state on the avatar, and
  goal.stop always working;
- the MCP server itself (the official SDK's stdio client) driving the real host.

Skipped when this checkout cannot run the game (no Godot .NET, no .NET SDK, or the C# project not built yet: the
Windows and Linux runners build it before this suite runs).
"""
from __future__ import annotations

import asyncio
import json
import secrets
import struct
import sys
import time
import unittest

from support import CONTRACTS, COMPANION, SRC, command, contract_problems, query
from test_link import closed, raw_connect

from enfractal_companion import link
from enfractal_companion.link import LinkClient, LinkError, encode_frame, parse_session, read_frame
from enfractal_companion.real_game import GameNotAvailable, RealGame

GAME: RealGame | None = None
WHY_NOT = ""


def setUpModule():
    global GAME, WHY_NOT
    try:
        GAME = RealGame()
    except GameNotAvailable as error:
        WHY_NOT = str(error)
        return
    GAME.start()


def tearDownModule():
    if GAME is None:
        return
    GAME.stop()
    errors = GAME.errors()
    if errors:
        raise AssertionError("the game wrote to stderr:\n" + "\n".join(errors[:20]))


def companion_goal(goal: str, action_id: str, **args) -> dict:
    return command("goal.set", {"actor": "avatar:companion", "goal": goal, **args}, action_id)


def fresh_id(prefix: str) -> str:
    return f"{prefix}-{secrets.token_hex(6)}"


class RealHostCase(unittest.IsolatedAsyncioTestCase):
    """A fresh link session per test (the host clears the companion's perception memory with each one)."""

    def setUp(self):
        if GAME is None:
            self.skipTest(f"the real game cannot run here: {WHY_NOT}")
        self.assertTrue(GAME.running, "the game stopped:\n" + "\n".join(GAME.lines[-30:]))
        self.info = parse_session(GAME.session_path.read_bytes())
        self.results: list[dict] = []

    async def asyncSetUp(self):
        self.clients: list[LinkClient] = []

    async def asyncTearDown(self):
        for client in self.clients:
            await client.close()
        await asyncio.sleep(0.1)  # let the game see the session end before the next test connects
        for result in self.results:
            self.assertEqual(contract_problems(result), [], result)
            self.assertEqual((result["principal"], result["room_id"]), (COMPANION, "test_room"), result)

    def client(self, **kwargs) -> LinkClient:
        client = LinkClient.from_file(GAME.session_path, **kwargs)
        self.clients.append(client)
        return client

    async def ask(self, client: LinkClient, message: dict) -> dict:
        result = await client.request(message)
        self.results.append(result)
        return result

    @staticmethod
    async def line(pattern: str, timeout_s: float, after: int) -> str:
        """Wait for the game to print a line, off the event loop."""
        return await asyncio.to_thread(GAME.wait_for, pattern, timeout_s, after=after)

    async def settle_rate(self):
        """The host allows 30 messages a second per companion; tests that send many wait the window out."""
        await asyncio.sleep(1.1)

    async def job_state(self, client: LinkClient, job_id: str, timeout_s: float = 15.0) -> dict:
        """Poll jobs.status until the job ends (or the time is up)."""
        deadline = time.monotonic() + timeout_s
        while True:
            status = await self.ask(client, query("jobs.status", {"job_id": job_id}, fresh_id("q")))
            self.assertTrue(status["ok"], status)
            if status["data"]["state"] != "running" or time.monotonic() > deadline:
                return status["data"]
            await asyncio.sleep(0.25)


# ---------------------------------------------------------------------------- the link, against the C# listener

class LinkHandshake(RealHostCase):
    async def test_an_authenticated_client_gets_the_companion_principal_from_the_game(self):
        client = self.client()
        ready = await client.connect()
        self.assertEqual((ready["principal"], ready["avatar"], ready["room_id"]), (COMPANION, "avatar:companion", "test_room"))
        self.assertEqual(ready["limits"], {"max_request_frame": link.MAX_REQUEST_FRAME, "max_response_frame": link.MAX_RESPONSE_FRAME})
        result = await self.ask(client, query("room.describe", {}))
        self.assertTrue(result["ok"], result)

    async def test_refuses_a_client_that_does_not_know_the_token(self):
        reader, writer, reply = await raw_connect(self.info, client_proof="0" * 64)
        self.assertEqual(reply, {"type": "refused", "code": "auth_failed"})
        self.assertTrue(await closed(reader))
        writer.close()

    async def test_proofs_that_are_not_lowercase_hex_are_refused_not_left_hanging(self):
        for proof in (chr(0xE9), "g" * 64, "A" * 64, "a" * 63):
            with self.subTest(proof=ascii(proof)):
                reader, writer, reply = await raw_connect(self.info, client_proof=proof)
                self.assertEqual(reply, {"type": "refused", "code": "auth_failed"})
                writer.close()

    async def test_the_game_proves_the_session_before_the_companion_sends_anything(self):
        # raw_connect checks the game's proof against the session token before it sends its own.
        reader, writer, reply = await raw_connect(self.info, verify_server=True)
        self.assertEqual(reply["type"], "ready")
        writer.close()

    async def test_the_token_never_crosses_the_socket(self):
        wire = bytearray()

        async def proxy(reader, writer):
            upstream_reader, upstream_writer = await asyncio.open_connection(self.info.host, self.info.port)

            async def pump(src, dst):
                try:
                    while data := await src.read(65536):
                        wire.extend(data)
                        dst.write(data)
                        await dst.drain()
                except (ConnectionError, OSError):
                    pass
                finally:
                    dst.close()

            await asyncio.gather(pump(reader, upstream_writer), pump(upstream_reader, writer))

        relay = await asyncio.start_server(proxy, "127.0.0.1", 0)
        info = link.SessionInfo("127.0.0.1", relay.sockets[0].getsockname()[1], self.info.token, "test_room")
        client = LinkClient(lambda: info)
        self.clients.append(client)
        self.results.append(await client.request(query("observe", {"actor": "avatar:companion"})))
        await client.close()
        relay.close()
        await relay.wait_closed()
        self.assertGreater(len(wire), 100)
        for needle in (self.info.token.encode(), self.info.token.upper().encode(), bytes.fromhex(self.info.token)):
            self.assertNotIn(needle, bytes(wire))

    async def test_refuses_a_second_companion_while_one_is_connected(self):
        first = self.client()
        await first.connect()
        with self.assertRaisesRegex(LinkError, r"refused the connection \(busy\)"):
            await self.client().connect()
        self.assertTrue((await self.ask(first, query("room.describe", {})))["ok"])

    async def test_failed_proofs_never_lock_out_the_real_companion(self):
        for _ in range(20):
            reader, writer, reply = await raw_connect(self.info, client_proof="f" * 64)
            self.assertEqual(reply["code"], "auth_failed")
            writer.close()
        self.assertEqual((await self.client().connect())["type"], "ready")

    async def test_refuses_malformed_hellos(self):
        for hello in ({"type": "hello", "protocol": "other", "version": 1, "client_nonce": "0" * 64},
                      {"type": "hello", "protocol": link.PROTOCOL, "version": 2, "client_nonce": "0" * 64},
                      {"type": "hello", "protocol": link.PROTOCOL, "version": 1, "client_nonce": "short"},
                      {"type": "hello", "protocol": link.PROTOCOL, "version": 1, "client_nonce": "0" * 64,
                       "principal": "player:local"}):
            with self.subTest(hello=hello):
                reader, writer = await asyncio.open_connection(self.info.host, self.info.port)
                writer.write(encode_frame(hello))
                await writer.drain()
                self.assertEqual(await read_frame(reader, 4096), {"type": "refused", "code": "hello_invalid"})
                self.assertTrue(await closed(reader))
                writer.close()

    async def test_a_handshake_frame_over_its_limit_is_refused(self):
        reader, writer = await asyncio.open_connection(self.info.host, self.info.port)
        writer.write(struct.pack(">I", 5000))
        await writer.drain()
        self.assertEqual(await read_frame(reader, 4096), {"type": "refused", "code": "handshake_invalid"})
        writer.close()

    async def test_caps_unauthenticated_connections_at_eight(self):
        idle = [await asyncio.open_connection(self.info.host, self.info.port) for _ in range(link.MAX_PENDING_HANDSHAKES)]
        await asyncio.sleep(0.2)
        reader, writer = await asyncio.open_connection(self.info.host, self.info.port)
        self.assertEqual(await read_frame(reader, 4096), {"type": "refused", "code": "busy"})
        writer.close()
        for _r, w in idle:
            w.close()
        await asyncio.sleep(0.2)
        self.assertEqual((await self.client().connect())["type"], "ready")

    async def test_refuses_a_client_that_stalls_the_handshake(self):
        reader, writer = await asyncio.open_connection(self.info.host, self.info.port)
        started = time.monotonic()
        reply = await asyncio.wait_for(read_frame(reader, 4096), link.HANDSHAKE_TIMEOUT_S + 5)
        self.assertEqual(reply, {"type": "refused", "code": "handshake_timeout"})
        self.assertGreaterEqual(time.monotonic() - started, link.HANDSHAKE_TIMEOUT_S - 0.5)
        writer.close()

    async def test_a_new_session_follows_a_closed_one(self):
        client = self.client()
        self.assertTrue((await self.ask(client, query("room.describe", {})))["ok"])
        await client.close()
        await asyncio.sleep(0.2)
        self.assertTrue((await self.ask(client, query("room.describe", {}, "q-2")))["ok"])


class LinkSessionFile(RealHostCase):
    def test_the_session_file_is_lf_utf8_json_naming_a_loopback_literal(self):
        raw = GAME.session_path.read_bytes()
        self.assertNotIn(b"\r", raw)
        self.assertFalse(raw.startswith(b"\xef\xbb\xbf"))
        document = json.loads(raw)
        self.assertEqual(set(document), {"schema", "version", "host", "port", "token", "room_id", "pid", "created_utc"})
        self.assertEqual((document["schema"], document["version"], document["host"], document["room_id"]),
                         ("enfractal.companion_session", 1, "127.0.0.1", "test_room"))
        self.assertEqual(parse_session(raw).port, self.info.port)
        self.assertEqual(sorted(p.name for p in GAME.session_path.parent.iterdir()), ["session.json"])  # no temp file left

    def test_the_game_never_prints_its_token(self):
        self.assertFalse(any(self.info.token in line for line in GAME.lines))


class LinkFrames(RealHostCase):
    async def authenticated(self):
        reader, writer, reply = await raw_connect(self.info)
        self.assertEqual(reply["type"], "ready")
        self.addAsyncCleanup(self._close, writer)
        return reader, writer

    @staticmethod
    async def _close(writer):
        writer.close()

    async def send(self, writer, document):
        writer.write(encode_frame(document))
        await writer.drain()

    async def test_refuses_an_approval_frame_from_the_companion_connection(self):
        reader, writer = await self.authenticated()
        await self.send(writer, {"type": "player_decision", "request_id": "ab" * 16, "approve": True})
        self.assertEqual(await read_frame(reader, 4096), {"type": "refused", "code": "frame_invalid"})
        self.assertTrue(await closed(reader))

    async def test_refuses_a_principal_on_the_request_frame(self):
        reader, writer = await self.authenticated()
        await self.send(writer, {"type": "request", "seq": 1, "principal": "player:local", "message": query("room.describe", {})})
        self.assertEqual(await read_frame(reader, 4096), {"type": "refused", "code": "frame_invalid"})

    async def test_refuses_an_oversized_frame_without_reading_it(self):
        reader, writer = await self.authenticated()
        writer.write(struct.pack(">I", 50_000_000))
        await writer.drain()
        self.assertEqual(await read_frame(reader, 4096), {"type": "refused", "code": "frame_invalid"})
        self.assertTrue(await closed(reader))

    async def test_refuses_frames_with_duplicate_keys_or_bad_bytes(self):
        for body in (b'{"type":"request","seq":1,"seq":2,"message":{}}',
                     b'{"type":"request","seq":1,"message":{"op":"a","op":"b"}}',
                     b'{"type":"request","seq":1,"message":{"note":"\xff"}}',
                     b'\xef\xbb\xbf{"type":"request","seq":1,"message":{}}',
                     b'{"type":"request","seq":1,"message":{"n":1e999}}',
                     b'[1]', b'{"type":"request","seq":0,"message":{}}', b'{"type":"request","seq":1.5,"message":{}}'):
            with self.subTest(body=body[:50]):
                reader, writer = await self.authenticated()
                writer.write(struct.pack(">I", len(body)) + body)
                await writer.drain()
                self.assertEqual(await read_frame(reader, 4096), {"type": "refused", "code": "frame_invalid"})

    async def test_a_principal_inside_the_message_is_refused_and_never_adopted(self):
        message = command("entity.grab", {"target": "obj:book"}, "grab-1")
        message["principal"] = "player:local"
        result = await self.ask(self.client(), message)
        self.assertEqual((result["error"]["code"], result["principal"]), ("field_unknown", COMPANION))

    async def test_a_message_over_the_command_limit_inside_a_legal_frame_is_answered(self):
        result = await self.ask(self.client(), query("entities.list", {"cursor": "1" * 70_000}))
        self.assertEqual(result["error"]["code"], "request_invalid")

    async def test_malformed_messages_are_answered_not_closed(self):
        client = self.client()
        deep: dict = {}
        cursor = deep
        for _ in range(100):
            cursor["a"] = {}
            cursor = cursor["a"]
        for message in ({"schema": "enfractal.command"}, [], "text", {"schema": "enfractal.query", "version": 1, "deep": deep}):
            with self.subTest(message=str(message)[:40]):
                result = await self.ask(client, message)
                self.assertEqual(result["error"]["code"], "request_invalid")
        self.assertTrue((await self.ask(client, query("room.describe", {})))["ok"])

    async def test_the_client_still_refuses_to_send_a_frame_over_the_limit(self):
        with self.assertRaisesRegex(LinkError, "too large"):
            await self.client().request(query("entities.list", {"cursor": "1" * 200_000}))


# ---------------------------------------------------------------------------- the boundary, at the real host

class Boundary(RealHostCase):
    async def test_refuses_principals_smuggled_into_the_envelope_or_the_args(self):
        client = self.client()
        envelope = companion_goal("stay", fresh_id("p"))
        envelope["principal"] = "player:local"
        args = companion_goal("stay", fresh_id("p"), principal="player:local")
        for message, path in ((envelope, "$.principal"), (args, "$.args.principal")):
            with self.subTest(path=path):
                result = await self.ask(client, message)
                self.assertEqual((result["error"]["code"], result["error"]["field_path"]), ("field_unknown", path))

    async def test_refuses_approvals_carried_in_a_command(self):
        client = self.client()
        for key in ("approval", "approved_by"):
            message = companion_goal("stay", fresh_id("a"))
            message[key] = "player:local"
            with self.subTest(key=key):
                self.assertEqual((await self.ask(client, message))["error"]["code"], "field_unknown")

    async def test_refuses_the_player_only_ops(self):
        client = self.client()
        unlock = await self.ask(client, command("protect.unlock", {"targets": ["obj:box"]}, fresh_id("u"), expected_revision=0))
        physics = await self.ask(client, command("world.set_physics", {"preset": "room_floaty"}, fresh_id("w")))
        self.assertEqual((unlock["error"]["code"], unlock["error"]["field_path"]), ("permission_denied", "$.op"))
        self.assertEqual(physics["error"]["code"], "permission_denied")

    async def test_the_companion_never_acts_or_perceives_through_the_players_avatar(self):
        client = self.client()
        for message in (command("goal.set", {"actor": "avatar:player", "goal": "stay"}, fresh_id("g")),
                        command("goal.stop", {"actor": "avatar:player"}, fresh_id("s")),
                        query("observe", {"actor": "avatar:player"})):
            with self.subTest(op=message["op"]):
                self.assertEqual((await self.ask(client, message))["error"]["code"], "actor_denied")

    async def test_unknown_and_foreign_ids_are_indistinguishable(self):
        client = self.client()
        answers = []
        for target in ("obj:not_here", "creation:00000099", "obj:paint_clutter"):
            result = await self.ask(client, query("entity.inspect", {"target": target}, "q-same"))
            self.assertEqual(result["error"]["code"], "target_not_found")
            answers.append(json.dumps(result["error"], sort_keys=True))
        self.assertEqual(len(set(answers)), 1, answers)

    async def test_refuses_a_message_for_another_room(self):
        result = await self.ask(self.client(), query("room.describe", {}, room_id="garage_example"))
        self.assertEqual(result["error"]["code"], "room_mismatch")

    async def test_refuses_hidden_characters_anywhere_in_a_request(self):
        client = self.client()
        for text in ("zero​width", "tag\U000E0041", "bidi‮", "selector️"):
            with self.subTest(text=ascii(text)):
                result = await self.ask(client, companion_goal("stay", fresh_id("t"), **{}) | {"note": text})
                self.assertEqual(result["error"]["code"], "request_invalid")

    async def test_refuses_invented_ops_and_answers_approvals_it_never_held_as_unknown(self):
        client = self.client()
        invented = command("approval.grant", {"request_id": "ab" * 16}, fresh_id("i"))
        self.assertEqual((await self.ask(client, invented))["error"]["code"], "request_invalid")
        status = await self.ask(client, query("approval.status", {"request_id": "ab" * 16}))
        self.assertEqual(status["error"]["code"], "target_not_found")

    async def test_the_rate_limit_holds_and_stops_are_never_limited(self):
        await self.settle_rate()
        client = self.client()
        await client.connect()
        answers = [await self.ask(client, query("room.describe", {}, f"q-{i}")) for i in range(40)]
        limited = [a for a in answers if not a["ok"]]
        self.assertTrue(limited, "forty queries at once were never rate limited")
        self.assertTrue(all(a["error"]["code"] == "rate_limited" and a["error"]["retryable"] for a in limited))
        stop = await self.ask(client, command("goal.stop", {"actor": "avatar:companion"}, fresh_id("stop")))
        self.assertTrue(stop["ok"], stop)
        await self.settle_rate()

    async def test_goal_stop_always_applies(self):
        await self.settle_rate()
        client = self.client()
        follow = await self.ask(client, companion_goal("follow", "follow-stop-1", target="avatar:player"))
        self.assertTrue(follow["ok"], follow)
        # Malformed expectations and a reused action id: a stop still applies, every time.
        for _ in range(3):
            stop = command("goal.stop", {"actor": "avatar:companion"}, "follow-stop-1",
                           expected_entities={"obj:not_here": 7}, expected_revision=999)
            result = await self.ask(client, stop)
            self.assertTrue(result["ok"], result)
            self.assertFalse(result["replayed"])
        self.assertEqual((await self.job_state(client, follow["job_id"], 0))["state"], "cancelled")
        # With the rate budget spent, a stop still applies.
        for i in range(35):
            await client.request(query("room.describe", {}, f"q-flood-{i}"))
        stop = await self.ask(client, command("goal.stop", {}, fresh_id("stop")))
        self.assertTrue(stop["ok"], stop)
        await self.settle_rate()


# ---------------------------------------------------------------------------- embodiment, through the kernel

class Embodiment(RealHostCase):
    async def asyncSetUp(self):
        await super().asyncSetUp()
        await self.settle_rate()
        self.marker = len(GAME.lines)

    async def visible_target(self, client: LinkClient) -> str:
        observed = await self.ask(client, query("observe", {"actor": "avatar:companion"}, fresh_id("q")))
        self.assertTrue(observed["ok"], observed)
        ids = [item["id"] for item in observed["data"]["visible"] if item["kind"] == "object"]
        self.assertTrue(ids, observed)
        return ids[0]

    async def test_look_at_turns_the_companion_and_its_job_succeeds(self):
        client = self.client()
        target = await self.visible_target(client)
        result = await self.ask(client, companion_goal("look_at", fresh_id("look"), target=target))
        self.assertTrue(result["ok"], result)
        self.assertEqual(result["data"]["target_seen"], "now")
        self.assertEqual((await self.job_state(client, result["job_id"]))["state"], "succeeded")

    async def test_point_at_points_and_its_job_succeeds(self):
        client = self.client()
        target = await self.visible_target(client)
        result = await self.ask(client, companion_goal("point_at", fresh_id("point"), target=target))
        self.assertTrue(result["ok"], result)
        self.assertEqual((await self.job_state(client, result["job_id"]))["state"], "succeeded")
        at_a_place = await self.ask(client, companion_goal("point_at", fresh_id("point"), position_m=[0.5, 0.0, 0.5]))
        self.assertTrue(at_a_place["ok"], at_a_place)
        self.assertNotIn("job_id", at_a_place)  # a place is not a thing: no job to re-check

    async def test_come_brings_the_companion_to_the_player(self):
        client = self.client()
        result = await self.ask(client, companion_goal("come", fresh_id("come"), target="avatar:player"))
        self.assertTrue(result["ok"], result)
        self.assertEqual((await self.job_state(client, result["job_id"]))["state"], "succeeded")
        without_target = await self.ask(client, companion_goal("come", fresh_id("come")))
        self.assertTrue(without_target["ok"], without_target)

    async def test_follow_runs_until_replaced_or_stopped(self):
        client = self.client()
        result = await self.ask(client, companion_goal("follow", fresh_id("follow"), target="avatar:player"))
        self.assertTrue(result["ok"], result)
        await asyncio.sleep(0.5)
        self.assertEqual((await self.job_state(client, result["job_id"], 0))["state"], "running")
        stop = await self.ask(client, command("goal.stop", {}, fresh_id("stop")))
        self.assertTrue(stop["ok"], stop)
        self.assertEqual((await self.job_state(client, result["job_id"], 0))["state"], "cancelled")
        again = await self.ask(client, companion_goal("follow", fresh_id("follow")))
        self.assertTrue(again["ok"], again)

    async def test_the_avatar_shows_listening_planning_and_acting(self):
        client = self.client()
        await client.connect()
        await self.line(r"COMPANION_LINK session start", 5, self.marker)
        await self.ask(client, query("observe", {"actor": "avatar:companion"}, fresh_id("q")))
        await self.line(r"COMPANION_STATE planning", 5, self.marker)
        await self.ask(client, companion_goal("look_at", fresh_id("look"), position_m=[-1.5, 0.0, -1.5]))
        came = await self.ask(client, companion_goal("come", fresh_id("come"), target="avatar:player"))
        self.assertEqual((await self.job_state(client, came["job_id"]))["state"], "succeeded")
        finished = len(GAME.lines)
        # Nothing more from the AI: planning gives way to listening once its hold is over.
        await self.line(r"COMPANION_STATE listening", 10, finished)
        await client.close()
        await self.line(r"COMPANION_STATE offline", 5, finished)
        states = GAME.states(self.marker)
        self.assertIn("acting", states)
        self.assertLess(states.index("planning"), states.index("acting"))
        self.assertEqual(states[-2:], ["listening", "offline"])

    async def test_go_to_walks_the_companion_to_a_thing(self):
        """go_to needs the body's go-to (Lane P's CompanionAvatar.GoTo, docs/companion/proposals/a2-real-host.md);
        until it lands the host refuses go_to with unsupported_capability."""
        client = self.client()
        result = await self.ask(client, companion_goal("go_to", fresh_id("go"), target="obj:doorstop"))
        if not result["ok"] and result["error"]["code"] == "unsupported_capability":
            self.skipTest("the real host does not walk to things yet (needs the body's go-to)")
        self.assertTrue(result["ok"], result)
        self.assertEqual((await self.job_state(client, result["job_id"], 30))["state"], "succeeded")
        me = await self.ask(client, query("entity.inspect", {"target": "avatar:companion"}, fresh_id("q")))
        doorstop = await self.ask(client, query("entity.inspect", {"target": "obj:doorstop"}, fresh_id("q")))
        position, box = me["data"]["entity"]["position_m"], doorstop["data"]["entity"]["bounds_m"]
        dx = max(box["min_m"][0] - position[0], 0, position[0] - box["max_m"][0])
        dz = max(box["min_m"][2] - position[2], 0, position[2] - box["max_m"][2])
        self.assertLessEqual((dx * dx + dz * dz) ** 0.5, 0.1, (position, box))
        back = await self.ask(client, companion_goal("come", fresh_id("come"), target="avatar:player"))
        self.assertEqual((await self.job_state(client, back["job_id"], 30))["state"], "succeeded")

    async def test_fetch_through_the_real_host(self):
        """Fetch needs Lane P's sandbox verbs in the host (P3) and the body's go-to; until both land the host refuses
        it with unsupported_capability. This test runs the whole fetch once it does (test_fetch.py pins the lifecycle)."""
        client = self.client()
        result = await self.ask(client, companion_goal("fetch", fresh_id("fetch"), target="obj:doorstop"))
        if not result["ok"] and result["error"]["code"] == "unsupported_capability":
            self.skipTest("the real host does not fetch yet (needs P3's verbs and the body's go-to)")
        self.assertTrue(result["ok"], result)
        self.assertEqual((await self.job_state(client, result["job_id"], 60))["state"], "succeeded")
        held = await self.ask(client, query("entity.inspect", {"target": "obj:doorstop"}, fresh_id("q")))
        self.assertEqual(held["data"]["entity"].get("held_by"), "avatar:companion")
        released = await self.ask(client, command("entity.release", {"actor": "avatar:companion"}, fresh_id("down")))
        self.assertTrue(released["ok"], released)


# ---------------------------------------------------------------------------- the MCP server against the real host

def server_parameters(*extra: str):
    from mcp import StdioServerParameters
    return StdioServerParameters(command=sys.executable, args=["-m", "enfractal_companion", "--log-level", "ERROR",
                                                               "--contracts-dir", str(CONTRACTS), *extra],
                                 env={"PYTHONPATH": str(SRC)})


class McpClientOnTheRealHost(RealHostCase):
    async def test_an_mcp_client_drives_follow_come_look_and_point_through_the_real_host(self):
        from mcp import Client
        await self.settle_rate()
        async with Client(server_parameters("--session-file", str(GAME.session_path)), mode="auto") as client:
            tools = {tool.name for tool in (await client.list_tools()).tools}
            self.assertEqual(len(tools), 29)
            self.assertNotIn("protect_unlock", tools)
            self.assertNotIn("world_set_physics", tools)

            async def call(name, arguments):
                result = (await client.call_tool(name, arguments)).structured_content
                self.results.append(result)
                return result

            observed = await call("observe", {})
            self.assertTrue(observed["ok"], observed)
            target = next(item["id"] for item in observed["data"]["visible"] if item["kind"] == "object")
            jobs = []
            for goal, extra in (("look_at", {"target": target}), ("point_at", {"target": target}),
                                ("come", {"target": "avatar:player"})):
                result = await call("goal_set", {"action_id": fresh_id(goal), "goal": goal, **extra})
                self.assertTrue(result["ok"], result)
                for _ in range(60):
                    status = await call("jobs_status", {"job_id": result["job_id"]})
                    if status["data"]["state"] != "running":
                        break
                    await asyncio.sleep(0.25)
                jobs.append((goal, status["data"]["state"]))
            self.assertEqual(jobs, [("look_at", "succeeded"), ("point_at", "succeeded"), ("come", "succeeded")])
            follow = await call("goal_set", {"action_id": fresh_id("follow"), "goal": "follow"})
            self.assertTrue(follow["ok"], follow)
            stop = await call("goal_stop", {})
            self.assertTrue(stop["ok"], stop)
            unlock = await client.call_tool("goal_set", {"action_id": fresh_id("x"), "goal": "stay", "principal": "player:local"})
            self.assertEqual(unlock.structured_content["error"]["code"], "field_unknown")


if __name__ == "__main__":
    unittest.main()
