param([Parameter(Mandatory)][string]$PostgresConnection)
$ErrorActionPreference = 'Stop'
$production = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../production'))
$evidence = Join-Path $PSScriptRoot 'evidence/recheck'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$previousConnection = $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION
function Invoke-Gate([scriptblock]$Gate) {
    & $Gate
    if ($LASTEXITCODE -ne 0) { throw "Gate failed with exit code $LASTEXITCODE" }
}
Push-Location $production
try {
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = $PostgresConnection
    Invoke-Gate { pwsh -NoProfile -File verification/Test-DependencyAudit.ps1 }
    Invoke-Gate { dotnet format whitespace Task.sln --verify-no-changes --no-restore }
    Invoke-Gate { pwsh -NoProfile -File verification/Test-ProjectBoundaries.ps1 }
    Invoke-Gate { dotnet build Task.sln --configuration Release --no-restore }
    Invoke-Gate { dotnet test Task.sln --configuration Release --no-build --no-restore --logger trx --results-directory $evidence }
    Invoke-Gate { dotnet run --project ../catalog_tree_1/UiProbe/UiProbe.csproj --configuration Release -- $evidence }
    Invoke-Gate { pwsh -NoProfile -File verification/Test-IndependentSecurityReview.ps1 -Configuration Release -SkipRestore -NoBuild -SkipTests -EvidenceDirectory (Join-Path $evidence 'security') }
    Invoke-Gate { pwsh -NoProfile -File verification/Test-ProductionSecretsTls.Contract.ps1 }
}
finally {
    $env:TASK_POSTGRES_TEST_ADMIN_CONNECTION = $previousConnection
    Pop-Location
}
