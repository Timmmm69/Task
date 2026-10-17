# Task 11.3 — состояния учётных записей · 11.3.0

Реализовано в `feature/admin-user-management`, поверх `ec39281` (этап 11.2) и `4c737b2` (11.1). Использована существующая рабочая копия в `work/admin_user_management_11_1/repo`; посторонние изменения основной папки сохранены. `origin/main` проверен и fetched; на момент проверки ветка содержит два предыдущих коммита сверх origin/main. Эта задача не публикует изменения в main.

## Матрица актуального серверного контракта

| Исходное состояние | activate | block | unblock | deactivate | reactivate |
|---|---|---|---|---|---|
| pending_activation | active | запрет | запрет | deactivated | запрет |
| active | запрет | blocked | active, без изменения | deactivated | active, без изменения |
| blocked | запрет | blocked, без изменения | active | deactivated | запрет |
| deactivated | запрет | запрет | запрет | запрет | active |

UI показывает только эффективные изменения: activate для pending_activation, block для active, unblock для blocked, deactivate для pending_activation/active/blocked, reactivate для deactivated. Разрешённые сервером повторы без изменения состояния отдельных кнопок не имеют. Все 20 сочетаний проверены на реальной PostgreSQL 16; серверные правила не изменены. В БД pending_activation хранится как `pending`.

| Действие UI | Permission | Причина | Тело | Idempotency-Key |
|---|---|---|---|---|
| Активировать | User.Create | нет | expectedVersion | обязательный |
| Заблокировать | User.Block | 1–2000 символов | expectedVersion + reason | обязательный |
| Разблокировать | User.Block | нет | отсутствует | серверный implicit key |
| Деактивировать | User.Block | 1–2000 символов | expectedVersion + reason | обязательный |
| Восстановить учётную запись | User.Block | нет | отсутствует | серверный implicit key |

Для карточки и свежего чтения дополнительно нужен User.Read. Все команды отправляют strong If-Match вида `"v19"`; клиент 11.1 уже содержит эти правила. Для bodyless команд сервер использует ключ `operation:userId:vVersion`.

## Поведение и безопасность

- Перед подтверждением выполняется GET полной карточки. В подтверждении показаны имя, логин, действие и последствия. Панель расположена внутри карточки; выбор сотрудника, вкладки, редактирование и обновление списка заблокированы до завершения или отмены.
- Активация делает запись активной, но не выдаёт учётные данные. Остальные эффективные переходы отзывают существующие сессии и refresh-токены; block/deactivate запрещают вход. Эти последствия подтверждены SQL существующего store, а не придуманы клиентом.
- Permission проверяется перед чтением и отправкой; серверная policy остаётся обязательной. Offline/reconnecting, завершённая сессия и отсутствие прав блокируют команду. Общий authenticated executor также проверяет readiness сессии; локальной очереди нет.
- До серверного ответа сохраняется подтверждённое состояние. Успех обновляет список, выбранную карточку, статус и версию прямо из receipt, без зависимого повторного чтения списка. Повтор с `Idempotency-Replayed: true` обрабатывается как подтверждённый результат, включая неизменившуюся версию.
- Двойной клик защищён общим busy-флагом и AsyncCommand. Поздние ответы после logout, изменения capabilities или деактивации раздела не возвращают данные в UI.
- После 412/428 автоматического повтора нет. Подтверждение заблокировано; нужно отменить действие, заново получить карточку и подтвердить операцию. После терминального отказа также требуется новое подтверждение.
- При потере соединения, неопределённом ответе, отмене отправленного запроса или REQUEST_IN_PROGRESS причина заморожена. Кнопка «Проверить исходный запрос» явно повторяет исходные id/version/reason/key; код не создаёт новый запрос автоматически. Отмена до отправки не вызывает мутацию; отмена после неопределённого результата не объявляет откат.
- Сервер возвращает INVALID_STATE_TRANSITION как для недопустимого перехода, так и для последнего администратора. UI честно объясняет оба варианта и не выдумывает отдельный LAST_ADMIN-код. Тест с настоящими permissions подтверждает сохранение active, версии и сессии при block/deactivate последнего администратора.
- Подтверждённые коды: INVALID_STATE_TRANSITION, VERSION_CONFLICT, PRECONDITION_REQUIRED, VALIDATION_FAILED, IDEMPOTENCY_KEY_REUSED, REQUEST_IN_PROGRESS; 401/403/404/5xx представлены существующими типами ошибок. Произвольные server problem details в UI не выводятся.

