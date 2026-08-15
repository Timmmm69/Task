# Task.Api black-box smoke test: health endpoints and X-Correlation-ID handling.
[CmdletBinding()]
param(
    [int]$ReadyTimeoutSeconds = 30,
    [int]$HttpTimeoutSeconds = 5,
    [string]$TaskDatabaseConnectionString,
    [int]$ExpectedReadyStatusCode = 503,
    [ValidateSet('Ready', 'NotReady')]
    [string]$ExpectedReadyState = 'NotReady',
    [string]$ExpectedPersistenceCode = 'NotConfigured'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Failed = $false
$script:BaseUri = $null
function Write-Ok { Write-Host "[ OK ] $($args -join ' ')" }
function Write-Bad { Write-Host "[FAIL] $($args -join ' ')" -ForegroundColor Red; $script:Failed = $true }
function Assert-Check {
    param([bool]$Condition, [string]$OkText, [string]$BadText)
    if ($Condition) { Write-Ok $OkText } else { Write-Bad $BadText }
}
function Get-HeaderValue {
    param($Headers, [string]$Name)
    if ($null -eq $Headers) { return $null }
    foreach ($entry in $Headers.GetEnumerator()) { if ($entry.Key -ieq $Name) { return $entry.Value } }
    return $null
}

function ConvertFrom-TaskJson {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { throw 'empty JSON body' }
    $Text | ConvertFrom-Json
}
function Invoke-TaskApiHttp {
    param([string]$Path, [hashtable]$RequestHeaders = @{})
    try {
        $response = Invoke-WebRequest -Uri "$script:BaseUri$Path" -Headers $RequestHeaders -UseBasicParsing -TimeoutSec $HttpTimeoutSeconds
        return [pscustomobject]@{ StatusCode = [int]$response.StatusCode; Headers = $response.Headers; Body = $response.Content }
    }
    catch {
        $webResponse = $_.Exception.Response
        if ($null -eq $webResponse) { throw }
        $bodyText = $null
        try {
            $reader = New-Object System.IO.StreamReader($webResponse.GetResponseStream(), [System.Text.Encoding]::UTF8)
            $bodyText = $reader.ReadToEnd()
            $reader.Dispose()
        }
        catch { }
        return [pscustomobject]@{ StatusCode = [int]$webResponse.StatusCode; Headers = $webResponse.Headers; Body = $bodyText }
    }
}

$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$binDir = Join-Path $repoRoot 'work\production\src\Task.Api\bin'
$dll = @("$binDir\Debug\net10.0\Task.Api.dll", "$binDir\Release\net10.0\Task.Api.dll") | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $dll) { Write-Host '[FAIL] Task.Api.dll not found. Build the solution first.'; exit 1 }
$dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if (-not $dotnet) { Write-Host '[FAIL] dotnet CLI not found on PATH.'; exit 1 }
$listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
$listener.Stop()
$script:BaseUri = "http://127.0.0.1:$port"
$stdoutLog = Join-Path ([System.IO.Path]::GetTempPath()) "task-api-$port.stdout.log"
$stderrLog = Join-Path ([System.IO.Path]::GetTempPath()) "task-api-$port.stderr.log"
$process = $null
try {
    $previousUrls = $env:ASPNETCORE_URLS
    $previousTaskDatabase = $env:ConnectionStrings__TaskDatabase
    $env:ASPNETCORE_URLS = $script:BaseUri
    if ([string]::IsNullOrWhiteSpace($TaskDatabaseConnectionString)) {
        Remove-Item Env:\ConnectionStrings__TaskDatabase -Force -ErrorAction SilentlyContinue
    }
    else {
        $env:ConnectionStrings__TaskDatabase = $TaskDatabaseConnectionString
    }
    try {
        $process = Start-Process -FilePath $dotnet.Source `
            -ArgumentList @('"' + $dll + '"') `
            -WorkingDirectory (Split-Path -Parent $dll) `
            -WindowStyle Hidden `
            -RedirectStandardOutput $stdoutLog `
            -RedirectStandardError $stderrLog `
            -PassThru
    }
    finally {
        if ($null -eq $previousUrls) { Remove-Item Env:\ASPNETCORE_URLS -Force -ErrorAction SilentlyContinue }
        else { $env:ASPNETCORE_URLS = $previousUrls }
        if ($null -eq $previousTaskDatabase) { Remove-Item Env:\ConnectionStrings__TaskDatabase -Force -ErrorAction SilentlyContinue }
        else { $env:ConnectionStrings__TaskDatabase = $previousTaskDatabase }
    }

    $ready = $false
    $deadline = (Get-Date).AddSeconds($ReadyTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) { break }
        try {
            Invoke-WebRequest -Uri "$script:BaseUri/health/live" -UseBasicParsing -TimeoutSec 2 | Out-Null
            $ready = $true
            break
        }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) {
        Write-Bad "Task.Api did not answer on $script:BaseUri within $ReadyTimeoutSeconds s (exited: $($process.HasExited))."
        if (Test-Path -LiteralPath $stderrLog) { Get-Content -LiteralPath $stderrLog | ForEach-Object { Write-Host "  stderr: $_" } }
        exit 1
    }
    $live = Invoke-TaskApiHttp '/health/live'
    Assert-Check ($live.StatusCode -eq 200) '/health/live returned HTTP 200.' "/health/live returned HTTP $($live.StatusCode), expected 200."
    $liveJson = $null
    try { $liveJson = ConvertFrom-TaskJson $live.Body } catch { Write-Bad '/health/live JSON body is unreadable.' }
    if ($null -ne $liveJson) {
        Assert-Check ($liveJson.status -eq 'Alive') '/health/live JSON status is "Alive".' "/health/live JSON status is '$($liveJson.status)', expected 'Alive'."
    }

    $readyCheck = Invoke-TaskApiHttp '/health/ready'
    Assert-Check ($readyCheck.StatusCode -eq $ExpectedReadyStatusCode) "/health/ready returned expected HTTP $ExpectedReadyStatusCode." "/health/ready returned HTTP $($readyCheck.StatusCode), expected $ExpectedReadyStatusCode."
    $readyJson = $null
    try { $readyJson = ConvertFrom-TaskJson $readyCheck.Body } catch { Write-Bad '/health/ready JSON body is unreadable.' }
    if ($null -ne $readyJson) {
        $expectedReady = $ExpectedReadyState -eq 'Ready'
        Assert-Check ($readyJson.status -eq $ExpectedReadyState) "/health/ready JSON status is '$ExpectedReadyState'." "/health/ready JSON status is '$($readyJson.status)', expected '$ExpectedReadyState'."
        Assert-Check ($readyJson.details.ready -eq $expectedReady) "/health/ready details.ready is $($expectedReady.ToString().ToLowerInvariant())." "/health/ready details.ready is '$($readyJson.details.ready)', expected $expectedReady."
        $persistence = [string]$readyJson.details.persistence
        Assert-Check (-not [string]::IsNullOrWhiteSpace($persistence)) "/health/ready details.persistence is non-empty ($($persistence.Length) chars)." '/health/ready details.persistence is missing or empty.'
        Assert-Check ($readyJson.details.persistenceCode -eq $ExpectedPersistenceCode) "/health/ready persistenceCode is '$ExpectedPersistenceCode'." "/health/ready persistenceCode is '$($readyJson.details.persistenceCode)', expected '$ExpectedPersistenceCode'."
    }

    $validCorr = [Guid]::NewGuid()
    $corrResponse = Invoke-TaskApiHttp '/health/live' -RequestHeaders @{ 'X-Correlation-ID' = $validCorr.ToString() }
    $echoed = Get-HeaderValue $corrResponse.Headers 'X-Correlation-ID'
    $parsedEchoed = [Guid]::Empty
    $echoMatches = $null -ne $echoed -and [Guid]::TryParseExact([string]$echoed, 'D', [ref]$parsedEchoed) -and $parsedEchoed.Equals($validCorr)
    Assert-Check $echoMatches "Valid correlation ID '$($validCorr.ToString())' is returned unchanged in X-Correlation-ID." "Valid correlation ID was not echoed; got '$echoed'."

    $badCorr = 'definitely-not-a-guid'
    $replResponse = Invoke-TaskApiHttp '/health/live' -RequestHeaders @{ 'X-Correlation-ID' = $badCorr }
    $replaced = Get-HeaderValue $replResponse.Headers 'X-Correlation-ID'
    $parsedReplacement = [Guid]::Empty
    $replacedValid = $null -ne $replaced -and [Guid]::TryParseExact([string]$replaced, 'D', [ref]$parsedReplacement) -and -not $parsedReplacement.Equals([Guid]::Empty) -and -not ($replaced -ceq $badCorr)
    Assert-Check $replacedValid "Invalid correlation ID '$badCorr' was replaced with valid GUID '$replaced'." "Invalid correlation ID was not replaced with a valid GUID; got '$replaced'."
}
catch {
    Write-Bad "Unexpected failure: $($_.Exception.Message)"
    if (Test-Path -LiteralPath $stderrLog) { Get-Content -LiteralPath $stderrLog | ForEach-Object { Write-Host "  stderr: $_" } }
}
finally {
    if ($null -ne $process) {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            Wait-Process -Id $process.Id -Timeout 5 -ErrorAction SilentlyContinue
        }
        $stillAlive = $null -ne (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)
        for ($i = 0; $stillAlive -and $i -lt 25; $i++) { Start-Sleep -Milliseconds 200; $stillAlive = $null -ne (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) }
        if ($stillAlive) { cmd /c "taskkill /F /T /PID $process.Id >nul 2>nul"; Start-Sleep -Milliseconds 500; $stillAlive = $null -ne (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) }
        if ($stillAlive) { Write-Host "  warning: Task.Api process $($process.Id) is still running." }
        else { Write-Ok "Task.Api process $($process.Id) has stopped." }
    }
    Remove-Item -LiteralPath $stdoutLog, $stderrLog -Force -ErrorAction SilentlyContinue
}

if ($script:Failed) { Write-Host 'Smoke test FAILED.'; exit 1 }
Write-Host 'Smoke test PASSED.'
exit 0
