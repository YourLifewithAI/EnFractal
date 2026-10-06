# EnFractal direction: one room, two avatars, AI as magic

**Direction change, 6 October 2026.** EnFractal no longer starts from real geography at district or globe scale. It starts from **one real room or house that the player photographs**, reconstructed into individually editable game objects, restyled into something whimsical, and inhabited by a **player avatar about 10 cm tall** and a **separate avatar for the player's own AI**. The AI is the player's magic. Everything else in this repository is measured against that.

The earlier vision (Barton Creek, the Pfluger district, a shared painterly Earth) is retained in Git history and in a few explicitly historical documents. It is no longer the product. See the [cleanup plan](CLEANUP-PLAN.md) for what is being removed and what carries forward.

## What stays true from the earlier vision

- **Two embodied identities.** The player has a personalized avatar. Their AI has a separate, customizable, named avatar with its own stable identity. Both are in the scene; neither is an editor cursor.
- **The AI is the magic.** The player speaks or types a wish; the companion interprets it and acts through the same validated world operations the player's manual controls use. The companion never gets a second physics or a bypass around the rules.
- **One baseline ruleset.** Data-only creation manifests, one compiler, one authority, budgets, receipts, and protection (save versus save-and-protect). The existing compiler/authority/world-state kernel is the seed of this; it is kept and generalized.
- **Game-only AI profile with a hard security boundary.** Scoped observations, typed commands, no shell/files/URLs/credentials, model-neutral transport (MCP or equivalent). Any capable AI client should be able to be the companion.
- **Native Godot .NET with C#.** [ADR 0001](engine/decisions/0001-native-godot-csharp.md) stands. New gameplay code is C#; surviving GDScript migrates incrementally.
- **Single-player first, multiplayer last.** Unchanged.
- **Honest evidence.** Checkpoints record what runs and what is still open. Screenshots do not certify collision; agent ratings do not certify fun.

## What changes

| Earlier | Now |
|---|---|
| Real geography from USGS/OSM, 4 km districts, a shared Earth | One room or house from the player's own photos |
| 0.30 m player in real-scale terrain | ~0.10 m player in a real-scale room (a 5 m garage is a 50-body-length world) |
| Terrain height grids, collision streaming, map packages, spatial pins | A room shell (floor, walls, ceiling, openings) plus a set of discrete object assets with stable IDs |
| Offline Python geodata builder | An **MCP server plus agent skill** that guides photo capture, reconstructs the space, produces per-object assets, applies a style, and exports a Godot room |
| Painterly Central Texas landscape as the art laboratory | A chosen whimsical style applied over reconstructed indoor objects; Tiny Glade warmth remains the reference for feel |
| PostgreSQL local save/travel service with sandbox worlds and portals | Simple local saves per room (file-based); travel between rooms of one house is in-process scene switching |
| Wish examples: hurricane, flood the river, castle on a bank | Wish examples: a dragon haunting the garage, a cozy village across the shelves, the room turned into a sci-fi spaceport |

## The experience, in order

1. **Capture.** The player photographs a room (phone is fine). Their AI reviews the set, says what is missing ("I have never seen the floor under the desk; take four more photos of the shelving from the left"), and asks for more until coverage is good enough.
2. **Reconstruct.** The pipeline recovers the room shell and an inventory of objects with positions, sizes and categories. Each object becomes its own complete, closed, collidable asset with metadata (category, material, mass estimate, movable or fixed, affordances such as openable or container). Fused scan meshes are reference material, not the game world.
3. **Stylize.** A style preset (painterly, storybook, toy, cel, and so on) is applied consistently across the shell and every object. The result should look handmade and inviting rather than like a photogrammetry scan.
4. **Inhabit.** The player and companion spawn on the floor at ~10 cm. The player walks, jumps, climbs onto low things, and manipulates objects within their reach; the companion follows, looks, points, fetches, and acts.
5. **Wish.** The player asks for something. The companion composes supported capabilities into a result: creatures with bodies and behaviors, structures from generators, transformations of existing objects, bounded effects. Previews gate destructive changes; locks protect what the player chose to keep.
6. **Persist.** Saves record the room's object state, creations, locks, companion identity and style pins. Reload reproduces them. Undo restores eligible prior revisions.

## Baseline rules the kernel must provide

These are the primitives every scenario (dragon fight, cozy village, spaceport) composes from. None of this is new to the project's thinking; the room scope makes it tractable.

- **Entities with stable IDs**: shell parts, scanned objects, avatars, creations, creatures, effects. Each has transform, bounds, material role, collision, protection state, provenance (scanned, generated, player-made, AI-made) and revision.
- **Operations**: inspect, propose, validate, preview, commit, revise, remove, act, stop. Manual controls and the AI use the same ones.
- **Capability registry**: typed, bounded primitives the compiler accepts (shapes, materials, joints, forces, lights, timers, triggers, movement goals, spawn, transform, restyle, and later health/damage/hit for combat).
- **Budgets**: per-wish and per-room limits on parts, active bodies, effect duration, generation time and inference cost. Splitting a wish cannot evade them.
- **Protection**: save versus save-and-protect; locks resist direct and indirect effects; unlock is a direct player action outside the AI's tools.
- **Receipts and idempotency**: every mutation has an action ID, fingerprint and durable receipt.
- **Scenario packs** (later): bundles of generators, creature families, behavior goals and style overrides that make "haunt this room with a dragon" or "turn this into a spaceport" a composition rather than a hard-coded mode. Combat needs health, damage and hit-detection primitives that do not exist yet; that is S3 work, not an assumption.

