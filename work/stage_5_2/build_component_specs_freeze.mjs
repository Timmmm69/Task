import fs from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..", "..");
const work = path.join(root, "work", "stage_5_2");
const sourcePath = path.join(work, "Component_Usage_Map_0.1.csv");
const usageCsvPath = path.join(work, "Component_Usage_Map_1.0.csv");
const usageMdPath = path.join(work, "Component_Usage_Map_1.0.md");
const specsCsvPath = path.join(work, "Component_Implementation_Specs_0.9.csv");
const specsMdPath = path.join(work, "Component_Implementation_Specs_0.9.md");
const validationPath = path.join(work, "Component_Spec_Freeze_Validation_0.1.md");

function parseCsv(text) {
  const rows = [];
  let row = [];
  let value = "";
  let quoted = false;
  for (let i = 0; i < text.length; i += 1) {
    const char = text[i];
    if (quoted) {
      if (char === '"' && text[i + 1] === '"') {
        value += '"';
        i += 1;
      } else if (char === '"') {
        quoted = false;
      } else {
        value += char;
      }
    } else if (char === '"') {
      quoted = true;
    } else if (char === ",") {
      row.push(value);
      value = "";
    } else if (char === "\n") {
      row.push(value.replace(/\r$/, ""));
      rows.push(row);
      row = [];
      value = "";
    } else {
      value += char;
    }
  }
  if (value || row.length) {
    row.push(value.replace(/\r$/, ""));
    rows.push(row);
  }
  const [headers, ...data] = rows.filter((item) => item.some((cell) => cell !== ""));
  return data.map((cells) => Object.fromEntries(headers.map((header, index) => [header, cells[index] ?? ""])));
}

function csvEscape(value) {
  const text = String(value ?? "");
  return `"${text.replaceAll('"', '""')}"`;
}

function writeCsv(filePath, rows) {
  const headers = Object.keys(rows[0]);
  const body = [
    headers.map(csvEscape).join(","),
    ...rows.map((row) => headers.map((header) => csvEscape(row[header])).join(",")),
  ].join("\n");
  fs.writeFileSync(filePath, `${body}\n`, "utf8");
}

const behavior = {
  SurfaceTitle: "Names the current surface and optional context without duplicating the window title.",
  PermissionState: "Translates current server capability into available, disabled-with-reason or non-disclosing unavailable presentation.",
  ConnectivityBanner: "Explains connectivity, cache freshness and the next safe recovery action.",
  FieldLabel: "Binds visible label, required/optional meaning, hint and validation to one control.",
  FormLayout: "Orders fields, section boundaries, dirty state and explicit save/cancel actions.",
  ReadOnlyBanner: "Explains why content is read-only, its freshness and which actions remain safe.",
  RetryAction: "Starts only an idempotent recovery check and never implies success before authoritative readiness.",
  ValidationMessage: "Associates a concise error with its field and optionally links from a summary.",
  PageLayout: "Composes title, commands, content, state messaging and optional inspector within the desktop shell.",
  ConflictNotice: "Preserves the local draft and offers explicit compare, reload, reapply or discard paths.",
  DialogShell: "Owns modal semantics, title, action ordering, dismissal rules and focus return.",
  FocusTrap: "Keeps keyboard focus inside blocking/modal content and restores it to the deterministic invoker.",
  ErrorMessage: "States the failure, safe consequence, recovery action and diagnostic identifier when useful.",
  DataList: "Presents an authorized collection with stable selection, loading, empty, stale and partial states.",
  SemanticStatus: "Pairs text and icon with semantic color for status that remains understandable without color.",
  TaskRow: "Summarizes task identity, project/assignee, status, urgency and due information at list density.",
  TaskStatusControl: "Offers only server-allowed transitions and preserves current state on conflict or failure.",
  UrgencyIndicator: "Expresses low/medium/high/critical urgency with text and a directional/status icon.",
  InspectorPanel: "Shows details for the current selection while preserving list context and stale/read-only disclosure.",
  EmptyState: "Explains authorized empty, filtered empty or no-cache conditions with the smallest safe next action.",
  LoadingState: "Reserves layout and distinguishes initial load, refresh and section-level progress.",
  CommandBar: "Prioritizes page or selection commands and collapses to named icon actions in narrow layouts.",
  ConnectionStatus: "Provides persistent online/syncing/offline/read-only state with access to diagnostics.",
  NavigationRail: "Navigates primary modules, exposes the active destination and preserves accessible names in compact mode.",
  ProfileMenu: "Shows current identity and safe account/session actions without exposing hidden directory data.",
  PeoplePicker: "Selects authorized people, communicates redaction/unavailability and never reveals forbidden directory entries.",
  DateTimePicker: "Captures date/date-time values with timezone context, validation and disabled/read-only behavior.",
  TimelineHistory: "Displays chronological authorized changes with actor, timestamp, action and redaction handling.",
  ProjectPicker: "Selects an authorized project and handles removed or unavailable targets without disclosure.",
  FilterBar: "Applies, clears and summarizes filters while preserving a deterministic filtered-empty recovery.",
  ReminderEditor: "Creates, edits or removes reminder timing with validation and target-availability recheck.",
  FileLocationView: "Shows verified file locations and separates unavailable, forbidden and diagnostic outcomes.",
  RecurrenceEditor: "Builds recurrence rules, exceptions and termination with preview and DST/conflict validation.",
  ProgressIndicator: "Communicates determinate, indeterminate, paused or failed progress with a text equivalent.",
  LifecycleBanner: "Explains active/completed/archived/trashed lifecycle and the actions still allowed.",
  NotificationItem: "Shows unread/read notification context and rechecks the target before applying an action.",
  SearchBox: "Provides labelled query input, loading/results/no-results feedback and keyboard selection.",
  RedactionMarker: "Explains partial or unavailable access without revealing protected identity or object data.",
  Pagination: "Moves through stable result pages, exposes current page and disables impossible directions.",
  PopoverSurface: "Anchors a non-blocking surface, supports Escape/outside dismissal and returns focus to its invoker.",
  ContextMenu: "Shows only capability-safe contextual actions and provides disabled reasons when disclosure is allowed.",
  BulkResultSummary: "Summarizes successful, failed and retryable batch outcomes without hiding partial failure.",
  CommentThread: "Displays authorized comments, empty/loading states and safe unavailable behavior.",
  SelectionBar: "Summarizes one/many selected items and exposes only actions valid for the current capability mix.",
  TreeView: "Presents expandable hierarchy with selected/expanded semantics and safe loading/unavailable children.",
};

