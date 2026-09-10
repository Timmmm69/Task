[CmdletBinding()]
param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '..\evidence\ops05'))
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'OPS-05 acceptance requires Windows.' }
$deployment = (Resolve-Path (Join-Path $PSScriptRoot '..\deployment\desktop')).Path
$temp = Join-Path ([IO.Path]::GetTempPath()) "task-ops05-$([Guid]::NewGuid().ToString('N'))"
$subject = "CN=Task OPS-05 Acceptance $([Guid]::NewGuid().ToString('N'))"
$rsa = [Security.Cryptography.RSA]::Create(3072)
$request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
    $subject, $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256,
    [Security.Cryptography.RSASignaturePadding]::Pkcs1)
$request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
$request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true))
$oids = [Security.Cryptography.OidCollection]::new()
[void]$oids.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
$request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids, $true))
$created = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddDays(30))
$certificate = $created
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $v1 = Join-Path $temp 'v1'
    $v2 = Join-Path $temp 'v2'
    $installRoot = Join-Path $temp 'installed'
    Write-Host 'OPS-05: building signed 1.0.0 release'
    & (Join-Path $deployment 'Build-TaskDesktopRelease.ps1') -Version '1.0.0' -Certificate $certificate -OutputDirectory $v1 -MinimumClientVersion '1.0.0' | Out-Null
    Write-Host 'OPS-05: building signed mandatory 1.1.0 release'
    & (Join-Path $deployment 'Build-TaskDesktopRelease.ps1') -Version '1.1.0' -Certificate $certificate -OutputDirectory $v2 -MinimumClientVersion '1.1.0' -RolloutPercent 0 | Out-Null
    Write-Host 'OPS-05: installing, updating, rejecting tamper/downgrade, and rolling back'
    $release1 = Join-Path $v1 'Task.Desktop-1.0.0'
    & (Join-Path $release1 'Install-TaskDesktop.ps1') -ExpectedPublisherThumbprint $certificate.Thumbprint -InstallRoot $installRoot -NoShellIntegration | Out-Null
    $state = Get-Content -LiteralPath (Join-Path $installRoot 'state.json') -Raw | ConvertFrom-Json
    if ($state.currentVersion -ne '1.0.0') { throw 'Initial installation did not activate 1.0.0.' }
    $localRejected = $false
    try {
        & (Join-Path $installRoot 'releases\1.0.0\tools\Invoke-TaskDesktopUpdate.ps1') -ExpectedPublisherThumbprint $certificate.Thumbprint -InstallRoot $installRoot -ChannelPath (Join-Path $v2 'channel.json') -NoShellIntegration | Out-Null
    } catch { $localRejected = $_.Exception.Message -like '*acceptance gate*' }
    if (-not $localRejected) { throw 'Local update channel was not fail-closed.' }
    $update = & (Join-Path $installRoot 'releases\1.0.0\tools\Invoke-TaskDesktopUpdate.ps1') -ExpectedPublisherThumbprint $certificate.Thumbprint -InstallRoot $installRoot -ChannelPath (Join-Path $v2 'channel.json') -AllowLocalTestChannel -DeviceId 'ops05-target-a' -NoShellIntegration
    $state = Get-Content -LiteralPath (Join-Path $installRoot 'state.json') -Raw | ConvertFrom-Json
    if ($state.currentVersion -ne '1.1.0' -or $state.previousVersion -ne '1.0.0') { throw 'Mandatory update did not atomically retain rollback state.' }
    $downgradeRejected = $false
    try { & (Join-Path $release1 'Install-TaskDesktop.ps1') -ExpectedPublisherThumbprint $certificate.Thumbprint -InstallRoot $installRoot -NoShellIntegration | Out-Null } catch { $downgradeRejected = $_.Exception.Message -like '*prohibited*' }
    if (-not $downgradeRejected) { throw 'Uncontrolled downgrade was not rejected.' }
    $tampered = Join-Path $temp 'tampered'
    Copy-Item -LiteralPath (Join-Path $v2 'Task.Desktop-1.1.0') -Destination $tampered -Recurse
    Add-Content -LiteralPath (Join-Path $tampered 'app\Task.Desktop.exe') -Value 'tamper' -NoNewline
    $tamperRejected = $false
    try { & (Join-Path $tampered 'Install-TaskDesktop.ps1') -ExpectedPublisherThumbprint $certificate.Thumbprint -InstallRoot (Join-Path $temp 'tamper-install') -NoShellIntegration | Out-Null } catch { $tamperRejected = $_.Exception.Message -like '*hash mismatch*' }
    if (-not $tamperRejected) { throw 'Tampered release was not rejected.' }
    & (Join-Path $installRoot 'releases\1.1.0\tools\Rollback-TaskDesktop.ps1') -ExpectedPublisherThumbprint $certificate.Thumbprint -InstallRoot $installRoot -NoShellIntegration | Out-Null
    $state = Get-Content -LiteralPath (Join-Path $installRoot 'state.json') -Raw | ConvertFrom-Json
    if ($state.currentVersion -ne '1.0.0' -or $state.previousVersion -ne '1.1.0') { throw 'Rollback did not restore the previous validated release.' }
    $signatures = Get-ChildItem -LiteralPath (Join-Path $installRoot 'releases') -Recurse -File | Where-Object Extension -in @('.exe','.ps1','.psm1') | ForEach-Object { Get-AuthenticodeSignature -LiteralPath $_.FullName }
    if (@($signatures | Where-Object { $_.Status -in @('NotSigned','HashMismatch','NotSupportedFileFormat','Incompatible') -or $_.SignerCertificate.Thumbprint -ne $certificate.Thumbprint }).Count -ne 0) { throw 'Installed Authenticode publisher-pin validation failed.' }
    New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
    [ordered]@{
        task = 'OPS-05'; result = 'PASS'; testedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        host = [ordered]@{ os = [Environment]::OSVersion.VersionString; architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString(); powershell = $PSVersionTable.PSVersion.ToString() }
        scenarios = [ordered]@{ signedInitialInstall = $true; mandatoryUpdateOverridesRolloutZero = $update.result -eq 'installed'; localChannelFailClosed = $localRejected; tamperRejected = $tamperRejected; uncontrolledDowngradeRejected = $downgradeRejected; validatedRollback = $true; installedPublisherPinsValid = $true }
        versions = @('1.0.0','1.1.0'); publisherThumbprint = $certificate.Thumbprint
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'acceptance.json') -Encoding utf8NoBOM
    Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'acceptance.json') -Raw
} finally {
    if (Test-Path -LiteralPath $temp) { [IO.Directory]::Delete($temp, $true) }
}
