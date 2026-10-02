# EnFractal world and creation vision

**Product direction, clarified 2 October 2026.** This is the intended player experience, look, and behavior of the finished world. The [MVP roadmap](roadmap/ROADMAP.md) defines staged proofs of this direction. **Single-player comes first; multiplayer comes last, after the art, AI interaction, and gameplay are convincing. No multiplayer networking work belongs in the current development phase; a minimal AI connection bridge may still be needed.** Earlier [visual style options](roadmap/research/03-visual-style-options.md) remain useful comparisons, but their faceted-atlas recommendation is superseded by this direction. The founder has selected **native Godot .NET with C#** for current development. Browser delivery and alternative-engine experiments are deferred. See the [accepted decision](engine/decisions/0001-native-godot-csharp.md); the [platform study](roadmap/research/14-single-player-platform-and-engine.md) remains background.

> A geographically grounded, painterly 3D world where the player and their embodied AI companion can turn spoken or typed wishes into extraordinary, physically meaningful changes. The AI is the world's magic.

The result should feel warm and visually coherent while remaining an original EnFractal world. Tiny Glade is a reference for warmth and for construction that responds gracefully to broad player intentions. Its medieval subject matter and exact appearance are not targets. Real-world geography retains its dimensions while the initial player avatar is 30 cm (0.30 m) tall. A player should recognize the place from this small-inhabitant viewpoint and familiar overview views, then be free to make it extraordinary: summon a hurricane, turn their companion into a rideable dragon, raise a castle, flood a river, or transform trees into giant mushrooms. Exploration, reshaping, and invention are parts of this experience, not its complete definition. Cozy presentation can coexist with player-chosen spectacle, disruption, and discovery.

The founder's later impressionistic 3D examples refine this direction: layered brushlike foliage, distinct tree silhouettes, selective surface marks, warm light against cooler depth, and coherent natural/built materials. The [Barton Creek painterly reference brief](engine/phase1/painterly-reference-brief.md) translates those examples into local vegetation, limestone, modern structures, low-graphics fallbacks, and paired walking-height review criteria. It is an art target, not a claim that the current prototype meets it.

## First place and scale

The active first district is Austin's Pfluger Pedestrian Bridge / Lady Bird Lake waterfront: **6th Street north, Barton Springs Road south, Congress Avenue east, MoPac west**. Begin with the bridge/landing and a short adjoining route before extending detail across that named district. Trace and verify the boundary polygon during source preparation. Barton Creek remains preserved as an earlier prototype and regression fixture, with new content work there on hold.

Favor well-known, well-documented locations reconstructed primarily from permitted public geodata and photographs. The founder can verify fidelity locally and supply targeted gap-filling photos, but the pipeline must demonstrate what it can reconstruct without a bespoke full photo survey. Record source date, uncertainty and procedural interpretation. The initial 30 cm player profile needs matching camera, movement, collision, navigation and near-ground art; shrinking the camera alone is insufficient. Claims begin as small local creative plots, with multiplayer ownership deferred. See the [district/scale study](roadmap/research/15-pfluger-district-and-small-avatar.md).

All newly authored world dimensions, movement/physics values and player-facing measurements use metric units. Runtime coordinates remain meters; source-native units are retained only as provenance and explicitly converted on import.

## The experience to preserve

- **Grounded place:** The geography, street pattern, terrain, and important landmarks make a region recognizable. Evidence-backed features and invented detail stay distinguishable in the authoring record.
- **Warm exploration:** Forms are readable at walking height; lighting has gentle transitions and appealing shadow color; nearby materials feel tactile. The environment invites lingering and play, not merely an overhead screenshot.
- **Local identity:** The starting place preserves contemporary architecture, landforms, materials, and vegetation. A modern office remains modern until deliberately changed; a desert and a rainforest retain distinct starting identities. An intentional fantasy transformation is welcome and remains recorded as a player change. One art language can accommodate both without making every region pastoral.
- **Expressive agency:** Loose wishes can produce substantial, surprising changes. The system should compose supported capabilities into new outcomes, not restrict the imagination to a catalog of named presets. New forms connect convincingly to terrain, structures, bodies, and each other.
- **An embodied partnership:** The player has a personalized sprite/avatar, and their own AI has a separate, customizable embodied avatar. Both belong in the scene. Conversation, companionship, transformation, travel, and experimentation are central play, not a chat box attached to a building editor.
- **Consistent consequences:** World rules govern both player and AI actions, including indirect physical effects and protected creations. Single-player establishes these rules locally first. Graphics settings cannot change physical outcomes or hide essential warnings. Eventual multiplayer must preserve the same consistency, but it is a later project.

