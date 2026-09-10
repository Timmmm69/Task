# OPS-05 — signed Windows installation and update

## Release contract

Task 1.0 uses the canonical idempotent PowerShell deployment model; MSI and GPO are intentionally
out of scope. `Build-TaskDesktopRelease.ps1` publishes a self-contained `win-x64` WPF client,
Authenticode-signs every executable and operational script, hashes every payload file in a detached
signed manifest, and emits a separately signed stable/pilot channel. The PFX/private key is never
accepted as a file argument and never enters the output: an authorized operator imports the key into
their protected CurrentUser certificate store and supplies only its thumbprint.

The publisher certificate must chain to an enterprise-trusted code-signing root and contain the Code
Signing EKU. Production builds should use the internal RFC 3161 timestamp service. The company PKI
must deploy the root and publisher trust before installation. SmartScreen, antivirus, or trust
failures are blockers; scripts contain no bypass.

## Build and publish

1. On the isolated signing workstation, verify a clean approved commit and import the release
   certificate through the company key-custody process.
2. Run `Build-TaskDesktopRelease.ps1` with the approved semantic version, certificate thumbprint,
   output directory, rollout percentage, compatibility floors, HTTPS release URL, and timestamp URL.
3. Independently compare `packageSha256` from signed `channel.json`, verify both detached signatures,
   and malware-scan the ZIP. Publish the ZIP, `channel.json`, and `channel.json.signature.json` to the
   same controlled HTTPS origin. Public/uncontrolled URLs are prohibited.
4. Set API `Task:ClientRelease:MinimumClientVersion` only after the corresponding signed release is
   available. `RecommendedClientVersion` may move first; it must never be lower than the minimum.

## Install, rollout, and rollback

Extract the release ZIP and run its signed `Install-TaskDesktop.ps1`, passing the approved publisher
thumbprint. The per-user installer verifies its own trusted publisher, the verifier module, detached
manifest, every SHA-256, and every Authenticode-designated file before copying anything. Releases are
stored side-by-side under `%LOCALAPPDATA%\Programs\Task\releases`; an atomic state file and Start Menu
shortcut select the active version. Re-running the same release is safe. Direct downgrade is rejected.

The installed signed `Invoke-TaskDesktopUpdate.ps1` accepts only HTTPS channels in production. A
signed deterministic rollout bucket gates recommended updates; a client below
`minimumClientVersion` updates regardless of rollout percentage. Package hash, release signature,
publisher identity, and version agreement are all checked before activation. Local channels require
the explicit acceptance-only switch.

If activation fails, the old running process and active state remain unchanged. If a newly activated
version fails acceptance, run the signed `Rollback-TaskDesktop.ps1`; it revalidates the retained
previous release and atomically swaps the active/previous versions. It never edits `%LOCALAPPDATA%\Task`
cache or credentials. After rollback, keep the bad channel unpublished and investigate before retry.

## Target-PC acceptance

Run `Test-Ops05WindowsRelease.ps1` for a reproducible Windows gate. It creates a temporary trusted test
publisher, builds 1.0.0 and 1.1.0, performs install, mandatory update at rollout 0%, local-channel
fail-closed, tamper rejection, downgrade rejection, and rollback, verifies all installed signatures,
then removes the test trust and files. Production rollout additionally requires pilot evidence from
the supported company Windows image: trust chain, antivirus allow decision, launch/login, update,
restart, rollback, and relaunch. Record only host asset ID, OS build, timestamps, versions, result, and
ticket; never export private keys, tokens, employee data, or local cache.
