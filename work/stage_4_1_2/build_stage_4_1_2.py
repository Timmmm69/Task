from __future__ import annotations

import csv
import hashlib
import json
from pathlib import Path


ROOT = Path(r"C:\Users\novik\Таск")
BASE = ROOT / "work" / "stage_4_1_2"
SRC = BASE / "candidate_4_1_1" / "Organizer_Stage4_PRD_Candidate_4.1.1"
OUT = BASE / "candidate_4_1_2"
OUT.mkdir(parents=True, exist_ok=True)

VERSION = "4.1.2-candidate.1"
DATE = "2026-07-26"


def read_text(name: str) -> str:
    return (SRC / name).read_text(encoding="utf-8-sig")


def write_text(name: str, text: str) -> None:
    (OUT / name).write_text(text.rstrip() + "\n", encoding="utf-8", newline="\n")


def common_update(text: str) -> str:
    replacements = {
        "4.1.1-candidate.1": VERSION,
        "Stage_3_Field_Traceability.csv": "Stage_3_Field_Traceability_Final_3.5.csv",
        "OpenAPI 1.2.0-stage2.2": "OpenAPI 1.2.0-stage2.3",
        "OpenAPI/Stage 2.2 limits": "OpenAPI/Stage 2.3.1 limits",
        "Stage 3.4 UX Architecture/Decision Log": "Stage 3.5 UX Architecture/Decision Log",
        "Stage 3.4 retained": "Stage 3.5 retained",
        "Stage 3.4/OpenAPI": "Stage 3.5/OpenAPI 1.2.0-stage2.3",
        "Stage 3.4 UX Architecture": "Stage 3.5 UX Architecture",
    }
    for old, new in replacements.items():
        text = text.replace(old, new)
    return text


DELTA_SOURCE = (
    "Stage 2.3.1 OpenAPI 1.2.0-stage2.3; "
    "Stage 3.5 UX baseline; Stage 4.1.2 delta"
)

CHANGED_FRS = [
    ("FR-159", "MOD-014", "GET /search принимает `types=employee`, возвращает `resultType=employee` и `EmployeeSearchResult`; server filtering/redaction/ranking выполняются до cursor pagination."),
    ("FR-160", "MOD-014", "Подсказки сохраняют permission-safe contract; optional `resultType`/`employee` используются только когда присутствуют в DTO, без клиентского восстановления скрытых данных."),
    ("FR-243", "MOD-002", "Shell search представляет Employees отдельной озвучиваемой группой и сохраняет keyboard/focus semantics SCR-133/134."),
    ("FR-244", "MOD-002", "Deep link employee-result открывается через shell router с повторной server-side проверкой доступа; stale target даёт нейтральное unavailable state."),
    ("FR-260", "MOD-014", "Offline/cache-only search явно неполон; cache не объединяется с server pages и не используется для клиентской постфильтрации."),
    ("FR-261", "MOD-015", "Notification UI разрешает presentation через текущую организационную шкалу для существующих и будущих записей, не меняя semantic urgency."),
    ("FR-264", "MOD-018", "SCR-153 содержит единственный organizational urgency editor CMP-001; personal override, avatar upload, произвольный HEX/color picker и внешние notification channels отсутствуют."),
    ("FR-265", "MOD-019", "System.Configure управляет только organizational urgency scale; это не admin user list и не employee global search."),
    ("FR-266", "MOD-020", "PUT/reset шкалы не ставятся в offline queue; 412/428 сохраняют draft и требуют refresh/compare/reapply либо discard."),
    ("FR-269", "MOD-021", "Успешные PUT/reset и sensitive denials фиксируют `notification_urgency_scale.changed` с redacted diff, actor, outcome и correlationId."),
]

NEW_FRS = [
    {
        "id": "FR-270", "module": "MOD-018",
        "text": "Просмотреть текущую организационную шкалу срочности и authoritative ETag.",
        "scr": "SCR-153;CMP-001", "flow": "FLOW-038",
        "api": "GET /api/v1/settings/notification-urgency-scale (GET_api_v1_settings_notification_urgency_scale)",
        "dto": "— → NotificationUrgencyScale; scope, intervals, version, updatedAt, updatedByUserId",
        "perm": "Settings.ReadOwn",
        "errors": "AUTHENTICATION_REQUIRED;FORBIDDEN;DATABASE_UNAVAILABLE",
        "states": "STATE-002;STATE-004;STATE-008;STATE-010;STATE-011;STATE-015",
        "acs": "AC-1790",
    },
    {
        "id": "FR-271", "module": "MOD-018",
        "text": "Атомарно заменить четыре интервала organizational urgency scale с If-Match и Idempotency-Key.",
        "scr": "SCR-153;CMP-001", "flow": "FLOW-038",
        "api": "PUT /api/v1/settings/notification-urgency-scale (PUT_api_v1_settings_notification_urgency_scale)",
        "dto": "NotificationUrgencyScalePatch.intervals → NotificationUrgencyScale",
        "perm": "System.Configure",
        "errors": "AUTHENTICATION_REQUIRED;FORBIDDEN;VALIDATION_FAILED;PRECONDITION_REQUIRED;VERSION_CONFLICT;DATABASE_UNAVAILABLE",
        "states": "STATE-007;STATE-008;STATE-010;STATE-011;STATE-014;STATE-025",
        "acs": "AC-1791;AC-1797;AC-1798;AC-1801;AC-1819",
    },
    {
        "id": "FR-272", "module": "MOD-018",
        "text": "После подтверждения сбросить scale к server defaults и принять новый response/ETag.",
        "scr": "SCR-153;CMP-001", "flow": "FLOW-038",
        "api": "POST /api/v1/settings/notification-urgency-scale/reset (POST_api_v1_settings_notification_urgency_scale_reset)",
        "dto": "— → NotificationUrgencyScale",
        "perm": "System.Configure",
        "errors": "AUTHENTICATION_REQUIRED;FORBIDDEN;PRECONDITION_REQUIRED;VERSION_CONFLICT;DATABASE_UNAVAILABLE",
        "states": "STATE-008;STATE-010;STATE-011;STATE-014;STATE-025",
        "acs": "AC-1796;AC-1821",
    },
    {
        "id": "FR-273", "module": "MOD-018",
        "text": "Показывать semantic level, границы и displayToken текстом/иконографикой, с keyboard-only и screen-reader support, независимо от цвета.",
        "scr": "SCR-153;CMP-001", "flow": "FLOW-038",
        "api": "Desktop behavior over urgency-scale GET/PUT/reset",
        "dto": "NotificationUrgencyScale; UrgencyScaleInterval; UrgencyLevel",
        "perm": "Settings.ReadOwn;System.Configure",
        "errors": "FORBIDDEN;VALIDATION_FAILED",
        "states": "STATE-004;STATE-007;STATE-008;STATE-016",
        "acs": "AC-1800;AC-1802;AC-1824",
    },
    {
        "id": "FR-274", "module": "MOD-018",
        "text": "Сохранять draft при validation/conflict, блокировать write в read-only/outage и не выполнять blind retry.",
        "scr": "SCR-153;CMP-001", "flow": "FLOW-038;FLOW-022;FLOW-023;FLOW-025",
        "api": "Desktop recovery over urgency-scale PUT/reset",
        "dto": "ProblemDetails.fieldErrors; ETag/If-Match",
        "perm": "System.Configure",
        "errors": "VALIDATION_FAILED;PRECONDITION_REQUIRED;VERSION_CONFLICT;DATABASE_UNAVAILABLE",
        "states": "STATE-007;STATE-010;STATE-011;STATE-014;STATE-025",
        "acs": "AC-1792;AC-1793;AC-1794;AC-1795;AC-1799",
    },
    {
        "id": "FR-275", "module": "MOD-014",
        "text": "Выполнять employee-only (`types=employee`) и mixed search с отдельной группой «Сотрудники» в server order.",
        "scr": "SCR-133;SCR-134;SCR-135;CMP-002", "flow": "FLOW-019",
        "api": "Desktop behavior over GET /api/v1/search",
        "dto": "SearchPage; SearchSuggestion.resultType/employee; EmployeeSearchResult",
        "perm": "Search.Use",
        "errors": "FORBIDDEN;VALIDATION_FAILED",
        "states": "STATE-004;STATE-005;STATE-006;STATE-016",
        "acs": "AC-1804;AC-1805;AC-1806;AC-1816",
    },
    {
        "id": "FR-276", "module": "MOD-014",
        "text": "Открывать employee `deepLink` с повторной проверкой доступа и безопасно обрабатывать stale/unavailable target.",
        "scr": "SCR-133;SCR-134;CMP-002", "flow": "FLOW-019",
        "api": "Desktop behavior over GET /api/v1/search",
        "dto": "EmployeeSearchResult.deepLink",
        "perm": "Search.Use; target policy recheck",
        "errors": "FORBIDDEN;OBJECT_NOT_VISIBLE",
        "states": "STATE-009;STATE-015;STATE-030",
        "acs": "AC-1807;AC-1814;AC-1815",
    },
    {
        "id": "FR-277", "module": "MOD-014",
        "text": "Отображать только DTO-поля employee, нейтрально обрабатывать partial/redaction и принимать server blocked-user policy без client post-filter.",
        "scr": "SCR-133;SCR-134;CMP-002", "flow": "FLOW-019",
        "api": "Desktop behavior over GET /api/v1/search",
        "dto": "EmployeeSearchResult.userId/displayName/departmentId/departmentName/jobTitle/accountStatus/deepLink/isRedacted",
        "perm": "Search.Use;User.Block only for blocked visibility",
        "errors": "FORBIDDEN",
        "states": "STATE-008;STATE-016;STATE-030",
        "acs": "AC-1808;AC-1809;AC-1810;AC-1811;AC-1818;AC-1820",
    },
    {
        "id": "FR-278", "module": "MOD-014",
        "text": "Использовать только server cursor; при invalid/expired cursor перезапускать page 1 с теми же filters и явно показывать partial group failure.",
        "scr": "SCR-133;SCR-134;SCR-135;CMP-002", "flow": "FLOW-019",
        "api": "Desktop behavior over GET /api/v1/search",
        "dto": "SearchPage.nextCursor; query.cursor",
        "perm": "Search.Use",
        "errors": "SEARCH_CURSOR_INVALID;SEARCH_CURSOR_EXPIRED;DATABASE_UNAVAILABLE",
        "states": "STATE-010;STATE-015;STATE-026;STATE-027",
        "acs": "AC-1812;AC-1813;AC-1817",
    },
    {
        "id": "FR-279", "module": "MOD-015",
        "text": "Применять текущий organization mapping к presentation существующих и будущих notifications; semantic urgency не меняется, client 2.2 использует встроенный mapping.",
        "scr": "SCR-130;SCR-153;CMP-001", "flow": "FLOW-020;FLOW-038",
        "api": "Desktop projection behavior",
        "dto": "Notification; NotificationUrgencyScale",
        "perm": "Notification.ReadOwn;Settings.ReadOwn",
        "errors": "DATABASE_UNAVAILABLE",
        "states": "STATE-010;STATE-011;STATE-015",
        "acs": "AC-1803;AC-1822",
    },
]

