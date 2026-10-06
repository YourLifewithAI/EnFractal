"""Boundary tests for the companion link: the loopback connection with a per-session token."""
from __future__ import annotations

import asyncio
import json
import secrets
import struct
import tempfile
import unittest
from pathlib import Path

from support import COMPANION, command, contract_problems, new_host, query

from enfractal_companion import link
from enfractal_companion.link import (
    LinkClient, LinkError, LinkServer, SessionInfo, encode_frame, parse_session, read_frame, write_session_file,
)


async def raw_connect(info: SessionInfo, *, client_proof: str | None = None, verify_server: bool = True):
    """A hand-rolled client, so tests can misbehave in ways LinkClient never would."""
    reader, writer = await asyncio.open_connection(info.host, info.port)
    nonce = secrets.token_hex(32)
    writer.write(encode_frame({"type": "hello", "protocol": link.PROTOCOL, "version": 1, "client_nonce": nonce}))
    await writer.drain()
    challenge = await read_frame(reader, 4096)
    if challenge.get("type") != "challenge":
        return reader, writer, challenge
    if verify_server:
        assert challenge["server_proof"] == link._proof(info.token, link._SERVER_LABEL, nonce, challenge["server_nonce"])
    proof = client_proof or link._proof(info.token, link._CLIENT_LABEL, nonce, challenge["server_nonce"])
    writer.write(encode_frame({"type": "auth", "client_proof": proof}))
    await writer.drain()
    reply = await read_frame(reader, 4096)
    return reader, writer, reply


async def closed(reader: asyncio.StreamReader) -> bool:
    try:
        data = await asyncio.wait_for(reader.read(1), 2)
    except (ConnectionError, OSError):
        return True
    return data == b""


class LinkCase(unittest.IsolatedAsyncioTestCase):
    handshake_timeout_s = link.HANDSHAKE_TIMEOUT_S

    def setUp(self):
        self.host = new_host()  # loads the room from disk; keep it off the event loop

    async def asyncSetUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="enfractal-link-")
        self.session_path = Path(self.tmp.name) / "session.json"
        self.server = LinkServer(self.host.handle, self.host.room_id, handshake_timeout_s=self.handshake_timeout_s)
        self.info = await self.server.start(self.session_path)
        self.clients: list[LinkClient] = []

    async def asyncTearDown(self):
        for client in self.clients:
            await client.close()
        await self.server.close()
        self.tmp.cleanup()
        for result in self.host.emitted:
            self.assertEqual(contract_problems(result), [], result)

    def client(self, **kwargs) -> LinkClient:
        client = LinkClient.from_file(self.session_path, **kwargs)
        self.clients.append(client)
        return client


