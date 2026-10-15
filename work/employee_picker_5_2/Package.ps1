$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$name = '20261015_project_employee_picker_5_2_1.0.0'
$target = Join-Path $repo "outputs/$name"
New-Item -ItemType Directory -Force -Path $target | Out-Null
Set-Content -LiteralPath (Join-Path $target '.gitattributes') -Value '* -text whitespace=cr-at-eol' -Encoding utf8
$scope = @(
    'work/production/src/Task.Desktop/Modes/CorporateApplicationContext.cs',
    'work/production/src/Task.Desktop/ViewModels/ProjectsViewModel.cs',
    'work/production/src/Task.Desktop/ViewModels/ProjectsViewModel.Employees.cs',
    'work/production/src/Task.Desktop/Views/ProjectsView.xaml',
    'work/production/tests/Task.Desktop.Tests/Projects/ProjectsViewModelTests.cs',
    'work/production/tests/Task.Desktop.Tests/Projects/ProjectEmployeePickerTests.cs',
    'work/production/tests/Task.Desktop.Tests/Projects/ProjectPickerWindowsTests.cs',
    'work/production/docs/PROD-04-projects-and-members.md',
    'work/production/docs/PROJECT-EMPLOYEE-PICKER-5.2.md',
    '.project-dashboard/roadmap.json'
)
foreach ($path in $scope) {
    $dest = Join-Path $target "source/$path"
    New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo $path) -Destination $dest
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'evidence') -Destination $target -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'validation-report.md') -Destination $target
Set-Content -LiteralPath (Join-Path $target 'VERSION') -Value '1.0.0' -Encoding utf8
$tests = @{}
foreach ($namePart in @('projects-and-employees', 'desktop-regression', 'server-contract')) {
    [xml]$trx = Get-Content -LiteralPath (Join-Path $target "evidence/$namePart.trx") -Raw
    $counts = $trx.TestRun.ResultSummary.Counters
    if ([int]$counts.failed -ne 0 -or [int]$counts.total -ne [int]$counts.passed) { throw "Incomplete/failed receipt: $namePart" }
    $tests[$namePart] = @{ total = [int]$counts.total; passed = [int]$counts.passed; failed = [int]$counts.failed }
}
$entries = @(Get-ChildItem -LiteralPath $target -File -Recurse | Where-Object Name -NotIn @('manifest.json','SHA256SUMS') | Sort-Object FullName | ForEach-Object {
    @{ path = [IO.Path]::GetRelativePath($target, $_.FullName).Replace('\','/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$manifest = @{ artifact = 'Task project Employee Picker 5.2'; version = '1.0.0'; branch = 'feature/employee-picker';
    source_base = '1cfb6c2'; date = '2026-10-15'; tests = $tests; changed_files = $scope;
    limitations = @('Full role coverage blocked without User.Read; UUID fallback retained', 'No live organization database/network role walkthrough'); files = $entries }
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $target 'manifest.json') -Encoding utf8
$sumLines = @(Get-ChildItem -LiteralPath $target -File -Recurse | Where-Object Name -NE 'SHA256SUMS' | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetRelativePath($target, $_.FullName).Replace('\','/')
})
$sumLines | Set-Content -LiteralPath (Join-Path $target 'SHA256SUMS') -Encoding utf8
foreach ($entry in $entries) {
    if ((Get-FileHash -LiteralPath (Join-Path $target $entry.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw "Hash mismatch: $($entry.path)" }
}
$zip = "$target.zip"
Compress-Archive -Path (Join-Path $target '*') -DestinationPath $zip -Force
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zip.sha256" -Value "$zipHash  $name.zip" -Encoding utf8
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($entry in $entries) {
        $archived = $archive.GetEntry($entry.path)
        if ($null -eq $archived) { throw "Missing zip entry: $($entry.path)" }
        $stream = $archived.Open(); $hasher = [Security.Cryptography.SHA256]::Create()
        try { $hash = [Convert]::ToHexString($hasher.ComputeHash($stream)).ToLowerInvariant() }
        finally { $stream.Dispose(); $hasher.Dispose() }
        if ($hash -ne $entry.sha256) { throw "Zip hash mismatch: $($entry.path)" }
    }
} finally { $archive.Dispose() }
@{ version = '1.0.0'; zip_sha256 = $zipHash; files_verified = $entries.Count; tests = $tests; passed = $true } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$zip.validation.json" -Encoding utf8
Write-Host "Verified $($entries.Count) files; ZIP SHA-256 $zipHash"
