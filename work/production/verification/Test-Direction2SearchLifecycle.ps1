[CmdletBinding()]
param(
    [string]$EvidenceDirectory = ''
)

if ($PSVersionTable.PSEdition -ne 'Core') {
    & (Get-Command pwsh.exe).Source -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath `
        -EvidenceDirectory $EvidenceDirectory
    exit $LASTEXITCODE
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$productionRoot = Join-Path $repoRoot 'work\production'
$phaseScript = Join-Path $PSScriptRoot 'Test-TaskWriteE2E.ps1'
$desktopExe = Join-Path $productionRoot 'src\Task.Desktop\bin\Release\net10.0-windows\Task.Desktop.exe'
$runtimeRoot = Join-Path $env:LOCALAPPDATA 'TaskE2ERuntime\task-write-e2e'
$statePath = Join-Path $runtimeRoot 'state.json'
if (-not $EvidenceDirectory) {
    $EvidenceDirectory = Join-Path $productionRoot 'evidence\direction2-search-lifecycle-1.0.0'
}
$evidenceRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
$repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $evidenceRoot.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'EvidenceDirectory must be inside the Task repository.'
}

[IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing.Common
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Direction2Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
'@

$desktopProcess = $null
$setupStarted = $false
$checks = [Collections.Generic.List[string]]::new()

function Pass([string]$message) {
    $checks.Add($message)
    Write-Host "[PASS] $message"
}

function Assert-Direction2([bool]$condition, [string]$message) {
    if (-not $condition) { throw "Direction 2 assertion failed: $message" }
    Pass $message
}

function Invoke-Phase([string]$phase) {
    & (Get-Command pwsh.exe).Source -NoProfile -ExecutionPolicy Bypass -File $phaseScript -Phase $phase
    if ($LASTEXITCODE -ne 0) { throw "E2E phase $phase failed with exit code $LASTEXITCODE." }
}

function Find-ById([System.Windows.Automation.AutomationElement]$root, [string]$id) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-ByName([System.Windows.Automation.AutomationElement]$root, [string]$name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Wait-ById([System.Windows.Automation.AutomationElement]$root, [string]$id, [int]$seconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        $element = Find-ById $root $id
        if ($null -ne $element -and -not $element.Current.IsOffscreen) { return $element }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Visible UIA element '$id' was not found."
}

function Wait-ByName([System.Windows.Automation.AutomationElement]$root, [string]$name, [int]$seconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        $element = Find-ByName $root $name
        if ($null -ne $element -and -not $element.Current.IsOffscreen) { return $element }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Visible UIA element '$name' was not found."
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

function Start-Desktop {
    Assert-Direction2 (Test-Path -LiteralPath $desktopExe -PathType Leaf) 'Release WPF executable exists.'
    $script:desktopProcess = Start-Process -FilePath $desktopExe -WorkingDirectory (Split-Path $desktopExe) -PassThru
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:desktopProcess.Id)
    $idCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'MainWindow')
    $condition = [System.Windows.Automation.AndCondition]::new($processCondition, $idCondition)
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $window = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($null -ne $window) {
            $handle = [IntPtr]$window.Current.NativeWindowHandle
            [Direction2Native]::SetWindowPos($handle, [IntPtr]::Zero, 0, 0, 1280, 820, 0x0014) | Out-Null
            [Direction2Native]::SetForegroundWindow($handle) | Out-Null
            Start-Sleep -Milliseconds 500
            return $window
        }
        if ($script:desktopProcess.HasExited) { throw 'Task.Desktop exited before MainWindow opened.' }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Task.Desktop did not open MainWindow within 45 seconds.'
}

function Stop-Desktop {
    if ($null -eq $script:desktopProcess) { return }
    $process = Get-Process -Id $script:desktopProcess.Id -ErrorAction SilentlyContinue
    if ($process) {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(10000)) { Stop-Process -Id $process.Id -Force }
    }
    $script:desktopProcess = $null
}

function Capture-Window([System.Windows.Automation.AutomationElement]$window, [string]$name) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    [Direction2Native]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Milliseconds 300
    $rect = [Direction2Native+RECT]::new()
    Assert-Direction2 ([Direction2Native]::GetWindowRect($handle, [ref]$rect)) "$name window bounds are available."
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $bitmap = [Drawing.Bitmap]::new($width, $height)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size) }
        finally { $graphics.Dispose() }
        $path = Join-Path $evidenceRoot "$name.png"
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
        Assert-Direction2 ((Get-Item -LiteralPath $path).Length -gt 10000) "$name screenshot contains a rendered WPF surface."
    }
    finally { $bitmap.Dispose() }
}

function Open-SearchPage([System.Windows.Automation.AutomationElement]$window) {
    Select-Element (Wait-ById $window 'Navigation_search')
    return Wait-ById $window 'GlobalSearchTextBox'
}

try {
    if (Test-Path -LiteralPath $statePath) { throw "Existing E2E runtime found at $runtimeRoot; clean it before this run." }
    Invoke-Phase 'Setup'
    $setupStarted = $true

    $window = Start-Desktop
    $null = Open-SearchPage $window
    $searchBox = Wait-ById $window 'GlobalSearchTextBox'
    $value = [System.Windows.Automation.ValuePattern]$searchBox.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)
    $value.SetValue('QA-02')
    Invoke-Element (Wait-ById $window 'GlobalSearchButton')
    $null = Wait-ByName $window 'QA-02 deterministic replay probe'
    Assert-Direction2 ($null -ne (Wait-ById $window 'GlobalSearchResults')) 'Grouped search results expose a stable UIA root.'
    Capture-Window $window 'normal'

    Invoke-Element (Wait-ById $window 'GlobalSearchOverlayButton')
    $null = Wait-ById $window 'GlobalSearchOverlayResults'
    Capture-Window $window 'normal-overlay'
    Invoke-Element (Wait-ByName $window 'Закрыть поиск')

    Invoke-Phase 'StopApi'
    Invoke-Element (Wait-ById $window 'GlobalSearchButton')
    $offlineFeedback = Wait-ById $window 'WorkHubFeedbackText' 45
    Assert-Direction2 ($offlineFeedback.Current.Name -match 'недоступен|подтверждённые') 'Offline feedback is exposed through UIA.'
    Assert-Direction2 ($null -ne (Wait-ById $window 'GlobalSearchResults')) 'Offline state keeps the last confirmed result list available.'
    Capture-Window $window 'offline'
    Stop-Desktop

    Invoke-Phase 'StartApi'
    Invoke-Phase 'SeedReadOnlyDesktop'
    $window = Start-Desktop
    $null = Open-SearchPage $window
    $limited = Wait-ById $window 'WorkHubLimitedRoleText'
    Assert-Direction2 ($limited.Current.Name -eq 'Ограниченная роль') 'Limited-role redaction state is visible and named.'
    Capture-Window $window 'limited-role'

    $screenshots = Get-ChildItem -LiteralPath $evidenceRoot -Filter '*.png' | Sort-Object Name | ForEach-Object {
        [ordered]@{ file = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; bytes = $_.Length }
    }
    $result = [ordered]@{
        schemaVersion = 1
        result = 'PASS'
        scenarios = @('normal', 'normal-overlay', 'offline', 'limited-role')
        screenshots = @($screenshots)
        checks = @($checks)
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'validation.json'),
        (($result | ConvertTo-Json -Depth 8) + "`n"), [Text.UTF8Encoding]::new($false))
    Pass 'Direction 2 search/lifecycle native evidence passed.'
}
finally {
    Stop-Desktop
    if ($setupStarted -and (Test-Path -LiteralPath $statePath)) { Invoke-Phase 'Cleanup' }
}
