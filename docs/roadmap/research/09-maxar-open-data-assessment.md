# Maxar Open Data plugin and high resolution geography

Assessment dated 1 October 2026. The founder asked whether [opengeos/qgis-maxar-plugin](https://github.com/opengeos/qgis-maxar-plugin) supplies very high resolution data that could help EnFractal. This is a source and architecture assessment, not a downloaded image, an installed plugin, a license grant, or a performance measurement. The [MVP roadmap](../ROADMAP.md) remains the coordinating plan.

## Finding

Yes, the plugin can browse and download unusually sharp imagery. Its author describes the associated Maxar/Vantor Open Data releases as roughly **30–50 cm ground sample distance** for visual imagery. A 30 cm sample means one image pixel represents approximately 30 cm on the ground; it does **not** mean 30 cm height samples, accurate 3D geometry, or 30 cm positional accuracy. Multispectral imagery may be coarser, and panchromatic bands are grayscale. The plugin shows per-scene metadata so exact resolution and date must be checked for a candidate scene. [Plugin author explanation](https://gishub.org/blog/maxar-open-data-plugin/), [plugin repository](https://github.com/opengeos/qgis-maxar-plugin).

The decisive limitation for EnFractal is the **imagery license and coverage**. The plugin code is MIT-licensed, but the imagery it opens is published under **Creative Commons Attribution–NonCommercial 4.0**. The AWS registry and Maxar/Vantor's own program and protocol state that restriction. The program releases imagery around selected disaster activations, before and after events; it is not a continuous freely reusable high resolution Earth basemap. The explicit OpenStreetMap exception in the protocol applies to OSM, not automatically to a game. Since EnFractal plans subscriptions, the roadmap must not place these images or derived in-game textures in distributed or hosted game assets without a separate suitable commercial grant. [AWS dataset and license](https://registry.opendata.aws/maxar-open-data/), [current program](https://www.maxar.com/open-data), [Maxar activation protocol, licensing section](https://maxar-marketing.s3.amazonaws.com/files/downloads/119757_opendataprotocol_2020_04.pdf).

This is a conservative product decision based on the stated license, not a ruling on every possible transformation or development use. If a particular Maxar scene becomes important, obtain written rights covering a paid game, derived assets, redistribution, caching and player-created exports before putting it in the asset pipeline. A free download or an MIT GitHub repository does not supply those rights.

## What the plugin actually provides

The QGIS plugin reads a separate event catalog, displays footprint polygons, filters by date/cloud cover, and opens remote Cloud Optimized GeoTIFFs in QGIS. It can download selected visual, multispectral or panchromatic files. Its source code points to the [opengeos/maxar-open-data catalog](https://github.com/opengeos/maxar-open-data); the [AWS listing](https://registry.opendata.aws/maxar-open-data/) identifies the separate imagery bucket. The plugin is a GIS review/download tool, not a terrain engine, a high resolution DEM source, a global mosaic license, or a Godot streaming integration. [Plugin README](https://github.com/opengeos/qgis-maxar-plugin), [plugin data-loading code](https://github.com/opengeos/qgis-maxar-plugin/blob/main/maxar_open_data/dialogs/maxar_dock.py).

The publisher's broader commercial imagery archive advertises global coverage up to 30 cm resolution, but that archive is **not** the same collection as the disaster-focused Open Data catalog. It should not be used to infer that the plugin unlocks unrestricted global 30 cm imagery. [Vantor imagery types](https://pro-docs.maxar.com/en-us/Imagery/Imagery_types.htm).

Resolution also has a rendering cost. If a 2 × 2 km region were represented as one 30 cm RGB raster, the arithmetic is about 6,667 samples per side, 44 million pixels and **133 MB uncompressed** before mipmaps, terrain geometry, backups or duplicate dates. An 8 × 8 km source extract is about **2.1 GB uncompressed** at the same sampling. These are sizing examples, not the actual compressed COG download sizes. A game would need regional tiling, downsampling, caches and selective visibility even if licensing were solved. Those costs reinforce the existing low hardware plan.

## More suitable conditional sources for the first region

If the founder selects a **United States** starting area, evaluate [USGS-distributed NAIP](https://www.usgs.gov/centers/eros/science/usgs-eros-archive-aerial-photography-national-agriculture-imagery-program-naip) for imagery and [USGS 3DEP](https://www.usgs.gov/3d-elevation-program) for terrain. The USGS describes NAIP as public-domain aerial photography, says its 2018 resolution standard changed to **0.6 m** with a **0.3 m option** for some coastal areas, and provides coverage/access information. It says 3DEP products are free without use restrictions; [1 m bare-earth DEMs](https://data.usgs.gov/datacatalog/data/USGS%3A77ae0551-c61e-4979-aedd-d797abdcde0e) exist where coverage is available. These are two different data types and must be aligned by acquisition date, horizontal/vertical reference systems and actual local coverage. A 0.6 m pixel does not guarantee 0.6 m geographic accuracy: USGS documents a looser positional accuracy specification for NAIP.

Do not assume every US site has matching recent NAIP and 1 m DEM tiles, or that a later commercial mosaic inherits a public-domain status. Inspect the exact selected products, attribution/metadata, and any third-party layer before packaging. A non-US area can still use the original Copernicus GLO-30/WorldCover approach, or another explicitly licensed local source. High resolution is useful only where it improves the playable scene enough to justify processing, memory and rights management.

## Roadmap decision and test

Keep Maxar Open Data as an **external visual research reference** for places in its event catalog. Do not make it an MVP imagery, elevation, terrain-texture, client-streaming or world-export dependency. The plugin can help a researcher inspect how buildings, vegetation and flood damage appear from above, but any game asset traced from a scene needs a separate rights determination.

Before choosing the first region, compare two or three candidate locations with a simple evidence sheet:

| Gate | Evidence needed |
|---|---|
| Rights | Exact imagery and elevation product rights for a paid, downloadable, exportable game; permitted derivatives and attribution |
| Coverage | A contiguous buffered 8 × 8 km extract, with meaningful image date, cloud cover and source gaps |
| Geometry | Usable bare-earth heights, datum/coordinate conversion and collision-quality tests; imagery alone cannot pass |
| Visual value | Does actual high detail improve the chosen faceted/toon scene over an authored palette or downsampled source? |
| Client cost | Package size, tile count, peak decode/GPU memory and 720p integrated-graphics route |
| Durability | A pinned version that remains available for existing homes and sandbox copies |

Choose one legally usable package and a small comparable scene. If a United States site has both NAIP and 3DEP coverage, test it against the Copernicus-based baseline. Do not change the game architecture first: the existing map format already separates source imagery, elevation, render derivatives and persistent world edits. Record source IDs, acquisition dates, rights and checksums before any image enters a build.
