param([string]$Filter = '')
$ErrorActionPreference = 'Stop'
$production = (Resolve-Path "$PSScriptRoot/../production").Path
$evidence = Join-Path $PSScriptRoot 'evidence/database'
[IO.Directory]::CreateDirectory($evidence) | Out-Null
# Same isolated native PostgreSQL workflow as production/verification/Test-Authorization.ps1.
$runtime = Join-Path $env:TEMP ('task-context-' + [guid]::NewGuid().ToString('N'))
$pg = 'C:\Program Files\PostgreSQL\16\bin'
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
$savedConnection = $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION
try {
    & "$pg/initdb.exe" -D $runtime -U postgres --auth=trust --encoding=UTF8 --locale=C *> "$evidence/init.log"
    if ($LASTEXITCODE -ne 0) { throw 'Isolated PostgreSQL initialization failed.' }
    Start-Process -FilePath "$pg/postgres.exe" -ArgumentList "-D `"$runtime`" -h 127.0.0.1 -p $port" -WindowStyle Hidden -RedirectStandardOutput "$evidence/postgres.log" -RedirectStandardError "$evidence/postgres-error.log"
    $ready = $false
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        & "$pg/pg_isready.exe" -h 127.0.0.1 -p $port -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) { throw 'Isolated PostgreSQL did not start.' }
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = "Host=127.0.0.1;Port=$port;Database=postgres;Username=postgres;Pooling=false"
    & dotnet build "$production/Task.sln" -c Release --nologo -v quiet *> "$evidence/build.log"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $testArgs = @('test', "$production/Task.sln", '-c', 'Release', '--no-build', '--no-restore', '--nologo', '-v', 'minimal', '--logger', 'trx;LogFilePrefix=database', '--results-directory', $evidence)
    if ($Filter) { $testArgs += @('--filter', $Filter) }
    $ErrorActionPreference = 'Continue'
    & dotnet @testArgs *> "$evidence/tests.log"
    $gateResult = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Get-Content "$evidence/tests.log" | Select-Object -Last 35
    if ($gateResult -ne 0) { throw 'Database gate failed.' }
    & "$production/verification/Test-ProjectBoundaries.ps1" *> "$evidence/boundaries.log"
    if ($LASTEXITCODE -ne 0) { throw 'Boundary gate failed.' }
}
finally {
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = $savedConnection
    if (Test-Path -LiteralPath "$runtime/postmaster.pid") { & "$pg/pg_ctl.exe" -D $runtime -m fast -w stop | Out-Null }
}
