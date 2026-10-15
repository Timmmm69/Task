param([string]$Filter='FullyQualifiedName~Inbox', [string]$Project='work/production/Task.sln', [string]$Configuration='Debug')
$ErrorActionPreference='Stop'
$taskFilesystem=New-Object -ComObject Scripting.FileSystemObject
$taskShortRoot=$taskFilesystem.GetFolder($PSScriptRoot).ShortPath
$taskRoot=Join-Path $taskShortRoot ('pg-'+[guid]::NewGuid().ToString('N'))
$pg='C:\Program Files\PostgreSQL\16\bin'
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$listener.Start(); $port=([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
$savedConnection=$env:TASK_POSTGRES_TEST_ADMIN_CONNECTION
$evidence=Join-Path $PSScriptRoot ('gate-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
try {
 & "$pg/initdb.exe" -D $taskRoot -U postgres --auth=trust --encoding=UTF8 --locale=C *> (Join-Path $evidence 'postgres-init.log')
 if($LASTEXITCODE -ne 0) { throw 'initdb failed' }
 Start-Process -FilePath "$pg/postgres.exe" -ArgumentList "-D `"$taskRoot`" -h 127.0.0.1 -p $port" -WindowStyle Hidden -RedirectStandardOutput (Join-Path $evidence 'postgres.log') -RedirectStandardError (Join-Path $evidence 'postgres-error.log')
 $ready=$false
 for($i=0;$i -lt 50;$i++) { & "$pg/pg_isready.exe" -h 127.0.0.1 -p $port -U postgres *> $null; if($LASTEXITCODE -eq 0){$ready=$true;break}; Start-Sleep -Milliseconds 200 }
 if(-not $ready){throw 'PostgreSQL did not start'}
 $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION="Host=127.0.0.1;Port=$port;Database=postgres;Username=postgres;Pooling=false"
 $argsList=@('test',$Project,'-c',$Configuration,'--no-restore','--logger','trx','--results-directory',$evidence)
 if($Filter){$argsList+=@('--filter',$Filter)}
 $ErrorActionPreference='Continue'
 & dotnet @argsList *> (Join-Path $evidence 'tests.log')
 $result=$LASTEXITCODE
 $ErrorActionPreference='Stop'
 Get-Content (Join-Path $evidence 'tests.log') -Tail 22
 @{evidence=$evidence;exitCode=$result;postgres16=$true}|ConvertTo-Json|Set-Content (Join-Path $PSScriptRoot 'latest-gate.json')
 exit $result
} finally {
 $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION=$savedConnection
 if(Test-Path -LiteralPath (Join-Path $taskRoot 'postmaster.pid')){& "$pg/pg_ctl.exe" -D $taskRoot -m fast -w stop *> (Join-Path $evidence 'postgres-stop.log')}
}
