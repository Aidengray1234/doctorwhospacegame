param(
    [string]$ConfigPath = "$PSScriptRoot\config.json",
    [switch]$Once
)

$ErrorActionPreference = 'Stop'

function Write-RelayLog([string]$Message) {
    $stamp = (Get-Date).ToUniversalTime().ToString('o')
    Write-Host "[$stamp] $Message"
}

function Run-Git([string]$WorkingDirectory, [string[]]$Arguments) {
    $previousPreference = $ErrorActionPreference
    try {
        # Windows PowerShell can turn normal native stderr output into a
        # terminating NativeCommandError when ErrorActionPreference is Stop.
        $ErrorActionPreference = 'Continue'
        $output = & git.exe -C $WorkingDirectory @Arguments 2>&1 | ForEach-Object { [string]$_ }
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    $textOutput = ($output -join [Environment]::NewLine)
    if ($exitCode -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code ${exitCode}:`n$textOutput"
    }
    return $textOutput
}

function Test-SafeTargetPath([string]$RelativePath, [bool]$AllowUnityYaml) {
    if ([string]::IsNullOrWhiteSpace($RelativePath)) { return $false }
    $normalized = $RelativePath.Replace('\','/').TrimStart('/')
    if ($normalized.Contains('../') -or $normalized.Contains('/..') -or $normalized.Contains(':')) { return $false }

    $allowed = $normalized -match '^(Assets|Packages|ProjectSettings|Tools)/' -or
               $normalized -in @('AGENTS.md','README-FIRST.md','VALIDATION.md','.gitignore','.gitattributes')
    if (-not $allowed) { return $false }

    $ext = [IO.Path]::GetExtension($normalized).ToLowerInvariant()
    $textExtensions = @('.cs','.json','.asmdef','.asmref','.shader','.hlsl','.cginc','.compute','.txt','.md','.xml','.uss','.uxml','.inputactions','.ps1','.bat','.cmd','.gitignore','.gitattributes')
    if ($textExtensions -notcontains $ext -and -not ($normalized -in @('.gitignore','.gitattributes'))) {
        if (-not ($AllowUnityYaml -and $ext -in @('.unity','.prefab','.asset','.mat'))) { return $false }
    }
    return $true
}

function Write-Result([string]$ResultsRoot, [string]$JobId, [string]$Status, [string]$Message, [string]$Commit = '') {
    $result = [ordered]@{
        schemaVersion = '1.0'
        jobId = $JobId
        status = $Status
        message = $Message
        commit = $Commit
        completedUtc = (Get-Date).ToUniversalTime().ToString('o')
    }
    $path = Join-Path $ResultsRoot "$JobId.json"
    $result | ConvertTo-Json -Depth 8 | Set-Content -Path $path -Encoding UTF8
}

if (-not (Test-Path $ConfigPath)) { throw "Config not found: $ConfigPath. Run Install-UnityGPT-Dropbox-Bridge.ps1 first." }
$config = Get-Content $ConfigPath -Raw | ConvertFrom-Json

$projectRoot = [IO.Path]::GetFullPath([string]$config.projectRoot)
$dropboxRoot = [IO.Path]::GetFullPath([string]$config.dropboxProjectRoot)
$worktreeRoot = [IO.Path]::GetFullPath([string]$config.worktreeRoot)
$pollSeconds = [Math]::Max(3, [int]$config.pollSeconds)
$allowUnityYaml = [bool]$config.allowUnityYaml

$inboxRoot = Join-Path $dropboxRoot 'inbox'
$processingRoot = Join-Path $dropboxRoot 'processing'
$appliedRoot = Join-Path $dropboxRoot 'applied'
$rejectedRoot = Join-Path $dropboxRoot 'rejected'
$resultsRoot = Join-Path $dropboxRoot 'results'

foreach ($path in @($inboxRoot,$processingRoot,$appliedRoot,$rejectedRoot,$resultsRoot)) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

if (-not (Test-Path (Join-Path $projectRoot '.git'))) { throw "Unity project is not a Git repository: $projectRoot" }
Run-Git $projectRoot @('fetch','origin','unity-gpt-work') | Out-Null

if (-not (Test-Path $worktreeRoot)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $worktreeRoot -Parent) | Out-Null
    $worktreeOutput = Run-Git $projectRoot @('worktree','add','--force','-B','unity-gpt-work',$worktreeRoot,'origin/unity-gpt-work')
    if (-not [string]::IsNullOrWhiteSpace($worktreeOutput)) { Write-Host $worktreeOutput }
}

Write-RelayLog "Dropbox relay online. Inbox: $inboxRoot"
Write-RelayLog 'Only validated text-file writes are supported. No commands from jobs are executed.'

while ($true) {
    $jobs = Get-ChildItem -Path $inboxRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name
    foreach ($jobFolder in $jobs) {
        $manifestPath = Join-Path $jobFolder.FullName 'manifest.json'
        if (-not (Test-Path $manifestPath)) { continue }

        $manifest = $null
        try {
            $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
            if (-not [bool]$manifest.enabled) { continue }
            $jobId = if ([string]::IsNullOrWhiteSpace([string]$manifest.jobId)) { $jobFolder.Name } else { [string]$manifest.jobId }
            if ($jobId -notmatch '^[A-Za-z0-9._-]+$') { throw 'Unsafe jobId.' }

            $processingPath = Join-Path $processingRoot $jobFolder.Name
            if (Test-Path $processingPath) { throw "Processing destination already exists: $processingPath" }
            Move-Item -LiteralPath $jobFolder.FullName -Destination $processingPath
            Write-RelayLog "Processing $jobId"

            Run-Git $projectRoot @('fetch','origin','unity-gpt-work') | Out-Null
            Run-Git $worktreeRoot @('reset','--hard','origin/unity-gpt-work') | Out-Null
            Run-Git $worktreeRoot @('clean','-fd') | Out-Null

            $operations = @($manifest.operations)
            if ($operations.Count -eq 0) { throw 'Job contains no operations.' }
            if ($operations.Count -gt 100) { throw 'Job exceeds the 100-operation safety limit.' }

            foreach ($operation in $operations) {
                if ([string]$operation.type -ne 'write') { throw "Unsupported operation type: $($operation.type). Only write is allowed." }
                $target = ([string]$operation.path).Replace('\','/').TrimStart('/')
                if (-not (Test-SafeTargetPath $target $allowUnityYaml)) { throw "Blocked target path or file type: $target" }

                $sourceRelative = ([string]$operation.source).Replace('\','/').TrimStart('/')
                if ($sourceRelative.Contains('../') -or -not $sourceRelative.StartsWith('files/')) { throw "Unsafe source path: $sourceRelative" }
                $sourceFull = [IO.Path]::GetFullPath((Join-Path $processingPath $sourceRelative))
                if (-not $sourceFull.StartsWith([IO.Path]::GetFullPath($processingPath), [StringComparison]::OrdinalIgnoreCase)) { throw 'Source escapes the job folder.' }
                if (-not (Test-Path $sourceFull -PathType Leaf)) { throw "Source file missing: $sourceRelative" }
                if ((Get-Item $sourceFull).Length -gt 5MB) { throw "Source file exceeds 5 MB: $sourceRelative" }

                $destinationFull = [IO.Path]::GetFullPath((Join-Path $worktreeRoot $target))
                if (-not $destinationFull.StartsWith($worktreeRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Destination escapes the worktree.' }
                New-Item -ItemType Directory -Force -Path (Split-Path $destinationFull -Parent) | Out-Null
                Copy-Item -LiteralPath $sourceFull -Destination $destinationFull -Force
            }

            Run-Git $worktreeRoot @('add','--all') | Out-Null
            & git -C $worktreeRoot diff --cached --quiet
            if ($LASTEXITCODE -eq 0) {
                Write-Result $resultsRoot $jobId 'no_changes' 'No file changes were produced.'
            } else {
                $commitMessage = if ([string]::IsNullOrWhiteSpace([string]$manifest.commitMessage)) { "Unity GPT Dropbox job: $jobId" } else { [string]$manifest.commitMessage }
                Run-Git $worktreeRoot @('commit','-m',$commitMessage) | Out-Null
                $commit = (Run-Git $worktreeRoot @('rev-parse','HEAD')).Trim()
                Run-Git $worktreeRoot @('push','origin','HEAD:unity-gpt-work') | Out-Null
                Write-Result $resultsRoot $jobId 'published' 'Changes were published to unity-gpt-work for Unity preview.' $commit
                Write-RelayLog "Published $jobId as $commit"
            }

            $appliedPath = Join-Path $appliedRoot $jobFolder.Name
            if (Test-Path $appliedPath) { $appliedPath = Join-Path $appliedRoot ($jobFolder.Name + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
            Move-Item -LiteralPath $processingPath -Destination $appliedPath
        }
        catch {
            $jobId = if ($null -ne $manifest -and -not [string]::IsNullOrWhiteSpace([string]$manifest.jobId)) { [string]$manifest.jobId } else { $jobFolder.Name }
            $errorMessage = $_.Exception.Message
            Write-RelayLog "Rejected ${jobId}: $errorMessage"
            Write-Result $resultsRoot $jobId 'rejected' $errorMessage
            $current = if (Test-Path (Join-Path $processingRoot $jobFolder.Name)) { Join-Path $processingRoot $jobFolder.Name } else { $jobFolder.FullName }
            if (Test-Path $current) {
                $rejectedPath = Join-Path $rejectedRoot $jobFolder.Name
                if (Test-Path $rejectedPath) { $rejectedPath = Join-Path $rejectedRoot ($jobFolder.Name + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
                Move-Item -LiteralPath $current -Destination $rejectedPath
            }
        }
    }

    if ($Once) { break }
    Start-Sleep -Seconds $pollSeconds
}

