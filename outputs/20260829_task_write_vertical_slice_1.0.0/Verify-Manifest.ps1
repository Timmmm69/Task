[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
$manifestPath = Join-Path $root 'manifest.json'
$sumsPath = Join-Path $root 'SHA256SUMS'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'manifest.json is missing.' }
if (-not (Test-Path -LiteralPath $sumsPath -PathType Leaf)) { throw 'SHA256SUMS is missing.' }

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$actualPayload = @(Get-ChildItem -LiteralPath $root -File -Recurse |
    Where-Object { $_.FullName -notin @($manifestPath, $sumsPath) } |
    ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') } |
    Sort-Object)
$listedPayload = @($manifest.files | ForEach-Object { [string]$_.path } | Sort-Object)

if (($actualPayload -join "`n") -ne ($listedPayload -join "`n")) {
    throw 'Manifest file set does not match the package payload.'
}

foreach ($entry in $manifest.files) {
    $path = Join-Path $root ([string]$entry.path).Replace('/', [IO.Path]::DirectorySeparatorChar)
    $file = Get-Item -LiteralPath $path
    if ($file.Length -ne [long]$entry.size) { throw "Size mismatch: $($entry.path)" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne [string]$entry.sha256) { throw "SHA-256 mismatch: $($entry.path)" }
}

$sumEntries = @{}
foreach ($line in Get-Content -LiteralPath $sumsPath) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Invalid SHA256SUMS line: $line" }
    $sumEntries[$Matches[2]] = $Matches[1]
}
$sumTargets = @($actualPayload + 'manifest.json' | Sort-Object)
if (($sumEntries.Keys | Sort-Object) -join "`n" -ne ($sumTargets -join "`n")) {
    throw 'SHA256SUMS file set does not match payload plus manifest.json.'
}
foreach ($relative in $sumTargets) {
    $path = Join-Path $root $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($sumEntries[$relative] -ne $hash) { throw "SHA256SUMS mismatch: $relative" }
}

Write-Host "[PASS] Package integrity verified: $($manifest.files.Count) payload files."
