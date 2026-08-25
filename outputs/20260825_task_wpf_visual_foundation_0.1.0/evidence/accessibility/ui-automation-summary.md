# UI Automation and accessibility evidence

## Static UIA trees

- `../before/uia-tree.txt` — production before state.
- `../after/1200x900-uia-tree.txt` — loaded Direction 2 fixture.
- `../after/1487x1058-uia-tree.txt` — wide selected/detail layout.
- `../after/keyboard-enter-detail-uia-tree.txt` — focus after Enter.

## Stable identifiers confirmed

`MainWindow`, `NavigationListBox`, `ConnectionStatusText`, `LogoutButton`,
`SessionMessageText`, `SelectedSectionArea`, `TasksScreen`, `TasksRefreshButton`,
`NewTaskButton`, `ReadOnlyNoticeText`, `TasksStateMessage`, `TasksList`,
`TaskDetailsArea`, `TaskDetailsState` and `TasksLoadMoreButton` remain present.

Navigation rows expose one selectable action with a factual name and HelpText;
decorative icon geometries do not create meaningless speech fragments. Task rows
expose one composite accessible name containing title, status, priority and due
date. Disabled New Task exposes the reason through HelpText.

## Keyboard/UIA run

- Arrow/ListBox selection semantics: PASS.
- Selected row: fill plus border/leading indicator, not color only.
- Visible focus: shared 2 px focus resource, PASS.
- Enter on a task: inspector expanded and UIA focus became:
  `область Карточка выбранной задачи ... ID: TaskDetailsArea`.
- Loading/error status regions use live announcements.
- Refresh and load-more have stable names and AutomationId.
- Real API run exposed 50 rows on the first page, 55 after load-more, then restored
  the first page after refresh.
- Server-side revoke returned the app to `AuthWindow`; auth UIA identifiers were
  unchanged and the login error region announced the terminal session state.

## High Contrast and DPI

High Contrast system brushes and non-transparent foreground fallbacks are covered
by resource tests. The actual OS High Contrast toggle was not changed. All visual
captures and UIA runs used the real current 144 DPI / 150% scale. A separate 200%
isolated display session was unavailable and is explicitly unverified.
