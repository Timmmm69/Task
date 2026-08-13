param(
    [string]$EventPath = $env:GITHUB_EVENT_PATH,
    [string]$BaseRef,
    [string]$HeadRef = "HEAD"
)
. "$PSScriptRoot\Delegation.Common.ps1"
if (-not $EventPath -or -not (Test-Path -LiteralPath $EventPath)) { throw "GitHub event payload is required." }
$event = Get-Content -Raw -Encoding UTF8 $EventPath | ConvertFrom-Json
$body = [string]$event.pull_request.body
$packet = ConvertFrom-PacketMetadata $body
Test-DelegationPacket $packet | Out-Null
if (-not $BaseRef) { $BaseRef = [string]$packet.base_sha }
& git cat-file -e "$BaseRef^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) { throw "STALE_PACKET: packet base_sha is not available in repository history." }
& git merge-base --is-ancestor $BaseRef $HeadRef
if ($LASTEXITCODE -ne 0) { throw "STALE_PACKET: packet base_sha is not an ancestor of the delegated change." }
$result = Test-DelegationDiff -Packet $packet -BaseRef $BaseRef -HeadRef $HeadRef
if ($event.pull_request.head.ref -notlike "agent/deepseek/*") { throw "SCOPE_VIOLATION: delegation PR must use agent/deepseek/* branch." }
$result | ConvertTo-Json -Compress
