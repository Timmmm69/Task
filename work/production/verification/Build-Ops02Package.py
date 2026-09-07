"""Build the fully validated OPS-02 synthetic clean-room acceptance package."""

from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[3]
VERSION = "1.0.0"
NAME = f"20260907_ops02_network_tls_{VERSION}"
OUTPUT = ROOT / "outputs" / NAME

SOURCES = [
    "work/production/deployment/ops02/Bootstrap-Ops02Database.sh",
    "work/production/deployment/ops02/Grant-Ops02Runtime.sh",
    "work/production/deployment/ops02/New-Ops02CleanRoomAssets.ps1",
    "work/production/deployment/ops02/Ops02.Foundation.psm1",
    "work/production/deployment/ops02/Set-Ops02Firewall.ps1",
    "work/production/deployment/ops02/Test-Ops02DatabaseTls.sh",
    "work/production/deployment/ops02/ops02.parameters.example.json",
    "work/production/deployment/security/compose.production.yaml",
    "work/production/deployment/security/production.env.example",
    "work/production/docs/OPS-02-production-like-network-tls-foundation.md",
    "work/production/docs/SEC-03-production-secrets-tls.md",
    "work/production/verification/Build-Ops02Package.py",
    "work/production/verification/Test-Ops02CleanRoom.ps1",
    "work/production/verification/Test-Ops02Foundation.ps1",
    "work/production/verification/Test-Ops02RuntimeFoundation.ps1",
    "work/production/verification/Test-ProductionSecretsTls.Contract.ps1",
    "work/production/verification/Test-ProductionSecretsTls.ps1",
]

EVIDENCE = [
    "work/production/evidence/ops02-foundation/checks.json",
    "work/production/evidence/ops02-foundation/dns-compose.resolved.yaml",
    "work/production/evidence/ops02-foundation/firewall-plan.json",
    "work/production/evidence/ops02-foundation/production-compose.resolved.yaml",
    "work/production/evidence/ops02-foundation/runtime-foundation.json",
    "work/production/evidence/ops02-linux-firewall/docker-user-chain.txt",
    "work/production/evidence/ops02-linux-firewall/firewall-apply.json",
    "work/production/evidence/ops02-linux-firewall/firewall-remove.json",
    "work/production/evidence/ops02-linux-firewall/input-chain.txt",
    "work/production/evidence/ops02-linux-firewall/iptables-after.rules",
    "work/production/evidence/ops02-linux-firewall/iptables-before.rules",
    "work/production/evidence/ops02-clean-room/clean-room-runs.json",
]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(*args: str) -> str:
    return subprocess.check_output(
        ["git", "-c", "core.quotepath=false", "-c", "core.safecrlf=false", *args], cwd=ROOT
    ).decode("utf-8").strip()


def load_json(relative: str) -> dict:
    return json.loads((ROOT / relative).read_text(encoding="utf-8-sig"))


def validate() -> tuple[dict, dict, dict]:
    assert all((ROOT / path).is_file() for path in SOURCES + EVIDENCE), "OPS-02 package input is incomplete"
    foundation = load_json("work/production/evidence/ops02-foundation/checks.json")
    runtime = load_json("work/production/evidence/ops02-foundation/runtime-foundation.json")
    clean = load_json("work/production/evidence/ops02-clean-room/clean-room-runs.json")
    apply = load_json("work/production/evidence/ops02-linux-firewall/firewall-apply.json")
    remove = load_json("work/production/evidence/ops02-linux-firewall/firewall-remove.json")
    assert foundation["result"] == "PASS" and len(foundation["checks"]) == 9
    assert all(foundation["checks"].values())
    assert runtime["result"] == "PASS"
    assert clean["result"] == "PASS" and clean["syntheticOnly"] is True and len(clean["runs"]) == 2
    required_run_flags = [
        "httpsReady", "hsts", "untrustedCaRejected", "wrongSanRejected", "tls11Rejected",
        "rotationChangedThumbprint", "wrongRotationRolledBack",
    ]
    for run in clean["runs"]:
        assert run["result"] == "PASS" and all(run[name] is True for name in required_run_flags)
        assert "expectedVersion=13 actualVersion=13" in run["migrationStatus"]
        assert "TLSv1.3 plaintextRejected=true" in run["databaseTls"]
        assert "127.0.0.1:18443->8443/tcp" in run["ports"]
        assert ":5432->" not in run["ports"] and ":8080->" not in run["ports"]
    assert apply["action"] == "apply" and remove["action"] == "remove"
    docker_rules = "\n".join(apply["rules"][1])
    assert "--ctorigdstport 5432 -j DROP" in docker_rules
    assert "--ctorigdstport 8080 -j DROP" in docker_rules
    for relative in EVIDENCE:
        content = (ROOT / relative).read_text(encoding="utf-8-sig", errors="replace")
        assert "BEGIN PRIVATE KEY" not in content and "BEGIN EC PRIVATE KEY" not in content
    return foundation, runtime, clean


