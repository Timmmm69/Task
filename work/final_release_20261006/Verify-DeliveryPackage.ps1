param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
if (-not $manifest.cleanSourceTree -or $manifest.sourceRevision -notmatch '^[0-9a-f]{40}$') { throw 'Invalid source provenance.' }
if ((Get-Content -LiteralPath (Join-Path $package 'VERSION') -Raw).Trim() -ne $manifest.version) { throw 'Version mismatch.' }
$expectedFiles = @($manifest.supportingFiles.path) + @('manifest.json','PACKAGE-SHA256SUMS')
$actualFiles = @(Get-ChildItem -LiteralPath $package -Recurse -File -Force | Where-Object { -not $_.FullName.StartsWith((Join-Path $package 'Task') + '\') } | ForEach-Object { [IO.Path]::GetRelativePath($package,$_.FullName).Replace('\','/') })
if (@(Compare-Object $expectedFiles $actualFiles).Count) { throw 'Unexpected package files.' }
foreach ($file in $manifest.supportingFiles) {
    $path = [IO.Path]::GetFullPath((Join-Path $package $file.path))
    if (-not $path.StartsWith($package + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Supporting path escaped package.' }
    if ((Get-Item -LiteralPath $path).Length -ne $file.bytes -or (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Supporting file mismatch: $($file.path)" }
}
foreach ($line in Get-Content -LiteralPath (Join-Path $package 'PACKAGE-SHA256SUMS')) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw 'Malformed package hash list.' }
    $hash = $Matches[1]; $name = $Matches[2]
    $path = [IO.Path]::GetFullPath((Join-Path $package $name))
    if (-not $path.StartsWith($package + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Checksum path escaped package.' }
    if ((Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $hash) { throw "Package hash mismatch: $name" }
}
$smoke = Get-Content -LiteralPath (Join-Path $package 'receipts/windows-smoke.json') -Raw | ConvertFrom-Json
$archive = Join-Path $package "Task-$($manifest.version)-win-x64-unsigned.zip"
if ($smoke.result -ne 'PASS' -or $smoke.sourceCommit -ne $manifest.sourceRevision -or $smoke.archiveSha256 -ne (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()) { throw 'Smoke receipt belongs to another ZIP.' }
$corporate = @(Get-Content -LiteralPath (Join-Path $package 'receipts/corporate-zip.json') -Raw | ConvertFrom-Json)
if ($corporate.Count -ne 5 -or @($corporate | Where-Object result -NE 'PASS').Count) { throw 'Corporate ZIP acceptance incomplete.' }
$exeHash = ($manifest.files | Where-Object path -EQ 'Task.Desktop.exe').sha256
if ($smoke.exeSha256 -ne $exeHash -or ($corporate | Where-Object check -EQ 'Portable Corporate saved session native startup').detail.executableSha256.ToLowerInvariant() -ne $exeHash) { throw 'Wrong executable tested.' }
$statePath = Join-Path $env:LOCALAPPDATA 'TaskE2ERuntime/task-write-e2e/state.json'
$fixturePassword = if (Test-Path -LiteralPath $statePath) { (Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json).AccountPassword } else { $null }
foreach ($file in $manifest.supportingFiles | Where-Object path -Match '\.(json|md|log|txt)$') {
    $content = Get-Content -LiteralPath (Join-Path $package $file.path) -Raw
    if ($content -match '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----' -or $content -match '(?i)Bearer\s+[a-z0-9_-]{20,}\.' -or ($fixturePassword -and $content.Contains($fixturePassword))) { throw "Secret material in $($file.path)" }
}
& "$PSScriptRoot/../production/deployment/desktop/Test-UnsignedPortableClient.ps1" -PackageDirectory $package | Out-Host
Write-Output 'PASS: ZIP re-read/extraction, payload and document hashes, source/version binding, exact tested EXE, sanitized receipt composition.'
