# Task 14.4 — независимый аудит «Вернуть на доработку»

Версия пакета: **1.0.0**. Дата: **22.10.2026**, Europe/Minsk. Репозиторий: `Timmmm69/Task`.

**Итог: PASS_WITH_LIMITATIONS.** Переход подтверждён нормативной моделью. Найденные дефекты безопасности и реализации исправлены в локальной ветке аудита; открытых блокирующих дефектов возврата не установлено. Это заключение относится к проверенному коду с исправлениями 14.4, а не к исходному `e9b08b6` без исправлений. Общая совместимость всех API с нормативным OpenAPI и приёмка в реальной сети компании полностью не подтверждены.

## 1. Проверенные источники и метод

Аудит начат без участия в реализации 14.1–14.3. Выполнены собственная сверка источников, чтение diff и исходников, воспроизведение дефектов, проверки с настоящим PostgreSQL 16 и запуск native Windows WPF. Два отдельных read-only reviewer-прохода проверили серверную безопасность/API и WPF/QA; интеграцию и изменения выполнил основной аудитор. Ранее написанные отчёты использованы как указатели, но не как доказательство работоспособности.

| Источник | Что проверено независимо |
|---|---|
| `sources/concept/Task_Concept_Final.txt`, §7, §11.2, §17–19 | Изменение статуса собственных задач, история, уведомления; отличия расширенной концепции от MVP. |
| `sources/stage_1/architecture_organizer.md`, §12.3 | Серверная авторизация, optimistic concurrency, узкая команда, атомарность и история. |
| `sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2/docs/01_core_domain_and_data.md:367–380` | Прямая каноническая стрелка `review --> in_progress: returned`. Указанный в AGENTS ZIP 2.2 отсутствует; использован отслеживаемый распакованный оригинал 2.2. |
| `sources/stage_2_3/Organizer_Stage2_Technical_Specification_2.3_Final.zip` | Финальный technical package 2.3.1: domain model, concurrency, permissions, errors и OpenAPI. Возврат сохранён. |
| Тот же ZIP, `stage_2_3/openapi/openapi.yaml` | Действующий нормативный OpenAPI `1.2.0-stage2.3`; SHA-256 `36c15dff5adba0041fcfd79f5a0d203835dac5cdd4ad24122bcd92177c13220d`. |
| `sources/stage_3_4/Organizer_Stage3_Final_Baseline_3.4.zip`, `Stage_3_User_Flows.md:218–238` | FLOW-008: разрешённый переход, narrow command, refresh row/card/history, offline и конкурентное изменение. |
| `sources/stage_3_5/Organizer_Stage3_Final_Baseline_3.5.zip`, FLOW-008 | Финальное UX-уточнение, ограничения команд и восстановление после конфликтов. |
| `sources/stage_4_1_1/Organizer_Stage4_PRD_Candidate_4.1.1.zip`, BR-035, FR-250 | Исходный PRD-кандидат также явно содержит возврат из review и server-allowed transitions. |
| `work/stage_4_6_lite/final_baseline/Organizer_Stage4_PRD_Candidate_4.5.zip`, BR-035, FR-250, AC-035, AC-1412 | Утверждённый последующий PRD; approval-файл рядом: `Approved: Stage 4.5 + Stage 4.6 Lite PASS.` |
| `Task_14_1_Status_Return_Contract_Audit.md` из пакетов 14.1/14.2 | Исходные противоречия, прежние ограничения и список проверок; выводы перепроверены по нормативным файлам и исходникам. |
| `outputs/20261022_task_14_2_status_return_contract_1.0.0/Task_14_2_Status_Return_Implementation_Contract.md`, addendum | Минимальный сценарий, reason=null, отсутствие нового уведомления/reviewer-policy; AC-14-02 сохраняет смысл обычного Start. Входные документы находятся в исходной рабочей папке проекта; их SHA зафиксированы в пакете аудита. |
| `work/production/docs/task-14-2-return-contract-decision.md` в `1077445` | DEC-14-RETURN-001 заменяет только запрет возврата в историческом implementation prompt. Канонические требования и HTTP DTO не изменены. |
| Production source и автоматизированные тесты | Domain, Application, endpoint, auth/session, permissions/object scope, PostgreSQL executor, history/outbox/notification/search, WPF Tasks/Today, personal shared-rule callers. |

