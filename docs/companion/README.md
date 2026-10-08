# Companion command surface (A1)

Players bring their own AI. Any MCP client or agent harness reaches the game through one model-neutral
MCP server, whose tools map one to one onto the command contract in
[contracts/game-command.schema.json](../../contracts/game-command.schema.json). The server talks to the
running game over a loopback link with a per-session token ([TRANSPORT.md](TRANSPORT.md)). The security
boundary and the tests that prove it are in [SECURITY.md](SECURITY.md); what the companion can see and
remember is in [PERCEPTION.md](PERCEPTION.md).

```
any MCP client or harness --stdio--> enfractal-companion --loopback link--> game host
   tools/list, tools/call            adapter: contract messages            Run 1: mock host (Python)
                                     locked-down process                   Run 2: kernel CommandHost (C#)
```

## Layout

| Path | What it is |
|---|---|
| `companion/pyproject.toml`, `companion/uv.lock` | Pinned environment (Python 3.11 or newer; the founder's machine uses 3.14.2) |
| `companion/requirements.lock.txt` | The same pins with hashes, exported for runners without uv (`uv export --frozen --no-dev --no-emit-project --format requirements-txt -o requirements.lock.txt`) |
| `companion/src/enfractal_companion/contract.py` | Loads `contracts/` and `contracts/validate.py` (with ECMA-262 patterns); derives the tool catalogue |
| `.../server.py` | The MCP server (official Python SDK `mcp==2.2.0`, low-level API) and the adapter |
| `.../profile.py` | Prints the play-only client profile |
| `.../link.py` | The loopback link: session file, mutual HMAC handshake, canonical frames |
| `.../canonical.py` | Canonical JSON v1, byte for byte as Lane P defined it: fingerprints, size limits, frames |
| `.../mock_host.py`, `.../mock_game.py` | The mock game host (with perception memory and goal jobs), and a console that runs it as a stand-in game |
| `.../perception.py` | Line of sight and the 10 cm body for the mock host |
| `.../lockdown.py` | Environment scrub and audit-hook sandbox for the server process |
| `.../refusals.py`, `.../textsafety.py` | One mapping from contract violations to error codes; untrusted-text rules, emoji markers in place |
| `companion/schemas/companion-link.schema.json` | The link's session file and frames, proposed for `contracts/` |
| `companion/tests/` | Boundary tests (host, link, MCP surface, perception, perception memory, text rules, no holds, sandbox, profile), the stdio acceptance test, and `test_kernel_alignment.py`, which reads the kernel host's constants and fails when the mock's policy drifts from them |
| `companion/tests/fixtures/contract_memory_v1.json` | The perception-memory result fields as proposed; `contracts/` has carried them since `72e9015`, and a test checks the two still match |
| `docs/companion/proposals/` | Changes for files this lane does not own, as patches, and the kernel host's change requests (below) |

## Tools

One tool per op in `$defs/command_op` and `$defs/query_op`, except `$defs/player_only_ops`
(`protect.unlock`), which has no tool and which the game refuses from a companion anyway. The list is
derived from the contract at startup, so a new player-only op disappears from the surface without a
code change.

| Tool | Op | Kind |
|---|---|---|
| `room_describe`, `entities_list`, `entity_inspect`, `capabilities_list`, `observe`, `jobs_status`, `receipt_lookup`, `approval_status` | the query of the same name | read-only |
| `entity_grab`, `entity_release`, `entity_place`, `entity_set_part`, `entity_remove`, `entity_transform`, `creation_place`, `creation_revise`, `creation_activate`, `protect_lock`, `goal_set`, `goal_stop`, `effect_start`, `effect_stop`, `style_set`, `room_checkpoint`, `room_undo` | the command of the same name | command |

Tool names use underscores because several vendors' function-calling APIs reject dots. A tool's
arguments are the op's `args` from the contract, flattened, plus for commands the envelope fields the
sender owns: `action_id` (required, except for the two stops, where the adapter mints one),
`expected_revision`, `expected_entities`, `preview`, `note`. The adapter fills `schema`, `version`,
`room_id`, `op` and `query_id`, and fills `actor` with the companion's own avatar where the contract
requires one. Nothing else is accepted: no principal, no approval, no room. Numbers are sent as canonical
JSON v1 writes them (`2.0` is sent as `2`).

When the adapter refuses a call before sending it, it answers with a contract-valid `enfractal.result` of
its own (same error codes as the host). Its `field_path` names the tool argument for unknown or
forbidden fields (`$.principal`) and the contract message otherwise (`$.args.target`), as the host does.

Each result is the game's `enfractal.result`, returned twice in the same call: as `structuredContent`,
and as a text block holding a fixed preamble line and one line of ASCII-escaped JSON. `isError` is true
when `ok` is false. Published input schemas inline every `$ref` and drop `if`/`then`/`not`, which older
clients reject; `--schema-profile minimal` keeps only the most widely supported JSON Schema keywords. The
adapter and the host still validate every message against the full contract.

## Run the tests

```powershell
uv sync --project companion --frozen                       # once: creates companion/.venv
uv run --project companion --locked python -m unittest discover -s companion/tests
```

Expected: `Ran 492 tests ... OK (skipped=1)` in about 50 seconds. The skipped test is the stand-in for a
`contracts/` without the memory fields; today's contract carries them, so its counterpart runs instead.
The canonical JSON test uses the kernel's golden fixture in `game/tests/fixtures/kernel/` when the
checkout has it (or the folder `ENFRACTAL_KERNEL_FIXTURE_DIR` names), and this lane's byte-identical copy
otherwise. `ENFRACTAL_CONTRACTS_DIR` points the whole suite, the stdio server included, at another copy of
`contracts/`, to check a proposed change.

## Connect an MCP client or agent harness

The server is a standard MCP stdio server with no vendor extensions. Any client that can launch a stdio
server with a command line can use it. Both the initialize-handshake protocol era (2025-11-25) and
2026-07-28 are served. Registering a server is persistent client configuration, so the founder approves
it on their own machine.

1. Create the environment once, in the checkout the game runs from:

   ```powershell
   uv sync --project companion --frozen
   ```

2. Print the **play-only profile**: the one server a play session needs, in the common `mcpServers`
   JSON shape.

   ```powershell
   companion\.venv\Scripts\python.exe -m enfractal_companion.profile           # the running game
   companion\.venv\Scripts\python.exe -m enfractal_companion.profile --mock    # the built-in mock game
   ```

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

   Give it to the client as its whole MCP configuration for play, or translate it into the client's own
   format: one stdio server, that command, those arguments. **Play with this server only**, in a client
   session that has no shell, file, browser or other MCP tools, so that nothing a sign in the room says
   can reach anything but the game's checked commands. The server needs no keys or tokens; do not pass
   any to it (it clears its environment at startup either way). There is no token to hand over: the
   server reads the game's session file itself.

   **On Windows, give the server `SYSTEMROOT`.** Some clients start MCP servers with an almost empty environment (Codex does). Without `SYSTEMROOT`, Python's asyncio cannot load Winsock, and the server exits at once (`WinError 10106`). Add `SYSTEMROOT = C:\Windows` to the server's environment in the client's configuration; in Codex, `env = { SYSTEMROOT = 'C:\Windows' }`. Found on 7 October 2026 with Codex CLI 0.162.

3. Check that the client lists 25 tools and no resources or prompts, then ask, for example: "Observe what
   the companion can see, then set a follow goal for it." Expect an `observe` result listing the
   test-room props and a `goal_set` result with `"ok": true` and `"transient": true`.

4. To see held commands as well, run the stand-in game in a second terminal and use the profile without
   `--mock`, which reads the game's session file:

   ```powershell
   companion\.venv\Scripts\python.exe -m enfractal_companion.mock_game
   ```

   With the recommended policy, ask the companion to remove the book. The console prints
   `[held] <request id> entity.remove ...`; type `approve <first characters of the id>` there, as the
   player, then ask the companion to check `approval_status`. Typing `say obj:box <text>` puts a sign in
   the world to try prompt injection. To try perception memory: ask the companion to observe, type
   `walk 1.6 0 0.2` (behind the box), ask where the book is (it answers from memory), type
   `move obj:book -1.7 0 -1.2`, ask again (now `may_be_stale`), ask it to fetch the book, then type
   `walk 0.45 0 0.2` and `arrive`: the fetch fails honestly, and `jobs_status` says why.

Without `--mock` or `--session-file`, the server reads the real game's session file from
`%APPDATA%\Godot\app_userdata\EnFractal\companion\session.json` (Godot's `user://`). Until Run 2 wires
the kernel host, nothing writes it and every tool answers `not_ready`.

## Proposals for files this lane does not own

The four Run 1 patches were applied on `run1/integration` (`72e9015` and earlier); they stay as the record.
They are applied from the stored blob so line-ending conversion cannot touch them
(`git show run1/companion:<path> | git apply`).

**Before the Run 2 swap:** [proposals/kernel-host-gaps.md](proposals/kernel-host-gaps.md) records the
mock alignment and the eight host requests. All eight are closed on the real host as of 7 October 2026
(see [the Run 1 status](../runs/RUN-1-STATUS.md#still-open)). The transport swap remains A2 work in Run 2.

| Patch | What it does | Checked by |
|---|---|---|
| `proposals/runners-run1.diff` | Adds this suite to `run-engine-tests.ps1` and `tools/linux/test-all.sh` (with its environment from `tools/linux/setup-toolchain.sh`), and pins line endings for the link schema, the fixture copy and these patches in `.gitattributes`. Unchanged this round | Applies on `0ea09ff` with `git apply --check`, alone and after the three patches below; the bash scripts pass `bash -n` and the PowerShell runner parses. Not run: the runners belong to the integrator |
| `proposals/contracts-run1.diff` | Promotes the link schema; adds `capabilities.list` and `jobs.status` payloads and a display-text checkpoint label; adds the perception-memory fields to `entity_summary` (`seen`, `last_seen_ago_s`, `last_seen_revision`, `may_be_stale`, the last three only on remembered summaries) and `remembered` to `observe`; examples for memory, a goal job and `jobs.status`, and two invalid memory examples. Rebuilt on `0ea09ff` | Applies there with `git apply --check`; 34 contract tests alone, 40 with the text-rules patch (either order), the validator on the test room, its style pins, the garage and all 34 valid messages; this suite against the patched copy (469 tests) |
| `proposals/contracts-text-rules.diff` | Review finding 5, the trailing-newline item and the emoji answer: the invisible characters and plane 14 refused in `display_text`, `long_text` and scalar strings, VS15, VS16 and the joiner let through by the patterns; ECMA-262 patterns, plane 14, unpaired surrogates, misplaced emoji markers and unreadable numbers in `contracts/validate.py`; six contract tests with the shared emoji vectors. Rebuilt on `0ea09ff` | Applies there; 40 contract tests with both contract patches, either order; this suite against the patched copy |
| `proposals/roomdata-text-rules.diff` | The same text rule in the C# room loader (`IsSafeText`, `MisplacedEmojiMarker` with the Extended_Pictographic table), `\z` for its anchored patterns (.NET's `$` also matches before a final newline), and the shared emoji vectors plus a room-name load test in `RoomDataTest.cs`. Rebuilt on `0ea09ff` | Applies there. **Not built or run**: no C# toolchain in this lane's worktree |

## Decisions taken in this lane

- **Python, official SDK, low-level server.** The tool list is generated from the contract, so the SDK's
  decorator API would only get in the way. `mcp==2.2.0` (released 7 September 2026) was chosen over the
  then four-day-old 2.3.0.
- **The adapter validates, the host decides.** The adapter refuses what it can without asking the game,
  so a confused model fails fast, but every rule is enforced again by the host, because anything holding
  the token can bypass the adapter.
- **No protection depends on holding.** Holds are a policy table that may be empty (the founder's
  direction), and every protection is tested with it empty.
- **One error mapping.** Adapter and mock host share `refusals.py`, so the same attack gets the same
  contract error code at either layer, with template messages that never echo requester text.
- **Results are data.** World text only ever appears inside JSON string values, stripped of invisible
  characters; the text block is ASCII-escaped and one line long, so a sign cannot start a new line,
  hide characters or close a tag.
- **A companion undoes only its own changes**, and never anything protected; undoing the player's
  changes is the player's. The founder confirmed this on 6 October 2026.
- **The companion remembers what its own avatar saw** (founder, 6 October 2026): per session and room,
  bounded, never saved, with a one-bit `may_be_stale`; only goals that move or turn it may aim at a
  remembered thing, re-checked on arrival ([PERCEPTION.md](PERCEPTION.md)). (Replaced on 7 October by
  [shared team knowledge and selective game-relevant memory](JOURNAL.md). The per-session perception
  cache remains interim code; the shared map and journal are not implemented yet.)
- **Standard emoji are allowed in world text** (founder, 6 October 2026): VS15, VS16, the joiner and the
  keycap combiner only where an emoji puts them; every other invisible character stays blocked.
- **The companion has the player's 10 cm body** (founder, 6 October 2026): eye 0.087 m, reach 0.15 m.
- **Canonical frames, no lockout** on the link (TRANSPORT.md).
- **The mock follows the kernel host** wherever the kernel fixes a number or a rule that the contract
  allows (6 October 2026, night): its ledger, rate limit and approval limit, and the fingerprint over the
  command exactly as received, so `"preview": false` on the wire is other content. The MCP tools treat
  `preview: false` as their default and never send it, so a model that spells the default out still
  replays rather than conflicts. The adapter keeps its tighter per-kind rate buckets in front of the
  host's limit.
- **No in-game bridge yet.** `game/scripts/native/Companion/` is untouched. In Run 2 the bridge is a
  `LinkServer` equivalent in C# in front of P1's `CommandHost`, implementing [TRANSPORT.md](TRANSPORT.md)
  and passing `companion/tests/test_link.py`'s cases.
