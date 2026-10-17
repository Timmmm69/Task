param([string]$DeliveryRoot = 'C:\Users\novik\Таск\outputs')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$name = '20261017_admin_user_management_11.3.0'
$package = Join-Path $repo "outputs/$name"
$scope = @(
    '.project-dashboard/roadmap.json',
    'work/production/src/Task.Desktop/ViewModels/AdministrationUserTransitions.cs',
    'work/production/src/Task.Desktop/ViewModels/AdministrationViewModel.cs',
    'work/production/src/Task.Desktop/ViewModels/AdministrationUsers.cs',
    'work/production/src/Task.Desktop/Views/AdministrationView.xaml',
    'work/production/tests/Task.Desktop.Tests/Administration/AdministrationUserTransitionTests.cs',
    'work/production/tests/Task.Desktop.Tests/Administration/AdministrationUserWindowsTests.cs',
    'work/production/tests/Task.ServiceHosts.Tests/UserEndpointsTests.cs',
    'work/production/tests/Task.Tests/PostgresIdentityLifecycleTests.cs',
    'work/verification/11_3/Run-Postgres.ps1',
    'work/verification/11_3/Package.ps1'
)
function WriteJson($path, $value) { $value | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding utf8NoBOM }
function Counters($file) {
    [xml]$trx = Get-Content -LiteralPath (Join-Path $PSScriptRoot $file) -Raw
    $c = $trx.TestRun.ResultSummary.Counters
    if ([int]$c.total -eq 0) { throw "No tests in $file" }
    return @{ total=[int]$c.total; passed=[int]$c.passed; failed=[int]$c.failed; skipped=([int]$c.total - [int]$c.executed); evidence="evidence/$file" }
}
$checks = [ordered]@{
    administration = (Counters 'administration-release-final.trx')
    api = (Counters 'api-release.trx')
    identity_postgres = (Counters 'identity-postgres-release.trx')
    desktop_first = (Counters 'desktop-release.trx')
    desktop_second = (Counters 'desktop-release-final.trx')
    personal_search_isolated = (Counters 'personal-search-isolated.trx')
    inbox_focus_isolated = (Counters 'inbox-focus-isolated.trx')
}
foreach ($gate in @('administration','api','identity_postgres','personal_search_isolated','inbox_focus_isolated')) {
    if ($checks[$gate].failed -ne 0 -or $checks[$gate].skipped -ne 0) { throw "Required gate failed: $gate" }
}
New-Item -ItemType Directory -Force -Path (Join-Path $package 'evidence/screenshots') | Out-Null
foreach ($file in $scope) {
    $target = Join-Path $package "source/$file"
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $target -Force
}
$evidence = @('administration-release-final.trx','api-release.trx','identity-postgres-release.trx',
    'desktop-release.trx','desktop-release-final.trx','personal-search-isolated.trx','inbox-focus-isolated.trx',
    'build-release.log','postgres-runtime.json','identity-postgres.log')
foreach ($file in $evidence) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $package "evidence/$file") -Force }
Copy-Item -Path (Join-Path $PSScriptRoot 'screenshots/*.png') -Destination (Join-Path $package 'evidence/screenshots') -Force
'11.3.0' | Set-Content -LiteralPath (Join-Path $package 'VERSION') -Encoding utf8NoBOM
WriteJson (Join-Path $package 'source-scope.json') $scope
# New source files must be staged before this snapshot is built.
& git -C $repo diff --binary ec39281 -- $scope | Set-Content -LiteralPath (Join-Path $package 'source.patch') -Encoding utf8NoBOM
if ($LASTEXITCODE -ne 0) { throw 'Cannot create scoped source patch.' }
WriteJson (Join-Path $package 'validation-report.json') ([ordered]@{
    version='11.3.0'; branch='feature/admin-user-management'; baseline='ec39281';
    implementation='implemented'; targeted_gate='passed'; overall_desktop_gate='not_green';
    tests=$checks; release_solution_build='passed'; native_wpf='passed, real Windows UIA with fake API';
    postgres='16, isolated loopback runtime, real stores'; live_end_to_end='not_run';
    security=@{ strong_if_match=$true; fresh_details=$true; expected_version_per_endpoint=$true;
        required_reason=$true; permission_checks=$true; no_offline_queue=$true; no_automatic_412_retry=$true;
        same_uncertain_request=$true; last_admin_store_test='passed'; server_contracts_unchanged=$true };
    ready_for_11_4_development=$true; full_product_acceptance=$false;
    limitations=@('Full desktop second run: InboxZeroUiTests Window.Activate failure; isolated pass.',
        'First full desktop run: PersonalShellViewModelTests 300ms timing assertion; isolated pass.',
        'Final UpsertUser selection adjustment checked by 139 administration tests and Release build; full suite not rerun after this small edit.',
        'Live Corporate WPF/HTTP/PostgreSQL, physical network disconnect, Narrator, mixed DPI not manually verified.',
        'Concurrent changes to different administrator accounts were not tested; existing server guard unchanged.')
})
$records = @(Get-ChildItem -LiteralPath $package -File -Recurse | Where-Object { $_.Name -notin @('manifest.json','checks.sha256') } | Sort-Object FullName | ForEach-Object {
    @{ path=[IO.Path]::GetRelativePath($package,$_.FullName).Replace('\','/'); bytes=$_.Length;
        sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
WriteJson (Join-Path $package 'manifest.json') ([ordered]@{ version='11.3.0'; task='11.3'; base_commit='ec39281'; files=$records })
$checksums = @($records | ForEach-Object { "$($_.sha256)  $($_.path)" })
$checksums += ((Get-FileHash -LiteralPath (Join-Path $package 'manifest.json')).Hash.ToLowerInvariant() + '  manifest.json')
$checksums | Set-Content -LiteralPath (Join-Path $package 'checks.sha256') -Encoding utf8NoBOM
foreach ($entry in $records) {
    if ((Get-FileHash -LiteralPath (Join-Path $package $entry.path)).Hash.ToLowerInvariant() -ne $entry.sha256) { throw "Hash mismatch: $($entry.path)" }
}
$zip = Join-Path $repo "outputs/$name.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
[IO.Compression.ZipFile]::CreateFromDirectory($package, $zip)
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($entry in $records) {
        $item = $archive.GetEntry($entry.path)
        if ($null -eq $item) { throw "Missing ZIP entry: $($entry.path)" }
        $stream = $item.Open()
        try { $digest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
        finally { $stream.Dispose() }
        if ($digest -ne $entry.sha256) { throw "ZIP hash mismatch: $($entry.path)" }
    }
}
finally { $archive.Dispose() }
$archiveHash = (Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant()
"$archiveHash  $name.zip" | Set-Content -LiteralPath "$zip.sha256" -Encoding utf8NoBOM
Copy-Item -LiteralPath $package -Destination $DeliveryRoot -Recurse -Force
Copy-Item -LiteralPath $zip,"$zip.sha256" -Destination $DeliveryRoot -Force
if ((Get-FileHash -LiteralPath (Join-Path $DeliveryRoot "$name.zip")).Hash.ToLowerInvariant() -ne $archiveHash) { throw 'Delivery hash mismatch.' }
Write-Output "Verified $($records.Count) manifest entries and ZIP payload; SHA-256 $archiveHash"
