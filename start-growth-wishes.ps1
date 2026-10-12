#requires -Version 7.0
param([string]$Client, [switch]$Demo)
$ErrorActionPreference = 'Stop'
if ($Demo -and $Client) { throw 'Choose -Demo or -Client, not both.' }
if (-not $Demo -and -not $Client) { throw 'Choose -Demo for the no-provider demonstration, or -Client with your own adapter JSON file.' }
$pythonPath = Join-Path $PSScriptRoot 'companion/.venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $pythonPath -PathType Leaf)) { throw 'Prepare the pinned companion environment with run-engine-tests.ps1 first.' }
$priorPython = $env:PYTHONPATH
try {
    $env:PYTHONPATH = Join-Path $PSScriptRoot 'companion/src'
    if ($Demo) {
        $Client = Join-Path $PSScriptRoot '.cache/growth-playtest/wish-demo.json'
        & $pythonPath -m enfractal_companion.wish_runner --write-demo-client $Client
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    $sessionPath = Join-Path $PSScriptRoot '.cache/growth-playtest/roaming/Godot/app_userdata/EnFractal/companion/session.json'
    & $pythonPath -m enfractal_companion.wish_runner --client $Client --session $sessionPath
    exit $LASTEXITCODE
} finally { $env:PYTHONPATH = $priorPython }
