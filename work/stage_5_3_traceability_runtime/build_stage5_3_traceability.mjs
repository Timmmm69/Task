import crypto from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";

const root = process.cwd();
const threadId = "019fa078-3f10-7ec1-99e2-7c1cba4ee3d4";
const workDir = path.join(root, "work", "stage_5_3_traceability");
const outputDir = path.join(root, "outputs", threadId, "stage_5_3_traceability");
const scrInventoryPath = path.join(root, "work", "stage_5_2", "Component_Inventory_0.1.csv");
const flowInventoryPath = path.join(root, "work", "stage_5_2", "Flow_Design_Inventory_0.1.csv");

const rel = (...parts) => parts.join("/");
const outputBase = rel("outputs", threadId);
const evidence = {
  base: [
    "work/stage_5_prototype/design-qa-stage5-p0.md",
    "work/stage_5_prototype/design-qa-stage5-edge-states.md",
    "work/stage_5_prototype/design-qa-stage5-surfaces.md",
    "work/stage_5_prototype/design-qa-stage5-component-gaps.md",
  ],
  waveA: [
    "work/stage_5_3_wave_a/VALIDATION_REPORT.md",
    "work/stage_5_3_wave_a/MANIFEST.sha256",
  ],
  waveB: [
    rel(outputBase, "stage_5_3_wave_b_implementation", "VALIDATION_REPORT.md"),
    rel(outputBase, "stage_5_3_wave_b_implementation", "manifest.json"),
  ],
  search: [
    rel(outputBase, "stage_5_3_wave_c_search_increment", "VALIDATION_REPORT.md"),
    rel(outputBase, "stage_5_3_wave_c_search_increment", "manifest.json"),
  ],
  lifecycle: [
    rel(outputBase, "stage_5_3_wave_c_lifecycle_increment", "VALIDATION_REPORT.md"),
    rel(outputBase, "stage_5_3_wave_c_lifecycle_increment", "manifest.json"),
  ],
  settings: [
    rel(outputBase, "stage_5_3_wave_c_settings_increment", "VALIDATION_REPORT.md"),
    rel(outputBase, "stage_5_3_wave_c_settings_increment", "manifest.json"),
  ],
  admin: [
    rel(outputBase, "stage_5_3_wave_c_admin_increment", "VALIDATION_REPORT.md"),
    rel(outputBase, "stage_5_3_wave_c_admin_increment", "manifest.json"),
  ],
  operations: [
    rel(outputBase, "stage_5_3_wave_c_operations_increment", "VALIDATION_REPORT.md"),
    rel(outputBase, "stage_5_3_wave_c_operations_increment", "manifest.json"),
    rel(outputBase, "stage_5_3_wave_c_operations_increment", "qa", "design-qa-wave-c-operations.md"),
  ],
  calendarEditor: [
    rel(outputBase, "stage_5_3_calendar_event_editor_increment", "VALIDATION_REPORT.md"),
    rel(outputBase, "stage_5_3_calendar_event_editor_increment", "manifest.json"),
    rel(outputBase, "stage_5_3_calendar_event_editor_increment", "qa", "qa-wave-c-calendar-event-editor.png"),
  ],
};

function parseCsv(text) {
  /** @type {string[][]} */
  const rows = [];
  /** @type {string[]} */
  let row = [];
  let field = "";
  let quoted = false;
  const source = text.replace(/^\uFEFF/, "");
  for (let index = 0; index < source.length; index += 1) {
    const character = source[index];
    if (quoted) {
      if (character === '"' && source[index + 1] === '"') {
        field += '"';
        index += 1;
      } else if (character === '"') {
        quoted = false;
      } else {
        field += character;
      }
    } else if (character === '"') {
      quoted = true;
    } else if (character === ",") {
      row.push(field);
      field = "";
    } else if (character === "\n") {
      row.push(field.replace(/\r$/, ""));
      rows.push(row);
      row = [];
      field = "";
    } else {
      field += character;
    }
  }
  if (field.length > 0 || row.length > 0) {
    row.push(field.replace(/\r$/, ""));
    rows.push(row);
  }
  const headers = rows.shift() ?? [];
  return rows
    .filter((item) => item.some((value) => value !== ""))
    .map((values) => Object.fromEntries(headers.map((header, index) => [header, values[index] ?? ""])));
}

function escapeCsv(value) {
  return `"${String(value ?? "").replace(/"/g, '""')}"`;
}

