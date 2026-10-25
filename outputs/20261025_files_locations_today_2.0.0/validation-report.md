# Файлы и расположения: компоновка по образцу «Сегодня»

Версия: 2.0.0. База: 3fe76562b3c51fee3e36b933b169b101376afb47.

## Что изменено

Это переработка компоновки: каталог слева, выбранный объект и расположения справа; отдельная прокрутка дерева и деталей. Создание записи и перемещение находятся в сворачиваемых секциях, основные действия — перед списком расположений. При отсутствии выбора показана подсказка, как в инспекторе «Сегодня».

| Образец в TodayView.xaml | Реализация в файлах |
| --- | --- |
| TodayThreeZoneGrid / TodayInspectorRegion: постоянная навигация рядом с деталями | FilesCatalogRegion / FilesInspectorRegion, пропорции 34:66 для дерева и формы |
| Заголовки регионов высотой 68 | Task.Files.Header с MinHeight=68 |
| TodayQueueTemplate: плоские строки, значок, заголовок и метаданные | Строки расположений и файлов проекта на Task.DataList.Item; дерево с Task.Files.TreeItem |
| Тонкие разделители и белая базовая поверхность | Task.Files.Panel, CornerRadius=0, разделители в 1 px |
| Счётчик очереди и пустая область деталей | Счётчики расположений / файлов и подсказка при отсутствии выбранной записи |
| Общие токены шрифта, цветов и кнопки 40 px | Task.Files.* основаны на существующих Task.Input.* / Task.Button.* |
| Инспектор: отдельные группы действий и полей | Форма «Путь и тип расположения» / «Порядок открытия», действия Save / Cancel внизу |

Выпадающие списки, поля и флажки получили плоские шаблоны с фокусом и состоянием disabled. Высокая контрастность использует системные кисти. Удаление ссылки отделено цветом и текстовым пояснением; физические файлы не удаляются. Файлы проекта оформлены такими же строками и панелью выбора.

## Свежие проверки

- Release-сборка Task.Desktop: PASS (0 warnings, 0 errors), receipt desktop-build.txt.
- Сборка Task.Desktop.Tests вместе с последней редакцией приложения: PASS (0 warnings, 0 errors в итоговой инкрементальной сборке), receipt final-build.txt. В предшествующей полной сборке тестов было два предупреждения xUnit1031 / xUnit2031 в неизменённых тестах, receipt tests-build.txt.
- Все пять XAML разобраны; все ссылки Task.* найдены; сохранены прежние AutomationId, Command, CommandParameter, Click, ItemsSource, SelectedItem, SelectedValue, IsChecked, IsEnabled и обработчики перетаскивания: PASS, receipt static-checks.txt. Сравнение выражений Binding допускает только изменение подписи пустого выбора и замену триггера Choosing эквивалентной привязкой Visibility.
- Изменения WorkHubView ограничены CatalogDropArea; другие разделы и нижняя панель связей не изменены: PASS.
- git diff --check: PASS.
- Запуск FileLocations / ProjectFiles / VisualFoundation: BLOCKED ENVIRONMENT, exit 1; testhost требует отсутствующий Microsoft.WindowsDesktop.App 10.0 на Linux. Тесты не выполнены; receipt tests-run.txt.

Команды (SDK .NET 10.0.402):

```text
dotnet build work/production/src/Task.Desktop/Task.Desktop.csproj -c Release -p:EnableWindowsTargeting=true -m:1
dotnet build work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release -p:EnableWindowsTargeting=true -m:1 --no-restore
dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --no-build --no-restore -p:EnableWindowsTargeting=true -m:1 --filter "FullyQualifiedName~FileLocations|FullyQualifiedName~ProjectFiles|FullyQualifiedName~VisualFoundation"
```

## Проверка на Windows перед слиянием

Нативный рендер и скриншоты не получены в Linux. Выполнить указанные тесты, затем проверить: дерево (разворачивание, выбор мышью и стрелками, перетаскивание в папку и корень), сворачиваемые секции создания и перемещения, добавление и редактирование local/UNC/mapped путей, dropdown клавиатурой, Tab/Space/Escape, длинные пути, масштаб 100/150/200%, высокую контрастность, пустое и ограниченное состояние, disabled при загрузке/недостатке прав, безопасное подтверждение удаления и панель связанных объектов.

API, права, ViewModel, хранение данных и работа с физическими файлами не изменены. Стили имеют явные ключи Task.Files.* и не заменяют элементы других разделов. Readiness и roadmap не повышались. Требуется обычная проверка PR перед слиянием.
