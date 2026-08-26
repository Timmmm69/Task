[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path $packageRoot 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$excluded = @('manifest.json', 'SHA256SUMS', 'Verify-Manifest.ps1')

$rootPrefix = $packageRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
$actualFiles = Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
    ForEach-Object {
        if (-not $_.FullName.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "File is outside the package root: $($_.FullName)"
        }

        $_.FullName.Substring($rootPrefix.Length).Replace('\', '/')
    } |
    Where-Object { $_ -notin $excluded } |
    Sort-Object
$expectedFiles = @($manifest.files.path) | Sort-Object

if (Compare-Object -ReferenceObject $expectedFiles -DifferenceObject $actualFiles) {
    throw 'Manifest file set does not match the package contents.'
}

foreach ($entry in $manifest.files) {
    $path = Join-Path $packageRoot $entry.path
    $item = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($item.Length -ne [long]$entry.sizeBytes -or $hash -ne $entry.sha256) {
        throw "Manifest mismatch: $($entry.path)"
    }
}

Write-Output "PASS: verified $($manifest.files.Count) content files for package $($manifest.packageVersion)."
