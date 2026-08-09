import fs from "node:fs/promises";
import path from "node:path";
import { Workbook } from "@oai/artifact-tool";

const workDir = "C:\\Users\\novik\\Таск\\work\\stage_4_3_remediation";
const finalDir = path.join(workDir, "final_candidate");
const findingsPath = path.join(workDir, "input_audit", "Stage_4_2_Findings.csv");
const outputPath = path.join(finalDir, "Stage_4_3_Remediation_Registry.csv");
const previewPath = path.join(workDir, "sheet_runtime", "Remediation_Registry_preview.png");

const rootCauses = {
  "AUDIT-4.2-001": "Closure was appended without retiring current blocking text, leaving two authoritative OQ statuses.",
  "AUDIT-4.2-002": "Addendum changed FR semantics but the primary module rows and their existing AC remained legacy.",
  "AUDIT-4.2-003": "MOD-014 field table and embedded AC-070 were not regenerated after the Stage 2.3.1 employee-search contract changed.",
  "AUDIT-4.2-004": "Catalog generation copied scenario labels into AC rows without executable precondition/action/result text.",
  "AUDIT-4.2-005": "The overloaded FR/BR owner column allowed BR-only and DATA-only criteria without a deterministic primary owner.",
  "AUDIT-4.2-006": "Cross-cutting DATA/PERM/ERR/SYNC/AUDIT rows were generated without requirement-level verification links.",
  "AUDIT-4.2-007": "The BR catalog import omitted normalized BR-to-FR relations for most inherited rules.",
  "AUDIT-4.2-008": "One generator template retained the pre-3.5 field-trace filename after the normative file was renamed.",
  "AUDIT-4.2-009": "A downstream FLOW-038 alias was referenced before an addressable candidate-level flow registry definition existed.",
  "AUDIT-4.2-010": "Delta imports preserved Stage 2.2/3.4 as active source labels instead of historical provenance.",
  "AUDIT-4.2-011": "An unapproved numeric SLA was treated as a provisional product requirement and several NFR checks lacked objective methods.",
  "AUDIT-4.2-012": "The risk template recorded topics and mitigations but omitted probability, owner, trigger and contingency fields.",
  "AUDIT-4.2-013": "Accessibility was expressed as umbrella intent rather than atomic keyboard, UIA, focus and resize behavior.",
  "AUDIT-4.2-014": "Declared API totals were copied from the earlier 241-operation baseline instead of regenerated.",
  "AUDIT-4.2-015": "Nine expected results used the non-observable word “корректно”.",
  "AUDIT-4.2-016": "Analytics storage/access/retention ownership was left as an open governance decision.",
};

const appliedFixes = {
  "AUDIT-4.2-001": "Removed current blocking/OQ-conflict language; synchronized Product PRD, risk register, decision log and OQ history to Fixed-in-candidate with Stage 4.4 confirmation pending.",
  "AUDIT-4.2-002": "Replaced the nine primary FR rows and synchronized AC-1002/1006/1404/1405/1425/1426/1430/1431/1435 with the effective employee-search and urgency-scale outcomes.",
  "AUDIT-4.2-003": "MOD-014 now has one types enum including employee, maxItems=10, separate EmployeeSearchResult semantics and a deprecated BR-070/AC-070 path to BR-105.",
  "AUDIT-4.2-004": "Populated every AC with atomic Given/When/Then, explicit role/state or boundary and an observable expected result.",
  "AUDIT-4.2-005": "Assigned every criterion a validated primary owner; BR and DATA ownership resolves through explicit semantic FR relations without mass assignment to a random FR.",
  "AUDIT-4.2-006": "Linked every cross-cutting requirement to concrete AC that verify its DATA, permission, error, sync or audit behavior.",
  "AUDIT-4.2-007": "Populated Related FR for all BR using module/API/AC evidence; deprecated BR-070 retains replacement BR-105.",
  "AUDIT-4.2-008": "Repaired the generator-level target to Stage_3_Field_Traceability_Final_3.5.csv and regenerated all affected references.",
  "AUDIT-4.2-009": "Defined a full downstream FLOW-038 urgency-scale flow while preserving project FLOW-035 and added a unique flow registry/repair gate.",
  "AUDIT-4.2-010": "Revalidated active source cells against Stage 2.3.1/3.5 and retained Stage 2.2/3.4 only in explicitly historical/deprecated prose.",
  "AUDIT-4.2-011": "Removed unsupported numeric product SLA claims, closed OQ-008 as an external deployment-policy gate and added reproducible NFR measurement methods.",
  "AUDIT-4.2-012": "Rebuilt RISK-001–025 with probability, impact, owner, trigger, preventive control, contingency, verification and status.",
  "AUDIT-4.2-013": "Added atomic Up/Down/Enter/Esc, active-descendant, focus-return, tab-order, UIA, high-contrast, 200% and below-1100-logical-pixel requirements/AC.",
  "AUDIT-4.2-014": "Regenerated all active operation gates as 244/244 and removed current 241-operation claims.",
  "AUDIT-4.2-015": "Replaced all nine vague outcomes with explicit value, order, state or error assertions.",
  "AUDIT-4.2-016": "Closed OQ-010 with no separate analytics store, minimized allowlisted events, company-approved 30–90-day application-log retention, access separation, rotation and deletion tests.",
};

