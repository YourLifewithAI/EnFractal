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
pwsh -NoProfile -File tools/test-pfluger.ps1
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

The Windows export writes `.cache/releases/windows/EnFractal.exe` and its companion data files. Keep that directory together. The exporter executes an actual release probe of compiled C#, the 0.30 m profile and the retained GDScript compiler with valid/invalid input, then loads the default Pfluger scene. Both run from an empty working directory with developer .NET variables/runtime search paths removed. The resource probe checks that Pfluger is included and the retired Barton map is excluded. This does not certify art or the minimum device. Builds are currently unsigned development artifacts.

## Active small-avatar preview

Run `pwsh -NoProfile -File run-pfluger.ps1`. The default Godot entry and native export open the same Pfluger scene. WASD moves, Shift runs, Space jumps, R recovers, F1/F2/F3 select eye/follow/reference views, C customizes the two avatars, and 1–5 direct the companion. The companion is currently deterministic local behavior, with no AI login or connection. Profile preferences persist independently of old world saves.

`tools/test-pfluger.ps1` checks small-body movement and walks the full 150 m source route through normal controls. `tools/test-pfluger.ps1 -Capture` runs the short scene/render checks and writes actual GPU views to `docs/images/pfluger-*.png`; it does not replace the full headless traversal. Both use the helper's isolated test application data. `run-pfluger.ps1` uses normal player application data. `run-map.ps1` explicitly selects the retired Barton development fixture; that map is absent from release packages.
