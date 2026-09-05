[CmdletBinding()]
param([string]$Filter = '')
$ErrorActionPreference = 'Stop'
$production = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$evidence = Join-Path $production 'evidence/sec02'
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$runtime = Join-Path $env:TEMP ('task-sec02-' + [guid]::NewGuid().ToString('N'))
$pg = 'C:\Program Files\PostgreSQL\16\bin'
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
$savedConnection = $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION
try {
    & "$pg/initdb.exe" -D $runtime -U postgres --auth=trust --encoding=UTF8 --locale=C *> (Join-Path $evidence 'postgres-init.log')
    if ($LASTEXITCODE -ne 0) { throw 'Isolated PostgreSQL initialization failed.' }
    Start-Process -FilePath "$pg/postgres.exe" -ArgumentList "-D `"$runtime`" -h 127.0.0.1 -p $port" -WindowStyle Hidden -RedirectStandardOutput (Join-Path $evidence 'postgres.log') -RedirectStandardError (Join-Path $evidence 'postgres-error.log')
    $ready = $false
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        & "$pg/pg_isready.exe" -h 127.0.0.1 -p $port -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) { throw 'Isolated PostgreSQL did not start.' }
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = "Host=127.0.0.1;Port=$port;Database=postgres;Username=postgres;Pooling=false"
    $testArgs = @('test', (Join-Path $production 'Task.sln'), '--no-restore', '--verbosity', 'minimal', '--logger', 'trx', '--results-directory', $evidence)
    if ($Filter) { $testArgs += @('--filter', $Filter) }
    # Windows PowerShell must not kill a test run at the first stderr diagnostic.
    $ErrorActionPreference = 'Continue'
    & dotnet @testArgs *> (Join-Path $evidence 'tests.log')
    $result = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Get-Content (Join-Path $evidence 'tests.log') | Select-Object -Last 65
    if ($result -ne 0) { throw 'Authorization gate failed. See evidence/sec02/tests.log.' }
}
finally {
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = $savedConnection
    if (Test-Path -LiteralPath (Join-Path $runtime 'postmaster.pid')) { & "$pg/pg_ctl.exe" -D $runtime -m fast -w stop | Out-Null }
}
