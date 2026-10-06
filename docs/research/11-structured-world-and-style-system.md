# Structured world and style system

> **Retained research, context only.** Written for the shared-Earth and Pfluger-district plans. The technical findings still apply; the sequencing, locations, scale and any reference to S0–S6, SP packets or E packets are superseded by the [room-scale direction](../ROOM-SCALE-DIRECTION.md). Links to removed files were unlinked; those files are on the `geography-era-final` branch.

> **Current direction — 2 October 2026:** The [revised roadmap](../history/ROADMAP.md) and [backlog](../history/BACKLOG.md) govern scope and order: single-player embodied AI first, multiplayer last. Retain semantic parts, provenance and coherent regeneration. Current single-player scope includes dramatic fantasy transformations and the embodied companion; earlier remote-play dependencies and tiny construction-only limits are not the current product boundary. The dated research below is retained for context.

**Planning direction, 1 October 2026.** This translates the founder's [world and creation vision](../history/WORLD-VISION.md) into interfaces and tests. It does not claim that the current Barton Creek viewer already has semantic buildings, player editing, or procedural joins. The existing map builder and three-style material study are inputs to this work.

## Required separation

1. **Evidence and base geography:** source elevation, mapped features, photographs, measurements, rights and confidence. Keep original inputs and correction history. Use real coordinates and a pinned base version.
2. **Canonical editable world:** persistent semantic objects and relationships anchored to that geography. This is the authority for what exists, who may change it, and how parts connect. Avoid storing only a fused scan or finished mesh.
3. **Derivatives:** meshes, textures, material assignments, LODs, impostors, collision and navigation. Generate them from canonical objects plus pinned generator/style versions. They can be cached or rebuilt; do not treat them as the sole durable edit record.

The existing map research already separates immutable source terrain, deterministic shaping, authoritative player changes and disposable render derivatives. The additional requirement is an explicit **semantic place layer** between source geography and derivatives. A street line may become a path object; a building footprint may become a building with separately authored roof, façade and entrance parts. The first conversion can be sparse and selective. Source data does not magically reveal every wall, window or room.

## Minimal object and relationship contract

Before several agents author generators, freeze a small example contract covering:

| Concern | Needed information |
|---|---|
| Identity and location | Stable object/part ID, `WorldId`, `FrameId`, local transform, bounds and owning region/parcel |
| Meaning | Feature type and supported operations: terrain patch, path, step, wall, opening, roof, vegetation, water edge, prop, etc. |
| Relationships | Containment, adjacency, attachment, crossing, support and path connectivity; explicit affected bounds for regeneration |
| Evidence | Source reference and rights, source-backed/estimated/authored/player-change label, confidence and correction history where useful |
| Appearance | Material role, style family and regional overrides; no claim that a raw texture is an editable semantic structure |
| Behavior | Approved interaction geometry, physics/collision shape or proxy, capability references and relevant permissions |
| Reproduction | Source version, generator ID/version, seed, editable parameters, manual overrides and output hashes |
| Change | Expected object/terrain/permission revision, actor from authenticated context, operation ID and durable receipt |

This table is a contract checklist, not a prematurely fixed JSON schema. The E02 contract packet should define exact field names, allowed references, coordinate units, version handling and errors with small real examples. Blueprints remain portable local-coordinate descriptions; placement and permissions remain world-specific. Existing AI/MCP and physics contracts must consume the same canonical object IDs and operations.

## Responsive construction

Construction generators should operate on explicit relationships and bounded neighborhoods. A request to draw a path through an authorized wall can propose an opening; moving the path should update that opening and frame. Raising an authorized roof can propose appropriate supports. A stream edge can generate banks and crossings where it meets a path. The exact details come from local style and building type, not a universal medieval kit.

Each operation must have declared preconditions, a bounded area of effect, deterministic output for pinned inputs, and a clear failure or preview when a join cannot be made safely. The system should preserve author-intent parameters and manual exceptions. Regeneration must not silently erase unrelated player work or reinterpret an older saved world under new generator code. A generator upgrade needs explicit compatibility handling or a reviewed migration with before/after preview.

