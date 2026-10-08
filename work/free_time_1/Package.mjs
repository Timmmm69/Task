import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';

const version = '1.0.1';
const output = 'outputs/20261008_calendar_free_time_1.0.1';
const files = [
  'src/Task.Desktop/Calendar/DesktopCalendarApiClient.cs',
  'src/Task.Desktop/Calendar/DesktopCalendarContracts.cs',
  'src/Task.Desktop/Calendar/FreeTimeSearch.cs',
  'src/Task.Desktop/Modes/CorporateApplicationContext.cs',
  'src/Task.Desktop/Modes/PersonalApplicationContext.cs',
  'src/Task.Desktop/PersonalWindow.xaml.cs',
  'src/Task.Desktop/ViewModels/CalendarViewModel.cs',
  'src/Task.Desktop/ViewModels/CalendarViewModel.FreeTime.cs',
  'src/Task.Desktop/ViewModels/MainWindowViewModel.cs',
  'src/Task.Desktop/ViewModels/TaskCardEditor.cs',
  'src/Task.Desktop/ViewModels/TaskEditorViewModel.cs',
  'src/Task.Desktop/Views/CalendarView.xaml',
  'src/Task.Desktop/Views/CalendarView.xaml.cs',
  'tests/Task.Desktop.Tests/Calendar/DesktopCalendarApiClientTests.cs',
  'tests/Task.Desktop.Tests/Calendar/FreeTimeSearchTests.cs',
  'tests/Task.Desktop.Tests/WindowsUxAccessibilityTests.cs'
].map(p => `work/production/${p}`);
fs.mkdirSync(output, { recursive: true });
// SHA-256 manifests require the artifact bytes to survive checkout unchanged.
fs.writeFileSync(path.join(output, '.gitattributes'), '* -text whitespace=cr-at-eol\n*.diff whitespace=cr-at-eol,-blank-at-eol\n');
for (const file of files) {
  const target = path.join(output, 'source', file);
  fs.mkdirSync(path.dirname(target), { recursive: true }); fs.copyFileSync(file, target);
}
for (const file of ['UiProbe.csproj', 'Program.cs']) {
  const target = path.join(output, 'verification', 'UiProbe', file);
  fs.mkdirSync(path.dirname(target), { recursive: true }); fs.copyFileSync(path.join('work/free_time_1/UiProbe', file), target);
}
fs.cpSync('work/free_time_1/evidence', path.join(output, 'evidence'), { recursive: true });
const diff = execFileSync('git', ['-c', 'core.safecrlf=false', 'diff', 'HEAD', '--', ...files], { encoding: 'utf8' });
fs.writeFileSync(path.join(output, 'tracked-source.diff'), diff);
const counters = ['desktop', 'domain', 'api'].map(name => {
  const trx = fs.readFileSync(`work/free_time_1/evidence/${name}.trx`, 'utf8');
  const counters = trx.match(/<Counters ([^>]+)/)?.[1];
  const number = field => Number(counters?.match(new RegExp(`${field}="(\\d+)"`))?.[1]);
  const result = { suite: name, total: number('total'), passed: number('passed'), failed: number('failed'), notExecuted: number('notExecuted') };
  if (!result.total || result.failed || result.notExecuted || result.passed !== result.total) throw new Error(`Test gate failed: ${name}`);
  return result;
});
const ui = JSON.parse(fs.readFileSync('work/free_time_1/evidence/ui/ui-result.json', 'utf8'));
if (!ui.passed || ui.uiHeartbeatsDuringRead < 2 || ui.logicalFocus !== 'CalendarTimelineScroll') throw new Error('UI gate failed');
const verification = { version, suites: counters, totalPassed: counters.reduce((n, c) => n + c.passed, 0), ui,
  build: { passed: true, evidence: 'evidence/build.txt' }, dashboard: { valid: true, changedItem: 'PROD-03', progressUnchanged: true },
  limitations: ['Corporate Calendar has no reliable per-user Task/Event ownership projection: all permission-visible intervals are conservatively considered.', 'No live corporate PostgreSQL/network session was exercised.', 'Actual OS High Contrast and a physical screen reader were not toggled; inherited system-color triggers, names and real UI Automation peers were reviewed.'] };
