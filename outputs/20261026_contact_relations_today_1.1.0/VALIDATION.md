# Контактные связи: компоновка «Сегодня», 1.1.0

Предыдущий head PR #18: 87cb4764ff7e4443359223139e84f3eab5e6066d. Main: 3fe76562b3c51fee3e36b933b169b101376afb47.

## Результат

Референс — work/production/src/Task.Desktop/Views/TodayView.xaml:
- TodayQueueTemplate: плоские строки с разделителем, сеткой 24/*, иконками и основной/вторичной информацией.
- TodayQueueRegion и TodayInspectorRegion: заголовки секций высотой 49 DIP и отдельная область деталей.
- TodayInspectorRegion: шрифт DetailTitle, компактные обведённые метаданные высотой 38 DIP, отступы 20,14,20,18 и действия под полями.

Вместо вложенных скруглённых карточек теперь плоские разделы «Способы связи» и «Адреса», счётчики и пустые состояния с иконками. Формы канала и адреса, подтверждение удаления и сохранение/отмена собраны в отдельную область «Детали и действия». При появлении формы она прокручивается в видимую область внешнего ScrollViewer; фокус не перехватывается. Стили ограничены ContactRelationsView. Оба шаблона выпадающих списков и флажков сохранены без семантических изменений. Бизнес-логика, команды, binding paths, ограничения ввода, automation IDs/names и права не менялись. API, DTO, ViewModel, БД и другие экраны не менялись. Roadmap не переоценивался.

## Свежие проверки 2026-10-26

- PASS: Release-сборка Task.Desktop.Tests и приложения с компиляцией XAML/BAML: exit 0, 0 ошибок, 2 предупреждения анализатора в ранее существовавших DesktopCredentialVaultTests.cs:347 и PersonalPlanningTests.cs:397. SDK 10.0.402.
- PASS: dotnet format whitespace --folder --verify-no-changes --include ContactRelationsView.xaml.cs из каталога Views, exit 0.
- PASS: XML, уникальность присваиваний свойств, разрешение ресурсов темы, существование TargetName внутри шаблонов.
- PASS: сравнение с предыдущим head — исходные команды/параметры, ItemsSource, SelectedValue, IsChecked, IsEnabled, Visibility, binding Text, MaxLength и automation-атрибуты сохранены.
- PASS: семантическое сравнение Combo, ComboItem и Check — шаблоны неизменны.
- PASS: git diff --check и контрольные SHA-256.
- BLOCKED: runtime-тесты ContactCardWindowsTests, ContactCardRelationsTests, DesktopContactRelationsClientTests прерваны до выполнения из-за отсутствия Microsoft.WindowsDesktop.App в Linux. Успешных runtime-тестов не заявлено.
- Первоначальный dotnet format с загрузкой проекта прерван из-за недоступного named pipe (Permission denied). Повтор в режиме --folder проверил изменённый C#-файл без MSBuild host и прошёл.

Команды из корня репозитория:

```sh
dotnet build work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --configuration Release -p:EnableWindowsTargeting=true -p:UseSharedCompilation=false --nologo -m:1
dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --configuration Release -p:EnableWindowsTargeting=true --no-build --no-restore --filter 'FullyQualifiedName~ContactCardWindowsTests|FullyQualifiedName~ContactCardRelationsTests|FullyQualifiedName~DesktopContactRelationsClientTests' --nologo
```

## До слияния на Windows

Выполнить указанные тесты и обязательные CI gates. Проверить нативный рендер при 800×600 и 1100×1000, DPI 100/150/200%, high contrast, оба списка мышью и Alt+Down/стрелками/Enter/Esc, флажки Tab/Space, длинные строки и прокрутку. Сценарии: пустые/заполненные каналы и адреса, добавление/редактирование канала, удаление и отмена удаления, добавление адреса, сохранение, отмена и ошибки, режим чтения и блокировка на время сохранения. При появлении редактора/подтверждения проверить прокрутку к его началу без перехвата фокуса. Скриншотов и подтверждения нативного визуального результата в этой Linux-среде нет.

Предыдущий CI (run 40177820065) остановился до build на Verify production formatting с WHITESPACE в посторонних PostgresProductApiStore.cs, CompaniesViewModel.cs, ObjectLinkTargetPickerViewModel.cs и других файлах. Эта задача их не исправляет. Успешный предыдущий/новый локальный build не заменяет обязательные CI gates.

PR остаётся draft до Windows-проверки. Main не изменён. Отчёт и логи 1.0.0 относятся к предыдущей версии; для текущих файлов использовать manifest и SHA256SUMS 1.1.0.
