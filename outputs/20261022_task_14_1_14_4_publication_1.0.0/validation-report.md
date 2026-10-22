# Task 14.1–14.4 — финальная проверка публикации

Версия 1.0.0, 22.10.2026 (Europe/Minsk). Результат: **PASS**.
Проверенный source commit: `ea1e435480a726ca0e32264158de9e873857b79a`.

Исправлены все ошибки, выявленные проверкой перед main:

- Native UI gate: дочерний тест получает foreground authorization; ожидание активации ограничено, проверяется реальное foreground-окно. Проверки мыши, клавиатуры и фокуса сохранены.
- API-14.4-07: история возвращает все семь обязательных полей, UTC timestamp, каноническую категорию изменения и correlationId. Поля WPF сохранены и явно объявлены в исправленном контракте; приватный payload не раскрывается.
- DOC-14.4-08: производный OpenAPI и operation-specific CSV согласуют VERSION_CONFLICT=412, ACCOUNT_BLOCKED=423, INTERNAL_ERROR=503 и IDEMPOTENCY_REQUEST_IN_PROGRESS=409 с Retry-After. Исторические sources не изменены.
- В общем ProblemDetails восстановлен обязательный fieldErrors=[], объявлен существующий retryable.
- Исправлен недетерминированный fixture теста пагинации: равные timestamps больше не получают случайный GUID tie-break. Проверка запрета неподдерживаемого Title sort сохранена.

## Проверка

Release solution build: 0 errors. Полные suites в финальных проверках: **3407 PASS / 0 FAIL / 0 SKIP**.
Это полный Domain/PostgreSQL suite (914), полный ServiceHosts recheck (671)
и полный Desktop recheck (1822). Production-код после первого полного прогона не менялся.
Его единственный FAIL был в старом fixture пагинации; оригинальный красный TRX сохранён в evidence/original-full-gate.
После исправления fixture повторён весь Desktop; после усиления Retry-After/примеров повторён весь ServiceHosts.
Перекрывающиеся прогоны не прибавляются к общему числу тестов.

Реальные PostgreSQL history responses и HTTP error responses проверены JSON Schema, включая типы, UUID, timestamps,
status/code и Retry-After. Каждый обязательный history field отдельно удалялся из копии ответа: schema обязана отвергнуть такой ответ.
Итоги находятся в contract/contract-validation.json. Dashboard validation и порядок roadmap проверены; progress/status не повышались.

Все исходные пакеты 14.1–14.4 проверены по manifest/SHA256SUMS, ZIP 14.4 дополнительно по SHA/CRC/inventory/payload.
Исторический FAIL предыдущего аудита сохранён; текущая проверка заменяет его рекомендацию не сливать из-за native gate.
Источники, рабочие материалы других задач и несвязанный commit employee-picker не входят в эту публикацию.

## Пределы результата

Пакет готов для main по прямому поручению пользователя «Исправь все ошибки и после этого запушь всё в main».
Здесь подтверждены исправления и автоматизированные/native WPF проверки. Живой LAN/multi-client walkthrough,
реальный screen reader и физические mixed-DPI/multi-monitor сценарии не запускались. Это оставшиеся ручные
приёмочные работы, а не утверждение о найденных неисправленных дефектах. Полная нормативная совместимость
всех остальных API вне перечисленных исправлений этим пакетом не сертифицируется.
