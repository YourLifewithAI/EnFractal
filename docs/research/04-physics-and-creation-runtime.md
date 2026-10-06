# Physics and the creation runtime

> **Retained research, context only.** Written for the shared-Earth and Pfluger-district plans. The technical findings still apply; the sequencing, locations, scale and any reference to S0–S6, SP packets or E packets are superseded by the [room-scale direction](../ROOM-SCALE-DIRECTION.md). Links to removed files were unlinked; those files are on the `geography-era-final` branch.

> **Current direction — 2 October 2026:** The [revised roadmap](../history/ROADMAP.md) and [backlog](../history/BACKLOG.md) govern scope and order: single-player embodied AI first, multiplayer last. The earlier two-player demonstration and small wind/glide vocabulary below no longer define the first product. Bounded storms, floods, rideable companion forms and protected transformations are now active single-player capability work. The dated research below is retained for context.

Planning recommendation, 2026-09-30. This is a design and validation plan, not an implemented engine or a measured capacity claim. Scope assumes one developer with coding agents, a Windows client, a small private alpha, and the project's total hosting/AI ceiling below $100 per month.

## Recommended starting point

Use one authoritative simulation per active region and an existing rigid-body engine. If the stack bake-off selects Godot/C#, use its built-in Jolt backend. Keep creation rules in ordinary, independently testable C# domain code; put Godot-specific body construction, collision queries, and stepping behind a small adapter. A world is a contract plus persistent state, not one continuously running planet-sized physics scene.

Start with walking, jumping, simple gliding, bounded wind, primitive props, and a few attachments. The first convincing demonstration is two players combining these operations into a small storm dragon that helps a consenting friend reach a ledge. They should see the same consequence, save their designs, change gravity in a personal destination, and return with their home-world state intact.

This is deliberately an expressive game rules system. It is not universal scientific simulation. Weather, flight, materials, creature bodies, and terrain edits receive explicit approximations. Inventions become possible when supported operations combine; a new field name never creates a new executable power.

## Existing-engine decision

| Candidate | Useful foundation | Cost or limitation for this project | Decision |
|---|---|---|---|
| Godot + built-in Jolt | Existing rigid-body integration, terrain collisions, shape queries, character collision support; C# gameplay without maintaining a separate native binding | Some exposed joint properties are unsupported; reported contact impulses have limitations; integration must be profiled | Leading candidate if Godot wins the client bake-off |
| Godot Physics | Same Godot application structure; provides an in-engine comparison | Different contact/joint behavior requires retuning; no assumed performance advantage | Keep as controlled fallback for a reproducible Jolt defect |
| Unity + PhysX | Established C# scene/editor workflow and rigid-body simulation | Engine/platform choice changes the distribution and maintenance plan; determinism still has conditions | Credible whole-stack alternative, not a second simultaneous implementation |
| Rust + Rapier | Rust engine with rigid bodies, joints, sensors, snapshots and bindings | Requires renderer/editor integration and a different core language; integration burden matters more than theoretical language speed | Consider if Rust stack is chosen or Godot proves unsuitable |
| Unreal + Chaos | Integrated physics and extensive engine tooling | Our judgment: too much engine/editor scope for this small, hardware-constrained prototype unless an existing Unreal skill base changes the tradeoff | Later comparison only; no destruction-system investment now |
| Direct C++ Jolt | Native access to engine facilities and more control over integration | Developer owns networking, tooling, scene/editor integration and binding maintenance | Escape hatch for an identified bottleneck, not MVP foundation |

