param(
    [string]$GodotPath = '',
    [string]$OutputDirectory = '',
    [string]$ExpectedVersion = '4.7.2.stable.official.ed1daf0bf',
    [switch]$IncludeMovingRoute,
    [switch]$SkipGraphics
)

$ErrorActionPreference = 'Stop'
$projectPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\game')).Path
if (-not $GodotPath) {
    if ($env:ENFRACTAL_GODOT) {
        $GodotPath = $env:ENFRACTAL_GODOT
    } else {
        $GodotPath = Join-Path $env:USERPROFILE 'Downloads\Godot_v4.7.2-stable_win64.exe\Godot_v4.7.2-stable_win64.exe'
    }
}
if (-not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    throw "Godot executable not found: $GodotPath. Pass -GodotPath or set ENFRACTAL_GODOT."
}
if ($SkipGraphics -and $IncludeMovingRoute) { throw '-IncludeMovingRoute requires graphics; remove -SkipGraphics.' }
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot ('results\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path

function Get-SourceManifest {
    param([string]$Destination)
    $sourceFiles = @(Get-ChildItem -LiteralPath $projectPath -Force | Where-Object { $_.Name -ne '.godot' } | ForEach-Object {
        Get-ChildItem -LiteralPath $_.FullName -Recurse -File -Force
    } | Sort-Object FullName)
    $sourceLines = @($sourceFiles | ForEach-Object {
        $relative = $_.FullName.Substring($projectPath.Length + 1).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    })
    [System.IO.File]::WriteAllLines($Destination, $sourceLines, (New-Object System.Text.UTF8Encoding($false)))
    return [ordered]@{
        file_count = $sourceLines.Count
        sha256 = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
        path = $Destination
    }
}

$sourceBefore = Get-SourceManifest -Destination (Join-Path $OutputDirectory 'source-files-before.sha256')

function Invoke-Probe {
    param(
        [string]$Name,
        [string[]]$Arguments,
        [int]$TimeoutSeconds = 90,
        [string]$ExpectedText = ''
    )
    $stdoutPath = Join-Path $OutputDirectory "$Name.stdout.txt"
    $stderrPath = Join-Path $OutputDirectory "$Name.stderr.txt"
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $GodotPath
    $startInfo.Arguments = $Arguments -join ' '
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $peakWorkingSet = [int64]0
    $peakPrivateBytes = [int64]0
    $timedOut = $false
    while ($true) {
        if ($process.WaitForExit(100)) { break }
        $process.Refresh()
        try {
            $peakWorkingSet = [Math]::Max($peakWorkingSet, $process.WorkingSet64)
            $peakPrivateBytes = [Math]::Max($peakPrivateBytes, $process.PrivateMemorySize64)
        } catch {
            # Process exit between WaitForExit and sample is normal.
        }
        if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
            $timedOut = $true
            $process.Kill()
            $process.WaitForExit()
            break
        }
    }
    $process.WaitForExit() # Complete redirected stream readers before consulting ExitCode.
    $process.Refresh()
    $timer.Stop()
    $stdout = $stdoutTask.Result
    $stderr = $stderrTask.Result
    Set-Content -LiteralPath $stdoutPath -Value $stdout -Encoding utf8
    Set-Content -LiteralPath $stderrPath -Value $stderr -Encoding utf8
    return [ordered]@{
        name = $Name
        arguments = $Arguments
        exit_code = $process.ExitCode
        timed_out = $timedOut
        elapsed_ms = [Math]::Round($timer.Elapsed.TotalMilliseconds)
        sampled_peak_working_set_mib = [Math]::Round($peakWorkingSet / 1MB, 1)
        sampled_peak_private_bytes_mib = [Math]::Round($peakPrivateBytes / 1MB, 1)
        stdout_path = $stdoutPath
        stderr_path = $stderrPath
        stderr_nonempty = -not [string]::IsNullOrWhiteSpace($stderr)
        error_marker = ($stdout + $stderr) -match '(?m)^(SCRIPT ERROR|ERROR:)'
        expected_text_seen = (-not $ExpectedText) -or $stdout.Contains($ExpectedText)
    }
}

