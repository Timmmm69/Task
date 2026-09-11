#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ImageMapPath,

    [string]$EvidenceDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Invoke-ExternalCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Command,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'

    try {
        $output = @(& $Command @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = @($output)
    }
}

function Get-JsonString {
    param(
        [Parameter(Mandatory)]
        $Object,

        [Parameter(Mandatory)]
        [string]$PropertyName
    )

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or $null -eq $property.Value) {
        return ''
    }

    return [string]$property.Value
}

foreach ($command in @('docker', 'trivy')) {
    if ($null -eq (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "$command CLI is unavailable; container vulnerability scanning was not executed."
    }
}

$resolvedImageMapPath = (Resolve-Path -LiteralPath $ImageMapPath).Path

if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path (Split-Path -Parent $resolvedImageMapPath) 'trivy-reports'
}

New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$EvidenceDirectory = (Resolve-Path -LiteralPath $EvidenceDirectory).Path

$trivyVersionResult = Invoke-ExternalCommand -Command 'trivy' -Arguments @('--version')
if ($trivyVersionResult.ExitCode -ne 0) {
    throw "Could not determine Trivy version. Exit code: $($trivyVersionResult.ExitCode)."
}

$trivyVersionText = ($trivyVersionResult.Output | ForEach-Object { "$_" }) -join [Environment]::NewLine
[IO.File]::WriteAllText(
    (Join-Path $EvidenceDirectory 'trivy-version.txt'),
    $trivyVersionText + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false)
)

$versionMatch = [regex]::Match(
    $trivyVersionText,
    '(?im)^Version:\s*v?(?<version>\d+\.\d+\.\d+)'
)

if (-not $versionMatch.Success) {
    throw 'Could not parse Trivy version.'
}

$trivyVersion = [version]$versionMatch.Groups['version'].Value
$minimumTrivyVersion = [version]'0.74.0'

if ($trivyVersion -lt $minimumTrivyVersion) {
    throw "Trivy $trivyVersion is too old. Minimum supported version is $minimumTrivyVersion."
}

$imageMap = Get-Content -Raw -LiteralPath $resolvedImageMapPath | ConvertFrom-Json

if ($null -eq $imageMap) {
    throw 'Image scan map is empty or invalid.'
}

$deployableTargets = @(
    'task-api',
    'task-worker',
    'task-backup-agent',
    'task-database-migrator'
)

$availableTargets = @($imageMap.PSObject.Properties.Name)
$missingTargets = @(
    $deployableTargets |
        Where-Object { $_ -notin $availableTargets }
)

if ($missingTargets.Count -gt 0) {
    throw "Image scan map is missing deployable targets: $($missingTargets -join ', ')."
}

$blockingFindings = @()
$eolTargets = @()
$imageSummaries = @()

