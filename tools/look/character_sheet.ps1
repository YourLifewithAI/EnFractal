#requires -Version 7.0
<#
.SYNOPSIS
Renders one contact sheet per drawn character (nine tiles each) on this machine's GPU, and optionally what the bubble costs.

.DESCRIPTION
Runs res://tests/native/Look/character_sheet_capture.tscn in a real window (never --headless: headless renders nothing) and
writes sheet_<id>.png per character plus character_sheets.json into -OutDir, then quits. A Godot window appears briefly.

Each sheet stands the character (its GLB with CharacterLook applied) on the test room's floor at the review clock (17:00 on
15 April, the lamps on, where the low sun through the west window lands on the player's spawn), in nine tiles: idle, walk, run,
a hop landing, a 30 degree slope, wading, climbing, a tumble mid-flop, and for a floating character floating and casting (its
halo lit through the real look) or for the others a close look at the face. The poses come from later packets through
CharacterSheetPoses (game/tests/native/Look/CharacterSheetPoses.cs); until a packet registers one, a tile shows the rest pose and
its label says so. -Timing adds the bubble's cost: GPU time of a 1920 x 1080 frame with the bubble parts shown and hidden, in
alternating blocks, at a play distance and close up, for characters that have bubble parts; -Variants with it also measures
the bubble shader's render-mode variants (one skin, lit with and without shadows) for the budget's record.

As the look captures do (capture-look.ps1), it refuses to start while a game window titled "EnFractal..." is open (someone is
playing on this GPU; -AllowOpenGame overrides), waits until nvidia-smi shows the card quiet for six seconds, keeps the player's
user:// data out of the run, and writes the engine's log to character_sheet.log beside the sheets.

.EXAMPLE
pwsh -NoProfile -File tools/look/character_sheet.ps1 -OutDir .cache/character-sheets/after
pwsh -NoProfile -File tools/look/character_sheet.ps1 -Characters gubble -Timing -OutDir C:\temp\sheets
#>
param(
    [string]$Label = 'after',
    [string]$OutDir = '',
    [string]$Characters = '',
    [string]$Tile = '720x450',
    [switch]$Timing,
    [switch]$Variants,
    [int]$MaxBusyVramMiB = 3072,
    [int]$MaxBusyUtilisation = 60,
    [int]$TimeoutSeconds = 300,
    [switch]$AllowOpenGame
)
$ErrorActionPreference = 'Stop'

# Someone playing the game on this GPU: never render over them.
$openGame = Get-Process | Where-Object { $_.MainWindowTitle -like 'EnFractal*' }
if ($openGame -and -not $AllowOpenGame) {
    throw "An EnFractal game window is open ($(($openGame | ForEach-Object { "$($_.ProcessName) $($_.Id): $($_.MainWindowTitle)" }) -join '; ')). Character sheets wait for a quiet GPU; close the game or pass -AllowOpenGame."
}
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repository 'tools/native-toolchain.ps1')
$toolchain = Get-EnfractalNativeToolchain
if (-not $OutDir) { $OutDir = ".cache/character-sheets/$Label" }
$outPath = [System.IO.Path]::GetFullPath($OutDir, $repository)
New-Item -ItemType Directory -Force $outPath | Out-Null
$assets = Join-Path $repository 'assets/characters'

$env:DOTNET_ROOT = Split-Path -Parent $toolchain.DotnetPath
$env:PATH = $env:DOTNET_ROOT + [System.IO.Path]::PathSeparator + $env:PATH
$env:DOTNET_CLI_HOME = Join-Path $repository '.cache/dotnet-home'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$projectPath = $toolchain.ProjectPath
$commit = (git -C $repository rev-parse --short HEAD).Trim()
$dirty = [bool](git -C $repository status --porcelain -- game tools/look assets/characters)
Build-EnfractalNativeProject -Toolchain $toolchain | Out-Null
$import = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--editor', '--path', $projectPath, '--import') -TimeoutSeconds 180
if ($import.Stderr -match 'SCRIPT ERROR:') { throw $import.Stderr }

