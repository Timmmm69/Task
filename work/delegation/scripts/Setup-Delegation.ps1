param([string]$ModelId)
$ErrorActionPreference = "Stop"
$root = (& git rev-parse --show-toplevel).Trim()
if (-not (Get-Command opencode -ErrorAction SilentlyContinue)) { throw "OpenCode is not installed or is not on PATH." }
& opencode models --refresh | Out-Null
$models = @(& opencode models | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '/' })
if (-not $ModelId) {
    $matches = @($models | Where-Object { $_ -match '(?i)deepseek.*v?4.*flash|deepseek.*flash' })
    if ($matches.Count -ne 1) {
        $shown = if ($matches.Count) { $matches -join "`n  " } else { "(none)" }
        throw "Could not select exactly one DeepSeek V4 Flash model. Candidates:`n  $shown`nRun again with -ModelId provider/model."
    }
    $ModelId = $matches[0]
}
if ($models -notcontains $ModelId) { throw "Model '$ModelId' is not present in 'opencode models'." }
$settingsPath = Join-Path $root "work\delegation\local.settings.json"
@{ model = $ModelId; configured_at = (Get-Date).ToUniversalTime().ToString("o") } |
    ConvertTo-Json | Set-Content -Encoding UTF8 $settingsPath
Write-Host "Delegation configured with model: $ModelId"
