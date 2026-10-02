# Shared discovery/process helpers. Dot-source this file; it does not install tools.
# Native tools and their caches stay inside the checkout unless explicitly supplied.

function Get-EnfractalNativeToolchain {
    [CmdletBinding()]
    param([string]$EnginePath, [string]$DotnetPath)

    $repositoryPath = Split-Path -Parent $PSScriptRoot
    $projectPath = Join-Path $repositoryPath 'game'
    $requiredSdk = (Get-Content -LiteralPath (Join-Path $projectPath 'global.json') -Raw | ConvertFrom-Json).sdk.version
    $requiredEngine = ([xml](Get-Content -LiteralPath (Join-Path $projectPath 'EnFractal.csproj') -Raw)).Project.Sdk.Split('/')[1]

    if (-not $EnginePath) { $EnginePath = $env:ENFRACTAL_GODOT_DOTNET }
    if (-not $EnginePath) { $EnginePath = $env:ENFRACTAL_GODOT }
    if (-not $EnginePath) {
        foreach ($cacheName in @('godot', 'godot-dotnet')) {
            $cachePath = Join-Path $repositoryPath ".cache/$cacheName"
            if (Test-Path -LiteralPath $cachePath -PathType Container) {
                $candidate = Get-ChildItem -LiteralPath $cachePath -Recurse -File |
                    Where-Object { $_.Name -eq "Godot_v$requiredEngine-stable_mono_win64_console.exe" } |
                    Sort-Object FullName | Select-Object -First 1
                if ($candidate) { $EnginePath = $candidate.FullName; break }
            }
        }
    }
    if (-not $EnginePath) {
        $candidate = Get-Command godot-mono,godot,godot4 -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($candidate) { $EnginePath = $candidate.Source }
    }
    if (-not $EnginePath -or -not (Test-Path -LiteralPath $EnginePath -PathType Leaf)) {
        throw "Godot .NET $requiredEngine is missing. Extract its full mono archive under .cache/godot or set ENFRACTAL_GODOT_DOTNET. See docs/NATIVE-BUILD.md."
    }
    $EnginePath = (Resolve-Path -LiteralPath $EnginePath).Path
    $engineVersion = (& $EnginePath --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $engineVersion -notmatch ('^' + [regex]::Escape($requiredEngine) + '\.stable\.mono(?:\.|$)')) {
        throw "Expected Godot .NET $requiredEngine; '$EnginePath' reported '$engineVersion'. A standard Godot build cannot run the C# project."
    }
    if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $EnginePath) 'GodotSharp') -PathType Container)) {
        throw 'GodotSharp is missing beside the .NET executable. Extract the complete archive, not only the executable.'
    }

    if (-not $DotnetPath) { $DotnetPath = $env:ENFRACTAL_DOTNET }
    if (-not $DotnetPath) {
        $portableDotnet = Join-Path $repositoryPath '.cache/dotnet/dotnet.exe'
        if (Test-Path -LiteralPath $portableDotnet -PathType Leaf) { $DotnetPath = $portableDotnet }
    }
    if (-not $DotnetPath) {
        $candidate = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
        if ($candidate) { $DotnetPath = $candidate.Source }
    }
    if (-not $DotnetPath -or -not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) {
        throw ".NET SDK $requiredSdk is missing. Extract it under .cache/dotnet or set ENFRACTAL_DOTNET. A runtime-only installation is insufficient."
    }
    $DotnetPath = (Resolve-Path -LiteralPath $DotnetPath).Path
    $installedSdks = (& $DotnetPath --list-sdks | Out-String)
    if ($LASTEXITCODE -ne 0 -or $installedSdks -notmatch ('(?m)^' + [regex]::Escape($requiredSdk) + '\s')) {
        throw "The selected dotnet host does not contain pinned SDK $requiredSdk. See game/global.json and docs/NATIVE-BUILD.md."
    }
    return [pscustomobject]@{
        RepositoryPath = $repositoryPath
        ProjectPath = $projectPath
        EnginePath = $EnginePath
        EngineVersion = $engineVersion
        DotnetPath = $DotnetPath
        SdkVersion = $requiredSdk
    }
}