Точные хэши и выдержки собраны заново в `evidence/independent-source-evidence.json` и `evidence/canonical-baselines-evidence.json`. Нормативные ZIP и `sources/` не изменялись. Исторический запрет в `outputs/20260826_task_write_vertical_slice_specs_1.0.0/04_SERVER_TASK_TRANSITION.md:33` не проигнорирован: его узкое замещение явно записано в решении 14.2. Подтверждение возврата не зависит только от этого решения — сама стрелка существует в канонических 2.2 и PRD.

## 2. Проверенные коммиты и границы diff

| Назначение | Commit |
|---|---|
| Проверенный `origin/main`, общий baseline | `fa504bc01584547afc50398ba6c1735391c4173c` |
| 14.2, сервер | `10774455af70c2a0d5c586bd6f5af26bbbc0a695` |
| 14.3, WPF; исходная точка аудита | `e9b08b6ad4623dd29b6daa00ab2ac1fefff3da83` |
| Исправления 14.4 | `18a763d50b6a600743259eaca75dd98b62225c23 (основное исправление: 89d41421eb668244b72e67f2306959fdc8ef2fa2)` |

Проверены оба последовательных diff и кумулятивный `fa504bc..e9b08b6`, включая все изменённые production/test/doc/dashboard-файлы и материалы двух пакетов. Полный список — `evidence/audited-commit-files.json`. Все 44 записи SHA256SUMS пакетов 14.2–14.3 совпали; шесть source overlays 14.3 совпадают с `e9b08b6` после нормализации CRLF/LF. Старые TRX не засчитаны за проверки 14.4.

Работа выполнена в `work/status_return_14_4/repo`, ветка `codex/status-return-14-4-audit`. Исходная ветка и неотслеживаемые материалы других задач сохранены. На момент проверки 14.2 и 14.3 ещё **не входят в origin/main**. Push, merge и создание PR этим аудитом не выполнялись.

## 3. Матрица бизнес-переходов

Фактическая матрица выбранного production baseline с возвратом: **9 разрешённых, 16 запрещённых** комбинаций. `✓` — active task, допустимый actor/scope и актуальная версия; `—` — 409 INVALID_STATE_TRANSITION. Archived/trashed отклоняются независимо от пары статусов.

| Из / В | new | in_progress | review | completed | cancelled |
|---|---|---|---|---|---|
| new | — | ✓ Start | — | ✓¹ | ✓ |
| in_progress | — | — | ✓ | ✓¹ | ✓ |
| review | — | **✓ ReturnForRework** | — | ✓ | ✓ |
| completed | — | —² | — | — | — |
| cancelled | —² | — | — | — | — |

¹ Прямое completion из new/in_progress разрешалось до 14.2. Нормативная диаграмма не рисует эти стрелки, а UX FLOW-007 допускает completion при отсутствии обязательной review-policy. Это прежнее расхождение, которое DEC-14-RETURN-001 оставляет отдельным вопросом; новых разрешений аудит не добавлял.

² Каноническая диаграмма содержит условные `completed → in_progress: reopen with permission` и `cancelled → new: restore workflow`. Их полный policy/workflow не реализован этим узким срезом и не был самовольно добавлен. Возврат из review не является terminal reopening или восстановлением из корзины.

Для возврата подтверждены: статус `in_progress`, version+1, actor/time сервера, неизменность прочих данных, null completion metadata, одна запись audit/event/outbox, возможность снова отправить результат на проверку. Доменный `Start` теперь отдельно отклоняет review; `ReturnForRework` принимает только active review. Общая матрица TaskRules сохранена для других клиентов, включая personal.

## 4. Матрица permissions и безопасности

