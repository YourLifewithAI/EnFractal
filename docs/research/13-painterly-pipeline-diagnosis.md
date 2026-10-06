# Why the painterly art is not working yet

Research and implementation audit of baseline `cf46c03`, prompted by the founder's concern that repeated passes keep producing the same low-quality graphics. This is a change in production approach, not an accepted art milestone. Three independent agents examined artist workflows, the renderer implementation, and hardware-conscious techniques. No production rendering code was changed by this research packet.

## Finding

We have repeatedly refined a blockout instead of producing a finished environment-art pipeline. The brief already called for authored trees, painted material information and composed ground cover, but the implementation kept using closed canopy lobes, simple stems, solid colors and scattered small primitives. Changing their palette and lighting cannot supply the missing forms, silhouettes and material structure.

There is also a confirmed face-orientation defect in the reference scene's terrain. That must be corrected before judging lighting. It is a prerequisite, not an explanation for the entire quality gap: a diagnostic correction restores illumination but leaves the primitive assets plainly visible.

The current captures do not meet the supplied references. Previous scores around 5/10 were subjective internal assessments, not evidence that the art is halfway to the goal. Successful collision, persistence and geometry-budget checks do not raise visual quality.

## What the code and captures show

| Area | Current evidence | Why it falls short | Required change |
|---|---|---|---|
| Tree architecture | Workshop dressing, `_make_trees`: straight seven-sided cylinders and a few closed crown lobes | Repeated poles and balloons lack the branching, gaps and uneven outline of an oak | Author tapering, curving branch forks and intentional canopy groups; prove the silhouette from multiple angles |
| Foliage | `_crown_mesh`: a 10-column, 4-row shell plus 16/32 opaque marks | Surface decorations cannot create depth or sky gaps inside a solid crown | Compare painted, tightly fitted leaf-cluster cards against properly shaped opaque clusters on the same branch structure |
| Materials | Workshop and reference-scene material factories use albedo colors, roughness and vertex colors without painted texture inputs | Bark, rock, soil and leaves have too little distinct surface information | Original reusable painted textures, meaningful UVs, controlled roughness and self-occlusion; broad marks following form |
| Ground and water | Large plain terrain regions, small scatter marks and narrow color ribbons | Disconnected props and abrupt edges; little sense of soil, rock strata or creek banks | A continuous terrain material treatment plus designed banks, ledges and clustered ground-cover patches |
| Rendering correctness | Terrain face orientation and supplied normals disagree; two-sided rendering masks the visibility symptom | Lighting is evaluated on the wrong side of affected surfaces | Repair orientation and normal conventions, then rebalance lighting and material colors |
| Low profile | Workshop disables shadows and fog outright | Removes depth and grounding along with cost | Reduce shadow distance/resolution, distant detail and tiny-object shadow casting first; preserve major forms and contacts |
| Production order | Repeated full-scene procedural passes before one convincing tree/material proof | The generator multiplies the same visual weaknesses | Establish a small approved asset vocabulary, then automate variation and placement |

These observations concern the art fixtures. The repository does contain a photo-terrain shader and map imagery; the problem is the absence of a finished painterly asset/material library for the illustrated scenery, not an absence of all textures or shaders anywhere in the project.

## What the reference artists actually do

The mechanisms below come from artists describing their own work or from official engine documentation. Our proposed applications are engineering/art-direction inferences, not claims about every supplied screenshot.

### Painted foliage is geometry, texture and shading working together