function Invoke-EnfractalNativeProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Toolchain,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [int]$TimeoutSeconds = 120,
        [string]$WorkingDirectory,
        [switch]$WithoutDotnetRuntime
    )
    if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Native helpers require PowerShell 7 or newer.' }
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FilePath
    if (-not $WorkingDirectory) { $WorkingDirectory = $Toolchain.ProjectPath }
    if (-not (Test-Path -LiteralPath $WorkingDirectory -PathType Container)) {
        throw "Native working directory does not exist: $WorkingDirectory"
    }
    $start.WorkingDirectory = (Resolve-Path -LiteralPath $WorkingDirectory).Path
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    if ($WithoutDotnetRuntime) {
        # Release probes must find their own bundled runtime, not developer tools.
        # This removes inherited host configuration as well as our normal SDK setup.
        foreach ($key in @($start.Environment.Keys)) {
            if ($key -match '^(DOTNET_|COREHOST_|COMPlus_|MSBUILD)' -or $key -in @('NUGET_PACKAGES', 'ENFRACTAL_DOTNET')) {
                [void]$start.Environment.Remove($key)
            }
        }
        $start.Environment['PATH'] = (Join-Path $env:SystemRoot 'System32') + [System.IO.Path]::PathSeparator + $env:SystemRoot
    } else {
        $dotnetRoot = Split-Path -Parent $Toolchain.DotnetPath
        $start.Environment['DOTNET_ROOT'] = $dotnetRoot
        $start.Environment['PATH'] = $dotnetRoot + [System.IO.Path]::PathSeparator + $start.Environment['PATH']
        $start.Environment['DOTNET_CLI_HOME'] = Join-Path $Toolchain.RepositoryPath '.cache/dotnet-home'
        $start.Environment['NUGET_PACKAGES'] = Join-Path $Toolchain.RepositoryPath '.cache/nuget/packages'
        $start.Environment['DOTNET_NOLOGO'] = '1'
        $start.Environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
        $start.Environment['DOTNET_ADD_GLOBAL_TOOLS_TO_PATH'] = 'false'
        $start.Environment['DOTNET_GENERATE_ASPNET_CERTIFICATE'] = 'false'
    }
    $start.Environment['APPDATA'] = Join-Path $Toolchain.RepositoryPath '.cache/native-test/appdata'
    $start.Environment['LOCALAPPDATA'] = Join-Path $Toolchain.RepositoryPath '.cache/native-test/localappdata'
    $start.Environment['XDG_DATA_HOME'] = Join-Path $Toolchain.RepositoryPath '.cache/native-test/data'
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        [void]$process.Start()
        $stdoutRead = $process.StandardOutput.ReadToEndAsync()
        $stderrRead = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            $timedOutStdout = $stdoutRead.GetAwaiter().GetResult()
            $timedOutStderr = $stderrRead.GetAwaiter().GetResult()
            throw "Native command timed out after $TimeoutSeconds seconds: $FilePath`n$timedOutStdout`n$timedOutStderr"
        }
        $stdout = $stdoutRead.GetAwaiter().GetResult()
        $stderr = $stderrRead.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "Native command failed ($($process.ExitCode)): $FilePath`n$stdout`n$stderr"
        }
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Stdout = $stdout; Stderr = $stderr }
    } finally {
        $process.Dispose()
    }
}

function Build-EnfractalNativeProject {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)]$Toolchain)
    $result = Invoke-EnfractalNativeProcess -Toolchain $Toolchain -FilePath $Toolchain.DotnetPath `
        -Arguments @('build', 'EnFractal.csproj', '--configuration', 'Debug', '--nologo') -TimeoutSeconds 180
    if (-not [string]::IsNullOrWhiteSpace($result.Stderr)) { Write-Warning $result.Stderr.Trim() }
    Write-Output $result.Stdout.Trim()
}
