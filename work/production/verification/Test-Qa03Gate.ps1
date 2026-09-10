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
if ($runtimeRoot -ne $expectedRuntime) { throw 'Unexpected QA-03 runtime path.' }
$writeE2E = Join-Path $PSScriptRoot 'Test-TaskWriteE2E.ps1'
$qa03Phases = Join-Path $PSScriptRoot 'Test-Qa03CriticalE2E.ps1'
$desktopExe = Join-Path $repoRoot 'work\production\src\Task.Desktop\bin\Release\net10.0-windows\Task.Desktop.exe'
$statePath = Join-Path $runtimeRoot 'state.json'
$runLog = Join-Path $evidenceRoot 'qa03-gate.log'
$buildLog = Join-Path $evidenceRoot 'qa03-release-build.log'
$pgBin = 'C:\Program Files\PostgreSQL\16\bin'
$desktopProcess = $null
$setupStarted = $false
$startedAt = [DateTimeOffset]::UtcNow

[IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null
[IO.File]::WriteAllText($runLog, '', [Text.UTF8Encoding]::new($false))

function Write-Pass([string]$message) { Write-Host "[PASS] $message" }
function Assert-Qa03([bool]$condition, [string]$message) {
    if (-not $condition) { throw "QA-03 assertion failed: $message" }
    Write-Pass $message
}
function Append-Log([object[]]$content, [string]$path) {
    $text = ($content | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    [IO.File]::AppendAllText($path, $text + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}
function Invoke-Phase([string]$phase, [string[]]$extra = @()) {
    $heading = "=== Test-TaskWriteE2E phase: $phase ==="
    Append-Log @($heading) $runLog
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $writeE2E,
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
function Invoke-Qa03Phase([string]$phase) {
    $heading = "=== Test-Qa03CriticalE2E phase: $phase ==="
    Append-Log @($heading) $runLog
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $qa03Phases,
        '-Phase', $phase, '-EvidencePath', $evidenceRoot)
    $phaseStem = $phase.ToLowerInvariant()
    $stdoutPath = Join-Path $evidenceRoot "qa03-$phaseStem.stdout.log"
    $stderrPath = Join-Path $evidenceRoot "qa03-$phaseStem.stderr.log"
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
    if ($exitCode -ne 0) { throw "Test-Qa03CriticalE2E phase '$phase' failed with exit code $exitCode." }
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
function Invoke-Sql([string]$sql) {
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $result = & (Join-Path $pgBin 'psql.exe') -X -v ON_ERROR_STOP=1 -h 127.0.0.1 `
        -p $state.PostgresPort -U postgres -d $state.DatabaseName -tA -c $sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL command failed: $($result -join [Environment]::NewLine)" }
    return ($result -join [Environment]::NewLine).Trim()
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
function Find-ElementsByNameContains([System.Windows.Automation.AutomationElement]$root,
    [string]$text, [string]$controlType = '') {
    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $text)
    $condition = $nameCondition
    if ($controlType) {
        $typeCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::$controlType)
        $condition = [System.Windows.Automation.AndCondition]::new($nameCondition, $typeCondition)
    }
    return ,@($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition))
}
function Wait-ElementByNameContains([System.Windows.Automation.AutomationElement]$root,
    [string]$text, [int]$timeoutSeconds = 60, [string]$controlType = '') {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    do {
        $found = Find-ElementsByNameContains $root $text $controlType
        if ($found.Count -ge 1) { return $found[0] }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "UI Automation element containing '$text' did not appear within $timeoutSeconds seconds."
}
function Wait-StatusText([System.Windows.Automation.AutomationElement]$window,
    [string]$fragment, [int]$timeoutSeconds = 45) {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    do {
        $status = Find-ElementById $window 'ConnectionStatusText'
        if ($null -ne $status -and $status.Current.Name.Contains($fragment)) { return $status }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Connection status '$fragment' did not appear within $timeoutSeconds seconds."
}
function Wait-ElementByNameExact([System.Windows.Automation.AutomationElement]$root,
    [string]$name, [int]$timeoutSeconds = 30) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    do {
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $element) { return $element }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "UI Automation element named '$name' did not appear within $timeoutSeconds seconds."
}
function Dump-UiaTree([System.Windows.Automation.AutomationElement]$root, [string]$path) {
    $lines = [Collections.Generic.List[string]]::new()
    function Walk($element, [int]$depth) {
        if ($depth -gt 12 -or $null -eq $element) { return }
        $id = ''
        $name = ''
        try { $id = $element.Current.AutomationId; $name = $element.Current.Name } catch { }
        $type = $element.Current.ControlType.ProgrammaticName
        if ($id -or $name) { $lines.Add(('  ' * $depth) + "[$type] id='$id' name='$name'") }
        $children = $element.FindAll([System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($child in $children) { Walk $child ($depth + 1) }
    }
    Walk $root 0
    [IO.File]::WriteAllLines($path, $lines, [Text.UTF8Encoding]::new($false))
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
function Navigate-To([System.Windows.Automation.AutomationElement]$window, [string]$route, [string]$sectionId) {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $item = Wait-ElementById $window "Navigation_$route" 15
        $selection = [System.Windows.Automation.SelectionItemPattern]$item.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern)
        $selection.Select()
        $verifyDeadline = [DateTime]::UtcNow.AddSeconds(5)
        do {
            $section = Find-ElementById $window $sectionId
            if ($null -ne $section) { Start-Sleep -Milliseconds 400; return }
            Start-Sleep -Milliseconds 250
        } while ([DateTime]::UtcNow -lt $verifyDeadline)
        Write-Host "[WARN] Navigation to '$route' did not reveal '$sectionId'; reselecting."
    } while ([DateTime]::UtcNow -lt $deadline)
    Capture-Window 'navigation-failed'
    throw "Navigation to '$route' did not reveal '$sectionId'."
}
function Capture-Window([string]$name) {
    Add-Type -AssemblyName System.Drawing
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Qa03WindowCapture {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(
        IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
'@
    $handle = [Qa03WindowCapture]::GetForegroundWindow()
    $rect = [Qa03WindowCapture+RECT]::new()
    Assert-Qa03 ([Qa03WindowCapture]::GetWindowRect($handle, [ref]$rect)) 'Foreground window bounds are available.'
    $width = $rect.Right - $rect.Left; $height = $rect.Bottom - $rect.Top
    $positionOnly = 0x0004 -bor 0x0010
    Assert-Qa03 ([Qa03WindowCapture]::SetWindowPos($handle, [IntPtr]::Zero, 0, 0, $width, $height, $positionOnly)) 'Foreground window is positioned for complete capture.'
    Start-Sleep -Milliseconds 150
    Assert-Qa03 ([Qa03WindowCapture]::GetWindowRect($handle, [ref]$rect)) 'Capture window bounds are refreshed.'
    $width = $rect.Right - $rect.Left; $height = $rect.Bottom - $rect.Top
    $dpiScale = [Qa03WindowCapture]::GetDpiForWindow($handle) / 96.0
    $captureWidth = [int][Math]::Round($width * $dpiScale)
    $captureHeight = [int][Math]::Round($height * $dpiScale)
    $bitmap = [Drawing.Bitmap]::new($captureWidth, $captureHeight)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size) }
        finally { $graphics.Dispose() }
        $path = Join-Path $evidenceRoot ($name + '.png')
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
    Write-Pass "Captured $name.png."
}
function Start-Desktop {
    Assert-Qa03 (Test-Path -LiteralPath $desktopExe -PathType Leaf) 'Release WPF executable exists.'
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
    return $window
}
function Assert-TaskListItem([System.Windows.Automation.AutomationElement]$window,
    [string]$title, [int]$timeoutSeconds = 45) {
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
function Assert-ListContains([System.Windows.Automation.AutomationElement]$window,
    [string]$listId, [string]$fragment, [int]$timeoutSeconds = 60) {
    $list = Wait-ElementById $window $listId $timeoutSeconds
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    do {
        $elements = $list.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($element in $elements) {
            try {
                if ($element.Current.Name.Contains($fragment, [StringComparison]::Ordinal)) { return $element }
            }
            catch { }
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "List '$listId' did not show an item containing '$fragment' within $timeoutSeconds seconds."
}
function Login-Api([string]$login) {
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $body = @{ login = $login; password = $state.AccountPassword; device = @{
        deviceKey = [guid]::NewGuid().ToString('N'); deviceName = 'QA-03 gate conflict'
        platform = 'windows'; appVersion = '1.0.0'; osVersion = 'Windows E2E'
    } } | ConvertTo-Json -Depth 4 -Compress
    $response = Invoke-WebRequest -Method Post -Uri "$($state.BaseUrl)/api/v1/auth/login" `
        -ContentType 'application/json' -Body $body -SkipHttpErrorCheck
    if ($response.StatusCode -ne 200) { throw 'Gate API login failed.' }
    return $response.Content | ConvertFrom-Json
}
function Patch-TaskTitle([string]$taskId, [string]$title) {
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $tokens = Login-Api $state.AdminLogin
    $headers = @{ Authorization = "Bearer $($tokens.accessToken)"; 'X-Correlation-ID' = [guid]::NewGuid().ToString('D') }
    $current = Invoke-WebRequest -Uri "$($state.BaseUrl)/api/v1/tasks/$taskId" -Headers $headers -SkipHttpErrorCheck
    if ($current.StatusCode -ne 200) { throw 'Gate task read failed.' }
    $etag = ($current.Headers['ETag'] | Select-Object -First 1).ToString()
    $headers['If-Match'] = $etag
    $headers['Idempotency-Key'] = [guid]::NewGuid().ToString('N')
    $body = @{ title = $title } | ConvertTo-Json -Compress
    $patch = Invoke-WebRequest -Method Patch -Uri "$($state.BaseUrl)/api/v1/tasks/$taskId" `
        -Headers $headers -ContentType 'application/json' -Body $body -SkipHttpErrorCheck
    if ($patch.StatusCode -ne 200) { throw 'Gate task conflict mutation failed.' }
}

try {
    if (Test-Path -LiteralPath $runtimeRoot) {
        throw "QA-03 clean runtime already exists at $runtimeRoot. Remove it only via the Cleanup phase."
    }
    if (-not $SkipBuild) {
        $buildOutput = & dotnet build (Join-Path $repoRoot 'work\production\Task.sln') `
            --configuration Release --no-restore 2>&1
        $buildExit = $LASTEXITCODE
        [IO.File]::WriteAllLines($buildLog, ($buildOutput | ForEach-Object { $_.ToString() }), [Text.UTF8Encoding]::new($false))
        $buildOutput | ForEach-Object { Write-Host $_ }
        Assert-Qa03 ($buildExit -eq 0) 'Release solution build passed.'
    }

    $setupStarted = $true
    Invoke-Phase 'Setup'
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    Assert-Qa03 ($state.AdminLogin -eq 'task-e2e-admin') 'QA-03 deterministic seed profile is active.'

    Invoke-Qa03Phase 'SeedProductData'
    Invoke-Qa03Phase 'VerifyProductApi'
    Invoke-Qa03Phase 'RunWorkerDelivery'
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json

    $ui = [ordered]@{}
    $window = Start-Desktop
    $connection = (Wait-ElementById $window 'ConnectionStatusText').Current.Name
    Assert-Qa03 ($connection -match '^Подключено к серверу компании') 'Admin WPF is connected to the production HTTPS API.'

    $todayScreen = Wait-ElementById $window 'TodayScreen' 60
    Assert-Qa03 ((Wait-ElementByNameContains $todayScreen 'QA-03 today timed task' 60).Current.Name.Contains('QA-03 today timed task')) 'Today shows the seeded timed task.'
    Assert-Qa03 ((Wait-ElementByNameContains $todayScreen 'QA-03 today untimed task' 60).Current.Name.Contains('QA-03 today untimed task')) 'Today shows the seeded untimed task.'
    Assert-Qa03 ((Wait-ElementByNameContains $todayScreen 'QA-03 overdue task' 60).Current.Name.Contains('QA-03 overdue task')) 'Today shows the seeded overdue task.'
    Assert-Qa03 ((Wait-ElementByNameContains $todayScreen 'QA-03 deterministic event' 60).Current.Name.Contains('QA-03 deterministic event')) 'Today shows the seeded all-day event.'
    $ui['today'] = 'PASS'
    Capture-Window 'today'

    Navigate-To $window 'tasks' 'NewTaskButton'
    Invoke-Element (Wait-ElementById $window 'NewTaskButton' 30 -Enabled)
    $title = Wait-ElementById $window 'TaskTitleTextBox'
    Set-ElementValue $title 'QA-03 ui created task'
    Invoke-Element (Wait-ElementById $window 'SaveTaskButton' 15 -Enabled)
    $createdItem = Assert-TaskListItem $window 'QA-03 ui created task'
    Assert-Qa03 ($createdItem.Current.Name -match 'Статус: Новая') 'Admin created a deterministic task through the real WPF UI.'
    $ui['taskCreate'] = 'PASS'
    Capture-Window 'tasks-created'

    $uiTaskId = Invoke-Sql "SELECT id FROM work.tasks WHERE title = 'QA-03 ui created task' LIMIT 1;"
    Assert-Qa03 (-not [string]::IsNullOrWhiteSpace($uiTaskId)) 'UI-created task row exists in PostgreSQL.'

    $inspector = Wait-ElementByNameExact $window 'Сведения о выбранной задаче' 30
    $expandPattern = [System.Windows.Automation.ExpandCollapsePattern]$inspector.GetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($expandPattern.Current.ExpandCollapseState -ne
        [System.Windows.Automation.ExpandCollapseState]::Expanded) {
        $expandPattern.Expand()
    }
    Invoke-Element (Wait-ElementById $window 'EditTaskButton' 30 -Enabled)
    $editorTitle = Wait-ElementById $window 'TaskTitleTextBox' 30
    Patch-TaskTitle $uiTaskId 'QA-03 ui task concurrent edit'
    Set-ElementValue $editorTitle 'QA-03 ui draft title'
    Invoke-Element (Wait-ElementById $window 'SaveTaskButton' 15 -Enabled)
    $reload = Wait-ElementById $window 'ReloadConflictButton' 30 -Enabled
    Assert-Qa03 ($null -ne $reload) 'Concurrent server edit surfaces the safe conflict UI.'
    $ui['taskConflict'] = 'PASS'
    Capture-Window 'tasks-conflict'
    Invoke-Element $reload
    $reloadedTitle = Wait-ElementById $window 'TaskTitleTextBox' 30
    Set-ElementValue $reloadedTitle 'QA-03 ui task final'
    Invoke-Element (Wait-ElementById $window 'SaveTaskButton' 30 -Enabled)
    $finalItem = Assert-TaskListItem $window 'QA-03 ui task final'
    Assert-Qa03 ($finalItem.Current.Name -match 'Статус: Новая') 'Conflict reload and explicit re-save completed the task edit.'
    $ui['taskConflictRecovery'] = 'PASS'
    Capture-Window 'tasks-recovered'

    Navigate-To $window 'calendar' 'CalendarScreen'
    Wait-ElementById $window 'CalendarScreen' 30 | Out-Null
    $weekRange = (Wait-ElementById $window 'CalendarWeekRange' 30).Current.Name
    Assert-Qa03 (-not [string]::IsNullOrWhiteSpace($weekRange)) 'Calendar shows the current week range.'
    Assert-Qa03 ((Wait-ElementByNameContains $window 'QA-03 deterministic event' 60).Current.Name.Contains('QA-03 deterministic event')) 'Calendar grid renders the seeded event.'
    Assert-Qa03 ((Wait-ElementByNameContains $window 'QA-03 recurrence E2E' 60).Current.Name.Contains('QA-03 recurrence E2E')) 'Calendar grid renders the seeded recurrence occurrence.'
    $ui['calendar'] = 'PASS'
    Capture-Window 'calendar'

    Navigate-To $window 'projects' 'ProjectsView'
    Wait-ElementById $window 'ProjectsView' 30 | Out-Null
    try {
        Assert-Qa03 ((Assert-ListContains $window 'ProjectsList' 'QA-03 deterministic project' 60).Current.Name.Contains('QA-03 deterministic project')) 'Projects list renders the seeded project.'
    }
    catch {
        Dump-UiaTree $window (Join-Path $evidenceRoot 'projects-failed-tree.txt')
        Capture-Window 'projects-failed'
        throw
    }
    Invoke-Element (Wait-ElementById $window 'NewProjectButton' 30 -Enabled)
    $projectName = Wait-ElementById $window 'ProjectNameTextBox' 30
    Set-ElementValue $projectName 'QA-03 ui created project'
    Invoke-Element (Wait-ElementById $window 'SaveProjectButton' 30 -Enabled)
    $uiProject = Assert-ListContains $window 'ProjectsList' 'QA-03 ui created project' 60
    Assert-Qa03 ($uiProject.Current.Name.Contains('QA-03 ui created project')) 'Project was created through the real WPF UI.'
    $ui['projectCreate'] = 'PASS'
    Capture-Window 'projects'

    Navigate-To $window 'catalog' 'WorkHubView'
    Wait-ElementById $window 'WorkHubView' 30 | Out-Null
    Assert-Qa03 ((Assert-ListContains $window 'CatalogList' 'QA-03 deterministic file.txt' 60).Current.Name.Contains('QA-03 deterministic file.txt')) 'Catalog list renders the seeded file reference.'
    $catalogNameBox = Wait-ElementByNameContains $window 'Название файла' 30 'Edit'
    Set-ElementValue $catalogNameBox 'QA-03 ui catalog file'
    Invoke-Element (Wait-ElementById $window 'CreateCatalogItemButton' 30 -Enabled)
    $uiCatalog = Assert-ListContains $window 'CatalogList' 'QA-03 ui catalog file' 60
    Assert-Qa03 ($uiCatalog.Current.Name.Contains('QA-03 ui catalog file')) 'Catalog entry was created through the real WPF UI.'
    $ui['catalogCreate'] = 'PASS'
    Capture-Window 'catalog'

    Navigate-To $window 'contacts' 'WorkHubView'
    Wait-ElementById $window 'WorkHubView' 30 | Out-Null
    $firstName = Wait-ElementById $window 'ContactFirstNameTextBox' 30
    $displayName = Wait-ElementById $window 'ContactDisplayNameTextBox' 30
    Set-ElementValue $firstName 'Qa03Ui'
    Set-ElementValue $displayName 'QA-03 ui contact'
    Invoke-Element (Wait-ElementById $window 'CreateContactButton' 30 -Enabled)
    $uiContact = Assert-ListContains $window 'ContactsList' 'QA-03 ui contact' 60
    Assert-Qa03 ($uiContact.Current.Name.Contains('QA-03 ui contact')) 'Contact was created through the real WPF UI.'
    $ui['contactCreate'] = 'PASS'
    Capture-Window 'contacts'

    Navigate-To $window 'search' 'WorkHubView'
    Wait-ElementById $window 'WorkHubView' 30 | Out-Null
    $searchBox = Wait-ElementById $window 'GlobalSearchTextBox' 30
    Set-ElementValue $searchBox 'QA-03 deterministic project'
    Invoke-Element (Wait-ElementById $window 'GlobalSearchButton' 30 -Enabled)
    $searchHit = Assert-ListContains $window 'GlobalSearchResults' 'QA-03 deterministic project' 60
    Assert-Qa03 ($searchHit.Current.Name.Contains('QA-03 deterministic project')) 'Global search finds the seeded project in the real WPF UI.'
    $ui['search'] = 'PASS'
    Capture-Window 'search'

    Navigate-To $window 'notifications' 'WorkHubView'
    Wait-ElementById $window 'WorkHubView' 30 | Out-Null
    $notification = Assert-ListContains $window 'NotificationsList' 'Напоминание' 60
    Assert-Qa03 ($notification.Current.Name.Contains('Напоминание')) 'Notification center renders the worker-delivered reminder.'
    $deliveredState = Assert-ListContains $window 'NotificationsList' 'delivered' 30
    Assert-Qa03 ($deliveredState.Current.Name.Contains('delivered')) 'Notification center shows the unread delivered state.'
    Invoke-Element (Wait-ElementById $window 'MarkAllNotificationsReadButton' 30 -Enabled)
    $readState = Assert-ListContains $window 'NotificationsList' 'read' 60
    Assert-Qa03 ($readState.Current.Name.Contains('read')) 'Mark-all-read transitioned the reminder to the read state.'
    $ui['notifications'] = 'PASS'
    Capture-Window 'notifications'

    Navigate-To $window 'tasks' 'NewTaskButton'
    Invoke-Phase 'StopApi'
    Invoke-Element (Wait-ElementById $window 'TasksRefreshButton' 30 -Enabled)
    Wait-StatusText $window 'Сервер недоступен' 60 | Out-Null
    $offlineNew = Wait-ElementById $window 'NewTaskButton' 30
    Assert-Qa03 (-not $offlineNew.Current.IsEnabled) 'Server loss disables task creation in the shell.'
    $offlineContext = (Wait-ElementById $window 'ConnectionStatusText' 30).Current.Name
    Assert-Qa03 ($offlineContext -match 'Сервер недоступен') 'Shell truthfully reports the offline state.'
    $ui['offlineShell'] = 'PASS'
    Capture-Window 'offline'

    Invoke-Phase 'StartApi'
    Invoke-Element (Wait-ElementById $window 'TasksRefreshButton' 30 -Enabled)
    Wait-StatusText $window 'Подключено к серверу компании' 60 | Out-Null
    $onlineNew = Wait-ElementById $window 'NewTaskButton' 30
    Assert-Qa03 ($onlineNew.Current.IsEnabled) 'Explicit refresh restores task creation after reconnect.'
    $ui['reconnect'] = 'PASS'
    Capture-Window 'reconnected'
    Stop-Desktop

    Invoke-Phase 'StopApi'
    Invoke-Phase 'StartApi'
    Invoke-Qa03Phase 'VerifyCriticalProduct'
    Invoke-Qa03Phase 'VerifyNotificationsApi'

    $uiEvidence = [ordered]@{
        schemaVersion = 1
        scenario = 'QA-03 critical user scenario E2E'
        seedProfile = 'qa03-deterministic-v1'
        result = 'PASS'
        executable = 'work/production/src/Task.Desktop/bin/Release/net10.0-windows/Task.Desktop.exe'
        initialConnection = $connection
        uiChecks = $ui
        uiTaskId = $uiTaskId
        conflictServerTitle = 'QA-03 ui task concurrent edit'
        finalTitle = 'QA-03 ui task final'
        startedAtUtc = $startedAt.ToString('O')
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'ui-assertions.json'),
        (($uiEvidence | ConvertTo-Json -Depth 6) + "`n"), [Text.UTF8Encoding]::new($false))
    Append-Log @('QA-03 CRITICAL E2E GATE PASSED.') $runLog
    Write-Pass 'QA-03 CRITICAL E2E GATE PASSED.'
}
finally {
    Stop-Desktop
    if ($setupStarted -and (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        try { Invoke-Phase 'Cleanup' }
        catch { Append-Log @("CLEANUP FAILED: $($_.Exception.Message)") $runLog; throw }
    }
}
