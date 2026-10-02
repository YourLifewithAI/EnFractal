$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $modernShell = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue
    if (-not $modernShell) {
        throw 'PowerShell 7 or newer is required for reliable Godot process exit-code reporting.'
    }
    & $modernShell.Source -NoProfile -File $PSCommandPath
    exit $LASTEXITCODE
}

# All developer launchers now use the pinned .NET engine and compiled C# entry.
. (Join-Path $PSScriptRoot 'tools/native-toolchain.ps1')
$nativeToolchain = Get-EnfractalNativeToolchain
Build-EnfractalNativeProject -Toolchain $nativeToolchain
$projectPath = $nativeToolchain.ProjectPath
$enginePath = $nativeToolchain.EnginePath
$env:DOTNET_ROOT = Split-Path -Parent $nativeToolchain.DotnetPath
$env:PATH = $env:DOTNET_ROOT + [System.IO.Path]::PathSeparator + $env:PATH

& (Join-Path $PSScriptRoot 'tools\ensure-godot-art-imports.ps1') -EnginePath $enginePath -ProjectPath $projectPath

Write-Output 'Opening the retired Barton regression fixture. Use run-pfluger.ps1 for the active game.'
& $enginePath --path $projectPath -- --legacy-barton
exit $LASTEXITCODE
