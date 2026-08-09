import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { Workbook } from "@oai/artifact-tool";

const root = path.resolve("..");
const candidate = path.join(root, "candidate_4_5");
const reportDir = path.join(root, "reports", "csv_previews");
const files = [
  "Stage_4_Business_Rules_Catalog_4.5.csv",
  "Stage_4_Acceptance_Criteria_Catalog_4.5.csv",
  "Stage_4_NFR_Catalog_4.5.csv",
  "Stage_4_Requirements_Traceability_4.5.csv",
  "Stage_4_5_Remediation_Registry.csv",
  "Stage_4_5_AC_Atomicity_Analysis.csv",
  "Stage_4_5_STATE_Resolution.csv",
];

await mkdir(reportDir, { recursive: true });
const results = [];
function recordCount(csv) {
  let quoted = false;
  let records = 0;
  for (let index = 0; index < csv.length; index += 1) {
    if (csv[index] === '"') {
      if (quoted && csv[index + 1] === '"') index += 1;
      else quoted = !quoted;
    } else if (csv[index] === "\n" && !quoted) records += 1;
  }
  return csv.endsWith("\n") ? records : records + 1;
}
for (const filename of files) {
  const text = await readFile(path.join(candidate, filename), "utf8");
  const workbook = await Workbook.fromCSV(text);
  const sheet = "CSV import";
  const rows = recordCount(text);
  const columns = text.split(/\r?\n/, 1)[0].split(",").length;
  const inspected = await workbook.inspect({ kind: "table", range: `${sheet}!A1:${String.fromCharCode(64 + Math.min(columns, 26))}${Math.min(rows, 8)}` });
  if (inspected.truncated || !inspected.ndjson.includes("\"kind\":\"table\"")) throw new Error(`Inspection failed: ${filename}`);
  const image = await workbook.render({ sheetName: sheet, range: `A1:${String.fromCharCode(64 + Math.min(columns, 12))}${Math.min(rows, 12)}` });
  const buffer = Buffer.from(await image.arrayBuffer());
  const preview = path.join(reportDir, filename.replace(/\.csv$/i, ".png"));
  await writeFile(preview, buffer);
  results.push({ file: filename, rows: rows - 1, columns, preview: path.basename(preview), status: "PASS" });
}
await writeFile(path.join(root, "reports", "csv_artifact_verification.json"), JSON.stringify(results, null, 2));
console.log(JSON.stringify(results));
