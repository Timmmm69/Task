param([Parameter(Mandatory)][string]$PacketPath)
. "$PSScriptRoot\Delegation.Common.ps1"
$packet = ConvertFrom-DelegationYaml -Text (Get-Content -Raw -Encoding UTF8 $PacketPath)
Test-DelegationPacket $packet | Out-Null
$packet | ConvertTo-Json -Depth 8
