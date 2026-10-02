# ADR 0001: native Godot with C# for the single-player game

**Status: accepted by founder direction, 2 October 2026.** This accepts the platform and implementation direction; it does not certify a build, migration, performance target or completed S0 gate. Implementation evidence must be recorded separately against an exact commit and toolchain.

## Decision and scope

Build the initial single-player EnFractal experience as a **native Godot application with C# as the primary language for new application and domain code**. Begin on Windows, retaining the current Compatibility renderer until a measured art or performance requirement justifies changing it. Python remains appropriate for geodata preparation and the existing local persistence service; SQL/PostgreSQL and Godot shaders retain their current roles. Existing GDScript is supported during an incremental transition.

The active location is the **Pfluger Pedestrian Bridge / Lady Bird Lake district**, bounded by 6th Street to the north, Barton Springs Road to the south, Congress Avenue to the east and MoPac to the west. Start with a bounded landing/trail/waterfront route. Preserve Barton Creek packages, fixtures and saves; further Barton-specific content development is on hold.

The initial player height is exactly **0.30 m (30 cm)** in real-scale geography. Authored measurements, interface measurements and tests use metric units. The companion has its own stable identity and customizable body; its default and dragon/mount dimensions are separate capability decisions, not implied by the player's height.

The embodied, dedicated game-only BYO AI is central to S0–S6. Browser delivery, Bevy comparison and a custom renderer are no longer active implementation choices. Multiplayer, real-account services, replication and hosted world orchestration remain deferred until single-player acceptance and an explicit founder go decision. Native local AI integration does not authorize these additional systems.

This decision supersedes the platform uncertainty in the older [phase 0 record](../phase0/decisions.md) and the browser/native bake-off proposed in [research 14](../../roadmap/research/14-single-player-platform-and-engine.md). Their observations remain historical evidence. The current [roadmap](../../roadmap/ROADMAP.md), [backlog](../../roadmap/BACKLOG.md) and [world vision](../../WORLD-VISION.md) define the experience and execution sequence.

## Baseline inspected

Published implementation `750ef88284322b6ab736ba7f4bd4f333c954cd27` contains a GDScript Godot application, a Python map builder, Godot shaders and a Python/PostgreSQL local save/travel service. The checkpoint reports 24 engine suites; those reported results are not C# migration evidence. At the start of this decision review there was no C# project or C# runtime source in the repository. Planning documents had uncommitted single-player, Pfluger and metric-avatar revisions; this ADR does not replace or revert them.

The local game currently owns creation validation through [creation_compiler.gd](../../../game/scripts/creation_compiler.gd), command authorization through [creation_authority.gd](../../../game/scripts/creation_authority.gd), and physical capability execution through [invention_runtime.gd](../../../game/scripts/invention_runtime.gd). [travel_runtime.gd](../../../game/scripts/travel_runtime.gd) provides the trusted persistence adapter. These are useful working boundaries to preserve.

The current character is still a 1.7 m fixture with human-scale movement and clearance constants. Neither this ADR nor a C# project file converts it to the accepted 0.30 m player. The current geographic package has 2 m terrain samples and visual structure approximations; it does not establish the detailed traversal surfaces required at the new scale.

## Incremental C# boundary

1. Pin a verified Godot **.NET** editor/export-template patch and compatible .NET SDK in the repository's reproducible toolchain configuration. Begin by matching the existing 4.7.2 engine baseline where available; any patch change needs explicit regression evidence. A `dotnet` host alone is not evidence that a build SDK exists. Retain the exact versions and source/artifact provenance in the S0 report.
2. Add the C# project, typed configuration/profile code and a narrow adapter to the existing GDScript compiler/authority. Keep engine-independent records and calculations separate from scene-node construction. Scene changes and physics activation remain on Godot's owning thread.
3. Initially, the existing compiler remains the sole producer of accepted creation artifacts and their hashes. C# forwards bounded requests and checks the returned result shape; it does not implement a second creation validator or reinterpret accepted source with different numeric rules. Additional validation at an adapter boundary may reject malformed transport input but cannot grant a power the authority rejects.
4. Move cohesive implementations to C# only when their contract fixtures pass on both sides. A temporary comparison runner may exercise the old and new implementations, but only one may own live mutations. Remove or explicitly retire the replaced production path after acceptance. Preserve useful GDScript scene/presentation helpers rather than requiring a mechanical whole-repository translation.
5. Treat compiler, permission, physics and storage changes as separately reviewable changes. Changing language does not by itself justify changing movement, schema semantics, saved appearance, costs or collision. Measure managed/native crossings, allocations and scene costs before making performance claims.

