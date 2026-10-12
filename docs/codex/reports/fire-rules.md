# Fire rules — first gameplay specification

**Status: proposed behavior, 12 October 2026.** The founder's ownership rule below is confirmed; the numerical tuning and remaining behavior are proposed defaults. The first authored-part implementation now supports island combustion and restoration; see [implementation and current limits](fire-restoration.md). The finer surface-patch behavior and complete incident model below remain the target specification.

## 1. What is protected

**Player-made objects are protected by default. Generated landscape and stock buildings are not.** This is the founder's direction, not a blanket protection of the island.

- Player-made includes objects the player builds and objects the Gubble creates on the player's behalf. Their protection is established when their creation is committed, before a fire tick can affect them.
- Generated trees, grass, shrubs, scenery, stock props and stock buildings are eligible for burning. Eligibility does not make every material fuel: stone remains stone, water remains water, and a timber building can burn.
- Moving, carrying, renaming or recoloring an existing landscape/stock object does not make it player-made. A genuinely new player-built addition is protected separately; attaching it does not protect the entire stock building.
- Restyling and room regeneration must preserve ownership and protection. Neither can silently turn a player creation into combustible scenery.
- Protection blocks direct ignition, spread, scorching, fuel consumption and fire damage. A firebolt cannot bypass it, and a protected object cannot relay heat from a fire to another object.
- Existing explicit protection locks remain effective, including locks already placed on stock objects. Only the player's existing explicit unlock operation may remove protection; the Gubble cannot unlock anything. The world generator assigns no blanket fire protection to landscape or stock buildings.
- Burning scenery must not indirectly destroy a protected creation by deleting its support. Scenery may char, but removal of a supporting collider is blocked while a protected creation depends on it. This does not make the entire surrounding landscape immune to fire.

## 2. What catches fire

Fire behavior comes from validated material and ownership data, never from a display name, texture color or an AI guess at runtime. Builders/exporters assign a combustion profile to eligible surfaces or parts. Existing visual material roles are inputs to that assignment, not sufficient runtime permission to destroy an object.

The following numbers are **gameplay tuning**, not measurements of real combustion. One reference patch is approximately **0.05 m × 0.05 m**, half a player-height across. Burn time is the patch's initial fuel supply expressed as seconds of active burning.

| Exposed material/profile | Ignition exposure | Initial fuel | Burn result |
|---|---:|---:|---|
| Paper, cardboard, dry litter | 0.5 s | 6 s | Char, then consume eligible material |
| Dry grass, dry leaves | 0.8 s | 8 s | Blackened vegetation and bare/scorched ground |
| Cloth, carpet | 1.5 s | 12 s | Char, then consume eligible material |
| Dry twigs, brush | 2 s | 16 s | Char and lose eligible small branches |
| Timber, bark, wooden building parts | 4 s | 30 s | Char; consume parts authored to burn away |
| Living foliage, green grass | 5 s | 10 s | Wither and char; harder to ignite than dry vegetation |
| Leather | 6 s | 15 s | Char; consume only if authored as consumable |
| Stone, brick, concrete, ceramic, glass, metal, bare soil, sand | Never | None | No ignition or spread through the material |
| Water, underwater vegetation | Never while submerged | None | Extinguish flames in contact |
| Unknown material, unclassified plastic/rubber, decorative light, creature/avatar | Not enabled in this first version | None | No ignition; require a later explicit profile |

Paint does not turn metal into wood or make timber fireproof. Mixed objects use the profile of the exposed part that is actually hit. A stone stock house can lose its timber roof and wooden door without its stone walls becoming fuel. A fully timber stock house can burn throughout its eligible parts. Bare terrain and rock remain present beneath vegetation fires.

Unknown profiles are a content-coverage gap to fix in the builder, not a reason to exempt the island. The first content pass must classify the island's vegetation and stock buildings before combustion is advertised as working across the landscape. No fuel tanks, explosions, toxic smoke or melting materials in this version.

## 3. Ignition and heat

Each eligible patch has remaining fuel, accumulated exposure, wet time, and a burn state: **intact → warming → burning → extinguished or spent**. Scorching is a separate persistent amount; extinguishing does not erase it.

