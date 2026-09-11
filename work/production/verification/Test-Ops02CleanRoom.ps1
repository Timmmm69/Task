[CmdletBinding()]
param([Parameter(Mandatory)][string]$EvidenceDirectory)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$ops = Join-Path $root 'work/production/deployment/ops02'
$compose = '/repo/work/production/deployment/security/compose.production.yaml'
$dindImage = 'docker@sha256:5efed980cba3fc126cf54e21a5a6ff8849d05b6e0623d6e7612f48e9cd6cd17e'
$powerShellImage = 'mcr.microsoft.com/powershell@sha256:810c4f1e0c9d23022c3ec18c50a6205ee4b60766f1739d329b2948df1fd7d5b0'
$postgresImage = 'postgres@sha256:57c72fd2a128e416c7fcc499958864df5301e940bca0a56f58fddf30ffc07777'
$images = [ordered]@{
    api = 'task-api@sha256:90986d76833f9395a30f7ff22e537d485cd8d9d7beb51eec7fe2b727add69951'
    migrator = 'task-database-migrator@sha256:7fd4b27316bfa4e8fa255399ed42a9dbe8dda79f07832b05057d9ce4fe842c4d'
    worker = 'task-release/task-worker@sha256:b317197ea5d22fe8bc9cefc20c140b7e755333b10241d32298fdbdb5696f3b22'
    postgres = $postgresImage
    tlsProxy = 'nginxinc/nginx-unprivileged@sha256:0c79d56aee561a1d81c63f00eee5fb5fe29279560cdc55e91425133104c7fbe6'
}
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('task-ops02-full-' + [Guid]::NewGuid().ToString('N'))
$imageTar = Join-Path $tempRoot 'images.tar'

function Docker {
    param([string[]]$Arguments, [switch]$Capture, [int[]]$AllowedExitCodes = @(0))
    $value = & docker.exe @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($AllowedExitCodes -notcontains $exitCode) { throw "docker failed with exit code ${exitCode}: docker $($Arguments -join ' ')" }
    if ($Capture) { return ($value -join "`n") }
    if ($null -ne $value) { Write-Output $value }
}
function Dind {
    param([string]$Name, [string[]]$Arguments, [switch]$Capture, [int[]]$AllowedExitCodes = @(0))
    return Docker -Arguments (@('exec', $Name, 'docker') + $Arguments) -Capture:$Capture -AllowedExitCodes $AllowedExitCodes
}

