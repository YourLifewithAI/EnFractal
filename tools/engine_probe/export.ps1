param(
    [string]$GodotPath = '',
    [string]$ArchivePath = '',
    [string]$OutputDirectory = '',
    [string]$SnapshotRef = '',
    [switch]$DownloadTemplates
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not $GodotPath) {
    $GodotPath = if ($env:ENFRACTAL_GODOT) { $env:ENFRACTAL_GODOT } else { Join-Path $env:USERPROFILE 'Downloads\Godot_v4.7.2-stable_win64.exe\Godot_v4.7.2-stable_win64.exe' }
}
if (-not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) { throw "Godot editor not found: $GodotPath" }
$cacheRoot = Join-Path $repoRoot '.cache\engine_probe'
New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
if (-not $ArchivePath) { $ArchivePath = Join-Path $cacheRoot 'Godot_v4.7.2-stable_export_templates.tpz' }
$templateUrl = 'https://github.com/godotengine/godot-builds/releases/download/4.7.2-stable/Godot_v4.7.2-stable_export_templates.tpz'
$expectedSha256 = 'f298490b8d44d934be425a5a65a51bf15f422428b229a06a6e11d9ffea248011'
if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
    if (-not $DownloadTemplates) { throw "Official 4.7.2 templates archive missing at $ArchivePath (1,281,349,702 bytes). Re-run with -DownloadTemplates." }
    curl.exe --location --fail --retry 3 --continue-at - --silent --show-error --output $ArchivePath $templateUrl
    if ($LASTEXITCODE -ne 0) { throw "Template download failed with code $LASTEXITCODE" }
}
$archiveInfo = Get-Item -LiteralPath $ArchivePath
if ($archiveInfo.Length -ne 1281349702) { throw "Template archive length mismatch: $($archiveInfo.Length)" }
$actualSha256 = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha256 -ne $expectedSha256) { throw "Official template SHA-256 mismatch: $actualSha256" }

