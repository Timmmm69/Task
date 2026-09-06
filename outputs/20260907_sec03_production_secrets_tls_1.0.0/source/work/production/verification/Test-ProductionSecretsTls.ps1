[CmdletBinding()]
param(
    [string]$SecretRoot,
    [string]$ExpectedServerName,
    [string]$ExpectedDatabaseName = 'postgres',
    [string]$Endpoint,
    [ValidateRange(1, 65535)]
    [int]$ExpectedHttpsPort = 443,
    [string]$EnvironmentFile,
    [string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
$productionRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repositoryRoot = (Resolve-Path (Join-Path $productionRoot '..\..')).Path
$deploymentRoot = Join-Path $productionRoot 'deployment\security'
$composePath = Join-Path $deploymentRoot 'compose.production.yaml'
$nginxPath = Join-Path $deploymentRoot 'nginx.conf'
$hbaPath = Join-Path $deploymentRoot 'postgresql.pg_hba.conf'
$checks = [ordered]@{}
$certificateEvidence = [ordered]@{}

function Assert-Sec03 {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw "SEC-03 production security gate failed: $Message"
    }
}

function Read-RequiredText {
    param([string]$Path)
    Assert-Sec03 (Test-Path -LiteralPath $Path -PathType Leaf) "Required file is missing: $Path"
    return [IO.File]::ReadAllText($Path)
}

function Test-SecretPermissions {
    param([string[]]$Paths)
    if ($IsWindows) {
        return
    }

    $forbidden = [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::GroupWrite -bor
        [IO.UnixFileMode]::GroupExecute -bor [IO.UnixFileMode]::OtherRead -bor
        [IO.UnixFileMode]::OtherWrite -bor [IO.UnixFileMode]::OtherExecute
    foreach ($path in $Paths) {
        $mode = [IO.File]::GetUnixFileMode($path)
        Assert-Sec03 (($mode -band $forbidden) -eq 0) "Secret file permissions are broader than owner-only: $path"
    }
}

function Get-PemCertificates {
    param([string]$Path)
    $collection = [Security.Cryptography.X509Certificates.X509Certificate2Collection]::new()
    $collection.ImportFromPem((Read-RequiredText $Path))
    Assert-Sec03 ($collection.Count -gt 0) "No certificates were found in $Path"
    return $collection
}

function Test-ServerCertificate {
    param(
        [string]$Name,
        [string]$CertificatePath,
        [string]$PrivateKeyPath,
        [string]$CaPath,
        [string]$DnsName
    )

    $leaf = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem((Read-RequiredText $CertificatePath))
    $withKey = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile($CertificatePath, $PrivateKeyPath)
    $now = [DateTimeOffset]::UtcNow
    Assert-Sec03 ($leaf.NotBefore.ToUniversalTime() -le $now.UtcDateTime) "$Name certificate is not valid yet."
    Assert-Sec03 ($leaf.NotAfter.ToUniversalTime() -gt $now.AddDays(30).UtcDateTime) "$Name certificate expires in 30 days or less."
    Assert-Sec03 ($leaf.MatchesHostname($DnsName, $false, $false)) "$Name certificate does not match DNS name '$DnsName'."

    $serverAuthOid = '1.3.6.1.5.5.7.3.1'
    $ekuExtensions = @($leaf.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] })
    Assert-Sec03 ($ekuExtensions.Count -eq 1) "$Name certificate must contain an Extended Key Usage extension."
    Assert-Sec03 (@($ekuExtensions[0].EnhancedKeyUsages | Where-Object Value -eq $serverAuthOid).Count -eq 1) "$Name certificate is not valid for TLS server authentication."

    $payload = [Text.Encoding]::UTF8.GetBytes('task-sec03-key-pair-check')
    $rsaPrivate = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($withKey)
    $ecdsaPrivate = [Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPrivateKey($withKey)
    if ($null -ne $rsaPrivate) {
        $signature = $rsaPrivate.SignData($payload, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $publicKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($leaf)
        Assert-Sec03 ($publicKey.VerifyData($payload, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) "$Name certificate and private key do not match."
    }
    elseif ($null -ne $ecdsaPrivate) {
        $signature = $ecdsaPrivate.SignData($payload, [Security.Cryptography.HashAlgorithmName]::SHA256)
        $publicKey = [Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPublicKey($leaf)
        Assert-Sec03 ($publicKey.VerifyData($payload, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256)) "$Name certificate and private key do not match."
    }
    else {
        throw "SEC-03 production security gate failed: $Name certificate must use RSA or ECDSA."
    }

    $caCertificates = Get-PemCertificates $CaPath
    $roots = @($caCertificates | Where-Object { $_.Subject -eq $_.Issuer })
    Assert-Sec03 ($roots.Count -eq 1) "$Name CA bundle must contain exactly one self-signed trust anchor."
    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    $chain.ChainPolicy.TrustMode = [Security.Cryptography.X509Certificates.X509ChainTrustMode]::CustomRootTrust
    $chain.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
    $chain.ChainPolicy.DisableCertificateDownloads = $true
    [void]$chain.ChainPolicy.CustomTrustStore.Add($roots[0])
    foreach ($certificate in $caCertificates) {
        if ($certificate.Thumbprint -ne $roots[0].Thumbprint) {
            [void]$chain.ChainPolicy.ExtraStore.Add($certificate)
        }
    }
    Assert-Sec03 ($chain.Build($leaf)) "$Name certificate does not build to the supplied trust anchor: $($chain.ChainStatus.StatusInformation -join '; ')"

    $certificateEvidence[$Name] = [ordered]@{
        dns_name = $DnsName
        thumbprint_sha256 = $leaf.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant()
        not_before_utc = $leaf.NotBefore.ToUniversalTime().ToString('O')
        not_after_utc = $leaf.NotAfter.ToUniversalTime().ToString('O')
        issuer = $leaf.Issuer
    }
}

function Test-IdentityKeyRing {
    param([string]$IdentityRoot)
    $privatePath = Join-Path $IdentityRoot 'signing-current.pem'
    $activePublicPath = Join-Path $IdentityRoot 'verification/signing-current.pem'
    Assert-Sec03 (Test-Path -LiteralPath $activePublicPath -PathType Leaf) 'The active JWT public key must be verification/signing-current.pem.'
    $privateKey = [Security.Cryptography.ECDsa]::Create()
    $publicKey = [Security.Cryptography.ECDsa]::Create()
    try {
        $privateKey.ImportFromPem((Read-RequiredText $privatePath))
        $publicKey.ImportFromPem((Read-RequiredText $activePublicPath))
        Assert-Sec03 ($privateKey.KeySize -eq 256 -and $publicKey.KeySize -eq 256) 'JWT signing keys must use ECDSA P-256.'
        $payload = [Text.Encoding]::UTF8.GetBytes('task-sec03-jwt-key-pair-check')
        $signature = $privateKey.SignData($payload, [Security.Cryptography.HashAlgorithmName]::SHA256)
        Assert-Sec03 ($publicKey.VerifyData($payload, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256)) 'Active JWT private/public keys do not match.'
    }
    finally {
        $privateKey.Dispose()
        $publicKey.Dispose()
    }

    foreach ($publicPath in Get-ChildItem -LiteralPath (Join-Path $IdentityRoot 'verification') -File -Filter '*.pem') {
        $publicText = Read-RequiredText $publicPath.FullName
        Assert-Sec03 ($publicText -notmatch 'PRIVATE KEY') "JWT verification ring contains private material: $($publicPath.Name)"
    }
}

$compose = Read-RequiredText $composePath
$nginx = Read-RequiredText $nginxPath
$hba = Read-RequiredText $hbaPath
$envExample = Read-RequiredText (Join-Path $deploymentRoot 'production.env.example')

Assert-Sec03 ($compose -notmatch '(?i)(?:Password|Pepper|SigningKey)\s*=') 'Compose contains an inline credential assignment.'
Assert-Sec03 ($compose -notmatch '(?i)Password=') 'A database password is embedded in a connection string.'
Assert-Sec03 (([regex]::Matches($compose, 'Passfile=/run/task-secrets/')).Count -eq 3) 'API, worker and migrator must use mounted PostgreSQL passfiles.'
Assert-Sec03 (([regex]::Matches($compose, 'SSL Mode=VerifyFull')).Count -eq 3) 'Every application PostgreSQL connection must verify TLS and host name.'
Assert-Sec03 (([regex]::Matches($compose, 'Root Certificate=/run/task-secrets/postgres-ca\.pem')).Count -eq 3) 'Every application PostgreSQL connection must pin the approved CA bundle.'
Assert-Sec03 ($compose -match '(?ms)^  postgres:.*?^    networks:\r?\n      - database\r?$') 'PostgreSQL must be isolated on the database network.'
$postgresBlock = [regex]::Match($compose, '(?ms)^  postgres:\r?\n(?<body>.*?)(?=^  task-database-migrator:)')
Assert-Sec03 ($postgresBlock.Success -and $postgresBlock.Groups['body'].Value -notmatch '(?m)^\s+ports:') 'PostgreSQL must not publish a host port.'
$apiBlock = [regex]::Match($compose, '(?ms)^  task-api:\r?\n(?<body>.*?)(?=^  task-worker:)')
Assert-Sec03 ($apiBlock.Success -and $apiBlock.Groups['body'].Value -notmatch '(?m)^\s+ports:') 'The HTTP API must not publish a host port.'
Assert-Sec03 (([regex]::Matches($compose, '(?m)^\s+ports:$')).Count -eq 1) 'Only the TLS proxy may publish a host port.'
Assert-Sec03 ($compose -match '\$\{TASK_HTTPS_BIND_IP:\?TASK_HTTPS_BIND_IP is required\}:\$\{TASK_HTTPS_PORT:-443\}:8443') 'The TLS listener must require an explicit host bind address.'
Assert-Sec03 ($compose -match '(?ms)^  database:\r?\n    internal: true' -and $compose -match '(?ms)^  application-edge:\r?\n    internal: true') 'Database and clear-text application-edge networks must be internal.'
Assert-Sec03 (([regex]::Matches($compose, '(?m)^    read_only: true$')).Count -ge 4) 'All application/proxy containers must have a read-only root filesystem.'
Assert-Sec03 (([regex]::Matches($compose, 'cap_drop: \["ALL"\]')).Count -ge 4) 'All application/proxy containers must drop Linux capabilities.'
Assert-Sec03 (([regex]::Matches($compose, 'no-new-privileges:true')).Count -ge 4) 'All application/proxy containers must deny privilege escalation.'
Assert-Sec03 ($compose -notmatch '(?i)SSL Mode=(?:Disable|Allow|Prefer|Require)(?:;|\r?$)') 'Production database connections allow unverified TLS.'
$checks.compose_secret_and_network_contract = $true

Assert-Sec03 ($nginx -match 'ssl_protocols TLSv1\.2 TLSv1\.3;') 'TLS proxy must permit only TLS 1.2 and TLS 1.3.'
Assert-Sec03 ($nginx -match 'ssl_session_tickets off;') 'TLS session tickets must be disabled unless their keys are explicitly managed.'
Assert-Sec03 ($nginx -match 'Strict-Transport-Security') 'HSTS is missing.'
Assert-Sec03 ($nginx -match 'proxy_pass http://task_api;' -and $nginx -notmatch '(?m)^\s*listen\s+80') 'Proxy must terminate TLS without a plaintext listener.'
Assert-Sec03 ($nginx -match 'log_format task_safe[^;]*\$uri' -and $nginx -notmatch 'log_format task_safe[^;]*\$request_uri') 'Access logs must exclude query strings.'
$checks.edge_tls_policy = $true

Assert-Sec03 ($compose -match 'ssl=on' -and $compose -match 'ssl_min_protocol_version=TLSv1\.2') 'PostgreSQL TLS is not mandatory at server startup.'
Assert-Sec03 ($hba -match '(?m)^hostnossl\s+all\s+all\s+0\.0\.0\.0/0\s+reject$') 'IPv4 plaintext PostgreSQL traffic is not rejected.'
Assert-Sec03 ($hba -match '(?m)^hostnossl\s+all\s+all\s+::/0\s+reject$') 'IPv6 plaintext PostgreSQL traffic is not rejected.'
Assert-Sec03 (([regex]::Matches($hba, '(?m)^hostssl\s+all\s+all\s+.*\s+scram-sha-256$')).Count -eq 2) 'TLS PostgreSQL traffic must use SCRAM-SHA-256.'
$checks.database_tls_policy = $true

Assert-Sec03 ($envExample -notmatch '(?i)(password|pepper|private|secret)\s*=') 'The environment template must contain non-secret values only.'
Assert-Sec03 (([regex]::Matches($envExample, '(?m)^[A-Z0-9_]+_IMAGE=.*@sha256:<64-hex-digest>$')).Count -eq 5) 'Every production image example must use an immutable digest.'
$scannableProductionFiles = @(Get-ChildItem -LiteralPath $productionRoot -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/](?:bin|obj|evidence)[\\/]' -and
    ($_.Extension -in '.cs', '.csproj', '.json', '.md', '.ps1', '.py', '.sh', '.txt', '.xml', '.yaml', '.yml', '.conf', '.example' -or
        $_.Name -eq 'Dockerfile')
})
$trackedPrivateKeys = @(Select-String -LiteralPath $scannableProductionFiles.FullName -Pattern '-----BEGIN (?:EC |RSA )?PRIVATE KEY-----')
Assert-Sec03 ($trackedPrivateKeys.Count -eq 0) 'Tracked production source contains private key material.'
$checks.repository_secret_boundary = $true

if (-not [string]::IsNullOrWhiteSpace($EnvironmentFile)) {
    $environmentText = Read-RequiredText (Resolve-Path -LiteralPath $EnvironmentFile).Path
    $sensitiveEnvironmentNames = @([regex]::Matches($environmentText, '(?im)^(?<name>[^#=]*(?:PASSWORD|PEPPER|PRIVATE|TOKEN|SECRET)[^=]*)=') |
        Where-Object { $_.Groups['name'].Value.Trim() -ne 'TASK_SECRET_ROOT' })
    Assert-Sec03 ($sensitiveEnvironmentNames.Count -eq 0) 'Deployment environment file contains a secret-like variable.'
    $imageLines = @([regex]::Matches($environmentText, '(?m)^(?<name>[A-Z0-9_]+_IMAGE)=(?<value>[^\r\n]+)$'))
    Assert-Sec03 ($imageLines.Count -eq 5) 'Deployment environment file must define exactly five image references.'
    foreach ($match in $imageLines) {
        Assert-Sec03 ($match.Groups['value'].Value -match '@sha256:[a-f0-9]{64}$') "Image $($match.Groups['name'].Value) is not pinned by digest."
    }
    $checks.immutable_deployment_environment = $true
}

if (-not [string]::IsNullOrWhiteSpace($SecretRoot)) {
    Assert-Sec03 (-not [string]::IsNullOrWhiteSpace($ExpectedServerName)) 'ExpectedServerName is required with SecretRoot.'
    $resolvedSecretRoot = (Resolve-Path -LiteralPath $SecretRoot).Path
    $repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    Assert-Sec03 (-not ($resolvedSecretRoot + [IO.Path]::DirectorySeparatorChar).StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) 'SecretRoot must be outside the repository.'

    $requiredSecretFiles = @(
        'database/postgres-admin-password',
        'database/task-migration.pgpass',
        'database/task-runtime.pgpass',
        'database/postgres-ca.pem',
        'database/postgres.crt',
        'database/postgres.key',
        'edge/tls.crt',
        'edge/tls.key',
        'edge/ca-chain.pem',
        'identity/signing-current.pem',
        'identity/password-pepper'
    ) | ForEach-Object { Join-Path $resolvedSecretRoot $_ }
    foreach ($file in $requiredSecretFiles) {
        Assert-Sec03 (Test-Path -LiteralPath $file -PathType Leaf) "Required secret is missing: $file"
        Assert-Sec03 ((Get-Item -LiteralPath $file).Length -gt 0) "Required secret is empty: $file"
    }
    $verificationKeys = @(Get-ChildItem -LiteralPath (Join-Path $resolvedSecretRoot 'identity/verification') -File -Filter '*.pem')
    Assert-Sec03 ($verificationKeys.Count -in 1, 2) 'Identity verification key ring must contain one or two public PEM files.'

    $adminPassword = (Read-RequiredText (Join-Path $resolvedSecretRoot 'database/postgres-admin-password')).TrimEnd("`r", "`n")
    Assert-Sec03 ($adminPassword.Length -ge 32) 'PostgreSQL administrator password must contain at least 32 characters.'
    foreach ($passfileName in @('task-migration.pgpass', 'task-runtime.pgpass')) {
        $passfile = (Read-RequiredText (Join-Path $resolvedSecretRoot "database/$passfileName")).TrimEnd("`r", "`n")
        Assert-Sec03 ($passfile -notmatch '[\r\n]') "$passfileName must contain exactly one credential record."
        $parts = @($passfile -split ':', 5)
        Assert-Sec03 ($parts.Count -eq 5 -and $parts[0] -eq $ExpectedDatabaseName -and $parts[1] -eq '5432' -and -not [string]::IsNullOrWhiteSpace($parts[2]) -and $parts[4].Length -ge 32) "$passfileName has an invalid host/port/database/password contract."
        $expectedUser = if ($passfileName -eq 'task-migration.pgpass') { 'task_migration' } else { 'task_runtime' }
        Assert-Sec03 ($parts[3] -eq $expectedUser) "$passfileName contains the wrong database role."
    }
    $pepper = (Read-RequiredText (Join-Path $resolvedSecretRoot 'identity/password-pepper')).Trim()
    Assert-Sec03 ($pepper.Length -ge 32) 'Password pepper must contain at least 32 non-whitespace characters.'

    Test-SecretPermissions @($requiredSecretFiles | Where-Object { $_ -match '(?:password|\.pgpass|\.key$|signing-current|password-pepper)' })
    Test-ServerCertificate 'edge' (Join-Path $resolvedSecretRoot 'edge/tls.crt') (Join-Path $resolvedSecretRoot 'edge/tls.key') (Join-Path $resolvedSecretRoot 'edge/ca-chain.pem') $ExpectedServerName
    Test-ServerCertificate 'database' (Join-Path $resolvedSecretRoot 'database/postgres.crt') (Join-Path $resolvedSecretRoot 'database/postgres.key') (Join-Path $resolvedSecretRoot 'database/postgres-ca.pem') $ExpectedDatabaseName
    Test-IdentityKeyRing (Join-Path $resolvedSecretRoot 'identity')
    $checks.external_secret_bundle = $true
    $checks.certificate_chain_hostname_key_and_lifetime = $true
}

if (-not [string]::IsNullOrWhiteSpace($Endpoint)) {
    $uri = [Uri]$Endpoint
    Assert-Sec03 ($uri.Scheme -eq 'https' -and $uri.Port -eq $ExpectedHttpsPort) 'Endpoint must be an HTTPS URL on the approved port.'
    $response = Invoke-WebRequest -Uri ([Uri]::new($uri, '/health/ready')) -Method Get -MaximumRedirection 0 -SkipHttpErrorCheck
    Assert-Sec03 ($response.StatusCode -eq 200) 'Live readiness endpoint did not return HTTP 200 over trusted TLS.'
    Assert-Sec03 ($response.Headers['Strict-Transport-Security'] -match 'max-age=31536000') 'Live TLS endpoint did not return the required HSTS policy.'
    $checks.live_trusted_tls_readiness = $true
}

if (-not [string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
    $evidence = [ordered]@{
        schema_version = 1
        checked_at_utc = [DateTimeOffset]::UtcNow.ToString('O')
        checks = $checks
        certificates = $certificateEvidence
    }
    [IO.File]::WriteAllText(
        (Join-Path $EvidenceDirectory 'checks.json'),
        (($evidence | ConvertTo-Json -Depth 8) + [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false))
}

Write-Output "SEC-03 production secrets/TLS gate passed: $($checks.Keys -join ', ')."
