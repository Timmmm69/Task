[CmdletBinding()]
param(
    [string]$EvidenceDirectory,
    [switch]$SkipFoundation,
    [switch]$SkipCleanRoom,
    [switch]$SkipSec03Contract
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$verificationRoot = Join-Path $repositoryRoot 'work\production\verification'
if (-not $EvidenceDirectory) {
    $EvidenceDirectory = Join-Path $repositoryRoot 'work\production\evidence\ops-runbook\install'
}
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null

$releasePackage = Join-Path $repositoryRoot 'outputs\20260911_task_container_release_0.6.0'
$release = Get-Content -Raw (Join-Path $releasePackage 'release.json') | ConvertFrom-Json
if ($release.status -ne 'PASS') { throw 'Container release 0.6.0 is not PASS; the clean-room reproduction requires the verified release.' }
$imageMap = Get-Content -Raw (Join-Path $releasePackage 'image-map.json') | ConvertFrom-Json
$releaseImages = [ordered]@{
    'task-api'               = @($imageMap.'task-api', ($release.images | Where-Object target -eq 'task-api').imageDigest)
    'task-database-migrator' = @($imageMap.'task-database-migrator', ($release.images | Where-Object target -eq 'task-database-migrator').imageDigest)
    'task-worker'            = @($imageMap.'task-worker', ($release.images | Where-Object target -eq 'task-worker').imageDigest)
}
$infraImages = [ordered]@{
    'postgres:16-alpine'                      = 'sha256:57c72fd2a128e416c7fcc499958864df5301e940bca0a56f58fddf30ffc07777'
    'nginxinc/nginx-unprivileged:1.29-alpine' = 'sha256:0c79d56aee561a1d81c63f00eee5fb5fe29279560cdc55e91425133104c7fbe6'
    'docker:29-dind'                          = 'sha256:5efed980cba3fc126cf54e21a5a6ff8849d05b6e0623d6e7612f48e9cd6cd17e'
    'mcr.microsoft.com/powershell:latest'     = 'sha256:810c4f1e0c9d23022c3ec18c50a6205ee4b60766f1739d329b2948df1fd7d5b0'
    'coredns/coredns:1.14.6'                  = 'sha256:900f9c109f7a33545d3c811516e8376df9019147b750f5ce3e254468769176ea'
    'node:22-alpine'                          = 'sha256:16e22a550f3863206a3f701448c45f7912c6896a62de43add43bb9c86130c3e2'
}
$imageInventory = [Collections.Generic.List[object]]::new()

function Invoke-Gate {
    param([string]$Name, [scriptblock]$Body)
    $started = [DateTimeOffset]::UtcNow
    try {
        & $Body *>&1 | Out-Host
        return [ordered]@{ name = $Name; result = 'PASS'; startedAtUtc = $started.ToString('O'); finishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') }
    }
    catch {
        return [ordered]@{ name = $Name; result = 'FAIL'; startedAtUtc = $started.ToString('O'); finishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); error = $_.Exception.Message }
    }
}

try {
    if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7+ is required for the clean-room reproduction.' }
    $serverVersion = & docker version --format '{{.Server.Os}}/{{.Server.Arch}} {{.Server.Version}}' 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Docker engine is unavailable.' }
    if ($serverVersion -notmatch 'linux/amd64') { throw "Docker engine must be linux/amd64; found: $serverVersion" }

    $gateResults = [Collections.Generic.List[object]]::new()

    foreach ($entry in $releaseImages.GetEnumerator()) {
        $target = $entry.Key
        $indexDigest = $entry.Value[0]
        $manifestDigest = $entry.Value[1]
        $inspect = & docker image inspect $indexDigest --format '{{.Id}}' 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Verified release image is not loaded: $target @ $indexDigest" }
        $imageInventory += [ordered]@{ image = $target; releaseVersion = '0.6.0'; manifestDigest = $manifestDigest; indexDigest = $indexDigest; localId = $inspect.Trim(); verifiedBy = 'release-image-map' }
    }

    foreach ($entry in $infraImages.GetEnumerator()) {
        $imageName = $entry.Key
        $expectedDigest = $entry.Value
        $inspect = & docker image inspect $imageName --format '{{.Id}}|{{json .RepoDigests}}' 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Required pinned image is missing: $imageName" }
        $parts = $inspect -split '\|', 2
        $id = $parts[0]
        $repoDigests = if ($parts.Count -gt 1) { ($parts[1] | ConvertFrom-Json) } else { @() }
        $idMatches = $id.StartsWith("sha256:$($expectedDigest.Substring(7, 12))")
        $digestMatches = @($repoDigests | Where-Object { $_ -eq $imageName.Split(':')[0] + '@' + $expectedDigest }).Count -gt 0
        if (-not ($idMatches -or $digestMatches)) {
            throw "Image $imageName does not match the tested digest $expectedDigest (id=$id). Stop condition: never substitute an unverified image."
        }
        $imageInventory += [ordered]@{ image = $imageName; expectedDigest = $expectedDigest; id = $id; verifiedBy = if ($digestMatches) { 'RepoDigest' } else { 'IdPrefix' } }
    }

    $environment = [ordered]@{
        hostOs        = (Get-CimInstance Win32_OperatingSystem).Caption
        dockerServer  = $serverVersion
        powershell    = $PSVersionTable.PSVersion.ToString()
        gitCommit     = (git -C $repositoryRoot rev-parse HEAD).Trim()
        gitStatus     = (git -C $repositoryRoot status --porcelain)
        images        = $imageInventory
        scopeNote     = 'Synthetic clean-room install reproduction (runbook phases A-D). Customer CA, secrets manager, corporate DNS, alert webhook and real storage are not exercised and remain customer-owned inputs.'
    }

    if (-not $SkipFoundation) {
        $gateResults += Invoke-Gate 'ops02-foundation' {
            & (Join-Path $verificationRoot 'Test-Ops02Foundation.ps1') -EvidenceDirectory (Join-Path $EvidenceDirectory 'foundation')
        }
        $gateResults += Invoke-Gate 'ops02-runtime-foundation' {
            & (Join-Path $verificationRoot 'Test-Ops02RuntimeFoundation.ps1') -EvidenceDirectory (Join-Path $EvidenceDirectory 'runtime-foundation')
        }
    }

    if (-not $SkipCleanRoom) {
        $gateResults += Invoke-Gate 'ops02-clean-room' {
            & (Join-Path $verificationRoot 'Test-Ops02CleanRoom.ps1') -EvidenceDirectory (Join-Path $EvidenceDirectory 'clean-room')
        }
    }

    if (-not $SkipSec03Contract) {
        $gateResults += Invoke-Gate 'sec03-contract' {
            & (Join-Path $verificationRoot 'Test-ProductionSecretsTls.Contract.ps1') -EvidenceDirectory (Join-Path $EvidenceDirectory 'sec03-contract')
        }
    }

    $failed = @($gateResults | Where-Object result -ne 'PASS').Count
    $summary = [ordered]@{
        schemaVersion  = 1
        checkedAtUtc   = [DateTimeOffset]::UtcNow.ToString('O')
        result         = if ($failed -eq 0) { 'PASS' } else { 'FAIL' }
        syntheticOnly  = $true
        environment    = $environment
        gates          = $gateResults
         phasesCovered  = @('A: synthetic parameters and assets', 'B: deterministic networks, DNS zone, firewall plan', 'C: PostgreSQL TLS bootstrap, migration v14, runtime grants', 'D: API/Worker/TLS proxy, HTTPS readiness, HSTS, negative TLS probes, certificate rotation, port inventory', 'SEC-03 source contract')
        notCoveredHere = @('host firewall live apply (Linux-host step; verified in work/production/evidence/ops02-linux-firewall)', 'monitoring and alert delivery (step 5)', 'backup provisioning and drills (step 5)', 'desktop client release (step 5)', 'recovery incident drills (step 5)')
    }
    [IO.File]::WriteAllText((Join-Path $EvidenceDirectory 'install.json'), (($summary | ConvertTo-Json -Depth 12) + "`n"), [Text.UTF8Encoding]::new($false))

    if ($failed -eq 0) {
        Write-Output 'OPS RUNBOOK INSTALL REPRO: PASS'
    }
    else {
        throw "OPS RUNBOOK INSTALL REPRO: FAIL ($failed gate(s) failed)."
    }
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
