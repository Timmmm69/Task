param([switch]$Full)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$pg = 'C:/Program Files/PostgreSQL/16/bin'
$runtimeDirectory = Join-Path $PSScriptRoot ('postgres-runtime-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($runtimeDirectory) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $runtimeDirectory 'data')) | Out-Null
# PostgreSQL's Windows binaries cannot initialize a cluster through a Cyrillic path.
# Only the alias is in TEMP; every database file stays in this workspace's work/.
$runtimeAlias = Join-Path $env:TEMP ('task-admin11-4-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Junction -Path $runtimeAlias -Target $runtimeDirectory | Out-Null
$runtime = Join-Path $runtimeAlias 'data'
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
$savedConnection = $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION
function Check([string]$name, [string[]]$arguments) {
    & dotnet @arguments *> (Join-Path $PSScriptRoot ($name + '.log'))
    $code = $LASTEXITCODE
    Get-Content (Join-Path $PSScriptRoot ($name + '.log')) | Select-Object -Last 12
    if ($code -ne 0) { throw "Gate failed: $name ($code)" }
}
Push-Location $repo
try {
    & "$pg/initdb.exe" -D $runtime -U postgres --auth=trust --encoding=UTF8 --locale=C *> (Join-Path $PSScriptRoot 'postgres-init.log')
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL init failed' }
    Start-Process -FilePath "$pg/postgres.exe" -ArgumentList "-D `"$runtime`" -h 127.0.0.1 -p $port" -WindowStyle Hidden -RedirectStandardOutput (Join-Path $PSScriptRoot 'postgres.log') -RedirectStandardError (Join-Path $PSScriptRoot 'postgres-error.log')
    $ready = $false
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        & "$pg/pg_isready.exe" -h 127.0.0.1 -p $port -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 200
    }
    if (!$ready) { throw 'PostgreSQL startup failed' }
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = "Host=127.0.0.1;Port=$port;Database=postgres;Username=postgres;Pooling=false"
    '{"postgres":"16","configured":true,"loopback_only":true,"executed":true}' | Set-Content (Join-Path $PSScriptRoot 'postgres-runtime.json')
    Check 'build' @('build', 'work/production/Task.sln', '-c', 'Release', '--no-restore', '-v', 'minimal')
    $filter = 'FullyQualifiedName~Administration_|FullyQualifiedName~Authorization_|FullyQualifiedName~PostgresIdentityLifecycle'
    $args = @('test','work/production/tests/Task.Tests/Task.Tests.csproj','-c','Release','--no-build','--no-restore','--logger','trx;LogFileName=postgres-scoped.trx','--results-directory',$PSScriptRoot,'--filter',$filter)
    Check 'postgres-scoped' $args
    if ($Full) {
        foreach ($project in @('Task.Tests','Task.ServiceHosts.Tests','Task.Desktop.Tests')) {
            Check $project @('test',"work/production/tests/$project/$project.csproj",'-c','Release','--no-build','--no-restore','--logger',"trx;LogFileName=$project.trx",'--results-directory',$PSScriptRoot)
        }
    }
}
finally {
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = $savedConnection
    if (Test-Path -LiteralPath (Join-Path $runtime 'postmaster.pid')) { & "$pg/pg_ctl.exe" -D $runtime -m fast -w stop | Out-Null }
    $alias = Get-Item -LiteralPath $runtimeAlias
    if ($alias.LinkType -eq 'Junction' -and $alias.Target -eq $runtimeDirectory) { Remove-Item -LiteralPath $runtimeAlias -Force }
    Pop-Location
}
