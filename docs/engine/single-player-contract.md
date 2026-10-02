# Single-player experience and rules contract

Accepted product decisions, 2 October 2026. Implementation progress belongs in the [roadmap](../roadmap/ROADMAP.md) and phase checkpoints; this contract is not evidence that a feature already works.

## First journey

The player enters the Pfluger Bridge landing as a 0.30 m inhabitant beside a separately named, colored and embodied companion. Initial avatars use editable original geometry; richer customization can follow without changing identity. The first camera studies are shoulder view and close inspection. Camera choice never changes body dimensions or geographic scale. Coordinates, distances and physics use meters and other metric units.

The landing and a 150 m adjoining route are the first fidelity sample within the 6th Street / Barton Springs Road / Congress Avenue / MoPac district. Public source geometry establishes the place; inferred bridge elevations, surface details and vegetation must carry uncertainty. Preserve the Barton package and its saves under their original IDs.

1. Move, look, jump, recover, change appearance and name the companion. A brief control card explains stopping and recovery before asking the player to connect AI.
2. Ask the companion to follow, wait, come here or look at a selected object. Its visible state distinguishes waiting for AI from moving through the world. Direct movement and Stop take priority.
3. Select a nearby editable grove and ask for mushrooms. Preview shows the selected area, affected objects and excluded locked objects. Revise the area, approve the exact change, then inspect its editable source and undo it.
4. Ask for a castle with a usable entrance. Review foundations and access, revise it, then choose **Save and protect**. Autosave alone does not imply protection.
5. Ask the same companion to become a rideable dragon. Mount, steer or request a destination, cancel that goal, land and dismount safely. Appearance/morph changes retain identity and delegated limits.
6. Try a bounded hurricane, flood or rampage in the editable experiment area. Visuals accompany a supported physical consequence. The protected castle, its support and its declared entrance remain usable. Stop interrupts both the behavior and its outstanding effects.
7. Exit and return. Identities, source, protection, map/style/generator pins and eligible history survive. Export a checkpoint and restore it to a separate location.

Each step needs actual play evidence. A manual fixture does not certify conversational AI, a screenshot does not certify collision, and an agent rating does not certify enjoyment.

## Targeting and changes

“Here” resolves to a visible selection owned by the game, with a world ID and revision. “Those trees” yields permitted semantic object handles, never arbitrary scene paths. Ambiguous or unsupported intent produces a focused explanation; it does not silently expand the selected area. Compound wishes are bounded jobs with shared total budgets, cancellation and previews where needed.

Durable changes carry editable source, generator/style versions and deterministic seeds. A proposal is separate from approval. A direct player approval binds its exact content, selected targets, base revision, world and expiry. Stale proposals require revalidation; a model's claim of approval is never sufficient. Actions with a current small reversible allowance can proceed without a separate dialog for every component.

Undo restores an eligible prior revision only if it still satisfies current protections and does not overwrite unrelated work. Interrupted activation must leave either the old or the new geometry and collision together. Temporary effects expire explicitly; they do not become durable geometry just because a save occurred.

## Protection and freedom

Save persists state. Save and protect additionally locks specified objects, their support region and a declared access corridor. Unlock is a direct player action outside the AI's tools. The experiment plot is the area where bounded destructive play is allowed; being outside a locked object is not sufficient authorization to change any place.

Every mutation and later effect tick checks current world bounds, grants, locks, support and access. A hurricane can bend foliage and move approved debris, a flood can change traversability or buoyancy, and a rampage can break opted-in scenery. None may undermine a locked structure, push debris through it, trap its occupant or flood its required entrance. Preview exclusions and visible deflection communicate this boundary. Cosmetic rain may remain visible.

This is bounded game physics: forces, collision, permitted transformations and documented magic sources. No universal fluid/weather/structural solver is promised. Low graphics quality changes decoration, not physics or protection. The existing invention compiler remains the validator for its supported source; new capabilities need a versioned, tested extension before admission.

## Companion boundary

The player's dedicated game-only AI profile receives scoped observations and typed game commands. It cannot access shell, arbitrary files or URLs, personal mail, developer credentials, database credentials or raw save envelopes. Object text and imported content are untrusted observations. The game applies the rules even when a model follows malicious text.

Player identity, companion identity and developer identity are separate. The native bridge does not reuse this coding task's permissions. A supported real client must be selected and paired before S2 can pass; a second real client is required for S6 interoperability. Speech needs a tested speech path plus text/caption fallback. Multiplayer accounts and transport remain deferred to M7.
