# Task Stage 1 gap overrides verification:
# validates gap_overrides_wave_{a,b,c}.csv exact columns, resolution statuses and
# Traceability mode semantics:
#   - single-operation : resolved, API operationId method/path consistent with
#     outputs/stage_2_3/openapi/openapi.yaml, Related == API operationId,
#     non-empty Source evidence/Resolution rationale/Permission/Server handler planned.
#   - module-wide       : resolved, Related holds 2+ distinct sorted operationIds,
#     API operationId/method/path empty, non-empty evidence/rationale/Permission/Handler.
#   - unresolved        : empty API fields and empty Related, non-empty evidence/rationale.
# Checks duplicate (Matrix source row, API operationId) pairs and expected totals.
# Exits 0 on success, 1 on any violation. No external modules required.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$traceDir = Join-Path $repoRoot 'work\production_stage_1_baseline\traceability'
$openapiFile = Join-Path $repoRoot 'outputs\stage_2_3\openapi\openapi.yaml'

# New baseline totals (mode-aware).
$expectedResolvedSingle = 1074
$expectedResolvedModuleWide = 92
$expectedUnresolved = 80

$ExpectedColumns = @(
    'Matrix source row', 'Requirement', 'Type', 'Module', 'Requirement title',
    'Resolution status', 'Traceability mode', 'Related OpenAPI operationIds',
    'API operationId', 'API method', 'API path', 'Permission',
    'Server handler planned', 'Screen Stage 3.5', 'FLOW Stage 3.5', 'Test type',
    'Source evidence', 'Resolution rationale'
)
$Modes = @('single-operation', 'module-wide', 'unresolved')
$EmptyForUnresolved = @('API operationId', 'API method', 'API path', 'Permission', 'Server handler planned')
$EmptyForModuleWide = @('API operationId', 'API method', 'API path')
$RequiredForUnresolved = @('Source evidence', 'Resolution rationale')
$RequiredForSingle = @('Source evidence', 'Resolution rationale', 'Permission', 'Server handler planned')
$RequiredForModuleWide = @('Source evidence', 'Resolution rationale', 'Permission', 'Server handler planned')

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
$waveCounts = @{}
$allRows = @()
$totalSingle = 0
$totalModuleWide = 0
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

    $single = 0
    $moduleWide = 0
    $unresolved = 0
    foreach ($row in $rows) {
        $loc = "$csvPath (matrix row $($row.'Matrix source row'))"
        $status = $row.'Resolution status'
        if ($status -ne 'resolved' -and $status -ne 'unresolved') {
            Fail "${loc}: invalid Resolution status '$status'"
            continue
        }
        $mode = $row.'Traceability mode'
        if ($mode -notin $Modes) {
            Fail "${loc}: invalid Traceability mode '$mode'"
            continue
        }
        if ($status -eq 'resolved' -and $mode -eq 'unresolved') {
            Fail "${loc}: status resolved but mode unresolved"
            continue
        }
        if ($status -eq 'unresolved' -and $mode -ne 'unresolved') {
            Fail "${loc}: status unresolved but mode '$mode'"
            continue
        }

        if ($mode -eq 'single-operation') {
            $single++
            $opId = $row.'API operationId'
            $related = $row.'Related OpenAPI operationIds'
            if ([string]::IsNullOrWhiteSpace($opId)) {
                Fail "${loc}: single-operation row missing API operationId"
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
            if ($related.Trim() -ne $opId) {
                Fail "${loc}: single-operation Related must equal API operationId, found '$related'"
            }
            foreach ($col in $RequiredForSingle) {
                if ([string]::IsNullOrWhiteSpace($row.$col)) {
                    Fail "${loc}: single-operation row must have non-empty '$col'"
                }
            }
            $allRows += $row
        }
        elseif ($mode -eq 'module-wide') {
            $moduleWide++
            $related = $row.'Related OpenAPI operationIds'
            if ([string]::IsNullOrWhiteSpace($related)) {
                Fail "${loc}: module-wide row missing Related OpenAPI operationIds"
            }
            else {
                $ops = @(@($related -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() }))
                if ($ops.Count -lt 1) {
                    Fail "${loc}: module-wide Related must contain at least one operationId"
                }
                if (@($ops | Select-Object -Unique).Count -ne $ops.Count) {
                    Fail "${loc}: module-wide Related contains duplicate operationIds"
                }
                $sorted = @(@($ops | Sort-Object))
                if (($sorted -join ';') -ne ($ops -join ';')) {
                    Fail "${loc}: module-wide Related operationIds must be sorted ascending"
                }
                foreach ($opId in $ops) {
                    if (-not $openApi.ContainsKey($opId)) {
                        Fail "${loc}: module-wide operationId '$opId' not found in openapi.yaml"
                    }
                }
            }
            foreach ($col in $EmptyForModuleWide) {
                if (-not [string]::IsNullOrWhiteSpace($row.$col)) {
                    Fail "${loc}: module-wide row must have empty '$col', found '$($row.$col)'"
                }
            }
            foreach ($col in $RequiredForModuleWide) {
                if ([string]::IsNullOrWhiteSpace($row.$col)) {
                    Fail "${loc}: module-wide row must have non-empty '$col'"
                }
            }
            $allRows += $row
        }
        else {
            $unresolved++
            foreach ($col in $EmptyForUnresolved) {
                if (-not [string]::IsNullOrWhiteSpace($row.$col)) {
                    Fail "${loc}: unresolved row must have empty '$col', found '$($row.$col)'"
                }
            }
            if (-not [string]::IsNullOrWhiteSpace($row.'Related OpenAPI operationIds')) {
                Fail "${loc}: unresolved row must have empty Related OpenAPI operationIds"
            }
            foreach ($col in $RequiredForUnresolved) {
                if ([string]::IsNullOrWhiteSpace($row.$col)) {
                    Fail "${loc}: unresolved row must have non-empty '$col'"
                }
            }
            $allRows += $row
        }
    }

    $waveCounts[$wave] = @{ single = $single; moduleWide = $moduleWide; unresolved = $unresolved }
    $totalSingle += $single
    $totalModuleWide += $moduleWide
    $totalUnresolved += $unresolved
}

