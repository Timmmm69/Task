[CmdletBinding()]
param(
    [string]$EvidenceDirectory,
    [string]$CoreDnsImage = 'docker.io/coredns/coredns@sha256:900f9c109f7a33545d3c811516e8376df9019147b750f5ce3e254468769176ea',
    [string]$DnsProbeImage = 'docker.io/library/node@sha256:16e22a550f3863206a3f701448c45f7912c6896a62de43add43bb9c86130c3e2'
)

$ErrorActionPreference = 'Stop'
if ($CoreDnsImage -notmatch '@sha256:[a-f0-9]{64}$') { throw 'CoreDnsImage must be pinned by sha256 digest.' }
if ($DnsProbeImage -notmatch '@sha256:[a-f0-9]{64}$') { throw 'DnsProbeImage must be pinned by sha256 digest.' }
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$opsRoot = Join-Path $repositoryRoot 'work/production/deployment/ops02'
$sourceParameters = Join-Path $opsRoot 'ops02.parameters.example.json'
$assetGenerator = Join-Path $opsRoot 'New-Ops02CleanRoomAssets.ps1'
$runId = [Guid]::NewGuid().ToString('N').Substring(0, 10)
$projectName = "task-ops02-dns-$runId"
$networkPrefix = "task-ops02-$runId"
$testRoot = Join-Path ([IO.Path]::GetTempPath()) $projectName
$assetRoot = Join-Path $testRoot 'assets'
$parameterPath = Join-Path $testRoot 'parameters.json'
$createdNetworks = [Collections.Generic.List[string]]::new()

function Invoke-Docker {
    param([Parameter(Mandatory)][string[]]$Arguments, [switch]$Capture)
    $result = & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "docker failed ($LASTEXITCODE): docker $($Arguments -join ' ')" }
    if ($Capture) { return ($result -join "`n") }
}

