[CmdletBinding()]
param(
    [string]$EvidenceDirectory,
    [switch]$SkipMonitoring,
    [switch]$SkipBackup,
    [switch]$SkipRotation,
    [switch]$SkipClient
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$verificationRoot = Join-Path $repositoryRoot 'work\production\verification'
$productionRoot = Join-Path $repositoryRoot 'work\production'
if (-not $EvidenceDirectory) {
    $EvidenceDirectory = Join-Path $repositoryRoot 'work\production\evidence\ops-runbook\operate'
}
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null

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
    if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7+ is required for the operate/recover reproduction.' }
    $serverVersion = & docker version --format '{{.Server.Os}}/{{.Server.Arch}} {{.Server.Version}}' 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Docker engine is unavailable.' }
    if ($serverVersion -notmatch 'linux/amd64') { throw "Docker engine must be linux/amd64; found: $serverVersion" }
    $python = (Get-Command python).Source
    & python -c 'import yaml' 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'PyYAML is required for the OPS-04 acceptance gate.' }

    $backupOperatorImage = 'task-backup-ops:1.0.0'
    $backupApiImage = 'task-api:ops03-1.0.0'
    $backupMigratorImage = 'task-database-migrator:ops03-1.0.0'
    $imageInventory = [Collections.Generic.List[object]]::new()

    $operatorScripts = [ordered]@{
        'acceptance.py' = '/opt/task-backup/acceptance.py'
        'runner.py'     = '/opt/task-backup/runner.py'
        'entrypoint.py' = '/opt/task-backup/entrypoint.py'
    }
    foreach ($scriptName in $operatorScripts.Keys) {
        $expected = (Get-FileHash -LiteralPath (Join-Path $productionRoot "deployment\backup\$scriptName") -Algorithm SHA256).Hash.ToLowerInvariant()
        $inside = & docker run --rm --entrypoint sh $backupOperatorImage -c "sha256sum $($operatorScripts[$scriptName])" 2>&1
        if ($LASTEXITCODE -ne 0 -or $inside -notmatch $expected) {
            throw "Operator image $backupOperatorImage does not carry the verified $scriptName. Stop condition: never run an unverified backup operator image."
        }
    }
    $requiredImages = [ordered]@{
        'task-backup-ops:1.0.0'            = 'verified by embedded script sha256 (matches work/production/deployment/backup)'
        'task-api:ops03-1.0.0'             = 'OPS-03 1.0.0 fixture image (clean-room synthetic only)'
        'task-database-migrator:ops03-1.0.0' = 'OPS-03 1.0.0 fixture image (clean-room synthetic only)'
    }
    foreach ($entry in $requiredImages.GetEnumerator()) {
        $inspect = & docker image inspect $entry.Key --format '{{.Id}}|{{.Created}}' 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Required image is missing: $($entry.Key)" }
        $parts = $inspect -split '\|', 2
        $imageInventory += [ordered]@{ image = $entry.Key; localId = $parts[0]; createdAt = $parts[1]; note = $entry.Value }
    }

    $environment = [ordered]@{
        hostOs        = (Get-CimInstance Win32_OperatingSystem).Caption
        dockerServer  = $serverVersion
        powershell    = $PSVersionTable.PSVersion.ToString()
        python        = & python --version 2>&1
        gitCommit     = (git -C $repositoryRoot rev-parse HEAD).Trim()
        gitStatus     = (git -C $repositoryRoot status --porcelain)
        images        = $imageInventory
        scopeNote     = 'Synthetic operate/recover reproduction of the OPS-operations-runbook phases E-G and the incident drill (section 5). Customer webhook, corporate CA, real storage devices, corporate code-signing chain and customer workloads are not exercised and remain customer-owned inputs.'
    }

    $gateResults = [Collections.Generic.List[object]]::new()

    if (-not $SkipMonitoring) {
        $gateResults += Invoke-Gate 'ops04-monitoring-alert-delivery' {
            & $python (Join-Path $verificationRoot 'ops04-acceptance.py') --output (Join-Path $EvidenceDirectory 'monitoring\ops04-acceptance.json')
        }
    }

    if (-not $SkipBackup) {
        $gateResults += Invoke-Gate 'backup-pitr-restore-and-recovery-drills' {
            & (Join-Path $verificationRoot 'Test-BackupRestore.ps1') -SkipBuild `
                -OutputDirectory 'work/production/evidence/ops-runbook/operate/backup-restore'
        }
    }

    if (-not $SkipRotation) {
        $gateResults += Invoke-Gate 'ops02-maintenance-rerun-certificate-rotation' {
            & (Join-Path $verificationRoot 'Test-Ops02CleanRoom.ps1') -EvidenceDirectory (Join-Path $EvidenceDirectory 'rotation')
        }
    }

    if (-not $SkipClient) {
        $gateResults += Invoke-Gate 'ops05-client-update-rollback' {
            & (Join-Path $verificationRoot 'Test-Ops05WindowsRelease.ps1') -EvidenceDirectory (Join-Path $EvidenceDirectory 'client')
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
        phasesCovered  = @(
            'E: monitoring config coverage (19 alerts, 10 scrape jobs) and synthetic firing alert delivery in production mode with token auth, secret redaction and receipts',
            'F: backup provisioning, encrypted base/full backup, PITR target, three independent restorations (readonly-secondary, protected-snapshot, escrow-b) with schema and fixture verification',
            'Maintenance: post-change OPS-02 re-validation with edge certificate rotation, wrong-SAN rotation rejection and rollback to the trusted leaf',
            'G: signed client release build, initial install, mandatory update over rollout-zero, tamper and uncontrolled downgrade rejection, validated rollback, installed publisher pins',
            'Incident recovery (runbook section 5): isolated recovery operator, PITR restore from escrow B, recovered Task.Api business smoke (login/task/audit/catalog/capabilities), RTO <= 14400 s'
        )
        notCoveredHere = @(
            'customer alert webhook URL/token and on-call owner (synthetic HTTP sink used)',
            'customer CA issuance, corporate DNS names and client trust policy (synthetic clean-room CA and resolver used)',
            'real storage devices, immutable/offline NAS snapshots and named escrow custodians (Docker volumes used)',
            'corporate code-signing certificate, RFC 3161 timestamp and controlled HTTPS origin (self-signed acceptance certificate used)',
            'host firewall live apply and monitoring compose on a real Linux host (verified separately in work/production/evidence/ops02-linux-firewall and outputs/20260908_ops04_monitoring_alerting_1.0.0)'
        )
    }
    [IO.File]::WriteAllText((Join-Path $EvidenceDirectory 'checks.json'), (($summary | ConvertTo-Json -Depth 12) + "`n"), [Text.UTF8Encoding]::new($false))

    if ($failed -eq 0) {
        Write-Output 'OPS RUNBOOK OPERATE/RECOVER REPRO: PASS'
    }
    else {
        throw "OPS RUNBOOK OPERATE/RECOVER REPRO: FAIL ($failed gate(s) failed)."
    }
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
