# Barton Creek walking-height art reference

This is an **illustrative Phase 1 art and scale fixture**, not a finished grounded painterly scene or a surveyed reconstruction. Open `res://scenes/art_reference.tscn` in the Godot project to review it at a 1.67 m eye height. The default view faces a small contemporary trail lookout; set `ENFRACTAL_ART_VIEW=creek` before launch for the creek and trail view. Set `ENFRACTAL_ART_PROFILE=low` for the lower graphics profile. The lookout scene remains separate from the playable map; the [workshop art integration](workshop-art-integration.md) applies a bounded version of this visual language to actual path/platform edits. The October reference pass follows the [founder's five images, short tree video, and 80.lv breakdown](painterly-reference-brief.md) as visual principles; none of those third-party pixels or models are included in the project.

| View | Before this pass | Current pass |
|---|---|---|
| Contemporary lookout, standard | ![Earlier standard reference](../../images/barton-art-reference-before.png) | ![Current standard reference](../../images/barton-art-reference.png) |
| Trail and creek, standard | ![Earlier creek reference](../../images/barton-art-reference-creek-before.png) | ![Current creek reference](../../images/barton-art-reference-creek.png) |
| Contemporary lookout, low | ![Earlier low reference](../../images/barton-art-reference-low-before.png) | ![Current low reference](../../images/barton-art-reference-low.png) |

The source geometry is the pinned `barton_creek_v0` USGS 3DEP height grid and OSM trail and waterway centerlines. The source tree points in that package are already representative generated trees, not a census. Tree crowns, 45 additional seeded tree centers, grasses, shrubs, limestone outcrops, creek and trail widths, stone/soil colors, and the lookout are **authored interpretations**. The creek is a colored ribbon laid over terrain, not a hydrologic surface. The lookout is a semantic set of separate stone, steel, and cedar parts with editable source parameters in `art_reference.gd`; it is not an existing Barton Creek structure. Its short trail connector is also separately labeled. No external artwork or unlicensed imagery is embedded.

The local visual vocabulary is weathered limestone, dark creek water, broad oak-like crowns, taller juniper/cedar-like crowns, restrained understory, soil path, and a contemporary cedar/steel shade structure. The source tree tags select among three shared sculpted silhouettes with crooked/tapered trunks; authored trees use the same visual families. This pass gives oak crowns separated, asymmetric masses with small opaque solid leaf clusters, more varied ground and stone color, a blue sky gradient, denser seeded understory and grass, and a variably widened two-tone creek bank. These visual additions remain reproducible from the same map and recipe. A test of flat diamond leaf cards produced dark artifacts; those cards were removed from the final version. The palette stays green and limestone/soil based instead of copying the references' pink fantasy trees or medieval buildings.

The scene is still **a procedural blockout**. The canopy masses and shrubs need stronger original shapes; the creek is visibly a straight-edged colored ribbon without convincing bank contact or depth; the distant terrain and sky have broad flat areas; stone lacks close-up strata and weathering. My visual self-assessment is **about 5/10 standard and 4.5/10 low**. A separate reviewer rated the final fixture **5.0/10 standard, 4.6/10 low, and about 4.8/10 for the creek-facing standard view**; they independently reran the focused smoke test. This **does not pass the 8.5/10 art gate** or substitute for a playable workshop review. The next art work should prioritize a reusable live-oak/juniper hero asset, a true shallow-creek edge assembly with rock/soil contact, and in-game comparison after actual player edits.

The fixture has no texture files, alpha foliage, post-process effects, or runtime shaders. One directional light supplies the scene; the low profile disables its shadows. Vegetation and rocks use shared meshes and `MultiMesh` instances. The source runtime remains identical on both profiles, so the low profile does **not** reduce map-package parsing or its memory cost.

| Measured scene construction count | Standard | Low |
|---|---:|---:|
| 192 m near terrain triangles | 18,432 | 4,608 |
| Coarse outer elevation triangles | 1,760 | 440 |
| Tree centers retained | 53 | 53 |
| Canopy masses / solid leaf clusters | 124 / 744 | 84 / 168 |
| Shrubs / rocks / limestone ledges / grass tufts | 110 / 38 / 18 / 880 | 45 / 14 / 8 / 230 |
| Upper-bound mesh triangles, before visibility culling | 72,964 | 28,940 |
| Shared material instances | 15 | 15 |
| Shadow-casting lights | 1 | 0 |
| One scene-build sample on RTX 2070 Super | 381 ms | 248 ms |

The upper bound is counted from Godot mesh faces times instance counts, including geometry outside the camera; it is **not** measured triangles drawn on a frame. The build samples measure synchronous scene construction and map loading on the current machine, not steady-state frame time. Frame rate, GPU memory, repeated-run distributions, and the lowest target laptop still need measurement. No claim is made that this reference currently meets the roadmap's final art, device, or accessibility acceptance criteria.

The capture script is `game/tests/art_reference_capture.gd`. Set `ENFRACTAL_ART_CAPTURE` to an absolute PNG path, then launch Godot with `--path D:\Enfractal\game --script res://tests/art_reference_capture.gd` using the project's installed executable. `game/tests/art_reference_smoke.gd` checks source anchors, walking-height camera, the separate lookout and trail join, material/terrain budgets, and the reduced low profile. The capture script prints the generated counts, and the three images above show exactly what was visually reviewed.
