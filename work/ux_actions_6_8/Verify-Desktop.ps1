$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
$project = Join-Path $root 'work/production/tests/Task.Desktop.Tests'
$evidence = Join-Path $PSScriptRoot 'evidence'
$env:TASK_CONTEXT_EVIDENCE = Join-Path $evidence 'screenshots'
& dotnet build "$root/work/production/Task.sln" -c Release --nologo -v quiet *> "$evidence/release-build.log"
if ($LASTEXITCODE -ne 0) { throw 'Release build failed' }
& dotnet test $project -c Release --no-build --nologo --filter 'FullyQualifiedName~TaskContextActionsTests|FullyQualifiedName~ContextCreate' -v minimal --logger 'trx;LogFileName=targeted-final.trx' --results-directory $evidence *> "$evidence/targeted-final.log"
if ($LASTEXITCODE -ne 0) { throw 'Targeted tests failed' }
foreach ($suite in @('CommandPaletteWindowsTests', 'CompletionFeedbackUiTests', 'ExplorerDropViewTests', 'ViewStateUiTests', 'InboxZeroUiTests', 'TaskContextActionsUiTests')) {
    $ErrorActionPreference = 'Continue'
    & dotnet test $project -c Release --no-build --nologo --filter "FullyQualifiedName~$suite" -v minimal --logger "trx;LogFileName=$suite.trx" --results-directory $evidence *> "$evidence/$suite.log"
    $gateResult = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($gateResult -ne 0) { Get-Content "$evidence/$suite.log" | Select-Object -Last 30; throw "Suite failed: $suite" }
    Get-Content "$evidence/$suite.log" | Select-Object -Last 2
}
