import fs from "node:fs/promises";
import path from "node:path";
import { Workbook } from "@oai/artifact-tool";

const root = "C:/Users/novik/Таск";
const source = path.join(root, "work/stage_3_5_20260726/stage34/Organizer_Stage3_Final_Baseline_3.4/Stage_3_Field_Traceability.csv");
const output = path.join(root, "work/stage_3_5_20260726/package_3_5/Stage_3_Field_Traceability_Final_3.5.csv");
const previewPath = path.join(root, "work/stage_3_5_20260726/sheet_tool/traceability_preview.png");

const csvText = await fs.readFile(source, "utf8");
const workbook = await Workbook.fromCSV(csvText, { sheetName: "Traceability" });
const sheet = workbook.worksheets.getItem("Traceability");
const used = sheet.getUsedRange();
const base = used.values;
const header = base[0];

const searchErrors = "AUTHENTICATION_REQUIRED;FORBIDDEN;VALIDATION_FAILED;SEARCH_CURSOR_INVALID;SEARCH_CURSOR_EXPIRED";
const readErrors = "AUTHENTICATION_REQUIRED;FORBIDDEN";
const writeErrors = "AUTHENTICATION_REQUIRED;FORBIDDEN;VALIDATION_FAILED;VERSION_CONFLICT";

for (let r = 1; r < base.length; r += 1) {
  const row = base[r];
  if (["SCR-133", "SCR-134", "SCR-135"].includes(row[0]) && row[3] === "GET_api_v1_search" && row[8] === "types") {
    row[15] = 'enum=["task","calendar_event","project","catalog_item","file_location","contact","company","interaction","comment","employee"]; minItems=1; maxItems=10; uniqueItems=true';
    row[23] = "Выберите один или несколько типов; employee — отдельная группа. Не более 10 значений.";
    row[24] = "Stage 2.3.1 openapi.yaml#/paths//api/v1/search/get/parameters/types";
  }
}

function row(scr, surface, control, op, method, apiPath, requestDto, responseDto, field, type, format, required, nullable, readOnly, limits, defaultValue, patch, version, permission, relation, errors, state, message, sourceRef) {
  return [scr, surface, control, op, method, apiPath, requestDto, responseDto, field, type, format, required, nullable, readOnly, "False", limits, defaultValue, patch, version, permission, relation, errors, state, message, sourceRef];
}

const urgencyFields = [
  ["scope", "Scope · Read-only label", "string", "—", "True", "False", "True", 'enum=["organization"]', "organization", "Область задаётся сервером и не редактируется."],
  ["intervals", "Интервалы · Four-row editor", "array", "—", "True", "False", "False", "minItems=4; maxItems=4; uniqueItems=true", "0–24;25–49;50–74;75–100", "Требуется ровно четыре упорядоченных интервала."],
  ["intervals[].urgencyLevel", "Semantic urgency · Fixed label", "string", "—", "True", "False", "False", 'enum=["low","normal","high","critical"]', "low;normal;high;critical", "Каждый semantic level используется ровно один раз."],
  ["intervals[].minScore", "Нижняя граница · Numeric input", "integer", "int32", "True", "False", "False", "min=0; max=100", "0;25;50;75", "Введите целое 0–100; интервалы без разрывов/пересечений."],
  ["intervals[].maxScore", "Верхняя граница · Numeric input", "integer", "int32", "True", "False", "False", "min=0; max=100", "24;49;74;100", "Введите целое 0–100; minScore ≤ maxScore."],
  ["intervals[].displayToken", "Display token · Text input", "string", "—", "True", "False", "False", "minLength=1; maxLength=64", "server defaults", "Введите 1–64 символа; token не является единственным признаком срочности."],
  ["version", "Version · Read-only value", "integer", "int64", "True", "False", "True", "min=1", "—", "Версия только для чтения; сохранение использует ETag/If-Match."],
  ["updatedAt", "Обновлено · Read-only timestamp", "string", "date-time", "True", "False", "True", "—", "—", "Не редактируется; отображается в локальном формате."],
  ["updatedByUserId", "Кем обновлено · Read-only reference", "string|null", "uuid", "False", "True", "True", "—", "null", "При null показать нейтральное «система» без вымышленного пользователя."],
];

