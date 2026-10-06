"""The proposed link schema (companion/schemas/companion-link.schema.json) matches what the link sends."""
from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from jsonschema import Draft202012Validator
from referencing import Registry, Resource
from referencing.jsonschema import DRAFT202012
from support import COMPANION_DIR, CONTRACTS, command, new_host, query

from enfractal_companion import link
from enfractal_companion.link import LinkClient, LinkServer

SCHEMA_PATH = COMPANION_DIR / "schemas" / "companion-link.schema.json"


def link_validator() -> Draft202012Validator:
    resources = []
    for path in [CONTRACTS / "common.schema.json", SCHEMA_PATH]:
        contents = json.loads(path.read_bytes())
        resources.append((contents["$id"], Resource.from_contents(contents, default_specification=DRAFT202012)))
    schema = json.loads(SCHEMA_PATH.read_bytes())
    Draft202012Validator.check_schema(schema)
    return Draft202012Validator(schema, registry=Registry().with_resources(resources))


class LinkSchema(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.validator = link_validator()
        self.host = new_host()

    def assertValid(self, document):
        errors = [e.message for e in self.validator.iter_errors(document)]
        self.assertEqual(errors, [], document)

    async def test_every_frame_and_the_session_file_validate(self):
        sent: list[dict] = []
        real = link.encode_frame

        def recording(document):
            sent.append(json.loads(json.dumps(document)))
            return real(document)

        with tempfile.TemporaryDirectory() as tmp, mock.patch.object(link, "encode_frame", recording):
            session = Path(tmp) / "session.json"
            server = LinkServer(self.host.handle, self.host.room_id)
            await server.start(session)
            self.assertValid(json.loads(session.read_bytes()))
            client = LinkClient.from_file(session)
            await client.request(query("observe", {"actor": "avatar:companion"}))
            await client.request(command("goal.set", {"actor": "avatar:companion", "goal": "follow"}, "f-1"))
            second = LinkClient.from_file(session)
            with self.assertRaises(link.LinkError):
                await second.connect()  # refused: busy
            await client.close()
            await second.close()
            await server.close()
        types = [frame["type"] for frame in sent]
        for expected in ("hello", "challenge", "auth", "ready", "request", "response", "refused"):
            self.assertIn(expected, types)
        for frame in sent:
            with self.subTest(frame=frame["type"]):
                self.assertValid(frame)

    def test_the_schema_refuses_frames_that_carry_authority(self):
        for frame in ({"type": "request", "seq": 1, "message": {}, "principal": "player:local"},
                      {"type": "auth", "client_proof": "a" * 64, "token": "b" * 64},
                      {"type": "player_decision", "request_id": "c" * 32, "approve": True},
                      {"type": "hello", "protocol": "enfractal.companion_link", "version": 1, "client_nonce": "d" * 64,
                       "principal": "player:local"}):
            with self.subTest(frame=frame["type"]):
                self.assertFalse(self.validator.is_valid(frame))

    def test_the_schema_refuses_a_session_file_off_loopback(self):
        document = {"schema": "enfractal.companion_session", "version": 1, "host": "10.0.0.2", "port": 4000,
                    "token": "e" * 64, "room_id": "test_room", "pid": 1, "created_utc": "2026-10-06T00:00:00Z"}
        self.assertFalse(self.validator.is_valid(document))
        document["host"] = "127.0.0.1"
        self.assertTrue(self.validator.is_valid(document))


if __name__ == "__main__":
    unittest.main()