$quotedProject = '"' + $projectPath + '"'
$version = Invoke-Probe -Name 'version' -Arguments @('--headless', '--version') -TimeoutSeconds 20
$headless = Invoke-Probe -Name 'headless-movement' -Arguments @('--headless', '--path', $quotedProject, '--script', 'res://tests/movement_physics_smoke.gd') -ExpectedText 'Movement smoke test passed'

$graphics = $null
$captureMetrics = $null
$movingRoute = $null
$movingRouteMetrics = $null
$capturePath = Join-Path $OutputDirectory 'pin-capture.png'
if (-not $SkipGraphics) {
    $oldCapture = $env:ENFRACTAL_PROBE_CAPTURE
    $oldMetrics = $env:ENFRACTAL_PROBE_METRICS
    $oldView = $env:ENFRACTAL_VIEW
    try {
        $env:ENFRACTAL_PROBE_CAPTURE = $capturePath
        $env:ENFRACTAL_PROBE_METRICS = Join-Path $OutputDirectory 'pin-capture-metrics.json'
        $env:ENFRACTAL_VIEW = 'pin'
        $graphics = Invoke-Probe -Name 'graphics-pin' -Arguments @('--path', $quotedProject, '--script', 'res://tests/engine_render_probe.gd') -ExpectedText 'Engine render probe passed'
        if (Test-Path -LiteralPath $env:ENFRACTAL_PROBE_METRICS -PathType Leaf) {
            $captureMetrics = Get-Content -LiteralPath $env:ENFRACTAL_PROBE_METRICS -Raw | ConvertFrom-Json
        }
    } finally {
        $env:ENFRACTAL_PROBE_CAPTURE = $oldCapture
        $env:ENFRACTAL_PROBE_METRICS = $oldMetrics
        $env:ENFRACTAL_VIEW = $oldView
    }
}

if ($IncludeMovingRoute) {
    $oldRoutePath = $env:ENFRACTAL_PROBE_ROUTE_METRICS
    $oldView = $env:ENFRACTAL_VIEW
    try {
        $env:ENFRACTAL_PROBE_ROUTE_METRICS = Join-Path $OutputDirectory 'moving-route-metrics.json'
        $env:ENFRACTAL_VIEW = 'pin'
        $movingRoute = Invoke-Probe -Name 'graphics-route' -Arguments @('--path', $quotedProject, '--script', 'res://tests/engine_route_probe.gd') -ExpectedText 'Engine route probe passed'
        if (Test-Path -LiteralPath $env:ENFRACTAL_PROBE_ROUTE_METRICS -PathType Leaf) {
            $movingRouteMetrics = Get-Content -LiteralPath $env:ENFRACTAL_PROBE_ROUTE_METRICS -Raw | ConvertFrom-Json
        }
    } finally {
        $env:ENFRACTAL_PROBE_ROUTE_METRICS = $oldRoutePath
        $env:ENFRACTAL_VIEW = $oldView
    }
}