- Direct contact with a placed magical flame adds **1 exposure-second per simulation second**. Contact is determined by the host's effect volume and the surface geometry, not by visible particles.
- A firebolt's first solid hit contributes **1 exposure-second once**, then the bolt ends. Dry paper may ignite from one hit; timber needs sustained or repeated exposure. It cannot hit through an intervening surface.
- A burning neighboring patch contributes **0.35 exposure-seconds per second** when eligible under the spread rules below.
- Combined exposure is capped at **1 exposure-second per second** per patch. Duplicate/replayed impacts contribute nothing; accepted distinct impacts are also capped to one exposure-second per patch in any simulation second. Stacking emitters cannot create an unlimited heating rate.
- When no source reaches it, a non-burning patch loses exposure at **0.5 exposure-seconds per second**, down to zero.
- Ignition requires all of: an enabled combustible profile, remaining fuel, no applicable protection, no wet/cooldown state, sufficient exposure, and available simulation budget.
- Wet or protected patches do not accumulate latent exposure. Crossing a threshold and igniting is one atomic host decision, with protection checked again at that moment.
- Magical flames can remain over noncombustible stone while their spell lasts. That is a magical heat source, not burning stone. Immersion or dousing ends that source.

## 4. Burning, spread and the landscape

- An ignited patch sustains itself using fuel even after the original spell ends. Each second burning consumes one second of fuel. At zero fuel it stops producing heat and cannot reignite until new fuel is explicitly added by a supported world operation.
- Fire can spread only to an eligible surface within **0.05 m** of an actively burning patch, with an unobstructed path. Resolve surface proximity, not object-center proximity. Contact across adjacent patches is allowed; passage through walls, floors, rock or water is not.
- Every spread ignition must satisfy the full ignition and protection rules. Merely standing within a large object's bounding box cannot set all its parts alight.
- Distant embers, smoke and flickering light are visual only. They do not ignite anything. Wind can affect presentation; wind-driven spread is deferred until it has an explicit bounded rule.
- Vegetation burns in local patches, not as a single island-wide mesh. A flame near one tree does not immediately ignite a forest or every instance of that tree asset.
- An intact stock building can develop a fire through its exposed combustible parts. Heat cannot jump to a roof through an intact stone ceiling. Existing openings and actual burning connections determine the route.

Initial performance/playtest limits: **6 magical emitters**, **24 simultaneously burning patches**, **64 tracked warming patches**, and **128 patches changed by one fire incident**. Natural fire sprites must be batched separately from the six magical emitters. There is no blanket immunity or distance cutoff for landscape: a fire advances through neighboring fuel until extinguished, exhausted or limited by the incident budget.

An incident is a connected fire and all its descendants. Fires that meet merge their budgets and provenance; repeated casting into the same connected area does not reset its changed-patch count. When a limit is reached, existing fire continues consuming its finite fuel, but further warming/ignition is refused without consuming a new target's fuel or banking an instant ignition for later. The player gets a brief “Fire spread limit reached” notice. These limits are proposed tuning for the first implementation, not a claim that large forest fires are already supported.

## 5. Water, dousing and cooling

- Sustained contact with a water volume extinguishes the contacted patch or magical source on the next simulation step. Surface position and water depth matter; being near a pond does not count as immersion.
- Immersion clears exposure and keeps a patch wet. After leaving water, it remains non-ignitable for **10 simulation seconds**. Water repeatedly touching it restarts that interval. Unconsumed fuel remains.
- Dousing clears the selected flame and its exposure immediately and gives it a **3-second ignition cooldown**. A water-based douse also applies the 10-second wet interval; magical dousing alone does not imply wetness.
- The player's **Douse all** stops magical emitters and every active/warming combustion patch in the loaded island, including descendants. It does not repair damage or restore consumed fuel. Stops remain available when budgets are full or revision expectations are stale.
- The Gubble may douse its own fire incidents and descendants under the existing ownership policy. If player and Gubble incidents merge, a Gubble stop must not cancel the player's contribution without permission; mixed incidents require player dousing. The player can always stop the whole incident.
- A normal spell timeout removes only the magical source. Dousing an entire incident also removes its descendants. The UI must distinguish “spell faded” from “fire extinguished.”

## 6. Damage and lasting changes

The first combustion implementation damages **eligible landscape material and stock parts**, while player-made objects retain their protection. It does not silently introduce health or combat for the player, Gubble or animals.

