[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('edge', 'database')]
    [string]$Purpose,
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9](?:[A-Za-z0-9.-]{0,251}[A-Za-z0-9])?$')]
    [string]$DnsName,
    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$outputFullPath = [IO.Path]::GetFullPath($OutputDirectory)
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (($outputFullPath + [IO.Path]::DirectorySeparatorChar).StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'TLS private keys and CSRs must be generated outside the repository.'
}

New-Item -ItemType Directory -Path $outputFullPath -Force | Out-Null
$baseName = "task-$Purpose-$DnsName"
$keyPath = Join-Path $outputFullPath "$baseName.key"
$csrPath = Join-Path $outputFullPath "$baseName.csr"
$metadataPath = Join-Path $outputFullPath "$baseName.request.json"
foreach ($path in @($keyPath, $csrPath, $metadataPath)) {
    if (Test-Path -LiteralPath $path) {
        throw "Refusing to overwrite existing certificate-request material: $path"
    }
}

$key = [Security.Cryptography.ECDsa]::Create()
$key.GenerateKey([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
$request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
    "CN=$DnsName",
    $key,
    [Security.Cryptography.HashAlgorithmName]::SHA256)
[void]$request.CertificateExtensions.Add(
    [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
[void]$request.CertificateExtensions.Add(
    [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
        [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,
        $true))
$eku = [Security.Cryptography.OidCollection]::new()
[void]$eku.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
[void]$request.CertificateExtensions.Add(
    [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($eku, $true))
$san = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
$san.AddDnsName($DnsName)
[void]$request.CertificateExtensions.Add($san.Build($true))

[IO.File]::WriteAllText($keyPath, $key.ExportPkcs8PrivateKeyPem(), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($csrPath, $request.CreateSigningRequestPem(), [Text.UTF8Encoding]::new($false))
$metadata = [ordered]@{
    schema_version = 1
    purpose = $Purpose
    dns_name = $DnsName.ToLowerInvariant()
    algorithm = 'ECDSA P-256 / SHA-256'
    requested_eku = 'serverAuth'
    created_at_utc = [DateTimeOffset]::UtcNow.ToString('O')
}
[IO.File]::WriteAllText(
    $metadataPath,
    (($metadata | ConvertTo-Json) + [Environment]::NewLine),
    [Text.UTF8Encoding]::new($false))

if (-not $IsWindows) {
    [IO.File]::SetUnixFileMode(
        $keyPath,
        [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite)
}

Write-Output "CSR_READY purpose=$Purpose dnsName=$($metadata.dns_name) algorithm=ecdsa-p256"
Write-Warning 'Submit the CSR to the approved corporate/internal CA. Keep the private key owner-readable only and never attach it to a ticket or commit it.'
