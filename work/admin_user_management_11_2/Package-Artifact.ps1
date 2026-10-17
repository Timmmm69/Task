$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
$version = '11.2.0'
$package = Join-Path $repo "outputs/20261017_admin_user_management_$version"
$evidence = Join-Path $repo 'work/verification/admin-user-11-2'
New-Item -ItemType Directory -Force $package, (Join-Path $package 'evidence') | Out-Null
$scope = @(
    'work/production/src/Task.Desktop/ViewModels/AdminUserEditor.cs',
    'work/production/src/Task.Desktop/ViewModels/AdministrationUsers.cs',
    'work/production/src/Task.Desktop/ViewModels/AdministrationViewModel.cs',
    'work/production/src/Task.Desktop/Views/AdministrationView.xaml',
    'work/production/tests/Task.Desktop.Tests/Administration/AdministrationUserEditorTests.cs',
    'work/production/tests/Task.Desktop.Tests/Administration/AdministrationUserWindowsTests.cs',
    'work/production/tests/Task.Desktop.Tests/Administration/AdministrationViewModelTests.cs',
    '.project-dashboard/roadmap.json',
    'work/admin_user_management_11_2/Package-Artifact.ps1'
)
foreach ($relative in $scope) {
    $destination = Join-Path $package "source/$relative"
    New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($destination)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo $relative) -Destination $destination -Force
}
$receipts = @('administration-release.trx', 'desktop-release.trx', 'desktop-release-final.trx', 'users-contract.trx', 'inherited-native-isolated.trx')
$counts = [ordered]@{}
foreach ($receipt in $receipts) {
    [xml]$trx = Get-Content -LiteralPath (Join-Path $evidence $receipt) -Raw
    $counter = $trx.TestRun.ResultSummary.Counters
    $knownFailure = $receipt -eq 'desktop-release-final.trx' -and [int]$counter.failed -eq 1 -and
        @($trx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'Failed' -and $_.testName -like '*TaskContextActionsUiTests.NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow' }).Count -eq 1
    if ([int]$counter.total -lt 1 -or [int]$counter.notExecuted -ne 0 -or ([int]$counter.failed -ne 0 -and !$knownFailure)) { throw "Unexpected failed or empty test receipt: $receipt" }
    $counts[$receipt] = @{ total = [int]$counter.total; passed = [int]$counter.passed; failed = [int]$counter.failed; skipped = [int]$counter.notExecuted }
    Copy-Item -LiteralPath (Join-Path $evidence $receipt) -Destination (Join-Path $package 'evidence') -Force
}
Copy-Item -LiteralPath (Join-Path $evidence 'build-release.log') -Destination (Join-Path $package 'evidence') -Force
Copy-Item -LiteralPath (Join-Path $evidence 'screenshots') -Destination (Join-Path $package 'evidence') -Recurse -Force
Set-Content -LiteralPath (Join-Path $package 'VERSION') -Value $version -Encoding utf8
$scope | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'source-scope.json') -Encoding utf8
@{
    version = $version; date = '2026-10-17'; branch = 'feature/admin-user-management'
    baseline = '4c737b2'; status = 'SCOPED_PASS_WITH_INHERITED_NATIVE_WPF_FAILURE'
    readyForStage11_3Development = $true; fullDesktopGateGreen = $false
    inheritedFailure = @{ test = 'TaskContextActionsUiTests.NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow'; location = 'TaskContextActionsUiTests.cs:62'; isolatedResult = 'PASS'; baselineEvidence = 'outputs/20261017_admin_user_management_11.1.0/validation-report.json'; firstFullRun = '1366/1366 PASS'; finalFullRun = '1368 PASS / 1 FAIL'; fixedInThisScope = $false }
    tests = $counts; build = @{ command = 'dotnet build work/production/Task.sln -c Release --no-restore'; exitCode = 0 }
    permissions = @('User.Read', 'User.Create', 'User.Update')
    nativeWpf = @{ passed = $true; transport = 'Test double implementing existing typed client interface'; viewports = @('1120x900', '800x600'); checks = @('real Window', 'UIA Invoke/Value', 'binding errors', 'fixed Save/Cancel footer', 'scrolling', 'offline/read-only', 'create/edit/cancel'); manualLiveCorporateServer = 'NOT RUN' }
    limitations = @('DUPLICATE_RESOURCE does not identify login vs workEmail; UI identifies both candidate fields', 'No permitted named department directory; creation omits departmentId and PATCH preserves it', 'Creation returns pending_activation, no password; credential provisioning is a separate dependency', 'Version conflicts preserve draft and require explicit cancel/reopen before another update', 'No live production account mutations, manual Narrator or physical DPI matrix acceptance')
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $package 'validation-report.json') -Encoding utf8
$diff = & git diff HEAD --binary -- $scope
if ($LASTEXITCODE -ne 0) { throw 'Cannot produce source diff' }
[IO.File]::WriteAllText((Join-Path $package 'source.patch'), ($diff -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$entries = @(Get-ChildItem -LiteralPath $package -File -Recurse | Where-Object { $_.Name -notin @('manifest.json', 'checks.sha256') } | Sort-Object FullName | ForEach-Object {
    @{ path = [IO.Path]::GetRelativePath($package, $_.FullName).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
@{ version = $version; branch = 'feature/admin-user-management'; baseline = '4c737b2'; files = $entries } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $package 'manifest.json') -Encoding utf8
@($entries | ForEach-Object { "$($_.sha256)  $($_.path)" }) + @("$((Get-FileHash -LiteralPath (Join-Path $package 'manifest.json')).Hash.ToLowerInvariant())  manifest.json") | Set-Content -LiteralPath (Join-Path $package 'checks.sha256') -Encoding utf8
foreach ($entry in $entries) {
    if ((Get-FileHash -LiteralPath (Join-Path $package $entry.path)).Hash.ToLowerInvariant() -ne $entry.sha256) { throw "Hash mismatch: $($entry.path)" }
}
$zipPath = "$package.zip"
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zipPath -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($entry in $entries) {
        $z = $zip.GetEntry($entry.path)
        if ($null -eq $z) { throw "Missing ZIP entry: $($entry.path)" }
        $stream = $z.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() } finally { $stream.Dispose() }
        if ($hash -ne $entry.sha256) { throw "ZIP hash mismatch: $($entry.path)" }
    }
} finally { $zip.Dispose() }
"$((Get-FileHash -LiteralPath $zipPath).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath "$zipPath.sha256" -Encoding utf8
Write-Output "Package validated: $zipPath; $($entries.Count) payload files."
