param([string]$ReportPath)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $modernShell = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue
    if (-not $modernShell) {
        throw 'PowerShell 7 or newer is required for reliable Godot process exit-code reporting.'
    }
    $forwardedArguments = @('-NoProfile', '-File', $PSCommandPath)
    if ($ReportPath) { $forwardedArguments += @('-ReportPath', $ReportPath) }
    & $modernShell.Source @forwardedArguments
    exit $LASTEXITCODE
}
$transportProbeRoot = Join-Path $PSScriptRoot 'transport_smoke'
$transportEngine = if ($env:ENFRACTAL_GODOT) { $env:ENFRACTAL_GODOT } else {
    Join-Path $env:USERPROFILE 'Downloads\Godot_v4.7.2-stable_win64.exe\Godot_v4.7.2-stable_win64.exe'
}
if (-not (Test-Path -LiteralPath $transportEngine -PathType Leaf)) {
    throw 'Godot executable not found; set ENFRACTAL_GODOT.'
}
$transportRunId = [Guid]::NewGuid().ToString('N')
if (-not $ReportPath) { $ReportPath = Join-Path $env:TEMP "enfractal-transport-$transportRunId.json" }
$ReportPath = [System.IO.Path]::GetFullPath($ReportPath)
$transportStdout = Join-Path $env:TEMP "enfractal-transport-$transportRunId.out.txt"
$transportStderr = Join-Path $env:TEMP "enfractal-transport-$transportRunId.err.txt"
$previousReport = $env:ENFRACTAL_TRANSPORT_REPORT
$previousRunId = $env:ENFRACTAL_TRANSPORT_RUN_ID
try {
    $env:ENFRACTAL_TRANSPORT_REPORT = $ReportPath
    $env:ENFRACTAL_TRANSPORT_RUN_ID = $transportRunId
    $transportProcess = Start-Process -FilePath $transportEngine -ArgumentList @(
        '--headless', '--path', ('"' + $transportProbeRoot + '"'), '--script', 'res://dtls_probe.gd'
    ) -WindowStyle Hidden -PassThru -RedirectStandardOutput $transportStdout -RedirectStandardError $transportStderr
    if (-not $transportProcess.WaitForExit(25000)) {
        Stop-Process -Id $transportProcess.Id -Force
        throw 'Loopback transport smoke exceeded its 25-second limit.'
    }
    $transportProcess.Refresh()
    if (-not (Test-Path -LiteralPath $ReportPath -PathType Leaf)) {
        Get-Content -LiteralPath $transportStderr
        throw 'Transport smoke did not produce its report.'
    }
    $transportReport = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
    $expectedCases = @('trusted_matching_name', 'wrong_hostname', 'untrusted_certificate', 'expired_certificate', 'plaintext_client_to_dtls_server')
    if ($transportReport.schema -ne 'enfractal-transport-smoke-v1' -or $transportReport.run_id -ne $transportRunId) {
        throw 'Transport report is stale or has an unexpected schema.'
    }
    if ($transportReport.cases.Count -ne 5 -or @(Compare-Object $expectedCases @($transportReport.cases.name)).Count -ne 0) {
        throw 'Transport report does not contain exactly the five expected cases.'
    }
    if (@($transportReport.cases | Where-Object { -not $_.passed -or -not $_.setup_ok }).Count -ne 0) {
        throw 'At least one transport case failed or did not complete setup.'
    }
    $transportReport.cases | Select-Object name,passed,server_connected,client_connected,server_received,client_received,elapsed_ms | Format-Table
    Write-Output "Engine: $($transportReport.engine); report: $ReportPath"
    Write-Output "Expected negative-case TLS diagnostics: $transportStderr"
    if ($transportProcess.ExitCode -ne 0 -or -not $transportReport.passed) {
        Get-Content -LiteralPath $transportStderr
        throw "Transport smoke failed (exit $($transportProcess.ExitCode))."
    }
} finally {
    $env:ENFRACTAL_TRANSPORT_REPORT = $previousReport
    $env:ENFRACTAL_TRANSPORT_RUN_ID = $previousRunId
}