In the founder's linked breakdown, Alexander Maznev uses brush-shaped foliage textures, deliberately constructed branches, transferred canopy normals and vertex-color occlusion. His bark and grass also carry authored pattern information. The enclosing canopy shell is a helper for shading the foliage; it is not simply the final solid visible tree. The scene starts with one directional light. This is strong evidence that asset construction, rather than an elaborate lighting stack alone, carries much of the appearance. [Artist's breakdown](https://80.lv/articles/breakdown-making-3d-landscape-look-like-painting)

David Holland's *Meadows* similarly uses cards distributed on shaped canopies, transferred normals and painted plant silhouettes. He describes narrowing a sprawling environment into a smaller scene to focus on materials and lighting. Transfer the asset workflow and iteration discipline; his historical Unreal lighting settings are not a recipe for this Godot build. [Holland's firsthand account](https://80.lv/articles/meadows-creating-stylized-nature-in-ue4)

Our reference script explicitly says an earlier leaf-card overlay was removed after dark artifacts. Kristóf Lovas demonstrates how card normals and back-face normal handling can create precisely that class of discontinuity. This is a diagnostic candidate for the abandoned experiment, not a proven retrospective cause: the failed version lacks sufficient preserved evidence. Test its components in isolation instead of treating that attempt as proof that cards cannot work. His tutorial studies a look inspired by other games; it does not document those games' internal renderers. [Lovas's foliage tutorial](https://polycount.com/discussion/209623/smooth-foliage-like-in-breath-of-the-wild-europa-by-helder-pinto-mini-tutorial)

### Composition and materials must be authored before mass production

Airborn's environment team describes establishing composition, proving representative assets and creating small complementary kits. They balance dense and quiet areas and distinct depth layers, with materials judged in the destination renderer. The useful lesson for EnFractal is the production order, not their cinematic effects budget. [Airborn's production account](https://80.lv/articles/airborn-3d-production-of-a-stylized-ue5-animation)

For our walking-height scene, that means large readable tree/terrain masses, medium branch and planting groups, and selective fine marks. Random scatter at one scale does not create this hierarchy. Neither does uniform detail everywhere. The ground needs deliberate transitions between trail, soil, rock and plants, with quieter areas around interaction points.

### Godot can support an authored art pipeline

Blender Studio's DOGWALK is a useful pipeline comparable: its developers describe creating assets in Blender, importing through glTF, and judging the result in Godot, including deliberate lighting simplifications. Its papercraft look differs from our target and its success does not certify our Compatibility renderer or laptop performance. It does demonstrate that using Godot does not require replacing authored art with runtime primitives. [Official Godot developer interview](https://godotengine.org/article/godot-showcase-dogwalk/)

Tiny Glade's developers describe tailored procedural art systems and their own rendering work. Its appearance is not a preset we can acquire by choosing the same language or adding generic procedural generation. Our inference is to reproduce the discipline of coherent construction rules using our own regional assets, not attempt to recreate their engine. [Developer interview](https://80.lv/articles/exclusive-tiny-glade-developers-discuss-bevy-proceduralism-publishers-cozy-games), [official technology information](https://pouncelight.games/tiny-glade/info/)

## Confirmed technical prerequisite

An isolated runtime probe on the installed Godot **4.7.2** inspected the art-reference mesh arrays. All 18,432 near-terrain triangles and all 1,760 far-terrain triangles disagreed with their supplied normals under Godot's clockwise front-face convention. Standard box geometry and the tested canopy shells provided controls with the correct convention. [Godot ArrayMesh convention](https://docs.godotengine.org/en/stable/classes/class_arraymesh.html)

Reversing only the terrain indices in memory, while retaining the camera, lights and materials, restored direct illumination and visible cast shadows on the terrain. The corrected diagnostic is overbright because the existing material/light choices were made around the defective result. Do not treat it as an art improvement ready to ship. The production repair must audit other custom mesh generators, preserve collision behavior, and recalibrate light and color after correcting the geometry.

The diagnostic evidence records the probe, captures, affected scope and remaining uncertainty. The old foliage-card failure is a separate investigation; the terrain defect does not establish its cause.

## Replace assumed performance restrictions with measurements

The art-reference budget explicitly permits zero texture pixels and disables transparency and runtime shaders. These fixture restrictions were allowed to define the art approach without a comparison showing they were necessary. They are not requirements from the founder. `StandardMaterial3D` already executes GPU shaders; “no shaders” is not a meaningful distinction. In the next implementation packet, replace the categorical restrictions with measured texture/material/overdraw budgets and the smallest material implementation that provides the required appearance.

| Candidate | First experiment | Cost and limitation to measure |
|---|---|---|
| Painted cutout foliage | One atlas, cards fitted to visible shapes, alpha scissor | Overlapping covered pixels and shadow rendering still cost time; avoid large mostly empty rectangles |
| Smooth canopy shading | Transfer/blend normals from a canopy proxy; test front/back lighting | Preserve authored normals during export; do not flatten all branch and leaf shading indiscriminately |
| Painted surface materials | Shared bark, limestone, soil and foliage textures; subtle roughness variation | Measure imported texture memory and mip behavior, not PNG disk size |
| Contact and depth | Asset self-occlusion, terrain-aware joins and bounded sun shadows | Movable objects must not carry permanently painted shadows of their old neighbors |
| Material transitions | Stable local/geographic masks blending soil, grass and rock within the terrain material | Additional texture samples have a cost; use triplanar mapping selectively where stretching requires it |
| Low-detail trees | Fewer overlapping leaf groups, simpler branches, preserved crown gaps and color masses | A low triangle count is insufficient if fill rate or draw submission is the bottleneck |
| Ground cover | Plant patches aligned with slope and ground color; spatially bounded instance groups | Density and group size affect overdraw and culling; retain navigable and visually quiet spaces |

Godot's alpha-scissor mode supports shadow casting and avoids blended-transparency sorting. It is an appropriate first test for foliage. Alpha-to-coverage with MSAA can improve edges, but must be compared in motion against its cost. Closed opaque surfaces should normally cull their backs; reserve double-sided materials for assets that need them. [Godot material guidance](https://docs.godotengine.org/en/stable/tutorials/3d/standard_material_3d.html)

Jess Hider's grass example demonstrates tightly fitted masked cards, intentionally directed normals and matching grass roots to ground color. Those principles are useful; copy neither its Unreal shader graph nor its upward-normal choice blindly onto sloping Barton terrain. [Artist's grass tutorial](https://jesshiderue4.wordpress.com/materials/stylized-wind-blown-grass/)

Use mipmaps and suitable filtering; assess leaf-edge shimmer while walking. Plain MSAA alone does not solve texture-cutout aliasing. [Godot antialiasing guidance](https://docs.godotengine.org/en/stable/tutorials/3d/3d_antialiasing.html) Texture compression and actual import format determine residency; modest shared atlases deserve measurement rather than a blanket ban. [Godot image import guidance](https://docs.godotengine.org/en/stable/tutorials/assets_pipeline/importing_images.html)

For repeated assets, use spatially bounded MultiMeshes because a group is culled as a whole. [Godot MultiMesh guidance](https://docs.godotengine.org/en/stable/tutorials/performance/using_multimesh.html) Baked lightmaps may help fixed scenery but cannot be rebaked in an exported game, so they cannot be the general lighting solution for freely edited worlds. [Godot LightmapGI limitations](https://docs.godotengine.org/en/stable/classes/class_lightmapgi.html)

Check optional effects against the actual build. Current official documentation lists Compatibility support for effects such as SSAO and glow; older blanket claims that this renderer cannot provide them are unreliable. None is a prerequisite for the first asset proof. [Renderer feature table](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html)

## The next build: a proof of the art pipeline

This sequence replaces another whole-map palette/lobe pass. It is scoped to one roughly **30 m walking-height Barton Creek patch**. The sizes, atlas resolution and budgets below are initial experiment choices, not measured laptop limits.

| Packet | Owner and dependencies | Deliverable | Exit evidence |
|---|---|---|---|
| ART-0: trustworthy lighting | Rendering agent; first | Fix and audit face orientation, normals, culling and vertex-color conventions; neutral gray sphere/plane/card fixture | Front/back and rotating-light checks; meaningful visual evidence; collision unaffected; recorded engine/renderer version |
| ART-1: one excellent live oak | Asset agent, with technical artist; can author while ART-0 runs | Original Blender source, forked/tapered trunk and branches, deliberate canopy groups and gaps, one original painted leaf atlas; candidate card and opaque representations | Eight-angle turntable, close/mid/far views, neutral and warm light in Godot; trunk is recognizable and crown has depth from inside and outside |
| ART-2: material and ground proof | Material agent; parallel to ART-1 using shared scale/palette contract | Bark, limestone, soil and ground-cover samples; one terrain transition and limestone ledge | Painted marks follow forms; plant bases meet ground; no glossy/plastic default look, floating overlays or high-frequency noise field |
| ART-3: composed playable patch | Integrator; after ART-0–2 | Oak plus juniper, understory, short trail, creek bank and editable path/platform relationship on the pinned map | Three walking-height views, one construction view and a walk-through; contacts and style survive an edit and reload |
| ART-4: measured low profile | Performance agent; measurement fixture can prepare earlier | Same assets at reduced cost, profile toggles and named-device report | Frame-time distribution and memory, dense-canopy view, motion shimmer and distance changes; visual criteria assessed separately from performance |
| ART-5: reusable generator vocabulary | World-builder agent; only after visual acceptance | Versioned asset/material families and bounded variation/placement rules | Several deterministic arrangements maintain identity and style; source intent remains editable; no reversion to generic lobes |

At most three implementation packets should be active together, consistent with the backlog. Review after ART-0–2, then after ART-3–4. Keep one independent art reviewer and one technical/performance reviewer outside the authorship of each result. A third independent reviewer supplies the formal checkpoint assessment when that gate is evaluated. Research supports these specific experiments instead of continuously widening the reference list.

### Asset handoff contract

For the first oak, retain the editable `.blend`, original painted source and exported glTF/GLB. A 512–1024 pixel atlas is an initial comparison, not a required final resolution. Document channels: visible color, cutout mask, any self-occlusion/roughness data and per-vertex controls. Keep color interpretation explicit. Preserve UVs and authored normals and verify them after import. [Blender normal editing](https://docs.blender.org/manual/en/latest/modeling/modifiers/normals/normal_edit.html), [Godot glTF workflow](https://docs.godotengine.org/en/stable/tutorials/assets_pipeline/importing_3d_scenes/available_formats.html)

Record asset ID, source rights, real-world dimensions, pivot, material-family IDs, style version, generator version, seed and collision proxy. Keep branches/crowns or building parts separately addressable where editing needs them. Distinguish verified map features from illustrative vegetation. Procedural generation should assemble and vary these assets within a measured envelope. Source assets may be manually or procedurally authored; the deciding evidence is their visual result, not the authoring method.

AI creation remains structured: “make a shaded path” selects approved tree/path/material families and changes semantic parameters, then regenerates affected joins. It does not require welding the region into one uneditable mesh or inventing fresh arbitrary materials for every request.

### Visual and performance acceptance

Use the supplied images as visual targets, with the existing reference brief translating them to Texas geography. Geographic grounding does **not** require dull olive colors. Warm limestone, luminous foliage, cool shade and selective saturation can preserve local identity. Borrow the relationships between light, color and forms without relabeling fantasy species as surveyed Barton vegetation.

Review silhouette, material identity, light/color, contact, depth and composition against the references. Assess walking-height motion and at least three views; one flattering still or a white-background asset render is insufficient. If reviewers still describe the result as balloons, plasticine or a blockout, stop expansion and revise that asset/material combination. Do not award appearance points for test coverage, determinism or low triangle count. Keep the existing 8.5 visual target and separate milestone gate; do not create a new score for this research report as a substitute for rendered progress.

Begin measurement at 1280×720 and compare other resolutions if useful. Record exact hardware, renderer, settings, warm/cold conditions, p50/p95/p99 frame times, visible primitives, draw calls, texture residency and memory peaks. Capture a dense foliage view and a short repeatable walking route. Compare one change at a time: opaque/scissor foliage, alpha-to-coverage, shadow reach, ground layers. A provisional 16 MiB texture allocation for the small proof is an experiment cap, not evidence of fit on the laptop. Keep the roadmap's device targets; do not invent achieved FPS. [Godot GPU profiling guidance](https://docs.godotengine.org/en/stable/tutorials/performance/gpu_optimization.html)

## Engine decision and limits

Continue the first proof with Blender for asset authoring and Godot for the playable result. There is no evidence that changing engines will fix closed blob canopies, absent painted surfaces, weak composition or incorrect face orientation. Consider a renderer/engine comparison only if a specific required appearance remains blocked after the same competent asset has been tested with documented settings.

More parallel code generation alone will not resolve the gap. The team needs actual asset creation, material/shading work and strict visual editing, with a reference-quality asset demonstrated before its procedural variations are propagated. Agents can construct and test that pipeline; they cannot certify the founder's aesthetic satisfaction through numerical self-ratings. This research does not claim a finished tree, repaired production renderer, accepted art style or proven low-device performance.