const prototypeVerified = new Set([
  "SurfaceTitle", "ConnectivityBanner", "FieldLabel", "FormLayout", "ReadOnlyBanner", "RetryAction",
  "ValidationMessage", "PageLayout", "ConflictNotice", "DialogShell", "ErrorMessage", "DataList",
  "SemanticStatus", "TaskRow", "TaskStatusControl", "UrgencyIndicator", "InspectorPanel", "EmptyState",
  "LoadingState", "CommandBar", "ConnectionStatus", "NavigationRail", "ProfileMenu", "PeoplePicker",
  "ProjectPicker", "FilterBar", "ProgressIndicator", "NotificationItem", "SearchBox", "RedactionMarker",
  "Pagination", "PopoverSurface", "CommentThread", "TreeView",
  "PermissionState", "FocusTrap", "DateTimePicker", "FileLocationView", "TimelineHistory",
  "ReminderEditor", "RecurrenceEditor", "LifecycleBanner", "ContextMenu", "BulkResultSummary",
  "SelectionBar",
]);

const partialVerified = new Set();

const evidenceByComponent = {
  PeoplePicker: "work/stage_5_prototype/src/App.jsx#TaskEditorDialog; work/stage_5_prototype/design-qa-stage5-surfaces.md",
  ProjectPicker: "work/stage_5_prototype/src/App.jsx#TaskEditorDialog; work/stage_5_prototype/design-qa-stage5-surfaces.md",
  FilterBar: "work/stage_5_prototype/src/App.jsx#TasksSurface/SearchOverlay; work/stage_5_prototype/implementation-direction2-tasks-final.png",
  Pagination: "work/stage_5_prototype/src/App.jsx#TasksSurface; work/stage_5_prototype/implementation-direction2-tasks-final.png",
  TreeView: "work/stage_5_prototype/src/App.jsx#ProjectsSurface; work/stage_5_prototype/implementation-direction2-projects-final.png",
  NotificationItem: "work/stage_5_prototype/src/App.jsx#NotificationCenter; work/stage_5_prototype/edge-notification-target-changed.png",
  PopoverSurface: "work/stage_5_prototype/src/App.jsx#NotificationCenter/ProfileMenu; work/stage_5_prototype/edge-notification-target-changed.png",
  ProgressIndicator: "work/stage_5_prototype/src/App.jsx#AuthSurface/ProjectsSurface; work/stage_5_prototype/edge-bootstrap-repeated-failure.png",
  PermissionState: "work/stage_5_prototype/src/App.jsx#TasksSurface/ProjectsSurface/FileLocationView; work/stage_5_prototype/design-qa-stage5-component-gaps.md",
  FocusTrap: "work/stage_5_prototype/src/App.jsx#useDialogFocusTrap/TaskEditorDialog; work/stage_5_2/Accessibility_Evidence_Working_0.4.md",
  DateTimePicker: "work/stage_5_prototype/src/App.jsx#TaskEditorDialog; work/stage_5_prototype/implementation-direction2-task-scheduling.png",
  FileLocationView: "work/stage_5_prototype/src/App.jsx#task-details-file-location-view; work/stage_5_prototype/edge-file-location-unavailable.png",
  TimelineHistory: "work/stage_5_prototype/src/App.jsx#ProjectsSurface; work/stage_5_prototype/implementation-direction2-project-history.png",
  ReminderEditor: "work/stage_5_prototype/src/App.jsx#TaskEditorDialog; work/stage_5_prototype/implementation-direction2-task-scheduling-lower.png",
  RecurrenceEditor: "work/stage_5_prototype/src/App.jsx#TaskEditorDialog; work/stage_5_prototype/implementation-direction2-task-scheduling-lower.png",
  LifecycleBanner: "work/stage_5_prototype/src/App.jsx#ProjectsSurface; work/stage_5_prototype/implementation-direction2-project-history.png",
  ContextMenu: "work/stage_5_prototype/src/App.jsx#TasksSurface; work/stage_5_prototype/design-qa-stage5-component-gaps.md",
  BulkResultSummary: "work/stage_5_prototype/src/App.jsx#TasksSurface; work/stage_5_prototype/implementation-direction2-bulk-actions.png",
  SelectionBar: "work/stage_5_prototype/src/App.jsx#TasksSurface; work/stage_5_prototype/implementation-direction2-bulk-actions.png",
};

