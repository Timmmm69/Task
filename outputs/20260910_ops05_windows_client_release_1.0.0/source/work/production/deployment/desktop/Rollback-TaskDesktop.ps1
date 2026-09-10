[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExpectedPublisherThumbprint,
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\Task'),
    [switch]$NoShellIntegration
)
$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $PSScriptRoot 'TaskDesktop.Release.psm1'
$moduleSignature = Get-AuthenticodeSignature -LiteralPath $modulePath
if ($moduleSignature.Status -in @('NotSigned','HashMismatch','NotSupportedFileFormat','Incompatible') -or $null -eq $moduleSignature.SignerCertificate -or $moduleSignature.SignerCertificate.Thumbprint -ne $ExpectedPublisherThumbprint.Replace(' ', '').ToUpperInvariant()) { throw 'The rollback verifier is not signed by the expected publisher.' }
Import-Module $modulePath -Force
Assert-TaskSignedFile $PSCommandPath $ExpectedPublisherThumbprint
$state = Read-TaskInstallState $InstallRoot
if ($null -eq $state -or [string]::IsNullOrWhiteSpace($state.previousVersion)) { throw 'No validated previous Task release is available.' }
$previousRoot = Join-Path $InstallRoot "releases\$($state.previousVersion)"
Assert-TaskRelease $previousRoot $ExpectedPublisherThumbprint | Out-Null
$newState = [ordered]@{ schemaVersion = 1; currentVersion = $state.previousVersion; previousVersion = $state.currentVersion; publisherThumbprint = $state.publisherThumbprint; installedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') }
Write-TaskInstallState $InstallRoot $newState
if (-not $NoShellIntegration) { Set-TaskShellIntegration $InstallRoot $newState.currentVersion }
[pscustomobject]@{ result = 'rolled_back'; version = $newState.currentVersion; previousVersion = $newState.previousVersion }
