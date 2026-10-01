# Global Earth map: architecture and delivery plan

Status: planning recommendation, not implemented or benchmarked. Sources checked 2026-09-30. This workstream assumes one developer using coding agents, a provisional Godot .NET/C# client and authoritative runtime, an 8 GiB integrated-graphics baseline, and a combined early hosting-plus-AI ceiling of $100/month. All proposed sizes and performance limits below are test targets or sizing examples, not demonstrated capacity.

## Recommendation and scope

Build an immutable, versioned Earth geography package, then layer each world's persistent changes over it. Begin with a curated, geographically located play area approximately 2 × 2 km, within an approximately 8 × 8 km source-data extract. A coarse globe provides context and future addresses; the initial playable boundary is explicit. The physics team's candidate local-coordinate magnitude limit is 2 km, encompassing the core's corners. Expand only after creation, shared consequences, persistence and portals work in that region.

This preserves the global design without spending the MVP on worldwide high-detail downloads or empty travel. Every location already has a world ID, globally meaningful position and stable address. However, a coarse visible continent is not advertised as a fully simulated destination. Later regions can reuse the same package format and placement rules.

Represent real geography at the accuracy actually supplied by the sources: recognizable coastline, elevation, major landforms and broad vegetation. Fill small features procedurally or through authored content. A 30 m elevation sample interpolated onto a 1 m grid still does not contain measured 1 m terrain. Neither a generated tree nor a generated building is a claim about the corresponding real object. The first release should communicate that this is a geographically grounded fantasy Earth.

