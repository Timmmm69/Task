# Task production architecture boundary check:
# direct ProjectReference and target framework rules for src projects only.
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
# Api/Worker/BackupAgent -> Application+Infrastructure; Desktop -> Application; Domain -> (none).
$AllowedDependencies = @{
    'Task.Domain'        = @()
    'Task.Application'   = @('Task.Domain')
    'Task.Infrastructure'= @('Task.Application', 'Task.Domain')
    'Task.Api'           = @('Task.Application', 'Task.Infrastructure')
    'Task.Worker'        = @('Task.Application', 'Task.Infrastructure')
    'Task.BackupAgent'   = @('Task.Application', 'Task.Infrastructure')
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

if ($errors.Count -gt 0) {
    Write-Host "[FAIL] Project boundary check found $($errors.Count) violation(s)." -ForegroundColor Red
    exit 1
}
Write-Host '[ OK ] All production project boundaries and target frameworks are valid.'
exit 0