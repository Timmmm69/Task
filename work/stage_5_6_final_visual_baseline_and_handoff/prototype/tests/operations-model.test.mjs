import assert from "node:assert/strict";
import test from "node:test";

import {
  canEnterMaintenance,
  filterAuthorizedAudit,
  getVisibleOperationSections,
  isOperationsWritable,
  transitionOperation,
} from "../src/operationsModel.js";

const ALL_EVENTS = "\u0412\u0441\u0435 \u0441\u043e\u0431\u044b\u0442\u0438\u044f";

test("limited Operations navigation exposes only health and audit", () => {
  const sections = ["health", "jobs", "backups", "audit", "organization"].map((id) => ({ id }));
  assert.deepEqual(getVisibleOperationSections(sections, true).map((item) => item.id), ["health", "audit"]);
  assert.equal(getVisibleOperationSections(sections, false).length, 5);
});

test("Operations mutations require online, loaded, unblocked non-maintenance state", () => {
  assert.equal(isOperationsWritable({ offline: false, loading: false, writeBlocked: false, maintenance: false }), true);
  assert.equal(isOperationsWritable({ offline: true, loading: false, writeBlocked: false, maintenance: false }), false);
  assert.equal(isOperationsWritable({ offline: false, loading: true, writeBlocked: false, maintenance: false }), false);
  assert.equal(isOperationsWritable({ offline: false, loading: false, writeBlocked: true, maintenance: false }), false);
  assert.equal(isOperationsWritable({ offline: false, loading: false, writeBlocked: false, maintenance: true }), false);
});

test("operation transition changes only the selected server object", () => {
  const jobs = [{ id: "a", state: "failed" }, { id: "b", state: "queued" }];
  assert.deepEqual(transitionOperation(jobs, "a", { state: "running", progress: 12 }), [
    { id: "a", state: "running", progress: 12 },
    { id: "b", state: "queued" },
  ]);
});

test("audit text search never matches a redacted event", () => {
  const rows = [
    { actor: "Ivan", action: "Session.Revoke", target: "Allowed session", type: "Security", authorized: true },
    { actor: "Hidden actor", action: "Secret", target: "Hidden target", type: "Security", authorized: false },
  ];
  assert.equal(filterAuthorizedAudit(rows, { query: "Hidden", type: ALL_EVENTS }).length, 0);
  assert.equal(filterAuthorizedAudit(rows, { query: "Session", type: "Security" }).length, 1);
  assert.equal(filterAuthorizedAudit(rows, { query: "", type: ALL_EVENTS }).length, 2);
});

test("maintenance requires writable state, approval, no active job and exact phrase", () => {
  const valid = { writable: true, approved: true, activeJobExists: false, confirmation: "RESTORE" };
  assert.equal(canEnterMaintenance(valid), true);
  assert.equal(canEnterMaintenance({ ...valid, writable: false }), false);
  assert.equal(canEnterMaintenance({ ...valid, approved: false }), false);
  assert.equal(canEnterMaintenance({ ...valid, activeJobExists: true }), false);
  assert.equal(canEnterMaintenance({ ...valid, confirmation: "restore" }), false);
});
