# Task Stage 1 gap overrides verification:
# validates gap_overrides_wave_{a,b,c}.csv exact columns, resolution statuses,
# unresolved rows (empty API fields, non-empty evidence/rationale), resolved rows
# (operationId/method/path consistent with outputs/stage_2_3/openapi/openapi.yaml,
# non-empty Source evidence/Resolution rationale/Permission/Server handler planned),
# duplicate (Matrix source row, API operationId) pairs and expected totals.
# Exits 0 on success, 1 on any violation. No external modules required.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$traceDir = Join-Path $repoRoot 'work\production_stage_1_baseline\traceability'
$openapiFile = Join-Path $repoRoot 'outputs\stage_2_3\openapi\openapi.yaml'
$expectedResolved = 1001
$expectedUnresolved = 245

$ExpectedColumns = @(
    'Matrix source row', 'Requirement', 'Type', 'Module', 'Requirement title',
    'Resolution status', 'API operationId', 'API method', 'API path', 'Permission',
    'Server handler planned', 'Screen Stage 3.5', 'FLOW Stage 3.5', 'Test type',
    'Source evidence', 'Resolution rationale'
)
$EmptyForUnresolved = @('API operationId', 'API method', 'API path', 'Permission', 'Server handler planned')
$RequiredForUnresolved = @('Source evidence', 'Resolution rationale')
$RequiredForResolved = @('Source evidence', 'Resolution rationale', 'Permission', 'Server handler planned')

$errors = New-Object System.Collections.Generic.List[string]
function Fail {
    param([string]$Message)
    $script:errors.Add($Message)
    Write-Host "[FAIL] $Message" -ForegroundColor Red
}

function Get-OpenApiOperations {
    param([string]$YamlPath)
    $map = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::Ordinal)
    if (-not (Test-Path -LiteralPath $YamlPath)) {
        Fail "OpenAPI file not found: $YamlPath"
        return $map
    }
    $lines = Get-Content -LiteralPath $YamlPath -Encoding UTF8
    $inPaths = $false
    $currentPath = $null
    $currentMethod = $null
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if (-not $inPaths) {
            if ($line -match '^paths:\s*$') { $inPaths = $true }
            continue
        }
        if ($line -match '^components:\s*$') { break }
        if ($line.Trim() -eq '') { continue }
        $indent = 0
        while ($indent -lt $line.Length -and $line[$indent] -eq ' ') { $indent++ }
        $rest = $line.Substring($indent)
        if ($indent -eq 2 -and $rest -match '^/.+:\s*$') {
            $currentPath = $rest -replace ':\s*$', ''
            $currentMethod = $null
        }
        elseif ($indent -eq 4 -and $rest -match '^(get|post|put|patch|delete|options|head|trace):\s*$') {
            $currentMethod = $matches[1].ToUpperInvariant()
        }
        elseif ($indent -eq 6 -and $rest -match '^operationId:\s*(.+?)\s*$') {
            if ($null -ne $currentMethod) {
                $map[$matches[1]] = [pscustomobject]@{
                    OperationId = $matches[1]
                    Method = $currentMethod
                    Path = $currentPath
                }
            }
        }
    }
    return $map
}

$openApi = Get-OpenApiOperations -YamlPath $openapiFile
Write-Host "[ OK ] parsed $($openApi.Count) operations from openapi.yaml"

$waves = @('a', 'b', 'c')
$waveResolved = @{}
$waveUnresolved = @{}
$allRows = @()
$totalResolved = 0
$totalUnresolved = 0

