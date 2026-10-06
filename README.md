# EnFractal

A sandbox where you and your AI play together inside a real room that you photographed, shrunk to the size of a thumb.

**Direction as of 6 October 2026.** The player photographs a room or house. An AI-driven pipeline guides the capture, reconstructs the space, turns every object into its own game asset, and applies a chosen whimsical style. The player then enters that room as a ~10 cm avatar beside a separate avatar for their own AI. The AI is the player's magic: a spoken or typed wish ("there's a dragon haunting the garage", "build a cozy village along the shelves", "make this a spaceport") becomes a real change in the world through one shared baseline ruleset. Single-player first; multiplayer last.

Read the [room-scale direction](docs/ROOM-SCALE-DIRECTION.md) for the concept, the kernel rules and the R0–R5 phases. Read the [capture-to-Godot pipeline design](docs/pipeline/ROOM-CAPTURE-PIPELINE.md) for the MCP server and skill that take photos to a playable room, including the method review of the founder's reconstruction specification.

## Status

The repository is mid-transition. It previously built a geography-based world (Barton Creek, then the Pfluger district in Austin) with a 0.30 m player. That work is being removed or generalized per the [cleanup plan](docs/CLEANUP-PLAN.md), which lists every file group, its disposition and the founder questions that gate deletion. Until the plan is executed, the old launchers and tests still refer to map packages.

What carries forward from the earlier work:

- **Native Godot .NET 4.7.2 with C#** and a pinned, checksum-verified toolchain. See [native build](docs/NATIVE-BUILD.md) and [ADR 0001](docs/engine/decisions/0001-native-godot-csharp.md).
- **Small-body controllers**: a metric player controller with stepping, recovery and camera, and a separate deterministic companion body with follow/stay/come/look/point/stop. Both are scale parameters away from 10 cm.
- **The creation kernel**: data-only creation manifests, one compiler, a host-owned authority with budgets, receipts and protection, a world-state store, and an in-game invention editor. This becomes the baseline ruleset every wish composes from.
- **Painterly shaders and original art sources** for the style pass.
- **Research** on AI/MCP security, physics and creation runtime, structured world and style, and platform choice.

## Working rules

- Metric units everywhere; 1 world unit = 1 metre. Captured rooms and generated assets are real size.
- The companion and manual controls use the same validated operations. No second physics, no bypass.
- Claims need evidence: a checkpoint records what ran, on what machine, and what remains open.
- Photo sets for test rooms live in the founder's Google Drive folder **Enfractal / Photos for space generation / <room>**. The first test case is the garage; the second is a friend's back yard.

## Historical documents

[Earlier world vision](docs/WORLD-VISION.md), [earlier roadmap](docs/roadmap/ROADMAP.md) and [backlog](docs/roadmap/BACKLOG.md) carry a superseded banner. Checkpoints under `docs/engine/checkpoints/` record what the geography-era builds did and are candidates for removal in the cleanup plan.
