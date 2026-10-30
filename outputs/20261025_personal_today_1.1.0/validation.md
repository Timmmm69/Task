# Личный режим в композиции «Сегодня» — 1.1.0

Source commit: 569c59de5a3ddb2b33e70b1fbad8488e0c2c7ce0. PR #22.
Supersedes the styling-only 1.0.0 report; older manifest describes that historical revision.

## Что изменено

- Композиция TodayView: плоские панели, тонкие разделители, панель действий 68 px, секции 49 px, заголовок подробностей 21 px.
- Задачи: вместо таблицы карточки строк со значком статуса, названием, приоритетом и датами. Соседняя панель содержит план, срок, описание, переходы статуса и чек-лист; открыта по умолчанию, сохранённая настройка пользователя учитывается.
- Проекты: список с метаданными, отдельная панель проекта, даты в блоке планирования, редактор внутри рабочей области.
- Напоминания: создание внутри своей секции; уведомления и их действия отделены, прочитанные заголовки имеют обычное начертание.
- При ширине окна менее 1100 px панели располагаются вертикально. Редактор проекта занимает обе строки. Для напоминаний предусмотрена прокрутка и увеличенная минимальная высота, чтобы форма не вытесняла список.
- Стили композиции выделены в Personal.Workspace.xaml и подключены только к трём представлениям. Команды, обработчики, AutomationId, формат дат и хранение данных сохранены.

## Проверки

PASS: dotnet build work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -p:EnableWindowsTargeting=true --configuration Release --disable-build-servers -m:1

Клиент и тесты скомпилированы. 0 errors; 2 существующих предупреждения xUnit1031 (DesktopCredentialVaultTests.cs:347) и xUnit2031 (PersonalPlanningTests.cs:397).

PASS: XML всех изменённых XAML, наличие именованных ресурсов, сохранность команд/обработчиков/AutomationId относительно PR 1.0.0; git diff --check.

Добавлен PersonalWorkspaceLayoutWindowsTests: реальная загрузка трёх WPF-представлений, измерение неперекрывающихся панелей на 1200/800 px и полноразмерный редактор проекта на узком окне.

NOT RUN: dotnet test ...Task.Desktop.Tests.csproj -p:EnableWindowsTargeting=true --configuration Release --no-build --no-restore --filter FullyQualifiedName~PersonalWorkspaceLayoutWindowsTests. Testhost завершился до запуска теста: Microsoft.WindowsDesktop.App 10.0 отсутствует в Linux (exit 1).

CI первой версии (run 40181209016) остановился до сборки/тестов на Verify production formatting: массовые WHITESPACE-ошибки в существующих C#-файлах, включая WorkHubContactCardTests/WorkHubContactRelationsTests. Это не зелёный общий gate. Массовое форматирование посторонних файлов в эту задачу не включено.

## Осталось на Windows

Запустить новый тест и существующие Personal-тесты. Проверить внешний вид при 800×600 / 1200×900 и 100/150/200% DPI, длинные названия, пустые списки, раскрытые формы, клавиатурный фокус, выпадающие списки и высокую контрастность. Скриншотов нативного приложения нет; визуальное совпадение с TodayView не объявляется проверенным. PR остаётся draft, main не изменён. Roadmap не повышался.
