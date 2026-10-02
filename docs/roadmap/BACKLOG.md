# Enfractal execution backlog and agent assignments

Planning baseline, 30 September 2026, updated for the founder's [world and creation vision](../WORLD-VISION.md) on 1 October. The [Barton Creek map and local workshop](../../maps/barton_creek/README.md) now exercise parts of E01–E08A through a local walking, path/platform editing and save/reload fixture; the [first checkpoint](../engine/checkpoints/phase0-1.md) leaves the contract, art, low-hardware and shared-world gates open. The [roadmap](ROADMAP.md) defines scope, budget, provisional architecture and phase gates. Detailed research task IDs are supporting checklists; these E identifiers provide one coordinated dependency order. In particular, there is one creation compiler, one authority loop and one portal transaction design. [Structured-world requirements](research/11-structured-world-and-style-system.md) apply to the relevant packets below.

The [latest bounded review](../engine/checkpoints/reference-guided-2026-10-02.md) records the founder's impressionistic 3D references, independent art scores, and a three-process Windows authority fixture. It leaves the same phase gates open.

The [painterly pipeline diagnosis](research/13-painterly-pipeline-diagnosis.md) redirects E04/E08 to ART-0–5. The subsequent [ART-0–3 implementation checkpoint](../engine/checkpoints/art0-3.md) repairs the orientation defect and delivers original editable tree assets, painted materials and a playable patch. All 15 engine checks pass. Two independent final art reviews score standard 6/10 and one scores low 5.8/10; the unchanged 8.5 visual gate remains open. Finish the habitat/bank/material relationships before wider expansion. ART-4 device measurement and ART-5 reusable generation acceptance remain pending. The old zero-texture/no-cutout fixture budget is not a product requirement.

## Working rules

**E11–E14 implementation update, 2 October:** The [manual invention checkpoint](../engine/checkpoints/manual-invention.md) implements the bounded local compiler/editor/permissions/creative loop, including five shared-capability designs and actual map/editor tests. This advances the main roadmap's Phase 3; it does not declare preceding remote-play gates or the separate art gate complete. The existing path/platform join remains a separate preserved fixture pending convergence into the general editor.

Start with two independent builders and one reviewer when there is enough work that can proceed without shared-file edits. Use at most three active implementation packets until the founder can review them comfortably. Research/fixture preparation may use more parallel agents. The founder reserves integration and actual playtesting time; no agent may lower an acceptance criterion merely to mark its own task complete.

Each packet produces one reviewable change, instructions to reproduce it, evidence from relevant checks, limitations, and a handoff note. Keep implementation packets around 1–3 working days; split an item below into smaller changes if it exceeds that. Estimates in the roadmap apply to phases, not an assertion that every table row fits one agent turn.

Keep a **cross-layer comparable-projects research agent** alongside the builders. Its first [ten-layer evidence matrix](research/12-comparables-and-layer-practices.md) covers map/provenance, streaming, style/editing, physics, multiplayer, durable state, portals, AI/MCP, player experience, and operations. For each new packet, the research agent checks current primary documentation or source, records the mechanism and rights boundary, names what will and will not transfer, and proposes one measurable EnFractal experiment. Revisit version-sensitive references at the relevant checkpoint; a comparable project's feature is not acceptance evidence for our implementation.

Use proposed file ownership, not files already created: `contracts/`, `world-data/`, `client/`, `runtime/`, `services/`, `tests/`, `assets/`, `ops/`. Establish actual repository structure only when implementation starts. Worktrees/branches isolate edits; they do not eliminate shared-interface coordination. The integrator owns dependency versions, schema revisions, migration order and the final merge.

## First four packets

