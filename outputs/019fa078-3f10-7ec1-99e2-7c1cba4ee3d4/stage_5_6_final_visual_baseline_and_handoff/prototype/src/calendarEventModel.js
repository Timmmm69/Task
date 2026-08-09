export const CALENDAR_BLOCKING_STATES = new Set(["forbidden", "deleted", "session", "conflict"]);

/**
 * @typedef {object} CalendarEventDraft
 * @property {string|null} id
 * @property {string} title
 * @property {string} eventDate
 * @property {string} timeZone
 * @property {boolean} isAllDay
 * @property {string} project
 * @property {string} assignee
 * @property {string} status
 * @property {number} start
 * @property {number} duration
 * @property {string} description
 * @property {string[]} userAttendees
 * @property {string[]} contactAttendees
 * @property {string} response
 * @property {number|null} version
 * @property {string|null} state
 */

/** @param {Partial<CalendarEventDraft> & Record<string, unknown>} [overrides] @returns {CalendarEventDraft & Record<string, unknown>} */
export function createCalendarEventDraft(overrides = {}) {
  return {
    id: null,
    title: "",
    eventDate: "2026-07-28",
    timeZone: "Europe/Minsk",
    isAllDay: false,
    project: "Отчётность",
    assignee: "Иван С.",
    status: "Запланировано",
    start: 600,
    duration: 30,
    description: "",
    userAttendees: ["Иван С."],
    contactAttendees: [],
    response: "pending",
    version: null,
    state: null,
    ...overrides,
  };
}

/** @param {CalendarEventDraft} draft @returns {Record<string, string>} */
export function validateCalendarEventDraft(draft) {
  /** @type {Record<string, string>} */
  const fieldErrors = {};
  if (!draft.title?.trim()) Object.assign(fieldErrors, { title: "Укажите название события" });
  if (!draft.eventDate) Object.assign(fieldErrors, { eventDate: "Укажите дату события" });
  if (!draft.timeZone) Object.assign(fieldErrors, { timeZone: "Укажите часовой пояс" });
  if (!draft.project) Object.assign(fieldErrors, { projectId: "Укажите проект" });
  if (!draft.isAllDay && (!Number.isInteger(draft.start) || draft.start < 0 || draft.start >= 1440)) Object.assign(fieldErrors, { startAtUtc: "Укажите корректное время начала" });
  if (!draft.isAllDay && (!Number.isInteger(draft.duration) || draft.duration < 15)) Object.assign(fieldErrors, { endAtUtc: "Окончание должно быть позже начала" });
  return fieldErrors;
}

/** @param {{isWritable: boolean, state: string|null, capability: boolean}} options */
export function canCommitCalendarEvent({ isWritable, state, capability }) {
  return Boolean(isWritable && capability && !CALENDAR_BLOCKING_STATES.has(state));
}

/** @param {CalendarEventDraft} draft @param {{users: string[], contacts: string[]}} attendees */
export function replaceCalendarAttendees(draft, { users, contacts }) {
  return { ...draft, userAttendees: [...new Set(users)], contactAttendees: [...new Set(contacts)] };
}

/** @param {CalendarEventDraft} draft @param {string} response */
export function applyCalendarResponse(draft, response) {
  if (!["accepted", "tentative", "declined"].includes(response)) return draft;
  return { ...draft, response };
}

/** @param {CalendarEventDraft} candidate @param {CalendarEventDraft[]} items */
export function hasCalendarOverlap(candidate, items) {
  if (candidate.isAllDay) return false;
  return items.some((item) => item.id !== candidate.id && candidate.start < item.start + item.duration && candidate.start + candidate.duration > item.start);
}
