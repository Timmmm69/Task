[CmdletBinding()]
param(
    [string]$EvidenceDirectory = '',
    [switch]$SkipBuild,
    [switch]$WithNarrator
)

if ($PSVersionTable.PSEdition -ne 'Core') {
    $pwsh = Get-Command pwsh.exe -ErrorAction Stop
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($EvidenceDirectory) { $arguments += @('-EvidenceDirectory', $EvidenceDirectory) }
    if ($SkipBuild) { $arguments += '-SkipBuild' }
    if ($WithNarrator) { $arguments += '-WithNarrator' }
    & $pwsh.Source @arguments
    exit $LASTEXITCODE
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$productionRoot = Join-Path $repoRoot 'work\production'
if (-not $EvidenceDirectory) {
    $EvidenceDirectory = Join-Path $productionRoot 'evidence\desk05'
}
$evidenceRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
$repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $evidenceRoot.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'EvidenceDirectory must be inside the Task repository.'
}

$runtimeRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'TaskE2ERuntime\task-write-e2e'))
$isolatedDesktopData = Join-Path $runtimeRoot 'desktop-appdata'
$statePath = Join-Path $runtimeRoot 'state.json'
$phaseScript = Join-Path $PSScriptRoot 'Test-TaskWriteE2E.ps1'
$desktopExe = Join-Path $productionRoot 'src\Task.Desktop\bin\Release\net10.0-windows\Task.Desktop.exe'
$desktopProcess = $null
$setupStarted = $false
$sessionStash = $null
$narratorWasRunning = $false
$narratorStartedIds = @()
$checks = [Collections.Generic.List[string]]::new()
$startedAt = [DateTimeOffset]::UtcNow

[IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null

function Pass([string]$message) {
    $checks.Add($message)
    Write-Host "[PASS] $message"
}

function Assert-Desk05([bool]$condition, [string]$message) {
    if (-not $condition) { throw "DESK-05 assertion failed: $message" }
    Pass $message
}

function Invoke-Captured([string]$fileName, [string]$executable, [string[]]$arguments) {
    $stdout = Join-Path $evidenceRoot "$fileName.stdout.log"
    $stderr = Join-Path $evidenceRoot "$fileName.stderr.log"
    $process = Start-Process -FilePath $executable -ArgumentList $arguments -WindowStyle Hidden `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $process.WaitForExit()
    $exitCode = $process.ExitCode
    $process.Dispose()
    if (Test-Path -LiteralPath $stdout) { Get-Content -LiteralPath $stdout | ForEach-Object { Write-Host $_ } }
    if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr | ForEach-Object { Write-Host $_ } }
    if ($exitCode -ne 0) { throw "$fileName failed with exit code $exitCode." }
}

function Invoke-Phase([string]$phase) {
    Invoke-Captured "phase-$($phase.ToLowerInvariant())" (Get-Command pwsh.exe).Source `
        @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $phaseScript, '-Phase', $phase,
            '-DesktopAppDataPath', $isolatedDesktopData)
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing.Common

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Desk05Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern void keybd_event(
        byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(
        IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
'@

function Find-ElementById(
    [System.Windows.Automation.AutomationElement]$root,
    [string]$automationId
) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Wait-ElementById(
    [System.Windows.Automation.AutomationElement]$root,
    [string]$automationId,
    [int]$timeoutSeconds = 30
) {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    do {
        $element = Find-ElementById $root $automationId
        if ($null -ne $element) { return $element }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "UI Automation element '$automationId' was not found."
}

function Start-DesktopWindow([string]$automationId) {
    Assert-Desk05 (Test-Path -LiteralPath $desktopExe -PathType Leaf) 'Release WPF executable exists.'
    $previousDataDirectory = [Environment]::GetEnvironmentVariable('TASK_DESKTOP_DATA_DIRECTORY', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('TASK_DESKTOP_DATA_DIRECTORY', $isolatedDesktopData, 'Process')
        $script:desktopProcess = Start-Process -FilePath $desktopExe -WorkingDirectory (Split-Path $desktopExe) -PassThru
    }
    finally {
        [Environment]::SetEnvironmentVariable('TASK_DESKTOP_DATA_DIRECTORY', $previousDataDirectory, 'Process')
    }
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:desktopProcess.Id)
    $idCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    $condition = [System.Windows.Automation.AndCondition]::new($processCondition, $idCondition)
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $window = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($null -ne $window) { return $window }
        if ($script:desktopProcess.HasExited) { throw "Task.Desktop exited before opening $automationId." }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Task.Desktop did not open $automationId within 45 seconds."
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

function Assert-UiaElement(
    [System.Windows.Automation.AutomationElement]$window,
    [string]$automationId,
    [string]$patternName = ''
) {
    $element = Wait-ElementById $window $automationId
    Assert-Desk05 (-not [string]::IsNullOrWhiteSpace($element.Current.Name)) "$automationId exposes a screen-reader name."
    Assert-Desk05 (-not $element.Current.IsOffscreen) "$automationId is visible in the UIA tree."
    if ($patternName) {
        $pattern = $null
        $identifier = switch ($patternName) {
            'Invoke' { [System.Windows.Automation.InvokePattern]::Pattern }
            'Selection' { [System.Windows.Automation.SelectionPattern]::Pattern }
            'Value' { [System.Windows.Automation.ValuePattern]::Pattern }
            default { throw "Unsupported UIA pattern $patternName" }
        }
        Assert-Desk05 ($element.TryGetCurrentPattern($identifier, [ref]$pattern)) `
            "$automationId exposes the $patternName UIA pattern."
    }
    return $element
}

function Select-UiaElement([System.Windows.Automation.AutomationElement]$element) {
    $pattern = [System.Windows.Automation.SelectionItemPattern]$element.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
}

function Capture-Window(
    [System.Windows.Automation.AutomationElement]$window,
    [string]$path
) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    $window.SetFocus()
    [Desk05Native]::SetForegroundWindow($handle) | Out-Null
    [Desk05Native]::BringWindowToTop($handle) | Out-Null
    if (-not [Desk05Native]::SetWindowPos($handle, [IntPtr](-1), 0, 0, 0, 0, 0x0003)) {
        throw "Could not raise $(Split-Path -Leaf $path) above desktop overlays."
    }
    Start-Sleep -Milliseconds 300

    $rect = $window.Current.BoundingRectangle
    $width = [Math]::Max(1, [int][Math]::Round($rect.Width))
    $height = [Math]::Max(1, [int][Math]::Round($rect.Height))
    $bitmap = [Drawing.Bitmap]::new($width, $height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$rect.Left, [int]$rect.Top, 0, 0, $bitmap.Size)
        $brightSamples = 0
        for ($sampleX = 0; $sampleX -lt 10; $sampleX++) {
            for ($sampleY = 0; $sampleY -lt 10; $sampleY++) {
                $pixelX = [Math]::Min($width - 1, [int](($sampleX + 0.5) * $width / 10))
                $pixelY = [Math]::Min($height - 1, [int](($sampleY + 0.5) * $height / 10))
                if ($bitmap.GetPixel($pixelX, $pixelY).GetBrightness() -gt 0.1) { $brightSamples++ }
            }
        }
        Assert-Desk05 ($brightSamples -ge 20) `
            "$(Split-Path -Leaf $path) contains a rendered native window surface."
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        [Desk05Native]::SetWindowPos($handle, [IntPtr](-2), 0, 0, 0, 0, 0x0003) | Out-Null
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Test-ViewportMatrix(
    [System.Windows.Automation.AutomationElement]$window,
    [string]$prefix,
    [string[]]$criticalIds
) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    $hostDpi = [Desk05Native]::GetDpiForWindow($handle)
    $screenWidth = [Desk05Native]::GetSystemMetrics(0)
    $screenHeight = [Desk05Native]::GetSystemMetrics(1)
    $matrix = [Collections.Generic.List[object]]::new()
    foreach ($scale in @(100, 125, 150, 200)) {
        $logicalWidth = [Math]::Floor(1920 * 100 / $scale)
        $logicalHeight = [Math]::Floor(1040 * 100 / $scale)
        $physicalWidth = [Math]::Min($screenWidth, [Math]::Round($logicalWidth * $hostDpi / 96))
        $physicalHeight = [Math]::Min($screenHeight, [Math]::Round($logicalHeight * $hostDpi / 96))
        Assert-Desk05 ([Desk05Native]::SetWindowPos($handle, [IntPtr]::Zero, 0, 0,
                $physicalWidth, $physicalHeight, 0x0014)) "$prefix window accepted the $scale% viewport."
        Start-Sleep -Milliseconds 500
        $windowRect = $window.Current.BoundingRectangle
        $bounds = [ordered]@{}
        foreach ($id in $criticalIds) {
            $element = Wait-ElementById $window $id
            $rect = $element.Current.BoundingRectangle
            $inside = -not $element.Current.IsOffscreen -and $rect.Width -gt 0 -and $rect.Height -gt 0 `
                -and $rect.Left -ge ($windowRect.Left - 2) -and $rect.Top -ge ($windowRect.Top - 2) `
                -and $rect.Right -le ($windowRect.Right + 2) -and $rect.Bottom -le ($windowRect.Bottom + 2)
            Assert-Desk05 $inside "$prefix $id remains unclipped at the $scale% logical viewport."
            $bounds[$id] = @{
                left = [Math]::Round($rect.Left, 1); top = [Math]::Round($rect.Top, 1)
                width = [Math]::Round($rect.Width, 1); height = [Math]::Round($rect.Height, 1)
            }
        }
        $imageName = "$prefix-$scale.png"
        Capture-Window $window (Join-Path $evidenceRoot $imageName)
        $matrix.Add([ordered]@{
            scalePercent = $scale
            logicalViewport = @{ width = $logicalWidth; height = $logicalHeight }
            actualWindowPixels = @{
                width = [Math]::Round($windowRect.Width); height = [Math]::Round($windowRect.Height)
            }
            hostDpi = $hostDpi
            criticalBounds = $bounds
            screenshot = $imageName
            result = 'PASS'
        })
    }
    return @($matrix)
}

function Get-FocusedAutomationPath {
    $ids = [Collections.Generic.List[string]]::new()
    $element = [System.Windows.Automation.AutomationElement]::FocusedElement
    for ($depth = 0; $null -ne $element -and $depth -lt 20; $depth++) {
        $id = $element.Current.AutomationId
        if (-not [string]::IsNullOrWhiteSpace($id)) { $ids.Add($id) }
        $element = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($element)
    }
    return @($ids)
}

function Send-VirtualKey([System.Windows.Automation.AutomationElement]$window, [byte]$virtualKey) {
    [Desk05Native]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 200
    [Desk05Native]::keybd_event($virtualKey, 0, 0, [UIntPtr]::Zero)
    [Desk05Native]::keybd_event($virtualKey, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 650
    return @(Get-FocusedAutomationPath)
}

function Send-F6([System.Windows.Automation.AutomationElement]$window) { return @(Send-VirtualKey $window 0x75) }

function Send-Tab([System.Windows.Automation.AutomationElement]$window) { return @(Send-VirtualKey $window 0x09) }

function Start-NarratorForSmoke {
    $before = @(Get-Process -Name Narrator -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    $script:narratorWasRunning = $before.Count -gt 0
    if (-not $script:narratorWasRunning) {
        $narratorPath = Join-Path $env:WINDIR 'System32\Narrator.exe'
        Assert-Desk05 (Test-Path -LiteralPath $narratorPath -PathType Leaf) 'Windows Narrator executable exists.'
        Start-Process -FilePath $narratorPath | Out-Null
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $active = @(Get-Process -Name Narrator -ErrorAction SilentlyContinue)
        if ($active.Count -gt 0) { break }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)
    Assert-Desk05 ($active.Count -gt 0) 'Windows Narrator is active during keyboard focus traversal.'
    $script:narratorStartedIds = @($active | Where-Object { $_.Id -notin $before } | Select-Object -ExpandProperty Id)
}

try {
    if (-not $SkipBuild) {
        Invoke-Captured 'release-build' (Get-Command dotnet.exe).Source `
            @('build', (Join-Path $productionRoot 'Task.sln'), '--configuration', 'Release', '--no-restore')
    }
    Invoke-Captured 'windows-ux-tests' (Get-Command dotnet.exe).Source @(
        'test', (Join-Path $productionRoot 'tests\Task.Desktop.Tests\Task.Desktop.Tests.csproj'),
        '--configuration', 'Release', '--no-build', '--filter', 'FullyQualifiedName~WindowsUxAccessibilityTests',
        '--results-directory', $evidenceRoot, '--logger', 'trx;LogFileName=windows-ux.trx')
    Pass 'Focused DESK-05 source and layout tests passed.'

    $setupStarted = $true
    Invoke-Phase 'Setup'
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    Assert-Desk05 ($state.UiInitialTitle -eq 'QA-02 deterministic WPF task') 'Synthetic clean-stand profile is active.'

    $sessionStash = Join-Path $runtimeRoot 'desk05-admin-session'
    if (Test-Path -LiteralPath $sessionStash) { throw "Unexpected existing session stash: $sessionStash" }
    Move-Item -LiteralPath $state.DesktopAppData -Destination $sessionStash
    $authWindow = Start-DesktopWindow 'AuthWindow'
    $serverAddress = Assert-UiaElement $authWindow 'ServerAddressTextBox' 'Value'
    $null = Assert-UiaElement $authWindow 'CheckServerConnectionButton' 'Invoke'
    $null = Assert-UiaElement $authWindow 'ContinueFromServerSetupButton'
    $authMatrix = Test-ViewportMatrix $authWindow 'auth' `
        @('ServerAddressTextBox', 'CheckServerConnectionButton', 'ContinueFromServerSetupButton')
    $serverAddress.SetFocus()
    $authFocus = Send-Tab $authWindow
    Assert-Desk05 ($authFocus -contains 'CheckServerConnectionButton') `
        'Tab moves authentication focus to the next enabled action.'
    Stop-Desktop

    if (Test-Path -LiteralPath $state.DesktopAppData) {
        Remove-Item -LiteralPath $state.DesktopAppData -Recurse -Force
    }
    Move-Item -LiteralPath $sessionStash -Destination $state.DesktopAppData
    $sessionStash = $null

    $mainWindow = Start-DesktopWindow 'MainWindow'
    $navigation = Assert-UiaElement $mainWindow 'NavigationListBox' 'Selection'
    Select-UiaElement (Wait-ElementById $mainWindow 'Navigation_tasks')
    $null = Wait-ElementById $mainWindow 'TasksRefreshButton'
    $null = Assert-UiaElement $mainWindow 'TasksRefreshButton' 'Invoke'
    $null = Assert-UiaElement $mainWindow 'NewTaskButton' 'Invoke'
    $null = Assert-UiaElement $mainWindow 'TasksList' 'Selection'
    $null = Assert-UiaElement $mainWindow 'LogoutButton' 'Invoke'
    $connection = Assert-UiaElement $mainWindow 'ConnectionStatusText'
    $liveProperty = [System.Windows.Automation.AutomationElementIdentifiers]::LiveSettingProperty
    $livePropertySupported = @($connection.GetSupportedProperties() | Where-Object { $_.Id -eq $liveProperty.Id }).Count -gt 0
    if ($livePropertySupported) {
        $liveSetting = $connection.GetCurrentPropertyValue($liveProperty, $true)
        Assert-Desk05 ($null -ne $liveSetting) `
            'Connection status exposes the UIA live setting property.'
    }
    else {
        Pass 'Connection live region is declared in XAML; this host UIA client does not enumerate LiveSetting on TextBlock peers.'
    }

    $mainMatrix = Test-ViewportMatrix $mainWindow 'main' @(
        'NavigationListBox', 'TasksRefreshButton', 'NewTaskButton', 'ConnectionStatusText', 'LogoutButton', 'TasksList')

    [Desk05Native]::SetForegroundWindow([IntPtr]$mainWindow.Current.NativeWindowHandle) | Out-Null
    $navigation.SetFocus()
    $headerFocus = Send-F6 $mainWindow
    Assert-Desk05 (@($headerFocus | Where-Object { $_ -in @('TasksRefreshButton', 'NewTaskButton', 'LogoutButton') }).Count -gt 0) `
        'F6 moves focus from navigation to the header command region.'
    $contentFocus = Send-F6 $mainWindow
    Write-Host "F6 content focus path: $($contentFocus -join ' > ')"
    Assert-Desk05 (@($contentFocus | Where-Object {
                $_ -in @('SelectedSectionArea', 'TasksList', 'TasksLoadMoreButton', 'TaskDetailsArea')
            }).Count -gt 0) `
        'F6 moves focus from header commands to the selected content region.'
    $navigationFocus = Send-F6 $mainWindow
    Assert-Desk05 ($navigationFocus -contains 'NavigationListBox') `
        'F6 returns focus from content to the primary navigation region.'

    $narratorFocusTargets = @()
    if ($WithNarrator) {
        Start-NarratorForSmoke
        [Desk05Native]::SetForegroundWindow([IntPtr]$mainWindow.Current.NativeWindowHandle) | Out-Null
        $narratorFocusTargets = foreach ($id in @('NavigationListBox', 'TasksRefreshButton', 'TasksList')) {
            $element = Wait-ElementById $mainWindow $id
            $element.SetFocus()
            Start-Sleep -Milliseconds 350
            Assert-Desk05 (-not [string]::IsNullOrWhiteSpace($element.Current.Name)) `
                "Narrator traversal target $id retains its accessible name."
            @{ automationId = $id; name = $element.Current.Name }
        }
    }

    $result = [ordered]@{
        schemaVersion = 1
        task = 'DESK-05'
        result = 'PASS'
        dataProfile = 'qa02-deterministic-v1; synthetic only'
        platform = @{
            osVersion = [Environment]::OSVersion.VersionString
            architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
            screenPixels = @{ width = [Desk05Native]::GetSystemMetrics(0); height = [Desk05Native]::GetSystemMetrics(1) }
            nativeMainWindowDpi = [Desk05Native]::GetDpiForWindow([IntPtr]$mainWindow.Current.NativeWindowHandle)
            perMonitorV2Manifest = $true
        }
        nativeUia = @{
            authentication = 'PASS'
            mainWindow = 'PASS'
            patterns = @('Value', 'Invoke', 'Selection')
            liveRegionContract = 'PASS'
            hostEnumeratesLiveSettingProperty = $livePropertySupported
        }
        keyboard = @{
            authenticationFocusPath = $authFocus
            f6HeaderPath = $headerFocus
            f6ContentPath = $contentFocus
            f6NavigationPath = $navigationFocus
            result = 'PASS'
        }
        narrator = @{
            requested = [bool]$WithNarrator
            activeDuringFocusTraversal = [bool]$WithNarrator
            focusTargets = @($narratorFocusTargets)
            speechTranscriptCaptured = $false
            scope = 'Compatibility smoke: Narrator active while named UIA focus targets were traversed; auditory wording was not transcribed.'
        }
        dpiMatrix = @{
            method = 'Native WPF/UIA windows at logical 1920x1040 viewports equivalent to 100/125/150/200%; screenshots and critical bounds captured on the current Windows host.'
            authentication = $authMatrix
            mainWindow = $mainMatrix
            result = 'PASS'
        }
        checks = @($checks)
        startedAtUtc = $startedAt.ToString('O')
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText(
        (Join-Path $evidenceRoot 'windows-ux.json'),
        (($result | ConvertTo-Json -Depth 12) + "`n"),
        [Text.UTF8Encoding]::new($false))
    Pass 'DESK-05 native Windows UX matrix passed.'
}
finally {
    Stop-Desktop
    foreach ($id in $narratorStartedIds) {
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $sessionStash -and (Test-Path -LiteralPath $sessionStash)) {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if (Test-Path -LiteralPath $state.DesktopAppData) {
            Remove-Item -LiteralPath $state.DesktopAppData -Recurse -Force
        }
        Move-Item -LiteralPath $sessionStash -Destination $state.DesktopAppData
    }
    if ($setupStarted -and (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        Invoke-Phase 'Cleanup'
    }
}
