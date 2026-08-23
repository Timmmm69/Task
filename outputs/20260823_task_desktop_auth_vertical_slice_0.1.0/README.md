# Task desktop authentication vertical slice 0.1.0

Этот пакет фиксирует результат **Инкремента A — client/session foundation**.

В пакет вошли локальная HTTPS-конфигурация сервера, probe `/health/live` и
`/health/ready`, типизированный desktop client для смены пароля, расширенное
отображение auth problem codes, fail-closed session readiness, восстановление
сохранённой сессии и причины terminal sign-out.

Полный desktop authentication vertical slice ещё не завершён: workflow/ViewModel
(Инкремент B), WPF composition и ручной E2E (Инкремент C) не входят в этот результат.

Исходный implementation commit: `4a5f72355f45b84f4df70496e3db092ba98bc9eb`.
Обязательный полный solution gate подтверждён после уточнения тестового контракта
periodic maintenance worker: тесты больше не считают дополнительный допустимый
timer tick ошибкой. Подробности — в `validation-report.md`.

Проверка целостности:

```powershell
powershell -ExecutionPolicy Bypass -File .\Verify-Manifest.ps1
```
