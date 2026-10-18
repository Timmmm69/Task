param([string]$Filter = '', [string]$Receipt = 'release')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$evidence = Join-Path $PSScriptRoot ('results/' + $Receipt)
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$runtime = Join-Path $env:TEMP ('task-history13-4-postgres-' + [guid]::NewGuid().ToString('N'))
$pg = 'C:/Program Files/PostgreSQL/16/bin'
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
try {
    & "$pg/initdb.exe" -D $runtime -U postgres --auth=trust --encoding=UTF8 --locale=C *> (Join-Path $evidence 'postgres-init.log')
    if ($LASTEXITCODE -ne 0) { throw 'Isolated PostgreSQL initialization failed' }
    Start-Process -FilePath "$pg/postgres.exe" -ArgumentList "-D `"$runtime`" -h 127.0.0.1 -p $port" -WindowStyle Hidden -RedirectStandardOutput (Join-Path $evidence 'postgres.log') -RedirectStandardError (Join-Path $evidence 'postgres-error.log')
    $ready = $false
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        & "$pg/pg_isready.exe" -h 127.0.0.1 -p $port -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) { throw 'Isolated PostgreSQL did not start' }
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = "Host=127.0.0.1;Port=$port;Database=postgres;Username=postgres;Pooling=false"
    $env:TASK_PROJECT_PICKER_EVIDENCE = Join-Path $evidence 'screenshots'
    & dotnet build (Join-Path $root 'work/production/Task.sln') -c Release --no-restore -v quiet *> (Join-Path $evidence 'build.log')
    if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $evidence 'build.log') -Tail 25; throw 'Build failed' }
    Write-Output 'Release solution build PASS; isolated PostgreSQL 16 running.'
    $ErrorActionPreference = 'Continue'
    $filterArgs = @()
    if ($Filter) { $filterArgs = @('--filter', $Filter) }
    & dotnet test (Join-Path $root 'work/production/Task.sln') -c Release --no-build --no-restore @filterArgs --logger trx --results-directory $evidence -v quiet *> (Join-Path $evidence 'tests.log')
    $result = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Get-Content (Join-Path $evidence 'tests.log') -Tail 65
    if ($result -ne 0) { throw 'Release solution gate failed; inspect tests.log and TRX' }
}
finally {
    if (Test-Path -LiteralPath (Join-Path $runtime 'postmaster.pid')) { & "$pg/pg_ctl.exe" -D $runtime -m fast -w stop | Out-Null }
}
