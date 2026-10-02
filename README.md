# EnFractal
A world of magic, shaped by you and your AI

## The world we are building

The [world and creation vision](docs/WORLD-VISION.md) centers a personalized player avatar and a separate, customizable avatar for the player's own AI. **The AI is the magic of the game:** spoken or typed wishes can become castles, rideable dragons, storms, floods and transformed landscapes through a common ruleset. The world begins in recognizable real geography, rendered as grounded painterly 3D; new forms remain editable and visually coherent. Saved-and-protected creations must survive direct and indirect effects.

**Current priority: single-player first, multiplayer last.** Prove the companion, expressive magic, art, controls, persistence and accessibility before investing in shared-world networking. The intended connection is a dedicated game-only profile for the player's own AI. Manual controls remain available for precision, accessibility and recovery. These are product goals; the current build remains a local engineering prototype without the embodied AI or the full magic vocabulary.

The active first destination is the **Pfluger Pedestrian Bridge / Lady Bird Lake district in Austin**, bounded by 6th Street, Barton Springs Road, Congress Avenue and MoPac. The initial player height is **30 cm (0.30 m)** in real-scale geography. The goal is reusable public-data reconstruction of recognizable places; Barton Creek is retired as a destination. Its map data is excluded from native builds; useful code and art carry forward, with temporary legacy regression fixtures retained. A source-based Pfluger preview and the 30 cm controller are implemented; detailed fidelity and art acceptance remain open. [District and scale plan](docs/roadmap/research/15-pfluger-district-and-small-avatar.md).

## MVP planning

The [revised roadmap](docs/roadmap/ROADMAP.md) defines single-player phases S0–S6, followed by the deferred multiplayer stage M7. The [execution backlog](docs/roadmap/BACKLOG.md) provides active SP packets and preserves the mapping to earlier E identifiers. **Native Godot .NET with C# is the accepted software baseline.** New gameplay uses C# while working GDScript systems migrate incrementally under regression tests. Python remains the offline geodata/save-service tooling. Browser delivery and other engine experiments are deferred. See the [accepted decision](docs/engine/decisions/0001-native-godot-csharp.md); the [platform study](docs/roadmap/research/14-single-player-platform-and-engine.md) is retained as background. The [comparable-projects research](docs/roadmap/research/12-comparables-and-layer-practices.md) remains useful background.

The checkpoint phase numbers below refer to the original roadmap and record what was built. They do not dictate the new execution order or establish completion of the single-player companion experience.

The [first phases 0–1 checkpoint](docs/engine/checkpoints/phase0-1.md) records what has been built, independent 1–10 ratings, and the remaining gates. Its first-pass median is 4.90/10, below the 8.5 target; the current local build is an engineering fixture rather than an accepted MVP milestone.

The [follow-up build evidence](docs/engine/checkpoints/followup-0-2.md) covers bounded terrain residency, a cross-language spatial pin, art in the playable workshop, and a two-client authority loopback. Each has a targeted review; the phase gates remain open.

## Play the active preview

Run `pwsh -NoProfile -File run-pfluger.ps1` after the [native bootstrap](docs/NATIVE-BUILD.md). The Godot project and exported game now open Pfluger by default. The preview includes a 30 cm player, separate customizable companion, eye/follow/reference cameras, mapped terrain and an inferred bridge/trail structure. The companion currently uses direct controls; real Codex connection and magic are S2 work. Art and geographic fidelity are still under review.

The [Pfluger source package](maps/pfluger_district/README.md) traces a 3.3517 km² district and a 150 m candidate route from archived USGS/OSM data. Source audit gaps stay visible. Avatar appearance preferences are separate from legacy saves; the preview does not yet persist world edits.

WASD moves, Shift runs, Space jumps and R recovers. Click to look; Esc releases the pointer. F1/F2/F3 change cameras, C customizes both avatars, and 1–5 control the companion. Run `tools/test-pfluger.ps1` for the new controller/scene checks.