# Another job may use the GPU in bursts: wait until the card has been quiet for six seconds (as capture-look.ps1 does).
$gpuNote = 'nvidia-smi unavailable'
if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) {
    $quiet = 0
    for ($sample = 1; $sample -le 300 -and $quiet -lt 3; $sample++) {
        $fields = (nvidia-smi --query-gpu=name,memory.used,memory.total,utilization.gpu --format=csv,noheader,nounits | Select-Object -First 1).Split(',').ForEach({ $_.Trim() })
        if ([int]$fields[1] -le $MaxBusyVramMiB -and [int]$fields[3] -le $MaxBusyUtilisation) { $quiet++ } else { $quiet = 0 }
        $gpuNote = "$($fields[0]); $($fields[1]) of $($fields[2]) MiB in use and $($fields[3])% utilisation before launch"
        if ($quiet -lt 3) { Start-Sleep -Seconds 2 }
    }
    if ($quiet -lt 3) { throw 'The GPU did not go quiet within 10 minutes; another job is using it. Retry later.' }
}
Write-Output "GPU: $gpuNote"

$userArgs = @("--out=$($outPath -replace '\\', '/')", "--assets=$($assets -replace '\\', '/')", "--tile=$Tile",
    "--commit=$commit$(if ($dirty) { '+uncommitted' })", "--note=$gpuNote")
if ($Characters) { $userArgs += "--characters=$Characters" }
if ($Timing) { $userArgs += '--timing=true' }
if ($Timing -and $Variants) { $userArgs += '--variants=true' }
$engineArgs = @('--path', $projectPath, '--disable-vsync', '--resolution', '960x540', '--position', '40,40',
    'res://tests/native/Look/character_sheet_capture.tscn', '--') + $userArgs

# A visible window is required, so this does not use the hidden-window helper the test runners use.
$start = [System.Diagnostics.ProcessStartInfo]::new($toolchain.EnginePath)
foreach ($argument in $engineArgs) { $start.ArgumentList.Add($argument) }
$start.WorkingDirectory = $projectPath
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['DOTNET_ROOT'] = $env:DOTNET_ROOT
$start.Environment['PATH'] = $env:PATH
# Keep the player's real user:// data (avatar preferences, rooms) out of the sheets.
$start.Environment['APPDATA'] = Join-Path $repository '.cache/look-capture/appdata'
$start.Environment['LOCALAPPDATA'] = Join-Path $repository '.cache/look-capture/localappdata'
New-Item -ItemType Directory -Force $start.Environment['APPDATA'], $start.Environment['LOCALAPPDATA'] | Out-Null
$process = [System.Diagnostics.Process]::Start($start)
$stdoutRead = $process.StandardOutput.ReadToEndAsync()
$stderrRead = $process.StandardError.ReadToEndAsync()
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill($true)
    throw "The character sheets timed out after $TimeoutSeconds s`n$($stdoutRead.Result)`n$($stderrRead.Result)"
}
$process.WaitForExit()
$stdout = $stdoutRead.Result
$stderr = $stderrRead.Result
Set-Content -LiteralPath (Join-Path $outPath 'character_sheet.log') -Value "$stdout`n--- stderr`n$stderr"
if ($stdout -notmatch 'CHARACTER_SHEET_DONE' -or $process.ExitCode -ne 0) {
    throw "The character sheets failed (exit $($process.ExitCode)):`n$stdout`n$stderr"
}
$stdout.Split("`n") | Where-Object { $_ -match '^(CHARACTER_SHEET|Godot Engine|Vulkan|D3D12)' } | ForEach-Object { Write-Output $_.TrimEnd() }
if ($stderr.Trim()) { Write-Output '--- engine stderr (warnings) ---'; Write-Output $stderr.Trim() }
Write-Output "Character sheets and character_sheets.json written to $outPath"
