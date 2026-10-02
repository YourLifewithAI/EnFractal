param([switch]$Capture)
$ErrorActionPreference = 'Stop'
$pythonPath = Join-Path $PSScriptRoot '.cache/save-travel-venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $pythonPath)) { throw 'Prepare the local service with python services/save_travel/setup.py first.' }
Push-Location $PSScriptRoot
try {
    & $pythonPath -m unittest discover -s services/save_travel/tests -v
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL service checks failed.' }
    & $pythonPath -m unittest discover -s tests/save_travel_recovery -v
    if ($LASTEXITCODE -ne 0) { throw 'Recovery checks failed.' }
    $integrationArguments = @('tools/run-save-travel-tests.py')
    if ($Capture) { $integrationArguments += '--capture' }
    & $pythonPath @integrationArguments
    if ($LASTEXITCODE -ne 0) { throw 'Actual game and service restart checks failed.' }
} finally {
    Pop-Location
}
