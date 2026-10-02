# Pfluger district source foundation

This is the first reproducible source package for the active Lady Bird Lake/Pfluger area. Barton Creek is retired; its map remains only as a temporary development regression fixture. This package supplies geographic context for the playable/art pass; it does not claim a reconstructed, collision-ready district.

The road-traced polygon follows **West 6th Street, Congress Avenue, Barton Springs Road and MoPac**. Its computed projected area is **3.3517 km²**, approximately **3.18 × 2.04 km** in its axis-aligned bounds. These are measurements of the archived mapped trace, not survey precision. `package/district_boundary.json` provides every boundary way ID, the local metre polygon and geographic GeoJSON. A declared **13.398 m** connector extends Barton Springs' western service-road junction to the nearest expressway carriageway centreline. Paired carriageways use the enclosed face containing Pfluger Bridge. Interior traffic islands remain inside the district. Review this convention before freezing the gameplay boundary.

The **4096 × 4096 m terrain square is only a processing envelope**. The intended district is the polygon, and every generated mapped feature is clipped to it. World dimensions stay in projected metres; the nominal player height is **0.30 m**. Small avatar size does not imply sub-metre source accuracy.

## Included and verified

- The 2026-10-02 USGS 3DEP image-service response, sampled at 2 m: 2048² raw pixels and a derived 2049² shared-vertex height grid. Every decoded height is checked against the source-derived grid.
- Seven public OSM map-query partitions, plus the complete Lady Bird Lake relation (32671, version 57), including its island holes. Original source requests, times and hashes are archived.
- 535 building footprints/proxies, 879 road parts, 1,913 trail parts, 41 waterways, 10 water polygons, 200 vegetation areas and 162 developed areas. These counts describe clipped OSM records, not independently verified real-world objects. Individual trees and photo-derived detail are absent.
- A **150 m horizontal candidate route** from the north Pfluger approach toward the bridge, traced along the connected OSM walking/cycleway graph, with source way IDs. It excludes mapped stairs. `walkability_verified` is deliberately false: the deck/ramp elevation, railings, continuity and small-avatar clearances still need reconstruction and validation. Never snap its bridge points to bare earth.
- A source inspection [preview](package/preview.png), boundary, route, feature database, terrain payload, audit and checksummed manifest. The package uses the existing base-map spatial fields so the next runtime pass can consume it; it intentionally has no fabricated Barton photo-pilot fields.

## Reproduce offline

Use Python 3.13 and `requirements.txt` in an isolated environment. From the repository root:

```powershell
py -3.13 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r maps/pfluger_district/requirements.txt
.\.venv\Scripts\python.exe -m mapbuilder.pfluger build
.\.venv\Scripts\python.exe -m mapbuilder.pfluger verify
.\.venv\Scripts\python.exe -m pytest tests/test_pfluger.py
```

This session's installed interpreter is `.cache/pfluger-venv/Scripts/python.exe`; the environment is ignored by Git. Building and verifying the archived snapshot require no network. Tests rebuild in a temporary directory, compare every output byte, exercise corrupted/unmanifested source rejection and package corruption detection, and check the geographic contracts.

To deliberately acquire a **new** public snapshot:

```powershell
python -m mapbuilder.pfluger fetch --sources maps/pfluger_district/sources/YYYY-MM-DD
python -m mapbuilder.pfluger build --sources maps/pfluger_district/sources/YYYY-MM-DD --package .cache/pfluger-candidate
```

Fetch refuses a nonempty source directory. Live-source updates are not expected to reproduce the archived package. Review new road names, topology, lake relation, timestamps, metadata and boundary before adoption; the trace fails closed when the expected enclosure or limited connector is missing. JSON/Python/Markdown output uses LF newlines; the committed data byte hashes must survive checkout. Builder-source hashes normalize text to LF because the legacy Python files can have platform-dependent checkouts.

## Remaining source and reconstruction gates

`package/audit.json` records the current limits. In-district DEM pixel centres span roughly **125.38–165.46 m** in the source's unconfirmed vertical datum. A **5.25 m** low outlier elsewhere in the envelope is retained and flagged; it lies outside the district. Elevation values are not validated bridge decks or water levels. Native source tile lineage, vertical datum, terrain/water consistency and outlier diagnosis remain open.

The current OSM adapter retains the existing 1–2 m simplification of roads/trails. At a 0.30 m player scale, that is contextual mapping, not collision detail. It reconstructs the audited lake relation; other relation-only features can be absent. Building heights distinguish contributor tags from inferred/default proxies. A 3D consumer must honor polygon holes and cannot infer bridge height from `layer` tags.

Next source work should prioritize the landing's bridge/ramp/underpass structure and ground contact, followed by permitted photographs and richer City of Austin/TxGIO layers. `source_registry.json` lists authoritative candidates and their coverage/rights gates. No Austin imagery or Commons photograph has been downloaded or redistributed here. Publicly viewable material is not automatically licensed for reuse; metadata discovery and redistribution permission remain separate.

The native S1 preview consumes an identical copy of this package under `game/maps/pfluger_district`. Its inferred structural deck permits a tested 150 m walk; that runtime result does not change the source audit's unverified real-world walkability or establish surveyed bridge dimensions. See the [S1 checkpoint](../../docs/engine/checkpoints/s1-pfluger-avatar.md).

See [COPYING.md](COPYING.md) for the included USGS/OSM data terms, the [scope study](../../docs/roadmap/research/15-pfluger-district-and-small-avatar.md) for the district decision, and [world vision](../../docs/WORLD-VISION.md) for the broader game.
