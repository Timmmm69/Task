# Object Links 10.1–10.3 — публикация 10.3.1

Исходный код: `402483b57d8daca5c7c9efbf1c59dd339ff03388`. Пользователь разрешил публикацию в main с открытой ручной приёмкой.

**Полная приёмка НЕ ПРОЙДЕНА. Статус ручной приёмки: OPEN.**

Закрыты прежние ограничения: полный набор related targets, discovery без Search.Use, чтение всех страниц связей.

| Проверка | PASS | FAIL | SKIP |
| --- | --- | --- | --- |
| desktop | 1279 | 0 | 0 |
| api | 626 | 0 | 0 |
| domain_postgres | 863 | 0 | 0 |

Release solution build: PASS, 0 errors, 11 warnings. PostgreSQL 16 использована фактически на изолированном loopback-стенде.

Новый endpoint прошёл независимый security review; Desktop и API необходимо обновлять совместно.

После публикации открыть [чек-лист ручной приёмки](../../work/production/docs/OBJECT-LINKS-MANUAL-ACCEPTANCE.md). Не закрывать его по результатам автоматических тестов.