## Изменённые файлы

Production:

- `work/production/src/Task.Desktop/ViewModels/AdministrationUserTransitions.cs` — действия, подтверждение, свежая версия, ошибки и явный повтор.
- `work/production/src/Task.Desktop/ViewModels/AdministrationViewModel.cs` — lifecycle команд, выбор, offline feedback и dispose.
- `work/production/src/Task.Desktop/ViewModels/AdministrationUsers.cs` — взаимное исключение редактирования и изменения состояния, authoritative receipt.
- `work/production/src/Task.Desktop/Views/AdministrationView.xaml` — кнопки и панель подтверждения с причиной.

Проверки:

- `work/production/tests/Task.Desktop.Tests/Administration/AdministrationUserTransitionTests.cs` — 46 новых проверок VM.
- `work/production/tests/Task.Desktop.Tests/Administration/AdministrationUserWindowsTests.cs` — дополненный native WPF UIA probe всех пяти действий.
- `work/production/tests/Task.ServiceHosts.Tests/UserEndpointsTests.cs` — параметризованные серверные permissions и stable rejection codes.
- `work/production/tests/Task.Tests/PostgresIdentityLifecycleTests.cs` — полная матрица store, replay и реальная защита последнего администратора.
- `work/verification/11_3/Run-Postgres.ps1` — воспроизводимый изолированный loopback-only PostgreSQL gate.
- `.project-dashboard/roadmap.json` — только evidence/note DESK-03; progress/status сохранены, dashboard:order выполнен.

Публичные API, DTO, серверные переходы, migrations и dependencies не менялись. Сброс паролей, массовые действия, удаление и роли не добавлялись.

## Проверки

| Проверка | Результат |
|---|---|
| Release solution build | PASS |
| Администрирование, финальный Release | 139 PASS / 0 FAIL / 0 SKIP |
| Все ServiceHosts/API тесты | 631 PASS / 0 FAIL / 0 SKIP |
| Identity/security/account/session/refresh/authorization с PostgreSQL 16 | 242 PASS / 0 FAIL / 0 SKIP |
| Полный desktop Release, первый прогон | 1410 PASS / 1 FAIL: PersonalShellViewModelTests.Palette_UsesExistingPersonalSearchAndNavigation |
| Персональный поиск отдельно | 1 PASS |
| Полный desktop Release, второй прогон | 1414 PASS / 1 FAIL: InboxZeroUiTests.NativeWpf_ZeroFocusKeyboardSemanticsMotionAndExternalChanges, Window.Activate, строка 85 |
| Inbox focus отдельно | 1 PASS |
| Native WPF UIA: пять действий, причины, отмена, offline/read-only, binding errors, 1120×900 и 800×600 | PASS |
| git diff --check, dashboard:order, dashboard:validate | PASS |

Первый полный desktop прогон был до четырёх добавленных негативных тестов detail read; второй включает их. После второго прогона изменена только ветка выбора в UpsertUser для сохранения поведения редактора профиля; на финальном коде повторно прошли 139 administration tests и Release build. Полный desktop suite на этой последней мелкой правке заново не запускался. Сбои относятся к неизменённым тестам поиска и фокуса; их isolated PASS не делает полный gate зелёным. Первые запуски новых SQL-тестов выявили ошибки фикстур (pending/grant и переиспользование aggregate version); они исправлены и финальный PostgreSQL gate целиком пройден.

TRX, runtime receipt и native screenshots включены в пакет. Исходники и source.patch позволяют проверить scope; manifest/checks.sha256 фиксируют SHA-256 каждого файла.

## Остаточные ограничения и 11.4

Реализация 11.3 и её целевые проверки выполнены. К разработке 11.4 можно переходить: клиент и серверные lifecycle-контракты согласованы. Общий финальный desktop gate не зелёный из-за указанного сбоя фокуса; полная продуктовая приёмка не объявляется завершённой.

Не проверены вручную: установленный клиент с живым Corporate API, физическое отключение сети, Narrator и mixed-DPI. WPF использует реальную Windows presentation/UI Automation с fake API, а PostgreSQL отдельно проверяет настоящий server store. Сквозное live WPF → HTTP → PostgreSQL выполнение не проводилось. Не проверялась конкурентная блокировка разных администраторов; существующая серверная защита не изменялась. Существующие предупреждения анализаторов в несвязанных тестах сохранены.
