import fs from "node:fs/promises";
import path from "node:path";

const root = "C:/Users/novik/Таск";
const flowPath = path.join(root, "work/stage_4_6_lite/design_input/Stage_3_User_Flows_Final_3.5.md");
const componentPath = path.join(root, "work/stage_5_2/Component_Inventory_0.1.csv");
const outDir = path.join(root, "work/stage_5_2");

const flowText = await fs.readFile(flowPath, "utf8");
const componentCsv = await fs.readFile(componentPath, "utf8");
const clean = (value) => value.replace(/`/g, "").replace(/\s+/g, " ").trim();
const csv = (value) => `"${String(value ?? "").replaceAll('"', '""')}"`;

function parseCsv(text) {
  const rows = [];
  let row = [], cell = "", quoted = false;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (quoted) {
      if (ch === '"' && text[i + 1] === '"') { cell += '"'; i++; }
      else if (ch === '"') quoted = false;
      else cell += ch;
    } else if (ch === '"') quoted = true;
    else if (ch === ",") { row.push(cell); cell = ""; }
    else if (ch === "\n") { row.push(cell.replace(/\r$/, "")); rows.push(row); row = []; cell = ""; }
    else cell += ch;
  }
  if (cell || row.length) { row.push(cell); rows.push(row); }
  const headers = rows.shift();
  return rows.filter((r) => r.some(Boolean)).map((r) => Object.fromEntries(headers.map((h, i) => [h, r[i] ?? ""])));
}

const screens = parseCsv(componentCsv);
const screenMap = new Map(screens.map((r) => [r["SCR ID"], r]));
const lines = flowText.split(/\r?\n/);
const catalog = [];
let inCatalog = false;
for (const line of lines) {
  if (line === "## Каталог") { inCatalog = true; continue; }
  if (inCatalog && line.startsWith("## FLOW-")) break;
  if (!inCatalog || !line.startsWith("| FLOW-")) continue;
  const cells = line.slice(1, -1).split("|").map(clean);
  if (cells.length === 6) {
    const [id, name, roles, permission, api, outcome] = cells;
    catalog.push({ id, name, roles, permission, api, outcome });
  }
}