function toCsv(headers, rows) {
  return `${headers.map(escapeCsv).join(",")}\n${rows
    .map((row) => headers.map((header) => escapeCsv(row[header])).join(","))
    .join("\n")}\n`;
}

function sha256(buffer) {
  return crypto.createHash("sha256").update(buffer).digest("hex").toUpperCase();
}

async function hashFile(relativePath) {
  return sha256(await fs.readFile(path.join(root, ...relativePath.split("/"))));
}

function joinEvidence(items) {
  return items.join(" | ");
}

function mapScr(row) {
  const id = row["SCR ID"];
  const module = row.Module;
  if (id === "SCR-044") {
    return {
      status: "VERIFIED_PACKAGE",
      source: joinEvidence([...evidence.waveA, ...evidence.calendarEditor]),
      scope: "Calendar views plus the canonical CalendarEvent create/edit/attendee/respond contract and recovery states are versioned and verified.",
      browser: "Verified in approved in-app Browser",
      remaining: "Formal approval and native Windows/runtime evidence remain Gate work.",
    };
  }
  if (/^SCR-18[3-8]$/.test(id)) {
    return {
      status: "VERIFIED_PACKAGE",
      source: joinEvidence(evidence.operations),
      scope: "Operations implementation, build, 15/15 tests and approved-browser Design QA pass across Health, Jobs, Backups, Audit, Organization, limited-role and offline states.",
      browser: "Verified in approved in-app Browser",
      remaining: "Formal approval and native Windows/runtime evidence remain Gate work.",
    };
  }
  if (module === "Calendar" || id === "SCR-033") {
    return {
      status: "VERIFIED_PACKAGE",
      source: joinEvidence(evidence.waveA),
      scope: "Wave A Direction 2 prototype and browser evidence.",
      browser: "Pass for captured package state",
      remaining: "Formal annotated-frame and stakeholder approval remain Gate work.",
    };
  }
  if (["Projects", "Files", "CRM"].includes(module)) {
    return {
      status: "VERIFIED_PACKAGE",
      source: joinEvidence(evidence.waveB),
      scope: "Wave B implementation package with build, tests and browser Design QA.",
      browser: "Pass for captured package state",
      remaining: "Native Windows/SMB/runtime and formal approval remain outside prototype evidence.",
    };
  }
  if (module === "Search") {
    return {
      status: "VERIFIED_PACKAGE",
      source: joinEvidence(evidence.search),
      scope: "Search increment with permission-safe partial/offline behavior, build, tests and browser QA.",
      browser: "Pass for captured package state",
      remaining: "Formal approval remains Gate work.",
    };
  }
  if (module === "Lifecycle") {
    return {
      status: "VERIFIED_PACKAGE",
      source: joinEvidence(evidence.lifecycle),
      scope: "Archive/Trash increment with restore, purge, legal-hold, conflict and offline evidence.",
      browser: "Pass for captured package state",
      remaining: "Formal approval remains Gate work.",
    };
  }
  if (module === "Settings") {
    return {
      status: "VERIFIED_PACKAGE",
      source: joinEvidence(evidence.settings),
      scope: "Settings increment with scope, conflict, security/device/cache/connection and offline evidence.",
      browser: "Pass for captured package state",
      remaining: "Formal approval remains Gate work.",
    };
  }
  if (module === "Admin") {
    return {
      status: "VERIFIED_PACKAGE",
      source: joinEvidence(evidence.admin),
      scope: "Admin users/departments/roles/sessions/resources increment with capability, guard, conflict and offline evidence.",
      browser: "Pass for captured package state",
      remaining: "Formal approval and real authorization/runtime remain Gate work.",
    };
  }
  return {
    status: "PROTOTYPE_EVIDENCE_MAPPED",
    source: joinEvidence(evidence.base),
    scope: "Direction 2 base prototype, P0/resilience/surface/component-gap browser and semantic evidence.",
    browser: "Representative prototype evidence",
    remaining: "Per-SCR annotated-frame acceptance, Windows runtime and formal stakeholder approval remain open.",
  };
}

const waveBFlowIds = new Set([
  "FLOW-013", "FLOW-014", "FLOW-015", "FLOW-016", "FLOW-017",
  "FLOW-018", "FLOW-035", "FLOW-036", "FLOW-037",
]);

