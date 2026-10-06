# Visual direction, asset pipeline, and art decision plan

**Dated option survey, prepared September 30, 2026.** Its original style recommendation below is superseded by the founder's [grounded painterly 3D world vision](../history/WORLD-VISION.md), confirmed October 1. The Barton Creek Greenbelt study has since produced three material/lighting captures, but no finished environment art or low-hardware benchmark. Performance figures below remain proposed acceptance targets, not observed minimum-device results. This workstream assumes one developer with coding agents, a Windows native first release, an 8 GiB integrated-graphics baseline, and an additional test on the observed RTX 2070 Super laptop with 8 GiB VRAM confirmed by the coordinating agent through `nvidia-smi`.

## Recommendation and decision to make

Original survey recommendation: prototype **an illustrated Earth with faceted landscapes and softly shaded creature avatars**. That was a useful low-cost comparator, not the current end-product goal. The chosen direction keeps the lessons about coherent silhouettes and bounded lighting but calls for natural proportions, softly sculpted forms, painterly materials and local identity at walking height. Give elevation, rivers, streets, buildings and biome boundaries geographic credibility while allowing inhabitants and inventions to be fantastical.

The original shortlist was **A, faceted atlas**; **B, soft toon**; and **C, painterly atlas**. The first Greenbelt comparison explored material palettes, not finished versions of those three pipelines. Future tests should evaluate how to achieve the confirmed grounded painterly direction on the same authored, editable scene and measured devices. Do not implement twelve complete art pipelines.

