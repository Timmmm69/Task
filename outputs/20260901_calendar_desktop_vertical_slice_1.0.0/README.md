# Calendar Desktop vertical slice 1.0.0

Production WPF-клиент теперь показывает недельное расписание из Task + CalendarEvent и позволяет создавать и редактировать события через защищённый HTTPS API.

## Что вошло

- typed Desktop client для schedule, event details, conflicts, create и update;
- единый session refresh/retry pipeline без второго token manager;
- недельный экран понедельник–воскресенье, all-day/timed/point presentation, server conflict indicators и inspector;
- форма создания/редактирования с локальным временем, UTC conversion, idempotency и optimistic concurrency;
- capability/session fail-closed, cancellation, stale-response suppression и очистка защищённых данных;
- WPF accessibility metadata и keyboard-accessible standard controls.

## Честная граница

Завершён production calendar read/write vertical slice для недельного экрана. Day/month modes, recurrence editor и attendee management не входят в этот пакет и остаются следующими продуктовыми инкрементами; поэтому dashboard-пункт `PROD-03` не объявлен завершённым на 100%.

Проверки и фактические результаты приведены в `VALIDATION_REPORT.md`, `E2E_REPORT.md` и `VISUAL_ACCESSIBILITY_REPORT.md`.