| ID | Assignment | Depends on | Concrete output | Acceptance and reviewer |
|---|---|---|---|---|
| E01 | Founder/product agent: scope and feasibility decisions | This plan | One-page decision record for native Windows, initial real location, avatar sizes, camera, expected test devices and candidate host geography | Founder can explain the first 15-minute experience and defer listed non-goals; reviewer checks budget and device assumptions |
| E02 | Contracts agent with runtime ownership | E01 | Units, `WorldId`, `FrameId`, base/collision revisions, blueprint vs placement, stable semantic object IDs/relationships, evidence-confidence labels, generator/style versions, capability/error catalogue, auth/transport/presence boundaries, durable action semantics and minimal data inventory/retention policy | Map, art, physics, persistence and MCP reviewers agree on edit examples; no duplicated compiler or authority owner; credentials/raw prompts excluded from routine logs |
| E03 | Stack/performance builder | E01, minimal E02/E04 fixtures | Capped Godot native/headless feasibility scene and measurement report; verify maintained authenticated/encrypted gameplay transport support; conditional Unity comparison only for an identified blocker | Native + Linux headless export, capsule/terrain/representative stress, measurable memory/frame/tick data and supported transport path; separate reviewer reproduces; stop at ~20 founder hours then decide |
| E04 | Map and art fixture agents in separate files | E01 | Small licensed Barton Creek terrain/photo sample, source-versus-illustrative labels, preliminary material treatments and a common walking-height comparison route | Same geography and geometry across treatments; no unlicensed assets; map conversion/corners checked; note that current palette captures do not yet pass final art or low-device gates |

E02 and E04 can proceed together after E01. E03 consumes the minimum frozen fixture contract, not finished art or every future schema. No hosted service is necessary yet. If the platform, license or debugging gate fails, record the blocker and choose a narrower alternative before E05–E14.

Optional map-authoring experiment within E04: after region/source selection, compare a small licensed fixture prepared through [QGIS MCP](research/10-qgis-mcp-assessment.md) with the reproducible GDAL/PROJ recipe. Record source rights, output agreement and peak memory. E04 and E05 must still be achievable without QGIS MCP; it does not become a player runtime or a hosted service.

## Region and movement

| ID | Assignment | Depends on | Concrete output | Acceptance and reviewer |
|---|---|---|---|---|
| E05 | Map/data builder | E02–E04 | Reproducible regional package recipe, quadtree IDs, datum conversions, checksums/notices, feature IDs with evidence/confidence and a sparse-delta fixture | Coordinate/height round trips, source gaps and dateline/pole addressing fixtures; source resolution and invented details honestly reported; map reviewer |
| E06 | Renderer builder | E03, E05 | Coarse/fine tile residency, asynchronous decode/upload, cancellation, bounded disk/RAM/GPU queues and fallback view | Glide across tile seams and portal-like large jumps without holes under valid collision; corrupt/missing assets fail safely; performance reviewer |
| E07 | Physics builder | E02, E03 | Capsule controller, walk/jump/glide, gravity presets, wind adapter, speed caps and safe recovery | Slope/seam/thin-wall fixtures; local frame bounds; no assumed deterministic cross-client physics; independent physics reviewer |
| E08 | Art/UI builder | E03, E04 | Grounded painterly reference scene at walking height, original editable source assets, regional material roles, natural-scale starter kit, low-detail versions, readable effect symbols and initial settings | Recognizable Barton Creek character and modern building types; mesh/material/texture bounds and protection/force/portal cues readable on low profile; founder plus accessibility reviewer |
| E08A | Structured-scene builder | E02, E05, E08 | Small semantic object/relationship fixture and deterministic join generator for an authorized path meeting a platform, step or shelter; retain editable parameters and source labels | Move one part, regenerate only affected joins, keep stable IDs, preserve nearby work and reproduce result from pinned source/generator/style versions; map, art and runtime reviewers |

E05/E07/E08 can progress in parallel after contracts. E08A uses their small shared fixture; it does not require a complete city-scale semantic conversion. E06 depends on real package output but can start against a tiny fixture. The exit is a geographically situated walking/gliding scene with measured bounded resources and one editable construction relationship, not a polished global flyover.

## Shared play and inventions