The first C# slice should prove one typed metric/profile read and one valid/invalid command round trip through the real GDScript boundary. It should not wait for castle, dragon, weather, speech or network systems.

## Rules and trust boundary

The local world authority owns outcomes even when no remote server exists. Player controls, the manual editor and the companion submit typed intentions through the same protected game operations. The compiler derives artifacts and resource costs; the authority checks current identity, grants, target/world revisions and protection; the runtime applies supported effects and rechecks ongoing permissions and budgets. Appearance changes do not replace an identity or erase restrictions.

The companion receives a dedicated, revocable game grant and only permitted observations. Identity and grants come from the trusted local adapter, never from model-supplied fields. Follow, navigation, animation and continuous force/water behavior run in ordinary game code; model calls occur at intention or job boundaries. Stop revokes queued and active agent work. World text, asset metadata, source descriptions and model output are data, not instructions to expand permissions.

The AI surface cannot expose arbitrary scripts, shaders, shell commands, local files, SQL, provider credentials, role changes, unlock operations or arbitrary save-envelope writes. A model cannot approve itself. Routine reversible actions may execute within an existing scoped grant; consequential changes bind any required player review to the exact operation, targets, world, artifact/revision and expiry. Locked source, supporting terrain and declared access/support relationships require protection against both direct edits and indirect physical effects.

The [save/travel service](../../../services/save_travel/README.md) trusts the Godot host to validate gameplay envelopes. Its loopback bearer token is a host credential, not an agent grant. Keep it and the database DSN out of companion context, tool results, logs and saves. The C# adapter must use the existing authority/persistence seam; it must not give the companion `world_save`, `bootstrap` or a database connection. A successful provider response or authenticated MCP call still requires game authorization.

Local snapshots currently expose fixture-wide state, and live-clearance/distance callbacks are optional in the existing API. Before exposing companion tools, provide filtered observations and require the relevant host checks to be installed; absence must not silently admit an action. This is local integration work, not a reason to build account or multiplayer services.

## Metric and avatar-profile contract

Use metres, seconds and explicitly named derived SI units for physics and geographic geometry. Keep geographic source coordinates and the project's bounded local physics frame distinct. Angular quantities must name their convention and unit; existing creation `rotation_deg` values remain degrees until an explicit schema change. Do not reinterpret old values during C# conversion.

Introduce a versioned small-player profile whose identity and version are independent of world rules and appearance. Its initial height is **0.30 m**. Its remaining authored fields must cover body shape/radius, eye/camera placement, collision/contact margins, floor snap, step/slope behavior, navigation clearance, speed/acceleration, jump/glide, interaction reach and recovery clearance. Their tuned values require a small traversal fixture rather than an automatic scalar applied to the old 1.7 m controller. Do not globally scale the world or its gravity to achieve the smaller body.

The selected profile identifier, version and content hash must be observable in the running build and included in new saved-world compatibility metadata before profile-dependent state is persisted. A profile update must not silently move a saved avatar into an unsafe surface or change an accepted creation's collision. Companion bodies and riding/morph profiles have separate identifiers with explicit interaction rules. Changing either body's form retains its logical identity and grants.

Preserve original CRS, horizontal/vertical units, datum, acquisition metadata and raw source values at import. Convert explicitly into the canonical metre-based representation once. Use the source CRS/unit definition rather than assuming all feet-based sources use the same definition; do not merely rename source fields with an `_m` suffix. Tagged building heights need explicit supported-unit parsing or an honest inferred fallback. A finely encoded or resampled elevation grid does not acquire finer survey accuracy.

For 0.30 m traversal, test curbs, roots, steps, thin edges, ramps, deck approaches and water boundaries. Bridge decks and underpasses require separate supporting surfaces; the current one-height-per-horizontal-position terrain sampler alone is insufficient. Finer collision/render details may be layered over the regional geographic baseline. Fractional grid spacing requires an explicit runtime/package contract change because current grid spacing is integer-based.

## Storage and version contracts

Keep distinct version axes for the map/spatial definition, creation-source schema, compiler, rule set, style/generators, avatar profiles, saved-world envelope and adapter/protocol. A C# assembly version is not a substitute for those contracts. Accepted source remains durable; compiled meshes and artifacts are derived under pinned versions. Save acknowledgement follows durable commit; a timeout is an uncertain result that must reconcile through the existing receipt/revision path.

