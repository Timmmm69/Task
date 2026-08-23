# Task desktop authentication vertical slice 0.1.0

Пакет фиксирует проверенный результат **Инкрементов A, B и C**.

Инкремент A содержит desktop auth foundation: локальную HTTPS-конфигурацию,
health probe, типизированный auth client, DPAPI vault, restore/readiness,
single-flight refresh и terminal sign-out.

Инкремент B добавляет production workflow/ViewModels: первый запуск, настройку
сервера, login, обязательную смену пароля, offline recovery, подтверждённое
ready-состояние, logout, безопасную смену сервера, реакцию на terminal refresh,
cancellable async-команды и безопасные русские сообщения.

Инкремент C подключает workflow к WPF: отдельное блокирующее auth/startup-окно,
панели server setup/login/change-password/recovery, composition root, открытие
main shell только после `Ready`, возврат к login при logout/terminal sign-out,
явный lifetime `HttpClient`/`SessionService`, keyboard flow и UI Automation.

Implementation commits:

- Инкремент A: `4a5f72355f45b84f4df70496e3db092ba98bc9eb`
- Инкремент B: `0502c79f231446a96e2424455435ed2ee2ca60ba`
- Инкремент C: `bed8d1298cc20501ab383a41e484bfcac52a52c0`

Gate C по коду и автоматическим проверкам пройден. Выполнен ограниченный ручной
smoke первого запуска на чистом локальном профиле. Полный desktop authentication
vertical slice нельзя объявить завершённым до реального API/PostgreSQL E2E и
ручной DPI/accessibility-матрицы.

Подробные фактические результаты приведены в `validation-report.md`.

Проверка целостности:

```powershell
powershell -ExecutionPolicy Bypass -File .\Verify-Manifest.ps1
```