function mapFlow(row) {
  const id = row["FLOW ID"];
  if (id === "FLOW-031") {
    return {
      status: "VERIFIED_PACKAGE",
      source: joinEvidence([...evidence.waveA, ...evidence.calendarEditor]),
      scope: "Slot-to-editor creation, canonical fields, attendees, RSVP, validation, overlap, idempotency and offline guards are verified.",
      remaining: "Formal approval and native Windows/runtime evidence remain Gate work.",
    };
  }
  if (id === "FLOW-032") {
    return {
      status: "VERIFIED_PACKAGE",
      source: joinEvidence(evidence.waveA),
      scope: "Calendar drag/resize, overlap, rollback and read-only behavior verified.",
      remaining: "Formal approval remains Gate work.",
    };
  }
  if (waveBFlowIds.has(id)) {
    const sources = [...evidence.waveB];
    if (id === "FLOW-035") sources.push(...evidence.lifecycle, ...evidence.settings);
    return {
      status: "VERIFIED_PACKAGE",
      source: joinEvidence(sources),
      scope: "Wave B implementation evidence; cross-module lifecycle/settings packages included where the flow spans them.",
      remaining: "Native runtime and formal approval remain Gate work.",
    };
  }
  if (id === "FLOW-019") {
    return {
      status: "VERIFIED_PACKAGE",
      source: joinEvidence(evidence.search),
      scope: "Global Search keyboard, filter, partial/redacted, cursor and offline evidence.",
      remaining: "Formal approval remains Gate work.",
    };
  }
  if (["FLOW-026", "FLOW-027"].includes(id)) {
    return {
      status: "VERIFIED_PACKAGE",
      source: joinEvidence([...evidence.waveB, ...evidence.lifecycle]),
      scope: "Cross-object archive/trash lifecycle verified across Wave B and Archive/Trash increments.",
      remaining: "Formal approval remains Gate work.",
    };
  }
  if (id === "FLOW-028") {
    return {
      status: "VERIFIED_PACKAGE",
      source: joinEvidence(evidence.lifecycle),
      scope: "Trash restore, conflict, parent-unavailable and retention boundaries verified.",
      remaining: "Formal approval remains Gate work.",
    };
  }
  if (["FLOW-029", "FLOW-030"].includes(id)) {
    return {
      status: "VERIFIED_PACKAGE",
      source: joinEvidence(evidence.admin),
      scope: "User lifecycle and permission-change flows verified in the Admin increment.",
      remaining: "Real authorization/runtime and formal approval remain Gate work.",
    };
  }
  return {
    status: "PROTOTYPE_EVIDENCE_MAPPED",
    source: joinEvidence(evidence.base),
    scope: "Direction 2 base prototype provides representative interactive, keyboard, semantic and resilience evidence.",
    remaining: "Flow-specific annotated acceptance, Windows runtime and formal approval remain open.",
  };
}

const scrInventory = parseCsv(await fs.readFile(scrInventoryPath, "utf8"));
const flowInventory = parseCsv(await fs.readFile(flowInventoryPath, "utf8"));

const scrRows = scrInventory.map((row) => {
  const mapped = mapScr(row);
  return {
    "SCR ID": row["SCR ID"],
    Module: row.Module,
    "Surface name": row["Surface name"],
    Priority: row.Priority,
    "Inventory status": row["Inventory status"],
    "Evidence status": mapped.status,
    "Evidence source": mapped.source,
    "Evidence scope": mapped.scope,
    "Browser status": mapped.browser,
    "Remaining acceptance": mapped.remaining,
    "Gate 5.3": "OPEN",
  };
});

const flowRows = flowInventory.map((row) => {
  const mapped = mapFlow(row);
  return {
    "FLOW ID": row["FLOW ID"],
    Flow: row.Flow,
    "Scenario group": row["Scenario group"],
    Modules: row.Modules,
    "SCR references": row["SCR references"],
    Priority: row.Priority,
    "Required design evidence": row["Required design evidence"],
    "Evidence status": mapped.status,
    "Evidence source": mapped.source,
    "Evidence scope": mapped.scope,
    "Remaining acceptance": mapped.remaining,
    "Gate 5.3": "OPEN",
  };
});

const countBy = (rows, key) => Object.fromEntries(
  [...new Set(rows.map((row) => row[key]))]
    .sort()
    .map((value) => [value, rows.filter((row) => row[key] === value).length]),
);

