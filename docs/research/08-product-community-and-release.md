# Product, community, accessibility, and release plan

> **Current direction — 2 October 2026:** The [revised roadmap](../history/ROADMAP.md) and [backlog](../history/BACKLOG.md) govern scope and order: single-player embodied AI first, multiplayer last. The first product is now individual play with two avatars, spoken/typed wishes and embodied AI magic. Manual controls are a fallback, not a substitute for AI acceptance. Paired multiplayer cohorts, community operations and human-to-human voice chat are deferred. The dated research below is retained for context.

Planning proposal, September 30, 2026. This document adds the teams needed to turn the engineering prototype into a worthwhile small game. No competitor playtests, user interviews, deployments, or launches have been performed. Scope assumes one founder with coding agents, a total hosting/AI allowance below $100 monthly, and the limits in persistent Earth and portals: eight simultaneous people overall, four per sandbox, and one active sandbox service-wide.

## Product hypothesis and the first fifteen minutes

**Home Earth must provide a satisfying activity before a player uses AI or enters a portal.** Its initial promise is: become an expressive creature, learn how wind and movement interact, contribute something visible to a shared place, and return to find it there. The globe gives geographic context; one deliberately interesting coastal region provides the playable experience. A sparse representation of the rest of Earth is an overview, not finished playable geography.

Use one compact **ridge-and-garden circuit**, built from the physics team's walking, jumping, gliding, bounded wind, primitive props, and attachments. The community garden is a protected visual landmark, not a new farming, weather, or ecosystem simulation. Nearby authorized workshop bays contain the dynamic experiments.

| Time | Player experience | Product evidence |
|---|---|---|
| 0–2 minutes | Choose one of three creature templates; set text size and camera comfort; walk to a visible wind marker | Can start without AI, code, purchases, or a lengthy account tutorial |
| 2–5 minutes | Follow a gentle route, jump, and glide down a low ridge; instant safe recovery after a fall | Movement is enjoyable and understandable on modest hardware |
| 5–9 minutes | Enter an opt-in workshop; rotate a capped wind device to carry an approved lightweight object toward a target | Supported primitives produce a visible, comprehensible consequence |
| 9–12 minutes | Adjust one parameter or swap one part; optionally help a consenting friend glide to a ledge | Invention is accessible through ordinary controls; cooperation adds value |
| 12–15 minutes | Save a named blueprint and place an approved exhibit on the player's allotted display bench; locate it in the shared atlas | Persistence has a personal and social purpose |

Provide a fully solo version; presence of another player must enrich the activity rather than block completion. A first-time player can use a preset wind device without constructing anything. A second visit presents a different route or constrained design challenge using the same components. Avoid daily streaks, artificial scarcity, trading, grind, procedural quest systems, and offline plant growth in this MVP.

Only after this loop works introduce optional AI: “Make my glider turn more gently” or “Suggest a wind device that lifts this approved prop.” A manual template/parameter editor remains equally capable. The first sandbox visit compares normal and quarter-strength gravity in the same region, then returns to a saved exhibit. This teaches the difference between changing a design and changing a world's supported rules.

## Onboarding, discovery, and explanatory UI

Keep one visible next action, with a skip/replay option and no requirement to finish a tutorial before exploring. Explain practical limits where they matter: “This workshop has room for four moving parts” is more useful than a CPU budget label. Show saved/pending status, current world, key rule changes, permission state, and a reliable recovery/Return Home control.

The atlas initially shows the starting district, friends who consent to visible presence, bookmarked exhibits, and invited destinations. Do not rank an almost-empty globe by purchasable land or bury the start behind coordinates. Mark unopened geography clearly. Offer names and descriptions alongside icons; require no knowledge of latitude/longitude to find one's own bench.

A creator can inspect a blueprint's supported parts, duplicate it when its reuse permission permits, change one parameter, preview the result, and publish a new revision. Explain why a request fails and how to fix it. AI refusals and server validation errors must not erase the manual route. The creator sees when a copied design has attribution or reuse conditions.

## Research before expanding the feature list

Conduct a small structured comparison of **Resonite, Boundless, and Wish Master**. This is a study plan, not a report of completed use. Resonite documents collaborative creation and ProtoFlux; Roblox describes schema-based functional generation and reports Wish Master examples. Boundless's public feature page contains both present-tense descriptions and explicit future-feature caveats, so hands-on notes must record the actual build, date, account tier, and availability. [Resonite features](https://resonite.com/features), [Roblox generation announcement](https://about.roblox.com/newsroom/2026/02/accelerating-creation-powered-roblox-cube-foundation-model), [Boundless feature page](https://playboundless.com/).

