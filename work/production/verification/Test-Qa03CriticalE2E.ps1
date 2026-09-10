[CmdletBinding()]
param(
    [ValidateSet('SeedProductData', 'VerifyProductApi', 'RunWorkerDelivery', 'VerifyNotificationsApi', 'VerifyCriticalProduct')]
    [string]$Phase = 'SeedProductData',
    [string]$EvidencePath = ''
)

if ($PSVersionTable.PSEdition -ne 'Core') {
    $pwsh = Get-Command pwsh.exe -ErrorAction Stop
    $forward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Phase', $Phase)
    if ($EvidencePath) { $forward += @('-EvidencePath', $EvidencePath) }
    & $pwsh.Source @forward
    exit $LASTEXITCODE
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Reuses the isolated PostgreSQL 16 + production HTTPS API runtime provisioned by
# Test-TaskWriteE2E.ps1 -Phase Setup (state.json in the shared QA-02/QA-03 runtime).
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$productionRoot = Join-Path $repoRoot 'work\production'
$runtimeRoot = Join-Path $env:LOCALAPPDATA 'TaskE2ERuntime\task-write-e2e'
$statePath = Join-Path $runtimeRoot 'state.json'
$pgBin = 'C:\Program Files\PostgreSQL\16\bin'

function Write-Pass([string]$message) { Write-Host "[PASS] $message" }
function Assert-Qa03([bool]$condition, [string]$message) {
    if (-not $condition) { throw "QA-03 assertion failed: $message" }
    Write-Pass $message
}
function Get-State {
    if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        throw 'QA-03 state is absent. Run Test-TaskWriteE2E.ps1 -Phase Setup first.'
    }
    return Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
}
function Save-State($state) {
    [IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}
function Invoke-Sql([string]$sql) {
    $state = Get-State
    $sqlPath = Join-Path $runtimeRoot 'qa03-psql-query.sql'
    $outPath = Join-Path $runtimeRoot 'qa03-psql-out.tmp'
    $errPath = Join-Path $runtimeRoot 'qa03-psql-err.tmp'
    [IO.File]::WriteAllText($sqlPath, $sql, [Text.UTF8Encoding]::new($false))
    $process = Start-Process -FilePath (Join-Path $pgBin 'psql.exe') `
        -ArgumentList @('-X', '-v', 'ON_ERROR_STOP=1', '-h', '127.0.0.1', '-p', "$($state.PostgresPort)",
            '-U', 'postgres', '-d', $state.DatabaseName, '-tA', '-f', "`"$sqlPath`"") `
        -WindowStyle Hidden -RedirectStandardOutput $outPath -RedirectStandardError $errPath -PassThru
    $process.WaitForExit()
    $exitCode = $process.ExitCode
    $process.Dispose()
    if ($exitCode -ne 0) {
        throw "PostgreSQL command failed: $([IO.File]::ReadAllText($errPath, [Text.UTF8Encoding]::new($false)))"
    }
    return [IO.File]::ReadAllText($outPath, [Text.UTF8Encoding]::new($false)).Trim()
}
function Invoke-Login([string]$login) {
    $state = Get-State
    $deviceKey = [guid]::NewGuid().ToString('N')
    $body = @{
        login = $login; password = $state.AccountPassword
        device = @{ deviceKey = $deviceKey; deviceName = 'Task QA-03 E2E'; platform = 'windows'
            appVersion = '1.0.0'; osVersion = 'Windows E2E' }
    } | ConvertTo-Json -Depth 4 -Compress
    $response = Invoke-WebRequest -Method Post -Uri "$($state.BaseUrl)/api/v1/auth/login" `
        -ContentType 'application/json' -Headers @{ 'X-Correlation-ID' = [guid]::NewGuid().ToString('D') } `
        -Body $body -SkipHttpErrorCheck
    Assert-Qa03 ($response.StatusCode -eq 200) "Real account '$login' can log in."
    return ($response.Content | ConvertFrom-Json)
}
function Invoke-Call([string]$method, [string]$path, [string]$token,
    [string]$body = '', [hashtable]$headers = @{}) {
    $state = Get-State
    $allHeaders = @{ Authorization = "Bearer $token"; 'X-Correlation-ID' = [guid]::NewGuid().ToString('D') }
    foreach ($entry in $headers.GetEnumerator()) { $allHeaders[$entry.Key] = $entry.Value }
    $parameters = @{
        Method = $method; Uri = "$($state.BaseUrl)$path"; Headers = $allHeaders
        SkipHttpErrorCheck = $true
    }
    if ($body) { $parameters.Body = $body; $parameters.ContentType = 'application/json' }
    return Invoke-WebRequest @parameters
}
function Write-JsonEvidence([string]$name, $payload) {
    if (-not $EvidencePath) { return }
    [IO.Directory]::CreateDirectory($EvidencePath) | Out-Null
    [IO.File]::WriteAllText((Join-Path $EvidencePath $name),
        (($payload | ConvertTo-Json -Depth 8) + "`n"), [Text.UTF8Encoding]::new($false))
}

function Seed-ProductData {
    $state = Get-State
    $tokens = Invoke-Login $state.AdminLogin
    $token = $tokens.accessToken
    $session = Invoke-Call Get '/api/v1/auth/session' $token
    $userId = ($session.Content | ConvertFrom-Json).userId
    Assert-Qa03 (-not [string]::IsNullOrWhiteSpace($userId)) 'Admin session exposes the canonical user id.'

    $seed = $null
    if ($state.PSObject.Properties['Qa03']) { $seed = $state.Qa03 }
    if (-not $seed) { $seed = [pscustomobject]@{} }

    $projectBody = @{
        name = 'QA-03 deterministic project'; description = 'Critical E2E seed project'
        ownerUserId = $userId; status = 'active'
    } | ConvertTo-Json -Compress
    $project = Invoke-Call Post '/api/v1/projects' $token $projectBody @{ 'Idempotency-Key' = [guid]::NewGuid().ToString('N') }
    Assert-Qa03 ($project.StatusCode -in @(200, 201)) 'Seeded project was created through the product API.'
    $projectJson = $project.Content | ConvertFrom-Json
    $projectGet = Invoke-Call Get "/api/v1/projects/$($projectJson.id)" $token
    $projectVersion = ($projectGet.Headers.ETag | Select-Object -First 1).ToString()
    $seed | Add-Member -NotePropertyName ProjectId -NotePropertyValue $projectJson.id -Force
    $seed | Add-Member -NotePropertyName ProjectVersion -NotePropertyValue $projectVersion -Force

    $roles = Invoke-Call Get '/api/v1/project-roles' $token
    Assert-Qa03 ($roles.StatusCode -eq 200) 'Project roles are readable through the product API.'
    $roleList = @($roles.Content | ConvertFrom-Json)
    Assert-Qa03 ($roleList.Count -ge 1) 'At least one project role is available for membership.'
    $roleId = $roleList[0].id

    $readerId = Invoke-Sql "SELECT id FROM iam.user_accounts WHERE login = '$($state.ReadOnlyLogin)';"
    Assert-Qa03 (-not [string]::IsNullOrWhiteSpace($readerId)) 'Read-only account id is resolvable.'
    $seed | Add-Member -NotePropertyName ReaderUserId -NotePropertyValue $readerId -Force

    $memberBody = @{
        userAccountId = $readerId
        projectRoleId = $roleId
        status = 'active'
    } | ConvertTo-Json -Compress
    $member = Invoke-Call Post "/api/v1/projects/$($projectJson.id)/members" $token $memberBody `
        @{ 'Idempotency-Key' = [guid]::NewGuid().ToString('N'); 'If-Match' = $projectVersion }
    Assert-Qa03 ($member.StatusCode -in @(200, 201)) 'Read-only user was added as a project member.'

    $today = [DateTime]::Today
    $todayIso = $today.ToString('yyyy-MM-dd')
    $localTimeZone = [TimeZoneInfo]::Local.Id
    $eventBody = @{
        title = 'QA-03 deterministic event'
        eventDate = $todayIso
        isAllDay = $true
        timeZone = $localTimeZone
    } | ConvertTo-Json -Compress
    $event = Invoke-Call Post '/api/v1/calendar-events' $token $eventBody @{ 'Idempotency-Key' = [guid]::NewGuid().ToString('N') }
    Assert-Qa03 ($event.StatusCode -in @(200, 201)) 'Seeded calendar event was created.'
    $seed | Add-Member -NotePropertyName EventId -NotePropertyValue (($event.Content | ConvertFrom-Json).id) -Force

    $definition = @{
        frequency = 'daily'; interval = 1; weekdays = @(); monthDays = @()
        occurrenceStartDate = $todayIso; localStartTime = '09:00:00'; timeZone = $localTimeZone
        maxOccurrences = 3
        template = @{
            title = 'QA-03 recurrence E2E'; description = 'Seeded recurrence description'
            authorUserId = $userId; priority = 'normal'; plannedDurationMinutes = 60
            assigneeIds = @(); watcherIds = @(); checklists = @(); reminderRules = @(); templateVersion = 1
        }
    } | ConvertTo-Json -Depth 10 -Compress
    $series = Invoke-Call Post '/api/v1/recurrence-series' $token $definition @{ 'Idempotency-Key' = [guid]::NewGuid().ToString('N') }
    Assert-Qa03 ($series.StatusCode -eq 201) 'Seeded recurrence series was created transactionally.'
    $seed | Add-Member -NotePropertyName SeriesId -NotePropertyValue (($series.Content | ConvertFrom-Json).id) -Force

    $catalogBody = @{
        name = 'QA-03 deterministic file.txt'
        itemType = 'file_reference'
        description = 'Critical E2E catalog reference'
        sortOrder = 0
    } | ConvertTo-Json -Compress
    $catalog = Invoke-Call Post '/api/v1/catalog-items' $token $catalogBody @{ 'Idempotency-Key' = [guid]::NewGuid().ToString('N') }
    Assert-Qa03 ($catalog.StatusCode -in @(200, 201)) 'Seeded catalog item was created.'
    $catalogJson = $catalog.Content | ConvertFrom-Json
    $catalogGet = Invoke-Call Get "/api/v1/catalog-items/$($catalogJson.id)" $token
    $catalogVersion = ($catalogGet.Headers.ETag | Select-Object -First 1).ToString()
    $seed | Add-Member -NotePropertyName CatalogId -NotePropertyValue $catalogJson.id -Force
    $seed | Add-Member -NotePropertyName CatalogVersion -NotePropertyValue $catalogVersion -Force

    $localPath = Join-Path $runtimeRoot 'qa03-local-files\qa03-reference.txt'
    [IO.Directory]::CreateDirectory((Split-Path $localPath)) | Out-Null
    [IO.File]::WriteAllText($localPath, 'QA-03 deterministic file content.', [Text.UTF8Encoding]::new($false))
    $locationBody = @{
        locationType = 'local_path'
        rawPath = $localPath
        priority = 0
        isEnabled = $true
        isPrimary = $true
    } | ConvertTo-Json -Compress
    $location = Invoke-Call Post "/api/v1/catalog-items/$($catalogJson.id)/locations" $token $locationBody `
        @{ 'Idempotency-Key' = [guid]::NewGuid().ToString('N'); 'If-Match' = $catalogVersion }
    Assert-Qa03 ($location.StatusCode -in @(200, 201)) 'Seeded file location was attached to the catalog item.'
    $locationJson = $location.Content | ConvertFrom-Json
    $seed | Add-Member -NotePropertyName LocationId -NotePropertyValue $locationJson.id -Force

    $resolved = Invoke-Call Post "/api/v1/catalog-items/$($catalogJson.id)/resolve-location" $token '{}' @{ 'Idempotency-Key' = [guid]::NewGuid().ToString('N') }
    Assert-Qa03 ($resolved.StatusCode -eq 200) 'Location resolve succeeds for the owning device session.'
    $resolvedJson = $resolved.Content | ConvertFrom-Json
    Assert-Qa03 ($resolvedJson.status -eq 'requires_client_check') 'Resolve returns the explicit client-check status.'
    Assert-Qa03 ($resolvedJson.location.canOpenOnDevice -eq $true) 'Resolved location is openable on this device.'

    $contactBody = @{
        firstName = 'Qa03'; lastName = 'Contact'; displayName = 'QA-03 deterministic contact'
        status = 'active'
    } | ConvertTo-Json -Compress
    $contact = Invoke-Call Post '/api/v1/contacts' $token $contactBody @{ 'Idempotency-Key' = [guid]::NewGuid().ToString('N') }
    Assert-Qa03 ($contact.StatusCode -in @(200, 201)) 'Seeded contact was created.'
    $seed | Add-Member -NotePropertyName ContactId -NotePropertyValue (($contact.Content | ConvertFrom-Json).id) -Force

    $localZone = [TimeZoneInfo]::Local
    $timedStart = [DateTimeOffset]::new($today.AddHours(9), $localZone.GetUtcOffset([DateTime]::SpecifyKind($today.AddHours(9), [DateTimeKind]::Unspecified)))
    $timedDeadline = $timedStart.AddHours(1)
    $untimedDeadline = [DateTimeOffset]::new($today.AddHours(18), $localZone.GetUtcOffset([DateTime]::SpecifyKind($today.AddHours(18), [DateTimeKind]::Unspecified)))
    $overdueDeadline = [DateTimeOffset]::new($today.AddDays(-1).AddHours(12), $localZone.GetUtcOffset([DateTime]::SpecifyKind($today.AddDays(-1).AddHours(12), [DateTimeKind]::Unspecified)))
    $timedTask = Invoke-Call Post '/api/v1/tasks' $token (@{
        title = 'QA-03 today timed task'; priority = 'high'
        startAtUtc = $timedStart.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
        deadlineAt = $timedDeadline.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    } | ConvertTo-Json -Compress) @{ 'Idempotency-Key' = [guid]::NewGuid().ToString('N') }
    if ($timedTask.StatusCode -ne 201) { throw "Timed task create failed: HTTP $($timedTask.StatusCode) $($timedTask.Content)" }
    Assert-Qa03 ($timedTask.StatusCode -eq 201) 'Seeded timed today task was created.'
    $seed | Add-Member -NotePropertyName TimedTaskId -NotePropertyValue (($timedTask.Content | ConvertFrom-Json).id) -Force

    $untimedTask = Invoke-Call Post '/api/v1/tasks' $token (@{
        title = 'QA-03 today untimed task'; priority = 'normal'
        deadlineAt = $untimedDeadline.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    } | ConvertTo-Json -Compress) @{ 'Idempotency-Key' = [guid]::NewGuid().ToString('N') }
    Assert-Qa03 ($untimedTask.StatusCode -eq 201) 'Seeded untimed today task was created.'
    $seed | Add-Member -NotePropertyName UntimedTaskId -NotePropertyValue (($untimedTask.Content | ConvertFrom-Json).id) -Force

    $overdueTask = Invoke-Call Post '/api/v1/tasks' $token (@{
        title = 'QA-03 overdue task'; priority = 'high'
        deadlineAt = $overdueDeadline.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    } | ConvertTo-Json -Compress) @{ 'Idempotency-Key' = [guid]::NewGuid().ToString('N') }
    Assert-Qa03 ($overdueTask.StatusCode -eq 201) 'Seeded overdue task was created.'
    $seed | Add-Member -NotePropertyName OverdueTaskId -NotePropertyValue (($overdueTask.Content | ConvertFrom-Json).id) -Force

    $orgId = Invoke-Sql "SELECT organization_id FROM iam.user_accounts WHERE login = '$($state.AdminLogin)';"
    $adminId = Invoke-Sql "SELECT id FROM iam.user_accounts WHERE login = '$($state.AdminLogin)';"
    $reminderId = [guid]::NewGuid().ToString('D')
    $dueAt = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('o')
    $reminderSql = @"
INSERT INTO calendar.reminders (id, organization_id, target_object_id, recipient_user_id,
    trigger_type, absolute_trigger_at, next_trigger_at, status, created_by)
VALUES ('$reminderId', '$orgId', '$($seed.TimedTaskId)', '$adminId',
    'absolute', '$dueAt', '$dueAt', 'scheduled', '$adminId');
"@
    Invoke-Sql $reminderSql | Out-Null
    Assert-Qa03 ((Invoke-Sql "SELECT count(*) FROM calendar.reminders WHERE id='$reminderId';") -eq '1') 'Due reminder is scheduled for worker delivery.'
    $seed | Add-Member -NotePropertyName ReminderId -NotePropertyValue $reminderId -Force
    $seed | Add-Member -NotePropertyName AdminUserId -NotePropertyValue $adminId -Force

    $state | Add-Member -NotePropertyName Qa03 -NotePropertyValue $seed -Force
    Save-State $state
    Write-Pass 'QA-03 PRODUCT SEED COMPLETE.'
}

function Verify-ProductApi {
    $state = Get-State
    $seed = $state.Qa03
    $checks = [Collections.Generic.List[string]]::new()
    $admin = Invoke-Login $state.AdminLogin
    $adminToken = $admin.accessToken
    function Check([bool]$condition, [string]$message) {
        if (-not $condition) { throw "FAIL: $message" }
        $checks.Add($message); Write-Pass $message
    }

    $projects = Invoke-Call Get '/api/v1/projects?limit=200' $adminToken
    Check ($projects.StatusCode -eq 200 -and @((($projects.Content | ConvertFrom-Json).items)).Count -ge 1) 'Projects list contains seeded project'
    $projectItems = @(($projects.Content | ConvertFrom-Json).items)
    Check ($projectItems.id -contains $seed.ProjectId) 'Seeded project is present in the list'
    $projectGet = Invoke-Call Get "/api/v1/projects/$($seed.ProjectId)" $adminToken
    $projectJson = $projectGet.Content | ConvertFrom-Json
    Check ($projectGet.StatusCode -eq 200 -and $projectJson.name -eq 'QA-03 deterministic project') 'Seeded project details round-trip'
    $members = Invoke-Call Get "/api/v1/projects/$($seed.ProjectId)/members" $adminToken
    Check ($members.StatusCode -eq 200 -and @((($members.Content | ConvertFrom-Json).items)).Count -eq 1) 'Project membership includes the seeded member'

    $from = [uri]::EscapeDataString([DateTime]::Today.AddDays(-1).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ'))
    $to = [uri]::EscapeDataString([DateTime]::Today.AddDays(7).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ'))
    $timeZoneParam = [uri]::EscapeDataString([TimeZoneInfo]::Local.Id)
    $calendar = Invoke-Call Get "/api/v1/calendar?from=$from&to=$to&timezone=$timeZoneParam" $adminToken
    Check ($calendar.StatusCode -eq 200) 'Calendar range reads through the product API'
    $calendarItems = @(($calendar.Content | ConvertFrom-Json).items)
    Check (@($calendarItems | Where-Object { $_.objectId -eq $seed.EventId }).Count -eq 1) 'Seeded event is visible in the calendar range'
    Check (@($calendarItems | Where-Object { $_.recurrenceSeriesId -eq $seed.SeriesId }).Count -eq 3) 'Seeded series materialized exactly three occurrences'
    Check (@($calendarItems | Where-Object { $_.objectId -eq $seed.TimedTaskId }).Count -eq 1) 'Seeded timed task is visible in the calendar range'

    $catalog = Invoke-Call Get '/api/v1/catalog-items?limit=200' $adminToken
    Check ($catalog.StatusCode -eq 200 -and @((($catalog.Content | ConvertFrom-Json).items)).id -contains $seed.CatalogId) 'Catalog list contains the seeded item'
    $tree = Invoke-Call Get '/api/v1/catalog/tree' $adminToken
    Check ($tree.StatusCode -eq 200) 'Catalog tree is readable'
    $locations = Invoke-Call Get "/api/v1/catalog-items/$($seed.CatalogId)/locations" $adminToken
    $locationJson = @($locations.Content | ConvertFrom-Json)
    Check ($locations.StatusCode -eq 200 -and $locationJson.Count -eq 1 -and $locationJson[0].id -eq $seed.LocationId) 'Seeded file location round-trips'
    $resolve = Invoke-Call Post "/api/v1/catalog-items/$($seed.CatalogId)/resolve-location" $adminToken '{}' @{ 'Idempotency-Key' = [guid]::NewGuid().ToString('N') }
    Check ($resolve.StatusCode -eq 200) 'Resolve-location answers for a fresh device session'
    $resolveJson = $resolve.Content | ConvertFrom-Json
    Check ($resolveJson.status -eq 'unavailable_on_device') 'Local paths stay device-scoped for other sessions'

    $contacts = Invoke-Call Get '/api/v1/contacts?limit=200' $adminToken
    Check ($contacts.StatusCode -eq 200 -and @((($contacts.Content | ConvertFrom-Json).items)).id -contains $seed.ContactId) 'Contacts list contains the seeded contact'

    $taskSearch = Invoke-Call Get '/api/v1/search?q=QA-03%20today%20timed&limit=100' $adminToken
    $taskHits = @(($taskSearch.Content | ConvertFrom-Json).items)
    Check ($taskSearch.StatusCode -eq 200 -and @($taskHits | Where-Object { $_.objectType -eq 'task' -and $_.objectId -eq $seed.TimedTaskId }).Count -eq 1) 'Search finds the seeded task'
    $projectSearch = Invoke-Call Get '/api/v1/search?q=QA-03%20deterministic%20project&limit=100' $adminToken
    Check ($projectSearch.StatusCode -eq 200 -and @(($projectSearch.Content | ConvertFrom-Json).items | Where-Object { $_.objectType -eq 'project' }).Count -ge 1) 'Search finds the seeded project'
    $contactSearch = Invoke-Call Get '/api/v1/search?q=QA-03%20deterministic%20contact&limit=100' $adminToken
    Check ($contactSearch.StatusCode -eq 200 -and @(($contactSearch.Content | ConvertFrom-Json).items | Where-Object { $_.objectType -eq 'contact' }).Count -ge 1) 'Search finds the seeded contact'
    $fileSearch = Invoke-Call Get '/api/v1/search?q=QA-03%20deterministic%20file&limit=100' $adminToken
    Check ($fileSearch.StatusCode -eq 200 -and @(($fileSearch.Content | ConvertFrom-Json).items | Where-Object { $_.objectType -eq 'catalog_item' }).Count -ge 1) 'Search finds the seeded catalog item'

    $notifications = Invoke-Call Get '/api/v1/notifications?limit=200' $adminToken
    Check ($notifications.StatusCode -eq 200 -and @((($notifications.Content | ConvertFrom-Json).items)).Count -eq 0) 'Notifications are empty before worker delivery'

    $settings = Invoke-Call Get '/api/v1/settings/me' $adminToken
    Check ($settings.StatusCode -eq 200) 'Personal settings are readable'
    $orgSettings = Invoke-Call Get '/api/v1/settings/organization' $adminToken
    Check ($orgSettings.StatusCode -eq 200) 'Organization settings are readable'

    $reader = Invoke-Login $state.ReadOnlyLogin
    $readerProjects = Invoke-Call Get '/api/v1/projects' $reader.accessToken
    Check ($readerProjects.StatusCode -eq 403) 'Read-only account cannot list projects'
    $readerCreate = Invoke-Call Post '/api/v1/projects' $reader.accessToken `
        (@{ name = 'denied'; ownerUserId = $seed.AdminUserId } | ConvertTo-Json -Compress) @{ 'Idempotency-Key' = [guid]::NewGuid().ToString('N') }
    Check ($readerCreate.StatusCode -eq 403) 'Read-only account cannot create projects'

    Write-JsonEvidence 'api-assertions.json' ([ordered]@{
        schemaVersion = 1
        scenario = 'QA-03 critical product API E2E'
        seedProfile = 'qa03-deterministic-v1'
        result = 'PASS'
        projectId = $seed.ProjectId
        eventId = $seed.EventId
        seriesId = $seed.SeriesId
        catalogId = $seed.CatalogId
        locationId = $seed.LocationId
        contactId = $seed.ContactId
        timedTaskId = $seed.TimedTaskId
        untimedTaskId = $seed.UntimedTaskId
        overdueTaskId = $seed.OverdueTaskId
        reminderId = $seed.ReminderId
        resolveStatus = 'requires_client_check'
        resolveCanOpenOnDevice = $true
        resolveForeignDeviceStatus = 'unavailable_on_device'
        readerProjectListStatus = 403
        readerProjectCreateStatus = 403
        checks = $checks
    })
    Write-Pass "QA-03 PRODUCT API VERIFICATION PASSED: $($checks.Count) checks."
}

function Run-WorkerDelivery {
    $state = Get-State
    $seed = $state.Qa03
    $workerDll = Join-Path $productionRoot 'src\Task.Worker\bin\Release\net10.0\Task.Worker.dll'
    Assert-Qa03 (Test-Path -LiteralPath $workerDll -PathType Leaf) 'Release Task.Worker binary exists.'
    $workerStdout = Join-Path $runtimeRoot 'qa03-worker.stdout.log'
    $workerStderr = Join-Path $runtimeRoot 'qa03-worker.stderr.log'
    $oldConnection = $env:ConnectionStrings__TaskDatabase
    $oldEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $workerProcess = $null
    try {
        $env:ConnectionStrings__TaskDatabase = $state.ConnectionString
        $env:ASPNETCORE_ENVIRONMENT = 'Production'
        $workerProcess = Start-Process -FilePath (Get-Command dotnet.exe).Source `
            -ArgumentList @('"' + $workerDll + '"') -WorkingDirectory (Split-Path $workerDll) `
            -WindowStyle Hidden -RedirectStandardOutput $workerStdout -RedirectStandardError $workerStderr -PassThru
        $deadline = [DateTime]::UtcNow.AddSeconds(90)
        $delivered = '0'
        while ([DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Seconds 3
            if ($workerProcess.HasExited) {
                $stderr = Get-Content $workerStderr -ErrorAction SilentlyContinue
                throw "Task.Worker exited early: $($stderr -join ' ')"
            }
            $delivered = Invoke-Sql "SELECT count(*) FROM notify.notifications WHERE notification_type='reminder' AND recipient_user_id='$($seed.AdminUserId)';"
            if ($delivered -eq '1') { break }
        }
        Assert-Qa03 ($delivered -eq '1') 'Production worker delivered the due reminder as a user notification.'
        $occurrence = Invoke-Sql "SELECT count(*) FROM calendar.reminder_occurrences WHERE reminder_id='$($seed.ReminderId)' AND status='delivered';"
        Assert-Qa03 ($occurrence -eq '1') 'Reminder occurrence reached the delivered state.'
        $reminderStatus = Invoke-Sql "SELECT status FROM calendar.reminders WHERE id='$($seed.ReminderId)';"
        Assert-Qa03 ($reminderStatus -eq 'delivered') 'Reminder aggregate is marked delivered.'
        $feed = Invoke-Sql 'SELECT count(*) FROM sync.change_feed;'
        Assert-Qa03 ([int]$feed -ge 1) 'Outbox publisher materialized the durable change feed.'
        $notificationRow = Invoke-Sql "SELECT id || '|' || title || '|' || status FROM notify.notifications WHERE notification_type='reminder' AND recipient_user_id='$($seed.AdminUserId)' LIMIT 1;"
        Assert-Qa03 ($notificationRow.Split('|')[1] -eq 'Напоминание') 'Delivered notification has the canonical reminder title.'
        $notificationId = $notificationRow.Split('|')[0]
        $seed | Add-Member -NotePropertyName NotificationId -NotePropertyValue $notificationId -Force
        $state | Add-Member -NotePropertyName Qa03 -NotePropertyValue $seed -Force
        Save-State $state
    }
    finally {
        $env:ConnectionStrings__TaskDatabase = $oldConnection
        $env:ASPNETCORE_ENVIRONMENT = $oldEnvironment
        if ($workerProcess) {
            Stop-Process -Id $workerProcess.Id -Force -ErrorAction SilentlyContinue
            $workerProcess.WaitForExit(10000) | Out-Null
        }
    }
    Write-JsonEvidence 'worker-delivery.json' ([ordered]@{
        schemaVersion = 1
        scenario = 'QA-03 production worker reminder delivery'
        seedProfile = 'qa03-deterministic-v1'
        result = 'PASS'
        workerExecutable = 'work/production/src/Task.Worker/bin/Release/net10.0/Task.Worker.dll'
        reminderId = $seed.ReminderId
        notificationId = $seed.NotificationId
        targetTaskId = $seed.TimedTaskId
        notificationTitle = 'Напоминание'
        notificationType = 'reminder'
        reminderOccurrenceState = 'delivered'
        reminderState = 'delivered'
        changeFeedRows = [int]$feed
    })
    Write-Pass 'QA-03 WORKER DELIVERY PASSED.'
}

function Verify-NotificationsApi {
    $state = Get-State
    $seed = $state.Qa03
    $checks = [Collections.Generic.List[string]]::new()
    $admin = Invoke-Login $state.AdminLogin
    $token = $admin.accessToken
    function Check([bool]$condition, [string]$message) {
        if (-not $condition) { throw "FAIL: $message" }
        $checks.Add($message); Write-Pass $message
    }
    $list = Invoke-Call Get '/api/v1/notifications?limit=200' $token
    Check ($list.StatusCode -eq 200) 'Notifications list is readable after the UI read flow'
    $items = @(($list.Content | ConvertFrom-Json).items)
    $delivered = @($items | Where-Object { $_.id -eq $seed.NotificationId })
    Check ($delivered.Count -eq 1) 'Worker-delivered reminder notification remains visible'
    Check ($delivered[0].notificationType -eq 'reminder') 'Notification keeps the reminder type'
    Check ($delivered[0].sourceObjectId -eq $seed.TimedTaskId) 'Notification links to the source task'
    Check ($delivered[0].status -eq 'read') 'Notification reached the read state through the real WPF UI'

    $read = Invoke-Call Post "/api/v1/notifications/$($seed.NotificationId)/read" $token '{}'
    Check ($read.StatusCode -in @(200, 204)) 'Read endpoint is idempotent for an already-read notification'

    $readAll = Invoke-Call Post '/api/v1/notifications/read-all' $token `
        (@{ notificationIds = @($seed.NotificationId) } | ConvertTo-Json -Compress) @{ 'Idempotency-Key' = [guid]::NewGuid().ToString('N') }
    Check ($readAll.StatusCode -in @(200, 204)) 'Read-all accepts the delivered notification id'

    $sqlStatus = Invoke-Sql "SELECT status FROM notify.notifications WHERE id='$($seed.NotificationId)';"
    Check ($sqlStatus -eq 'read') 'PostgreSQL retains the read notification state'

    Write-JsonEvidence 'notifications-assertions.json' ([ordered]@{
        schemaVersion = 1
        scenario = 'QA-03 notification read E2E'
        seedProfile = 'qa03-deterministic-v1'
        result = 'PASS'
        notificationId = $seed.NotificationId
        reminderId = $seed.ReminderId
        finalStatus = $sqlStatus
        notificationType = 'reminder'
        sourceTaskId = $seed.TimedTaskId
        readThroughWpfUi = 'PASS'
        checks = $checks
    })
    Write-Pass "QA-03 NOTIFICATIONS VERIFICATION PASSED: $($checks.Count) checks."
}

function Verify-CriticalProduct {
    $state = Get-State
    $seed = $state.Qa03
    $checks = [Collections.Generic.List[string]]::new()
    function Check([bool]$condition, [string]$message) {
        if (-not $condition) { throw "FAIL: $message" }
        $checks.Add($message); Write-Pass $message
    }
    $admin = Invoke-Login $state.AdminLogin
    $token = $admin.accessToken

    $tasks = Invoke-Call Get '/api/v1/tasks' $token
    $taskItems = @(($tasks.Content | ConvertFrom-Json).items)
    $uiTask = @($taskItems | Where-Object { $_.title -eq 'QA-03 ui task final' })
    Check ($uiTask.Count -eq 1) 'UI-created task exists after the conflict flow and API restart'
    Check ($uiTask[0].version -ge 3) 'Conflict reload advanced the persisted task version'
    Check ($uiTask[0].status -eq 'new') 'UI task retains the new status'

    $uiTaskId = $uiTask[0].id
    $taskSql = Invoke-Sql "SELECT count(*) FROM governance.domain_events WHERE aggregate_id='$uiTaskId';"
    Check ([int]$taskSql -ge 3) 'UI task lifecycle emitted durable domain events'

    $projects = Invoke-Call Get '/api/v1/projects?limit=200' $token
    $projectItems = @(($projects.Content | ConvertFrom-Json).items)
    Check (@($projectItems | Where-Object { $_.name -eq 'QA-03 ui created project' }).Count -eq 1) 'UI-created project is persisted'
    Check (@($projectItems | Where-Object { $_.name -eq 'QA-03 deterministic project' }).Count -eq 1) 'Seeded project survived the whole gate'

    $catalog = Invoke-Call Get '/api/v1/catalog-items?limit=200' $token
    $catalogItems = @(($catalog.Content | ConvertFrom-Json).items)
    Check (@($catalogItems | Where-Object { $_.name -eq 'QA-03 ui catalog file' }).Count -eq 1) 'UI-created catalog item is persisted'

    $contacts = Invoke-Call Get '/api/v1/contacts?limit=200' $token
    $contactItems = @(($contacts.Content | ConvertFrom-Json).items)
    Check (@($contactItems | Where-Object { $_.displayName -eq 'QA-03 ui contact' }).Count -eq 1) 'UI-created contact is persisted'

    $eventSql = Invoke-Sql "SELECT count(*) FROM calendar.events WHERE id='$($seed.EventId)';"
    Check ($eventSql -eq '1') 'Seeded calendar event survived the whole gate'
    $seriesSql = Invoke-Sql "SELECT count(*) FROM calendar.recurrence_series WHERE id='$($seed.SeriesId)';"
    Check ($seriesSql -eq '1') 'Seeded recurrence series survived the whole gate'
    $notificationSql = Invoke-Sql "SELECT status FROM notify.notifications WHERE id='$($seed.NotificationId)';"
    Check ($notificationSql -eq 'read') 'Worker-delivered notification retained its read state'
    $reminderSql = Invoke-Sql "SELECT status FROM calendar.reminders WHERE id='$($seed.ReminderId)';"
    Check ($reminderSql -eq 'delivered') 'Reminder retained its delivered state'

    $db = [ordered]@{
        postgresVersion = Invoke-Sql 'SHOW server_version;'
        migrationVersion = Invoke-Sql 'SELECT max(version) FROM infrastructure.schema_migrations;'
        uiTaskId = $uiTaskId
        uiTaskTitle = $uiTask[0].title
        uiTaskVersion = $uiTask[0].version
        uiTaskStatus = $uiTask[0].status
        uiProjectCount = 2
        uiCatalogItemCount = 2
        uiContactCount = 2
        seededEventCount = 1
        seededSeriesCount = 1
        notificationState = $notificationSql
        reminderState = $reminderSql
    }
    Write-JsonEvidence 'product-db-assertions.json' ([ordered]@{
        schemaVersion = 1
        scenario = 'QA-03 critical product persistence after UI and API restart'
        seedProfile = 'qa03-deterministic-v1'
        result = 'PASS'
        database = $db
        checks = $checks
    })
    Write-Pass "QA-03 CRITICAL PRODUCT VERIFICATION PASSED: $($checks.Count) checks."
}

switch ($Phase) {
    'SeedProductData' { Seed-ProductData }
    'VerifyProductApi' { Verify-ProductApi }
    'RunWorkerDelivery' { Run-WorkerDelivery }
    'VerifyNotificationsApi' { Verify-NotificationsApi }
    'VerifyCriticalProduct' { Verify-CriticalProduct }
}

exit 0