| Actor/условие | Просмотр | Возврат | Доказательство |
|---|---|---|---|
| Допустимый actor с Task.Read и effective Task.ChangeStatus в scope задачи | Да | Да | Endpoint policy + реальный PostgreSQL executor/object policy. |
| Только Task.Read / observer / read-only | По scope | Нет | Capability недостаточно; VM блокирует, сервер перепроверяет. |
| Watcher без права изменения статуса объекта | Да | Нет | Real PostgreSQL watcher scenario. |
| Посторонний actor / чужая организация / hidden task | Нет | Нет, fail-closed | Scoped load/visibility; 404 OBJECT_NOT_VISIBLE, без mutation и payload. |
| Explicit deny / revoked или expired grant | По оставшимся правам | Нет | Deny-wins и iam.permission_available/object-action recheck. После смены scope старый authenticated command также отклоняется. |
| Отозванная/истёкшая сессия, отозванное устройство, изменённые credentials/scope | Нет новых разрешённых команд | Нет execute и replay | Новые реальные PostgreSQL тесты: 7 состояний × execute/replay, без побочных эффектов. |
| Account blocked | Нет write | Нет | Существующий 423 ACCOUNT_BLOCKED; desktop трактует как завершение доступа, а не сетевую неопределённость. |
| Нет Comment.Create | По Task.Read | Разрешено при Task.ChangeStatus | Возврат reason=null не создаёт комментарий. |
| Нет History.Read | По Task.Read | Разрешено при Task.ChangeStatus | История ограничивается отдельно; право истории не является prerequisite команды. |

Публичное право — `Task.ChangeStatus`; runtime — `task.changestatus`; endpoint policy — `permission.task.change-status`. Название роли само по себе не авторизует действие. Отдельное правило «только постановщик/руководитель» или запрет self-return не вводились: их нет в утверждённом минимальном контракте.

## 5. Проверка API и транзакции

Используется прежняя операция `POST /api/v1/tasks/{id}/transition`, operationId `POST_api_v1_tasks_id_transition`. DTO: required targetStatus из пяти существующих значений, optional string|null reason до 2000; additionalProperties=false. Desktop возврат передаёт `{"targetStatus":"in_progress","reason":null}`. Новых endpoint, DTO, enum, permissions и миграций нет.

| Проверка | Результат |
|---|---|
| If-Match | Strong ETag защищает задачу из URL; missing→428; malformed→400; stale→412 VERSION_CONFLICT. |
| Idempotency | Scope organization/user/operation/key; hash связывает taskId, expectedVersion и body. Exact replay возвращает прежний body/ETag; другой intent/reason при том же key→409 KEY_REUSED. |
| Безопасность replay | До acquire/replay проверяются действительность сессии и permissions; затем повторяются object visibility/action checks. Replay не обходит отзыв доступа. |
| Атомарность | Aggregate, audit `task.change_status`, TaskStatusChanged, realtime outbox и durable idempotency response — одна transaction/commit. Инъекция event failure не оставляет частичных записей. |
| Конкуренция | Return vs complete и return vs редактирование карточки: один победитель, одна новая version; проигравший получает version conflict. Save не перезаписывает возвращённый статус молча. |
| Payload | fromStatus=review, targetStatus=in_progress, aggregateVersion, actorId, correlationId; changedFields=status. Reason намеренно не сохраняется. |
| История | После commit событие доступно существующей history/workspace projection с правильной версией/actor. Известный общий DTO drift описан ниже. |
| Побочные эффекты | Нет нового notification type, комментария, смены исполнителя, проекта, срока, приоритета или завершения задачи. Fresh list/detail/search видят сохранённую задачу; недоступному actor search её не раскрывает. |

Причина необязательна и не гарантируется историей; отсутствие return notification — явно принятое ограничение 14.2, а не скрытый недоделанный side effect. Общую совместимость runtime с нормативным OpenAPI нельзя обозначить PASS из-за прежних расхождений HistoryEntry и каталога ошибок (§10).

## 6. Проверка UI

**Native Windows WPF фактически запускался.** Создавалось настоящее MainWindow с настоящими XAML/control templates, dispatcher, UI Automation peers и клавиатурным фокусом. Сетевой клиент в этом native-сценарии — детерминированная fixture; это не доказательство LAN end-to-end.

