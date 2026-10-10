# Task: пять улучшений форм — validation 1.0.0

PASS. Проверен итоговый diff; scope — только эти улучшения. Sources не менялись.

- Календарь WPF, ручной ввод, Сегодня/Завтра: дата без времени, пустое значение, неверный день без исправления, ScheduleZone/local timezone и переходы DST; точное начало ограничивает выбор дня.
- Enter/Ctrl+Enter: команды и CanExecute активной видимой формы, multiline Enter в задачах/проектах обоих пространств, busy/repeat и IME rejection; ошибка и новый черновик во время запроса сохраняются. Отложенный focus не входит в скрытый view/изменённый context.
- Corporate footer находится вне ScrollViewer и в границах окна 800x480; поля прокручиваются. Существующие retry/conflict/discard команды сохранены.
- Checkbox использует прежний versioned command. Native Space, возврат в невыполненное состояние и реальная SQLite-ошибка подтверждают отсутствие ложной галочки. Удаление отдельно; доступное имя берётся из текста, завершённый текст зачёркнут без снижения контраста.
- Personal filter default off, только «Задачи»: completed/cancelled, счётчик/объяснение/показ скрытых, сохранность редактора/capture/checklist drafts. Настройка не сохраняется; существующие установки показывают все задачи при запуске.

Фактические проверки: 9 targeted tests (входят в общий набор); 755 desktop regression + 1 OS-focus test отдельным последовательным процессом; Release build 0 errors/0 warnings; shell contract PASS. Native WPF: 79 checks PASS, 40 PNG, bindingErrors=[]; реальные системные клавиши, размеры 1200x900 и 800x480, 96/144 DPI renders. Основные normal/minimum/error/checkbox/filter screenshots визуально осмотрены.

Границы: synthetic local fixtures; Corporate presentation adapters проверяют интерфейс и существующие VM/commands без живого сервера компании/credentials. 144 DPI — render, не физический mixed-monitor тест. IME/repeat проверены через классификацию клавиш, не установкой каждого IME. High contrast: checkbox использует стандартные WPF цвета и зачёркивание без opacity; отдельное переключение темы Windows не выполнялось. Existing xUnit1031 warning в DesktopCredentialVaultTests не изменялся. Пакет не содержит installer/portable binary.

Dashboard: только evidence/note/timestamp DESK-05, QA-04, PROD-02; progress/status без повышения. npm run dashboard:order и dashboard:validate PASS. Чужие локальные dashboard правки исключены из index.
