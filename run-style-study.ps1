$ErrorActionPreference = 'Stop'

if (-not $env:ENFRACTAL_VIEW) {
    $env:ENFRACTAL_VIEW = 'greenbelt_study'
}
if (-not $env:ENFRACTAL_STYLE) {
    $env:ENFRACTAL_STYLE = 'atlas'
}

& (Join-Path $PSScriptRoot 'run-map.ps1')
exit $LASTEXITCODE
