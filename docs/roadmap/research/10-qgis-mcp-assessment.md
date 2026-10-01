# QGIS MCP as an agent-assisted map authoring tool

Assessment dated 1 October 2026. The founder pointed out `qgis_MCP` after the [Maxar imagery assessment](09-maxar-open-data-assessment.md). This is a planning evaluation of public repositories, not an installed or benchmarked integration. The [MVP roadmap](../ROADMAP.md) remains the coordinating plan.

## Which project?

Two similarly named projects are relevant:

| Project | What its published repository shows | Enfractal assessment |
|---|---|---|
| [jjsantos01/qgis_mcp](https://github.com/jjsantos01/qgis_mcp) | Earlier QGIS plugin plus Python MCP server; project/layer operations, QGIS Processing, map rendering and arbitrary PyQGIS execution. Its README describes Claude Desktop and QGIS 3.x, tested on 3.22. The last repository commit checked is from October 2025; GitHub lists no repository license. | Useful proof of the concept, but clarify its code rights before copying any code. Do not assume its setup and client compatibility reflect current QGIS or Codex. |
| [nkarasiak/qgis-mcp](https://github.com/nkarasiak/qgis-mcp) | Actively updated QGIS plugin and separate MCP server; its README documents QGIS 3.28–4.x, Codex and other MCP clients, and tools for layers, rasters, processing, rendering, coordinate transforms and project inspection. [QGIS's plugin registry](https://plugins.qgis.org/plugins/qgis_mcp_plugin/) lists a September 2026 stable release. The project declares GPL v2-or-later for the QGIS plugin and MIT for the server. | Better candidate for a bounded local evaluation, subject to a real compatibility and resource test. Pin an exact release/commit and keep the plugin and server versions aligned. |

The newer project's documented architecture is **coding agent → MCP server → local socket → QGIS plugin → PyQGIS**. It is an interface to a running desktop GIS, not a map dataset or an alternative to Godot. Its tool list includes raster inspection, coordinate conversion, processing algorithms, raster calculation and map render/export. Those features could reduce the founder's manual GIS work; none proves that a complete Enfractal tile pipeline already exists. [Architecture and tool catalogue](https://github.com/nkarasiak/qgis-mcp#architecture), [tool catalogue](https://github.com/nkarasiak/qgis-mcp#tools-125).

## Fit with the Maxar plugin and game architecture

The [Maxar QGIS plugin](https://github.com/opengeos/qgis-maxar-plugin) can put selected disaster-scene imagery into a QGIS project. QGIS MCP should then be able to inspect and process ordinary layers exposed in that project; this **cross-plugin path is a hypothesis until tested live**. It would help an agent compare footprints, resolution, CRS and visual appearance. It would not turn Maxar Open Data into a worldwide basemap, supply terrain elevation or change the imagery's [CC BY-NC 4.0 terms](https://registry.opendata.aws/maxar-open-data/). Only appropriately licensed source layers may enter distributed game assets.

Enfractal has two distinct MCP uses. **QGIS MCP is a founder/developer tool** for preparing geography offline. The separate [Enfractal creation MCP](05-ai-mcp-and-security.md) is a restricted interface for players to inspect supported game rules, validate designs and request previews/publication. QGIS's file, SQL and arbitrary-code tools must never be exposed through the player service or installed as a requirement on player computers. The production client and authoritative server consume small versioned map packages, not a live QGIS session. This matches the [global map pipeline](01-global-map.md) and keeps the low-hardware client target intact.

QGIS MCP is an optional interface to the GIS workbench. A scripted, reviewable GDAL/PROJ conversion recipe remains the durable build specification. Agent conversations or a saved `.qgz` project alone cannot reproduce a release: record input URLs and hashes, license, acquisition date, coordinate and vertical reference systems, processing parameters and tool versions. Keep source COGs and generated game tiles distinct.

## Proposed bounded evaluation

After selecting a first region and obtaining licensed source files, run one **small local comparison**, not a full high-resolution world import:

1. In a disposable QGIS project, use the chosen pinned QGIS MCP version to list source layers and report their bounds, CRS, pixel size, NoData values, date and licenses. Check those facts against the source metadata rather than accepting an agent's description.
2. Process a 256–512 m test window of licensed elevation and optional imagery. Reproject/clip it, inspect seam and nodata behavior, render a hillshade for human review and export the exact output and parameters. Avoid Maxar-derived outputs in a game fixture without suitable rights.
3. Re-run the same transformation with a versioned GDAL/PROJ recipe and compare bounds, pixel alignment, elevation statistics and checksums where outputs are intended to be bitwise identical. Preserve a human-readable deviation report where different resampling backends legitimately produce different bytes.
4. Measure QGIS peak memory, time, scratch-disk use and output size on the founder's machine; then test an 8 × 8 km *source extract* only if the small window succeeds. A pretty render is not a low-memory guarantee.
5. Feed the resulting tiny, licensed fixture into E04/E05. The Godot client must open the derived package without QGIS, MCP, a network map API or an AI call.

Pass only if an agent can reliably inspect and prepare the test area with less founder effort **and** the resulting map package remains reproducible and legally distributable. Otherwise use normal QGIS interaction or command-line GDAL; do not add QGIS MCP to the MVP critical path. This is a development convenience experiment, not a player feature or a new monthly hosting service.

## Operational boundary

The maintained project documents an `execute_code` tool and a local socket that lacks authentication by default; it explicitly warns that another local process could drive QGIS, and that its optional token is not a sandbox against processes under the same user. Use a local development profile, keep the service on loopback, enable its token when appropriate, and start/stop it for the work session. Limit the agent to a disposable project and reviewed processing steps; do not grant an unattended GIS agent game credentials or destructive access to the repository. The project also notes that its client-side confirmation behavior depends on the MCP client, so test that behavior rather than assuming every operation will prompt. [Maintainer's configuration and authentication notes](https://github.com/nkarasiak/qgis-mcp#configuration).

The tool's open-source license is separate from licenses on imagery, DEMs, and derived game assets. If Enfractal ever distributes or modifies the QGIS plugin itself, review the component's GPL obligations. Simply using QGIS MCP locally for preparation does not require adding it to the shipped game.
