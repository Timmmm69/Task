[CmdletBinding()]
param(
    [switch]$SkipTestRun
)

$ErrorActionPreference = 'Stop'
$production = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$evidence = Join-Path $production 'evidence/api01'

if (-not $SkipTestRun) {
    & (Join-Path $PSScriptRoot 'Test-IdentityApi.ps1') -Filter ''
    if ($LASTEXITCODE -ne 0) { throw 'The full identity and desktop regression gate failed.' }
}

$latestByAssembly = @{}
foreach ($path in Get-ChildItem -LiteralPath $evidence -Filter '*.trx' | Sort-Object LastWriteTime) {
    [xml]$document = Get-Content -LiteralPath $path.FullName -Raw
    $counters = $document.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
    $definition = $document.SelectSingleNode("//*[local-name()='TestDefinitions']/*[local-name()='UnitTest']")
    if ($null -eq $counters -or $null -eq $definition -or [int]$counters.total -lt 100) { continue }
    $assembly = [IO.Path]::GetFileNameWithoutExtension([string]$definition.storage)
    $latestByAssembly[$assembly] = [pscustomobject]@{ Path = $path; Document = $document; Counters = $counters }
}

$requiredAssemblies = @('Task.Desktop.Tests', 'Task.ServiceHosts.Tests', 'Task.Tests')
foreach ($assembly in $requiredAssemblies) {
    if (-not $latestByAssembly.ContainsKey($assembly)) { throw "Missing a full TRX run for $assembly." }
    $counters = $latestByAssembly[$assembly].Counters
    if ([int]$counters.failed -ne 0 -or [int]$counters.notExecuted -ne 0 -or [int]$counters.total -ne [int]$counters.passed) {
        throw "The latest $assembly run is not completely green."
    }
}

$requiredScenarios = @(
    'DesktopServerConnectionTests.Probe_CertificateFailure_ReturnsTlsFailure',
    'AuthWorkflowViewModelTests.ServerSetup_ValidProbe_NormalizesAndSaves_ThenShowsLogin',
    'AuthWorkflowViewModelTests.Login_SuccessWithConfirmedSession_TransitionsToReady',
    'AuthWorkflowViewModelTests.Login_MustChangePassword_CannotReachReady',
    'AuthWorkflowViewModelTests.PasswordChange_SuccessRequiresSecondSessionConfirmation',
    'AuthWorkflowViewModelTests.StartupRestore_ConfirmedSession_TransitionsToReady',
    'AuthWorkflowViewModelTests.StartupRestore_RevokedSession_ClearsVaultAndReturnsToLogin',
    'AuthWorkflowViewModelTests.Logout_FromReady_AlwaysClearsLocalSessionAndShowsLogin',
    'DesktopCredentialVaultTests.EncryptedFile_DoesNotContainPlaintextTokenOrIdentity',
    'SessionServiceTests.Refresh_RefreshTokenReuse_SignsOut',
    'AuthEndpointsTests.Login_Returns200_WithExpectedTokenShape',
    'AuthSessionEndpointsTests.ChangePassword_Success_Returns204',
    'AuthSessionEndpointsTests.Logout_Returns204_AndRevokesCurrentSession',
    'PostgresIdentityLifecycleTests'
)

$results = foreach ($entry in $latestByAssembly.Values) {
    $entry.Document.SelectNodes("//*[local-name()='UnitTestResult']")
}
foreach ($scenario in $requiredScenarios) {
    $matches = @($results | Where-Object { [string]$_.testName -like "*$scenario*" })
    if ($matches.Count -eq 0) { throw "Required DESK-01 scenario is absent from the current evidence: $scenario" }
    if (@($matches | Where-Object { [string]$_.outcome -ne 'Passed' }).Count -ne 0) {
        throw "Required DESK-01 scenario did not pass: $scenario"
    }
}

$total = ($requiredAssemblies | ForEach-Object { [int]$latestByAssembly[$_].Counters.passed } | Measure-Object -Sum).Sum
Write-Host "DESK-01 gate passed: $total tests, $($requiredScenarios.Count) required auth scenarios, zero failed or skipped."