class Handshake(LinkCase):
    async def test_authenticated_client_gets_the_companion_principal_from_the_game(self):
        client = self.client()
        ready = await client.connect()
        self.assertEqual((ready["principal"], ready["avatar"], ready["room_id"]),
                         (COMPANION, "avatar:companion", "test_room"))
        result = await client.request(query("room.describe", {}))
        self.assertTrue(result["ok"])
        self.assertEqual(result["principal"], COMPANION)

    async def test_refuses_a_client_that_does_not_know_the_token(self):
        reader, writer, reply = await raw_connect(self.info, client_proof="0" * 64)
        self.assertEqual(reply, {"type": "refused", "code": "auth_failed"})
        self.assertTrue(await closed(reader))
        writer.close()
        self.assertEqual(self.host.emitted, [])

    async def test_client_refuses_a_listener_that_cannot_prove_the_session(self):
        received: list[dict] = []

        async def squatter(reader, writer):
            hello = await read_frame(reader, 4096)
            received.append(hello)
            writer.write(encode_frame({"type": "challenge", "protocol": link.PROTOCOL, "version": 1,
                                       "server_nonce": secrets.token_hex(32), "server_proof": secrets.token_hex(32)}))
            await writer.drain()
            try:
                received.append(await read_frame(reader, 1 << 20))
            except (asyncio.IncompleteReadError, ConnectionError, LinkError):
                pass
            finally:
                writer.close()

        fake = await asyncio.start_server(squatter, "127.0.0.1", 0)
        port = fake.sockets[0].getsockname()[1]
        info = SessionInfo("127.0.0.1", port, self.info.token, "test_room")
        client = LinkClient(lambda: info)
        with self.assertRaisesRegex(LinkError, "could not prove"):
            await client.request(command("goal.set", {"actor": "avatar:companion", "goal": "follow"}, "f-1"))
        await asyncio.sleep(0.05)
        fake.close()
        await fake.wait_closed()
        self.assertEqual([frame["type"] for frame in received], ["hello"])  # no auth proof, no request

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
        info = SessionInfo("127.0.0.1", relay.sockets[0].getsockname()[1], self.info.token, "test_room")
        client = LinkClient(lambda: info)
        self.clients.append(client)
        await client.request(query("observe", {"actor": "avatar:companion"}))
        await client.close()
        relay.close()
        await relay.wait_closed()
        self.assertGreater(len(wire), 100)
        token = self.info.token
        for needle in (token.encode(), token.upper().encode(), bytes.fromhex(token)):
            self.assertNotIn(needle, bytes(wire))

    async def test_refuses_a_second_companion_while_one_is_connected(self):
        first = self.client()
        await first.connect()
        second = self.client()
        with self.assertRaisesRegex(LinkError, r"refused the connection \(busy\)"):
            await second.connect()
        self.assertTrue((await first.request(query("room.describe", {})))["ok"])

    async def test_locks_out_after_repeated_failed_handshakes(self):
        for _ in range(link.FAILED_AUTH_LIMIT):
            reader, writer, reply = await raw_connect(self.info, client_proof="f" * 64)
            self.assertEqual(reply["code"], "auth_failed")
            writer.close()
        client = self.client()
        with self.assertRaisesRegex(LinkError, r"\(locked\)"):
            await client.connect()

    async def test_refuses_a_malformed_hello(self):
        for hello in ({"type": "hello", "protocol": "other", "version": 1, "client_nonce": "0" * 64},
                      {"type": "hello", "protocol": link.PROTOCOL, "version": 2, "client_nonce": "0" * 64},
                      {"type": "hello", "protocol": link.PROTOCOL, "version": 1, "client_nonce": "short"},
                      {"type": "hello", "protocol": link.PROTOCOL, "version": 1, "client_nonce": "0" * 64,
                       "principal": "player:local"}):
            with self.subTest(hello=hello):
                reader, writer = await asyncio.open_connection(self.info.host, self.info.port)
                writer.write(encode_frame(hello))
                await writer.drain()
                self.assertEqual((await read_frame(reader, 4096))["type"], "refused")
                writer.close()

    def test_refuses_to_listen_off_loopback(self):
        for host in ("0.0.0.0", "192.168.1.10", "::"):
            with self.subTest(host=host), self.assertRaises(ValueError):
                LinkServer(self.host.handle, "test_room", host=host)
        self.assertEqual(self.server._server.sockets[0].getsockname()[0], "127.0.0.1")


class SlowHandshake(LinkCase):
    handshake_timeout_s = 0.3

    async def test_refuses_a_client_that_stalls_the_handshake(self):
        reader, writer = await asyncio.open_connection(self.info.host, self.info.port)
        reply = await read_frame(reader, 4096)
        self.assertEqual(reply, {"type": "refused", "code": "handshake_timeout"})
        writer.close()


class Frames(LinkCase):
    async def authenticated(self):
        reader, writer, reply = await raw_connect(self.info)
        self.assertEqual(reply["type"], "ready")
        self.addAsyncCleanup(self._close, writer)
        return reader, writer

    @staticmethod
    async def _close(writer):
        writer.close()

    async def send_frame(self, writer, document):
        writer.write(encode_frame(document))
        await writer.drain()

    async def test_refuses_an_approval_frame_from_the_companion_connection(self):
        held = self.host.handle(COMPANION, command("entity.remove", {"target": "obj:box"}, "rm-1",
                                                   expected_entities={"obj:box": 0}))
        request_id = held["approval_needed"]["request_id"]
        reader, writer = await self.authenticated()
        await self.send_frame(writer, {"type": "player_decision", "request_id": request_id, "approve": True})
        self.assertEqual(await read_frame(reader, 4096), {"type": "refused", "code": "frame_invalid"})
        self.assertTrue(await closed(reader))
        self.assertEqual(self.host.approvals[request_id].state, "pending")
        self.assertFalse(self.host.entities["obj:box"].removed)

    async def test_refuses_a_principal_on_the_request_frame(self):
        reader, writer = await self.authenticated()
        await self.send_frame(writer, {"type": "request", "seq": 1, "principal": "player:local",
                                       "message": query("room.describe", {})})
        self.assertEqual(await read_frame(reader, 4096), {"type": "refused", "code": "frame_invalid"})
        self.assertEqual(self.host.emitted, [])

    async def test_principal_inside_the_message_is_refused_and_never_adopted(self):
        client = self.client()
        message = command("entity.grab", {"target": "obj:book"}, "grab-1")
        message["principal"] = "player:local"
        result = await client.request(message)
        self.assertEqual(result["error"]["code"], "field_unknown")
        self.assertEqual(result["principal"], COMPANION)

    async def test_refuses_an_oversized_frame_without_reading_it(self):
        reader, writer = await self.authenticated()
        writer.write(struct.pack(">I", 50_000_000))
        await writer.drain()
        self.assertEqual(await read_frame(reader, 4096), {"type": "refused", "code": "frame_invalid"})
        self.assertTrue(await closed(reader))

    async def test_refuses_a_frame_with_duplicate_keys(self):
        reader, writer = await self.authenticated()
        body = b'{"type":"request","seq":1,"seq":2,"message":{}}'
        writer.write(struct.pack(">I", len(body)) + body)
        await writer.drain()
        self.assertEqual(await read_frame(reader, 4096), {"type": "refused", "code": "frame_invalid"})

    async def test_refuses_a_message_over_the_command_limit_inside_a_legal_frame(self):
        client = self.client()
        message = query("entities.list", {"cursor": "1" * 70_000})
        result = await client.request(message)
        self.assertEqual(result["error"]["code"], "request_invalid")
        self.assertEqual(result["error"]["allowed"], 65536)

    async def test_client_refuses_to_send_a_frame_over_the_limit(self):
        client = self.client()
        with self.assertRaisesRegex(LinkError, "too large"):
            await client.request(query("entities.list", {"cursor": "1" * 200_000}))
        self.assertEqual(self.host.emitted, [])