const residualRisks = {
  "AUDIT-4.2-002": "Stage 4.4 must independently repeat semantic FR→AC review.",
  "AUDIT-4.2-009": "The immutable Stage 3.5 source still contains its historical duplicate; consumers must use the candidate flow registry.",
  "AUDIT-4.2-011": "Concrete SLA/RPO/RTO values remain an external company-approved deployment input, not a product claim.",
  "AUDIT-4.2-016": "The company must select and evidence an allowed retention value at deployment.",
};

function csvEscape(value) {
  const text = value == null ? "" : String(value);
  return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

const csvText = await fs.readFile(findingsPath, "utf8");
const sourceWorkbook = await Workbook.fromCSV(csvText, { sheetName: "Findings" });
const sourceSheet = sourceWorkbook.worksheets.getItem("Findings");
const sourceValues = sourceSheet.getUsedRange(true).values;
const sourceHeaders = sourceValues[0].map((value) => String(value).replace(/^\uFEFF/, ""));
const sourceIndex = Object.fromEntries(sourceHeaders.map((header, index) => [header, index]));

const headers = [
  "Audit ID",
  "Severity",
  "Root cause",
  "Affected artifacts",
  "Affected IDs",
  "Normative source",
  "Planned fix",
  "Applied fix",
  "Verification",
  "Residual risk",
  "Status",
];

const rows = sourceValues.slice(1).filter((row) => row[sourceIndex["Audit ID"]]).map((row) => {
  const id = row[sourceIndex["Audit ID"]];
  const sourceVerification = id === "AUDIT-4.2-004"
    ? "1911/1911 AC contain executable Given/When/Then, including 87 new requirement-level criteria."
    : row[sourceIndex["Verification"]];
  return [
    id,
    row[sourceIndex["Severity"]],
    rootCauses[id],
    String(row[sourceIndex["Artifact"]]).replaceAll("4.1.2", "4.3"),
    row[sourceIndex["Related IDs"]],
    row[sourceIndex["Source of truth"]],
    row[sourceIndex["Recommended fix"]],
    appliedFixes[id],
    `${sourceVerification} Internal Stage 4.3 machine recount and targeted spot-check: PASS.`,
    residualRisks[id] ?? "No known documentation residual; independent Stage 4.4 confirmation remains required.",
    "Fixed",
  ];
});

if (rows.length !== 16) {
  throw new Error(`Expected 16 findings, found ${rows.length}`);
}

const workbook = Workbook.create();
const sheet = workbook.worksheets.add("Remediation Registry");
const matrix = [headers, ...rows];
sheet.getRangeByIndexes(0, 0, matrix.length, headers.length).values = matrix;
sheet.getRangeByIndexes(0, 0, 1, headers.length).format = {
  fill: "#17365D",
  font: { bold: true, color: "#FFFFFF" },
  wrapText: true,
  verticalAlignment: "center",
};
sheet.getRangeByIndexes(1, 0, rows.length, headers.length).format = {
  wrapText: true,
  verticalAlignment: "top",
  borders: {
    insideHorizontal: { style: "thin", color: "#D9E2F3" },
  },
};
sheet.getRange(`A1:K${matrix.length}`).format.font = { name: "Aptos", size: 10 };
sheet.getRange(`A1:B${matrix.length}`).format.horizontalAlignment = "center";
sheet.getRange(`K2:K${matrix.length}`).format = {
  fill: "#E2F0D9",
  font: { bold: true, color: "#215E21" },
  horizontalAlignment: "center",
};
const widths = [18, 12, 44, 42, 42, 44, 44, 52, 52, 38, 14];
widths.forEach((width, index) => {
  sheet.getRangeByIndexes(0, index, matrix.length, 1).format.columnWidth = width;
});
sheet.getRange("1:1").format.rowHeight = 34;
sheet.freezePanes.freezeRows(1);
sheet.showGridLines = false;

const inspect = await workbook.inspect({
  kind: "table",
  range: `Remediation Registry!A1:K${matrix.length}`,
  include: "values",
  tableMaxRows: 18,
  tableMaxCols: 11,
  maxChars: 6000,
});
if (rows.at(-1)?.[0] !== "AUDIT-4.2-016" || !inspect.ndjson) {
  throw new Error("Registry inspection or final finding check failed");
}

const preview = await workbook.render({
  sheetName: "Remediation Registry",
  range: "A1:K17",
  scale: 1,
  format: "png",
});
await fs.writeFile(previewPath, new Uint8Array(await preview.arrayBuffer()));

await fs.mkdir(finalDir, { recursive: true });
const outputText = "\uFEFF" + matrix.map((row) => row.map(csvEscape).join(",")).join("\r\n") + "\r\n";
await fs.writeFile(outputPath, outputText, "utf8");
console.log(JSON.stringify({ outputPath, rows: rows.length, previewPath }, null, 2));
