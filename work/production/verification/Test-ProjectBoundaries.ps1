# Task production architecture boundary check:
# direct ProjectReference and target framework rules for src projects,
# and solution/test-project membership rules for tests projects.
# Exits 0 on success, 1 on any violation. No external modules required.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$srcDir = Join-Path $repoRoot 'work\production\src'
if (-not (Test-Path -LiteralPath $srcDir)) {
    Write-Host "[FAIL] src directory not found: $srcDir"
    exit 1
}

# Allowed architecture (direct references only):
# Application -> Domain; Infrastructure -> Application+Domain;
# Api/Worker/BackupAgent -> Application+Infrastructure; DatabaseMigrator -> Infrastructure;
# Desktop -> Application; Domain -> (none).
$AllowedDependencies = @{
    'Task.Domain'        = @()
    'Task.Application'   = @('Task.Domain')
    'Task.Infrastructure'= @('Task.Application', 'Task.Domain')
    'Task.Api'           = @('Task.Application', 'Task.Infrastructure')
    'Task.Worker'        = @('Task.Application', 'Task.Infrastructure')
    'Task.BackupAgent'   = @('Task.Application', 'Task.Infrastructure')
    'Task.DatabaseMigrator' = @('Task.Infrastructure')
    'Task.Desktop'       = @('Task.Application')
}

$expectedFramework = {
    param([string]$ProjectName)
    if ($ProjectName -eq 'Task.Desktop') { return 'net10.0-windows' }
    return 'net10.0'
}

$errors = New-Object System.Collections.Generic.List[string]
function Fail-Project {
    param([string]$ProjectName, [string]$Message)
    $script:errors.Add("$ProjectName`: $Message")
    Write-Host "[FAIL] $ProjectName`: $Message" -ForegroundColor Red
}

$projects = Get-ChildItem -LiteralPath $srcDir -Filter '*.csproj' -Recurse | Sort-Object FullName
if ($projects.Count -eq 0) {
    Write-Host '[FAIL] No production projects found under src.' -ForegroundColor Red
    exit 1
}
foreach ($project in $projects) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project.Name)
    if (-not $AllowedDependencies.ContainsKey($projectName)) {
        Fail-Project $projectName "not a known production project; add it to the allowed dependency map before referencing other projects"
        continue
    }

    $xml = New-Object System.Xml.XmlDocument
    $xml.Load($project.FullName)

    $actualDeps = New-Object System.Collections.Generic.List[string]
    $refNodes = $xml.SelectNodes('//*[local-name()="ProjectReference"]')
    foreach ($refNode in $refNodes) {
        $include = $refNode.GetAttribute('Include')
        if ([string]::IsNullOrWhiteSpace($include)) {
            Fail-Project $projectName 'ProjectReference without Include attribute'
            continue
        }
        $includePath = if ([System.IO.Path]::IsPathRooted($include)) { $include } else { Join-Path $project.DirectoryName $include }
        $resolved = [System.IO.Path]::GetFullPath($includePath)
        if (-not (Test-Path -LiteralPath $resolved)) {
            Fail-Project $projectName "ProjectReference points to non-existent project: $include"
            continue
        }
        $refName = [System.IO.Path]::GetFileNameWithoutExtension([System.IO.Path]::GetFileName($resolved))
        $actualDeps.Add($refName)
    }

    $expected = @($AllowedDependencies[$projectName])
    $unexpected = @($actualDeps | Where-Object { $_ -notin $expected })
    $missing = @($expected | Where-Object { $_ -notin $actualDeps })
    if ($unexpected.Count -gt 0 -or $missing.Count -gt 0) {
        $actualText = if ($actualDeps.Count -eq 0) { '(none)' } else { $actualDeps -join ', ' }
        $expectedText = if ($expected.Count -eq 0) { '(none)' } else { $expected -join ', ' }
        Fail-Project $projectName "invalid ProjectReference set. Actual: $actualText. Expected: $expectedText."
    }

    $tfmNodes = $xml.SelectNodes('//*[local-name()="TargetFramework"]')
    $actualTfm = $null
    foreach ($tfmNode in $tfmNodes) {
        if (-not [string]::IsNullOrWhiteSpace($tfmNode.InnerText)) { $actualTfm = $tfmNode.InnerText; break }
    }
    $expectedTfm = & $expectedFramework $projectName
    if ($actualTfm -ne $expectedTfm) {
        Fail-Project $projectName "invalid TargetFramework. Actual: $actualTfm. Expected: $expectedTfm."
    }

    Write-Host "[ OK ] $projectName (TargetFramework: $actualTfm, ProjectReferences: $(@($actualDeps)) )"
}

# Test solution boundary check:
# Task.sln must contain exactly the known test projects,
# and each test csproj must be explicitly marked IsTestProject=true.
$slnPath = Join-Path (Join-Path $repoRoot 'work\production') 'Task.sln'
$expectedTestProjects = @('Task.Tests', 'Task.ServiceHosts.Tests', 'Task.Desktop.Tests')
if (-not (Test-Path -LiteralPath $slnPath)) {
    Write-Host "[FAIL] Task.sln not found: $slnPath" -ForegroundColor Red
    exit 1
}

$slnTestProjects = New-Object System.Collections.Generic.List[string]
foreach ($line in Get-Content -LiteralPath $slnPath) {
    if ($line -match '^Project\("{.*}"\)\s*=\s*"([^"]+)",\s*"([^"]+\.csproj)"') {
        $slnProjectName = $matches[1]
        if ($slnProjectName -like '*.Tests') {
            $slnTestProjects.Add($slnProjectName)
        }
    }
}

$unexpectedTestProjects = @($slnTestProjects | Where-Object { $_ -notin $expectedTestProjects })
$missingTestProjects = @($expectedTestProjects | Where-Object { $_ -notin $slnTestProjects })
if ($unexpectedTestProjects.Count -gt 0 -or $missingTestProjects.Count -gt 0) {
    $actualTestText = if ($slnTestProjects.Count -eq 0) { '(none)' } else { $slnTestProjects -join ', ' }
    $expectedTestText = $expectedTestProjects -join ', '
    Fail-Project 'Task.sln' "invalid test project set. Actual: $actualTestText. Expected: $expectedTestText."
}

foreach ($testProjectName in $expectedTestProjects) {
    $testCsproj = Join-Path (Join-Path (Split-Path -Parent $slnPath) "tests\$testProjectName") "$testProjectName.csproj"
    if (-not (Test-Path -LiteralPath $testCsproj)) {
        Fail-Project $testProjectName "csproj not found at expected path: $testCsproj"
        continue
    }
    $testXml = New-Object System.Xml.XmlDocument
    $testXml.Load($testCsproj)
    $isTestProjectNode = $testXml.SelectSingleNode('//*[local-name()="IsTestProject"]')
    $isTestProjectValue = if ($isTestProjectNode -ne $null) { $isTestProjectNode.InnerText.Trim() } else { '' }
    if (-not [string]::Equals($isTestProjectValue, 'true', [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-Project $testProjectName "csproj is not marked as a test project (IsTestProject: '$isTestProjectValue')"
        continue
    }
    Write-Host "[ OK ] $testProjectName (IsTestProject: $isTestProjectValue)"
}

if ($errors.Count -gt 0) {
    Write-Host "[FAIL] Project boundary check found $($errors.Count) violation(s)." -ForegroundColor Red
    exit 1
}
Write-Host '[ OK ] All production and test project boundaries are valid.'
exit 0
