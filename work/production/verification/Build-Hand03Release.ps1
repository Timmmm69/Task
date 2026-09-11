#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.0',
    [string]$OutputDirectory,
    [switch]$AllowEphemeralValidationSigner
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo "outputs/$(Get-Date -Format yyyyMMdd)_hand03_release_candidate_$Version" }
$output = [IO.Path]::GetFullPath($OutputDirectory, $repo)
$outputsRoot = [IO.Path]::GetFullPath((Join-Path $repo 'outputs')) + [IO.Path]::DirectorySeparatorChar
if (-not $output.StartsWith($outputsRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be below outputs/.' }
if (Test-Path -LiteralPath $output) { throw "Output already exists: $output" }
if (-not $AllowEphemeralValidationSigner) { throw 'Pass -AllowEphemeralValidationSigner for an internal RC, or extend this script with an approved corporate certificate before production publication.' }

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    $result = @(& $Command @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "$Command failed ($LASTEXITCODE): $($result -join "`n")" }
    return ($result -join "`n").Trim()
}
function Get-Sha([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Write-Json([string]$Path, $Value) { $Value | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM }
function Copy-TreeFile([string]$Source, [string]$Destination) {
    $parent = Split-Path -Parent $Destination
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    Copy-Item -LiteralPath $Source -Destination $Destination
}

foreach ($command in 'git','dotnet','node','pwsh','tar') { Get-Command $command -ErrorAction Stop | Out-Null }
$pending = Invoke-Checked git @('-C',$repo,'status','--porcelain','--untracked-files=no','--','work/production')
if ($pending) { throw 'HAND-03 must bind to a committed work/production tree; commit scoped production changes first.' }
$revision = Invoke-Checked git @('-C',$repo,'rev-parse','HEAD')
$productionTree = Invoke-Checked git @('-C',$repo,'rev-parse','HEAD:work/production')
$epoch = [long](Invoke-Checked git @('-C',$repo,'show','-s','--format=%ct','HEAD'))
$containerSource = Join-Path $repo 'outputs/20260911_task_container_release_0.6.0'
$containerRelease = Get-Content -LiteralPath (Join-Path $containerSource 'release.json') -Raw | ConvertFrom-Json
if ($containerRelease.status -ne 'PASS') { throw 'Verified container release is not PASS.' }
Invoke-Checked node @((Join-Path $repo 'work/production/deployment/containers/verify-release.mjs'),$containerSource) | Write-Host

$cleanupAllowed = $output.StartsWith($outputsRoot, [StringComparison]::OrdinalIgnoreCase)
try {
    New-Item -ItemType Directory -Path $output | Out-Null
    foreach ($relative in @('components/server','components/desktop','source','compliance','signature')) { New-Item -ItemType Directory -Path (Join-Path $output $relative) -Force | Out-Null }

    foreach ($name in @('release.json','source.json','image-map.json','validation-report.md')) { Copy-TreeFile (Join-Path $containerSource $name) (Join-Path $output "components/server/$name") }
    Copy-Item -LiteralPath (Join-Path $containerSource 'evidence') -Destination (Join-Path $output 'components/server/evidence') -Recurse
    foreach ($target in @('task-api','task-worker','task-backup-agent','task-database-migrator','task-container-validation')) {
        Copy-TreeFile (Join-Path $containerSource "images/$target-1.oci.tar") (Join-Path $output "components/server/$target.oci.tar")
    }

    $rsa = [Security.Cryptography.RSA]::Create(3072)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        "CN=Task HAND-03 Internal RC $Version", $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,$true))
    $oids = [Security.Cryptography.OidCollection]::new(); [void]$oids.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids,$true))
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::FromUnixTimeSeconds($epoch).AddMinutes(-5), [DateTimeOffset]::FromUnixTimeSeconds($epoch).AddYears(2))
    [IO.File]::WriteAllBytes((Join-Path $output 'signature/signer.cer'), $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))

    & (Join-Path $repo 'work/production/deployment/desktop/Build-TaskDesktopRelease.ps1') -Version $Version -Certificate $certificate -OutputDirectory (Join-Path $output 'components/desktop') -MinimumClientVersion $Version
    if ($LASTEXITCODE -ne 0) { throw 'Desktop release build failed.' }

    $sourceTar = Join-Path $output 'source/task-production.tar'
    Invoke-Checked git @('-C',$repo,'archive','--format=tar',"--mtime=@$epoch","--output=$sourceTar","$revision`:work/production") | Out-Null
    Write-Json (Join-Path $output 'source/source.json') ([ordered]@{ revision=$revision; productionTree=$productionTree; sourceDateEpoch=$epoch; archiveSha256=Get-Sha $sourceTar })

    $packages = [ordered]@{}
    foreach ($lockPath in Get-ChildItem -LiteralPath (Join-Path $repo 'work/production') -Filter packages.lock.json -Recurse -File) {
        $lock = Get-Content -LiteralPath $lockPath.FullName -Raw | ConvertFrom-Json
        foreach ($framework in $lock.dependencies.PSObject.Properties) {
            foreach ($dependency in $framework.Value.PSObject.Properties) {
                $value = $dependency.Value
                if ($value.type -eq 'Project' -or -not $value.resolved) { continue }
                $key = "$($dependency.Name.ToLowerInvariant())@$($value.resolved)"
                if (-not $packages.Contains($key)) { $packages[$key] = [ordered]@{ name=$dependency.Name; version=[string]$value.resolved; contentHash=[string]$value.contentHash } }
            }
        }
    }
    $nugetLine = Invoke-Checked dotnet @('nuget','locals','global-packages','--list')
    $nugetRoot = ($nugetLine -split ': ',2)[1].Trim()
    $licenseRows = @()
    $spdxPackages = @()
    foreach ($item in $packages.Values | Sort-Object name,version) {
        $folder = Join-Path $nugetRoot "$($item.name.ToLowerInvariant())/$($item.version.ToLowerInvariant())"
        $nuspec = Get-ChildItem -LiteralPath $folder -Filter '*.nuspec' -File | Select-Object -First 1
        if (-not $nuspec) { throw "NuGet metadata unavailable for $($item.name) $($item.version)" }
        [xml]$xml = Get-Content -LiteralPath $nuspec.FullName -Raw
        $metadata = $xml.package.metadata
        $declared = if ($metadata.license.'#text') { [string]$metadata.license.'#text' } elseif ($metadata.license) { [string]$metadata.license } elseif ($metadata.licenseUrl) { [string]$metadata.licenseUrl } else { 'NOASSERTION' }
        $licenseRows += [pscustomobject]@{ package=$item.name; version=$item.version; licenseDeclared=$declared; projectUrl=[string]$metadata.projectUrl; source='NuGet package nuspec' }
        $spdxId = 'SPDXRef-Package-' + (($item.name + '-' + $item.version) -replace '[^A-Za-z0-9.-]','-')
        $spdxPackages += [ordered]@{ SPDXID=$spdxId; name=$item.name; versionInfo=$item.version; downloadLocation="https://www.nuget.org/packages/$($item.name)/$($item.version)"; filesAnalyzed=$false; licenseConcluded='NOASSERTION'; licenseDeclared=$declared; copyrightText='NOASSERTION'; externalRefs=@([ordered]@{ referenceCategory='PACKAGE-MANAGER'; referenceType='purl'; referenceLocator="pkg:nuget/$($item.name)@$($item.version)" }) }
    }
    $licenseRows | Export-Csv -LiteralPath (Join-Path $output 'compliance/third-party-licenses.csv') -NoTypeInformation -Encoding utf8NoBOM
    $namespace = "https://task.local/spdx/hand-03/$Version/$productionTree"
    $sbom = [ordered]@{ spdxVersion='SPDX-2.3'; dataLicense='CC0-1.0'; SPDXID='SPDXRef-DOCUMENT'; name="Task-$Version"; documentNamespace=$namespace; creationInfo=[ordered]@{ created=[DateTimeOffset]::FromUnixTimeSeconds($epoch).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ'); creators=@('Tool: Task-HAND-03-builder-1.0') }; packages=$spdxPackages; relationships=@($spdxPackages | ForEach-Object { [ordered]@{ spdxElementId='SPDXRef-DOCUMENT'; relationshipType='DESCRIBES'; relatedSpdxElement=$_.SPDXID } }) }
    Write-Json (Join-Path $output 'compliance/sbom.spdx.json') $sbom

    Copy-TreeFile (Join-Path $repo 'work/production/docs/HAND-03-release-package.md') (Join-Path $output 'README.md')
    Copy-TreeFile (Join-Path $repo 'work/production/verification/Verify-Hand03Release.ps1') (Join-Path $output 'Verify-Release.ps1')
    $preReport = Join-Path $output 'validation-report.md'
    @('# HAND-03 validation report','','Pending independent verifier execution.') | Set-Content -LiteralPath $preReport -Encoding utf8NoBOM

    $componentServer = [ordered]@{ version=[string]$containerRelease.version; sourceRevision=[string]$containerRelease.revision; status=[string]$containerRelease.status; images=@($containerRelease.images | ForEach-Object target) }
    $manifestFiles = @(Get-ChildItem -LiteralPath $output -Recurse -Force -File | Where-Object { [IO.Path]::GetRelativePath($output,$_.FullName).Replace('\','/') -notin @('manifest.json','SHA256SUMS','signature/manifest.p7s','signature/signature.json') } | Sort-Object FullName | ForEach-Object { [ordered]@{ path=[IO.Path]::GetRelativePath($output,$_.FullName).Replace('\','/'); size=$_.Length; sha256=Get-Sha $_.FullName } })
    $manifest = [ordered]@{ schemaVersion=1; task='HAND-03'; releaseVersion=$Version; classification='internal-release-candidate'; sourceRevision=$revision; productionTree=$productionTree; sourceDateEpoch=$epoch; components=[ordered]@{ server=$componentServer; desktop=[ordered]@{ version=$Version; architecture='win-x64'; publisherThumbprint=$certificate.Thumbprint } }; compliance=[ordered]@{ sbom='SPDX-2.3'; packages=$spdxPackages.Count; licenseRows=$licenseRows.Count }; files=$manifestFiles }
    Write-Json (Join-Path $output 'manifest.json') $manifest

    Add-Type -AssemblyName System.Security.Cryptography.Pkcs
    $manifestPath = Join-Path $output 'manifest.json'
    function Seal-Release {
        $cms = [Security.Cryptography.Pkcs.SignedCms]::new([Security.Cryptography.Pkcs.ContentInfo]::new([IO.File]::ReadAllBytes($manifestPath)),$true)
        $signer = [Security.Cryptography.Pkcs.CmsSigner]::new([Security.Cryptography.Pkcs.SubjectIdentifierType]::IssuerAndSerialNumber,$certificate)
        $signer.IncludeOption = [Security.Cryptography.X509Certificates.X509IncludeOption]::EndCertOnly
        $cms.ComputeSignature($signer)
        [IO.File]::WriteAllBytes((Join-Path $output 'signature/manifest.p7s'),$cms.Encode())
        Write-Json (Join-Path $output 'signature/signature.json') ([ordered]@{ algorithm='CMS-SHA256-RSA'; trust='internal-self-signed-validation'; manifestSha256=Get-Sha $manifestPath; signerThumbprint=$certificate.Thumbprint; certificateSha256=Get-Sha (Join-Path $output 'signature/signer.cer') })
        @(Get-ChildItem -LiteralPath $output -Recurse -Force -File | Where-Object Name -ne 'SHA256SUMS' | Sort-Object FullName | ForEach-Object { "$(Get-Sha $_.FullName)  $([IO.Path]::GetRelativePath($output,$_.FullName).Replace('\','/'))" }) | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS') -Encoding utf8NoBOM
    }
    Seal-Release
    # The verifier is a separate executable gate. Its first sealed pass produces the report; the second checks the final seal.
    & (Join-Path $repo 'work/production/verification/Verify-Hand03Release.ps1') -ReleaseDirectory $output -ReportPath $preReport -SkipArchive | Out-Null
    $reportEntry = $manifest.files | Where-Object path -eq 'validation-report.md'
    $reportEntry.size = (Get-Item -LiteralPath $preReport).Length
    $reportEntry.sha256 = Get-Sha $preReport
    Write-Json $manifestPath $manifest
    Seal-Release
    & (Join-Path $repo 'work/production/verification/Verify-Hand03Release.ps1') -ReleaseDirectory $output -SkipArchive | Out-Null

    $archive = "$output.zip"
    Add-Type -AssemblyName System.IO.Compression
    $zipStream = [IO.File]::Open($archive,[IO.FileMode]::CreateNew)
    try {
        $zip = [IO.Compression.ZipArchive]::new($zipStream,[IO.Compression.ZipArchiveMode]::Create,$true)
        try {
            foreach ($file in Get-ChildItem -LiteralPath $output -Recurse -Force -File | Sort-Object FullName) {
                $relative = [IO.Path]::GetRelativePath($output,$file.FullName).Replace('\','/')
                $entry = $zip.CreateEntry($relative,[IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::FromUnixTimeSeconds($epoch)
                $input = $file.OpenRead(); $destination = $entry.Open()
                try { $input.CopyTo($destination) } finally { $destination.Dispose(); $input.Dispose() }
            }
        } finally { $zip.Dispose() }
    } finally { $zipStream.Dispose() }
    & (Join-Path $repo 'work/production/verification/Verify-Hand03Release.ps1') -ReleaseDirectory $output | Out-Null
    "$(Get-Sha $archive)  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
    [ordered]@{ result='PASS'; package=$archive; sha256=Get-Sha $archive; manifestSha256=Get-Sha $manifestPath; signerThumbprint=$certificate.Thumbprint } | ConvertTo-Json
}
catch {
    if ($cleanupAllowed -and (Test-Path -LiteralPath $output)) { Remove-Item -LiteralPath $output -Recurse -Force }
    if ($cleanupAllowed -and (Test-Path -LiteralPath "$output.zip")) { Remove-Item -LiteralPath "$output.zip" -Force }
    throw
}