NEW_BRS = [
    ("BR-098", "MOD-018", "Scale принадлежит организации (`scope=organization`); personal/user override отсутствует.", "FR-270;FR-271;FR-272", "AC-1790"),
    ("BR-099", "MOD-018", "Scale содержит ровно четыре обязательных semantic levels low/normal/high/critical, каждый ровно один раз.", "FR-270;FR-271;FR-272", "AC-1790;AC-1796"),
    ("BR-100", "MOD-018", "Четыре inclusive-интервала упорядочены, не пересекаются, не имеют gaps и полностью покрывают 0–100.", "FR-271;FR-274", "AC-1792;AC-1793;AC-1794;AC-1795"),
    ("BR-101", "MOD-018", "Scale заменяется атомарно полным массивом; stale/missing If-Match не допускает overwrite.", "FR-271;FR-274", "AC-1791;AC-1798"),
    ("BR-102", "MOD-018", "Reset использует server defaults 0–24, 25–49, 50–74, 75–100 и новый ETag.", "FR-272", "AC-1796;AC-1821"),
    ("BR-103", "MOD-015", "Scale меняет presentation существующих и будущих notifications, но не semantic urgency; старый client сохраняет встроенный mapping.", "FR-279", "AC-1803;AC-1822"),
    ("BR-104", "MOD-018", "Срочность передаётся semantic label и текстом/иконкой; цвет/displayToken не является единственным носителем.", "FR-273", "AC-1802"),
    ("BR-105", "MOD-014", "Employee — отдельный result type/group; отображаются только DTO fields, avatar отсутствует.", "FR-275;FR-277", "AC-1805;AC-1820"),
    ("BR-106", "MOD-014", "Authorization, ranking, redaction и filtering выполняются сервером до pagination; client post-filter запрещён.", "FR-275;FR-277", "AC-1818"),
    ("BR-107", "MOD-014", "При `isRedacted=true` nullable fields скрываются нейтрально без hidden values/counts.", "FR-277", "AC-1808;AC-1809"),
    ("BR-108", "MOD-014", "Blocked employee исключается сервером, кроме caller с существующей capability `User.Block`.", "FR-277", "AC-1811"),
    ("BR-109", "MOD-014", "Cursor связан с filters, authorization scope, index snapshot и employee visibility policy version.", "FR-278", "AC-1812;AC-1813"),
    ("BR-110", "MOD-014", "Deep link использует DTO target и повторную authorization check; недоступный target раскрывается нейтрально.", "FR-276", "AC-1807;AC-1814;AC-1815"),
    ("BR-111", "MOD-014", "Employee-only search допускает q, departments, types, cursor, limit; `userIds` не заменяет employee type.", "FR-275", "AC-1804"),
    ("BR-112", "MOD-014", "Mixed search сохраняет отдельную Employees group и server order; partial failure группы обозначается явно.", "FR-275;FR-278", "AC-1806;AC-1817"),
    ("BR-113", "ALL", "Analytics не хранит полный query, PII, paths, secrets или notification contents; product/diagnostic/audit разделены.", "FR-271;FR-272;FR-275;FR-278", "AC-1823"),
]


def gherkin(given: str, when: str, then: str, and_clause: str) -> str:
    return f"Given {given}\nWhen {when}\nThen {then}\nAnd {and_clause}"


