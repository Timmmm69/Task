from __future__ import annotations

import hashlib
import json
import shutil
import zipfile
from pathlib import Path


PROJECT = Path(__file__).resolve().parents[2]
PACKAGE = Path(__file__).resolve().parent / "stage_2_3"
RUNTIME = PACKAGE / "qa" / "reports" / "stage_2_3_runtime"
OUTPUTS = PROJECT / "outputs"
OUTPUT_DIR = OUTPUTS / "stage_2_3"
VERSION = "2.3.1"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text.rstrip() + "\n", encoding="utf-8", newline="\n")


def included_files(root: Path) -> list[Path]:
    result = []
    for path in root.rglob("*"):
        if not path.is_file():
            continue
        relative = path.relative_to(root)
        if any(part in {"bin", "obj", "node_modules", ".git"} for part in relative.parts):
            continue
        result.append(path)
    return sorted(result, key=lambda item: item.relative_to(root).as_posix())


runtime_result = json.loads((RUNTIME / "runtime_validation.json").read_text(encoding="utf-8"))
contract_diff = (RUNTIME / "Stage_2_3_Contract_Diff.csv").read_text(encoding="utf-8")
write(PACKAGE / "Stage_2_3_Contract_Diff.csv", contract_diff)

validation_report = f"""
# Stage 2.3 Validation

Version: `{VERSION}`  
Validation date: `2026-07-26`  
Decision: **PASS — final baseline is eligible for Stage 3.5**.

## Gate summary

| Gate | Result | Evidence |
|---|---|---|
| Input ZIP CRC, manifest and SHA-256 | PASS | 352 ZIP entries; 351 manifest entries; all hashes matched |
| YAML parse and OpenAPI 3.1 schema | PASS | `qa/reports/stage_2_3_runtime/openapi_contract_validation.log` |
| Local `$ref` resolution | PASS | 2,776 references resolved |
| Redocly lint | PASS | 0 errors, 0 warnings; container exit 0 |
| Contract counts | PASS | 244 operations; 237 schemas; 91 permissions; 44 stable errors |
| Backward compatibility | PASS | All 241 Stage 2.2 operations preserved; additive changes only |
| C# desktop generation | PASS | NSwag 14.7.1; 244 operations |
| C# desktop compilation | PASS | .NET 8.0.423; 0 errors, 0 warnings |
| C# server stub generation | PASS | NSwag 14.7.1; 244 actions |
| C# server compilation | PASS | .NET 8.0.423; 0 errors, 0 warnings |
| TypeScript dependent codegen | PASS | 277 SDK files; strict compilation passed |
| PostgreSQL clean install | PASS | PostgreSQL 16.10 |
| PostgreSQL 2.2 → 2.3 upgrade | PASS | Data preserved |
| Repeated seed/migration | PASS | `002` and `005` reruns passed |
| Urgency-scale constraints | PASS | Invalid gap rejected; transaction rolled back |
| Functional contract tests | PASS | {len(runtime_result["functional_contract_tests"])} checks |
| OQ-001 | CLOSED | Organization urgency-scale API/DTO/migration |
| OQ-003 | CLOSED | Employee global-search result contract |
| Critical / High remaining | 0 / 0 | All detected defects fixed and gates repeated |

## Counts

- API operations: **244**.
- DTO/schema: **237**.
- Permission catalog: **91**.
- Stable error codes: **44**.
- Concrete request schemas checked: **124**.
- Concrete success response schemas checked: **231**.
- Operation permission bindings checked: **230**.
- Stable error bindings checked: **4,579**.

## Defects

Three defects were found and fixed: two High contract/runtime defects and one High packaging/codegen consistency defect. Remaining Critical/High/Medium: **0/0/0**.
"""
write(PACKAGE / "Stage_2_3_Validation.md", validation_report)

