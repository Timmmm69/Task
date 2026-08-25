[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$packageDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path $packageDirectory 'MANIFEST.sha256'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Manifest not found: $manifestPath"
}

$expectedFiles = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
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
    if (-not $expectedFiles.Add($relativePath)) {
        $failures.Add("Duplicate manifest entry: $relativePath")
        continue
    }

    $filePath = Join-Path $packageDirectory ($relativePath -replace '/', '\')
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        $failures.Add("Missing file: $relativePath")
        continue
    }

    $actual = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        $failures.Add("Hash mismatch: $relativePath")
    }
}

$actualFiles = Get-ChildItem -LiteralPath $packageDirectory -Recurse -File |
    Where-Object { $_.FullName -ne $manifestPath } |
    ForEach-Object {
        $_.FullName.Substring($packageDirectory.Length + 1).Replace('\', '/')
    }

foreach ($actualFile in $actualFiles) {
    if (-not $expectedFiles.Contains($actualFile)) {
        $failures.Add("Untracked package file: $actualFile")
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "Manifest verification passed for $($expectedFiles.Count) files."
