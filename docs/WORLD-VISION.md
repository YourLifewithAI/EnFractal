# EnFractal world and creation vision

**Product direction, confirmed 1 October 2026.** This is the intended look, feel, and construction behavior of the finished world. The [MVP roadmap](roadmap/ROADMAP.md) defines the smaller first playable proof. Earlier [visual style options](roadmap/research/03-visual-style-options.md) remain useful comparisons, but their faceted-atlas recommendation is superseded by this direction. Godot is the current prototype engine; the visual goal is an asset and system contract, not the name of an engine or rendering technique.

> A geographically grounded, cozy 3D world with natural proportions, softly sculpted forms, painterly materials, and gentle lighting—fully explorable and structurally editable.

The result should feel warm and visually coherent while remaining an original EnFractal world. Tiny Glade is a reference for warmth and for construction that responds gracefully to broad player edits. Its medieval subject matter, miniature presentation, and exact appearance are not targets. The player should be able to walk through a place at believable human-scale distances, recognize its real-world character, and change it through meaningful parts that remain editable.

The founder's later impressionistic 3D examples refine this direction: layered brushlike foliage, distinct tree silhouettes, selective surface marks, warm light against cooler depth, and coherent natural/built materials. The [Barton Creek painterly reference brief](engine/phase1/painterly-reference-brief.md) translates those examples into local vegetation, limestone, modern structures, low-graphics fallbacks, and paired walking-height review criteria. It is an art target, not a claim that the current prototype meets it.

## The experience to preserve

- **Grounded place:** The geography, street pattern, terrain, and important landmarks make a region recognizable. Evidence-backed features and invented detail stay distinguishable in the authoring record.
- **Warm exploration:** Forms are readable at walking height; lighting has gentle transitions and appealing shadow color; nearby materials feel tactile. The environment invites lingering and play, not merely an overhead screenshot.
- **Local identity:** A modern office building remains a modern office building. A desert, rainforest, industrial district, and coastal village retain different landforms, materials, vegetation, and architecture. One art language unifies presentation without making every place pastoral.
- **Creative agency:** Players can make substantial changes where permitted. Their creations connect to the ground, surrounding structures, and each other in ways that look intentional. The same place can evolve without losing its identity or becoming a pile of unrelated models.
- **Shared consequences:** Edits are inspectable, permissioned, persistent, and experienced consistently by other players. Visual simplification on a slow computer cannot change world authority, interaction geometry, or essential warnings.

## Three layers of a place

**1. Geographic foundation.** Versioned source elevation, mapped streets and buildings, waterways, photographs, and authored corrections establish the starting arrangement. Record source, rights, resolution, confidence, and date for each important claim. A plausible undocumented courtyard is an interpretation, not a reconstructed fact. Geography is a starting condition: authorized player changes may replace a parking lot or reshape a permitted plot without rewriting the source evidence.

**2. Structured world.** Represent significant features as identifiable, editable objects and relationships rather than one fused scene or one flattened image. Terrain patches, paths, terraces, walls, roofs, openings, windows, trees, watercourses, and inventions need stable identities, local transforms, material roles, interaction geometry, permissions, and revisions. A photo-derived mesh may supply shape and texture, but a useful building still needs a structured roof, façade, entrances, and editable parts. Unusual geometry is welcome when it can meet the same contracts.

**3. Visual interpretation.** Style recipes turn the structured world into visible meshes, materials, lighting, vegetation, and distant representations. They may vary by biome, era, district, and graphics profile while sharing EnFractal's visual grammar. Style is reapplied when the world changes; it is not a one-time filter baked over the first map. Source geometry, semantic objects, player edits, and disposable render derivatives remain separately versioned.

The intended flow is:

```text
Geographic and photographic evidence
    → structured, editable place
    → permitted player or AI edit
    → rule-based details and joins
    → validation and persistent world change
    → style-consistent, scalable rendering
```

## What “grounded painterly 3D” means

Painterly means selective detail, controlled variation, and intentional color. It does not require visible brushstrokes, a watercolor screen filter, low-poly faceting, or photorealistic texture noise.

| Aspect | Art rule |
|---|---|
| Shape | Keep natural proportions and recognizable silhouettes. Soften edges where appropriate; concentrate detail at entrances, ledges, joins, and landmarks that help people understand the space. |
| Material | Build reusable families for local stone, soil, plaster, wood, glass, metal, pavement, foliage, and water. Preserve tactile differences with controlled color/roughness variation and restrained texture detail. |
| Light | Favor soft transitions, readable shadow color, atmospheric depth, and a stable sense of time and place. Avoid making costly dynamic lighting a requirement for the baseline look. |
| Vegetation | Use sculpted masses, local species cues, modest movement, and selected close detail. Repetition and identical geometric blobs should not define a forest. |
| Architecture | Preserve type and context. Softness comes from material treatment, edge detail, color, and lighting, not turning every building into a cottage. |
| Scale and camera | Judge scenes at walking height, during movement and close inspection, as well as from aerial views. Doors, paths, steps, windows, and terrain transitions must read at believable scale. |
| Gameplay readability | Protection, hazards, force boundaries, ownership, portal state, and interactable parts remain visible on every graphics profile without relying on color alone. |