const newRows = [];
for (const f of urgencyFields) {
  newRows.push(row("SCR-153", "Notifications и DND", f[1], "GET_api_v1_settings_notification_urgency_scale", "GET", "/api/v1/settings/notification-urgency-scale", "—", "NotificationUrgencyScale", f[0], f[2], f[3], f[4], f[5], f[6], f[7], f[8], "response-only", "ETag returned", "Settings.ReadOwn", "organization", readErrors, f[6] === "True" ? "read-only" : "view", f[9], `Stage 2.3.1 openapi.yaml#/components/schemas/NotificationUrgencyScale/${f[0]}`));
}
for (const f of urgencyFields) {
  const isWrite = f[0] === "intervals" || f[0].startsWith("intervals[].");
  newRows.push(row("SCR-153", "Notifications и DND", f[1], "PUT_api_v1_settings_notification_urgency_scale", "PUT", "/api/v1/settings/notification-urgency-scale", "NotificationUrgencyScalePatch", "NotificationUrgencyScale", f[0], f[2], f[3], f[4], f[5], isWrite ? "False" : "True", f[7], f[8], isWrite ? "full replacement; intervals required" : "response-only", "If-Match required; ETag returned", "System.Configure", "organization", writeErrors, isWrite ? "editable; STATE-007/014/025" : "read-only", f[9], `Stage 2.3.1 openapi.yaml PUT urgency scale; schema field ${f[0]}`));
}
for (const f of urgencyFields) {
  newRows.push(row("SCR-153", "Notifications и DND", `Reset result · ${f[1]}`, "POST_api_v1_settings_notification_urgency_scale_reset", "POST", "/api/v1/settings/notification-urgency-scale/reset", "—", "NotificationUrgencyScale", f[0], f[2], f[3], f[4], f[5], "True", f[7], f[8], "response-only", "If-Match required; ETag returned", "System.Configure", "organization", writeErrors, "read-only reset result; STATE-014/025", f[9], `Stage 2.3.1 openapi.yaml POST urgency scale reset; response field ${f[0]}`));
}
newRows.push(row("SCR-153", "Notifications и DND", "Reset to defaults · Command button", "POST_api_v1_settings_notification_urgency_scale_reset", "POST", "/api/v1/settings/notification-urgency-scale/reset", "—", "NotificationUrgencyScale", "— (bodyless operation)", "—", "—", "False", "False", "False", "—", "—", "no request body", "If-Match required; ETag returned", "System.Configure", "organization", writeErrors, "enabled/disabled by capability; STATE-014/025", "Подтвердите сброс к значениям 0–24, 25–49, 50–74, 75–100.", "Stage 2.3.1 openapi.yaml operation POST_api_v1_settings_notification_urgency_scale_reset"));

