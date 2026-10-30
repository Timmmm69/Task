# Взаимодействия — 1.0.0

Основа: origin/main 3fe76562b3c51fee3e36b933b169b101376afb47.

Обновлены карточки записей, форма, выбор типа, группировка полей, подсказки дат и расположение действий. Общие ресурсы темы и бизнес-логика не изменены. Ограничение постраничного просмотра явно сохранено. Существующий Windows-тест дополнен проверкой открытия списка через UI Automation, шести типов и сохранения выбранного типа.

## Проверка

- Release build приложения и Task.Desktop.Tests: PASS, 0 ошибок. Два предупреждения анализатора в неизменённых DesktopCredentialVaultTests.cs и PersonalPlanningTests.cs.
- Команда: `/tmp/task-contact-dotnet/dotnet build work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:BuildInParallel=false -m:1 --no-restore -v:minimal`.
- XML и ссылки на ресурсы XAML: PASS. Набор привязок команд сохранён.
- git diff --check: PASS.
- `dotnet test ... --filter FullyQualifiedName~Interactions`: запуск прерван, Microsoft.WindowsDesktop.App отсутствует на Linux. Тесты не объявляются пройденными.
- Визуальная проверка WPF, клавиатуры, popup и компактного окна: требуется Windows. Существующий native-тест проверяет создание, изменение, конфликт, компактную компоновку и ошибки привязок.
- Для Windows: `dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --filter "FullyQualifiedName~Interactions"`.

Готовность roadmap не повышалась: Windows-проверка ещё не выполнена.
