#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+(-[a-z0-9.-]+)?$')] [string]$Version,
    [Parameter(Mandatory)] [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$output = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)
$outputsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'outputs')) + [IO.Path]::DirectorySeparatorChar
if (-not $output.StartsWith($outputsRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Release output must be below outputs/.' }
if (Test-Path -LiteralPath $output) { throw 'Output already exists; release evidence must never be overwritten.' }

function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments)
    $result = @(& $Command @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "$Command failed ($LASTEXITCODE): $($result -join "`n")" }
    return ($result -join "`n").Trim()
}
function Write-Json {
    param([string]$Path, $Value)
    $Value | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}
function Expand-ReleaseTar {
    param([string]$Archive, [string]$Destination)
    # Windows bsdtar cannot open Unicode archive arguments reliably. Use the
    # Unicode-aware process working directory and ASCII relative paths instead.
    $parent = Split-Path -Parent $Archive
    Push-Location -LiteralPath $parent
    try {
        Invoke-Checked tar @('-xf', (Split-Path -Leaf $Archive), '-C', [IO.Path]::GetRelativePath($parent, $Destination)) | Out-Null
    }
    finally { Pop-Location }
}
function Remove-TemporaryDirectory {
    param([string]$Path)
    $allowed = [IO.Path]::GetFullPath((Join-Path $repoRoot 'work/tmp')) + [IO.Path]::DirectorySeparatorChar
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe cleanup path: $full" }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}

foreach ($command in 'git', 'docker', 'node', 'tar', 'pwsh') { Get-Command $command -ErrorAction Stop | Out-Null }
if (Invoke-Checked git @('-C', $repoRoot, 'status', '--porcelain', '--untracked-files=all')) {
    throw 'Commit or preserve pending changes before making a release; a clean source commit is required.'
}
$revision = Invoke-Checked git @('-C', $repoRoot, 'rev-parse', 'HEAD')
$epoch = [long](Invoke-Checked git @('-C', $repoRoot, 'show', '-s', '--format=%ct', 'HEAD'))
$tree = Invoke-Checked git @('-C', $repoRoot, 'rev-parse', 'HEAD:work/production')
if ((Invoke-Checked docker @('version', '--format', '{{.Server.Os}}/{{.Server.Arch}}')) -ne 'linux/amd64') {
    throw 'A running linux/amd64 Docker engine is required.'
}
$buildkit = 'moby/buildkit:v0.23.2@sha256:e39f6119f134b4811af19fd5c20f495a6a264a85c1b6920daf569b23009dd42c'
$runId = 'taskrelease' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$temp = Join-Path $repoRoot "work/tmp/$runId"
$context = Join-Path $temp 'source'
$builders = @()
$failure = $null
$records = [ordered]@{}
$targets = @('task-api', 'task-worker', 'task-backup-agent', 'task-database-migrator', 'task-container-validation')
$previousMetadata = $env:BUILDX_METADATA_PROVENANCE
try {
    New-Item -ItemType Directory -Path $output, $context, (Join-Path $output 'images'), (Join-Path $output 'evidence') | Out-Null
    # Keep large, locally retained binary archives out of Git; hashes remain tracked.
    "/images/`n/source.tar`n" | Set-Content (Join-Path $output '.gitignore') -Encoding utf8NoBOM
    '* -text' | Set-Content (Join-Path $output '.gitattributes') -Encoding utf8NoBOM
    $sourceArchive = Join-Path $output 'source.tar'
    Invoke-Checked git @('-C', $repoRoot, 'archive', '--format=tar', "--output=$sourceArchive", 'HEAD:work/production') | Out-Null
    Expand-ReleaseTar $sourceArchive $context
    $source = [ordered]@{
        version = $Version; revision = $revision; productionTree = $tree; sourceDateEpoch = $epoch
        sourceArchiveSha256 = (Get-FileHash $sourceArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        platform = 'linux/amd64'; buildkitImage = $buildkit
        dockerVersion = Invoke-Checked docker @('version', '--format', '{{json .}}') | ConvertFrom-Json
        buildxVersion = Invoke-Checked docker @('buildx', 'version')
        nodeVersion = Invoke-Checked node @('--version')
    }
    Write-Json (Join-Path $output 'source.json') $source
    $env:BUILDX_METADATA_PROVENANCE = 'max'
    foreach ($pass in 1, 2) {
        $builder = "$runId-$pass"
        $builders += $builder
        Invoke-Checked docker @('buildx', 'create', '--name', $builder, '--driver', 'docker-container', '--driver-opt', "image=$buildkit") | Out-Null
        Invoke-Checked docker @('buildx', 'inspect', $builder, '--bootstrap') | Set-Content (Join-Path $output "evidence/builder-$pass.txt")
        foreach ($target in $targets) {
            Write-Host "Building $target, independent builder $pass/2..."
            $archive = Join-Path $output "images/$target-$pass.oci.tar"
            $metadata = Join-Path $output "evidence/$target-$pass.buildx.json"
            $arguments = @('buildx', 'build', '--builder', $builder, '--platform', 'linux/amd64',
                '--target', $target, '--build-arg', "VERSION=$Version", '--build-arg', "GIT_SHA=$revision",
                '--build-arg', "SOURCE_DATE_EPOCH=$epoch", '--provenance=mode=max', '--metadata-file', $metadata,
                '--output', "type=oci,dest=$archive,rewrite-timestamp=true", '--tag', "task-release/$($target):$Version",
                '--file', (Join-Path $context 'deployment/containers/Dockerfile'), $context)
            Invoke-Checked docker $arguments | Set-Content (Join-Path $output "evidence/$target-$pass.build.txt")
            $layout = Join-Path $temp "$target-$pass"
            New-Item -ItemType Directory -Path $layout | Out-Null
            Expand-ReleaseTar $archive $layout
            $expectedPath = Join-Path $temp 'expected.json'
            Write-Json $expectedPath @{ target = $target; version = $Version; revision = $revision; epoch = $epoch }
            $recordPath = Join-Path $output "evidence/$target-$pass.oci.json"
            Invoke-Checked node @((Join-Path $context 'deployment/containers/verify-oci.mjs'), $layout, $expectedPath, $recordPath) | Write-Host
            $record = Get-Content -Raw $recordPath | ConvertFrom-Json
            if ($pass -eq 1) { $records[$target] = $record }
            elseif ($records[$target].imageDigest -ne $record.imageDigest) { throw "Independent builds differ: $target" }
            Remove-TemporaryDirectory $layout
        }
        # Each pass uses its own empty BuildKit state and fresh NuGet cache.
        Invoke-Checked docker @('buildx', 'rm', $builder) | Out-Null
        $builders = @($builders | Where-Object { $_ -ne $builder })
    }
    $imageMap = [ordered]@{}
    foreach ($target in $targets) {
        Invoke-Checked docker @('load', '--input', (Join-Path $output "images/$target-1.oci.tar")) | Out-Null
        $imageMap[$target] = $records[$target].configDigest
        # A load may preserve an index, but the runtime gate uses the immutable config/image ID.
        Invoke-Checked docker @('image', 'inspect', $imageMap[$target]) | Out-Null
    }
    $mapPath = Join-Path $output 'image-map.json'
    Write-Json $mapPath $imageMap
    Invoke-Checked pwsh @('-NoProfile', '-File', (Join-Path $repoRoot 'work/production/verification/Test-ContainerPackaging.ps1'),
        '-Version', $Version, '-GitSha', $revision, '-ImageMapPath', $mapPath) |
        Set-Content (Join-Path $output 'evidence/runtime-gate.txt')
}
catch { $failure = $_.Exception.Message }
finally {
    $env:BUILDX_METADATA_PROVENANCE = $previousMetadata
    foreach ($builder in $builders) {
        try { Invoke-Checked docker @('buildx', 'rm', $builder) | Out-Null }
        catch { $failure = "$failure`nBuilder cleanup failed: $($_.Exception.Message)" }
    }
    try { Remove-TemporaryDirectory $temp }
    catch { $failure = "$failure`nTemporary source cleanup failed: $($_.Exception.Message)" }
}
if (Test-Path -LiteralPath $output) {
    $status = if ($failure) { 'FAILED' } else { 'PASS' }
    Write-Json (Join-Path $output 'release.json') @{
        schemaVersion = 1; version = $Version; revision = $revision; status = $status
        independentBuilds = 2; reproducibility = 'linux/amd64 image manifest, config and layers; attestation timestamps may differ'
        images = @($records.Values | ForEach-Object { @{ target = $_.target; imageDigest = $_.imageDigest; configDigest = $_.configDigest } })
        failure = $failure
    }
    @(
        "# Container release $Version — $status", '', "Source commit: $revision", '',
        'Two isolated pinned BuildKit builders; locked NuGet restore; SOURCE_DATE_EPOCH and rewritten layer timestamps.',
        'Every OCI blob hash, runtime image label and provenance subject/build parameters is verified before comparison.',
        'Runtime gate consumes the exported images by immutable config IDs. PASS requires the PostgreSQL/hardening gate and cleanup.',
        'Attestations are unsigned build evidence; no registry publication, signing or production deployment is claimed.',
        '', "Failure: $failure"
    ) | Set-Content (Join-Path $output 'validation-report.md') -Encoding utf8NoBOM
    $hashes = @(Get-ChildItem -LiteralPath $output -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($output, $_.FullName).Replace('\', '/')
        "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $relative"
    })
    $hashes | Set-Content (Join-Path $output 'SHA256SUMS') -Encoding utf8NoBOM
}
if ($failure) { throw $failure }
Write-Host "Container release $Version PASSED: $output"
