[CmdletBinding()]
param([string]$ReportPath = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Uses the isolated PostgreSQL 16 + HTTPS runtime provisioned by Test-TaskWriteE2E -Phase Setup.
$runtime = Join-Path $env:LOCALAPPDATA 'TaskE2ERuntime/task-write-e2e'
$state = Get-Content -LiteralPath (Join-Path $runtime 'state.json') -Raw | ConvertFrom-Json
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw "FAIL: $message" }
    $checks.Add($message); Write-Host "[PASS] $message"
}
function Login([string]$login) {
    $body = @{ login=$login; password=$state.AccountPassword; device=@{ deviceKey=[guid]::NewGuid().ToString('N'); deviceName='Calendar E2E'; platform='windows'; appVersion='1.0.0'; osVersion='Windows' } }
    $r=Invoke-WebRequest -Uri "$($state.BaseUrl)/api/v1/auth/login" -Method Post -ContentType 'application/json' -Body ($body|ConvertTo-Json -Depth 6) -SkipHttpErrorCheck
    Check ($r.StatusCode -eq 200) "Login $login"
    return ($r.Content|ConvertFrom-Json).accessToken
}
function Call([string]$method,[string]$path,$body=$null,[string]$etag='',[string]$key='', [string]$access=$token) {
    $headers=@{Authorization="Bearer $access"; 'X-Correlation-ID'=[guid]::NewGuid().ToString('D')}
    if ($etag) {$headers['If-Match']=$etag}; if ($key) {$headers['Idempotency-Key']=$key}
    $args=@{Uri="$($state.BaseUrl)/api/v1/$path";Method=$method;Headers=$headers;SkipHttpErrorCheck=$true}
    if ($null -ne $body) {$args.Body=$body|ConvertTo-Json -Depth 20 -Compress; $args.ContentType='application/json'}
    Invoke-WebRequest @args
}
function Sql([string]$sql) {
    $result=& 'C:/Program Files/PostgreSQL/16/bin/psql.exe' -X -v ON_ERROR_STOP=1 -h 127.0.0.1 -p $state.PostgresPort -U postgres -d $state.DatabaseName -tA -c $sql 2>&1
    if ($LASTEXITCODE -ne 0) {throw 'Database verification failed.'}; ($result -join "`n").Trim()
}
$token=Login $state.AdminLogin
$session=(Call GET 'auth/session').Content|ConvertFrom-Json
Check ($session.capabilities -contains 'Recurrence.Manage') 'Administrator has recurrence capability'
$date=[datetime]::Today.ToString('yyyy-MM-dd')
$definition=@{frequency='daily';interval=1;weekdays=@();monthDays=@();occurrenceStartDate=$date;localStartTime='09:00:00';timeZone='Europe/Minsk';maxOccurrences=4;
    template=@{title='Calendar recurrence E2E';description='Persisted recurrence description';authorUserId=$session.userId;priority='normal';plannedDurationMinutes=60;assigneeIds=@();watcherIds=@();checklists=@();reminderRules=@();templateVersion=1}}
