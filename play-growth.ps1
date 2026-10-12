#requires -Version 7.0
param([string]$Room)
$ErrorActionPreference = 'Stop'

if (-not $Room) {
    $fixtureRoot = Join-Path $PSScriptRoot '.cache/landscape-fixture'
    $manifest = Get-ChildItem -LiteralPath $fixtureRoot -Filter 'room.json' -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'landscape_garage_nominal' } |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (-not $manifest) { throw 'Run tools/test-room.ps1 once to prepare the landscape, or pass -Room with your room path.' }
    $Room = $manifest.Directory.FullName.Replace('\', '/')
}

$priorRoaming = $env:APPDATA
$priorLocal = $env:LOCALAPPDATA
try {
    # Keep this branch's repeatable playtest profile alongside its build and fixture.
    $env:APPDATA = Join-Path $PSScriptRoot '.cache/growth-playtest/roaming'
    $env:LOCALAPPDATA = Join-Path $PSScriptRoot '.cache/growth-playtest/local'
    New-Item -ItemType Directory -Force $env:APPDATA,$env:LOCALAPPDATA | Out-Null
    & (Join-Path $PSScriptRoot 'run-room.ps1') -Room $Room
    exit $LASTEXITCODE
} finally {
    $env:APPDATA = $priorRoaming
    $env:LOCALAPPDATA = $priorLocal
}
