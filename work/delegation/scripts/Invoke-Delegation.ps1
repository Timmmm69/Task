param([Parameter(Mandatory)][string]$PacketPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Delegation.Common.ps1"

$root = Get-RepositoryRoot
$packetFile = [IO.Path]::GetFullPath((Join-Path (Get-Location) $PacketPath))
if (-not (Test-Path -LiteralPath $packetFile)) { throw "PACKET_INVALID: packet file not found: $PacketPath" }
$packet = ConvertFrom-DelegationYaml -Text (Get-Content -Raw -Encoding UTF8 $packetFile)
Test-DelegationPacket $packet | Out-Null

if (-not (Get-Command opencode -ErrorAction SilentlyContinue)) { throw "OpenCode is not installed or is not on PATH. Run Setup-Delegation.ps1 after installation." }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "GitHub CLI is required." }
$settingsPath = Join-Path $root "work\delegation\local.settings.json"
if (-not (Test-Path $settingsPath)) { throw "Delegation is not configured. Run work/delegation/scripts/Setup-Delegation.ps1." }
$model = (Get-Content -Raw $settingsPath | ConvertFrom-Json).model

$status = @(& git -C $root status --porcelain)
if ($status.Count) { throw "Dispatcher requires a clean primary worktree." }
& git -C $root fetch origin main --quiet
if ($LASTEXITCODE -ne 0) { throw "Unable to fetch origin/main." }
$mainSha = (& git -C $root rev-parse main).Trim()
$originSha = (& git -C $root rev-parse origin/main).Trim()
if ($mainSha -ne $originSha) { throw "Local main must exactly match origin/main." }
$resolvedBase = (& git -C $root rev-parse $packet.base_sha).Trim()
if ($resolvedBase -ne $originSha) { throw "STALE_PACKET: base_sha must equal current origin/main ($originSha)." }

& "$PSScriptRoot\Cleanup-Delegations.ps1" -IncludeStaleProcesses
$tmpRoot = Join-Path $root "work\tmp"
$worktreeRoot = Join-Path $tmpRoot "delegation-worktrees"
$registryPath = Join-Path $tmpRoot "delegation-registry.json"
New-Item -ItemType Directory -Force $worktreeRoot | Out-Null
$mutex = [Threading.Mutex]::new($false, "Global\TaskDelegationRegistry")
if (-not $mutex.WaitOne([TimeSpan]::FromSeconds(20))) { throw "Could not lock delegation registry." }
try {
    $registry = if (Test-Path $registryPath) { @(Get-Content -Raw $registryPath | ConvertFrom-Json) } else { @() }
    if ($registry.Count -ge 2) { throw "DELEGATION_BUSY: two tasks are already active." }
    foreach ($active in $registry) {
        foreach ($owned in $packet.owned_paths) {
            foreach ($activeOwned in $active.owned_paths) {
                $a = $owned.TrimEnd('*','/'); $b = ([string]$activeOwned).TrimEnd('*','/')
                if ($a.StartsWith($b) -or $b.StartsWith($a)) { throw "DELEGATION_CONFLICT: owned_paths overlap with $($active.task_id)." }
            }
        }
    }
    $slug = (($packet.title.ToLowerInvariant() -replace '[^a-z0-9]+','-').Trim('-'))
    if ($slug.Length -gt 32) { $slug = $slug.Substring(0,32).TrimEnd('-') }
    if (-not $slug) { $slug = "delegated-task" }
    $branch = "agent/deepseek/$($packet.task_id.ToLowerInvariant())-$slug"
    $worktree = Join-Path $worktreeRoot ($packet.task_id.ToLowerInvariant() + "-" + [Guid]::NewGuid().ToString("N").Substring(0,8))
    $entry = [pscustomobject]@{ task_id=$packet.task_id; branch=$branch; worktree=$worktree; owned_paths=@($packet.owned_paths); pid=$PID; status="running"; pr_url=$null; started_at=(Get-Date).ToUniversalTime().ToString("o") }
    @($registry + $entry) | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 $registryPath
} finally { $mutex.ReleaseMutex(); $mutex.Dispose() }

