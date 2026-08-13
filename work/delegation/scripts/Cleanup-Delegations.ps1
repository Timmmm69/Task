param([switch]$IncludeStaleProcesses)
$ErrorActionPreference = "Stop"
$root = (& git rev-parse --show-toplevel).Trim()
$registryPath = Join-Path $root "work\tmp\delegation-registry.json"
if (-not (Test-Path $registryPath)) { return }
$registry = @(Get-Content -Raw $registryPath | ConvertFrom-Json)
$kept = [Collections.Generic.List[object]]::new()
foreach ($item in $registry) {
    $remove = $false
    $prState = (& gh pr view $item.branch --json state --jq .state 2>$null)
    if ($LASTEXITCODE -eq 0 -and $prState -in @("MERGED", "CLOSED")) { $remove = $true }
    if ($IncludeStaleProcesses -and $item.status -eq "running" -and -not (Get-Process -Id $item.pid -ErrorAction SilentlyContinue)) { $remove = $true }
    if ($remove) {
        if (Test-Path $item.worktree) { & git worktree remove --force $item.worktree | Out-Null }
        & git branch -D $item.branch 2>$null | Out-Null
    } else { $kept.Add($item) }
}
@($kept) | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 $registryPath
& git worktree prune