const scrStatusCounts = countBy(scrRows, "Evidence status");
const flowStatusCounts = countBy(flowRows, "Evidence status");
const validationChecks = {
  scrCount: scrRows.length === 128,
  scrUnique: new Set(scrRows.map((row) => row["SCR ID"])).size === 128,
  scrIdsValid: scrRows.every((row) => /^SCR-\d{3}$/.test(row["SCR ID"])),
  scrAllMapped: scrRows.every((row) => row["Evidence source"] && row["Evidence status"]),
  scrStatusCounts: JSON.stringify(scrStatusCounts) === JSON.stringify({
    PROTOTYPE_EVIDENCE_MAPPED: 46,
    VERIFIED_PACKAGE: 82,
  }),
  flowCount: flowRows.length === 37,
  flowUnique: new Set(flowRows.map((row) => row["FLOW ID"])).size === 37,
  flowIdsValid: flowRows.every((row) => /^FLOW-\d{3}$/.test(row["FLOW ID"])),
  flowAllMapped: flowRows.every((row) => row["Evidence source"] && row["Evidence status"]),
  flowStatusCounts: JSON.stringify(flowStatusCounts) === JSON.stringify({
    PROTOTYPE_EVIDENCE_MAPPED: 20,
    VERIFIED_PACKAGE: 17,
  }),
};

if (Object.values(validationChecks).some((value) => value !== true)) {
  throw new Error(`Traceability validation failed: ${JSON.stringify(validationChecks)}`);
}

const scrHeaders = Object.keys(scrRows[0]);
const flowHeaders = Object.keys(flowRows[0]);
const scrCsv = toCsv(scrHeaders, scrRows);
const flowCsv = toCsv(flowHeaders, flowRows);
const sourceHashes = {
  "work/stage_5_2/Component_Inventory_0.1.csv": await hashFile("work/stage_5_2/Component_Inventory_0.1.csv"),
  "work/stage_5_2/Flow_Design_Inventory_0.1.csv": await hashFile("work/stage_5_2/Flow_Design_Inventory_0.1.csv"),
};

const report = `# Task — Stage 5.3 Consolidated Traceability Report 0.1.2

**Date:** 2026-08-01  
**Direction:** 2 — Timeline planner  
**Result:** 128/128 SCR and 37/37 FLOW records are mapped to concrete evidence sources. Gate 5.3 remains open.

## Executive result

- SCR mapping completeness: **128/128**.
- FLOW mapping completeness: **37/37**.
- SCR evidence statuses: **82 VERIFIED_PACKAGE**, **46 PROTOTYPE_EVIDENCE_MAPPED**.
- FLOW evidence statuses: **17 VERIFIED_PACKAGE**, **20 PROTOTYPE_EVIDENCE_MAPPED**.
- No SCR or FLOW identifier is duplicated or omitted.

Mapping completeness is not the same as Gate approval. The matrices keep evidence strength and remaining acceptance work separate.

## Explicit gaps

1. **46 base-prototype SCR and 20 base-prototype FLOW rows:** representative Direction 2 interactive, keyboard, semantic and resilience evidence is mapped, but per-record annotated approval and Windows runtime evidence remain open.
2. Formal stakeholder approval, native Windows/UIA/Narrator, actual 200% scaling and real infrastructure behavior are not inferred from prototype evidence.

## Evidence interpretation

- \`VERIFIED_PACKAGE\`: a versioned Wave A/B/C validation package provides captured-state build/test/browser evidence.
- \`PROTOTYPE_EVIDENCE_MAPPED\`: representative base-prototype QA evidence exists, but record-specific formal acceptance is still required.

## Gate decision

The traceability inventory is complete and auditable, but Gate 5.3 remains open pending the explicit gaps above and formal evidence approval. This package does not close Stage 5.3 or Stage 5.
`;

const validation = `# Task — Stage 5.3 Consolidated Traceability Validation 0.1.2

**Result:** PASS for inventory reconciliation and evidence mapping; Gate 5.3 remains open.

## Automated checks

| Check | Result |
|---|---|
| SCR source rows | PASS — 128 |
| Unique SCR IDs | PASS — 128/128 |
| SCR ID format | PASS |
| SCR evidence source/status present | PASS — 128/128 |
| SCR status distribution | PASS — 82 verified package, 46 prototype mapped |
| FLOW source rows | PASS — 37 |
| Unique FLOW IDs | PASS — 37/37 |
| FLOW ID format | PASS |
| FLOW evidence source/status present | PASS — 37/37 |
| FLOW status distribution | PASS — 17 verified package, 20 prototype mapped |

## Source integrity

- \`Component_Inventory_0.1.csv\`: \`${sourceHashes["work/stage_5_2/Component_Inventory_0.1.csv"]}\`
- \`Flow_Design_Inventory_0.1.csv\`: \`${sourceHashes["work/stage_5_2/Flow_Design_Inventory_0.1.csv"]}\`

## Boundary

This validation proves exact inventory coverage and evidence routing. It does not convert prototype evidence into stakeholder approval, native Windows/runtime verification or closed Gate status.
`;