| Сценарий | Результат |
|---|---|
| Доступность | В Tasks отдельные CanReturnForRework=review и CanStart=new; необходимы loaded current details, права, online/session, отсутствие busy/editor/workspace draft. |
| Подтверждение | «Вернуть задачу на доработку?» / «Вернуть», без ввода причины; Cancel ничего не отправляет. |
| Double submit | Async command + общий mutation gate; повторное подтверждение во время write не отправляет второй запрос. |
| Авторитетный статус | До server success карточка остаётся review; после ответа — подтверждённые server status/version. |
| Ошибки | Forbidden/read-only, session/account interruption, 404, 409 invalid state, validation, 412 и network uncertainty проверены. Ложный успех не показывается. |
| Offline/retry | Offline блокирует отправку. При неопределённом результате сохраняется исходный command/key/body/version; reconnect не отправляет автоматически. Transport проверен также через одно refresh/retry после 401. |
| Конкурентный edit | Открытый editor/несохранённый workspace draft блокирует return; 412 предлагает загрузить актуальную версию, без перезаписи чужой работы. |
| List/card/history | Authoritative response обновляет список/карточку; workspace/history перечитываются. При Review-фильтре строка удаляется, selection/detail переходят к оставшейся задаче либо null. Сбой history refresh не откатывает успешную команду. |
| Keyboard/accessibility | Native проверка focus origin→confirmation, Tab→Back, cancel→origin; также проверено, что cancel обычного Complete не переводит focus на Return. AutomationId/Name, tab stop, focus visual и assertive announcement сохранены. |
| Компактная геометрия | Native screenshots и hit testing обеих вертикальных границ кнопки при логических размерах окна (DIP) логических размерах окна (DIP) 1100×900, 800×800, 800×600 и минимуме 800×480. Кнопка не закрыта footer/announcement; список имеет конечный viewport. |
| Today/shared callers | Проверена матрица Today и local personal regression. В Today обнаруженная подмена «Начать работу» возвратом устранена: отдельное действие с подтверждением. |

Projects/Today/поиск не получают нового фонового realtime-транспорта. Возврат сохраняется в общем источнике данных; повторные чтения/активация представлений используют актуальные данные. Мгновенная синхронизация уже открытых неактивных представлений и разных рабочих мест не доказана этим аудитом.

## 7. Негативные сценарии и дополненное покрытие

- Все 25 комбинаций статусов, same-state, terminal, archived и trashed; новый разрешённый переход ровно один.
- Required/malformed If-Match и key, invalid DTO/длина reason, actor/tenant/object denial; существующие endpoint/security suites.
- Отзыв сессии **после** формирования authenticated command и до write; replay после отзыва. Дополнительно idle/absolute expiry, device revocation, credential/scope mismatch, blocked account.
- HTTP mapping session-invalid exception для transition, create и patch; 423 на desktop больше не маскируется под retryable malformed response.
- Parallel same key; different key competing completion; stale version; role revocation до replay; event failure rollback.
- Return одновременно с сохранением title; свежие list/detail/search; denied search actor; сохранность полей и completion metadata.
- Двойная отправка, запрет команды при editor/comment draft, read-only/offline, selection/deactivation во время запроса, поздний ответ.
- Original-intent retry после сетевой неопределённости; byte-equivalent transport/body/key/If-Match; отдельная 401 refresh ветвь.
- Review filter с оставшейся/последней строкой; failure history reload после подтверждённого успеха.
- Реальный focus без искусственного `confirm.Focus()` до проверки; cancel из Return и обычного Complete; реальный hit testing в минимальном окне.

## 8. Сборка

**PASS: Release solution build, 0 errors.** Исходная сборка до исправлений также прошла; для финального кода сохранён `evidence/final-acceptance/build.log`. Старые предупреждения ASPDEPR004, xUnit1031/xUnit2031 не относятся к возврату.

Проверена Release-сборка всей `work/production/Task.sln` на установленном .NET SDK 10.0.400. Предупреждения старых WebHostBuilder/xUnit участков не выдаются за ошибки возврата и не исправлялись попутно. Точный лог включён в evidence.

## 9. Тесты и воспроизводимость

| Проверка | PASS | FAIL | SKIP | Evidence |
|---|---:|---:|---:|---|
| Полный Domain / PostgreSQL 16 | 905 | 0 | 0 | `verified-release/novik_TIM_2026-10-22_17_36_27_net10.0.trx` |
| Полный HTTP / ServiceHosts | 669 | 0 | 0 | `verified-release/novik_TIM_2026-10-22_17_39_50_net10.0.trx` |
| Полный Desktop до последнего уточнения viewport (89d4142) | 1821 | 1 | 0 | `desktop-final/desktop-final.trx` |