try {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker CLI is required.' }
    $serverVersion = Invoke-Docker -Arguments @('info', '--format', '{{.ServerVersion}}') -Capture
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $parameters = Get-Content -LiteralPath $sourceParameters -Raw | ConvertFrom-Json -Depth 20
    $parameters.serverAddress = '127.0.0.1'
    $parameters.httpsBindAddress = '127.0.0.1'
    $parameters.dns.bindAddress = '127.0.0.1'
    $parameters.dns.image = $CoreDnsImage
    $parameters.dockerNetworks.databaseSubnet = '10.253.210.0/24'
    $parameters.dockerNetworks.applicationEdgeSubnet = '10.253.211.0/24'
    $parameters.dockerNetworks.frontendSubnet = '10.253.212.0/24'
    [IO.File]::WriteAllText($parameterPath, (($parameters | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
    & $assetGenerator -ParameterFile $parameterPath -OutputDirectory $assetRoot -Confirm:$false

    foreach ($network in @(
        @{ Suffix = 'database'; Subnet = '10.253.210.0/24'; Internal = $true },
        @{ Suffix = 'application-edge'; Subnet = '10.253.211.0/24'; Internal = $true },
        @{ Suffix = 'frontend'; Subnet = '10.253.212.0/24'; Internal = $false })) {
        $name = "$networkPrefix-$($network.Suffix)"
        $arguments = @('network', 'create', '--driver', 'bridge', '--subnet', $network.Subnet)
        if ($network.Internal) { $arguments += '--internal' }
        $arguments += $name
        Invoke-Docker -Arguments $arguments
        $createdNetworks.Add($name)
    }

    $networkEvidence = @()
    foreach ($name in $createdNetworks) {
        $inspection = (Invoke-Docker -Arguments @('network', 'inspect', $name) -Capture | ConvertFrom-Json)[0]
        $networkEvidence += [ordered]@{
            name = $name
            driver = $inspection.Driver
            internal = [bool]$inspection.Internal
            subnets = @($inspection.IPAM.Config | ForEach-Object Subnet)
        }
    }
    if (-not $networkEvidence[0].internal -or -not $networkEvidence[1].internal -or $networkEvidence[2].internal) {
        throw 'Runtime Docker network isolation does not match the OPS-02 contract.'
    }

    $dnsEnv = Join-Path $assetRoot 'dns/dns.env'
    $dnsCompose = Join-Path $assetRoot 'dns/compose.dns.yaml'
    Invoke-Docker -Arguments @('compose', '-p', $projectName, '--env-file', $dnsEnv, '-f', $dnsCompose, 'up', '-d', '--pull', 'never')
    $containerId = Invoke-Docker -Arguments @('compose', '-p', $projectName, '--env-file', $dnsEnv, '-f', $dnsCompose, 'ps', '-q', 'authoritative-dns') -Capture
    $running = Invoke-Docker -Arguments @('inspect', '--format', '{{.State.Running}}', $containerId) -Capture
    if ($running.Trim() -ne 'true') {
        $logs = Invoke-Docker -Arguments @('logs', $containerId) -Capture
        throw "The clean-room authoritative DNS container did not stay running: $logs"
    }
    $container = (Invoke-Docker -Arguments @('inspect', $containerId) -Capture | ConvertFrom-Json)[0]
    $dnsNetwork = $container.NetworkSettings.Networks.PSObject.Properties | Select-Object -First 1
    $dnsAddress = [string]$dnsNetwork.Value.IPAddress
    if ([string]::IsNullOrWhiteSpace($dnsAddress)) { throw 'CoreDNS container has no runtime network address.' }
    $probeScript = @'
const dns = require('node:dns');
const resolver = new dns.Resolver();
resolver.setServers([process.argv[1]]);
resolver.resolve4('task.cleanroom.test', (error, addresses) => {
  if (error) { console.error(error.code); process.exitCode = 2; return; }
  console.log(JSON.stringify(addresses));
});
'@
    $probeOutput = Invoke-Docker -Arguments @(
        'run', '--rm', '--pull', 'never', '--network', "${projectName}_default",
        $DnsProbeImage, 'node', '-e', $probeScript, $dnsAddress) -Capture
    $resolvedAddresses = @($probeOutput | ConvertFrom-Json)
    if ($resolvedAddresses -notcontains '127.0.0.1') { throw 'The clean-room authoritative DNS did not resolve task.cleanroom.test.' }

    if (-not $container.HostConfig.ReadonlyRootfs -or $container.HostConfig.SecurityOpt -notcontains 'no-new-privileges:true') {
        throw 'Runtime CoreDNS container hardening does not match the generated Compose contract.'
    }
    $published = @($container.NetworkSettings.Ports.PSObject.Properties | ForEach-Object Name | Sort-Object)
    if (($published -join ',') -ne '53/tcp,53/udp') { throw "Unexpected DNS published ports: $($published -join ', ')" }

    $evidence = [ordered]@{
        schemaVersion = 1
        checkedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        result = 'PASS'
        dockerServerVersion = $serverVersion.Trim('"')
        dns = [ordered]@{
            name = 'task.cleanroom.test'
            address = '127.0.0.1'
            probeImage = $DnsProbeImage
            image = $container.Config.Image
            readOnlyRootFilesystem = [bool]$container.HostConfig.ReadonlyRootfs
            noNewPrivileges = $container.HostConfig.SecurityOpt -contains 'no-new-privileges:true'
            publishedPorts = $published
        }
        networks = $networkEvidence
        limitation = 'Windows Docker Desktop runtime proof; Linux host iptables apply remains a deployment-host acceptance action.'
    }
    if ($EvidenceDirectory) {
        New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $EvidenceDirectory 'runtime-foundation.json'), (($evidence | ConvertTo-Json -Depth 12) + "`n"), [Text.UTF8Encoding]::new($false))
    }
    Write-Output "OPS-02 runtime foundation passed: authoritative DNS and three isolated Docker networks reproduced."
}
finally {
    if (Test-Path -LiteralPath (Join-Path $testRoot 'assets/dns/compose.dns.yaml')) {
        & docker compose -p $projectName --env-file (Join-Path $assetRoot 'dns/dns.env') -f (Join-Path $assetRoot 'dns/compose.dns.yaml') down --volumes --remove-orphans 2>$null | Out-Null
    }
    foreach ($name in $createdNetworks) { & docker network rm $name 2>$null | Out-Null }
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedRoot = (Resolve-Path -LiteralPath $testRoot).Path
        $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (($resolvedRoot + [IO.Path]::DirectorySeparatorChar).StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
        }
    }
}
