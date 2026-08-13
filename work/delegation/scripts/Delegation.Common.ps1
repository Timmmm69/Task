Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:RequiredPacketFields = @(
    "task_id", "title", "base_branch", "base_sha", "risk", "merge",
    "owned_paths", "forbidden_paths", "max_files", "max_changed_lines",
    "requirements", "acceptance", "required_checks", "reference_files", "stop_conditions"
)
$script:ListPacketFields = @(
    "owned_paths", "forbidden_paths", "requirements", "acceptance",
    "required_checks", "reference_files", "stop_conditions"
)
$script:AlwaysForbiddenPaths = @(
    "sources/**", "outputs/**", ".github/**", "**/package.json", "**/package-lock.json",
    "**/pnpm-lock.yaml", "**/yarn.lock", "**/*.csproj", "**/*.sln", "**/migrations/**",
    "**/*auth*", "**/*permission*", "**/*security*", "**/*secret*", "**/*signing*",
    "**/*deploy*", "**/*backup*", "**/*restore*", "**/*update*"
)

function Get-RepositoryRoot {
    $root = (& git rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $root) { throw "Not inside a Git repository." }
    return [IO.Path]::GetFullPath($root.Trim())
}

function ConvertFrom-DelegationYaml {
    param([Parameter(Mandatory)][string]$Text)

    $packet = [ordered]@{}
    $currentList = $null
    $seenRoot = $false
    foreach ($rawLine in ($Text -split "`r?`n")) {
        if ($rawLine -match '^\s*$' -or $rawLine -match '^\s*#') { continue }
        if (-not $seenRoot) {
            if ($rawLine.Trim() -ne "DELEGATION_PACKET:") { throw "PACKET_INVALID: first content line must be DELEGATION_PACKET:." }
            $seenRoot = $true
            continue
        }
        if ($rawLine -match '^  ([a-z_]+):\s*(.*)$') {
            $key = $Matches[1]
            $value = $Matches[2].Trim()
            if ($packet.Contains($key)) { throw "PACKET_INVALID: duplicate field '$key'." }
            if ($script:ListPacketFields -contains $key) {
                if ($value) { throw "PACKET_INVALID: list field '$key' must use indented dash items." }
                $packet[$key] = [Collections.Generic.List[string]]::new()
                $currentList = $key
            } else {
                if (-not $value) { throw "PACKET_INVALID: scalar field '$key' is empty." }
                $packet[$key] = $value.Trim('"').Trim("'")
                $currentList = $null
            }
            continue
        }
        if ($rawLine -match '^    -\s+(.+)$' -and $currentList) {
            $packet[$currentList].Add($Matches[1].Trim().Trim('"').Trim("'"))
            continue
        }
        throw "PACKET_INVALID: unsupported YAML line: $rawLine"
    }
    if (-not $seenRoot) { throw "PACKET_INVALID: DELEGATION_PACKET root is missing." }
    return $packet
}

