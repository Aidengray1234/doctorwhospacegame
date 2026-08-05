param(
    [string]$RemoteName = "origin",
    [string]$WorkBranch = "unity-gpt-work",
    [string]$StatusBranch = "unity-gpt-status",
    [int]$PollSeconds = 10,
    [switch]$Once
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Write-BridgeLog {
    param([string]$Message)
    $stamp = (Get-Date).ToUniversalTime().ToString("o")
    $line = "[$stamp] $Message"
    Write-Host $line
    Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    # Git writes normal progress messages to stderr. Windows PowerShell can turn
    # redirected native stderr into a terminating NativeCommandError when the
    # relay uses ErrorActionPreference=Stop, so temporarily relax it here and
    # rely on Git's actual exit code instead.
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = & git -C $WorkingDirectory @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "git $($Arguments -join ' ') failed ($exitCode):`n$($output -join [Environment]::NewLine)"
    }

    return [PSCustomObject]@{
        ExitCode = $exitCode
        Output = ($output -join [Environment]::NewLine)
    }
}

function Get-StableHash {
    param([string]$Text)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($bytes)
        return ([BitConverter]::ToString($hash).Replace("-", "").Substring(0, 16)).ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Copy-DirectoryContents {
    param([string]$Source, [string]$Destination)

    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    if (Test-Path -LiteralPath $Source) {
        Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
        }
    }
}

function Ensure-StatusClone {
    param([string]$RemoteUrl, [string]$CacheRoot)

    if (-not (Test-Path -LiteralPath (Join-Path $CacheRoot ".git"))) {
        if (Test-Path -LiteralPath $CacheRoot) {
            Remove-Item -LiteralPath $CacheRoot -Recurse -Force
        }

        New-Item -ItemType Directory -Path (Split-Path -Parent $CacheRoot) -Force | Out-Null
        Write-BridgeLog "Creating private status cache at $CacheRoot"
        $cacheParent = Split-Path -Parent $CacheRoot
        $cacheLeaf = Split-Path -Leaf $CacheRoot
        $clone = Invoke-Git -WorkingDirectory $cacheParent -Arguments @("clone", "--no-checkout", $RemoteUrl, $cacheLeaf) -AllowFailure
        if (-not [string]::IsNullOrWhiteSpace($clone.Output)) {
            $clone.Output -split "`r?`n" | ForEach-Object { Write-BridgeLog $_ }
        }
        if ($clone.ExitCode -ne 0) {
            throw "Unable to clone the GitHub repository for the status branch.`n$($clone.Output)"
        }

        Invoke-Git -WorkingDirectory $CacheRoot -Arguments @("config", "user.name", "Unity GPT Bridge") | Out-Null
        Invoke-Git -WorkingDirectory $CacheRoot -Arguments @("config", "user.email", "unity-gpt-bridge@local.invalid") | Out-Null
    }
}

function Prepare-StatusBranch {
    param([string]$CacheRoot, [string]$Branch)

    Invoke-Git -WorkingDirectory $CacheRoot -Arguments @("fetch", "--prune", "origin") | Out-Null
    $remoteCheck = Invoke-Git -WorkingDirectory $CacheRoot -Arguments @("show-ref", "--verify", "--quiet", "refs/remotes/origin/$Branch") -AllowFailure

    if ($remoteCheck.ExitCode -eq 0) {
        Invoke-Git -WorkingDirectory $CacheRoot -Arguments @("checkout", "-B", $Branch, "origin/$Branch") | Out-Null
    }
    else {
        $localCheck = Invoke-Git -WorkingDirectory $CacheRoot -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/$Branch") -AllowFailure
        if ($localCheck.ExitCode -eq 0) {
            Invoke-Git -WorkingDirectory $CacheRoot -Arguments @("checkout", $Branch) | Out-Null
        }
        else {
            Invoke-Git -WorkingDirectory $CacheRoot -Arguments @("checkout", "--orphan", $Branch) | Out-Null
            Invoke-Git -WorkingDirectory $CacheRoot -Arguments @("rm", "-rf", ".") -AllowFailure | Out-Null
        }
    }
}

