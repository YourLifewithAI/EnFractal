$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $modernShell = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue
    if (-not $modernShell) {
        throw 'PowerShell 7 or newer is required for reliable Godot process exit-code reporting.'
    }
    & $modernShell.Source -NoProfile -File $PSCommandPath
    exit $LASTEXITCODE
}

$projectPath = Join-Path $PSScriptRoot 'game'
$installedGodot = Join-Path $env:USERPROFILE 'Downloads\Godot_v4.7.2-stable_win64.exe\Godot_v4.7.2-stable_win64.exe'
$portableGodot = Join-Path $PSScriptRoot '.cache\godot\Godot_v4.7.2-stable_win64.exe'
if ($env:ENFRACTAL_GODOT -and (Test-Path -LiteralPath $env:ENFRACTAL_GODOT -PathType Leaf)) {
    $enginePath = $env:ENFRACTAL_GODOT
} elseif (Test-Path -LiteralPath $installedGodot -PathType Leaf) {
    $enginePath = $installedGodot
} elseif (Test-Path -LiteralPath $portableGodot -PathType Leaf) {
    $enginePath = $portableGodot
} else {
    $engine = Get-Command godot,godot4 -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $engine) {
        throw 'Godot 4.7.2 was not found. Set ENFRACTAL_GODOT to its executable path.'
    }
    $enginePath = $engine.Source
}

& (Join-Path $PSScriptRoot 'tools\ensure-godot-art-imports.ps1') -EnginePath $enginePath -ProjectPath $projectPath

$tests = @(
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
    $name = [System.IO.Path]::GetFileNameWithoutExtension($test)
    $stdoutPath = Join-Path $env:TEMP "enfractal-$name-$PID.out.txt"
    $stderrPath = Join-Path $env:TEMP "enfractal-$name-$PID.err.txt"
    $process = Start-Process -FilePath $enginePath -ArgumentList @('--headless', '--path', $projectPath, '--script', "res://tests/$test") -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    if (-not $process.WaitForExit(30000)) {
        Stop-Process -Id $process.Id -Force
        throw "$test timed out after 30 seconds."
    }
    $process.Refresh()
    $exitCode = $process.ExitCode
    $stdout = Get-Content -LiteralPath $stdoutPath -Raw
    $stderr = Get-Content -LiteralPath $stderrPath -Raw
    if ($null -eq $exitCode -or $exitCode -ne 0 -or -not [string]::IsNullOrWhiteSpace($stderr)) {
        Write-Output $stdout
        Write-Output $stderr
        throw "$test failed with exit code $exitCode."
    }
    Write-Output ($stdout.Trim().Split("`n") | Select-Object -Last 1)
}
