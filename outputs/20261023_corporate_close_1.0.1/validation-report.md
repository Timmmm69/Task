# Task №2 — защита корпоративного закрытия, 1.0.1

Статус: исправление реализовано, автоматическая проверка Windows/WPF пройдена. Ручная приёмка на рабочем месте компании не выполнялась; этот пакет не является утверждением о прохождении live-server/Explorer manual gate.

## Причина и база

MainWindow.OnClosing содержал независимый неполный список из трёх редакторов. Он не использовал общий ModeSwitchGuard.Inspect, мог предлагать выйти во время сохранения, спрашивал перед простым скрытием в трей и полностью обходил проверки при повторном Close после сохранения настроек вида.

Исходная папка: ветка codex/employee-picker-5-1-verification, 1 собственный коммит и 7 коммитов отставания от origin/main. Отслеживаемых незакоммиченных изменений не было, присутствовали многочисленные чужие untracked-артефакты. Попытка merge безопасно остановилась из-за пересечения untracked-файлов. Они не перемещались и не удалялись.

Работа выполнена в work/corporate_close/repo на основе задачи №1 (5ad8521), затем синхронизирована с актуальным origin/main. Точные git SHA приведены в manifest и baseline evidence. Тесты сохранения черновика входящих при навигации присутствуют и входят в полный прогон. После повторной проверки исходники опубликованы в main. Совпадение опубликованного исходного коммита с origin/main проверено до формирования пакета; точный sourceCommit записан в manifest.

Применены действующие Concept / architecture Stage 1 / технический контракт 2.3.1 / UX baseline 3.5. SHA-256 источников: evidence/baseline.json. Источники не изменены. Онлайн-запись остаётся server-authoritative; очереди offline-записей и автосохранения нет. Serena не ответила на инициализацию и активацию; исследование выполнено локальными средствами.

## Обнаруживаемые категории

Ниже все категории общего Inspect; контактные и корпоративные взаимодействия представлены отдельными экземплярами одной категории. Проверяется весь shell, независимо от выбранного раздела. Открытая форма консервативно считается незавершённым состоянием, даже если пользователь ещё не изменил все её поля.

1. Задача.
2. Изменение статуса задачи.
3. Комментарий задачи.
4. Чек-лист задачи.
5. Связи задачи.
6. Планирование задачи на сегодня.
7. Изменение статуса в разделе «Сегодня».
8. Карточка сотрудника.
9. Действие с учётной записью.
10. Событие календаря.
11. Повторения.
12. Проект.
13. Новый участник проекта.
14. Роли участников проекта.
15. Связь проекта с файлом.
16. Связь проекта с контактом.
17. Преобразование входящей задачи.
18. Новая входящая задача.
19. Запись каталога.
20. Путь файла.
21. Контакт.
22. Карточка контакта.
23. Канал или адрес контакта.
24. Удаление канала контакта.
25. Карточка компании.
26. Взаимодействие.
27. Расположение файла.
28. Связи объекта.
29. Настройки профиля.
30. Настройки уведомлений.
31. Корпоративное напоминание.
32. Настройки организации.

По сравнению с прежним списком добавлены планирование/статус в «Сегодня», карточка сотрудника/действие с учётной записью, связи проекта с файлом/контактом, редактор расположения файла и связи объекта. Удаление канала контакта уже обнаруживалось исходным кодом.

## Поведение закрытия

1. При доступном трее обычный X/Close сначала скрывает окно, сохраняя контекст. Предупреждение и освобождение контекста не выполняются.
2. Настоящий выход (пункт трея RequestExit либо закрытие без трея) использует CorporateCloseGuard и общий Inspect. При чистом состоянии диалога нет.
3. При черновиках штатный MessageBox показывает количество и понятные категории, прямо предупреждает о потере данных. По умолчанию выбран ответ «Нет».
4. Отмена не вызывает Save/Discard/Cancel-команды редакторов, не меняет выбранный раздел, значения, конфликт, версии и ключи повторных запросов.
5. Подтверждение разрешает уничтожение контекста обычным lifecycle. Дополнительной записи на сервер нет.
6. Операции проверяются до и после модального диалога и при повторном Close после async flush настроек вида. Подтверждение черновиков не обходит эту проверку.
7. Одно разрешение действует на техническое повторение того же закрытия. Обычная отмена не авторизует будущий выход.

## Незавершённые и неизвестные операции

Используются прежние IsBusy/IsExecuting/IsSaving и сохранённые идентичности команд в Tasks, Today, Inbox, recurrence, workspace и Administration. Для API без надёжного доступного владельцу маркера добавлен небольшой локальный ClosePendingOperations в соответствующий существующий ViewModel. Это счётчик активных вызовов и признак неизвестного исхода, без глобального реестра, хранения черновиков, автоматических повторов, отмены запросов или изменения их аргументов.

