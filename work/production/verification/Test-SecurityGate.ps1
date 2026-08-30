[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$productionRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionPath = Join-Path $productionRoot 'Task.sln'

$configurationFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $productionRoot 'src') -Recurse -File |
        Where-Object { $_.Name -like 'appsettings*.json' }
    Get-ChildItem -LiteralPath (Join-Path $productionRoot 'deployment') -Recurse -File |
        Where-Object { $_.Extension -in '.yaml', '.yml' -or $_.Name -eq 'Dockerfile' }
)

foreach ($file in $configurationFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($content -match '-----BEGIN (?:EC |RSA )?PRIVATE KEY-----') {
        throw "SEC-01 private-key scan failed for tracked runtime configuration: $($file.Name)"
    }

    $credentialLines = $content -split "`r?`n" |
        Where-Object { $_ -match '(?i)(?:password|pepper|signingkey|refreshtoken)\s*[=:]' }
    foreach ($line in $credentialLines) {
        if ($line -notmatch '\$\{' -and $line -notmatch '(?i)file:' -and $line -notmatch '<[^>]+>') {
            throw "SEC-01 embedded-credential scan failed for tracked runtime configuration: $($file.Name)"
        }
    }
}

& dotnet test $solutionPath --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    throw "SEC-01 test gate failed with exit code $LASTEXITCODE."
}

Write-Output 'SEC-01 security gate passed: tracked runtime configuration contains no embedded key/credential patterns and the complete solution test suite is green.'
