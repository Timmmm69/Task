# QA-04 — внутренний release sign-off

- Решение: **APPROVED — PASS_WITH_NON_BLOCKING_FINDINGS**.
- Версия evidence package: `1.1.0`.
- Дата: 11 сентября 2026 года.
- Область решения: QA-04, шаги 5–8 поверх готового baseline шагов 1–4.
- Блокирующие findings: 0.
- Неблокирующие findings: 4 (1 Medium, 3 Low), все имеют disposition.

Основание решения: свежий controlled Windows run подтвердил 12/12 critical UI scenarios,
8/8 DPI cases, UIA/keyboard contracts, clean setup/cleanup и сохранность данных после
offline/reconnect. Визуальный внутренний review не выявил потери доступа к критическим действиям.

Принятые ограничения: Narrator не входит в acceptance scope и не заявлен как проверенный;
100/125/200% воспроизведены логическими viewport-эквивалентами на физическом 150% host;
mixed-monitor переход остаётся deployment smoke. Эти ограничения не блокируют внутренний
release sign-off по утверждённому scope QA-04.

Подписант: внутренний QA / Codex. Это машинно воспроизводимый внутренний sign-off, а не
криптографическая подпись и не приёмка заказчиком.
