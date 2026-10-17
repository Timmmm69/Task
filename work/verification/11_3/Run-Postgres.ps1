param([string]$Filter = 'FullyQualifiedName~Identity|FullyQualifiedName~Security|FullyQualifiedName~Account|FullyQualifiedName~Session|FullyQualifiedName~RefreshToken|FullyQualifiedName~Authorization')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$evidence = $PSScriptRoot
# PostgreSQL's Windows binaries cannot initialize under a Cyrillic path. The drive
# alias keeps every runtime file physically inside this task's work directory.
$asciiDrive = @('R:', 'Q:', 'P:') | Where-Object { -not (Test-Path "$_\") } | Select-Object -First 1
if (-not $asciiDrive) { throw 'No free drive alias for the isolated runtime.' }
& subst.exe $asciiDrive $evidence
if ($LASTEXITCODE -ne 0) { throw 'Cannot create the temporary ASCII path alias.' }
$runtime = "$asciiDrive\postgres-runtime-$([guid]::NewGuid().ToString('N'))"
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
    & dotnet test (Join-Path $repo 'work/production/tests/Task.Tests/Task.Tests.csproj') -c Release --filter $Filter --logger 'trx;LogFileName=identity-postgres-release.trx' --results-directory $evidence *> (Join-Path $evidence 'identity-postgres.log')
    if ($LASTEXITCODE -ne 0) { throw 'Identity/PostgreSQL gate failed; see identity-postgres.log.' }
    @{ postgres='16'; executed=$true; loopback_only=$true; test_connection_configured=$true } | ConvertTo-Json | Set-Content (Join-Path $evidence 'postgres-runtime.json') -Encoding utf8
}
finally {
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = $savedConnection
    if (Test-Path -LiteralPath (Join-Path $runtime 'postmaster.pid')) { & "$pg/pg_ctl.exe" -D $runtime -m fast -w stop *> (Join-Path $evidence 'postgres-stop.log') }
    & subst.exe $asciiDrive /D
}
