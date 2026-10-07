# Cleanup plan for the room-scale direction

**Executed 6 October 2026** after the founder approved all six groups. The record below is kept as written for the proposal; this section states what actually happened and where it deviated.

- Everything removed is recoverable from the `geography-era-final` branch (a tag of the same name exists locally; the hosted Git proxy refused tag pushes, so the branch is the durable reference). Restore any path with `git checkout geography-era-final -- <path>`.
- Groups A, B, C and E were deleted, each in its own commit. Group D was renamed to `assets/art_sources/painterly`, `game/assets/art/painterly` and `tools/art/build_painterly_trees.py`, with asset manifests and Godot import sidecars regenerated. Group F moved to `docs/history/`; the kept research moved from `docs/roadmap/research/` to `docs/research/`.
- A hand-built placeholder room replaced the Pfluger scene as the boot target, with `run-room.ps1` and `tools/test-room.ps1` replacing the Pfluger launcher and test. As of 7 October 2026, boot loads `game/scenes/room.tscn` as `RoomWorld` (`game/scripts/native/RoomWorld.cs`), with `RoomHud.cs`.
- Deviations from the proposal: `creation_ops.gd` and its test were deleted rather than generalized (the stone-platform vocabulary was pinned to the Greenbelt style file and terrain joins; the compiler/authority kernel is the real baseline). `player_controller.gd`, `player_test.tscn` and `world_physics_smoke.gd` were kept, labeled as a legacy fixture, because `invention_runtime.gd` and its tests are wired to that body; R0 rewires them to the C# controller. `manual_invention_integration.gd` was deleted because it drove the real Barton map; `invention_editor_smoke.gd` now hosts the runtime on a flat fixture. The S0 and manual-invention checkpoints and the two painterly asset documents moved to `docs/history/` rather than staying under `docs/engine/`.
- Persistence semantics from the save/travel service are recorded in `docs/engine/persistence-notes.md`.
- Not verified here: this container has no Godot or .NET toolchain. The C# build, `run-engine-tests.ps1` and `tools/test-room.ps1` must be run on the Windows development machine before this is trusted; expect the first run to reimport the renamed art assets.

**Proposed 6 October 2026. Nothing listed here has been deleted yet.** The founder asked to be consulted before any deletion. Each group below names its disposition, its size, and the question that gates it. Git history keeps everything; "delete" means remove from the working tree and from future builds, not from history.

Repository today: 465 files, 128 MB. About 118 MB of that is geodata, map packages, Barton art and evidence screenshots for places the game no longer visits.

## Summary

| Group | Files | Size | Disposition | Gate |
|---|---:|---:|---|---|
| A. Geography: map sources, packages, builder, terrain runtime, district scenes, their tests and docs | ~160 | ~104 MB | **Delete** | Q1 |
| B. PostgreSQL save/travel service, sandbox travel UI, recovery tooling | ~25 | 0.2 MB | **Delete**, keep the authority's persistence seam | Q2 |
| C. Multiplayer authority and transport experiments | ~15 | 0.1 MB | **Delete** | Q3 |
| D. Barton painterly art sources (trees, painted textures, build script) | ~25 | 24.5 MB | **Keep and rename**, or delete | Q4 |
| E. Geography-specific research documents | 5 | small | **Delete** | Q5 |
| F. Superseded planning documents | 6 | small | **Move to `docs/history/`** | Q6 |
| G. Creation kernel, controllers, toolchain, painterly shaders, kept research | ~110 | ~1 MB | **Keep and generalize** | none |

## A. Geography (delete, Q1)

Everything that exists because the world was a real place reconstructed from USGS elevation and OpenStreetMap features.

**Data and builder**

- `maps/barton_creek/` (13 files, 18.5 MB) and `maps/pfluger_district/` (23 files, 33.1 MB): archived OSM/USGS sources, package outputs, source ledgers.
- `game/maps/barton_creek/` (11.7 MB) and `game/maps/pfluger_district/` (11.6 MB): runtime copies of the packages.
- `mapbuilder/` (13 Python files): fetch, OSM, terrain, tiles, photo pilot, Pfluger tracing, spatial pin, verification.
- `requirements-map.txt`.

**Runtime code tied to terrain and districts**

