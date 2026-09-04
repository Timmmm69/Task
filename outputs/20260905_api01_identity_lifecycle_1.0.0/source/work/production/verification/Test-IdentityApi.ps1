[CmdletBinding()]
param([string]$Filter = 'FullyQualifiedName~PostgresIdentityLifecycleTests')
$ErrorActionPreference = 'Stop'
$production = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$evidence = Join-Path $production 'evidence/api01'
[IO.Directory]::CreateDirectory($evidence) | Out-Null
# PostgreSQL on Windows needs an ASCII cluster path; this disposable runtime owns only this directory.
$runtime = Join-Path $env:TEMP ('task-api01-' + [guid]::NewGuid().ToString('N'))
$pg = 'C:\Program Files\PostgreSQL\16\bin'
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
$savedConnection = $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION
try {
    & "$pg/initdb.exe" -D $runtime -U postgres --auth=trust --encoding=UTF8 --locale=C 2>&1 | Out-File (Join-Path $evidence 'postgres-init.log')
    if ($LASTEXITCODE -ne 0) { throw 'Isolated PostgreSQL initialization failed.' }
    $process = Start-Process -FilePath "$pg/postgres.exe" -ArgumentList "-D `"$runtime`" -h 127.0.0.1 -p $port" -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'postgres.log') -RedirectStandardError (Join-Path $evidence 'postgres-error.log')
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
    & dotnet @testArgs 2>&1 | Tee-Object (Join-Path $evidence 'tests.log')
    if ($LASTEXITCODE -ne 0) { throw 'Identity API gate failed.' }
}
finally {
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = $savedConnection
    if (Test-Path -LiteralPath (Join-Path $runtime 'postmaster.pid')) { & "$pg/pg_ctl.exe" -D $runtime -m fast -w stop | Out-Null }
}
