"""Build the validated OPS-02 steps 1-3 source/runtime evidence package."""

from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[3]
VERSION = "0.3.0"
NAME = f"20260907_ops02_network_tls_foundation_{VERSION}"
OUTPUT = ROOT / "outputs" / NAME
EVIDENCE = ROOT / "work/production/evidence/ops02-foundation"
SOURCES = [
    "work/production/deployment/ops02/New-Ops02CleanRoomAssets.ps1",
    "work/production/deployment/ops02/Ops02.Foundation.psm1",
    "work/production/deployment/ops02/Set-Ops02Firewall.ps1",
    "work/production/deployment/ops02/ops02.parameters.example.json",
    "work/production/deployment/security/compose.production.yaml",
    "work/production/deployment/security/production.env.example",
    "work/production/docs/OPS-02-production-like-network-tls-foundation.md",
    "work/production/docs/SEC-03-production-secrets-tls.md",
    "work/production/verification/Build-Ops02FoundationPackage.py",
    "work/production/verification/Test-Ops02Foundation.ps1",
    "work/production/verification/Test-Ops02RuntimeFoundation.ps1",
    "work/production/verification/Test-ProductionSecretsTls.Contract.ps1",
    "work/production/verification/Test-ProductionSecretsTls.ps1",
]
EVIDENCE_FILES = [
    "checks.json",
    "dns-compose.resolved.yaml",
    "firewall-plan.json",
    "production-compose.resolved.yaml",
    "runtime-foundation.json",
]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(*args: str) -> str:
    return subprocess.check_output(
        ["git", "-c", "core.quotepath=false", "-c", "core.safecrlf=false", *args],
        cwd=ROOT,
    ).decode("utf-8").strip()


def validate_evidence() -> tuple[dict, dict]:
    checks = json.loads((EVIDENCE / "checks.json").read_text(encoding="utf-8-sig"))
    required = {
        "parameter_contract_and_external_secret_boundary",
        "invalid_and_mutable_parameters_rejected",
        "local_ca_chain_san_eku_key_and_lifetime",
        "complete_synthetic_secret_bundle",
        "incomplete_secret_bundle_rejected",
        "authoritative_dns_assets",
        "deny_by_default_firewall_plan",
        "deterministic_internal_docker_topology",
        "compose_configuration_parses",
    }
    assert checks["result"] == "PASS", checks
    assert required == {name for name, value in checks["checks"].items() if value is True}, checks
    runtime = json.loads((EVIDENCE / "runtime-foundation.json").read_text(encoding="utf-8-sig"))
    assert runtime["result"] == "PASS", runtime
    assert runtime["dns"]["address"] == "127.0.0.1", runtime
    assert runtime["dns"]["readOnlyRootFilesystem"] is True, runtime
    assert runtime["dns"]["noNewPrivileges"] is True, runtime
    assert runtime["dns"]["publishedPorts"] == ["53/tcp", "53/udp"], runtime
    assert [item["internal"] for item in runtime["networks"]] == [True, True, False], runtime
    return checks, runtime


def main() -> None:
    assert all((ROOT / path).is_file() for path in SOURCES), "OPS-02 source set is incomplete"
    assert all((EVIDENCE / path).is_file() for path in EVIDENCE_FILES), "OPS-02 evidence set is incomplete"
    checks, runtime = validate_evidence()

    if OUTPUT.exists():
        shutil.rmtree(OUTPUT)
    (OUTPUT / "source").mkdir(parents=True)
    (OUTPUT / "evidence").mkdir()
    for relative in SOURCES:
        destination = OUTPUT / "source" / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, destination)
    for name in EVIDENCE_FILES:
        shutil.copyfile(EVIDENCE / name, OUTPUT / "evidence" / name)

    report = f"""# OPS-02 network/TLS foundation validation report — {VERSION}

Result: STEPS 1–3 IMPLEMENTATION PASS. Base commit: {git('rev-parse', 'HEAD')}.

## Verified

The machine-readable parameter contract rejected repository-local private-key output and mutable
DNS images. A synthetic RSA root CA issued separate edge/PostgreSQL server-auth certificates; the
gate verified SAN, EKU, lifetime, chain and private-key match. It generated an authoritative DNS
zone, a hardened pinned CoreDNS Compose definition and parameterized production subnets.

All {len(checks['checks'])} source/synthetic checks passed. Docker Engine
{runtime['dockerServerVersion']} then reproduced the authoritative DNS answer and three bridge
networks: database/internal, application-edge/internal and frontend/non-internal. The DNS container
used a read-only root filesystem, no-new-privileges and only TCP/UDP 53. Temporary containers and
networks were removed after the test.

The firewall plan permits approved employee/management CIDRs to the explicit HTTPS destination
before denying direct Docker-subnet traffic, ports 5432/8080 and unapproved HTTPS sources. Host
INPUT permits only established traffic plus configured management CIDR/ports on the external
interface. The apply path requires Linux/root, an existing Docker `DOCKER-USER` chain, explicit
lockout acknowledgement, captures pre/post rules and restores the snapshot on failure.

No generated CA key, leaf private key, password or reusable credential is included in this package.

## Validation boundary

The available runtime was Windows Docker Desktop, so live Linux-host iptables mutation was not
performed. The implementation and exact rule ordering are validated, but deployment acceptance
must still capture protected before/after firewall exports on the clean Linux application host.
Application deployment, HTTPS readiness, PostgreSQL `VerifyFull`, TLS negative cases, certificate
rotation/rollback and a second clean-room replay are OPS-02 steps 4–6. Therefore the overall OPS-02
roadmap item remains in progress and is not release-complete.
"""
    (OUTPUT / "validation-report.md").write_text(report, encoding="utf-8")
    (OUTPUT / "VERSION").write_text(VERSION + "\n", encoding="utf-8")

    files_before_manifest = [
        {
            "path": path.relative_to(OUTPUT).as_posix(),
            "size": path.stat().st_size,
            "sha256": sha256(path),
        }
        for path in sorted(OUTPUT.rglob("*"))
        if path.is_file()
    ]
    manifest = {
        "package": NAME,
        "version": VERSION,
        "base_commit": git("rev-parse", "HEAD"),
        "release_status": "ops02_in_progress_steps_1_3_implemented",
        "completed_steps": [1, 2, 3],
        "pending_steps": [4, 5, 6],
        "source_files": SOURCES,
        "evidence_files": EVIDENCE_FILES,
        "files": files_before_manifest,
    }
    (OUTPUT / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    checksummed = sorted(path for path in OUTPUT.rglob("*") if path.is_file() and path.name != "SHA256SUMS")
    (OUTPUT / "SHA256SUMS").write_text(
        "".join(f"{sha256(path)}  {path.relative_to(OUTPUT).as_posix()}\n" for path in checksummed),
        encoding="utf-8",
    )
    for path in checksummed:
        assert sha256(path) in (OUTPUT / "SHA256SUMS").read_text(encoding="utf-8")

    archive = OUTPUT.parent / f"{OUTPUT.name}.zip"
    if archive.exists():
        archive.unlink()
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
        for path in sorted(OUTPUT.rglob("*")):
            if path.is_file():
                bundle.write(path, path.relative_to(OUTPUT).as_posix())
    with zipfile.ZipFile(archive) as bundle:
        assert bundle.testzip() is None
    archive_sha = sha256(archive)
    archive.with_suffix(".zip.sha256").write_text(f"{archive_sha}  {archive.name}\n", encoding="utf-8")
    print(json.dumps({"package": str(archive), "sha256": archive_sha, "checks": len(checks["checks"])}))


if __name__ == "__main__":
    main()
