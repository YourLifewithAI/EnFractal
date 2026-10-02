#requires -Version 7.0
param([switch]$Capture)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'native-toolchain.ps1')
$toolchain = Get-EnfractalNativeToolchain
Build-EnfractalNativeProject -Toolchain $toolchain
$import = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath `
    -Arguments @('--headless', '--editor', '--path', $toolchain.ProjectPath, '--import') -TimeoutSeconds 120
if ($import.Stderr -match 'ERROR:|SCRIPT ERROR:') { throw $import.Stderr }
foreach ($scene in @('native_small_avatar', 'native_pfluger_scene')) {
    $arguments = @('--path', $toolchain.ProjectPath, '--fixed-fps', '60', "res://tests/$scene.tscn")
    if ($Capture -and $scene -eq 'native_pfluger_scene') { $arguments += @('--', '--capture-pfluger') }
    else { $arguments = @('--headless') + $arguments }
    $result = Invoke-EnfractalNativeProcess -Toolchain $toolchain -FilePath $toolchain.EnginePath -Arguments $arguments -TimeoutSeconds 90
    if ($result.Stderr -or $result.Stdout -notmatch 'checks passed') { throw "$($result.Stdout)`n$($result.Stderr)" }
    Write-Output $result.Stdout.Trim()
}