Art style does **not** require a particular gameplay language. Meshes and textures are data; scripting governs asset construction, animation, and presentation; shaders govern appearance. Godot documents toon, unshaded, vertex-lighting, and custom spatial shading paths. Unity exposes HLSL/ShaderLab and graphical shader workflows. An attractive style can be implemented in either engine; it cannot by itself settle the engine decision. [Godot spatial shaders](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/spatial_shader.html), [Unity shader authoring](https://docs.unity3d.com/Manual/shader-writing.html).

## Twelve concrete options

All ratings are **relative engineering judgments, unmeasured**, assuming comparable visible content and a competent implementation. L/M/H mean low/medium/high. VRAM covers resident graphics assets; fill covers pixels, overdraw, shading, and extra passes; CPU covers rendering preparation, animation, and asset changes, excluding authoritative physics. Authoring is human effort and pipeline complexity. A low rating does not mean unlimited objects are safe.

Implementation paths used below:

- **G:** Godot, with GDScript or C# gameplay/tools and Godot Shading Language where custom shading is needed.
- **U:** Unity, with C# gameplay/tools and Shader Graph or HLSL/ShaderLab where needed.
- **Native:** optional C++ extension or worker for measured meshing bottlenecks; not a blanket requirement.
- **Asset:** Blender or another modeling tool, with glTF/GLB interchange. Custom shaders require engine-side recreation; glTF is not a universal shader source format.

| Direction | VRAM | Fill | CPU | Authoring | Creative flexibility | Viable path |
|---|---|---|---|---|---|---|
| 1. Faceted illustrated Earth | L | L | L–M | L | High for procedural terrain and modular inventions | G or U; vertex-color materials |
| 2. Soft toon creatures and scenery | L–M | M | M | M | High; expressive arbitrary silhouettes | G toon/custom shader or U toon shader |
| 3. Hand-painted storybook | M | L–M | M | H | High visually; painting every new asset is costly | G/U simple lit shader; Asset painting |
| 4. Clay miniatures | M | M | M | M–H | High for assembled organic creatures | G/U rough opaque materials; Asset sculpt/retopology |
| 5. Papercraft and folded terrain | L–M | L–M | M | M | Moderate; broad shapes work, folds constrain detail | G/U opaque meshes; minimal custom shader |
| 6. Block voxel world | L | L–M | M–H | L–M | Very high for grid building; organic silhouettes limited | G/U chunk meshing; Native if justified |
| 7. Smooth sculptable voxel world | M | M | H | H | Very high for caves and organic edits | G/U mesh extraction; Native likely useful |
| 8. Retro textured 3D/pixel presentation | L | L | M | M | High for objects; small-scale detail can become illegible | G/U low-resolution render target, simple shader |
| 9. Sprite creatures in 3D terrain | L–M | M–H | L–M | M–H | Moderate; arbitrary poses/viewpoints need more sprites | G/U billboard/atlas shader; Asset sprite sheets |
| 10. Living topographic atlas | L–M | M | L–M | M | High for geography, moderate for embodied play | G/U terrain contour shader plus simple meshes |
| 11. Restrained realistic PBR | H | H | M–H | H | Very high visual range; difficult coherence | G/U standard PBR; Asset textures/normal maps |
| 12. Procedural implicit creatures | L–M | H | M–H | H | High within procedural vocabulary | G/U bounded raymarch shader, or baked meshes |

**1. Faceted illustrated Earth.** Triangular mountain planes, solid-color tree crowns, rounded low-poly creatures, and clean architectural kits. Advantages: material reuse, easy recoloring, readable geography, and rapid procedural variations. Disadvantages: repetitive terrain becomes conspicuous; poor silhouette design can look unfinished. It is the strongest solo-developer foundation. Separate geography precision from visible polygon density; a recognizable coastline need not have every beach pebble.

**2. Soft toon.** Broad light bands, restrained rim highlights, expressive faces, and occasional silhouette outlines. Advantages: strange creatures feel intentional, animation reads well, and terrain can share the palette. Disadvantages: outlines introduce extra geometry or screen work; hard light bands can flicker as terrain changes detail. Begin with outline-free shading; reserve outlines for selection and accessibility. Do not require a renderer-specific normal buffer for the baseline.

**3. Hand-painted storybook.** Painted color variation and brush-shaped detail on simple meshes. Advantages: warmth and a distinctive handmade identity without photorealistic lighting. Disadvantages: UV work, texture memory, painting skill, and inconsistent player assets. Use reusable trim sheets and small tileable patterns; keep brush detail subordinate to collision-relevant edges. A shared paper-grain accent is more achievable than individually painting the whole planet.

**4. Clay miniatures.** Rounded forms, earthy matte materials, sculpted eyes and mouth shapes, and deliberately simplified anatomy. Advantages: assembled creatures have a coherent material vocabulary; terrain can feel tactile. Disadvantages: close views expose poor topology, and realistic clay lighting can quietly become expensive. Approximate softness with normals and colors rather than required subsurface scattering. This is a useful creature treatment even if the landscape stays faceted.

**5. Papercraft.** Folded ridgelines, layered cardboard trees, cutout facial features, and printed diagrams on inventions. Advantages: a strong recognizable identity and low texture requirements. Disadvantages: thin parts and open seams fail from unusual viewpoints; flying behind scenery reveals shortcuts. Prefer closed opaque geometry; avoid forests of overlapping translucent cards. World-scale terrain must retain real elevations rather than literally becoming a tabletop diorama.

**6. Block voxels.** Earth elevation and buildings become a consistent grid with a tight palette. Advantages: intuitive construction, understandable edits, easy kit generation. Disadvantages: grid storage, remeshing, seams, and small-scale fidelity remain hard; voxels are not automatically fast. Draw merged visible surfaces rather than one node or cube per occupied cell. This direction changes the terrain/edit architecture and needs the map team's explicit agreement.

**7. Smooth sculptable voxels.** Rounded terrain and caves formed from a volume, with blended surface materials. Advantages: deformable organic worlds and fewer visual restrictions than blocks. Disadvantages: mesh extraction, collision rebuilding, volume persistence, and boundary handling expand MVP scope substantially. Use only in a bounded experimental sandbox initially, if selected. Rendering a smooth planet does not require storing its whole interior as voxels.

**8. Retro textured 3D.** Low-resolution textures, limited palettes, crisp silhouettes, and an optional coarse internal render resolution. Advantages: modest texture footprint and a deliberate nostalgic identity. Disadvantages: distant landmarks and ant-scale objects vanish; artificial vertex wobble and dithering can impair comfort. Keep interface text native-resolution, make pixelation optional, and avoid reducing physical precision to imitate historical hardware.

**9. Sprite/3D hybrid.** Illustrated creatures and distant vegetation face the camera within a geometric world. Advantages: economical distant crowds and beautifully authored character poses. Disadvantages: transparent layers consume fill rate, aerial views reveal flatness, and custom creatures require many animation/view combinations. Use this principally for distant impostors, decorations, or stylized companions; do not rely on it for every freely rotating player avatar.

**10. Living topographic atlas.** Elevation bands, contour lines, understated roads, geographic labels, and sculptural creatures. Advantages: emphasizes that this is Earth, supports wayfinding, and transitions naturally to the globe view. Disadvantages: contours can shimmer, labels clutter, and immersive encounters may feel diagrammatic. Fade contours by screen scale, prioritize labels, and offer the treatment as an atlas mode or distant-terrain layer. Preserve understandable walking surfaces nearby.

**11. Restrained realism.** Physically based stone, soil, wood, foliage, and believable creature surfaces. Advantages: immediate recognition of real places and broad existing asset availability. Disadvantages: texture residency, shadows, vegetation overdraw, and mismatched purchased assets create production and hardware risk. It could suit later private worlds, but it is the weakest baseline for this developer/budget combination. No ray tracing, volumetric clouds, or dynamic global illumination should be mandatory.

**12. Procedural implicit creatures.** Smooth mathematical shapes merge into fantastical bodies; controlled surface patterns respond to parameters. Advantages: unusually flexible variation and compact descriptors. Disadvantages: raymarching cost grows with screen coverage and step count; collision, animation, and consistent detail reduction need separate solutions. Prefer offline or bounded background conversion into conventional meshes for shared Earth. Continuous full-screen implicit rendering is an experiment, not the baseline pipeline.

## Shared identity and creative freedom

Define a one-page style contract with material roles, region-specific palettes, minimum feature thickness, consistent eye/face readability, and recognizable symbols for force, protection, ownership, and portal state. A desert, rainforest, industrial district and coastal village need different local colors, vegetation and architecture under a common visual grammar. Modern buildings retain their type; warmth does not require a miniature or medieval treatment. Palette choices remain user-configurable within the supported families. Shapes, motion, labels, and sound must reinforce important meanings; color alone is insufficient.

Appearance and capability have different contracts. Wings may be decorative; flight is a runtime capability. A huge visual aura must not imply a wider authoritative force radius. A harmless outline cannot conceal a damaging effect. Terrain detail settings cannot remove the visual indication of protected ground, portal boundaries, or nearby relevant creatures. Players may disable flashing, camera shake, bloom, motion blur, and decorative particles without losing gameplay information.

For MVP, creations assemble approved meshes, rig sockets, materials, decals, and bounded effects. AI proposes parameter values and supported component combinations; it does not submit arbitrary shader programs. Asset publication produces an immutable content hash, bounds, triangle/vertex counts, material count, texture dimensions, bone count, animation count, dependencies, license metadata, and low-detail fallback. Enforce both per-object and per-scene limits; a thousand individually legal objects still exceed a scene budget.

Allow more appearance variation in private sandboxes while retaining client safety limits. Destination validation can remap a material or substitute an approved proxy, with a visible preview before travel. Preserve the original design rather than destructively normalizing its source. Eventually, opt-in custom imports can pass conversion, rights review, content checks, and performance validation before publication; that is beyond the first primitive-composition release.

## Asset pipeline and provisional envelopes

Use editable source assets and an automated, reproducible export path. glTF is an appropriate neutral asset interchange candidate; pin supported features and run round-trip checks for skeletons, transforms, colors, and scale rather than assuming every exporter agrees. [Khronos glTF](https://www.khronos.org/gltf/).

Initial **diagnostic envelopes**, coordinated with rendering research:

| Item | Low-profile starting envelope | Adjustment rule |
|---|---|---|
| Whole visible frame | 250,000 triangles; 350 draw calls, counting terrain and extra/shadow passes | Profile bottlenecks before raising either |
| Nearby creature | 3,000–8,000 triangles; at most 32 bones and 2 materials | Keep silhouette; simplify anatomy before animation quality |
| Creature detail chain | Roughly 100% / 40% / 10% of source geometry, then proxy | Choose transitions by screen size, not fixed universal distances |
| Common prop | 100–2,000 triangles, normally one material | Merge or instance repeated static content per spatial cell |
| Textures | 512-pixel common; 1,024-pixel exceptional hero atlas | Avoid unique texture sets for every component |
| Fully animated nearby avatars | Initially 4; reduced update cadence/proxies farther away | Keep interaction cues and authoritative state current |
| Low client memory | Attributable CPU plus shared-GPU allocation envelope ≤2.5 GiB | Measure iGPU shared-memory accounting without double counting |
| Discrete client memory | Private working set ≤4 GiB and GPU allocation ≤2 GiB | Keep substantial headroom within the confirmed 8 GiB GPU |

These are starting hypotheses, not capacity guarantees. Skinning, texture bandwidth, state changes, and transparency can dominate despite low polygon counts. Limit blended effect coverage and overlapping layers; use opaque geometry where practical. Reuse material families instead of generating a unique shader for every invention. These choices follow the bottlenecks described in [Godot GPU optimization](https://docs.godotengine.org/en/stable/tutorials/performance/gpu_optimization.html).

Repeated scenery should be instanced in small geographic batches so culling remains effective. Godot's documented MultiMesh approach trades per-instance culling for efficient grouped drawing; verify details against the pinned release because that documentation carries an update warning. [MultiMesh documentation](https://docs.godotengine.org/en/stable/tutorials/performance/using_multimesh.html). Use mesh detail reduction plus separate distant representations; neither should change collision authority. [Mesh LOD](https://docs.godotengine.org/en/stable/tutorials/3d/mesh_lod.html), [visibility ranges](https://docs.godotengine.org/en/stable/tutorials/3d/visibility_ranges.html).

Start with a dozen reusable landscape pieces, three creature bodies with interchangeable parts, eight invention components, one portal frame, and three biome palettes. Agents can prepare procedural variations and validators; the developer approves silhouettes, color, and readability. Expanding the asset catalogue before this kit works is unnecessary expense.

## Benchmark scene, agent tasks, and acceptance

The Barton Creek package is now the reference geography. Its existing fixed Greenbelt camera and three palette captures are a start, but the actual benchmark still needs a walking-height scene with authored forest, water, rocks, a small workshop, protected garden, portal, four nearby animated creatures, twelve distant proxies, and a modular wind contraption. Add a stress variant with dense vegetation and overlapping effects. Use the same camera route, geography, mesh counts, animations, and lighting across compared treatments. The selected grounded painterly direction is the target; alternatives are diagnostic fallbacks and cost comparisons.

| Agent-sized task | Dependencies | Reviewable output / completion check |
|---|---|---|
| ART-01: style specification | Product vocabulary and geographic sample | Palette, silhouette rules, readable static comparisons of A/B/C |
| ART-02: modular starter kit | ART-01 and agreed sockets/rig limits | Source assets, exports, detail chains, license manifests |
| ART-03: shader variants | Renderer bake-off and shared mesh kit | Equivalent baseline/optional treatments; no missing materials |
| ART-04: asset validator | Creation schema and ART-02 export contract | Clear rejection or downgrade for oversized, unsupported assets |
| ART-05: visual benchmark | ART-02/03 and performance harness | Reproducible release-build captures, timings, memory and issue list |
| ART-06: readability review | ART-05 plus actual volunteer playtests | Findings on silhouettes, interaction cues, settings and accessibility |

Parallelize ART-02 and ART-03 after ART-01 freezes shared interfaces; ART-04 can proceed independently against fixtures. Run ART-05 after integration, then ART-06. Assign one integrator; agents should not independently change the same palette, rig, import settings, or benchmark camera. Initial planning estimate: 5–10 focused developer days including integration/review, with substantial uncertainty around asset polish; this is not six concurrent full-time teams.

Measure release builds after warmup, repeated camera routes, and a 30-minute thermal run. Record hardware, power profile, resolution, renderer, build hash, scene hash, CPU/GPU timing, p50/p95/p99 frame times, peak memory, upload stalls, and shader compilation hitches. Treat cold start separately. Proposed gates: 720p iGPU p95 ≤33.3 ms/p99 ≤50 ms; 1080p RTX laptop p95 ≤16.7 ms/p99 ≤25 ms. Also offer a 30 fps laptop cap. Actual integrated-graphics hardware must be tested; the RTX result cannot certify it.

Reject a style if meeting those gates destroys the useful view of the world. Ask five initial testers to identify a protected plot, active force boundary, portal destination status, and creature facing direction in the low profile; investigate every failure. Verify reconnection or rapid travel cannot retain unlimited unloaded graphics assets. Fail safely to approved proxies when an asset is missing or over budget, preserving interaction cues.

Compatibility is a candidate baseline, not a performance conclusion. Godot documents different renderer capabilities, including limitations on compute and normal-buffer access; the selected style must survive those limitations or the engine team must explicitly change the baseline. [Renderer comparison](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html).

## Procurement, cost, dependencies, and unresolved decisions

Allocate **$0 recurring art-service spend** for the initial alpha plan; use existing tools, original modular assets, and selectively adapted permissive assets. Any optional one-time asset purchase must fit an explicit development allowance, separate from the under-$100 monthly hosting/AI ceiling. Generated variants should be cached; generating art whenever a visitor enters would add avoidable cost and inconsistency.

Kenney states its asset-page assets are CC0; Poly Haven publishes its assets under CC0 and distinguishes them from protected website content. These are sources to evaluate, not assurances that any particular downloaded bundle is suitable. Keep the included license and provenance for every selected file. Check modifications, redistributability, source inclusion, and any noncopyright rights before shipping. Open game code does not automatically make third-party assets redistributable. [Kenney asset licensing](https://kenney.nl/support), [Poly Haven licensing](https://polyhaven.com/license).

Dependencies: map team provides coordinates, terrain geometry/material classes, and detail transitions; physics defines collision and effect boundaries; creation/MCP defines supported components; persistence supplies versioned asset references; rendering defines residency and culling; portals declare destination appearance compatibility. Freeze those interfaces before asset proliferation.

Remaining decisions are user preference among the shortlist, acceptable geographic abstraction, intended creature scale range, camera freedom, and whether custom asset import is necessary before public alpha. The largest risks are shader portability, animation labor, unbounded user content, and visual detail that hides authoritative rules. The decision gate is one appealing, readable, measured scene; committing to a full global art production pipeline comes afterward.