fs.writeFileSync(path.join(output, 'validation-report.json'), JSON.stringify(verification, null, 2) + '\n');
fs.writeFileSync(path.join(output, 'version.json'), JSON.stringify({ feature: 'calendar-free-time', version, baseCommit: execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim(), artifactType: 'source-and-validation' }, null, 2) + '\n');
const report = `# Calendar: ближайшее свободное рабочее время — ${version}

Реализовано непосредственно в текущем Organizer, только в work/production. Новая серверная сущность, миграция, API endpoint, зависимость, lunch break и holiday calendar не добавлены. Публикация выполняется отдельным scope-коммитом после проверки точного staged tree; фактический commit/hash проверяется после push. Существующие пользовательские изменения сохранены.

## Поведение и алгоритм

- Toolbar «Найти время», варианты 15/30/60 минут, по умолчанию 30, первые 3 непересекающихся слота; компактная панель в Calendar.
- Реальные рабочие часы, выходные (ISO 1–7) и первый день недели получаются через существующий settings/me; Personal читает свои реальные WorkspaceSettings. При отсутствии подтверждённых настроек поиск прекращается.
- Зона передаётся от текущего Calendar; во всех вычислениях используется явный TimeZoneInfo. В существующем корпоративном composition root Calendar выбирает локальную зону: пользовательского/организационного timezone-поля в UserSettings нет. Новая зона не выдумана, ID и UTC-смещения явно показываются.
- Поиск начинается с текущего момента, округлённого вверх до минуты (точность обычного draft), в пределах рабочих часов, затем переходит к следующим рабочим дням. Диапазон — 7 календарных дней; «Показать дальше» запрашивает только следующую неделю.
- Busy-список сортируется по UTC start. Пересечения с рабочим периодом обрезает существующий CalendarOverlapPolicy.Evaluate. Монотонный cursor объединяет перекрывающиеся, вложенные и adjacent интервалы и выделяет gaps. Complexity — sort + scan, без pairwise O(n²); максимум 7 дней в одном вычислении.
- DST: отсутствующая рабочая граница сдвигается к первой существующей минуте; ambiguous start использует существующий PersonalTimePolicy (первый UTC-момент), ambiguous end — последний. Длительность измеряется в UTC, повторяющиеся часы различаются UTC-смещениями. Ordinary create draft сохраняет точный выбранный UTC, включая второе повторение; неоднозначный wall-clock start не отправляется в контракт, который его запрещает.
- API допускает 366 дней и 500 items; поиск использует недельные chunks. RangeTooLarge / CALENDAR_RANGE_TOO_LARGE и существующее VALIDATION_FAILED для >500 items дробятся до часа; максимум 64 запросов за попытку, после чего выдаётся объяснение без ложных слотов. NextCursor/неполный или неверный range не считается полным ответом.
- Чтение (включая синхронный Personal SQLite client), сборка списка и расчёт вынесены с UI thread. Большой список из 100000 интервалов проверен; WPF dispatcher продолжал tick во время медленного чтения (${ui.uiHeartbeatsDuringRead} ticks).

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
| Все Task.Desktop.Tests | ${counters[0].passed}/${counters[0].total} PASS |
| Domain Calendar/ScheduleQuery/TaskCard | ${counters[1].passed}/${counters[1].total} PASS |
| ServiceHosts Calendar API | ${counters[2].passed}/${counters[2].total} PASS |
| Сумма тестов | ${verification.totalPassed} PASS, 0 failed, 0 skipped |
| Real WPF 1120×700 | PASS: 3 visible keyboard slots, UIA name, Invoke, day navigation, scroll ${ui.scrollOffset}, logical focus, highlight expiry, 0 writes |
| 100000 busy items + dispatcher heartbeat | PASS |
| dashboard:order / dashboard:validate | PASS; только PROD-03 изменён относительно исходного пользовательского файла, progress не повышался |
| git diff --check / self-review | PASS |

Обязательные случаи покрыты: empty/full day; gap 30/29; unsorted, overlapping, nested и adjacent; вне workday; past today; next weekday/weekends; date-only/cancelled/completed; DST gap/overlap, ambiguous ending boundary; zone != machine; complete/incomplete offline cache; actual API range-code mapping и retry splitting; exhausted limits; malformed coverage; forbidden; late session reply; navigation; ordinary corporate shell draft; сохранение UTC обеих повторяющихся часов.

Self-review: отдельный overlap algorithm не добавлен; source/backend DTO и статусы не изменялись; UI не выдаёт результат при неизвестном полном диапазоне; offline write guards сохраняются; источник кеша ограничен текущими разрешёнными чтениями. Исправлены найденные ошибки token disposal, stale range version mixing, empty-overlay и event subscription lifetime. Перед публикацией добавлены регрессии: ручная смена периода/режима/даты снимает старый выбранный слот и предложение создания; malformed API range code не приводит к исключению; «Сегодня» использует timezone Calendar и внедрённые часы.

Фактическое переключение OS High Contrast/проверка физическим screen reader и live corporate DB/network не выполнялись. Проверены существующие HC styles/system colors, XAML accessibility contract и реальный WPF UI Automation peer. Итоговый пакет — исходники и доказательства проверки, не установщик/обновление уже запущенного portable-клиента.

## Изменённые файлы

${files.map(f => `- \`${f}\``).join('\n')}

Также: .project-dashboard/roadmap.json (только PROD-03); work/free_time_1/UiProbe/{UiProbe.csproj,Program.cs}, Update-Dashboard.mjs и Package.mjs — воспроизводимые проверки/упаковка. Полные новые файлы входят в source/; tracked-source.diff содержит diff отслеживаемых исходников. Manifest перечисляет каждый файл пакета с SHA-256.
`;
fs.writeFileSync(path.join(output, 'validation-report.md'), report);
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
function walk(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => entry.isDirectory() ? walk(path.join(directory, entry.name)) : [path.join(directory, entry.name)]);
}
const entries = walk(output).filter(p => path.basename(p) !== 'manifest.json').sort().map(file => ({ path: path.relative(output, file).replaceAll('\\', '/'), bytes: fs.statSync(file).size, sha256: hash(file) }));
fs.writeFileSync(path.join(output, 'manifest.json'), JSON.stringify({ version, files: entries }, null, 2) + '\n');
for (const entry of entries) if (hash(path.join(output, entry.path)) !== entry.sha256) throw new Error(`Hash mismatch: ${entry.path}`);
console.log(JSON.stringify({ output, files: entries.length, totalPassed: verification.totalPassed, manifestVerified: true }));
