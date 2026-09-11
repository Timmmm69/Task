[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$PackageDirectory,
    [switch]$SkipReleaseHashes
)

$ErrorActionPreference = 'Stop'

if (-not $RepositoryRoot) {
    $RepositoryRoot = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..\..')).Path
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Contains {
    param([string]$Path, [string[]]$Needles)
    $content = [string](Get-Content -LiteralPath $Path -Raw -Encoding UTF8)
    foreach ($needle in $Needles) {
        Assert-True ($content.Contains($needle)) "Missing '$needle' in $Path"
    }
}

function Read-Json {
    param([string]$Path)
    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "Missing file: $Path"
    Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

$roadmapPath = Join-Path $RepositoryRoot '.project-dashboard\roadmap.json'
$roadmap = Read-Json $roadmapPath

$items = @{}
foreach ($item in $roadmap.items) { $items[$item.id] = $item }

$gateSummary = @()
$gateTotal = 0
$gatePassed = 0
foreach ($gate in $roadmap.release_gates) {
    $gateOk = $true
    foreach ($required in $gate.required_items) {
        $gateTotal++
        $entry = $items[$required]
        if ($null -eq $entry) { throw "Gate $($gate.id) references unknown item: $required" }
        if ($entry.status -ne 'done' -or $entry.progress -ne 100) {
            $gateOk = $false
            throw "Gate $($gate.id): $required is not done@100 ($($entry.status)/$($entry.progress))"
        }
        $gatePassed++
    }
    $gateSummary += "$($gate.id): $($gate.required_items.Count)/$($gate.required_items.Count) done"
}

foreach ($dependency in @('HAND-02', 'HAND-03', 'HAND-04', 'QA-04')) {
    $entry = $items[$dependency]
    Assert-True ($null -ne $entry -and $entry.status -eq 'done' -and $entry.progress -eq 100) "HAND-05 dependency not done@100: $dependency"
}

$docs = Join-Path $RepositoryRoot 'work\production\docs'
$signOff = Join-Path $docs 'HAND-05-internal-readiness-sign-off.md'
Assert-True (Test-Path -LiteralPath $signOff -PathType Leaf) "Missing sign-off document: $signOff"
Assert-Contains $signOff @(
    'INTERNAL READINESS',
    'APPROVED (PASS)',
    'Сверка release gates',
    'Disposition внутренних findings',
    'Сверка состава release package',
    'Post-handoff действия заказчика'
)

$findingsCsv = Join-Path $docs 'HAND-05-findings-disposition.csv'
Assert-True (Test-Path -LiteralPath $findingsCsv -PathType Leaf) "Missing findings register: $findingsCsv"
$findings = @(Import-Csv -LiteralPath $findingsCsv)
Assert-True ($findings.Count -ge 5) "Findings register must contain at least 5 rows"
$criticalHighReviewed = 0
foreach ($row in $findings) {
    Assert-True ($row.disposition -ne 'open' -and $row.status -ne 'open') "Open finding in register: $($row.id)"
    if ($row.severity -in @('Critical', 'High')) {
        $criticalHighReviewed++
        Assert-True ($row.disposition -eq 'fixed' -and $row.status -eq 'closed') "Critical/High finding is not fixed: $($row.id)"
    }
}

$actionsCsv = Join-Path $docs 'HAND-05-post-handoff-actions.csv'
Assert-True (Test-Path -LiteralPath $actionsCsv -PathType Leaf) "Missing post-handoff actions: $actionsCsv"
$actions = @(Import-Csv -LiteralPath $actionsCsv)
Assert-True ($actions.Count -ge 12) "Post-handoff actions must contain at least 12 rows"

$rcDir = Join-Path $RepositoryRoot 'outputs\20260911_hand03_release_candidate_1.0.0'
$rcManifestPath = Join-Path $rcDir 'manifest.json'
$rcManifest = Read-Json $rcManifestPath

Assert-True ($rcManifest.task -eq 'HAND-03') 'RC manifest task is not HAND-03.'
Assert-True ($rcManifest.releaseVersion -eq '1.0.0') 'RC releaseVersion is not 1.0.0.'
Assert-True ($rcManifest.classification -eq 'internal-release-candidate') 'RC classification is not internal-release-candidate.'
Assert-True ($rcManifest.components.server.version -eq '0.6.0') 'RC server version is not 0.6.0.'
Assert-True (@($rcManifest.components.server.images).Count -eq 5) 'RC server images are not 5.'
Assert-True ($rcManifest.components.desktop.version -eq '1.0.0') 'RC desktop version is not 1.0.0.'
Assert-True ($rcManifest.components.desktop.architecture -eq 'win-x64') 'RC desktop architecture is not win-x64.'
Assert-True ($rcManifest.components.desktop.publisherThumbprint -eq '0CA60C002A379AC5CF8FAD09FF9E5A02512457B8') 'RC desktop publisher thumbprint mismatch.'
Assert-True ($rcManifest.compliance.sbom -eq 'SPDX-2.3') 'RC SBOM is not SPDX-2.3.'
Assert-True ($rcManifest.compliance.packages -eq 46) 'RC SBOM package count is not 46.'
Assert-True ($rcManifest.compliance.licenseRows -eq 46) 'RC license row count is not 46.'
Assert-True (@($rcManifest.files).Count -eq 65) 'RC payload file count is not 65.'

foreach ($required in @(
    'validation-report.md',
    'Verify-Release.ps1',
    'SHA256SUMS',
    'README.md',
    'signature/signer.cer',
    'signature/signature.json',
    'compliance/sbom.spdx.json',
    'compliance/third-party-licenses.csv',
    'components/server/image-map.json',
    'components/server/release.json',
    'components/desktop/channel.json',
    'components/desktop/Task.Desktop-1.0.0-win-x64.zip',
    'source/task-production.tar'
)) {
    Assert-True (Test-Path -LiteralPath (Join-Path $rcDir ($required -replace '/', '\')) -PathType Leaf) "RC component is missing: $required"
}

Assert-Contains (Join-Path $rcDir 'validation-report.md') @('PASS')

$hashCache = @{}
$verifiedFiles = 0
if (-not $SkipReleaseHashes) {
    foreach ($entry in $rcManifest.files) {
        $relative = $entry.path -replace '/', '\'
        $full = Join-Path $rcDir $relative
        Assert-True (Test-Path -LiteralPath $full -PathType Leaf) "RC manifest file is missing: $relative"
        $item = Get-Item -LiteralPath $full
        Assert-True ($item.Length -eq [int64]$entry.size) "RC manifest size mismatch: $relative"
        if (-not $hashCache.ContainsKey($full)) {
            $hashCache[$full] = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        Assert-True ($hashCache[$full] -eq $entry.sha256) "RC manifest hash mismatch: $relative"
        $verifiedFiles++
    }
    foreach ($line in Get-Content -LiteralPath (Join-Path $rcDir 'SHA256SUMS') -Encoding UTF8) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split '\s{2,}', 2
        Assert-True ($parts.Count -eq 2) "Invalid RC SHA256SUMS record: $line"
        $full = Join-Path $rcDir ($parts[1] -replace '/', '\')
        Assert-True (Test-Path -LiteralPath $full -PathType Leaf) "RC SHA256SUMS target is missing: $parts[1]"
        if (-not $hashCache.ContainsKey($full)) {
            $hashCache[$full] = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        Assert-True ($hashCache[$full] -eq $parts[0].ToLowerInvariant()) "RC SHA256SUMS mismatch: $parts[1]"
    }
}

$installEvidence = Read-Json (Join-Path $RepositoryRoot 'work\production\evidence\ops-runbook\install\install.json')
Assert-True ($installEvidence.result -eq 'PASS') 'HAND-02 install reproduction is not PASS.'
foreach ($gate in $installEvidence.gates) {
    Assert-True ($gate.result -eq 'PASS') "HAND-02 install gate is not PASS: $($gate.name)"
}
$operateEvidence = Read-Json (Join-Path $RepositoryRoot 'work\production\evidence\ops-runbook\operate\checks.json')
Assert-True ($operateEvidence.result -eq 'PASS') 'HAND-02 operate/recover reproduction is not PASS.'
foreach ($gate in $operateEvidence.gates) {
    Assert-True ($gate.result -eq 'PASS') "HAND-02 operate gate is not PASS: $($gate.name)"
}

$qa04SignOff = Join-Path $RepositoryRoot 'outputs\20260911_qa04_accessibility_usability_1.1.0\RELEASE_SIGN_OFF.md'
Assert-True (Test-Path -LiteralPath $qa04SignOff -PathType Leaf) "QA-04 release sign-off is missing: $qa04SignOff"
Assert-Contains $qa04SignOff @('APPROVED')

$hand04Manifest = Read-Json (Join-Path $RepositoryRoot 'outputs\20260911_hand04_user_admin_guides_1.0.0\manifest.json')
foreach ($guide in @('HAND-04-user-guide.md', 'HAND-04-administrator-guide.md', 'HAND-04-demo-walkthrough.md')) {
    Assert-True ($hand04Manifest.package_files -contains $guide) "HAND-04 package is missing $guide"
}

if ($PackageDirectory) {
    $resolvedPackage = (Resolve-Path -LiteralPath $PackageDirectory).Path
    $sumsPath = Join-Path $resolvedPackage 'SHA256SUMS'
    Assert-True (Test-Path -LiteralPath $sumsPath -PathType Leaf) "Package checksum file is missing: $sumsPath"
    foreach ($line in Get-Content -LiteralPath $sumsPath -Encoding UTF8) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split '\s{2,}', 2
        Assert-True ($parts.Count -eq 2) "Invalid checksum record: $line"
        $file = Join-Path $resolvedPackage $parts[1]
        Assert-True (Test-Path -LiteralPath $file -PathType Leaf) "Checksum target is missing: $file"
        Assert-True ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -eq $parts[0].ToLowerInvariant()) "Checksum mismatch: $file"
    }
}

[pscustomobject]@{
    result = 'PASS'
    decision = 'INTERNAL READINESS APPROVED'
    gates = $gateSummary
    gate_items_verified = $gatePassed
    dependencies = @('HAND-02', 'HAND-03', 'HAND-04', 'QA-04')
    findings = $findings.Count
    critical_high_reviewed = $criticalHighReviewed
    open_findings = 0
    blocking_open = 0
    post_handoff_actions = $actions.Count
    release_candidate = 'outputs/20260911_hand03_release_candidate_1.0.0'
    release_files = @($rcManifest.files).Count
    release_hashes_verified = $verifiedFiles
} | ConvertTo-Json -Depth 4
