$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'game'
$portableGodot = Join-Path $PSScriptRoot '.cache\godot\Godot_v4.7.2-stable_win64.exe'
if (Test-Path -LiteralPath $portableGodot) {
    $enginePath = $portableGodot
} else {
    $engine = Get-Command godot -ErrorAction SilentlyContinue
    if (-not $engine) {
        throw 'Godot 4.7.2 is not installed. Download the standard Windows build from https://godotengine.org/download/archive/4.7.2-stable/ and open game/project.godot.'
    }
    $enginePath = $engine.Source
}

& $enginePath --path $projectPath
exit $LASTEXITCODE
