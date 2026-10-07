# Who owns which files

Every run is built by parallel agent lanes. A lane **edits only the paths it owns** and may read anything. Shared files belong to the integrator, who merges lanes, keeps contracts and consumers in step, and runs the full suite. This map is what keeps four lanes from editing the same file at once. When a path is not listed, it belongs to the integrator.

## Integrator (shared files)

| Path | Why it is shared |
|---|---|
| `contracts/**` | The agreements between tracks; see [contracts/README.md](../../contracts/README.md) |
| `AGENTS.md`, `CLAUDE.md`, `README.md`, `docs/ROOM-SCALE-DIRECTION.md`, `docs/runs/**`, `docs/CLEANUP-PLAN.md` | Plans and agent rules |
| `game/project.godot`, `game/export_presets.cfg`, `game/EnFractal.csproj`, `game/EnFractal.sln`, `game/global.json` | Project-wide settings |
| `.gitattributes`, `.gitignore` | Line-ending pins and ignore rules |
| `run-engine-tests.ps1`, `run-room.ps1`, `tools/*.ps1`, `tools/native-toolchain.lock.json`, `tools/linux/**` | Toolchain and the test lists every lane runs |
| `game/scripts/native/RoomWorld.cs`, `game/native/**`, `game/scenes/room.tscn`, `game/scenes/native_*.tscn` | Composition root, boot and release probe |
| `tools/rooms/**`, `game/rooms/test_room/**` | The placeholder room (regenerate with its builder; lanes request changes) |
| `game/tests/native/RoomDataTest.cs`, `game/tests/native_room_data.tscn` | Contract-consumption fixture |
| `docs/engine/persistence-notes.md`, `docs/engine/quality-gates.md`, `docs/engine/decisions/**` | Cross-track rules |
| `docs/history/**`, `docs/research/**` | Read-only context; nobody edits history |

## Track L: Look

| Path | Packets |
|---|---|
| `docs/look/**` (look bible, review captures, scores) | L1, L7 |
| `game/styles/**` (presets) | L1, L3, L5, L6 |
| `game/shaders/**` | L3 |
| `game/scripts/native/Look/**` (look director, material library, preset reader) | L2, L3, L5 |
| `game/scripts/painterly_assets.gd` | L3; keep its public function names, the creation visuals call them |
| `game/assets/art/painterly/**`, `assets/art_sources/painterly/**`, `tools/art/**` | L4, reference art |
| `tools/look/**` (review cameras, capture harness, mesh treatment) | L4, L7 |
| `game/tests/native/Look/**`, `game/tests/native_look_*.tscn` | L tests |

## Track C: Capture

| Path | Packets |
|---|---|
| `pipeline/roomscan/**` (Python package, MCP server, SKILL.md, tests) | C1 to C7 |
| `docs/pipeline/**` | C design notes and compute costs |
| `captures/` (local photos and intermediate data; **ignored by Git, never committed**) | C1 to C5 |
| Captured room exports go to the player's user data, `user://rooms/<room_id>/` (on Windows under `%APPDATA%\Godot\app_userdata\EnFractal\rooms\`), never into the repository; Git ignores `game/rooms/*` except `test_room` | C6 |

## Track P: Play

| Path | Packets |
|---|---|
| `game/scripts/creation_*.gd`, `game/scripts/invention_*.gd`, `game/scripts/world_state.gd`, `game/scripts/world_physics_profile.gd`, `game/scripts/player_controller.gd` (legacy fixture, to be removed), `game/creation_templates/**` | P1 |
| `game/scripts/native/Kernel/**` (C# side of the kernel and the command host; the integrator wires its `CommandHost.Attach(RoomWorld)` entry point into `RoomWorld.cs` at merge) | P1 |
| `game/tests/native/Kernel/**`, `game/tests/native_kernel_*.tscn`, `game/tests/fixtures/kernel/**`, `tools/kernel/**` (command-host tests, golden canonical-JSON fixtures and their Python reproducer) | P1 |
| `game/native/WorldScaleProfile.cs`, `game/scripts/native/SmallPlayerController.cs`, `game/scripts/native/CompanionAvatar.cs` (body and movement only), `game/scripts/native/Navigation/**`, `game/tests/native/RoomNavigationTest.cs`, `game/tests/native_room_navigation.tscn` | P2 |
| `game/scripts/native/Room/**` (room data and builder) | P6, from Run 2 |
| `game/scripts/native/Sandbox/**` | P3, from Run 2 |
| `game/scripts/native/Saves/**` | P4, from Run 3 |
| `game/scripts/native/RoomHud.cs`, `game/scripts/native/Ui/**` | P5, from Run 3 |
| `game/tests/*.gd` (kernel suites), `game/tests/native/SmallAvatarPhysicsTest.cs`, `game/tests/native_small_avatar.tscn`, `game/scenes/player_test.tscn` | P1, P2 |
| `docs/engine/phase3/**` | P1 kernel records |

## Codex (GPT), a contractor

| Path | Notes |
|---|---|
| `codex/<brief>` branches only | Never `main`, `run1/*` or a lane branch |
| The files in its brief's `scope` block, normally `docs/codex/reports/<brief>.md` | `tools/codex/check_scope.py` checks every branch before the integrator merges it |

The integrator owns `docs/codex/README.md`, `docs/codex/briefs/**` and `tools/codex/**`. Codex works on research, test runs and audits, never on contracts, the kernel, the companion's server or the Look lane's GPU work. See [docs/codex/README.md](../codex/README.md).

## Track A: AI companion

| Path | Packets |
|---|---|
| `companion/**` (model-neutral MCP server for the game command surface, mock host, boundary tests) | A1 |
| `game/scripts/native/Companion/**` (in-game bridge endpoint and goal runner; drives CompanionAvatar through its public API) | A1, A2 |
| `docs/companion/**` | A notes |

## Rules for crossing a boundary

- **Need a change in a file you do not own?** Write the exact diff and the reason in your lane report. The integrator applies it or hands it to the owning lane. Do not edit it yourself, even for a one-line fix.
- **Need a contract change?** Same: propose it. Contracts change in one integrator commit with their examples, consumers and tests.
- **Dependencies** pinned in a lockfile inside your owned directory (for example `pipeline/roomscan/pyproject.toml` with `uv.lock`, or `companion/pyproject.toml`) are pre-approved. Global installs, new services and paid APIs are not.
- **New tests** go in your owned test paths. Ask the integrator to add them to `run-engine-tests.ps1`, `tools/test-room.ps1` or `tools/linux/test-all.sh`.
- **New directories** under an owned path are yours. A new top-level directory needs the integrator.
- **Generated files** (rooms, examples, `.uid` sidecars Godot creates for new scripts) are committed by whoever owns the generator or the new script.
- **Run-specific grants** in a run brief (for example, the Look lane changing the renderer line in `project.godot`) apply only to that run and only to the lines named.
