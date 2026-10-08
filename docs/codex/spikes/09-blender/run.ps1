param(
    [ValidateSet('box', 'jar', 'verify')][string]$Object = 'box',
    [string]$OutputDirectory = (Join-Path $env:TEMP 'enfractal-09-blender-spike/artifacts'),
    [string]$Blender = 'C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe',
    [string[]]$RecipeArguments = @()
)
$ErrorActionPreference = 'Stop'
$taskTemp = [System.IO.Path]::GetFullPath((Join-Path $env:TEMP 'enfractal-09-blender-spike'))
$taskOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$env:BLENDER_USER_RESOURCES = Join-Path $taskTemp 'user'
$env:BLENDER_USER_CONFIG = Join-Path $taskTemp 'user/config'
$env:BLENDER_USER_DATAFILES = Join-Path $taskTemp 'user/datafiles'
$env:BLENDER_USER_SCRIPTS = Join-Path $taskTemp 'user/scripts'
$env:TEMP = Join-Path $taskTemp 'tmp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $taskOutput,$env:TEMP,$env:BLENDER_USER_CONFIG,$env:BLENDER_USER_DATAFILES,$env:BLENDER_USER_SCRIPTS | Out-Null
$scriptPath = Join-Path $PSScriptRoot "$Object.py"
$logPath = Join-Path $taskOutput "$Object.log"
$watch = [System.Diagnostics.Stopwatch]::StartNew()
Write-Output "RUN object=$Object background=true config=$env:BLENDER_USER_CONFIG temp=$env:TEMP"
& $Blender --background --factory-startup --python-exit-code 1 --python $scriptPath -- --out $taskOutput @RecipeArguments 2>&1 | Tee-Object -FilePath $logPath
$blenderExit = $LASTEXITCODE
$watch.Stop()
$line = "PROCESS object=$Object exit=$blenderExit wall_seconds=$($watch.Elapsed.TotalSeconds.ToString('F3', [Globalization.CultureInfo]::InvariantCulture))"
Write-Output $line
Add-Content -LiteralPath $logPath -Value $line
exit $blenderExit
