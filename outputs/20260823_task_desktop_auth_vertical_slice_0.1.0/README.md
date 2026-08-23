# Task desktop authentication vertical slice 0.1.0

Пакет фиксирует проверенный результат **Инкрементов A и B**.

Инкремент A содержит desktop auth foundation: локальную HTTPS-конфигурацию,
health probe, типизированный auth client, DPAPI vault, restore/readiness,
single-flight refresh и terminal sign-out.

Инкремент B добавляет production workflow/ViewModels: первый запуск, настройку
сервера, login, обязательную смену пароля, offline recovery, подтверждённое
ready-состояние, logout, безопасную смену сервера, реакцию на terminal refresh,
cancellable async-команды и безопасные русские сообщения.

Implementation commits:

- Инкремент A: `4a5f72355f45b84f4df70496e3db092ba98bc9eb`
- Инкремент B: `0502c79f231446a96e2424455435ed2ee2ca60ba`

Gate B пройден. Полный desktop authentication vertical slice ещё не завершён:
WPF composition, keyboard/UI Automation wiring, DPI/manual smoke и реальный
API/PostgreSQL E2E относятся к Инкременту C.

Подробные фактические результаты приведены в `validation-report.md`.

Проверка целостности:

```powershell
powershell -ExecutionPolicy Bypass -File .\Verify-Manifest.ps1
```
