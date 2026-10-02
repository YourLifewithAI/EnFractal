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
        throw 'Godot 4.7.2 was not found. Set ENFRACTAL_GODOT to the path of Godot_v4.7.2-stable_win64.exe or open game/project.godot in Godot.'
    }
    $enginePath = $engine.Source
}

& (Join-Path $PSScriptRoot 'tools\ensure-godot-art-imports.ps1') -EnginePath $enginePath -ProjectPath $projectPath

& $enginePath --path $projectPath
exit $LASTEXITCODE
