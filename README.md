# EnFractal
A limitless shared world

## The world we are building

The [world and creation vision](docs/WORLD-VISION.md) is the project's stated long-term direction: a geographically grounded, cozy 3D Earth with natural proportions, softly sculpted forms, painterly materials and gentle lighting. Players should be able to explore recognizable places at walking height and change them through structured, persistent parts. A shared construction and visual system should make new player and AI-assisted work belong in its local environment without erasing regional character. The MVP proves this in a bounded area and creation vocabulary before broader Earth coverage or more ambitious transformations.

## MVP planning

The [MVP roadmap](docs/roadmap/ROADMAP.md) describes the proposed shared Earth, sandbox worlds, creation system, technical architecture, validation gates, and operating budget. The [execution backlog](docs/roadmap/BACKLOG.md) breaks the work into agent-sized tasks. Detailed research is linked from the roadmap.

## First playable engine slice

The [Barton Creek map and local workshop](maps/barton_creek/README.md) are the first implementation, centered at **30.250924, -97.810494** near Barton Creek Square and the MoPac / Highway 360 area. A reproducible source-to-package builder provides sampled USGS terrain, mapped OSM roads, trails, creeks and building footprints, plus illustrative vegetation. A [photo-informed pilot](maps/barton_creek/photo_pilot/README.md) adds color and simplified detail at one Greenbelt segment and the mall west side.

The [first base-engine slice](docs/engine/first-slice.md) adds a validated read-only map runtime, terrain collision streaming, temporary first-person walking/jump/glide/recovery, and one semantic stone platform that can be placed, resized, removed and reloaded from local saved state. Open the map with [`run-map.ps1`](run-map.ps1), press **1** then **Tab** to walk in the test workshop, and use **P/E/O** to edit the platform. Run the engine checks with [`run-engine-tests.ps1`](run-engine-tests.ps1). This is a local engineering fixture, not the shared Home Earth, multiplayer, AI creation or a portal system. The grounded painterly 3D look is the confirmed direction; the current scene is an early visual study rather than its final art treatment.

![Basic Barton Creek Square exterior study](docs/images/barton-mall-exterior-study.png)

The [Greenbelt visual style study](docs/greenbelt-style-study.md) applies three editable looks to the same small scene. Run [`run-style-study.ps1`](run-style-study.ps1) to compare them in Godot with keys **4**, **5**, and **6**.
