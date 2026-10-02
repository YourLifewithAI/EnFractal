# Pfluger district and the 30 cm player

> Later founder clarification: Barton Creek may be retired/deleted as a destination. Reuse its useful systems and art; temporary regression dependencies and personal saves should be handled explicitly.


**Scope clarification and source review — 2 October 2026.** This updates the first-location and avatar-scale decisions in the [roadmap](../ROADMAP.md), [backlog](../BACKLOG.md) and [vision](../../WORLD-VISION.md). It is a planning/source-feasibility review: no new district has been downloaded, reconstructed or benchmarked.

## Selected scope

The active destination is the **Pfluger Pedestrian Bridge / Lady Bird Lake district in Austin**. The founder specifies **6th Street north, Barton Springs Road south, Congress Avenue east and MoPac west**. Trace those named boundaries into an explicit polygon, settle junctions/road edges and add a separate scenery/source buffer before calculating area. Do not substitute an arbitrary fixed-size square or treat the entire district as the first detailed scene.

Begin with the bridge, one landing and a short adjoining trail/waterfront route (a proposed first detailed route of roughly 100–200 m, subject to data). Keep the rest as clearly identified coarse context and extend detail progressively within the selected district. A small local claimable plot belongs on this route. In single-player it is a creative/protection boundary, not a prerequisite for online ownership services.

Barton Creek content development is **on hold**. Preserve its package, source archives, saves and regression tests. Reuse suitable regional assets, geospatial conversion, manifests, streaming, editable-source compilation and recovery. The new district needs its own package/base identity, origin and configuration. Never reinterpret a Barton save at a downtown origin. Current hard-coded workshop/spawn locations need configuration before reuse.

Longer-term scope is a growing catalog of well-documented, recognizable places, reconstructed primarily from reusable public sources. Global addressing may remain; continuous detailed whole-Earth coverage is not an initial delivery requirement. Streaming can avoid loading a globe at once, but it does not solve missing evidence, content production or near-ground quality.

## Small inhabitants in a real-scale place

The player is initially **30 cm (0.30 m)** tall, while geography and engine units remain meters at real-world scale. Compared with a hypothetical 1.8 m avatar, the same distances span six times as many body heights and the same ground area spans 36 times as many squared body heights. This is a scale comparison, not a guarantee of 36 times the useful content, player capacity or enjoyment. Travel speed, sightlines and traversal still determine perceived size.

This reduces the amount of geography needed for a rich-feeling world, but increases the importance of near-ground detail. Curbs, roots, steps, stems and drainage edges become significant obstacles. The present Barton grid's 2 m sample interval spans about 6.7 player heights. Interpolation and fine height encoding do not recover unmeasured geometry.

Implement **one small-player profile** first. The current test controller is approximately 1.7 m tall, walks/runs at 5/8 m/s and has 0.4 m floor-snap and spawn-height tolerances—values inappropriate to copy unchanged for this scale. Retune collision dimensions/contact margins, stepping, slopes, movement, jump/glide, camera eye height/near clipping, picking/reach, navigation, occupancy, spawn and recovery together. Do not automatically divide gravity or all spell forces by a scale factor. See [controller](../../../game/scripts/player_controller.gd), [body](../../../game/scenes/player_test.tscn) and [runtime](../../../game/scripts/invention_runtime.gd).

Bridge decks, approaches and underpasses require multiple traversable surfaces at the same horizontal location; one terrain-height query cannot represent them. Placement, recovery and AI navigation must select the correct supporting surface. Use detailed structural/near-player geometry over the regional terrain foundation, with collision matching visible boundaries. Do not make the entire district centimeter-resolution or solid-collide every grass blade.

The companion's size and dragon/mount dimensions remain separate design decisions. Stable identity and protection survive shape/size changes. Claims and generated doors, paths and mount points must expose meaningful dimensions for the supported body profiles. Arbitrary ant-to-continent scaling remains deferred.

## Verified source leads and remaining audit

The review verified descriptions and service metadata, not tile-by-tile district coverage or permission to redistribute every hosted asset. Record source date, CRS/vertical datum, units, resolution, missing cells, allowed reuse and checksums before packaging.

