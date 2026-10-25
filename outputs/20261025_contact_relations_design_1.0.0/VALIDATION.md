# Контактные связи — оформление 1.0.0

База: 3fe76562b3c51fee3e36b933b169b101376afb47 (origin/main). Дата: 2026-10-25.

## Изменения

Разделы способов связи и адресов оформлены карточками в визуальном языке обновлённой карточки задачи (PR #13). Добавлены локальные стили списков и флажков, подписи и подсказки полей, переносы текста, пустые состояния, отдельное подтверждение удаления, отступы кнопок для переноса на узкой ширине. Поля расположены в одну колонку и используют существующий внешний ScrollViewer карточки. Полный адрес имеет минимальную высоту 80 DIP. Сообщение об ограничении доступа скрывается, когда пустое. Для карточек, popup и флажков предусмотрены системные кисти высокой контрастности.

Стили загружаются только ContactRelationsView. Theme.xaml и остальные PR не затронуты. Сохранены команды, привязки, MaxLength, automation IDs/names и ограничения редактирования/видимости. ViewModel, API и БД не менялись. Roadmap не обновлён: готовность продукта не переоценивалась.

## Проверки

- PASS: финальные XML двух XAML-файлов, разрешение ключей ресурсов, отсутствие двойных присваиваний свойств, наличие именованных целей триггеров шаблонов.
- PASS: сравнение с origin/main — исходные привязки Text, команд, данных, выбора, флажков, IsEnabled/Visibility, лимиты и automation-атрибуты сохранены.
- PASS: git diff --check.
- PASS: Release-сборка Task.Desktop с компиляцией XAML/BAML, 0 предупреждений / 0 ошибок, exit 0.
- PASS: Release-сборка Task.Desktop.Tests и зависимостей, 0 ошибок, exit 0. Два существующих предупреждения анализаторов в DesktopCredentialVaultTests:347 и PersonalPlanningTests:397.
- BLOCKED: запуск выбранных тестов прерван до выполнения: Microsoft.WindowsDesktop.App отсутствует в Linux, exit 1. Пройденных runtime-тестов не заявлено.
- Начальная параллельная сборка завершилась exit 1 без диагностических ошибок; последовательный повтор приложения и тестов прошёл. Compiler server не смог открыть сокет среды и использовал fallback; для сборки тестов установлен UseSharedCompilation=false.

SDK: 10.0.402. Команды из корня репозитория (на Windows убрать EnableWindowsTargeting при необходимости):

```sh
dotnet build work/production/src/Task.Desktop/Task.Desktop.csproj --configuration Release -p:EnableWindowsTargeting=true --no-restore --nologo -v:normal -m:1
dotnet build work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --configuration Release -p:EnableWindowsTargeting=true -p:UseSharedCompilation=false --no-restore --nologo -m:1
dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --configuration Release -p:EnableWindowsTargeting=true --no-build --no-restore --filter 'FullyQualifiedName~ContactCardWindowsTests|FullyQualifiedName~ContactCardRelationsTests|FullyQualifiedName~DesktopContactRelationsClientTests' --nologo
```

## До слияния на Windows

Выполнить указанные тесты и обязательные проверки CI. Открыть карточку контакта: пустые/заполненные каналы и адреса, добавление и редактирование канала, добавление адреса, сохранение, отмена и подтверждение удаления; режим только чтения и сохранение в процессе. Проверить оба списка мышью и Alt+Down/стрелками/Enter/Esc, оба флажка Tab/Space, доступность кнопок через прокрутку при 800×600 и 1100×1000, масштаб 100/150/200%, высокую контрастность и длинные значения. Нативный рендер и эти сценарии здесь не проверены. PR — draft до этой проверки; main не изменён.
