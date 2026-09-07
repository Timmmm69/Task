[CmdletBinding()]
param([string]$EvidenceDirectory)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$deploymentRoot = Join-Path $repositoryRoot 'work/production/deployment'
$opsRoot = Join-Path $deploymentRoot 'ops02'
$parameterFile = Join-Path $opsRoot 'ops02.parameters.example.json'
$assetGenerator = Join-Path $opsRoot 'New-Ops02CleanRoomAssets.ps1'
$firewallTool = Join-Path $opsRoot 'Set-Ops02Firewall.ps1'
$composePath = Join-Path $deploymentRoot 'security/compose.production.yaml'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('task-ops02-foundation-' + [Guid]::NewGuid().ToString('N'))
$assetRoot = Join-Path $testRoot 'assets'
$localEvidence = Join-Path $testRoot 'evidence'
$dockerConfigRoot = Join-Path $testRoot 'docker-config'
$checks = [ordered]@{}

function Assert-Ops02Test {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "OPS-02 foundation gate failed: $Message" }
}

function Test-Certificate {
    param([string]$Name, [string]$CertificatePath, [string]$KeyPath, [string]$CaPath, [string]$DnsName)
    $certificateCollection = [Security.Cryptography.X509Certificates.X509Certificate2Collection]::new()
    $certificateCollection.ImportFromPem((Get-Content -LiteralPath $CertificatePath -Raw))
    $caCollection = [Security.Cryptography.X509Certificates.X509Certificate2Collection]::new()
    $caCollection.ImportFromPem((Get-Content -LiteralPath $CaPath -Raw))
    Assert-Ops02Test ($certificateCollection.Count -eq 1 -and $caCollection.Count -eq 1) "$Name certificate bundle is malformed."
    $certificate = $certificateCollection[0]
    $ca = $caCollection[0]
    Assert-Ops02Test ($certificate.MatchesHostname($DnsName, $false, $false)) "$Name certificate SAN does not match $DnsName."
    $eku = @($certificate.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] })
    Assert-Ops02Test ($eku.Count -eq 1 -and @($eku[0].EnhancedKeyUsages | Where-Object Value -eq '1.3.6.1.5.5.7.3.1').Count -eq 1) "$Name certificate lacks serverAuth EKU."
    Assert-Ops02Test ($certificate.NotAfter.ToUniversalTime() -gt [DateTime]::UtcNow.AddDays(30)) "$Name certificate has insufficient remaining lifetime."

    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    $chain.ChainPolicy.TrustMode = [Security.Cryptography.X509Certificates.X509ChainTrustMode]::CustomRootTrust
    $chain.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
    [void]$chain.ChainPolicy.CustomTrustStore.Add($ca)
    Assert-Ops02Test ($chain.Build($certificate)) "$Name certificate does not chain to the generated test root."

    $privateKey = [Security.Cryptography.RSA]::Create()
    $privateKey.ImportFromPem((Get-Content -LiteralPath $KeyPath -Raw))
    $payload = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $signature = $privateKey.SignData($payload, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $publicKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($certificate)
    Assert-Ops02Test ($publicKey.VerifyData($payload, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) "$Name certificate and key do not match."
    $privateKey.Dispose()
    $publicKey.Dispose()
    $chain.Dispose()
    $certificate.Dispose()
    $ca.Dispose()
}

function Invoke-ComposeConfig {
    param([string]$EnvironmentFile, [string]$ComposeFile, [string]$OutputFile)
    & docker compose --env-file $EnvironmentFile -f $ComposeFile config --no-path-resolution | Out-File -LiteralPath $OutputFile -Encoding utf8
    Assert-Ops02Test ($LASTEXITCODE -eq 0) "docker compose config failed for $ComposeFile."
    Assert-Ops02Test ((Get-Item -LiteralPath $OutputFile).Length -gt 0) "docker compose config produced no output for $ComposeFile."
}

try {
    New-Item -ItemType Directory -Path $testRoot, $localEvidence, $dockerConfigRoot -Force | Out-Null
    & $assetGenerator -ParameterFile $parameterFile -OutputDirectory $assetRoot -Confirm:$false
    Assert-Ops02Test ($? -and $LASTEXITCODE -in 0, $null) 'Clean-room asset generator failed.'

    $requiredAssets = @(
        'assets-metadata.json', 'production.env.partial', 'ca/private/root-ca.key',
        'client/task-clean-room-root-ca.crt', 'secrets/edge/tls.crt', 'secrets/edge/tls.key',
        'secrets/database/postgres.crt', 'secrets/database/postgres.key', 'secrets/database/postgres-ca.pem',
        'secrets/database/postgres-admin-password', 'secrets/database/task-migration.pgpass',
        'secrets/database/task-runtime.pgpass', 'secrets/identity/signing-current.pem',
        'secrets/identity/password-pepper', 'secrets/identity/verification/signing-current.pem',
        'dns/Corefile', 'dns/compose.dns.yaml', 'dns/dns.env', 'dns/zones/db.cleanroom.test'
    )
    foreach ($relative in $requiredAssets) {
        Assert-Ops02Test (Test-Path -LiteralPath (Join-Path $assetRoot $relative) -PathType Leaf) "Generated asset is missing: $relative"
    }
    $checks.parameter_contract_and_external_secret_boundary = $true

    $insideRepositoryRejected = $false
    try {
        & $assetGenerator -ParameterFile $parameterFile -OutputDirectory (Join-Path $repositoryRoot 'work/production/evidence/forbidden-ops02-assets') -Confirm:$false 2>$null | Out-Null
    }
    catch { $insideRepositoryRejected = $_.Exception.Message -match 'outside the repository' }
    Assert-Ops02Test $insideRepositoryRejected 'Generator accepted a CA private-key destination inside the repository.'

    $invalidConfiguration = Get-Content -LiteralPath $parameterFile -Raw | ConvertFrom-Json -Depth 20
    $invalidConfiguration.dns.image = 'coredns:latest'
    $invalidPath = Join-Path $testRoot 'invalid.parameters.json'
    [IO.File]::WriteAllText($invalidPath, (($invalidConfiguration | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
    $mutableImageRejected = $false
    try { & $assetGenerator -ParameterFile $invalidPath -OutputDirectory (Join-Path $testRoot 'invalid-assets') -Confirm:$false 2>$null | Out-Null }
    catch { $mutableImageRejected = $_.Exception.Message -match 'immutable digest' }
    Assert-Ops02Test $mutableImageRejected 'Mutable DNS image reference was accepted.'
    $checks.invalid_and_mutable_parameters_rejected = $true

    Test-Certificate 'edge' (Join-Path $assetRoot 'secrets/edge/tls.crt') (Join-Path $assetRoot 'secrets/edge/tls.key') (Join-Path $assetRoot 'secrets/edge/ca-chain.pem') 'task.cleanroom.test'
    Test-Certificate 'database' (Join-Path $assetRoot 'secrets/database/postgres.crt') (Join-Path $assetRoot 'secrets/database/postgres.key') (Join-Path $assetRoot 'secrets/database/postgres-ca.pem') 'postgres'
    $checks.local_ca_chain_san_eku_key_and_lifetime = $true

    & (Join-Path $PSScriptRoot 'Test-ProductionSecretsTls.ps1') `
        -SecretRoot (Join-Path $assetRoot 'secrets') `
        -ExpectedServerName 'task.cleanroom.test' `
        -ExpectedDatabaseName 'postgres' `
        -EnvironmentFile (Join-Path $assetRoot 'production.env.partial') | Out-Null
    $checks.complete_synthetic_secret_bundle = $true
    $incompleteRoot = Join-Path $testRoot 'incomplete-secrets'
    Copy-Item -LiteralPath (Join-Path $assetRoot 'secrets') -Destination $incompleteRoot -Recurse
    Remove-Item -LiteralPath (Join-Path $incompleteRoot 'edge/tls.key') -Force
    $incompleteRejected = $false
    try {
        & (Join-Path $PSScriptRoot 'Test-ProductionSecretsTls.ps1') `
            -SecretRoot $incompleteRoot -ExpectedServerName 'task.cleanroom.test' `
            -ExpectedDatabaseName 'postgres' -EnvironmentFile (Join-Path $assetRoot 'production.env.partial') 2>$null | Out-Null
    }
    catch { $incompleteRejected = $_.Exception.Message -match 'Required secret is missing' }
    Assert-Ops02Test $incompleteRejected 'Incomplete secret bundle was accepted.'
    $checks.incomplete_secret_bundle_rejected = $true

    $zone = Get-Content -LiteralPath (Join-Path $assetRoot 'dns/zones/db.cleanroom.test') -Raw
    $corefile = Get-Content -LiteralPath (Join-Path $assetRoot 'dns/Corefile') -Raw
    Assert-Ops02Test ($zone -match '(?m)^task IN A 192\.0\.2\.10$') 'Authoritative DNS zone lacks the Task A record.'
    Assert-Ops02Test ($zone -match '(?m)^ns1 IN A 192\.0\.2\.53$') 'Authoritative DNS zone lacks the resolver A record.'
    Assert-Ops02Test ($corefile -match 'file db\.cleanroom\.test cleanroom\.test') 'CoreDNS does not load the generated authoritative zone.'
    $checks.authoritative_dns_assets = $true

    & $firewallTool -ParameterFile $parameterFile -Action Plan -EvidenceDirectory $localEvidence | Out-Null
    $firewall = Get-Content -LiteralPath (Join-Path $localEvidence 'firewall-plan.json') -Raw
    $allowIndex = $firewall.IndexOf('--ctorigdstport 443 -j RETURN', [StringComparison]::Ordinal)
    $subnetDropIndex = $firewall.IndexOf('-d 172.30.10.0/24 -j DROP', [StringComparison]::Ordinal)
    $denyIndex = $firewall.LastIndexOf('--ctorigdstport 443 -j DROP', [StringComparison]::Ordinal)
    Assert-Ops02Test ($allowIndex -ge 0 -and $subnetDropIndex -gt $allowIndex -and $denyIndex -gt $subnetDropIndex) 'Firewall plan does not allow approved HTTPS before deny rules.'
    Assert-Ops02Test ($firewall -match '--ctorigdstport 5432 -j DROP' -and $firewall -match '--ctorigdstport 8080 -j DROP') 'Firewall plan does not explicitly block PostgreSQL/API host ports.'
    Assert-Ops02Test ($firewall -match 'TASK-OPS02-IN.*198\.51\.100\.0/28.*--dport 22.*RETURN' -and $firewall -match 'TASK-OPS02-IN.*-j DROP') 'Host INPUT plan is not deny-by-default with bounded management access.'
    $checks.deny_by_default_firewall_plan = $true

    $compose = Get-Content -LiteralPath $composePath -Raw
    Assert-Ops02Test ($compose -match '(?ms)^  database:\r?\n    internal: true.*TASK_DATABASE_SUBNET') 'Database network is not internal and parameterized.'
    Assert-Ops02Test ($compose -match '(?ms)^  application-edge:\r?\n    internal: true.*TASK_APPLICATION_EDGE_SUBNET') 'Application-edge network is not internal and parameterized.'
    Assert-Ops02Test ($compose -match 'TASK_FRONTEND_SUBNET') 'Frontend network subnet is not parameterized.'
    Assert-Ops02Test (([regex]::Matches($compose, '(?m)^    ports:$')).Count -eq 1) 'Production topology publishes more than the TLS proxy.'
    $checks.deterministic_internal_docker_topology = $true

    Assert-Ops02Test ($null -ne (Get-Command docker -ErrorAction SilentlyContinue)) 'Docker CLI with Compose is required for the foundation gate.'
    $previousDockerConfig = $env:DOCKER_CONFIG
    try {
        $env:DOCKER_CONFIG = $dockerConfigRoot
        Invoke-ComposeConfig (Join-Path $assetRoot 'production.env.partial') $composePath (Join-Path $localEvidence 'production-compose.resolved.yaml')
        Invoke-ComposeConfig (Join-Path $assetRoot 'dns/dns.env') (Join-Path $assetRoot 'dns/compose.dns.yaml') (Join-Path $localEvidence 'dns-compose.resolved.yaml')
    }
    finally { $env:DOCKER_CONFIG = $previousDockerConfig }
    $checks.compose_configuration_parses = $true

    $metadata = Get-Content -LiteralPath (Join-Path $assetRoot 'assets-metadata.json') -Raw | ConvertFrom-Json
    $evidence = [ordered]@{
        schemaVersion = 1
        checkedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        result = 'PASS'
        scope = 'OPS-02 steps 1-3 source and synthetic foundation; no live Linux firewall or application deployment claim'
        checks = $checks
        certificateMetadata = $metadata
    }
    [IO.File]::WriteAllText((Join-Path $localEvidence 'checks.json'), (($evidence | ConvertTo-Json -Depth 10) + "`n"), [Text.UTF8Encoding]::new($false))

    if ($EvidenceDirectory) {
        New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
        foreach ($name in @('checks.json', 'firewall-plan.json', 'production-compose.resolved.yaml', 'dns-compose.resolved.yaml')) {
            Copy-Item -LiteralPath (Join-Path $localEvidence $name) -Destination (Join-Path $EvidenceDirectory $name) -Force
        }
    }
    Write-Output "OPS-02 foundation gate passed: $($checks.Keys -join ', ')."
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
