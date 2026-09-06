[CmdletBinding()]
param([string]$EvidenceDirectory)

$ErrorActionPreference = 'Stop'
$gate = Join-Path $PSScriptRoot 'Test-ProductionSecretsTls.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("task-sec03-" + [Guid]::NewGuid().ToString('N'))

function Write-Utf8File {
    param([string]$Path, [string]$Value)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false))
}

function New-TestCa {
    $key = [Security.Cryptography.RSA]::Create(2048)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=Task SEC-03 ephemeral test CA',
        $key,
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    [void]$request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $false, 0, $true))
    [void]$request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
            [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign -bor
            [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::CrlSign,
            $true))
    [void]$request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($request.PublicKey, $false))
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddYears(1))
    return @{ Key = $key; Certificate = $certificate }
}

function New-TestServerCertificate {
    param($Ca, [string]$DnsName)
    $key = [Security.Cryptography.RSA]::Create(2048)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        "CN=$DnsName",
        $key,
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    [void]$request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    [void]$request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
            [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature -bor
            [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment,
            $true))
    $eku = [Security.Cryptography.OidCollection]::new()
    [void]$eku.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
    $request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($eku, $true))
    $san = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    $san.AddDnsName($DnsName)
    [void]$request.CertificateExtensions.Add($san.Build($true))
    [void]$request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($request.PublicKey, $false))
    $serial = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
    $certificate = $request.Create(
        $Ca.Certificate,
        [DateTimeOffset]::UtcNow.AddMinutes(-5),
        [DateTimeOffset]::UtcNow.AddDays(60),
        $serial)
    return @{ Key = $key; Certificate = $certificate }
}

function Protect-TestSecretsOnUnix {
    param([string]$Root)
    if ($IsWindows) { return }
    $mode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite
    Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object {
        if ($_.Name -match '(?:password|\.pgpass|\.key$|signing-current|password-pepper)') {
            [IO.File]::SetUnixFileMode($_.FullName, $mode)
        }
    }
}

try {
    $edgeName = 'task.company.internal'
    $databaseName = 'postgres'
    $ca = New-TestCa
    $edge = New-TestServerCertificate $ca $edgeName
    $database = New-TestServerCertificate $ca $databaseName
    $identityKey = [Security.Cryptography.ECDsa]::Create()
    $identityKey.GenerateKey([Security.Cryptography.ECCurve+NamedCurves]::nistP256)

    Write-Utf8File (Join-Path $testRoot 'edge/tls.crt') $edge.Certificate.ExportCertificatePem()
    Write-Utf8File (Join-Path $testRoot 'edge/tls.key') $edge.Key.ExportPkcs8PrivateKeyPem()
    Write-Utf8File (Join-Path $testRoot 'edge/ca-chain.pem') $ca.Certificate.ExportCertificatePem()
    Write-Utf8File (Join-Path $testRoot 'database/postgres.crt') $database.Certificate.ExportCertificatePem()
    Write-Utf8File (Join-Path $testRoot 'database/postgres.key') $database.Key.ExportPkcs8PrivateKeyPem()
    Write-Utf8File (Join-Path $testRoot 'database/postgres-ca.pem') $ca.Certificate.ExportCertificatePem()
    Write-Utf8File (Join-Path $testRoot 'database/postgres-admin-password') ('a' * 48)
    Write-Utf8File (Join-Path $testRoot 'database/task-migration.pgpass') ('postgres:5432:task:task_migration:' + ('m' * 48))
    Write-Utf8File (Join-Path $testRoot 'database/task-runtime.pgpass') ('postgres:5432:task:task_runtime:' + ('r' * 48))
    Write-Utf8File (Join-Path $testRoot 'identity/signing-current.pem') $identityKey.ExportPkcs8PrivateKeyPem()
    Write-Utf8File (Join-Path $testRoot 'identity/password-pepper') ('p' * 48)
    Write-Utf8File (Join-Path $testRoot 'identity/verification/signing-current.pem') $identityKey.ExportSubjectPublicKeyInfoPem()
    $digest = 'sha256:' + ('a' * 64)
    $environmentPath = Join-Path $testRoot 'production.env'
    Write-Utf8File $environmentPath (@"
TASK_API_IMAGE=example/task-api@$digest
TASK_WORKER_IMAGE=example/task-worker@$digest
TASK_DATABASE_MIGRATOR_IMAGE=example/task-migrator@$digest
TASK_TLS_PROXY_IMAGE=example/nginx@$digest
POSTGRES_IMAGE=example/postgres@$digest
TASK_DB_NAME=task
TASK_SERVER_NAME=$edgeName
TASK_HTTPS_BIND_IP=10.20.30.40
TASK_HTTPS_PORT=443
TASK_SECRET_ROOT=$testRoot
"@)
    Protect-TestSecretsOnUnix $testRoot

    & $gate -SecretRoot $testRoot -ExpectedServerName $edgeName -ExpectedDatabaseName $databaseName -EnvironmentFile $environmentPath -EvidenceDirectory $EvidenceDirectory

    $wrongNameRejected = $false
    try {
        & $gate -SecretRoot $testRoot -ExpectedServerName 'attacker.invalid' -ExpectedDatabaseName $databaseName | Out-Null
    }
    catch {
        $wrongNameRejected = $_.Exception.Message -match 'does not match DNS name'
    }
    if (-not $wrongNameRejected) {
        throw 'SEC-03 contract test failed: a certificate for the wrong DNS name was accepted.'
    }

    Write-Output 'SEC-03 contract tests passed: valid external bundle accepted; DNS mismatch rejected.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        $resolved = (Resolve-Path -LiteralPath $testRoot).Path
        $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (($resolved + [IO.Path]::DirectorySeparatorChar).StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