Godot documents built-in Jolt and its joint/contact caveats; avoid copying settings between backends and assuming equivalence. [Godot Jolt documentation](https://docs.godotengine.org/en/stable/tutorials/physics/using_jolt_physics.html) Rapier documents its Rust implementation and bindings. [Rapier overview](https://rapier.rs/docs/) Epic documents Chaos as Unreal's physics solution. [Unreal physics](https://dev.epicgames.com/documentation/unreal-engine/physics-in-unreal-engine?lang=en-US) Direct Jolt is C++ and MIT licensed. [Jolt repository](https://github.com/jrouwe/JoltPhysics)

Bake-off fixtures: capsule on seams/slopes, fast prop against thin wall, a twelve-body joint assembly, sixteen simultaneous gusts, sleeping/waking props, and crowded spawn points. Use identical geometry, movement limits and workload. Capture CPU tick percentiles, memory, tunneling, stuck avatars, and implementation friction. Pin the chosen engine release and settings in the world-contract build manifest. Do not upgrade a live ruleset without replaying these fixtures.

## Simulation, authority and networking

Proposed initial cadence: **30 Hz authoritative gameplay/physics**, with **15 Hz snapshots** and independently rendered frames. Compare 60 Hz physics with 30 Hz commands if 30 Hz contacts or flight fail acceptance. These values are hypotheses, not achieved performance. Fix the cadence for each published ruleset; graphics settings do not change it. Godot distinguishes fixed physics ticks from rendering and documents interpolation. [Physics interpolation](https://docs.godotengine.org/en/stable/tutorials/physics/interpolation/physics_interpolation_introduction.html)

The server receives input intentions with sequence numbers: move, jump, activate supported ability, interact. It does not accept a client's claimed position, force result, target ownership, or successful payment. Validate authentication, input bounds, freshness and rate limits before queuing. Each tick:

1. Apply due admission/ACL/world-contract changes in a defined order.
2. Validate commands against current state; reserve their work and target budgets.
3. Evaluate bounded behavior graphs and assemble permitted movement/forces.
4. Step physics; reconcile contacts with game rules; emit authoritative consequences.
5. Update bounded state, persistence events and relevant replication snapshots.

Predict the local avatar controller and reconcile acknowledged input. Interpolate remote bodies from snapshots. Initially avoid client prediction of multi-body contraptions; a cosmetic animation can start immediately while actual movement awaits authority. Never award inventory or mutate protected property from prediction.

Do not base networking on identical simulation across clients. Jolt's deterministic operation requires ordering/build conditions; Rapier has optional deterministic configurations with tradeoffs; PhysX also documents conditional determinism. Those properties do not make an entire streaming multiplayer application deterministic. [Jolt architecture](https://github.com/jrouwe/JoltPhysics/blob/master/Docs/Architecture.md), [Rapier determinism](https://rapier.rs/docs/user_guides/templates/determinism), [PhysX simulation](https://nvidia-omniverse.github.io/PhysX/physx/5.7.0/docs/Simulation.html)

Keep input history, periodic authoritative snapshots and contract/build identifiers for diagnosis. Exact full-world replay is a later capability. Bound backlog and queued commands; overload first rejects new expensive work and reduces optional replication detail. Do not secretly enlarge the timestep or drop physics steps while claiming normal simulation. Persistent overload moves the region into a visible degraded/pause state with safe reconnect, then lowers future admission limits.

## Earth coordinates and terrain contact

Persist canonical double-precision Earth-centered coordinates, world identity and a stable regional `FrameId`. Use a dedicated double-coordinate type, not the engine's ordinary float vector. The map workstream supplies geographic/vertical-datum conversion. Convert into a local tangent frame for physics; the candidate playable core is 2 × 2 km, with coordinates kept within approximately 2 km of its origin. Render-origin shifts are client presentation operations, distinct from authoritative frame changes.

All shape dimensions, gravity, velocity, timestamps and impulses have explicit SI units. The local gravitational down direction comes from the regional frame. A small regional tangent approximation is acceptable for the first area; a global flyover is a different presentation layer. Planet-scale trajectories, orbital dynamics and continuous globe-wide physics are out of scope.

Godot documents float precision loss at large coordinates and the memory/performance/build implications of enabling double precision throughout the engine. Our initial choice is standard builds plus geographic double metadata and bounded local scenes; measure before adopting custom double builds. [Large world coordinates](https://docs.godotengine.org/en/stable/tutorials/physics/large_world_coordinates.html)

The terrain interface supplies `FrameId`, immutable base-data hash, monotonic `TerrainCollisionRevision`, and authoritative surface height, normal and material queries. Keep collision geometry independent from visual LOD. A visually unloaded ridge remains a collider while relevant bodies can reach it. Do not admit movement into missing collider tiles: hold at a visible loading boundary or use a validated safe location.

Terrain edits stage replacement visual/collision assets, then activate their agreed revision at a tick boundary. Preserve the previous collider until readiness. Initially changes are approved bounded patches inside a workshop; planet-wide erosion and arbitrary digging are deferred. Seam tests must cover neighboring tiles and revisions, not just smooth central terrain.

Later frame handoff transforms positions, orientations and linear/angular velocities once under a versioned transfer. A linked mechanism stays on one authoritative worker. No cross-worker rigid joints in the MVP. Transfer admission can reject an oversized assembly rather than splitting its solver ownership. The storage/portal plan owns the durable transfer state machine.

## Initial physics vocabulary

| Primitive | MVP behavior and restriction |
|---|---|
| Avatar body | One capsule or simple approved compound; walking, slope handling, jumping and recovery controller; visual wings/limbs need not become physical bodies |
| Rigid prop | Sphere, box, capsule or vetted convex proxy; capped mass, dimensions and speed; sleeping supported; static concave geometry only |
| Attachment | Fixed and hinge configurations verified on selected backend; capped bodies, links and motors; no arbitrary cyclic constraint networks |
| Wind field | Bounded sphere/cone, duration, acceleration and targets; authoritative field query, no simulated fluid particles |
| Glide controller | Bounded acceleration/drag and turning based on relative airflow; reduced fall speed; no computational-fluid-dynamics claim |
| Sensor | Nearby permitted entities/events only; capped radius, scan frequency and result count; cannot expose private objects |
| Material parameter | Small vetted friction/bounce palette; safe numeric ranges; no arbitrary engine-property writes |
| Visual/audio effect | Referenced approved asset and bounded intensity/lifetime; cosmetic output cannot apply hidden gameplay force |
| Terrain operation | Later MVP workshop patch request, subject to land permission and collider revision; unavailable in public starting space |

Characters need explicit gravity/force integration in their movement controller; selecting a character node does not automatically produce physically driven locomotion. [Godot CharacterBody3D](https://docs.godotengine.org/en/stable/classes/class_characterbody3d.html)

Starting collision scale: bodies roughly 0.25–4 m high, props at least 0.10 m across, and controlled speed bands. These are proposed authoring limits to test. True ant-scale terrain, continental creatures, ragdoll crowds, deformable construction, fluid simulation, general structural destruction, and high-speed vehicles are later capability projects. Cosmetic size variation cannot mislead players about authoritative reach or collisions; show a readable interaction envelope where necessary.

Storm-dragon recipe: approved creature shell + normal avatar capsule + glide mode + gust sensor/field + cloud particles + cooldown. A gust affects explicitly consenting nearby friends for at most two seconds, within a proposed eight-meter radius, with acceleration capped at 4 m/s² and a resulting speed clamp. Updraft volumes create the ridge challenge. Lightning is initially visual. Landing and safe recovery remain available if consent disappears or the gust ends. Tune these values for enjoyment after stability tests.

This recipe validates the technical architecture but does not establish enough creative depth for a lasting game. Test a gliding creature, a stationary updraft totem, and a consent-triggered rescue pad using the same capabilities. Within contained workshops, add a wind-driven hinged spinner and a sensor-triggered light so players can discover timing and feedback without public destruction. An anchored field uses the same wind primitive as an avatar ability; it must not secretly require bespoke totem code.

Use three small challenges: reach a ridge together, make a device a friend can operate, and remix someone else's design to solve a different route. Ask eight testers to create or modify one design without author intervention; seek at least six completions and four independently chosen functional combinations beyond the supplied dragon. These small-cohort gates are hypotheses, not statistical evidence. If play collapses into appearance changes and one obvious movement build, improve affordances and challenge design before adding geography or expensive physics. A bounded latch/interaction switch or one safe attachment may add more creative value than many visual options; each new primitive still requires permission and work-budget tests.

## Declarative language and execution budgets

The manifest is versioned data, preferably JSON for interchange. It names supported capability versions, shapes/assets, bounded parameters, triggers, graph edges and small typed state slots. It includes display metadata and declared maximum resource demand; server compilation computes actual reservations. Creator identity, permissions, balances and authoritative object IDs come from authenticated runtime records, never trusted manifest fields.

Keep three objects separate:

- `CreationManifest`: portable design and required capabilities.
- `CompiledCreation`: validated immutable artifact, hash and calculated limits.
- `CreationInstance`: world-scoped owner, revision, state, budget reservations and lifecycle.

A per-event graph is acyclic. Recurring behavior uses explicit timers or a small finite-state machine with maximum transitions per tick, rather than arbitrary loops. Graph acyclicity alone cannot stop feedback between objects, so every descendant event carries a runtime-assigned effect-root ID and charges shared quotas. Timers, pending events, query candidates and spawned bodies count as work. No arbitrary scripts, native plugins, network requests, file access or executable shader submission from player/AI manifests.

Proposed starting ceilings, to be lowered or raised only after profiling:

| Budget scope | Initial ceiling |
|---|---|
| Per creation | 16 behavior nodes, 8 dynamic bodies, 8 joints, 8 typed state slots |
| Behavior activity | 5 trigger activations/second; 32 node evaluations/activation; cooldowns cannot reset this allowance |
| Per field | 8 m radius, 2 s lifetime, 8 targets; bounded candidate collection as well as returned targets |
| Per region | 128 awake dynamic props, 32 active joints, 16 active fields; avatars separately admitted |
| Effect queue | Hard maximum pending events per actor/region; reject before enqueue; calibrate numeric size in profiling |
| Per actor | Aggregate creations, fields, timers and retained storage; no multiplying allowance by splitting one design |

These simultaneous maxima do not imply all combinations are affordable. Region and process ceilings override individual entitlements. For the first host, also cap aggregate demand across Home Earth, the active sandbox and trials at 128 awake props, 32 joints and 16 fields until simultaneous-load measurements support more. A region cannot reserve capacity already assigned elsewhere. Admission is eight people combined, at most four in the sandbox. Preview trials queue or reuse the optional sandbox slot; they do not create an unbudgeted third simulation. Include collider loading, restart and serialization peaks in host memory tests.

Separate gameplay energy from compute tokens. Unlimited fictional energy never grants more evaluation, memory or simulation capacity. An effect chain cannot start a fresh resource account merely by transferring ownership or emitting another event.

Validation returns structured paths and stable codes such as `unsupported_capability`, `parameter_out_of_range`, `permission_denied`, `region_capacity`, and `contract_changed`, with permitted alternatives. Admission reserves capacity before materialization. Quota exhaustion stops the offending behavior safely and reports why; it must not discard another player's persistent work.

## Permissions, indirect effects and different scales

Every consequential action checks the authenticated principal, current world contract, target permission/consent, location and remaining budget. Publication is not perpetual permission. Revocation invalidates queued actions and future force application by the next tick; already applied momentum needs an explicit safe handling rule.

Attribution is necessary but insufficient. Recording that Alice pushed a boulder does not itself prevent that boulder harming Bob's garden. General contact-by-contact permission isolation across unrestricted rigid mechanisms is a major engineering problem. The alpha therefore uses a constrained interaction policy:

- Public gardens/buildings are immutable and have no structural damage mechanic.
- Avatars cannot push other avatars by default; friendly lifts use revocable explicit consent and capped displacement.
- Player dynamic contraptions operate inside an authorized workshop envelope with physical containment and escape checks. Shared experiments require a common opt-in interaction group.
- Wind cannot push arbitrary public props across parcels. Protected public props are static; executable force targets are explicitly admitted.
- A field stops affecting a target when permission changes. Boundary sweeps, speed clamps and safe recovery prevent lingering motion from crossing a workshop's containment envelope.

Collision layers implement coarse categories; they are not a replacement for owner-specific authorization. If the selected engine cannot implement a safe restriction, remove that interaction from the shared-Earth vocabulary. Restore broader combinations only after an independent adversarial suite demonstrates the boundary.

For cross-scale consistency, the server owns one coarse outcome. A large avatar's foot can trigger an approved interaction against a habitat proxy; clients choose detail, not survival state. Detection, force exposure and collision shapes are explicit rules with bounded queries. An ant-detailed view and a distant dragon view must both receive the habitat's same revision. This is a later scale extension; MVP validates two permitted size classes without promising microscopic precision.

## Personal worlds and persistence

A personal world has its own `WorldId`, contract, physics space and instance namespace, even when sharing a process. The first alternative rule is 0.25× gravity in a copy of the playable patch. A later preset can change the gravity direction. These are supported contract variations, not arbitrary executable replacement laws.

Changing a contract creates a new revision, validates existing creations, and applies only after a safe pause/spawn transition. Unsupported instances become inert or remain unavailable with explanations. A personal world cannot alter the home region's physics settings. Appearance and blueprints may travel; foreign inventory, authority and functional abilities are revalidated by the destination. Portal arrival clears unsupported transient forces and creates a valid destination avatar.

Persist portable object state and accepted durable edits, not engine pointers or physics caches. On resume rebuild bodies, validate placement, and settle them safely. Dormant worlds do not simulate every missed tick or accumulate free physical output. Offline gardens may use a separate bounded elapsed-time rule later. Snapshot format, engine version and contract version are explicit, with backup-before-migration and rollback procedures.

## Agent-sized work and gates

These are future implementation tasks, not work performed during this planning phase. Use contracts/fixtures to parallelize; one integrator owns the final authoritative tick loop.

| Task | Independent artifact | Dependencies and acceptance |
|---|---|---|
| P1: domain contract | Units, manifest types, capability catalog, stable error taxonomy, authority matrix | Coordinate with AI/MCP and portal teams; ten valid and twenty invalid example designs agree across validators |
| P2: physics bake-off | Identical engine fixtures, profile captures, selected version/settings ADR | Can run beside P1; correctness plus performance gate, no decision from vendor benchmarks |
| P3: movement | Capsule walking, recovery, glide and limited wind adapter | Needs P1/P2; two clients agree after corrections; no client-position authority |
| P4: compiler/budgets | Bounded graph compiler, typed state and quota reservations | Needs P1; independently reviewed malformed graphs and event feedback cannot escape limits |
| P5: world geometry | Frame conversion, collision streaming/revisions and safe placement | Parallel with P3/P4; needs map contract; origin/corner/seam tests pass |
| P6: interaction policy | Consent, containment, collision categories and revocation | Needs P3/P4; malicious wind/prop/ownership chains cannot alter protected fixtures |
| P7: persistence/portals | Portable state adapter, separate gravity world, restart recovery | Needs region storage/portal protocol; repeated interrupted transfers produce one authoritative avatar |
| P8: independent test agent | State-machine/property tests, network fault/load fixtures and evidence report | Begins from P1 specification, not copied implementation assertions; gates integration |

Target gate workload: eight connected players/bots, 128 awake props, 32 joints and 16 fields in the chosen host profile for 30 minutes. Test both concentration in Home Earth and split allocation across simultaneously active Home/sandbox workers, including a sandbox cold load while Home remains occupied. Proposed 30 Hz gate: each simulation's total tick p95 below 20 ms and p99 below 30 ms, no growing work queues, no unbounded memory growth, no persistent lost or duplicated objects. These must fit the overall host resource budget; passing one isolated worker does not validate the combined deployment. Failure means reducing admitted workload before expansion.

Movement gate: selected caps and colliders resist tunneling; avatars recover from invalid placement; gravity/glide remain usable at 30 FPS rendering and 30/60 Hz candidate physics. Under 150 ms RTT, 30 ms jitter and 2% packet loss, authority remains correct and ordinary movement corrections converge without permanent divergent state. Set visual correction tolerances during the fixture bake-off rather than inventing guaranteed smoothness.

## Invariants and adversarial acceptance

Use generated command sequences against a simple reference model. Hypothesis supports rule-based action sequences and invariants; it can drive a black-box headless test API while C# unit tests cover domain functions. [Hypothesis stateful testing](https://hypothesis.readthedocs.io/en/latest/stateful.html) Aim initially for 10,000 reproducible short generated sequences plus named failure scenarios. This is defect discovery, not a proof of correctness.

Required invariants: one authority per live instance; no permission amplification; aggregate work never exceeds reserved limits; no non-finite numeric state; no cross-world state contamination; stable world outcome across graphics settings; no duplicate durable consequences for retried commands; and revision-consistent collision/terrain state.

Named scenarios include revocation between validation and execution; disconnect during a gust; repeated old input; NaN/overflow parameters; two edits racing on one tile; a timer emitting another timer; a gifted object trying to reset quotas; a high-speed body approaching protected space; a collider unload beneath a player; restart with a partially saved assembly; and a portal timeout after source suspension but before destination acknowledgement.

Costs are bounded primarily through admission and dormancy. This design introduces no dedicated physics service purchase or per-player inference loop. Actual server capacity and shared-process memory must be measured; the $100 ceiling is a budget constraint, not evidence of affordability. Remaining decisions are control feel, acceptable scale range, workshop interaction policy, and whether 30 Hz suffices. No expansion of the vocabulary should precede permission, persistence and budget evidence for the existing one.

All external links above are primary documentation inspected 2026-09-30. Stable/current documentation can change; archive the exact selected release references when implementation begins.
