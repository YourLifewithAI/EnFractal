# EnFractal execution backlog: single-player embodied AI first

**Revised 2 October 2026 against published implementation `750ef88`.** Execute this backlog with the [v0.4 roadmap](ROADMAP.md) and [world vision](../WORLD-VISION.md). The player and their separately embodied AI companion are the center of play. Spoken/typed wishes should produce expressive, physically meaningful changes while saved/locked creations remain protected. Native Godot .NET with C# is selected; browser and alternative-engine experiments are deferred unless a measured blocker justifies reopening them. Multiplayer is last, after single-player acceptance and an explicit founder go decision.

## Status and identifier rules

The [manual invention checkpoint](../engine/checkpoints/manual-invention.md) records the original Phase 3 local compiler/editor loop. The [save/travel checkpoint](../engine/checkpoints/save-and-travel.md) records original Phase 4 local PostgreSQL saves, sandbox visits, Return Home and recovery, reporting 24/24 engine suites. The [art checkpoint](../engine/checkpoints/art0-3.md) remains below the 8.5 visual target. These reports retain their historical names; they do not certify the new companion experience or remote play.

**SP identifiers below are the current execution order.** Existing E01–E44 identifiers retain their meaning in code, research and checkpoints; use the crosswalk below instead of relabeling old evidence. S0–S6 and M7 are the new roadmap phases. ART-0–5 remains an independent art workstream.

Preserve all existing saves and working local systems. In particular, `.cache/postgresql/data` contains saved worlds. Do not delete it, rerun setup unnecessarily, replace existing saves, or expose the trusted save/travel API to AI or Internet clients. Current fixed local identities and protected-garden fixtures do not implement per-creation lock protection or a delegated companion.

## Working rules

The initial player-height target is **0.30 m (30 cm)**. Use metric units for authored content, physics, UI measurements and tests. Preserve source-native unit metadata and validate conversion to meters at import; do not apply a second global scale to geographic data.

- At most two builders plus one independent reviewer on packets with distinct owned files. Split large capabilities into reviewable subpackets; a row below is not a claim that a flood/dragon system fits one agent turn.
- Every assignment declares product outcome, contracts, owned files, dependencies, supported inputs/outputs, acceptance evidence, exclusions and stop condition. Prefer 1–3-day subpackets; estimate from evidence and record token/tool usage.
- Source-driven parts, one compiler/authority, current permissions and complete work budgets apply to both manual and AI actions. Never create a competing AI physics implementation.
- Research only the active single-player packet; the native Godot/C# choice is settled. Existing multiplayer tests may remain, but do not commission new network work, investigations or capacity research before M7.
- Use separate task branches/worktrees across both computers. Integrate published updates before starting overlapping edits; one owner merges schema/rules changes. Historical phase completion never auto-starts the next obsolete packet.
- Local save, cancellation, protection and basic security checks belong in each feature. Later integration packets qualify the combined experience, not repair intentionally omitted foundations.

## S0–S2: decisions, embodiment and the first magic

