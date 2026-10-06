#requires -Version 7.0
<#
.SYNOPSIS
Captures the five fixed look-review cameras at 1920x1080 on this machine's GPU and measures frame time.

.DESCRIPTION
Runs res://tests/native_look_capture.tscn in a real window (never --headless: headless renders nothing),
writes one PNG per camera plus timings.json into -OutDir, then quits. A Godot window appears briefly.
With -Baseline <commit>, the game folder of that commit is exported under .cache/look-baseline/ and the
current harness is copied into it, so "before" captures use exactly that commit's look code.

Before launching, the script checks GPU memory with nvidia-smi and waits while another job (for example
pose estimation) holds most of it, so timings are not taken under contention.

.EXAMPLE
pwsh -NoProfile -File tools/look/capture-look.ps1 -Label after -OutDir docs/look/reviews/run1/after -Sweep
pwsh -NoProfile -File tools/look/capture-look.ps1 -Label before -OutDir docs/look/reviews/run1/before -Baseline 28fc364
#>
param(
    [string]$Label = 'after',
    [string]$OutDir = 'docs/look/reviews/run1/after',
    [string]$Baseline = '',
    [string]$Cameras = 'tools/look/review_cameras.json',
    [int]$WarmupFrames = 120,
    [int]$MeasureFrames = 300,
    [string]$Only = '',
    [switch]$Sweep,
    [switch]$RootViewport,
    [int]$MaxBusyVramMiB = 4096,
    [int]$TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repository 'tools/native-toolchain.ps1')
$toolchain = Get-EnfractalNativeToolchain
$camerasPath = (Resolve-Path -LiteralPath (Join-Path $repository $Cameras)).Path
$outPath = [System.IO.Path]::GetFullPath((Join-Path $repository $OutDir))
New-Item -ItemType Directory -Force $outPath | Out-Null

$env:DOTNET_ROOT = Split-Path -Parent $toolchain.DotnetPath
$env:DOTNET_CLI_HOME = Join-Path $repository '.cache/dotnet-home'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Invoke-Checked([string]$File, [string[]]$Arguments, [string]$WorkingDirectory) {
    Push-Location $WorkingDirectory
    try {
        $output = & $File @Arguments 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) { throw "$File $($Arguments -join ' ') failed ($LASTEXITCODE):`n$output" }
        return $output
    } finally { Pop-Location }
}

$projectPath = $toolchain.ProjectPath
$commit = (git -C $repository rev-parse --short HEAD).Trim()
$dirty = [bool](git -C $repository status --porcelain -- game tools/look)
if ($Baseline) {
    $commit = (git -C $repository rev-parse --short $Baseline).Trim()
    $dirty = $false
    $exportRoot = Join-Path $repository ".cache/look-baseline/$commit"
    if (Test-Path -LiteralPath $exportRoot) { Remove-Item -LiteralPath $exportRoot -Recurse -Force }
    New-Item -ItemType Directory -Force $exportRoot | Out-Null
    $archive = Join-Path $exportRoot 'game.tar'
    git -C $repository archive --format=tar -o $archive $commit game
    if ($LASTEXITCODE -ne 0) { throw "git archive $commit failed" }
    tar -xf $archive -C $exportRoot
    if ($LASTEXITCODE -ne 0) { throw "could not extract $archive" }
    Remove-Item -LiteralPath $archive
    $projectPath = Join-Path $exportRoot 'game'
    # The harness only uses RoomWorld's public surface, so the current file runs against older commits.
    New-Item -ItemType Directory -Force (Join-Path $projectPath 'tests/native/Look') | Out-Null
    Copy-Item -LiteralPath (Join-Path $repository 'game/tests/native/Look/LookCaptureHarness.cs') -Destination (Join-Path $projectPath 'tests/native/Look/')
    Copy-Item -LiteralPath (Join-Path $repository 'game/tests/native_look_capture.tscn') -Destination (Join-Path $projectPath 'tests/')
    Write-Output "Exported $commit to $projectPath"
}

