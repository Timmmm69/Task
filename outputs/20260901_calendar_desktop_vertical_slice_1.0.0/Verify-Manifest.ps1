Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSCommandPath
$manifest = Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
$expected = @($manifest.artifacts.path | Sort-Object)
$actual = @(Get-ChildItem -LiteralPath $root -File |
    Where-Object Name -NotIn @('manifest.json', 'SHA256SUMS', 'Verify-Manifest.ps1') |
    ForEach-Object Name | Sort-Object)

if (Compare-Object $expected $actual) { throw 'Manifest file set mismatch.' }
foreach ($artifact in $manifest.artifacts) {
    $path = Join-Path $root $artifact.path
    $file = Get-Item -LiteralPath $path
    if ($file.Length -ne $artifact.size) { throw "Size mismatch: $($artifact.path)" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $artifact.sha256) { throw "Hash mismatch: $($artifact.path)" }
}

$sumLines = Get-Content -LiteralPath (Join-Path $root 'SHA256SUMS')
if ($sumLines.Count -ne $manifest.artifacts.Count) { throw 'SHA256SUMS entry count mismatch.' }
foreach ($artifact in $manifest.artifacts) {
    $expectedLine = "$($artifact.sha256)  $($artifact.path)"
    if ($sumLines -notcontains $expectedLine) { throw "SHA256SUMS mismatch: $($artifact.path)" }
}
Write-Host "PASS: $($manifest.artifacts.Count) artifacts verified for $($manifest.package) $($manifest.version)."