foreach ($wave in $waves) {
    $csvPath = Join-Path $traceDir "gap_overrides_wave_$wave.csv"
    if (-not (Test-Path -LiteralPath $csvPath)) {
        Fail "override CSV not found: $csvPath"
        continue
    }
    $rows = @(Import-Csv -LiteralPath $csvPath)
    if ($rows.Count -eq 0) {
        Fail "$csvPath line 1: file has headers but no data rows"
        continue
    }

    $actualColumns = @($rows[0].PSObject.Properties.Name)
    $setMissing = @($ExpectedColumns | Where-Object { $_ -notin $actualColumns })
    $setExtra = @($actualColumns | Where-Object { $_ -notin $ExpectedColumns })
    if ($setMissing.Count -gt 0 -or $setExtra.Count -gt 0) {
        Fail "$csvPath line 1: column set mismatch. missing: '$($setMissing -join ', ')' extra: '$($setExtra -join ', ')'"
        continue
    }
    if (($actualColumns -join ',') -ne ($ExpectedColumns -join ',')) {
        Fail "$csvPath line 1: column order mismatch. actual: '$($actualColumns -join ',')' expected: '$($ExpectedColumns -join ',')'"
        continue
    }

    $resolved = 0
    $unresolved = 0
    foreach ($row in $rows) {
        $loc = "$csvPath (matrix row $($row.'Matrix source row'))"
        $status = $row.'Resolution status'
        if ($status -ne 'resolved' -and $status -ne 'unresolved') {
            Fail "${loc}: invalid Resolution status '$status'"
            continue
        }
        if ($status -eq 'unresolved') {
            $unresolved++
            foreach ($col in $EmptyForUnresolved) {
                if (-not [string]::IsNullOrWhiteSpace($row.$col)) {
                    Fail "${loc}: unresolved row must have empty '$col', found '$($row.$col)'"
                }
            }
            foreach ($col in $RequiredForUnresolved) {
                if ([string]::IsNullOrWhiteSpace($row.$col)) {
                    Fail "${loc}: unresolved row must have non-empty '$col'"
                }
            }
            $allRows += $row
            continue
        }
        $resolved++
        $opId = $row.'API operationId'
        if ([string]::IsNullOrWhiteSpace($opId)) {
            Fail "${loc}: resolved row missing API operationId"
        }
        elseif (-not $openApi.ContainsKey($opId)) {
            Fail "${loc}: operationId '$opId' not found in openapi.yaml"
        }
        else {
            $op = $openApi[$opId]
            if ($row.'API method' -ne $op.Method) {
                Fail "${loc}: method '$($row.'API method')' does not match openapi.yaml '$($op.Method)' for operationId '$opId'"
            }
            if ($row.'API path' -ne $op.Path) {
                Fail "${loc}: path '$($row.'API path')' does not match openapi.yaml '$($op.Path)' for operationId '$opId'"
            }
        }
        foreach ($col in $RequiredForResolved) {
            if ([string]::IsNullOrWhiteSpace($row.$col)) {
                Fail "${loc}: resolved row must have non-empty '$col'"
            }
        }
        $allRows += $row
    }

    $waveResolved[$wave] = $resolved
    $waveUnresolved[$wave] = $unresolved
    $totalResolved += $resolved
    $totalUnresolved += $unresolved
}

foreach ($wave in $waves) {
    if (-not $waveResolved.ContainsKey($wave)) { continue }
    Write-Host "[ OK ] wave_$wave`: resolved=$($waveResolved[$wave]) unresolved=$($waveUnresolved[$wave]) total=$($waveResolved[$wave] + $waveUnresolved[$wave])"
}

$seenPairs = New-Object System.Collections.Generic.HashSet[string]
foreach ($row in $allRows) {
    $key = "$($row.'Matrix source row')`n$($row.'API operationId')"
    if (-not $seenPairs.Add($key)) {
        Fail "$($row.'Matrix source row'): duplicate (Matrix source row, API operationId) pair"
    }
}

if ($totalResolved -ne $expectedResolved -or $totalUnresolved -ne $expectedUnresolved) {
    Fail "totals mismatch: actual resolved=$totalResolved unresolved=$totalUnresolved; expected resolved=$expectedResolved unresolved=$expectedUnresolved"
}

Write-Host "[ OK ] totals: resolved=$totalResolved unresolved=$totalUnresolved rows=$($totalResolved + $totalUnresolved)"

if ($errors.Count -gt 0) {
    Write-Host "[FAIL] Gap override verification found $($errors.Count) violation(s)." -ForegroundColor Red
    exit 1
}
Write-Host '[ OK ] All gap override checks passed.'
exit 0