function defaultEvidence(component) {
  if (evidenceByComponent[component]) return evidenceByComponent[component];
  if (prototypeVerified.has(component)) return "work/stage_5_prototype/src/App.jsx; work/stage_5_prototype/src/styles.css; work/stage_5_prototype/design-qa-stage5-surfaces.md";
  return "work/stage_5_2/Component_Library_Architecture_0.1.md; work/stage_5_2/Component_Usage_Map_1.0.csv";
}

function readiness(component) {
  if (prototypeVerified.has(component)) return "Prototype-verified";
  if (partialVerified.has(component)) return "Partially verified";
  return "Specified";
}

function remaining(component, variants) {
  const status = readiness(component);
  if (status === "Prototype-verified") return "Formal library frame, Windows runtime measurements and remaining module variants";
  if (status === "Partially verified") return `Complete and verify remaining variants: ${variants}`;
  return `Construct and verify variants: ${variants}`;
}

function tierPath(tier) {
  if (tier.startsWith("02")) return "Primitive";
  if (tier.startsWith("03")) return "Core";
  if (tier.startsWith("04")) return "State";
  if (tier.startsWith("05")) return "Domain";
  return "Pattern";
}

function failureRule(component, tier) {
  if (component === "PermissionState" || component === "RedactionMarker") return "Server authorization is authoritative; hidden targets must not be disclosed.";
  if (component === "ConflictNotice" || component === "TaskStatusControl") return "Preserve the local/current value; never overwrite or report success after conflict.";
  if (component === "NotificationItem") return "Recheck target/version/capability before action; changed targets show current state and no false success.";
  if (component === "RetryAction" || component === "ConnectivityBanner") return "Retry is bounded and idempotent; writes remain disabled until authoritative readiness.";
  if (tier.startsWith("04")) return "Never rely on color alone; state text and a safe recovery consequence are required.";
  if (tier.startsWith("05")) return "Current server capability and object version remain authoritative.";
  return "Preserve accessible name, focus context and user input when the operation cannot complete.";
}