Обёрнуты только существующие вызовы записи: проекты/участники/архивирование; календарь; каталог/путь/восстановление; контакты/каналы/адреса/компании/взаимодействия; расположения и связи объектов/проектов; профиль/уведомления/организация/напоминания. Обычные чтения не создают такой блокировки. Успешный либо определённо отклонённый ответ освобождает активный вызов. ServerUnavailable/MalformedResponse, исключение и отмена запроса оставляют неизвестный результат защищённым.

Существенное консервативное ограничение: для API без доказуемого исходного request identity неопределённый исход блокирует обычный выход на оставшееся время жизни этого контекста. Посторонняя успешная запись не снимает блокировку. Новая система восстановления/проверки таких запросов здесь не добавлялась. Для существующих команд с сохранённой идентичностью их штатный успешный повтор может разрешить неопределённость. Долгая блокировка предпочтительнее уничтожения неопределённого запроса.

## Трей, смена режима, сессия

Штатный Windows tray callback теперь вызывает тот же RequestExit, который проверен WPF-тестом. Уведомления и tray освобождаются только после настоящего закрытия либо обязательного технического перехода.

PrepareAsync не вызывается из синхронного OnClosing; его Save/Discard/Cancel контракт сохранён. Общая HasPendingOperations применяется и при смене режима: Discard не может обойти защиту операции с неизвестным исходом. CloseAfterModeSwitch обходит повторный пользовательский вопрос после подготовки режима. CloseForAuthenticationTransition сохраняет прежний обязательный teardown: logout/истечение сессии/возврат к авторизации нельзя отменить диалогом и оставить доступ к данным.

Ограничение безопасности: при обязательном завершении сессии in-memory черновики и ожидания могут быть уничтожены по прежним правилам доступа. Этот переход намеренно не превращён в отменяемый пользовательский выход.

## Повторная проверка перед публикацией в main

Найдены и исправлены шесть ошибок:

1. Повторный Close из CloseForAuthenticationTransition/CloseAfterModeSwitch во время подтверждения вызывал WPF InvalidOperationException. Технический переход теперь присоединяется к текущему закрытию и имеет приоритет над отменой; повторный tray exit не вызывает рекурсию. Добавлен настоящий WPF regression; до исправления тест падал.
2. После определённого отказа задачи (403/401) и recurrence (валидация/конфликт/недоступная сущность) оставался маркер неизвестного исхода. Для однозначных ответов маркер теперь освобождается; неизвестный исход по-прежнему блокирует выход. Для recurrence изменение ограничено Corporate. Тест задачи до исправления воспроизвёл лишнюю блокировку.
3. Отмена финального Close сторонним Closing handler после async flush могла оставить окно отключённым. Восстанавливаются IsEnabled и флаги попытки закрытия; следующий выход снова запрашивает подтверждение. Добавлен WPF regression.
4. Успех нового capture-запроса снимал защиту предыдущего capture с неизвестным исходом. Неизвестность теперь сохраняется; добавлен регрессионный тест.

5. Отмена выхода из трея безусловно включала окно, ранее отключённое для смены режима. Теперь восстановление IsEnabled выполняется только для завершающего Close после ViewState flush; добавлен WPF regression отключённого владельца.
6. DisposeAsync мог завершиться синхронно, и ViewState вызывал вложенный Close из Closing. Для Corporate завершение теперь ставится в Dispatcher, причём флаг CompletingClose устанавливается только в отложенном callback. Сохранены отсутствие двойного вопроса и повторная проверка операций; путь Personal не менялся.

## Автоматические проверки

Добавлены CorporateCloseGuardTests, CorporateCloseUiTests, ClosePendingOperationsTests. Расширены ApplicationModeTests.AssertCancelledAsync, ContactCardViewModelTests и fixture WorkHubContactCardTests. Нативный WPF-тест запускается в отдельном Windows STA-процессе и вызывает настоящий Window.Close/RequestExit, использует настоящий ViewState async flush и проверяет события Closed/видимость/значения. Решение в диалоге и доступность трея подаются через внутренние test seams; это не ручной тест физического меню Explorer.