Write-Output (Invoke-Checked $toolchain.DotnetPath @('build', 'EnFractal.csproj', '--configuration', 'Debug', '--nologo', '-v:q') $projectPath).Trim()
$import = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--editor', '--path', $projectPath, '--import') -TimeoutSeconds 180
if ($import.Stderr -match 'SCRIPT ERROR:') { throw $import.Stderr }

# Wait while another GPU job holds most of the card's memory.
$gpuNote = 'nvidia-smi unavailable'
if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) {
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        $query = (nvidia-smi --query-gpu=name,memory.used,memory.total,utilization.gpu --format=csv,noheader,nounits | Select-Object -First 1).Split(',').ForEach({ $_.Trim() })
        $gpuNote = "$($query[0]); $($query[1]) of $($query[2]) MiB in use and $($query[3])% utilisation before launch"
        if ([int]$query[1] -le $MaxBusyVramMiB) { break }
        Write-Output "GPU busy ($gpuNote); waiting 30 s (attempt $attempt of 20)"
        Start-Sleep -Seconds 30
    }
    Write-Output "GPU: $gpuNote"
}

$userArgs = @("--cameras=$camerasPath", "--out=$outPath", "--label=$Label", "--commit=$commit$(if ($dirty) { '+uncommitted' })",
    "--warmup=$WarmupFrames", "--frames=$MeasureFrames", "--note=$gpuNote")
if ($Only) { $userArgs += "--only=$Only" }
if ($Sweep) { $userArgs += '--sweep' }
if ($RootViewport) { $userArgs += '--root-viewport' }
$engineArgs = @('--path', $projectPath, '--disable-vsync')
if ($RootViewport) { $engineArgs += @('--resolution', '1920x1080', '--position', '0,0') }
else { $engineArgs += @('--resolution', '960x540', '--position', '40,40') }
$engineArgs += @('res://tests/native_look_capture.tscn', '--') + $userArgs

# A visible window is required, so this does not use the hidden-window helper the test runners use.
$start = [System.Diagnostics.ProcessStartInfo]::new($toolchain.EnginePath)
foreach ($argument in $engineArgs) { $start.ArgumentList.Add($argument) }
$start.WorkingDirectory = $projectPath
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['DOTNET_ROOT'] = $env:DOTNET_ROOT
# Keep the player's real user:// data (avatar preferences, rooms) out of review captures.
$start.Environment['APPDATA'] = Join-Path $repository '.cache/look-capture/appdata'
$start.Environment['LOCALAPPDATA'] = Join-Path $repository '.cache/look-capture/localappdata'
New-Item -ItemType Directory -Force $start.Environment['APPDATA'], $start.Environment['LOCALAPPDATA'] | Out-Null
$process = [System.Diagnostics.Process]::Start($start)
$stdoutRead = $process.StandardOutput.ReadToEndAsync()
$stderrRead = $process.StandardError.ReadToEndAsync()
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill($true)
    throw "Look capture timed out after $TimeoutSeconds s`n$($stdoutRead.Result)`n$($stderrRead.Result)"
}
$stdout = $stdoutRead.Result
$stderr = $stderrRead.Result
Set-Content -LiteralPath (Join-Path $repository ".cache/look-capture-$Label.log") -Value "$stdout`n--- stderr`n$stderr"
if ($process.ExitCode -ne 0 -or $stdout -notmatch 'LOOK_CAPTURE_DONE') {
    throw "Look capture failed (exit $($process.ExitCode)):`n$stdout`n$stderr"
}
$stdout.Split("`n") | Where-Object { $_ -match '^(LOOK|Godot Engine|Vulkan|D3D12|OpenGL)' } | ForEach-Object { Write-Output $_.TrimEnd() }
if ($stderr.Trim()) { Write-Output "--- engine stderr (warnings) ---"; Write-Output $stderr.Trim() }
Write-Output "Captures and timings.json written to $outPath"
