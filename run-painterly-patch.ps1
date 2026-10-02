$ErrorActionPreference = 'Stop'

$previousEnvironment = @{}
$patchEnvironment = @{
    ENFRACTAL_STYLE = 'natural'
    ENFRACTAL_VIEW = 'pin'
    ENFRACTAL_START_WALK = '1'
}
foreach ($name in $patchEnvironment.Keys) {
    $previousEnvironment[$name] = @{
        Exists = Test-Path -LiteralPath ('Env:\' + $name)
        Value = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
}

$launchExitCode = 0
try {
    foreach ($name in $patchEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $patchEnvironment[$name], 'Process')
    }
    & (Join-Path $PSScriptRoot 'run-map.ps1')
    $launchExitCode = $LASTEXITCODE
} finally {
    foreach ($name in $patchEnvironment.Keys) {
        if ($previousEnvironment[$name].Exists) {
            [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name].Value, 'Process')
        } else {
            Remove-Item -LiteralPath ('Env:\' + $name) -ErrorAction SilentlyContinue
        }
    }
}
exit $launchExitCode
