# Validation report — Task 14.1 / 1.0.0

Дата: 2026-10-22 (Europe/Minsk). Проверенный origin/main и worktree HEAD: `fa504bc01584547afc50398ba6c1735391c4173c`.

- PASS: три финальных ZIP совпали с SHA-256 sidecars; OpenAPI из ZIP совпал с baseline hash.
- PASS: evidence locators найдены в исходных файлах/ZIP members; для каждого записан SHA-256 и номер строки.
- PASS: targeted Release tests: 252 passed, 0 failed, 1 skipped; counters прочитаны из TRX, а не из предположений по exit code.
- PASS: git diff и staged diff проверенного worktree пусты; git diff --check успешен. Production/source/test tracked files не менялись.
- PASS: Markdown sections, UTF-8, отсутствие trailing whitespace, парность code fences и итоговый status проверены.
- PASS: manifest и SHA256SUMS сформированы и каждый hash перечитан/проверен. Manifest перечисляет все payload files; SHA256SUMS дополнительно покрывает manifest.json. Самореферентные checksums исключены.
- LIMITATION: PostgreSQL integration test RealPostgres_PatchIsAtomicDurableAndConcurrencySafe skipped; реальные PostgreSQL transition/authorization/notification tests, WPF visual acceptance и end-to-end возврат не выполнялись. Это не production release gate.
- LIMITATION: существующие тесты закрепляют отказ review→in_progress. PASS не означает соответствия нормативной state machine.
- DECISION: конфликт канонических требований и более позднего implementation prompt не разрешён; статус CONTRACT_CHANGE_REQUIRED относится к согласованию технической спецификации реализации, а не к доказанной необходимости новых HTTP полей.

Первый desktop namespace filter выбрал 0 тестов; он не засчитан, повторный корректный filter дал 132 passed (сохранённый desktop-status.trx). Существующие build warnings не исправлялись.

Воспроизведение проверок: из корня проверенного checkout выполнить следующие команды (реальные БД не используются):

```powershell
dotnet test work/production/tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c Release --filter "FullyQualifiedName~TransitionTask_|FullyQualifiedName~TransitionPolicy_|FullyQualifiedName~TaskPermissionAuthorizationTests"
dotnet test work/production/tests/Task.Tests/Task.Tests.csproj -c Release --filter "FullyQualifiedName~TaskAggregateTests|FullyQualifiedName~TaskWorkStatusTests|FullyQualifiedName~TaskUpdateTests|FullyQualifiedName~PermissionDecisionServiceTests"
dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --filter "FullyQualifiedName~TasksViewModelTests|FullyQualifiedName~DesktopTasksApiClientTests"
git diff --check
```