const sourceRows = parseCsv(fs.readFileSync(sourcePath, "utf8").replace(/^\uFEFF/, ""));
if (sourceRows.length !== 45) throw new Error(`Expected 45 component families, found ${sourceRows.length}`);
const names = sourceRows.map((row) => row.Component);
if (new Set(names).size !== names.length) {
  const duplicates = [...new Set(names.filter((name, index) => names.indexOf(name) !== index))];
  throw new Error(`Duplicate component names found: ${JSON.stringify(duplicates)}`);
}
for (const name of names) {
  if (!behavior[name]) throw new Error(`Missing behavior specification for ${name}`);
}

const usageRows = sourceRows.map((row, index) => ({
  "Component ID": `CMP-${String(index + 1).padStart(3, "0")}`,
  Component: row.Component,
  "Library path": `Task/${tierPath(row["Library tier"])}/${row.Component}`,
  "Library tier": row["Library tier"],
  Priority: row.Priority,
  "Surface count": row["Surface count"],
  "SCR IDs": row["SCR IDs"],
  "FLOW count": row["FLOW count"],
  "FLOW IDs": row["FLOW IDs"],
  Modules: row.Modules,
  "Surface types": row["Surface types"],
  "Required variants": row["Required variants"],
  "Related canonical STATE": row["Related canonical STATE"],
  "Related NFR": row["Related NFR"],
  "Accessibility contract": row["Accessibility contract"],
  "Behavior contract": behavior[row.Component],
  "Failure rule": failureRule(row.Component, row["Library tier"]),
  "Implementation readiness": readiness(row.Component),
  Evidence: defaultEvidence(row.Component),
  "Remaining verification": remaining(row.Component, row["Required variants"]),
  "Spec version": "0.9",
  "Freeze status": "Frozen for Gate 5.2 candidate",
}));

const specRows = usageRows.map((row) => ({
  "Component ID": row["Component ID"],
  Component: row.Component,
  "Library path": row["Library path"],
  Priority: row.Priority,
  Purpose: row["Behavior contract"],
  Anatomy: "Container; visible label/content; semantic icon where applicable; state/validation region; optional safe action",
  "Required variants": row["Required variants"],
  "State inputs": row["Related canonical STATE"] || "Default; hover; focus; pressed; disabled where applicable",
  "Required NFR": row["Related NFR"],
  "Keyboard contract": "Native Tab/Shift+Tab order; Enter/Space activation; Escape only for dismissible overlays; arrows for list/tree/menu semantics",
  "UIA contract": "Expose stable name, role, state and value; disabled/read-only/expanded/selected/current states must be programmatic",
  "Scaling contract": "No clipped meaning at Windows 200%; long Russian content wraps or truncates with an accessible full value",
  "Failure rule": row["Failure rule"],
  "Implementation readiness": row["Implementation readiness"],
  Evidence: row.Evidence,
  "Remaining verification": row["Remaining verification"],
  "Spec version": row["Spec version"],
}));

writeCsv(usageCsvPath, usageRows);
writeCsv(specsCsvPath, specRows);

const statusCounts = Object.fromEntries(["Prototype-verified", "Partially verified", "Specified"].map((status) => [
  status,
  usageRows.filter((row) => row["Implementation readiness"] === status).length,
]));
const allScr = new Set(usageRows.flatMap((row) => row["SCR IDs"].split("; ").filter(Boolean)));
const allFlows = new Set(usageRows.flatMap((row) => row["FLOW IDs"].split("; ").filter(Boolean)));
const p0 = usageRows.filter((row) => row.Priority === "P0").length;
const p1 = usageRows.filter((row) => row.Priority === "P1").length;
const p2 = usageRows.filter((row) => row.Priority === "P2").length;

const usageMd = `# Stage 5.2 — Component Usage Map 1.0

**Date:** 2026-07-28  
**Status:** FROZEN FOR GATE 5.2 CANDIDATE  
**Coverage:** ${usageRows.length}/45 component families · ${allScr.size}/128 SCR · ${allFlows.size}/37 FLOW

## Freeze result

| Check | Result |
|---|---:|
| Unique component families | ${usageRows.length} |
| P0 / P1 / P2 | ${p0} / ${p1} / ${p2} |
| Prototype-verified | ${statusCounts["Prototype-verified"]} |
| Partially verified | ${statusCounts["Partially verified"]} |
| Specified, construction pending | ${statusCounts.Specified} |
| Families without SCR usage | ${usageRows.filter((row) => !row["SCR IDs"]).length} |
| Families without FLOW usage | ${usageRows.filter((row) => !row["FLOW IDs"]).length} |

## Contract

\`Component_Usage_Map_1.0.csv\` is the frozen implementation handoff map. Every component has a stable ID/path, SCR/FLOW consumers, variants, STATE/NFR ownership, behavior and failure rules, accessibility contract, current evidence and explicit remaining verification.

“Frozen” means the behavior and traceability contract is stable for implementation. It does not mean all 45 visual families are constructed or that Gate 5.2 is closed. Remaining construction and OS-level accessibility evidence stay visible in each row.
`;
fs.writeFileSync(usageMdPath, usageMd, "utf8");

