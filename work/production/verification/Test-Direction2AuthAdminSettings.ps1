[CmdletBinding()]
param([string]$EvidenceDirectory = '')

if ($PSVersionTable.PSEdition -ne 'Core') {
    & (Get-Command pwsh.exe).Source -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -EvidenceDirectory $EvidenceDirectory
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
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $productionRoot 'evidence\direction2-auth-admin-settings-1.0.0' }
$evidenceRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
$repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $evidenceRoot.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'EvidenceDirectory must be inside the Task repository.' }
[IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing.Common
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class AuthAdminNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
'@

$desktopProcess = $null
$setupStarted = $false
$checks = [Collections.Generic.List[string]]::new()
function Pass([string]$message) { $checks.Add($message); Write-Host "[PASS] $message" }
function Assert-State([bool]$condition, [string]$message) { if (-not $condition) { throw "Direction 2 assertion failed: $message" }; Pass $message }
function Invoke-Phase([string]$phase) { & (Get-Command pwsh.exe).Source -NoProfile -ExecutionPolicy Bypass -File $phaseScript -Phase $phase; if ($LASTEXITCODE -ne 0) { throw "E2E phase $phase failed." } }

function Find-ById($root, [string]$id) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Find-ByName($root, [string]$name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Wait-ById($root, [string]$id, [int]$seconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do { $element = Find-ById $root $id; if ($null -ne $element -and -not $element.Current.IsOffscreen) { return $element }; Start-Sleep -Milliseconds 150 } while ([DateTime]::UtcNow -lt $deadline)
    throw "Visible UIA element '$id' was not found."
}
function Wait-ByName($root, [string]$name, [int]$seconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do { $element = Find-ByName $root $name; if ($null -ne $element -and -not $element.Current.IsOffscreen) { return $element }; Start-Sleep -Milliseconds 150 } while ([DateTime]::UtcNow -lt $deadline)
    throw "Visible UIA element '$name' was not found."
}
function Select-Element($element) { ([System.Windows.Automation.SelectionItemPattern]$element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select() }
function Invoke-Element($element) { ([System.Windows.Automation.InvokePattern]$element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke() }
function Invoke-ById($root, [string]$id) {
    $element = Find-ById $root $id
    if ($null -eq $element) { throw "Invokable element '$id' was not found." }
    Invoke-Element $element
}
function Select-Navigation($window, [string]$id) {
    $element = Find-ById $window $id
    if ($null -eq $element) { throw "Navigation item '$id' was not found." }
    Select-Element $element
    Start-Sleep -Milliseconds 250
}
function Select-NamedItem($root, [string]$name) {
    $element = Find-ByName $root $name
    if ($null -eq $element) { throw "Selectable item '$name' was not found." }
    Select-Element $element
    Start-Sleep -Milliseconds 250
}

function Wait-Window([int]$processId, [string]$automationId, [int]$seconds = 45) {
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $idCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    $condition = [System.Windows.Automation.AndCondition]::new($processCondition, $idCondition)
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        $window = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($null -ne $window) {
            $handle = [IntPtr]$window.Current.NativeWindowHandle
            [AuthAdminNative]::SetWindowPos($handle, [IntPtr]::Zero, 0, 0, 1280, 820, 0x0014) | Out-Null
            [AuthAdminNative]::SetForegroundWindow($handle) | Out-Null
            Start-Sleep -Milliseconds 250
            return $window
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Window '$automationId' did not open."
}
function Capture-Window($window, [string]$name) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    [AuthAdminNative]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Milliseconds 200
    $rect = [AuthAdminNative+RECT]::new()
    Assert-State ([AuthAdminNative]::GetWindowRect($handle, [ref]$rect)) "$name bounds are available."
    $bitmap = [Drawing.Bitmap]::new($rect.Right - $rect.Left, $rect.Bottom - $rect.Top)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size) } finally { $graphics.Dispose() }
        $path = Join-Path $evidenceRoot "$name.png"
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
        Assert-State ((Get-Item -LiteralPath $path).Length -gt 10000) "$name contains a rendered WPF surface."
    } finally { $bitmap.Dispose() }
}
function Stop-Desktop {
    if ($null -eq $script:desktopProcess) { return }
    $process = Get-Process -Id $script:desktopProcess.Id -ErrorAction SilentlyContinue
    if ($process) { $process.CloseMainWindow() | Out-Null; if (-not $process.WaitForExit(8000)) { Stop-Process -Id $process.Id -Force } }
    $script:desktopProcess = $null
}
function Start-Desktop { $script:desktopProcess = Start-Process -FilePath $desktopExe -WorkingDirectory (Split-Path $desktopExe) -PassThru; $script:desktopProcess }

try {
    if (Test-Path -LiteralPath $statePath) { throw "Existing E2E runtime found at $runtimeRoot; clean it before this run." }
    Assert-State (Test-Path -LiteralPath $desktopExe -PathType Leaf) 'Release WPF executable exists.'

    $firstConnectionData = Join-Path $evidenceRoot 'first-connection-data'
    [IO.Directory]::CreateDirectory($firstConnectionData) | Out-Null
    $previousDataDirectory = $env:TASK_DESKTOP_DATA_DIRECTORY
    $env:TASK_DESKTOP_DATA_DIRECTORY = $firstConnectionData
    $process = Start-Desktop
    $auth = Wait-Window $process.Id 'AuthWindow'
    $null = Wait-ById $auth 'ServerAddressTextBox'
    Capture-Window $auth 'first-connection'
    Stop-Desktop
    $env:TASK_DESKTOP_DATA_DIRECTORY = $previousDataDirectory

    Invoke-Phase 'Setup'; $setupStarted = $true
    $env:TASK_DESKTOP_BOOTSTRAP_MINIMUM_MS = '5000'
    $process = Start-Desktop
    $bootstrap = Wait-Window $process.Id 'BootstrapWindow'
    Invoke-Phase 'StopApi'
    $null = Wait-ById $bootstrap 'BootstrapRetryButton' 45
    Capture-Window $bootstrap 'bootstrap-failure'

    Invoke-Phase 'StartApi'
    Invoke-ById $bootstrap 'BootstrapRetryButton'
    $null = Wait-ById $bootstrap 'BootstrapProgress'
    Capture-Window $bootstrap 'bootstrap-progress'
    $main = Wait-Window $process.Id 'MainWindow'

    Select-Navigation $main 'Navigation_settings'
    $settingsNavigation = Wait-ById $main 'SettingsNavigation'
    Capture-Window $main 'settings-profile'
    Select-NamedItem $settingsNavigation 'Уведомления'
    Capture-Window $main 'settings-notifications'
    Select-NamedItem $settingsNavigation 'Подключение и сервер'
    Capture-Window $main 'settings-server'

    Select-Navigation $main 'Navigation_administration'
    $null = Wait-ById $main 'AdminUsersList'
    $adminTabs = Wait-ById $main 'AdministrationTabs'
    Capture-Window $main 'admin-users'
    Select-NamedItem $adminTabs 'Роли и права'
    $null = Wait-ById $main 'AdminRolesList'
    Capture-Window $main 'admin-roles'
    Select-NamedItem $adminTabs 'Сетевые ресурсы'
    $null = Wait-ById $main 'AdminResourcesList'
    Capture-Window $main 'admin-resources'

    Select-Navigation $main 'Navigation_settings'
    $settingsNavigation = Wait-ById $main 'SettingsNavigation'
    Select-NamedItem $settingsNavigation 'Подключение и сервер'
    Invoke-Phase 'StopApi'
    Invoke-ById $main 'SaveOrganizationSettingsButton'
    $null = Wait-ByName $main 'Сервер недоступен · только чтение' 45
    Capture-Window $main 'settings-offline'
    Stop-Desktop

    Invoke-Phase 'StartApi'; Invoke-Phase 'SeedReadOnlyDesktop'
    $process = Start-Desktop
    $main = Wait-Window $process.Id 'MainWindow'
    Select-Navigation $main 'Navigation_administration'
    $adminTabs = Wait-ById $main 'AdministrationTabs'
    $limitedTab = Find-ByName $adminTabs 'Управление доступом'
    if ($null -eq $limitedTab) { throw 'Limited administration tab was not found.' }
    Select-Element $limitedTab
    Start-Sleep -Milliseconds 500
    $limitedSelection = [System.Windows.Automation.SelectionItemPattern]$limitedTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    Assert-State $limitedSelection.Current.IsSelected 'Capability-gated administration tab is selected for the read-only account.'
    Capture-Window $main 'admin-limited-role'

    $screenshots = Get-ChildItem -LiteralPath $evidenceRoot -Filter '*.png' | Sort-Object Name | ForEach-Object {
        [ordered]@{ file = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; bytes = $_.Length }
    }
    $result = [ordered]@{ schemaVersion = 1; result = 'PASS'; scenarios = @($screenshots.file); screenshots = @($screenshots); checks = @($checks); completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') }
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'validation.json'), (($result | ConvertTo-Json -Depth 8) + "`n"), [Text.UTF8Encoding]::new($false))
    Pass 'Direction 2 auth, settings and administration evidence passed.'
}
finally {
    Stop-Desktop
    Remove-Item Env:TASK_DESKTOP_DATA_DIRECTORY -ErrorAction SilentlyContinue
    Remove-Item Env:TASK_DESKTOP_BOOTSTRAP_MINIMUM_MS -ErrorAction SilentlyContinue
    if ($setupStarted -and (Test-Path -LiteralPath $statePath)) { Invoke-Phase 'Cleanup' }
}
