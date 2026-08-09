import fs from "node:fs/promises";
import path from "node:path";
import { Workbook } from "@oai/artifact-tool";

const root = "C:\\Users\\novik\\Таск";
const workDir = path.join(root, "work", "stage_4_4_reaudit");
const outputDir = path.join(root, "outputs", "stage_4_4_reaudit");
const evidence = JSON.parse(await fs.readFile(path.join(workDir, "audit_evidence.json"), "utf8"));
await fs.mkdir(outputDir, { recursive: true });

function csvEscape(value) {
  const text = value == null ? "" : String(value);
  return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

async function writeCsv(name, headers, rows, widths) {
  const matrix = [headers, ...rows.map((row) => headers.map((header) => row[header] ?? ""))];
  const workbook = Workbook.create();
  const sheet = workbook.worksheets.add("Audit");
  sheet.getRangeByIndexes(0, 0, matrix.length, headers.length).values = matrix;
  sheet.getRangeByIndexes(0, 0, 1, headers.length).format = {
    fill: "#17365D", font: { bold: true, color: "#FFFFFF" }, wrapText: true, verticalAlignment: "center",
  };
  if (matrix.length > 1) {
    sheet.getRangeByIndexes(1, 0, matrix.length - 1, headers.length).format = {
      wrapText: true, verticalAlignment: "top",
      borders: { insideHorizontal: { style: "thin", color: "#D9E2F3" } },
    };
  }
  widths.forEach((width, index) => { sheet.getRangeByIndexes(0, index, matrix.length, 1).format.columnWidth = width; });
  sheet.getRange(`A1:${String.fromCharCode(64 + Math.min(headers.length, 26))}${matrix.length}`).format.font = { name: "Aptos", size: 10 };
  sheet.freezePanes.freezeRows(1);
  sheet.showGridLines = false;
  const inspect = await workbook.inspect({ kind: "table", range: `Audit!A1:${String.fromCharCode(64 + Math.min(headers.length, 26))}${Math.min(matrix.length, 20)}`, include: "values", tableMaxRows: 20, tableMaxCols: headers.length, maxChars: 5000 });
  if (!inspect.ndjson) throw new Error(`Inspection failed for ${name}`);
  const preview = await workbook.render({ sheetName: "Audit", range: `A1:${String.fromCharCode(64 + Math.min(headers.length, 26))}${Math.min(matrix.length, 25)}`, scale: 1, format: "png" });
  await fs.writeFile(path.join(workDir, "sheet_runtime", `${name}.png`), new Uint8Array(await preview.arrayBuffer()));
  await fs.writeFile(path.join(outputDir, name), "\uFEFF" + matrix.map((row) => row.map(csvEscape).join(",")).join("\r\n") + "\r\n", "utf8");
}

function metric(name, value) { return `| ${name} | ${value} |`; }
const metrics = evidence.metrics;
const partial = evidence.original_results.filter((row) => row["Independent result"] !== "Confirmed Fixed");
const severities = Object.fromEntries(["Critical", "High", "Medium", "Low", "Observation"].map((value) => [value, evidence.new_findings.filter((finding) => finding.Severity === value).length]));

const inputLines = ["# Stage 4.4 Input Validation", "", "Independent validation of the three supplied ZIP packages before audit.", "", "| Package | SHA-256 | CRC | Read/reopen | Manifest |", "|---|---|---|---|---|"];
for (const input of Object.values(evidence.input_checks)) {
  inputLines.push(`| ${path.basename(input.path)} | ${input.sha256_pass ? "PASS" : "FAIL"} | ${input.crc_pass ? "PASS" : "FAIL"} | ${input.reopen_pass ? "PASS" : "FAIL"} | ${input.manifest.present && input.manifest.missing_or_hash_mismatch.length === 0 ? "PASS" : "FAIL"} |`);
}
inputLines.push("", "All packages were fully read, reopened and extracted to independent working copies. No unsafe member paths, zero-byte members or temporary files were found.");
await fs.writeFile(path.join(outputDir, "Stage_4_4_Input_Validation.md"), inputLines.join("\n") + "\n", "utf8");

await writeCsv(
  "Stage_4_4_Findings.csv",
  ["Audit ID", "Severity", "Category", "Artifact", "Location", "Related IDs", "Source of truth", "Expected", "Actual", "Defect", "Consequence", "Recommended fix", "Verification", "Confidence", "Status"],
  evidence.new_findings,
  [18, 12, 18, 40, 28, 48, 38, 46, 46, 44, 40, 46, 42, 12, 12],
);
await writeCsv(
  "Stage_4_4_Finding_Verification.csv",
  ["Audit ID", "Original severity", "Original defect", "Claimed fix", "Verified files", "Verified IDs", "Normative source", "Verification method", "Independent result", "Residual risk", "Evidence"],
  evidence.original_results,
  [18, 14, 44, 44, 42, 34, 38, 42, 20, 42, 48],
);
await writeCsv(
  "Stage_4_4_FR_BR_AC_Audit.csv",
  ["Kind", "ID", "Module", "Relation", "Audit result", "Evidence"],
  evidence.fr_br_ac_audit,
  [10, 16, 14, 48, 18, 46],
);
await writeCsv(
  "Stage_4_4_API_Traceability_Audit.csv",
  ["Operation ID", "In candidate", "FR evidence", "AC evidence", "Audit result"],
  evidence.api_rows,
  [48, 14, 18, 32, 14],
);
await writeCsv(
  "Stage_4_4_Reference_Audit.csv",
  ["Check", "Expected", "Actual", "Result", "Evidence"],
  evidence.reference_audit,
  [34, 28, 38, 12, 52],
);

const summary = [
  "# Stage 4.4 Executive Summary", "",
  "**Verdict: FAIL.** Candidate 4.3 is not eligible for final-baseline designation or Stage 5 handoff.", "",
  "## Why", "",
  "Two Medium findings remain after independent verification:", "",
  "1. All 87 AC added to close orphaned cross-cutting requirements are broad generated templates. Each combines multiple FRs, conditions and expected results, rather than furnishing one bounded, requirement-level test.",
  "2. Thirty retained STATE identifiers do not resolve to published IDs in the Stage 3.5 baseline, and no source-controlled mapping is present in the candidate.", "",
  "## Recount", "", "| Metric | Independent result |", "|---|---:|",
  metric("Modules", metrics.modules), metric("FR / BR / AC / NFR", `${metrics.fr} / ${metrics.br} / ${metrics.ac} / ${metrics.nfr}`),
  metric("API operationId coverage", `${metrics.api_textual_coverage}/${metrics.api_operation_ids}`), metric("Finding-affected field coverage", `${metrics.field_level_coverage.covered}/${metrics.field_level_coverage.total}`),
  metric("FR without AC", metrics.fr_without_ac.length), metric("AC without valid primary owner", metrics.ac_without_valid_primary_owner.length), metric("Orphaned requirements", metrics.orphaned_requirements.length),
  metric("Unknown permissions / stable errors", `${metrics.unknown_permissions.length} / ${metrics.unknown_stable_errors.length}`), metric("Unknown UX IDs", `${Object.values(metrics.unknown_ux).reduce((sum, items) => sum + items.length, 0)}`),
  metric("Duplicate IDs", Object.values(metrics.duplicate_ids).reduce((sum, items) => sum + items.length, 0)), metric("Broken AC targets", metrics.invalid_ac_refs.length),
  "", "## Original findings", "", `Confirmed fixed: ${evidence.original_results.length - partial.length}/16. Partially fixed: ${partial.map((row) => row["Audit ID"]).join(", ")}.`,
  "", "## Decision", "", "Do not create a final baseline or design-input package. Prepare Stage 4.5 remediation for the two open Medium findings.", "",
].join("\n");
await fs.writeFile(path.join(outputDir, "Stage_4_4_Executive_Summary.md"), summary, "utf8");

const report = [
  "# Stage 4.4 Independent Re-audit Report", "", "**Verdict:** FAIL", "", "## Method", "", "The audit independently validated SHA-256, CRC, full archive reads, reopen and manifests for candidate 4.3, the re-audit input and audit 4.2. It used fresh working copies and freshly extracted Stage 2.3.1 and Stage 3.5 sources. Claims in Stage 4.3 were not treated as proof.",
  "", "## Findings", "",
  ...evidence.new_findings.map((finding) => `### ${finding["Audit ID"]} — ${finding.Severity}\n\n${finding.Defect}\n\n- Artifact: ${finding.Artifact}\n- Location: ${finding.Location}\n- Evidence: ${finding.Actual}\n- Consequence: ${finding.Consequence}\n- Required remediation: ${finding["Recommended fix"]}`),
  "", "## Independent results for audit 4.2", "", "| Audit ID | Original severity | Result | Evidence |", "|---|---|---|---|",
  ...evidence.original_results.map((row) => `| ${row["Audit ID"]} | ${row["Original severity"]} | ${row["Independent result"]} | ${row.Evidence} |`),
  "", "## Non-blocking confirmations", "", "- All 244 normative operationIds are present in the candidate traceability set.", "- All 21 finding-affected fields/parameters are represented in candidate requirements and acceptance material.", "- The contract-level inventories contain no unknown permissions or stable errors.", "- OQ-001 and OQ-003 are internally aligned at the contract/PRD level; MOD-014 is not the reason for this FAIL.", "", "## Finalization decision", "", "Because two Medium findings remain, Stage 4.3 cannot be promoted to a final PRD baseline. No Stage 5 work is authorized.", "",
].join("\n");
await fs.writeFile(path.join(outputDir, "Stage_4_4_Independent_Audit_Report.md"), report, "utf8");

const permissions = ["# Stage 4.4 Permissions and Security Audit", "", "- Normative permission catalog checked: 91 codes.", "- Unknown candidate permission references: 0.", "- Unknown stable errors: 0.", "- Server-side enforcement, partial access, redaction and blocked-user requirements are present for MOD-014.", "- No privilege-escalation finding was evidenced by this document audit.", "", "This result does not cure the open AC-atomicity and STATE-resolution findings; security/UX evidence must remain traceable in the remediation."].join("\n") + "\n";
await fs.writeFile(path.join(outputDir, "Stage_4_4_Permissions_Security_Audit.md"), permissions, "utf8");

const ux = ["# Stage 4.4 UX and Accessibility Audit", "", "Accessibility criteria for keyboard interaction, focus, announcements, High Contrast, non-color semantics, 200% scaling and narrow layouts are present in the candidate.", "", `However, ${metrics.unknown_ux.STATE.length} STATE references have no addressable published Stage 3.5 ID. This is AUDIT-4.4-002 (Medium): a designer or tester cannot reliably resolve these numbered state references to normative UX behavior.`, "", "OQ-003/MOD-014 controls are otherwise aligned: employee is a separate result type and group; DTO fields are bounded; filtering and redaction are server-side; client post-filtering is prohibited.", ""].join("\n");
await fs.writeFile(path.join(outputDir, "Stage_4_4_UX_Accessibility_Audit.md"), ux, "utf8");

const nfr = ["# Stage 4.4 NFR Audit", "", `NFR rows checked: ${metrics.nfr}. Each row contains source/assumption, scope, measurable target and measurement method.`, "", "No unsupported numeric product SLA was found. Deployment-policy values remain external operational inputs rather than invented product requirements.", "", "NFR result: structurally PASS; it does not override the overall FAIL caused by AC traceability and unresolved UX state identifiers.", ""].join("\n");
await fs.writeFile(path.join(outputDir, "Stage_4_4_NFR_Audit.md"), nfr, "utf8");

const oq1 = ["# Stage 4.4 OQ-001 Verification", "", "**Verdict: Fixed.**", "", "The candidate consistently identifies an organization-scoped urgency scale, settings permissions, semantic levels, intervals, reset, ETag/If-Match, idempotency, validation, outage/version-conflict behavior, non-color accessibility and effects on current/future notifications. No user override or arbitrary HEX color model was found.", "", "This OQ result does not authorize finalization while independent Medium findings remain elsewhere in the candidate.", ""].join("\n");
await fs.writeFile(path.join(outputDir, "Stage_4_4_OQ_001_Verification.md"), oq1, "utf8");

const oq3 = ["# Stage 4.4 OQ-003 and MOD-014 Verification", "", "**Verdict: Fixed.**", "", "The candidate provides `employee` as a separate result type and an Employees group; uses `EmployeeSearchResult`; bounds displayed fields; contains no invented avatar; retains server-side filtering/redaction/ranking/cursor behavior; prohibits client post-filtering; and defines deep-link, stale/unavailable and partial-failure behavior.", "", "FLOW-035 remains the historical project-completion flow, while candidate-level FLOW-038 addresses urgency-scale management. MOD-014 is not conflicted in the audited scope.", ""].join("\n");
await fs.writeFile(path.join(outputDir, "Stage_4_4_OQ_003_Verification.md"), oq3, "utf8");

const design = ["# Stage 4.4 Design Readiness", "", "**Not approved.**", "", "The candidate contains many design-ready details, including the OQ-001 and MOD-014 surfaces. It is not ready for Stage 5 because (1) 87 cross-cutting AC do not provide atomic testable behavior and (2) 30 STATE references cannot be resolved to published Stage 3.5 IDs. A designer would need to invent or infer behavior for unresolved state references.", ""].join("\n");
await fs.writeFile(path.join(outputDir, "Stage_4_4_Design_Readiness.md"), design, "utf8");

const development = ["# Stage 4.4 Development Readiness", "", "**Not approved for baseline handoff.**", "", "Backend, desktop, PostgreSQL and security inputs are materially stronger after 4.3, and the 244-operation contract is fully represented. The missing full recertification of all 1340 DTO constraints is not itself a PRD defect: the normative Stage 2.3.1 contract remains validated and the 21 finding-affected fields were checked.", "", "The release is nevertheless blocked until atomic cross-cutting acceptance criteria and a source-controlled STATE mapping are supplied.", ""].join("\n");
await fs.writeFile(path.join(outputDir, "Stage_4_4_Development_Readiness.md"), development, "utf8");

const validation = ["# Stage 4.4 Independent Validation", "", "**Result: FAIL.**", "", "| Gate | Result |", "|---|---|",
  metric("Input package integrity", "PASS"), metric("Modules / FR / BR / AC / NFR", `${metrics.modules} / ${metrics.fr} / ${metrics.br} / ${metrics.ac} / ${metrics.nfr}`), metric("API operation coverage", `${metrics.api_textual_coverage}/${metrics.api_operation_ids}`), metric("Affected field coverage", `${metrics.field_level_coverage.covered}/${metrics.field_level_coverage.total}`), metric("FR without AC", metrics.fr_without_ac.length), metric("AC without valid primary owner", metrics.ac_without_valid_primary_owner.length), metric("Orphaned trace rows", metrics.orphaned_requirements.length), metric("Unknown permission/error", `${metrics.unknown_permissions.length}/${metrics.unknown_stable_errors.length}`), metric("Unknown UX IDs", Object.values(metrics.unknown_ux).reduce((sum, items) => sum + items.length, 0)), metric("New Medium findings", severities.Medium),
  "", "Fail condition met: Medium findings are open. Candidate 4.3 remains a remediation candidate, not the final baseline.", ""].join("\n");
await fs.writeFile(path.join(outputDir, "Stage_4_4_Independent_Validation.md"), validation, "utf8");

console.log(JSON.stringify({ verdict: evidence.verdict, csvs: 5, markdown: 11, outputDir }, null, 2));
