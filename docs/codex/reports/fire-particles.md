# Natural fire pass — 12 October 2026

Founder-requested continuation on `codex/fire-and-journal`; uncommitted. Replaced the outlined polygon tongues with 48 flame, 12 smoke and 16 ember GPU particles per host effect. Flame/embers use additive blending; smoke uses ordinary alpha blending. Advected procedural noise breaks up sprite edges; an incandescent color ramp cools with particle age. Light strength was reduced to preserve the flame silhouette. Gameplay commands and authority are unchanged.

Research and choice:

- [Godot GPU particles](https://docs.godotengine.org/en/stable/tutorials/3d/particles/creating_a_3d_particle_system.html) provide the native runtime. Fixed particle counts bound the effect; expensive built-in 3D turbulence is disabled, using animated noise and small lateral drift instead.
- [EmberGen](https://docs.jangafx.com/embergen/pages/getting_started.html) can bake simulated fire to flipbooks. It remains an alternative for authored effects; no installation, license purchase or external assets were needed here.
- [Volumetric fog](https://docs.godotengine.org/en/stable/tutorials/3d/volumetric_fog.html) has a spatial-resolution/performance tradeoff. A tiny flame does not justify raising the whole scene's fog resolution in this pass.
- [Navier–Stokes fluid simulation](https://developer.nvidia.com/gpugems/gpugems3/part-v-physics-simulation/chapter-30-real-time-simulation-and-rendering-3d-fluids) and [blackbody spectra](https://www.pbr-book.org/4ed/Radiometry%2C_Spectra%2C_and_Color/Representing_Spectral_Distributions) are not implemented. The motion and color are visual approximations.

The first shader incorrectly wrote EMISSION in unshaded mode, producing light without a flame. A solid-material comparison isolated it; switching the output to ALBEDO fixed it, consistent with [Godot's unshaded render mode](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/spatial_shader.html). The island fixture now compares rendered pixels with its light disabled, then particles disabled, to catch invisible fire.

Captures and raw GPU log: `.cache/fire-natural/`, `.cache/fire-natural-render.log`. The generated synthetic garage island is used, with isolated test saves. Burning, damage, volumetric self-shadowing and measured worst-case GPU timings remain unimplemented/unverified. Founder aesthetic acceptance remains open. No contract changes or external change requests; no paid APIs or rented GPU time.

```text
Build-EnfractalNativeProject: exit 0; 0 Warning(s), 0 Error(s)
native_fire_island.tscn --island=<generated garage> --capture-dir=<cache>: exit 0
FIRE_ISLAND: 14/14 checks passed
run-engine-tests.ps1: exit 0 (.cache/fire-particles-engine.log)
NATIVE_KERNEL_PLAY_HUD: 126/126 checks passed
companion: Ran 625 tests in 153.644s
tools/test-room.ps1: exit 0 (.cache/fire-particles-room.log)
NATIVE_SMALL_AVATAR: 254/254 checks passed
NATIVE_ROOM_NAVIGATION: 14/14 checks passed
NATIVE_KERNEL_LANDSCAPE: 42/42 checks passed
NATIVE_ROOM_DATA: 56/56 checks passed
NATIVE_LOOK: 1002/1002 checks passed
```
