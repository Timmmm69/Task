[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory)

$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.product -ne 'Task unsigned portable Windows client') { throw 'Unexpected manifest.' }
$payload = Join-Path $package 'Task'
$root = [IO.Path]::GetFullPath($payload).TrimEnd('\') + '\'
foreach ($entry in @($manifest.files)) {
    $path = [IO.Path]::GetFullPath((Join-Path $payload ([string]$entry.path)))
    if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Manifest path escapes package: $($entry.path)" }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing payload: $($entry.path)" }
    $item = Get-Item -LiteralPath $path
    if ($item.Length -ne $entry.bytes) { throw "Size mismatch: $($entry.path)" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw "Hash mismatch: $($entry.path)" }
}
$actualFiles = @(Get-ChildItem -LiteralPath $payload -Recurse -File)
if ($actualFiles.Count -ne @($manifest.files).Count) { throw 'Unexpected number of payload files.' }
$archive = Join-Path $package "Task-$($manifest.version)-win-x64-unsigned.zip"
if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) { throw 'Archive is missing.' }
$expectedLine = (Get-Content -LiteralPath (Join-Path $package 'SHA256SUMS') -Raw).Trim()
$actualLine = "$( (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() )  $([IO.Path]::GetFileName($archive))"
if ($actualLine -ne $expectedLine) { throw 'Archive hash mismatch.' }
$temporary = Join-Path ([IO.Path]::GetTempPath()) "task-portable-check-$([Guid]::NewGuid().ToString('N'))"
try {
    Expand-Archive -LiteralPath $archive -DestinationPath $temporary
    $expanded = Join-Path $temporary 'Task'
    foreach ($entry in @($manifest.files)) {
        $path = Join-Path $expanded ([string]$entry.path)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Archive payload missing: $($entry.path)" }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw "Archive payload hash mismatch: $($entry.path)" }
    }
    if (@(Get-ChildItem -LiteralPath $expanded -Recurse -File).Count -ne @($manifest.files).Count) { throw 'Archive contains unexpected payload files.' }
} finally {
    if (Test-Path -LiteralPath $temporary) {
        $resolved = (Resolve-Path -LiteralPath $temporary).Path
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Temporary extraction escaped the temp directory.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
[pscustomobject]@{ result = 'PASS'; version = $manifest.version; files = $actualFiles.Count; archiveSha256 = $actualLine.Split(' ')[0] }
