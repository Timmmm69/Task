[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Solution = 'Task.sln'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$productionRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionPath = Join-Path $productionRoot $Solution

if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
    throw "Dependency audit solution was not found: $solutionPath"
}

if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet CLI is unavailable; the dependency audit was not executed.'
}

# NU1900: vulnerability data could not be retrieved.
# NU1903: high severity vulnerability.
# NU1904: critical severity vulnerability.
# NU1905: audit source does not provide vulnerability data.
#
# Low (NU1901) and moderate (NU1902) findings remain visible warnings.
# High/critical findings and an unavailable/unusable audit fail closed.
# MSBuild command-line property values encode semicolons as %3B.
$blockingAuditWarnings = 'NU1900%3BNU1903%3BNU1904%3BNU1905'

$restoreArguments = @(
    'restore'
    $solutionPath
    '-p:NuGetAudit=true'
    '-p:NuGetAuditMode=all'
    '-p:NuGetAuditLevel=low'
    "-p:WarningsAsErrors=$blockingAuditWarnings"
    '--verbosity'
    'minimal'
)

Write-Output 'Running NuGet dependency audit: direct + transitive dependencies, low+ severity.'

& dotnet @restoreArguments
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    throw "NuGet dependency audit failed with exit code $exitCode. High/critical vulnerabilities or unavailable audit data block the gate."
}

Write-Output 'NuGet dependency audit passed: no blocking high/critical vulnerability or audit-source failure was reported.'
