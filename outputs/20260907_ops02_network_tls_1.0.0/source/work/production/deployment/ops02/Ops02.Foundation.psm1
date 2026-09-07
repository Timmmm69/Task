Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Ops02 {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "OPS-02 parameter contract failed: $Message" }
}

function Test-Property {
    param($Object, [string]$Name)
    return $null -ne $Object -and $Object.PSObject.Properties.Name -contains $Name
}

function Test-Ipv4Address {
    param([string]$Value)
    $parsed = $null
    return [Net.IPAddress]::TryParse($Value, [ref]$parsed) -and
        $parsed.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork
}

function Test-Ipv4Cidr {
    param([string]$Value)
    if ($Value -notmatch '^([^/]+)/([0-9]{1,2})$') { return $false }
    $prefix = [int]$Matches[2]
    return (Test-Ipv4Address $Matches[1]) -and $prefix -ge 1 -and $prefix -le 32
}

function Test-DnsName {
    param([string]$Value, [bool]$RequireFqdn = $false)
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Length -gt 253) { return $false }
    if ($RequireFqdn -and $Value -notmatch '\.') { return $false }
    foreach ($label in $Value.TrimEnd('.').Split('.')) {
        if ($label -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$') { return $false }
    }
    return $true
}

function Assert-StringArray {
    param($Value, [string]$Name, [scriptblock]$Predicate)
    Assert-Ops02 ($null -ne $Value -and @($Value).Count -gt 0) "$Name must contain at least one value."
    foreach ($item in @($Value)) {
        Assert-Ops02 (& $Predicate ([string]$item)) "$Name contains an invalid value '$item'."
    }
}

function Get-Ops02Configuration {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $configuration = Get-Content -LiteralPath $resolved -Raw | ConvertFrom-Json -Depth 20
    $required = @(
        'schemaVersion', 'environment', 'deploymentName', 'serverDnsName', 'databaseDnsName',
        'serverAddress', 'httpsBindAddress', 'httpsPort', 'externalInterface', 'employeeCidrs',
        'managementCidrs', 'managementTcpPorts', 'dns', 'testPki', 'dockerNetworks', 'deployment'
    )
    foreach ($name in $required) { Assert-Ops02 (Test-Property $configuration $name) "Missing '$name'." }

    Assert-Ops02 ($configuration.schemaVersion -eq 1) 'schemaVersion must be 1.'
    Assert-Ops02 ($configuration.environment -eq 'clean-room') "environment must be 'clean-room'."
    Assert-Ops02 ($configuration.deploymentName -match '^[a-z][a-z0-9-]{2,40}$') 'deploymentName is invalid.'
    Assert-Ops02 (Test-DnsName ([string]$configuration.serverDnsName) $true) 'serverDnsName must be a valid FQDN.'
    Assert-Ops02 (Test-DnsName ([string]$configuration.databaseDnsName)) 'databaseDnsName is invalid.'
    Assert-Ops02 (Test-Ipv4Address ([string]$configuration.serverAddress)) 'serverAddress must be IPv4.'
    Assert-Ops02 (Test-Ipv4Address ([string]$configuration.httpsBindAddress)) 'httpsBindAddress must be IPv4.'
    Assert-Ops02 ($configuration.serverAddress -eq $configuration.httpsBindAddress) 'serverAddress and httpsBindAddress must match for the clean-room host.'
    Assert-Ops02 ([int]$configuration.httpsPort -ge 1 -and [int]$configuration.httpsPort -le 65535) 'httpsPort is outside 1..65535.'
    Assert-Ops02 ($configuration.externalInterface -match '^[A-Za-z0-9_.:-]{1,32}$') 'externalInterface is invalid.'
    Assert-StringArray $configuration.employeeCidrs 'employeeCidrs' { param($item) Test-Ipv4Cidr $item }
    Assert-StringArray $configuration.managementCidrs 'managementCidrs' { param($item) Test-Ipv4Cidr $item }

    Assert-Ops02 (@($configuration.managementTcpPorts).Count -gt 0) 'managementTcpPorts must not be empty.'
    foreach ($port in @($configuration.managementTcpPorts)) {
        Assert-Ops02 ([int]$port -ge 1 -and [int]$port -le 65535 -and [int]$port -ne [int]$configuration.httpsPort) "Invalid or duplicate-purpose management port '$port'."
    }

    foreach ($name in @('zoneName', 'bindAddress', 'ttlSeconds', 'image')) {
        Assert-Ops02 (Test-Property $configuration.dns $name) "Missing dns.$name."
    }
    $zone = ([string]$configuration.dns.zoneName).TrimEnd('.').ToLowerInvariant()
    $serverName = ([string]$configuration.serverDnsName).TrimEnd('.').ToLowerInvariant()
    Assert-Ops02 (Test-DnsName $zone $true) 'dns.zoneName must be a valid FQDN.'
    Assert-Ops02 ($serverName.EndsWith(".$zone", [StringComparison]::Ordinal) -and $serverName -ne $zone) 'serverDnsName must be a host below dns.zoneName.'
    Assert-Ops02 (Test-Ipv4Address ([string]$configuration.dns.bindAddress)) 'dns.bindAddress must be IPv4.'
    Assert-Ops02 ([int]$configuration.dns.ttlSeconds -ge 30 -and [int]$configuration.dns.ttlSeconds -le 86400) 'dns.ttlSeconds must be within 30..86400.'
    Assert-Ops02 ([string]$configuration.dns.image -match '@sha256:[a-fA-F0-9]{64}$') 'dns.image must be an immutable digest reference.'

    foreach ($name in @('caCommonName', 'leafValidityDays')) {
        Assert-Ops02 (Test-Property $configuration.testPki $name) "Missing testPki.$name."
    }
    Assert-Ops02 (-not [string]::IsNullOrWhiteSpace($configuration.testPki.caCommonName) -and $configuration.testPki.caCommonName.Length -le 64) 'testPki.caCommonName is invalid.'
    Assert-Ops02 ([int]$configuration.testPki.leafValidityDays -ge 31 -and [int]$configuration.testPki.leafValidityDays -le 397) 'testPki.leafValidityDays must be within 31..397.'

    $subnets = @()
    foreach ($name in @('databaseSubnet', 'applicationEdgeSubnet', 'frontendSubnet')) {
        Assert-Ops02 (Test-Property $configuration.dockerNetworks $name) "Missing dockerNetworks.$name."
        $value = [string]$configuration.dockerNetworks.$name
        Assert-Ops02 (Test-Ipv4Cidr $value) "dockerNetworks.$name is invalid."
        $subnets += $value
    }
    Assert-Ops02 (@($subnets | Sort-Object -Unique).Count -eq 3) 'Docker network subnets must be distinct.'

    foreach ($name in @('secretRoot', 'environmentFile', 'databaseName', 'images')) {
        Assert-Ops02 (Test-Property $configuration.deployment $name) "Missing deployment.$name."
    }
    Assert-Ops02 ([string]$configuration.deployment.secretRoot -match '^/') 'deployment.secretRoot must be an absolute Linux path.'
    Assert-Ops02 ([string]$configuration.deployment.environmentFile -match '^/') 'deployment.environmentFile must be an absolute Linux path.'
    Assert-Ops02 ([string]$configuration.deployment.databaseName -match '^[A-Za-z_][A-Za-z0-9_]{0,62}$') 'deployment.databaseName is invalid.'
    foreach ($name in @('api', 'worker', 'migrator', 'tlsProxy', 'postgres')) {
        Assert-Ops02 (Test-Property $configuration.deployment.images $name) "Missing deployment.images.$name."
        Assert-Ops02 ([string]$configuration.deployment.images.$name -match '@sha256:[a-fA-F0-9]{64}$') "deployment.images.$name must be an immutable digest reference."
    }

    return $configuration
}

