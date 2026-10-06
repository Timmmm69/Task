param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$uri = 'https://get.enterprisedb.com/postgresql/postgresql-15.19-1-windows-x64-binaries.zip'
$archive = Join-Path $PSScriptRoot 'postgresql-15.19-1-windows-x64-binaries.zip'
$binaries = Join-Path $PSScriptRoot 'postgres15-binaries'
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $uri -OutFile $archive -TimeoutSec 180 }
if (-not (Test-Path -LiteralPath (Join-Path $binaries 'pgsql/bin/postgres.exe'))) {
    New-Item -ItemType Directory -Path $binaries -Force | Out-Null
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($entry in $zip.Entries) {
            if (-not $entry.Name -or $entry.FullName -notmatch '^pgsql/(bin|lib|share)/') { continue }
            $path = [IO.Path]::GetFullPath((Join-Path $binaries $entry.FullName))
            if (-not $path.StartsWith($binaries + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Archive path escaped extraction root.' }
            New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path, $false)
        }
    } finally { $zip.Dispose() }
}
$asciiBinaries = Join-Path $env:LOCALAPPDATA ('TaskE2ERuntime/pg15-tools-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $asciiBinaries -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $binaries 'pgsql') -Destination $asciiBinaries -Recurse
$bin = Join-Path $asciiBinaries 'pgsql/bin'
$root = Join-Path $env:LOCALAPPDATA ('TaskE2ERuntime/pg15-' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $root 'data'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$state = @{root=$root;data=$data;pid=0;port=0;bin=$bin;tools=$asciiBinaries;uri=$uri;archiveSha256=(Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()}
$state | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'pg15-state.json') -Encoding utf8
& (Join-Path $bin 'initdb.exe') -D $data -U postgres -A trust --encoding=UTF8 --no-locale *> (Join-Path $EvidenceDirectory 'initdb.log')
if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL 15 initdb failed.' }
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
$process = Start-Process -FilePath (Join-Path $bin 'postgres.exe') -WindowStyle Hidden -PassThru `
    -ArgumentList @('-D', ('"' + $data + '"'), '-p', $port, '-h', '127.0.0.1') `
    -RedirectStandardOutput (Join-Path $root 'postgres.stdout.log') -RedirectStandardError (Join-Path $root 'postgres.stderr.log')
$state.pid = $process.Id; $state.port = $port
$state | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'pg15-state.json') -Encoding utf8
$deadline = [DateTime]::UtcNow.AddSeconds(30)
do {
    & (Join-Path $bin 'pg_isready.exe') -h 127.0.0.1 -p $port -U postgres *> $null
    if ($LASTEXITCODE -eq 0) { break }
    if ($process.HasExited) { throw 'PostgreSQL 15 process exited.' }
    Start-Sleep -Milliseconds 200
} while ([DateTime]::UtcNow -lt $deadline)
if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL 15 did not become ready.' }
$version = & (Join-Path $bin 'postgres.exe') --version
@{result='PASS';version=$version;port=$port;source=$uri;archiveSha256=$state.archiveSha256;isolated=$true;serviceInstalled=$false} |
    ConvertTo-Json | Set-Content (Join-Path $EvidenceDirectory 'fixture.json') -Encoding utf8
Write-Output "PASS: $version fixture on loopback, no installed service."
