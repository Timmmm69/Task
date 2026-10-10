# Validation report — Task UX 6–8, 1.0.0

Дата: 10.10.2026. База: `3a8eceda2e7ec4db13d0f2db2ca5023e079a60cc`. Scope: 22 production/test files, документация проверки и три evidence/note dashboard. Задачи 1–5 сохранены; задачи 9–10 не выполнялись. Серверные API/DTO, права, схема базы и допустимая вложенность не менялись.

## Выполненные проверки

- 771 desktop tests PASS, 0 failed, 0 skipped: 759 в основном прогоне и 12 в шести последовательных WPF-suite. TRX приложены. 14 targeted tests входят в этот общий итог, не прибавляются повторно.
- Release build всей Task.sln PASS. Имеется исходное предупреждение xUnit1031 в DesktopCredentialVaultTests; новых ошибок/предупреждений production нет.
- 11 PostgreSQL/API integration tests PASS (5 Task.Tests, 6 Task.ServiceHosts.Tests), на изолированном настоящем PostgreSQL с явно установленным TASK_POSTGRES_TEST_ADMIN_CONNECTION. Создание/повторное чтение project/parent, сохранность parent, видимость/tenant, запрет второго уровня, отменённый parent и архивный project, rollback невалидных связей.
- Production DesktopTasksApiClient: два transport round-trip tests проверяют обычный create command и повторный GET обеих связей. PersonalTasksClient: настоящий локальный SQLite, create/save/re-read project/parent, отсутствие наследования проекта, ошибки без потери черновика и отсутствие чужих проектов.
- Настоящие MainWindow/PersonalWindow: OS mouse double-click правильной строки и пустой области, защита вложенных интерактивных контролов/scrollbar, read-only просмотр, OS Enter/Space для project/subtask actions, title focus и возврат в список, Tab, прокрутка/выбор/очистка parent, обновление ItemsSource, обычные окна и минимум 800×480, 0 binding errors. Screenshots сохранены и осмотрены.
- Поздние options сохраняют ID, название и пользовательское изменение/очистку; CanExecute защищает busy, права, terminal/nested parents, редактор, комментарий и чек-лист. Создание идёт только через обычное подтверждение. Отмена не отправляет mutation. Validation/Forbidden сохраняют редактор и не показывают успех.
- Предыдущий WPF UiProbe: PASS, 79 checks, 0 binding errors; проверены прежние пользовательские пути, календарь и клавиатурное сохранение.
- Итоговый diff перечитан; diff --check PASS. Чужие изменения/артефакты сохранены и исключены из staging. Git fetch до публикации: HEAD совпадал с origin/main.

## Ограничения и обнаруженные проблемы

- Дополнительный старый Test-ProjectBoundaries.ps1 FAIL: он ожидает net10.0-windows, тогда как исходный main уже имеет net10.0-windows10.0.17763.0. Это не изменение данного пакета; .csproj и проверки не ослаблялись. Лог приложен. Обязательные сборка/регрессия/сценарии проходят.
- Старый ViewStateUiTests первоначально нестабильно завершался с CLR teardown assertion. Устранена гонка тестовой инфраструктуры: теперь ожидается завершение STA-потока до выхода testhost. Все исходные assertions сохранены; финальные изолированный и последовательный прогоны PASS.
- Проверки выполнены на синтетических данных и изолированных локальных базах/HTTP fixtures. Подключение к реальному серверу компании и пользовательским данным не выполнялось.
- Пакет содержит проверенный production source, тесты и evidence; отдельный portable release в scope не входил.

Dashboard: только DESK-05, QA-04, PROD-02 evidence/note/updated_at; npm run dashboard:order выполнен. Progress/status не повышены. SHA-256 всех файлов — manifest.json, ZIP hash — внешний .sha256. Commit/push проверяются после сборки пакета и сообщаются в финальном ответе.
