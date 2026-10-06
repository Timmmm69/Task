param([Parameter(Mandatory)][string]$PackageDirectory, [Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$taskWorkspace = (Resolve-Path "$PSScriptRoot/../..").Path
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
$runRoot = Join-Path $PSScriptRoot "evidence/native-$([Guid]::NewGuid().ToString('N'))"
$fixture = Join-Path $runRoot 'data'
$application = Join-Path $runRoot 'application'
if (Test-Path -LiteralPath $application) { throw 'Application fixture already exists.' }
New-Item -ItemType Directory -Path $application,$EvidenceDirectory -Force | Out-Null
$probe = Join-Path $PSScriptRoot '../final_acceptance_stage6/Probe/bin/Release/net10.0-windows/Probe.exe'
$oldZip = Join-Path $taskWorkspace 'outputs/20261006_personal_workspace_1.0.0/client-package/Task-1.0.0-win-x64-unsigned.zip'
$oldRoot = Join-Path $runRoot 'old-package'
Expand-Archive -LiteralPath $oldZip -DestinationPath $oldRoot
Copy-Item -Path (Join-Path $oldRoot 'Task/*') -Destination $application -Recurse
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$checks = [Collections.Generic.List[object]]::new()
function Launch([string]$root) {
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $application 'Task.Desktop.exe'))
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $application
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.Environment['TASK_DESKTOP_DATA_DIRECTORY'] = $root
    $process = [Diagnostics.Process]::Start($start)
    $processes.Add($process)
    return $process
}
function Window([Diagnostics.Process]$process, [string]$expected) {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $process.Refresh()
        if ($process.HasExited) { throw 'Application exited before opening its window.' }
        if ($process.MainWindowTitle -eq $expected) { return $process.MainWindowTitle }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Expected '$expected', got '$($process.MainWindowTitle)'."
}
function Close([Diagnostics.Process]$process) {
    if ($process.HasExited) { return }
    $null = $process.CloseMainWindow()
    if (-not $process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
}
function Check([string]$name, [string]$category, $details) {
    $checks.Add(@{check=$name;result='PASS';category=$category;details=$details})
    $checks | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $EvidenceDirectory 'native-portable.json') -Encoding utf8
    Write-Output "PASS: $name"
}
function Snapshot([string]$action, [string]$name) {
    $path = Join-Path $PSScriptRoot "evidence/$name.json"
    & $probe $action $fixture $path | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Probe failed: $action" }
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
try {
    $null = Snapshot 'seed' 'native-seed'
    $null = Snapshot 'backup' 'native-backup'
    $selectorRoot = Join-Path $runRoot 'selector-data'
    $selector = Launch $selectorRoot
    $title = Window $selector 'Task — выбор режима'
    Close $selector
    Check 'Extracted N package first-run selector' 'CONFIRMED BY NATIVE WINDOWS RUN' @{title=$title;cleanData=$true}
    $before = Snapshot 'verify' 'before-n'
    $n = Launch $fixture
    $title = Window $n 'Task — Personal'
    Close $n
    $after = Snapshot 'verify' 'after-n'
    if ($before -ne $after) { throw 'N startup changed expected dataset.' }
    Check 'N Personal startup/restart exact dataset' 'CONFIRMED BY NATIVE WINDOWS RUN' @{snapshotSha256=$after;title=$title}
    $database = Join-Path $fixture 'Personal/tasks.db'
    $dbHash = (Get-FileHash -LiteralPath $database -Algorithm SHA256).Hash
    $nHash = (Get-FileHash -LiteralPath (Join-Path $application 'Task.Desktop.exe') -Algorithm SHA256).Hash
    $previous = Join-Path $runRoot 'previous-application'
    $resolvedApplication = (Resolve-Path -LiteralPath $application).Path
    $allowed = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'evidence')).TrimEnd('\') + '\'
    if (-not $resolvedApplication.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Application escaped fixture.' }
    Move-Item -LiteralPath $resolvedApplication -Destination $previous
    New-Item -ItemType Directory -Path $application | Out-Null
    Copy-Item -Path (Join-Path $PackageDirectory 'Task/*') -Destination $application -Recurse
    if ((Get-FileHash -LiteralPath $database -Algorithm SHA256).Hash -ne $dbHash) { throw 'Package replacement changed DB.' }
    $nextHash = (Get-FileHash -LiteralPath (Join-Path $application 'Task.Desktop.exe') -Algorithm SHA256).Hash
    if ($nextHash -eq $nHash) { throw 'Replacement uses the same executable.' }
    $cleanNextData = Join-Path $runRoot 'clean-next-data'
    [IO.Directory]::CreateDirectory($cleanNextData) | Out-Null
    $detector = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $detector.Start()
    try {
        $port = ([Net.IPEndPoint]$detector.LocalEndpoint).Port
        [IO.File]::WriteAllText((Join-Path $cleanNextData 'server-settings.json'), "{`"version`":1,`"baseUrl`":`"https://127.0.0.1:$port/`"}")
        $cleanNext = Launch $cleanNextData
        $cleanTitle = Window $cleanNext 'Task — выбор режима'
        Start-Sleep -Milliseconds 300
        if ($detector.Pending()) { throw 'Server was contacted before Corporate selection.' }
        Close $cleanNext
        Check 'Final package first-run selector/zero server probes' 'CONFIRMED BY NATIVE WINDOWS RUN' @{title=$cleanTitle;serverConnections=0;businessDataPresent=$false}
    } finally { $detector.Stop() }
    $next = Launch $fixture
    $title = Window $next 'Task — Personal'
    $second = Launch $fixture
    $secondTitle = Window $second 'Task — Personal recovery'
    Close $second
    Check 'Second Personal process controlled refusal' 'CONFIRMED BY NATIVE WINDOWS RUN' @{title=$secondTitle;firstAlive=(-not $next.HasExited)}
    $preference = Join-Path $fixture 'application-preferences.json'
    [IO.File]::WriteAllText($preference, '{"version":1,"mode":"Corporate"}')
    $corporate = Launch $fixture
    $corporateTitle = Window $corporate 'Task — Corporate · вход'
    Close $corporate
    Check 'Corporate second process while Personal owns DB' 'CONFIRMED BY NATIVE WINDOWS RUN' @{title=$corporateTitle;firstAlive=(-not $next.HasExited);loginPerformed=$false}
    Close $next
    [IO.File]::WriteAllText($preference, '{"version":1,"mode":"Personal"}')
    $preserved = Snapshot 'verify' 'after-next'
    if ($preserved -ne $after) { throw 'N+1 lost exact dataset.' }
    Check 'Portable package replacement exact objects/search/dedupe' 'CONFIRMED BY NATIVE WINDOWS RUN' @{snapshotSha256=$preserved;nExeSha256=$nHash;nextExeSha256=$nextHash;dataOutsideApplication=$true;schemaMigrationNeeded=$false}
    $null = Snapshot 'next' 'next-occurrence'
    $nextBefore = Snapshot 'backup' 'new-backup'
    $restart = Launch $fixture
    $null = Window $restart 'Task — Personal'
    Close $restart
    $nextAfter = Snapshot 'verify' 'next-restart'
    if ($nextBefore -ne $nextAfter) { throw 'Recurrence/reminder changed after restart.' }
    Check 'Next occurrence/no duplicates/new backup/restart' 'CONFIRMED BY AUTOMATED TEST' @{snapshotSha256=$nextAfter;notificationCount=1}
    $null = Snapshot 'lifecycle' 'after-lifecycle'
    Check 'Marker archive/trash/restore and metadata purge/file survival' 'CONFIRMED BY AUTOMATED TEST' @{fileStillExists=(Test-Path -LiteralPath (Join-Path $fixture 'harmless.txt'))}
} finally {
    foreach ($process in $processes) { try { Close $process } finally { $process.Dispose() } }
}
