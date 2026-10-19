# Validation report — Task 13.3.0

Дата: 2026-10-19. База: 4accd66 (13.2), ветка feature/project-detail-sections. Точный commit и полный список файлов находятся в source-receipt.json и changed-files.txt итогового пакета.

## Фактические проверки

| Проверка | Результат |
|---|---|
| Release build `dotnet build work/production/Task.sln -c Release` | PASS, 0 ошибок, 0 предупреждений в финальном прогоне |
| Полный desktop, финальный `dotnet test ...Task.Desktop.Tests.csproj -c Release --no-build` | 1750 PASS / 0 FAIL / 0 SKIP |
| Product endpoints `dotnet test ...Task.ServiceHosts.Tests.csproj -c Release --filter FullyQualifiedName~ProductEndpointsTests` | 254 PASS / 0 FAIL / 0 SKIP |
| Новые сценарии 13.3 в полном desktop | 70 PASS; входят в 1750, не суммировать повторно |
| Первый targeted gate файлов/контактов | 102 PASS |
| Host integration gate | 12 PASS |
| WPF + HTTP transport gate | 3 PASS |
| `npm run dashboard:order` и `dashboard:validate` | PASS; только PROD-04 evidence/note, без повышения status/progress |
| `git diff --check` | PASS |
| Package manifest / размеры / SHA-256 / ZIP CRC / точное содержимое ZIP | Машинный результат в соседнем `*.zip.validation.json`; проверяется после создания commit/архива |

Полный первый desktop-прогон: 1749 PASS / 1 FAIL / 0 SKIP. Существующий `TaskContextActionsUiTests.NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow` упал на строке 62 (keyboard focus). Его изолированный повтор: 1 PASS; повтор полного набора: 1750 PASS. Этот факт не скрыт и не объявлен доказанным baseline-дефектом. Production Tasks и сам тест не изменялись. Оба полных TRX и log сохранены в evidence. Первые сборки тестов также показывают существующие xUnit analyzer/ASPDEPR004 предупреждения; финальная incremental Release build решения не имеет предупреждений.

## Acceptance criteria

| AC | Evidence |
|---|---|
| 1: связанные контакт и компания, разрешённые имена и типы | LoadsContactAndCompanyNamesWithTypes; NativeProjectContacts: реальные WPF TextBlock/ListBox, automation name и два типа |
| 2–3: выбор контакта и компании, единый project_contact | AddUsesSameContractForBothTypes; ProjectContactsUseExisting...: реальный DesktopWorkApiClient/HTTP, body, тип, target и сильный If-Match |
| 4: удаляется только связь | RemovalChangesOnlyTheConfirmedProjectLink; транспортный POST/DELETE, нет PATCH/DELETE CRM; host сохраняет другие связи |
| 5: без Contact.Read нет запросов или раскрытия данных | MissingReadPermissionNeverLoadsLinksNamesOrPicker; host revocation очищает Items и Picker.Results |
| 6: смена проекта и поздние ответы | LateReadIsCancelled; LateMetadata...; LateMutation...; ProjectLinksHost_CancelsBothSections |
| 7: конфликт версии без ложного успеха | FailedWritesNeverClaimSuccess; ProjectLinksHost_OlderRead...; явный refresh, общий текст с файлами |
| 8: недоступная CRM-цель | UnavailableTargets...; TargetRevokedAfterPickerConfirmationNeverPosts; NavigationRevalidates... |

## Покрытые негативные и совместные сценарии

Нет Project.Read, Contact.Read, ObjectLink.Read, Project.Update, Contact.Update, ObjectLink.Create или ObjectLink.Delete; project forbidden; target 403/404/archived/trashed; duplicate; 412/version conflict; 401 из links/metadata/picker/write; server unavailable; offline и reconnect/refresh; scope/organization/project changes; collapse/reopen; session ended; dispose. HTTP ошибки Object Links и pagination дополнительно покрыты существующими тестами, включёнными в общий gate.

Общая CAS-версия проверена сквозной цепочкой файл → контакт → удаление файла → удаление контакта: версии 5→6→7→8→9. Остальные строки и editor draft сохраняются. Вторая параллельная запись блокируется; lease снимается после ошибки. Запоздалый read не понижает версию и требует обновления своего раздела.

Навигация обоих типов проверена через настоящий MainWindowViewModel / WorkHubViewModel в существующие CRM cards. Native WPF child process проверяет раскрытие, binding двух типов, automation name, клавиатурный фокус/переходы, подключение штатного picker, отзыв Contact.Update и повторное открытие. Скриншоты `project-contacts-loaded.png` и `project-contacts-picker.png` визуально проверены; доступны прокрутка и стандартные controls.

## Контракты и границы доказательства

`PostgresProductApiLinks` и `ProductApiContracts` проверены по текущему исходному коду: project_contact допускает contact/company; POST проверяет Contact.Update цели, Project.Update источника, project-level write, lifecycle, видимость, ETag и duplicate. Сервер, модели CRM, Project DTO, БД, migrations, глобальный поиск и sources не менялись.

Product endpoints gate использует существующий тестовый сервер; desktop transport tests используют контролируемый HTTP transport. Это не live Corporate API/PostgreSQL прогон. В этой задаче не запускались live стенд компании, новые real PostgreSQL интеграционные сценарии, ручная приёмка со всеми корпоративными ролями, Narrator или physical mixed-DPI. NAS не относится к новому CRM-разделу и отдельно не проверялся.

Итог: реализация и автоматизированный gate этапа 13.3 готовы к завершающему этапу 13.4. Готовность production deployment и завершение 13.4 не заявляются. Main не изменён; коммит создан в feature/project-detail-sections. Содержимое пакета считается проверенным только при validated=true в машинном package receipt.