function Assert-Ops02OutputOutsideRepository {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
    $outputPath = [IO.Path]::GetFullPath($Path)
    $repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    Assert-Ops02 (-not (($outputPath + [IO.Path]::DirectorySeparatorChar).StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase))) 'Generated CA keys and clean-room assets must stay outside the repository.'
    return $outputPath
}

function Get-Ops02FirewallPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Configuration)

    $interface = [string]$Configuration.externalInterface
    $bind = [string]$Configuration.httpsBindAddress
    $httpsPort = [string]$Configuration.httpsPort
    $inputRules = [Collections.Generic.List[object]]::new()
    $dockerRules = [Collections.Generic.List[object]]::new()
    $inputRules.Add(@('-m', 'conntrack', '--ctstate', 'ESTABLISHED,RELATED', '-j', 'RETURN'))
    foreach ($cidr in @($Configuration.managementCidrs)) {
        foreach ($port in @($Configuration.managementTcpPorts)) {
            $inputRules.Add(@('-i', $interface, '-s', [string]$cidr, '-p', 'tcp', '--dport', [string]$port, '-j', 'RETURN'))
        }
    }
    $inputRules.Add(@('-i', $interface, '-j', 'DROP'))
    $inputRules.Add(@('-j', 'RETURN'))

    $dockerRules.Add(@('-m', 'conntrack', '--ctstate', 'ESTABLISHED,RELATED', '-j', 'RETURN'))
    foreach ($cidr in @($Configuration.employeeCidrs) + @($Configuration.managementCidrs)) {
        $dockerRules.Add(@('-i', $interface, '-s', [string]$cidr, '-p', 'tcp', '-m', 'conntrack', '--ctorigdst', $bind, '--ctorigdstport', $httpsPort, '-j', 'RETURN'))
    }
    foreach ($subnet in @(
        $Configuration.dockerNetworks.databaseSubnet,
        $Configuration.dockerNetworks.applicationEdgeSubnet,
        $Configuration.dockerNetworks.frontendSubnet)) {
        $dockerRules.Add(@('-i', $interface, '-d', [string]$subnet, '-j', 'DROP'))
    }
    foreach ($port in @('5432', '8080')) {
        $dockerRules.Add(@('-i', $interface, '-p', 'tcp', '-m', 'conntrack', '--ctorigdst', $bind, '--ctorigdstport', $port, '-j', 'DROP'))
    }
    $dockerRules.Add(@('-i', $interface, '-p', 'tcp', '-m', 'conntrack', '--ctorigdst', $bind, '--ctorigdstport', $httpsPort, '-j', 'DROP'))
    $dockerRules.Add(@('-j', 'RETURN'))

    return [ordered]@{
        schemaVersion = 1
        inputChain = 'TASK-OPS02-IN'
        dockerUserChain = 'TASK-OPS02-DOCKER'
        inputRules = @($inputRules)
        dockerUserRules = @($dockerRules)
    }
}

Export-ModuleMember -Function Get-Ops02Configuration, Assert-Ops02OutputOutsideRepository, Get-Ops02FirewallPlan
