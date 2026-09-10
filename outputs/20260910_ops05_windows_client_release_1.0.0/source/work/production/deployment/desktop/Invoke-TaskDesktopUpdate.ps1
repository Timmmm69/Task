[CmdletBinding(DefaultParameterSetName='Https')]
param(
    [Parameter(Mandatory)][string]$ExpectedPublisherThumbprint,
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\Task'),
    [Parameter(Mandatory, ParameterSetName='Https')][ValidatePattern('^https://')][uri]$ChannelUri,
    [Parameter(Mandatory, ParameterSetName='Local')][string]$ChannelPath,
    [Parameter(ParameterSetName='Local')][switch]$AllowLocalTestChannel,
    [string]$DeviceId = $env:COMPUTERNAME,
    [switch]$NoShellIntegration
)
$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $PSScriptRoot 'TaskDesktop.Release.psm1'
$moduleSignature = Get-AuthenticodeSignature -LiteralPath $modulePath
if ($moduleSignature.Status -in @('NotSigned','HashMismatch','NotSupportedFileFormat','Incompatible') -or $null -eq $moduleSignature.SignerCertificate -or $moduleSignature.SignerCertificate.Thumbprint -ne $ExpectedPublisherThumbprint.Replace(' ', '').ToUpperInvariant()) { throw 'The updater verifier is not signed by the expected publisher.' }
Import-Module $modulePath -Force
Assert-TaskSignedFile $PSCommandPath $ExpectedPublisherThumbprint
$temp = Join-Path ([IO.Path]::GetTempPath()) "task-update-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    if ($PSCmdlet.ParameterSetName -eq 'Local') {
        if (-not $AllowLocalTestChannel) { throw 'Local update channels are allowed only by the acceptance gate.' }
        $channelFile = (Resolve-Path -LiteralPath $ChannelPath).Path
        Copy-Item -LiteralPath $channelFile -Destination (Join-Path $temp 'channel.json')
        Copy-Item -LiteralPath "$channelFile.signature.json" -Destination (Join-Path $temp 'channel-signature.json')
    } else {
        $base = [uri]::new($ChannelUri, '.')
        Invoke-WebRequest -Uri $ChannelUri -OutFile (Join-Path $temp 'channel.json') -UseBasicParsing
        Invoke-WebRequest -Uri ([uri]::new($ChannelUri.AbsoluteUri + '.signature.json')) -OutFile (Join-Path $temp 'channel-signature.json') -UseBasicParsing
    }
    Assert-TaskDetachedSignature (Join-Path $temp 'channel.json') (Join-Path $temp 'channel-signature.json') $ExpectedPublisherThumbprint | Out-Null
    $channel = Get-Content -LiteralPath (Join-Path $temp 'channel.json') -Raw | ConvertFrom-Json
    if ($channel.schemaVersion -ne 1 -or $channel.product -ne 'Task.Desktop' -or $channel.channel -notin @('stable','pilot')) { throw 'Unsupported update channel manifest.' }
    $state = Read-TaskInstallState $InstallRoot
    if ($null -eq $state) { throw 'Task must be installed before it can be updated.' }
    $current = ConvertTo-TaskVersion $state.currentVersion
    $target = ConvertTo-TaskVersion $channel.clientVersion
    $minimum = ConvertTo-TaskVersion $channel.minimumClientVersion
    if ($target -le $current) { return [pscustomobject]@{ result = 'current'; version = $current.ToString(3) } }
    $mandatory = $current -lt $minimum
    $hashBytes = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("$($channel.rolloutSalt):$DeviceId"))
    $bucket = [BitConverter]::ToUInt32($hashBytes, 0) % 100
    if (-not $mandatory -and $bucket -ge [int]$channel.rolloutPercent) { return [pscustomobject]@{ result = 'deferred'; version = $current.ToString(3); rolloutBucket = $bucket } }
    $zip = Join-Path $temp 'release.zip'
    if ($PSCmdlet.ParameterSetName -eq 'Local') {
        $source = Join-Path (Split-Path $channelFile) $channel.packageFile
        Copy-Item -LiteralPath $source -Destination $zip
    } else {
        $releaseUri = [uri]$channel.releaseUri
        if ($releaseUri.Scheme -ne 'https') { throw 'The release URI must use HTTPS.' }
        Invoke-WebRequest -Uri $releaseUri -OutFile $zip -UseBasicParsing
    }
    if ((Get-TaskSha256 $zip) -ne $channel.packageSha256) { throw 'Downloaded release hash does not match the signed channel.' }
    $expanded = Join-Path $temp 'release'
    Expand-Archive -LiteralPath $zip -DestinationPath $expanded
    $releaseManifest = Assert-TaskRelease $expanded $ExpectedPublisherThumbprint
    if ($releaseManifest.clientVersion -ne $channel.clientVersion) { throw 'Channel and release versions do not match.' }
    & (Join-Path $expanded 'Install-TaskDesktop.ps1') -ExpectedPublisherThumbprint $ExpectedPublisherThumbprint -InstallRoot $InstallRoot -NoShellIntegration:$NoShellIntegration
} finally {
    if (Test-Path -LiteralPath $temp) { [IO.Directory]::Delete($temp, $true) }
}
