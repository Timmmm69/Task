param([Parameter(Mandatory)][string]$PackageDirectory, [Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$phaseScript = (Resolve-Path "$PSScriptRoot/../production/verification/Test-TaskWriteE2E.ps1").Path
$runtime = Join-Path $env:LOCALAPPDATA 'TaskE2ERuntime/task-write-e2e'
$data = Join-Path $PSScriptRoot ('runtime-corporate-' + [Guid]::NewGuid().ToString('N'))
if (-not (Test-Path -LiteralPath (Join-Path $runtime 'state.json'))) { throw 'Expected existing isolated Task fixture.' }
$process = $null
$checks = [Collections.Generic.List[object]]::new()
$clientVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PackageDirectory 'Task/Task.Desktop.exe')).ProductVersion.Split('+')[0]
function Phase([string]$name) {
    $phaseProcess = Start-Process -FilePath (Get-Command pwsh).Source -WindowStyle Hidden -PassThru `
        -ArgumentList @('-NoProfile', '-File', ('"' + $phaseScript + '"'), '-Phase', $name, '-DesktopAppDataPath', ('"' + $data + '"')) `
        -RedirectStandardOutput (Join-Path $EvidenceDirectory "phase-$name.stdout.log") `
        -RedirectStandardError (Join-Path $EvidenceDirectory "phase-$name.stderr.log")
    try {
        if (-not $phaseProcess.WaitForExit(120000)) { $phaseProcess.Kill(); throw "Corporate phase timed out: $name" }
        if ($phaseProcess.ExitCode -ne 0) { throw "Corporate phase failed: $name" }
    } finally { $phaseProcess.Dispose() }
}
function Check([string]$name, $detail) {
    $checks.Add(@{check=$name;result='PASS';category='CONFIRMED BY REAL CORPORATE SERVER';environment='Windows localhost HTTPS + isolated PostgreSQL 16';detail=$detail})
    $checks | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $EvidenceDirectory 'corporate-portable.json') -Encoding utf8
    Write-Output "PASS: $name"
}
try {
    Phase 'StartApi'
    $state = Get-Content -LiteralPath (Join-Path $runtime 'state.json') -Raw | ConvertFrom-Json
    $health = Invoke-WebRequest "$($state.BaseUrl)/health/ready" -TimeoutSec 15
    if ($health.StatusCode -ne 200) { throw 'HTTPS readiness probe failed.' }
    Check 'Corporate HTTPS readiness' @{statusCode=200;certificateValidation='default OS validation';trustBypass=$false}
    $deviceKey = [Guid]::NewGuid().ToString('N')
    $body = @{login=$state.AdminLogin;password=$state.AccountPassword;device=@{deviceKey=$deviceKey;deviceName='Task portable acceptance';platform='windows';appVersion=$clientVersion;osVersion='Windows acceptance'}} | ConvertTo-Json -Depth 4
    $tokens = Invoke-RestMethod -Method Post -Uri "$($state.BaseUrl)/api/v1/auth/login" -Body $body -ContentType 'application/json' -TimeoutSec 15
    $headers = @{Authorization="Bearer $($tokens.accessToken)"}
    $session = Invoke-RestMethod "$($state.BaseUrl)/api/v1/auth/session" -Headers $headers
    if (-not $session.userId -or @($session.capabilities).Count -eq 0) { throw 'Authenticated session/capabilities missing.' }
    Check 'Login/session/capabilities' @{capabilityCount=@($session.capabilities).Count}
    $probe = Join-Path $PSScriptRoot '../final_acceptance_stage6/Probe/bin/Release/net10.0-windows/Probe.exe'
    & $probe seed $data (Join-Path $PSScriptRoot 'evidence/corporate-cohabitation-seed.json') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Shared-root Personal marker fixture failed.' }
    # Independent real login for the DPAPI-restored native client session.
    $savedDevice = [Guid]::NewGuid().ToString('N')
    $savedBody = @{login=$state.AdminLogin;password=$state.AccountPassword;device=@{deviceKey=$savedDevice;deviceName='Task ZIP session';platform='windows';appVersion=$clientVersion;osVersion='Windows acceptance'}} | ConvertTo-Json -Depth 4
    $savedTokens = Invoke-RestMethod -Method Post -Uri "$($state.BaseUrl)/api/v1/auth/login" -Body $savedBody -ContentType 'application/json' -TimeoutSec 15
    $entry = @{DeviceId=$savedTokens.sessionId;OrgId='';Login=$state.AdminLogin;DeviceKey=$savedDevice;RefreshToken=$savedTokens.refreshToken;SavedAtUtc=[DateTime]::UtcNow;Version=2} | ConvertTo-Json -Compress
    $protected = [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($entry),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes((Join-Path $data 'credentials.bin'),$protected)
    @{version=1;baseUrl="$($state.BaseUrl)/"} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $data 'server-settings.json') -Encoding utf8
    [IO.File]::WriteAllText((Join-Path $data 'application-preferences.json'), '{"version":1,"mode":"Corporate"}')
    $personalDatabase = Join-Path $data 'Personal/tasks.db'
    $personalHash = (Get-FileHash -LiteralPath $personalDatabase -Algorithm SHA256).Hash
    $createHeaders = @{Authorization=$headers.Authorization;'Idempotency-Key'=[Guid]::NewGuid().ToString('N')}
    $markerTitle = 'CORPORATE_SECRET_MARKER-' + [Guid]::NewGuid().ToString('N')
    $marker = Invoke-RestMethod -Method Post -Uri "$($state.BaseUrl)/api/v1/tasks" -Headers $createHeaders -ContentType 'application/json' -Body (@{title=$markerTitle;priority='normal'} | ConvertTo-Json)
    $corporate = Invoke-RestMethod "$($state.BaseUrl)/api/v1/search?q=$markerTitle&limit=100" -Headers $headers
    $personal = Invoke-RestMethod "$($state.BaseUrl)/api/v1/search?q=PERSONAL_SECRET_MARKER&limit=100" -Headers $headers
    if (@($corporate.items).Count -ne 1 -or @($personal.items).Count -ne 0) { throw 'Corporate marker isolation failed.' }
    & $probe verify $data (Join-Path $PSScriptRoot 'evidence/corporate-cohabitation-verify.json') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Personal saw Corporate marker.' }
    Check 'Corporate marker present/Personal marker absent' @{corporateMarkerHits=1;personalMarkerHits=0}
    $logout = Invoke-WebRequest -Method Post -Uri "$($state.BaseUrl)/api/v1/auth/logout" -Headers $headers -SkipHttpErrorCheck -TimeoutSec 15
    $revoked = Invoke-WebRequest -Uri "$($state.BaseUrl)/api/v1/auth/session" -Headers $headers -SkipHttpErrorCheck -TimeoutSec 15
    if ($logout.StatusCode -notin @(200,204) -or $revoked.StatusCode -ne 401) { throw 'Logout failed to revoke the session.' }
    Check 'Logout revokes real server session' @{logoutStatus=[int]$logout.StatusCode;revokedSessionStatus=401}
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $PackageDirectory 'Task/Task.Desktop.exe'))
    $start.UseShellExecute = $false
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.Environment['TASK_DESKTOP_DATA_DIRECTORY'] = $data
    $process = [Diagnostics.Process]::Start($start)
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $process.Refresh()
        if ($process.HasExited) { throw 'Portable Corporate process exited.' }
        if ($process.MainWindowTitle -eq 'Task — Corporate') { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowTitle -ne 'Task — Corporate') { throw "Saved session did not restore: $($process.MainWindowTitle)" }
    if ((Get-FileHash -LiteralPath $personalDatabase -Algorithm SHA256).Hash -ne $personalHash) { throw 'Corporate changed Personal database.' }
    Check 'Portable Corporate saved session native startup' @{title=$process.MainWindowTitle;executableSha256=(Get-FileHash -LiteralPath $start.FileName -Algorithm SHA256).Hash;dpapiSession=$true}
} finally {
    if ($process) {
        if (-not $process.HasExited) { $null = $process.CloseMainWindow(); if (-not $process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() } }
        $process.Dispose()
    }
    if (Test-Path -LiteralPath (Join-Path $runtime 'state.json')) { Phase 'StopApi' }
}