- Burning increases a persistent scorch amount and reduces persistent fuel. Extinguishing leaves a partially burned patch with its remaining fuel; it may reignite once cool and dry.
- At fuel exhaustion, authored vegetation can disappear and leave a scorched patch. An explicitly consumable stock part can become its authored charred remnant or be removed through the kernel, subject to protection and support checks.
- A visual material tag never authorizes deleting a complete building. Buildings need separately identified burnable parts and a defined burnout result. Until those exist, eligible surfaces char but structural collision remains.
- Destroying structural supports, collapsing buildings, damaging carried objects, avatar injury and animal injury require separate implementation. They are deferred, not simulated by deleting arbitrary scene nodes.
- Ordinary movement must remain possible. Burned ground retains its underlying land collider; no accidental holes through the island. Optional charred debris must have explicit bounded collision and lifetime rules before it is added.

## 7. Authority, saves and journal

Manual controls and the Gubble use the same fire capabilities and rules. A visual-only spell must not silently acquire destructive powers: the capability version and UI must explicitly advertise ignition when enabled. Apply the existing host approval policy to destructive companion casts, with bounded targets/spread scope held by the host; no request carries approval. Protection and permissions are rechecked during spread, not just at cast time.

Run combustion at a fixed **0.1-second simulation step**, with bounded work and stable ordering by patch ID. The trusted simulation submits progression through the same command/kernel authority; graphics, smoke and particle systems never change world state. Each progression batch is tied to its initiating incident and principal, cannot be fabricated by the companion, and commits its fuel/scorch changes and receipt atomically. Pausing or a slow frame must not cause an unbounded catch-up burst.

Save fuel, scorch, wet/cooldown time, exposure, burn state, incident provenance and budgets as room state. Resume active combustion only after the room is fully loaded. Time spent outside the game causes **no offline burning**. Natural combustion can resume from saved state; transient magical emitters still do not reappear. Failed persistence must not publish a half-applied burn.

The journal records a useful summary per incident, such as “The dry brush near the shore burned; the wooden hut was scorched.” It may record the first observed discovery of a material's fire behavior. It does not list each heat tick, spark, patch or extinguishing animation, and the Gubble cannot learn unseen object identities from fire results or journal summaries.

## 8. Acceptance examples

1. A new player-built wooden chair beside burning grass stays unscorched with all fuel unchanged; the Gubble-built equivalent behaves identically.
2. Dry landscape grass ignites; bare stone beneath it does not. Wet grass cannot ignite until its wet interval ends and fresh exposure reaches its threshold.
3. A stock timber hut can burn. A stock stone hut's wooden door can burn while the stone walls remain. Moving or repainting either hut does not grant automatic protection.
4. Fire reaching a protected player addition cannot ignite it or remove its supporting collider. Unrelated landscape continues burning.
5. A firebolt applies its impact once; replay cannot apply extra heat. Protected or wet targets retain no stored ignition exposure.
6. Spread stops at a solid wall, a water gap wider than the transfer distance, exhausted fuel, or the incident budget. Light and flying embers cannot bridge those barriers.
7. Repeated or simultaneous casts cannot evade incident/room budgets. The player can always douse all; a companion stop cannot cancel a player's independent or mixed incident.
8. Reload reproduces fuel and scorch, resumes saved natural burning without offline catch-up, and does not recreate transient magical spells.
9. A failed save leaves both authority and rendered burn state unchanged. A headless run and a rendered run produce the same authoritative combustion state.
10. The journal records one meaningful incident summary, without routine heat messages or undiscovered identities.

## Integration proposals

This is a specification only. No schemas, presets, pinned assets or gameplay files change with this document. Before implementation, the integrator must coordinate contract additions for combustion profiles, patch/part identities, persistent burn/incident state, destructive capability semantics, simulation progression and permission scope. Builders must emit validated profiles and stock-building parts; loaders, validator, kernel, saves, companion observations and tests must consume the same definitions. Do not hand-edit generated room files or add unsupported fields to today's contracts.

Related: [project direction](../../ROOM-SCALE-DIRECTION.md), [contracts](../../../contracts/README.md), [command-host policy](../../engine/phase3/command-host.md), [current visual fire](fire-particles.md). Document-only change: no engine/GPU run, no external service use or cost. Open for founder review: timing, spread budgets and burnout behavior; the player-made-versus-landscape protection distinction is already the founder's instruction.