**Итого полных suites: 3395 PASS / 1 FAIL / 0 SKIP. Полный gate не является зелёным.** Единственный FAIL — `TaskContextActionsUiTests.NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow`, `Assert.True(company.Activate())`, строка 62. Он независимо воспроизведён на неизменённом `e9b08b6` (`baseline-ui-recheck/baseline-ui.trx`); сценарий теста до действий продукта не дошёл. Тест не отключён и не ослаблен.

После последнего уточнения компактного viewport выполнен повторный scoped acceptance на актуальном коде; новые source-изменения после него отсутствуют:

| Повторная проверка | PASS | FAIL | SKIP |
|---|---:|---:|---:|
| Domain/PostgreSQL return | 23 | 0 | 0 |
| HTTP return / shared session mapping | 22 | 0 | 0 |
| Desktop return / Today / filtering / native WPF | 152 | 0 | 0 |

Эти scoped прогоны перекрываются с полными suites и не прибавляются к общему числу. Сводка всех сохранённых red/green прогонов — `evidence/test-summary.json`; последние результаты — `evidence/final-acceptance/`.


Первый red-прогон `defect-repro`: **2 PostgreSQL failures** воспроизвели принятие revoked-session execute/replay, **1 native WPF failure** — потерю фокуса. Отдельный `layout-repro-and-regression` подтвердил clipping кнопки в 800×600. Эти неуспешные проверки сохранены как доказательства дефектов, а не удалены ради зелёного отчёта.

Промежуточный полный gate выявил неактуальные auth-context fixtures: после назначения роли тесты продолжали передавать scopeVersion=1. Fixtures теперь читают действующую версию, а чужой tenant проверяется с реально действительным authenticated context. Новый тест гонки save/return также явно выдаёт actor оба требуемых capability. Проверки безопасности ради прохождения тестов не ослаблены.

Runner `Run-AuditGate.ps1` создаёт отдельный PostgreSQL 16, привязанный только к 127.0.0.1, и останавливает его в finally. Временный PG data directory использует ASCII TEMP из-за ошибки initdb на кириллическом пути; исходники/тесты/логи/артефакты находятся в work/outputs проекта. Бизнес-данные и существующие серверы не затрагиваются. TRX counters, размеры/хэши, источник и перечень изменений сохранены для повторения.

## 10. Найденные дефекты

Участки исходного кода относятся к `e9b08b6`; актуальные исправленные координаты находятся в source overlay/patch пакета. Severity отражает исходный дефект, а не его состояние после исправления.

### SEC-14.4-01 — HIGH — недействительная сессия могла execute/replay

- **Файл/участок:** `work/production/src/Task.Infrastructure/Persistence/PostgresTaskWriteCommandExecutor.cs`, исходные строки 66–94 и 304–334.
- **Воспроизведение:** создать authenticated return command, отозвать соответствующую запись iam.sessions, вызвать executor. Повторить после первого успешного исполнения тем же key.
- **Последствия:** mutation либо выдача сохранённого ответа после отзыва доступа; обычная проверка middleware не защищает запрос, ожидающий транзакционную блокировку. Дефект предшествует 14.2, но затрагивает новый возврат.
- **Исправление:** validate до idempotency/permissions; состояния сессии/device/account и claim versions; share locks на session/account до commit; существующие 401/423 через endpoint. Server-side context переносит версии в internal command.
- **Повторная проверка:** 14 реальных PG сценариев execute/replay, HTTP mapping и полные create/patch regression; результат в §9. Никаких частичных task/audit/event/outbox/idempotency effects.

### UI-14.4-02 — MEDIUM — потеря keyboard focus при подтверждении

- **Файл/участок:** `work/production/src/Task.Desktop/MainWindow.xaml:901`, `MainWindow.xaml.cs:155`, `TasksViewModel.cs:1561`; прежний `TaskReturnUiTests.cs:68` вручную фокусировал confirm и скрывал дефект.
- **Воспроизведение:** фокус на Return, invoke; кнопка скрывается, actual focused element — ScrollViewer вместо подтверждения.
- **Последствия:** нарушенная предсказуемая keyboard navigation и труднодоступное подтверждение.
- **Исправление:** focus на подтверждение при его появлении; сохранение и восстановление origin при cancel; fallback на список, если действие исчезло; guards от позднего перехвата фокуса в другой секции.
- **Повторная проверка:** native WPF assert до любого ручного focus, Tab/Back, cancel Return и cancel Complete; screenshots/hit tests.

