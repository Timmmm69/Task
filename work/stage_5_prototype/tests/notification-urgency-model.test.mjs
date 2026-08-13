import assert from "node:assert/strict";
import test from "node:test";
import {
  DEFAULT_URGENCY_THRESHOLDS,
  urgencyForMinutes,
  urgencyTierForNotification,
} from "../src/notificationUrgencyModel.js";

test("urgencyForMinutes maps elapsed time to overdue", () => {
  assert.equal(urgencyForMinutes(-1).key, "overdue");
  assert.equal(urgencyForMinutes(-480).key, "overdue");
});

test("urgencyForMinutes maps time before deadline using default thresholds", () => {
  assert.equal(urgencyForMinutes(0).key, "critical");
  assert.equal(urgencyForMinutes(59).key, "critical");
  assert.equal(urgencyForMinutes(60).key, "soon");
  assert.equal(urgencyForMinutes(359).key, "soon");
  assert.equal(urgencyForMinutes(360).key, "hours");
  assert.equal(urgencyForMinutes(1439).key, "hours");
  assert.equal(urgencyForMinutes(1440).key, "far");
  assert.equal(urgencyForMinutes(4320).key, "far");
});

test("urgencyForMinutes respects custom thresholds", () => {
  const thresholds = { criticalMinutes: 30, soonMinutes: 120, hoursMinutes: 720 };
  assert.equal(urgencyForMinutes(15, thresholds).key, "critical");
  assert.equal(urgencyForMinutes(45, thresholds).key, "soon");
  assert.equal(urgencyForMinutes(300, thresholds).key, "hours");
  assert.equal(urgencyForMinutes(800, thresholds).key, "far");
});

test("urgencyTierForNotification returns null for neutral notifications", () => {
  assert.equal(urgencyTierForNotification({ id: "n1", kind: "assignment" }), null);
  assert.equal(urgencyTierForNotification({ id: "n2", kind: "project" }), null);
  assert.equal(urgencyTierForNotification(null), null);
});

test("urgencyTierForNotification computes tier from deadlineMinutesFromNow at render", () => {
  assert.equal(urgencyTierForNotification({ id: "n1", deadlineMinutesFromNow: 37 }).key, "critical");
  assert.equal(urgencyTierForNotification({ id: "n2", deadlineMinutesFromNow: 180 }).key, "soon");
  assert.equal(urgencyTierForNotification({ id: "n3", deadlineMinutesFromNow: 600 }).key, "hours");
  assert.equal(urgencyTierForNotification({ id: "n4", deadlineMinutesFromNow: -90 }).key, "overdue");
});

test("default thresholds keep strict ascending order", () => {
  const { criticalMinutes, soonMinutes, hoursMinutes } = DEFAULT_URGENCY_THRESHOLDS;
  assert.ok(criticalMinutes < soonMinutes);
  assert.ok(soonMinutes < hoursMinutes);
});