$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$computer = Get-CimInstance Win32_ComputerSystem
$gpu = @(Get-CimInstance Win32_VideoController | ForEach-Object { $_.Name })
$godotVersion = (Get-Content -LiteralPath $version.stdout_path -Raw).Trim()
$projectConfig = Get-Content -LiteralPath (Join-Path $projectPath 'project.godot') -Raw
$rendererMatch = [regex]::Match($projectConfig, '(?m)^renderer/rendering_method="([^"]+)"')
$rendererSetting = if ($rendererMatch.Success) { $rendererMatch.Groups[1].Value } else { '' }
$rendererObserved = ''
if ($graphics) {
    $graphicsLog = Get-Content -LiteralPath $graphics.stdout_path -Raw
    $rendererObserved = @($graphicsLog -split "`r?`n" | Where-Object { $_ -match 'Compatibility - Using Device:' } | Select-Object -First 1)[0]
}
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$templateRoot = Join-Path $env:APPDATA 'Godot\export_templates'
$templates = @()
if (Test-Path -LiteralPath $templateRoot -PathType Container) {
    $templates = @(Get-ChildItem -LiteralPath $templateRoot -Directory | ForEach-Object { $_.Name })
}
$sourceAfter = Get-SourceManifest -Destination (Join-Path $OutputDirectory 'source-files-after.sha256')
$summary = [ordered]@{
    timestamp_utc = (Get-Date).ToUniversalTime().ToString('o')
    godot_executable = (Resolve-Path -LiteralPath $GodotPath).Path
    godot_version = $godotVersion
    project = $projectPath
    expected_godot_version = $ExpectedVersion
    version_matches_expected = $godotVersion -eq $ExpectedVersion
    source_file_count = $sourceBefore.file_count
    source_manifest_sha256 = $sourceBefore.sha256
    source_manifest_path = $sourceBefore.path
    source_unchanged_during_probe = $sourceBefore.sha256 -eq $sourceAfter.sha256
    source_manifest_after_sha256 = $sourceAfter.sha256
    source_manifest_after_path = $sourceAfter.path
    renderer_setting = $rendererSetting
    renderer_observed = $rendererObserved
    cpu = $cpu.Name
    physical_memory_gib = [Math]::Round($computer.TotalPhysicalMemory / 1GB, 2)
    graphics_adapters = $gpu
    dotnet_cli_available = [bool]$dotnet
    installed_export_template_versions = $templates
    version_probe = $version
    headless_movement_probe = $headless
    graphics_pin_probe = $graphics
    graphics_capture_metrics = $captureMetrics
    graphics_timing_eligible = ($null -ne $captureMetrics -and $captureMetrics.sample_count -ge 100 -and $captureMetrics.remaining_lod_tiles -eq 0)
    moving_route_probe = $movingRoute
    moving_route_metrics = $movingRouteMetrics
    capture_path = if (Test-Path -LiteralPath $capturePath -PathType Leaf) { $capturePath } else { $null }
    caveat = 'Hidden-window development-laptop diagnostics. Process memory is sampled at 100 ms and can miss shorter peaks. Stationary intervals are main-loop callbacks after LOD settles; optional route intervals are frame-post-draw callbacks during rapid camera travel. Neither is display presentation timing. Export, C#, and 8 GiB iGPU certification require separate checks.'
}
$summaryPath = Join-Path $OutputDirectory 'summary.json'
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $summaryPath -Encoding utf8
$summary | ConvertTo-Json -Depth 12
if ($version.exit_code -ne 0 -or $version.stderr_nonempty -or $version.error_marker -or $version.timed_out -or [string]::IsNullOrWhiteSpace($godotVersion) -or -not $summary.version_matches_expected -or -not $summary.source_unchanged_during_probe -or $rendererSetting -ne 'gl_compatibility' -or $headless.exit_code -ne 0 -or $headless.stderr_nonempty -or $headless.error_marker -or $headless.timed_out -or -not $headless.expected_text_seen -or (-not $SkipGraphics -and ($graphics.exit_code -ne 0 -or $graphics.stderr_nonempty -or $graphics.error_marker -or $graphics.timed_out -or -not $graphics.expected_text_seen -or -not $captureMetrics -or -not $summary.capture_path -or -not $summary.graphics_timing_eligible -or [string]::IsNullOrWhiteSpace($rendererObserved))) -or ($IncludeMovingRoute -and ($movingRoute.exit_code -ne 0 -or $movingRoute.stderr_nonempty -or $movingRoute.error_marker -or $movingRoute.timed_out -or -not $movingRoute.expected_text_seen -or -not $movingRouteMetrics))) {
    throw "Engine probe did not pass; inspect $OutputDirectory"
}
