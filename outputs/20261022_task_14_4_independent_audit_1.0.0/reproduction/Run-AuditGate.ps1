param([string]$Filter = '', [string]$Receipt = 'final', [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repo = Join-Path $PSScriptRoot 'repo'
$evidence = Join-Path $PSScriptRoot ('evidence/' + $Receipt)
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$runtime = Join-Path $env:TEMP ('task-return14-4-' + $Receipt + '-' + [guid]::NewGuid().ToString('N'))
$pg = 'C:/Program Files/PostgreSQL/16/bin'
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
$previousConnection = $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION
$previousEvidence = $env:TASK_RETURN_EVIDENCE
try {
    & "$pg/initdb.exe" -D $runtime -U postgres --auth=trust --encoding=UTF8 --locale=C *> (Join-Path $evidence 'postgres-init.log')
    if ($LASTEXITCODE -ne 0) { throw 'Isolated PostgreSQL initialization failed' }
    Start-Process -FilePath "$pg/postgres.exe" -ArgumentList "-D `"$runtime`" -h 127.0.0.1 -p $port" -WindowStyle Hidden -RedirectStandardOutput (Join-Path $evidence 'postgres.log') -RedirectStandardError (Join-Path $evidence 'postgres-error.log') | Out-Null
    $ready = $false
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        & "$pg/pg_isready.exe" -h 127.0.0.1 -p $port -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) { throw 'Isolated PostgreSQL did not start' }
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = "Host=127.0.0.1;Port=$port;Database=postgres;Username=postgres;Pooling=false"
    $env:TASK_RETURN_EVIDENCE = Join-Path $evidence 'native'
    if (-not $SkipBuild) {
        & dotnet build (Join-Path $repo 'work/production/Task.sln') -c Release --no-restore -v quiet *> (Join-Path $evidence 'build.log')
        if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $evidence 'build.log') -Tail 15; throw 'Build failed' }
    }
    $filterArgs = @()
    if ($Filter) { $filterArgs = @('--filter', $Filter) }
    & dotnet test (Join-Path $repo 'work/production/Task.sln') -c Release --no-build --no-restore -m:1 @filterArgs --logger trx --results-directory $evidence -v quiet *> (Join-Path $evidence 'tests.log')
    $testExit = $LASTEXITCODE
    Get-Content (Join-Path $evidence 'tests.log') -Tail 35
    @{ postgres = '16'; isolated = $true; loopbackOnly = $true; filter = $Filter; exitCode = $testExit } | ConvertTo-Json | Set-Content (Join-Path $evidence 'runtime.json') -Encoding utf8
    if ($testExit -ne 0) { throw 'Audit test gate failed; inspect tests.log and TRX' }
}
finally {
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = $previousConnection
    $env:TASK_RETURN_EVIDENCE = $previousEvidence
    if (Test-Path -LiteralPath (Join-Path $runtime 'postmaster.pid')) { & "$pg/pg_ctl.exe" -D $runtime -m fast -w stop *> (Join-Path $evidence 'postgres-stop.log') }
}