Allocate approximately 90 minutes per product, plus one shared synthesis hour. Test the same questions where supported: enter with a friend; make or adapt an odd creature or useful contraption; use it together; save; leave; return; understand who can modify it. Measure time to first interesting action, failed attempts, help required, persistence clarity, invitation friction, and ability to inspect/reuse another person's work. Record unsupported tasks instead of forcing equivalence. Do not infer their server costs or scalability from a client session.

Agents prepare observation sheets and summarize permitted notes; the founder performs the interaction and judges feel. Avoid introducing every observed feature into scope. The output is three specific changes to our onboarding or invention workflow, with a reason and a cost.

## First ten testers and useful measurements

Recruit ten consenting adult volunteers for an initial private cohort: a mix of builders, explorers/social players, and people unfamiliar with creation tools. Seek at least two people on integrated graphics and several with differing input, vision, reading, or motion preferences, without claiming a representative accessibility study. Use five paired appointments so the eight-person total limit is respected. Establish service windows and meaningful opportunities to return before inviting them.

Observe the first fifteen minutes without coaching except when someone is stuck, uncomfortable, or unsafe; label every intervention. Reserve another fifteen minutes for unstructured play and a short interview. Ask what they would return to do, what surprised them, and what they believe the limits mean. Avoid “Would you pay?” as a substitute for evidence that they want to use it again.

Minimal event telemetry: consented pseudonymous tester ID, build ID, hardware class, launch/start/completion timestamps, first save, save reload, invitation attempt/outcome, safe return, voluntary session, crash, and blocking error category. Store structured events rather than raw prompt/chat content by default. Define retention and deletion with the privacy work before collection; research recording requires a separate explicit choice.

| Metric | Numerator and denominator | Proposed decision use |
|---|---|---|
| Installation/start | Testers reaching the controllable starting area / all ten invited testers | Report failed installs and nonattendance separately; do not erase friction |
| Core-loop completion | Testers completing movement, manual edit, and durable save without intervention / all testers who started | Initial goal: at least 7 of 10 invitees complete; show both counts |
| Persistence understanding | Testers who independently find their saved exhibit on a later session / those attempting that task | Every data-loss case blocks expansion regardless of percentage |
| Voluntary seven-day return | Testers starting an unprompted second meaningful session within seven days / starters whose seven-day window has elapsed | Initial hypothesis: at least 4 of the original 10 return; no market-fit claim |
| Cooperation | Pairs completing the opt-in lift or shared contraption challenge / pairs attempting it | Diagnose consent and shared-state confusion |
| Support burden | Founder support/moderation minutes / active tester, with total weekly hours | Reduce scope or invite volume when operations displace development |

Count a meaningful return as at least ten minutes or completion of an exploration/creation action; exclude developer accounts, automated bots, and scheduled test appointments from voluntary retention. Also report “returned after a reminder” separately. Disclose outages and unavailable session slots alongside both invited-cohort and eligible-cohort counts. With ten testers, one person changes a percentage dramatically; retain reasons and verbatim consented feedback rather than treating thresholds as statistical proof.

## Community rules and anti-grief product controls

Publish a short conduct policy covering harassment, impersonation, unwanted contact, offensive creations, repeated disruptive effects, and evading a block. Technical legality does not make conduct acceptable. Public starting structures are protected; avatars cannot shove one another by default; friendly wind interactions require revocable consent. Workshops have visible boundaries and containment, following the physics policy rather than promising arbitrary cross-parcel interaction.

Start with preset emotes and a small moderated, rate-limited text channel for the invited adult cohort. No voice chat, direct-message system, public marketplace, real-money items, or arbitrary image/audio/model uploads in the initial release. The text channel is optional and can be muted without losing instructions. Curated template names and short user labels still require content handling.

Block immediately hides a person's messages and invitations and disables future directed interactions where applicable. Explain that a block does not remove all shared-world objects or silently create a different authoritative Earth. Offer mute, report, return to a safe area, and leave-session controls without requiring confrontation. Reports include the relevant world, entity, session, and action IDs plus optional user text; they should be possible after the other person disconnects.

The founder owns reports, sanctions, appeals, and exceptional restoration. Agents may organize evidence and draft responses, but should not autonomously adjudicate serious conduct disputes. Provide separate moderator actions for quarantining a creation, suspending an invitation/world, and removing a participant. Keep an appeal contact and decision record. Do not promise around-the-clock monitoring; publish staffed session windows and suspend admissions when urgent incidents cannot be handled.

## Accessibility acceptance

Make the entire start/settings/tutorial/creation/portal/exit flow operable with a keyboard, with remappable actions, visible focus, current-binding prompts, alternatives to dragging, and toggles for sustained input. Offer an assisted glide/camera mode and no required rapid repeated inputs. Multiplayer simulation continues while menus are open, so a menu cannot promise to pause everybody; provide a safe protected menu state or return point. [Microsoft input guidance](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/107).

