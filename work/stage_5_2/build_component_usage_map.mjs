import fs from "node:fs/promises";
import path from "node:path";

const root = path.resolve(".");
const componentInventoryPath = path.join(root, "work/stage_5_2/Component_Inventory_0.1.csv");
const componentSummaryPath = path.join(root, "work/stage_5_2/Component_Family_Summary_0.1.csv");
const flowInventoryPath = path.join(root, "work/stage_5_2/Flow_Design_Inventory_0.1.csv");
const stateAuditPath = path.join(root, "outputs/stage_4_6_lite/Stage_4_6_Lite_STATE_Audit.csv");
const outputCsvPath = path.join(root, "work/stage_5_2/Component_Usage_Map_0.1.csv");
const outputMdPath = path.join(root, "work/stage_5_2/Component_Usage_Map_0.1.md");

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

function uniqueSorted(values) {
  return [...new Set(values.filter(Boolean))].sort((a, b) => a.localeCompare(b, "en"));
}

const componentInventory = parseCsv(await fs.readFile(componentInventoryPath, "utf8"));
const componentSummary = parseCsv(await fs.readFile(componentSummaryPath, "utf8"));
const flowInventory = parseCsv(await fs.readFile(flowInventoryPath, "utf8"));
const stateAudit = parseCsv(await fs.readFile(stateAuditPath, "utf8"));

const componentTier = {
  SurfaceTitle: "03 Core Components",
  PermissionState: "04 State Components",
  ConnectivityBanner: "04 State Components",
  FieldLabel: "03 Core Components",
  FormLayout: "06 Patterns",
  ReadOnlyBanner: "04 State Components",
  RetryAction: "04 State Components",
  ValidationMessage: "04 State Components",
  PageLayout: "06 Patterns",
  ConflictNotice: "04 State Components",
  DialogShell: "03 Core Components",
  FocusTrap: "02 Primitives",
  ErrorMessage: "04 State Components",
  DataList: "03 Core Components",
  SemanticStatus: "04 State Components",
  TaskRow: "05 Domain Components",
  TaskStatusControl: "05 Domain Components",
  UrgencyIndicator: "05 Domain Components",
  InspectorPanel: "06 Patterns",
  EmptyState: "04 State Components",
  LoadingState: "04 State Components",
  CommandBar: "03 Core Components",
  ConnectionStatus: "04 State Components",
  NavigationRail: "03 Core Components",
  ProfileMenu: "03 Core Components",
  PeoplePicker: "05 Domain Components",
  DateTimePicker: "05 Domain Components",
  TimelineHistory: "05 Domain Components",
  ProjectPicker: "05 Domain Components",
  FilterBar: "06 Patterns",
  ReminderEditor: "05 Domain Components",
  FileLocationView: "05 Domain Components",
  RecurrenceEditor: "05 Domain Components",
  ProgressIndicator: "04 State Components",
  LifecycleBanner: "04 State Components",
  NotificationItem: "05 Domain Components",
  SearchBox: "03 Core Components",
  RedactionMarker: "04 State Components",
  Pagination: "06 Patterns",
  PopoverSurface: "06 Patterns",
  ContextMenu: "06 Patterns",
  BulkResultSummary: "04 State Components",
  CommentThread: "05 Domain Components",
  SelectionBar: "06 Patterns",
  TreeView: "06 Patterns",
};

