[CmdletBinding()]
param(
    [string]$Repository,
    [string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Repository)) { $Repository = Join-Path $PSScriptRoot 'repo' }
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) { $EvidenceDirectory = Join-Path $PSScriptRoot 'fix-evidence' }
$production = Join-Path (Resolve-Path -LiteralPath $Repository) 'work/production'
$evidence = $EvidenceDirectory
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$pg = 'C:\Program Files\PostgreSQL\16\bin'
$runtime = Join-Path $env:TEMP ('task-pr13pr14-' + [Guid]::NewGuid().ToString('N'))
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
$previousConnection = $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION
try {
    & "$pg/initdb.exe" -D $runtime -U postgres --auth=trust --encoding=UTF8 --locale=C *> (Join-Path $evidence 'postgres-init.log')
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL initialization failed.' }
    Start-Process -FilePath "$pg/postgres.exe" -ArgumentList "-D `"$runtime`" -h 127.0.0.1 -p $port" -WindowStyle Hidden -RedirectStandardOutput (Join-Path $evidence 'postgres.log') -RedirectStandardError (Join-Path $evidence 'postgres-error.log') | Out-Null
    $ready = $false
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        & "$pg/pg_isready.exe" -h 127.0.0.1 -p $port -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) { throw 'PostgreSQL readiness failed.' }
    & "$pg/psql.exe" -h 127.0.0.1 -p $port -U postgres -d postgres -Atc 'SHOW server_version;' | Set-Content (Join-Path $evidence 'postgres-version.txt')
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = "Host=127.0.0.1;Port=$port;Database=postgres;Username=postgres;Pooling=false"
    $env:TASK_COMPANY_CARD_EVIDENCE = Join-Path $evidence 'companies'
    $env:TASK_CONTEXT_EVIDENCE = Join-Path $evidence 'task-actions'
    $ErrorActionPreference = 'Continue'
    & dotnet test (Join-Path $production 'Task.sln') -c Release --no-build --no-restore --logger 'trx;LogFilePrefix=integration-pg16' --results-directory (Join-Path $evidence 'full-pg16') *> (Join-Path $evidence 'full-pg16.log')
    $testExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Get-Content (Join-Path $evidence 'full-pg16.log') -Tail 16
    if ($testExit -ne 0) { throw "Integration tests failed: exit $testExit" }
}
finally {
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = $previousConnection
    Remove-Item Env:TASK_COMPANY_CARD_EVIDENCE -ErrorAction SilentlyContinue
    Remove-Item Env:TASK_CONTEXT_EVIDENCE -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath (Join-Path $runtime 'postmaster.pid')) {
        & "$pg/pg_ctl.exe" -D $runtime -m fast -w stop *> (Join-Path $evidence 'postgres-stop.log')
    }
}
