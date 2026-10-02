# EnFractal data and art credits

## Active Pfluger district

The Pfluger preview uses archived USGS 3DEP elevation and [OpenStreetMap contributors](https://www.openstreetmap.org/copyright) data under ODbL 1.0. See [the source ledger](../maps/pfluger_district/sources/2026-10-02/sources.json), [data terms](../maps/pfluger_district/COPYING.md) and [source audit](../maps/pfluger_district/package/audit.json). The derived database, district boundary and route are separately distributed under ODbL. Bridge dimensions, building proxies, water level and seeded vegetation are labeled interpretations; no third-party Pfluger photos or Austin imagery are included. Original editable oak assets, painted textures and shaders from the earlier art pass are reused.

## Retired Barton Creek study


The playable map uses elevation from the [U.S. Geological Survey 3D Elevation Program](https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer). Roads, trails, waterways, building footprints and parking polygons come from [© OpenStreetMap contributors](https://www.openstreetmap.org/copyright), under the Open Database License 1.0. The OSM-derived feature database and raw extract are distributed with the repository under the terms described in [`maps/barton_creek/sources/COPYING.md`](../maps/barton_creek/sources/COPYING.md).

The small photo-informed rendering pilot uses two Wikimedia Commons photographs as **color and appearance references**. The photographs themselves are not projected onto game geometry, and the 3D scene is not a photogrammetric reconstruction.

| Reference | Creator and rights | Use in this prototype |
| --- | --- | --- |
| [Barton Creek Greenbelt](https://commons.wikimedia.org/wiki/File:BartonCreekGreenbelt.jpg), 2007 | Julia Duffy (JTduffy), [dedicated to the public domain](https://commons.wikimedia.org/wiki/File:BartonCreekGreenbelt.jpg#Licensing) | Regional limestone, vegetation and creek color cues. Exact camera position is unknown. The archived 960 px thumbnail is [`greenbelt_reference_960.jpg`](../maps/barton_creek/photo_pilot/sources/greenbelt_reference_960.jpg). |
| [Barton Creek Square Mall, west side](https://commons.wikimedia.org/wiki/File:Barton_Creek_Square_Mall_Austin_Texas_2020.jpg), 2020 | Larry D. Moore, [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | Stone, glazing, pavement, facade massing and historical storefront-sign cues for a simplified west-facing mall exterior. The archived 960 px thumbnail is [`mall_west_960.jpg`](../maps/barton_creek/photo_pilot/sources/mall_west_960.jpg). |

The photo thumbnails are downsized Wikimedia Commons derivatives of the originals. The game uses their sampled color values and procedural geometry; it does not display the photographs. The exact source file hashes, sample rectangles, location confidence and art-direction adjustments are recorded in [`evidence.json`](../maps/barton_creek/photo_pilot/evidence.json) and the generated [`photo_pilot.json`](maps/barton_creek/photo_pilot.json). The 3D limestone shelf, bank stones, tree colors, stall paint, mall facade heights and entrance placements are illustrative. Storefront signs reproduce cues from the **2020** reference and are not verified as current. Parking paint is also derived from OSM parking-aisle centerlines and should remain credited to OSM contributors.
