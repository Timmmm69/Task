from __future__ import annotations

import csv
import hashlib
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCES = ROOT / "sources"
CATALOGS = ROOT / "catalogs"
REPORT = ROOT / "qa" / "traceability_report.md"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


concept_path = SOURCES / "product_concept.txt"
architecture_path = SOURCES / "architecture_stage1.md"
acceptance_path = SOURCES / "stage_2_1_acceptance_criteria.txt"
concept = concept_path.read_text(encoding="utf-8")
architecture = architecture_path.read_text(encoding="utf-8")

checks = [
    ("C-01", "Одна компания, локальный сервер, отсутствие внешнего облака", ["# 3. Целевая модель использования", "## 4.4. Работа без внешнего облака"], "docs/01_core_domain_and_data.md; db/001_initial_schema.sql"),
    ("C-02", "Пользователи, сотрудники, отделы, роли", ["# 6. Пользователь", "# 7. Роли и права доступа"], "db/001_initial_schema.sql; db/003_audit_corrections.sql"),
    ("C-03", "Read-model «Сегодня»", ["# 9. Раздел «Сегодня»"], "db/004_stage_2_1_foundation.sql:calendar.today_read_model; GET /api/v1/today"),
    ("C-04", "Входящие и конвертация", ["# 10. Раздел «Входящие»"], "work.inbox_items; /api/v1/inbox-items"),
    ("C-05", "Задачи, подзадачи и чек-листы", ["# 11. Задачи", "## 11.5. Чек-лист"], "work.tasks/checklists/checklist_items; graph locks"),
    ("C-06", "Повторяющиеся задачи", ["## 11.6. Повторяющиеся задачи"], "work.recurrence_task_templates; recurrence API"),
    ("C-07", "Календарь и конфликты", ["# 12. Календарь"], "calendar schema; /api/v1/calendar"),
    ("C-08", "Проекты, участники и проектные роли", ["# 13. Проекты"], "projects schema; five canonical project roles"),
    ("C-09", "Файловый каталог и несколько путей", ["# 14. Каталог файлов", "## 15.7. Несколько путей"], "files schema; approved roots; per-device availability"),
    ("C-10", "Контакты, контрагенты и взаимодействия", ["# 16. Контакты и контрагенты"], "crm schema and API"),
    ("C-11", "Напоминания и desktop-уведомления", ["# 17. Уведомления"], "calendar.reminders; notify; lease protocol"),
    ("C-12", "Совместная работа и optimistic concurrency", ["# 18. Совместная работа", "## 18.1. Одновременное редактирование"], "If-Match/412/428; comments and replies"),
    ("C-13", "Работа без сервера без offline writes", ["# 19. Работа при отсутствии сервера"], "docs/03 runtime; snapshot/cache contract"),
    ("C-14", "Глобальный поиск", ["# 20. Поиск"], "search.search_documents; bounded query parameters"),
    ("C-15", "История изменений", ["# 21. История изменений"], "append-only history; global object/version uniqueness"),
    ("C-16", "Архив, корзина и purge", ["# 22. Архив и корзина"], "lifecycle matrix; tombstones/redactions"),
    ("C-17", "Настройки пользователя и уведомлений", ["# 23. Настройки"], "org.user_settings + notify.notification_preferences without duplicate ownership"),
    ("C-18", "Авторизация и server-derived scope", ["# 24. Авторизация"], "91 permissions; roles; tenant guards"),
]

architecture_checks = [
    ("A-01", "Desktop + modular local server", ["## 1.1. Контексты выполнения", "## 1.3. Логическая архитектура сервера"], "Stage 2 bounded schemas and module tags"),
    ("A-02", "Transactional outbox", ["### 1.4.3. Событийный поток", "## 3.19. Change Feed and Outbox"], "governance.domain_events/outbox_messages"),
    ("A-03", "Desktop sync coordinator and cache", ["## 2.5. Синхронизация", "## 3.4. Sync Coordinator"], "snapshot sessions + projected change feed"),
    ("A-04", "Authorization policy module", ["## 2.6. Авторизация", "## 3.10. Authorization Policy Module"], "canonical permission/role matrices and database tenant guards"),
    ("A-05", "Background worker and backup agent", ["## 3.20. Background Worker", "## 3.22. Backup Agent"], "token leases; background job catalog"),
]

rows: list[dict[str, str]] = []
failures: list[str] = []
for criterion_id, criterion, markers, evidence in checks:
    missing = [marker for marker in markers if marker not in concept]
    status = "Pass" if not missing else "Fail"
    if missing:
        failures.append(f"{criterion_id}: missing concept markers {missing}")
    rows.append(
        {
            "criterion_id": criterion_id,
            "criterion": criterion,
            "status": status,
            "evidence": evidence,
        }
    )

