# Grounded movement prototype

This is an early Godot physics slice for the Barton Creek test map. Press **Tab** to switch between the existing free-fly inspection camera and a temporary first-person walking view. In walking mode, **WASD** moves, **Shift** runs, **Space** jumps and slows descent while held in the air, and **R** returns to the last grounded position. Keys **1–3** return to fly mode at the existing viewpoints. The walking camera is a test fixture; the intended player-facing creature camera is third-person.

The player uses a capsule `CharacterBody3D`. Walk, run, jump, gravity, glide descent, slope angle, and map bounds have explicit limits in `game/scripts/player_controller.gd`. Ground comes from `MapRuntime`'s validated height grid. `game/scripts/terrain_collision_streamer.gd` makes 64 m collision patches at the source grid's 2 m spacing, with at most a 3 × 3 ring active around the player. Adjacent patches are resident before the player crosses a patch boundary. Collision resolution stays fixed as visible terrain LOD changes. Spawn samples the exact collision triangle and searches nearby for walkable ground when the selected point is too steep.

The collision source is pinned to the map manifest's `heights_sha256`, with collision revision **0**. This version supports immutable terrain only; there is no live terrain edit/revision handoff yet. Mapped buildings, roads, water, vegetation, and illustrative mall details remain visual geometry without physical collision. The collider ring is built synchronously; shape staging or caching may still be needed before low-hardware acceptance. This is local-client movement only, with no multiplayer authority or prediction.

From the repository root, run the Godot 4.7.2 engine checks, including the deterministic movement smoke route:

```powershell
.\run-engine-tests.ps1
```

The script waits for the Windows Godot process to finish and checks its output; directly invoking the GUI executable from this PowerShell session can return before the test completes. The movement route checks source hash/revision, terrain contact on both sides of two seams, collision ring bounds and crossing, grounded spawn, walking speed, jumping, glide fall speed, recovery, and the return to free-fly. The [walking-height workshop capture](../images/barton-workshop-engine.png) verifies the scene renders on the development laptop. These checks are not a low-end device benchmark.
