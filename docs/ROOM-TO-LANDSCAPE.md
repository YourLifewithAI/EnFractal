# The room becomes a landscape

**Draft, 7 October 2026.** Design input for the revised Run 2. The founder's decisions below are settled; the implementation choices are proposals. Numbers marked *start* are first guesses. Nothing here passes a look or play gate.

## The goal

A photographed room becomes a fantastical landscape whose arrangement the player recognises. The couch's long rise, the open space beside it and the shelves' height survive as geography. Its places belong together.

The [brief 12 sheet](codex/spikes/12-room-to-landscape/sheet.png) still reads as a room with lumps. Build ground, transitions, destinations and horizon together.

## The founder's rules

- The layout stays recognisable; the landscape must be believable, cohesive and fit the space.
- Everything scanned becomes landscape, including small things, unless the player explicitly preserves something during setup. Resources, movable objects, interactables and relics are populated separately afterwards. A scanned jar does not automatically become a carryable jar.
- Conversion is fixed at import. Deformation becomes possible later, after the landscape is built and play begins. Live transformation is deferred.
- Volume grounds the world, without requiring exact reconstruction or exact boxes. Fun and charm matter more than fidelity to scan noise.
- Confident kind gives character; uncertain recognition falls back to shape. Colour supports those choices rather than deciding them alone.
- Neighbours become places: chair, desk and bookshelf might together make a mountain ascent, terraced town or castle.
- Build the creation engine first. The companion's design magic comes later. Test varied rooms locally; crowdsourcing is deferred.

## What comes in

Use C3's metric shell, openings, bounds, spawns and light hints, plus **every** reviewed C4 inventory entry: stable snapshot ID, kind, posed box, bottom-centre placement, broad colours, confidence and support. Keep metres, kilograms, seconds, degrees, +Y up and -Z forward.

The [current scan](../pipeline/roomscan/README.md) provides boxes and supports, not reliable occupied solids, cavity maps or a neighbour graph. Its confidence is an evidence score, not a calibrated probability. Derive neighbours from oriented footprints, height overlap and support links; label derived relationships and inferred voids. Known free passages and player setup exceptions are hard constraints. Missing supports, contradictory overlaps and unknown cavities appear in the review, rather than silently becoming floating land.

## Build order

1. **Choose the whole-room language.** Start with one temperate, painterly landscape and one stone-and-timber settlement kit. Resolve biome, palette, landform family and setup exceptions once. Other themes follow after this works.

