[CmdletBinding()]
param(
    [string]$Version = '0.4.0',
    [string]$GitSha
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$productionRoot = Join-Path $repoRoot 'work\production'
$dockerfile = Join-Path $productionRoot 'deployment\containers\Dockerfile'
$composeFile = Join-Path $productionRoot 'deployment\containers\compose.validation.yaml'
$initializeRolesSql = Join-Path $productionRoot 'deployment\containers\sql\initialize-validation-roles.sql'
$grantRuntimeSql = Join-Path $productionRoot 'deployment\containers\sql\grant-runtime.sql'
$tmpRoot = Join-Path $repoRoot 'work\tmp'
$projectName = 'taskc' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$tempDirectory = Join-Path $tmpRoot $projectName
$envFile = Join-Path $tempDirectory 'validation.env'
$databaseName = 'task_validation'
$organizationId = [Guid]::NewGuid()
$failure = $null
$cleanupFailure = $null

function Write-Ok { Write-Host "[ OK ] $($args -join ' ')" }

function New-RandomHex {
    $bytes = New-Object byte[] 32
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) }
    finally { $generator.Dispose() }
    return ([BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
}

function Invoke-Docker {
    param(
        [Parameter(Mandatory)] [string[]]$Arguments,
        [int[]]$ExpectedExitCodes = @(0),
        [switch]$AllowFailure
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& docker @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorActionPreference }
    if (-not $AllowFailure -and $exitCode -notin $ExpectedExitCodes) {
        $safeOutput = ($output | Select-Object -Last 40) -join [Environment]::NewLine
        throw "Docker command failed with exit $exitCode.$([Environment]::NewLine)$safeOutput"
    }

    return [pscustomobject]@{ ExitCode = $exitCode; Output = @($output) }
}

function Get-ComposeArguments {
    param([string[]]$Tail, [string[]]$Profiles = @())
    $arguments = @('compose', '-p', $projectName, '--env-file', $envFile, '-f', $composeFile)
    foreach ($profile in $Profiles) {
        $arguments += @('--profile', $profile)
    }
    return $arguments + $Tail
}

function Invoke-Compose {
    param(
        [Parameter(Mandatory)] [string[]]$Tail,
        [string[]]$Profiles = @(),
        [int[]]$ExpectedExitCodes = @(0),
        [switch]$AllowFailure
    )
    Invoke-Docker -Arguments (Get-ComposeArguments -Tail $Tail -Profiles $Profiles) `
        -ExpectedExitCodes $ExpectedExitCodes -AllowFailure:$AllowFailure
}

function Invoke-PsqlText {
    param(
        [Parameter(Mandatory)] [string]$Sql,
        [Parameter(Mandatory)] [string]$User,
        [Parameter(Mandatory)] [string]$Password,
        [string[]]$ExtraArguments = @(),
        [switch]$AllowFailure
    )

    $arguments = Get-ComposeArguments -Profiles @() -Tail @(
        'exec', '-T', '-e', "PGPASSWORD=$Password", 'postgres',
        'psql', '-h', '127.0.0.1', '-U', $User, '-d', $databaseName,
        '-v', 'ON_ERROR_STOP=1'
    )
    $arguments += $ExtraArguments
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @($Sql | & docker @arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorActionPreference }
    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "psql failed with exit $exitCode. $(($output | Select-Object -Last 20) -join [Environment]::NewLine)"
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Output = @($output) }
}

function Assert-Denied {
    param([string]$Sql, [string]$Name, [string]$RuntimePassword)
    $result = Invoke-PsqlText -Sql $Sql -User 'task_runtime' -Password $RuntimePassword -AllowFailure
    $text = $result.Output -join "`n"
    if ($result.ExitCode -eq 0 -or $text -notmatch 'permission denied|must be owner') {
        throw "Runtime role negative check '$Name' was not rejected by PostgreSQL permissions."
    }
    Write-Ok "Runtime role cannot $Name."
}

function Test-ImageContract {
    param([string]$Image, [string]$ExpectedTitle, [string]$RuntimePassword, [string]$MigrationPassword, [string]$AdminPassword)

    $uid = (Invoke-Docker -Arguments @('run', '--rm', '--entrypoint', 'id', $Image, '-u')).Output -join ''
    if ($uid -notmatch '^\d+$' -or $uid -eq '0') { throw "$ExpectedTitle image runs as invalid UID '$uid'." }

    $sdks = (Invoke-Docker -Arguments @('run', '--rm', '--entrypoint', 'dotnet', $Image, '--list-sdks')).Output -join "`n"
    if (-not [string]::IsNullOrWhiteSpace($sdks)) { throw "$ExpectedTitle runtime image contains a .NET SDK." }

    $labelJson = (Invoke-Docker -Arguments @('image', 'inspect', $Image, '--format={{json .Config.Labels}}')).Output -join ''
    $labelData = $labelJson | ConvertFrom-Json
    $labels = "$($labelData.'org.opencontainers.image.title')|$($labelData.'org.opencontainers.image.version')|$($labelData.'org.opencontainers.image.revision')"
    if ($labels -ne "$ExpectedTitle|$Version|$GitSha") { throw "$ExpectedTitle OCI labels are '$labels'." }

    $metadata = ((Invoke-Docker -Arguments @('image', 'inspect', $Image)).Output +
        (Invoke-Docker -Arguments @('history', '--no-trunc', '--format', '{{.CreatedBy}}', $Image)).Output) -join "`n"
    foreach ($secret in @($RuntimePassword, $MigrationPassword, $AdminPassword)) {
        if ($metadata.IndexOf($secret, [StringComparison]::Ordinal) -ge 0) {
            throw "$ExpectedTitle image metadata contains a validation credential."
        }
    }
    if ($metadata -match '(?i)ConnectionStrings__TaskDatabase|POSTGRES_PASSWORD|Password=') {
        throw "$ExpectedTitle image metadata contains secret-like configuration."
    }
    Write-Ok "$ExpectedTitle image is non-root, SDK-free, labelled, and contains no credential metadata."
}

function Stop-And-CheckService {
    param([string]$Service)
    Invoke-Compose -Profiles @('background') -Tail @('up', '-d', '--no-deps', $Service) | Out-Null
    $containerId = ((Invoke-Compose -Profiles @('background') -Tail @('ps', '-q', $Service)).Output -join '').Trim()
    if ([string]::IsNullOrWhiteSpace($containerId)) { throw "$Service container did not start." }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    Invoke-Docker -Arguments @('stop', '--time', '10', $containerId) | Out-Null
    $timer.Stop()
    $exitCode = ((Invoke-Docker -Arguments @('inspect', '--format', '{{.State.ExitCode}}', $containerId)).Output -join '').Trim()
    if ($timer.Elapsed.TotalSeconds -gt 10.5 -or $exitCode -ne '0') {
        throw "$Service did not stop cleanly within 10 seconds (elapsed=$([math]::Round($timer.Elapsed.TotalSeconds, 2)), exit=$exitCode)."
    }
    Write-Ok "$Service handled SIGTERM and exited 0 in $([math]::Round($timer.Elapsed.TotalSeconds, 2)) seconds."
}

try {
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if ($null -eq $docker) { throw 'Docker CLI is unavailable; the container gate was not executed.' }
    $server = Invoke-Docker -Arguments @('version', '--format', '{{.Server.Os}}/{{.Server.Arch}}')
    if (($server.Output -join '').Trim() -ne 'linux/amd64') {
        throw "Docker must provide a linux/amd64 engine for this gate; actual: '$(($server.Output -join '').Trim())'."
    }
    Write-Ok 'Docker linux/amd64 engine is available.'

    if ([string]::IsNullOrWhiteSpace($GitSha)) {
        $GitSha = (& git -C $repoRoot rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Could not determine Git SHA.' }
    }
    if ($GitSha -notmatch '^[0-9a-f]{40}$') { throw "GitSha '$GitSha' is not a full commit SHA." }

    foreach ($required in @($dockerfile, $composeFile, $initializeRolesSql, $grantRuntimeSql)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required file is missing: $required" }
    }

    New-Item -ItemType Directory -Path $tempDirectory -Force | Out-Null
    $adminPassword = New-RandomHex
    $migrationPassword = New-RandomHex
    $runtimePassword = New-RandomHex

    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $apiPort = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    $listener.Stop()

    $imagePrefix = "task-validation-$projectName"
    $images = [ordered]@{
        'task-api' = "$imagePrefix-api`:$Version"
        'task-worker' = "$imagePrefix-worker`:$Version"
        'task-backup-agent' = "$imagePrefix-backup-agent`:$Version"
        'task-database-migrator' = "$imagePrefix-database-migrator`:$Version"
        'task-container-validation' = "$imagePrefix-store-gate`:$Version"
    }

    [IO.File]::WriteAllLines($envFile, @(
        'POSTGRES_IMAGE=postgres:16-alpine@sha256:075f7ba66bc9b3ce7d6b8b635208ff61cd7cf1a67d71ec530eec5d7ae0cbe571'
        "POSTGRES_ADMIN_PASSWORD=$adminPassword"
        "TASK_DB_NAME=$databaseName"
        "TASK_MIGRATION_PASSWORD=$migrationPassword"
        "TASK_RUNTIME_PASSWORD=$runtimePassword"
        "TASK_API_HOST_PORT=$apiPort"
        "TASK_VALIDATION_ORGANIZATION_ID=$organizationId"
        "TASK_API_IMAGE=$($images['task-api'])"
        "TASK_WORKER_IMAGE=$($images['task-worker'])"
        "TASK_BACKUP_AGENT_IMAGE=$($images['task-backup-agent'])"
        "TASK_DATABASE_MIGRATOR_IMAGE=$($images['task-database-migrator'])"
        "TASK_CONTAINER_VALIDATION_IMAGE=$($images['task-container-validation'])"
    ), [Text.UTF8Encoding]::new($false))

    foreach ($target in $images.Keys) {
        Invoke-Docker -Arguments @(
            'build', '--platform', 'linux/amd64', '--target', $target,
            '--build-arg', "VERSION=$Version", '--build-arg', "GIT_SHA=$GitSha",
            '--tag', $images[$target], '--file', $dockerfile, $productionRoot
        ) | Out-Null
        Write-Ok "Built $target."
    }

    Test-ImageContract -Image $images['task-api'] -ExpectedTitle 'Task.Api' -RuntimePassword $runtimePassword -MigrationPassword $migrationPassword -AdminPassword $adminPassword
    Test-ImageContract -Image $images['task-worker'] -ExpectedTitle 'Task.Worker' -RuntimePassword $runtimePassword -MigrationPassword $migrationPassword -AdminPassword $adminPassword
    Test-ImageContract -Image $images['task-backup-agent'] -ExpectedTitle 'Task.BackupAgent' -RuntimePassword $runtimePassword -MigrationPassword $migrationPassword -AdminPassword $adminPassword
    Test-ImageContract -Image $images['task-database-migrator'] -ExpectedTitle 'Task.DatabaseMigrator' -RuntimePassword $runtimePassword -MigrationPassword $migrationPassword -AdminPassword $adminPassword

    Invoke-Compose -Tail @('up', '-d', 'postgres') | Out-Null
    $healthy = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $probe = Invoke-Compose -Tail @('exec', '-T', 'postgres', 'pg_isready', '-U', 'postgres', '-d', $databaseName) -AllowFailure
        if ($probe.ExitCode -eq 0) { $healthy = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $healthy) { throw 'PostgreSQL 16 did not become ready.' }
    Write-Ok 'PostgreSQL 16 is ready.'

    $initArguments = Get-ComposeArguments -Profiles @() -Tail @(
        'exec', '-T', '-e', "PGPASSWORD=$adminPassword", 'postgres',
        'psql', '-h', '127.0.0.1', '-U', 'postgres', '-d', $databaseName,
        '-v', 'ON_ERROR_STOP=1', '-v', "database_name=$databaseName",
        '-v', "migration_password=$migrationPassword", '-v', "runtime_password=$runtimePassword"
    )
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $initOutput = @(Get-Content -Raw -LiteralPath $initializeRolesSql | & docker @initArguments 2>&1)
        $initExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorActionPreference }
    if ($initExitCode -ne 0) { throw "Role initialization failed. $(($initOutput | Select-Object -Last 20) -join [Environment]::NewLine)" }
    Write-Ok 'Fixed migration/runtime roles were created without tracked credentials.'

    $before = Invoke-Compose -Profiles @('tools') -Tail @('run', '--rm', '--no-deps', 'task-database-migrator', 'status') -ExpectedExitCodes @(6)
    if (($before.Output -join "`n") -notmatch 'code=MigrationsRequired') { throw 'Initial migration status did not report MigrationsRequired.' }
    Write-Ok 'Migration status returned 6 before apply.'

    $apply = Invoke-Compose -Profiles @('tools') -Tail @('run', '--rm', '--no-deps', 'task-database-migrator', 'apply')
    if (($apply.Output -join "`n") -notmatch 'code=Applied') { throw 'Migration apply did not report Applied.' }
    Write-Ok 'Migration role applied the catalog.'

    Invoke-PsqlText -Sql (Get-Content -Raw -LiteralPath $grantRuntimeSql) -User 'task_migration' -Password $migrationPassword | Out-Null
    Write-Ok 'Idempotent minimal runtime grants were applied by task_migration.'
    Invoke-PsqlText -Sql (Get-Content -Raw -LiteralPath $grantRuntimeSql) -User 'task_migration' -Password $migrationPassword | Out-Null
    Write-Ok 'Runtime grant script is idempotent.'

    $after = Invoke-Compose -Profiles @('tools') -Tail @('run', '--rm', '--no-deps', 'task-database-migrator', 'status')
    if (($after.Output -join "`n") -notmatch 'code=Ready') { throw 'Post-apply migration status did not report Ready.' }
    Write-Ok 'Migration status returned 0/Ready after apply.'

    $seedSql = "INSERT INTO core.organizations (id, code, name, default_time_zone) VALUES ('$organizationId', 'container-validation', 'Container validation', 'UTC');"
    Invoke-PsqlText -Sql $seedSql -User 'task_migration' -Password $migrationPassword | Out-Null

    $privilegeSql = @'
SELECT concat_ws('|',
    has_database_privilege(current_user, current_database(), 'CONNECT'),
    has_database_privilege(current_user, current_database(), 'CREATE'),
    has_schema_privilege(current_user, 'core', 'USAGE'),
    has_schema_privilege(current_user, 'core', 'CREATE'),
    has_table_privilege(current_user, 'core.objects', 'SELECT,INSERT,UPDATE'),
    has_table_privilege(current_user, 'infrastructure.schema_migrations', 'UPDATE'));
'@
    $privileges = Invoke-PsqlText -Sql $privilegeSql -User 'task_runtime' -Password $runtimePassword -ExtraArguments @('-A', '-t')
    if ((($privileges.Output -join '').Trim()) -ne 't|f|t|f|t|f') {
        throw "Runtime role privilege contract is incorrect: '$(($privileges.Output -join '').Trim())'."
    }
    Write-Ok 'Runtime role has required DML/readiness rights and no database/schema CREATE or history write.'

    Assert-Denied -RuntimePassword $runtimePassword -Name 'CREATE TABLE' -Sql 'CREATE TABLE core.runtime_forbidden (id integer);'
    Assert-Denied -RuntimePassword $runtimePassword -Name 'ALTER TABLE' -Sql 'ALTER TABLE core.objects ADD COLUMN runtime_forbidden integer;'
    Assert-Denied -RuntimePassword $runtimePassword -Name 'DROP TABLE' -Sql 'DROP TABLE work.tasks;'
    Assert-Denied -RuntimePassword $runtimePassword -Name 'UPDATE migration history' -Sql 'UPDATE infrastructure.schema_migrations SET name = name;'

    Invoke-Compose -Profiles @('runtime') -Tail @('up', '-d', '--no-deps', 'task-api') | Out-Null
    $baseUri = "http://127.0.0.1:$apiPort"
    $apiReady = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $live = Invoke-WebRequest -UseBasicParsing -TimeoutSec 3 -Uri "$baseUri/health/live"
            $ready = Invoke-WebRequest -UseBasicParsing -TimeoutSec 3 -Uri "$baseUri/health/ready"
            if ($live.StatusCode -eq 200 -and $ready.StatusCode -eq 200 -and ($ready.Content | ConvertFrom-Json).status -eq 'Ready') {
                $apiReady = $true
                break
            }
        }
        catch { Start-Sleep -Seconds 1 }
    }
    if (-not $apiReady) { throw 'Task.Api container did not reach live=200 and ready=200/Ready.' }
    Write-Ok 'Task.Api uses the runtime role and reports live/ready.'

    $postgresId = ((Invoke-Compose -Tail @('ps', '-q', 'postgres')).Output -join '').Trim()
    $postgresPorts = ((Invoke-Docker -Arguments @('inspect', '--format', '{{json .HostConfig.PortBindings}}', $postgresId)).Output -join '').Trim()
    if ($postgresPorts -ne '{}' -and $postgresPorts -ne 'null') { throw "PostgreSQL publishes host ports: $postgresPorts" }
    $networkInternal = ((Invoke-Docker -Arguments @('network', 'inspect', "${projectName}_database", '--format', '{{.Internal}}')).Output -join '').Trim()
    if ($networkInternal -ne 'true') { throw 'Database network is not internal.' }
    $apiId = ((Invoke-Compose -Profiles @('runtime') -Tail @('ps', '-q', 'task-api')).Output -join '').Trim()
    $apiInspect = ((Invoke-Docker -Arguments @('inspect', $apiId)).Output -join "`n") | ConvertFrom-Json
    $apiHostIp = [string]$apiInspect[0].NetworkSettings.Ports.'8080/tcp'[0].HostIp
    if ($apiHostIp -ne '127.0.0.1') { throw "API validation port is bound to '$apiHostIp', not loopback." }
    Write-Ok 'PostgreSQL has no host port; network is internal; API is loopback-only.'

    $storeGate = Invoke-Compose -Profiles @('tools') -Tail @('run', '--rm', '--no-deps', 'task-container-validation')
    if (($storeGate.Output -join "`n") -notmatch 'code=Passed') { throw 'Production task-store runtime gate failed.' }
    Write-Ok 'Production PostgresTaskAggregateStore passed add/get boundary/save/concurrency under task_runtime.'

    Stop-And-CheckService -Service 'task-worker'
    Stop-And-CheckService -Service 'task-backup-agent'

    Write-Host 'Container packaging verification PASSED.' -ForegroundColor Green
}
catch {
    $failure = $_.Exception.Message
}
finally {
    if (Test-Path -LiteralPath $envFile -PathType Leaf) {
        try {
            Invoke-Compose -Profiles @('runtime', 'tools', 'background') -Tail @('down', '--volumes', '--remove-orphans', '--timeout', '10') -AllowFailure | Out-Null

            $remainingContainers = @((Invoke-Docker -Arguments @('ps', '-aq', '--filter', "label=com.docker.compose.project=$projectName") -AllowFailure).Output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            if ($remainingContainers.Count -gt 0) { Invoke-Docker -Arguments (@('rm', '-f') + $remainingContainers) -AllowFailure | Out-Null }
            $remainingVolumes = @((Invoke-Docker -Arguments @('volume', 'ls', '-q', '--filter', "label=com.docker.compose.project=$projectName") -AllowFailure).Output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            if ($remainingVolumes.Count -gt 0) { Invoke-Docker -Arguments (@('volume', 'rm', '-f') + $remainingVolumes) -AllowFailure | Out-Null }
            $remainingNetworks = @((Invoke-Docker -Arguments @('network', 'ls', '-q', '--filter', "label=com.docker.compose.project=$projectName") -AllowFailure).Output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            if ($remainingNetworks.Count -gt 0) { Invoke-Docker -Arguments (@('network', 'rm') + $remainingNetworks) -AllowFailure | Out-Null }

            $left = @((Invoke-Docker -Arguments @('ps', '-aq', '--filter', "label=com.docker.compose.project=$projectName") -AllowFailure).Output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            $leftVolumes = @((Invoke-Docker -Arguments @('volume', 'ls', '-q', '--filter', "label=com.docker.compose.project=$projectName") -AllowFailure).Output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            $leftNetworks = @((Invoke-Docker -Arguments @('network', 'ls', '-q', '--filter', "label=com.docker.compose.project=$projectName") -AllowFailure).Output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            if ($left.Count -gt 0 -or $leftVolumes.Count -gt 0 -or $leftNetworks.Count -gt 0) {
                throw 'Docker cleanup left project containers, volumes, or networks behind.'
            }
        }
        catch { $cleanupFailure = $_.Exception.Message }
    }

    try {
        if (Test-Path -LiteralPath $tempDirectory) {
            $tmpRootFull = [IO.Path]::GetFullPath($tmpRoot)
            $tempFull = [IO.Path]::GetFullPath($tempDirectory)
            $relativeTemp = [IO.Path]::GetRelativePath($tmpRootFull, $tempFull)
            $parentPrefix = "..$([IO.Path]::DirectorySeparatorChar)"
            if (
                $relativeTemp -in '.', '..' -or
                [IO.Path]::IsPathRooted($relativeTemp) -or
                $relativeTemp.StartsWith($parentPrefix, [StringComparison]::Ordinal)
            ) {
                throw "Refusing to remove temp directory outside work/tmp: $tempFull"
            }
            Remove-Item -LiteralPath $tempFull -Recurse -Force
        }
    }
    catch { $cleanupFailure = $_.Exception.Message }
}

if ($null -ne $cleanupFailure) {
    Write-Host "[FAIL] Cleanup failed: $cleanupFailure" -ForegroundColor Red
    exit 1
}
if ($null -ne $failure) {
    Write-Host "[FAIL] $failure" -ForegroundColor Red
    Write-Host 'Container packaging verification FAILED; the Docker/PostgreSQL gate is not satisfied.' -ForegroundColor Red
    exit 1
}

Write-Ok 'All temporary containers, networks, volumes, and credential files were removed.'
exit 0
