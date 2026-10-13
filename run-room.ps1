#requires -Version 7.0
# -Style v2 plays the default style's v2 (or -Style <preset_id>/v<N>) instead of the room's pinned look; -Room <id> picks the room,
# and -Room landscape the newest landscape tools/test-room.ps1 prepared. -Profile <name> keeps this play's saves, settings and
# companion session under .cache/profiles/<name> instead of your own user folders, so a playtest can be repeated or reset.
param([string]$Style, [string]$Room, [string]$Profile)
$ErrorActionPreference = 'Stop'
if ($Profile -and $Profile -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$') { throw '-Profile takes a short name of letters, digits, - and _.' }
if ($Room -eq 'landscape') {
    $manifest = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '.cache/landscape-fixture') -Filter 'room.json' -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'landscape_garage_nominal' } |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (-not $manifest) { throw 'Run tools/test-room.ps1 once to prepare the landscape.' }
    $Room = $manifest.Directory.FullName.Replace('\', '/')
}
. (Join-Path $PSScriptRoot 'tools/native-toolchain.ps1')
$nativeToolchain = Get-EnfractalNativeToolchain
Build-EnfractalNativeProject -Toolchain $nativeToolchain
$env:DOTNET_ROOT = Split-Path -Parent $nativeToolchain.DotnetPath
$env:PATH = $env:DOTNET_ROOT + [System.IO.Path]::PathSeparator + $env:PATH
$userArgs = @()
if ($Style) { $userArgs += "--style=$Style" }
if ($Room) { $userArgs += "--room=$Room" }
if ($userArgs) { $userArgs = @('--') + $userArgs }
$priorRoaming = $env:APPDATA
$priorLocal = $env:LOCALAPPDATA
$exitCode = 1
try {
    if ($Profile) {
        # Godot's user:// (saves, wishes, the companion's session file) follows APPDATA on Windows.
        $profileRoot = Join-Path $PSScriptRoot ".cache/profiles/$Profile"
        $env:APPDATA = Join-Path $profileRoot 'roaming'
        $env:LOCALAPPDATA = Join-Path $profileRoot 'local'
        New-Item -ItemType Directory -Force $env:APPDATA, $env:LOCALAPPDATA | Out-Null
    }
    & (Join-Path $PSScriptRoot 'tools/ensure-godot-art-imports.ps1') -EnginePath $nativeToolchain.EnginePath -ProjectPath $nativeToolchain.ProjectPath
    & $nativeToolchain.EnginePath --path $nativeToolchain.ProjectPath @userArgs
    $exitCode = $LASTEXITCODE
} finally {
    $env:APPDATA = $priorRoaming
    $env:LOCALAPPDATA = $priorLocal
}
exit $exitCode
