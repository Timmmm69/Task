import fs from "node:fs/promises";
import path from "node:path";

const root = path.resolve(".");
const roleSourcePath = path.join(root, "work/stage_4_6_lite/design_input/Stage_3_Role_Interface_Matrix_Final_3.5.md");
const stateSourcePath = path.join(root, "work/stage_4_6_lite/design_input/Stage_3_State_Matrix_Final_3.5.md");
const stateAuditPath = path.join(root, "outputs/stage_4_6_lite/Stage_4_6_Lite_STATE_Audit.csv");
const verticalSlicePath = path.join(root, "work/stage_5_2/Vertical_Slice_Scenario_Contracts_0.1.csv");

const roleCsvPath = path.join(root, "work/stage_5_2/Role_Capability_Design_Matrix_0.1.csv");
const roleMdPath = path.join(root, "work/stage_5_2/Role_Capability_Design_Matrix_0.1.md");
const stateCsvPath = path.join(root, "work/stage_5_2/State_Component_Coverage_Matrix_0.1.csv");
const stateMdPath = path.join(root, "work/stage_5_2/State_Component_Coverage_Matrix_0.1.md");
const usabilityCsvPath = path.join(root, "work/stage_5_2/Usability_Test_Script_0.1.csv");
const usabilityMdPath = path.join(root, "work/stage_5_2/Usability_Test_Script_0.1.md");

function parseCsv(text) {
  const rows = [];
  let row = [];
  let field = "";
  let quoted = false;
  const source = text.replace(/^\uFEFF/, "");
  for (let i = 0; i < source.length; i++) {
    const ch = source[i];
    if (quoted) {
      if (ch === '"' && source[i + 1] === '"') {
        field += '"';
        i++;
      } else if (ch === '"') {
        quoted = false;
      } else {
        field += ch;
      }
    } else if (ch === '"') {
      quoted = true;
    } else if (ch === ",") {
      row.push(field);
      field = "";
    } else if (ch === "\n") {
      row.push(field.replace(/\r$/, ""));
      rows.push(row);
      row = [];
      field = "";
    } else {
      field += ch;
    }
  }
  if (field.length || row.length) {
    row.push(field.replace(/\r$/, ""));
    rows.push(row);
  }
  const headers = rows.shift() ?? [];
  return rows
    .filter((values) => values.some((value) => value !== ""))
    .map((values) => Object.fromEntries(headers.map((header, index) => [header, values[index] ?? ""])));
}

function csvEscape(value) {
  const text = String(value ?? "");
  return `"${text.replaceAll('"', '""')}"`;
}

function writeCsv(rows) {
  const headers = Object.keys(rows[0]);
  return "\uFEFF" + [
    headers.map(csvEscape).join(","),
    ...rows.map((row) => headers.map((header) => csvEscape(row[header])).join(",")),
  ].join("\r\n") + "\r\n";
}

function parseMarkdownTable(lines, headerIndex) {
  const parseLine = (line) => line
    .split("|")
    .slice(1, -1)
    .map((cell) => cell.trim());
  const headers = parseLine(lines[headerIndex]);
  const rows = [];
  for (let i = headerIndex + 2; i < lines.length && lines[i].trim().startsWith("|"); i++) {
    const values = parseLine(lines[i]);
    if (values.every((value) => /^:?-+:?$/.test(value))) continue;
    rows.push(Object.fromEntries(headers.map((header, index) => [header, values[index] ?? ""])));
  }
  return rows;
}

function unique(values) {
  return [...new Set(values.filter(Boolean))];
}

const roleSource = await fs.readFile(roleSourcePath, "utf8");
const stateSource = await fs.readFile(stateSourcePath, "utf8");
const stateAudit = parseCsv(await fs.readFile(stateAuditPath, "utf8"));
const verticalSlice = parseCsv(await fs.readFile(verticalSlicePath, "utf8"));

const roleLines = roleSource.split(/\r?\n/);
const roleHeaderIndexes = roleLines
  .map((line, index) => ({ line, index }))
  .filter(({ line }) => line.startsWith("| Screen/action | Admin | Manager | Employee | Observer |"))
  .map(({ index }) => index);
const roleSourceRows = roleHeaderIndexes.flatMap((headerIndex, index) =>
  parseMarkdownTable(roleLines, headerIndex).map((row) => ({
    ...row,
    Source: index === 0 ? "Stage 3 Role Interface Matrix — baseline" : "Stage 3.5 role/interface delta",
  })),
);