### UI-14.4-03 — HIGH — clipping действия в поддерживаемом компактном окне

- **Файл/участок:** `work/production/src/Task.Desktop/MainWindow.xaml`, прежняя сетка Grid.Row=3 и compact Expander MaxHeight=240 (около строк 617–774).
- **Воспроизведение:** 800×600, раскрыть inspector и Return confirmation; confirm располагается вне доступной области и перекрывается count/announcement/footer. MinHeight окна — 480.
- **Последствия:** основной сценарий нельзя надёжно выполнить мышью в поддерживаемой геометрии, включая небольшие рабочие области при высоком DPI.
- **Исправление:** конечный viewport списка/инспектора и прокрутка переполнения; compact expanded pane имеет достаточную минимальную высоту, не выталкивая неподвижные панели. Height привязан к выделенному viewport, а не к собственной ActualHeight.
- **Повторная проверка:** native 1100×900, 800×800, 800×600, 800×480; hit testing верхней и нижней части confirm. Исходный red и финальные screenshots сохранены.

### UI-14.4-04 — MEDIUM — ACCOUNT_BLOCKED считался неопределённым ответом

- **Файл/участок:** `work/production/src/Task.Desktop/Tasks/DesktopTasksApiClient.cs`, MapWriteResult/MapGetResult.
- **Воспроизведение:** получить существующий 423 ACCOUNT_BLOCKED на transition. Ранее ветка попадала в MalformedResponse, pending intent оставался как для сетевой ошибки.
- **Последствия:** неверный текст и повторные попытки вместо прекращения write access.
- **Исправление:** 423 преобразуется в AuthenticationFailure в read/write; существующие VM session-ended guards блокируют команды.
- **Повторная проверка:** transport test Return_BlockedAccountEndsWriteAccess, VM session tests и desktop regression. Нового HTTP-кода нет.

### DOM-14.4-05 — MEDIUM — возврат расширил смысл обычного Start

- **Файл/участок:** `work/production/src/Task.Domain/TaskAggregate.cs:139`, `work/production/src/Task.Application/TaskUpdateCommandService.cs:351`; `TaskLifecycleService.Start` наследовал расширенное поведение.
- **Воспроизведение:** вызвать Start у review aggregate; в 14.2 он выполнял возврат, хотя AC-14-02 принятого implementation contract сохраняет Start только для new.
- **Последствия:** семантика существующей команды Start меняется вне нового действия.
- **Исправление:** отдельный ReturnForRework с guard active review; Start снова только new; HTTP transition выбирает нужный доменный метод. HTTP matrix осталась 9/16.
- **Повторная проверка:** domain preservation/guards, Start(review) rejection, полные endpoint и shared-rule regression.

### UI-14.4-06 — MEDIUM — Today предлагал возврат под именем «Начать работу»

- **Файл/участок:** `work/production/src/Task.Desktop/ViewModels/TodayQuickActions.cs:29,64`, `work/production/src/Task.Desktop/Views/TodayView.xaml:587`.
- **Воспроизведение:** выбрать review task в Today после расширения общей TaskRules. CanStart становился true и отправлял in_progress сразу, под старым названием и без return confirmation.
- **Последствия:** неверный смысл действия и несогласованный UX нового возврата в связанном представлении.
- **Исправление:** CanStart=new, отдельный CanReturnForRework=review, правильный текст/label и подтверждение перед отправкой; permission/network/retry и матрица не расширены.
- **Повторная проверка:** обновлена полная Today matrix и отдельный тест confirmation/cancel/reason/version; полный desktop gate.

### API-14.4-07 — MEDIUM — прежний HistoryEntry DTO drift, открыт

- **Файл/участок:** `work/production/src/Task.Infrastructure/Persistence/PostgresProductApiRelations.cs`, tasks/history projection; нормативный OpenAPI HistoryEntry около строки 27959.
- **Воспроизведение:** GET history после return: runtime отдаёт event projection id/action/version/actorUserId/occurredAt/changedFields, а нормативная HistoryEntry требует другой набор/имена полей, включая objectId/objectVersion/changedAt/changeType/correlationId.
- **Последствия:** общий внешне сгенерированный клиент не получает полную нормативную shape; причина/from-target не показываются этим UI-представлением. Это не отсутствие самой audit/event записи.
- **Исправление:** не выполнялось; 14.2 прямо оставил общий API/history alignment за пределами минимального возврата, а самостоятельно менять API запрещено задачей.
- **Повторная проверка:** наличие TaskStatusChanged/version/actor/status в текущей projection подтверждено настоящим PG; нормативная shape остаётся ограничением, не обозначена PASS.

