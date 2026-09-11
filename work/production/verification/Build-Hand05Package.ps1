[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

if (-not $RepositoryRoot) {
    $RepositoryRoot = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..\..')).Path
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $RepositoryRoot 'outputs\20260911_hand05_internal_readiness_signoff_1.0.0'
}

$docs = Join-Path $RepositoryRoot 'work\production\docs'
$testScript = Join-Path $RepositoryRoot 'work\production\verification\Test-Hand05Readiness.ps1'

function Write-TextFile {
    param([string]$Path, [string]$Value, [switch]$NoNewline)
    $content = $Value
    if (-not $NoNewline) {
        $content = $content -replace "`r?`n", "`r`n"
    }
    [System.IO.File]::WriteAllText($Path, $content, (New-Object System.Text.UTF8Encoding($false)))
}

& $testScript -RepositoryRoot $RepositoryRoot | Out-Null

if (Test-Path -LiteralPath $OutputDirectory) {
    $resolved = (Resolve-Path -LiteralPath $OutputDirectory).Path
    $outputsRoot = (Resolve-Path (Join-Path $RepositoryRoot 'outputs')).Path
    if (-not $resolved.StartsWith($outputsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace output outside outputs: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($name in @(
    'HAND-05-internal-readiness-sign-off.md',
    'HAND-05-findings-disposition.csv',
    'HAND-05-post-handoff-actions.csv'
)) {
    Copy-Item -LiteralPath (Join-Path $docs $name) -Destination (Join-Path $OutputDirectory $name)
}

Write-TextFile -Path (Join-Path $OutputDirectory 'VERSION') -Value '1.0.0' -NoNewline

$readme = @'
# HAND-05 Internal readiness sign-off

Version 1.0.0. This package contains the unified internal readiness sign-off for the Task release candidate: six release gates, consolidated findings disposition and the post-handoff customer action list.

Start with `HAND-05-internal-readiness-sign-off.md`. The sign-off confirms functionality, UX, security, operations, findings disposition and release package composition in the scope a developer can close without customer data, infrastructure or approvals. Customer acceptance follows the handoff.

The package does not contain secrets, tokens, private keys or production seed data, and it does not replace customer acceptance, corporate code signing or the HAND-03 production publication procedure.
'@
Write-TextFile -Path (Join-Path $OutputDirectory 'README.md') -Value $readme.Trim()

$sourceFiles = @(
    'work/production/docs/HAND-05-internal-readiness-sign-off.md',
    'work/production/docs/HAND-05-findings-disposition.csv',
    'work/production/docs/HAND-05-post-handoff-actions.csv',
    'work/production/verification/Test-Hand05Readiness.ps1',
    'work/production/verification/Build-Hand05Package.ps1',
    'outputs/20260911_hand03_release_candidate_1.0.0/manifest.json',
    'outputs/20260911_qa04_accessibility_usability_1.1.0/RELEASE_SIGN_OFF.md',
    '.project-dashboard/roadmap.json'
)

$sourceInventory = foreach ($relativePath in $sourceFiles) {
    $fullPath = Join-Path $RepositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "Missing source file: $relativePath" }
    $item = Get-Item -LiteralPath $fullPath
    [ordered]@{ path = $relativePath; bytes = $item.Length; sha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant() }
}

$manifest = [ordered]@{
    package = 'HAND-05 internal readiness sign-off'
    version = '1.0.0'
    date = '2026-09-11'
    roadmap_id = 'HAND-05'
    state = 'implemented-and-validated-for-handoff'
    decision = 'INTERNAL READINESS APPROVED (PASS)'
    source_evidence = [ordered]@{
        release_candidate = 'outputs/20260911_hand03_release_candidate_1.0.0'
        release_gates = 6
        gate_items = 30
        findings_open = 0
        findings_blocking = 0
        runbook_install = 'work/production/evidence/ops-runbook/install/install.json: PASS'
        runbook_operate = 'work/production/evidence/ops-runbook/operate/checks.json: PASS'
        qa04_sign_off = 'outputs/20260911_qa04_accessibility_usability_1.1.0/RELEASE_SIGN_OFF.md: APPROVED'
    }
    source_files = @($sourceInventory)
    package_files = @(
        'README.md',
        'VERSION',
        'HAND-05-internal-readiness-sign-off.md',
        'HAND-05-findings-disposition.csv',
        'HAND-05-post-handoff-actions.csv',
        'validation-report.md'
    )
}
Write-TextFile -Path (Join-Path $OutputDirectory 'manifest.json') -Value ($manifest | ConvertTo-Json -Depth 8)

$validation = @'
# HAND-05 validation report

Result: PASS

The package contains the unified internal readiness sign-off, the consolidated findings disposition and the post-handoff customer action list. The static gate verifies the six roadmap release gates, HAND-02/03/04/QA-04 dependency evidence, findings disposition, release candidate composition (65/65 SHA-256) and package checksums.

The sign-off confirms only what a developer can close without customer data, infrastructure and approvals. Corporate secrets manager, CA, DNS, storage, alert webhook, corporate code signing, real accounts, production phases A-H and formal customer acceptance remain post-handoff actions and are not claimed here.

Validation command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File work/production/verification/Test-Hand05Readiness.ps1 -PackageDirectory outputs/20260911_hand05_internal_readiness_signoff_1.0.0
```
'@
Write-TextFile -Path (Join-Path $OutputDirectory 'validation-report.md') -Value $validation.Trim()

$checksumTargets = Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object Name -ne 'SHA256SUMS' | Sort-Object Name
$checksums = foreach ($file in $checksumTargets) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $file.Name
}
Write-TextFile -Path (Join-Path $OutputDirectory 'SHA256SUMS') -Value ($checksums -join [Environment]::NewLine) -NoNewline

& $testScript -RepositoryRoot $RepositoryRoot -PackageDirectory $OutputDirectory
