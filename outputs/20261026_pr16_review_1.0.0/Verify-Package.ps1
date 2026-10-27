$ErrorActionPreference = 'Stop'
function Get-ReviewHash([string]$Path) {
    $bytes = if ([IO.Path]::GetExtension($Path) -eq '.png') { [IO.File]::ReadAllBytes($Path) }
    else { [Text.Encoding]::UTF8.GetBytes([IO.File]::ReadAllText($Path).Replace("`r`n", "`n")) }
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]$bytes)).ToLowerInvariant()
}
$manifestPath = Join-Path $PSScriptRoot 'manifest.json'
$expected = ((Get-Content (Join-Path $PSScriptRoot 'manifest.sha256') -Raw).Trim() -split '\s+')[0]
if ((Get-ReviewHash $manifestPath) -ne $expected) { throw 'Manifest SHA-256 mismatch' }
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
foreach ($entry in $manifest.files) {
    if ((Get-ReviewHash (Join-Path $PSScriptRoot $entry.path)) -ne $entry.sha256) { throw "SHA-256 mismatch: $($entry.path)" }
}
Write-Output "PASS: $($manifest.files.Count) package files verified"
