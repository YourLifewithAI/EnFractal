#requires -Version 7.0
param([string]$EnginePath, [string]$DotnetPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'native-toolchain.ps1')
$nativeToolchain = Get-EnfractalNativeToolchain -EnginePath $EnginePath -DotnetPath $DotnetPath
$template = Join-Path $nativeToolchain.RepositoryPath '.cache/godot-export/templates/windows_release_x86_64.exe'
if (-not (Test-Path -LiteralPath $template -PathType Leaf)) {
    throw 'Missing .NET export templates. Run tools/bootstrap-native.ps1 -IncludeExportTemplates.'
}
Build-EnfractalNativeProject -Toolchain $nativeToolchain
$outputPath = Join-Path $nativeToolchain.RepositoryPath '.cache/releases/windows/EnFractal.exe'
New-Item -ItemType Directory -Force (Split-Path -Parent $outputPath) | Out-Null
$export = Invoke-EnfractalNativeProcess -Toolchain $nativeToolchain -FilePath $nativeToolchain.EnginePath `
    -Arguments @('--headless', '--path', $nativeToolchain.ProjectPath, '--export-release', 'Windows Native', $outputPath) -TimeoutSeconds 240
if ($export.Stderr -match 'ERROR:|SCRIPT ERROR:') { throw "Export reported an error:`n$($export.Stderr)" }
$probeWorkingPath = Join-Path $nativeToolchain.RepositoryPath '.cache/export-probe-empty'
New-Item -ItemType Directory -Force $probeWorkingPath | Out-Null
$probe = Invoke-EnfractalNativeProcess -Toolchain $nativeToolchain -FilePath $outputPath `
    -Arguments @('--headless', '--', '--native-contract-probe') -TimeoutSeconds 30 `
    -WorkingDirectory $probeWorkingPath -WithoutDotnetRuntime
if ($probe.Stderr -or $probe.Stdout -notmatch 'Native release probe passed:') {
    throw "Exported native runtime failed its contract probe:`n$($probe.Stdout)`n$($probe.Stderr)"
}
Write-Output $probe.Stdout.Trim()
Write-Output "Native Windows build: $outputPath"
