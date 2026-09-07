[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory)][string]$ParameterFile,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Ops02.Foundation.psm1') -Force

function Write-Utf8File {
    param([string]$Path, [string]$Value)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false))
}

function New-TestCa {
    param([string]$CommonName)
    $key = [Security.Cryptography.RSA]::Create(3072)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        "CN=$CommonName", $key, [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    [void]$request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $true, 0, $true))
    [void]$request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
            [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign -bor
            [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::CrlSign, $true))
    [void]$request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($request.PublicKey, $false))
    $certificate = $request.CreateSelfSigned(
        [DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddYears(2))
    return @{ Key = $key; Certificate = $certificate }
}

function New-TestServerCertificate {
    param($Ca, [string]$DnsName, [int]$ValidityDays)
    $key = [Security.Cryptography.RSA]::Create(2048)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        "CN=$DnsName", $key, [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    [void]$request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    [void]$request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
            [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature -bor
            [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment, $true))
    $eku = [Security.Cryptography.OidCollection]::new()
    [void]$eku.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
    [void]$request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($eku, $true))
    $san = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    $san.AddDnsName($DnsName)
    [void]$request.CertificateExtensions.Add($san.Build($true))
    [void]$request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($request.PublicKey, $false))
    $serial = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
    $certificate = $request.Create(
        $Ca.Certificate, [DateTimeOffset]::UtcNow.AddMinutes(-5),
        [DateTimeOffset]::UtcNow.AddDays($ValidityDays), $serial)
    return @{ Key = $key; Certificate = $certificate }
}

