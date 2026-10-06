param([Parameter(Mandatory)][string]$PackageDirectory, [Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$manifest = Get-Content -LiteralPath (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
if (-not $manifest.cleanSourceTree) { throw 'Delivery requires committed source.' }
$archive = Join-Path $package "Task-$($manifest.version)-win-x64-unsigned.zip"
$archiveHash = (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
& "$PSScriptRoot/../production/deployment/desktop/Test-UnsignedPortableClient.ps1" -PackageDirectory $package | Out-Host
$allowed = @('D3DCompiler_47_cor3.dll','e_sqlite3.dll','PenImc_cor3.dll','PresentationNative_cor3.dll','Task.Desktop.exe','vcruntime140_cor3.dll','wpfgfx_cor3.dll')
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $entries = @($zip.Entries | Where-Object Name | ForEach-Object FullName)
    if ($entries.Count -ne $allowed.Count) { throw 'Unexpected ZIP inventory.' }
    foreach ($entry in $entries) {
        if ($entry.Replace('\','/') -notin @($allowed | ForEach-Object { 'Task/' + $_ })) { throw "Forbidden ZIP entry: $entry" }
    }
} finally { $zip.Dispose() }
$run = Join-Path $PSScriptRoot ('runtime-' + [Guid]::NewGuid().ToString('N'))
$extracted = Join-Path $run 'extracted'
Expand-Archive -LiteralPath $archive -DestinationPath $extracted
$exe = Join-Path $extracted 'Task/Task.Desktop.exe'
$exeHash = (Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant()
$checks = [Collections.Generic.List[object]]::new()
foreach ($mode in @('FirstRun','Personal','Corporate','PersonalRestart')) {
    $root = Join-Path $run $(if ($mode -eq 'PersonalRestart') { 'Personal' } else { $mode })
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    if ($mode -in @('Personal','Corporate')) {
        @{version=1;mode=$mode} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'application-preferences.json') -Encoding utf8
    }
    $title = switch ($mode) { 'FirstRun' {'Task — выбор режима'} 'Corporate' {'Task — Corporate · вход'} default {'Task — Personal'} }
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = Split-Path $exe
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.Environment['TASK_DESKTOP_DATA_DIRECTORY'] = $root
    $process = [Diagnostics.Process]::Start($start)
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        do {
            $process.Refresh()
            if ($process.HasExited) { throw "Delivered EXE exited during $mode smoke." }
            if ($process.MainWindowTitle -eq $title) { break }
            Start-Sleep -Milliseconds 200
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($process.MainWindowTitle -ne $title) { throw "Unexpected $mode window: $($process.MainWindowTitle)" }
        $checks.Add(@{scenario=$mode;result='PASS';title=$title;dataOutsideApplication=$true})
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(10000)) { throw 'Application did not close normally.' }
    } finally {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}
$signature = Get-AuthenticodeSignature -LiteralPath $exe
if ($signature.Status -ne 'NotSigned') { throw 'Unexpected unsigned delivery signature state.' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
if (-not $version.StartsWith($manifest.version)) { throw 'Wrong executable version.' }
$icon = Join-Path $PSScriptRoot '../production/src/Task.Desktop/Assets/Task.ico'
if ((Get-FileHash -LiteralPath $icon).Hash.ToLowerInvariant() -ne '1500bf60892702387af3a458e2718069c2ab38a47cc9b4a8008f63df0083d93c') { throw 'Non-canonical Task icon.' }
@{result='PASS';archiveSha256=$archiveHash;sourceCommit=$manifest.sourceRevision;exeSha256=$exeHash;version=$version;signature=[string]$signature.Status;inventory=$entries;checks=$checks.ToArray();excluded='Only seven exact runtime files: no databases, caches, passwords/tokens, keys, certificates, installer or test fixtures';cleanWindows=$false;physicalOffline=$false;visiblePopup='NOT RUN'} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $evidence 'delivery-smoke.json') -Encoding utf8
# The persistence harness receives the verified extraction of this exact delivered ZIP.
& pwsh -NoProfile -File "$PSScriptRoot/../portable_environment_acceptance_1/Test-CandidatePersistence.ps1" -PackageDirectory $extracted -EvidenceDirectory (Join-Path $evidence 'persistence')
if ($LASTEXITCODE -ne 0) { throw 'Delivered ZIP persistence/replacement acceptance failed.' }
if ((Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() -ne $archiveHash) { throw 'Delivery ZIP changed during smoke.' }
Write-Output 'PASS: exact delivered ZIP smoke, restart, replacement, mode separation and inventory.'
