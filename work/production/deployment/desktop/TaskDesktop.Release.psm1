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

function Initialize-VoravaDentShellLink {
    if ('VoravaDentDesktop.ShellLink' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace VoravaDentDesktop
{
    public static class ShellLink
    {
        private static readonly Guid ClassId = new Guid("00021401-0000-0000-C000-000000000046");

        public static void Write(string file, string target, string directory, string description)
        {
            var link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(ClassId, true));
            link.SetPath(target);
            link.SetWorkingDirectory(directory);
            link.SetDescription(description);
            link.SetIconLocation(target, 0);
            ((IPersistFile)link).Save(file, true);
        }

        public static string ReadTarget(string file)
        {
            var link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(ClassId, true));
            ((IPersistFile)link).Load(file, 0);
            var target = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4);
            return target.ToString();
        }

        // Slot order and marshaling follow the Windows IShellLinkW interface.
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, IntPtr findData, int flags);
            void GetIDList(out IntPtr item);
            void SetIDList(IntPtr item);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int capacity);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int capacity);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int capacity);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int show);
            void SetShowCmd(int show);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder icon, int capacity, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
            void Resolve(IntPtr window, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        }
    }
}
'@
}

function Set-TaskShellIntegration([string]$InstallRoot, [string]$Version) {
    $appRoot = Join-Path $InstallRoot "releases\$Version\app"
    $exe = Join-Path $appRoot 'VoravaDent.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        $exe = Join-Path $appRoot 'Task.Desktop.exe'
    }
    $programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    $product = 'Варава Дент'
    $releasePrefix = [IO.Path]::GetFullPath((Join-Path $InstallRoot 'releases')) + [IO.Path]::DirectorySeparatorChar
    $shortcutPath = Join-Path $programs "$product.lnk"
    Initialize-VoravaDentShellLink
    [VoravaDentDesktop.ShellLink]::Write($shortcutPath, $exe, $appRoot, $product)
    $legacyPath = Join-Path $programs 'Task.lnk'
    if (Test-Path -LiteralPath $legacyPath -PathType Leaf) {
        $legacyTarget = [VoravaDentDesktop.ShellLink]::ReadTarget($legacyPath)
        if ($legacyTarget.StartsWith($releasePrefix, [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetFileName($legacyTarget) -eq 'Task.Desktop.exe') {
            Remove-Item -LiteralPath $legacyPath
        }
    }
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (Test-Path -LiteralPath $runKey) {
        foreach ($name in 'Task', $product) {
            $command = Get-ItemPropertyValue -LiteralPath $runKey -Name $name -ErrorAction SilentlyContinue
            if ($command -match '^"([^"]+)" --background$' -and
                $Matches[1].StartsWith($releasePrefix, [StringComparison]::OrdinalIgnoreCase) -and
                [IO.Path]::GetFileName($Matches[1]) -in 'Task.Desktop.exe', 'VoravaDent.exe') {
                Set-ItemProperty -LiteralPath $runKey -Name $product -Value "`"$exe`" --background"
                if ($name -eq 'Task') { Remove-ItemProperty -LiteralPath $runKey -Name $name }
            }
        }
    }
    $uninstall = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TaskDesktop'
    New-Item -Path $uninstall -Force | Out-Null
    Set-ItemProperty -Path $uninstall -Name DisplayName -Value $product
    Set-ItemProperty -Path $uninstall -Name DisplayIcon -Value $exe
    Set-ItemProperty -Path $uninstall -Name DisplayVersion -Value $Version
    Set-ItemProperty -Path $uninstall -Name Publisher -Value $product
    Set-ItemProperty -Path $uninstall -Name InstallLocation -Value $InstallRoot
}

Export-ModuleMember -Function *-Task*
