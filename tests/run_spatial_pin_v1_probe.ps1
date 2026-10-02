$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $modernShell = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue
    if (-not $modernShell) {
        throw 'PowerShell 7 or newer is required for reliable Godot process exit-code reporting.'
    }
    & $modernShell.Source -NoProfile -File $PSCommandPath
    exit $LASTEXITCODE
}
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'game'
$enginePath = if ($env:ENFRACTAL_GODOT) {
    $env:ENFRACTAL_GODOT
} else {
    Join-Path $env:USERPROFILE 'Downloads\Godot_v4.7.2-stable_win64.exe\Godot_v4.7.2-stable_win64.exe'
}
if (-not (Test-Path -LiteralPath $enginePath -PathType Leaf)) {
    throw 'Godot 4.7.2 executable not found; set ENFRACTAL_GODOT.'
}
$pythonPath = Join-Path $repoRoot '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $pythonPath -PathType Leaf)) {
    throw 'Repository Python virtual environment not found.'
}
$env:ENFRACTAL_SPATIAL_V1_CASES = Join-Path $PSScriptRoot 'fixtures\spatial_pin_v1_cases.json'
$env:ENFRACTAL_SPATIAL_V1_OUTPUT = Join-Path $env:TEMP "enfractal-spatial-v1-$PID.json"
$stdoutPath = Join-Path $env:TEMP "enfractal-spatial-v1-$PID.out.txt"
$stderrPath = Join-Path $env:TEMP "enfractal-spatial-v1-$PID.err.txt"
$process = Start-Process -FilePath $enginePath -ArgumentList @(
    '--headless', '--path', $projectPath, '--script',
    'res://tests/spatial_pin_v1_probe.gd'
) -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
if (-not $process.WaitForExit(30000)) {
    Stop-Process -Id $process.Id -Force
    throw 'Godot spatial-pin v1 probe timed out.'
}
$process.Refresh()
$stdout = Get-Content -LiteralPath $stdoutPath -Raw
$stderr = Get-Content -LiteralPath $stderrPath -Raw
if ($process.ExitCode -ne 0 -or -not [string]::IsNullOrWhiteSpace($stderr)) {
    Write-Output $stdout
    Write-Output $stderr
    throw "Godot spatial-pin v1 probe failed with exit code $($process.ExitCode)."
}
& $pythonPath -m tests.spatial_pin_v1_compare --godot $env:ENFRACTAL_SPATIAL_V1_OUTPUT
if ($LASTEXITCODE -ne 0) {
    throw 'Python/Godot spatial-pin bytes or hashes differ.'
}
Write-Output $stdout.Trim()
