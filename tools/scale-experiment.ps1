#requires -Version 7.0
<#
.SYNOPSIS
The scale experiment (branch run2/scale-experiment, not for merging): the garage island built from the garage magnified x10
around today's 10 cm body, and a way to walk around it without touching the player's own saves.

.DESCRIPTION
Builds, once, under .cache/scale-experiment/ (derived data, never committed):
  rooms/garage_nominal_x<F>   the garage corpus room magnified x<F> (pipeline/landscape/generator/scale.py)
  package-x<F>                the landscape package (generate --world-scale <F> --cell <Cell>)
  landscape_garage_x<F>       the exported room the game loads
then, with -Play, opens the game on it in a window. The game's user data (saves, the profile and its one-time hints, the
companion's memory) goes to .cache/scale-experiment/profile, never to the player's own %APPDATA%; the environment is restored
afterwards. -Rebuild regenerates everything. Nothing here changes a desktop shortcut.

.EXAMPLE
pwsh -NoProfile -File C:\dev\EnFractal-run2\scale\tools\scale-experiment.ps1 -Play
#>
param(
    [double]$Factor = 10,
    [double]$Cell = 0.1,
    [switch]$Play,
    [switch]$Rebuild
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'native-toolchain.ps1')
$toolchain = Get-EnfractalNativeToolchain
$tag = ('{0:g}' -f $Factor).Replace('.', '_')
$work = Join-Path $repository '.cache/scale-experiment'
$room = Join-Path $work "rooms/garage_nominal_x$tag"
$package = Join-Path $work "package-x$tag"
$roomId = "landscape_garage_x$tag"
$exported = Join-Path $work $roomId
$profile = Join-Path $work 'profile'

if ($Rebuild) { foreach ($path in @($room, $package, $exported)) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force } } }
$python = (Get-Command python -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
if (-not (Test-Path -LiteralPath (Join-Path $exported 'room.json'))) {
    foreach ($path in @($room, $package, $exported)) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force } }
    $steps = @(
        @('-B', '-m', 'pipeline.landscape.generator.scale', '--room', 'pipeline/landscape/corpus/rooms/garage_nominal', '--factor', "$Factor", '--out', $room),
        @('-B', '-m', 'pipeline.landscape.generator.generate', '--room', $room, '--world-scale', "$Factor", '--cell', "$Cell", '--out', $package),
        @('-B', '-S', '-m', 'pipeline.landscape.export', '--package', $package, '--room', $room, '--room-id', $roomId, '--out', $exported, '--world-scale', "$Factor")
    )
    foreach ($arguments in $steps) {
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        $result = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $python -WorkingDirectory $repository -TimeoutSeconds 7200 -Arguments $arguments
        Write-Output ("{0} ({1:0} s)" -f $result.Stdout.Trim(), $clock.Elapsed.TotalSeconds)
    }
}
Write-Output "Scale experiment room: $exported"
if (-not $Play) { return }

$openGame = Get-Process | Where-Object { $_.MainWindowTitle -like 'EnFractal*' }
if ($openGame) { Write-Warning "Another EnFractal window is open ($($openGame.MainWindowTitle -join '; ')); both share the GPU." }
Build-EnfractalNativeProject -Toolchain $toolchain
$saved = @{ APPDATA = $env:APPDATA; LOCALAPPDATA = $env:LOCALAPPDATA; DOTNET_ROOT = $env:DOTNET_ROOT; PATH = $env:PATH }
try {
    $env:DOTNET_ROOT = Split-Path -Parent $toolchain.DotnetPath
    $env:PATH = $env:DOTNET_ROOT + [System.IO.Path]::PathSeparator + $env:PATH
    & (Join-Path $PSScriptRoot 'ensure-godot-art-imports.ps1') -EnginePath $toolchain.EnginePath -ProjectPath $toolchain.ProjectPath
    # The experiment's own profile folder: the player's saves and one-time hints are never read or written.
    $env:APPDATA = Join-Path $profile 'appdata'
    $env:LOCALAPPDATA = Join-Path $profile 'localappdata'
    New-Item -ItemType Directory -Force $env:APPDATA, $env:LOCALAPPDATA | Out-Null
    & $toolchain.EnginePath --path $toolchain.ProjectPath -- "--room=$($exported -replace '\\', '/')"
} finally {
    foreach ($key in $saved.Keys) { Set-Item -Path "env:$key" -Value $saved[$key] }
}
