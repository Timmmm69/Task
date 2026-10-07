param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$package = Join-Path $repo 'outputs/20261008_location_manager_1.0.0'
$evidence = Join-Path $package 'evidence/accepted'
$quarantine = Join-Path $repo 'archive/quarantine/location_manager_attempts_1.0.0'
$utf8 = [Text.UTF8Encoding]::new($false)
function Write-Json([string]$path, $value) { [IO.File]::WriteAllText($path, (($value | ConvertTo-Json -Depth 12) + "`n"), $utf8) }

$ui = Get-Content -LiteralPath (Join-Path $evidence 'location-manager-ui.json') -Raw | ConvertFrom-Json
if ($ui.result -ne 'PASS' -or -not $ui.physicalFileUnchanged) { throw 'Real UI evidence failed.' }
$counters = @(Get-ChildItem -LiteralPath $evidence -Filter '*.trx' | ForEach-Object {
    [xml]$trx = Get-Content -LiteralPath $_.FullName -Raw
    $c = $trx.TestRun.ResultSummary.Counters
    [pscustomobject]@{ file = $_.Name; total = [int]$c.total; passed = [int]$c.passed; failed = [int]$c.failed; notExecuted = [int]$c.notExecuted }
})
if ($counters.Count -ne 3 -or ($counters | Measure-Object -Property passed -Sum).Sum -ne 1958 -or
    ($counters | Measure-Object -Property failed -Sum).Sum -ne 0 -or ($counters | Measure-Object -Property notExecuted -Sum).Sum -ne 0) { throw 'Final test counters failed.' }
if (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'TaskE2ERuntime/task-locations-e2e')) { throw 'Owned E2E runtime was not cleaned.' }

# Preserve obsolete attempts. Both ends of every move are checked explicitly.
[IO.Directory]::CreateDirectory($quarantine) | Out-Null
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path $package 'evidence'))
$evidencePrefix = $evidenceRoot.TrimEnd('\') + '\'
$quarantinePrefix = [IO.Path]::GetFullPath($quarantine).TrimEnd('\') + '\'
foreach ($old in Get-ChildItem -LiteralPath $evidenceRoot | Where-Object Name -ne 'accepted') {
    $source = [IO.Path]::GetFullPath($old.FullName)
    $destination = [IO.Path]::GetFullPath((Join-Path $quarantine $old.Name))
    if (-not $source.StartsWith($evidencePrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not $destination.StartsWith($quarantinePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected quarantine move.' }
    if (Test-Path -LiteralPath $destination) { throw 'Existing quarantine destination preserved.' }
    Move-Item -LiteralPath $source -Destination $destination
}

$files = @(
    'work/production/docs/QA-03-critical-e2e-matrix.md',
    'work/production/src/Task.Desktop/Administration/DesktopAdministrationApiClient.cs',
    'work/production/src/Task.Desktop/ViewModels/CatalogNodeViewModel.cs',
    'work/production/src/Task.Desktop/ViewModels/WorkHubCatalogViewModel.cs',
    'work/production/src/Task.Desktop/ViewModels/WorkHubViewModel.cs',
    'work/production/src/Task.Desktop/ViewModels/WorkHubLocationsViewModel.cs',
    'work/production/src/Task.Desktop/ViewModels/FileLocationsViewModel.cs',
    'work/production/src/Task.Desktop/Views/WorkHubView.xaml',
    'work/production/src/Task.Desktop/Views/FileLocationsView.xaml',
    'work/production/src/Task.Desktop/Views/FileLocationsView.xaml.cs',
    'work/production/src/Task.Desktop/Work/DesktopWorkApiClient.cs',
    'work/production/src/Task.Desktop/Work/DesktopFileLocationsClient.cs',
    'work/production/tests/Task.Desktop.Tests/Work/FileLocationsViewModelTests.cs',
    'work/production/tests/Task.Desktop.Tests/Work/DesktopFileLocationsClientTests.cs',
    'work/production/tests/Task.Tests/PostgresFileLocationManagerTests.cs',
    'work/production/verification/Test-Qa03Gate.ps1',
    'work/production/verification/Test-TaskWriteE2E.ps1',
    'work/location_manager_1/implementation.md',
    'work/location_manager_1/Update-Dashboard.mjs',
    'work/location_manager_1/Package-And-Validate.ps1'
)
foreach ($file in $files) {
    $source = Join-Path $repo $file; $destination = Join-Path $package "source/$file"
    [IO.Directory]::CreateDirectory((Split-Path $destination)) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { throw "Source copy differs: $file" }
}
Copy-Item -LiteralPath (Join-Path $repo 'work/production/artifacts/location-manager-build.log') -Destination (Join-Path $package 'evidence/release-build.log')
Copy-Item -LiteralPath (Join-Path $repo 'work/location_manager_1/project-boundaries.log') -Destination (Join-Path $package 'evidence/project-boundaries.log')
[IO.File]::WriteAllText((Join-Path $package 'VERSION'), "1.0.0`n", $utf8)
Write-Json (Join-Path $package 'validation.json') @{
    version = '1.0.0'; result = 'PASS'; scope = 'Corporate Location Manager'; testCounters = $counters
    totalPassed = 1958; failed = 0; skipped = 0; realPostgresEnabled = $true
    realWpfHttpsPostgresUi = 'PASS'; physicalFileUnchanged = $true; fixtureCleanup = 'PASS'
    backendSchemaChanged = $false; personalSchemaChanged = $false; upstreamPublished = $false
}
$entries = @(Get-ChildItem -LiteralPath $package -File -Recurse | Where-Object { $_.Name -notin @('manifest.json', 'manifest.json.sha256') } | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{ path = [IO.Path]::GetRelativePath($package, $_.FullName).Replace('\', '/'); size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
Write-Json (Join-Path $package 'manifest.json') @{
    version = '1.0.0'; product = 'Task'; scope = 'Corporate Catalog Location Manager'
    baseCommit = '06443765750ccea544c136f8d22551324afed667'; files = $entries
}
$manifestHash = (Get-FileHash -LiteralPath (Join-Path $package 'manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $package 'manifest.json.sha256'), "$manifestHash  manifest.json`n", $utf8)

Add-Type -AssemblyName System.IO.Compression
$zipPath = Join-Path $repo 'outputs/Task-location-manager-1.0.0.zip'
if (Test-Path -LiteralPath $zipPath) { throw 'Existing ZIP preserved; choose a new package version.' }
[IO.Compression.ZipFile]::CreateFromDirectory($package, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$zipPath.sha256", "$zipHash  Task-location-manager-1.0.0.zip`n", $utf8)
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($entry in $entries) {
        $member = $zip.GetEntry($entry.path)
        if ($null -eq $member -or $member.Length -ne $entry.size) { throw "ZIP member mismatch: $($entry.path)" }
        $stream = $member.Open()
        try { $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
        finally { $stream.Dispose() }
        if ($actual -ne $entry.sha256) { throw "ZIP checksum mismatch: $($entry.path)" }
    }
}
finally { $zip.Dispose() }
Write-Json "$zipPath.validation.json" @{ version = '1.0.0'; result = 'PASS'; sha256 = $zipHash; manifestSha256 = $manifestHash; checkedFiles = $entries.Count; totalTests = 1958; uiResult = $ui.result }
Write-Host "PASS: $($entries.Count) hashed members; 1958 tests; real UI; ZIP round-trip SHA-256 verified."