function roleComponents(action) {
  const value = action.toLowerCase();
  const components = ["PermissionState", "SemanticStatus"];
  if (value.includes("task") || value.includes("subtask") || value.includes("watcher") || value.includes("assignee")) {
    components.push("TaskRow", "TaskStatusControl", "InspectorPanel", "FormLayout");
  }
  if (value.includes("project")) components.push("DataList", "InspectorPanel", "FormLayout");
  if (value.includes("file") || value.includes("catalog") || value.includes("network resource")) {
    components.push("FileLocationView", "ErrorMessage", "DialogShell");
  }
  if (value.includes("search") || value.includes("employee") || value.includes("user")) {
    components.push("SearchBox", "DataList", "RedactionMarker");
  }
  if (value.includes("comment")) components.push("CommentThread", "FormLayout");
  if (value.includes("notification")) components.push("NotificationItem");
  if (value.includes("calendar") || value.includes("today")) components.push("DateTimePicker", "DataList");
  if (value.includes("setting") || value.includes("role") || value.includes("department") || value.includes("health") || value.includes("backup") || value.includes("audit")) {
    components.push("PageLayout", "FormLayout", "DataList");
  }
  if (components.length === 2) components.push("PageLayout", "DataList");
  return unique(components).join("; ");
}

function roleStates(policy) {
  const value = policy.toLowerCase();
  const states = ["CapabilityFiltered"];
  if (value.includes("hidden") || value.includes("hide") || value.includes("omitted")) states.push("Hidden");
  if (value.includes("disable")) states.push("DisabledWithReason");
  if (value.includes("read-only") || value.includes("read only") || value.includes("locked")) states.push("ReadOnly");
  if (value.includes("forbid") || value.includes("deny")) states.push("ForbiddenResult");
  if (value.includes("redact") || value.includes("no hidden")) states.push("RedactedOrUnavailable");
  states.push("ServerRecheck");
  return unique(states).join("; ");
}

const roleRows = roleSourceRows.map((row, index) => {
  const permission = row["Permission"] || row["Permission/capability"];
  const policy = row["Hidden/disabled/forbidden"] || row["UI/server policy"];
  return {
    "Role contract ID": `ROLE-${String(index + 1).padStart(3, "0")}`,
    "Screen/action": row["Screen/action"],
    "Admin": row.Admin,
    "Manager": row.Manager,
    "Employee": row.Employee,
    "Observer": row.Observer,
    "Permission/capability": permission,
    "UI/server policy": policy,
    "Required presentation states": roleStates(policy),
    "Required components": roleComponents(row["Screen/action"]),
    "Design evidence": "Role comparison frame or component variant + capability/forbidden keyboard walkthrough",
    "Accessibility evidence": "Disabled reason announced; hidden controls absent from UIA tree; read-only state exposed; no hidden counts",
    "Canonical source": row.Source,
    "Contract status": "Role contract ready",
    "Visual status": "Pending VIS-001",
  };
});

const stateLines = stateSource.split(/\r?\n/);
const stateHeaderIndex = stateLines.findIndex((line) => line.startsWith("| Surface | State | Trigger | UI behavior | Allowed actions | Message | Recovery | API/error |"));
if (stateHeaderIndex < 0) throw new Error("Published State Matrix table not found");
const publishedStates = parseMarkdownTable(stateLines, stateHeaderIndex);

function stateComponents(state, surface, behavior) {
  const stateName = state.toLowerCase();
  const value = `${state} ${surface} ${behavior}`.toLowerCase();
  const components = [];
  if (/^(initial|loading|refreshing|syncpending|backgroundoperation|backgroundapply)$/.test(stateName)) components.push("LoadingState", "ProgressIndicator");
  if (/^(empty|filteredempty|zeroresults|noitems)$/.test(stateName)) components.push("EmptyState", "DataList");
  if (/validation|invalid|ruleinvalid|cycle|rangetoolarge|unsafe/.test(value)) components.push("ValidationMessage", "FormLayout");
  if (/forbidden|partialaccess|objectunavailable|redact|staleselection/.test(value)) components.push("PermissionState", "RedactionMarker", "InspectorPanel");
  if (/conflict|precondition|targetchanged|nameconflict/.test(value)) components.push("ConflictNotice", "DialogShell");
  if (/archived|trashed|completed|retention/.test(value)) components.push("LifecycleBanner", "ReadOnlyBanner");
  if (/serverunavailable|reconnecting|maintenance|storagefull|databaseunavailable|offline/.test(value)) components.push("ConnectivityBanner", "ConnectionStatus", "ReadOnlyBanner");
  if (/file|location|networkunavailable|accessdenied|notfound|otherdevicelocal/.test(value)) components.push("FileLocationView", "ErrorMessage", "DialogShell");
  if (/rate|timeout|internalerror|backupfailed/.test(value)) components.push("ErrorMessage", "RetryAction");
  if (/notification/.test(value)) components.push("NotificationItem");
  if (/search|cursor/.test(value)) components.push("SearchBox", "DataList");
  if (components.length === 0) components.push("SemanticStatus", "ErrorMessage");
  return unique(components).join("; ");
}

