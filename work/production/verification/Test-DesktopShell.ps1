# Test-DesktopShell.ps1
# Static contract verification for the Task.Desktop MainWindow shell.
# Reads MainWindow.xaml as XML only and never modifies any project file.
# Exit code: 0 = all checks passed, 1 = at least one check failed.

param(
    [string]$XamlPath = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:failed = $false

function Write-VerificationResult {
    param(
        [string]$Name,
        [bool]$Condition,
        [string]$Detail
    )
    $suffix = ''
    if ($Detail) { $suffix = ' (' + $Detail + ')' }
    if ($Condition) {
        Write-Host ('[ OK ]  ' + $Name + $suffix)
    }
    else {
        Write-Host ('[FAIL]  ' + $Name + $suffix)
        $script:failed = $true
    }
}

function Get-ElementByAutomationId {
    param(
        [System.Xml.XmlElement]$Root,
        [string]$Id
    )
    foreach ($node in $Root.SelectNodes('//*')) {
        foreach ($attr in $node.Attributes) {
            if ($attr.LocalName -eq 'AutomationProperties.AutomationId' -and $attr.Value -eq $Id) {
                return $node
            }
        }
    }
    return $null
}

function Get-AttributeValue {
    param(
        [System.Xml.XmlElement]$Node,
        [string]$LocalName
    )
    foreach ($attr in $Node.Attributes) {
        if ($attr.LocalName -eq $LocalName) { return $attr.Value }
    }
    return $null
}

try {
    if (-not $XamlPath) {
        $projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
        $XamlPath = Join-Path $projectRoot 'src\Task.Desktop\MainWindow.xaml'
    }

    Write-Host ('Verifying Desktop shell contract from: ' + $XamlPath)
    Write-Host ''

    $fileOk = Test-Path -LiteralPath $XamlPath -PathType Leaf
    Write-VerificationResult -Name 'MainWindow.xaml exists' -Condition $fileOk -Detail $XamlPath
    if (-not $fileOk) {
        Write-Host ''
        Write-Host 'Desktop shell contract verification: FAILED'
        exit 1
    }

    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $false
    $xml.Load($XamlPath)
    Write-VerificationResult -Name 'MainWindow.xaml parses as well-formed XML' -Condition $true

    $root = $xml.DocumentElement
    Write-VerificationResult -Name 'XAML root element is Window' -Condition ($root.LocalName -eq 'Window') -Detail $root.LocalName

    $navListBox = Get-ElementByAutomationId -Root $root -Id 'NavigationListBox'
    Write-VerificationResult -Name 'AutomationId=NavigationListBox present' -Condition ($null -ne $navListBox)

    $selectedArea = Get-ElementByAutomationId -Root $root -Id 'SelectedSectionArea'
    Write-VerificationResult -Name 'AutomationId=SelectedSectionArea present' -Condition ($null -ne $selectedArea)

    $statusText = Get-ElementByAutomationId -Root $root -Id 'ConnectionStatusText'
    Write-VerificationResult -Name 'AutomationId=ConnectionStatusText present' -Condition ($null -ne $statusText)

    if ($null -ne $navListBox) {
        Write-VerificationResult -Name 'NavigationListBox is a ListBox' -Condition ($navListBox.LocalName -eq 'ListBox') -Detail $navListBox.LocalName

        $itemsSource = Get-AttributeValue -Node $navListBox -LocalName 'ItemsSource'
        $itemsSourceOk = ($itemsSource -match '^\{Binding\s+Sections\s*\}$')
        Write-VerificationResult -Name 'NavigationListBox ItemsSource binds to Sections' -Condition $itemsSourceOk -Detail $itemsSource

        $selectedItem = Get-AttributeValue -Node $navListBox -LocalName 'SelectedItem'
        $selectedItemOk = ($selectedItem -match '^\{Binding\s+SelectedSection(\s*,\s*Mode=(TwoWay|OneWayToSource))?\s*\}$')
        Write-VerificationResult -Name 'NavigationListBox SelectedItem binds to SelectedSection' -Condition $selectedItemOk -Detail $selectedItem
    }

    if ($null -ne $selectedArea) {
        Write-VerificationResult -Name 'SelectedSectionArea is a content container' -Condition ($selectedArea.LocalName -eq 'Grid') -Detail $selectedArea.LocalName
    }

    if ($null -ne $statusText) {
        $statusBinding = Get-AttributeValue -Node $statusText -LocalName 'Text'
        $statusBindingOk = ($statusBinding -match '^\{Binding\s+ConnectionStatus\s*\}$')
        Write-VerificationResult -Name 'ConnectionStatusText binds Text to ConnectionStatus' -Condition $statusBindingOk -Detail $statusBinding

        $liveSetting = Get-AttributeValue -Node $statusText -LocalName 'AutomationProperties.LiveSetting'
        $liveSettingOk = ($liveSetting -eq 'Polite')
        Write-VerificationResult -Name 'ConnectionStatusText has LiveSetting=Polite' -Condition $liveSettingOk -Detail $liveSetting
    }

    Write-Host ''
    if ($script:failed) {
        Write-Host 'Desktop shell contract verification: FAILED'
        exit 1
    }
    else {
        Write-Host 'Desktop shell contract verification: PASSED'
        exit 0
    }
}
catch {
    Write-Host ('[FAIL]  Unexpected error during verification: ' + $_.Exception.Message)
    Write-Host ''
    Write-Host 'Desktop shell contract verification: FAILED'
    exit 1
}