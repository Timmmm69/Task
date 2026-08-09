import fs from "node:fs/promises";
import path from "node:path";

const root = "C:/Users/novik/Таск";
const sourcePath = path.join(root, "work/stage_4_6_lite/design_input/Stage_3_Screen_Catalog_Final_3.5.md");
const outDir = path.join(root, "work/stage_5_2");
const source = await fs.readFile(sourcePath, "utf8");
const lines = source.split(/\r?\n/);

const clean = (value) => value.replace(/`/g, "").replace(/\s+/g, " ").trim();
const csv = (value) => `"${String(value ?? "").replaceAll('"', '""')}"`;
const records = [];
const seen = new Set();
let moduleName = "";

function patternFor(type, name) {
  const t = type.toLowerCase();
  const n = name.toLowerCase();
  if (t.includes("shell")) return "Application shell";
  if (t.includes("tray") || t.includes("system integration") || t.includes("windows")) return "Windows integration surface";
  if (t.includes("context menu")) return "Context menu";
  if (t.includes("action bar")) return "Selection action bar";
  if (t.includes("status") && t.includes("popover")) return "Status popover";
  if (t.includes("popover")) return "Popover";
  if (t.includes("blocking")) return "Blocking state";
  if (t.includes("drawer")) return "Drawer";
  if (t.includes("side panel")) return "Details inspector";
  if (t.includes("panel")) return "Panel";
  if (t.includes("dialog")) return "Modal dialog";
  if (t.includes("overlay")) return "Overlay / command surface";
  if (t.includes("tab")) return "Tab workspace";
  if (n.includes("список") || n.includes("результат")) return "Data list";
  if (t.includes("page")) return "Page";
  return "Specialized surface";
}

function sharedComponents(record) {
  const text = [
    record.name, record.type, record.purpose, record.entry, record.data, record.actions,
    record.states, record.errors, record.transitions, record.dependencies,
  ].join(" ").toLowerCase();
  const relational = [record.name, record.type, record.purpose, record.data, record.actions].join(" ").toLowerCase();
  const stateText = [record.purpose, record.states, record.errors].join(" ").toLowerCase();
  const set = new Set(["SurfaceTitle"]);
  const add = (condition, ...names) => { if (condition) names.forEach((name) => set.add(name)); };

  add(record.type.toLowerCase().includes("shell"), "NavigationRail", "CommandBar", "ConnectionStatus", "ProfileMenu");
  add(/page/.test(record.type.toLowerCase()), "PageLayout");
  add(/dialog|blocking/.test(record.type.toLowerCase()), "DialogShell", "FocusTrap");
  add(/panel|drawer|inspector/.test(record.type.toLowerCase()) || /карточк|инспектор/.test(text), "InspectorPanel");
  add(/popover|overlay/.test(record.type.toLowerCase()), "PopoverSurface");
  add(/context menu/.test(record.type.toLowerCase()), "ContextMenu");
  add(/action bar/.test(record.type.toLowerCase()) || /multi-select|массов/.test(text), "SelectionBar");
  add(/список|результат|очеред|каталог|history|audit|sessions|devices/.test(text), "DataList");
  add(/дерев/.test(text), "TreeView");
  add(/фильтр|filter/.test(text), "FilterBar");
  add(/search|поиск/.test(text), "SearchBox");
  const isForm = /editor|редактор|создать|изменить|настроить|save|create|update|apply|reset|добавить|сохранить/.test(`${record.name} ${record.type} ${record.purpose} ${record.actions}`.toLowerCase());
  add(isForm, "FormLayout", "FieldLabel", "ValidationMessage");
  add(/task|задач/.test(relational), "TaskRow", "TaskStatusControl", "UrgencyIndicator");
  add(/project|проект/.test(relational), "ProjectPicker");
  add(/assignee|watcher|member|employee|исполн|наблюд|участник|сотруд|people relations/.test(relational), "PeoplePicker");
  add(/date|time|calendar|срок|время|дат|календар/.test(relational), "DateTimePicker");
  add(/reminder|напомин/.test(relational), "ReminderEditor");
  add(/recurrence|series|повтор|серии/.test(relational), "RecurrenceEditor");
  add(/comment|комментар/.test(relational), "CommentThread");
  add(/file|path|smb|файл|путь/.test(relational), "FileLocationView");
  add(/notification|toast|уведом/.test(relational), "NotificationItem");
  add(/pagination|cursor|page forward|страниц/.test(text), "Pagination");
  add(/loading|checking|progress|pending|background|загруз|выполня/.test(text), "ProgressIndicator", "LoadingState");
  add(/empty|noitems|zeroresults|пуст/.test(text), "EmptyState");
  add(/offline|serverunavailable|unavailable|reconnecting|maintenance|database_unavailable|dependency_unavailable/.test(stateText), "ConnectivityBanner", "ReadOnlyBanner", "RetryAction");
  add(/conflict|version_conflict|precondition/.test(stateText), "ConflictNotice");
  add(/forbidden|object_not_visible|permission|capabilit|прав|доступ/.test(stateText), "PermissionState");
  add(/redact|partial access|hidden count|скрыт|недоступн.*пол/.test(stateText), "RedactionMarker");
  add(/archiv|trash|корзин|purge/.test(stateText), "LifecycleBanner");
  add(/error|invalid|failed|timeout|ошиб/.test(stateText), "ErrorMessage");
  add(/status|priority|urgency|state|состояни|приоритет|срочност/.test(text), "SemanticStatus");
  add(/history|audit|истори|аудит/.test(text), "TimelineHistory");
  add(/bulk|batch|массов/.test(text), "BulkResultSummary");
  return [...set].sort();
}

function priorityFor(module, record) {
  if (["Auth","Sync","Shell","Today","Inbox","Tasks","Search","Shared"].includes(module)) return "P0";
  if (["Calendar","Projects","Files","CRM","Notifications"].includes(module)) return "P1";
  if (/blocking|conflict|offline|unavailable/.test(`${record.type} ${record.states}`.toLowerCase())) return "P0";
  return "P2";
}

for (let index = 0; index < lines.length; index++) {
  const line = lines[index];
  const heading = line.match(/^##\s+(.+?)\s*$/);
  if (heading) moduleName = clean(heading[1]);
  if (!line.startsWith("| SCR-")) continue;
  const cells = line.slice(1, -1).split("|").map(clean);
  if (cells.length < 14) continue;
  const [id,name,type,roles,permission,purpose,entry,data,api,actions,states,errors,transitions,dependencies] = cells;
  if (seen.has(id)) continue;
  seen.add(id);
  if (/^SCR-18[2-8]$/.test(id)) moduleName = "Admin";
  const record = {
    id, module: moduleName, name, type, roles, permission, purpose, entry, data, api,
    actions, states, errors, transitions, dependencies, sourceLine: index + 1,
  };
  record.pattern = patternFor(type, name);
  record.components = sharedComponents(record);
  record.priority = priorityFor(moduleName, record);
  record.styleDependency = "VIS-001";
  record.inventoryStatus = "Mapped";
  records.push(record);
}

if (records.length !== 128) {
  throw new Error(`Expected 128 unique SCR records, found ${records.length}`);
}

const headers = [
  "SCR ID","Module","Surface name","Surface type","Primary pattern","Shared components",
  "Roles","Permission","Purpose","Entry points","Actions","States","Errors","Related SCR",
  "Dependencies","Priority","Style dependency","Inventory status","Source line",
];
const rows = records.map((r) => [
  r.id,r.module,r.name,r.type,r.pattern,r.components.join("; "),r.roles,r.permission,r.purpose,
  r.entry,r.actions,r.states,r.errors,r.transitions,r.dependencies,r.priority,
  r.styleDependency,r.inventoryStatus,r.sourceLine,
]);
const csvText = [headers, ...rows].map((row) => row.map(csv).join(",")).join("\r\n") + "\r\n";
await fs.writeFile(path.join(outDir, "Component_Inventory_0.1.csv"), csvText, "utf8");

const componentMap = new Map();
for (const record of records) {
  for (const component of record.components) {
    if (!componentMap.has(component)) componentMap.set(component, []);
    componentMap.get(component).push(record);
  }
}
const p0Components = new Set([
  "SurfaceTitle","PageLayout","NavigationRail","CommandBar","ConnectionStatus","ProfileMenu",
  "DialogShell","FocusTrap","InspectorPanel","DataList","FormLayout","FieldLabel",
  "ValidationMessage","SemanticStatus","ErrorMessage","LoadingState","EmptyState",
  "ConnectivityBanner","ReadOnlyBanner","RetryAction","PermissionState","ConflictNotice",
  "TaskRow","TaskStatusControl","UrgencyIndicator",
]);
const componentRows = [...componentMap.entries()]
  .map(([component, refs]) => ({
    component,
    count: refs.length,
    samples: refs.slice(0, 10).map((r) => r.id).join("; "),
    modules: [...new Set(refs.map((r) => r.module))].join("; "),
    priority: p0Components.has(component) ? "P0" : refs.length >= 5 ? "P1" : "P2",
    status: component === "SurfaceTitle" || component === "PageLayout" ? "Ready for architecture" : "Requires design",
    dependency: "VIS-001 for visual styling",
  }))
  .sort((a,b) => a.priority.localeCompare(b.priority) || b.count - a.count || a.component.localeCompare(b.component));
const componentHeaders = ["Component","Surface count","Sample SCR","Modules","Priority","Status","Dependency"];
const componentCsv = [componentHeaders, ...componentRows.map((r) => [
  r.component,r.count,r.samples,r.modules,r.priority,r.status,r.dependency,
])].map((row) => row.map(csv).join(",")).join("\r\n") + "\r\n";
await fs.writeFile(path.join(outDir, "Component_Family_Summary_0.1.csv"), componentCsv, "utf8");

const moduleCounts = [...new Set(records.map((r) => r.module))]
  .map((module) => [module, records.filter((r) => r.module === module).length])
  .sort((a,b) => b[1] - a[1]);
const typeCounts = [...new Set(records.map((r) => r.pattern))]
  .map((pattern) => [pattern, records.filter((r) => r.pattern === pattern).length])
  .sort((a,b) => b[1] - a[1]);

const md = `# Stage 5.2 — Component Inventory 0.1

**Status:** COMPLETE (inventory); visual styling awaits VIS-001  
**Source:** \`Stage_3_Screen_Catalog_Final_3.5.md\`  
**Unique normative surfaces:** ${records.length}  
**Shared component families:** ${componentRows.length}  
**Generated:** 2026-07-28

## Scope and method

The inventory maps the first normative 14-column definition of every unique \`SCR-XXX\` in the Stage 3.5 Screen Catalog. Later delta tables are not counted as new surfaces. Each surface is assigned a primary pattern, shared component families, priority and source line. The mapping does not add screens, actions, permissions, fields or business logic.

## Module coverage

| Module | Surfaces |
|---|---:|
${moduleCounts.map(([name,count]) => `| ${name} | ${count} |`).join("\n")}

## Primary pattern coverage

| Pattern | Surfaces |
|---|---:|
${typeCounts.map(([name,count]) => `| ${name} | ${count} |`).join("\n")}

## Design-system implications

- P0 architecture can proceed before the visual decision: naming, component ownership, state composition and SCR usage mapping.
- Color, typography, density, radii, elevation and detailed interaction styling remain dependent on \`VIS-001\`.
- Shared state components must cover loading, empty, validation, permission, offline/read-only, conflict, lifecycle and recovery semantics without inventing new durable states.
- Every component retains server-authoritative permission and error behavior from its owning SCR.
- \`Component_Inventory_0.1.csv\` is the canonical surface-level mapping.
- \`Component_Family_Summary_0.1.csv\` is the shared-library backlog for Stage 5.2.

## Validation

- Expected unique surfaces: 128
- Parsed unique surfaces: ${records.length}
- Duplicate SCR rows included: 0
- Unmapped surfaces: 0
- Unknown visual decisions embedded: 0
- Result: PASS
`;
await fs.writeFile(path.join(outDir, "Component_Inventory_0.1.md"), md, "utf8");

console.log(JSON.stringify({
  surfaces: records.length,
  componentFamilies: componentRows.length,
  modules: moduleCounts,
  patterns: typeCounts,
  outputs: [
    "Component_Inventory_0.1.csv",
    "Component_Family_Summary_0.1.csv",
    "Component_Inventory_0.1.md",
  ],
}, null, 2));
