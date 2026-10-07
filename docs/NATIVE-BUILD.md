# Native Godot .NET build

The selected foundation is **Godot .NET 4.7.2 x64 with .NET SDK 8.0.425** on Windows. Keep the entire Godot .NET distribution together: the executable requires its adjacent `GodotSharp` directory. The regular Godot executable has no C# support. A .NET runtime without the SDK cannot compile this project.

The first C# addition is incremental: `game/native/WorldScaleProfile.cs` holds an engine-independent meter/body contract, and `NativeWorldContract.cs` exposes a narrow checked dictionary interface to existing GDScript. Existing GDScript scenes, controller and geographic code remain available. Adding the C# project does not itself migrate gameplay or prove performance.

## Bootstrap on either development machine

Run `pwsh -NoProfile -File tools/bootstrap-native.ps1 -IncludeExportTemplates` for checksum-verified portable tools, then the build/test commands below. This installs only under ignored `.cache`, with no system installer. Export templates are a large download and can be omitted until exporting. Manual setup is also supported:

1. Obtain the Windows x64 **.NET** archive from the [official Godot 4.7.2 archive](https://godotengine.org/download/archive/4.7.2-stable/) and extract the full distribution below `.cache/godot/`. The expected console executable is `.cache/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`.
2. Obtain the Windows x64 .NET SDK **8.0.425** archive from [Microsoft's .NET 8 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), verify it against Microsoft's release metadata/checksum, and extract it below `.cache/dotnet/`. The expected host is `.cache/dotnet/dotnet.exe`. No system installer is required.
3. Run the following from the repository with PowerShell 7. The first build restores the Godot NuGet packages and needs access to the configured NuGet source. Later builds can use the local package cache.

```powershell
pwsh -NoProfile -File tools/build-native.ps1
pwsh -NoProfile -File tools/test-native-interop.ps1
pwsh -NoProfile -File run-engine-tests.ps1
pwsh -NoProfile -File tools/test-room.ps1
pwsh -NoProfile -File tools/export-native.ps1
```

`game/global.json` pins the SDK exactly; `game/EnFractal.csproj` pins `Godot.NET.Sdk/4.7.2` and targets `net8.0`. Upgrade them deliberately with the editor/package versions, then run the native and retained regression checks. `game/EnFractal.sln` is available to IDEs. The helpers print the verified tool versions and keep .NET CLI/NuGet caches inside the ignored `.cache/` directory; Godot's generated managed output stays in ignored `game/.godot/`.

Tools installed elsewhere can be selected per process:

```powershell
$env:ENFRACTAL_GODOT_DOTNET = 'C:\Tools\Godot\Godot_v4.7.2-stable_mono_win64_console.exe'
$env:ENFRACTAL_DOTNET = 'C:\Tools\dotnet\dotnet.exe'
pwsh -NoProfile -File tools/test-native-interop.ps1
```

An existing `ENFRACTAL_GODOT` is accepted only if it points to the correct .NET build. Explicit invalid selections fail instead of silently choosing another engine. Repository `.cache` candidates are considered before PATH; runtime-only `dotnet` installations fail with a SDK-specific message.

## Calling the C# contract

```gdscript
var contract = load("res://native/NativeWorldContract.cs").new()
var dimensions: Dictionary = contract.call("GetDefaultProfile")
assert(contract.call("ValidateProfile", dimensions))
```

The version-1 dictionary includes `meters_per_world_unit`, `height_m`, `radius_m`, `eye_height_m` and `interaction_reach_m`. The default body is 0.30 m high, radius 0.06 m, eye height 0.26 m and interaction reach 0.45 m, with **one world unit equal to one meter**. It does not rescale geography or gravity. These are initial body parameters, not a claim that movement is already tuned. Unknown fields, unsupported schema versions, nonnumeric/nonfinite values and invalid body dimensions are rejected. Every returned dictionary is a fresh copy.

The interop smoke test runs through the compiled Godot C# bindings rather than reproducing the C# code in another language. It verifies the GDScript round trip, units, malformed values, capsule/eye bounds and isolation from caller mutation. The discovery function in `tools/native-toolchain.ps1` is reusable by the main launcher and test runner. `Invoke-EnfractalNativeProcess` is intended for build/test automation: it uses an explicit argument list, a hidden child process, a timeout, checkout-local caches and isolated application-data paths under `.cache/native-test/`. It does not read or overwrite the player's normal saves.

## Verified upstream requirements

Checked on 2026-10-02: the [official Windows download page](https://godotengine.org/download/windows/) lists stable .NET 4.7.2, and [NuGet publishes matching Godot.NET.Sdk 4.7.2](https://www.nuget.org/packages/Godot.NET.Sdk/4.7.2). The [tagged project generator](https://raw.githubusercontent.com/godotengine/godot/4.7.2-stable/modules/mono/editor/GodotTools/GodotTools.ProjectEditor/ProjectGenerator.cs) specifies `net8.0` for desktop projects. The [.NET prerequisites](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html) require the SDK separately from the .NET-enabled editor. The selected patch was verified against Microsoft's release metadata when bootstrapping this checkout.

Windows x64 is the verified bootstrap target. Other native platforms need their matching Godot .NET and SDK packages plus export validation; this file does not claim they were tested. The native-only platform decision does not change source-data license or asset attribution requirements.

The Windows export writes `.cache/releases/windows/EnFractal.exe` and its companion data files. Keep that directory together. The exporter executes an actual release probe of compiled C#, the 0.30 m profile and the retained GDScript compiler with valid/invalid input, then loads the default placeholder room scene. Both run from an empty working directory with developer .NET variables/runtime search paths removed. This does not certify art or the minimum device. Builds are currently unsigned development artifacts.

## The room scene

Run `pwsh -NoProfile -File run-room.ps1`. The default Godot entry and the native export open `scenes/room.tscn`, whose `RoomWorld` loads a room from data: `game/rooms/test_room/room.json` and its asset files, verified by hash at load time, styled by the room's pinned preset or the default `game/styles/storybook_painterly/v1.json` (presets are versioned files; a pin that does not verify falls back to the default with a visible notice). The test room is a hand-built 4 × 3 × 2.4 m box with furniture-sized proxies, generated by `tools/rooms/build_test_room.py`; it is not a captured or styled space. Pass `-- --room=<room_id>` to open another room; a bare id looks in `user://rooms/` before `res://rooms/`. WASD moves, Shift runs, Space jumps, R recovers, F1/F2/F3 select the eye, over-the-shoulder and diorama views (in F3 the mouse orbits, the wheel zooms and W A S D move relative to the view), G cycles gravity, C customizes the two avatars, and 1–5 direct the companion. The companion is deterministic local behavior with no AI connection; it finds its way round furniture on a navigation mesh baked from the room's collision. Appearance preferences persist under `user://single_player/room/`.

`tools/test-room.ps1` runs the small-avatar physics fixture (`tests/native_small_avatar.tscn`, the 0.10 m body), the room navigation fixture (`tests/native_room_navigation.tscn`: the companion routes round the big box through the command host), the room data fixture (`tests/native_room_data.tscn`: contract loading, hash and path rejection, derived scene, collision, look seam and a full room boot), and then boots the default room headlessly, requiring `ROOM_WORLD_READY`. `run-engine-tests.ps1` runs the retained GDScript kernel suites and the release probe, which also verifies the shipped room and style hashes. `tools/check-run1-readiness.ps1` reports, read-only, what this machine still needs for each Run 1 lane.

## Linux cloud sessions

`tools/linux/setup-toolchain.sh` installs the pinned Linux Godot .NET 4.7.2 and .NET SDK 8.0.425 (checksums in `tools/native-toolchain.lock.json`) and the contract validator's Python environment under `.cache/linux/`. `tools/linux/test-all.sh` then runs everything the Windows runners check, headless, with C# warnings treated as errors, plus the contract tests. Headless Linux renders nothing, so captures and frame timings still need a GPU machine.

The geography-era scenes, map packages and their tests were removed in the room-scale cleanup; see `docs/CLEANUP-PLAN.md`. Everything is restorable from the `geography-era-final` branch.
