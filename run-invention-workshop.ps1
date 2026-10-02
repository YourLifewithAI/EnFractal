$ErrorActionPreference = 'Stop'
$inventionEnvironment = @{
    ENFRACTAL_MANUAL_INVENTION = '1'
    ENFRACTAL_START_WALK = '1'
    ENFRACTAL_STYLE = 'natural'
    ENFRACTAL_VIEW = 'pin'
}
$previousInventionEnvironment = @{}
foreach ($key in $inventionEnvironment.Keys) {
    $previousInventionEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
}
try {
    foreach ($key in $inventionEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($key, $inventionEnvironment[$key], 'Process')
    }
    & (Join-Path $PSScriptRoot 'run-map.ps1')
    $inventionExitCode = $LASTEXITCODE
} finally {
    foreach ($key in $inventionEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($key, $previousInventionEnvironment[$key], 'Process')
    }
}
exit $inventionExitCode
