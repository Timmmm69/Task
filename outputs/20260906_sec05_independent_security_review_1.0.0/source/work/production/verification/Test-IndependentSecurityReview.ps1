[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipRestore,
    [switch]$NoBuild,
    [string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
$productionRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repositoryRoot = (Resolve-Path (Join-Path $productionRoot '..\..')).Path
$solutionPath = Join-Path $productionRoot 'Task.sln'
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $productionRoot 'evidence\sec05'
}

New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$EvidenceDirectory = (Resolve-Path -LiteralPath $EvidenceDirectory).Path

function Assert-ReviewCondition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw "SEC-05 security review failed: $Message"
    }
}

function Invoke-LoggedDotNet {
    param([string[]]$Arguments, [string]$LogName)
    $logPath = Join-Path $EvidenceDirectory $LogName
    $output = @(& dotnet @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $logText = (($output | ForEach-Object { "$_" }) -join [Environment]::NewLine) + [Environment]::NewLine
    [IO.File]::WriteAllText($logPath, $logText, [Text.UTF8Encoding]::new($false))
    if ($exitCode -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $exitCode. See $logPath"
    }
    return $output
}

$checks = [ordered]@{}

if (-not $SkipRestore) {
    Invoke-LoggedDotNet @(
        'restore', $solutionPath, '--locked-mode', '--verbosity', 'minimal'
    ) 'locked-restore.log' | Out-Null

    $linuxProjects = @(
        'src/Task.Api/Task.Api.csproj',
        'src/Task.Worker/Task.Worker.csproj',
        'src/Task.BackupAgent/Task.BackupAgent.csproj',
        'src/Task.DatabaseMigrator/Task.DatabaseMigrator.csproj',
        'verification/container-task-store/Task.ContainerValidation.csproj'
    )
    $linuxRestoreLog = Join-Path $EvidenceDirectory 'linux-x64-locked-restore.log'
    Remove-Item -LiteralPath $linuxRestoreLog -Force -ErrorAction SilentlyContinue
    foreach ($relativeProject in $linuxProjects) {
        $project = Join-Path $productionRoot $relativeProject
        $output = @(& dotnet restore $project --runtime linux-x64 --locked-mode --verbosity minimal 2>&1)
        $exitCode = $LASTEXITCODE
        $logText = (($output | ForEach-Object { "$_" }) -join [Environment]::NewLine) + [Environment]::NewLine
        [IO.File]::AppendAllText($linuxRestoreLog, $logText, [Text.UTF8Encoding]::new($false))
        if ($exitCode -ne 0) {
            throw "linux-x64 locked restore failed for $relativeProject. See $linuxRestoreLog"
        }
    }
    $checks.locked_restore = $true
    $checks.linux_x64_locked_restore = $true
}

$dependencyOutput = Invoke-LoggedDotNet @(
    'list', $solutionPath, 'package', '--vulnerable', '--include-transitive',
    '--format', 'json', '--no-restore'
) 'dependency-vulnerabilities.json'
$dependencyInventory = ($dependencyOutput -join [Environment]::NewLine) | ConvertFrom-Json
$vulnerablePackages = @()
foreach ($project in @($dependencyInventory.projects)) {
    foreach ($framework in @($project.frameworks | Where-Object { $null -ne $_ })) {
        foreach ($propertyName in @('topLevelPackages', 'transitivePackages')) {
            $property = $framework.PSObject.Properties[$propertyName]
            if ($null -ne $property) {
                $vulnerablePackages += @($property.Value)
            }
        }
    }
}
Assert-ReviewCondition ($vulnerablePackages.Count -eq 0) 'NuGet reported vulnerable direct or transitive packages.'
$checks.dependency_advisories = $true

$tracked = @(& git -C $repositoryRoot ls-files --cached --others --exclude-standard -- 'work/production') |
    Where-Object { $_ -notmatch '(^|/)evidence/' }
Assert-ReviewCondition ($LASTEXITCODE -eq 0) 'Unable to enumerate the production source set.'
$trackedFiles = @($tracked | ForEach-Object { Join-Path $repositoryRoot $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
$scannableFiles = @($trackedFiles | Where-Object {
    $_ -match '\.(?:cs|csproj|json|md|props|ps1|py|sh|targets|txt|xml|ya?ml)$' -or
    [IO.Path]::GetFileName($_) -eq 'Dockerfile'
})

$privateKeyMatches = @(Select-String -LiteralPath $scannableFiles -Pattern '-----BEGIN (?:EC |RSA )?PRIVATE KEY-----')
Assert-ReviewCondition ($privateKeyMatches.Count -eq 0) 'Tracked production source contains private key material.'

$configurationFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $productionRoot 'src') -Recurse -File |
        Where-Object { $_.Name -like 'appsettings*.json' }
    Get-ChildItem -LiteralPath (Join-Path $productionRoot 'deployment') -Recurse -File |
        Where-Object { $_.Extension -in '.yaml', '.yml' -or $_.Name -eq 'Dockerfile' }
)
foreach ($file in $configurationFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    $credentialLines = $content -split "`r?`n" |
        Where-Object { $_ -match '(?i)(?:password|pepper|signingkey|refreshtoken)\s*[=:]' }
    foreach ($line in $credentialLines) {
        Assert-ReviewCondition (
            $line -match '\$\{' -or $line -match '(?i)file:' -or $line -match '<[^>]+>'
        ) "Embedded credential-like value in $($file.FullName)."
    }
}
$checks.secret_scan = $true

$sourceFiles = @($trackedFiles | Where-Object { $_ -match '[\\/]src[\\/].*\.cs$' })
$tlsBypasses = @(Select-String -LiteralPath $sourceFiles -Pattern 'DangerousAcceptAnyServerCertificateValidator|ServerCertificateCustomValidationCallback')
Assert-ReviewCondition ($tlsBypasses.Count -eq 0) 'A source-level TLS certificate validation bypass was found.'

$desktopSecurityFiles = @($sourceFiles | Where-Object { $_ -match '[\\/]Task\.Desktop[\\/]Security[\\/]' })
$plaintextDesktopEndpoints = @(Select-String -LiteralPath $desktopSecurityFiles -Pattern '"http://')
Assert-ReviewCondition ($plaintextDesktopEndpoints.Count -eq 0) 'Desktop security code contains a plaintext HTTP endpoint.'

$corsEnablers = @(Select-String -LiteralPath $sourceFiles -Pattern '\.(?:AddCors|UseCors)\(')
Assert-ReviewCondition ($corsEnablers.Count -eq 0) 'CORS was enabled without an approved browser-client decision.'

$anonymousEndpoints = @(Select-String -LiteralPath $sourceFiles -Pattern '\.AllowAnonymous\(\)')
Assert-ReviewCondition ($anonymousEndpoints.Count -eq 4) 'Anonymous endpoint inventory changed; expected login, refresh, live and ready only.'
$checks.static_application_boundary = $true

$authEndpoints = Get-Content -LiteralPath (Join-Path $productionRoot 'src/Task.Api/Auth/AuthEndpoints.cs') -Raw
$abuseProtector = Get-Content -LiteralPath (Join-Path $productionRoot 'src/Task.Api/Auth/LoginAbuseProtector.cs') -Raw
Assert-ReviewCondition ($authEndpoints -match 'MaxAuthRequestBodyBytes\s*=\s*8\s*\*\s*1024') 'Auth body limit is missing.'
Assert-ReviewCondition ($authEndpoints -match 'REQUEST_TOO_LARGE') 'Auth oversize requests do not use the canonical error.'
Assert-ReviewCondition ($abuseProtector -match 'DefaultMaxConcurrentPasswordChecks\s*=\s*2') 'Password-check concurrency is not bounded.'
Assert-ReviewCondition ($abuseProtector -match '_accounts' -and $abuseProtector -match '_addresses' -and $abuseProtector -match '_global') 'Layered login throttling is incomplete.'
$checks.login_abuse_controls = $true

$compose = Get-Content -LiteralPath (Join-Path $productionRoot 'deployment/containers/compose.validation.yaml') -Raw
$postgresMatch = [regex]::Match($compose, '(?ms)^  postgres:\r?\n(?<body>.*?)(?=^  [a-zA-Z0-9_-]+:)')
Assert-ReviewCondition $postgresMatch.Success 'PostgreSQL service block was not found.'
Assert-ReviewCondition ($postgresMatch.Groups['body'].Value -notmatch '(?m)^\s+ports:') 'PostgreSQL is published to the host.'
Assert-ReviewCondition ($compose -match '"127\.0\.0\.1:\$\{TASK_API_HOST_PORT:\?TASK_API_HOST_PORT is required\}:8080"') 'Validation API is not loopback-bound.'
Assert-ReviewCondition ($compose -match '(?ms)^  database:\r?\n\s+internal:\s+true') 'Database network is not internal.'
Assert-ReviewCondition (([regex]::Matches($compose, '(?m)^\s+read_only:\s+true$')).Count -ge 5) 'Runtime services are not read-only.'
Assert-ReviewCondition (([regex]::Matches($compose, '(?m)^\s+cap_drop:\s+\["ALL"\]$')).Count -ge 5) 'Runtime capabilities are not fully dropped.'
Assert-ReviewCondition (([regex]::Matches($compose, 'no-new-privileges:true')).Count -ge 5) 'Runtime no-new-privileges is incomplete.'
$checks.network_and_container_boundary = $true

$testArguments = @(
    'test', $solutionPath,
    '--configuration', $Configuration,
    '--no-restore',
    '--verbosity', 'minimal',
    '--logger', 'trx;LogFilePrefix=sec05',
    '--results-directory', $EvidenceDirectory,
    '--blame-hang',
    '--blame-hang-timeout', '90s',
    '--blame-hang-dump-type', 'none'
)
if ($NoBuild) {
    $testArguments += '--no-build'
}
Invoke-LoggedDotNet $testArguments 'tests.log' | Out-Null
$checks.complete_solution_tests = $true

$checksPath = Join-Path $EvidenceDirectory 'checks.json'
[IO.File]::WriteAllText(
    $checksPath,
    (($checks | ConvertTo-Json -Depth 4) + [Environment]::NewLine),
    [Text.UTF8Encoding]::new($false))

Write-Output "SEC-05 independent security review gate passed. Evidence: $EvidenceDirectory"
