# Native Windows UIA recheck — unaccepted attempt

Date: 2026-08-09
Client: Task Gate 5.6 Client 0.1.1 portable x64
Executable SHA-256: 8B047DD69E1A64269F8961FE0416727E5083E0C2B30285A73DD2E92A2D412E53
Source commit: 6a16be2fb371d41af0540569c77daf59eb902a9d (PR #3 head; not merged to main).
Windows: 10.0.26200.0; one active 2560x1600 display; interactive session.
Tool: .NET System.Windows.Automation plus native keyboard injection. Inspect.exe was absent after searching C:\Program Files (x86)\Windows Kits\10\bin.

This is an Electron-client attempt, not browser-prototype evidence. It is not accepted EVD-WIN-UIA evidence: Inspect capture, full role/flow coverage, Narrator observation, and QA + Desktop tech lead review are absent.

Observed: native Electron window Task — Сегодня (Chrome_WidgetWin_1) with Chromium RootWebArea; named focusable shell controls; Search redaction copy; and CalendarEvent editor with named title/date/timezone/attendees controls, validation/mutation guards, and a synthetic save result.
Observed focus concern: after transition to sign-in, UIA could not set keyboard focus to Login or Password although they were reported enabled/focusable. This is a manual Inspect/Narrator retest candidate, not a confirmed production defect.

| ID | Result | Basis / limitation |
|---|---|---|
| WIN-A11Y-01 | PARTIAL | Manager shell names and a foreground keyboard route observed; not Employee/full focus return. |
| WIN-A11Y-02 | PARTIAL | Connection/sign-in controls named; authentication and announcement not demonstrated; focus retest required. |
| WIN-A11Y-03 | NOT_RUN | New Task flow not reached after sign-in focus limitation. |
| WIN-A11Y-04 | PARTIAL | Desktop CalendarEvent editor and synthetic save observed; full keyboard/focus/guard coverage unverified. |
| WIN-A11Y-05 | PARTIAL | Manager Search route and permission-safe redaction observed; Observer flow not run. |
| WIN-A11Y-06 | NOT_RUN | Offline read-only not executed. |
| WIN-A11Y-07 | NOT_RUN | Reconnect not executed. |
| WIN-A11Y-08 | NOT_RUN | Conflict/draft restoration not executed. |
| WIN-A11Y-09 | NOT_RUN | Observer restriction not executed. |
| WIN-A11Y-10 | NOT_RUN | Admin restore guard not executed. |
| WIN-A11Y-11 | PARTIAL | Archive/Trash controls present; Admin destructive flow not run. |
| WIN-A11Y-12 | PARTIAL | Tabs/comboboxes/state-bearing controls observed; menu/table/tree/progress coverage incomplete. |

Narrator.exe is installed but this session has no auditable speech-output capture or listener, so no Narrator smoke result is claimed. DPI/multi-monitor, moderated sessions, finding disposition, and owner approvals were unavailable.

Decision: all nine evidence rows remain PENDING. Do not sign Gate 5.6 or change any row to ACCEPTED based on this report.
