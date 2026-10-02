param([switch]$Check)
$ErrorActionPreference = 'Stop'
$configPath = Join-Path $PSScriptRoot '.cache/save-travel-config.json'
$pythonPath = Join-Path $PSScriptRoot '.cache/save-travel-venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $configPath) -or -not (Test-Path -LiteralPath $pythonPath)) {
    throw 'Run python services/save_travel/setup.py once to prepare the local save service, then run this launcher again.'
}
$configuration = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if ($configuration.manage_postgres -ne $false) {
    $control = Join-Path $configuration.postgres_bin 'pg_ctl.exe'
    & $control status -D $configuration.postgres_data *> $null
    if ($LASTEXITCODE -ne 0) {
        $postgresLog = Join-Path $PSScriptRoot '.cache/postgresql/postgres.log'
        & $control start -D $configuration.postgres_data -l $postgresLog -w
        if ($LASTEXITCODE -ne 0) { throw 'The local database did not start. Inspect .cache/postgresql/postgres.log.' }
    }
}
$port = if ($configuration.port) { [int]$configuration.port } else { 8765 }
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $port)
try { $listener.Start() } catch { throw "Local save port $port is already in use. Close the earlier Save and Travel launcher before starting another." } finally { $listener.Stop() }
$previousEnvironment = @{}
$environment = @{
    ENFRACTAL_SAVE_TRAVEL = '1'
    ENFRACTAL_MANUAL_INVENTION = '1'
    ENFRACTAL_START_WALK = '1'
    ENFRACTAL_STYLE = 'natural'
    ENFRACTAL_VIEW = 'pin'
    ENFRACTAL_TRAVEL_PORT = [string]$port
    ENFRACTAL_TRAVEL_TOKEN = [string]$configuration.token
}
$service = $null
$resultCode = 0
try {
    foreach ($key in $environment.Keys) {
        $previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $environment[$key], 'Process')
    }
    $serviceScript = Join-Path $PSScriptRoot 'services/save_travel/server.py'
    $serviceOut = Join-Path $PSScriptRoot '.cache/save-travel-service.out'
    $serviceErr = Join-Path $PSScriptRoot '.cache/save-travel-service.err'
    $service = Start-Process -FilePath $pythonPath -ArgumentList @('"' + $serviceScript + '"', '--config', '"' + $configPath + '"', '--port', [string]$port) -WindowStyle Hidden -PassThru -RedirectStandardOutput $serviceOut -RedirectStandardError $serviceErr
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($service.HasExited) { throw 'The local save service could not start. Inspect .cache/save-travel-service.err.' }
        try {
            $response = Invoke-RestMethod -Uri "http://127.0.0.1:$port/v1/action" -Method Post -ContentType 'application/json' -Headers @{ Authorization = 'Bearer ' + $configuration.token } -Body '{"op":"status"}' -TimeoutSec 1
            if ($response.code -in @('not_initialized', 'session_stale')) { $ready = $true; break }
            if ($response.ok) { $ready = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 100
    }
    if (-not $ready) { throw 'The local save service did not become ready.' }
    Write-Output 'Local saved worlds are ready. T opens travel; B opens the invention workshop.'
    if (-not $Check) {
        & (Join-Path $PSScriptRoot 'run-map.ps1')
        $resultCode = $LASTEXITCODE
    }
} finally {
    if ($service -and -not $service.HasExited) { Stop-Process -Id $service.Id -Force }
    foreach ($key in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($key, $previousEnvironment[$key], 'Process')
    }
}
exit $resultCode
