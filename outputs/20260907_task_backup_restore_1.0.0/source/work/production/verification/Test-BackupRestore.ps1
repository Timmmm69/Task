[CmdletBinding()]
param(
    [string]$Image = 'task-backup-ops:1.0.0',
    [string]$ApiImage = 'task-api:ops03-1.0.0',
    [string]$MigratorImage = 'task-database-migrator:ops03-1.0.0',
    [string]$OutputDirectory = 'work/tmp/backup-verification-1.0.0',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$runId = 'task-backup-' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$scratch = Join-Path $projectRoot "work/tmp/$runId"
$output = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
$network = "$runId-net"
$containers = [Collections.Generic.List[string]]::new()
$volumes = [ordered]@{}
foreach ($suffix in @('data','local','offhost','snapshot','state','restore','socket',
    'secondary-state','secondary-work','snapshot-state','snapshot-work','escrow-b-state','escrow-b-work')) {
    $volumes[$suffix] = "$runId-$suffix"
}

function Invoke-Docker([string[]]$Arguments) {
    $result = & docker @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($result -join "`n") }
    return ($result -join "`n")
}
function New-Hex([int]$Bytes = 32) {
    $value = [byte[]]::new($Bytes)
    [Security.Cryptography.RandomNumberGenerator]::Fill($value)
    [Convert]::ToHexString($value).ToLowerInvariant()
}
function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { ([Net.IPEndPoint]$listener.LocalEndpoint).Port } finally { $listener.Stop() }
}
function Wait-Api([int]$Port, [string]$Container) {
    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/health/ready" -TimeoutSec 2 -SkipHttpErrorCheck
            if ($response.StatusCode -eq 200 -and ($response.Content | ConvertFrom-Json).status -eq 'Ready') { return }
        } catch { }
        if ((Invoke-Docker @('inspect',$Container,'--format','{{.State.Running}}')).Trim() -ne 'true') {
            throw (Invoke-Docker @('logs',$Container))
        }
        Start-Sleep -Seconds 1
    }
    throw "Task.Api did not become ready: $(Invoke-Docker @('logs',$Container))"
}
function Invoke-Api([int]$Port, [string]$Method, [string]$Path, $Body = $null,
    [string]$Token = '', [hashtable]$ExtraHeaders = @{}) {
    $headers = @{'X-Correlation-ID'=[guid]::NewGuid().ToString('D')}
    if ($Token) { $headers.Authorization = "Bearer $Token" }
    foreach ($name in $ExtraHeaders.Keys) { $headers[$name] = $ExtraHeaders[$name] }
    $parameters = @{ Uri="http://127.0.0.1:$Port$Path"; Method=$Method; Headers=$headers; SkipHttpErrorCheck=$true }
    if ($null -ne $Body) { $parameters.ContentType='application/json'; $parameters.Body=($Body | ConvertTo-Json -Depth 8 -Compress) }
    Invoke-WebRequest @parameters
}
function Start-Recovery([string]$Lane, [string]$Repository, [string]$StateVolume,
    [string]$WorkVolume, [string]$Keys) {
    $name = "$runId-$Lane"
    $containers.Add($name)
    Invoke-Docker @('run','-d','--name',$name,'--network','none','--read-only','--cap-drop','ALL',
        '--security-opt','no-new-privileges:true','--tmpfs','/tmp:mode=1777',
        '--tmpfs','/run/task-backup:uid=1001,gid=1001,mode=0700',
        '--mount',"type=volume,source=$Repository,target=/backup/offhost,readonly",
        '--mount',"type=volume,source=$StateVolume,target=/var/lib/task-backup",
        '--mount',"type=volume,source=$WorkVolume,target=/restore",
        '--mount',"type=bind,source=$Keys,target=/run/secrets,readonly",$Image,'operator') | Out-Null
    $name
}
function Invoke-Acceptance([string]$Container, [string]$StorageId, [string]$EscrowId,
    $Evidence, [string]$Target, [string]$Incident) {
    $text = Invoke-Docker @('exec',$Container,'python3','/opt/task-backup/acceptance.py','drill',
        '--label',$Evidence.backup.copies[1].label,'--target',$Target,'--incident-at',$Incident,
        '--dataset-id','ops03-clean-room-v1','--storage-copy-id',$StorageId,'--escrow-copy-id',$EscrowId,
        '--minimum-database-bytes',([string]$Evidence.backup.copies[1].databaseBytes),
        '--expected-schema-sha256',$Evidence.acceptanceBaseline.schemaSha256,
        '--expected-fixture-sha256',$Evidence.pitr.fixtureProbeSha256,'--scope','fixture')
    $receipt = $text | ConvertFrom-Json
    if ($receipt.status -ne 'succeeded' -or $receipt.result.productionAccepted -ne $false -or
        $receipt.result.fixtureProbeSha256 -ne $Evidence.pitr.fixtureProbeSha256) {
        throw "Recovery acceptance failed for $StorageId/$EscrowId."
    }
    $text
}