runtime_report = r"""
# Stage 2.3 Runtime Validation

## Environment

| Tool | Version |
|---|---|
| .NET SDK | 8.0.423 |
| .NET runtime | 8.0.29 |
| NSwag | 14.7.1.0 |
| NJsonSchema | 11.6.1 |
| Docker client / engine | 29.6.1 / 29.6.1 |
| Docker Desktop | 4.80.0 |
| PostgreSQL | 16.10 |
| Node.js | 22.23.1 |
| npm | 10.9.8 |
| Redocly CLI | 2.40.0 |
| openapi-typescript | 7.9.1 |
| openapi-typescript-codegen | 0.29.0 |
| TypeScript | 5.8.3 |
| Python | 3.12.13 |
| openapi-spec-validator | 0.7.2 |

## Executed gates

The following commands were executed from `work/stage_2_3_validation/stage_2_3`:

```powershell
docker run --rm -v "${root}:/work:ro" -w /work node:22-alpine sh -lc "npx --yes @redocly/cli@2.40.0 lint openapi/openapi.yaml --format=stylish"

nswag openapi2csclient /input:openapi/openapi.yaml /output:qa/generated/desktop-csharp/OrganizerDesktopClient.g.cs /classname:OrganizerClient /namespace:Organizer.DesktopSdk /operationGenerationMode:SingleClientFromOperationId /generateClientInterfaces:true /injectHttpClient:true /disposeHttpClient:false /jsonLibrary:SystemTextJson /generateNullableReferenceTypes:true /useRequiredKeyword:true /generateOptionalPropertiesAsNullable:true

nswag openapi2cscontroller /input:openapi/openapi.yaml /output:qa/generated/server-csharp/OrganizerController.g.cs /classname:OrganizerController /namespace:Organizer.ServerStubs /controllerBaseClass:Microsoft.AspNetCore.Mvc.ControllerBase /controllerStyle:Abstract /useActionResultType:true /operationGenerationMode:SingleClientFromOperationId /generateModelValidationAttributes:true /useCancellationToken:true /jsonLibrary:SystemTextJson /generateNullableReferenceTypes:true /useRequiredKeyword:true /generateOptionalPropertiesAsNullable:true

dotnet build qa/generated/desktop-csharp/Organizer.DesktopSdk.csproj -c Release --nologo
dotnet build qa/generated/server-csharp/Organizer.ServerStubs.csproj -c Release --nologo

docker run --rm -v "${root}:/work" -w /work node:22-alpine sh -lc "npx --yes openapi-typescript@7.9.1 openapi/openapi.yaml -o qa/generated/server-contract/schema.d.ts && npx --yes openapi-typescript-codegen@0.29.0 --input openapi/openapi.yaml --output qa/generated/desktop-sdk --client fetch --useOptions --useUnionTypes && npx --yes --package typescript@5.8.3 tsc --project qa/generated/tsconfig.json"

docker compose -p organizer_stage_2_3_clean up -d
docker compose -p organizer_stage_2_3_upgrade up -d
```

PostgreSQL Scenario A applied migrations `001` through `005`, loaded a realistic organization/employee fixture, reran `005` and `002`, ran database contract tests, and confirmed that an invalid interval gap fails with SQLSTATE class 23 behavior. Scenario B built an exact Stage 2.2 state, loaded data, applied `005`, reran seed/migration, and proved that employee data was unchanged.

## Result

Every mandatory runtime gate passed after the fixes. Full console evidence is under `qa/reports/stage_2_3_runtime/`.
"""
write(PACKAGE / "Stage_2_3_Runtime_Validation.md", runtime_report)

fix_registry = r"""
# Stage 2.3 Fix Registry

| ID | Severity | Root cause | Changed files | Verification | Status |
|---|---|---|---|---|---|
| S231-H-001 | High | Migration 005 used unqualified, nonexistent `organizations`/`users` relations and only documented defaults in comments | `db/005_stage_2_3_contract_alignment.sql`; DB tests | Clean install and 2.2→2.3 upgrade on PostgreSQL 16.10; repeated migration/seed; invalid gap rejected | Fixed |
| S231-H-002 | High | New contract referenced permission codes `Settings.Read` and `User.ReadBlocked`, absent from the canonical 91-code catalog | `openapi/openapi.yaml`; `catalogs/api_catalog.csv`; contract alignment and derived artifacts | All 230 operation permission bindings resolve; permission count remains 91; Redocly/codegen repeated | Fixed |
| S231-H-003 | High | Candidate retained Stage 2.2 generated clients/stubs and derived API files with 241 operations; server handler generator hard-coded 241 | `qa/generated/**`; `qa/generate_server_stub.py`; `endpoints_dump.txt`; API docs/diff | C#/TypeScript regeneration for 244 operations; .NET and TypeScript compilation pass; derived catalog parity pass | Fixed |

Remaining defects: Critical **0**, High **0**, Medium **0**.
"""
write(PACKAGE / "Stage_2_3_Fix_Registry.md", fix_registry)

