$ErrorActionPreference = "Stop"
$root = (& git rev-parse --show-toplevel).Trim()
$output = Join-Path $root "outputs\delegation_system"
$relativeFiles = @(
    ".github/CODEOWNERS",
    ".github/workflows/ci.yml",
    ".opencode/agents/task-delegate.md",
    ".opencode/agents/task-worker.md",
    ".opencode/commands/delegate.md",
    ".gitignore",
    "work/delegation/README.md",
    "work/delegation/scripts/Cleanup-Delegations.ps1",
    "work/delegation/scripts/Delegation.Common.ps1",
    "work/delegation/scripts/Invoke-Delegation.ps1",
    "work/delegation/scripts/New-DelegationManifest.ps1",
    "work/delegation/scripts/Setup-Delegation.ps1",
    "work/delegation/scripts/Test-DelegationPacket.ps1",
    "work/delegation/scripts/Test-DelegationPr.ps1",
    "work/delegation/templates/DELEGATION_PACKET.yaml",
    "work/delegation/tests/Delegation.Tests.ps1",
    "outputs/delegation_system/VERSION.txt",
    "outputs/delegation_system/VALIDATION_REPORT.md"
)
$entries = foreach ($relative in $relativeFiles) {
    $absolute = Join-Path $root ($relative.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $absolute)) { throw "Manifest input missing: $relative" }
    $item = Get-Item -LiteralPath $absolute
    [ordered]@{
        path = $relative
        bytes = $item.Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $absolute).Hash.ToLowerInvariant()
    }
}
$manifest = [ordered]@{
    artifact = "Task DeepSeek Delegation System"
    version = (Get-Content -Raw (Join-Path $output "VERSION.txt")).Trim()
    generated_at = (Get-Date).ToUniversalTime().ToString("o")
    files = @($entries)
}
$manifestPath = Join-Path $output "manifest.json"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 $manifestPath
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $manifestPath).Hash.ToLowerInvariant()
"$hash  manifest.json" | Set-Content -Encoding ASCII (Join-Path $output "MANIFEST.sha256")
Write-Host "Manifest generated for $($entries.Count) files."
