[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packageDirectory = $PSScriptRoot
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $packageDirectory)
$manifestPath = Join-Path $packageDirectory 'MANIFEST.sha256'
$failed = $false

foreach ($line in Get-Content -LiteralPath $manifestPath) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') {
        Write-Error "Invalid manifest line: $line"
        $failed = $true
        continue
    }

    $expected = $Matches[1]
    $relativePath = $Matches[2]
    $filePath = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        Write-Error "Manifest file is missing: $relativePath"
        $failed = $true
        continue
    }

    $bytes = [System.IO.File]::ReadAllBytes($filePath)
    $canonicalBytes = New-Object System.Collections.Generic.List[byte]
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        if ($bytes[$index] -eq 13 -and $index + 1 -lt $bytes.Length -and $bytes[$index + 1] -eq 10) {
            $canonicalBytes.Add(10)
            $index++
            continue
        }

        $canonicalBytes.Add($bytes[$index])
    }

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $actual = ([BitConverter]::ToString($sha256.ComputeHash($canonicalBytes.ToArray()))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }

    if ($actual -ne $expected) {
        Write-Error "SHA-256 mismatch: $relativePath"
        $failed = $true
    }
}

if ($failed) {
    exit 1
}

Write-Host '[ OK ] All canonical manifest SHA-256 values match.'
exit 0