function Protect-GeneratedAssets {
    param([string]$Root)
    if ($IsWindows) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $acl = [Security.AccessControl.DirectorySecurity]::new()
        $acl.SetAccessRuleProtection($true, $false)
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $identity.User,
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit,
            [Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
        Set-Acl -LiteralPath $Root -AclObject $acl
        return
    }

    [IO.File]::SetUnixFileMode($Root, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute)
    Get-ChildItem -LiteralPath $Root -Recurse -Directory | ForEach-Object {
        [IO.File]::SetUnixFileMode($_.FullName, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute)
    }
    Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object {
        $mode = if ($_.Name -match '(?:\.key$|root-ca\.key$)') {
            [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite
        }
        else {
            [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor
            [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::OtherRead
        }
        [IO.File]::SetUnixFileMode($_.FullName, $mode)
    }
}

$configuration = Get-Ops02Configuration -Path $ParameterFile
$outputPath = Assert-Ops02OutputOutsideRepository -Path $OutputDirectory
if (Test-Path -LiteralPath $outputPath) {
    $children = @(Get-ChildItem -LiteralPath $outputPath -Force)
    if ($children.Count -gt 0 -and -not $Force) {
        throw "Refusing to overwrite non-empty clean-room directory: $outputPath"
    }
    if ($children.Count -gt 0 -and $Force -and $PSCmdlet.ShouldProcess($outputPath, 'remove existing generated clean-room assets')) {
        Remove-Item -LiteralPath $outputPath -Recurse -Force
    }
}
if (-not $PSCmdlet.ShouldProcess($outputPath, 'generate local test CA, leaf certificates and authoritative DNS assets')) { return }

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$ca = New-TestCa -CommonName ([string]$configuration.testPki.caCommonName)
$edge = New-TestServerCertificate -Ca $ca -DnsName ([string]$configuration.serverDnsName) -ValidityDays ([int]$configuration.testPki.leafValidityDays)
$database = New-TestServerCertificate -Ca $ca -DnsName ([string]$configuration.databaseDnsName) -ValidityDays ([int]$configuration.testPki.leafValidityDays)

Write-Utf8File (Join-Path $outputPath 'ca/private/root-ca.key') $ca.Key.ExportPkcs8PrivateKeyPem()
Write-Utf8File (Join-Path $outputPath 'ca/task-clean-room-root-ca.crt') $ca.Certificate.ExportCertificatePem()
Write-Utf8File (Join-Path $outputPath 'secrets/edge/tls.crt') $edge.Certificate.ExportCertificatePem()
Write-Utf8File (Join-Path $outputPath 'secrets/edge/tls.key') $edge.Key.ExportPkcs8PrivateKeyPem()
Write-Utf8File (Join-Path $outputPath 'secrets/edge/ca-chain.pem') $ca.Certificate.ExportCertificatePem()
Write-Utf8File (Join-Path $outputPath 'secrets/database/postgres.crt') $database.Certificate.ExportCertificatePem()
Write-Utf8File (Join-Path $outputPath 'secrets/database/postgres.key') $database.Key.ExportPkcs8PrivateKeyPem()
Write-Utf8File (Join-Path $outputPath 'secrets/database/postgres-ca.pem') $ca.Certificate.ExportCertificatePem()
Write-Utf8File (Join-Path $outputPath 'client/task-clean-room-root-ca.crt') $ca.Certificate.ExportCertificatePem()
$adminPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
$migrationPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
$runtimePassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
$passwordPepper = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
Write-Utf8File (Join-Path $outputPath 'secrets/database/postgres-admin-password') ($adminPassword + "`n")
Write-Utf8File (Join-Path $outputPath 'secrets/database/task-migration.pgpass') ("$($configuration.databaseDnsName):5432:$($configuration.deployment.databaseName):task_migration:$migrationPassword`n")
Write-Utf8File (Join-Path $outputPath 'secrets/database/task-runtime.pgpass') ("$($configuration.databaseDnsName):5432:$($configuration.deployment.databaseName):task_runtime:$runtimePassword`n")
$identityKey = [Security.Cryptography.ECDsa]::Create()
$identityKey.GenerateKey([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
Write-Utf8File (Join-Path $outputPath 'secrets/identity/signing-current.pem') $identityKey.ExportPkcs8PrivateKeyPem()
Write-Utf8File (Join-Path $outputPath 'secrets/identity/password-pepper') ($passwordPepper + "`n")
Write-Utf8File (Join-Path $outputPath 'secrets/identity/verification/signing-current.pem') $identityKey.ExportSubjectPublicKeyInfoPem()

$zoneName = ([string]$configuration.dns.zoneName).TrimEnd('.').ToLowerInvariant()
$serverName = ([string]$configuration.serverDnsName).TrimEnd('.').ToLowerInvariant()
$serverOwner = $serverName.Substring(0, $serverName.Length - $zoneName.Length - 1)
$serial = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd') + '01'
$zoneLines = @(
    "`$ORIGIN $zoneName.",
    "`$TTL $([int]$configuration.dns.ttlSeconds)",
    "@ IN SOA ns1.$zoneName. hostmaster.$zoneName. (",
    "  $serial 300 120 604800 60 )",
    "@ IN NS ns1.$zoneName.",
    "ns1 IN A $($configuration.dns.bindAddress)",
    "$serverOwner IN A $($configuration.serverAddress)"
)
$databaseName = ([string]$configuration.databaseDnsName).TrimEnd('.').ToLowerInvariant()
if ($databaseName.EndsWith(".$zoneName", [StringComparison]::Ordinal) -and $databaseName -ne $zoneName) {
    $databaseOwner = $databaseName.Substring(0, $databaseName.Length - $zoneName.Length - 1)
    $zoneLines += "$databaseOwner IN A $($configuration.serverAddress)"
}
Write-Utf8File (Join-Path $outputPath "dns/zones/db.$zoneName") (($zoneLines -join "`n") + "`n")
$corefile = @"
${zoneName}:53 {
    errors
    log
    root /etc/coredns/zones
    file db.$zoneName $zoneName
}
"@
Write-Utf8File (Join-Path $outputPath 'dns/Corefile') $corefile
$dnsCompose = @'
name: task-ops02-dns

services:
  authoritative-dns:
    image: ${OPS02_DNS_IMAGE:?OPS02_DNS_IMAGE must be an immutable CoreDNS digest reference}
    command: ["-conf", "/etc/coredns/Corefile"]
    ports:
      - "${OPS02_DNS_BIND_IP:?OPS02_DNS_BIND_IP is required}:53:53/udp"
      - "${OPS02_DNS_BIND_IP:?OPS02_DNS_BIND_IP is required}:53:53/tcp"
    volumes:
      - type: bind
        source: ${OPS02_DNS_ASSET_ROOT:?OPS02_DNS_ASSET_ROOT is required}
        target: /etc/coredns
        read_only: true
    read_only: true
    cap_drop: ["ALL"]
    cap_add: ["NET_BIND_SERVICE"]
    security_opt: ["no-new-privileges:true"]
    restart: unless-stopped
'@
Write-Utf8File (Join-Path $outputPath 'dns/compose.dns.yaml') ($dnsCompose + "`n")
$dnsEnv = @(
    "OPS02_DNS_IMAGE=$($configuration.dns.image)",
    "OPS02_DNS_BIND_IP=$($configuration.dns.bindAddress)",
    "OPS02_DNS_ASSET_ROOT=$((Join-Path $outputPath 'dns').Replace('\', '/'))"
) -join "`n"
Write-Utf8File (Join-Path $outputPath 'dns/dns.env') ($dnsEnv + "`n")

$productionEnvironment = @(
    "TASK_API_IMAGE=$($configuration.deployment.images.api)",
    "TASK_WORKER_IMAGE=$($configuration.deployment.images.worker)",
    "TASK_DATABASE_MIGRATOR_IMAGE=$($configuration.deployment.images.migrator)",
    "TASK_TLS_PROXY_IMAGE=$($configuration.deployment.images.tlsProxy)",
    "POSTGRES_IMAGE=$($configuration.deployment.images.postgres)",
    "TASK_DB_NAME=$($configuration.deployment.databaseName)",
    "TASK_SERVER_NAME=$($configuration.serverDnsName)",
    "TASK_HTTPS_BIND_IP=$($configuration.httpsBindAddress)",
    "TASK_HTTPS_PORT=$($configuration.httpsPort)",
    "TASK_SECRET_ROOT=$($configuration.deployment.secretRoot)",
    "TASK_DATABASE_SUBNET=$($configuration.dockerNetworks.databaseSubnet)",
    "TASK_APPLICATION_EDGE_SUBNET=$($configuration.dockerNetworks.applicationEdgeSubnet)",
    "TASK_FRONTEND_SUBNET=$($configuration.dockerNetworks.frontendSubnet)"
) -join "`n"
Write-Utf8File (Join-Path $outputPath 'production.env.partial') ($productionEnvironment + "`n")

$metadata = [ordered]@{
    schemaVersion = 1
    purpose = 'OPS-02 synthetic clean-room assets; never customer production PKI'
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    serverDnsName = [string]$configuration.serverDnsName
    serverAddress = [string]$configuration.serverAddress
    databaseDnsName = [string]$configuration.databaseDnsName
    dnsZone = $zoneName
    ca = [ordered]@{ thumbprintSha256 = $ca.Certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256); notAfterUtc = $ca.Certificate.NotAfter.ToUniversalTime().ToString('O') }
    edge = [ordered]@{ thumbprintSha256 = $edge.Certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256); notAfterUtc = $edge.Certificate.NotAfter.ToUniversalTime().ToString('O') }
    database = [ordered]@{ thumbprintSha256 = $database.Certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256); notAfterUtc = $database.Certificate.NotAfter.ToUniversalTime().ToString('O') }
}
Write-Utf8File (Join-Path $outputPath 'assets-metadata.json') (($metadata | ConvertTo-Json -Depth 8) + "`n")
Protect-GeneratedAssets -Root $outputPath

Write-Output "OPS02_ASSETS_READY server=$($configuration.serverDnsName) dnsZone=$zoneName testPkiOnly=true"
Write-Warning 'The generated root CA and its private key are synthetic clean-room material. Never deploy them to a customer or copy the CA private key into the Task secret bundle.'