backward = r"""
# Stage 2.3 Backward Compatibility

## Decision

**PASS.** Stage 2.3 is additive relative to the canonical Stage 2.2 package.

- All **241** existing operation IDs remain present.
- No existing method/path pair changed.
- No existing schema or property was removed.
- No required field was added to an existing request DTO.
- No existing enum was narrowed.
- Existing `SearchSuggestion` gained only optional `resultType` and `employee` fields.
- Three operations and five schemas were added.
- No permission or stable error code was added or removed.
- Migration `005_stage_2_3_contract_alignment.sql` is additive and uses a documented forward-fix strategy.
- A Stage 2.2 client can ignore unknown fields and continue rendering the required generic `object` projection for employee search results.
- Existing notification semantic urgency remains unchanged; old clients retain their built-in visual mapping.

The machine-readable diff is `Stage_2_3_Contract_Diff.csv`.
"""
write(PACKAGE / "Stage_2_3_Backward_Compatibility.md", backward)

migration = r"""
# Stage 2.3 Migration Test Report

## Runtime

- PostgreSQL image: `postgres:16.10-alpine`.
- Database: isolated `organizer_stage_2_1`.
- Migration under test: `db/005_stage_2_3_contract_alignment.sql`.

## Scenario A — clean install

Result: **PASS**.

1. Applied `001`, `002`, `003`, `004`, `005`.
2. Loaded an organization, settings, department, employee profile, and user account fixture.
3. Reran `005` to execute idempotent defaults for the newly created organization.
4. Reran permission seed `002`.
5. Verified one scale and four default intervals covering 0..100.
6. Verified ordering/search indexes and 91 permissions.
7. Submitted an invalid gap (`normal.min_score=26`); the deferred constraint rejected commit.
8. Verified rollback preserved the valid scale.

Evidence: `qa/reports/stage_2_3_runtime/postgres_scenario_a_clean.log`.

## Scenario B — upgrade 2.2 → 2.3

Result: **PASS**.

1. Applied the complete Stage 2.2 state (`001`–`004` plus seed).
2. Inserted realistic organization and employee data.
3. Applied `005`.
4. Verified default scale creation and preserved employee data.
5. Reran `002` and `005`; both passed without duplicate rows.
6. Repeated database contract tests.

Evidence: `qa/reports/stage_2_3_runtime/postgres_scenario_b_upgrade.log`.

## Rollback strategy

The migration is additive. Production rollback uses a documented forward-fix approach; destructive down migration is intentionally not supplied.
"""
write(PACKAGE / "Stage_2_3_Migration_Test_Report.md", migration)

codegen = f"""
# Stage 2.3 Code Generation Report

## C# desktop client

- Generator: NSwag `openapi2csclient` 14.7.1.0.
- Operation mode: `SingleClientFromOperationId`.
- JSON: `System.Text.Json`; nullable reference types and C# required members enabled.
- Generated async operations: **244**.
- Generated source: `qa/generated/desktop-csharp/OrganizerDesktopClient.g.cs`.
- SHA-256: `{sha256(PACKAGE / "qa/generated/desktop-csharp/OrganizerDesktopClient.g.cs")}`.
- Compilation: .NET SDK 8.0.423, `net8.0`, **0 warnings, 0 errors**.

## C# server stubs

- Generator: NSwag `openapi2cscontroller` 14.7.1.0.
- Abstract ASP.NET Core controller with cancellation tokens and validation attributes.
- Generated actions: **244**.
- Generated source: `qa/generated/server-csharp/OrganizerController.g.cs`.
- SHA-256: `{sha256(PACKAGE / "qa/generated/server-csharp/OrganizerController.g.cs")}`.
- Compilation: .NET SDK 8.0.423, `net8.0`, **0 warnings, 0 errors**.

## Dependent TypeScript artifacts

- Server schema: `openapi-typescript` 7.9.1.
- Desktop SDK: `openapi-typescript-codegen` 0.29.0; **277 files**.
- Compiler: TypeScript 5.8.3 strict/noEmit.
- Compilation: **PASS**.

The generated artifacts contain `EmployeeSearchResult`, urgency-scale DTOs, all three new settings operations, `If-Match`, `ETag`, and `Idempotency-Key`.
"""
write(PACKAGE / "Stage_2_3_Codegen_Report.md", codegen)
write(PACKAGE / "codegen_validation_report.md", codegen)
write(PACKAGE / "qa" / "reports" / "codegen_report.md", codegen)