| ID | Assignment | Depends on | Concrete output | Acceptance and reviewer |
|---|---|---|---|---|
| E09 | Networking/identity builder | E02, E03, E07 | Maintained authenticated and encrypted gameplay/control transports, server identity/session-key handling, logical avatar identity, sequenced commands, host authority, session replacement and redacted logs | Invalid server identity, tampered/replayed packets, logout/revocation, forged owner/position, duplicate sessions and rate floods fail safely; no custom cryptography or credentials in URLs/logs; security reviewer |
| E10 | Replication builder | E06, E09 | Interest sets, snapshots/interpolation, local movement prediction and versioned traffic classes | Two remote clients agree on effects at loss/jitter; private state excluded; measured bytes; network reviewer |
| E11 | Runtime compiler owner | E02, E07; E08A fixture | Single manifest validator/compiler for capability, semantic part and visual contracts, bounded IR, lifecycle and aggregate compute/render ledger | Ten valid and twenty invalid fixtures, cycles/NaN/feedback/quotas/unsupported material or join/unknown ops; independent reference-model review |
| E12 | Manual editor builder | E08, E08A, E11 | Inspect and edit supported parts/relationships, template/parameter editing, bounded local isolated preview consuming E11 artifacts, cleanup, draft import/export and placement confirmation | Move a path/platform part and see regenerated joins; reopen and revise source intent rather than a fused mesh; local preview grants no live authority; preserve draft on errors; UX reviewer |
| E13 | Permissions/physics builder | E07, E09, E11 | Immutable public areas, contained workshops, explicit consent and bounded directed effects | Revocation and chained momentum cannot bypass protection; escape/recovery fixtures; independent adversarial reviewer |
| E14 | Product integrator | E10, E12, E13 | Ridge/workshop loop, composed storm dragon and at least three other functional recipe fixtures; shared placement still goes through E09/E13 authority | Functional combinations without special-case creature code; founder-controlled fixture sessions first, eight-person creative-depth observations scheduled within E33 after research preparation |

Do not have the AI agent build another compiler under E23. It consumes E11. Network simulation capacity, body budgets and client animation detail are separate controls. The first two-player proof does not claim the eight-player ceiling until later combined-load testing.

## Persistence and portals

**E15–E20 local implementation update, 2 October:** The [save and travel checkpoint](../engine/checkpoints/save-and-travel.md) records implemented PostgreSQL storage, local sandbox travel/invitations, recovery tests and isolated restoration, plus the remaining acceptance gaps per packet. Actual remote accounts, hosted simulation workers, queue/load acceptance and an off-host restore are not marked complete by this local evidence.

| ID | Assignment | Depends on | Concrete output | Acceptance and reviewer |
|---|---|---|---|---|
| E15 | Persistence builder | E02, E09, E11 | PostgreSQL schema/migrations, durable action IDs, hash/revision-bound placement receipts, semantic object relationships, source intent/generator-style versions, blob references and motion checkpoint policy; bind E12 confirmation to durable placement | Retry/unknown commit produces one placement; crash preserves acknowledged editable parts and joins; reload can regenerate pinned derivatives without silently changing older work; storage reviewer |
| E16 | Home permissions builder | E13, E15 | Stable parcels/exhibit addresses, ownership roles, current permission revision and moderation hooks | No permission from a claimed blueprint owner; canonical Home state after reconnect; authority reviewer |
| E17 | World lifecycle builder | E05, E07, E15 | Base-only sandbox fork, independent deltas/rules, save/wake/drain/stop, shared host admission and fair queue | World slot held until teardown; paused offline physics; quota/decode/count limits; no third preview process; operations reviewer |
| E18 | Portal builder | E11, E16, E17 | Invitation UI/ACL, destination manifest, hash-bound adaptation, fenced transfer state machine and Return Home | One logical avatar, consumed invitation and reserved capacity; compatible return; no Home item duplication; independent portal reviewer |
| E19 | Fault-test agent | E15, E18; test design starts at E02 | Exhaustive named transfer interruption/race suite plus generated sequences | Kill/retry at each state, unknown DB commit, stale lease, changed rules, blocked invitation, double reconnect, all-eight return burst; reproduction seeds retained |
| E20 | Recovery/ops builder | E15–E18 | Off-host backup, retention, export, restore, migration rehearsal and incident instructions | Restore into a clean isolated host with consistent assets/revisions; measured actual loss/recovery window; reviewer does restore without author guidance |

Hold public expansion for any unresolved authority or data-loss defect. A failed transfer freezes/reconciles; it cannot infer non-commit from a timeout. Source-owned independent creations stay in their world, and imported blueprints receive destination-local instances. Default fork rights exclude other players' Home builds.

## Optional AI and MCP

