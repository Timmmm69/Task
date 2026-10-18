$ErrorActionPreference = 'Stop'
$taskRepo = (Get-Location).Path
$gateRoot = Join-Path $taskRepo 'work/crm_interactions_12_4'
$gateEvidence = Join-Path $gateRoot ('evidence/run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $gateEvidence | Out-Null
$pgBin = 'C:/Program Files/PostgreSQL/16/bin'
$shortGateRoot = (New-Object -ComObject Scripting.FileSystemObject).GetFolder($gateRoot).ShortPath
$pgData = Join-Path $shortGateRoot ('postgres-' + [guid]::NewGuid().ToString('N'))
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $pgPort = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
try {
 & "$pgBin/initdb.exe" -D $pgData -U postgres --auth=trust --encoding=UTF8 --locale=C *> (Join-Path $gateEvidence 'postgres-init.log')
 if ($LASTEXITCODE -ne 0) { throw 'initdb failed' }
 Start-Process -FilePath "$pgBin/postgres.exe" -ArgumentList "-D `"$pgData`" -h 127.0.0.1 -p $pgPort" -WindowStyle Hidden -RedirectStandardOutput (Join-Path $gateEvidence 'postgres.log') -RedirectStandardError (Join-Path $gateEvidence 'postgres-error.log')
 $pgReady = $false
 for ($attempt = 0; $attempt -lt 50; $attempt++) {
  & "$pgBin/pg_isready.exe" -h 127.0.0.1 -p $pgPort -U postgres *> $null
  if ($LASTEXITCODE -eq 0) { $pgReady = $true; break }; Start-Sleep -Milliseconds 200
 }
 if (!$pgReady) { throw 'PostgreSQL not ready' }
 $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = "Host=127.0.0.1;Port=$pgPort;Database=postgres;Username=postgres;Pooling=false"
 $env:TASK_INTERACTIONS_EVIDENCE = Join-Path $gateEvidence 'ui-interactions'
 $env:TASK_COMPANY_CARD_EVIDENCE = Join-Path $gateEvidence 'ui-companies'
 $env:TASK_CONTACT_CARD_EVIDENCE = Join-Path $gateEvidence 'ui-contacts'
 & dotnet test work/production/Task.sln -c Release --no-restore -v minimal --logger trx --results-directory $gateEvidence *> (Join-Path $gateEvidence 'tests.log')
 $gateExit = $LASTEXITCODE
 Get-Content (Join-Path $gateEvidence 'tests.log') | Select-Object -Last 60
 if ($gateExit -ne 0) { throw "Release gate failed: $gateExit" }
} finally {
 if (Test-Path -LiteralPath (Join-Path $pgData 'postmaster.pid')) { & "$pgBin/pg_ctl.exe" -D $pgData -m fast -w stop | Out-Null }
}