const version = `Task · Stage 5.3 consolidated traceability package
Version: 0.1.2
Date: 2026-08-01
Direction: 2 — Timeline planner
Gate status: open
Result: 128/128 SCR and 37/37 FLOW evidence sources mapped; explicit gaps retained
`;

const coreFiles = {
  "VERSION.txt": version,
  "Stage_5_3_Traceability_Report_0.1.2.md": report,
  "Stage_5_3_Traceability_Validation_0.1.2.md": validation,
  "SCR_Evidence_Matrix_0.1.csv": scrCsv,
  "FLOW_Evidence_Matrix_0.1.csv": flowCsv,
};

const artifactHashes = Object.fromEntries(
  Object.entries(coreFiles).map(([file, contents]) => [file, sha256(contents)]),
);

const acceptedEvidencePaths = [...new Set([
  "work/stage_5_2/Component_Inventory_0.1.csv",
  "work/stage_5_2/Flow_Design_Inventory_0.1.csv",
  ...Object.values(evidence).flat(),
])].sort();

/** @type {{path: string, sha256: string}[]} */
const acceptedEvidence = [];
for (const relativePath of acceptedEvidencePaths) {
  acceptedEvidence.push({ path: relativePath, sha256: await hashFile(relativePath) });
}

const manifest = {
  product: "Task",
  stage: "5.3",
  package: "consolidated_traceability",
  version: "0.1.2",
  date: "2026-08-01",
  direction: "Direction 2 — Timeline planner",
  gateStatus: "open",
  result: "128/128 SCR and 37/37 FLOW evidence sources mapped; Operations browser acceptance verified",
  summary: {
    scrTotal: 128,
    scrMapped: 128,
    scrStatusCounts,
    flowTotal: 37,
    flowMapped: 37,
    flowStatusCounts,
  },
  validationChecks,
  sourceHashes,
  artifactHashes,
  acceptedEvidence,
  boundaries: [
    "No Gate 5.3 or Stage 5 closure claim",
    "No formal stakeholder approval claim",
    "No native Windows, UIA, Narrator, actual 200% scaling or real infrastructure claim",
  ],
};

const manifestText = `${JSON.stringify(manifest, null, 2)}\n`;
const manifestHash = sha256(manifestText);
const packageFiles = {
  ...coreFiles,
  "manifest.json": manifestText,
  "MANIFEST.sha256": `${manifestHash}  manifest.json\n`,
};

async function writePackage(directory) {
  await fs.mkdir(directory, { recursive: true });
  for (const [file, contents] of Object.entries(packageFiles)) {
    await fs.writeFile(path.join(directory, file), contents, "utf8");
  }
}

await writePackage(workDir);
await writePackage(outputDir);

async function verifyPackage(directory) {
  const parsedManifest = JSON.parse(await fs.readFile(path.join(directory, "manifest.json"), "utf8"));
  /** @type {{file: string, expected: string, actual: string, match: boolean}[]} */
  const checks = [];
  for (const [file, expected] of Object.entries(parsedManifest.artifactHashes)) {
    const actual = sha256(await fs.readFile(path.join(directory, file)));
    const expectedHash = String(expected);
    checks.push({ file, expected: expectedHash, actual, match: expectedHash === actual });
  }
  const actualManifest = sha256(await fs.readFile(path.join(directory, "manifest.json")));
  const recordedManifest = (await fs.readFile(path.join(directory, "MANIFEST.sha256"), "utf8")).trim().split(/\s+/)[0];
  return {
    directory,
    artifactCount: checks.length,
    artifactsPass: checks.every((check) => check.match),
    manifestHash: actualManifest,
    manifestHashPass: actualManifest === recordedManifest,
  };
}

console.log(JSON.stringify({
  scrStatusCounts,
  flowStatusCounts,
  sourceHashes,
  work: await verifyPackage(workDir),
  output: await verifyPackage(outputDir),
}, null, 2));