- `game/scripts/map_runtime.gd`, `map_viewer.gd` (1300 lines, the Barton viewer), `terrain_collision_streamer.gd`, `terrain_mesh_job.gd`, `spatial_pin_v1.gd`, `workshop_runtime.gd`, `path_platform_join.gd`, `painterly_workshop.gd`, `workshop_art_dressing.gd`, `art_reference.gd`, `painterly_ground_kit.gd`, `player_controller.gd` (the 1.7 m GDScript controller superseded by the C# one).
- `game/scripts/native/PflugerWorld.cs` (district scene builder). `PflugerHud.cs` is **not** deleted: its customization panel, companion buttons and camera switching are generalized into a room HUD (group G).
- `game/scenes/main.tscn`, `pfluger_world.tscn`, `art_reference.tscn`, `player_test.tscn`.
- `game/shaders/pfluger_deck.gdshader`, `pfluger_water.gdshader`, `photo_terrain.gdshader`, `painterly_ground.gdshader` (terrain patch shader).
- `game/styles/greenbelt_styles.json`, `game/styles/art_reference.json`.
- `game/native/NativeContractProbe.cs` and `NativeGameBoot.cs` lose their Barton/Pfluger branches (edit, not delete).
- `game/export_presets.cfg` and `game/project.godot` lose the Barton exclusions and the "Barton Creek local workshop" application name (edit).
- `.gitattributes` loses the map-package line-ending pins (edit).

**Tests that only exist for the terrain and districts**

- `game/tests/`: `map_runtime_smoke`, `terrain_seam_smoke`, `terrain_residency_smoke`, `terrain_loading_input_smoke`, `visual_streaming_smoke`, `movement_physics_smoke` (legacy controller on terrain), `path_platform_join_smoke`, `workshop_integration`, `workshop_path_integration`, `workshop_capture`, `workshop_path_capture`, `workshop_art_smoke`, `workshop_art_capture`, `art_reference_smoke`, `art_reference_capture`, `painterly_geometry_smoke`, `painterly_patch_capture`, `engine_render_probe`, `engine_route_probe`, `phase0_canonical_probe`, `spatial_pin_v1_probe`, `native/PflugerSceneTest.cs`, `native_pfluger_scene.tscn`.
- `tests/`: `test_mapbuilder.py`, `test_pfluger.py`, `test_photo_pilot.py`, `test_regional_contract.py`, `test_spatial_pin_v1.py`, `test_phase0_canonical.py`, `spatial_pin_v1_compare.py`, `phase0_canonical_compare.py`, `run_spatial_pin_v1_probe.ps1`, `run_phase0_canonical_probe.ps1`, `fixtures/phase0_canonical_*.json`, `fixtures/spatial_pin_v1_*.json`.
- `test_world_state.gd` and `test_creation_ops.gd` pin a Barton manifest; they are rewritten against a room manifest, not deleted (group G).

**Launchers and tools**

- `run-map.ps1`, `run-pfluger.ps1`, `run-style-study.ps1`, `run-painterly-patch.ps1`, `run-invention-workshop.ps1` (wraps `run-map.ps1`; replaced by a room launcher).
- `tools/test-pfluger.ps1`, `tools/capture-workshop-art.ps1`, `tools/diagnostics/` (terrain lighting audit scripts), `tools/engine_probe/` (phase 0 feasibility probes on the Barton scene).
- `run-engine-tests.ps1` keeps only the retained suites (edit).

**Documents and evidence**

- `docs/images/` (55 PNGs, 23.5 MB) and `docs/videos/art3-walkthrough.mp4` (3.4 MB): screenshots of Barton, Pfluger and the painterly workshop.
- `docs/engine/phase0/` (canonicalization, spatial pin, geo contract, feasibility, decisions), `docs/engine/phase1/` (Barton art reference, terrain streaming, map contract, structured workshop, painterly kit and audits, capture JSON), `docs/engine/first-slice.md`, `docs/engine/movement-physics.md`, `docs/greenbelt-style-study.md`.
- `docs/engine/checkpoints/`: `phase0-1.md`, `followup-0-2.md`, `reference-guided-2026-10-02.md`, `art0-3.md`, `s1-pfluger-avatar.md`, `s1-pfluger-photo-reference-audit.md`. (`s0-native-foundation.md` and `manual-invention.md` stay; see G.)
- `game/CREDITS.md` and `game/RELEASE-CREDITS.md` lose the USGS/OSM/Wikimedia sections (edit; the files stay for future asset credits).

One caveat worth a sentence: the painterly reference brief and the pipeline diagnosis under `docs/engine/phase1/` and `docs/roadmap/research/13-*` describe *why* the earlier art fell short. The diagnosis (research 13) is kept. The brief is Barton-specific and goes.

## B. PostgreSQL save/travel (delete, Q2)

The local PostgreSQL control plane, sandbox worlds, invitations, portal travel and off-host recovery were built for a shared Earth. A single room needs a file-based save per room; moving between rooms of one house is a scene switch.

- `services/save_travel/` (server, service, setup, migration, tests), `tools/save-travel/`, `tools/run-save-travel-tests.py`, `run-save-travel.ps1`, `run-save-travel-tests.ps1`.
- `game/scripts/travel_runtime.gd`, `travel_panel.gd`, `travel_transport.gd`; `game/tests/travel_panel_smoke.gd`, `travel_adapter_faults.gd`, `save_travel_integration.gd`; `tests/save_travel_recovery/`.
- `docs/engine/phase4/` (recovery, research, travel UI), `docs/engine/checkpoints/save-and-travel.md`.

Kept: the authority's `persistence_sink` and receipt model, and `durable_creation_smoke.gd`, which tests the authority's save envelope without the database. The ideas worth carrying (idempotent action receipts, fenced sessions, uncertain-result reconciliation) are already in the authority.

**Founder decision, 6 October 2026:** multiplayer with invited players and their companion AIs is a real later goal, but single-player comes first. The service can be restored from history at any time; the last commit before the deletions will be tagged so restoring it is one command. The expectation is that it will serve as a reference when the multiplayer phase begins rather than being revived verbatim, because it was built around sandbox worlds, invitations and host fencing for a shared Earth. Before deletion, the protocol and transaction-contract sections of `services/save_travel/README.md` are moved into a short persistence-notes document so the file-based room saves honor the same semantics from the start.

**Note on local data.** `.cache/postgresql/data` on the founder's two machines holds Barton-era world saves. It is not in the repository and nothing here touches it. The question is only whether the founder wants a one-time export before the tooling that can read it is removed.

## C. Multiplayer authority and transport experiments (delete, Q3)

- `tests/shared_authority/` (Godot subproject), `tests/transport_smoke/` (DTLS probe subproject), `tests/run_authority_smoke.ps1`, `run_authority_process_smoke.ps1`, `run_transport_smoke.ps1`, `tests/fixtures/phase2_authority_vectors.json`.
- `docs/engine/phase2/` (shared authority prototype, separate-process fixture, test design, transport feasibility, evidence JSON).

The local `creation_authority.gd` is a different thing (single-process command boundary) and stays.

## D. Barton painterly art sources (decide, Q4)

- `assets/art_sources/barton/trees/*.blend` (11.6 MB), `game/assets/art/barton/textures/*.png` and `trees/*.glb` (12.9 MB), `tools/art/build_barton_trees.py`, `tools/ensure-godot-art-imports.ps1`, `game/scripts/painterly_assets.gd` (tree factory), `game/tests/painterly_asset_capture.gd`, `docs/engine/phase1/painterly-asset-library.md`, `painterly-material-kit.md`.

Recommendation: **keep, renamed** to `assets/art_sources/painterly/` and `game/assets/art/painterly/`. The oak and juniper are original editable assets; the back yard test case and the "cozy village" wish both want trees, and the painted ground/limestone/bark textures are usable material references for the style pass. The alternative is to delete now and regenerate later; the cost of keeping is 24.5 MB in the tree.

## E. Geography-specific research (delete, Q5)

- `docs/roadmap/research/01-global-map.md`, `06-persistent-earth-and-portals.md`, `09-maxar-open-data-assessment.md`, `10-qgis-mcp-assessment.md`, `15-pfluger-district-and-small-avatar.md`.

Kept (still relevant, some with edits later): `02` performance and stack, `03` visual style options, `04` physics and creation runtime, `05` AI/MCP and security, `07` business and costs, `08` product/community/release, `11` structured world and style, `12` comparables, `13` painterly diagnosis, `14` platform and engine.

## F. Superseded planning documents (move to `docs/history/`, Q6)

- `docs/WORLD-VISION.md`, `docs/roadmap/ROADMAP.md`, `docs/roadmap/BACKLOG.md`, `docs/roadmap/PLANNING-BRIEF.md`, `docs/engine/single-player-contract.md`, `docs/engine/decisions/0002-codex-game-profile.md` (the Codex-first client choice; the new direction is model-neutral, so this becomes background).

They already carry superseded banners. Moving them keeps `docs/` readable without losing the budget constraints, the protection semantics and the first-journey ideas that the new contract will absorb. The alternative is to delete them once the new contract is written.

## G. Keep and generalize (no question)

**C#**

- `game/native/WorldScaleProfile.cs`: add a ~0.10 m profile; make it the default once the controller is retuned.
- `game/native/NativeWorldContract.cs`, `LegacyCreationCompiler.cs`: unchanged.
- `game/native/NativeGameBoot.cs`, `NativeContractProbe.cs`: load a room scene; drop district branches.
- `game/scripts/native/SmallPlayerController.cs`, `CompanionAvatar.cs`: retune speeds, step height, margins for 10 cm; otherwise sound.
- `game/scripts/native/PflugerHud.cs` becomes `RoomHud.cs`: keep customization, companion buttons, camera switching; drop the district credits.
- `game/tests/native/SmallAvatarPhysicsTest.cs`, `native_small_avatar.tscn`: retune expectations.

**GDScript kernel**

- `creation_compiler.gd`, `creation_authority.gd`, `creation_ops.gd`, `creation_visuals.gd`, `creation_execution.gd`, `invention_runtime.gd`, `invention_editor.gd`, `world_state.gd`, `world_physics_profile.gd`, `game/creation_templates/*.json`.
- Generalization needed: the authority's fixed Barton plot and protected rectangles become a room-bounds query; the terrain-height callable becomes a surface query against physics; `invention_runtime.gd` stops reaching into the map viewer for the player body; style version strings stop saying `barton_painterly_v1`; `world_state.gd` pins a room manifest instead of a map manifest; `creation_visuals.gd` stops depending on the terrain ground kit.
- Tests kept: `creation_compiler_smoke`, `creation_authority_smoke`, `creation_visuals_smoke`, `invention_runtime_smoke`, `invention_editor_smoke`, `manual_invention_integration`, `durable_creation_smoke`, `native_interop_smoke`, `world_physics_smoke` (needs a room test scene in place of `player_test.tscn`), `test_world_state` and `test_creation_ops` (rewritten pins).

**Shaders and art**: `painterly_surface`, `painterly_foliage`, `painterly_bark`, `painterly_sky`; group D if kept.

**Toolchain**: `tools/bootstrap-native.ps1`, `build-native.ps1`, `export-native.ps1`, `native-toolchain.ps1`, `native-toolchain.lock.json`, `test-native-interop.ps1`, `run-engine-tests.ps1` (pruned), `game/EnFractal.csproj`, `EnFractal.sln`, `global.json`, `project.godot` (edited), `export_presets.cfg` (edited).

**Docs**: `NATIVE-BUILD.md` (edit the Pfluger references), `engine/decisions/0001-*`, `engine/quality-gates.md` (generic review rubric), `engine/phase3/*` (compiler, authority, editor, contract, research), `engine/checkpoints/manual-invention.md`, `engine/checkpoints/s0-native-foundation.md`, the retained research under E.

## Execution order once approved

1. Delete groups A, B, C and E in one commit each so the history is legible.
2. Rename group D (if kept) and move group F.
3. Edit the kept configuration (`project.godot`, `export_presets.cfg`, `.gitattributes`, `run-engine-tests.ps1`, `NATIVE-BUILD.md`, credits).
4. Add a minimal hand-authored test room scene (floor, four walls, a few boxes) so the retained suites and the controllers have a scene to run in; rewrite `test_world_state` and `test_creation_ops` against it.
5. Generalize the kernel (group G notes) behind the retained tests.
6. Verify: C# build, the pruned engine suite and the small-avatar test pass on the Windows development machine. This container has no Godot or .NET toolchain, so that verification happens on the founder's machine or a Windows runner.

## Questions for the founder

1. **Q1 Geography.** Delete everything in group A? This is the bulk of the repository and the clearest consequence of the new direction. Recommendation: yes.
2. **Q2 Save/travel.** Delete the PostgreSQL service and travel UI (group B) in favor of file-based room saves? And do you want a one-time export of the Barton-era saves in `.cache/postgresql/data` on your machines before the tooling goes? Recommendation: delete; export only if those worlds matter to you.
3. **Q3 Multiplayer experiments.** Delete the authority/transport fixtures (group C)? They remain in history for the eventual multiplayer phase. Recommendation: yes.
4. **Q4 Barton art.** Keep the oak/juniper sources and painted textures under a neutral name, or delete them? Recommendation: keep and rename.
5. **Q5 Research.** Delete the five geography research documents (group E)? Recommendation: yes.
6. **Q6 Old plans.** Move the superseded vision/roadmap/backlog/contract to `docs/history/`, or delete them outright? Recommendation: move.
7. **Capture device.** Does your phone have LiDAR (an iPhone Pro model)? If so, a RoomPlan export is a cheap second input alongside photos. The garage photos came from an iPhone 17 dual-wide camera.
8. **Compute.** Is the RTX 2070 Super (8 GB) the only GPU, and are you willing to use a cloud GPU session or a hosted image-to-3D API for asset generation if local models do not fit? This changes which backends the pipeline builds first.
9. **First style preset.** Your art notes point at Tiny Glade warmth. Should the first preset be painterly/storybook (closest to the existing shaders), or something else you have in mind for the garage?
10. **Scale.** Keep 1 unit = 1 metre with a ~0.10 m avatar, as recommended in the direction document, with a ×10 import scale as the fallback if physics proves jittery? Any objection to tuning gravity and jump for feel rather than realism?
11. **First AI client.** The earlier plan selected Codex as the first game-only companion client. The new direction says any AI. Is Codex still the first client to pair, or should the first integration target Claude, with the MCP surface kept neutral either way?