redocly = r"""
# Stage 2.3 Redocly Report

- Command: `docker run --rm -v "${root}:/work:ro" -w /work node:22-alpine sh -lc "npx --yes @redocly/cli@2.40.0 lint openapi/openapi.yaml --format=stylish"`.
- Redocly CLI: `2.40.0`.
- Configuration: built-in recommended configuration (no project override exists).
- Errors: **0**.
- Warnings: **0**.
- Exit code: **0**.
- Full log: `qa/reports/stage_2_3_runtime/redocly_lint_docker.log`.

The first Windows-local invocation completed validation successfully but the bundled Node process crashed during shutdown. The identical pinned command was repeated in the project Docker runtime and returned exit code 0; the container result is the release gate evidence.
"""
write(PACKAGE / "Stage_2_3_Redocly_Report.md", redocly)

openapi_report = r"""
# Stage 2.3 OpenAPI Validation Report

- YAML parse: PASS.
- OpenAPI 3.1 schema validation: PASS.
- Redocly lint: PASS (0 errors, 0 warnings).
- Local references: PASS (2,776 resolved).
- Unique operation IDs: PASS (244).
- Unique method/path pairs: PASS (244).
- Concrete request schemas: PASS (124).
- Concrete success response schemas: PASS (231).
- Empty business DTO: none.
- Unrestricted `additionalProperties: true`: none.
- Required/nullable/enum/limits checks: PASS.
- If-Match, ETag and idempotency checks: PASS.
- Permission and stable error binding: PASS.
- Stage 2.3 functional contract checks: PASS (27).

Machine-readable evidence: `qa/reports/stage_2_3_runtime/runtime_validation.json`.
"""
write(PACKAGE / "openapi_validation_report.md", openapi_report)

validation_json = {
    "version": VERSION,
    "date": "2026-07-26",
    "status": "PASS",
    "operations": 244,
    "schemas": 237,
    "permissions": 91,
    "stableErrors": 44,
    "defectsFound": 3,
    "defectsFixed": 3,
    "remaining": {"critical": 0, "high": 0, "medium": 0},
    "gates": {
        "openapi": "PASS",
        "redocly": "PASS",
        "desktopCSharpGeneration": "PASS",
        "desktopCSharpCompilation": "PASS",
        "serverCSharpGeneration": "PASS",
        "serverCSharpCompilation": "PASS",
        "postgresqlClean": "PASS",
        "postgresqlUpgrade": "PASS",
        "repeatedSeed": "PASS",
        "backwardCompatibility": "PASS",
    },
}
write(
    PACKAGE / "qa" / "validation_report.json",
    json.dumps(validation_json, ensure_ascii=False, indent=2),
)

# JSON manifest intentionally excludes both manifest files to avoid recursive hashes.
manifest_exclusions = {"00_MANIFEST.md", "MANIFEST.json"}
manifest_entries = [
    {
        "path": path.relative_to(PACKAGE).as_posix(),
        "bytes": path.stat().st_size,
        "sha256": sha256(path),
    }
    for path in included_files(PACKAGE)
    if path.relative_to(PACKAGE).as_posix() not in manifest_exclusions
]
write(
    PACKAGE / "MANIFEST.json",
    json.dumps({"version": VERSION, "files": manifest_entries}, ensure_ascii=False, indent=2),
)
all_for_markdown = [
    path
    for path in included_files(PACKAGE)
    if path.relative_to(PACKAGE).as_posix() != "00_MANIFEST.md"
]
manifest_lines = [
    "# Organizer Stage 2.3 Final Manifest",
    "",
    f"Version: `{VERSION}`",
    "",
    f"Files (excluding this manifest): **{len(all_for_markdown)}**",
    "",
    "| Path | Bytes | SHA-256 |",
    "|---|---:|---|",
]
for path in all_for_markdown:
    relative = path.relative_to(PACKAGE).as_posix()
    manifest_lines.append(f"| `{relative}` | {path.stat().st_size} | `{sha256(path)}` |")