| ID / phase | Deliverable | Depends on | Acceptance evidence |
|---|---|---|---|
| **SP01 / S0** — experience and rules | First-journey storyboard; distinct player/companion identities; customization/camera options; selected-target language; save versus save-and-protect semantics; direct override, scoped grants and undo policy | Current vision and checkpoint inventory | Founder can explain hurricane, flood, castle, rideable dragon, rampage and mushroom examples without graph jargon; exact protected support/access and allowed destructive areas documented |
| **SP02 / S0** — native software baseline | Accepted Godot .NET/C# ADR, pinned engine/SDK, repeatable bootstrap/build, C# metric/profile contract and GDScript interop; retain existing rules and saves | SP01; founder decision | Build and headless interop pass; existing engine regressions pass on pinned .NET build; document supported development host and untested devices. No browser/Bevy comparison required |
| **SP03A / S1** — district source and fidelity audit | Trace the 6th/Barton Springs/Congress/MoPac polygon; inventory permitted public terrain, imagery, structures, trail and photo evidence; date/rights/coverage ledger; distinct Pfluger district package; bridge/landing/trail sample | SP01; existing map pipeline; [district study](research/15-pfluger-district-and-small-avatar.md) | No Barton overwrite; documented bounds/datum; public-source-only baseline and held-out reference views; bridge decks/underpasses separate from terrain; explicit missing close-detail evidence |
| **SP03 / S1** — inhabited art scene | Pfluger bridge/landing/trail/waterfront and urban context; real-scale geography, roots/soil, banks, paths/ground and foliage masses; editable sources and low profile | SP01, minimum SP03A sample; reuse existing assets; final engine-specific investment follows SP02 | 30 cm camera plus ordinary reference views, motion and edits reviewed by founder; clear source/interpretation labels and collision joins; no device claim from screenshots |
| **SP04 / S1** — two avatars and controls | 0.30 m player body/sprite, matched camera/collision/step/speed/reach profile and distinct customizable companion; stable identity, follow/stay/come/look/point/stop, navigation and direct-control priority | SP01, minimum SP03 scene | Player traverses representative curbs, roots, bridge decks and underpasses; camera/near-clip and foliage visibility work at small scale; companion visibly acknowledges instructions and cannot push/trap player; appearance changes preserve identity; deterministic movement/controller code runs without per-frame inference |
| **SP05 / S2** — restricted local command boundary | Game-only companion identity/grants, filtered observations, typed commands, preview/jobs, cancellation, current protection hooks and exact approval binding | SP01; existing compiler/authority/saves; SP02 transport choice | AI cannot read credentials, call raw save envelopes, change its own grant/locks or bypass manual validation; direct malicious commands, guessed handles and revoked grants fail; no remote account prerequisite |
| **SP06 / S2** — conversation with the player's AI | One real BYO AI in an isolated profile; speech and text, visible listening/planning/acting states, target references, useful clarification, latency feedback and interrupt | SP04, SP05; selected connection route from SP02 | Real provider/client intent becomes validated commands; cancellation and provider outage preserve drafts/saves; text/captions remain usable without voice; record setup/inference cost, not presumed subscription entitlement |
| **SP07 / S2** — protected transformations | Object-level lock/important state, support/access envelopes, multi-object change validation, temporary effects versus durable edits, undo/recovery and runtime rechecks | SP01, SP05 and existing local persistence | Own locked castle/fixtures survive replacement, terrain removal, forces/debris and simulated water changes; autosave does not unintentionally lock every edit; companion cannot unlock; revocation works mid-job |
| **SP08 / S2** — first embodied magic | Conversational selection and tree-to-mushroom transformation with seeded editable families, colliders, coherent ground contact and revision | SP03, SP06, SP07 | Player says a loose request, sees intended area, changes it, applies/revises/stops and reloads; protected tree remains intact; genuine semantic replacement, not a texture-only illusion |

Barton Creek content work is on hold; its map/saves/regressions remain intact. SP03A source auditing precedes detailed new-region art; SP02 may use a clearly labeled existing or synthetic scale specimen while the new data is audited. Local plot claiming belongs to SP01/SP07, without online accounts.

SP03 art/source work, SP04 controller study and SP05 contract work can proceed alongside SP02 within separate files. Do not build the same whole game in two engines. SP07 establishes reusable protection before any destructive power; anticipated flood/force cases can initially use fixtures, then must rerun against real capabilities.

## S3–S4: expressive wishes and lasting changes