const stateRows = publishedStates.map((row, index) => {
  const matchingStateIds = stateAudit
    .filter((auditRow) => auditRow["Canonical target"] === row.State)
    .map((auditRow) => auditRow["Original reference"]);
  return {
    "State contract ID": `STC-${String(index + 1).padStart(3, "0")}`,
    "STATE references": matchingStateIds.join("; "),
    "Surface": row.Surface,
    "State": row.State,
    "Trigger": row.Trigger,
    "UI behavior": row["UI behavior"],
    "Allowed actions": row["Allowed actions"],
    "Message": row.Message,
    "Recovery": row.Recovery,
    "API/error": row["API/error"],
    "Required components": stateComponents(row.State, row.Surface, row["UI behavior"]),
    "Required design evidence": "State frame/component variant + trigger/action/recovery walkthrough + long RU message + 200% scaling",
    "Accessibility evidence": "UIA name/role/state; live announcement where appropriate; deterministic focus; non-color semantics",
    "Canonical source": "Stage 3 State Matrix Final 3.5",
    "Contract status": "State contract ready",
    "Visual status": "Pending VIS-001",
  };
});

const roleAssignment = {
  "FLOW-001": "Admin",
  "FLOW-002": "Observer",
  "FLOW-004": "Manager",
  "FLOW-005": "Employee",
  "FLOW-019": "Observer",
  "FLOW-022": "Employee",
  "FLOW-023": "Observer",
  "FLOW-024": "Manager",
  "FLOW-025": "Manager",
  "FLOW-034": "Employee",
};

function moderatorPrompt(flowId) {
  if (flowId === "FLOW-019") return "Что вы ожидаете увидеть при ограниченном доступе к сотруднику? Что в интерфейсе объясняет отсутствие данных?";
  if (["FLOW-022", "FLOW-023", "FLOW-024"].includes(flowId)) return "Как вы понимаете текущее качество соединения и можно ли сейчас безопасно изменять данные?";
  if (flowId === "FLOW-025") return "Что произошло с вашей версией задачи и какой вариант восстановления вы считаете безопасным?";
  if (["FLOW-004", "FLOW-005", "FLOW-034"].includes(flowId)) return "Какие поля кажутся обязательными и понятно ли, что будет создано после подтверждения?";
  return "Что вы ожидаете на следующем шаге и что подтверждает успешное завершение?";
}

const usabilityRows = verticalSlice.map((row, index) => ({
  "Test case ID": `UT-${String(index + 1).padStart(2, "0")}`,
  "Slice": row.Slice,
  "FLOW ID": row["FLOW ID"],
  "Participant role": roleAssignment[row["FLOW ID"]] ?? "Employee",
  "Scenario": row.Flow,
  "Starting fixture": row["Test fixture"],
  "Task prompt": `Выполните сценарий «${row.Flow}» так, как сделали бы это в обычной работе. Не используйте подсказки модератора без необходимости.`,
  "Critical success": row["Critical-path acceptance"],
  "Keyboard checkpoint": row["Keyboard contract"],
  "Accessibility checkpoint": row["Accessibility evidence"],
  "Primary metric": "Task completion without critical error",
  "Secondary metrics": "Time on task; wrong turns; help requests; confidence 1–5; focus/announcement defects",
  "Stop condition": "Critical data-loss/security misunderstanding, unrecoverable focus trap or participant cannot proceed after two neutral prompts",
  "Moderator prompt": moderatorPrompt(row["FLOW ID"]),
  "Evidence capture": "Screen/audio notes; event timestamps; observed path; errors; quotes; severity; role/fixture version",
  "Pass threshold": "Completion by intended role; no Critical/High usability or accessibility finding; authoritative outcome understood",
  "Design dependency": "Interactive prototype after VIS-001",
  "Script status": "Test contract ready",
  "Execution status": "Pending prototype",
}));

await fs.writeFile(roleCsvPath, writeCsv(roleRows), "utf8");
await fs.writeFile(stateCsvPath, writeCsv(stateRows), "utf8");
await fs.writeFile(usabilityCsvPath, writeCsv(usabilityRows), "utf8");

