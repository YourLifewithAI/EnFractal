# E03 engine feasibility evidence — first local probe

**Observed 1 October 2026 on the development laptop.** This is a bounded phase 0 probe, not acceptance of the full E03 stack gate or the phase 6 performance gate. It uses the current Barton Creek scene and a hidden window. The project remains on Godot provisionally; this probe found no reason to start a Unity challenger, but it does not settle the proposed C# production choice.

## Reproduce

From the repository root on Windows, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\engine_probe\run.ps1
```

Set `ENFRACTAL_GODOT` or pass `-GodotPath` if the pinned engine is elsewhere. Add `-SkipGraphics` for a headless-only smoke. The script creates timestamped raw stdout, stderr, `summary.json`, source-file SHA-256 manifests, and a PNG under `tools/engine_probe/results/`; that directory is ignored by Git. It requires the exact `4.7.2.stable.official.ed1daf0bf` revision, launches the engine with no visible helper window, times each process, samples memory every 100 ms, and rejects nonzero exits, stderr, Godot error markers, missing success markers, a missing render capture, or game source changes during the probe. The render probe waits for terrain LOD residency, then takes 180 main-loop interval samples after 30 additional warm frames. Its source is [`engine_render_probe.gd`](../../../game/tests/engine_render_probe.gd).

Add `-IncludeMovingRoute` to run the supplemental [`engine_route_probe.gd`](../../../game/tests/engine_route_probe.gd): two 1.7 km rapid camera sweeps across terrain tiles at a 60 fps cap, with a residency wait between passes. It records queue length and worker/mesh-commit peaks. This is a visual streaming stress, not normal avatar motion or a minimum-device benchmark.

To recreate the export pair from the current working game, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\engine_probe\export.ps1
```

