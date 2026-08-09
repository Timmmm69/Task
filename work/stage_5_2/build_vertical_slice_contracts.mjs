import fs from "node:fs/promises";
import path from "node:path";

const root = path.resolve(".");
const flowInventoryPath = path.join(root, "work/stage_5_2/Flow_Design_Inventory_0.1.csv");
const componentInventoryPath = path.join(root, "work/stage_5_2/Component_Inventory_0.1.csv");
const outputCsvPath = path.join(root, "work/stage_5_2/Vertical_Slice_Scenario_Contracts_0.1.csv");
const outputMdPath = path.join(root, "work/stage_5_2/Vertical_Slice_Scenario_Contracts_0.1.md");

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

function splitRefs(value) {
  return String(value ?? "")
    .split(";")
    .map((item) => item.trim())
    .filter(Boolean);
}

function unique(values) {
  return [...new Set(values.filter(Boolean))];
}

const flowInventory = parseCsv(await fs.readFile(flowInventoryPath, "utf8"));
const componentInventory = parseCsv(await fs.readFile(componentInventoryPath, "utf8"));

const selectedFlowIds = [
  "FLOW-001",
  "FLOW-002",
  "FLOW-004",
  "FLOW-005",
  "FLOW-019",
  "FLOW-022",
  "FLOW-023",
  "FLOW-024",
  "FLOW-025",
  "FLOW-034",
];

const sliceByFlow = {
  "FLOW-001": "VS-01 Auth & Bootstrap",
  "FLOW-002": "VS-01 Auth & Bootstrap",
  "FLOW-004": "VS-02 Task Creation",
  "FLOW-005": "VS-02 Task Creation",
  "FLOW-019": "VS-03 Search & Redaction",
  "FLOW-022": "VS-04 Resilience & Conflict",
  "FLOW-023": "VS-04 Resilience & Conflict",
  "FLOW-024": "VS-04 Resilience & Conflict",
  "FLOW-025": "VS-04 Resilience & Conflict",
  "FLOW-034": "VS-02 Task Creation",
};

const fixtureByFlow = {
  "FLOW-001": "First-run Windows client; reachable approved LAN endpoint; valid employee credentials; empty authorized cache",
  "FLOW-002": "Previously signed-in employee; valid refresh session; populated authorized cache",
  "FLOW-004": "Authenticated user with Task create capability; populated Today/Tasks data",
  "FLOW-005": "Authenticated user with quick-create capability; Shell available",
  "FLOW-019": "Authenticated user; mixed task/project/employee results; at least one redacted or unavailable employee target",
  "FLOW-022": "Authenticated user with populated cache; server becomes unavailable during normal work",
  "FLOW-023": "Server unavailable; authorized cache available; business writes must be prevented",
  "FLOW-024": "Client in read-only/reconnecting mode; server returns; cursor and scope require validation",
  "FLOW-025": "Editable task with local draft; server version changes before save",
  "FLOW-034": "Authenticated user; Inbox capture exists and can be converted to a full task",
};

const acceptanceByFlow = {
  "FLOW-001": "Configure/verify endpoint when required → authenticate → bootstrap authorized data → land in Shell/Today without stale unauthorized content",
  "FLOW-002": "Refresh session → restore authorized Shell and cache → land on last safe context without exposing stale unauthorized data",
  "FLOW-004": "Open editor → validate required fields → create versioned Task → show authoritative result and return focus deterministically",
  "FLOW-005": "Open quick create → enter minimum valid task → submit → confirm creation and preserve keyboard context",
  "FLOW-019": "Enter query → navigate grouped results by keyboard → show redaction/unavailable semantics → open only authorized target",
  "FLOW-022": "Detect outage → announce connection loss → switch to honest read-only cache mode → expose diagnostics/retry without accepting writes",
  "FLOW-023": "Browse authorized cached data → disable prohibited writes with reasons → preserve selection/focus and disclose staleness",
  "FLOW-024": "Reconnect → authenticate/synchronize → validate cursor/scope → refresh content → restore write capability only after authoritative readiness",
  "FLOW-025": "Detect version conflict → preserve local draft → explain conflict → allow reload/compare/reapply/discard only where authorized",
  "FLOW-034": "Capture Inbox item → open conversion path → add required task fields → create Task and resolve source Inbox item deterministically",
};

const keyboardByFlow = {
  "FLOW-001": "Tab/Shift+Tab through endpoint and sign-in; Enter submits; Esc closes non-blocking dialog; focus returns to failing field or first actionable recovery",
  "FLOW-002": "No pointer required; focus restores to safe Shell target after session refresh",
  "FLOW-004": "Tab/Shift+Tab across form; picker navigation with arrows; Enter confirms; Esc preserves draft/returns focus",
  "FLOW-005": "Shortcut opens quick create; Enter submits valid minimum; Esc closes and returns focus to invocation target",
  "FLOW-019": "Shortcut focuses search; Up/Down navigates groups/results; Enter opens; Esc clears/closes in deterministic order",
  "FLOW-022": "Banner/status reachable without focus theft; retry/diagnostics keyboard operable",
  "FLOW-023": "Disabled actions expose reason without dead-end focus; list and inspector remain keyboard navigable",
  "FLOW-024": "Reconnection announcements do not steal focus; refreshed target remains deterministic",
  "FLOW-025": "Conflict actions and compare content are fully keyboard reachable; closing returns to preserved draft",
  "FLOW-034": "Capture and conversion complete with keyboard; validation focuses first invalid field; success returns to source context",
};

