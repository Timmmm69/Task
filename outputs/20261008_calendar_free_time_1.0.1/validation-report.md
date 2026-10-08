# Calendar: ближайшее свободное рабочее время — 1.0.1

Реализовано непосредственно в текущем Organizer, только в work/production. Новая серверная сущность, миграция, API endpoint, зависимость, lunch break и holiday calendar не добавлены. Публикация выполняется отдельным scope-коммитом после проверки точного staged tree; фактический commit/hash проверяется после push. Существующие пользовательские изменения сохранены.

## Поведение и алгоритм

- Toolbar «Найти время», варианты 15/30/60 минут, по умолчанию 30, первые 3 непересекающихся слота; компактная панель в Calendar.
- Реальные рабочие часы, выходные (ISO 1–7) и первый день недели получаются через существующий settings/me; Personal читает свои реальные WorkspaceSettings. При отсутствии подтверждённых настроек поиск прекращается.
- Зона передаётся от текущего Calendar; во всех вычислениях используется явный TimeZoneInfo. В существующем корпоративном composition root Calendar выбирает локальную зону: пользовательского/организационного timezone-поля в UserSettings нет. Новая зона не выдумана, ID и UTC-смещения явно показываются.
- Поиск начинается с текущего момента, округлённого вверх до минуты (точность обычного draft), в пределах рабочих часов, затем переходит к следующим рабочим дням. Диапазон — 7 календарных дней; «Показать дальше» запрашивает только следующую неделю.
- Busy-список сортируется по UTC start. Пересечения с рабочим периодом обрезает существующий CalendarOverlapPolicy.Evaluate. Монотонный cursor объединяет перекрывающиеся, вложенные и adjacent интервалы и выделяет gaps. Complexity — sort + scan, без pairwise O(n²); максимум 7 дней в одном вычислении.
- DST: отсутствующая рабочая граница сдвигается к первой существующей минуте; ambiguous start использует существующий PersonalTimePolicy (первый UTC-момент), ambiguous end — последний. Длительность измеряется в UTC, повторяющиеся часы различаются UTC-смещениями. Ordinary create draft сохраняет точный выбранный UTC, включая второе повторение; неоднозначный wall-clock start не отправляется в контракт, который его запрещает.
- API допускает 366 дней и 500 items; поиск использует недельные chunks. RangeTooLarge / CALENDAR_RANGE_TOO_LARGE и существующее VALIDATION_FAILED для >500 items дробятся до часа; максимум 64 запросов за попытку, после чего выдаётся объяснение без ложных слотов. NextCursor/неполный или неверный range не считается полным ответом.
- Чтение (включая синхронный Personal SQLite client), сборка списка и расчёт вынесены с UI thread. Большой список из 100000 интервалов проверен; WPF dispatcher продолжал tick во время медленного чтения (24 ticks).

## Что занимает время

Task или CalendarEvent из разрешённой сервером Calendar projection с реальными положительными start/end UTC и без all-day признака занимает интервал. Cancelled не занимает. Date-only, all-day и записи без полного start/end не превращаются в искусственные интервалы.

Completed сохраняет текущую семантику режимов: Personal исключает completed (как PersonalTaskStore.Conflicts); Corporate учитывает их реальные интервалы (как ScheduleQueryService.GetConflicts). Бизнес-статусы не меняются. У corporate Task EndAtUtc сохраняется существующая проекция API, новые duration/end semantics не добавлялись.

Поиск рассчитан на рабочие настройки текущего пользователя. Корпоративный read model не предоставляет надёжного фильтра личной Task-занятости: users в существующем API фильтрует только attendees событий. Поэтому учитываются ВСЕ доступные Calendar-интервалы, включая общие, что явно объясняется в UI. Это консервативный результат по известным данным, а не обещание полной личной занятости. Скрытые записи других людей не запрашиваются.

## Выбор, offline, доступность

Выбор переводит Calendar в день слота, прокручивает сетку, возвращает focus в CalendarTimelineScroll и выделяет окно системным цветом на 5 секунд. На пустом дне empty-overlay больше не перекрывает выбранное окно. Кнопка создания отдельно открывает существующий Create Task flow с датой, точным start и duration; автоматического сохранения нет, имеющийся draft не перезаписывается.

Cache содержит только подтверждённые полные диапазоны и максимум 32 страницы текущей сессии. Свежая пересекающаяся страница инвалидирует старую, чтобы не смешивать версии. Offline fallback допускается только при полном непрерывном покрытии ВСЕГО chunk и наличии реальных cached settings; результат помечается потенциально неактуальным, creation/save блокируются. Forbidden/authentication failure не допускают cache fallback; session/capability loss очищают защищённые данные и отменяют поздние ответы. Недостаточный cache и range error не дают ложного свободного слота.

