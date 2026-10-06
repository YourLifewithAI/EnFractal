# Companion command surface (A1)

The player's AI reaches the game through one model-neutral MCP server. Its tools map one to one onto the
command contract in [contracts/game-command.schema.json](../../contracts/game-command.schema.json). The
server talks to the running game over a loopback link with a per-session token ([TRANSPORT.md](TRANSPORT.md)).
The security boundary and the tests that prove it are in [SECURITY.md](SECURITY.md).

```
MCP client (any vendor) --stdio--> enfractal-companion --loopback link--> game host
   tools/list, tools/call           adapter: contract messages            Run 1: mock host (Python)
                                    sandboxed process                     Run 2: kernel CommandHost (C#)
```

## Layout

| Path | What it is |
|---|---|
| `companion/pyproject.toml`, `companion/uv.lock` | Pinned environment (Python 3.11 or newer; the founder's machine uses 3.14.2) |
| `companion/requirements.lock.txt` | The same pins with hashes, exported for runners without uv (`uv export --frozen --no-dev --no-emit-project --format requirements-txt -o requirements.lock.txt`) |
| `companion/src/enfractal_companion/contract.py` | Loads `contracts/` and `contracts/validate.py`; derives the tool catalogue |
| `.../server.py` | The MCP server (official Python SDK `mcp==2.2.0`, low-level API) and the adapter |
| `.../link.py` | The loopback link: session file, mutual HMAC handshake, frames |
| `.../mock_host.py`, `.../mock_game.py` | The mock game host, and a console that runs it as a stand-in game |
| `.../lockdown.py` | Environment scrub and audit-hook sandbox for the server process |
| `.../refusals.py`, `.../textsafety.py` | One mapping from contract violations to error codes; untrusted-text rules |
| `companion/schemas/companion-link.schema.json` | The link's session file and frames, proposed for `contracts/` |
| `companion/tests/` | Boundary tests (host, link, MCP surface, sandbox) and the stdio acceptance test |

## Tools

One tool per op in `$defs/command_op` and `$defs/query_op`, except `$defs/player_only_ops`
(`protect.unlock`), which has no tool. The list is derived from the contract at startup, so a new
player-only op disappears from the surface without a code change.

| Tool | Op | Kind |
|---|---|---|
| `room_describe`, `entities_list`, `entity_inspect`, `capabilities_list`, `observe`, `jobs_status`, `receipt_lookup`, `approval_status` | the query of the same name | read-only |
| `entity_grab`, `entity_release`, `entity_place`, `entity_set_part`, `entity_remove`, `entity_transform`, `creation_place`, `creation_revise`, `creation_activate`, `protect_lock`, `goal_set`, `goal_stop`, `effect_start`, `effect_stop`, `style_set`, `room_checkpoint`, `room_undo` | the command of the same name | command |

Tool names use underscores because several vendors' function-calling APIs reject dots. A tool's
arguments are the op's `args` from the contract, flattened, plus for commands the envelope fields the
sender owns: `action_id` (required), `expected_revision`, `expected_entities`, `preview`, `note`. The
adapter fills `schema`, `version`, `room_id`, `op` and `query_id`, and fills `actor` with the companion's
own avatar where the contract requires one. Nothing else is accepted: no principal, no approval, no room.

When the adapter refuses a call before sending it, it answers with a contract-valid `enfractal.result` of
its own (same error codes as the host). Its `field_path` names the tool argument for unknown or
forbidden fields (`$.principal`) and the contract message otherwise (`$.args.target`), as the host does.

Each result is the game's `enfractal.result`, returned twice in the same call: as `structuredContent`,
and as a text block holding a fixed preamble line and one line of ASCII-escaped JSON. `isError` is true
when `ok` is false. Published input schemas inline every `$ref` and drop `if`/`then`/`not`, which older
clients reject; the adapter and the host still validate every message against the full contract.

## Run the tests

```powershell
uv sync --project companion --frozen                       # once: creates companion/.venv
uv run --project companion --locked python -m unittest discover -s companion/tests
```

Expected: `Ran 141 tests ... OK` in about 20 seconds. The contract tests still pass unchanged
(`python -m unittest discover -s contracts/tests` with the contract validator's environment).

## Register it with an MCP client

Registering a server is persistent client configuration, so the founder approves it. These commands
are for Claude Code; any MCP client that launches a stdio server works the same way with the same
command line. Use the integrator's checkout path.

1. Create the environment once:

   ```powershell
   uv sync --project C:\dev\EnFractal\companion --frozen
   ```

2. Register the server against the built-in mock game (no Godot needed). Local scope keeps it in the
   founder's own Claude Code settings for this project only:

   ```powershell
   claude mcp add --scope local enfractal-companion -- C:\dev\EnFractal\companion\.venv\Scripts\python.exe -m enfractal_companion --mock
   ```

   The equivalent project-scope `.mcp.json` entry (a repository file, so the integrator's call):

   ```json
   {
     "mcpServers": {
       "enfractal-companion": {
         "type": "stdio",
         "command": "C:\\dev\\EnFractal\\companion\\.venv\\Scripts\\python.exe",
         "args": ["-m", "enfractal_companion", "--mock"]
       }
     }
   }
   ```

3. Start `claude` in `C:\dev\EnFractal`, run `/mcp` and check that `enfractal-companion` is connected
   with 25 tools. Then ask, for example: "Using the enfractal-companion tools, observe what the companion
   can see, then set a follow goal for it." Expect an `observe` result listing the test-room props and a
   `goal_set` result with `"ok": true` and `"transient": true`.

4. To see approvals as well, run the stand-in game in a second terminal and point a second registration
   at its session file instead of `--mock`:

   ```powershell
   C:\dev\EnFractal\companion\.venv\Scripts\python.exe -m enfractal_companion.mock_game
   claude mcp add --scope local enfractal-companion-game -- C:\dev\EnFractal\companion\.venv\Scripts\python.exe -m enfractal_companion --session-file C:\dev\EnFractal\companion\.cache\session.json
   ```

   Ask the companion to remove the book. The console prints `[held] <request id> entity.remove ...`;
   type `approve <first characters of the id>` there, as the player, then ask the companion to check
   `approval_status`. Typing `say obj:box <text>` puts a sign in the world to try prompt injection.

5. Remove the registrations afterwards with `claude mcp remove enfractal-companion --scope local`
   (and `enfractal-companion-game`).

Without `--mock` or `--session-file`, the server reads the real game's session file from
`%APPDATA%\Godot\app_userdata\EnFractal\companion\session.json` (Godot's `user://`). Until Run 2 wires the
kernel host, nothing writes it and every tool answers `not_ready`.

## Decisions taken in this lane

- **Python, official SDK, low-level server.** The tool list is generated from the contract, so the SDK's
  decorator API would only get in the way. `mcp==2.2.0` (released 7 September 2026) was chosen over the
  four-day-old 2.3.0. It serves both the initialize-handshake era (2025-11-25) and 2026-07-28; the
  acceptance test drives both.
- **The adapter validates, the host decides.** The adapter refuses what it can without asking the game
  (unknown fields, identity keys, foreign actors, size, rate) so a confused model fails fast, but every
  rule is enforced again by the host, because anything holding the token can bypass the adapter.
- **One error mapping.** Adapter and mock host share `refusals.py`, so the same attack gets the same
  contract error code at either layer, with template messages that never echo requester text.
- **Results are data.** World text only ever appears inside JSON string values; the text block is
  ASCII-escaped and one line long, so a sign cannot start a new line, hide characters or close a tag.
- **No in-game bridge yet.** `game/scripts/native/Companion/` is untouched. In Run 2 the bridge is a
  `LinkServer` equivalent in C# in front of P1's `CommandHost`, implementing [TRANSPORT.md](TRANSPORT.md)
  and passing `companion/tests/test_link.py`'s cases.