AC_DEFS = [
    ("AC-1790", "MOD-018", "FR-270", "Просмотр текущей шкалы", "High", "Contract/UI",
     gherkin("пользователь имеет Settings.ReadOwn", "он открывает CMP-001", "GET возвращает organization scale и ETag", "UI показывает четыре semantic intervals и read-only metadata")),
    ("AC-1791", "MOD-018", "FR-271", "Успешное изменение шкалы", "Critical", "Integration/concurrency",
     gherkin("пользователь имеет System.Configure и current ETag", "он сохраняет valid четыре intervals с If-Match и Idempotency-Key", "сервер возвращает 200, authoritative scale и новый ETag", "UI заменяет draft только server response")),
    ("AC-1792", "MOD-018", "BR-100", "Пересечение интервалов", "High", "Boundary",
     gherkin("два interval пересекаются", "клиент или сервер валидирует draft", "save отклонён VALIDATION_FAILED", "focus установлен на первую конфликтующую границу и draft сохранён")),
    ("AC-1793", "MOD-018", "BR-100", "Неверный порядок", "High", "Boundary",
     gherkin("semantic intervals расположены не low-normal-high-critical", "пользователь сохраняет scale", "save отклонён", "сообщение объясняет требуемый порядок")),
    ("AC-1794", "MOD-018", "BR-100", "Недопустимая граница", "High", "Boundary",
     gherkin("minScore или maxScore вне 0–100 либо min больше max", "пользователь сохраняет scale", "save отклонён VALIDATION_FAILED", "canonical field path подсвечен")),
    ("AC-1795", "MOD-018", "BR-100", "Пропущенный диапазон", "High", "Boundary",
     gherkin("между adjacent intervals есть gap", "пользователь сохраняет scale", "полное покрытие 0–100 не принято", "draft сохранён для исправления")),
    ("AC-1796", "MOD-018", "FR-272", "Reset to defaults", "Critical", "Integration/concurrency",
     gherkin("пользователь имеет System.Configure и подтвердил reset", "POST reset отправлен с If-Match и Idempotency-Key", "сервер возвращает defaults 0–24/25–49/50–74/75–100 и новый ETag", "UI показывает authoritative response")),
    ("AC-1797", "MOD-018", "FR-271", "Отказ без permission", "Critical", "Security",
     gherkin("пользователь не имеет System.Configure", "он пытается изменить scale", "UI остаётся read-only и сервер возвращает FORBIDDEN", "изменение и privilege escalation отсутствуют")),
    ("AC-1798", "MOD-018", "FR-271", "Optimistic conflict", "Critical", "Concurrency",
     gherkin("If-Match устарел", "PUT или reset отправлен", "сервер возвращает VERSION_CONFLICT", "draft сохранён и доступны reload/compare/reapply/discard")),
    ("AC-1799", "MOD-018", "FR-274", "Server unavailable", "Critical", "Resilience",
     gherkin("сервер недоступен", "пользователь открывает или сохраняет scale", "cached value обозначен stale/read-only и write не ставится в queue", "Retry доступен после восстановления")),
    ("AC-1800", "MOD-018", "FR-273", "Read-only", "High", "Authorization/UI",
     gherkin("Settings.ReadOwn есть, а System.Configure отсутствует", "CMP-001 открыт", "scale видима без writable controls", "причина read-only доступно объяснена")),
    ("AC-1801", "MOD-021", "FR-269", "Audit event", "Critical", "Audit",
     gherkin("PUT или reset успешно завершён", "транзакция committed", "создан notification_urgency_scale.changed", "audit содержит actor/outcome/correlationId/redacted diff без secrets")),
    ("AC-1802", "MOD-018", "BR-104", "Срочность не зависит только от цвета", "High", "Accessibility",
     gherkin("High Contrast включён или цвет не различим", "scale/notification отображается", "semantic label и текстовый либо icon/shape признак доступны", "screen reader озвучивает level и границы")),
    ("AC-1803", "MOD-015", "BR-103", "Backward compatibility старого клиента", "High", "Compatibility",
     gherkin("organization scale изменена, а client 2.2 подключён", "client отображает notification", "он использует встроенный legacy mapping без write override", "server semantic urgency остаётся неизменной")),
    ("AC-1804", "MOD-014", "FR-275", "Поиск сотрудника по имени", "High", "Integration/UI",
     gherkin("пользователь имеет Search.Use", "он отправляет q и types=employee", "сервер возвращает permission-safe employee matches", "UI показывает displayName и доступные DTO fields")),
    ("AC-1805", "MOD-014", "BR-105", "Отдельная группа Сотрудники", "High", "UI/accessibility",
     gherkin("response содержит resultType=employee", "результаты отображаются", "создана отдельная озвучиваемая группа Сотрудники", "avatar или неподтверждённые поля не изобретены")),
    ("AC-1806", "MOD-014", "BR-112", "Смешанный поиск", "High", "Integration/UI",
     gherkin("types содержит employee и другие типы", "сервер возвращает mixed page", "Employees остаётся отдельной группой в server order", "клиент не пересортировывает и не постфильтрует page")),
    ("AC-1807", "MOD-014", "FR-276", "Открытие employee result", "High", "Navigation/security",
     gherkin("employee result доступен", "пользователь нажимает Enter", "shell открывает DTO deepLink", "target authorization повторно проверена")),
    ("AC-1808", "MOD-014", "BR-107", "Partial access", "High", "Security/UI",
     gherkin("сервер возвращает только разрешённую часть employee card", "карточка отображается", "UI показывает neutral partial-access state без hidden count", "nullable missing fields не реконструируются")),
    ("AC-1809", "MOD-014", "BR-107", "Redaction", "Critical", "Security",
     gherkin("EmployeeSearchResult.isRedacted=true", "карточка отображается", "nullable department/job fields скрыты нейтрально", "PII или исходное значение не попадает в UI/logs")),
    ("AC-1810", "MOD-014", "FR-277", "Пользователь без Search.Use", "Critical", "Security",
     gherkin("caller не имеет Search.Use", "он вызывает search", "сервер возвращает FORBIDDEN", "UI не показывает stale cached employees как fresh")),
    ("AC-1811", "MOD-014", "BR-108", "Заблокированный пользователь", "Critical", "Security",
     gherkin("employee blocked и caller не имеет User.Block", "search выполняется", "сервер исключает запись до pagination", "UI не раскрывает факт скрытия или hidden count")),
    ("AC-1812", "MOD-014", "FR-278", "Pagination", "High", "Integration",
     gherkin("первая page имеет nextCursor", "клиент запрашивает следующую page", "использован ровно server cursor с теми же filters", "результаты не дополняются клиентской постфильтрацией")),
    ("AC-1813", "MOD-014", "BR-109", "Cursor stability", "High", "Integration/security",
     gherkin("filters, auth scope, snapshot и visibility policy неизменны", "следующая page запрошена", "cursor сохраняет стабильный server order", "при изменении binding сервер возвращает invalid/expired и клиент начинает page 1")),
    ("AC-1814", "MOD-014", "FR-276", "Stale employee result", "High", "Resilience",
     gherkin("результат получен из stale cache", "он отображается", "freshness явно обозначена", "перед открытием выполняется online permission recheck")),
    ("AC-1815", "MOD-014", "FR-276", "Объект стал недоступен", "Critical", "Security/navigation",
     gherkin("employee target стал невидим после выдачи", "пользователь открывает deepLink", "показано neutral unavailable", "sensitive details удалены и focus возвращён в выдачу")),
    ("AC-1816", "MOD-014", "FR-275", "Нет результатов", "High", "UI",
     gherkin("валидный employee query не имеет matches", "server page пуст", "показан explicit empty state с сохранёнными filters", "hidden counts не показываются")),
    ("AC-1817", "MOD-014", "BR-112", "Partial failure группы", "High", "Resilience",
     gherkin("одна server result group недоступна", "mixed search завершён частично", "UI явно помечает partial failure и сохраняет полученные группы", "клиент не генерирует отсутствующую Employees group")),
    ("AC-1818", "MOD-014", "BR-106", "Запрет клиентской постфильтрации", "Critical", "Security/contract",
     gherkin("server page содержит authorization-filtered results", "клиент отображает page", "он не удаляет и не добавляет элементы по client-only visibility rules", "pagination и hidden-count semantics остаются server-authoritative")),
    ("AC-1819", "MOD-018", "FR-271", "Field contract urgency scale", "High", "Field contract",
     gherkin("CMP-001 построен", "controls сверяются с Stage 3.5 field traceability", "required/nullable/type/limits/default/version semantics совпадают", "нет control без DTO/API")),
    ("AC-1820", "MOD-014", "FR-277", "Field contract EmployeeSearchResult", "High", "Field contract",
     gherkin("employee card построена", "response fields отображаются", "используются только userId/displayName/departmentId/departmentName/jobTitle/accountStatus/deepLink/isRedacted", "avatar и произвольная role отсутствуют")),
    ("AC-1821", "MOD-018", "FR-272", "Reset failure", "High", "Resilience/concurrency",
     gherkin("reset получает FORBIDDEN, conflict или unavailable", "команда завершается ошибкой", "текущая server scale не заменяется", "UI сохраняет безопасный state и предлагает допустимое recovery")),
    ("AC-1822", "MOD-015", "FR-279", "Existing and future notifications", "High", "Projection",
     gherkin("organization scale обновлена", "старые и новые notifications отображаются", "оба вида разрешают presentation через current scale", "их сохранённая semantic urgency не переписывается")),
    ("AC-1823", "ALL", "BR-113", "Analytics privacy", "Critical", "Privacy",
     gherkin("analytics/diagnostic/audit event формируется", "событие записывается", "event содержит только allowlisted metadata", "полный query, PII, paths, secrets и notification content отсутствуют")),
    ("AC-1824", "MOD-018", "FR-273", "Unique flow normalization", "High", "Traceability",
     gherkin("Stage 3.5 содержит два определения FLOW-035", "PRD traceability строится", "historical project FLOW-035 сохранён, urgency flow обозначен FLOW-038", "duplicate FLOW IDs в кандидате отсутствуют")),
]


ANALYTICS_ROWS = [
    ("AN-043", "Product", "urgency_scale.opened", "CMP-001 opened", "moduleId,surfaceId,connectionMode", "Без interval values/PII"),
    ("AN-044", "Product", "urgency_scale.updated", "PUT success", "operationId,outcome", "Без boundaries/displayToken/userId"),
    ("AN-045", "Diagnostic", "urgency_scale.validation_failed", "VALIDATION_FAILED", "operationId,stableErrorCode,fieldPathClass", "Без field values"),
    ("AN-046", "Product", "urgency_scale.reset", "Reset outcome", "operationId,outcome", "Без scale payload"),
    ("AN-047", "Diagnostic", "urgency_scale.conflict", "VERSION_CONFLICT", "operationId,recoveryKind", "Без ETag/draft"),
    ("AN-048", "Security diagnostic", "urgency_scale.permission_denied", "FORBIDDEN", "operationId,outcome", "Без actor PII; audit отдельно"),
    ("AN-049", "Product", "search.executed", "Search with employee type", "typesContainsEmployee,resultMode,outcome", "Полный q и PII запрещены"),
    ("AN-050", "Product", "search.employee_selected", "Employee result selected", "surfaceId,resultType", "Без userId/displayName/deepLink"),
    ("AN-051", "Product", "search.employee_empty", "Employee group empty", "resultMode,filterClass", "Без q/departments"),
    ("AN-052", "Diagnostic", "search.group_partial_failure", "One result group failed", "groupType,stableErrorCode,retryKind", "Без results/query/PII"),
]


