# PR №13 и №14 — проверенная интеграция 1.0.0

Дата: 2026-10-26. Проверенная ревизия кода: `751933ad45a1b4323d17eb6a79a4b6ad8e3985fa`.
База после уже опубликованных PR №15 и №16: `d4c355a2f04b4f30144e2b8adb87b0226d914b0d`.

## Изменения

- Сохранены оба дизайна: карточка задачи и экран компаний по образцу «Сегодня».
- Удалены буквальные backslash-n из StackPanel: они вызывали XamlParseException при создании MainWindow.
- Словарь TaskCard явно подключает базовые контролы, устраняя runtime lookup failure Task.Input.Combo.
- Новые поверхности, popup, выбранные элементы и checkbox используют системные цвета в высокой контрастности; фон Companies использует существующий Task.Grid.SurfaceBase.
- Существующие ошибки whitespace исправлены отдельным коммитом: в 73 C#-файлах последовательность непробельных символов сохранена. Бизнес-логика и NuGet-зависимости не менялись.
- Container scan выявил уязвимый runtime 10.0.11. В обоих deployment Dockerfile закреплены официальные .NET runtime/ASP.NET 10.0.14 и SDK 10.0.403 с проверенными MCR SHA-256 digest. Это servicing/security update внутри .NET 10; registry metadata и старый отказ сканера сохранены. Основание: [Microsoft OOB security release от 25 октября](https://devblogs.microsoft.com/dotnet/dotnet-october-2026-oob-security-updates/).
- Устранена гонка готовности контейнерной БД: healthcheck и packaging probe ждут TCP 127.0.0.1, которым затем пользуется инициализация ролей. Раньше probe мог принять временный socket-only init server за готовую БД и получить Connection refused. Поведение bootstrap подтверждено [официальным PostgreSQL entrypoint](https://github.com/docker-library/postgres/blob/master/docker-entrypoint.sh).
- Девять PostgreSQL-зависимых Fact/Theory используют существующие RequiresPostgres атрибуты. Быстрый CI без БД корректно пропускает их; обязательный PostgreSQL gate исполняет проверки. Тест ProjectComments, не требующий БД, оставлен обычным Fact.
- Нативные UI-тесты учитывают настройку Windows отключения анимаций и проверяют попадание вне строки/фокус перед вводом. Непроверенное предположение, что низ списка всегда пуст, устранено; проверки поведения сохранены.

## Проверки

Итоговая локальная Release-проверка всех трёх проектов: **3504 PASS, 0 FAIL, 0 SKIP**. Backend-проекты выполнены с изолированным PostgreSQL 16.14; desktop-проект полностью повторён после уточнения native mouse helper и объединения с актуальным main/PR №15/№16. Backend sources и backend test sources между этими прогонами побайтно не менялись (проверено Git). Точные результаты трёх проектов и пути receipts в test-summary.json и TRX. Обновлённые container images отдельно проходят сборку/сквозные проверки/сканирование в CI на точной итоговой ревизии.

Release solution build, whitespace verify, dependency audit, project boundaries, SEC-05 static/dependency review и SEC-03 secrets/TLS contract — PASS. Локально прототип: build и 29 тестов PASS; delegation subsystem: 7 PASS.

Новый WPF-тест материализует production-шаблоны и проверяет цвета обычных/выбранных элементов, checked/mixed checkbox, popup, заголовок Expander, переход фокуса и automation toggles при LayoutTransform 1/1.5/2. Только источники HC-условий заменены тестовой константой; системные настройки не менялись. Загрузка MainWindow и корпоративное закрытие повторно прошли после исправления обеих ошибок.

GitHub CI для той же ревизии кода: **SUCCESS**. Включает production cheap gates, реальный PostgreSQL 16, container packaging и vulnerability scan. Подробности и ссылки — github-ci.json.

## Ограничения и история диагностики

LayoutTransform-проверка не является физическим mixed-DPI/системным HC walkthrough. Полная ручная матрица на разных мониторах и Narrator этим пакетом не подтверждается.

Первый локальный полный прогон прервался из-за PowerShell Stop на stderr при найденной ошибке ресурса. Следующий прогон выявил предположение о координатах/фокусе в native TaskContextActions; GitHub также выявил зависимость теста анимации от настроек Windows. Эти отказы и целевые неудачные TRX сохранены отдельно. Итоговый final-run относится к исправленному коду; исходный review-пакет не переписывался.

Локальный Docker daemon недоступен; контейнерные проверки подтверждены GitHub CI. Изолированный PostgreSQL остановлен после тестов. Чужие неотслеживаемые файлы в основной рабочей папке сохранены.

## Публикация

Оба исходных PR включены в историю интеграции; публикация выполняется fast-forward push в main после проверок. Этот отчёт описывает проверенный код и не подменяет отдельную проверку фактического remote SHA после push.

manifest.json, SHA256SUMS и соседний validation JSON фиксируют фактически проверенные файлы и ZIP. Progress/status dashboard не повышаются; к затронутым пунктам добавляется только evidence.
