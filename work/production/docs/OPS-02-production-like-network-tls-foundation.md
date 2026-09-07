# OPS-02 — production-like network/TLS clean-room runbook

Status: complete. The source gates, live Linux `iptables` acceptance and two independent synthetic
Docker-in-Docker clean-room replays passed. No company data or company infrastructure is required.

## 1. Scope and fixed security boundary

This foundation reproduces the infrastructure that must exist before the Task application starts:

- a customer-parameter contract containing no credentials or private keys;
- a synthetic local root CA and separate `serverAuth` leaves for the edge and PostgreSQL;
- an authoritative clean-room DNS zone served by a pinned CoreDNS image;
- deterministic Docker subnets for `database`, `application-edge` and `frontend`;
- deny-by-default Linux INPUT and Docker `DOCKER-USER` rules;
- an explicit external TLS bind address and no published API or PostgreSQL port.

The test CA is disposable laboratory material. Its root must never be installed in a customer trust
store, and its signing key must never be copied into `TASK_SECRET_ROOT`. A customer deployment uses
the corporate/approved internal CA and retains only issued leaves, their private keys and public CA
chains according to `SEC-03-production-secrets-tls.md`.

The clean-room DNS resolver is a separate host/node. Do not run it on the Task application host:
the application-host firewall intentionally does not open TCP/UDP 53. In production, corporate DNS
replaces this synthetic resolver.

## 2. Parameter contract

Copy `deployment/ops02/ops02.parameters.example.json` to an operations-controlled location and
replace every documentation/test value. The file is non-secret and may be retained as evidence.

| Parameter | Owner | Rule |
|---|---|---|
| `serverDnsName` | customer DNS administrator | FQDN below `dns.zoneName`; clients use this identity, never a raw IP |
| `serverAddress`, `httpsBindAddress` | network administrator | same explicit application-host IPv4 address |
| `databaseDnsName` | deployment administrator | Compose/PostgreSQL TLS identity; normally `postgres` |
| `employeeCidrs` | network/security | approved employee and VPN sources for HTTPS |
| `managementCidrs`, `managementTcpPorts` | operations/security | break-glass/admin source ranges and ports; `/0` is rejected |
| `externalInterface` | host administrator | Linux interface receiving client/management traffic |
| `dns.*` | DNS administrator | separate resolver bind IP, zone, TTL and immutable CoreDNS digest |
| `testPki.*` | clean-room operator | synthetic CA display name and 31–397 day leaf lifetime |
| `dockerNetworks.*` | network administrator | three distinct non-overlapping Docker IPv4 subnets |
| `deployment.*` | release/deployment | Linux paths, database name and five immutable service digests |

The example uses IANA documentation ranges and fake Task image digests. It is structurally valid but
must not be applied unchanged. All image values must end in `@sha256:<64 hex>`; tags and mutable
references are rejected.

Validate the contract and all generated-source invariants:

```powershell
./work/production/verification/Test-Ops02Foundation.ps1 `
  -EvidenceDirectory <protected-evidence-directory>
```

Stop if the contract rejects a value. Do not weaken validation to accommodate an environment.

## 3. Generate the synthetic CA and DNS assets

Run on an administrative workstation with PowerShell 7. The output must be outside the repository
and on encrypted, access-controlled storage:

```powershell
./work/production/deployment/ops02/New-Ops02CleanRoomAssets.ps1 `
  -ParameterFile <ops02.parameters.json> `
  -OutputDirectory <external-clean-room-assets>
