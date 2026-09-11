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

# SIG # Begin signature block
# MIIHIQYJKoZIhvcNAQcCoIIHEjCCBw4CAQExDzANBglghkgBZQMEAgEFADB5Bgor
# BgEEAYI3AgEEoGswaTA0BgorBgEEAYI3AgEeMCYCAwEAAAQQH8w7YFlLCE63JNLG
# KX7zUQIBAAIBAAIBAAIBAAIBADAxMA0GCWCGSAFlAwQCAQUABCDr5za1Oiy97vUB
# L2mYyUR3GgA/s8vaXsnGkNzrV/TZR6CCBBAwggQMMIICdKADAgECAggw2RYt8462
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
# BCAK6RtGgc/qxzqWJFIhs5PdOKhROWcTTDcvJzJOfOGosjANBgkqhkiG9w0BAQEF
# AASCAYBPNMjVvn3i92kA2qFYjTz+1DfvGYqR4VBFENQzmsnRzXXzk6Rr6CnTfmVk
# bWW+CZtjidp/+cxlSytEASVEuXanwZjFi5+gwTM1X/GfH0gffzrlewRsTFpHHhnO
# Ex0OBuk/FXmxRkvYj3Zax2+pPr9oZ3uc0xwHjbkfbOXT/6l5sdIxVcLczAdotFbj
# rb4/MGnWKDBGIdojuyrEsLxgWsRE0IeBe8GNvnOo+p+f47BKSAxef32fx77c//Ds
# KU53Z2It3pOKNaMwWBWQ4nAd3fiainkqdBAqf2xR40RMIAE2DMdbtoMi1sXBB/zS
# K0QnIY7I4YBtMZeYI1NZi7CagR8QfHnos0rMvV3ecqoKyVM+Aji2DP7K9hZhgpCf
# liG99IxiYFc0Q6hxibHvonsoEqYhd5nAjp9rBUDikMRDulydCiuwc1CleECMytXJ
# D/y3LxGkuAmrEw5kwpE01PwuoCafeFpywp8N/RDNHghkcqXH256ccsCCpJA5bNGG
# SKnDrCk=
# SIG # End signature block