## The player and their magic

The player moves and acts through their own avatar, speaks or types a wish, and sees their companion understand and act in the world. Requests need not arrive as precise construction specifications. The AI connects intention to the scene, resolves ordinary details, and asks for clarification when ambiguity materially changes the result. An appropriate preview or brief explanation makes a large destructive change understandable; routine reversible actions should remain fluid. The player can interrupt, revise, or undo supported changes and choose what to preserve.

The AI uses a **dedicated, game-only bring-your-own-AI profile**. Its world identity, knowledge, and allowed actions are scoped to the game. A player's separate assistant permissions, files, accounts, or spending privileges are not inherited. MCP or another adapter may connect that profile to the world, but the transport is an implementation choice; the desired experience is a present, capable companion. The world runtime enforces a shared baseline of physical rules, protection, and resource limits regardless of which model interprets the request.

A transformation changes embodiment without erasing identity. If the companion becomes a dragon, it remains the same companion with the same action grants and restrictions. The player can mount its supported body, travel with it, or ask it to act. A request to rampage may affect unprotected scenery through the supported physical systems; becoming larger or changing species cannot reset protection checks. Player transformations follow the same principle. Both avatars retain their own identity and agency rather than becoming interchangeable editor cursors.

Manual controls remain valuable for precise edits, accessibility, recovery, and provider outages. They use the same world operations and keep the local world usable. They do **not** establish that the central AI experience has been delivered: acceptance must include a real companion interpreting loose wishes and producing meaningful in-world results.

## Rules that support expressive freedom

The baseline should limit computational cost and define consistent consequences while allowing broad creative composition. Bounds on affected area, active bodies, simulation steps, generation time, or sustained effects keep a wish affordable. Those limits should be visible and adjustable within the supported device budget. A bounded hurricane with real force is a useful early capability; an arbitrary ban on weather because the first editor only places platforms would mistake prototype scope for product scope.

Physical meaning matters. Wind should exert supported forces; a rideable creature needs a body, attachment, movement, and actions; a flood must eventually change water extent and affect objects. Effects may use simplified game physics, provided they behave consistently and their limits are clear. Visual spectacle alone must not be described as a completed physical capability.

**Saving and locking are separate actions.** Saving records a creation or world revision so it can be restored. Locking protects it from changes and damaging effects. A saved object remains alterable unless it is also locked. Offer a clearly labeled “Save and protect” (lock) action, including for creations marked important, while ordinary autosave records revisions without locking every object. Never make the protection state ambiguous. Protection covers indirect consequences such as wind-driven debris, dragon impacts, flood forces, and changes to supporting terrain, not only direct edit commands. The player can deliberately unlock a creation; transformation, reload, or an AI request cannot silently remove its lock.

For large effects, the system should explain which protected objects constrain the result and preserve the player's intention within those constraints where possible. Revision history and recovery support experimentation, but do not replace protection. The eventual multiplayer design can add ownership and consent across players after this single-player behavior works.

## Three layers of a place

**1. Geographic foundation.** Versioned source elevation, mapped streets and buildings, waterways, photographs, and authored corrections establish the starting arrangement. Record source, rights, resolution, confidence, and date for each important claim. A plausible undocumented courtyard is an interpretation, not a reconstructed fact. Geography is a starting condition: player changes may replace a parking lot, raise a castle, or transform a woodland without rewriting the source evidence. A region can remain recognizable around a deliberate transformation; preserving the source does not require freezing the playable world.

**2. Structured world.** Represent significant features as identifiable, editable objects and relationships rather than one fused scene or one flattened image. Terrain patches, paths, terraces, walls, roofs, openings, windows, trees, watercourses, creatures, avatars, and inventions need stable identities, local transforms, material roles, interaction geometry, protection state, and revisions. Physical effects also need explicit parameters, affected regions, lifetimes, and supported interactions. A photo-derived mesh may supply shape and texture, but a useful building still needs a structured roof, façade, entrances, and editable parts. Unusual geometry and fantasy forms are welcome when they meet the same contracts.

