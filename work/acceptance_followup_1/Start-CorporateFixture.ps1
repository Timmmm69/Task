param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$runtime = Join-Path $env:LOCALAPPDATA 'TaskE2ERuntime/task-write-e2e'
if (Test-Path -LiteralPath $runtime) { throw 'Fixture runtime already exists.' }
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$phase = (Resolve-Path "$PSScriptRoot/../production/verification/Test-TaskWriteE2E.ps1").Path
$data = Join-Path $runtime 'desktop-appdata'
$process = Start-Process -FilePath (Get-Command pwsh).Source -WindowStyle Hidden -PassThru `
    -ArgumentList @('-NoProfile','-File',('"' + $phase + '"'),'-Phase','Setup','-DesktopAppDataPath',('"' + $data + '"')) `
    -RedirectStandardOutput (Join-Path $EvidenceDirectory 'setup.stdout.log') `
    -RedirectStandardError (Join-Path $EvidenceDirectory 'setup.stderr.log')
try {
    if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Fixture setup timed out.' }
    if ($process.ExitCode -ne 0) { throw 'Fixture setup failed.' }
} finally { $process.Dispose() }
$state = Get-Content (Join-Path $runtime 'state.json') -Raw | ConvertFrom-Json
@{result='PASS';postgresMajor=16;port=$state.PostgresPort;https=$state.BaseUrl;dataRoot=$data;isolated=$true} |
    ConvertTo-Json | Set-Content (Join-Path $EvidenceDirectory 'fixture.json') -Encoding utf8
Write-Output 'PASS: isolated PostgreSQL 16 and real HTTPS Task.Api fixture started.'