| ID / phase | Deliverable | Depends on | Acceptance evidence |
|---|---|---|---|
| **SP09 / S3** — castle/place generator | Reusable walls, towers, openings, foundations and entry paths; loose-prompt composition and conversational revision; converge path/platform relationships with the general editor | SP03, SP05, SP07, SP08 | Walkable castle/tower with coherent joins on source terrain; parts remain editable; protect it and revise only after explicit unlock; different prompts produce meaningful structural variation |
| **SP10 / S3** — rideable companion morph | Persistent AI identity across dragon transformation; body/animation, mounting/dismounting, collision, steer/goal modes, glide/flight and safe recovery | SP04, SP05, SP07, SP06 | Player really rides, steers or delegates, interrupts, lands and dismounts; permission/identity unchanged; low profile preserves collision, feedback and rider safety |
| **SP11 / S3** — hurricane capability | Reusable bounded moving/rotating force fields, atmospheric treatment, vegetation response and opt-in debris/breakable test props | SP03, SP05, SP07, SP06 | Visible storm exerts measured gameplay forces; direct/indirect damage cannot reach locked fixtures or supports; cancellation and total budgets hold across child effects; no scientific-weather claim |
| **SP12 / S3** — river/flood capability | Bounded channel water level/extent and at least one gameplay consequence (buoyancy or altered traversability); coherent banks and wet materials | SP03, SP05, SP07, SP06 | Water interacts with terrain and permitted objects, safe route/recovery exists, support/access protection holds, state persists/expires explicitly; decorative water alone does not pass |
| **SP13 / S3** — directed behavior and rampage | Area/time/target-scoped companion goals operating on explicitly editable/breakable scenery, using common navigation/effect capabilities | SP06, SP07, SP10; SP11 capabilities where used | Loose instruction causes observable multi-step behavior; locks/indirect effects respected each tick; stop revokes queued and ongoing actions; no background/offline autonomy |
| **SP14 / S4** — reusable creation/regeneration | Consistent editing of structures, terrain relationships, plants, water and companion-created assemblies; source/style/generator pins, shared budgets and deterministic affected-join rebuilds | SP08–SP13; integrate incrementally | At least two novel combinations beyond recipes; revised parts remain individually editable; large jobs cannot bypass budgets by splitting; visual/collision revision activation stays aligned |
| **SP15 / S4** — integrated saves and reversal | Extend existing persistence/recovery for avatar identity, protection, transformations and generator pins; chosen browser/native storage adapter, export/import and clean-location restore | SP02, SP07 and incremental SP08–SP14 source contracts | Acknowledged changes survive restart; interrupted jobs have known outcomes; restore reproduces content/locks; eligible undo never destroys intervening accepted work; browser eviction/native-service failure tested |

Do not discard the local database/travel implementation to fit a new diagram. Reuse it first and measure its first-run cost. A simpler storage backend, if warranted by SP02, requires explicit migration fixtures and backup; neither hosted portals nor online account services are dependencies of SP15. Full fluid dynamics, unrestricted executable uploads and unlimited terrain/physics work remain outside the initial implementation; their absence must not be disguised with visual effects.

## S5–S6: qualify and release the single-player experience

| ID / phase | Deliverable | Depends on | Acceptance evidence |
|---|---|---|---|
| **SP16 / S5** — integrated quality and safety | Real-device frame/memory/cold-start and effect stress routes; art/low-profile review; control remapping, scalable text, captions, reduced motion; independent AI/protection attacks | Start tests at SP05; final pass SP08–SP15 | Roadmap's single-player gates pass; malicious world text, forged approvals, indirect locked-object damage, revocation, crash and work/billing floods bounded; report measured platform limits |
| **SP17 / S5** — player experience | Founder sessions followed by a small invited set of individual fresh-player sessions; full avatar/conversation/magic/ride/protect/save journey; repeat-play observations | Founder sessions begin at SP04 and SP08; final integrated study after SP16 and all S3 wish categories | Record unassisted completions, prompt-to-result alignment, revision/stop success, waits, confusion and return play; fix blockers; no agent rating substitutes for player evidence |
| **SP18 / S6** — accessible release and iteration | Reproducible native Windows package; easy setup/connect path, export/backups, second actual AI-client interoperability, source/data/asset notices and measured cost; highest-value art/capability iterations | SP17; SP02 delivery decision | Clean first run on declared native devices; two supported AI clients use same rules; no protection/save/security blockers; affordable inference/distribution and repeated creative use; explicit single-player acceptance record |

Speech with the companion is active scope. Voice chat between human players is deferred. The native AI bridge stays local and separate from multiplayer services. Packaging multiple clients must share world semantics and content contracts; unsupported phones/browsers receive an honest message rather than an untested compatibility promise.