$success = $false
try {
    & git -C $root worktree add -b $branch $worktree $originSha
    if ($LASTEXITCODE -ne 0) { throw "Unable to create delegation worktree." }
    $workPacketPath = Join-Path $worktree "work\tmp\delegation-packet.yaml"
    New-Item -ItemType Directory -Force (Split-Path $workPacketPath) | Out-Null
    Copy-Item -LiteralPath $packetFile -Destination $workPacketPath
    $blockedCommands = Join-Path $worktree "work\tmp\blocked-commands"
    New-Item -ItemType Directory -Force $blockedCommands | Out-Null
    foreach ($name in @("git", "gh")) {
        "@echo off`r`necho BLOCKED: $name is owned by the delegation dispatcher. 1^>^&2`r`nexit /b 126" |
            Set-Content -Encoding ASCII (Join-Path $blockedCommands "$name.cmd")
    }
    $prompt = @"
Execute the delegation packet at work/tmp/delegation-packet.yaml. Read only AGENTS.md, that packet, and its reference_files before editing. Stay strictly inside owned_paths. Do not run git commit, push, merge, checkout, branch, worktree, reset, clean, or rebase commands; the dispatcher owns Git. Run required_checks. If any stop condition occurs, make no speculative change and finish with BLOCKED. End with a compact RESULT containing changed_files, summary, checks, risks, deviations, and status READY, NEEDS_REVIEW, or BLOCKED.
"@
    $originalPath = $env:PATH
    try {
        $env:PATH = "$blockedCommands;$originalPath"
        & opencode run --dir $worktree --agent task-worker --model $model --auto $prompt
    } finally { $env:PATH = $originalPath }
    if ($LASTEXITCODE -ne 0) { throw "OpenCode worker failed." }

    $diff = Test-DelegationDiff -Packet $packet -BaseRef $originSha -RepositoryPath $worktree -IncludeWorkingTree
    foreach ($check in $packet.required_checks) {
        Write-Host "Running required check: $check"
        & powershell -NoProfile -ExecutionPolicy Bypass -Command "Set-Location -LiteralPath '$($worktree.Replace("'", "''"))'; $check"
        if ($LASTEXITCODE -ne 0) { throw "Required check failed: $check" }
    }
    & git -C $worktree add -- @($diff.files)
    & git -C $worktree commit -m "delegate: $($packet.task_id) $($packet.title)"
    if ($LASTEXITCODE -ne 0) { throw "Unable to commit delegation changes." }
    & git -C $worktree push -u origin $branch
    if ($LASTEXITCODE -ne 0) { throw "Unable to push delegation branch." }

    $metadata = ConvertTo-PacketMetadata $packet
    $bodyPath = Join-Path $worktree "work\tmp\delegation-pr-body.md"
    $status = if ($packet.risk -eq "low") { "READY" } else { "NEEDS_REVIEW" }
    @"
## Delegated task $($packet.task_id)

$($packet.title)

### RESULT

- changed_files: $($diff.files -join ', ')
- changed_lines: $($diff.changed_lines)
- required_checks: passed
- risks: see packet stop conditions
- deviations: none reported by dispatcher
- status: $status

<!-- DELEGATION_PACKET_JSON:$metadata -->
"@ | Set-Content -Encoding UTF8 $bodyPath
    $draftArgs = if ($packet.merge -eq "codex-review") { @("--draft") } else { @() }
    $prUrl = (& gh pr create --repo Timmmm69/Task --base main --head $branch --title "[$($packet.task_id)] $($packet.title)" --body-file $bodyPath @draftArgs).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Unable to create pull request." }
    if ($packet.merge -eq "automatic") {
        Write-Host "AUTOMERGE_QUEUED: trusted GitHub workflow will merge after required checks pass."
    }
    $registryMutex = [Threading.Mutex]::new($false, "Global\TaskDelegationRegistry")
    if (-not $registryMutex.WaitOne([TimeSpan]::FromSeconds(20))) { throw "Could not update delegation registry." }
    try {
        $registry = @(Get-Content -Raw $registryPath | ConvertFrom-Json)
        foreach ($item in $registry) {
            if ($item.task_id -eq $packet.task_id) {
                $item.status = "pull_request"
                $item.pr_url = $prUrl
                $item.pid = 0
            }
        }
        @($registry) | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 $registryPath
    } finally { $registryMutex.ReleaseMutex(); $registryMutex.Dispose() }
    $success = $true
    Write-Host "DELEGATION_CREATED: $prUrl"
} finally {
    if (-not $success -and (Test-Path $worktree)) { Write-Warning "Worktree retained for diagnosis: $worktree" }
    if (-not $success) {
        $cleanupMutex = [Threading.Mutex]::new($false, "Global\TaskDelegationRegistry")
        if ($cleanupMutex.WaitOne([TimeSpan]::FromSeconds(20))) {
            try {
                $registry = if (Test-Path $registryPath) { @(Get-Content -Raw $registryPath | ConvertFrom-Json) } else { @() }
                @($registry | Where-Object { $_.task_id -ne $packet.task_id }) | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 $registryPath
            } finally { $cleanupMutex.ReleaseMutex() }
        }
        $cleanupMutex.Dispose()
    }
}
