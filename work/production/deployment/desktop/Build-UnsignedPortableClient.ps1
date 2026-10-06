[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$outputsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'outputs'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith($outputsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Output directory must be inside outputs/.'
}
if (Test-Path -LiteralPath $output) { throw "Output already exists: $output" }

$desktopProject = Join-Path $projectRoot 'work\production\src\Task.Desktop\Task.Desktop.csproj'
$revision = (git -C $projectRoot rev-parse HEAD).Trim()
$workingTreeChanges = @(git -C $projectRoot status --short -- work/production/src)
$payload = Join-Path $output 'Task'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
dotnet publish $desktopProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=$Version -p:DebugType=None -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Task desktop publish failed.' }

$files = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{
        path = [IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\', '/')
        bytes = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
if (-not (Test-Path -LiteralPath (Join-Path $payload 'Task.Desktop.exe'))) { throw 'Desktop executable is missing.' }
$manifest = [ordered]@{
    schemaVersion = 1
    product = 'Task unsigned portable Windows client'
    version = $Version
    architecture = 'win-x64'
    sourceRevision = $revision
    cleanSourceTree = ($workingTreeChanges.Count -eq 0)
    sourceChanges = $workingTreeChanges
    files = $files
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'manifest.json') -Encoding utf8
$archive = Join-Path $output "Task-$Version-win-x64-unsigned.zip"
Compress-Archive -LiteralPath $payload -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS') -Encoding ascii
[pscustomobject]@{ archive = $archive; sha256 = $hash; sourceRevision = $revision; cleanSourceTree = ($workingTreeChanges.Count -eq 0); files = $files.Count }