**3. Visual interpretation.** Style recipes turn the structured world into visible meshes, materials, lighting, vegetation, and distant representations. They may vary by biome, era, district, and graphics profile while sharing EnFractal's visual grammar. Style is reapplied when the world changes; it is not a one-time filter baked over the first map. Source geometry, semantic objects, player edits, and disposable render derivatives remain separately versioned.

The intended flow is:

```text
Geographic and photographic evidence
    → structured, editable place
    → player intention, expressed by speech, text, or direct action
    → companion interpretation and composed world operations
    → protection, physics, and resource validation
    → coherent details, physical consequences, and revisable world state
    → style-consistent, scalable rendering
```

## What “grounded painterly 3D” means

Painterly means selective detail, controlled variation, and intentional color. It does not require visible brushstrokes, a watercolor screen filter, low-poly faceting, or photorealistic texture noise.

| Aspect | Art rule |
|---|---|
| Shape | Keep natural proportions and recognizable silhouettes in the geographic baseline. Give transformed forms deliberate anatomy, scale, and readable silhouettes. Concentrate detail at entrances, ledges, joins, and landmarks that help people understand the space. |
| Material | Build reusable families for local stone, soil, plaster, wood, glass, metal, pavement, foliage, and water. Preserve tactile differences with controlled color/roughness variation and restrained texture detail. |
| Light | Favor soft transitions, readable shadow color, atmospheric depth, and a stable sense of time and place. Avoid making costly dynamic lighting a requirement for the baseline look. |
| Vegetation | Use sculpted masses, local species cues, modest movement, and selected close detail. Repetition and identical geometric blobs should not define a forest. |
| Architecture | Preserve type and context in the baseline; make deliberate new structures belong materially in their surroundings. A summoned castle and a modern mall can share an art language without becoming the same building type. |
| Scale and camera | Judge scenes at walking height, during movement and close inspection, as well as from aerial views. Doors, paths, steps, windows, and terrain transitions must read at believable scale. |
| Gameplay readability | Protection, hazards, force boundaries, companion actions, and interactable parts remain visible on every graphics profile without relying on color alone. |

The style contract should specify a small set of material roles and palettes, shape and edge rules, light ranges, vegetation families, effect language, and low-detail fallbacks. It must also allow regional parameters and deliberate landmark exceptions. Blender source assets, geographic inputs, photographs, generator versions, seeds, style settings, and manual corrections should remain available so the result can be regenerated and revised. Exported meshes and textures are runtime derivatives, not the only editable source.

The immediate art proving ground is a Pfluger Bridge landing/trail and waterfront scene with recognizable urban context, judged from the 30 cm avatar viewpoint and ordinary reference heights. Tree roots, soil, limestone, banks, paths, terrain, and foliage masses must form convincing relationships at walking height. These are the production lessons to carry into a dragon's feet meeting the ground, giant mushroom stems emerging from forest soil, or a castle meeting a rocky bank. This small place tests the art pipeline; it does not define the limits of the game's imagination.

## Transformation and construction that maintain coherence

The design principle is: **the player supplies the intention; the system handles the details that make the result coherent.** A path meeting a terrace should gain a suitable step or ramp. A wall opening should acquire a lintel and frame. A stream meeting terrain should form a plausible bank. Trees becoming mushrooms should retain meaningful placement and contact while acquiring the new form's structure. A companion becoming a dragon should gain supported anatomy, a rideable body, and actions rather than merely a larger decorative mesh. These are examples of generators, physical capabilities, and relationship handling; the first playable proof implements a selected subset honestly.

Each accepted edit should preserve:

- the source intent and editable parameters, rather than only a final fused mesh;
- stable identities and relationships for affected parts, plus generator and style versions;
- the distinction between source-backed geometry, authored interpretation, and player change;
- validated placement, protection, interaction/collision geometry, visual detail levels, and resource cost;
- an inspectable proposed result where appropriate and a revision that can be saved, reopened, and modified;
- avatar identity and existing locks across transformation, save, and reload.

The companion and direct controls use the **same validated world operations**: inspect, propose, transform, construct, act, preview, commit, and revise. AI can compose supported components, behaviors, and effects into outcomes the designer did not individually name. It does not gain an alternate route around protection, physics, or resource limits. A visual contract belongs beside the physics contract: coherent material families, extensible generators, render complexity, fallback representations, and clear error messages. Automated checks can enforce bounds and compatibility; human art review remains necessary for grace, charm, and local appropriateness. AI-assisted critique may help, but it is not final authority.

