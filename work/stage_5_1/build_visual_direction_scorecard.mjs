import fs from "node:fs/promises";
import path from "node:path";

const root = path.resolve(".");
const outputCsvPath = path.join(root, "work/stage_5_1/Visual_Direction_Decision_Scorecard_0.1.csv");
const outputMdPath = path.join(root, "work/stage_5_1/Visual_Direction_Decision_Scorecard_0.1.md");

const criteria = [
  {
    id: "VIS-C01",
    criterion: "Daily task management fit",
    weight: 0.20,
    d1: 5,
    d1Evidence: "Today-first list, overdue/today/tomorrow grouping, persistent inspector and clear create action",
    d2: 5,
    d2Evidence: "Strong day-planning timeline plus unscheduled/overdue list and task details",
    d3: 4,
    d3Evidence: "Fast queue triage and batch-oriented work; less explicit time-planning context",
    basis: "Today, Tasks, Inbox and Calendar are primary daily-work surfaces",
  },
  {
    id: "VIS-C02",
    criterion: "Desktop density and scanability",
    weight: 0.15,
    d1: 4,
    d1Evidence: "Balanced table density with stable columns and a readable details rail",
    d2: 3,
    d2Evidence: "Timeline and two stacked right panels consume space; more competing reading axes",
    d3: 5,
    d3Evidence: "Highest information density, compact queue and strong keyboard-oriented scanning",
    basis: "Windows desktop organizer with large lists, Russian text and 100–200% scaling",
  },
  {
    id: "VIS-C03",
    criterion: "Reuse across 128 SCR surfaces",
    weight: 0.15,
    d1: 5,
    d1Evidence: "Generic list-detail shell maps cleanly to Tasks, Projects, CRM, Files, Settings and Admin",
    d2: 3,
    d2Evidence: "Timeline-first composition is excellent for Today/Calendar but less natural for many admin/editor surfaces",
    d3: 4,
    d3Evidence: "Dense workbench pattern scales to lists and operations, but requires alternate templates for editors/settings",
    basis: "128 unique surfaces and 45 shared component families",
  },
  {
    id: "VIS-C04",
    criterion: "Vertical-slice coverage",
    weight: 0.15,
    d1: 5,
    d1Evidence: "List-detail shell supports Auth/Shell, task create/edit, search, read-only and conflict variants",
    d2: 4,
    d2Evidence: "Covers Today and task details strongly; Auth/Search/Settings need secondary templates",
    d3: 4,
    d3Evidence: "Supports search, queues and conflict-heavy work well; richer create/edit flows need expanded panels/dialogs",
    basis: "10 P0 FLOW contracts across Auth, Task, Search and Resilience",
  },
  {
    id: "VIS-C05",
    criterion: "Accessibility and scaling risk",
    weight: 0.15,
    d1: 5,
    d1Evidence: "Clear focus target, moderate density, stable regions and strong text-plus-icon status semantics",
    d2: 4,
    d2Evidence: "Clear hierarchy, but timeline/current-time line and split content create more navigation/announcement complexity",
    d3: 3,
    d3Evidence: "Dense rows, toolbar states and compressed detail area increase 200% scaling and focus-order risk",
    basis: "NFR-002/003/004/005 and Accessibility Baseline 0.1",
  },
  {
    id: "VIS-C06",
    criterion: "WPF/Windows implementation feasibility",
    weight: 0.10,
    d1: 5,
    d1Evidence: "Conventional navigation, virtualized grid/list and inspector are straightforward WPF patterns",
    d2: 4,
    d2Evidence: "Timeline virtualization, synchronized scrolling and split panes add implementation complexity",
    d3: 5,
    d3Evidence: "DataGrid-like queue and command surfaces align well with mature WPF controls",
    basis: "Windows/WPF architecture and large-list virtualization requirements",
  },
  {
    id: "VIS-C07",
    criterion: "Resilience and read-only visibility",
    weight: 0.10,
    d1: 5,
    d1Evidence: "Persistent connection/read-only footer and stable inspector make system mode continuously visible",
    d2: 4,
    d2Evidence: "Top connection status and bottom read-only bar are clear, though distributed across two regions",
    d3: 4,
    d3Evidence: "Persistent status bar is strong; dense command area needs careful disabled-reason presentation",
    basis: "Server-authoritative online writes, read-only cache and published resilience states",
  },
];

function csvEscape(value) {
  const text = String(value ?? "");
  return `"${text.replaceAll('"', '""')}"`;
}

const headers = [
  "Criterion ID","Criterion","Weight","Direction 1 score","Direction 1 evidence",
  "Direction 2 score","Direction 2 evidence","Direction 3 score","Direction 3 evidence","Canonical basis",
];
const rows = criteria.map((row) => ({
  "Criterion ID": row.id,
  "Criterion": row.criterion,
  "Weight": row.weight,
  "Direction 1 score": row.d1,
  "Direction 1 evidence": row.d1Evidence,
  "Direction 2 score": row.d2,
  "Direction 2 evidence": row.d2Evidence,
  "Direction 3 score": row.d3,
  "Direction 3 evidence": row.d3Evidence,
  "Canonical basis": row.basis,
}));
const csv = "\uFEFF" + [
  headers.map(csvEscape).join(","),
  ...rows.map((row) => headers.map((header) => csvEscape(row[header])).join(",")),
].join("\r\n") + "\r\n";
await fs.writeFile(outputCsvPath, csv, "utf8");

const weighted = (key) => criteria.reduce((sum, row) => sum + row.weight * row[key], 0) / 5;
const scores = {
  "Direction 1": weighted("d1"),
  "Direction 2": weighted("d2"),
  "Direction 3": weighted("d3"),
};
const recommendation = Object.entries(scores).sort((a, b) => b[1] - a[1])[0][0];
const weightSum = criteria.reduce((sum, row) => sum + row.weight, 0);
if (Math.abs(weightSum - 1) > 1e-9) throw new Error(`Weights must sum to 1; actual ${weightSum}`);
if (criteria.some((row) => [row.d1, row.d2, row.d3].some((score) => score < 1 || score > 5))) {
  throw new Error("Direction scores must be between 1 and 5");
}

const markdown = `# Stage 5.1 — Visual Direction Decision Scorecard 0.1

**Date:** 2026-07-28  
**Decision:** \`VIS-001\`  
**Authority:** Product owner  
**Status:** recommendation ready; selection remains open

## Weighted result

| Direction | Score |
|---|---:|
| Direction 1 — Calm list/detail | ${(scores["Direction 1"] * 100).toFixed(0)}% |
| Direction 2 — Timeline planner | ${(scores["Direction 2"] * 100).toFixed(0)}% |
| Direction 3 — Dense workbench | ${(scores["Direction 3"] * 100).toFixed(0)}% |

**Recommendation:** ${recommendation}.

Direction 1 is the strongest default for the full product because its list-detail shell has the lowest cross-surface and accessibility risk while preserving daily-work clarity. Direction 2 remains strongest for schedule-centric planning; Direction 3 remains strongest for high-density keyboard triage.

This recommendation does not close \`VIS-001\`. The Product owner must select Direction 1, 2 or 3 before visual foundations and component construction begin.
`;
await fs.writeFile(outputMdPath, markdown, "utf8");

console.log(JSON.stringify({
  criteria: criteria.length,
  weightSum,
  scores,
  recommendation,
  outputCsvPath,
  outputMdPath,
}, null, 2));