```

The generator refuses a repository-local output and a non-empty destination. `-Force` is allowed
only for a disposable, already verified clean-room directory; it destroys the prior generated CA
and assets before creating new material.

Generated layout:

```text
assets-metadata.json                 public certificate metadata
production.env.partial              non-secret Compose inputs for later OPS-02 steps
ca/private/root-ca.key               synthetic signing key; never package or deploy
ca/task-clean-room-root-ca.crt       synthetic public trust anchor
client/task-clean-room-root-ca.crt   copy for the isolated test client
secrets/edge/*                       edge leaf/key and public chain
secrets/database/*                   PostgreSQL leaf/key and public CA
secrets/database/*.pgpass            independent generated migration/runtime credentials
secrets/identity/*                   generated JWT P-256 key ring and password pepper
rotation/edge-v2/*                   second valid edge leaf for rotation acceptance
rotation/wrong-san/*                 same-CA leaf with deliberately incorrect SAN
dns/Corefile                         authoritative CoreDNS configuration
dns/zones/db.<zone>                  RFC 1035 zone with NS and Task A records
dns/compose.dns.yaml                 hardened resolver container
dns/dns.env                          non-secret pinned resolver inputs
```

On Unix, directories are set to owner-only and private keys to `0600`. On Windows, inherited ACLs
are removed and the current user receives full control. Inspect the effective ACL before copying
any leaf private key to another host.

### Start and verify the separate clean-room resolver

On the DNS node:

```powershell
docker compose `
  --env-file <external-clean-room-assets>/dns/dns.env `
  -f <external-clean-room-assets>/dns/compose.dns.yaml `
  config

docker compose `
  --env-file <external-clean-room-assets>/dns/dns.env `
  -f <external-clean-room-assets>/dns/compose.dns.yaml `
  up -d --pull never
```

Configure only the isolated clean-room client to use `dns.bindAddress`, then verify both UDP and
TCP resolution according to the site's DNS tooling. The required result is exactly
`serverDnsName -> serverAddress`. NXDOMAIN for unrelated names is expected: this is an
authoritative test zone, not a general recursive resolver.

The repository runtime gate exercises the same zone inside Docker without changing the workstation
resolver:

```powershell
./work/production/verification/Test-Ops02RuntimeFoundation.ps1 `
  -EvidenceDirectory <protected-evidence-directory>
```

It uses only pinned images, verifies the A response and container hardening, creates three temporary
networks, records their isolation/subnets and removes all runtime objects in `finally`.

### Trust the synthetic CA on the isolated Windows client

From an elevated PowerShell session on that disposable client only:

```powershell
$certificate = Import-Certificate `
  -FilePath <external-clean-room-assets>/client/task-clean-room-root-ca.crt `
  -CertStoreLocation Cert:\LocalMachine\Root
$certificate.Thumbprint
```

Record the SHA-256 metadata from `assets-metadata.json`, not private material. Remove the trust
anchor after the clean-room exercise:

```powershell
Remove-Item -LiteralPath "Cert:\LocalMachine\Root\<recorded-thumbprint>"
```

Never use `-SkipCertificateCheck`, a custom accept-all callback or an HTTP fallback.

## 4. Deterministic application-host networks

`deployment/security/compose.production.yaml` requires these explicit values:

- `TASK_DATABASE_SUBNET` for the internal PostgreSQL network;
- `TASK_APPLICATION_EDGE_SUBNET` for the internal clear-text proxy/API boundary;
- `TASK_FRONTEND_SUBNET` for the TLS proxy frontend network.

`database` and `application-edge` remain `internal: true`. Only `tls-proxy` publishes a port, bound
to `TASK_HTTPS_BIND_IP:TASK_HTTPS_PORT`; API 8080 and PostgreSQL 5432 remain unbound.

Before application deployment, render and retain the resolved topology:

```powershell
docker compose `
  --env-file <external-clean-room-assets>/production.env.partial `
  -f ./work/production/deployment/security/compose.production.yaml `
  config --no-path-resolution `
  | Out-File <protected-evidence-directory>/production-compose.resolved.yaml
```

Stop on subnet overlap with the host, VPN, corporate LAN or another Docker network.

## 5. Deny-by-default firewall

Supported application host: Linux Docker Engine using the iptables backend with an existing
`DOCKER-USER` chain. Native Docker nftables mode, Windows containers, missing local/break-glass
console access or an unknown external interface are stop conditions, not reasons to skip the gate.

First render and review the exact rule order without changing the host:

```powershell
./work/production/deployment/ops02/Set-Ops02Firewall.ps1 `
  -ParameterFile <ops02.parameters.json> `
  -Action Plan `
  -EvidenceDirectory <protected-evidence-directory>
```

The plan creates two owned chains:

- `TASK-OPS02-IN`: established traffic, bounded management CIDR/port allows, then drop all other
  traffic arriving on the external interface;
- `TASK-OPS02-DOCKER`: established traffic, approved HTTPS source allows, Docker-subnet drops,
  explicit 5432/8080 drops, then an unapproved-source HTTPS drop.

The approved HTTPS rule is intentionally before the Docker-subnet drop because published traffic
has already undergone DNAT when it reaches `DOCKER-USER`.

Apply only from the host console or verified break-glass channel, as root:

```powershell
sudo pwsh ./work/production/deployment/ops02/Set-Ops02Firewall.ps1 `
  -ParameterFile <ops02.parameters.json> `
  -Action Apply `
  -AcknowledgeRemoteLockoutRisk `
  -EvidenceDirectory <protected-evidence-directory> `
  -Confirm:$false
```

The script captures `iptables-before.rules`, restores it automatically if any command fails, and
writes `iptables-after.rules` on success. Persist the verified result with the supported mechanism
of the chosen Linux distribution and ensure it loads after Docker Engine creates `DOCKER-USER`.

Remove only the OPS-02-owned jumps/chains when rolling back:

```powershell
sudo pwsh ./work/production/deployment/ops02/Set-Ops02Firewall.ps1 `
  -ParameterFile <ops02.parameters.json> `
  -Action Remove `
  -EvidenceDirectory <protected-evidence-directory> `
  -Confirm:$false
```

Do not flush Docker or host-wide chains manually.

## 6. Deploy and prove the application TLS path

The fully automated acceptance runner uses two disposable privileged Docker-in-Docker containers.
Each run generates a fresh synthetic CA, credentials and volumes, then loads the already pinned
Task/PostgreSQL/nginx images without reaching a company network:

```powershell
./work/production/verification/Test-Ops02CleanRoom.ps1 `
  -EvidenceDirectory ./work/production/evidence/ops02-clean-room
```

For each run the runner:

1. starts PostgreSQL with TLS required, bootstraps distinct migration/runtime roles and verifies the
   initial `MigrationsRequired` state;
2. applies migrations and requires `Ready`, version 13;
3. starts API, Worker and the TLS proxy while publishing only HTTPS on the explicit loopback bind;
4. requires HTTPS readiness, certificate-chain/hostname verification and one-year HSTS;
5. connects to PostgreSQL with libpq `sslmode=verify-full` and proves plaintext is rejected;
6. proves an untrusted CA, a wrong hostname and TLS 1.1 are rejected;
7. rotates to a second valid edge leaf, waits until the proxy serves its fingerprint, installs a
   deliberate wrong-SAN leaf, proves rejection, rolls back and proves readiness again;
8. records port inventory and destroys the Compose project, volume, networks, DinD container and
   generated private material in `finally`.

Rotation deliberately restarts only `tls-proxy`; API, Worker and PostgreSQL are not restarted. This
avoids the nondeterministic overlap of old/new nginx workers during rapid acceptance rotations.

## 7. Firewall acceptance

`Set-Ops02Firewall.ps1` was also executed inside a temporary Linux Docker network namespace with
`NET_ADMIN`, the Docker `DOCKER-USER` chain and the synthetic parameters. Acceptance requires:

- apply result `OPS02_FIREWALL_APPLY_OK` and exported INPUT/DOCKER-USER chains;
- approved-source HTTPS rules before Docker-subnet denial;
- explicit denial of direct 5432/8080 and unapproved HTTPS;
- remove result `OPS02_FIREWALL_REMOVE_OK` with no OPS-02-owned chains left.

The retained machine-readable files are under
`work/production/evidence/ops02-linux-firewall/`. They contain only synthetic documentation CIDRs.

## 8. Evidence and stop conditions

Required evidence:

- validated non-secret parameter file;
- `checks.json`, both resolved Compose configurations and `firewall-plan.json`;
- public `assets-metadata.json` values and CA trust-import thumbprint;
- `runtime-foundation.json` from an actual Docker daemon;
- protected pre/post firewall exports from the temporary Linux namespace;
- `clean-room-runs.json` containing both independent PASS runs and their port inventories.

Never collect the CA signing key, a leaf private key, a secret path containing customer identity,
passwords or token material in the evidence package.

Stop on a mutable image, repository-local generated assets, certificate/SAN mismatch, DNS response
to an unexpected address, subnet overlap, absent `DOCKER-USER`, unbounded management access,
published 5432/8080, more than one application-host port mapping, or failed firewall rollback.

OPS-02 is complete only when the source/runtime gates, Linux firewall acceptance and both rows in
`clean-room-runs.json` are `PASS`, the final package validates, and no secret/private-key file is
present in retained evidence. Re-run the same gates after any network, Compose, proxy, certificate,
PostgreSQL TLS or firewall change.
