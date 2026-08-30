import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { computeExecutionPlan } from "./execution-plan.mjs";

export const dashboardDir = dirname(fileURLToPath(import.meta.url));
export const repoDir = join(dashboardDir, "..");

export function readJson(name) {
  return JSON.parse(readFileSync(join(dashboardDir, name), "utf8"));
}

export function git(args, fallback = null) {
  try {
    return execFileSync("git", args, {
      cwd: repoDir,
      encoding: "utf8",
      windowsHide: true,
      timeout: 2500,
      stdio: ["ignore", "pipe", "ignore"]
    }).trim();
  } catch {
    return fallback;
  }
}

export function getGitTelemetry() {
  const statusLines = (git(["status", "--porcelain"], "") || "").split(/\r?\n/).filter(Boolean);
  const [ahead = "?", behind = "?"] = (git(["rev-list", "--left-right", "--count", "HEAD...origin/main"], "?\t?") || "?\t?").split(/\s+/);
  const log = git(["log", "-8", "--date=iso-strict", "--pretty=format:%h%x1f%ad%x1f%s"], "") || "";
  return {
    branch: git(["branch", "--show-current"], "Не определено"),
    commit: git(["rev-parse", "--short", "HEAD"], "Не определено"),
    last_commit_at: git(["log", "-1", "--date=iso-strict", "--pretty=format:%ad"], null),
    last_message: git(["log", "-1", "--pretty=format:%s"], "Нет данных"),
    dirty: statusLines.length > 0,
    changed_files: statusLines.length,
    ahead: Number.isFinite(Number(ahead)) ? Number(ahead) : null,
    behind: Number.isFinite(Number(behind)) ? Number(behind) : null,
    recent: log.split(/\r?\n/).filter(Boolean).map((line) => {
      const [hash, at, message] = line.split("\x1f");
      return { hash, at, message };
    })
  };
}

function weighted(items) {
  const total = items.reduce((sum, item) => sum + item.weight, 0);
  const completed = items.reduce((sum, item) => sum + item.weight * item.progress, 0);
  return total ? completed / total : 0;
}

export function buildDashboard(roadmap, tests, now = new Date()) {
  const byId = new Map(roadmap.items.map((item) => [item.id, item]));
  const gates = roadmap.release_gates.map((gate) => {
    const required = gate.required_items.map((id) => byId.get(id)).filter(Boolean);
    const blockers = required.filter((item) => item.progress < 100 || item.status !== "done");
    return { ...gate, passed: blockers.length === 0, blockers: blockers.map((item) => ({ id: item.id, title: item.title, progress: item.progress, note: item.note })) };
  });
  const categories = roadmap.categories
    .slice()
    .sort((a, b) => a.order - b.order)
    .map((category) => {
      const items = roadmap.items.filter((item) => item.category === category.id);
      return {
        ...category,
        progress: weighted(items),
        ready: items.filter((item) => item.status === "done").length,
        total: items.length,
        blocked: items.filter((item) => item.status === "blocked").length
      };
    });
  const counts = {
    done: roadmap.items.filter((item) => item.status === "done").length,
    in_progress: roadmap.items.filter((item) => item.status === "in_progress").length,
    blocked: roadmap.items.filter((item) => item.status === "blocked").length,
    unknown: roadmap.items.filter((item) => ["not_started", "unverified"].includes(item.status)).length
  };
  const priorityCandidates = roadmap.items
    .filter((item) => item.progress < 100 && ["blocker", "critical"].includes(item.criticality))
    .sort((a, b) => (a.progress - b.progress) || (b.weight - a.weight) || a.id.localeCompare(b.id));
  const priority = priorityCandidates
    .filter((item, index, items) => items.findIndex((candidate) => candidate.category === item.category) === index)
    .slice(0, 3)
    .map(({ id, title, progress, note, category }) => ({ id, title, progress, note, category }));
  const blockers = roadmap.items
    .filter((item) => item.status === "blocked" || (item.criticality === "blocker" && item.progress < 100))
    .sort((a, b) => (a.progress - b.progress) || (b.weight - a.weight));
  const overall = weighted(roadmap.items);
  const executionPlan = computeExecutionPlan(roadmap.items);
  const categoryNames = new Map(roadmap.categories.map((category) => [category.id, category.name]));
  const serializePlanEntry = (entry) => entry ? ({
    order: entry.order,
    available_now: entry.available_now,
    unresolved: entry.unresolved,
    graph_blocked: entry.graph_blocked,
    unlocks: entry.unlocks,
    item: { ...entry.item, category_name: categoryNames.get(entry.item.category) || entry.item.category }
  }) : null;
  return {
    generated_at: now.toISOString(),
    project: roadmap.project,
    overall,
    remaining_weighted_work: 100 - overall,
    handoff_ready: gates.every((gate) => gate.passed),
    counts,
    categories,
    gates,
    blockers,
    priority,
    execution_plan: {
      now: serializePlanEntry(executionPlan.now),
      after: executionPlan.after.map(serializePlanEntry),
      later: executionPlan.later.map(serializePlanEntry),
      total_remaining: executionPlan.queue.length
    },
    tests,
    git: getGitTelemetry(),
    items: roadmap.items
  };
}
