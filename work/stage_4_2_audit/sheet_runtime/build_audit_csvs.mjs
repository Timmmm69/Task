import fs from "node:fs/promises";
import path from "node:path";
import { Workbook } from "@oai/artifact-tool";

const workDir = "C:\\Users\\novik\\Таск\\work\\stage_4_2_audit";
const outputDir = "C:\\Users\\novik\\Таск\\outputs\\stage_4_2_audit";
const payload = JSON.parse(
  await fs.readFile(path.join(workDir, "audit_payload.json"), "utf8"),
);

const definitions = [
  {
    key: "findings",
    file: "Stage_4_2_Findings.csv",
    sheet: "Findings",
    headers: [
      "Audit ID",
      "Severity",
      "Category",
      "Artifact",
      "Location",
      "Related IDs",
      "Source of truth",
      "Expected",
      "Actual",
      "Defect",
      "Consequence",
      "Recommended fix",
      "Verification",
      "Confidence",
      "Status",
    ],
  },
  {
    key: "traceability_rows",
    file: "Stage_4_2_Traceability_Audit.csv",
    sheet: "Traceability",
    headers: [
      "Requirement",
      "Type",
      "Module",
      "Source",
      "Source present",
      "API refs valid",
      "Permission refs valid",
      "Stable error refs valid",
      "AC refs valid",
      "SCR",
      "FLOW",
      "STATE",
      "Status",
      "Notes",
    ],
  },
  {
    key: "api_rows",
    file: "Stage_4_2_API_Coverage_Audit.csv",
    sheet: "API Coverage",
    headers: [
      "Operation ID",
      "Method",
      "Path",
      "OpenAPI line",
      "Request",
      "Response",
      "Permission",
      "HTTP codes",
      "Idempotency",
      "Locking",
      "FR IDs",
      "AC IDs",
      "UX IDs",
      "Permission refs valid",
      "Stable error refs valid",
      "Coverage status",
      "Evidence",
    ],
  },
  {
    key: "fr_br_ac_rows",
    file: "Stage_4_2_FR_BR_AC_Audit.csv",
    sheet: "FR BR AC",
    headers: [
      "Entity ID",
      "Type",
      "Module",
      "Parent IDs",
      "Direct FR",
      "Parent exists",
      "Source present",
      "Given",
      "When",
      "Then",
      "Vague terms",
      "Status",
      "Notes",
    ],
  },
];

function columnName(index) {
  let result = "";
  let current = index + 1;
  while (current > 0) {
    const remainder = (current - 1) % 26;
    result = String.fromCharCode(65 + remainder) + result;
    current = Math.floor((current - 1) / 26);
  }
  return result;
}

function csvCell(value) {
  const text = value === null || value === undefined ? "" : String(value);
  if (/[",\r\n]/.test(text)) {
    return `"${text.replaceAll('"', '""')}"`;
  }
  return text;
}

function csvText(headers, rows) {
  return (
    "\uFEFF" +
    [headers, ...rows]
      .map((row) => row.map(csvCell).join(","))
      .join("\r\n") +
    "\r\n"
  );
}

await fs.mkdir(outputDir, { recursive: true });
const verification = [];

for (const definition of definitions) {
  const records = payload[definition.key];
  const rows = records.map((record) =>
    definition.headers.map((header) => record[header] ?? ""),
  );
  const matrix = [definition.headers, ...rows];
  const workbook = Workbook.create();
  const sheet = workbook.worksheets.add(definition.sheet);
  sheet.showGridLines = false;
  sheet.freezePanes.freezeRows(1);
  const used = sheet.getRangeByIndexes(
    0,
    0,
    matrix.length,
    definition.headers.length,
  );
  used.values = matrix;
  const header = sheet.getRangeByIndexes(0, 0, 1, definition.headers.length);
  header.format = {
    fill: "#17365D",
    font: { bold: true, color: "#FFFFFF" },
    wrapText: true,
    verticalAlignment: "center",
  };
  header.format.rowHeight = 32;
  const body = sheet.getRangeByIndexes(
    1,
    0,
    Math.max(rows.length, 1),
    definition.headers.length,
  );
  body.format = {
    font: { color: "#1F2937" },
    verticalAlignment: "top",
  };
  used.format.borders = {
    insideHorizontal: { style: "thin", color: "#E5E7EB" },
    bottom: { style: "thin", color: "#CBD5E1" },
  };

  const previewRows = Math.min(matrix.length, 26);
  const lastColumn = columnName(definition.headers.length - 1);
  const preview = await workbook.render({
    sheetName: definition.sheet,
    range: `A1:${lastColumn}${previewRows}`,
    scale: 0.8,
    format: "png",
  });
  const previewPath = path.join(
    workDir,
    "sheet_runtime",
    `${definition.sheet.replaceAll(" ", "_")}_preview.png`,
  );
  await fs.writeFile(
    previewPath,
    new Uint8Array(await preview.arrayBuffer()),
  );
  const inspect = await workbook.inspect({
    kind: "table",
    range: `${definition.sheet}!A1:${lastColumn}${Math.min(matrix.length, 8)}`,
    include: "values,formulas",
    tableMaxRows: 8,
    tableMaxCols: definition.headers.length,
    maxChars: 5000,
  });
  const outputPath = path.join(outputDir, definition.file);
  await fs.writeFile(outputPath, csvText(definition.headers, rows), "utf8");
  verification.push({
    file: definition.file,
    rows: rows.length,
    columns: definition.headers.length,
    preview: previewPath,
    inspectChars: inspect.ndjson.length,
  });
}

console.log(JSON.stringify(verification, null, 2));