function Publish-Status {
    param(
        [string]$ProjectRoot,
        [string]$CacheRoot,
        [string]$Branch
    )

    $sourceStatus = Join-Path $ProjectRoot ".unity-gpt\status"
    $destinationStatus = Join-Path $CacheRoot ".unity-gpt\status"
    Copy-DirectoryContents -Source $sourceStatus -Destination $destinationStatus

    $bridgeInfoPath = Join-Path $CacheRoot ".unity-gpt\bridge-info.json"
    New-Item -ItemType Directory -Path (Split-Path -Parent $bridgeInfoPath) -Force | Out-Null
    $info = [ordered]@{
        schemaVersion = "1.0"
        projectName = Split-Path -Leaf $ProjectRoot
        statusGeneratedUtc = if (Test-Path -LiteralPath (Join-Path $sourceStatus "ready.flag")) { (Get-Content -LiteralPath (Join-Path $sourceStatus "ready.flag") -Raw).Trim() } else { "unknown" }
        workBranch = $WorkBranch
        statusBranch = $StatusBranch
    }
    $info | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $bridgeInfoPath -Encoding UTF8

    Invoke-Git -WorkingDirectory $CacheRoot -Arguments @("add", "-f", ".unity-gpt/status", ".unity-gpt/bridge-info.json") | Out-Null
    $staged = Invoke-Git -WorkingDirectory $CacheRoot -Arguments @("diff", "--cached", "--quiet") -AllowFailure
    if ($staged.ExitCode -eq 0) {
        return $false
    }

    $message = "Unity status " + (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
    Invoke-Git -WorkingDirectory $CacheRoot -Arguments @("commit", "-m", $message) | Out-Null
    Invoke-Git -WorkingDirectory $CacheRoot -Arguments @("push", "origin", "HEAD:$Branch") | Out-Null
    return $true
}

function Fetch-WorkBranch {
    param([string]$ProjectRoot, [string]$Remote, [string]$Branch)

    $fetch = Invoke-Git -WorkingDirectory $ProjectRoot -Arguments @("fetch", "--prune", $Remote, "refs/heads/$Branch`:refs/remotes/$Remote/$Branch") -AllowFailure
    if ($fetch.ExitCode -ne 0) {
        return [PSCustomObject]@{
            Available = $false
            Commit = ""
            Error = $fetch.Output
        }
    }

    $commitResult = Invoke-Git -WorkingDirectory $ProjectRoot -Arguments @("rev-parse", "$Remote/$Branch") -AllowFailure
    return [PSCustomObject]@{
        Available = ($commitResult.ExitCode -eq 0)
        Commit = if ($commitResult.ExitCode -eq 0) { $commitResult.Output.Trim() } else { "" }
        Error = if ($commitResult.ExitCode -eq 0) { "" } else { $commitResult.Output }
    }
}

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = (Resolve-Path (Join-Path $scriptDirectory "..\..")).Path
$bridgeDirectory = Join-Path $projectRoot ".unity-gpt"
New-Item -ItemType Directory -Path $bridgeDirectory -Force | Out-Null
$script:LogPath = Join-Path $bridgeDirectory "relay.log"

try {
    $repo = Invoke-Git -WorkingDirectory $projectRoot -Arguments @("rev-parse", "--show-toplevel")
    $projectRoot = $repo.Output.Trim()
    $remoteResult = Invoke-Git -WorkingDirectory $projectRoot -Arguments @("remote", "get-url", $RemoteName)
    $remoteUrl = $remoteResult.Output.Trim()

    $cacheKey = Get-StableHash "$projectRoot|$remoteUrl|$StatusBranch"
    $cacheRoot = Join-Path $env:LOCALAPPDATA "UnityGPTBridge\$cacheKey\status-repository"

    Write-BridgeLog "Unity GPT Relay started for $projectRoot"
    Write-BridgeLog "Remote: $RemoteName ($remoteUrl)"
    Write-BridgeLog "Work branch: $WorkBranch | Status branch: $StatusBranch"
    Write-BridgeLog "Press Ctrl+C to stop."

    Ensure-StatusClone -RemoteUrl $remoteUrl -CacheRoot $cacheRoot
    $lastWorkCommit = ""

    do {
        try {
            Prepare-StatusBranch -CacheRoot $cacheRoot -Branch $StatusBranch
            $published = Publish-Status -ProjectRoot $projectRoot -CacheRoot $cacheRoot -Branch $StatusBranch
            if ($published) {
                Write-BridgeLog "Published updated Unity status."
            }

            $work = Fetch-WorkBranch -ProjectRoot $projectRoot -Remote $RemoteName -Branch $WorkBranch
            $state = [ordered]@{
                schemaVersion = "1.0"
                checkedUtc = (Get-Date).ToUniversalTime().ToString("o")
                workBranch = $WorkBranch
                statusBranch = $StatusBranch
                workBranchAvailable = $work.Available
                workCommit = $work.Commit
                error = $work.Error
            }
            $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $bridgeDirectory "relay-state.json") -Encoding UTF8

            if ($work.Available -and $work.Commit -ne $lastWorkCommit) {
                $lastWorkCommit = $work.Commit
                Write-BridgeLog "New/updated GPT work commit available: $lastWorkCommit"
                Write-BridgeLog "Open Tools > Unity GPT Bridge and click Fetch & Preview."
            }
            elseif (-not $work.Available -and -not [string]::IsNullOrWhiteSpace($work.Error)) {
                Write-BridgeLog "Work branch is not available yet. Create and push '$WorkBranch' when the repository setup is complete."
            }
        }
        catch {
            Write-BridgeLog "Relay cycle error: $($_.Exception.Message)"
        }

        if (-not $Once) {
            Start-Sleep -Seconds ([Math]::Max(5, $PollSeconds))
        }
    } while (-not $Once)
}
catch {
    Write-BridgeLog "Fatal relay error: $($_.Exception.Message)"
    Write-Host ""
    Write-Host "The relay stopped. Fix the error above, then run it again." -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}
