[CmdletBinding()]
param(
    [string]$EvidenceDirectory = 'work/tmp/backup-verification',
    [string]$OutputDirectory = 'outputs/20260903_task_backup_restore_0.6.0'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$evidence = Join-Path $root $EvidenceDirectory
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$allowed = [IO.Path]::GetFullPath((Join-Path $root 'outputs')) + [IO.Path]::DirectorySeparatorChar
if (!$output.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { throw 'Package must be in outputs/.' }
if (Test-Path -LiteralPath $output) { throw 'Refusing to overwrite an existing result package.' }
$integration = Get-Content (Join-Path $evidence 'integration.json') -Raw | ConvertFrom-Json
$recovery = Get-Content (Join-Path $evidence 'offhost-only-restore.json') -Raw | ConvertFrom-Json
$cleanup = Get-Content (Join-Path $evidence 'cleanup.json') -Raw | ConvertFrom-Json
$run = Get-Content (Join-Path $evidence 'run.json') -Raw | ConvertFrom-Json
if ($run.status -ne 'succeeded' -or !$run.sources.Count) { throw 'The current verification run did not succeed.' }
foreach ($source in $run.sources) {
    if ((Get-FileHash -LiteralPath (Join-Path $root $source.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $source.sha256) {
        throw "Evidence is stale for source: $($source.path)"
    }
}
if ($integration.checks.Count -lt 12 -or $recovery.status -ne 'succeeded' -or
    $cleanup.containersRemaining -ne 0 -or $cleanup.volumesRemaining -ne 0 -or !$cleanup.testSecretsRemoved) {
    throw 'Required backup/recovery/cleanup evidence is incomplete.'
}
New-Item -ItemType Directory -Path $output | Out-Null
[IO.File]::WriteAllText((Join-Path $output '.gitattributes'), "* -text`n")
Copy-Item -LiteralPath $evidence -Destination (Join-Path $output 'evidence') -Recurse
$paths = @(
    'work/production/src/Task.BackupAgent',
    'work/production/deployment/backup',
    'work/production/tests/Task.ServiceHosts.Tests/BackupScheduleTests.cs',
    'work/production/tests/Task.ServiceHosts.Tests/BackupRestoreAgentLifecycleTests.cs',
    'work/production/verification/Test-BackupRestore.ps1',
    'work/production/verification/Test-ContainerPackaging.ps1',
    'work/production/verification/backup-integration.py',
    'work/production/docs/backup-restore-runbook.md',
    'work/production/docs/task-container-deployment-foundation.md'
)
foreach ($relative in $paths) {
    $source = Join-Path $root $relative
    $files = if (Test-Path -LiteralPath $source -PathType Container) {
        Get-ChildItem -LiteralPath $source -File -Force # Never package bin/obj or generated secrets.
    } else { Get-Item -LiteralPath $source }
    foreach ($file in $files) {
        $fileRelative = [IO.Path]::GetRelativePath($root,$file.FullName)
        $destination = Join-Path $output "source/$fileRelative"
        New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
}
[IO.File]::WriteAllText((Join-Path $output 'VERSION'), "0.6.0`n")
Copy-Item -LiteralPath (Join-Path $root 'work/tmp/backup-validation-report.md') -Destination (Join-Path $output 'validation-report.md')
Compress-Archive -LiteralPath (Join-Path $output 'source') -DestinationPath (Join-Path $output 'task-backup-restore-0.6.0.zip')
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $output 'task-backup-restore-0.6.0.zip'))
try {
    foreach ($entry in $archive.Entries) {
        if (!$entry.Name) { continue }
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        $original = Join-Path $output $entry.FullName
        if ($hash -ne (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash) {
            throw "ZIP entry differs from source: $($entry.FullName)"
        }
    }
} finally { $archive.Dispose() }
$records = @(Get-ChildItem -LiteralPath $output -File -Recurse -Force | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path=[IO.Path]::GetRelativePath($output,$_.FullName).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$manifest = [ordered]@{ version='0.6.0'; kind='OPS-03 source overlay and verification package; requires the baseline repository';
    baselineCommit=(& git -C $root rev-parse HEAD); workingTreeChangesIncluded=$true;
    createdAt=[DateTimeOffset]::UtcNow.ToString('O'); files=$records }
$manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'manifest.json') -Encoding utf8NoBOM
$checksums = @($records | ForEach-Object { "$($_.sha256)  $($_.path)" })
$checksums += "$((Get-FileHash (Join-Path $output 'manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant())  manifest.json"
$checksums | Set-Content (Join-Path $output 'SHA256SUMS') -Encoding utf8NoBOM
foreach ($record in $records) {
    $actual = (Get-FileHash -LiteralPath (Join-Path $output $record.path) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $record.sha256) { throw "Package hash mismatch: $($record.path)" }
}
Write-Host "Verified package: $output ($($records.Count) files)"