$templateDir = Join-Path $cacheRoot 'templates\4.7.2.stable'
New-Item -ItemType Directory -Path $templateDir -Force | Out-Null
$templateHashes = @{
    'windows_release_x86_64.exe' = 'd34d36f3be1a6c49c56525ae86469b92e4f417ddf0b43cf00dd80c385c4b0562'
    'windows_release_x86_64_console.exe' = '52bdcae9068e8d23b840e5c63b0c1798ffe2cb66144e5c4bc7af11fb8c8600df'
    'linux_release.x86_64' = 'd9f79ab89b5ae369aeed11c6052d402e8218cd503bf85b4a235f9c30c46a7c63'
    'version.txt' = '38885c88f75abbc797a1db7559719800279a00cfb9fa8b2e2868a7f6f84bad2e'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
try {
    foreach ($name in @('windows_release_x86_64.exe', 'windows_release_x86_64_console.exe', 'linux_release.x86_64', 'version.txt')) {
        $entry = $zip.GetEntry('templates/' + $name)
        if (-not $entry) { throw "Official template entry missing: $name" }
        $target = Join-Path $templateDir $name
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            if ((Get-Item -LiteralPath $target).Length -ne $entry.Length) { throw "Cached template size mismatch: $target" }
            continue
        }
        $sourceStream = $entry.Open()
        $targetStream = [System.IO.File]::Create($target)
        try { $sourceStream.CopyTo($targetStream) } finally { $targetStream.Dispose(); $sourceStream.Dispose() }
    }
} finally { $zip.Dispose() }
foreach ($name in $templateHashes.Keys) {
    $actual = (Get-FileHash -LiteralPath (Join-Path $templateDir $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $templateHashes[$name]) { throw "Extracted template SHA-256 mismatch: $name" }
}
if ((Get-Content -LiteralPath (Join-Path $templateDir 'version.txt') -Raw).Trim() -ne '4.7.2.stable') { throw 'Template version does not match Godot 4.7.2 stable' }

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot ('results\export-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$projectCopy = Join-Path $OutputDirectory 'project'
if (Test-Path -LiteralPath $projectCopy) { throw "Export snapshot path already exists: $projectCopy" }
if ($SnapshotRef) {
    $sourceArchive = Join-Path $OutputDirectory 'project-source.zip'
    $objectRef = $SnapshotRef + ':game'
    & git -C $repoRoot archive '--format=zip' "--output=$sourceArchive" $objectRef
    if ($LASTEXITCODE -ne 0) { throw "Could not archive game at Git ref $SnapshotRef" }
    [System.IO.Compression.ZipFile]::ExtractToDirectory($sourceArchive, $projectCopy)
} else {
    New-Item -ItemType Directory -Path $projectCopy | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'game') -Force | Where-Object { $_.Name -ne '.godot' } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $projectCopy -Recurse
    }
}
$sourceCommit = if ($SnapshotRef) {
    $commitRef = $SnapshotRef + '^{commit}'
    (& git -C $repoRoot rev-parse $commitRef).Trim()
} else {
    (& git -C $repoRoot rev-parse HEAD).Trim()
}
if ($LASTEXITCODE -ne 0 -or -not $sourceCommit) { throw 'Could not resolve source Git commit' }
$sourceLines = @(Get-ChildItem -LiteralPath $projectCopy -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($projectCopy.Length + 1).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relative"
})
$sourceManifestPath = Join-Path $OutputDirectory 'source-files.sha256'
[System.IO.File]::WriteAllLines($sourceManifestPath, $sourceLines, (New-Object System.Text.UTF8Encoding($false)))
$sourceManifestHash = (Get-FileHash -LiteralPath $sourceManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
$windowsOutput = Join-Path $OutputDirectory 'Enfractal-Windows-Phase0.exe'
$linuxOutput = Join-Path $OutputDirectory 'Enfractal-Linux-Phase0.x86_64'
$windowsPck = [System.IO.Path]::ChangeExtension($windowsOutput, '.pck')
$linuxPck = [System.IO.Path]::ChangeExtension($linuxOutput, '.pck')
if ($windowsPck -eq $linuxPck) { throw 'Windows and Linux export PCK paths must be distinct' }
$windowsTemplate = (Join-Path $templateDir 'windows_release_x86_64.exe').Replace('\', '/')
$linuxTemplate = (Join-Path $templateDir 'linux_release.x86_64').Replace('\', '/')
$windowsExport = $windowsOutput.Replace('\', '/')
$linuxExport = $linuxOutput.Replace('\', '/')
$preset = @"
[preset.0]
name="Windows Client"
platform="Windows Desktop"
runnable=true
dedicated_server=false
custom_features=""
export_filter="all_resources"
include_filter="*.json,*.r16,*.md"
exclude_filter=""
export_path="$windowsExport"
encrypt_pck=false
encrypt_directory=false

[preset.0.options]
custom_template/debug=""
custom_template/release="$windowsTemplate"
debug/export_console_wrapper=0
binary_format/architecture="x86_64"
binary_format/embed_pck=false

[preset.1]
name="Linux Headless"
platform="Linux"
runnable=false
dedicated_server=true
custom_features=""
export_filter="all_resources"
include_filter="*.json,*.r16,*.md"
exclude_filter=""
export_path="$linuxExport"
encrypt_pck=false
encrypt_directory=false

[preset.1.options]
custom_template/debug=""
custom_template/release="$linuxTemplate"
binary_format/architecture="x86_64"
binary_format/embed_pck=false
"@
[System.IO.File]::WriteAllText((Join-Path $projectCopy 'export_presets.cfg'), $preset, (New-Object System.Text.UTF8Encoding($false)))

function Invoke-Godot {
    param([string]$Name, [string]$Arguments, [int]$TimeoutSeconds = 180)
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $GodotPath
    $startInfo.Arguments = $Arguments
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        $process.WaitForExit()
        throw "$Name timed out after $TimeoutSeconds seconds"
    }
    $stdout = $stdoutTask.Result
    $stderr = $stderrTask.Result
    Set-Content -LiteralPath (Join-Path $OutputDirectory "$Name.stdout.txt") -Value $stdout -Encoding utf8
    Set-Content -LiteralPath (Join-Path $OutputDirectory "$Name.stderr.txt") -Value $stderr -Encoding utf8
    if ($process.ExitCode -ne 0 -or $stderr -match '(?m)^(SCRIPT ERROR|ERROR:)' -or $stdout -match '(?m)^(SCRIPT ERROR|ERROR:)') {
        throw "$Name failed (exit $($process.ExitCode)); inspect $OutputDirectory"
    }
    return [ordered]@{ name = $Name; exit_code = $process.ExitCode; stdout = (Join-Path $OutputDirectory "$Name.stdout.txt"); stderr = (Join-Path $OutputDirectory "$Name.stderr.txt") }
}

$quotedProject = '"' + $projectCopy + '"'
$versionRun = Invoke-Godot -Name 'version' -Arguments '--headless --version' -TimeoutSeconds 20
$versionText = (Get-Content -LiteralPath $versionRun.stdout -Raw).Trim()
if ($versionText -ne '4.7.2.stable.official.ed1daf0bf') { throw "Editor version mismatch: $versionText" }
$importRun = Invoke-Godot -Name 'import' -Arguments "--headless --editor --path $quotedProject --import"
$windowsRun = Invoke-Godot -Name 'export-windows' -Arguments "--headless --path $quotedProject --export-release `"Windows Client`" `"$windowsOutput`""
$linuxRun = Invoke-Godot -Name 'export-linux' -Arguments "--headless --path $quotedProject --export-release `"Linux Headless`" `"$linuxOutput`""
foreach ($path in @($windowsOutput, $linuxOutput, $windowsPck, $linuxPck)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Expected export artifact missing: $path" }
}
$summary = [ordered]@{
    timestamp_utc = (Get-Date).ToUniversalTime().ToString('o')
    editor_version = $versionText
    template_archive_sha256 = $actualSha256
    template_archive_bytes = $archiveInfo.Length
    source_ref = if ($SnapshotRef) { $SnapshotRef } else { 'working-tree snapshot' }
    source_base_commit = $sourceCommit
    source_file_count = $sourceLines.Count
    source_manifest_sha256 = $sourceManifestHash
    source_manifest_path = $sourceManifestPath
    snapshot_project = $projectCopy
    windows_artifact = $windowsOutput
    windows_artifact_bytes = (Get-Item -LiteralPath $windowsOutput).Length
    windows_artifact_sha256 = (Get-FileHash -LiteralPath $windowsOutput -Algorithm SHA256).Hash.ToLowerInvariant()
    linux_artifact = $linuxOutput
    linux_artifact_bytes = (Get-Item -LiteralPath $linuxOutput).Length
    linux_artifact_sha256 = (Get-FileHash -LiteralPath $linuxOutput -Algorithm SHA256).Hash.ToLowerInvariant()
    windows_pck_bytes = (Get-Item -LiteralPath $windowsPck).Length
    windows_pck_sha256 = (Get-FileHash -LiteralPath $windowsPck -Algorithm SHA256).Hash.ToLowerInvariant()
    linux_pck_bytes = (Get-Item -LiteralPath $linuxPck).Length
    linux_pck_sha256 = (Get-FileHash -LiteralPath $linuxPck -Algorithm SHA256).Hash.ToLowerInvariant()
    import = $importRun
    windows_export = $windowsRun
    linux_export = $linuxRun
    note = 'Artifacts built from a snapshot copy of game/. Linux binary still requires a Linux runtime test; this script does not run it.'
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'export-summary.json') -Encoding utf8
$summary | ConvertTo-Json -Depth 8