The style contract should specify a small set of material roles and palettes, shape and edge rules, light ranges, vegetation families, effect language, and low-detail fallbacks. It must also allow regional parameters and deliberate landmark exceptions. Blender source assets, geographic inputs, photographs, generator versions, seeds, style settings, and manual corrections should remain available so the result can be regenerated and revised. Exported meshes and textures are runtime derivatives, not the only editable source.

## Construction that maintains coherence

The design principle is: **the player supplies the intention; the system handles the details that make the result coherent.** A path meeting a terrace should gain a suitable step or ramp. A wall opening should acquire a lintel, frame, and readable boundary. A stream meeting terrain should form a plausible bank. A greenhouse joined to a café should have an intentional connection and compatible materials. These are examples of rule-based generators and relationship handling, not a promise that the first MVP supports every edit.

Each accepted edit should preserve:

- the source intent and editable parameters, rather than only a final fused mesh;
- stable identities and relationships for affected parts, plus generator and style versions;
- the distinction between source-backed geometry, authored interpretation, and player change;
- validated placement, permissions, interaction/collision geometry, visual detail levels, and resource cost;
- an inspectable preview and a durable revision that can be reopened and modified.

The manual editor and an AI assistant use the **same approved operations**: inspect, propose, edit, validate, preview, and publish. AI may arrange supported components and propose parameters, but it does not gain an alternate route around permissions, physics, material rules, or resource limits. A visual contract belongs beside the physics contract: approved material families, supported generators, render complexity, fallback representations, and clear error messages. Automated checks can enforce bounds and compatibility; human art review remains necessary for grace, charm, and local appropriateness. AI-assisted critique may help, but it is not final authority.

For example, a player could eventually request: “Turn this parking lot into a terraced garden with a café, a stream, and a greenhouse. Keep the surrounding buildings recognizable.” The system would first identify the lot and its permissions, propose editable terrain and building parts, resolve paths/steps/water/joins, preserve the neighboring landmarks, show a costed preview, and publish only a validated change. Later edits to the greenhouse or café would operate on those parts. This is a **long-term capability example**, beyond the first MVP's bounded construction vocabulary and terrain-edit permissions.

New construction types should be possible through reviewed additions to the component and generator library. A novel player idea can reveal a missing family; it should not require arbitrary player-supplied code or shaders to become viable. Approved extensions receive tests for appearance, editability, persistence, physics, permissions, and cost before general use.

## Performance is part of the art direction

Stylization does not make a scene automatically cheap. Shadows, transparent foliage, animation, unique materials, and dense geometry can still overwhelm the target laptop or an integrated-graphics device. Preserve the look with shared assets, bounded materials/effects, instancing, simpler distant geometry, local detail, and measured streaming. A distant proxy may omit decorative leaves; it may not conceal a portal boundary or change an authoritative collider. Low settings should feel intentionally composed rather than broken.

## Staged proof

1. **Current prototype:** The [Barton Creek map](../maps/barton_creek/README.md) proves a repeatable source-to-viewer path. The [Greenbelt style study](greenbelt-style-study.md) compares three material and lighting treatments on identical geometry. It is a diagnostic study, not the finished art direction; the sparse illustrative trees and rocks do not yet meet this vision.
2. **Next art and structure slice:** Choose a small Greenbelt view and build a walking-height reference scene using natural proportions, authored vegetation and rock silhouettes, locally appropriate materials, and an explicit style recipe. Preserve original and derived assets. Demonstrate one small, permissioned edit whose join details regenerate without flattening the whole scene.
3. **MVP creation proof:** In a contained authorized plot, a person can place, revise, save, reopen, and share a useful structure through the manual editor; optional AI proposes the same kind of edit through the same validator. Test changed geometry, joins, materials, readability, and performance with another player present. More ambitious terrain and building transformations wait for their own permission and persistence gates.
4. **Long-term product:** Expand the semantic world and generator library region by region so existing environments and many player-made changes remain coherent, locally distinct, structurally editable, and affordable to render.

An art review should compare the unchanged place and at least two later edits from walking and aerial cameras. Reviewers should be able to identify the region and building type, trace which details are evidence versus interpretation, reopen an edited part, and see that paths, openings, terrain, and materials still meet cleanly. Performance review must include the lowest intended graphics profile. Passing machine checks does not by itself establish that the place feels inviting; the founder and players must judge that directly.
