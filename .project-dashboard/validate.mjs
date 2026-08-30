import { accessSync, constants, readFileSync } from "node:fs";
import { join } from "node:path";
import { buildDashboard, dashboardDir, readJson } from "./lib.mjs";
import { applyRecommendedOrder, computeExecutionPlan } from "./execution-plan.mjs";

const allowedProgress = new Set([0, 25, 50, 75, 100]);
const allowedStatus = new Set(["done", "in_progress", "blocked", "not_started", "unverified"]);
const roadmap = readJson("roadmap.json");
const tests = readJson("test-results.json");
const ids = new Set();
const categoryIds = new Set(roadmap.categories.map((category) => category.id));
const errors = [];

for (const item of roadmap.items) {
  for (const field of ["id", "title", "description", "category", "weight", "criticality", "priority", "dependencies", "blocked_by", "recommended_order", "next_action", "status", "progress", "evidence", "note", "updated_at"]) {
    if (item[field] === undefined || item[field] === null) errors.push(`${item.id || "item"}: missing ${field}`);
  }
  if (ids.has(item.id)) errors.push(`duplicate item id: ${item.id}`);
  ids.add(item.id);
  if (!categoryIds.has(item.category)) errors.push(`${item.id}: unknown category`);
  if (!allowedProgress.has(item.progress)) errors.push(`${item.id}: invalid progress`);
  if (!allowedStatus.has(item.status)) errors.push(`${item.id}: invalid status`);
  if (!(item.weight > 0)) errors.push(`${item.id}: invalid weight`);
  if (!Array.isArray(item.evidence) || item.evidence.length === 0) errors.push(`${item.id}: evidence required`);
  if (!Number.isInteger(item.priority) || item.priority < 1 || item.priority > 4) errors.push(`${item.id}: priority must be 1-4`);
  if (!Array.isArray(item.dependencies) || !Array.isArray(item.blocked_by)) errors.push(`${item.id}: dependencies and blocked_by must be arrays`);
  if (item.status === "done" && item.progress !== 100) errors.push(`${item.id}: done must be 100`);
}
for (const item of roadmap.items) {
  for (const dependency of [...item.dependencies, ...item.blocked_by]) {
    if (!ids.has(dependency)) errors.push(`${item.id}: unknown dependency ${dependency}`);
    if (dependency === item.id) errors.push(`${item.id}: self dependency`);
  }
}
for (const gate of roadmap.release_gates) {
  for (const id of gate.required_items) if (!ids.has(id)) errors.push(`${gate.id}: unknown item ${id}`);
}
if (roadmap.items.length < 30 || roadmap.items.length > 50) errors.push(`roadmap size ${roadmap.items.length}, expected 30-50`);
for (const name of ["public/index.html", "public/styles.css", "public/app.js", "README.md"]) {
  try { accessSync(join(dashboardDir, name), constants.R_OK); } catch { errors.push(`missing ${name}`); }
}
const result = buildDashboard(roadmap, tests, new Date("2026-08-30T13:30:00Z"));
const manual = roadmap.items.reduce((sum, item) => sum + item.weight * item.progress, 0) / roadmap.items.reduce((sum, item) => sum + item.weight, 0);
if (Math.abs(result.overall - manual) > 1e-9) errors.push("overall calculation mismatch");
for (const category of result.categories) {
  if (!Number.isFinite(category.progress)) errors.push(`${category.id}: invalid category progress`);
}
if (result.handoff_ready !== result.gates.every((gate) => gate.passed)) errors.push("handoff gate mismatch");
const planned = applyRecommendedOrder(roadmap.items);
for (const item of roadmap.items) {
  const expected = planned.find((candidate) => candidate.id === item.id)?.recommended_order;
  if (item.recommended_order !== expected) errors.push(`${item.id}: recommended_order is stale; run npm run dashboard:order`);
}
const executionPlan = computeExecutionPlan(roadmap.items);
if (executionPlan.queue.some((entry) => entry.graph_blocked)) errors.push("execution plan contains a dependency cycle or unreachable item");
if (executionPlan.now && !executionPlan.now.available_now) errors.push("next task is blocked");
if (tests.total !== tests.passed + tests.failed + tests.skipped) errors.push("test totals mismatch");
const visible = [readFileSync(join(dashboardDir, "public/index.html"), "utf8"), readFileSync(join(dashboardDir, "public/app.js"), "utf8")].join("\n");
if (visible.includes("Hours Left")) errors.push("false hours estimate present");

if (errors.length) {
  console.error(errors.join("\n"));
  process.exit(1);
}
console.log(JSON.stringify({ valid: true, items: roadmap.items.length, categories: result.categories.length, gates: result.gates.length, overall: Number(result.overall.toFixed(2)), handoff_ready: result.handoff_ready }, null, 2));
