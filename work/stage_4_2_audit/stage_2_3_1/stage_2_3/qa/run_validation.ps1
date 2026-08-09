$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$reports = Join-Path $PSScriptRoot "reports"
New-Item -ItemType Directory -Force -Path $reports | Out-Null

$bundledRoot = Join-Path $env:USERPROFILE ".cache\codex-runtimes\codex-primary-runtime\dependencies"
$pythonCandidates = @(
    (Join-Path $bundledRoot "python\python.exe"),
    (Get-Command python -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue)
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
$pnpmCandidates = @(
    (Join-Path $bundledRoot "bin\fallback\pnpm.cmd"),
    (Get-Command pnpm -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue)
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
if ($pythonCandidates.Count -eq 0 -or $pnpmCandidates.Count -eq 0) {
    throw "Python and pnpm are required for validation."
}
$python = $pythonCandidates[0]
$pnpm = $pnpmCandidates[0]
$nodeBin = Join-Path $bundledRoot "node\bin"
$fallbackBin = Join-Path $bundledRoot "bin\fallback"
$env:Path = "$nodeBin;$fallbackBin;$env:Path"
$env:NO_UPDATE_NOTIFIER = "1"
$env:REDOCLY_TELEMETRY = "off"

$pythonPackages = Join-Path $env:TEMP "organizer-stage2-qa-python"
New-Item -ItemType Directory -Force -Path $pythonPackages | Out-Null
& $python -m pip install --disable-pip-version-check --quiet --upgrade --target $pythonPackages -r (Join-Path $PSScriptRoot "requirements.txt")
if ($LASTEXITCODE -ne 0) {
    throw "Python dependency installation failed."
}
$env:PYTHONPATH = $pythonPackages

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Command,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [switch]$Append
    )
    if (-not $Append) {
        Remove-Item -LiteralPath $LogPath -ErrorAction SilentlyContinue
    }
    & $Command 2>&1 | Tee-Object -FilePath $LogPath -Append:$Append
    if ($LASTEXITCODE -ne 0) {
        throw "Validation command failed. See $LogPath"
    }
}

$validationErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
Push-Location $root
try {
    & $python qa\build_openapi.py
    if ($LASTEXITCODE -ne 0) { throw "OpenAPI build failed." }
    & $python qa\sync_artifacts.py
    if ($LASTEXITCODE -ne 0) { throw "Catalog synchronization failed." }
    & $python qa\generate_permission_seed.py
    if ($LASTEXITCODE -ne 0) { throw "Permission seed generation failed." }
    & $python qa\generate_traceability.py
    if ($LASTEXITCODE -ne 0) { throw "Traceability generation failed." }

    $postgresLog = Join-Path $reports "postgresql_validation.log"
    docker compose down --volumes --remove-orphans 2>&1 | Tee-Object -FilePath $postgresLog
    docker compose up -d 2>&1 | Tee-Object -FilePath $postgresLog -Append
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL container start failed." }

    $containerName = "organizer_stage2_technical_specification_21-postgres-1"
    $healthy = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $health = docker inspect --format "{{.State.Health.Status}}" $containerName 2>$null
        if ($health -eq "healthy") {
            $healthy = $true
            break
        }
        Start-Sleep -Seconds 2
    }
    if (-not $healthy) {
        docker compose logs 2>&1 | Tee-Object -FilePath $postgresLog -Append
        throw "PostgreSQL did not become healthy."
    }

    docker compose cp db/. postgres:/migrations/ 2>&1 | Tee-Object -FilePath $postgresLog -Append
    docker compose cp qa/database_contract_tests.sql postgres:/migrations/database_contract_tests.sql 2>&1 | Tee-Object -FilePath $postgresLog -Append
    foreach ($migration in @(
        "001_initial_schema.sql",
        "002_seed_authorization.sql",
        "003_audit_corrections.sql",
        "004_stage_2_1_foundation.sql"
    )) {
        "APPLY=$migration" | Tee-Object -FilePath $postgresLog -Append
        docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U organizer_migrator -d organizer_stage_2_1 -f "/migrations/$migration" 2>&1 |
            Tee-Object -FilePath $postgresLog -Append
        if ($LASTEXITCODE -ne 0) { throw "Migration $migration failed." }
    }

    "RERUN=002_seed_authorization.sql" | Tee-Object -FilePath $postgresLog -Append
    docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U organizer_migrator -d organizer_stage_2_1 -f "/migrations/002_seed_authorization.sql" 2>&1 |
        Tee-Object -FilePath $postgresLog -Append
    if ($LASTEXITCODE -ne 0) { throw "Idempotent permission seed rerun failed." }

    "RUN=database_contract_tests.sql" | Tee-Object -FilePath $postgresLog -Append
    docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U organizer_migrator -d organizer_stage_2_1 -f "/migrations/database_contract_tests.sql" 2>&1 |
        Tee-Object -FilePath $postgresLog -Append
    if ($LASTEXITCODE -ne 0) { throw "Database contract tests failed." }

    $concurrencyLog = Join-Path $reports "concurrency_validation.log"
    Invoke-Checked -LogPath $concurrencyLog -Command {
        & $python qa\concurrency_tests.py
    }

    $artifactLog = Join-Path $reports "artifact_validation.log"
    Invoke-Checked -LogPath $artifactLog -Command {
        & $python qa\validate_artifacts.py
    }
    Invoke-Checked -LogPath $artifactLog -Append -Command {
        & $python -m openapi_spec_validator openapi\openapi.yaml
    }

    $lintLog = Join-Path $reports "openapi_lint.log"
    Invoke-Checked -LogPath $lintLog -Command {
        & $pnpm --package=@redocly/cli@2.40.0 dlx redocly lint openapi/openapi.yaml --format=stylish
    }

    $desktopSdk = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "generated\desktop-sdk"))
    if (-not $desktopSdk.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean codegen output outside package root."
    }
    if (Test-Path -LiteralPath $desktopSdk) {
        Remove-Item -LiteralPath $desktopSdk -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $desktopSdk | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot "generated\server-contract") | Out-Null

    $codegenLog = Join-Path $reports "codegen_validation.log"
    "RUN=server-contract openapi-typescript@7.9.1" | Tee-Object -FilePath $codegenLog
    Invoke-Checked -LogPath $codegenLog -Append -Command {
        & $pnpm dlx openapi-typescript@7.9.1 openapi/openapi.yaml -o qa/generated/server-contract/schema.d.ts
    }
    "PASS=server-contract" | Tee-Object -FilePath $codegenLog -Append
    "RUN=server-stub generator" | Tee-Object -FilePath $codegenLog -Append
    Invoke-Checked -LogPath $codegenLog -Append -Command {
        & $python qa\generate_server_stub.py
    }
    "PASS=server-stub" | Tee-Object -FilePath $codegenLog -Append
    "RUN=desktop-sdk openapi-typescript-codegen@0.29.0" | Tee-Object -FilePath $codegenLog -Append
    Invoke-Checked -LogPath $codegenLog -Append -Command {
        & $pnpm dlx openapi-typescript-codegen@0.29.0 --input openapi/openapi.yaml --output qa/generated/desktop-sdk --client fetch --useOptions --useUnionTypes
    }
    "PASS=desktop-sdk" | Tee-Object -FilePath $codegenLog -Append
    "RUN=typescript-strict-compile TypeScript@5.8.3" | Tee-Object -FilePath $codegenLog -Append
    Invoke-Checked -LogPath $codegenLog -Append -Command {
        & $pnpm --package=typescript@5.8.3 dlx tsc --project qa/generated/tsconfig.json
    }
    "PASS=typescript-strict-compile" | Tee-Object -FilePath $codegenLog -Append

    $inventoryQuery = @"