const contractRows = selectedFlowIds.map((flowId) => {
  const flow = flowInventory.find((row) => row["FLOW ID"] === flowId);
  if (!flow) throw new Error(`Missing selected flow ${flowId}`);
  const scrIds = splitRefs(flow["SCR references"]);
  const scrRows = scrIds.map((scrId) => componentInventory.find((row) => row["SCR ID"] === scrId)).filter(Boolean);
  const states = unique(scrRows.flatMap((row) => splitRefs(row.States)));
  const errors = unique(scrRows.flatMap((row) => splitRefs(row.Errors)));
  const entryPoints = unique(scrRows.flatMap((row) => splitRefs(row["Entry points"])));
  return {
    "Slice": sliceByFlow[flowId],
    "FLOW ID": flowId,
    "Flow": flow.Flow,
    "Roles": flow.Roles,
    "Permission": flow.Permission,
    "API": flow.API,
    "Outcome": flow.Outcome,
    "SCR references": flow["SCR references"],
    "Entry points": entryPoints.join("; "),
    "Required states": states.join("; "),
    "Required errors": errors.join("; "),
    "Shared components": flow["Shared components"],
    "Test fixture": fixtureByFlow[flowId],
    "Critical-path acceptance": acceptanceByFlow[flowId],
    "Keyboard contract": keyboardByFlow[flowId],
    "Required design evidence": flow["Required design evidence"],
    "Accessibility evidence": "Focus order/return; UIA names/roles/states; non-color semantics; Narrator/NVDA script; 200% scaling",
    "Contract status": "Scenario contract ready",
    "Visual status": "Pending VIS-001",
  };
});

const headers = Object.keys(contractRows[0]);
const csv = [
  headers.map(csvEscape).join(","),
  ...contractRows.map((row) => headers.map((header) => csvEscape(row[header])).join(",")),
].join("\r\n") + "\r\n";
await fs.writeFile(outputCsvPath, "\uFEFF" + csv, "utf8");

const sliceCounts = Object.entries(
  contractRows.reduce((acc, row) => {
    acc[row.Slice] = (acc[row.Slice] ?? 0) + 1;
    return acc;
  }, {}),
);
const rowsWithoutScr = contractRows.filter((row) => !row["SCR references"]);
const rowsWithoutStates = contractRows.filter((row) => !row["Required states"]);
const rowsWithoutAcceptance = contractRows.filter((row) => !row["Critical-path acceptance"]);

const markdown = `# Stage 5.2 — Vertical Slice Scenario Contracts 0.1

**Date:** 2026-07-28  
**Status:** scenario contracts complete; frames and prototype await \`VIS-001\`  
**Coverage:** ${contractRows.length}/10 planned P0 vertical-slice flows

## Slice composition

| Slice | FLOW contracts |
|---|---:|
${sliceCounts.map(([slice, count]) => `| ${slice} | ${count} |`).join("\n")}

## Validation

| Check | Expected | Actual | Result |
|---|---:|---:|---|
| Planned vertical-slice flows | 10 | ${contractRows.length} | PASS |
| Rows without SCR references | 0 | ${rowsWithoutScr.length} | ${rowsWithoutScr.length === 0 ? "PASS" : "FAIL"} |
| Rows without required states | 0 | ${rowsWithoutStates.length} | ${rowsWithoutStates.length === 0 ? "PASS" : "FAIL"} |
| Rows without acceptance contract | 0 | ${rowsWithoutAcceptance.length} | ${rowsWithoutAcceptance.length === 0 ? "PASS" : "FAIL"} |

## Contract

Each row in \`Vertical_Slice_Scenario_Contracts_0.1.csv\` defines the source flow, roles/permissions, APIs, SCR surfaces, entry points, required states/errors, realistic fixture, critical-path acceptance, keyboard behavior and accessibility evidence.

The four slices are Auth & Bootstrap, Task Creation, Search & Redaction, and Resilience & Conflict. They are the execution contract for tasks S5-0209 through S5-0214.

## Boundary

This artifact is not a visual frame or interactive prototype. Layout, styling, component instances and final accessibility evidence remain blocked by \`VIS-001\`.
`;
await fs.writeFile(outputMdPath, markdown, "utf8");

if (contractRows.length !== selectedFlowIds.length) throw new Error("Vertical-slice flow count mismatch");
if (rowsWithoutScr.length || rowsWithoutStates.length || rowsWithoutAcceptance.length) {
  throw new Error("Vertical-slice contract validation failed");
}

console.log(JSON.stringify({
  contracts: contractRows.length,
  slices: sliceCounts.length,
  outputCsvPath,
  outputMdPath,
}, null, 2));