$key=[guid]::NewGuid().ToString('N')
$create=Call POST 'recurrence-series' $definition '' $key
Check ($create.StatusCode -eq 201) "Create series and occurrences transaction (HTTP $($create.StatusCode))"
$series=$create.Content|ConvertFrom-Json; $id=$series.id
$replay=Call POST 'recurrence-series' $definition '' $key
Check ($replay.StatusCode -eq 201 -and (($replay.Content|ConvertFrom-Json).id -eq $id)) 'Same-key creation replays the same series'
$definition.template.title='Wrong key payload'
$reused=Call POST 'recurrence-series' $definition '' $key
Check ($reused.StatusCode -eq 409) 'Changed payload with reused key is rejected'
$definition.template.title='Calendar recurrence E2E'
$occurrences=(Call GET "recurrence-series/$id/occurrences").Content|ConvertFrom-Json
Check ($occurrences.Count -eq 4) 'Exactly four persisted tasks generated for count limit'
Check ($occurrences[0].template.description -eq 'Persisted recurrence description') 'Occurrence retains full template snapshot'
Check ((Sql "SELECT count(*) FROM governance.domain_events WHERE aggregate_id='$id'") -eq '1') 'Replay adds no domain events'
$from=[datetime]::Today.AddDays(-1).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$to=[datetime]::Today.AddDays(42).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$schedule=Call GET "calendar?from=$from&to=$to&timezone=Europe%2FMinsk"
Check ($schedule.StatusCode -eq 200) 'Month-sized calendar range reads production data'
$items=($schedule.Content|ConvertFrom-Json).items
Check (@($items|Where-Object {$_.recurrenceSeriesId -eq $id}).Count -eq 4) 'Calendar marks every generated task as recurring'
$target=$occurrences|Sort-Object localDate|Select-Object -First 1
$change=@{scope='this_occurrence';expectedTaskVersion=$target.taskVersion;patch=@{title='Only this occurrence';priority='high';plannedDurationMinutes=30}}
$changeKey=[guid]::NewGuid().ToString('N')
$applied=Call POST "recurrence-series/$id/apply-change?occurrenceKey=$($target.localDate)" $change '"v1"' $changeKey
Check ($applied.StatusCode -eq 200) 'Single-occurrence edit succeeds with task and series preconditions'
$again=Call POST "recurrence-series/$id/apply-change?occurrenceKey=$($target.localDate)" $change '"v1"' $changeKey
Check ($again.StatusCode -eq 200) 'Scoped change replays before stale-version validation'
$stale=Call PATCH "recurrence-series/$id" @{interval=2} '"v1"'
Check ($stale.StatusCode -eq 412) 'Stale series version is rejected'
$series=(Call GET "recurrence-series/$id").Content|ConvertFrom-Json
$template=$series.template; $template.title='Whole series title'
$patch=Call PATCH "recurrence-series/$id" @{template=$template} ('"v'+$series.version+'"')
Check ($patch.StatusCode -eq 200) 'Whole-series template edit succeeds'
$occurrences=(Call GET "recurrence-series/$id/occurrences").Content|ConvertFrom-Json
Check (@($occurrences|Where-Object {$_.title -eq 'Only this occurrence'}).Count -eq 1) 'Single-instance exception survives whole-series editing'
Check (@($occurrences|Where-Object {$_.title -eq 'Whole series title'}).Count -eq 3) 'Remaining instances receive the template change'
$series=$patch.Content|ConvertFrom-Json
$paused=Call PATCH "recurrence-series/$id" @{status='paused'} ('"v'+$series.version+'"')
Check ($paused.StatusCode -eq 200 -and ($paused.Content|ConvertFrom-Json).status -eq 'paused') 'Pause stops generation'
$series=$paused.Content|ConvertFrom-Json
$resumed=Call POST "recurrence-series/$id/resume" @{expectedVersion=$series.version} ('"v'+$series.version+'"') ([guid]::NewGuid().ToString('N'))
Check ($resumed.StatusCode -eq 200 -and ($resumed.Content|ConvertFrom-Json).status -eq 'active') 'Resume restores generation'
$invalid=$definition.Clone(); $invalid.untilDate=[datetime]::Today.AddDays(4).ToString('yyyy-MM-dd')
$bad=Call POST 'recurrence-series' $invalid '' ([guid]::NewGuid().ToString('N'))
Check ($bad.StatusCode -eq 422) 'Mutually exclusive termination modes are rejected'
$reader=Login $state.ReadOnlyLogin
$denied=Call POST 'recurrence-series' $definition '' ([guid]::NewGuid().ToString('N')) $reader
Check ($denied.StatusCode -eq 403) 'Read-only account cannot write recurrence series'
$missing=Call GET ('recurrence-series/'+[guid]::NewGuid())
Check ($missing.StatusCode -eq 404) 'Unknown series fails closed'
# Event attendee collections use existing event endpoints and must round-trip.
$event=@{title='Calendar attendees E2E';eventDate=$date;isAllDay=$true;timeZone='Europe/Minsk';userAttendees=@(@{userAccountId=$session.userId;role='required';responseStatus='accepted'});contactAttendees=@()}
$eventCreated=Call POST 'calendar-events' $event '' ([guid]::NewGuid().ToString('N'))
Check ($eventCreated.StatusCode -eq 201) 'Event with attendee is created'
$eventId=($eventCreated.Content|ConvertFrom-Json).id
$eventPatch=Call PATCH "calendar-events/$eventId" @{title='Attendee retained'} '"v1"'
Check ($eventPatch.StatusCode -eq 200 -and @((($eventPatch.Content|ConvertFrom-Json).userAttendees)).Count -eq 1) 'Omitted attendee array preserves membership'
$eventClear=Call PATCH "calendar-events/$eventId" @{userAttendees=@()} '"v2"'
Check ($eventClear.StatusCode -eq 200 -and @((($eventClear.Content|ConvertFrom-Json).userAttendees)).Count -eq 0) 'Explicit empty array removes membership'
if ($ReportPath) {
    $lines=@('# Calendar production E2E', '', 'Environment: isolated PostgreSQL 16, schema 8, production HTTPS API, real administrator and read-only sessions.', '')
    $lines += $checks|ForEach-Object {"- PASS: $_"}
    [IO.File]::WriteAllLines([IO.Path]::GetFullPath($ReportPath), $lines)
}
Write-Host "Calendar E2E passed: $($checks.Count) checks."
