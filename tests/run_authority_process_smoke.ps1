param([string]$ReportPath)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $modernShell = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue
    if (-not $modernShell) { throw 'PowerShell 7 or newer is required for reliable process exit-code reporting.' }
    $forwardedArguments = @('-NoProfile', '-File', $PSCommandPath)
    if ($ReportPath) { $forwardedArguments += @('-ReportPath', $ReportPath) }
    & $modernShell.Source @forwardedArguments
    exit $LASTEXITCODE
}

$probeRoot = Join-Path $PSScriptRoot 'shared_authority'
$engine = if ($env:ENFRACTAL_GODOT) { $env:ENFRACTAL_GODOT } else {
    Join-Path $env:USERPROFILE 'Downloads\Godot_v4.7.2-stable_win64.exe\Godot_v4.7.2-stable_win64.exe'
}
if (-not (Test-Path -LiteralPath $engine -PathType Leaf)) { throw 'Godot executable not found; set ENFRACTAL_GODOT.' }
$sourceFiles = @('run_authority_process_smoke.ps1', 'shared_authority/project.godot',
    'shared_authority/authority_model.gd', 'shared_authority/authority_process.gd',
    'shared_authority/remote_client.gd')
$sourcePins = [ordered]@{}
foreach ($sourceFile in $sourceFiles) {
    $sourcePins[$sourceFile] = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $sourceFile) -Algorithm SHA256).Hash.ToLowerInvariant()
}
$enginePin = (Get-FileHash -LiteralPath $engine -Algorithm SHA256).Hash.ToLowerInvariant()
$runId = [Guid]::NewGuid().ToString('N')
$tempBase = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar)
$runRoot = [System.IO.Path]::GetFullPath((Join-Path $tempBase "enfractal-authority-process-$runId"))
if (-not $runRoot.StartsWith($tempBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing an unexpected temporary work directory.'
}
New-Item -ItemType Directory -Path $runRoot | Out-Null
if (-not $ReportPath) { $ReportPath = Join-Path $tempBase "enfractal-authority-process-$runId.json" }
$ReportPath = [System.IO.Path]::GetFullPath($ReportPath)
$processes = @{}
$clock = [Diagnostics.Stopwatch]::StartNew()
try {
    function Start-ProbeProcess([string]$role, [string]$scriptPath) {
        $arguments = @('--headless', '--path', ('"' + $probeRoot + '"'), '--script', $scriptPath,
            '--', '--run-dir', ('"' + $runRoot + '"'), '--run-id', $runId)
        if ($role -ne 'authority') { $arguments += @('--role', $role) }
        Start-Process -FilePath $engine -ArgumentList $arguments -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $runRoot "$role.stdout.txt") `
            -RedirectStandardError (Join-Path $runRoot "$role.stderr.txt")
    }
    $processes.authority = Start-ProbeProcess 'authority' 'res://authority_process.gd'
    $admission = Join-Path $runRoot 'admission.json'
    while (-not (Test-Path -LiteralPath $admission -PathType Leaf) -and $clock.Elapsed.TotalSeconds -lt 10) {
        $processes.authority.Refresh()
        if ($processes.authority.HasExited) { break }
        Start-Sleep -Milliseconds 25
    }
    if (-not (Test-Path -LiteralPath $admission -PathType Leaf)) {
        Get-Content -LiteralPath (Join-Path $runRoot 'authority.stdout.txt') -ErrorAction SilentlyContinue
        Get-Content -LiteralPath (Join-Path $runRoot 'authority.stderr.txt') -ErrorAction SilentlyContinue
        throw 'Authority process failed to publish loopback admission configuration.'
    }
    $processes.alice = Start-ProbeProcess 'alice' 'res://remote_client.gd'
    $aliceReady = Join-Path $runRoot 'alice-session.json'
    while (-not (Test-Path -LiteralPath $aliceReady -PathType Leaf) -and $clock.Elapsed.TotalSeconds -lt 15) {
        $processes.alice.Refresh()
        if ($processes.alice.HasExited) { break }
        Start-Sleep -Milliseconds 25
    }
    if (-not (Test-Path -LiteralPath $aliceReady -PathType Leaf)) {
        Get-Content -LiteralPath (Join-Path $runRoot 'alice.stdout.txt') -ErrorAction SilentlyContinue
        Get-Content -LiteralPath (Join-Path $runRoot 'alice.stderr.txt') -ErrorAction SilentlyContinue
        throw 'Alice process did not reach its admitted-session barrier.'
    }
    $processes.bob = Start-ProbeProcess 'bob' 'res://remote_client.gd'
    while ($clock.Elapsed.TotalSeconds -lt 35) {
        $unfinished = @($processes.Values | Where-Object { $_.Refresh(); -not $_.HasExited })
        if ($unfinished.Count -eq 0) { break }
        Start-Sleep -Milliseconds 25
    }
    $unfinished = @($processes.Values | Where-Object { $_.Refresh(); -not $_.HasExited })
    if ($unfinished.Count -gt 0) { throw 'Separate-process authority fixture exceeded its 35-second total limit.' }
    $reports = @{}
    foreach ($role in @('authority', 'alice', 'bob')) {
        $path = Join-Path $runRoot "$role-report.json"
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            Get-Content -LiteralPath (Join-Path $runRoot "$role.stdout.txt") -ErrorAction SilentlyContinue
            Get-Content -LiteralPath (Join-Path $runRoot "$role.stderr.txt") -ErrorAction SilentlyContinue
            throw "$role did not produce a report."
        }
        $reports[$role] = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $stdout = Get-Content -LiteralPath (Join-Path $runRoot "$role.stdout.txt") -Raw
        $stderr = Get-Content -LiteralPath (Join-Path $runRoot "$role.stderr.txt") -Raw
        if ($processes[$role].ExitCode -ne 0 -or -not $reports[$role].passed -or $reports[$role].error -or
            -not [string]::IsNullOrWhiteSpace($stderr) -or $stdout -match '(?m)^(SCRIPT ERROR|ERROR|WARNING):') {
            foreach ($loggedRole in @('authority', 'alice', 'bob')) {
                Write-Output "$loggedRole stdout:"
                Get-Content -LiteralPath (Join-Path $runRoot "$loggedRole.stdout.txt") -ErrorAction SilentlyContinue
                Write-Output "$loggedRole stderr:"
                Get-Content -LiteralPath (Join-Path $runRoot "$loggedRole.stderr.txt") -ErrorAction SilentlyContinue
            }
            throw "$role failed (exit $($processes[$role].ExitCode))."
        }
        if ($reports[$role].run_id -ne $runId -or $reports[$role].role -ne $role) { throw "$role report is stale or misidentified." }
        if (@($reports[$role].cases | Where-Object { -not $_.passed }).Count -gt 0) { throw "$role has a failed case." }
    }
    if ($reports.authority.schema -ne 'enfractal-phase2-multiprocess-authority-v1' -or
        $reports.alice.schema -ne 'enfractal-phase2-multiprocess-client-v1' -or
        $reports.bob.schema -ne 'enfractal-phase2-multiprocess-client-v1') { throw 'Unexpected report schema.' }
    $expected = @{
        authority = @('two_clients_admitted', 'two_client_intents_accepted', 'one_server_owned_tick',
            'nonadmitted_peer_excluded_at_broadcast', 'exact_tick_one_positions',
            'adversarial_rejections_preserved_state', 'both_clients_completed_bound_session')
        alice = @('preadmission_intent_rejected', 'preadmission_no_snapshot', 'valid_one_use_join',
            'valid_glide_intent', 'received_exact_tick_one_shared_positions', 'snapshot_exact_allowlist',
            'private_marker_filtered',
            'forged_position_rejected', 'duplicate_sequence_rejected', 'forged_rules_rejected', 'bound_completion')
        bob = @('one_use_ticket_rejected_on_other_connection', 'replay_probe_no_snapshot_before_broadcast',
            'valid_one_use_join', 'copied_other_process_session_rejected', 'valid_move_intent',
            'received_exact_tick_one_shared_positions', 'replay_probe_no_snapshot_after_tick_one',
            'snapshot_exact_allowlist', 'private_marker_filtered', 'forged_owner_rejected',
            'wrong_world_rejected', 'stale_epoch_rejected', 'bound_completion')
    }
    foreach ($role in @('authority', 'alice', 'bob')) {
        $observed = @($reports[$role].cases.name)
        if ($observed.Count -ne $expected[$role].Count -or @(Compare-Object $expected[$role] $observed).Count -ne 0) {
            throw "$role report does not contain the required case set."
        }
    }
    if ($reports.authority.tick -ne 1 -or $reports.authority.connected_peers_at_tick -ne 3 -or
        $reports.authority.snapshot_recipients -ne 2 -or
        $reports.authority.bind_address -ne '127.0.0.1' -or $reports.authority.public_listener) {
        throw 'Authority tick or listener identity does not match the bounded fixture.'
    }
    $pids = @($reports.authority.process_id, $reports.alice.process_id, $reports.bob.process_id)
    if (@($pids | Select-Object -Unique).Count -ne 3 -or @($pids | Where-Object { $_ -le 0 }).Count -gt 0) {
        throw 'The reports do not prove three distinct processes.'
    }
    foreach ($role in @('authority', 'alice', 'bob')) {
        if ([int]$reports[$role].process_id -ne $processes[$role].Id) {
            throw "$role self-reported PID does not match its launched process."
        }
    }
    foreach ($role in @('alice', 'bob')) {
        if ($reports[$role].observed_tick -ne $reports.authority.tick -or $reports[$role].observed_rules_revision -ne 1) {
            throw "$role did not observe the authority tick and rule revision."
        }
        foreach ($avatar in @('alice', 'bob')) {
            foreach ($axis in @('x', 'z')) {
                $actual = [double]$reports[$role].observed_positions.$avatar.$axis
                $expectedValue = [double]$reports.authority.positions.$avatar.$axis
                if ([Math]::Abs($actual - $expectedValue) -gt 0.000001) {
                    throw "$role did not observe the authority-owned $avatar/$axis position."
                }
            }
        }
    }
    foreach ($sourceFile in $sourceFiles) {
        $afterHash = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $sourceFile) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($afterHash -ne $sourcePins[$sourceFile]) { throw "Source changed during the fixture: $sourceFile" }
    }
    if ((Get-FileHash -LiteralPath $engine -Algorithm SHA256).Hash.ToLowerInvariant() -ne $enginePin) {
        throw 'Godot executable changed during the fixture.'
    }
    $combined = [ordered]@{
        schema = 'enfractal-phase2-multiprocess-run-v1'
        run_id = $runId
        utc = [DateTime]::UtcNow.ToString('o')
        transport = 'ENet+DTLS'
        separate_processes = 3
        network_scope = '127.0.0.1 only'
        engine_sha256 = $enginePin
        source_sha256 = $sourcePins
        report_contains_bearer_secrets = $false
        duration_ms = [int]$clock.Elapsed.TotalMilliseconds
        passed = $true
        authority = $reports.authority
        alice = $reports.alice
        bob = $reports.bob
    }
    $combinedJson = $combined | ConvertTo-Json -Depth 16
    $admissionSecrets = Get-Content -LiteralPath $admission -Raw | ConvertFrom-Json
    $aliceSession = Get-Content -LiteralPath $aliceReady -Raw | ConvertFrom-Json
    $bobSession = Get-Content -LiteralPath (Join-Path $runRoot 'bob-session.json') -Raw | ConvertFrom-Json
    if ($aliceSession.run_id -ne $runId -or $bobSession.run_id -ne $runId) { throw 'Session markers came from another run.' }
    foreach ($secret in @($admissionSecrets.tickets.alice, $admissionSecrets.tickets.bob,
            $aliceSession.session_id, $bobSession.session_id)) {
        if (-not [string]::IsNullOrEmpty($secret) -and $combinedJson.Contains($secret)) {
            throw 'Refusing to retain a report containing a temporary bearer value.'
        }
    }
    $combinedJson | Set-Content -LiteralPath $ReportPath -Encoding utf8
    Write-Output "Separate-process authority: $(@($reports.authority.cases).Count + @($reports.alice.cases).Count + @($reports.bob.cases).Count) checks passed; $([int]$clock.Elapsed.TotalMilliseconds) ms"
    Write-Output "Report: $ReportPath"
} finally {
    foreach ($process in $processes.Values) {
        $process.Refresh()
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    }
    if ($runRoot.StartsWith($tempBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $runRoot -PathType Container)) {
        Remove-Item -LiteralPath $runRoot -Recurse -Force
    }
}
