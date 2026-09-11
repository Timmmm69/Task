Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-TaskSha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function ConvertTo-TaskVersion([string]$Value) {
    if ($Value -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid three-part release version: $Value" }
    [Version]$Value
}

function Assert-TaskSignedFile([string]$Path, [string]$ExpectedThumbprint) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -in @(
        [Management.Automation.SignatureStatus]::NotSigned,
        [Management.Automation.SignatureStatus]::HashMismatch,
        [Management.Automation.SignatureStatus]::NotSupportedFileFormat,
        [Management.Automation.SignatureStatus]::Incompatible
    ) -or $null -eq $signature.SignerCertificate) {
        throw "Authenticode signature is not valid for '$Path': $($signature.Status)."
    }
    $actual = $signature.SignerCertificate.Thumbprint.Replace(' ', '').ToUpperInvariant()
    if ($actual -ne $ExpectedThumbprint.Replace(' ', '').ToUpperInvariant()) {
        throw "Unexpected publisher for '$Path'."
    }
    $now = [DateTimeOffset]::UtcNow
    if ($signature.SignerCertificate.NotBefore.ToUniversalTime() -gt $now.UtcDateTime -or $signature.SignerCertificate.NotAfter.ToUniversalTime() -le $now.UtcDateTime) {
        throw "Publisher certificate is outside its validity window for '$Path'."
    }
}

function New-TaskDetachedSignature(
    [string]$Path,
    [Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
    [string]$Destination
) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($Certificate)
    if ($null -ne $rsa) {
        $algorithm = 'RSA-SHA256'
        $signature = $rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    } else {
        $ecdsa = [Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPrivateKey($Certificate)
        if ($null -eq $ecdsa) { throw 'The release certificate must have an RSA or ECDSA private key.' }
        $algorithm = 'ECDSA-SHA256'
        $signature = $ecdsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256)
    }
    [ordered]@{
        schemaVersion = 1
        algorithm = $algorithm
        certificate = [Convert]::ToBase64String($Certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
        signature = [Convert]::ToBase64String($signature)
    } | ConvertTo-Json | Set-Content -LiteralPath $Destination -Encoding utf8NoBOM
}

function Assert-TaskDetachedSignature([string]$Path, [string]$SignaturePath, [string]$ExpectedThumbprint) {
    $document = Get-Content -LiteralPath $SignaturePath -Raw | ConvertFrom-Json
    if ($document.schemaVersion -ne 1) { throw "Unsupported detached signature schema in '$SignaturePath'." }
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new([Convert]::FromBase64String($document.certificate))
    $actual = $certificate.Thumbprint.Replace(' ', '').ToUpperInvariant()
    if ($actual -ne $ExpectedThumbprint.Replace(' ', '').ToUpperInvariant()) { throw "Unexpected detached-signature publisher for '$Path'." }
    $eku = @($certificate.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] })
    if ($eku.Count -ne 1 -or @($eku[0].EnhancedKeyUsages | Where-Object Value -eq '1.3.6.1.5.5.7.3.3').Count -ne 1) {
        throw 'The publisher certificate is not valid for code signing.'
    }
    $now = [DateTimeOffset]::UtcNow
    if ($certificate.NotBefore.ToUniversalTime() -gt $now.UtcDateTime -or $certificate.NotAfter.ToUniversalTime() -le $now.UtcDateTime) { throw 'The publisher certificate is outside its validity window.' }
    $bytes = [IO.File]::ReadAllBytes($Path)
    $signature = [Convert]::FromBase64String($document.signature)
    if ($document.algorithm -eq 'RSA-SHA256') {
        $key = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($certificate)
        $valid = $key.VerifyData($bytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    } elseif ($document.algorithm -eq 'ECDSA-SHA256') {
        $key = [Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPublicKey($certificate)
        $valid = $key.VerifyData($bytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256)
    } else { throw "Unsupported signature algorithm '$($document.algorithm)'." }
    if (-not $valid) { throw "Detached signature verification failed for '$Path'." }
    $certificate
}

function Assert-TaskRelease([string]$ReleaseRoot, [string]$ExpectedThumbprint) {
    $manifestPath = Join-Path $ReleaseRoot 'release-manifest.json'
    $signaturePath = Join-Path $ReleaseRoot 'release-signature.json'
    Assert-TaskDetachedSignature $manifestPath $signaturePath $ExpectedThumbprint | Out-Null
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.product -ne 'Task.Desktop') { throw 'Unsupported Task desktop release manifest.' }
    [void](ConvertTo-TaskVersion $manifest.clientVersion)
    $root = [IO.Path]::GetFullPath($ReleaseRoot).TrimEnd('\') + '\'
    foreach ($entry in @($manifest.files)) {
        $path = [IO.Path]::GetFullPath((Join-Path $ReleaseRoot ([string]$entry.path)))
        if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Manifest path escapes release root: $($entry.path)" }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Release file is missing: $($entry.path)" }
        if ((Get-TaskSha256 $path) -ne $entry.sha256) { throw "Release hash mismatch: $($entry.path)" }
        if ($entry.authenticode) { Assert-TaskSignedFile $path $ExpectedThumbprint }
    }
    $manifest
}

