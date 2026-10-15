$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$evidence = Join-Path $PSScriptRoot 'evidence'
$runtime = Join-Path $env:TEMP ('task-employee-picker-postgres-' + [guid]::NewGuid().ToString('N'))
$pg = 'C:\Program Files\PostgreSQL\16\bin'
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
$savedConnection = $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION
try {
    & "$pg/initdb.exe" -D $runtime -U postgres --auth=trust --encoding=UTF8 --locale=C *> (Join-Path $evidence 'postgres-init.log')
    if ($LASTEXITCODE -ne 0) { throw 'Isolated PostgreSQL initialization failed.' }
    Start-Process -FilePath "$pg/postgres.exe" -ArgumentList "-D `"$runtime`" -h 127.0.0.1 -p $port" -WindowStyle Hidden -RedirectStandardOutput (Join-Path $evidence 'postgres.log') -RedirectStandardError (Join-Path $evidence 'postgres-error.log')
    $ready = $false
    for ($attempt=0; $attempt -lt 50; $attempt++) {
        & "$pg/pg_isready.exe" -h 127.0.0.1 -p $port -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready=$true; break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) { throw 'Isolated PostgreSQL did not start.' }
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = "Host=127.0.0.1;Port=$port;Database=postgres;Username=postgres;Pooling=false"
    $filter='FullyQualifiedName~Task.Tests.Calendar.|FullyQualifiedName~PostgresCalendarEventStoreTests|FullyQualifiedName~PostgresProductApiTests|FullyQualifiedName~PostgresProductStoresTests|FullyQualifiedName~PostgresAuthorizationScenarioTests|FullyQualifiedName~PostgresAuthorizationPolicyStoreTests'
    & dotnet test (Join-Path $repo 'work/production/tests/Task.Tests/Task.Tests.csproj') -c Release --no-build --filter $filter --logger 'trx;LogFileName=domain-postgres.trx' --results-directory $evidence *> (Join-Path $evidence 'domain-postgres.log')
    if ($LASTEXITCODE -ne 0) { throw 'Domain/PostgreSQL gate failed.' }
    @{ postgres='16'; executed=$true; loopback_only=$true; test_connection_configured=$true } | ConvertTo-Json | Set-Content (Join-Path $evidence 'postgres-runtime.json') -Encoding utf8
}
finally {
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = $savedConnection
    if (Test-Path -LiteralPath (Join-Path $runtime 'postmaster.pid')) { & "$pg/pg_ctl.exe" -D $runtime -m fast -w stop *> (Join-Path $evidence 'postgres-stop.log') }
}
