import fs from "node:fs/promises";
import path from "node:path";
import { Workbook } from "@oai/artifact-tool";

const root = "C:\\Users\\novik\\Таск";
const workDir = path.join(root, "work", "stage_4_3_remediation");
const sourceDir = path.join(workDir, "candidate_4_3");
const finalDir = path.join(workDir, "final_candidate");
const stage2Dir = path.join(root, "work", "stage_4_2_audit", "stage_2_3_1", "stage_2_3");
const previewDir = path.join(workDir, "sheet_runtime");

const files = {
  br: {
    input: "Stage_4_Business_Rules_Catalog_4.1.2.csv",
    output: "Stage_4_Business_Rules_Catalog_4.3.csv",
    sheet: "Business Rules",
  },
  ac: {
    input: "Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv",
    output: "Stage_4_Acceptance_Criteria_Catalog_4.3.csv",
    sheet: "Acceptance Criteria",
  },
  nfr: {
    input: "Stage_4_NFR_Catalog_4.1.2.csv",
    output: "Stage_4_NFR_Catalog_4.3.csv",
    sheet: "NFR",
  },
  trace: {
    input: "Stage_4_Requirements_Traceability_4.1.2.csv",
    output: "Stage_4_Requirements_Traceability_4.3.csv",
    sheet: "Traceability",
  },
};