### DOC-14.4-08 — LOW — прежние противоречия каталога ошибок и OpenAPI

- **Файл/участок:** normative `catalogs/errors.csv`, VERSION_CONFLICT; `openapi.yaml`, POST transition responses/x-error-codes.
- **Воспроизведение:** CSV указывает VERSION_CONFLICT=409, тогда как операция OpenAPI и runtime используют 412. Несколько общих runtime ветвей (например 423/503 и request-in-progress) описаны операцией неполно.
- **Последствия:** документарная неоднозначность для внешних клиентов и полного contract validation.
- **Исправление:** не выполнялось; сохранён действующий согласованный runtime/operation contract и старые stable codes. Требуется отдельное согласование нормативных файлов.
- **Повторная проверка:** runtime 412 и invalid-session mapping проверены; документарный drift остаётся открытым.

## 11. Выполненные исправления

Production: транзакционная проверка session/context до replay; соответствующий existing error mapping; 423 в desktop; отдельный Domain.ReturnForRework; управление keyboard focus; bounded compact viewport; согласованный Today return. Новых HTTP schemas, migrations, permissions, notification types и обязательной причины нет.

Tests: добавлен `PostgresTaskReturnAuditTests.cs`; расширены return/transport/native/Today/HTTP/domain suites; старые PG auth fixtures приведены к реальным текущим scope versions. Иные тесты не отключались и не ослаблялись ради PASS. Список и SHA source overlay — manifest; патчи включены как относительно e9b08b6, так и относительно origin/main.

Roadmap: обновлены только evidence/note затронутых `DESK-02` и `API-02`, выполнен `npm run dashboard:order`; progress/status не повышены.

## 12. Оставшиеся риски и пределы проверки

1. Общие HistoryEntry/error-catalog/state-machine расхождения описаны явно (§3, §10); audit не объявляет весь продукт нормативно полным и не меняет API/бизнес-модель.
2. Windows UI запускался с настоящим WPF и test client. Связка **реальное рабочее место → HTTPS/LAN → реальный API → PG → второе рабочее место**, потеря пакетов на сети и мгновенное cross-view/cross-client обновление вручную не проверены.
3. Реальный screen reader/NVDA и физический mixed-DPI/multi-monitor walkthrough не запускались. Наличие AutomationId/Name/LiveSetting и программная focus navigation не равны полной ручной accessibility-приёмке.
4. Новый notification о возврате и сохраняемая причина не обещаются; для их появления нужно отдельное решение. История показывает существующее событие смены статуса.
5. PostgreSQL integration выполнена на версии 16 с отдельным runtime role, не на всех поддерживаемых версиях/производственных данных и не как нагрузочный тест.
6. Nullable session/version в низкоуровневом command остаётся для доверенных внутренних/system callers. Публичные HTTP фабрики передают authenticated session и обе версии; JSON-клиент не может выбрать эти внутренние поля.
7. Полный native gate содержит воспроизводимый исходный сбой активации окна (§9). Он не скрыт успешными scoped tests. Для merge нужно получить успешный интерактивный прогон этого gate либо отдельно принять и оформить ограничение окружения.

## 13. Итоговый статус и готовность к Pull Request

**PASS_WITH_LIMITATIONS.** Канонический возврат подтверждён; обнаруженные HIGH/MEDIUM дефекты реализации возврата исправлены и перепроверены. Реальный PostgreSQL, HTTP adapter, общие save paths и настоящий native WPF проверены независимо от отчётов 14.1–14.3. Одни успешные unit-тесты основанием вывода не являлись.

**К Draft Pull Request / code review готов проверенный локальный инкремент 14.2–14.4 с данным отчётом и раскрытыми ограничениями. Merge пока не рекомендован: полный native gate не зелёный.** Изменения не опубликованы и не слиты в main. Заключение означает готовность к code review, а не безусловную готовность производственного rollout или закрытие общих API/history gaps. Перед релизной приёмкой требуется живой LAN/multi-client walkthrough и отдельно согласованный общий contract alignment.
