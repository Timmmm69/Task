[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$OutputDirectory = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path 'outputs\20260911_hand04_user_admin_guides_1.0.0')
)

$ErrorActionPreference = 'Stop'
$docs = Join-Path $RepositoryRoot 'work\production\docs'
$testScript = Join-Path $RepositoryRoot 'work\production\verification\Test-Hand04Documentation.ps1'

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
foreach ($name in @('HAND-04-user-guide.md', 'HAND-04-administrator-guide.md', 'HAND-04-demo-walkthrough.md')) {
    Copy-Item -LiteralPath (Join-Path $docs $name) -Destination (Join-Path $OutputDirectory $name)
}

Set-Content -LiteralPath (Join-Path $OutputDirectory 'VERSION') -Value '1.0.0' -NoNewline -Encoding utf8

$readme = @'
# HAND-04 User and administrator guides

Version 1.0.0. This delivery package contains concise Russian guides for Task users and administrators, plus a synthetic demo walkthrough based on the validated QA-03 clean-room profile.

Start with `HAND-04-user-guide.md` for daily client work and `HAND-04-administrator-guide.md` for account lifecycle, installation, updates and support boundaries. Use `HAND-04-demo-walkthrough.md` only on an isolated synthetic stand.

The package does not contain user passwords, tokens, private keys, production seeds or a bypass for TLS, code signing, Windows ACL or authorization checks.
'@
Set-Content -LiteralPath (Join-Path $OutputDirectory 'README.md') -Value $readme.Trim() -NoNewline -Encoding utf8

$sourceFiles = @(
    'work/production/docs/HAND-04-user-guide.md',
    'work/production/docs/HAND-04-administrator-guide.md',
    'work/production/docs/HAND-04-demo-walkthrough.md',
    'work/production/verification/Test-Hand04Documentation.ps1',
    'work/production/verification/Build-Hand04Package.ps1',
    'outputs/20260910_qa03_critical_e2e_1.0.0/manifest.json',
    '.project-dashboard/roadmap.json'
)

$sourceInventory = foreach ($relativePath in $sourceFiles) {
    $fullPath = Join-Path $RepositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "Missing source file: $relativePath" }
    $item = Get-Item -LiteralPath $fullPath
    [ordered]@{ path = $relativePath; bytes = $item.Length; sha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant() }
}

$manifest = [ordered]@{
    package = 'HAND-04 user and administrator handoff guides'
    version = '1.0.0'
    date = '2026-09-11'
    roadmap_id = 'HAND-04'
    state = 'implemented-and-validated-for-handoff'
    source_evidence = [ordered]@{
        qa03_package = 'outputs/20260910_qa03_critical_e2e_1.0.0'
        seed_profile = 'qa03-deterministic-v1'
        clean_room = 'real Release WPF, HTTPS API, PostgreSQL and worker'
    }
    source_files = @($sourceInventory)
    package_files = @('README.md', 'VERSION', 'HAND-04-user-guide.md', 'HAND-04-administrator-guide.md', 'HAND-04-demo-walkthrough.md', 'validation-report.md')
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'manifest.json') -NoNewline -Encoding utf8

$validation = @'
# HAND-04 validation report

Result: PASS

The handoff package contains the required user guide, administrator guide and synthetic demo walkthrough. The static validation checks the required safety instructions, identity lifecycle routes, role boundaries, QA-03 evidence reference and package checksums.

The referenced QA-03 package records the deterministic profile `qa03-deterministic-v1` with PASS for real Release WPF plus HTTPS API plus PostgreSQL, conflict recovery, offline/reconnect behavior and cleanup. The guides do not claim that customer infrastructure, corporate PKI, SMB ACL, real user accounts or formal customer acceptance were performed in this package.

Validation command:

```powershell
pwsh -NoProfile -File work/production/verification/Test-Hand04Documentation.ps1 -PackageDirectory outputs/20260911_hand04_user_admin_guides_1.0.0
```
'@
Set-Content -LiteralPath (Join-Path $OutputDirectory 'validation-report.md') -Value $validation.Trim() -NoNewline -Encoding utf8

$checksumTargets = Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object Name -ne 'SHA256SUMS' | Sort-Object Name
$checksums = foreach ($file in $checksumTargets) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $file.Name
}
Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS') -Value ($checksums -join [Environment]::NewLine) -NoNewline -Encoding utf8

& $testScript -RepositoryRoot $RepositoryRoot -PackageDirectory $OutputDirectory
