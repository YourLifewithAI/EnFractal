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
| `game/rooms/<captured_room_id>/**` (export output; never `test_room`) | C6 |
| `captures/` (local photos and intermediate data; **ignored by Git, never committed**) | C1 to C5 |

## Track P: Play

| Path | Packets |
|---|---|
| `game/scripts/creation_*.gd`, `game/scripts/invention_*.gd`, `game/scripts/world_state.gd`, `game/scripts/world_physics_profile.gd`, `game/scripts/player_controller.gd` (legacy fixture, to be removed), `game/creation_templates/**` | P1 |
| `game/scripts/native/Kernel/**` (C# side of the kernel and the command host) | P1 |
| `game/native/WorldScaleProfile.cs`, `game/scripts/native/SmallPlayerController.cs`, `game/scripts/native/CompanionAvatar.cs` (body and movement only) | P2 |
| `game/scripts/native/Room/**` (room data and builder) | P6, from Run 2 |
| `game/scripts/native/Sandbox/**` | P3, from Run 2 |
| `game/scripts/native/Saves/**` | P4, from Run 3 |
| `game/scripts/native/RoomHud.cs`, `game/scripts/native/Ui/**` | P5, from Run 3 |
| `game/tests/*.gd` (kernel suites), `game/tests/native/SmallAvatarPhysicsTest.cs`, `game/tests/native_small_avatar.tscn`, `game/scenes/player_test.tscn` | P1, P2 |
| `docs/engine/phase3/**` | P1 kernel records |

## Track A: AI companion

| Path | Packets |
|---|---|
| `companion/**` (model-neutral MCP server for the game command surface, mock host, boundary tests) | A1 |
| `game/scripts/native/Companion/**` (in-game bridge endpoint and goal runner; drives CompanionAvatar through its public API) | A1, A2 |
| `docs/companion/**` | A notes |

## Rules for crossing a boundary

- **Need a change in a file you do not own?** Write the exact diff and the reason in your lane report. The integrator applies it or hands it to the owning lane. Do not edit it yourself, even for a one-line fix.
- **Need a contract change?** Same: propose it. Contracts change in one integrator commit with their examples, consumers and tests.
- **New tests** go in your owned test paths. Ask the integrator to add them to `run-engine-tests.ps1`, `tools/test-room.ps1` or `tools/linux/test-all.sh`.
- **New directories** under an owned path are yours. A new top-level directory needs the integrator.
- **Generated files** (rooms, examples, `.uid` sidecars Godot creates for new scripts) are committed by whoever owns the generator or the new script.
- **Run-specific grants** in a run brief (for example, the Look lane changing the renderer line in `project.godot`) apply only to that run and only to the lines named.