The existing readers enforce an exact `enfractal.creation-world` v1 shape with compiler version 1, `barton_painterly_v1`, the original `base_pin`, and the fixed fixture identities `local_player` and `guest_player`. The save/travel database also stores one pinned map. **Do not append companion/profile/lock fields to this envelope or bootstrap a Pfluger map into the existing Barton database.** That would violate the current contract rather than perform a migration.

Use a separate, explicitly identified new-world namespace/profile for Pfluger work while preserving the v1 reader and the original database. Introduce new metadata through a versioned envelope and reviewed loader/migration when those features are implemented. A migration must preserve source, prior revisions and locks, validate new rules/profiles, produce a distinct recoverable result, and leave the original untouched on failure. Cross-region blueprint import creates destination-local instances after validation; it never reinterprets an old placement in a new geographic origin.

Preserve the existing storage implementation initially. Replacing PostgreSQL with an embedded store is a separate decision requiring measured first-run benefit and equivalent save/receipt/recovery behavior. Never treat `.cache/postgresql/data` as disposable. Backup, migration and restore tests use new isolated destinations; application tests must not overwrite personal saves.

Canonical hashes need special care at the language boundary. GDScript creation hashes and Python storage-envelope digests currently serve different contracts. Do not compare them as interchangeable hashes or assume `System.Text.Json` defaults match either canonicalizer. Before porting a hash-producing implementation, use exact-byte fixtures for key ordering, Unicode escaping, negative zero, integer/float normalization, representative fractions, exponent notation and safe-integer limits. Preserve rejection of nonfinite and oversized/deep input.

## S0 exit evidence

S0 is complete only when a checkpoint records the following against the actual changed commit and pinned toolchain:

| Check | Required evidence |
|---|---|
| Native toolchain | C# project builds, Godot .NET imports it and runs its C# entry/adapter; exact SDK/editor/template versions recorded; no standard-editor fallback hiding missing C# execution |
| Shared rules | A C# caller invokes the actual existing compiler with valid and invalid inputs; results, errors and hashes match the direct path. Retained authority tests verify revisions and save round trips; the C# mutation adapter is S2 work. No second live mutation owner |
| Metric/profile boundary | The new typed configuration carries exactly 0.30 m, rejects invalid units/nonfinite dimensions and identifies the profile version; legacy values are not silently scaled; full small-body traversal remains an S1 gate |
| Existing behavior | The relevant existing 24 local engine suites run with the .NET host; preserve the GDScript fixtures. Run database/recovery suites if the persistence path changes; no new multiplayer tests are commissioned |
| Save compatibility | An isolated v1 save loads, recompiles and round-trips without semantic loss; incompatible base/profile/envelope input cannot overwrite it; tested destinations are separate from personal worlds |
| Native delivery | A release export actually executes C# and the GDScript compiler adapter and exits cleanly on Windows; record first-run instructions. Visible gameplay inspection follows with S1. Headless tests supplement this; they are not a gameplay or device-performance certificate |
| Next-stage limits | Record unavailable hardware/data/client integration and the next independent packet. Missing S2 provider access does not block native S0 acceptance or justify calling a mocked assistant real AI |

No art score, compiler test count or successful export establishes the new first region, player-scale movement, companion experience or the complete S0–S6 product.

## Next-stage blockers and test gaps

There is currently no player-AI provider adapter, selected real BYO client, speech integration or paired game-only agent in the inspected implementation. The next integration must name one actual supported client/provider route, its explicit local connection and credential ownership, and verify tool calling, interruption, failure handling and speech/text support. Do not substitute this privileged development assistant for the restricted player companion, assume a consumer subscription grants API use, or invent credentials. Fixture commands prove plumbing only. Authorized client pairing is the external input needed for a real S2 demonstration; unrelated S1 work can proceed meanwhile.

Existing tests cover the local workshop and save/travel fixture, not the new contracts. Add focused tests with the implementing packet for: small-body clearance/camera/recovery; supporting-surface selection over and under bridge decks; companion identity and grants; per-object locks and indirect effects; source-driven vegetation/construction changes; C# serialization and interop failure paths; versioned profile/world migration; and actual AI injection/cancellation behavior. The public-source Pfluger package and its verified unit/datum/structure provenance also remain future evidence. Preserve those distinctions when recording progress.
