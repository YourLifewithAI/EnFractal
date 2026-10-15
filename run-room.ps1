#requires -Version 7.0
# -Style v2 plays the default style's v2 (or -Style <preset_id>/v<N>) instead of the room's pinned look; -Room <id> picks the room,
# -Room landscape the garage island from the shared fixture cache, and -Room landscape10 the same island at the 10x world's scale. -Profile <name> keeps this play's saves, settings and
# companion session under .cache/profiles/<name> instead of your own user folders, so a playtest can be repeated or reset.
# -Team <team id>, -Mode creative|challenge|live and -Difficulty gentle|standard|brave (with -Mode challenge) pass the team, the
# mode and the difficulty to the game as --team=, --mode= and --difficulty= (the mechanics round, contracts/README.md "Teams and the
# save layout"). The teams packet (M3) makes the boot honour them and refuse Live; until then the game ignores them.
# -Knack quick_feet:3 (or -Knack quick_feet:3,cat_feet:2, up to three; the rank 1 to 3, 1 when left off) passes the team's knacks to the game as
# --knack= until the team file (M3) holds them: the body takes the pack's rank for each, none by default (the mechanics round, M5).
param([string]$Style, [string]$Room, [string]$Profile, [string]$Team, [string]$Mode, [string]$Difficulty, [string[]]$Knack)
$ErrorActionPreference = 'Stop'
if ($Profile -and $Profile -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$') { throw '-Profile takes a short name of letters, digits, - and _.' }
if ($Team -and $Team -cnotmatch '^[a-z][a-z0-9_-]{0,63}$') { throw '-Team takes a team id: lowercase letters, digits, - and _, starting with a letter.' }
if ($Mode -and $Mode -cnotin @('creative', 'challenge', 'live')) { throw '-Mode is creative, challenge or live.' }
if ($Difficulty -and ($Difficulty -cnotin @('gentle', 'standard', 'brave') -or $Mode -cne 'challenge')) { throw '-Difficulty is gentle, standard or brave, with -Mode challenge.' }
$knackArgs = @($Knack | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($knackArgs.Count -gt 3) { throw '-Knack takes up to three knacks, one for each slot.' }
foreach ($knackArg in $knackArgs) { if ($knackArg -cnotmatch '^[a-z][a-z0-9_-]{0,63}(:[1-3])?$') { throw "-Knack takes a knack id and a rank, like quick_feet:3 (rank 1 to 3, 1 when left off): '$knackArg'." } }
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
if ($Team) { $userArgs += "--team=$Team" }
if ($Mode) { $userArgs += "--mode=$Mode" }
if ($Difficulty) { $userArgs += "--difficulty=$Difficulty" }
foreach ($knackArg in $knackArgs) { $userArgs += "--knack=$knackArg" }
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
