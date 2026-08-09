import fs from "node:fs/promises";
import path from "node:path";
import { Workbook } from "@oai/artifact-tool";

// Handoff scaffold. It intentionally performs no write until the semantic
// relation maps are populated and validated; this prevents fabricated FR links.
const root = "C:\\Users\\novik\\Таск\\work\\stage_4_3_remediation";
const candidateDir = path.join(root, "candidate_4_3");

const catalogs = [
  {
    input: "Stage_4_Business_Rules_Catalog_4.1.2.csv",
    output: "Stage_4_Business_Rules_Catalog_4.3.csv",
    expectedRows: 113,
    headers: ["BR ID", "Module", "Rule", "Source", "Related FR", "Verification"],
  },
  {
    input: "Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv",
    output: "Stage_4_Acceptance_Criteria_Catalog_4.3.csv",
    expectedRows: 1824,
    headers: ["AC ID", "Module", "FR/BR", "Scenario", "Priority", "Test type", "Source", "Gherkin"],
  },
  {
    input: "Stage_4_NFR_Catalog_4.1.2.csv",
    output: "Stage_4_NFR_Catalog_4.3.csv",
    expectedRows: 25,
    headers: ["NFR ID", "Area", "Requirement", "Target", "Measurement", "Source/Assumption", "Modules"],
  },
  {
    input: "Stage_4_Requirements_Traceability_4.1.2.csv",
    output: "Stage_4_Requirements_Traceability_4.3.csv",
    expectedRows: 497,
    headers: ["Requirement", "Module", "Concept", "SCR", "FLOW", "STATE", "API", "DTO field", "Permission", "Error", "AC", "Source"],
  },
];

// Required semantic maps before enabling writes:
// - brToFr: BR ID -> verified FR IDs;
// - dataOwnerEvidence: AC ID -> { primaryOwner: DATA-*, operationId, frIds, evidence };
// - crossCuttingToAc: requirement ID -> existing/new verifiable AC IDs.
const brToFr = new Map();
const dataOwnerEvidence = new Map();
const crossCuttingToAc = new Map();

if (
  brToFr.size === 0 ||
  dataOwnerEvidence.size === 0 ||
  crossCuttingToAc.size === 0
) {
  const observed = [];
  for (const catalog of catalogs) {
    const stat = await fs.stat(path.join(candidateDir, catalog.input));
    observed.push({
      input: catalog.input,
      output: catalog.output,
      expectedRows: catalog.expectedRows,
      inputBytes: stat.size,
      status: "BLOCKED_PENDING_SEMANTIC_MAP",
    });
  }
  console.log(JSON.stringify(observed, null, 2));
  process.exit(2);
}

// Keep artifact-tool in the authoring path. Once the semantic maps are supplied,
// import each matrix into a Workbook, render/inspect it, then save the 4.3 CSV
// from that same validated matrix with a UTF-8 BOM.
void Workbook;

