#requires -Version 7.0
# Runs the small-avatar physics fixture, then boots the placeholder room headlessly.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'native-toolchain.ps1')
$toolchain = Get-EnfractalNativeToolchain
Build-EnfractalNativeProject -Toolchain $toolchain
$import = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--editor', '--path', $toolchain.ProjectPath, '--import') -TimeoutSeconds 120
if ($import.Stderr -match 'ERROR:|SCRIPT ERROR:') { throw $import.Stderr }
$avatar = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--path', $toolchain.ProjectPath, '--fixed-fps', '60', 'res://tests/native_small_avatar.tscn') -TimeoutSeconds 90
if ($avatar.Stderr -or $avatar.Stdout -notmatch 'checks passed') { throw "$($avatar.Stdout)`n$($avatar.Stderr)" }
Write-Output $avatar.Stdout.Trim()
$room = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--path', $toolchain.ProjectPath, '--fixed-fps', '60', '--quit-after', '240', 'res://scenes/room_test.tscn') -TimeoutSeconds 60
if ($room.Stderr -or $room.Stdout -notmatch 'ROOM_WORLD_READY') { throw "Placeholder room failed to load:`n$($room.Stdout)`n$($room.Stderr)" }
Write-Output $room.Stdout.Trim()
