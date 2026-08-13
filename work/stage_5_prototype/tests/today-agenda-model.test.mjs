import assert from "node:assert/strict";
import test from "node:test";
import {
  sortScheduledTasks,
  sortUntimedTasks,
  deriveAgendaSections,
  computeDayProgress,
} from "../src/todayAgendaModel.js";

test("scheduled tasks sort by start time, priority, then original order", () => {
  const tasks = [
    { id: "late", time: "14:00", priorityTone: "low" },
    { id: "same-low", time: "10:00–11:00", priorityTone: "low" },
    { id: "same-high", time: "10:00–10:30", priorityTone: "high" },
    { id: "same-high-second", time: "10:00", priorityTone: "high" },
  ];
  assert.deepEqual(
    sortScheduledTasks(tasks).map((task) => task.id),
    ["same-high", "same-high-second", "same-low", "late"],
  );
});

test("untimed tasks sort by priority without changing equal-priority order", () => {
  const tasks = [
    { id: "low", priorityTone: "low" },
    { id: "medium-first", priorityTone: "medium" },
    { id: "high", priorityTone: "high" },
    { id: "medium-second", priorityTone: "medium" },
  ];
  assert.deepEqual(
    sortUntimedTasks(tasks).map((task) => task.id),
    ["high", "medium-first", "medium-second", "low"],
  );
});

test("deriveAgendaSections splits open tasks and keeps completed separately", () => {
  const items = [
    { id: "done-untimed", completed: true, priorityTone: "high" },
    { id: "open-timed", time: "12:00", priorityTone: "medium" },
    { id: "done-timed", time: "09:00", completed: true, priorityTone: "low" },
    { id: "open-untimed", priorityTone: "high" },
    { id: "open-timed-early", time: "08:00", priorityTone: "low" },
  ];
  const sections = deriveAgendaSections(items);
  assert.deepEqual(sections.scheduled.map((task) => task.id), ["open-timed-early", "open-timed"]);
  assert.deepEqual(sections.untimed.map((task) => task.id), ["open-untimed"]);
  assert.deepEqual(sections.completed.map((task) => task.id), ["done-untimed", "done-timed"]);
});

test("computeDayProgress reports empty day as neutral", () => {
  const progress = computeDayProgress([]);
  assert.deepEqual(progress, { total: 0, completed: 0, percent: 0, isEmpty: true, isDone: false });
});

test("computeDayProgress reports partial progress with rounded percent", () => {
  const items = [
    { id: "a", completed: true },
    { id: "b", completed: true },
    { id: "c", completed: true },
    { id: "d" },
    { id: "e" },
    { id: "f" },
    { id: "g" },
    { id: "h" },
  ];
  const progress = computeDayProgress(items);
  assert.equal(progress.total, 8);
  assert.equal(progress.completed, 3);
  assert.equal(progress.percent, 38);
  assert.equal(progress.isEmpty, false);
  assert.equal(progress.isDone, false);
});

test("computeDayProgress reports 100% when all tasks are done", () => {
  const items = [
    { id: "a", completed: true },
    { id: "b", completed: true },
  ];
  const progress = computeDayProgress(items);
  assert.equal(progress.percent, 100);
  assert.equal(progress.isDone, true);
});
