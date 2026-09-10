[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExpectedPublisherThumbprint,
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\Task'),
    [switch]$AllowDowngrade,
    [switch]$NoShellIntegration
)
$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $PSScriptRoot 'tools\TaskDesktop.Release.psm1'
$moduleSignature = Get-AuthenticodeSignature -LiteralPath $modulePath
if ($moduleSignature.Status -in @('NotSigned','HashMismatch','NotSupportedFileFormat','Incompatible') -or $null -eq $moduleSignature.SignerCertificate -or $moduleSignature.SignerCertificate.Thumbprint -ne $ExpectedPublisherThumbprint.Replace(' ', '').ToUpperInvariant()) { throw 'The release verifier module is not signed by the expected publisher.' }
Import-Module $modulePath -Force
Assert-TaskSignedFile $PSCommandPath $ExpectedPublisherThumbprint
$manifest = Assert-TaskRelease $PSScriptRoot $ExpectedPublisherThumbprint
$incoming = ConvertTo-TaskVersion $manifest.clientVersion
$state = Read-TaskInstallState $InstallRoot
if ($null -ne $state) {
    $current = ConvertTo-TaskVersion $state.currentVersion
    if ($incoming -lt $current -and -not $AllowDowngrade) { throw "Downgrade from $current to $incoming is prohibited." }
    if ($state.publisherThumbprint -ne $ExpectedPublisherThumbprint.Replace(' ', '').ToUpperInvariant()) { throw 'Installed publisher identity does not match the release publisher.' }
}
$releaseRoot = Join-Path $InstallRoot "releases\$incoming"
if (-not (Test-Path -LiteralPath $releaseRoot)) {
    $staging = "$releaseRoot.staging-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    try {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app') -Destination $staging -Recurse
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'tools') -Destination $staging -Recurse
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-TaskDesktop.ps1') -Destination $staging
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'release-manifest.json') -Destination $staging
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'release-signature.json') -Destination $staging
        Move-Item -LiteralPath $staging -Destination $releaseRoot
    } finally { if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force } }
}
Assert-TaskRelease $releaseRoot $ExpectedPublisherThumbprint | Out-Null
$previous = if ($null -ne $state -and $state.currentVersion -ne $incoming.ToString(3)) { $state.currentVersion } else { $state.previousVersion }
$newState = [ordered]@{ schemaVersion = 1; currentVersion = $incoming.ToString(3); previousVersion = $previous; publisherThumbprint = $ExpectedPublisherThumbprint.Replace(' ', '').ToUpperInvariant(); installedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') }
Write-TaskInstallState $InstallRoot $newState
if (-not $NoShellIntegration) { Set-TaskShellIntegration $InstallRoot $incoming.ToString(3) }
[pscustomobject]@{ result = 'installed'; version = $incoming.ToString(3); previousVersion = $previous; installRoot = $InstallRoot }
