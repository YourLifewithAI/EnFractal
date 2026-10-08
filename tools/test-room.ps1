#requires -Version 7.0
# Runs the small-avatar fixture, the room navigation fixture and the room data/builder fixture, then boots the default room headlessly.
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
$navigation = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--path', $toolchain.ProjectPath, '--fixed-fps', '60', 'res://tests/native_room_navigation.tscn') -TimeoutSeconds 90
if ($navigation.Stderr -or $navigation.Stdout -notmatch 'checks passed') { throw "$($navigation.Stdout)`n$($navigation.Stderr)" }
Write-Output $navigation.Stdout.Trim()
# Play on the land: the garage landscape, generated and exported at test time (derived data, never committed; about 35 s),
# cached under .cache/landscape-fixture by a hash of the generator, the exporter, the package format and the corpus room.
$repository = Split-Path -Parent $PSScriptRoot
$landscapeInputs = @('pipeline/landscape/generator', 'pipeline/landscape/export', 'pipeline/landscape/harness', 'pipeline/landscape/corpus/rooms/garage_nominal') |
    ForEach-Object { Get-ChildItem -LiteralPath (Join-Path $repository $_) -Recurse -File -Include '*.py', '*.json' } |
    Where-Object { $_.FullName -notmatch '[\\/](tests|renders|__pycache__)[\\/]' } | Sort-Object FullName |
    ForEach-Object { [System.IO.Path]::GetRelativePath($repository, $_.FullName).Replace('\', '/') + ' ' + (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash }
$landscapeKey = (Get-FileHash -Algorithm SHA256 -InputStream ([System.IO.MemoryStream]::new([System.Text.Encoding]::UTF8.GetBytes($landscapeInputs -join "`n")))).Hash.Substring(0, 16).ToLowerInvariant()
$fixtureRoot = Join-Path $repository '.cache/landscape-fixture'
$landscapeRoom = Join-Path $fixtureRoot "$landscapeKey/landscape_garage_nominal"
if (-not (Test-Path -LiteralPath (Join-Path $landscapeRoom 'room.json'))) {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
    $python = (Get-Command python -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    New-Item -ItemType Directory -Force (Join-Path $fixtureRoot $landscapeKey) | Out-Null
    $package = Join-Path $fixtureRoot "$landscapeKey/package"
    $generate = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $python -WorkingDirectory $repository -TimeoutSeconds 300 `
        -Arguments @('-B', '-m', 'pipeline.landscape.generator.generate', '--room', 'pipeline/landscape/corpus/rooms/garage_nominal', '--out', $package)
    $export = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $python -WorkingDirectory $repository -TimeoutSeconds 300 `
        -Arguments @('-B', '-S', '-m', 'pipeline.landscape.export', '--package', $package, '--room', 'pipeline/landscape/corpus/rooms/garage_nominal', '--room-id', 'landscape_garage_nominal', '--out', $landscapeRoom)
    Write-Output (($generate.Stdout + $export.Stdout).Trim())
}
$landscape = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--path', $toolchain.ProjectPath, '--fixed-fps', '60', 'res://tests/native_kernel_landscape.tscn', '--', "--landscape=$landscapeRoom") -TimeoutSeconds 300
if ($landscape.Stderr -or $landscape.Stdout -notmatch 'NATIVE_KERNEL_LANDSCAPE: \d+/\d+ checks passed') { throw "$($landscape.Stdout)`n$($landscape.Stderr)" }
Write-Output (($landscape.Stdout.Trim().Split("`n") | Where-Object { $_ -match '^(LANDSCAPE_|NATIVE_KERNEL_LANDSCAPE)' }) -join "`n")
$roomData = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--path', $toolchain.ProjectPath, '--fixed-fps', '60', 'res://tests/native_room_data.tscn') -TimeoutSeconds 90
if ($roomData.Stderr -or $roomData.Stdout -notmatch 'checks passed') { throw "$($roomData.Stdout)`n$($roomData.Stderr)" }
Write-Output $roomData.Stdout.Trim()
$look = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--path', $toolchain.ProjectPath, '--fixed-fps', '60', 'res://tests/native_look_preset.tscn') -TimeoutSeconds 90
if ($look.Stderr -or $look.Stdout -notmatch 'checks passed') { throw "$($look.Stdout)`n$($look.Stderr)" }
Write-Output $look.Stdout.Trim()
$room = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--path', $toolchain.ProjectPath, '--fixed-fps', '60', '--quit-after', '240', 'res://scenes/room.tscn') -TimeoutSeconds 60
if ($room.Stderr -or $room.Stdout -notmatch 'ROOM_WORLD_READY') { throw "Placeholder room failed to load:`n$($room.Stdout)`n$($room.Stderr)" }
Write-Output $room.Stdout.Trim()
