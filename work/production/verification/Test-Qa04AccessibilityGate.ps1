[CmdletBinding()]
param(
    [string]$EvidenceDirectory = '',
    [switch]$SkipBuild,
    [switch]$ReuseChildEvidence
)

if ($PSVersionTable.PSEdition -ne 'Core') {
    $pwsh = Get-Command pwsh.exe -ErrorAction Stop
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($EvidenceDirectory) { $arguments += @('-EvidenceDirectory', $EvidenceDirectory) }
    if ($SkipBuild) { $arguments += '-SkipBuild' }
    if ($ReuseChildEvidence) { $arguments += '-ReuseChildEvidence' }
    & $pwsh.Source @arguments
    exit $LASTEXITCODE
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'outputs'))
if (-not $EvidenceDirectory) {
    $EvidenceDirectory = Join-Path $outputRoot '20260911_qa04_accessibility_steps_1_3_1.0.0\evidence'
}
$evidenceRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
$outputPrefix = $outputRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $evidenceRoot.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'EvidenceDirectory must be inside the repository outputs directory.'
}

$qa03Evidence = Join-Path $evidenceRoot 'qa03'
$desk05Evidence = Join-Path $evidenceRoot 'desk05'
$qa03Gate = Join-Path $PSScriptRoot 'Test-Qa03Gate.ps1'
$desk05Gate = Join-Path $PSScriptRoot 'Test-Desk05WindowsUx.ps1'
$matrixPath = Join-Path $repoRoot 'work\production\docs\QA-04-accessibility-usability-matrix.md'
$desktopExe = Join-Path $repoRoot 'work\production\src\Task.Desktop\bin\Release\net10.0-windows\Task.Desktop.exe'
$startedAt = [DateTimeOffset]::UtcNow

[IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null

function Assert-Qa04([bool]$condition, [string]$message) {
    if (-not $condition) { throw "QA-04 assertion failed: $message" }
    Write-Host "[PASS] $message"
}

function Invoke-Gate([string]$name, [string]$script, [string[]]$arguments) {
    $logPath = Join-Path $evidenceRoot "$name.log"
    $output = & (Get-Command pwsh.exe).Source -NoProfile -ExecutionPolicy Bypass -File $script @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    [IO.File]::WriteAllText($logPath, $text + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) { throw "$name failed with exit code $exitCode." }
}

if (-not $ReuseChildEvidence) {
    $qa03Arguments = @('-EvidenceDirectory', $qa03Evidence)
    if ($SkipBuild) { $qa03Arguments += '-SkipBuild' }
    Invoke-Gate 'qa03-child-gate' $qa03Gate $qa03Arguments
    Invoke-Gate 'desk05-child-gate' $desk05Gate @('-EvidenceDirectory', $desk05Evidence, '-SkipBuild')
}

$uiEvidence = Get-Content -LiteralPath (Join-Path $qa03Evidence 'ui-assertions.json') -Raw | ConvertFrom-Json
$windowsEvidence = Get-Content -LiteralPath (Join-Path $desk05Evidence 'windows-ux.json') -Raw | ConvertFrom-Json
$cleanupLog = Get-Content -LiteralPath (Join-Path $qa03Evidence 'phase-cleanup.stdout.log') -Raw
$matrixText = Get-Content -LiteralPath $matrixPath -Raw
$desktopFile = Get-Item -LiteralPath $desktopExe
$desktopHash = Get-FileHash -LiteralPath $desktopExe -Algorithm SHA256

Assert-Qa04 ($uiEvidence.result -eq 'PASS') 'Critical Release WPF scenario evidence is PASS.'
Assert-Qa04 ($uiEvidence.seedProfile -eq 'qa03-deterministic-v1') 'Deterministic synthetic data profile is recorded.'
Assert-Qa04 ($cleanupLog -match 'original Desktop app-data restored') 'Clean stand restored the original Desktop AppData.'
foreach ($name in @(
        'today', 'taskCreate', 'taskConflict', 'taskConflictRecovery', 'calendar',
        'projectCreate', 'catalogCreate', 'contactCreate', 'search', 'notifications',
        'offlineShell', 'reconnect')) {
    Assert-Qa04 ($uiEvidence.uiChecks.$name -eq 'PASS') "Critical UI scenario '$name' is PASS."
}

Assert-Qa04 ($windowsEvidence.result -eq 'PASS') 'Native Windows evidence is PASS.'
Assert-Qa04 ($windowsEvidence.dataProfile -match 'synthetic only') 'Native checks use synthetic data only.'
Assert-Qa04 ($windowsEvidence.nativeUia.authentication -eq 'PASS') 'Authentication UIA contract is PASS.'
Assert-Qa04 ($windowsEvidence.nativeUia.mainWindow -eq 'PASS') 'Main-window UIA contract is PASS.'
Assert-Qa04 ((@($windowsEvidence.nativeUia.patterns) -join ',') -eq 'Value,Invoke,Selection') `
    'Required Value, Invoke and Selection UIA patterns are present.'
Assert-Qa04 ($windowsEvidence.keyboard.result -eq 'PASS') 'Keyboard navigation is PASS.'
Assert-Qa04 (@($windowsEvidence.keyboard.authenticationFocusPath) -contains 'CheckServerConnectionButton') `
    'Authentication Tab path reaches the next enabled action.'
Assert-Qa04 (@($windowsEvidence.keyboard.f6HeaderPath) -contains 'TasksRefreshButton') `
    'F6 reaches the header command region.'
Assert-Qa04 (@($windowsEvidence.keyboard.f6ContentPath) -contains 'TasksList') `
    'F6 reaches the selected content region.'
Assert-Qa04 (@($windowsEvidence.keyboard.f6NavigationPath) -contains 'NavigationListBox') `
    'F6 returns to the primary navigation region.'
Assert-Qa04 ($matrixText -match 'ENV-01' -and $matrixText -match 'SCN-05') `
    'The controlled accessibility matrix is present and complete for steps 1-3.'

$result = [ordered]@{
    schemaVersion = 1
    task = 'QA-04'
    scope = 'steps-1-3'
    result = 'PASS'
    completion = @{
        matrixDefined = 'PASS'
        reproducibleSyntheticStand = 'PASS'
        keyboardAndNativeUia = 'PASS'
        overallQa04 = 'IN_PROGRESS'
    }
    platform = $windowsEvidence.platform
    seedProfile = $uiEvidence.seedProfile
    releaseArtifact = @{
        path = 'work/production/src/Task.Desktop/bin/Release/net10.0-windows/Task.Desktop.exe'
        bytes = $desktopFile.Length
        sha256 = $desktopHash.Hash.ToLowerInvariant()
        lastWriteTimeUtc = $desktopFile.LastWriteTimeUtc.ToString('O')
    }
    criticalUiScenarios = $uiEvidence.uiChecks
    nativeUia = $windowsEvidence.nativeUia
    keyboard = $windowsEvidence.keyboard
    matrixDocument = 'work/production/docs/QA-04-accessibility-usability-matrix.md'
    childEvidence = @{
        qa03 = 'evidence/qa03'
        desk05 = 'evidence/desk05'
    }
    startedAtUtc = $startedAt.ToString('O')
    completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
}
[IO.File]::WriteAllText(
    (Join-Path $evidenceRoot 'qa04-accessibility.json'),
    (($result | ConvertTo-Json -Depth 10) + "`n"),
    [Text.UTF8Encoding]::new($false))
Write-Host '[PASS] QA-04 steps 1-3 accessibility gate passed.'
