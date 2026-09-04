[CmdletBinding()]
param([string]$ReportPath = '', [switch]$VerifyExisting)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Provision with Test-TaskWriteE2E.ps1 -Phase Setup; restore with -Phase Cleanup.
$runtime = Join-Path $env:LOCALAPPDATA 'TaskE2ERuntime/task-write-e2e'
$state = Get-Content -LiteralPath (Join-Path $runtime 'state.json') -Raw | ConvertFrom-Json
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw "FAIL: $message" }
    $checks.Add($message)
    Write-Host "[PASS] $message"
}
function Login([string]$login) {
    $body = @{ login = $login; password = $state.AccountPassword; device = @{
        deviceKey = [guid]::NewGuid().ToString('N'); deviceName = 'Today E2E'
        platform = 'windows'; appVersion = '1.0.0'; osVersion = 'Windows'
    } }
    $response = Invoke-WebRequest "$($state.BaseUrl)/api/v1/auth/login" -Method Post `
        -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 6)
    Check ($response.StatusCode -eq 200) "Login $login through trusted HTTPS"
    return ($response.Content | ConvertFrom-Json).accessToken
}
function Call([string]$method, [string]$path, $body = $null, [string]$access = $token) {
    $args = @{ Uri = "$($state.BaseUrl)/api/v1/$path"; Method = $method
        Headers = @{ Authorization = "Bearer $access"; 'Idempotency-Key' = [guid]::NewGuid().ToString('N') }
        SkipHttpErrorCheck = $true }
    if ($null -ne $body) { $args.Body = $body | ConvertTo-Json -Depth 10; $args.ContentType = 'application/json' }
    return Invoke-WebRequest @args
}
$token = Login $state.AdminLogin
$date = [datetime]::Today
$zone = [TimeZoneInfo]::Local.Id
$from = [uri]::EscapeDataString($date.ToUniversalTime().ToString('o'))
$to = [uri]::EscapeDataString($date.AddDays(1).ToUniversalTime().ToString('o'))
$path = "calendar?from=$from&to=$to&timezone=$([uri]::EscapeDataString($zone))"
if (-not $VerifyExisting) {
$empty = Call GET $path
Check ($empty.StatusCode -eq 200 -and @((($empty.Content | ConvertFrom-Json).items)).Count -eq 0) 'Fresh current local day is empty'
$prefix = 'Today E2E'
$timed = Call POST 'tasks' @{ title = "$prefix timed task"; priority = 'high'
    startAtUtc = $date.AddHours(9).ToUniversalTime().ToString('o')
    deadlineAt = $date.AddHours(10).ToUniversalTime().ToString('o') }
Check ($timed.StatusCode -eq 201) 'Persist task with start time'
$untimed = Call POST 'tasks' @{ title = "$prefix untimed task"; priority = 'normal'
    deadlineAt = $date.AddHours(18).ToUniversalTime().ToString('o') }
Check ($untimed.StatusCode -eq 201) 'Persist task without start time'
$event = Call POST 'calendar-events' @{ title = "$prefix event"; eventDate = $date.ToString('yyyy-MM-dd')
    isAllDay = $false; timeZone = $zone
    startAtUtc = $date.AddHours(11).ToUniversalTime().ToString('o')
    endAtUtc = $date.AddHours(12).ToUniversalTime().ToString('o') }
Check ($event.StatusCode -eq 201) 'Persist timed calendar event'
$allDay = Call POST 'calendar-events' @{ title = "$prefix all day"; eventDate = $date.ToString('yyyy-MM-dd')
    isAllDay = $true; timeZone = $zone }
Check ($allDay.StatusCode -eq 201) 'Persist all-day event'
$tomorrow = Call POST 'tasks' @{ title = "$prefix tomorrow"; priority = 'normal'
    startAtUtc = $date.AddDays(1).AddHours(9).ToUniversalTime().ToString('o') }
Check ($tomorrow.StatusCode -eq 201) 'Persist next-day exclusion probe'
}
$schedule = Call GET $path
Check ($schedule.StatusCode -eq 200) 'Read exactly current local midnights through Calendar API'
$items = @(($schedule.Content | ConvertFrom-Json).items)
Check ($items.Count -eq 4) 'Current day contains exactly four persisted records; tomorrow excluded'
Check (@($items | Where-Object { -not $_.isAllDay -and $null -ne $_.startAtUtc }).Count -eq 2) 'Two timed records'
Check (@($items | Where-Object { $_.isAllDay -or $null -eq $_.startAtUtc }).Count -eq 2) 'Two untimed/all-day records'
$reader = Login $state.ReadOnlyLogin
$readOnly = Call GET $path $null $reader
Check ($readOnly.StatusCode -eq 200) 'Read-only account can read calendar (Calendar.Read maps to task.read)'
if ($ReportPath) {
    $lines = @('# Today HTTPS/PostgreSQL E2E', '', 'Production API, trusted HTTPS, isolated PostgreSQL 16, schema 11.', '')
    $lines += $checks | ForEach-Object { "- PASS: $_" }
    [IO.File]::WriteAllLines([IO.Path]::GetFullPath($ReportPath), $lines)
}