The [S0 foundation](docs/engine/checkpoints/s0-native-foundation.md) records native build/export and regression evidence. The [S1 checkpoint](docs/engine/checkpoints/s1-pfluger-avatar.md) records the full-route test, actual engine views and open art/fidelity gates. The [Codex integration decision](docs/engine/decisions/0002-codex-game-profile.md) records the selected provider and the isolation proof still needed before connection.

## Earlier engineering checkpoints

Barton is retired from the product and excluded from native exports. Its content remains temporarily as a development regression fixture; reusable original art, terrain tools, creation rules and save/recovery code carry forward. The historical launchers below explicitly open that fixture, not the new experience.

The [S0 native foundation](docs/engine/checkpoints/s0-native-foundation.md) locks Godot .NET/C#, adds a repeatable build/test/export path and preserves the existing workshop. Start with [native build instructions](docs/NATIVE-BUILD.md). S1–S6 gameplay and acceptance work remains in progress.

The [Phase 4 local save and travel checkpoint](docs/engine/checkpoints/save-and-travel.md) adds PostgreSQL-backed saves, separate Home/sandbox inventions, quarter-gravity visits, invitations for a local test identity, safe Return Home, and tested backup/recovery. Launch [`run-save-travel.ps1`](run-save-travel.ps1); **T** opens travel and **B** opens creation. Fresh checkouts first run `python services/save_travel/setup.py`. Remote friends and off-host recovery remain open gates.

The [manual invention checkpoint](docs/engine/checkpoints/manual-invention.md) implements the **original roadmap's Phase 3 local creation loop**: edit parts and behavior graphs, test privately, place or equip an invention, use it, reopen/revise it and reload the saved world. Launch [`run-invention-workshop.ps1`](run-invention-workshop.ps1). **B** builds; **F/V** use/revise nearby ground inventions; **E/Q** use/revise the worn design; **C** allows or stops effects; **H** changes camera. Five starter designs share one compiler, permission service and capability interpreter. Remote multiplayer and the visual-quality gate remain open.

The [ART-0–3 implementation checkpoint](docs/engine/checkpoints/art0-3.md) adds corrected terrain lighting, original editable oak/juniper assets, painted materials, and a composed playable workshop. Launch it with [`run-painterly-patch.ps1`](run-painterly-patch.ps1). All 15 engine checks pass; independent art reviewers score the current standard views **6/10**, so the **8.5 visual gate remains open**. The report includes actual Godot views, a walk-through, low-profile evidence and the remaining visual work.

![Current painterly workshop implementation; visual gate remains open](docs/images/art3-standard-arrival.png)

The [Barton Creek map and local workshop](maps/barton_creek/README.md) are the first implementation, centered at **30.250924, -97.810494** near Barton Creek Square and the MoPac / Highway 360 area. A reproducible source-to-package builder provides sampled USGS terrain, mapped OSM roads, trails, creeks and building footprints, plus illustrative vegetation. A [photo-informed pilot](maps/barton_creek/photo_pilot/README.md) adds color and simplified detail at one Greenbelt segment and the mall west side.

The [first base-engine slice](docs/engine/first-slice.md) adds a validated read-only map runtime, terrain collision streaming, temporary first-person walking/jump/glide/recovery, and a local semantic workshop. A [path now connects to the editable platform](docs/engine/phase1/structured-workshop.md), regenerates its walkable join when moved, and survives save/reload. Open the map with [`run-map.ps1`](run-map.ps1), press **1** then **Tab** to walk in the test workshop, and use **P/E/J/K/O** for the platform and path. Run the engine checks with [`run-engine-tests.ps1`](run-engine-tests.ps1). This is a local engineering fixture, not the shared Home Earth, multiplayer, AI creation or a portal system. The grounded painterly 3D look is the confirmed direction; the current [walking-height art reference](docs/engine/phase1/art-reference.md) remains a blockout below the final quality target.

![Basic Barton Creek Square exterior study](docs/images/barton-mall-exterior-study.png)

The [Greenbelt visual style study](docs/greenbelt-style-study.md) applies three editable looks to the same small scene. Run [`run-style-study.ps1`](run-style-study.ps1) to compare them in Godot with keys **4**, **5**, and **6**.
