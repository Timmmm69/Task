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

# SIG # Begin signature block
# MIIHIQYJKoZIhvcNAQcCoIIHEjCCBw4CAQExDzANBglghkgBZQMEAgEFADB5Bgor
# BgEEAYI3AgEEoGswaTA0BgorBgEEAYI3AgEeMCYCAwEAAAQQH8w7YFlLCE63JNLG
# KX7zUQIBAAIBAAIBAAIBAAIBADAxMA0GCWCGSAFlAwQCAQUABCBdv+MoCQNrAdsb
# tZJCksM8U6FmhcJ0ug/6DrVH+MyuNqCCBBAwggQMMIICdKADAgECAggw2RYt8462
# SjANBgkqhkiG9w0BAQsFADApMScwJQYDVQQDEx5UYXNrIEhBTkQtMDMgSW50ZXJu
# YWwgUkMgMS4wLjAwHhcNMjYwOTExMTU0NDE5WhcNMjgwOTExMTU0OTE5WjApMScw
# JQYDVQQDEx5UYXNrIEhBTkQtMDMgSW50ZXJuYWwgUkMgMS4wLjAwggGiMA0GCSqG
# SIb3DQEBAQUAA4IBjwAwggGKAoIBgQDalqq2I89jW4s73YplsVpXqhsWReH3JX4y
# Tnlvo0caLbGT5iiI1IwvaLKL4jILIMNKTVKdZxAzvRRM4VsvIN3Vc5jHExjBWf/r
# +rVIXn/ftQzf5O6oyuPybmd11jz5R/kH4NE6j7176E2QJGY1EHmeGeCU3u/77/wf
# KAuVod5BFrAzrYofjxvQs0w99W+I/UElL5kR9aNX24btdauuBJI34iOQyNrldz9I
# W/zWm8RkdrJZbw3pnMK7ME6IALwMleIJwc1FbVjSXB2i1rTIfGnFwE23+er72ZU/
# N5XeNG0wNSY/3Z5aqkyO8YWS5lqLSG5fU6NNQ6Ok12nP3fG7J22JO6vSqQ687p+2
# lroXa0Y+aQ1BaEl14oEZDl9IzYFAbtqiRgpWWm6O9LRL2u2qtcI9dxggmt4AeeiY
# zhdoiO2klDBUTn1CmsJhLh0pFF+cxBy3sKP1lgydVwkTQqYFKwkTLllqhQx0xiGQ
# XBH+SzD6JudP3ChRZkFbPAsg4RJtB30CAwEAAaM4MDYwDAYDVR0TAQH/BAIwADAO
# BgNVHQ8BAf8EBAMCB4AwFgYDVR0lAQH/BAwwCgYIKwYBBQUHAwMwDQYJKoZIhvcN
# AQELBQADggGBACV0kzFNLEQV/jj0trRRTfm8FQIni/DCdEQJOWbEUwlvBvQE7OGo
# QgCmwFpNIYnyifaWnFGke2pEcfGbPi3aoxiqzb+wpVI9J6Ls4PadFM6eASZKPjl5
# FotpVr8AUm6rkHl3iaGTEAi+uGFAbringvAuKUfAL9MoqwcEK3TIBYS8bPKqz4Du
# 3oVzOfrQ6Zq+WWQN0Twe1NToHz6kz7FlkNgbVPCbYRnsAatDFZ+vi4gaRI+PnMI+
# uMyiw3PolIYr2ur9BtRmfS0EfilL5PDbKzY9Wx6t+U+Up47tX45z19VUmobQ/Jd1
# DSExfVEKIRIYH0pn/VWvX+pvM2f+pm45cFkoC7Y79iAetvMyQfgbl6LEf4a4Wlkp
# Ft41O7Um0q3pZCc0XxaIW4VLv7swkjUVf7kkYYijGF4cz93FbwVyuzwu9ulx6uck
# NlG2eFOTjbY0Hm3QrwCqJ/OChhThK+1l2/ojsPYVxAtaW9IYD0L51lmESdky4+Ms
# kqskSNEJgCygZTGCAmcwggJjAgEBMDUwKTEnMCUGA1UEAxMeVGFzayBIQU5ELTAz
# IEludGVybmFsIFJDIDEuMC4wAggw2RYt8462SjANBglghkgBZQMEAgEFAKCBhDAY
# BgorBgEEAYI3AgEMMQowCKACgAChAoAAMBkGCSqGSIb3DQEJAzEMBgorBgEEAYI3
# AgEEMBwGCisGAQQBgjcCAQsxDjAMBgorBgEEAYI3AgEVMC8GCSqGSIb3DQEJBDEi
# BCDWw5XNwEIviR7J2zGTE91NJloptQ4zSBWb1CkkZRIWDjANBgkqhkiG9w0BAQEF
# AASCAYDD2X9028H2dYIAHDyFmsCkdYUFOuBvgVvlVxkhEQ5O6B1D6MGwSh1/I+Ct
# e0DkmrYWAH6g83oh9mm+uWqhocOC2f0e26shdyLIJmRdcly5wHMmvpWe/5g9XqUy
# J1LPqjtbrFq1lTMI4yIToxQtCf0YcebqkCQhShBgBayRrUg74wiOkAaM3nd65Nu8
# xBafs0DYQZtZa1BybDFPHKHyD2CFKFKHF/fTR6ZzgiEdxMBWuTvp0t+DDw9TKih9
# ZW7QYRoxd9jJwZjjZ1WtCqNjrg+ZQoJECPTpoxIjEg8yLEH3u6v8XH2OSn8pnoVL
# tlGz6dpi591uSe+vwJHNj7no8KtqGfHreQebiknMnHzZQXGBgEmR6D3hbmCXl062
# h9myfJ2u3Tm3zMmsR/jqDpje/MkDrw21FJ9kHWp9eItpydvsPExOCvPKW8GAGa+6
# ogZenlzCYACfOsQH3Rv0G+3hgO8etFdKMAU1eMTu9+Pzkgg17s4Y5R6STUeXrFOF
# HoXHR4c=
# SIG # End signature block