| ID | Assignment | Depends on | Concrete output | Acceptance and reviewer |
|---|---|---|---|---|
| E21 | Command gateway builder | E09, E11, E15, E16 | Restricted game API for inspecting authorized semantic parts, typed edit drafts/jobs, preview and publication; ACL/handle checks | Manual/MCP paths reach identical structure, style, physics and permission rules; guessed IDs/expired grants fail; gateway reviewer |
| E22 | Preview builder | E11, E12, E17, E21 | Hosted queued trial reusing E12 preview interface in optional worker slot; metrics, timeout, cancellation and cleanup | Game remains responsive; trial cannot exceed global work/memory or leave dangling bodies; performance/security reviewer |
| E23 | MCP integration builder | E21, E22 | Pinned supported SDK/protocol, explicit handles, local stdio adapter and tool/resource schemas | Two actual clients discover/validate/preview; version mismatch clear; no arbitrary execution tool; independent compatibility reviewer |
| E24 | Inference builder | E11, E21 | One provider adapter, permitted/redacted context, provider-routing notice, model-neutral fixtures, timeouts, max-charge reservations and spending ledger | Two attempts/session maximum; raw prompts/chat not stored by default; malformed output preserves draft; lower dollar/token cap wins concurrently; cost/privacy reviewer |
| E25 | Publication UX builder | E12, E15, E21, E22 | Extend/reuse manual placement review for asynchronous AI/MCP: show changed parts/joins, material and cost effects, source versus invented detail; bind creator/world/parcel/hash/revision/expiry and idempotent command | Changed draft/rules/consent invalidate approval; repeating request creates one revision; human can review appearance and scope before publishing; security reviewer |
| E26 | Independent security agent | E13, E18, E23–E25 | Threat model, prompt-injection/handle/credential/quota/race cases, restricted-agent profile and issue report | No cross-user access, unbounded cost or privilege growth; confirmed mitigations and limits; founder accepts remaining non-blocking risks |

Hosted remote MCP should wait until local tools and game authorization are stable. Include its maintained authorization library and compatibility tests as an explicitly scoped subtask if required for the alpha; external assistants can initially connect through the local adapter. Gameplay packets never use MCP. Model generation is optional, and no cloud GPU or local client LLM is required.

## Hardening and private alpha

| ID | Assignment | Depends on | Concrete output | Acceptance and reviewer |
|---|---|---|---|---|
| E27 | Performance reviewer + renderer fixes | E06, E10, E14, E17–E19, E22 | Reproducible walking-height and edited-scene routes, raw client/host metrics and threshold report | Grounded painterly look and essential cues survive low settings; low device and laptop targets; simultaneous Home/sandbox stress, cold load, 100 portal trips and two-hour client soak; explicitly lower cap if necessary |
| E28 | Accessibility/UX builder | E08, E12, E18, E25 | Keyboard remapping, scalable text, non-color cues, reduced motion and clear pending/saved states | Fresh-user flow works at 200% UI scale/keyboard/no sound/low graphics; document screen-reader limits; human review |
| E29 | Community/tools builder | E09, E13, E16, E18 | Conduct/report/block/quarantine/appeal, diagnostic export, tester notice, consent choices and research retention/deletion procedure from E02 | Misuse scenarios; block cannot fork Home; founder rehearsal; notice/optional recording and AI routing explained before recruiting testers |
| E30 | Release/operations builder | E20, E26–E29 | Pinned builds, clean Windows package, licenses, version/rollback/support policy; minimal telemetry event dictionary, denominator definitions and collection instrumentation | Fresh install/rebuild; event capture and deletion checked on developer fixtures; no raw chat/prompt collection by default; no request to disable protection; release reviewer |
| E31 | Remote multiplayer reviewer | E19, E27, E30 | Real two/four/eight-person sessions plus 50/100/200 ms loss/jitter fixtures | Correct consequences/reconnect and declared population cap under actual host profile; no averages-only capacity claim |
| E32 | Cost/operations analyst | E20, E24, E27, E31 | Actual host/storage/bandwidth/model/cash report, spend reservations and graceful limits | Under $100 actual monthly ledger; domain/IPv4/tax/one-time cash accounted; alert and admission cutoffs demonstrated |
| E33 | Founder with research agent | E28–E32 | First ten consented testers in paired appointments; observation notes and defect triage | Report all invites, starts, unassisted completions and seven-day eligible returns; fix blocking cases before expanding |
| E34 | Founder/product agent | E33 | Up to 20 activated testers across cohorts with four-week opportunity to return; iterate only highest-value obstacles | Denominators, service windows, support burden and meaningful repeat creation measured; no statistically inflated claims |
| E35 | Founder/release reviewer | E20, E26, E31, E32, E34 | Private MVP acceptance record with exact supported device/content/population limits | Every roadmap blocker resolved; restore drills and staffing affordable; establish next prioritized iteration |

The target eight combined players is conditional. A validated four-player alpha may still test the core experience, but publish the lower limit and update queues, plan claims and demand tests. Do not pass an eight-player criterion by quietly removing the sandbox workload or running clients locally on the server.

