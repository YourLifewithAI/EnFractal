#requires -Version 7.0
<#
.SYNOPSIS
Run one Codex job locally, in its own checkout and Codex's Windows sandbox, and save its final message.

.DESCRIPTION
The integrator's launcher for Codex (GPT) jobs on the founder's machine. It uses the CLI bundled with the Codex desktop
app (signed in with the founder's ChatGPT plan, so no API spend) and never the founder's own Codex configuration, whose
defaults are full machine access with no approvals. Every job:
  - gets its own checkout, C:\dev\EnFractal-codex\<brief> on branch codex/<brief> (created from -Base when missing), a
    worktree of the integrator's repository, so parallel jobs never switch branches under each other;
  - runs in the workspace-write sandbox: it reads anything and writes only inside its checkout and temp. It cannot
    commit (the repository's .git lies outside the checkout); the integrator checks the files and commits them on the
    job's branch. -ReadOnly forbids writes too;
  - has computer use, the browser, apps, plugins, hooks and image generation off; web search is on with -Search;
  - writes Codex's final message to -Out, and the transcript next to it.

.EXAMPLE
pwsh -NoProfile -File tools/codex/run.ps1 -Brief 05-docs-audit -Out $scratch\05.md
pwsh -NoProfile -File tools/codex/run.ps1 -Brief 03-image-to-3d-survey -Search -Out $scratch\03.md
pwsh -NoProfile -File tools/codex/run.ps1 -Prompt "Review commit abc123 ..." -Base abc123 -ReadOnly -Effort high -Out $scratch\review.md
#>
param(
    [string]$Brief,
    [string]$Prompt,
    [Parameter(Mandatory)] [string]$Out,
    # Default: C:\dev\EnFractal-codex\<brief>; a -Prompt job gets C:\dev\EnFractal-codex\scratch, detached at -Base.
    [string]$Checkout,
    [string]$Base = 'origin/run2/integration',
    [string]$Model = 'gpt-6.1-sol',
    [ValidateSet('low', 'medium', 'high', 'xhigh')] [string]$Effort = 'medium',
    [switch]$Search,
    [switch]$ReadOnly
)
$ErrorActionPreference = 'Stop'
if (-not $Brief -and -not $Prompt) { throw 'Give -Brief <name> or -Prompt <text>.' }
$package = Get-AppxPackage -Name 'OpenAI.Codex'
if (-not $package) { throw 'The Codex desktop app is not installed; its bundled CLI is what this script runs.' }
$codex = Join-Path $package.InstallLocation 'app\resources\codex.exe'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Checkout) { $Checkout = Join-Path 'C:\dev\EnFractal-codex' ($Brief ? $Brief : 'scratch') }
if (-not (Test-Path -LiteralPath $Checkout)) {
    $worktree = $Brief ? @('worktree', 'add', '-b', "codex/$Brief", $Checkout, $Base) : @('worktree', 'add', '--detach', $Checkout, $Base)
    git -C $repo @worktree 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "git worktree add failed for $Checkout" }
}
$Checkout = (Resolve-Path -LiteralPath $Checkout).Path
$Out = [System.IO.Path]::GetFullPath($Out)

$rules = Get-Content -Raw -LiteralPath (Join-Path $Checkout 'docs\codex\README.md')
$where = if ($ReadOnly) {
    "Your sandbox is read-only: change no files. Deliver your result as your final message, in Markdown."
} else {
    "Your sandbox lets you read anything and write only inside your checkout, $Checkout, which is on the branch the brief names. Write your files there, within the brief's scope; you cannot commit or push, so the integrator checks your files and commits them. Scratch files go in your temp folder, never in the repository."
}
$task = if ($Brief) {
    $briefText = Get-Content -Raw -LiteralPath (Join-Path $Checkout "docs\codex\briefs\$Brief.md")
    "The founder names brief $Brief. Do that brief and nothing else.`n`n$briefText"
} else { $Prompt }
$fullPrompt = "$rules`n`n---`n`n$where Read AGENTS.md in your checkout for the project's rules. End with a short final message: what you did, the files you wrote, and anything unfinished.`n`n---`n`n$task"

$arguments = @('exec', '--ignore-user-config', '--ephemeral', '-C', $Checkout, '-m', $Model, '-c', "model_reasoning_effort=`"$Effort`"",
    '-s', ($ReadOnly ? 'read-only' : 'workspace-write'), '-c', 'windows.sandbox="unelevated"', '-o', $Out)
foreach ($feature in 'browser_use', 'browser_use_external', 'browser_annotation_api', 'computer_use', 'apps', 'plugins',
    'image_generation', 'hooks', 'in_app_browser', 'remote_plugin') {
    $arguments += @('--disable', $feature)
}
$arguments += @('-c', ($Search ? 'web_search="live"' : 'web_search="disabled"'))
$arguments += '-'

# PowerShell 7 here is a Store (MSIX) package, and the sandbox's restricted token cannot start one (CreateProcessAsUserW
# fails). pwsh also puts its own package folder on PATH, so drop every WindowsApps entry and Codex falls back to the
# built-in Windows PowerShell.
$env:PATH = ($env:PATH -split ';' | Where-Object { $_ -and $_ -notmatch '\\WindowsApps(\\|$)' }) -join ';'
# Codex writes logs into the working directory, so run it from the output's folder, never from a checkout.
New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
Push-Location (Split-Path $Out)
try {
    $fullPrompt | & $codex @arguments 2>&1 | Out-File -LiteralPath "$Out.log" -Encoding utf8
    $code = $LASTEXITCODE
} finally { Pop-Location }
Write-Output "codex exit $code; final message: $Out; transcript: $Out.log"
exit $code