Use heightfield terrain plus separately placed structures, vegetation and rocks. Defer globally excavatable voxels, planet-wide photogrammetry, accurate building interiors, real-time weather assimilation and continuous ocean simulation. Caves can initially be authored spaces or a small number of separate meshes. A heightfield cannot represent every cave or overhang; that limitation is explicit in the DEM documentation. [Copernicus DEM API documentation](https://documentation.dataspace.copernicus.eu/APIs/SentinelHub/Data/DEM.html)

## Data selection, licensing and ingestion

| Layer | Proposed source and use | Obligations and limitations | MVP decision |
|---|---|---|---|
| Overview geography | Natural Earth land, coastline and geographic labels | Its supplied raster/vector data are public domain. Overview scale is inadequate for close-up shoreline collision. | Include a small offline overview; maintain credits anyway. |
| Elevation | Copernicus DEM GLO-30 regional subset, with a declared GLO-90 fallback | Select the free-and-open product instance, retain license and release identifiers, and reproduce required source/modified-data and liability notices in distribution. GLO-30 is a surface model containing vegetation and structures, not uniformly bare earth. | Primary regional source, frozen to a release. |
| Biome classification | ESA WorldCover 2021 v200 regional extract | CC BY 4.0 and dataset-specific attribution. Its approximately 10 m classes are input to broad artistic distribution, not individual plant identities. | Optional after a simple authored biome works. |
| Roads and buildings | OpenStreetMap extracts | ODbL attribution and applicable database share-alike obligations. Dataset licensing and hosting-service policies are separate. | Defer; do not depend on city reconstruction for the first playtest. |
| Imagery and bathymetry | None initially | An image visible on a map service is not automatically licensed for game redistribution. Underwater geography requires its own source review. | Stylized materials, water surfaces and explicitly synthetic seabed if needed. |

Sources: [Natural Earth terms](https://www.naturalearthdata.com/about/terms-of-use/), [Copernicus collection and specifications](https://dataspace.copernicus.eu/explore-data/data-collections/copernicus-contributing-missions/collections-description/COP-DEM), [Copernicus licenses, GLO-30-F section pp. 22–24](https://dataspace.copernicus.eu/sites/default/files/media/files/2025-06/copernicus_contributing_mission_data_access_v2_cop_dem_licenses.pdf), [WorldCover data and license](https://esa-worldcover.org/en/data-access), [OSM copyright](https://www.openstreetmap.org/copyright).

Keep a provenance manifest for each output: source URL, product instance, release, acquisition/retrieval dates, checksum, horizontal and vertical reference systems, source resolution, processing version and applicable notices. Archive the selected license with the recipe. Open game code does not erase third-party data conditions. A legal review of any later OSM-derived database/export design belongs before that layer ships; do not assume either that ODbL licenses all game code or that it has no implications for derived geography.

Download approved regional source products during packaging, not each time a player walks. Copernicus offers registered-user bulk access, while its processing API has its own access requirements; availability through a service is not a promise of anonymous runtime access. Pin the source and verify actual regional coverage rather than silently treating missing heights as sea level. [Copernicus access documentation](https://dataspace.copernicus.eu/explore-data/data-collections/copernicus-contributing-missions/collections-description/COP-DEM) [DEM API access conditions](https://documentation.dataspace.copernicus.eu/APIs/SentinelHub/Data/DEM.html)

Proposed pipeline: extract a buffered region; reject invalid values; normalize datum and units; inspect water and steep terrain; create coarse-to-fine levels; apply versioned artistic rules; produce render and collision derivatives; emit checksums and provenance. Use GDAL/PROJ tooling offline, with small Python orchestration if useful. The shipped C# game should consume prepared binary payloads rather than embedding a desktop GIS stack. Keep source rasters in Cloud Optimized GeoTIFF for reproducible processing and regional reads; this is an authoring/archive format, not a requirement that the game decode TIFF. GDAL supports tiled COG creation with overviews. [GDAL COG driver](https://gdal.org/en/stable/drivers/raster/cog.html)

Choose the initial region for variation and gameplay: accessible slopes, a ridge for gliding, water, protected arrival space and building ground. Avoid requiring a photorealistic city to establish recognition. Artificially safe starting terrain is an authored overlay with provenance, so nobody mistakes it for source elevation.

## Coordinates and planetary tiling

Persist positions with `WorldId`, a named global reference frame and double-precision ECEF coordinates. Present latitude/longitude to people; calculate nearby interactions in a stable local frame. Use meters throughout runtime interfaces. Local East/North/Up frames are established geospatial practice; geographic-to-local conversion passes through geocentric coordinates. [PROJ topocentric conversion](https://proj.org/en/stable/operations/conversions/topocentric.html)

The base manifest must distinguish orthometric source heights from WGS84 ellipsoidal heights. Copernicus specifies EGM2008 vertically. Apply the selected geoid transformation before ECEF conversion, recording its version; otherwise shorelines, entities and imported data can disagree vertically. Sea level should follow the chosen reference surface, not be hardcoded to ellipsoid height zero everywhere. Water height in the play area is an explicit surface property. [Copernicus reference systems](https://dataspace.copernicus.eu/explore-data/data-collections/copernicus-contributing-missions/collections-description/COP-DEM) [DEM height conversion](https://documentation.dataspace.copernicus.eu/APIs/SentinelHub/Data/DEM.html)

Physics coordinates remain close to zero in the region frame; the camera uses its own origin-relative coordinates. Camera rebasing never changes saved addresses. Cross-frame entity transfer transforms position, orientation and linear/angular velocity together exactly once. The physics authority owns this transition; the map supplies transforms. Define longitude wrapping, dateline-spanning bounds, poles and negative elevation in the initial schema, even when only a temperate region is playable. A pole needs a declared orientation convention for East/North.

| Spatial structure | Advantages | Costs and limits | Proposed role |
|---|---|---|---|
| Geographic quadtree | Direct connection to source data; simple hierarchy and cached ancestors; established terrain formats | Uneven physical cell shape near poles; dateline and polar handling need tests | Initial delivery/index hierarchy |
| Cube-sphere quadtree | Six well-defined faces, no latitude singularity in patch layout | Face seams, neighbor transforms and reprojection add engineering | Reconsider if polar traversal or near-uniform global patches become essential |
| Hexagonal/geographic discrete grid | Useful for grouping activity and broad analytics | Additional machinery to triangulate/render; one grid rarely fits all tasks | Optional service index later |
| One flat world or dense global voxel array | Simple only at small local scale | Precision, curvature and storage become incompatible with the objective | Reject as the planetary representation |

Choose a versioned geographic quadtree for the first delivery format, while keeping stable geographic object coordinates independent of tile keys. Cesium's quantized-mesh specification is an existing multiresolution terrain quadtree reference. OGC 3D Tiles provides hierarchical delivery and implicit quadtree/octree representations for renderable content. Neither specifies game authority, persistence or editable terrain semantics. Use either interoperable payloads where supported or a small documented heightfield payload; do not build a general 3D Tiles engine solely for MVP compliance. [Quantized mesh](https://github.com/CesiumGS/quantized-mesh) [OGC 3D Tiles](https://www.ogc.org/standards/3DTiles/)

Rendering tiles, collision patches, replication interest cells, server authority areas and player parcels are different partitions. No property right derives from a rendering tile. A future worker split must not renumber a player's land. A practical initial collision patch experiment is 256 m across, but that number is neither a parcel size nor a server boundary.

## Surface detail, construction and persistent edits

Separate five layers: licensed source elevation; deterministic terrain shaping; deterministic decorative distribution; authoritative player changes; disposable rendering derivatives. Give generators explicit versions and stable seeds. Gameplay-relevant procedural features must be reproducible or materialized by the authority; client-specific visual noise may change grass density, but must not decide whether a rock blocks movement.

Store structures as entities referencing versioned blueprints, transforms, materials, permissions and state. Store terrain changes as bounded authoritative transactions. Suggested transaction fields are `WorldId`, operation ID, actor, expected terrain revision, canonical frame and affected bounds, approved operation parameters, budget charge, resulting revision and output hashes. Persist the accepted result and audit metadata; replay must not depend forever on an old executable brush algorithm.

For MVP terrain shaping, use a sparse overlay of edited height samples plus bounded paint/material changes. An integer millimeter encoding for edited values is a candidate stable format, not a claim of survey accuracy or a commitment to millimeter-scale creature physics. Validate its range and compression before freezing it. Keep collision, vegetation removal and foundations consistent with the accepted revision. Cosmetic dirt or grass scatter need not become millions of database objects.

Serialize overlapping changes through a region authority. An edit touching several patches is one operation with one declared outcome; avoid half-applied hills after a crash. Initial edits must remain within the active authority's allowed extent. For each accepted operation, stage new surface/collision data, activate at a specified simulation tick, then publish the revision. If generation fails, keep the previous playable surface. Large terrain reshaping can visibly take time rather than blocking the game thread.

Use a durable append log plus compact snapshots of changed patches and entities. Checkpoints bound recovery work; they are not the only backup. Include idempotent retry IDs, corruption detection and a tested restore path. Content hashes deduplicate identical base and derived assets; they do not authorize access to private edits. Revision manifests for private worlds require access checks even if public base terrain is universally cacheable.

## Sandbox copies and base updates

A new sandbox references an immutable base manifest and receives an empty delta namespace, a new world ID and its own rules. It does not copy all Earth files or duplicate the home Earth's authoritative inventory. A regional seed can represent a location on the same underlying Earth while only the allowed active area is hosted. The portal and business plans must state the free tier's activity, storage and visitor limits separately.

Default copies include public baseline geography and the creator's permitted blueprints. Copying another player's home structure, private changes or personal state requires its own permission. A later explicit home-world snapshot feature can reference an authorized snapshot revision; it must never mean a continuously changing live pointer to all home-Earth edits.

Base map updates are migrations. A sandbox remains pinned until its owner or service policy adopts a new base. The shared Earth evaluates changed terrain against buildings, portals and protected sites before rollout; retain the previous version for rollback. Never auto-upgrade a mountain beneath a settlement on client refresh. An old base may require continued storage, so retention and migration policy are operating costs.

Export consists of the manifest, appropriately redistributable source references or assets, permitted deltas and required notices. Exporting a world's appearance does not export shared-Earth permissions, money or inventory authority. The same format supports a future independently hosted compatible world without requiring federation in the first release.

## Streaming, cache and cost controls

Ship enough coarse terrain and a small arrival package to show a useful scene before network refinement. Request nearby collision/arrival dependencies first, then visible detailed surfaces, then distant decoration. Maintain ancestors until replacement tiles are ready. Border samples and geometry transition rules must agree across adjacent levels; visual skirts alone do not repair collision cracks.

Bound request queues, decoded CPU memory, GPU uploads, disk cache and regeneration work independently. Cancel stale requests after fast travel. A missing high-detail tile yields coarse scenery; missing required collision keeps a visitor at a safe location until arrival is possible. It must never result in falling through an apparently loaded ground plane. Atlas browsing should not allocate full-resolution gameplay colliders.

Initial experiments should target a compressed regional play package below 250 MiB, a configurable disk cache initially around 512 MiB, and substantially smaller mandatory arrival data. These are proposed gates to reconcile with the rendering team's process-memory budget. Do not preload every future region. One 129 × 129 grid of 16-bit samples is about 32.5 KiB before metadata; its generated mesh, collision object and textures can consume many times that amount. Instrument actual retained allocations rather than treating network bytes as RAM usage.

Size costs from measurements: new players × cold package bytes, plus returning players × changed bytes, plus overhead, gives monthly transfer. For illustration, 100 cold downloads of 100 MB are 10 GB; 1,000 downloads of 250 MB are 250 GB before overhead. Source data being free says nothing about egress cost. Under the $100 combined ceiling, favor a single packaged region, shared immutable downloads, content hashing and a strict map spend allocation agreed with the business workstream before selecting hosting.

Do not use OpenStreetMap's public standard tile service as a game CDN or offline download source: its policy prohibits bulk prefetch and offline use. A permitted extract or licensed provider is a different arrangement. [OSMF standard tile policy](https://operations.osmfoundation.org/policies/tiles/)

Offline cached geography may support an explicitly separate local preview later. It cannot accept authoritative shared-Earth actions or mint transferable possessions while disconnected. Reconnection reconciles against current server state. Cache purge and world deletion must distinguish public reusable terrain from private data and backups.

## Interfaces and parallel agent work packages

Freeze these contracts with renderer, physics and persistence owners before they implement independently:

| Interface | Required information and responsibility |
|---|---|
| `BaseMapManifest` | Dataset/projection/geoid/generator versions, layer IDs, tile scheme version, content hashes, provenance and licensing |
| `WorldMapManifest` | World ID, pinned base hash, authorized overlay revision, available areas and access rules |
| `FrameDescriptor` | Stable frame ID, double-precision anchor, global/local transform, unit/axis conventions and valid simulation extent |
| `SurfaceQuery` | Revision-aware authoritative height, normal, material and water surface; rejects unavailable collision areas |
| `RenderTile` | Bounds, error metric, ancestor/dependency references and disposable geometry/material payload |
| `TerrainCollisionRevision` | Collision hash, frame, activation tick and affected bounds; frame transfer and physics compatibility information |
| `TerrainEditResult` | Accepted/rejected status, structured reason, charged budget, revision and affected assets |

| Work package | Independent agent output | Dependencies | Acceptance gate |
|---|---|---|---|
| MAP-01 Source audit | Regional source/license/provenance manifest and attribution mockup | Region choice | Every shipped layer has a pinned source and verified redistribution path |
| MAP-02 Coordinate contract | Coordinate ADR and reference test vectors | Physics owner | ECEF/local round trips, poles/dateline, negative heights and origin changes behave within declared tolerances |
| MAP-03 Offline packaging design | Reproducible regional pipeline recipe and sizing report | MAP-01/02 | Repeated build produces identical canonical hashes; no silent missing-data fill |
| MAP-04 Terrain delivery spike plan | Two LOD/tile options with measured-data collection protocol | Renderer contract | Seam traversal and interrupted downloads have safe fallbacks on baseline hardware |
| MAP-05 Edit persistence design | Transaction/snapshot schemas and recovery scenarios | World authority and physics contracts | Overlapping edit, retry and crash cases never create partial accepted terrain |
| MAP-06 Sandbox fork design | Base/delta ownership, snapshot and export rules | MAP-05, portal policy | A fork changes independently; no private-home or inventory leakage |
| MAP-07 Geography QA | Fixtures for water, steep slopes, source gaps and border cases | MAP-03 | Geographic provenance and gameplay deviations remain visible to developers |
| MAP-08 Release cost audit | Package, cache, transfer and version-retention model | MAP-03/04, business ceiling | Measured package fits allocation or scope is reduced before release |

The first two packages can run together; packaging follows their shared contract. Delivery and edit design then run in parallel. One integrator owns schema changes. Agents should not each invent their own tile hierarchy or frame conventions. Future coding agents need bounded branches, fixture data and reviewable outputs; no task should request that an agent independently “build the whole globe.”

## Decision gates and remaining questions

Gate A: approve the data manifest, playable extent and globally meaningful coordinate contract. Gate B: demonstrate one regional route with walking, flight, LOD changes and interrupted streaming while respecting memory limits. Gate C: two clients observe one edited surface revision, survive restart and visit an isolated sandbox copy. Gate D: expand to a second distant region without changing object address or persistence schemas. Gate E: only after usage evidence, decide whether fully traversable global coverage is worth its packaging, navigation and moderation costs.

Highest risks are inconsistent vertical datums, source artifacts mistaken for playable ground, renderer/collision disagreement, runaway derivative caches, expensive map ingestion before product validation, and base upgrades moving persistent homes. Ant-sized detailed ecosystems, global dynamic rivers and unbounded digging would substantially change this architecture; require explicit later decisions.

Open choices for the product owner are the initial geographic location, the acceptable degree of fantasy alteration, whether named real roads/buildings matter before retention is demonstrated, and whether the first public release needs continent-to-continent travel or can expose only approved destinations. The recommended default is recognizable geography, curated safe starting content and region-by-region playable expansion.

## Primary-source bibliography

All checked 2026-09-30; source claims above are distinguished from proposed architecture.

1. [Natural Earth terms of use](https://www.naturalearthdata.com/about/terms-of-use/) — public-domain status of supplied map data.
2. [Copernicus DEM collection](https://dataspace.copernicus.eu/explore-data/data-collections/copernicus-contributing-missions/collections-description/COP-DEM) — product instances, access, resolutions, datum and source limitations.
3. [Copernicus DEM license bundle](https://dataspace.copernicus.eu/sites/default/files/media/files/2025-06/copernicus_contributing_mission_data_access_v2_cop_dem_licenses.pdf) — distinguish the GLO-30-F free/open license from other product licenses; final three pages contain the relevant grant and obligations.
4. [Copernicus DEM API documentation](https://documentation.dataspace.copernicus.eu/APIs/SentinelHub/Data/DEM.html) — service access, fallback and height conversion details.
5. [ESA WorldCover data access](https://esa-worldcover.org/en/data-access) — versioned land-cover products, attribution and CC BY 4.0.
6. [OpenStreetMap copyright](https://www.openstreetmap.org/copyright) — data license and attribution entry point.
7. [OSMF standard tile usage policy](https://operations.osmfoundation.org/policies/tiles/) — public tile service limits, caching and prohibited offline/bulk use.
8. [PROJ topocentric conversion](https://proj.org/en/stable/operations/conversions/topocentric.html) — geographic, ECEF and local ENU transforms.
9. [GDAL Cloud Optimized GeoTIFF driver](https://gdal.org/en/stable/drivers/raster/cog.html) — offline raster packaging and overview support.
10. [Cesium quantized-mesh specification](https://github.com/CesiumGS/quantized-mesh) — terrain quadtree, border vertices, geographic placement and payload structure.
11. [OGC 3D Tiles standard](https://www.ogc.org/standards/3DTiles/) — hierarchical geospatial render-content delivery.
