[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EvidenceDirectory,
    [switch]$SkipBuild
)

if ($PSVersionTable.PSEdition -ne 'Core') {
    $pwsh = Get-Command pwsh.exe -ErrorAction Stop
    $forward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath,
        '-EvidenceDirectory', $EvidenceDirectory)
    if ($SkipBuild) { $forward += '-SkipBuild' }
    & $pwsh.Source @forward
    exit $LASTEXITCODE
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'outputs'))
$evidenceRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
$outputPrefix = $outputRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $evidenceRoot.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'EvidenceDirectory must be inside the repository outputs directory.'
}

$runtimeRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'TaskE2ERuntime\task-write-e2e'))
$expectedRuntime = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'TaskE2ERuntime\task-write-e2e'))
if ($runtimeRoot -ne $expectedRuntime) { throw 'Unexpected QA-02 runtime path.' }
$phaseScript = Join-Path $PSScriptRoot 'Test-TaskWriteE2E.ps1'
$desktopExe = Join-Path $repoRoot 'work\production\src\Task.Desktop\bin\Release\net10.0-windows\Task.Desktop.exe'
$statePath = Join-Path $runtimeRoot 'state.json'
$runLog = Join-Path $evidenceRoot 'qa02-clean-stand.log'
$buildLog = Join-Path $evidenceRoot 'qa02-release-build.log'
$desktopProcess = $null
$setupStarted = $false
$startedAt = [DateTimeOffset]::UtcNow

[IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null
[IO.File]::WriteAllText($runLog, '', [Text.UTF8Encoding]::new($false))

function Write-Pass([string]$message) { Write-Host "[PASS] $message" }
function Assert-Qa02([bool]$condition, [string]$message) {
    if (-not $condition) { throw "QA-02 assertion failed: $message" }
    Write-Pass $message
}
function Append-Log([object[]]$content, [string]$path) {
    $text = ($content | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    [IO.File]::AppendAllText($path, $text + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}
function Invoke-Phase([string]$phase, [string[]]$extra = @()) {
    $heading = "=== Test-TaskWriteE2E phase: $phase ==="
    Append-Log @($heading) $runLog
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $phaseScript,
        '-Phase', $phase) + $extra
    $phaseStem = $phase.ToLowerInvariant()
    $stdoutPath = Join-Path $evidenceRoot "phase-$phaseStem.stdout.log"
    $stderrPath = Join-Path $evidenceRoot "phase-$phaseStem.stderr.log"
    $process = Start-Process -FilePath (Get-Command pwsh.exe).Source -ArgumentList $arguments `
        -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru
    $process.WaitForExit()
    $exitCode = $process.ExitCode
    $output = @()
    if (Test-Path -LiteralPath $stdoutPath) { $output += Get-Content -LiteralPath $stdoutPath }
    if (Test-Path -LiteralPath $stderrPath) { $output += Get-Content -LiteralPath $stderrPath }
    $process.Dispose()
    $output | ForEach-Object { Write-Host $_ }
    Append-Log $output $runLog
    if ($exitCode -ne 0) { throw "Test-TaskWriteE2E phase '$phase' failed with exit code $exitCode." }
}
function Stop-Desktop {
    if ($null -eq $script:desktopProcess) { return }
    $process = Get-Process -Id $script:desktopProcess.Id -ErrorAction SilentlyContinue
    if ($process) {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(10000)) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit(5000) | Out-Null
        }
    }
    $script:desktopProcess = $null
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Find-ElementById([System.Windows.Automation.AutomationElement]$root, [string]$automationId) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Wait-ElementById([System.Windows.Automation.AutomationElement]$root, [string]$automationId,
    [int]$timeoutSeconds = 45, [switch]$Enabled) {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    do {
        $element = Find-ElementById $root $automationId
        if ($null -ne $element -and (-not $Enabled -or $element.Current.IsEnabled)) { return $element }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "UI Automation element '$automationId' was not ready within $timeoutSeconds seconds."
}
function Invoke-Element([System.Windows.Automation.AutomationElement]$element) {
    $pattern = [System.Windows.Automation.InvokePattern]$element.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}
function Select-Element([System.Windows.Automation.AutomationElement]$element) {
    $pattern = [System.Windows.Automation.SelectionItemPattern]$element.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
}
function Set-ElementValue([System.Windows.Automation.AutomationElement]$element, [string]$value) {
    $pattern = [System.Windows.Automation.ValuePattern]$element.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($value)
}
function Wait-TaskItem([System.Windows.Automation.AutomationElement]$window, [string]$title,
    [int]$timeoutSeconds = 45) {
    $list = Wait-ElementById $window 'TasksList' $timeoutSeconds
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    do {
        $items = $list.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
        foreach ($item in $items) {
            if ($item.Current.Name.StartsWith($title + '.', [StringComparison]::Ordinal)) { return $item }
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Task '$title' did not appear in the WPF task list."
}
function Start-DesktopAndOpenTasks([string]$taskTitle) {
    Assert-Qa02 (Test-Path -LiteralPath $desktopExe -PathType Leaf) 'Release WPF executable exists.'
    $script:desktopProcess = Start-Process -FilePath $desktopExe -WorkingDirectory (Split-Path $desktopExe) -PassThru
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:desktopProcess.Id)
    $windowCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'MainWindow')
    $condition = [System.Windows.Automation.AndCondition]::new($processCondition, $windowCondition)
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $window = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($null -ne $window) { break }
        if ($script:desktopProcess.HasExited) { throw 'Task.Desktop exited before opening MainWindow.' }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -eq $window) { throw 'Authenticated Task MainWindow did not open within 45 seconds.' }

    Select-Element (Wait-ElementById $window 'Navigation_tasks')
    Wait-ElementById $window 'NewTaskButton' | Out-Null
    if ($taskTitle) { Wait-TaskItem $window $taskTitle | Out-Null }
    return $window
}

try {
    if (Test-Path -LiteralPath $runtimeRoot) {
        throw "QA-02 clean runtime already exists at $runtimeRoot. Remove it only via the Cleanup phase."
    }
    if (-not $SkipBuild) {
        $buildOutput = & dotnet build (Join-Path $repoRoot 'work\production\Task.sln') `
            --configuration Release --no-restore 2>&1
        $buildExit = $LASTEXITCODE
        [IO.File]::WriteAllLines($buildLog, ($buildOutput | ForEach-Object { $_.ToString() }), [Text.UTF8Encoding]::new($false))
        $buildOutput | ForEach-Object { Write-Host $_ }
        Assert-Qa02 ($buildExit -eq 0) 'Release solution build passed.'
    }

    $setupStarted = $true
    Invoke-Phase 'Setup'
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    Assert-Qa02 ($state.UiInitialTitle -eq 'QA-02 deterministic WPF task') 'QA-02 deterministic seed profile is active.'

    $window = Start-DesktopAndOpenTasks ''
    Invoke-Element (Wait-ElementById $window 'NewTaskButton' 45 -Enabled)
    $title = Wait-ElementById $window 'TaskTitleTextBox'
    Set-ElementValue $title $state.UiInitialTitle
    Invoke-Element (Wait-ElementById $window 'SaveTaskButton' 15 -Enabled)
    $createdItem = Wait-TaskItem $window $state.UiInitialTitle
    Assert-Qa02 ($createdItem.Current.Name -match 'Статус: Новая') 'Admin created the deterministic task through the real WPF UI.'
    $adminConnection = (Wait-ElementById $window 'ConnectionStatusText').Current.Name
    Assert-Qa02 ($adminConnection -match '^Подключено к серверу компании') 'Admin WPF is connected to the production HTTPS API.'
    Stop-Desktop

    Invoke-Phase 'VerifyCritical' @('-EvidencePath', $evidenceRoot)
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json

    $window = Start-DesktopAndOpenTasks $state.UiInitialTitle
    $reopenedItem = Wait-TaskItem $window $state.UiInitialTitle
    Assert-Qa02 ($reopenedItem.Current.Name -match 'Статус: Новая') 'Fresh WPF process rendered the persisted task after API restart.'
    Stop-Desktop

    Invoke-Phase 'SeedReadOnlyDesktop'
    $window = Start-DesktopAndOpenTasks ''
    $newTask = Wait-ElementById $window 'NewTaskButton'
    Assert-Qa02 (-not $newTask.Current.IsEnabled) 'Read-only WPF session can read tasks and cannot start task creation.'
    $readerNotice = (Wait-ElementById $window 'ReadOnlyNoticeText').Current.Name
    $readerConnection = (Wait-ElementById $window 'ConnectionStatusText').Current.Name

    $uiEvidence = [ordered]@{
        schemaVersion = 1
        scenario = 'QA-02 critical task/auth clean-stand E2E'
        seedProfile = 'qa02-deterministic-v1'
        result = 'PASS'
        executable = 'work/production/src/Task.Desktop/bin/Release/net10.0-windows/Task.Desktop.exe'
        taskTitle = $state.UiInitialTitle
        adminCreateThroughWpf = 'PASS'
        adminConnection = $adminConnection
        apiRestartAndFreshWpfProcess = 'PASS'
        readOnlyTaskScreen = 'PASS'
        readOnlyAdminTaskVisibility = 'FILTERED_AS_EXPECTED'
        readOnlyCreateDisabled = $true
        readOnlyNotice = $readerNotice
        readOnlyConnection = $readerConnection
        startedAtUtc = $startedAt.ToString('O')
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'ui-assertions.json'),
        (($uiEvidence | ConvertTo-Json -Depth 5) + "`n"), [Text.UTF8Encoding]::new($false))
    Write-Pass 'QA-02 CLEAN-STAND TASK/AUTH E2E PASSED.'
}
finally {
    Stop-Desktop
    if ($setupStarted -and (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        try { Invoke-Phase 'Cleanup' }
        catch { Append-Log @("CLEANUP FAILED: $($_.Exception.Message)") $runLog; throw }
    }
}
