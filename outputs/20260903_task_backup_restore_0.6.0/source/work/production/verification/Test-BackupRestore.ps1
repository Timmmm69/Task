[CmdletBinding()]
param(
    [string]$Image = 'task-backup-ops:0.6.0',
    [string]$OutputDirectory = 'work/tmp/backup-verification',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$runId = 'task-backup-' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$scratch = Join-Path $projectRoot "work/tmp/$runId"
$output = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
$volumes = @('data','local','offhost','state','restore','socket','recovery-state','recovery-work') | ForEach-Object { "$runId-$_" }
$sourceFiles = @(
    Get-ChildItem (Join-Path $projectRoot 'work/production/deployment/backup') -File -Force
    Get-ChildItem (Join-Path $projectRoot 'work/production/src/Task.BackupAgent') -File
    Get-Item $PSCommandPath
    Get-Item (Join-Path $PSScriptRoot 'backup-integration.py')
    Get-Item (Join-Path $PSScriptRoot 'Test-ContainerPackaging.ps1')
)
$sourceHashes = @($sourceFiles | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path=[IO.Path]::GetRelativePath($projectRoot,$_.FullName).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
function Invoke-Docker([string[]]$Arguments) {
    $result = & docker @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($result -join "`n") }
    return ($result -join "`n")
}
try {
    New-Item -ItemType Directory -Force -Path $scratch,$output | Out-Null
    @{ runId=$runId; status='running'; startedAt=[DateTimeOffset]::UtcNow.ToString('O') } |
        ConvertTo-Json | Set-Content (Join-Path $output 'run.json') -Encoding utf8NoBOM
    $secrets = New-Item -ItemType Directory -Path (Join-Path $scratch 'secrets')
    foreach ($name in @('repo1-key','repo2-key','assets-key','postgres-password')) {
        $bytes = [byte[]]::new(32)
        [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
        [IO.File]::WriteAllText((Join-Path $secrets.FullName $name), [Convert]::ToHexString($bytes).ToLowerInvariant())
    }
    $assets = New-Item -ItemType Directory -Path (Join-Path $scratch 'assets')
    foreach ($category in @('configuration','keys','certificates','assets','installers','migrations')) {
        $folder = New-Item -ItemType Directory -Path (Join-Path $assets.FullName $category)
        [IO.File]::WriteAllText((Join-Path $folder.FullName 'fixture.txt'), "Test recovery $category content")
    }
    if (!$SkipBuild) {
        Invoke-Docker @('build','-f',(Join-Path $projectRoot 'work/production/deployment/backup/Dockerfile'),'-t',$Image,(Join-Path $projectRoot 'work/production')) | Out-Null
    }
    foreach ($volume in $volumes) { Invoke-Docker @('volume','create',$volume) | Out-Null }
    $mounts = @()
    $targets = @('/var/lib/postgresql/data','/backup/local','/backup/offhost','/var/lib/task-backup','/restore','/run/postgresql')
    for ($i=0; $i -lt $targets.Count; $i++) { $mounts += @('--mount',"type=volume,source=$($volumes[$i]),target=$($targets[$i])") }
    $mounts += @('--mount',"type=bind,source=$($secrets.FullName),target=/run/secrets,readonly",
        '--mount',"type=bind,source=$($assets.FullName),target=/recovery-input,readonly",
        '--mount',"type=bind,source=$projectRoot/work/production/verification,target=/verification,readonly",
        '--mount',"type=bind,source=$projectRoot/work/production/src/Task.Infrastructure/Persistence/Migrations,target=/test-migrations,readonly")
    Invoke-Docker (@('run','-d','--name',$runId,'--network','none','--read-only','--cap-drop','ALL',
        '--security-opt','no-new-privileges:true','--tmpfs','/tmp:mode=1777',
        '--tmpfs','/run/task-backup:uid=1001,gid=1001,mode=0700','-e','TASK_BACKUP_VALIDATION=1') + $mounts + @($Image,'database')) | Out-Null
    $ready = $false
    for ($i=0; $i -lt 60; $i++) {
        & docker exec $runId pg_isready -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (!$ready) { throw (Invoke-Docker @('logs',$runId)) }
    $log = Invoke-Docker @('exec',$runId,'python3','/verification/backup-integration.py')
    [IO.File]::WriteAllText((Join-Path $output 'integration.txt'),$log)
    if (!$log.Contains('BACKUP_INTEGRATION_PASSED')) { throw 'Backup integration did not complete.' }
    Invoke-Docker @('cp',"${runId}:/var/lib/task-backup/integration-evidence.json",(Join-Path $output 'integration.json')) | Out-Null
    $evidence = Get-Content (Join-Path $output 'integration.json') -Raw | ConvertFrom-Json
    $recoveryTarget = ([DateTimeOffset]$evidence.pitr.target).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'")
    Invoke-Docker @('stop','--time','15',$runId) | Out-Null
    $recoveryName = "$runId-recovery"
    Invoke-Docker @('run','-d','--name',$recoveryName,'--network','none','--read-only','--cap-drop','ALL',
        '--security-opt','no-new-privileges:true','--tmpfs','/tmp:mode=1777',
        '--tmpfs','/run/task-backup:uid=1001,gid=1001,mode=0700',
        '--mount',"type=volume,source=$($volumes[2]),target=/backup/offhost,readonly",
        '--mount',"type=volume,source=$($volumes[6]),target=/var/lib/task-backup",
        '--mount',"type=volume,source=$($volumes[7]),target=/restore",
        '--mount',"type=bind,source=$($secrets.FullName),target=/run/secrets,readonly",$Image,'operator') | Out-Null
    $recoveredText = Invoke-Docker @('exec',$recoveryName,'/opt/task-backup/runner.py','restore','--repo','2',
        '--label',$evidence.backup.copies[1].label,'--target',$recoveryTarget)
    $recovered = $recoveredText | ConvertFrom-Json
    $rows = Invoke-Docker @('exec',$recoveryName,'psql','-X','-h',$recovered.result.socket,'-U','postgres','-d','task','-At',
        '-c',"SELECT string_agg(id::text, ',' ORDER BY id) FROM recovery_probe")
    if ($rows.Trim() -ne '1,2') { throw 'Off-host-only incident recovery returned wrong records.' }
    [IO.File]::WriteAllText((Join-Path $output 'offhost-only-restore.json'),$recoveredText)
    $packages = Invoke-Docker @('exec',$recoveryName,'dpkg-query','-W','postgresql-16','pgbackrest','python3-cryptography')
    [IO.File]::WriteAllText((Join-Path $output 'backend-versions.txt'),$packages)
    Invoke-Docker @('image','inspect',$Image,'--format','{{.Id}}') | Set-Content (Join-Path $output 'image-id.txt')
    Write-Host $log
    Write-Host 'PASS recovery from read-only secondary with original server stopped, fresh state/workspace and no primary volumes/socket'
}
finally {
    & docker rm -f "$runId-recovery" *> $null
    & docker rm -f $runId *> $null
    foreach ($volume in $volumes) { & docker volume rm $volume *> $null }
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
