param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force $EvidenceDirectory | Out-Null
$production = (Resolve-Path "$PSScriptRoot/../production").Path
$steps = @(
    @{Name='restore'; File='dotnet'; Arguments=@('restore','Task.sln')},
    @{Name='dependency-audit'; File='pwsh'; Arguments=@('-NoProfile','-File','./verification/Test-DependencyAudit.ps1')},
    @{Name='whitespace'; File='dotnet'; Arguments=@('format','whitespace','Task.sln','--verify-no-changes','--no-restore')},
    @{Name='boundaries'; File='pwsh'; Arguments=@('-NoProfile','-File','./verification/Test-ProjectBoundaries.ps1')},
    @{Name='build'; File='dotnet'; Arguments=@('build','Task.sln','--configuration','Release','--no-restore')},
    @{Name='tests'; File='dotnet'; Arguments=@('test','Task.sln','--configuration','Release','--no-build','--no-restore','--logger','trx','--results-directory',"$EvidenceDirectory/tests",'--blame-hang','--blame-hang-timeout','60s','--blame-hang-dump-type','none')},
    @{Name='secrets-tls'; File='pwsh'; Arguments=@('-NoProfile','-File','./verification/Test-ProductionSecretsTls.Contract.ps1')},
    @{Name='locked-restore'; File='dotnet'; Arguments=@('restore','Task.sln','--locked-mode')},
    @{Name='security-review'; File='pwsh'; Arguments=@('-NoProfile','-File','./verification/Test-IndependentSecurityReview.ps1','-Configuration','Release','-SkipRestore','-NoBuild','-SkipTests','-EvidenceDirectory',"$EvidenceDirectory/security-review")}
)
$results = @()
Push-Location $production
try {
    foreach ($step in $steps) {
        $started = [DateTimeOffset]::UtcNow
        $watch = [Diagnostics.Stopwatch]::StartNew()
        & $step.File @($step.Arguments) *> "$EvidenceDirectory/$($step.Name).log"
        $code = $LASTEXITCODE
        $results += @{name=$step.Name;exitCode=$code;startedAtUtc=$started.ToString('O');seconds=$watch.Elapsed.TotalSeconds;command="$($step.File) $($step.Arguments -join ' ')"}
        $results | ConvertTo-Json -Depth 5 | Set-Content "$EvidenceDirectory/checks.json" -Encoding utf8
        Write-Output "$($step.Name): exit $code"
        if ($code -ne 0) { exit $code }
    }
} finally { Pop-Location }
