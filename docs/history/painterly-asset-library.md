# Original Barton painterly asset library

> Links to files removed in the room-scale cleanup have been unlinked; those files are on the `geography-era-final` branch.

This is the first editable art library for the ART-0–3 checkpoint. Its appearance has not passed the 8.5 gate. It replaces the central workshop's closed primitive crowns with branched meshes and painted cutout foliage.

## Sources and exports

The [Blender source directory](../../assets/art_sources/painterly/trees) contains original `.blend` files with branch curves, hidden canopy guides, final meshes, packed original textures and the builder source. The [rebuild instructions](../../assets/art_sources/painterly/trees/README.md) use the deterministic [Blender recipe](../../tools/art/build_painterly_trees.py). Direct manual Blender edits remain editable, but the current recipe rebuild does not automatically read those edits back.

| Asset | Branch controls | Foliage cards | Total triangles |
|---|---:|---:|---:|
| `barton_live_oak_v1` | 32 | 559 | 12,620 |
| `barton_ashe_juniper_v1` | 11 | 492 | 6,890 |

The oak's overlapping horizontal groups follow primary and secondary boughs. Juniper groups overlap along their supporting branches. Neither export contains a closed canopy shell. GLBs have separate `Trunk` and `Leaves` nodes and use shared runtime materials. Asset manifests beside the [runtime exports](../../game/assets/art/painterly/trees) record bounds, hashes, seed, pivot convention, material roles and limitations. They describe illustrative species studies, not scanned/surveyed individuals.

!Oak in the destination engine

!Juniper in the destination engine

Additional walking-height and distant views are retained. The capture script generates all eight angles, a warm-light view and these distance checks in Godot; Blender beauty renders are not used as acceptance evidence. The old opaque primitive assets remain in historical evidence as the baseline representation, not a finished alternative.

## Painted textures and provenance

Six original PNG sources are checked into [the texture directory](../../game/assets/art/painterly/textures): oak leaf cluster, juniper spray, grass tuft, bark, ground paint and limestone paint. They were generated with the built-in image-generation tool for this project. Exact prompts and provenance are in [generation-prompts.json](../../assets/art_sources/painterly/textures/generation-prompts.json) and [grass-generation-prompt.json](../../assets/art_sources/painterly/textures/grass-generation-prompt.json). No pixels from the supplied reference images or third-party artwork were copied into these assets.

The source PNGs are retained at their generated resolution. Foliage/grass have real alpha channels. Tracked Godot `.import` settings cap runtime size at **1024 px**, enable mipmaps and VRAM compression, and preserve alpha borders. This replaces the old fixture's zero-texture/no-cutout rule with an explicit art experiment. Compression quality, overdraw and motion still require target-device review. Image generation is not bitwise repeatable: reproducibility comes from retaining/versioning the actual source files, not from promising identical regeneration from a prompt.

## Material contract

- Leaf UVs address one complete painted branchlet/spray. Canopy proxy normals are exported; there are no camera-facing billboards. Vertex **R** is authored self-occlusion, **G** height/wind weight, **B** stable variation, **A** one. This is vertex data, not albedo. The occlusion is not a ray-traced bake.
- The foliage shader uses alpha scissor with alpha-to-coverage, restrained contrast, rough diffuse lighting and two-sided normal handling. Wind defaults to zero for reproducible inspection.
- Bark UVs follow branch length and circumference. Limestone uses object-local triplanar paint so patterns travel with edited objects. Ground paint uses stable map coordinates, separate from the immutable height samples.
- Contact soil derives from semantic path/platform parameters and remains effective throughout the authorized plot. Illustrative plants/rill dressing remain separate from source geography and editable construction entities.
- The runtime supplies only simplified lower-trunk collision for the central trees. Surround trees are visual dressing; fine branch collision and individual tree editing are not implemented.

Opaque geometry culls backfaces. Cutout leaves/grass and the small transparent rain-garden surface have separate rendering behavior. Importer-generated tree LODs are disabled; unreviewed simplification must not silently destroy painted leaf cards. The current low profile retains tree architecture and reduces small plants, shadow reach and MSAA instead.

The [material kit](painterly-material-kit.md) describes reusable APIs. The new sky is an original bounded procedural shader, with ordinary distance fog supported by the installed Compatibility renderer; it does not use volumetric fog or baked lighting. See [Godot's environment documentation](https://docs.godotengine.org/en/stable/classes/class_environment.html) for the engine controls.