Tab/Enter и Escape используют обычные WPF команды; Automation.Name слота содержит дату + start/end + offsets + duration. Status — polite live region. Панель наследует существующие High Contrast triggers Task.State.Inline, кнопки — стандартные стили, highlight использует SystemColors.HighlightBrushKey с непрозрачной рамкой.

## Проверки

Перед push проверена изолированная копия точного staged source tree, без посторонних локальных правок. Build, все перечисленные тесты и WPF probe выполнены в этой копии; исходный tree ID сохранён в evidence/validated-source-tree.txt.

| Gate | Результат |
| --- | --- |
| dotnet build work/production/Task.sln --no-restore | PASS, 0 errors |
| Все Task.Desktop.Tests | 678/678 PASS |
| Domain Calendar/ScheduleQuery/TaskCard | 215/215 PASS |
| ServiceHosts Calendar API | 33/33 PASS |
| Сумма тестов | 926 PASS, 0 failed, 0 skipped |
| Real WPF 1120×700 | PASS: 3 visible keyboard slots, UIA name, Invoke, day navigation, scroll 561, logical focus, highlight expiry, 0 writes |
| 100000 busy items + dispatcher heartbeat | PASS |
| dashboard:order / dashboard:validate | PASS; только PROD-03 изменён относительно исходного пользовательского файла, progress не повышался |
| git diff --check / self-review | PASS |

Обязательные случаи покрыты: empty/full day; gap 30/29; unsorted, overlapping, nested и adjacent; вне workday; past today; next weekday/weekends; date-only/cancelled/completed; DST gap/overlap, ambiguous ending boundary; zone != machine; complete/incomplete offline cache; actual API range-code mapping и retry splitting; exhausted limits; malformed coverage; forbidden; late session reply; navigation; ordinary corporate shell draft; сохранение UTC обеих повторяющихся часов.

Self-review: отдельный overlap algorithm не добавлен; source/backend DTO и статусы не изменялись; UI не выдаёт результат при неизвестном полном диапазоне; offline write guards сохраняются; источник кеша ограничен текущими разрешёнными чтениями. Исправлены найденные ошибки token disposal, stale range version mixing, empty-overlay и event subscription lifetime. Перед публикацией добавлены регрессии: ручная смена периода/режима/даты снимает старый выбранный слот и предложение создания; malformed API range code не приводит к исключению; «Сегодня» использует timezone Calendar и внедрённые часы.

Фактическое переключение OS High Contrast/проверка физическим screen reader и live corporate DB/network не выполнялись. Проверены существующие HC styles/system colors, XAML accessibility contract и реальный WPF UI Automation peer. Итоговый пакет — исходники и доказательства проверки, не установщик/обновление уже запущенного portable-клиента.

## Изменённые файлы

- `work/production/src/Task.Desktop/Calendar/DesktopCalendarApiClient.cs`
- `work/production/src/Task.Desktop/Calendar/DesktopCalendarContracts.cs`
- `work/production/src/Task.Desktop/Calendar/FreeTimeSearch.cs`
- `work/production/src/Task.Desktop/Modes/CorporateApplicationContext.cs`
- `work/production/src/Task.Desktop/Modes/PersonalApplicationContext.cs`
- `work/production/src/Task.Desktop/PersonalWindow.xaml.cs`
- `work/production/src/Task.Desktop/ViewModels/CalendarViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/CalendarViewModel.FreeTime.cs`
- `work/production/src/Task.Desktop/ViewModels/MainWindowViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/TaskCardEditor.cs`
- `work/production/src/Task.Desktop/ViewModels/TaskEditorViewModel.cs`
- `work/production/src/Task.Desktop/Views/CalendarView.xaml`
- `work/production/src/Task.Desktop/Views/CalendarView.xaml.cs`
- `work/production/tests/Task.Desktop.Tests/Calendar/DesktopCalendarApiClientTests.cs`
- `work/production/tests/Task.Desktop.Tests/Calendar/FreeTimeSearchTests.cs`
- `work/production/tests/Task.Desktop.Tests/WindowsUxAccessibilityTests.cs`

Также: .project-dashboard/roadmap.json (только PROD-03); work/free_time_1/UiProbe/{UiProbe.csproj,Program.cs}, Update-Dashboard.mjs и Package.mjs — воспроизводимые проверки/упаковка. Полные новые файлы входят в source/; tracked-source.diff содержит diff отслеживаемых исходников. Manifest перечисляет каждый файл пакета с SHA-256.
