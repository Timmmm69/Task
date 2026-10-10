# Task UX actions 6–8

Версия 1.0.0. Изменения реализованы в обычных формах Personal/Corporate. Задачи 9–10 не входят в пакет.

- `Verify-Desktop.ps1`: Release build, targeted tests, последовательные WPF-suite. Основная desktop-регрессия запускается отдельно с исключением только этих suite, затем все они запускаются последовательно; тесты не отключаются.
- `Verify-Database.ps1`: изолированный PostgreSQL, существующие серверные integration tests и новая проверка связей. Дополнительный boundary-check сохраняет исходную проверку и сообщает существующее расхождение framework.
- `Package.py`: проверяет итоговые TRX и создаёт исходники, evidence, manifest и ZIP с SHA-256 в outputs/.
- `Update-Dashboard.mjs`: меняет evidence/note только DESK-05, QA-04, PROD-02. Для commit используется roadmap-for-index.json из HEAD с этими изменениями, чтобы чужие рабочие изменения dashboard оставались вне commit.

В UI-фикстурах только синтетические данные. Основная регрессия: 759 тестов; отдельные WPF-suite: 12; всего 771 без пропусков. Проверка старых пользовательских путей использует существующий UiProbe проекта.
