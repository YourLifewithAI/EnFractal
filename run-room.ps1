#requires -Version 7.0
# -Style v2 plays the default style's v2 (or -Style <preset_id>/v<N>) instead of the room's pinned look; -Room <id> picks the room,
# -Room landscape the garage island from the shared fixture cache, and -Room landscape10 the same island at the 10x world's scale. -Profile <name> keeps this play's saves, settings and
# companion session under .cache/profiles/<name> instead of your own user folders, so a playtest can be repeated or reset.
param([string]$Style, [string]$Room, [string]$Profile)
$ErrorActionPreference = 'Stop'
if ($Profile -and $Profile -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$') { throw '-Profile takes a short name of letters, digits, - and _.' }
. (Join-Path $PSScriptRoot 'tools/native-toolchain.ps1')
$nativeToolchain = Get-EnfractalNativeToolchain
if ($Room -eq 'landscape' -or $Room -eq 'landscape10') {
    # The garage island from the shared fixture cache (tools/landscape-fixture.ps1), built there first if it is missing.
    . (Join-Path $PSScriptRoot 'tools/landscape-fixture.ps1')
    $scale = if ($Room -eq 'landscape10') { 10 } else { 1 }
    $Room = (Get-EnfractalLandscapeFixture -Toolchain $nativeToolchain -Scale $scale).Room.Replace('\', '/')
}
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