function Read-TaskInstallState([string]$InstallRoot) {
    $path = Join-Path $InstallRoot 'state.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function Write-TaskInstallState([string]$InstallRoot, [object]$State) {
    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    $path = Join-Path $InstallRoot 'state.json'
    $temp = "$path.$([Guid]::NewGuid().ToString('N')).tmp"
    $State | ConvertTo-Json | Set-Content -LiteralPath $temp -Encoding utf8NoBOM
    Move-Item -LiteralPath $temp -Destination $path -Force
}

function Set-TaskShellIntegration([string]$InstallRoot, [string]$Version) {
    $exe = Join-Path $InstallRoot "releases\$Version\app\Task.Desktop.exe"
    $programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    $shortcutPath = Join-Path $programs 'Task.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = Split-Path $exe
    $shortcut.Description = 'Task company organizer'
    $shortcut.Save()
    $uninstall = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TaskDesktop'
    New-Item -Path $uninstall -Force | Out-Null
    Set-ItemProperty -Path $uninstall -Name DisplayName -Value 'Task'
    Set-ItemProperty -Path $uninstall -Name DisplayVersion -Value $Version
    Set-ItemProperty -Path $uninstall -Name Publisher -Value 'Task'
    Set-ItemProperty -Path $uninstall -Name InstallLocation -Value $InstallRoot
}

Export-ModuleMember -Function *-Task*

# SIG # Begin signature block
# MIIHIQYJKoZIhvcNAQcCoIIHEjCCBw4CAQExDzANBglghkgBZQMEAgEFADB5Bgor
# BgEEAYI3AgEEoGswaTA0BgorBgEEAYI3AgEeMCYCAwEAAAQQH8w7YFlLCE63JNLG
# KX7zUQIBAAIBAAIBAAIBAAIBADAxMA0GCWCGSAFlAwQCAQUABCA76Sr8o7DJWoMq
# dexiPPFezqCaZ0bUou7Nzed7zv4vUaCCBBAwggQMMIICdKADAgECAggw2RYt8462
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
# BCAPDyraSJxen+RjIyjdZVZrOKCV3mzeJ1N+jAVeM1hICjANBgkqhkiG9w0BAQEF
# AASCAYB5NpiEwoiTixEloIydAT0E0f7iOPoKjf8AF/VGc88al1tMU76cDApvYk93
# AeAn/Lh3l8JS1rHZ73UgyxRu1ThCNepxBlRy75iE3NSgRYHORYp6BgE4ITTXzem0
# WNZP0RXpH3M1XKFdtzMU9E9YF2Q6u5t50xL8BE5Mx+PJalMkHibujw7+qn9cYt0K
# 6+5c1aXdonjeeMSGv7jMaMwEdnoRJt5Hn4pfTMSBv/H/4dzxE3Ava7/lwRb6T6ZW
# JwYXsyyj79OhYZay6JfutRNIgsxLxf/LM/795axcgMz5JQetv7ya2uQrx1TccVGq
# Z8GFIy2Kfbo1xczJUXTbPYAls0yKSKqaCFhwvafb6r4vvp4QKt1IlENEc1amkG7L
# 3j0NZX9mIXOhJs9RGUpGAVsZ+TRs4b90ta3n7icZNXR7Eo5ApzPcTBfQFG5UIImz
# OR9RSokiyOP7+g72+IkrO7I5B3kij7zfdfUdjU2e3FUPDKFejLALFkty2gCjLIWT
# iCPX2iI=
# SIG # End signature block