foreach ($target in $deployableTargets) {
    $imageReference = [string]$imageMap.$target

    if ($imageReference -notmatch '^sha256:[0-9a-f]{64}$') {
        throw "Image map entry for $target is not an immutable local sha256 reference: '$imageReference'."
    }

    $inspectResult = Invoke-ExternalCommand -Command 'docker' -Arguments @(
        'image',
        'inspect',
        $imageReference,
        '--format={{.Id}}'
    )

    if ($inspectResult.ExitCode -ne 0) {
        $diagnostic = ($inspectResult.Output | Select-Object -Last 20) -join [Environment]::NewLine
        throw "Docker cannot resolve local image $target ($imageReference). $diagnostic"
    }

    $reportPath = Join-Path $EvidenceDirectory "$target.json"
    Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue

    Write-Output "Scanning $target ($imageReference) with Trivy..."

    $scanResult = Invoke-ExternalCommand -Command 'trivy' -Arguments @(
        'image',
        '--image-src', 'docker',
        '--scanners', 'vuln',
        '--pkg-types', 'os,library',
        '--severity', 'HIGH,CRITICAL',
        '--format', 'json',
        '--output', $reportPath,
        '--no-progress',
        '--timeout', '10m',
        '--exit-code', '0',
        '--exit-on-eol', '2',
        $imageReference
    )

    if ($scanResult.ExitCode -notin @(0, 2)) {
        $diagnostic = ($scanResult.Output | Select-Object -Last 40) -join [Environment]::NewLine
        throw "Trivy failed while scanning $target with exit code $($scanResult.ExitCode). $diagnostic"
    }

    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw "Trivy did not produce the expected JSON report for $target."
    }

    $report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
    if ($null -eq $report) {
        throw "Trivy produced an empty or invalid JSON report for $target."
    }

    $resultsProperty = $report.PSObject.Properties['Results']
    if ($null -eq $resultsProperty) {
        throw "Trivy JSON report for $target does not contain Results."
    }

    $vulnerabilities = @()

    foreach ($result in @($resultsProperty.Value)) {
        if ($null -eq $result) {
            continue
        }

        $vulnerabilityProperty = $result.PSObject.Properties['Vulnerabilities']
        if ($null -eq $vulnerabilityProperty -or $null -eq $vulnerabilityProperty.Value) {
            continue
        }

        foreach ($vulnerability in @($vulnerabilityProperty.Value)) {
            if ($null -ne $vulnerability) {
                $vulnerabilities += $vulnerability
            }
        }
    }

    $criticalCount = 0
    $fixableHighCount = 0
    $unfixedHighCount = 0

    foreach ($vulnerability in $vulnerabilities) {
        $severity = (Get-JsonString -Object $vulnerability -PropertyName 'Severity').ToUpperInvariant()
        $vulnerabilityId = Get-JsonString -Object $vulnerability -PropertyName 'VulnerabilityID'
        $packageName = Get-JsonString -Object $vulnerability -PropertyName 'PkgName'
        $installedVersion = Get-JsonString -Object $vulnerability -PropertyName 'InstalledVersion'
        $fixedVersion = Get-JsonString -Object $vulnerability -PropertyName 'FixedVersion'
        $status = (Get-JsonString -Object $vulnerability -PropertyName 'Status').ToLowerInvariant()

        $isFixable = (
            -not [string]::IsNullOrWhiteSpace($fixedVersion) -or
            $status -eq 'fixed'
        )

        $isBlocking = $false

        if ($severity -eq 'CRITICAL') {
            $criticalCount++
            $isBlocking = $true
        }
        elseif ($severity -eq 'HIGH') {
            if ($isFixable) {
                $fixableHighCount++
                $isBlocking = $true
            }
            else {
                $unfixedHighCount++
            }
        }

        if ($isBlocking) {
            $finding = [pscustomobject]@{
                Target = $target
                VulnerabilityId = $vulnerabilityId
                Severity = $severity
                Package = $packageName
                InstalledVersion = $installedVersion
                FixedVersion = $fixedVersion
                Status = $status
            }

            $blockingFindings += $finding

            $fixedDisplay = if ([string]::IsNullOrWhiteSpace($fixedVersion)) {
                '<none>'
            }
            else {
                $fixedVersion
            }

            Write-Host (
                "[BLOCK] {0}: {1} {2} package={3} installed={4} fixed={5}" -f
                $target,
                $vulnerabilityId,
                $severity,
                $packageName,
                $installedVersion,
                $fixedDisplay
            )
        }
    }

    $isEol = $scanResult.ExitCode -eq 2
    if ($isEol) {
        $eolTargets += $target
        Write-Host "[BLOCK] $target uses an end-of-life operating system."
    }

    if ($unfixedHighCount -gt 0) {
        Write-Warning "$target contains $unfixedHighCount unfixed HIGH vulnerabilities. They remain visible in the JSON report but do not block this policy."
    }

    $imageSummaries += [pscustomobject]@{
        Target = $target
        Image = $imageReference
        Critical = $criticalCount
        FixableHigh = $fixableHighCount
        UnfixedHigh = $unfixedHighCount
        EndOfLifeOperatingSystem = $isEol
        Report = [IO.Path]::GetFileName($reportPath)
    }

    Write-Output (
        "{0}: critical={1}, fixable-high={2}, unfixed-high={3}, eol={4}" -f
        $target,
        $criticalCount,
        $fixableHighCount,
        $unfixedHighCount,
        $isEol
    )
}

$summary = [ordered]@{
    SchemaVersion = 1
    Scanner = 'Trivy'
    ScannerVersion = $trivyVersion.ToString()
    ScannedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Policy = [ordered]@{
        Critical = 'block-all'
        High = 'block-when-fixable'
        UnfixedHigh = 'report-only'
        EndOfLifeOperatingSystem = 'block'
        ScannerFailure = 'block'
    }
    Images = $imageSummaries
    EndOfLifeTargets = $eolTargets
    BlockingFindings = $blockingFindings
}

[IO.File]::WriteAllText(
    (Join-Path $EvidenceDirectory 'summary.json'),
    (($summary | ConvertTo-Json -Depth 8) + [Environment]::NewLine),
    [Text.UTF8Encoding]::new($false)
)

if ($eolTargets.Count -gt 0 -or $blockingFindings.Count -gt 0) {
    throw (
        "Container vulnerability gate failed: blocking-findings={0}, eol-targets={1}. Evidence: {2}" -f
        $blockingFindings.Count,
        $eolTargets.Count,
        $EvidenceDirectory
    )
}

Write-Output "Container vulnerability gate passed. Evidence: $EvidenceDirectory"
