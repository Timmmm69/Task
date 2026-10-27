# Task: подтверждения архивирования и исключения участника — 1.0.0

Дата: 23.10.2026. База: origin/main cb23ef1. Ветка: fix/project-action-confirmations.

## Изменения

№5: архивирование завершённого проекта требует модального подтверждения с названием проекта и кнопками «Отмена» / «Архивировать». Отмена не меняет список или выбранный проект и не вызывает API.

№6: исключение требует подтверждения с названием проекта, доступным именем и однозначным userAccountId. Без User.Read используется UUID без дополнительных запросов к каталогу. Текст явно сообщает, что учётная запись не удаляется; кнопка списка переименована в «Исключить».

Обе операции используют существующий PersonalConfirmationDialog через внедряемый callback. WPF остаётся в MainWindow; без обработчика ViewModel безопасно отменяет действие. Существующий диалог обеспечивает безопасный default/focus на «Отмена», Esc и закрытие окна; WPF-тест проверяет обе операции, имена UI Automation и все четыре исхода.

После положительного ответа повторно проверяются ссылка/ID/версия исходного проекта, поколение контекста и доступа, активность, сессия, соединение, permission, подтверждённые данные и допустимость действия. Исключение дополнительно проверяет присутствие именно исходного элемента списка, userAccountId, владельца и состояние секции. Замена списка, смена проекта, отмена контекста и потеря с последующим восстановлением доступа инвалидируют подтверждение. AsyncCommand сохраняет защиту от повторного входа; reentrant regression tests подтверждают один API-вызов.

Использованы прежние API, обработка серверных ошибок, обновление версии/участников и серверное подтверждение архивирования. Контракт RemoveMemberAsync не изменён; автоматических повторов и новых idempotency keys нет.

## Проверки

Release-сборка Task.Desktop: PASS, ошибок 0, предупреждений 0. Сборка тестового проекта выполнялась командами dotnet test. Точные команды и счётчики: validation.json.

- projects: total=301, passed=301, failed=0, skipped=0. Failures:
- desktop: total=1901, passed=1900, failed=1, skipped=0. Failures: Task.Desktop.Tests.CommandPaletteTests.Debounce_SearchesOnlyLatestValidQuery_PreservesServerRanking
- debounce-recheck: total=1, passed=1, failed=0, skipped=0. Failures:

Projects включает regression tests отмены, правильных аргументов, недоступных действий, смены контекста, серверных ошибок, повторного входа и native WPF. TRX сохранены локально в work/project_action_confirmations/validation.

Общий desktop-прогон не объявляется успешным при наличии failed в validation.json. Несвязанный тест CommandPaletteTests.Debounce_SearchesOnlyLatestValidQuery_PreservesServerRanking проверен отдельно; его код не изменён. Предупреждения xUnit1031 и xUnit2031 относятся к существующим DesktopCredentialVaultTests и PersonalPlanningTests.

## Канонические источники и границы

Прочитаны AGENTS.md, CANONICAL_BASELINE.md и work/delegation/README.md. Использованы текущий технический baseline Stage 2.3.1 и UX Stage 3.5 (в частности FLOW-026 / FLOW-035: отдельное подтверждение архивирования, архивирование не является удалением). Корневой AGENTS перечисляет более старые версии; согласно прямому заданию текущие версии взяты из CANONICAL_BASELINE.md. Противоречий затронутых API-контрактов не обнаружено.

API, DTO, backend, БД, permissions и sources/ не изменены. Dashboard progress не повышался: инкремент уточняет UX существующих операций. Новой UI-test инфраструктуры нет: использован существующий шаблон изолированного процесса и STA Dispatcher с ресурсами приложения без запуска выбора режима.

Основная рабочая копия содержит чужие untracked-файлы и оставлена без изменений; работа выполнена в отдельном worktree от актуального origin/main. Изменения подготовлены на отдельной ветке для PR; в main не влиты.

## Изменённые исходные файлы

- `work/production/src/Task.Desktop/MainWindow.xaml.cs`
- `work/production/src/Task.Desktop/ViewModels/ProjectsViewModel.Sections.cs`
- `work/production/src/Task.Desktop/ViewModels/ProjectsViewModel.cs`
- `work/production/src/Task.Desktop/Views/ProjectsView.xaml`
- `work/production/tests/Task.Desktop.Tests/Projects/ProjectActionConfirmationTests.cs`
- `work/production/tests/Task.Desktop.Tests/Projects/ProjectConfirmationUiTests.cs`
- `work/production/tests/Task.Desktop.Tests/Projects/ProjectDetailSectionsTests.cs`
- `work/production/tests/Task.Desktop.Tests/Projects/ProjectsViewModelTests.cs`
