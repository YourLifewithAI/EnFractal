param(
    [Parameter(Mandatory = $true)][string]$ExportDirectory,
    [string]$LinuxWslDistro = 'Ubuntu',
    [switch]$SkipLinux
)

$ErrorActionPreference = 'Stop'
$ExportDirectory = (Resolve-Path -LiteralPath $ExportDirectory).Path
$windowsExe = Join-Path $ExportDirectory 'Enfractal-Windows-Phase0.exe'
$windowsPck = Join-Path $ExportDirectory 'Enfractal-Windows-Phase0.pck'
$linuxExe = Join-Path $ExportDirectory 'Enfractal-Linux-Phase0.x86_64'
$linuxPck = Join-Path $ExportDirectory 'Enfractal-Linux-Phase0.pck'
foreach ($path in @($windowsExe, $windowsPck, $linuxExe, $linuxPck)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Export artifact missing: $path" }
}

function Invoke-Hidden {
    param(
        [string]$Name,
        [string]$Executable,
        [string]$Arguments,
        [hashtable]$Environment = @{},
        [int]$TimeoutSeconds = 60
    )
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $Executable
    $startInfo.Arguments = $Arguments
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($key in $Environment.Keys) { $startInfo.EnvironmentVariables[$key] = $Environment[$key] }
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        $process.WaitForExit()
        throw "$Name exceeded $TimeoutSeconds seconds"
    }
    $stdout = $stdoutTask.Result
    $stderr = $stderrTask.Result
    $stdoutPath = Join-Path $ExportDirectory "$Name.stdout.txt"
    $stderrPath = Join-Path $ExportDirectory "$Name.stderr.txt"
    Set-Content -LiteralPath $stdoutPath -Value $stdout -Encoding utf8
    Set-Content -LiteralPath $stderrPath -Value $stderr -Encoding utf8
    return [ordered]@{ exit_code = $process.ExitCode; stdout = $stdout; stderr = $stderr; stdout_path = $stdoutPath; stderr_path = $stderrPath }
}

$capturePath = Join-Path $ExportDirectory 'windows-release-smoke.png'
$metricsPath = Join-Path $ExportDirectory 'windows-release-smoke-metrics.json'
$windows = Invoke-Hidden -Name 'windows-release-smoke' -Executable $windowsExe -Arguments '' -Environment @{
    ENFRACTAL_CAPTURE = $capturePath
    ENFRACTAL_METRICS = $metricsPath
    ENFRACTAL_VIEW = 'pin'
}
if ($windows.exit_code -ne 0 -or -not [string]::IsNullOrWhiteSpace($windows.stderr) -or -not $windows.stdout.Contains('Compatibility - Using Device:') -or -not $windows.stdout.Contains('Barton Creek map loaded:') -or -not $windows.stdout.Contains('Map screenshot saved:') -or -not (Test-Path -LiteralPath $capturePath -PathType Leaf)) {
    throw "Windows release graphics smoke failed; inspect $ExportDirectory"
}

$linux = $null
if (-not $SkipLinux) {
    if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) { throw 'wsl.exe unavailable; run with -SkipLinux or provide an Ubuntu WSL distro' }
    if ($LinuxWslDistro -notmatch '^[A-Za-z0-9_.-]+$') { throw 'WSL distro name contains unsupported characters' }
    if ($ExportDirectory -notmatch '^([A-Za-z]):\\') { throw 'Linux WSL smoke requires a drive-letter export path' }
    $drive = $Matches[1].ToLowerInvariant()
    $remainder = $ExportDirectory.Substring(2).Replace('\', '/')
    $wslDirectory = "/mnt/$drive$remainder"
    if ($wslDirectory.Contains("'")) { throw 'WSL smoke does not support an apostrophe in the export path' }
    $quotedWslDirectory = "'" + $wslDirectory + "'"
    $linuxCommand = "cd $quotedWslDirectory && env -u DISPLAY -u WAYLAND_DISPLAY GODOT_SILENCE_ROOT_WARNING=1 ./Enfractal-Linux-Phase0.x86_64 --quit-after 120"
    $wslArguments = "-d $LinuxWslDistro -- bash -lc `"$linuxCommand`""
    $linux = Invoke-Hidden -Name 'linux-release-smoke' -Executable 'wsl.exe' -Arguments $wslArguments
    if ($linux.exit_code -ne 0 -or -not [string]::IsNullOrWhiteSpace($linux.stderr) -or -not $linux.stdout.Contains('Barton Creek map loaded:')) {
        throw "Linux release no-display smoke failed; inspect $ExportDirectory"
    }
}

$result = [ordered]@{
    windows_graphics_exit_code = $windows.exit_code
    windows_renderer = @($windows.stdout -split "`r?`n" | Where-Object { $_ -match 'Compatibility - Using Device:' } | Select-Object -First 1)[0]
    windows_capture = $capturePath
    windows_capture_bytes = (Get-Item -LiteralPath $capturePath).Length
    linux_headless_exit_code = if ($linux) { $linux.exit_code } else { $null }
    linux_wsl_distro = if ($linux) { $LinuxWslDistro } else { $null }
    linux_display_variables_unset = [bool]$linux
    note = 'Windows capture is a smoke image. The fixed-frame in-game metric may have too few post-LOD samples for timing. Linux WSL2 is not a VPS load test.'
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ExportDirectory 'release-smoke-summary.json') -Encoding utf8
$result | ConvertTo-Json -Depth 8