## Original E-packet crosswalk

This table preserves identifiers and useful work without preserving obsolete dependencies. Original technical details remain in dated research/checkpoints and repository history. None of the deferred gates is marked passed by this revision.

| Original IDs | Current disposition |
|---|---|
| E01–E03 scope/contracts/stack | SP01–SP02; native Godot/C# selected; remote transport deferred |
| E04–E08A map/movement/art/structure | SP03–SP04, SP08–SP14; preserve existing packages, controllers, source assets and joins |
| E09–E10 remote identity/replication | Deferred to MP01; not dependencies of local permissions, saves, AI or art |
| E11–E14 compiler/editor/permissions/loop | Reuse local implementation; extend SP05–SP14; E13 local work drops E09, E14 solo loop drops E10 |
| E15–E16 state/permissions | Preserve local database evidence; SP07/SP15 add protected source and companion state; real accounts/parcels deferred |
| E17–E20 lifecycle/travel/faults/recovery | Keep working local journeys, fencing, exports/restore; SP15 reuses safety; hosted workers, queues, online invitations and off-host service guarantees move to MP03 |
| E21–E26 gateway/preview/MCP/inference/security | SP05–SP08/SP16: local authority and bounded local previews first; no E09, hosted-worker or federation prerequisite; BYO companion central rather than optional |
| E27–E28 performance/accessibility | SP03/SP16; target single-player effects and controls, not combined multiplayer host load |
| E29 community/moderation | Multiplayer portions deferred MP02; basic solo tester privacy/consent included in SP17 |
| E30 release | SP18 for single-player; no E31 remote-session dependency |
| E31 remote sessions | Deferred MP04 |
| E32 costs | SP02/SP06/SP18 for actual solo distribution/inference; host-population costs deferred |
| E33–E35 research/acceptance | SP17/SP18 individual playtests and new single-player record; old shared-MVP criteria remain deferred |
| E36–E39 payments/business | Deferred; no billing implementation before demonstrated value and separately approved service scope |
| E40 more Earth/authority cells | Second region after single-player evidence; authority cells belong to multiplayer expansion |
| E41 new physics/scales | Relevant bounded weather/water/transformation capabilities brought forward SP10–SP13; ant-to-continent scaling remains later |
| E42 AI avatars | Brought forward SP04/SP06/SP10/SP13; no longer depends on a multiplayer release; unattended agents still later |
| E43 federation | Last within MP05, after independently reviewed credential/transfer boundaries |
| E44 platforms/services | Native Godot/C# selected in SP02; browser evaluation deferred; remaining ports, VR and commercial services remain evidence-driven |

## M7: multiplayer last — inactive until founder go decision

Entry requires SP18 acceptance and a founder decision to spend effort on shared play. Retain current code/tests, but do not start these packets automatically. All multiplayer work remains at the end of this execution plan.

| ID | Deferred scope | Dependencies and evidence |
|---|---|---|
| **MP01** | Authenticated remote authority, gameplay transport, replication/prediction and filtered observations | SP18 + go decision; two real remote clients use the completed companion/magic loop; forged/replayed inputs, loss/jitter and session replacement tests |
| **MP02** | Shared locked creations, per-player consent, indirect-effect permissions, report/block/moderation | MP01; adversarial storms/floods/rampages cannot harm protected work, exploit physics chains or leak private observations |
| **MP03** | Hosted persistence, worker admission, invitations, safe travel, backups/off-host restore | MP01–MP02; extend preserved local receipts/fencing; real identities, process faults and destination loss recover safely |
| **MP04** | Invited multiplayer trials, population limits, operation/support and cost qualification | MP01–MP03; measured supported population, real network sessions, restore drill and affordable hosting; no implied infinite concurrency |
| **MP05** | Wider shared Earth, independent worlds and eventual federation | MP04 plus separately reviewed permissions/compatibility/budget; one canonical shared state, scoped visitor credentials, safe return and conformance; no exported AI/provider keys |
