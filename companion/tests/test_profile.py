"""The play-only client profile (founder decision, 6 October 2026): one MCP server, nothing else.

Any MCP client or agent harness can take the profile. These tests check what it says, and then
launch the server exactly as it says with the official SDK's stdio client: the session that
results offers the game's tools and nothing else.
"""
from __future__ import annotations

import io
import json
import re
import sys
import unittest
from contextlib import redirect_stdout
from pathlib import Path

from mcp import Client, StdioServerParameters
from support import contract_problems, contracts

from enfractal_companion import profile

VENDOR_WORDS = re.compile(r"anthropic|claude|openai|chatgpt|\bgpt|gemini|google|mistral|llama|copilot|bedrock|vertex",
                          re.IGNORECASE)


def printed(*argv: str) -> dict:
    out = io.StringIO()
    with redirect_stdout(out):
        status = profile.main(list(argv))
    assert status == 0
    return json.loads(out.getvalue())


class Profile(unittest.TestCase):
    def test_names_exactly_one_stdio_server_that_runs_this_package(self):
        for argv, extra in (((), []), (("--mock",), ["--mock"]),
                            (("--session-file", "game/session.json"), ["--session-file", str(Path("game/session.json").resolve())]),
                            (("--mock", "--schema-profile", "minimal"), ["--mock", "--schema-profile", "minimal"])):
            with self.subTest(argv=argv):
                document = printed(*argv)
                self.assertEqual(list(document), ["mcpServers"])
                self.assertEqual(list(document["mcpServers"]), ["enfractal-companion"])
                server = document["mcpServers"]["enfractal-companion"]
                self.assertEqual(set(server), {"type", "command", "args"})  # no env: the server needs no keys
                self.assertEqual(server["type"], "stdio")
                self.assertTrue(Path(server["command"]).is_absolute())
                self.assertEqual(server["args"], ["-m", "enfractal_companion", *extra])

    def test_the_command_is_the_companions_own_interpreter_not_what_it_links_to(self):
        self.assertEqual(profile.play_only_profile()["mcpServers"]["enfractal-companion"]["command"],
                         str(Path(sys.executable).absolute()))

    def test_offers_nothing_but_the_game_and_names_no_vendor(self):
        text = json.dumps(printed("--mock"))
        self.assertIsNone(VENDOR_WORDS.search(text))
        self.assertNotIn("--no-lockdown", text)
        for word in ("shell", "http", "url", "env", "token", "key", "secret"):
            self.assertNotIn(f'"{word}', text.lower())

    def test_refuses_two_game_sources_at_once(self):
        with self.assertRaises(ValueError):
            profile.play_only_profile(mock=True, session_file=Path("x.json"))


class LaunchedFromTheProfile(unittest.IsolatedAsyncioTestCase):
    async def test_a_client_launched_from_the_profile_sees_the_game_and_nothing_else(self):
        server = printed("--mock")["mcpServers"]["enfractal-companion"]
        parameters = StdioServerParameters(command=server["command"], args=server["args"])
        async with Client(parameters, mode="legacy") as client:
            capabilities = client.server_capabilities
            self.assertIsNone(capabilities.resources)
            self.assertIsNone(capabilities.prompts)
            tools = (await client.list_tools()).tools
            self.assertEqual({t.name for t in tools}, {s.name for s in contracts().tool_specs()})
            self.assertNotIn("protect_unlock", {t.name for t in tools})
            observed = (await client.call_tool("observe", {})).structured_content
            self.assertTrue(observed["ok"])
            self.assertEqual(contract_problems(observed), [])


if __name__ == "__main__":
    unittest.main()