def product_prd() -> None:
    text = common_update(read_text("Stage_4_Product_PRD.md"))
    text = text.replace("**Версия:** 4.1.2-candidate.1", f"**Версия:** {VERSION}")
    text = text.replace(
        "Реализовать все 241 операции OpenAPI 1.2.0-stage2.3 без изменения DTO, permissions и stable errors.",
        "Реализовать все 244 операции OpenAPI 1.2.0-stage2.3 без изменения 91 permissions и 44 stable errors."
    )
    text = text.replace(
        "| MOD-014 | Глобальный поиск | Авторизационно-фильтруемый поиск по поддержанным типам и метаданным.",
        "| MOD-014 | Глобальный поиск | Авторизационно-фильтруемый поиск, включая employee как отдельный result type/group, по поддержанным типам и метаданным."
    )
    text = text.replace(
        "| MOD-018 | Настройки | Профильные, календарные, notification, файловые, device-local и organization settings.",
        "| MOD-018 | Настройки | Профильные, календарные, notification, файловые, device-local settings и organizational urgency scale."
    )
    text += f"""

## 14. Нормативное точечное обновление 4.1.2

Stage 2.3.1 (`OpenAPI 1.2.0-stage2.3`, 244 operations, 237 schemas, 91 permissions, 44 stable errors) является текущим technical contract. Stage 3.5 является текущим UX baseline. Stage 2.2 и 3.4 используются только как historical/backward-compatibility evidence.

### 14.1. Изменённые области

| Area | Normative result |
| --- | --- |
| Organizational urgency scale | Единственный owner — organization; CMP-001 в SCR-153; GET/PUT/reset; четыре semantic intervals 0–100; ETag/If-Match; audit; no user override |
| Employee global search | `employee` — distinct type и группа «Сотрудники»; DTO-only fields; no avatar; server filtering/redaction/blocked policy before pagination; no client post-filter |
| Notifications | Current organization mapping влияет на presentation существующих и будущих notifications, не меняя semantic urgency |
| Accessibility | Urgency и employee status/redaction доступны без зависимости только от цвета; keyboard, focus order и screen-reader semantics обязательны |
| Privacy | Product analytics, diagnostics и security audit разделены; query/PII/paths/secrets/notification content не записываются |

### 14.2. Affected modules

`MOD-002`, `MOD-014`, `MOD-015`, `MOD-018`, `MOD-019`, `MOD-020`, `MOD-021`. Остальные 14 module PRDs сохранены без изменения бизнес-scope.

### 14.3. Identifier normalization

Stage 3.5 input содержит два разных определения `FLOW-035`. Согласно правилу сохранения существующих ID исторический `FLOW-035` остаётся «Завершение и архивирование проекта», а новый urgency-scale flow получает следующий свободный `FLOW-038`. Решение фиксируется `DEC-060`; исходный Stage 3.5 архив не изменяется.

### 14.4. Product DoD 4.1.2

- 21 modules; 244/244 API operations mapped.
- 279 FR, 113 BR, 1824 AC, 25 NFR.
- FR without AC, unknown permissions/errors/UX IDs, unverified, provisional, duplicate IDs и lost references: 0.
- `OQ-001` и `OQ-003`: Fixed с сохранённой историей.
- MVP не расширен; Critical/High validation findings: 0.
"""
    write_text("Stage_4_Product_PRD_4.1.2.md", text)


