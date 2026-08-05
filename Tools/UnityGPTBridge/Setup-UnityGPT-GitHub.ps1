param(
    [string]$RepositoryUrl = "",
    [string]$MainBranch = "main",
    [string]$WorkBranch = "unity-gpt-work"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Invoke-Git {
    param([string[]]$Arguments, [switch]$AllowFailure)
    $output = & git -C $script:ProjectRoot @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "git $($Arguments -join ' ') failed ($exitCode):`n$($output -join [Environment]::NewLine)"
    }
    return [PSCustomObject]@{ ExitCode = $exitCode; Output = ($output -join [Environment]::NewLine) }
}

function Ensure-GitIgnore {
    $path = Join-Path $script:ProjectRoot ".gitignore"
    $marker = "# BEGIN Unity GPT Bridge"
    $block = @"
$marker
[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Bb]uild/
[Bb]uilds/
[Ll]ogs/
UserSettings/
MemoryCaptures/
Recordings/
.vs/
.idea/
.gradle/
.consulo/
ExportedObj/
*.csproj
*.sln
*.suo
*.tmp
*.user
*.userprefs
*.pidb
*.booproj
*.svd
*.pdb
*.mdb
*.opendb
*.VC.db
sysinfo.txt
crashlytics-build.properties
.unity-gpt/
# END Unity GPT Bridge
"@

    $existing = if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Raw } else { "" }
    if ($existing -notmatch [Regex]::Escape($marker)) {
        if (-not [string]::IsNullOrWhiteSpace($existing) -and -not $existing.EndsWith([Environment]::NewLine)) {
            Add-Content -LiteralPath $path -Value "" -Encoding UTF8
        }
        Add-Content -LiteralPath $path -Value $block -Encoding UTF8
        Write-Host "Updated .gitignore"
    }
}

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:ProjectRoot = (Resolve-Path (Join-Path $scriptDirectory "..\..")).Path

if (-not (Test-Path -LiteralPath (Join-Path $script:ProjectRoot "Assets")) -or
    -not (Test-Path -LiteralPath (Join-Path $script:ProjectRoot "Packages")) -or
    -not (Test-Path -LiteralPath (Join-Path $script:ProjectRoot "ProjectSettings"))) {
    throw "This script must be inside a Unity project root containing Assets, Packages, and ProjectSettings."
}

Write-Host "Unity GPT Bridge GitHub setup" -ForegroundColor Cyan
Write-Host "Project: $script:ProjectRoot"
Write-Host "Use an EMPTY private GitHub repository to avoid first-push conflicts." -ForegroundColor Yellow
Write-Host ""

& git --version | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Git is not installed or is not available in PATH."
}

if (-not (Test-Path -LiteralPath (Join-Path $script:ProjectRoot ".git"))) {
    Invoke-Git -Arguments @("init") | Out-Null
    Invoke-Git -Arguments @("branch", "-M", $MainBranch) | Out-Null
    Write-Host "Initialized Git repository."
}

Ensure-GitIgnore

$userName = Invoke-Git -Arguments @("config", "user.name") -AllowFailure
if ($userName.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($userName.Output)) {
    Invoke-Git -Arguments @("config", "user.name", "Aiden") | Out-Null
}
$userEmail = Invoke-Git -Arguments @("config", "user.email") -AllowFailure
if ($userEmail.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($userEmail.Output)) {
    Invoke-Git -Arguments @("config", "user.email", "aiden@users.noreply.github.com") | Out-Null
}

if ([string]::IsNullOrWhiteSpace($RepositoryUrl)) {
    $RepositoryUrl = Read-Host "Paste the empty private GitHub repository URL (example: https://github.com/name/project.git)"
}
if ([string]::IsNullOrWhiteSpace($RepositoryUrl)) {
    throw "Repository URL was not supplied."
}

$remote = Invoke-Git -Arguments @("remote", "get-url", "origin") -AllowFailure
if ($remote.ExitCode -ne 0) {
    Invoke-Git -Arguments @("remote", "add", "origin", $RepositoryUrl) | Out-Null
}
elseif ($remote.Output.Trim() -ne $RepositoryUrl.Trim()) {
    $replace = Read-Host "origin is currently '$($remote.Output.Trim())'. Replace it with the supplied URL? (y/N)"
    if ($replace -match "^[Yy]") {
        Invoke-Git -Arguments @("remote", "set-url", "origin", $RepositoryUrl) | Out-Null
    }
}

Write-Host "Staging Unity project files. Library, Temp, Logs, obj, Builds, and .unity-gpt are ignored."
Invoke-Git -Arguments @("add", "Assets", "Packages", "ProjectSettings", "Tools", "AGENTS.md", ".gitignore") | Out-Null
$status = Invoke-Git -Arguments @("diff", "--cached", "--quiet") -AllowFailure
if ($status.ExitCode -ne 0) {
    Invoke-Git -Arguments @("commit", "-m", "Set up Unity project and Unity GPT Bridge") | Out-Null
}

$currentBranch = (Invoke-Git -Arguments @("branch", "--show-current")).Output.Trim()
if ([string]::IsNullOrWhiteSpace($currentBranch)) {
    Invoke-Git -Arguments @("branch", "-M", $MainBranch) | Out-Null
    $currentBranch = $MainBranch
}

Write-Host "Pushing $currentBranch to GitHub. A browser/login prompt may appear."
Invoke-Git -Arguments @("push", "-u", "origin", $currentBranch) | Out-Null

$remoteWork = Invoke-Git -Arguments @("ls-remote", "--exit-code", "--heads", "origin", $WorkBranch) -AllowFailure
if ($remoteWork.ExitCode -ne 0) {
    $localWork = Invoke-Git -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/$WorkBranch") -AllowFailure
    if ($localWork.ExitCode -ne 0) {
        Invoke-Git -Arguments @("branch", $WorkBranch, "HEAD") | Out-Null
    }
    Invoke-Git -Arguments @("push", "-u", "origin", "$WorkBranch`:$WorkBranch") | Out-Null
    Write-Host "Created remote work branch '$WorkBranch'."
}
else {
    Write-Host "Remote work branch '$WorkBranch' already exists."
}

Write-Host ""
Write-Host "Setup complete." -ForegroundColor Green
Write-Host "1. Open Unity and choose Tools > Unity GPT Bridge."
Write-Host "2. Click Export Snapshot."
Write-Host "3. Click Start Git Relay."
Write-Host "4. Connect the private repository to ChatGPT's GitHub app."
Write-Host ""
Read-Host "Press Enter to close"
