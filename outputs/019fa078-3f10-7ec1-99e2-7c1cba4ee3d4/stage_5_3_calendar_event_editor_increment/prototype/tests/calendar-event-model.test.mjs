import test from "node:test";
import assert from "node:assert/strict";
import {
  applyCalendarResponse,
  canCommitCalendarEvent,
  createCalendarEventDraft,
  hasCalendarOverlap,
  replaceCalendarAttendees,
  validateCalendarEventDraft,
} from "../src/calendarEventModel.js";

test("canonical CalendarEvent draft contains required contract fields", () => {
  const draft = createCalendarEventDraft();
  assert.deepEqual(Object.keys(draft).filter((key) => ["title", "eventDate", "timeZone", "isAllDay", "project", "start", "duration", "userAttendees", "contactAttendees"].includes(key)).sort(), ["contactAttendees", "duration", "eventDate", "isAllDay", "project", "start", "timeZone", "title", "userAttendees"]);
});

test("validation returns canonical field paths and keeps the supplied draft immutable", () => {
  const draft = createCalendarEventDraft({ title: "", eventDate: "", project: "", duration: 0 });
  const before = structuredClone(draft);
  assert.deepEqual(validateCalendarEventDraft(draft), {
    title: "Укажите название события",
    eventDate: "Укажите дату события",
    projectId: "Укажите проект",
    endAtUtc: "Окончание должно быть позже начала",
  });
  assert.deepEqual(draft, before);
});

test("offline and blocking server states never allow a calendar mutation", () => {
  for (const state of ["forbidden", "deleted", "session", "conflict"]) {
    assert.equal(canCommitCalendarEvent({ isWritable: true, capability: true, state }), false);
  }
  assert.equal(canCommitCalendarEvent({ isWritable: false, capability: true, state: null }), false);
  assert.equal(canCommitCalendarEvent({ isWritable: true, capability: false, state: null }), false);
  assert.equal(canCommitCalendarEvent({ isWritable: true, capability: true, state: null }), true);
});

test("attendee replacement deduplicates users and contacts without mixing scopes", () => {
  const result = replaceCalendarAttendees(createCalendarEventDraft(), { users: ["Иван С.", "Иван С."], contacts: ["ООО «Вектор»", "ООО «Вектор»"] });
  assert.deepEqual(result.userAttendees, ["Иван С."]);
  assert.deepEqual(result.contactAttendees, ["ООО «Вектор»"]);
});

test("attendee response accepts only canonical response statuses", () => {
  const draft = createCalendarEventDraft();
  assert.equal(applyCalendarResponse(draft, "accepted").response, "accepted");
  assert.equal(applyCalendarResponse(draft, "unknown"), draft);
});

test("overlap guard ignores the edited event and all-day events", () => {
  const items = [{ id: "one", start: 600, duration: 60 }];
  assert.equal(hasCalendarOverlap({ id: "two", start: 630, duration: 30, isAllDay: false }, items), true);
  assert.equal(hasCalendarOverlap({ id: "one", start: 630, duration: 30, isAllDay: false }, items), false);
  assert.equal(hasCalendarOverlap({ id: "two", start: 630, duration: 30, isAllDay: true }, items), false);
});