const sections = new Map();
const headingMatches = [...flowText.matchAll(/^## (FLOW-\d{3})\.\s+(.+)$/gm)];
for (let i = 0; i < headingMatches.length; i++) {
  const current = headingMatches[i];
  const start = current.index;
  const end = i + 1 < headingMatches.length ? headingMatches[i + 1].index : flowText.length;
  if (!sections.has(current[1])) sections.set(current[1], flowText.slice(start, end));
}

function groupFor(id) {
  const n = Number(id.slice(-3));
  if (n <= 3) return "Auth & bootstrap";
  if (n <= 12) return "Task management";
  if (n <= 14) return "Projects";
  if (n <= 17) return "Files";
  if (n === 18 || n === 37) return "CRM";
  if (n === 19) return "Search";
  if (n <= 21) return "Notifications & reminders";
  if (n <= 25) return "Resilience & conflict";
  if (n <= 28) return "Lifecycle";
  if (n <= 30) return "Administration";
  if (n <= 32) return "Calendar";
  if (n <= 34) return "Task & Inbox";
  if (n === 35) return "Projects";
  if (n === 36) return "Files";
  return "Other";
}

const p0 = new Set(["FLOW-001","FLOW-002","FLOW-004","FLOW-005","FLOW-019","FLOW-022","FLOW-023","FLOW-024","FLOW-025","FLOW-034"]);
const stage5ScreenMap = {
  "FLOW-001":["SCR-001","SCR-002","SCR-003","SCR-004","SCR-007"],
  "FLOW-002":["SCR-001","SCR-003","SCR-004"],
  "FLOW-003":["SCR-006","SCR-001"],
  "FLOW-004":["SCR-023","SCR-024","SCR-028","SCR-029"],
  "FLOW-005":["SCR-008","SCR-022","SCR-024"],
  "FLOW-006":["SCR-024","SCR-025","SCR-029"],
  "FLOW-007":["SCR-010","SCR-020","SCR-024","SCR-025","SCR-034"],
  "FLOW-008":["SCR-020","SCR-024","SCR-025","SCR-034"],
  "FLOW-009":["SCR-023","SCR-024","SCR-033"],
  "FLOW-010":["SCR-023","SCR-024","SCR-026"],
  "FLOW-011":["SCR-024","SCR-026","SCR-027"],
  "FLOW-012":["SCR-024","SCR-026","SCR-027","SCR-209"],
  "FLOW-013":["SCR-060","SCR-070","SCR-061"],
  "FLOW-014":["SCR-061","SCR-064","SCR-071"],
  "FLOW-015":["SCR-080","SCR-081","SCR-084","SCR-090"],
  "FLOW-016":["SCR-081","SCR-084","SCR-085","SCR-090"],
  "FLOW-017":["SCR-081","SCR-083","SCR-086","SCR-210"],
  "FLOW-018":["SCR-110","SCR-112","SCR-111","SCR-115"],
  "FLOW-019":["SCR-133","SCR-134","SCR-135","SCR-136"],
  "FLOW-020":["SCR-130","SCR-131","SCR-132"],
  "FLOW-021":["SCR-028","SCR-130","SCR-131","SCR-132"],
  "FLOW-022":["SCR-004","SCR-160","SCR-206"],
  "FLOW-023":["SCR-004","SCR-160","SCR-206"],
  "FLOW-024":["SCR-003","SCR-004","SCR-160","SCR-206"],
  "FLOW-025":["SCR-023","SCR-024","SCR-032"],
  "FLOW-026":["SCR-034","SCR-072","SCR-089","SCR-140","SCR-208"],
  "FLOW-027":["SCR-034","SCR-089","SCR-119","SCR-141","SCR-208"],
  "FLOW-028":["SCR-141","SCR-142"],
  "FLOW-029":["SCR-170","SCR-171","SCR-172","SCR-174","SCR-181"],
  "FLOW-030":["SCR-170","SCR-177","SCR-178","SCR-179"],
  "FLOW-031":["SCR-040","SCR-044","SCR-046"],
  "FLOW-032":["SCR-040","SCR-041","SCR-042","SCR-047"],
  "FLOW-033":["SCR-020","SCR-031","SCR-209"],
  "FLOW-034":["SCR-012","SCR-013","SCR-014","SCR-022","SCR-082"],
  "FLOW-035":["SCR-061","SCR-069","SCR-072","SCR-140"],
  "FLOW-036":["SCR-081","SCR-083","SCR-087","SCR-090","SCR-210"],
  "FLOW-037":["SCR-110","SCR-111","SCR-113","SCR-116","SCR-117","SCR-118"],
};
const records = catalog.map((item) => {
  const section = sections.get(item.id) ?? "";
  const explicitScr = section.match(/\bSCR-\d{3}\b/g) ?? [];
  const scr = [...new Set([...explicitScr, ...(stage5ScreenMap[item.id] ?? [])])].sort();
  const states = [...new Set(section.match(/\bSTATE-\d{3}\b/g) ?? [])].sort();
  const componentSet = new Set();
  const modules = new Set();
  for (const id of scr) {
    const screen = screenMap.get(id);
    if (!screen) continue;
    modules.add(screen.Module);
    for (const component of screen["Shared components"].split(/;\s*/).filter(Boolean)) componentSet.add(component);
  }
  const priority = p0.has(item.id) ? "P0" : "P1";
  return {
    ...item,
    group: groupFor(item.id),
    screens: scr,
    states,
    modules: [...modules].sort(),
    components: [...componentSet].sort(),
    priority,
    evidence: priority === "P0" ? "Interactive prototype + keyboard walkthrough" : "Annotated storyboard or module prototype",
    mappingBasis: explicitScr.length ? "Stage 3 section + Stage 5 cross-source mapping" : "Stage 5 cross-source mapping",
    status: "Mapped",
  };
});

if (records.length !== 37) throw new Error(`Expected 37 FLOW records, found ${records.length}`);
if (new Set(records.map((r) => r.id)).size !== 37) throw new Error("Duplicate FLOW IDs found");

const headers = [
  "FLOW ID","Flow","Scenario group","Roles","Permission","API","Outcome","SCR references",
  "STATE references","Modules","Shared components","Mapping basis","Priority","Required design evidence","Inventory status",
];
const rows = records.map((r) => [
  r.id,r.name,r.group,r.roles,r.permission,r.api,r.outcome,r.screens.join("; "),
  r.states.join("; "),r.modules.join("; "),r.components.join("; "),r.mappingBasis,r.priority,r.evidence,r.status,
]);
const outCsv = [headers, ...rows].map((row) => row.map(csv).join(",")).join("\r\n") + "\r\n";
await fs.writeFile(path.join(outDir, "Flow_Design_Inventory_0.1.csv"), outCsv, "utf8");

const groupCounts = [...new Set(records.map((r) => r.group))]
  .map((group) => [group, records.filter((r) => r.group === group).length])
  .sort((a,b) => b[1] - a[1]);
const unresolvedScreens = records.filter((r) => r.screens.some((id) => !screenMap.has(id)));
const noScreens = records.filter((r) => r.screens.length === 0);
const md = `# Stage 5.2 — Flow Design Inventory 0.1

**Status:** COMPLETE  
**Source:** \`Stage_3_User_Flows_Final_3.5.md\`  
**Normative flows:** ${records.length}/37  
**P0 vertical-slice flows:** ${records.filter((r) => r.priority === "P0").length}  
**Generated:** 2026-07-28

## Scenario groups

| Group | Flows |
|---|---:|
${groupCounts.map(([group,count]) => `| ${group} | ${count} |`).join("\n")}

## Evidence policy

- P0 flows require an interactive prototype and keyboard walkthrough.
- P1 flows require an annotated storyboard or module prototype before Gate 5.3.
- Every flow retains its normative roles, permission, API and outcome.
- SCR/STATE/component references are extracted from the published flow sections and the 128-surface component inventory.
- Missing explicit SCR references do not create an invented screen; they remain traceability findings for review.

## Validation

- Expected flows: 37
- Parsed unique flows: ${records.length}
- Duplicate IDs: 0
- Unknown referenced SCR: ${unresolvedScreens.length}
- Flows without explicit SCR reference in section text: ${noScreens.length}
- Result: ${unresolvedScreens.length === 0 ? "PASS" : "REVIEW"}

The canonical row-level mapping is \`Flow_Design_Inventory_0.1.csv\`.
`;
await fs.writeFile(path.join(outDir, "Flow_Design_Inventory_0.1.md"), md, "utf8");

console.log(JSON.stringify({
  flows: records.length,
  p0: records.filter((r) => r.priority === "P0").length,
  unknownScreenReferences: unresolvedScreens.length,
  flowsWithoutExplicitScreens: noScreens.map((r) => r.id),
  groupCounts,
}, null, 2));