Provide scalable text and UI, high-contrast panels, text explanations for icons, captions for important sounds, and shape/label cues alongside color. Default to restrained camera movement; provide reduced motion, disable camera shake and decorative flashes, and allow field-of-view/sensitivity adjustment. Never remove authoritative effect warnings when reducing decoration. [Microsoft text guidance](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/101), [motion guidance](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/117).

Evaluate screen-reader support in the chosen engine during the UI spike; document actual supported flows and remaining barriers. Do not market complete accessibility based on a checklist. Test settings before first movement, 200% UI scaling, keyboard-only construction, muted audio, reduced motion, and low graphics using the same core loop. Accessible text and controls are foundational; full gamepad coverage and localization expansion can follow measured need.

## QA, packaging, rollback, and support

Distribute one versioned Windows release archive containing a playable build, instructions, license notices, known issues, support contact, and checksum manifest. Use an authenticated invite/download path where appropriate. Avoid building a custom launcher/store/updater initially. Account for distribution egress within the hosting budget. Confirm signing/distribution requirements and cost before a public launch; never teach users to disable security software. Godot supports command-line release export with presets, if selected. [Godot export documentation](https://docs.godotengine.org/en/stable/tutorials/export/exporting_projects.html).

Pin engine/export templates, dependencies, asset hashes, build commands, and protocol/rules/schema versions. Require a clean-machine rebuild producing functionally equivalent release artifacts; claim bit-identical reproducibility only after measuring it. Keep the last known good release and deployment manifest. Reject incompatible clients with a plain explanation and download link. Save data lives separately from the application directory so replacing a build preserves local settings.

Before each cohort release run the minimal end-to-end journey: fresh install; non-AI play; two-player consent; durable save; reconnect; sandbox invitation; changed-gravity preview; return; load saved exhibit; report/block; exit. Combine this with the performance, portal fault, persistence restore, and creation validation suites owned elsewhere. Test one fresh Windows user environment and the lowest supported graphics profile; the developer's editor session is insufficient.

Stage migrations on a restored copy before touching live data. Deployment gates include current backup verification, capacity check, version compatibility, and a documented rollback or forward-repair path. A client crash or gameplay regression can use an older compatible build; a database migration may not be reversible. Do not automatically restore an old database and discard new acknowledged player work to make a rollback appear easy.

Have one incident template: symptoms, impact, build, detection time, mitigation, affected saves, recovery result, follow-up. Add a bounded diagnostic export the player can inspect; omit secrets and unrelated local files. For critical data-loss or abusive-interaction defects, close affected features or admissions first. The founder decides reopening after the relevant fixture passes.

## Work packages, capacity, and public-release gate

| Package | Dependency | Reviewable completion evidence |
|---|---|---|
| PRODUCT-01 core loop | Primitive vocabulary and protected region | Fifteen-minute script, solo/pair variants, explicit exclusions |
| UX-01 onboarding/access | PRODUCT-01 and client UI spike | Keyboard/manual/no-AI route, comfort settings, failure explanations |
| RESEARCH-01 precedents | Available access and founder time | Dated observation notes with three justified design changes |
| COMMUNITY-01 operations | Identity, permissions, action IDs | Conduct/report/block/appeal flow demonstrated with two test accounts |
| QA-01 cohort protocol | Playable vertical slice | Ten-person recruitment plan, event dictionary, denominator definitions |
| RELEASE-01 distribution | Pinned stack, save/version contract | Clean install/rebuild, prior-version recovery, support runbook |

Agents can draft flows, fixtures, analytics definitions, packaging scripts, and runbooks concurrently after interfaces settle. The founder must review interactions, conduct live sessions, resolve art/product choices, and own incidents. Reserve approximately 8–12 human hours for the first ten interviews/observations and synthesis. During a small alpha, budget 4–6 hours weekly for scheduled hosting, reports, support, backups, and review; development time is additional. If that capacity is unavailable, reduce cohort size and service windows rather than assuming agents replace human community work.

Expand invitations only after the loop is understandable, saved work survives recovery, low-end performance passes, blocking/reporting works, operational load fits the founder's time, and the budget retains headroom. Before a public audience or billing, schedule jurisdiction-specific legal/privacy review of minimum age, consent, moderation responsibilities, data retention/deletion, geography/asset rights, subscription terms, and consumer obligations. This is scheduled professional review work, not a legal determination that an adult-only label resolves every requirement. Start the research cohort as invited adults; select the eventual audience and age policy before marketing or open registration.

The first release earns expansion through repeat voluntary use and reliable operations. Global geography coverage, autonomous AI residents, open federation, commerce, voice, and broad uploads remain later workstreams rather than prerequisites for learning whether this small shared Earth is worth inhabiting.