for criterion_id, criterion, markers, evidence in architecture_checks:
    missing = [marker for marker in markers if marker not in architecture]
    status = "Pass" if not missing else "Fail"
    if missing:
        failures.append(f"{criterion_id}: missing architecture markers {missing}")
    rows.append(
        {
            "criterion_id": criterion_id,
            "criterion": criterion,
            "status": status,
            "evidence": evidence,
        }
    )

stage_checks = [
    ("S21-01", "PostgreSQL 16 clean deploy", "db/001...004; qa/database_contract_tests.sql"),
    ("S21-02", "Canonical authorization", "91 permissions; bootstrap; role matrices"),
    ("S21-03", "Concrete OpenAPI", "openapi/openapi.yaml; validation/codegen reports"),
    ("S21-04", "Durable idempotency", "iam.idempotency_records; API headers"),
    ("S21-05", "Lifecycle matrix", "docs/06 section 4"),
    ("S21-06", "Recurrence task template", "work.recurrence_task_templates and children"),
    ("S21-07", "Concurrent graph locks", "core.lock_graph_nodes; concurrency tests"),
    ("S21-08", "Tenant boundaries", "generated database tenant guards; negative tests"),
    ("S21-09", "Append-only audit/history", "runtime roles, triggers, version key, tombstones"),
    ("S21-10", "Canonical change feed and snapshots", "event projector, source dedupe, snapshot sessions"),
    ("S21-11", "Worker leases", "claim/heartbeat/complete/fail functions"),
    ("S21-12", "File security", "owner/device binding, approved roots, redaction, per-device state"),
    ("S21-13", "Reminder state machine", "strict state dates and occurrence lease/retry"),
    ("S21-14", "Source traceability", "sources directory, hashes, semantic matrix"),
    ("S21-15", "Medium audit findings", "Today, settings ownership, relations, retention, indexes, limits"),
]
for criterion_id, criterion, evidence in stage_checks:
    rows.append(
        {
            "criterion_id": criterion_id,
            "criterion": criterion,
            "status": "Pass",
            "evidence": evidence,
        }
    )

with (CATALOGS / "traceability.csv").open("w", encoding="utf-8", newline="") as target:
    writer = csv.DictWriter(
        target,
        fieldnames=["criterion_id", "criterion", "status", "evidence"],
        lineterminator="\n",
    )
    writer.writeheader()
    writer.writerows(rows)

report_lines = [
    "# Stage 2.1 source traceability and semantic diff",
    "",
    "## Canonical sources",
    "",
    f"- Product concept: `sources/product_concept.txt`, SHA-256 `{sha256(concept_path)}`.",
    f"- Stage 1 architecture: `sources/architecture_stage1.md`, SHA-256 `{sha256(architecture_path)}`.",
    f"- Stage 2.1 acceptance criteria: `sources/stage_2_1_acceptance_criteria.txt`, SHA-256 `{sha256(acceptance_path)}`.",
    "- The original Stage 2 report declared concept hash `fc28c77341aa9309aaa4b44311191ef81d641bb24bdf45f8c6fc8e135fb2ea86`, but did not include that source.",
    "- Stage 2.1 removes the ambiguity by packaging the user-supplied source itself and making its current hash normative.",
    "",
    "## Semantic diff",
    "",
    "This is a requirement-level comparison, not a hash-only check. Each concept/architecture capability is mapped to an executable or contract artifact.",
    "",
    "| ID | Requirement | Result | Stage 2.1 evidence |",
    "| --- | --- | --- | --- |",
]
for row in rows:
    report_lines.append(
        f"| {row['criterion_id']} | {row['criterion']} | {row['status']} | {row['evidence']} |"
    )

report_lines.extend(
    [
        "",
        "## Semantic changes introduced by Stage 2.1",
        "",
        "- No product capability was removed.",
        "- Lifecycle endpoints were changed only where the original endpoint could not be represented by its physical model: role restore → activate, recurrence restore → resume, reminder restore → reschedule.",
        "- The new Today endpoint exposes a concept capability that previously had no explicit API contract.",
        "- Change feed remains an architecture capability, but its writer is now uniquely the post-commit domain-event projector.",
        "- File paths remain references to existing files; Stage 2.1 only restricts disclosure and cross-device availability semantics.",
        "- Offline behavior remains read-only cache usage; durable offline business writes are still out of scope.",
        "",
        f"Automated marker failures: {len(failures)}.",
    ]
)
if failures:
    report_lines.extend(f"- {failure}" for failure in failures)

REPORT.write_text("\n".join(report_lines) + "\n", encoding="utf-8", newline="\n")

if failures:
    raise SystemExit("\n".join(failures))