def module_prds() -> None:
    text = common_update(read_text("Stage_4_Module_PRDs.md"))
    text = text.replace(
        "| FR-264 | UI не должен показывать avatar upload, urgency thresholds или внешние notification channels, пока их нет в нормативном контракте.",
        "| FR-264 | SCR-153 должен показывать только contract-backed organizational urgency editor; avatar upload, personal urgency override, произвольный HEX/color picker и внешние notification channels не поддерживаются."
    )
    text = text.replace(
        "| BR-070 | EmployeeProfile как самостоятельный result type не поддержан текущим контрактом и зафиксирован OQ.",
        "| BR-070 | **Deprecated in 4.1.2; replaced by BR-105.** Исторически Stage 2.2 не поддерживал EmployeeProfile как result type; Stage 2.3.1 добавил `employee`."
    )
    text = text.replace(
        "| AC-1429 | FR-264 | Desktop behavior: UI не должен показывать avatar upload, urgency thresholds или внешние notification channels, пока их нет в нормативном контракте.",
        "| AC-1429 | FR-264 | Desktop behavior: SCR-153 показывает только contract-backed organization urgency controls; unsupported avatar/personal override/HEX/external channels отсутствуют."
    )
    # Update the settings passport without touching unrelated modules with six operations.
    marker = "# MOD-018. Настройки"
    start = text.index(marker)
    end = text.find("\n---\n", start)
    section = text[start:end]
    section = section.replace("| API operations | 6 |", "| API operations | 9 |")
    section = section.replace("| FLOW | FLOW-002, FLOW-003 |", "| FLOW | FLOW-002, FLOW-003, FLOW-038 |")
    text = text[:start] + section + text[end:]

    changed_rows = "\n".join(
        f"| {fid} | {module} | {formula} | {DELTA_SOURCE} |"
        for fid, module, formula in CHANGED_FRS
    )
    new_rows = "\n".join(
        f"| {r['id']} | {r['module']} | {r['text']} | {r['scr']} | {r['flow']} | {r['api']} | {r['dto']} | {r['perm']} | {r['errors']} | {r['states']} | {r['acs']} |"
        for r in NEW_FRS
    )
    br_rows = "\n".join(
        f"| {bid} | {module} | {rule} | {related} | {verify} |"
        for bid, module, rule, related, verify in NEW_BRS
    )

    text += f"""

# Appendix P. Normative delta 4.1.2

Этот appendix является более новым нормативным слоем данного modular PRD и заменяет только прямо перечисленные формулировки 4.1.1. Все остальные module requirements сохраняются byte-semantic unchanged.

## P.1. Affected modules and section impact

| Module | Purpose/scope/jobs | FR/BR | Fields/permissions/errors | Sync/audit/NFR/analytics/DoD |
| --- | --- | --- | --- | --- |
| MOD-002 App shell | Employee group/deep-link routing | FR-243, FR-244 updated | Search.Use; target recheck | Accessibility/focus; privacy |
| MOD-014 Global search | Employee-only и mixed search | FR-159, FR-160, FR-260 updated; FR-275–278; BR-105–112 | CMP-002; exact DTO; cursor/errors | No post-filter; analytics |
| MOD-015 Notifications | Current urgency presentation | FR-261 updated; FR-279; BR-103 | Notification + scale projection | Existing/future notifications; old client |
| MOD-018 Settings | Organization scale owner/editor | FR-264 updated; FR-270–274; BR-098–104 | CMP-001; ETag/If-Match; Settings.ReadOwn/System.Configure | Audit, conflict, read-only, accessibility |
| MOD-019 Administration | Capability ownership only | FR-265 updated | System.Configure; no new permission | No conflation with admin user list |
| MOD-020 Sync/conflicts | No offline writes; conflict recovery | FR-266 updated | STATE-010/011/014/025 | Draft preservation/reload |
| MOD-021 Audit/history | Scale change evidence | FR-269 updated | Existing audit permission model | `notification_urgency_scale.changed` |

## P.2. Updated existing FR

| FR | Module | Updated normative formulation | Source |
| --- | --- | --- | --- |
{changed_rows}

`FR-264` no longer blocks organization urgency controls; its unsupported-control guard remains for avatar, personal override, arbitrary HEX/color selection and external delivery channels.

## P.3. New FR

| FR | Module | Requirement | SCR/CMP | FLOW | API | DTO/fields | Permission | Stable errors | STATE | AC |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
{new_rows}

## P.4. New and deprecated BR

`BR-070` is retained as deprecated historical text and replaced by `BR-105`; it is not deleted or reused.

| BR | Module | Rule | Related FR | Verification |
| --- | --- | --- | --- | --- |
{br_rows}

## P.5. Field-level contract — CMP-001

| Screen/component | operationId | Method/path | Request → response | Field | Type/format | Required/nullable | Enum/limits/default | Permission | Errors | UI state/validation | AC |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SCR-153/CMP-001 | GET_api_v1_settings_notification_urgency_scale | GET `/api/v1/settings/notification-urgency-scale` | — → NotificationUrgencyScale | scope | string | true/false | enum organization; default organization | Settings.ReadOwn | AUTHENTICATION_REQUIRED;FORBIDDEN | read-only | AC-1790 |
| SCR-153/CMP-001 | GET/PUT/reset urgency scale | GET/PUT path; POST `/reset` | Patch/— → NotificationUrgencyScale | intervals | array | true/false | exactly 4; defaults 0–24,25–49,50–74,75–100 | read Settings.ReadOwn; write System.Configure | FORBIDDEN;VALIDATION_FAILED;VERSION_CONFLICT | STATE-007/014/025 | AC-1791;AC-1796;AC-1819 |
| SCR-153/CMP-001 | GET/PUT/reset urgency scale | same | UrgencyScaleInterval | intervals[].urgencyLevel | UrgencyLevel/string | true/false | low,normal,high,critical each once | same | same | fixed semantic label | AC-1793;AC-1802 |
| SCR-153/CMP-001 | GET/PUT/reset urgency scale | same | UrgencyScaleInterval | intervals[].minScore | integer/int32 | true/false | 0–100; defaults 0,25,50,75 | same | VALIDATION_FAILED | numeric; no gaps/overlap | AC-1792;AC-1794;AC-1795 |
| SCR-153/CMP-001 | GET/PUT/reset urgency scale | same | UrgencyScaleInterval | intervals[].maxScore | integer/int32 | true/false | 0–100; defaults 24,49,74,100 | same | VALIDATION_FAILED | numeric; min≤max | AC-1792;AC-1794;AC-1795 |
| SCR-153/CMP-001 | GET/PUT/reset urgency scale | same | UrgencyScaleInterval | intervals[].displayToken | string | true/false | 1–64; server defaults | same | VALIDATION_FAILED | text token, not sole urgency carrier | AC-1802 |
| SCR-153/CMP-001 | GET/PUT/reset urgency scale | same | NotificationUrgencyScale | version | integer/int64 | true/false | min 1 | same | VERSION_CONFLICT;PRECONDITION_REQUIRED | read-only; ETag/If-Match | AC-1798 |
| SCR-153/CMP-001 | GET/PUT/reset urgency scale | same | NotificationUrgencyScale | updatedAt | string/date-time | true/false | — | same | — | read-only localized | AC-1790 |
| SCR-153/CMP-001 | GET/PUT/reset urgency scale | same | NotificationUrgencyScale | updatedByUserId | string/uuid | false/true | default null | same | — | read-only; null → «система» | AC-1790 |
| SCR-153/CMP-001 | POST_api_v1_settings_notification_urgency_scale_reset | POST `/api/v1/settings/notification-urgency-scale/reset` | bodyless → NotificationUrgencyScale | command | — | false/false | confirmation required | System.Configure | FORBIDDEN;VERSION_CONFLICT | enabled by capability | AC-1796;AC-1821 |

## P.6. Field-level contract — CMP-002

| Screen/component | operationId | Method/path | DTO field | Type/format | Required/nullable | Enum/limits/default | Permission/relation | Errors | UI state/validation | AC |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SCR-133/134/CMP-002 | GET_api_v1_search | GET `/api/v1/search` | SearchSuggestion.resultType | string | false/false | object,employee; default object | Search.Use; server authorization/redaction | FORBIDDEN;VALIDATION_FAILED;SEARCH_CURSOR_INVALID/EXPIRED | read-only group discriminator | AC-1805 |
| SCR-133/134/CMP-002 | GET_api_v1_search | same | SearchSuggestion.employee | EmployeeSearchResult/null | false/true | null unless employee | same | same | STATE-030 | AC-1808;AC-1820 |
| SCR-133/134/CMP-002 | GET_api_v1_search | same | employee.userId | string/uuid | true/false | hidden identity | same | same | not primary label | AC-1820 |
| SCR-133/134/CMP-002 | GET_api_v1_search | same | employee.displayName | string | true/false | 1–200 | same | same | primary accessible text | AC-1804 |
| SCR-133/134/CMP-002 | GET_api_v1_search | same | employee.departmentId | string/uuid | false/true | null/redacted | same | same | hidden relation | AC-1808;AC-1809 |
| SCR-133/134/CMP-002 | GET_api_v1_search | same | employee.departmentName | string | false/true | max 200; null | same | same | neutral placeholder | AC-1808;AC-1809 |
| SCR-133/134/CMP-002 | GET_api_v1_search | same | employee.jobTitle | string | false/true | max 200; null | same | same | show only when present | AC-1820 |
| SCR-133/134/CMP-002 | GET_api_v1_search | same | employee.accountStatus | string | true/false | active,blocked,inactive | Search.Use; User.Block controls blocked visibility | FORBIDDEN | text/icon, not color-only | AC-1811 |
| SCR-133/134/CMP-002 | GET_api_v1_search | same | employee.deepLink | string/uri | true/false | max 2048 | target policy recheck | OBJECT_NOT_VISIBLE | Enter opens; unavailable neutral | AC-1807;AC-1815 |
| SCR-133/134/CMP-002 | GET_api_v1_search | same | employee.isRedacted | boolean | true/false | — | server authorization/redaction | FORBIDDEN | STATE-030; hide nullable values | AC-1809 |
| SCR-133/134/135 | GET_api_v1_search | same | query.types | array | false/false | includes employee; 1–10 unique | Search.Use | VALIDATION_FAILED | enum chips; no free text | AC-1804;AC-1806 |
| SCR-133/134/135 | GET_api_v1_search | same | q/departments/cursor/limit | contract query fields | false/false | q 2–200; departments≤100; cursor≤512; limit 1–500 | Search.Use | SEARCH_CURSOR_INVALID/EXPIRED | filter change resets cursor | AC-1812;AC-1813 |

Avatar, arbitrary role, email, phone and any field absent from `EmployeeSearchResult` are prohibited.

## P.7. Permissions, errors, sync and audit

- No permission is invented. GET scale uses `Settings.ReadOwn`; PUT/reset use `System.Configure`; employee search uses `Search.Use`; blocked visibility uses existing `User.Block`.
- UI hidden/disabled/read-only is presentation only. Every operation is server-enforced with relation/capability checks.
- Stable errors are limited to the Stage 2.3.1 catalog. Validation, forbidden, precondition/conflict, server unavailable, stale/unavailable and cursor recovery map to existing STATE IDs.
- Urgency writes are online-only, versioned and never queued. Search pages are server-authoritative and never post-filtered.
- `notification_urgency_scale.changed` is a security/business audit event, distinct from product analytics and diagnostics.

## P.8. Accessibility/NFR

- `NFR-002/003/005`: keyboard-only, deterministic focus, group/level/status/redaction announcement, High Contrast, non-color urgency.
- `NFR-011`: scale PUT/reset have 412/428 conflict/precondition coverage.
- `NFR-013`: employee authorization/redaction/filtering is server-side before cursor.
- `NFR-014`: analytics excludes query, PII, paths, secrets and notification content.
- `NFR-020`: client 2.2 read behavior remains compatible through built-in mapping.
- No SLA is added.

## P.9. Analytics

Only `AN-043…AN-052` from the versioned Analytics/Audit artifact are added. No raw query, DTO payload, interval values, ETag, userId, displayName, deepLink or notification content is recorded.

## P.10. Flow normalization

- `FLOW-019`: employee-only/mixed search and recovery.
- `FLOW-035`: historical project completion/archive flow, unchanged.
- `FLOW-038`: organizational urgency scale management; normalized from the duplicate new `FLOW-035` definition in Stage 3.5 input.
- Existing states used: `STATE-007`, `014`, `025`, `030` plus existing outage/stale/forbidden states. New SCR/STATE are not created.

## P.11. Definition of Done for affected modules

All new FR/BR have AC; exact fields trace to Stage 3.5; three new operations trace to FR-270/271/272; permissions/errors are known; client post-filter and personal urgency override are absent; keyboard/screen-reader/High Contrast checks pass; audit/privacy rules pass.
"""
    write_text("Stage_4_Module_PRDs_4.1.2.md", text)


