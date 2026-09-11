#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [string]$ReportPath,
    [switch]$SkipArchive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security.Cryptography.Pkcs

$root = [IO.Path]::GetFullPath($ReleaseDirectory)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Release directory does not exist: $root" }

function Read-Json([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing required file: $Path" }
    return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json
}
function Get-Sha([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

$checks = [Collections.Generic.List[object]]::new()
function Add-Check([string]$Name, [bool]$Passed, [string]$Detail) {
    $checks.Add([ordered]@{ name = $Name; passed = $Passed; detail = $Detail })
    if (-not $Passed) { throw "$Name failed: $Detail" }
}

$manifestPath = Join-Path $root 'manifest.json'
$manifest = Read-Json $manifestPath
Add-Check 'manifest schema/version' ($manifest.schemaVersion -eq 1 -and $manifest.releaseVersion -match '^\d+\.\d+\.\d+$') "version=$($manifest.releaseVersion)"

$actualPaths = @(Get-ChildItem -LiteralPath $root -Recurse -Force -File | ForEach-Object {
    [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\','/')
} | Where-Object { $_ -notin @('manifest.json','SHA256SUMS','signature/manifest.p7s','signature/signature.json') } | Sort-Object -CaseSensitive)
$manifestPaths = @($manifest.files | ForEach-Object { $_.path } | Sort-Object -CaseSensitive)
Add-Check 'manifest inventory' (($actualPaths -join "`n") -ceq ($manifestPaths -join "`n")) "$($manifestPaths.Count) files"
foreach ($entry in $manifest.files) {
    $path = Join-Path $root ($entry.path.Replace('/', [IO.Path]::DirectorySeparatorChar))
    Add-Check "file $($entry.path)" ((Test-Path -LiteralPath $path -PathType Leaf) -and (Get-Sha $path) -eq $entry.sha256 -and (Get-Item -LiteralPath $path).Length -eq $entry.size) 'size and SHA-256 match'
}

$sumPath = Join-Path $root 'SHA256SUMS'
$sumLines = @(Get-Content -LiteralPath $sumPath -Encoding utf8 | Where-Object { $_ })
foreach ($line in $sumLines) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Invalid SHA256SUMS line: $line" }
    $path = Join-Path $root ($Matches[2].Replace('/', [IO.Path]::DirectorySeparatorChar))
    Add-Check "checksum $($Matches[2])" ((Test-Path -LiteralPath $path -PathType Leaf) -and (Get-Sha $path) -eq $Matches[1]) 'matches'
}
$summed = @($sumLines | ForEach-Object { ($_ -split '  ',2)[1] } | Sort-Object -CaseSensitive)
$allExceptSums = @(Get-ChildItem -LiteralPath $root -Recurse -Force -File | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\','/') } | Where-Object { $_ -ne 'SHA256SUMS' } | Sort-Object -CaseSensitive)
Add-Check 'SHA256SUMS inventory' (($summed -join "`n") -ceq ($allExceptSums -join "`n")) "$($summed.Count) files"

$signature = Read-Json (Join-Path $root 'signature/signature.json')
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path $root 'signature/signer.cer'))
Add-Check 'signer certificate pin' ($certificate.Thumbprint -eq $signature.signerThumbprint -and (Get-Sha (Join-Path $root 'signature/signer.cer')) -eq $signature.certificateSha256) $certificate.Thumbprint
$cms = [Security.Cryptography.Pkcs.SignedCms]::new([Security.Cryptography.Pkcs.ContentInfo]::new([IO.File]::ReadAllBytes($manifestPath)), $true)
$cms.Decode([IO.File]::ReadAllBytes((Join-Path $root 'signature/manifest.p7s')))
$cms.CheckSignature($true)
Add-Check 'detached CMS signature' ($cms.SignerInfos.Count -eq 1 -and $cms.SignerInfos[0].Certificate.Thumbprint -eq $certificate.Thumbprint -and $signature.manifestSha256 -eq (Get-Sha $manifestPath)) 'cryptographically valid and pinned'

$sbom = Read-Json (Join-Path $root 'compliance/sbom.spdx.json')
$licenses = @(Import-Csv -LiteralPath (Join-Path $root 'compliance/third-party-licenses.csv'))
Add-Check 'SPDX SBOM' ($sbom.spdxVersion -eq 'SPDX-2.3' -and $sbom.packages.Count -gt 0) "$($sbom.packages.Count) packages"
Add-Check 'license inventory' ($licenses.Count -eq $sbom.packages.Count -and @($licenses | Where-Object { -not $_.licenseDeclared }).Count -eq 0) "$($licenses.Count) package rows"

$container = Read-Json (Join-Path $root 'components/server/release.json')
$imageMap = Read-Json (Join-Path $root 'components/server/image-map.json')
Add-Check 'server component' ($container.status -eq 'PASS' -and $container.version -eq $manifest.components.server.version -and $container.revision -eq $manifest.components.server.sourceRevision) "$($container.images.Count) reproducible images"
foreach ($image in $container.images) {
    $record = Read-Json (Join-Path $root "components/server/evidence/$($image.target)-1.oci.json")
    Add-Check "OCI digest $($image.target)" ($imageMap.($image.target) -eq $record.indexDigest -and $image.imageDigest -eq $record.imageDigest -and $image.configDigest -eq $record.configDigest) $record.indexDigest
}

$desktopRoot = Join-Path $root "components/desktop/Task.Desktop-$($manifest.components.desktop.version)"
Import-Module (Join-Path $desktopRoot 'tools/TaskDesktop.Release.psm1') -Force
Assert-TaskRelease $desktopRoot $certificate.Thumbprint | Out-Null
$desktopManifest = Read-Json (Join-Path $desktopRoot 'release-manifest.json')
Add-Check 'desktop component' ($desktopManifest.clientVersion -eq $manifest.components.desktop.version -and $desktopManifest.publisherThumbprint -eq $certificate.Thumbprint) 'signed release and publisher pin valid'

$source = Read-Json (Join-Path $root 'source/source.json')
Add-Check 'source binding' ($source.revision -eq $manifest.sourceRevision -and $source.productionTree -eq $manifest.productionTree -and (Get-Sha (Join-Path $root 'source/task-production.tar')) -eq $source.archiveSha256) $source.revision

if (-not $SkipArchive) {
    $archive = "$root.zip"
    if (Test-Path -LiteralPath $archive) {
        Add-Type -AssemblyName System.IO.Compression
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $zipPaths = @($zip.Entries | Where-Object { $_.Name } | ForEach-Object FullName | Sort-Object -CaseSensitive)
            $folderPaths = @(Get-ChildItem -LiteralPath $root -Recurse -Force -File | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\','/') } | Sort-Object -CaseSensitive)
            Add-Check 'archive inventory' (($zipPaths -join "`n") -ceq ($folderPaths -join "`n")) "$($zipPaths.Count) entries reopen successfully"
            foreach ($entry in $zip.Entries | Where-Object { $_.Name }) { $stream = $entry.Open(); try { $stream.CopyTo([IO.Stream]::Null) } finally { $stream.Dispose() } }
            Add-Check 'archive complete read' $true 'all entries read without CRC/decompression errors'
        } finally { $zip.Dispose() }
    }
}

$result = [ordered]@{
    task = 'HAND-03'; result = 'PASS'; releaseVersion = $manifest.releaseVersion
    sourceRevision = $manifest.sourceRevision; manifestSha256 = Get-Sha $manifestPath
    signerThumbprint = $certificate.Thumbprint; checksPassed = $checks.Count
    verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); checks = $checks
}
if ($ReportPath) {
    $lines = @(
        "# HAND-03 independent validation report — $($manifest.releaseVersion)", '',
        '**Result: PASS**', '',
        "- Source revision: ``$($manifest.sourceRevision)``", '- Final manifest SHA-256: recorded in `signature/signature.json` and verified by this gate.',
        "- Signer thumbprint: ``$($certificate.Thumbprint)``", "- Checks passed: $($checks.Count)", '',
        'The verifier recalculated every manifest and SHA256SUMS entry, verified the detached CMS signature and signer pin,',
        'validated the SPDX/license inventory, server reproducibility evidence and OCI digest map, reopened and completely read',
        'the archive when present, validated the source binding, and ran the signed desktop release publisher/hash checks.', '',
        'Trust boundary: this package uses the internal self-signed validation certificate included in the release. It proves',
        'integrity and single-signer consistency, not corporate publisher identity. Production deployment requires rebuilding',
        'the desktop component with the customer corporate code-signing certificate and RFC 3161 timestamp, then rerunning this gate.'
    )
    $lines | Set-Content -LiteralPath $ReportPath -Encoding utf8NoBOM
}
$result | ConvertTo-Json -Depth 8