For example, “Turn this parking lot into a terraced garden with a café, a stream, and a greenhouse; keep the surrounding buildings recognizable” combines terrain, structures, water, and joins. “Become a dragon, let me ride you, and knock down that unprotected tower” combines identity-preserving transformation, mounting, movement, targeting, and physical effects. The system resolves those relationships, shows the important consequences, and commits only supported changes. Later requests operate on the resulting parts and state. These are **product-direction examples**, not claims that the current prototype can already perform them.

New forms and interactions should be possible through reviewed additions to the component, generator, and capability libraries. A novel idea can reveal a missing capability; the response should explain the current limit and preserve the wish for future extension, rather than redefine the product around today's presets. Extensions receive tests for appearance, editability, persistence, physics, protection, and cost. General expressive freedom grows through composable systems; it does not require arbitrary code execution or a separate hand-written special case for every wish.

## Performance is part of the art direction

Stylization does not make a scene automatically cheap. Shadows, transparent foliage, animation, unique materials, dense geometry, and many active physical bodies can overwhelm the target laptop or an integrated-graphics device. Preserve the look with shared assets, bounded materials/effects, instancing, simpler distant geometry, local detail, and measured streaming. Budget active simulation separately from visual decoration. A distant proxy may omit decorative leaves; it may not conceal a protected boundary or change collision behavior. Low settings should feel intentionally composed rather than broken. Measure the chosen native client’s rendering, simulation and AI-connection limits on actual target devices; language choice alone proves no performance or art result.

## Staged proof

1. **Honest baseline:** The [Barton Creek map](../maps/barton_creek/README.md) proves a repeatable source-to-viewer path. The [manual invention checkpoint](engine/checkpoints/manual-invention.md) implements a local editor/compiler/capability loop, and [ART-0–3](engine/checkpoints/art0-3.md) adds authored trees, painted materials, and a playable composition. Their recorded art scores remain below the target. These fixtures do not establish an embodied AI companion, broad wish fulfillment, hurricane simulation, a rideable dragon, or flooding. Existing authority experiments are prior technical evidence, not authorization to continue networking now.
2. **Single-player place and partnership:** Complete a convincing Pfluger landing/trail/waterfront slice, a 30 cm player movement/camera profile, personalized player avatar, and separate customizable companion. Connect the dedicated game-only AI profile and prove a spoken or typed loose wish becoming a meaningful world change through the actual runtime. Include revision, interruption, clear save/lock behavior, and a usable manual recovery path. Judge the AI interaction itself, not only the editor beneath it.
3. **Single-player expressive systems:** Deliver several distinct wish families using composable capabilities. A bounded hurricane experiment needs wind forces and selected movable debris; a dragon experiment needs transformation, mounting, movement, and at least one world action; construction or vegetation transformation needs editable structure and regenerated contacts. Test that existing locks withstand direct and indirect effects and that avatar transformations retain identity. The scope and physical fidelity of each experiment must be stated; no single early slice promises every example at once.
4. **Richer consequences and a complete solo loop:** Extend only after the preceding interactions feel good and fit the target device. A controlled flood can first prove changing water extent and coherent banks; buoyancy and coupled object interactions follow as explicit physical milestones. Generalized fluids, erosion, arbitrary destruction, and unlimited dynamic bodies are not implied. Build and test persistence, restoration, protected creations, expressive variation, and repeated solo play alongside improving the art. Source geography remains recoverable throughout.
5. **Expansion, then multiplayer:** Grow the accepted art and generator vocabulary to additional places and richer wishes. Multiplayer is a final deferred phase after the single-player experience works. Only then revisit networking, shared authority, cross-player consent, and world travel as new engineering and product gates. None is a prerequisite for validating the current solo game.

An art review should compare the unchanged place and at least two later changes from walking and aerial cameras. Reviewers should be able to identify the baseline region and building types, distinguish deliberate fantasy from source evidence, reopen a changed part, and see that paths, openings, terrain, bodies, and materials still meet convincingly. Gameplay review must separately test the embodied companion, wish interpretation, meaningful physical consequences, protection, and the player's ability to intervene. Performance review must include the lowest intended graphics profile on named hardware. Passing machine checks or completing a manual edit cannot by itself establish that this place is inviting or that its AI magic is enjoyable; the founder and players must judge those experiences directly.