def main() -> None:
    foundation, runtime, clean = validate()
    if OUTPUT.exists():
        shutil.rmtree(OUTPUT)
    (OUTPUT / "source").mkdir(parents=True)
    (OUTPUT / "evidence").mkdir()
    for relative in SOURCES:
        destination = OUTPUT / "source" / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, destination)
    for relative in EVIDENCE:
        destination = OUTPUT / "evidence" / Path(relative).relative_to("work/production/evidence")
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, destination)

    report = f"""# OPS-02 production-like network/TLS validation report — {VERSION}

Result: PASS. OPS-02 complete. Base commit: {git('rev-parse', 'HEAD')}.

All {len(foundation['checks'])} foundation checks passed. Docker Engine
{runtime['dockerServerVersion']} reproduced authoritative DNS and the isolated three-network
topology. A temporary Linux network namespace accepted and removed the deny-by-default INPUT and
DOCKER-USER rules while retaining before/after exports.

Two independent synthetic Docker-in-Docker runs generated fresh CA/credentials and volumes. Both
initialized PostgreSQL, applied migration version 13, started API/Worker/TLS proxy, returned HTTPS
readiness with HSTS, connected to PostgreSQL using verify-full/TLS 1.3, rejected plaintext DB
traffic, untrusted CA, wrong SAN and TLS 1.1, rotated the edge certificate, rejected the deliberately
bad rotation, rolled back, and exposed no host port except 127.0.0.1:18443.

The evidence and package contain no private key or reusable credential. All addresses, names,
certificates and credentials used by acceptance were disposable synthetic values; no company data
or company infrastructure was used or is required.
"""
    (OUTPUT / "validation-report.md").write_text(report, encoding="utf-8")
    (OUTPUT / "VERSION").write_text(VERSION + "\n", encoding="utf-8")

    entries = [
        {"path": p.relative_to(OUTPUT).as_posix(), "size": p.stat().st_size, "sha256": sha256(p)}
        for p in sorted(OUTPUT.rglob("*")) if p.is_file()
    ]
    manifest = {
        "package": NAME,
        "version": VERSION,
        "base_commit": git("rev-parse", "HEAD"),
        "release_status": "ops02_complete",
        "synthetic_only": True,
        "completed_steps": [1, 2, 3, 4, 5, 6],
        "clean_room_runs": len(clean["runs"]),
        "source_files": SOURCES,
        "evidence_files": EVIDENCE,
        "files": entries,
    }
    (OUTPUT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    checksummed = sorted(p for p in OUTPUT.rglob("*") if p.is_file() and p.name != "SHA256SUMS")
    (OUTPUT / "SHA256SUMS").write_text(
        "".join(f"{sha256(p)}  {p.relative_to(OUTPUT).as_posix()}\n" for p in checksummed), encoding="utf-8"
    )

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
    print(json.dumps({"package": str(archive), "sha256": archive_sha, "result": "PASS"}))


if __name__ == "__main__":
    main()