const employeeFields = [
  ["resultType", "Result type · Group discriminator", "SearchSuggestion", "string", "—", "False", "False", 'enum=["object","employee"]', "object", "employee создаёт отдельную группу Employees."],
  ["employee", "Employee payload · Result card", "SearchSuggestion", "EmployeeSearchResult|null", "—", "False", "True", "oneOf EmployeeSearchResult|null", "null", "Заполняется только для resultType=employee."],
  ["employee.userId", "Employee ID · Hidden identifier", "EmployeeSearchResult", "string", "uuid", "True", "False", "—", "—", "Не показывать как primary label; использовать для identity."],
  ["employee.displayName", "Имя · Result primary text", "EmployeeSearchResult", "string", "—", "True", "False", "minLength=1; maxLength=200", "—", "Обязательное отображаемое имя."],
  ["employee.departmentId", "Department ID · Hidden relation", "EmployeeSearchResult", "string|null", "uuid", "False", "True", "—", "null", "Nullable/redacted relation; не показывать UUID пользователю."],
  ["employee.departmentName", "Отдел · Result secondary text", "EmployeeSearchResult", "string|null", "—", "False", "True", "maxLength=200", "null", "При null/redaction показать нейтральный placeholder."],
  ["employee.jobTitle", "Должность · Result secondary text", "EmployeeSearchResult", "string|null", "—", "False", "True", "maxLength=200", "null", "Показывать только при наличии."],
  ["employee.accountStatus", "Статус · Text/icon badge", "EmployeeSearchResult", "string", "—", "True", "False", 'enum=["active","blocked","inactive"]', "—", "Статус передаётся текстом и иконкой, не только цветом."],
  ["employee.deepLink", "Открыть сотрудника · Deep link", "EmployeeSearchResult", "string", "uri", "True", "False", "maxLength=2048", "—", "Enter открывает deepLink; permission повторно проверяется."],
  ["employee.isRedacted", "Redaction · Accessible marker", "EmployeeSearchResult", "boolean", "—", "True", "False", "—", "—", "True скрывает nullable поля без раскрытия скрытого значения."],
];
for (const f of employeeFields) {
  newRows.push(row("SCR-133,SCR-134", "Глобальный поиск / Полные результаты", f[1], "GET_api_v1_search", "GET", "/api/v1/search", "(query parameters)", "SearchPage", f[0], f[3], f[4], f[5], f[6], "True", f[7], f[8], "response-only", "cursor bound to visibility policy", "Search.Use", f[0] === "employee.accountStatus" ? "User.Block controls blocked visibility" : "server authorization/redaction", searchErrors, f[6] === "True" ? "STATE-030 partial access" : "read-only result", f[9], `Stage 2.3.1 openapi.yaml#/components/schemas/${f[2]}/${f[0].replace("employee.", "")}`));
}

const startRow = base.length;
sheet.getRangeByIndexes(0, 0, base.length, header.length).values = base;
sheet.getRangeByIndexes(startRow, 0, newRows.length, header.length).values = newRows;

const allValues = sheet.getUsedRange().values;
const csvEscape = (value) => {
  const text = value === null || value === undefined ? "" : String(value);
  return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
};
const serialized = allValues.map((r) => r.map(csvEscape).join(",")).join("\r\n") + "\r\n";
await fs.writeFile(output, serialized, "utf8");

const preview = workbook.worksheets.add("Stage 3.5 Delta Preview");
preview.showGridLines = false;
preview.getRangeByIndexes(0, 0, newRows.length + 1, header.length).values = [header, ...newRows];
preview.freezePanes.freezeRows(1);
preview.getRange(`A1:Y1`).format = {
  fill: "#1F4E78",
  font: { bold: true, color: "#FFFFFF" },
  wrapText: true,
  borders: { preset: "outside", style: "thin", color: "#9EADBA" },
};
preview.getRange(`A2:Y${newRows.length + 1}`).format = {
  font: { color: "#1F2937" },
  wrapText: true,
  verticalAlignment: "top",
  borders: { insideHorizontal: { style: "thin", color: "#D7DEE5" } },
};
preview.getRange("A:Y").format.columnWidth = 18;
preview.getRange("B:C").format.columnWidth = 28;
preview.getRange("V:Y").format.columnWidth = 34;
preview.getRange(`A1:Y${newRows.length + 1}`).format.autofitRows();

const check = await workbook.inspect({
  kind: "table",
  sheetId: "Traceability",
  range: `A${startRow + 1}:Y${startRow + newRows.length}`,
  include: "values",
  tableMaxRows: 4,
  tableMaxCols: 25,
  maxChars: 5000,
});
console.log(check.ndjson);
console.log(JSON.stringify({
  baseRows: base.length - 1,
  newRows: newRows.length,
  finalRows: allValues.length - 1,
  newContractControls: 20,
  fieldBackedControls: 19,
  unverified: serialized.match(/unverified/gi)?.length ?? 0,
  provisional: serialized.match(/provisional/gi)?.length ?? 0,
}));

const rendered = await workbook.render({ sheetName: "Stage 3.5 Delta Preview", range: `A1:Y${newRows.length + 1}`, scale: 0.8, format: "png" });
await fs.writeFile(previewPath, new Uint8Array(await rendered.arrayBuffer()));