## Scale and physics decision

Keep **1 world unit = 1 metre** and keep all authored and captured dimensions in metres. The capture pipeline produces real-metre assets, the existing controllers and body-profile contract are metric, and nothing has to be rescaled. The player body profile becomes roughly 0.10 m tall, 0.02 m radius, 0.087 m eye height, 0.15 m reach; exact values are tuned in S1.

Two things are decisions, not assumptions:

- **Game feel at 10 cm.** Real gravity makes a 10 cm body fall and jump in a fraction of a second. The existing bounded physics-profile mechanism (`world_physics_profile.gd`) already allows a tuned gravity/jump profile; treat feel as a tunable, not a physics-accuracy claim.
- **Engine precision at small scale.** Godot physics (Jolt is built in since 4.4) is tuned around metre-scale bodies. If a 0.02 m radius capsule proves jittery, the fallback is to scale the room ×10 at import (1 unit = 10 cm) and keep the avatar at 1 unit. That is a one-line import setting if assets stay metric; it is not a reason to abandon metres now.

## Single-player choices that keep multiplayer possible

Multiplayer is a real future goal: players inviting other players and their companion AIs into a shared room. It is deferred until single-player is smooth. These four habits cost nothing now and avoid a rewrite later. None of them is networking.

- **One command path for every world change.** Typed command, action ID, authority check, receipt. No gameplay code moves or edits an entity outside that path. In multiplayer that path becomes the server's; everything else replicates its results.
- **The room is data; the scene is derived.** Shell, objects, creations, locks and avatars are fully described by serializable state, and the Godot scene is rebuilt from it. Sharing a room later is sending that state.
- **Every action names a principal.** Player, companion, and later guests and their companions are principals with grants. Keep "who did this" on every action even with one person in the room.
- **The companion connects through a restricted adapter.** Scoped observations and typed commands only. Each future guest brings their own AI through the same surface, which is what keeps a guest's companion from wrecking the host's room.

Deferred until the multiplayer phase: transport, replication, accounts, hosted services, consent and moderation, and any database service. The persistence semantics from the earlier save/travel work (idempotent receipts, fenced sessions, a checkpoint before any transfer, reconcile-before-retry) are kept as rules for the file-based saves, not as a server.

## Rendering decision to revisit

The earlier choice of the Compatibility renderer served an 8 GiB integrated-graphics baseline for a 4 km outdoor world. A single bounded room is the ideal case for **Forward+ with VoxelGI** (bounded interior global illumination) and depth of field for the tilt-shift "diorama" look described in the founder's art notes. Recommendation: switch the room scene to Forward+ and profile on the development machine; keep a Compatibility fallback only if a measured device requirement appears.

## Phases, restated for rooms

| Phase | Goal | Exit evidence |
|---|---|---|
| **R0 — cleanup and kernel** | Remove geography, keep and generalize the creation kernel, controllers and toolchain; new 10 cm body profile; room scene contract | Build and retained tests pass without any map package; a hand-authored test room loads with both avatars |
| **R1 — capture and reconstruct** | MCP server and skill: ingest, coverage assessment, guidance loop, room shell, object inventory | The garage photo set produces a shell, a reviewed object inventory and specific capture guidance; a second room (the friend's back yard) exercises the outdoor edge case |
| **R2 — assets and style** | Per-object complete assets with collision and metadata; style presets; Godot export; agent-driven visual review | The garage loads in Godot as discrete objects; the player walks it at 10 cm; one style preset applied consistently; founder review |
| **R3 — companion and first magic** | Game-only AI connection; follow/look/fetch/act; one transformation and one spawned creature through the kernel | Real AI client performs a loose wish in the garage; locks hold; stop works; manual fallback works |
| **R4 — scenario packs** | Dragon encounter, cozy building, spaceport restyle as compositions of capabilities | Each scenario runs from a loose wish; novel combinations work; budgets hold |
| **R5 — persistence, polish, playtests** | Saves, undo, second room, house navigation, accessibility, performance | Founder and fresh-player sessions; reproducible Windows build |

Multiplayer remains a later project with its own gates.

## Pointers

- [Capture-to-Godot pipeline design](pipeline/ROOM-CAPTURE-PIPELINE.md): the MCP/skill, method recommendations, and the garage test case.
- [Cleanup plan](CLEANUP-PLAN.md): what is deleted, kept, or generalized, and the questions that gate deletion.
- Historical: [earlier world vision](WORLD-VISION.md), [earlier roadmap](roadmap/ROADMAP.md), [earlier backlog](roadmap/BACKLOG.md).
