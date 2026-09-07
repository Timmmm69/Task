[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][string]$ParameterFile,
    [ValidateSet('Plan', 'Apply', 'Remove')][string]$Action = 'Plan',
    [string]$EvidenceDirectory,
    [switch]$AcknowledgeRemoteLockoutRisk
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Ops02.Foundation.psm1') -Force

function Invoke-Iptables {
    param([Parameter(Mandatory)][string[]]$Arguments, [switch]$IgnoreFailure)
    & iptables @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0 -and -not $IgnoreFailure) {
        throw "iptables failed ($LASTEXITCODE): iptables $($Arguments -join ' ')"
    }
    return $LASTEXITCODE
}

function Test-IptablesRule {
    param([string]$Chain, [string[]]$Rule)
    & iptables -C $Chain @Rule 2>$null
    return $LASTEXITCODE -eq 0
}

function Test-IptablesChain {
    param([string]$Chain)
    & iptables -n -L $Chain 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}

function Convert-RuleToText {
    param([string]$Chain, [object[]]$Rule)
    return 'iptables -A ' + $Chain + ' ' + (($Rule | ForEach-Object { [string]$_ }) -join ' ')
}

$configuration = Get-Ops02Configuration -Path $ParameterFile
$plan = Get-Ops02FirewallPlan -Configuration $configuration
$planEvidence = [ordered]@{
    schemaVersion = 1
    action = $Action.ToLowerInvariant()
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    externalInterface = [string]$configuration.externalInterface
    httpsEndpoint = "https://$($configuration.serverDnsName):$($configuration.httpsPort)"
    employeeCidrs = @($configuration.employeeCidrs)
    managementCidrs = @($configuration.managementCidrs)
    dockerSubnets = @(
        [string]$configuration.dockerNetworks.databaseSubnet,
        [string]$configuration.dockerNetworks.applicationEdgeSubnet,
        [string]$configuration.dockerNetworks.frontendSubnet)
    rules = @(
        @($plan.inputRules | ForEach-Object { Convert-RuleToText $plan.inputChain $_ }),
        @($plan.dockerUserRules | ForEach-Object { Convert-RuleToText $plan.dockerUserChain $_ }))
}

if ($EvidenceDirectory) {
    New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $EvidenceDirectory "firewall-$($Action.ToLowerInvariant()).json"),
        (($planEvidence | ConvertTo-Json -Depth 10) + [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false))
}

if ($Action -eq 'Plan') {
    $planEvidence | ConvertTo-Json -Depth 10
    return
}

if ($IsWindows) { throw 'OPS-02 firewall apply/remove is supported only on the Linux Docker host.' }
if ((id -u) -ne '0') { throw 'OPS-02 firewall apply/remove must run as root from a local or break-glass console.' }
foreach ($command in @('iptables', 'iptables-save', 'iptables-restore')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Required firewall command is unavailable: $command" }
}
if ($Action -eq 'Apply' -and -not $AcknowledgeRemoteLockoutRisk) {
    throw 'Pass -AcknowledgeRemoteLockoutRisk only after confirming local/break-glass access and the management CIDRs/ports.'
}
if (-not (Test-IptablesChain 'DOCKER-USER')) {
    throw 'Docker DOCKER-USER chain is unavailable. Start Docker Engine before applying the OPS-02 firewall.'
}

$snapshot = (& iptables-save) -join "`n"
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($snapshot)) { throw 'Could not capture the pre-change iptables snapshot.' }
if ($EvidenceDirectory) {
    [IO.File]::WriteAllText((Join-Path $EvidenceDirectory 'iptables-before.rules'), $snapshot + "`n", [Text.UTF8Encoding]::new($false))
}

try {
    if ($Action -eq 'Remove') {
        if ($PSCmdlet.ShouldProcess('Linux host firewall', 'remove OPS-02 managed chains')) {
            if (Test-IptablesRule 'INPUT' @('-i', [string]$configuration.externalInterface, '-j', $plan.inputChain)) {
                Invoke-Iptables -Arguments @('-D', 'INPUT', '-i', [string]$configuration.externalInterface, '-j', $plan.inputChain)
            }
            if (Test-IptablesRule 'DOCKER-USER' @('-i', [string]$configuration.externalInterface, '-j', $plan.dockerUserChain)) {
                Invoke-Iptables -Arguments @('-D', 'DOCKER-USER', '-i', [string]$configuration.externalInterface, '-j', $plan.dockerUserChain)
            }
            foreach ($chain in @($plan.inputChain, $plan.dockerUserChain)) {
                if (Test-IptablesChain $chain) {
                    Invoke-Iptables -Arguments @('-F', $chain)
                    Invoke-Iptables -Arguments @('-X', $chain)
                }
            }
        }
    }
    elseif ($PSCmdlet.ShouldProcess('Linux host firewall', 'apply OPS-02 deny-by-default INPUT and DOCKER-USER policy')) {
        foreach ($chain in @($plan.inputChain, $plan.dockerUserChain)) {
            if (-not (Test-IptablesChain $chain)) { Invoke-Iptables -Arguments @('-N', $chain) }
            Invoke-Iptables -Arguments @('-F', $chain)
        }
        foreach ($rule in @($plan.inputRules)) { Invoke-Iptables -Arguments (@('-A', $plan.inputChain) + @($rule)) }
        foreach ($rule in @($plan.dockerUserRules)) { Invoke-Iptables -Arguments (@('-A', $plan.dockerUserChain) + @($rule)) }
        if (-not (Test-IptablesRule 'INPUT' @('-i', [string]$configuration.externalInterface, '-j', $plan.inputChain))) {
            Invoke-Iptables -Arguments @('-I', 'INPUT', '1', '-i', [string]$configuration.externalInterface, '-j', $plan.inputChain)
        }
        if (-not (Test-IptablesRule 'DOCKER-USER' @('-i', [string]$configuration.externalInterface, '-j', $plan.dockerUserChain))) {
            Invoke-Iptables -Arguments @('-I', 'DOCKER-USER', '1', '-i', [string]$configuration.externalInterface, '-j', $plan.dockerUserChain)
        }
    }
}
catch {
    $snapshot | & iptables-restore
    if ($LASTEXITCODE -ne 0) {
        throw "OPS-02 firewall change failed and automatic rollback also failed. Use the protected snapshot immediately. Original error: $($_.Exception.Message)"
    }
    throw "OPS-02 firewall change failed; the pre-change rules were restored. $($_.Exception.Message)"
}

$after = (& iptables-save) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Firewall changed, but post-change evidence capture failed.' }
if ($EvidenceDirectory) {
    [IO.File]::WriteAllText((Join-Path $EvidenceDirectory 'iptables-after.rules'), $after + "`n", [Text.UTF8Encoding]::new($false))
}
Write-Output "OPS02_FIREWALL_$($Action.ToUpperInvariant())_OK interface=$($configuration.externalInterface)"
