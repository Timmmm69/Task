param([Parameter(Mandatory)][string]$ResultsDirectory, [Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$baseline = Get-Content -LiteralPath "$PSScriptRoot/Db-Scenarios.json" -Raw
$names = @($baseline | ConvertFrom-Json)
if ($names.Count -ne 54) { throw "Expected 54 baseline scenarios, found $($names.Count)." }
$results = @()
$notRun = 0
foreach ($file in Get-ChildItem $ResultsDirectory -Filter '*.trx' -Recurse) {
    [xml]$trx = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($result in $trx.TestRun.Results.UnitTestResult) {
        $results += $result
        if ([string]$result.Output.StdOut -match 'NOT RUN') { $notRun++ }
    }
}
$proof = @($names | ForEach-Object {
    $name = $_
    $match = @($results | Where-Object testName -EQ $name)
    $pass = $match.Count -eq 1 -and $match[0].outcome -eq 'Passed' -and [string]$match[0].Output.StdOut -notmatch 'NOT RUN'
    [ordered]@{ testName=$name; result=($(if ($pass) {'PASS'} else {'FAIL'})); runs=$match.Count; duration=([string]$match[0].duration) }
})
$summary = [ordered]@{
    result=($(if (@($proof | Where-Object result -NE 'PASS').Count -eq 0 -and $notRun -eq 0) {'PASS'} else {'FAIL'}))
    baselineNotRun=54; newlyExecutedPassed=@($proof | Where-Object result -EQ 'PASS').Count
    runnerPassed=@($results | Where-Object outcome -EQ 'Passed').Count
    runnerSkipped=@($results | Where-Object outcome -EQ 'NotExecuted').Count
    runnerFailed=@($results | Where-Object outcome -EQ 'Failed').Count
    diagnosticNotRun=$notRun
    fixtures=@('Real native PostgreSQL 16','Real native PostgreSQL 15.19')
    note='Both test connection variables supplied. The formerly silent Database.Create early returns cannot run when the configured PostgreSQL connection is present; live assertions executed.'
    scenarios=$proof
}
$summary | ConvertTo-Json -Depth 6 | Set-Content $OutputPath -Encoding utf8
if ($summary.result -ne 'PASS') { throw 'Baseline DB scenarios did not all execute successfully.' }
Write-Output "Formerly NOT RUN: $($summary.newlyExecutedPassed)/54 PASS; total $($summary.runnerPassed) PASS, $($summary.runnerSkipped) SKIP, $($summary.runnerFailed) FAIL."
