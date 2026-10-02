$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $modernShell = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue
    if (-not $modernShell) {
        throw 'PowerShell 7 or newer is required for reliable Godot process exit-code reporting.'
    }
    & $modernShell.Source -NoProfile -File $PSCommandPath
    exit $LASTEXITCODE
}

# Native baseline: build C# before running retained GDScript regressions.
. (Join-Path $PSScriptRoot 'tools/native-toolchain.ps1')
$nativeToolchain = Get-EnfractalNativeToolchain
Build-EnfractalNativeProject -Toolchain $nativeToolchain
$projectPath = $nativeToolchain.ProjectPath
$enginePath = $nativeToolchain.EnginePath
# Keep regression saves independent from the player's worlds on either machine.
$env:DOTNET_ROOT = Split-Path -Parent $nativeToolchain.DotnetPath
$env:PATH = $env:DOTNET_ROOT + [System.IO.Path]::PathSeparator + $env:PATH
$env:APPDATA = Join-Path $PSScriptRoot '.cache/engine-tests/roaming'
$env:LOCALAPPDATA = Join-Path $PSScriptRoot '.cache/engine-tests/local'
New-Item -ItemType Directory -Force $env:APPDATA,$env:LOCALAPPDATA | Out-Null

& (Join-Path $PSScriptRoot 'tools\ensure-godot-art-imports.ps1') -EnginePath $enginePath -ProjectPath $projectPath

$tests = @(
    'native_interop_smoke.gd',
    'map_runtime_smoke.gd',
    'terrain_seam_smoke.gd',
    'visual_streaming_smoke.gd',
    'terrain_residency_smoke.gd',
    'terrain_loading_input_smoke.gd',
    'test_world_state.gd',
    'test_creation_ops.gd',
    'movement_physics_smoke.gd',
    'world_physics_smoke.gd',
    'path_platform_join_smoke.gd',
    'workshop_integration.gd',
    'workshop_path_integration.gd',
    'art_reference_smoke.gd',
    'workshop_art_smoke.gd',
    'painterly_geometry_smoke.gd',
    'creation_compiler_smoke.gd',
    'creation_authority_smoke.gd',
    'creation_visuals_smoke.gd',
    'invention_runtime_smoke.gd',
    'invention_editor_smoke.gd',
    'manual_invention_integration.gd',
    'durable_creation_smoke.gd',
    'travel_panel_smoke.gd',
    'travel_adapter_faults.gd'
)
foreach ($test in $tests) {
    $result = Invoke-EnfractalNativeProcess -Toolchain $nativeToolchain -FilePath $enginePath `
        -Arguments @('--headless', '--path', $projectPath, '--script', "res://tests/$test") -TimeoutSeconds 30
    $exitCode = $result.ExitCode
    $stdout = $result.Stdout
    $stderr = $result.Stderr
    if ($null -eq $exitCode -or $exitCode -ne 0 -or -not [string]::IsNullOrWhiteSpace($stderr)) {
        Write-Output $stdout
        Write-Output $stderr
        throw "$test failed with exit code $exitCode."
    }
    Write-Output ($stdout.Trim().Split("`n") | Select-Object -Last 1)
}

$probe = Invoke-EnfractalNativeProcess -Toolchain $nativeToolchain -FilePath $enginePath `
    -Arguments @('--headless', '--path', $projectPath, 'res://scenes/native_contract_probe.tscn') -TimeoutSeconds 30
if ($probe.Stderr -or $probe.Stdout -notmatch 'Native release probe passed:') {
    throw "Native compiler probe failed:`n$($probe.Stdout)`n$($probe.Stderr)"
}
Write-Output ($probe.Stdout.Trim().Split("`n") | Select-Object -Last 1)