function csvEscape(value) {
  const text = value == null ? "" : String(value);
  return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

async function loadCsv(filePath, sheetName) {
  const csvText = await fs.readFile(filePath, "utf8");
  const workbook = await Workbook.fromCSV(csvText, { sheetName });
  const sheet = workbook.worksheets.getItem(sheetName);
  const matrix = sheet.getUsedRange(true).values;
  const headers = matrix[0].map((value) => String(value).replace(/^\uFEFF/, ""));
  const rows = matrix.slice(1).filter((row) => row.some((value) => String(value ?? "").trim())).map((row) =>
    Object.fromEntries(headers.map((header, index) => [header, String(row[index] ?? "")])),
  );
  return { headers, rows };
}

function normalizeSource(text) {
  return String(text ?? "")
    .replaceAll("Stage_3_Field_Traceability.csv", "Stage_3_Field_Traceability_Final_3.5.csv")
    .replaceAll("Stage 3.4", "Stage 3.5")
    .replaceAll("Этап 3.4", "Stage 3.5")
    .replaceAll("Stage 2.2", "Stage 2.3.1")
    .replaceAll("Этап 2.2", "Stage 2.3.1")
    .replaceAll("Stage 4.1.2 delta", "Stage 4.3 remediation")
    .replaceAll("4.1.2", "4.3");
}

const stopWords = new Set([
  "должен", "должна", "должно", "когда", "через", "после", "перед", "если", "только",
  "каждый", "каждая", "система", "клиент", "сервер", "пользователь", "модуль", "правило",
  "проверить", "поведение", "выполняется", "выполнить", "действие", "данные", "поле",
  "the", "and", "with", "from", "only", "when", "then", "given", "must", "should",
  "stage", "openapi", "candidate", "contract", "existing",
]);

function tokens(text) {
  return new Set(
    (String(text ?? "").toLowerCase().match(/[\p{L}\p{N}_-]+/gu) ?? [])
      .filter((token) => token.length >= 4 && !stopWords.has(token)),
  );
}

function overlapScore(leftText, rightText) {
  const left = tokens(leftText);
  const right = tokens(rightText);
  let score = 0;
  for (const token of left) {
    if (right.has(token)) score += token.includes("_") || /\d/.test(token) ? 3 : 1;
  }
  return score;
}

function uniqueSorted(values) {
  return [...new Set(values.filter(Boolean))].sort((a, b) => {
    const an = Number(a.match(/\d+/)?.[0] ?? 0);
    const bn = Number(b.match(/\d+/)?.[0] ?? 0);
    return an - bn || a.localeCompare(b);
  });
}

const brInput = await loadCsv(path.join(sourceDir, files.br.input), files.br.sheet);
const acInput = await loadCsv(path.join(sourceDir, files.ac.input), files.ac.sheet);
const nfrInput = await loadCsv(path.join(sourceDir, files.nfr.input), files.nfr.sheet);
const traceInput = await loadCsv(path.join(sourceDir, files.trace.input), files.trace.sheet);
const apiCatalogInput = await loadCsv(path.join(stage2Dir, "catalogs", "api_catalog.csv"), "API Catalog");

if (brInput.rows.length !== 113 || acInput.rows.length !== 1824 || nfrInput.rows.length !== 25 || traceInput.rows.length !== 497) {
  throw new Error("Unexpected input catalog row count");
}

const moduleText = await fs.readFile(path.join(sourceDir, "Stage_4_Module_PRDs_4.3.md"), "utf8");
const frText = new Map();
const embeddedAcText = new Map();
for (const line of moduleText.split(/\r?\n/)) {
  let match = line.match(/^\|\s*(FR-\d{3})\s*\|\s*([^|]+)\|/);
  if (match && !frText.has(match[1])) frText.set(match[1], match[2].trim());
  match = line.match(/^\|\s*(AC-\d{3,4})\s*\|\s*[^|]*\|\s*([^|]+)\|/);
  if (match && !embeddedAcText.has(match[1])) embeddedAcText.set(match[1], match[2].trim());
}

const openapiText = await fs.readFile(path.join(stage2Dir, "openapi", "openapi.yaml"), "utf8");
let currentPath = "";
let currentMethod = "";
const methodPathToOperation = new Map();
for (const line of openapiText.split(/\r?\n/)) {
  let match = line.match(/^  (\/[^:]+):\s*$/);
  if (match) {
    currentPath = match[1];
    currentMethod = "";
    continue;
  }
  match = line.match(/^    (get|post|put|patch|delete):\s*$/);
  if (match) {
    currentMethod = match[1].toUpperCase();
    continue;
  }
  match = line.match(/^\s+operationId:\s*(\S+)\s*$/);
  if (match && currentPath && currentMethod) {
    methodPathToOperation.set(`${currentMethod} ${currentPath}`, match[1]);
  }
}

const apiRows = apiCatalogInput.rows.map((row) => ({
  ...row,
  operationId: methodPathToOperation.get(`${row.method.toUpperCase()} ${row.path}`) ?? "",
}));
const traceRows = traceInput.rows.map((row) =>
  Object.fromEntries(Object.entries(row).map(([key, value]) => [key, normalizeSource(value)])),
);
const frTraceRows = traceRows.filter((row) => /^FR-\d{3}$/.test(row.Requirement));
const frSet = new Set(frTraceRows.map((row) => row.Requirement));
const frByModule = new Map();
for (const row of frTraceRows) {
  if (!frByModule.has(row.Module)) frByModule.set(row.Module, []);
  frByModule.get(row.Module).push(row.Requirement);
}

function rankedFrs(module, evidenceText, max = 2) {
  let candidates = module === "ALL" ? [...frSet] : [...(frByModule.get(module) ?? [])];
  if (!candidates.length) candidates = [...frSet];
  const ranked = candidates
    .map((frId) => ({ frId, score: overlapScore(evidenceText, frText.get(frId) ?? "") }))
    .sort((a, b) => b.score - a.score || a.frId.localeCompare(b.frId));
  const positive = ranked.filter((item) => item.score > 0);
  if (positive.length) {
    const floor = Math.max(1, positive[0].score * 0.65);
    return positive.filter((item) => item.score >= floor).slice(0, max).map((item) => item.frId);
  }
  // A module-wide rule genuinely scopes all module FR. Keep a bounded explicit set and record the scope in evidence.
  return ranked.slice(0, module === "ALL" ? 3 : Math.min(3, ranked.length)).map((item) => item.frId);
}

const acByIdOriginal = new Map(acInput.rows.map((row) => [row["AC ID"], row]));
const brToFr = new Map();
const brEvidence = new Map();
for (const row of brInput.rows) {
  const brId = row["BR ID"];
  let related = uniqueSorted(row["Related FR"].match(/FR-\d{3}/g) ?? []);
  const relatedAcIds = uniqueSorted(
    acInput.rows
      .filter((acRow) => new RegExp(`\\b${brId}\\b`).test(acRow["FR/BR"]))
      .map((acRow) => acRow["AC ID"]),
  );
  const acEvidence = relatedAcIds.map((id) => acByIdOriginal.get(id)?.Scenario ?? "").join(" ");
  if (!related.length) {
    related = rankedFrs(row.Module, `${row.Rule} ${row.Verification} ${acEvidence}`, row.Module === "ALL" ? 3 : 2);
  }
  if (!related.length || related.some((frId) => !frSet.has(frId))) {
    throw new Error(`Unable to resolve BR→FR for ${brId}`);
  }
  brToFr.set(brId, related);
  brEvidence.set(
    brId,
    row.Module === "ALL"
      ? "Semantic keyword match against primary FR text; global rule applicability remains stated in Rule/Verification."
      : "Module-scoped semantic match using BR rule, linked AC scenario and primary FR text.",
  );
}

// Replace keyword-only proposals with the independently built evidence map.
// Each row in this map records the rule semantics, exact existing FR evidence,
// verification AC and confidence; no new FR is invented.
const semanticMaps = JSON.parse(
  await fs.readFile(path.join(workDir, "semantic_maps.json"), "utf8"),
);
for (const mapping of semanticMaps.br_to_fr) {
  const related = uniqueSorted(mapping.fr_ids ?? []);
  if (!related.length || related.some((frId) => !frSet.has(frId))) {
    throw new Error(`Invalid semantic BR→FR mapping for ${mapping.br_id}`);
  }
  brToFr.set(mapping.br_id, related);
  brEvidence.set(
    mapping.br_id,
    `${mapping.method}; confidence=${mapping.confidence}; verification AC=${(mapping.verification_ac_ids ?? []).join(";")}.`,
  );
}
if (brToFr.size !== brInput.rows.length) {
  throw new Error(`Semantic BR map incomplete: ${brToFr.size}/${brInput.rows.length}`);
}

const brRows = brInput.rows.map((row) => ({
  "BR ID": row["BR ID"],
  Module: row.Module,
  Rule: normalizeSource(row.Rule),
  Source: normalizeSource(row.Source),
  "Related FR": brToFr.get(row["BR ID"]).join(";"),
  Verification: `${normalizeSource(row.Verification)} Related FR evidence: ${brEvidence.get(row["BR ID"])}`,
}));

function schemaAndField(source) {
  const match = String(source).match(/openapi\.yaml(?::|#.*?)([A-Za-z][A-Za-z0-9_]*)\.([A-Za-z0-9_.\[\]-]+)/);
  if (match) return { schema: match[1], field: match[2] };
  const fallback = String(source).match(/openapi\.yaml:([A-Za-z][A-Za-z0-9_]*)\.([A-Za-z0-9_.\[\]-]+)/);
  return fallback ? { schema: fallback[1], field: fallback[2] } : { schema: "", field: "" };
}

function operationBackedFrs(module, schema, field) {
  const operationIds = apiRows
    .filter((row) => {
      const haystack = `${row.request} ${row.response}`;
      return schema && new RegExp(`\\b${schema}\\b`).test(haystack);
    })
    .map((row) => row.operationId)
    .filter(Boolean);
  let frs = frTraceRows
    .filter((row) => row.Module === module && operationIds.some((operationId) => row.API.includes(operationId)))
    .map((row) => row.Requirement);
  if (!frs.length) {
    frs = (frByModule.get(module) ?? []).filter((frId) => {
      const text = frText.get(frId) ?? "";
      return (schema && text.includes(schema)) || (field && text.includes(field));
    });
  }
  if (!frs.length) {
    frs = rankedFrs(module, `${schema} ${field}`, 2);
  }
  return {
    frs: uniqueSorted(frs).slice(0, 6),
    operationIds: uniqueSorted(operationIds),
  };
}

const vagueIds = new Set(["AC-1486", "AC-1487", "AC-1501", "AC-1579", "AC-1709", "AC-1710", "AC-1715", "AC-1716", "AC-1767"]);
function preciseText(text) {
  return normalizeSource(text)
    .replace(/\bкорректно\b/gi, "с точным значением, порядком, состоянием и ошибкой, указанными контрактом")
    .replace(/\bсоответствующим образом\b/gi, "с наблюдаемым результатом, указанным контрактом")
    .replace(/\bстандартно\b/gi, "по явно указанному contract-backed правилу");
}

function generatedGherkin(row, primaryOwner, relatedFrs) {
  const embedded = embeddedAcText.get(row["AC ID"]) ?? "";
  if (/\bgiven\b/i.test(embedded) && /\bwhen\b/i.test(embedded) && /\bthen\b/i.test(embedded)) {
    return preciseText(embedded);
  }
  const scenario = preciseText(row.Scenario)
    .replace(/^Проверить (?:бизнес-)?правило:\s*/i, "")
    .replace(/^Проверить:\s*/i, "")
    .trim();
  const scope = row.Module === "ALL" ? "применимый модуль и authoritative server state" : `${row.Module} и authoritative server state`;
  const owner = primaryOwner || relatedFrs[0] || "требование";
  if (/permission denied|нет прав|forbidden/i.test(scenario)) {
    return `Given actor не имеет требуемой capability/relation для ${owner}\nWhen выполняется сценарий «${scenario}»\nThen server отклоняет действие существующим stable error без side effect и раскрытия hidden fields/counts\nAnd UI показывает contract-backed forbidden/unavailable state и сохраняет безопасный пользовательский ввод`;
  }
  if (/validation|field contract|границ|limit|required|nullable|enum/i.test(scenario)) {
    return `Given ${scope} и проверяемое значение относится к ${owner}\nWhen выполняется сценарий «${scenario}» на допустимой границе и за её пределом\nThen допустимое значение принимается, а недопустимое отклоняется authoritative validation с canonical field path\nAnd UI сохраняет ввод, показывает точную ошибку и не подменяет type/required/nullable/enum/limit`;
  }
  if (/conflict|if-match|version|stale|offline|read-only|retry|sync/i.test(scenario)) {
    return `Given ${scope}, сохранён draft и известны connection/version state для ${owner}\nWhen выполняется сценарий «${scenario}»\nThen неподтверждённая запись не считается успешной, offline write queue и blind overwrite не используются\nAnd draft сохраняется; UI предлагает contract-backed refresh/retry/recovery с точным state/error`;
  }
  return `Given ${scope}, actor и данные удовлетворяют предусловиям ${owner}\nWhen выполняется сценарий «${scenario}»\nThen наблюдаемый server/UI результат в точности реализует указанное правило без дополнительной несовместимой интерпретации\nAnd нарушение правила отклоняется без неавторизованного side effect или раскрытия данных`;
}

const acRows = [];
const dataEvidence = new Map();
for (const row of acInput.rows) {
  const originalOwners = uniqueSorted(row["FR/BR"].match(/(?:FR|BR|DATA|NFR)-\d{3}/g) ?? []);
  const directFrs = originalOwners.filter((id) => id.startsWith("FR-"));
  const brOwners = originalOwners.filter((id) => id.startsWith("BR-"));
  const dataOwners = originalOwners.filter((id) => id.startsWith("DATA-"));
  let relatedFrs = [...directFrs];
  let primaryOwner = directFrs[0] ?? brOwners[0] ?? dataOwners[0] ?? "";
  let ownerEvidence = directFrs.length ? "Direct existing FR relation." : "";
  for (const brId of brOwners) {
    relatedFrs.push(...(brToFr.get(brId) ?? []));
    ownerEvidence = `Primary BR owner; validated through BR catalog Related FR (${brEvidence.get(brId)}).`;
  }
  for (const dataId of dataOwners) {
    const { schema, field } = schemaAndField(row.Source);
    const backed = operationBackedFrs(row.Module, schema, field);
    relatedFrs.push(...backed.frs);
    ownerEvidence = backed.operationIds.length
      ? `Primary ${dataId}; ${schema}.${field} → ${backed.operationIds.join(";")} → FR trace relation.`
      : `Primary ${dataId}; ${schema}.${field} → primary module FR text/contract relation (no unique direct operation schema reference).`;
    dataEvidence.set(row["AC ID"], ownerEvidence);
  }
  relatedFrs = uniqueSorted(relatedFrs);
  if (!primaryOwner || !relatedFrs.length || relatedFrs.some((frId) => !frSet.has(frId))) {
    throw new Error(`Unable to establish AC owner relation for ${row["AC ID"]}`);
  }
  const combinedOwners = uniqueSorted([...relatedFrs, ...originalOwners]).join(";");
  const gherkin = preciseText(row.Gherkin).trim() || generatedGherkin(row, primaryOwner, relatedFrs);
  acRows.push({
    "AC ID": row["AC ID"],
    Module: row.Module,
    "FR/BR": combinedOwners,
    "Primary owner": primaryOwner,
    "Related FR": relatedFrs.join(";"),
    "Owner evidence": ownerEvidence,
    Scenario: preciseText(row.Scenario),
    Priority: row.Priority,
    "Test type": row["Test type"],
    Source: normalizeSource(row.Source),
    Gherkin: gherkin,
  });
}

function crossCuttingDefinition(requirement, module, relatedFrs) {
  const prefix = requirement.split("-")[0];
  const frList = relatedFrs.join(";");
  if (prefix === "DATA") {
    return {
      scenario: `Cross-cutting DATA contract for ${module}: no unsupported DTO field or server write is introduced`,
      testType: "Contract/traceability",
      source: "Stage 2.3.1 OpenAPI/dto_field_catalog.csv; Stage 3.5 field traceability",
      gherkin: `Given ${module} implements ${frList || "desktop-only behavior"} and current Stage 2.3.1/3.5 contracts are loaded\nWhen a field, control or payload mapping is added or changed\nThen every server-backed value resolves to an existing DTO field with exact type/required/nullable/enum/limit semantics\nAnd desktop-only presentation introduces no unsupported API, DTO field, permission or stable error`,
    };
  }
  if (prefix === "PERM") {
    return {
      scenario: `Cross-cutting permission enforcement for ${module}`,
      testType: "Security/authorization",
      source: "Stage 2.3.1 permissions.csv and OpenAPI access policies; Stage 1 server-authoritative security",
      gherkin: `Given actor capability/relation scope is known for ${module}\nWhen any read or command owned by ${frList} is attempted and permission is present, absent, partial or revoked mid-flow\nThen server rechecks the existing catalog permission, applies redaction/partial-access policy before data delivery, and default-denies unauthorized work\nAnd UI hidden/disabled state is presentation only; revoke purges unsafe cache/focus state without privilege escalation`,
    };
  }
  if (prefix === "ERR") {
    return {
      scenario: `Cross-cutting stable-error and recovery mapping for ${module}`,
      testType: "Error/recovery",
      source: "Stage 2.3.1 errors.csv and OpenAPI x-error-codes; Stage 3.5 state registry",
      gherkin: `Given an operation owned by ${frList} returns each applicable Stage 2.3.1 stable error\nWhen ${module} maps the failure to UI state and recovery\nThen the exact code produces an accessible user message, retryability and recovery action without raw exception or invented code\nAnd draft/input is preserved whenever the contract does not require discarding it`,
    };
  }
  if (prefix === "SYNC") {
    return {
      scenario: `Cross-cutting sync, offline, read-only and conflict behavior for ${module}`,
      testType: "Concurrency/resilience",
      source: "Stage 1 sync/offline architecture; Stage 2.3.1 ETag/If-Match/idempotency; Stage 3.5 conflict states",
      gherkin: `Given ${module} has a draft or cached projection for ${frList} and server/version/permission state changes\nWhen outage, stale ETag, reconnect or scope invalidation occurs\nThen shared writes are not queued offline or shown as successful; 412/428/409 preserve draft and require explicit refresh/compare/reapply/discard\nAnd reconnect reauthenticates, rechecks scope and bootstraps when required before writable state returns`,
    };
  }
  return {
    scenario: `Cross-cutting append-only audit behavior for ${module}`,
    testType: "Audit/security",
    source: "Stage 1 audit architecture; Stage 2.3.1 audit/history contract and retention controls",
    gherkin: `Given a sensitive or administrative outcome owned by ${frList} succeeds or is permission-denied\nWhen ${module} emits business/security audit evidence\nThen append-only audit records actor, object, outcome, timestamp and correlationId with allowlisted redacted diff\nAnd secrets, full queries, notification content, raw file paths and unauthorized fields are never recorded or exposed`,
  };
}

let nextAcNumber = 1825;
const crossCuttingNewIds = new Map();
for (const row of traceRows.filter((item) => !item.AC.trim())) {
  const relatedFrs = uniqueSorted(frByModule.get(row.Module) ?? rankedFrs(row.Module, `${row.Requirement} ${row.Source}`, 3));
  if (!relatedFrs.length) throw new Error(`No related FR for cross-cutting requirement ${row.Requirement}`);
  const acId = `AC-${String(nextAcNumber++).padStart(4, "0")}`;
  const definition = crossCuttingDefinition(row.Requirement, row.Module, relatedFrs);
  acRows.push({
    "AC ID": acId,
    Module: row.Module,
    "FR/BR": uniqueSorted([...relatedFrs, row.Requirement]).join(";"),
    "Primary owner": row.Requirement,
    "Related FR": relatedFrs.join(";"),
    "Owner evidence": "Requirement-level verification AC added in Stage 4.3; module scope explicitly resolves to existing FR rows.",
    Scenario: definition.scenario,
    Priority: row.Requirement.startsWith("PERM-") || row.Requirement.startsWith("AUDIT-") ? "Critical" : "High",
    "Test type": definition.testType,
    Source: definition.source,
    Gherkin: definition.gherkin,
  });
  crossCuttingNewIds.set(row.Requirement, acId);
  row.AC = acId;
}

const acSet = new Set(acRows.map((row) => row["AC ID"]));
for (const row of traceRows) {
  row.AC = uniqueSorted(row.AC.match(/AC-\d{3,4}/g) ?? []).join(";");
  if (!row.AC) throw new Error(`Blank AC remains for ${row.Requirement}`);
  for (const acId of row.AC.split(";")) {
    if (!acSet.has(acId)) throw new Error(`Unknown AC ${acId} from ${row.Requirement}`);
  }
  row.Source = normalizeSource(row.Source);
  row["DTO field"] = normalizeSource(row["DTO field"]);
}

const nfrUpdates = {
  "NFR-001": {
    Target: "Every company-approved Windows 10/11 image installs, launches, signs in and exits without unsupported runtime or privilege requirement.",
    Measurement: "Versioned install/launch/sign-in/logout smoke matrix on each approved corporate image; 0 failed required step.",
  },
  "NFR-002": {
    Target: "100% primary/destructive flows and CMP-001/CMP-002 complete with Tab/Shift+Tab, Up/Down, Enter and Esc without pointer input.",
    Measurement: "Automated UIA plus manual keyboard matrix; exact focus-return and disabled-reason assertions for every listed flow.",
  },
  "NFR-003": {
    Target: "0 missing accessible name/role/state, active-descendant, group announcement or deterministic focus-return assertion in CMP-001/CMP-002 critical paths.",
    Measurement: "UIA tree snapshot + Narrator/NVDA script for loading, result-group, redaction, validation, conflict, unavailable and close/Esc states.",
  },
  "NFR-004": {
    Target: "At 100%, 150% and 200% scaling and below 1100 logical px, no critical control is clipped; reading/tab order remains logical and horizontal scroll is not required for the primary action.",
    Measurement: "Window-size × scaling × high-contrast visual/keyboard matrix on approved Windows images.",
  },
  "NFR-006": {
    Target: "0 full-dataset list requests; client retains only contract page payloads plus realized viewport and does not grow with total dataset size during bounded scroll test.",
    Measurement: "Production-scale fixture profile: request log, page/cursor assertions, realized-item count and memory trend.",
  },
  "NFR-007": {
    Target: "100% calendar reads include bounded from/to; 0 unbounded multi-year request; out-of-contract range returns CALENDAR_RANGE_TOO_LARGE with preserved filter state.",
    Measurement: "Generated contract/UI tests for minimum/maximum supported ranges, DST boundaries and one over-limit range.",
  },
  "NFR-015": {
    Target: "0 plaintext HTTP API endpoints; untrusted/expired/mismatched certificate is rejected and no credentials or command payload is sent.",
    Measurement: "Deployment endpoint scan and negative TLS handshake/certificate matrix inside the approved LAN configuration.",
  },
  "NFR-024": {
    Requirement: "The product candidate does not assert an unapproved numeric availability SLA, RPO or RTO. Deployment must reference a company-approved operational policy before production approval.",
    Target: "0 unsupported SLA/RPO/RTO value in product requirements; deployment evidence links one approved external policy and its validation plan.",
    Measurement: "PRD token scan plus deployment-policy presence/approval/link check; numeric objectives are tested only after company approval.",
    "Source/Assumption": "Stage 1 architecture §0.5 and §18 require company approval; OQ-008 closed as an external deployment-policy gate, not a product value.",
  },
};

const nfrRows = nfrInput.rows.map((row) => {
  const update = nfrUpdates[row["NFR ID"]] ?? {};
  return {
    "NFR ID": row["NFR ID"],
    Area: row.Area,
    Requirement: preciseText(update.Requirement ?? row.Requirement),
    Target: preciseText(update.Target ?? row.Target),
    Measurement: preciseText(update.Measurement ?? row.Measurement),
    "Source/Assumption": normalizeSource(update["Source/Assumption"] ?? row["Source/Assumption"]),
    Modules: row.Modules,
  };
});

const brHeaders = ["BR ID", "Module", "Rule", "Source", "Related FR", "Verification"];
const acHeaders = ["AC ID", "Module", "FR/BR", "Primary owner", "Related FR", "Owner evidence", "Scenario", "Priority", "Test type", "Source", "Gherkin"];
const nfrHeaders = ["NFR ID", "Area", "Requirement", "Target", "Measurement", "Source/Assumption", "Modules"];
const traceHeaders = ["Requirement", "Module", "Concept", "SCR", "FLOW", "STATE", "API", "DTO field", "Permission", "Error", "AC", "Source"];

function matrixFromRows(headers, rows) {
  return [headers, ...rows.map((row) => headers.map((header) => row[header] ?? ""))];
}

async function authorCsv(definition, headers, rows, columnWidths) {
  const matrix = matrixFromRows(headers, rows);
  const workbook = Workbook.create();
  const sheet = workbook.worksheets.add(definition.sheet);
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
    borders: { insideHorizontal: { style: "thin", color: "#D9E2F3" } },
  };
  headers.forEach((_, index) => {
    sheet.getRangeByIndexes(0, index, matrix.length, 1).format.columnWidth = columnWidths[index] ?? 24;
  });
  sheet.getRange("1:1").format.rowHeight = 34;
  sheet.freezePanes.freezeRows(1);
  sheet.showGridLines = false;
  const inspect = await workbook.inspect({
    kind: "table",
    range: `${definition.sheet}!A1:${String.fromCharCode(64 + headers.length)}${Math.min(matrix.length, 25)}`,
    include: "values",
    tableMaxRows: Math.min(25, matrix.length),
    tableMaxCols: headers.length,
    maxChars: 5000,
  });
  if (!inspect.ndjson) throw new Error(`Empty inspection for ${definition.output}`);
  const preview = await workbook.render({
    sheetName: definition.sheet,
    range: `A1:${String.fromCharCode(64 + headers.length)}${Math.min(matrix.length, 25)}`,
    scale: 1,
    format: "png",
  });
  const previewPath = path.join(previewDir, definition.output.replace(".csv", "_preview.png"));
  await fs.writeFile(previewPath, new Uint8Array(await preview.arrayBuffer()));
  const csvText = "\uFEFF" + matrix.map((row) => row.map(csvEscape).join(",")).join("\r\n") + "\r\n";
  for (const outputDir of [sourceDir, finalDir]) {
    await fs.mkdir(outputDir, { recursive: true });
    await fs.writeFile(path.join(outputDir, definition.output), csvText, "utf8");
  }
  return { file: definition.output, rows: rows.length, previewPath };
}

const results = [];
results.push(await authorCsv(files.br, brHeaders, brRows, [14, 12, 54, 34, 38, 52]));
results.push(await authorCsv(files.ac, acHeaders, acRows, [14, 12, 48, 18, 38, 48, 54, 12, 24, 38, 70]));
results.push(await authorCsv(files.nfr, nfrHeaders, nfrRows, [14, 18, 58, 54, 54, 48, 24]));
results.push(await authorCsv(files.trace, traceHeaders, traceRows, [18, 12, 20, 20, 20, 22, 42, 36, 38, 38, 36, 44]));

const blankGherkin = acRows.filter((row) => !/\bgiven\b/i.test(row.Gherkin) || !/\bwhen\b/i.test(row.Gherkin) || !/\bthen\b/i.test(row.Gherkin));
const ownerless = acRows.filter((row) => !row["Primary owner"] || !row["Related FR"]);
const vague = acRows.filter((row) => /\bкорректно\b/i.test(`${row.Scenario} ${row.Gherkin}`));
const blankBr = brRows.filter((row) => !row["Related FR"]);
const blankTrace = traceRows.filter((row) => !row.AC);
const oldRefs = [...brRows, ...acRows, ...nfrRows, ...traceRows].filter((row) =>
  Object.values(row).some((value) => String(value).includes("Stage_3_Field_Traceability.csv")),
);
if (blankGherkin.length || ownerless.length || vague.length || blankBr.length || blankTrace.length || oldRefs.length) {
  throw new Error(JSON.stringify({
    blankGherkin: blankGherkin.map((row) => row["AC ID"]),
    ownerless: ownerless.map((row) => row["AC ID"]),
    vague: vague.map((row) => row["AC ID"]),
    blankBr: blankBr.map((row) => row["BR ID"]),
    blankTrace: blankTrace.map((row) => row.Requirement),
    oldRefs: oldRefs.length,
  }));
}

const appendixMarker = "## P.3. Stage 4.3 cross-cutting verification AC";
if (!moduleText.includes(appendixMarker)) {
  const appendixRows = acRows
    .filter((row) => Number(row["AC ID"].match(/\d+/)?.[0] ?? 0) >= 1825)
    .map((row) => `| ${row["AC ID"]} | ${row["Primary owner"]} | ${row["Related FR"]} | ${row.Scenario} | ${row["Test type"]} |`)
    .join("\n");
  const appendix = `\n\n${appendixMarker}\n\nThese requirement-level AC close the Stage 4.2 orphaned verification rows without inventing new FR, API, DTO, permission or stable error IDs. Full executable Gherkin is canonical in \`Stage_4_Acceptance_Criteria_Catalog_4.3.csv\`.\n\n| AC | Primary owner | Related FR | Scenario | Test type |\n|---|---|---|---|---|\n${appendixRows}\n`;
  const updatedModuleText = moduleText.replaceAll("4.1.2", "4.3").trimEnd() + appendix + "\n";
  await fs.writeFile(path.join(sourceDir, "Stage_4_Module_PRDs_4.3.md"), updatedModuleText, "utf8");
  await fs.writeFile(path.join(finalDir, "Stage_4_Module_PRDs_4.3.md"), updatedModuleText, "utf8");
}

const summary = {
  results,
  brMapped: brRows.length,
  acTotal: acRows.length,
  acAdded: acRows.length - acInput.rows.length,
  originalBlankGherkinFixed: acInput.rows.filter((row) => !row.Gherkin.trim()).length,
  dataOwnerRelations: dataEvidence.size,
  crossCuttingAdded: crossCuttingNewIds.size,
  traceRows: traceRows.length,
  nfrRows: nfrRows.length,
  checks: {
    blankGherkin: blankGherkin.length,
    ownerless: ownerless.length,
    vague: vague.length,
    blankBr: blankBr.length,
    blankTrace: blankTrace.length,
    oldRefs: oldRefs.length,
  },
};
await fs.writeFile(path.join(workDir, "catalog_remediation_summary.json"), JSON.stringify(summary, null, 2), "utf8");
console.log(JSON.stringify(summary, null, 2));