## Payment only after repeat value

| ID | Assignment | Depends on | Concrete output | Acceptance and reviewer |
|---|---|---|---|---|
| E36 | Business agent + founder interviews | E32, E34 | One costed creator-service package and five specific price/benefit interviews | Benefits already work; not all respondents are merely expressing encouragement; measured session/storage allocation |
| E37 | Billing builder | E15, E36, E38 scope | Hosted checkout, verified notification processing, idempotent entitlements, cancel/grace/refund paths | Duplicate/out-of-order/failed payment scenarios; no card storage; returned success page cannot grant service; billing reviewer |
| E38 | Founder with appropriate professional input | E29, E32, proposed E36 | Audience/privacy/content/consumer/tax review, actual service description, retention/export and support commitments | Required review costs funded, notices and procedures fit jurisdiction/audience; no generic compliance assertion |
| E39 | Founder/business reviewer | E35–E38 | Limited monthly pilot, two-cycle cost/use/cancellation report and reinvestment ledger | Positive measured cash contribution, meaningful free access, refunds/obligations/reserve funded; no automatic budget increase |

E37 can draft/test with fake payments after service assumptions exist, but real billing cannot precede E38 and the private MVP reliability gate. The dependency is the review's scope and findings, not a vague promise to obtain advice eventually. Do not sell annual/lifetime services, speculative land or unmeasured concurrency.

## Later tracks with separate permission and funding

| ID | Expansion | Prerequisite evidence | First bounded experiment |
|---|---|---|---|
| E40 | More Earth and authority cells | E35, measured demand/cost | A second region; then two workers with one canonical state and whole-assembly handoff |
| E41 | New physics and scales | Stable vocabulary, performance headroom, E13/E26 suite | One added primitive or two explicit scale classes with habitat proxy outcomes |
| E42 | AI avatars | E35, reliable game-only identities, affordable inference | One visible agent, bounded area/lease, navigate/interact/wait/stop; no account/economic actions |
| E43 | External hosting/federation | E18/E19 recovery, neutral export/conformance kit, funded independent review | One allowlisted operator, audience-scoped visitor credentials, incompatible-power rejection and destination disappearance |
| E44 | Platforms and services | Retained demand and founder capacity | Browser atlas/editor or one native port before promising mobile/VR parity; group hosting before marketplace |

These are discovery tracks, not fixed release dates. Expand code vocabulary, geographic coverage and hosted concurrency separately so failure can be attributed and reversed.

## Agent handoff template

For each coding packet, the coordinator should provide this complete request in ordinary language:

> Implement E[number] within the agreed MVP scope. Use the linked contract versions and fixtures. Own only [files/module]; coordinate changes outside that boundary with the integrator. Depend on [completed packets], and use a stub for [explicitly permitted unavailable dependency]. Deliver [concrete artifact] with [named acceptance evidence], reproduction steps and limitations. Do not add services, change the budget, broaden player permissions or change shared schemas without a reviewed decision. Stop and report if the acceptance conditions require a different architecture. A separate reviewer will validate the result.

Reviewer request:

> Review E[number] against the original requirement and invariants, independently from its author's assertions. Reproduce the relevant build/test or play route. Look for cross-world/authority/resource regressions and missing failure cases. Report actionable issues with evidence and severity; do not rewrite unrelated modules or lower the agreed thresholds. Separate observed failures from hypotheses.

Founder acceptance record: task ID; commit/build; contract versions; evidence links; observed hardware/host configuration; unresolved risks; accepted limits; cost impact; next dependent packet. Keep actual test outputs in a versioned evidence directory once implementation begins. A status of done means the founder can reproduce the delivered behavior, not merely that an agent returned a patch.

## Recommended weekly rhythm

Choose one player-visible milestone each week or work cycle. At the start, resolve interfaces and assign at most two independent implementation packets. Mid-cycle, integrate one small slice and run the actual client. At the end, obtain independent review and update the evidence/decision log; carry failed gates forward openly. During alpha reserve 4–6 founder hours for scheduled sessions, support and recovery practice before allocating the remaining time to new features.

Keep a small triage order: data/authority/billing defects first; crashes and unusable performance second; confusion preventing the core loop third; creative breadth fourth; geographic and cosmetic expansion last. A surprising player invention may justify changing that order, but it must still respect persistence, permission and resource invariants.