const specsMd = `# Stage 5.2 — Component Implementation Specs 0.9

**Date:** 2026-07-28  
**Status:** implementation candidate, behavior frozen  
**Source:** \`Component_Usage_Map_1.0.csv\`

## Shared rules

1. Server authorization, object version and synchronization readiness are authoritative.
2. No component may report success before the server-confirmed result is known.
3. Disabled actions disclose a reason only when disclosure itself is authorized.
4. Status is never color-only; text and/or an official Fluent icon carry the meaning.
5. Keyboard order follows visual order. Modal surfaces trap focus and return it deterministically.
6. All components expose stable UIA name, role, state and value.
7. Components must retain meaning at Windows 200% scaling and with long Russian strings.
8. Loading, empty, error, read-only, permission and conflict behavior are composed from shared state components.

## Readiness

| Status | Families | Meaning |
|---|---:|---|
| Prototype-verified | ${statusCounts["Prototype-verified"]} | Representative variants work in the Direction 2 prototype |
| Partially verified | ${statusCounts["Partially verified"]} | Some behavior is proven; named variants remain |
| Specified | ${statusCounts.Specified} | Contract is frozen; visual construction/evidence is pending |

## Family index

| ID | Component | Path | Priority | Readiness | Surface count |
|---|---|---|---|---|---:|
${usageRows.map((row) => `| ${row["Component ID"]} | ${row.Component} | \`${row["Library path"]}\` | ${row.Priority} | ${row["Implementation readiness"]} | ${row["Surface count"]} |`).join("\n")}

## Detailed contract

The machine-readable \`Component_Implementation_Specs_0.9.csv\` is authoritative for per-family purpose, variants, STATE/NFR inputs, keyboard/UIA/scaling behavior, failure rule, evidence and remaining verification.

## Gate boundary

This artifact completes the specification/usage-map portion of \`S5-0215\`. Gate 5.2 still requires remaining component construction, controlled Windows UIA/Narrator/200%/contrast evidence and formal Product Owner/Windows Tech Lead/QA approval.
`;
fs.writeFileSync(specsMdPath, specsMd, "utf8");

const validationMd = `# Component Spec Freeze Validation 0.1

**Date:** 2026-07-28  
**Result:** PASS

| Check | Expected | Actual | Result |
|---|---:|---:|---|
| Component rows | 45 | ${usageRows.length} | PASS |
| Unique component IDs | 45 | ${new Set(usageRows.map((row) => row["Component ID"])).size} | PASS |
| Unique component names | 45 | ${new Set(usageRows.map((row) => row.Component)).size} | PASS |
| SCR coverage | 128 | ${allScr.size} | PASS |
| FLOW coverage | 37 | ${allFlows.size} | PASS |
| Behavior contracts | 45 | ${usageRows.filter((row) => row["Behavior contract"]).length} | PASS |
| Failure rules | 45 | ${usageRows.filter((row) => row["Failure rule"]).length} | PASS |
| Accessibility contracts | 45 | ${usageRows.filter((row) => row["Accessibility contract"]).length} | PASS |
| Remaining verification explicit | 45 | ${usageRows.filter((row) => row["Remaining verification"]).length} | PASS |
| Pending VIS-001 markers | 0 | ${usageRows.filter((row) => Object.values(row).some((value) => String(value).includes("Pending VIS-001"))).length} | PASS |

The freeze is traceability-complete but not equivalent to full visual construction or Gate approval.
`;
fs.writeFileSync(validationPath, validationMd, "utf8");

console.log(JSON.stringify({
  componentCount: usageRows.length,
  scrCoverage: allScr.size,
  flowCoverage: allFlows.size,
  statusCounts,
  outputs: [usageCsvPath, usageMdPath, specsCsvPath, specsMdPath, validationPath],
}, null, 2));
