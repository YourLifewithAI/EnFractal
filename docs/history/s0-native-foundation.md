# S0: accepted native foundation

> Links to files removed in the room-scale cleanup have been unlinked; those files are on the `geography-era-final` branch.

2 October 2026. Implementation baseline: `750ef88`; this checkpoint adds the founder's revised single-player roadmap and native software decision. **S0 engineering foundation delivered; S1–S6 acceptance is not implied.**

## Decisions implemented

Native Windows x86-64, Godot .NET **4.7.2** and C# are the working baseline. SDK **8.0.425**, engine packages and portable download checksums are pinned. Compatibility rendering remains the initial path. Browser, Bevy and custom-renderer studies are retained research and deferred from active execution.

The [experience contract](single-player-contract.md) records two identities, the embodied companion, metric units, 0.30 m player height, Pfluger district/landing scope, targeting, direct Stop, reversible jobs, save versus protect, support/access protection and the dedicated game-only AI boundary. [ADR 0001](../engine/decisions/0001-native-godot-csharp.md) fixes the implementation and compatibility boundaries. The [roadmap](ROADMAP.md) and [backlog](BACKLOG.md) put multiplayer last.

The actual C# project includes a checked metric/body profile, a GDScript interop contract, a compiled native entry point and a narrow adapter invoking the **existing** creation compiler. It neither duplicates creation validation/hashing nor changes the old creation schema. GDScript remains during incremental migration. The legacy main scene and normal application save-directory name remain intact.

Build, test, bootstrap and Windows export helpers are documented in [Native build](../NATIVE-BUILD.md). Downloads install only into ignored repository-local tool directories. Tests use separate application-data paths. No PostgreSQL installation, data deletion or service schema migration was performed.

## Verification

- Official engine, .NET SDK and export-template archives matched pinned SHA-512 checksums. Re-running the bootstrap recognizes installed tools.
- C# Debug build: **0 warnings, 0 errors**.
- Compiled C#–GDScript profile smoke: correct units and 0.30 m default, malformed/nonfinite/unknown values rejected, returned data isolated from caller mutation.
- All **24 retained engine suites** pass with the .NET host, including legacy state/save-envelope round trips, compiler/authority, movement, art, manual invention and travel-adapter faults. The integrated runner also passes the new interop suite and C# compiler probe: **26 checks/suites in total**, not 26 individual assertions.
- Actual Windows release export runs the compiled C# entry/probe, verifies the exact metric profile and compares valid artifacts and invalid errors to direct GDScript compiler results. A built-in probe argument is used because official release templates reject command-line scene overrides.
- A fresh Windows checkout initially exposed a pre-existing source-hash mismatch: the style source requires LF while legacy map JSON hashes require CRLF. Explicit Git attributes preserve both; recorded map/style hashes and saved base pins are not changed to hide the mismatch.

These are development-host correctness/export results, not a frame-rate, low-device, gameplay or art certificate. Database-service/recovery suites were not rerun because the database path/schema did not change. No real AI client, voice path, new-region fidelity, protected-object extensions, dragon or weather implementation is certified by S0.

## Continue with S1

Subsequent founder clarification retired Barton as a destination; the S1 checkpoint supersedes the preservation-of-destination wording below while retaining useful code, assets, saves and temporary regression fixtures.

Build the separate public-source Pfluger package, a tested 0.30 m controller/camera and distinct customizable companion, then integrate the bounded landing route and inspect actual rendered views. Bridge surfaces must remain independent of terrain, with inferred elevations/widths labeled until audited. Preserve all Barton content and regression fixtures.

Founder art/playtest judgment and actual minimum-device tests remain open. Selecting and pairing a real dedicated game-only AI client is needed for the S2 real-AI gate; a development task or mocked response cannot substitute for it. Continue independent implementation while awaiting those inputs.