Add `-SnapshotRef HEAD` to export the last committed `game/` tree instead of the live working tree. [`export.ps1`](../../../tools/engine_probe/export.ps1) verifies the official standard Godot 4.7.2 templates archive by exact byte count and SHA-256 against [Godot's release asset metadata](https://api.github.com/repos/godotengine/godot-builds/releases/tags/4.7.2-stable), extracts only the Windows/Linux x86_64 release binaries to ignored `.cache/engine_probe/`, then imports and exports a separate snapshot project. The first run needs the 1,281,349,702-byte archive (about 1.19 GiB); pass `-DownloadTemplates` to fetch it from the [official release](https://github.com/godotengine/godot-builds/releases/tag/4.7.2-stable). No account, hosted service or recurring bill is involved. The Windows and Linux PCKs have distinct names so one export cannot overwrite the other.

The script also requires the exact editor revision, checks SHA-256 for the cached extracted templates, excludes Godot's transient `.godot` cache from a working-tree snapshot, and writes `source-files.sha256` plus the source-manifest and artifact hashes into `export-summary.json`. This identifies the exact exported files even when agents have uncommitted changes. To run both release artifacts from a generated export directory, use:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\engine_probe\smoke-export.ps1 -ExportDirectory <export directory>
```

[`smoke-export.ps1`](../../../tools/engine_probe/smoke-export.ps1) captures the Windows build in a hidden window and runs the Linux build in Ubuntu WSL with display variables unset. It verifies map startup, renderer selection, a saved PNG and clean exits. Use `-SkipLinux` on a machine without that WSL distro. Its screenshot metric is only a smoke check; the fixed-frame capture may provide too few settled samples to assess frame pacing.

## What passed on this machine

| Evidence | Observation |
|---|---|
| Installed engine and renderer | Standard Godot `4.7.2.stable.official.ed1daf0bf`; project selects `gl_compatibility`. Render log identified OpenGL 3.3 Compatibility on an NVIDIA RTX 2070 Super, driver 576.80. |
| Machine | Intel i7-10875H, 31.79 GiB physical RAM; both RTX 2070 Super and Intel UHD adapters are present. The render log confirms the **NVIDIA** adapter was selected. |
| Headless simulation | `movement_physics_smoke.gd` exited 0 with empty stderr. It exercised terrain seam, bounded collision ring, safe spawn, walk, jump, glide, recovery, and free-fly toggle. Its reported initial 3 × 3 collision activation was 35 ms in this run. |
| Native Windows graphics smoke | The same map rendered at 1280 × 720; a PNG was saved after all requested visual LOD jobs finished. The Godot process exited 0 with empty stderr. |
| Warm stationary frame intervals | **180** post-LOD main-loop samples: p50 **6.94 ms**, p95 **7.18 ms**, p99 **14.03 ms**, maximum **20.93 ms**. The LOD queue reached zero; 30 stable frames and 30 further warm frames preceded sampling. |
| Process memory during this short run | Headless sampled peak working set **193.6 MiB** and private bytes **144.0 MiB**; graphics sampled peak working set **307.6 MiB** and private bytes **397.1 MiB**. |
| Release export pair | A stable `HEAD` snapshot (`a746f5c`) and later working-tree snapshots imported and exported without Godot errors. The final probe and working-tree export shared the same **60 source files** and source-manifest SHA-256 `6f84bd11eaf25f49fe745974b18dc1fccf0f138bcb97c54229e4514e538ba69a`; the probe verified no source changes during measurement. Its artifacts were a **109.1 MB Windows executable + 11.7 MB PCK** and a **73.5 MB Linux x86_64 executable + 11.7 MB PCK**; exact artifact hashes are in its ignored `export-summary.json`. These identify the working snapshot but do not create a durable Git ref until committed. |
| Windows release run | The exported Windows executable loaded the map and saved a 1280 × 720 PNG under the Compatibility renderer on the RTX 2070 Super; it exited 0 without stderr. This was visually inspected. |
| Linux release run | The exported Linux executable loaded the map and exited 0 under Ubuntu WSL2 with `DISPLAY` and `WAYLAND_DISPLAY` unset, and **without** an explicit `--headless` flag. This verifies the dedicated-server export tag's basic no-display behavior. It does not measure a VPS or authority loop. |
| Supplemental moving route | On the development RTX at 1280 × 720 and a 60 fps cap, two passes of 360 frame-post-draw intervals crossed local X/Z `(0,350)` to `(1536,-400)` and back. The first pass with unrefined LOD had p95 **19.91 ms**, p99 **29.27 ms**, max **58.98 ms**, three intervals over 33.3 ms and one over 50 ms; queue peak 24. After residency settled, the return pass had p95 **17.64 ms**, p99 **23.11 ms**, max **31.06 ms**, no intervals over 33.3 ms; queue peak 8. Peak measured mesh commit was **17.35 ms**. |

The matching probe is in ignored `tools/engine_probe/results/20261001-204241/`; the integrated export and smoke evidence is in ignored `tools/engine_probe/results/export-20261001-204341/`. Artifact SHA-256 values for that export are Windows EXE `4a9eaded8955ef789ab02651ed9d2dde80328fbb342bd2a6db4db33e86305668`, Windows PCK `db45118bd1628604c9f5d94793bbf43e4e4620d495058d9ecf27e0cd9cf6adbb`, Linux executable `d9f79ab89b5ae369aeed11c6052d402e8218cd503bf85b4a235f9c30c46a7c63`, and Linux PCK `3d549c744fa99accbaf5dd41831500fde49b1cef6aa6b5985087aaf784c390aa`.

The full graphics process took about 6.6 seconds, including project load, initial scene creation, LOD work, sampling, and screenshot. The render fixture reported 1.39 seconds from scene attachment to stable LOD residency; that figure excludes work done during scene attachment. The frame statistics are **main-loop callback intervals in a hidden, stationary window**, not measured display presentation, GPU time, or a traversal of the map. The 100 ms process sampling can miss shorter memory peaks. A previous fixed-frame screenshot yielded only four post-LOD samples, so its apparent p95 was rejected; the new probe explicitly waits for residency and samples 180 frames. These stationary values alone do not satisfy the roadmap's moving-route or minimum-device frame gate.

The supplemental route completed and returned to zero pending LOD jobs. Its initial scene attachment took about **1.17 seconds** before the first route sample; that is startup work, not a frame-time sample. Its two passes load/render different visual LODs; the first is not a cold-disk run, and the second is not a fully cached repetition because the return path changes tile LOD again. The samples are frame-post-draw callback intervals, not display-present timestamps. The 58.98 ms maximum and 17.35 ms mesh commit indicate visible-hitch risk despite the off-thread mesh build; the low-device gate remains open.

## Gates still open

| Roadmap E03 question | Status and next evidence |
|---|---|
| Native Windows release export | **Basic gate passed.** The artifact builds, loads the Barton map and renders a capture. Release frame pacing, sustained traversal and memory still need their own measurements. |
| Linux headless release export | **Basic gate passed in WSL2.** The dedicated-server artifact starts with no display and loads the map. A real Linux host, server tick/command stress, and resource use under its host budget remain untested. Godot documents that an export template is the preferred server runtime over the larger editor binary. [Dedicated-server export guide](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_dedicated_servers.html). |
| Godot/C# maintainability | **Open.** The `.NET` CLI is absent, and this working slice is GDScript. Install the supported SDK and matching Godot .NET editor, then implement and debug one representative shared-contract/terrain-capsule change before committing to C#. |
| Low-hardware graphics | **Open.** This process chose the RTX 2070 Super, not the Intel UHD adapter or the proposed 8 GiB integrated-graphics target. Godot's Compatibility renderer does not support `--gpu-index` selection; the supplemental Intel probe could not be run without an OS preference change, which automatic approval review rejected. Obtain a real minimum-profile machine and repeat a moving-route test at 1280 × 720; record presented-frame pacing, CPU/GPU load, GPU/shared memory, and temperature over a sustained run. [Godot OpenGL adapter selection limitation](https://github.com/godotengine/godot/blob/master/drivers/gles3/rasterizer_gles3.cpp). |
| Representative stress and transport | **Open.** A stationary pin view is not maximum props/effects or gliding across streamed tiles. Exercise churn and a moving camera, then verify the planned authenticated and encrypted transport path before exposing multiplayer. |

The next narrow gate is a release-artifact moving-route/stress measurement and a real minimum-device run, then the C# maintainability and authenticated transport experiments. Keep Godot as the working candidate and GDScript as a fixture implementation, without claiming the production stack or 8.5/10 quality threshold has been met. Godot's export runner ignores the editor's `--script` test harness in these artifacts, so release checks must use the game's built-in capture or an explicit in-game probe entry point; `--quit-after` is sufficient for the basic no-display load check.

An independent probe review rated the **probe quality 8/10** and **E03 acceptance readiness 4/10** after checking the source-file and artifact hashes, release logs, and screenshot. The score reflects the open C#, transport, minimum-device, and server stress gates above; the user-requested 8.5/10 quality target is not yet met for E03.