2. **Volume establishes geography.** Convert boxes into coarse solids, keeping dominant footprints, height order and open corridors. Broad low forms become rises or plateaux; tall narrow forms become crags; flat textiles become ground patches. Combine supported small forms into their parent landform while retaining their contributions in the data. Use authored solid primitives and local implicit blends, with subtraction for voids. A floor heightfield may cover open ground; it cannot be the sole representation for stacked shelves, caves and overhangs. [Arches](https://diglib.eg.org/items/fe20de3e-be7c-49c9-b003-f1afcf282db5) provides precedent for volumetric terrain with those features; the proposed room implementation is unverified.

3. **Kind adds character.** A couch suggests a ridge enclosing a high valley; shelves suggest terraced cliffs; a desk suggests a tableland with a hollow underneath. Below an evidence threshold (*start*: 0.7), use proportions; a reviewer may override kind. Preserve gaps when evidenced or explicitly inferred for review. Templates omit furniture and appliance details.

4. **Colour informs materials.** Pool broad colours over each place, then map them into the theme's limited palette. Blue may inform shaded stone or flowers; it does not force a lake. Blend shared soil and stone across contributors, reserving stronger accents for destinations. Scan colour is guidance; its exact RGB values need not survive.

5. **Neighbours compose places.** Connect touching/supporting objects and nearby boxes (*start*: gaps up to 0.25 m, with compatible heights), without bridging a protected passage. Limit grouping so chains of clutter do not swallow the room. A chair below a desk beside shelves becomes foothill, terrace and keep within one castle place. Select entrances, terraces and destinations, then join them with routes. Kind changes the arrangement's character; volume keeps its grounding. Places share geology, vegetation and architectural scale. Use local adjacency rules for roofs, retaining walls and vegetation; keep global route constraints separate. [WFC](https://github.com/mxgmn/WaveFunctionCollapse) supplies adjacency propagation but can encounter contradictions. Begin with a small rule library and bounded deterministic search, not a whole-room WFC solver.

6. **Make the shell ground, horizon and sky.** Extend relief, paths and vegetation over the floor, joining landforms with foothills, scree, terraces and clearings. Replace visible wall/ceiling surfaces with a landscape presentation while retaining the metric boundary data. Irregular ridgelines and foreground slopes mask straight edges; atmospheric distance and a sky background remove the visible ceiling seam. Distant scenery is view-only, beyond the playable world in appearance, without out-of-bounds entities. Doors become passes or gates; windows become views or light features. Show impassable edges as cliffs, dense growth or closed gates, so collision is understandable.

   Ceiling-as-sky does not settle lighting. The [look bible](look/LOOK-BIBLE.md) still requires light through real openings and lamps. Until the founder changes that rule, keep those sources and their occlusion, even when the enclosing surfaces are visually hidden. Lane L must test whether this produces a believable landscape. An open-sky sun is a founder decision.

7. **Populate, validate, export.** Place gameplay entities only after the terrain and routes pass their checks. Export fixed terrain assets, separate populated assets, collision and walkable-surface identities into a pinned room manifest; create initial room state through the trusted host. Navigation and scene nodes are derived. Failed validation leaves the previous playable room intact.

## Recognisable and cohesive

Keep the room's outline, landmark order, broad height relationships and important negative spaces. Merge contacts and soften corners; do not preserve a rectangular border around every contributor.

For major contributors, allow footprint dimensions and relief height to vary by 25% in either direction (*start*), and horizontal centre displacement up to 0.10 m (*start*). A transition skirt may extend another 0.20 m (*start*), within bounds and reserved free space. Thin sources may become painted ground patches or small relief; report these exceptions explicitly. Measure contributor envelopes before blending and the final place envelope afterwards. Never trade a route or spawn clearance for a prettier skirt. If constraints cannot coexist, report the conflict for review.

Join contacts with shared terraces or soil, retaining deliberate cliffs. Carry one rock language and detail scale throughout. Water needs a low basin and credible outlet or contained pond; omit it initially if crossing rules are missing. Vegetation follows slope, soil, exposure and distance from paths, with clustered spacing and density caps. This layering is informed by [Coherent Multi-Layer Landscape Synthesis](https://perso.liris.cnrs.fr/eric.galin/Articles/2017-landscape-synthesis.pdf); our rules are proposals.

Check grounding with footprint/height comparisons and a source-to-place map. Review an overview **and** the avatar's eye: connected terrain, plausible transitions, consistent palette, destinations and no dominant wall/ceiling box. The founder must recognise the arrangement and accept it as a landscape.

## Routes for a 10 cm body

Plan playable regions before meshing. Every designated region and required pickup must connect to spawn; record intentionally inaccessible scenic summits separately. Do not reclassify a failed destination as scenery to pass a check.

Use routes no steeper than 30 degrees (*start*), no narrower than 0.12 m (*start*), and overhead clearance at least 0.12 m (*start*). Verify against both actual body profiles and collision. The current companion navigation uses a 4 cm eroded radius and a 1 cm baked climb; the player steps 2 cm ([navigation](../game/scripts/native/Navigation/RoomNavigation.cs), [controller](../game/scripts/native/SmallPlayerController.cs)). Shared stair routes therefore start at risers no higher than 1 cm, or use continuous ramps. These dimensions remain unverified on generated terrain.

Ascend through switchbacks, ledges and ramps. Steep faces can suggest future climbing; required routes cannot depend on climbing, jumping, swimming or magic that the companion lacks.

Keep table undersides and shelf gaps as caves or overhangs when supported by evidence; boxes alone cannot certify them. Tag invented openings as inferred. Check entrances, interior headroom, support thickness and exits with swept capsules. Derive static collision from the same solids as the visible terrain, preserving voids instead of filling them with convex hulls. Reject bad winding, degenerate triangles, seams, spawn penetration and unsupported geometry. Use Godot movement trials as well as navigation queries; a flat summit is not evidence of access.

## Things to find, carry and use

Populate loose stones, timber pieces, simple crates, tools, markers and a few relics. Place stones near scree, timber near groves or workshops, crates on settlement terraces, and relics at readable destinations or inside reachable caves. Clearings provide staging surfaces for arranging and stacking. Vegetation and architectural dressing remain decoration unless deliberately made interactive.

Start with finite objects and existing sandbox verbs: grab, carry, place, push. Resources are carryable pieces initially; harvesting, crafting, inventories, relic powers and growth need later rules. An interactable must advertise only an operation the host implements. Include a light item either avatar can carry, a pushable obstacle and a placement surface; choose masses from host limits, not from the original furniture's mass.

Reject intersecting or unsupported placements, blocked routes and pickups beyond reach from a valid standing position. Check a return route with the carried item's envelope. Companion fetch must approach a known target, grab through host checks and return holding it; release saves its final placement. Failed routes return `target_unreachable`. Discovery still comes from the two avatars' eyes, not from the generator's full population list or an overview camera.

## Determinism and stored data

The same reviewed shell/inventory snapshot, setup choices, generator version, recipe kit and style pin produce the same baseline. A rescan or changed theme creates a new baseline.

Hash canonical resolved inputs; sort contributors and ties by stable IDs; derive independent random streams per place and layer. Pin tool/exporter versions and fix output timestamps. Timings and run timestamps belong in audit logs, outside pinned content. Test repeated builds for identical manifest, mesh, collision and population bytes; cross-version reproducibility is unverified.

Keep scan evidence and build intermediates in ignored local capture storage. Exported rooms live in `user://rooms/<room_id>/`; synthetic fixtures alone belong in Git. A hash-listed landscape specification stores resolved theme, source contributors, places, solid operations, route reservations, population rules and seed. The manifest pins that specification and all assets. Room state stores mutable entity poses, removals, protection, creations, journal and discoveries; loading never scatters again.

**Contract proposals, for the integrator:** an optional manifest reference to that strictly validated specification; an explicit asset world layer (terrain, populated, decoration); stable terrain support/surface references for placement and map levels; shell presentation separated from physical boundary/lighting. Fixed mesh collision and landscape material roles already exist in the asset vocabulary. Do not hide required gameplay semantics in ignored extensions. Update readers, validator, examples and migration tests together; existing rooms keep their meaning. No new deformation state or command is needed now.

## Test rooms

Use a synthetic corpus with truth boxes, supports, voids and expected destinations:

| Room | Pressure it adds |
|---|---|
| Garage | Large mixed forms, unknown kinds, stacked storage, wide opening |
| Bedroom | Broad low bed, narrow bedside gaps, wardrobe height |
| Kitchen | Continuous counters, under-counter voids, overhead units |
| Living room | Couch valley, table overhang, rug and connected seating |
| Office | Chair-desk-shelf place, supported small objects, competing routes |
| Cluttered | Dense groups, overlap uncertainty, protected narrow passages |
| Near-empty | Ground and horizon carry the world without invented large landmarks |

Every fixture must pass source contribution accounting, grounding tolerances, supported geometry, bounds, spawn clearance, connected required regions, pickup/return routes, byte determinism and contract validation. Add rotated boxes, uncertain kinds, missing supports and colour conflicts as variants. Invalid inputs fail with a useful reason. Founder review judges believability and recognition for **each** room; record rejection and its cause. No crowd testing is planned.

## What Run 2 builds first

1. **Corpus and planner — Codex, bounded brief in `pipeline/landscape/`.** Build synthetic inputs, contributor/place graphs, route reservations and deterministic receipts. Accept when all seven room types account for every contributor, preserve hard free-space constraints, and flag impossible cases. No GPU or contract edits.
2. **One complete terrain language — Codex geometry; Claude integrator and P for contracts, loading and collision.** Build the temperate kit, transitions, voids and terrain packages. Accept repeated bytes, validated exports, clean topology and headless movement/navigation checks across the corpus. Stage exports before publication. No isolated-object recipe milestone substitutes for this.
3. **Landscape and play gate — Claude L on GPU, P for sandbox/host, A for fetch.** Resolve lighting with the founder, render ground/sky/horizon, add bounded population and prove carry/place/push/fetch plus save/reload. Accept founder-approved eye/overview views, reachable destinations, honest refusals and the look bible's measured frame budget. Start visually with the synthetic garage, then cover the corpus; Lane C alone takes an approved build to real capture data.

Keep solid operations and contributor/surface IDs for later bounded deformation, collision/navigation rebuilds and support reconciliation. Future magic uses the shared command path, revisions, budgets and protection. The trusted adapter assigns principals; names remain untrusted; the companion receives no files, shell, URLs, credentials, saves or player-only operations. Configured approvals are host-held and player-clicked. Deformation commands and live conversion are deferred.

## Questions for the founder

1. Should landscape lighting retain real windows/lamps, or may the sky become a lighting source?
2. May some clearly scenic summits remain inaccessible, while every playable destination is reachable?
3. What first setup exception should we support: preserving a chosen real object, an area, or neither in the prototype?
