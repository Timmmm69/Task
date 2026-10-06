param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$state = Get-Content (Join-Path $env:LOCALAPPDATA 'TaskE2ERuntime/task-write-e2e/state.json') -Raw | ConvertFrom-Json
$pg15 = Get-Content "$PSScriptRoot/pg15-state.json" -Raw | ConvertFrom-Json
$old16 = $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION
$old15 = $env:TASK_POSTGRES15_TEST_ADMIN_CONNECTION
try {
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = "Host=127.0.0.1;Port=$($state.PostgresPort);Database=postgres;Username=postgres;SSL Mode=Disable"
    $env:TASK_POSTGRES15_TEST_ADMIN_CONNECTION = "Host=127.0.0.1;Port=$($pg15.port);Database=postgres;Username=postgres;SSL Mode=Disable"
    & pwsh -NoProfile -File "$PSScriptRoot/../final_acceptance_stage6/Run-Gate.ps1" -EvidenceDirectory $EvidenceDirectory
    exit $LASTEXITCODE
} finally {
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = $old16
    $env:TASK_POSTGRES15_TEST_ADMIN_CONNECTION = $old15
}