SELECT json_build_object(
  'postgresqlVersion', current_setting('server_version'),
  'schemas', (SELECT count(*) FROM pg_namespace WHERE nspname IN ('core','org','iam','projects','work','calendar','files','crm','collab','notify','governance','sync','search','ops')),
  'tablesAndPartitions', (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname IN ('core','org','iam','projects','work','calendar','files','crm','collab','notify','governance','sync','search','ops') AND c.relkind IN ('r','p')),
  'views', (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname IN ('core','org','iam','projects','work','calendar','files','crm','collab','notify','governance','sync','search','ops') AND c.relkind='v'),
  'indexes', (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname IN ('core','org','iam','projects','work','calendar','files','crm','collab','notify','governance','sync','search','ops') AND c.relkind='i'),
  'triggers', (SELECT count(*) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname IN ('core','org','iam','projects','work','calendar','files','crm','collab','notify','governance','sync','search','ops') AND NOT t.tgisinternal),
  'permissions', (SELECT count(*) FROM iam.permissions),
  'systemRoles', (SELECT count(*) FROM iam.roles WHERE is_system),
  'projectRoles', (SELECT count(*) FROM projects.project_roles WHERE is_system)
);
"@
    $inventory = docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U organizer_migrator -d organizer_stage_2_1 -Atc $inventoryQuery
    if ($LASTEXITCODE -ne 0) { throw "Schema inventory query failed." }
    $inventory | Set-Content -LiteralPath (Join-Path $reports "postgresql_schema_inventory.json") -Encoding utf8

    $codegenReport = @"
# OpenAPI code generation report

- OpenAPI: `openapi/openapi.yaml`.
- Server contract generator: `openapi-typescript 7.9.1`.
- Server output: `qa/generated/server-contract/schema.d.ts` plus generated typed `handlers.ts` stub for 241 operation IDs.
- Desktop SDK generator: `openapi-typescript-codegen 0.29.0`, Fetch client.
- Desktop output files: $((Get-ChildItem -LiteralPath $desktopSdk -File -Recurse).Count).
- Compiler: `TypeScript 5.8.3`, strict mode, no emit.
- Compile result: pass.
- Full command log: `qa/reports/codegen_validation.log`.
"@
    $codegenReport | Set-Content -LiteralPath (Join-Path $reports "codegen_report.md") -Encoding utf8

    "STAGE_2_1_VALIDATION_PASSED" | Tee-Object -FilePath (Join-Path $reports "validation_summary.log")

    & $python qa\generate_manifest.py
    if ($LASTEXITCODE -ne 0) { throw "Manifest generation failed." }
}
finally {
    Pop-Location
    $ErrorActionPreference = $validationErrorActionPreference
}