function Test-DelegationPacket {
    param([Parameter(Mandatory)][Collections.IDictionary]$Packet)

    foreach ($field in $script:RequiredPacketFields) {
        if (-not $Packet.Contains($field)) { throw "PACKET_INVALID: required field '$field' is missing." }
        if ($script:ListPacketFields -contains $field -and $Packet[$field].Count -eq 0) {
            throw "PACKET_INVALID: list '$field' cannot be empty."
        }
    }
    $unknown = @($Packet.Keys | Where-Object { $script:RequiredPacketFields -notcontains $_ })
    if ($unknown.Count) { throw "PACKET_INVALID: unknown fields: $($unknown -join ', ')." }
    if ($Packet.task_id -notmatch '^TASK-[A-Z0-9][A-Z0-9-]{2,31}$') { throw "PACKET_INVALID: task_id must match TASK-[A-Z0-9-]." }
    if ($Packet.base_branch -ne "main") { throw "PACKET_INVALID: only base_branch main is allowed." }
    if ($Packet.base_sha -notmatch '^[0-9a-fA-F]{7,40}$') { throw "PACKET_INVALID: base_sha must be a Git commit SHA." }
    if ($Packet.risk -notin @("low", "medium")) { throw "PACKET_INVALID: risk must be low or medium." }
    if ($Packet.merge -notin @("automatic", "codex-review")) { throw "PACKET_INVALID: merge must be automatic or codex-review." }
    if ($Packet.risk -eq "low" -and $Packet.merge -ne "automatic") { throw "PACKET_INVALID: low risk requires automatic merge." }
    if ($Packet.risk -eq "medium" -and $Packet.merge -ne "codex-review") { throw "PACKET_INVALID: medium risk requires codex-review." }

    $maxFiles = 0; $maxLines = 0
    if (-not [int]::TryParse($Packet.max_files, [ref]$maxFiles)) { throw "PACKET_INVALID: max_files must be an integer." }
    if (-not [int]::TryParse($Packet.max_changed_lines, [ref]$maxLines)) { throw "PACKET_INVALID: max_changed_lines must be an integer." }
    $fileLimit = if ($Packet.risk -eq "low") { 3 } else { 8 }
    $lineLimit = if ($Packet.risk -eq "low") { 150 } else { 400 }
    if ($maxFiles -lt 1 -or $maxFiles -gt $fileLimit) { throw "PACKET_INVALID: max_files exceeds the $($Packet.risk) limit ($fileLimit)." }
    if ($maxLines -lt 1 -or $maxLines -gt $lineLimit) { throw "PACKET_INVALID: max_changed_lines exceeds the $($Packet.risk) limit ($lineLimit)." }

    foreach ($path in @($Packet.owned_paths) + @($Packet.reference_files)) {
        $normalized = $path.Replace('\', '/')
        if ($normalized.StartsWith('/') -or $normalized -match '^[A-Za-z]:' -or $normalized -match '(^|/)\.\.(/|$)') {
            throw "PACKET_INVALID: paths must be repository-relative: $path"
        }
    }
    if (@($Packet.owned_paths | Where-Object { -not $_.StartsWith("work/") }).Count) {
        throw "PACKET_INVALID: every owned_path must be under work/."
    }
    return $true
}

function ConvertTo-GlobRegex {
    param([Parameter(Mandatory)][string]$Glob)
    $escaped = [Regex]::Escape($Glob.Replace('\', '/'))
    $escaped = $escaped.Replace('\*\*', '.*').Replace('\*', '[^/]*').Replace('\?', '[^/]')
    return '^' + $escaped + '$'
}

function Test-PathMatchesAnyGlob {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string[]]$Globs)
    $normalized = $Path.Replace('\', '/')
    foreach ($glob in $Globs) {
        $candidate = $glob.Replace('\', '/').TrimEnd('/')
        if ($candidate -notmatch '[*?]' -and ($normalized -eq $candidate -or $normalized.StartsWith($candidate + '/'))) { return $true }
        if ($normalized -match (ConvertTo-GlobRegex $candidate)) { return $true }
    }
    return $false
}

function Get-EffectiveForbiddenPaths {
    param([Parameter(Mandatory)][Collections.IDictionary]$Packet)
    return @($script:AlwaysForbiddenPaths + @($Packet.forbidden_paths) | Select-Object -Unique)
}

function Test-DelegationDiff {
    param(
        [Parameter(Mandatory)][Collections.IDictionary]$Packet,
        [Parameter(Mandatory)][string]$BaseRef,
        [string]$HeadRef = "HEAD",
        [string]$RepositoryPath = ".",
        [switch]$IncludeWorkingTree
    )
    $diffRange = if ($IncludeWorkingTree) { $BaseRef } else { "$BaseRef...$HeadRef" }
    $nameStatus = @(& git -C $RepositoryPath diff --name-status --find-renames $diffRange)
    if ($LASTEXITCODE -ne 0) { throw "Unable to inspect delegation diff." }
    if ($IncludeWorkingTree) {
        $untracked = @(& git -C $RepositoryPath ls-files --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) { throw "Unable to inspect untracked files." }
        $nameStatus += @($untracked | ForEach-Object { "A`t$_" })
    }
    $files = [Collections.Generic.List[string]]::new()
    foreach ($line in $nameStatus) {
        if (-not $line) { continue }
        $parts = $line -split "`t"
        $status = $parts[0]
        if ($status -match '^[DRC]') { throw "SCOPE_VIOLATION: deleted, renamed, or copied files are not allowed ($line)." }
        $path = $parts[-1].Replace('\', '/')
        $files.Add($path)
    }
    if ($files.Count -eq 0) { throw "SCOPE_VIOLATION: delegation produced no changes." }
    if ($files.Count -gt [int]$Packet.max_files) { throw "SCOPE_VIOLATION: $($files.Count) files changed; limit is $($Packet.max_files)." }

    $forbidden = Get-EffectiveForbiddenPaths $Packet
    foreach ($file in $files) {
        if (-not (Test-PathMatchesAnyGlob -Path $file -Globs @($Packet.owned_paths))) { throw "SCOPE_VIOLATION: '$file' is outside owned_paths." }
        if (Test-PathMatchesAnyGlob -Path $file -Globs $forbidden) { throw "SCOPE_VIOLATION: '$file' matches a forbidden path." }
    }
    $numstat = @(& git -C $RepositoryPath diff --numstat $diffRange)
    $changedLines = 0
    foreach ($line in $numstat) {
        $parts = $line -split "`t"
        if ($parts.Count -ge 2) {
            if ($parts[0] -eq '-' -or $parts[1] -eq '-') { throw "SCOPE_VIOLATION: binary changes are not allowed." }
            $changedLines += [int]$parts[0] + [int]$parts[1]
        }
    }
    if ($IncludeWorkingTree) {
        foreach ($file in $untracked) {
            $absolute = Join-Path $RepositoryPath $file
            try {
                $bytes = [IO.File]::ReadAllBytes($absolute)
                if ($bytes -contains 0) { throw "SCOPE_VIOLATION: binary changes are not allowed." }
                $text = [Text.Encoding]::UTF8.GetString($bytes)
                if ($text.Length) { $changedLines += ($text -split "`r?`n").Count }
            } catch {
                if ($_.Exception.Message -like "SCOPE_VIOLATION:*") { throw }
                throw "SCOPE_VIOLATION: unable to inspect new file '$file'."
            }
        }
    }
    if ($changedLines -gt [int]$Packet.max_changed_lines) { throw "SCOPE_VIOLATION: $changedLines changed lines; limit is $($Packet.max_changed_lines)." }

    if ($Packet.risk -eq "low") {
        $dependencySignals = @(& git -C $RepositoryPath diff $diffRange -- @($files) | Select-String -Pattern '^\+.*(PackageReference|ProjectReference|dependencies|devDependencies|public\s+(class|interface|record)|export\s+(class|function|const))')
        if ($dependencySignals.Count) { throw "SCOPE_VIOLATION: low-risk diff appears to change a dependency or public interface." }
    }
    return [pscustomobject]@{ files = @($files); file_count = $files.Count; changed_lines = $changedLines }
}

function ConvertTo-PacketMetadata {
    param([Parameter(Mandatory)][Collections.IDictionary]$Packet)
    $ordered = [ordered]@{}
    foreach ($key in $script:RequiredPacketFields) { $ordered[$key] = $Packet[$key] }
    $json = $ordered | ConvertTo-Json -Depth 8 -Compress
    return [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
}

function ConvertFrom-PacketMetadata {
    param([Parameter(Mandatory)][string]$Body)
    if ($Body -notmatch '<!--\s*DELEGATION_PACKET_JSON:([A-Za-z0-9+/=]+)\s*-->') { throw "PACKET_INVALID: PR metadata is missing." }
    $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Matches[1]))
    $object = $json | ConvertFrom-Json
    $packet = [ordered]@{}
    foreach ($property in $object.PSObject.Properties) { $packet[$property.Name] = $property.Value }
    return $packet
}