$sourceFiles = @(
    Get-ChildItem (Join-Path $projectRoot 'work/production/deployment/backup') -File -Force
    Get-ChildItem (Join-Path $projectRoot 'work/production/src/Task.BackupAgent') -File
    Get-ChildItem (Join-Path $projectRoot 'work/production/src/Task.Domain') -File -Recurse | Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]'
    Get-ChildItem (Join-Path $projectRoot 'work/production/src/Task.Application') -File -Recurse | Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]'
    Get-ChildItem (Join-Path $projectRoot 'work/production/src/Task.Infrastructure') -File -Recurse | Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]'
    Get-ChildItem (Join-Path $projectRoot 'work/production/src/Task.Api') -File -Recurse | Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]'
    Get-ChildItem (Join-Path $projectRoot 'work/production/src/Task.DatabaseMigrator') -File -Recurse | Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]'
    Get-Item $PSCommandPath
    Get-Item (Join-Path $PSScriptRoot 'backup-integration.py')
    Get-Item (Join-Path $PSScriptRoot 'backup-acceptance-tests.py')
    Get-Item (Join-Path $PSScriptRoot 'Test-ContainerPackaging.ps1')
    Get-Item (Join-Path $projectRoot 'work/production/deployment/containers/Dockerfile')
    Get-Item (Join-Path $projectRoot 'work/production/deployment/containers/NuGet.Config')
    Get-Item (Join-Path $projectRoot 'work/production/deployment/containers/sql/grant-runtime.sql')
) | Sort-Object FullName -Unique
$sourceHashes = @($sourceFiles | ForEach-Object {
    [ordered]@{ path=[IO.Path]::GetRelativePath($projectRoot,$_.FullName).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})

try {
    New-Item -ItemType Directory -Force -Path $scratch,$output | Out-Null
    @{ runId=$runId; status='running'; startedAt=[DateTimeOffset]::UtcNow.ToString('O') } |
        ConvertTo-Json | Set-Content (Join-Path $output 'run.json') -Encoding utf8NoBOM

    $secretsA = New-Item -ItemType Directory -Path (Join-Path $scratch 'escrow-a')
    $secretsB = New-Item -ItemType Directory -Path (Join-Path $scratch 'escrow-b')
    $postgresPassword = New-Hex
    foreach ($name in @('repo1-key','repo2-key','assets-key')) {
        [IO.File]::WriteAllText((Join-Path $secretsA.FullName $name), (New-Hex))
    }
    [IO.File]::WriteAllText((Join-Path $secretsA.FullName 'postgres-password'),$postgresPassword)
    Get-ChildItem -LiteralPath $secretsA.FullName -File | Copy-Item -Destination $secretsB.FullName

    $assets = New-Item -ItemType Directory -Path (Join-Path $scratch 'assets')
    foreach ($category in @('configuration','keys','certificates','assets','installers','migrations')) {
        $folder = New-Item -ItemType Directory -Path (Join-Path $assets.FullName $category)
        [IO.File]::WriteAllText((Join-Path $folder.FullName 'fixture.txt'), "OPS-03 clean-room $category")
    }
    $verificationKeys = New-Item -ItemType Directory -Path (Join-Path $assets.FullName 'keys/verification')
    $pepper = New-Hex
    $accountPassword = "Task-Ops03-$(New-Hex 12)!aA7"
    $runtimePassword = New-Hex
    [IO.File]::WriteAllText((Join-Path $assets.FullName 'keys/pepper.txt'),$pepper)
    $curve = [Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256')
    $ecdsa = [Security.Cryptography.ECDsa]::Create($curve)
    try {
        [IO.File]::WriteAllText((Join-Path $assets.FullName 'keys/signing.pem'),$ecdsa.ExportPkcs8PrivateKeyPem())
        [IO.File]::WriteAllText((Join-Path $verificationKeys.FullName 'signing.pem'),$ecdsa.ExportSubjectPublicKeyInfoPem())
    } finally { $ecdsa.Dispose() }
    $bootstrap = New-Item -ItemType Directory -Path (Join-Path $scratch 'bootstrap')
    [IO.File]::WriteAllText((Join-Path $bootstrap.FullName 'password.txt'),$accountPassword)
    [IO.File]::WriteAllText((Join-Path $bootstrap.FullName 'pepper.txt'),$pepper)
    [IO.File]::WriteAllText((Join-Path $bootstrap.FullName 'runtime-role.sql'),
        "CREATE ROLE task_runtime LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '$runtimePassword';`n\i /runtime-sql/grant-runtime.sql`n")

    if (!$SkipBuild) {
        Invoke-Docker @('build','-f',(Join-Path $projectRoot 'work/production/deployment/backup/Dockerfile'),'-t',$Image,(Join-Path $projectRoot 'work/production')) | Out-Null
        Invoke-Docker @('build','--target','task-api','-f',(Join-Path $projectRoot 'work/production/deployment/containers/Dockerfile'),'-t',$ApiImage,(Join-Path $projectRoot 'work/production')) | Out-Null
        Invoke-Docker @('build','--target','task-database-migrator','-f',(Join-Path $projectRoot 'work/production/deployment/containers/Dockerfile'),'-t',$MigratorImage,(Join-Path $projectRoot 'work/production')) | Out-Null
    }
    $unit = Invoke-Docker @('run','--rm','--network','none','--entrypoint','python3',
        '--mount',"type=bind,source=$projectRoot/work/production/verification,target=/verification,readonly",
        $Image,'/verification/backup-acceptance-tests.py')
    [IO.File]::WriteAllText((Join-Path $output 'acceptance-unit.txt'),$unit)

    Invoke-Docker @('network','create',$network) | Out-Null
    foreach ($volume in $volumes.Values) { Invoke-Docker @('volume','create',$volume) | Out-Null }
    $mounts = @()
    $primaryTargets = @{
        data='/var/lib/postgresql/data'; local='/backup/local'; offhost='/backup/offhost';
        state='/var/lib/task-backup'; restore='/restore'; socket='/run/postgresql'
    }
    foreach ($name in $primaryTargets.Keys) {
        $mounts += @('--mount',"type=volume,source=$($volumes[$name]),target=$($primaryTargets[$name])")
    }
    $mounts += @('--mount',"type=bind,source=$($secretsA.FullName),target=/run/secrets,readonly",
        '--mount',"type=bind,source=$($assets.FullName),target=/recovery-input,readonly",
        '--mount',"type=bind,source=$($bootstrap.FullName),target=/fixture-bootstrap,readonly",
        '--mount',"type=bind,source=$projectRoot/work/production/deployment/containers/sql,target=/runtime-sql,readonly",
        '--mount',"type=bind,source=$projectRoot/work/production/verification,target=/verification,readonly",
        '--mount',"type=bind,source=$projectRoot/work/production/src/Task.Infrastructure/Persistence/Migrations,target=/test-migrations,readonly")
    $containers.Add($runId)
    Invoke-Docker (@('run','-d','--name',$runId,'--network',$network,'--network-alias','task-db','--read-only','--cap-drop','ALL',
        '--security-opt','no-new-privileges:true','--tmpfs','/tmp:mode=1777',
        '--tmpfs','/run/task-backup:uid=1001,gid=1001,mode=0700','-e','TASK_BACKUP_VALIDATION=1') + $mounts + @($Image,'database')) | Out-Null
    $ready = $false
    for ($i=0; $i -lt 60; $i++) {
        & docker exec $runId pg_isready -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (!$ready) { throw (Invoke-Docker @('logs',$runId)) }
    Invoke-Docker @('run','--rm','--network',$network,'--entrypoint','psql',
        '-e',"PGPASSWORD=$postgresPassword",$Image,'-X','-h','task-db','-U','postgres','-d','postgres',
        '-At','-v','ON_ERROR_STOP=1','-c','SELECT 1') | Out-Null
    $setupLog = Invoke-Docker @('exec','-e','TASK_BACKUP_FIXTURE_PHASE=setup',$runId,'python3','/verification/backup-integration.py')
    if (!$setupLog.Contains('BACKUP_FIXTURE_READY')) { throw 'Backup fixture setup did not complete.' }

    $connection = "Host=task-db;Port=5432;Database=task;Username=postgres;Password=$postgresPassword;SSL Mode=Disable"
    Invoke-Docker @('run','--rm','--network',$network,'--read-only','--cap-drop','ALL','--security-opt','no-new-privileges:true',
        '--tmpfs','/tmp:mode=1777','-e',"ConnectionStrings__TaskDatabase=$connection",$MigratorImage,'apply') | Out-Null
    Invoke-Docker @('exec',$runId,'psql','-X','-U','postgres','-d','task','-v','ON_ERROR_STOP=1','-c',
        "CREATE TABLE public.recovery_probe(id integer PRIMARY KEY, value text NOT NULL); INSERT INTO recovery_probe VALUES(1, 'base')") | Out-Null
    Invoke-Docker @('exec',$runId,'psql','-X','-U','postgres','-d','task','-v','ON_ERROR_STOP=1','-f','/fixture-bootstrap/runtime-role.sql') | Out-Null
    Invoke-Docker @('run','--rm','--network',$network,'--read-only','--cap-drop','ALL','--security-opt','no-new-privileges:true',
        '--tmpfs','/tmp:mode=1777','--mount',"type=bind,source=$($bootstrap.FullName),target=/fixture-bootstrap,readonly",
        '-e',"ConnectionStrings__TaskDatabase=$connection",'-e','TASK_BOOTSTRAP_ORGANIZATION_CODE=ops03-fixture',
        '-e','TASK_BOOTSTRAP_ORGANIZATION_NAME=OPS-03 clean room','-e','TASK_BOOTSTRAP_TIME_ZONE=UTC',
        '-e','TASK_BOOTSTRAP_ADMIN_FIRST_NAME=Recovery','-e','TASK_BOOTSTRAP_ADMIN_LAST_NAME=Operator',
        '-e','TASK_BOOTSTRAP_ADMIN_LOGIN=ops03-admin','-e','TASK_BOOTSTRAP_PASSWORD_FILE=/fixture-bootstrap/password.txt',
        '-e','TASK_BOOTSTRAP_PEPPER_FILE=/fixture-bootstrap/pepper.txt',$MigratorImage,'bootstrap-admin') | Out-Null

    $primaryApi = "$runId-api-primary"
    $containers.Add($primaryApi)
    $primaryPort = Get-FreePort
    Invoke-Docker @('run','-d','--name',$primaryApi,'--network',$network,'-p',"127.0.0.1:$primaryPort`:8080",
        '--read-only','--cap-drop','ALL','--security-opt','no-new-privileges:true','--tmpfs','/tmp:mode=1777',
        '--mount',"type=bind,source=$($assets.FullName),target=/recovery-assets,readonly",
        '-e',"ConnectionStrings__TaskDatabase=Host=task-db;Port=5432;Database=task;Username=task_runtime;Password=$runtimePassword;SSL Mode=Disable",
        '-e','Task__Identity__Issuer=http://ops03.fixture','-e','Task__Identity__Audience=task-ops03',
        '-e','Task__Identity__SigningKeyReference=file:/recovery-assets/keys/signing.pem',
        '-e','Task__Identity__PepperReference=file:/recovery-assets/keys/pepper.txt',
        '-e','Task__Identity__VerificationKeysDirectory=file:/recovery-assets/keys/verification',$ApiImage) | Out-Null
    Wait-Api $primaryPort $primaryApi
    $loginBody = @{ login='ops03-admin'; password=$accountPassword; device=@{
        deviceKey=(New-Hex 16); deviceName='OPS-03 seed'; platform='windows'; appVersion='1.0.0'; osVersion='clean-room' } }
    $login = Invoke-Api $primaryPort Post '/api/v1/auth/login' $loginBody
    if ($login.StatusCode -ne 200) { throw "Primary fixture login failed: $($login.Content)" }
    $token = ($login.Content | ConvertFrom-Json).accessToken
    $created = Invoke-Api $primaryPort Post '/api/v1/tasks' @{title='OPS-03 recovered task'; priority='high'} $token `
        @{'Idempotency-Key'="ops03-seed-$(New-Hex 8)"}
    if ($created.StatusCode -ne 201) { throw "Primary fixture task creation failed: $($created.Content)" }
    $taskId = ($created.Content | ConvertFrom-Json).id
    Invoke-Docker @('rm','-f',$primaryApi) | Out-Null
    $containers.Remove($primaryApi) | Out-Null
    $businessFixturePath = Join-Path $scratch 'business-fixture.json'
    [ordered]@{ login='ops03-admin'; taskId=$taskId; taskTitle='OPS-03 recovered task' } |
        ConvertTo-Json | Set-Content $businessFixturePath -Encoding utf8NoBOM
    Invoke-Docker @('cp',$businessFixturePath,"${runId}:/var/lib/task-backup/business-fixture.json") | Out-Null

    $log = Invoke-Docker @('exec','-e','TASK_BACKUP_FIXTURE_PHASE=exercise',$runId,'python3','/verification/backup-integration.py')
    [IO.File]::WriteAllText((Join-Path $output 'integration.txt'),$setupLog + "`n" + $log)
    if (!$log.Contains('BACKUP_INTEGRATION_PASSED')) { throw 'Backup integration did not complete.' }
    Invoke-Docker @('cp',"${runId}:/var/lib/task-backup/integration-evidence.json",(Join-Path $output 'integration.json')) | Out-Null
    $evidence = Get-Content (Join-Path $output 'integration.json') -Raw | ConvertFrom-Json
    $target = ([DateTimeOffset]$evidence.pitr.target).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'")
    $incident = ([DateTimeOffset]$evidence.pitr.incident).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'")
    Invoke-Docker @('run','--rm','--network','none','--user','0:0','--entrypoint','/bin/sh',
        '--mount',"type=volume,source=$($volumes.offhost),target=/source,readonly",
        '--mount',"type=volume,source=$($volumes.snapshot),target=/target",
        $Image,'-c','cp -a /source/. /target/') | Out-Null
    Invoke-Docker @('stop','--time','15',$runId) | Out-Null

    $secondary = Start-Recovery 'secondary' $volumes.offhost $volumes['secondary-state'] $volumes['secondary-work'] $secretsA.FullName
    [IO.File]::WriteAllText((Join-Path $output 'acceptance-secondary.json'),
        (Invoke-Acceptance $secondary 'readonly-secondary' 'escrow-a' $evidence $target $incident))
    Invoke-Docker @('rm','-f',$secondary) | Out-Null
    $containers.Remove($secondary) | Out-Null

    $snapshot = Start-Recovery 'snapshot' $volumes.snapshot $volumes['snapshot-state'] $volumes['snapshot-work'] $secretsA.FullName
    [IO.File]::WriteAllText((Join-Path $output 'acceptance-protected-snapshot.json'),
        (Invoke-Acceptance $snapshot 'protected-snapshot' 'escrow-a' $evidence $target $incident))
    Invoke-Docker @('rm','-f',$snapshot) | Out-Null
    $containers.Remove($snapshot) | Out-Null

    $escrowB = Start-Recovery 'escrow-b' $volumes.snapshot $volumes['escrow-b-state'] $volumes['escrow-b-work'] $secretsB.FullName
    $serviceStarted = [DateTimeOffset]::UtcNow
    $restoreText = Invoke-Docker @('exec',$escrowB,'/opt/task-backup/runner.py','restore','--repo','2',
        '--label',$evidence.backup.copies[1].label,'--target',$target)
    $restored = $restoreText | ConvertFrom-Json
    if ($restored.status -ne 'succeeded') { throw 'Escrow B restore failed.' }
    $rows = Invoke-Docker @('exec',$escrowB,'psql','-X','-h',$restored.result.socket,'-U','postgres','-d','task','-At',
        '-c',"SELECT string_agg(id::text, ',' ORDER BY id) FROM recovery_probe")
    if ($rows.Trim() -ne '1,2') { throw 'Escrow B PITR returned the wrong clean-room rows.' }
    $recoveryRoot = [IO.Path]::GetDirectoryName($restored.result.data).Replace('\','/')
    $recoveredAssets = "$recoveryRoot/recovery-assets"
    Invoke-Docker @('exec',$escrowB,'mkdir','-p',$recoveredAssets) | Out-Null
    Invoke-Docker @('exec',$escrowB,'tar','-xf',$restored.result.assets.archive,'-C',$recoveredAssets) | Out-Null

    $serviceApi = "$runId-api-recovery"
    $containers.Add($serviceApi)
    $servicePort = Get-FreePort
    Invoke-Docker @('run','-d','--name',$serviceApi,'--network',$network,'-p',"127.0.0.1:$servicePort`:8080",
        '--user','1001:1001','--read-only','--cap-drop','ALL','--security-opt','no-new-privileges:true','--tmpfs','/tmp:mode=1777',
        '--mount',"type=volume,source=$($volumes['escrow-b-work']),target=/restore,readonly",
        '-e',"ConnectionStrings__TaskDatabase=Host=$($restored.result.socket);Database=task;Username=postgres;SSL Mode=Disable",
        '-e','Task__Identity__Issuer=http://ops03.fixture','-e','Task__Identity__Audience=task-ops03',
        '-e',"Task__Identity__SigningKeyReference=file:$recoveredAssets/keys/signing.pem",
        '-e',"Task__Identity__PepperReference=file:$recoveredAssets/keys/pepper.txt",
        '-e',"Task__Identity__VerificationKeysDirectory=file:$recoveredAssets/keys/verification",$ApiImage) | Out-Null
    Wait-Api $servicePort $serviceApi
    $recoveryLogin = Invoke-Api $servicePort Post '/api/v1/auth/login' $loginBody
    if ($recoveryLogin.StatusCode -ne 200) { throw "Recovered login failed: $($recoveryLogin.Content)" }
    $recoveryToken = ($recoveryLogin.Content | ConvertFrom-Json).accessToken
    $task = Invoke-Api $servicePort Get "/api/v1/tasks/$taskId" $null $recoveryToken
    $audit = Invoke-Api $servicePort Get "/api/v1/audit?objectId=$taskId&pageSize=20" $null $recoveryToken
    $catalog = Invoke-Api $servicePort Get '/api/v1/catalog/tree' $null $recoveryToken
    $capabilities = Invoke-Api $servicePort Get '/api/v1/capabilities' $null $recoveryToken
    foreach ($probe in @($task,$audit,$catalog,$capabilities)) {
        if ($probe.StatusCode -ne 200) { throw "Recovered API business smoke failed: HTTP $($probe.StatusCode) $($probe.Content)" }
    }
    if (($task.Content | ConvertFrom-Json).title -ne $evidence.businessFixture.taskTitle) {
        throw 'Recovered API returned the wrong task.'
    }
    $serviceReady = [DateTimeOffset]::UtcNow
    $serviceRto = ($serviceReady - [DateTimeOffset]$incident).TotalSeconds
    if ($serviceRto -gt 14400) { throw 'Clean-room service recovery exceeded the four-hour RTO.' }
    [ordered]@{
        version='1.0.0'; status='succeeded'; scope='fixture'; releaseReady=$true; productionAccepted=$false
        datasetId='ops03-clean-room-v1'; storageCopyId='protected-snapshot'; escrowCopyId='escrow-b'
        label=$evidence.backup.copies[1].label; target=$target; incidentAt=$incident
        requestedLossWindowSeconds=([DateTimeOffset]$incident - [DateTimeOffset]$target).TotalSeconds
        restoreStartedAt=$serviceStarted.ToString('O'); serviceReadyAt=$serviceReady.ToString('O')
        serviceReadyRtoSeconds=[Math]::Round($serviceRto,3); rtoLimitSeconds=14400
        fixtureProbeSha256=$evidence.pitr.fixtureProbeSha256; schemaSha256=$evidence.acceptanceBaseline.schemaSha256
        isolation=[ordered]@{ originalServerStopped=$true; primaryVolumesMounted=$false; repositoryReadOnly=$true; apiPublishedLoopbackOnly=$true }
        apiSmoke=[ordered]@{ readiness=200; login=200; task=200; audit=200; catalog=200; capabilities=200; taskId=$taskId }
        customerDeploymentRequired=@('map storage IDs to physical devices/hosts','configure immutable/offline retention and production ACLs','place escrow copies with named custodians','repeat drill with representative customer workload')
    } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'acceptance-service-escrow-b.json') -Encoding utf8NoBOM
    [IO.File]::WriteAllText((Join-Path $output 'offhost-only-restore.json'),$restoreText)
    Invoke-Docker @('rm','-f',$serviceApi) | Out-Null
    $containers.Remove($serviceApi) | Out-Null
    Invoke-Docker @('exec',$escrowB,'pg_ctl','-D',$restored.result.data,'-m','fast','-w','stop') | Out-Null

    [ordered]@{
        version='1.0.0'; status='succeeded'; releaseReady=$true; productionAccepted=$false
        restorations=3; storageTargets=@('readonly-secondary','protected-snapshot'); escrowCopies=@('escrow-a','escrow-b')
        rpoLimitSeconds=900; rtoLimitSeconds=14400; measuredServiceRtoSeconds=[Math]::Round($serviceRto,3)
        receipts=@('acceptance-secondary.json','acceptance-protected-snapshot.json','acceptance-service-escrow-b.json')
    } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'acceptance-suite.json') -Encoding utf8NoBOM
    $packages = Invoke-Docker @('exec',$escrowB,'dpkg-query','-W','postgresql-16','pgbackrest','python3-cryptography')
    [IO.File]::WriteAllText((Join-Path $output 'backend-versions.txt'),$packages)
    Invoke-Docker @('image','inspect',$Image,'--format','{{.Id}}') | Set-Content (Join-Path $output 'image-id.txt')
    Invoke-Docker @('image','inspect',$ApiImage,'--format','{{.Id}}') | Set-Content (Join-Path $output 'api-image-id.txt')
    Write-Host $log
    Write-Host 'PASS three clean-room recoveries, independent escrow copies and recovered Task.Api business smoke'
}
finally {
    foreach ($container in @($containers)) { & docker rm -f $container *> $null }
    & docker network rm $network *> $null
    foreach ($volume in $volumes.Values) { & docker volume rm $volume *> $null }
    $containersLeft = @(& docker ps -aq --filter "name=$runId" | Where-Object { $_ })
    $volumesLeft = @(& docker volume ls -q --filter "name=$runId" | Where-Object { $_ })
    $allowed = [IO.Path]::GetFullPath((Join-Path $projectRoot 'work/tmp')) + [IO.Path]::DirectorySeparatorChar
    $resolved = [IO.Path]::GetFullPath($scratch)
    if (!$resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    if ($containersLeft.Count -or $volumesLeft.Count) { throw 'Backup fixture cleanup left Docker resources.' }
    if (Test-Path -LiteralPath $output) {
        [IO.File]::WriteAllText((Join-Path $output 'cleanup.json'), '{"containersRemaining":0,"volumesRemaining":0,"testSecretsRemoved":true}')
    }
}
foreach ($source in $sourceHashes) {
    if ((Get-FileHash -LiteralPath (Join-Path $projectRoot $source.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $source.sha256) {
        throw "Source changed during verification: $($source.path)"
    }
}
[ordered]@{ runId=$runId; status='succeeded'; completedAt=[DateTimeOffset]::UtcNow.ToString('O'); sources=$sourceHashes } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'run.json') -Encoding utf8NoBOM