| Требование | Проверка |
|---|---|
| A | CleanExit + native clean Window.Close, подтверждение запрещено callback-ом |
| B | Реальный Tasks.Editor, отмена/подтверждение без записи |
| C, D | Inbox.Conversion и CaptureText, включая неактивный раздел |
| E | Реальный TaskWorkspace: комментарий, чек-лист, связь, сохранение текста |
| F | Контакт/карточка/компания; прежние CRM fixtures также проверяют общий close guard |
| G, H | Несколько разделов, тот же editor reference, значения и выбранный раздел |
| I | Подтверждение не увеличивает число API-записей |
| J | Ожидаемый PATCH не отменяется; неизвестный ответ/комментарий/контакт блокируют выход |
| K | Настоящий WPF Close скрывает окно при заданной доступности tray |
| L | CloseAfterModeSwitch не вызывает confirm callback |
| M | Existing ModeSwitch tests + сохранённый контракт PrepareAsync |
| N | WPF offline/read-only и конфликт сохраняют черновик при отмене |
| O | Повторные отмены и последующее подтверждение, один prompt при async flush |

Фактически выполнено:

- Release Task.Desktop build: PASS, 0 ошибок; evidence/desktop-build.log.
- Финальный целевой прогон: 45 PASS, 0 FAIL / 0 SKIP; evidence/review-targeted.trx.
- Финальный полный Task.Desktop.Tests Release: 1850 PASS, 0 FAIL / 0 SKIP; evidence/review-desktop-full.trx.
- Более ранние Debug/Release прогоны до последнего усиления: по 1839 PASS; они не подменяют финальный receipt.
- git diff --check и git apply --check --reverse implementation.patch: PASS. dashboard:order и dashboard:validate: PASS, progress/status не повышались.

Один промежуточный полный прогон содержал FAIL в WPF Close, а также тайминговых/фокусных CommandPalette и TaskContextActions тестах. После исправления Close финальные целевой и полный прогоны прошли; production-код палитры и действий по двойному щелчку не изменялся. Исходный неуспешный receipt сохранён отдельно.

Ручные сценарии Windows: НЕ ВЫПОЛНЯЛИСЬ. Не выполнены физическое меню Explorer/tray, live-server logout/token expiration и ручной выбор кнопок стандартного MessageBox. Соответствующие внутренние lifecycle-пути проверены автоматикой; не следует выдавать это за ручной корпоративный end-to-end прогон.

Аварийный Kill/питание/принудительное завершение Windows не защищаются новым дисковым хранилищем черновиков: такое хранилище запрещено границами задачи. Backend, OpenAPI, DTO, БД и permissions не изменены. Файлы Personal не изменены; общий исходный код сохранения лишь получает локальный диагностический маркер, который Personal lifecycle не использует.

В ходе review сохранены отдельные receipts воспроизведения исходных ошибок; они намеренно содержат FAIL и не входят в итоговый PASS-счётчик.

## Изменённые файлы

Полный diff — implementation.patch. Все конечные версии — source-overlay. Автоматически проверенный scope — changed-files.json.

- `.project-dashboard/roadmap.json`
- `work/production/src/Task.Desktop/Infrastructure/ClosePendingOperations.cs`
- `work/production/src/Task.Desktop/Infrastructure/ViewState.cs`
- `work/production/src/Task.Desktop/MainWindow.xaml.cs`
- `work/production/src/Task.Desktop/Modes/CorporateApplicationContext.cs`
- `work/production/src/Task.Desktop/Modes/CorporateCloseGuard.cs`
- `work/production/src/Task.Desktop/Modes/ModeSwitchGuard.cs`
- `work/production/src/Task.Desktop/Notifications/MainWindowNotifications.cs`
- `work/production/src/Task.Desktop/ViewModels/AdministrationUsers.cs`
- `work/production/src/Task.Desktop/ViewModels/CalendarViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/CompaniesViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/ContactCardRelations.cs`
- `work/production/src/Task.Desktop/ViewModels/ContactCardViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/CorporateRemindersViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/FileLocationsViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/InboxViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/InteractionsViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/ObjectLinksViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/ProjectContactsViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/ProjectFilesViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/ProjectsViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/RecurrencePaneViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/TaskWorkspaceViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/TasksViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/TodayQuickActions.cs`
- `work/production/src/Task.Desktop/ViewModels/WorkHubCatalogViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/WorkHubViewModel.cs`
- `work/production/tests/Task.Desktop.Tests/ApplicationModeTests.cs`
- `work/production/tests/Task.Desktop.Tests/Calendar/RecurrencePaneTests.cs`
- `work/production/tests/Task.Desktop.Tests/ClosePendingOperationsTests.cs`
- `work/production/tests/Task.Desktop.Tests/CorporateCloseGuardTests.cs`
- `work/production/tests/Task.Desktop.Tests/CorporateCloseUiTests.cs`
- `work/production/tests/Task.Desktop.Tests/Work/ContactCardViewModelTests.cs`
- `work/production/tests/Task.Desktop.Tests/Work/WorkHubContactCardTests.cs`