class SessionFiles(unittest.TestCase):
    TOKEN = "ab" * 32

    def doc(self, **changes):
        document = {"schema": "enfractal.companion_session", "version": 1, "host": "127.0.0.1", "port": 40000,
                    "token": self.TOKEN, "room_id": "test_room", "pid": 1, "created_utc": "2026-10-06T00:00:00Z"}
        document.update(changes)
        return json.dumps(document).encode()

    def test_refuses_a_session_file_pointing_off_this_computer(self):
        for host in ("10.0.0.5", "example.com", "0.0.0.0", "localhost", "127.0.0.1.evil.example", "::ffff:8.8.8.8"):
            with self.subTest(host=host), self.assertRaisesRegex(LinkError, "not loopback"):
                parse_session(self.doc(host=host))

    def test_refuses_malformed_session_files(self):
        for raw in (self.doc(token="short"), self.doc(token=self.TOKEN.upper()), self.doc(port=0), self.doc(port=True),
                    self.doc(version=2), self.doc(extra="x"), self.doc(room_id="Bad Room"),
                    b'{"schema":"enfractal.companion_session","schema":"x"}', b"not json", b"[]"):
            with self.subTest(raw=raw[:60]), self.assertRaises(LinkError):
                parse_session(raw)

    def test_session_file_is_lf_bytes_and_session_info_hides_the_token(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "nested" / "session.json"
            info = SessionInfo("127.0.0.1", 41000, self.TOKEN, "test_room")
            write_session_file(path, info)
            raw = path.read_bytes()
            self.assertNotIn(b"\r", raw)
            self.assertFalse(raw.startswith(b"\xef\xbb\xbf"))
            self.assertEqual(parse_session(raw), info)
            self.assertNotIn(self.TOKEN, repr(info))
            self.assertEqual([p.name for p in path.parent.iterdir()], ["session.json"])  # no temp file left


class Reconnect(LinkCase):
    async def test_client_follows_a_restarted_game_to_its_new_session(self):
        client = self.client()
        self.assertTrue((await client.request(query("room.describe", {})))["ok"])
        old = self.info
        await self.server.close()
        self.server = LinkServer(self.host.handle, self.host.room_id)
        self.info = await self.server.start(self.session_path)
        self.assertNotEqual((old.port, old.token), (self.info.port, self.info.token))
        with self.assertRaises(LinkError):
            await client.request(query("room.describe", {}, "q-2"))  # the old connection is gone; outcome unknown
        self.assertTrue((await client.request(query("room.describe", {}, "q-3")))["ok"])

    async def test_link_errors_never_contain_the_token(self):
        messages = []
        for loader in (lambda: SessionInfo("127.0.0.1", 1, self.info.token, "test_room"),
                       lambda: SessionInfo(self.info.host, self.info.port, "cd" * 32, "test_room")):
            try:
                await LinkClient(loader).connect()
            except LinkError as error:
                messages.append(str(error))
        self.assertEqual(len(messages), 2)
        for message in messages:
            self.assertNotIn(self.info.token, message)
            self.assertNotIn("cd" * 32, message)


if __name__ == "__main__":
    unittest.main()