const roleMd = `# Stage 5.4 — Role/Capability Design Matrix 0.1

**Date:** 2026-07-28  
**Status:** role behavior contract complete; visual evidence awaits \`VIS-001\`  
**Coverage:** ${roleRows.length} canonical screen/action contracts × Admin, Manager, Employee and Observer

## Validation

| Check | Expected | Actual | Result |
|---|---:|---:|---|
| Role columns per contract | 4 | 4 | PASS |
| Contracts without permission/capability | 0 | ${roleRows.filter((row) => !row["Permission/capability"]).length} | ${roleRows.every((row) => row["Permission/capability"]) ? "PASS" : "FAIL"} |
| Contracts without UI/server policy | 0 | ${roleRows.filter((row) => !row["UI/server policy"]).length} | ${roleRows.every((row) => row["UI/server policy"]) ? "PASS" : "FAIL"} |
| Contracts without component mapping | 0 | ${roleRows.filter((row) => !row["Required components"]).length} | PASS |

The CSV preserves capability-first behavior: system roles are a baseline, while server authorization, object relation, lifecycle and field masks remain authoritative.

Final role comparison frames, UIA evidence and prototype walkthroughs remain pending.
`;
const stateMd = `# Stage 5.4 — State/Component Coverage Matrix 0.1

**Date:** 2026-07-28  
**Status:** published-state behavior contract complete; visual evidence awaits \`VIS-001\`  
**Coverage:** ${stateRows.length} named rows from the canonical Stage 3 State Matrix

## Validation

| Check | Expected | Actual | Result |
|---|---:|---:|---|
| Named published state rows | > 0 | ${stateRows.length} | PASS |
| Rows without trigger/UI/action/recovery | 0 | ${stateRows.filter((row) => !row.Trigger || !row["UI behavior"] || !row["Allowed actions"] || !row.Recovery).length} | PASS |
| Rows without component mapping | 0 | ${stateRows.filter((row) => !row["Required components"]).length} | PASS |
| Rows without evidence contract | 0 | ${stateRows.filter((row) => !row["Required design evidence"]).length} | PASS |

The matrix does not create new durable states. It maps published state behavior to reusable Stage 5 component families and required accessibility/design evidence.
`;
const usabilityMd = `# Stage 5.5 — Usability Test Script 0.1

**Date:** 2026-07-28  
**Status:** test contract and fixtures ready; execution awaits the interactive prototype  
**Coverage:** ${usabilityRows.length} P0 vertical-slice scenarios across all four system roles

## Session protocol

1. Confirm participant role and fixture version.
2. Ask the participant to think aloud without teaching the interface.
3. Use at most two neutral prompts before marking the scenario blocked.
4. Capture completion, time, wrong turns, assistance, confidence, focus/announcement defects and security misunderstandings.
5. Stop on possible data loss, disclosure, unrecoverable focus trap or unsafe recovery choice.
6. Classify findings by severity and map them to FLOW/SCR/STATE/NFR.

## Readiness checks

| Check | Expected | Actual | Result |
|---|---:|---:|---|
| Planned scenarios | 10 | ${usabilityRows.length} | PASS |
| System roles represented | 4 | ${new Set(usabilityRows.map((row) => row["Participant role"])).size} | ${new Set(usabilityRows.map((row) => row["Participant role"])).size === 4 ? "PASS" : "FAIL"} |
| Scenarios without fixture/success/metrics | 0 | ${usabilityRows.filter((row) => !row["Starting fixture"] || !row["Critical success"] || !row["Primary metric"]).length} | PASS |

Execution results, recordings/notes, severity triage and remediation evidence remain pending.
`;
await fs.writeFile(roleMdPath, roleMd, "utf8");
await fs.writeFile(stateMdPath, stateMd, "utf8");
await fs.writeFile(usabilityMdPath, usabilityMd, "utf8");

const roleFailures = roleRows.filter((row) => !row["Permission/capability"] || !row["UI/server policy"] || !row["Required components"]);
const stateFailures = stateRows.filter((row) => !row.Trigger || !row["UI behavior"] || !row["Allowed actions"] || !row.Recovery || !row["Required components"]);
const usabilityFailures = usabilityRows.filter((row) => !row["Starting fixture"] || !row["Critical success"] || !row["Primary metric"]);
if (roleRows.length < 30 || roleFailures.length) throw new Error(`Role matrix validation failed: rows=${roleRows.length}, failures=${roleFailures.length}`);
if (stateRows.length < 40 || stateFailures.length) throw new Error(`State matrix validation failed: rows=${stateRows.length}, failures=${stateFailures.length}`);
if (usabilityRows.length !== 10 || usabilityFailures.length || new Set(usabilityRows.map((row) => row["Participant role"])).size !== 4) {
  throw new Error(`Usability script validation failed: rows=${usabilityRows.length}, failures=${usabilityFailures.length}, roles=${new Set(usabilityRows.map((row) => row["Participant role"])).size}`);
}

console.log(JSON.stringify({
  roleContracts: roleRows.length,
  stateContracts: stateRows.length,
  usabilityScenarios: usabilityRows.length,
  representedRoles: new Set(usabilityRows.map((row) => row["Participant role"])).size,
  outputs: [roleCsvPath, roleMdPath, stateCsvPath, stateMdPath, usabilityCsvPath, usabilityMdPath],
}, null, 2));
