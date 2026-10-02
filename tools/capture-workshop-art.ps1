param([ValidateSet('standard', 'low', 'all')][string]$Profile = 'all')

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $modernShell = Get-Command pwsh -CommandType Application -ErrorAction Stop
    & $modernShell.Source -NoProfile -File $PSCommandPath -Profile $Profile
    exit $LASTEXITCODE
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'game'
$enginePath = Join-Path $repoRoot '.cache\godot\Godot_v4.7.2-stable_win64.exe'
if ($env:ENFRACTAL_GODOT -and (Test-Path -LiteralPath $env:ENFRACTAL_GODOT -PathType Leaf)) {
    $enginePath = $env:ENFRACTAL_GODOT
}
if (-not (Test-Path -LiteralPath $enginePath -PathType Leaf)) {
    throw 'Godot 4.7.2 executable was not found. Set ENFRACTAL_GODOT.'
}

$profiles = if ($Profile -eq 'all') { @('standard', 'low') } else { @($Profile) }
foreach ($choice in $profiles) {
    $env:ENFRACTAL_ART_PROFILE = $choice
    $env:ENFRACTAL_WORKSHOP_ART_CAPTURE_PREFIX = Join-Path $repoRoot "docs\images\barton-workshop-art-$choice"
    $stdoutPath = Join-Path $env:TEMP "enfractal-workshop-art-$choice-$PID.out.txt"
    $stderrPath = Join-Path $env:TEMP "enfractal-workshop-art-$choice-$PID.err.txt"
    $process = Start-Process -FilePath $enginePath -ArgumentList @('--path', $projectPath, '--script', 'res://tests/workshop_art_capture.gd') -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    if (-not $process.WaitForExit(90000)) {
        Stop-Process -Id $process.Id -Force
        throw "Workshop art capture timed out ($choice)."
    }
    $process.Refresh()
    $stdout = Get-Content -LiteralPath $stdoutPath -Raw
    $stderr = Get-Content -LiteralPath $stderrPath -Raw
    Write-Output $stdout
    if ($process.ExitCode -ne 0 -or -not [string]::IsNullOrWhiteSpace($stderr)) {
        Write-Output $stderr
        throw "Workshop art capture failed ($choice), exit code $($process.ExitCode)."
    }
}
