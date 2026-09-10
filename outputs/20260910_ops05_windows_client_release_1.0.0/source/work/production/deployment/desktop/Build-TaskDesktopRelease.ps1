[CmdletBinding(DefaultParameterSetName='Store')]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory, ParameterSetName='Store')][string]$CertificateThumbprint,
    [Parameter(Mandatory, ParameterSetName='Certificate')][Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('stable','pilot')][string]$Channel = 'stable',
    [ValidateRange(0,100)][int]$RolloutPercent = 100,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$MinimumClientVersion = $Version,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$MinimumServerVersion = '1.0.0',
    [uri]$ReleaseUri,
    [uri]$TimestampServerUri
)
$ErrorActionPreference = 'Stop'
$desktopRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\src\Task.Desktop')).Path
if ($PSCmdlet.ParameterSetName -eq 'Store') {
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$($CertificateThumbprint.Replace(' ', ''))"
} else {
    $certificate = $Certificate
}
if (-not $certificate.HasPrivateKey) { throw 'The release certificate has no private key.' }
$codeSigningOid = '1.3.6.1.5.5.7.3.3'
$eku = @($certificate.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] })
if ($eku.Count -ne 1 -or @($eku[0].EnhancedKeyUsages | Where-Object Value -eq $codeSigningOid).Count -ne 1) { throw 'A code-signing certificate is required.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output | Out-Null
$releaseRoot = Join-Path $output "Task.Desktop-$Version"
$appRoot = Join-Path $releaseRoot 'app'
$toolsRoot = Join-Path $releaseRoot 'tools'
New-Item -ItemType Directory -Path $appRoot,$toolsRoot | Out-Null
dotnet publish (Join-Path $desktopRoot 'Task.Desktop.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=$Version -p:DebugType=None -o $appRoot
if ($LASTEXITCODE -ne 0) { throw 'Task desktop publish failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-TaskDesktop.ps1') -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Invoke-TaskDesktopUpdate.ps1') -Destination $toolsRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Rollback-TaskDesktop.ps1') -Destination $toolsRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TaskDesktop.Release.psm1') -Destination $toolsRoot
function Sign-ReleaseFile([string]$Path) {
    $parameters = @{ FilePath = $Path; Certificate = $certificate; HashAlgorithm = 'SHA256'; IncludeChain = 'All' }
    if ($null -ne $TimestampServerUri) { $parameters.TimestampServer = $TimestampServerUri.AbsoluteUri }
    $signature = Set-AuthenticodeSignature @parameters
    if ($signature.Status -in @('NotSigned','HashMismatch','NotSupportedFileFormat','Incompatible') -or $null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) { throw "Signing failed for '$Path': $($signature.Status)." }
}
Get-ChildItem -LiteralPath $appRoot -File -Recurse | Where-Object Extension -eq '.exe' | ForEach-Object { Sign-ReleaseFile $_.FullName }
Get-ChildItem -LiteralPath $releaseRoot -File -Recurse | Where-Object Extension -in @('.ps1','.psm1') | ForEach-Object { Sign-ReleaseFile $_.FullName }
Import-Module (Join-Path $toolsRoot 'TaskDesktop.Release.psm1') -Force
$files = @(Get-ChildItem -LiteralPath $releaseRoot -File -Recurse | Where-Object Name -notin @('release-manifest.json','release-signature.json') | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($releaseRoot, $_.FullName).Replace('\','/'); size = $_.Length; sha256 = Get-TaskSha256 $_.FullName; authenticode = $_.Extension -in @('.exe','.ps1','.psm1') }
})
$manifest = [ordered]@{ schemaVersion = 1; product = 'Task.Desktop'; clientVersion = $Version; architecture = 'win-x64'; apiVersions = @('v1'); minimumServerVersion = $MinimumServerVersion; publisherThumbprint = $certificate.Thumbprint; createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); files = $files }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot 'release-manifest.json') -Encoding utf8NoBOM
New-TaskDetachedSignature (Join-Path $releaseRoot 'release-manifest.json') $certificate (Join-Path $releaseRoot 'release-signature.json')
Assert-TaskRelease $releaseRoot $certificate.Thumbprint | Out-Null
$zip = Join-Path $output "Task.Desktop-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $releaseRoot '*') -DestinationPath $zip -CompressionLevel Fastest
$channelDocument = [ordered]@{ schemaVersion = 1; product = 'Task.Desktop'; channel = $Channel; clientVersion = $Version; minimumClientVersion = $MinimumClientVersion; minimumServerVersion = $MinimumServerVersion; rolloutPercent = $RolloutPercent; rolloutSalt = [Guid]::NewGuid().ToString('N'); packageFile = [IO.Path]::GetFileName($zip); releaseUri = if ($null -ne $ReleaseUri) { $ReleaseUri.AbsoluteUri } else { $null }; packageSha256 = Get-TaskSha256 $zip; publisherThumbprint = $certificate.Thumbprint; publishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') }
$channelPath = Join-Path $output 'channel.json'
$channelDocument | ConvertTo-Json | Set-Content -LiteralPath $channelPath -Encoding utf8NoBOM
New-TaskDetachedSignature $channelPath $certificate "$channelPath.signature.json"
[pscustomobject]@{ release = $zip; channel = $channelPath; version = $Version; publisherThumbprint = $certificate.Thumbprint }