foreach ($wave in $waves) {
    if (-not $waveCounts.ContainsKey($wave)) { continue }
    $c = $waveCounts[$wave]
    Write-Host "[ OK ] wave_$wave`: single=$($c.single) moduleWide=$($c.moduleWide) unresolved=$($c.unresolved) total=$($c.single + $c.moduleWide + $c.unresolved)"
}
Write-Host "[ OK ] totals: single=$totalSingle moduleWide=$totalModuleWide unresolved=$totalUnresolved rows=$($totalSingle + $totalModuleWide + $totalUnresolved)"

$seenPairs = New-Object System.Collections.Generic.HashSet[string]
foreach ($row in $allRows) {
    $opId = $row.'API operationId'
    if ([string]::IsNullOrWhiteSpace($opId)) { continue }
    $key = "$($row.'Matrix source row')`n$opId"
    if (-not $seenPairs.Add($key)) {
        Fail "$($row.'Matrix source row'): duplicate (Matrix source row, API operationId) pair"
    }
}

if ($totalSingle -ne $expectedResolvedSingle -or $totalModuleWide -ne $expectedResolvedModuleWide -or $totalUnresolved -ne $expectedUnresolved) {
    Fail "totals mismatch: actual single=$totalSingle moduleWide=$totalModuleWide unresolved=$totalUnresolved; expected single=$expectedResolvedSingle moduleWide=$expectedResolvedModuleWide unresolved=$expectedUnresolved"
}

if ($errors.Count -gt 0) {
    Write-Host "[FAIL] Gap override verification found $($errors.Count) violation(s)." -ForegroundColor Red
    exit 1
}
Write-Host '[ OK ] All gap override checks passed.'
exit 0