New-Item -ItemType Directory -Path $tempRoot, $EvidenceDirectory -Force | Out-Null
try {
    Docker -Arguments @('save', '--output', $imageTar, 'task-release/task-api:0.6.0', 'task-release/task-database-migrator:0.6.0', 'task-release/task-worker:0.6.0', 'postgres:16-alpine', 'nginxinc/nginx-unprivileged:1.29-alpine')
    $runs = @()
    foreach ($runNumber in 1, 2) {
        $runName = "ops02-clean-$runNumber"
        $dind = "task-$runName"
        $project = "ops02run$runNumber"
        $runRoot = Join-Path $tempRoot "run-$runNumber"
        $assets = Join-Path $runRoot 'assets'
        $parameterPath = Join-Path $runRoot 'parameters.json'
        $runEvidence = Join-Path $EvidenceDirectory "run-$runNumber"
        New-Item -ItemType Directory -Path $runRoot, $runEvidence -Force | Out-Null
        $configuration = Get-Content -LiteralPath (Join-Path $ops 'ops02.parameters.example.json') -Raw | ConvertFrom-Json -Depth 20
        $configuration.serverAddress = '127.0.0.1'; $configuration.httpsBindAddress = '127.0.0.1'; $configuration.httpsPort = 18443
        $configuration.deployment.secretRoot = '/assets/secrets'; $configuration.deployment.environmentFile = '/assets/production.env.partial'
        foreach ($name in $images.Keys) { $configuration.deployment.images.$name = $images[$name] }
        [IO.File]::WriteAllText($parameterPath, (($configuration | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
        & (Join-Path $ops 'New-Ops02CleanRoomAssets.ps1') -ParameterFile $parameterPath -OutputDirectory $assets -Confirm:$false
        try {
            $repoPath = (Resolve-Path $root).Path
            Docker -Arguments @('run','-d','--privileged','--name',$dind,'--pull','never','--mount',"type=bind,source=$imageTar,target=/imports/images.tar,readonly",'--mount',"type=bind,source=$assets,target=/input-assets,readonly",'--mount',"type=bind,source=$repoPath,target=/repo,readonly",$dindImage,'--iptables=true')
            for ($i=0; $i -lt 40; $i++) { & docker.exe exec $dind docker info 2>$null | Out-Null; if ($LASTEXITCODE -eq 0) { break }; Start-Sleep -Milliseconds 500 }
            Docker -Arguments @('exec',$dind,'sh','-lc','cp -a /input-assets /assets && chown 70:70 /assets/secrets/database/postgres* && chown 1654:1654 /assets/secrets/database/*.pgpass && chown -R 1654:1654 /assets/secrets/identity && chown -R 101:101 /assets/secrets/edge /assets/rotation && chmod 600 /assets/secrets/database/postgres.key /assets/secrets/database/postgres-admin-password /assets/secrets/database/*.pgpass /assets/secrets/identity/signing-current.pem /assets/secrets/identity/password-pepper /assets/secrets/edge/tls.key /assets/rotation/*/tls.key && docker load -i /imports/images.tar >/dev/null && apk add --no-cache curl openssl >/dev/null')
            Dind -Name $dind -Arguments (@('compose','-p',$project,'--env-file','/assets/production.env.partial','-f',$compose,'up','-d','postgres'))
            for ($i=0; $i -lt 40; $i++) { $health = Dind -Name $dind -Arguments (@('inspect','--format','{{.State.Health.Status}}',"$project-postgres-1")) -Capture; if ($health -eq 'healthy') { break }; Start-Sleep -Seconds 1 }
            Docker -Arguments @('exec','-e',"OPS02_POSTGRES_CONTAINER=$project-postgres-1",'-e','OPS02_ASSET_ROOT=/assets','-e','OPS02_SOURCE_ROOT=/repo/work/production',$dind,'sh','/repo/work/production/deployment/ops02/Bootstrap-Ops02Database.sh')
            $statusBefore = Dind -Name $dind -Arguments (@('compose','-p',$project,'--env-file','/assets/production.env.partial','-f',$compose,'--profile','tools','run','--rm','task-database-migrator','status')) -Capture -AllowedExitCodes @(0,6)
            if ($statusBefore -notmatch 'MigrationsRequired') { throw 'Initial migration status did not report MigrationsRequired.' }
            $apply = Dind -Name $dind -Arguments (@('compose','-p',$project,'--env-file','/assets/production.env.partial','-f',$compose,'--profile','tools','run','--rm','task-database-migrator','apply')) -Capture
            $statusAfter = Dind -Name $dind -Arguments (@('compose','-p',$project,'--env-file','/assets/production.env.partial','-f',$compose,'--profile','tools','run','--rm','task-database-migrator','status')) -Capture
            Docker -Arguments @('exec','-e',"OPS02_POSTGRES_CONTAINER=$project-postgres-1",'-e','OPS02_SOURCE_ROOT=/repo/work/production',$dind,'sh','/repo/work/production/deployment/ops02/Grant-Ops02Runtime.sh')
            Dind -Name $dind -Arguments (@('compose','-p',$project,'--env-file','/assets/production.env.partial','-f',$compose,'up','-d','task-api','task-worker','tls-proxy'))
            $trusted = $null
            for ($i=0; $i -lt 30; $i++) { try { $trusted = Docker -Arguments @('exec',$dind,'curl','--fail','--silent','--show-error','--cacert','/assets/client/task-clean-room-root-ca.crt','--resolve','task.cleanroom.test:18443:127.0.0.1','https://task.cleanroom.test:18443/health/ready') -Capture; if ($trusted -match '"ready":true') { break } } catch {}; Start-Sleep -Seconds 1 }
            if ($trusted -notmatch '"ready":true') { throw 'Trusted HTTPS readiness failed.' }
            $headers = Docker -Arguments @('exec',$dind,'curl','--silent','--show-error','--dump-header','-','--output','/dev/null','--cacert','/assets/client/task-clean-room-root-ca.crt','--resolve','task.cleanroom.test:18443:127.0.0.1','https://task.cleanroom.test:18443/health/ready') -Capture
            if ($headers -notmatch '(?im)^strict-transport-security:\s*max-age=31536000') { throw 'Trusted HTTPS response did not include the required HSTS header.' }
            $dbTls = Docker -Arguments @('exec','-e',"OPS02_NETWORK=${project}_database",'-e','OPS02_ASSET_ROOT=/assets','-e',"OPS02_POSTGRES_IMAGE=$postgresImage",$dind,'sh','/repo/work/production/deployment/ops02/Test-Ops02DatabaseTls.sh') -Capture
            foreach ($negative in @(
                'curl --fail --silent https://127.0.0.1:18443/health/ready >/dev/null 2>&1',
                'curl --fail --silent --cacert /assets/client/task-clean-room-root-ca.crt --resolve attacker.cleanroom.test:18443:127.0.0.1 https://attacker.cleanroom.test:18443/health/ready >/dev/null 2>&1',
                'curl --fail --silent --tls-max 1.1 --cacert /assets/client/task-clean-room-root-ca.crt --resolve task.cleanroom.test:18443:127.0.0.1 https://task.cleanroom.test:18443/health/ready >/dev/null 2>&1')) {
                & docker.exe exec $dind sh -lc "$negative; test `$? -ne 0" | Out-Null; if ($LASTEXITCODE -ne 0) { throw "Negative TLS probe unexpectedly succeeded: $negative" }
            }
            $oldFingerprint = Docker -Arguments @('exec',$dind,'openssl','x509','-in','/assets/secrets/edge/tls.crt','-noout','-fingerprint','-sha256') -Capture
            Docker -Arguments @('exec',$dind,'sh','-lc',"cat /assets/rotation/edge-v2/tls.crt >/assets/secrets/edge/tls.crt; cat /assets/rotation/edge-v2/tls.key >/assets/secrets/edge/tls.key; docker restart $project-tls-proxy-1 >/dev/null")
            $newFingerprint = Docker -Arguments @('exec',$dind,'openssl','x509','-in','/assets/secrets/edge/tls.crt','-noout','-fingerprint','-sha256') -Capture
            if ($newFingerprint -eq $oldFingerprint) { throw 'Rotation did not change the edge certificate.' }
            function Wait-ServedCertificate([string]$ExpectedFingerprint) {
                for ($attempt=0; $attempt -lt 20; $attempt++) {
                    $served = Docker -Arguments @('exec',$dind,'sh','-lc','openssl s_client -connect 127.0.0.1:18443 -servername task.cleanroom.test </dev/null 2>/dev/null | openssl x509 -noout -fingerprint -sha256') -Capture
                    if ($served.Trim() -eq $ExpectedFingerprint.Trim()) { return }
                    Start-Sleep -Milliseconds 500
                }
                throw "TLS proxy did not begin serving certificate $ExpectedFingerprint"
            }
            Wait-ServedCertificate $newFingerprint
            $wrongFingerprint = Docker -Arguments @('exec',$dind,'openssl','x509','-in','/assets/rotation/wrong-san/tls.crt','-noout','-fingerprint','-sha256') -Capture
            try {
                Docker -Arguments @('exec',$dind,'sh','-lc',"cat /assets/rotation/wrong-san/tls.crt >/assets/secrets/edge/tls.crt; cat /assets/rotation/wrong-san/tls.key >/assets/secrets/edge/tls.key; docker restart $project-tls-proxy-1 >/dev/null")
                Wait-ServedCertificate $wrongFingerprint
                & docker.exe exec $dind curl --fail --silent --cacert /assets/client/task-clean-room-root-ca.crt --resolve task.cleanroom.test:18443:127.0.0.1 https://task.cleanroom.test:18443/health/ready 2>$null | Out-Null
                if ($LASTEXITCODE -eq 0) { throw 'Wrong-SAN rotated certificate was unexpectedly trusted.' }
            }
            finally {
                Docker -Arguments @('exec',$dind,'sh','-lc',"cat /assets/rotation/edge-v2/tls.crt >/assets/secrets/edge/tls.crt; cat /assets/rotation/edge-v2/tls.key >/assets/secrets/edge/tls.key; docker restart $project-tls-proxy-1 >/dev/null")
                Wait-ServedCertificate $newFingerprint
            }
            Docker -Arguments @('exec',$dind,'curl','--fail','--silent','--cacert','/assets/client/task-clean-room-root-ca.crt','--resolve','task.cleanroom.test:18443:127.0.0.1','https://task.cleanroom.test:18443/health/ready') | Out-Null
            $ports = Dind -Name $dind -Arguments (@('ps','--format','{{.Names}}|{{.Ports}}')) -Capture
            if ($ports -match '0\.0\.0\.0:5432|0\.0\.0\.0:8080|127\.0\.0\.1:5432|127\.0\.0\.1:8080') { throw 'Forbidden host port was published.' }
            $runs += [ordered]@{ run=$runNumber; result='PASS'; migrationInitialStatus=$statusBefore.Trim(); migrationApply=$apply.Trim(); migrationStatus=$statusAfter.Trim(); httpsReady=$true; hsts=$true; databaseTls=$dbTls.Trim(); untrustedCaRejected=$true; wrongSanRejected=$true; tls11Rejected=$true; rotationChangedThumbprint=$true; wrongRotationRolledBack=$true; ports=$ports }
        }
        finally {
            & docker.exe exec $dind docker compose -p $project --env-file /assets/production.env.partial -f $compose down --volumes --remove-orphans 2>$null | Out-Null
            & docker.exe rm -f $dind 2>$null | Out-Null
        }
    }
    $evidence = [ordered]@{ schemaVersion=1; checkedAtUtc=[DateTimeOffset]::UtcNow.ToString('O'); result='PASS'; syntheticOnly=$true; runs=$runs }
    [IO.File]::WriteAllText((Join-Path $EvidenceDirectory 'clean-room-runs.json'), (($evidence | ConvertTo-Json -Depth 12)+"`n"), [Text.UTF8Encoding]::new($false))
    Write-Output 'OPS-02 CLEAN_ROOM PASS: two independent synthetic DinD runs completed.'
}
finally {
    if (Test-Path $tempRoot) { $resolved=(Resolve-Path $tempRoot).Path; $prefix=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()); if ($resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { Remove-Item $resolved -Recurse -Force } }
}