const variants = {
  SurfaceTitle: "page | panel | dialog",
  PermissionState: "available | disabled-with-reason | forbidden-result | unavailable",
  ConnectivityBanner: "offline | reconnecting | maintenance | unavailable",
  FieldLabel: "normal | required | disabled | error",
  FormLayout: "simple | sectioned | dirty | read-only",
  ReadOnlyBanner: "offline | archived | trashed | permission",
  RetryAction: "safe-read retry | diagnostics",
  ValidationMessage: "inline | summary-linked",
  PageLayout: "list | list-detail | editor | settings",
  ConflictNotice: "stale | precondition | compare | reapply",
  DialogShell: "modal | blocking | destructive",
  FocusTrap: "initial | cycle | return-target",
  ErrorMessage: "inline | block | page | trace-id",
  DataList: "loading | empty | selected | stale | partial",
  SemanticStatus: "info | success | warning | danger | offline",
  TaskRow: "default | focused | selected | overdue | read-only",
  TaskStatusControl: "allowed | pending | disabled | conflict",
  UrgencyIndicator: "low | medium | high | critical",
  InspectorPanel: "empty-selection | loading | details | stale",
  EmptyState: "authorized-empty | filtered-empty | no-cache",
  LoadingState: "initial | refreshing | section",
  CommandBar: "page | selection | narrow",
  ConnectionStatus: "online | syncing | offline | read-only",
  NavigationRail: "expanded | compact | capability-filtered",
  ProfileMenu: "normal | offline | session-issue",
  PeoplePicker: "loading | results | selected | redacted | unavailable",
  DateTimePicker: "date-only | date-time | invalid | disabled",
  TimelineHistory: "loading | content | empty | partial",
  ProjectPicker: "loading | results | selected | unavailable",
  FilterBar: "empty | active | invalid | narrow",
  ReminderEditor: "none | scheduled | invalid | disabled",
  FileLocationView: "available | unavailable | forbidden | diagnostics",
  RecurrenceEditor: "none | rule | exception | invalid",
  ProgressIndicator: "determinate | indeterminate | paused | failed",
  LifecycleBanner: "active | completed | archived | trashed",
  NotificationItem: "unread | read | unavailable-target | action-failed",
  SearchBox: "idle | typing | loading | results | no-results",
  RedactionMarker: "partial-access | unavailable | anonymized",
  Pagination: "first | middle | last | loading",
  PopoverSurface: "open | loading | error | closing",
  ContextMenu: "available | capability-filtered | disabled-with-reason",
  BulkResultSummary: "success | partial | failed | retryable",
  CommentThread: "loading | content | empty | unavailable",
  SelectionBar: "single | multi | partial-capability | busy",
  TreeView: "collapsed | expanded | loading-children | unavailable",
};

const stateTargets = stateAudit.map((row) => row["Canonical target"]);
const stateRules = {
  LoadingState: ["Initial", "Loading", "Refreshing"],
  EmptyState: ["Empty", "FilteredEmpty"],
  PermissionState: ["Forbidden", "ObjectUnavailable", "PartialAccess"],
  ConnectivityBanner: ["ServerUnavailable", "Reconnecting"],
  ConnectionStatus: ["ServerUnavailable", "Reconnecting"],
  ReadOnlyBanner: ["ServerUnavailable (cached read-only presentation)", "Archived", "Trashed"],
  ConflictNotice: ["Conflict", "PreconditionFailed"],
  ValidationMessage: ["ValidationError"],
  ErrorMessage: ["ValidationError", "ObjectUnavailable", "ServerUnavailable"],
  LifecycleBanner: ["Archived", "Trashed"],
  RedactionMarker: ["PartialAccess", "ObjectUnavailable"],
  DataList: ["Loading", "Refreshing", "Empty", "FilteredEmpty", "PartialAccess"],
  InspectorPanel: ["Loading", "ObjectUnavailable", "PartialAccess"],
  SearchBox: ["Loading", "Empty", "FilteredEmpty", "PartialAccess"],
  FileLocationView: ["ObjectUnavailable", "Forbidden", "ServerUnavailable"],
  BulkResultSummary: ["Conflict", "ValidationError", "ObjectUnavailable"],
};

const additionalNfr = {
  DataList: ["NFR-006", "NFR-023", "NFR-025"],
  Pagination: ["NFR-006", "NFR-023", "NFR-025"],
  SearchBox: ["NFR-008", "NFR-023", "NFR-025"],
  FilterBar: ["NFR-008", "NFR-025"],
  ConnectivityBanner: ["NFR-009", "NFR-010", "NFR-024"],
  ConnectionStatus: ["NFR-009", "NFR-010", "NFR-024"],
  ReadOnlyBanner: ["NFR-009", "NFR-010"],
  ConflictNotice: ["NFR-011", "NFR-012", "NFR-017"],
  RetryAction: ["NFR-012", "NFR-017"],
  ErrorMessage: ["NFR-017"],
  FileLocationView: ["NFR-018", "NFR-019", "NFR-020"],
  TimelineHistory: ["NFR-021", "NFR-023"],
  RecurrenceEditor: ["NFR-007"],
  DateTimePicker: ["NFR-007"],
  NotificationItem: ["NFR-024"],
  BulkResultSummary: ["NFR-025"],
};

const baselineNfr = ["NFR-002", "NFR-003", "NFR-004", "NFR-005"];

