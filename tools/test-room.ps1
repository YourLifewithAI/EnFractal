#requires -Version 7.0
# Runs the small-avatar fixture, the room navigation fixture and the room data/builder fixture, plays the 1x garage landscape,
# then boots the default room headlessly and the landscape through the game's main scene.
# -Scale runs the scale suite instead, and only it: the 10x garage (built once into the shared fixture cache, about 4 min), its
# load, navigation, memory, a long walk across it and a come from far away, one SCALE_* line each (tests/native_scale_suite.tscn).
param([switch]$Scale)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'native-toolchain.ps1')
. (Join-Path $PSScriptRoot 'landscape-fixture.ps1')
$toolchain = Get-EnfractalNativeToolchain
Build-EnfractalNativeProject -Toolchain $toolchain
$import = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--editor', '--path', $toolchain.ProjectPath, '--import') -TimeoutSeconds 120
if ($import.Stderr -match 'ERROR:|SCRIPT ERROR:') { throw $import.Stderr }
if ($Scale) {
    $island = Get-EnfractalLandscapeFixture -Toolchain $toolchain -Scale 10
    $suite = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath -TimeoutSeconds 1200 `
        -Arguments @('--headless', '--path', $toolchain.ProjectPath, '--fixed-fps', '60', 'res://tests/native_scale_suite.tscn', '--', "--landscape=$($island.Room)")
    if ($suite.Stderr -or $suite.Stdout -notmatch 'NATIVE_SCALE_SUITE: \d+/\d+ checks passed') { throw "$($suite.Stdout)`n$($suite.Stderr)" }
    Write-Output "SCALE_FIXTURE x10 $($island.Key): generated in $($island.Record.seconds.generate) s, exported in $($island.Record.seconds.export) s, $([math]::Round($island.Record.room_bytes / 1MB)) MB"
    Write-Output (($suite.Stdout.Trim().Split("`n") | Where-Object { $_ -match '^(SCALE_|NATIVE_SCALE_SUITE)' }) -join "`n")
    return
}
$avatar = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--path', $toolchain.ProjectPath, '--fixed-fps', '60', 'res://tests/native_small_avatar.tscn') -TimeoutSeconds 90
if ($avatar.Stderr -or $avatar.Stdout -notmatch 'checks passed') { throw "$($avatar.Stdout)`n$($avatar.Stderr)" }
Write-Output $avatar.Stdout.Trim()
$navigation = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--path', $toolchain.ProjectPath, '--fixed-fps', '60', 'res://tests/native_room_navigation.tscn') -TimeoutSeconds 90
if ($navigation.Stderr -or $navigation.Stdout -notmatch 'checks passed') { throw "$($navigation.Stdout)`n$($navigation.Stderr)" }
Write-Output $navigation.Stdout.Trim()
# Play on the land: the 1x garage landscape, derived data never committed, from the fixture cache every worktree shares
# (tools/landscape-fixture.ps1; built there first, about 35 s, when the generator, exporter, harness or corpus room changed).
$landscapeRoom = (Get-EnfractalLandscapeFixture -Toolchain $toolchain -Scale 1).Room
$landscape = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--path', $toolchain.ProjectPath, '--fixed-fps', '60', 'res://tests/native_kernel_landscape.tscn', '--', "--landscape=$landscapeRoom") -TimeoutSeconds 300
if ($landscape.Stderr -or $landscape.Stdout -notmatch 'NATIVE_KERNEL_LANDSCAPE: \d+/\d+ checks passed') { throw "$($landscape.Stdout)`n$($landscape.Stderr)" }
Write-Output (($landscape.Stdout.Trim().Split("`n") | Where-Object { $_ -match '^(LANDSCAPE_|NATIVE_KERNEL_LANDSCAPE)' }) -join "`n")
$roomData = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--path', $toolchain.ProjectPath, '--fixed-fps', '60', 'res://tests/native_room_data.tscn') -TimeoutSeconds 90
if ($roomData.Stderr -or $roomData.Stdout -notmatch 'checks passed') { throw "$($roomData.Stdout)`n$($roomData.Stderr)" }
Write-Output $roomData.Stdout.Trim()
# The 10x world's loading (S6): plant copies and terrain tiles read, drawn per tile, built per frame, collided from their rows.
$island = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--path', $toolchain.ProjectPath, '--fixed-fps', '60', 'res://tests/native_island_loading.tscn') -TimeoutSeconds 180
if ($island.Stderr -or $island.Stdout -notmatch 'NATIVE_ISLAND_LOADING: \d+/\d+ checks passed') { throw "$($island.Stdout)`n$($island.Stderr)" }
Write-Output (($island.Stdout.Trim().Split("`n") | Where-Object { $_ -match '^(ISLAND_|NATIVE_ISLAND)' }) -join "`n")
# The look on the real garage too (its landscape and open-sea checks skip without --landscape).
$look = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--path', $toolchain.ProjectPath, '--fixed-fps', '60', 'res://tests/native_look_preset.tscn', '--', "--landscape=$landscapeRoom") -TimeoutSeconds 90
if ($look.Stderr -or $look.Stdout -notmatch 'checks passed' -or $look.Stdout -match 'checks were skipped') { throw "$($look.Stdout)`n$($look.Stderr)" }
Write-Output $look.Stdout.Trim()
$room = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--path', $toolchain.ProjectPath, '--fixed-fps', '60', 'res://scenes/room.tscn', '--', '--quit-after-ready=240') -TimeoutSeconds 60
if ($room.Stderr -or $room.Stdout -notmatch 'ROOM_WORLD_READY' -or $room.Stdout -notmatch 'ROOM_WORLD_QUIT') { throw "Placeholder room failed to load:`n$($room.Stdout)`n$($room.Stderr)" }
Write-Output $room.Stdout.Trim()
# The landscape as the player opens it: installed in the (runner's) user folder and booted by id through the main scene, which
# reads --room, quitting by --quit-after-ready (a smoke boot of 13 October never quit; tools/smoke-boot.ps1).
$installed = Join-Path $toolchain.RepositoryPath '.cache/native-test/appdata/Godot/app_userdata/EnFractal/rooms/landscape_garage_nominal'
if (Test-Path -LiteralPath $installed) { Remove-Item -LiteralPath $installed -Recurse -Force }
New-Item -ItemType Directory -Force (Split-Path -Parent $installed) | Out-Null
Copy-Item -LiteralPath $landscapeRoom -Destination $installed -Recurse
$boot = Invoke-EnfractalRoomBoot -Toolchain $toolchain -Room 'landscape_garage_nominal' -Frames 240 -TimeoutSeconds 120
Write-Output "$($boot.Ready)`n$($boot.Quit)"
Write-Output ("MAIN_SCENE_BOOT landscape_garage_nominal from the user folder: ready, quit, exit 0 in {0:0.0} s" -f $boot.Seconds)