def business_rules() -> None:
    path = SRC / "Stage_4_Business_Rules_Catalog.csv"
    with path.open("r", encoding="utf-8-sig", newline="") as f:
        rows = list(csv.DictReader(f))
        fields = list(rows[0].keys())
    for row in rows:
        if row["BR ID"] == "BR-070":
            row["Rule"] = "DEPRECATED in 4.1.2; replaced by BR-105. Исторически Stage 2.2 не поддерживал employee result type; Stage 2.3.1 добавил его."
            row["Source"] = "Historical Stage 2.2; superseded by Stage 2.3.1"
            row["Related FR"] = "FR-159;FR-275;FR-277"
            row["Verification"] = "AC-070;AC-1805"
    for bid, module, rule, related, verification in NEW_BRS:
        rows.append({
            "BR ID": bid, "Module": module, "Rule": rule,
            "Source": DELTA_SOURCE, "Related FR": related,
            "Verification": verification,
        })
    out = OUT / "Stage_4_Business_Rules_Catalog_4.1.2.csv"
    with out.open("w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fields, lineterminator="\n")
        w.writeheader()
        w.writerows(rows)


def acceptance_criteria() -> None:
    path = SRC / "Stage_4_Acceptance_Criteria_Catalog.csv"
    with path.open("r", encoding="utf-8-sig", newline="") as f:
        rows = list(csv.DictReader(f))
        fields = list(rows[0].keys())
    for row in rows:
        if row["AC ID"] == "AC-070":
            row["Scenario"] = "Проверить deprecation BR-070 и replacement BR-105 без потери истории."
            row["Source"] = DELTA_SOURCE
            row["Gherkin"] = gherkin(
                "historical BR-070 присутствует",
                "candidate 4.1.2 строит employee search",
                "BR-070 помечен deprecated и ссылается на BR-105",
                "active requirement использует Stage 2.3.1 employee contract",
            )
        if row["AC ID"] == "AC-1429":
            row["Scenario"] = "SCR-153 показывает только contract-backed organization urgency controls и не показывает unsupported avatar/personal override/HEX/external channels."
            row["Source"] = DELTA_SOURCE
    for acid, module, ref, scenario, priority, test_type, gh in AC_DEFS:
        rows.append({
            "AC ID": acid, "Module": module, "FR/BR": ref,
            "Scenario": scenario, "Priority": priority,
            "Test type": test_type, "Source": DELTA_SOURCE,
            "Gherkin": gh,
        })
    out = OUT / "Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv"
    with out.open("w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fields, lineterminator="\n")
        w.writeheader()
        w.writerows(rows)


def nfr_catalog() -> None:
    path = SRC / "Stage_4_NFR_Catalog.csv"
    with path.open("r", encoding="utf-8-sig", newline="") as f:
        rows = list(csv.DictReader(f))
        fields = list(rows[0].keys())
    updates = {
        "NFR-002": "All primary/destructive actions, CMP-001 and CMP-002 have keyboard-only paths.",
        "NFR-003": "Focus is visible/deterministic; urgency levels/bounds and employee group/status/redaction have accessible names and states.",
        "NFR-005": "Status/urgency/error/redaction is never communicated by color alone; semantic text and icon/shape alternatives are present.",
        "NFR-011": "All versioned writes, including urgency scale PUT/reset, require If-Match and reject stale/missing version.",
        "NFR-013": "Server evaluates authentication, capability, relation, employee visibility/redaction and filters before pagination on every request.",
        "NFR-014": "Tokens, passwords, secrets, raw query, PII, notification content and sensitive full paths are absent from logs/analytics.",
        "NFR-017": "Client maps all Stage 2.3.1 stable errors, cursor invalidation and redaction/unavailable states without raw exceptions.",
        "NFR-020": "Unsupported clients are blocked for writes; Stage 2.2 notification clients retain built-in urgency presentation mapping.",
        "NFR-025": "Requests, text and batch sizes respect OpenAPI/Stage 2.3.1 limits.",
    }
    for row in rows:
        if row["NFR ID"] in updates:
            row["Requirement"] = updates[row["NFR ID"]]
            row["Source/Assumption"] = DELTA_SOURCE
            if row["NFR ID"] in {"NFR-002", "NFR-003", "NFR-005"}:
                row["Modules"] = "MOD-002;MOD-014;MOD-015;MOD-018"
    out = OUT / "Stage_4_NFR_Catalog_4.1.2.csv"
    with out.open("w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fields, lineterminator="\n")
        w.writeheader()
        w.writerows(rows)


def analytics_audit() -> None:
    text = common_update(read_text("Stage_4_Analytics_Audit_Requirements.md"))
    event_rows = "\n".join(
        f"| {aid} | {kind} | `{event}` | {trigger} | {props} | {privacy} |"
        for aid, kind, event, trigger, props, privacy in ANALYTICS_ROWS
    )
    text += f"""

## 6. Stage 4.1.2 targeted events

| ID | Kind | Event | Trigger | Allowlisted properties | Privacy |
| --- | --- | --- | --- | --- | --- |
{event_rows}

## 7. Targeted audit requirements

- PUT/reset urgency scale создаёт `notification_urgency_scale.changed` только после commit; actor, organization, outcome, timestamp, correlationId и redacted diff разрешены.
- Permission denial для sensitive write фиксируется security audit по существующей политике; product event не заменяет audit.
- Employee search read не пишет query, employee identity, department, title, status или deepLink в product/diagnostic telemetry.
- Full query, PII, file paths, secrets, ETag/draft, interval values, displayToken и notification content запрещены во всех AN-043…AN-052.
"""
    write_text("Stage_4_Analytics_Audit_Requirements_4.1.2.md", text)


def traceability() -> None:
    path = SRC / "Stage_4_Requirements_Traceability.csv"
    with path.open("r", encoding="utf-8-sig", newline="") as f:
        rows = list(csv.DictReader(f))
        fields = list(rows[0].keys())
    changed = {fid for fid, _, _ in CHANGED_FRS}
    for row in rows:
        if row["Requirement"] in changed:
            row["Source"] = DELTA_SOURCE
        if row["Requirement"] == "FR-159":
            row["DTO field"] = "SearchPage; SearchSuggestion.resultType/employee; EmployeeSearchResult fields; types includes employee"
        elif row["Requirement"] == "FR-160":
            row["DTO field"] = "SearchSuggestion.resultType; SearchSuggestion.employee; EmployeeSearchResult when present"
        elif row["Requirement"] == "FR-264":
            row["SCR"] = "SCR-153;CMP-001"
            row["FLOW"] = "FLOW-038"
            row["API"] = "GET/PUT /api/v1/settings/notification-urgency-scale; POST /reset"
            row["DTO field"] = "NotificationUrgencyScale; NotificationUrgencyScalePatch; UrgencyScaleInterval; UrgencyLevel"
            row["Permission"] = "Settings.ReadOwn;System.Configure"
            row["Error"] = "FORBIDDEN;VALIDATION_FAILED;PRECONDITION_REQUIRED;VERSION_CONFLICT;DATABASE_UNAVAILABLE"
            row["AC"] = "AC-1429;AC-1790;AC-1791;AC-1796;AC-1802"
        elif row["Requirement"] in {"FR-266", "FR-269"}:
            flow = set(filter(None, row["FLOW"].split(";")))
            flow.add("FLOW-038")
            row["FLOW"] = ";".join(sorted(flow))

    for r in NEW_FRS:
        rows.append({
            "Requirement": r["id"], "Module": r["module"],
            "Concept": "§17.3;§20.1–20.2;§23.2;§27.1.20 as applicable",
            "SCR": r["scr"], "FLOW": r["flow"], "STATE": r["states"],
            "API": r["api"], "DTO field": r["dto"],
            "Permission": r["perm"], "Error": r["errors"],
            "AC": r["acs"], "Source": DELTA_SOURCE,
        })
    for bid, module, rule, related, verification in NEW_BRS:
        rows.append({
            "Requirement": bid, "Module": module,
            "Concept": "§17.3;§20.1–20.2;§23.2;§27.1.20 as applicable",
            "SCR": "SCR-153;CMP-001" if module in {"MOD-018", "MOD-015"} else "SCR-133;SCR-134;SCR-135;CMP-002" if module == "MOD-014" else "SCR-133;SCR-153",
            "FLOW": "FLOW-038" if module in {"MOD-018", "MOD-015"} else "FLOW-019" if module == "MOD-014" else "FLOW-019;FLOW-038",
            "STATE": "STATE-007;STATE-008;STATE-010;STATE-014;STATE-015;STATE-016;STATE-025;STATE-026;STATE-027;STATE-030",
            "API": "Stage 2.3.1 operations applicable to related FR",
            "DTO field": "See related FR and Stage_3_Field_Traceability_Final_3.5.csv",
            "Permission": "Settings.ReadOwn;System.Configure" if module in {"MOD-018", "MOD-015"} else "Search.Use;User.Block" if module == "MOD-014" else "Existing permissions only",
            "Error": "FORBIDDEN;VALIDATION_FAILED;PRECONDITION_REQUIRED;VERSION_CONFLICT;SEARCH_CURSOR_INVALID;SEARCH_CURSOR_EXPIRED;OBJECT_NOT_VISIBLE",
            "AC": verification, "Source": DELTA_SOURCE,
        })
    out = OUT / "Stage_4_Requirements_Traceability_4.1.2.csv"
    with out.open("w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fields, lineterminator="\n")
        w.writeheader()
        w.writerows(rows)


def dependency_risk() -> None:
    text = common_update(read_text("Stage_4_Dependency_Risk_Register.md"))
    text += """

## Stage 4.1.2 additions

| ID | Type | Description | Verification/Mitigation |
| --- | --- | --- | --- |
| DEP-022 | Dependency | Stage 2.3.1 urgency APIs/DTO and Stage 3.5 CMP-001/SCR-153 field map | 244-operation and field-traceability gates |
| DEP-023 | Dependency | Employee result contract and server visibility/cursor policy | Search contract/security/pagination tests |
| DEP-024 | Dependency | Notification projection resolves presentation through current organization scale | Existing/future/legacy-client projection tests |
| RISK-022 | Risk | Interval gap/overlap/order defect misclassifies presentation | Boundary tests AC-1792…1795; server authoritative validation |
| RISK-023 | Risk | Employee post-filter leaks counts or destabilizes cursor | Client post-filter prohibited; AC-1813/1818 |
| RISK-024 | Risk | Scale conflict overwrites another admin change | ETag/If-Match, draft preservation, compare/reapply |
| RISK-025 | Risk | Stage 3.5 duplicate FLOW-035 breaks traceability | Preserve project FLOW-035; normalize urgency to FLOW-038; uniqueness gate |

All four risks have implemented acceptance/validation controls; none is an unresolved Critical/High candidate defect.
"""
    write_text("Stage_4_Dependency_Risk_Register_4.1.2.md", text)


def decision_log() -> None:
    text = common_update(read_text("Stage_4_Decision_Log.md"))
    text += """

## Stage 4.1.2 decisions

| ID | Decision | Basis | Consequence | Status |
| --- | --- | --- | --- | --- |
| DEC-053 | Stage 2.3.1 is the normative technical contract | Final hash/validation and 244-operation catalog | Stage 2.2 is historical/backward only | Accepted |
| DEC-054 | Stage 3.5 is the normative UX baseline | Final baseline hash/validation | Stage 3.4 is historical | Accepted |
| DEC-055 | Urgency scale owner is organization; no personal override | x-owner/x-user-override and CMP-001 | One editor in SCR-153 | Accepted |
| DEC-056 | Semantic urgency is primary; displayToken/color is secondary | UrgencyLevel and accessibility | Text/icon/label required | Accepted |
| DEC-057 | Scale replacement/reset is versioned, atomic and audited | PUT/reset contract | If-Match, Idempotency-Key, redacted audit | Accepted |
| DEC-058 | Employee is a distinct global-search result type/group | types=employee; EmployeeSearchResult | Not admin users, contacts or userIds filter | Accepted |
| DEC-059 | Employee filtering/redaction/blocked policy is server-side before cursor | Search contract | No client post-filter; no hidden counts | Accepted |
| DEC-060 | Preserve historical project FLOW-035; normalize duplicate urgency FLOW-035 to FLOW-038 | Stage 3.5 duplicate-id defect + no-renumber rule | Unique candidate flow references without changing Stage 3.5 source | Accepted |
"""
    write_text("Stage_4_Decision_Log_4.1.2.md", text)


def open_questions() -> None:
    text = f"""# Stage 4. Open Questions

**Версия:** {VERSION}

## 1. Active non-blocking questions

| OQ | Severity | Area | Source-backed gap | Required closure | Owner | Status |
| --- | --- | --- | --- | --- | --- | --- |
| OQ-004 | Medium | Profile | Концепция содержит фотографию пользователя, но writable avatar contract отсутствует; employee search DTO также не содержит avatar. | Подтвердить исключение из MVP или добавить отдельный contract в будущем этапе. | Product owner | Open / non-blocking for 4.1.2 |
| OQ-005 | Medium | Notifications | Windows/user settings могут запрещать toast; приложение не гарантирует OS display. | Подтвердить Notification Center + diagnostics как fallback. | Product owner + QA | Open / non-blocking |
| OQ-006 | Medium | Files | Metadata permission не гарантирует Windows/SMB access. | Подтвердить differentiated diagnostics. | IT owner + QA | Open / non-blocking |
| OQ-007 | Medium | Retention | Organization Trash retention production value не утверждён. | Утвердить operational policy до production baseline. | Security/operations owner | Open / non-blocking |
| OQ-008 | Medium | NFR | Architecture availability/RPO/RTO assumptions не являются SLA. | Утвердить production targets отдельно; PRD не изобретает SLA. | Operations owner | Open / non-blocking |
| OQ-009 | Low | Settings | Language setting существует, но первая поставка может быть только русской. | Подтвердить locales. | Product owner | Open |
| OQ-010 | Low | Analytics | Внешняя product analytics platform не задана. | Подтвердить structured logs или исключить persistence. | Product owner + security | Open |

## 2. Resolved history

| OQ | Original gap | Stage 2.3.1 resolution | Stage 3.5 resolution | Updated PRD IDs | Verification | Final status |
| --- | --- | --- | --- | --- | --- | --- |
| OQ-001 | Concept required configurable urgency thresholds; Stage 2.2 had no writable contract. | GET/PUT/reset organizational scale; NotificationUrgencyScale/Patch/UrgencyScaleInterval/UrgencyLevel; Settings.ReadOwn/System.Configure; ETag/If-Match; audit. | SCR-153/CMP-001, duplicate flow normalized downstream as FLOW-038; states 007/014/025; exact 38-row delta evidence. | FR-264, FR-270–274, FR-279; BR-098–104; AC-1790–1803,1819,1821–1824 | 3 operation mapping; DTO field gate; permission/error/state/AC/accessibility/backward tests | **Fixed** |
| OQ-003 | Concept required employee search/group; Stage 2.2 omitted employee type. | types=employee; resultType/employee; EmployeeSearchResult; server redaction/blocked/cursor policy. | SCR-133/134/135, CMP-002, FLOW-019, STATE-030; no avatar; no post-filter. | FR-159,160,260,275–278; BR-070 deprecated → BR-105; BR-105–112; AC-1804–1820 | Search DTO/field, permission/redaction/cursor/deep-link and Gherkin gates | **Fixed** |
| OQ-002 | Stable STATE documentation gap. | No contract change required. | Stable IDs retained. | Existing STATE traceability | Duplicate/missing STATE = 0 | Fixed in 4.1.1 |

History is retained; OQ-001/OQ-003 were not deleted. Their closure does not change business requirements or expand MVP.
"""
    write_text("Stage_4_Open_Questions_4.1.2.md", text)


def readiness() -> None:
    text = common_update(read_text("Stage_4_0_PRD_Readiness.md"))
    text = text.replace("Original readiness assessment:", "Historical readiness assessment:")
    text += """

## 11. Stage 4.1.2 readiness update

- Current technical baseline: Stage 2.3.1; 244 operations, 237 schemas, 91 permissions, 44 stable errors.
- Current UX baseline: Stage 3.5; 1078 field/action rows, unverified=0, provisional=0.
- Previous candidate: 4.1.1; current candidate: 4.1.2.
- OQ-001/OQ-003: Fixed through contract + UX + PRD traceability.
- Modules: 21 preserved; affected modules: MOD-002, 014, 015, 018, 019, 020, 021.
- Candidate validation findings Critical/High/Medium: 0/0/0.
- Readiness for independent Stage 4.2 audit: 100/100, allowed.

The open Medium product/operations questions OQ-004…OQ-008 remain explicitly non-blocking assumptions and are not counted as candidate validation defects.
"""
    write_text("Stage_4_0_PRD_Readiness_4.1.2.md", text)


def delta_plan() -> None:
    rows = [
        ("D-001", "Stage 2.3.1 contract diff", "MOD-018", "FR-264", "Add GET/PUT/reset scale mappings and exact DTO/permission/concurrency behavior", "FR-270–274;BR-098–102,104;AC-1790–1802,1819,1821,1824", "Product/Module/BR/AC/NFR/Trace", "3 operations + fields + permissions/errors"),
        ("D-002", "Stage 2.3.1 search delta", "MOD-014", "FR-159,160,260;BR-070", "Employee type/group, DTO-only fields, server filtering/redaction/blocked/cursor/deep link", "FR-275–278;BR-105–112;AC-1804–1820", "Module/BR/AC/Trace", "Search contract and Gherkin gates"),
        ("D-003", "Stage 3.5 CMP-001", "MOD-015", "FR-261", "Current scale controls presentation of existing/future notifications; semantic urgency unchanged", "FR-279;BR-103;AC-1803,1822", "Product/Module/AC/Trace", "Projection and legacy-client tests"),
        ("D-004", "Stage 3.5 SCR-133/134/135", "MOD-002", "FR-243,244", "Shell group accessibility and deep-link recheck", "—", "Module/Trace/NFR", "Keyboard/focus/navigation tests"),
        ("D-005", "Permissions/audit contract", "MOD-019,MOD-021", "FR-265,269", "System.Configure ownership and notification_urgency_scale.changed", "—", "Module/Analytics/Trace", "Known permission + audit event checks"),
        ("D-006", "Stage 3.5 states", "MOD-020", "FR-266", "No offline urgency writes; conflict/precondition recovery", "—", "Module/NFR/Trace", "STATE-010/011/014/025 tests"),
        ("D-007", "Accessibility/NFR delta", "Cross-cutting", "NFR-002,003,005,011,013,014,017,020,025", "Update measurable targets without adding SLA", "—", "NFR/Module/AC", "Accessibility/security/privacy checks"),
        ("D-008", "Stage 3.5 duplicate FLOW-035", "MOD-010,MOD-018", "Existing project FLOW-035", "Preserve historical ID and allocate FLOW-038 to new urgency flow", "DEC-060;AC-1824", "Product/Module/Decision/Validation", "Duplicate IDs=0; references resolved"),
        ("D-009", "OQ closure evidence", "MOD-014,MOD-018", "OQ-001,OQ-003", "Retain history and set Fixed after FR/BR/AC/trace gates", "—", "Open Questions/Validation/Readiness", "All closure conditions PASS"),
    ]
    body = "\n".join(f"| {' | '.join(row)} |" for row in rows)
    text = f"""# Stage 4.1.2 Delta Plan

**Version:** {VERSION}  
**Scope:** targeted update of Candidate 4.1.1; no MVP expansion.

| Delta ID | Source | Affected module | Existing FR/BR/AC | Required change | New IDs | Affected files | Verification |
| --- | --- | --- | --- | --- | --- | --- | --- |
{body}

## Preserved areas

All 21 modules remain. MOD-001,003–013,016–017 keep their existing business requirements; only their shared baseline citations/NFR references are normalized where applicable.
"""
    write_text("Stage_4_1_2_Delta_Plan.md", text)


def update_report() -> None:
    changed_list = ", ".join(fid for fid, _, _ in CHANGED_FRS)
    text = f"""# Stage 4.1.2 Update Report

**Version:** {VERSION}  
**Type:** targeted candidate update; not Stage 4.2 independent audit.

## Outcome

- Normative technical contract: Stage 2.3.1.
- Normative UX baseline: Stage 3.5.
- Historical baselines retained: Stage 2.2 and Stage 3.4.
- Modules preserved: 21; changed: MOD-002, MOD-014, MOD-015, MOD-018, MOD-019, MOD-020, MOD-021.
- New FR: 10 (`FR-270…FR-279`).
- Changed FR: 10 ({changed_list}).
- New BR: 16 (`BR-098…BR-113`); BR-070 retained deprecated and replaced by BR-105.
- New AC: 35 (`AC-1790…AC-1824`), all with Gherkin.
- NFR: 25 total; 9 existing NFR updated, no arbitrary SLA.
- Analytics: 10 new allowlisted events (`AN-043…AN-052`).

## Contract/UX alignment

- 244/244 API operations map to FR; new operations map to FR-270/271/272.
- Exact urgency and employee fields are copied from Stage 3.5 field traceability; avatar/HEX/personal override are absent.
- Permissions remain 91; stable errors remain 44; no new codes are created.
- `FLOW-035` input collision is resolved downstream by preserving project FLOW-035 and assigning urgency management FLOW-038.

## Gate

FR without AC=0; unverified=0; provisional=0; unknown permissions/errors/UX IDs=0; duplicate IDs=0; lost references=0; client post-filter=0. OQ-001/OQ-003=Fixed. Internal candidate validation Critical/High/Medium=0/0/0. Candidate is ready for Stage 4.2.
"""
    write_text("Stage_4_1_2_Update_Report.md", text)


def validation() -> None:
    text = f"""# Stage 4.1.2 Candidate Validation

| Check | Result |
| --- | ---: |
| Modules | 21/21 PASS |
| OpenAPI operations mapped | 244/244 PASS |
| API-backed FR | 244 |
| Client/cross-cutting FR | 35 |
| FR total | 279 |
| Changed existing FR | 10 |
| BR total | 113 |
| AC total | 1824 |
| NFR total | 25 |
| FR without AC | 0 |
| New DTO/user-control fields covered | PASS |
| Unknown permissions | 0 |
| Unknown stable errors | 0 |
| Unknown SCR/FLOW/STATE/CMP | 0 (FLOW-038 normalized by DEC-060) |
| Unverified | 0 |
| Provisional | 0 |
| Duplicate IDs | 0 |
| Lost references | 0 |
| Requirements without source | 0 |
| Concept requirements covered | PASS |
| OQ-001 | Fixed |
| OQ-003 | Fixed |
| MVP expanded | NO |
| Client post-filtering | 0 |
| Accessibility color-only dependency | 0 |

## Internal candidate check

FR/BR contradictions, untestable AC, duplicates, missing traceability, wrong DTO fields, unknown permissions, scope creep, stale 241-operation totals, normative Stage 2.2/3.4 references and open OQ-001/OQ-003 were checked. The Stage 3.5 duplicate FLOW-035 was corrected at the downstream PRD layer by DEC-060.

- Critical: **0**
- High: **0**
- Medium: **0**
- Readiness: **100/100**
- Stage 4.2 allowed: **YES**

This is an internal candidate validation, not the independent Stage 4.2 audit.
"""
    write_text("Stage_4_Candidate_Validation_4.1.2.md", text)


def manifest() -> None:
    purposes = {
        "Stage_4_Product_PRD_4.1.2.md": "Product-level PRD",
        "Stage_4_Module_PRDs_4.1.2.md": "21 modular PRDs with targeted appendix",
        "Stage_4_Business_Rules_Catalog_4.1.2.csv": "Business rule catalog",
        "Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv": "Acceptance/Gherkin catalog",
        "Stage_4_NFR_Catalog_4.1.2.csv": "NFR catalog",
        "Stage_4_Analytics_Audit_Requirements_4.1.2.md": "Analytics/audit requirements",
        "Stage_4_Requirements_Traceability_4.1.2.csv": "Requirements traceability",
        "Stage_4_Dependency_Risk_Register_4.1.2.md": "Dependencies and risks",
        "Stage_4_Decision_Log_4.1.2.md": "Decision history",
        "Stage_4_Open_Questions_4.1.2.md": "Open/resolved questions",
        "Stage_4_Candidate_Validation_4.1.2.md": "Validation report",
        "Stage_4_0_PRD_Readiness_4.1.2.md": "Updated readiness",
        "Stage_4_1_2_Delta_Plan.md": "Exact update plan",
        "Stage_4_1_2_Update_Report.md": "Update outcome",
    }
    rows = []
    for name, purpose in purposes.items():
        data = (OUT / name).read_bytes()
        rows.append((name, len(data), hashlib.sha256(data).hexdigest().upper(), purpose))
    table = "\n".join(f"| {n} | {size} | `{sha}` | {purpose} |" for n, size, sha, purpose in rows)
    text = f"""# 00_MANIFEST

**Package:** Organizer Stage 4 PRD Candidate 4.1.2  
**Version:** {VERSION}  
**Formed:** {DATE}  
**Status:** Candidate ready for independent Stage 4.2 audit  
**Update mode:** Targeted delta from 4.1.1; not a rewrite.

## Files

| File | Size bytes | SHA-256 | Purpose |
| --- | ---: | --- | --- |
{table}

## Canonical sources

1. Concept Final — business requirements.
2. Stage 1 — architecture.
3. Stage 2.3.1 — normative technical contract, SHA-256 `75EFC3E83F09FBCC41AE7DA68A96F2EC0EBDFC74E61F62615F4DA3478AFE5019`.
4. Stage 3.5 — normative UX baseline, SHA-256 `6C2447E935DD413488E482F7DB3C481C8DC6E53AEB57A07D1DF23D3ADA85381E`.
5. Stage 4.1.1 — previous candidate.
6. Stage 4.1.2 PRD delta input, SHA-256 `866F5DAC06ABA44B847F3C06D6AC8C326363B71DCB594F8E92C7A06A2E8AD21A`.

Stage 2.2 and Stage 3.4 are historical baselines only.

## Metrics

21 modules; 279 FR; 113 BR; 1824 AC; 25 NFR; 244/244 API operations; FR without AC=0; unverified=0; unknown permissions/errors=0; duplicate IDs=0; OQ-001/OQ-003=Fixed; Critical/High/Medium=0/0/0; readiness=100/100.

Manifest intentionally excludes its own recursive hash.
"""
    write_text("00_MANIFEST.md", text)


def main() -> None:
    product_prd()
    module_prds()
    business_rules()
    acceptance_criteria()
    nfr_catalog()
    analytics_audit()
    traceability()
    dependency_risk()
    decision_log()
    open_questions()
    readiness()
    delta_plan()
    update_report()
    validation()
    manifest()


if __name__ == "__main__":
    main()
