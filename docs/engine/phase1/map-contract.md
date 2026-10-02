# E05 regional map contract: second-region fixture

**Status:** bounded engineering evidence, not a second playable region or a completed global map. The existing Barton Creek package remains unchanged. The Python map builder still assembles Barton-specific OSM and photo layers; only its reusable terrain, coordinate, tile-addressing and validation contracts were exercised on a separate synthetic location.

The test fixture uses a 4 m square centered near Wellington, New Zealand (41.2865° S, 174.7767° E), in EPSG:32760. Its 4 × 4 invented DEM crosses zero elevation. It has no downloaded source, surveyed surface, feature database, rights claim or Godot scene. Repeated encoding produces identical bytes. The fixture checks geographic-to-**projected-grid-offset** coordinate round trips, negative-height encoding and full decoded-grid agreement with the input DEM. Current `MapConfig` coordinates are UTM projected-grid offsets; they are **not** a true local tangent East/North/Up frame.

## Addressing rules

`enfractal-geographic-quadtree-v1` gives an address `geo-v1/level/column/row` from WGS84 longitude and latitude. Level zero is one 360° × 180° cell; each level quarters the parent. Columns increase east, rows increase south. Longitude +180° aliases −180°; internal east/south boundaries belong to the east/south child. The north pole belongs to row zero and the south pole to the final row. This is an **index**, not a playable globe mesh or a polar local-frame convention.

`enfractal-local-grid-v1` indexes a regional package's terrain tiles in metres: +X projected easting and +Z decreasing projected northing. The positive outer edge belongs to the last tile, matching the Godot map runtime; outside coordinates have no tile. A local tile key has meaning only with the package's pinned `map_id`, spatial frame and content hashes. It is not a land parcel, authority cell or persistent object ID. New map versions record both scheme names in `tile_addressing`; the shipped Barton Creek v0 package predates this optional field and continues to verify under its established implicit local-grid rule.

## Property-level feature provenance and the frozen v0 package

Barton Creek v0 has a known field error: OSM way `27453848` (Barton Creek Square Mall) has `building:levels=2`, so its 6.4 m proxy height is **inferred** using 3.2 m per level. The packaged `height_is_estimate=false` incorrectly presents that value as non-estimated. The existing `barton_creek_v0` package, its feature hash and local-workshop pins remain immutable. The builder's explicit `legacy-v0` path reproduces its six checked-in files byte for byte, including that historical field value.

The public `build_features()` boundary rejects v1 provenance under `barton_creek_v0` and rejects the legacy mode under any other map ID. A direct helper call therefore cannot accidentally publish corrected feature bytes with the frozen v0 identity.

For future map IDs, the corrected OSM feature schema records a stable feature ID containing map ID, OSM element type (`way`), OSM ID, feature category and clipped-part index. A SHA-256 digest of the archived OSM snapshot bytes is stored on each source-derived feature and in feature metadata. Building `footprint_evidence` identifies an OSM way outline processed through projection, region clipping and simplification. Separate `height_evidence` distinguishes an OSM contributor's numeric `height` tag, a height inferred from `building:levels`, and an illustrative class default. Only a valid numeric `height` tag sets `height_is_estimate=false`; even that is an OSM contributor report, not an independent survey. Clipped-part indices may change when source geometry changes, so migrations must match features against old IDs and geometry explicitly instead of assuming a part index survives every source refresh.

There are two integration options. A future `barton_creek_v1` can reuse the same archived DEM and OSM bytes with a new versioned source manifest and corrected feature payload; existing v0 worlds stay pinned until an explicit reviewed migration. Alternatively, a temporary UI annotation can warn about v0's known height provenance while v0 remains active, without changing the base package. Never emit corrected feature bytes under `barton_creek_v0` or silently repoint existing sparse deltas. A disposable v1 **features-only** candidate from the same snapshot measured 3,646,550 bytes, versus v0's 2,113,006 bytes; this includes repeated evidence and IDs but is not a playable package.

## Height and source gaps

Fetch, build and verify now call one DEM check. It requires a single aligned projected band, the configured extent and CRS, finite values, no masked/nodata cells and a plausible elevation range. **Any missing cell stops the build.** A repaired DEM would require a separately reviewed recipe and provenance; a gap is never silently interpreted as safe ground. Package verification also compares **every decoded height vertex** with the archived source-derived vertex grid to within the recorded quantization error. A changed payload cannot pass merely by updating its hash and min/max metadata.

The present `heights.r16` format stores metres as `offset + uint16 × scale`, north row first, with the package's `height_origin_m` subtracted in the runtime. The fixture confirms signed source elevations remain representable. It does not establish an absolute global vertical datum. Barton Creek's service response still does not identify a trustworthy source-tile datum, so its local Y must not be converted to global ECEF or matched to another region's heights as if the datums agreed.

## Sparse-delta pin

`base_pin_material` checks and returns the fields a world delta must bind to: map ID, heights/features SHA-256 hashes, CRS, axes, projected center, geographic center, grid and tile dimensions, and all height-decoder values. The fixture changes the projected center while leaving the payload hashes unchanged and confirms the spatial pin changes; the verifier rejects the mismatch with the build configuration. The Godot local workshop stores a digest of these same spatial fields plus the two payload hashes. This Python function does not claim to serialize that Godot digest, so cross-language canonical-hash conformance remains a shared-schema gate before server persistence.

## Verification

From the repository root, run `./.venv/Scripts/python.exe -m pytest -q` and `./.venv/Scripts/python.exe -m mapbuilder verify`. The current results are 14 Python tests passing and the unchanged Barton Creek package verifying. A fresh v0 build to a disposable `.cache` directory reproduced **all six files, including `manifest.json`, byte for byte**. The synthetic fixture covers coordinate/height round trips, local and geographic tile boundaries, dateline/pole addresses, source gaps, deterministic encoding, and source-bound payload validation. The Barton mall regression fixture proves the v1 corrected estimate flag, field-level evidence and feature ID without replacing the checked-in v0 package.

## Remaining E05 gates

- Choose and license actual second-region source data; report native resolution, vertical datum and coverage at every playable cell. The Wellington fixture is fabricated data, not that source selection.
- Split or reject dateline-crossing source requests explicitly. The geographic index has boundary tests, while the current regional UTM fetch/OSM bounding-box pipeline does not ingest a dateline- or pole-spanning region.
- Define a global ECEF/local frame transform, geoid conversion and exact versioned pin serialization with the runtime, physics and persistence owners. A geographic tile address alone does not solve those contracts.
- Generalize or replace the Barton-specific OSM/photo/landmark stages, then build, play and profile a real second region. Establish source-version migration behavior for sleeping world deltas before upgrading any pinned base.