| Source | Useful evidence | Limit to preserve |
|---|---|---|
| [Austin 2021 contours](https://catalog.data.gov/dataset/elevation_contours_2021), [TxGIO lidar](https://www.tnris.org/stratmap/elevation-lidar.html), [USGS 3DEP](https://www.usgs.gov/3d-elevation-program/about-3dep-products-services) | Terrain/lidar discovery; Austin contours describe 2021 acquisition; 3DEP offers data without use restrictions | Prefer original point clouds/DEM and product reports to smoothed contours; identify exact district tiles/datum first |
| [Austin Building Footprints 2023](https://maps.austintexas.gov/gis/rest/Shared/PlanimetricsSurvey_1/MapServer/0) | Footprints plus maximum/base/elevation attributes | Validate units/nulls/meaning; not finished roofs, entrances, facades or interiors |
| [Aerials 2023](https://maps.austintexas.gov/gis/Image/AerialMosaics/Aerials2023/ImageServer) | Approximately 0.1524 m pixels after source-unit conversion from EPSG:2277; useful overhead alignment | Pixel spacing is not 3D accuracy; redistribution rights are not established by an accessible endpoint |
| [Impervious Cover 2023](https://maps.austintexas.gov/gis/rest/Shared/PlanimetricsSurvey_1/MapServer/1), [sidewalks](https://maps.austintexas.gov/arcgis/rest/services/Shared/Transportation_1/MapServer/15), [urban trails](https://maps.austintexas.gov/arcgis/rest/services/Shared/Transportation_1/MapServer/5) | Ground-surface classification and route layout | Filter planned/potential sidewalks; linework does not establish deck heights, small steps or collision-ready surfaces |
| [Parks](https://maps.austintexas.gov/arcgis/rest/services/Shared/Infrastructure_2/MapServer/0), [2022 canopy](https://catalog.data.gov/dataset/tree-canopy-2022) | Park identities and canopy distribution | Canopy polygons do not supply individual trunks, species, heights, branches or understory; do not infer rights to their upstream imagery |
| [Trail Conservancy: Pfluger Bridge Circle](https://thetrailconservancy.org/projects/pfluger-bridge-circle/) | Firsthand landmark, bridge/landing and planting context | Current conditions still need date verification; web publication does not itself grant asset reuse |
| [2018 bridge panorama by Sk5893](https://commons.wikimedia.org/wiki/File:Pfluger_Pedestrian_Bridge.jpg), [2015 bridge photograph by Bryan Rutherford](https://commons.wikimedia.org/wiki/File:Pfluger_pedestrian_bridge.JPG) | Dated/geolocated views with explicit CC BY-SA 4.0 licensing | Follow each file's attribution/share-alike requirements for reused/adapted material; two dated views are not a complete photogrammetry set |

Austin's dataset-specific [Open Data Terms](https://datahub.austintexas.gov/stories/s/ranj-cccq) require verification; their contents were not retrieved during this review. Do not substitute the site's general legal notice or a blank copyright field for a dataset license. Publicly viewable photos are discovery leads until reuse terms are recorded. No permission or current coverage claim is inferred for Google/other commercial street imagery.

More photographs increase the chance of useful evidence, not the certainty of automatic reconstruction. Dates, viewpoints, overlap, camera metadata, occlusions and rights determine usefulness. Water/submerged surfaces, bridge undersides, dense canopy interiors and small ground features are likely gaps. Select a dated baseline instead of silently combining 2021 terrain, 2022 canopy, 2023 buildings and older photographs into an allegedly exact present-day model.

## Reconstruct, interpret, verify

1. **Audit and pin a public-source snapshot.** Identify landmarks, bridge/deck topology, paths, shoreline, building masses and source rights. Label verified, inferred and absent information at feature level.
2. **Build a measured semantic baseline.** Generate terrain, separate structures, route surfaces, vegetation zones and water boundaries. Preserve editable parts and evidence before applying style. Do not use a fused scan as the sole source of a bridge or building.
3. **Apply the painterly recipes.** Preserve distinctive silhouettes and spatial relationships; simplify incidental noise. Focus near-player detail on roots/ground, path edges, banks, stems, bridge approaches and readable railings. Use scalable distant city/vegetation representations.
4. **Validate before bespoke correction.** Reserve some public photos and the founder's observations as held-out checks. Compare matching ordinary-height viewpoints for landmark/layout fidelity, then walk the route at the 30 cm camera for art, collision and visibility. Record errors and uncertainty separately from aesthetic judgments.
5. **Request only targeted missing evidence.** The founder's photographs can test accuracy first, then fill named gaps such as a ramp connection or underside. Track what required those photos or manual correction; a fully hand-modeled showcase does not establish an automated location pipeline.
6. **Test manipulation and regeneration.** Claim a small plot, create/protect work, transform vegetation and use companion magic; reload and verify both geographic baseline and deliberate changes. Preserve locks and source/generator/style versions.

Acceptance records should distinguish source-backed reconstruction, plausible procedural detail and player changes. Report landmark alignment/topology and visible gaps, founder recognition, small-avatar traversal, regeneration effort and the proportion of work needing manual correction. A repeat run from pinned inputs must reproduce the baseline. A second place later tests transferability; the initial goal is one verified local district and an enjoyable solo experience, not a promise of exact reconstruction anywhere.
