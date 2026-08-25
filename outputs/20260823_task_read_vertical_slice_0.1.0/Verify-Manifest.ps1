[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$packageDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path $packageDirectory 'MANIFEST.sha256'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Manifest not found: $manifestPath"
}

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($line in Get-Content -LiteralPath $manifestPath) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    if ($line -notmatch '^([0-9a-fA-F]{64})  (.+)$') {
        $failures.Add("Invalid manifest line: $line")
        continue
    }

    $expected = $Matches[1].ToLowerInvariant()
    $relativePath = $Matches[2]
    $filePath = Join-Path $packageDirectory $relativePath
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        $failures.Add("Missing file: $relativePath")
        continue
    }

    $actual = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        $failures.Add("Hash mismatch: $relativePath")
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output 'Manifest verification passed.'