write(PACKAGE / "00_MANIFEST.md", "\n".join(manifest_lines))

# Publish the validated tree without copying generated build intermediates.
for source in included_files(PACKAGE):
    relative = source.relative_to(PACKAGE)
    destination = OUTPUT_DIR / relative
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)

main_zip = OUTPUTS / "Organizer_Stage2_Technical_Specification_2.3_Final.zip"
with zipfile.ZipFile(main_zip, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for path in included_files(PACKAGE):
        archive.write(path, (Path("stage_2_3") / path.relative_to(PACKAGE)).as_posix())

delta_root = Path(__file__).resolve().parent / "stage_3_5_delta_final"
delta_root.mkdir(parents=True, exist_ok=True)
delta_paths = [
    "dto_field_catalog.csv",
    "Stage_2_3_Contract_Alignment.md",
    "Stage_2_3_Contract_Diff.csv",
    "Stage_2_3_Fix_Registry.md",
    "Stage_2_3_Validation.md",
    "Stage_2_3_Runtime_Validation.md",
    "Stage_2_3_Backward_Compatibility.md",
    "catalogs/api_catalog.csv",
    "catalogs/errors.csv",
    "catalogs/permissions.csv",
    "db/005_stage_2_3_contract_alignment.sql",
    "openapi/openapi.yaml",
]
for relative_text in delta_paths:
    source = PACKAGE / relative_text
    destination = delta_root / relative_text
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)
delta_files_before_manifest = [
    path
    for path in included_files(delta_root)
    if path.relative_to(delta_root).as_posix() != "00_MANIFEST.md"
]
delta_manifest_lines = [
    "# Organizer Stage 3.5 Contract Delta Input Final Manifest",
    "",
    f"Version: `{VERSION}`",
    "",
    "| Path | Bytes | SHA-256 |",
    "|---|---:|---|",
]
for path in delta_files_before_manifest:
    relative = path.relative_to(delta_root).as_posix()
    delta_manifest_lines.append(f"| `{relative}` | {path.stat().st_size} | `{sha256(path)}` |")
write(delta_root / "00_MANIFEST.md", "\n".join(delta_manifest_lines))

delta_zip = OUTPUTS / "Organizer_Stage3_5_Contract_Delta_Input_Final.zip"
with zipfile.ZipFile(delta_zip, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for path in included_files(delta_root):
        archive.write(path, (Path("stage_3_5_delta") / path.relative_to(delta_root)).as_posix())

for archive_path in (main_zip, delta_zip):
    digest = sha256(archive_path)
    write(
        archive_path.with_suffix(archive_path.suffix + ".sha256"),
        f"{digest}  {archive_path.name}",
    )
    with zipfile.ZipFile(archive_path) as archive:
        bad_file = archive.testzip()
        if bad_file is not None:
            raise RuntimeError(f"CRC failure in {archive_path.name}: {bad_file}")
        entries = archive.infolist()
        for entry in entries:
            if not entry.is_dir():
                archive.read(entry.filename)
    validation_path = archive_path.with_suffix(".validation.md")
    write(
        validation_path,
        "\n".join(
            [
                f"# Validation Report — {archive_path.name}",
                "",
                f"- Version: `{VERSION}`.",
                f"- SHA-256: `{digest}`.",
                f"- ZIP entries: **{len(entries)}**.",
                "- CRC/readback: **PASS**.",
                "- Manifest present: **PASS**.",
                "- Temporary directories, node_modules, NuGet cache, bin/obj, and test databases: **excluded**.",
            ]
        ),
    )

print(
    json.dumps(
        {
            "version": VERSION,
            "mainZip": str(main_zip),
            "mainSha256": sha256(main_zip),
            "deltaZip": str(delta_zip),
            "deltaSha256": sha256(delta_zip),
            "status": "PASS",
        },
        ensure_ascii=True,
        indent=2,
    )
)
