param([string]$ReportPath)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $modernShell = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue
    if (-not $modernShell) { throw 'PowerShell 7 or newer is required for reliable Godot process exit-code reporting.' }
    $forwardedArguments = @('-NoProfile', '-File', $PSCommandPath)
    if ($ReportPath) { $forwardedArguments += @('-ReportPath', $ReportPath) }
    & $modernShell.Source @forwardedArguments
    exit $LASTEXITCODE
}
$authorityProbeRoot = Join-Path $PSScriptRoot 'shared_authority'
$authorityEngine = if ($env:ENFRACTAL_GODOT) { $env:ENFRACTAL_GODOT } else {
    Join-Path $env:USERPROFILE 'Downloads\Godot_v4.7.2-stable_win64.exe\Godot_v4.7.2-stable_win64.exe'
}
if (-not (Test-Path -LiteralPath $authorityEngine -PathType Leaf)) { throw 'Godot executable not found; set ENFRACTAL_GODOT.' }
$authorityRunId = [Guid]::NewGuid().ToString('N')
if (-not $ReportPath) { $ReportPath = Join-Path $env:TEMP "enfractal-authority-$authorityRunId.json" }
$ReportPath = [System.IO.Path]::GetFullPath($ReportPath)
$authorityStdout = Join-Path $env:TEMP "enfractal-authority-$authorityRunId.out.txt"
$authorityStderr = Join-Path $env:TEMP "enfractal-authority-$authorityRunId.err.txt"
$previousReport = $env:ENFRACTAL_AUTHORITY_REPORT
$previousRunId = $env:ENFRACTAL_AUTHORITY_RUN_ID
try {
    $env:ENFRACTAL_AUTHORITY_REPORT = $ReportPath
    $env:ENFRACTAL_AUTHORITY_RUN_ID = $authorityRunId
    $authorityProcess = Start-Process -FilePath $authorityEngine -ArgumentList @(
        '--headless', '--path', ('"' + $authorityProbeRoot + '"'), '--script', 'res://authority_probe.gd'
    ) -WindowStyle Hidden -PassThru -RedirectStandardOutput $authorityStdout -RedirectStandardError $authorityStderr
    if (-not $authorityProcess.WaitForExit(30000)) {
        Stop-Process -Id $authorityProcess.Id -Force
        throw 'Loopback authority smoke exceeded its 30-second limit.'
    }
    $authorityProcess.Refresh()
    if (-not (Test-Path -LiteralPath $ReportPath -PathType Leaf)) {
        Get-Content -LiteralPath $authorityStdout
        Get-Content -LiteralPath $authorityStderr
        throw 'Authority smoke did not produce its report.'
    }
    $authorityReport = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
    $expectedCases = @(
        'preadmission_intent', 'preadmission_no_world_snapshot',
        'alice_admitted', 'bob_admitted', 'ticket_single_use', 'ticket_reuse_no_world_snapshot',
        'missing_ticket_rejected', 'missing_ticket_no_world_snapshot',
        'alice_valid_intent', 'bob_valid_intent', 'stale_tick0_cannot_pass_missing_tick1',
        'two_clients_same_authority_snapshot',
        'private_snapshot_fields_filtered', 'server_owned_wind_and_glide',
        'forged_position_rejected', 'forged_owner_and_actor_rejected', 'client_rules_rejected',
        'duplicate_sequence_rejected', 'changed_duplicate_sequence_rejected',
        'copied_session_rejected', 'wrong_world_rejected', 'stale_epoch_rejected',
        'non_string_session_rejected', 'non_string_world_rejected',
        'non_string_frame_rejected', 'non_numeric_epoch_rejected', 'boolean_epoch_rejected',
        'out_of_range_axis_rejected', 'non_numeric_axis_rejected', 'client_time_rejected',
        'unknown_operation_rejected', 'oversized_packet_rejected',
        'old_pending_intent_before_reconnect', 'alice_reconnected', 'old_connection_fenced',
        'old_pending_intent_cleared', 'old_connection_no_snapshot',
        'new_connection_intent', 'new_connection_tick3_observed'
    )
    if ($authorityReport.schema -ne 'enfractal-phase2-authority-probe-v1' -or $authorityReport.run_id -ne $authorityRunId) {
        throw 'Authority report is stale or has an unexpected schema.'
    }
    if ($authorityReport.cases.Count -ne $expectedCases.Count -or @(Compare-Object $expectedCases @($authorityReport.cases.name)).Count -ne 0) {
        throw 'Authority report does not contain exactly the expected cases.'
    }
    if ($authorityReport.setup_error -or @($authorityReport.cases | Where-Object { -not $_.passed }).Count -ne 0 -or -not $authorityReport.passed -or $authorityProcess.ExitCode -ne 0) {
        $authorityReport.cases | Select-Object name,passed,detail | Format-Table
        Get-Content -LiteralPath $authorityStdout
        Get-Content -LiteralPath $authorityStderr
        throw "Authority smoke failed (exit $($authorityProcess.ExitCode))."
    }
    $authorityStdoutText = Get-Content -LiteralPath $authorityStdout -Raw
    $authorityStderrText = Get-Content -LiteralPath $authorityStderr -Raw
    if (-not [string]::IsNullOrWhiteSpace($authorityStderrText) -or $authorityStdoutText -match '(?m)^(SCRIPT ERROR|ERROR|WARNING):') {
        Get-Content -LiteralPath $authorityStdout
        Get-Content -LiteralPath $authorityStderr
        throw 'Authority smoke emitted unexpected engine diagnostics.'
    }
    $authorityReport.cases | Select-Object name,passed | Format-Table
    Write-Output "Engine: $($authorityReport.engine); report: $ReportPath"
} finally {
    $env:ENFRACTAL_AUTHORITY_REPORT = $previousReport
    $env:ENFRACTAL_AUTHORITY_RUN_ID = $previousRunId
}