const usageRows = componentSummary.map((summary) => {
  const component = summary.Component;
  const scrRows = componentInventory.filter((row) => splitRefs(row["Shared components"]).includes(component));
  const flowRows = flowInventory.filter((row) => splitRefs(row["Shared components"]).includes(component));
  const scrIds = uniqueSorted(scrRows.map((row) => row["SCR ID"]));
  const flowIds = uniqueSorted(flowRows.map((row) => row["FLOW ID"]));
  const modules = uniqueSorted(scrRows.flatMap((row) => splitRefs(row.Module)));
  const surfaceTypes = uniqueSorted(scrRows.map((row) => row["Surface type"]));
  const relatedStates = (stateRules[component] ?? []).filter((target) => stateTargets.includes(target));
  const nfrIds = uniqueSorted([...baselineNfr, ...(additionalNfr[component] ?? [])]);
  return {
    "Component": component,
    "Library tier": componentTier[component] ?? "06 Patterns",
    "Priority": summary.Priority,
    "Surface count": scrIds.length,
    "SCR IDs": scrIds.join("; "),
    "FLOW count": flowIds.length,
    "FLOW IDs": flowIds.join("; "),
    "Modules": modules.join("; "),
    "Surface types": surfaceTypes.join("; "),
    "Required variants": variants[component] ?? "default | focused | disabled | error",
    "Related canonical STATE": relatedStates.join("; "),
    "Related NFR": nfrIds.join("; "),
    "Accessibility contract": "Visible focus; keyboard path; UIA name/role/state/value; non-color semantics; 200% scaling; long RU text",
    "Mapping basis": "Component Inventory + Flow Inventory + State audit + NFR catalog",
    "Contract status": "Behavioral contract ready",
    "Visual status": "Pending VIS-001",
  };
});

const headers = Object.keys(usageRows[0]);
const csv = [
  headers.map(csvEscape).join(","),
  ...usageRows.map((row) => headers.map((header) => csvEscape(row[header])).join(",")),
].join("\r\n") + "\r\n";
await fs.writeFile(outputCsvPath, "\uFEFF" + csv, "utf8");

const countsByTier = Object.entries(
  usageRows.reduce((acc, row) => {
    acc[row["Library tier"]] = (acc[row["Library tier"]] ?? 0) + 1;
    return acc;
  }, {}),
).sort(([a], [b]) => a.localeCompare(b, "en"));
const zeroScr = usageRows.filter((row) => row["Surface count"] === 0);
const zeroFlow = usageRows.filter((row) => row["FLOW count"] === 0);

const markdown = `# Stage 5.2 — Component Usage Map 0.1

**Date:** 2026-07-28  
**Status:** behavioral and traceability contract complete; visual construction awaits \`VIS-001\`  
**Coverage:** ${usageRows.length}/45 component families

## Summary

| Check | Result |
|---|---:|
| Component families | ${usageRows.length} |
| Families with SCR usage | ${usageRows.length - zeroScr.length} |
| Families without SCR usage | ${zeroScr.length} |
| Families with FLOW usage | ${usageRows.length - zeroFlow.length} |
| Families without direct FLOW usage | ${zeroFlow.length} |

## Library tiers

| Tier | Families |
|---|---:|
${countsByTier.map(([tier, count]) => `| ${tier} | ${count} |`).join("\n")}

## Contract

Each row in \`Component_Usage_Map_0.1.csv\` records the owning library tier, SCR and FLOW consumers, required variants, applicable canonical states, NFR ownership, accessibility contract and visual dependency.

Direct FLOW usage is evidence from the published Flow Design Inventory. A blank FLOW list does not remove the component from scope when normative SCR surfaces require it.

## Boundary

This artifact completes the pre-visual usage map. Final visual variants, dimensions, token values, component frames, accessibility screenshots and development measurements remain blocked by \`VIS-001\` and subsequent Gate 5.2 work.
`;
await fs.writeFile(outputMdPath, markdown, "utf8");

if (usageRows.length !== 45) throw new Error(`Expected 45 component families, got ${usageRows.length}`);
if (zeroScr.length !== 0) throw new Error(`Components without SCR usage: ${zeroScr.map((row) => row.Component).join(", ")}`);

console.log(JSON.stringify({
  componentFamilies: usageRows.length,
  familiesWithScr: usageRows.length - zeroScr.length,
  familiesWithFlow: usageRows.length - zeroFlow.length,
  outputCsvPath,
  outputMdPath,
}, null, 2));
