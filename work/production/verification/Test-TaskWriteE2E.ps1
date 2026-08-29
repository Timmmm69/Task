[CmdletBinding()]
param(
    [ValidateSet('Setup', 'SeedAdminDesktop', 'MutateForConflict', 'StopApi', 'StartApi',
        'Verify', 'SeedReadOnlyDesktop', 'Capture', 'Cleanup')]
    [string]$Phase = 'Setup',
    [string]$EvidencePath = '',
    [string]$CaptureName = ''
)

if ($PSVersionTable.PSEdition -ne 'Core') {
    $pwsh = Get-Command pwsh.exe -ErrorAction Stop
    $forward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Phase', $Phase)
    if ($EvidencePath) { $forward += @('-EvidencePath', $EvidencePath) }
    if ($CaptureName) { $forward += @('-CaptureName', $CaptureName) }
    & $pwsh.Source @forward
    exit $LASTEXITCODE
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$productionRoot = Join-Path $repoRoot 'work\production'
# PostgreSQL 16 on Windows rejects non-ASCII data-directory paths, while the repository
# path intentionally contains Cyrillic characters. Runtime data is therefore isolated in
# the current user's local app-data and is removed/restored by the Cleanup phase.
$runtimeRoot = Join-Path $env:LOCALAPPDATA 'TaskE2ERuntime\task-write-e2e'
$statePath = Join-Path $runtimeRoot 'state.json'
$pgBin = 'C:\Program Files\PostgreSQL\16\bin'

function Write-Pass([string]$message) { Write-Host "[PASS] $message" }
function Assert-E2E([bool]$condition, [string]$message) {
    if (-not $condition) { throw "E2E assertion failed: $message" }
    Write-Pass $message
}
function New-Secret([int]$bytes = 24) {
    return [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes($bytes))
        .Replace('+', 'A').Replace('/', 'B').Replace('=', '')
}
function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return ([Net.IPEndPoint]$listener.LocalEndpoint).Port }
    finally { $listener.Stop() }
}
function Save-State($state) {
    [IO.Directory]::CreateDirectory($runtimeRoot) | Out-Null
    [IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
}
function Get-State {
    if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        throw "E2E state is absent. Run -Phase Setup first."
    }
    return Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
}
function Invoke-Psql($state, [string]$sql, [string]$database = '') {
    if (-not $database) { $database = $state.DatabaseName }
    $result = & (Join-Path $pgBin 'psql.exe') -X -v ON_ERROR_STOP=1 -h 127.0.0.1 `
        -p $state.PostgresPort -U postgres -d $database -tA -c $sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL command failed: $($result -join [Environment]::NewLine)" }
    return ($result -join [Environment]::NewLine).Trim()
}
function Wait-Api($state) {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri "$($state.BaseUrl)/health/ready" -TimeoutSec 2
            if ($response.StatusCode -eq 200) { return }
        }
        catch { Start-Sleep -Milliseconds 400 }
    }
    $log = if (Test-Path -LiteralPath $state.ApiStderr) { Get-Content $state.ApiStderr -Tail 30 } else { @() }
    throw "Production HTTPS API did not become ready. $($log -join ' ')"
}
function Start-E2EPostgres($state) {
    if ($state.PostgresPid -and (Get-Process -Id ([int]$state.PostgresPid) -ErrorAction SilentlyContinue)) {
        return $state
    }
    $pgErrorLog = Join-Path $runtimeRoot 'postgres.stderr.log'
    $process = Start-Process -FilePath (Join-Path $pgBin 'postgres.exe') `
        -ArgumentList "-D `"$($state.PostgresData)`" -p $($state.PostgresPort) -h 127.0.0.1" `
        -WindowStyle Hidden -RedirectStandardOutput $state.PostgresLog `
        -RedirectStandardError $pgErrorLog -PassThru
    $state.PostgresPid = $process.Id
    Save-State $state
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        & (Join-Path $pgBin 'pg_isready.exe') -h 127.0.0.1 -p $state.PostgresPort -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { Write-Pass 'Existing PostgreSQL E2E cluster is ready.'; return $state }
        if ($process.HasExited) {
            throw "postgres restart failed: $((Get-Content $pgErrorLog -ErrorAction SilentlyContinue) -join ' ')"
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'PostgreSQL E2E cluster did not become ready within 30 seconds.'
}
function Start-E2EApi($state) {
    $state = Start-E2EPostgres $state
    if ($state.ApiPid -and (Get-Process -Id ([int]$state.ApiPid) -ErrorAction SilentlyContinue)) {
        return $state
    }
    $apiDll = Join-Path $productionRoot 'src\Task.Api\bin\Release\net10.0\Task.Api.dll'
    Assert-E2E (Test-Path -LiteralPath $apiDll) 'Release Task.Api binary exists.'
    $names = @(
        'ASPNETCORE_ENVIRONMENT','ASPNETCORE_URLS','ConnectionStrings__TaskDatabase',
        'Task__Identity__Issuer','Task__Identity__Audience','Task__Identity__SigningKeyReference',
        'Task__Identity__PepperReference','Task__Identity__VerificationKeysDirectory',
        'Kestrel__Certificates__Default__Path','Kestrel__Certificates__Default__Password'
    )
    $old = @{}
    foreach ($name in $names) { $old[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    try {
        $env:ASPNETCORE_ENVIRONMENT = 'Production'
        $env:ASPNETCORE_URLS = $state.BaseUrl
        $env:ConnectionStrings__TaskDatabase = $state.ConnectionString
        $env:Task__Identity__Issuer = 'https://task-e2e.local'
        $env:Task__Identity__Audience = 'task-desktop-e2e'
        $env:Task__Identity__SigningKeyReference = "file:$($state.SigningKeyPath)"
        $env:Task__Identity__PepperReference = "file:$($state.PepperPath)"
        $env:Task__Identity__VerificationKeysDirectory = "file:$($state.VerificationKeysDirectory)"
        $env:Kestrel__Certificates__Default__Path = $state.CertificatePath
        $env:Kestrel__Certificates__Default__Password = $state.CertificatePassword
        $process = Start-Process -FilePath (Get-Command dotnet.exe).Source `
            -ArgumentList @('"' + $apiDll + '"') -WorkingDirectory (Split-Path $apiDll) `
            -WindowStyle Hidden -RedirectStandardOutput $state.ApiStdout `
            -RedirectStandardError $state.ApiStderr -PassThru
        $state.ApiPid = $process.Id
        Save-State $state
    }
    finally {
        foreach ($name in $names) {
            [Environment]::SetEnvironmentVariable($name, $old[$name], 'Process')
        }
    }
    Wait-Api $state
    Write-Pass "Production HTTPS API is ready on $($state.BaseUrl)."
    return $state
}
function Stop-E2EApi($state) {
    if ($state.ApiPid) {
        $process = Get-Process -Id ([int]$state.ApiPid) -ErrorAction SilentlyContinue
        if ($process) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit(10000) | Out-Null
        }
    }
    $state.ApiPid = 0
    Save-State $state
    Write-Pass 'Production API process is stopped.'
    return $state
}
function Invoke-Login($state, [string]$login) {
    $deviceKey = New-Secret 24
    $body = @{
        login = $login
        password = $state.AccountPassword
        device = @{
            deviceKey = $deviceKey; deviceName = 'Task E2E'; platform = 'windows'
            appVersion = '1.0.0'; osVersion = 'Windows E2E'
        }
    } | ConvertTo-Json -Depth 4 -Compress
    $response = Invoke-WebRequest -Method Post -Uri "$($state.BaseUrl)/api/v1/auth/login" `
        -ContentType 'application/json' -Headers @{ 'X-Correlation-ID' = [guid]::NewGuid().ToString('D') } `
        -Body $body -SkipHttpErrorCheck
    Assert-E2E ($response.StatusCode -eq 200) "Real account '$login' can log in."
    $tokens = $response.Content | ConvertFrom-Json
    return [pscustomobject]@{ Tokens = $tokens; DeviceKey = $deviceKey }
}
function Invoke-Authorized($state, [string]$method, [string]$path, [string]$token,
    [string]$body = '', [hashtable]$headers = @{}) {
    $allHeaders = @{ Authorization = "Bearer $token"; 'X-Correlation-ID' = [guid]::NewGuid().ToString('D') }
    foreach ($entry in $headers.GetEnumerator()) { $allHeaders[$entry.Key] = $entry.Value }
    $parameters = @{
        Method = $method; Uri = "$($state.BaseUrl)$path"; Headers = $allHeaders
        SkipHttpErrorCheck = $true
    }
    if ($body) { $parameters.Body = $body; $parameters.ContentType = 'application/json' }
    return Invoke-WebRequest @parameters
}
function Seed-Desktop($state, [string]$login) {
    $session = Invoke-Login $state $login
    $appData = $state.DesktopAppData
    [IO.Directory]::CreateDirectory($appData) | Out-Null
    $settings = @{ version = 1; baseUrl = "$($state.BaseUrl)/" } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $appData 'server-settings.json'), $settings, [Text.UTF8Encoding]::new($false))
    $entry = [ordered]@{
        DeviceId = $session.Tokens.sessionId; OrgId = ''; Login = $login
        DeviceKey = $session.DeviceKey; RefreshToken = $session.Tokens.refreshToken
        SavedAtUtc = [DateTime]::UtcNow; Version = 2
    } | ConvertTo-Json -Compress
    $protected = [Security.Cryptography.ProtectedData]::Protect(
        [Text.Encoding]::UTF8.GetBytes($entry), $null,
        [Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes((Join-Path $appData 'credentials.bin'), $protected)
    Write-Pass "Desktop session was seeded for '$login' through the real login endpoint."
}
function Setup-E2E {
    Assert-E2E (Test-Path -LiteralPath (Join-Path $pgBin 'initdb.exe')) 'PostgreSQL 16 binaries are installed.'
    if (Test-Path -LiteralPath $runtimeRoot) {
        throw "E2E runtime already exists at $runtimeRoot. Run -Phase Cleanup first."
    }
    [IO.Directory]::CreateDirectory($runtimeRoot) | Out-Null
    $dataPath = Join-Path $runtimeRoot 'postgres-data'
    $secretsPath = Join-Path $runtimeRoot 'secrets'
    $verificationKeys = Join-Path $runtimeRoot 'verification-keys'
    [IO.Directory]::CreateDirectory($secretsPath) | Out-Null
    [IO.Directory]::CreateDirectory($verificationKeys) | Out-Null
    $postgresPort = Get-FreePort
    do { $apiPort = Get-FreePort } while ($apiPort -eq $postgresPort)
    $databaseName = 'task_write_e2e'
    $connection = "Host=127.0.0.1;Port=$postgresPort;Database=$databaseName;Username=postgres;SSL Mode=Disable"
    $accountPassword = "Task-E2E-$(New-Secret 18)!aA7"
    $pepper = New-Secret 32
    $pepperPath = Join-Path $secretsPath 'pepper.txt'
    [IO.File]::WriteAllText($pepperPath, $pepper, [Text.UTF8Encoding]::new($false))

    $curve = [Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256')
    $ecdsa = [Security.Cryptography.ECDsa]::Create($curve)
    try {
        $privatePem = $ecdsa.ExportPkcs8PrivateKeyPem()
        $publicPem = $ecdsa.ExportSubjectPublicKeyInfoPem()
    }
    finally { $ecdsa.Dispose() }
    $signingKey = Join-Path $secretsPath 'e2e.pem'
    [IO.File]::WriteAllText($signingKey, $privatePem, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $verificationKeys 'e2e.pem'), $publicPem, [Text.UTF8Encoding]::new($false))

    $certificatePath = Join-Path $secretsPath 'localhost.pfx'
    $certificatePassword = New-Secret 20
    & dotnet dev-certs https -ep $certificatePath -p $certificatePassword | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to export the trusted HTTPS development certificate.' }

    $init = & (Join-Path $pgBin 'initdb.exe') -D $dataPath -U postgres -A trust --encoding=UTF8 --no-locale 2>&1
    if ($LASTEXITCODE -ne 0) { throw "initdb failed: $($init -join ' ')" }
    Write-Pass 'Isolated PostgreSQL 16 cluster was initialized.'
    $pgLog = Join-Path $runtimeRoot 'postgres.log'
    $pgErrorLog = Join-Path $runtimeRoot 'postgres.stderr.log'
    $postgresProcess = Start-Process -FilePath (Join-Path $pgBin 'postgres.exe') `
        -ArgumentList "-D `"$dataPath`" -p $postgresPort -h 127.0.0.1" `
        -WindowStyle Hidden -RedirectStandardOutput $pgLog -RedirectStandardError $pgErrorLog -PassThru
    $readyDeadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        & (Join-Path $pgBin 'pg_isready.exe') -h 127.0.0.1 -p $postgresPort -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { break }
        if ($postgresProcess.HasExited) {
            throw "postgres failed: $((Get-Content $pgErrorLog -ErrorAction SilentlyContinue) -join ' ')"
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $readyDeadline)
    if ($LASTEXITCODE -ne 0) {
        throw 'PostgreSQL 16 did not become ready within 30 seconds.'
    }
    Write-Pass "PostgreSQL 16 is accepting connections on 127.0.0.1:$postgresPort."
    & (Join-Path $pgBin 'createdb.exe') -h 127.0.0.1 -p $postgresPort -U postgres $databaseName
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create the E2E database.' }
    Write-Pass 'Isolated E2E database was created.'

    $desktopAppData = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Task'
    $desktopBackup = Join-Path $runtimeRoot 'preserved-desktop-appdata'
    if (Test-Path -LiteralPath $desktopAppData) {
        Move-Item -LiteralPath $desktopAppData -Destination $desktopBackup
    }
    Write-Pass 'Existing Desktop app-data was preserved for later restoration.'

    $state = [pscustomobject]@{
        PostgresPort = $postgresPort; PostgresData = $dataPath; PostgresLog = $pgLog
        PostgresPid = $postgresProcess.Id
        DatabaseName = $databaseName; ConnectionString = $connection
        BaseUrl = "https://localhost:$apiPort"; ApiPid = 0
        ApiStdout = (Join-Path $runtimeRoot 'api.stdout.log')
        ApiStderr = (Join-Path $runtimeRoot 'api.stderr.log')
        SigningKeyPath = $signingKey; PepperPath = $pepperPath
        VerificationKeysDirectory = $verificationKeys
        CertificatePath = $certificatePath; CertificatePassword = $certificatePassword
        AccountPassword = $accountPassword; AdminLogin = 'task-e2e-admin'
        ReadOnlyLogin = 'task-e2e-reader'; DesktopAppData = $desktopAppData
        DesktopBackup = $desktopBackup; ReplayProbeTitle = "E2E replay probe $([guid]::NewGuid().ToString('N'))"
        UiInitialTitle = "WPF E2E create $([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))"
        UiFinalTitle = "WPF E2E completed $([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))"
        UiTaskId = ''; DirectProbeId = ''
    }
    Save-State $state
    Write-Pass 'Ephemeral E2E state was saved outside the repository.'

    $migratorDll = Join-Path $productionRoot 'src\Task.DatabaseMigrator\bin\Release\net10.0\Task.DatabaseMigrator.dll'
    $oldConnection = $env:ConnectionStrings__TaskDatabase
    $oldBootstrap = @{}
    $bootstrapNames = @('TASK_BOOTSTRAP_ORGANIZATION_CODE','TASK_BOOTSTRAP_ORGANIZATION_NAME',
        'TASK_BOOTSTRAP_TIME_ZONE','TASK_BOOTSTRAP_ADMIN_FIRST_NAME','TASK_BOOTSTRAP_ADMIN_LAST_NAME',
        'TASK_BOOTSTRAP_ADMIN_LOGIN','TASK_BOOTSTRAP_PASSWORD_FILE','TASK_BOOTSTRAP_PEPPER_FILE')
    foreach ($name in $bootstrapNames) { $oldBootstrap[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    $passwordPath = Join-Path $secretsPath 'initial-password.txt'
    [IO.File]::WriteAllText($passwordPath, $accountPassword, [Text.UTF8Encoding]::new($false))
    try {
        $env:ConnectionStrings__TaskDatabase = $connection
        & dotnet $migratorDll apply
        if ($LASTEXITCODE -ne 0) { throw 'Production migrator apply failed.' }
        Write-Pass 'Production migrator applied all migrations.'
        $env:TASK_BOOTSTRAP_ORGANIZATION_CODE = 'task-e2e'
        $env:TASK_BOOTSTRAP_ORGANIZATION_NAME = 'Task E2E Organization'
        $env:TASK_BOOTSTRAP_TIME_ZONE = 'Europe/Minsk'
        $env:TASK_BOOTSTRAP_ADMIN_FIRST_NAME = 'E2E'
        $env:TASK_BOOTSTRAP_ADMIN_LAST_NAME = 'Administrator'
        $env:TASK_BOOTSTRAP_ADMIN_LOGIN = $state.AdminLogin
        $env:TASK_BOOTSTRAP_PASSWORD_FILE = $passwordPath
        $env:TASK_BOOTSTRAP_PEPPER_FILE = $pepperPath
        & dotnet $migratorDll bootstrap-admin
        if ($LASTEXITCODE -ne 0) { throw 'Offline administrator bootstrap failed.' }
        Write-Pass 'Real administrator account was bootstrapped.'
    }
    finally {
        $env:ConnectionStrings__TaskDatabase = $oldConnection
        foreach ($name in $bootstrapNames) {
            [Environment]::SetEnvironmentVariable($name, $oldBootstrap[$name], 'Process')
        }
    }

    Invoke-Psql $state "UPDATE iam.user_accounts SET must_change_password = false WHERE login = '$($state.AdminLogin)';" | Out-Null
    $adminId = Invoke-Psql $state "SELECT id FROM iam.user_accounts WHERE login = '$($state.AdminLogin)';"
    $orgId = Invoke-Psql $state "SELECT organization_id FROM iam.user_accounts WHERE id = '$adminId';"
    $employeeId = [guid]::NewGuid().ToString('D'); $readerId = [guid]::NewGuid().ToString('D'); $roleId = [guid]::NewGuid().ToString('D')
    $readerSql = @"
INSERT INTO core.objects (id, organization_id, object_type, created_at, created_by, updated_at, updated_by)
VALUES ('$employeeId', '$orgId', 'employee_profile', clock_timestamp(), '$adminId', clock_timestamp(), '$adminId');
INSERT INTO org.employee_profiles (id, organization_id, first_name, last_name, display_name, preferred_time_zone)
VALUES ('$employeeId', '$orgId', 'E2E', 'Reader', 'E2E Reader', 'Europe/Minsk');
INSERT INTO core.objects (id, organization_id, object_type, created_at, created_by, updated_at, updated_by)
VALUES ('$readerId', '$orgId', 'user_account', clock_timestamp(), '$adminId', clock_timestamp(), '$adminId');
INSERT INTO iam.user_accounts (id, organization_id, employee_profile_id, login, password_hash,
    password_algorithm, password_parameters, must_change_password)
SELECT '$readerId', organization_id, '$employeeId', '$($state.ReadOnlyLogin)', password_hash,
    password_algorithm, password_parameters, false FROM iam.user_accounts WHERE id = '$adminId';
INSERT INTO iam.authorization_scope_versions (user_account_id, version) VALUES ('$readerId', 1);
INSERT INTO iam.roles (id, organization_id, code, display_name, is_system)
VALUES ('$roleId', '$orgId', 'task_reader', 'Task reader', false);
INSERT INTO iam.role_permissions (role_id, permission_code, effect) VALUES ('$roleId', 'task.read', 'grant');
INSERT INTO iam.user_roles (user_account_id, role_id, granted_by) VALUES ('$readerId', '$roleId', '$adminId');
"@
    Invoke-Psql $state $readerSql | Out-Null
    $state = Start-E2EApi $state

    $adminSession = Invoke-Login $state $state.AdminLogin
    $probeKey = "e2e-create-$([guid]::NewGuid().ToString('N'))"
    $probeBody = @{ title = $state.ReplayProbeTitle; priority = 'normal' } | ConvertTo-Json -Compress
    $created = Invoke-Authorized $state Post '/api/v1/tasks' $adminSession.Tokens.accessToken $probeBody `
        @{ 'Idempotency-Key' = $probeKey }
    Assert-E2E ($created.StatusCode -eq 201) 'Direct create probe returned HTTP 201.'
    $probe = $created.Content | ConvertFrom-Json
    $state.DirectProbeId = $probe.id
    $replayed = Invoke-Authorized $state Post '/api/v1/tasks' $adminSession.Tokens.accessToken $probeBody `
        @{ 'Idempotency-Key' = $probeKey }
    Assert-E2E ($replayed.StatusCode -eq 201) 'Same-key create replay returned the stored HTTP 201 response.'
    $replayHeader = ($replayed.Headers['Idempotency-Replayed'] | Select-Object -First 1).ToString()
    Assert-E2E ($replayHeader -eq 'true') 'Replay is explicitly marked Idempotency-Replayed=true.'
    $probeCounts = Invoke-Psql $state "SELECT (SELECT count(*) FROM work.tasks WHERE id='$($state.DirectProbeId)') || ',' || (SELECT count(*) FROM governance.domain_events WHERE aggregate_id='$($state.DirectProbeId)') || ',' || (SELECT count(*) FROM governance.outbox_messages o JOIN governance.domain_events e ON e.id=o.domain_event_id WHERE e.aggregate_id='$($state.DirectProbeId)') || ',' || (SELECT count(*) FROM governance.audit_entries WHERE object_id='$($state.DirectProbeId)') || ',' || (SELECT count(*) FROM iam.idempotency_records WHERE resource_id='$($state.DirectProbeId)' AND state='completed');"
    Assert-E2E ($probeCounts -eq '1,1,1,1,1') 'Same-key replay produced exactly one row, audit, event, outbox and completed idempotency record.'

    $readerSession = Invoke-Login $state $state.ReadOnlyLogin
    $read = Invoke-Authorized $state Get '/api/v1/tasks' $readerSession.Tokens.accessToken
    Assert-E2E ($read.StatusCode -eq 200) 'Read-only user retains Task.Read access.'
    $denied = Invoke-Authorized $state Post '/api/v1/tasks' $readerSession.Tokens.accessToken `
        (@{ title = 'must be denied' } | ConvertTo-Json -Compress) @{ 'Idempotency-Key' = 'readonly-denied-e2e' }
    Assert-E2E ($denied.StatusCode -eq 403) 'Read-only direct write is denied with HTTP 403.'
    Save-State $state
    Seed-Desktop $state $state.AdminLogin
    Write-Pass "SETUP COMPLETE. UI create title: $($state.UiInitialTitle)"
    Write-Pass "UI final title: $($state.UiFinalTitle)"
}

function Mutate-ForConflict($state) {
    if (-not $state.UiTaskId) {
        $state.UiTaskId = Invoke-Psql $state "SELECT id FROM work.tasks WHERE title = '$($state.UiFinalTitle.Replace("'", "''"))' OR title = '$($state.UiInitialTitle.Replace("'", "''"))' ORDER BY updated_at DESC LIMIT 1;"
        Save-State $state
    }
    Assert-E2E (-not [string]::IsNullOrWhiteSpace($state.UiTaskId)) 'UI-created task is present before conflict injection.'
    $session = Invoke-Login $state $state.AdminLogin
    $current = Invoke-Authorized $state Get "/api/v1/tasks/$($state.UiTaskId)" $session.Tokens.accessToken
    Assert-E2E ($current.StatusCode -eq 200) 'Current task version was loaded for conflict injection.'
    $etag = ($current.Headers.ETag | Select-Object -First 1).ToString()
    $body = @{ title = "Concurrent server edit $([DateTime]::UtcNow.ToString('HHmmss'))" } | ConvertTo-Json -Compress
    $result = Invoke-Authorized $state Patch "/api/v1/tasks/$($state.UiTaskId)" $session.Tokens.accessToken $body `
        @{ 'Idempotency-Key' = "e2e-conflict-$([guid]::NewGuid().ToString('N'))"; 'If-Match' = $etag }
    Assert-E2E ($result.StatusCode -eq 200) 'Concurrent server PATCH advanced the version.'
}

function Verify-E2E($state) {
    if (-not $state.UiTaskId) {
        $escaped = $state.UiFinalTitle.Replace("'", "''")
        $state.UiTaskId = Invoke-Psql $state "SELECT id FROM work.tasks WHERE title='$escaped' ORDER BY updated_at DESC LIMIT 1;"
        Save-State $state
    }
    Assert-E2E (-not [string]::IsNullOrWhiteSpace($state.UiTaskId)) 'UI-created task row exists.'
    $state = Stop-E2EApi $state
    $state = Start-E2EApi $state
    $session = Invoke-Login $state $state.AdminLogin
    $get = Invoke-Authorized $state Get "/api/v1/tasks/$($state.UiTaskId)" $session.Tokens.accessToken
    Assert-E2E ($get.StatusCode -eq 200) 'Final GET succeeds after API restart.'
    $task = $get.Content | ConvertFrom-Json
    Assert-E2E ($task.title -eq $state.UiFinalTitle) 'Final server title matches the UI edit.'
    Assert-E2E ($task.priority -eq 'high') 'Final server priority matches the UI edit.'
    Assert-E2E ($task.status -eq 'completed') 'Final server status is completed.'
    $list = Invoke-Authorized $state Get '/api/v1/tasks' $session.Tokens.accessToken
    $page = $list.Content | ConvertFrom-Json
    Assert-E2E ($page.items.id -contains $state.UiTaskId) 'Final list contains the UI-created task after restart.'
    $counts = Invoke-Psql $state "SELECT (SELECT count(*) FROM governance.audit_entries WHERE object_id='$($state.UiTaskId)') || ',' || (SELECT count(*) FROM governance.domain_events WHERE aggregate_id='$($state.UiTaskId)') || ',' || (SELECT count(*) FROM governance.outbox_messages o JOIN governance.domain_events e ON e.id=o.domain_event_id WHERE e.aggregate_id='$($state.UiTaskId)') || ',' || (SELECT count(*) FROM iam.idempotency_records WHERE resource_id='$($state.UiTaskId)' AND state='completed');"
    $parts = $counts.Split(',') | ForEach-Object { [int]$_ }
    Assert-E2E ($parts[0] -ge 6) 'UI lifecycle emitted durable audit evidence.'
    Assert-E2E ($parts[1] -ge 6) 'UI lifecycle emitted durable domain events.'
    Assert-E2E ($parts[2] -eq $parts[1]) 'Every UI domain event has one outbox message.'
    Assert-E2E ($parts[3] -eq $parts[1]) 'Every successful UI mutation has one completed idempotency record.'
    $safe = [ordered]@{
        postgresVersion = Invoke-Psql $state 'SHOW server_version;'
        migrationVersion = Invoke-Psql $state 'SELECT max(version) FROM infrastructure.schema_migrations;'
        uiTaskId = $state.UiTaskId; finalTitle = $task.title; finalPriority = $task.priority
        finalStatus = $task.status; finalVersion = $task.version
        auditCount = $parts[0]; domainEventCount = $parts[1]
        outboxCount = $parts[2]; completedIdempotencyCount = $parts[3]
        replayProbeCounts = 'task=1,audit=1,event=1,outbox=1,idempotency=1'
        readOnlyGetStatus = 200; readOnlyWriteStatus = 403; apiRestartPersistence = 'PASS'
    }
    if ($EvidencePath) {
        [IO.Directory]::CreateDirectory($EvidencePath) | Out-Null
        [IO.File]::WriteAllText((Join-Path $EvidencePath 'db-assertions.json'),
            ($safe | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
    }
    Write-Pass 'REAL TASK WRITE E2E PASSED.'
}

function Capture-TaskWindow($state) {
    if (-not $EvidencePath -or -not $CaptureName) { throw 'Capture requires -EvidencePath and -CaptureName.' }
    [IO.Directory]::CreateDirectory($EvidencePath) | Out-Null
    Add-Type -AssemblyName System.Drawing
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TaskE2EWindowCapture {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(
        IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
'@
    $handle = [TaskE2EWindowCapture]::GetForegroundWindow()
    $rect = [TaskE2EWindowCapture+RECT]::new()
    Assert-E2E ([TaskE2EWindowCapture]::GetWindowRect($handle, [ref]$rect)) 'Foreground window bounds are available.'
    $width = $rect.Right - $rect.Left; $height = $rect.Bottom - $rect.Top
    Assert-E2E ($width -ge 400 -and $height -ge 300) 'Foreground window is large enough for visual evidence.'
    # GetWindowRect and CopyFromScreen use the caller's DPI coordinate space. Moving the
    # evidence window to the screen origin prevents a full-width WPF window from being
    # clipped when it was launched with a positive X offset on a scaled display.
    $positionOnly = 0x0004 -bor 0x0010 # SWP_NOZORDER | SWP_NOACTIVATE
    Assert-E2E ([TaskE2EWindowCapture]::SetWindowPos(
        $handle, [IntPtr]::Zero, 0, 0, $width, $height, $positionOnly)) 'Foreground window is positioned for complete capture.'
    Start-Sleep -Milliseconds 150
    Assert-E2E ([TaskE2EWindowCapture]::GetWindowRect($handle, [ref]$rect)) 'Capture window bounds are refreshed.'
    $width = $rect.Right - $rect.Left; $height = $rect.Bottom - $rect.Top
    $dpiScale = [TaskE2EWindowCapture]::GetDpiForWindow($handle) / 96.0
    $captureWidth = [int][Math]::Round($width * $dpiScale)
    $captureHeight = [int][Math]::Round($height * $dpiScale)
    $bitmap = [Drawing.Bitmap]::new($captureWidth, $captureHeight)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size) }
        finally { $graphics.Dispose() }
        $path = Join-Path $EvidencePath ($CaptureName + '.png')
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
    Write-Pass "Captured $CaptureName.png."
}

function Cleanup-E2E($state) {
    $state = Stop-E2EApi $state
    $desktopParent = [IO.Path]::GetFullPath((Split-Path $state.DesktopAppData -Parent))
    $expectedDesktop = [IO.Path]::Combine($desktopParent, 'Task')
    if ([IO.Path]::GetFullPath($state.DesktopAppData) -ne $expectedDesktop) {
        throw 'Refusing to clean an unexpected desktop app-data path.'
    }
    if (Test-Path -LiteralPath $state.DesktopAppData) {
        Remove-Item -LiteralPath $state.DesktopAppData -Recurse -Force
    }
    if (Test-Path -LiteralPath $state.DesktopBackup) {
        Move-Item -LiteralPath $state.DesktopBackup -Destination $state.DesktopAppData
    }
    & (Join-Path $pgBin 'pg_ctl.exe') -D $state.PostgresData -m fast -w stop | Out-Null
    $runtimeParent = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'TaskE2ERuntime'))
    $resolvedRuntime = [IO.Path]::GetFullPath($runtimeRoot)
    $runtimePrefix = $runtimeParent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedRuntime.StartsWith($runtimePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to clean an unexpected E2E runtime path.'
    }
    if (Test-Path -LiteralPath $resolvedRuntime) {
        Remove-Item -LiteralPath $resolvedRuntime -Recurse -Force
    }
    Write-Pass 'PostgreSQL stopped, original Desktop app-data restored, and E2E runtime removed.'
}

switch ($Phase) {
    'Setup' { Setup-E2E }
    'SeedAdminDesktop' { $state = Get-State; Seed-Desktop $state $state.AdminLogin }
    'MutateForConflict' { Mutate-ForConflict (Get-State) }
    'StopApi' { Stop-E2EApi (Get-State) | Out-Null }
    'StartApi' { Start-E2EApi (Get-State) | Out-Null }
    'Verify' { Verify-E2E (Get-State) }
    'SeedReadOnlyDesktop' { $state = Get-State; Seed-Desktop $state $state.ReadOnlyLogin }
    'Capture' { Capture-TaskWindow (Get-State) }
    'Cleanup' { Cleanup-E2E (Get-State) }
}
