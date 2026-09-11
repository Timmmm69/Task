[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$PackageDirectory
)

$ErrorActionPreference = 'Stop'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Contains {
    param([string]$Path, [string[]]$Needles)
    $content = Get-Content -LiteralPath $Path -Raw
    foreach ($needle in $Needles) {
        Assert-True ($content.Contains($needle, [StringComparison]::Ordinal)) "Missing '$needle' in $Path"
    }
}

$docs = Join-Path $RepositoryRoot 'work\production\docs'
$userGuide = Join-Path $docs 'HAND-04-user-guide.md'
$adminGuide = Join-Path $docs 'HAND-04-administrator-guide.md'
$demoGuide = Join-Path $docs 'HAND-04-demo-walkthrough.md'

foreach ($path in @($userGuide, $adminGuide, $demoGuide)) {
    Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "Required guide is missing: $path"
}

Assert-Contains $userGuide @('https://', 'не помещает их в offline-очередь', 'Не отключайте проверку TLS', 'конфликт')
Assert-Contains $adminGuide @('POST /users', 'POST /auth/admin-reset-password', 'PUT /users/{id}/roles', 'последнего активного администратора', 'графической консоли')
Assert-Contains $demoGuide @('qa03-deterministic-v1', 'Test-Qa03Gate.ps1', 'system_observer', 'cleanup: PASS')

$qaManifestPath = Join-Path $RepositoryRoot 'outputs\20260910_qa03_critical_e2e_1.0.0\manifest.json'
Assert-True (Test-Path -LiteralPath $qaManifestPath -PathType Leaf) "QA-03 manifest is missing: $qaManifestPath"
$qaManifest = Get-Content -LiteralPath $qaManifestPath -Raw | ConvertFrom-Json
Assert-True ($qaManifest.validation.real_wpf_https_postgresql -eq 'PASS') 'QA-03 WPF/HTTPS/PostgreSQL evidence is not PASS.'
Assert-True ($qaManifest.validation.conflict_recovery -eq 'PASS') 'QA-03 conflict recovery evidence is not PASS.'
Assert-True ($qaManifest.validation.offline_shell_and_reconnect -eq 'PASS') 'QA-03 offline/reconnect evidence is not PASS.'
Assert-True ($qaManifest.validation.cleanup -eq 'PASS') 'QA-03 cleanup evidence is not PASS.'

if ($PackageDirectory) {
    $resolvedPackage = (Resolve-Path -LiteralPath $PackageDirectory).Path
    $sumsPath = Join-Path $resolvedPackage 'SHA256SUMS'
    Assert-True (Test-Path -LiteralPath $sumsPath -PathType Leaf) "Package checksum file is missing: $sumsPath"
    foreach ($name in @('HAND-04-user-guide.md', 'HAND-04-administrator-guide.md', 'HAND-04-demo-walkthrough.md')) {
        $sourceHash = (Get-FileHash -LiteralPath (Join-Path $docs $name) -Algorithm SHA256).Hash
        $packagePath = Join-Path $resolvedPackage $name
        Assert-True (Test-Path -LiteralPath $packagePath -PathType Leaf) "Packaged guide is missing: $packagePath"
        Assert-True ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash -eq $sourceHash) "Packaged guide differs from its source: $name"
    }
    foreach ($line in Get-Content -LiteralPath $sumsPath) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split '\s{2,}', 2
        Assert-True ($parts.Count -eq 2) "Invalid checksum record: $line"
        $file = Join-Path $resolvedPackage $parts[1]
        Assert-True (Test-Path -LiteralPath $file -PathType Leaf) "Checksum target is missing: $file"
        Assert-True ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -eq $parts[0].ToLowerInvariant()) "Checksum mismatch: $file"
    }
}

[pscustomobject]@{
    result = 'PASS'
    guides = 3
    qa03SeedProfile = $qaManifest.validation.seed_profile
    qa03Reference = '20260910_qa03_critical_e2e_1.0.0'
} | ConvertTo-Json -Depth 3