Derive visual and collision changes together, then activate their agreed revisions only when both are ready. The physics workstream's terrain revision boundary remains authoritative. Nearby graphics profiles may use different detail, but all see the same accepted world parts and interaction geometry. Aggregate scene budgets apply even when each individual component passes its own limit.

## Visual contract for old and new content

The [art direction](../history/WORLD-VISION.md) is a rule system, not a post-processing filter. Define shared roles for stone, soil, foliage, pavement, plaster, wood, glass, metal and water; shape/edge conventions; light and shadow ranges; effect language; and a low-detail version of each supported component. Regional recipes then choose local palettes, vegetation and architecture. A modern mall still reads as a mall, and a dry landscape does not inherit Greenbelt greens.

The Greenbelt style JSON demonstrates editable color and light parameters, but it is **not yet this contract**. It has no structured building parts, component generators, material compatibility checks or automatic quality review. Grow the recipe only alongside a reference scene and measured hardware constraints. Blender can hold editable source meshes and procedural assets; Python can prepare geographic and photo evidence offline; Godot currently renders the scene. glTF/GLB exports and generated textures are interchange/runtime outputs, while source assets and rules remain editable.

Automatic visual validation should reject unsupported shaders/materials, excessive geometry or textures, missing low-detail substitutes, invalid bounds, unlicensed references and unreadable required gameplay cues. It may suggest a compatible material or simplified substitute. Whether an object feels graceful, cozy or locally appropriate is a human review question, first for the founder and later for test players. Do not equate passing numeric limits with an art-quality pass.

## Manual and AI editing use one path

The manual editor and optional AI assistant should submit typed intents or supported component changes to the same creation compiler and authority. A useful workflow is:

```text
Inspect authorized objects and local context
  → draft typed edit with stable targets and parameters
  → resolve affected relationships and generated details
  → validate rights, permissions, physics, style and aggregate cost
  → preview appearance and consequences
  → confirm against current revisions
  → publish one durable, idempotent change
  → regenerate bounded render/collision derivatives
```

AI may suggest arrangement and revise a draft after errors. It cannot mint a new capability, bypass permissions, import arbitrary code/shaders, or approve its own publication. The ordinary player sees the same preview and review information regardless of whether a model helped draft the change. The game retains the durable structured edit, not a private chat transcript as the only source of truth.

## Staged acceptance fixtures

**Initial structure fixture, within MVP scope:** In a contained authorized plot of the Barton Creek test region, place a path and small platform/step or shelter from approved parts. Move one part so a join must regenerate. Preview and publish through the manual editor, reload the world, then edit it again. Check stable IDs, source-versus-player labels, permissions, deterministic regeneration, revision-consistent collision, low-detail representation, and a second client's view. An AI draft can be added only through that established path. Review at walking height and on the lowest supported graphics profile.

**Later transformation fixture:** On an explicitly authorized parking-lot parcel, propose terraces, garden, café, greenhouse and short stream while retaining surrounding mapped buildings. Then change path alignment, raise part of the café and switch a material family. The parts remain editable; steps, foundations, water edges and entrances regenerate coherently; the saved revision survives reload; the low profile remains recognizable. This is the founder's long-term example, not an MVP promise or permission to modify every real-world structure.

**Art review:** Compare unchanged and edited views from walking and aerial cameras in at least two distinct environments. Review whether local geography and building type remain recognizable, materials and joins feel cohesive, invented details are identifiable as such, and essential play cues survive low settings. Record subjective findings separately from objective geometry, memory and frame-time measurements.

## Ownership and order

E02 owns the canonical object/operation contract with map, physics, art, persistence and MCP reviewers. E04/E05 supply provenance-complete geographic and photo fixtures, including source confidence. E08 owns initial style grammar, editable source assets, component families and visual fallbacks. E11 remains the single compiler/budget owner and consumes semantic/visual constraints; E12 supplies the manual edit and preview path; E15 persists source intent and revisions; E21–E25 expose the same bounded operations to AI/MCP and publication. One integrator resolves generator/schema versions. Agents may work in separate files after the contracts freeze; they should not independently invent incompatible object IDs or style semantics.
