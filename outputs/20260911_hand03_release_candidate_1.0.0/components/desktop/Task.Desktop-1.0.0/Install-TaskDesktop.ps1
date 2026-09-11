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

# SIG # Begin signature block
# MIIHIQYJKoZIhvcNAQcCoIIHEjCCBw4CAQExDzANBglghkgBZQMEAgEFADB5Bgor
# BgEEAYI3AgEEoGswaTA0BgorBgEEAYI3AgEeMCYCAwEAAAQQH8w7YFlLCE63JNLG
# KX7zUQIBAAIBAAIBAAIBAAIBADAxMA0GCWCGSAFlAwQCAQUABCARTA5gyuexIhXj
# uEy8+t3U+6txQj0eSiGa6DkqregE6aCCBBAwggQMMIICdKADAgECAggw2RYt8462
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
# BCBwdPhQIQZZdTQsSYzK7EjUU+13b5/pqOaFiPY06hKSlDANBgkqhkiG9w0BAQEF
# AASCAYBFNXZQQ24UeOkDIWTZyQ3jMo6pBwuZmAx9d/yKEvGrwWWXSPV+zZECkG97
# e3aORf6NRMOuDZK3kLumAF5fHHicCDaIrfQV1wxmAqnIsL+uJ6AdhaXvtnM+iP9A
# niEYDW4ehVspqxKcWuICtyrTkamiypZuibRzNXqG/U2c05lijSur2aZZ2cTxwO+E
# uUbIk2tpifZp0gNZK3y3Ytsx7EFzJqHiFiipsjVYmnbDyjqRQZxEpBf0USKSFgLK
# 5WClj1E0DYJ38XPgvO+ITTZ80R7Rq2GHFZo0rLxkTOIRB7VfaG9+1HV9vUebW8VK
# KtQnA8+vrfQZD2mgpBi0w8e1hAbRI/eJOPN+Z1AzGkh0hvBqS9K7ZZxFZtgJXY0A
# VhlKtWkg+zrkvVcv9dKAGUIpaM2y3pUr4mu8Y8s4xV+dsOR0TvLvIa/NM2vS2Uax
# zB/rnJ0sLGnHtL6wStmEsCq6Mv4RyM17JBc3D8lSK0Wdu6Z/XSab/AVIeEFhMdIv
# EPCCOOE=
# SIG # End signature block
